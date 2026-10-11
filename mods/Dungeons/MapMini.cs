using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// One map picture on screen (the big map's, the minimap's): its texture, the canvas MapPainter draws into, and the drawing itself, done
    /// on a worker thread from a copy of the scene (MapPainter is plain arithmetic, nothing of the engine's) and put into the texture when
    /// done. A picture is asked for with what decides it (a signature): asked again for the same, nothing happens; asked while one is being
    /// drawn, the newest is drawn after it. ShownView is the view of the picture in the texture now (markers on top follow it).
    /// </summary>
    internal sealed class MapSheet
    {
        public readonly int Size;
        public readonly Texture2D Texture;
        public MapScene ShownView;
        private readonly MapCanvas _canvas;
        private Task _drawing;
        private MapScene _drawingView, _next;
        private int _nextFloor, _generation, _drawingGeneration;
        private string _wanted = "";

        public MapSheet(int size, string name)
        {
            Size = size;
            _canvas = new MapCanvas(size, size);
            Texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        /// <summary>Draws this scene's floor, unless the same (by its signature) was last asked for; force: whatever it was.</summary>
        public void Request(MapScene scene, int floor, string signature, bool force)
        {
            if (!force && signature == _wanted) return;
            _wanted = signature;
            var copy = new MapScene { Map = scene.Map, Shown = (bool[])scene.Shown.Clone(), X0 = scene.X0, Z0 = scene.Z0, Ppm = scene.Ppm, Mini = scene.Mini };
            foreach (KeyValuePair<string, int> kv in scene.States) copy.States[kv.Key] = kv.Value;
            copy.Things.AddRange(scene.Things);
            copy.Cleared.UnionWith(scene.Cleared);
            copy.Recorded.UnionWith(scene.Recorded);
            copy.MyCheckpoint = scene.MyCheckpoint;
            _next = copy;
            _nextFloor = floor;
            Start();
        }

        private void Start()
        {
            if (_drawing != null || _next == null) return;
            MapScene scene = _next;
            int floor = _nextFloor;
            MapCanvas canvas = _canvas;
            _next = null;
            _drawingView = scene;
            _drawingGeneration = _generation;
            _drawing = Task.Run(() => MapPainter.Draw(scene, floor, canvas));
        }

        /// <summary>Every frame: a picture done goes into the texture (true when a new one shows), and the next one asked for starts.</summary>
        public bool Poll()
        {
            if (_drawing == null || !_drawing.IsCompleted) return false;
            Task done = _drawing;
            _drawing = null;
            bool shown = false;
            if (done.IsFaulted) DungeonMap.WarnOnce("draw", "Drawing the dungeon map: " + done.Exception?.GetBaseException());
            else if (_drawingGeneration == _generation && Texture != null)
            {
                Texture.SetPixels32(_canvas.Px);
                Texture.Apply(false);
                ShownView = _drawingView;
                shown = true;
            }
            Start();
            return shown;
        }

        /// <summary>What it showed is no longer wanted (closed, another dungeon): a picture still being drawn is thrown away when done.</summary>
        public void Forget()
        {
            _generation++;
            _wanted = "";
            _next = null;
            ShownView = null;
        }

        public void Destroy()
        {
            Forget();
            try { _drawing?.Wait(2000); } catch (Exception) { }   // (the canvas is the drawing's until it's done)
            _drawing = null;
            if (Texture != null) Object.Destroy(Texture);
        }
    }

    /// <summary>
    /// The corner minimap inside one of our dungeons: the dungeon map of the player's floor (MapPainter, minimap style: no frame, no floor
    /// below, no plates, thicker lines), north up as the game's own minimap is (the game never turns its minimap: only the player's arrow,
    /// which is the game's own, on top), centred on the player and following them, about Span metres across; the floor follows them up and
    /// down. Same fog of war and live mechanisms as the big map.
    ///
    /// It is laid over the game's small map image (Minimap.m_mapImageSmall) as that image's first child, filling it: it moves, scales, turns
    /// and hides with it (the HUD's hide key, another mod resizing or turning the minimap), and everything the game draws over its map (the
    /// player's arrow, the wind) still draws over this. The small map's pins (the world below's: of no use up here) are hidden while it shows,
    /// and each put back as it was. All of it goes when the player leaves the dungeon, logs out, or the mod unloads.
    ///
    /// Its picture covers a square of Size / Ppm metres round where the player was when it was drawn, and is drawn again only when what it
    /// shows changes (the floor, a room visited, a mechanism, a door) or the player comes near its edge; walking only moves the image's UVs.
    /// </summary>
    internal static class DungeonMiniMap
    {
        public const int Size = 1024;
        public const float Ppm = 4f;            // pixels a metre in its picture (1024 px: 256 m round the player)
        public const float Span = 64f;          // metres across the minimap
        private const string Name = "QuadDungeonsMini";

        private static MapSheet _sheet;
        private static RawImage _image;
        private static RectTransform _marks;
        private static readonly List<RawImage> _arrows = new List<RawImage>();
        private static Site _site;
        private static MapScene _scene;
        private static int _floor = -1;
        private static Vector2 _origin;         // the middle of its picture (dungeon frame x, z)
        private static bool _hasOrigin;
        private static float _nextLook, _nextScan;
        private static readonly List<MapThing> _found = new List<MapThing>();
        private static readonly List<KeyValuePair<GameObject, bool>> _hidden = new List<KeyValuePair<GameObject, bool>>();

        public static bool Showing => _image != null;

        /// <summary>Every frame (DungeonMap.Tick): shown while the player is inside one of our dungeons, built here; else the game's own.</summary>
        public static void Tick(Player p)
        {
            Minimap mm = Minimap.instance;
            Site site = p != null && !p.IsDead() && !Game.m_noMap ? Dungeons.Containing(p.transform.position) : null;
            if (site?.Layout == null || !site.Ready || !site.HasOrigin || mm == null || mm.m_mapImageSmall == null)
            {
                if (_site != null || _image != null) Remove();
                return;
            }
            if (_image == null || _site != site) { Remove(); Attach(mm, site); }
            if (_image == null) return;

            Vector3 local = p.transform.position - site.Origin;
            int floor = _scene.Map.FloorOf(local.y);
            var flat = new Vector2(local.x, local.z);
            // its picture: round where the player is, drawn again (round them) when they come within the minimap's reach of its edge
            float reach = Size / 2f / Ppm - Span;
            bool far = !_hasOrigin || Mathf.Abs(flat.x - _origin.x) > reach || Mathf.Abs(flat.y - _origin.y) > reach;
            if (far) { _origin = flat; _hasOrigin = true; }
            if (floor != _floor || far || Time.unscaledTime >= _nextLook) { _floor = floor; Look(); }
            if (_sheet.Poll() && !_image.enabled) _image.enabled = true;
            Place(p, local);
        }

        private static void Attach(Minimap mm, Site site)
        {
            RawImage map = mm.m_mapImageSmall;
            foreach (Transform old in map.transform.Cast<Transform>().Where(t => t.name == Name).ToList()) Object.Destroy(old.gameObject);   // (a hot reload's)
            if (_sheet == null) _sheet = new MapSheet(Size, "QuadDungeonsMiniMap");
            _sheet.Forget();
            RectTransform r = GameUi.Stretch(map.transform, Name);
            r.SetSiblingIndex(0);   // (over the map image, under whatever the game or another mod put on it)
            _image = r.gameObject.AddComponent<RawImage>();
            _image.raycastTarget = false;
            _image.texture = _sheet.Texture;
            _image.enabled = false;   // (until its first picture is drawn)
            _marks = GameUi.Stretch(r, "Marks");
            // the world's pins below would show at their x and z, meaning nothing up here
            Hide(mm.m_pinRootSmall != null ? mm.m_pinRootSmall.gameObject : null);
            Hide(mm.m_pinNameRootSmall != null ? mm.m_pinNameRootSmall.gameObject : null);
            _site = site;
            _scene = new MapScene { Map = DungeonMap.MapOf(site.Layout), Mini = true, Shown = new bool[site.Layout.Rooms.Count] };
            _floor = -1;
            _hasOrigin = false;
            _nextScan = 0f;
        }

        private static void Hide(GameObject go)
        {
            if (go == null || _hidden.Any(h => h.Key == go)) return;
            _hidden.Add(new KeyValuePair<GameObject, bool>(go, go.activeSelf));
            if (go.activeSelf) go.SetActive(false);
        }

        /// <summary>The game's minimap back exactly as it was: ours gone, the pins it hid shown again (each as it was).</summary>
        public static void Remove()
        {
            if (_image != null) Object.Destroy(_image.gameObject);
            _image = null;
            _marks = null;
            _arrows.Clear();
            foreach (KeyValuePair<GameObject, bool> h in _hidden)
                if (h.Key != null && h.Key.activeSelf != h.Value) h.Key.SetActive(h.Value);
            _hidden.Clear();
            _site = null;
            _scene = null;
            _found.Clear();
            _sheet?.Forget();
        }

        /// <summary>Everything gone (leaving the world, the mod unloading).</summary>
        public static void Clear()
        {
            Remove();
            _sheet?.Destroy();
            _sheet = null;
        }

        /// <summary>Asks for a picture of what it shows now (drawn only if that changed).</summary>
        private static void Look()
        {
            _nextLook = Time.unscaledTime + 0.25f;
            Fill(_site, _scene, _found, ref _nextScan);
            _scene.Look(Ppm, _origin, Size, Size);
            _sheet.Request(_scene, _floor, DungeonMap.Signature(_site, _scene, _floor, _found), false);
        }

        /// <summary>A scene's live part: the rooms seen, the mechanisms' states, the doors and chests found (every second).</summary>
        internal static void Fill(Site site, MapScene scene, List<MapThing> found, ref float nextScan)
        {
            bool[] visited = MapMemory.Visited(site);
            if (visited != null) scene.Shown = visited;
            DungeonMap.States(site, scene);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; DungeonMap.Find(site, found); }
            scene.Things.Clear();
            scene.Things.AddRange(found);
        }

        /// <summary>Every frame: the image's window on its picture, centred on the player; other players' arrows.</summary>
        private static void Place(Player me, Vector3 local)
        {
            MapScene view = _sheet.ShownView;
            if (view == null) return;
            Vector2 px = view.Px(local);
            float w = Span * view.Ppm / Size;
            var uv = new Rect(px.x / Size - w / 2f, px.y / Size - w / 2f, w, w);
            if (_image.uvRect != uv) _image.uvRect = uv;
            int n = 0;
            Rect area = _marks.rect;
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == me || !_site.Contains(other.transform.position)) continue;
                Vector3 o = other.transform.position - _site.Origin;
                Vector2 at = (view.Px(o) - px) / (Span * view.Ppm) + new Vector2(0.5f, 0.5f);   // (0..1 across the minimap)
                if (at.x < 0f || at.y < 0f || at.x > 1f || at.y > 1f) continue;
                RawImage arrow = Arrow(n++);
                var r = (RectTransform)arrow.transform;
                var pos = new Vector2(at.x * area.width, at.y * area.height);
                if ((r.anchoredPosition - pos).sqrMagnitude > 0.01f) r.anchoredPosition = pos;
                Vector3 f = other.GetLookDir();
                Quaternion turn = Quaternion.Euler(0f, 0f, -Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                if (Quaternion.Angle(r.localRotation, turn) > 0.5f) r.localRotation = turn;
                float alpha = _scene.Map.FloorOf(o.y) == _floor ? 1f : 0.35f;
                if (Mathf.Abs(arrow.color.a - alpha) > 0.01f) arrow.color = new Color(1f, 1f, 1f, alpha);
            }
            for (int i = n; i < _arrows.Count; i++)
                if (_arrows[i].gameObject.activeSelf) _arrows[i].gameObject.SetActive(false);
        }

        private static RawImage Arrow(int i)
        {
            while (_arrows.Count <= i)
            {
                RawImage img = GameUi.Rect(_marks, "Arrow", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f)).gameObject.AddComponent<RawImage>();
                img.raycastTarget = false;
                img.texture = DungeonMap.IconTexture(MapMark.Other);
                _arrows.Add(img);
            }
            if (!_arrows[i].gameObject.activeSelf) _arrows[i].gameObject.SetActive(true);
            return _arrows[i];
        }

        /// <summary>What the minimap shows now (or would, inside), as a picture: `dungeon mapshot mini`. The player's arrow (the game's,
        /// on the minimap) is drawn in. Null with why when there's nothing to draw.</summary>
        public static MapCanvas Picture(Site site, Player p, out int floor, out string state)
        {
            floor = -1;
            var scene = new MapScene { Map = DungeonMap.MapOf(site.Layout), Mini = true, Shown = new bool[site.Layout.Rooms.Count] };
            var found = new List<MapThing>();
            float scan = 0f;
            Fill(site, scene, found, ref scan);
            Vector3 local = p.transform.position - site.Origin;
            floor = scene.Map.FloorOf(local.y);
            scene.Look(Ppm, new Vector2(local.x, local.z), Size, Size);
            var whole = new MapCanvas(Size, Size);
            MapPainter.Draw(scene, floor, whole);
            // the window the minimap shows, cut out of it
            int n = Mathf.RoundToInt(Span * Ppm);
            var cut = new MapCanvas(n, n);
            int x0 = Size / 2 - n / 2, y0 = Size / 2 - n / 2;
            for (int y = 0; y < n; y++) Array.Copy(whole.Px, (y0 + y) * Size + x0, cut.Px, y * n, n);
            Camera cam = Utils.GetMainCamera();
            Vector3 look = cam != null ? cam.transform.forward : p.transform.forward;
            MapPainter.Icon(cut, MapMark.You, new Vector2(n / 2f, n / 2f), 26f, new Vector2(look.x, look.z));
            foreach (Player other in Player.GetAllPlayers())
            {
                if (other == null || other == p || !site.Contains(other.transform.position)) continue;
                Vector2 at = scene.Px(other.transform.position - site.Origin) - new Vector2(x0, y0);
                Vector3 f = other.GetLookDir();
                if (at.x >= 0f && at.y >= 0f && at.x < n && at.y < n) MapPainter.Icon(cut, MapMark.Other, at, 22f, new Vector2(f.x, f.z));
            }
            Minimap mm = Minimap.instance;
            Rect rect = mm != null && mm.m_mapImageSmall != null ? mm.m_mapImageSmall.rectTransform.rect : default;
            state = (Showing ? "showing on the minimap" : "not showing on the minimap now") + $" (its image is {rect.width:0} x {rect.height:0} UI units; {Span:0} m across)";
            return cut;
        }
    }
}
