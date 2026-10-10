using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// Fermenters: when one is empty (and has its roof, without which nothing ferments) it takes a mead base you ticked from a chest
    /// near it; when it is ready it taps itself; and the mead goes into a chest assigned to it or to Potions (K), or one that already
    /// holds it, otherwise it drops as in the game. Loading and tapping go through the fermenter's own code.
    /// </summary>
    internal static class Ferment
    {
        private static readonly List<Fermenter> All = new List<Fermenter>();
        private static readonly AccessTools.FieldRef<Fermenter, bool> HasRoof = AccessTools.FieldRefAccess<Fermenter, bool>("m_hasRoof");
        private static readonly AccessTools.FieldRef<Fermenter, bool> Exposed = AccessTools.FieldRefAccess<Fermenter, bool>("m_exposed");
        private static readonly AccessTools.FieldRef<Fermenter, int> TapItem = AccessTools.FieldRefAccess<Fermenter, int>("m_delayedTapItem");
        private static float _next;

        public enum State { Empty, Fermenting, Ready }

        public static void Register(Fermenter f) { if (f != null && !All.Contains(f)) All.Add(f); }
        public static void Seed() { foreach (Fermenter f in Object.FindObjectsOfType<Fermenter>()) Register(f); }
        public static void Clear() => All.Clear();

        private static ZDO Zdo(Fermenter f) { ZNetView v = f != null ? f.GetComponent<ZNetView>() : null; return v != null && v.IsValid() ? v.GetZDO() : null; }

        public static State StateOf(Fermenter f, out double seconds)
        {
            seconds = 0;
            ZDO zdo = Zdo(f);
            if (zdo == null || zdo.GetInt(ZDOVars.s_content) == 0) return State.Empty;
            long start = zdo.GetLong(ZDOVars.s_startTime, 0L);
            seconds = start == 0L ? 0 : (ZNet.instance.GetTime() - new System.DateTime(start)).TotalSeconds;
            return seconds > f.m_fermentationDuration ? State.Ready : State.Fermenting;
        }

        public static bool CanFerment(Fermenter f) => HasRoof(f) && !Exposed(f);

        /// <summary>What is in it, for the menu and the log: "Empty", "Mead base: Minor healing, 40%", "Ready: 6 Minor healing mead".</summary>
        public static string Line(Fermenter f)
        {
            State state = StateOf(f, out double seconds);
            ZDO zdo = Zdo(f);
            Fermenter.ItemConversion c = zdo != null ? f.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.gameObject.name.GetStableHashCode() == zdo.GetInt(ZDOVars.s_content)) : null;
            string roof = CanFerment(f) ? "" : "   <color=#ff9a66>needs a roof and walls round it to ferment</color>";
            switch (state)
            {
                case State.Empty: return "Empty" + roof;
                case State.Ready: return c != null ? $"Ready: {c.m_producedItems} {Localization.instance.Localize(c.m_to.m_itemData.m_shared.m_name)}" : "Ready";
                default:
                    string name = c != null ? Localization.instance.Localize(c.m_from.m_itemData.m_shared.m_name) : "mead base";
                    return $"{name}: {Mathf.Clamp01((float)(seconds / f.m_fermentationDuration)) * 100f:0}% (about {Mathf.CeilToInt((float)((f.m_fermentationDuration - seconds) / 60.0))} min left)" + roof;
            }
        }

        /// <summary>Called every frame; every few seconds, fermenters this player runs tap themselves when ready and reload when empty.</summary>
        public static void Tick(Plugin plugin, Player player, float range)
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 3f;
            All.RemoveAll(f => f == null);
            float max = range * range;
            foreach (Fermenter f in All.ToArray())
            {
                if ((f.transform.position - player.transform.position).sqrMagnitude > max) continue;
                ZNetView view = f.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                if (!view.IsOwner()) { if (view.HasOwner()) continue; view.ClaimOwnership(); } // exactly one player runs each fermenter
                AutoSetting setting = AutoFeed.Read(f);
                State state = StateOf(f, out _);
                if (state == State.Ready && setting.TakeOff)
                {
                    view.InvokeRPC("RPC_Tap"); // we own it, so it runs right here; the mead appears a moment later
                    plugin.Info($"{Localization.instance.Localize(f.m_name)} at {f.transform.position:F0} was ready and tapped itself");
                }
                else if (state == State.Empty && setting.On && setting.Allowed.Count > 0 && CanFerment(f))
                    plugin.FermentAutoStep(player, f, setting);
            }
        }

        /// <summary>The mead comes out: into chests where it can, the rest dropped as in the game. Returns false when we placed it all.</summary>
        public static bool Deliver(Fermenter f)
        {
            Plugin plugin = Plugin.Instance;
            AutoSetting setting = AutoFeed.Read(f);
            if (plugin == null || !plugin.AutoEnabled || !setting.Output) return true;
            int content = TapItem(f);
            Fermenter.ItemConversion c = f.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.gameObject.name.GetStableHashCode() == content);
            if (c == null || c.m_to == null) return true;

            int count = c.m_producedItems;
            string label = Localization.instance.Localize(f.m_name);
            int stored = AutoFeed.SendItem(f.transform.position, c.m_to, count, plugin.OutputRadius, Cooking.CategoryOf(c.m_to.m_itemData.m_shared), plugin.Info, label);
            if (stored < count) stored += Chests.AddToChestHolding(f.transform.position, c.m_to, count - stored, plugin.OutputRadius);
            if (stored <= 0) return true;                    // nowhere to put it: the game drops it all as usual

            f.m_spawnEffects.Create(f.m_outputPoint.position, Quaternion.identity);
            for (int i = stored; i < count; i++)             // whatever did not fit drops at the tap, as the game does
            {
                ItemDrop item = Object.Instantiate(c.m_to, f.m_outputPoint.position + Vector3.up * 0.3f, Quaternion.identity);
                ItemDrop.OnCreateNew(item);
            }
            return false;
        }
    }

    public partial class Plugin
    {
        /// <summary>Load an empty fermenter with one ticked mead base from the chests near it.</summary>
        internal void FermentAutoStep(Player player, Fermenter fermenter, AutoSetting setting)
        {
            if (!Stations.TryGet(fermenter.gameObject, out StationInfo info, out _)) return;
            List<Container> chests = Chests.Near(info.Position, _autoRadius.Value);
            if (chests.Count == 0) return;
            LimitPlan plan = Limits.For(info);
            AutoFeed.Silent = true;
            try
            {
                foreach (ItemDrop drop in info.Inputs)
                {
                    string name = drop.m_itemData.m_shared.m_name;
                    if (!setting.Allowed.Contains(name) || !Limits.MayFeed(info, setting, plan, drop, false, Chests.Count(chests, name), _outputRadius.Value, out _)) continue;
                    if (AddOne(player, info, drop, false, chests, chestsOnly: true))
                    {
                        Logger.LogInfo($"{Localization.instance.Localize(fermenter.m_name)} at {fermenter.transform.position:F0} took {Localization.instance.Localize(name)} from a chest");
                        break;
                    }
                }
            }
            finally { AutoFeed.Silent = false; }
        }
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class Fermenter_Awake
    {
        private static void Postfix(Fermenter __instance) => Ferment.Register(__instance);
    }

    // The mead coming out of the tap: into chests when it can. After every other mod's prefix, and not when one of them has delivered it.
    [HarmonyPatch(typeof(Fermenter), "DelayedTap")]
    internal static class Fermenter_DelayedTap
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Fermenter __instance, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            try { return Ferment.Deliver(__instance); }
            catch (System.Exception e) { Plugin.Instance?.Log("Could not put the mead in a chest, dropping it instead: " + e.Message); return true; }
        }
    }

    // Looking at a fermenter: whether it reloads and taps itself.
    [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
    internal static class Fermenter_GetHoverText
    {
        private static void Postfix(Fermenter __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || string.IsNullOrEmpty(__result) || !plugin.AutoEnabled) return;
            AutoSetting s = AutoFeed.Read(__instance);
            __result += $"\n<size=14>{Ferment.Line(__instance)}\nAuto-load from chests: {(s.On ? "<color=#8fe388>ON</color>" : "off")}, taps itself when ready: {(s.TakeOff ? "<color=#8fe388>ON</color>" : "off")}</size>";
        }
    }
}
