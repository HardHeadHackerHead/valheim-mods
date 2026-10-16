using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// It patches methods, adds a skill (its backup is on the character) and one item, the Web Shooters, which goes in through ItemReload.cs
// (kept registered across a reload, the new copy's put in its place) with its recipe through Shared/Content.cs: safe to reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Webslinger
{
    /// <summary>
    /// Webslinger: be Spider-Man in Valheim. Sprint and jump to leap far ahead; wear the Web Shooters (a utility item made at the workbench) to
    /// press Jump in the air to shoot a web and swing (let go to fly off), press Sprint in the air to zip to the spot under the
    /// crosshair, stick to any wall you hit in the air and crawl along it, and feel attacks coming (spider-sense). A plain jump stays the
    /// game's own. The moves train a new Webslinging skill: longer webs, faster zips and swings.
    /// Movement is each player's own game's job, so only the players who want it need the mod; with it, they also see each other's webs.
    /// The whole plan, with the phases still to come (web combat, suits, web building), is docs/webslinger-design.md.
    ///
    /// Split across files: Plugin.cs (settings, setup), Controls.cs (the player's input, and test driving), Anchors.cs (finding what a web
    /// sticks to, and walls), Web.cs (the moves: swing, zip, crawl, flight), Look.cs (the webs drawn, the body's lean, sounds, the camera),
    /// Shooters.cs (the Web Shooters item, and the shooters on the wrists), Suit.cs (the Spider-Viking and Venom suits), SuitProbe.cs (pictures of the body's textures), ItemReload.cs (the item across a reload),
    /// Share.cs (the web shared with other players), Sense.cs (spider-sense), Marks.cs (the web marks on screen), Body.cs (the poses and flips), Fx.cs (web splats, speed lines, the camera), Sounds.cs (the web's sounds), Skill.cs (the Webslinging skill), Compat.cs (other mods),
    /// Tools.cs (Claude Tools commands).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.webslinger";
        public const string Name = "Webslinger";
        public const string Version = "0.3.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        // each player's own
        internal static ConfigEntry<bool> Enabled, Swing, Zip, Crawl, SpiderSense, Hints, Sounds, SpeedFov, BodyLean, HoldToSwing, ShowMarks;
        internal static ConfigEntry<SenseMode> SenseFrom;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<bool> Splats, SpeedLines, CameraMotion, WebTrails;
        internal static ConfigEntry<float> WebTrailSeconds;
        // the server's
        internal static ConfigEntry<float> WebReach, WebReachAt100, ZipReach, ZipReachAt100, ZipSpeed, MaxSpeed, Pump, ReleaseBoost, PerfectBoost,
            CrawlSpeed, FallDamage, WebLandingDamage, Steer, LeapForward, LeapUp, SkillGain, SenseRange, SwingSpeed, SwingSpeedAt100;
        internal static ConfigEntry<int> AirZips, AirZipsAt100;
        internal static ConfigEntry<bool> SkyWebs, NeedsShooters;
        internal static ConfigEntry<string> ShootersCost, SuitCost, VenomCost;
        internal static ConfigEntry<float> WebCost, SwingCostPerSecond, ZipCost, AirZipCost, CrawlCostPerSecond, SkillCostCut;
        internal static DHack.Shared.ServerSettings Synced;   // how strong and how dear the moves are is the server's in multiplayer

        internal enum SenseMode { OutOfSight, Everywhere }

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Config.SaveOnConfigSet = false;

            Enabled = Config.Bind("1 - Moves", "Enabled", true, "Webslinger on or off (all the moves).");
            NeedsShooters = Synced.Add(Config.Bind("1 - Moves", "NeedsWebShooters", true, "Webs (swings and zips) shoot only while the Web Shooters are worn (the utility slot); the leap, the wall crawl and spider-sense never need them. Off: webs always work, and the Web Shooters do nothing. [Synced with Server]"));
            ShootersCost = Synced.Bind("1 - Moves", "WebShootersCost", "LeatherScraps:4, Resin:10, Flint:2", "What the Web Shooters take to make at the workbench. " + DHack.Shared.Costs.Help);
            ShootersCost.SettingChanged += (o, e) => Shooters.ApplyCost();
            SuitCost = Synced.Bind("1 - Moves", "SpiderVikingSuitCost", "LeatherScraps:6, DeerHide:4, Raspberry:10, Blueberries:10", "What the Spider-Viking suit takes to make at the workbench (level 2). " + DHack.Shared.Costs.Help);
            SuitCost.SettingChanged += (o, e) => Suit.ApplyCost();
            VenomCost = Synced.Bind("1 - Moves", "VenomSuitCost", "LeatherScraps:6, DeerHide:4, Ooze:10, Coal:10", "What the Venom suit takes to make at the workbench (level 2). " + DHack.Shared.Costs.Help);
            VenomCost.SettingChanged += (o, e) => Suit.ApplyCost();
            Swing = Config.Bind("1 - Moves", "Swing", true, "Swing: press and hold Jump in the air to shoot a web up ahead and swing from it; let go to fly off. Only when something is in reach to stick to: otherwise a jump is just a jump.");
            HoldToSwing = Config.Bind("1 - Moves", "HoldToKeepSwinging", true, "Keep holding Jump and, at the top of each swing, the next web shoots ahead by itself. Off: one web per press.");
            Zip = Config.Bind("1 - Moves", "Zip", true, "Zip: press Sprint in the air to pull yourself to the spot under the crosshair (a top: land on it; a wall: stick to it). Nothing in reach: a short zip forward.");
            Crawl = Config.Bind("1 - Moves", "WallCrawl", true, "Wall crawl: move into a wall in the air to stick to it. Move keys crawl, Jump kicks off, Crouch lets go.");
            Hints = Config.Bind("1 - Moves", "Hints", true, "A short hint the first time you do each move in a session.");
            SpiderSense = Config.Bind("2 - Spider-sense", "Enabled", true, "Spider-sense: wavy lines round your head, a glow on the side it comes from and a sound, a moment before an attack or an arrow reaches you.");
            SenseFrom = Config.Bind("2 - Spider-sense", "From", SenseMode.OutOfSight, "OutOfSight: only for attacks you can't see (behind or beside you) and anything shot at you. Everywhere: every attack aimed at you.");
            Sounds = Config.Bind("4 - Feel", "Sounds", true, "The web's sounds: the thwip as it shoots, the thunk as it sticks, the snap as it lets go, the wind at speed, the spider-sense tingle.");
            SoundVolume = Config.Bind("4 - Feel", "SoundVolume", 0.8f, new ConfigDescription("How loud the web's sounds are (on top of the game's sound effects volume).", new AcceptableValueRange<float>(0f, 1f)));
            Splats = Config.Bind("4 - Feel", "WebSplats", true, "A small web patch where each web sticks, fading after a while.");
            WebTrails = Config.Bind("4 - Feel", "WebsLeftHanging", true, "A web you let go of stays stuck where it hit and hangs down from there, swaying, until it fades: a trail of webs behind you (others' webs too).");
            WebTrailSeconds = Config.Bind("4 - Feel", "HangingWebSeconds", 10f, new ConfigDescription("How long a web left hanging stays (seconds).", new AcceptableValueRange<float>(2f, 60f)));
            SpeedLines = Config.Bind("4 - Feel", "SpeedLines", true, "Speed lines streaming past at swinging speed. Turn it off if motion on screen makes you feel sick.");
            CameraMotion = Config.Bind("4 - Feel", "CameraMotion", true, "The camera rolls a little into turns and toward the web, and dips at the bottom of a swing. Turn it off if camera motion makes you feel sick.");
            SpeedFov = Config.Bind("4 - Feel", "SpeedFov", true, "The view widens a little at high speed. Turn it off if camera motion makes you feel sick.");
            ShowMarks = Config.Bind("4 - Feel", "WebMarks", true, "In the air, a small web mark shows where a swing web would stick (no mark: nothing in reach, a jump is just a jump), and a ring under the crosshair when a zip can reach it.");
            BodyLean = Config.Bind("4 - Feel", "BodyLean", true, "The body leans along the web and takes the moves' poses: the arm up the web, legs tucked at the top of a swing, flips on a perfect release, a spider's crawl on walls (players with the mod see them too).");

            WebReach = Synced.Add(Config.Bind("3 - Strengths", "WebReach", 40f, new ConfigDescription("How far a swing web reaches at Webslinging skill 0 (metres). [Synced with Server]", new AcceptableValueRange<float>(10f, 150f))));
            WebReachAt100 = Synced.Add(Config.Bind("3 - Strengths", "WebReachAtSkill100", 60f, new ConfigDescription("The same at Webslinging skill 100 (metres). [Synced with Server]", new AcceptableValueRange<float>(10f, 150f))));
            SkyWebs = Synced.Add(Config.Bind("3 - Strengths", "SkyWebs", false, "With nothing in reach high enough to swing from (open plains, the sea), a swing web sticks to the sky up ahead, as in The Amazing Spider-Man. Off: webs need something solid, and with nothing in reach a jump is just a jump. [Synced with Server]"));
            LeapForward = Synced.Add(Config.Bind("3 - Strengths", "SprintLeapForward", 22f, new ConfigDescription("The Spider-Man leap: a jump while sprinting throws you this fast ahead (metres per second; the game's sprint is about 7). 0: a sprint jump is the game's own. A jump without sprinting is always the game's own. [Synced with Server]", new AcceptableValueRange<float>(0f, 40f))));
            LeapUp = Synced.Add(Config.Bind("3 - Strengths", "SprintLeapUp", 13f, new ConfigDescription("How fast the leap goes up (metres per second; the game's gravity on a player is about 20, eased while the leap rises). Looking up leaps higher and shorter. [Synced with Server]", new AcceptableValueRange<float>(0f, 30f))));
            ZipReach = Synced.Add(Config.Bind("3 - Strengths", "ZipReach", 35f, new ConfigDescription("How far a zip reaches at Webslinging skill 0 (metres). [Synced with Server]", new AcceptableValueRange<float>(5f, 150f))));
            ZipReachAt100 = Synced.Add(Config.Bind("3 - Strengths", "ZipReachAtSkill100", 50f, new ConfigDescription("The same at Webslinging skill 100 (metres). [Synced with Server]", new AcceptableValueRange<float>(5f, 150f))));
            ZipSpeed = Synced.Add(Config.Bind("3 - Strengths", "ZipSpeed", 30f, new ConfigDescription("How fast a zip pulls you (metres per second). [Synced with Server]", new AcceptableValueRange<float>(5f, 80f))));
            AirZips = Synced.Add(Config.Bind("3 - Strengths", "AirZips", 2, new ConfigDescription("Zips forward into nothing (no spot in reach) before touching the ground, at skill 0. [Synced with Server]", new AcceptableValueRange<int>(0, 10))));
            AirZipsAt100 = Synced.Add(Config.Bind("3 - Strengths", "AirZipsAtSkill100", 3, new ConfigDescription("The same at Webslinging skill 100. [Synced with Server]", new AcceptableValueRange<int>(0, 10))));
            MaxSpeed = Synced.Add(Config.Bind("3 - Strengths", "MaxSpeed", 35f, new ConfigDescription("The fastest a swing, a dive or a flight can ever go (metres per second; the game's sprint is about 7). SwingSpeed is the usual speed. [Synced with Server]", new AcceptableValueRange<float>(10f, 100f))));
            SwingSpeed = Synced.Add(Config.Bind("3 - Strengths", "SwingSpeed", 20f, new ConfigDescription("How fast swinging gets at Webslinging skill 0 (metres per second): pumping builds speed up to it, and faster (a dive) the air slowly brings it back. [Synced with Server]", new AcceptableValueRange<float>(5f, 60f))));
            SwingSpeedAt100 = Synced.Add(Config.Bind("3 - Strengths", "SwingSpeedAtSkill100", 26f, new ConfigDescription("The same at Webslinging skill 100 (metres per second). [Synced with Server]", new AcceptableValueRange<float>(5f, 60f))));
            Steer = Synced.Add(Config.Bind("3 - Strengths", "SwingSteer", 100f, new ConfigDescription("How fast a swing turns toward the move keys (degrees per second). [Synced with Server]", new AcceptableValueRange<float>(0f, 360f))));
            Pump = Synced.Add(Config.Bind("3 - Strengths", "SwingPump", 12f, new ConfigDescription("How much holding a move key speeds up a swing that way (metres per second, each second). [Synced with Server]", new AcceptableValueRange<float>(0f, 40f))));
            ReleaseBoost = Synced.Add(Config.Bind("3 - Strengths", "ReleaseBoost", 4f, new ConfigDescription("The fling upward when you let go of a web (metres per second). [Synced with Server]", new AcceptableValueRange<float>(0f, 20f))));
            PerfectBoost = Synced.Add(Config.Bind("3 - Strengths", "PerfectReleaseBoost", 8f, new ConfigDescription("The fling when you let go near the top of the swing, still rising (metres per second; more with the skill). [Synced with Server]", new AcceptableValueRange<float>(0f, 30f))));
            CrawlSpeed = Synced.Add(Config.Bind("3 - Strengths", "CrawlSpeed", 3f, new ConfigDescription("How fast you crawl on walls (metres per second; half as fast again sprinting). [Synced with Server]", new AcceptableValueRange<float>(0.5f, 15f))));
            FallDamage = Synced.Add(Config.Bind("3 - Strengths", "FallDamagePercent", 50f, new ConfigDescription("Of the game's fall damage, how much you still take on other falls (percent): spider agility. 100: the game's own. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            WebLandingDamage = Synced.Add(Config.Bind("3 - Strengths", "WebLandingDamagePercent", 0f, new ConfigDescription("Of the game's fall damage, how much you take landing from a swing, a zip, a wall or a flight off one (percent). 0: none, as in the Spider-Man games. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SenseRange = Synced.Add(Config.Bind("3 - Strengths", "SpiderSenseRange", 30f, new ConfigDescription("How far away an attack sets off spider-sense (metres). [Synced with Server]", new AcceptableValueRange<float>(0f, 80f))));
            SkillGain = Synced.Add(Config.Bind("3 - Strengths", "SkillGain", 1f, new ConfigDescription("How fast the Webslinging skill rises (1 = like the game's skills). [Synced with Server]", new AcceptableValueRange<float>(0f, 10f))));

            WebCost = Synced.Add(Config.Bind("5 - Stamina", "SwingWeb", 0f, new ConfigDescription("Stamina for each swing web. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SwingCostPerSecond = Synced.Add(Config.Bind("5 - Stamina", "SwingPerSecond", 0f, new ConfigDescription("Stamina per second hanging on a swing web. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            ZipCost = Synced.Add(Config.Bind("5 - Stamina", "Zip", 0f, new ConfigDescription("Stamina for a zip to a spot. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            AirZipCost = Synced.Add(Config.Bind("5 - Stamina", "AirZip", 0f, new ConfigDescription("Stamina for a zip forward into nothing. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            CrawlCostPerSecond = Synced.Add(Config.Bind("5 - Stamina", "CrawlPerSecond", 0f, new ConfigDescription("Stamina per second crawling on a wall (nothing while still). [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            SkillCostCut = Synced.Add(Config.Bind("5 - Stamina", "SkillCutPercent", 33f, new ConfigDescription("How much less every move costs at Webslinging skill 100 (percent; less in between). [Synced with Server]", new AcceptableValueRange<float>(0f, 90f))));

            Config.SaveOnConfigSet = true;
            Config.Save();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Skill.Create();   // (after the translations; also puts it on the player already in the world after a hot reload)
            Shooters.Register();                 // a hot reload while in a world
            Suit.Register();
            ItemReload.Repoint(name => Shooters.Current(name) ?? Suit.Current(name));   // items made by the copy this replaces
            Shooters.Refresh();                     // and the ones the game gave their own copy of the data (the give command)
            Suit.RefreshBag();
            Suit.Refresh();                         // a suit worn: shown by this copy
            Skill.Gain = SkillGain.Value;
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Skill.Gain = SkillGain.Value;   // (the server's, live)
            Controls.Update();
            Sense.Update();
            Marks.Update();
            Tools.Update();
        }

        private void LateUpdate()
        {
            Look.Late();   // after the animation has posed the players: the webs from their hands
            Shooters.Late();
            Sense.Late();
        }

        private void OnDestroy()
        {
            Share.Reset();   // (first: "no web" on the player's ZDO while it is still ours)
            Web.Reset();
            Look.Reset();
            Sense.Reset();
            Marks.Reset();
            Body.Reset();
            Fx.Reset();
            global::Webslinger.Sounds.Reset();
            Controls.Reset();
            Compat.Reset();
            Skill.Reset();
            Synced?.Dispose();
            _harmony?.UnpatchSelf();
            Suit.Refresh();   // (unpatched: the real armour shows again)
            Shooters.Unregister();
            Suit.Unregister();
            Tools.Unregister();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the Web Shooters and the suit are registered before a saved pair is loaded.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() { Shooters.Register(); Suit.Register(); } }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() { Shooters.Register(); Suit.Register(); } }

    // The game swaps in another ObjectDB's item lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB { private static void Postfix() { Shooters.Register(); Suit.Register(); } }
}
