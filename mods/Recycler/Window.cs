using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Recycler
{
    /// <summary>The Recycler's menu: your recyclable gear on the left, what the selected item returns on the right.</summary>
    internal static class Window
    {
        private class Row { public ItemDrop.ItemData Item; public string Name, Sub; }

        internal static bool IsOpen;

        private static RecyclerStation _station;
        private static readonly List<Row> Rows = new List<Row>();
        private static ItemDrop.ItemData _selected;
        private static Calc.Quote _quote;
        private static Vector2 _scroll;
        private static float _nextRefresh;
        private static bool _pending;
        private static ItemDrop.ItemData _confirmFor;
        private static float _confirmUntil;

        private static Texture2D _white;
        private static GUIStyle _title, _text, _small, _button, _bad;

        private static readonly Color Panel = new Color(0.08f, 0.10f, 0.11f, 0.97f);
        private static readonly Color RowOn = new Color(0.16f, 0.45f, 0.5f, 0.55f);
        private static readonly Color RowOff = new Color(1f, 1f, 1f, 0.05f);
        private static readonly Color Teal = new Color(0.3f, 0.8f, 0.85f);

        internal static void Open(RecyclerStation station)
        {
            _station = station;
            _selected = null; _quote = null; _confirmFor = null;
            _scroll = Vector2.zero;
            IsOpen = true;
            Refresh(Player.m_localPlayer);
        }

        internal static void Close()
        {
            IsOpen = false;
            _station = null; _selected = null; _quote = null; _pending = false;
            Rows.Clear();
            Calc.ClearCache();
            if (_white != null) { Object.Destroy(_white); _white = null; }
        }

        internal static void Tick()
        {
            if (!IsOpen) return;
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead() || _station == null || Menu.IsVisible() || Input.GetKeyDown(KeyCode.Escape) ||
                (_station.transform.position - player.transform.position).sqrMagnitude > 6f * 6f)
            {
                Close();
                return;
            }
            if (_pending) { _pending = false; Recycle(player); }
            if (Time.time >= _nextRefresh) Refresh(player);
        }

        // ---- data -------------------------------------------------------------------------------------

        private static void Refresh(Player player)
        {
            _nextRefresh = Time.time + 0.4f;
            Rows.Clear();
            if (player == null) return;
            bool stillThere = false;
            foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
            {
                if (!Calc.Listed(item)) continue;
                if (item == _selected) stillThere = true;
                string sub = item.m_quality > 1 ? $"Quality {item.m_quality}" : "";
                if (item.m_equipped) sub += (sub.Length > 0 ? "  ·  " : "") + "equipped";
                Rows.Add(new Row { Item = item, Name = Localization.instance.Localize(item.m_shared.m_name), Sub = sub });
            }
            Rows.Sort((a, b) => string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            if (!stillThere) _selected = null;
            _quote = _selected != null && _station != null ? Calc.Evaluate(_selected, _station) : null;
        }

        private static void Recycle(Player player)
        {
            ItemDrop.ItemData item = _selected;
            if (item == null || _station == null || !player.GetInventory().ContainsItem(item)) return;
            Calc.Quote quote = Calc.Evaluate(item, _station);
            if (quote.Blocked != null) return;

            string name = Localization.instance.Localize(item.m_shared.m_name);
            if (item.m_equipped) player.UnequipItem(item, false);
            player.GetInventory().RemoveItem(item);

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
                player.Message(MessageHud.MessageType.TopLeft, said.Count > 0 ? $"Recycled {name}: {string.Join(", ", said)}" : $"Recycled {name}, but nothing came back");

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

        // ---- drawing ----------------------------------------------------------------------------------

        private static void Init()
        {
            if (_white != null && _title != null) return;
            _white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            _white.SetPixel(0, 0, Color.white); _white.Apply();
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _text = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = new Color(0.9f, 0.93f, 0.94f) }, wordWrap = true };
            _small = new GUIStyle(_text) { fontSize = 12, normal = { textColor = new Color(0.6f, 0.68f, 0.7f) } };
            _bad = new GUIStyle(_text) { normal = { textColor = new Color(1f, 0.6f, 0.45f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        }

        private static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private static void Icon(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            Rect s = sprite.textureRect, t = new Rect(s.x / sprite.texture.width, s.y / sprite.texture.height, s.width / sprite.texture.width, s.height / sprite.texture.height);
            GUI.DrawTextureWithTexCoords(r, sprite.texture, t);
        }

        internal static void Draw()
        {
            if (!IsOpen || _station == null || Event.current == null) return;
            Init();
            float w = Mathf.Min(800f, Screen.width - 40f), h = Mathf.Min(520f, Screen.height - 40f);
            var win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            Fill(win, Panel);

            GUI.Label(new Rect(win.x + 16, win.y + 10, 300, 32), "Recycler", _title);
            string share = $"Returns {Calc.Percent(_station)}% of the materials   ·   presses {RecyclerPress.CountNear(_station.transform.position)}/2";
            GUI.Label(new Rect(win.x + 140, win.y + 16, win.width - 260, 24), share, _small);
            if (GUI.Button(new Rect(win.xMax - 92, win.y + 10, 80, 28), "Close")) { Close(); return; }

            var list = new Rect(win.x + 12, win.y + 52, win.width * 0.46f, win.height - 64);
            var right = new Rect(list.xMax + 14, list.y, win.xMax - list.xMax - 26, list.height);
            Fill(list, new Color(1, 1, 1, 0.04f));
            DrawList(list);
            DrawDetails(right);
        }

        private static void DrawList(Rect area)
        {
            if (Rows.Count == 0)
            {
                GUI.Label(new Rect(area.x + 10, area.y + 10, area.width - 20, 120), "Nothing in your inventory can be recycled.\nOnly weapons, armor and tools with a recipe are accepted. Items locked with L are skipped.", _small);
                return;
            }
            const float rowH = 46f;
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, area.width - 18, Rows.Count * rowH));
            for (int i = 0; i < Rows.Count; i++)
            {
                Row row = Rows[i];
                var r = new Rect(4, i * rowH + 2, area.width - 26, rowH - 4);
                if (r.yMax < _scroll.y || r.y > _scroll.y + area.height) continue;
                Fill(r, row.Item == _selected ? RowOn : RowOff);
                Icon(new Rect(r.x + 4, r.y + 3, 36, 36), row.Item.GetIcon());
                GUI.Label(new Rect(r.x + 48, r.y + 2, r.width - 52, 22), row.Name, _text);
                if (row.Sub.Length > 0) GUI.Label(new Rect(r.x + 48, r.y + 22, r.width - 52, 18), row.Sub, _small);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _selected = row.Item; _confirmFor = null; _quote = Calc.Evaluate(row.Item, _station); }
            }
            GUI.EndScrollView();
        }

        private static void DrawDetails(Rect area)
        {
            if (_selected == null || _quote == null)
            {
                GUI.Label(area, "Pick an item on the left to see what it would return.\n\nA Recycler Press within 8 m raises the share.", _small);
                return;
            }

            float y = area.y;
            GUI.Label(new Rect(area.x, y, area.width, 28), Localization.instance.Localize(_selected.m_shared.m_name), _title);
            y += 36;
            GUI.Label(new Rect(area.x, y, area.width, 22), $"You get back about {_quote.Percent}% of its materials", _small);
            y += 30;

            foreach (Calc.Entry e in _quote.Entries)
            {
                Icon(new Rect(area.x, y, 32, 32), e.Res.m_itemData.GetIcon());
                string amount = e.Min == e.Max ? e.Min.ToString() : $"{e.Min}–{e.Max}";
                GUI.Label(new Rect(area.x + 40, y + 5, area.width - 40, 24), $"{amount}  {Localization.instance.Localize(e.Res.m_itemData.m_shared.m_name)}", _text);
                y += 36;
            }

            float bottom = area.yMax;
            if (_quote.Blocked != null) GUI.Label(new Rect(area.x, bottom - 100, area.width, 44), _quote.Blocked, _bad);
            bool valuable = Plugin.ConfirmValuable.Value && Calc.Valuable(_selected);
            bool confirming = valuable && _confirmFor == _selected && Time.time < _confirmUntil;
            if (valuable && _quote.Blocked == null)
                GUI.Label(new Rect(area.x, bottom - 100, area.width, 44), _selected.m_equipped ? "This item is equipped. Recycling destroys it." : "This item has been upgraded. Recycling destroys it.", _bad);

            GUI.enabled = _quote.Blocked == null;
            string label = confirming ? "Yes, recycle it" : valuable ? "Recycle…" : "Recycle";
            if (GUI.Button(new Rect(area.x, bottom - 48, area.width, 40), label, _button))
            {
                if (valuable && !confirming) { _confirmFor = _selected; _confirmUntil = Time.time + 5f; }
                else _pending = true;
            }
            GUI.enabled = true;
        }
    }
}
