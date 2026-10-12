using System;
using System.Collections.Generic;
using System.Linq;

namespace Dungeons
{
    /// <summary>
    /// The puzzle solver: proves a laid-out dungeon can be finished, and finds one shortest way (Layout.Solution).
    ///
    /// The dungeon as a graph of places: each placed room's doorways. A link between two rooms joins its two doorways, unless a gate or
    /// door mechanism in either room closes that doorway (state 0). Inside a room, doorways are joined by its "inside" joins (always), or
    /// all of them when it has neither "inside" nor mechanisms with "joins"; and by its mechanisms' joins in the state each is in.
    ///
    /// A search over (every changing mechanism's state, the part of the graph the player is in): from the entrance, all at their start.
    ///   - using a lever or plate the player can reach (standing on its "reachedFrom" doorway's side, else anywhere in its room) applies
    ///     its wires ("next", "set:n") and costs one use; the player is then where the lever is, and may find the way back gone. A "hold"
    ///     plate lets go when stepped off, so alone it counts for nothing; Solve(..., together: true) counts it as held down for good
    ///     (a second player or a creature on it), to tell a puzzle that needs two from one that can't be done;
    ///   - a room tagged "fight" opens its gates once the fight is won: the solver takes them as open (the fight is the player's job);
    ///   - a breakable wall is passable (a player can break it); Solve(..., standing: true) keeps every one standing, to show nothing needs
    ///     a secret room;
    ///   - a shortcut's pair joins its two ends once the far end has been reached (a remembered flag in the search), so it can't take the
    ///     player anywhere before they've been there (Solve(..., noShortcuts: true) leaves it out, to show it never shortens a solution);
    ///   - an encounter (a fight's waves) is taken as won: its wires apply, for free, once its place is reached (it starts on entering
    ///     or on opening its chest), or as part of the use of the lever or plate whose "start" wire starts it;
    ///   - a mechanism with a cycle turns by itself: the player may wait anywhere for any of its states (free), so its joins are all open
    ///     to the player, as if at once, and a cycling gate is passable (its real timing isn't modelled); levers acting on it change
    ///     nothing the solver counts. That keeps it out of the search: a dungeon with two turning rooms is no bigger to search. One with
    ///     "cycleIn" cycles only while it's set to one of those states: then all its joins are open (a gate of it passable); in any
    ///     other state it's an ordinary mechanism, set by levers;
    /// A room's "drops" (PackReader.Drops: jumping off a balcony) take the player from one doorway to another, one way, for free.
    /// Breadth first by uses (waiting costs none), so the first time the end's room is reached is with the fewest uses. The search goes on
    /// to see every step's room reached too. States are few (a handful of mechanisms of 2-4 states); it gives up past SolverLimit.
    /// </summary>
    internal static partial class Layouts
    {
        private const int SolverLimit = 200000;      // search states before giving up
        private const long MaxCodes = 10000000;      // mechanism state combinations before giving up

        /// <summary>How many states a mechanism has: its poses, the states its joins name, and a gate or door at least two.</summary>
        public static int States(MechDef m)
        {
            if (m == null) return 1;
            int n = Math.Max(1, m.Poses.Count);
            foreach (Join j in m.Joins) n = Math.Max(n, j.State + 1);
            if (m.SpinScale != null) n = Math.Max(n, m.SpinScale.Count);
            if (m.CycleIn != null && m.CycleIn.Count > 0) n = Math.Max(n, m.CycleIn.Max() + 1);
            if (IsGate(m)) n = Math.Max(n, 2);
            return n;
        }

        /// <summary>A gate, door or lock: closes its Doorway in state 0.</summary>
        public static bool IsGate(MechDef m) => m != null && (m.Kind == "gate" || m.Kind == "door" || m.Kind == "lock");   // (a lock: a gate a key opens)

        /// <summary>A lever or plate: what players use.</summary>
        public static bool IsUsable(MechDef m) => m != null && (m.Kind == "lever" || m.Kind == "plate");

        /// <summary>A mechanism's state after a wire acts on it ("next" wraps; "set:n"; anything else leaves it).</summary>
        public static int After(MechDef target, int state, string act)
        {
            int n = States(target);
            if (act == "next") return (state + 1) % n;
            if (act != null && act.StartsWith("set:") && int.TryParse(act.Substring(4), out int to)) return Math.Max(0, Math.Min(n - 1, to));
            return state;
        }

        /// <summary>Solves a layout's puzzle: fills solution with one shortest list of uses (mechanism keys) from the entrance to the end
        /// (the last plan step's room). Null if it can be done and every plan step's room can be reached, else why not.</summary>
        internal static string Solve(Layout layout, List<string> solution, bool together = false, bool standing = false, bool noShortcuts = false)
        {
            solution.Clear();
            try { return new Puzzle(layout, together, standing, noShortcuts).Run(solution); }
            catch (Exception e) { return "the solver failed: " + e.GetType().Name + ": " + e.Message; }
        }

        /// <summary>Every doorway (Key(room, doorway)) the player can stand at, with the fewest uses it takes: the layout as it is so
        /// far (the spine grows the next step from these). Empty if the search can't be made.</summary>
        internal static Dictionary<long, int> UsesToReach(Layout layout)
        {
            var map = new Dictionary<long, int>();
            try
            {
                var puzzle = new Puzzle(layout);
                puzzle.Run(new List<string>());
                foreach (PlacedRoom r in layout.Rooms)
                    for (int d = 0; d < r.Def.Doorways.Count; d++)
                    {
                        int uses = puzzle.NodeUses(puzzle.Base(r.Index) + d);
                        if (uses >= 0) map[Key(r.Index, d)] = uses;
                    }
            }
            catch (Exception) { }
            return map;
        }

        private sealed class Puzzle
        {
            private sealed class Mech { public int Room; public MechDef Def; public string Key; public int Slot = -1; }
            private sealed class Use
            {
                public string Key;
                public int[] Nodes;                // where the player must be (places, after the constructor)
                public bool Anywhere;              // a chained trigger: wherever the player is
                public readonly List<KeyValuePair<int, string>> Acts = new List<KeyValuePair<int, string>>();   // always: slot, act
                public readonly List<Part> Parts = new List<Part>();   // the triggers' parts, each when its guard holds
            }

            /// <summary>A trigger's part of a use: applied when its guard holds (on the states as they were when it's used), at most once with a
            /// flag, taking and giving key items (counters).</summary>
            private sealed class Part
            {
                public readonly List<int[]> Guard = new List<int[]>();                  // mechanism (index in _mechs), state, not (1: !=)
                public readonly List<KeyValuePair<int, string>> Acts = new List<KeyValuePair<int, string>>();
                public readonly List<KeyValuePair<int, int>> Needs = new List<KeyValuePair<int, int>>(), Gives = new List<KeyValuePair<int, int>>();
                public int Flag = -1;                                                    // a Once part's flag slot
                public Part After;                                                       // applied only if this part was (a trigger on a block's use:
                                                                                         // no place without the item placed)
            }

            private readonly Layout _l;
            private readonly int[] _base;                 // per room: its first doorway's node
            private readonly int[] _roomOf;               // per node: its room
            private readonly int _n;                      // nodes
            private readonly List<Mech> _mechs = new List<Mech>();
            private readonly List<Mech> _slots = new List<Mech>();        // the mechanisms whose state can change, in code order
            private readonly long[] _mult;
            private readonly List<Use> _uses = new List<Use>();
            private readonly List<Use> _won = new List<Use>();             // encounters won where they're reached: free, not in the solution
            private readonly Dictionary<int, List<int>> _freeStates = new Dictionary<int, List<int>>();   // mechanism -> states the player picks at will
            private readonly List<KeyValuePair<int[], int[][]>> _teleports = new List<KeyValuePair<int[], int[][]>>();   // (from nodes + to node), guard
            public readonly List<string> TriggerProblems = new List<string>();
            private readonly List<KeyValuePair<int, int>> _drops = new List<KeyValuePair<int, int>>();   // one-way: from node, to node
            private readonly List<int>[] _gatesAt;        // per node: the mechanisms (index in _mechs) closing it in state 0
            // Doorways joined whatever the changing mechanisms do are one "place" (most of a dungeon is a few places); the search only
            // joins places, by the joins that change: a mechanism's join in one state, a link a changing gate stands in.
            private readonly int[] _place;                // per node: its place
            private readonly int _m;                      // places
            private readonly List<int[]> _joinEdges = new List<int[]>();  // place, place, mechanism (index in _mechs), state
            private readonly List<KeyValuePair<int[], int[]>> _gateEdges = new List<KeyValuePair<int[], int[]>>();  // (place, place), gates
            private readonly Dictionary<long, int[]> _parts = new Dictionary<long, int[]>();   // code -> per place: its part's lowest place
            private readonly int[] _firstUses;            // per place, after Run: the fewest uses to stand there, or -1 never

            public Puzzle(Layout layout, bool together = false, bool standing = false, bool noShortcuts = false)
            {
                _l = layout;
                _base = new int[layout.Rooms.Count];
                for (int r = 0; r < layout.Rooms.Count; r++) { _base[r] = _n; _n += layout.Rooms[r].Def.Doorways.Count; }
                _roomOf = new int[_n];
                for (int r = 0; r < layout.Rooms.Count; r++)
                    for (int d = 0; d < layout.Rooms[r].Def.Doorways.Count; d++) _roomOf[_base[r] + d] = r;
                _gatesAt = new List<int>[_n];
                foreach (PlacedRoom r in layout.Rooms)
                    foreach (Join j in PackReader.Drops(layout.Pack, r.Def))
                        if (Node(r.Index, j.A) is int a && Node(r.Index, j.B) is int b) _drops.Add(new KeyValuePair<int, int>(a, b));

                var byKey = new Dictionary<string, Mech>();
                foreach (PlacedRoom r in layout.Rooms)
                    foreach (MechDef m in r.Def.Mechs)
                    {
                        var mech = new Mech { Room = r.Index, Def = m, Key = r.Index + "." + m.Id };
                        if (byKey.ContainsKey(mech.Key)) continue;
                        byKey[mech.Key] = mech;
                        _mechs.Add(mech);
                        if (IsGate(m) && Node(r.Index, m.Doorway) is int at)
                            (_gatesAt[at] ?? (_gatesAt[at] = new List<int>())).Add(_mechs.Count - 1);
                    }

                // The triggers and blocks (TriggerSolve), as rules on mechanism keys.
                TriggerSolve.Model tm = null;
                try { tm = TriggerSolve.For(layout); } catch (Exception e) { TriggerProblems.Add("the triggers can't be read by the solver: " + e.Message); }
                tm = tm ?? new TriggerSolve.Model();
                TriggerProblems.AddRange(tm.Problems);
                var guarded = new HashSet<string>(tm.Rules.SelectMany(r => r.Guard).Concat(tm.Teleports.SelectMany(t => t.Guard)).Select(g => g.Key).Where(k => k != null));
                foreach (KeyValuePair<string, List<int>> f in tm.Free)
                    if (byKey.TryGetValue(f.Key, out Mech fm)) _freeStates[_mechs.IndexOf(fm)] = f.Value;
                Mech Flag(string key, int states)
                {
                    var def = new MechDef { Id = key, Kind = "flag" };
                    for (int i = 0; i < states; i++) def.Poses.Add(new Pose());
                    var flag = new Mech { Room = 0, Def = def, Key = key };
                    _mechs.Add(flag);
                    SlotOf(flag);
                    return flag;
                }
                var counters = new Dictionary<string, int>();   // key item -> its counter's slot
                foreach (KeyValuePair<string, int> k in tm.KeyCounts) counters[k.Key] = Flag("item:" + k.Key, Math.Max(1, k.Value) + 1).Slot;
                var into = tm.Rules.Where(r => r.Into != null).GroupBy(r => r.Into).ToDictionary(g => g.Key, g => g.ToList());

                // The uses, and which mechanisms they (or time, or triggers) change: only those get a slot in the code, and only if their
                // state can change the way (joins, a gate's doorway) or a trigger waits on it: a gear that's only for the look changes nothing.
                void AddAct(Use use, Part part, string key, string act, int depth)
                {
                    if (key == null || !byKey.TryGetValue(key, out Mech target)) return;
                    if (act == "start")   // (an encounter started is won: its wires, and the triggers on its win)
                    {
                        if (target.Def.Kind != "encounter" || depth >= 4) return;
                        var wires = (target.Def.Encounter?.Boss?.AllWires() ?? Enumerable.Empty<Wire>()).Concat(target.Def.Wires);
                        foreach (Wire w in wires)
                            if (w.Do != "hold" || together) AddAct(use, part, Target(layout, target.Room, w), w.Do == "hold" ? "set:1" : w.Do, depth + 1);
                        if (into.TryGetValue(target.Key, out var won))
                            foreach (TriggerSolve.Rule r in won)
                            {
                                Part p = ToPart(use, r, depth + 1);
                                if (p == null) continue;
                                if (part != null) { p.Guard.InsertRange(0, part.Guard); p.After = part; }
                                use.Parts.Add(p);
                            }
                        return;
                    }
                    if ((!Matters(target.Def) && !guarded.Contains(target.Key)) || Free(target) || _freeStates.ContainsKey(_mechs.IndexOf(target))) return;
                    var a = new KeyValuePair<int, string>(SlotOf(target), act);
                    if (part != null) part.Acts.Add(a); else use.Acts.Add(a);
                }
                Part ToPart(Use use, TriggerSolve.Rule r, int depth)
                {
                    var p = new Part();
                    foreach (TriggerSolve.Guard g in r.Guard)
                    {
                        if (g.Key == null || !byKey.TryGetValue(g.Key, out Mech gm)) return null;   // (a guard on nothing: it never holds)
                        p.Guard.Add(new[] { _mechs.IndexOf(gm), g.State, g.Not ? 1 : 0 });
                    }
                    foreach (KeyValuePair<string, int> n in r.Needs) { if (!counters.TryGetValue(n.Key, out int c)) return null; p.Needs.Add(new KeyValuePair<int, int>(c, n.Value)); }
                    foreach (KeyValuePair<string, int> n in r.Gives) if (counters.TryGetValue(n.Key, out int c)) p.Gives.Add(new KeyValuePair<int, int>(c, n.Value));
                    foreach (TriggerSolve.Act a in r.Acts) AddAct(use, p, a.Key, a.Do, depth);
                    if (r.Once) p.Flag = Flag("once:" + r.Key + "#" + _mechs.Count, 2).Slot;
                    return p;
                }
                int[] RoomNodes(int room, IEnumerable<string> ids)
                {
                    int[] all = Enumerable.Range(_base[room], layout.Rooms[room].Def.Doorways.Count).ToArray();
                    if (ids == null) return all;
                    return ids.Select(id => Node(room, id)).Where(n => n != null).Select(n => n.Value).ToArray();
                }
                var made = new Dictionary<string, Use>();
                foreach (Mech m in _mechs.ToList())
                {
                    if (m.Def.Kind == "flag") continue;
                    string on = PackReader.StartOn(layout.Pack, layout.Rooms[m.Room].Def, m.Def);
                    if (!IsUsable(m.Def) && (on == null || on == "wire")) continue;
                    var use = new Use { Key = m.Key, Nodes = RoomNodes(m.Room, string.IsNullOrEmpty(m.Def.ReachedFrom) ? null : new[] { m.Def.ReachedFrom }) };
                    if (on != null) AddAct(use, null, m.Key, "start", 0);   // (an encounter won where it's reached)
                    else
                        foreach (Wire w in m.Def.Wires)
                        {
                            if (w.Do == "hold" && !together) continue;
                            AddAct(use, null, Target(layout, m.Room, w), w.Do == "hold" ? "set:1" : w.Do, 0);
                        }
                    if (on == null && into.TryGetValue(m.Key, out var rules))   // (triggers on this lever or plate)
                        foreach (TriggerSolve.Rule r in rules) { Part p = ToPart(use, r, 0); if (p != null) use.Parts.Add(p); }
                    made[m.Key] = use;
                    if (use.Acts.Count == 0 && use.Parts.Count == 0) continue;
                    (on != null ? _won : _uses).Add(use);
                }
                foreach (TriggerSolve.Rule r in tm.Rules.Where(r => r.Into == null))   // (a trigger's or block's own use, or a free trigger)
                {
                    if (r.Room < 0 || r.Room >= layout.Rooms.Count) continue;
                    var use = new Use { Key = r.Key, Nodes = RoomNodes(r.Room, r.Nodes), Anywhere = r.Anywhere };
                    Part p = ToPart(use, r, 0);
                    if (p == null) continue;
                    use.Parts.Insert(0, p);
                    if (into.TryGetValue(r.Key, out var rules))   // (triggers on this block's use: a pedestal's place, a brazier lit)
                        foreach (TriggerSolve.Rule q in rules) { Part qp = ToPart(use, q, 0); if (qp != null) { qp.Guard.InsertRange(0, p.Guard); qp.After = p; use.Parts.Add(qp); } }
                    made[r.Key] = use;
                    (r.Cost ? _uses : _won).Add(use);
                }
                // (a part after the one it depends on: an encounter's win parts are made before the part that starts it)
                foreach (Use u in _uses.Concat(_won))
                {
                    int Depth(Part x) { int d = 0; for (Part y = x.After; y != null && d < 16; y = y.After) d++; return d; }
                    List<Part> ordered = u.Parts.Select((x, i) => new { x, i }).OrderBy(x => Depth(x.x)).ThenBy(x => x.i).Select(x => x.x).ToList();
                    u.Parts.Clear();
                    u.Parts.AddRange(ordered);
                }
                // Teleporters: one way, while their guard holds.
                foreach (TriggerSolve.Teleport t in tm.Teleports)
                {
                    if (!byKey.TryGetValue(t.From, out Mech fromM) || !byKey.TryGetValue(t.To, out Mech toM)) continue;
                    int[] fromNodes = RoomNodes(fromM.Room, string.IsNullOrEmpty(fromM.Def.ReachedFrom) ? null : new[] { fromM.Def.ReachedFrom });
                    int[] toNodes = RoomNodes(toM.Room, string.IsNullOrEmpty(toM.Def.ReachedFrom) ? null : new[] { toM.Def.ReachedFrom });
                    if (fromNodes.Length == 0 || toNodes.Length == 0) continue;
                    var guard = new List<int[]>();
                    bool ok = true;
                    foreach (TriggerSolve.Guard g in t.Guard)
                        if (g.Key != null && byKey.TryGetValue(g.Key, out Mech gm)) guard.Add(new[] { _mechs.IndexOf(gm), g.State, g.Not ? 1 : 0 }); else ok = false;
                    if (ok) _teleports.Add(new KeyValuePair<int[], int[][]>(fromNodes.Concat(new[] { toNodes[0] }).ToArray(), guard.ToArray()));
                }
                _mult = new long[_slots.Count];
                long mult = 1;
                for (int s = 0; s < _slots.Count; s++)
                {
                    _mult[s] = mult;
                    mult *= States(_slots[s].Def);
                    if (mult > MaxCodes) throw new InvalidOperationException($"{_slots.Count} changing mechanisms have too many states together to check");
                }

                // The places: what joins whatever happens (rooms' inside joins, joins of mechanisms that never change or in every state,
                // links no changing gate stands in), and the joins that change, between places.
                var uf = Enumerable.Range(0, _n).ToArray();
                int Find(int x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }
                void Union(int? a, int? b) { if (a != null && b != null) { int ra = Find(a.Value), rb = Find(b.Value); if (ra != rb) uf[Math.Max(ra, rb)] = Math.Min(ra, rb); } }
                foreach (PlacedRoom r in layout.Rooms)
                {
                    if (r.Def.Inside.Count > 0)
                        foreach (Join j in r.Def.Inside) Union(Node(r.Index, j.A), Node(r.Index, j.B));
                    else if (!r.Def.Mechs.Any(m => m.Joins.Count > 0))
                        for (int d = 1; d < r.Def.Doorways.Count; d++) Union(_base[r.Index], _base[r.Index] + d);
                }
                var joins = new List<int[]>();     // node, node, mechanism, state
                for (int i = 0; i < _mechs.Count; i++)
                    foreach (Join j in _mechs[i].Def.Joins)
                    {
                        if (!(Node(_mechs[i].Room, j.A) is int a) || !(Node(_mechs[i].Room, j.B) is int b)) continue;
                        MechDef md = _mechs[i].Def;
                        if (j.State < 0 || Free(_mechs[i]) || (_freeStates.TryGetValue(i, out var fs) && fs.Contains(j.State))
                            || (_mechs[i].Slot < 0 && (j.State == md.Start || Cycling(md, md.Start)))) Union(a, b);
                        else if (_mechs[i].Slot >= 0)
                        {
                            joins.Add(new[] { a, b, i, j.State });
                            if (Gated(md)) foreach (int c in md.CycleIn.Where(c => c != j.State && md.Cycle > 0f)) joins.Add(new[] { a, b, i, c });
                        }
                    }
                var gated = new List<KeyValuePair<int[], int[]>>();
                foreach (Link k in layout.Links)
                {
                    int a = _base[k.Room] + k.Doorway, b = _base[k.OtherRoom] + k.OtherDoorway;
                    var gates = (_gatesAt[a] ?? new List<int>()).Concat(_gatesAt[b] ?? new List<int>()).ToList();
                    if (gates.Any(g => _mechs[g].Slot < 0 && !Free(_mechs[g]) && _mechs[g].Def.Start == 0 && !Cycling(_mechs[g].Def, 0))) continue;   // (shut for good)
                    gates.RemoveAll(g => _mechs[g].Slot < 0);                                       // (open for good, or opens by itself)
                    if (standing && (Wall(k.Room, k.Doorway) || Wall(k.OtherRoom, k.OtherDoorway))) continue;   // (a secret's wall, left standing)
                    if (gates.Count == 0) Union(a, b); else gated.Add(new KeyValuePair<int[], int[]>(new[] { a, b }, gates.ToArray()));
                }
                // Shortcuts: each pair a join between its two ends, open once a flag (a slot of its own) says the far end was reached.
                var teleports = new List<int[]>();   // near node, far node, flag mechanism
                if (!noShortcuts)
                    foreach (var pair in _mechs.Where(m => m.Def.Kind == "shortcut")
                        .Select(m => new { M = m, End = PackReader.ShortcutEnd(layout.Pack, layout.Rooms[m.Room].Def, m.Def, out string id), Id = id })
                        .Where(x => x.End != null).GroupBy(x => x.Id))
                    {
                        var nearEnd = pair.FirstOrDefault(x => x.End == "near");
                        var farEnd = pair.FirstOrDefault(x => x.End == "far");
                        if (nearEnd == null || farEnd == null || layout.Rooms[nearEnd.M.Room].Def.Doorways.Count == 0 || layout.Rooms[farEnd.M.Room].Def.Doorways.Count == 0) continue;
                        var flag = new Mech { Room = farEnd.M.Room, Def = new MechDef { Id = "reached", Kind = "flag", Poses = { new Pose(), new Pose() } }, Key = farEnd.M.Key + "/reached" };
                        _mechs.Add(flag);
                        int slot = SlotOf(flag);
                        teleports.Add(new[] { _base[nearEnd.M.Room], _base[farEnd.M.Room], _mechs.Count - 1 });
                        var reached = new Use { Key = flag.Key, Nodes = new[] { _base[farEnd.M.Room] } };
                        reached.Acts.Add(new KeyValuePair<int, string>(slot, "set:1"));
                        _won.Add(reached);
                    }
                if (teleports.Count > 0)
                {
                    _mult = new long[_slots.Count];
                    long m2 = 1;
                    for (int s = 0; s < _slots.Count; s++) { _mult[s] = m2; m2 *= States(_slots[s].Def); }
                    if (m2 > MaxCodes) throw new InvalidOperationException("too many states together to check");
                }
                foreach (int[] t in teleports) joins.Add(new[] { t[0], t[1], t[2], 1 });

                _place = new int[_n];
                var placeOf = new Dictionary<int, int>();
                for (int i = 0; i < _n; i++)
                {
                    int root = Find(i);
                    if (!placeOf.TryGetValue(root, out int pl)) placeOf[root] = pl = placeOf.Count;
                    _place[i] = pl;
                }
                _m = placeOf.Count;
                foreach (int[] j in joins) if (_place[j[0]] != _place[j[1]]) _joinEdges.Add(new[] { _place[j[0]], _place[j[1]], j[2], j[3] });
                foreach (var g in gated)
                    if (_place[g.Key[0]] != _place[g.Key[1]]) _gateEdges.Add(new KeyValuePair<int[], int[]>(new[] { _place[g.Key[0]], _place[g.Key[1]] }, g.Value));
                for (int i = 0; i < _drops.Count; i++) _drops[i] = new KeyValuePair<int, int>(_place[_drops[i].Key], _place[_drops[i].Value]);
                _drops.RemoveAll(d => d.Key == d.Value);
                foreach (Use u in _uses.Concat(_won)) u.Nodes = u.Nodes.Select(x => _place[x]).Distinct().ToArray();
                for (int i = 0; i < _teleports.Count; i++)
                    _teleports[i] = new KeyValuePair<int[], int[][]>(_teleports[i].Key.Select(x => _place[x]).ToArray(), _teleports[i].Value);
                _firstUses = new int[_m];
            }

            /// <summary>Whether a guard holds in a code (a mechanism the player sets at will, or that cycles, holds any of its states).</summary>
            private bool Holds(IEnumerable<int[]> guard, long code)
            {
                foreach (int[] g in guard)
                {
                    Mech m = _mechs[g[0]];
                    IEnumerable<int> can = _freeStates.TryGetValue(g[0], out var fs) ? fs
                        : Free(m) ? Enumerable.Range(0, States(m.Def)) : new[] { StateOf(code, m) };
                    if (!can.Any(st => g[2] == 0 ? st == g[1] : st != g[1])) return false;
                }
                return true;
            }

            private int SlotState(long code, int slot) => (int)(code / _mult[slot] % States(_slots[slot].Def));

            /// <summary>A use applied to a code: its own acts, then each trigger part whose guard holds (on the code as it was), whose flag
            /// isn't set and whose key items are there.</summary>
            private long Apply(Use use, long code)
            {
                long next = code;
                foreach (KeyValuePair<int, string> act in use.Acts) next = With(next, act.Key, After(_slots[act.Key].Def, SlotState(next, act.Key), act.Value));
                HashSet<Part> applied = null;
                foreach (Part p in use.Parts)
                {
                    if (p.After != null && (applied == null || !applied.Contains(p.After))) continue;
                    if (!Holds(p.Guard, code) || (p.Flag >= 0 && SlotState(next, p.Flag) == 1)) continue;
                    if (p.Needs.Any(n => SlotState(next, n.Key) < n.Value)) continue;
                    (applied ?? (applied = new HashSet<Part>())).Add(p);
                    foreach (KeyValuePair<int, int> n in p.Needs) next = With(next, n.Key, SlotState(next, n.Key) - n.Value);
                    foreach (KeyValuePair<int, int> g in p.Gives) next = With(next, g.Key, Math.Min(States(_slots[g.Key].Def) - 1, SlotState(next, g.Key) + g.Value));
                    foreach (KeyValuePair<int, string> act in p.Acts) next = With(next, act.Key, After(_slots[act.Key].Def, SlotState(next, act.Key), act.Value));
                    if (p.Flag >= 0) next = With(next, p.Flag, 1);
                }
                return next;
            }

            /// <summary>Whether a breakable wall closes this doorway.</summary>
            private bool Wall(int room, int door) =>
                _l.Rooms[room].Def.Mechs.Any(m => m.Kind == "breakable" && m.Doorway == _l.Rooms[room].Def.Doorways[door].Id);

            /// <summary>After Run: the fewest uses to stand at a node (a doorway), or -1 if never.</summary>
            public int NodeUses(int node) => _firstUses[_place[node]];

            public int Base(int room) => _base[room];

            /// <summary>Whether a mechanism's state can open or close a way: it joins doorways in some state, or it's a gate or door in one.</summary>
            private static bool Matters(MechDef m) => m.Joins.Count > 0 || (IsGate(m) && !string.IsNullOrEmpty(m.Doorway));

            /// <summary>Open to the player whatever its state: it turns by itself (wait for it), or it's a gate of a fight room (it opens when
            /// the fight is won).</summary>
            private bool Free(Mech m) => (m.Def.Cycle > 0f && !Gated(m.Def)) || (IsGate(m.Def) && _l.Rooms[m.Room].Def.Tags.Any(t => string.Equals(t, "fight", StringComparison.OrdinalIgnoreCase)));

            /// <summary>Cycles only in some states ("cycleIn").</summary>
            private static bool Gated(MechDef m) => m.CycleIn != null && m.CycleIn.Count > 0;

            /// <summary>Whether a mechanism in this state is cycling (so all its joins are open to the player).</summary>
            private static bool Cycling(MechDef m, int state) => m.Cycle > 0f && (!Gated(m) || m.CycleIn.Contains(state));

            private int SlotOf(Mech m)
            {
                if (m.Slot < 0) { m.Slot = _slots.Count; _slots.Add(m); }
                return m.Slot;
            }

            private int? Node(int room, string doorway)
            {
                if (string.IsNullOrEmpty(doorway)) return null;
                int d = _l.Rooms[room].Def.Doorways.FindIndex(x => x.Id == doorway);
                return d < 0 ? (int?)null : _base[room] + d;
            }

            private int StateOf(long code, Mech m) => m.Slot < 0 ? m.Def.Start : (int)(code / _mult[m.Slot] % States(m.Def));

            private long With(long code, int slot, int state)
            {
                int now = (int)(code / _mult[slot] % States(_slots[slot].Def));
                return code + (state - now) * _mult[slot];
            }

            /// <summary>Per place, the lowest place of the part of the dungeon it's in, with the mechanisms in this code's states.</summary>
            private int[] Parts(long code)
            {
                if (_parts.TryGetValue(code, out int[] parts)) return parts;
                parts = new int[_m];
                for (int i = 0; i < _m; i++) parts[i] = i;
                int Find(int x) { while (parts[x] != x) { parts[x] = parts[parts[x]]; x = parts[x]; } return x; }
                void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra < rb) parts[rb] = ra; else if (rb < ra) parts[ra] = rb; }
                foreach (int[] j in _joinEdges)
                    if (StateOf(code, _mechs[j[2]]) == j[3]) Union(j[0], j[1]);
                foreach (var g in _gateEdges)
                    if (g.Value.All(x => { int st = StateOf(code, _mechs[x]); return st != 0 || Cycling(_mechs[x].Def, st); })) Union(g.Key[0], g.Key[1]);
                for (int i = 0; i < _m; i++) parts[i] = Find(i);
                _parts[code] = parts;
                return parts;
            }

            public string Run(List<string> solution)
            {
                if (_l.Rooms.Count == 0 || _n == 0) return "no rooms";
                List<PlanStep> plan = _l.Pack?.Plan ?? new List<PlanStep>();
                int end = plan.Count > 0 ? _l.Rooms.FindIndex(r => r.Step == StepName(plan[plan.Count - 1])) : -1;
                int way = EntranceDoorway(_l);
                int start = _place[_base[0] + Math.Max(0, way)];
                var endPlaces = end < 0 ? new int[0] : Enumerable.Range(_base[end], _l.Rooms[end].Def.Doorways.Count).Select(i => _place[i]).Distinct().ToArray();

                long startCode = 0;
                foreach (Mech m in _slots) startCode += Math.Min(m.Def.Start, States(m.Def) - 1) * _mult[m.Slot];

                // 0-1 breadth first: a state is (code, part); drops go to the front, uses to the back.
                var dist = new Dictionary<long, int>();
                var from = new Dictionary<long, KeyValuePair<long, string>>();   // state -> the state before it and the use (null: a wait)
                var queue = new LinkedList<long>();
                long Id(long code, int part) => code * _m + part;
                long first = Id(startCode, Parts(startCode)[start]);
                dist[first] = 0;
                queue.AddLast(first);
                for (int i = 0; i < _m; i++) _firstUses[i] = -1;
                long found = -1;
                int popped = 0;
                var done = new HashSet<long>();

                void Relax(long state, long next, int d, string use)
                {
                    if (dist.TryGetValue(next, out int known) && known <= d) return;
                    dist[next] = d;
                    from[next] = new KeyValuePair<long, string>(state, use);
                    if (use == null) queue.AddFirst(next); else queue.AddLast(next);
                }

                while (queue.Count > 0)
                {
                    long state = queue.First.Value;
                    queue.RemoveFirst();
                    if (!done.Add(state)) continue;   // (pushed again with fewer uses: already seen)
                    if (++popped > SolverLimit) return $"the puzzle has too many states to check (over {SolverLimit})";
                    long code = state / _m;
                    int part = (int)(state % _m);
                    int d = dist[state];
                    int[] parts = Parts(code);
                    for (int i = 0; i < _m; i++)
                        if (parts[i] == part && _firstUses[i] < 0) _firstUses[i] = d;   // (popped in order of uses: the first is the fewest)
                    if (found < 0 && (end < 0 || endPlaces.Any(i => parts[i] == part)))
                        found = state;

                    foreach (Use use in _uses)
                        foreach (int at in use.Anywhere ? new[] { part } : use.Nodes)
                        {
                            if (parts[at] != part) continue;
                            long next = Apply(use, code);
                            if (next != code) Relax(state, Id(next, Parts(next)[at]), d + 1, use.Key);
                        }
                    foreach (Use won in _won)   // (an encounter won where it's reached, a free trigger: free)
                        foreach (int at in won.Anywhere ? new[] { part } : won.Nodes)
                        {
                            if (parts[at] != part) continue;
                            long next = Apply(won, code);
                            if (next != code) Relax(state, Id(next, Parts(next)[at]), d, null);
                        }
                    foreach (KeyValuePair<int[], int[][]> t in _teleports)   // (a teleporter: free, one way, while its guard holds)
                    {
                        int to = t.Key[t.Key.Length - 1];
                        if (parts[to] == part || !Holds(t.Value, code)) continue;
                        for (int i = 0; i < t.Key.Length - 1; i++)
                            if (parts[t.Key[i]] == part) { Relax(state, Id(code, parts[to]), d, null); break; }
                    }
                    foreach (KeyValuePair<int, int> drop in _drops)   // (a jump down: free, one way)
                        if (parts[drop.Key] == part && parts[drop.Value] != part) Relax(state, Id(code, parts[drop.Value]), d, null);
                }

                // Every plan step's room reached at some point, and the end.
                var unreached = _l.Rooms.Where(r => r.Step != null && !Enumerable.Range(_base[r.Index], r.Def.Doorways.Count).Any(i => NodeUses(i) >= 0))
                    .Select(r => r.Step).ToList();
                if (found < 0)
                    return "the end can't be reached, whatever is used in whatever order" +
                           (unreached.Count > 0 ? $" (steps never reached: {string.Join(", ", unreached)})" : "");
                if (unreached.Count > 0) return $"step {unreached[0]} can't be reached, whatever is used in whatever order";
                for (long s = found; s != first; s = from[s].Key)
                    if (from[s].Value != null) solution.Add(from[s].Value);
                solution.Reverse();
                return null;
            }
        }
    }
}
