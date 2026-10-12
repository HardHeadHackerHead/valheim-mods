using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Reloadable in a world: the gun goes in only through ItemReload (see ItemReload.cs).
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Guns
{
    /// <summary>
    /// Quad's Guns: firearms for Valheim, starting with the Machine Gun. A gun is an item cloned from the Arbalest (so the hands, the aim
    /// pose and the Crossbows skill are the game's own) whose shots are fired here instead of by the game's attack: hold the attack button
    /// and it fires on its own, faster than any animation could, with a spread that grows the longer you hold (see Fire.cs).
    ///
    /// Split across files: Plugin.cs (setup, hooks), Things.cs (registering), MachineGun.cs (the item, its projectile and model),
    /// Fire.cs (firing and spread). Each new gun is a Gun in Guns.All; the rest is shared.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.guns";
        public const string Name = "Quad's Guns";
        public const string Version = "0.1.0";

        // all of these are the server's in multiplayer: they decide how hard a player can hit
        internal static ConfigEntry<float> Recoil, Volume, AimZoom, AimSpread, Damage, RoundsPerSecond, MinSpread, MaxSpread, SpreadPerShot, SpreadRecovery, ShotStamina;
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
            Damage = Synced.Add(Config.Bind("1 - Machine Gun", "Damage", 14f, new ConfigDescription("Piercing damage of one round, before skill. In multiplayer the server's value applies.", new AcceptableValueRange<float>(1f, 200f))));
            RoundsPerSecond = Synced.Add(Config.Bind("1 - Machine Gun", "RoundsPerSecond", 10f, new ConfigDescription("How many rounds it fires each second while the attack button is held. In multiplayer the server's value applies.", new AcceptableValueRange<float>(1f, 30f))));
            MinSpread = Synced.Add(Config.Bind("1 - Machine Gun", "MinSpread", 0.6f, new ConfigDescription("Spread in degrees of the first shot of a burst. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 15f))));
            MaxSpread = Synced.Add(Config.Bind("1 - Machine Gun", "MaxSpread", 6f, new ConfigDescription("Spread in degrees after holding the trigger a long time. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 30f))));
            SpreadPerShot = Synced.Add(Config.Bind("1 - Machine Gun", "SpreadPerShot", 0.06f, new ConfigDescription("How much of the way from MinSpread to MaxSpread each shot goes (0.06 = about 16 rounds). In multiplayer the server's value applies.", new AcceptableValueRange<float>(0.005f, 1f))));
            SpreadRecovery = Synced.Add(Config.Bind("1 - Machine Gun", "SpreadRecoveryPerSecond", 1.2f, new ConfigDescription("How fast the spread settles when you stop firing (full range per second). In multiplayer the server's value applies.", new AcceptableValueRange<float>(0.1f, 10f))));
            ShotStamina = Synced.Add(Config.Bind("1 - Machine Gun", "StaminaPerShot", 0f, new ConfigDescription("Stamina each round costs. 0 for none (the default while the gun is being tested). In multiplayer the server's value applies.", new AcceptableValueRange<float>(0f, 5f))));
            AimZoom = Config.Bind("2 - Aiming", "Zoom", 1.6f, new ConfigDescription("How far the camera zooms in while you aim (hold the block button: right mouse). 1 for none. Your own setting.", new AcceptableValueRange<float>(1f, 4f)));
            AimSpread = Synced.Add(Config.Bind("2 - Aiming", "SpreadWhileAiming", 0.35f, new ConfigDescription("What share of the spread stays while you aim. In multiplayer the server's value applies.", new AcceptableValueRange<float>(0.05f, 1f))));
            Recoil = Config.Bind("3 - Look and sound", "Recoil", 1f, new ConfigDescription("How much each shot kicks your view up. 0 for none, 1 is the default. Your own setting.", new AcceptableValueRange<float>(0f, 3f)));
            Volume = Config.Bind("3 - Look and sound", "ShotVolume", 1f, new ConfigDescription("How loud the gun is (on top of the game's sound-effects volume). Your own setting.", new AcceptableValueRange<float>(0f, 2f)));
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == DHack.Shared.ServerSettings.ServerMod.Missing;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Things.Register();   // a hot reload while in a world
            ItemReload.Repoint(Things.Current);   // guns made by the copy this replaces
            Things.Refresh();                      // and the ones the game gave their own copy of the data (the give command)
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Aim.Tick();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$guns_server_missing");
            }
        }

        private void LateUpdate() => Aim.Pose();

        private void OnDestroy()
        {
            Aim.Stop();
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
