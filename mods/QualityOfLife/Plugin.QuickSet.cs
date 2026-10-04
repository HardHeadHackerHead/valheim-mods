using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
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
        private GUIStyle _badgeStyle;

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

        // ---- badges on assigned items -----------------------------------------------------------

        private void DrawQuickSetBadges()
        {
            if (!_quickSetEnabled.Value || !_showBadges.Value || Event.current.type != EventType.Repaint) return;
            Player player = Player.m_localPlayer;
            InventoryGui gui = InventoryGui.instance;
            if (player == null || gui == null || !InventoryGui.IsVisible() || gui.m_playerGrid == null) return;

            var elements = ElementsField?.GetValue(gui.m_playerGrid) as IList;
            if (elements == null) return;

            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _badgeStyle.normal.textColor = new Color(0.1f, 0.07f, 0.02f);
            }

            Inventory inventory = player.GetInventory();
            int width = inventory.GetWidth();
            string label = _quickSetKey.Value.MainKey.ToString();
            if (label.Length > 5) label = "*";

            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!IsQuick(item)) continue;
                int index = item.m_gridPos.y * width + item.m_gridPos.x;
                if (index < 0 || index >= elements.Count) continue;

                var element = elements[index] as InventoryElement;
                if (element == null) continue;

                // The slot's on-screen rectangle (works for both overlay and camera canvases).
                RectTransform rect = element.GetElementRectTransform();
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                Camera cam = rect.GetComponentInParent<Canvas>()?.worldCamera;
                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

                // Small gold tab in the slot's top-left corner (IMGUI's y runs top-down).
                var badge = new Rect(bottomLeft.x + 2f, Screen.height - topRight.y + 2f, 15f + 6f * (label.Length - 1), 15f);
                GUI.DrawTexture(badge, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                                new Color(0.95f, 0.78f, 0.3f, 0.95f), Vector4.zero, new Vector4(4f, 4f, 4f, 4f));
                GUI.Label(badge, label, _badgeStyle);
            }
        }
    }
}
