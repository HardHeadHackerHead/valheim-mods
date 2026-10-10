using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// The game's own code, read from its DLLs (assembly_valheim and friends): every type, field and method with its real signature, who
    /// calls each method, who reads and writes each field, and a fingerprint of each method's code. So an assistant can look up what really
    /// exists instead of guessing names, and so a game update can be compared with the version before (which methods changed or vanished,
    /// and which mods patch them).
    /// </summary>
    internal class GameIndex
    {
        public class Method
        {
            public string Type, Name, Signature, Key;
            public bool Static;
            public ulong Hash;
            public List<string> Calls = new List<string>();     // "Type.Method" of what it calls (the game's own)
        }

        public class Field
        {
            public string Type, Name, FieldType;
            public bool Static;
        }

        public class TypeInfo
        {
            public string Name, Base, Kind;
            public List<Field> Fields = new List<Field>();
            public List<Method> Methods = new List<Method>();
        }

        public string Version = "", Mvid = "";
        public readonly Dictionary<string, TypeInfo> Types = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Callers = new Dictionary<string, List<string>>(StringComparer.Ordinal); // "Type.Method" -> callers
        public readonly Dictionary<string, List<string>> Readers = new Dictionary<string, List<string>>(StringComparer.Ordinal); // "Type.field" -> methods
        public readonly Dictionary<string, List<string>> Writers = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public static readonly string[] Assemblies = { "assembly_valheim.dll", "assembly_utils.dll", "assembly_guiutils.dll" };

        /// <param name="version">The game's version; null reads it from the game's own code (Version.CurrentVersion).</param>
        public static GameIndex Build(string managedDir, string version = null)
        {
            var index = new GameIndex { Version = version };
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDir);
            var mvids = new List<string>();
            try
            {
                foreach (string file in Assemblies.Select(a => Path.Combine(managedDir, a)).Where(File.Exists))
                {
                    using (var stream = new MemoryStream(File.ReadAllBytes(file)))
                    using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { AssemblyResolver = resolver }))
                    {
                        mvids.Add(asm.MainModule.Mvid.ToString("N").Substring(0, 8));
                        foreach (TypeDefinition t in asm.MainModule.Types.SelectMany(All).Where(t => !t.Name.StartsWith("<")))
                            index.Add(t);
                    }
                }
            }
            finally { resolver.Dispose(); }
            index.Mvid = string.Join("-", mvids.ToArray());
            if (string.IsNullOrEmpty(index.Version)) index.Version = index._readVersion ?? "unknown";
            return index;
        }

        /// <summary>The game's version and build id (as Build gives them), cheaply: only the assemblies' ids and the Version type are read.</summary>
        public static void Identify(string managedDir, out string version, out string mvid)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDir);
            var mvids = new List<string>();
            version = null;
            try
            {
                foreach (string file in Assemblies.Select(a => Path.Combine(managedDir, a)).Where(File.Exists))
                {
                    using (var stream = new MemoryStream(File.ReadAllBytes(file)))
                    using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { AssemblyResolver = resolver }))
                    {
                        mvids.Add(asm.MainModule.Mvid.ToString("N").Substring(0, 8));
                        TypeDefinition v = asm.MainModule.GetType("Version");
                        if (v != null && version == null) version = ReadVersion(v);
                    }
                }
            }
            finally { resolver.Dispose(); }
            mvid = string.Join("-", mvids.ToArray());
            version = version ?? "unknown";
        }

        private static IEnumerable<TypeDefinition> All(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(All));

        private string _readVersion;

        private void Add(TypeDefinition t)
        {
            string name = Scan.Name(t);
            if (name == "Version") _readVersion = ReadVersion(t) ?? _readVersion;
            var info = new TypeInfo
            {
                Name = name, Base = t.BaseType != null ? Scan.Name(t.BaseType) : null,
                Kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class",
            };
            foreach (FieldDefinition f in t.Fields.Where(f => !f.Name.StartsWith("<")))
                info.Fields.Add(new Field { Type = name, Name = f.Name, FieldType = Short(f.FieldType), Static = f.IsStatic });
            foreach (MethodDefinition m in t.Methods)
            {
                var method = new Method
                {
                    Type = name, Name = m.Name, Static = m.IsStatic,
                    Signature = $"{(m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsAssembly ? "internal" : "private")} {(m.IsStatic ? "static " : "")}" +
                                $"{Short(m.ReturnType)} {m.Name}({string.Join(", ", m.Parameters.Select(p => (p.ParameterType.IsByReference ? (p.IsOut ? "out " : "ref ") : "") + Short(p.ParameterType) + " " + p.Name).ToArray())})",
                    Key = name + "." + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName).ToArray()) + ")",
                };
                string self = name + "." + m.Name;
                if (m.HasBody)
                {
                    method.Hash = Fingerprint(m);
                    // iterators and async methods keep their code in a compiler-made class: count its calls as the method's own
                    IEnumerable<Instruction> code = m.Body.Instructions;
                    foreach (Instruction i in code)
                    {
                        if (i.Operand is MethodReference called && (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj))
                        {
                            string target = Scan.Name(called.DeclaringType) + "." + called.Name;
                            if (!method.Calls.Contains(target)) method.Calls.Add(target);
                            Link(Callers, target, self);
                        }
                        else if (i.Operand is FieldReference f)
                        {
                            string target = Scan.Name(f.DeclaringType) + "." + f.Name;
                            if (i.OpCode == OpCodes.Stfld || i.OpCode == OpCodes.Stsfld) Link(Writers, target, self);
                            else if (i.OpCode == OpCodes.Ldflda || i.OpCode == OpCodes.Ldsflda) { Link(Readers, target, self); Link(Writers, target, self + " (by reference)"); }
                            else Link(Readers, target, self);
                        }
                    }
                }
                info.Methods.Add(method);
            }
            Types[name] = info;
        }

        /// <summary>The game's version from Version's static constructor: CurrentVersion = new GameVersion(major, minor, patch).</summary>
        private static string ReadVersion(TypeDefinition version)
        {
            MethodDefinition cctor = version.Methods.FirstOrDefault(m => m.Name == ".cctor" && m.HasBody);
            if (cctor == null) return null;
            IList<Instruction> code = cctor.Body.Instructions;
            for (int i = 3; i < code.Count; i++)
            {
                bool store = (code[i].OpCode == OpCodes.Stsfld && code[i].Operand is FieldReference f && f.Name.Contains("CurrentVersion"))
                          || (code[i].OpCode == OpCodes.Call && code[i].Operand is MethodReference m && m.Name == "set_CurrentVersion");
                if (!store || code[i - 1].OpCode != OpCodes.Newobj) continue;
                int?[] parts = { Int(code[i - 4]), Int(code[i - 3]), Int(code[i - 2]) };
                if (parts.All(x => x.HasValue)) return string.Join(".", parts.Select(x => x.Value.ToString()).ToArray());
            }
            return null;
        }

        private static int? Int(Instruction i)
        {
            if (i.OpCode == OpCodes.Ldc_I4_S || i.OpCode == OpCodes.Ldc_I4) return Convert.ToInt32(i.Operand);
            if (i.OpCode.Code >= Code.Ldc_I4_0 && i.OpCode.Code <= Code.Ldc_I4_8) return i.OpCode.Code - Code.Ldc_I4_0;
            return null;
        }

        private static void Link(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out List<string> list)) map[key] = list = new List<string>();
            if (!list.Contains(value)) list.Add(value);
        }

        /// <summary>A type's name as code writes it: no namespace for System and Unity types, generics as List&lt;ItemData&gt;.</summary>
        public static string Short(TypeReference t)
        {
            if (t is ByReferenceType br) return Short(br.ElementType);
            if (t is ArrayType at) return Short(at.ElementType) + "[]";
            if (t is GenericInstanceType g)
            {
                string n = g.ElementType.Name;
                int tick = n.IndexOf('`');
                return (tick >= 0 ? n.Substring(0, tick) : n) + "<" + string.Join(", ", g.GenericArguments.Select(Short).ToArray()) + ">";
            }
            switch (t.FullName)
            {
                case "System.Void": return "void";
                case "System.Boolean": return "bool";
                case "System.Int32": return "int";
                case "System.Int64": return "long";
                case "System.Single": return "float";
                case "System.Double": return "double";
                case "System.String": return "string";
                case "System.Object": return "object";
                case "System.Byte": return "byte";
                case "System.UInt32": return "uint";
            }
            return t.IsNested ? Short(t.DeclaringType) + "." + t.Name : t.Name;
        }

        /// <summary>
        /// A fingerprint of a method's code that survives a plain rebuild (metadata tokens and offsets change, the code doesn't): op codes,
        /// what they refer to by name, constants, and branches as distances.
        /// </summary>
        private static ulong Fingerprint(MethodDefinition m)
        {
            ulong h = 14695981039346656037UL;
            void Mix(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } h ^= 0xFF; h *= 1099511628211UL; }
            IList<Instruction> code = m.Body.Instructions;
            var at = new Dictionary<Instruction, int>();
            for (int k = 0; k < code.Count; k++) at[code[k]] = k;
            for (int k = 0; k < code.Count; k++)
            {
                Instruction i = code[k];
                Mix(i.OpCode.Name);
                switch (i.Operand)
                {
                    case null: break;
                    case MemberReference r: Mix(r.FullName); break;
                    case Instruction target: Mix((at.TryGetValue(target, out int t) ? t - k : 0).ToString()); break;
                    case Instruction[] targets: foreach (Instruction x in targets) Mix((at.TryGetValue(x, out int t2) ? t2 - k : 0).ToString()); break;
                    case VariableDefinition v: Mix("v" + v.Index); break;
                    case ParameterDefinition p: Mix("p" + p.Index); break;
                    default: Mix(Convert.ToString(i.Operand, System.Globalization.CultureInfo.InvariantCulture)); break;
                }
            }
            return h;
        }

        // ---- looking things up ----

        public IEnumerable<Method> AllMethods => Types.Values.SelectMany(t => t.Methods);

        /// <summary>Methods named Type.Method (all overloads); the type can be the part after the last dot of a nested type.</summary>
        public List<Method> Find(string typeDotMethod)
        {
            int dot = typeDotMethod.LastIndexOf('.');
            if (dot <= 0) return new List<Method>();
            string type = typeDotMethod.Substring(0, dot), name = typeDotMethod.Substring(dot + 1);
            return Types.Values.Where(t => t.Name == type || t.Name.EndsWith("." + type)).SelectMany(t => t.Methods).Where(m => m.Name == name).ToList();
        }

        public TypeInfo Type(string name) =>
            Types.TryGetValue(name, out TypeInfo t) ? t : Types.Values.FirstOrDefault(x => x.Name.EndsWith("." + name, StringComparison.Ordinal))
            ?? Types.Values.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The game command: game find &lt;text&gt;, game &lt;Type&gt;, game &lt;Type.Member&gt; (a[0] is the command's name).</summary>
        public static JObject Lookup(GameIndex g, string[] a, List<Compat.Use> library, string managedDir)
        {
            if (a[1].Equals("find", StringComparison.OrdinalIgnoreCase))
            {
                string text = string.Join(" ", a.Skip(2).ToArray());
                bool Has(string s) => s.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
                return new JObject
                {
                    ["types"] = new JArray(g.Types.Values.Where(t => Has(t.Name)).Take(40).Select(t => $"{t.Kind} {t.Name}" + (t.Base != null && t.Base != "System.Object" ? " : " + t.Base : ""))),
                    ["methods"] = new JArray(g.AllMethods.Where(m => Has(m.Name)).Take(60).Select(m => $"{m.Type}: {m.Signature}")),
                    ["fields"] = new JArray(g.Types.Values.SelectMany(t => t.Fields).Where(f => Has(f.Name)).Take(60).Select(f => $"{f.Type}.{f.Name}: {(f.Static ? "static " : "")}{f.FieldType}")),
                };
            }
            string q = string.Join(" ", a.Skip(1).ToArray());
            TypeInfo type = g.Type(q);
            if (type != null)
                return new JObject
                {
                    ["type"] = $"{type.Kind} {type.Name}" + (type.Base != null ? " : " + type.Base : ""), ["version"] = g.Version,
                    ["fields"] = new JArray(type.Fields.Take(250).Select(f => $"{(f.Static ? "static " : "")}{f.FieldType} {f.Name}")),
                    ["methods"] = new JArray(type.Methods.Take(250).Select(m => m.Signature)),
                    ["read the code"] = $"ilspycmd -t {type.Name.Replace('.', '+')} \"{Path.Combine(managedDir, "assembly_valheim.dll")}\"",
                };
            List<Method> methods = g.Find(q);
            if (methods.Count > 0)
            {
                string key = methods[0].Type + "." + methods[0].Name;
                List<Compat.Use> patched = library.Where(u => u.Target == key || u.Target == key + " (enumerator)").ToList();
                return new JObject
                {
                    ["method"] = key, ["system"] = Compat.SystemOf(key), ["version"] = g.Version,
                    ["overloads"] = new JArray(methods.Select(m => m.Signature)),
                    ["calledBy"] = new JArray((g.Callers.TryGetValue(key, out var c) ? c : new List<string>()).Take(60)),
                    ["calls"] = new JArray(methods.SelectMany(m => m.Calls).Distinct().Where(x => !x.StartsWith("System.") && !x.StartsWith("UnityEngine.Debug")).Take(80)),
                    ["patchedBy (library)"] = new JArray(patched.Select(u => $"{u.Mod} ({u.From}): {u.Describe()}").Distinct().Take(40)),
                    ["note"] = "Mods installed here that patch it: who " + key,
                    ["read the code"] = $"ilspycmd -t {methods[0].Type.Replace('.', '+')} \"{Path.Combine(managedDir, "assembly_valheim.dll")}\" (then find {methods[0].Name})",
                };
            }
            int dot = q.LastIndexOf('.');
            TypeInfo owner = dot > 0 ? g.Type(q.Substring(0, dot)) : null;
            Field field = owner?.Fields.FirstOrDefault(f => f.Name == q.Substring(dot + 1));
            if (field != null)
            {
                string key = field.Type + "." + field.Name;
                return new JObject
                {
                    ["field"] = $"{(field.Static ? "static " : "")}{field.FieldType} {key}",
                    ["changedBy"] = new JArray((g.Writers.TryGetValue(key, out var w) ? w : new List<string>()).Take(80)),
                    ["readBy"] = new JArray((g.Readers.TryGetValue(key, out var r) ? r : new List<string>()).Take(80)),
                };
            }
            return new JObject { ["notFound"] = q, ["try"] = "game find " + (dot > 0 ? q.Substring(dot + 1) : q) };
        }

        // ---- comparing game versions ----

        public JObject Snapshot() => new JObject
        {
            ["version"] = Version, ["mvid"] = Mvid, ["made"] = DateTime.Now.ToString("s"),
            ["methods"] = new JObject(AllMethods.GroupBy(m => m.Key).Select(g => new JProperty(g.Key, g.First().Hash.ToString("x16")))),
        };

        /// <summary>
        /// What changed between two snapshots, by "Type.Method": gone (no method of that name any more), signature (an overload's
        /// parameters changed), changed (same signature, different code), and how many are new.
        /// </summary>
        public static JObject Diff(JObject older, JObject newer)
        {
            var a = ((JObject)older["methods"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            var b = ((JObject)newer["methods"]).Properties().ToDictionary(p => p.Name, p => (string)p.Value);
            string NameOf(string key) => key.Substring(0, key.IndexOf('('));
            var namesB = new HashSet<string>(b.Keys.Select(NameOf));
            var namesA = new HashSet<string>(a.Keys.Select(NameOf));
            var gone = new SortedSet<string>(); var signature = new SortedSet<string>(); var changed = new SortedSet<string>();
            foreach (var kv in a)
            {
                string name = NameOf(kv.Key);
                if (!namesB.Contains(name)) gone.Add(name);
                else if (!b.TryGetValue(kv.Key, out string hash)) signature.Add(name);
                else if (hash != kv.Value) changed.Add(name);
            }
            changed.ExceptWith(signature);
            return new JObject
            {
                ["from"] = older["version"], ["to"] = newer["version"],
                ["gone"] = new JArray(gone), ["signature"] = new JArray(signature), ["changed"] = new JArray(changed),
                ["new"] = namesB.Count(n => !namesA.Contains(n)),
            };
        }

        public static void Save(JObject snapshot, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream f = File.Create(path))
            using (var gz = new System.IO.Compression.GZipStream(f, System.IO.Compression.CompressionLevel.Optimal))
            {
                byte[] data = Encoding.UTF8.GetBytes(snapshot.ToString(Newtonsoft.Json.Formatting.None));
                gz.Write(data, 0, data.Length);
            }
        }

        public static JObject Load(string path)
        {
            using (FileStream f = File.OpenRead(path))
            using (var gz = new System.IO.Compression.GZipStream(f, System.IO.Compression.CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
                return JObject.Parse(reader.ReadToEnd());
        }
    }
}
