using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts.Editor
{
    /// <summary>
    /// Draws the editor's pictures with pooled interface objects: each frame the editor says "a rectangle here, some words there" and the
    /// painter reuses the objects it made before (only what changed is touched), hiding the ones not asked for. It has no clicks and no
    /// event system of its own: the editor does its own hit testing, so the same pictures work on a screen in the world (the Terminal,
    /// which only has a cursor) and in an ordinary window. Everything is in the editor's own space: 1280 x 720, y down.
    ///
    /// Pictures go into named containers (a container clips what it holds); inside one, a higher z is drawn above a lower, and within a z the
    /// rectangles are under the words, so a block's words are never hidden by the block next to it.
    /// </summary>
    internal sealed class Painter
    {
        private sealed class Item
        {
            internal GameObject Go;
            internal RectTransform Rt;
            internal Image Img;
            internal TextMeshProUGUI Tmp;
            internal float X = float.NaN, Y, W, H, Size;
            internal Color Color;
            internal string Text;
            internal TextAlignmentOptions Align;
            internal Sprite Sprite;
            internal bool On;
        }

        private sealed class Group
        {
            internal RectTransform Rt;
            internal readonly List<Item> Items = new List<Item>();
            internal int Used, Prev;
        }

        private sealed class Layer { internal Group Rects, Texts; }

        private sealed class Container
        {
            internal RectTransform Rt, Content;   // Rt clips; Content holds the layers and carries the scale
            internal Rect Clip;
            internal readonly SortedDictionary<int, Layer> Layers = new SortedDictionary<int, Layer>();
        }

        private readonly RectTransform _root;
        private readonly Dictionary<string, Container> _containers = new Dictionary<string, Container>();
        private readonly List<string> _order = new List<string>();
        private TMP_FontAsset _font;
        private TextMeshProUGUI _measure;
        private readonly Dictionary<string, float> _widths = new Dictionary<string, float>();
        private Container _to;
        private int _z;

        internal Painter(RectTransform root, TMP_FontAsset font)
        {
            _root = root;
            _font = font != null ? font : AnyFont();
        }

        internal TMP_FontAsset Font { set { _font = value; } }

        /// <summary>A font from the game's own interface, for a host that has none to give (a text object with no font logs a warning every time).</summary>
        private static TMP_FontAsset AnyFont()
        {
            if (GameUi.Sans != null) return GameUi.Sans;
            if (GameUi.Serif != null) return GameUi.Serif;
            foreach (TMP_Text t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (t != null && t.font != null) return t.font;
            return null;
        }

        // ---- containers ----

        /// <summary>Makes (once) a container over this rectangle, in the order made: later ones are drawn above. The rectangle may be changed later by calling this again.</summary>
        internal void Area(string name, Rect clip, bool mask = true)
        {
            if (!_containers.TryGetValue(name, out Container c))
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(_root, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                if (mask) go.AddComponent<RectMask2D>();
                var content = new GameObject("content", typeof(RectTransform));
                var crt = (RectTransform)content.transform;
                crt.SetParent(rt, false);
                crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = Vector2.zero;
                c = new Container { Rt = rt, Content = crt };
                _containers[name] = c;
                _order.Add(name);
            }
            if (c.Clip != clip || c.Rt.sizeDelta == Vector2.zero)
            {
                c.Clip = clip;
                c.Rt.anchoredPosition = new Vector2(clip.x, -clip.y);
                c.Rt.sizeDelta = new Vector2(clip.width, clip.height);
                c.Content.sizeDelta = new Vector2(clip.width, clip.height);
            }
        }

        /// <summary>Draws a container's pictures bigger or smaller, about its top left (zoom). Positions given to Rect and Text stay in unscaled space.</summary>
        internal void Scale(string container, float scale)
        {
            Container c = _containers[container];
            if (Mathf.Abs(c.Content.localScale.x - scale) > 0.0001f) c.Content.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>This container is left exactly as the last picture drew it (nothing is hidden), for a picture that skips its work.</summary>
        internal void Keep(string container)
        {
            foreach (Layer l in _containers[container].Layers.Values) { l.Rects.Used = l.Rects.Prev; l.Texts.Used = l.Texts.Prev; }
        }

        /// <summary>Everything drawn next goes to this container, at this height.</summary>
        internal void To(string container, int z = 0)
        {
            _to = _containers[container];
            _z = z;
        }

        private Layer LayerFor()
        {
            if (!_to.Layers.TryGetValue(_z, out Layer l))
            {
                l = new Layer { Rects = MakeGroup("rects" + _z), Texts = MakeGroup("texts" + _z) };
                _to.Layers[_z] = l;
                int index = 0;
                foreach (KeyValuePair<int, Layer> kv in _to.Layers)
                {
                    kv.Value.Rects.Rt.SetSiblingIndex(index++);
                    kv.Value.Texts.Rt.SetSiblingIndex(index++);
                }
            }
            return l;
        }

        private Group MakeGroup(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_to.Content, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return new Group { Rt = rt };
        }

        // ---- a frame ----

        internal void Begin()
        {
            foreach (Container c in _containers.Values)
                foreach (Layer l in c.Layers.Values) { l.Rects.Prev = l.Rects.Used; l.Texts.Prev = l.Texts.Used; l.Rects.Used = 0; l.Texts.Used = 0; }
        }

        internal void End()
        {
            foreach (Container c in _containers.Values)
                foreach (Layer l in c.Layers.Values) { Trim(l.Rects); Trim(l.Texts); }
        }

        private static void Trim(Group g)
        {
            for (int i = g.Used; i < g.Items.Count; i++)
                if (g.Items[i].On) { g.Items[i].Go.SetActive(false); g.Items[i].On = false; }
        }

        internal void Clear()
        {
            foreach (Container c in _containers.Values) if (c.Rt != null) UnityEngine.Object.Destroy(c.Rt.gameObject);
            _containers.Clear();
            _order.Clear();
            if (_measure != null) UnityEngine.Object.Destroy(_measure.gameObject);
            _measure = null;
        }

        // ---- drawing ----

        /// <summary>A filled rectangle; round: with the soft corners the blocks have.</summary>
        internal void Rect(float x, float y, float w, float h, Color color, bool round = true)
        {
            if (w <= 0f || h <= 0f) return;
            Layer l = LayerFor();
            Item item = Next(l.Rects, false);
            Place(item, x, y, w, h);
            Sprite sprite = round ? Style.Round : null;
            if (item.Sprite != sprite)
            {
                item.Sprite = sprite;
                item.Img.sprite = sprite;
                item.Img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            }
            if (item.Color != color) { item.Color = color; item.Img.color = color; }
        }

        /// <summary>One line of words in a box (cut off with ... if too long).</summary>
        internal void Text(string text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            if (string.IsNullOrEmpty(text) || w <= 0f) return;
            Layer l = LayerFor();
            Item item = Next(l.Texts, true);
            Place(item, x, y, w, h);
            if (item.Text != text) { item.Text = text; item.Tmp.text = text; }
            if (Math.Abs(item.Size - size) > 0.01f) { item.Size = size; item.Tmp.fontSize = size; }
            if (item.Color != color) { item.Color = color; item.Tmp.color = color; }
            if (item.Align != align) { item.Align = align; item.Tmp.alignment = align; }
        }

        private void Place(Item item, float x, float y, float w, float h)
        {
            x -= _to.Clip.x; y -= _to.Clip.y;
            if (item.X != x || item.Y != y) { item.X = x; item.Y = y; item.Rt.anchoredPosition = new Vector2(x, -y); }
            if (item.W != w || item.H != h) { item.W = w; item.H = h; item.Rt.sizeDelta = new Vector2(w, h); }
        }

        private Item Next(Group g, bool text)
        {
            if (g.Used == g.Items.Count) g.Items.Add(Make(g, text));
            Item item = g.Items[g.Used++];
            if (!item.On) { item.Go.SetActive(true); item.On = true; }
            return item;
        }

        private Item Make(Group g, bool text)
        {
            var go = new GameObject(text ? "t" : "r", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(g.Rt, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var item = new Item { Go = go, Rt = rt, On = !text };
            if (text)
            {
                go.SetActive(false);   // (a text made active starts with the game's default font, which does not exist: it warns each time)
                TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
                if (_font != null) t.font = _font;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.richText = false;
                t.raycastTarget = false;
                t.margin = Vector4.zero;
                item.Tmp = t;
            }
            else
            {
                Image img = go.AddComponent<Image>();
                img.raycastTarget = false;
                item.Img = img;
            }
            return item;
        }

        // ---- measuring ----

        /// <summary>How wide a line of words is at this size.</summary>
        internal float Width(string text, float size)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            string key = size + "|" + text;
            if (_widths.TryGetValue(key, out float w)) return w;
            if (_measure == null)
            {
                var go = new GameObject("measure", typeof(RectTransform));
                go.SetActive(false);
                go.transform.SetParent(_root, false);
                _measure = go.AddComponent<TextMeshProUGUI>();
                if (_font != null) _measure.font = _font;
                _measure.textWrappingMode = TextWrappingModes.NoWrap;
                _measure.richText = false;
                _measure.raycastTarget = false;
                _measure.enabled = false;
                go.SetActive(true);
            }
            _measure.fontSize = size;
            w = _measure.GetPreferredValues(text, 4000f, 0f).x;
            if (_widths.Count > 4000) _widths.Clear();
            _widths[key] = w;
            return w;
        }
    }

    /// <summary>The look: colours by kind of block, sizes, and the soft-cornered rectangle everything is drawn with.</summary>
    internal static class Style
    {
        internal const float Font = 17f, Row = 34f, Pill = 26f, Pad = 10f, Gap = 6f, Indent = 22f, Foot = 12f;

        internal static readonly Color Back = new Color(0.075f, 0.082f, 0.105f), Panel = new Color(0.125f, 0.135f, 0.17f), Bar = new Color(0.16f, 0.17f, 0.23f);
        internal static readonly Color Text = new Color(0.95f, 0.95f, 0.98f), Dim = new Color(0.62f, 0.66f, 0.76f), Accent = new Color(0.36f, 0.5f, 0.95f);
        internal static readonly Color Good = new Color(0.4f, 0.8f, 0.5f), Warn = new Color(1f, 0.72f, 0.3f), Bad = new Color(0.95f, 0.38f, 0.35f);
        internal static readonly Color Button = new Color(0.27f, 0.3f, 0.48f), ButtonOff = new Color(0.17f, 0.19f, 0.27f);
        internal static readonly Color Inset = new Color(0f, 0f, 0f, 0.28f), PillFill = new Color(1f, 1f, 1f, 0.9f), PillText = new Color(0.1f, 0.1f, 0.14f);

        internal static Color Category(string category)
        {
            switch (category)
            {
                case "events": return new Color(0.83f, 0.58f, 0.13f);      // gold
                case "control": return new Color(0.84f, 0.45f, 0.17f);     // orange
                case "values": return new Color(0.23f, 0.6f, 0.38f);       // green
                case "text": return new Color(0.6f, 0.36f, 0.72f);         // purple
                case "lists": return new Color(0.82f, 0.3f, 0.28f);        // red
                case "players": return new Color(0.27f, 0.5f, 0.85f);      // blue
                case "world": return new Color(0.16f, 0.6f, 0.6f);         // teal
                case "effects": return new Color(0.8f, 0.25f, 0.65f);      // magenta
                case "actions": return new Color(0.55f, 0.36f, 0.78f);     // violet
                case "memory": return new Color(0.82f, 0.33f, 0.55f);      // pink
                case "functions": return new Color(0.2f, 0.28f, 0.6f);     // dark blue
                default: return new Color(0.4f, 0.45f, 0.55f);             // (a category this editor does not know yet)
            }
        }

        /// <summary>Round blocks are coloured by what they answer, so a player can see what fits where.</summary>
        internal static Color ByType(string type)
        {
            switch (type)
            {
                case "number": return new Color(0.2f, 0.52f, 0.8f);
                case "text": return new Color(0.66f, 0.34f, 0.62f);
                case "bool": return new Color(0.16f, 0.62f, 0.58f);
                case "pos": return new Color(0.78f, 0.55f, 0.18f);
                case "player": case "actor": case "creature": case "scope": return new Color(0.36f, 0.42f, 0.8f);
                default: return new Color(0.4f, 0.45f, 0.5f);
            }
        }

        /// <summary>A colour for each person editing the same script (the master gives the lowest number nobody has, 0 to 7).</summary>
        internal static Color Presence(int index)
        {
            switch (((index % 8) + 8) % 8)
            {
                case 0: return new Color(0.95f, 0.45f, 0.35f);
                case 1: return new Color(0.35f, 0.75f, 0.95f);
                case 2: return new Color(0.55f, 0.85f, 0.4f);
                case 3: return new Color(0.95f, 0.8f, 0.3f);
                case 4: return new Color(0.75f, 0.5f, 0.95f);
                case 5: return new Color(0.95f, 0.5f, 0.8f);
                case 6: return new Color(0.4f, 0.9f, 0.8f);
                default: return new Color(0.9f, 0.65f, 0.4f);
            }
        }

        internal static Color Darker(Color c, float by) => new Color(c.r * by, c.g * by, c.b * by, c.a);
        internal static Color Lighter(Color c, float by) => new Color(Mathf.Lerp(c.r, 1f, by), Mathf.Lerp(c.g, 1f, by), Mathf.Lerp(c.b, 1f, by), c.a);

        private static Sprite _round;

        /// <summary>A 9-sliced rounded rectangle, made once.</summary>
        internal static Sprite Round
        {
            get
            {
                if (_round != null) return _round;
                const int n = 32, r = 11;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(r - x - 0.5f, x + 0.5f - (n - r)));
                        float dy = Mathf.Max(0f, Mathf.Max(r - y - 0.5f, y + 0.5f - (n - r)));
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d + 0.5f);
                        pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _round = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return _round;
            }
        }
    }
}
