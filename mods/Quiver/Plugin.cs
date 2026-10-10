using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Quiver
{
    /// <summary>
    /// Arrows you shoot that hit the ground, a wall or a creature can be picked up again (some break), and a quiver sits on your back while
    /// you have arrows equipped, with the arrows you carry sticking out of it.
    ///
    /// Split across files: Plugin.cs (settings, setup), Recovery.cs (arrows that can be picked up), QuiverView.cs (the quiver on your back).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.quiver";
        public const string Name = "Quiver";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced; // settings the server decides in multiplayer

        internal static ConfigEntry<bool> Recover, Bolts, FireBurns, ShowQuiver, Debug;
        internal static ConfigEntry<float> GroundChance, CreatureChance, Back, Side, Height, TiltSide, TiltBack;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string ServerNote = " In multiplayer the server's value applies.";
            Recover = Synced.Add(Config.Bind("Arrows", "PickUpArrows", true, "Arrows you shoot that hit something land on the ground as arrows you can pick up again." + ServerNote));
            GroundChance = Synced.Add(Config.Bind("Arrows", "ChanceOnGround", 1f, new ConfigDescription("The chance an arrow that hit the ground, a tree, a wall or the like is kept. 1 keeps every one." + ServerNote, new AcceptableValueRange<float>(0f, 1f))));
            CreatureChance = Synced.Add(Config.Bind("Arrows", "ChanceOnCreature", 0.75f, new ConfigDescription("The chance an arrow that hit a creature is kept: it drops with that creature's loot when it dies. 0.75 keeps three in four." + ServerNote, new AcceptableValueRange<float>(0f, 1f))));
            Bolts = Synced.Add(Config.Bind("Arrows", "IncludeBolts", true, "Crossbow bolts can be picked up too." + ServerNote));
            FireBurns = Synced.Add(Config.Bind("Arrows", "FireArrowsBurnUp", true, "Fire arrows are always used up." + ServerNote));

            Debug = Config.Bind("Arrows", "LogHits", false, "Write what each arrow hit did (kept, broke, why not) to the BepInEx log. For finding out why an arrow did not come back.");
            ShowQuiver = Config.Bind("Quiver", "Show", true, "A quiver on your back while you have arrows (or bolts) equipped.");
            Back = Config.Bind("Quiver", "Back", 0.2f, new ConfigDescription("How far behind your spine the quiver hangs (metres).", new AcceptableValueRange<float>(0.05f, 0.4f)));
            Side = Config.Bind("Quiver", "Side", 0.1f, new ConfigDescription("How far to your right it hangs (negative: left).", new AcceptableValueRange<float>(-0.3f, 0.3f)));
            Height = Config.Bind("Quiver", "Height", -0.03f, new ConfigDescription("How far up it hangs (negative: lower).", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            TiltSide = Config.Bind("Quiver", "TiltSide", 22f, new ConfigDescription("How far the top leans to your right (degrees).", new AcceptableValueRange<float>(-45f, 45f)));
            TiltBack = Config.Bind("Quiver", "TiltBack", 8f, new ConfigDescription("How far the top leans backwards (degrees).", new AcceptableValueRange<float>(-45f, 45f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void Update()
        {
            Synced?.Update(); // notices joining and leaving a server, for the settings it decides
            Stuck.Tick();
            Player player = Player.m_localPlayer;
            if (player != null && !BetterArchery.QuiverOn && player.GetComponent<QuiverView>() == null) player.gameObject.AddComponent<QuiverView>();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Stuck.Clear();
            foreach (QuiverView view in FindObjectsOfType<QuiverView>()) { view.Clear(); Destroy(view); }   // (a reload: nothing of this copy left on the player)
            Synced?.Dispose();
        }
    }

    /// <summary>
    /// BetterArchery does both jobs too: its arrows can be picked up again (on by default) and it hangs its own quiver on your back (on by
    /// default). Doing them twice would give two arrows back for one, and two quivers. Looked up the first time it matters (in a world),
    /// not when the mod starts: mods loaded later, and every mod ScriptEngine loads, aren't listed yet then.
    /// </summary>
    internal static class BetterArchery
    {
        private static bool _checked, _arrows, _quiver;

        /// <summary>BetterArchery's retrievable arrows are on: ours stand down.</summary>
        public static bool ArrowsOn { get { Check(); return _arrows; } }

        /// <summary>BetterArchery's quiver is on: ours isn't drawn.</summary>
        public static bool QuiverOn { get { Check(); return _quiver; } }

        private static void Check()
        {
            if (_checked || Player.m_localPlayer == null) return;
            _checked = true;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("ishid4.mods.betterarchery", out PluginInfo info)) return;
            _arrows = Setting(info, "Retrievable Arrows", "Enable Retrievable Arrows");
            _quiver = Setting(info, "Quiver", "Enable Quiver");
            if (_arrows) Plugin.Log.LogInfo("BetterArchery's retrievable arrows are on: arrows are picked up its way, and this mod's arrow recovery is off");
            if (_quiver) Plugin.Log.LogInfo("BetterArchery's quiver is on: this mod's quiver is not drawn");
        }

        // On when it says so, or when it can't be read (its default is on).
        private static bool Setting(PluginInfo info, string section, string key)
        {
            ConfigEntry<bool> entry = null;
            return info.Instance == null || !info.Instance.Config.TryGetEntry(section, key, out entry) || entry.Value;
        }
    }
}
