using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using static ClaudeTools.Compat;

namespace ClaudeTools
{
    /// <summary>
    /// The commands. They read files only (the game doesn't need to run): installed mods are the DLLs in BepInEx/plugins and BepInEx/scripts,
    /// the library is BepInEx/claude/library (shared with the game) plus the patch map built into this program.
    /// </summary>
    internal static class Cli
    {
        private const string Usage = @"modkit: Claude Tools' commands for Valheim mod makers, without the game running.

  modkit modcheck <mod name | GUID | path\to\Mod.dll>   the pre-release check (or modkit check): mistakes that lose players' things or break other mods
  modkit who <Type.Method>                           which mods (installed and popular) patch a game method (who system <name>: a whole system)
  modkit clashes [mod] [all]                         installed mods that clash with each other or with popular mods, most likely first
  modkit patches [text]                              every patch in the installed mods, by game method
  modkit scan <path\to\Mod.dll>                      one DLL's plugins and patches
  modkit systems                                     the game systems (methods that do one job)
  modkit game find <text> | game <Type> | game <Type.Member>   the game's real code: signatures, callers, fields
  modkit gameupdate                                  what changed in the game since the last version seen, and which mods patch it
  modkit library                                     the mod library (popular mods and what they patch)
  modkit library get <Namespace-Name> [...]          download a Thunderstore mod's DLL (code only) into the library
  modkit library drop <Namespace-Name> [...]         remove one
  modkit library update [count]                      fetch the most-downloaded mods (count, 10 by default)
  modkit package <mod folder> [--namespace Team]     get a mod ready for Thunderstore: writes its thunderstore.toml (for tcli) and checks
                                                     everything an upload needs (first time: --namespace, the Thunderstore team)
  modkit package check <built .zip>                  check the zip tcli build made, before testing and publishing it

Options: --json (the raw answer), --valheim <folder> (the game), --bepinex <folder> (BepInEx, when a mod manager keeps it in a profile).";

        private static string _valheim, _bepinex, _managed, _library;
        private static string[] _search;

        public static int Run(string[] args, string valheim, string bepinex)
        {
            bool json = args.Contains("--json");
            string[] a = args.Where(x => x != "--json").ToArray();
            _valheim = valheim;
            _bepinex = bepinex;
            _managed = Path.Combine(valheim, "valheim_Data", "Managed");
            _library = Path.Combine(_bepinex, "claude", "library");
            _search = new[] { Path.Combine(_bepinex, "core"), _managed, Path.Combine(_bepinex, "plugins") };
            if (a.Length == 0 || a[0] == "help" || a[0] == "--help" || a[0] == "-h") { Console.WriteLine(Usage); return 0; }
            try
            {
                JObject answer = Command(a);
                if (answer == null) return 1;
                if (json) Console.WriteLine(answer.ToString());
                else foreach (string line in Lines(answer, "")) Console.WriteLine(line);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("modkit " + a[0] + ": " + e.GetBaseException().Message);
                return 1;
            }
        }

        private static string Rest(string[] a, int from) => string.Join(" ", a.Skip(from).ToArray());

        private static JObject Fail(string why) { Console.Error.WriteLine(why); return null; }

        private static JObject Command(string[] a)
        {
            switch (a[0].ToLowerInvariant())
            {
                case "scan":
                    if (a.Length < 2 || !File.Exists(Rest(a, 1))) return Fail("modkit scan <path to a .dll>");
                    return Scan.Dll(Rest(a, 1), _search);

                case "check":
                case "modcheck":
                {
                    string what = Rest(a, 1).Trim('"');
                    if (what.Length == 0) return Fail("modkit modcheck <mod name | GUID | path to a .dll>");
                    List<JObject> scans = Installed();
                    string dll = File.Exists(what) ? what : null, guid = null;
                    if (dll == null)
                    {
                        // by plugin name or GUID, else by the DLL's file name (a plugin's name can differ: "Quad's Cigars" is CigarSmoking.dll)
                        JObject hit = scans.FirstOrDefault(s => (s["plugins"] as JArray)?.Any(p => Is(p, what)) ?? false)
                                   ?? scans.FirstOrDefault(s => string.Equals(Path.GetFileNameWithoutExtension((string)s["path"]), what, StringComparison.OrdinalIgnoreCase));
                        if (hit == null) return Fail($"no installed mod called {what} (use its name, GUID or DLL name, or a path to its .dll)");
                        dll = (string)hit["path"];
                        guid = (string)((hit["plugins"] as JArray)?.FirstOrDefault(p => Is(p, what)) ?? (hit["plugins"] as JArray)?.FirstOrDefault())?["guid"];
                    }
                    else guid = (string)(Scan.Dll(dll, _search)["plugins"] as JArray)?.FirstOrDefault()?["guid"];
                    List<Use> installed = UsesFromInstalled(scans);
                    JObject result = Check.Run(dll, _search, OthersFor(guid, installed.Concat(Library(installed)), LibraryScans(Path.Combine(_library, "mods"), Shipped())));
                    result["note"] = "Candidates, not certainties: read the code at \"where\" before changing it. Rules come from real bugs (lost items, deleted buildings, broken mods).";
                    return result;
                }

                case "who":
                {
                    string text = Rest(a, 1);
                    if (text.Length == 0) return Fail("modkit who <Type.Method>, or who system <name> (modkit systems lists them)");
                    string asked = text.StartsWith("system ", StringComparison.OrdinalIgnoreCase) ? text.Substring(7).Trim() : text;
                    string system = Systems.Select(s => s[0]).FirstOrDefault(s => s.Equals(asked, StringComparison.OrdinalIgnoreCase))
                                 ?? (asked != text ? Systems.Select(s => s[0]).FirstOrDefault(s => s.IndexOf(asked, StringComparison.OrdinalIgnoreCase) >= 0) : null);
                    bool Hit(Use u) => system != null ? SystemOf(u.Target) == system : u.Target.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                    List<Use> installed = UsesFromInstalled(Installed());
                    var all = installed.Where(Hit).Concat(Library(installed).Where(Hit)).GroupBy(u => u.Target).OrderBy(g => g.Key).ToList();
                    return new JObject
                    {
                        ["looked for"] = system != null ? "system: " + system : text, ["methods"] = all.Count,
                        ["list"] = new JArray(all.Take(200).Select(g => new JObject
                        {
                            ["method"] = g.Key, ["system"] = SystemOf(g.Key),
                            ["installed"] = new JArray(g.Where(u => u.From == "installed").Select(u => u.Mod + ": " + u.Describe()).Distinct()),
                            ["library"] = new JArray(g.Where(u => u.From != "installed").Select(u => $"{u.Mod} ({u.From}): {u.Describe()}").Distinct()),
                        })),
                    };
                }

                case "clashes":
                {
                    bool all = a.Skip(1).Any(x => x.Equals("all", StringComparison.OrdinalIgnoreCase));
                    string only = string.Join(" ", a.Skip(1).Where(x => !x.Equals("all", StringComparison.OrdinalIgnoreCase)).ToArray());
                    List<Use> installed = UsesFromInstalled(Installed());
                    JObject report = Compare(installed, Library(installed), only.Length > 0 ? only : null, all);
                    report["installed means"] = "the mods' DLLs in BepInEx/plugins and BepInEx/scripts (with the game running, its clashes command uses what is actually loaded)";
                    return report;
                }

                case "patches":
                {
                    string text = Rest(a, 1);
                    var shown = UsesFromInstalled(Installed()).Where(u => text.Length == 0 || u.Target.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                                    || (u.Mod ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(u => u.Target).OrderBy(g => g.Key).ToList();
                    return new JObject
                    {
                        ["methods"] = shown.Count, ["patches"] = shown.Sum(g => g.Count()),
                        ["list"] = new JArray(shown.Take(300).Select(g => new JObject
                        {
                            ["method"] = g.Key, ["system"] = SystemOf(g.Key), ["patches"] = new JArray(g.Select(u => u.Mod + ": " + u.Describe())),
                        })),
                    };
                }

                case "systems":
                    return new JObject { ["systems"] = new JArray(Systems.Select(s => new JObject { ["system"] = s[0], ["methods"] = new JArray(s.Skip(1)) })) };

                case "game":
                {
                    if (a.Length < 2) return Fail("modkit game find <text> | game <Type> | game <Type.Member>");
                    GameIndex g = GameIndex.Build(_managed);
                    List<Use> installed = UsesFromInstalled(Installed());
                    return GameIndex.Lookup(g, a, Library(installed), _managed);
                }

                case "gameupdate": return GameUpdate();

                case "library": return LibraryCommand(a);

                case "package":
                    return Package.Run(a, _search, CheckFull, () => LibraryScans(Path.Combine(_library, "mods"), Shipped()));

                default:
                    return Fail($"modkit: no command '{a[0]}' (modkit help lists them)");
            }
        }

        /// <summary>modcheck on any DLL, with the installed and popular mods to compare against (as the modcheck command does).</summary>
        private static JObject CheckFull(string dll)
        {
            string guid = (string)(Scan.Dll(dll, _search)["plugins"] as JArray)?.FirstOrDefault()?["guid"];
            List<Use> installed = UsesFromInstalled(Installed());
            return Check.Run(dll, _search, OthersFor(guid, installed.Concat(Library(installed)), LibraryScans(Path.Combine(_library, "mods"), Shipped())));
        }

        private static bool Is(JToken plugin, string what) =>
            string.Equals((string)plugin["name"], what, StringComparison.OrdinalIgnoreCase) || string.Equals((string)plugin["guid"], what, StringComparison.OrdinalIgnoreCase);

        /// <summary>Every DLL in BepInEx/plugins and BepInEx/scripts, scanned (with its path).</summary>
        private static List<JObject> Installed()
        {
            var result = new List<JObject>();
            foreach (string root in new[] { Path.Combine(_bepinex, "plugins"), Path.Combine(_bepinex, "scripts") }.Where(Directory.Exists))
                foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        JObject scan = Scan.Dll(dll, _search);
                        scan["path"] = dll;
                        result.Add(scan);
                    }
                    catch (Exception) { }
                }
            return result;
        }

        private static List<JObject> _shipped;
        private static string _shippedBuilt = "";

        private static List<JObject> Shipped()
        {
            if (_shipped != null) return _shipped;
            using (Stream s = typeof(Cli).Assembly.GetManifestResourceStream("library/shipped.json.gz"))
                _shipped = ReadShipped(s, out _shippedBuilt);
            return _shipped;
        }

        /// <summary>The library's patches (downloaded and shipped), leaving out installed mods (they are in the installed list).</summary>
        private static List<Use> Library(List<Use> installed) =>
            UsesFromLibrary(LibraryScans(Path.Combine(_library, "mods"), Shipped()), new HashSet<string>(installed.Select(u => u.Guid)));

        // ---- gameupdate ----

        private static string Safe(string s) => new string((s ?? "").Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray());

        private static JObject GameUpdate()
        {
            GameIndex g = GameIndex.Build(_managed);
            string dir = Path.Combine(_library, "game");
            string path = Path.Combine(dir, $"methods_{Safe(g.Version)}_{g.Mvid}.json.gz");
            if (!File.Exists(path)) GameIndex.Save(g.Snapshot(), path);
            string previous = Directory.GetFiles(dir, "methods_*.json.gz").Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (previous == null)
                return new JObject { ["game"] = g.Version, ["snapshot"] = path, ["note"] = "The first game version seen here: after the next game update, gameupdate compares the two." };
            JObject diff = GameIndex.Diff(GameIndex.Load(previous), GameIndex.Load(path));
            List<Use> installed = UsesFromInstalled(Installed());
            return UpdateImpact(diff, installed, Library(installed));
        }

        // ---- library ----

        private static JObject LibraryCommand(string[] a)
        {
            string mods = Path.Combine(_library, "mods");
            string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            string requestedFile = Path.Combine(_library, "requested.json");
            List<string> requested = File.Exists(requestedFile) ? JArray.Parse(File.ReadAllText(requestedFile)).Select(x => (string)x).Where(IsPackage).ToList() : new List<string>();
            void SaveRequested() { Directory.CreateDirectory(_library); File.WriteAllText(requestedFile, new JArray(requested.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)).ToString()); }
            var fetched = new JArray();

            if (sub == "get" || sub == "drop")
            {
                var names = a.Skip(2).SelectMany(x => x.Split(',')).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (names.Count == 0 || names.Any(n => !IsPackage(n)))
                    return Fail("name the mod as Thunderstore does, Namespace-Name (e.g. modkit library get Azumatt-AzuCraftyBoxes): its page is thunderstore.io/c/valheim/p/Namespace/Name");
                if (sub == "get")
                {
                    foreach (string n in names)
                    {
                        int dash = n.IndexOf('-');
                        JObject scan = Thunderstore.Fetch(mods, n.Substring(0, dash), n.Substring(dash + 1), 0, 0, _search, out long bytes);
                        fetched.Add($"{n} {scan["version"]}: {((JArray)scan["dlls"]).Sum(d => ((JArray)d["patches"]).Count)} patches, DLLs in {Path.Combine(mods, n, "dll")} ({bytes / 1048576} MB downloaded)");
                        requested.Add(n);
                    }
                }
                else
                {
                    requested.RemoveAll(r => names.Contains(r, StringComparer.OrdinalIgnoreCase));
                    foreach (string n in names) { string d = Inside(mods, n); if (Directory.Exists(d)) Directory.Delete(d, true); }
                    fetched.Add("dropped " + string.Join(", ", names.ToArray()));
                }
                SaveRequested();
                WriteIndex(null, requested);
                return new JObject { ["done"] = fetched };
            }

            if (sub == "update")
            {
                // Each mod is checked against Thunderstore's latest version: one it already has (same version) costs one small request,
                // a new version replaces the old one. A mod's scan.json is written last, so a run cut short starts that mod again next time.
                int top = a.Length > 2 && int.TryParse(a[2], out int n) ? n : 10;
                var also = AlsoKeep();
                var keep = new HashSet<string>(requested.Concat(also), StringComparer.OrdinalIgnoreCase);
                var queue = Thunderstore.Top(top).Select((l, i) => new { ns = (string)l["namespace"], name = (string)l["name"], rank = i + 1, downloads = (long?)l["download_count"] ?? 0 })
                    .Concat(keep.ToList().Where(k => k.IndexOf('-') > 0).Select(k => new { ns = k.Substring(0, k.IndexOf('-')), name = k.Substring(k.IndexOf('-') + 1), rank = 0, downloads = 0L }));
                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in queue)
                {
                    string full = m.ns + "-" + m.name;
                    if (!done.Add(full)) continue;
                    keep.Add(full);
                    string line;
                    try
                    {
                        JObject scan = Thunderstore.Fetch(mods, m.ns, m.name, m.rank, m.downloads, _search, out long bytes);
                        line = $"{(m.rank > 0 ? "#" + m.rank : "kept")} {full} {scan["version"]}" + (bytes > 0 ? $": new, {bytes / 1048576} MB downloaded" : ": up to date");
                    }
                    catch (Exception e) { line = $"{(m.rank > 0 ? "#" + m.rank : "kept")} {full}: {e.GetBaseException().Message} (kept what was there; run again to retry)"; }
                    fetched.Add(line);
                    Console.Error.WriteLine(line); // (progress as it goes; the answer itself is printed at the end)
                }
                if (Directory.Exists(mods))
                    foreach (string dir in Directory.GetDirectories(mods).Where(d => !keep.Contains(Path.GetFileName(d)))) Directory.Delete(dir, true);
                WriteIndex(top, requested);
                return new JObject { ["fetched"] = fetched, ["library"] = _library };
            }

            JObject index = ReadJson(Path.Combine(_library, "index.json"));
            if (index == null) { WriteIndex(null, requested); index = ReadJson(Path.Combine(_library, "index.json")); }
            return new JObject
            {
                ["folder"] = _library, ["updated"] = index?["updated"], ["requested"] = new JArray(requested),
                ["mods"] = index?["mods"] is JArray list ? new JArray(list.Select(m => $"#{m["rank"]} {m["package"]} {m["version"]}: {m["patches"]} patches ({m["source"]})")) : null,
            };
        }

        /// <summary>The game's AlsoKeep setting (mods to keep whatever their rank), from Claude Tools' config file.</summary>
        private static List<string> AlsoKeep()
        {
            string cfg = new[] { "com.quad.claudetools.cfg", "com.dhack.claudetools.cfg" } // (its id before 2026-10)
                .Select(f => Path.Combine(_bepinex, "config", f)).FirstOrDefault(File.Exists);
            if (cfg == null) return new List<string>();
            string line = File.ReadAllLines(cfg).FirstOrDefault(l => l.TrimStart().StartsWith("AlsoKeep", StringComparison.Ordinal) && l.Contains("="));
            if (line == null) return new List<string>();
            return line.Substring(line.IndexOf('=') + 1).Split(',').Select(x => x.Trim()).Where(IsPackage).ToList();
        }

        /// <summary>library/index.json and patchmap.json, as the game writes them (keeping its settings when it wrote them first).</summary>
        private static void WriteIndex(int? top, List<string> requested)
        {
            Directory.CreateDirectory(_library);
            JObject old = ReadJson(Path.Combine(_library, "index.json")) ?? new JObject();
            JArray mods = BuildIndex(LibraryScans(Path.Combine(_library, "mods"), Shipped()), out JObject patchmap);
            old["updated"] = DateTime.Now.ToString("s");
            if (top.HasValue) { old["top"] = top.Value; old["downloaded"] = true; }
            old["shipped"] = _shippedBuilt;
            old["requested"] = new JArray(requested.Distinct(StringComparer.OrdinalIgnoreCase));
            old["mods"] = mods;
            File.WriteAllText(Path.Combine(_library, "index.json"), old.ToString());
            File.WriteAllText(Path.Combine(_library, "patchmap.json"), patchmap.ToString());
        }
    }
}
