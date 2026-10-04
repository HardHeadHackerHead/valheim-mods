using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// The on-screen help: a materials panel (what all the orders need, against what you have), a banner while the plan key
    /// is held, and a label on the order you're aiming at.
    /// </summary>
    public partial class Plugin
    {
        private class Need { public string Item, Name; public int Amount; }

        private List<Need> _needs = new List<Need>();
        private float _nextNeeds;
        private GUIStyle _text, _bold, _title;

        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color Green = new Color(0.55f, 1f, 0.6f);
        private static readonly Color Orange = new Color(1f, 0.62f, 0.45f);
        private static readonly Color Cyan = new Color(0.45f, 0.85f, 1f);

        /// <summary>Total up the materials every order still needs.</summary>
        private void RefreshNeeds()
        {
            if (Time.unscaledTime < _nextNeeds) return;
            _nextNeeds = Time.unscaledTime + 0.5f;

            var totals = new Dictionary<string, Need>();
            foreach (Order o in _orders.Values)
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(o.Prefab) : null;
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null) continue;

                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem == null || req.m_amount <= 0) continue;
                    string item = req.m_resItem.m_itemData.m_shared.m_name;
                    if (!totals.TryGetValue(item, out Need need))
                        totals[item] = need = new Need { Item = item, Name = Localization.instance.Localize(item) };
                    need.Amount += req.m_amount;
                }
            }
            _needs = totals.Values.OrderByDescending(n => n.Amount).ToList();
        }

        private void EnsureStyles()
        {
            if (_text != null) return;
            GUIStyle Make(int size, FontStyle style, TextAnchor anchor)
            {
                var s = new GUIStyle(GUI.skin.label)
                {
                    fontSize = size, fontStyle = style, alignment = anchor, wordWrap = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0),
                };
                s.normal.textColor = Color.white; // tinted per use through GUI.color
                return s;
            }
            _text = Make(12, FontStyle.Normal, TextAnchor.MiddleLeft);
            _bold = Make(12, FontStyle.Bold, TextAnchor.MiddleLeft);
            _title = Make(13, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !_enabled.Value) return;
            Player player = Player.m_localPlayer;
            if (player == null || Hud.IsUserHidden() || Menu.IsVisible() || InventoryGui.IsVisible()) return;
            if (!player.InPlaceMode() && !_alwaysShowPanel.Value) return;
            EnsureStyles();

            float s = Mathf.Max(0.75f, Screen.height / 1080f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            if (PlanKeyHeld && player.InPlaceMode()) DrawBanner(sw);
            if (_orders.Count > 0) { RefreshNeeds(); DrawPanel(player); }
            if (Aimed != null) DrawAimLabel(sw, sh);
            GUI.matrix = previousMatrix; // leave the drawing scale as we found it, for whatever draws after us
        }

        private void DrawBanner(float sw)
        {
            var r = new Rect(sw / 2f - 190f, 70f, 380f, 30f);
            Round(r, new Color(0.05f, 0.09f, 0.13f, 0.85f), 7f);
            Outline(r, new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f), 7f);
            Label(r, "PLAN MODE: clicking places a build order, not the piece", _bold, Cyan, TextAnchor.MiddleCenter);
        }

        private void DrawPanel(Player player)
        {
            int shown = Mathf.Min(_needs.Count, 10);
            float w = 250f, h = 34f + shown * 19f + (_needs.Count > shown ? 18f : 0f) + 22f;
            float x = _panelX.Value, y = _panelY.Value;
            var panel = new Rect(x, y, w, h);

            Round(new Rect(x + 2f, y + 3f, w, h), new Color(0f, 0f, 0f, 0.35f), 8f);
            Round(panel, new Color(0.06f, 0.05f, 0.045f, 0.88f), 7f);
            Outline(panel, new Color(Cyan.r, Cyan.g, Cyan.b, 0.7f), 7f);

            Label(new Rect(x + 10f, y + 6f, w - 20f, 20f), $"BUILD ORDERS  ·  {_orders.Count} piece(s)", _title, Cyan, TextAnchor.MiddleLeft);

            Inventory inv = player.GetInventory();
            float ly = y + 30f;
            foreach (Need n in _needs.Take(shown))
            {
                int have = inv.CountItems(n.Item); // includes nearby chests when BuildFromChests is installed
                Color c = have >= n.Amount ? Green : Orange;
                Label(new Rect(x + 10f, ly, w - 90f, 18f), n.Name, _text, Color.white, TextAnchor.MiddleLeft);
                Label(new Rect(x + w - 82f, ly, 72f, 18f), $"{have} / {n.Amount}", _bold, c, TextAnchor.MiddleRight);
                ly += 19f;
            }
            if (_needs.Count > shown)
            {
                Label(new Rect(x + 10f, ly, w - 20f, 18f), $"+ {_needs.Count - shown} more materials", _text, new Color(0.7f, 0.68f, 0.64f), TextAnchor.MiddleLeft);
                ly += 18f;
            }
            Label(new Rect(x + 10f, ly + 2f, w - 20f, 16f), $"Hold {_planKey.Value} + place to plan", _text, new Color(0.7f, 0.68f, 0.64f), TextAnchor.MiddleLeft);
        }

        private void DrawAimLabel(float sw, float sh)
        {
            Order o = Aimed;
            string text = $"{PieceName(o.Prefab)}   planned by {(string.IsNullOrEmpty(o.By) ? "someone" : o.By)}";
            string hint = $"[{_selectKey.Value}] select this piece    [{_removeKey.Value}] remove    (Shift: whole cluster)";

            var r = new Rect(sw / 2f - 230f, sh / 2f + 90f, 460f, 44f);
            Round(r, new Color(0.05f, 0.05f, 0.05f, 0.85f), 7f);
            Outline(r, new Color(Gold.r, Gold.g, Gold.b, 0.85f), 7f);
            Label(new Rect(r.x, r.y + 3f, r.width, 20f), text, _bold, Gold, TextAnchor.MiddleCenter);
            Label(new Rect(r.x, r.y + 22f, r.width, 18f), hint, _text, Color.white, TextAnchor.MiddleCenter);
        }

        // ---- drawing helpers ----------------------------------------------------------------------

        private static void Round(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));

        private static void Outline(Rect r, Color c, float radius) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one, new Vector4(radius, radius, radius, radius));

        /// <summary>Text with a 1px dark shadow so it reads on any background.</summary>
        private static void Label(Rect r, string text, GUIStyle style, Color color, TextAnchor anchor)
        {
            TextAnchor previous = style.alignment;
            style.alignment = anchor;
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = Color.white;
            style.alignment = previous;
        }
    }
}
