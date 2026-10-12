using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The dungeon map's view of a layout (MapWindow.cs shows it, MapDraw.cs draws it): its floors, which rooms stand on each, and where each
    /// doorway leads. Plain arithmetic on the layout (no game objects, no engine maths), so tools/dungeons/map draws the same maps outside
    /// the game.
    ///
    /// The floors come from the doorways: every doorway is a metre or so above the floor it opens on, and rooms only join at doorways, so
    /// the heights doorways are at (within Cluster of each other) are the dungeon's floors, lowest first. A point is on the highest floor
    /// whose doorways are at most Feet above it (a player's feet, a lever's foot). A room is on every floor from its own (its floor: the
    /// bottom of its box for our rooms, its lowest doorway for the game's, whose boxes are rougher) up to the highest it reaches: the last
    /// floor whose floor height is under its ceiling (our rooms), or its highest doorway. So the 48 m hall of Shifting Halls is on its
    /// four storeys, a tall stair room on both of the floors it joins, and a room 24 m tall with doorways only at the bottom shows on the
    /// floor above as the open space it is (drawn hatched: "the upper part of a taller room").
    /// </summary>
    internal sealed class MapLayout
    {
        public const float Cluster = 2.5f;   // doorways closer than this in height are on one floor
        public const float Feet = 2f;        // a point up to this far below a floor's doorways is on it
        private const float Headroom = 2.5f; // a room reaches a floor only if its ceiling is at least this far above that floor's doorways

        public readonly Layout Layout;
        public readonly List<float> Levels = new List<float>();   // each floor's doorway height (dungeon frame), lowest first
        public readonly Bounds[] Boxes;      // each room's box in the dungeon's frame (turned by its yaw: quarter turns only)
        public readonly int[] Base, Top;     // each room's lowest and highest floor
        public readonly int[][] DoorFloor;   // [room][doorway]: the floor it opens on
        public readonly int[][] LinkRoom;    // [room][doorway]: the room it joins (-1: none: sealed, or the way in)
        public readonly int[][] LinkDoor;    // [room][doorway]: the doorway it joins there
        public readonly bool[][] Locked;     // [room][doorway]: a plan step's lock (a gate the plan put there)
        public readonly int EntranceDoor;    // room 0's way in (-1: none)
        public readonly List<Flight> Flights = new List<Flight>();   // the fixed stairs in our rooms
        public int[][] DoorMark;             // [room][doorway]: +1 the room leads up from it, -1 down, 2 both, 0 neither
        public readonly List<Drop> Drops = new List<Drop>();         // the pack's declared drops, one per edge dropped from
        public readonly List<Breakable> Breakables = new List<Breakable>();   // walls to break, and the secret rooms behind them
        public readonly List<Stash> Stashes = new List<Stash>();               // walls to break in a room, and what they hide in it
        public readonly List<Shortcut> Shortcuts = new List<Shortcut>();      // shortcut doors, in pairs
        public readonly List<KeyValuePair<int, MechDef>> Bosses = new List<KeyValuePair<int, MechDef>>();   // boss fights (room, encounter)
        public readonly List<KeyValuePair<int, MechDef>> Teleporters = new List<KeyValuePair<int, MechDef>>();   // (room, teleporter)

        public int Floors => Levels.Count;

        public MapLayout(Layout layout)
        {
            Layout = layout;
            int n = layout.Rooms.Count;
            Boxes = new Bounds[n];
            Base = new int[n];
            Top = new int[n];
            DoorFloor = new int[n][];
            LinkRoom = new int[n][];
            LinkDoor = new int[n][];
            Locked = new bool[n][];

            // the floors: doorway heights, gathered into clusters from the lowest up
            var heights = new List<float>();
            foreach (PlacedRoom r in layout.Rooms)
                for (int d = 0; d < r.Def.Doorways.Count; d++) heights.Add(r.DoorwayPos(d).y);
            heights.Sort();
            int start = 0;
            for (int i = 1; i <= heights.Count; i++)
            {
                if (i < heights.Count && heights[i] - heights[start] <= Cluster) continue;
                Levels.Add(heights.Skip(start).Take(i - start).Average());
                start = i;
            }
            if (Levels.Count == 0) Levels.Add(0f);

            foreach (PlacedRoom r in layout.Rooms)
            {
                int i = r.Index, doors = r.Def.Doorways.Count;
                Vector3 size = r.Def.Size;
                if (Quarter(r.Yaw) % 2 == 1) size = new Vector3(size.z, size.y, size.x);
                Boxes[i] = new Bounds(r.Pos, size);
                DoorFloor[i] = new int[doors];
                LinkRoom[i] = Enumerable.Repeat(-1, doors).ToArray();
                LinkDoor[i] = Enumerable.Repeat(-1, doors).ToArray();
                Locked[i] = new bool[doors];
                int low = int.MaxValue, high = int.MinValue;
                for (int d = 0; d < doors; d++)
                {
                    int f = Nearest(r.DoorwayPos(d).y);
                    DoorFloor[i][d] = f;
                    low = Math.Min(low, f);
                    high = Math.Max(high, f);
                }
                if (r.Def.GamePrefab == null && size.y > 0.01f)
                {
                    // our own rooms: the box is the room (its floor is its bottom)
                    Base[i] = FloorOf(Boxes[i].min.y + Headroom);   // (a little up from the bottom: a pit or footing below the floor isn't a floor)
                    int reach = Base[i];
                    while (reach + 1 < Levels.Count && Levels[reach + 1] + Headroom <= Boxes[i].max.y) reach++;
                    Top[i] = Math.Max(reach, high == int.MinValue ? reach : high);
                    if (low != int.MaxValue) Base[i] = Math.Min(Base[i], low);
                }
                else
                {
                    Base[i] = low != int.MaxValue ? low : FloorOf(r.Pos.y);
                    Top[i] = high != int.MinValue ? high : Base[i];
                }
            }
            foreach (Link l in layout.Links)
            {
                if (!Valid(l.Room, l.Doorway) || !Valid(l.OtherRoom, l.OtherDoorway)) continue;
                LinkRoom[l.Room][l.Doorway] = l.OtherRoom;
                LinkDoor[l.Room][l.Doorway] = l.OtherDoorway;
                LinkRoom[l.OtherRoom][l.OtherDoorway] = l.Room;
                LinkDoor[l.OtherRoom][l.OtherDoorway] = l.Doorway;
            }
            foreach (Link l in layout.Locks)
            {
                if (Valid(l.Room, l.Doorway)) Locked[l.Room][l.Doorway] = true;
                if (Valid(l.OtherRoom, l.OtherDoorway)) Locked[l.OtherRoom][l.OtherDoorway] = true;
            }
            EntranceDoor = Layouts.EntranceDoorway(layout);
            FindFlights();
            FindDoorMarks();
            FindDrops();
            FindSecrets();
        }

        private bool Valid(int room, int door) => room >= 0 && room < Layout.Rooms.Count && door >= 0 && door < Layout.Rooms[room].Def.Doorways.Count;

        /// <summary>The floor a point (dungeon frame) is on: the highest whose doorways are at most Feet above it (the lowest if none).</summary>
        public int FloorOf(float y)
        {
            int f = 0;
            for (int i = 0; i < Levels.Count; i++)
                if (Levels[i] - Feet <= y) f = i;
            return f;
        }

        private int Nearest(float y)
        {
            int best = 0;
            for (int i = 1; i < Levels.Count; i++)
                if (Math.Abs(Levels[i] - y) < Math.Abs(Levels[best] - y)) best = i;
            return best;
        }

        /// <summary>An end cap that is only a wall (no floor to it: the game's, and our flat ones). End caps with room in them (a nook, a
        /// treasury) are rooms.</summary>
        public bool IsWall(int room)
        {
            RoomDef def = Layout.Rooms[room].Def;
            return def.EndCap && (def.Size.x < 1f || def.Size.z < 1f);
        }

        /// <summary>A room shows on this floor (a wall never does).</summary>
        public bool On(int room, int floor) => !IsWall(room) && floor >= Base[room] && floor <= Top[room];

        /// <summary>Joined to a room that is a room (not a wall): a way through.</summary>
        public bool Open(int room, int door)
        {
            int other = LinkRoom[room][door];
            return other >= 0 && !IsWall(other);
        }

        /// <summary>The room a point (dungeon frame) is in, or -1.</summary>
        public int RoomAt(Vector3 p)
        {
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (IsWall(i)) continue;
                Bounds b = Boxes[i];
                if (p.x >= b.min.x - 0.25f && p.x <= b.max.x + 0.25f && p.z >= b.min.z - 0.25f && p.z <= b.max.z + 0.25f
                    && p.y >= b.min.y - 1f && p.y <= b.max.y + 0.5f) return i;
            }
            return -1;
        }

        /// <summary>A mechanism's place in the dungeon's frame (its room's position and turn: quarter turns, exactly).</summary>
        public Vector3 MechAt(int room, MechDef m)
        {
            PlacedRoom r = Layout.Rooms[room];
            return r.Pos + Layouts.Turn(m.At, Quarter(r.Yaw));
        }

        /// <summary>Where a mechanism's moving part stands in a state (dungeon frame): its pose's place, turned with it and its room (a
        /// pushblock's cell).</summary>
        public Vector3 PoseAt(int room, MechDef m, int state)
        {
            Vector3 at = MechAt(room, m);
            if (m.Poses.Count == 0) return at;
            Pose pose = m.Poses[Math.Max(0, Math.Min(m.Poses.Count - 1, state))];
            float yaw = (Layout.Rooms[room].Yaw + m.Euler.y) * Mathf.Deg2Rad;
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            return at + new Vector3(pose.At.x * c + pose.At.z * s, pose.At.y, -pose.At.x * s + pose.At.z * c);
        }

        /// <summary>A mechanism's turn about the up axis in a state, in degrees (its room's, its own, and its pose's in that state).</summary>
        public float MechYaw(int room, MechDef m, int state)
        {
            float yaw = Layout.Rooms[room].Yaw + m.Euler.y;
            if (m.Poses.Count > 0) yaw += m.Poses[Math.Max(0, Math.Min(m.Poses.Count - 1, state))].Euler.y;
            return yaw;
        }

        /// <summary>A doorway by its id in a room (-1: none).</summary>
        public int Door(int room, string id) => string.IsNullOrEmpty(id) ? -1 : Layout.Rooms[room].Def.Doorways.FindIndex(d => d.Id == id);

        /// <summary>The way out of a room through a doorway (a horizontal unit vector; Doorway.Facing points into the room).</summary>
        public Vector3 Outward(int room, int door)
        {
            Vector3 f = -Layout.Rooms[room].DoorwayFacing(door);
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.zero;
        }

        /// <summary>The vault: the room of the plan's "end" step (or any room tagged "end").</summary>
        public bool IsVault(int room)
        {
            PlacedRoom r = Layout.Rooms[room];
            if (r.Def.Tags.Any(t => string.Equals(t, "end", StringComparison.OrdinalIgnoreCase))) return true;
            PlanStep step = r.Step == null ? null : Layout.Pack?.Plan.FirstOrDefault(s => Layouts.StepName(s) == r.Step);
            return step != null && string.Equals(step.Tag, "end", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The rooms' footprints (x, z) together: what a map of these rooms must hold. False when there are none.</summary>
        public bool Extent(bool[] shown, out Vector2 min, out Vector2 max) => Extent(shown, -1, out min, out max);

        /// <summary>The same, of the rooms on one floor only (-1: every floor).</summary>
        public bool Extent(bool[] shown, int floor, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            for (int i = 0; i < Boxes.Length; i++)
            {
                if (shown != null && (i >= shown.Length || !shown[i])) continue;
                if (IsWall(i) || (floor >= 0 && !On(i, floor))) continue;
                min = Vector2.Min(min, new Vector2(Boxes[i].min.x, Boxes[i].min.z));
                max = Vector2.Max(max, new Vector2(Boxes[i].max.x, Boxes[i].max.z));
                any = true;
            }
            return any;
        }

        // ---- the ways between floors that don't move

        /// <summary>A flight of fixed stairs in one of our rooms: steps (parts named like a stair, step or ladder) one after another, each
        /// a little higher and close beside the last. Low and High are its lowest and highest steps' middles (dungeon frame).</summary>
        internal sealed class Flight
        {
            public int Room, Steps;
            public Vector3 Low, High, Up;    // Up: the way up it, flat
            public float Width, Step;        // across, metres; one step's depth along it
            public int LowFloor, HighFloor;
        }

        /// <summary>A declared drop (the pack's "drops": jumping off a balcony, into a pit): from the doorway A of a room, towards where it
        /// lands. One per edge dropped from.</summary>
        internal sealed class Drop
        {
            public int Room, From, Floor;
            public Vector3 At, Toward;       // the edge (just inside its doorway), and the way down from it (flat)
        }

        private static bool IsStep(string prefab)
        {
            string n = (prefab ?? "").ToLowerInvariant();
            return n.Contains("stair") || n.Contains("step") || n.Contains("ladder");
        }

        private void FindFlights()
        {
            foreach (PlacedRoom r in Layout.Rooms)
            {
                if (r.Def.GamePrefab != null || r.Def.Parts == null) continue;
                int q = Quarter(r.Yaw);
                Flight f = null;
                Vector3 last = default;
                PartDef lastPart = null;
                foreach (PartDef part in r.Def.Parts)
                {
                    if (!IsStep(part.Prefab)) continue;
                    Vector3 at = r.Pos + Layouts.Turn(part.At, q);
                    float dy = f != null ? at.y - last.y : 0f;
                    float flat = f != null ? new Vector2(at.x - last.x, at.z - last.z).magnitude : 0f;
                    bool next = f != null && lastPart != null && part.Euler == lastPart.Euler && part.Scale == lastPart.Scale
                                && Math.Abs(dy) > 0.05f && Math.Abs(dy) <= 1.6f && flat <= 3.5f && flat > 0.05f;
                    if (next && f.Steps > 1 && Math.Sign(dy) != Math.Sign(f.High.y - f.Low.y)) next = false;   // (turned back: a new flight)
                    if (!next)
                    {
                        if (f != null) Add(f);
                        f = new Flight { Room = r.Index, Steps = 1, Low = at, High = at };
                        // across: the piece's own x (two metres a piece, scaled); up: against its own forward, as the game's stair pieces
                        // climb (taken from the steps themselves when there are two or more)
                        float yaw = (r.Yaw + part.Euler.y) * Mathf.Deg2Rad;
                        f.Up = -new Vector3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));
                        f.Width = 2f * Math.Abs(part.Scale.x);
                        f.Step = 2f * Math.Abs(part.Scale.z);
                    }
                    else
                    {
                        f.Steps++;
                        f.Step = flat;
                        if (at.y < f.Low.y) f.Low = at; else f.High = at;
                    }
                    last = at;
                    lastPart = part;
                }
                if (f != null) Add(f);
            }
        }

        private void Add(Flight f)
        {
            if (f.Steps > 1)
            {
                Vector3 up = f.High - f.Low;
                up.y = 0f;
                if (up.sqrMagnitude > 0.01f) f.Up = up.normalized;
            }
            f.LowFloor = FloorOf(f.Low.y + 0.5f);
            f.HighFloor = FloorOf(f.High.y + 1.5f);   // (its top: the top step's surface, a metre up, and a little)
            Flights.Add(f);
        }

        /// <summary>Doorways of a room that joins floors with no moving part (a stair room's top and bottom, a game room whose doorways are on
        /// two floors): the room leads up from one on a lower floor, down from one on a higher. Rooms whose ways through are their
        /// mechanisms' (the hall's turning stairs) show those instead.</summary>
        private void FindDoorMarks()
        {
            DoorMark = new int[Layout.Rooms.Count][];
            foreach (PlacedRoom r in Layout.Rooms)
            {
                int i = r.Index, n = r.Def.Doorways.Count;
                DoorMark[i] = new int[n];
                if (IsWall(i) || r.Def.Mechs.Any(m => m.Joins.Count > 0)) continue;
                for (int d = 0; d < n; d++)
                {
                    if (!Way(i, d)) continue;
                    bool up = false, down = false;
                    for (int e = 0; e < n; e++)
                    {
                        if (e == d || !Way(i, e)) continue;
                        if (DoorFloor[i][e] > DoorFloor[i][d]) up = true;
                        if (DoorFloor[i][e] < DoorFloor[i][d]) down = true;
                    }
                    DoorMark[i][d] = up && down ? 2 : up ? 1 : down ? -1 : 0;
                }
            }
        }

        /// <summary>A doorway that leads somewhere: to another room, or out (the entrance room's way in).</summary>
        public bool Way(int room, int door) => Open(room, door) || (room == 0 && door == EntranceDoor);

        private void FindDrops()
        {
            foreach (PlacedRoom r in Layout.Rooms)
            {
                List<Join> drops = PackReader.Drops(Layout.Pack, r.Def);
                foreach (IGrouping<string, Join> from in drops.GroupBy(j => j.A))
                {
                    int a = Door(r.Index, from.Key);
                    if (a < 0) continue;
                    Vector3 edge = r.DoorwayPos(a) + Flatten(r.DoorwayFacing(a)) * 1.5f;
                    // towards where it lands: the doorways dropped to, together (else the room's middle)
                    var lands = from.Select(j => Door(r.Index, j.B)).Where(b => b >= 0).Select(b => r.DoorwayPos(b)).ToList();
                    Vector3 to = lands.Count > 0 ? new Vector3(lands.Average(v => v.x), 0f, lands.Average(v => v.z)) : Boxes[r.Index].center;
                    var toward = new Vector3(to.x - edge.x, 0f, to.z - edge.z);
                    if (toward.sqrMagnitude < 1f) toward = new Vector3(Boxes[r.Index].center.x - edge.x, 0f, Boxes[r.Index].center.z - edge.z);
                    if (toward.sqrMagnitude < 0.01f) toward = Flatten(r.DoorwayFacing(a));
                    Drops.Add(new Drop { Room = r.Index, From = a, Floor = DoorFloor[r.Index][a], At = edge, Toward = toward.normalized });
                }
            }
        }

        private static Vector3 Flatten(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }

        // ---- secrets, shortcuts and the boss
        // (Read here only, so the map follows the model in one place. Mechanisms of kind "breakable": a wall over its Doorway, in the secret
        // room (tagged "secret") behind it; broken once it has a record (MechSet.IsBroken). Kind "shortcut": a door at its Doorway or where it
        // stands; the two of a pair share MechDef.Pair, the far one is Deep; open once either has a record (MechSet.ShortcutOpen). A boss: an
        // encounter with EncounterDef.Boss, or any encounter in a room tagged "boss".)

        /// <summary>A breakable wall over a doorway, and the secret room behind it: the one of the two farther from the entrance.</summary>
        internal sealed class Breakable
        {
            public int Room, Door, OtherRoom, OtherDoor, Secret;
            public MechDef Mech;
        }

        /// <summary>A breakable wall inside a room (no doorway: a closet walled up), and where the things it hides stand (dungeon frame):
        /// the room's parts whose "hiddenBy" names it, or (a pack from before that field) its saved parts within 3 m of it.</summary>
        internal sealed class Stash
        {
            public int Room;
            public MechDef Mech;
            public readonly List<Vector3> Hidden = new List<Vector3>();
            public readonly List<MechDef> Mechs = new List<MechDef>();   // its room's mechanisms behind it ("hiddenBy" on a mechanism)
        }

        /// <summary>A pair of shortcut doors (or a lone one): its ends, the first the one nearer the entrance (shown only once it's open).</summary>
        internal sealed class Shortcut
        {
            public readonly List<KeyValuePair<int, MechDef>> Ends = new List<KeyValuePair<int, MechDef>>();
        }

        private static bool BossRoom(PlacedRoom r) => r.Def.Tags.Any(t => string.Equals(t, "boss", StringComparison.OrdinalIgnoreCase))
                                                     || string.Equals(r.Step, "boss", StringComparison.OrdinalIgnoreCase);

        /// <summary>The room something in this one is seen from: itself, or for a wall (an end cap holding a shortcut's door), the room it
        /// closes.</summary>
        public int SeenFrom(int room) => IsWall(room) && LinkRoom[room].Length > 0 && LinkRoom[room][0] >= 0 ? LinkRoom[room][0] : room;

        public static bool IsBreakable(MechDef m) => m != null && m.Kind == "breakable";
        public static bool IsShortcut(MechDef m) => m != null && m.Kind == "shortcut";

        private void FindSecrets()
        {
            var shortcuts = new List<KeyValuePair<int, MechDef>>();
            foreach (PlacedRoom r in Layout.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                {
                    if (IsBreakable(m))
                    {
                        int d = Door(r.Index, m.Doorway);
                        int other = d >= 0 ? LinkRoom[r.Index][d] : -1;
                        if (d < 0) { AddStash(r, m); continue; }
                        if (other < 0) continue;
                        // the room tagged "secret" (Shifting Halls puts the wall in the secret room's own doorway); untagged, the room across
                        // the doorway, as runtime places it
                        int secret = r.Def.HiddenBy == m.Id ? r.Index : PackReader.Secret(r.Def) ? r.Index : other;
                        Breakables.Add(new Breakable { Room = r.Index, Door = d, OtherRoom = other, OtherDoor = LinkDoor[r.Index][d], Secret = secret, Mech = m });
                    }
                    else if (IsShortcut(m)) shortcuts.Add(new KeyValuePair<int, MechDef>(r.Index, m));
                    else if (m.Kind == "teleporter") Teleporters.Add(new KeyValuePair<int, MechDef>(r.Index, m));
                    else if (m.Kind == "encounter" && (m.Encounter?.Boss != null || BossRoom(r)))
                        Bosses.Add(new KeyValuePair<int, MechDef>(r.Index, m));
                }
            // a boss room with no fight of its own: its mark in the room's middle (never "defeated": nothing says so)
            foreach (PlacedRoom r in Layout.Rooms)
                if (BossRoom(r) && !Bosses.Any(b => b.Key == r.Index)) Bosses.Add(new KeyValuePair<int, MechDef>(r.Index, null));
            var paired = new HashSet<string>();
            foreach (KeyValuePair<int, MechDef> end in shortcuts)
            {
                string key = end.Key + "." + end.Value.Id;
                if (paired.Contains(key)) continue;
                var pair = new Shortcut();
                pair.Ends.Add(end);
                paired.Add(key);
                foreach (KeyValuePair<int, MechDef> other in shortcuts)
                {
                    string k = other.Key + "." + other.Value.Id;
                    if (paired.Contains(k)) continue;
                    string mine = string.IsNullOrEmpty(end.Value.Pair) ? end.Value.Id : end.Value.Pair;
                    string theirs = string.IsNullOrEmpty(other.Value.Pair) ? other.Value.Id : other.Value.Pair;
                    if (mine == theirs) { pair.Ends.Add(other); paired.Add(k); }
                }
                // the entrance end first: the one that isn't Deep (else the one nearer the entrance)
                pair.Ends.Sort((a, b) => a.Value.Deep != b.Value.Deep ? a.Value.Deep.CompareTo(b.Value.Deep)
                                                                       : Layout.Rooms[a.Key].Depth.CompareTo(Layout.Rooms[b.Key].Depth));
                Shortcuts.Add(pair);
            }
        }

        private void AddStash(PlacedRoom r, MechDef m)
        {
            var stash = new Stash { Room = r.Index, Mech = m };
            int q = Quarter(r.Yaw);
            Vector3 wall = MechAt(r.Index, m);
            bool named = r.Def.Parts.Any(pt => pt.HiddenBy == m.Id) || r.Def.Mechs.Any(x => x.HiddenBy == m.Id);
            foreach (MechDef x in r.Def.Mechs)
                if (x.HiddenBy == m.Id) stash.Mechs.Add(x);
            foreach (PartDef pt in r.Def.Parts)
            {
                Vector3 at = r.Pos + Layouts.Turn(pt.At, q);
                if (named ? pt.HiddenBy == m.Id : pt.Saved && (at - wall).sqrMagnitude <= 3f * 3f) stash.Hidden.Add(at);
            }
            Stashes.Add(stash);
        }

        /// <summary>Where a shortcut or breakable shows: just inside its doorway, or where it stands. In a wall (a shortcut's door is a flat
        /// end cap): against it, in the room it closes.</summary>
        public Vector3 MarkAt(int room, MechDef m, out int floor)
        {
            PlacedRoom r = Layout.Rooms[room];
            int d = Door(room, m.Doorway);
            if (IsWall(room) && r.Def.Doorways.Count > 0)
            {
                if (d < 0) d = 0;
                floor = DoorFloor[room][d];
                return r.DoorwayPos(d) - Flatten(r.DoorwayFacing(d)) * 1.6f;   // (facing points into the cap: back, into the room)
            }
            if (d >= 0) { floor = DoorFloor[room][d]; return r.DoorwayPos(d) + Flatten(r.DoorwayFacing(d)) * 1.6f; }
            Vector3 at = MechAt(room, m);
            floor = FloorOf(at.y + 0.5f);
            return at;
        }

        internal static int Quarter(float yaw) => ((int)Math.Round(yaw / 90f) % 4 + 4) % 4;
    }
}
