using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DHack.Shared;
using HarmonyLib;
using UnityEngine;

// Its one piece reloads in place (Shared/PieceReload.cs: never unregistered in between, placed roadsters get the new code): safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace RocketRoadster
{
    /// <summary>
    /// Rocket Roadster. A white wedge of a two-seat sports car after the one revealed in October 2026, with cold-gas thrusters: it hops
    /// over things, flies for a few seconds on a tank of compressed air, barrel rolls and flips in the air, and lands on its wheels. Built
    /// with the hammer at a forge.
    ///
    /// The driving (springs, motor, steering, seats) is Shared/GroundVehicle.cs, as for FourWheelers and MountainBikes. Here: Plugin.cs
    /// (settings, setup, the keys), Things.cs (the piece), Model.cs and Facets.cs (its look), Roadster.cs (thrusters, air control, doors,
    /// tricks), Jets.cs (plumes and sounds), RoadsterHud.cs (the gauge).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.rocketroadster";
        public const string Name = "RocketRoadster";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ServerSettings Synced;   // how fast, how high and what it costs is the server's in multiplayer

        /// <summary>What a roadster costs before anyone changes it: iron for the frame, surtling cores for the motor, crystal for the glass.</summary>
        internal const string DefaultCost = "Iron:20, SurtlingCore:6, Crystal:6, FineWood:10";

        internal static ConfigEntry<float> Cruise, TopSpeed, Reverse, Accel, Coast, TurnRate, Grip;
        internal static ConfigEntry<float> BoostSpeed, BoostPush, LiftStrength, HopSpeed, TankSeconds, RefillSeconds, RollRate, PitchRate, SelfLevel, Downforce;
        internal static ConfigEntry<bool> Help, TrickCallouts, LandingGuard, UprightCamera;
        internal static ConfigEntry<float> EngineVolume, ThrusterVolume, RamDamage;
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

            ConfigEntry<float> F(string section, string key, float value, string text, float min, float max) =>
                Synced.Add(Config.Bind(section, key, value, new ConfigDescription(text + " [Synced with Server]", new AcceptableValueRange<float>(min, max))));

            Cruise = F("1 - Driving", "CruiseSpeed", 22f, "How fast it goes holding W (metres per second; a walking player does about 5).", 3f, 40f);
            TopSpeed = F("1 - Driving", "FullSpeed", 34f, "How fast it goes holding Shift+W with the air tank empty (metres per second).", 3f, 60f);
            Reverse = F("1 - Driving", "ReverseSpeed", 7f, "How fast it backs up holding S once stopped (metres per second).", 1f, 15f);
            Accel = F("1 - Driving", "Acceleration", 14f, "How quickly it picks up speed (metres per second, each second). Braking is stronger.", 1f, 30f);
            Coast = F("1 - Driving", "Coasting", 3f, "How quickly it slows down with nothing held (metres per second, each second).", 0.5f, 10f);
            TurnRate = F("1 - Driving", "TurnRate", 100f, "The fastest it can turn (degrees per second), at low speed. Faster, it turns on a wider circle.", 20f, 200f);
            Grip = F("1 - Driving", "Grip", 7f, "How much it holds its line in a turn instead of sliding sideways (higher grips more).", 0.5f, 10f);

            BoostSpeed = F("2 - Thrusters", "BoostSpeed", 58f, "Its top speed with the rear thrusters firing (Shift+W with air in the tank), metres per second.", 10f, 120f);
            BoostPush = F("2 - Thrusters", "BoostPush", 26f, "How hard the rear thrusters push (metres per second, each second), on the ground and in the air.", 0f, 80f);
            LiftStrength = F("2 - Thrusters", "LiftStrength", 1.6f, "How hard the lift thrusters push (Space), in times its weight: over 1 climbs, under 1 only slows a fall.", 0f, 4f);
            HopSpeed = F("2 - Thrusters", "HopSpeed", 11f, "How hard a tap of Space throws it up off the ground (metres per second).", 0f, 30f);
            TankSeconds = F("2 - Thrusters", "TankSeconds", 8f, "How many seconds of lift a full air tank gives (boosting uses a little more than half as much).", 1f, 120f);
            RefillSeconds = F("2 - Thrusters", "RefillSeconds", 14f, "How many seconds an empty tank takes to fill again with the wheels on the ground.", 1f, 300f);
            RollRate = F("2 - Thrusters", "RollRate", 420f, "How fast A and D roll it in the air (degrees per second: 360 is a barrel roll a second).", 30f, 1080f);
            PitchRate = F("2 - Thrusters", "PitchRate", 320f, "How fast W and S flip it in the air (degrees per second).", 30f, 1080f);
            SelfLevel = F("2 - Thrusters", "SelfLevel", 4f, "How quickly it turns back to wheels-down in the air when you hold no direction (0 never).", 0f, 10f);
            Downforce = F("2 - Thrusters", "Downforce", 1.4f, "How hard its fans press it onto the road at speed, in times its weight (over 1 lets it drive round a loop).", 0f, 4f);

            LandingGuard = Synced.Add(Config.Bind("2 - Thrusters", "LandingGuard", true, new ConfigDescription("Near the ground, the attitude jets take over and turn it wheels-down in time, so it always lands on its wheels. [Synced with Server]")));
            RamDamage = F("4 - Combat", "RamDamage", 12f, "Blunt damage to an enemy you run over, for each metre per second over 3. Players and tamed animals are never hurt; 0 turns it off.", 0f, 60f);
            EngineVolume = Config.Bind("5 - Look", "MotorVolume", 1f, new ConfigDescription("How loud the electric motor's whine is (0 is silent; the game's own volume settings apply on top).", new AcceptableValueRange<float>(0f, 2f)));
            ThrusterVolume = Config.Bind("5 - Look", "ThrusterVolume", 1f, new ConfigDescription("How loud the thrusters are (0 is silent).", new AcceptableValueRange<float>(0f, 2f)));
            Help = Config.Bind("5 - Look", "ShowControls", true, "Show the controls the first time you get in during a session.");
            TrickCallouts = Config.Bind("5 - Look", "TrickCallouts", true, "Say which rolls and flips you landed.");
            UprightCamera = Config.Bind("5 - Look", "UprightCamera", true, "Keep the camera level while the roadster rolls and flips (it follows the car from above and behind, horizon straight).");
            Cost = Synced.Bind("3 - Building", "Cost", DefaultCost, "What a roadster costs to build with the hammer (near a forge). " + Costs.Help);
            Cost.SettingChanged += (s, e) => Things.ApplyCost();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == ServerSettings.ServerMod.Missing;
            Config.SaveOnConfigSet = true;
            Config.Save();

            GroundVehicle.Spec = new VehicleSpec
            {
                Id = "dh_roadster",
                Wheels = Model.Wheels,
                Front = 2,
                WheelRadius = Model.WheelRadius,
                Travel = 0.26f,
                Mass = 900f,
                Cruise = () => Cruise.Value, Top = () => TopSpeed.Value, Reverse = () => Reverse.Value, Accel = () => Accel.Value,
                Coast = () => Coast.Value, TurnRate = () => TurnRate.Value, Grip = () => Grip.Value,
                Stamina = null,
                Upright = 1.6f,
                RamDamage = () => RamDamage.Value, RamCenter = new Vector3(0f, 0.6f, 2.8f), RamSize = new Vector3(2.2f, 1.1f, 1.0f),
                MinRadius = 5.5f, LateralAccel = 20f, Engine = false, EngineVolume = () => EngineVolume.Value,
                Pose = new System.Collections.Generic.Dictionary<string, float>
                {
                    { "lean", -4f }, { "head", 2f }, { "kneeOut", 0.12f }, { "elbowOut", 0.35f }, { "steerLean", 1f }, { "toeDown", 0.1f },
                },
                Command = "roadster",
                RideKey = "$roadster_ride", TooFarKey = "$roadster_too_far", TooHeavyKey = "$roadster_too_heavy", PassengerKey = "$roadster_passenger",
                HelpKey = "$roadster_help", ShowHelp = () => Help.Value,
                PaintKey = "$roadster_paint", RightKey = "$roadster_right", DustSize = 1.4f, UseRange = 4f,
                PassengerBeside = true,
                Paints = new[]
                {
                    new Color(0.7f, 0.72f, 0.75f), new Color(0.07f, 0.07f, 0.08f), new Color(0.62f, 0.06f, 0.05f), new Color(0.6f, 0.62f, 0.66f),
                    new Color(0.12f, 0.22f, 0.5f), new Color(0.72f, 0.5f, 0.26f),
                },
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
            RoadsterHud.Tick();
            VehicleTools.Update(Name);
            SafeLanding.Tick();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$roadster_server_missing");
            }
        }

        private void LateUpdate() => VehicleRider.Late();   // (after the animation: the riders' pose on top of it)

        private void OnDestroy()
        {
            Roadster.LiftHeld = false;
            VehicleRider.Reset();
            VehicleTools.Unregister();
            _harmony?.UnpatchSelf();
            RoadsterHud.Destroy();
            Things.Unregister();
            Synced?.Dispose();
            Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    /// <summary>
    /// Whoever leaves a roadster high up (it broke under them, the game took them off for some reason of its own) comes down without
    /// fall damage: until their feet touch something, the height they fell from is kept at where they are (docs/modding-pitfalls.md,
    /// "An attached player lets go on any input").
    /// </summary>
    internal static class SafeLanding
    {
        private static readonly AccessTools.FieldRef<Character, float> MaxAir = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static bool _wasAboard;
        private static float _until;

        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (p == null) { _wasAboard = false; return; }
            bool aboard = Roadster.Aboard(p) != null;
            if (_wasAboard && !aboard && !p.IsOnGround()) _until = Time.time + 20f;
            _wasAboard = aboard;
            if (Time.time >= _until) return;
            if (p.IsOnGround() || p.IsSwimming() || p.IsDead()) { _until = 0f; return; }
            MaxAir(p) = p.transform.position.y;
        }
    }

    /// <summary>
    /// The keys, while the local player drives a roadster or sits in one. Jump is the lift thrusters, never "get off" (the game lets go of
    /// a vehicle on jump, attack or dodge); in the air nothing but E gets the driver out, and E only once it is near the ground. A
    /// passenger lets go of their seat on any key (the game's chair), so in the air their keys do nothing.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls
    {
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold,
                                   ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool dodge)
        {
            if (__instance != Player.m_localPlayer) return;
            if (GroundVehicle.Riding(__instance) is Roadster car)
            {
                bool input = false;
                try { input = TakeInput(__instance); } catch (Exception) { }
                Roadster.LiftHeld = input && (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));
                jump = false;
                if (car.Airborne) { attack = attackHold = secondaryAttack = secondaryAttackHold = dodge = false; }
                return;
            }
            Roadster.LiftHeld = false;
            Roadster seat = Roadster.PassengerIn(__instance);
            if (seat != null && seat.Airborne)
            {
                movedir = Vector3.zero;
                attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = dodge = false;
            }
        }
    }

    /// <summary>
    /// The camera stays level while you are in a roadster. The game tilts a seated player's camera with their body and swings it round
    /// their head, so a barrel roll turned the whole picture over. Here it looks where the mouse points, horizon straight, from behind a
    /// spot above the middle of the car (world up, not the car's), as far out as the player zoomed (at least far enough to see the car).
    /// First person is left to the game.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "GetCameraPosition")]
    internal static class GameCamera_GetCameraPosition
    {
        private static readonly AccessTools.FieldRef<GameCamera, float> Distance = AccessTools.FieldRefAccess<GameCamera, float>("m_distance");
        private static readonly System.Reflection.MethodInfo Collide = AccessTools.Method(typeof(GameCamera), "CollideRay2");

        private static void Postfix(GameCamera __instance, ref Vector3 pos, ref Quaternion rot)
        {
            if (!Plugin.UprightCamera.Value) return;
            Player p = Player.m_localPlayer;
            Roadster car = p != null ? Roadster.Aboard(p) : null;
            if (car == null || p.m_eye == null) return;
            float d = Distance(__instance);
            if (d <= 0.05f) return;
            d = Mathf.Max(d, 6f);
            Vector3 look = p.m_eye.forward;   // (the game keeps this level: mouse yaw and pitch only)
            Vector3 pivot = car.transform.TransformPoint(new Vector3(0f, 0.6f, -0.3f)) + Vector3.up * 1.6f;
            Vector3 end = pivot - look * d;
            if (Collide != null)
            {
                try { var args = new object[] { pivot, pivot, end }; Collide.Invoke(__instance, args); end = (Vector3)args[2]; }
                catch (Exception) { }
            }
            pos = end;
            rot = Quaternion.LookRotation(look, Vector3.up);
        }
    }

    /// <summary>E in the air doesn't open the door: "land first". (Anything else that takes the driver out, the game's own reasons, still does.)</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    internal static class Player_StopDoodadControl
    {
        private static bool Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || __instance.IsDead()) return true;
            if (!(GroundVehicle.Riding(__instance) is Roadster car) || !car.HighUp) return true;
            if (!(ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse"))) return true;
            __instance.Message(MessageHud.MessageType.Center, "$roadster_land_first");
            return false;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the roadster is registered before a saved one is loaded (docs/modding-pitfalls.md).
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
