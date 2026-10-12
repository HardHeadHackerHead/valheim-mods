using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DHack.Shared;
using HarmonyLib;
using UnityEngine;

// Its one piece reloads in place (Shared/PieceReload.cs: never unregistered in between, placed mountain bikes get the new code): safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace MountainBikes
{
    /// <summary>
    /// Mountain Bikes. A two-wheeled trail bike for one rider, built with the hammer on dry land. Hold W to pedal, Shift+W to sprint (it
    /// tires you out; coasting and braking are free), S to brake and back up, A and D to steer and lean, Jump or E to get off. Press E on it to
    /// climb on. It stands up by itself when parked.
    ///
    /// The driving itself (springs on each wheel, the motor, handing the controls to the rider) is Shared/GroundVehicle.cs, which
    /// MountainBikes uses too. Here: Plugin.cs (settings, setup), Things.cs (the piece), Model.cs (its look).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.mountainbikes";
        public const string Name = "MountainBikes";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ServerSettings Synced;   // how fast, how nimble and what it costs is the server's in multiplayer

        /// <summary>What a mountain bike costs before anyone changes it.</summary>
        internal const string DefaultCost = "FineWood:6, Bronze:4, LeatherScraps:4, Resin:6";

        internal static ConfigEntry<float> Cruise, TopSpeed, Reverse, Accel, Coast, TurnRate, Grip, PedalStamina;
        internal static ConfigEntry<bool> Help;
        internal static ConfigEntry<float> RamDamage;
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

            Cruise = Synced.Add(Config.Bind("1 - Driving", "CruiseSpeed", 9.5f, new ConfigDescription("How fast it goes pedalling with W (metres per second; a walking player does about 5, running about 7). [Synced with Server]", new AcceptableValueRange<float>(3f, 30f))));
            TopSpeed = Synced.Add(Config.Bind("1 - Driving", "FullSpeed", 15f, new ConfigDescription("How fast it goes sprinting with Shift+W, which tires you out faster (metres per second). [Synced with Server]", new AcceptableValueRange<float>(3f, 40f))));
            Reverse = Synced.Add(Config.Bind("1 - Driving", "ReverseSpeed", 2f, new ConfigDescription("How fast it backs up holding S once stopped (metres per second). [Synced with Server]", new AcceptableValueRange<float>(1f, 10f))));
            Accel = Synced.Add(Config.Bind("1 - Driving", "Acceleration", 4.5f, new ConfigDescription("How quickly it picks up speed (metres per second, each second). Braking is stronger. [Synced with Server]", new AcceptableValueRange<float>(1f, 20f))));
            Coast = Synced.Add(Config.Bind("1 - Driving", "Coasting", 0.8f, new ConfigDescription("How quickly it slows down with nothing held (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            TurnRate = Synced.Add(Config.Bind("1 - Driving", "TurnRate", 120f, new ConfigDescription("The fastest it can turn (degrees per second), at low speed. Faster, it turns on a wider circle. [Synced with Server]", new AcceptableValueRange<float>(20f, 200f))));
            Grip = Synced.Add(Config.Bind("1 - Driving", "Grip", 10f, new ConfigDescription("How much it holds its line in a turn instead of sliding sideways (higher grips more). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            PedalStamina = Synced.Add(Config.Bind("1 - Driving", "PedalStamina", 3f, new ConfigDescription("How much stamina pedalling costs each second (sprinting costs three times that). Coasting and braking are free. At 0 it never tires you. [Synced with Server]", new AcceptableValueRange<float>(0f, 20f))));
            RamDamage = Synced.Add(Config.Bind("4 - Combat", "RamDamage", 3f, new ConfigDescription("Blunt damage to an enemy you ride into, for each metre per second over 3. Players and tamed animals are never hurt; 0 turns it off. [Synced with Server]", new AcceptableValueRange<float>(0f, 50f))));
            Help = Config.Bind("2 - Look", "ShowControls", true, "Show the controls the first time you get on in a session.");
            Cost = Synced.Bind("3 - Building", "Cost", DefaultCost, "What a mountain bike costs to build with the hammer (at a workbench). " + Costs.Help);
            Cost.SettingChanged += (s, e) => Things.ApplyCost();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == ServerSettings.ServerMod.Missing;
            Config.SaveOnConfigSet = true;
            Config.Save();

            GroundVehicle.Spec = new VehicleSpec
            {
                Id = "dh_bike",
                Wheels = Model.Wheels,
                Front = 1,
                WheelRadius = Model.WheelRadius,
                Travel = 0.2f,
                Mass = 120f,
                Cruise = () => Cruise.Value, Top = () => TopSpeed.Value, Reverse = () => Reverse.Value, Accel = () => Accel.Value,
                Coast = () => Coast.Value, TurnRate = () => TurnRate.Value, Grip = () => Grip.Value,
                Stamina = () => PedalStamina.Value,
                Upright = 4f,
                RamDamage = () => RamDamage.Value, RamCenter = new Vector3(0f, 0.7f, 0.75f), RamSize = new Vector3(0.6f, 1.0f, 0.9f),
                MinRadius = 2.0f, LateralAccel = 11f, LeansWithTurn = true,
                RideKey = "$mountainbikes_ride", TooFarKey = "$mountainbikes_too_far", TooHeavyKey = "$mountainbikes_too_heavy",
                HelpKey = "$mountainbikes_help", ShowHelp = () => Help.Value,
                PaintKey = "$mountainbikes_paint", RightKey = "$mountainbikes_right", DustSize = 0.55f,
                Paints = new[] { new Color(0.18f, 0.5f, 0.25f), new Color(0.66f, 0.10f, 0.08f), new Color(0.15f, 0.3f, 0.65f), new Color(0.85f, 0.4f, 0.1f), new Color(0.08f, 0.08f, 0.09f), new Color(0.9f, 0.88f, 0.8f), new Color(0.5f, 0.2f, 0.6f) },
            };

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register(); // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            VehicleHud.Tick();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$mountainbikes_server_missing");
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            VehicleHud.Destroy();
            Things.Unregister();
            Synced?.Dispose();
            Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the mountain bike is registered before a saved one is loaded (docs/modding-pitfalls.md).
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
