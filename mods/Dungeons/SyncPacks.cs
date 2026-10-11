using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Dungeons
{
    /// <summary>
    /// Which packs and entrance looks are in use, and where each came from. Every game builds a dungeon from its pack and seed, so every
    /// player on a server must use the same set:
    ///   - offline, on the host and on a dedicated server: its own set: the ones built into the DLL (packs/*.json and packs/looks/*.json),
    ///     and BepInEx/config/Dungeons/packs/*.json and packs/looks/*.json (one there with a built-in one's name replaces it). Read again
    ///     for each world, so a host can change them between two sessions;
    ///   - a client connected to a server that has the mod: exactly the server's set, sent when it connects (SyncNet.cs), before any of the
    ///     world's objects. Until it's in (Waiting), no pack at all, so no dungeon is ever laid out from the client's own packs there.
    /// This class decides; Packs.cs is told (its sources: "config" when this game's own set is read, "server" when the server's comes,
    /// and Packs.Waiting meanwhile), and EntranceLooks.cs reads Looks() again when Generation changes (its prefabs come only from the
    /// DLL's own looks, which never change).
    ///
    /// Each file is checked when it's read, and again when it arrives from the server. One that can't be used (not JSON, no valid name, a
    /// name already taken, too big, or the pack reader throws on it) is logged and left out, never stopping anything. Anything else wrong
    /// is kept with it and listed by `dungeon packs` (a pack's problems with the game's rooms only show in a world: Packs.Get reads it
    /// with them). The rule is the same on every game, so the server and its players always end up with the same set.
    /// </summary>
    internal static class PackSync
    {
        public const string BuiltIn = "built in", Config = "config", Server = "server";
        private const int MaxFile = 8 << 20;        // one pack or look's JSON (Shifting Halls is ~0.5 MB)
        private const int MaxEntries = 256;
        public const int MaxSetBytes = 64 << 20;    // the whole set, unpacked

        internal sealed class Entry
        {
            public string Kind;            // "pack" or "look"
            public string Name;            // lower case: what packs and ZDOs call it
            public string Text;            // its JSON, compact: what's hashed and sent
            public JObject Json;
            public string Source;          // BuiltIn, Config or Server
            public string Origin;          // where it was read from (a resource, a file; from the server: its source there)
            public string Hash;            // of Text (8 hex digits)
            public readonly List<string> Problems = new List<string>();   // what the look check says (kept, not stopping it)
        }

        internal sealed class Set
        {
            public readonly Dictionary<string, Entry> Packs = new Dictionary<string, Entry>();
            public readonly Dictionary<string, Entry> Looks = new Dictionary<string, Entry>();
            public readonly List<string> LeftOut = new List<string>();   // what couldn't be used, and why
            public string Hash = "";       // of every entry's kind, name and text (16 hex digits): two games with the same hash use the same set
            public int Bytes;              // the JSON's size
            public IEnumerable<Entry> All => Packs.Values.OrderBy(e => e.Name, StringComparer.Ordinal).Concat(Looks.Values.OrderBy(e => e.Name, StringComparer.Ordinal));
        }

        private static Set _builtIn, _local, _server;
        private static bool _waiting, _same;

        /// <summary>Changes whenever the set in use does: Packs and EntranceLooks read theirs again.</summary>
        public static int Generation { get; private set; } = 1;

        /// <summary>A client whose server has the mod, before the server's set is in: there are no packs meanwhile.</summary>
        public static bool Waiting => _waiting;

        /// <summary>Why there are no dungeons in this game now (a client on a server without a matching Dungeons, or whose server's set
        /// never came or can't be used), or null. Packs gives none meanwhile, so nothing is laid out, spawned or entered.</summary>
        public static string OffReason { get; private set; }

        /// <summary>The server's set is in use (a client).</summary>
        public static bool FromServer => _server != null || _same;

        /// <summary>The server said its set is the same as this game's own (so it wasn't sent).</summary>
        public static bool SameAsServer => _same;

        /// <summary>Only the DLL's own packs and looks: the entrance prefabs are made for these looks alone.</summary>
        public static Set BuiltInSet => _builtIn ?? (_builtIn = ReadBuiltIn());

        /// <summary>This game's own set: built in, and the config folder.</summary>
        public static Set Local
        {
            get
            {
                if (_local != null) return _local;
                _local = ReadLocal();
                Tell();   // (the config folder's packs, for Packs.cs)
                return _local;
            }
        }

        /// <summary>The set in use now (while Waiting: this game's own, but Packs.cs gives none of it).</summary>
        public static Set Current => _server ?? Local;

        /// <summary>The entrance looks in use: lower-case name -> JSON.</summary>
        public static Dictionary<string, JObject> Looks() => Current.Looks.ToDictionary(kv => kv.Key, kv => kv.Value.Json);

        /// <summary>Where a pack in use came from: "built in", "config (file)", "server (built in there)"...; null if there's none.</summary>
        public static string SourceOf(string pack) => Describe(Find(Current.Packs, pack));

        public static string LookSourceOf(string look) => Describe(Find(Current.Looks, look));

        public static Entry Find(Dictionary<string, Entry> table, string name) =>
            name != null && table.TryGetValue(name.Trim().ToLowerInvariant(), out Entry e) ? e : null;

        public static string Describe(Entry e)
        {
            if (e == null) return null;
            if (e.Source == Config) return $"{Config} ({e.Origin})";
            if (e.Source == Server) return $"{Server} ({e.Origin} there)";
            return e.Source;
        }

        /// <summary>The folder players put their own packs in (and looks/ in it for looks).</summary>
        public static string ConfigFolder => Path.Combine(Path.Combine(Paths.ConfigPath, "Dungeons"), "packs");

        // ---- what SyncNet says

        /// <summary>The server has the mod: no packs until its set comes.</summary>
        public static void Expect()
        {
            _server = null;
            _same = false;
            _waiting = true;
            Changed();
        }

        /// <summary>No dungeons on this server, and why (for the log, `dungeon sync` and the entrance's hover text).</summary>
        public static void TurnOff(string why)
        {
            OffReason = why;
            Changed();
        }

        /// <summary>The server's set came: used exactly, while connected.</summary>
        public static void UseServer(Set set)
        {
            AddKeys(set);
            _server = set;
            _same = false;
            _waiting = false;
            Changed();
        }

        /// <summary>The server's set is this game's own (the hashes matched): it's used as it is.</summary>
        public static void UseSame()
        {
            _server = null;
            _same = true;
            _waiting = false;
            Changed();
        }

        /// <summary>The host or single player reads its own set again now (`dungeon reload`): the one it had (to compare), and Packs.cs
        /// and the looks are told. The new set is returned (Local).</summary>
        public static Set Reload(out Set before)
        {
            before = _local ?? new Set();
            _local = ReadLocal();
            AddKeys(_local);
            Changed();
            return _local;
        }

        /// <summary>A new connection, or leaving a world: back to this game's own set, read again next time (the config folder may change).</summary>
        public static void Reset()
        {
            bool was = _server != null || _waiting || _same || _local != null || OffReason != null;
            OffReason = null;
            _server = null;
            _same = _waiting = false;
            _local = null;
            if (was) Changed();
        }

        private static void Changed()
        {
            Generation++;
            Tell();
        }

        /// <summary>Tells Packs.cs the set in use: the server's whole set on a client that has it, else this game's own config packs over
        /// the built-in ones (Packs.cs reads those itself); and whether it must wait (no packs at all meanwhile).</summary>
        private static void Tell()
        {
            try
            {
                global::Dungeons.Packs.Waiting = _waiting || OffReason != null;
                if (_server != null)
                {
                    // (an empty "server" source would mean the client's own: never that on a server with the mod)
                    if (_server.Packs.Count == 0) global::Dungeons.Packs.Waiting = true;
                    else global::Dungeons.Packs.SetSource("server", _server.Packs.Values.Select(e => e.Text).ToList());
                    return;
                }
                global::Dungeons.Packs.SetSource("server", null);
                global::Dungeons.Packs.SetSource("config", _local == null ? null : _local.Packs.Values.Where(e => e.Source == Config).Select(e => e.Text).ToList());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Telling the packs which set is in use: " + e.Message); }
        }

        // ---- reading

        private static Set ReadBuiltIn()
        {
            var set = new Set();
            var asm = typeof(PackSync).Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(r => r.EndsWith(".json")).OrderBy(r => r, StringComparer.Ordinal))
            {
                string kind = res.StartsWith("packs/") ? "pack" : res.StartsWith("looks/") ? "look" : null;
                if (kind == null) continue;
                try
                {
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (var reader = new StreamReader(s)) Add(set, kind, reader.ReadToEnd(), BuiltIn, res);
                }
                catch (Exception e) { Leave(set, $"{res}: {e.Message}"); }
            }
            Finish(set);
            return set;
        }

        private static Set ReadLocal()
        {
            var set = new Set();
            foreach (Entry e in BuiltInSet.All) (e.Kind == "pack" ? set.Packs : set.Looks)[e.Name] = e;
            set.LeftOut.AddRange(BuiltInSet.LeftOut);
            try
            {
                string looks = Path.Combine(ConfigFolder, "looks");
                Directory.CreateDirectory(looks);
                WriteReadme();
                foreach (string folder in new[] { ConfigFolder, looks })
                    foreach (string file in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string kind = folder == looks ? "look" : "pack";
                        string where = kind == "look" ? "looks/" + Path.GetFileName(file) : Path.GetFileName(file);
                        try
                        {
                            if (new FileInfo(file).Length > MaxFile) { Leave(set, $"{where}: bigger than {MaxFile >> 20} MB"); continue; }
                            Add(set, kind, File.ReadAllText(file), Config, where);
                        }
                        catch (Exception e) { Leave(set, $"{where}: can't be read: {e.Message}"); }
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"The packs folder {ConfigFolder} can't be read: {e.Message}"); }
            Finish(set);
            AddKeys(set);
            int own = set.All.Count(e => e.Source == Config);
            if (own > 0) Plugin.Log.LogInfo($"Packs from {ConfigFolder}: {string.Join(", ", set.All.Where(e => e.Source == Config).Select(e => $"{e.Kind} {e.Name}"))}");
            return set;
        }

        /// <summary>The key items of packs that aren't built in (BlockKeys registers the built-in ones itself), registered as soon as the
        /// pack is known: before the player's character comes, so a key in their bag or on the floor finds its prefab.</summary>
        private static void AddKeys(Set set)
        {
            foreach (Entry e in set.Packs.Values.Where(e => e.Source != BuiltIn))
                try { BlockKeys.Add(e.Json); }
                catch (Exception x) { Plugin.Log.LogWarning($"Pack {e.Name}: its key items can't be registered: {x.Message}"); }
        }

        /// <summary>Checks one pack or look and adds it (logged and left out if it can't be used). A config one may replace a built-in one.</summary>
        private static void Add(Set set, string kind, string text, string source, string origin)
        {
            Entry e = Check(kind, text, source, origin, out string why);
            if (e == null) { Leave(set, $"{origin}: {why}"); return; }
            Dictionary<string, Entry> table = kind == "pack" ? set.Packs : set.Looks;
            if (table.TryGetValue(e.Name, out Entry old))
            {
                if (!(source == Config && old.Source == BuiltIn)) { Leave(set, $"{origin}: a {kind} called {e.Name} is already in ({Describe(old)})"); return; }
                Plugin.Log.LogWarning($"{ConfigFolder}/{origin} replaces the built-in {kind} {e.Name}" + (kind == "pack" ? " (its dungeons already in the world change: the same seed, other rooms)" : ""));
            }
            if (table.Count >= MaxEntries) { Leave(set, $"{origin}: more than {MaxEntries} {kind}s"); return; }
            table[e.Name] = e;
        }

        /// <summary>What a pack or look is, or null with why it can't be used (no game types needed, nothing loaded).</summary>
        public static Entry Check(string kind, string text, string source, string origin, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(text)) { why = "empty"; return null; }
            if (text.Length > MaxFile) { why = $"bigger than {MaxFile >> 20} MB"; return null; }
            JObject json;
            try { json = JObject.Parse(text); }
            catch (Exception e) { why = "not JSON: " + e.Message; return null; }
            string name = ((string)json["name"] ?? "").Trim();
            if (!NameOk(kind, name)) { why = kind == "look" ? "its \"name\" must be lower case letters, digits and _ (it names the look in saves)" : "its \"name\" must be letters, digits, _ and - (at most 48)"; return null; }
            var entry = new Entry { Kind = kind, Name = name.ToLowerInvariant(), Json = json, Text = json.ToString(Formatting.None), Source = source, Origin = origin };
            entry.Hash = Hex(Sha(entry.Text), 8);
            if (kind == "look")
            {
                try { entry.Problems.AddRange(LookCheck.Check(json, origin)); }
                catch (Exception e) { why = "the look check fails on it: " + e.Message; return null; }
            }
            else
            {
                // Read without the game's rooms (a world isn't needed): whether it can be read at all. Its problems are listed in a world.
                try { PackReader.Read((JObject)json.DeepClone(), _ => null, new List<string>()); }
                catch (Exception e) { why = "the pack reader fails on it: " + e.GetType().Name + ": " + e.Message; return null; }
            }
            return entry;
        }

        private static bool NameOk(string kind, string name)
        {
            if (name.Length == 0 || name.Length > 48) return false;
            if (kind == "look") return name.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_');
            return name.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-');
        }

        private static void Leave(Set set, string why)
        {
            set.LeftOut.Add(why);
            Plugin.Log.LogWarning("Left out: " + why);
        }

        private static void Finish(Set set)
        {
            var all = new StringBuilder();
            foreach (Entry e in set.All) all.Append(e.Kind).Append('\n').Append(e.Name).Append('\n').Append(e.Text).Append('\n');
            set.Hash = Hex(Sha(all.ToString()), 16);
            set.Bytes = set.All.Sum(e => e.Text.Length);
        }

        private static byte[] Sha(string text)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        }

        private static string Hex(byte[] bytes, int digits) => string.Concat(bytes.Select(b => b.ToString("x2"))).Substring(0, digits);

        private static void WriteReadme()
        {
            string file = Path.Combine(Path.Combine(Paths.ConfigPath, "Dungeons"), "README.txt");
            if (File.Exists(file)) return;
            File.WriteAllText(file,
                "Quad's Dungeons: your own packs and entrance looks.\r\n\r\n" +
                "packs/<name>.json        a pack (the format is in Packs.cs; the built-in ones are examples)\r\n" +
                "packs/looks/<name>.json  an entrance look (the format is in EntranceLooks.cs)\r\n\r\n" +
                "A file with a built-in one's name replaces it. They're read when a world starts (on a server, when it starts).\r\n\r\n" +
                "Making a pack (single player or as the host): edit the file, then 'dungeon reload' (Claude Tools) reads the folder\r\n" +
                "again at once, checks each changed pack over 50 seeds and remakes its dungeons (never one with a player inside:\r\n" +
                "'dungeon reload force' takes them out to its entrance first). 'dungeon respawn [new]' remakes the nearest one.\r\n" +
                "The setting ReloadPacksOnSave does the reload a second after each save.\r\n\r\n" +
                "On a server, only the server's folder counts: players get the server's packs and looks when they join, and use\r\n" +
                "exactly those while they play there. The game's log says what was left out and why; 'dungeon packs' and\r\n" +
                "'dungeon sync' (Claude Tools) show where each came from.\r\n");
        }

        // ---- sending (SyncNet: the server packs its set once, a client unpacks it)

        /// <summary>The set as bytes to send: every entry's kind, name, source and JSON, gzipped.</summary>
        public static byte[] Pack(Set set)
        {
            var pkg = new ZPackage();
            pkg.Write(1);   // (the format)
            List<Entry> all = set.All.ToList();
            pkg.Write(all.Count);
            foreach (Entry e in all)
            {
                pkg.Write(e.Kind);
                pkg.Write(e.Name);
                pkg.Write(e.Source);
                pkg.Write(e.Text);
            }
            byte[] raw = pkg.GetArray();
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal)) gzip.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        /// <summary>The server's set from its bytes, each entry checked as this game's own are. Throws if the bytes are broken.</summary>
        public static Set Unpack(byte[] data)
        {
            byte[] raw;
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[81920];
                int n;
                while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, n);
                    if (output.Length > MaxSetBytes) throw new InvalidDataException($"bigger than {MaxSetBytes >> 20} MB unpacked");
                }
                raw = output.ToArray();
            }
            var pkg = new ZPackage(raw);
            int format = pkg.ReadInt();
            if (format != 1) throw new InvalidDataException($"format {format} (this version reads 1)");
            int count = pkg.ReadInt();
            if (count < 0 || count > 2 * MaxEntries) throw new InvalidDataException($"{count} entries");
            var set = new Set();
            for (int i = 0; i < count; i++)
            {
                string kind = pkg.ReadString(), name = pkg.ReadString(), source = pkg.ReadString(), text = pkg.ReadString();
                if (kind != "pack" && kind != "look") { Leave(set, $"the server's {name}: an unknown kind {kind}"); continue; }
                Entry e = Check(kind, text, Server, source, out string why);
                if (e == null) { Leave(set, $"the server's {kind} {name}: {why}"); continue; }
                Dictionary<string, Entry> table = kind == "pack" ? set.Packs : set.Looks;
                if (table.ContainsKey(e.Name)) { Leave(set, $"the server's {kind} {name}: sent twice"); continue; }
                table[e.Name] = e;
            }
            Finish(set);
            return set;
        }
    }
}
