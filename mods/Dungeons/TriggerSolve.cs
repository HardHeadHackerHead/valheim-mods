using System;
using System.Collections.Generic;
using System.Linq;

namespace Dungeons
{
    /// <summary>
    /// The solver's view of the triggers and blocks (no game types: the harness solves with it too). For(layout) turns a laid-out dungeon's
    /// triggers and blocks into the solver's own terms, on mechanism keys "&lt;room&gt;.&lt;id&gt;"; Unmodelled(pack) says what of a pack it
    /// can't turn (the pack reader reports it as a problem: a puzzle the solver can't check is never silently accepted). A trigger marked
    /// "solver": "ignore" is left out (the layout must be solvable without it).
    ///
    /// How a trigger becomes a rule (where it fires, what it costs):
    ///   - a lever pulled, a plate pressed or let go: acts INTO that lever's or plate's own use (the same use, its acts applied with its wires
    ///     when the guard holds);
    ///   - an item put on or taken off a pedestal: into the pedestal's own place/take use; a brazier lit: into its "light" use (when players
    ///     can light it), else chained; a breakable broken: a use at its room (the player breaks it);
    ///   - an encounter cleared: into the encounter (its acts apply when it's won, with its wires);
    ///   - entering or leaving an area, kills in an area (the fight is the player's, as encounters are): free, at its room (or "from");
    ///   - a mechanism reaching a state, a brazier lit or out by other means, a repeating timer: chained, free, anywhere, guarded by that
    ///     state (a "set" is the same however often it's applied; a "next" in a chained rule can't be modelled);
    ///   - a sequence: one use at its room, when every event in it is something the player does in that room;
    ///   - "all": one rule guarded by every condition; "any": one rule per condition; signals and named timers are inlined (a timer's
    ///     actions as if at once: waiting is free).
    /// Blocks: a lock is a use (needs one of its key, opens it, once); a lightable brazier a use (lights it), one that burns out goes out
    /// for free; a pedestal a place use (its key item needed if it takes one) and a take use (gives it back); a pushblock is free over the
    /// cells it can be pushed to (it needs a reset when it can get stuck); a teleporter pair a link while its sending end is on.
    /// Counters (conditions, events) can't be modelled.
    /// </summary>
    internal static class TriggerSolve
    {
        internal sealed class Guard { public string Key; public int State; public bool Not; }
        internal sealed class Act { public string Key; public string Do; }   // Do: "set:n", "next", "start"

        internal sealed class Rule
        {
            public string Key;                  // for the solution list ("3.t1", "3.ped1/place", "5.lock1")
            public int Room;
            public List<string> Nodes;          // doorway ids of Room where the player must stand (null: anywhere in the room)
            public bool Anywhere;               // chained: wherever the player is
            public bool Cost;                   // a use (counts in the solution, like a lever); false: free (like a won encounter)
            public bool Once;                   // applies at most once (a flag slot); false when every act is a "set" and nothing is given
            public string Into;                 // applies with this mechanism's own use (a lever's, a plate's, an encounter's win), not alone
            public readonly List<Guard> Guard = new List<Guard>();
            public readonly List<Act> Acts = new List<Act>();
            public readonly Dictionary<string, int> Needs = new Dictionary<string, int>();   // key item (prefab) -> how many it uses up
            public readonly Dictionary<string, int> Gives = new Dictionary<string, int>();
        }

        /// <summary>The player at From's place may go to To's place while Guard holds (always one way: a pair that sends both ways is two).</summary>
        internal sealed class Teleport { public string From, To; public bool OneWay = true; public readonly List<Guard> Guard = new List<Guard>(); }

        internal sealed class Model
        {
            public readonly List<Rule> Rules = new List<Rule>();
            public readonly Dictionary<string, List<int>> Free = new Dictionary<string, List<int>>();   // mech key -> states the player picks at will
            public readonly List<Teleport> Teleports = new List<Teleport>();
            public readonly Dictionary<string, int> KeyCounts = new Dictionary<string, int>();         // key item -> the most there can be
            public readonly List<string> Problems = new List<string>();                                // what couldn't be modelled
        }

        /// <summary>A laid-out dungeon's triggers and blocks as the solver's rules.</summary>
        public static Model For(Layout layout)
        {
            var model = new Model();
            if (layout?.Rooms == null) return model;
            foreach (PlacedRoom r in layout.Rooms)
            {
                if (r.Def == null) continue;
                string Resolve(string id) => id == null ? null : Layouts.Target(layout, r.Index, new Wire { Target = id });
                Translate(layout.Pack, r.Def, r.Index, r.Index.ToString(), Resolve, model);
            }
            // teleporter pairs: the ends anywhere in the dungeon (the first two of each pair)
            var ends = layout.Rooms.Where(r => r.Def != null).SelectMany(r => r.Def.Mechs.Where(m => m.Kind == "teleporter" && !string.IsNullOrEmpty(m.Pair))
                .Select(m => new { Key = r.Index + "." + m.Id, Def = m })).GroupBy(x => x.Def.Pair);
            foreach (var pair in ends)
            {
                var list = pair.Take(2).ToList();
                if (list.Count < 2) continue;
                // one directed link per end that sends (a two-way pair: two links), while that end is on
                for (int i = 0; i < 2; i++)
                {
                    if (list[i].Def.Block != null && !list[i].Def.Block.Send) continue;
                    var t = new Teleport { From = list[i].Key, To = list[1 - i].Key, OneWay = true };
                    t.Guard.Add(new Guard { Key = list[i].Key, State = 1 });
                    model.Teleports.Add(t);
                }
            }
            foreach (Rule rule in model.Rules)
                foreach (KeyValuePair<string, int> g in rule.Gives)
                    if (rule.Once && !rule.Key.EndsWith("/take")) model.KeyCounts[g.Key] = (model.KeyCounts.TryGetValue(g.Key, out int n) ? n : 0) + g.Value;
            return model;
        }

        /// <summary>What of a pack's triggers and blocks the solver can't model (one line each, for the pack's problems).</summary>
        public static List<string> Unmodelled(Pack pack)
        {
            var model = new Model();
            if (pack == null) return model.Problems;
            foreach (RoomDef def in pack.Rooms)
            {
                string Resolve(string id) => id == null ? null : id.StartsWith("step:") ? id : def.Mechs.Any(m => m.Id == id) ? def.Name + "." + id : null;
                Translate(pack, def, -1, def.Name, Resolve, model);
            }
            return model.Problems.Distinct().ToList();
        }

        // ---- one room

        private static void Translate(Pack pack, RoomDef def, int room, string roomKey, Func<string, string> resolve, Model model)
        {
            string Key(string id) => roomKey + "." + id;
            string Where(TriggerDef t) => $"{def.Name} trigger {t.Id}";
            MechDef Local(string id) => def.Mechs.FirstOrDefault(m => m.Id == id);

            // the blocks' own rules
            foreach (MechDef m in def.Mechs)
            {
                BlockDef b = m.Block;
                List<string> at = string.IsNullOrEmpty(m.ReachedFrom) ? null : new List<string> { m.ReachedFrom };
                switch (m.Kind)
                {
                    case "lock":
                        if (b?.Key == null) break;
                        var open = new Rule { Key = Key(m.Id), Room = room, Nodes = at, Cost = true, Once = true };
                        open.Needs[TriggerReader.ItemPrefab(pack, b.Key)] = 1;
                        open.Acts.Add(new Act { Key = Key(m.Id), Do = "set:1" });
                        model.Rules.Add(open);
                        break;
                    case "brazier":
                        if (b == null) break;
                        if (b.Lightable)
                        {
                            var light = new Rule { Key = Key(m.Id) + "/light", Room = room, Nodes = at, Cost = true };
                            light.Acts.Add(new Act { Key = Key(m.Id), Do = "set:1" });
                            model.Rules.Add(light);
                        }
                        if (b.Burn > 0f)
                        {
                            var burnOut = new Rule { Key = Key(m.Id) + "/out", Room = room, Anywhere = true };
                            burnOut.Guard.Add(new Guard { Key = Key(m.Id), State = 1 });
                            burnOut.Acts.Add(new Act { Key = Key(m.Id), Do = "set:0" });
                            model.Rules.Add(burnOut);
                        }
                        break;
                    case "pedestal":
                        if (b == null || string.IsNullOrEmpty(b.Item)) break;
                        string item = TriggerReader.ItemPrefab(pack, b.Item);
                        bool key = TriggerReader.KeyItems(pack).ContainsKey(b.Item);
                        var place = new Rule { Key = Key(m.Id) + "/place", Room = room, Nodes = at, Cost = true };
                        place.Guard.Add(new Guard { Key = Key(m.Id), State = 0 });
                        place.Acts.Add(new Act { Key = Key(m.Id), Do = "set:1" });
                        if (key) place.Needs[item] = 1;
                        model.Rules.Add(place);
                        var take = new Rule { Key = Key(m.Id) + "/take", Room = room, Nodes = at, Cost = true };
                        take.Guard.Add(new Guard { Key = Key(m.Id), State = 1 });
                        take.Acts.Add(new Act { Key = Key(m.Id), Do = "set:0" });
                        if (key) take.Gives[item] = 1;
                        model.Rules.Add(take);
                        break;
                    case "pushblock":
                        if (b == null || b.Cells.Count == 0) break;
                        List<int> cells = PushReach(b, m.Start, out bool stuck);
                        bool reset = !string.IsNullOrEmpty(b.Group) && def.Triggers.Any(t => t.Do.Any(a => a.Verb == TriggerActions.Reset && a.Target == b.Group));
                        if (stuck && !reset) model.Problems.Add($"{def.Name} pushblock {m.Id} can be pushed where it can't come back from: give it a \"group\" and a trigger that resets it (a lever: {{\"reset\": group}})");
                        model.Free[Key(m.Id)] = cells;
                        break;
                }
            }

            foreach (TriggerDef t in def.Triggers)
            {
                if (t.SolverIgnore) continue;
                // what it does, as the solver sees it (signals and named timers inlined)
                var acts = new List<Act>();
                var gives = new Dictionary<string, int>();
                bool ok = Effects(pack, def, t, resolve, acts, gives, model.Problems, Where(t), 0);
                if (!ok || (acts.Count == 0 && gives.Count == 0)) continue;   // (it changes nothing the solver counts: a message, a sound...)
                if (t.If.Any(c => c.Counter != null) || t.On.Any(e => e.Kind == TriggerEvents.Counter))
                {
                    model.Problems.Add($"{Where(t)}: counters can't be checked by the solver (use mechanisms' states in \"if\", or mark it \"solver\": \"ignore\")");
                    continue;
                }
                var guards = new List<List<Guard>>();   // one rule per guard set: "all" one, "any" one per condition
                List<Guard> Conds(IEnumerable<TriggerCondition> cs) => cs.Select(c => new Guard { Key = resolve(c.Mech), State = c.State >= 0 ? c.State : c.Not, Not = c.State < 0 }).ToList();
                if (t.When == "any" && t.If.Count > 1) foreach (TriggerCondition c in t.If) guards.Add(Conds(new[] { c }));
                else guards.Add(Conds(t.If));
                if (guards.SelectMany(g => g).Any(g => g.Key == null)) continue;   // (a condition on nothing: the reader says so)
                bool once = !t.Repeat && (gives.Count > 0 || acts.Any(a => !a.Do.StartsWith("set:")));
                bool chainedNext = acts.Any(a => a.Do == "next");

                if (t.When == "sequence")
                {
                    // every event something the player does in this room: one use, at the room
                    if (t.On.Any(e => !PlayerAct(e, Local)))
                    {
                        model.Problems.Add($"{Where(t)}: a sequence's events must all be things a player does in its room (levers, plates, pedestals, braziers they light), for the solver");
                        continue;
                    }
                    if (t.Within > 0f && t.Within < 3f * t.On.Count)
                        model.Problems.Add($"{Where(t)}: \"within\" {t.Within} s for {t.On.Count} events (at least 3 s each, so a player can do it)");
                    foreach (List<Guard> g in guards) model.Rules.Add(Make(Key(t.Id), room, null, false, true, once, null, g, acts, gives));
                    continue;
                }
                foreach (TriggerEvent e in t.On)
                {
                    string mech = e.Mech != null ? resolve(e.Mech) : null;
                    MechDef local = e.Mech != null ? Local(e.Mech) : null;
                    foreach (List<Guard> g0 in guards)
                    {
                        var g = new List<Guard>(g0);
                        switch (e.Kind)
                        {
                            case TriggerEvents.Lever: case TriggerEvents.Press: case TriggerEvents.Release:
                                if (mech == null) break;
                                model.Rules.Add(Make(Key(t.Id), room, null, false, false, once, mech, g, acts, gives));
                                break;
                            case TriggerEvents.Cleared:
                                if (mech == null) break;
                                model.Rules.Add(Make(Key(t.Id), room, null, false, false, once, mech, g, acts, gives));
                                break;
                            case TriggerEvents.Place: case TriggerEvents.Take:
                                if (mech == null) break;
                                if (e.Item != null && local?.Block != null && e.Item != local.Block.Item)
                                {
                                    model.Problems.Add($"{Where(t)}: \"{e.Kind}\" of {e.Item}, not the pedestal's own item (the solver only knows its item)");
                                    break;
                                }
                                model.Rules.Add(Make(Key(t.Id), room, null, false, false, once, mech + (e.Kind == TriggerEvents.Place ? "/place" : "/take"), g, acts, gives));
                                break;
                            case TriggerEvents.Lit:
                                if (mech == null) break;
                                if (local?.Block != null && local.Block.Lightable) { model.Rules.Add(Make(Key(t.Id), room, null, false, false, once, mech + "/light", g, acts, gives)); break; }
                                g.Add(new Guard { Key = mech, State = 1 });
                                if (chainedNext) { NextProblem(model, Where(t)); break; }
                                model.Rules.Add(Make(Key(t.Id), room, null, true, false, once, null, g, acts, gives));
                                break;
                            case TriggerEvents.Broken:
                                if (mech == null) break;
                                model.Rules.Add(Make(Key(t.Id), room, null, false, true, once, null, g, acts, gives));
                                break;
                            case TriggerEvents.Enter: case TriggerEvents.Leave: case TriggerEvents.Kills:
                                model.Rules.Add(Make(Key(t.Id), room, e.From != null ? new List<string> { e.From } : null, false, false, once, null, g, acts, gives));
                                break;
                            case TriggerEvents.State: case TriggerEvents.Out:
                                if (mech == null) break;
                                if (e.Kind == TriggerEvents.State && e.State < 0)
                                {
                                    model.Problems.Add($"{Where(t)}: a \"state\" event with no \"state\" (any change) can't be checked by the solver: name the state");
                                    break;
                                }
                                g.Add(new Guard { Key = mech, State = e.Kind == TriggerEvents.Out ? 0 : e.State });
                                if (chainedNext) { NextProblem(model, Where(t)); break; }
                                model.Rules.Add(Make(Key(t.Id), room, null, true, false, once, null, g, acts, gives));
                                break;
                            case TriggerEvents.Timer:
                                if (!string.IsNullOrEmpty(e.Name)) break;   // (a named timer: inlined into whatever starts it)
                                if (chainedNext) { NextProblem(model, Where(t)); break; }
                                model.Rules.Add(Make(Key(t.Id), room, null, true, false, once, null, g, acts, gives));
                                break;
                            case TriggerEvents.Signal:
                                if (e.Name != null && e.Name.StartsWith("@"))
                                    model.Problems.Add($"{Where(t)}: a dungeon-wide signal ({e.Name}) can't be followed by the solver: send it in the room, or mark it \"solver\": \"ignore\"");
                                break;   // (a room signal: inlined into whatever sends it)
                        }
                    }
                }
            }
        }

        private static void NextProblem(Model model, string where) =>
            model.Problems.Add($"{where}: a \"next\" in a trigger that follows a state can't be checked by the solver (it may apply any number of times): use \"set\", or mark it \"solver\": \"ignore\"");

        private static Rule Make(string key, int room, List<string> nodes, bool anywhere, bool cost, bool once, string into, List<Guard> guard, List<Act> acts, Dictionary<string, int> gives)
        {
            var r = new Rule { Key = key, Room = room, Nodes = nodes, Anywhere = anywhere, Cost = cost, Once = once, Into = into };
            r.Guard.AddRange(guard);
            r.Acts.AddRange(acts);
            foreach (KeyValuePair<string, int> g in gives) r.Gives[g.Key] = g.Value;
            return r;
        }

        /// <summary>Something the player does at a mechanism of its room.</summary>
        private static bool PlayerAct(TriggerEvent e, Func<string, MechDef> local)
        {
            MechDef m = e.Mech != null ? local(e.Mech) : null;
            if (m == null) return false;
            switch (e.Kind)
            {
                case TriggerEvents.Lever: case TriggerEvents.Press: case TriggerEvents.Release: case TriggerEvents.Place: case TriggerEvents.Take: return true;
                case TriggerEvents.Lit: return m.Block != null && m.Block.Lightable;
                default: return false;
            }
        }

        /// <summary>A trigger's actions the solver counts: set, next and start on mechanisms, a reset's mechanisms back to their start, key
        /// items given; signals sent and named timers started in the room are followed (their triggers' actions as if at once). False when
        /// something in it can't be modelled (said in problems).</summary>
        private static bool Effects(Pack pack, RoomDef def, TriggerDef t, Func<string, string> resolve, List<Act> acts, Dictionary<string, int> gives,
            List<string> problems, string where, int depth)
        {
            if (depth > 4) { problems.Add($"{where}: signals or timers lead round in a circle (or more than 4 deep)"); return false; }
            bool ok = true;
            foreach (TriggerAction a in t.Do)
            {
                switch (a.Verb)
                {
                    case TriggerActions.Set: case TriggerActions.Next: case TriggerActions.Start:
                        string key = resolve(a.Target);
                        if (key == null) break;
                        acts.Add(new Act { Key = key, Do = a.Verb == TriggerActions.Set ? "set:" + a.To : a.Verb });
                        break;
                    case TriggerActions.Reset:
                        foreach (MechDef m in def.Mechs.Where(m => TriggerReader.GroupOf(m) == a.Target))
                        {
                            string k = resolve(m.Id);
                            if (k != null) acts.Add(new Act { Key = k, Do = "set:" + m.Start });
                        }
                        break;
                    case TriggerActions.Item:
                        if (!TriggerReader.KeyItems(pack).ContainsKey(a.Target ?? "")) break;   // (a game item: nothing the solver counts)
                        string prefab = TriggerReader.ItemPrefab(pack, a.Target);
                        gives[prefab] = (gives.TryGetValue(prefab, out int n) ? n : 0) + Math.Max(1, a.Count);
                        break;
                    case TriggerActions.Signal: case TriggerActions.Timer:
                        if (a.Stop || string.IsNullOrEmpty(a.Target)) break;
                        if (a.Target.StartsWith("@")) break;   // (dungeon-wide: the listener says it can't be followed)
                        string kind = a.Verb == TriggerActions.Signal ? TriggerEvents.Signal : TriggerEvents.Timer;
                        foreach (TriggerDef next in def.Triggers.Where(x => !x.SolverIgnore && x.On.Any(e => e.Kind == kind && e.Name == a.Target)))
                        {
                            if (next.If.Count > 0 || next.When == "sequence")
                            {
                                problems.Add($"{where}: the {kind} {a.Target} it sends goes to trigger {next.Id}, which has conditions or is a sequence: the solver can't follow that (mark one \"solver\": \"ignore\")");
                                ok = false;
                                continue;
                            }
                            if (a.Verb == TriggerActions.Timer && a.Every > 0f && next.Do.Any(x => x.Verb == TriggerActions.Next))
                            {
                                problems.Add($"{where}: a repeating timer {a.Target} whose trigger does \"next\": the solver can't follow that (use a cycle on the mechanism)");
                                ok = false;
                                continue;
                            }
                            ok &= Effects(pack, def, next, resolve, acts, gives, problems, where, depth + 1);
                        }
                        break;
                }
            }
            return ok;
        }

        /// <summary>The cells a pushblock can be pushed to from its start (a player stands on the cell behind it: floor, or ' ' (floor for
        /// players only); the cell ahead must be floor), and whether it can get stuck (some cell it reaches can't get back to all the others).
        /// Where the player walks round to the cell behind isn't checked: the grid's '#' and edges are taken as the only walls.</summary>
        public static List<int> PushReach(BlockDef b, int start, out bool stuck)
        {
            int rows = b.Rows.Count, cols = b.Rows.Count == 0 ? 0 : b.Rows.Max(r => r.Length);
            bool Floor(int c, int r) => TriggerReader.StateAt(b, c, r) >= 0;
            // (a player stands on floor or ' ' (floor for players only); off the grid, or on '#', they can't: the grid is all the room there is)
            bool Stand(int c, int r) => c >= 0 && r >= 0 && r < rows && c < b.Rows[r].Length && (Floor(c, r) || b.Rows[r][c] == ' ');
            List<int> Moves(int s)
            {
                var list = new List<int>();
                var at = b.Cells[s];
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int tc = at.x + d.Item1, tr = at.y + d.Item2;
                    if (Floor(tc, tr) && Stand(at.x - d.Item1, at.y - d.Item2)) list.Add(TriggerReader.StateAt(b, tc, tr));
                }
                return list;
            }
            HashSet<int> From(int s)
            {
                var seen = new HashSet<int> { s };
                var todo = new Stack<int>();
                todo.Push(s);
                while (todo.Count > 0) foreach (int n in Moves(todo.Pop())) if (seen.Add(n)) todo.Push(n);
                return seen;
            }
            start = Math.Max(0, Math.Min(b.Cells.Count - 1, start));
            HashSet<int> reach = From(start);
            stuck = reach.Any(s => !From(s).SetEquals(reach));
            return reach.OrderBy(x => x).ToList();
        }
    }
}
