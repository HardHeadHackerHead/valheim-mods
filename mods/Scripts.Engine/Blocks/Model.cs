using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Scripts.Blocks
{
    /// <summary>
    /// A block program: what the block editor edits and keeps (docs/scripting-design.md, "The block editor"). A list of stacks on a
    /// workspace; a stack is a hat block ("when ...") with the blocks under it as its body, or loose blocks that do nothing until they are
    /// joined to a hat. Stored as JSON, compiled to JavaScript by Compiler, which runs in the same sandbox as typed code.
    /// </summary>
    internal sealed class Program
    {
        internal const int Format = 1, MaxBlocks = 1500, MaxDepth = 24, MaxVars = 60, MaxTops = 100, MaxFields = 12;

        internal string Name = "My script", About = "";
        internal readonly List<string> Vars = new List<string>();
        internal readonly List<Top> Tops = new List<Top>();

        /// <summary>One stack on the workspace: its place, and its blocks from the top (a hat first, if it has one).</summary>
        internal sealed class Top
        {
            /// <summary>A stable id for live co-editing (docs/scripting-collab.md): empty until a collaboration session assigns it, kept by moves, undo and saves.</summary>
            internal string Uid = "";
            internal float X, Y;
            internal bool Collapsed;   // the editor shows only its first line (a view setting; the compiler ignores it)
            internal readonly List<Block> Chain = new List<Block>();
        }

        /// <summary>Every block in the program, inside bodies and sockets too.</summary>
        internal IEnumerable<Block> AllBlocks()
        {
            foreach (Top t in Tops) foreach (Block b in t.Chain) foreach (Block x in b.All()) yield return x;
        }

        private List<string> Names(Func<Block, IEnumerable<string>> pick) =>
            AllBlocks().SelectMany(pick).Select(Naming.Word).Where(n => n != null).Distinct().ToList();

        /// <summary>The my-blocks that are defined, the names of their inputs, the local variables and the signals the program uses.</summary>
        internal List<string> Procedures() => Names(b => b.Type == "define" ? new[] { b.Field("name") } : new string[0]);
        internal List<string> Params() => Names(b => b.Type == "define" ? new[] { b.Field("p1"), b.Field("p2"), b.Field("p3"), b.Field("p4") } : new string[0]);
        internal List<string> Locals() => Names(b => b.Type == "local_var" ? new[] { b.Field("name") } : new string[0]);
        internal List<string> Signals() => Names(b => b.Type == "on_signal" || b.Type == "send_signal" ? new[] { b.Field("name") } : new string[0]);

        internal int CountBlocks()
        {
            int n = 0;
            foreach (Top t in Tops) foreach (Block b in t.Chain) n += b.CountAll();
            return n;
        }

        // ---- reading and writing ----

        internal string ToJson()
        {
            var o = new JObject
            {
                ["format"] = Format, ["api"] = 1, ["name"] = Name, ["about"] = About,
                ["vars"] = new JArray(Vars.Cast<object>().ToArray()),
                ["tops"] = new JArray(Tops.Select(t => TopJson(t)).Cast<object>().ToArray()),
            };
            return o.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>One stack as JSON (also what a collaboration "addTop" carries). The id is written only when there is one.</summary>
        internal static JObject TopJson(Top t)
        {
            var o = new JObject();
            if (t.Uid.Length > 0) o["u"] = t.Uid;
            o["x"] = Math.Round(t.X); o["y"] = Math.Round(t.Y);
            if (t.Collapsed) o["collapsed"] = true;   // (only written when set, so a file without it stays the same)
            o["blocks"] = new JArray(t.Chain.Select(b => b.ToJson()).Cast<object>().ToArray());
            return o;
        }

        /// <summary>A stack from JSON, or null when it has no blocks.</summary>
        internal static Top TopFromJson(JToken t)
        {
            if (t == null) return null;
            var top = new Top { X = (float)(t.Value<double?>("x") ?? 0), Y = (float)(t.Value<double?>("y") ?? 0), Collapsed = t.Value<bool?>("collapsed") ?? false, Uid = Naming.Uid(t.Value<string>("u")) };
            if (t["blocks"] is JArray blocks) foreach (JToken b in blocks) { Block block = Block.FromJson(b as JObject, 0); if (block != null) top.Chain.Add(block); }
            return top.Chain.Count > 0 ? top : null;
        }

        /// <summary>Reads a program. Unknown block types and loose values are kept so the editor can show them in red; nothing here runs them.</summary>
        internal static Program FromJson(string json, out string error)
        {
            error = null;
            var p = new Program();
            try
            {
                JObject o = JObject.Parse(json);
                if (o.Value<int?>("format") is int f && f > Format) { error = "This blocks file is from a newer version."; return null; }
                p.Name = Clip(o.Value<string>("name") ?? "My script", 40);
                p.About = Clip(o.Value<string>("about") ?? "", 200);
                if (o["vars"] is JArray vars) foreach (JToken v in vars) { string s = v.Type == JTokenType.String ? Naming.Var((string)v) : null; if (s != null && !p.Vars.Contains(s) && p.Vars.Count < MaxVars) p.Vars.Add(s); }
                if (o["tops"] is JArray tops)
                    foreach (JToken t in tops.Take(MaxTops))
                    {
                        Top top = TopFromJson(t);
                        if (top != null) p.Tops.Add(top);
                    }
                return p;
            }
            catch (Exception e)
            {
                error = "That is not a blocks file: " + e.Message;
                return null;
            }
        }

        internal static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        /// <summary>A copy (the editor's undo keeps copies as text).</summary>
        internal Program Copy() => FromJson(ToJson(), out _);
    }

    /// <summary>One block in a program: its type, the values typed into its slots, the blocks plugged into its sockets, and its bodies.</summary>
    internal sealed class Block
    {
        private static int _nextId = 1;

        internal string Type = "";
        /// <summary>A stable id for live co-editing (docs/scripting-collab.md): empty until a collaboration session assigns it. A clone starts with none (it is a new block); moves, undo and saves keep it.</summary>
        internal string Uid = "";
        /// <summary>A number the editor can name this block by (to point at it in an error); not saved.</summary>
        internal readonly int Id = _nextId++;
        /// <summary>Typed values: numbers, text and dropdown choices, by slot key.</summary>
        internal readonly Dictionary<string, string> Fields = new Dictionary<string, string>();
        /// <summary>Blocks plugged into sockets, by slot key.</summary>
        internal readonly Dictionary<string, Block> Inputs = new Dictionary<string, Block>();
        /// <summary>The blocks inside a C-shaped block or a hat, by body key ("do", "else").</summary>
        internal readonly Dictionary<string, List<Block>> Bodies = new Dictionary<string, List<Block>>();

        /// <summary>The names a block's inside parts can have ("do2" and "do3" are the extra branches of a chain).</summary>
        internal static readonly string[] BodyKeys = { "do", "do2", "do3", "else" };

        internal Block(string type) { Type = type; }

        internal BlockDef Def => Defs.Find(Type);

        internal string Field(string key, string fallback = "") => Fields.TryGetValue(key, out string v) ? v : fallback;

        internal List<Block> Body(string key)
        {
            if (!Bodies.TryGetValue(key, out List<Block> list)) Bodies[key] = list = new List<Block>();
            return list;
        }

        internal int CountAll()
        {
            int n = 1;
            foreach (Block b in Inputs.Values) n += b.CountAll();
            foreach (List<Block> list in Bodies.Values) foreach (Block b in list) n += b.CountAll();
            return n;
        }

        /// <summary>This block and everything inside it, one at a time.</summary>
        internal IEnumerable<Block> All()
        {
            yield return this;
            foreach (Block i in Inputs.Values) foreach (Block x in i.All()) yield return x;
            foreach (List<Block> list in Bodies.Values) foreach (Block b in list) foreach (Block x in b.All()) yield return x;
        }

        internal Block Clone()
        {
            var c = new Block(Type);
            foreach (var kv in Fields) c.Fields[kv.Key] = kv.Value;
            foreach (var kv in Inputs) c.Inputs[kv.Key] = kv.Value.Clone();
            foreach (var kv in Bodies) c.Bodies[kv.Key] = kv.Value.Select(b => b.Clone()).ToList();
            return c;
        }

        internal JObject ToJson()
        {
            var o = new JObject { ["type"] = Type };
            if (Uid.Length > 0) o["u"] = Uid;
            if (Fields.Count > 0) o["f"] = new JObject(Fields.Select(kv => new JProperty(kv.Key, kv.Value)));
            if (Inputs.Count > 0) o["in"] = new JObject(Inputs.Select(kv => new JProperty(kv.Key, kv.Value.ToJson())));
            foreach (var kv in Bodies) if (kv.Value.Count > 0) o[kv.Key] = new JArray(kv.Value.Select(b => b.ToJson()).Cast<object>().ToArray());
            return o;
        }

        internal static Block FromJson(JObject o, int depth)
        {
            if (o == null || depth > Program.MaxDepth + 4) return null;
            string type = o.Value<string>("type");
            if (string.IsNullOrEmpty(type) || type.Length > 40) return null;
            var b = new Block(type) { Uid = Naming.Uid(o.Value<string>("u")) };
            if (o["f"] is JObject f) foreach (JProperty p in f.Properties().Take(Program.MaxFields)) b.Fields[p.Name] = Program.Clip(p.Value.Type == JTokenType.Null ? "" : p.Value.ToString(), 200);
            if (o["in"] is JObject ins) foreach (JProperty p in ins.Properties().Take(Program.MaxFields)) { Block child = FromJson(p.Value as JObject, depth + 1); if (child != null) b.Inputs[p.Name] = child; }
            foreach (string key in BodyKeys)
                if (o[key] is JArray list) foreach (JToken t in list) { Block child = FromJson(t as JObject, depth + 1); if (child != null) b.Body(key).Add(child); }
            return b;
        }
    }

    internal static class Naming
    {
        /// <summary>An id read from a file or the wire (docs/scripting-collab.md): 1 to 16 characters of 0-9 and a-z, else "" (none).</summary>
        internal static string Uid(string u)
        {
            if (string.IsNullOrEmpty(u) || u.Length > 16) return "";
            foreach (char c in u) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z'))) return "";
            return u;
        }

        /// <summary>A variable's name as the player typed it, cleaned to letters, digits and spaces (the compiler makes the identifier).</summary>
        internal static string Var(string name)
        {
            if (name == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim()) if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_') sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? null : (s.Length > 20 ? s.Substring(0, 20) : s);
        }

        /// <summary>The identifier a variable gets in the generated code: always "v_" and hex of its characters, so no name can ever be code.</summary>
        internal static string VarId(string name) => Hex("v_", name);

        /// <summary>The same for the other kinds of name (my blocks, their inputs, local variables), each with its own prefix so none can clash.</summary>
        internal static string ProcId(string name) => Hex("p_", name);
        internal static string ParamId(string name) => Hex("a_", name);
        internal static string LocalId(string name) => Hex("l_", name);

        private static string Hex(string prefix, string name)
        {
            var sb = new System.Text.StringBuilder(prefix);
            foreach (char c in name) sb.Append(((int)c).ToString("x"));
            return sb.ToString();
        }

        /// <summary>A name typed for a signal, a my-block or a local variable: letters, digits and spaces only, up to 30 characters, or null.</summary>
        internal static string Word(string name)
        {
            if (name == null) return null;
            var sb = new System.Text.StringBuilder();
            foreach (char c in name.Trim()) if (char.IsLetterOrDigit(c) || c == ' ') sb.Append(c);
            string s = sb.ToString().Trim();
            return s.Length == 0 ? null : (s.Length > 30 ? s.Substring(0, 30) : s);
        }
    }
}
