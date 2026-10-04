using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ModUpdater
{
    public partial class Plugin
    {
        // ---- data ------------------------------------------------------------------------------

        /// <summary>A mod DLL found on disk (read without loading it).</summary>
        private class LocalMod { public string Guid, Name, Version, Path; public bool InPlugins, Disabled; }

        /// <summary>One mod as described by manifest.json (written by publish.ps1).</summary>
        private class RemoteMod { public string guid, name, version, description, notes; public string[] files; }

        private enum Status { UpToDate, UpdateAvailable, NotInstalled, LocalNewer, Rebuilt, Disabled, LocalOnly }

        private class Row
        {
            public string Name, Description, Notes, LocalVersion, RemoteVersion;
            public Status Status;
            public RemoteMod Remote;
            public LocalMod Local;
            /// <summary>Can the user switch this mod on/off? (Not the manager itself, and not loader plugins.)</summary>
            public bool CanToggle => Local != null && !Local.InPlugins && Local.Guid != Plugin.Guid;
        }

        private List<LocalMod> _local = new List<LocalMod>();
        private RemoteMod[] _remote = new RemoteMod[0];
        private readonly Dictionary<string, string> _remoteSha = new Dictionary<string, string>();
        private List<Row> _rows = new List<Row>();
        private string _statusLine = "Not checked yet.";
        private DateTime _lastRefresh = DateTime.MinValue;
        private bool _busy;
        private string _authNote = "";

        // ---- what's installed ------------------------------------------------------------------

        /// <summary>Find every BepInEx plugin DLL on disk and read its name/version from the [BepInPlugin] attribute.</summary>
        private void ScanLocal()
        {
            var found = new List<LocalMod>();
            Scan(_scriptsDir, SearchOption.TopDirectoryOnly, false, found);
            Scan(_pluginsDir, SearchOption.AllDirectories, true, found);
            _local = found;
        }

        private static void Scan(string dir, SearchOption option, bool inPlugins, List<LocalMod> into)
        {
            if (!Directory.Exists(dir)) return;
            // "X.dll.disabled" is a mod the user switched off (ScriptEngine only loads *.dll).
            foreach (string path in Directory.GetFiles(dir, "*.dll*", option))
            {
                bool disabled = path.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase);
                if (!disabled && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                LocalMod mod = ReadPlugin(path);
                if (mod == null) continue;
                mod.InPlugins = inPlugins;
                mod.Disabled = disabled;
                into.Add(mod);
            }
        }

        // Reading a DLL (and, for the hash, all of its bytes) is slow, and the same unchanged files get looked at every time the window
        // opens, a refresh runs, or you join a world. So remember the answer per file, and only redo it if the file actually changed.
        private class FileMemo { public long Length; public long Ticks; public LocalMod Mod; public string Sha; }
        private static readonly Dictionary<string, FileMemo> Memo = new Dictionary<string, FileMemo>();

        private static FileMemo MemoFor(string path)
        {
            var info = new FileInfo(path);
            if (!Memo.TryGetValue(path, out FileMemo memo) || memo.Length != info.Length || memo.Ticks != info.LastWriteTimeUtc.Ticks)
                Memo[path] = memo = new FileMemo { Length = info.Length, Ticks = info.LastWriteTimeUtc.Ticks };
            return memo;
        }

        private static LocalMod ReadPlugin(string path)
        {
            FileMemo memo;
            try { memo = MemoFor(path); } catch { return null; } // vanished or unreadable
            if (memo.Mod != null) return memo.Mod;

            try
            {
                // Read from memory with Cecil so we never lock or load the DLL.
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (ModuleDefinition module = ModuleDefinition.ReadModule(ms))
                {
                    foreach (TypeDefinition type in module.Types)
                        foreach (CustomAttribute attr in type.CustomAttributes)
                            if (attr.AttributeType.FullName == "BepInEx.BepInPlugin" && attr.ConstructorArguments.Count >= 3)
                                return memo.Mod = new LocalMod
                                {
                                    Guid = (string)attr.ConstructorArguments[0].Value,
                                    Name = (string)attr.ConstructorArguments[1].Value,
                                    Version = (string)attr.ConstructorArguments[2].Value,
                                    Path = path,
                                };
                }
            }
            catch { /* not a plugin DLL (a library, or unreadable): ignore */ }
            return null;
        }

        /// <summary>The Git hash of a local file, remembered until the file changes.</summary>
        private static string LocalSha(string path)
        {
            FileMemo memo = MemoFor(path);
            return memo.Sha ?? (memo.Sha = GitBlobSha(File.ReadAllBytes(path)));
        }

        // ---- what's on GitHub ------------------------------------------------------------------

        /// <param name="autoInstall">Install anything out of date straight away.</param>
        /// <param name="notify">Say in chat if updates are waiting (used once when joining a world).</param>
        private IEnumerator RefreshRoutine(bool autoInstall, bool notify = false)
        {
            if (_busy) yield break;
            if (!Configured)
            {
                _statusLine = "Not connected to GitHub yet. Add a read-only access token (see 'Connect to GitHub' below).";
                yield break;
            }

            _busy = true;
            _statusLine = "Checking GitHub...";
            bool ok = false;
            try
            {
                string listJson = null, manifestJson = null, error = null;
                yield return Get($"{ApiBase}?ref={_branch.Value}", "application/vnd.github+json", (t, b, e) => { listJson = t; error = e; });
                if (error != null)
                {
                    _statusLine = error.StartsWith("404")
                        ? $"Check failed (404): can't find {_owner.Value}/{_repo.Value} folder '{_folder.Value}' on branch '{_branch.Value}'. Check Owner/Repo/Folder/Branch, and that the token can see the repo."
                        : "Check failed: " + error;
                    yield break;
                }

                yield return Get($"{ApiBase}/manifest.json?ref={_branch.Value}", "application/vnd.github.raw+json", (t, b, e) => { manifestJson = t; error = e; });
                if (error != null)
                {
                    _statusLine = error.StartsWith("404")
                        ? "No manifest.json on GitHub yet: the owner needs to run publish.ps1, then commit and push."
                        : "Check failed: " + error;
                    yield break;
                }

                string problem = ApplyRefresh(listJson, manifestJson);
                if (problem != null) { _statusLine = problem; yield break; }
                ok = true;
            }
            finally
            {
                _busy = false;
            }

            if (!ok) yield break;

            int pending = _rows.Count(NeedsUpdate);
            string via = $" (via {_authNote})";
            _statusLine = pending == 0 ? $"Up to date. Checked {_lastRefresh:HH:mm:ss}{via}."
                                       : $"{pending} update(s) available. Checked {_lastRefresh:HH:mm:ss}{via}.";
            RequestPeerVersions();

            if (notify && pending > 0)
                Say($"{pending} mod update(s) available. Press {_hotkey.Value} to open the mod manager.");

            if (autoInstall && pending > 0)
                yield return InstallRoutine(_rows.Where(NeedsUpdate).Select(r => r.Remote).ToList());
        }

        /// <summary>
        /// Turn GitHub's two responses into our data. Returns null on success, or a message naming the step that
        /// failed (an iterator can't use try/catch around its yields, so this lives in its own method).
        /// </summary>
        private string ApplyRefresh(string listJson, string manifestJson)
        {
            string step = "reading the file list";
            try
            {
                var shas = new Dictionary<string, string>();
                foreach (JToken item in JArray.Parse(listJson))
                    if ((string)item["type"] == "file") shas[(string)item["name"]] = (string)item["sha"];

                step = "reading manifest.json";
                var mods = new List<RemoteMod>();
                foreach (JToken m in JObject.Parse(manifestJson)["mods"])
                {
                    mods.Add(new RemoteMod
                    {
                        guid = (string)m["guid"],
                        name = (string)m["name"],
                        version = (string)m["version"],
                        description = (string)m["description"],
                        notes = (string)m["notes"],
                        files = m["files"] != null ? m["files"].Select(f => (string)f).ToArray() : new string[0],
                    });
                }

                _remoteSha.Clear();
                foreach (var kv in shas) _remoteSha[kv.Key] = kv.Value;
                _remote = mods.ToArray();

                step = "scanning installed mods";
                ScanLocal();
                step = "comparing versions";
                BuildRows();

                _lastRefresh = DateTime.Now;
                _authNote = AuthSource;
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Refresh failed while {step}: {ex}");
                return $"Check failed while {step}: {ex.GetType().Name}: {ex.Message} (details in BepInEx\\LogOutput.log)";
            }
        }

        private void BuildRows()
        {
            var rows = new List<Row>();
            foreach (RemoteMod r in _remote)
            {
                LocalMod local = _local.FirstOrDefault(l => l.Guid == r.guid);
                var row = new Row
                {
                    Name = r.name, Description = r.description, Notes = r.notes, RemoteVersion = r.version,
                    Remote = r, Local = local, LocalVersion = local?.Version,
                };

                if (local == null) row.Status = Status.NotInstalled;
                else if (local.Disabled) row.Status = Status.Disabled;
                else
                {
                    int cmp = CompareVersions(local.Version, r.version);
                    if (cmp < 0) row.Status = Status.UpdateAvailable;
                    else if (cmp > 0) row.Status = Status.LocalNewer;
                    else row.Status = FilesMatch(r) ? Status.UpToDate : Status.Rebuilt;
                }
                rows.Add(row);
            }

            // Mods we have that aren't in the repo (the loader itself, ScriptEngine, a mod still in development...).
            foreach (LocalMod l in _local)
                if (!_remote.Any(r => r.guid == l.Guid))
                    rows.Add(new Row { Name = l.Name, LocalVersion = l.Version, Local = l, Status = l.Disabled ? Status.Disabled : Status.LocalOnly });

            _rows = rows;
        }

        private bool NeedsUpdate(Row r) =>
            r.Status == Status.NotInstalled || r.Status == Status.UpdateAvailable ||
            (r.Status == Status.Rebuilt && !_developerMode.Value);

        private bool FilesMatch(RemoteMod r)
        {
            foreach (string file in r.files ?? new string[0])
            {
                string path = Path.Combine(_scriptsDir, file);
                if (!_remoteSha.TryGetValue(file, out string sha)) continue;
                if (!File.Exists(path) || LocalSha(path) != sha) return false;
            }
            return true;
        }

        private static int CompareVersions(string a, string b)
        {
            // (System.Version spelled out: inside Plugin, "Version" is our own version string constant.)
            if (System.Version.TryParse(a, out System.Version va) && System.Version.TryParse(b, out System.Version vb)) return va.CompareTo(vb);
            return string.CompareOrdinal(a, b);
        }

        /// <summary>Same hash Git uses for a file ("blob" SHA-1), so we can compare against GitHub's listing without downloading.</summary>
        private static string GitBlobSha(byte[] content)
        {
            byte[] header = Encoding.ASCII.GetBytes("blob " + content.Length + "\0");
            var all = new byte[header.Length + content.Length];
            Buffer.BlockCopy(header, 0, all, 0, header.Length);
            Buffer.BlockCopy(content, 0, all, header.Length, content.Length);
            using (SHA1 sha = SHA1.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(all)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---- installing ------------------------------------------------------------------------

        private IEnumerator InstallRoutine(List<RemoteMod> mods)
        {
            if (_busy || mods.Count == 0) yield break;
            _busy = true;
            var installed = new List<string>();
            var toReload = new List<ModFile>();
            string failure = null;
            try
            {
                foreach (RemoteMod mod in mods)
                {
                    _statusLine = $"Downloading {mod.name}...";
                    foreach (string file in mod.files ?? new string[0])
                    {
                        string localPath = Path.Combine(_scriptsDir, file);
                        if (_remoteSha.TryGetValue(file, out string sha) && File.Exists(localPath) &&
                            LocalSha(localPath) == sha) continue;

                        byte[] data = null; string error = null;
                        yield return Get($"{ApiBase}/{file}?ref={_branch.Value}", "application/vnd.github.raw+json", (t, b, e) => { data = b; error = e; });
                        if (error != null || data == null) { failure = $"{file}: {error}"; yield break; }

                        // Write to a temp file then move, so ScriptEngine never sees a half-written DLL.
                        string tmp = localPath + ".part";
                        File.WriteAllBytes(tmp, data);
                        if (File.Exists(localPath)) File.Delete(localPath);
                        File.Move(tmp, localPath);
                    }
                    installed.Add(mod.name);
                    ModFile modFile = FileOf(mod);
                    if (modFile != null) toReload.Add(modFile);
                }
            }
            finally
            {
                _busy = false;
                if (failure != null) _statusLine = "Download failed: " + failure;
            }

            if (failure != null) yield break;

            // Finish all our own bookkeeping first: the reload is last because, if the manager itself was
            // updated, it destroys this running copy (a fresh copy takes over a moment later).
            ScanLocal();
            BuildRows();
            BroadcastVersions();
            _statusLine = $"Updated: {string.Join(", ", installed)} (reloading)";
            Say(_statusLine);

            // Reload only the mods we just changed (the others keep running untouched).
            if (!ReloadMods(toReload))
            {
                _statusLine = $"Updated: {string.Join(", ", installed)} - press F6 to reload";
                Say("Press F6 to reload the mods (ScriptEngine not detected).");
            }
        }

        /// <summary>Switch a mod on or off by renaming X.dll to X.dll.disabled (ScriptEngine only loads *.dll), then reload.</summary>
        private void SetEnabled(Row row, bool enable)
        {
            if (_busy || row.Local == null || !row.CanToggle) return;
            try
            {
                string path = row.Local.Path;
                string target = enable ? path.Substring(0, path.Length - ".disabled".Length) : path + ".disabled";
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
            }
            catch (Exception e)
            {
                _statusLine = $"Couldn't {(enable ? "enable" : "disable")} {row.Name}: {e.Message}";
                return;
            }

            // Finish our own bookkeeping first, then (un)load just this mod. Nothing else is reloaded.
            ScanLocal();
            BuildRows();
            BroadcastVersions();
            _statusLine = $"{row.Name} {(enable ? "enabled" : "disabled")}";

            string dll = enable ? row.Local.Path.Substring(0, row.Local.Path.Length - ".disabled".Length) : row.Local.Path;
            var file = new ModFile { Guid = row.Local.Guid, Path = dll };
            bool ok = enable ? ReloadMods(new List<ModFile> { file }) : UnloadMods(new List<ModFile> { file });
            if (!ok) _statusLine += " - press F6 to apply";
        }

        private void InstallOne(Row row) { if (row.Remote != null) StartCoroutine(InstallRoutine(new List<RemoteMod> { row.Remote })); }

        private void InstallAll() => StartCoroutine(InstallRoutine(_rows.Where(NeedsUpdate).Select(r => r.Remote).ToList()));
    }
}
