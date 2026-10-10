using System.Collections;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// Moving a mod to a new GUID without losing anything (in 2026-10 every mod went from com.dhack.* to com.quad.*). A plugin calls
    /// FromOldGuid(this, OldGuid) first thing in Awake, before binding any setting:
    ///
    ///   * Settings: BepInEx names a mod's config file after its GUID, so the old one (com.dhack.x.cfg, and any com.dhack.x.*.cfg beside it)
    ///     is copied to the new name the first time, and read in. The old file stays where it was, as a backup.
    ///   * Two copies: with a new GUID, BepInEx no longer sees an old copy as the same mod and would run both (everything twice: crafts
    ///     paid twice, chests counted twice). If the old GUID is loaded too, this copy stands down and says which file to delete.
    ///
    /// Saved game data doesn't depend on the GUID (it lives in ZDOs, item and player data and files under our own names), so it needs nothing.
    /// Shared source (mods/Shared/Migration.cs), compiled into every mod by mods/Directory.Build.props.
    /// </summary>
    internal static class Migration
    {
        public static void FromOldGuid(BaseUnityPlugin plugin, string oldGuid)
        {
            string newGuid = MetadataHelper.GetMetadata(plugin)?.GUID;
            if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid) || oldGuid == newGuid) return;
            var log = BepInEx.Logging.Logger.CreateLogSource(newGuid);
            try
            {
                bool movedMain = false;
                if (Directory.Exists(Paths.ConfigPath))
                    foreach (string old in Directory.GetFiles(Paths.ConfigPath, oldGuid + ".*"))
                    {
                        string name = Path.GetFileName(old);
                        if (!name.EndsWith(".cfg") && !name.EndsWith(".json") && !name.EndsWith(".txt")) continue;
                        string target = Path.Combine(Paths.ConfigPath, newGuid + name.Substring(oldGuid.Length));
                        if (File.Exists(target)) continue;
                        File.Copy(old, target);
                        log.LogInfo($"Settings moved over from {name} (the mod's id is now {newGuid}; the old file is kept as a backup)");
                        if (name == oldGuid + ".cfg") movedMain = true;
                    }
                if (movedMain) plugin.Config.Reload();
            }
            catch (System.Exception e) { log.LogWarning($"Couldn't move the settings over from {oldGuid}.cfg: {e.Message}"); }
            finally { BepInEx.Logging.Logger.Sources.Remove(log); }

            plugin.StartCoroutine(StandDownIfTwin(plugin, oldGuid));
        }

        private static IEnumerator StandDownIfTwin(BaseUnityPlugin plugin, string oldGuid)
        {
            yield return null; // (ScriptEngine and Claude Tools' dev loader start their mods a frame after loading them)
            yield return null;
            if (plugin == null || !Chainloader.PluginInfos.TryGetValue(oldGuid, out PluginInfo old) || old.Instance == null || old.Instance == plugin) yield break;
            string name = MetadataHelper.GetMetadata(plugin)?.Name ?? oldGuid;
            string where = string.IsNullOrEmpty(old.Location) ? "an older copy" : old.Location;
            string text = $"{name}: two copies are installed, an old one ({where}) and this one. Both would run and do everything twice, so " +
                          $"this one stands down. Delete the old copy, then restart the game.";
            Debug.LogError("[" + name + "] " + text);
            Object.Destroy(plugin); // its OnDestroy undoes everything it did
            var notice = new GameObject("Migration_TwoCopies_" + name);
            Object.DontDestroyOnLoad(notice);
            notice.AddComponent<Notice>().Text = text;
        }

        /// <summary>Says it on screen once the player is in a world (nobody reads the log).</summary>
        private sealed class Notice : MonoBehaviour
        {
            public string Text;
            private float _next;

            private void Update()
            {
                if (Time.unscaledTime < _next || Player.m_localPlayer == null || MessageHud.instance == null) return;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Text);
                _next = Time.unscaledTime + 60f; // again every minute until it's fixed
            }
        }
    }
}
