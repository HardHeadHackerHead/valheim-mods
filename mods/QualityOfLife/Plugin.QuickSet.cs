using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// Quick set: tag items in your inventory, then swap to all of them with one key and back with the same key.
    /// The set is stored as a tag on the items themselves (ItemData.m_customData), which the game saves with your
    /// inventory, so it survives restarts and moving items around.
    /// </summary>
    public partial class Plugin
    {
        private const string Tag = "DHack.QuickSwap";

        /// <summary>What we had equipped before the last quick swap, so a second press can put it back.</summary>
        private List<ItemDrop.ItemData> _quickBefore;
        private bool _quickSwapped;

        private static readonly System.Reflection.FieldInfo ElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");

        private static bool IsQuick(ItemDrop.ItemData item) => item.m_customData != null && item.m_customData.ContainsKey(Tag);

        // ---- assigning (inventory open) ---------------------------------------------------------

        /// <summary>The item in YOUR inventory that the mouse (or gamepad cursor) is over, or null.</summary>
        private static ItemDrop.ItemData HoveredItem(Player player)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_playerGrid == null) return null;

            ItemDrop.ItemData item = null;
            if (ZInput.IsGamepadActive()) item = gui.m_playerGrid.GetGamepadSelectedItem();
            if (item == null)
            {
                Vector3 mouse = Input.mousePosition;
                item = gui.m_playerGrid.GetItem(new Vector2i((int)mouse.x, (int)mouse.y));
            }

            // Only things you're carrying: items sitting in a chest don't count.
            return item != null && player.GetInventory().ContainsItem(item) ? item : null;
        }

        private void AssignHovered(Player player)
        {
            ItemDrop.ItemData item = HoveredItem(player);
            if (item == null) return;

            if (IsQuick(item))
            {
                item.m_customData.Remove(Tag);
                Tell(player, $"Removed from quick set: {NameOf(item)}");
            }
            else
            {
                item.m_customData[Tag] = "1";
                Tell(player, $"Added to quick set: {NameOf(item)}");
            }
        }

        // ---- swapping (in the world) ------------------------------------------------------------

        private void SwapQuickSet(Player player)
        {
            // The quick set = every tagged item you're carrying right now (items you've dropped or stored are skipped).
            List<ItemDrop.ItemData> quick = player.GetInventory().GetAllItems().Where(IsQuick).ToList();
            if (quick.Count == 0)
            {
                Tell(player, $"No quick items yet. Open your inventory, hover an item and press {_quickSetKey.Value}.");
                return;
            }

            if (!quick.All(player.IsItemEquiped))
            {
                // Remember what we had on, then equip the set.
                _quickBefore = EquippedNow(player);
                _quickSwapped = true;
                Run(Equip(player, quick, null, "Quick set equipped"));
            }
            else if (_quickSwapped && _quickBefore != null)
            {
                // Everything in the set is already on: go back to what we had before.
                List<ItemDrop.ItemData> back = _quickBefore.Where(i => player.GetInventory().ContainsItem(i)).ToList();
                List<ItemDrop.ItemData> remove = quick.Where(q => !back.Contains(q)).ToList(); // set items we didn't have before
                _quickSwapped = false;
                Run(Equip(player, back, remove, "Back to your previous gear"));
            }
            else
            {
                Tell(player, "Quick set is already equipped.");
            }
        }

        // ---- tags on your inventory's cells ----------------------------------------------------

        private const string QuickBadgeName = "QoLQuickBadge", KeepBadgeName = "QoLKeepBadge";
        private static readonly Color QuickBadgeFill = new Color(0.95f, 0.78f, 0.3f, 0.95f), QuickBadgeText = new Color(0.1f, 0.07f, 0.02f);
        private static readonly Color KeepBadgeFill = new Color(0.65f, 0.15f, 0.15f, 0.95f);
        private readonly HashSet<int> _quickCells = new HashSet<int>();

        /// <summary>
        /// Small tags on the inventory's own cells: a gold one with the quick-set key in the top-left corner of each quick-set item, and a red
        /// "KEEP" in the bottom-left corner of a locked item while the mouse is over it. They are children of the cells, so they move with
        /// them; nothing in them catches the mouse.
        /// </summary>
        private void UpdateBadges(Player player, InventoryGui gui)
        {
            var elements = gui.m_playerGrid != null ? ElementsField?.GetValue(gui.m_playerGrid) as IList : null;
            if (elements == null) return;
            Inventory inventory = player.GetInventory();
            int width = inventory.GetWidth();

            _quickCells.Clear();
            if (_quickSetEnabled.Value && _showBadges.Value)
                foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                    if (IsQuick(item)) _quickCells.Add(item.m_gridPos.y * width + item.m_gridPos.x);
            int keepCell = -1;
            if (_stackEnabled.Value)
            {
                ItemDrop.ItemData hovered = HoveredItem(player);
                if (hovered != null && IsKept(hovered)) keepCell = hovered.m_gridPos.y * width + hovered.m_gridPos.x;
            }
            string key = _quickSetKey.Value.MainKey.ToString();
            if (key.Length > 5) key = "*";

            for (int i = 0; i < elements.Count; i++)
            {
                var cell = elements[i] as InventoryElement;
                if (cell == null) continue;
                Badge(cell.transform, QuickBadgeName, _quickCells.Contains(i), key, true);
                Badge(cell.transform, KeepBadgeName, i == keepCell, "KEEP", false);
            }
        }

        /// <summary>Shows (making it the first time) or hides one tag on a cell. A tag left by a hot reload is reused: it's only Unity parts.</summary>
        private static void Badge(Transform cell, string name, bool show, string text, bool top)
        {
            Transform badge = cell.Find(name);
            if (!show)
            {
                if (badge != null && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
                return;
            }
            if (badge != null)
            {
                TMP_Text label = badge.GetComponentInChildren<TMP_Text>(true);
                if (label == null || label.text != text) { badge.name = "QoLOldBadge"; Destroy(badge.gameObject); badge = null; }   // (the key was changed)
            }
            if (badge == null)
            {
                RectTransform tag = top
                    ? GameUi.Tag(cell, text, new Vector2(2f, -2f), 15f, 11f, QuickBadgeFill, QuickBadgeText)
                    : GameUi.Tag(cell, text, new Vector2(2f, 2f), 13f, 10f, KeepBadgeFill, GameUi.Text, Vector2.zero);
                tag.name = name;
                return;
            }
            if (!badge.gameObject.activeSelf) badge.gameObject.SetActive(true);
        }

        /// <summary>Takes every tag off the inventory's cells (unloading).</summary>
        private static void RemoveBadges(InventoryGui gui)
        {
            var elements = gui != null && gui.m_playerGrid != null ? ElementsField?.GetValue(gui.m_playerGrid) as IList : null;
            if (elements == null) return;
            foreach (object e in elements)
                if (e is InventoryElement cell && cell != null)
                    foreach (Transform child in cell.transform.Cast<Transform>().Where(t => t.name == QuickBadgeName || t.name == KeepBadgeName).ToList())
                        Destroy(child.gameObject);
        }
    }
}
