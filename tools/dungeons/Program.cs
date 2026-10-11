using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Dungeons
{
    /// <summary>
    /// Lays out many seeds of each pack outside the game, with the mod's own code (Model.cs, Layouts.cs, LayoutSolver.cs, PackReader.cs),
    /// the game's rooms taken from the catalogue `dungeon rooms` writes (BepInEx/claude/dungeon-rooms.json). Reports per pack: failures and
    /// why, tries, room counts, sizes, floors, how often each room is used, mechanisms, locks, doors, the puzzle's solution lengths, whether
    /// every seed comes out the same twice, faults found by checking each good layout again with other code (a replay of its solution
    /// among them), and a sample map with its mechanisms.
    ///
    ///   dotnet build tools/dungeons -c Release
    ///   tools/dungeons/bin/Release/net48/LayoutTest.exe [pack.json ...] [--seeds 1000] [--from 1] [--map seed [--try n]] [--describe seed] [--look file]
    ///       [--catalogue path]
    /// With no pack named: every pack in mods/Dungeons/packs. Exit code 1 when any seed fails, comes out differently twice, or has a fault.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string repo = FindRepo();
            var packFiles = new List<string>();
            string catalogue = @"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\dungeon-rooms.json";
            int seeds = 1000, from = 1;
            int? map = null, describe = null, attempt = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seeds": seeds = int.Parse(args[++i]); break;
                    case "--from": from = int.Parse(args[++i]); break;
                    case "--map": map = int.Parse(args[++i]); break;
                    case "--describe": describe = int.Parse(args[++i]); break;
                    case "--try": attempt = int.Parse(args[++i]); break;
                    case "--look":   // one look file checked, nothing else
                        string lf = args[++i];
                        List<string> found = LookCheck.Check(JObject.Parse(File.ReadAllText(lf)), Path.GetFileName(lf));
                        foreach (string p in found) Console.WriteLine("  look problem: " + p);
                        Console.WriteLine(found.Count == 0 ? "fine" : found.Count + " problems");
                        return found.Count == 0 ? 0 : 1;
                    case "--catalogue": catalogue = args[++i]; break;
                    default: packFiles.Add(args[i]); break;
                }
            }
            if (packFiles.Count == 0) packFiles.AddRange(Directory.GetFiles(Path.Combine(repo, "mods", "Dungeons", "packs"), "*.json").OrderBy(f => f));

            // The entrance looks: each file checked (LookCheck), and their names, for the packs' "entranceLook".
            var looks = new HashSet<string>();
            int exit = 0;
            string lookDir = Path.Combine(repo, "mods", "Dungeons", "packs", "looks");
            if (Directory.Exists(lookDir))
                foreach (string file in Directory.GetFiles(lookDir, "*.json").OrderBy(f => f))
                {
                    List<string> wrong;
                    try
                    {
                        JObject look = JObject.Parse(File.ReadAllText(file));
                        wrong = LookCheck.Check(look, "looks/" + Path.GetFileName(file));
                        if (!string.IsNullOrEmpty((string)look["name"])) looks.Add(((string)look["name"]).ToLowerInvariant());
                        if ((string)look["name"] != Path.GetFileNameWithoutExtension(file))   // (a pack names the file; the saved prefab comes from "name")
                            wrong.Add($"looks/{Path.GetFileName(file)}: its \"name\" is {look["name"]}, not the file's name");
                    }
                    catch (Exception e) { wrong = new List<string> { $"looks/{Path.GetFileName(file)}: can't be read: {e.Message}" }; }
                    Console.WriteLine($"Entrance look {Path.GetFileNameWithoutExtension(file)}: {(wrong.Count == 0 ? "fine" : wrong.Count + " problems")}");
                    foreach (string p in wrong) Console.WriteLine("  look problem: " + p);
                    if (wrong.Count > 0) exit = 1;
                }

            var rooms = new Dictionary<string, JObject>();
            foreach (JObject r in JArray.Parse(File.ReadAllText(catalogue)).OfType<JObject>()) rooms[(string)r["name"]] = r;
            foreach (string file in packFiles)
            {
                var problems = new List<string>();
                Pack pack = PackReader.Read(JObject.Parse(File.ReadAllText(file)), name => rooms.TryGetValue(name, out JObject r) ? PackReader.FromCatalogue(r) : null, problems);
                Console.WriteLine($"==== Pack {pack.Name} ({pack.Title}): {pack.Rooms.Count} rooms, plan {string.Join(" > ", pack.Plan.Select(Plan))}, " +
                                  $"{pack.MinRooms}-{pack.MaxRooms} rooms, max size {Fmt(PackReader.MaxSize(pack))}, min uses {PackReader.MinUses(pack)}, " +
                                  $"environment {pack.Environment}, theme {pack.Theme ?? "-"}, entrance look {pack.EntranceLook ?? "-"}, " +
                                  $"doors {(pack.Doors.Count == 0 ? "none" : string.Join(", ", pack.Doors.Select(d => $"\"{d.Key}\" {d.Value}")))} at {pack.DoorChance:0.##}");
                Console.WriteLine($"  mechanisms in its rooms: {pack.Rooms.Sum(r => r.Mechs.Count)} " +
                                  $"({string.Join(", ", pack.Rooms.SelectMany(r => r.Mechs).GroupBy(m => m.Kind).Select(g => $"{g.Count()} {g.Key}"))})");
                Console.WriteLine("  creatures: " + string.Join("; ", PackReader.Biomes.Select(b =>
                    $"{b} {string.Join(" ", PackReader.CreatureTiers(pack, b).Select(t => t + "=" + string.Join(",", PackReader.Creatures(pack, b, t))))}")));
                if (pack.EntranceLook != null && !looks.Contains(pack.EntranceLook.ToLowerInvariant()))
                    problems.Add($"{pack.Name}: entranceLook {pack.EntranceLook} has no file in packs/looks (the default look is used)");
                foreach (string p in problems) Console.WriteLine("  pack problem: " + p);

                if (describe != null) { Console.WriteLine(Layouts.Describe(Layouts.Make(pack, describe.Value)).ToString()); continue; }
                if (map != null && attempt != null)
                {
                    Layout one = Layouts.Try(pack, map.Value, attempt.Value);
                    Console.WriteLine(Layouts.Map(one));
                    // Where the player can stand, and in how few uses (room.doorway id: uses).
                    Console.WriteLine("Reachable: " + string.Join(", ", Layouts.UsesToReach(one).OrderBy(p => p.Key)
                        .Select(p => $"{p.Key >> 16}.{one.Rooms[(int)(p.Key >> 16)].Def.Doorways[(int)(p.Key & 0xFFFF)].Id}:{p.Value}")));
                    continue;
                }
                if (map != null) { Console.WriteLine(Layouts.Map(Layouts.Make(pack, map.Value))); continue; }
                if (Run(pack, seeds, from) != 0 || problems.Count > 0) exit = 1;
                Console.WriteLine();
            }
            return exit;
        }

        private static string Plan(PlanStep s) => Layouts.StepName(s) + (s.OpensWith != null ? $" (opens with {s.OpensWith})" : "");

        private static int Run(Pack pack, int seeds, int from)
        {
            int ok = 0, differ = 0, sealedSeeds = 0, sealedDoors = 0;
            var why = new Dictionary<string, int>();
            var tries = new SortedDictionary<int, int>();
            var counts = new SortedDictionary<int, int>();
            var floors = new SortedDictionary<int, int>();
            var uses = new SortedDictionary<int, int>();
            var use = new Dictionary<string, int>();
            var inLayouts = new Dictionary<string, int>();
            var sizes = new List<UnityEngine.Vector3>();
            var caps = new List<int>();
            var mechs = new List<int>();
            var doors = new List<int>();
            var locks = new List<int>();
            var ms = new List<double>();
            int firstOk = -1, bad = 0, replayed = 0, best = -1, bestUses = -1;
            var retried = new Dictionary<string, int>();
            var faults = new Dictionary<string, KeyValuePair<int, string>>();   // kind -> how many, the first
            var watch = new Stopwatch();
            for (int seed = from; seed < from + seeds; seed++)
            {
                watch.Restart();
                Layout l = Layouts.Make(pack, seed, out int t);
                watch.Stop();
                ms.Add(watch.Elapsed.TotalMilliseconds);
                if (Layouts.Describe(l).ToString() != Layouts.Describe(Layouts.Make(pack, seed)).ToString()) differ++;
                tries[t] = tries.TryGetValue(t, out int n) ? n + 1 : 1;
                for (int k = 0; k < t - 1; k++)
                    foreach (string p in Layouts.Try(pack, seed, k).Problems)
                    {
                        string key = Regex.Replace(p, @"\d+", "#");
                        retried[key] = retried.TryGetValue(key, out int c) ? c + 1 : 1;
                    }
                if (!l.Ok)
                {
                    foreach (string p in l.Problems.Skip(1))
                    {
                        string k = Regex.Replace(p, @"\d+", "#");
                        why[k] = why.TryGetValue(k, out int c) ? c + 1 : 1;
                    }
                    continue;
                }
                ok++;
                if (firstOk < 0) firstOk = seed;
                foreach (string p in Validate(l))
                {
                    bad++;
                    string k = Regex.Replace(p, @"\d+", "#");
                    faults[k] = faults.TryGetValue(k, out var f) ? new KeyValuePair<int, string>(f.Key + 1, f.Value) : new KeyValuePair<int, string>(1, $"seed {seed}: {p}");
                }
                if (l.Rooms.All(r => r.Def.Mechs.All(m => m.Cycle <= 0f))) replayed++;
                int count = Layouts.RoomCount(l);
                counts[count] = counts.TryGetValue(count, out n) ? n + 1 : 1;
                uses[l.Solution.Count] = uses.TryGetValue(l.Solution.Count, out n) ? n + 1 : 1;
                if (l.Solution.Count > bestUses) { bestUses = l.Solution.Count; best = seed; }
                caps.Add(l.Rooms.Count - count);
                sizes.Add(l.Box.size);
                mechs.Add(l.Rooms.Sum(r => r.Def.Mechs.Count));
                doors.Add(l.Doors.Count);
                locks.Add(l.Locks.Count);
                int sealedHere = Layouts.Sealed(l).Count;
                sealedDoors += sealedHere;
                if (sealedHere > 0) sealedSeeds++;
                var levels = new List<float>();
                foreach (PlacedRoom r in l.Rooms)
                    for (int d = 0; d < r.Def.Doorways.Count; d++)
                    {
                        float y = r.DoorwayPos(d).y;
                        if (!levels.Any(v => Math.Abs(v - y) < 1f)) levels.Add(y);
                    }
                floors[levels.Count] = floors.TryGetValue(levels.Count, out n) ? n + 1 : 1;
                foreach (var g in l.Rooms.GroupBy(r => r.Def.Name))
                {
                    use[g.Key] = (use.TryGetValue(g.Key, out n) ? n : 0) + g.Count();
                    inLayouts[g.Key] = (inLayouts.TryGetValue(g.Key, out n) ? n : 0) + 1;
                }
            }

            Console.WriteLine($"Seeds {from}..{from + seeds - 1}: {ok} good, {seeds - ok} failed, {differ} came out differently the second time");
            Console.WriteLine($"Time per layout: {ms.Average():0.00} ms on average, {ms.Max():0.0} ms at most");
            Console.WriteLine("Tries needed: " + string.Join(", ", tries.Select(p => $"{p.Key}: {p.Value}")));
            Console.WriteLine($"Checked again here (doorways meet, boxes clear, max, min depth, every doorway closed, gates on locks, wires, doors, " +
                              $"solution replayed in {replayed} with no cycling mechanisms): {bad} faults");
            foreach (var p in faults.OrderByDescending(p => p.Value.Key)) Console.WriteLine($"  {p.Value.Key,5}  {p.Key}   (first: {p.Value.Value})");
            if (retried.Count > 0)
            {
                Console.WriteLine("Why tries were retried:");
                foreach (var p in retried.OrderByDescending(p => p.Value).Take(12)) Console.WriteLine($"  {p.Value,5}  {p.Key}");
            }
            if (why.Count > 0)
            {
                Console.WriteLine("Why seeds failed (the last try's problems):");
                foreach (var p in why.OrderByDescending(p => p.Value)) Console.WriteLine($"  {p.Value,5}  {p.Key}");
            }
            if (ok == 0) return 1;
            Console.WriteLine("Rooms (not counting end caps): " + string.Join(", ", counts.Select(p => $"{p.Key}: {p.Value}")) +
                              $"  (average {counts.Sum(p => p.Key * p.Value) / (double)ok:0.0})");
            Console.WriteLine("Solution length (uses): " + string.Join(", ", uses.Select(p => $"{p.Key}: {p.Value}")) +
                              $"  (average {uses.Sum(p => p.Key * p.Value) / (double)ok:0.0})");
            Console.WriteLine($"Mechanisms per layout: {mechs.Average():0.0} ({mechs.Min()}-{mechs.Max()}); locks {locks.Average():0.0}; " +
                              $"doors {doors.Average():0.0} ({doors.Min()}-{doors.Max()})");
            Console.WriteLine($"End caps: {caps.Average():0.0} on average, {caps.Min()}-{caps.Max()}");
            Console.WriteLine($"Sealed doorways (no cap fitted): {sealedDoors} in {sealedSeeds} layouts");
            Console.WriteLine("Floors (doorway heights): " + string.Join(", ", floors.Select(p => $"{p.Key}: {p.Value}")));
            Console.WriteLine($"Size x: {sizes.Min(s => s.x):0}-{sizes.Max(s => s.x):0} m (average {sizes.Average(s => s.x):0}), " +
                              $"y: {sizes.Min(s => s.y):0}-{sizes.Max(s => s.y):0} (average {sizes.Average(s => s.y):0}), " +
                              $"z: {sizes.Min(s => s.z):0}-{sizes.Max(s => s.z):0} (average {sizes.Average(s => s.z):0})");
            Console.WriteLine("Rooms used (in how many good layouts, how many in all):");
            foreach (RoomDef r in pack.Rooms)
                Console.WriteLine($"  {r.Name,-34} {(inLayouts.TryGetValue(r.Name, out int a) ? a : 0),5} layouts {(use.TryGetValue(r.Name, out int b) ? b : 0),6}");
            int sample = bestUses > 0 ? best : firstOk;
            Console.WriteLine($"\nSample (seed {sample}{(bestUses > 0 ? ", the longest solution" : "")}): " + Layouts.Map(Layouts.Make(pack, sample)));
            return seeds - ok > 0 || differ > 0 || bad > 0 ? 1 : 0;
        }

        /// <summary>A good layout checked again with other code than the builder's: joined doorways at the same place (PlacedRoom's own
        /// turning, the engine's way), facing opposite ways, of the same type; no two boxes overlapping by more than the margin; each room's
        /// max and min depth kept; every doorway joined; the way in capped; nothing past the end; every lock closed by a gate its key's
        /// room is wired to; every wire acting on something; doors only where they may go; the solution replayed.</summary>
        private static IEnumerable<string> Validate(Layout l)
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
            foreach (Link k in l.Doors)
            {
                PlacedRoom a = l.Rooms[k.Room], b = l.Rooms[k.OtherRoom];
                Doorway da = a.Def.Doorways[k.Doorway], db = b.Def.Doorways[k.OtherDoorway];
                if (!da.AllowDoor || !db.AllowDoor || a.Def.EndCap || b.Def.EndCap || !l.Pack.Doors.ContainsKey(da.Type ?? "") || Math.Abs(a.DoorwayFacing(k.Doorway).y) > 0.5f)
                    yield return $"a door at {k.Room}.{k.Doorway} where none may go";
                if (a.Def.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == da.Id) || b.Def.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == db.Id))
                    yield return $"a door at {k.Room}.{k.Doorway} where a gate stands";
            }
            if (l.Rooms.All(r => r.Def.Mechs.All(m => m.Cycle <= 0f)))
                foreach (string p in Replay(l)) yield return p;
        }

        /// <summary>The solution played through, step by step, with a walk of its own: each use within reach of where the player is,
        /// and the end reached after the last. (Layouts with cycling mechanisms aren't replayed: their waits aren't in the solution.)</summary>
        private static IEnumerable<string> Replay(Layout l)
        {
            var state = new Dictionary<string, int>();
            foreach (PlacedRoom r in l.Rooms) foreach (MechDef m in r.Def.Mechs) state[r.Index + "." + m.Id] = m.Start;
            var at = new KeyValuePair<int, int>(0, Math.Max(0, Layouts.EntranceDoorway(l)));
            foreach (string key in l.Solution)
            {
                MechDef lever = Layouts.Mech(l, key, out int room);
                if (lever == null) { yield return $"solution: {key} isn't a mechanism"; yield break; }
                HashSet<KeyValuePair<int, int>> reach = Walk(l, state, at);
                List<Doorway> doorways = l.Rooms[room].Def.Doorways;
                var from = Enumerable.Range(0, doorways.Count).Where(d => lever.ReachedFrom == null || doorways[d].Id == lever.ReachedFrom)
                    .Select(d => new KeyValuePair<int, int>(room, d)).Where(reach.Contains).ToList();
                if (from.Count == 0) { yield return $"solution: {key} can't be reached when it's used"; yield break; }
                at = from[0];
                foreach (Wire w in lever.Wires)
                {
                    string target = Layouts.Target(l, room, w);
                    if (target == null || w.Do == "hold") continue;
                    MechDef t = Layouts.Mech(l, target, out _);
                    state[target] = Layouts.After(t, state[target], w.Do);
                }
            }
            int endRoom = l.Rooms.FindIndex(r => r.Step == Layouts.StepName(l.Pack.Plan.Last()));
            if (endRoom >= 0 && !Walk(l, state, at).Any(p => p.Key == endRoom)) yield return "solution: the end isn't reached after it";
        }

        /// <summary>Every (room, doorway) the player can walk to from one, with the mechanisms in these states.</summary>
        private static HashSet<KeyValuePair<int, int>> Walk(Layout l, Dictionary<string, int> state, KeyValuePair<int, int> start)
        {
            bool Closed(int room, int d) => l.Rooms[room].Def.Mechs.Any(m => Layouts.IsGate(m) && m.Doorway == l.Rooms[room].Def.Doorways[d].Id && state[room + "." + m.Id] == 0);
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
                foreach (MechDef m in def.Mechs) joins.AddRange(m.Joins.Where(j => j.State < 0 || j.State == state[p.Key + "." + m.Id]));
                if (def.Inside.Count == 0 && def.Mechs.All(m => m.Joins.Count == 0))
                    for (int d = 0; d < def.Doorways.Count; d++) next.Add(new KeyValuePair<int, int>(p.Key, d));
                foreach (Join j in joins)
                {
                    if (j.A == id && Index(j.B) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.B)));
                    if (j.B == id && Index(j.A) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.A)));
                }
                foreach (Join j in PackReader.Drops(l.Pack, def))   // one way
                    if (j.A == id && Index(j.B) >= 0) next.Add(new KeyValuePair<int, int>(p.Key, Index(j.B)));
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

        private static string Fmt(UnityEngine.Vector3 v) => v == UnityEngine.Vector3.zero ? "none" : $"{v.x:0}x{v.y:0}x{v.z:0}";

        /// <summary>The repo's root: the first folder up from the program's, or else from the current one, with mods/Dungeons in it.</summary>
        private static string FindRepo()
        {
            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                string dir = start;
                while (dir != null && !Directory.Exists(Path.Combine(dir, "mods", "Dungeons"))) dir = Path.GetDirectoryName(dir.TrimEnd('\\', '/'));
                if (dir != null) return dir;
            }
            return ".";
        }
    }
}
