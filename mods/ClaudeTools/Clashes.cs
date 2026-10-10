using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using static ClaudeTools.Compat;

namespace ClaudeTools
{
    /// <summary>
    /// Which mods change the same parts of the game: the patches running in this game (Harmony's own list, so it is exact) and the mod
    /// library's, compared method by method and by game system (two mods can clash through different methods: one counts chest items in
    /// Inventory.CountItems, another in Player.HaveRequirementItems, and the chests are counted twice).
    ///
    /// Two mods patching the same method is normal and usually fine: postfixes stack. What makes a clash likely is a prefix that can skip the
    /// game's method (the other mods' changes to that method's body never run), two transpilers rewriting the same code, or two mods both
    /// changing the same result.
    /// </summary>
    public partial class Plugin
    {
        // ---- the patches running in this game ----

        private static readonly Dictionary<string, KeyValuePair<DateTime, JObject>> InstalledCache = new Dictionary<string, KeyValuePair<DateTime, JObject>>();

        /// <summary>Scan the installed mods' DLLs (for what Harmony's list can't say: whether a prefix can skip, MonoMod hooks). Cached by file time.</summary>
        private static List<JObject> ScanInstalled()
        {
            var result = new List<JObject>();
            string[] search = SearchDirs();
            foreach (string root in new[] { Paths.PluginPath, Path.Combine(Paths.BepInExRootPath, "scripts") }.Where(Directory.Exists))
            {
                foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
                {
                    try
                    {
                        DateTime when = File.GetLastWriteTimeUtc(dll);
                        lock (InstalledCache)
                        {
                            if (InstalledCache.TryGetValue(dll, out var hit) && hit.Key == when) { result.Add(hit.Value); continue; }
                        }
                        JObject scan = Scan.Dll(dll, search);
                        lock (InstalledCache) InstalledCache[dll] = new KeyValuePair<DateTime, JObject>(when, scan);
                        result.Add(scan);
                    }
                    catch (Exception) { }
                }
            }
            return result;
        }

        /// <summary>Run the installed scan on a worker thread, then hand back the patches running in this game.</summary>
        private static IEnumerator Installed(Action<List<Use>> done)
        {
            List<JObject> scans = null;
            var worker = new Thread(() => { try { scans = ScanInstalled(); } catch (Exception) { scans = new List<JObject>(); } }) { IsBackground = true };
            worker.Start();
            while (worker.IsAlive) yield return null;
            done(LiveUses(scans));
        }

        private static List<Use> LiveUses(List<JObject> scans)
        {
            var bySkip = new Dictionary<string, string>();
            foreach (JObject s in scans)
                foreach (JObject p in s["patches"] ?? new JArray())
                    if (p["skips"] != null) bySkip[(string)p["method"] + "|" + (string)p["target"]] = (string)p["skips"];

            var owners = new Dictionary<Assembly, PluginInfo>();
            foreach (PluginInfo pi in Chainloader.PluginInfos.Values) if (pi.Instance != null) owners[pi.Instance.GetType().Assembly] = pi;

            var uses = new List<Use>();
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                HarmonyLib.Patches info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                string target = TargetName(original);
                void Add(IEnumerable<Patch> list, string kind)
                {
                    foreach (Patch patch in list ?? Enumerable.Empty<Patch>())
                    {
                        MethodInfo pm = patch.PatchMethod;
                        if (pm == null) continue;
                        bool known = owners.TryGetValue(pm.DeclaringType.Assembly, out PluginInfo pi);
                        string method = TypeName(pm.DeclaringType) + "." + pm.Name;
                        ParameterInfo[] ps = pm.GetParameters();
                        string skips = null;
                        if (kind == "prefix")
                            skips = bySkip.TryGetValue(method + "|" + target, out string s) ? s
                                  : pm.ReturnType == typeof(bool) || ps.Any(x => x.Name == "__runOriginal") ? "sometimes" : "never";
                        uses.Add(new Use
                        {
                            Mod = known ? pi.Metadata.Name : patch.owner, Guid = known ? pi.Metadata.GUID : patch.owner, From = "installed",
                            Target = target, Kind = kind, Skips = skips, Method = method, Priority = patch.priority,
                            ChangesResult = ps.Any(x => x.Name == "__result" && x.ParameterType.IsByRef),
                            RunOriginal = ps.Any(x => x.Name == "__runOriginal"),
                            Args = "(" + string.Join(", ", original.GetParameters().Select(x => x.ParameterType.Name.TrimEnd('&')).ToArray()) + ")",
                            ChangesArgs = ps.Where(x => x.ParameterType.IsByRef && !x.Name.StartsWith("__")).Select(x => x.Name).ToArray(),
                        });
                    }
                }
                Add(info.Prefixes, "prefix");
                Add(info.Postfixes, "postfix");
                Add(info.Transpilers, "transpiler");
                Add(info.Finalizers, "finalizer");
                Add(info.ILManipulators, "ilmanipulator");
            }

            // MonoMod hooks are not in Harmony's list: take them from the scan, for the mods that are loaded
            var loaded = Chainloader.PluginInfos.Values.Where(p => p.Instance != null).ToDictionary(p => p.Metadata.GUID, p => p);
            foreach (JObject s in scans)
            {
                PluginInfo pi = (s["plugins"] ?? new JArray()).Select(p => (string)p["guid"]).Where(g => g != null && loaded.ContainsKey(g)).Select(g => loaded[g]).FirstOrDefault();
                if (pi == null) continue;
                foreach (JObject p in (s["patches"] ?? new JArray()).Where(p => ((string)p["kind"] ?? "").Contains("hook")))
                    uses.Add(FromScan(p, pi.Metadata.Name, pi.Metadata.GUID, "installed"));
            }
            return uses;
        }

        /// <summary>The library's patches, leaving out mods that are installed (those are in the live list).</summary>
        private static List<Use> LibraryUses() => UsesFromLibrary(LibraryScans(), new HashSet<string>(Chainloader.PluginInfos.Keys));

        /// <summary>A game method as "Type.Method"; an iterator's MoveNext as "Type.Method (enumerator)".</summary>
        private static string TargetName(MethodBase m)
        {
            Type t = m.DeclaringType;
            string name = m.Name;
            if (t != null && t.Name.StartsWith("<") && t.Name.Contains(">d__") && t.DeclaringType != null)
            {
                name = t.Name.Substring(1, t.Name.IndexOf('>') - 1) + " (enumerator)";
                t = t.DeclaringType;
            }
            return (t != null ? TypeName(t) : "?") + "." + name;
        }

        private static string TypeName(Type t)
        {
            string n = (t.FullName ?? t.Name).Replace('+', '.');
            int tick = n.IndexOf('`');
            return tick >= 0 ? n.Substring(0, tick) : n;
        }

        /// <summary>
        /// The clashes report, made on its own after each launch and library update: library/clashes.json, and one line in the log
        /// (so a player sees "1 likely clash" in LogOutput.log without asking).
        /// </summary>
        private IEnumerator ClashReport()
        {
            JObject report = null;
            List<Use> installed = null;
            yield return Installed(uses => installed = uses);
            if (installed == null) yield break;
            var skip = new HashSet<string>(Chainloader.PluginInfos.Keys); // (read here: BepInEx's list belongs to the main thread)
            yield return OnWorker(() =>
            {
                report = Compare(installed, UsesFromLibrary(LibraryScans(), skip), null, false);
                report["made"] = DateTime.Now.ToString("s");
                Directory.CreateDirectory(LibraryDir);
                File.WriteAllText(Path.Combine(LibraryDir, "clashes.json"), report.ToString());
            }, e => Logger.LogWarning("Could not make the clashes report: " + e.Message));
            if (report == null) yield break;
            int likely = (int?)report["likely"] ?? 0, check = (int?)report["check"] ?? 0;
            string worst = string.Join("; ", ((JArray)report["pairs"]).Where(p => (string)p["level"] == "likely").Take(3)
                .Select(p => $"{p["a"]} and {p["b"]}").ToArray());
            Logger.LogInfo($"Mod clashes: {likely} likely{(likely > 0 ? " (" + worst + ")" : "")}, {check} to check, against {report["libraryMods"]} library mods. " +
                           "Details: BepInEx/claude/library/clashes.json, or type claude clashes in the console (F5).");
        }

        // ---- commands ----

        private void RegisterLibraryCommands()
        {
            Builtin("library", "library [update | get <Namespace-Name> | drop <Namespace-Name> | clear]: the mod library (most-downloaded Thunderstore mods and what they patch): " +
                "what is in it; fetch or refresh it now; download one mod by its Thunderstore name (kept until dropped, even with DownloadMods off), or drop it",
                (a, output, error) =>
                {
                    string sub = a.Length > 1 ? a[1].ToLowerInvariant() : "";
                    if (sub == "get" || sub == "drop")
                    {
                        var names = a.Skip(2).SelectMany(x => x.Split(',')).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        if (names.Count == 0 || names.Any(n => !Compat.IsPackage(n)))
                        {
                            error("name the mod as Thunderstore does, Namespace-Name (e.g. library get Azumatt-AzuCraftyBoxes): its page is thunderstore.io/c/valheim/p/Namespace/Name");
                            return null;
                        }
                        List<string> list = Requested();
                        if (sub == "get")
                        {
                            SaveRequested(list.Concat(names));
                            bool started = StartLibraryUpdate(true, !_libraryOn.Value);
                            output(new JObject { ["getting"] = new JArray(names), ["update"] = started ? "started" : "an update is running: run library get again when it is done", ["note"] = "run library to follow it" });
                        }
                        else
                        {
                            if (Library.Running) { error("an update is running"); return null; }
                            SaveRequested(list.Where(x => !names.Contains(x, StringComparer.OrdinalIgnoreCase)));
                            foreach (string n in names)
                            {
                                string dir = Compat.Inside(LibraryMods, n);
                                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception e) { error($"{n}: {e.Message}"); }
                            }
                            output(new JObject { ["dropped"] = new JArray(names) });
                        }
                        return null;
                    }
                    if (sub == "update")
                    {
                        bool started = StartLibraryUpdate(true);
                        output(new JObject { ["update"] = started ? "started" : "already running", ["top"] = _libraryTop.Value, ["note"] = "run library again to follow it" });
                        return null;
                    }
                    if (sub == "clear")
                    {
                        if (Library.Running) { error("an update is running"); return null; }
                        try { if (Directory.Exists(LibraryDir)) Directory.Delete(LibraryDir, true); }
                        catch (Exception e) { error(e.Message); return null; }
                        output(new JObject { ["cleared"] = LibraryDir });
                        return null;
                    }
                    JObject index = ReadJson(Path.Combine(LibraryDir, "index.json"));
                    output(new JObject
                    {
                        ["on"] = _libraryOn.Value, ["top"] = _libraryTop.Value, ["folder"] = LibraryDir,
                        ["running"] = Library.Running, ["step"] = Library.Step, ["done"] = Library.Done, ["total"] = Library.Total, ["error"] = Library.LastError,
                        ["updated"] = index?["updated"], ["mods"] = index?["mods"] is JArray mods
                            ? new JArray(mods.Select(m => $"#{m["rank"]} {m["package"]} {m["version"]}: {m["patches"]} patches")) : null,
                        ["recent"] = new JArray(Library.Log.Skip(Math.Max(0, Library.Log.Count - 8))),
                    });
                    return null;
                });

            Builtin("patches", "patches [text]: every patch running in this game, by game method (only methods or mods containing the text)",
                (a, output, error) => Installed(uses =>
                {
                    string text = Rest(a, 1);
                    var shown = uses.Where(u => text.Length == 0 || u.Target.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || Matches(u, text))
                                    .GroupBy(u => u.Target).OrderBy(g => g.Key).ToList();
                    output(new JObject
                    {
                        ["methods"] = shown.Count, ["patches"] = shown.Sum(g => g.Count()),
                        ["list"] = new JArray(shown.Take(300).Select(g => new JObject
                        {
                            ["method"] = g.Key, ["system"] = SystemOf(g.Key), ["mods"] = g.Select(u => u.Mod).Distinct().Count(),
                            ["patches"] = new JArray(g.Select(u => u.Mod + ": " + u.Describe())),
                        })),
                    });
                }));

            Builtin("who", "who <Type.Method | text | system name>: which mods (installed, and in the library) patch a game method, or any method of a game system (\"systems\" lists them; \"who system crafting\" finds one by part of its name)",
                (a, output, error) =>
                {
                    string text = Rest(a, 1);
                    if (text.Length == 0) { error("say which method or system, e.g. who InventoryGui.UpdateRecipeList, or who crafting payment"); return null; }
                    return Installed(installed =>
                    {
                        string asked = text.StartsWith("system ", StringComparison.OrdinalIgnoreCase) ? text.Substring(7).Trim() : text;
                        string system = Systems.Select(s => s[0]).FirstOrDefault(s => s.Equals(asked, StringComparison.OrdinalIgnoreCase))
                                     ?? (asked != text ? Systems.Select(s => s[0]).FirstOrDefault(s => s.IndexOf(asked, StringComparison.OrdinalIgnoreCase) >= 0) : null);
                        bool Hit(Use u) => system != null ? SystemOf(u.Target) == system : u.Target.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                        var all = installed.Where(Hit).Concat(LibraryUses().Where(Hit)).GroupBy(u => u.Target).OrderBy(g => g.Key).ToList();
                        output(new JObject
                        {
                            ["looked for"] = system != null ? "system: " + system : text, ["methods"] = all.Count,
                            ["list"] = new JArray(all.Take(200).Select(g => new JObject
                            {
                                ["method"] = g.Key, ["system"] = SystemOf(g.Key),
                                ["installed"] = new JArray(g.Where(u => u.From == "installed").Select(u => u.Mod + ": " + u.Describe())),
                                ["library"] = new JArray(g.Where(u => u.From != "installed").Select(u => $"{u.Mod} ({u.From}): {u.Describe()}")),
                            })),
                        });
                    });
                });

            Builtin("clashes", "clashes [mod] [all]: installed mods that change the same methods or do the same job as each other or as library mods, most likely first",
                (a, output, error) =>
                {
                    bool all = a.Skip(1).Any(x => x.Equals("all", StringComparison.OrdinalIgnoreCase));
                    string only = string.Join(" ", a.Skip(1).Where(x => !x.Equals("all", StringComparison.OrdinalIgnoreCase)).ToArray());
                    return Installed(installed => output(Compare(installed, LibraryUses(), only.Length > 0 ? only : null, all)));
                });

            Builtin("systems", "systems: the game systems clashes and who group methods into (methods that do one job)", (a, output, error) =>
            {
                output(new JObject { ["systems"] = new JArray(Systems.Select(s => new JObject { ["system"] = s[0], ["methods"] = new JArray(s.Skip(1)) })) });
                return null;
            });
        }
    }
}
