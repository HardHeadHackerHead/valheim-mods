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
        public const string Guid = "com.quad.buildfromchests";
        public const string OldGuid = "com.dhack.buildfromchests"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "BuildFromChests";
        public const string Version = "1.4.1";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowHaveCounts;
        internal static ConfigEntry<float> Radius;
        internal static BepInEx.Logging.ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced; // settings the server decides in multiplayer

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            ShowHaveCounts = Config.Bind("Display", "ShowHaveCounts", true,
                "In the build menu, show how many of each material you HAVE (inventory + chests) next to how many you need.");
            Enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            Radius = Synced.Add(Config.Bind("General", "Radius", 20f,
                new ConfigDescription("How far (in meters) from YOU a chest can be and still be used while building (also for BuildOrders: building its ghosts, " +
                    "hold E to build all, and its fetch key). Raise it to build far from your storehouse: 60 reaches across a big base. Chests only count " +
                    "while their area is loaded around you (about 100 m and more). Takes effect at once. In multiplayer the server's value applies.",
                    new AcceptableValueRange<float>(2f, 150f))));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            // On a hot reload, chests already exist and won't run Awake again, so pick them up here.
            foreach (Container c in FindObjectsOfType<Container>()) ChestScanner.Register(c);

            Api.Publish(); // lets BuildOrders fetch materials from the chests around you
            Logger.LogInfo($"{Name} {Version} loaded");

            // Awake with a player already in the world means this was a hot reload (F6 / ScriptEngine).
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | {(Enabled.Value ? "on" : "off")}, radius {Radius.Value:0.#}m", Talker.Type.Normal);
        }

        private void Update() => Synced?.Update(); // notices joining and leaving a server, for the settings it decides

        // ScriptEngine destroys the old plugin instance on reload; remove our patches so they don't stack.
        private void OnDestroy()
        {
            Api.Withdraw();
            _harmony?.UnpatchSelf();
            Synced?.Dispose();
            ChestScanner.Suspend = false;
            ChestScanner.Consuming = false;
            HaveLabel.DestroyAll(); // the "you have" numbers live in the game's UI, so remove them ourselves
        }
    }
}
