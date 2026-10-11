using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// Its one piece reloads in place (Shared/PieceReload.cs: never unregistered in between, placed jet skis get the new code): safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace JetSkis
{
    /// <summary>
    /// Jet Skis. A small, fast boat for one, built with the hammer on the water like the raft. A surtling core's fire pushes it instead of the
    /// wind: hold W to go, Shift+W for full speed, S to reverse, A and D to steer (it leans into the turn), Jump to get off. Press E on it from
    /// the water or the shore to climb on.
    ///
    /// It is the game's raft underneath (floating, saving, wear, ownership in multiplayer all stay the game's), with a new body and its own
    /// motor and steering. Split across files: Plugin.cs (settings, setup), Things.cs (the piece), Model.cs (its look), JetSki.cs (what one
    /// does: the motor, steering, the rider climbing on, the lean), Patches.cs (the game's boat code handing over to it), Rider.cs (how the rider sits on it), JetHud.cs (its gauge), GameUi.cs (the game's look for it), Look.cs (materials),
    /// Tools.cs (a Claude Tools command for testing).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.jetskis";
        public const string Name = "JetSkis";
        public const string Version = "0.2.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // how fast and how nimble is the server's in multiplayer

        internal static ConfigEntry<float> Cruise, TopSpeed, Reverse, Accel, Coast, TurnRate, Grip, EngineVolume;
        internal static ConfigEntry<bool> Lean, Help;

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Config.SaveOnConfigSet = false;

            Cruise = Synced.Add(Config.Bind("1 - Driving", "CruiseSpeed", 11f, new ConfigDescription("How fast it goes holding W (metres per second; a longship under full sail does about 10). [Synced with Server]", new AcceptableValueRange<float>(3f, 30f))));
            TopSpeed = Synced.Add(Config.Bind("1 - Driving", "FullSpeed", 17f, new ConfigDescription("How fast it goes holding Shift+W (metres per second). [Synced with Server]", new AcceptableValueRange<float>(3f, 40f))));
            Reverse = Synced.Add(Config.Bind("1 - Driving", "ReverseSpeed", 4f, new ConfigDescription("How fast it backs up holding S (metres per second). [Synced with Server]", new AcceptableValueRange<float>(1f, 10f))));
            Accel = Synced.Add(Config.Bind("1 - Driving", "Acceleration", 5f, new ConfigDescription("How quickly it picks up speed (metres per second, each second). Braking is a little stronger. [Synced with Server]", new AcceptableValueRange<float>(1f, 20f))));
            Coast = Synced.Add(Config.Bind("1 - Driving", "Coasting", 2.5f, new ConfigDescription("How quickly it slows down with nothing held (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            TurnRate = Synced.Add(Config.Bind("1 - Driving", "TurnRate", 95f, new ConfigDescription("How fast it turns at full lock (degrees per second). It turns slower when barely moving. [Synced with Server]", new AcceptableValueRange<float>(20f, 200f))));
            Grip = Synced.Add(Config.Bind("1 - Driving", "Grip", 3f, new ConfigDescription("How much it holds its line in a turn instead of sliding sideways (higher grips more). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            Lean = Config.Bind("2 - Look", "LeanIntoTurns", true, "The jet ski (and you on it) leans into turns and lifts its nose at speed, your head turns where you steer and you bob over the waves.");
            Help = Config.Bind("2 - Look", "ShowControls", true, "Show the controls the first time you get on in a session.");
            EngineVolume = Config.Bind("2 - Look", "EngineVolume", 1f, new ConfigDescription("How loud the engine is (0 is silent, 1 as made; the game's own volume settings apply on top).", new AcceptableValueRange<float>(0f, 2f)));
            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register(); // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Tools.Update();
        }

        private void LateUpdate() => Rider.Late();   // (after the animation: the riders' pose on top of it)

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Tools.Unregister();
            Rider.Reset();
            JetHud.Destroy();
            Things.Unregister();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the jet ski is registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB
    {
        private static void Postfix() => Things.Register();
    }
}
