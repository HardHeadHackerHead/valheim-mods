using System;
using System.Collections.Generic;
using HarmonyLib;

namespace CraftFromChests
{
    // Announce the mod when you spawn into a world (first launch; reloads announce from Plugin.Awake).
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class Player_OnSpawned
    {
        private static void Postfix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) Plugin.Announce("loaded");
        }
    }

    // Track every chest that exists in the world.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) => ChestScanner.Register(__instance);
    }

    // "How many do I have?" -> include nearby chests. Drives the recipe list, requirement icons and Craft button.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems), typeof(string), typeof(int), typeof(bool))]
    internal static class Inventory_CountItems
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (name == null || !ChestScanner.Applies(__instance)) return;
            __result += ChestScanner.CountInChests(name, quality, matchWorldLevel);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
    internal static class Inventory_HaveItem
    {
        private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            if (__result || !ChestScanner.Applies(__instance)) return;
            __result = ChestScanner.CountInChests(name, -1, matchWorldLevel) > 0;
        }
    }

    // Mark the window in which crafting actually consumes materials. (Restores what it was: DoCrafting below may have set it already.)
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class Player_ConsumeResources
    {
        private static void Prefix(out bool __state) { __state = ChestScanner.Consuming; ChestScanner.Consuming = true; }
        private static void Finalizer(bool __state) => ChestScanner.Consuming = __state;
    }

    // Pressing Craft. The game hands over the item first and charges after, so this is where a craft the chests can no longer pay for
    // (someone emptied one since the window counted it) is refused: by the time the game charges it's too late. It also marks "any one
    // of these" recipes (meads, food) as paying, because the game charges those with its own RemoveItem here, not ConsumeResources.
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class InventoryGui_DoCrafting
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Player player, Recipe ___m_craftRecipe, ItemDrop.ItemData ___m_craftUpgradeItem, bool ___m_multiCrafting,
                                   int ___m_multiCraftAmount, bool __runOriginal, out bool __state)
        {
            __state = ChestScanner.Consuming;
            if (!__runOriginal) return false; // another mod already said no
            if (player == null || ___m_craftRecipe == null || !ChestScanner.Applies(player.GetInventory())) return true;
            if (player.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)) return true; // nothing will be charged

            // The same amounts the game is about to charge (InventoryGui.DoCrafting, Player.ConsumeResources).
            int quality = ___m_craftUpgradeItem == null ? 1 : ___m_craftUpgradeItem.m_quality + 1;
            int multiplier = ___m_multiCrafting ? ___m_multiCraftAmount : 1;
            var needs = new List<ChestScanner.Need>();
            if (___m_craftRecipe.m_requireOnlyOneIngredient)
            {
                ItemDrop.ItemData single;
                int need;
                try { ___m_craftRecipe.GetAmount(quality, out need, out single, multiplier); }
                catch (Exception) { return true; } // the game will fail the same way and craft nothing
                if (single == null) return true;   // nothing to pay with: the game refuses by itself
                needs.Add(new ChestScanner.Need { Name = single.m_shared.m_name, Amount = need, Quality = single.m_quality });
            }
            else
            {
                CraftingStation station = player.GetCurrentCraftingStation();
                foreach (Piece.Requirement req in ___m_craftRecipe.m_resources)
                {
                    if (!ChestScanner.Charged(req, station)) continue;
                    int amount = req.GetAmount(quality) * multiplier;
                    if (amount > 0) needs.Add(new ChestScanner.Need { Name = req.m_resItem.m_itemData.m_shared.m_name, Amount = amount, Quality = -1 });
                }
            }

            string missing = ChestScanner.FindShortfall(player.GetInventory(), needs);
            if (missing != null)
            {
                player.Message(MessageHud.MessageType.Center, $"Not enough {Localization.instance.Localize(missing)} in the chests any more");
                return false;
            }
            ChestScanner.Consuming = true;
            return true;
        }

        private static void Finalizer(bool __state) => ChestScanner.Consuming = __state;
    }

    // "Any one of these" recipes: the game picks the ingredient you have enough of and reads its quality from your own stack. When the
    // chests make up the amount but you carry none, it would get nothing back and fail; hand it one of the chests' items to read instead.
    [HarmonyPatch(typeof(Player), nameof(Player.GetFirstRequiredItem))]
    internal static class Player_GetFirstRequiredItem
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Player __instance, Inventory inventory, Recipe recipe, int qualityLevel, int craftMultiplier,
                                    ref int amount, ref int extraAmount, ref ItemDrop.ItemData __result)
        {
            if (__result != null || recipe == null || !ChestScanner.Applies(inventory)) return;
            CraftingStation station = __instance.GetCurrentCraftingStation();
            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (!ChestScanner.Charged(req, station)) continue;
                int need = req.GetAmount(qualityLevel) * craftMultiplier;
                string name = req.m_resItem.m_itemData.m_shared.m_name;
                for (int q = 1; q <= req.m_resItem.m_itemData.m_shared.m_maxQuality; q++)
                {
                    if (inventory.CountItems(name, q) < need) continue; // (counts the chests)
                    ItemDrop.ItemData sample = ChestScanner.FindInChests(name, q);
                    if (sample == null) continue;
                    amount = need;
                    extraAmount = req.m_extraAmountOnlyOneIngredient;
                    __result = sample;
                    return;
                }
            }
        }
    }

    // While crafting: take from the player's inventory first, then top up from nearby chests. Runs after every other mod's prefix: one
    // that pays from somewhere else (Adventure Backpacks pays from the backpack) lowers the amount or skips the method, and only what's
    // left is ours to find.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased, bool __runOriginal)
        {
            if (!__runOriginal) return false; // another mod has paid all of it
            if (!ChestScanner.Consuming || !ChestScanner.Applies(__instance)) return true;

            int own = ChestScanner.OwnCount(__instance, name, itemQuality, worldLevelBased);
            if (own >= amount) return true; // player has enough, run vanilla

            int left = ChestScanner.RemoveFromChests(name, amount - own, itemQuality, worldLevelBased);
            if (left > 0) Plugin.Log.LogWarning($"Crafting: the chests were {left} {name} short after all"); // (checked just before, so it shouldn't happen)
            amount = own; // the game takes the rest from the inventory
            return own > 0;
        }
    }
}
