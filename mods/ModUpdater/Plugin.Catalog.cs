using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using UnityEngine;

namespace ModUpdater
{
    public partial class Plugin
    {
        // ---- data ------------------------------------------------------------------------------

        /// <summary>A mod DLL found on disk (read without loading it).</summary>
        private class LocalMod { public string Guid, Name, Version, Path; public bool InPlugins; }

        // These mirror manifest.json (written by publish.ps1); JsonUtility needs public fields.
        [Serializable] private class RemoteMod { public string guid, name, version, description; public string[] files; }
        [Serializable] private class Manifest { public RemoteMod[] mods; }
        [Serializable] private class Entry { public string name, sha, type; }
        [Serializable] private class Listing { public Entry[] items; }

        private enum Status { UpToDate, UpdateAvailable, NotInstalled, LocalNewer, Rebuilt, LocalOnly }

        private class Row
        {
            public string Name, Description, LocalVersion, RemoteVersion;
            public Status Status;
            public RemoteMod Remote;
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
            foreach (string path in Directory.GetFiles(dir, "*.dll", option))
            {
                LocalMod mod = ReadPlugin(path);
                if (mod == null) continue;
                mod.InPlugins = inPlugins;
                into.Add(mod);
            }
        }

        private static LocalMod ReadPlugin(string path)
        {
            try
            {
                // Read from memory with Cecil so we never lock or load the DLL.
                using (var ms = new MemoryStream(File.ReadAllBytes(path)))
                using (ModuleDefinition module = ModuleDefinition.ReadModule(ms))
                {
                    foreach (TypeDefinition type in module.Types)
                        foreach (CustomAttribute attr in type.CustomAttributes)
                            if (attr.AttributeType.FullName == "BepInEx.BepInPlugin" && attr.ConstructorArguments.Count >= 3)
                                return new LocalMod
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

        // ---- what's on GitHub ------------------------------------------------------------------

        private IEnumerator RefreshRoutine(bool autoInstall)
        {
            if (_busy) yield break;
            if (!Configured)
            {
                _statusLine = "Not set up: in BepInEx\\config\\" + Guid + ".cfg fill in Owner and Repo, and either a Token or log in with `gh auth login`.";
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

                Listing listing = JsonUtility.FromJson<Listing>("{\"items\":" + listJson + "}");
                _remoteSha.Clear();
                foreach (Entry e in listing.items) if (e.type == "file") _remoteSha[e.name] = e.sha;

                _remote = JsonUtility.FromJson<Manifest>(manifestJson)?.mods ?? new RemoteMod[0];
                _lastRefresh = DateTime.Now;
                _authNote = AuthSource;
                ScanLocal();
                BuildRows();
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

            if (autoInstall && pending > 0)
                yield return InstallRoutine(_rows.Where(NeedsUpdate).Select(r => r.Remote).ToList());
        }

        private void BuildRows()
        {
            var rows = new List<Row>();
            foreach (RemoteMod r in _remote)
            {
                LocalMod local = _local.FirstOrDefault(l => l.Guid == r.guid);
                var row = new Row { Name = r.name, Description = r.description, RemoteVersion = r.version, Remote = r, LocalVersion = local?.Version };

                if (local == null) row.Status = Status.NotInstalled;
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
                    rows.Add(new Row { Name = l.Name, LocalVersion = l.Version, Status = Status.LocalOnly });

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
                if (!File.Exists(path) || GitBlobSha(File.ReadAllBytes(path)) != sha) return false;
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
                            GitBlobSha(File.ReadAllBytes(localPath)) == sha) continue;

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

            if (!ReloadScripts())
            {
                _statusLine = $"Updated: {string.Join(", ", installed)} - press F6 to reload";
                Say("Press F6 to reload the mods (ScriptEngine not detected).");
            }
        }

        private void InstallOne(Row row) { if (row.Remote != null) StartCoroutine(InstallRoutine(new List<RemoteMod> { row.Remote })); }

        private void InstallAll() => StartCoroutine(InstallRoutine(_rows.Where(NeedsUpdate).Select(r => r.Remote).ToList()));
    }
}
