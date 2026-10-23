using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The Arena, after the one in the first Fable. Build an Arena Standard (hammer, Misc) in the middle of a ring and press E on it: the Arena
    /// Master offers contests. Walk the Long Road (three rounds in each land, Meadows to Ashlands, on the arena's steel), or fight one named
    /// champion (the Champion Bout) or the Endless Horde with your own gear, with rules to make it harder and worth more (bare fists, no food, hard), and stake coins on yourself. The crowd fills the stands,
    /// cheers and boos, and its favour raises the prize. Challenge another player to a Duel for a wager. Your wins are kept on your character.
    ///
    /// Split across files: Plugin.cs (settings, setup), Patches.cs (the game hooks), Things.cs and Stand.cs (the piece), Roster.cs (who you fight),
    /// Contest.cs (the contests), Kit.cs (the arena's steel and meals, your things held while you fight), Rules.cs (bare fists, no food), Crowd.cs (the stands, the noise, the favour), Hud.cs (the
    /// announcer and the bars), Window.cs (the Arena Master's menu), Ladder.cs (what you have won), Duel.cs (a duel between two players), Tools.cs.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.arena";
        public const string OldGuid = "com.dhack.arena"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "Arena";
        public const string Version = "0.5.2";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // the settings the server decides in multiplayer (what a contest pays and costs, how hard it is, what the waystone costs)
        internal static ConfigEntry<string> WaystoneCost;
        private static bool _tellNoServer;   // the server hasn't the mod: say so once the player is in the world

        internal static ConfigEntry<float> Rewards, CrowdVolume, CrowdSize, EntryFee, MealFreshness, RingHunger, FoeHealth, FoeDamage;
        internal static ConfigEntry<int> MaxStake, MaxWager;
        internal static ConfigEntry<bool> OwnSounds;
        internal static ConfigEntry<bool> RealDeath, Spectators, Announcer, Gifts, MapPin, AllTiers, RespawnAtArena;
        internal static ConfigEntry<KeyboardShortcut> YieldKey, ReadyKey;

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string ServerSays = " In multiplayer the server's value applies.";
            Rewards = Synced.Add(Config.Bind("Arena", "RewardPercent", 100f, new ConfigDescription("How big the prizes are (percent)." + ServerSays, new AcceptableValueRange<float>(10f, 500f))));
            RealDeath = Synced.Add(Config.Bind("Arena", "DeathIsReal", true, "You can die in a contest. Your tombstone is carried just outside the ring for you to collect, and the purse and stake are lost. Off: beaten, you are carried out at a third of your health with half your purse. (Duels never kill.)" + ServerSays));
            EntryFee = Synced.Add(Config.Bind("Arena", "EntryFeePercent", 100f, new ConfigDescription("How much it costs to enter a contest (percent of the usual: 150 coins for the Long Road; today's trial 60, a Champion Bout 50 and the Endless Horde 40 in the Meadows, more in each land after). The first fight of a character is free." + ServerSays, new AcceptableValueRange<float>(0f, 500f))));
            FoeHealth = Synced.Add(Config.Bind("Arena", "FoeHealthPercent", 100f, new ConfigDescription("How much health the arena's fighters have (percent of the usual), champions too, in every contest. 200 takes twice the beating." + ServerSays, new AcceptableValueRange<float>(25f, 1000f))));
            FoeDamage = Synced.Add(Config.Bind("Arena", "FoeDamagePercent", 100f, new ConfigDescription("How hard the arena's fighters hit (percent of the usual), champions too, in every contest. On top of the game's own difficulty." + ServerSays, new AcceptableValueRange<float>(25f, 1000f))));
            MaxStake = Synced.Add(Config.Bind("Arena", "MaxStake", 500, new ConfigDescription("The most a player may stake on themselves in a Champion Bout or today's trial (coins; a win pays it double, so a high limit pays out more). The Arena Master's slider goes from nothing up to this, or what you carry. 0: no stakes." + ServerSays, new AcceptableValueRange<int>(0, 100000))));
            MaxWager = Synced.Add(Config.Bind("Arena", "MaxWager", 1000, new ConfigDescription("The most each player may wager on a duel (coins; the winner takes both less a tenth, so nothing new is made). The slider goes up to this, or what you carry. 0: no wagers." + ServerSays, new AcceptableValueRange<int>(0, 100000))));
            RespawnAtArena = Synced.Add(Config.Bind("Arena", "RespawnAtArena", true, "Fall in a contest and you rise again in the arena's forecourt (not at your bed)." + ServerSays));
            MapPin = Config.Bind("Arena", "MapPin", true, "The Arena is marked on your map.");
            AllTiers = Synced.Add(Config.Bind("Arena", "AllTiers", false, "Every land's fighters can be chosen in a Champion Bout or the Endless Horde at once (normally each opens as you beat the boss before it). For trying them out: the arena still pays metals and trophies only from lands your world has reached, today's trial stays among them, and what the crowd throws from a land your world has not reached is only lent (it goes back after the fight)." + ServerSays));
            Gifts = Synced.Add(Config.Bind("Crowd", "Gifts", true, "When the crowd likes you it throws you things now and then: food and meads, then arrows, bombs and strong meads, and (on the arena's steel) the next land's weapon, more often and better the more they love you." + ServerSays));
            MealFreshness = Synced.Add(Config.Bind("Crowd", "LeftoverMeals", 70f, new ConfigDescription("On the arena's steel: how much of its time one of the kitchen's meals (yesterday's leftovers) has left once eaten, percent. 100 is a fresh meal." + ServerSays, new AcceptableValueRange<float>(5f, 100f))));
            RingHunger = Synced.Add(Config.Bind("Crowd", "RingHunger", 1.6f, new ConfigDescription("On the arena's steel: how many times faster food burns (fighting is hungry work), so a fresh meal lasts about a land and the crowd's gifts matter. 1 is as outside." + ServerSays, new AcceptableValueRange<float>(1f, 6f))));
            Spectators = Config.Bind("Crowd", "Spectators", true, "A crowd fills the stands during a fight.");
            CrowdSize = Config.Bind("Crowd", "FullHouse", 30f, new ConfigDescription("How full the stands are during a fight (percent of the seats). Fewer is lighter on the frame rate.", new AcceptableValueRange<float>(0f, 100f)));
            Announcer = Config.Bind("Crowd", "Announcer", true, "The announcer's calls across the top of the screen.");
            YieldKey = Config.Bind("Arena", "YieldKey", new KeyboardShortcut(KeyCode.Backspace), "Press twice to leave a contest: before it begins you back out with your fee back; between rounds you walk out with your winnings; in a round you give up (at a lower rate).");
            ReadyKey = Config.Bind("Arena", "ReadyKey", KeyboardShortcut.Empty, "Press to end a break between rounds (or the arming) at once: the next round starts in 3 seconds. None by default: the challenge emote does it too, and Shift+E at the Armourer.");
            OwnSounds = Config.Bind("Crowd", "OwnSounds", true, "Play your own crowd sounds from BepInEx/config/Arena/crowd (.ogg, .mp3 or .wav): a file named like one of the arena's (crowd_cheer...) replaces it, any other is a voice from the stands, heard now and then when the crowd cheers (see readme.txt there). Off: only the arena's own.");
            CrowdVolume = Config.Bind("Crowd", "Volume", 0.5f, new ConfigDescription("How loud the crowd is.", new AcceptableValueRange<float>(0f, 1f)));
            WaystoneCost = Synced.Bind("Building", "WaystoneCost", Things.WaystoneDefault, "What an Arena Waystone costs to build with the hammer (at a workbench). " + DHack.Shared.Costs.Help);
            WaystoneCost.SettingChanged += (s, e) => Things.ApplyCost();
            NewFoodDefaults();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == DHack.Shared.ServerSettings.ServerMod.Missing;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Duel.Init();
            Net.Init();
            Layout.Load();
            Show.ClearLeftovers();   // (a hot reload: what the last copy of the mod left in the world)
            Things.Register();   // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        /// <summary>
        /// 0.5.0 made the ring's food kinder (leftovers 70% left instead of 50%, food burning 1.6 times as fast instead of 2.5): a config
        /// file still on the old defaults takes the new ones, once (a marker file says it has), so a value someone chose is never touched.
        /// </summary>
        private static void NewFoodDefaults()
        {
            try
            {
                string marker = System.IO.Path.Combine(BepInEx.Paths.CachePath, "Arena", "food-defaults-0.5.0");
                if (System.IO.File.Exists(marker)) return;
                if (Mathf.Approximately(MealFreshness.Value, 50f)) MealFreshness.Value = 70f;
                if (Mathf.Approximately(RingHunger.Value, 2.5f)) RingHunger.Value = 1.6f;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(marker));
                System.IO.File.WriteAllText(marker, "The ring's food settings were moved to the 0.5.0 defaults where they were still on the old ones.");
            }
            catch (System.Exception e) { Log.LogWarning("Could not move the food settings to the new defaults: " + e.Message); }
        }

        private void Update()
        {
            Synced?.Update();   // (notices joining and leaving a server, for the settings it decides)
            Tools.Update();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$arena_server_missing");
            }
            Site.Tick();
            Ground.Tick();
            Net.Tick();
            Window.Tick();
            Kit.Tick();
            float dt = Time.deltaTime;
            Contest.Tick(dt);
            Guard.Tick();
            Duel.Tick(dt);
            Crowd.Tick(dt);
            Scenery.Tick(dt);
            Hud.Tick();
        }


        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Contest.Abort("The mod was unloaded.");
            Duel.Abort(true);
            Duel.Unregister();
            Crowd.Clear();
            Show.ClearLeftovers();
            Scenery.Drop();
            Site.Unpin();
            Net.Unregister();
            Fx.Clear();
            Hud.Clear();
            Window.Close();
            Ui.Destroy();
            Tools.Unregister();
            Things.Unregister();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }
}
