using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Whether a room's puzzle can be walked, jumped or fallen round. For each of the pack's own rooms whose mechanisms join doorways (a
    /// hall of turning stairs, a rotating room), in every combination of its mechanisms' states, the places the solver keeps apart are
    /// checked against the room's own geometry:
    ///   - every part is a box (tools/dungeons/prefab_bounds.json: each prefab's box in its own frame, scaled, turned and moved as the part;
    ///     a prefab in prefab_shapes.json is several boxes instead, its collider's shape: a hollow ribcage can be walked through)
    ///     is; a mechanism's parts as they stand in that state's pose), made axis-aligned again (a tilted beam counts as its whole box);
    ///   - the room is cut into 0.5 m columns; a box's top is a place to stand (not a thin standing part's, under 0.3 m: bars, a grate; and
    ///     for a palette piece standing upright, only at a height where the palette measured a foothold) where it covers a column's middle and nothing else is in
    ///     the column from just above it to 1.8 m up (anything touching a column at all counts there, so a thin wall still splits a floor);
    ///   - a prop (not a structure piece: a stair, floor or wall is a surface like any other) with footholds (the palettes' "climb.ledges": standable faces measured from the game's colliders, stretched as the part is,
    ///     climbed band by band from its foot, each within a jump of the last; standing as the prefab stands) is climbed: from the floor beside its foot to any place within a jump of it (and of that foot spot, across at most 3 m of the prop),
    ///     up to its highest foothold + one jump, with nothing in the way at body height (as a jump) but the prop itself;
    ///   - with Quad's Parkour (when the pack allows it), a wall climb or ledge grab reaches 6 m up within 2 m: "only with Parkour", its own
    ///     section; a failure only when the pack says "parkour": true itself, information when it's left to the default;
    ///   - places join by walking (next columns, at most 0.6 m up or down), by jumping (one jump, the game's: up to 3.1 m up at Jump 100 (1.6 m at
    ///     Jump 0), and across as far as a sprinter flies to that height, 10.9 m on the level at Jump 100 and Run 100, further down; the body (1.8 m)
    ///     clear all along the real arc, or along a run off the edge with no jump: a rail under the arc is jumped over, bars across it or
    ///     a lintel over a fall's mouth aren't) and by falling (as a jump, any height down: one way);
    ///   - each doorway stands on the place under its point a metre into the room (or two or three, past a gate standing in it).
    /// Then, against the solver in the same state ("inside" joins, the mechanisms' joins, "drops"), each way from one doorway to another
    /// that the player has and the solver doesn't, "->" (or "<->" both ways):
    ///   - "walkable" or "jumpable": with no fall over 4 m on the way (no fall damage): the puzzle can be skipped, so close it off;
    ///   - "only with a fall": declare the fall in "drops" (the solver then counts it), or close it off.
    ///   - and the other way round, in every room of ours (not only puzzle rooms): each join the solver counts ("inside", or all the
    ///     doorways of a room with no joins, a mechanism's join in that state, a drop) that the player can't walk, jump or fall:
    ///     "blocked" (a room walled off inside where the solver thinks it's open: the dungeon could be impossible). A platform's join
    ///     that's blocked in its pose is checked again as a ride: the platform in all its poses at once, all its tops one place.
    /// "unchecked" when the bounds file is missing, parts have no bounds, or a doorway has nothing under it. A finding is a place to look
    /// (boxes are rough), not proof; nothing found means no gap under these limits.
    /// </summary>
    internal static class GapCheck
    {
        public const float Step = 0.6f, Headroom = 1.8f, Cell = 0.5f;

        /// <summary>One jump, from the game's numbers (builder2, from the game files: Character.Jump, the air control, the run speed):
        /// upward speed V = 8 × (1 + 0.4 × Jump skill), gravity 20, so the feet rise V²/40 (1.6 m at Jump 0, 3.1 m at Jump 100); a sprinter
        /// leaves at the run speed S = 7 × (1 + 0.25 × Run skill) and lands h higher after (V + √(V² − 40h)) / 20 s, with a 0.4 × (1 + 0.4 ×
        /// Jump skill) m push forward, and the body (radius 0.5 m) catches a ledge that much sooner. Puzzles must hold at any skill: by
        /// default the worst case, Jump 100 and Run 100 (the user's choice). Set by Skills().</summary>
        public static int JumpSkill { get; private set; } = 100;
        public static int RunSkill { get; private set; } = 100;
        public const float BodyRadius = 0.5f;
        private static readonly bool AllSpots = Environment.GetEnvironmentVariable("GAPALLSPOTS") == "1";   // (jumps from and to every spot, not only edges: to check the shortcut)
        private static float V = 11.2f, S = 8.75f;
        public static float JumpUp => V * V / 40f;
        /// <summary>How far across a jump reaches landing h higher (lower: negative); -1 if it can't get that high.</summary>
        public static float Reach(float h, float speed = -1f)
        {
            float d = V * V - 40f * h;
            return d < 0f ? -1f : (speed < 0f ? S : speed) * (V + (float)Math.Sqrt(d)) / 20f + 0.4f * (1f + 0.4f * JumpSkill / 100f) + BodyRadius;
        }

        /// <summary>"--run-up" (a test, not the gate's rule): a jump leaves at the speed a sprint reaches on its own place's longest run, from
        /// standing (the game: each physics step, 50 a second, the speed aimed at moves 1 m/s nearer the run speed, m_acceleration 1, and the
        /// body's speed goes half way to it). Off by default: a player who lands on a step from a jump keeps the speed (on the ground the
        /// speed aimed at is the run speed already, in the air the air control pulls towards it), so hopping on keeps a sprint's speed.</summary>
        public static bool RunUp;
        public static float SpeedAfter(float run)
        {
            float v = 0f, x = 0f;
            for (int i = 0; i < 200 && x < run && v < S - 0.001f; i++) { v += (Math.Min(S, v + 1f) - v) * 0.5f; x += v * 0.02f; }
            return x >= run ? v : S;
        }
        public static float JumpAcross => Reach(0f);
        public static void Skills(int jump, int run)
        {
            JumpSkill = Math.Max(0, Math.Min(100, jump));
            RunSkill = Math.Max(0, Math.Min(100, run));
            V = 8f * (1f + 0.4f * JumpSkill / 100f);
            S = 7f * (1f + 0.25f * RunSkill / 100f);
        }
        public const float SafeFall = 4f;   // the game's fall damage starts above this: a route with no longer fall is a plain jump
        public const float ClimbAcross = 3f;   // a climbed prop's width counted in its reach at most (the far side of a pillar or statue)
        public const float ParkourUp = 6f, ParkourAcross = 2f;   // with Quad's Parkour: a wall climb and a ledge grab reach this high, this close

        /// <summary>Props a player can climb in the plain game, and how high (m above their foot at scale 1): the palettes' "climb.top" (Jump
        /// 0) and "climb.top_skilled" (Jump 100), the highest standable face (38° or flatter, room for the feet) reachable from the floor
        /// with footholds a jump apart (builder2 measured them from the game's colliders). Not the "structure" pieces (stairs, floors,
        /// walls, arches): they're the room's own surfaces, walked and jumped from as they are. Set by Palettes().</summary>
        public static readonly Dictionary<string, KeyValuePair<float, float>> Climbers = new Dictionary<string, KeyValuePair<float, float>>(StringComparer.Ordinal);
        private sealed class Climb { public string Prefab; public float Top; }

        /// <summary>Each palette piece's measured footholds ("climb.ledges": height bands, from its foot, of faces 38° or flatter with room
        /// for the feet): a box top of a piece in a palette is a place to stand only where one of them is (a grate's or a pole's top isn't).
        /// Set by Palettes().</summary>
        private static readonly Dictionary<string, List<Vector2>> Ledges = new Dictionary<string, List<Vector2>>(StringComparer.Ordinal);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], string> NoTop =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], string>();
        public const float Thin = 0.3f;   // a standing part thinner than this (and taller than 1 m) has no top to stand on: bars, a grate on edge
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], Climb> Climbable =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], Climb>();

        /// <summary>Prefabs not to climb (env GAPNOCLIMB, comma-separated): to see what one prop's footholds open.</summary>
        private static readonly HashSet<string> NoClimb = new HashSet<string>(
            (Environment.GetEnvironmentVariable("GAPNOCLIMB") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

        /// <summary>Reads the palettes (tools/dungeons/palettes/*.json): every piece that isn't a "structure" piece and has footholds
        /// ("climb": {"top" or "top_skilled": more than 0}) can be climbed to that height. How many were read.</summary>
        public static int Palettes(string dir)
        {
            if (!Directory.Exists(dir)) return 0;
            foreach (string f in Directory.GetFiles(dir, "*.json"))
                try
                {
                    foreach (JObject e in (JObject.Parse(File.ReadAllText(f))["pieces"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        var climb = e["climb"] as JObject;
                        if (climb?["ledges"] is JArray bands && !string.IsNullOrEmpty((string)e["prefab"]))
                            Ledges[(string)e["prefab"]] = bands.OfType<JArray>().Where(l => l.Count == 2).Select(l => new Vector2((float)l[0], (float)l[1])).ToList();
                        float top = (float?)climb?["top"] ?? 0f, skilled = (float?)climb?["top_skilled"] ?? top;
                        if ((top > 0f || skilled > 0f) && (string)e["mount"] != "structure" && !string.IsNullOrEmpty((string)e["prefab"])
                            && !NoClimb.Contains((string)e["prefab"])) Climbers[(string)e["prefab"]] = new KeyValuePair<float, float>(top, skilled);
                    }
                }
                catch (Exception) { }
            return Climbers.Count;
        }
        private const float ClearLow = 1.2f, ClearHigh = 2.4f;   // the jump off a climbed prop: its body passes this high above where it lands
        // (a jump between places is checked on its arc: Arc in OneState)
        private const int MaxStates = 512;                        // combinations checked per room at most
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], string> Names =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Vector3[], string>();
        /// <summary>"room:a:b" (env GAPDEBUG): print how a's doorway reaches b's in the first state it does, hop by hop.</summary>
        private static readonly string Debug = Environment.GetEnvironmentVariable("GAPDEBUG");

        /// <summary>Each prefab's boxes in its own frame ([min, max] each): its one box from the bounds file, or, if the shapes file lists
        /// it ({prefab: [[lo, hi], ...]}), those boxes instead. Null if there's no bounds file.</summary>
        public static Dictionary<string, List<Vector3[]>> Bounds(string file, string shapes = null)
        {
            if (!File.Exists(file)) return null;
            Vector3[] Pair(JToken lo, JToken hi) => lo is JArray a && hi is JArray b
                ? new[] { new Vector3((float)a[0], (float)a[1], (float)a[2]), new Vector3((float)b[0], (float)b[1], (float)b[2]) } : null;
            var map = new Dictionary<string, List<Vector3[]>>();
            foreach (JProperty p in JObject.Parse(File.ReadAllText(file)).Properties())
                if (p.Value is JArray mm && mm.Count == 2 && Pair(mm[0], mm[1]) is Vector3[] box) map[p.Name] = new List<Vector3[]> { box };
            if (shapes != null && File.Exists(shapes))
                foreach (JProperty p in JObject.Parse(File.ReadAllText(shapes)).Properties())
                {
                    // (an empty list: a piece players walk through, vines, a rug, a web: no boxes at all)
                    if (p.Value is JArray none && none.Count == 0) { map[p.Name] = new List<Vector3[]>(); continue; }
                    var list = (p.Value as JArray ?? new JArray()).OfType<JArray>().Where(e => e.Count == 2).Select(e => Pair(e[0], e[1])).Where(b => b != null).ToList();
                    if (list.Count > 0) map[p.Name] = list;
                }
            return map;
        }

        /// <summary>A part's boxes in its room's frame (one per box of its prefab's shape).</summary>
        private static IEnumerable<Vector3[]> Boxes(PartDef p, Dictionary<string, List<Vector3[]>> bounds, MechDef mech, Pose pose)
        {
            if (bounds[p.Prefab].Count == 0) return Enumerable.Empty<Vector3[]>();   // (walked through: nothing)
            float foot = bounds[p.Prefab].Min(b => b[0].y);
            return bounds[p.Prefab].Select(local => Box(p, local, mech, pose, foot));
        }

        /// <summary>One line per finding for a room (each with how many of its states show it), "unchecked..." lines, or nothing.</summary>
        public static List<string> Check(Pack pack, RoomDef room, Dictionary<string, List<Vector3[]>> bounds)
        {
            var found = new List<string>();
            List<MechDef> moving = room.Mechs.Where(m => m.Joins.Count > 0).ToList();
            if (room.Parts.Count == 0 || (room.Doorways.Count < 2 && !room.Mechs.Any(m => m.Encounter != null))) return found;
            if (bounds == null) { found.Add("unchecked: no tools/dungeons/prefab_bounds.json"); return found; }

            var missing = new SortedSet<string>(room.Parts.Concat(room.Mechs.SelectMany(m => m.Parts)).Select(p => p.Prefab).Where(p => !bounds.ContainsKey(p)));
            var fixedBoxes = room.Parts.Where(p => bounds.ContainsKey(p.Prefab)).SelectMany(p => Boxes(p, bounds, null, null)).ToList();
            // Mechanisms that don't join anything stand in their start pose (levers, gears, gates: a gate's doorway is outside the room).
            // (a gate stands open: what it closes is a doorway's link, the solver's business, not the way through the room)
            foreach (MechDef m in room.Mechs.Where(m => m.Joins.Count == 0))
                fixedBoxes.AddRange(m.Parts.Where(p => bounds.ContainsKey(p.Prefab)).SelectMany(p => Boxes(p, bounds, m, PoseOf(m, Layouts.IsGate(m) ? 1 : m.Start))));

            int combos = 1;
            foreach (MechDef m in moving) combos = Math.Min(MaxStates + 1, combos * Layouts.States(m));
            var tally = new Dictionary<string, int>();   // finding -> states it shows in
            var nowhere = new SortedSet<string>();
            var pointsOff = new SortedSet<string>();
            int checkedStates = 0;
            var state = new int[moving.Count];
            for (int c = 0; c < Math.Min(combos, MaxStates); c++)
            {
                int rest = c;
                for (int k = 0; k < moving.Count; k++) { state[k] = rest % Layouts.States(moving[k]); rest /= Layouts.States(moving[k]); }
                var boxes = new List<Vector3[]>(fixedBoxes);
                for (int k = 0; k < moving.Count; k++)
                    boxes.AddRange(moving[k].Parts.Where(p => bounds.ContainsKey(p.Prefab)).SelectMany(p => Boxes(p, bounds, moving[k], PoseOf(moving[k], state[k]))));
                var pending = new List<KeyValuePair<string, string>>();   // rides blocked in this snapshot
                foreach (string f in OneState(pack, room, moving, state, boxes, nowhere, pending, null, null, c == 0 ? pointsOff : null)) tally[f] = tally.TryGetValue(f, out int n) ? n + 1 : 1;
                if (pending.Count > 0)
                {
                    // Riding: the platform in every pose at once; stepping on it anywhere carries the player to all its places.
                    var rideBoxes = new List<Vector3[]>(fixedBoxes);
                    var ride = new HashSet<Vector3[]>();
                    for (int k = 0; k < moving.Count; k++)
                    {
                        if (moving[k].Kind != "platform")
                        {
                            rideBoxes.AddRange(moving[k].Parts.Where(p => bounds.ContainsKey(p.Prefab)).SelectMany(p => Boxes(p, bounds, moving[k], PoseOf(moving[k], state[k]))));
                            continue;
                        }
                        for (int pose = 0; pose < Math.Max(1, moving[k].Poses.Count); pose++)
                            foreach (PartDef p in moving[k].Parts.Where(p => bounds.ContainsKey(p.Prefab)))
                                foreach (Vector3[] b in Boxes(p, bounds, moving[k], PoseOf(moving[k], pose)))
                                {
                                    rideBoxes.Add(b);
                                    ride.Add(b);
                                }
                    }
                    foreach (string f in OneState(pack, room, moving, state, rideBoxes, nowhere, null, ride, pending)) tally[f] = tally.TryGetValue(f, out int n) ? n + 1 : 1;
                }
                checkedStates++;
            }
            foreach (var t in tally.OrderBy(t => t.Key)) found.Add($"{t.Key} (in {t.Value} of {checkedStates} states)");
            if (combos > MaxStates) found.Add($"unchecked in part: only the first {MaxStates} combinations of its mechanisms' states");
            foreach (string p in pointsOff) found.Add($"{p}: not on a floor: blocked: creatures can't come there");
            if (missing.Count > 0) found.Add($"unchecked in part: no bounds for {string.Join(", ", missing)}");
            if (nowhere.Count > 0) found.Add($"unchecked in part: nothing to stand on at doorway {string.Join(", ", nowhere)}");
            return found;
        }

        /// <summary>One snapshot of a room. pending (when given) collects the rides (a platform's joins) that are blocked in it; with ride
        /// (the boxes of a platform in all its poses) and only, it checks just those rides, the platform's tops all one place.</summary>
        private static List<string> OneState(Pack pack, RoomDef room, List<MechDef> moving, int[] state, List<Vector3[]> boxes, SortedSet<string> nowhere,
            List<KeyValuePair<string, string>> pending, HashSet<Vector3[]> ride = null, List<KeyValuePair<string, string>> only = null, SortedSet<string> pointsOff = null)
        {
            // The columns: what's in each (intervals), and where one can stand.
            Vector3 half = room.Size * 0.5f;
            int nx = Math.Max(1, (int)Math.Ceiling(room.Size.x / Cell)), nz = Math.Max(1, (int)Math.Ceiling(room.Size.z / Cell));
            var solid = new List<Vector2>[nx * nz];
            var tops = new List<float>[nx * nz];
            var topBox = new Dictionary<long, Vector3[]>();
            for (int i = 0; i < solid.Length; i++) { solid[i] = new List<Vector2>(); tops[i] = new List<float>(); }
            foreach (Vector3[] b in boxes)
            {
                int x0 = Math.Max(0, (int)Math.Floor((b[0].x + half.x) / Cell)), x1 = Math.Min(nx - 1, (int)Math.Floor((b[1].x + half.x) / Cell - 1e-4));
                int z0 = Math.Max(0, (int)Math.Floor((b[0].z + half.z) / Cell)), z1 = Math.Min(nz - 1, (int)Math.Floor((b[1].z + half.z) / Cell - 1e-4));
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        solid[x * nz + z].Add(new Vector2(b[0].y, b[1].y));
                        float cx = -half.x + (x + 0.5f) * Cell, cz = -half.z + (z + 0.5f) * Cell;
                        if (cx >= b[0].x && cx <= b[1].x && cz >= b[0].z && cz <= b[1].z && !NoTop.TryGetValue(b, out _)) { tops[x * nz + z].Add(b[1].y); topBox[((long)(x * nz + z) << 20) ^ (long)Math.Round(b[1].y * 100)] = b; }
                    }
            }
            var spots = new List<KeyValuePair<int, float>>();   // (column, height)
            var spotsAt = new List<int>[nx * nz];
            for (int i = 0; i < solid.Length; i++)
            {
                spotsAt[i] = new List<int>();
                foreach (float y in tops[i].Distinct())
                    if (!solid[i].Any(s => s.x < y + Headroom && s.y > y + 0.05f))
                    {
                        spotsAt[i].Add(spots.Count);
                        spots.Add(new KeyValuePair<int, float>(i, y));
                    }
            }

            // Walking.
            var area = Enumerable.Range(0, spots.Count).ToArray();
            int Find(int a) { while (area[a] != a) a = area[a] = area[area[a]]; return a; }
            for (int x = 0; x < nx; x++)
                for (int z = 0; z < nz; z++)
                    foreach (int s in spotsAt[x * nz + z])
                        foreach (int t in (x + 1 < nx ? spotsAt[(x + 1) * nz + z] : new List<int>()).Concat(z + 1 < nz ? spotsAt[x * nz + z + 1] : new List<int>()))
                            if (Math.Abs(spots[s].Value - spots[t].Value) <= Step) area[Find(s)] = Find(t);
            if (ride != null)
            {
                int on = -1;
                for (int s = 0; s < spots.Count; s++)
                    if (topBox.TryGetValue(((long)spots[s].Key << 20) ^ (long)Math.Round(spots[s].Value * 100), out var b) && ride.Contains(b))
                    {
                        if (on < 0) on = s; else area[Find(s)] = Find(on);
                    }
            }
            var members = new Dictionary<int, List<int>>();
            for (int s = 0; s < spots.Count; s++)
            {
                int a = Find(s);
                if (!members.TryGetValue(a, out var list)) members[a] = list = new List<int>();
                list.Add(s);
            }

            // Jumps and falls: from each area, the areas it reaches (and on from them).
            int reachCells = (int)Math.Ceiling(Reach(-room.Size.y) / Cell);   // (the farthest: a jump down the room's whole height)
            var runSpeed = new Dictionary<int, float>();   // (--run-up: an area's speed after its longest straight run, its box's diagonal)
            if (RunUp)
                foreach (var m in members)
                {
                    int x0 = m.Value.Min(sp => spots[sp].Key / nz), x1 = m.Value.Max(sp => spots[sp].Key / nz);
                    int z0 = m.Value.Min(sp => spots[sp].Key % nz), z1 = m.Value.Max(sp => spots[sp].Key % nz);
                    float dx = (x1 - x0) * Cell, dz = (z1 - z0) * Cell;
                    runSpeed[m.Key] = SpeedAfter((float)Math.Sqrt(dx * dx + dz * dz));
                }
            bool Reaches(int from, int to)   // (one jump from spot to spot: across against the height it lands at)
            {
                float r = Reach(spots[to].Value - spots[from].Value, RunUp ? runSpeed[Find(from)] : -1f);
                if (r < 0f) return false;
                int ax = spots[from].Key / nz - spots[to].Key / nz, az = spots[from].Key % nz - spots[to].Key % nz;
                return (ax * ax + az * az) * Cell * Cell <= r * r;
            }
            // (an area is jumped from its edge: the spot nearest anything outside it is on its edge, and it's walked to)
            var edge = new bool[spots.Count];
            {
                var cellArea = new Dictionary<int, HashSet<int>>();
                for (int s = 0; s < spots.Count; s++)
                {
                    if (!cellArea.TryGetValue(spots[s].Key, out var set)) cellArea[spots[s].Key] = set = new HashSet<int>();
                    set.Add(Find(s));
                }
                var around = new[] { 1, 0, -1, 0, 0, 1, 0, -1 };
                for (int s = 0; s < spots.Count; s++)
                {
                    int c = spots[s].Key, cx = c / nz, cz = c % nz, a = Find(s);
                    for (int k = 0; k < 8 && !edge[s]; k += 2)
                    {
                        int x = cx + around[k], z = cz + around[k + 1];
                        if (x < 0 || z < 0 || x >= nx || z >= nz || !cellArea.TryGetValue(x * nz + z, out var there) || !there.Contains(a)) edge[s] = true;
                    }
                }
            }
            // (and landed on at its edge too, for the same reason: the jumps go edge to edge)
            var edgeAt = new List<int>[nx * nz];
            float lowest = spots.Count > 0 ? spots.Min(sp => sp.Value) : 0f;
            for (int s = 0; s < spots.Count; s++)
                if (edge[s]) (edgeAt[spots[s].Key] ?? (edgeAt[spots[s].Key] = new List<int>())).Add(s);
            // A jump's way through the air: the feet on the real arc (up at V, gravity 20, across at an even speed, landing on the way down,
            // or on the way up onto something higher), or a run off the edge with no jump (no rise: under a lintel a jump would hit), and
            // the body (1.8 m) clear of everything in each column it passes. Clear if any of them is, at a speed the run allows.
            bool Arc(int from, int to)
            {
                float y0 = spots[from].Value, h = spots[to].Value - y0;
                int ax = spots[from].Key / nz, az = spots[from].Key % nz, bx = spots[to].Key / nz, bz = spots[to].Key % nz;
                int steps = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az));
                float across = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) * Cell;
                float need = Math.Max(0f, across - BodyRadius - 0.4f * (1f + 0.4f * JumpSkill / 100f));   // (the push forward and the body's reach)
                foreach (float v in new[] { V, 0f })
                {
                    if (v == 0f && h >= 0f) continue;
                    float disc = v * v - 40f * h;
                    if (disc < 0f) continue;
                    foreach (float sign in v > 0f && h > 0f ? new[] { 1f, -1f } : new[] { 1f })
                    {
                        float t1 = (v + sign * (float)Math.Sqrt(disc)) / 20f;
                        if (t1 <= 0f || need > S * t1 + 1e-3f) continue;
                        bool clear = true;
                        for (int k = 1; k < steps && clear; k++)
                        {
                            float f = (float)k / steps, t = f * t1, y = y0 + v * t - 10f * t * t;
                            int x = (int)Math.Round(ax + (bx - ax) * f), z = (int)Math.Round(az + (bz - az) * f);
                            if (solid[x * nz + z].Any(s => s.x < y + Headroom - 0.1f && s.y > y + 0.05f)) clear = false;
                        }
                        if (clear) return true;
                    }
                }
                return false;
            }
            bool Clear(int from, int to, float hi, Vector3[] over = null)
            {
                float low = hi + ClearLow;
                int ax = spots[from].Key / nz, az = spots[from].Key % nz, bx = spots[to].Key / nz, bz = spots[to].Key % nz;
                int steps = Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az));
                for (int k = 1; k < steps; k++)
                {
                    int x = (int)Math.Round(ax + (bx - ax) * (float)k / steps), z = (int)Math.Round(az + (bz - az) * (float)k / steps);
                    // (a climb: the prop's own box is what the player is going up, not in the way; anything else beside it still is)
                    if (solid[x * nz + z].Any(s => s.x < hi + ClearHigh && s.y > low
                        && !(over != null && Math.Abs(s.x - over[0].y) < 0.001f && Math.Abs(s.y - over[1].y) < 0.001f))) return false;
                }
                return true;
            }
            var climbs = new Dictionary<int, HashSet<int>>();   // area -> areas a tall prop beside it lets the player climb to
            var climbVia = new Dictionary<long, KeyValuePair<KeyValuePair<int, int>, string>>();   // (from area, to area) -> ((a foot spot, the spot reached), the prop), for GAPDEBUG
            foreach (Vector3[] b in boxes)
            {
                if (!Climbable.TryGetValue(b, out Climb climb)) continue;
                float hold = b[0].y + climb.Top, top = hold + JumpUp;   // (its highest foothold, then a jump)
                // (from a foot spot, only as far as across the prop and a jump: a long beam or a flight of stairs isn't climbed at one end
                // and left from the other; walking along its top is the walk's business)
                float width = Math.Min(ClimbAcross, Math.Max(b[1].x - b[0].x, b[1].z - b[0].z));
                var foot = new List<int>();
                var up = new List<int>();
                for (int sp = 0; sp < spots.Count; sp++)
                {
                    int c = spots[sp].Key;
                    float cx = -half.x + (c / nz + 0.5f) * Cell, cz = -half.z + (c % nz + 0.5f) * Cell, y = spots[sp].Value;
                    float dx = Math.Max(0f, Math.Max(b[0].x - cx, cx - b[1].x)), dz = Math.Max(0f, Math.Max(b[0].z - cz, cz - b[1].z));
                    float d = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (d <= 1f && Math.Abs(y - b[0].y) <= Step + 0.5f) foot.Add(sp);
                    if (y > b[0].y + JumpUp && y <= top && d <= Reach(y - hold)) up.Add(sp);
                }
                foreach (int f in foot)
                {
                    int fa = Find(f);
                    if (!climbs.TryGetValue(fa, out var to)) climbs[fa] = to = new HashSet<int>();
                    int fc = spots[f].Key;
                    foreach (int u in up)
                    {
                        int uc = spots[u].Key;
                        float ax = (uc / nz - fc / nz) * Cell, az = (uc % nz - fc % nz) * Cell, across = Reach(spots[u].Value - hold) + width;
                        if (ax * ax + az * az > across * across || !Clear(f, u, spots[u].Value, b)) continue;   // (and nothing in the way of the jump off it)
                        if (Find(u) != fa && to.Add(Find(u))) climbVia[((long)fa << 32) | (uint)Find(u)] = new KeyValuePair<KeyValuePair<int, int>, string>(new KeyValuePair<int, int>(f, u), climb.Prefab);
                    }
                }
            }
            var hops = new Dictionary<long, HashSet<int>>();
            HashSet<int> Hops(int a, bool safe, bool parkour = false)
            {
                long hk = a * 4L + (safe ? 1 : 0) + (parkour ? 2 : 0);
                if (hops.TryGetValue(hk, out var set)) return set;
                set = hops[hk] = new HashSet<int>();
                if (climbs.TryGetValue(a, out var climbed)) set.UnionWith(climbed);   // (up a tall prop: no fall)
                foreach (int s in members[a])
                {
                    if (!edge[s] && !AllSpots) continue;
                    int sx = spots[s].Key / nz, sz = spots[s].Key % nz;
                    // (no further than a jump down to the lowest place there is, or to a safe fall)
                    int rc = Math.Min(reachCells, (int)Math.Ceiling(Reach(Math.Max(lowest - spots[s].Value, safe ? -SafeFall : float.MinValue)) / Cell));
                    for (int x = Math.Max(0, sx - rc); x <= Math.Min(nx - 1, sx + rc); x++)
                        for (int z = Math.Max(0, sz - rc); z <= Math.Min(nz - 1, sz + rc); z++)
                        {
                            if ((x - sx) * (x - sx) + (z - sz) * (z - sz) > rc * rc) continue;
                            var at = AllSpots ? spotsAt[x * nz + z] : edgeAt[x * nz + z];
                            if (at == null) continue;
                            foreach (int t in at)
                            {
                                int b = Find(t);
                                float dy = spots[t].Value - spots[s].Value;
                                bool climbUp = parkour && dy > JumpUp && dy <= ParkourUp && (x - sx) * (x - sx) + (z - sz) * (z - sz) <= (ParkourAcross / Cell) * (ParkourAcross / Cell);
                                if (b == a || set.Contains(b) || (safe && dy < -SafeFall) || !(climbUp || Reaches(s, t))) continue;
                                if (climbUp ? Clear(s, t, Math.Max(spots[s].Value, spots[t].Value)) : Arc(s, t)) set.Add(b);
                            }
                        }
                }
                return set;
            }
            HashSet<int> From(int a, bool safe = false, bool parkour = false)
            {
                var seen = new HashSet<int> { a };
                var todo = new Stack<int>();
                todo.Push(a);
                while (todo.Count > 0) foreach (int b in Hops(todo.Pop(), safe, parkour)) if (seen.Add(b)) todo.Push(b);
                return seen;
            }

            pointsOff = pointsOff ?? new SortedSet<string>();
            // An encounter's points must be places to stand (the creatures come there): a spot from 1.5 m below to 0.5 m above each.
            foreach (MechDef m in room.Mechs.Where(m => m.Encounter != null))
                foreach (Vector3 p in m.Encounter.Points)
                {
                    int x = (int)Math.Floor((p.x + half.x) / Cell), z = (int)Math.Floor((p.z + half.z) / Cell);
                    bool on = x >= 0 && z >= 0 && x < nx && z < nz && spotsAt[x * nz + z].Any(sp => spots[sp].Value >= p.y - 1.5f && spots[sp].Value <= p.y + 0.5f);
                    if (!on) pointsOff.Add($"{m.Id} point {p}");
                }

            // Each doorway's area.
            var doorArea = new Dictionary<string, int>();
            foreach (Doorway d in room.Doorways)
            {
                int best = -1;
                for (float depth = 1f; depth <= 3f && best < 0; depth += 1f)   // (a gate in the doorway can stand on the first metre)
                {
                    Vector3 p = d.At + d.Facing * depth;
                    int x = (int)Math.Floor((p.x + half.x) / Cell), z = (int)Math.Floor((p.z + half.z) / Cell);
                    if (x >= 0 && z >= 0 && x < nx && z < nz)
                        foreach (int s in spotsAt[x * nz + z])
                            if (Math.Abs(spots[s].Value - p.y) <= 1f && (best < 0 || Math.Abs(spots[s].Value - p.y) < Math.Abs(spots[best].Value - p.y))) best = s;
                }
                if (best < 0) nowhere.Add(d.Id); else doorArea[d.Id] = Find(best);
            }

            // The solver in this state: "inside" (or all, with no joins), these joins both ways, drops one way.
            var ids = room.Doorways.Select(d => d.Id).ToList();
            var edges = new List<KeyValuePair<string, string>>();
            var rides = new HashSet<KeyValuePair<string, string>>();   // joins made by riding a platform: a snapshot can't check them
            void Both(string a, string b) { edges.Add(new KeyValuePair<string, string>(a, b)); edges.Add(new KeyValuePair<string, string>(b, a)); }
            foreach (Join j in room.Inside) Both(j.A, j.B);
            if (room.Inside.Count == 0 && moving.Count == 0)   // (a room with neither joins all its doorways)
                foreach (string other in ids.Skip(1)) Both(ids[0], other);
            for (int k = 0; k < moving.Count; k++)
                foreach (Join j in moving[k].Joins.Where(j => j.State < 0 || j.State == state[k]))
                {
                    Both(j.A, j.B);
                    if (moving[k].Kind == "platform") { rides.Add(new KeyValuePair<string, string>(j.A, j.B)); rides.Add(new KeyValuePair<string, string>(j.B, j.A)); }
                }
            foreach (Join j in PackReader.Drops(pack, room)) edges.Add(new KeyValuePair<string, string>(j.A, j.B));
            HashSet<string> Solver(string id)
            {
                var seen = new HashSet<string> { id };
                var todo = new Stack<string>();
                todo.Push(id);
                while (todo.Count > 0) { string x = todo.Pop(); foreach (var e in edges) if (e.Key == x && seen.Add(e.Value)) todo.Push(e.Value); }
                return seen;
            }

            string Spot(int sp)
            {
                int c = spots[sp].Key;
                string what = topBox.TryGetValue(((long)c << 20) ^ (long)Math.Round(spots[sp].Value * 100), out var bx) && Names.TryGetValue(bx, out string nm) ? nm : "?";
                return $"({-half.x + (c / nz + 0.5f) * Cell:0.0}, {spots[sp].Value:0.0}, {-half.z + (c % nz + 0.5f) * Cell:0.0}) on {what}";
            }

            if (Debug != null && Debug.Split(':')[0] == room.Name && doorArea.ContainsKey(Debug.Split(':')[1]) && doorArea.ContainsKey(Debug.Split(':')[2])
                && !Solver(Debug.Split(':')[1]).Contains(Debug.Split(':')[2]) && From(doorArea[Debug.Split(':')[1]]).Contains(doorArea[Debug.Split(':')[2]]))
            {
                // Breadth first over areas, remembering the hop (spot to spot) into each.
                int start = doorArea[Debug.Split(':')[1]], goal = doorArea[Debug.Split(':')[2]];
                bool safeOnly = From(start, true).Contains(goal);   // (show the safe route when there is one)
                var via = new Dictionary<int, KeyValuePair<int, int>> { [start] = new KeyValuePair<int, int>(-1, -1) };
                var q = new Queue<int>();
                q.Enqueue(start);
                var climbed = new Dictionary<int, string>();   // area -> the prop climbed into it
                while (q.Count > 0 && !via.ContainsKey(goal))
                {
                    int a = q.Dequeue();
                    if (climbs.TryGetValue(a, out var up))
                        foreach (int b in up)
                            if (!via.ContainsKey(b) && climbVia.TryGetValue(((long)a << 32) | (uint)b, out var cv))
                            {
                                via[b] = cv.Key;
                                climbed[b] = cv.Value;
                                q.Enqueue(b);
                            }
                    foreach (int s in members[a])
                    {
                        int sx = spots[s].Key / nz, sz = spots[s].Key % nz;
                        for (int x = Math.Max(0, sx - reachCells); x <= Math.Min(nx - 1, sx + reachCells); x++)
                            for (int z = Math.Max(0, sz - reachCells); z <= Math.Min(nz - 1, sz + reachCells); z++)
                                foreach (int t in spotsAt[x * nz + z])
                                {
                                    int b = Find(t);
                                    if (via.ContainsKey(b) || !Reaches(s, t)) continue;
                                    if (safeOnly && spots[t].Value - spots[s].Value < -SafeFall) continue;
                                    if (!Arc(s, t)) continue;
                                    via[b] = new KeyValuePair<int, int>(s, t);
                                    q.Enqueue(b);
                                }
                    }
                }
                string Where(int s)
                {
                    int c = spots[s].Key;
                    string what = topBox.TryGetValue(((long)c << 20) ^ (long)Math.Round(spots[s].Value * 100), out var bx) && Names.TryGetValue(bx, out string nm) ? nm : "?";
                    return $"({-half.x + (c / nz + 0.5f) * Cell:0.0}, {spots[s].Value:0.0}, {-half.z + (c % nz + 0.5f) * Cell:0.0}) on {what}";
                }
                var chain = new List<string>();
                for (int a = goal; via.ContainsKey(a) && via[a].Key >= 0; a = Find(via[a].Key))
                    chain.Add(climbed.TryGetValue(a, out string prop) ? $"  climb {prop} from {Where(via[a].Key)} up to {Where(via[a].Value)}" : $"  hop {Where(via[a].Key)} -> {Where(via[a].Value)}");
                chain.Reverse();
                Console.WriteLine($"GAPDEBUG {room.Name} state {string.Join(",", state)}: {Debug.Split(':')[1]} -> {Debug.Split(':')[2]}{(safeOnly ? " (no fall over 4 m)" : " (with a fall)")}" + Environment.NewLine + string.Join(Environment.NewLine, chain));
            }

            // Each way the solver doesn't know: a walk or jump (no fall over SafeFall on it: close it off), or one that needs a fall (declare
            // it in "drops" and the solver counts it, or close it off). A pair that goes both ways the same is said once.
            var routes = new Dictionary<KeyValuePair<string, string>, string>();
            var full = new Dictionary<int, HashSet<int>>();
            var safeFrom = new Dictionary<int, HashSet<int>>();
            foreach (string a in ids.Where(doorArea.ContainsKey))
            {
                int aa = doorArea[a];
                HashSet<int> there = full.TryGetValue(aa, out var f) ? f : full[aa] = From(aa);
                HashSet<string> solver = Solver(a);
                foreach (string b in ids.Where(doorArea.ContainsKey))
                {
                    if (a == b || solver.Contains(b) || !there.Contains(doorArea[b])) continue;
                    HashSet<int> safe = safeFrom.TryGetValue(aa, out var sf) ? sf : safeFrom[aa] = From(aa, true);
                    routes[new KeyValuePair<string, string>(a, b)] = aa == doorArea[b] ? "walkable" : safe.Contains(doorArea[b]) ? "jumpable" : $"only with a fall of over {SafeFall:0} m";
                }
            }
            // And the other way round: each join the solver counts (inside, a mechanism's in this state, a drop) must be walkable,
            // jumpable or fallable here, or the dungeon may be impossible where the solver says it's fine.
            var blocked = new HashSet<KeyValuePair<string, string>>();
            foreach (var e in edges.Distinct())
            {
                if (!doorArea.ContainsKey(e.Key) || !doorArea.ContainsKey(e.Value) || e.Key == e.Value) continue;
                if (only != null && !only.Contains(e)) continue;
                int aa = doorArea[e.Key];
                HashSet<int> there = full.TryGetValue(aa, out var f) ? f : full[aa] = From(aa);
                if (there.Contains(doorArea[e.Value])) continue;
                if (pending != null && rides.Contains(e)) { pending.Add(e); continue; }   // (a ride: checked again, riding)
                blocked.Add(e);
                if (Debug != null && Debug == $"{room.Name}:{e.Key}:{e.Value}")
                {
                    // The nearest the player gets: of what A reaches, the spot closest (across, then up or down) to any spot of B's area.
                    int bestS = -1, bestT = -1;
                    float bestD = float.MaxValue;
                    foreach (int ar in there)
                        foreach (int sp in members[ar])
                            foreach (int tp in members[doorArea[e.Value]])
                            {
                                float dx = (spots[sp].Key / nz - spots[tp].Key / nz) * Cell, dz = (spots[sp].Key % nz - spots[tp].Key % nz) * Cell;
                                float d = (float)Math.Sqrt(dx * dx + dz * dz) + Math.Abs(spots[sp].Value - spots[tp].Value) * 0.5f;
                                if (d < bestD) { bestD = d; bestS = sp; bestT = tp; }
                            }
                    if (bestS >= 0)
                    {
                        float hi = Math.Max(spots[bestS].Value, spots[bestT].Value);
                        Console.WriteLine($"GAPDEBUG {room.Name} state {string.Join(",", state)}: {e.Key} -> {e.Value} blocked; nearest: {Spot(bestS)} to {Spot(bestT)}" +
                                          $" ({bestD:0.0}), {(Arc(bestS, bestT) ? "nothing in the way of a jump" : "something in the way of a jump")}");
                    }
                }
            }
            var found = new List<string>();
            foreach (var b in blocked)
            {
                bool both = blocked.Contains(new KeyValuePair<string, string>(b.Value, b.Key));
                if (both && string.CompareOrdinal(b.Key, b.Value) > 0) continue;
                found.Add($"{b.Key} {(both ? "<->" : "->")} {b.Value}: blocked: the solver joins them, but there's no way to walk or jump it");
            }
            if (only != null) return found;   // (the rides only)
            if (PackReader.Parkour(pack))   // (Quad's Parkour allowed inside: what it adds; a failure only when the pack asks for it)
                foreach (string a in ids.Where(doorArea.ContainsKey))
                {
                    int aa = doorArea[a];
                    HashSet<int> there = full.TryGetValue(aa, out var f0) ? f0 : full[aa] = From(aa);
                    HashSet<int> pk = From(aa, false, true);
                    HashSet<string> solver = Solver(a);
                    foreach (string b in ids.Where(doorArea.ContainsKey))
                        if (a != b && !solver.Contains(b) && !there.Contains(doorArea[b]) && pk.Contains(doorArea[b]))
                            found.Add($"{a} -> {b}: only with Parkour (a wall climb or ledge grab), and the solver doesn't know it" +
                                (PackReader.ParkourSaid(pack) ? " (the pack says \"parkour\": true: close it off)" : " (information)"));
                }
            foreach (var r in routes)
            {
                string a = r.Key.Key, b = r.Key.Value;
                bool both = routes.TryGetValue(new KeyValuePair<string, string>(b, a), out string kind) && kind == r.Value;
                if (both && string.CompareOrdinal(a, b) > 0) continue;
                found.Add($"{a} {(both ? "<->" : "->")} {b}: {r.Value}, and the solver doesn't know it");
            }
            return found;
        }

        private static Pose PoseOf(MechDef m, int state) => m.Poses.Count == 0 ? null : m.Poses[Math.Max(0, Math.Min(m.Poses.Count - 1, state))];

        /// <summary>A part's box in its room's frame: its prefab's box scaled and placed as the part, then (a mechanism's) by the pose and
        /// the mechanism, turned as Unity's Quaternion.Euler does (z, then x, then y), then made axis-aligned. Plain arithmetic: the
        /// engine's rotation code doesn't run outside the game.</summary>
        private static Vector3[] Box(PartDef p, Vector3[] local, MechDef mech, Pose pose, float foot)
        {
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            for (int c = 0; c < 8; c++)
            {
                var v = new Vector3((c & 1) == 0 ? local[0].x : local[1].x, (c & 2) == 0 ? local[0].y : local[1].y, (c & 4) == 0 ? local[0].z : local[1].z);
                v = p.At + Rotate(new Vector3(v.x * p.Scale.x, v.y * p.Scale.y, v.z * p.Scale.z), p.Euler);
                if (pose != null) v = pose.At + Rotate(v, pose.Euler);
                if (mech != null) v = mech.At + Rotate(v, mech.Euler);
                min = new Vector3(Math.Min(min.x, v.x), Math.Min(min.y, v.y), Math.Min(min.z, v.z));
                max = new Vector3(Math.Max(max.x, v.x), Math.Max(max.y, v.y), Math.Max(max.z, v.z));
            }
            var box = new[] { min, max };
            Names.Add(box, p.Prefab + (mech != null ? " (" + mech.Id + ")" : ""));
            bool upright = Upright(p.Euler) && (pose == null || Upright(pose.Euler)) && (mech == null || Upright(mech.Euler));
            // (its top a place to stand: not on a thin standing part, and, upright, only where the palette measured a foothold)
            float top = local[1].y - foot;
            if ((Math.Min(max.x - min.x, max.z - min.z) < Thin && max.y - min.y > 1f)
                || (upright && Ledges.TryGetValue(p.Prefab, out var bands) && !bands.Any(l => top >= l.x - 0.25f && top <= l.y + 0.25f)))
                NoTop.Add(box, p.Prefab);
            // (footholds only as the prefab stands: turned off upright, a part is climbed by its top end alone, as any box)
            // (how high: its footholds as this part is stretched, climbed from its foot band by band, each within a jump of the last: a post
            // stretched to 7 m has its top face 7 m up, out of reach, not "climbable to 7 m")
            if (Climbers.ContainsKey(p.Prefab) && upright && Ledges.TryGetValue(p.Prefab, out var held))
            {
                float reach = 0f;
                foreach (Vector2 band in held.OrderBy(x => x.x))
                    if (band.x * p.Scale.y <= reach + JumpUp) reach = Math.Max(reach, band.y * p.Scale.y);
                if (reach > 0f) Climbable.Add(box, new Climb { Prefab = p.Prefab, Top = reach });
            }
            return box;
        }

        private static bool Upright(Vector3 euler)
        {
            float Off(float a) { a = ((a % 360f) + 360f) % 360f; return Math.Min(a, 360f - a); }
            return Off(euler.x) < 5f && Off(euler.z) < 5f;
        }

        private static Vector3 Rotate(Vector3 v, Vector3 euler)
        {
            double r = Math.PI / 180.0;
            double cz = Math.Cos(euler.z * r), sz = Math.Sin(euler.z * r), cx = Math.Cos(euler.x * r), sx = Math.Sin(euler.x * r),
                   cy = Math.Cos(euler.y * r), sy = Math.Sin(euler.y * r);
            double x = v.x * cz - v.y * sz, y = v.x * sz + v.y * cz, z = v.z;      // about z
            double y2 = y * cx - z * sx, z2 = y * sx + z * cx;                       // about x
            double x3 = x * cy + z2 * sy, z3 = -x * sy + z2 * cy;                    // about y
            return new Vector3((float)x3, (float)y2, (float)z3);
        }
    }
}
