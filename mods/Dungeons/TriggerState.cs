using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Dungeons
{
    /// <summary>
    /// What the triggers and blocks remember, in one string on the entrance's ZDO (TriggerState.Key): "name=value;..." for what has
    /// happened since the dungeon was new, written only by the entrance's owner, cleared by a reset. Never a ZDOID: triggers and mechanisms
    /// by their layout keys, players' session ids only for a moment (a lock's or pedestal's "who paid", so their game can take the item).
    ///   f|&lt;trigger&gt;              1: it fired (once-only triggers)
    ///   q|&lt;trigger&gt;              a sequence's progress: "&lt;events done&gt;,&lt;server time of the first&gt;"
    ///   k|&lt;trigger&gt;|&lt;event&gt;      kills counted in a "kills" event's area
    ///   c|&lt;counter&gt;              a counter's value
    ///   t|&lt;timer&gt;                "&lt;server time it rings&gt;,&lt;every (0: once)&gt;"
    ///   p|&lt;pedestal&gt;             the item prefab on it
    ///   w|&lt;pedestal or lock&gt;     "&lt;session id&gt;,&lt;server time&gt;": who just put an item on it, or opened it with a key
    ///   m                        the music the triggers asked for ("" none)
    /// </summary>
    internal static class TriggerState
    {
        public static readonly int Key = "quaddungeon_trig".GetStableHashCode();

        public static Dictionary<string, string> Parse(string text)
        {
            var all = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return all;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq > 0) all[entry.Substring(0, eq)] = entry.Substring(eq + 1);
            }
            return all;
        }

        public static string Format(Dictionary<string, string> all)
        {
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in all.OrderBy(k => k.Key, System.StringComparer.Ordinal))
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        /// <summary>A name safe in the string (no ';' or '=': a pack's counter or timer name is anything).</summary>
        public static string Clean(string s) => (s ?? "").Replace(';', '_').Replace('=', '_').Replace('|', '_');

        public static string F(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

        public static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0.0;

        public static int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;

        public static long L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : 0L;
    }
}
