using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The game stacks items by name, quality and world level alone (Inventory.FindFreeStackItem when an item is added or picked up, and
    /// dropping one stack onto another in the inventory), so a lent stack and an unlent one of the same thing would merge and one's mark is
    /// gone: a mead the crowd gave you to keep, picked up beside the two lent ones, was taken back with them; lent arrows added to bought
    /// ones were kept. Here an item joins only a stack with the same loan mark (or with none, when it has none). Items that carry no mark,
    /// beside stacks that carry none, are not affected.
    /// </summary>
    internal static class KeepApart
    {
        internal static string Mark(ItemDrop.ItemData item) =>
            item?.m_customData != null && item.m_customData.TryGetValue(Kit.LoanKey, out string v) ? v : null;

        /// <summary>The item Inventory.AddItem is placing, while it looks for a stack to join.</summary>
        private static ItemDrop.ItemData _adding;

        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> Items =
            AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");

        /// <summary>
        /// A new stack of something the arena hands out, marked before it goes in: marked after, a stack the game joined to one already there
        /// would not be (Inventory.AddItem by name returns its own copy then). Null when there is no free slot (nothing is added).
        /// </summary>
        internal static ItemDrop.ItemData AddMarked(Inventory inv, string prefab, int amount, int quality, string loan)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (inv == null || drop == null || !inv.HaveEmptySlot()) return null;   // (with one free slot it always fits: never more than a stack)
            ItemDrop.ItemData item = drop.m_itemData.Clone();
            item.m_dropPrefab = go;
            item.m_stack = Mathf.Clamp(amount, 1, Mathf.Max(1, item.m_shared.m_maxStackSize));
            item.m_quality = Mathf.Clamp(quality, 1, Mathf.Max(1, item.m_shared.m_maxQuality));
            item.m_durability = item.GetMaxDurability();
            item.m_crafterName = "The Arena";
            item.m_worldLevel = (byte)Game.m_worldLevel;
            if (loan != null) item.m_customData[Kit.LoanKey] = loan;
            if (!inv.AddItem(item)) return null;
            return inv.ContainsItem(item) ? item
                 : inv.GetAllItems().LastOrDefault(i => i.m_dropPrefab == go && i.m_quality == item.m_quality && Mark(i) == loan);   // (joined a stack)
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
        internal static class Inventory_AddItem
        {
            private static void Prefix(ItemDrop.ItemData item, out ItemDrop.ItemData __state) { __state = _adding; _adding = item; }
            private static void Finalizer(ItemDrop.ItemData __state) => _adding = __state;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
        internal static class Inventory_AddItem_At
        {
            private static void Prefix(ItemDrop.ItemData item, out ItemDrop.ItemData __state) { __state = _adding; _adding = item; }
            private static void Finalizer(ItemDrop.ItemData __state) => _adding = __state;
        }

        /// <summary>The stack an added item would join: another one with the same mark, or none (then it takes a slot of its own).</summary>
        [HarmonyPatch(typeof(Inventory), "FindFreeStackItem")]
        internal static class Inventory_FindFreeStackItem
        {
            private static void Postfix(Inventory __instance, string name, int quality, float worldLevel, ref ItemDrop.ItemData __result)
            {
                if (_adding == null || __result == null) return;
                string want = Mark(_adding);
                if (Mark(__result) == want) return;
                __result = null;
                foreach (ItemDrop.ItemData i in Items(__instance))
                    if (i.m_shared.m_name == name && i.m_quality == quality && i.m_stack < i.m_shared.m_maxStackSize && i.m_worldLevel == worldLevel && Mark(i) == want)
                    {
                        __result = i;
                        return;
                    }
            }
        }

        /// <summary>A stack dropped on a stack of the same thing with another mark, in the inventory window: refused (it stays in your hand).</summary>
        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        internal static class InventoryGrid_DropItem
        {
            private static bool Prefix(InventoryGrid __instance, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
            {
                ItemDrop.ItemData there = __instance.GetInventory()?.GetItemAt(pos.x, pos.y);
                if (item == null || there == null || there == item || there.m_shared.m_maxStackSize <= 1 || !there.IsSameType(item) || Mark(there) == Mark(item)) return true;
                __result = false;
                return false;
            }
        }
    }
}
