using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using Mono.Cecil;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace ClaudeTools
{
    /// <summary>
    /// The mod library: the most-downloaded Valheim mods on Thunderstore, kept in BepInEx/claude/library so you (and an assistant) can see
    /// what they change in the game before writing a mod that changes the same things. Off until you switch it on.
    ///
    ///   * Only their DLLs are fetched, straight out of each zip (byte ranges, not the whole download), then their embedded assets are
    ///     stripped so a 180 MB content mod keeps a code-only DLL of under a megabyte. These DLLs are never loaded or run: they live outside
    ///     plugins and scripts, and are only read (here, and by decompilers like ilspycmd).
    ///   * Each mod's patches are listed in library/mods/&lt;Namespace-Name&gt;/scan.json, and all of them by game method in library/patchmap.json.
    ///   * It refreshes itself every few days (RefreshDays), and when you raise TopMods. Mods that drop out of the top are removed.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _libraryOn;
        private ConfigEntry<int> _libraryTop, _libraryDays;
        private ConfigEntry<string> _libraryExtra, _librarySkip;

        internal static string LibraryDir => Path.Combine(Folder, "library");
        internal static string LibraryMods => Path.Combine(LibraryDir, "mods");

        private const string Listing = "https://thunderstore.io/api/cyberstorm/listing/valheim/?ordering=most-downloaded&page=";
        private const string PackageApi = "https://thunderstore.io/api/experimental/package/";

        internal class LibraryState
        {
            public bool Running;
            public string Step = "", LastError;
            public int Done, Total;
            public DateTime Started;
            public readonly List<string> Log = new List<string>();
            public void Note(string line) { Log.Add(DateTime.Now.ToString("HH:mm:ss") + " " + line); if (Log.Count > 40) Log.RemoveAt(0); }
        }

        internal static readonly LibraryState Library = new LibraryState();
        private volatile bool _stopping;

        private void BindLibrary()
        {
            _libraryOn = Config.Bind("Library", "DownloadMods", false,
                "Keep a library of the most-downloaded Valheim mods on Thunderstore in BepInEx/claude/library: what each one patches in the game, " +
                "and a code-only copy of its DLL (never loaded or run). For seeing which mods change the same things as yours. Downloads only the DLLs.");
            _libraryTop = Config.Bind("Library", "TopMods", 10, new ConfigDescription(
                "How many of the most-downloaded mods to keep (mod managers, BepInEx and modpacks don't count).", new AcceptableValueRange<int>(1, 1000)));
            _libraryDays = Config.Bind("Library", "RefreshDays", 7, new ConfigDescription(
                "Check for new versions and a new top list after this many days.", new AcceptableValueRange<int>(1, 90)));
            _libraryExtra = Config.Bind("Library", "AlsoKeep", "",
                "More mods to keep whatever their rank, as Namespace-Name separated by commas (e.g. Azumatt-AzuCraftyBoxes, Vapok-AdventureBackpacks).");
            _librarySkip = Config.Bind("Library", "Skip", "ebkr-r2modman, denikson-BepInExPack_Valheim, Kesomannen-GaleModManager",
                "Packages that are not mods, left out of the top list (Namespace-Name, separated by commas).");
            _libraryOn.SettingChanged += (s, e) => { if (_libraryOn.Value) StartLibraryUpdate(false); };
            _libraryTop.SettingChanged += (s, e) => { if (_libraryOn.Value) StartLibraryUpdate(false); };
            StartCoroutine(LibraryWhenDue());
            StartCoroutine(GameSnapshot());
        }

        private IEnumerator LibraryWhenDue()
        {
            yield return new WaitForSecondsRealtime(20f); // (let the game finish starting)
            // the shipped map on disk too, for assistants to read (and again when this ClaudeTools ships a newer one), off the main thread
            int top = _libraryTop.Value;
            yield return OnWorker(() =>
            {
                JObject index = ReadJson(Path.Combine(LibraryDir, "index.json"));
                if (Shipped().Count > 0 && (string)index?["shipped"] != ShippedBuilt) WriteIndex(top, (bool?)index?["downloaded"] ?? false);
            }, e => Logger.LogWarning("Mod library: could not write the shipped patch map: " + e.Message));
            if (_libraryOn.Value) StartLibraryUpdate(false);
            else if (Requested().Count > 0) StartLibraryUpdate(false, true);
            yield return ClashReport();
        }

        /// <summary>Start an update unless one is running. Unless forced, nothing is fetched when the library is fresh and big enough.</summary>
        /// <param name="onlyRequested">Only the mods asked for with "library get" (when the top list is not being downloaded).</param>
        internal bool StartLibraryUpdate(bool force, bool onlyRequested = false)
        {
            if (Library.Running) return false;
            if (!force)
            {
                JObject index = ReadJson(Path.Combine(LibraryDir, "index.json"));
                bool fresh = index != null && DateTime.TryParse((string)index["updated"], out DateTime when) && (DateTime.Now - when).TotalDays < _libraryDays.Value;
                bool enough = onlyRequested
                    ? Requested().All(r => Directory.Exists(Compat.Inside(LibraryMods, r)))
                    : (bool?)index?["downloaded"] == true && (int?)index["top"] == _libraryTop.Value && (string)index["also"] == Normalize(_libraryExtra.Value);
                if (fresh && enough) return false;
            }
            StartCoroutine(UpdateLibrary(onlyRequested));
            return true;
        }

        // ---- mods asked for by name ("library get"), kept whatever their rank until "library drop" ----

        private static string RequestedFile => Path.Combine(LibraryDir, "requested.json");

        internal static List<string> Requested()
        {
            try
            {
                return File.Exists(RequestedFile)
                    ? JArray.Parse(File.ReadAllText(RequestedFile)).Select(x => (string)x).Where(Compat.IsPackage).ToList()
                    : new List<string>();
            }
            catch (Exception) { return new List<string>(); }
        }

        internal static void SaveRequested(IEnumerable<string> list)
        {
            Directory.CreateDirectory(LibraryDir);
            File.WriteAllText(RequestedFile, new JArray(list.Where(Compat.IsPackage).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)).ToString());
        }

        private static string Normalize(string list) => string.Join(",", Names(list).OrderBy(x => x).ToArray());
        private static IEnumerable<string> Names(string list) => (list ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0);

        private static IEnumerable<string> Packages(string list) => Names(list).Where(Compat.IsPackage);

        private class Package
        {
            public string Namespace, Name, Version, Url;
            public long Downloads;
            public int Rank; // 0: kept because AlsoKeep names it
            public string Full => Namespace + "-" + Name;
        }

        private IEnumerator UpdateLibrary(bool onlyRequested)
        {
            Library.Running = true;
            Library.LastError = null;
            Library.Started = DateTime.Now;
            Library.Done = 0;
            Library.Note("update started");
            try
            {
                Directory.CreateDirectory(LibraryMods);
                var skip = new HashSet<string>(Names(_librarySkip.Value), StringComparer.OrdinalIgnoreCase);
                int top = _libraryTop.Value;

                // 1. the top list, 20 a page, most downloaded first
                var wanted = new List<Package>();
                for (int page = 1; !onlyRequested && wanted.Count < top && page <= 100; page++)
                {
                    Library.Step = $"reading Thunderstore's list (page {page})";
                    string text = null, err = null;
                    yield return GetText(Listing + page, (t, e) => { text = t; err = e; });
                    if (text == null) { Fail("Thunderstore's list: " + err); yield break; }
                    JObject json = TryParse(text);
                    if (json == null) { Fail("Thunderstore's list: an answer that isn't JSON (the site may be down)"); yield break; }
                    foreach (JObject r in json["results"] ?? new JArray())
                    {
                        var p = new Package { Namespace = (string)r["namespace"], Name = (string)r["name"], Downloads = (long?)r["download_count"] ?? 0 };
                        bool modpack = (r["categories"] as JArray)?.Any(c => ((string)c["slug"] ?? "").Contains("modpack")) ?? false;
                        if (p.Namespace == null || p.Name == null || !Compat.IsPackage(p.Full) || skip.Contains(p.Full) || modpack || (bool?)r["is_deprecated"] == true) continue;
                        if (wanted.Count < top) { p.Rank = wanted.Count + 1; wanted.Add(p); }
                    }
                    if (json["next"] == null || json["next"].Type == JTokenType.Null) break;
                }
                foreach (string full in (onlyRequested ? new string[0] : Packages(_libraryExtra.Value)).Concat(Requested()))
                {
                    int dash = full.IndexOf('-');
                    if (dash > 0 && !wanted.Any(w => w.Full.Equals(full, StringComparison.OrdinalIgnoreCase)))
                        wanted.Add(new Package { Namespace = full.Substring(0, dash), Name = full.Substring(dash + 1) });
                }
                Library.Total = wanted.Count;
                Library.Note($"{wanted.Count} mods wanted");

                // 2. each one: its latest version, and its DLLs if they are new
                foreach (Package p in wanted)
                {
                    if (_stopping) yield break;
                    Library.Step = $"{p.Full} ({Library.Done + 1} of {wanted.Count})";
                    string text = null, err = null;
                    yield return GetText(PackageApi + p.Namespace + "/" + p.Name + "/", (t, e) => { text = t; err = e; });
                    JObject info = text != null ? TryParse(text) : null;
                    if (info == null) { Library.Note($"{p.Full}: {err ?? "an answer that isn't JSON"}"); Library.Done++; continue; }
                    p.Version = (string)info["latest"]?["version_number"];
                    p.Url = (string)info["latest"]?["download_url"];
                    if (p.Rank == 0)
                    {
                        // (Thunderstore's package API reports -1 downloads: its search listing has the real count)
                        string found = null;
                        yield return GetText("https://thunderstore.io/api/cyberstorm/listing/valheim/?deprecated=true&q=" + Uri.EscapeDataString(p.Name), (t, e) => found = t);
                        JToken hit = found == null ? null : (TryParse(found)?["results"] as JArray)?.FirstOrDefault(r =>
                            string.Equals((string)r["namespace"], p.Namespace, StringComparison.OrdinalIgnoreCase) && string.Equals((string)r["name"], p.Name, StringComparison.OrdinalIgnoreCase));
                        p.Downloads = Math.Max(0, (long?)hit?["download_count"] ?? 0);
                    }
                    string dir = Compat.Inside(LibraryMods, p.Full);
                    JObject old = ReadJson(Path.Combine(dir, "scan.json"));
                    if (old != null && (string)old["version"] == p.Version)
                    {
                        old["rank"] = p.Rank; old["downloads"] = p.Downloads; // (same version: only its standing changed)
                        File.WriteAllText(Path.Combine(dir, "scan.json"), old.ToString());
                        Library.Done++;
                        continue;
                    }
                    var chunks = new List<KeyValuePair<Zip.Entry, byte[]>>();
                    var fetch = new FetchResult();
                    yield return FetchDlls(p, chunks, fetch);
                    if (!fetch.Failed) yield return Digest(p, chunks, fetch);
                    if (fetch.Failed) Library.Note($"{p.Full}: kept what was there (the download failed; the next update tries again)");
                    Library.Done++;
                    yield return new WaitForSecondsRealtime(0.3f); // (go easy on Thunderstore)
                }

                // 3. drop mods no longer wanted (not when only fetching the ones asked for), then the index and the patch map
                var keep = new HashSet<string>(wanted.Select(w => w.Full), StringComparer.OrdinalIgnoreCase);
                foreach (string dir in Directory.GetDirectories(LibraryMods).Where(d => !onlyRequested && !keep.Contains(Path.GetFileName(d))))
                {
                    try { Directory.Delete(dir, true); Library.Note("removed " + Path.GetFileName(dir)); } catch (Exception e) { Library.Note("could not remove " + dir + ": " + e.Message); }
                }
                Library.Step = "writing the patch map";
                bool downloaded = !onlyRequested || (bool?)ReadJson(Path.Combine(LibraryDir, "index.json"))?["downloaded"] == true;
                yield return OnWorker(() => WriteIndex(top, downloaded), e => Library.Note("could not write the patch map: " + e.Message));
                Library.Note($"update done: {Library.Done} mods");
                StartCoroutine(ClashReport());
                Library.Step = "done";
            }
            finally { Library.Running = false; }
        }

        private void Fail(string why)
        {
            Library.LastError = why;
            Library.Step = "failed";
            Library.Note(why);
            Logger.LogWarning("Mod library: " + why);
        }

        /// <summary>Take a package's DLLs out of its zip: the end of the zip says where they are, then only those bytes are fetched.</summary>
        private class FetchResult { public bool Failed; }

        private IEnumerator FetchDlls(Package p, List<KeyValuePair<Zip.Entry, byte[]>> chunks, FetchResult result)
        {
            if (p.Url == null) { Library.Note($"{p.Full}: no download"); result.Failed = true; yield break; }
            string url = p.Url;
            yield return Resolve(p.Url, u => url = u); // (Thunderstore sends the download on to its CDN; ranges go straight there)

            byte[] tail = null; long total = 0; string err = null;
            yield return GetBytes(url, -65536, -1, (b, t, e) => { tail = b; total = t; err = e; });
            if (tail == null) { Library.Note($"{p.Full}: {err}"); result.Failed = true; yield break; }
            long tailStart = total - tail.Length;
            if (!Zip.FindDirectory(tail, tailStart, out long dirAt, out long dirSize)) { Library.Note($"{p.Full}: not a zip"); result.Failed = true; yield break; }
            byte[] dir;
            if (dirAt >= tailStart) { dir = new byte[dirSize]; Array.Copy(tail, dirAt - tailStart, dir, 0, dirSize); }
            else
            {
                dir = null;
                yield return GetBytes(url, dirAt, dirAt + dirSize - 1, (b, t, e) => { dir = b; err = e; });
                if (dir == null) { Library.Note($"{p.Full}: {err}"); result.Failed = true; yield break; }
            }
            foreach (Zip.Entry e in Zip.Entries(dir).Where(x => x.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && x.Size > 0))
            {
                if (e.Size > 400L * 1024 * 1024) { Library.Note($"{p.Full}: {e.Name} is too big ({e.Size / 1048576} MB), left out"); continue; }
                byte[] chunk = null;
                long from = e.LocalOffset, to = Math.Min(total - 1, e.LocalOffset + Zip.Span(e) - 1);
                yield return GetBytes(url, from, to, (b, t, x) => { chunk = b; err = x; });
                if (chunk == null) { Library.Note($"{p.Full}: {e.Name}: {err}"); result.Failed = true; yield break; }
                chunks.Add(new KeyValuePair<Zip.Entry, byte[]>(e, chunk)); // (unpacked on the worker thread, not here: a big one takes seconds)
            }
            if (chunks.Count == 0) Library.Note($"{p.Full}: no DLLs in it");
        }

        /// <summary>
        /// On a worker thread: unpack each DLL, strip its embedded assets, save the code-only copy and list its patches, all into a fresh
        /// folder that replaces the old copy only when everything worked (a failure keeps the old one).
        /// </summary>
        private IEnumerator Digest(Package p, List<KeyValuePair<Zip.Entry, byte[]>> chunks, FetchResult fetch)
        {
            string dir = Compat.Inside(LibraryMods, p.Full), fresh = dir + ".new";
            string[] search = SearchDirs();
            string failed = null;
            var worker = new Thread(() =>
            {
                try
                {
                    if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
                    string dllDir = Path.Combine(fresh, "dll");
                    Directory.CreateDirectory(dllDir);
                    var scans = new JArray();
                    foreach (var kv in chunks)
                    {
                        byte[] data = Zip.Extract(kv.Value, kv.Key, out long need);
                        if (data == null) { failed = $"{kv.Key.Name}: the download was cut short ({need} bytes needed)"; return; }
                        string name = Path.GetFileName(kv.Key.Name);
                        string path = Path.Combine(dllDir, name);
                        for (int n = 2; File.Exists(path); n++) path = Path.Combine(dllDir, Path.GetFileNameWithoutExtension(name) + "_" + n + ".dll");
                        if (!Scan.Slim(data, path, search)) continue; // (a native DLL: nothing to read)
                        JObject scan = Scan.Dll(path, search);
                        scan["inZip"] = kv.Key.Name;
                        scans.Add(scan);
                    }
                    var result = new JObject
                    {
                        ["package"] = p.Full, ["version"] = p.Version, ["rank"] = p.Rank, ["downloads"] = p.Downloads,
                        ["page"] = $"https://thunderstore.io/c/valheim/p/{p.Namespace}/{p.Name}/", ["scanned"] = DateTime.Now.ToString("s"),
                        ["dlls"] = scans,
                    };
                    File.WriteAllText(Path.Combine(fresh, "scan.json"), result.ToString());
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    Directory.Move(fresh, dir);
                }
                catch (Exception e) { failed = e.Message; }
                finally
                {
                    try { if (failed != null && Directory.Exists(fresh)) Directory.Delete(fresh, true); } catch (Exception) { }
                }
            }) { IsBackground = true, Name = "ClaudeTools library" };
            worker.Start();
            while (worker.IsAlive) yield return null;
            chunks.Clear();
            if (failed != null) { fetch.Failed = true; Library.Note($"{p.Full}: {failed}"); }
            else Library.Note($"{p.Full} {p.Version}: scanned");
        }

        /// <summary>Run <paramref name="work"/> on a worker thread and wait for it (the game keeps running); errors go to <paramref name="error"/>.</summary>
        internal static IEnumerator OnWorker(Action work, Action<Exception> error)
        {
            Exception failed = null;
            var worker = new Thread(() => { try { work(); } catch (Exception e) { failed = e; } }) { IsBackground = true, Name = "ClaudeTools worker" };
            worker.Start();
            while (worker.IsAlive) yield return null;
            if (failed != null) error(failed);
        }

        private static JObject TryParse(string text)
        {
            try { return JObject.Parse(text); }
            catch (Exception) { return null; }
        }

        internal static string[] SearchDirs() => new[] { Paths.BepInExAssemblyDirectory, Paths.ManagedPath, Paths.PluginPath };

        /// <summary>library/index.json (what is there) and library/patchmap.json (every patch, by game method).</summary>
        private void WriteIndex(int top, bool downloaded)
        {
            Directory.CreateDirectory(LibraryDir);
            JArray mods = Compat.BuildIndex(LibraryScans(), out JObject patchmap);
            File.WriteAllText(Path.Combine(LibraryDir, "index.json"), new JObject
            {
                ["updated"] = DateTime.Now.ToString("s"), ["top"] = top, ["also"] = Normalize(_libraryExtra.Value), ["downloaded"] = downloaded,
                ["shipped"] = ShippedBuilt, ["requested"] = new JArray(Requested()),
                ["game"] = GameVersion(), ["mods"] = mods,
            }.ToString());
            File.WriteAllText(Path.Combine(LibraryDir, "patchmap.json"), patchmap.ToString());
        }

        private static string GameVersion()
        {
            try { return global::Version.GetVersionString(); } catch (Exception) { return ""; }
        }

        /// <summary>Every library mod: the downloaded ones' scan.json, then the shipped list's mods that were not downloaded.</summary>
        internal static IEnumerable<JObject> LibraryScans() => Compat.LibraryScans(LibraryMods, Shipped());

        // ---- the patch map shipped inside ClaudeTools (made by tools/PatchMap from the top Thunderstore mods: facts only, no code) ----

        private static List<JObject> _shipped;
        internal static string ShippedBuilt = "";

        internal static List<JObject> Shipped()
        {
            if (_shipped != null) return _shipped;
            try
            {
                using (Stream s = typeof(Plugin).Assembly.GetManifestResourceStream("library/shipped.json.gz"))
                    _shipped = Compat.ReadShipped(s, out ShippedBuilt);
            }
            catch (Exception e) { _shipped = new List<JObject>(); Log?.LogWarning("Mod library: could not read the shipped patch map: " + e.Message); }
            return _shipped;
        }

        internal static JObject ReadJson(string path) => Compat.ReadJson(path);

        // ---- the web ----

        // Going easy on Thunderstore: requests at least 0.35 s apart, and when it answers 429 (too many) or 503, wait and try again.
        private static float _lastRequest = -10f;

        private static IEnumerator Send(Func<UnityWebRequest> make, Action<UnityWebRequest> done)
        {
            for (int attempt = 0; ; attempt++)
            {
                float wait = _lastRequest + 0.35f - Time.realtimeSinceStartup;
                if (wait > 0f) yield return new WaitForSecondsRealtime(wait);
                _lastRequest = Time.realtimeSinceStartup;
                UnityWebRequest req = make();
                yield return req.SendWebRequest();
                if ((req.responseCode == 429 || req.responseCode == 503) && attempt < 6)
                {
                    float after = float.TryParse(req.GetResponseHeader("Retry-After"), out float s) ? s : Mathf.Min(120f, 5f * Mathf.Pow(2f, attempt));
                    req.Dispose();
                    Library.Note($"Thunderstore says to slow down: waiting {after:0} s");
                    yield return new WaitForSecondsRealtime(after);
                    continue;
                }
                try { done(req); }
                finally { req.Dispose(); }
                yield break;
            }
        }

        private static IEnumerator GetText(string url, Action<string, string> done) => Send(() =>
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = 60;
            req.SetRequestHeader("Accept", "application/json");
            return req;
        }, req =>
        {
            if (req.result != UnityWebRequest.Result.Success) done(null, $"{req.error} ({req.responseCode})");
            else done(req.downloadHandler.text, null);
        });

        /// <summary>
        /// Bytes <paramref name="from"/>..<paramref name="to"/> of a file, or the last -from bytes when from is negative. Reports the
        /// file's full size; a server that ignores ranges sends all of it, which is cut to the range asked for.
        /// </summary>
        private static IEnumerator GetBytes(string url, long from, long to, Action<byte[], long, string> done) => Send(() =>
        {
            UnityWebRequest req = UnityWebRequest.Get(url);
            req.timeout = 300;
            req.SetRequestHeader("Range", from < 0 ? $"bytes={from}" : $"bytes={from}-{to}");
            return req;
        }, req =>
        {
            if (req.result != UnityWebRequest.Result.Success) { done(null, 0, $"{req.error} ({req.responseCode})"); return; }
            byte[] data = req.downloadHandler.data;
            if (req.responseCode == 206)
            {
                string range = req.GetResponseHeader("Content-Range") ?? ""; // bytes a-b/total
                long total = long.TryParse(range.Substring(range.LastIndexOf('/') + 1), out long t) ? t : data.Length;
                done(data, total, null);
                return;
            }
            long size = data.Length; // the whole file
            long start = from < 0 ? Math.Max(0, size + from) : Math.Min(from, size);
            long end = from < 0 ? size - 1 : Math.Min(to, size - 1);
            var part = new byte[Math.Max(0, end - start + 1)];
            Array.Copy(data, start, part, 0, part.Length);
            done(part, size, null);
        });

        /// <summary>Where a download link sends you (Thunderstore's links go on to its CDN). The link itself if it can't tell.</summary>
        private static IEnumerator Resolve(string url, Action<string> done)
        {
            using (UnityWebRequest req = UnityWebRequest.Head(url))
            {
                req.redirectLimit = 0;
                req.timeout = 30;
                yield return req.SendWebRequest();
                string location = req.GetResponseHeader("Location");
                done(req.responseCode >= 300 && req.responseCode < 400 && !string.IsNullOrEmpty(location) ? location : url);
            }
        }
    }
}
