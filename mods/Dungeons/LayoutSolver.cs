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
    ///     its wires ("next", "set:n"; "hold" plates aren't counted: they let go when stepped off) and costs one use; the player is then
    ///     where the lever is, and may find the way back gone;
    ///   - a mechanism with a cycle turns by itself: the player may wait anywhere they can stand for its next state (free). Several
    ///     cycling mechanisms are taken as if each could be waited for on its own (their real timing isn't modelled).
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
            if (IsGate(m)) n = Math.Max(n, 2);
            return n;
        }

        /// <summary>A gate or door: closes its Doorway in state 0.</summary>
        public static bool IsGate(MechDef m) => m != null && (m.Kind == "gate" || m.Kind == "door");

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
        internal static string Solve(Layout layout, List<string> solution)
        {
            solution.Clear();
            try { return new Puzzle(layout).Run(solution); }
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
                        int uses = puzzle.FirstUses[puzzle.Base(r.Index) + d];
                        if (uses >= 0) map[Key(r.Index, d)] = uses;
                    }
            }
            catch (Exception) { }
            return map;
        }

        private sealed class Puzzle
        {
            private sealed class Mech { public int Room; public MechDef Def; public string Key; public int Slot = -1; }
            private sealed class Use { public string Key; public int[] Nodes; public readonly List<KeyValuePair<int, string>> Acts = new List<KeyValuePair<int, string>>(); }

            private readonly Layout _l;
            private readonly int[] _base;                 // per room: its first doorway's node
            private readonly int[] _roomOf;               // per node: its room
            private readonly int _n;                      // nodes
            private readonly List<Mech> _mechs = new List<Mech>();
            private readonly List<Mech> _slots = new List<Mech>();        // the mechanisms whose state can change, in code order
            private readonly long[] _mult;
            private readonly List<Use> _uses = new List<Use>();
            private readonly List<int> _cycling = new List<int>();        // slots that turn by themselves
            private readonly List<KeyValuePair<int, int>> _drops = new List<KeyValuePair<int, int>>();   // one-way: from node, to node
            private readonly List<int>[] _gatesAt;        // per node: the mechanisms (index in _mechs) closing it in state 0
            private readonly Dictionary<long, int[]> _parts = new Dictionary<long, int[]>();   // code -> per node: its part's lowest node
            public readonly int[] FirstUses;              // per node, after Run: the fewest uses to stand there, or -1 never

            public Puzzle(Layout layout)
            {
                _l = layout;
                _base = new int[layout.Rooms.Count];
                for (int r = 0; r < layout.Rooms.Count; r++) { _base[r] = _n; _n += layout.Rooms[r].Def.Doorways.Count; }
                _roomOf = new int[_n];
                FirstUses = new int[_n];
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

                // The uses, and which mechanisms they (or time) change: only those get a slot in the code, and only if their state can
                // change the way (joins, or a gate's doorway): a spinning gear that's only for the look changes nothing.
                foreach (Mech m in _mechs)
                {
                    if (m.Def.Cycle > 0f && Matters(m.Def)) SlotOf(m);
                    if (!IsUsable(m.Def)) continue;
                    var use = new Use { Key = m.Key };
                    foreach (Wire w in m.Def.Wires)
                    {
                        if (w.Do == "hold") continue;
                        string key = Target(layout, m.Room, w);
                        if (key == null || !byKey.TryGetValue(key, out Mech target) || !Matters(target.Def)) continue;
                        use.Acts.Add(new KeyValuePair<int, string>(SlotOf(target), w.Do));
                    }
                    if (use.Acts.Count == 0) continue;
                    int[] all = Enumerable.Range(_base[m.Room], layout.Rooms[m.Room].Def.Doorways.Count).ToArray();
                    use.Nodes = string.IsNullOrEmpty(m.Def.ReachedFrom) ? all : Node(m.Room, m.Def.ReachedFrom) is int from ? new[] { from } : new int[0];
                    _uses.Add(use);
                }
                foreach (Mech m in _slots) if (m.Def.Cycle > 0f) _cycling.Add(m.Slot);
                _mult = new long[_slots.Count];
                long mult = 1;
                for (int s = 0; s < _slots.Count; s++)
                {
                    _mult[s] = mult;
                    mult *= States(_slots[s].Def);
                    if (mult > MaxCodes) throw new InvalidOperationException($"{_slots.Count} changing mechanisms have too many states together to check");
                }
            }

            public int Base(int room) => _base[room];

            /// <summary>Whether a mechanism's state can open or close a way: it joins doorways in some state, or it's a gate or door in one.</summary>
            private static bool Matters(MechDef m) => m.Joins.Count > 0 || (IsGate(m) && !string.IsNullOrEmpty(m.Doorway));

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

            /// <summary>Per node, the lowest node of the part of the graph it's in, with the mechanisms in this code's states.</summary>
            private int[] Parts(long code)
            {
                if (_parts.TryGetValue(code, out int[] parts)) return parts;
                parts = new int[_n];
                for (int i = 0; i < _n; i++) parts[i] = i;
                int Find(int x) { while (parts[x] != x) { parts[x] = parts[parts[x]]; x = parts[x]; } return x; }
                void Union(int? a, int? b)
                {
                    if (a == null || b == null) return;
                    int ra = Find(a.Value), rb = Find(b.Value);
                    if (ra < rb) parts[rb] = ra; else if (rb < ra) parts[ra] = rb;
                }
                foreach (PlacedRoom r in _l.Rooms)
                {
                    RoomDef def = r.Def;
                    if (def.Inside.Count > 0)
                        foreach (Join j in def.Inside) Union(Node(r.Index, j.A), Node(r.Index, j.B));
                    else if (!def.Mechs.Any(m => m.Joins.Count > 0))
                        for (int d = 1; d < def.Doorways.Count; d++) Union(_base[r.Index], _base[r.Index] + d);
                }
                foreach (Mech m in _mechs)
                {
                    if (m.Def.Joins.Count == 0) continue;
                    int state = StateOf(code, m);
                    foreach (Join j in m.Def.Joins)
                        if (j.State < 0 || j.State == state) Union(Node(m.Room, j.A), Node(m.Room, j.B));
                }
                foreach (Link k in _l.Links)
                {
                    int a = _base[k.Room] + k.Doorway, b = _base[k.OtherRoom] + k.OtherDoorway;
                    if (Closed(code, a) || Closed(code, b)) continue;
                    Union(a, b);
                }
                for (int i = 0; i < _n; i++) parts[i] = Find(i);
                _parts[code] = parts;
                return parts;
            }

            private bool Closed(long code, int node) => _gatesAt[node] != null && _gatesAt[node].Any(g => StateOf(code, _mechs[g]) == 0);

            public string Run(List<string> solution)
            {
                if (_l.Rooms.Count == 0 || _n == 0) return "no rooms";
                List<PlanStep> plan = _l.Pack?.Plan ?? new List<PlanStep>();
                int end = plan.Count > 0 ? _l.Rooms.FindIndex(r => r.Step == StepName(plan[plan.Count - 1])) : -1;
                int way = EntranceDoorway(_l);
                int start = _base[0] + Math.Max(0, way);

                long startCode = 0;
                foreach (Mech m in _slots) startCode += Math.Min(m.Def.Start, States(m.Def) - 1) * _mult[m.Slot];

                // 0-1 breadth first: a state is (code, part); waits go to the front, uses to the back.
                var dist = new Dictionary<long, int>();
                var from = new Dictionary<long, KeyValuePair<long, string>>();   // state -> the state before it and the use (null: a wait)
                var queue = new LinkedList<long>();
                long Id(long code, int part) => code * _n + part;
                long first = Id(startCode, Parts(startCode)[start]);
                dist[first] = 0;
                queue.AddLast(first);
                for (int i = 0; i < _n; i++) FirstUses[i] = -1;
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
                    long code = state / _n;
                    int part = (int)(state % _n);
                    int d = dist[state];
                    int[] parts = Parts(code);
                    for (int i = 0; i < _n; i++)
                        if (parts[i] == part && FirstUses[i] < 0) FirstUses[i] = d;   // (popped in order of uses: the first is the fewest)
                    if (found < 0 && (end < 0 || Enumerable.Range(_base[end], _l.Rooms[end].Def.Doorways.Count).Any(i => parts[i] == part)))
                        found = state;

                    foreach (Use use in _uses)
                        foreach (int at in use.Nodes)
                        {
                            if (parts[at] != part) continue;
                            long next = code;
                            foreach (KeyValuePair<int, string> act in use.Acts)
                            {
                                MechDef target = _slots[act.Key].Def;
                                int now = (int)(next / _mult[act.Key] % States(target));
                                next = With(next, act.Key, After(target, now, act.Value));
                            }
                            Relax(state, Id(next, Parts(next)[at]), d + 1, use.Key);
                        }
                    foreach (KeyValuePair<int, int> drop in _drops)   // (a jump down: free, one way)
                        if (parts[drop.Key] == part && parts[drop.Value] != part) Relax(state, Id(code, parts[drop.Value]), d, null);
                    foreach (int slot in _cycling)
                    {
                        MechDef m = _slots[slot].Def;
                        long next = With(code, slot, (int)((code / _mult[slot] + 1) % States(m)));
                        int[] nextParts = Parts(next);
                        for (int i = 0; i < _n; i++)
                            if (parts[i] == part) Relax(state, Id(next, nextParts[i]), d, null);
                    }
                }

                // Every plan step's room reached at some point, and the end.
                var unreached = _l.Rooms.Where(r => r.Step != null && !Enumerable.Range(_base[r.Index], r.Def.Doorways.Count).Any(i => FirstUses[i] >= 0))
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
