using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Dungeons
{
    /// <summary>
    /// What's wrong in an entrance look's file (packs/looks/&lt;name&gt;.json; its format is at the top of EntranceLooks.cs): fields nobody
    /// reads (a typo means a part silently left as it was), parts with neither "prefab" nor "location", a location part with nothing kept,
    /// numbers that aren't numbers. No game types here: the looks are read before a world loads, and tools/dungeons checks them too.
    /// Whether the prefabs and locations exist only the game can say (EntranceLooks.Dress logs the missing ones).
    /// </summary>
    internal static class LookCheck
    {
        private static readonly string[] LookFields = { "name", "about", "front", "parts", "inside" };
        private static readonly string[] PrefabFields = { "prefab", "at", "euler", "scale" };
        private static readonly string[] LocationFields = { "location", "keep", "doorway", "at", "euler" };

        /// <summary>The look's problems (empty: none). where names it in each line ("looks/giant.json").</summary>
        public static List<string> Check(JObject json, string where)
        {
            var problems = new List<string>();
            if (json == null) { problems.Add($"{where}: not a JSON object"); return problems; }
            string name = (string)json["name"];
            if (string.IsNullOrEmpty(name)) problems.Add($"{where}: no \"name\"");
            else if (name.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')))
                problems.Add($"{where}: the name {name} must be lower case letters, digits and _ (it names a saved prefab)");
            Unknown(json, LookFields, where, problems);
            if (json["front"] != null && !(json["front"].Type == JTokenType.Float || json["front"].Type == JTokenType.Integer))
                problems.Add($"{where}: \"front\" isn't a number");
            else if (json["front"] != null && (float)json["front"] < 0f) problems.Add($"{where}: \"front\" is below 0 (players would come out inside the door)");
            if (!(json["parts"] is JArray parts) || parts.Count == 0) { problems.Add($"{where}: no \"parts\" (the entrance would only show its stand-in)"); return problems; }
            for (int i = 0; i < parts.Count; i++)
            {
                string at = $"{where} part {i}";
                if (!(parts[i] is JObject p)) { problems.Add($"{at}: not an object"); continue; }
                bool prefab = !string.IsNullOrEmpty((string)p["prefab"]), location = !string.IsNullOrEmpty((string)p["location"]);
                if (prefab && location) problems.Add($"{at}: has both \"prefab\" and \"location\" (only the location is used)");
                if (!prefab && !location) { problems.Add($"{at}: has neither \"prefab\" nor \"location\""); continue; }
                Unknown(p, location ? LocationFields : PrefabFields, at + (location ? $" ({p["location"]})" : $" ({p["prefab"]})"), problems);
                foreach (string v in new[] { "at", "euler", "scale", "doorway" })
                    if (p[v] != null && !(p[v] is JArray a && a.Count == 3 && a.All(n => n.Type == JTokenType.Float || n.Type == JTokenType.Integer)))
                        problems.Add($"{at}: \"{v}\" isn't three numbers");
                if (p["scale"] is JArray s && s.Count == 3 && s.Any(n => (n.Type == JTokenType.Float || n.Type == JTokenType.Integer) && (float)n <= 0f))
                    problems.Add($"{at}: a \"scale\" of 0 or less");
                if (location && !(p["keep"] is JArray keep && keep.Any(k => !string.IsNullOrEmpty((string)k))))
                    problems.Add($"{at}: a location part with nothing in \"keep\" (nothing would be copied)");
            }
            // The way out seen from inside: prefabs only.
            if (json["inside"] != null && !(json["inside"] is JArray)) problems.Add($"{where}: \"inside\" isn't a list");
            if (json["inside"] is JArray inside)
                for (int i = 0; i < inside.Count; i++)
                {
                    string at = $"{where} inside part {i}";
                    if (!(inside[i] is JObject p)) { problems.Add($"{at}: not an object"); continue; }
                    if (string.IsNullOrEmpty((string)p["prefab"])) { problems.Add($"{at}: has no \"prefab\" (inside parts are prefabs only)"); continue; }
                    Unknown(p, PrefabFields, $"{at} ({p["prefab"]})", problems);
                    foreach (string v in new[] { "at", "euler", "scale" })
                        if (p[v] != null && !(p[v] is JArray a && a.Count == 3 && a.All(n => n.Type == JTokenType.Float || n.Type == JTokenType.Integer)))
                            problems.Add($"{at}: \"{v}\" isn't three numbers");
                }
            return problems;
        }

        private static void Unknown(JObject o, string[] known, string where, List<string> problems)
        {
            foreach (JProperty f in o.Properties())
                if (!known.Contains(f.Name) && !f.Name.StartsWith("_", StringComparison.Ordinal))
                    problems.Add($"{where}: unknown field \"{f.Name}\" (not read; known: {string.Join(", ", known)})");
        }
    }
}
