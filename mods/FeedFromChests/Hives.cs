using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// Beehives: one honey every 20 minutes, at most 4, and a full hive stops making any until it is emptied. So hives near you empty
    /// themselves into a chest: one assigned to honey or to Food (the chest assign menu, K), or else one that already holds honey.
    /// With no such chest the honey stays in the hive, as in the game. Looking at a hive says when the next honey comes, or why none is
    /// coming (wrong biome, too much cover). Production itself is the game's: only the emptying is ours.
    /// </summary>
    internal static class Hives
    {
        private static readonly List<Beehive> All = new List<Beehive>();
        private static float _next, _summaryAt = -100f;

        public static void Register(Beehive h) { if (h != null && !All.Contains(h)) All.Add(h); }
        public static void Seed() { foreach (Beehive h in Object.FindObjectsOfType<Beehive>()) Register(h); }
        public static void Clear() => All.Clear();

        public static int Honey(Beehive h) => View(h)?.GetZDO().GetInt(ZDOVars.s_level) ?? 0;
        private static ZNetView View(Beehive h) { ZNetView v = h != null ? h.GetComponent<ZNetView>() : null; return v != null && v.IsValid() ? v : null; }
        public static bool BiomeOk(Beehive h) => (Heightmap.FindBiome(h.transform.position) & h.m_biome) != 0;

        public static float Cover(Beehive h)
        {
            global::Cover.GetCoverForPoint(h.m_coverPoint.position, out float cover, out bool _);
            return cover;
        }

        /// <summary>Seconds until the next honey (counting only time already recorded on the hive).</summary>
        public static float NextIn(Beehive h)
        {
            ZNetView v = View(h);
            return v == null ? 0f : Mathf.Max(0f, h.m_secPerUnit - v.GetZDO().GetFloat(ZDOVars.s_product));
        }

        /// <summary>Called every frame; every few seconds, hives this player runs put their honey into chests.</summary>
        public static void Tick(Plugin plugin, Player player, float range, float radius)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            All.RemoveAll(h => h == null);
            float max = range * range;
            var report = new List<string>();
            foreach (Beehive h in All.ToArray())
            {
                if ((h.transform.position - player.transform.position).sqrMagnitude > max) continue;
                ZNetView view = View(h);
                if (view == null) continue;
                int honey = Honey(h);
                report.Add($"{honey}/{h.m_maxHoney}" + (!BiomeOk(h) ? " (wrong biome)" : Cover(h) >= h.m_maxCover ? $" (too covered: {Cover(h):P0})" : ""));
                if (honey <= 0) continue;
                if (!view.IsOwner()) { if (view.HasOwner()) continue; view.ClaimOwnership(); } // exactly one player empties each hive
                int stored = Store(plugin, h, honey, radius);
                if (stored > 0) view.GetZDO().Set(ZDOVars.s_level, honey - stored);
            }
            if (report.Count > 0 && Time.unscaledTime - _summaryAt > 60f)
            {
                _summaryAt = Time.unscaledTime;
                plugin.Info($"Beehives near you: {report.Count}; honey {string.Join(", ", report.ToArray())}");
            }
        }

        /// <summary>Put the hive's honey into chests (as many as the game would hand out). Returns how many hive levels were stored.</summary>
        private static int Store(Plugin plugin, Beehive h, int levels, float radius)
        {
            ItemDrop honey = h.m_honeyItem;
            if (honey == null) return 0;
            int stored = 0;
            for (int i = 0; i < levels; i++)
            {
                int count = Game.instance != null ? Game.instance.ScaleDrops(honey.m_itemData, 1) : 1; // the world's resource rate, as when picked
                int sent = AutoFeed.SendItem(h.transform.position, honey, count, radius, "Food", plugin.Info, Localization.instance.Localize(h.m_name));
                if (sent < count) sent += Chests.AddToChestHolding(h.transform.position, honey, count - sent, radius);
                if (sent < count) break; // nowhere to put it: leave the rest in the hive
                stored++;
            }
            return stored;
        }

        /// <summary>The line added to the hive's hover text: when the next honey comes, or why none will.</summary>
        public static string HoverLine(Beehive h, bool collecting)
        {
            string status;
            if (!BiomeOk(h)) status = "<color=#ff9a66>Bees don't live in this biome</color> (Meadows, Black Forest or Plains only)";
            else if (Cover(h) >= h.m_maxCover) status = $"<color=#ff9a66>Too covered: {Cover(h):P0}</color> (needs under {h.m_maxCover:P0}: move it out from under roofs and trees)";
            else if (Honey(h) >= h.m_maxHoney) status = "<color=#ffc066>Full: it stops making honey until it is emptied</color>";
            else status = $"Next honey in {Mathf.CeilToInt(NextIn(h) / 60f)} min (one every {h.m_secPerUnit / 60f:0} min, up to {h.m_maxHoney})";
            return $"\n<size=14>{status}" + (collecting ? "\nHoney goes into a chest assigned to it or to Food (K), or one that already has honey" : "") + "</size>";
        }
    }

    [HarmonyPatch(typeof(Beehive), "Awake")]
    internal static class Beehive_Awake
    {
        private static void Postfix(Beehive __instance) => Hives.Register(__instance);
    }

    [HarmonyPatch(typeof(Beehive), nameof(Beehive.GetHoverText))]
    internal static class Beehive_GetHoverText
    {
        private static void Postfix(Beehive __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(__result) || !plugin.HiveInfo) return;
            try { __result += Hives.HoverLine(__instance, plugin.CollectsHoney); }
            catch (System.Exception) { }
        }
    }
}
