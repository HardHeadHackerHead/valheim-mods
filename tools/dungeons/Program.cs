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
            if (Environment.GetEnvironmentVariable("REPLAYDEBUG") != null) LayoutAudit.ReplayDebug = Console.WriteLine;
            var packFiles = new List<string>();
            string catalogue = @"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\dungeon-rooms.json";
            int seeds = 1000, from = 1;
            int? map = null, describe = null, attempt = null, profile = null;
            bool noShapes = false;
            string dump = null, stage = "all";
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
                    case "--dump": dump = args[++i]; break;       // (one JSON line per good layout, for make_pack's variety check)
                    case "--jump-skill": GapCheck.Skills(int.Parse(args[++i]), GapCheck.RunSkill); break;   // (the gap check for a player with this Jump skill; 100 unless said)
                    case "--run-skill": GapCheck.Skills(GapCheck.JumpSkill, int.Parse(args[++i])); break;   // (and this Run skill; 100 unless said)
                    case "--run-up": GapCheck.RunUp = true; break;   // (a test: jumps leave at the speed a sprint from standing reaches on the place's longest run)
                    case "--stage": stage = args[++i]; break;     // read (pack problems, looks) | gaps (the gap check) | seeds (the seeds); all: every one
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

            GapCheck.Palettes(Path.Combine(repo, "tools", "dungeons", "palettes"));   // (tall props that can be climbed)
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
                if (stage == "read") { if (problems.Count > 0) exit = 1; continue; }
                bool oneSeed = describe != null || map != null || profile != null || stage == "seeds";   // (one seed asked for, or only the seeds: no room checks)
                var puzzleRooms = oneSeed ? new List<RoomDef>() : pack.Rooms.Where(r => r.GamePrefab == null && r.Parts.Count > 0 && (r.Doorways.Count > 1 || r.Mechs.Any(m => m.Encounter != null))).ToList();
                if (puzzleRooms.Count > 0)
                {
                    int findings = 0, notChecked = 0, falls = 0, blocked = 0, parkour = 0, parkourFails = 0;
                    foreach (RoomDef r in puzzleRooms)
                        foreach (string f in GapCheck.Check(pack, r, bounds))
                        {
                            Console.WriteLine($"  gaps {r.Name}: {f}");
                            if (f.StartsWith("unchecked")) notChecked++; else if (f.Contains("only with Parkour")) { if (f.Contains("(information)")) parkour++; else parkourFails++; }
                            else if (f.Contains(": blocked:")) blocked++; else if (f.Contains("only with a fall")) falls++; else findings++;
                        }
                    Console.WriteLine($"  gap check (Jump {GapCheck.JumpSkill}, Run {GapCheck.RunSkill}: {GapCheck.JumpUp:0.0} m up, {GapCheck.JumpAcross:0.0} m across on the level, further down): {puzzleRooms.Count} rooms of ours: " +
                                      $"{blocked} joins blocked, {findings} walkable or jumpable ways round the puzzle, {falls} falls to declare in \"drops\" or close off" +
                                      $"{(notChecked > 0 ? $", {notChecked} unchecked" : "")}" + (parkourFails > 0 ? $"; with Parkour (the pack allows it, failing): {parkourFails} ways round the puzzle" : "")
                                      + (parkour > 0 ? $"; with Parkour (information, not failing): {parkour} more ways" : ""));
                    if (findings + falls + blocked + parkourFails > 0) exit = 1;
                }

                if (stage == "gaps") continue;
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
                if (Run(pack, seeds, from, dump) != 0 || problems.Count > 0) exit = 1;
                Console.WriteLine();
            }
            return exit;
        }

        private static string Plan(PlanStep s) => Layouts.StepName(s) + (s.OpensWith != null ? $" (opens with {s.OpensWith})" : "");

        private static int Run(Pack pack, int seeds, int from, string dump = null)
        {
            StreamWriter dumpFile = dump != null ? new StreamWriter(dump, false) : null;
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
                dumpFile?.WriteLine(Dump(l));
                int wayIn = Layouts.EntranceDoorway(l);
                foreach (Link k in l.Links.Where(k => (k.Room == 0 && k.Doorway == wayIn) || (k.OtherRoom == 0 && k.OtherDoorway == wayIn)))
                {
                    string cap = l.Rooms[k.Room == 0 && k.Doorway == wayIn ? k.OtherRoom : k.Room].Def.Name;
                    wayCaps[cap] = wayCaps.TryGetValue(cap, out int wc) ? wc + 1 : 1;
                }
                foreach (string p in LayoutAudit.Check(l))
                {
                    bad++;
                    string k = Regex.Replace(p, @"\d+", "#");
                    faults[k] = faults.TryGetValue(k, out var f) ? new KeyValuePair<int, string>(f.Key + 1, f.Value) : new KeyValuePair<int, string>(1, $"seed {seed}: {p}");
                }
                replayed++;
                if (LayoutAudit.Triggered(l)) triggered++;
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

            dumpFile?.Dispose();
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

        /// <summary>A layout for the variety check: its rooms, the way (rooms) from the entrance to the end, the solution.</summary>
        private static string Dump(Layout l)
        {
            int end = l.Pack.Plan.Count > 0 ? l.Rooms.FindIndex(r => r.Step == Layouts.StepName(l.Pack.Plan.Last())) : -1;
            var prev = new Dictionary<int, int> { [0] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach (Link k in l.Links.Where(k => k.Room == r || k.OtherRoom == r))
                {
                    int o = k.Room == r ? k.OtherRoom : k.Room;
                    if (prev.ContainsKey(o)) continue;
                    prev[o] = r;
                    queue.Enqueue(o);
                }
            }
            var path = new List<int>();
            for (int r = end; r >= 0 && prev.ContainsKey(r); r = prev[r]) path.Insert(0, r);
            return new JObject
            {
                ["seed"] = l.Seed,
                ["rooms"] = new JArray(l.Rooms.Select(r => new JObject { ["name"] = r.Def.Name, ["depth"] = r.Depth, ["step"] = r.Step, ["cap"] = r.Def.EndCap })),
                ["path"] = new JArray(path),
                ["solution"] = new JArray(l.Solution),
            }.ToString(Newtonsoft.Json.Formatting.None);
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
