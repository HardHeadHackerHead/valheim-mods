using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json.Linq;

namespace ClaudeTools
{
    /// <summary>
    /// Reads a mod's DLL (never loads or runs it) and lists what it changes in the game: its Harmony patches (from their attributes, and
    /// the targets it names near hand-made Harmony.Patch calls) and its MonoMod hooks (On.X.Method += ...). For each patch: the game method,
    /// prefix/postfix/transpiler/finalizer, whether a prefix can skip the game's method (never, sometimes, always), the game method's
    /// arguments it can change (ref parameters), whether it can change the result, its priority, and where the patch lives in the mod.
    /// </summary>
    internal static class Scan
    {
        /// <summary>Everything found in one DLL: its BepInEx plugins and their patches.</summary>
        public static JObject Dll(string path, IEnumerable<string> searchDirs)
        {
            var result = new JObject { ["dll"] = Path.GetFileName(path) };
            var plugins = new JArray();
            var patches = new JArray();
            var errors = new JArray();
            var resolver = new DefaultAssemblyResolver();
            foreach (string dir in searchDirs.Concat(new[] { Path.GetDirectoryName(path) }).Where(Directory.Exists).Distinct()) resolver.AddSearchDirectory(dir);
            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(path))) // (read into memory: the file is never locked)
                using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { AssemblyResolver = resolver }))
                {
                    result["assembly"] = asm.Name.Name;
                    foreach (TypeDefinition type in asm.MainModule.Types.SelectMany(AllTypes))
                    {
                        // (each step on its own: one unreadable attribute must not lose the type's patches)
                        try { Plugin(type, plugins); } catch (Exception e) { errors.Add($"{type.FullName} (plugin): {e.Message}"); }
                        try { AttributePatches(type, patches); } catch (Exception e) { errors.Add($"{type.FullName} (patches): {e.Message}"); }
                        foreach (MethodDefinition m in type.Methods.Where(m => m.HasBody))
                            try { CodePatches(m, patches); } catch (Exception e) { errors.Add($"{type.FullName}.{m.Name}: {e.Message}"); }
                    }
                }
            }
            catch (BadImageFormatException) { result["native"] = true; } // a native DLL (no .NET code in it)
            catch (Exception e) { errors.Add(e.Message); }
            finally { resolver.Dispose(); }
            result["plugins"] = plugins;
            result["patches"] = patches;
            if (errors.Count > 0) result["errors"] = errors;
            return result;
        }

        /// <summary>
        /// Write a DLL without its embedded resources (asset bundles, sounds): code only, still readable by decompilers. Content mods carry
        /// 100+ MB of assets inside their DLL; the code is often under a megabyte. False for a native DLL (no .NET code).
        /// </summary>
        public static bool Slim(byte[] data, string path, IEnumerable<string> searchDirs)
        {
            var resolver = new DefaultAssemblyResolver();
            foreach (string d in searchDirs.Where(Directory.Exists)) resolver.AddSearchDirectory(d);
            try
            {
                using (var stream = new MemoryStream(data))
                using (AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(stream, new ReaderParameters { AssemblyResolver = resolver }))
                {
                    asm.MainModule.Resources.Clear();
                    asm.Write(path);
                }
                return true;
            }
            catch (BadImageFormatException) { return false; }
            catch (Exception)
            {
                File.WriteAllBytes(path, data); // could not rewrite it: keep it whole
                return true;
            }
            finally { resolver.Dispose(); }
        }

        private static IEnumerable<TypeDefinition> AllTypes(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(AllTypes));

        // ---- the plugin itself ----

        private static void Plugin(TypeDefinition type, JArray plugins)
        {
            CustomAttribute info = Attr(type, "BepInEx.BepInPlugin");
            if (info == null) return;
            var p = new JObject
            {
                ["guid"] = Arg(info, 0), ["name"] = Arg(info, 1), ["version"] = Arg(info, 2), ["class"] = Name(type),
            };
            var deps = new JArray();
            var incompatible = new JArray();
            foreach (CustomAttribute a in type.CustomAttributes)
            {
                string n = a.AttributeType.FullName;
                if (n == "BepInEx.BepInDependency")
                {
                    // (guid), (guid, DependencyFlags) or (guid, "minimum version")
                    var dep = new JObject { ["guid"] = Arg(a, 0) };
                    object second = a.ConstructorArguments.Count > 1 ? a.ConstructorArguments[1].Value : null;
                    dep["soft"] = second != null && !(second is string) && Convert.ToInt32(second) == 2;
                    if (second is string version) dep["minVersion"] = version;
                    deps.Add(dep);
                }
                else if (n == "BepInEx.BepInIncompatibility") incompatible.Add(Arg(a, 0));
            }
            if (deps.Count > 0) p["dependencies"] = deps;
            if (incompatible.Count > 0) p["incompatible"] = incompatible;
            plugins.Add(p);
        }

        // ---- [HarmonyPatch] classes and methods ----

        private static readonly string[] Kinds = { "Prefix", "Postfix", "Transpiler", "Finalizer", "ILManipulator" };

        private static void AttributePatches(TypeDefinition type, JArray patches)
        {
            Target classTarget = TargetOf(type.CustomAttributes);
            int? classPriority = Priority(type.CustomAttributes);
            foreach (MethodDefinition m in type.Methods)
            {
                string kind = KindOf(m);
                if (kind == null) continue;
                Target t = classTarget.With(TargetOf(m.CustomAttributes));
                if (t.Type == null && t.Method == null)
                {
                    // the target comes from a TargetMethod()/TargetMethods() method: take what it names
                    MethodDefinition finder = type.Methods.FirstOrDefault(x => x.Name == "TargetMethod" || x.Name == "TargetMethods" || Attr(x, "HarmonyLib.HarmonyTargetMethod") != null || Attr(x, "HarmonyLib.HarmonyTargetMethods") != null);
                    if (finder == null) continue; // not a patch at all
                    t = Named(finder) ?? new Target { Method = "(chosen in code)" };
                    t.Computed = true;
                }
                patches.Add(Describe(t, kind, m, Priority(m.CustomAttributes) ?? classPriority));
            }
        }

        private static string KindOf(MethodDefinition m)
        {
            foreach (string k in Kinds) if (Attr(m, "HarmonyLib.Harmony" + k) != null) return k.ToLowerInvariant();
            bool patchClass = Attr(m.DeclaringType, "HarmonyLib.HarmonyPatch") != null || Attr(m, "HarmonyLib.HarmonyPatch") != null;
            return patchClass && m.IsStatic && Kinds.Contains(m.Name) ? m.Name.ToLowerInvariant() : null;
        }

        private class Target
        {
            public string Type, Method, MethodType, Args;
            public bool Computed;

            public Target With(Target more) => new Target
            {
                Type = more.Type ?? Type, Method = more.Method ?? Method, MethodType = more.MethodType ?? MethodType, Args = more.Args ?? Args,
            };

            /// <summary>The game method as "Type.Method" (constructors .ctor, properties get_X/set_X, iterators "Method (enumerator)").</summary>
            public string Full()
            {
                string method = Method ?? "";
                switch (MethodType)
                {
                    case "Getter": method = "get_" + method; break;
                    case "Setter": method = "set_" + method; break;
                    case "Constructor": method = ".ctor"; break;
                    case "StaticConstructor": method = ".cctor"; break;
                    case "Enumerator": method += " (enumerator)"; break;
                }
                return (Type ?? "?") + "." + method;
            }
        }

        private static readonly string[] MethodTypes = { "Normal", "Getter", "Setter", "Constructor", "StaticConstructor", "Enumerator", "Async" };

        /// <summary>What [HarmonyPatch(...)] attributes name: a type, a method name, a method type, argument types (several attributes add up).</summary>
        private static Target TargetOf(IEnumerable<CustomAttribute> attrs)
        {
            var t = new Target();
            foreach (CustomAttribute a in attrs.Where(x => x.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
            {
                var strings = new List<string>();
                foreach (CustomAttributeArgument arg in a.ConstructorArguments)
                {
                    if (arg.Value is TypeReference tr) t.Type = Name(tr);
                    else if (arg.Value is string s) strings.Add(s);
                    else if (arg.Value is CustomAttributeArgument[] arr && arr.Length > 0 && arr[0].Value is TypeReference)
                        t.Args = "(" + string.Join(", ", arr.Select(x => x.Value is TypeReference r ? r.Name : "?").ToArray()) + ")";
                    else if (arg.Type.FullName == "HarmonyLib.MethodType")
                    {
                        int i = Convert.ToInt32(arg.Value);
                        t.MethodType = i >= 0 && i < MethodTypes.Length ? MethodTypes[i] : i.ToString();
                    }
                }
                if (strings.Count == 1) t.Method = strings[0];
                else if (strings.Count >= 2) { t.Type = t.Type ?? strings[0].Split(',')[0]; t.Method = strings[1]; } // ("TypeName", "Method")
            }
            return t;
        }

        private static int? Priority(IEnumerable<CustomAttribute> attrs)
        {
            CustomAttribute a = attrs.FirstOrDefault(x => x.AttributeType.FullName == "HarmonyLib.HarmonyPriority");
            return a != null && a.ConstructorArguments.Count > 0 ? Convert.ToInt32(a.ConstructorArguments[0].Value) : (int?)null;
        }

        private static JObject Describe(Target t, string kind, MethodDefinition m, int? priority)
        {
            var p = new JObject { ["target"] = t.Full(), ["kind"] = kind };
            if (t.Args != null) p["args"] = t.Args;
            if (t.Computed) p["computed"] = true;
            if (kind == "prefix") p["skips"] = Skips(m);
            string[] refs = m.Parameters.Where(x => x.ParameterType.IsByReference && !x.Name.StartsWith("__")).Select(x => x.Name).ToArray();
            if (refs.Length > 0) p["changesArgs"] = new JArray(refs);
            if (m.Parameters.Any(x => x.Name == "__result" && x.ParameterType.IsByReference)) p["changesResult"] = true;
            if (m.Parameters.Any(x => x.Name == "__runOriginal")) p["runOriginal"] = true; // (it looks whether another prefix already skipped)
            if (priority.HasValue && priority.Value != 400) p["priority"] = priority.Value;
            foreach (string order in new[] { "Before", "After" })
            {
                CustomAttribute a = Attr(m, "HarmonyLib.Harmony" + order) ?? Attr(m.DeclaringType, "HarmonyLib.Harmony" + order);
                if (a != null && a.ConstructorArguments.Count > 0 && a.ConstructorArguments[0].Value is CustomAttributeArgument[] ids)
                    p[order.ToLowerInvariant()] = new JArray(ids.Select(x => x.Value as string).Where(x => x != null));
            }
            p["method"] = Name(m.DeclaringType) + "." + m.Name;
            return p;
        }

        /// <summary>
        /// Whether a bool prefix can skip the game's method: "never" (it always returns true), "always" (always false), "sometimes".
        /// Read from what reaches each return: a constant, or a local set from constants. Anything else counts as "sometimes".
        /// </summary>
        private static string Skips(MethodDefinition m)
        {
            if (m.ReturnType.FullName != "System.Boolean") return m.Parameters.Any(x => x.Name == "__runOriginal" && x.ParameterType.IsByReference) ? "sometimes" : "never";
            if (!m.HasBody) return "sometimes";
            var values = new HashSet<int>();
            foreach (Instruction ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret))
            {
                int? v = Constant(ret.Previous);
                if (v.HasValue) { values.Add(v.Value); continue; }
                int local = LocalIndex(ret.Previous);
                if (local < 0) return "sometimes";
                foreach (Instruction st in m.Body.Instructions.Where(i => StoreIndex(i) == local))
                {
                    int? sv = Constant(st.Previous);
                    if (!sv.HasValue) return "sometimes";
                    values.Add(sv.Value);
                }
            }
            if (values.Count == 0) return "sometimes";
            if (values.All(v => v == 0)) return "always";
            return values.Contains(0) ? "sometimes" : "never";
        }

        private static int? Constant(Instruction i)
        {
            if (i == null) return null;
            if (i.OpCode == OpCodes.Ldc_I4_0) return 0;
            if (i.OpCode == OpCodes.Ldc_I4_1) return 1;
            if (i.OpCode == OpCodes.Ldc_I4_S || i.OpCode == OpCodes.Ldc_I4) return Convert.ToInt32(i.Operand) == 0 ? 0 : 1;
            return null;
        }

        private static int LocalIndex(Instruction i)
        {
            if (i == null) return -1;
            if (i.OpCode == OpCodes.Ldloc_0) return 0;
            if (i.OpCode == OpCodes.Ldloc_1) return 1;
            if (i.OpCode == OpCodes.Ldloc_2) return 2;
            if (i.OpCode == OpCodes.Ldloc_3) return 3;
            if ((i.OpCode == OpCodes.Ldloc_S || i.OpCode == OpCodes.Ldloc) && i.Operand is VariableDefinition v) return v.Index;
            return -1;
        }

        private static int StoreIndex(Instruction i)
        {
            if (i.OpCode == OpCodes.Stloc_0) return 0;
            if (i.OpCode == OpCodes.Stloc_1) return 1;
            if (i.OpCode == OpCodes.Stloc_2) return 2;
            if (i.OpCode == OpCodes.Stloc_3) return 3;
            if ((i.OpCode == OpCodes.Stloc_S || i.OpCode == OpCodes.Stloc) && i.Operand is VariableDefinition v) return v.Index;
            return -1;
        }

        /// <summary>The method a TargetMethod() names, from the type and name it loads (AccessTools.Method(typeof(X), "Name")).</summary>
        private static Target Named(MethodDefinition finder)
        {
            if (!finder.HasBody) return null;
            string type = null, method = null;
            foreach (Instruction i in finder.Body.Instructions)
            {
                if (i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference tr && type == null) type = Name(tr);
                else if (i.OpCode == OpCodes.Ldstr && method == null) method = (string)i.Operand;
            }
            return type == null && method == null ? null : new Target { Type = type, Method = method };
        }

        // ---- patches made in code: harmony.Patch(...) and MonoMod hooks ----

        private static void CodePatches(MethodDefinition m, JArray patches)
        {
            IList<Instruction> code = m.Body.Instructions;
            int from = 0;
            for (int n = 0; n < code.Count; n++)
            {
                Instruction i = code[n];
                if ((i.OpCode != OpCodes.Call && i.OpCode != OpCodes.Callvirt && i.OpCode != OpCodes.Newobj) || !(i.Operand is MethodReference called)) continue;
                string owner = called.DeclaringType.FullName;
                if (owner == "HarmonyLib.Harmony" && called.Name == "Patch")
                {
                    // harmony.Patch(original, prefix, postfix, ...): the first type and method name loaded since the last call are the original
                    string type = null, name = null;
                    var names = new List<string>();
                    for (int k = from; k < n; k++)
                    {
                        if (code[k].OpCode == OpCodes.Ldtoken && code[k].Operand is TypeReference tr && type == null) type = Name(tr);
                        else if (code[k].OpCode == OpCodes.Ldstr) names.Add((string)code[k].Operand);
                    }
                    if (names.Count > 0) name = names[0];
                    var p = new JObject { ["target"] = (type ?? "?") + "." + (name ?? "?"), ["kind"] = "patch (in code)", ["computed"] = true, ["method"] = Name(m.DeclaringType) + "." + m.Name };
                    if (names.Count > 1) p["names"] = new JArray(names.Skip(1).Distinct());
                    patches.Add(p);
                    from = n + 1;
                }
                else if (called.Name.StartsWith("add_") && (owner.StartsWith("On.") || owner.StartsWith("IL.")))
                {
                    // MonoMod HookGen: On.Player.Update += ... (IL.X is an IL hook, like a transpiler)
                    string game = owner.Substring(3).Replace('/', '.');
                    patches.Add(new JObject
                    {
                        ["target"] = game + "." + called.Name.Substring(4),
                        ["kind"] = owner.StartsWith("On.") ? "hook" : "il-hook",
                        ["method"] = Name(m.DeclaringType) + "." + m.Name,
                    });
                }
            }
        }

        // ---- helpers ----

        private static CustomAttribute Attr(ICustomAttributeProvider p, string fullName) =>
            p.HasCustomAttributes ? p.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == fullName) : null;

        private static string Arg(CustomAttribute a, int i) => a.ConstructorArguments.Count > i ? a.ConstructorArguments[i].Value?.ToString() : null;

        /// <summary>A type's name as the game's code writes it: nested types with dots (ItemDrop.ItemData), generic arguments left out.</summary>
        internal static string Name(TypeReference t)
        {
            string n = t.FullName.Replace('/', '.');
            int tick = n.IndexOf('`');
            return tick >= 0 ? n.Substring(0, tick) : n;
        }
    }
}
