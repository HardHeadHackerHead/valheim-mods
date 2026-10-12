using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DHack.Shared;
using HarmonyLib;
using UnityEngine;

// Its one piece reloads in place (Shared/PieceReload.cs: never unregistered in between, placed four wheelers get the new code): safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace FourWheelers
{
    /// <summary>
    /// Four Wheelers. A four-wheeled all-terrain runabout for a rider and a passenger, built with the hammer on dry land. A surtling core
    /// drives it: hold W to go, Shift+W for full speed, S to brake and back up, A and D to steer, Jump or E to get off. Press E on it to
    /// climb on. A rack at the back holds a few things.
    ///
    /// The driving itself (springs on each wheel, the motor, handing the controls to the rider) is Shared/GroundVehicle.cs, which
    /// MountainBikes uses too. Here: Plugin.cs (settings, setup), Things.cs (the piece), Model.cs (its look).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.fourwheelers";
        public const string Name = "FourWheelers";
        public const string Version = "0.1.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ServerSettings Synced;   // how fast, how nimble and what it costs is the server's in multiplayer

        /// <summary>What a four wheeler costs before anyone changes it.</summary>
        internal const string DefaultCost = "FineWood:10, Iron:10, SurtlingCore:2, LeatherScraps:8";

        internal static ConfigEntry<float> Cruise, TopSpeed, Reverse, Accel, Coast, TurnRate, Grip;
        internal static ConfigEntry<bool> Help;
        internal static ConfigEntry<float> EngineVolume, RamDamage;
        internal static ConfigEntry<string> Cost;
        private static bool _tellNoServer;   // the server hasn't the mod: say so once the player is in the world

        private Harmony _harmony;

        private void Awake()
        {
            Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new ServerSettings(Guid, Config, Logger);
            Config.SaveOnConfigSet = false;

            Cruise = Synced.Add(Config.Bind("1 - Driving", "CruiseSpeed", 17f, new ConfigDescription("How fast it goes holding W (metres per second; a walking player does about 5). [Synced with Server]", new AcceptableValueRange<float>(3f, 30f))));
            TopSpeed = Synced.Add(Config.Bind("1 - Driving", "FullSpeed", 28f, new ConfigDescription("How fast it goes holding Shift+W (metres per second). [Synced with Server]", new AcceptableValueRange<float>(3f, 40f))));
            Reverse = Synced.Add(Config.Bind("1 - Driving", "ReverseSpeed", 5f, new ConfigDescription("How fast it backs up holding S once stopped (metres per second). [Synced with Server]", new AcceptableValueRange<float>(1f, 10f))));
            Accel = Synced.Add(Config.Bind("1 - Driving", "Acceleration", 9f, new ConfigDescription("How quickly it picks up speed (metres per second, each second). Braking is stronger. [Synced with Server]", new AcceptableValueRange<float>(1f, 20f))));
            Coast = Synced.Add(Config.Bind("1 - Driving", "Coasting", 3f, new ConfigDescription("How quickly it slows down with nothing held (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            TurnRate = Synced.Add(Config.Bind("1 - Driving", "TurnRate", 110f, new ConfigDescription("The fastest it can turn (degrees per second), at low speed. Faster, it turns on a wider circle. [Synced with Server]", new AcceptableValueRange<float>(20f, 200f))));
            Grip = Synced.Add(Config.Bind("1 - Driving", "Grip", 6f, new ConfigDescription("How much it holds its line in a turn instead of sliding sideways (higher grips more). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            EngineVolume = Config.Bind("2 - Look", "EngineVolume", 1f, new ConfigDescription("How loud the engine is (0 is silent, 1 as made; the game's own volume settings apply on top).", new AcceptableValueRange<float>(0f, 2f)));
            RamDamage = Synced.Add(Config.Bind("4 - Combat", "RamDamage", 10f, new ConfigDescription("Blunt damage to an enemy you run over, for each metre per second over 3 (so 12 m/s does 90, full speed 250). Players and tamed animals are never hurt; 0 turns it off. [Synced with Server]", new AcceptableValueRange<float>(0f, 50f))));
            Help = Config.Bind("2 - Look", "ShowControls", true, "Show the controls the first time you get on in a session.");
            Cost = Synced.Bind("3 - Building", "Cost", DefaultCost, "What a four wheeler costs to build with the hammer (at a workbench). " + Costs.Help);
            Cost.SettingChanged += (s, e) => Things.ApplyCost();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == ServerSettings.ServerMod.Missing;
            Config.SaveOnConfigSet = true;
            Config.Save();

            GroundVehicle.Spec = new VehicleSpec
            {
                Id = "dh_atv",
                Wheels = Model.Wheels,
                Front = 2,
                WheelRadius = Model.WheelRadius,
                Travel = 0.3f,
                Mass = 450f,
                Cruise = () => Cruise.Value, Top = () => TopSpeed.Value, Reverse = () => Reverse.Value, Accel = () => Accel.Value,
                Coast = () => Coast.Value, TurnRate = () => TurnRate.Value, Grip = () => Grip.Value,
                Stamina = null,
                Upright = 1.2f,
                RamDamage = () => RamDamage.Value, RamCenter = new Vector3(0f, 0.65f, 0.95f), RamSize = new Vector3(1.6f, 1.1f, 1.3f),
                MinRadius = 3.6f, LateralAccel = 15f, Engine = true, EngineVolume = () => EngineVolume.Value,
                Pose = new System.Collections.Generic.Dictionary<string, float> { { "lean", 22f }, { "head", 14f }, { "kneeOut", 0.55f }, { "steerLean", 6f } }, Command = "atv",
                RideKey = "$fourwheelers_ride", TooFarKey = "$fourwheelers_too_far", TooHeavyKey = "$fourwheelers_too_heavy", PassengerKey = "$fourwheelers_passenger",
                HelpKey = "$fourwheelers_help", ShowHelp = () => Help.Value,
                PaintKey = "$fourwheelers_paint", RightKey = "$fourwheelers_right", DustSize = 1f,
                Paints = new[] { new Color(0.66f, 0.10f, 0.08f), new Color(0.15f, 0.3f, 0.65f), new Color(0.18f, 0.5f, 0.25f), new Color(0.85f, 0.65f, 0.1f), new Color(0.08f, 0.08f, 0.09f), new Color(0.9f, 0.88f, 0.8f), new Color(0.85f, 0.4f, 0.1f) },
            };

            VehicleRider.Init(GroundVehicle.Spec);
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register(); // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            VehicleHud.Tick();
            VehicleTools.Update(Name);
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$fourwheelers_server_missing");
            }
        }

        private void LateUpdate() => VehicleRider.Late();   // (after the animation: the riders' pose on top of it)

        private void OnDestroy()
        {
            VehicleRider.Reset();
            VehicleTools.Unregister();
            _harmony?.UnpatchSelf();
            VehicleHud.Destroy();
            Things.Unregister();
            Synced?.Dispose();
            Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the four wheeler is registered before a saved one is loaded (docs/modding-pitfalls.md).
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
