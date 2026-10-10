using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ClaudeTools
{
    /// <summary>
    /// Starts modkit: finds the Valheim folder, and lets .NET find Mono.Cecil and Newtonsoft.Json in it (they come with the game and BepInEx)
    /// before any code that uses them runs.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var rest = new List<string>(args);
            string Take(string flag)
            {
                int at = rest.IndexOf(flag);
                if (at < 0 || at + 1 >= rest.Count) return null;
                string v = rest[at + 1];
                rest.RemoveRange(at, 2);
                return v;
            }
            string valheim = Take("--valheim") ?? FindValheim();
            if (valheim == null)
            {
                Console.Error.WriteLine("modkit: can't find the Valheim folder. Set VALHEIM_DIR, or add --valheim <folder>.");
                return 2;
            }
            // BepInEx: given, or the one this program lives in (BepInEx/claude/modkit: a mod manager's profile keeps it outside the game), or the game's
            string bepinex = Take("--bepinex") ?? Environment.GetEnvironmentVariable("BEPINEX_DIR");
            if (!IsBepInEx(bepinex)) bepinex = Ancestors(AppContext.BaseDirectory).FirstOrDefault(IsBepInEx) ?? Path.Combine(valheim, "BepInEx");
            string[] dirs = { Path.Combine(bepinex, "core"), Path.Combine(valheim, "valheim_Data", "Managed") };
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string file = new AssemblyName(e.Name).Name + ".dll";
                foreach (string dir in dirs)
                {
                    string path = Path.Combine(dir, file);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            return Start(rest.ToArray(), valheim, bepinex);
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // (so Cecil and Newtonsoft load only after the resolver is in place)
        private static int Start(string[] args, string valheim, string bepinex) => Cli.Run(args, valheim, bepinex);

        private static bool IsBepInEx(string d) => !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "core", "BepInEx.dll"));

        private static IEnumerable<string> Ancestors(string d)
        {
            for (; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d.TrimEnd('/', '\\'))) yield return d;
        }

        /// <summary>The Valheim folder: VALHEIM_DIR, a folder above this program (it lives in BepInEx/claude/modkit), or Steam's usual places.</summary>
        private static string FindValheim()
        {
            bool IsGame(string d) => d != null && Directory.Exists(Path.Combine(d, "valheim_Data"));
            string env = Environment.GetEnvironmentVariable("VALHEIM_DIR");
            if (IsGame(env)) return env;
            for (string d = AppContext.BaseDirectory; !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d.TrimEnd('/', '\\')))
                if (IsGame(d)) return d;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] guesses =
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Valheim", @"D:\SteamLibrary\steamapps\common\Valheim", @"E:\SteamLibrary\steamapps\common\Valheim",
                Path.Combine(home, ".local/share/Steam/steamapps/common/Valheim"), Path.Combine(home, ".steam/steam/steamapps/common/Valheim"),
                Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Valheim"),
                Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Valheim"),
            };
            return guesses.FirstOrDefault(IsGame);
        }
    }
}
