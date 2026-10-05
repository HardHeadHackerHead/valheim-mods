using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// The short window while we ask a station to take something. During it, the game's "do you have any fuel?" and "use one fuel"
    /// questions about your inventory also look in the nearby chests. Outside that window nothing changes.
    /// </summary>
    internal static class Feed
    {
        public const string StandInTag = "DHack.FeedStandIn";

        public static bool Active;
        public static bool Suspend; // our own inventory work, so we don't recurse into ourselves

        /// <summary>Automatic feeding: take only from the chests, never from the player's own inventory.</summary>
        public static bool ChestsOnly;
        public static List<Container> Chests = new List<Container>();

        /// <summary>
        /// Non-null while a big fill is running. Items to be taken from chests are only counted here and removed in one go at the end,
        /// instead of saving the chest again for every single item.
        /// </summary>
        public static Dictionary<string, int> Reserved;

        public static int ReservedFor(string name) => Reserved != null && Reserved.TryGetValue(name, out int n) ? n : 0;

        public static void Reserve(string name, int amount)
        {
            if (Reserved != null) Reserved[name] = ReservedFor(name) + amount;
        }

        public static bool Applies(Inventory inventory)
        {
            if (!Active || Suspend) return false;
            Player p = Player.m_localPlayer;
            return p != null && inventory == p.GetInventory();
        }
    }

    // "Do I have any of this?" -> also true if a nearby chest does (that we haven't already counted as used).
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
    internal static class Inventory_HaveItem
    {
        private static void Postfix(Inventory __instance, string name, ref bool __result)
        {
            if (!Feed.Applies(__instance)) return;
            if (Feed.ChestsOnly) { __result = FeedFromChests.Chests.Count(Feed.Chests, name) - Feed.ReservedFor(name) > 0; return; } // not your own items
            if (__result) return;
            __result = FeedFromChests.Chests.Count(Feed.Chests, name) - Feed.ReservedFor(name) > 0;
        }
    }

    // "Use up 1 of this" -> from your inventory first, then from the chests.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem
    {
        private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
        {
            if (!Feed.Applies(__instance)) return true;

            if (Feed.ChestsOnly) // automatic feeding: straight from the chests
            {
                if (Feed.Reserved != null) Feed.Reserve(name, amount);
                else FeedFromChests.Chests.Take(Feed.Chests, name, amount);
                return false;
            }

            Feed.Suspend = true;
            try
            {
                int have = __instance.CountItems(name, itemQuality, worldLevelBased);
                if (have >= amount) return true; // your inventory has enough: the game does it as usual

                if (have > 0) __instance.RemoveItem(name, have, itemQuality, worldLevelBased);

                int fromChests = amount - have;
                if (Feed.Reserved != null) Feed.Reserve(name, fromChests);                    // a big fill: take it from the chests at the end
                else FeedFromChests.Chests.Take(Feed.Chests, name, fromChests);               // a single add: take it now
                return false;
            }
            finally
            {
                Feed.Suspend = false;
            }
        }
    }

    // Some stations insist the item is in your inventory before they accept it. Our stand-in items aren't, so say "yes, removed".
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveOneItem), typeof(ItemDrop.ItemData))]
    internal static class Inventory_RemoveOneItem
    {
        private static bool Prefix(ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null || item.m_customData == null || !item.m_customData.ContainsKey(Feed.StandInTag)) return true;
            __result = true;
            return false;
        }
    }
}
