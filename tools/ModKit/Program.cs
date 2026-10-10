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
            string valheim = null;
            int at = rest.IndexOf("--valheim");
            if (at >= 0 && at + 1 < rest.Count) { valheim = rest[at + 1]; rest.RemoveRange(at, 2); }
            valheim = valheim ?? FindValheim();
            if (valheim == null)
            {
                Console.Error.WriteLine("modkit: can't find the Valheim folder. Run it from BepInEx/claude/modkit, set VALHEIM_DIR, or add --valheim <folder>.");
                return 2;
            }
            string[] dirs = { Path.Combine(valheim, "BepInEx", "core"), Path.Combine(valheim, "valheim_Data", "Managed") };
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
            return Start(rest.ToArray(), valheim);
        }

        [MethodImpl(MethodImplOptions.NoInlining)] // (so Cecil and Newtonsoft load only after the resolver is in place)
        private static int Start(string[] args, string valheim) => Cli.Run(args, valheim);

        /// <summary>The Valheim folder: VALHEIM_DIR, a folder above this program (it lives in BepInEx/claude/modkit), or Steam's usual places.</summary>
        private static string FindValheim()
        {
            bool IsGame(string d) => d != null && Directory.Exists(Path.Combine(d, "valheim_Data")) && Directory.Exists(Path.Combine(d, "BepInEx"));
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
