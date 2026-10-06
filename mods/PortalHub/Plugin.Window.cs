using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PortalHub
{
    /// <summary>The portal menu: pick where this portal goes.</summary>
    public partial class Plugin
    {
        /// <summary>True while the menu is open (the input patches use it to stop the game reacting to clicks and typing).</summary>
        internal static bool WindowOpen;

        private TeleportWorld _portal;
        private string _portalId = "";
        private string _search = "";
        private bool _sortByName;
        private Vector2 _scroll;
        private Rect _rect;
        private bool _placed;
        private float _openedAt;
        private string _nameEdit = "";
        private string _expectDest; private float _expectBy;

        private class Row { public PortalInfo Info; public float Distance; public bool Favorite; public string Where; }
        private List<Row> _rows = new List<Row>();
        private string _rowsKey;

        // ---- opening and closing ----

        internal void OpenWindow(TeleportWorld portal)
        {
            ZNetView view = portal.GetComponent<ZNetView>();
            if (view == null || view.GetZDO() == null) return;
            _portal = portal;
            _portalId = PortalId(view.GetZDO());
            _search = "";
            _scroll = Vector2.zero;
            _rowsKey = null;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            WindowOpen = true;
            _openedAt = Time.time;
            _expectDest = null;
            ZDO zdo = view.GetZDO();
            _nameEdit = zdo != null ? (zdo.GetString(ZDOVars.s_tag) ?? "") : "";
            RequestList();
            _nextAsk = Time.time + 5f;
        }

        internal static void CloseFromEscape() => Instance?.CloseWindow();

        private void CloseWindow()
        {
            WindowOpen = false;
            _portal = null;
        }

        private void UpdateWindow()
        {
            if (!WindowOpen) return;
            Player player = Player.m_localPlayer;
            if (player == null || _portal == null || Menu.IsVisible() || (_portal.transform.position - player.transform.position).sqrMagnitude > 20f * 20f)
                CloseWindow();

            // A link that never shows up means the host's game has no PortalHub to carry it out.
            if (_expectDest != null && Time.time > _expectBy)
            {
                PortalInfo me = ThisPortal;
                if (me == null || me.Dest != _expectDest) Tell("Nothing changed. PortalHub has to be installed on the host's game too.");
                _expectDest = null;
            }
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

        // ---- drawing ----

        private void OnGUI()
        {
            DrawMapLines();
            if (!WindowOpen || _portal == null) return;
            FreeTheMouse();
            EnsureStyles();
            BuildRows();

            Matrix4x4 previous = GUI.matrix;
            float s = Mathf.Max(0.8f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;

            float w = Mathf.Min(560f, sw - 40f), h = Mathf.Min(640f, sh - 60f);
            if (!_placed) { _rect = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h); _placed = true; }
            _rect.width = w; _rect.height = h;

            _rect = GUI.Window(8851, _rect, DrawContents, GUIContent.none, GUIStyle.none);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, sw - w));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, sh - h));
            GUI.matrix = previous;
        }

        private void DrawContents(int id)
        {
            float w = _rect.width, h = _rect.height;
            Rounded(new Rect(0, 0, w, h), new Color(0.07f, 0.06f, 0.05f, 0.98f), 9f);
            Outline(new Rect(0, 0, w, h), new Color(0.62f, 0.47f, 0.22f, 1f), 9f);

            PortalInfo me = ThisPortal;
            string myName = me != null ? NameOf(me) : "this portal";
            PortalInfo goesTo = me != null && me.Dest.Length > 0 ? Find(me.Dest) : null;

            GUILayout.BeginArea(new Rect(16f, 12f, w - 32f, h - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Portal: " + myName, _title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _btn, GUILayout.Width(70), GUILayout.Height(28))) CloseWindow();
            GUILayout.EndHorizontal();

            // Name it right here (the box takes up to 16 letters).
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name:", _text, GUILayout.Width(58));
            _nameEdit = GUILayout.TextField(_nameEdit ?? "", 16, _field, GUILayout.Height(26));
            bool changed = me == null || _nameEdit != me.Name;
            if (GUILayout.Button("Save", changed ? _btnOn : _btn, GUILayout.Width(70), GUILayout.Height(26)) && changed && _portal != null)
            {
                _portal.SetText(_nameEdit.Trim()); // the game's own rename, so everyone sees it
                _nextAsk = Time.time + 0.8f;
            }
            GUILayout.EndHorizontal();

            if (!IsServer && PortalsAt < _openedAt && Time.time - _openedAt > 3f)
                GUILayout.Label("The host's game is not answering. PortalHub has to be installed on the host too.", _dim);

            // Where it goes now.
            if (goesTo != null) GUILayout.Label("Goes to:  " + NameOf(goesTo) + "  ·  " + Far(Vector3.Distance(me.Pos, goesTo.Pos)), _good);
            else if (me != null && me.Dest.Length > 0) GUILayout.Label("Goes to a portal that no longer exists.", _dim);
            else GUILayout.Label("Not linked here yet. (Without a choice, a portal still joins another with the same name.)", _dim);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", _text, GUILayout.Width(58));
            _search = GUILayout.TextField(_search, _field, GUILayout.Height(26));
            if (_search.Length > 0 && GUILayout.Button("x", _btn, GUILayout.Width(28), GUILayout.Height(26))) _search = "";
            if (GUILayout.Button(_sortByName ? "A-Z" : "Nearest", _btn, GUILayout.Width(80), GUILayout.Height(26))) { _sortByName = !_sortByName; _rowsKey = null; }
            GUILayout.EndHorizontal();

            bool both = _linkBothWays.Value;
            bool bothNow = GUILayout.Toggle(both, "  Link it back too (the other portal comes here)", _toggle);
            if (bothNow != both) _linkBothWays.Value = bothNow;
            GUILayout.Space(4);

            DrawRows(me);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Portals.Count <= 1 ? "No other portals found yet." : "Star a portal to keep it at the top. \"has a link\" means linking back would replace that portal's current link.", _dim);
            GUILayout.FlexibleSpace();
            if (me != null && me.Dest.Length > 0 && GUILayout.Button("Unlink", _btn, GUILayout.Width(90), GUILayout.Height(26)))
                SetDestination(_portalId, "", false);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, w, 40));
        }

        private void DrawRows(PortalInfo me)
        {
            if (_rows.Count == 0)
            {
                GUILayout.Label(_search.Length > 0 ? "No match." : "Build a second portal and it will show up here.", _dim);
                GUILayout.FlexibleSpace();
                return;
            }

            const float rowHeight = 34f;
            Rect area = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var content = new Rect(0f, 0f, area.width - 20f, _rows.Count * rowHeight);
            _scroll = GUI.BeginScrollView(area, _scroll, content);

            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / rowHeight));
            int last = Mathf.Min(_rows.Count - 1, first + Mathf.CeilToInt(area.height / rowHeight) + 1);
            for (int i = first; i <= last; i++)
            {
                Row row = _rows[i];
                float y = i * rowHeight;
                bool linked = me != null && me.Dest == row.Info.Id;

                if (linked) Rounded(new Rect(0f, y, content.width, rowHeight - 2f), new Color(0.2f, 0.32f, 0.2f, 0.7f), 5f);

                if (GUI.Button(new Rect(2f, y + 4f, 26f, 26f), row.Favorite ? "★" : "☆", row.Favorite ? _btnOn : _btn)) { ToggleFavorite(row.Info.Id); }
                GUI.Label(new Rect(36f, y + 2f, content.width - 250f, 30f), NameOf(row.Info), _text);
                bool replaces = _linkBothWays.Value && !linked && row.Info.Dest.Length > 0 && row.Info.Dest != _portalId;
                string detail = (row.Where.Length > 0 ? row.Where + " · " : "") + Far(row.Distance);
                GUI.Label(new Rect(content.width - 214f, y + 2f, 100f, 30f), replaces ? "has a link" : detail, replaces ? _warn : _dim);

                if (GUI.Button(new Rect(content.width - 112f, y + 3f, 52f, 28f), "Map", _btn)) { var pos = row.Info.Pos; CloseWindow(); ShowOnMap(pos); }
                if (GUI.Button(new Rect(content.width - 56f, y + 3f, 56f, 28f), linked ? "Linked" : "Link", linked ? _btnOn : _btn) && !linked)
                { SetDestination(_portalId, row.Info.Id, _linkBothWays.Value); _expectDest = row.Info.Id; _expectBy = Time.time + 4f; }
            }
            GUI.EndScrollView();
        }

        private static void ShowOnMap(Vector3 pos)
        {
            if (Minimap.instance != null) Minimap.instance.ShowPointOnMap(pos);
        }

        // ---- the mouse ----

        /// <summary>Unity's cursor is set directly while the window draws, because the game doesn't always free it (Linux, controllers).</summary>
        private static void FreeTheMouse()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        // ---- look ----

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private GUIStyle _warn, _title, _text, _dim, _good, _btn, _btnOn, _toggle, _field;
        private Texture2D _roundedTexture;

        private Texture2D Box(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _textures.Add(t);
            return t;
        }

        private GUIStyle Button(Color fill, Color hover, Color border)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(3, 3, 3, 3),
            };
            s.normal.background = Box(fill, border);
            s.hover.background = s.active.background = s.focused.background = Box(hover, border);
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = new Color(0.95f, 0.9f, 0.8f);
            return s;
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
            _text = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _text.normal.textColor = new Color(0.93f, 0.9f, 0.85f);
            _dim = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            _dim.normal.textColor = new Color(0.68f, 0.66f, 0.62f);
            _warn = new GUIStyle(_dim); _warn.normal.textColor = new Color(0.95f, 0.65f, 0.3f);
            _good = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _good.normal.textColor = new Color(0.6f, 0.9f, 0.6f);
            _btn = Button(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.45f, 0.36f, 0.2f));
            _btnOn = Button(new Color(0.55f, 0.42f, 0.16f), new Color(0.62f, 0.48f, 0.2f), new Color(0.95f, 0.78f, 0.35f));
            _toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 13 };
            _toggle.normal.textColor = _toggle.onNormal.textColor = _toggle.hover.textColor = _toggle.onHover.textColor =
                _toggle.active.textColor = _toggle.onActive.textColor = new Color(0.95f, 0.9f, 0.8f);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
        }

        private void DestroyStyles()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _title = _text = _dim = _warn = _good = _btn = _btnOn = _toggle = _field = null;
        }

        private static void Rounded(Rect r, Color c, float radius)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.zero, new Vector4(radius, radius, radius, radius));
        }

        private static void Outline(Rect r, Color c, float radius)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, Vector4.one * 1.5f, new Vector4(radius, radius, radius, radius));
        }
    }
}
