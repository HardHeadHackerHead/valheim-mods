using HarmonyLib;
using UnityEngine;

namespace GearSlots
{
    /// <summary>
    /// How the extra slots hook into the game. They are plain cells of the player's real inventory (two extra rows under the
    /// ones you see), so saving, weight, crafting and dragging all work the way they do for any item. These patches only
    /// (1) make the inventory that tall, (2) keep the game from auto-filling those cells, (3) refuse items that don't belong
    /// in a slot, and (4) draw those cells in our own panel.
    /// </summary>
    internal static class Patches
    {
        public static void Extend(Inventory inv)
        {
            if (inv == null) return;
            if (!(System.AppDomain.CurrentDomain.GetData("DHack.GearSlots.NormalRows") is int))
                Layout.RememberNormalRows(inv.GetHeight());
            int wanted = Layout.NormalRows + Layout.ExtraRows;
            if (inv.GetHeight() < wanted) inv.SetHeight(wanted);
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class PlayerAwake
        {
            private static void Postfix(Player __instance) => Extend(__instance.GetInventory());
        }

        // How many ordinary rows this character has: the game keeps it in the "invrows" key (rows can be bought from the trader).
        private static int SavedRows(Player player) =>
            player.TryGetUniqueKeyValue(Player.InventoryRowsKey, out string value) && int.TryParse(value, out int rows) ? Mathf.Clamp(rows, 0, 9) : 4;

        // The character's items are loaded before it spawns (positions are not checked then), so take its row count from the save
        // and size the inventory for it straight away. Switching to a character with a different row count lands its gear right.
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class PlayerLoad
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer && Player.m_localPlayer != null) return;
                Layout.RememberNormalRows(SavedRows(__instance));
                Inventory inv = __instance.GetInventory();
                inv.SetHeight(Layout.NormalRows + Layout.ExtraRows);
            }
        }

        // On every spawn (and when a row is bought) the game sets the inventory to its ordinary height and then drops anything
        // below it, which used to throw the gear slots on the ground. Do the same job but keep the gear rows under the ordinary
        // ones, moving them down when a row is added.
        [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
        private static class SetInventorySize
        {
            private static bool Prefix(Player __instance, int rows)
            {
                rows = Mathf.Clamp(rows, 0, 9);
                Inventory inv = __instance.GetInventory();
                int before = Layout.NormalRows;
                if (rows != before)
                    foreach (ItemDrop.ItemData item in inv.GetAllItems())
                    {
                        if (item.m_gridPos.y >= before) item.m_gridPos.y += rows - before;        // gear rows follow the ordinary ones
                        else if (item.m_gridPos.y >= rows) item.m_gridPos = new Vector2i(-1, -1); // a removed ordinary row: dropped, as in the game
                    }
                Layout.RememberNormalRows(rows);
                inv.SetHeight(rows + Layout.ExtraRows);
                __instance.AddUniqueKeyValue(Player.InventoryRowsKey, rows.ToString());
                if (InventoryGui.instance != null) InventoryGui.instance.SetInventorySize(rows);
                __instance.DropInvalidItems();
                return false;
            }
        }

        // ---- the game must not drop loose items into the gear cells ----

        [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
        private static class FindEmptySlot
        {
            private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
            {
                if (!Layout.IsPlayerInventory(__instance) || __result.y < Layout.NormalRows) return;
                __result = Layout.FindFreeNormalCell(__instance, topFirst);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
        private static class HaveEmptySlot
        {
            private static void Postfix(Inventory __instance, ref bool __result)
            {
                if (!Layout.IsPlayerInventory(__instance)) return;
                __result = Layout.NormalItemCount(__instance) < __instance.GetWidth() * Layout.NormalRows;
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
        private static class GetEmptySlots
        {
            private static void Postfix(Inventory __instance, ref int __result)
            {
                if (!Layout.IsPlayerInventory(__instance)) return;
                __result = __instance.GetWidth() * Layout.NormalRows - Layout.NormalItemCount(__instance);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
        private static class CanAddItem
        {
            private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
            {
                if (!__result || !Layout.IsPlayerInventory(__instance)) return;
                if (stack <= 0) stack = item.m_stack;
                int freeCells = __instance.GetWidth() * Layout.NormalRows - Layout.NormalItemCount(__instance);
                int stackSpace = (int)AccessTools.Method(typeof(Inventory), "FindFreeStackSpace").Invoke(__instance, new object[] { item.m_shared.m_name, item.m_worldLevel });
                __result = stackSpace + freeCells * item.m_shared.m_maxStackSize >= stack;
            }
        }

        // ---- dragging an item onto (or out of) a gear cell ----

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
        private static class DropItem
        {
            private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
            {
                Inventory target = __instance.GetInventory();

                // Onto a gear slot: the item has to belong there.
                if (Layout.IsPlayerInventory(target))
                {
                    Slot slot = Layout.At(pos.x, pos.y);
                    if (slot != null && !Layout.Fits(slot, item)) return Refuse(item, slot, out __result);
                }

                // Out of a gear slot onto an item that would be swapped back into it: that item has to fit too.
                if (Layout.IsPlayerInventory(fromInventory))
                {
                    Slot origin = Layout.At(item.m_gridPos.x, item.m_gridPos.y);
                    ItemDrop.ItemData other = target.GetItemAt(pos.x, pos.y);
                    if (origin != null && other != null && other != item && !Layout.Fits(origin, other)) return Refuse(other, origin, out __result);
                }
                return true;
            }

            private static bool Refuse(ItemDrop.ItemData item, Slot slot, out bool result)
            {
                result = false;
                if (Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize(item.m_shared.m_name) + " doesn't go in a " + slot.Label + " slot");
                return false;
            }
        }

        // ---- draw the gear cells in our panel ----

        [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
        private static class UpdateGui
        {
            private static void Postfix(InventoryGrid __instance)
            {
                if (Plugin.Instance == null || !Layout.IsPlayerInventory(__instance.GetInventory())) return;
                Panel.Update(__instance);
            }
        }
    }
}
