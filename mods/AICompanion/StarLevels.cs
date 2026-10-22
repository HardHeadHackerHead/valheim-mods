using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// StarLevelSystem (MidnightMods) sets up every creature but players a moment after it appears (a Character.Awake postfix), our
    /// companion too. Its spawn multiplier (1.1 in every biome by default) then made a second one from the prefab one time in ten: a
    /// "Companion" with nothing saved on it (no name, no player, no looks: bald, "someone's companion"), at every summon, wake and Bring
    /// back. Its spawn reducer (a rate below 1, or a creature on a biome's "spawns disabled" list) destroys a creature instead, a companion
    /// with all it carries; its Splitter modifier makes copies of a creature as it dies; and it gave companions star levels (more damage
    /// and size) and its biome stats (half as much again from poison, by default).
    ///
    /// So StarLevelSystem now treats a companion as it treats a player: its setup queue turns it away (as it turns players away), and
    /// whatever it works out for one is not kept, so its damage, speed and size patches find nothing for it (as for a player). Its code
    /// is patched by reflection once a world loads, and only when it is installed. In case that ever fails (a new version), its own marks
    /// are on the companion as well: "managed by another mod" (never multiplied, never deleted, its API's two bools) and "None" as its
    /// modifiers (no Splitter). Stars or a size it gave a companion before 0.25.1 are taken back.
    /// </summary>
    internal static class StarLevels
    {
        public const string PluginId = "MidnightsFX.StarLevelSystem";
        private const string Managed = "SLS_EXTMGD", Multiplied = "SLS_MULT", Modifiers = "SLS_MODV2", NoModifiers = "{\"None\": \"Minor\"}", Size = "SLS_SIZE";

        private static bool _hooked;
        private static MethodInfo _clear;

        /// <summary>A world is loading (every mod is loaded by now): patch StarLevelSystem if it is installed. Once.</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            try
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(PluginId, out PluginInfo info) || info.Instance == null) return;
                Assembly sls = info.Instance.GetType().Assembly;
                MethodInfo enqueue = AccessTools.Method(sls.GetType("StarLevelSystem.modules.CreatureSetup.CreatureSetupQueue"), "Enqueue");
                Type cache = sls.GetType("StarLevelSystem.Data.CompositeLazyCache");
                MethodInfo build = AccessTools.Method(cache, "GetAndSetLocalCache");
                _clear = AccessTools.Method(cache, "ClearCachedCreature", new[] { typeof(Character) });
                Harmony harmony = Plugin.Instance?.Harmony;
                if (harmony == null || enqueue == null || build == null || _clear == null)
                {
                    Plugin.Instance?.Warn("StarLevelSystem found, but not the parts to keep it off companions (a new version?): it may give them star levels; they still keep its spawn multiplier off them");
                    return;
                }
                harmony.Patch(enqueue, prefix: new HarmonyMethod(typeof(StarLevels), nameof(NotSetUp)));
                harmony.Patch(build, postfix: new HarmonyMethod(typeof(StarLevels), nameof(NotKept)));
                Plugin.Instance?.Note("StarLevelSystem found: it leaves companions as they are (no copies, star levels or modifiers), as it does players");
            }
            catch (Exception e) { Plugin.Instance?.Warn("Keeping StarLevelSystem off companions: " + e.Message); }
        }

        /// <summary>Its setup queue: a companion is turned away, as a player is (false, nothing queued).</summary>
        private static bool NotSetUp(Character __0, ref bool __result)
        {
            if (!(__0 is Humanoid h) || !Companion.Is(h)) return true;
            __result = false;
            return false;
        }

        /// <summary>What it worked out for a companion is not kept (as for a player: its other patches look it up and find nothing).</summary>
        private static void NotKept(Character __0)
        {
            if (!(__0 is Humanoid h) || !Companion.Is(h)) return;
            try { _clear?.Invoke(null, new object[] { __0 }); } catch { }
        }

        /// <summary>Its marks on a companion, on the game running it (as it is made, and once for each one from before 0.25.1).</summary>
        public static void Exempt(Humanoid c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner()) return;
            ZDO z = view.GetZDO();
            if (z.GetBool(Managed, false) && z.GetString(Modifiers, "") == NoModifiers && c.GetLevel() <= 1) return;
            if (z.GetInt(ZDOVars.s_level, 0) <= 0) z.Set(ZDOVars.s_level, 1); // (a level it has: StarLevelSystem rolls one for a creature without)
            z.Set(Managed, true);
            z.Set(Multiplied, true);
            z.Set(Modifiers, NoModifiers);
            if (c.GetLevel() > 1)
            {
                Plugin.Instance?.Note($"{Companion.NameOf(c)} had {c.GetLevel() - 1} star(s) from StarLevelSystem: back to none");
                c.SetLevel(1);   // (a player's level: its health is its food's, Food, from its next tick)
            }
            if (z.GetVec3(Size, Vector3.zero) != Vector3.zero)
            {
                z.Set(Size, Vector3.zero);   // (its "no size of mine"; a removed key would stay on other games)
                c.transform.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// A companion with no player and no name is a copy another mod made of one (every companion we make gets both at once, since the
        /// first version): it can never be given orders, so it goes. Anything put in its bag goes into a tombstone where it stood (anyone
        /// can open it). On the game running it. True when it was removed.
        /// </summary>
        public static bool RemoveStray(Humanoid c)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || !view.IsOwner()) return false;
            ZDO z = view.GetZDO();
            if (z.GetLong(Keys.Master, 0L) != 0L || !string.IsNullOrEmpty(z.GetString(Keys.Name, ""))) return false;
            Container gear = c.GetComponent<Container>();
            if (gear != null && !Containers.Take(gear)) return false; // (someone has its bag open: next time)
            int kept = gear != null && gear.GetInventory().NrOfItems() > 0 ? Companion.DropGear(c) : 0;
            Plugin.Instance?.Note($"Removed a companion with no player and no name at {c.transform.position:F0} (a copy another mod made, such as StarLevelSystem's spawn multiplier)"
                                  + (kept > 0 ? $"; {kept} item stacks from its bag are in a tombstone there" : ""));
            ZNetScene.instance.Destroy(c.gameObject);
            return true;
        }
    }

    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class Character_Awake_StarLevels
    {
        private static void Postfix(Character __instance) { if (__instance is Humanoid h && Companion.Is(h)) StarLevels.Exempt(h); }
    }
}
