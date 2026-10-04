using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BuildFromChests
{
    /// <summary>
    /// Building with the hammer (and other build tools) uses materials from chests near you, not just your inventory.
    /// Companion to CraftFromChests, which does the same for crafting stations.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.buildfromchests";
        public const string Name = "BuildFromChests";
        public const string Version = "1.1.1";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHaveCounts;
        internal static ConfigEntry<float> Radius;
        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            ShowHaveCounts = Config.Bind("Display", "ShowHaveCounts", true,
                "In the build menu, show how many of each material you HAVE (inventory + chests) next to how many you need.");
            Enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            Radius = Config.Bind("General", "Radius", 20f,
                "How far (in meters) from YOU a chest can be and still be used while building.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            // On a hot reload, chests already exist and won't run Awake again, so pick them up here.
            foreach (Container c in FindObjectsOfType<Container>()) ChestScanner.Register(c);

            Logger.LogInfo($"{Name} {Version} loaded");

            // Awake with a player already in the world means this was a hot reload (F6 / ScriptEngine).
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | {(Enabled.Value ? "on" : "off")}, radius {Radius.Value:0.#}m", Talker.Type.Normal);
        }

        // ScriptEngine destroys the old plugin instance on reload; remove our patches so they don't stack.
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            ChestScanner.Suspend = false;
            ChestScanner.Consuming = false;
            HaveLabel.DestroyAll(); // the "you have" numbers live in the game's UI, so remove them ourselves
        }
    }
}
