using System;

namespace FeedFromChests
{
    /// <summary>
    /// Sorts items "least to greatest" the way the game's progression goes (wood, then fine wood, then core wood...). The game has no
    /// quality number on items, so common ones are ranked from a list here; anything not on it comes after, ordered by its trade value.
    /// </summary>
    internal static class Tiers
    {
        // Game item names with the "$item_" prefix and underscores removed, lowest tier first.
        private static readonly string[] Order =
        {
            // wood
            "wood", "finewood", "roundlog", "elderbark", "yggdrasilwood", "blackwood",
            // raw ore and scrap
            "copperore", "tinore", "ironscrap", "silverore", "blackmetalscrap", "flametalore",
            // refined metals
            "copper", "tin", "bronze", "iron", "silver", "blackmetal", "flametal",
            // raw meat, roughly by biome
            "boarmeat", "deermeat", "necktail", "wolfmeat", "haremeat", "chickenmeat", "loxmeat", "serpentmeat", "bugmeat", "asksvinmeat",
        };

        private static string Normalize(string itemName)
        {
            string n = (itemName ?? "").ToLowerInvariant();
            if (n.StartsWith("$item_")) n = n.Substring(6);
            return n.Replace("_", "").Replace(" ", "");
        }

        /// <summary>Lower = lesser. Known items come first in progression order, then the rest by trade value.</summary>
        public static int Rank(ItemDrop.ItemData.SharedData shared)
        {
            int index = Array.IndexOf(Order, Normalize(shared.m_name));
            return index >= 0 ? index : 1000 + shared.m_value;
        }
    }
}
