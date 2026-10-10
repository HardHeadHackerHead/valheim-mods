using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// Makes mods/ClaudeTools/Shipped/patchmap.json.gz: for the top N Valheim mods on Thunderstore, their plugins and every patch (which game
    /// method, how), as ClaudeTools' own library would list them. Only facts about the mods go in it, none of their code. The mods' code-only
    /// DLLs are kept in tools/PatchMap/cache (not committed), so a rebuild only downloads mods that have a new version.
    /// </summary>
    internal static class Program
    {


        private static int Main(string[] args)
        {
            int top = args.Any(x => int.TryParse(x, out _)) ? int.Parse(args.First(x => int.TryParse(x, out _))) : 100;
            bool rescan = args.Contains("--rescan"); // scan the cached DLLs again (after the scanner improves) instead of reusing their scan.json
            string root = FindRoot();
            string cache = Path.Combine(root, "tools", "PatchMap", "cache");
            string output = Path.Combine(root, "mods", "ClaudeTools", "Shipped", "patchmap.json.gz");
            string valheim = Environment.GetEnvironmentVariable("VALHEIM_DIR") ?? "D:/SteamLibrary/steamapps/common/Valheim";
            string[] search = { Path.Combine(valheim, "BepInEx", "core"), Path.Combine(valheim, "valheim_Data", "Managed"), Path.Combine(valheim, "BepInEx", "plugins") };

            var mods = new JArray();
            long fetched = 0;
            int failed = 0;
            int rank = 0;
            foreach (JObject listed in Thunderstore.Top(top))
            {
                string ns = (string)listed["namespace"], name = (string)listed["name"], full = ns + "-" + name;
                rank++;
                // the version cached from an earlier run, if any: a rescan uses it without asking Thunderstore, and a failed check keeps it
                string modCache = Path.Combine(cache, full);
                string cachedVersion = Directory.Exists(modCache)
                    ? Directory.GetDirectories(modCache).Select(Path.GetFileName).FirstOrDefault(v => File.Exists(Path.Combine(modCache, v, "scan.json"))) : null;
                long listedDownloads = (long?)listed["download_count"] ?? 0;
                try
                {
                    JObject info = rescan && cachedVersion != null ? null : Thunderstore.Package(ns, name);
                    string version = info != null ? (string)info["latest"]["version_number"] : cachedVersion;
                    string dir = Path.Combine(cache, full, version);
                    string scanFile = Path.Combine(dir, "scan.json");
                    JArray dlls;
                    var cached = File.Exists(scanFile) ? JObject.Parse(File.ReadAllText(scanFile))["dlls"] as JArray : null;
                    if (cached != null && !rescan) dlls = cached;
                    else if (cached != null)
                    {
                        dlls = new JArray();
                        foreach (string path in Directory.GetFiles(dir, "*.dll"))
                        {
                            JObject scan = Scan.Dll(path, search);
                            scan["inZip"] = cached.FirstOrDefault(d => (string)d["dll"] == Path.GetFileName(path))?["inZip"] ?? Path.GetFileName(path);
                            dlls.Add(scan);
                        }
                        File.WriteAllText(scanFile, new JObject { ["dlls"] = dlls }.ToString());
                    }
                    else
                    {
                        // a new version: fetch it into a fresh folder, and only then remove the old one (a failed download keeps the old)
                        string fresh = dir + ".new";
                        if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
                        Directory.CreateDirectory(fresh);
                        dlls = new JArray();
                        foreach (var kv in Thunderstore.Dlls((string)info["latest"]["download_url"], ref fetched))
                        {
                            string path = Path.Combine(fresh, Path.GetFileName(kv.Key));
                            for (int k = 2; File.Exists(path); k++) path = Path.Combine(fresh, Path.GetFileNameWithoutExtension(kv.Key) + "_" + k + ".dll");
                            if (!Scan.Slim(kv.Value, path, search)) continue;
                            JObject scan = Scan.Dll(path, search);
                            scan["dll"] = Path.GetFileName(path);
                            scan["inZip"] = kv.Key;
                            dlls.Add(scan);
                        }
                        File.WriteAllText(Path.Combine(fresh, "scan.json"), new JObject { ["dlls"] = dlls }.ToString());
                        if (Directory.Exists(modCache)) foreach (string old in Directory.GetDirectories(modCache).Where(d => d != fresh)) Directory.Delete(old, true);
                        Directory.Move(fresh, dir);
                    }
                    mods.Add(Entry(full, version, rank, listedDownloads, ns, name, dlls));
                    Console.WriteLine($"#{rank} {full} {version}: {dlls.Sum(d => (d["patches"] as JArray)?.Count ?? 0)} patches");
                }
                catch (Exception e)
                {
                    string why = e.GetBaseException().Message;
                    if (cachedVersion != null)
                    {
                        var dlls = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(modCache, cachedVersion, "scan.json")))["dlls"];
                        mods.Add(Entry(full, cachedVersion, rank, listedDownloads, ns, name, dlls));
                        Console.WriteLine($"#{rank} {full}: {why} (kept the cached scan of {cachedVersion})");
                    }
                    else { failed++; Console.WriteLine($"#{rank} {full}: {why} (left out: run again to retry)"); }
                }
            }

            var map = new JObject
            {
                ["built"] = DateTime.UtcNow.ToString("s") + "Z", ["top"] = top, ["source"] = "thunderstore.io, Valheim, most downloaded",
                ["note"] = "What each mod patches in the game, read from its DLL. No code from the mods is included.",
                ["mods"] = mods,
            };
            if (failed > Math.Max(1, top / 10))
            {
                Console.Error.WriteLine($"{failed} mods could not be read (see above): the shipped map was NOT changed. Run again later.");
                return 1;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (FileStream file = File.Create(output))
            using (var gz = new GZipStream(file, CompressionLevel.Optimal))
            {
                byte[] json = Encoding.UTF8.GetBytes(map.ToString(Formatting.None));
                gz.Write(json, 0, json.Length);
            }
            Console.WriteLine($"{mods.Count} mods, {mods.Sum(m => ((JArray)m["dlls"]).Sum(d => ((JArray)d["patches"]).Count))} patches -> {output} " +
                              $"({new FileInfo(output).Length / 1024} KB); downloaded {fetched / 1048576} MB");
            return 0;
        }

        /// <summary>One mod in the library's own scan.json shape, so the game reads shipped and downloaded mods alike.</summary>
        private static JObject Entry(string full, string version, int rank, long downloads, string ns, string name, JArray dlls) => new JObject
        {
            ["package"] = full, ["version"] = version, ["rank"] = rank, ["downloads"] = downloads,
            ["page"] = $"https://thunderstore.io/c/valheim/p/{ns}/{name}/",
            ["dlls"] = new JArray(dlls.Select(d => new JObject
            {
                ["dll"] = d["dll"], ["assembly"] = d["assembly"], ["plugins"] = d["plugins"] ?? new JArray(), ["patches"] = d["patches"] ?? new JArray(),
            })),
        };

        /// <summary>The repo's root: the folder above tools/PatchMap, wherever the tool runs from.</summary>
        private static string FindRoot()
        {
            for (string dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, '/')))
                if (Directory.Exists(Path.Combine(dir, "mods", "ClaudeTools")) && Directory.Exists(Path.Combine(dir, "tools", "PatchMap"))) return dir;
            throw new DirectoryNotFoundException("run this from inside the valheim-mods repo");
        }
    }
}
