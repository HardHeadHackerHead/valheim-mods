using System.IO;

namespace Scripts
{
    /// <summary>
    /// The example scripts that come with the mod, written into the scripts folder the first time (never over a file that is there:
    /// players edit them). They are the first gallery: every one is a block program (the typed Proxy Woodcutter is in docs/examples, for people who have the Terminal).
    /// </summary>
    internal static class Examples
    {
        internal static void Install()
        {
            Finish();
        }

        /// <summary>
        /// Writes the examples: the block versions (NAME.blocks.json and the NAME.js made from it) where there is one, else the typed
        /// text. A script a player has changed is never written over. A block example that was shipped untouched is brought up to date:
        /// when the file on disk still hashes to what was recorded in examples.txt when it was written, when it is still the typed text
        /// this mod used to write, when its blocks are still exactly the shipped ones (only the code made from them is old), or when it is a
        /// version from before the record existed (Earlier).
        /// </summary>
        private static void Finish()
        {
            var record = LoadRecord();
            var blocks = new System.Collections.Generic.Dictionary<string, Blocks.Program>();
            foreach (var kv in Blocks.BlockExamples.All()) blocks[kv.Key] = kv.Value;
            foreach (var kv in blocks)
            {
                string path = Path.Combine(Host.Folder, kv.Key), blocksPath = Editor.Files.BlocksPath(kv.Key);
                Blocks.Compiled c = Blocks.Compiler.Compile(kv.Value);
                if (!c.Ok) { Core.Log.LogWarning("Example " + kv.Key + " does not compile: " + string.Join("; ", c.Problems.ConvertAll(p => p.Message).ToArray())); continue; }
                bool write = !File.Exists(path);
                if (!write)
                {
                    string disk = File.ReadAllText(path), hash = Hash(disk);
                    // opening a block script in the editor adds ids to its blocks and saves it again: that is not an edit, so the blocks are
                    // compared without their ids against what was written (examples.txt keeps a hash of those too)
                    bool blocksUntouched = File.Exists(blocksPath) && record.TryGetValue(kv.Key + "#blocks", out string recordedBlocks) && recordedBlocks == BlocksHash(File.ReadAllText(blocksPath));
                    write = blocksUntouched
                        || (record.TryGetValue(kv.Key, out string recorded) && recorded == hash)
                        || Earlier.Contains(hash)
                        || (File.Exists(blocksPath) && Same(File.ReadAllText(blocksPath), kv.Value.ToJson()));
                }
                if (!write) continue;
                File.WriteAllText(blocksPath, kv.Value.ToJson());
                File.WriteAllText(path, c.Js);
                record[kv.Key] = Hash(c.Js);
                record[kv.Key + "#blocks"] = BlocksHash(kv.Value.ToJson());
            }
            SaveRecord(record);
        }

        // what examples.txt says each shipped block example hashed to when it was written
        private static string RecordPath => Path.Combine(Host.Folder, "examples.txt");

        private static System.Collections.Generic.Dictionary<string, string> LoadRecord()
        {
            var record = new System.Collections.Generic.Dictionary<string, string>();
            try
            {
                if (File.Exists(RecordPath))
                    foreach (string line in File.ReadAllLines(RecordPath))
                    {
                        int bar = line.IndexOf('|');
                        if (bar > 0) record[line.Substring(0, bar)] = line.Substring(bar + 1).Trim();
                    }
            }
            catch (System.Exception) { }
            return record;
        }

        private static void SaveRecord(System.Collections.Generic.Dictionary<string, string> record)
        {
            try { File.WriteAllLines(RecordPath, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(record, kv => kv.Key + "|" + kv.Value))); }
            catch (System.Exception e) { Core.Log.LogWarning("Could not write examples.txt: " + e.Message); }
        }

        /// <summary>A hash of a blocks file without the ids the editor gives its blocks.</summary>
        private static string BlocksHash(string json)
        {
            try
            {
                Newtonsoft.Json.Linq.JToken t = Newtonsoft.Json.Linq.JToken.Parse(json);
                Strip(t);
                return Hash(t.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (System.Exception) { return ""; }
        }

        private static void Strip(Newtonsoft.Json.Linq.JToken t)
        {
            if (t is Newtonsoft.Json.Linq.JObject o) { o.Remove("u"); foreach (var p in System.Linq.Enumerable.ToList(o.Properties())) Strip(p.Value); }
            else if (t is Newtonsoft.Json.Linq.JArray a) foreach (var x in a) Strip(x);
        }

        private static string Hash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return System.BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Norm(text)))).Replace("-", "").ToLowerInvariant();
        }

        // block examples written before the record existed, that are still replaced while they are exactly as written (the first Wild Hunt)
        private static readonly System.Collections.Generic.HashSet<string> Earlier = new System.Collections.Generic.HashSet<string>
        {
            "cb476df1da9c3e9de811ea7dbebbce1e9740cab6",
            "3cfcf70494d5db7d4cc5944b6bd5811a29a762a6",   // (the same file as the installer hashes it: line ends and the last newline left out)
        };

        private static string Compile(Blocks.Program p) => Blocks.Compiler.Compile(p).Js ?? "";
        private static bool Same(string a, string b) => Norm(a) == Norm(b);
        private static string Norm(string s) => s.Replace((char)13 + "" + (char)10, "" + (char)10).TrimEnd();
    }

    /// <summary>The powers the player allowed, per script file: a text file of "file|power" lines, next to the scripts.</summary>
    internal static class Approvals
    {
        private static string PathFor => Path.Combine(Host.Folder, "approvals.txt");

        internal static void Load()
        {
            if (!File.Exists(PathFor)) return;
            foreach (string line in File.ReadAllLines(PathFor))
            {
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                string file = line.Substring(0, bar), cap = line.Substring(bar + 1);
                Script s = Host.Scripts.Find(x => x.File == file);
                if (s == null) { s = new Script { File = file }; Host.Reload(s); Host.Scripts.Add(s); }
                s.Approved.Add(cap);
            }
        }

        internal static void Save()
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (Script s in Host.Scripts) foreach (string cap in s.Approved) lines.Add(s.File + "|" + cap);
            File.WriteAllLines(PathFor, lines.ToArray());
        }
    }
}
