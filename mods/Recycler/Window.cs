using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Recycler
{
    /// <summary>
    /// The Recycler's window, in the game's own look (GameUi.cs): your recyclable gear on the left in the inventory's own cells, what the
    /// chosen piece returns on the right, and the Recycle button. It is built again whenever what it shows changes.
    /// </summary>
    internal static class Window
    {
        private const float W = 860f, H = 580f, Pad = 30f, ListW = 470f;

        internal static bool IsOpen => _window != null;
        internal static float ClosedAt = -1f;   // (the E that closes it mustn't open it again)

        private static RectTransform _window, _content;
        private static RecyclerStation _station;
        private static readonly List<ItemDrop.ItemData> Rows = new List<ItemDrop.ItemData>();
        private static ItemDrop.ItemData _selected;
        private static Calc.Quote _quote;
        private static float _nextRefresh, _openedAt;
        private static bool _pending;
        private static ItemDrop.ItemData _confirmFor;
        private static float _confirmUntil;
        private static string _shown = "";

        internal static void Open(RecyclerStation station)
        {
            if (!GameUi.Ready()) return;
            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Close;
            _station = station;
            _selected = null; _quote = null; _confirmFor = null;
            _openedAt = Time.unscaledTime;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            Refresh(Player.m_localPlayer);
            Build(true);
        }

        internal static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _content = null;
            _station = null; _selected = null; _quote = null; _pending = false; _shown = "";
            Rows.Clear();
            Calc.ClearCache();
            UITooltip.HideTooltip();
            ClosedAt = Time.unscaledTime;
        }

        internal static void Tick()
        {
            if (_window == null) { if (_station != null) Close(); return; }   // (the game rebuilt its interface: a world change)
            Player player = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");   // E closes it again, as it does a chest
            if (player == null || player.IsDead() || _station == null || Menu.IsVisible() || Input.GetKeyDown(KeyCode.Escape) || useAgain ||
                (_station.transform.position - player.transform.position).sqrMagnitude > 6f * 6f)
            {
                Close();
                return;
            }
            if (_pending) { _pending = false; Recycle(player); }
            if (Time.unscaledTime >= _nextRefresh) Refresh(player);
            if (Signature() != _shown) Build(false);
        }

        // ---- data -------------------------------------------------------------------------------------

        private static void Refresh(Player player)
        {
            _nextRefresh = Time.unscaledTime + 0.4f;
            Rows.Clear();
            if (player == null) return;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                if (Calc.Listed(item)) Rows.Add(item);
            Rows.Sort((a, b) => string.Compare(Localization.instance.Localize(a.m_shared.m_name), Localization.instance.Localize(b.m_shared.m_name),
                System.StringComparison.OrdinalIgnoreCase));
            if (!Rows.Contains(_selected)) _selected = null;
            _quote = _selected != null && _station != null ? Calc.Evaluate(_selected, _station) : null;
        }

        /// <summary>Everything the window shows, so it is built again only when something changed.</summary>
        private static string Signature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_station != null ? Calc.Percent(_station) : 0).Append('|').Append(Confirming).Append('|').Append(_selected?.GetHashCode() ?? 0);
            foreach (ItemDrop.ItemData item in Rows)
                sb.Append('|').Append(item.GetHashCode()).Append(',').Append(item.m_stack).Append(',').Append(item.m_quality).Append(',')
                  .Append(item.m_equipped).Append(',').Append((int)(item.GetDurabilityPercentage() * 50f));
            if (_quote != null) sb.Append('|').Append(_quote.Blocked).Append(string.Join(",", _quote.Entries.Select(e => e.Min + "-" + e.Max)));
            return sb.ToString();
        }

        /// <summary>A number in gold inside a translated line: the line's {0} is the number alone, so each language puts "%" where it goes.</summary>
        private static string Gold(int n) => $"<color=#ffd27a>{n}</color>";

        private static bool Valuable => _selected != null && Plugin.ConfirmValuable.Value && Calc.Valuable(_selected);
        private static bool Confirming => Valuable && _confirmFor == _selected && Time.unscaledTime < _confirmUntil;

        private static void Select(ItemDrop.ItemData item)
        {
            _selected = item; _confirmFor = null;
            _quote = _station != null ? Calc.Evaluate(item, _station) : null;
        }

        private static void Recycle(Player player)
        {
            ItemDrop.ItemData item = _selected;
            if (item == null || _station == null || !player.GetInventory().ContainsItem(item)) return;
            Calc.Quote quote = Calc.Evaluate(item, _station);
            if (quote.Blocked != null) return;

            string name = Localization.instance.Localize(item.m_shared.m_name);
            if (item.m_equipped) player.UnequipItem(item, false);
            // Pay first: only an item that really left the inventory gives anything back (another mod can refuse to let it go, e.g. a
            // backpack that still holds things).
            if (!player.GetInventory().RemoveItem(item) || player.GetInventory().ContainsItem(item))
            {
                player.Message(MessageHud.MessageType.Center, T("recycler_kept", name));
                Refresh(player);
                return;
            }

            var said = new List<string>();
            foreach (Calc.Entry e in quote.Entries)
            {
                int amount = Calc.Roll(e);
                if (amount <= 0) continue;
                Give(player, e.Res.gameObject, amount);
                said.Add($"{amount} {Localization.instance.Localize(e.Res.m_itemData.m_shared.m_name)}");
            }
            _station.MarkWorking();
            RecyclerStation.PlayEffects(_station.transform.position + Vector3.up);
            if (Plugin.ShowMessages.Value)
                player.Message(MessageHud.MessageType.TopLeft, said.Count > 0 ? T("recycler_recycled", name, string.Join(", ", said)) : T("recycler_recycled_nothing", name));

            _selected = null; _quote = null; _confirmFor = null;
            Refresh(player);
        }

        private static void Give(Player player, GameObject prefab, int amount)
        {
            Inventory inventory = player.GetInventory();
            int max = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            while (amount > 0)
            {
                int n = Mathf.Min(amount, max);
                if (inventory.CanAddItem(prefab, n)) inventory.AddItem(prefab, n);
                else Object.Instantiate(prefab, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity).GetComponent<ItemDrop>().SetStack(n);
                amount -= n;
            }
        }

        // ---- the window --------------------------------------------------------------------------------------------------------

        private static void Build(bool fresh)
        {
            if (!GameUi.Ready() || _station == null) { Close(); return; }
            float scrolled = !fresh && _content != null ? _content.anchoredPosition.y : 0f;
            if (_window != null) Object.Destroy(_window.gameObject);
            UITooltip.HideTooltip();
            _shown = Signature();

            _window = GameUi.Window("RecyclerWindow", new Vector2(W, H));
            Transform w = _window;

            // title, the braid under it, and the share this Recycler returns
            GameUi.Label(w, T("recycler_piece"), new Vector2(Pad, -20f), new Vector2(300f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            int presses = RecyclerPress.CountNear(_station.transform.position);
            GameUi.Label(w, T("recycler_returns", Gold(Calc.Percent(_station))) + "\n<size=80%><color=#bdb7a9>" + T("recycler_presses_near", presses) + "</color></size>",
                new Vector2(-Pad, -22f), new Vector2(360f, 50f), 19f, GameUi.Text, GameUi.Sans, TextAlignmentOptions.TopRight, new Vector2(1f, 1f));
            GameUi.Braid(w, new Vector2(0f, -76f), W - Pad * 2f);

            DrawList(w);
            DrawDetails(w);

            if (_content != null) _content.anchoredPosition = new Vector2(0f, Mathf.Clamp(scrolled, 0f, Mathf.Max(0f, _content.sizeDelta.y)));
        }

        private static void DrawList(Transform w)
        {
            GameUi.Label(w, T("recycler_your_gear"), new Vector2(Pad, -94f), new Vector2(ListW, 28f), 20f, GameUi.Dim, GameUi.Serif);
            RectTransform box = GameUi.Box(w, new Vector2(Pad - 6f, -124f), new Vector2(ListW, H - 124f - Pad));
            _content = GameUi.Scroll(box, 8f);
            if (Rows.Count == 0)
            {
                GameUi.Label(_content, T("recycler_nothing") + "\n\n<size=85%><color=#bdb7a9>" + T("recycler_nothing_hint") + "</color></size>", new Vector2(12f, -12f), new Vector2(ListW - 40f, 160f), 19f, GameUi.Text);
                return;
            }
            float space = GameUi.CellSpace;
            int columns = Mathf.Max(1, Mathf.FloorToInt((ListW - 16f) / space));
            float left = (ListW - 16f - columns * space) / 2f + (space - 64f) / 2f;
            for (int i = 0; i < Rows.Count; i++)
            {
                ItemDrop.ItemData item = Rows[i];
                GameUi.Cell(_content, new Vector2(left + i % columns * space, -6f - i / columns * space), item, null, item == _selected, () => Select(item));
            }
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, (Rows.Count + columns - 1) / columns * space + 12f);
        }

        private static void DrawDetails(Transform w)
        {
            float x = Pad + ListW + 16f, width = W - x - Pad, y = -94f;
            if (_selected == null || _quote == null)
            {
                GameUi.Label(w, T("recycler_pick") + "\n\n<size=85%><color=#bdb7a9>" + T("recycler_pick_hint") + "</color></size>", new Vector2(x, y), new Vector2(width, 200f), 19f, GameUi.Text);
                return;
            }

            // each line takes the height its text needs (a long name or a narrow window wraps), so the next starts below it
            void Line(string text, float size, Color color, TMP_FontAsset font, float gap)
            {
                TMP_Text t = GameUi.Label(w, text, new Vector2(x, y), new Vector2(width, 30f), size, color, font);
                float h = t.GetPreferredValues(text, width, 0f).y;
                ((RectTransform)t.transform).sizeDelta = new Vector2(width, h);
                y -= h + gap;
            }
            Line(Localization.instance.Localize(_selected.m_shared.m_name), 28f, GameUi.Warm, GameUi.Norse, 4f);
            var notes = new List<string>();
            if (_selected.m_shared.m_maxQuality > 1) notes.Add(T("recycler_quality", _selected.m_quality));
            if (_selected.m_equipped) notes.Add(T("recycler_equipped"));
            if (notes.Count > 0) Line(string.Join("  ·  ", notes), 17f, GameUi.Dim, GameUi.Sans, 8f);
            Line(T("recycler_get_back", Gold(_quote.Percent)), 19f, GameUi.Text, GameUi.Sans, 10f);

            float space = GameUi.CellSpace;
            foreach (Calc.Entry e in _quote.Entries.Take(5))
            {
                string amount = e.Min == e.Max ? e.Min.ToString() : $"{e.Min}-{e.Max}";
                GameUi.Cell(w, new Vector2(x, y), e.Res.m_itemData, amount, false, null);
                GameUi.Label(w, Localization.instance.Localize(e.Res.m_itemData.m_shared.m_name), new Vector2(x + space, y - 18f), new Vector2(width - space, 30f), 20f, GameUi.Text);
                y -= space;
            }
            if (_quote.Entries.Count > 5) GameUi.Label(w, T("recycler_more", _quote.Entries.Count - 5), new Vector2(x, y), new Vector2(width, 24f), 17f, GameUi.Dim);

            // what stands in the way, or what recycling it would cost, above the button
            string warning = _quote.Blocked ?? (!Valuable ? null
                : _selected.m_equipped ? T("recycler_warn_equipped")
                : _selected.m_quality >= 2 ? T("recycler_warn_upgraded")
                : T("recycler_warn_moddata"));
            if (warning != null) GameUi.Label(w, warning, new Vector2(x, -(H - Pad - 112f)), new Vector2(width, 56f), 18f, GameUi.Warn, GameUi.Sans, TextAlignmentOptions.BottomLeft);

            string label = Confirming ? T("recycler_button_confirm") : Valuable ? T("recycler_button_ask") : T("recycler_button");
            var button = GameUi.Button(w, label, new Vector2(x, -(H - Pad - 48f)), new Vector2(width, 48f), () =>
            {
                if (Valuable && !Confirming) { _confirmFor = _selected; _confirmUntil = Time.unscaledTime + 5f; }
                else _pending = true;
            }, _quote.Blocked == null, 22f);
            if (Confirming) GameUi.Label(button).color = GameUi.Warn;
        }
    }
}
