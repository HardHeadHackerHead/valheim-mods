using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// "modkit package": getting a mod ready for Thunderstore. It writes and keeps up to date the mod's thunderstore.toml (the Thunderstore
    /// CLI's own project file, so `tcli build` and `tcli publish` read it), and checks everything Thunderstore and players need before an
    /// upload: the manifest's rules, icon, README, dependencies (from the DLL's [BepInDependency], mapped to Thunderstore packages through the
    /// mod library), the version against what's already published, the AI disclosure Thunderstore asks for, and modcheck. It never uploads:
    /// that's `tcli publish`, with the team's token, after the player says so.
    ///
    ///   modkit package <mod folder> [--namespace Team] [--website URL] [--dll path] [--no-ai]
    ///   modkit package check <built .zip>
    /// </summary>
    internal static class Package
    {
        private const string BepInExPack = "denikson-BepInExPack_Valheim";
        private static readonly Regex NameRule = new Regex("^[A-Za-z0-9_]+$");
        private static readonly Regex VersionRule = new Regex(@"^\d+\.\d+\.\d+$");

        // Thunderstore's Valheim categories (thunderstore.io/api/experimental/community/valheim/category/), checked online when possible
        private static readonly string[] KnownCategories =
        {
            "mods", "tools", "libraries", "misc", "tweaks", "utility", "client-side", "server-side", "building", "crafting", "gear", "enemies",
            "npcs", "world-generation", "transportation", "vehicles", "pvp", "audio", "language", "skins", "modpacks", "ai-generated",
            "mistlands-update", "ashlands-update", "bog-witch-update", "deep-north-update", "hildirs-request-update",
        };

        private sealed class Findings
        {
            public readonly List<JObject> List = new List<JObject>();
            public void Add(string level, string what, string fix) => List.Add(new JObject { ["level"] = level, ["what"] = what, ["fix"] = fix });
            public JArray Sorted() => new JArray(List.OrderBy(f => Array.IndexOf(new[] { "problem", "warning", "note" }, (string)f["level"])));
            public int Count(string level) => List.Count(f => (string)f["level"] == level);
        }

        public static JObject Run(string[] a, string[] search, Func<string, JObject> checkDll, Func<IEnumerable<JObject>> libraryScans)
        {
            if (a.Length >= 3 && a[1].Equals("check", StringComparison.OrdinalIgnoreCase)) return CheckZip(Rest(a, 2), checkDll);
            string folder = a.Skip(1).FirstOrDefault(x => !x.StartsWith("--"));
            if (folder == null || !Directory.Exists(folder))
                throw new ArgumentException("modkit package <mod folder> [--namespace Team] [--website URL] [--dll path] [--no-ai]   or   modkit package check <built .zip>");
            folder = Path.GetFullPath(folder);
            string Opt(string name) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
            bool ai = !a.Contains("--no-ai");
            var f = new Findings();

            // ---- the built DLL and what it says about itself ----
            string assembly = AssemblyName(folder);
            string dll = Opt("--dll") ?? FindBuilt(folder, assembly);
            if (dll == null || !File.Exists(dll))
                throw new ArgumentException($"no built {assembly}.dll in {folder}\\bin: build it first (dotnet build \"{folder}\" -c Release), or give --dll <path>");
            JObject scan = Scan.Dll(dll, search);
            JObject plugin = (scan["plugins"] as JArray)?.FirstOrDefault() as JObject;
            if (plugin == null) throw new ArgumentException($"{Path.GetFileName(dll)} has no [BepInPlugin] class");
            string version = (string)plugin["version"] ?? "";
            DateTime built = File.GetLastWriteTimeUtc(dll);
            string newer = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                                    .FirstOrDefault(p => File.GetLastWriteTimeUtc(p) > built);
            if (newer != null) f.Add("problem", $"{Path.GetFileName(newer)} changed after the DLL was built: the package would hold an old build.", $"Rebuild: dotnet build \"{folder}\" -c Release");

            // ---- thunderstore.toml: create it, or bring its version and dependencies up to date ----
            string tomlPath = Path.Combine(folder, "thunderstore.toml");
            var deps = Dependencies(plugin, libraryScans(), f);
            bool created = !File.Exists(tomlPath);
            Toml toml;
            if (created)
            {
                string ns = Opt("--namespace");
                if (string.IsNullOrEmpty(ns))
                    throw new ArgumentException("first time for this mod: say which Thunderstore team publishes it, --namespace <Team> (make one at https://thunderstore.io/teams/; " +
                                                "the name can't be changed later and becomes part of every dependency string)");
                toml = New(ns, PackageName((string)plugin["name"]), version, Description(folder, f), Opt("--website") ?? "", assembly, dll, folder, ai);
            }
            else
            {
                toml = Toml.Read(tomlPath);
                if (Opt("--namespace") != null) toml.Set("package", "namespace", Opt("--namespace"));
                if (Opt("--website") != null) toml.Set("package", "websiteUrl", Opt("--website"));
            }
            toml.Set("package", "versionNumber", version);
            foreach (var d in deps) toml.Set("package.dependencies", d.Key, d.Value);
            // the package's README and CHANGELOG, made again each time (in the build folder) when the toml uses the made ones
            string genDir = Path.Combine(folder, Generated);
            if ((toml.Get("build", "readme") ?? "").Replace('\\', '/').Contains(Generated + "/")) WriteReadme(folder, Path.Combine(genDir, "README.md"), f);
            string changelog = WriteChangelog(folder, version, genDir);
            if (changelog != null && !toml.HasCopyTarget("CHANGELOG.md")) toml.AddCopy(changelog, "CHANGELOG.md");
            File.WriteAllText(tomlPath, toml.Write(), new UTF8Encoding(false));

            string nsName = toml.Get("package", "namespace") ?? "", name = toml.Get("package", "name") ?? "", desc = toml.Get("package", "description") ?? "";
            string full = $"{nsName}-{name}-{version}";

            // ---- the manifest's rules ----
            if (!NameRule.IsMatch(nsName)) f.Add("problem", $"Team (namespace) \"{nsName}\" isn't a valid Thunderstore team name.", "Letters, digits and underscores only: set namespace in thunderstore.toml.");
            if (!NameRule.IsMatch(name) || name.Length > 128) f.Add("problem", $"Package name \"{name}\" breaks Thunderstore's rule (letters, digits, _; at most 128).", "Change name in thunderstore.toml. Never rename after the first upload: a new name is a new package.");
            if (!VersionRule.IsMatch(version)) f.Add("problem", $"Version \"{version}\" isn't Major.Minor.Patch.", "Set the plugin's Version to three numbers, e.g. 1.0.0, and rebuild.");
            if (desc.Length == 0 || desc.Length > 250 || desc.StartsWith("TODO")) f.Add("problem", $"The description is {(desc.Length == 0 || desc.StartsWith("TODO") ? "missing" : desc.Length + " characters (250 at most)")}.", "One line saying what the mod does: description in thunderstore.toml.");
            string web = toml.Get("package", "websiteUrl") ?? "";
            if (web.Length > 0 && !web.StartsWith("https://") && !web.StartsWith("http://")) f.Add("problem", $"websiteUrl \"{web}\" isn't a web address.", "A link to the source (GitHub) or leave it empty.");
            if (web.Length == 0) f.Add("note", "No websiteUrl: players can't find the source or report bugs.", "Set websiteUrl to the mod's GitHub page.");

            // ---- files ----
            string Rel(string key, string fallback) => Path.GetFullPath(Path.Combine(folder, toml.Get("build", key) ?? fallback));
            CheckIcon(Rel("icon", "./icon.png"), f);
            CheckReadme(Rel("readme", "./README.md"), ai, f);
            if (changelog == null)
                f.Add("note", "No CHANGELOG.md or CHANGELOG.txt (optional, but where players look when an update breaks something).",
                      "Add CHANGELOG.md beside the csproj: a heading per version (## 1.0.1), newest first, a line per change.");
            else if (changelog.Contains(Generated) && !Regex.IsMatch(File.ReadAllText(Path.Combine(folder, "CHANGELOG.txt")), @"(^|\n)\s*" + Regex.Escape(version) + ":"))
                f.Add("note", $"CHANGELOG.txt has no \"{version}:\" entry, so its newest paragraph is listed under {version}.", $"Start each release's paragraph with \"{version}: \" so the versions stay apart.");

            // ---- AI: Thunderstore asks for it to be said ----
            JArray cats = toml.GetArray("publish.categories", "valheim");
            if (ai)
            {
                if (!cats.Any(c => (string)c == "ai-generated"))
                    f.Add("warning", "Made with AI, but not in Thunderstore's \"ai-generated\" category (its rules ask for it).", "Add \"ai-generated\" to [publish.categories] valheim in thunderstore.toml.");
                if (!HasAiMetadata(dll))
                    f.Add("warning", "The DLL doesn't say it was made with AI (Thunderstore asks for AssemblyMetadata, or the mod may be removed).",
                          "In any .cs file: [assembly: System.Reflection.AssemblyMetadata(\"AI_Assisted_Creation\", \"This assembly was partially or fully created with the assistance of Generative AI.\")] " +
                          "and [assembly: System.Reflection.AssemblyMetadata(\"AI_Model_Vendor\", \"Anthropic\")], then rebuild. (The csproj form <AssemblyMetadata Include=...> does nothing while GenerateAssemblyInfo is false.)");
            }
            foreach (JToken c in cats.Where(c => !KnownCategories.Contains((string)c)))
                f.Add("problem", $"\"{c}\" isn't one of Thunderstore's Valheim categories.", "Use: " + string.Join(", ", KnownCategories));

            // ---- what's already published ----
            JObject online = Published(nsName, name, out string offline);
            if (online != null)
            {
                var versions = (online["versions"] as JArray ?? new JArray()).Select(v => (string)v["version_number"]).Where(v => v != null).ToList();
                if (versions.Contains(version)) f.Add("problem", $"{full} is already published: a version can't be replaced.", "Raise the plugin's Version, add the change to the changelog, rebuild.");
                else if (versions.Any(v => Newer(v, version))) f.Add("problem", $"{version} is lower than the published {versions.First(v => Newer(v, version))}.", "Raise the plugin's Version above every published one.");
            }
            else if (offline != null && offline.StartsWith("404")) f.Add("note", $"{nsName}-{name} isn't on Thunderstore yet: this is its first upload.", "Check the name is the one you want to keep: renaming later makes a new package.");
            else if (offline != null) f.Add("note", "Couldn't ask Thunderstore what's published: " + offline, "Check the version by hand on the package's page.");
            foreach (var d in toml.Keys("package.dependencies"))
            {
                string[] parts = d.Split('-');
                if (parts.Length != 2) { f.Add("problem", $"Dependency \"{d}\" isn't Team-Package.", "Write it as Team-Package = \"version\"."); continue; }
                if (Published(parts[0], parts[1], out string err) == null && err != null && err.Contains("404"))
                    f.Add("problem", $"Dependency {d} isn't on Thunderstore.", "Check the team and package name on its Thunderstore page.");
            }

            // ---- the code ----
            JObject check = checkDll(dll);
            int problems = (int?)check["problems"] ?? 0, warnings = (int?)check["warnings"] ?? 0;
            if (problems > 0) f.Add("problem", $"modcheck finds {problems} problem(s) in {Path.GetFileName(dll)}.", $"Run modkit modcheck \"{dll}\" and fix them (valheim-prerelease).");
            else if (warnings > 0) f.Add("note", $"modcheck finds {warnings} warning(s).", $"Read them (modkit modcheck \"{dll}\"): fix each, or know why it's fine.");
            string git = GitChanges(folder);
            if (git != null) f.Add("warning", "Changes not committed in this mod's folder: " + git, "Commit or stash them, rebuild, then package (the build includes whatever is in the folder).");

            string zip = Path.Combine(Path.GetFullPath(Path.Combine(folder, toml.Get("build", "outdir") ?? "./thunderstore-build")), $"{nsName}-{name}-{version}.zip");
            return new JObject
            {
                ["package"] = full, ["toml"] = tomlPath, ["toml was"] = created ? "created: read it and fill in what's marked" : "updated (version, dependencies)",
                ["dll"] = dll, ["dependencies"] = new JArray(toml.Keys("package.dependencies").Select(k => $"{k}-{toml.Get("package.dependencies", k)}")),
                ["categories"] = cats, ["problems"] = f.Count("problem"), ["warnings"] = f.Count("warning"), ["findings"] = f.Sorted(),
                ["tcli"] = Tcli(),
                ["next"] = f.Count("problem") > 0 ? new JArray("fix the problems, then run modkit package again") : new JArray(
                    $"tcli build --config-path \"{tomlPath}\"",
                    $"modkit package check \"{zip}\"",
                    "test that zip: r2modman (or Thunderstore Mod Manager) > a new, empty profile > Settings > Import local mod > start the game, use the mod, read the log",
                    "ask the player before publishing; then, with the team's service account token in TCLI_AUTH_TOKEN:",
                    $"tcli publish --config-path \"{tomlPath}\" --file \"{zip}\""),
            };
        }

        // ---- the built zip ----

        private static JObject CheckZip(string zipPath, Func<string, JObject> checkDll)
        {
            zipPath = zipPath.Trim('"');
            if (!File.Exists(zipPath)) throw new ArgumentException($"no file {zipPath} (tcli build writes it to the toml's outdir)");
            var f = new Findings();
            string manifestText = null, dllTemp = null;
            var names = new List<string>();
            using (var z = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read))
            {
                names = z.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
                foreach (string must in new[] { "manifest.json", "icon.png", "README.md" })
                    if (!names.Contains(must)) f.Add("problem", $"No {must} at the root of the zip.", "Thunderstore needs manifest.json, icon.png and README.md at the root (not inside a folder).");
                ZipArchiveEntry m = z.GetEntry("manifest.json");
                if (m != null) using (var r = new StreamReader(m.Open(), Encoding.UTF8)) manifestText = r.ReadToEnd();
                ZipArchiveEntry icon = z.GetEntry("icon.png");
                if (icon != null)
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "modkit-icon-" + Guid.NewGuid().ToString("N") + ".png");
                    icon.ExtractToFile(tmp); CheckIcon(tmp, f); File.Delete(tmp);
                }
                var dlls = z.Entries.Where(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
                if (dlls.Count == 0) f.Add("problem", "No .dll in the zip.", "Add the mod's DLL with a [[build.copy]] entry (target \"plugins/YourMod.dll\").");
                foreach (var d in dlls.Where(d => !d.FullName.Replace('\\', '/').StartsWith("plugins/")))
                    f.Add("warning", $"{d.FullName} isn't under plugins/: mod managers may put it in the wrong place.", "Copy it to plugins/ (target \"plugins/YourMod.dll\").");
                if (dlls.Count > 0)
                {
                    dllTemp = Path.Combine(Path.GetTempPath(), "modkit-" + Guid.NewGuid().ToString("N") + ".dll");
                    dlls[0].ExtractToFile(dllTemp);
                }
            }
            JObject manifest = null;
            try { manifest = manifestText != null ? JObject.Parse(manifestText) : null; }
            catch (Exception e) { f.Add("problem", "manifest.json isn't valid JSON: " + e.Message, "Rebuild with tcli build."); }
            if (manifest != null)
            {
                string name = (string)manifest["name"] ?? "", ver = (string)manifest["version_number"] ?? "", desc = (string)manifest["description"] ?? "";
                if (!NameRule.IsMatch(name)) f.Add("problem", $"name \"{name}\" breaks Thunderstore's rule.", "Letters, digits and underscores only.");
                if (!VersionRule.IsMatch(ver)) f.Add("problem", $"version_number \"{ver}\" isn't Major.Minor.Patch.", "Three numbers, e.g. 1.0.0.");
                if (desc.Length == 0 || desc.Length > 250) f.Add("problem", $"description is {desc.Length} characters.", "1 to 250.");
                if (manifest["dependencies"] is JArray deps && !deps.Any(d => ((string)d).StartsWith(BepInExPack + "-")))
                    f.Add("warning", "The dependencies don't include BepInExPack_Valheim.", $"Add {BepInExPack} to [package.dependencies].");
            }
            JObject check = null;
            if (dllTemp != null)
            {
                try { check = checkDll(dllTemp); } finally { try { File.Delete(dllTemp); } catch (Exception) { } }
                if (((int?)check["problems"] ?? 0) > 0) f.Add("problem", $"modcheck finds {check["problems"]} problem(s) in the packaged DLL.", "Fix them before uploading.");
                Match dv = Regex.Match((check["plugins"] as JArray)?.FirstOrDefault()?.ToString() ?? "", @"(\d+\.\d+(?:\.\d+)*)\s*\(");
                string dllVersion = dv.Success ? dv.Groups[1].Value : null;
                if (manifest != null && dllVersion != null && dllVersion != (string)manifest["version_number"])
                    f.Add("problem", $"The DLL says version {dllVersion}, the manifest {(string)manifest["version_number"]}.", "Run modkit package on the mod folder, then tcli build again.");
            }
            return new JObject
            {
                ["zip"] = zipPath, ["files"] = new JArray(names), ["problems"] = f.Count("problem"), ["warnings"] = f.Count("warning"), ["findings"] = f.Sorted(),
                ["next"] = f.Count("problem") > 0 ? "fix the problems and build again"
                         : "test this zip on a clean mod manager profile (Import local mod), then ask the player before tcli publish",
            };
        }

        // ---- pieces ----

        private static string Rest(string[] a, int from) => string.Join(" ", a.Skip(from).ToArray());

        private static string AssemblyName(string folder)
        {
            string csproj = Directory.GetFiles(folder, "*.csproj").FirstOrDefault();
            if (csproj == null) throw new ArgumentException($"no .csproj in {folder}: give the mod's project folder");
            Match m = Regex.Match(File.ReadAllText(csproj), "<AssemblyName>([^<]+)</AssemblyName>");
            return m.Success ? m.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(csproj);
        }

        private static string FindBuilt(string folder, string assembly)
        {
            string bin = Path.Combine(folder, "bin");
            if (!Directory.Exists(bin)) return null;
            return Directory.GetFiles(bin, assembly + ".dll", SearchOption.AllDirectories)
                            .OrderByDescending(p => p.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar))
                            .ThenByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        /// <summary>A package name from the plugin's name: letters, digits and underscores ("Quad's Cigars" becomes "Quads_Cigars").</summary>
        private static string PackageName(string plugin)
        {
            string s = Regex.Replace(Regex.Replace(plugin ?? "Mod", @"[\s\-\.]+", "_"), "[^A-Za-z0-9_]", "");
            return s.Length > 0 ? s : "Mod";
        }

        private static string Description(string folder, Findings f)
        {
            string file = Path.Combine(folder, "DESCRIPTION.txt");
            if (!File.Exists(file)) return "TODO: one line saying what the mod does (250 characters at most)";
            string first = File.ReadAllText(file).Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Replace("\r", " ").Replace("\n", " ").Trim() ?? "";
            if (first.Length <= 250) return first;
            string cut = first.Substring(0, 247);
            int end = Math.Max(cut.LastIndexOf(". "), cut.LastIndexOf(": "));
            f.Add("warning", "DESCRIPTION.txt's first paragraph is longer than Thunderstore allows (250): it was cut.", "Read description in thunderstore.toml and write one good line.");
            return end > 100 ? cut.Substring(0, end + 1) : cut + "...";
        }

        /// <summary>
        /// Thunderstore dependencies from the DLL: BepInExPack always, and each hard [BepInDependency] mapped to its Thunderstore package through
        /// the mod library (popular mods' plugin GUIDs). Soft ones are left out (the mod works without them).
        /// </summary>
        private static List<KeyValuePair<string, string>> Dependencies(JObject plugin, IEnumerable<JObject> library, Findings f)
        {
            var result = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(BepInExPack, PackVersion()) };
            var byGuid = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject mod in library)
            {
                string package = (string)mod["package"], ver = (string)mod["version"];
                if (package == null) continue;
                foreach (JToken g in mod.SelectTokens("$..plugins[*]"))
                {
                    string guid = g.Type == JTokenType.String ? (string)g : (string)g["guid"];
                    if (guid != null && !byGuid.ContainsKey(guid)) byGuid[guid] = new KeyValuePair<string, string>(package, ver);
                }
            }
            foreach (JToken d in plugin["dependencies"] as JArray ?? new JArray())
            {
                string guid = (string)d["guid"];
                if ((bool?)d["soft"] == true || guid == null) continue;
                if (byGuid.TryGetValue(guid, out var pkg) && !pkg.Key.Equals(BepInExPack, StringComparison.OrdinalIgnoreCase))
                    result.Add(new KeyValuePair<string, string>(pkg.Key, pkg.Value ?? "1.0.0"));
                else if (!byGuid.ContainsKey(guid))
                    f.Add("problem", $"It needs {guid} ([BepInDependency]), and the library doesn't know which Thunderstore package that is.",
                          "Find the package on thunderstore.io/c/valheim (or modkit library get <Team-Name>), and add it to [package.dependencies] as Team-Name = \"version\".");
            }
            return result;
        }

        private static string PackVersion()
        {
            try { return (string)Thunderstore.Package("denikson", "BepInExPack_Valheim")["latest"]["version_number"]; }
            catch (Exception) { return "5.4.2351"; }
        }

        private static JObject Published(string ns, string name, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(ns) || string.IsNullOrEmpty(name)) return null;
            try { return Thunderstore.Package(ns, name); }
            catch (Exception e) { error = e.GetBaseException().Message; if (error.Contains("404") || error.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0) error = "404 (not published yet)"; return null; }
        }

        private static bool Newer(string a, string b)
        {
            int[] x = a.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray(), y = b.Split('.').Select(s => int.TryParse(s, out int n) ? n : 0).ToArray();
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        private static void CheckIcon(string path, Findings f)
        {
            if (!File.Exists(path)) { f.Add("problem", "No icon.png.", "A 256 x 256 PNG beside the csproj: with the game running, `render <your prefab> size=256x256 bg=dark` makes one from your mod's piece or item."); return; }
            byte[] b = File.ReadAllBytes(path);
            bool png = b.Length > 24 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';
            int w = png ? (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19] : 0, h = png ? (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23] : 0;
            if (!png) f.Add("problem", "icon.png isn't a PNG file.", "Save it as PNG, 256 x 256.");
            else if (w != 256 || h != 256) f.Add("problem", $"icon.png is {w} x {h}; Thunderstore needs exactly 256 x 256.", "Resize it (or `render <prefab> size=256x256` in the game).");
        }

        private static void CheckReadme(string path, bool ai, Findings f)
        {
            if (!File.Exists(path)) { f.Add("problem", "No README.md.", "Write one: what the mod does (first two lines), what it adds, settings, keys, known clashes, a screenshot."); return; }
            byte[] b = File.ReadAllBytes(path);
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) f.Add("warning", "README.md starts with a byte-order mark.", "Save it as UTF-8 without BOM.");
            string text;
            try { text = new UTF8Encoding(false, true).GetString(b); }
            catch (Exception) { f.Add("problem", "README.md isn't UTF-8.", "Save it as UTF-8."); return; }
            if (text.Trim().Length < 200) f.Add("warning", "README.md is very short.", "Say what it does, what it adds, its settings and keys, known clashes; add a screenshot.");
            var relative = Regex.Matches(text, @"!\[[^\]]*\]\((?!https?://)([^)\s]+)[^)]*\)").Cast<Match>()
                .Concat(Regex.Matches(text, @"<img[^>]*\ssrc\s*=\s*[""'](?!https?://)([^""']+)[""']", RegexOptions.IgnoreCase).Cast<Match>()).ToList();
            if (relative.Count > 3) f.Add("problem", $"README.md shows {relative.Count} images by relative paths: on Thunderstore they all show broken.",
                                         "Link each from where it's public: https://raw.githubusercontent.com/<you>/<repo>/main/<path>.");
            else foreach (Match m in relative)
                f.Add("problem", $"README.md shows an image by a relative path ({m.Groups[1].Value}): Thunderstore doesn't serve files from the package, so it shows broken.",
                      "Link it from where it's public: https://raw.githubusercontent.com/<you>/<repo>/main/<path>.");
            if (!Regex.IsMatch(text, @"\bclaude\b|\bAI\b|\bgenerative\b|\bLLM\b|AI[- ]assisted|AI[- ]generated", RegexOptions.IgnoreCase))
                f.Add(ai ? "warning" : "note", "README.md doesn't say whether AI was used to make the mod.",
                      ai ? "Thunderstore asks for it: a line such as \"Made with the help of Claude (Anthropic).\"" : "Fine if no AI was used.");
            if (!Regex.IsMatch(text, @"(?i)clash|incompatib|compatib|conflict")) f.Add("note", "README.md doesn't mention other mods (known clashes or compatibility).", "A \"Known clashes\" section, even if it says none are known.");
        }

        private static bool HasAiMetadata(string dll)
        {
            try
            {
                using (var asm = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(File.ReadAllBytes(dll))))
                    return asm.CustomAttributes.Any(a => a.AttributeType.Name == "AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2
                                                         && ((string)a.ConstructorArguments[0].Value ?? "").StartsWith("AI_", StringComparison.Ordinal));
            }
            catch (Exception) { return false; }
        }

        private static string GitChanges(string folder)
        {
            string out_ = RunTool("git", $"-C \"{folder}\" status --porcelain -- .", out bool ok);
            if (!ok || string.IsNullOrWhiteSpace(out_)) return null;
            var lines = out_.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            return string.Join(", ", lines.Take(6)) + (lines.Count > 6 ? $" (+{lines.Count - 6} more)" : "");
        }

        /// <summary>Whether the Thunderstore CLI is here, and how to get it if not.</summary>
        private static JObject Tcli()
        {
            string v = RunTool("tcli", "--version", out bool ok);
            return ok ? new JObject { ["installed"] = v.Split('\n')[0].Trim() }
                      : new JObject { ["installed"] = false, ["install"] = "dotnet tool install -g tcli   (needs the .NET SDK, which builds mods anyway; then open a new terminal)" };
        }

        private static string RunTool(string exe, string args, out bool ok)
        {
            ok = false;
            try
            {
                var p = Process.Start(new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                ok = p.HasExited && p.ExitCode == 0;
                return o;
            }
            catch (Exception) { return ""; }
        }

        // ---- thunderstore.toml ----

        private static Toml New(string ns, string name, string version, string description, string website, string assembly, string dll, string folder, bool ai)
        {
            string dllRel = "./" + MakeRelative(folder, dll).Replace('\\', '/');
            var t = new Toml();
            t.Header = "# Thunderstore package for this mod, read by the Thunderstore CLI (tcli build, tcli publish). Made by Claude Tools' modkit package,\n" +
                       "# which keeps versionNumber and the dependencies up to date from the DLL; everything else is yours to edit.\n";
            t.Set("config", "schemaVersion", "0.0.1");
            t.Set("package", "namespace", ns);
            t.Set("package", "name", name);
            t.Set("package", "versionNumber", version);
            t.Set("package", "description", description);
            t.Set("package", "websiteUrl", website);
            t.SetRaw("package", "containsNsfwContent", "false");
            t.Section("package.dependencies");
            t.Set("build", "icon", IconPath(folder));
            t.Set("build", "readme", "./" + Generated + "/README.md"); // made from ./README.md by modkit package (links that work on Thunderstore)
            t.Set("build", "outdir", "./" + Generated);
            t.AddCopy(dllRel, $"plugins/{assembly}.dll");
            t.Set("publish", "repository", "https://thunderstore.io");
            t.SetRaw("publish", "communities", "[ \"valheim\" ]");
            t.SetRaw("publish.categories", "valheim", ai ? "[ \"mods\", \"ai-generated\" ]" : "[ \"mods\" ]");
            return t;
        }

        private const string Generated = "thunderstore-build";

        /// <summary>
        /// Where the package's icon lives: thunderstore/icon.png when it's there (or when the mod already embeds an icon.png of its own, such as
        /// a build-menu icon), else icon.png beside the csproj.
        /// </summary>
        private static string IconPath(string folder)
        {
            if (File.Exists(Path.Combine(folder, "thunderstore", "icon.png"))) return "./thunderstore/icon.png";
            string csproj = Directory.GetFiles(folder, "*.csproj").FirstOrDefault();
            bool embedded = csproj != null && Regex.IsMatch(File.ReadAllText(csproj), @"Include=""icon\.png""", RegexOptions.IgnoreCase);
            return embedded ? "./thunderstore/icon.png" : "./icon.png";
        }

        /// <summary>
        /// The package's README: the mod's README.md with relative links made absolute (Thunderstore shows only public https images, and a
        /// relative link goes nowhere): images to raw.githubusercontent.com, pages to github.com, from the git remote and branch. Images not
        /// on the remote yet are reported (push them before publishing).
        /// </summary>
        private static void WriteReadme(string folder, string to, Findings f)
        {
            string src = Path.Combine(folder, "README.md");
            if (!File.Exists(src)) return;
            string text = File.ReadAllText(src, Encoding.UTF8).TrimStart('\uFEFF');
            GitHub gh = GitHub.Of(folder);
            if (gh == null)
            {
                if (Regex.IsMatch(text, @"(!\[[^\]]*\]\((?!https?://)|<img[^>]*\ssrc\s*=\s*[""'](?!https?://))", RegexOptions.IgnoreCase))
                    f.Add("warning", "README.md links images by relative paths and the folder isn't a GitHub repository, so they can't be made absolute.",
                          "Host the images somewhere public and link them with https:// addresses.");
            }
            else
            {
                var unpushed = new List<string>();
                string Fix(string url, bool image)
                {
                    if (Regex.IsMatch(url, @"^(https?:|mailto:|#|data:)", RegexOptions.IgnoreCase)) return url;
                    int cut = url.IndexOfAny(new[] { '#', '?' });
                    string path = cut >= 0 ? url.Substring(0, cut) : url, tail = cut >= 0 ? url.Substring(cut) : "";
                    string repoPath = gh.Resolve(path);
                    if (repoPath == null) return url;
                    if (image && !gh.OnRemote(repoPath)) unpushed.Add(repoPath);
                    return (image ? gh.Raw : gh.Blob) + repoPath + tail;
                }
                text = Regex.Replace(text, @"(!?)\[([^\]]*)\]\(([^)\s]+)((?:\s+""[^""]*"")?)\)", m =>
                    $"{m.Groups[1].Value}[{m.Groups[2].Value}]({Fix(m.Groups[3].Value, m.Groups[1].Value == "!")}{m.Groups[4].Value})");
                text = Regex.Replace(text, @"(<img\b[^>]*?\ssrc\s*=\s*[""'])([^""']+)([""'])", m => m.Groups[1].Value + Fix(m.Groups[2].Value, true) + m.Groups[3].Value, RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"(<a\b[^>]*?\shref\s*=\s*[""'])([^""']+)([""'])", m => m.Groups[1].Value + Fix(m.Groups[2].Value, false) + m.Groups[3].Value, RegexOptions.IgnoreCase);
                if (unpushed.Count > 0)
                    f.Add("warning", $"README images not on {gh.Remote}/{gh.Branch} yet: {string.Join(", ", unpushed.Distinct().Take(5))}{(unpushed.Count > 5 ? " ..." : "")}. On Thunderstore they show broken until pushed.",
                          "Commit and push them before publishing.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.WriteAllText(to, text, new UTF8Encoding(false));
        }

        /// <summary>
        /// CHANGELOG.md for the package: the mod's own CHANGELOG.md, or one made from CHANGELOG.txt (paragraphs, newest first): a paragraph
        /// starting "1.2.0:" gets that version's heading, the newest without one gets the current version's, older ones go under "Earlier".
        /// </summary>
        private static string WriteChangelog(string folder, string version, string generatedDir)
        {
            if (File.Exists(Path.Combine(folder, "CHANGELOG.md"))) return "./CHANGELOG.md";
            string txt = Path.Combine(folder, "CHANGELOG.txt");
            if (!File.Exists(txt)) return null;
            var paras = Regex.Split(File.ReadAllText(txt, Encoding.UTF8).TrimStart('\uFEFF').Replace("\r\n", "\n"), @"\n\s*\n")
                             .Select(p => Regex.Replace(p.Trim(), @"\s*\n\s*", " ")).Where(p => p.Length > 0).ToList();
            var sb = new StringBuilder();
            bool earlier = false;
            for (int i = 0; i < paras.Count; i++)
            {
                Match v = Regex.Match(paras[i], @"^(\d+\.\d+(?:\.\d+)?):\s*(.*)$", RegexOptions.Singleline);
                if (v.Success) { sb.Append($"## {v.Groups[1].Value}\n\n- {v.Groups[2].Value}\n\n"); earlier = false; continue; }
                if (i == 0) { sb.Append($"## {version}\n\n- {paras[i]}\n\n"); continue; }
                if (!earlier) { sb.Append("## Earlier\n\n"); earlier = true; }
                sb.Append($"- {paras[i]}\n\n");
            }
            Directory.CreateDirectory(generatedDir);
            File.WriteAllText(Path.Combine(generatedDir, "CHANGELOG.md"), sb.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
            return "./" + Generated + "/CHANGELOG.md";
        }

        /// <summary>Where a folder sits on GitHub: owner/repo, branch, the folder's path in the repo.</summary>
        private sealed class GitHub
        {
            public string Remote, Branch, Prefix, Root, Raw, Blob;

            public static GitHub Of(string folder)
            {
                string url = Git(folder, "remote get-url origin");
                Match m = Regex.Match(url ?? "", @"github\.com[:/]([^/\s]+)/([^/\s]+?)(?:\.git)?\s*$");
                if (!m.Success) return null;
                string branch = Git(folder, "rev-parse --abbrev-ref HEAD")?.Trim();
                if (string.IsNullOrEmpty(branch) || branch == "HEAD") branch = "main";
                var g = new GitHub
                {
                    Remote = "origin", Branch = branch, Prefix = (Git(folder, "rev-parse --show-prefix") ?? "").Trim(),
                    Root = (Git(folder, "rev-parse --show-toplevel") ?? "").Trim(),
                };
                g.Raw = $"https://raw.githubusercontent.com/{m.Groups[1].Value}/{m.Groups[2].Value}/{branch}/";
                g.Blob = $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}/blob/{branch}/";
                return g;
            }

            /// <summary>A link relative to the folder, as a path from the repo's root (null if it climbs out of the repo).</summary>
            public string Resolve(string rel)
            {
                var parts = new List<string>(Prefix.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
                foreach (string p in Uri.UnescapeDataString(rel).Replace('\\', '/').Split('/'))
                {
                    if (p == "" || p == ".") continue;
                    if (p == "..") { if (parts.Count == 0) return null; parts.RemoveAt(parts.Count - 1); }
                    else parts.Add(p);
                }
                return string.Join("/", parts.Select(Uri.EscapeDataString));
            }

            public bool OnRemote(string repoPath) => Git(Root, $"cat-file -e {Remote}/{Branch}:{Uri.UnescapeDataString(repoPath)}") != null;

            private static string Git(string dir, string args)
            {
                string o = RunTool("git", $"-C \"{dir}\" {args}", out bool ok);
                return ok ? o : null;
            }
        }

        private static string MakeRelative(string folder, string file)
        {
            Uri from = new Uri(folder.TrimEnd('\\', '/') + Path.DirectorySeparatorChar), to = new Uri(file);
            return Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString());
        }

        /// <summary>
        /// Just enough TOML for thunderstore.toml: [sections], [[build.copy]] tables, and key = "string" | true/false | [ "array" ] lines.
        /// It keeps the order and anything it doesn't understand (comments, other keys) as written.
        /// </summary>
        private sealed class Toml
        {
            private sealed class Entry { public string Section, Key, Raw, Line; }
            private readonly List<Entry> _lines = new List<Entry>();
            public string Header = "";

            public static Toml Read(string path)
            {
                var t = new Toml();
                string section = "";
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("[[")) { section = line.Trim('[', ']', ' '); t._lines.Add(new Entry { Section = section, Line = raw }); continue; }
                    if (line.StartsWith("[")) { section = line.Trim('[', ']', ' '); t._lines.Add(new Entry { Section = section, Line = raw }); continue; }
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.StartsWith("#"))
                        t._lines.Add(new Entry { Section = section, Key = line.Substring(0, eq).Trim().Trim('"'), Raw = line.Substring(eq + 1).Trim() });
                    else t._lines.Add(new Entry { Section = section, Line = raw });
                }
                return t;
            }

            public string Get(string section, string key)
            {
                Entry e = _lines.FirstOrDefault(x => x.Key != null && x.Section == section && x.Key == key);
                if (e == null) return null;
                string r = e.Raw;
                return r.StartsWith("\"") && r.EndsWith("\"") && r.Length >= 2 ? r.Substring(1, r.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\") : r;
            }

            public JArray GetArray(string section, string key)
            {
                string r = Get(section, key) ?? "";
                return new JArray(Regex.Matches(r, "\"([^\"]*)\"").Cast<Match>().Select(m => m.Groups[1].Value));
            }

            public IEnumerable<string> Keys(string section) => _lines.Where(x => x.Key != null && x.Section == section).Select(x => x.Key).ToList();

            public void Set(string section, string key, string value) =>
                SetRaw(section, key, "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");

            public void SetRaw(string section, string key, string raw)
            {
                Entry e = _lines.FirstOrDefault(x => x.Key != null && x.Section == section && x.Key == key);
                if (e != null) { e.Raw = raw; return; }
                Section(section);
                int at = _lines.FindLastIndex(x => x.Section == section && (x.Key != null || x.Line.Trim().StartsWith("[")));
                _lines.Insert(at + 1, new Entry { Section = section, Key = key, Raw = raw });
            }

            public void Section(string section)
            {
                if (_lines.Any(x => x.Section == section && x.Key == null && x.Line.Trim() == "[" + section + "]")) return;
                if (_lines.Count > 0) _lines.Add(new Entry { Section = section, Line = "" });
                _lines.Add(new Entry { Section = section, Line = "[" + section + "]" });
            }

            public bool HasCopyTarget(string target) => _lines.Any(x => x.Section == "build.copy" && x.Key == "target" && x.Raw.Trim('"') == target);

            /// <summary>A [[build.copy]] table, right after the other build tables (tcli refuses one that comes after [publish]).</summary>
            public void AddCopy(string source, string target)
            {
                int at = _lines.FindLastIndex(x => x.Section == "build.copy" || x.Section == "build");
                at = at < 0 ? _lines.Count : at + 1;
                _lines.InsertRange(at, new[]
                {
                    new Entry { Section = "build.copy", Line = "" },
                    new Entry { Section = "build.copy", Line = "[[build.copy]]" },
                    new Entry { Section = "build.copy", Key = "source", Raw = "\"" + source + "\"" },
                    new Entry { Section = "build.copy", Key = "target", Raw = "\"" + target + "\"" },
                });
            }

            public string Write()
            {
                var sb = new StringBuilder(Header);
                foreach (Entry e in _lines)
                {
                    if (e.Key == null) { sb.Append(e.Line).Append('\n'); continue; }
                    string key = Regex.IsMatch(e.Key, "^[A-Za-z0-9_-]+$") ? e.Key : "\"" + e.Key + "\"";
                    sb.Append(key).Append(" = ").Append(e.Raw).Append('\n');
                }
                return sb.ToString();
            }
        }
    }
}
