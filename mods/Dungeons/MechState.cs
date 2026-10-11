using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The server's clock, smoothed, for the mechanisms. ZNet's time (ZNet.GetTimeSeconds) goes on in steps of the physics step, and on a
    /// client it is set from the server's every 2 s (a jump of a few hundredths): moving parts read it through here, so they move evenly and
    /// every player computes the same pose for the same moment. Inside a physics step it's the step's time, in a frame the frame's.
    /// </summary>
    internal static class MechClock
    {
        private static double _offset;
        private static bool _known;

        /// <summary>Server seconds now (in FixedUpdate: at this physics step).</summary>
        public static double Now => !_known ? (ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0.0)
                                            : (Time.inFixedTimeStep ? Time.fixedTimeAsDouble : Time.timeAsDouble) + _offset;

        /// <summary>Every physics step: follows the server's time slowly, or jumps to it when far off (a first reading, a save loaded).</summary>
        public static void Step()
        {
            if (ZNet.instance == null) { _known = false; return; }
            double target = ZNet.instance.GetTimeSeconds() - Time.fixedTimeAsDouble;
            if (!_known || Math.Abs(target - _offset) > 0.5) { _offset = target; _known = true; }
            else _offset += (target - _offset) * 0.05;
        }

        public static void Reset() => _known = false;
    }

    /// <summary>One mechanism's saved state: it goes from state From to state To, starting at server time Time (taking its Seconds).</summary>
    internal struct MechRecord
    {
        public int To, From;
        public double Time;
    }

    /// <summary>
    /// Every mechanism's state in one string on the entrance's ZDO (Keys.Mechs): "key=to,from,time;..." for the ones that ever changed;
    /// the others are at their start. Small (a handful of mechanisms), written only by the entrance's owner, and cleared by a reset.
    /// </summary>
    internal static class MechRecords
    {
        public static Dictionary<string, MechRecord> Parse(string text)
        {
            var all = new Dictionary<string, MechRecord>();
            if (string.IsNullOrEmpty(text)) return all;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string[] f = entry.Substring(eq + 1).Split(',');
                if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int to)
                    || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int from)
                    || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double time)) continue;
                all[entry.Substring(0, eq)] = new MechRecord { To = to, From = from, Time = time };
            }
            return all;
        }

        public static string Format(Dictionary<string, MechRecord> all)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, MechRecord> kv in all)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value.To.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(kv.Value.From.ToString(CultureInfo.InvariantCulture)).Append(',').Append(kv.Value.Time.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }

    /// <summary>Where a mechanism is at a moment: going from state From to state To since Start (at rest once Start + its seconds is past).</summary>
    internal struct MechMotion
    {
        public int From, To;
        public double Start;
        public float Seconds;

        public bool Moving(double now) => Seconds > 0f && now < Start + Seconds && now >= Start - 0.001;
        public float Progress(double now) => Seconds <= 0f ? 1f : Mathf.Clamp01((float)((now - Start) / Seconds));

        /// <summary>The motion at a moment, from its record (or none: at its start since ever). A cycling mechanism goes one state on every
        /// Cycle seconds from its record's time (or from server time 0, so every player agrees without anything saved).</summary>
        public static MechMotion At(MechDef def, int states, bool has, MechRecord rec, double now)
        {
            int start = Mathf.Clamp(def.Start, 0, Math.Max(0, states - 1));
            var m = has ? new MechMotion { From = rec.From, To = rec.To, Start = rec.Time, Seconds = def.Seconds }
                        : new MechMotion { From = start, To = start, Start = def.Cycle > 0f ? 0.0 : double.NegativeInfinity, Seconds = def.Seconds };
            if (def.Cycle > 0f && states > 1 && now >= m.Start + def.Cycle)
            {
                long k = (long)Math.Floor((now - m.Start) / def.Cycle);
                m.From = (int)((m.To + k - 1) % states);
                m.To = (int)((m.To + k) % states);
                m.Start += k * (double)def.Cycle;
            }
            m.From = Mathf.Clamp(m.From, 0, Math.Max(0, states - 1));
            m.To = Mathf.Clamp(m.To, 0, Math.Max(0, states - 1));
            return m;
        }
    }
}
