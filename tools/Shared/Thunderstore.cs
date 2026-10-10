using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// Thunderstore for the command-line tools (PatchMap, modkit): the most-downloaded Valheim mods, a package's latest version, and its DLLs
    /// taken out of the zip by byte ranges. (The game's own copy of this uses UnityWebRequest, in Library.cs.)
    /// </summary>
    internal static class Thunderstore
    {
        private const string Listing = "https://thunderstore.io/api/cyberstorm/listing/valheim/?ordering=most-downloaded&page=";
        private const string PackageApi = "https://thunderstore.io/api/experimental/package/";
        public static readonly string[] NotMods = { "ebkr-r2modman", "denikson-BepInExPack_Valheim", "Kesomannen-GaleModManager" };

        private static readonly HttpClient Http = MakeClient();

        private static HttpClient MakeClient()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeTools-modkit/1.0");
            return client;
        }

        /// <summary>The top mods, most downloaded first (listing entries: namespace, name, download_count, categories...).</summary>
        public static IEnumerable<JObject> Top(int top)
        {
            int count = 0;
            for (int page = 1; page <= 200; page++)
            {
                JObject json = JObject.Parse(GetString(Listing + page));
                foreach (JObject r in json["results"])
                {
                    string full = r["namespace"] + "-" + r["name"];
                    bool modpack = ((JArray)r["categories"]).Any(c => ((string)c["slug"] ?? "").Contains("modpack"));
                    if (NotMods.Contains(full) || modpack || (bool?)r["is_deprecated"] == true) continue;
                    if (count++ >= top) yield break;
                    yield return r;
                }
                if (json["next"] == null || json["next"].Type == JTokenType.Null) yield break;
            }
        }

        /// <summary>
        /// A package's total downloads, from Thunderstore's search listing (its package API reports -1 for them). 0 when it can't tell.
        /// </summary>
        public static long Downloads(string ns, string name)
        {
            try
            {
                JObject json = JObject.Parse(GetString("https://thunderstore.io/api/cyberstorm/listing/valheim/?deprecated=true&q=" + Uri.EscapeDataString(name)));
                JToken hit = (json["results"] as JArray)?.FirstOrDefault(r => string.Equals((string)r["namespace"], ns, StringComparison.OrdinalIgnoreCase)
                                                                          && string.Equals((string)r["name"], name, StringComparison.OrdinalIgnoreCase));
                return Math.Max(0, (long?)hit?["download_count"] ?? 0);
            }
            catch (Exception) { return 0; }
        }

        /// <summary>A package's details (latest.version_number, latest.download_url...).</summary>
        public static JObject Package(string ns, string name) => JObject.Parse(GetString(PackageApi + ns + "/" + name + "/"));

        /// <summary>A package's DLLs, taken out of its zip by byte ranges (the end of the zip says where they are).</summary>
        public static List<KeyValuePair<string, byte[]>> Dlls(string url, ref long fetched)
        {
            HttpResponseMessage head = Send(() => new HttpRequestMessage(HttpMethod.Head, url));
            if (head.Headers.Location != null) url = head.Headers.Location.ToString();
            byte[] tail = Range(url, "bytes=-65536", out long total);
            fetched += tail.Length;
            long tailStart = total - tail.Length;
            if (!Zip.FindDirectory(tail, tailStart, out long dirAt, out long dirSize)) throw new InvalidDataException("not a zip");
            byte[] dir = dirAt >= tailStart ? tail.Skip((int)(dirAt - tailStart)).Take((int)dirSize).ToArray() : Range(url, $"bytes={dirAt}-{dirAt + dirSize - 1}", out total);
            var result = new List<KeyValuePair<string, byte[]>>();
            foreach (Zip.Entry e in Zip.Entries(dir).Where(x => x.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && x.Size > 0 && x.Size < 600L * 1048576))
            {
                byte[] chunk = Range(url, $"bytes={e.LocalOffset}-{Math.Min(total - 1, e.LocalOffset + Zip.Span(e) - 1)}", out total);
                fetched += chunk.Length;
                byte[] data = Zip.Extract(chunk, e, out long _);
                if (data != null) result.Add(new KeyValuePair<string, byte[]>(e.Name, data));
            }
            return result;
        }

        /// <summary>
        /// Download a package into a library folder as the game does: mods/&lt;Namespace-Name&gt;/dll (code only) and scan.json. Returns its
        /// scan.json, or the existing one when that version is already there.
        /// </summary>
        public static JObject Fetch(string modsDir, string ns, string name, int rank, long downloads, string[] search, out long fetched)
        {
            fetched = 0;
            JObject info = Package(ns, name);
            string full = ns + "-" + name, version = (string)info["latest"]["version_number"];
            string dir = Compat.Inside(modsDir, full);
            JObject old = Compat.ReadJson(Path.Combine(dir, "scan.json"));
            if (old != null && (string)old["version"] == version)
            {
                old["rank"] = rank; old["downloads"] = downloads > 0 ? downloads : Downloads(ns, name);
                File.WriteAllText(Path.Combine(dir, "scan.json"), old.ToString());
                return old;
            }
            // fetch into a fresh folder and swap it in only when it worked: a failed download keeps the old copy
            string fresh = dir + ".new";
            if (Directory.Exists(fresh)) Directory.Delete(fresh, true);
            string dllDir = Path.Combine(fresh, "dll");
            Directory.CreateDirectory(dllDir);
            var scans = new JArray();
            foreach (var kv in Dlls((string)info["latest"]["download_url"], ref fetched))
            {
                string path = Path.Combine(dllDir, Path.GetFileName(kv.Key));
                for (int k = 2; File.Exists(path); k++) path = Path.Combine(dllDir, Path.GetFileNameWithoutExtension(kv.Key) + "_" + k + ".dll");
                if (!Scan.Slim(kv.Value, path, search)) continue;
                JObject scan = Scan.Dll(path, search);
                scan["inZip"] = kv.Key;
                scans.Add(scan);
            }
            var result = new JObject
            {
                ["package"] = full, ["version"] = version, ["rank"] = rank, ["downloads"] = downloads > 0 ? downloads : Downloads(ns, name),
                ["page"] = $"https://thunderstore.io/c/valheim/p/{ns}/{name}/", ["scanned"] = DateTime.Now.ToString("s"), ["dlls"] = scans,
            };
            File.WriteAllText(Path.Combine(fresh, "scan.json"), result.ToString());
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(fresh, dir);
            return result;
        }

        // ---- going easy on Thunderstore: at most a few requests a second, and when it says "too many" (429), wait and try again ----

        private static DateTime _last = DateTime.MinValue;
        private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(350);

        private static HttpResponseMessage Send(Func<HttpRequestMessage> make)
        {
            for (int attempt = 0; ; attempt++)
            {
                TimeSpan wait = _last + Spacing - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) System.Threading.Thread.Sleep(wait);
                _last = DateTime.UtcNow;
                HttpResponseMessage res = Http.SendAsync(make()).Result;
                if ((int)res.StatusCode != 429 && (int)res.StatusCode != 503) return res;
                if (attempt >= 6) return res;
                TimeSpan after = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Min(120, 5 * Math.Pow(2, attempt)));
                Console.Error.WriteLine($"Thunderstore says to slow down: waiting {after.TotalSeconds:0} s");
                System.Threading.Thread.Sleep(after);
            }
        }

        private static string GetString(string url)
        {
            HttpResponseMessage res = Send(() => new HttpRequestMessage(HttpMethod.Get, url));
            res.EnsureSuccessStatusCode();
            return res.Content.ReadAsStringAsync().Result;
        }

        private static byte[] Range(string url, string range, out long total)
        {
            HttpResponseMessage res = Send(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("Range", range);
                return req;
            });
            if ((int)res.StatusCode != 206) throw new HttpRequestException($"{(int)res.StatusCode} for a byte range of {url}");
            total = res.Content.Headers.ContentRange?.Length ?? -1;
            return res.Content.ReadAsByteArrayAsync().Result;
        }
    }
}
