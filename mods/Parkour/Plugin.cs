using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

// It only patches methods and adds a skill (nothing saved in the world, no prefabs): safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Parkour
{
    /// <summary>
    /// Parkour: run along walls, kick off them, run up them, pull yourself up onto ledges and roll as you land. Each move costs stamina like
    /// sprinting, jumping and dodging do (more in heavy armour), and trains a new Parkour skill that makes the moves longer, higher, cheaper
    /// and (from level 50) rolls by itself on a hard landing. A blue bar over the stamina bar shows what is left of a move.
    /// Movement is each player's own game's job, so only the players who want it need the mod; the others see them move and lean.
    ///
    /// Split across files: Plugin.cs (settings, setup), Controls.cs (the player's input, and test driving), Walls.cs (finding walls and
    /// ledges), Moves.cs (the wall moves), Roll.cs (the landing roll), Look.cs (the lean and the hands on a ledge), Feel.cs (sounds and the camera), Share.cs (the ledge pose shared with other players), Bar.cs (the blue bar),
    /// Skill.cs (the Parkour skill), Compat.cs (other climbing mods), Tools.cs (Claude Tools commands).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.parkour";
        public const string Name = "Parkour";
        public const string Version = "0.1.1";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // each player's own
        internal static ConfigEntry<bool> Enabled, WallRun, WallJump, WallClimb, LedgeGrab, PullUp, Shimmy, Slide, LandingRoll, AutoRoll, ShowBar, Hints, Sounds, CameraTilt, FovKick;
        internal static ConfigEntry<float> ShimmyMin, ShimmyMax, SlideMin, SlideMax, SlideBoost, LandingSlideMax, SlideJumpBoost, SlideJumpHeight;
        // the server's
        internal static ConfigEntry<float> WallRunMin, WallRunMax, HangMin, HangMax, WallRunSpeedBonus, ClimbMin, ClimbMax, WetShorter, SkillGain;
        internal static ConfigEntry<int> KicksAtSkill0, KickEveryLevels, KicksMax, AutoRollLevel;
        internal static ConfigEntry<float> RollWindowMin, RollWindowMax, RollSafeMin, RollSafeMax, RollDamageShare;
        internal static ConfigEntry<float> WallRunCost, WallRunCostPerSecond, ClimbCost, HangCostPerSecond, MantleCost, SkillCostCut;
        internal static DHack.Shared.ServerSettings Synced;   // how strong and how dear the moves are is the server's in multiplayer

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Config.SaveOnConfigSet = false;

            Enabled = Config.Bind("1 - Moves", "Enabled", true, "Parkour on or off (all the moves).");
            WallRun = Config.Bind("1 - Moves", "WallRun", true, "Wall run: sprint, and jump at a wall at an angle to run along it.");
            WallJump = Config.Bind("1 - Moves", "WallJump", true, "Wall jump: jump while on a wall, or just after touching one, to kick off it.");
            WallClimb = Config.Bind("1 - Moves", "WallClimb", true, "Wall climb: sprint straight at a wall and jump near it to run up it.");
            LedgeGrab = Config.Bind("1 - Moves", "LedgeGrab", true, "Ledge grab: hold forward in the air to catch an edge in front of you, then forward or jump to pull up, crouch or back to let go.");
            PullUp = Config.Bind("1 - Moves", "PullUp", true, "Pull up onto a ledge you hang from (forward or jump). Off: jump kicks off instead (handy with Just Vault, whose key vaults up from the ledge).");
            Shimmy = Config.Bind("1 - Moves", "Shimmy", true, "Hanging from a ledge, left or right moves you along it.");
            Slide = Config.Bind("1 - Moves", "Slide", true, "Sprinting, press crouch to slide, and hold it to keep sliding; jump to leap out of the slide with its speed.");
            LandingRoll = Config.Bind("1 - Moves", "LandingRoll", true, "Landing roll: press crouch just before you land to roll (the game's dodge roll) and take less fall damage.");
            AutoRoll = Config.Bind("1 - Moves", "AutoRoll", true, "From the Parkour level the server sets (50), roll by yourself when you land moving forward from a fall that would hurt.");
            ShowBar = Config.Bind("1 - Moves", "ParkourBar", true, "A blue bar over the stamina bar during a move: wall run time left, wall jumps left.");
            Hints = Config.Bind("1 - Moves", "Hints", true, "A short hint the first time you do each move in a session.");
            Sounds = Config.Bind("4 - Feel", "Sounds", true, "The game's own footsteps on walls and ledges (for the surface: wood, stone, metal, ground), and an effort as you pull up. Others hear them too.");
            CameraTilt = Config.Bind("4 - Feel", "CameraTilt", true, "The camera leans a few degrees toward the wall while you wall run. Turn it off if camera motion makes you feel sick.");
            FovKick = Config.Bind("4 - Feel", "FovKick", true, "The view widens a little for a moment as you kick off a wall or pull up. Turn it off if camera motion makes you feel sick.");

            WallRunMin = Synced.Add(Config.Bind("2 - Strengths", "WallRunSeconds", 2f, new ConfigDescription("How long a wall run lasts at Parkour skill 0 (seconds): the blue bar. [Synced with Server]", new AcceptableValueRange<float>(0.3f, 5f))));
            WallRunMax = Synced.Add(Config.Bind("2 - Strengths", "WallRunSecondsAtSkill100", 4f, new ConfigDescription("How long a wall run lasts at Parkour skill 100 (seconds): the blue bar. [Synced with Server]", new AcceptableValueRange<float>(0.3f, 8f))));
            WallRunSpeedBonus = Synced.Add(Config.Bind("2 - Strengths", "WallRunSpeedPercentAtSkill100", 15f, new ConfigDescription("How much faster a wall run is at Parkour skill 100 (percent; less in between). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            KicksAtSkill0 = Synced.Add(Config.Bind("2 - Strengths", "WallJumpsInARow", 2, new ConfigDescription("How many wall jumps in a row before you must touch the ground, at Parkour skill 0. [Synced with Server]", new AcceptableValueRange<int>(1, 20))));
            KickEveryLevels = Synced.Add(Config.Bind("2 - Strengths", "OneMoreWallJumpEveryLevels", 20, new ConfigDescription("One more wall jump in a row every this many Parkour levels. [Synced with Server]", new AcceptableValueRange<int>(1, 100))));
            KicksMax = Synced.Add(Config.Bind("2 - Strengths", "WallJumpsInARowMax", 6, new ConfigDescription("The most wall jumps in a row, however high the skill. [Synced with Server]", new AcceptableValueRange<int>(1, 20))));
            ClimbMin = Synced.Add(Config.Bind("2 - Strengths", "ClimbHeight", 2f, new ConfigDescription("How high you run up a wall at Parkour skill 0 (metres). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 6f))));
            ClimbMax = Synced.Add(Config.Bind("2 - Strengths", "ClimbHeightAtSkill100", 4f, new ConfigDescription("How high you run up a wall at Parkour skill 100 (metres). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 10f))));
            HangMin = Synced.Add(Config.Bind("2 - Strengths", "HangSeconds", 3f, new ConfigDescription("How long you can hang from a ledge at Parkour skill 0 (seconds): the blue bar, full again with every grab. [Synced with Server]", new AcceptableValueRange<float>(0.5f, 20f))));
            HangMax = Synced.Add(Config.Bind("2 - Strengths", "HangSecondsAtSkill100", 6f, new ConfigDescription("How long you can hang from a ledge at Parkour skill 100 (seconds). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 30f))));
            ShimmyMin = Synced.Add(Config.Bind("2 - Strengths", "ShimmySpeed", 1.5f, new ConfigDescription("How fast you move along a ledge at Parkour skill 0 (metres per second). [Synced with Server]", new AcceptableValueRange<float>(0.2f, 6f))));
            ShimmyMax = Synced.Add(Config.Bind("2 - Strengths", "ShimmySpeedAtSkill100", 2f, new ConfigDescription("The same at Parkour skill 100. [Synced with Server]", new AcceptableValueRange<float>(0.2f, 6f))));
            SlideMin = Synced.Add(Config.Bind("2 - Strengths", "SlideSeconds", 1f, new ConfigDescription("How long a slide can last at Parkour skill 0 (seconds; the blue bar). [Synced with Server]", new AcceptableValueRange<float>(0.2f, 5f))));
            SlideMax = Synced.Add(Config.Bind("2 - Strengths", "SlideSecondsAtSkill100", 1.5f, new ConfigDescription("The same at Parkour skill 100. [Synced with Server]", new AcceptableValueRange<float>(0.2f, 5f))));
            SlideBoost = Synced.Add(Config.Bind("2 - Strengths", "SlideBoostPercent", 25f, new ConfigDescription("How much faster than your sprint a slide starts (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            LandingSlideMax = Synced.Add(Config.Bind("2 - Strengths", "LandingSlideMaxHeight", 4f, new ConfigDescription("Crouch just before landing from at most this high (metres), moving forward, and you land in a slide; from higher, in a roll. [Synced with Server]", new AcceptableValueRange<float>(0f, 20f))));
            SlideJumpBoost = Synced.Add(Config.Bind("2 - Strengths", "SlideJumpBoostPercent", 40f, new ConfigDescription("A jump out of a slide carries the slide's speed and this much more forward (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SlideJumpHeight = Synced.Add(Config.Bind("2 - Strengths", "SlideJumpExtraHeight", 30f, new ConfigDescription("A jump out of a slide goes this much higher than a normal jump (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            WetShorter = Synced.Add(Config.Bind("2 - Strengths", "WetShorterPercent", 30f, new ConfigDescription("How much shorter wall runs are while you are Wet (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            RollWindowMin = Synced.Add(Config.Bind("2 - Strengths", "RollWindowSeconds", 0.3f, new ConfigDescription("How long before landing a crouch press still rolls, at Parkour skill 0 (seconds). [Synced with Server]", new AcceptableValueRange<float>(0.05f, 2f))));
            RollWindowMax = Synced.Add(Config.Bind("2 - Strengths", "RollWindowSecondsAtSkill100", 0.5f, new ConfigDescription("The same at Parkour skill 100 (seconds). [Synced with Server]", new AcceptableValueRange<float>(0.05f, 2f))));
            RollSafeMin = Synced.Add(Config.Bind("2 - Strengths", "RollSafeHeight", 8f, new ConfigDescription("A roll takes no fall damage up to this height, at Parkour skill 0 (metres; the game's falls hurt from 4). [Synced with Server]", new AcceptableValueRange<float>(4f, 40f))));
            RollSafeMax = Synced.Add(Config.Bind("2 - Strengths", "RollSafeHeightAtSkill100", 10f, new ConfigDescription("The same at Parkour skill 100 (metres). [Synced with Server]", new AcceptableValueRange<float>(4f, 40f))));
            RollDamageShare = Synced.Add(Config.Bind("2 - Strengths", "RollDamagePercent", 50f, new ConfigDescription("Of the fall damage from above the safe height, how much a roll still takes (percent). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            AutoRollLevel = Synced.Add(Config.Bind("2 - Strengths", "AutoRollLevel", 50, new ConfigDescription("The Parkour level from which hard landings roll by themselves (101: never). [Synced with Server]", new AcceptableValueRange<int>(0, 101))));
            SkillGain = Synced.Add(Config.Bind("2 - Strengths", "SkillGain", 1f, new ConfigDescription("How fast the Parkour skill rises (1 = like the game's Jump skill). [Synced with Server]", new AcceptableValueRange<float>(0f, 10f))));

            WallRunCost = Synced.Add(Config.Bind("3 - Stamina", "WallRun", 0f, new ConfigDescription("Stamina to start a wall run. 0: a wall run uses only the blue parkour bar, and sprinting costs nothing while on the wall. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            WallRunCostPerSecond = Synced.Add(Config.Bind("3 - Stamina", "WallRunPerSecond", 0f, new ConfigDescription("Stamina per second on a wall run (0: only the blue parkour bar runs down). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            ClimbCost = Synced.Add(Config.Bind("3 - Stamina", "WallClimb", 0f, new ConfigDescription("Stamina to run up a wall (0: only the blue parkour bar, the height left, runs down). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            HangCostPerSecond = Synced.Add(Config.Bind("3 - Stamina", "HangPerSecond", 0f, new ConfigDescription("Stamina per second hanging from a ledge (0: only the blue parkour bar runs down). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            MantleCost = Synced.Add(Config.Bind("3 - Stamina", "PullUp", 0f, new ConfigDescription("Stamina to pull yourself up onto a ledge (0: free, like the other moves on the blue parkour bar). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SkillCostCut = Synced.Add(Config.Bind("3 - Stamina", "SkillCutPercent", 33f, new ConfigDescription("How much less the moves above cost at Parkour skill 100 (percent; less in between). A wall jump costs the game's own jump stamina, a landing roll its dodge stamina. [Synced with Server]", new AcceptableValueRange<float>(0f, 90f))));

            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Skill.Register(Player.m_localPlayer);   // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Tools.Update();
        }

        private void LateUpdate()
        {
            Look.Late();   // after the animation has posed the player
            Bar.Late();    // after the HUD has placed the stamina bar
        }

        private void OnDestroy()
        {
            Share.Reset();   // (first: "no pose" on the player's ZDO while it is still ours)
            Moves.GiveHandsBack();   // (what a hang put away comes back out)
            Moves.Reset();
            Roll.Reset();
            Feel.Reset();
            Look.Reset();
            Bar.Destroy();
            Controls.Reset();
            Compat.Reset();
            Skill.Reset();
            Synced?.Dispose();
            _harmony?.UnpatchSelf();
            Tools.Unregister();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }
}
