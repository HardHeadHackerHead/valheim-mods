using System.Collections.Generic;
using UnityEngine;

namespace Recycler
{
    /// <summary>What a piece of gear would return, and whether it can be recycled here at all.</summary>
    internal static class Calc
    {
        internal const string KeepTag = "DHack.QuickStack.Keep"; // set by QualityOfLife's item lock (L)

        internal class Entry { public ItemDrop Res; public double Expected; public int Min, Max; }

        internal class Quote
        {
            public readonly List<Entry> Entries = new List<Entry>();
            public string Blocked;     // null when it can be recycled
            public int Percent;
        }

        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        private static readonly System.Random Dice = new System.Random();

        internal static void ClearCache() => Recipes.Clear();

        /// <summary>The share returned with the Presses near this Recycler, in percent.</summary>
        internal static int Percent(RecyclerStation station)
        {
            int presses = RecyclerPress.CountNear(station.transform.position);
            int bonus = presses >= 2 ? Plugin.Press2Bonus.Value : presses == 1 ? Plugin.Press1Bonus.Value : 0;
            return Mathf.Clamp(Plugin.BasePercent.Value + bonus, 0, 100);
        }

        private static Recipe RecipeOf(ItemDrop.ItemData item)
        {
            string key = item.m_shared.m_name;
            if (!Recipes.TryGetValue(key, out Recipe recipe))
            {
                recipe = ObjectDB.instance != null ? ObjectDB.instance.GetRecipe(item) : null;
                if (recipe != null && (recipe.m_resources == null || recipe.m_resources.Length == 0 || recipe.m_requireOnlyOneIngredient)) recipe = null;
                Recipes[key] = recipe;
            }
            return recipe;
        }

        /// <summary>Is this kind of item something the Recycler takes at all (type allowed in the settings, has a recipe, not locked)?</summary>
        internal static bool Listed(ItemDrop.ItemData item)
        {
            if (item.m_customData != null && item.m_customData.ContainsKey(KeepTag)) return false;
            if (!TypeAllowed(item.m_shared.m_itemType)) return false;
            return RecipeOf(item) != null;
        }

        private static bool TypeAllowed(ItemDrop.ItemData.ItemType t)
        {
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return Plugin.AllowWeapons.Value;
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return Plugin.AllowArmor.Value;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return Plugin.AllowTools.Value;
                default:
                    return false;
            }
        }

        internal static bool Valuable(ItemDrop.ItemData item) => item.m_equipped || item.m_quality >= 2 || HasModData(item);

        /// <summary>
        /// Other mods keep their data on the item itself (a backpack's contents, enchantments): recycling destroys it with the item. The
        /// game keeps nothing there, and the item lock (L) never reaches the Recycler (locked items aren't listed).
        /// </summary>
        internal static bool HasModData(ItemDrop.ItemData item) => item.m_customData != null && item.m_customData.Count > 0;

        internal static Quote Evaluate(ItemDrop.ItemData item, RecyclerStation station)
        {
            var quote = new Quote { Percent = Percent(station) };
            Recipe recipe = RecipeOf(item);
            if (recipe == null) { quote.Blocked = "This can't be recycled."; return quote; }

            if (Plugin.RequireStation.Value && recipe.m_craftingStation != null)
            {
                string stationName = Localization.instance.Localize(recipe.m_craftingStation.m_name);
                CraftingStation near = CraftingStation.HaveBuildStationInRange(recipe.m_craftingStation.m_name, station.transform.position);
                if (near == null) { quote.Blocked = $"Needs a {stationName} nearby."; return quote; }
                int need = recipe.GetRequiredStationLevel(1);
                if (near.GetLevel() < need) { quote.Blocked = $"Needs the {stationName} at level {need}."; return quote; }
            }


            int quality = Mathf.Max(1, item.m_quality);
            double stackShare = item.m_stack / (double)Mathf.Max(1, recipe.m_amount);
            // Levels above the item's own maximum come from the upgrader station (battle idols, a gamble), not from materials: none back.
            int levels = Mathf.Min(quality, Mathf.Max(1, item.m_shared.m_maxQuality));
            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (req.m_resItem == null || req.m_upgraderResource) continue; // (battle idols are only for the upgrader station: never handed out)
                int invested = 0;
                for (int q = 1; q <= levels; q++) invested += req.GetAmount(q);
                double expected = invested * (quote.Percent / 100.0) * stackShare;
                int min = (int)System.Math.Floor(expected);
                int max = Plugin.ChanceRounding.Value && expected - min > 0.001 ? min + 1 : min;
                if (max <= 0) continue;
                quote.Entries.Add(new Entry { Res = req.m_resItem, Expected = expected, Min = min, Max = max });
            }
            if (quote.Entries.Count == 0) quote.Blocked = "Too little material to get anything back.";
            return quote;
        }

        /// <summary>How many of this entry you actually get this time (a leftover fraction is a chance of one more).</summary>
        internal static int Roll(Entry e)
        {
            if (e.Max == e.Min) return e.Min;
            return Dice.NextDouble() < e.Expected - e.Min ? e.Max : e.Min;
        }
    }
}
