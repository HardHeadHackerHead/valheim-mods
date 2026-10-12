using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// The game stacks items by name, quality and world level alone and copies m_customData to both halves of a split, so the swigs kept on a
    /// stack (Swigs.Key) would be lost or made up: a half-drunk mug dropped onto a full stack takes the full stack's state (swigs gained), and a
    /// split gives both halves the same swigs left. The rule here: a stack with swigs missing (half-drunk) never joins another stack, and
    /// nothing joins it; only full stacks stack with full stacks. A split keeps the half-drunk top mug on the half that stays, and the new half
    /// starts full. Only our own drinks are touched.
    /// </summary>
    internal static class Stacks
    {
        internal static bool Full(ItemDrop.ItemData item) => Swigs.Left(item) >= Swigs.PerMug;

        /// <summary>Two stacks of our drinks may merge only if neither has swigs missing.</summary>
        internal static bool MayMerge(ItemDrop.ItemData a, ItemDrop.ItemData b) =>
            !Swigs.IsDrink(a) || !Swigs.IsDrink(b) || (Full(a) && Full(b));

        /// <summary>The item Inventory.AddItem is placing, while it looks for a stack to join.</summary>
        private static ItemDrop.ItemData _adding;

        /// <summary>True while a part of a stack is being dropped on the ground (Humanoid.DropItem).</summary>
        private static bool _droppingPart;

        private static readonly AccessTools.FieldRef<Inventory, List<ItemDrop.ItemData>> Items =
            AccessTools.FieldRefAccess<Inventory, List<ItemDrop.ItemData>>("m_inventory");

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

        /// <summary>The stack an added drink would join: another one that is full too (when it is), or none (then it takes a slot of its own).</summary>
        [HarmonyPatch(typeof(Inventory), "FindFreeStackItem")]
        internal static class Inventory_FindFreeStackItem
        {
            private static void Postfix(Inventory __instance, string name, int quality, float worldLevel, ref ItemDrop.ItemData __result)
            {
                if (_adding == null || __result == null || !Swigs.IsDrink(_adding) || MayMerge(_adding, __result)) return;
                __result = null;
                if (!Full(_adding)) return;   // (a half-drunk mug always takes a slot of its own)
                foreach (ItemDrop.ItemData i in Items(__instance))
                    if (i.m_shared.m_name == name && i.m_quality == quality && i.m_stack < i.m_shared.m_maxStackSize && i.m_worldLevel == worldLevel && Full(i))
                    {
                        __result = i;
                        return;
                    }
            }
        }

        /// <summary>
        /// Moving an amount of a stack to a slot (dragging, a split, a drop onto a stack): the private AddItem(item, amount, x, y, ...).
        /// Onto a stack: refused when it would mix a half-drunk stack with another (it stays in your hand). Into an empty slot with some of the
        /// stack left behind (a split): the new stack starts full; the stack left behind keeps the half-drunk mug. Moving a whole stack, and
        /// loading a saved inventory (the item's stack is used up), never change the key.
        /// </summary>
        [HarmonyPatch]
        internal static class Inventory_AddItem_Amount
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Inventory), "AddItem", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool) });

            private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, int x, int y, ref bool __result, out bool __state)
            {
                __state = false;
                if (item == null || !Swigs.IsDrink(item)) return true;
                ItemDrop.ItemData there = __instance.GetItemAt(x, y);
                if (there == null) { __state = true; return true; }
                if (there == item || MayMerge(item, there)) return true;
                __result = false;
                return false;
            }

            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int x, int y, bool __result, bool __state)
            {
                if (!__state || !__result || item.m_stack <= 0) return;   // (a whole stack moved, or a load: nothing left behind)
                ItemDrop.ItemData made = __instance.GetItemAt(x, y);
                if (made != null && made != item && Swigs.IsDrink(made)) made.m_customData?.Remove(Swigs.Key);
            }
        }

        // Dropping part of a stack on the ground copies the swigs to the dropped part (ItemDrop.DropItem clones the item): it starts full.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
        internal static class Humanoid_DropItem
        {
            private static void Prefix(ItemDrop.ItemData item, int amount, out bool __state)
            {
                __state = _droppingPart;
                _droppingPart = item != null && amount > 0 && amount < item.m_stack && Swigs.IsDrink(item);
            }
            private static void Finalizer(bool __state) => _droppingPart = __state;
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        internal static class ItemDrop_DropItem
        {
            private static void Postfix(ItemDrop __result)
            {
                if (_droppingPart && __result != null && Swigs.IsDrink(__result.m_itemData)) __result.m_itemData.m_customData?.Remove(Swigs.Key);
            }
        }

        /// <summary>
        /// Drinks lying on the ground: ItemDrop.AutoStackItems (many items about) joins them by name and quality alone. For our drinks this does
        /// the same, but only between full stacks.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), "AutoStackItems")]
        internal static class ItemDrop_AutoStackItems
        {
            private static readonly AccessTools.FieldRef<ItemDrop, bool> HaveStacked = AccessTools.FieldRefAccess<ItemDrop, bool>("m_haveAutoStacked");
            private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> NView = AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");
            private static readonly MethodInfo Save = AccessTools.Method(typeof(ItemDrop), "Save");

            private static bool Prefix(ItemDrop __instance)
            {
                ItemDrop.ItemData mine = __instance.m_itemData;
                if (!Swigs.IsDrink(mine)) return true;
                if (mine.m_shared.m_maxStackSize <= 1 || mine.m_stack >= mine.m_shared.m_maxStackSize || HaveStacked(__instance)) return false;
                if (!Full(mine)) return false;   // (a half-drunk stack stays as it is)
                HaveStacked(__instance) = true;
                bool joined = false;
                foreach (Collider c in Physics.OverlapSphere(__instance.transform.position, 4f, LayerMask.GetMask("item")))
                {
                    if (!c.attachedRigidbody) continue;
                    ItemDrop other = c.attachedRigidbody.GetComponent<ItemDrop>();
                    ZNetView view = other != null ? NView(other) : null;
                    if (other == null || other == __instance || !other.m_itemData.m_shared.m_autoStack || view == null || !view.IsValid() || !view.IsOwner()) continue;
                    if (other.m_itemData.m_shared.m_name != mine.m_shared.m_name || other.m_itemData.m_quality != mine.m_quality || !Full(other.m_itemData)) continue;
                    int room = mine.m_shared.m_maxStackSize - mine.m_stack;
                    if (room == 0) break;
                    if (other.m_itemData.m_stack <= room)
                    {
                        mine.m_stack += other.m_itemData.m_stack;
                        joined = true;
                        view.Destroy();
                    }
                }
                if (joined) Save?.Invoke(__instance, null);
                return false;
            }
        }
    }
}
