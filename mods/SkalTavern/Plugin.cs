using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// Ale and mead that get you tipsy, and a toast. Four drinks come from the cauldron (ale, honey mead, blueberry wine, skaldic mead); each
    /// one raises how drunk you are, and that wears off with time. A little drink is warming and gives you a bit of stamina; more and the
    /// world sways and your feet wander; too much and you stagger, and later have a hangover. A mug is drunk in four swigs, and every swig is
    /// a toast: beside friends (or your companions), everyone who raises a cup together gets a short Skal! buff.
    ///
    /// Split across files: Plugin.cs (settings, setup), Drinks.cs (the items, recipes and effects), Swigs.cs (a mug, four swigs), Tipsy.cs
    /// (how drunk you are and what it does), Toast.cs (the toast and its buff).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.skaltavern";
        public const string OldGuid = "com.dhack.skaltavern"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "SkalTavern";
        public const string Version = "0.5.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // the settings the server decides in multiplayer (what drinking costs you, and how long it lasts)

        internal static ConfigEntry<float> SoberMinutes, Sway, Drift, ToastSeconds, Strength;
        internal static ConfigEntry<bool> StumbleOn, PassOutOn, HangoverOn, ScreenFx, Muffle, StarsOn, LeanOn, HiccupsOn, SlurOn, ConfusionOn, PukeOn;

        private Harmony _harmony;
        internal int AwakeFrame;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            AwakeFrame = Time.frameCount;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string ServerSays = " In multiplayer the server's value applies.";
            SoberMinutes = Synced.Add(Config.Bind("Drinking", "MinutesToSober", 8f, new ConfigDescription("How many minutes it takes to sober up from very drunk (100). Longer and you stay tipsy for longer." + ServerSays, new AcceptableValueRange<float>(1f, 60f))));
            Strength = Synced.Add(Config.Bind("Drinking", "EffectStrength", 100f, new ConfigDescription("How strong everything about being drunk is (percent): the picture, the sway, your steering, the sound. 0 for none, 200 for a night you will not remember." + ServerSays, new AcceptableValueRange<float>(0f, 300f))));
            ScreenFx = Config.Bind("Effects", "Picture", true, "The picture changes as you get drunk: dark edges, colour fringing, blur, double vision, colours that drift.");
            Muffle = Config.Bind("Effects", "Sound", true, "Sounds go muffled and wobbly as you get drunk.");
            StarsOn = Config.Bind("Effects", "Stars", true, "Stars circle your head when you are drunk.");
            LeanOn = Config.Bind("Effects", "Weave", true, "Your body leans and weaves when you are drunk.");
            HiccupsOn = Config.Bind("Effects", "Hiccups", true, "Hiccups now and then, and a drunken cheer when you stand still.");
            SlurOn = Config.Bind("Effects", "SlurredChat", true, "What you say in chat comes out slurred when you are drunk.");
            ConfusionOn = Synced.Add(Config.Bind("Effects", "ReversedControls", true, "Sloshed, your controls reverse for a moment now and then." + ServerSays));
            PukeOn = Synced.Add(Config.Bind("Effects", "Puke", true, "Too much drink and you throw up." + ServerSays));
            Sway = Config.Bind("Drinking", "ScreenSway", 100f, new ConfigDescription("How much the view sways when you are drunk (percent). 0 for none.", new AcceptableValueRange<float>(0f, 200f)));
            Drift = Synced.Add(Config.Bind("Drinking", "FeetDrift", 100f, new ConfigDescription("How much your walking wanders when you are drunk (percent). 0 for none." + ServerSays, new AcceptableValueRange<float>(0f, 200f))));
            StumbleOn = Synced.Add(Config.Bind("Drinking", "Stumble", true, "Very drunk, you stagger now and then." + ServerSays));
            PassOutOn = Synced.Add(Config.Bind("Drinking", "PassOut", true, "Too much drink knocks you down (and sobers you a little)." + ServerSays));
            HangoverOn = Synced.Add(Config.Bind("Drinking", "Hangover", true, "After a big night you wake with a hangover: slower stamina and health for a few minutes." + ServerSays));
            ToastSeconds = Synced.Add(Config.Bind("Toast", "Window", 6f, new ConfigDescription("Every swig of a drink is a toast with whoever is near. How many seconds apart two toasts can be and still count as together: friends who drink together (and your companions) get a Skal! buff." + ServerSays, new AcceptableValueRange<float>(2f, 20f))));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Drinks.Register(); // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();   // (notices joining and leaving a server, for the settings it decides)
            Toast.UpdateNetwork();
            Tools.Update();
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Tipsy.Tick(player, Time.deltaTime);
            Body.Tick(player, Time.deltaTime);
            Sound.Tick(Tipsy.Level);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Toast.Unregister();
            Tools.Unregister();
            Tipsy.Clear();
            Fx.Reset();
            Drinks.Unregister();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the drinks are registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix() => Drinks.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() => Drinks.Register();
    }

    // The game swaps in another ObjectDB's item lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB
    {
        private static void Postfix() => Drinks.Register();
    }
}
