using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// The Sort button under your inventory: joins split stacks together, then lines everything up by kind (weapons, armor,
    /// food, materials...) and name. Your hotbar row, anything in the GearSlots mod's gear/food/quick slots, and nothing else
    /// is left where it is; no item is ever deleted or dropped.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _sortEnabled;

        private void BindSortConfig()
        {
            _sortEnabled = Config.Bind("Sort", "Enabled", true, "Show a Sort button under your inventory: it joins split stacks and puts items in order. (Respects ProtectHotbar in the QuickStack settings.)");
        }

        /// <summary>How many inventory rows are ordinary ones (the GearSlots mod adds gear rows below them).</summary>
        private static int OrdinaryRows(Inventory inventory) =>
            AppDomain.CurrentDomain.GetData("DHack.GearSlots.NormalRows") is int rows && rows <= inventory.GetHeight() ? rows : inventory.GetHeight();

        // The order kinds of item appear in, then name.
        private static int KindOrder(ItemDrop.ItemData item)
        {
            int index = Array.IndexOf(Categories.All, Categories.Of(item.m_shared));
            return index < 0 ? Categories.All.Length : index;
        }

        private void SortInventory(Player player)
        {
            Inventory inventory = player.GetInventory();
            int rows = OrdinaryRows(inventory), width = inventory.GetWidth();
            int firstRow = _protectHotbar.Value ? 1 : 0;
            if (rows <= firstRow) return;

            List<ItemDrop.ItemData> items = inventory.GetAllItems()
                .Where(i => i.m_gridPos.y >= firstRow && i.m_gridPos.y < rows).ToList();
            if (items.Count == 0) { Tell(player, "Nothing to sort"); return; }

            int stacksBefore = items.Count;
            int totalBefore = inventory.GetAllItems().Sum(i => i.m_stack);

            // 1. Join stacks of the same thing (and the same quality), fullest first.
            var kept = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in items.OrderByDescending(i => i.m_stack))
            {
                if (item.m_shared.m_maxStackSize > 1)
                {
                    foreach (ItemDrop.ItemData target in kept)
                    {
                        if (item.m_stack <= 0) break;
                        if (target.m_shared.m_maxStackSize <= 1 || target.m_stack >= target.m_shared.m_maxStackSize || !target.IsSameType(item)) continue;
                        int n = Mathf.Min(item.m_stack, target.m_shared.m_maxStackSize - target.m_stack);
                        target.m_stack += n;
                        item.m_stack -= n;
                    }
                }
                if (item.m_stack > 0) kept.Add(item);
                else inventory.RemoveItem(item); // emptied into another stack
            }

            // 2. Put them in order.
            List<ItemDrop.ItemData> ordered = kept
                .OrderBy(KindOrder)
                .ThenBy(i => Localization.instance.Localize(i.m_shared.m_name), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(i => i.m_quality)
                .ThenByDescending(i => i.m_stack)
                .ToList();

            int slot = firstRow * width;
            foreach (ItemDrop.ItemData item in ordered)
            {
                item.m_gridPos = new Vector2i(slot % width, slot / width);
                slot++;
            }

            // The inventory updates its weight and tells the rest of the game it changed.
            try { AccessTools.Method(typeof(Inventory), "Changed")?.Invoke(inventory, new object[] { false, false }); }
            catch (Exception) { }

            int totalAfter = inventory.GetAllItems().Sum(i => i.m_stack);
            if (totalAfter != totalBefore) Logger.LogWarning($"SORT CHANGED ITEM COUNT: {totalBefore} -> {totalAfter}");

            int joined = stacksBefore - ordered.Count;
            Tell(player, joined > 0 ? $"Sorted ({joined} stack{(joined == 1 ? "" : "s")} joined)" : "Sorted");
            Logger.LogInfo($"Sorted the inventory: {stacksBefore} stacks -> {ordered.Count}, {totalBefore} items before and {totalAfter} after");
        }
    }
}
