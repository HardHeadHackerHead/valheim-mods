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
        /// <summary>Make the inventory tall enough for the gear rows (never lower: other mods may have made it taller still).</summary>
        public static void Extend(Inventory inv)
        {
            if (inv == null || Compat.StandingDown) return;
            if (!(System.AppDomain.CurrentDomain.GetData("DHack.GearSlots.NormalRows") is int) && Player.m_localPlayer != null)
                Layout.RememberNormalRows(Layout.WorkOutBase(Player.m_localPlayer)); // installed (or reloaded) with a player in the world
            int wanted = Layout.NormalRows + Layout.ExtraRows;
            if (inv.GetHeight() < wanted) inv.SetHeight(wanted);
        }

        // The character's items are loaded before it spawns (positions are not checked then), so find where its gear rows are and
        // make the inventory tall enough for them straight away. Other mods (ValheimPlus' extra rows) may already have made it
        // taller: it is only ever raised.
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static class PlayerLoad
        {
            [HarmonyPriority(Priority.First)] // before other slot mods look at those cells
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return; // the main menu's preview, or someone else
                if (Compat.StandingDown)
                {
                    var (moved, left) = Layout.EmptyGearRows(__instance);
                    Plugin.Instance?.StoodDown(moved, left);
                    return;
                }
                Place(__instance, Layout.WorkOutBase(__instance));
            }
        }

        private static Player _placed; // the character whose gear rows were found when it loaded

        private static void Place(Player player, int gearBase)
        {
            Layout.RememberNormalRows(gearBase);
            Layout.SaveBase(player, gearBase);
            Inventory inv = player.GetInventory();
            inv.SetHeight(Mathf.Max(inv.GetHeight(), gearBase + Layout.ExtraRows));
            _placed = player;
        }

        // A brand-new character has nothing to load (the game skips Player.Load), and its first spawn doesn't resize the inventory.
        // Its gear rows go under whatever the game and other mods made it by the end of that spawn (ValheimPlus adds its rows then).
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class OnSpawned
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Compat.StandingDown) return;
                if (__instance != _placed) Place(__instance, __instance.GetInventory().GetHeight());
                // Mods that size the inventory window to the whole height (ValheimPlus does, after its rows) would leave room for the
                // gear rows in it: they show in their own panel, so the window is as tall as the ordinary rows.
                if (InventoryGui.instance != null) InventoryGui.instance.SetInventorySize(Layout.NormalRows);
            }
        }

        // On every spawn (and when a row is bought) the game sets the inventory to its ordinary height and then drops anything below
        // it. The game's method runs as it is, with every other mod's changes to it (ValheimPlus makes it taller); this only notes
        // where the gear rows were, so DropInvalidItems below can move them under the new height before anything is dropped.
        [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
        private static class SetInventorySize
        {
            internal static bool Resizing;
            internal static int OldBase;

            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Compat.StandingDown) return;
                Resizing = true;
                OldBase = Layout.NormalRows;
            }

            // If a mod after us lowered the height again, raise it (anything it dropped is already gone, but nothing more will be).
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer || Compat.StandingDown) return;
                Extend(__instance.GetInventory());
            }

            [HarmonyFinalizer]
            private static void Finalizer() => Resizing = false;
        }

        // The game calls this only from SetInventorySize, right after setting the height. The height it finds here is the new
        // number of ordinary rows (the game's, or what ValheimPlus and the like made it): the gear rows move to just below them and
        // the inventory grows to fit, so the game finds nothing out of place to drop.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropInvalidItems))]
        private static class DropInvalidItems
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Humanoid __instance)
            {
                if (!SetInventorySize.Resizing || __instance != Player.m_localPlayer) return;
                Player player = (Player)__instance;
                Inventory inv = player.GetInventory();
                int oldBase = SetInventorySize.OldBase, newBase = inv.GetHeight();

                var gear = new System.Collections.Generic.List<ItemDrop.ItemData>();
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                    if (item.m_gridPos.y >= oldBase && item.m_gridPos.y < oldBase + Layout.ExtraRows) gear.Add(item);
                foreach (ItemDrop.ItemData item in gear) item.m_gridPos.y += newBase - oldBase; // the gear rows follow the ordinary ones

                Layout.RememberNormalRows(newBase);
                Layout.SaveBase(player, newBase);
                inv.SetHeight(newBase + Layout.ExtraRows);

                // Anything else now below the ordinary rows (a row taken away, when ValheimPlus is removed or set lower) goes into a
                // free cell of the bag. Only if the bag is full is it dropped, as the game would.
                foreach (ItemDrop.ItemData item in inv.GetAllItems())
                {
                    if (gear.Contains(item) || item.m_gridPos.y < newBase) continue;
                    Vector2i free = Layout.FindFreeNormalCell(inv, topFirst: false);
                    item.m_gridPos = free.x >= 0 ? free : new Vector2i(-1, -1);
                }
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
