using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GearSlots
{
    /// <summary>
    /// The "Gear" panel beside the inventory. It does not make its own item grid: it takes the game's own cells for the extra
    /// inventory rows and moves them here, so every click, drag, tooltip and equip marker behaves exactly like the rest of
    /// the inventory.
    /// </summary>
    internal static class Panel
    {
        private static readonly Color Gold = new Color(0.85f, 0.68f, 0.3f, 1f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.38f);

        private static RectTransform _panel;
        private static TMP_Text _title;
        private static readonly Dictionary<Slot, TMP_Text> Labels = new Dictionary<Slot, TMP_Text>();
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> ElementsField =
            AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private const float Pad = 10f, TitleHeight = 24f;

        public static void Update(InventoryGrid grid)
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null) return;

            List<InventoryElement> elements = ElementsField(grid);
            Inventory inv = grid.GetInventory();
            int width = inv.GetWidth(), normal = Layout.NormalRows;
            if (elements == null || elements.Count < width * (normal + Layout.ExtraRows) || elements[0] == null) return;

            // Keep the ordinary grid exactly as tall as it is without this mod.
            grid.m_gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, normal * grid.m_elementSpace);

            bool show = Plugin.Instance.ShowPanel;
            EnsurePanel(gui, elements[0]);
            _panel.gameObject.SetActive(show);

            float pitch = grid.m_elementSpace;
            Vector2 cell = elements[0].GetElementRectTransform() != null ? elements[0].GetElementRectTransform().rect.size : new Vector2(pitch - 6f, pitch - 6f);
            if (cell.x < 8f) cell = new Vector2(pitch - 6f, pitch - 6f);

            // Cells in the extra rows that no slot uses stay hidden.
            for (int row = 0; row < Layout.ExtraRows; row++)
                for (int x = 0; x < width; x++)
                {
                    InventoryElement el = elements[(normal + row) * width + x];
                    if (el != null && Layout.At(x, normal + row) == null) el.gameObject.SetActive(false);
                }

            foreach (Slot slot in Layout.All)
            {
                InventoryElement el = elements[(normal + slot.Row) * width + slot.Col];
                if (el == null) continue;
                var rt = el.transform as RectTransform;
                if (rt.parent != _panel)
                {
                    Vector2 size = rt.rect.size.x > 8f ? rt.rect.size : cell;
                    rt.SetParent(_panel, false);
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.sizeDelta = size;
                    rt.localScale = Vector3.one;
                }
                el.gameObject.SetActive(show);
                rt.anchoredPosition = new Vector2(Pad + slot.Col * pitch, -(Pad + TitleHeight + slot.Row * pitch));

                // Quick slots show their key where the hotbar shows its numbers.
                var binding = el.transform.Find("binding");
                TMP_Text bindingText = binding != null ? binding.GetComponent<TMP_Text>() : null;
                if (bindingText != null)
                {
                    bool quick = slot.Quick >= 0;
                    bindingText.enabled = quick;
                    if (quick) bindingText.text = Plugin.Instance.QuickKeyName(slot.Quick);
                }

                // The slot's name, written faintly in the empty cell.
                if (!Labels.TryGetValue(slot, out TMP_Text label) || label == null)
                {
                    Labels[slot] = label = MakeLabel(rt, slot.Label, el.m_amount);
                    label.fontSize = 11f;
                    label.alignment = TextAlignmentOptions.Bottom;
                    AddSilhouette(rt, slot, rt.rect.size);
                }
                Transform icon = rt.Find("GearSlotsIcon");
                if (icon != null) icon.gameObject.SetActive(Layout.ItemIn(inv, slot) == null);
                if (label.transform.parent != rt) label.transform.SetParent(rt, false);
                label.enabled = Layout.ItemIn(inv, slot) == null;
            }

            float panelWidth = Pad * 2f + (Layout.Columns - 1) * pitch + cell.x;
            float panelHeight = Pad * 2f + TitleHeight + (Layout.ExtraRows - 1) * pitch + cell.y;
            _panel.sizeDelta = new Vector2(panelWidth, panelHeight);
            Place(gui.m_player, panelWidth, panelHeight);
        }

        private static void EnsurePanel(InventoryGui gui, InventoryElement sample)
        {
            if (_panel != null) return;
            var go = new GameObject("GearSlotsPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panel = go.GetComponent<RectTransform>();
            _panel.SetParent(gui.m_player, false);
            _panel.SetAsFirstSibling(); // behind the cells we move in
            _panel.pivot = new Vector2(0f, 1f);

            var bg = go.GetComponent<Image>();
            Image wood = FindWoodPanel(gui.m_player);
            var outline = go.GetComponent<Outline>();
            if (wood != null)
            {
                // Same wooden board as the inventory itself.
                bg.sprite = wood.sprite; bg.type = wood.type; bg.color = wood.color; bg.material = wood.material;
                bg.pixelsPerUnitMultiplier = wood.pixelsPerUnitMultiplier;
                outline.enabled = false;
            }
            else
            {
                bg.color = new Color(0.07f, 0.055f, 0.045f, 0.92f);
                outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }

            _title = MakeLabel(_panel, "Gear", sample.m_amount);
            _title.fontSize = 17f;
            _title.color = Gold;
            _title.alignment = TextAlignmentOptions.TopLeft;
            var trt = _title.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(Pad, 0f); trt.offsetMax = new Vector2(-Pad, -5f);
        }

        /// <summary>The biggest picture directly on the inventory window: its wooden background.</summary>
        private static Image FindWoodPanel(RectTransform inventory)
        {
            Image best = null; float area = 0f;
            foreach (Image img in inventory.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || img.transform == _panel || (_panel != null && img.transform.IsChildOf(_panel))) continue;
                if (img.GetComponentInParent<InventoryElement>() != null) continue;
                Rect r = img.rectTransform.rect;
                if (r.width * r.height > area) { area = r.width * r.height; best = img; }
            }
            return best;
        }

        private static Sprite IconOf(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || ObjectDB.instance == null) return null;
            GameObject go = ObjectDB.instance.GetItemPrefab(prefab);
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.GetIcon() : null;
        }

        /// <summary>A dark outline of an item in the empty slot, so you can tell what goes there at a glance.</summary>
        private static void AddSilhouette(Transform parent, Slot slot, Vector2 size)
        {
            Sprite icon = IconOf(slot.Icon);
            if (icon == null) return;
            var go = new GameObject("GearSlotsIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            var img = go.GetComponent<Image>();
            img.sprite = icon; img.raycastTarget = false; img.preserveAspect = true;
            img.color = new Color(0f, 0f, 0f, 0.35f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size * 0.62f;
            rt.anchoredPosition = new Vector2(0f, 4f);
        }

        private static TMP_Text MakeLabel(Transform parent, string text, TMP_Text font)
        {
            var go = new GameObject("GearSlotsLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.text = text;
            t.fontSize = 12f;
            t.color = Dim;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return t;
        }

        /// <summary>Room the QualityOfLife buttons (Sort, Stack to chests) take under the inventory, so the panel sits below them.</summary>
        private static float ReservedBelow() =>
            System.AppDomain.CurrentDomain.GetData("DHack.QoL.UnderInventoryHeight") is float h ? h : 0f;

        /// <summary>Left of the inventory when there is room on screen, otherwise just below it.</summary>
        private static void Place(RectTransform inventory, float width, float height)
        {
            _panel.anchorMin = _panel.anchorMax = inventory.pivot; // so anchoredPosition is in the inventory's own coordinates
            Rect r = inventory.rect;
            var corners = new Vector3[4];
            inventory.GetWorldCorners(corners);
            float scale = inventory.lossyScale.x > 0f ? inventory.lossyScale.x : 1f;
            bool room = corners[0].x - (width + Plugin.Instance.PanelGap) * scale >= 0f;

            Vector2 pos = room
                ? new Vector2(r.xMin - width - Plugin.Instance.PanelGap, r.yMax)
                : new Vector2(r.xMin, r.yMin - Plugin.Instance.PanelGap - ReservedBelow());
            _panel.anchoredPosition = pos + Plugin.Instance.PanelOffset;
        }

        /// <summary>Hand every moved cell back to the main grid and remove the panel (when the mod is unloaded). The grid then rebuilds its cells itself.</summary>
        public static void Dispose(bool restoreCells)
        {
            InventoryGui gui = InventoryGui.instance;
            if (restoreCells && gui != null && gui.m_playerGrid != null && _panel != null)
            {
                InventoryGrid grid = gui.m_playerGrid;
                foreach (InventoryElement el in ElementsField(grid))
                    if (el != null && el.transform.parent == _panel) { el.transform.SetParent(grid.m_gridRoot, false); el.gameObject.SetActive(true); }
                AccessTools.Field(typeof(InventoryGrid), "m_width").SetValue(grid, -1); // different from the inventory's width, so it rebuilds
            }
            if (_panel != null) Object.Destroy(_panel.gameObject);
            _panel = null; _title = null;
            Labels.Clear();
        }
    }
}
