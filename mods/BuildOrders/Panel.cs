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
        private GUIStyle _text, _bold, _title, _big, _small;

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
            _big = Make(16, FontStyle.Bold, TextAnchor.MiddleLeft);
            _small = Make(11, FontStyle.Normal, TextAnchor.MiddleLeft);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !_enabled.Value) return;
            Player player = Player.m_localPlayer;
            if (player == null || Hud.IsUserHidden() || Menu.IsVisible() || InventoryGui.IsVisible()) return;
            bool placing = player.InPlaceMode();
            if (!placing && !_alwaysShowPanel.Value && Aimed == null) return; // nothing to show (a close ghost can be built with E)
            EnsureStyles();

            float s = Mathf.Max(0.75f, Screen.height / 1080f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            if (PlanKeyHeld && placing) DrawBanner(sw);
            if (_orders.Count > 0 && (placing || _alwaysShowPanel.Value)) { RefreshNeeds(); DrawPanel(player); }
            if (Aimed != null) DrawAimLabel(sw, sh, placing);
            GUI.matrix = previousMatrix; // leave the drawing scale as we found it, for whatever draws after us
        }

        private void DrawBanner(float sw)
        {
            var r = new Rect(sw / 2f - 190f, 70f, 380f, 30f);
            Round(r, new Color(0.05f, 0.09f, 0.13f, 0.85f), 7f);
            Outline(r, new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f), 7f);
            Label(r, "PLAN MODE: clicking places a build order, not the piece" + (_planToggle.Value ? $"   ({_planKey.Value} to leave)" : ""), _bold, Cyan, TextAnchor.MiddleCenter);
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
            Label(new Rect(x + 10f, ly + 2f, w - 20f, 16f), PlanHint, _text, new Color(0.7f, 0.68f, 0.64f), TextAnchor.MiddleLeft);
        }

        private void DrawAimLabel(float sw, float sh, bool placing)
        {
            if (!placing) { DrawBuildPrompt(sw, sh); return; }

            Order o = Aimed;
            string text = $"{PieceName(o.Prefab)}   planned by {(string.IsNullOrEmpty(o.By) ? "someone" : o.By)}";
            string hint = $"[{_selectKey.Value}] select this piece    [{_removeKey.Value}] remove    (Shift: whole cluster)";

            var r = new Rect(sw / 2f - 230f, sh / 2f + 90f, 460f, 44f);
            Round(r, new Color(0.05f, 0.05f, 0.05f, 0.85f), 7f);
            Outline(r, new Color(Gold.r, Gold.g, Gold.b, 0.85f), 7f);
            Label(new Rect(r.x, r.y + 3f, r.width, 20f), text, _bold, Gold, TextAnchor.MiddleCenter);
            Label(new Rect(r.x, r.y + 22f, r.width, 18f), hint, _text, Color.white, TextAnchor.MiddleCenter);
        }

        /// <summary>
        /// On foot, aiming at a ghost: the piece's picture and name, a tile for each material (its picture, and what you have out of what
        /// it needs, red when short), and a key cap saying E builds it. Styled after the game's own build requirements.
        /// </summary>
        private void DrawBuildPrompt(float sw, float sh)
        {
            Order o = Aimed;
            const float tile = 64f, gap = 8f;
            int n = HintLines.Count;
            float w = Mathf.Max(340f, n * (tile + gap) - gap + 40f);
            Stab stab = StabilityOf(o.Id);
            float h = 64f + (n > 0 ? tile + 38f : 0f) + 38f + (stab != null ? 24f : 0f) + 24f; // + support line, + the hold-E line
            var r = new Rect(sw / 2f - w / 2f, sh / 2f + 80f, w, h);

            Round(new Rect(r.x + 2f, r.y + 4f, r.width, r.height), new Color(0f, 0f, 0f, 0.4f), 10f); // soft shadow
            Round(r, new Color(0.07f, 0.06f, 0.05f, 0.92f), 10f);
            Outline(r, new Color(Gold.r, Gold.g, Gold.b, 0.9f), 10f);

            // the piece
            Icon(new Rect(r.x + 14f, r.y + 10f, 46f, 46f), HintPieceIcon);
            Label(new Rect(r.x + 68f, r.y + 10f, r.width - 82f, 24f), PieceName(o.Prefab), _big, Gold, TextAnchor.MiddleLeft);
            Label(new Rect(r.x + 68f, r.y + 34f, r.width - 82f, 18f), $"planned by {(string.IsNullOrEmpty(o.By) ? "someone" : o.By)}", _text, new Color(0.72f, 0.7f, 0.66f), TextAnchor.MiddleLeft);

            // the materials
            float y = r.y + 64f;
            if (n > 0)
            {
                float x = r.x + (r.width - (n * (tile + gap) - gap)) / 2f;
                for (int i = 0; i < n; i++)
                {
                    HintLine line = HintLines[i];
                    bool enough = line.Have >= line.Need;
                    Color edge = enough ? new Color(0.45f, 0.85f, 0.5f, 0.9f) : new Color(0.95f, 0.4f, 0.35f, 0.95f);
                    var t = new Rect(x + i * (tile + gap), y, tile, tile + 24f);
                    Round(t, enough ? new Color(0.12f, 0.15f, 0.11f, 0.95f) : new Color(0.2f, 0.1f, 0.09f, 0.95f), 7f);
                    Outline(t, edge, 7f);
                    Icon(new Rect(t.x + 8f, t.y + 6f, tile - 16f, tile - 16f), line.Icon);
                    Label(new Rect(t.x, t.y + tile - 8f, t.width, 16f), $"{line.Have} / {line.Need}", _bold, enough ? Green : Orange, TextAnchor.MiddleCenter);
                    Label(new Rect(t.x, t.y + tile + 6f, t.width, 14f), line.Name, _small, new Color(0.85f, 0.83f, 0.78f), TextAnchor.MiddleCenter);
                }
                y += tile + 38f;
            }

            // how well it would be supported (an estimate; the game does the real thing once it is built)
            if (stab != null)
            {
                string text = stab.Support >= stab.Max ? "Solid: it rests on the ground"
                            : stab.Collapses ? "Nothing supports it yet: it would fall if built now"
                            : $"Support {stab.Percent}%";
                Color sc = stab.Collapses ? new Color(1f, 0.35f, 0.3f) : StabilityColor(stab);
                Label(new Rect(r.x, y, r.width, 20f), text, _text, sc, TextAnchor.MiddleCenter);
                y += 24f;
            }

            // what E does
            if (HintKnown && HintAffordable)
            {
                const float cap = 26f;
                var key = new Rect(r.x + r.width / 2f - 110f, y + 4f, cap, cap);
                Round(key, new Color(0.95f, 0.78f, 0.35f, 1f), 6f);
                Label(key, "E", _big, new Color(0.12f, 0.09f, 0.04f), TextAnchor.MiddleCenter);
                Label(new Rect(key.xMax + 8f, y + 4f, 190f, cap), "Build it", _big, Color.white, TextAnchor.MiddleLeft);
            }
            else
            {
                Label(new Rect(r.x, y + 4f, r.width, 26f), HintKnown ? "Missing materials" : "You haven't unlocked this piece yet", _big, Orange, TextAnchor.MiddleCenter);
            }
            y += 32f;

            // hold E for everything nearby
            var bar = new Rect(r.x + 24f, y, r.width - 48f, 16f);
            if (HoldProgress > 0f)
            {
                Round(bar, new Color(0.2f, 0.18f, 0.14f, 0.95f), 6f);
                Round(new Rect(bar.x, bar.y, bar.width * HoldProgress, bar.height), new Color(0.95f, 0.78f, 0.35f, 1f), 6f);
            }
            Label(bar, HoldProgress > 0f ? "Keep holding to build everything nearby" : $"Hold E: build everything within {_buildAllRadius.Value:0} m", _small,
                  HoldProgress > 0f ? new Color(0.1f, 0.08f, 0.04f) : new Color(0.72f, 0.7f, 0.66f), TextAnchor.MiddleCenter);
        }

        /// <summary>An item or piece picture (they are cut out of a shared sheet, so draw just the part we want).</summary>
        private static void Icon(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            Texture2D tex = sprite.texture;
            Rect s = sprite.textureRect;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(s.x / tex.width, s.y / tex.height, s.width / tex.width, s.height / tex.height));
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
