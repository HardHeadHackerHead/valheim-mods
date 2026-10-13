using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Terminal
{
    /// <summary>
    /// Turns a web page into what the terminal's browser can show: readable text in TextMeshPro's rich text (headings bigger, bold and
    /// italic, bullets, links underlined and numbered so a click finds them). It is not a browser engine: no scripts, no styles, no
    /// images (an image's description shows in brackets), tables flattened into lines. That is enough for text pages like Wikipedia
    /// articles, search results and documentation. Nothing from the page is ever shown as a tag: every "&lt;" in its text is swapped for a
    /// look-alike, so a page can't inject rich text of its own.
    /// </summary>
    internal static class Html
    {
        internal sealed class Page
        {
            public string Title = "";
            public string Rich = "";
            public readonly List<string> Links = new List<string>();
        }

        private static readonly HashSet<string> Skipped = new HashSet<string> { "script", "style", "noscript", "svg", "template", "iframe", "object", "canvas", "select", "button", "nav", "aside", "footer" };
        private static readonly HashSet<string> BlockTags = new HashSet<string> { "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "table", "tr", "blockquote", "form", "ul", "ol", "dl", "figure", "figcaption", "details", "summary", "fieldset", "center" };
        private const string LinkColor = "#6db3ff";

        internal static Page Convert(string html, Uri baseUri, int maxChars)
        {
            var page = new Page();
            var o = new Writer(maxChars);
            int i = 0, n = html.Length, pre = 0, bold = 0, italic = 0;
            bool inLink = false;
            while (i < n && !o.Full)
            {
                if (html[i] != '<')
                {
                    int next = html.IndexOf('<', i);
                    if (next < 0) next = n;
                    string text = WebUtility.HtmlDecode(html.Substring(i, next - i));
                    o.Text(text, pre > 0);
                    i = next;
                    continue;
                }
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    int end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = end < 0 ? n : end + 3;
                    continue;
                }
                if (i + 1 < n && (html[i + 1] == '!' || html[i + 1] == '?'))
                {
                    int end = html.IndexOf('>', i);
                    i = end < 0 ? n : end + 1;
                    continue;
                }
                int close = TagEnd(html, i);
                if (close < 0) { o.Text("‹", false); i++; continue; }   // (a stray "<")
                string inner = html.Substring(i + 1, close - i - 1);
                i = close + 1;
                bool closing = inner.StartsWith("/");
                if (closing) inner = inner.Substring(1);
                int space = 0;
                while (space < inner.Length && !char.IsWhiteSpace(inner[space]) && inner[space] != '/') space++;
                string name = inner.Substring(0, space).ToLowerInvariant();
                if (name.Length == 0) continue;

                if (!closing && Skipped.Contains(name))
                {
                    int end = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                    if (end < 0) { i = n; continue; }
                    int e2 = html.IndexOf('>', end);
                    i = e2 < 0 ? n : e2 + 1;
                    continue;
                }
                if (name == "title")
                {
                    if (!closing)
                    {
                        int end = html.IndexOf("</title", i, StringComparison.OrdinalIgnoreCase);
                        if (end < 0) end = n;
                        page.Title = Clean(WebUtility.HtmlDecode(html.Substring(i, end - i)));
                        i = end;
                    }
                    continue;
                }
                if (name == "head" && !closing)
                {
                    // (the title is read above; the rest of the head is not shown)
                    int end = html.IndexOf("</head", i, StringComparison.OrdinalIgnoreCase);
                    int tt = html.IndexOf("<title", i, StringComparison.OrdinalIgnoreCase);
                    if (tt >= 0 && (end < 0 || tt < end))
                    {
                        int ts = html.IndexOf('>', tt);
                        int te = ts < 0 ? -1 : html.IndexOf("</title", ts, StringComparison.OrdinalIgnoreCase);
                        if (ts >= 0 && te >= 0) page.Title = Clean(WebUtility.HtmlDecode(html.Substring(ts + 1, te - ts - 1)));
                    }
                    if (end >= 0) { int e2 = html.IndexOf('>', end); i = e2 < 0 ? n : e2 + 1; }
                    continue;
                }

                switch (name)
                {
                    case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                        if (!closing) { o.Break(2); o.Raw(name == "h1" ? "<size=150%><b>" : name == "h2" ? "<size=130%><b>" : "<size=115%><b>"); }
                        else { o.Raw("</b></size>"); o.Break(2); }
                        break;
                    case "b": case "strong":
                        if (!closing) { bold++; o.Raw("<b>"); } else if (bold > 0) { bold--; o.Raw("</b>"); }
                        break;
                    case "i": case "em": case "cite":
                        if (!closing) { italic++; o.Raw("<i>"); } else if (italic > 0) { italic--; o.Raw("</i>"); }
                        break;
                    case "a":
                        if (!closing)
                        {
                            string target = Resolve(baseUri, Attribute(inner, "href"));
                            if (target != null && !inLink)
                            {
                                inLink = true;
                                o.Raw("<link=\"" + page.Links.Count + "\"><color=" + LinkColor + ">");
                                page.Links.Add(target);
                            }
                        }
                        else if (inLink) { inLink = false; o.Raw("</color></link>"); }
                        break;
                    case "br":
                        o.Raw("\n"); o.AtLineStart = true;
                        break;
                    case "hr":
                        o.Break(1); o.Raw("────────────────────"); o.Break(1);
                        break;
                    case "li": case "dt": case "dd":
                        if (!closing) { o.Break(1); o.Raw(name == "dd" ? "    " : " • "); o.AtLineStart = false; }
                        break;
                    case "td": case "th":
                        if (!closing && !o.AtLineStart) o.Raw("  |  ");
                        break;
                    case "pre":
                        if (!closing) { pre++; o.Break(2); } else { if (pre > 0) pre--; o.Break(2); }
                        break;
                    case "img":
                        {
                            string alt = Clean(WebUtility.HtmlDecode(Attribute(inner, "alt") ?? ""));
                            if (alt.Length > 0) o.Text("[" + alt + "]", false);
                        }
                        break;
                    default:
                        if (BlockTags.Contains(name)) o.Break(name == "p" || name == "table" || name == "ul" || name == "ol" || name == "blockquote" ? 2 : 1);
                        break;
                }
            }
            while (bold-- > 0) o.Raw("</b>");
            while (italic-- > 0) o.Raw("</i>");
            if (inLink) o.Raw("</color></link>");
            if (o.Full) o.Raw("\n\n<i>(the rest of this page is not shown)</i>");
            page.Rich = o.ToString().Trim('\n', ' ');
            return page;
        }

        /// <summary>Plain text: shown as it is (nothing in it is a tag).</summary>
        internal static Page FromText(string text, int maxChars)
        {
            var page = new Page();
            var o = new Writer(maxChars);
            o.Text(text, true);
            page.Rich = o.ToString();
            return page;
        }

        /// <summary>A page of our own words and links (the start page, an error).</summary>
        internal static string Escape(string text) => text.Replace('<', '‹').Replace('>', '›');

        // ---- helpers ----

        private static int TagEnd(string html, int start)
        {
            char quote = '\0';
            for (int j = start + 1; j < html.Length && j < start + 4000; j++)
            {
                char c = html[j];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '"' || c == '\'') quote = c;
                else if (c == '>') return j;
            }
            return -1;
        }

        private static string Attribute(string tag, string name)
        {
            int at = 0;
            while (true)
            {
                at = tag.IndexOf(name, at, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return null;
                bool startOk = at == 0 || char.IsWhiteSpace(tag[at - 1]);
                int p = at + name.Length;
                while (p < tag.Length && char.IsWhiteSpace(tag[p])) p++;
                if (!startOk || p >= tag.Length || tag[p] != '=') { at += name.Length; continue; }
                p++;
                while (p < tag.Length && char.IsWhiteSpace(tag[p])) p++;
                if (p >= tag.Length) return "";
                if (tag[p] == '"' || tag[p] == '\'')
                {
                    int end = tag.IndexOf(tag[p], p + 1);
                    return end < 0 ? tag.Substring(p + 1) : tag.Substring(p + 1, end - p - 1);
                }
                int stop = p;
                while (stop < tag.Length && !char.IsWhiteSpace(tag[stop])) stop++;
                return tag.Substring(p, stop - p);
            }
        }

        /// <summary>A link's address, whole (null for anything but a web address: mail links, scripts, page-internal anchors).</summary>
        private static string Resolve(Uri baseUri, string href)
        {
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#")) return null;
            href = WebUtility.HtmlDecode(href.Trim());
            if (!Uri.TryCreate(baseUri, href, out Uri uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
            return uri.GetLeftPart(UriPartial.Query);
        }

        private static string Clean(string text)
        {
            var sb = new StringBuilder();
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c == '<' ? '‹' : c == '>' ? '›' : c);
            }
            return sb.ToString();
        }

        /// <summary>The text so far: tracks the line start, caps the length, and keeps blank lines to two at most.</summary>
        private sealed class Writer
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly int _max;
            private int _newlines = 2;   // (the start of the page counts as after a blank line)
            private bool _space;
            public bool AtLineStart { get => _newlines > 0; set => _newlines = value ? Math.Max(_newlines, 1) : 0; }
            public bool Full => _sb.Length >= _max;
            public Writer(int max) { _max = max; }
            public override string ToString() => _sb.ToString();

            public void Raw(string text)
            {
                if (_space && !text.StartsWith("<") && !text.StartsWith("\n")) { _sb.Append(' '); _space = false; }
                _sb.Append(text);
                if (text.EndsWith("\n")) _newlines = Math.Max(_newlines, 1);
            }

            public void Break(int lines)
            {
                _space = false;
                while (_newlines < lines) { _sb.Append('\n'); _newlines++; }
            }

            public void Text(string text, bool keepSpace)
            {
                if (text.Length == 0) return;
                text = text.Replace('<', '‹').Replace('>', '›');
                if (keepSpace)
                {
                    _sb.Append(text.Replace("\r", ""));
                    _newlines = text.EndsWith("\n") ? Math.Max(_newlines, 1) : 0;
                    return;
                }
                foreach (char c in text)
                {
                    if (char.IsWhiteSpace(c)) { _space = _newlines == 0; continue; }
                    if (_space) { _sb.Append(' '); _space = false; }
                    _sb.Append(c);
                    _newlines = 0;
                }
            }
        }
    }
}
