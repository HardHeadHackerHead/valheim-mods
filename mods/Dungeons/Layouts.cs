using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The layout builder: from a pack and a seed, the same dungeon every time (docs/dungeons-design.md, "The layout builder").
    ///
    /// How a layout grows, the way the game's DungeonGenerator does it but with a plan:
    ///   1. the entrance room at the origin, turned 0, its way in (the doorway the game marks as the entrance) towards -z;
    ///   2. the spine: from the entrance, rooms one after another, and the plan's steps on it in order (each step's room joins the room
    ///      before it, after a few rooms in between), so every step lies on the way to the next one;
    ///   3. branches: side rooms on any open doorway until the room count is reached (never past the last step: the end stays the end);
    ///   4. closing: two open doorways that touch (a loop) are joined, as the game does; every other doorway gets an end cap of its type;
    ///      one no cap fits is left "sealed" (the game logs "Failed to place end cap" for those);
    ///   5. checking: each step only through the step before it, every lock only through its gate, and then the puzzle solved
    ///      (LayoutSolver.cs): from the entrance, using levers and plates, the end is reached, in at least the pack's "minUses" uses;
    ///   6. doors: joints where both doorways allow one, by the pack's DoorChance (a random stream of their own).
    /// A try that fails retries with a seed made from the first, a few times, then gives back the last try with its Problems.
    ///
    /// Locks: a plan step with OpensWith is entered through a gate. Its room's own gate (a gate or door mechanism with a "doorway") if it
    /// has one, else the pack's "gates" pattern for that doorway's type, added to that placed room (PlacedRoom.Def is then a copy of its
    /// RoomDef, with the gate in its Mechs: everyone reads a placed room's mechanisms from r.Def.Mechs). The opening step's room must
    /// hold a lever or plate; if none of its wires reaches the gate, its first lever gets one ("step:<locked step>", "set:1").
    ///
    /// Rooms join where doorways of the same type meet, at the same place, facing opposite ways (Doorway.Facing points into its room),
    /// and their boxes (Room.m_size, centred on the room) may not overlap, less a small margin (the game shrinks the new box by 0.1 m).
    /// Rooms only turn in quarter turns (all the game's doorways face along the axes), and every position is snapped to 1 cm, with exact
    /// integer turns and no engine maths, so a layout is the same on every machine (and in tools/dungeons, outside the game).
    /// It never uses UnityEngine.Random: the generator is our own (Rng, below).
    /// </summary>
    internal static partial class Layouts
    {
        public const int Tries = 12;            // seeds tried before giving up
        private const float Margin = 0.05f;     // boxes may overlap this much on an axis (the game: its new box 0.1 m smaller)
        private const float Near = 0.05f;       // doorways closer than this are at the same place

        /// <summary>Lays out a dungeon. Never throws: a seed that can't make a good dungeon returns a Layout with Problems.</summary>
        public static Layout Make(Pack pack, int seed) => Make(pack, seed, out _);

        /// <summary>The same, and how many tries it took (tools/dungeons and `dungeon seeds` count them).</summary>
        public static Layout Make(Pack pack, int seed, out int tries)
        {
            tries = 0;
            Layout last = null;
            for (int t = 0; t < Tries; t++)
            {
                tries = t + 1;
                last = Try(pack, seed, t);
                if (last.Ok) return last;
                if (pack == null || pack.Rooms.Count == 0) break;
            }
            last.Problems.Insert(0, $"no good layout in {tries} tries; the last one's problems:");
            return last;
        }

        /// <summary>One try (0 is the seed itself, the others seeds made from it), good or not: for testing the builder.</summary>
        public static Layout Try(Pack pack, int seed, int attempt)
        {
            try
            {
                return new Attempt(pack, seed, attempt == 0 ? seed : Derive(seed, attempt)).Run();
            }
            catch (Exception e)
            {
                var failed = new Layout { Pack = pack, Seed = seed };
                failed.Problems.Add("error: " + e.GetType().Name + ": " + e.Message);
                return failed;
            }
        }

        /// <summary>The entrance room's way in (room 0's doorway marked as the entrance): where a player arrives, facing into the room
        /// (PlacedRoom.DoorwayFacing). -1 if the room has none.</summary>
        public static int EntranceDoorway(Layout layout)
        {
            if (layout == null || layout.Rooms.Count == 0) return -1;
            List<Doorway> doors = layout.Rooms[0].Def.Doorways;
            for (int i = 0; i < doors.Count; i++)
                if (doors[i].Entrance) return i;
            return -1;
        }

        /// <summary>Doorways nothing joins and no end cap closes (room, doorway): the builder should wall them up.</summary>
        public static List<KeyValuePair<int, int>> Sealed(Layout layout)
        {
            var joined = new HashSet<long>();
            foreach (Link l in layout.Links) { joined.Add(Key(l.Room, l.Doorway)); joined.Add(Key(l.OtherRoom, l.OtherDoorway)); }
            var list = new List<KeyValuePair<int, int>>();
            foreach (PlacedRoom r in layout.Rooms)
                for (int d = 0; d < r.Def.Doorways.Count; d++)
                    if (!joined.Contains(Key(r.Index, d))) list.Add(new KeyValuePair<int, int>(r.Index, d));
            return list;
        }

        /// <summary>The key ("&lt;room&gt;.&lt;mech id&gt;") of the mechanism a wire of room's acts on, or null if there's none.
        /// "step:&lt;id&gt;" is the room the plan put that step in: the gate on its lock if it's locked, else its first mechanism that
        /// isn't a lever or plate; "step:&lt;id&gt;.&lt;mech&gt;" names one. Anything else is a mechanism in the same room.</summary>
        public static string Target(Layout layout, int room, Wire wire)
        {
            if (layout == null || wire == null || string.IsNullOrEmpty(wire.Target) || room < 0 || room >= layout.Rooms.Count) return null;
            string t = wire.Target;
            if (!t.StartsWith("step:")) return layout.Rooms[room].Def.Mechs.Any(m => m.Id == t) ? room + "." + t : null;
            string step = t.Substring(5), mech = null;
            int dot = step.IndexOf('.');
            if (dot >= 0) { mech = step.Substring(dot + 1); step = step.Substring(0, dot); }
            int r = layout.Rooms.FindIndex(p => p.Step == step);
            if (r < 0) return null;
            List<MechDef> mechs = layout.Rooms[r].Def.Mechs;
            if (mech != null) return mechs.Any(m => m.Id == mech) ? r + "." + mech : null;
            foreach (Link l in layout.Locks)
            {
                if (l.Room != r) continue;
                string door = layout.Rooms[r].Def.Doorways[l.Doorway].Id;
                MechDef gate = mechs.FirstOrDefault(m => IsGate(m) && m.Doorway == door);
                if (gate != null) return r + "." + gate.Id;
            }
            MechDef first = mechs.FirstOrDefault(m => !IsUsable(m));
            return first != null ? r + "." + first.Id : null;
        }

        /// <summary>A mechanism by its key, or null: (room, mechanism).</summary>
        public static MechDef Mech(Layout layout, string key, out int room)
        {
            room = -1;
            int dot = key?.IndexOf('.') ?? -1;
            if (dot <= 0 || !int.TryParse(key.Substring(0, dot), out int r) || r < 0 || r >= layout.Rooms.Count) return null;
            string id = key.Substring(dot + 1);
            MechDef m = layout.Rooms[r].Def.Mechs.FirstOrDefault(x => x.Id == id);
            if (m != null) room = r;
            return m;
        }

        /// <summary>The rooms in a layout that aren't end caps (what MinRooms and MaxRooms count).</summary>
        public static int RoomCount(Layout layout) => layout.Rooms.Count(r => !r.Def.EndCap);

        /// <summary>What a layout is, for a command's answer: rooms (name, where, depth, step), links, locks, size, problems.</summary>
        public static JObject Describe(Layout layout)
        {
            var o = new JObject
            {
                ["pack"] = layout.Pack?.Name,
                ["seed"] = layout.Seed,
                ["ok"] = layout.Ok,
            };
            if (!layout.Ok) o["problems"] = new JArray(layout.Problems);
            o["rooms"] = RoomCount(layout);
            o["endCaps"] = layout.Rooms.Count - RoomCount(layout);
            o["size"] = V(layout.Box.size);
            o["box"] = new JObject { ["min"] = V(layout.Box.min), ["max"] = V(layout.Box.max) };
            o["entrance"] = new JObject { ["room"] = 0, ["doorway"] = EntranceDoorway(layout) };

            var other = new Dictionary<long, string>();
            foreach (Link l in layout.Links)
            {
                other[Key(l.Room, l.Doorway)] = l.OtherRoom + "." + l.OtherDoorway;
                other[Key(l.OtherRoom, l.OtherDoorway)] = l.Room + "." + l.Doorway;
            }
            var rooms = new JArray();
            foreach (PlacedRoom r in layout.Rooms)
            {
                var doors = new JArray();
                for (int d = 0; d < r.Def.Doorways.Count; d++)
                    doors.Add(other.TryGetValue(Key(r.Index, d), out string to) ? to : "sealed");
                var room = new JObject
                {
                    ["i"] = r.Index,
                    ["name"] = r.Def.Name,
                    ["at"] = V(r.Pos),
                    ["yaw"] = r.Yaw,
                    ["depth"] = r.Depth,
                };
                if (r.Step != null) room["step"] = r.Step;
                if (r.Def.EndCap) room["endCap"] = true;
                room["doorways"] = doors;   // each: "room.doorway" it joins, or "sealed"
                rooms.Add(room);
            }
            o["placed"] = rooms;
            // A lock: the room behind it and its doorway, the doorway it's opened from, its step and the step that opens it.
            o["locks"] = new JArray(layout.Locks.Select(l => new JObject
            {
                ["room"] = l.Room, ["doorway"] = l.Doorway, ["from"] = l.OtherRoom + "." + l.OtherDoorway,
                ["step"] = layout.Rooms[l.Room].Step,
                ["opensWith"] = layout.Pack.Plan.FirstOrDefault(s => StepName(s) == layout.Rooms[l.Room].Step)?.OpensWith,
            }));
            List<KeyValuePair<int, int>> sealedDoors = Sealed(layout);
            if (sealedDoors.Count > 0) o["sealed"] = new JArray(sealedDoors.Select(p => p.Key + "." + p.Value));
            // Mechanisms: key, kind, start, states, where (dungeon frame), the doorway a gate closes, what a lever's wires act on.
            var mechs = new JArray();
            foreach (PlacedRoom r in layout.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                {
                    var mo = new JObject
                    {
                        ["key"] = r.Index + "." + m.Id, ["kind"] = m.Kind, ["start"] = m.Start, ["states"] = States(m),
                        ["at"] = V(r.Pos + Turn(m.At, Quarter(r.Yaw))),
                    };
                    if (!string.IsNullOrEmpty(m.Doorway)) mo["doorway"] = m.Doorway;
                    if (m.Cycle > 0f) mo["cycle"] = m.Cycle;
                    if (m.Wires.Count > 0) mo["wires"] = new JArray(m.Wires.Select(w => (Target(layout, r.Index, w) ?? "?" + w.Target) + " " + w.Do));
                    if (!string.IsNullOrEmpty(m.ReachedFrom)) mo["reachedFrom"] = m.ReachedFrom;
                    mechs.Add(mo);
                }
            if (mechs.Count > 0) o["mechs"] = mechs;
            if (layout.Doors.Count > 0) o["doors"] = new JArray(layout.Doors.Select(l => l.Room + "." + l.Doorway));
            if (layout.Solution.Count > 0 || mechs.Count > 0) o["solution"] = new JArray(layout.Solution);
            return o;
        }

        /// <summary>The layout as text, one block per floor (seen from above, north up, 1 character pair = 2 m), for `dungeon map`. A floor
        /// is a height doorways are at (joined doorways share one); a room shows on every floor it has a doorway on.</summary>
        public static string Map(Layout layout)
        {
            var sb = new StringBuilder();
            sb.Append($"{layout.Pack?.Name} seed {layout.Seed}: {RoomCount(layout)} rooms, {layout.Rooms.Count - RoomCount(layout)} end caps, ")
              .Append($"{layout.Box.size.x:0}x{layout.Box.size.y:0}x{layout.Box.size.z:0} m");
            sb.Append(layout.Ok ? "\n" : "\nPROBLEMS:\n  " + string.Join("\n  ", layout.Problems) + "\n");
            if (layout.Rooms.Count == 0) return sb.ToString();

            // The floors: the heights doorways are at (joined doorways share one), highest first.
            var levels = new List<float>();
            foreach (PlacedRoom r in layout.Rooms)
                for (int d = 0; d < r.Def.Doorways.Count; d++)
                {
                    float y = r.DoorwayPos(d).y;
                    if (!levels.Any(l => Math.Abs(l - y) < 1f)) levels.Add(y);
                }
            levels.Sort((a, b) => b.CompareTo(a));

            var joined = new HashSet<long>();
            foreach (Link l in layout.Links) { joined.Add(Key(l.Room, l.Doorway)); joined.Add(Key(l.OtherRoom, l.OtherDoorway)); }
            var locked = new HashSet<long>();
            foreach (Link l in layout.Locks) { locked.Add(Key(l.Room, l.Doorway)); locked.Add(Key(l.OtherRoom, l.OtherDoorway)); }
            int way = EntranceDoorway(layout);

            const float cell = 2f;
            Vector3 min = layout.Box.min;
            int w = Math.Max(1, (int)Math.Ceiling(layout.Box.size.x / cell)), h = Math.Max(1, (int)Math.Ceiling(layout.Box.size.z / cell));
            for (int f = 0; f < levels.Count; f++)
            {
                float level = levels[f];
                var grid = new char[h, w];
                for (int z = 0; z < h; z++) for (int x = 0; x < w; x++) grid[z, x] = ' ';
                foreach (PlacedRoom r in layout.Rooms)
                {
                    if (!Enumerable.Range(0, r.Def.Doorways.Count).Any(d => Math.Abs(r.DoorwayPos(d).y - level) < 1f)) continue;   // (not on this floor)
                    Box b = BoxOf(r.Def, r.Pos, Quarter(r.Yaw));
                    char c = r.Def.EndCap ? '#' : Symbol(r.Index);
                    for (int z = 0; z < h; z++)
                        for (int x = 0; x < w; x++)
                        {
                            float cx = min.x + (x + 0.5f) * cell, cz = min.z + (z + 0.5f) * cell;
                            if (cx > b.Min.x && cx < b.Max.x && cz > b.Min.z && cz < b.Max.z) grid[z, x] = c;
                        }
                }
                foreach (PlacedRoom r in layout.Rooms)
                    for (int d = 0; d < r.Def.Doorways.Count; d++)
                    {
                        Vector3 at = r.DoorwayPos(d);
                        if (Math.Abs(at.y - level) > 1f) continue;
                        at += r.DoorwayFacing(d) * 0.5f;   // just inside its room
                        int x = (int)Math.Floor((at.x - min.x) / cell), z = (int)Math.Floor((at.z - min.z) / cell);
                        if (x < 0 || z < 0 || x >= w || z >= h) continue;
                        long k = Key(r.Index, d);
                        char c = r.Index == 0 && d == way ? 'E' : locked.Contains(k) ? 'L' : r.Def.EndCap ? '#' : joined.Contains(k) ? '+' : '!';
                        if (grid[z, x] != 'E' && grid[z, x] != 'L') grid[z, x] = c;
                    }
                foreach (Link l in layout.Doors)
                {
                    PlacedRoom r = layout.Rooms[l.Room];
                    Vector3 at = r.DoorwayPos(l.Doorway);
                    if (Math.Abs(at.y - level) > 1f) continue;
                    at += r.DoorwayFacing(l.Doorway) * 0.5f;
                    int x = (int)Math.Floor((at.x - min.x) / cell), z = (int)Math.Floor((at.z - min.z) / cell);
                    if (x >= 0 && z >= 0 && x < w && z < h && grid[z, x] == '+') grid[z, x] = ':';
                }
                // Mechanisms, on the floor they stand on (the highest doorway height at or below them, else the lowest).
                foreach (PlacedRoom r in layout.Rooms)
                    foreach (MechDef m in r.Def.Mechs)
                    {
                        Vector3 at = r.Pos + Turn(m.At, Quarter(r.Yaw));
                        float on = levels.Where(l => l <= at.y + 1f).DefaultIfEmpty(levels[levels.Count - 1]).Max();
                        if (Math.Abs(on - level) > 0.01f) continue;
                        int x = (int)Math.Floor((at.x - min.x) / cell), z = (int)Math.Floor((at.z - min.z) / cell);
                        if (x >= 0 && z >= 0 && x < w && z < h) grid[z, x] = MechMark(m.Kind);
                    }
                sb.Append($"\nFloor {f + 1} of {levels.Count}, doorways at y {level:0.#}:\n");
                for (int z = h - 1; z >= 0; z--)
                {
                    var line = new StringBuilder("  ");
                    for (int x = 0; x < w; x++) line.Append(grid[z, x]).Append(grid[z, x]);
                    sb.Append(line.ToString().TrimEnd()).Append('\n');
                }
            }
            sb.Append("\nE way in, + joined, : door, L locked, # end cap, ! sealed; mechanisms: / lever, _ plate, = gate or door, ^ platform,\n")
              .Append("% stair, @ rotator, ~ floor. Rooms:\n");
            foreach (PlacedRoom r in layout.Rooms)
            {
                if (r.Def.EndCap) continue;
                sb.Append($"  {Symbol(r.Index)} {r.Def.Name} (depth {r.Depth}{(r.Step != null ? ", step " + r.Step : "")})\n");
            }
            bool any = false;
            foreach (PlacedRoom r in layout.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                {
                    if (!any) { sb.Append("Mechanisms:\n"); any = true; }
                    sb.Append($"  {MechMark(m.Kind)} {r.Index}.{m.Id} {m.Kind}, {States(m)} states, starts {m.Start}");
                    if (!string.IsNullOrEmpty(m.Doorway)) sb.Append($", closes doorway {m.Doorway} when 0");
                    if (m.Cycle > 0f) sb.Append($", turns by itself every {m.Cycle:0.#} s");
                    if (m.Wires.Count > 0) sb.Append(": " + string.Join(", ", m.Wires.Select(w => (Target(layout, r.Index, w) ?? "nothing (" + w.Target + ")") + " " + w.Do)));
                    if (!string.IsNullOrEmpty(m.ReachedFrom)) sb.Append($" (from doorway {m.ReachedFrom}'s side)");
                    sb.Append('\n');
                }
            if (any) sb.Append($"Solution ({layout.Solution.Count} uses): {(layout.Solution.Count > 0 ? string.Join(", ", layout.Solution) : "none needed")}\n");
            if (layout.Doors.Count > 0) sb.Append($"Doors: {layout.Doors.Count}\n");
            return sb.ToString();
        }

        // ---- one try ----

        private sealed class Attempt
        {
            private readonly Pack _pack;
            private readonly Rng _rng;
            private readonly Layout _layout;
            private readonly List<Box> _boxes = new List<Box>();
            private readonly List<int[]> _linkOf = new List<int[]>();      // per room, per doorway: its link's index, or -1
            private readonly List<int> _parentLink = new List<int>();      // per room: the link it was placed by (-1 for the entrance)
            private readonly Dictionary<RoomDef, int> _used = new Dictionary<RoomDef, int>();
            private readonly HashSet<string> _planTags;                    // rooms with these tags are kept for their steps
            private readonly HashSet<long> _dead = new HashSet<long>();    // doorways nothing fits on (they get end caps)
            private readonly Vector3 _maxSize;
            private Box _all;
            private int _way = -1;                                         // the entrance room's way in
            private int _end = -1;                                         // the last spine room: no branches after the end
            private readonly HashSet<int> _own = new HashSet<int>();       // placed rooms given their own copy of their RoomDef
            private readonly int _rngSeed;
            private readonly bool _puzzle;                                 // the pack has mechanisms or gates: the spine follows the solver
            private int _version;                                          // bumped on every change the solver would see
            private int _reachVersion = -1;
            private Dictionary<long, int> _reach;

            public Attempt(Pack pack, int seed, int rngSeed)
            {
                _pack = pack;
                _rngSeed = rngSeed;
                _rng = new Rng(rngSeed);
                _layout = new Layout { Pack = pack, Seed = seed };
                _planTags = new HashSet<string>(pack.Plan.Where(s => !string.IsNullOrEmpty(s.Tag)).Select(s => s.Tag), StringComparer.OrdinalIgnoreCase);
                _maxSize = PackReader.MaxSize(pack);
                _puzzle = PackReader.HasGates(pack) || pack.Rooms.Any(r => r.Mechs.Count > 0);
            }

            public Layout Run()
            {
                if (!PlaceEntrance()) return _layout;
                int target = _pack.MinRooms + _rng.Range(Math.Max(0, _pack.MaxRooms - _pack.MinRooms) + 1);
                bool spine = Spine(target);
                if (spine) Branches(target);
                Close();
                if (spine) Check();
                if (_layout.Ok) Doors();
                return _layout;
            }

            private bool PlaceEntrance()
            {
                PlanStep first = _pack.Plan.Count > 0 ? _pack.Plan[0] : null;
                List<RoomDef> starts = _pack.Rooms.Where(r => r.Entrance && r.Doorways.Any(d => d.Entrance)).ToList();
                if (first != null && starts.Any(r => HasTag(r, first.Tag))) starts = starts.Where(r => HasTag(r, first.Tag)).ToList();
                if (starts.Count == 0) { Problem("the pack has no entrance room (a room marked entrance, with a doorway marked entrance)"); return false; }
                RoomDef def = WeightedOrder(starts, true)[0];
                Add(def, Vector3.zero, 0, 0, -1);
                _way = def.Doorways.FindIndex(d => d.Entrance);
                if (first != null && HasTag(def, first.Tag)) _layout.Rooms[0].Step = StepName(first);
                return true;
            }

            /// <summary>The plan's steps one after another, with rooms in between. False if a step found no place. The rooms between two
            /// steps grow from the newest one first, then from the ones before it back to the earlier step (a way that ran into a wall
            /// turns off earlier), so each step is still only reached through the one before it. A step with "from" grows from that
            /// earlier step's room instead (a hub: several steps off one hall).</summary>
            private bool Spine(int target)
            {
                int tip = 0;
                var segment = new List<int> { 0 };   // the earlier step's room and the rooms after it, newest last
                int first = _layout.Rooms[0].Step != null ? 1 : 0;
                for (int s = first; s < _pack.Plan.Count; s++)
                {
                    PlanStep step = _pack.Plan[s];
                    string after = PackReader.From(_pack, step);
                    if (after != null)
                    {
                        int hub = _layout.Rooms.FindIndex(r => r.Step == after);
                        if (hub < 0) { Problem($"step {StepName(step)} grows from step {after}, which wasn't placed"); return false; }
                        segment = new List<int> { hub };
                    }
                    bool last = s == _pack.Plan.Count - 1;
                    // A step the way goes on from (the next step without "from", or one "from" it) needs a second doorway; a branch's
                    // end (a key in a side wing) may be a dead end.
                    bool goesOn = (!last && PackReader.From(_pack, _pack.Plan[s + 1]) == null) || _pack.Plan.Any(p => PackReader.From(_pack, p) == StepName(step));
                    int stepsLeft = _pack.Plan.Count - s;
                    int budget = target - RoomCount() - stepsLeft;            // rooms left for in-between and branches
                    int gap = 1 + _rng.Range(Math.Max(1, budget * 2 / 3 / stepsLeft));
                    if (_pack.MaxRooms - RoomCount() < 2 * stepsLeft) gap = 0;   // (not room for a filler before every step left: this one goes on directly)
                    List<RoomDef> stepRooms = _pack.Rooms.Where(r => HasTag(r, step.Tag) && !r.Entrance && !r.EndCap && (!goesOn || Ways(r) >= 2)).ToList();
                    if (stepRooms.Count == 0) { Problem($"no room for step {StepName(step)} (tag {step.Tag}{(goesOn ? ", with two doorways or more" : "")})"); return false; }
                    // A key (a later step opens with it) wants a room with a lever or plate; a locked step one with a doorway a gate closes.
                    if (_pack.Plan.Any(p => p.OpensWith == StepName(step)) && stepRooms.Any(r => r.Mechs.Any(IsUsable)))
                        stepRooms = stepRooms.Where(r => r.Mechs.Any(IsUsable)).ToList();
                    bool locked = !string.IsNullOrEmpty(step.OpensWith);
                    Func<RoomDef, int, bool> entry = locked ? LockEntry : (Func<RoomDef, int, bool>)null;
                    if (locked && !stepRooms.Any(r => Enumerable.Range(0, r.Doorways.Count).Any(j => !r.Doorways[j].Entrance && LockEntry(r, j))))
                    {
                        Problem($"step {StepName(step)} is locked, but no {step.Tag} room has a gate and the pack has no \"gates\" for its doorways");
                        return false;
                    }

                    int placed = -1;
                    for (int between = 0; placed < 0; between++)
                    {
                        if (between >= gap)
                            placed = GrowAny(segment, stepRooms, false, entry);
                        if (placed >= 0) break;
                        if (RoomCount() + stepsLeft >= _pack.MaxRooms) { Problem($"step {StepName(step)}: no place for it within {_pack.MaxRooms} rooms"); return false; }
                        int filler = GrowAny(segment, Fillers(), true);
                        if (filler < 0 && between < gap) placed = GrowAny(segment, stepRooms, false, entry);   // (nothing fits in between: the step itself)
                        if (placed >= 0) break;
                        if (filler < 0) { Problem($"step {StepName(step)}: the way there ran into dead ends after room {segment[0]} ({_layout.Rooms[segment[0]].Def.Name})"); return false; }
                        segment.Add(filler);
                    }
                    _layout.Rooms[placed].Step = StepName(step);
                    if (locked)
                    {
                        Link by = _layout.Links[_parentLink[placed]];   // (parent first: a lock names the room behind it first)
                        _layout.Locks.Add(new Link { Room = by.OtherRoom, Doorway = by.OtherDoorway, OtherRoom = by.Room, OtherDoorway = by.Doorway });
                        Gate(by.OtherRoom, by.OtherDoorway);
                        if (!Opener(step)) return false;
                    }
                    tip = placed;
                    segment = new List<int> { placed };
                }
                _end = tip == 0 ? -1 : tip;
                return true;
            }

            /// <summary>Side rooms on open doorways until there are enough rooms (or nowhere left to put one).</summary>
            private void Branches(int target)
            {
                while (RoomCount() < target)
                {
                    List<KeyValuePair<int, int>> open = Open(true);
                    if (open.Count == 0) return;
                    KeyValuePair<int, int> at = open[_rng.Range(open.Count)];
                    int depth = _layout.Rooms[at.Key].Depth + 1;
                    List<RoomDef> fit = Fillers(true).Where(r => r.MinDepth <= depth).ToList();
                    if (TryAll(at.Key, at.Value, WeightedOrder(fit, false), depth, BehindWall) < 0) _dead.Add(Key(at.Key, at.Value));
                }
            }

            /// <summary>Joins touching doorways (loops), caps the rest, and leaves what nothing closes as sealed. A loop that would let
            /// the plan's order be walked around, or make the puzzle shorter, isn't joined (both sides get caps): a loop round a gate or a
            /// turning stair would make it pointless.</summary>
            private void Close()
            {
                if (_layout.Ok) Shortcut();
                List<KeyValuePair<int, int>> open = Open(false);
                bool guard = _layout.Ok && (_puzzle || _layout.Locks.Count > 0 || _pack.Plan.Count > 2);
                KeyValuePair<int, int> before = guard ? Measure() : default(KeyValuePair<int, int>);
                for (int i = 0; i < open.Count; i++)
                {
                    int room = open[i].Key, door = open[i].Value;
                    if (_linkOf[room][door] >= 0) continue;   // (joined to an earlier one in this loop)
                    Vector3 at = At(room, door);
                    bool wayIn = room == 0 && door == _way;
                    bool looped = false;
                    for (int j = i + 1; j < open.Count && !looped && !wayIn; j++)   // (the way in never loops: it's behind the way out)
                    {
                        int r2 = open[j].Key, d2 = open[j].Value;
                        if (r2 == 0 && d2 == _way) continue;
                        if (r2 == room || _linkOf[r2][d2] >= 0 || !SameType(Door(room, door), Door(r2, d2))) continue;
                        if (Dist(at, At(r2, d2)) > Near || Quadrant(Facing(room, door)) != Opposite(Quadrant(Facing(r2, d2)))) continue;
                        Join(room, door, r2, d2);
                        if (guard)
                        {
                            KeyValuePair<int, int> after = Measure();
                            if (after.Key > before.Key || (before.Value != int.MaxValue && after.Value < before.Value)) { Unjoin(); break; }
                        }
                        looped = true;
                    }
                    if (looped) continue;
                    string type = Door(room, door).Type ?? "";
                    List<RoomDef> caps = _pack.Rooms.Where(r => r.EndCap && !PackReader.HasShortcut(r) && r.Doorways.Any(d => (d.Type ?? "") == type)).ToList();
                    // The way in (behind the way out) and a doorway a gate closes take a plain cap: a flat seal, not a nook or a treasury
                    // (a door opening and shutting behind the Leave door, or a room behind a gate). The way in: the pack's "entranceCap" if
                    // it names one.
                    bool gated = _layout.Rooms[room].Def.Mechs.Any(m => IsGate(m) && m.Doorway == Door(room, door).Id);
                    if (wayIn || gated)
                    {
                        string named = wayIn ? PackReader.EntranceCap(_pack) : null;
                        caps = named != null ? caps.Where(r => r.Name == named).ToList() : caps.Where(PlainCap).ToList();
                    }
                    if (TryAll(room, door, WeightedOrder(caps, true), _layout.Rooms[room].Depth + 1) < 0 && (wayIn || gated))   // (others: sealed)
                        Problem(wayIn ? "no plain end cap fits the way in" : $"no plain end cap fits room {room}'s gated doorway {Door(room, door).Id}");
                }
            }

            private void Check()
            {
                if (_layout.Rooms.Count == 0) return;
                if (RoomCount() < _pack.MinRooms) Problem($"only {RoomCount()} rooms (the pack wants {_pack.MinRooms} or more)");
                if (_maxSize != Vector3.zero)
                {
                    Vector3 size = _all.Max - _all.Min;
                    if (size.x > _maxSize.x + Near || size.y > _maxSize.y + Near || size.z > _maxSize.z + Near)
                        Problem($"{size.x:0}x{size.y:0}x{size.z:0} m is bigger than the pack's {_maxSize.x:0}x{_maxSize.y:0}x{_maxSize.z:0}");
                }
                foreach (string p in Order()) Problem(p);
                if (!_layout.Ok) return;
                // And the puzzle: every step reached and the end too, from the entrance, with the levers and plates.
                string why = Solve(_layout, _layout.Solution, false, true);   // (every breakable wall standing: nothing may need a secret room)
                if (why != null && Solve(_layout, new List<string>()) == null) { Problem("the end needs a breakable wall broken (" + why + ")"); return; }
                if (why != null)
                {
                    // Not alone: would a second player (or a creature) holding a plate down do it? Say so: the pack needs a solo way.
                    var together = new List<string>();
                    Problem(Solve(_layout, together, true) == null
                        ? $"no solo way: only with a plate held down by a second player or a creature ({string.Join(", ", together)})"
                        : why);
                    return;
                }
                if (_layout.Rooms.Any(r => PackReader.HasShortcut(r.Def)))
                {
                    var without = new List<string>();
                    if (Solve(_layout, without, false, true, true) == null && without.Count > _layout.Solution.Count)
                        Problem($"the shortcut makes the puzzle shorter ({_layout.Solution.Count} uses, {without.Count} without it)");
                }
                int min = PackReader.MinUses(_pack);
                if (_layout.Solution.Count < min) Problem($"too easy: solved in {_layout.Solution.Count} uses (the pack wants {min} or more)");
            }

            /// <summary>What's wrong with the plan's order in the rooms as they are joined: a step not placed, a step reached without
            /// passing the one before it (or the one it grows from), a lock that can be walked around.</summary>
            private List<string> Order()
            {
                var problems = new List<string>();
                var stepRoom = new List<int>();
                foreach (PlanStep step in _pack.Plan)
                {
                    int i = _layout.Rooms.FindIndex(r => r.Step == StepName(step));
                    if (i < 0) { problems.Add($"step {StepName(step)} wasn't placed"); return problems; }
                    stepRoom.Add(i);
                }
                var none = new HashSet<int>();
                for (int s = 1; s < stepRoom.Count; s++)
                {
                    string after = PackReader.From(_pack, _pack.Plan[s]);
                    int before = after != null ? stepRoom[_pack.Plan.FindIndex(p => StepName(p) == after)] : stepRoom[s - 1];
                    if (before != 0 && Reach(before, none).Contains(stepRoom[s]))
                        problems.Add($"step {_layout.Rooms[stepRoom[s]].Step} can be reached without passing step {_layout.Rooms[before].Step}");
                }
                foreach (Link l in _layout.Locks)
                    if (Reach(-1, new HashSet<int> { _linkOf[l.Room][l.Doorway] }).Contains(l.Room))
                        problems.Add($"the lock before step {_layout.Rooms[l.Room].Step} can be walked around");
                return problems;
            }

            /// <summary>How a loop would change the dungeon: the order's problems and the puzzle's fewest uses (int.MaxValue: unsolved).</summary>
            private KeyValuePair<int, int> Measure()
            {
                int uses = int.MaxValue;
                if (_puzzle)
                {
                    var solution = new List<string>();
                    if (Solve(_layout, solution) == null) uses = solution.Count;
                }
                return new KeyValuePair<int, int>(Order().Count, uses);
            }

            /// <summary>The joints that get a door: both doorways allow one, neither room is an end cap, no gate stands there, it's in a
            /// wall (not a floor), the pack has a door for the type, and the pick (by DoorChance) says so. Its own random stream, so doors never change the rooms.</summary>
            private void Doors()
            {
                if (_pack.Doors.Count == 0 || _pack.DoorChance <= 0f) return;
                var rng = new Rng(_rngSeed ^ 0x0D0025);
                foreach (Link l in _layout.Links)
                {
                    PlacedRoom a = _layout.Rooms[l.Room], b = _layout.Rooms[l.OtherRoom];
                    if (a.Def.EndCap || b.Def.EndCap) continue;
                    if ((l.Room == 0 && l.Doorway == _way) || (l.OtherRoom == 0 && l.OtherDoorway == _way)) continue;
                    Doorway da = a.Def.Doorways[l.Doorway], db = b.Def.Doorways[l.OtherDoorway];
                    if (!da.AllowDoor || !db.AllowDoor || !_pack.Doors.ContainsKey(da.Type ?? "")) continue;
                    if (Quadrant(a.DoorwayFacing(l.Doorway)) >= 4) continue;   // (a hatch in a floor: no door stands there)
                    if (a.Def.Mechs.Any(m => IsGate(m) && m.Doorway == da.Id) || b.Def.Mechs.Any(m => IsGate(m) && m.Doorway == db.Id)) continue;
                    if (rng.Unit() < _pack.DoorChance) _layout.Doors.Add(l);
                }
            }

            // ---- locks ----

            /// <summary>Whether a locked step's room may be entered through doorway j: its own gate's doorway if it has gates, else any
            /// doorway the pack has a gate pattern for.</summary>
            private bool LockEntry(RoomDef def, int j)
            {
                List<MechDef> gates = def.Mechs.Where(m => IsGate(m) && !string.IsNullOrEmpty(m.Doorway)).ToList();
                if (gates.Count > 0) return gates.Any(m => m.Doorway == def.Doorways[j].Id);
                return PackReader.Gate(_pack, def.Doorways[j].Type) != null;
            }

            /// <summary>The gate in a locked doorway: the room's own, or the pack's pattern for the doorway's type, set in the doorway
            /// (its frame: at the doorway, +z into the room).</summary>
            private void Gate(int room, int door)
            {
                RoomDef def = _layout.Rooms[room].Def;
                Doorway d = def.Doorways[door];
                if (def.Mechs.Any(m => IsGate(m) && m.Doorway == d.Id)) return;
                MechDef gate = Copy(PackReader.Gate(_pack, d.Type));   // (LockEntry let only such doorways in)
                gate.Id = "lockgate";
                for (int n = 2; def.Mechs.Any(m => m.Id == gate.Id); n++) gate.Id = "lockgate" + n;
                gate.At = d.At;
                int q = Quadrant(d.Facing);
                gate.Euler = q == 4 ? new Vector3(-90f, 0f, 0f) : q == 5 ? new Vector3(90f, 0f, 0f) : new Vector3(0f, q * 90f, 0f);
                gate.Doorway = d.Id;
                gate.Wires = new List<Wire>();
                gate.Joins = new List<Join>();
                Own(room).Mechs.Add(gate);
            }

            /// <summary>The locked step's key (OpensWith) must open its gate: a lever or plate in the key's room wired to it, or the
            /// first lever there (first one with no wires of its own) wired now. False (and a problem) if the key's room has none.</summary>
            private bool Opener(PlanStep step)
            {
                int keyRoom = _layout.Rooms.FindIndex(r => r.Step == step.OpensWith);
                if (keyRoom < 0) { Problem($"step {StepName(step)} opens with {step.OpensWith}, which isn't placed before it"); return false; }
                var toGate = new Wire { Target = "step:" + StepName(step), Do = "set:1" };   // (opens it for good: fine for a one-shot lever)
                string gate = Target(_layout, keyRoom, toGate);
                List<MechDef> mechs = _layout.Rooms[keyRoom].Def.Mechs;
                if (mechs.Any(m => IsUsable(m) && m.Wires.Any(w => w.Do != "hold" && Target(_layout, keyRoom, w) == gate))) return true;
                int k = mechs.FindIndex(m => m.Kind == "lever" && m.Wires.Count == 0);
                if (k < 0) k = mechs.FindIndex(m => m.Kind == "lever");
                if (k < 0) k = mechs.FindIndex(IsUsable);
                if (k < 0) { Problem($"step {step.OpensWith} opens step {StepName(step)}, but its room ({_layout.Rooms[keyRoom].Def.Name}) has no lever or plate"); return false; }
                RoomDef own = Own(keyRoom);
                MechDef lever = Copy(own.Mechs[k]);
                lever.Wires = new List<Wire>(lever.Wires) { toGate };
                own.Mechs[k] = lever;
                return true;
            }

            /// <summary>A placed room's own copy of its RoomDef (its own Mechs list), made the first time it's asked for.</summary>
            private RoomDef Own(int room)
            {
                PlacedRoom r = _layout.Rooms[room];
                _version++;   // (the caller changes its mechanisms)
                if (_own.Add(room))
                {
                    RoomDef copy = Copy(r.Def);
                    copy.Mechs = new List<MechDef>(r.Def.Mechs);
                    r.Def = copy;
                }
                return r.Def;
            }

            // ---- placing ----

            /// <summary>A room from the list (by weight) on one of room's open doorways. The new room's index, or -1.</summary>
            private int Grow(int room, List<RoomDef> defs, bool needWayOn, Func<RoomDef, int, bool> entry = null)
            {
                int depth = _layout.Rooms[room].Depth + 1;
                List<RoomDef> fit = defs.Where(r => r.MinDepth <= depth && (!needWayOn || Ways(r) >= 2)).ToList();
                if (fit.Count == 0) return -1;
                List<KeyValuePair<int, int>> doors = Open(true).Where(p => p.Key == room).ToList();
                Shuffle(doors);
                RoomDef from = _layout.Rooms[room].Def;
                if (_puzzle && (from.Mechs.Count > 0 || from.Inside.Count > 0 || PackReader.Drops(_pack, from).Count > 0))
                {
                    // Only doorways the player can get to (with what's placed so far), those that take the most uses first, so the
                    // next step's room is always reachable and the puzzle grows harder along the plan. (A plain room's doorways are as
                    // reachable as the one it was entered by: the spine only grows from reachable ones.)
                    Dictionary<long, int> reach = Reachable();
                    doors = doors.Where(p => reach.ContainsKey(Key(p.Key, p.Value))).OrderByDescending(p => reach[Key(p.Key, p.Value)]).ToList();
                }
                foreach (RoomDef def in WeightedOrder(fit, false))
                    foreach (KeyValuePair<int, int> d in doors)
                    {
                        int i = TryPlace(def, d.Key, d.Value, depth, entry);
                        if (i >= 0) return i;
                    }
                return -1;
            }

            /// <summary>Where the player can stand in the layout so far, and in how few uses (the solver; remembered until it changes).</summary>
            private Dictionary<long, int> Reachable()
            {
                if (_reachVersion != _version) { _reach = UsesToReach(_layout); _reachVersion = _version; }
                return _reach;
            }

            /// <summary>Grow from the newest room in the list that takes one.</summary>
            private int GrowAny(List<int> rooms, List<RoomDef> defs, bool needWayOn, Func<RoomDef, int, bool> entry = null)
            {
                for (int k = rooms.Count - 1; k >= 0; k--)
                {
                    int i = Grow(rooms[k], defs, needWayOn, entry);
                    if (i >= 0) return i;
                }
                return -1;
            }

            /// <summary>A secret room is only ever joined by the doorway its breakable wall closes.</summary>
            private static bool BehindWall(RoomDef def, int j) =>
                !PackReader.Secret(def) || def.Mechs.Any(m => m.Kind == "breakable" && m.Doorway == def.Doorways[j].Id);

            private int TryAll(int room, int door, List<RoomDef> defs, int depth, Func<RoomDef, int, bool> entry = null)
            {
                foreach (RoomDef def in defs)
                {
                    int i = TryPlace(def, room, door, depth, entry);
                    if (i >= 0) return i;
                }
                return -1;
            }

            /// <summary>def joined to (room, door) by one of its doorways (one entry allows, if given), if its box fits. The new room's
            /// index, or -1.</summary>
            private int TryPlace(RoomDef def, int room, int door, int depth, Func<RoomDef, int, bool> entry = null, bool dry = false)
            {
                Doorway there = Door(room, door);
                Vector3 at = At(room, door);
                int want = Opposite(Quadrant(Facing(room, door)));
                if (want < 0) return -1;
                var mine = new List<int>();
                for (int j = 0; j < def.Doorways.Count; j++)
                    if (!def.Doorways[j].Entrance && SameType(def.Doorways[j], there) && (entry == null || entry(def, j)) && PackReader.JoinBy(_pack, def, def.Doorways[j]))
                        mine.Add(j);
                Shuffle(mine);
                foreach (int j in mine)
                {
                    int q = Quadrant(def.Doorways[j].Facing);
                    var turns = new List<int>();
                    if (q >= 0 && q < 4 && want < 4) turns.Add(((want - q) % 4 + 4) % 4);
                    else if (q >= 4 && q == want) { turns.AddRange(new[] { 0, 1, 2, 3 }); Shuffle(turns); }
                    foreach (int turn in turns)
                    {
                        Vector3 pos = Snap(at - Turn(def.Doorways[j].At, turn));
                        Box box = BoxOf(def, pos, turn);
                        if (!Fits(box)) continue;
                        if (dry) return 0;   // (it would fit)
                        int i = Add(def, pos, turn, depth, -1);
                        _parentLink[i] = Join(room, door, i, j);
                        return i;
                    }
                }
                return -1;
            }

            private int Add(RoomDef def, Vector3 pos, int turn, int depth, int parentLink)
            {
                int i = _layout.Rooms.Count;
                _version++;
                _layout.Rooms.Add(new PlacedRoom { Index = i, Def = def, Pos = pos, Yaw = turn * 90f, Depth = depth });
                Box box = BoxOf(def, pos, turn);
                _boxes.Add(box);
                _all = i == 0 ? box : _all.With(box);
                _layout.Box = new Bounds((_all.Min + _all.Max) * 0.5f, _all.Max - _all.Min);
                var links = new int[def.Doorways.Count];
                for (int d = 0; d < links.Length; d++) links[d] = -1;
                _linkOf.Add(links);
                _parentLink.Add(parentLink);
                _used[def] = Used(def) + 1;
                return i;
            }

            /// <summary>A plain cap: an end cap under 1 m deep along its doorway, with no mechanisms and no saved parts (no chest).</summary>
            private static bool PlainCap(RoomDef def) => def.EndCap && def.Mechs.Count == 0 && !def.Parts.Any(p => p.Saved) && CapDepth(def) < 1f;

            /// <summary>The pack's shortcut, if it has one: its far end on an open doorway of the far step's room (or of a room one link from
            /// it), its near end on a spare doorway of the entrance room (not the way in). Both or neither: it's left out if one doesn't fit.</summary>
            private void Shortcut()
            {
                PackReader.ShortcutSpec spec = PackReader.Shortcut(_pack);
                if (spec == null) return;
                RoomDef near = _pack.Rooms.FirstOrDefault(r => r.Name == spec.Near), far = _pack.Rooms.FirstOrDefault(r => r.Name == spec.Far);
                int step = _layout.Rooms.FindIndex(r => r.Step == spec.FarStep);
                if (near == null || far == null || step < 0) return;
                var rooms = new List<int> { step };
                foreach (int li in _linkOf[step])
                    if (li >= 0)
                    {
                        Link l = _layout.Links[li];
                        int other = l.Room == step ? l.OtherRoom : l.Room;
                        if (!_layout.Rooms[other].Def.EndCap && !PackReader.Secret(_layout.Rooms[other].Def)) rooms.Add(other);
                    }
                bool Spare(int r, int d) => _linkOf[r][d] < 0 && !(r == 0 && d == _way)
                    && !_layout.Rooms[r].Def.Mechs.Any(m => IsGate(m) && m.Doorway == Door(r, d).Id);
                KeyValuePair<int, int> farAt = default, nearAt = default;
                bool farOk = false, nearOk = false;
                foreach (int r in rooms)
                {
                    for (int d = 0; d < _linkOf[r].Length && !farOk; d++)
                        if (Spare(r, d) && TryPlace(far, r, d, _layout.Rooms[r].Depth + 1, null, true) >= 0) { farAt = new KeyValuePair<int, int>(r, d); farOk = true; }
                    if (farOk) break;
                }
                for (int d = 0; d < _linkOf[0].Length && !nearOk; d++)
                    if (Spare(0, d) && TryPlace(near, 0, d, 1, null, true) >= 0) { nearAt = new KeyValuePair<int, int>(0, d); nearOk = true; }
                if (!farOk || !nearOk) return;
                if (TryPlace(far, farAt.Key, farAt.Value, _layout.Rooms[farAt.Key].Depth + 1) < 0) return;
                if (TryPlace(near, 0, nearAt.Value, 1) < 0) Problem("the shortcut's far end is in, but its near end no longer fits");
            }

            /// <summary>Undoes the newest Join.</summary>
            private void Unjoin()
            {
                Link l = _layout.Links[_layout.Links.Count - 1];
                _layout.Links.RemoveAt(_layout.Links.Count - 1);
                _linkOf[l.Room][l.Doorway] = -1;
                _linkOf[l.OtherRoom][l.OtherDoorway] = -1;
                _version++;
            }

            private int Join(int room, int door, int other, int otherDoor)
            {
                _version++;
                _layout.Links.Add(new Link { Room = room, Doorway = door, OtherRoom = other, OtherDoorway = otherDoor });
                int li = _layout.Links.Count - 1;
                _linkOf[room][door] = li;
                _linkOf[other][otherDoor] = li;
                return li;
            }

            /// <summary>Inside the pack's size limit and clear of every other room's box (rooms with no floor area, like flat end caps,
            /// are never tested, as in the game).</summary>
            private bool Fits(Box box)
            {
                if (_maxSize != Vector3.zero)
                {
                    Box all = _all.With(box);
                    Vector3 size = all.Max - all.Min;
                    if (size.x > _maxSize.x + Near || size.y > _maxSize.y + Near || size.z > _maxSize.z + Near) return false;
                }
                if (box.Flat) return true;
                foreach (Box other in _boxes)
                    if (!other.Flat && box.Overlaps(other)) return false;
                return true;
            }

            // ---- what's open, what may go there ----

            /// <summary>Doorways nothing joins yet, in placing order (room, doorway). growing: not the way in, not nowhere-fits ones, and
            /// not past the end.</summary>
            private List<KeyValuePair<int, int>> Open(bool growing)
            {
                var list = new List<KeyValuePair<int, int>>();
                for (int r = 0; r < _layout.Rooms.Count; r++)
                {
                    if (growing && (r == _end || _layout.Rooms[r].Def.EndCap || PackReader.Secret(_layout.Rooms[r].Def))) continue;
                    for (int d = 0; d < _linkOf[r].Length; d++)
                    {
                        if (_linkOf[r][d] >= 0) continue;
                        if (growing && ((r == 0 && d == _way) || _dead.Contains(Key(r, d)))) continue;
                        list.Add(new KeyValuePair<int, int>(r, d));
                    }
                }
                return list;
            }

            /// <summary>The rooms that may go anywhere: not entrances, not end caps, not kept for a step, and not used up.</summary>
            private List<RoomDef> Fillers(bool secrets = false) => _pack.Rooms.Where(r => !r.Entrance && !r.EndCap && !r.Tags.Any(_planTags.Contains)
                && (secrets || !PackReader.Secret(r)) && !PackReader.HasShortcut(r) && (r.Max <= 0 || Used(r) < r.Max)).ToList();

            private int Used(RoomDef def) => _used.TryGetValue(def, out int n) ? n : 0;
            private int RoomCount() => _layout.Rooms.Count(r => !r.Def.EndCap);
            private static int Ways(RoomDef def) => def.Doorways.Count(d => !d.Entrance);

            /// <summary>The list in a weighted random order (each pick by weight from what's left). Rooms of weight 0 or less are left out,
            /// unless all are (allZero).</summary>
            private List<RoomDef> WeightedOrder(List<RoomDef> defs, bool allZero)
            {
                var left = defs.Where(d => d.Weight > 0f && (d.Max <= 0 || Used(d) < d.Max)).ToList();
                if (left.Count == 0 && allZero) left = defs.ToList();
                var order = new List<RoomDef>();
                while (left.Count > 0)
                {
                    double total = 0;
                    foreach (RoomDef d in left) total += Math.Max(d.Weight, 0.0001f);
                    double pick = _rng.Unit() * total;
                    int k = 0;
                    for (; k < left.Count - 1; k++)
                    {
                        pick -= Math.Max(left[k].Weight, 0.0001f);
                        if (pick < 0) break;
                    }
                    order.Add(left[k]);
                    left.RemoveAt(k);
                }
                return order;
            }

            private void Shuffle<T>(List<T> list)
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = _rng.Range(i + 1);
                    T t = list[i]; list[i] = list[j]; list[j] = t;
                }
            }

            // ---- the graph ----

            /// <summary>The rooms reachable from the entrance without going into `without` or through the closed links.</summary>
            private HashSet<int> Reach(int without, HashSet<int> closed)
            {
                var seen = new HashSet<int> { 0 };
                var queue = new Queue<int>();
                queue.Enqueue(0);
                while (queue.Count > 0)
                {
                    int r = queue.Dequeue();
                    foreach (int li in _linkOf[r])
                    {
                        if (li < 0 || closed.Contains(li)) continue;
                        Link l = _layout.Links[li];
                        int next = l.Room == r ? l.OtherRoom : l.Room;
                        if (next == without || !seen.Add(next)) continue;
                        queue.Enqueue(next);
                    }
                }
                return seen;
            }

            private Doorway Door(int room, int door) => _layout.Rooms[room].Def.Doorways[door];
            private Vector3 At(int room, int door) => _layout.Rooms[room].Pos + Turn(Door(room, door).At, Quarter(_layout.Rooms[room].Yaw));
            private Vector3 Facing(int room, int door) => Turn(Door(room, door).Facing, Quarter(_layout.Rooms[room].Yaw));
            private void Problem(string text) => _layout.Problems.Add(text);
        }

        // ---- small things ----

        /// <summary>The axis-aligned box a room takes, turned by quarter turns.</summary>
        private struct Box
        {
            public Vector3 Min, Max;
            public bool Flat;   // no floor area (the game's flat end caps): never tested

            public Box With(Box b) => new Box
            {
                Min = new Vector3(Math.Min(Min.x, b.Min.x), Math.Min(Min.y, b.Min.y), Math.Min(Min.z, b.Min.z)),
                Max = new Vector3(Math.Max(Max.x, b.Max.x), Math.Max(Max.y, b.Max.y), Math.Max(Max.z, b.Max.z)),
            };

            public bool Overlaps(Box b) =>
                Math.Min(Max.x, b.Max.x) - Math.Max(Min.x, b.Min.x) > Margin &&
                Math.Min(Max.y, b.Max.y) - Math.Max(Min.y, b.Min.y) > Margin &&
                Math.Min(Max.z, b.Max.z) - Math.Max(Min.z, b.Min.z) > Margin;
        }

        /// <summary>How deep an end cap is: its size along its (first) doorway's facing.</summary>
        internal static float CapDepth(RoomDef def)
        {
            if (def.Doorways.Count == 0) return 0f;
            int q = Quadrant(def.Doorways[0].Facing);
            return q == 0 || q == 2 ? def.Size.z : q == 1 || q == 3 ? def.Size.x : def.Size.y;
        }

        private static Box BoxOf(RoomDef def, Vector3 pos, int turn)
        {
            Vector3 half = def.Size * 0.5f;
            if (turn % 2 == 1) half = new Vector3(half.z, half.y, half.x);
            return new Box { Min = Snap(pos - half), Max = Snap(pos + half), Flat = def.Size.x < 0.01f || def.Size.z < 0.01f };
        }

        /// <summary>v turned by quarter turns about the up axis, exactly (the same as PlacedRoom.Turn(v, turn * 90), with no rounding).</summary>
        internal static Vector3 Turn(Vector3 v, int turn)
        {
            switch (((turn % 4) + 4) % 4)
            {
                case 1: return new Vector3(v.z, v.y, -v.x);
                case 2: return new Vector3(-v.x, v.y, -v.z);
                case 3: return new Vector3(-v.z, v.y, v.x);
                default: return v;
            }
        }

        private static readonly MethodInfo CloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>A shallow copy (every field, so one added to Model.cs later is copied too).</summary>
        private static T Copy<T>(T o) where T : class => (T)CloneMethod.Invoke(o, null);

        private static char MechMark(string kind)
        {
            switch (kind)
            {
                case "lever": return '/';
                case "plate": return '_';
                case "gate": case "door": return '=';
                case "platform": return '^';
                case "stair": return '%';
                case "rotator": return '@';
                case "floor": return '~';
                default: return '*';
            }
        }

        private static int Quarter(float yaw) => ((int)Math.Round(yaw / 90f) % 4 + 4) % 4;

        /// <summary>Which way a facing points: 0..3 the quarter turn from +z (as Turn counts them), 4 up, 5 down, -1 anything else.</summary>
        internal static int Quadrant(Vector3 f)
        {
            if (f.y > 0.9f) return 4;
            if (f.y < -0.9f) return 5;
            if (Math.Abs(f.x) < 0.05f && Math.Abs(f.z) > 0.9f) return f.z > 0 ? 0 : 2;
            if (Math.Abs(f.z) < 0.05f && Math.Abs(f.x) > 0.9f) return f.x > 0 ? 1 : 3;
            return -1;
        }

        private static int Opposite(int q) => q < 0 ? -1 : q < 4 ? (q + 2) % 4 : q == 4 ? 5 : 4;

        private static bool SameType(Doorway a, Doorway b) => string.Equals(a.Type ?? "", b.Type ?? "", StringComparison.Ordinal);
        private static bool HasTag(RoomDef def, string tag) => !string.IsNullOrEmpty(tag) && def.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        internal static string StepName(PlanStep step) => string.IsNullOrEmpty(step.Id) ? step.Tag : step.Id;
        private static long Key(int room, int door) => ((long)room << 16) | (uint)door;
        private static float Dist(Vector3 a, Vector3 b) => Math.Max(Math.Abs(a.x - b.x), Math.Max(Math.Abs(a.y - b.y), Math.Abs(a.z - b.z)));

        /// <summary>To the nearest centimetre, so sums of floats can't drift apart between machines.</summary>
        private static Vector3 Snap(Vector3 v) => new Vector3(Snap(v.x), Snap(v.y), Snap(v.z));
        private static float Snap(float f) => (float)(Math.Round(f * 100.0, MidpointRounding.AwayFromZero) / 100.0);

        private static char Symbol(int i)
        {
            const string symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDFGHIJKMNOPQRSTUVWXYZ";   // (no E or L: the map's marks)
            return symbols[i % symbols.Length];
        }

        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));

        /// <summary>Another seed from a seed and a try number (the same on every machine).</summary>
        private static int Derive(int seed, int attempt)
        {
            ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)attempt * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 31)) * 0x94D049BB133111EBUL;
            return (int)(z ^ (z >> 29));
        }

        /// <summary>Our own seeded random numbers (SplitMix64): the same sequence everywhere, and nobody else's state touched.</summary>
        internal sealed class Rng
        {
            private ulong _state;

            public Rng(int seed) { _state = (ulong)(uint)seed ^ 0x5DEECE66DUL; }

            public ulong Next()
            {
                ulong z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }

            /// <summary>0 to n - 1 (0 if n is 0 or less).</summary>
            public int Range(int n) => n <= 0 ? 0 : (int)(Next() % (ulong)n);

            /// <summary>[0, 1).</summary>
            public double Unit() => (Next() >> 11) * (1.0 / (1UL << 53));
        }
    }
}
