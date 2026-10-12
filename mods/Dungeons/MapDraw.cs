using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>What the map can show: the markers (each has a picture in the legend) and the room styles.</summary>
    internal enum MapMark
    {
        You, Other, Lever, Plate, GateShut, GateOpen, StairUp, StairDown, Path, Rotator, Chest, Vault, Exit, Unexplored, Upper, Flight, Drop,
        Shortcut, ShortcutShut, Boss, BossDefeated, Pedestal, PedestalFull, Brazier, BrazierLit, PushBlock, Teleporter, TeleporterOff,
        Checkpoint, CheckpointMine,
    }

    /// <summary>Something found in the world rather than in the layout (a door's state, a chest), or a player (for a picture).</summary>
    internal struct MapThing
    {
        public MapMark Mark;
        public Vector3 At;              // dungeon frame
        public Vector3 Forward;         // a door: the way it faces (its leaf lies across it); a player: where they look
        public float Width;             // a door's width
    }

    /// <summary>
    /// One picture of the map: which layout, which rooms show (fog of war: the ones visited), every mechanism's state now, what was found in
    /// the world, and the view (dungeon frame x, z at the picture's bottom-left corner, pixels a metre). MapPainter draws a floor of it.
    /// </summary>
    internal sealed class MapScene
    {
        public MapLayout Map;
        public bool[] Shown;
        public readonly Dictionary<string, int> States = new Dictionary<string, int>();   // "room.id": the state it is in (or going to) now
        public readonly HashSet<string> Cleared = new HashSet<string>();                  // "room.id" of the encounters won (fights, the boss)
        public string MyCheckpoint;     // "room.id" of the local player's checkpoint in this dungeon, or null
        public readonly HashSet<string> Recorded = new HashSet<string>();                 // "room.id" of the mechanisms with a record (ever used
                                                                                           // since the last reset: a wall broken, a shortcut opened)
        public readonly List<MapThing> Things = new List<MapThing>();
        public float X0, Z0, Ppm = 4f;
        public bool Mini;               // for the minimap: no frame, no floor below, no plates, even parchment, thicker lines and marks

        public bool Visible(int room) => room >= 0 && Shown != null && room < Shown.Length && Shown[room];
        public int StateOf(int room, MechDef m) => States.TryGetValue(room + "." + m.Id, out int s) ? s : m.Start;
        public Vector2 Px(Vector3 p) => new Vector2((p.x - X0) * Ppm, (p.z - Z0) * Ppm);

        /// <summary>A breakable wall still standing (no record yet: MechSet.IsBroken).</summary>
        public bool Standing(MapLayout.Breakable b) => !Recorded.Contains(b.Room + "." + b.Mech.Id);

        /// <summary>A mechanism behind a stash's wall that still stands ("hiddenBy" on it): not drawn.</summary>
        public bool Stashed(int room, MechDef m)
        {
            if (string.IsNullOrEmpty(m.HiddenBy)) return false;
            foreach (MapLayout.Stash st in Map.Stashes)
                if (st.Room == room && st.Mechs.Contains(m) && !Recorded.Contains(st.Room + "." + st.Mech.Id)) return true;
            return false;
        }

        /// <summary>Behind a stash's wall that still stands: a thing found there (its chest) isn't drawn.</summary>
        public bool Stashed(Vector3 p)
        {
            foreach (MapLayout.Stash st in Map.Stashes)
            {
                if (Recorded.Contains(st.Room + "." + st.Mech.Id)) continue;   // (broken)
                foreach (Vector3 h in st.Hidden)
                    if ((h - p).sqrMagnitude < 1.5f * 1.5f) return true;
            }
            return false;
        }

        /// <summary>A shortcut pair open (either door has a record: MechSet.ShortcutOpen).</summary>
        public bool Open(MapLayout.Shortcut pair) => pair.Ends.Any(e => Recorded.Contains(e.Key + "." + e.Value.Id));

        /// <summary>A doorway walled up by a breakable wall still standing (drawn as wall, never a doorway, from either side).</summary>
        public bool Sealed(int room, int door)
        {
            foreach (MapLayout.Breakable b in Map.Breakables)
                if (((b.Room == room && b.Door == door) || (b.OtherRoom == room && b.OtherDoor == door)) && Standing(b)) return true;
            return false;
        }

        /// <summary>Takes out of Shown the secret rooms whose wall still stands (a copy: the remembered rooms stay as they are). Call after
        /// the states are filled in.</summary>
        public void HideSecrets()
        {
            bool[] shown = null;
            foreach (MapLayout.Breakable b in Map.Breakables)
            {
                if (!Standing(b) || Shown == null || b.Secret >= Shown.Length || !Shown[b.Secret]) continue;
                if (shown == null) shown = (bool[])Shown.Clone();
                shown[b.Secret] = false;
            }
            if (shown != null) Shown = shown;
        }

        /// <summary>The closest a view may come: this many metres across at least, so one corridor never fills the map.</summary>
        public const float MinSpan = 48f;

        /// <summary>The view that holds the shown rooms of one floor (or of all floors: -1; or all of them, when that floor has none) in a
        /// picture this big, with a border and a few metres round them; at least MinSpan metres across, so one small room isn't drawn huge.</summary>
        public void Fit(int w, int h, float border, int floor = -1)
        {
            if (!Map.Extent(Shown, floor, out Vector2 min, out Vector2 max) && !Map.Extent(Shown, -1, out min, out max))
            {
                min = new Vector2(-MinSpan / 2f, -MinSpan / 2f);
                max = -min;
            }
            min -= new Vector2(6f, 6f);
            max += new Vector2(6f, 6f);
            Vector2 span = Vector2.Max(max - min, new Vector2(MinSpan, MinSpan));
            Vector2 mid = (min + max) / 2f;
            Ppm = Mathf.Min((w - 2f * border) / span.x, (h - 2f * border) / span.y);
            X0 = mid.x - w / 2f / Ppm;
            Z0 = mid.y - h / 2f / Ppm;
        }

        /// <summary>The most pixels a metre a view of this size may have (MinSpan across).</summary>
        public static float MaxPpm(int w, int h, float border) => Mathf.Min(w - 2f * border, h - 2f * border) / MinSpan;

        /// <summary>A view this close (pixels a metre), centred on a point (x, z in the dungeon's frame).</summary>
        public void Look(float ppm, Vector2 centre, int w, int h)
        {
            Ppm = ppm;
            X0 = centre.x - w / 2f / Ppm;
            Z0 = centre.y - h / 2f / Ppm;
        }

        /// <summary>The middle of the view (x, z in the dungeon's frame).</summary>
        public Vector2 Centre(int w, int h) => new Vector2(X0 + w / 2f / Ppm, Z0 + h / 2f / Ppm);

        /// <summary>The view k times closer than it is, centred on a point (x, z in the dungeon's frame).</summary>
        public void Zoom(float k, Vector2 centre, int w, int h)
        {
            Ppm *= k;
            X0 = centre.x - w / 2f / Ppm;
            Z0 = centre.y - h / 2f / Ppm;
        }
    }

    /// <summary>
    /// A picture in memory (row 0 at the bottom, as a Texture2D's), with the few shapes the map needs, smoothed at their edges: lines, dashes,
    /// rectangles, discs, rings, convex polygons, stripes. Plain C# (no engine calls), so the map can be drawn outside the game.
    /// </summary>
    internal sealed class MapCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;
        private float _cx0, _cy0, _cx1, _cy1;

        public MapCanvas(int w, int h)
        {
            W = w; H = h;
            Px = new Color32[w * h];
            NoClip();
        }

        public void Clip(float x0, float y0, float x1, float y1) { _cx0 = x0; _cy0 = y0; _cx1 = x1; _cy1 = y1; }
        public void NoClip() { _cx0 = 0f; _cy0 = 0f; _cx1 = W; _cy1 = H; }
        public void Clear(Color32 c) { for (int i = 0; i < Px.Length; i++) Px[i] = c; }

        /// <summary>Paints c over pixel (x, y) at coverage a (the colour's own alpha too).</summary>
        public void Put(int x, int y, Color32 c, float a)
        {
            if (x < 0 || y < 0 || x >= W || y >= H || x < _cx0 || y < _cy0 || x >= _cx1 || y >= _cy1) return;
            a *= c.a / 255f;
            if (a <= 0.004f) return;
            if (a > 1f) a = 1f;
            int i = y * W + x;
            Color32 d = Px[i];
            float da = d.a / 255f, outA = a + da * (1f - a);
            if (outA <= 0f) return;
            float k = da * (1f - a);
            Px[i] = new Color32((byte)((c.r * a + d.r * k) / outA + 0.5f), (byte)((c.g * a + d.g * k) / outA + 0.5f),
                                (byte)((c.b * a + d.b * k) / outA + 0.5f), (byte)(outA * 255f + 0.5f));
        }

        private int X0(float x) => Math.Max(0, (int)Math.Floor(Math.Max(x, _cx0)));
        private int X1(float x) => Math.Min(W - 1, (int)Math.Ceiling(Math.Min(x, _cx1)));
        private int Y0(float y) => Math.Max(0, (int)Math.Floor(Math.Max(y, _cy0)));
        private int Y1(float y) => Math.Min(H - 1, (int)Math.Ceiling(Math.Min(y, _cy1)));
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>A filled rectangle, its edges smoothed.</summary>
        public void Rect(float x0, float y0, float x1, float y1, Color32 c, float a = 1f)
        {
            if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
            if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
            for (int y = Y0(y0); y <= Y1(y1); y++)
            {
                float cy = Clamp01(Math.Min(y + 1f, y1) - Math.Max(y, y0));
                if (cy <= 0f) continue;
                for (int x = X0(x0); x <= X1(x1); x++)
                {
                    float cx = Clamp01(Math.Min(x + 1f, x1) - Math.Max(x, x0));
                    if (cx > 0f) Put(x, y, c, a * cx * cy);
                }
            }
        }

        /// <summary>A line of this width (pixels), round at its ends.</summary>
        public void Line(Vector2 p, Vector2 q, float width, Color32 c, float a = 1f)
        {
            float h = Math.Max(width, 1f) / 2f, fade = Math.Min(1f, width);
            if (Math.Abs(p.y - q.y) < 0.01f || Math.Abs(p.x - q.x) < 0.01f)
            {
                // along an axis (walls): a rectangle, much quicker
                Rect(Math.Min(p.x, q.x) - h, Math.Min(p.y, q.y) - h, Math.Max(p.x, q.x) + h, Math.Max(p.y, q.y) + h, c, a * fade);
                return;
            }
            Vector2 d = q - p;
            float len2 = d.sqrMagnitude;
            for (int y = Y0(Math.Min(p.y, q.y) - h - 1f); y <= Y1(Math.Max(p.y, q.y) + h + 1f); y++)
                for (int x = X0(Math.Min(p.x, q.x) - h - 1f); x <= X1(Math.Max(p.x, q.x) + h + 1f); x++)
                {
                    var m = new Vector2(x + 0.5f, y + 0.5f);
                    float t = len2 > 0f ? Clamp01(Vector2.Dot(m - p, d) / len2) : 0f;
                    float dist = (m - (p + d * t)).magnitude;
                    float cov = Clamp01(h + 0.5f - dist);
                    if (cov > 0f) Put(x, y, c, a * cov * fade);
                }
        }

        /// <summary>A dashed line: dashes of this length with gaps between, starting with a dash.</summary>
        public void Dashed(Vector2 p, Vector2 q, float width, float dash, float gap, Color32 c, float a = 1f)
        {
            float len = (q - p).magnitude;
            if (len < 0.01f) return;
            Vector2 dir = (q - p) / len;
            for (float s = 0f; s < len; s += dash + gap)
                Line(p + dir * s, p + dir * Math.Min(len, s + dash), width, c, a);
        }

        public void Disc(Vector2 o, float r, Color32 c, float a = 1f)
        {
            for (int y = Y0(o.y - r - 1f); y <= Y1(o.y + r + 1f); y++)
                for (int x = X0(o.x - r - 1f); x <= X1(o.x + r + 1f); x++)
                {
                    float d = (new Vector2(x + 0.5f, y + 0.5f) - o).magnitude;
                    float cov = Clamp01(r + 0.5f - d);
                    if (cov > 0f) Put(x, y, c, a * cov);
                }
        }

        public void Ring(Vector2 o, float r, float width, Color32 c, float a = 1f)
        {
            float h = width / 2f;
            for (int y = Y0(o.y - r - h - 1f); y <= Y1(o.y + r + h + 1f); y++)
                for (int x = X0(o.x - r - h - 1f); x <= X1(o.x + r + h + 1f); x++)
                {
                    float d = (new Vector2(x + 0.5f, y + 0.5f) - o).magnitude;
                    float cov = Clamp01(h + 0.5f - Math.Abs(d - r));
                    if (cov > 0f) Put(x, y, c, a * cov);
                }
        }

        /// <summary>A filled convex polygon (its corners in either order).</summary>
        public void Poly(Vector2[] pts, Color32 c, float a = 1f)
        {
            int n = pts.Length;
            float area = 0f;
            for (int i = 0; i < n; i++) area += pts[i].x * pts[(i + 1) % n].y - pts[(i + 1) % n].x * pts[i].y;
            float sign = area >= 0f ? 1f : -1f;
            Vector2 min = pts[0], max = pts[0];
            foreach (Vector2 p in pts) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            for (int y = Y0(min.y - 1f); y <= Y1(max.y + 1f); y++)
                for (int x = X0(min.x - 1f); x <= X1(max.x + 1f); x++)
                {
                    var m = new Vector2(x + 0.5f, y + 0.5f);
                    float d = float.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 e = pts[(i + 1) % n] - pts[i];
                        float len = e.magnitude;
                        if (len < 0.0001f) continue;
                        d = Math.Min(d, sign * (e.x * (m.y - pts[i].y) - e.y * (m.x - pts[i].x)) / len);
                    }
                    float cov = Clamp01(d + 0.5f);
                    if (cov > 0f) Put(x, y, c, a * cov);
                }
        }

        public void Outline(Vector2[] pts, float width, Color32 c, float a = 1f)
        {
            for (int i = 0; i < pts.Length; i++) Line(pts[i], pts[(i + 1) % pts.Length], width, c, a);
        }

        /// <summary>Diagonal stripes over a rectangle (a room's upper part, open to the floor below).</summary>
        public void Stripes(float x0, float y0, float x1, float y1, float spacing, float width, Color32 c, float a = 1f)
        {
            float h = width / 2f;
            for (int y = Y0(y0); y <= Y1(y1) && y < y1; y++)
                for (int x = X0(x0); x <= X1(x1) && x < x1; x++)
                {
                    if (x < x0 || y < y0) continue;
                    float t = (x + y) % spacing;
                    float d = Math.Min(t, spacing - t) * 0.7071f;
                    float cov = Clamp01(h + 0.5f - d);
                    if (cov > 0f) Put(x, y, c, a * cov);
                }
        }
    }

    /// <summary>
    /// Draws one floor of a MapScene the way Valheim's own map looks: brown ink on parchment, in a carved frame. Visited rooms as their
    /// footprints with their walls, the doorways as gaps (a doorway into a room not yet visited trails off in dashes), the floor below
    /// faintly under them, the upper part of a taller room striped; then the mechanisms as they stand now (never what to do with them),
    /// and what was found in the world (doors, chests; players for a picture).
    /// </summary>
    internal static class MapPainter
    {
        public static readonly Color32 Paper = new Color32(222, 203, 160, 255);
        public static readonly Color32 Wash = new Color32(238, 224, 190, 255);
        public static readonly Color32 Ink = new Color32(58, 38, 22, 255);
        public static readonly Color32 Red = new Color32(150, 38, 24, 255);
        public static readonly Color32 Rune = new Color32(28, 104, 112, 255);   // the giants' rune glow, in ink: the ways through
        public static readonly Color32 Gold = new Color32(196, 146, 44, 255);
        public static readonly Color32 Green = new Color32(46, 104, 52, 255);
        public static readonly Color32 Blue = new Color32(54, 92, 168, 255);
        public static readonly Color32 YouFill = new Color32(240, 196, 84, 255);
        public static readonly Color32 Flame = new Color32(214, 96, 28, 255);
        public static readonly Color32 Stone = new Color32(168, 158, 140, 255);
        public const float DoorGap = 4f;    // a doorway's gap in a wall, metres


        /// <summary>Draws floor `floor` of the scene into the canvas (all of it: the parchment first).</summary>
        public static void Draw(MapScene s, int floor, MapCanvas c)
        {
            Parchment(c, !s.Mini);
            MapLayout map = s.Map;
            // the minimap: shown smaller than it's drawn, so thicker walls and bigger marks
            float wall = Mathf.Clamp(s.Ppm * 0.4f, 2f, 5f) * (s.Mini ? 1.5f : 1f);
            float icon = Mathf.Clamp(s.Ppm * 2.4f, 20f, 34f) * (s.Mini ? 1.5f : 1f);

            // the floor below, faintly, for finding the way
            if (floor > 0 && !s.Mini)
                for (int i = 0; i < map.Boxes.Length; i++)
                    if (s.Visible(i) && map.On(i, floor - 1) && !map.On(i, floor)) Box(c, s, i, out _, out _, out _, out _, Ink, 0.16f, wall * 0.6f);

            // the rooms: their floor (washed), or the upper part of a taller room (striped), then the walls over them all
            for (int i = 0; i < map.Boxes.Length; i++)
            {
                if (!s.Visible(i) || !map.On(i, floor)) continue;
                Corners(s, i, out float x0, out float y0, out float x1, out float y1);
                c.Rect(x0, y0, x1, y1, Wash, 0.7f);
                if (floor > map.Base[i]) c.Stripes(x0, y0, x1, y1, Math.Max(6f, s.Ppm * 1.6f), Math.Max(1f, s.Ppm * 0.18f), Ink, 0.22f);
                // a soft shadow inside the walls
                float sh = wall * 1.6f;
                c.Rect(x0, y0, x1, y0 + sh, Ink, 0.06f); c.Rect(x0, y1 - sh, x1, y1, Ink, 0.06f);
                c.Rect(x0, y0, x0 + sh, y1, Ink, 0.06f); c.Rect(x1 - sh, y0, x1, y1, Ink, 0.06f);
            }
            for (int i = 0; i < map.Boxes.Length; i++)
                if (s.Visible(i) && map.On(i, floor)) Walls(c, s, i, floor, wall);

            // the ways between floors that don't move: fixed stairs, doorways onto another floor, drops
            foreach (MapLayout.Flight f in map.Flights)
                if (s.Visible(f.Room) && floor >= f.LowFloor && floor <= f.HighFloor) Flight(c, s, f, floor, icon);
            for (int i = 0; i < map.Boxes.Length; i++)
            {
                if (!s.Visible(i)) continue;
                PlacedRoom r = map.Layout.Rooms[i];
                for (int d = 0; d < r.Def.Doorways.Count; d++)
                {
                    int mark = map.DoorMark[i][d];
                    if (mark == 0 || map.DoorFloor[i][d] != floor || s.Sealed(i, d)) continue;
                    // beside the doorway, inside the room, pointing in: the way on through the room goes up (or down); past the way out's mark
                    Vector3 into = Flat(r.DoorwayFacing(d));
                    Vector2 at = s.Px(r.DoorwayPos(d) + into * (i == 0 && d == map.EntranceDoor ? 4.6f : 2.6f)), dir = new Vector2(into.x, into.z), side = new Vector2(dir.y, -dir.x);
                    float size = icon * 0.75f;
                    if (mark == 2) { Icon(c, MapMark.StairUp, at + side * size * 0.45f, size, dir); Icon(c, MapMark.StairDown, at - side * size * 0.45f, size, dir); }
                    else Icon(c, mark > 0 ? MapMark.StairUp : MapMark.StairDown, at, size, dir);
                }
            }
            foreach (MapLayout.Drop drop in map.Drops)
                if (s.Visible(drop.Room) && drop.Floor == floor)
                    DropArrow(c, s.Px(drop.At), new Vector2(drop.Toward.x, drop.Toward.z), Math.Max(icon * 1.4f, s.Ppm * 4f), icon);

            // what moves: the ways through it now (turning stairs, rotating rooms and bridges, platforms that join two doorways)
            for (int i = 0; i < map.Boxes.Length; i++)
            {
                if (!s.Visible(i)) continue;
                foreach (MechDef m in map.Layout.Rooms[i].Def.Mechs) if (!s.Stashed(i, m)) Ways(c, s, i, m, floor, icon);
            }
            // the gates, then the things to use, then what was found in the world
            for (int i = 0; i < map.Boxes.Length; i++)
            {
                if (!s.Visible(i)) continue;
                foreach (MechDef m in map.Layout.Rooms[i].Def.Mechs) if (!s.Stashed(i, m)) Mark(c, s, i, m, floor, icon);
                if (map.IsVault(i) && map.Base[i] == floor)
                {
                    // the vault: gilded inside its walls, its mark a little past the middle away from its way in (the chest is in the middle)
                    Corners(s, i, out float x0, out float y0, out float x1, out float y1);
                    float inset = Mathf.Clamp(s.Ppm * 0.9f, 4f, 10f);
                    c.Outline(new[] { new Vector2(x0 + inset, y0 + inset), new Vector2(x1 - inset, y0 + inset), new Vector2(x1 - inset, y1 - inset), new Vector2(x0 + inset, y1 - inset) },
                              Math.Max(1.5f, s.Ppm * 0.2f), Gold, 0.8f);
                    Vector3 mid = map.Boxes[i].center;
                    PlacedRoom vault = map.Layout.Rooms[i];
                    if (vault.Def.Doorways.Count > 0)
                    {
                        Vector3 away = mid - vault.DoorwayPos(0);
                        away.y = 0f;
                        Vector3 half = map.Boxes[i].extents;
                        if (away.sqrMagnitude > 0.01f) mid += away.normalized * Math.Min(half.x, half.z) * 0.6f;
                    }
                    Icon(c, MapMark.Vault, s.Px(mid), icon * 1.2f, Vector2.up);
                }
            }
            // shortcuts: both ends once the pair is open; before, only the far end (the entrance end is a plain wall till then)
            foreach (MapLayout.Shortcut pair in map.Shortcuts)
            {
                bool open = s.Open(pair);
                for (int k = 0; k < pair.Ends.Count; k++)
                {
                    KeyValuePair<int, MechDef> end = pair.Ends[k];
                    if (!s.Visible(map.SeenFrom(end.Key)) || (!open && k == 0 && pair.Ends.Count > 1)) continue;
                    Vector3 at = map.MarkAt(end.Key, end.Value, out int f);
                    if (f == floor) Icon(c, open ? MapMark.Shortcut : MapMark.ShortcutShut, s.Px(at), icon * 1.25f, Vector2.up);
                }
            }
            foreach (KeyValuePair<int, MechDef> boss in map.Bosses)
            {
                if (!s.Visible(boss.Key)) continue;
                Bounds box = map.Boxes[boss.Key];
                Vector3 at = boss.Value != null ? map.MechAt(boss.Key, boss.Value) : new Vector3(box.center.x, box.min.y + 1f, box.center.z);
                if (map.FloorOf(at.y + 0.5f) != floor) continue;
                bool won = boss.Value != null && s.Cleared.Contains(boss.Key + "." + boss.Value.Id);
                Icon(c, won ? MapMark.BossDefeated : MapMark.Boss, s.Px(at), icon * 1.3f, Vector2.up);
            }
            foreach (KeyValuePair<int, MechDef> end in map.Teleporters)
            {
                if (!s.Visible(end.Key) || s.Stashed(end.Key, end.Value)) continue;
                Vector3 at = map.MechAt(end.Key, end.Value);
                if (map.FloorOf(at.y + 0.5f) != floor) continue;
                bool on = s.StateOf(end.Key, end.Value) != 0;
                foreach (KeyValuePair<int, MechDef> other in map.Teleporters)
                {
                    if (other.Key == end.Key && other.Value == end.Value) continue;
                    if (string.IsNullOrEmpty(end.Value.Pair) || other.Value.Pair != end.Value.Pair || !s.Visible(other.Key) || s.Stashed(other.Key, other.Value)) continue;
                    Vector3 there = map.MechAt(other.Key, other.Value);
                    if (map.FloorOf(there.y + 0.5f) != floor || string.CompareOrdinal(end.Key + end.Value.Id, other.Key + other.Value.Id) > 0) continue;
                    c.Dashed(s.Px(at), s.Px(there), Math.Max(1.5f, s.Ppm * 0.2f), Math.Max(3f, s.Ppm * 0.5f), Math.Max(4f, s.Ppm * 0.9f), Rune, on ? 0.7f : 0.3f);
                    // a one-way pair (an end that only receives: Block.Send false): an arrow on the line, near the end it goes to
                    bool sendsHere = end.Value.Block?.Send ?? true, sendsThere = other.Value.Block?.Send ?? true;
                    if (sendsHere != sendsThere)
                    {
                        Vector2 from = s.Px(sendsHere ? at : there), to = s.Px(sendsHere ? there : at), d = to - from;
                        if (d.magnitude > icon * 2f) Arrow(c, to - d.normalized * icon * 0.9f, d, icon * 0.5f, true);
                    }
                }
                Icon(c, on ? MapMark.Teleporter : MapMark.TeleporterOff, s.Px(at), icon, Vector2.up);
            }
            // the way out: the entrance room's way in
            if (s.Visible(0) && map.EntranceDoor >= 0 && map.DoorFloor[0][map.EntranceDoor] == floor)
            {
                Vector3 at = map.Layout.Rooms[0].DoorwayPos(map.EntranceDoor);
                Vector3 outward = map.Outward(0, map.EntranceDoor);
                Icon(c, MapMark.Exit, s.Px(at - outward * 2.2f), icon * 1.1f, new Vector2(outward.x, outward.z));
            }
            foreach (MapThing t in s.Things)
            {
                if (map.FloorOf(t.At.y + 0.5f) != floor) continue;
                bool player = t.Mark == MapMark.You || t.Mark == MapMark.Other;
                if (!player)
                {
                    // a door at a shown room's doorway (it may join one not shown yet); anything else only inside a shown room (so nothing of
                    // a secret room behind its wall), and nothing a stash's wall still hides
                    bool door = t.Mark == MapMark.GateShut || t.Mark == MapMark.GateOpen;
                    if (door ? !NearShown(s, t.At) : !InShown(s, t.At)) continue;
                    if (s.Stashed(t.At)) continue;
                }
                var fwd = new Vector2(t.Forward.x, t.Forward.z);
                if (t.Mark == MapMark.GateShut || t.Mark == MapMark.GateOpen)
                {
                    // a door's leaf lies across the way it faces
                    Vector2 across = fwd.sqrMagnitude > 0.0001f ? new Vector2(fwd.y, -fwd.x).normalized : Vector2.right;
                    float half = Math.Max(1f, t.Width) / 2f * s.Ppm;
                    Vector2 o = s.Px(t.At);
                    Gate(c, o - across * half, o + across * half, t.Mark == MapMark.GateShut, icon);
                }
                else Icon(c, t.Mark, s.Px(t.At), t.Mark == MapMark.Chest ? icon * 0.9f : icon, fwd);
            }
            if (!s.Mini) Frame(c);
        }

        /// <summary>Inside a room that shows (its box, its floor to its ceiling).</summary>
        private static bool InShown(MapScene s, Vector3 p)
        {
            for (int i = 0; i < s.Map.Boxes.Length; i++)
            {
                if (!s.Visible(i)) continue;
                Vector3 lo = s.Map.Boxes[i].min, hi = s.Map.Boxes[i].max;
                if (p.x >= lo.x - 0.1f && p.x <= hi.x + 0.1f && p.z >= lo.z - 0.1f && p.z <= hi.z + 0.1f && p.y >= lo.y - 1f && p.y <= hi.y + 0.5f) return true;
            }
            return false;
        }

        /// <summary>Near a room that shows (things found in the world show only there: fog of war).</summary>
        private static bool NearShown(MapScene s, Vector3 p)
        {
            for (int i = 0; i < s.Map.Boxes.Length; i++)
            {
                if (!s.Visible(i)) continue;
                Vector3 lo = s.Map.Boxes[i].min, hi = s.Map.Boxes[i].max;   // (Bounds.Contains is the engine's code: not outside the game)
                if (p.x > lo.x - 1f && p.x < hi.x + 1f && p.y > lo.y - 1f && p.y < hi.y + 1f && p.z > lo.z - 1f && p.z < hi.z + 1f) return true;
            }
            return false;
        }

        private static void Corners(MapScene s, int room, out float x0, out float y0, out float x1, out float y1)
        {
            Bounds b = s.Map.Boxes[room];
            Vector2 lo = s.Px(b.min), hi = s.Px(b.max);
            x0 = lo.x; y0 = lo.y; x1 = hi.x; y1 = hi.y;
        }

        private static void Box(MapCanvas c, MapScene s, int room, out float x0, out float y0, out float x1, out float y1, Color32 ink, float a, float w)
        {
            Corners(s, room, out x0, out y0, out x1, out y1);
            c.Line(new Vector2(x0, y0), new Vector2(x1, y0), w, ink, a);
            c.Line(new Vector2(x0, y1), new Vector2(x1, y1), w, ink, a);
            c.Line(new Vector2(x0, y0), new Vector2(x0, y1), w, ink, a);
            c.Line(new Vector2(x1, y0), new Vector2(x1, y1), w, ink, a);
        }

        /// <summary>A room's four walls on this floor, with a gap at each doorway that leads somewhere (another room, or out); a doorway into
        /// a room not visited yet trails off in dashes. Doorways that go up or down get a ring.</summary>
        private static void Walls(MapCanvas c, MapScene s, int room, int floor, float wall)
        {
            MapLayout map = s.Map;
            PlacedRoom r = map.Layout.Rooms[room];
            Bounds b = map.Boxes[room];
            // gaps per wall: 0 south (min z), 1 north (max z), 2 west (min x), 3 east (max x); in metres along the wall
            var gaps = new List<Vector2>[4];
            for (int k = 0; k < 4; k++) gaps[k] = new List<Vector2>();
            for (int d = 0; d < r.Def.Doorways.Count; d++)
            {
                if (map.DoorFloor[room][d] != floor) continue;
                bool way = (map.Open(room, d) || (room == 0 && d == map.EntranceDoor)) && !s.Sealed(room, d);
                if (!way) continue;
                Vector3 at = r.DoorwayPos(d), into = r.DoorwayFacing(d);
                int side = into.z > 0.7f ? 0 : into.z < -0.7f ? 1 : into.x > 0.7f ? 2 : into.x < -0.7f ? 3 : -1;
                if (side < 0)
                {
                    c.Ring(s.Px(at), Math.Max(4f, s.Ppm * 1.6f), wall * 0.7f, Ink, 0.8f);   // a shaft or a hatch
                    continue;
                }
                float length = side < 2 ? b.size.x : b.size.z, along = side < 2 ? at.x : at.z;
                float g = Math.Min(DoorGap, length * 0.7f) / 2f;
                gaps[side].Add(new Vector2(along - g, along + g));
                int other = map.LinkRoom[room][d];
                if (other >= 0 && !s.Visible(other))
                {
                    // into a room not visited yet: the way trails off
                    Vector3 outward = map.Outward(room, d);
                    Vector3 edge = side == 0 ? new Vector3(at.x, at.y, b.min.z) : side == 1 ? new Vector3(at.x, at.y, b.max.z)
                                 : side == 2 ? new Vector3(b.min.x, at.y, at.z) : new Vector3(b.max.x, at.y, at.z);
                    Unexplored(c, s.Px(edge), new Vector2(outward.x, outward.z), g * s.Ppm, s.Ppm, wall);
                }
            }
            Wall(c, s, new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.min.z), gaps[0], true, wall);
            Wall(c, s, new Vector3(b.min.x, 0, b.max.z), new Vector3(b.max.x, 0, b.max.z), gaps[1], true, wall);
            Wall(c, s, new Vector3(b.min.x, 0, b.min.z), new Vector3(b.min.x, 0, b.max.z), gaps[2], false, wall);
            Wall(c, s, new Vector3(b.max.x, 0, b.min.z), new Vector3(b.max.x, 0, b.max.z), gaps[3], false, wall);
        }

        private static void Wall(MapCanvas c, MapScene s, Vector3 from, Vector3 to, List<Vector2> gaps, bool alongX, float wall)
        {
            float a = alongX ? from.x : from.z, b = alongX ? to.x : to.z;
            gaps.Sort((p, q) => p.x.CompareTo(q.x));
            float at = a;
            void Piece(float p, float q)
            {
                if (q - p < 0.05f) return;
                Vector3 u = alongX ? new Vector3(p, 0, from.z) : new Vector3(from.x, 0, p);
                Vector3 v = alongX ? new Vector3(q, 0, from.z) : new Vector3(from.x, 0, q);
                c.Line(s.Px(u), s.Px(v), wall, Ink, 0.92f);
            }
            foreach (Vector2 g in gaps)
            {
                Piece(at, Math.Min(b, g.x));
                at = Math.Max(at, g.y);
            }
            Piece(at, b);
        }

        private static void Unexplored(MapCanvas c, Vector2 edge, Vector2 outward, float halfGap, float ppm, float wall)
        {
            if (outward.sqrMagnitude < 0.0001f) return;
            outward.Normalize();
            var across = new Vector2(outward.y, -outward.x);
            float len = Math.Max(16f, ppm * 4f), dash = Math.Max(4f, ppm * 0.8f);
            c.Dashed(edge + across * halfGap, edge + across * halfGap + outward * len, wall * 0.8f, dash, dash * 0.8f, Ink, 0.7f);
            c.Dashed(edge - across * halfGap, edge - across * halfGap + outward * len, wall * 0.8f, dash, dash * 0.8f, Ink, 0.7f);
        }

        /// <summary>The ways a mechanism makes through its room now (its joins in its current state): a turning stair as a ladder between
        /// its landings (an arrow at the end on another floor), anything else as a rune-blue path through the room. A rotator joined to
        /// nothing now shows which way it points.</summary>
        private static void Ways(MapCanvas c, MapScene s, int room, MechDef m, int floor, float icon)
        {
            MapLayout map = s.Map;
            PlacedRoom r = map.Layout.Rooms[room];
            int state = s.StateOf(room, m);
            bool joined = false;
            foreach (Join j in m.Joins)
            {
                if (j.State != state && j.State != -1) continue;
                int a = map.Door(room, j.A), b = map.Door(room, j.B);
                if (a < 0 || b < 0) continue;
                joined = true;
                int fa = map.DoorFloor[room][a], fb = map.DoorFloor[room][b];
                if (fa != floor && fb != floor) continue;
                Vector3 pa = r.DoorwayPos(a) + Flat(r.DoorwayFacing(a)) * 1.5f, pb = r.DoorwayPos(b) + Flat(r.DoorwayFacing(b)) * 1.5f;
                if (fa == fb)
                {
                    Vector3 mid = m.Kind == "rotator" || m.Kind == "platform" ? map.MechAt(room, m) : (pa + pb) / 2f;
                    Path(c, s.Px(pa), s.Px(mid), s.Ppm);
                    Path(c, s.Px(mid), s.Px(pb), s.Ppm);
                    c.Disc(s.Px(pa), Math.Max(2.5f, s.Ppm * 0.45f), Rune);
                    c.Disc(s.Px(pb), Math.Max(2.5f, s.Ppm * 0.45f), Rune);
                    continue;
                }
                // between floors: from the end on this floor to the other
                Vector3 here = fa == floor ? pa : pb, there = fa == floor ? pb : pa;
                bool up = (fa == floor ? fb : fa) > floor;
                Vector2 p = s.Px(here), q = s.Px(there);
                if (m.Kind == "stair") Ladder(c, p, q, s.Ppm);
                else Path(c, p, q, s.Ppm);
                c.Disc(p, Math.Max(3f, s.Ppm * 0.55f), Rune);
                Vector2 dir = (q - p).sqrMagnitude > 0.01f ? (q - p).normalized : Vector2.up;
                Icon(c, up ? MapMark.StairUp : MapMark.StairDown, q - dir * icon * 0.2f, icon * 0.9f, dir);
            }
            if (m.Kind == "rotator")
            {
                Vector3 at = map.MechAt(room, m);
                if (map.FloorOf(at.y + 0.5f) != floor) return;
                Vector2 o = s.Px(at);
                if (!joined)
                {
                    // where it points now: along its own z, turned with it (a bridge across its pit)
                    float yaw = map.MechYaw(room, m, state) * Mathf.Deg2Rad;
                    var dir = new Vector2((float)Math.Sin(yaw), (float)Math.Cos(yaw));
                    Bounds b = map.Boxes[room];
                    float half = Math.Min(b.size.x, b.size.z) * 0.38f * s.Ppm;
                    c.Line(o - dir * half, o + dir * half, Math.Max(3f, s.Ppm * 0.8f), Ink, 0.75f);
                }
                Icon(c, MapMark.Rotator, o, icon, Vector2.up);
            }
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }

        private static void Path(MapCanvas c, Vector2 p, Vector2 q, float ppm)
        {
            float dash = Math.Max(4f, ppm * 0.9f);
            c.Dashed(p, q, Math.Max(2f, ppm * 0.35f), dash, dash * 0.6f, Rune, 0.95f);
        }

        private static void Ladder(MapCanvas c, Vector2 p, Vector2 q, float ppm)
        {
            Vector2 d = q - p;
            float len = d.magnitude;
            if (len < 1f) return;
            Vector2 dir = d / len, across = new Vector2(dir.y, -dir.x);
            float half = Math.Max(3f, ppm * 0.9f), w = Math.Max(1.5f, ppm * 0.22f);
            c.Line(p + across * half, q + across * half, w, Rune, 0.95f);
            c.Line(p - across * half, q - across * half, w, Rune, 0.95f);
            float step = Math.Max(5f, ppm * 1.4f);
            for (float t = step / 2f; t < len; t += step)
                c.Line(p + dir * t + across * half, p + dir * t - across * half, w * 0.8f, Rune, 0.8f);
        }

        /// <summary>A mechanism's own mark where it stands: a gate across its doorway (shut or open), a lever (leaning by its state), a plate
        /// (pressed or not), a way out.</summary>
        private static void Mark(MapCanvas c, MapScene s, int room, MechDef m, int floor, float icon)
        {
            MapLayout map = s.Map;
            PlacedRoom r = map.Layout.Rooms[room];
            int state = s.StateOf(room, m);
            Vector3 at = map.MechAt(room, m);
            bool here = map.FloorOf(at.y + 0.5f) == floor;
            if (Layouts.IsGate(m))
            {
                bool shut = state == 0;
                int d = map.Door(room, m.Doorway);
                if (d >= 0)
                {
                    if (map.DoorFloor[room][d] != floor) return;
                    Vector3 p = r.DoorwayPos(d), into = Flat(r.DoorwayFacing(d));
                    var across = new Vector3(into.z, 0f, -into.x);
                    Bounds b = map.Boxes[room];
                    float g = Math.Min(DoorGap, (Math.Abs(across.x) > 0.5f ? b.size.x : b.size.z) * 0.7f) / 2f;
                    Gate(c, s.Px(p - across * g), s.Px(p + across * g), shut, icon);
                }
                else if (here)
                {
                    float yaw = map.MechYaw(room, m, 0) * Mathf.Deg2Rad;
                    var across = new Vector3((float)Math.Cos(yaw), 0f, -(float)Math.Sin(yaw));
                    Gate(c, s.Px(at - across * 1.5f), s.Px(at + across * 1.5f), shut, icon);
                }
                return;
            }
            if (!here) return;
            switch (m.Kind)
            {
                case "lever": Lever(c, s.Px(at), icon, state != 0); break;
                case "plate": if (!s.Mini) Plate(c, s.Px(at), icon, state != 0); break;   // (too small to read on the minimap)
                case "exit": Icon(c, MapMark.Exit, s.Px(at), icon * 1.1f, Vector2.zero); break;
                case "pedestal": Icon(c, state != 0 ? MapMark.PedestalFull : MapMark.Pedestal, s.Px(at), icon, Vector2.up); break;
                case "brazier": Icon(c, state != 0 ? MapMark.BrazierLit : MapMark.Brazier, s.Px(at), icon, Vector2.up); break;
                case "checkpoint":
                    Icon(c, s.MyCheckpoint == room + "." + m.Id ? MapMark.CheckpointMine : MapMark.Checkpoint, s.Px(at), icon, Vector2.up);
                    break;
                case "pushblock":
                {
                    // where it stands now: its cell (its pose in this state)
                    Vector3 cell = map.PoseAt(room, m, state);
                    if (map.FloorOf(cell.y + 0.5f) == floor) Icon(c, MapMark.PushBlock, s.Px(cell), Math.Max(icon * 0.8f, (m.Block?.Cell ?? 2f) * s.Ppm * 0.8f), Vector2.up);
                    break;
                }
            }
        }

        // ---- the marks, also drawn alone for the legend

        /// <summary>A marker centred at o, about `size` pixels across; dir: which way it points (players, the way out, a stair's arrow).</summary>
        public static void Icon(MapCanvas c, MapMark mark, Vector2 o, float size, Vector2 dir)
        {
            float s = size;
            Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up, across = new Vector2(d.y, -d.x);
            switch (mark)
            {
                case MapMark.You:
                case MapMark.Other:
                {
                    var pts = new[] { o + d * s * 0.62f, o - d * s * 0.4f + across * s * 0.4f, o - d * s * 0.18f, o - d * s * 0.4f - across * s * 0.4f };
                    Vector2[] tri1 = { pts[0], pts[1], pts[2] }, tri2 = { pts[0], pts[2], pts[3] };
                    Color32 fill = mark == MapMark.You ? YouFill : Blue;
                    c.Poly(tri1, fill); c.Poly(tri2, fill);
                    c.Outline(pts, Math.Max(1.5f, s * 0.08f), Ink);
                    break;
                }
                case MapMark.Lever: Lever(c, o, s, false); break;
                case MapMark.Plate: Plate(c, o, s, false); break;
                case MapMark.GateShut: Gate(c, o - new Vector2(s * 0.45f, 0f), o + new Vector2(s * 0.45f, 0f), true, s); break;
                case MapMark.GateOpen: Gate(c, o - new Vector2(s * 0.45f, 0f), o + new Vector2(s * 0.45f, 0f), false, s); break;
                case MapMark.StairUp:
                case MapMark.StairDown:
                {
                    Vector2[] tri = { o + d * s * 0.42f, o - d * s * 0.3f + across * s * 0.36f, o - d * s * 0.3f - across * s * 0.36f };
                    c.Poly(tri, mark == MapMark.StairUp ? Ink : Wash);
                    c.Outline(tri, Math.Max(1.5f, s * 0.09f), Ink);
                    break;
                }
                case MapMark.Flight:
                {
                    FlightShape(c, o - new Vector2(0f, s * 0.42f), o + new Vector2(0f, s * 0.42f), s * 0.3f, 5, s * 0.06f);
                    Arrow(c, o + new Vector2(0f, s * 0.12f), Vector2.up, s * 0.5f, true);
                    break;
                }
                case MapMark.Drop:
                    DropArrow(c, o - new Vector2(0f, s * 0.45f), Vector2.up, s * 0.9f, s);
                    break;
                case MapMark.Shortcut:
                case MapMark.ShortcutShut:
                {
                    // a little arched door with a way through it both ways (shut: barred)
                    float w = s * 0.32f, h = s * 0.3f;
                    c.Disc(o, s * 0.48f, Wash, 0.9f);
                    c.Rect(o.x - w, o.y - h, o.x + w, o.y + h * 0.4f, Gold, 0.85f);
                    c.Disc(new Vector2(o.x, o.y + h * 0.4f), w, Gold, 0.85f);
                    c.Ring(new Vector2(o.x, o.y + h * 0.4f), w, Math.Max(1.2f, s * 0.07f), Ink);
                    c.Line(new Vector2(o.x - w, o.y - h), new Vector2(o.x - w, o.y + h * 0.4f), Math.Max(1.2f, s * 0.07f), Ink);
                    c.Line(new Vector2(o.x + w, o.y - h), new Vector2(o.x + w, o.y + h * 0.4f), Math.Max(1.2f, s * 0.07f), Ink);
                    c.Line(new Vector2(o.x - w, o.y - h), new Vector2(o.x + w, o.y - h), Math.Max(1.2f, s * 0.07f), Ink);
                    if (mark == MapMark.Shortcut)
                    {
                        Arrow(c, new Vector2(o.x + w * 0.35f, o.y), Vector2.right, s * 0.32f, true);
                        Arrow(c, new Vector2(o.x - w * 0.35f, o.y), Vector2.left, s * 0.32f, true);
                    }
                    else c.Line(new Vector2(o.x - w * 1.1f, o.y - h * 0.1f), new Vector2(o.x + w * 1.1f, o.y - h * 0.1f), Math.Max(2f, s * 0.13f), Red);
                    break;
                }
                case MapMark.Pedestal:
                case MapMark.PedestalFull:
                {
                    // a plinth from above: a square, its top's ring, what it holds in gold
                    float h = s * 0.32f;
                    var sq = new[] { new Vector2(o.x - h, o.y - h), new Vector2(o.x + h, o.y - h), new Vector2(o.x + h, o.y + h), new Vector2(o.x - h, o.y + h) };
                    c.Poly(sq, Paper);
                    c.Outline(sq, Math.Max(1.4f, s * 0.08f), Ink);
                    c.Ring(o, h * 0.6f, Math.Max(1f, s * 0.05f), Ink, 0.8f);
                    if (mark == MapMark.PedestalFull) c.Disc(o, h * 0.48f, Gold);
                    break;
                }
                case MapMark.Brazier:
                case MapMark.BrazierLit:
                {
                    // a bowl on its feet; lit, a flame in it
                    Vector2 bowl = o - new Vector2(0f, s * 0.12f);
                    c.Poly(new[] { bowl + new Vector2(-s * 0.36f, 0f), bowl + new Vector2(s * 0.36f, 0f), bowl + new Vector2(s * 0.2f, -s * 0.2f), bowl + new Vector2(-s * 0.2f, -s * 0.2f) }, Ink);
                    c.Line(bowl + new Vector2(-s * 0.12f, -s * 0.2f), bowl + new Vector2(-s * 0.22f, -s * 0.36f), Math.Max(1.2f, s * 0.07f), Ink);
                    c.Line(bowl + new Vector2(s * 0.12f, -s * 0.2f), bowl + new Vector2(s * 0.22f, -s * 0.36f), Math.Max(1.2f, s * 0.07f), Ink);
                    if (mark == MapMark.BrazierLit)
                    {
                        c.Poly(new[] { bowl + new Vector2(-s * 0.24f, s * 0.02f), bowl + new Vector2(s * 0.24f, s * 0.02f), bowl + new Vector2(0f, s * 0.6f) }, Flame);
                        c.Poly(new[] { bowl + new Vector2(-s * 0.11f, s * 0.02f), bowl + new Vector2(s * 0.11f, s * 0.02f), bowl + new Vector2(0f, s * 0.34f) }, Gold);
                    }
                    else c.Line(bowl + new Vector2(-s * 0.3f, s * 0.04f), bowl + new Vector2(s * 0.3f, s * 0.04f), Math.Max(1f, s * 0.05f), Ink, 0.6f);
                    break;
                }
                case MapMark.PushBlock:
                {
                    float h = s * 0.4f;
                    var sq = new[] { new Vector2(o.x - h, o.y - h), new Vector2(o.x + h, o.y - h), new Vector2(o.x + h, o.y + h), new Vector2(o.x - h, o.y + h) };
                    c.Poly(sq, Stone);
                    c.Outline(sq, Math.Max(1.4f, s * 0.08f), Ink);
                    c.Line(sq[0], sq[2], Math.Max(1f, s * 0.05f), Ink, 0.6f);
                    c.Line(sq[1], sq[3], Math.Max(1f, s * 0.05f), Ink, 0.6f);
                    break;
                }
                case MapMark.Teleporter:
                case MapMark.TeleporterOff:
                {
                    // a rune circle: two rings and a mark between (off: faded)
                    float a = mark == MapMark.Teleporter ? 1f : 0.45f;
                    c.Disc(o, s * 0.42f, Wash, 0.9f);
                    c.Ring(o, s * 0.4f, Math.Max(1.4f, s * 0.08f), Rune, a);
                    c.Ring(o, s * 0.22f, Math.Max(1.2f, s * 0.06f), Rune, a);
                    for (int k = 0; k < 4; k++)
                    {
                        float t = k * (float)Math.PI / 2f + 0.785f;
                        var d4 = new Vector2((float)Math.Cos(t), (float)Math.Sin(t));
                        c.Line(o + d4 * s * 0.24f, o + d4 * s * 0.38f, Math.Max(1f, s * 0.06f), Rune, a);
                    }
                    if (mark == MapMark.Teleporter) c.Disc(o, s * 0.08f, Rune);
                    break;
                }
                case MapMark.Checkpoint:
                case MapMark.CheckpointMine:
                {
                    // a banner on its pole (yours: gold)
                    Vector2 foot = o + new Vector2(-s * 0.18f, -s * 0.42f), top = foot + new Vector2(0f, s * 0.84f);
                    c.Line(foot, top, Math.Max(1.4f, s * 0.08f), Ink);
                    c.Poly(new[] { top, top + new Vector2(s * 0.46f, -s * 0.16f), top + new Vector2(0f, -s * 0.34f) }, mark == MapMark.CheckpointMine ? Gold : Wash);
                    c.Outline(new[] { top, top + new Vector2(s * 0.46f, -s * 0.16f), top + new Vector2(0f, -s * 0.34f) }, Math.Max(1.2f, s * 0.06f), Ink);
                    c.Rect(foot.x - s * 0.14f, foot.y - s * 0.03f, foot.x + s * 0.14f, foot.y + s * 0.04f, Ink);
                    break;
                }
                case MapMark.Boss:
                case MapMark.BossDefeated:
                {
                    // a giant's skull: the crown, the eyes, the nose, the teeth; defeated, struck through in red
                    float r = s * 0.34f;
                    Vector2 head = o + new Vector2(0f, s * 0.06f);
                    c.Disc(o, s * 0.5f, Wash, 0.9f);
                    c.Disc(head, r, Paper);
                    c.Rect(head.x - r * 0.62f, head.y - r * 1.25f, head.x + r * 0.62f, head.y - r * 0.4f, Paper);
                    c.Ring(head, r, Math.Max(1.4f, s * 0.07f), Ink);
                    c.Outline(new[] { new Vector2(head.x - r * 0.62f, head.y - r * 0.75f), new Vector2(head.x - r * 0.62f, head.y - r * 1.25f),
                                      new Vector2(head.x + r * 0.62f, head.y - r * 1.25f), new Vector2(head.x + r * 0.62f, head.y - r * 0.75f) },
                              Math.Max(1.2f, s * 0.06f), Ink);
                    c.Disc(head + new Vector2(-r * 0.4f, -r * 0.1f), r * 0.27f, Ink);
                    c.Disc(head + new Vector2(r * 0.4f, -r * 0.1f), r * 0.27f, Ink);
                    c.Poly(new[] { head + new Vector2(0f, -r * 0.35f), head + new Vector2(r * 0.14f, -r * 0.6f), head + new Vector2(-r * 0.14f, -r * 0.6f) }, Ink);
                    for (int t = -1; t <= 1; t++)
                        c.Line(new Vector2(head.x + t * r * 0.3f, head.y - r * 0.85f), new Vector2(head.x + t * r * 0.3f, head.y - r * 1.2f), Math.Max(1f, s * 0.04f), Ink);
                    if (mark == MapMark.BossDefeated)
                    {
                        c.Line(o + new Vector2(-s * 0.42f, -s * 0.42f), o + new Vector2(s * 0.42f, s * 0.42f), Math.Max(2f, s * 0.11f), Red);
                        c.Line(o + new Vector2(-s * 0.42f, s * 0.42f), o + new Vector2(s * 0.42f, -s * 0.42f), Math.Max(2f, s * 0.11f), Red);
                    }
                    break;
                }
                case MapMark.Path:
                    c.Dashed(o - new Vector2(s * 0.5f, 0f), o + new Vector2(s * 0.5f, 0f), Math.Max(2f, s * 0.12f), s * 0.22f, s * 0.14f, Rune);
                    break;
                case MapMark.Rotator:
                    c.Disc(o, s * 0.36f, Wash);
                    c.Ring(o, s * 0.34f, Math.Max(1.5f, s * 0.1f), Ink);
                    c.Ring(o, s * 0.16f, Math.Max(1f, s * 0.07f), Ink, 0.8f);
                    c.Disc(o, Math.Max(1.5f, s * 0.07f), Ink);
                    break;
                case MapMark.Chest:
                {
                    float w = s * 0.38f, h = s * 0.26f;
                    c.Rect(o.x - w, o.y - h, o.x + w, o.y + h, Gold);
                    c.Outline(new[] { new Vector2(o.x - w, o.y - h), new Vector2(o.x + w, o.y - h), new Vector2(o.x + w, o.y + h), new Vector2(o.x - w, o.y + h) },
                              Math.Max(1.5f, s * 0.08f), Ink);
                    c.Line(new Vector2(o.x - w, o.y + h * 0.25f), new Vector2(o.x + w, o.y + h * 0.25f), Math.Max(1f, s * 0.06f), Ink);
                    c.Disc(new Vector2(o.x, o.y + h * 0.25f), Math.Max(1.2f, s * 0.07f), Ink);
                    break;
                }
                case MapMark.Vault:
                {
                    float r = s * 0.5f;
                    Vector2[] dia = { o + new Vector2(0, r), o + new Vector2(r * 0.8f, 0), o - new Vector2(0, r), o - new Vector2(r * 0.8f, 0) };
                    c.Poly(dia, Gold);
                    c.Outline(dia, Math.Max(1.5f, s * 0.08f), Ink);
                    float k = 0.42f;
                    Vector2[] inner = { o + new Vector2(0, r * k), o + new Vector2(r * 0.8f * k, 0), o - new Vector2(0, r * k), o - new Vector2(r * 0.8f * k, 0) };
                    c.Poly(inner, Ink, 0.85f);
                    break;
                }
                case MapMark.Exit:
                {
                    c.Disc(o, s * 0.42f, Wash);
                    c.Ring(o, s * 0.4f, Math.Max(1.5f, s * 0.1f), Green);
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Vector2[] tri = { o + d * s * 0.3f, o - d * s * 0.2f + across * s * 0.24f, o - d * s * 0.2f - across * s * 0.24f };
                        c.Poly(tri, Green);
                    }
                    else c.Disc(o, s * 0.16f, Green);
                    break;
                }
                case MapMark.Unexplored:
                    c.Line(o - new Vector2(s * 0.5f, s * 0.3f), o + new Vector2(-s * 0.18f, -s * 0.3f), Math.Max(2f, s * 0.12f), Ink, 0.92f);
                    c.Line(o - new Vector2(-s * 0.18f, s * 0.3f), o + new Vector2(s * 0.5f, -s * 0.3f), Math.Max(2f, s * 0.12f), Ink, 0.92f);
                    Unexplored(c, o - new Vector2(0f, s * 0.3f), Vector2.up, s * 0.18f, s * 0.22f, Math.Max(2f, s * 0.1f));
                    break;
                case MapMark.Upper:
                {
                    float h = s * 0.42f;
                    c.Rect(o.x - h, o.y - h, o.x + h, o.y + h, Wash, 0.9f);
                    c.Stripes(o.x - h, o.y - h, o.x + h, o.y + h, Math.Max(4f, s * 0.24f), Math.Max(1f, s * 0.05f), Ink, 0.4f);
                    c.Outline(new[] { new Vector2(o.x - h, o.y - h), new Vector2(o.x + h, o.y - h), new Vector2(o.x + h, o.y + h), new Vector2(o.x - h, o.y + h) },
                              Math.Max(1.5f, s * 0.08f), Ink);
                    break;
                }
            }
        }

        /// <summary>A flight of fixed stairs: its steps across its footprint, and an arrow up it on its lower floor (or where it stays on one
        /// floor), down it on its upper floor.</summary>
        private static void Flight(MapCanvas c, MapScene s, MapLayout.Flight f, int floor, float icon)
        {
            var up = new Vector2(f.Up.x, f.Up.z);
            if (up.sqrMagnitude < 0.0001f) up = Vector2.up;
            up.Normalize();
            float half = Math.Max(0.4f, f.Step / 2f) * s.Ppm;
            Vector2 lo = s.Px(f.Low) - up * half, hi = s.Px(f.High) + up * half;
            FlightShape(c, lo, hi, f.Width / 2f * s.Ppm, f.Steps, Math.Max(1f, s.Ppm * 0.12f));
            float size = Mathf.Min(icon * 0.7f, (hi - lo).magnitude * 0.45f);
            if (floor == f.LowFloor) Arrow(c, hi - up * size * 0.6f, up, size, true);
            else if (floor == f.HighFloor) Arrow(c, lo + up * size * 0.6f, -up, size, false);
        }

        private static void FlightShape(MapCanvas c, Vector2 lo, Vector2 hi, float halfWidth, int steps, float line)
        {
            Vector2 d = hi - lo;
            if (d.sqrMagnitude < 0.01f) return;
            Vector2 up = d.normalized, across = new Vector2(up.y, -up.x) * halfWidth;
            var quad = new[] { lo + across, hi + across, hi - across, lo - across };
            c.Poly(quad, Wash, 0.95f);
            for (int k = 1; k < Math.Max(2, steps); k++)
            {
                Vector2 m = Vector2.Lerp(lo, hi, k / (float)Math.Max(2, steps));
                c.Line(m + across, m - across, line, Ink, 0.6f);
            }
            c.Outline(quad, line * 1.4f, Ink, 0.9f);
        }

        /// <summary>A triangle pointing along dir: filled (up) or hollow (down), in ink.</summary>
        private static void Arrow(MapCanvas c, Vector2 o, Vector2 dir, float s, bool filled)
        {
            Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up, across = new Vector2(d.y, -d.x);
            Vector2[] tri = { o + d * s * 0.5f, o - d * s * 0.35f + across * s * 0.42f, o - d * s * 0.35f - across * s * 0.42f };
            c.Poly(tri, filled ? Ink : Wash, filled ? 0.9f : 1f);
            c.Outline(tri, Math.Max(1.2f, s * 0.1f), Ink);
        }

        /// <summary>A declared drop: dashes from the edge you drop from, ending in a hollow arrow (down).</summary>
        private static void DropArrow(MapCanvas c, Vector2 from, Vector2 dir, float length, float icon)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            float head = Math.Max(8f, icon * 0.55f);
            Vector2 end = from + dir * Math.Max(length - head * 0.5f, head);
            c.Dashed(from, end, Math.Max(1.5f, icon * 0.08f), Math.Max(3f, icon * 0.18f), Math.Max(2f, icon * 0.12f), Ink, 0.85f);
            Arrow(c, end + dir * head * 0.3f, dir, head, false);
        }

        private static void Lever(MapCanvas c, Vector2 o, float s, bool pulled)
        {
            Vector2 foot = o - new Vector2(0f, s * 0.22f), head = foot + new Vector2(pulled ? s * 0.3f : -s * 0.3f, s * 0.44f);
            c.Disc(o, s * 0.42f, Wash, 0.85f);
            c.Rect(foot.x - s * 0.24f, foot.y - s * 0.08f, foot.x + s * 0.24f, foot.y + s * 0.02f, Ink);
            c.Line(foot, head, Math.Max(2f, s * 0.11f), Ink);
            c.Disc(head, Math.Max(2f, s * 0.12f), Red);
        }

        private static void Plate(MapCanvas c, Vector2 o, float s, bool pressed)
        {
            float h = s * 0.3f;
            var sq = new[] { new Vector2(o.x - h, o.y - h), new Vector2(o.x + h, o.y - h), new Vector2(o.x + h, o.y + h), new Vector2(o.x - h, o.y + h) };
            c.Rect(o.x - h, o.y - h, o.x + h, o.y + h, pressed ? Ink : Wash, pressed ? 0.6f : 0.9f);
            c.Outline(sq, Math.Max(1.5f, s * 0.09f), Ink);
            c.Rect(o.x - h * 0.45f, o.y - h * 0.45f, o.x + h * 0.45f, o.y + h * 0.45f, Ink, pressed ? 0.8f : 0.35f);
        }

        /// <summary>A gate or door across a doorway, from p to q: shut, a red bar; open, its two posts with the way between them.</summary>
        private static void Gate(MapCanvas c, Vector2 p, Vector2 q, bool shut, float s)
        {
            float post = Math.Max(2.5f, s * 0.16f);
            Vector2 d = q - p;
            float len = d.magnitude;
            if (shut) c.Line(p, q, Math.Max(3f, s * 0.26f), Red);
            else if (len > 0.5f)
            {
                // open: its leaf swung back from one post through a quarter turn, as a plan draws a door (either side: the map can't
                // tell which way it opens)
                Vector2 along = d / len, n = new Vector2(-along.y, along.x);
                float leaf = len * 0.8f;
                c.Line(p, p + n * leaf, Math.Max(2f, s * 0.12f), Ink, 0.9f);
                Vector2 last = p + along * leaf;
                for (int i = 1; i <= 8; i++)
                {
                    float t = i / 8f * (float)Math.PI / 2f;
                    Vector2 next = p + (along * (float)Math.Cos(t) + n * (float)Math.Sin(t)) * leaf;
                    if (i % 2 == 1) c.Line(last, next, Math.Max(1f, s * 0.06f), Ink, 0.6f);
                    last = next;
                }
            }
            c.Disc(p, post, Ink);
            c.Disc(q, post, Ink);
        }

        // ---- the parchment and its frame

        /// <summary>The parchment the map is drawn on (made once for a size, then copied): darker towards its edges for a whole sheet,
        /// even all over for the minimap (only a window of its picture shows, moving as the player walks).</summary>
        private static void Parchment(MapCanvas c, bool sheet)
        {
            int k = sheet ? 0 : 1;
            Color32[] paper = _papers[k];
            if (paper == null || paper.Length != c.Px.Length || _paperW[k] != c.W)
            {
                paper = MakePaper(c.W, c.H, sheet);
                _paperW[k] = c.W;
                _papers[k] = paper;
            }
            Array.Copy(paper, c.Px, paper.Length);
        }

        private static readonly Color32[][] _papers = new Color32[2][];
        private static readonly int[] _paperW = new int[2];
        private static volatile bool _warming;

        /// <summary>Makes the parchment for this size on a worker thread (it takes a tenth of a second or more: not on the frame the map opens).
        /// Plain arithmetic, nothing of the engine's.</summary>
        public static void Prewarm(int w, int h, bool sheet)
        {
            int k = sheet ? 0 : 1;
            if ((_papers[k] != null && _papers[k].Length == w * h && _paperW[k] == w) || _warming) return;
            _warming = true;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Color32[] paper = MakePaper(w, h, sheet);
                    if (_papers[k] == null) { _paperW[k] = w; _papers[k] = paper; }   // (the map may have made its own meanwhile)
                }
                finally { _warming = false; }
            });
        }

        /// <summary>Forget the parchment (the mod unloading).</summary>
        public static void Forget() { _papers[0] = _papers[1] = null; }

        private static Color32[] MakePaper(int w, int h, bool sheet)
        {
            var paper = new Color32[w * h];
            float cx = w / 2f, cy = h / 2f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float n = Noise(x / 150f, y / 150f) * 0.6f + Noise(x / 40f + 17f, y / 40f + 5f) * 0.3f + Noise(x / 9f, y / 9f) * 0.1f;
                    float grain = Hash(x, y) - 0.5f;
                    float rx = (x - cx) / cx, ry = (y - cy) / cy;
                    float edge = sheet ? Smooth(0.55f, 1.12f, (float)Math.Sqrt(rx * rx + ry * ry)) : 0.15f;
                    float stain = Smooth(0.62f, 0.8f, Noise(x / 260f + 41f, y / 260f + 9f)) * 0.08f;
                    float k = 1f + (n - 0.5f) * 0.12f + grain * 0.035f - edge * 0.26f - stain;
                    float warm = edge * 0.08f;   // edges browner, as old paper is
                    paper[y * w + x] = new Color32(B(Paper.r * k), B(Paper.g * k * (1f - warm * 0.5f)), B(Paper.b * k * (1f - warm)), 255);
                }
            return paper;
        }

        private static float Smooth(float a, float b, float v)
        {
            float t = Math.Max(0f, Math.Min(1f, (v - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }

        /// <summary>A frame of carved lines round the picture, with knots in its corners and small diamonds along it.</summary>
        public static void Frame(MapCanvas c)
        {
            float w = c.W, h = c.H;
            c.Rect(0, 0, w, 7, Ink, 0.85f); c.Rect(0, h - 7, w, h, Ink, 0.85f);
            c.Rect(0, 0, 7, h, Ink, 0.85f); c.Rect(w - 7, 0, w, h, Ink, 0.85f);
            float i = 13f;
            c.Outline(new[] { new Vector2(i, i), new Vector2(w - i, i), new Vector2(w - i, h - i), new Vector2(i, h - i) }, 2f, Ink, 0.8f);
            float j = 18f;
            c.Outline(new[] { new Vector2(j, j), new Vector2(w - j, j), new Vector2(w - j, h - j), new Vector2(j, h - j) }, 1f, Ink, 0.5f);
            foreach (Vector2 k in new[] { new Vector2(i, i), new Vector2(w - i, i), new Vector2(w - i, h - i), new Vector2(i, h - i) })
            {
                Vector2[] dia = { k + new Vector2(0, 9), k + new Vector2(9, 0), k - new Vector2(0, 9), k - new Vector2(9, 0) };
                c.Poly(dia, Paper);
                c.Outline(dia, 2f, Ink, 0.9f);
                c.Disc(k, 2.2f, Ink);
            }
            for (float t = 80f; t < w - 60f; t += 80f)
                foreach (float y in new[] { i, h - i })
                {
                    Vector2 o = new Vector2(t, y);
                    c.Poly(new[] { o + new Vector2(0, 4), o + new Vector2(4, 0), o - new Vector2(0, 4), o - new Vector2(4, 0) }, Ink, 0.8f);
                }
            for (float t = 80f; t < h - 60f; t += 80f)
                foreach (float x in new[] { i, w - i })
                {
                    Vector2 o = new Vector2(x, t);
                    c.Poly(new[] { o + new Vector2(0, 4), o + new Vector2(4, 0), o - new Vector2(0, 4), o - new Vector2(4, 0) }, Ink, 0.8f);
                }
        }

        private static byte B(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : v);

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        private static float Noise(float x, float y)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }
    }
}
