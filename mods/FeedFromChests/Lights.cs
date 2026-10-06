using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// Torches, sconces and braziers keep themselves lit: whenever one has room for more fuel (resin, coal...) and a chest near it has
    /// some, one is added, through the fire's own "add fuel". It is one setting for the whole mod, not per torch. Campfires and hearths
    /// (which burn wood) have their own setting, off unless you turn it on. Each fire is looked after by one player (whoever's game
    /// runs it), only while they are near, and only from chests (never your inventory).
    /// </summary>
    internal static class Lights
    {
        private static readonly List<Fireplace> All = new List<Fireplace>();
        private static float _next;

        public static void Register(Fireplace f) { if (f != null && !All.Contains(f)) All.Add(f); }
        public static void Seed() { foreach (Fireplace f in Object.FindObjectsOfType<Fireplace>()) Register(f); }
        public static void Clear() => All.Clear();

        /// <summary>A fire that burns wood (campfire, hearth, bonfire) rather than a light (torch, sconce, brazier).</summary>
        public static bool BurnsWood(Fireplace f) => f.m_fuelItem != null && Tiers.IsWood(f.m_fuelItem.m_itemData.m_shared);

        public static bool Refuels(Fireplace f, bool lights, bool fires) =>
            f != null && f.m_canRefill && !f.m_infiniteFuel && f.m_fuelItem != null && (BurnsWood(f) ? fires : lights);

        public static float FuelOf(Fireplace f)
        {
            ZNetView view = f.GetComponent<ZNetView>();
            return view != null && view.IsValid() ? view.GetZDO().GetFloat(ZDOVars.s_fuel) : 0f;
        }

        /// <summary>Called every frame; every couple of seconds, each fire this player runs that has room gets one more fuel from a chest.</summary>
        public static void Tick(Plugin plugin, Player player, float range, bool lights, bool fires)
        {
            if (!lights && !fires) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            All.RemoveAll(f => f == null);
            float max = range * range;
            int near = 0, others = 0, full = 0, tried = 0;
            foreach (Fireplace f in All.ToArray())
            {
                if (!Refuels(f, lights, fires)) continue;
                if ((f.transform.position - player.transform.position).sqrMagnitude > max) continue;
                ZNetView view = f.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                near++;
                if (!view.IsOwner()) { if (view.HasOwner()) { others++; continue; } view.ClaimOwnership(); } // exactly one player runs each fire
                if (Mathf.CeilToInt(FuelOf(f)) >= f.m_maxFuel) { full++; continue; }
                tried++;
                plugin.RefuelFromChests(player, f);
            }
            if (near > 0 && Time.unscaledTime - _summaryAt > 60f)
            {
                _summaryAt = Time.unscaledTime;
                plugin.Info($"Torches near you: {near}; full {full}, looked after by another player's game {others}, topping up {tried}");
            }
        }

        private static float _summaryAt = -100f;
    }

    public partial class Plugin
    {
        /// <summary>Add one fuel to a fire from the chests near it (quietly). Nothing happens if no chest within reach has any.</summary>
        internal void RefuelFromChests(Player player, Fireplace fire)
        {
            if (!Stations.TryGet(fire.gameObject, out StationInfo info, out _) || info.Fuel == null) return;
            List<Container> chests = Chests.Near(info.Position, _autoRadius.Value);
            if (chests.Count == 0 || Chests.Count(chests, info.Fuel.m_itemData.m_shared.m_name) <= 0) return;
            AutoFeed.Silent = true;
            bool added;
            try { added = AddOne(player, info, info.Fuel, true, chests, chestsOnly: true); }
            finally { AutoFeed.Silent = false; }
            if (added && (!_refuelledAt.TryGetValue(fire, out float last) || Time.unscaledTime - last > 60f))
            {
                _refuelledAt[fire] = Time.unscaledTime;
                Logger.LogInfo($"Refuelled {Localization.instance.Localize(fire.m_name)} at {fire.transform.position:F0} with {Localization.instance.Localize(info.Fuel.m_itemData.m_shared.m_name)} from a chest (now {Lights.FuelOf(fire):0.#}/{fire.m_maxFuel})");
            }
        }

        private readonly Dictionary<Fireplace, float> _refuelledAt = new Dictionary<Fireplace, float>();
    }

    [HarmonyPatch(typeof(Fireplace), "Awake")]
    internal static class Fireplace_Awake
    {
        private static void Postfix(Fireplace __instance) => Lights.Register(__instance);
    }

    // Looking at a torch: say that it keeps itself lit from the chests.
    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
    internal static class Fireplace_GetHoverText
    {
        private static void Postfix(Fireplace __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(__result) || !plugin.RefuelsItself(__instance)) return;
            __result += $"\n<size=14>Refuels itself with {Localization.instance.Localize(__instance.m_fuelItem.m_itemData.m_shared.m_name)} from chests within {plugin.FeedRadius:0} m</size>";
        }
    }
}
