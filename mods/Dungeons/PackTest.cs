using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Dungeons
{
    /// <summary>
    /// `dungeon test &lt;pack&gt; [seeds]`: the pack-level checks of the offline gate (tools/dungeons/make_pack.py), in the game: the pack read
    /// (PackReader's problems), then that many seeds laid out (default 200), each checked again by LayoutAudit (doorways meet, boxes clear,
    /// caps, gates on locks, wires, secrets, the shortcut, and the solution replayed with the triggers). Spread over frames (a few ms each).
    /// What it can't do here: the geometry checks (walk, props, levers, gaps: they need the offline prefab boxes) and the variety check.
    /// </summary>
    internal static class PackTest
    {
        private const double FrameMs = 6.0;   // layout work per frame

        /// <summary>The test, as a coroutine (Tools.cs runs it); output gets one report, error a reason it couldn't run.</summary>
        public static IEnumerator Run(string name, int seeds, Action<JObject> output, Action<string> error)
        {
            if (string.IsNullOrEmpty(name)) { error("which pack? dungeon test <pack> [seeds]"); yield break; }
            seeds = Math.Max(1, Math.Min(5000, seeds <= 0 ? 200 : seeds));
            var clock = Stopwatch.StartNew();
            while (!Packs.Ready(name) && clock.Elapsed.TotalSeconds < 15) yield return null;   // (its game rooms loading)
            Pack pack = null;
            try { pack = Packs.Get(name); } catch (Exception e) { error($"{name} can't be read: {e.Message}"); yield break; }
            if (pack == null) { error(Packs.Waiting ? "waiting for the server's packs" : $"no pack {name} (dungeon packs lists them)"); yield break; }

            int good = 0;
            var why = new Dictionary<string, int>();
            var faults = new Dictionary<string, KeyValuePair<int, string>>();
            var uses = new SortedDictionary<int, int>();
            double ms = 0, worst = 0;
            var frame = Stopwatch.StartNew();
            for (int seed = 1; seed <= seeds; seed++)
            {
                if (frame.Elapsed.TotalMilliseconds > FrameMs) { yield return null; frame.Restart(); }
                var one = Stopwatch.StartNew();
                Layout l;
                List<string> found;
                try
                {
                    l = Layouts.Make(pack, seed);
                    found = l.Ok ? LayoutAudit.Check(l).ToList() : null;
                }
                catch (Exception e) { l = null; found = new List<string> { "error: " + e.GetType().Name + ": " + e.Message }; }
                one.Stop();
                ms += one.Elapsed.TotalMilliseconds;
                worst = Math.Max(worst, one.Elapsed.TotalMilliseconds);
                if (l != null && !l.Ok)
                {
                    foreach (string p in l.Problems.Skip(1)) { string k = Regex.Replace(p, @"\d+", "#"); why[k] = why.TryGetValue(k, out int c) ? c + 1 : 1; }
                    continue;
                }
                if (l != null) { good++; uses[l.Solution.Count] = uses.TryGetValue(l.Solution.Count, out int u) ? u + 1 : 1; }
                foreach (string f in found ?? new List<string>())
                {
                    string k = Regex.Replace(f, @"\d+", "#");
                    faults[k] = faults.TryGetValue(k, out var v) ? new KeyValuePair<int, string>(v.Key + 1, v.Value) : new KeyValuePair<int, string>(1, $"seed {seed}: {f}");
                }
            }
            List<string> problems = Packs.Problems(name);
            bool pass = problems.Count == 0 && good == seeds && faults.Count == 0;
            output(new JObject
            {
                ["pack"] = pack.Name,
                ["source"] = Packs.SourceOf(name),
                ["result"] = pass ? "pass" : "FAIL",
                ["problems"] = new JArray(problems),
                ["seeds"] = seeds,
                ["good"] = good,
                ["failed"] = new JObject(why.OrderByDescending(p => p.Value).Select(p => new JProperty(p.Key, p.Value))),
                ["faults"] = new JArray(faults.OrderByDescending(p => p.Value.Key).Select(p => $"{p.Value.Key} x {p.Key} (first: {p.Value.Value})")),
                ["uses"] = new JObject(uses.Select(p => new JProperty(p.Key.ToString(), p.Value))),
                ["msPerLayout"] = Math.Round(ms / seeds, 2),
                ["msWorst"] = Math.Round(worst, 1),
                ["notHere"] = "walk, prop, lever and gap checks, map renders and variety: tools/dungeons/make_pack.py",
            });
        }
    }
}
