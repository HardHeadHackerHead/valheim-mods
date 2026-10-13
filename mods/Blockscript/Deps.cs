using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Scripts
{
    /// <summary>
    /// Jint, its parser Esprima, System.Buffers and the engine assembly itself travel inside this DLL as resources (lib/*.dll) and are
    /// loaded from there when the runtime asks for them, so a player installs one file and no other mod's copy of Jint can clash with
    /// ours. Nothing in this assembly names a Jint type: see Plugin.cs for why.
    /// </summary>
    internal static class Deps
    {
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>();
        private static bool _hooked;
        private static readonly string[] Names = { "System.Buffers", "Esprima", "Jint", "Scripts.Engine" };

        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            // loaded up front, in the order they need each other, so the runtime already has them when Jint first asks
            foreach (string name in new[] { "System.Buffers", "Esprima", "Jint" })
                Resolve(null, new ResolveEventArgs(name));
        }

        internal static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        internal static Assembly LoadEngine() => Resolve(null, new ResolveEventArgs("Scripts.Engine"));

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (Array.IndexOf(Names, name) < 0) return null;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out Assembly done)) return done;
                Assembly mine = typeof(Deps).Assembly;
                using (Stream stream = mine.GetManifestResourceStream("lib/" + name + ".dll"))
                {
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    Assembly loaded = Assembly.Load(bytes);
                    Loaded[name] = loaded;
                    return loaded;
                }
            }
        }
    }
}
