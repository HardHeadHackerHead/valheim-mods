using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The rules a contest can be fought under, each worth more: bare fists (no weapon or shield), no food or potions, hard (more of them, a star
    /// more), and against the clock (each round in 90 seconds).
    /// </summary>
    internal static class Rules
    {
        internal static bool Fists, NoFood, Hard, Timed;
        internal const float RoundSeconds = 90f;

        // What you were holding before the fight took it, to put back after.
        private static readonly List<ItemDrop.ItemData> Held = new List<ItemDrop.ItemData>();

        internal static float MultiplierOf(bool fists, bool noFood, bool hard, bool timed) => 1f + (fists ? 0.6f : 0f) + (noFood ? 0.3f : 0f) + (hard ? 0.5f : 0f) + (timed ? 0.4f : 0f);
        internal static float Multiplier => MultiplierOf(Fists, NoFood, Hard, Timed);

        internal static string TextOf(bool fists, bool noFood, bool hard, bool timed)
        {
            var parts = new List<string>();
            if (fists) parts.Add("bare fists");
            if (noFood) parts.Add("no food");
            if (hard) parts.Add("hard");
            if (timed) parts.Add("against the clock");
            return parts.Count == 0 ? "no extra rules" : string.Join(", ", parts);
        }

        internal static string Text() => TextOf(Fists, NoFood, Hard, Timed);

        internal static void Set(bool fists, bool noFood, bool hard, bool timed) { Fists = fists; NoFood = noFood; Hard = hard; Timed = timed; }

        internal static bool IsWeapon(ItemDrop.ItemData item)
        {
            if (item == null) return false;
            ItemDrop.ItemData.ItemType t = item.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Torch || t == ItemDrop.ItemData.ItemType.Tool;
        }

        /// <summary>The start of a bare-fist contest: what is in your hands goes back in the bag.</summary>
        internal static void Strip(Player player)
        {
            Held.Clear();
            if (!Fists || player == null) return;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems().ToList())
                if (item.m_equipped && IsWeapon(item)) { Held.Add(item); player.UnequipItem(item, false); }
        }

        internal static void Restore(Player player)
        {
            if (player != null && !player.IsDead())
                foreach (ItemDrop.ItemData item in Held)
                    if (player.GetInventory().ContainsItem(item) && !item.m_equipped) player.EquipItem(item, false);
            Held.Clear();
        }

        internal static void Clear() { Fists = NoFood = Hard = Timed = false; Held.Clear(); }
    }

    /// <summary>In a bare-fist contest, nothing can be taken in hand (a hotkey would otherwise put the axe straight back).</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class Humanoid_EquipItem_Fists
    {
        private static bool Prefix(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Contest.Active || !Rules.Fists || __instance != Player.m_localPlayer || !Rules.IsWeapon(item)) return true;
            __result = false;
            Hud.Say("Bare fists: no weapons in this contest.");
            return false;
        }
    }

    /// <summary>In a no-food contest, nothing is eaten or drunk.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
    internal static class Player_ConsumeItem_NoFood
    {
        private static bool Prefix(Player __instance, ref bool __result)
        {
            if (!Contest.Active || !Rules.NoFood || __instance != Player.m_localPlayer) return true;
            __result = false;
            Hud.Say("No food or drink in this contest.");
            return false;
        }
    }
}
