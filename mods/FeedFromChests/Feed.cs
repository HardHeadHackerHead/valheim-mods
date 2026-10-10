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

        /// <summary>Automatic feeding: take only from the chests, never from the player's own inventory.</summary>
        public static bool ChestsOnly;
        public static List<Container> Chests = new List<Container>();

        /// <summary>
        /// The item (by name) already taken out of a chest for this add, or null. The station's "use one" takes this one: it was paid for
        /// before the station was asked, so nothing goes in that the chests didn't give.
        /// </summary>
        public static string Prepaid;

        public static bool Applies(Inventory inventory)
        {
            if (!Active) return false;
            Player p = Player.m_localPlayer;
            return p != null && inventory == p.GetInventory();
        }
    }

    // "Do I have any of this?" -> also true for the one we took out of a chest for this add.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
    internal static class Inventory_HaveItem
    {
        private static void Postfix(Inventory __instance, string name, ref bool __result)
        {
            if (!Feed.Applies(__instance)) return;
            if (Feed.ChestsOnly) { __result = Feed.Prepaid == name; return; } // not your own items
            if (!__result) __result = Feed.Prepaid == name;
        }
    }

    // "Use up 1 of this" -> the one taken out of a chest for this add, else your inventory, then the chests. Runs after every other mod's
    // prefix, and does nothing when one of them has already paid (skipped the method).
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased, bool __runOriginal)
        {
            if (!__runOriginal) return false; // another mod has paid it
            if (!Feed.Applies(__instance)) return true;

            if (Feed.Prepaid != null && Feed.Prepaid == name && amount > 0)
            {
                Feed.Prepaid = null; // used: the station has its item
                if (--amount <= 0) return false;
            }

            int own = Feed.ChestsOnly ? 0 : FeedFromChests.Chests.OwnCount(__instance, name);
            if (own >= amount) return true; // your inventory has enough: the game does it as usual

            // (stations use one at a time, which the item taken beforehand covers; this is only for one that asks for more)
            int short_ = amount - own;
            int taken = FeedFromChests.Chests.Take(Feed.Chests, name, short_);
            if (taken < short_) Plugin.Instance?.Log($"A station used {short_ - taken} {name} more than the chests could give");
            amount = own;
            return own > 0;
        }
    }

    // Some stations insist the item is in your inventory before they accept it. Our stand-in items aren't, so say "yes, removed".
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveOneItem), typeof(ItemDrop.ItemData))]
    internal static class Inventory_RemoveOneItem
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(ItemDrop.ItemData item, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            if (item == null || item.m_customData == null || !item.m_customData.ContainsKey(Feed.StandInTag)) return true;
            __result = true;
            return false;
        }
    }
}
