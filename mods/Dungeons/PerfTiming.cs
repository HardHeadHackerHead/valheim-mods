using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.Profiling;

namespace Dungeons
{
    /// <summary>
    /// `dungeon perf`'s timing: where a frame goes. The game's own markers through ProfilerRecorder (physics, culling, rendering, scripts),
    /// read where this build of the game records them ("n/a" where it doesn't: a release player records only some); and our own per-frame
    /// work, timed here (Dungeons.Update, Dungeons.FixedUpdate). The recorders start on the first `dungeon perf` and keep the last 60 frames;
    /// the answer is their average. This game only, nothing saved.
    /// </summary>
    internal static class PerfTiming
    {
        private const int Frames = 60;

        private static readonly string[][] Markers =
        {
            new[] { "physics", "Physics.Simulate" },
            new[] { "physics (fixed step)", "FixedUpdate.PhysicsFixedUpdate" },
            new[] { "physics sync", "Physics.SyncColliderTransform" },
            new[] { "culling", "Culling" },
            new[] { "render", "Camera.Render" },
            new[] { "scripts: Update", "Update.ScriptRunBehaviourUpdate" },
            new[] { "scripts: LateUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate" },
            new[] { "scripts: FixedUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate" },
            new[] { "main thread", "Main Thread" },
        };

        private static readonly Dictionary<string, ProfilerRecorder> Recorders = new Dictionary<string, ProfilerRecorder>();
        private static bool _started;

        private static double _updateMs, _fixedMs;   // smoothed

        /// <summary>Times one of our per-frame jobs (Dungeons.Update / FixedUpdate): Begin, then EndUpdate or EndFixed.</summary>
        public static long Begin() => Stopwatch.GetTimestamp();

        public static void EndUpdate(long since)
        {
            _updateMs = Smooth(_updateMs, since);
            // this frame's parts, folded into their smoothed ms per frame (a part not seen this frame decays towards 0)
            foreach (string name in PartMs.Keys.ToList())
            {
                PartTicks.TryGetValue(name, out long t);
                double ms = t * 1000.0 / Stopwatch.Frequency;
                PartMs[name] = PartMs[name] * 0.95 + ms * 0.05;
            }
            foreach (KeyValuePair<string, long> kv in PartTicks)
                if (!PartMs.ContainsKey(kv.Key)) PartMs[kv.Key] = kv.Value * 1000.0 / Stopwatch.Frequency;
            PartTicks.Clear();
        }

        // ---- our parts: what in our code takes the time (Add, from wherever it's measured: Dungeons.Update's jobs, each dungeon's tend,
        // the culling's LateUpdate), summed per frame, smoothed

        private static readonly Dictionary<string, long> PartTicks = new Dictionary<string, long>();
        private static readonly Dictionary<string, double> PartMs = new Dictionary<string, double>();

        public static void Add(string part, long since)
        {
            long t = Stopwatch.GetTimestamp() - since;
            PartTicks[part] = PartTicks.TryGetValue(part, out long had) ? had + t : t;
        }
        public static void EndFixed(long since) => _fixedMs = Smooth(_fixedMs, since);

        private static double Smooth(double was, long since)
        {
            double ms = (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
            return was <= 0 ? ms : was * 0.95 + ms * 0.05;
        }

        private static void Start()
        {
            if (_started) return;
            _started = true;
            foreach (string[] m in Markers)
            {
                try
                {
                    ProfilerRecorder r = ProfilerRecorder.StartNew(ProfilerCategory.Internal, m[1], Frames);
                    Recorders[m[0]] = r;
                }
                catch (Exception) { }
            }
        }

        /// <summary>Stops the recorders (the mod going).</summary>
        public static void Stop()
        {
            foreach (ProfilerRecorder r in Recorders.Values) { try { r.Dispose(); } catch (Exception) { } }
            Recorders.Clear();
            _started = false;
        }

        public static JObject Report()
        {
            bool first = !_started;
            Start();
            var o = new JObject();
            foreach (string[] m in Markers)
            {
                if (!Recorders.TryGetValue(m[0], out ProfilerRecorder r) || !r.Valid) { o[m[0]] = "n/a"; continue; }
                o[m[0]] = first || r.Count == 0 ? "starting: run dungeon perf again" : $"{Average(r):0.00} ms";
            }
            o["dungeons: Update (ours)"] = $"{_updateMs:0.00} ms";
            o["dungeons: FixedUpdate (ours, per step)"] = $"{_fixedMs:0.00} ms";
            o["ours by part (ms per frame)"] = new JObject(PartMs.Where(kv => kv.Value >= 0.005).OrderByDescending(kv => kv.Value)
                                                                .Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 3))));
            return o;
        }

        private static double Average(ProfilerRecorder r)
        {
            int n = r.Capacity < r.Count ? r.Capacity : r.Count;
            if (n == 0) return 0;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += r.GetSample(i).Value;
            return sum / n / 1e6;   // (nanoseconds)
        }
    }
}
