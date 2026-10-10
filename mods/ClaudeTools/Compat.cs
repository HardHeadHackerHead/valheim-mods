using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// Comparing mods' patches, shared by the game (Clashes.cs) and the modkit command line: who patches what, how risky each patch is,
    /// game systems (methods that do one job), and the clashes report. Nothing here needs the game running.
    /// </summary>
    internal static class Compat
    {
        internal class Use
        {
            public string Mod, Guid, From, Target, Kind, Skips, Method, Args; // Args: which version (overload) of the method, e.g. "(ItemData, Int32)"
            public string[] ChangesArgs = new string[0];
            public bool ChangesResult, Guessed, RunOriginal;
            public int Priority = 400;

            public bool Rewrites => Kind == "transpiler" || Kind == "il-hook" || Kind == "ilmanipulator";
            public bool CanSkip => Skips == "sometimes" || Skips == "always" || Kind == "hook";
            public bool Risky => Rewrites || CanSkip || ChangesResult || ChangesArgs.Length > 0;

            public string Describe()
            {
                var parts = new List<string> { Kind };
                if (Skips == "always") parts.Add("always skips the game's method");
                else if (Skips == "sometimes") parts.Add("can skip the game's method");
                if (Kind == "hook") parts.Add("can replace the game's method");
                if (ChangesResult) parts.Add("changes the result");
                if (ChangesArgs.Length > 0) parts.Add("changes " + string.Join(", ", ChangesArgs));
                if (Priority != 400) parts.Add("priority " + Priority);
                if (Guessed) parts.Add("target read from code");
                return string.Join(", ", parts.ToArray());
            }
        }

        // ---- game systems: methods that do one job, so mods patching different ones can still clash ----

        internal static readonly string[][] Systems =
        {
            new[] { "crafting payment", "Inventory.CountItems", "Inventory.HaveItem", "Inventory.RemoveItem", "Player.HaveRequirementItems", "Player.HaveRequirements",
                    "Player.ConsumeResources", "Player.GetFirstRequiredItem", "InventoryGui.DoCrafting" },
            new[] { "recipe list", "InventoryGui.UpdateRecipeList", "InventoryGui.UpdateCraftingPanel", "InventoryGui.AddRecipeToList", "Player.GetAvailableRecipes",
                    "Player.UpdateKnownRecipesList", "Player.UpdateAvailablePiecesList" },
            new[] { "inventory size", "Player.SetInventorySize", "InventoryGui.SetInventorySize", "Player.OnSpawned", "Inventory..ctor" },
            new[] { "inventory slots", "Inventory.FindEmptySlot", "Inventory.HaveEmptySlot", "Inventory.GetEmptySlots", "Inventory.CanAddItem", "Inventory.AddItem",
                    "Inventory.MoveItemToThis", "Inventory.MoveAll", "InventoryGrid.DropItem" },
            new[] { "saving items and characters", "Inventory.Save", "Inventory.Load", "ItemDrop.SaveToZDO", "ItemDrop.LoadFromZDO", "Player.Save", "Player.Load",
                    "PlayerProfile.SavePlayerData", "PlayerProfile.LoadPlayerData", "PlayerProfile.SavePlayerToDisk", "PlayerProfile.LoadPlayerFromDisk" },
            new[] { "death and tombstones", "Player.OnDeath", "Player.CreateTombStone", "TombStone.*", "Inventory.MoveInventoryToGrave", "Humanoid.UnequipAllItems" },
            new[] { "equipment", "Humanoid.EquipItem", "Humanoid.UnequipItem", "Humanoid.SetupEquipment", "Humanoid.IsItemEquiped", "Player.QueueEquipAction",
                    "Player.QueueUnequipAction", "VisEquipment.*" },
            new[] { "portals and teleporting", "TeleportWorld.*", "Game.ConnectPortals", "ZDOMan.GetPortalList", "Player.TeleportTo", "Teleport.*", "Player.IsTeleportable",
                    "Inventory.IsTeleportable" },
            new[] { "containers", "Container.*", "InventoryGui.Show", "InventoryGui.Hide" },
            new[] { "picking up", "Player.AutoPickup", "Humanoid.Pickup", "ItemDrop.Pickup", "ItemDrop.Interact", "Pickable.*" },
            new[] { "smelting, cooking and fermenting", "Smelter.*", "CookingStation.*", "Fermenter.*", "Beehive.*" },
            new[] { "building", "Player.PlacePiece", "Player.TryPlacePiece", "Player.UpdatePlacement", "Player.UpdatePlacementGhost", "Player.SetupPlacementGhost",
                    "Player.RemovePiece", "Player.CheckCanRemovePiece", "Piece.*", "WearNTear.*" },
            new[] { "carry weight", "Player.GetMaxCarryWeight", "Inventory.GetTotalWeight", "Inventory.UpdateTotalWeight" },
            new[] { "food, health and stamina", "Player.UseStamina", "Player.HaveStamina", "Player.UpdateFood", "Player.EatFood", "Player.CanEat", "Player.GetTotalFoodValue",
                    "Player.SetMaxHealth", "Player.SetMaxStamina", "SEMan.*" },
            new[] { "skills", "Skills.*" },
            new[] { "combat and damage", "Character.Damage", "Character.RPC_Damage", "Character.ApplyDamage", "Humanoid.BlockAttack", "Attack.*", "HitData.*" },
            new[] { "keys and input", "ZInput.*", "PlayerController.TakeInput" },
            new[] { "inventory screen", "InventoryGui.*", "InventoryGrid.*" },
            new[] { "map", "Minimap.*" },
            new[] { "creatures' AI", "BaseAI.*", "MonsterAI.*", "AnimalAI.*", "Tameable.*" },
            new[] { "spawning", "SpawnSystem.*", "CreatureSpawner.*", "SpawnArea.*" },
            new[] { "world generation", "ZoneSystem.*", "WorldGenerator.*", "Heightmap.*", "TerrainComp.*" },
            new[] { "network", "ZNet.*", "ZDOMan.*", "ZRoutedRpc.*", "ZNetView.*" },
            // everyone adds items and pieces here; stacking postfixes is normal
            new[] { "registering items and pieces", "ObjectDB.Awake", "ObjectDB.CopyOtherDB", "ZNetScene.Awake", "FejdStartup.Awake", "FejdStartup.SetupObjectDB" },
        };

        internal const string Registration = "registering items and pieces";

        /// <summary>Systems where two mods doing the same job can cost players items: their overlaps rank first among the ones to check.</summary>
        internal static readonly HashSet<string> HighStakes = new HashSet<string>
            { "crafting payment", "inventory size", "inventory slots", "saving items and characters", "death and tombstones", "containers",
              "smelting, cooking and fermenting" };

        /// <summary>
        /// Methods where a prefix that skips the game's method usually does the job itself (pays, deposits, hands over). Two such prefixes both
        /// run (HarmonyX runs every prefix), so the job is done twice unless the later one reads __runOriginal.
        /// </summary>
        internal static readonly HashSet<string> DoesTheJob = new HashSet<string>
        {
            "Inventory.RemoveItem", "Inventory.RemoveOneItem", "Player.ConsumeResources", "Smelter.Spawn", "Fermenter.DelayedTap",
            "Fermenter.Tap", "CookingStation.SpawnItem", "Beehive.Extract", "Container.RPC_TakeAllRespons", "Inventory.MoveAll",
        };

        // systems whose overlaps are nearly always window-only input handling (a mod's window blocking the player's keys): not a job done twice
        private static readonly HashSet<string> NotAJob = new HashSet<string> { "keys and input", "inventory screen", Registration };

        internal static string SystemOf(string target)
        {
            string t = target.EndsWith(" (enumerator)") ? target.Substring(0, target.Length - 13) : target;
            foreach (string[] s in Systems)
                for (int i = 1; i < s.Length; i++)
                    if (s[i].EndsWith(".*") ? t.StartsWith(s[i].Substring(0, s[i].Length - 1), StringComparison.Ordinal) : t == s[i]) return s[0];
            return null;
        }

        internal static Use FromScan(JObject p, string mod, string guid, string from) => new Use
        {
            Mod = mod, Guid = guid, From = from, Target = (string)p["target"], Kind = (string)p["kind"], Skips = (string)p["skips"], Method = (string)p["method"],
            ChangesResult = (bool?)p["changesResult"] ?? false, Guessed = (bool?)p["computed"] ?? false, Priority = (int?)p["priority"] ?? 400,
            RunOriginal = (bool?)p["runOriginal"] ?? false, Args = (string)p["args"],
            ChangesArgs = (p["changesArgs"] as JArray)?.Select(x => (string)x).ToArray() ?? new string[0],
        };


        /// <summary>Library mods' patches (downloaded scan.json files and the shipped list), leaving out the GUIDs given (installed mods).</summary>
        internal static List<Use> UsesFromLibrary(IEnumerable<JObject> scans, ISet<string> skip)
        {
            var uses = new List<Use>();
            foreach (JObject scan in scans)
            {
                string package = (string)scan["package"];
                int rank = (int?)scan["rank"] ?? 0;
                string from = (bool?)scan["shipped"] == true ? "shipped list" : "library";
                foreach (JObject dll in scan["dlls"] ?? new JArray())
                {
                    string guid = (dll["plugins"] as JArray)?.Select(p => (string)p["guid"]).FirstOrDefault(g => g != null) ?? package;
                    if (skip != null && skip.Contains(guid)) continue;
                    foreach (JObject p in dll["patches"] ?? new JArray())
                        uses.Add(FromScan(p, package, guid, rank > 0 ? from + ", #" + rank : from));
                }
            }
            return uses;
        }

        /// <summary>Installed mods' patches from their DLL scans (without the game: Harmony's live list is better when it runs).</summary>
        internal static List<Use> UsesFromInstalled(IEnumerable<JObject> scans)
        {
            var uses = new List<Use>();
            foreach (JObject s in scans)
            {
                JObject plugin = (s["plugins"] as JArray)?.Cast<JObject>().FirstOrDefault();
                if (plugin == null) continue; // (a library DLL, not a mod)
                foreach (JObject p in s["patches"] ?? new JArray())
                    uses.Add(FromScan(p, (string)plugin["name"], (string)plugin["guid"], "installed"));
            }
            return uses;
        }

        /// <summary>The downloaded library mods (library/mods/*/scan.json), then the shipped list's mods not among them.</summary>
        internal static IEnumerable<JObject> LibraryScans(string modsDir, IEnumerable<JObject> shipped)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(modsDir))
            {
                foreach (string dir in Directory.GetDirectories(modsDir))
                {
                    JObject scan = ReadJson(Path.Combine(dir, "scan.json"));
                    if (scan == null) continue;
                    seen.Add((string)scan["package"] ?? Path.GetFileName(dir));
                    yield return scan;
                }
            }
            foreach (JObject m in shipped ?? Enumerable.Empty<JObject>()) if (!seen.Contains((string)m["package"])) yield return m;
        }

        /// <summary>The shipped patch map (gzipped JSON) as library entries; <paramref name="built"/> says when it was made.</summary>
        internal static List<JObject> ReadShipped(Stream gzipped, out string built)
        {
            built = "";
            var list = new List<JObject>();
            if (gzipped == null) return list;
            using (var gz = new System.IO.Compression.GZipStream(gzipped, System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
            {
                JObject map = JObject.Parse(reader.ReadToEnd());
                built = (string)map["built"] ?? "";
                foreach (JObject m in map["mods"] ?? new JArray()) { m["shipped"] = true; list.Add(m); }
            }
            return list;
        }

        // ---- names that become folders ----

        private static readonly Regex PackageName = new Regex(@"^[A-Za-z0-9_]+-[A-Za-z0-9_.]+$");

        /// <summary>A Thunderstore package name (Namespace-Name) that is safe as a folder name: no paths, no "..".</summary>
        internal static bool IsPackage(string name) => !string.IsNullOrEmpty(name) && name.Length < 200 && PackageName.IsMatch(name) && !name.Contains("..");

        /// <summary><paramref name="root"/>/<paramref name="name"/>, refusing anything that would land outside root (a name with a path in it).</summary>
        internal static string Inside(string root, string name)
        {
            if (!IsPackage(name)) throw new ArgumentException($"'{name}' is not a Thunderstore name (Namespace-Name)");
            string full = Path.GetFullPath(Path.Combine(root, name));
            string top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(top, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"'{name}' would be outside the library");
            return full;
        }

        internal static JObject ReadJson(string path)
        {
            try { return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null; }
            catch (Exception) { return null; }
        }

        // ---- comparing ----

        internal static readonly string[] Levels = { "stacks", "check", "likely" };

        /// <summary>
        /// 2 (likely): one always skips the game's method (it has taken the method over) while the other changes it too, or both rewrite
        /// it. 1 (check): either can skip it, rewrites it, or changes its result or arguments. Two prefixes that each skip only sometimes are
        /// usually each handling their own things (that is normal), so they are "check", not "likely". 0: they stack.
        /// </summary>
        internal static int Level(List<Use> a, List<Use> b, string target = null)
        {
            // two prefixes that each skip a method that pays or hands out (the same version of it): both do the job, unless the one that runs
            // later (lower priority) reads __runOriginal
            if (target != null && DoesTheJob.Contains(target))
            {
                foreach (Use x in a.Where(u => u.CanSkip && u.Kind == "prefix"))
                    foreach (Use y in b.Where(u => u.CanSkip && u.Kind == "prefix"))
                    {
                        if (x.Args != null && y.Args != null && x.Args != y.Args) continue; // different versions of the method
                        Use later = x.Priority < y.Priority ? x : y.Priority < x.Priority ? y : null;
                        bool handled = later != null ? later.RunOriginal : x.RunOriginal && y.RunOriginal;
                        if (!handled) return 2;
                    }
            }
            bool aAlways = a.Any(u => u.Skips == "always"), bAlways = b.Any(u => u.Skips == "always");
            bool aRisky = a.Any(u => u.Risky), bRisky = b.Any(u => u.Risky);
            bool aRewrite = a.Any(u => u.Rewrites), bRewrite = b.Any(u => u.Rewrites);
            if ((aAlways && bRisky) || (bAlways && aRisky) || (aRewrite && bRewrite)) return 2;
            return aRisky || bRisky ? 1 : 0;
        }

        internal static string Summary(List<Use> uses) => string.Join("; ", uses.Select(u => u.Describe()).Distinct().ToArray());

        internal static JObject Compare(List<Use> installed, List<Use> library, string only, bool all)
        {
            var byMod = installed.Concat(library).GroupBy(u => u.Guid).ToDictionary(g => g.Key, g => g.ToList());
            var mine = installed.Select(u => u.Guid).Distinct()
                .Where(g => only == null || Matches(byMod[g][0], only)).ToList();
            var pairs = new List<JObject>();
            var seen = new HashSet<string>();
            foreach (string a in mine)
            {
                List<Use> au = byMod[a];
                var aTargets = au.GroupBy(u => u.Target).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var kv in byMod)
                {
                    string b = kv.Key;
                    if (b == a || !seen.Add(string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a)) continue;
                    List<Use> bu = kv.Value;
                    var same = new List<KeyValuePair<int, JObject>>();
                    foreach (var bt in bu.GroupBy(u => u.Target))
                    {
                        if (!aTargets.TryGetValue(bt.Key, out List<Use> at)) continue;
                        int level = Level(at, bt.ToList(), bt.Key);
                        if (level == 0 && !all) continue;
                        same.Add(new KeyValuePair<int, JObject>(level, new JObject
                        {
                            ["method"] = bt.Key, ["system"] = SystemOf(bt.Key), ["level"] = Levels[level], ["a"] = Summary(at), ["b"] = Summary(bt.ToList()),
                        }));
                    }
                    // the same job through different methods
                    var related = new List<JObject>();
                    var sameTargets = new HashSet<string>(same.Select(s => (string)s.Value["method"]));
                    foreach (var sys in au.Where(u => u.Risky).GroupBy(u => SystemOf(u.Target)).Where(g => g.Key != null && !NotAJob.Contains(g.Key)))
                    {
                        var bs = bu.Where(u => u.Risky && SystemOf(u.Target) == sys.Key && !aTargets.ContainsKey(u.Target)).ToList();
                        var asys = sys.Where(u => !bu.Any(x => x.Target == u.Target)).ToList();
                        if (bs.Count == 0 || asys.Count == 0) continue;
                        related.Add(new JObject
                        {
                            ["system"] = sys.Key,
                            ["a"] = new JArray(asys.Select(u => u.Target + " (" + u.Describe() + ")").Distinct()),
                            ["b"] = new JArray(bs.Select(u => u.Target + " (" + u.Describe() + ")").Distinct()),
                        });
                    }
                    if (same.Count == 0 && related.Count == 0) continue;
                    int worst = Math.Max(same.Count > 0 ? same.Max(s => s.Key) : 0, related.Count > 0 ? 1 : 0);
                    var stakes = same.Where(s => s.Key > 0).Select(s => (string)s.Value["system"]).Concat(related.Select(r => (string)r["system"]))
                        .Where(x => x != null && HighStakes.Contains(x)).Distinct().ToList();
                    pairs.Add(new JObject
                    {
                        ["a"] = au[0].Mod, ["b"] = bu[0].Mod, ["bIs"] = bu[0].From, ["level"] = Levels[worst],
                        ["highStakes"] = stakes.Count > 0 ? new JArray(stakes) : null,
                        ["sameMethods"] = new JArray(same.OrderByDescending(s => s.Key).Take(15).Select(s => s.Value)),
                        ["moreSameMethods"] = Math.Max(0, same.Count - 15),
                        ["sameJob"] = new JArray(related),
                    });
                }
            }
            var sorted = pairs.OrderByDescending(p => Array.IndexOf(Levels, (string)p["level"]))
                              .ThenByDescending(p => p["highStakes"] is JArray h && h.Count > 0)
                              .ThenByDescending(p => ((JArray)p["sameMethods"]).Count + ((JArray)p["sameJob"]).Count).ToList();
            return new JObject
            {
                ["installedMods"] = mine.Count, ["libraryMods"] = library.Select(u => u.Guid).Distinct().Count(),
                ["likely"] = sorted.Count(p => (string)p["level"] == "likely"), ["check"] = sorted.Count(p => (string)p["level"] == "check"),
                ["checkHighStakes"] = sorted.Count(p => (string)p["level"] == "check" && p["highStakes"] is JArray h && h.Count > 0),
                ["pairs"] = new JArray(sorted.Take(80)),
                ["note"] = "likely: one mod has taken a method over (its prefix always skips the game's code) while the other changes it too, or both " +
                           "rewrite it (transpilers). check: either can skip it, rewrites it, or changes its result or arguments, so read both patches. " +
                           "sameJob: different methods doing one job (e.g. both count what the player has for crafting). highStakes: the overlap is in a " +
                           "system where two mods doing one job can cost items (crafting payment, inventory, saving, death, containers): read these first.",
            };
        }

        internal static bool Matches(Use u, string text) =>
            (u.Mod ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || (u.Guid ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;

        // ---- after a game update ----

        /// <summary>The mods (installed and in the library) that patch what a game update removed or changed.</summary>
        internal static JObject UpdateImpact(JObject diff, List<Use> installed, List<Use> library)
        {
            var gone = new HashSet<string>(((JArray)diff["gone"]).Select(x => (string)x));
            var signature = new HashSet<string>(((JArray)diff["signature"]).Select(x => (string)x));
            var changed = new HashSet<string>(((JArray)diff["changed"]).Select(x => (string)x));
            JArray Affected(IEnumerable<Use> uses) => new JArray(uses
                .Select(u => new { u, t = u.Target.EndsWith(" (enumerator)") ? u.Target.Substring(0, u.Target.Length - 13) : u.Target })
                .Where(x => gone.Contains(x.t) || signature.Contains(x.t) || changed.Contains(x.t))
                .GroupBy(x => x.u.Mod)
                .Select(grp => new JObject
                {
                    ["mod"] = grp.Key, ["from"] = grp.First().u.From,
                    ["gone"] = new JArray(grp.Where(x => gone.Contains(x.t)).Select(x => $"{x.u.Target} ({x.u.Kind})").Distinct()),
                    ["signatureChanged"] = new JArray(grp.Where(x => signature.Contains(x.t)).Select(x => $"{x.u.Target} ({x.u.Kind})").Distinct()),
                    ["codeChanged"] = new JArray(grp.Where(x => changed.Contains(x.t)).Select(x => $"{x.u.Target} ({x.u.Describe()})").Distinct()),
                })
                .OrderByDescending(o => ((JArray)o["gone"]).Count * 100 + ((JArray)o["signatureChanged"]).Count * 10 + ((JArray)o["codeChanged"]).Count));
            return new JObject
            {
                ["diff"] = diff, ["installed"] = Affected(installed), ["library"] = Affected(library),
                ["note"] = "gone: the patch can't apply (the mod errors or does nothing). signatureChanged: patches naming arguments or overloads may fail. " +
                           "codeChanged: transpilers may no longer find their pattern, and prefixes that replace the method may now skip new game code.",
            };
        }

        // ---- the pre-release check's view of other mods ----

        /// <summary>What other mods (all but <paramref name="guid"/>) risk on each game method and system, for Check.</summary>
        internal static Check.Others OthersFor(string guid, IEnumerable<Use> all)
        {
            var others = all.Where(u => u.Guid != guid && u.Risky).ToList();
            var byTarget = others.GroupBy(u => u.Target).ToDictionary(x => x.Key, x => x.Select(u => $"{u.Mod} ({u.From}): {u.Describe()}").Distinct().ToList());
            var bySystem = others.GroupBy(u => SystemOf(u.Target) ?? "").ToDictionary(x => x.Key, x => x.Select(u => $"{u.Target} ({u.Mod})").Distinct().ToList());
            return new Check.Others
            {
                SameMethod = t => byTarget.TryGetValue(t, out var l) ? l : new List<string>(),
                SameSystem = s => bySystem.TryGetValue(s, out var l) ? l : new List<string>(),
                SystemOf = SystemOf,
            };
        }

        // ---- the library's index and patch map ----

        /// <summary>index.json's mod list and patchmap.json (every patch by game method) from the library's mods.</summary>
        internal static JArray BuildIndex(IEnumerable<JObject> scans, out JObject patchmap)
        {
            var mods = new JArray();
            var targets = new SortedDictionary<string, JArray>(StringComparer.Ordinal);
            foreach (JObject scan in scans.OrderBy(s => (int?)s["rank"] is int r && r > 0 ? r : int.MaxValue))
            {
                string package = (string)scan["package"];
                var plugins = new JArray();
                int count = 0;
                foreach (JObject dll in scan["dlls"] ?? new JArray())
                {
                    foreach (JObject pl in dll["plugins"] ?? new JArray()) plugins.Add(pl["guid"]);
                    foreach (JObject patch in dll["patches"] ?? new JArray())
                    {
                        count++;
                        string target = (string)patch["target"];
                        if (!targets.TryGetValue(target, out JArray list)) targets[target] = list = new JArray();
                        var entry = (JObject)patch.DeepClone();
                        entry.Remove("target");
                        entry.AddFirst(new JProperty("mod", package));
                        list.Add(entry);
                    }
                }
                mods.Add(new JObject
                {
                    ["package"] = package, ["version"] = scan["version"], ["rank"] = scan["rank"], ["downloads"] = scan["downloads"], ["page"] = scan["page"],
                    ["source"] = (bool?)scan["shipped"] == true ? "shipped with ClaudeTools (no DLL here: library get " + package + ")" : "downloaded (DLL in mods/" + package + "/dll)",
                    ["plugins"] = plugins, ["patches"] = count, ["dlls"] = new JArray((scan["dlls"] ?? new JArray()).Select(d => d["dll"])),
                });
            }
            var map = new JObject();
            foreach (var kv in targets) map[kv.Key] = kv.Value;
            patchmap = new JObject { ["updated"] = DateTime.Now.ToString("s"), ["targets"] = map };
            return mods;
        }

        // ---- answers as readable lines (the game's console, and the command line) ----

        /// <summary>A command's answer as readable lines: values as "name: value", lists one item a line.</summary>
        internal static IEnumerable<string> Lines(JToken t, string indent)
        {
            if (!(t is JObject obj)) { yield return indent + Inline(t); yield break; }
            foreach (JProperty p in obj.Properties())
            {
                if (p.Value.Type == JTokenType.Null) continue;
                if (p.Value is JArray arr)
                {
                    if (arr.Count == 0) continue;
                    if (arr.All(x => x is JValue)) { yield return $"{indent}{p.Name}: {string.Join(", ", arr.Select(x => x.ToString()).ToArray())}"; continue; }
                    yield return $"{indent}{p.Name} ({arr.Count}):";
                    foreach (JToken item in arr) yield return indent + "  " + Inline(item);
                }
                else if (p.Value is JObject inner)
                {
                    yield return $"{indent}{p.Name}:";
                    foreach (string line in Lines(inner, indent + "  ")) yield return line;
                }
                else yield return $"{indent}{p.Name}: {p.Value}";
            }
        }

        internal static string Inline(JToken t)
        {
            if (t is JValue) return t.ToString();
            if (t is JArray arr) return string.Join(" | ", arr.Select(Inline).ToArray());
            var parts = new List<string>();
            foreach (JProperty p in ((JObject)t).Properties())
            {
                if (p.Value.Type == JTokenType.Null) continue;
                if (p.Value is JArray arr2) { if (arr2.Count > 0) parts.Add($"{p.Name}: [{string.Join(" | ", arr2.Select(Inline).ToArray())}]"); }
                else if (p.Value is JObject) parts.Add($"{p.Name}: {{...}}");
                else parts.Add($"{p.Name}: {p.Value}");
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
