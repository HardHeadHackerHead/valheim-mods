using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// A boss fight's saved state, beside its encounter's record (EncounterRecord): in the entrance's ZDO (BossFight.Key, one entry per
    /// boss encounter), written by the entrance's owner only, read by every game, which draws and hears the fight from it on the server's
    /// clock (MechClock). Positions are the world's (the dungeon stands at the same place on every game). A record whose Start isn't the
    /// fight now going on (the encounter idle, or started again) is stale: the owner writes a new one when a fight starts.
    /// </summary>
    internal class BossRecord
    {
        public double Start;            // when the fight started (the intro's clock)
        public bool Woke;               // the boss was spawned
        public int Beats;               // intro beats whose wires the owner fired
        public int Phase;               // phases passed
        public int Shield = -1;         // the phase whose shield is up (-1: none)
        public double ShieldAt;         // when it came up
        public double BrokeAt;          // when a shield last broke
        public double HitAt;            // when a weak hit last landed
        public double ArmourOff;        // its armour is off until then
        public float Enrage = 1f;
        public int Cast = -1;           // the ability being cast (-1: none)
        public double CastAt;
        public List<Vector3> CastPts = new List<Vector3>();   // its targets (a charge: from, to)
        public List<double> Next = new List<double>();        // each ability's next use (0: not yet scheduled)
        public double LeapAt;           // a leap: when it starts, from, to, how long
        public Vector3 LeapFrom, LeapTo;
        public float LeapDur;
        public float LeapArc;           // its arc's height (0: a dash along the floor)
        public int ShieldCount;         // the shield's conditions held this many times so far
        public double FlickerAt;        // when the shield last flickered (its conditions held, not for the last time)
        public double HealAt;           // when a phase's second wind last healed it
        public double ClampUntil;       // a weakTo mechanism coming down on it holds it in place until it lands (then this is its landing + 0.15)
        public int ClampWeak = -1;      // which weakTo holds it
        public double DeadAt;           // when it fell (0: alive)
        public int DeathBeats;          // death beats whose wires the owner fired
        public List<double> WeakSeen = new List<double>();    // each weakTo: the arrival it last looked at

        public BossRecord Copy()
        {
            var c = (BossRecord)MemberwiseClone();
            c.CastPts = new List<Vector3>(CastPts);
            c.Next = new List<double>(Next);
            c.WeakSeen = new List<double>(WeakSeen);
            return c;
        }

        // ---- the ZDO's string: "<mech key>=<field>|<field>|...;<mech key>=..."

        public static Dictionary<string, BossRecord> Parse(string text)
        {
            var all = new Dictionary<string, BossRecord>();
            if (string.IsNullOrEmpty(text)) return all;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0) continue;
                string[] f = entry.Substring(eq + 1).Split('|');
                if (f.Length < 22) continue;
                var r = new BossRecord
                {
                    Start = D(f[0]), Woke = f[1] == "1", Beats = I(f[2]), Phase = I(f[3]), Shield = I(f[4]), ShieldAt = D(f[5]),
                    BrokeAt = D(f[6]), HitAt = D(f[7]), ArmourOff = D(f[8]), Enrage = (float)D(f[9]), Cast = I(f[10]), CastAt = D(f[11]),
                    CastPts = Vecs(f[12]), Next = Doubles(f[13]), LeapAt = D(f[14]), LeapFrom = Vecs(f[15]).FirstOrDefault(),
                    LeapTo = Vecs(f[16]).FirstOrDefault(), LeapDur = (float)D(f[17]), DeadAt = D(f[18]), DeathBeats = I(f[19]),
                    WeakSeen = Doubles(f[20]), LeapArc = (float)D(f[21]),
                    ShieldCount = f.Length > 22 ? I(f[22]) : 0, FlickerAt = f.Length > 23 ? D(f[23]) : 0.0,
                    HealAt = f.Length > 24 ? D(f[24]) : 0.0, ClampUntil = f.Length > 25 ? D(f[25]) : 0.0,
                    ClampWeak = f.Length > 26 ? I(f[26]) : -1,
                };
                if (r.Enrage <= 0f) r.Enrage = 1f;
                all[entry.Substring(0, eq)] = r;
            }
            return all;
        }

        public static string Format(Dictionary<string, BossRecord> all)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, BossRecord> kv in all)
            {
                BossRecord r = kv.Value;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=');
                sb.Append(string.Join("|", new[]
                {
                    S(r.Start), r.Woke ? "1" : "0", r.Beats.ToString(CultureInfo.InvariantCulture), r.Phase.ToString(CultureInfo.InvariantCulture),
                    r.Shield.ToString(CultureInfo.InvariantCulture), S(r.ShieldAt), S(r.BrokeAt), S(r.HitAt), S(r.ArmourOff), S(r.Enrage),
                    r.Cast.ToString(CultureInfo.InvariantCulture), S(r.CastAt), Vecs(r.CastPts), Doubles(r.Next), S(r.LeapAt),
                    Vecs(new[] { r.LeapFrom }), Vecs(new[] { r.LeapTo }), S(r.LeapDur), S(r.DeadAt), r.DeathBeats.ToString(CultureInfo.InvariantCulture),
                    Doubles(r.WeakSeen), S(r.LeapArc), r.ShieldCount.ToString(CultureInfo.InvariantCulture), S(r.FlickerAt), S(r.HealAt),
                    S(r.ClampUntil), r.ClampWeak.ToString(CultureInfo.InvariantCulture),
                }));
            }
            return sb.ToString();
        }

        private static string S(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
        private static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0;
        private static int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;

        private static string Doubles(IEnumerable<double> list) => string.Join("/", list.Select(S));
        private static List<double> Doubles(string s) => string.IsNullOrEmpty(s) ? new List<double>() : s.Split('/').Select(D).ToList();

        private static string Vecs(IEnumerable<Vector3> list) =>
            string.Join("/", list.Select(v => S(v.x) + "," + S(v.y) + "," + S(v.z)));

        private static List<Vector3> Vecs(string s)
        {
            var list = new List<Vector3>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (string v in s.Split('/'))
            {
                string[] c = v.Split(',');
                if (c.Length == 3) list.Add(new Vector3((float)D(c[0]), (float)D(c[1]), (float)D(c[2])));
            }
            return list;
        }
    }
}
