using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// Which rooms of each dungeon this character has been in (the map's fog of war), kept with the character and never shared through the
    /// world: Player.m_customData["Dungeons.map.&lt;dungeon id&gt;"] = "&lt;pack&gt;|&lt;seed&gt;|&lt;rooms&gt;:&lt;'1' per room visited&gt;", saved
    /// with the character as the game saves it. A layout that changed since (a new version of its pack) starts again.
    /// </summary>
    internal static class MapMemory
    {
        private const string Prefix = "Dungeons.map.";
        private static Player _player;
        private static readonly Dictionary<string, bool[]> Known = new Dictionary<string, bool[]>();

        /// <summary>The rooms of this dungeon the local character has been in (one per room of its layout), or null when it can't be known.</summary>
        public static bool[] Visited(Site site)
        {
            Player p = Player.m_localPlayer;
            if (p == null || site?.Layout == null || string.IsNullOrEmpty(site.Id)) return null;
            if (p != _player) { Known.Clear(); _player = p; }   // (another character)
            if (Known.TryGetValue(site.Id, out bool[] rooms) && rooms.Length == site.Layout.Rooms.Count) return rooms;
            rooms = new bool[site.Layout.Rooms.Count];
            if (p.m_customData != null && p.m_customData.TryGetValue(Prefix + site.Id, out string saved) && saved != null)
            {
                int colon = saved.LastIndexOf(':');
                if (colon > 0 && saved.Substring(0, colon) == Fingerprint(site.Layout))
                    for (int i = 0; i < rooms.Length && colon + 1 + i < saved.Length; i++) rooms[i] = saved[colon + 1 + i] == '1';
            }
            Known[site.Id] = rooms;
            return rooms;
        }

        /// <summary>Marks a room visited and saves it with the character. True when it is new.</summary>
        public static bool Visit(Site site, int room)
        {
            bool[] rooms = Visited(site);
            if (rooms == null || room < 0 || room >= rooms.Length || rooms[room]) return false;
            rooms[room] = true;
            Player p = Player.m_localPlayer;
            if (p.m_customData == null) p.m_customData = new Dictionary<string, string>();
            var sb = new StringBuilder(Fingerprint(site.Layout)).Append(':');
            foreach (bool v in rooms) sb.Append(v ? '1' : '0');
            p.m_customData[Prefix + site.Id] = sb.ToString();
            return true;
        }

        private static string Fingerprint(Layout l) => $"{l.Pack?.Name}|{l.Seed}|{l.Rooms.Count}";

        public static void Clear() { Known.Clear(); _player = null; }
    }

    /// <summary>
    /// The dungeon map: M inside one of our dungeons opens it instead of the game's (whose world map is of no use 5000 m up). Floor by floor
    /// from above, opening on the player's floor; only the rooms this character has been in (MapMemory); every mechanism as it stands now
    /// (which way each turning stair, rotating room or bridge leads, which gates are open), never what to do with them. Drawn by MapPainter
    /// into a texture only when something it shows changed (a room visited, a mechanism, a door, the floor, the view); the players' arrows
    /// are moved every frame on top of it. Up/Down (or the wheel, or the d-pad) change floors; it opens fitted to the rooms seen on the floor shown, and fits
    /// again on each floor until the player zooms (+ and -, or the buttons) or drags it. M, Esc or Tab close it.
    /// </summary>
    internal static class DungeonMap
    {
        public const int Size = 1024;            // the picture, pixels square
        private const float Pad = 24f, ColumnW = 300f;
        private const float ZoomStep = 1.6f, Border = 40f;
        private static readonly MapMark[] Legend =
        {
            MapMark.You, MapMark.Other, MapMark.Lever, MapMark.Plate, MapMark.GateShut, MapMark.GateOpen, MapMark.Flight, MapMark.StairUp,
            MapMark.StairDown, MapMark.Drop, MapMark.Path, MapMark.Rotator, MapMark.Chest, MapMark.Shortcut, MapMark.ShortcutShut,
            MapMark.Boss, MapMark.BossDefeated, MapMark.Pedestal, MapMark.Brazier, MapMark.PushBlock, MapMark.Teleporter, MapMark.Checkpoint,
            MapMark.Vault, MapMark.Exit, MapMark.Unexplored, MapMark.Upper,
        };

        private static readonly Dictionary<Layout, MapLayout> Maps = new Dictionary<Layout, MapLayout>();
        private static readonly HashSet<string> Warned = new HashSet<string>();
        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly int PartOf = Keys.PartOf.GetStableHashCode();

        // the window
        private static RectTransform _window, _view, _marks;
        private static RawImage _image;
        private static MapSheet _sheet;          // the big map's picture (made the first time it opens)
        private static TMP_Text _floorText, _hereText;
        private static Button _up, _down, _closer, _farther;
        private static readonly List<RawImage> _arrows = new List<RawImage>();
        private static readonly List<TMP_Text> _names = new List<TMP_Text>();
        private static readonly Dictionary<MapMark, Texture2D> _icons = new Dictionary<MapMark, Texture2D>();

        // what it shows
        private static Site _site;
        private static MapScene _scene;
        private static bool _all;
        private static int _floor;
        private static float _ppm;               // the player's own view (zoomed or dragged): pixels a metre; 0: fitted to the floor shown
        private static Vector2 _centre;          // the player's own view's middle (dungeon frame x, z)
        private static float _openedAt, _closedAt = -10f, _nextLook, _nextScan, _nextVisit, _nextDrag, _nextWheel;
        private static int _closedFrame = -1;
        private static bool _dragging;
        private static Vector2 _dragFrom;
        private static readonly List<MapThing> _found = new List<MapThing>();
        private static string _recordsText;
        private static Dictionary<string, MechRecord> _records = new Dictionary<string, MechRecord>();

        internal static bool IsOpen => _window != null;

        /// <summary>The map's view of a layout (made once per layout).</summary>
        public static MapLayout MapOf(Layout layout)
        {
            if (!Maps.TryGetValue(layout, out MapLayout map)) Maps[layout] = map = new MapLayout(layout);
            return map;
        }

        // ---- every frame (from Dungeons.Update)

        public static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (p != null && !p.IsDead() && Time.unscaledTime >= _nextVisit)
            {
                _nextVisit = Time.unscaledTime + 0.25f;
                Visit(p);
            }
            DungeonMiniMap.Tick(p);
            if (_window == null) { if (_site != null) Close(); return; }   // (the game rebuilt its interface: a world change)
            bool again = Time.unscaledTime > _openedAt + 0.2f
                         && (ZInput.GetButtonDown("Map") || ZInput.GetButtonDown("JoyMap") || ZInput.GetButtonDown("JoyButtonB"));
            if (p == null || p.IsDead() || global::Menu.IsVisible() || again || _site == null || _site.Gone || _site.Layout == null
                || !Dungeons.Sites.Contains(_site) || (!_all && !_site.Contains(p.transform.position)))
            {
                Close();
                return;
            }
            ReadKeys(p);
            if (_sheet.Poll() && _image != null && !_image.enabled) _image.enabled = true;
            if (Time.unscaledTime >= _nextLook) Look(false);
            Arrows(p);
        }

        /// <summary>The room the local player stands in counts as visited (any dungeon of ours, built here).</summary>
        private static void Visit(Player p)
        {
            Vector3 at = p.transform.position;
            Site site = Dungeons.Containing(at);
            if (site?.Layout == null || !site.Ready || !site.HasOrigin) return;
            MapPainter.Prewarm(Size, Size, true);   // (the parchment, on a worker thread, before the first M)
            MapLayout map = MapOf(site.Layout);
            int room = map.RoomAt(at - site.Origin);
            if (room >= 0 && MapMemory.Visit(site, room) && _site == site) _nextLook = 0f;
        }

        private static void ReadKeys(Player p)
        {
            int floor = _floor;
            // the wheel: the game's own reading is held at 0 while a window is open (GameUi); one floor a notch, not a run of them from a trackpad
            float wheel = Time.unscaledTime >= _nextWheel ? Input.mouseScrollDelta.y : 0f;
            if (Mathf.Abs(wheel) > 0.1f) _nextWheel = Time.unscaledTime + 0.15f;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.PageUp) || ZInput.GetButtonDown("JoyDPadUp") || wheel > 0.1f) floor = Step(+1);
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.PageDown) || ZInput.GetButtonDown("JoyDPadDown") || wheel < -0.1f) floor = Step(-1);
            if (floor != _floor) { _floor = floor; Look(true); }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || ZInput.GetButtonDown("JoyDPadRight")) Zoom(+1, p);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || ZInput.GetButtonDown("JoyDPadLeft")) Zoom(-1, p);
            Drag();
        }

        /// <summary>The next floor up (+1) or down (-1) that has something to show, or the same.</summary>
        private static int Step(int way)
        {
            List<int> floors = Floors();
            int i = floors.IndexOf(_floor);
            if (i < 0) return floors.Count > 0 ? floors[0] : _floor;
            i = Mathf.Clamp(i + way, 0, floors.Count - 1);
            return floors[i];
        }

        /// <summary>The floors with a room to show, lowest first.</summary>
        private static List<int> Floors()
        {
            var list = new List<int>();
            if (_scene == null) return list;
            MapLayout map = _scene.Map;
            for (int f = 0; f < map.Floors; f++)
                for (int i = 0; i < map.Boxes.Length; i++)
                    if (_scene.Visible(i) && map.On(i, f)) { list.Add(f); break; }
            return list;
        }

        /// <summary>Closer (+1) or farther (-1), a step at a time: from the fitted view to at most MapScene.MaxPpm (a few rooms across),
        /// and back out to the fitted view, which then follows the floors again.</summary>
        private static void Zoom(int way, Player p)
        {
            if (_scene == null) return;
            float fit = FitPpm(), max = Mathf.Max(fit, MapScene.MaxPpm(Size, Size, Border));
            float now = _ppm > 0f ? _ppm : fit;
            float want = Mathf.Clamp(way > 0 ? now * ZoomStep : now / ZoomStep, fit, max);
            if (Mathf.Abs(want - now) < 0.001f) return;
            if (_ppm <= 0f)
            {
                // the first step closer: on the player if they're on this floor, else on the middle of what shows
                Vector3 me = p != null ? p.transform.position - _site.Origin : Vector3.zero;
                _centre = p != null && _scene.Map.FloorOf(me.y) == _floor ? new Vector2(me.x, me.z) : _scene.Centre(Size, Size);
            }
            _ppm = want <= fit * 1.01f ? 0f : want;   // (all the way out: fitted again)
            Look(true);
        }

        /// <summary>How close the fitted view of the floor shown is (pixels a metre).</summary>
        private static float FitPpm()
        {
            var probe = new MapScene { Map = _scene.Map, Shown = _scene.Shown };
            probe.Fit(Size, Size, Border, _floor);
            return probe.Ppm;
        }

        /// <summary>Dragging the picture with the mouse moves the view (it is the player's own from then on).</summary>
        private static void Drag()
        {
            if (_view == null || _scene == null) return;
            bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(_view, Input.mousePosition, null, out Vector2 local) && _view.rect.Contains(local);
            if (Input.GetMouseButtonDown(0) && inside) { _dragging = true; _dragFrom = local; }
            if (!Input.GetMouseButton(0)) _dragging = false;
            if (!_dragging || (local - _dragFrom).sqrMagnitude < 1f) return;
            if (_ppm <= 0f) { _ppm = _scene.Ppm; _centre = _scene.Centre(Size, Size); }   // (from the fitted view: held where it is)
            float k = Size / Mathf.Max(1f, _view.rect.width) / _ppm;   // metres per UI unit
            _centre -= (local - _dragFrom) * k;
            _dragFrom = local;
            if (Time.unscaledTime >= _nextDrag) { _nextDrag = Time.unscaledTime + 0.05f; Look(true); }
            else _nextLook = Mathf.Min(_nextLook, _nextDrag);
        }

        // ---- opening and closing

        /// <summary>The game's big map is about to open (M): inside one of our dungeons, ours opens instead and the game's stays shut (true).</summary>
        public static bool OpenInstead()
        {
            if (IsOpen) return true;
            if (Time.frameCount == _closedFrame || Time.unscaledTime < _closedAt + 0.15f)
            {
                // the M that closed ours this frame: the game's mustn't open on it (nor ours again)
                Player me = Player.m_localPlayer;
                return me != null && Dungeons.Containing(me.transform.position) != null;
            }
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || Game.m_noMap) return false;
            Site site = Dungeons.Containing(p.transform.position);
            if (site?.Layout == null || !site.HasOrigin || !site.Ready) return false;
            try { Open(site, -1, false); }
            catch (Exception e)
            {
                if (Warned.Add("open")) Plugin.Log.LogWarning("The dungeon map couldn't open (the game's opens instead): " + e);
                Close();
            }
            return IsOpen;
        }

        /// <summary>Opens the map of a dungeon on a floor (-1: the player's). all: every room, whatever this character has seen (a test).</summary>
        public static void Open(Site site, int floor, bool all)
        {
            if (!GameUi.Ready()) return;
            if (IsOpen) Close();
            GameUi.IsOpen = () => IsOpen;
            GameUi_EscapeCloses.Close = Close;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _sheet?.Forget();   // (a picture of the last time, still being drawn, isn't this one's)
            _site = site;
            _all = all;
            _ppm = 0f;
            _openedAt = Time.unscaledTime;
            _nextScan = 0f;
            _scene = Scene(site, all);
            Player p = Player.m_localPlayer;
            List<int> floors = Floors();
            int mine = p != null ? _scene.Map.FloorOf(p.transform.position.y - site.Origin.y) : 0;
            _floor = floor >= 0 ? floor : mine;
            if (!floors.Contains(_floor) && floors.Count > 0) _floor = floors.OrderBy(f => Math.Abs(f - _floor)).First();
            Build();
            Look(true);
        }

        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _view = null; _marks = null; _image = null;
            _floorText = _hereText = null; _up = _down = _closer = _farther = null;
            _arrows.Clear(); _names.Clear();
            _site = null; _scene = null; _dragging = false;
            _sheet?.Forget();
            _found.Clear();
            _closedAt = Time.unscaledTime;
            _closedFrame = Time.frameCount;
            UITooltip.HideTooltip();
        }

        /// <summary>Everything forgotten and removed (leaving the world, the mod unloading).</summary>
        public static void Clear()
        {
            Close();
            _sheet?.Destroy();
            _sheet = null;
            DungeonMiniMap.Clear();
            foreach (Texture2D t in _icons.Values) if (t != null) Object.Destroy(t);
            _icons.Clear();
            Maps.Clear();
            MapMemory.Clear();
            MapPainter.Forget();
            _records.Clear();
            _recordsText = null;
            _fights.Clear();
            _fightsText = null;
            GameUi.Destroy();
        }

        // ---- the window

        private static void Build()
        {
            Vector2 screen = GameUi.ScreenSize;
            float h = Mathf.Min(900f, screen.y - 40f), side = h - 2f * Pad;
            float w = side + ColumnW + 3f * Pad;
            if (w > screen.x - 40f) { w = screen.x - 40f; side = w - ColumnW - 3f * Pad; h = side + 2f * Pad; }
            _window = GameUi.Window("QuadDungeonsMap", new Vector2(w, h));

            // the map: the parchment picture in a sunken frame, the players' arrows over it
            RectTransform box = GameUi.Box(_window, new Vector2(Pad, -Pad), new Vector2(side, side));
            _view = GameUi.Stretch(box, "Map", -6f, -6f, -6f, -6f);
            _image = _view.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = true;   // (the mouse: dragging)
            if (_sheet == null) _sheet = new MapSheet(Size, "QuadDungeonsMap");
            _image.texture = _sheet.Texture;
            _image.enabled = false;   // (until its first picture is drawn: the texture still holds the last one)
            _marks = GameUi.Stretch(_view, "Marks");

            // the column: title, floor, buttons, legend, keys
            float x = 2f * Pad + side, y = Pad;
            GameUi.Line(_window, _site.Title, new Vector2(x, -y), ColumnW, 30f, GameUi.Warm, GameUi.Norse);
            y += 56f;
            GameUi.Braid(_window, new Vector2(x + ColumnW / 2f - w / 2f, -y), ColumnW);
            y += 14f;
            _floorText = GameUi.Line(_window, "", new Vector2(x, -y), ColumnW, 24f, GameUi.Gold);
            y += 34f;
            _hereText = GameUi.Line(_window, "", new Vector2(x, -y), ColumnW, 15f, GameUi.Dim);
            y += 34f;
            float bw = (ColumnW - 8f) / 2f;
            _up = GameUi.Button(_window, T("dungeons_map_up"), new Vector2(x, -y), new Vector2(bw, 38f), () => { _floor = Step(+1); Look(true); });
            _down = GameUi.Button(_window, T("dungeons_map_down"), new Vector2(x + bw + 8f, -y), new Vector2(bw, 38f), () => { _floor = Step(-1); Look(true); });
            y += 44f;
            _closer = GameUi.Button(_window, "+", new Vector2(x, -y), new Vector2(bw, 34f), () => Zoom(+1, Player.m_localPlayer), true, 22f);
            _farther = GameUi.Button(_window, "-", new Vector2(x + bw + 8f, -y), new Vector2(bw, 34f), () => Zoom(-1, Player.m_localPlayer), true, 22f);
            y += 48f;

            GameUi.Line(_window, T("dungeons_map_legend"), new Vector2(x, -y), ColumnW, 20f, GameUi.Warm, GameUi.Norse);
            y += 34f;
            float hint = 52f;
            // one column of rows, or two (narrower, cut with "...") when one would be cramped on a small screen
            float room = h - y - Pad - hint;
            int columns = room / Legend.Length >= 20f ? 1 : 2;
            int perColumn = (Legend.Length + columns - 1) / columns;
            float row = Mathf.Clamp(room / perColumn, 18f, 30f), colW = (ColumnW - (columns - 1) * 8f) / columns;
            float iconSize = row - 2f, top = y, font = columns > 1 ? 13f : 15f;
            for (int i = 0; i < Legend.Length; i++)
            {
                MapMark mark = Legend[i];
                float cx = x + (i / perColumn) * (colW + 8f), cy = top + (i % perColumn) * row;
                RawImage img = GameUi.At(_window, "Icon", cx, cy + (row - iconSize) / 2f, iconSize, iconSize).gameObject.AddComponent<RawImage>();
                img.texture = IconTexture(mark);
                img.raycastTarget = false;
                GameUi.Line(_window, T(Key(mark)), new Vector2(cx + iconSize + 6f, -cy - (row - font * 1.35f) / 2f), colW - iconSize - 6f, font, GameUi.Text);
            }
            string mapKey = Localization.instance != null ? Localization.instance.Localize("$KEY_Map") : "M";
            TMP_Text keys = GameUi.Label(_window, T("dungeons_map_keys", mapKey), new Vector2(x, -(h - Pad - hint)), new Vector2(ColumnW, hint), 13f, GameUi.Dim);
            keys.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>A marker's words in the legend (its translation key).</summary>
        private static string Key(MapMark mark)
        {
            switch (mark)
            {
                case MapMark.You: return "dungeons_map_you";
                case MapMark.Other: return "dungeons_map_others";
                case MapMark.Lever: return "dungeons_map_lever";
                case MapMark.Plate: return "dungeons_map_plate";
                case MapMark.GateShut: return "dungeons_map_gate_shut";
                case MapMark.GateOpen: return "dungeons_map_gate_open";
                case MapMark.StairUp: return "dungeons_map_stair_up";
                case MapMark.StairDown: return "dungeons_map_stair_down";
                case MapMark.Path: return "dungeons_map_path";
                case MapMark.Rotator: return "dungeons_map_rotator";
                case MapMark.Chest: return "dungeons_map_chest";
                case MapMark.Vault: return "dungeons_map_vault";
                case MapMark.Exit: return "dungeons_map_exit";
                case MapMark.Unexplored: return "dungeons_map_unexplored";
                case MapMark.Flight: return "dungeons_map_flight";
                case MapMark.Drop: return "dungeons_map_drop";
                case MapMark.Shortcut: return "dungeons_map_shortcut";
                case MapMark.ShortcutShut: return "dungeons_map_shortcut_shut";
                case MapMark.Boss: return "dungeons_map_boss";
                case MapMark.Pedestal: return "dungeons_map_pedestal";
                case MapMark.Brazier: return "dungeons_map_brazier";
                case MapMark.PushBlock: return "dungeons_map_pushblock";
                case MapMark.Teleporter: return "dungeons_map_teleporter";
                case MapMark.Checkpoint: return "dungeons_map_checkpoint";
                case MapMark.BossDefeated: return "dungeons_map_boss_defeated";
                default: return "dungeons_map_upper";
            }
        }

        /// <summary>A marker's picture on its own (the legend, the players' arrows), made once.</summary>
        internal static Texture2D IconTexture(MapMark mark)
        {
            if (_icons.TryGetValue(mark, out Texture2D t) && t != null) return t;
            const int n = 64;
            var c = new MapCanvas(n, n);
            c.Clear(new Color32(0, 0, 0, 0));
            if (mark != MapMark.You && mark != MapMark.Other) c.Disc(new Vector2(n / 2f, n / 2f), n * 0.48f, MapPainter.Paper, 0.9f);
            MapPainter.Icon(c, mark, new Vector2(n / 2f, n / 2f), mark == MapMark.You || mark == MapMark.Other ? n * 0.9f : n * 0.62f, Vector2.up);
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "QuadDungeonsMapIcon", wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(c.Px);
            t.Apply(false);
            _icons[mark] = t;
            return t;
        }

        // ---- what it shows

        /// <summary>A scene of a dungeon now: the rooms this character has been in (or all), every mechanism's state, the doors and chests
        /// found in the world. The view is set by Look.</summary>
        private static MapScene Scene(Site site, bool all)
        {
            var scene = new MapScene { Map = MapOf(site.Layout) };
            bool[] visited = all ? null : MapMemory.Visited(site);
            scene.Shown = visited ?? new bool[site.Layout.Rooms.Count];
            if (all) for (int i = 0; i < scene.Shown.Length; i++) scene.Shown[i] = true;
            States(site, scene);
            return scene;
        }

        /// <summary>Every mechanism's state now (where it is, or where it's going), read as the mechanisms read it: from the entrance's
        /// records and the server's clock (so a mechanism that turns by itself turns on the map too).</summary>
        internal static void States(Site site, MapScene scene)
        {
            string text = site.Zdo?.GetString(Keys.Mechs) ?? "";
            if (text != _recordsText) { _recordsText = text; _records = MechRecords.Parse(text); }
            double now = MechClock.Now;
            scene.States.Clear();
            scene.Recorded.Clear();
            scene.Recorded.UnionWith(_records.Keys);
            foreach (PlacedRoom r in site.Layout.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                {
                    string key = r.Index + "." + m.Id;
                    bool has = _records.TryGetValue(key, out MechRecord rec);
                    scene.States[key] = MechMotion.At(m, Math.Max(1, Layouts.States(m)), has, rec, now).To;
                }
            // the fights won (a boss defeated), and the secret rooms whose wall still stands taken out of what shows
            string fights = site.Zdo?.GetString(Keys.Encounters) ?? "";
            if (fights != _fightsText) { _fightsText = fights; _fights = EncounterRecord.Parse(fights); }
            scene.Cleared.Clear();
            foreach (KeyValuePair<string, EncounterRecord> kv in _fights)
                if (kv.Value.State == EncounterRecord.Cleared) scene.Cleared.Add(kv.Key);
            scene.HideSecrets();
            // the local player's checkpoint, if it's in this dungeon
            scene.MyCheckpoint = Checkpoints.Mine is Checkpoints.Point mine && mine.Dungeon == site.Id ? mine.Mech : null;
        }

        private static string _fightsText;
        private static Dictionary<string, EncounterRecord> _fights = new Dictionary<string, EncounterRecord>();

        /// <summary>The doors and chests this dungeon's builder made (marked as its own), as they are in the world now: a door open or shut,
        /// a chest where it stands (looted or not).</summary>
        internal static void Find(Site site, List<MapThing> found)
        {
            found.Clear();
            if (ZNetScene.instance == null || string.IsNullOrEmpty(site.Id) || !site.Box(out Bounds box)) return;
            box.Expand(8f);
            foreach (KeyValuePair<ZDO, ZNetView> kv in Instances(ZNetScene.instance))
            {
                ZNetView view = kv.Value;
                if (view == null || !box.Contains(view.transform.position) || kv.Key.GetString(PartOf) != site.Id) continue;
                Door door = view.GetComponent<Door>();
                if (door != null)
                {
                    float width = 2.5f;
                    Collider col = view.GetComponentInChildren<Collider>();
                    if (col != null) width = Mathf.Clamp(Mathf.Max(col.bounds.size.x, col.bounds.size.z), 1.5f, 6f);
                    found.Add(new MapThing { Mark = kv.Key.GetInt(ZDOVars.s_state) != 0 ? MapMark.GateOpen : MapMark.GateShut,
                                             At = view.transform.position - site.Origin, Forward = view.transform.forward, Width = width });
                }
                else if (view.GetComponent<Container>() != null && view.GetComponent<Character>() == null)
                    found.Add(new MapThing { Mark = MapMark.Chest, At = view.transform.position - site.Origin });
            }
        }

        /// <summary>Looks at what the map shows (a few times a second, or at once: now) and draws it again only when that changed.</summary>
        private static void Look(bool now)
        {
            if (_scene == null || _site == null || _sheet == null) return;
            _nextLook = Time.unscaledTime + 0.2f;
            if (_all)
            {
                States(_site, _scene);
                if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 1f; Find(_site, _found); }
                _scene.Things.Clear();
                _scene.Things.AddRange(_found);
            }
            else DungeonMiniMap.Fill(_site, _scene, _found, ref _nextScan);
            View(_scene, _floor, _ppm, _centre);
            Words();
            _sheet.Request(_scene, _floor, Signature(_site, _scene, _floor, _found), now);
        }

        /// <summary>What decides a picture: the floor, the view, the rooms shown, the states of their mechanisms, what was found.</summary>
        internal static string Signature(Site site, MapScene scene, int floor, List<MapThing> found)
        {
            var sb = new StringBuilder();
            sb.Append(floor).Append('|').Append(scene.Ppm.ToString("0.00")).Append(',').Append(scene.X0.ToString("0.0")).Append(',')
              .Append(scene.Z0.ToString("0.0")).Append('|');
            foreach (bool v in scene.Shown) sb.Append(v ? '1' : '0');
            sb.Append('|');
            foreach (PlacedRoom r in site.Layout.Rooms)
                if (scene.Visible(r.Index))
                    foreach (MechDef m in r.Def.Mechs)
                        if (m.Kind != "machine") sb.Append(scene.StateOf(r.Index, m));
            foreach (MapThing t in found) sb.Append('|').Append((int)t.Mark).Append(Mathf.RoundToInt(t.At.x)).Append(',').Append(Mathf.RoundToInt(t.At.z));
            foreach (string won in scene.Cleared) sb.Append("|w").Append(won);
            foreach (MapLayout.Breakable b in scene.Map.Breakables) sb.Append(scene.Standing(b) ? "|b1" : "|b0");
            foreach (MapLayout.Shortcut pair in scene.Map.Shortcuts) sb.Append(scene.Open(pair) ? "|s1" : "|s0");
            sb.Append("|c").Append(scene.MyCheckpoint);
            return sb.ToString();
        }

        internal static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key)) Plugin.Log.LogWarning(message);
        }

        /// <summary>The view: fitted to what this floor shows (so each floor fills the map, and fits again when the floor changes), or the
        /// player's own (ppm > 0: zoomed or dragged, kept across floors).</summary>
        private static void View(MapScene scene, int floor, float ppm, Vector2 centre)
        {
            if (ppm > 0f) scene.Look(ppm, centre, Size, Size);
            else scene.Fit(Size, Size, Border, floor);
        }

        /// <summary>The words and buttons that depend on the floor shown.</summary>
        private static void Words()
        {
            if (_floorText == null) return;
            List<int> floors = Floors();
            GameUi.SetText(_floorText, T("dungeons_map_floor", _floor + 1));
            Player p = Player.m_localPlayer;
            int mine = p != null ? _scene.Map.FloorOf(p.transform.position.y - _site.Origin.y) : -1;
            GameUi.SetText(_hereText, floors.Count == 0 ? T("dungeons_map_nothing") : mine == _floor ? T("dungeons_map_here") : T("dungeons_map_you_on", mine + 1));
            int i = floors.IndexOf(_floor);
            Enable(_up, i >= 0 && i < floors.Count - 1);
            Enable(_down, i > 0);
            Enable(_closer, (_ppm > 0f ? _ppm : _scene.Ppm) < MapScene.MaxPpm(Size, Size, Border) * 0.99f);
            Enable(_farther, _ppm > 0f);
        }

        private static void Enable(Button b, bool on)
        {
            if (b == null || b.interactable == on) return;
            b.interactable = on;
            TMP_Text label = GameUi.Label(b);
            if (label != null) label.color = on ? GameUi.Text : GameUi.Dim;
        }

        // ---- the players, every frame

        private static void Arrows(Player me)
        {
            if (_marks == null || _sheet?.ShownView == null) return;
            int n = 0;
            Camera cam = Utils.GetMainCamera();
            Vector3 look = cam != null ? cam.transform.forward : me.transform.forward;
            Arrow(n++, MapMark.You, me.transform.position, look, null);
            var seen = new HashSet<ZDOID>();
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == me) continue;
                seen.Add(other.GetZDOID());
                if (!_site.Contains(other.transform.position)) continue;
                Arrow(n++, MapMark.Other, other.transform.position, other.GetLookDir(), other.GetPlayerName());
            }
            // players this game hasn't loaded (far off in a big dungeon): where the server says they are, if they share it
            if (ZNet.instance != null)
                foreach (ZNet.PlayerInfo info in ZNet.instance.GetPlayerList())
                {
                    if (!info.m_publicPosition || info.m_characterID == me.GetZDOID() || seen.Contains(info.m_characterID)) continue;
                    if (!_site.Contains(info.m_position)) continue;
                    Arrow(n++, MapMark.Other, info.m_position, Vector3.zero, info.m_name);
                }
            for (int i = n; i < _arrows.Count; i++)
            {
                if (_arrows[i].gameObject.activeSelf) _arrows[i].gameObject.SetActive(false);
                if (_names[i].gameObject.activeSelf) _names[i].gameObject.SetActive(false);
            }
        }

        private static void Arrow(int i, MapMark mark, Vector3 world, Vector3 facing, string name)
        {
            while (_arrows.Count <= i)
            {
                RawImage img = GameUi.Rect(_marks, "Arrow", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f)).gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                _arrows.Add(img);
                TMP_Text label = GameUi.Label(_marks, "", Vector2.zero, new Vector2(160f, 24f), 13f, GameUi.Text, null, TextAlignmentOptions.Top, Vector2.zero);
                ((RectTransform)label.transform).pivot = new Vector2(0.5f, 1f);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                _names.Add(label);
            }
            RawImage arrow = _arrows[i];
            TMP_Text text = _names[i];
            Vector3 local = world - _site.Origin;
            Vector2 px = _sheet.ShownView.Px(local);   // (where the picture on screen has it: a new one may be being drawn)
            Rect area = _marks.rect;
            bool inView = px.x >= 0f && px.y >= 0f && px.x <= Size && px.y <= Size;
            if (arrow.gameObject.activeSelf != inView) arrow.gameObject.SetActive(inView);
            bool named = inView && !string.IsNullOrEmpty(name);
            if (text.gameObject.activeSelf != named) text.gameObject.SetActive(named);
            if (!inView) return;
            Texture2D tex = IconTexture(mark);
            if (arrow.texture != tex) arrow.texture = tex;
            var at = new Vector2(px.x / Size * area.width, px.y / Size * area.height);
            var r = (RectTransform)arrow.transform;
            if ((r.anchoredPosition - at).sqrMagnitude > 0.01f) r.anchoredPosition = at;
            float size = mark == MapMark.You ? 34f : 28f;
            if (r.sizeDelta.x != size) r.sizeDelta = new Vector2(size, size);
            float yaw = facing.sqrMagnitude > 0.0001f ? Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg : 0f;
            Quaternion turn = Quaternion.Euler(0f, 0f, -yaw);
            if (Quaternion.Angle(r.localRotation, turn) > 0.5f) r.localRotation = turn;
            // dim on another floor than the one shown
            float alpha = _scene.Map.FloorOf(local.y) == _floor ? 1f : 0.35f;
            if (Mathf.Abs(arrow.color.a - alpha) > 0.01f) arrow.color = new Color(1f, 1f, 1f, alpha);
            if (named)
            {
                GameUi.SetText(text, GameUi.NoParse(name));
                var tr = (RectTransform)text.transform;
                Vector2 under = at - new Vector2(0f, size * 0.55f);
                if ((tr.anchoredPosition - under).sqrMagnitude > 0.01f) tr.anchoredPosition = under;
                if (Mathf.Abs(text.alpha - alpha) > 0.01f) text.alpha = alpha;
            }
        }

        // ---- the test command: dungeon mapshot [floor] [all] [open], or dungeon mapshot mini (the minimap)

        /// <summary>
        /// Draws the map as this character sees it (or with "all", every room: a cheat, it gives the dungeon away) to
        /// BepInEx/claude/shots/dungeon-map-&lt;floor&gt;.png, with the players drawn in; floor: as the map counts them (1 the lowest; left out,
        /// the player's). "open" also opens the window there (for a screenshot). Answers what it drew.
        /// </summary>
        public static void Shot(string[] args, Action<JObject> output, Action<string> error)
        {
            Player p = Player.m_localPlayer;
            if (p == null) { error("no player: load a world first"); return; }
            bool all = args.Skip(1).Any(a => a.Equals("all", StringComparison.OrdinalIgnoreCase));
            bool open = args.Skip(1).Any(a => a.Equals("open", StringComparison.OrdinalIgnoreCase));
            int wanted = -1;
            foreach (string a in args.Skip(1))
                if (int.TryParse(a, out int n)) wanted = n - 1;
            if (all && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("mapshot all (the whole map)")); return; }
            Site site = Dungeons.Containing(p.transform.position)
                        ?? Dungeons.Sites.Where(s => s.Layout != null && s.HasOrigin).OrderBy(s => Dungeons.FlatDistance(s.EntrancePos, p.transform.position)).FirstOrDefault();
            if (site?.Layout == null) { error("no dungeon laid out near the player"); return; }
            if (args.Skip(1).Any(a => a.Equals("mini", StringComparison.OrdinalIgnoreCase)))
            {
                // what the minimap shows: the player's floor round them, fog of war as it is
                if (!site.Contains(p.transform.position)) { error("the minimap shows the dungeon only inside it: go in first"); return; }
                MapCanvas mini = DungeonMiniMap.Picture(site, p, out int miniFloor, out string state);
                string miniFile = Save(mini, "dungeon-map-mini.png", error);
                if (miniFile == null) return;
                output(new JObject
                {
                    ["file"] = miniFile,
                    ["dungeon"] = $"{site.PackName} seed {site.Seed} ({site.Id})",
                    ["floor"] = miniFloor + 1,
                    ["minimap"] = state,
                    ["pixels"] = $"{mini.W} x {mini.H} ({DungeonMiniMap.Ppm:0} a metre)",
                });
                return;
            }
            MapScene scene = Scene(site, all);
            var found = new List<MapThing>();
            Find(site, found);
            scene.Things.AddRange(found);
            MapLayout map = scene.Map;
            Vector3 me = p.transform.position - site.Origin;
            bool inside = site.Contains(p.transform.position);
            if (inside)
            {
                Camera cam = Utils.GetMainCamera();
                scene.Things.Add(new MapThing { Mark = MapMark.You, At = me, Forward = cam != null ? cam.transform.forward : p.transform.forward });
            }
            foreach (Player other in Player.GetAllPlayers())
                if (other != null && other != p && site.Contains(other.transform.position))
                    scene.Things.Add(new MapThing { Mark = MapMark.Other, At = other.transform.position - site.Origin, Forward = other.GetLookDir() });
            var floors = Enumerable.Range(0, map.Floors).Where(f => Enumerable.Range(0, map.Boxes.Length).Any(i => scene.Visible(i) && map.On(i, f))).ToList();
            int floor = wanted >= 0 ? wanted : inside ? map.FloorOf(me.y) : floors.FirstOrDefault();
            if (floor < 0 || floor >= map.Floors) { error($"no floor {floor + 1}: it has floors 1 to {map.Floors}"); return; }
            View(scene, floor, 0f, Vector2.zero);
            var canvas = new MapCanvas(Size, Size);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            MapPainter.Draw(scene, floor, canvas);
            double ms = clock.Elapsed.TotalMilliseconds;
            string file = Save(canvas, $"dungeon-map-{floor + 1}.png", error);
            if (file == null) return;

            var rooms = new JArray();
            for (int i = 0; i < map.Boxes.Length; i++)
                if (scene.Visible(i) && map.On(i, floor))
                    rooms.Add($"{i} {site.Layout.Rooms[i].Def.Name}{(floor > map.Base[i] ? " (upper part)" : "")}");
            var mechs = new JArray();
            for (int i = 0; i < map.Boxes.Length; i++)
            {
                if (!scene.Visible(i)) continue;
                foreach (MechDef m in site.Layout.Rooms[i].Def.Mechs)
                {
                    if (m.Kind == "machine") continue;
                    int s = scene.StateOf(i, m);
                    var joins = m.Joins.Where(j => j.State == s || j.State == -1).Select(j => $"{j.A}-{j.B}").ToList();
                    mechs.Add($"{i}.{m.Id} {m.Kind} state {s}{(joins.Count > 0 ? " joins " + string.Join(", ", joins) : "")} floor {map.FloorOf(map.MechAt(i, m).y + 0.5f) + 1}");
                }
            }
            output(new JObject
            {
                ["file"] = file,
                ["dungeon"] = $"{site.PackName} seed {site.Seed} ({site.Id})",
                ["floor"] = floor + 1,
                ["floors"] = new JArray(map.Levels.Select((l, i) => $"{i + 1}: doorways at {l:0.#} m{(floors.Contains(i) ? "" : " (nothing seen)")}")),
                ["playerFloor"] = inside ? map.FloorOf(me.y) + 1 : (int?)null,
                ["seen"] = $"{scene.Shown.Count(v => v)} of {scene.Shown.Length} rooms{(all ? " (all: fog of war ignored)" : "")}",
                ["roomsHere"] = rooms,
                ["mechanisms"] = mechs,
                ["found"] = new JArray(found.Select(t => $"{t.Mark} at {Mathf.RoundToInt(t.At.x)},{Mathf.RoundToInt(t.At.y)},{Mathf.RoundToInt(t.At.z)} floor {map.FloorOf(t.At.y + 0.5f) + 1}")),
                ["metresPerPixel"] = Math.Round(1.0 / scene.Ppm, 3),
                ["drawMs"] = Math.Round(ms, 1),
            });
            if (open) Open(site, floor, all);
        }

        /// <summary>A picture to BepInEx/claude/shots/&lt;name&gt; (null, after saying why, if it can't be).</summary>
        private static string Save(MapCanvas canvas, string name, Action<string> error)
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "claude", "shots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, name);
            var tex = new Texture2D(canvas.W, canvas.H, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(canvas.Px);
                tex.Apply(false);
                byte[] png = EncodeToPng(tex);
                if (png == null) { error("the game's PNG encoder (ImageConversion) wasn't found"); return null; }
                File.WriteAllBytes(file, png);
                return file;
            }
            finally { Object.Destroy(tex); }
        }

        /// <summary>The engine's PNG encoder (ImageConversion, its own module: found by reflection, so the mod needs no reference to it).</summary>
        internal static byte[] EncodeToPng(Texture2D tex)
        {
            Type t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            MethodInfo encode = t?.GetMethod("EncodeToPNG", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
            return encode?.Invoke(null, new object[] { tex }) as byte[];
        }
    }

    /// <summary>Inside one of our dungeons, the game's big map (M, or anything else that opens it) opens ours instead. Only then: everywhere
    /// else, and for the small map, the game's own runs as it is (MyLittleUI's postfix on it, the one popular patch there, still runs).</summary>
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class DungeonMap_InsteadOfTheGames
    {
        private static bool Prefix(Minimap.MapMode mode) => mode != Minimap.MapMode.Large || !DungeonMap.OpenInstead();
    }
}
