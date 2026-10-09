using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Rainbows
{
    /// <summary>
    /// A rainbow after the rain. When a good spell of rain ends and the sun is low enough (morning or evening), a rainbow arcs across the sky
    /// opposite the sun, fades in, stays for a few minutes and fades out as the sun climbs. Look up at it and you get a Rainbow's Blessing buff.
    /// Only the sky you see is changed (each player sees their own, from the weather the game already shares), so nothing is saved in the world.
    ///
    /// Split across files: Plugin.cs (settings, setup), Sky.cs (when one comes, and drawing it), Blessing.cs (the buff), Tools.cs (Claude Tools).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.rainbows";
        public const string Name = "Rainbows";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> Enabled, Chime, Buff;
        internal static ConfigEntry<float> Chance, MinRainSeconds, WaitMinutes, ShowMinutes, BuffMinutes, Brightness, Volume, DoubleChance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Enabled = Config.Bind("Rainbow", "Enabled", true, "Rainbows after the rain.");
            Chance = Config.Bind("Rainbow", "Chance", 0.8f, new ConfigDescription("How often a good spell of rain ends in a rainbow (0 never, 1 always), when the sun is low enough to show one.", new AcceptableValueRange<float>(0f, 1f)));
            MinRainSeconds = Config.Bind("Rainbow", "MinRainSeconds", 45f, new ConfigDescription("How long it must have rained (seconds) for the end of it to be worth a rainbow.", new AcceptableValueRange<float>(0f, 600f)));
            WaitMinutes = Config.Bind("Rainbow", "WaitMinutes", 8f, new ConfigDescription("If the sun is too high or too low when the rain ends, how long (minutes) to wait for it to come into place before giving up.", new AcceptableValueRange<float>(0f, 60f)));
            ShowMinutes = Config.Bind("Rainbow", "ShowMinutes", 3f, new ConfigDescription("How long the rainbow stays (minutes), if the sun does not move it out of the sky first.", new AcceptableValueRange<float>(0.5f, 30f)));
            Brightness = Config.Bind("Rainbow", "Brightness", 1f, new ConfigDescription("How strong the colours are.", new AcceptableValueRange<float>(0.2f, 2f)));
            DoubleChance = Config.Bind("Rainbow", "DoubleChance", 0.3f, new ConfigDescription("How often a rainbow is a double one : a fainter second rainbow outside the first, its colours the other way round, and a stronger blessing. 0 never, 1 always.", new AcceptableValueRange<float>(0f, 1f)));
            Chime = Config.Bind("Rainbow", "Chime", true, "A soft chime when a rainbow comes out.");
            Volume = Config.Bind("Rainbow", "ChimeVolume", 0.35f, new ConfigDescription("How loud the chime is.", new AcceptableValueRange<float>(0f, 1f)));
            Buff = Config.Bind("Blessing", "Enabled", true, "Look up at a rainbow and you get Rainbow's Blessing: stamina and health come back faster, and running and jumping cost less stamina. A double rainbow gives a stronger one.");
            BuffMinutes = Config.Bind("Blessing", "Minutes", 5f, new ConfigDescription("How long the blessing lasts (minutes).", new AcceptableValueRange<float>(0.5f, 60f)));

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Blessing.Register();   // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Tools.Update();
            Sky.Tick(Time.deltaTime);
        }

        private void LateUpdate() => Sky.Follow();

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Tools.Unregister();
            Sky.Clear();
            Blessing.Unregister();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the blessing is registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() => Blessing.Register(); }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() => Blessing.Register(); }

    // The game swaps in another ObjectDB's lists when a world loads, which drops what was added to the old ones.
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB { private static void Postfix() => Blessing.Register(); }
}
