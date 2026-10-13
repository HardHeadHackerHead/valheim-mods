using System;
using System.Collections.Generic;

namespace Scripts
{
    /// <summary>
    /// The comment block at the top of a script, read before anything runs:
    ///
    ///   // ==Script==
    ///   // name:  Lucky Boars
    ///   // needs: items, messages
    ///   // api:   1
    ///   // ==/Script==
    ///
    /// A script that asks for more than it lists is refused at the call, not here. Unknown keys are kept out; a missing name is the file's.
    /// </summary>
    internal sealed class Manifest
    {
        internal static readonly string[] Capabilities = { "messages", "effects", "store", "items", "takeItems", "spawn", "combat", "weather", "world", "teleport", "proxy", "net" };

        internal string Name = "", Version = "1", Author = "", About = "";
        internal int Api = 1;
        internal readonly List<string> Needs = new List<string>();
        internal readonly List<string> Problems = new List<string>();

        internal static Manifest Read(string source, string fallbackName)
        {
            var m = new Manifest { Name = fallbackName };
            int start = source.IndexOf("// ==Script==", StringComparison.Ordinal);
            if (start < 0 || start > 400) return m;   // (a script with no manifest asks for nothing)
            int end = source.IndexOf("// ==/Script==", start, StringComparison.Ordinal);
            if (end < 0) { m.Problems.Add("The // ==Script== block is not closed with // ==/Script==."); return m; }
            foreach (string raw in source.Substring(start, end - start).Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("//")) continue;
                line = line.Substring(2).Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim().ToLowerInvariant(), value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "name": if (value.Length > 0) m.Name = Clip(value, 40); break;
                    case "version": m.Version = Clip(value, 12); break;
                    case "author": m.Author = Clip(value, 40); break;
                    case "about": m.About = Clip(value, 200); break;
                    case "api": if (!int.TryParse(value, out m.Api) || m.Api < 1) { m.Api = 1; m.Problems.Add("api must be a whole number from 1."); } break;
                    case "needs":
                        foreach (string part in value.Split(','))
                        {
                            string cap = part.Trim();
                            if (cap.Length == 0) continue;
                            string known = Array.Find(Capabilities, c => string.Equals(c, cap, StringComparison.OrdinalIgnoreCase));
                            if (known == null) m.Problems.Add("There is no power called '" + cap + "'.");
                            else if (!m.Needs.Contains(known)) m.Needs.Add(known);
                        }
                        break;
                }
            }
            return m;
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
