using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// It patches methods, adds a skill (its backup is on the character) and one item, the skateboard, which goes in through ItemReload.cs
// (kept registered across a reload, the new copy's put in its place) with its recipe through Shared/Content.cs: safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Skateboards
{
    /// <summary>
    /// Quad's Skateboards: skate Valheim the way the old skateboarding games played. Make a skateboard at the workbench, hold it in your
    /// hand and click to drop it and hop on. W pushes (Shift pushes harder), S brakes, A and D carve. Hold Jump to crouch and let go to
    /// ollie (longer held, higher). In the air: A and D spin, Attack (left click) is a flip trick and Block (right click, held) a grab, each
    /// picked by the move key held (none, W, S, A, D). Land on the top of a beam, a wall or the edge of a floor to grind it (A and D keep
    /// your balance); hold Crouch on the ground to manual (W and S balance). Tricks chain into a combo whose points bank when you roll away
    /// clean; land crooked, or fall off a rail, and you bail and lose it. E steps off. Tricks train a new Skateboarding skill.
    /// Movement is each player's own game's job, so only the players who want it need the mod; with it, they also see each other skate.
    ///
    /// Split across files: Plugin.cs (settings, setup), Board.cs (the skateboard item and its model), Controls.cs (the player's input, and
    /// test driving), Skate.cs (the riding: rolling, airs, grinds, manuals, bails), Rails.cs (finding what can be ground), Tricks.cs (the
    /// tricks, the combo and the score), Look.cs (the stance, the board under the feet, the tricks' poses, for every player in sight),
    /// Share.cs (the pose shared with other players), ScoreHud.cs (the combo and balance meter on screen), Sounds.cs (the board's sounds),
    /// Skill.cs (the Skateboarding skill), Tools.cs (Claude Tools commands), GameUi.cs and ItemReload.cs (templates).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.skateboards";
        public const string Name = "Skateboards";
        public const string Version = "0.1.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // how fast, how high and what it costs is the server's in multiplayer

        // the server's
        internal static ConfigEntry<float> PushSpeed, SprintSpeed, TopSpeed, PushAccel, Brake, TurnRate, PushStamina, OllieSpeed, OllieCharge,
            SpinSpeed, SpinSpeedAt100, FlipSeconds, FlipSecondsAt100, LandAngle, SloppyAngle, Wobble, WobbleAt100, SafeDrop, SkillGain, PushBonusAt100;
        internal static ConfigEntry<bool> SpinAssist, ShowKeys, CameraSteer;
        internal static ConfigEntry<string> BoardCost, RailCost, LedgeCost, KickerCost, PipeCost;
        // each player's own
        internal static ConfigEntry<bool> Goofy, CameraFollow, Sounds, ShowScore, Hints;
        internal static ConfigEntry<float> SoundVolume;

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Config.SaveOnConfigSet = false;

            PushSpeed = Synced.Add(Config.Bind("1 - Riding", "PushSpeed", 8f, new ConfigDescription("How fast pushing with W gets you on flat ground (metres per second; running is about 7). Slopes and ramps take you faster. [Synced with Server]", new AcceptableValueRange<float>(2f, 20f))));
            SprintSpeed = Synced.Add(Config.Bind("1 - Riding", "SprintPushSpeed", 11f, new ConfigDescription("How fast pushing with Shift+W gets you (metres per second); it tires you more. [Synced with Server]", new AcceptableValueRange<float>(2f, 30f))));
            TopSpeed = Synced.Add(Config.Bind("1 - Riding", "TopSpeed", 24f, new ConfigDescription("The fastest a board ever rolls, downhill or off a ramp (metres per second). [Synced with Server]", new AcceptableValueRange<float>(5f, 60f))));
            PushAccel = Synced.Add(Config.Bind("1 - Riding", "PushAcceleration", 6f, new ConfigDescription("How quickly pushing picks up speed (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(1f, 30f))));
            Brake = Synced.Add(Config.Bind("1 - Riding", "Braking", 10f, new ConfigDescription("How quickly S slows you down (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(1f, 40f))));
            TurnRate = Synced.Add(Config.Bind("1 - Riding", "TurnRate", 150f, new ConfigDescription("The fastest A and D carve (degrees per second), at low speed; faster, you turn on a wider circle. [Synced with Server]", new AcceptableValueRange<float>(30f, 360f))));
            PushStamina = Synced.Add(Config.Bind("1 - Riding", "PushStamina", 2f, new ConfigDescription("Stamina each second of pushing (a harder push with Shift costs two and a half times that). Rolling, carving and tricks are free. 0: pushing never tires you. [Synced with Server]", new AcceptableValueRange<float>(0f, 20f))));
            PushBonusAt100 = Synced.Add(Config.Bind("1 - Riding", "PushSpeedPercentAtSkill100", 20f, new ConfigDescription("How much faster pushing gets you at Skateboarding skill 100 (percent; less in between). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            // (0.1.1 renamed the settings whose defaults it changed, so the easier ones reach configs saved by 0.1.0)
            OllieSpeed = Synced.Add(Config.Bind("2 - Tricks", "OlliePop", 7.5f, new ConfigDescription("How fast an ollie pops you up (metres per second; about 1.4 m high, higher held, and with the skill). [Synced with Server]", new AcceptableValueRange<float>(2f, 20f))));
            OllieCharge = Synced.Add(Config.Bind("2 - Tricks", "OllieChargePercent", 35f, new ConfigDescription("How much faster an ollie pops when Jump is held down a moment before letting go (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SpinSpeed = Synced.Add(Config.Bind("2 - Tricks", "SpinSpeed", 420f, new ConfigDescription("How fast A and D spin you in the air at Skateboarding skill 0 (degrees per second). [Synced with Server]", new AcceptableValueRange<float>(90f, 1440f))));
            SpinSpeedAt100 = Synced.Add(Config.Bind("2 - Tricks", "SpinSpeedAtSkill100", 620f, new ConfigDescription("The same at Skateboarding skill 100. [Synced with Server]", new AcceptableValueRange<float>(90f, 1440f))));
            FlipSeconds = Synced.Add(Config.Bind("2 - Tricks", "FlipTime", 0.4f, new ConfigDescription("How long a flip trick takes at Skateboarding skill 0 (seconds). Land with less than a third of it left and it counts (Sloppy); before that you bail. [Synced with Server]", new AcceptableValueRange<float>(0.15f, 2f))));
            FlipSecondsAt100 = Synced.Add(Config.Bind("2 - Tricks", "FlipTimeAtSkill100", 0.3f, new ConfigDescription("The same at Skateboarding skill 100. [Synced with Server]", new AcceptableValueRange<float>(0.15f, 2f))));
            SpinAssist = Synced.Add(Config.Bind("2 - Tricks", "SpinAssist", true, "Let go of A and D in the air and the spin settles on the nearest half turn by itself, so you land straight. Off: the board stays where you left it. [Synced with Server]"));
            LandAngle = Synced.Add(Config.Bind("2 - Tricks", "BailAngle", 75f, new ConfigDescription("How far the board may point off the way you are going when you land (degrees, nose or tail first) before it is a bail. [Synced with Server]", new AcceptableValueRange<float>(10f, 89f))));
            SloppyAngle = Synced.Add(Config.Bind("2 - Tricks", "SloppyAngle", 40f, new ConfigDescription("From this far off (degrees) a landing is Sloppy: the air's tricks count three quarters. Within 12 degrees it is Perfect (half again). [Synced with Server]", new AcceptableValueRange<float>(5f, 89f))));
            Wobble = Synced.Add(Config.Bind("2 - Tricks", "BalanceDifficulty", 0.6f, new ConfigDescription("How hard grinds and manuals pull you off balance at Skateboarding skill 0 (1: normal, 0: never). [Synced with Server]", new AcceptableValueRange<float>(0f, 5f))));
            WobbleAt100 = Synced.Add(Config.Bind("2 - Tricks", "BalanceDifficultyAtSkill100", 0.35f, new ConfigDescription("The same at Skateboarding skill 100. [Synced with Server]", new AcceptableValueRange<float>(0f, 5f))));
            SafeDrop = Synced.Add(Config.Bind("2 - Tricks", "SafeDrop", 8f, new ConfigDescription("How far you can drop on the board without fall damage, landed or bailed (metres; the game's own falls hurt from 4). Further than that, the rest hurts as the game's falls do. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SkillGain = Synced.Add(Config.Bind("2 - Tricks", "SkillGain", 1f, new ConfigDescription("How fast the Skateboarding skill rises (1 = like the game's skills). [Synced with Server]", new AcceptableValueRange<float>(0f, 10f))));
            BoardCost = Synced.Bind("3 - Making", "SkateboardCost", "Wood:8, Resin:4, LeatherScraps:2, Flint:2", "What a skateboard takes to make at the workbench. " + DHack.Shared.Costs.Help);
            BoardCost.SettingChanged += (o, e) => Board.ApplyCost();
            RailCost = Synced.Bind("3 - Making", "GrindRailCost", "Wood:6, Resin:2", "What a Grind Rail takes to build with the hammer (at a workbench). " + DHack.Shared.Costs.Help);
            LedgeCost = Synced.Bind("3 - Making", "SkateLedgeCost", "Wood:8, Flint:2", "What a Skate Ledge takes to build. " + DHack.Shared.Costs.Help);
            KickerCost = Synced.Bind("3 - Making", "KickerCost", "Wood:10", "What a Kicker (a launch ramp) takes to build. " + DHack.Shared.Costs.Help);
            PipeCost = Synced.Bind("3 - Making", "QuarterPipeCost", "Wood:24, Resin:4", "What a Quarter Pipe takes to build. " + DHack.Shared.Costs.Help);
            foreach (var cost in new[] { RailCost, LedgeCost, KickerCost, PipeCost }) cost.SettingChanged += (o, e) => Park.ApplyCosts();

            Goofy = Config.Bind("4 - Feel", "Goofy", false, "Ride goofy (right foot forward) instead of regular (left foot forward).");
            CameraSteer = Config.Bind("4 - Feel", "CameraSteer", true, "Holding W, the board heads where the camera looks (turn the camera right round and it brakes, spins round and pushes off that way). A and D still carve. Off: only A and D steer.");
            CameraFollow = Config.Bind("4 - Feel", "CameraFollow", true, "The camera swings round behind you as you roll, a moment after you last moved the mouse.");
            ShowKeys = Config.Bind("4 - Feel", "ShowKeys", true, "A small guide to the keys at the bottom right while you skate, for what you can do now (on the ground, in the air, on a rail, in a manual).");
            ShowScore = Config.Bind("4 - Feel", "ShowScore", true, "The tricks of your combo, its points and the balance meter on screen while you skate.");
            Hints = Config.Bind("4 - Feel", "Hints", true, "A short hint the first time you do each thing in a session.");
            Sounds = Config.Bind("4 - Feel", "Sounds", true, "The board's sounds: wheels rolling, the pop of an ollie, landings, grinds (others hear yours too).");
            SoundVolume = Config.Bind("4 - Feel", "SoundVolume", 0.8f, new ConfigDescription("How loud the board's sounds are (on top of the game's sound effects volume).", new AcceptableValueRange<float>(0f, 1f)));

            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Skill.Create();   // (after the translations; also puts it on the player already in the world after a hot reload)
            Board.Register();                                    // a hot reload while in a world
            Park.Register();
            ItemReload.Repoint(Board.Current);                   // boards made by the copy this replaces
            Board.Refresh();                                     // and the ones the game gave their own copy of the data (the give command)
            Skill.Gain = SkillGain.Value;
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Skill.Gain = SkillGain.Value;   // (the server's, live)
            Skate.Update();
            ScoreHud.Tick();
            Tools.Update();
        }

        private void LateUpdate() => Look.Late();   // after the animation has posed the players

        private void OnDestroy()
        {
            Skate.Stop(Player.m_localPlayer, "unloaded", quiet: true);
            Share.Reset();   // (after: "off the board" on the player's ZDO while it is still ours)
            Look.Reset();
            ScoreHud.Destroy();
            global::Skateboards.Sounds.Reset();
            Controls.Reset();
            Tricks.Reset();
            Skill.Reset();
            Synced?.Dispose();
            _harmony?.UnpatchSelf();
            Board.Unregister();
            Park.Unregister();
            Tools.Unregister();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the skateboard is registered before a saved one is loaded.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() { Board.Register(); Park.Register(); } }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() { Board.Register(); Park.Register(); } }

    // The game swaps in another ObjectDB's item lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB { private static void Postfix() { Board.Register(); Park.Register(); } }
}
