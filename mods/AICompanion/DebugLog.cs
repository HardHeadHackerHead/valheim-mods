using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace AICompanion
{
    /// <summary>One decision as the Debug tab shows it: where it came from, how long Jev took, every answer with its odds, and the exact JSON.</summary>
    internal class DecisionRecord
    {
        public DateTime When;
        public string Companion = "", Outcome = "", Note = "", Error = "", Request = "", Response = "";
        public bool FromJev, AskedJev;
        public float Ms = -1f, Confidence;
        public List<string> Answers = new List<string>();
        public string Enemies = "";

        public bool Failed => !string.IsNullOrEmpty(Error);
    }

    /// <summary>The last decisions of every companion this game runs (for the Debug tab), and optionally a log file for tuning.</summary>
    internal static class DebugLog
    {
        public static readonly List<DecisionRecord> Records = new List<DecisionRecord>(); // newest first
        private const int Max = 200;

        public static string FilePath => Path.Combine(Paths.BepInExRootPath, "AICompanion", "jev-decisions.jsonl");

        public static void Add(DecisionRecord r)
        {
            Records.Insert(0, r);
            if (Records.Count > Max) Records.RemoveAt(Records.Count - 1);
            if (!Plugin.LogToFile.Value || !r.AskedJev) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var line = new JObject
                {
                    ["time"] = r.When.ToString("o"), ["companion"] = r.Companion, ["outcome"] = r.Outcome, ["from_jev"] = r.FromJev,
                    ["ms"] = Math.Round(r.Ms), ["note"] = r.Note, ["error"] = r.Error,
                    ["request"] = Parse(r.Request), ["response"] = Parse(r.Response),
                };
                File.AppendAllText(FilePath, line.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            }
            catch (Exception e) { Plugin.Instance?.Warn("Could not write the Jev log: " + e.Message); }
        }

        private static JToken Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return JToken.Parse(s); } catch (Exception) { return s; }
        }

        /// <summary>Jev's answers as readable lines: "action: attack 62% · back_off 21% · … (confidence 54%)", "drink: yes 0.82".</summary>
        public static List<string> Describe(JObject answers)
        {
            var lines = new List<string>();
            if (answers == null) return lines;
            foreach (var kv in answers)
            {
                if (!(kv.Value is JObject a)) continue;
                string type = (string)a["type"];
                if (type == "noul") { lines.Add($"{kv.Key}: yes {(float?)a["noul"] ?? 0f:0.00}"); continue; }
                var odds = (a["probabilities"] as JObject)?.Properties().Select(p => (p.Name, P: (float?)p.Value ?? 0f)).OrderByDescending(p => p.P)
                           .Select(p => $"{p.Name} {p.P * 100f:0}%") ?? Enumerable.Empty<string>();
                string conf = a["confidence"] != null ? $"  (confidence {(float)a["confidence"] * 100f:0}%)" : "";
                lines.Add($"{kv.Key}: {string.Join("  ·  ", odds)}{conf}");
            }
            return lines;
        }

        public static string Summary()
        {
            var asked = Records.Where(r => r.AskedJev).ToList();
            if (asked.Count == 0) return "No Jev requests yet (this session).";
            var ok = asked.Where(r => !r.Failed).ToList();
            float avg = ok.Count > 0 ? ok.Average(r => r.Ms) : 0f;
            int used = asked.Count(r => r.FromJev);
            return $"{asked.Count} requests: {ok.Count} answered (avg {avg:0} ms, slowest {(ok.Count > 0 ? ok.Max(r => r.Ms) : 0f):0} ms), {asked.Count - ok.Count} failed; " +
                   $"Jev's call used {used} times, the built-in brain stepped in {asked.Count - used} times.";
        }
    }
}
