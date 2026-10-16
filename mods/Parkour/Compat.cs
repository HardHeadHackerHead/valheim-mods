using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// Other mods that act at walls and ledges. Each is looked for once, the first time a move could start (mods loaded after this one, and
    /// everything ScriptEngine loads, aren't in BepInEx's list at our Awake), by its BepInEx id, or by one of its types when it was loaded
    /// from scripts. Their types are only ever reached by reflection, so Parkour loads the same without them.
    ///
    ///   Smooth Climbing (HansenbansenTeam, hansenbansen.smoothclimbing): climbs any wall with its own key and pulls up over the top. Our
    ///     wall climb and ledge grab stand down (said once in the log). Wall runs, wall jumps and rolls stay, but never while it climbs or
    ///     plays its pull-up, nor for a moment after: both mods would set the body's speed in the same step.
    ///   Just Vault (igentuman, com.igentuman.justvault): vaults onto a ledge with its own key. Nothing stands down: its vault holds the body
    ///     (kinematic) and Player.CanMove is false meanwhile, which ends or refuses any move of ours. A player who'd rather vault up from a
    ///     ledge than pull up turns our pull-up off (the PullUp setting).
    ///   Climbing (Rabid_Wolf_Studios, com.rabid-wolf.valheim.skills.climbing): only a skill that makes sprinting up steep ground cheaper.
    ///     Nothing overlaps.
    ///   Quad's Dungeons (ours, com.quad.dungeons): a dungeon whose pack has parkour off forbids the moves inside it (they'd skip its
    ///     puzzle). Asked through its public Dungeons.Api.BlocksParkour(Vector3), found by reflection when a player spawns.
    ///   Quad's Webslinger (ours, com.quad.webslinger): swings, zips and wall crawls. While one runs nothing of ours does (both would set the
    ///     body's speed in the same step); while the player flies off a web no ledge grab, wall run or climb starts (its crawl takes
    ///     walls then); and a landing from a web never rolls by itself (it takes no fall damage: an automatic roll only stopped the
    ///     player dead and held the next jump back). A roll on crouch still works. Read from its static Web.State and Web.FlowAt.
    /// </summary>
    internal static class Compat
    {
        public const string SmoothClimbingGuid = "hansenbansen.smoothclimbing";
        public const string JustVaultGuid = "com.igentuman.justvault";
        public const string ClimbingGuid = "com.rabid-wolf.valheim.skills.climbing";

        public static bool SmoothClimbing, JustVault, Climbing;
        private static bool _checked;
        private static MethodInfo _isClimbing, _isMantling;    // Smooth Climbing's public static ClimbingState.IsClimbing(Player), ClimbRig.IsMantling(Player)
        private static readonly int ClimbingKey = "IsClimbing".GetStableHashCode();   // and its flag on the player's ZDO

        public static bool ClimbOff => SmoothClimbing;
        public static bool LedgeOff => SmoothClimbing;

        public static void Reset()
        {
            ForgetDungeons();
            _web = null;
            _webState = _webFlowAt = null;
            _webLookedAt = -10f;
            _checked = false;
            SmoothClimbing = JustVault = Climbing = false;
            _isClimbing = _isMantling = null;
        }

        public static void Check()
        {
            if (_checked) return;
            _checked = true;
            SmoothClimbing = Installed(SmoothClimbingGuid, "Valheim_Climbing_Mod.ClimbingState");
            JustVault = Installed(JustVaultGuid, "JustVault.VaultController");
            Climbing = Installed(ClimbingGuid, null);
            if (SmoothClimbing)
            {
                _isClimbing = Method("Valheim_Climbing_Mod.ClimbingState", "IsClimbing");
                _isMantling = Method("Valheim_Climbing_Mod.ClimbRig", "IsMantling");
                Plugin.Log.LogInfo("Smooth Climbing is installed: Parkour's wall climb and ledge grab are off (it climbs walls and pulls up over the top itself). Wall runs, wall jumps and landing rolls stay, and wait while it climbs.");
            }
            if (JustVault) Plugin.Log.LogInfo("Just Vault is installed: the two take turns (a move ends while it vaults). To vault up from a ledge instead of pulling up, turn Parkour's PullUp setting off.");
            if (Climbing) Plugin.Log.LogInfo("Climbing (the skill) is installed: no overlap with Parkour.");
        }

        /// <summary>Is Smooth Climbing climbing, or pulling the player up over a top, right now?</summary>
        public static bool SmoothClimbingBusy(Player p, ZDO zdo)
        {
            if (!SmoothClimbing) return false;
            if (zdo != null && zdo.GetBool(ClimbingKey)) return true;
            return Ask(_isClimbing, p) || Ask(_isMantling, p);
        }

        public static string Summary() =>
            (SmoothClimbing ? "Smooth Climbing: wall climb and ledge grab off; " : "") + (JustVault ? "Just Vault: taking turns; " : "") + (Climbing ? "Climbing: no overlap; " : "") +
            (!SmoothClimbing && !JustVault && !Climbing ? "none of the climbing mods installed" : "");

        // ---- Quad's Webslinger ----

        private static UnityEngine.Object _web;          // its live plugin (a reload destroys it: then looked for again)
        private static FieldInfo _webState, _webFlowAt;  // Webslinger.Web.State (an enum: None 0, Swing 1, Zip 2, Crawl 3, Fly 4), FlowAt (Time.time)
        private static float _webLookedAt = -10f;

        /// <summary>Webslinger's move now: 0 none (or not installed), 1 swing, 2 zip, 3 crawl, 4 flying off a web.</summary>
        public static int WebMove()
        {
            if (!FindWebslinger()) return 0;
            try { return Convert.ToInt32(_webState.GetValue(null)); }
            catch (Exception) { _webState = null; return 0; }
        }

        /// <summary>A Webslinger move ran within this many seconds (a landing from a web).</summary>
        public static bool WebLately(float seconds)
        {
            if (!FindWebslinger() || _webFlowAt == null) return false;
            try { return Time.time - (float)_webFlowAt.GetValue(null) < seconds; }
            catch (Exception) { _webFlowAt = null; return false; }
        }

        private static bool FindWebslinger()
        {
            if (_web != null && _webState != null) return true;
            if (Time.time - _webLookedAt < 2f) return false;
            _webLookedAt = Time.time;
            _webState = _webFlowAt = null;
            _web = null;
            Type plugin = Chainloader.PluginInfos.TryGetValue("com.quad.webslinger", out BepInEx.PluginInfo info) && info.Instance != null ? info.Instance.GetType() : null;
            UnityEngine.Object live = info?.Instance;
            if (plugin == null)
            {
                // (loaded from scripts: the live plugin object; after a reload the old one is destroyed)
                live = UnityEngine.Object.FindObjectsByType<BepInEx.BaseUnityPlugin>(FindObjectsSortMode.None).FirstOrDefault(x => x != null && x.GetType().FullName == "Webslinger.Plugin");
                plugin = live != null ? live.GetType() : null;
            }
            Type web = plugin?.Assembly.GetType("Webslinger.Web");
            if (web == null) return false;
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            _webState = web.GetField("State", any);
            _webFlowAt = web.GetField("FlowAt", any);
            if (_webState == null) return false;
            _web = live;
            Plugin.Log.LogInfo("Quad's Webslinger is installed: no parkour move while it swings, zips or crawls, no ledge grab or wall run while flying off a web, and no automatic roll landing from one.");
            return true;
        }

        // ---- Quad's Dungeons ----

        private static Func<Vector3, bool> _blocksParkour;
        private static bool _dungeonsLooked;

        /// <summary>Is the player inside a dungeon that forbids parkour? False when Quad's Dungeons isn't installed.</summary>
        public static bool DungeonForbids(Player p)
        {
            if (!_dungeonsLooked)
            {
                _dungeonsLooked = true;
                _blocksParkour = FindDungeons();
            }
            if (_blocksParkour == null || p == null) return false;
            try { return _blocksParkour(p.transform.position); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Asking Quad's Dungeons whether parkour is allowed failed (not asked again): " + e.Message);
                _blocksParkour = null;
                return false;
            }
        }

        public static void ForgetDungeons()
        {
            _dungeonsLooked = false;
            _blocksParkour = null;
        }

        /// <summary>Dungeons.Api.BlocksParkour(Vector3) in the newest loaded copy of Quad's Dungeons (a reload leaves the old one loaded).</summary>
        private static Func<Vector3, bool> FindDungeons()
        {
            try
            {
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies().Reverse())
                {
                    Type api;
                    try { api = asm.GetType("Dungeons.Api", false); }
                    catch (Exception) { continue; }
                    MethodInfo m = api?.GetMethod("BlocksParkour", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Vector3) }, null);
                    if (m == null || m.ReturnType != typeof(bool)) continue;
                    Plugin.Log.LogInfo("Quad's Dungeons is installed: no parkour in its dungeons that forbid it");
                    return (Func<Vector3, bool>)Delegate.CreateDelegate(typeof(Func<Vector3, bool>), m);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Looking for Quad's Dungeons: " + e.Message); }
            return null;
        }

        private static bool Installed(string guid, string typeName)
        {
            if (Chainloader.PluginInfos.ContainsKey(guid)) return true;
            if (typeName == null) return false;
            try { return AccessTools.TypeByName(typeName) != null; }   // (loaded by ScriptEngine: not in BepInEx's list)
            catch (Exception) { return false; }
        }

        private static MethodInfo Method(string type, string name)
        {
            try
            {
                MethodInfo m = AccessTools.TypeByName(type)?.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player) }, null);
                return m != null && m.ReturnType == typeof(bool) ? m : null;
            }
            catch (Exception) { return null; }
        }

        private static bool Ask(MethodInfo m, Player p)
        {
            if (m == null) return false;
            try { return (bool)m.Invoke(null, new object[] { p }); }
            catch (Exception) { return false; }
        }
    }
}
