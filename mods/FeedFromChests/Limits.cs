using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>What kind of station this is, for choosing which limits make sense.</summary>
    internal enum StationKind { Kiln, Smelter, Mill, Refinery, Spit, Oven, Fermenter, Other }

    /// <summary>
    /// The limits a station's menu offers, in its own words. Only what makes sense for it is shown, and only what is shown is used:
    ///   a target  - "make coal until the chests hold 100" (what it makes is what you count);
    ///   a reserve - "always leave 20 barley in the chests, for planting" (what it uses is also needed for something else);
    ///   a fuel reserve - "always leave 50 coal in the chests" (a smelter must not burn your last coal).
    /// </summary>
    internal class LimitPlan
    {
        public StationKind Kind;
        public bool Target, Reserve, FuelReserve;
        public string TargetLabel = "", TargetHelp = "", ReserveLabel = "", ReserveHelp = "", FuelLabel = "", FuelHelp = "";
        public int DefaultTarget, DefaultReserve, DefaultFuelReserve;
    }

    internal static class Limits
    {
        private static HashSet<string> _seeds;

        /// <summary>Items that are planted (barley, flax, carrots...): a station using them must leave some to replant.</summary>
        public static bool IsSeed(ItemDrop item)
        {
            if (_seeds == null && ZNetScene.instance != null)
            {
                _seeds = new HashSet<string>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null || prefab.GetComponent<Plant>() == null) continue;
                    Piece piece = prefab.GetComponent<Piece>();
                    if (piece == null) continue;
                    foreach (Piece.Requirement r in piece.m_resources)
                        if (r.m_resItem != null) _seeds.Add(r.m_resItem.m_itemData.m_shared.m_name);
                }
            }
            return item != null && _seeds != null && _seeds.Contains(item.m_itemData.m_shared.m_name);
        }

        public static void Forget() => _seeds = null;

        /// <summary>What an input becomes at this station (null if it does not convert anything).</summary>
        public static ItemDrop ProductOf(StationInfo info, ItemDrop input)
        {
            if (info == null || input == null) return null;
            string name = input.gameObject.name;
            switch (info.Component)
            {
                case Smelter s: return s.m_conversion.FirstOrDefault(c => c.m_from != null && c.m_from.gameObject.name == name)?.m_to;
                case CookingStation c: return c.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.gameObject.name == name)?.m_to;
                case Fermenter f: return f.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.gameObject.name == name)?.m_to;
                default: return null;
            }
        }

        private static string Name(ItemDrop item) => item != null ? Localization.instance.Localize(item.m_itemData.m_shared.m_name) : "";

        public static StationKind KindOf(StationInfo info)
        {
            switch (info?.Component)
            {
                case Smelter s:
                    bool fuel = s.m_maxFuel > 0 && s.m_fuelItem != null;
                    if (!fuel) return info.Inputs.Count > 0 && info.Inputs.All(IsSeed) ? StationKind.Mill : StationKind.Kiln;
                    return s.m_fuelItem.m_itemData.m_shared.m_name == "$item_coal" ? StationKind.Smelter : StationKind.Refinery;
                case CookingStation c: return c.m_useFuel ? StationKind.Oven : StationKind.Spit;
                case Fermenter _: return StationKind.Fermenter;
                default: return StationKind.Other;
            }
        }

        /// <summary>The limits for this station, worded for what it makes and uses.</summary>
        public static LimitPlan For(StationInfo info)
        {
            var plan = new LimitPlan { Kind = KindOf(info) };
            var products = info.Inputs.Select(i => ProductOf(info, i)).Where(p => p != null).Distinct().ToList();
            string product = products.Count == 1 ? Name(products[0]) : null;
            string makes = product ?? "each thing it makes";
            string input = info.Inputs.Count == 1 ? Name(info.Inputs[0]) : null;
            string fuel = Name(info.Fuel);
            switch (plan.Kind)
            {
                case StationKind.Kiln:
                    plan.Target = true; plan.DefaultTarget = 100;
                    plan.TargetLabel = $"Make {makes} until the chests hold";
                    plan.TargetHelp = $"It stops loading once your chests (and what is already in the kiln) add up to this much {makes}.";
                    plan.Reserve = true;
                    plan.ReserveLabel = input != null ? $"Always leave this much {input} in the chests" : "Always leave this much of each wood in the chests";
                    plan.ReserveHelp = "Wood it will never use, for building. 0: it may use all of it (up to the target).";
                    break;
                case StationKind.Smelter:
                    plan.FuelReserve = true;
                    plan.FuelLabel = $"Always leave this much {fuel} in the chests";
                    plan.FuelHelp = $"It smelts all the ore it is given; this only keeps some {fuel} back for other uses.";
                    break;
                case StationKind.Mill:
                    plan.Reserve = true; plan.DefaultReserve = 20;
                    plan.ReserveLabel = $"Always leave this much {input ?? "of each crop"} in the chests";
                    plan.ReserveHelp = "Kept back for planting, so you never grind your last seeds.";
                    plan.Target = true;
                    plan.TargetLabel = $"Make {makes} until the chests hold";
                    plan.TargetHelp = "0 = no limit.";
                    break;
                case StationKind.Refinery:
                    plan.Target = true;
                    plan.TargetLabel = $"Make {makes} until the chests hold";
                    plan.TargetHelp = "0 = no limit.";
                    plan.FuelReserve = true;
                    plan.FuelLabel = $"Always leave this much {fuel} in the chests";
                    plan.FuelHelp = "0 = it may use all of it.";
                    break;
                case StationKind.Oven:
                    plan.Target = true;
                    plan.TargetLabel = "Bake each kind until the chests hold";
                    plan.TargetHelp = "Counted for each kind of bread or pie on its own. 0 = no limit.";
                    break;
                case StationKind.Spit:
                    plan.Target = true;
                    plan.TargetLabel = "Cook each kind until the chests hold";
                    plan.TargetHelp = "Counted for each cooked food on its own. 0 = cook everything.";
                    plan.Reserve = true;
                    plan.ReserveLabel = "Always leave this much of each raw meat";
                    plan.ReserveHelp = "Kept raw, for taming. 0 = cook all of it.";
                    break;
                case StationKind.Fermenter:
                    plan.Target = true;
                    plan.TargetLabel = "Brew each mead until the chests hold";
                    plan.TargetHelp = "Counted for each mead on its own, so one you have plenty of is skipped. 0 = no limit.";
                    break;
            }
            return plan;
        }

        /// <summary>How much of a product there is: in the chests within <paramref name="radius"/>, plus what is already loaded or waiting at the station.</summary>
        public static int Made(StationInfo info, ItemDrop product, float radius)
        {
            if (product == null) return 0;
            int count = Chests.Count(Chests.Near(info.Position, radius), product.m_itemData.m_shared.m_name);
            ZNetView view = info.Component != null ? info.Component.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return count;
            ZDO zdo = view.GetZDO();
            string to = product.gameObject.name;
            switch (info.Component)
            {
                case Smelter s:
                    int queued = zdo.GetInt(ZDOVars.s_queued, 0);
                    for (int i = 0; i < queued; i++)
                    {
                        string ore = zdo.GetString("item" + i, "");
                        if (s.m_conversion.Any(c => c.m_from != null && c.m_from.gameObject.name == ore && c.m_to != null && c.m_to.gameObject.name == to)) count++;
                    }
                    string ready = zdo.GetString(ZDOVars.s_spawnOre, "");
                    if (s.m_conversion.Any(c => c.m_from != null && c.m_from.gameObject.name == ready && c.m_to != null && c.m_to.gameObject.name == to))
                        count += zdo.GetInt(ZDOVars.s_spawnAmount, 0);
                    break;
                case CookingStation c:
                    for (int i = 0; i < c.m_slots.Length; i++)
                    {
                        string item = zdo.GetString("slot" + i, "");
                        if (item == to || c.m_conversion.Any(x => x.m_from != null && x.m_from.gameObject.name == item && x.m_to != null && x.m_to.gameObject.name == to)) count++;
                    }
                    break;
                case Fermenter f:
                    int content = zdo.GetInt(ZDOVars.s_content, 0);
                    Fermenter.ItemConversion conv = f.m_conversion.FirstOrDefault(x => x.m_from != null && x.m_from.gameObject.name.GetStableHashCode() == content);
                    if (conv != null && conv.m_to != null && conv.m_to.gameObject.name == to) count += conv.m_producedItems;
                    break;
            }
            return count;
        }

        /// <summary>
        /// May the station use one more of this (ticked) input right now? <paramref name="stock"/> is how many the feeding chests hold.
        /// Returns a short line for the menu and the log, e.g. "Wood: 120 in the chests; Coal: 214 of 100 -> not loading (target reached)".
        /// </summary>
        public static bool MayFeed(StationInfo info, AutoSetting setting, LimitPlan plan, ItemDrop input, bool isFuel, int stock, float outRadius, out string line)
        {
            string name = Name(input);
            if (isFuel)
            {
                int keep = plan.FuelReserve ? setting.FuelReserve : 0;
                bool ok = stock > keep;
                line = $"{name} (fuel): {stock} in the chests" + (keep > 0 ? $", leaving {keep}" : "") + (ok ? "  ->  feeding" : stock == 0 ? "  ->  none in the chests" : "  ->  kept back");
                return ok;
            }
            int reserve = plan.Reserve ? setting.Reserve : 0;
            if (stock <= reserve)
            {
                line = $"{name}: {stock} in the chests" + (reserve > 0 ? $", leaving {reserve}" : "") + (stock == 0 ? "  ->  none in the chests" : "  ->  kept back");
                return false;
            }
            if (plan.Target && setting.Target > 0)
            {
                ItemDrop product = ProductOf(info, input);
                int made = Made(info, product, outRadius);
                if (made >= setting.Target)
                {
                    line = $"{name}: {stock} in the chests; {Name(product)}: {made} of {setting.Target}  ->  not loading (enough made)";
                    return false;
                }
                line = $"{name}: {stock} in the chests; {Name(product)}: {made} of {setting.Target}  ->  feeding";
                return true;
            }
            line = $"{name}: {stock} in the chests" + (reserve > 0 ? $", leaving {reserve}" : "") + "  ->  feeding";
            return true;
        }
    }
}
