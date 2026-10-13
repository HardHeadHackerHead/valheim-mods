using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// The guns go in through ItemReload (see ItemReload.cs) and their recipes through Shared/Content.cs (put back by name each time): reload in a world.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Guns
{
    /// <summary>
    /// Quad's Guns: firearms for Valheim. A gun is an item cloned from the Arbalest (so the hands, the aim
    /// pose and the Crossbows skill are the game's own) whose shots are fired here instead of by the game's attack: hold the attack button
    /// and it fires on its own, faster than any animation could, with a spread that grows the longer you hold (see Fire.cs).
    ///
    /// Split across files: Plugin.cs (setup, hooks), Defs.cs (every gun, as data), Models.cs (their models), Things.cs (registering), Gun.cs (the
    /// item and its round), Fire.cs (firing and spread), Flame.cs (the flamethrower's stream), Ammo.cs (magazines, reloading), Aim.cs (aiming, posing), AmmoHud.cs (the numbers and the scope).
    /// A new gun is one more GunDef in Defs.cs and a model in Models.cs.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.guns";
        public const string Name = "Quad's Guns";
        public const string Version = "0.3.1";

        // the server's in multiplayer: they decide how hard a player can hit
        internal static ConfigEntry<float> SpreadScale, ShotStamina, DiveStamina;
        // the player's own
        internal static ConfigEntry<float> Recoil, Volume, AimZoom;
        internal static ConfigEntry<bool> ArmPosing;
        internal static DHack.Shared.ServerSettings Synced;
        private static bool _tellNoServer;
        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Defs.Load();
            SpreadScale = Synced.Add(Config.Bind("0 - All guns", "SpreadScale", 1f, new ConfigDescription("Multiplies every gun's spread: 0 for perfectly accurate guns, 2 for twice as wild. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 4f))));
            ShotStamina = Synced.Add(Config.Bind("0 - All guns", "StaminaPerShot", 0f, new ConfigDescription("Stamina each round costs. 0 for none (the default while the guns are being tested). In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 5f))));
            DiveStamina = Synced.Add(Config.Bind("0 - All guns", "DiveStamina", 0f, new ConfigDescription("Stamina a dive costs (block + jump with a light gun). 0 for none (the default while the dive is being tried out). In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 50f))));
            foreach (GunDef d in Arsenal.All)
            {
                string damage = d.Flame ? "Fire damage of each puff of flame, before skill: it sets what it touches burning (the game burns it off over the next seconds), and a quarter of it again lands at once. In multiplayer the server's value applies."
                              : d.Pellets > 1 ? "Piercing damage of one pellet, before skill. In multiplayer the server's value applies." : "Piercing damage of one round, before skill. In multiplayer the server's value applies.";
                d.CDamage = Synced.Add(Config.Bind(d.Section, "Damage", d.Damage, new ConfigDescription(damage, new AcceptableValueRange<float>(1f, 1000f))));
                d.CCost = Synced.Bind(d.Section, "Cost", d.Cost, "What it takes to make at the forge (level " + d.Station + "). " + DHack.Shared.Costs.Help);
                d.CCost.SettingChanged += (s, e) => Things.ApplyCosts();
                d.CMag = Synced.Add(Config.Bind(d.Section, d.Flame ? "Fuel" : "MagazineSize", d.Mag, new ConfigDescription(d.Flame ? "How many puffs of flame a full tank holds. In multiplayer the server's value applies." : "How many rounds a full magazine holds. In multiplayer the server's value applies.", new AcceptableValueRange<int>(1, 1000))));
                d.CReload = Synced.Add(Config.Bind(d.Section, d.PerRound ? "SecondsPerRound" : "ReloadSeconds", d.Reload, new ConfigDescription(d.PerRound ? "Seconds to load each round. In multiplayer the server's value applies." : "Seconds a reload takes. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0.1f, 20f))));
                if (d.Auto || d.Cycle <= 0f)
                    d.CRate = Synced.Add(Config.Bind(d.Section, d.Flame ? "PuffsPerSecond" : "RoundsPerSecond", d.Rate, new ConfigDescription(d.Flame ? "How many puffs of flame it sends out each second while the attack button is held (each uses one of the fuel). In multiplayer the server's value applies." : d.Auto ? "How many rounds it fires each second while the attack button is held. In multiplayer the server's value applies." : "The most rounds it fires each second, one for each pull of the trigger. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0.5f, 40f))));
            }
            AimZoom = Config.Bind("1 - Your own", "AimZoomScale", 1f, new ConfigDescription("Multiplies how far the camera zooms in while you aim (hold the block button: right mouse). 1 for each gun's own zoom. Your own setting.", new AcceptableValueRange<float>(0.5f, 2f)));
            ArmPosing = Config.Bind("1 - Your own", "ArmPosing", true, new ConfigDescription("The mod bends your arms to hold each gun with both hands (shouldered when you aim, low when you sprint). Switch off to use the game's own crossbow carry. Your own setting."));
            Recoil = Config.Bind("1 - Your own", "Recoil", 1f, new ConfigDescription("How much each shot kicks your view up. 0 for none, 1 is the default. Your own setting.", new AcceptableValueRange<float>(0f, 3f)));
            Volume = Config.Bind("1 - Your own", "ShotVolume", 1f, new ConfigDescription("How loud the guns are (on top of the game's sound-effects volume). Your own setting.", new AcceptableValueRange<float>(0f, 2f)));
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == DHack.Shared.ServerSettings.ServerMod.Missing;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Things.Register();   // a hot reload while in a world
            foreach (Player p in Player.GetAllPlayers()) Share.Register(p);   // (a hot reload: the players already in the world)
            ItemReload.Repoint(Things.Current);   // guns made by the copy this replaces
            Things.Refresh();                      // and the ones the game gave their own copy of the data (the give command)
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Share.Publish();
            Aim.Tick();
            Fire.Tick();
            Ammo.Tick(Player.m_localPlayer);
            Flame.Tick();
            Requests.Tick();
            AmmoHud.Tick();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$guns_server_missing");
            }
        }

        private void LateUpdate() { Dive.Pose(Player.m_localPlayer); Aim.Pose(); Share.Late(); Flame.Late(); }

        private void OnDestroy()
        {
            Dive.End(Player.m_localPlayer);
            Share.Reset();
            Flame.Clear();   // (every stream stops, even when a reload in a world keeps the prefabs)
            Requests.Unregister();
            Aim.Stop();
            Ammo.Cancel();
            AmmoHud.Destroy();
            Synced?.Dispose();
            _harmony?.UnpatchSelf();
            Things.Unregister();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the gun is registered before a saved one is loaded.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() => Things.Register(); }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() => Things.Register(); }

    // The game swaps in another ObjectDB's item lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB { private static void Postfix() => Things.Register(); }
}
