using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace ModUpdater
{
    /// <summary>
    /// Press a hotkey to pull the latest mod DLLs from a (private) GitHub repo into BepInEx\scripts,
    /// then ask ScriptEngine to hot-reload them. Only files whose content changed are downloaded.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.modupdater";
        public const string Name = "ModUpdater";
        public const string Version = "1.0.0";

        private const string ScriptEngineGuid = "com.bepis.bepinex.scriptengine";

        private ConfigEntry<string> _owner, _repo, _branch, _folder, _token;
        private ConfigEntry<KeyboardShortcut> _hotkey;
        private ConfigEntry<bool> _checkOnStart;

        private bool _busy;
        private string _scriptsDir;

        [Serializable] private class Entry { public string name; public string sha; public string type; }
        [Serializable] private class Listing { public Entry[] items; }

        private void Awake()
        {
            _owner = Config.Bind("Repo", "Owner", "", "GitHub user or org that owns the repo, e.g. 'davidhacker'.");
            _repo = Config.Bind("Repo", "Repo", "", "Repository name.");
            _branch = Config.Bind("Repo", "Branch", "main", "Branch to pull from.");
            _folder = Config.Bind("Repo", "Folder", "dist", "Folder in the repo that holds the built mod .dll files.");
            _token = Config.Bind("Repo", "Token", "",
                "GitHub fine-grained personal access token with READ-ONLY 'Contents' access to this one repo. Keep it private.");
            _hotkey = Config.Bind("General", "Hotkey", new KeyboardShortcut(KeyCode.F7), "Press this in-game to check for mod updates.");
            _checkOnStart = Config.Bind("General", "CheckOnStart", false, "Also check for updates automatically when the game starts.");

            _scriptsDir = Path.Combine(Paths.BepInExRootPath, "scripts");
            Logger.LogInfo($"{Name} {Version} ready. Press {_hotkey.Value} in-game to update mods.");

            if (_checkOnStart.Value) StartCoroutine(CheckForUpdates(silentIfCurrent: true));
        }

        private void Update()
        {
            if (_hotkey.Value.IsDown() && !_busy) StartCoroutine(CheckForUpdates(silentIfCurrent: false));
        }

        private IEnumerator CheckForUpdates(bool silentIfCurrent)
        {
            if (string.IsNullOrEmpty(_owner.Value) || string.IsNullOrEmpty(_repo.Value) || string.IsNullOrEmpty(_token.Value))
            {
                Say("Not set up yet: fill in Owner, Repo and Token in BepInEx\\config\\" + Guid + ".cfg");
                yield break;
            }

            _busy = true;
            try
            {
                Directory.CreateDirectory(_scriptsDir);

                // 1. List the repo folder.
                string baseUrl = $"https://api.github.com/repos/{_owner.Value}/{_repo.Value}/contents/{_folder.Value}";
                string listJson = null, error = null;
                yield return Get($"{baseUrl}?ref={_branch.Value}", "application/vnd.github+json", (text, bytes, err) => { listJson = text; error = err; });
                if (error != null) { Say("Update check failed: " + error); yield break; }

                Listing listing = JsonUtility.FromJson<Listing>("{\"items\":" + listJson + "}");
                var updated = new List<string>();

                // 2. Download only files that differ from what's installed.
                foreach (Entry entry in listing.items)
                {
                    if (entry.type != "file" || !IsModFile(entry.name)) continue;

                    string localPath = Path.Combine(_scriptsDir, entry.name);
                    if (File.Exists(localPath) && GitBlobSha(File.ReadAllBytes(localPath)) == entry.sha) continue;

                    byte[] data = null;
                    yield return Get($"{baseUrl}/{entry.name}?ref={_branch.Value}", "application/vnd.github.raw+json", (text, bytes, err) => { data = bytes; error = err; });
                    if (error != null || data == null) { Say($"Download of {entry.name} failed: {error}"); yield break; }

                    // Write to a temp file then move, so ScriptEngine never sees a half-written DLL.
                    string tmp = localPath + ".part";
                    File.WriteAllBytes(tmp, data);
                    if (File.Exists(localPath)) File.Delete(localPath);
                    File.Move(tmp, localPath);
                    updated.Add(entry.name);
                }

                // 3. Report, and hot-reload if anything changed.
                if (updated.Count == 0)
                {
                    if (!silentIfCurrent) Say("Mods are up to date.");
                    yield break;
                }

                Say($"Updated {updated.Count} file(s): {string.Join(", ", updated)}");
                if (!ReloadScripts()) Say("Press F6 to reload them (ScriptEngine not detected).");
            }
            finally
            {
                _busy = false;
            }
        }

        private static bool IsModFile(string name) =>
            name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);

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

        /// <summary>One authenticated GET against the GitHub API.</summary>
        private IEnumerator Get(string url, string accept, Action<string, byte[], string> done)
        {
            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Authorization", "Bearer " + _token.Value);
                req.SetRequestHeader("Accept", accept);
                req.SetRequestHeader("User-Agent", Name);
                req.SetRequestHeader("X-GitHub-Api-Version", "2022-11-28");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    done(null, null, $"{req.responseCode} {req.error}".Trim());
                else
                    done(req.downloadHandler.text, req.downloadHandler.data, null);
            }
        }

        /// <summary>Ask ScriptEngine to reload scripts now (the same thing F6 does).</summary>
        private bool ReloadScripts()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(ScriptEngineGuid, out var info) || info.Instance == null) return false;
                var reload = AccessTools.Method(info.Instance.GetType(), "ReloadPlugins");
                if (reload == null) return false;
                reload.Invoke(info.Instance, null);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogWarning("Could not trigger ScriptEngine reload: " + e.Message);
                return false;
            }
        }

        /// <summary>Log it, and show it as a local chat line + top-left popup when in a world.</summary>
        private void Say(string message)
        {
            Logger.LogInfo(message);
            if (Chat.instance != null)
            {
                Chat.instance.AddString("[Updater]", message, Talker.Type.Normal);
                AccessTools.Field(typeof(Chat), "m_hideTimer").SetValue(Chat.instance, 0f);
                Chat.instance.m_chatWindow.gameObject.SetActive(true);
            }
        }
    }
}
