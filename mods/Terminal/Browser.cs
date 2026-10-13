using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using static DHack.Shared.Translations;

namespace Terminal
{
    /// <summary>
    /// A web browser for the terminal. Not a full engine (Valheim can't carry one): it fetches a page over https, reads its HTML and CSS,
    /// lays it out the way a browser does for a window about 900 pixels wide (Dom.cs, Css.cs, Layout.cs: blocks, text, floats, lists,
    /// tables, colours, pictures) and draws it into the screen (PageView.cs). There are no scripts, so what a page builds with JavaScript
    /// isn't there, and no forms, cookies or videos. The window has an address bar (a click asks for text with the game's own input box: a
    /// web address, or words to search for), Back, Home, Reload, a scroll bar the mouse wheel moves, and links a click follows.
    ///
    /// What is shared: the address and the scroll position are in the terminal's saved data, so everyone near sees the same page. The
    /// sitter's game fetches what they ask for; another player's game fetches the same address only when its host is on that player's own
    /// list (BrowserViewerHosts, local) and shows a note otherwise, so nobody's game is made to contact a site by somebody else. What is
    /// refused (Net.Check): anything but https on the normal port, local and private addresses (a player's own network), pages over 1.5 MB
    /// or slower than 12 seconds; every redirect is checked again. Pictures and stylesheets follow the same rules.
    /// </summary>
    internal sealed class BrowserApp : App
    {
        internal const string AppId = "browser";
        internal override string Id => AppId;
        internal override string Title => "Browser";
        internal override string Glyph => "@";
        internal override Color Tint => new Color(0.82f, 0.45f, 0.2f);
        internal override float LoadSeconds(TerminalPiece terminal) => 1.5f;
        internal override bool WantsCursor => true;
        internal override bool HasWindow => true;

        // layout, in the desktop's 1280x720 space
        private static readonly Rect Back = new Rect(12f, 10f, 96f, 44f), Home = new Rect(116f, 10f, 96f, 44f), Reload = new Rect(220f, 10f, 116f, 44f),
            Address = new Rect(348f, 10f, 820f, 44f), Close = new Rect(1186f, 10f, 82f, 44f),
            Prev = new Rect(1040f, 640f, 110f, 32f), Next = new Rect(1158f, 640f, 110f, 32f), Content = new Rect(0f, 64f, 1280f, 572f);

        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>();
        private static TextMeshProUGUI _address, _status;
        private static PageView _view;
        private static Doc _shown;

        private sealed class Session
        {
            public string Url = "";        // what is shown (or being loaded)
            public Doc Doc;
            public bool Loading;
            public int Generation;         // a page that arrives for an address that is no longer the current one is dropped
            public Dom.Result Parsed;
            public Uri Base;
            public readonly List<string> Sheets = new List<string>();
            public float LastVersionChange, NextPictureCheck;
            public int Version = -1, Relayouts;
            public readonly Stack<string> History = new Stack<string>();   // (the sitter's game only)
        }

        private static Session For(TerminalPiece t)
        {
            if (!Sessions.TryGetValue(t.Id, out Session s)) Sessions[t.Id] = s = new Session();
            return s;
        }

        /// <summary>What layout produced for a terminal's page (the "terminal page" test command).</summary>
        internal static string Describe(TerminalPiece t, int count)
        {
            Session s = For(t);
            if (s.Doc == null) return "no page";
            var sb = new System.Text.StringBuilder($"{s.Doc.Items.Length} things, {s.Doc.Links.Length} links, height {s.Doc.Height:0}, page colour {s.Doc.Page}, sheets {s.Sheets.Count}, styles {s.Parsed?.Styles.Count}" + Environment.NewLine);
            int shown = 0;
            void Tree(Node n, int depth)
            {
                if (shown++ > 40 || depth > 8) return;
                sb.AppendLine(new string(' ', depth * 2) + (n.IsText ? "\"" + (n.Text.Length > 30 ? n.Text.Substring(0, 30) : n.Text).Replace('\n', ' ') + "\"" : "<" + n.Tag + "> display=" + n.Style?.Display + " w=" + n.Style?.Width.V));
                foreach (Node k in n.Kids) Tree(k, depth + 1);
            }
            if (s.Parsed != null) Tree(s.Parsed.Root, 0);
            for (int i = 0; i < Math.Min(count, s.Doc.Items.Length); i++)
            {
                Layouter.Item it = s.Doc.Items[i];
                sb.AppendLine($"{i}: kind {it.Kind} at {it.X:0},{it.Y:0} size {it.W:0}x{it.H:0} {it.C} {(it.Text != null ? "'" + (it.Text.Length > 40 ? it.Text.Substring(0, 40) : it.Text) + "'" : "")}");
            }
            return sb.ToString();
        }

        internal static void ClearSessions() { Sessions.Clear(); _shown = null; _view = null; ImageStore.Clear(); }

        // ---- the window ----

        internal override void BuildWindow(Transform root)
        {
            Desktop.Box("Back", root, 0f, 0f, 1280f, 720f, new Color(0.09f, 0.1f, 0.12f));
            _view = new PageView(root, Content.x, Content.y, Content.width, Content.height);
            Desktop.Box("Toolbar", root, 0f, 0f, 1280f, 64f, new Color(0.16f, 0.18f, 0.22f));
            Button(root, Back, "Back"); Button(root, Home, "Home"); Button(root, Reload, "Reload"); Button(root, Close, "X");
            Desktop.Box("AddressBox", root, Address.x, Address.y, Address.width, Address.height, new Color(0.06f, 0.07f, 0.09f));
            _address = Desktop.Label("Address", root, "", 22, new Color(0.85f, 0.87f, 0.9f), TextAlignmentOptions.Left, Address.x + 12f, Address.y, Address.width - 24f, Address.height);
            _address.overflowMode = TextOverflowModes.Ellipsis;
            Desktop.Box("StatusBar", root, 0f, 636f, 1280f, 40f, new Color(0.16f, 0.18f, 0.22f));
            _status = Desktop.Label("Status", root, "", 20, new Color(0.7f, 0.73f, 0.78f), TextAlignmentOptions.Left, 16f, 640f, 1000f, 32f);
            Button(root, Prev, "Prev"); Button(root, Next, "Next");
        }

        private static void Button(Transform root, Rect r, string text)
        {
            Desktop.Box("Button " + text, root, r.x, r.y, r.width, r.height, new Color(0.26f, 0.34f, 0.42f));
            Desktop.Label("Text " + text, root, T("terminal_browser_" + text.ToLowerInvariant()), 22, new Color(0.9f, 0.92f, 0.95f), TextAlignmentOptions.Center, r.x, r.y, r.width, r.height);
        }

        internal override void UpdateWindow(TerminalPiece t, bool driver)
        {
            Session s = For(t);
            string url = t.BrowserUrl;
            if (url.Length == 0) url = Net.HomeUrl;
            if (s.Url != url) Load(t, s, url, driver);
            _address.text = url == Net.HomeUrl ? "" : Html.Escape(url);
            if (s.Doc != null && !ReferenceEquals(_shown, s.Doc)) { _shown = s.Doc; _view.Show(s.Doc); }
            if (s.Doc == null && _shown != null) { _shown = null; _view.Show(null); }
            float scroll = Mathf.Clamp(t.BrowserPage, 0, Mathf.CeilToInt(_view.MaxScroll));
            Pictures(s, driver);
            _view.Update(scroll);
            string title = s.Loading ? T("terminal_browser_loading") : s.Doc != null ? Html.Escape(s.Doc.Title) : "";
            float max = _view.MaxScroll;
            _status.text = title + (s.Doc != null && max > 1f ? "   -   " + Mathf.RoundToInt(100f * scroll / max) + "%" : "");
        }

        /// <summary>Starts the pictures of the page (a few at a time) and lays the page out again once their sizes are known.</summary>
        private static void Pictures(Session s, bool driver)
        {
            if (s.Doc == null) return;
            if (Time.unscaledTime >= s.NextPictureCheck)
            {
                s.NextPictureCheck = Time.unscaledTime + 0.4f;
                if (Plugin.BrowserImages.Value)
                    foreach (string url in s.Doc.PictureUrls)
                    {
                        if (ImageStore.Known(url)) continue;
                        if (!driver && !Net.ViewerMayLoad(new Uri(url).Host)) continue;
                        ImageStore.Request(url);
                    }
            }
            if (ImageStore.Version != s.Version)
            {
                s.Version = ImageStore.Version;
                s.LastVersionChange = Time.unscaledTime;
            }
            else if (s.LastVersionChange > 0f && Time.unscaledTime - s.LastVersionChange > 0.6f && s.Relayouts < 6 && s.Parsed != null)
            {
                s.LastVersionChange = 0f;
                s.Relayouts++;
                Build(s);   // (pictures with no stated size have one now)
            }
        }

        // ---- the sitter's mouse ----

        internal override void Click(TerminalPiece t, Vector2 at, Camera camera, RenderTexture target)
        {
            var p = new Vector2(at.x * 1280f, at.y * 720f);
            Session s = For(t);
            if (Close.Contains(p)) { t.CloseApp(Id); return; }
            if (Home.Contains(p)) { Go(t, s, Net.HomeUrl); return; }
            if (Reload.Contains(p)) { s.Url = ""; return; }   // (the next update loads it again)
            if (Back.Contains(p)) { if (s.History.Count > 0) { t.SetBrowser(s.History.Pop(), 0); } return; }
            if (Address.Contains(p)) { if (TextInput.instance != null) TextInput.instance.RequestText(new AddressInput(t, s), "$terminal_browser_address", 300); return; }
            float page = Mathf.Max(100f, Content.height / PageView.Zoom * 0.9f);
            if (Next.Contains(p)) { t.SetBrowser(t.BrowserUrl, Mathf.Max(0, t.BrowserPage + Mathf.RoundToInt(page))); return; }
            if (Prev.Contains(p)) { t.SetBrowser(t.BrowserUrl, Mathf.Max(0, t.BrowserPage - Mathf.RoundToInt(page))); return; }
            if (s.Doc != null && Content.Contains(p))
            {
                float docX = (p.x - Content.x) / PageView.Zoom, docY = (p.y - Content.y) / PageView.Zoom + t.BrowserPage;
                string link = s.Doc.LinkAt(docX, docY);
                if (link != null) Go(t, s, link);
            }
        }

        internal override void Scroll(TerminalPiece t, float delta)
        {
            if (Mathf.Abs(delta) < 0.001f) return;
            t.SetBrowser(t.BrowserUrl, Mathf.Max(0, t.BrowserPage - Mathf.RoundToInt(delta * 1500f)));
        }

        /// <summary>The sitter goes to an address (a link, the address bar, Home).</summary>
        private static void Go(TerminalPiece t, Session s, string url)
        {
            if (s.Url.Length > 0 && s.Url != Net.HomeUrl && s.History.Count < 40) s.History.Push(s.Url);
            t.SetBrowser(url, 0);
        }

        private sealed class AddressInput : TextReceiver
        {
            private readonly TerminalPiece _terminal; private readonly Session _session;
            public AddressInput(TerminalPiece t, Session s) { _terminal = t; _session = s; }
            public string GetText() => _session.Url == Net.HomeUrl ? "" : _session.Url;
            public void SetText(string text)
            {
                string url = Net.Normalize(text);
                if (url != null && _terminal != null) Go(_terminal, _session, url);
            }
        }

        // ---- loading a page ----

        private void Load(TerminalPiece t, Session s, string url, bool driver)
        {
            s.Url = url;
            s.Generation++;
            s.Loading = false;
            s.Relayouts = 0;
            const string own = "https://quads.terminal.invalid/";
            if (url == Net.HomeUrl) { Show(s, Pages.Home(), own); return; }
            if (!Plugin.BrowserEnabled.Value) { Show(s, Pages.Message(T("terminal_browser_off"), ""), own); return; }
            string problem = Net.Check(url, out Uri uri);
            if (problem != null) { Show(s, Pages.Message(problem, url), own); return; }
            if (!driver && !Net.ViewerMayLoad(uri.Host))   // (somebody else's page: this game doesn't contact the site)
            {
                Show(s, Pages.Message(T("terminal_browser_notloaded", uri.Host), url), own);
                return;
            }
            s.Loading = true;
            Plugin.Instance.StartCoroutine(Pipeline(s, url, driver, s.Generation));
        }

        private static IEnumerator Pipeline(Session s, string url, bool driver, int generation)
        {
            const string own = "https://quads.terminal.invalid/";
            Net.Result page = null;
            yield return Net.FetchText(url, 1500000, "text/html,application/xhtml+xml;q=0.9,text/plain;q=0.8", r => page = r);
            if (generation != s.Generation) yield break;
            if (page.Error != null) { s.Loading = false; Show(s, Pages.Message(page.Error, url), own); yield break; }
            string html = page.Text;
            bool plain = page.Type.StartsWith("text/") && !page.Type.Contains("html");
            if (plain) html = "<pre>" + WebUtility.HtmlEncode(html) + "</pre>";
            Uri final = new Uri(page.FinalUrl);
            Dom.Result parsed = Dom.Parse(html, final);
            s.Sheets.Clear();
            int taken = 0;
            foreach (string sheet in parsed.Sheets)
            {
                if (taken >= 4) break;
                if (Net.Check(sheet, out Uri su) != null) continue;
                if (!driver && !Net.ViewerMayLoad(su.Host)) continue;
                taken++;
                Net.Result css = null;
                yield return Net.FetchText(sheet, 400000, "text/css,*/*;q=0.1", r => css = r);
                if (generation != s.Generation) yield break;
                if (css.Error == null) s.Sheets.Add(css.Text);
            }
            if (generation != s.Generation) yield break;
            s.Parsed = parsed; s.Base = final;
            s.Loading = false;
            Build(s);
        }

        /// <summary>A page that is made of our own HTML (the start page, a message).</summary>
        private static void Show(Session s, string html, string baseUrl)
        {
            s.Parsed = Dom.Parse(html, new Uri(baseUrl));
            s.Base = new Uri(baseUrl);
            s.Sheets.Clear();
            Build(s);
        }

        /// <summary>Styles, layout, and the picture list: from the parsed page to something to draw.</summary>
        private static void Build(Session s)
        {
            float started = Time.realtimeSinceStartup;
            try
            {
                var css = new Css();
                css.AddSheet(Css.UserAgent, true);
                foreach (string sheet in s.Sheets) css.AddSheet(sheet);
                foreach (string style in s.Parsed.Styles) css.AddSheet(style);
                css.Apply(s.Parsed.Root, null);
                var layout = new Layouter(s.Base, ImageStore.Size);
                layout.Run(s.Parsed.Root, Css.ViewportWidth);
                s.Doc = Doc.From(layout, s.Parsed.Title.Length > 0 ? s.Parsed.Title : s.Base.Host);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("The browser could not show a page: " + e);
                var own = new Uri("https://quads.terminal.invalid/");
                s.Parsed = Dom.Parse(Pages.Message(T("terminal_browser_broken"), s.Url), own);
                s.Sheets.Clear();
                var css = new Css(); css.AddSheet(Css.UserAgent, true); css.Apply(s.Parsed.Root, null);
                var layout = new Layouter(own, ImageStore.Size);
                layout.Run(s.Parsed.Root, Css.ViewportWidth);
                s.Doc = Doc.From(layout, "");
            }
            float took = Time.realtimeSinceStartup - started;
            if (took > 0.25f) Plugin.Log.LogInfo($"Browser: laid out {s.Doc.Items.Length} things in {took * 1000f:0} ms");
        }
    }

    /// <summary>The browser's own pages, as HTML (so they go through the same layout as any other).</summary>
    internal static class Pages
    {
        private const string Style = "<style>body{margin:28px;background:#16181d;color:#d8dbe2}h1{color:#ffffff}a{color:#6db3ff;text-decoration:none}li{margin:6px 0}.small{color:#8a8f99;font-size:14px}</style>";

        internal static string Home()
        {
            var sb = new System.Text.StringBuilder("<html><head>" + Style + "</head><body>");
            sb.Append("<h1>").Append(WebUtility.HtmlEncode(T("terminal_browser_title"))).Append("</h1>");
            sb.Append("<p>").Append(WebUtility.HtmlEncode(T("terminal_browser_home_hint"))).Append("</p><ul>");
            string[][] sites =
            {
                new[] { "https://en.wikipedia.org/wiki/Valheim", "Valheim - Wikipedia" },
                new[] { "https://en.wikipedia.org/wiki/Special:Random", "A random Wikipedia article" },
                new[] { "https://html.duckduckgo.com/html/?q=valheim", "Search the web (DuckDuckGo): valheim" },
                new[] { "https://example.com", "example.com" },
            };
            foreach (string[] site in sites) sb.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(site[0])).Append("\">").Append(WebUtility.HtmlEncode(site[1])).Append("</a></li>");
            sb.Append("</ul><p class=\"small\">").Append(WebUtility.HtmlEncode(T("terminal_browser_limits"))).Append("</p></body></html>");
            return sb.ToString();
        }

        internal static string Message(string text, string url) =>
            "<html><head>" + Style + "</head><body><h1>" + WebUtility.HtmlEncode(text) + "</h1><p class=\"small\">" + WebUtility.HtmlEncode(url) + "</p></body></html>";
    }

    /// <summary>The browser's rules: which addresses it may open, how it fetches, and who may fetch what.</summary>
    internal static class Net
    {
        internal const string HomeUrl = "about:home";
        internal const string UserAgent = "QuadsTerminal/0.1 (a Valheim mod; simple browser)";
        private const int MaxRedirects = 5;

        internal sealed class Result { public string Text = "", Type = "", FinalUrl = "", Error; }

        /// <summary>What the address bar was given, as an address: words to search for become a search, a bare site name gets https://.</summary>
        internal static string Normalize(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) text = "https://" + text.Substring(7);
            if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return text;
            if (!text.Contains(" ") && text.Contains(".") && !text.StartsWith(".") && !text.EndsWith(".")) return "https://" + text;
            return "https://html.duckduckgo.com/html/?q=" + UnityWebRequest.EscapeURL(text);
        }

        /// <summary>Null when the address may be opened, else the reason (as words for the player).</summary>
        internal static string Check(string url, out Uri uri)
        {
            uri = null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return T("terminal_browser_https");
            if (uri.Port != 443) return T("terminal_browser_port");
            string host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host.EndsWith(".local") || host.EndsWith(".localhost") || host.EndsWith(".internal") || host.EndsWith(".invalid") || host.EndsWith(".lan") || host.EndsWith(".home") || !host.Contains(".") && !host.Contains(":"))
                return T("terminal_browser_private");
            if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress ip) && IsPrivate(ip)) return T("terminal_browser_private");
            return null;
        }

        private static bool IsPrivate(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] b = ip.GetAddressBytes();
            if (b.Length == 4)
                return b[0] == 0 || b[0] == 10 || b[0] == 127 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC || ip.IsIPv4MappedToIPv6;
        }

        /// <summary>Another player's page is only fetched here when its host is on this player's own list (or the list is "*").</summary>
        internal static bool ViewerMayLoad(string host)
        {
            host = host.ToLowerInvariant();
            foreach (string entry in Plugin.BrowserViewerHosts.Value.Split(','))
            {
                string e = entry.Trim().ToLowerInvariant();
                if (e == "*" || (e.Length > 0 && (host == e || host.EndsWith("." + e)))) return true;
            }
            return false;
        }

        /// <summary>Fetch text over https, following redirects by hand (each checked again), up to a size and 12 seconds.</summary>
        internal static IEnumerator FetchText(string url, int maxBytes, string accept, Action<Result> done)
        {
            string current = url;
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                string problem = Check(current, out Uri uri);
                if (problem != null) { done(Fail(problem, current)); yield break; }
                using (UnityWebRequest request = UnityWebRequest.Get(current))
                {
                    request.redirectLimit = 0;
                    request.timeout = 12;
                    request.SetRequestHeader("User-Agent", UserAgent);
                    request.SetRequestHeader("Accept", accept);
                    request.SetRequestHeader("Accept-Language", "en");
                    UnityWebRequestAsyncOperation op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (request.downloadedBytes > (ulong)maxBytes) { request.Abort(); done(Fail(T("terminal_browser_toobig"), current)); yield break; }
                        yield return null;
                    }
                    long code = request.responseCode;
                    if (code >= 300 && code < 400)
                    {
                        string location = request.GetResponseHeader("Location");
                        if (string.IsNullOrEmpty(location) || !Uri.TryCreate(uri, location, out Uri next)) { done(Fail(T("terminal_browser_failed", code), current)); yield break; }
                        current = next.GetLeftPart(UriPartial.Query);
                        continue;
                    }
                    if (request.result != UnityWebRequest.Result.Success || code >= 400)
                    {
                        done(Fail(code > 0 ? T("terminal_browser_failed", code) : T("terminal_browser_noconnection"), current));
                        yield break;
                    }
                    string type = (request.GetResponseHeader("Content-Type") ?? "").ToLowerInvariant();
                    if (type.Length > 0 && !type.StartsWith("text/") && !type.Contains("xml") && !type.Contains("json") && !type.Contains("css")) { done(Fail(T("terminal_browser_type", type), current)); yield break; }
                    done(new Result { Text = request.downloadHandler.text ?? "", Type = type.Length == 0 ? "text/html" : type, FinalUrl = current });
                    yield break;
                }
            }
            done(Fail(T("terminal_browser_redirects"), current));
        }

        private static Result Fail(string text, string url) => new Result { Error = text, FinalUrl = url };
    }
}
