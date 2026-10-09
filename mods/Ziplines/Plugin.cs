using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// Ziplines. Build a Zipline Post (with the hammer), build another, press E on one and then on the other, and a rope runs between them.
    /// Press E on either post to ride: you hang from the rope and slide along it, faster downhill, the world rushing past, and land at the other
    /// end. Press jump to let go. Every post keeps its own id and its partner's in its saved data, so the lines come back with the world.
    ///
    /// Split across files: Plugin.cs (settings, setup), Things.cs (registering the post piece), Post.cs (what a post does: linking, hover,
    /// the rope), Line.cs (the shape of a line), Ride.cs (the ride itself).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.ziplines";
        public const string Name = "Ziplines";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        internal static ConfigEntry<float> MaxLength, MinSlope, TopSpeed, MinSpeed, Hang, SpeedScale, WindVolume;
        internal static ConfigEntry<bool> Wind, WideView, LongFaster, NeedAxe;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            MaxLength = Config.Bind("Lines", "MaxLength", 12000f, new ConfigDescription("The longest a zipline can be (metres). A line remembers where its other end is, so the far post does not have to be loaded: the world around you loads as you ride.", new AcceptableValueRange<float>(15f, 20000f)));
            MinSlope = Config.Bind("Lines", "MinSlope", 0.005f, new ConfigDescription("How much a line must fall to be ridden, as a part of its length (0.005 is 5 m in every 1000). A line only runs downhill, from its higher post to its lower.", new AcceptableValueRange<float>(0.001f, 0.1f)));
            LongFaster = Config.Bind("Riding", "LongLinesFaster", true, "The longer the line, the faster you go (up to eight times), so a line of kilometres takes minutes, not an hour. You slow down for the last stretch either way.");
            TopSpeed = Config.Bind("Riding", "TopSpeed", 24f, new ConfigDescription("How fast you go downhill at the most (metres per second).", new AcceptableValueRange<float>(6f, 40f)));
            MinSpeed = Config.Bind("Riding", "MinSpeed", 5f, new ConfigDescription("How fast you go on the flat or uphill at the least (metres per second).", new AcceptableValueRange<float>(2f, 15f)));
            Hang = Config.Bind("Riding", "HangBelowRope", 2.5f, new ConfigDescription("How far below the rope your feet hang (metres): your arms reach up to the handle, so this is about your height plus a little.", new AcceptableValueRange<float>(1.5f, 3.5f)));
            NeedAxe = Config.Bind("Riding", "NeedAnAxe", true, "You hook an axe over the rope and hang from its handle, so you need one with you to ride. Off, you hang from a wooden triangle instead.");
            SpeedScale = Config.Bind("Riding", "SpeedPercent", 50f, new ConfigDescription("How fast the whole ride is, as a percent of the standard speed (50 is half as fast, 200 twice).", new AcceptableValueRange<float>(10f, 300f)));
            WindVolume = Config.Bind("Riding", "WindVolume", 0.12f, new ConfigDescription("How loud the wind is at full speed (0 to 1).", new AcceptableValueRange<float>(0f, 1f)));
            WideView = Config.Bind("Riding", "WideView", true, "The view widens as you speed up, for the feel of it.");
            Wind = Config.Bind("Riding", "WindSound", true, "The sound of the wind, rising with your speed.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register(); // a hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Ride.Tick();
            Tools.Update();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Ride.End("The ziplines were reloaded.");
            Tools.Unregister();
            Post.ForgetAll();
            Things.Unregister();
            if (Instance == this) Instance = null;
        }
    }

    // Whichever of ZNetScene and ObjectDB wakes first, the post is registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() => Things.Register();
    }

    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
    internal static class ObjectDB_CopyOtherDB
    {
        private static void Postfix() => Things.Register();
    }
}
