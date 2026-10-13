using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

namespace Terminal
{
    /// <summary>Text widths from the screen's own font (the glyph advances TextMeshPro itself uses), so layout and drawing agree.</summary>
    internal static class Fonts
    {
        private static readonly Dictionary<char, float> Em = new Dictionary<char, float>();
        private static TMP_FontAsset _for;

        /// <summary>TextMeshPro kerns pairs of letters; summing glyph advances alone comes out 4.3% too wide (measured against its own preferred width on two sentences).</summary>
        private const float Kerning = 0.9566f;

        private static float Advance(char c)
        {
            TMP_FontAsset f = Desktop.Font;
            if (f == null) return 0.5f;
            if (_for != f) { Em.Clear(); _for = f; }
            if (Em.TryGetValue(c, out float em)) return em;
            float v;
            if (f.characterLookupTable != null && f.characterLookupTable.TryGetValue(c, out TMP_Character ch) && ch.glyph != null && f.faceInfo.pointSize > 0f)
                v = ch.glyph.metrics.horizontalAdvance / f.faceInfo.pointSize * (f.faceInfo.scale == 0f ? 1f : f.faceInfo.scale);
            else v = c >= 0x2E80 ? 1f : c == '\t' ? 2f : 0.55f;
            Em[c] = v;
            return v;
        }

        internal static float Width(string s, float size, bool bold)
        {
            float w = 0f;
            for (int i = 0; i < s.Length; i++) w += Advance(s[i]);
            return w * size * (bold ? 1.06f : 1f) * Kerning;
        }
    }

    /// <summary>
    /// Lays a page out: from the tree and its styles to a flat list of things to draw at fixed places (boxes, text runs, pictures), the way a
    /// browser's layout step does, in the pieces a page like Wikipedia needs: block boxes with margins, padding, borders and widths; inline
    /// text with word wrap, alignment, bold, italic, colour and links; floats (an infobox on the right); lists with markers; tables with
    /// columns sized to their content; pictures. Not here: flex and grid (flex rows flow side by side, the rest stacks), positioning,
    /// transforms, overflow. All sizes are CSS pixels; the screen draws them scaled.
    /// </summary>
    internal sealed class Layouter
    {
        internal sealed class Item
        {
            public byte Kind;   // 0 nothing (a reserved place), 1 box, 2 text, 3 picture
            public float X, Y, W, H;
            public Color C;
            public string Text, Url, Alt, Href;
            public float Size;
            public bool Bold, Italic;
        }

        internal struct Link { public float X, Y, W, H; public string Href; }

        private sealed class FloatBox { public float X, Y, W, H; }
        private sealed class Bfc { public readonly List<FloatBox> Left = new List<FloatBox>(), Right = new List<FloatBox>(); }

        private abstract class Atom { public Style S; public string Href; }
        private sealed class Word : Atom { public string Text; public float W; }
        private sealed class Space : Atom { public float W; }
        private sealed class Break : Atom { }
        private sealed class Thing : Atom { public Node N; public float W, H; public bool Img; }

        internal readonly List<Item> Items = new List<Item>();
        internal readonly List<Link> Links = new List<Link>();
        internal readonly List<KeyValuePair<string, Node>> Pictures = new List<KeyValuePair<string, Node>>();
        internal float Height;
        internal Color Page = Color.white;

        private readonly Uri _base;
        private readonly Func<string, Vector2?> _imageSize;   // natural size of a picture that has loaded
        private int _depth;

        internal Layouter(Uri baseUri, Func<string, Vector2?> imageSize) { _base = baseUri; _imageSize = imageSize; }

        // ---- entry ----

        internal void Run(Node root, float width)
        {
            Node body = Find(root, "body") ?? root;
            Color? bg = body.Style != null ? body.Style.Background : null;
            if (!bg.HasValue && root.Style != null) bg = root.Style.Background;
            if (bg.HasValue && bg.Value.a > 0.5f) Page = bg.Value;
            var bfc = new Bfc();
            float y = Children(root, 0f, width, 0f, bfc);
            float floats = 0f;
            foreach (FloatBox f in bfc.Left) floats = Math.Max(floats, f.Y + f.H);
            foreach (FloatBox f in bfc.Right) floats = Math.Max(floats, f.Y + f.H);
            Height = Math.Max(y, floats) + 16f;
            Items.RemoveAll(i => i == null || i.Kind == 0);
        }

        private static Node Find(Node n, string tag)
        {
            if (n.Tag == tag) return n;
            foreach (Node k in n.Kids) { Node f = Find(k, tag); if (f != null) return f; }
            return null;
        }

        // ---- blocks ----

        private static bool IsBlockLevel(Node n)
        {
            if (n.IsText) return false;
            string d = n.Style.Display;
            return d == "block" || d == "list-item" || d == "table" || d == "flex" || d == "grid" || d == "table-row" || d == "table-row-group" || d == "table-caption" || d == "table-cell" || d == "flow-root";
        }

        private static bool Hidden(Node n) => n.IsText ? false : n.Style.Display == "none" || n.Style.Position == "absolute" || n.Style.Position == "fixed";

        private static bool FlexRow(Node n) => n.Style != null && (n.Style.Display == "flex" || n.Style.Display == "inline-flex") && !n.Style.FlexColumn;

        /// <summary>The children of a box laid out in order into [cx, cx+cw] from y; returns the y below the last.</summary>
        private float Children(Node n, float cx, float cw, float y, Bfc bfc)
        {
            if (++_depth > 120) { _depth--; return y; }
            var run = new List<Node>();
            float pendingMargin = 0f;
            bool flex = FlexRow(n);
            foreach (Node k in n.Kids)
            {
                if (Hidden(k)) continue;
                bool floating = !k.IsText && k.Style.Float != "none" && k.Style.Float != "";
                bool block = !flex && IsBlockLevel(k);
                if (floating && !flex)
                {
                    y = FlushRun(run, n, cx, cw, y, bfc, ref pendingMargin);
                    PlaceFloat(k, cx, cw, y, bfc);
                    continue;
                }
                if (!block) { run.Add(k); continue; }
                y = FlushRun(run, n, cx, cw, y, bfc, ref pendingMargin);
                Style s = k.Style;
                string clear = s.Clear;
                float top = s.MarginT.Resolve(cw, 0f);
                float gap = Math.Max(pendingMargin, top);
                y += gap;
                if (clear == "both" || clear == "left" || clear == "right") y = ClearY(bfc, y, clear);
                float bottom = Box(k, cx, cw, y, bfc);
                y = bottom;
                pendingMargin = s.MarginB.Resolve(cw, 0f);
            }
            y = FlushRun(run, n, cx, cw, y, bfc, ref pendingMargin);
            _depth--;
            return y + pendingMargin;
        }

        private float FlushRun(List<Node> run, Node parent, float cx, float cw, float y, Bfc bfc, ref float pendingMargin)
        {
            if (run.Count == 0) return y;
            var atoms = new List<Atom>();
            Collect(run, null, atoms, cw, FlexRow(parent));
            run.Clear();
            while (atoms.Count > 0 && (atoms[0] is Space)) atoms.RemoveAt(0);
            while (atoms.Count > 0 && (atoms[atoms.Count - 1] is Space)) atoms.RemoveAt(atoms.Count - 1);
            if (atoms.Count == 0) return y;
            y += pendingMargin;
            pendingMargin = 0f;
            return Lines(atoms, parent.Style, cx, cw, y, bfc);
        }

        private float ClearY(Bfc bfc, float y, string clear)
        {
            if (clear != "right") foreach (FloatBox f in bfc.Left) y = Math.Max(y, f.Y + f.H);
            if (clear != "left") foreach (FloatBox f in bfc.Right) y = Math.Max(y, f.Y + f.H);
            return y;
        }

        /// <summary>One block-level box with its margins, border, padding and content: laid out with its top border edge at y; returns the y of its bottom border edge.</summary>
        private float Box(Node n, float cx, float cw, float y, Bfc bfc)
        {
            Style s = n.Style;
            if (n.Tag == "img") return ImageBlock(n, cx, cw, y);
            if (s.Display == "table") return Table(n, cx, cw, y, bfc);

            float ml = s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f), mr = s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f);
            float extra = s.BorderL + s.BorderR + s.PadL + s.PadR;
            float width = cw - ml - mr;   // the border box
            if (!s.Width.Auto) width = s.Width.Resolve(cw, 0f) + extra;
            if (!s.MaxWidth.Auto) width = Math.Min(width, s.MaxWidth.Resolve(cw, width) + extra);
            if (!s.MinWidth.Auto) width = Math.Max(width, s.MinWidth.Resolve(cw, 0f) + extra);
            width = Math.Max(extra, Math.Min(width, cw));
            float x0 = cx + ml;
            if (s.MarginL.Auto && s.MarginR.Auto) x0 = cx + (cw - width) / 2f;
            else if (s.MarginL.Auto) x0 = cx + cw - width - mr;

            float contentX = x0 + s.BorderL + s.PadL, contentW = Math.Max(0f, width - extra);
            float contentY = y + s.BorderT + s.PadT;
            Item bgSlot = Reserve();
            float markerBottom = contentY;
            if (s.Display == "list-item") Marker(n, contentX, contentY);

            float bottom = Children(n, contentX, contentW, contentY, bfc);   // (a nested block shares its parent's floats: text beside an infobox wraps around it)
            if (n.Style.Float != "none") { /* (floats are boxes of their own: see PlaceFloat) */ }
            float contentH = Math.Max(bottom - contentY, markerBottom - contentY);
            if (!s.Height.Auto && !s.Height.Percent) contentH = Math.Max(contentH, s.Height.V);
            float boxH = s.BorderT + s.PadT + contentH + s.PadB + s.BorderB;

            if (s.Visible)
            {
                if (s.Background.HasValue && s.Background.Value.a > 0.01f) Fill(bgSlot, x0, y, width, boxH, s.Background.Value);
                Borders(s, x0, y, width, boxH);
            }
            return y + boxH;
        }

        private Item Reserve() { var item = new Item { Kind = 0 }; Items.Add(item); return item; }

        private static void Fill(Item slot, float x, float y, float w, float h, Color c) { slot.Kind = 1; slot.X = x; slot.Y = y; slot.W = w; slot.H = h; slot.C = c; }

        private void Borders(Style s, float x, float y, float w, float h)
        {
            if (s.BorderT > 0f) Items.Add(new Item { Kind = 1, X = x, Y = y, W = w, H = s.BorderT, C = s.BorderColor });
            if (s.BorderB > 0f) Items.Add(new Item { Kind = 1, X = x, Y = y + h - s.BorderB, W = w, H = s.BorderB, C = s.BorderColor });
            if (s.BorderL > 0f) Items.Add(new Item { Kind = 1, X = x, Y = y, W = s.BorderL, H = h, C = s.BorderColor });
            if (s.BorderR > 0f) Items.Add(new Item { Kind = 1, X = x + w - s.BorderR, Y = y, W = s.BorderR, H = h, C = s.BorderColor });
        }

        private void Marker(Node li, float contentX, float y)
        {
            Style s = li.Style;
            if (s.ListStyle == "none" || !s.Visible) return;
            string text;
            if (s.ListStyle == "decimal")
            {
                int index = 0;
                if (li.Parent != null) foreach (Node k in li.Parent.Kids) { if (k.Tag == "li") index++; if (k == li) break; }
                if (li.Parent != null && int.TryParse(li.Parent.Get("start"), out int start)) index += start - 1;
                text = index + ".";
            }
            else text = s.ListStyle == "circle" ? "◦" : s.ListStyle == "square" ? "▪" : "•";
            float w = Fonts.Width(text, s.FontSize, false);
            float lh = LineHeight(s);
            Items.Add(new Item { Kind = 2, Text = text, X = contentX - w - 6f, Y = y + (lh - s.FontSize * 1.2f) / 2f, W = w, H = s.FontSize * 1.2f, Size = s.FontSize, C = s.Color });
        }

        // ---- pictures ----

        private string Resolve(string src)
        {
            if (string.IsNullOrWhiteSpace(src)) return null;
            if (src.StartsWith("data:")) return null;
            return Uri.TryCreate(_base, System.Net.WebUtility.HtmlDecode(src.Trim()), out Uri u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.GetLeftPart(UriPartial.Query) : null;
        }

        /// <summary>The size a picture takes: its width and height attributes or style, else the loaded picture's own, else a small placeholder.</summary>
        private Vector2 PictureSize(Node n, float maxW, out string url)
        {
            url = Resolve(n.Get("src") ?? n.Get("data-src"));
            Style s = n.Style;
            float w = s.Width.Auto ? ParseAttr(n.Get("width"), maxW) : s.Width.Resolve(maxW, 0f);
            float h = s.Height.Auto ? ParseAttr(n.Get("height"), maxW) : s.Height.Resolve(maxW, 0f);
            Vector2? natural = url != null ? _imageSize(url) : null;
            if (w <= 0f && h <= 0f) { if (natural.HasValue) { w = natural.Value.x; h = natural.Value.y; } else { w = 120f; h = 80f; } }
            else if (w <= 0f) w = natural.HasValue ? h * natural.Value.x / Math.Max(1f, natural.Value.y) : h;
            else if (h <= 0f) h = natural.HasValue ? w * natural.Value.y / Math.Max(1f, natural.Value.x) : w * 0.66f;
            if (!s.MaxWidth.Auto) { float mw = s.MaxWidth.Resolve(maxW, w); if (w > mw) { h *= mw / w; w = mw; } }
            if (w > maxW) { h *= maxW / w; w = maxW; }
            return new Vector2(Math.Max(1f, w), Math.Max(1f, h));
        }

        private static float ParseAttr(string v, float basis)
        {
            if (string.IsNullOrEmpty(v)) return 0f;
            v = v.Trim();
            bool pct = v.EndsWith("%");
            if (pct) v = v.TrimEnd('%');
            if (v.EndsWith("px")) v = v.Substring(0, v.Length - 2);
            return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? (pct ? basis * f / 100f : f) : 0f;
        }

        private void AddPicture(Node n, string url, float x, float y, float w, float h, string href)
        {
            if (!n.Style.Visible) return;
            Items.Add(new Item { Kind = 3, X = x, Y = y, W = w, H = h, Url = url, Alt = n.Get("alt") ?? "", C = new Color(0.5f, 0.5f, 0.5f, 0.35f) });
            if (url != null && Pictures.Count < 60 && !Pictures.Any(p => p.Key == url)) Pictures.Add(new KeyValuePair<string, Node>(url, n));
            if (href != null) Links.Add(new Link { X = x, Y = y, W = w, H = h, Href = href });
        }

        private float ImageBlock(Node n, float cx, float cw, float y)
        {
            Vector2 size = PictureSize(n, cw, out string url);
            Style s = n.Style;
            float x = s.MarginL.Auto && s.MarginR.Auto ? cx + (cw - size.x) / 2f : cx + s.MarginL.Resolve(cw, 0f);
            AddPicture(n, url, x, y, size.x, size.y, EnclosingLink(n));
            return y + size.y;
        }

        private static string EnclosingLink(Node n)
        {
            for (Node up = n.Parent; up != null; up = up.Parent) if (up.Tag == "a" && up.Get("href") != null) return up.Get("href");
            return null;
        }

        // ---- floats ----

        private void PlaceFloat(Node n, float cx, float cw, float y, Bfc bfc)
        {
            Style s = n.Style;
            bool right = s.Float == "right";
            float ml = s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f), mr = s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f), mt = s.MarginT.Resolve(cw, 0f), mb = s.MarginB.Resolve(cw, 0f);
            float extra = s.BorderL + s.BorderR + s.PadL + s.PadR;
            float width;
            if (n.Tag == "img") width = PictureSize(n, cw, out _).x;
            else if (!s.Width.Auto) width = s.Width.Resolve(cw, 0f) + extra;
            else width = Math.Min(MaxContent(n, cw) + extra, cw * (cw > 600f ? 0.5f : 1f));
            if (!s.MaxWidth.Auto) width = Math.Min(width, s.MaxWidth.Resolve(cw, width) + extra);
            width = Math.Max(20f, Math.Min(width, cw - ml - mr));
            float total = width + ml + mr;
            float top = y + mt;
            // find a place where it fits beside the floats already there
            for (int guard = 0; guard < 40; guard++)
            {
                Range(bfc, top, 1f, cx, cw, out float left, out float rightEdge);
                if (rightEdge - left >= total) break;
                float next = NextFloatBottom(bfc, top);
                if (next <= top) break;
                top = next;
            }
            Range(bfc, top, 1f, cx, cw, out float l2, out float r2);
            float x = right ? r2 - total + ml : l2 + ml;
            float bottom;
            if (n.Tag == "img") bottom = ImageBlockAt(n, x, top);
            else if (s.Display == "table") bottom = Table(n, x, width, top, new Bfc());
            else
            {
                // laid out as a block of its own width, with a flow of its own inside
                var saved = s.Float; s.Float = "none";
                float savedML = s.MarginL.V, savedMR = s.MarginR.V; bool aml = s.MarginL.Auto, amr = s.MarginR.Auto;
                s.MarginL = Len.Px(0); s.MarginR = Len.Px(0);
                var savedWidth = s.Width; s.Width = Len.Px(width - extra);
                bottom = Box(n, x, width, top, new Bfc());
                s.Width = savedWidth; s.Float = saved; s.MarginL = aml ? Len.None : Len.Px(savedML); s.MarginR = amr ? Len.None : Len.Px(savedMR);
            }
            var box = new FloatBox { X = x - ml, Y = top - mt, W = total, H = bottom - top + mt + mb };
            (right ? bfc.Right : bfc.Left).Add(box);
        }

        private float ImageBlockAt(Node n, float x, float y)
        {
            Vector2 size = PictureSize(n, 100000f, out string url);
            AddPicture(n, url, x, y, size.x, size.y, EnclosingLink(n));
            return y + size.y;
        }

        private static void Range(Bfc bfc, float y, float h, float cx, float cw, out float left, out float right)
        {
            left = cx; right = cx + cw;
            foreach (FloatBox f in bfc.Left) if (f.Y + f.H > y && f.Y < y + h) left = Math.Max(left, f.X + f.W);
            foreach (FloatBox f in bfc.Right) if (f.Y + f.H > y && f.Y < y + h) right = Math.Min(right, f.X);
        }

        private static float NextFloatBottom(Bfc bfc, float y)
        {
            float best = float.MaxValue;
            foreach (FloatBox f in bfc.Left) if (f.Y + f.H > y) best = Math.Min(best, f.Y + f.H);
            foreach (FloatBox f in bfc.Right) if (f.Y + f.H > y) best = Math.Min(best, f.Y + f.H);
            return best == float.MaxValue ? y : best;
        }

        // ---- inline content ----

        private static float LineHeight(Style s) => s.LineHeight > 0f ? s.LineHeight : s.FontSize * 1.25f;

        private void Collect(List<Node> nodes, string href, List<Atom> atoms, float cw, bool flexItems)
        {
            foreach (Node n in nodes)
            {
                if (Hidden(n)) continue;
                if (n.IsText)
                {
                    Text(n.Text, n.Parent.Style, href, atoms);
                    continue;
                }
                Style s = n.Style;
                if (n.Tag == "br") { atoms.Add(new Break { S = s }); continue; }
                string link = n.Tag == "a" && n.Get("href") != null ? Hyperlink(n.Get("href")) ?? href : href;
                if (n.Tag == "img")
                {
                    Vector2 size = PictureSize(n, cw, out _);
                    atoms.Add(new Thing { S = s, N = n, W = size.x, H = size.y, Img = true, Href = link });
                    continue;
                }
                bool box = flexItems || s.Display == "inline-block" || s.Display == "inline-table" || s.Display == "inline-flex" || s.Display == "inline-grid" || IsBlockLevel(n);
                if (box)
                {
                    float extra = s.BorderL + s.BorderR + s.PadL + s.PadR + (s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f)) + (s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f));
                    float w = !s.Width.Auto ? s.Width.Resolve(cw, 0f) + extra : Math.Min(MaxContent(n, cw) + extra + 3f, cw);   // (+3: measuring is a hair short of drawing)
                    atoms.Add(new Thing { S = s, N = n, W = Math.Max(1f, Math.Min(w, cw)), Href = link });
                    continue;
                }
                float before = s.PadL + s.BorderL + (s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f));
                if (before > 0f) atoms.Add(new Space { S = s, W = before, Href = null });
                Collect(n.Kids, link, atoms, cw, false);
                float after = s.PadR + s.BorderR + (s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f));
                if (after > 0f) atoms.Add(new Space { S = s, W = after, Href = null });
            }
        }

        private string Hyperlink(string href)
        {
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#") || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return null;
            return Uri.TryCreate(_base, System.Net.WebUtility.HtmlDecode(href.Trim()), out Uri u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.GetLeftPart(UriPartial.Query) : null;
        }

        private static void Text(string text, Style s, string href, List<Atom> atoms)
        {
            bool pre = s.WhiteSpace == "pre" || s.WhiteSpace == "pre-wrap" || s.WhiteSpace == "pre-line";
            if (s.Transform == "uppercase") text = text.ToUpperInvariant();
            else if (s.Transform == "lowercase") text = text.ToLowerInvariant();
            float spaceW = Fonts.Width(" ", s.FontSize, s.Bold);
            var word = new StringBuilder();
            void FlushWord()
            {
                if (word.Length == 0) return;
                string w = word.ToString();
                word.Length = 0;
                atoms.Add(new Word { S = s, Href = href, Text = w, W = Fonts.Width(w, s.FontSize, s.Bold) });
            }
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r') continue;
                if (pre && c == '\n') { FlushWord(); atoms.Add(new Break { S = s }); continue; }
                if (char.IsWhiteSpace(c) || c == ' ')
                {
                    FlushWord();
                    if (c == ' ') { word.Append(' '); continue; }
                    if (pre) atoms.Add(new Space { S = s, Href = href, W = spaceW * (c == '\t' ? 4 : 1) });
                    else if (atoms.Count == 0 || !(atoms[atoms.Count - 1] is Space)) atoms.Add(new Space { S = s, Href = href, W = spaceW });
                    continue;
                }
                word.Append(c);
            }
            FlushWord();
        }

        /// <summary>Breaks atoms into lines, avoiding floats, and emits each line's text runs, pictures and boxes. Returns the y below the last line.</summary>
        private float Lines(List<Atom> atoms, Style block, float cx, float cw, float y, Bfc bfc)
        {
            int i = 0;
            string align = block.TextAlign;
            while (i < atoms.Count)
            {
                float lh0 = LineHeight(atoms[i].S);
                float left, right;
                for (int guard = 0; ; guard++)
                {
                    Range(bfc, y, lh0, cx, cw, out left, out right);
                    if (right - left >= Math.Min(40f, cw) || guard > 30) break;
                    float next = NextFloatBottom(bfc, y);
                    if (next <= y) break;
                    y = next;
                }
                float avail = Math.Max(10f, right - left);
                var line = new List<Atom>();
                float used = 0f;
                int lastSpace = -1;
                bool nowrap = false;
                while (i < atoms.Count)
                {
                    Atom a = atoms[i];
                    if (a is Break) { i++; break; }
                    float w = a is Word wd ? wd.W : a is Space sp ? sp.W : ((Thing)a).W;
                    nowrap = a.S.WhiteSpace == "pre" || a.S.WhiteSpace == "nowrap";
                    if (a is Space && line.Count == 0 && !nowrap) { i++; continue; }
                    if (used + w > avail && line.Count > 0 && !nowrap)
                    {
                        if (a is Space) { i++; }
                        else if (lastSpace >= 0 && false) { }
                        break;
                    }
                    if (a is Word big && big.W > avail && big.W > cw + 1f && !nowrap)
                    {
                        // a long word (an address): cut it to fit, as a browser would with overflow-wrap
                        float per = big.W / Math.Max(1, big.Text.Length);
                        int fit = Math.Max(1, (int)((avail - used) / Math.Max(1f, per)));
                        if (fit < big.Text.Length)
                        {
                            if (used > 0f && fit < 3) break;
                            var head = new Word { S = big.S, Href = big.Href, Text = big.Text.Substring(0, fit), W = per * fit };
                            atoms[i] = new Word { S = big.S, Href = big.Href, Text = big.Text.Substring(fit), W = big.W - head.W };
                            line.Add(head); used += head.W;
                            break;
                        }
                    }
                    line.Add(a); used += w; i++;
                    if (a is Space) lastSpace = line.Count - 1;
                }
                while (line.Count > 0 && line[line.Count - 1] is Space s0) { used -= s0.W; line.RemoveAt(line.Count - 1); }
                if (line.Count == 0) { y += lh0; continue; }
                y = EmitLine(line, used, align, left, avail, y, bfc, cx, cw);
            }
            return y;
        }

        private float EmitLine(List<Atom> line, float used, string align, float left, float avail, float y, Bfc bfc, float cx, float cw)
        {
            float lineH = 0f;
            foreach (Atom a in line)
            {
                float h = a is Thing t ? t.H : LineHeight(a.S);
                if (h > lineH) lineH = h;
            }
            float x = left;
            if (align == "center") x += Math.Max(0f, (avail - used) / 2f);
            else if (align == "right") x += Math.Max(0f, avail - used);

            int i = 0;
            while (i < line.Count)
            {
                Atom a = line[i];
                if (a is Thing th)
                {
                    float ty = y;
                    if (th.Img)
                    {
                        PictureSize(th.N, cw, out string url);
                        float py = th.S.VerticalAlign == "middle" ? y + (lineH - th.H) / 2f : y + lineH - th.H;
                        AddPicture(th.N, url, x, py, th.W, th.H, th.Href);
                    }
                    else
                    {
                        Style ts = th.N.Style;
                        float ml = ts.MarginL.Auto ? 0f : ts.MarginL.Resolve(cw, 0f), mt = ts.MarginT.Resolve(cw, 0f);
                        var sw = ts.Width; ts.Width = Len.Px(th.W - ts.BorderL - ts.BorderR - ts.PadL - ts.PadR - ml - (ts.MarginR.Auto ? 0f : ts.MarginR.Resolve(cw, 0f)));
                        float savedML = ts.MarginL.V; bool aml = ts.MarginL.Auto; ts.MarginL = Len.Px(0);
                        float bottom = th.N.Style.Display == "table" ? Table(th.N, x + ml, th.W, ty, new Bfc()) : Box(th.N, x + ml, th.W - ml, ty + mt, new Bfc());
                        ts.Width = sw; ts.MarginL = aml ? Len.None : Len.Px(savedML);
                        if (bottom - y > lineH) lineH = bottom - y;
                    }
                    x += th.W; i++;
                    continue;
                }
                // a run: the following words and spaces in the same style
                int j = i;
                var sb = new StringBuilder();
                float w = 0f;
                while (j < line.Count && !(line[j] is Thing) && SameStyle(line[j], a))
                {
                    if (line[j] is Word wd) { sb.Append(wd.Text); w += wd.W; }
                    else if (line[j] is Space sp) { sb.Append(' '); w += sp.W; }
                    j++;
                }
                if (sb.Length > 0 && a.S.Visible)
                {
                    float size = a.S.FontSize;
                    float top = y + (LineHeight(a.S) - size * 1.2f) / 2f + (lineH - LineHeight(a.S)) * 0.5f;
                    Items.Add(new Item { Kind = 2, Text = sb.ToString().Replace(' ', ' '), X = x, Y = top, W = w, H = size * 1.2f, Size = size, Bold = a.S.Bold, Italic = a.S.Italic, C = a.S.Color });
                    if (a.S.Underline) Items.Add(new Item { Kind = 1, X = x, Y = top + size * 1.08f, W = w, H = Math.Max(1f, size / 14f), C = a.S.Color });
                    if (a.S.Strike) Items.Add(new Item { Kind = 1, X = x, Y = top + size * 0.62f, W = w, H = Math.Max(1f, size / 14f), C = a.S.Color });
                    if (a.Href != null) Links.Add(new Link { X = x, Y = y, W = w, H = lineH, Href = a.Href });
                }
                x += w;
                i = Math.Max(j, i + 1);
            }
            return y + lineH;
        }

        private static bool SameStyle(Atom a, Atom b)
        {
            Style x = a.S, y = b.S;
            return ReferenceEquals(x, y) || (x.FontSize == y.FontSize && x.Bold == y.Bold && x.Italic == y.Italic && x.Color == y.Color && x.Underline == y.Underline && x.Strike == y.Strike && x.Visible == y.Visible && a.Href == b.Href);
        }

        // ---- measuring (for floats, inline boxes and table columns) ----

        /// <summary>How wide a box wants to be if nothing wraps (a table column's "maximum"), content only.</summary>
        private float MaxContent(Node n, float cw)
        {
            if (n.IsText) return Fonts.Width(Dom.Collapse(n.Text), n.Parent.Style.FontSize, n.Parent.Style.Bold) + 0f;
            if (Hidden(n)) return 0f;
            Style s = n.Style;
            if (n.Tag == "img") return PictureSize(n, cw, out _).x;
            if (!s.Width.Auto && !s.Width.Percent) return s.Width.V;
            if (s.Display == "table") return TableWidths(n, cw, out _, out _);
            float best = 0f, line = 0f;
            foreach (Node k in n.Kids)
            {
                if (Hidden(k)) continue;
                if (!k.IsText && (IsBlockLevel(k) || k.Tag == "br"))
                {
                    best = Math.Max(best, line); line = 0f;
                    if (k.Tag != "br") best = Math.Max(best, MaxContent(k, cw) + Extras(k, cw));
                    continue;
                }
                line += MaxContent(k, cw) + (k.IsText ? 0f : Extras(k, cw));
            }
            return Math.Max(best, line);
        }

        private float Extras(Node k, float cw)
        {
            Style s = k.Style;
            return s.BorderL + s.BorderR + s.PadL + s.PadR + (s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f)) + (s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f));
        }

        /// <summary>The narrowest a box can get: its longest word or widest picture.</summary>
        private float MinContent(Node n, float cw)
        {
            if (n.IsText)
            {
                float best = 0f;
                foreach (string word in n.Text.Split(' ', '\t', '\n', '\r')) best = Math.Max(best, Fonts.Width(word, n.Parent.Style.FontSize, n.Parent.Style.Bold));
                return best;
            }
            if (Hidden(n)) return 0f;
            if (n.Tag == "img") return Math.Min(PictureSize(n, cw, out _).x, 120f);
            Style s = n.Style;
            if (!s.Width.Auto && !s.Width.Percent) return s.Width.V;
            float m = 0f;
            foreach (Node k in n.Kids) m = Math.Max(m, MinContent(k, cw) + (k.IsText ? 0f : Extras(k, cw)));
            return m;
        }

        // ---- tables ----

        private sealed class Cell { public Node N; public int Col, Span, RowSpan; public float Min, Max; }

        private static List<List<Cell>> Rows(Node table)
        {
            var rows = new List<List<Cell>>();
            void Walk(Node n)
            {
                foreach (Node k in n.Kids)
                {
                    if (k.IsText || Hidden(k)) continue;
                    if (k.Tag == "tr")
                    {
                        var row = new List<Cell>();
                        int col = 0;
                        foreach (Node c in k.Kids)
                        {
                            if (c.IsText || Hidden(c) || (c.Tag != "td" && c.Tag != "th")) continue;
                            int span = 1; int.TryParse(c.Get("colspan"), out span); if (span < 1) span = 1; if (span > 30) span = 30;
                            row.Add(new Cell { N = c, Col = col, Span = span });
                            col += span;
                        }
                        rows.Add(row);
                    }
                    else if (k.Tag == "thead" || k.Tag == "tbody" || k.Tag == "tfoot") Walk(k);
                }
            }
            Walk(table);
            return rows;
        }

        /// <summary>The column widths a table wants: fills mins and maxes and returns the table's natural (maximum) width.</summary>
        private float TableWidths(Node table, float cw, out List<List<Cell>> rows, out float[] max)
        {
            rows = Rows(table);
            int cols = 0;
            foreach (List<Cell> r in rows) foreach (Cell c in r) cols = Math.Max(cols, c.Col + c.Span);
            max = new float[cols];
            var min = new float[cols];
            float pad = TablePad(table);
            foreach (List<Cell> r in rows)
                foreach (Cell c in r)
                {
                    float extra = c.N.Style.PadL + c.N.Style.PadR + c.N.Style.BorderL + c.N.Style.BorderR + pad * 2f;
                    float mx = (!c.N.Style.Width.Auto && !c.N.Style.Width.Percent ? c.N.Style.Width.V : MaxContent(c.N, cw)) + extra, mn = MinContent(c.N, cw) + extra;
                    c.Min = mn; c.Max = Math.Max(mn, mx);
                    if (c.Span == 1) { max[c.Col] = Math.Max(max[c.Col], c.Max); min[c.Col] = Math.Max(min[c.Col], c.Min); }
                }
            foreach (List<Cell> r in rows)
                foreach (Cell c in r)
                    if (c.Span > 1)
                    {
                        float have = 0f; for (int k = 0; k < c.Span && c.Col + k < cols; k++) have += max[c.Col + k];
                        if (have < c.Max) for (int k = 0; k < c.Span && c.Col + k < cols; k++) max[c.Col + k] += (c.Max - have) / c.Span;
                    }
            _minCols = min;
            float spacing = table.Style.BorderSpacing;
            float total = spacing;
            for (int k = 0; k < cols; k++) total += max[k] + spacing;
            return total;
        }

        private float[] _minCols;

        private static float TablePad(Node table) => float.TryParse(table.Get("cellpadding"), out float p) ? p : 0f;

        private float Table(Node table, float cx, float cw, float y, Bfc bfc)
        {
            Style s = table.Style;
            float natural = TableWidths(table, cw, out List<List<Cell>> rows, out float[] max);
            float[] min = _minCols;
            int cols = max.Length;
            if (cols == 0) return y;
            float spacing = s.BorderSpacing;
            if (float.TryParse(table.Get("cellspacing"), out float cs)) spacing = cs;
            float ml = s.MarginL.Auto ? 0f : s.MarginL.Resolve(cw, 0f), mr = s.MarginR.Auto ? 0f : s.MarginR.Resolve(cw, 0f);
            float avail = cw - ml - mr;
            float width = !s.Width.Auto ? s.Width.Resolve(cw, natural) : Math.Min(natural, avail);
            if (!s.MaxWidth.Auto) width = Math.Min(width, s.MaxWidth.Resolve(cw, width));
            string wattr = table.Get("width"); if (s.Width.Auto && wattr != null) { float wa = ParseAttr(wattr, avail); if (wa > 0f) width = wa; }
            width = Math.Max(10f, Math.Min(width, avail));
            float x0 = cx + ml;
            if (s.MarginL.Auto && s.MarginR.Auto) x0 = cx + (avail - width) / 2f + 0f;
            else if (table.Get("align") == "center") x0 = cx + (avail - width) / 2f;
            else if (table.Get("align") == "right") x0 = cx + avail - width;

            // column widths: between each column's minimum and maximum, as far as the table's width allows
            float inner = width - spacing * (cols + 1);
            float sumMin = 0f, sumMax = 0f;
            for (int k = 0; k < cols; k++) { sumMin += min[k]; sumMax += max[k]; }
            var colW = new float[cols];
            if (inner >= sumMax) { float extra = sumMax > 0f ? (inner - sumMax) : 0f; for (int k = 0; k < cols; k++) colW[k] = max[k] + (sumMax > 0f ? extra * max[k] / sumMax : inner / cols); }
            else if (inner <= sumMin) { for (int k = 0; k < cols; k++) colW[k] = sumMin > 0f ? min[k] * inner / sumMin : inner / cols; }
            else { float t = (inner - sumMin) / Math.Max(1f, sumMax - sumMin); for (int k = 0; k < cols; k++) colW[k] = min[k] + (max[k] - min[k]) * t; }
            var colX = new float[cols];
            float px = spacing;
            for (int k = 0; k < cols; k++) { colX[k] = px; px += colW[k] + spacing; }

            // a caption goes above
            foreach (Node k in table.Kids) if (!k.IsText && k.Tag == "caption" && !Hidden(k)) y = Box(k, x0, width, y, bfc);

            Item tableBg = Reserve();
            float startY = y;
            float cy = y + spacing;
            int borderAttr = int.TryParse(table.Get("border"), out int b) ? b : 0;
            float pad = TablePad(table);
            foreach (List<Cell> row in rows)
            {
                float rowH = 0f;
                var slots = new List<KeyValuePair<Cell, Item>>();
                var heights = new List<float>();
                var starts = new List<int>();
                foreach (Cell c in row)
                {
                    if (c.Col >= cols) continue;
                    float w = 0f; for (int k = 0; k < c.Span && c.Col + k < cols; k++) w += colW[c.Col + k] + (k > 0 ? spacing : 0f);
                    Style cs2 = c.N.Style;
                    if (borderAttr > 0 && cs2.BorderL == 0f && cs2.BorderT == 0f) { cs2.BorderL = cs2.BorderR = cs2.BorderT = cs2.BorderB = 1f; cs2.BorderColor = new Color(0.5f, 0.5f, 0.5f); }
                    if (pad > 0f && cs2.PadL == 0f && cs2.PadT == 0f) cs2.PadL = cs2.PadR = cs2.PadT = cs2.PadB = pad;
                    int start = Items.Count;
                    Item slot = Reserve();
                    float cellX = x0 + colX[c.Col];
                    float bottom = CellContent(c.N, cellX, w, cy, slot);
                    slots.Add(new KeyValuePair<Cell, Item>(c, slot));
                    heights.Add(bottom - cy);
                    starts.Add(start);
                    rowH = Math.Max(rowH, bottom - cy);
                }
                // every cell is as tall as the row; cells that ask for it are centred or bottom-aligned in it
                for (int k = 0; k < slots.Count; k++)
                {
                    Cell c = slots[k].Key; Item slot = slots[k].Value;
                    Style cs3 = c.N.Style;
                    if (slot.Kind == 1) slot.H = rowH;
                    string va = c.N.Get("valign") ?? cs3.VerticalAlign;
                    float dy = va == "middle" || va == "center" ? (rowH - heights[k]) / 2f : va == "bottom" ? rowH - heights[k] : 0f;
                    if (dy > 0.5f) for (int q = starts[k] + 1; q < Items.Count && (k + 1 >= starts.Count || q < starts[k + 1]); q++) Items[q].Y += dy;
                    if (cs3.Visible) Borders(cs3, x0 + colX[c.Col], cy, slot.W, rowH);
                }
                cy += rowH + spacing;
            }
            if (s.Visible && s.Background.HasValue && s.Background.Value.a > 0.01f) Fill(tableBg, x0, startY, width, cy - startY, s.Background.Value);
            return cy;
        }

        /// <summary>A table cell's box: background (in the reserved slot, as tall as the row once the row is known), padding and its content. Returns its bottom.</summary>
        private float CellContent(Node cell, float x, float w, float y, Item slot)
        {
            Style s = cell.Style;
            float contentX = x + s.BorderL + s.PadL, contentW = Math.Max(0f, w - s.BorderL - s.BorderR - s.PadL - s.PadR);
            float contentY = y + s.BorderT + s.PadT;
            float bottom = Children(cell, contentX, contentW, contentY, new Bfc());
            float h = s.BorderT + s.PadT + Math.Max(0f, bottom - contentY) + s.PadB + s.BorderB;
            if (!s.Height.Auto && !s.Height.Percent) h = Math.Max(h, s.Height.V);
            if (s.Visible && s.Background.HasValue && s.Background.Value.a > 0.01f) Fill(slot, x, y, w, h, s.Background.Value);
            else slot.W = w;
            slot.W = w;
            return y + h;
        }
    }
}
