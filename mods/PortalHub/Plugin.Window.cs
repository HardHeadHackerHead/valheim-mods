using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PortalHub
{
    /// <summary>
    /// The portal menu, in the game's own look (GameUi.cs): pick where this portal goes, rename it, star portals, look at them on the
    /// map. The name and search boxes are made once when it opens (so typing is never interrupted); the rest is built again whenever what
    /// it shows changes, and the list makes only the rows in view (a world can have hundreds of portals).
    /// </summary>
    public partial class Plugin
    {
        private const float WinW = 760f, WinH = 740f, Pad = 30f, ListTop = 306f, RowH = 44f;

        /// <summary>True while the menu is open (GameUi's input patches use it to stop the game reacting to clicks and typing).</summary>
        internal static bool WindowOpen => Instance != null && Instance._window != null;
        internal static float ClosedAt = -1f;   // (the E that closes it mustn't open it again)

        private RectTransform _window, _chrome, _list;
        private TMP_InputField _nameField, _searchField;
        private bool _open, _scrollTop;
        private string _chromeShown = "", _listKey, _listShown = "";
        private readonly Dictionary<int, GameObject> _rowObjects = new Dictionary<int, GameObject>();
        private Action _pending;   // a click's action, run in UpdateWindow (never while the window is being drawn)

        private TeleportWorld _portal;
        private string _portalId = "";
        private string _search = "";
        private string _nameEdit = "";
        private bool _sortByName;
        private float _openedAt, _openedAtUnscaled;
        private string _expectDest; private float _expectBy;

        private class Row { public PortalInfo Info; public float Distance; public bool Favorite; public string Where; }
        private List<Row> _rows = new List<Row>();
        private string _rowsKey;

        // ---- opening and closing ----

        /// <returns>false if the game's interface to build it from wasn't found (the game's own name box opens instead)</returns>
        internal bool OpenWindow(TeleportWorld portal)
        {
            ZNetView view = portal.GetComponent<ZNetView>();
            if (view == null || view.GetZDO() == null) return true;
            if (!GameUi.Ready() || GameUi.Canvas == null) return false;
            CloseWindow();
            GameUi.IsOpen = () => WindowOpen;   // turns on the input patches in GameUi.cs
            GameUi_EscapeCloses.Close = CloseFromEscape;

            _portal = portal;
            ZDO zdo = view.GetZDO();
            _portalId = PortalId(zdo);
            _search = "";
            _rowsKey = null;
            _pending = null;
            _expectDest = null;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.time;
            _openedAtUnscaled = Time.unscaledTime;
            _nameEdit = zdo.GetString(ZDOVars.s_tag) ?? "";
            _open = true;
            BuildWindow();
            RequestList();
            _nextAsk = Time.time + 5f;
            return true;
        }

        /// <summary>Closes the menu as Escape does. Claude Tools' "shoot close" calls it by this name (PortalHub.Plugin:CloseFromEscape).</summary>
        public static void CloseFromEscape() => Instance?.CloseWindow();

        private void CloseWindow()
        {
            if (_window != null) Destroy(_window.gameObject);
            if (_open) { ClosedAt = Time.unscaledTime; UITooltip.HideTooltip(); }
            _window = _chrome = _list = null;
            _nameField = _searchField = null;
            _rowObjects.Clear();
            _chromeShown = _listShown = "";
            _listKey = null;
            _open = false;
            _portal = null;
            _pending = null;
        }

        private void UpdateWindow()
        {
            if (!_open) return;
            if (_window == null) { CloseWindow(); return; }   // (the game rebuilt its interface: a world change)
            Player player = Player.m_localPlayer;
            bool useAgain = !GameUi.Typing && Time.unscaledTime > _openedAtUnscaled + 0.2f && ZInput.GetButtonDown("Use");   // E closes it, as it does a chest
            if (player == null || player.IsDead() || _portal == null || Menu.IsVisible() || useAgain ||
                (_portal.transform.position - player.transform.position).sqrMagnitude > 20f * 20f)
            {
                CloseWindow();
                return;
            }
            if (_pending != null) { Action act = _pending; _pending = null; act(); if (!_open) return; }

            // A link that never shows up means the host's game has no PortalHub to carry it out.
            if (_expectDest != null && Time.time > _expectBy)
            {
                PortalInfo me = ThisPortal;
                if (me == null || me.Dest != _expectDest) Tell("Nothing changed. PortalHub has to be installed on the host's game too.");
                _expectDest = null;
            }

            BuildRows();
            if (ChromeSignature() != _chromeShown) BuildChrome();
            UpdateList();
        }

        // ---- the list the menu shows ----

        private PortalInfo ThisPortal { get { RefreshPortalId(); return Find(_portalId); } }

        // A new portal gets its stored id from the host a moment after it is built: pick it up while the menu is open.
        private void RefreshPortalId()
        {
            ZNetView view = _portal != null ? _portal.GetComponent<ZNetView>() : null;
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null) return;
            string id = PortalId(zdo);
            if (id != _portalId) { _portalId = id; _rowsKey = null; }
        }

        private void BuildRows()
        {
            string key = _search + "|" + _sortByName + "|" + PortalsAt + "|" + _favorites.Value;
            if (key == _rowsKey) return;
            _rowsKey = key;

            Vector3 from = _portal != null ? _portal.transform.position : Vector3.zero;
            HashSet<string> favorites = Favorites();
            IEnumerable<Row> rows = Portals.Where(p => p.Id != _portalId).Select(p => new Row
            {
                Info = p, Distance = Vector3.Distance(from, p.Pos), Favorite = favorites.Contains(p.Id), Where = BiomeOf(p.Pos),
            });
            if (_search.Length > 0) rows = rows.Where(r => NameOf(r.Info).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0 || r.Where.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);

            IOrderedEnumerable<Row> ordered = rows.OrderByDescending(r => r.Favorite);
            _rows = (_sortByName ? ordered.ThenBy(r => NameOf(r.Info), StringComparer.OrdinalIgnoreCase) : ordered.ThenBy(r => r.Distance)).ToList();
        }

        private static string NameOf(PortalInfo p) => string.IsNullOrWhiteSpace(p.Name) ? "(no name) " + (int)p.Pos.x + ", " + (int)p.Pos.z : p.Name;

        private static string BiomeOf(Vector3 pos)
        {
            try
            {
                if (WorldGenerator.instance == null) return "";
                string biome = WorldGenerator.instance.GetBiome(pos).ToString();
                return Localization.instance.Localize("$biome_" + biome.ToLowerInvariant());
            }
            catch (Exception) { return ""; }
        }

        private static string Far(float metres) => metres >= 1000f ? (metres / 1000f).ToString("0.0") + " km" : (int)metres + " m";

        // ---- actions (run from UpdateWindow) ----

        private void SaveName()
        {
            PortalInfo me = ThisPortal;
            if ((me == null || _nameEdit != me.Name) && _portal != null)
            {
                _portal.SetText(_nameEdit.Trim()); // the game's own rename, so everyone sees it
                _nextAsk = Time.time + 0.8f;
            }
        }

        private void Link(string dest)
        {
            SetDestination(_portalId, dest, _linkBothWays.Value);
            _expectDest = dest;
            _expectBy = Time.time + 4f;
        }

        private static void ShowOnMap(Vector3 pos)
        {
            if (Minimap.instance != null) Minimap.instance.ShowPointOnMap(pos);
        }

        // ---- the window ----

        /// <summary>The parts made once: the panel, the name and search boxes, and the list's box.</summary>
        private void BuildWindow()
        {
            _window = GameUi.Window("PortalHubWindow", new Vector2(WinW, WinH));
            Transform w = _window;
            // the boxes: typing goes into them while the rest of the window is built again around them
            _nameField = GameUi.InputField(w, new Vector2(Pad + 80f, -92f), new Vector2(WinW - Pad * 2f - 80f - 132f, 40f), _nameEdit, "A name for this portal", 16,
                s => _nameEdit = s, s => _pending = SaveName, 19f);
            _searchField = GameUi.InputField(w, new Vector2(Pad + 80f, -206f), new Vector2(WinW - Pad * 2f - 80f - 160f - 96f - 20f, 40f), "", "Search names and lands", 40,
                s => { _search = s; _scrollTop = true; }, null, 19f);
            RectTransform box = GameUi.Box(w, new Vector2(Pad - 6f, -ListTop), new Vector2(WinW - Pad * 2f + 12f, WinH - ListTop - 84f));
            _list = GameUi.Scroll(box, 8f);
            // make the rows coming into view as it scrolls (in UpdateWindow alone they would show a frame late)
            ScrollRect scroll = _list.parent.GetComponent<ScrollRect>();
            if (scroll != null) scroll.onValueChanged.AddListener(_ => { if (_open && _list != null) FillRows(ThisPortal); });
            BuildChrome();
        }

        /// <summary>Everything the window shows around the boxes and the list, so it is built again only when something changed.</summary>
        private string ChromeSignature()
        {
            PortalInfo me = ThisPortal;
            PortalInfo goesTo = me != null && me.Dest.Length > 0 ? Find(me.Dest) : null;
            bool silent = !IsServer && PortalsAt < _openedAt && Time.time - _openedAt > 3f;
            return string.Join("|", new[]
            {
                me != null ? me.Name : "\u0001", me != null ? me.Dest : "", goesTo != null ? NameOf(goesTo) + Far(Vector3.Distance(me.Pos, goesTo.Pos)) : "",
                (me == null || _nameEdit != me.Name).ToString(), silent.ToString(), (_search.Length > 0).ToString(), _sortByName.ToString(),
                _linkBothWays.Value.ToString(), (Portals.Count <= 1).ToString(),
            });
        }

        private void BuildChrome()
        {
            if (_window == null) return;
            if (_chrome != null) Destroy(_chrome.gameObject);
            UITooltip.HideTooltip();
            _chromeShown = ChromeSignature();
            _chrome = GameUi.Rect(_window, "Chrome", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(WinW, WinH));
            _chrome.SetAsFirstSibling();   // (under the boxes)
            Transform c = _chrome;

            PortalInfo me = ThisPortal;
            PortalInfo goesTo = me != null && me.Dest.Length > 0 ? Find(me.Dest) : null;

            // title, close, the braid under them
            GameUi.Label(c, "Portal", new Vector2(Pad, -20f), new Vector2(300f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            GameUi.Button(c, "Close", new Vector2(WinW - Pad - 120f, -24f), new Vector2(120f, 40f), () => _pending = CloseWindow);
            GameUi.Braid(c, new Vector2(0f, -76f), WinW - Pad * 2f);

            // its name (the box takes up to 16 letters), saved with the game's own rename
            GameUi.Label(c, "Name", new Vector2(Pad, -100f), new Vector2(80f, 30f), 20f, GameUi.Dim, GameUi.Serif);
            bool changed = me == null || _nameEdit != me.Name;
            Button save = GameUi.Button(c, "Save", new Vector2(WinW - Pad - 120f, -92f), new Vector2(120f, 40f), () => _pending = SaveName, changed && _portal != null);
            if (changed) GameUi.Label(save).color = GameUi.Gold;

            // where it goes now
            string status; Color color;
            if (goesTo != null) { status = "Goes to:  " + GameUi.Plain(NameOf(goesTo)) + "  ·  " + Far(Vector3.Distance(me.Pos, goesTo.Pos)); color = GameUi.Good; }
            else if (me != null && me.Dest.Length > 0) { status = "Goes to a portal that no longer exists."; color = GameUi.Dim; }
            else { status = "Not linked here yet. <size=85%><color=#bdb7a9>(Without a choice, a portal still joins another with the same name.)</color></size>"; color = GameUi.Text; }
            float statusW = WinW - Pad * 2f - 132f, y = -146f;
            TMP_Text line = GameUi.Label(c, status, new Vector2(Pad, y), new Vector2(statusW, 30f), 19f, color);
            float h = Mathf.Min(48f, line.GetPreferredValues(status, statusW, 0f).y);
            ((RectTransform)line.transform).sizeDelta = new Vector2(statusW, h);
            if (me != null && me.Dest.Length > 0)
                GameUi.Button(c, "Unlink", new Vector2(WinW - Pad - 120f, -142f), new Vector2(120f, 36f), () => _pending = () => SetDestination(_portalId, "", false), true, 17f);
            if (!IsServer && PortalsAt < _openedAt && Time.time - _openedAt > 3f)
                GameUi.Label(c, "The host's game is not answering. PortalHub has to be installed on the host too.", new Vector2(Pad, y - h - 2f),
                    new Vector2(statusW, 24f), 16f, GameUi.Warn);

            // search and order
            GameUi.Label(c, "Search", new Vector2(Pad, -214f), new Vector2(80f, 30f), 20f, GameUi.Dim, GameUi.Serif);
            if (_search.Length > 0)
                GameUi.Button(c, "Clear", new Vector2(WinW - Pad - 160f - 8f - 96f, -206f), new Vector2(96f, 40f), () => _pending = () => { if (_searchField != null) _searchField.text = ""; }, true, 17f);
            GameUi.Button(c, _sortByName ? "Order: A-Z" : "Order: nearest", new Vector2(WinW - Pad - 160f, -206f), new Vector2(160f, 40f),
                () => _pending = () => { _sortByName = !_sortByName; _rowsKey = null; _scrollTop = true; }, true, 17f);

            // link it back too (an on/off setting: a button that says its state, gold when on)
            bool both = _linkBothWays.Value;
            Button toggle = GameUi.Button(c, both ? "Link it back too: On" : "Link it back too: Off", new Vector2(Pad, -256f), new Vector2(260f, 40f),
                () => _pending = () => _linkBothWays.Value = !_linkBothWays.Value, true, 17f);
            if (both) GameUi.Label(toggle).color = GameUi.Gold;
            GameUi.Label(c, "the other portal then comes here", new Vector2(Pad + 274f, -265f), new Vector2(WinW - Pad * 2f - 274f, 26f), 17f, GameUi.Dim);

            // a note under the list
            string note = Portals.Count <= 1 ? "No other portals found yet."
                : "Star a portal to keep it at the top. \"has a link\" means linking back would replace that portal's current link.";
            GameUi.Label(c, note, new Vector2(Pad, -(WinH - 70f)), new Vector2(WinW - Pad * 2f, 48f), 16f, GameUi.Dim);
        }

        // ---- the list: only the rows in view are made ----

        private int _first = -1, _last = -1;

        private void UpdateList()
        {
            if (_list == null) return;
            PortalInfo me = ThisPortal;
            string key = _rowsKey + "|" + (me != null ? me.Dest : "") + "|" + _linkBothWays.Value + "|" + _portalId;
            if (key != _listKey)
            {
                _listKey = key;
                // what the rows show (the list is fetched again every few seconds: build again only if it really changed)
                var sb = new System.Text.StringBuilder(key.Length + _rows.Count * 24);
                sb.Append(me != null ? me.Dest : "").Append('|').Append(_linkBothWays.Value).Append('|').Append(_search.Length > 0);
                foreach (Row r in _rows)
                    sb.Append('|').Append(r.Info.Id).Append(',').Append(r.Info.Name).Append(',').Append(r.Info.Dest).Append(',').Append(r.Favorite)
                      .Append(',').Append(r.Where).Append(',').Append(Far(r.Distance));
                string shown = sb.ToString();
                if (shown != _listShown) { _listShown = shown; RebuildList(); }
            }
            if (_scrollTop) { _scrollTop = false; _list.anchoredPosition = Vector2.zero; }
            FillRows(me);
        }

        private void RebuildList()
        {
            foreach (Transform child in _list.Cast<Transform>().ToList()) Destroy(child.gameObject);
            _rowObjects.Clear();
            _first = _last = -1;
            UITooltip.HideTooltip();
            float width = _list.sizeDelta.x;
            if (_rows.Count == 0)
            {
                string text = _search.Length > 0 ? "No match." : "Build a second portal and it will show up here.";
                TMP_Text t = GameUi.Label(_list, text, new Vector2(10f, -10f), new Vector2(width - 20f, 30f), 19f, GameUi.Dim);
                _list.sizeDelta = new Vector2(width, t.GetPreferredValues(text, width - 20f, 0f).y + 20f);
            }
            else _list.sizeDelta = new Vector2(width, _rows.Count * RowH - 4f);   // exactly: no gap after the last row (it would scroll for nothing)
            float top = Mathf.Max(0f, _list.sizeDelta.y - ((RectTransform)_list.parent).rect.height);
            _list.anchoredPosition = new Vector2(0f, Mathf.Clamp(_list.anchoredPosition.y, 0f, top));
        }

        /// <summary>Makes the rows that are in view (and a few around them) and drops the rest.</summary>
        private void FillRows(PortalInfo me)
        {
            if (_rows.Count == 0) return;
            float top = _list.anchoredPosition.y, view = ((RectTransform)_list.parent).rect.height;
            int first = Mathf.Max(0, Mathf.FloorToInt(top / RowH) - 3);
            int last = Mathf.Min(_rows.Count - 1, Mathf.CeilToInt((top + view) / RowH) + 3);
            if (first == _first && last == _last) return;
            _first = first; _last = last;
            foreach (int i in _rowObjects.Keys.Where(i => i < first || i > last).ToList())
            {
                if (_rowObjects[i] != null) Destroy(_rowObjects[i]);
                _rowObjects.Remove(i);
            }
            for (int i = first; i <= last; i++)
                if (!_rowObjects.ContainsKey(i)) _rowObjects[i] = MakeRow(i, me);
        }

        private GameObject MakeRow(int index, PortalInfo me)
        {
            Row row = _rows[index];
            string id = row.Info.Id;
            float width = _list.sizeDelta.x;
            bool linked = me != null && me.Dest == id;
            RectTransform r = GameUi.Rect(_list, "Row", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -index * RowH), new Vector2(width, RowH - 4f));
            if (linked)
            {
                var bg = r.gameObject.AddComponent<Image>();
                bg.color = new Color(0.3f, 0.5f, 0.25f, 0.35f);
                bg.raycastTarget = false;
            }

            Button star = GameUi.Button(r, row.Favorite ? "Starred" : "Star", new Vector2(2f, -3f), new Vector2(78f, 34f), () => _pending = () => ToggleFavorite(id), true, 16f);
            if (row.Favorite) GameUi.Label(star).color = GameUi.Gold;

            const float linkW = 86f, mapW = 64f, detailW = 176f;
            float detailX = width - linkW - 8f - mapW - 8f - detailW;
            TMP_Text name = GameUi.Label(r, NameOf(row.Info), new Vector2(92f, -7f), new Vector2(detailX - 92f - 8f, 28f), 19f, GameUi.Text);
            name.richText = false;   // (a name is the player's text)
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;

            bool replaces = _linkBothWays.Value && !linked && row.Info.Dest.Length > 0 && row.Info.Dest != _portalId;
            string detail = replaces ? "has a link" : (row.Where.Length > 0 ? row.Where + " · " : "") + Far(row.Distance);
            TMP_Text where = GameUi.Label(r, detail, new Vector2(detailX, -10f), new Vector2(detailW, 24f), 16f, replaces ? GameUi.Warn : GameUi.Dim);
            where.enableWordWrapping = false;
            where.overflowMode = TextOverflowModes.Ellipsis;

            Vector3 pos = row.Info.Pos;
            GameUi.Button(r, "Map", new Vector2(width - linkW - 8f - mapW, -3f), new Vector2(mapW, 34f), () => _pending = () => { CloseWindow(); ShowOnMap(pos); }, true, 16f);
            Button link = GameUi.Button(r, linked ? "Linked" : "Link", new Vector2(width - linkW, -3f), new Vector2(linkW, 34f), () => _pending = () => Link(id), !linked, 16f);
            if (linked) GameUi.Label(link).color = GameUi.Gold;
            return r.gameObject;
        }
    }
}
