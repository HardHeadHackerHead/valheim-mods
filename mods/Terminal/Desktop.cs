using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static DHack.Shared.Translations;

namespace Terminal
{
    /// <summary>
    /// What the monitor shows when the running game isn't in front: the boot screen, the desktop (an icon for every app in Apps.cs and a
    /// taskbar with the running apps and the game's clock), the loading screen, and the windows of apps that have one (the browser). It is
    /// one small UGUI scene, drawn by its own camera into whichever terminal's RenderTexture is asked for, so every player near a terminal
    /// sees the same screen (the state and the cursor are in the terminal's saved data). The driver's mouse moves a software cursor (the
    /// game's own mouse look is busy turning a seated player) and the attack button clicks: double-click an icon to start it.
    ///
    /// The scene lives far below the world at (0, -9000, 0) on the UI layer with a camera that only sees that layer, so nothing else can
    /// draw it and it draws nothing else. Everything is laid out in a 1280x720 space (CanvasScaler), whatever the texture's size.
    /// </summary>
    internal static class Desktop
    {
        private const float W = 1280f, H = 720f;
        private const float IconSize = 96f, IconGap = 132f, BarHeight = 44f, TaskWidth = 200f, TaskGap = 8f;
        private const int MaxTasks = 5;

        private static GameObject _root;
        private static Camera _camera;
        private static TMP_FontAsset _font;
        internal static TMP_FontAsset Font => _font;
        private static Texture2D _wallTexture, _cursorTexture;

        private static GameObject _boot, _loading, _desk, _dialog, _bar;
        private static RectTransform _bootBar, _loadBar, _cursor;
        private static TextMeshProUGUI _clock, _loadTitle, _loadText, _crashTitle;
        private static readonly List<IconView> Icons = new List<IconView>();
        private static readonly List<TaskView> Tasks = new List<TaskView>();
        private static readonly Dictionary<string, GameObject> Windows = new Dictionary<string, GameObject>();

        private sealed class IconView { public App App; public Image Bg; }
        private sealed class TaskView { public GameObject Root; public Image Bg; public TextMeshProUGUI Label; }

        // double-click: the app and the time of the last click on an icon
        private static string _lastApp;
        private static float _lastClick;

        // a click in an app's window waits for the next picture, so UI hit tests (links in text) are done with the camera as it draws
        private static bool _hasClick;
        private static Vector2 _click;

        // ---- drawing ----

        /// <summary>Draw the terminal's desktop (or boot or loading screen, or a window) into the texture.</summary>
        internal static bool Draw(TerminalPiece t, RenderTexture target, bool driver)
        {
            if (target == null || !Ensure()) return false;
            TerminalPiece.Mode mode = t.State;
            App front = mode == TerminalPiece.Mode.App ? Apps.Get(t.RunningApp) : null;
            bool window = front != null && front.HasWindow && Windows.ContainsKey(front.Id);
            if (mode == TerminalPiece.Mode.App && !window) return false;
            if (mode == TerminalPiece.Mode.Off) return false;

            _boot.SetActive(mode == TerminalPiece.Mode.Booting);
            _loading.SetActive(mode == TerminalPiece.Mode.Loading);
            bool desktop = mode == TerminalPiece.Mode.Desktop;
            _desk.SetActive(desktop);
            string crashed = desktop ? t.CrashedApp : "";
            _dialog.SetActive(crashed.Length > 0);
            foreach (KeyValuePair<string, GameObject> w in Windows) w.Value.SetActive(window && w.Key == front.Id);
            _bar.SetActive(desktop || window);

            float progress = t.Progress;
            if (mode == TerminalPiece.Mode.Booting) _bootBar.sizeDelta = new Vector2(360f * progress, 8f);
            if (mode == TerminalPiece.Mode.Loading)
            {
                App app = Apps.Get(t.RunningApp);
                _loadTitle.text = app != null ? Localization.instance.Localize(app.Title).ToUpperInvariant() : "";
                _loadBar.sizeDelta = new Vector2(560f * progress, 14f);
                _loadText.text = T("terminal_loading");
            }
            Vector2 cursor = Drive.CursorFor(t);   // (the sitter's own cursor is drawn from their mouse at once, not from the slower shared copy)
            float cx = cursor.x * W, cy = cursor.y * H;
            if (desktop)
            {
                for (int i = 0; i < Icons.Count; i++)
                {
                    IconView icon = Icons[i];
                    bool over = IconRect(i).Contains(new Vector2(cx, cy));
                    icon.Bg.color = new Color(icon.App.Tint.r, icon.App.Tint.g, icon.App.Tint.b, over ? 1f : 0.8f);
                }
                if (crashed.Length > 0)
                {
                    App gone = Apps.Get(crashed);
                    _crashTitle.text = T("terminal_crash_title", gone != null ? Localization.instance.Localize(gone.Title) : crashed);
                }
            }
            if (desktop || window) UpdateTaskbar(t, front);
            if (window) front.UpdateWindow(t, driver);
            _cursor.anchoredPosition = new Vector2(cx, -cy);
            _cursor.gameObject.SetActive(desktop || (window && front.WantsCursor));

            _camera.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            if (window && _hasClick)
            {
                _hasClick = false;
                front.Click(t, _click, _camera, target);
                Canvas.ForceUpdateCanvases();
            }
            _camera.Render();
            _camera.targetTexture = null;
            return true;
        }

        private static void UpdateTaskbar(TerminalPiece t, App front)
        {
            List<string> running = t.RunningApps;
            for (int i = 0; i < Tasks.Count; i++)
            {
                TaskView task = Tasks[i];
                App app = i < running.Count ? Apps.Get(running[i]) : null;
                task.Root.SetActive(app != null);
                if (app == null) continue;
                task.Label.text = Localization.instance.Localize(app.Title);
                task.Bg.color = front != null && front.Id == app.Id ? new Color(0.3f, 0.55f, 0.62f, 1f) : new Color(0.2f, 0.32f, 0.37f, 0.9f);
            }
            _clock.text = Clock();
        }

        private static string Clock()
        {
            if (EnvMan.instance == null) return "--:--";
            float hours = EnvMan.instance.GetDayFraction() * 24f;
            return string.Format("{0:00}:{1:00}", Mathf.FloorToInt(hours) % 24, Mathf.FloorToInt((hours % 1f) * 60f));
        }

        // ---- the driver's mouse ----

        private static Rect IconRect(int index) => new Rect(36f, 36f + index * IconGap, IconSize, IconSize + 28f);
        private static Rect TaskRect(int index) => new Rect(150f + index * (TaskWidth + TaskGap), H - BarHeight + 4f, TaskWidth, BarHeight - 8f);
        private static Rect OkRect => new Rect(560f, 392f, 160f, 48f);

        /// <summary>A click at a place on the screen (0 to 1 across, 0 to 1 down; the cursor).</summary>
        internal static void Click(TerminalPiece t, Vector2 at)
        {
            var p = new Vector2(at.x * W, at.y * H);
            TerminalPiece.Mode mode = t.State;
            if (mode != TerminalPiece.Mode.Desktop && mode != TerminalPiece.Mode.App) return;
            App front = mode == TerminalPiece.Mode.App ? Apps.Get(t.RunningApp) : null;
            if (mode == TerminalPiece.Mode.App && (front == null || !front.HasWindow)) return;

            // the taskbar answers in both places
            List<string> running = t.RunningApps;
            for (int i = 0; i < running.Count && i < MaxTasks; i++)
                if (TaskRect(i).Contains(p)) { t.Bring(running[i]); return; }

            if (mode == TerminalPiece.Mode.App) { _hasClick = true; _click = at; return; }   // (the window's own: with the next picture)

            if (t.CrashedApp.Length > 0)
            {
                if (OkRect.Contains(p)) t.ClearCrash();
                return;
            }
            for (int i = 0; i < Apps.All.Count; i++)
            {
                if (!IconRect(i).Contains(p)) continue;
                App app = Apps.All[i];
                if (_lastApp == app.Id && Time.unscaledTime - _lastClick < 0.7f) { _lastApp = null; t.Launch(app); }
                else { _lastApp = app.Id; _lastClick = Time.unscaledTime; }
                return;
            }
        }

        // ---- building the scene (once) ----

        private static bool Ensure()
        {
            if (_root != null && Icons.Count == Apps.All.Count) return true;
            Clear();
            _font = FindFont();
            if (_font == null) { Plugin.Log.LogWarning("No UI font found for the desktop"); return false; }
            int ui = LayerMask.NameToLayer("UI");
            _root = new GameObject("TerminalDesktop") { layer = ui };
            Object.DontDestroyOnLoad(_root);
            _root.transform.position = new Vector3(0f, -9000f, 0f);

            _camera = _root.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = 1 << ui;
            _camera.nearClipPlane = 0.1f; _camera.farClipPlane = 10f;
            _camera.allowHDR = false; _camera.allowMSAA = false;
            _camera.enabled = false;

            var canvasObject = new GameObject("Canvas") { layer = ui };
            canvasObject.transform.SetParent(_root.transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _camera;
            canvas.planeDistance = 1f;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(W, H);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = canvasObject.transform;

            // the desktop: wallpaper and icons
            _desk = Group("Desktop", root);
            _wallTexture = Gradient(new Color(0.05f, 0.12f, 0.2f), new Color(0.14f, 0.34f, 0.38f));
            Raw("Wallpaper", _desk.transform, 0f, 0f, W, H, _wallTexture);
            Label("Brand", _desk.transform, "Quad's OS", 28, new Color(1f, 1f, 1f, 0.35f), TextAlignmentOptions.TopRight, W - 340f, 24f, 320f, 40f);
            Label("Hint", _desk.transform, T("terminal_desktop_hint"), 20, new Color(1f, 1f, 1f, 0.55f), TextAlignmentOptions.TopRight, W - 640f, 70f, 620f, 30f);
            for (int i = 0; i < Apps.All.Count; i++)
            {
                App app = Apps.All[i];
                Rect r = IconRect(i);
                var icon = new IconView { App = app };
                icon.Bg = Box("Icon " + app.Id, _desk.transform, r.x, r.y, IconSize, IconSize, app.Tint);
                Label("Glyph", icon.Bg.transform, app.Glyph, 64, Color.white, TextAlignmentOptions.Center, 0f, 0f, IconSize, IconSize);
                Label("Title", _desk.transform, Localization.instance.Localize(app.Title), 22, Color.white, TextAlignmentOptions.Top, r.x - 20f, r.y + IconSize + 4f, IconSize + 40f, 28f);
                Icons.Add(icon);
            }

            // the windows of apps that have one
            foreach (App app in Apps.All)
            {
                if (!app.HasWindow) continue;
                GameObject window = Group("Window " + app.Id, root);
                app.BuildWindow(window.transform);
                window.SetActive(false);
                Windows[app.Id] = window;
            }

            // the taskbar (over the desktop and the windows)
            _bar = Group("Taskbar", root);
            Box("Bar", _bar.transform, 0f, H - BarHeight, W, BarHeight, new Color(0.06f, 0.08f, 0.1f, 0.97f));
            Box("Start", _bar.transform, 8f, H - BarHeight + 4f, 130f, BarHeight - 8f, new Color(0.2f, 0.45f, 0.5f, 1f));
            Label("StartText", _bar.transform, "Quad's OS", 22, Color.white, TextAlignmentOptions.Center, 8f, H - BarHeight + 4f, 130f, BarHeight - 8f);
            for (int i = 0; i < MaxTasks; i++)
            {
                Rect r = TaskRect(i);
                var task = new TaskView { Root = Group("Task " + i, _bar.transform) };
                task.Bg = Box("Bg", task.Root.transform, r.x, r.y, r.width, r.height, new Color(0.2f, 0.32f, 0.37f, 0.9f));
                task.Label = Label("Text", task.Root.transform, "", 22, Color.white, TextAlignmentOptions.Center, r.x, r.y, r.width, r.height);
                Tasks.Add(task);
            }
            _clock = Label("Clock", _bar.transform, "", 24, Color.white, TextAlignmentOptions.Right, W - 140f, H - BarHeight + 4f, 124f, BarHeight - 8f);

            // a program that crashed
            _dialog = Group("Dialog", root);
            Box("Shade", _dialog.transform, 0f, 0f, W, H - BarHeight, new Color(0f, 0f, 0f, 0.45f));
            Box("Window", _dialog.transform, 380f, 250f, 520f, 210f, new Color(0.11f, 0.13f, 0.16f, 1f));
            Box("Title bar", _dialog.transform, 380f, 250f, 520f, 40f, new Color(0.4f, 0.1f, 0.1f, 1f));
            _crashTitle = Label("Title", _dialog.transform, "", 24, Color.white, TextAlignmentOptions.Left, 396f, 252f, 490f, 36f);
            Label("Text", _dialog.transform, T("terminal_crash_text"), 22, new Color(0.78f, 0.8f, 0.83f), TextAlignmentOptions.Center, 390f, 310f, 500f, 60f);
            Box("OK", _dialog.transform, OkRect.x, OkRect.y, OkRect.width, OkRect.height, new Color(0.3f, 0.5f, 0.55f, 1f));
            Label("OKText", _dialog.transform, T("terminal_ok"), 24, Color.white, TextAlignmentOptions.Center, OkRect.x, OkRect.y, OkRect.width, OkRect.height);

            // the boot screen
            _boot = Group("Boot", root);
            Box("Black", _boot.transform, 0f, 0f, W, H, Color.black);
            Label("Brand", _boot.transform, "Quad's OS", 64, Color.white, TextAlignmentOptions.Center, 0f, 250f, W, 90f);
            Box("Track", _boot.transform, 460f, 380f, 360f, 8f, new Color(1f, 1f, 1f, 0.15f));
            _bootBar = Box("Bar", _boot.transform, 460f, 380f, 0f, 8f, new Color(0.35f, 0.8f, 0.85f, 1f)).rectTransform;
            Label("Starting", _boot.transform, T("terminal_starting"), 22, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Center, 0f, 410f, W, 30f);

            // the loading screen (an app's name and a bar)
            _loading = Group("Loading", root);
            Raw("Back", _loading.transform, 0f, 0f, W, H, Gradient(new Color(0.03f, 0.05f, 0.08f), new Color(0.1f, 0.2f, 0.24f)));
            _loadTitle = Label("AppName", _loading.transform, "", 110, Color.white, TextAlignmentOptions.Center, 0f, 220f, W, 150f);
            Box("Track", _loading.transform, 360f, 420f, 560f, 14f, new Color(1f, 1f, 1f, 0.15f));
            _loadBar = Box("Bar", _loading.transform, 360f, 420f, 0f, 14f, new Color(0.9f, 0.75f, 0.35f, 1f)).rectTransform;
            _loadText = Label("Text", _loading.transform, "", 24, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Center, 0f, 450f, W, 36f);

            // the cursor, on top of everything
            _cursorTexture = Arrow();
            RawImage cursor = Raw("Cursor", root, 0f, 0f, 24f, 36f, _cursorTexture);
            _cursor = cursor.rectTransform;
            return true;
        }

        internal static void Clear()
        {
            foreach (App app in Apps.All) app.Teardown();
            if (_root != null) Object.Destroy(_root);
            if (_wallTexture != null) Object.Destroy(_wallTexture);
            if (_cursorTexture != null) Object.Destroy(_cursorTexture);
            _root = null; _camera = null; _wallTexture = _cursorTexture = null;
            _boot = _loading = _desk = _dialog = _bar = null;
            Icons.Clear(); Tasks.Clear(); Windows.Clear();
            _hasClick = false;
        }

        // ---- small builders (everything is placed from the top left, in the 1280x720 space; apps' windows use them too) ----

        internal static GameObject Group(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return go;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        internal static Image Box(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        internal static RawImage Raw(string name, Transform parent, float x, float y, float w, float h, Texture texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var raw = go.GetComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;
            return raw;
        }

        internal static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            return label;
        }

        private static TMP_FontAsset FindFont()
        {
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            TMP_FontAsset pick = fonts.FirstOrDefault(f => f.name.IndexOf("Averia", System.StringComparison.OrdinalIgnoreCase) >= 0 && f.name.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? fonts.FirstOrDefault(f => f.name.IndexOf("Averia", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? fonts.FirstOrDefault();
            Plugin.Log.LogInfo("Desktop font: " + (pick != null ? pick.name : "none"));
            return pick;
        }

        private static Texture2D Gradient(Color top, Color bottom)
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TerminalGradient" };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            tex.Apply();
            return tex;
        }

        /// <summary>The mouse arrow: a white triangle with a black outline, tip at the top left.</summary>
        private static Texture2D Arrow()
        {
            const int w = 16, h = 24;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "TerminalCursor" };
            var a = new Vector2(1f, h - 1f); var b = new Vector2(1f, 5f); var c = new Vector2(12f, 12f);
            bool Inside(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return false;
                var p = new Vector2(x, y);
                float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool inside = Inside(x, y);
                    bool edge = inside && (!Inside(x - 1, y) || !Inside(x + 1, y) || !Inside(x, y - 1) || !Inside(x, y + 1));
                    tex.SetPixel(x, y, inside ? (edge ? Color.black : Color.white) : Color.clear);
                }
            tex.Apply();
            return tex;
        }

        private static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
