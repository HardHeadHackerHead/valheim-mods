using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GearSlots
{
    /// <summary>
    /// A second row under the game's hotbar (the 1-8 on screen) showing what is in your Quick slots, with the key to press
    /// where the numbers are. It uses the hotbar's own slot picture, so it looks the same.
    /// </summary>
    internal class Hotbar
    {
        private class Cell
        {
            public GameObject Go;
            public Image Icon;
            public GuiBar Durability;
            public TMP_Text Amount, Binding;
            public GameObject Equipped, Queued;
            public int StackShown = -1;
        }

        private readonly List<Cell> _cells = new List<Cell>();

        // The game's top-left messages ("You are sheltered", "Ate ...") sit where our row goes, so they step down while it shows.
        private RectTransform _messageText, _messageIcon;
        private Vector2 _textHome, _iconHome;
        private bool _messagesMoved;

        public void Update(Player player, Inventory inv)
        {
            HotkeyBar bar = Hud.instance != null ? Hud.instance.GetComponentInChildren<HotkeyBar>(true) : null;
            if (bar == null || bar.m_elementPrefab == null) { Destroy(); return; }
            if (_cells.Count != Layout.QuickCount || _cells[0].Go == null) Build(bar);

            bool hidden = InventoryGui.IsVisible() || !Plugin.Instance.ShowQuickBar;
            bool anyShown = false;
            for (int quick = 0; quick < _cells.Count; quick++)
            {
                Cell cell = _cells[quick];
                Slot slot = Layout.QuickSlot(quick);
                ItemDrop.ItemData item = slot != null ? Layout.ItemIn(inv, slot) : null;
                bool bound = Plugin.Instance.QuickKeyName(quick).Length > 0;

                cell.Go.SetActive(!hidden && item != null && bound);
                if (hidden || item == null || !bound) continue;
                anyShown = true;

                cell.Binding.text = ZInput.IsGamepadActive() ? "" : Plugin.Instance.QuickKeyName(quick);
                cell.Icon.gameObject.SetActive(true);
                cell.Icon.sprite = item.GetIcon();

                bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
                cell.Durability.gameObject.SetActive(worn);
                if (worn)
                {
                    if (item.m_durability <= 0f)
                    {
                        cell.Durability.SetValue(1f);
                        cell.Durability.SetColor(Mathf.Sin(Time.time * 10f) > 0f ? Color.red : new Color(0f, 0f, 0f, 0f));
                    }
                    else
                    {
                        cell.Durability.SetValue(item.GetDurabilityPercentage());
                        cell.Durability.ResetColor();
                    }
                }

                cell.Equipped.SetActive(item.m_equipped);
                cell.Queued.SetActive(player.IsEquipActionQueued(item));
                bool stacks = item.m_shared.m_maxStackSize > 1;
                cell.Amount.gameObject.SetActive(stacks);
                if (stacks && cell.StackShown != item.m_stack)
                {
                    cell.Amount.text = $"{item.m_stack} / {item.m_shared.m_maxStackSize}";
                    cell.StackShown = item.m_stack;
                }
            }

            MoveMessages(anyShown ? bar.m_elementSpace + 6f : 0f);
        }

        /// <summary>Shift the top-left message line (and its icon) down by <paramref name="drop"/>; 0 puts them back where the game keeps them.</summary>
        private void MoveMessages(float drop)
        {
            MessageHud hud = MessageHud.instance;
            if (hud == null || hud.m_messageText == null || hud.m_messageIcon == null) return;
            if (_messageText == null)
            {
                _messageText = hud.m_messageText.rectTransform; _textHome = _messageText.anchoredPosition;
                _messageIcon = hud.m_messageIcon.rectTransform; _iconHome = _messageIcon.anchoredPosition;
            }
            if (drop <= 0f && !_messagesMoved) return;
            _messageText.anchoredPosition = _textHome + new Vector2(0f, -drop);
            _messageIcon.anchoredPosition = _iconHome + new Vector2(0f, -drop);
            _messagesMoved = drop > 0f;
        }

        private void Build(HotkeyBar bar)
        {
            Destroy();
            for (int quick = 0; quick < Layout.QuickCount; quick++)
            {
                var cell = new Cell { Go = Object.Instantiate(bar.m_elementPrefab, bar.transform) };
                cell.Go.name = "GearSlotsQuick" + (quick + 1);
                cell.Go.transform.localPosition = new Vector3(quick * bar.m_elementSpace, -bar.m_elementSpace, 0f); // one row under the hotbar
                Transform t = cell.Go.transform;
                cell.Binding = t.Find("binding").GetComponent<TMP_Text>();
                cell.Icon = t.Find("icon").GetComponent<Image>();
                cell.Durability = t.Find("durability").GetComponent<GuiBar>();
                cell.Amount = t.Find("amount").GetComponent<TMP_Text>();
                cell.Equipped = t.Find("equiped").gameObject;
                cell.Queued = t.Find("queued").gameObject;
                var selected = t.Find("selected");
                if (selected != null) selected.gameObject.SetActive(false);
                _cells.Add(cell);
            }
        }

        public void Destroy()
        {
            if (_messageText != null && _messagesMoved) { _messageText.anchoredPosition = _textHome; _messageIcon.anchoredPosition = _iconHome; }
            _messagesMoved = false; _messageText = _messageIcon = null;
            foreach (Cell cell in _cells) if (cell.Go != null) Object.Destroy(cell.Go);
            _cells.Clear();
        }
    }
}
