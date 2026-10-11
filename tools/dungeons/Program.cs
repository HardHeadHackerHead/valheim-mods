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
    /// Rooms whose mechanisms join doorways are also checked for gaps a player could walk, jump or fall across (GapCheck.cs); set the
    /// environment variable GAPDEBUG=room:doorwayA:doorwayB to print how A gets to B, hop by hop, with the part under each spot.
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
            int? map = null, describe = null, attempt = null, profile = null;
            bool noShapes = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seeds": seeds = int.Parse(args[++i]); break;
                    case "--from": from = int.Parse(args[++i]); break;
                    case "--map": map = int.Parse(args[++i]); break;
                    case "--describe": describe = int.Parse(args[++i]); break;
                    case "--try": attempt = int.Parse(args[++i]); break;
                    case "--profile": profile = int.Parse(args[++i]); break;
                    case "--no-shapes": noShapes = true; break;   // (every prefab one box, as before prefab_shapes.json)
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

            Dictionary<string, List<UnityEngine.Vector3[]>> bounds = GapCheck.Bounds(Path.Combine(repo, "tools", "dungeons", "prefab_bounds.json"),
                noShapes ? null : Path.Combine(repo, "tools", "dungeons", "prefab_shapes.json"));

            var rooms = new Dictionary<string, JObject>();
            foreach (JObject r in JArray.Parse(File.ReadAllText(catalogue)).OfType<JObject>()) rooms[(string)r["name"]] = r;
            foreach (string file in packFiles)
            {
                var problems = new List<string>();
                Pack pack;
                try { pack = PackReader.Read(JObject.Parse(File.ReadAllText(file)), name => rooms.TryGetValue(name, out JObject r) ? PackReader.FromCatalogue(r) : null, problems); }
                catch (Exception e)   // (the game would leave the pack out: Packs.Get catches it too)
                {
                    Console.WriteLine($"==== Pack {Path.GetFileName(file)}: can't be read: {e.GetType().Name}: {e.Message}");
                    foreach (string line in (e.StackTrace ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Take(3))
                        Console.WriteLine("  " + line.Trim());
                    exit = 1;
                    continue;
                }
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

                // Can the puzzle rooms be walked, jumped or fallen round? (GapCheck: their parts' boxes against what the solver keeps apart.)
                bool oneSeed = describe != null || map != null || profile != null;   // (one seed asked for: no room checks, quick)
                var puzzleRooms = oneSeed ? new List<RoomDef>() : pack.Rooms.Where(r => r.GamePrefab == null && r.Parts.Count > 0 && (r.Doorways.Count > 1 || r.Mechs.Any(m => m.Encounter != null))).ToList();
                if (puzzleRooms.Count > 0)
                {
                    int findings = 0, notChecked = 0, falls = 0, blocked = 0;
                    foreach (RoomDef r in puzzleRooms)
                        foreach (string f in GapCheck.Check(pack, r, bounds))
                        {
                            Console.WriteLine($"  gaps {r.Name}: {f}");
                            if (f.StartsWith("unchecked")) notChecked++; else if (f.Contains(": blocked:")) blocked++; else if (f.Contains("only with a fall")) falls++; else findings++;
                        }
                    Console.WriteLine($"  gap check ({GapCheck.JumpAcross:0} m across, {GapCheck.JumpUp} m up, any height down): {puzzleRooms.Count} rooms of ours: " +
                                      $"{blocked} joins blocked, {findings} walkable or jumpable ways round the puzzle, {falls} falls to declare in \"drops\" or close off" +
                                      $"{(notChecked > 0 ? $", {notChecked} unchecked" : "")}");
                    if (findings + falls + blocked > 0) exit = 1;
                }

                if (profile != null)
                {
                    // How long a layout takes, and its solver alone (the search's size: what makes a puzzle slow).
                    var clock = Stopwatch.StartNew();
                    Layout l = Layouts.Make(pack, profile.Value, out int t);
                    long make = clock.ElapsedMilliseconds;
                    clock.Restart();
                    var sol = new List<string>();
                    string why = null;
                    for (int k = 0; k < 10; k++) why = Layouts.Solve(l, sol);
                    Console.WriteLine($"  seed {profile}: {t} tries, {make} ms; solving it: {clock.ElapsedMilliseconds / 10.0:0.0} ms ({why ?? sol.Count + " uses"})");
                    continue;
                }
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
            int firstOk = -1, bad = 0, replayed = 0, best = -1, bestUses = -1, together = 0, triggered = 0;
            var retried = new Dictionary<string, int>();
            var faults = new Dictionary<string, KeyValuePair<int, string>>();   // kind -> how many, the first
            var wayCaps = new SortedDictionary<string, int>();                  // what closes the way in
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
                int wayIn = Layouts.EntranceDoorway(l);
                foreach (Link k in l.Links.Where(k => (k.Room == 0 && k.Doorway == wayIn) || (k.OtherRoom == 0 && k.OtherDoorway == wayIn)))
                {
                    string cap = l.Rooms[k.Room == 0 && k.Doorway == wayIn ? k.OtherRoom : k.Room].Def.Name;
                    wayCaps[cap] = wayCaps.TryGetValue(cap, out int wc) ? wc + 1 : 1;
                }
                foreach (string p in Validate(l))
                {
                    bad++;
                    string k = Regex.Replace(p, @"\d+", "#");
                    faults[k] = faults.TryGetValue(k, out var f) ? new KeyValuePair<int, string>(f.Key + 1, f.Value) : new KeyValuePair<int, string>(1, $"seed {seed}: {p}");
                }
                replayed++;
                if (Triggered(l)) triggered++;
                var two = new List<string>();   // (shorter with a plate held down by a second player or a creature?)
                if (Layouts.Solve(l, two, true) == null && two.Count < l.Solution.Count) together++;
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
                              $"solution replayed in {replayed}{(triggered > 0 ? $", {triggered} of them with triggers" : "")}): {bad} faults");
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
            if (together > 0) Console.WriteLine($"Shorter for two (a plate held down): {together} of {ok} layouts");
            Console.WriteLine($"Mechanisms per layout: {mechs.Average():0.0} ({mechs.Min()}-{mechs.Max()}); locks {locks.Average():0.0}; " +
                              $"doors {doors.Average():0.0} ({doors.Min()}-{doors.Max()})");
            Console.WriteLine("The way in closed by: " + string.Join(", ", wayCaps.Select(p => $"{p.Key} {p.Value}")));
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
                var wires = (e.Encounter?.Boss?.Phases.SelectMany(p => p.Wires) ?? Enumerable.Empty<Wire>()).Concat(e.Wires);
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
            if (Environment.GetEnvironmentVariable("REPLAYDEBUG") != null)   // (what the replay ended with)
                Console.WriteLine($"REPLAY seed {l.Seed}: at {at}, states {string.Join(" ", state.Select(x => x.Key + "=" + x.Value))}, items {string.Join(" ", items.Select(x => x.Key + "=" + x.Value))}, " +
                                  $"rooms reached {string.Join(" ", Walk(l, state, at, tm).Select(x => x.Key).Distinct().OrderBy(x => x))}, end {endRoom}");
            if (endRoom >= 0 && !Walk(l, state, at, tm).Any(p => p.Key == endRoom)) yield return "solution: the end isn't reached after it";
        }

        /// <summary>Whether a layout has triggers or blocks the solver models by rules (TriggerSolve).</summary>
        private static bool Triggered(Layout l)
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
