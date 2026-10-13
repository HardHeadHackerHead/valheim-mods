using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Terminal
{
    /// <summary>
    /// A web page as a tree: elements with their attributes, and text. The parser is forgiving the way browsers are (unclosed paragraphs and
    /// list items, stray end tags, bare "&amp;") and drops what the renderer can't use (scripts, comments, the contents of style tags are
    /// kept apart as stylesheet text). Capped, so a hostile or huge page can't use the game's memory.
    /// </summary>
    internal sealed class Node
    {
        public string Tag;                 // lower case; "#text" for text
        public string Text;                // for text nodes
        public Dictionary<string, string> Attr;
        public readonly List<Node> Kids = new List<Node>();
        public Node Parent;
        public Style Style;                // set by Css.Apply
        public object Box;                 // set by Layout (an element's box)

        public bool IsText => Tag == "#text";
        public string Get(string name) => Attr != null && Attr.TryGetValue(name, out string v) ? v : null;
        public bool Has(string name) => Attr != null && Attr.ContainsKey(name);

        public bool HasClass(string cls)
        {
            string value = Get("class");
            if (value == null) return false;
            foreach (string part in value.Split(' ', '\t', '\n', '\r')) if (part == cls) return true;
            return false;
        }
    }

    internal static class Dom
    {
        internal const int MaxNodes = 40000, MaxDepth = 80;

        private static readonly HashSet<string> Void = new HashSet<string> { "br", "hr", "img", "input", "meta", "link", "wbr", "source", "area", "base", "col", "embed", "param", "track" };
        private static readonly HashSet<string> Dropped = new HashSet<string> { "script", "noscript", "template", "iframe", "object", "canvas", "svg", "math", "audio", "video", "select", "textarea", "button", "input", "form" };
        private static readonly HashSet<string> BlockStarters = new HashSet<string> { "p", "div", "ul", "ol", "dl", "table", "h1", "h2", "h3", "h4", "h5", "h6", "pre", "blockquote", "hr", "section", "article", "header", "footer", "nav", "aside", "main", "figure", "fieldset", "form", "address" };

        /// <summary>The parsed page: its root, and the text of its style tags and the addresses of its linked stylesheets.</summary>
        internal sealed class Result
        {
            public Node Root;
            public readonly List<string> Styles = new List<string>();
            public readonly List<string> Sheets = new List<string>();
            public string Title = "";
        }

        internal static Result Parse(string html, Uri baseUri)
        {
            var result = new Result();
            var root = new Node { Tag = "html", Attr = new Dictionary<string, string>() };
            result.Root = root;
            var bodyNode = new Node { Tag = "body", Attr = new Dictionary<string, string>(), Parent = root };   // (always there, so a stylesheet's body rules have something to match)
            root.Kids.Add(bodyNode);
            Node current = bodyNode;
            int count = 1, depth = 2;
            int i = 0, n = html.Length;
            var text = new StringBuilder();

            void FlushText()
            {
                if (text.Length == 0) return;
                string s = WebUtility.HtmlDecode(text.ToString());
                text.Length = 0;
                if (s.Length == 0 || count > MaxNodes) return;
                current.Kids.Add(new Node { Tag = "#text", Text = s, Parent = current });
                count++;
            }

            while (i < n && count <= MaxNodes)
            {
                if (html[i] != '<') { int next = html.IndexOf('<', i); if (next < 0) next = n; text.Append(html, i, next - i); i = next; continue; }
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0) { int e = html.IndexOf("-->", i + 4, StringComparison.Ordinal); i = e < 0 ? n : e + 3; continue; }
                if (i + 1 < n && (html[i + 1] == '!' || html[i + 1] == '?')) { int e = html.IndexOf('>', i); i = e < 0 ? n : e + 1; continue; }
                int close = TagEnd(html, i);
                if (close < 0) { text.Append('<'); i++; continue; }
                string inner = html.Substring(i + 1, close - i - 1);
                i = close + 1;
                bool closing = inner.StartsWith("/");
                if (closing) inner = inner.Substring(1);
                int sp = 0;
                while (sp < inner.Length && !char.IsWhiteSpace(inner[sp]) && inner[sp] != '/') sp++;
                string name = inner.Substring(0, sp).ToLowerInvariant();
                if (name.Length == 0 || !char.IsLetter(name[0])) { text.Append('<').Append(inner).Append('>'); continue; }

                if (!closing && (name == "script" || name == "style" || Dropped.Contains(name)) && name != "input")
                {
                    int end = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                    string body = end < 0 ? html.Substring(i) : html.Substring(i, end - i);
                    if (name == "style") result.Styles.Add(body);
                    if (end < 0) { i = n; continue; }
                    int e2 = html.IndexOf('>', end);
                    i = e2 < 0 ? n : e2 + 1;
                    continue;
                }
                if (!closing && name == "title")
                {
                    int end = html.IndexOf("</title", i, StringComparison.OrdinalIgnoreCase);
                    if (end < 0) end = n;
                    result.Title = Collapse(WebUtility.HtmlDecode(html.Substring(i, end - i)));
                    i = end;
                    continue;
                }
                if (closing)
                {
                    FlushText();
                    if (name == "html" || name == "body" || name == "head") continue;
                    // close back to the nearest open element of that name (if none is open, the end tag is ignored)
                    for (Node up = current; up != null && up != root; up = up.Parent)
                        if (up.Tag == name) { current = up.Parent; depth = DepthOf(current); break; }
                    continue;
                }
                FlushText();
                if (name == "html" || name == "head" || name == "body")
                {
                    if (name == "body") MergeAttributes(bodyNode, inner);   // (a page's body colour and the like)
                    else if (name == "html") MergeAttributes(root, inner);
                    continue;
                }
                if (name == "link")
                {
                    var link = new Node { Tag = "link", Attr = Attributes(inner) };
                    string rel = (link.Get("rel") ?? "").ToLowerInvariant(), media = (link.Get("media") ?? "").ToLowerInvariant();
                    string href = link.Get("href");
                    if (rel.Contains("stylesheet") && !string.IsNullOrEmpty(href) && media != "print" && Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out Uri sheet)) result.Sheets.Add(sheet.GetLeftPart(UriPartial.Query));
                    continue;
                }
                if (name == "meta" || name == "base") continue;

                // elements that end an open one
                if (name == "li") CloseUp(ref current, root, "li", "ul", "ol");
                else if (name == "dt" || name == "dd") CloseUp(ref current, root, name == "dt" ? "dd" : "dt", "dl", "dl");
                else if (name == "td" || name == "th") { CloseUp(ref current, root, "td", "tr", "table"); CloseUp(ref current, root, "th", "tr", "table"); }
                else if (name == "tr") { CloseUp(ref current, root, "td", "tr", "table"); CloseUp(ref current, root, "th", "tr", "table"); CloseUp(ref current, root, "tr", "table", "table"); }
                else if (name == "option") CloseUp(ref current, root, "option", "select", "select");
                if (BlockStarters.Contains(name) && current.Tag == "p") current = current.Parent;   // (a block ends an open paragraph)

                var node = new Node { Tag = name, Attr = Attributes(inner), Parent = current };
                current.Kids.Add(node);
                count++;
                bool selfClosed = inner.EndsWith("/") || Void.Contains(name);
                if (!selfClosed && depth < MaxDepth) { current = node; depth++; }
            }
            FlushText();
            return result;
        }

        private static int DepthOf(Node n) { int d = 1; for (; n != null; n = n.Parent) d++; return d; }

        /// <summary>An element of this name inside the current one (but not past a boundary) is closed before a new sibling opens.</summary>
        private static void CloseUp(ref Node current, Node root, string name, string boundary1, string boundary2)
        {
            for (Node up = current; up != null && up != root; up = up.Parent)
            {
                if (up.Tag == boundary1 || up.Tag == boundary2) return;
                if (up.Tag == name) { current = up.Parent; return; }
            }
        }

        private static void MergeAttributes(Node target, string tag)
        {
            Dictionary<string, string> a = Attributes(tag);
            foreach (KeyValuePair<string, string> kv in a) target.Attr[kv.Key] = kv.Value;
        }

        private static int TagEnd(string html, int start)
        {
            char quote = '\0';
            for (int j = start + 1; j < html.Length && j < start + 6000; j++)
            {
                char c = html[j];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return j;
            }
            return -1;
        }

        /// <summary>The attributes inside a tag ("a href=x class='y z'"): names lower case, values decoded.</summary>
        internal static Dictionary<string, string> Attributes(string tag)
        {
            var a = new Dictionary<string, string>();
            int i = 0, n = tag.Length;
            while (i < n && !char.IsWhiteSpace(tag[i])) i++;   // (the tag name)
            while (i < n)
            {
                while (i < n && (char.IsWhiteSpace(tag[i]) || tag[i] == '/')) i++;
                int start = i;
                while (i < n && !char.IsWhiteSpace(tag[i]) && tag[i] != '=' && tag[i] != '/') i++;
                if (i == start) { i++; continue; }
                string name = tag.Substring(start, i - start).ToLowerInvariant();
                while (i < n && char.IsWhiteSpace(tag[i])) i++;
                string value = "";
                if (i < n && tag[i] == '=')
                {
                    i++;
                    while (i < n && char.IsWhiteSpace(tag[i])) i++;
                    if (i < n && (tag[i] == '"' || tag[i] == '\''))
                    {
                        char q = tag[i++];
                        int end = tag.IndexOf(q, i);
                        if (end < 0) end = n;
                        value = tag.Substring(i, end - i);
                        i = Math.Min(n, end + 1);
                    }
                    else
                    {
                        int s2 = i;
                        while (i < n && !char.IsWhiteSpace(tag[i])) i++;
                        value = tag.Substring(s2, i - s2);
                    }
                }
                if (!a.ContainsKey(name)) a[name] = WebUtility.HtmlDecode(value);
            }
            return a;
        }

        internal static string Collapse(string s)
        {
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
