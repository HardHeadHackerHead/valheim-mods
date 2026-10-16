using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// Other mods that move the player at walls, found when a player spawns (mods loaded after this one, and everything the dev loader loads
    /// from scripts, aren't in BepInEx's list at our Awake). Their types are only reached by reflection, so Webslinger loads the same without
    /// them.
    ///
    ///   Parkour (ours, com.quad.parkour): wall runs, wall jumps, climbs, ledges. Its moves come first: no web, zip or crawl starts while
    ///     one runs, nor for a moment after (both mods would set the body's speed in the same step). Read from its static Moves.State.
    ///   Smooth Climbing (HansenbansenTeam): climbs walls with its own key; it flags "IsClimbing" on the player's ZDO. Nothing of ours
    ///     starts while it climbs.
    ///   The game's own grappling hook: no web while it pulls (GrapplingPoint.m_localGrappler).
    /// </summary>
    internal static class Compat
    {
        private static bool _looked;
        private static FieldInfo _parkourState;   // Parkour.Moves.State (an enum: None is 0)
        private static float _parkourAt = -10f;   // the last time a Parkour move was seen running
        private static readonly int SmoothClimbingKey = "IsClimbing".GetStableHashCode();

        public static void Reset()
        {
            _looked = false;
            _parkourState = null;
            _parkourAt = -10f;
        }

        /// <summary>A new body (spawned): look for the other mods again (the newest copy, if one was reloaded).</summary>
        public static void Forget() => _looked = false;

        /// <summary>Another mod is moving the player at a wall now, or just was.</summary>
        public static bool Busy(Player p)
        {
            Look4();
            try
            {
                if (_parkourState != null && Convert.ToInt32(_parkourState.GetValue(null)) != 0) _parkourAt = Time.time;
            }
            catch (Exception) { _parkourState = null; }
            if (Time.time - _parkourAt < 0.25f) return true;
            ZDO zdo = Share.ZdoOf(p);
            if (zdo != null && zdo.GetBool(SmoothClimbingKey)) return true;
            return GrapplingPoint.m_localGrappler != null;
        }

        private static void Look4()
        {
            if (_looked) return;
            _looked = true;
            _parkourState = null;
            Type moves = PluginType("com.quad.parkour", "Parkour.Plugin")?.Assembly.GetType("Parkour.Moves");
            _parkourState = moves?.GetField("State", BindingFlags.Public | BindingFlags.Static);
            if (_parkourState != null) Plugin.Log.LogInfo("Parkour is installed: its moves come first, and webs wait while one runs.");
        }

        /// <summary>A running plugin's type: from BepInEx's list, else (loaded from scripts) the live plugin object of that type.</summary>
        private static Type PluginType(string guid, string typeName)
        {
            if (Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) && info.Instance != null) return info.Instance.GetType();
            // (once per spawn: the newest live copy, after hot reloads the old ones are destroyed)
            BaseUnityPlugin live = UnityEngine.Object.FindObjectsByType<BaseUnityPlugin>(FindObjectsSortMode.None).FirstOrDefault(x => x != null && x.GetType().FullName == typeName);
            return live != null ? live.GetType() : null;
        }
    }
}
