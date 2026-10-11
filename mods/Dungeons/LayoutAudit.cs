using System;
using System.Collections.Generic;
using System.Linq;

namespace Dungeons
{
    /// <summary>
    /// A laid-out dungeon checked again with other code than the layout builder's and the solver's, one line per fault: in the offline
    /// harness (tools/dungeons) for every seed, and in the game by `dungeon test` (PackTest.cs). No game types, so both compile it.
    /// </summary>
    internal static class LayoutAudit
    {
        /// <summary>Where the replay says how it ended (the harness sets it when REPLAYDEBUG is set; null: nowhere).</summary>
        public static Action<string> ReplayDebug { get; set; }

        /// <summary>A good layout checked again with other code than the builder's: joined doorways at the same place (PlacedRoom's own
        /// turning, the engine's way), facing opposite ways, of the same type; no two boxes overlapping by more than the margin; each room's
        /// max and min depth kept; every doorway joined; the way in capped; nothing past the end; every lock closed by a gate its key's
        /// room is wired to; every wire acting on something; doors only where they may go; the solution replayed.</summary>
        public static IEnumerable<string> Check(Layout l)
        {
            foreach (Link k in l.Links)
            {
                PlacedRoom a = l.Rooms[k.Room], b = l.Rooms[k.OtherRoom];
                UnityEngine.Vector3 pa = a.DoorwayPos(k.Doorway), pb = b.DoorwayPos(k.OtherDoorway);
                if ((pa - pb).magnitude > 0.02f) yield return $"link {k.Room}.{k.Doorway}-{k.OtherRoom}.{k.OtherDoorway}: doorways {(pa - pb).magnitude:0.000} m apart";
                if ((a.DoorwayFacing(k.Doorway) + b.DoorwayFacing(k.OtherDoorway)).magnitude > 0.02f) yield return $"link {k.Room}.{k.Doorway}-{k.OtherRoom}.{k.OtherDoorway}: not face to face";
                if (a.Def.Doorways[k.Doorway].Type != b.Def.Doorways[k.OtherDoorway].Type) yield return $"link {k.Room}-{k.OtherRoom}: types differ";
            }
            var boxes = l.Rooms.Select(r =>
            {
                UnityEngine.Vector3 size = r.Def.Size;
                if (Math.Abs(Math.Round(r.Yaw / 90)) % 2 == 1) size = new UnityEngine.Vector3(size.z, size.y, size.x);
                return new UnityEngine.Bounds(r.Pos, size);
            }).ToList();
            for (int i = 0; i < l.Rooms.Count; i++)
                for (int j = i + 1; j < l.Rooms.Count; j++)
                {
                    if (l.Rooms[i].Def.Size.x == 0 || l.Rooms[i].Def.Size.z == 0 || l.Rooms[j].Def.Size.x == 0 || l.Rooms[j].Def.Size.z == 0) continue;
                    UnityEngine.Bounds a = boxes[i], b = boxes[j];
                    float ox = Math.Min(a.max.x, b.max.x) - Math.Max(a.min.x, b.min.x), oy = Math.Min(a.max.y, b.max.y) - Math.Max(a.min.y, b.min.y),
                          oz = Math.Min(a.max.z, b.max.z) - Math.Max(a.min.z, b.min.z);
                    if (ox > 0.06f && oy > 0.06f && oz > 0.06f) yield return $"rooms {i} and {j} overlap {ox:0.00}x{oy:0.00}x{oz:0.00}";
                }
            foreach (var g in l.Rooms.GroupBy(r => r.Def.Name))
                if (g.First().Def.Max > 0 && g.Count() > g.First().Def.Max) yield return $"{g.Key}: {g.Count()} (max {g.First().Def.Max})";
            foreach (PlacedRoom r in l.Rooms)
                if (!r.Def.EndCap && r.Depth < r.Def.MinDepth) yield return $"room {r.Index} {r.Def.Name} at depth {r.Depth} (min {r.Def.MinDepth})";
            foreach (var s in Layouts.Sealed(l)) yield return $"doorway {s.Key}.{s.Value} sealed";
            int way = Layouts.EntranceDoorway(l);
            if (way < 0 || !l.Links.Any(k => (k.Room == 0 && k.Doorway == way) || (k.OtherRoom == 0 && k.OtherDoorway == way))) yield return "the way in isn't capped";
            // The way in, and every doorway a gate closes, joined to an end cap: a plain one (or the pack's "entranceCap" at the way in).
            bool Plain(RoomDef d) => d.EndCap && d.Mechs.Count == 0 && !d.Parts.Any(p => p.Saved) && Layouts.CapDepth(d) < 1f;
            foreach (Link k in l.Links)
                foreach (var side in new[] { new { R = k.Room, D = k.Doorway, O = k.OtherRoom }, new { R = k.OtherRoom, D = k.OtherDoorway, O = k.Room } })
                {
                    RoomDef here = l.Rooms[side.R].Def, there = l.Rooms[side.O].Def;
                    bool wayIn = side.R == 0 && side.D == way;
                    bool gated = here.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == here.Doorways[side.D].Id);
                    if (wayIn && !there.EndCap) yield return $"the way in joins room {side.O} ({there.Name}), not an end cap";
                    else if ((wayIn || gated) && there.EndCap && !(wayIn && PackReader.EntranceCap(l.Pack) == there.Name) && !Plain(there))
                        yield return $"{(wayIn ? "the way in" : $"room {side.R}'s gated doorway {here.Doorways[side.D].Id}")} has {there.Name}, not a plain cap";
                }
            string end = l.Pack.Plan.Count > 0 ? Layouts.StepName(l.Pack.Plan[l.Pack.Plan.Count - 1]) : null;
            if (end != null && l.Links.Any(k => (l.Rooms[k.Room].Step == end && !l.Rooms[k.OtherRoom].Def.EndCap && l.Rooms[k.OtherRoom].Depth > l.Rooms[k.Room].Depth)))
                yield return "a room past the end";

            // Locks: a gate closes the doorway, and a lever or plate in the key step's room acts on it.
            foreach (Link k in l.Locks)
            {
                PlacedRoom r = l.Rooms[k.Room];
                string door = r.Def.Doorways[k.Doorway].Id;
                MechDef gate = r.Def.Mechs.FirstOrDefault(m => Layouts.IsGate(m) && m.Doorway == door);
                if (gate == null) { yield return $"the lock on {k.Room}.{k.Doorway} has no gate"; continue; }
                string gateKey = r.Index + "." + gate.Id;
                PlanStep step = l.Pack.Plan.First(p => Layouts.StepName(p) == r.Step);
                PlacedRoom key = l.Rooms.FirstOrDefault(x => x.Step == step.OpensWith);
                if (key == null || !key.Def.Mechs.Any(m => Layouts.IsUsable(m) && m.Wires.Any(w => Layouts.Target(l, key.Index, w) == gateKey)))
                    yield return $"the gate {gateKey} isn't wired from step {step.OpensWith}'s room";
                if (gate.Parts.Count == 0 && gate.Poses.Count < 2) yield return $"the gate {gateKey} has no parts or poses";
            }
            // Each room placed by a doorway its "joinBy" allows (its placing link is the first that names it second).
            for (int i = 1; i < l.Rooms.Count; i++)
            {
                int k = l.Links.FindIndex(x => x.OtherRoom == i);
                if (k >= 0 && !PackReader.JoinBy(l.Pack, l.Rooms[i].Def, l.Rooms[i].Def.Doorways[l.Links[k].OtherDoorway]))
                    yield return $"room {i} {l.Rooms[i].Def.Name} placed by doorway {l.Rooms[i].Def.Doorways[l.Links[k].OtherDoorway].Id} (not in its joinBy)";
            }
            foreach (PlacedRoom r in l.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                    foreach (Wire w in m.Wires)
                        if (Layouts.Target(l, r.Index, w) == null) yield return $"{r.Index}.{m.Id}'s wire to {w.Target} acts on nothing";
            // Secret rooms: joined by their wall's doorway, never a step, nothing beyond them but end caps.
            foreach (PlacedRoom r in l.Rooms.Where(x => PackReader.Secret(x.Def)))
            {
                int k = l.Links.FindIndex(x => x.OtherRoom == r.Index);
                string by = k >= 0 ? r.Def.Doorways[l.Links[k].OtherDoorway].Id : null;
                if (!r.Def.Mechs.Any(m => m.Kind == "breakable" && m.Doorway == by)) yield return $"secret room {r.Index} {r.Def.Name} joined by {by}, not by its wall";
                if (r.Step != null) yield return $"secret room {r.Index} {r.Def.Name} is plan step {r.Step}";
                foreach (Link x in l.Links.Where(x => x.Room == r.Index))
                    if (!l.Rooms[x.OtherRoom].Def.EndCap) yield return $"secret room {r.Index} {r.Def.Name} leads on to room {x.OtherRoom}";
            }
            // The shortcut: near end on the entrance room, far end on the far step's room or one room from it.
            PackReader.ShortcutSpec spec = PackReader.Shortcut(l.Pack);
            var ends = l.Rooms.Where(x => PackReader.HasShortcut(x.Def)).ToList();
            if (spec != null && ends.Count > 0)
            {
                PlacedRoom nearRoom = ends.FirstOrDefault(x => x.Def.Name == spec.Near), farRoom = ends.FirstOrDefault(x => x.Def.Name == spec.Far);
                int Parent(PlacedRoom x) { int k = l.Links.FindIndex(y => y.OtherRoom == x.Index); return k >= 0 ? l.Links[k].Room : -1; }
                int step = l.Rooms.FindIndex(x => x.Step == spec.FarStep);
                if (nearRoom == null || farRoom == null) yield return "only one end of the shortcut is placed";
                else
                {
                    if (Parent(nearRoom) != 0) yield return $"the shortcut's near end hangs off room {Parent(nearRoom)}, not the entrance room";
                    int fp = Parent(farRoom);
                    if (fp != step && !l.Links.Any(y => (y.Room == fp && y.OtherRoom == step) || (y.Room == step && y.OtherRoom == fp)))
                        yield return $"the shortcut's far end hangs off room {fp}, not by the far step's room {step}";
                }
            }
            foreach (Link k in l.Doors)
            {
                PlacedRoom a = l.Rooms[k.Room], b = l.Rooms[k.OtherRoom];
                Doorway da = a.Def.Doorways[k.Doorway], db = b.Def.Doorways[k.OtherDoorway];
                if (!da.AllowDoor || !db.AllowDoor || a.Def.EndCap || b.Def.EndCap || !l.Pack.Doors.ContainsKey(da.Type ?? "") || Math.Abs(a.DoorwayFacing(k.Doorway).y) > 0.5f)
                    yield return $"a door at {k.Room}.{k.Doorway} where none may go";
                if (a.Def.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == da.Id) || b.Def.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == db.Id))
                    yield return $"a door at {k.Room}.{k.Doorway} where a gate stands";
            }
            foreach (string p in Replay(l)) yield return p;
        }

        /// <summary>The solution played through, step by step, with a walk of its own: each use within reach of where the player is,
        /// and the end reached after the last. A mechanism that turns by itself counts as in every state at once (the player waits).</summary>
        private static IEnumerable<string> Replay(Layout l)
        {
            var state = new Dictionary<string, int>();
            foreach (PlacedRoom r in l.Rooms) foreach (MechDef m in r.Def.Mechs) state[r.Index + "." + m.Id] = m.Start;
            var at = new KeyValuePair<int, int>(0, Math.Max(0, Layouts.EntranceDoorway(l)));
            // The triggers and blocks, as the solver is given them (TriggerSolve), followed here with this walk's own code.
            TriggerSolve.Model tm;
            try { tm = TriggerSolve.For(l); } catch (Exception e) { tm = new TriggerSolve.Model(); tm.Problems.Add(e.Message); }
            var items = new Dictionary<string, int>();
            var done = new HashSet<TriggerSolve.Rule>();   // Once rules that have fired
            var into = tm.Rules.Where(r => r.Into != null).GroupBy(r => r.Into).ToDictionary(g => g.Key, g => g.ToList());
            MechDef Def(string key) => Layouts.Mech(l, key, out _);
            bool Cycling(string key) { MechDef m = Def(key); return m != null && m.Cycle > 0f && (m.CycleIn == null || m.CycleIn.Count == 0); }
            bool Holds(IEnumerable<TriggerSolve.Guard> guard, Dictionary<string, int> s0)
            {
                foreach (TriggerSolve.Guard g in guard)
                {
                    if (g.Key == null || !s0.ContainsKey(g.Key)) return false;
                    IEnumerable<int> can = tm.Free.TryGetValue(g.Key, out var fs) ? fs
                        : Cycling(g.Key) ? Enumerable.Range(0, Layouts.States(Def(g.Key))) : new[] { s0[g.Key] };
                    if (!can.Any(st => g.Not ? st != g.State : st == g.State)) return false;
                }
                return true;
            }
            var won = new HashSet<string>();
            void Act(string key, string act)
            {
                MechDef t = Def(key);
                if (t == null || !state.ContainsKey(key)) return;
                if (act == "start") { if (t.Kind == "encounter" && won.Add(key)) WinEncounter(key); return; }
                if (tm.Free.ContainsKey(key) || Cycling(key)) return;   // (the player sets it at will anyway)
                state[key] = Layouts.After(t, state[key], act);
            }
            bool Fire(TriggerSolve.Rule r, Dictionary<string, int> s0)
            {
                if (done.Contains(r) || !Holds(r.Guard, s0)) return false;
                if (r.Needs.Any(n => (items.TryGetValue(n.Key, out int c) ? c : 0) < n.Value)) return false;
                foreach (var n in r.Needs) items[n.Key] -= n.Value;
                foreach (var g in r.Gives) items[g.Key] = (items.TryGetValue(g.Key, out int c) ? c : 0) + g.Value;
                foreach (TriggerSolve.Act a in r.Acts) Act(a.Key, a.Do);
                if (r.Once) done.Add(r);
                return true;
            }
            void Into(string key, Dictionary<string, int> s0)
            {
                if (into.TryGetValue(key, out var rules)) foreach (TriggerSolve.Rule r in rules) Fire(r, s0);
            }
            void WinEncounter(string key)
            {
                MechDef e = Layouts.Mech(l, key, out int room);
                var s0 = new Dictionary<string, int>(state);
                var wires = (e.Encounter?.Boss?.AllWires() ?? Enumerable.Empty<Wire>()).Concat(e.Wires);
                foreach (Wire w in wires) if (w.Do != "hold") Act(Layouts.Target(l, room, w), w.Do);
                Into(key, s0);
            }
            void Use(int room, MechDef m)
            {
                var s0 = new Dictionary<string, int>(state);
                foreach (Wire w in m.Wires) if (w.Do != "hold") Act(Layouts.Target(l, room, w), w.Do);
                Into(room + "." + m.Id, s0);
            }
            bool At(HashSet<KeyValuePair<int, int>> reach, int room, IEnumerable<string> ids) =>
                Enumerable.Range(0, l.Rooms[room].Def.Doorways.Count).Any(d => (ids == null || ids.Contains(l.Rooms[room].Def.Doorways[d].Id)) && reach.Contains(new KeyValuePair<int, int>(room, d)));
            // What happens by itself where the player gets: encounters won, free triggers, until nothing more changes.
            void Win()
            {
                for (bool more = true; more;)
                {
                    more = false;
                    HashSet<KeyValuePair<int, int>> here = Walk(l, state, at, tm);
                    foreach (PlacedRoom r in l.Rooms)
                        foreach (MechDef e in r.Def.Mechs)
                        {
                            string on = PackReader.StartOn(l.Pack, r.Def, e);
                            if (on == null || on == "wire" || won.Contains(r.Index + "." + e.Id)) continue;
                            if (!At(here, r.Index, e.ReachedFrom == null ? null : new[] { e.ReachedFrom })) continue;
                            won.Add(r.Index + "." + e.Id);
                            WinEncounter(r.Index + "." + e.Id);
                            more = true;
                        }
                    foreach (TriggerSolve.Rule r in tm.Rules.Where(x => !x.Cost && x.Into == null))
                    {
                        if (r.Room < 0 || (!r.Anywhere && !At(here, r.Room, r.Nodes))) continue;
                        string before = string.Join(",", state.Values) + "|" + string.Join(",", items.Values) + "|" + done.Count;
                        var s0 = new Dictionary<string, int>(state);
                        if (!Fire(r, s0)) continue;
                        Into(r.Key, s0);
                        if (string.Join(",", state.Values) + "|" + string.Join(",", items.Values) + "|" + done.Count != before) more = true;
                    }
                }
            }
            foreach (string key in l.Solution)
            {
                Win();
                HashSet<KeyValuePair<int, int>> reach = Walk(l, state, at, tm);
                MechDef lever = Layouts.Mech(l, key, out int room);
                bool ownRule = tm.Rules.Any(r => r.Key == key && r.Cost && r.Into == null);   // (a lock's or pedestal's own use: its rule, not a pull)
                if (lever != null && !ownRule)
                {
                    List<Doorway> doorways = l.Rooms[room].Def.Doorways;
                    var from = Enumerable.Range(0, doorways.Count).Where(d => lever.ReachedFrom == null || doorways[d].Id == lever.ReachedFrom)
                        .Select(d => new KeyValuePair<int, int>(room, d)).Where(reach.Contains).ToList();
                    if (from.Count == 0) { yield return $"solution: {key} can't be reached when it's used"; yield break; }
                    at = from[0];
                    Use(room, lever);
                    continue;
                }
                // A trigger's or block's own use (a lock opened, a pedestal given its item, a sequence done).
                TriggerSolve.Rule rule = tm.Rules.FirstOrDefault(r => r.Key == key && r.Cost && r.Into == null
                    && (r.Anywhere || At(reach, r.Room, r.Nodes)) && Holds(r.Guard, state) && !done.Contains(r)
                    && r.Needs.All(n => (items.TryGetValue(n.Key, out int c) ? c : 0) >= n.Value));
                if (rule == null) { yield return $"solution: {key} isn't a mechanism or a use of a trigger that can be made then"; yield break; }
                if (!rule.Anywhere)
                    at = Enumerable.Range(0, l.Rooms[rule.Room].Def.Doorways.Count).Select(d => new KeyValuePair<int, int>(rule.Room, d))
                        .First(p => reach.Contains(p) && (rule.Nodes == null || rule.Nodes.Contains(l.Rooms[rule.Room].Def.Doorways[p.Value].Id)));
                var s1 = new Dictionary<string, int>(state);
                Fire(rule, s1);
                Into(rule.Key, s1);
            }
            Win();
            int endRoom = l.Rooms.FindIndex(r => r.Step == Layouts.StepName(l.Pack.Plan.Last()));
            if (ReplayDebug != null)   // (what the replay ended with: the harness's REPLAYDEBUG)
                ReplayDebug($"REPLAY seed {l.Seed}: at {at}, states {string.Join(" ", state.Select(x => x.Key + "=" + x.Value))}, items {string.Join(" ", items.Select(x => x.Key + "=" + x.Value))}, " +
                                  $"rooms reached {string.Join(" ", Walk(l, state, at, tm).Select(x => x.Key).Distinct().OrderBy(x => x))}, end {endRoom}");
            if (endRoom >= 0 && !Walk(l, state, at, tm).Any(p => p.Key == endRoom)) yield return "solution: the end isn't reached after it";
        }

        /// <summary>Whether a layout has triggers or blocks the solver models by rules (TriggerSolve).</summary>
        public static bool Triggered(Layout l)
        {
            try { TriggerSolve.Model m = TriggerSolve.For(l); return m.Rules.Count > 0 || m.Teleports.Count > 0 || m.Free.Count > 0; }
            catch (Exception) { return true; }
        }

        /// <summary>Every (room, doorway) the player can walk to from one, with the mechanisms in these states.</summary>
        private static HashSet<KeyValuePair<int, int>> Walk(Layout l, Dictionary<string, int> state, KeyValuePair<int, int> start, TriggerSolve.Model tm = null)
        {
            // (secret walls stand; a shortcut's ends join once its far end is in what's reached: walked again until nothing more opens)
            var open = new HashSet<string>();
            HashSet<KeyValuePair<int, int>> seen;
            while (true)
            {
                seen = WalkOnce(l, state, start, open, tm);
                int before = open.Count;
                foreach (PlacedRoom r in l.Rooms)
                    foreach (MechDef m in r.Def.Mechs)
                        if (PackReader.ShortcutEnd(l.Pack, r.Def, m, out string pair) == "far" && seen.Any(p => p.Key == r.Index)) open.Add(pair);
                if (open.Count == before) return seen;
            }
        }

        private static HashSet<KeyValuePair<int, int>> WalkOnce(Layout l, Dictionary<string, int> state, KeyValuePair<int, int> start, HashSet<string> openPairs,
            TriggerSolve.Model tm = null)
        {
            // (a mechanism the player sets at will (a pushblock's cells) is in all those states at once; a teleporter sends while its
            // guard holds)
            bool FreeIn(string key, int st) => tm != null && tm.Free.TryGetValue(key, out var fs) && fs.Contains(st);
            // (a mechanism that turns by itself can be waited for in any state: its joins are all open, a gate of it passable)
            // (and a fight room's gates open once the fight is won)
            bool Fight(int room) => l.Rooms[room].Def.Tags.Any(t => string.Equals(t, "fight", StringComparison.OrdinalIgnoreCase));
            bool Closed(int room, int d) => l.Rooms[room].Def.Mechs.Any(m => m.Kind == "breakable" && m.Doorway == l.Rooms[room].Def.Doorways[d].Id)
                || (!Fight(room) && l.Rooms[room].Def.Mechs.Any(m => Layouts.IsGate(m) && !Cycles(m, state[room + "." + m.Id]) && m.Doorway == l.Rooms[room].Def.Doorways[d].Id
                    && state[room + "." + m.Id] == 0 && !(tm != null && tm.Free.TryGetValue(room + "." + m.Id, out var fs) && fs.Any(x => x != 0))));
            var ends = new List<KeyValuePair<string, KeyValuePair<int, int>>>();   // (pair, end's room doorway 0) for open pairs
            foreach (PlacedRoom r in l.Rooms)
                foreach (MechDef m in r.Def.Mechs)
                    if (PackReader.ShortcutEnd(l.Pack, r.Def, m, out string pair) != null && openPairs.Contains(pair))
                        ends.Add(new KeyValuePair<string, KeyValuePair<int, int>>(pair, new KeyValuePair<int, int>(r.Index, 0)));
            var seen = new HashSet<KeyValuePair<int, int>> { start };
            var todo = new Stack<KeyValuePair<int, int>>();
            todo.Push(start);
            while (todo.Count > 0)
            {
                var p = todo.Pop();
                var next = new List<KeyValuePair<int, int>>();
                RoomDef def = l.Rooms[p.Key].Def;
                string id = def.Doorways[p.Value].Id;
                int Index(string doorway) => def.Doorways.FindIndex(d => d.Id == doorway);
                // Through the room.
                var joins = new List<Join>(def.Inside);
                foreach (MechDef m in def.Mechs) joins.AddRange(m.Joins.Where(j => j.State < 0 || Cycles(m, state[p.Key + "." + m.Id]) || j.State == state[p.Key + "." + m.Id]
                    || FreeIn(p.Key + "." + m.Id, j.State)));
                if (def.Inside.Count == 0 && def.Mechs.All(m => m.Joins.Count == 0))
                    for (int d = 0; d < def.Doorways.Count; d++) next.Add(new KeyValuePair<int, int>(p.Key, d));
                foreach (Join j in joins)
                {
                    if (j.A == id && Index(j.B) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.B)));
                    if (j.B == id && Index(j.A) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.A)));
                }
                foreach (Join j in PackReader.Drops(l.Pack, def))   // one way
                    if (j.A == id && Index(j.B) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.B)));
                if (tm != null)
                    foreach (TriggerSolve.Teleport t in tm.Teleports)
                    {
                        MechDef fromM = Layouts.Mech(l, t.From, out int fr), toM = Layouts.Mech(l, t.To, out int tr);
                        if (fromM == null || toM == null || fr != p.Key || (fromM.ReachedFrom != null && fromM.ReachedFrom != id)) continue;
                        if (!t.Guard.All(g => g.Key != null && state.ContainsKey(g.Key) && (g.Not ? state[g.Key] != g.State : state[g.Key] == g.State))) continue;
                        int td = toM.ReachedFrom != null ? l.Rooms[tr].Def.Doorways.FindIndex(d => d.Id == toM.ReachedFrom) : 0;
                        if (td >= 0) next.Add(new KeyValuePair<int, int>(tr, td));
                    }
                foreach (var e in ends.Where(e => e.Value.Key == p.Key && e.Value.Value == p.Value))   // (through a shortcut)
                    foreach (var o in ends.Where(o => o.Key == e.Key && !o.Value.Equals(e.Value))) next.Add(o.Value);
                // Out of it.
                foreach (Link k in l.Links)
                {
                    KeyValuePair<int, int> other;
                    if (k.Room == p.Key && k.Doorway == p.Value) other = new KeyValuePair<int, int>(k.OtherRoom, k.OtherDoorway);
                    else if (k.OtherRoom == p.Key && k.OtherDoorway == p.Value) other = new KeyValuePair<int, int>(k.Room, k.Doorway);
                    else continue;
                    if (!Closed(p.Key, p.Value) && !Closed(other.Key, other.Value)) next.Add(other);
                }
                foreach (var n in next) if (seen.Add(n)) todo.Push(n);
            }
            return seen;
        }

        /// <summary>Turning by itself in this state (a "cycleIn" one only in those states): every state of it as good as reached.</summary>
        private static bool Cycles(MechDef m, int state) => m.Cycle > 0f && (m.CycleIn == null || m.CycleIn.Count == 0 || m.CycleIn.Contains(state));

    }
}
