using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Terminal
{
    /// <summary>A CSS length: a number with a unit, or "auto". Em and rem are turned into pixels when the style is computed; percents wait for layout.</summary>
    internal struct Len
    {
        public float V;
        public bool Auto, Percent;
        public static readonly Len None = new Len { Auto = true };
        public static Len Px(float v) => new Len { V = v };
        public float Resolve(float basis, float fallback) => Auto ? fallback : Percent ? basis * V / 100f : V;
    }

    /// <summary>What a stylesheet says about one element, after inheritance. Only what the renderer uses.</summary>
    internal sealed class Style
    {
        // inherited
        public Color Color = Color.black;
        public float FontSize = 16f, LineHeight;        // pixels; line height 0 means "normal"
        public bool Bold, Italic, Underline, Strike, Visible = true, Mono, FlexColumn;
        public string TextAlign = "left", WhiteSpace = "normal", ListStyle = "disc", Transform = "none";

        // not inherited
        public string Display = "inline", Float = "none", Clear = "none", Position = "static", VerticalAlign = "baseline";
        public Len MarginT = Len.Px(0), MarginR = Len.Px(0), MarginB = Len.Px(0), MarginL = Len.Px(0);
        public float PadT, PadR, PadB, PadL, BorderT, BorderR, BorderB, BorderL;
        public Color BorderColor = Color.black;
        public Color? Background;
        public Len Width = Len.None, Height = Len.None, MaxWidth = Len.None, MinWidth = Len.None;
        public float BorderSpacing = 2f;

        public Style Inherit()
        {
            return new Style
            {
                Color = Color, FontSize = FontSize, LineHeight = LineHeight, Bold = Bold, Italic = Italic, Underline = Underline, Strike = Strike,
                Visible = Visible, Mono = Mono, TextAlign = TextAlign, WhiteSpace = WhiteSpace, ListStyle = ListStyle, Transform = Transform,
            };
        }
    }

    /// <summary>
    /// CSS: reads stylesheets (rules, @media for a screen about the size of the terminal's, custom properties and var()), matches their
    /// selectors against the page (rules are indexed by id, class and tag so a big site's sheet stays fast), and computes each element's
    /// Style: user-agent defaults, then the page's rules by specificity and order, then the element's own style attribute. What it doesn't
    /// understand (calc(), pseudo-classes, grid, flex) is skipped, never an error.
    /// </summary>
    internal sealed class Css
    {
        internal const float ViewportWidth = 900f;

        private sealed class Compound
        {
            public string Tag, Id; public List<string> Classes = new List<string>(); public List<string[]> Attrs = new List<string[]>();   // [name, op, value]
            public bool FirstChild, LastChild;
            public List<Compound> Not = new List<Compound>();
        }

        private sealed class Selector
        {
            public readonly List<Compound> Parts = new List<Compound>();      // left to right
            public readonly List<char> Combinators = new List<char>();         // between parts: ' ' or '>' or '+'
            public int Specificity;
        }

        private sealed class Rule
        {
            public Selector Sel; public Dictionary<string, string> Decls; public int Order;
        }

        private readonly Dictionary<string, List<Rule>> _byId = new Dictionary<string, List<Rule>>(), _byClass = new Dictionary<string, List<Rule>>(), _byTag = new Dictionary<string, List<Rule>>();
        private readonly List<Rule> _universal = new List<Rule>();
        private readonly Dictionary<string, string> _vars = new Dictionary<string, string>();
        private int _order;

        // ---- reading sheets ----

        internal void AddSheet(string text, bool userAgent = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (text.Length > 600000) text = text.Substring(0, 600000);
            text = StripComments(text);
            ParseBlock(text, userAgent);
        }

        private static string StripComments(string s)
        {
            if (s.IndexOf("/*", StringComparison.Ordinal) < 0) return s;
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                int c = s.IndexOf("/*", i, StringComparison.Ordinal);
                if (c < 0) { sb.Append(s, i, s.Length - i); break; }
                sb.Append(s, i, c - i);
                int e = s.IndexOf("*/", c + 2, StringComparison.Ordinal);
                i = e < 0 ? s.Length : e + 2;
            }
            return sb.ToString();
        }

        private void ParseBlock(string s, bool ua)
        {
            int i = 0, n = s.Length;
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(s[i])) i++;
                if (i >= n) break;
                if (s[i] == '@')
                {
                    int nameEnd = i + 1;
                    while (nameEnd < n && (char.IsLetter(s[nameEnd]) || s[nameEnd] == '-')) nameEnd++;
                    string at = s.Substring(i + 1, nameEnd - i - 1).ToLowerInvariant();
                    int brace = IndexOfAny(s, nameEnd, '{', ';');
                    if (brace < 0) break;
                    if (s[brace] == ';') { i = brace + 1; continue; }   // @import, @charset: not followed
                    int end = MatchBrace(s, brace);
                    string prelude = s.Substring(nameEnd, brace - nameEnd).Trim().ToLowerInvariant();
                    string body = s.Substring(brace + 1, Math.Max(0, end - brace - 1));
                    if (at == "media" && MediaMatches(prelude)) ParseBlock(body, ua);
                    else if (at == "supports") { /* not followed */ }
                    i = end + 1;
                    continue;
                }
                int open = s.IndexOf('{', i);
                if (open < 0) break;
                int close = MatchBrace(s, open);
                string selectorText = s.Substring(i, open - i).Trim();
                string declText = s.Substring(open + 1, Math.Max(0, close - open - 1));
                i = close + 1;
                AddRule(selectorText, declText, ua);
            }
        }

        private static int IndexOfAny(string s, int from, char a, char b)
        {
            for (int i = from; i < s.Length; i++) if (s[i] == a || s[i] == b) return i;
            return -1;
        }

        private static int MatchBrace(string s, int open)
        {
            int depth = 0;
            for (int i = open; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return s.Length;
        }

        /// <summary>@media: for a screen the size of the terminal's (a laptop-sized window); print and anything not understood leaves the block out.</summary>
        private static bool MediaMatches(string q)
        {
            if (q.Length == 0) return true;
            foreach (string part in q.Split(','))
            {
                string p = part.Trim();
                if (p.Contains("print") || p.Contains("speech") || p.Contains("prefers-") || p.Contains("hover") || p.Contains("pointer") || p.Contains("orientation") || p.Contains("resolution") || p.Contains("-webkit") ) continue;
                bool ok = true;
                int at = 0;
                while ((at = p.IndexOf('(', at)) >= 0)
                {
                    int end = p.IndexOf(')', at);
                    if (end < 0) { ok = false; break; }
                    string cond = p.Substring(at + 1, end - at - 1);
                    at = end + 1;
                    int colon = cond.IndexOf(':');
                    if (colon < 0) { ok = false; break; }
                    string key = cond.Substring(0, colon).Trim(), val = cond.Substring(colon + 1).Trim();
                    float v = ParseNumber(val, 16f);
                    if (key == "min-width") ok &= ViewportWidth >= v;
                    else if (key == "max-width") ok &= ViewportWidth <= v;
                    else if (key == "min-height" || key == "max-height") ok &= true;
                    else { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        private void AddRule(string selectorText, string declText, bool ua)
        {
            Dictionary<string, string> decls = ParseDeclarations(declText);
            if (decls.Count == 0) return;
            foreach (string part in SplitTop(selectorText, ','))
            {
                string text = part.Trim();
                if (text.Length == 0) continue;
                // custom properties are read from :root, html and body (the page's palette)
                if (text == ":root" || text == "html" || text == "body" || text.StartsWith(":root") && text.Length < 24)
                    foreach (KeyValuePair<string, string> kv in decls) if (kv.Key.StartsWith("--")) _vars[kv.Key] = kv.Value;
                Selector sel = ParseSelector(text);
                if (sel == null) continue;
                var rule = new Rule { Sel = sel, Decls = decls, Order = _order++ + (ua ? 0 : 100000) };
                Compound last = sel.Parts[sel.Parts.Count - 1];
                if (last.Id != null) Index(_byId, last.Id, rule);
                else if (last.Classes.Count > 0) Index(_byClass, last.Classes[last.Classes.Count - 1], rule);
                else if (last.Tag != null) Index(_byTag, last.Tag, rule);
                else _universal.Add(rule);
            }
        }

        private static void Index(Dictionary<string, List<Rule>> map, string key, Rule rule)
        {
            if (!map.TryGetValue(key, out List<Rule> list)) map[key] = list = new List<Rule>();
            list.Add(rule);
        }

        private static List<string> SplitTop(string s, char sep)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            char quote = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '"' || c == '\'') quote = c;
                else if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == sep && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        private static Dictionary<string, string> ParseDeclarations(string text)
        {
            var d = new Dictionary<string, string>();
            foreach (string decl in SplitTop(text, ';'))
            {
                int colon = decl.IndexOf(':');
                if (colon <= 0) continue;
                string name = decl.Substring(0, colon).Trim().ToLowerInvariant();
                string value = decl.Substring(colon + 1).Trim();
                int imp = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
                if (imp >= 0) value = value.Substring(0, imp).Trim();
                if (name.Length > 0 && value.Length > 0) d[name] = value;
            }
            return d;
        }

        private static Selector ParseSelector(string text)
        {
            var sel = new Selector();
            int i = 0, n = text.Length;
            char pending = ' ';
            while (i < n)
            {
                while (i < n && char.IsWhiteSpace(text[i])) { i++; }
                if (i >= n) break;
                if (text[i] == '>' || text[i] == '+' || text[i] == '~')
                {
                    if (text[i] == '~') return null;   // (general sibling: not followed)
                    pending = text[i]; i++; continue;
                }
                var c = new Compound();
                bool any = false;
                while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '>' && text[i] != '+' && text[i] != '~')
                {
                    char ch = text[i];
                    if (ch == '*') { i++; any = true; }
                    else if (ch == '#') { i++; c.Id = Ident(text, ref i); sel.Specificity += 10000; any = true; }
                    else if (ch == '.') { i++; c.Classes.Add(Ident(text, ref i)); sel.Specificity += 100; any = true; }
                    else if (ch == '[')
                    {
                        int end = text.IndexOf(']', i);
                        if (end < 0) return null;
                        string inner = text.Substring(i + 1, end - i - 1);
                        i = end + 1;
                        string[] attr = ParseAttr(inner);
                        if (attr == null) return null;
                        c.Attrs.Add(attr); sel.Specificity += 100; any = true;
                    }
                    else if (ch == ':')
                    {
                        int s2 = i;
                        i++;
                        if (i < n && text[i] == ':') return null;   // pseudo-elements: not drawn
                        string pseudo = Ident(text, ref i).ToLowerInvariant();
                        if (i < n && text[i] == '(')
                        {
                            int close = text.IndexOf(')', i);
                            if (close < 0 || pseudo != "not") return null;
                            string innerSel = text.Substring(i + 1, close - i - 1).Trim();
                            i = close + 1;
                            if (innerSel == ":focus" || innerSel == ":hover" || innerSel == ":active" || innerSel == ":focus-within" || innerSel == ":focus-visible" || innerSel == ":target" || innerSel == ":disabled") continue;   // (never in effect here: so "not" is always true)
                            Selector one = ParseSelector(innerSel);
                            if (one == null || one.Parts.Count != 1) return null;
                            c.Not.Add(one.Parts[0]);
                            sel.Specificity += 100; any = true;
                            continue;
                        }
                        if (pseudo == "link" || pseudo == "visited" || pseudo == "any-link") { any = true; continue; }
                        if (pseudo == "first-child") c.FirstChild = true;
                        else if (pseudo == "last-child") c.LastChild = true;
                        else if (pseudo == "root") { c.Tag = "html"; }
                        else return null;   // :hover, :focus, :not(), ...
                        sel.Specificity += 100; any = true;
                    }
                    else if (char.IsLetter(ch)) { c.Tag = Ident(text, ref i).ToLowerInvariant(); sel.Specificity += 1; any = true; }
                    else return null;
                }
                if (!any) return null;
                if (sel.Parts.Count > 0) sel.Combinators.Add(pending);
                sel.Parts.Add(c);
                pending = ' ';
            }
            return sel.Parts.Count > 0 ? sel : null;
        }

        private static string Ident(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '-' || s[i] == '_' || s[i] == '\\')) i++;
            return s.Substring(start, i - start);
        }

        private static string[] ParseAttr(string inner)
        {
            int op = -1; string kind = "";
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '=') { op = i; kind = "="; if (i > 0 && "~|^$*".IndexOf(inner[i - 1]) >= 0) { kind = inner[i - 1] + "="; op = i - 1; } break; }
            }
            if (op < 0) return new[] { inner.Trim().ToLowerInvariant(), "", "" };
            string name = inner.Substring(0, op).Trim().ToLowerInvariant();
            string value = inner.Substring(op + kind.Length).Trim().Trim('"', '\'');
            int flag = value.LastIndexOf(' ');
            if (flag > 0 && value.EndsWith(" i")) value = value.Substring(0, flag);
            return new[] { name, kind, value };
        }

        // ---- matching ----

        private bool Matches(Compound c, Node n)
        {
            if (c.Tag != null && n.Tag != c.Tag) return false;
            if (c.Id != null && n.Get("id") != c.Id) return false;
            foreach (string cls in c.Classes) if (!n.HasClass(cls)) return false;
            foreach (string[] a in c.Attrs)
            {
                string v = n.Get(a[0]);
                if (v == null) return false;
                switch (a[1])
                {
                    case "": break;
                    case "=": if (v != a[2]) return false; break;
                    case "~=": if (Array.IndexOf(v.Split(' '), a[2]) < 0) return false; break;
                    case "^=": if (!v.StartsWith(a[2])) return false; break;
                    case "$=": if (!v.EndsWith(a[2])) return false; break;
                    case "*=": if (!v.Contains(a[2])) return false; break;
                    case "|=": if (v != a[2] && !v.StartsWith(a[2] + "-")) return false; break;
                }
            }
            foreach (Compound not in c.Not) if (Matches(not, n)) return false;
            if (c.FirstChild || c.LastChild)
            {
                Node p = n.Parent; if (p == null) return false;
                Node first = null, last = null;
                foreach (Node k in p.Kids) if (!k.IsText) { if (first == null) first = k; last = k; }
                if (c.FirstChild && first != n) return false;
                if (c.LastChild && last != n) return false;
            }
            return true;
        }

        private bool MatchesFrom(Selector s, int index, Node n)
        {
            if (!Matches(s.Parts[index], n)) return false;
            if (index == 0) return true;
            char comb = s.Combinators[index - 1];
            if (comb == '>') return n.Parent != null && MatchesFrom(s, index - 1, n.Parent);
            if (comb == '+')
            {
                Node prev = null;
                if (n.Parent == null) return false;
                foreach (Node k in n.Parent.Kids) { if (k == n) break; if (!k.IsText) prev = k; }
                return prev != null && MatchesFrom(s, index - 1, prev);
            }
            for (Node up = n.Parent; up != null; up = up.Parent) if (MatchesFrom(s, index - 1, up)) return true;
            return false;
        }

        // ---- computing styles ----

        /// <summary>Compute every element's style under this node (parents first).</summary>
        internal void Apply(Node node, Style parent)
        {
            if (node.IsText) return;
            Style style = parent != null ? parent.Inherit() : new Style();
            var rules = new List<Rule>();
            string id = node.Get("id");
            if (id != null && _byId.TryGetValue(id, out List<Rule> byId)) rules.AddRange(byId);
            string cls = node.Get("class");
            if (cls != null) foreach (string c in cls.Split(' ', '\t', '\n', '\r')) if (c.Length > 0 && _byClass.TryGetValue(c, out List<Rule> byClass)) rules.AddRange(byClass);
            if (_byTag.TryGetValue(node.Tag, out List<Rule> byTag)) rules.AddRange(byTag);
            rules.AddRange(_universal);
            var matched = new List<Rule>();
            foreach (Rule r in rules) if (MatchesFrom(r.Sel, r.Sel.Parts.Count - 1, node)) matched.Add(r);
            matched.Sort((a, b) => a.Order >= 100000 == (b.Order >= 100000) ? (a.Sel.Specificity != b.Sel.Specificity ? a.Sel.Specificity.CompareTo(b.Sel.Specificity) : a.Order.CompareTo(b.Order)) : a.Order.CompareTo(b.Order));
            float parentSize = parent != null ? parent.FontSize : 16f;
            foreach (Rule r in matched) Declare(style, r.Decls, parentSize);
            string inline = node.Get("style");
            if (inline != null) Declare(style, ParseDeclarations(inline), parentSize);
            if (node.Has("hidden")) style.Display = "none";
            string align = node.Get("align");
            if (align != null && (node.Tag == "div" || node.Tag == "p" || node.Tag == "td" || node.Tag == "th" || node.Tag == "table" || node.Tag == "center" || node.Tag == "h1" || node.Tag == "h2" || node.Tag == "h3")) style.TextAlign = align.ToLowerInvariant();
            string bg = node.Get("bgcolor");
            if (bg != null && TryColor(bg, out Color bgc)) style.Background = bgc;
            if (node.Tag == "font")
            {
                string fc = node.Get("color"); if (fc != null && TryColor(fc, out Color fcc)) style.Color = fcc;
            }
            node.Style = style;
            foreach (Node kid in node.Kids) Apply(kid, style);
        }

        /// <summary>light-dark(a, b) picks the light colour (the terminal's pages are drawn as for a light scheme).</summary>
        private static string LightDark(string value)
        {
            for (int guard = 0; guard < 4; guard++)
            {
                int at = value.IndexOf("light-dark(", StringComparison.Ordinal);
                if (at < 0) return value;
                int depth = 0, comma = -1, end = -1;
                for (int i = at + 10; i < value.Length; i++)
                {
                    char c = value[i];
                    if (c == '(') depth++;
                    else if (c == ')') { depth--; if (depth == 0) { end = i; break; } }
                    else if (c == ',' && depth == 1 && comma < 0) comma = i;
                }
                if (end < 0 || comma < 0) return value;
                value = value.Substring(0, at) + value.Substring(at + 11, comma - at - 11).Trim() + value.Substring(end + 1);
            }
            return value;
        }

        private string ResolveVars(string value)
        {
            value = LightDark(value);
            for (int guard = 0; guard < 6; guard++)
            {
                int at = value.IndexOf("var(", StringComparison.Ordinal);
                if (at < 0) return value;
                int depth = 0, end = -1;
                for (int i = at + 3; i < value.Length; i++)
                {
                    if (value[i] == '(') depth++;
                    else if (value[i] == ')') { depth--; if (depth == 0) { end = i; break; } }
                }
                if (end < 0) return null;
                string inner = value.Substring(at + 4, end - at - 4);
                int comma = inner.IndexOf(',');
                string name = (comma < 0 ? inner : inner.Substring(0, comma)).Trim(), fallback = comma < 0 ? null : inner.Substring(comma + 1).Trim();
                string replacement = _vars.TryGetValue(name, out string v) ? v : fallback;
                if (replacement == null) return null;
                value = value.Substring(0, at) + replacement + value.Substring(end + 1);
            }
            return null;
        }

        private void Declare(Style s, Dictionary<string, string> decls, float parentSize)
        {
            // font-size first: em lengths in the other properties are relative to it
            if (decls.TryGetValue("font-size", out string fs)) { string v = ResolveVars(fs); if (v != null) FontSize(s, v, parentSize); }
            foreach (KeyValuePair<string, string> kv in decls)
            {
                if (kv.Key == "font-size" || kv.Key.StartsWith("--")) continue;
                string value = ResolveVars(kv.Value);
                if (value == null || value.Contains("calc(") || value.Contains("clamp(") || value.Contains("min(") || value.Contains("max(")) continue;
                try { One(s, kv.Key, value.Trim(), parentSize); } catch (Exception) { /* a value we can't read is ignored */ }
            }
        }

        private static void FontSize(Style s, string v, float parentSize)
        {
            v = v.ToLowerInvariant();
            switch (v)
            {
                case "xx-small": s.FontSize = 9f; return; case "x-small": s.FontSize = 10f; return; case "small": s.FontSize = 13f; return;
                case "medium": s.FontSize = 16f; return; case "large": s.FontSize = 18f; return; case "x-large": s.FontSize = 24f; return;
                case "xx-large": s.FontSize = 32f; return; case "smaller": s.FontSize = parentSize * 0.83f; return; case "larger": s.FontSize = parentSize * 1.2f; return;
            }
            Len l = ParseLen(v, parentSize, parentSize);
            if (!l.Auto) s.FontSize = Mathf.Max(6f, l.Percent ? parentSize * l.V / 100f : l.V);
        }

        private static void One(Style s, string name, string v, float parentSize)
        {
            string lower = v.ToLowerInvariant();
            float em = s.FontSize;
            switch (name)
            {
                case "color": if (TryColor(lower, out Color c)) s.Color = c; else if (lower == "inherit") { } break;
                case "background-color": if (TryColor(lower, out Color bc)) s.Background = bc; break;
                case "background": { Color? f = FirstColor(lower); if (f.HasValue) s.Background = f; else if (lower.StartsWith("none") || lower.StartsWith("transparent")) s.Background = null; break; }
                case "display": s.Display = lower.Split(' ')[0]; break;
                case "flex-direction": s.FlexColumn = lower.StartsWith("column"); break;
                case "visibility": s.Visible = lower != "hidden" && lower != "collapse"; break;
                case "float": s.Float = lower; break;
                case "clear": s.Clear = lower; break;
                case "position": s.Position = lower; break;
                case "vertical-align": s.VerticalAlign = lower; break;
                case "text-align": s.TextAlign = lower == "start" ? "left" : lower == "end" ? "right" : lower; break;
                case "white-space": s.WhiteSpace = lower; break;
                case "text-transform": s.Transform = lower; break;
                case "font-weight": s.Bold = lower == "bold" || lower == "bolder" || (float.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) && w >= 600f); break;
                case "font-style": s.Italic = lower == "italic" || lower == "oblique"; break;
                case "font-family": s.Mono = lower.Contains("mono") || lower.Contains("courier") || lower.Contains("consolas"); break;
                case "font":
                    {
                        if (lower.Contains("bold")) s.Bold = true; if (lower.Contains("italic")) s.Italic = true;
                        foreach (string tok in lower.Split(' ')) { string t = tok.Contains("/") ? tok.Substring(0, tok.IndexOf('/')) : tok; if (t.Length > 0 && (char.IsDigit(t[0]) || t[0] == '.') && !float.TryParse(t, out _)) { FontSize(s, t, parentSize); break; } }
                        break;
                    }
                case "line-height": { if (lower == "normal") s.LineHeight = 0f; else if (float.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out float lh)) s.LineHeight = lh * s.FontSize; else { Len l = ParseLen(lower, em, s.FontSize); if (!l.Auto) s.LineHeight = l.Percent ? s.FontSize * l.V / 100f : l.V; } break; }
                case "text-decoration": case "text-decoration-line": s.Underline = lower.Contains("underline"); s.Strike = lower.Contains("line-through"); break;
                case "list-style": case "list-style-type": s.ListStyle = lower.Contains("none") ? "none" : lower.Contains("decimal") ? "decimal" : lower.Contains("circle") ? "circle" : lower.Contains("square") ? "square" : lower.Contains("disc") ? "disc" : s.ListStyle; break;
                case "width": s.Width = ParseLen(lower, em, s.FontSize); break;
                case "height": s.Height = ParseLen(lower, em, s.FontSize); break;
                case "max-width": s.MaxWidth = ParseLen(lower, em, s.FontSize); break;
                case "min-width": s.MinWidth = ParseLen(lower, em, s.FontSize); break;
                case "margin": Four(lower, em, out s.MarginT, out s.MarginR, out s.MarginB, out s.MarginL, true); break;
                case "margin-top": s.MarginT = ParseLen(lower, em, s.FontSize); break;
                case "margin-right": s.MarginR = ParseLen(lower, em, s.FontSize); break;
                case "margin-bottom": s.MarginB = ParseLen(lower, em, s.FontSize); break;
                case "margin-left": s.MarginL = ParseLen(lower, em, s.FontSize); break;
                case "padding": { Four(lower, em, out Len a, out Len b, out Len c2, out Len d, false); s.PadT = a.V; s.PadR = b.V; s.PadB = c2.V; s.PadL = d.V; break; }
                case "padding-top": s.PadT = ParseLen(lower, em, s.FontSize).V; break;
                case "padding-right": s.PadR = ParseLen(lower, em, s.FontSize).V; break;
                case "padding-bottom": s.PadB = ParseLen(lower, em, s.FontSize).V; break;
                case "padding-left": s.PadL = ParseLen(lower, em, s.FontSize).V; break;
                case "border": case "border-top": case "border-right": case "border-bottom": case "border-left":
                    {
                        float width = 0f; Color col = s.Color; bool none = lower.Contains("none") || lower.Contains("hidden");
                        foreach (string tok in lower.Split(' '))
                        {
                            if (TryColor(tok, out Color tc)) col = tc;
                            else if (tok == "thin") width = 1f; else if (tok == "medium") width = 3f; else if (tok == "thick") width = 5f;
                            else { Len l = ParseLen(tok, em, s.FontSize); if (!l.Auto && !l.Percent && l.V > 0f) width = l.V; }
                        }
                        if (none) width = 0f; else if (width == 0f && !lower.Contains("0")) width = 1f;
                        if (name == "border" || name == "border-top") s.BorderT = width; if (name == "border" || name == "border-right") s.BorderR = width;
                        if (name == "border" || name == "border-bottom") s.BorderB = width; if (name == "border" || name == "border-left") s.BorderL = width;
                        s.BorderColor = col; break;
                    }
                case "border-width": { Four(lower, em, out Len a, out Len b, out Len c3, out Len d, false); s.BorderT = a.V; s.BorderR = b.V; s.BorderB = c3.V; s.BorderL = d.V; break; }
                case "border-color": if (TryColor(lower.Split(' ')[0], out Color bcl)) s.BorderColor = bcl; break;
                case "border-style": if (lower.StartsWith("none") || lower.StartsWith("hidden")) s.BorderT = s.BorderR = s.BorderB = s.BorderL = 0f; break;
                case "border-spacing": s.BorderSpacing = ParseLen(lower.Split(' ')[0], em, s.FontSize).V; break;
            }
        }

        private static void Four(string v, float em, out Len t, out Len r, out Len b, out Len l, bool allowAuto)
        {
            string[] p = v.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var a = new Len[4];
            for (int i = 0; i < Math.Min(4, p.Length); i++) a[i] = ParseLen(p[i], em, em);
            if (p.Length == 1) { a[1] = a[2] = a[3] = a[0]; }
            else if (p.Length == 2) { a[2] = a[0]; a[3] = a[1]; }
            else if (p.Length == 3) { a[3] = a[1]; }
            t = a[0]; r = a[1]; b = a[2]; l = a[3];
            if (!allowAuto) { if (t.Auto) t = Len.Px(0); if (r.Auto) r = Len.Px(0); if (b.Auto) b = Len.Px(0); if (l.Auto) l = Len.Px(0); }
        }

        // ---- values ----

        internal static Len ParseLen(string v, float em, float rootEm)
        {
            v = v.Trim().ToLowerInvariant();
            if (v == "auto" || v.Length == 0 || v == "inherit" || v == "initial" || v == "none" || v == "normal" || v == "fit-content" || v == "max-content" || v == "min-content") return Len.None;
            if (v == "0") return Len.Px(0f);
            int i = 0;
            while (i < v.Length && (char.IsDigit(v[i]) || v[i] == '.' || v[i] == '-' || v[i] == '+')) i++;
            if (i == 0 || !float.TryParse(v.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out float num)) return Len.None;
            string unit = v.Substring(i);
            switch (unit)
            {
                case "px": case "": return Len.Px(num);
                case "%": return new Len { V = num, Percent = true };
                case "em": return Len.Px(num * em);
                case "rem": return Len.Px(num * 16f);
                case "pt": return Len.Px(num * 1.333f);
                case "ex": return Len.Px(num * em * 0.5f);
                case "ch": return Len.Px(num * em * 0.5f);
                case "vw": return Len.Px(num * ViewportWidth / 100f);
                case "vh": return Len.Px(num * 4.1f);
                default: return Len.None;
            }
        }

        private static float ParseNumber(string v, float em) { Len l = ParseLen(v, em, em); return l.Auto ? 0f : l.V; }

        private static Color? FirstColor(string v)
        {
            foreach (string tok in SplitTop(v, ' ')) { if (TryColor(tok.Trim(), out Color c)) return c; }
            int rgb = v.IndexOf("rgb", StringComparison.Ordinal);
            if (rgb >= 0) { int end = v.IndexOf(')', rgb); if (end > 0 && TryColor(v.Substring(rgb, end - rgb + 1), out Color c)) return c; }
            return null;
        }

        private static readonly Dictionary<string, Color> Named = new Dictionary<string, Color>
        {
            ["black"] = Color.black, ["white"] = Color.white, ["red"] = new Color(1f, 0f, 0f), ["green"] = new Color(0f, 0.5f, 0f), ["blue"] = new Color(0f, 0f, 1f),
            ["yellow"] = new Color(1f, 1f, 0f), ["gray"] = new Color(0.5f, 0.5f, 0.5f), ["grey"] = new Color(0.5f, 0.5f, 0.5f), ["silver"] = new Color(0.75f, 0.75f, 0.75f),
            ["orange"] = new Color(1f, 0.65f, 0f), ["purple"] = new Color(0.5f, 0f, 0.5f), ["navy"] = new Color(0f, 0f, 0.5f), ["teal"] = new Color(0f, 0.5f, 0.5f),
            ["maroon"] = new Color(0.5f, 0f, 0f), ["lime"] = new Color(0f, 1f, 0f), ["aqua"] = new Color(0f, 1f, 1f), ["cyan"] = new Color(0f, 1f, 1f), ["fuchsia"] = new Color(1f, 0f, 1f),
            ["olive"] = new Color(0.5f, 0.5f, 0f), ["brown"] = new Color(0.65f, 0.16f, 0.16f), ["pink"] = new Color(1f, 0.75f, 0.8f), ["gold"] = new Color(1f, 0.84f, 0f),
            ["lightgray"] = new Color(0.83f, 0.83f, 0.83f), ["lightgrey"] = new Color(0.83f, 0.83f, 0.83f), ["darkgray"] = new Color(0.66f, 0.66f, 0.66f), ["darkgrey"] = new Color(0.66f, 0.66f, 0.66f),
            ["dimgray"] = new Color(0.41f, 0.41f, 0.41f), ["whitesmoke"] = new Color(0.96f, 0.96f, 0.96f), ["lightblue"] = new Color(0.68f, 0.85f, 0.9f), ["darkblue"] = new Color(0f, 0f, 0.55f),
            ["darkred"] = new Color(0.55f, 0f, 0f), ["darkgreen"] = new Color(0f, 0.39f, 0f), ["lightgreen"] = new Color(0.56f, 0.93f, 0.56f), ["crimson"] = new Color(0.86f, 0.08f, 0.24f),
            ["coral"] = new Color(1f, 0.5f, 0.31f), ["tomato"] = new Color(1f, 0.39f, 0.28f), ["salmon"] = new Color(0.98f, 0.5f, 0.45f), ["khaki"] = new Color(0.94f, 0.9f, 0.55f),
            ["beige"] = new Color(0.96f, 0.96f, 0.86f), ["ivory"] = new Color(1f, 1f, 0.94f), ["lavender"] = new Color(0.9f, 0.9f, 0.98f), ["skyblue"] = new Color(0.53f, 0.81f, 0.92f),
            ["steelblue"] = new Color(0.27f, 0.51f, 0.71f), ["royalblue"] = new Color(0.25f, 0.41f, 0.88f), ["dodgerblue"] = new Color(0.12f, 0.56f, 1f), ["slategray"] = new Color(0.44f, 0.5f, 0.56f),
            ["gainsboro"] = new Color(0.86f, 0.86f, 0.86f), ["aliceblue"] = new Color(0.94f, 0.97f, 1f), ["ghostwhite"] = new Color(0.97f, 0.97f, 1f),
        };

        internal static bool TryColor(string v, out Color color)
        {
            color = Color.black;
            v = v.Trim().ToLowerInvariant();
            if (v.Length == 0) return false;
            if (v == "transparent") { color = Color.clear; return true; }
            if (v[0] == '#')
            {
                string h = v.Substring(1);
                if (h.Length == 3 || h.Length == 4) { var sb = new StringBuilder(); foreach (char c in h) sb.Append(c).Append(c); h = sb.ToString(); }
                if ((h.Length == 6 || h.Length == 8) && int.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                {
                    float a = h.Length == 8 && int.TryParse(h.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int ai) ? ai / 255f : 1f;
                    color = new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
                    return true;
                }
                return false;
            }
            if (v.StartsWith("rgb"))
            {
                int open = v.IndexOf('('), close = v.IndexOf(')');
                if (open < 0 || close < open) return false;
                string[] p = v.Substring(open + 1, close - open - 1).Replace('/', ',').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 3) return false;
                float Chan(string t) => t.EndsWith("%") ? float.Parse(t.TrimEnd('%'), CultureInfo.InvariantCulture) / 100f : float.Parse(t, CultureInfo.InvariantCulture) / 255f;
                try { color = new Color(Chan(p[0]), Chan(p[1]), Chan(p[2]), p.Length > 3 ? (p[3].EndsWith("%") ? float.Parse(p[3].TrimEnd('%'), CultureInfo.InvariantCulture) / 100f : float.Parse(p[3], CultureInfo.InvariantCulture)) : 1f); return true; }
                catch (Exception) { return false; }
            }
            return Named.TryGetValue(v, out color);
        }

        // ---- the browser's own stylesheet ----

        internal const string UserAgent = @"
head,script,style,title,meta,link,template,noscript,area,base,datalist,param,source,track,svg,canvas,audio,video,dialog{display:none}
html,body,address,article,aside,blockquote,center,dd,details,div,dl,dt,fieldset,figcaption,figure,footer,form,header,hgroup,hr,main,menu,nav,ol,p,pre,section,summary,ul,h1,h2,h3,h4,h5,h6,caption,legend{display:block}
li{display:list-item}
table{display:table;border-spacing:2px}
thead,tbody,tfoot{display:table-row-group}
tr{display:table-row}
td,th{display:table-cell;padding:1px}
body{margin:8px}
p{margin:1em 0}
blockquote{margin:1em 40px}
h1{font-size:2em;margin:.67em 0;font-weight:bold}
h2{font-size:1.5em;margin:.83em 0;font-weight:bold}
h3{font-size:1.17em;margin:1em 0;font-weight:bold}
h4{margin:1.33em 0;font-weight:bold}
h5{font-size:.83em;margin:1.67em 0;font-weight:bold}
h6{font-size:.67em;margin:2.33em 0;font-weight:bold}
ul,ol,menu{margin:1em 0;padding-left:40px}
ul{list-style-type:disc}
ol{list-style-type:decimal}
ul ul,ol ul{list-style-type:circle}
ul ul,ul ol,ol ul,ol ol{margin:0}
dl{margin:1em 0}
dd{margin-left:40px}
pre{margin:1em 0;white-space:pre;font-family:monospace}
code,kbd,samp,tt{font-family:monospace}
b,strong,th{font-weight:bold}
th{text-align:center}
i,em,cite,dfn,var,address{font-style:italic}
u,ins{text-decoration:underline}
s,strike,del{text-decoration:line-through}
a{color:#0000ee;text-decoration:underline}
small{font-size:.83em}
big{font-size:1.17em}
sub,sup{font-size:.75em}
mark{background-color:#ffff00}
hr{margin:.5em 0;border:1px solid #808080;height:0}
center{text-align:center}
caption{text-align:center}
img{display:inline}
";
    }
}
