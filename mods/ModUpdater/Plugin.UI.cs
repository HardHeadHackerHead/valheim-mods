using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModUpdater
{
    /// <summary>The F7 window. Unity's immediate-mode GUI (OnGUI), with our own dark, opaque styling.</summary>
    public partial class Plugin
    {
        private const int WindowId = 7731;

        // ---- palette ---------------------------------------------------------------------------
        private static readonly Color Gold = new Color(0.95f, 0.78f, 0.35f);
        private static readonly Color TextMain = new Color(0.92f, 0.90f, 0.86f);
        private static readonly Color TextDim = new Color(0.62f, 0.60f, 0.56f);
        private static readonly Color Good = new Color(0.50f, 0.95f, 0.58f);
        private static readonly Color Warn = new Color(1f, 0.78f, 0.30f);
        private static readonly Color Bad = new Color(1f, 0.50f, 0.50f);

        private static readonly Color PillGreen = new Color(0.17f, 0.42f, 0.24f);
        private static readonly Color PillAmber = new Color(0.58f, 0.40f, 0.08f);
        private static readonly Color PillRed = new Color(0.52f, 0.18f, 0.18f);
        private static readonly Color PillBlue = new Color(0.17f, 0.33f, 0.58f);
        private static readonly Color PillGrey = new Color(0.27f, 0.27f, 0.27f);

        // ---- state -----------------------------------------------------------------------------
        private Rect _window;
        private bool _windowPlaced;
        private Vector2 _scroll;
        private string _confirmRevert;      // which mod's "Use GitHub copy" is waiting for a second click
        private float _confirmRevertAt;
        private Action _deferred;   // clicks are run from Update, not mid-draw, so the layout never changes under IMGUI

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private bool _stylesReady;
        private GUIStyle _sWindow, _sCard, _sTitle, _sH2, _sName, _sBody, _sDim, _sVer, _sPill, _sBtn, _sBtnSmall, _sBtnPrimary, _sToggle, _sRule;

        private void ToggleWindow()
        {
            WindowOpen = !WindowOpen;
            if (!WindowOpen) return;

            ScanLocal();
            BuildRows();
            // Refresh when opened if we've never checked, or it's been a while.
            if (Configured && (DateTime.Now - _lastRefresh).TotalSeconds > 60)
                StartCoroutine(RefreshRoutine(autoInstall: false));
        }

        // ---- styles ----------------------------------------------------------------------------

        private Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            _textures.Add(t);
            return t;
        }

        /// <summary>A tiny texture with a 2px border, used 9-sliced so any size gets a crisp outline.</summary>
        private Texture2D Boxed(Color fill, Color border)
        {
            const int size = 6;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, (x < 2 || y < 2 || x >= size - 2 || y >= size - 2) ? border : fill);
            t.Apply();
            _textures.Add(t);
            return t;
        }

        private static Font FindGameFont()
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f =>
                    f.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.name.IndexOf("Norse", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return null; }
        }

        private static GUIStyle TextStyle(int size, Color color, FontStyle fontStyle = FontStyle.Normal, bool wrap = false, Font font = null)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fontStyle, wordWrap = wrap, richText = false };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            return s;
        }

        private GUIStyle ButtonStyle(Color fill, Color hover, Color pressed, Color border, Color text)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(10, 10, 4, 4), margin = new RectOffset(3, 3, 3, 3),
            };
            s.normal.background = Boxed(fill, border);
            s.hover.background = Boxed(hover, border);
            s.active.background = Boxed(pressed, border);
            s.focused.background = s.normal.background;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = text;
            return s;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;
            Font font = FindGameFont();

            _sWindow = new GUIStyle(GUI.skin.window)
            {
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(18, 18, 14, 12),
            };
            Texture2D win = Boxed(new Color(0.075f, 0.065f, 0.055f, 1f), new Color(0.62f, 0.47f, 0.22f, 1f));
            _sWindow.normal.background = _sWindow.onNormal.background = win;
            _sWindow.normal.textColor = _sWindow.onNormal.textColor = TextMain;

            _sCard = new GUIStyle(GUI.skin.box)
            {
                border = new RectOffset(2, 2, 2, 2), padding = new RectOffset(12, 12, 9, 9), margin = new RectOffset(0, 0, 0, 0),
            };
            _sCard.normal.background = Boxed(new Color(0.13f, 0.115f, 0.10f, 1f), new Color(0.26f, 0.22f, 0.17f, 1f));

            _sTitle = TextStyle(22, Gold, FontStyle.Bold, false, font);
            _sH2 = TextStyle(15, Gold, FontStyle.Bold, false, font);
            _sName = TextStyle(15, TextMain, FontStyle.Bold, false, font);
            _sBody = TextStyle(12, TextMain, FontStyle.Normal, true);
            _sDim = TextStyle(12, TextDim, FontStyle.Normal, true);
            _sVer = TextStyle(13, TextDim);

            _sPill = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 2, 2), margin = new RectOffset(2, 2, 2, 2),
            };
            _sPill.normal.background = Solid(Color.white); // tinted per use through GUI.backgroundColor
            _sPill.normal.textColor = Color.white;

            _sBtn = ButtonStyle(new Color(0.22f, 0.19f, 0.15f), new Color(0.33f, 0.27f, 0.18f), new Color(0.42f, 0.33f, 0.16f),
                                new Color(0.45f, 0.36f, 0.2f), TextMain);
            _sBtnSmall = new GUIStyle(_sBtn) { fontSize = 12, padding = new RectOffset(4, 4, 2, 2) };
            _sBtnPrimary = ButtonStyle(new Color(0.18f, 0.40f, 0.24f), new Color(0.24f, 0.52f, 0.31f), new Color(0.15f, 0.33f, 0.2f),
                                       new Color(0.4f, 0.8f, 0.5f), Color.white);

            _sToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            _sToggle.normal.textColor = _sToggle.onNormal.textColor = _sToggle.hover.textColor = _sToggle.onHover.textColor =
                _sToggle.active.textColor = _sToggle.onActive.textColor = TextMain;

            _sRule = new GUIStyle { margin = new RectOffset(0, 0, 8, 8), fixedHeight = 1 };
            _sRule.normal.background = Solid(new Color(0.35f, 0.29f, 0.2f, 1f));
        }

        private void DestroyUi()
        {
            foreach (Texture2D t in _textures) if (t != null) Destroy(t);
            _textures.Clear();
            _stylesReady = false;
        }

        // ---- window ----------------------------------------------------------------------------

        private void OnGUI()
        {
            if (!WindowOpen) return;
            EnsureStyles();

            // Scale with the screen so text stays readable at 1440p/4K (UiScale in the config adjusts it further).
            float s = Mathf.Max(0.75f, Screen.height / 1080f) * Mathf.Clamp(_uiScale.Value, 0.5f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            float sw = Screen.width / s, sh = Screen.height / s;
            float w = Mathf.Min(960f, sw - 30f), h = Mathf.Min(700f, sh - 30f);

            if (!_windowPlaced)
            {
                _window = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
                _windowPlaced = true;
            }
            _window.width = w; // size follows the screen; the position is draggable
            _window.height = h;

            _window = GUI.Window(WindowId, _window, DrawWindow, GUIContent.none, _sWindow);
            _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, sw - w));
            _window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, sh - h));
        }

        private void DrawWindow(int id)
        {
            DrawHeader();
            DrawStatusLine();
            GUILayout.Space(6);
            if (_needsSetup) { DrawSetup(); GUILayout.Space(8); }

            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
            DrawMods();
            GUILayout.Space(14);
            DrawPlayers();
            GUILayout.EndScrollView();

            DrawFooter();
            GUI.DragWindow(new Rect(0, 0, 10000, 46)); // drag by the header
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mod Manager", _sTitle, GUILayout.ExpandWidth(false));
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            GUILayout.Space(9);
            GUILayout.Label($"v{Version}", _sVer, GUILayout.ExpandWidth(false));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            int pending = _rows.Count(NeedsUpdate);
            if (Button("Refresh", 90, false, !_busy && Configured)) Defer(() => StartCoroutine(RefreshRoutine(autoInstall: false)));
            if (Button(pending > 0 ? $"Update all ({pending})" : "Update all", 130, pending > 0, !_busy && pending > 0)) Defer(InstallAll);
            if (Button("Close", 70)) Defer(() => WindowOpen = false);
            GUILayout.EndHorizontal();
        }

        private void DrawStatusLine()
        {
            bool failed = _statusLine.StartsWith("Check failed") || _statusLine.StartsWith("Download failed") ||
                          _statusLine.StartsWith("Couldn't") || _statusLine.StartsWith("Not connected") || _statusLine.StartsWith("No manifest");
            Color c = failed ? Bad : _busy ? Warn : TextDim;
            string text = _busy ? _statusLine + new string('.', 1 + (int)(Time.realtimeSinceStartup * 2f) % 3) : _statusLine;
            GUILayout.Label(text, TextStyle(12, c, FontStyle.Normal, true));
        }

        // ---- connecting to GitHub --------------------------------------------------------------

        private string _tokenInput = "";

        /// <summary>Shown only while we have no credentials: paste a read-only token, or explicitly allow the GitHub CLI.</summary>
        private void DrawSetup()
        {
            GUILayout.BeginVertical(_sCard);
            GUILayout.Label("Connect to GitHub", _sH2);

            if (string.IsNullOrEmpty(_owner.Value) || string.IsNullOrEmpty(_repo.Value))
            {
                GUILayout.Label($"Owner and Repo aren't set yet. Fill them in under [Repo] in BepInEx\\config\\{Guid}.cfg, then press F6.", _sBody);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label($"The mods are in a private repo ({_owner.Value}/{_repo.Value}), so the manager needs an access token for it. " +
                            "Create a fine-grained token on GitHub (Settings > Developer settings > Fine-grained tokens) limited to " +
                            "ONLY this repository with Contents set to Read-only, then paste it here.", _sBody);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            _tokenInput = GUILayout.PasswordField(_tokenInput, '*', 255, GUILayout.ExpandWidth(true), GUILayout.Height(28));
            if (Button("Save token", 120, true, _tokenInput.Trim().Length > 0))
                Defer(() =>
                {
                    _token.Value = _tokenInput.Trim();   // saved in the config file on this PC
                    _tokenInput = "";
                    StartCoroutine(RefreshRoutine(autoInstall: false));
                });
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Don't want to make a token? You can let the manager borrow your GitHub CLI (gh) login instead. " +
                            "Be aware: that login is your whole GitHub sign-in, with much broader access than one read-only token. " +
                            "Only allow it if you're comfortable with that. You can turn it off again at the bottom of this window.", _sDim);
            GUILayout.BeginHorizontal();
            if (Button("Allow GitHub CLI login", 200)) Defer(() => _allowGitHubCli.Value = true);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        // ---- mods ------------------------------------------------------------------------------

        private void DrawMods()
        {
            // Real mods get a card each: everything on GitHub, plus mods that only exist in your scripts folder (not published yet).
            // Loader plugins (ScriptEngine, ...) live in the plugins folder and just get a line at the bottom.
            List<Row> mods = _rows.Where(r => !IsLoader(r)).ToList();
            GUILayout.Label($"Mods  ({mods.Count})", _sH2);
            GUILayout.Space(2);

            foreach (Row row in mods) DrawCard(row);
            if (mods.Count == 0) GUILayout.Label("No mods found yet. Press Refresh.", _sDim);

            List<Row> others = _rows.Where(IsLoader).ToList();
            if (others.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("Loaders", _sH2);
                foreach (Row row in others)
                    GUILayout.Label($"{row.Name}   v{row.LocalVersion}", _sDim);
            }
        }

        private void DrawCard(Row row)
        {
            GUILayout.BeginVertical(_sCard);
            GUILayout.BeginHorizontal();

            // name, version, description
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(row.Name, _sName, GUILayout.ExpandWidth(false));
            GUILayout.Space(6);
            GUILayout.BeginVertical();
            GUILayout.Space(2);
            GUILayout.Label(VersionText(row), _sVer, GUILayout.ExpandWidth(false));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(row.Description)) GUILayout.Label(row.Description, _sDim);
            GUILayout.EndVertical();

            // status badge
            StatusLook(row, out string pillText, out Color pillColor);
            GUILayout.BeginVertical(GUILayout.Width(170));
            GUILayout.FlexibleSpace();
            Pill(pillText, pillColor, 164);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            // actions
            GUILayout.BeginVertical(GUILayout.Width(130));
            GUILayout.FlexibleSpace();
            switch (row.Status)
            {
                case Status.NotInstalled:
                    if (Button("Install", 124, true, !_busy)) Defer(() => InstallOne(row));
                    break;
                case Status.UpdateAvailable:
                    if (Button("Update", 124, true, !_busy)) Defer(() => InstallOne(row));
                    break;
                case Status.Rebuilt:
                    if (!_developerMode.Value)
                    {
                        if (Button("Update", 124, true, !_busy)) Defer(() => InstallOne(row));
                    }
                    else
                    {
                        // Developer Mode: this overwrites your own build, so ask twice (the "sure?" state times out after 5s).
                        bool sure = _confirmRevert == row.Name && Time.realtimeSinceStartup - _confirmRevertAt < 5f;
                        if (Button(sure ? "Really replace mine?" : "Use GitHub copy", 124, false, !_busy))
                        {
                            if (sure) Defer(() => { _confirmRevert = null; InstallOne(row); });
                            else Defer(() => { _confirmRevert = row.Name; _confirmRevertAt = Time.realtimeSinceStartup; });
                        }
                    }
                    break;
            }
            if (row.CanToggle)
            {
                bool disabled = row.Status == Status.Disabled;
                GUILayout.BeginHorizontal();
                if (!disabled && Button("Reload", 60, false, !_busy, small: true)) Defer(() => ReloadOne(row));
                if (Button(disabled ? "Enable" : "Disable", disabled ? 124 : 60, disabled, !_busy, small: !disabled)) Defer(() => SetEnabled(row, disabled));
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // what's new, only when there's something to get
            if (!string.IsNullOrEmpty(row.Notes) && (row.Status == Status.UpdateAvailable || row.Status == Status.NotInstalled))
            {
                GUILayout.Space(4);
                GUILayout.Label("What's new in v" + row.RemoteVersion + ":  " + row.Notes, _sBody);
            }

            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        /// <summary>A loader plugin from the plugins folder (ScriptEngine and friends): not one of "our" mods.</summary>
        private static bool IsLoader(Row row) => row.Status == Status.LocalOnly && row.Local != null && row.Local.InPlugins;

        private static string VersionText(Row row)
        {
            switch (row.Status)
            {
                case Status.UpdateAvailable: return $"v{row.LocalVersion}  ->  v{row.RemoteVersion}";
                case Status.NotInstalled: return $"v{row.RemoteVersion} on GitHub";
                case Status.LocalNewer: return $"v{row.LocalVersion}   (GitHub has v{row.RemoteVersion})";
                default: return $"v{row.LocalVersion}";
            }
        }

        private void StatusLook(Row row, out string text, out Color color)
        {
            switch (row.Status)
            {
                case Status.UpToDate: text = "Up to date"; color = PillGreen; break;
                case Status.UpdateAvailable: text = "Update available"; color = PillAmber; break;
                case Status.NotInstalled: text = "Not installed"; color = PillRed; break;
                case Status.LocalNewer: text = "Newer than GitHub"; color = PillBlue; break;
                case Status.Rebuilt:
                    // Same version number but different bytes: for the mod's author that's just "I rebuilt it and haven't pushed".
                    if (_developerMode.Value) { text = "Unpublished changes"; color = PillBlue; }
                    else { text = "Update available"; color = PillAmber; }
                    break;
                case Status.Disabled: text = "Disabled"; color = PillGrey; break;
                default:
                    // A mod in your scripts folder that isn't on GitHub (yet): fine while you're building it.
                    if (row.Local != null && !row.Local.InPlugins) { text = "Not published yet"; color = PillBlue; }
                    else { text = "Local only"; color = PillGrey; }
                    break;
            }
        }

        // ---- players ---------------------------------------------------------------------------

        private void DrawPlayers()
        {
            GUILayout.Label("Players", _sH2);
            GUILayout.Space(2);

            if (_remote.Length == 0)
            {
                GUILayout.Label("Press Refresh to compare against the versions on GitHub.", _sDim);
                return;
            }
            if (ZRoutedRpc.instance == null || Player.m_localPlayer == null)
            {
                GUILayout.Label("Join a world to see what the other players have.", _sDim);
                return;
            }

            const float nameWidth = 150f;
            float colWidth = Mathf.Clamp((_window.width - nameWidth - 90f) / Mathf.Max(1, _remote.Length), 110f, 220f);

            GUILayout.BeginVertical(_sCard);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Player", _sDim, GUILayout.Width(nameWidth));
            foreach (RemoteMod r in _remote) GUILayout.Label(r.name, _sDim, GUILayout.Width(colWidth));
            GUILayout.EndHorizontal();

            DrawPlayerRow("You", VersionsOf(_local), colWidth, nameWidth);

            Dictionary<long, string> others = OtherPlayers();
            int behind = 0;
            foreach (var kv in others)
            {
                if (_peers.TryGetValue(kv.Key, out PeerMods peer))
                {
                    if (DrawPlayerRow(kv.Value, peer.Versions, colWidth, nameWidth)) behind++;
                }
                else
                {
                    behind++;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(kv.Value, _sName, GUILayout.Width(nameWidth));
                    Pill("no mod manager yet", PillRed, colWidth * _remote.Length - 6);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            if (others.Count == 0) GUILayout.Label("Nobody else is in this world right now.", _sDim);
            else if (behind == 0) GUILayout.Label("Everyone is up to date.", TextStyle(12, Good));
            else GUILayout.Label($"{behind} player(s) need updates. They can press {_hotkey.Value} and click Update all.", TextStyle(12, Warn, FontStyle.Normal, true));
        }

        private static Dictionary<string, string> VersionsOf(IEnumerable<LocalMod> mods)
        {
            var d = new Dictionary<string, string>();
            foreach (LocalMod m in mods) if (!m.Disabled) d[m.Guid] = m.Version; // (a plain loop: tolerates duplicate guids)
            return d;
        }

        /// <summary>One line per player; returns true if they're missing something or behind.</summary>
        private bool DrawPlayerRow(string who, Dictionary<string, string> versions, float colWidth, float nameWidth)
        {
            bool behind = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(who, _sName, GUILayout.Width(nameWidth));
            foreach (RemoteMod r in _remote)
            {
                if (!versions.TryGetValue(r.guid, out string v)) { Pill("missing", PillRed, colWidth - 6); behind = true; }
                else if (CompareVersions(v, r.version) >= 0) Pill("v" + v, PillGreen, colWidth - 6);
                else { Pill($"v{v} (old)", PillAmber, colWidth - 6); behind = true; }
            }
            GUILayout.EndHorizontal();
            return behind;
        }

        // ---- footer ----------------------------------------------------------------------------

        private void DrawFooter()
        {
            GUILayout.Box(GUIContent.none, _sRule, GUILayout.ExpandWidth(true));

            GUILayout.BeginHorizontal();
            bool auto = GUILayout.Toggle(_checkOnStart.Value, "Auto-update on start", _sToggle);
            if (auto != _checkOnStart.Value) _checkOnStart.Value = auto;
            GUILayout.Space(14);
            bool notify = GUILayout.Toggle(_notifyOnJoin.Value, "Notify about updates", _sToggle);
            if (notify != _notifyOnJoin.Value) _notifyOnJoin.Value = notify;
            GUILayout.Space(14);
            bool gh = GUILayout.Toggle(_allowGitHubCli.Value, "Allow GitHub CLI login", _sToggle);
            if (gh != _allowGitHubCli.Value) _allowGitHubCli.Value = gh;
            if (_developerMode.Value)
            {
                GUILayout.Space(14);
                bool rebuilt = GUILayout.Toggle(_autoReload.Value, "Auto-reload rebuilt mods", _sToggle);
                if (rebuilt != _autoReload.Value) _autoReload.Value = rebuilt;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Mod Manager v{Version}   |   {_owner.Value}/{_repo.Value}   |   signed in via {AuthSource}   |   {_hotkey.Value} opens/closes this window", _sDim);
            GUILayout.FlexibleSpace();
            if (Button("Reload all mods", 130, false, true, true)) Defer(() => { if (!ReloadScripts()) _statusLine = "ScriptEngine not found: press F6 instead."; });
            if (Button("Open mods folder", 130, false, true, true)) Defer(() => System.Diagnostics.Process.Start("explorer.exe", _scriptsDir));
            GUILayout.EndHorizontal();
        }

        // ---- widgets ---------------------------------------------------------------------------

        private void Defer(Action action) => _deferred = action;

        private bool Button(string text, float width, bool primary = false, bool enabled = true, bool small = false)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            GUIStyle style = primary ? _sBtnPrimary : small ? _sBtnSmall : _sBtn;
            bool clicked = GUILayout.Button(text, style, GUILayout.Width(width), GUILayout.Height(small ? 26 : 30));
            GUI.enabled = previous;
            return clicked;
        }

        private void Pill(string text, Color background, float width)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = background;
            GUILayout.Label(text, _sPill, GUILayout.Width(width), GUILayout.Height(24));
            GUI.backgroundColor = previous;
        }
    }
}
