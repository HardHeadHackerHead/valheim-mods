using System.Collections.Generic;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Terminal
{
    /// <summary>
    /// What a placed terminal does. It is a small computer: it starts off, boots when somebody sits down at it, shows a desktop, and runs
    /// an app (Apps.cs) that the sitter starts. Its whole state is in its saved data, so every player near it sees the same screen:
    ///
    ///   Off -> Booting (when somebody sits) -> Desktop -> Loading (an app is started) -> App (in front) -> Desktop (Escape: the app keeps
    ///   running in the background) -> App (its button on the taskbar). A crashed app (Valheim: its Proxy died) leaves a message on the desktop.
    ///
    /// The game that owns the terminal's saved data (the sitter's, since sitting claims it) moves the state on when a timer runs out
    /// (Advance), and draws the picture: the desktop (Desktop.cs) or the running app's (App.Draw), at a low rate and size for onlookers and
    /// a bigger, faster one for the sitter. Each terminal has its own Proxy, found by the id kept here (never a ZDOID: those change at
    /// every world load). Fields are read in Awake (not set on the prefab), so a hot reload's Reattach gives a standing terminal new code.
    /// </summary>
    internal class TerminalPiece : MonoBehaviour, Hoverable, Interactable
    {
        internal enum Mode { Off = 0, Booting = 1, Desktop = 2, Loading = 3, App = 4 }

        internal static readonly List<TerminalPiece> All = new List<TerminalPiece>();
        internal const string KeyId = "dh_tm_term", KeyProxy = "dh_tm_proxy", KeyRespawn = "dh_tm_respawn",
            KeyState = "dh_tm_state", KeyApp = "dh_tm_app", KeySince = "dh_tm_since", KeyUntil = "dh_tm_until",
            KeyCursor = "dh_tm_cursor", KeyCrash = "dh_tm_crash", KeyUser = "dh_tm_user", KeyUserBeat = "dh_tm_ubeat",
            KeyApps = "dh_tm_apps", KeyUrl = "dh_tm_url", KeyPage = "dh_tm_pg", KeyAppText = "dh_tm_atext", KeyAppNum = "dh_tm_anum";
        private const float UseDistance = 3.2f, BootSeconds = 2.5f;

        internal ZNetView Nview;
        internal Transform Seat, ViewPoint, SpawnPoint;
        private Renderer _screen;
        private RenderTexture _rt;
        private float _nextDraw, _farSince = -1f, _nextFind, _nextCursorWrite;
        private ProxyBody _proxy;
        private int _lastCursor = -1;

        internal string Id => Nview != null && Nview.GetZDO() != null ? Nview.GetZDO().GetString(KeyId, "") : "";

        private void Awake()
        {
            Nview = GetComponent<ZNetView>();
            if (Nview == null || Nview.GetZDO() == null) return;   // (the build ghost, or a copy that isn't a real object)
            Seat = transform.Find("seat");
            ViewPoint = transform.Find("view");
            SpawnPoint = transform.Find("spawn");
            Transform screen = transform.Find("Model/screen");
            if (screen != null) _screen = screen.GetComponent<Renderer>();
            All.Add(this);
            if (Nview.IsOwner() && Id.Length == 0) Nview.GetZDO().Set(KeyId, System.Guid.NewGuid().ToString("N").Substring(0, 12));
        }

        private void OnDestroy()
        {
            All.Remove(this);
            Release();
            if (Drive.Current == this) Drive.End("destroyed");
        }

        // ---- the computer's state (every game reads it from the saved data; the owner writes it) ----

        private ZDO Zdo => Nview != null ? Nview.GetZDO() : null;
        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0;

        internal Mode State => Zdo != null ? (Mode)Zdo.GetInt(KeyState, 0) : Mode.Off;
        internal string RunningApp => Zdo != null ? Zdo.GetString(KeyApp, "") : "";
        /// <summary>The apps that are running (one has the front, the rest are in the background): ids kept as a comma list in the saved data.</summary>
        internal List<string> RunningApps
        {
            get
            {
                var list = new List<string>();
                if (Zdo == null) return list;
                string text = Zdo.GetString(KeyApps, null);
                if (text == null) { string legacy = RunningApp; if (legacy.Length > 0 && State != Mode.Loading) list.Add(legacy); return list; }
                foreach (string id in text.Split(',')) if (id.Length > 0) list.Add(id);
                return list;
            }
        }

        internal bool IsRunning(string id) => RunningApps.Contains(id);

        private void SetRunning(List<string> list) { if (Own()) Zdo.Set(KeyApps, string.Join(",", list.ToArray())); }

        // the browser's page, shared by everyone near (an address and a page number)
        internal string BrowserUrl => Zdo != null ? Zdo.GetString(KeyUrl, "") : "";
        internal int BrowserPage => Zdo != null ? Zdo.GetInt(KeyPage, 0) : 0;
        internal void SetBrowser(string url, int page) { if (Own()) { Zdo.Set(KeyUrl, url ?? ""); Zdo.Set(KeyPage, page); } }

        // what the Scripts program has picked and which page of it is shown (every player near sees the same)
        internal string AppText => Zdo != null ? Zdo.GetString(KeyAppText, "") : "";
        internal int AppNumber => Zdo != null ? Zdo.GetInt(KeyAppNum, 0) : 0;
        internal void SetAppText(string text) { if (Own()) Zdo.Set(KeyAppText, text ?? ""); }
        internal void SetAppNumber(int n) { if (Own()) Zdo.Set(KeyAppNum, n); }

        internal string CrashedApp => Zdo != null ? Zdo.GetString(KeyCrash, "") : "";

        /// <summary>How far through the boot or loading screen it is, 0 to 1.</summary>
        internal float Progress
        {
            get
            {
                if (Zdo == null) return 0f;
                double since = Zdo.GetLong(KeySince, 0L) / 1000.0, until = Zdo.GetLong(KeyUntil, 0L) / 1000.0;
                return until <= since ? 1f : Mathf.Clamp01((float)((Now - since) / (until - since)));
            }
        }

        /// <summary>The sitter's cursor on the desktop: 0 to 1 across and 0 to 1 down.</summary>
        internal Vector2 Cursor
        {
            get
            {
                int packed = Zdo != null ? Zdo.GetInt(KeyCursor, 500 * 1001 + 300) : 500 * 1001 + 300;
                return new Vector2((packed / 1001) / 1000f, (packed % 1001) / 1000f);
            }
        }

        private bool Own()
        {
            if (Zdo == null) return false;
            if (!Nview.IsOwner()) Nview.ClaimOwnership();
            return true;
        }

        private void Set(Mode mode, string app, float seconds)
        {
            if (!Own()) return;
            double now = Now;
            Zdo.Set(KeyState, (int)mode);
            Zdo.Set(KeyApp, app ?? "");
            Zdo.Set(KeySince, (long)(now * 1000.0));
            Zdo.Set(KeyUntil, (long)((now + seconds) * 1000.0));
        }

        /// <summary>Somebody sat down at a terminal that was off: it starts up.</summary>
        internal void PowerOn()
        {
            if (State == Mode.Off) Set(Mode.Booting, "", BootSeconds);
        }

        /// <summary>The sitter started an app: its loading screen, unless it is already running (then it just comes to the front).</summary>
        internal void Launch(App app)
        {
            if (app == null || State != Mode.Desktop) return;
            if (IsRunning(app.Id)) { Bring(app.Id); return; }
            if (Own()) Zdo.Set(KeyCrash, "");
            float seconds = app.LoadSeconds(this);
            if (seconds <= 0f) { var now = RunningApps; now.Add(app.Id); SetRunning(now); Set(Mode.App, app.Id, 0f); return; }
            Set(Mode.Loading, app.Id, seconds);
        }

        /// <summary>An app that is running (in the background) comes to the front.</summary>
        internal void Bring(string appId)
        {
            if ((State == Mode.Desktop || State == Mode.App) && IsRunning(appId)) Set(Mode.App, appId, 0f);
        }

        /// <summary>The app is shut: gone from the taskbar. (Valheim can't be: its Proxy stands in the world.)</summary>
        internal void CloseApp(string appId)
        {
            if (!Own()) return;
            var now = RunningApps;
            now.Remove(appId);
            SetRunning(now);
            Set(Mode.Desktop, "", 0f);
        }

        /// <summary>Escape: the app in front goes to the background; the desktop shows.</summary>
        internal void ToDesktop()
        {
            if (State == Mode.App) Set(Mode.Desktop, RunningApp, 0f);   // (the app stays in the list: running in the background)
        }

        internal void ClearCrash() { if (Own()) Zdo.Set(KeyCrash, ""); }

        /// <summary>The app stopped by itself (its Proxy died): the desktop, with a message.</summary>
        internal void Crashed(string appId)
        {
            if (!Own()) return;
            var now = RunningApps;
            now.Remove(appId);
            SetRunning(now);
            Set(Mode.Desktop, "", 0f);
            Zdo.Set(KeyCrash, appId);
        }

        /// <summary>The owner moves the state on when a timer runs out.</summary>
        private void Advance()
        {
            if (Zdo == null || !Nview.IsOwner()) return;
            Mode mode = State;
            if (mode != Mode.Booting && mode != Mode.Loading) return;
            if (Progress < 1f) return;
            if (mode == Mode.Booting) { Set(Mode.Desktop, "", 0f); return; }
            App app = Apps.Get(RunningApp);
            var running = RunningApps;
            if (!running.Contains(RunningApp)) { running.Add(RunningApp); SetRunning(running); }
            Set(Mode.App, RunningApp, 0f);
            app?.Loaded(this);
        }

        // ---- who is sitting ----

        internal string User => Zdo != null ? Zdo.GetString(KeyUser, "") : "";

        /// <summary>Somebody other than this player is sitting here (their beat is fresh: a sitter who vanished frees the seat after a few seconds).</summary>
        internal bool BusyFor(string me) => User.Length > 0 && User != me && Zdo != null && Now - Zdo.GetFloat(KeyUserBeat, -100f) < 4.0;

        internal void SetUser(string name)
        {
            if (!Own()) return;
            Zdo.Set(KeyUser, name ?? "");
            Zdo.Set(KeyUserBeat, (float)Now);
        }

        internal void BeatUser() { if (Own()) Zdo.Set(KeyUserBeat, (float)Now); }

        /// <summary>The sitter moved the cursor (a small number, and not on every frame).</summary>
        internal void SetCursor(Vector2 at)
        {
            if (Time.time < _nextCursorWrite || !Own()) return;
            int packed = Mathf.RoundToInt(Mathf.Clamp01(at.x) * 1000f) * 1001 + Mathf.RoundToInt(Mathf.Clamp01(at.y) * 1000f);
            if (packed == _lastCursor) return;
            _nextCursorWrite = Time.time + 0.1f;
            _lastCursor = packed;
            Zdo.Set(KeyCursor, packed);
        }

        // ---- the Proxy ----

        /// <summary>This terminal's Proxy if it is loaded here (it stays within the tether, which is inside the loaded area), else null.</summary>
        internal ProxyBody FindProxy()
        {
            if (_proxy != null && _proxy.View != null && _proxy.View.GetZDO() != null) return _proxy;
            if (Time.time < _nextFind || Zdo == null) return null;
            _nextFind = Time.time + 0.5f;
            string id = Zdo.GetString(KeyProxy, "");
            if (id.Length == 0) return null;
            foreach (ProxyBody p in ProxyBody.All)
                if (p.Id == id) return _proxy = p;
            return null;
        }

        /// <summary>A new Proxy beside the terminal (the Valheim app's loading screen finished), owned by this game and remembered here.</summary>
        internal ProxyBody SpawnProxy()
        {
            if (!Own() || SpawnPoint == null) return null;
            string id = System.Guid.NewGuid().ToString("N").Substring(0, 12);
            Quaternion facing = Quaternion.LookRotation(transform.forward);   // (away from the stool)
            ProxyBody proxy = ProxyBody.Spawn(SpawnPoint.position, facing, id, Id);
            if (proxy == null) return null;
            Zdo.Set(KeyProxy, id);
            _proxy = proxy;
            Plugin.Log.LogInfo("A Proxy was made at a terminal");
            return proxy;
        }

        // ---- the screen ----

        private void Update() => Advance();

        private void LateUpdate()
        {
            Player player = Player.m_localPlayer;
            if (player == null || _screen == null) return;
            bool mine = Drive.Current == this;
            float range = Plugin.ViewerRange.Value;
            if (!mine && (player.transform.position - transform.position).sqrMagnitude > range * range)
            {
                if (_rt != null)
                {
                    if (_farSince < 0f) _farSince = Time.time;
                    else if (Time.time - _farSince > 5f) { Release(); Show(Model.Black); _farSince = -1f; }   // out of range for a while: the picture is let go
                }
                return;
            }
            _farSince = -1f;
            Mode mode = State;
            if (mode == Mode.Off) { Tint(1f); Show(Model.Black); return; }   // (the monitor is off)

            ProxyBody proxy = mode == Mode.App ? FindProxy() : null;
            bool picture = mode == Mode.App && Apps.Get(RunningApp) is ValheimApp;   // (the game's view; everything else is a screen the sitter points at, so it moves smoothly)
            float fps = mine ? (picture ? Plugin.DriverFps.Value : Mathf.Max(60f, Plugin.DriverFps.Value)) : (mode == Mode.App && proxy != null && !proxy.Driven ? 1f : Plugin.ViewerFps.Value);
            if (Time.unscaledTime < _nextDraw) return;
            int width = mine ? Plugin.DriverWidth.Value : Plugin.ViewerWidth.Value;
            Ensure(width, width * 9 / 16);
            bool drawn;
            if (mode == Mode.App)
            {
                App app = Apps.Get(RunningApp);
                drawn = app != null && app.Draw(this, _rt, mine);
                if (!drawn) drawn = Desktop.Draw(this, _rt, mine);   // (an app with a window of its own, or nothing to show yet)
            }
            else drawn = Desktop.Draw(this, _rt, mine);
            if (!drawn) return;
            _nextDraw = Time.unscaledTime + 1f / fps;
            Tint(picture ? 1f : Plugin.ScreenBrightness.Value);
            Show(_rt);
        }

        /// <summary>What the screen's material has (for the "terminal status" test command).</summary>
        internal string ScreenInfo()
        {
            if (_screen == null) return "no screen";
            Material m = _screen.material;
            Texture t = m.mainTexture;
            return m.shader.name + " | " + (t != null ? t.name + " " + t.width + "x" + t.height : "no texture") + " | rt=" + (_rt != null ? _rt.width + "x" + _rt.height : "none");
        }

        /// <summary>Saves what the screen shows now as a PNG (the "terminal eyeshot" test command). Null when nothing is drawn.</summary>
        internal string Snapshot(string path)
        {
            if (_rt == null) return null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _rt;
            var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            return path + " (" + _rt.width + "x" + _rt.height + ")";
        }

        private void Ensure(int width, int height)
        {
            if (_rt != null && _rt.width == width) return;
            Release();
            _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "TerminalScreen", antiAliasing = 1 };
            _rt.Create();
        }

        private void Release()
        {
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        /// <summary>
        /// How bright the screen glows. The game's own bloom turns bright text and panels into a haze at the close range a sitter has, so
        /// the desktop and the programs are dimmed (the game's view is left as it is).
        /// </summary>
        private void Tint(float level)
        {
            if (_screen == null) return;
            var color = new Color(level, level, level, 1f);
            if (_screen.material.color != color) _screen.material.color = color;
        }

        private void Show(Texture texture)
        {
            if (_screen != null && _screen.material.mainTexture != texture) _screen.material.mainTexture = texture;
        }

        // ---- using it ----

        public string GetHoverName() => "$terminal_piece";
        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            Player player = Player.m_localPlayer;
            if (player == null) return "";
            string name = Localization.instance.Localize("$terminal_piece");
            if ((player.transform.position - transform.position).magnitude > UseDistance) return Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
            string line;
            if (Drive.Current == this) line = Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] ") + T("terminal_stand");
            else if (BusyFor(player.GetPlayerName())) line = T("terminal_hover_busy", User);
            else line = Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] ") + T("terminal_hover_sit");
            return name + "\n" + line;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            Player player = user as Player;
            if (player == null || player != Player.m_localPlayer) return false;
            if ((player.transform.position - transform.position).magnitude > UseDistance) return false;
            Drive.Begin(player, this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>The Proxy died and has not come back yet.</summary>
        internal bool Recovering(out int secondsLeft)
        {
            secondsLeft = 0;
            if (Zdo == null || ZNet.instance == null) return false;
            long until = Zdo.GetLong(KeyRespawn, 0L);
            double left = until - Now;
            if (left <= 0) return false;
            secondsLeft = Mathf.CeilToInt((float)left);
            return true;
        }
    }
}
