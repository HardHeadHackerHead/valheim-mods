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
        public const string Guid = "com.dhack.arena";
        public const string Name = "Arena";
        public const string Version = "0.3.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // the settings the server decides in multiplayer (what a contest pays and costs, and how hard it is)

        internal static ConfigEntry<float> Rewards, CrowdVolume, CrowdSize, EntryFee, MealFreshness, RingHunger;
        internal static ConfigEntry<bool> RealDeath, Spectators, Announcer, Gifts, MapPin, AllTiers, RespawnAtArena;
        internal static ConfigEntry<KeyboardShortcut> YieldKey;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string ServerSays = " In multiplayer the server's value applies.";
            Rewards = Synced.Add(Config.Bind("Arena", "RewardPercent", 100f, new ConfigDescription("How big the prizes are (percent)." + ServerSays, new AcceptableValueRange<float>(10f, 500f))));
            RealDeath = Synced.Add(Config.Bind("Arena", "DeathIsReal", true, "You can die in a contest. Your tombstone is carried just outside the ring for you to collect, and the purse and stake are lost. Off: beaten, you are carried out at a third of your health with half your purse. (Duels never kill.)" + ServerSays));
            EntryFee = Synced.Add(Config.Bind("Arena", "EntryFeePercent", 100f, new ConfigDescription("How much it costs to enter a contest (percent of the usual: 150 coins for the Long Road; today's trial 60, a Champion Bout 50 and the Endless Horde 40 in the Meadows, more in each land after). The first fight of a character is free." + ServerSays, new AcceptableValueRange<float>(0f, 500f))));
            RespawnAtArena = Synced.Add(Config.Bind("Arena", "RespawnAtArena", true, "Fall in a contest and you rise again in the arena's forecourt (not at your bed)." + ServerSays));
            MapPin = Config.Bind("Arena", "MapPin", true, "The Arena is marked on your map.");
            AllTiers = Synced.Add(Config.Bind("Arena", "AllTiers", false, "Every land's fighters can be chosen in a Champion Bout or the Endless Horde at once (normally each opens as you beat the boss before it). For trying them out: the arena still pays metals and trophies only from lands your world has reached, today's trial stays among them, and what the crowd throws from a land your world has not reached is only lent (it goes back after the fight)." + ServerSays));
            Gifts = Synced.Add(Config.Bind("Crowd", "Gifts", true, "When the crowd likes you it throws you things now and then: food and meads, then arrows, bombs and strong meads, and (on the arena's steel) the next land's weapon, more often and better the more they love you." + ServerSays));
            MealFreshness = Synced.Add(Config.Bind("Crowd", "LeftoverMeals", 50f, new ConfigDescription("On the arena's steel: how much of its time one of the kitchen's meals (yesterday's leftovers) has left once eaten, percent. 100 is a fresh meal." + ServerSays, new AcceptableValueRange<float>(5f, 100f))));
            RingHunger = Synced.Add(Config.Bind("Crowd", "RingHunger", 2.5f, new ConfigDescription("On the arena's steel: how many times faster food burns (fighting is hungry work), so a fresh meal lasts about a land and the crowd's gifts matter. 1 is as outside." + ServerSays, new AcceptableValueRange<float>(1f, 6f))));
            Spectators = Config.Bind("Crowd", "Spectators", true, "A crowd fills the stands during a fight.");
            CrowdSize = Config.Bind("Crowd", "FullHouse", 30f, new ConfigDescription("How full the stands are during a fight (percent of the seats). Fewer is lighter on the frame rate.", new AcceptableValueRange<float>(0f, 100f)));
            Announcer = Config.Bind("Crowd", "Announcer", true, "The announcer's calls across the top of the screen.");
            YieldKey = Config.Bind("Arena", "YieldKey", new KeyboardShortcut(KeyCode.Backspace), "Press twice to give up a contest (you keep what you have earned so far, at a lower rate).");
            CrowdVolume = Config.Bind("Crowd", "Volume", 0.5f, new ConfigDescription("How loud the crowd is.", new AcceptableValueRange<float>(0f, 1f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Duel.Init();
            Net.Init();
            Layout.Load();
            Show.ClearLeftovers();   // (a hot reload: what the last copy of the mod left in the world)
            Things.Register();   // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();   // (notices joining and leaving a server, for the settings it decides)
            Tools.Update();
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
            if (Instance == this) Instance = null;
        }
    }
}
