using System;
using System.Collections.Generic;

namespace Scripts.Mp
{
    /// <summary>
    /// Who saved each script last, and when (docs/scripting-collab.md, "stale saves"). A save that began from an older version is refused
    /// with a message that names them ("Ann changed this script 2 minutes ago"): this is where the name comes from. Set by an admin's write,
    /// a live co-edit's autosave and the owner's own write; plain C#, so the tests can run it.
    /// </summary>
    internal static class LastWriter
    {
        private static readonly Dictionary<string, KeyValuePair<string, DateTime>> By = new Dictionary<string, KeyValuePair<string, DateTime>>();

        /// <summary>The time now (a test sets it).</summary>
        internal static Func<DateTime> Clock = () => DateTime.UtcNow;

        private static string Key(string file) => (file ?? "").ToLowerInvariant();

        internal static void Set(string file, string who)
        {
            if (string.IsNullOrEmpty(file)) return;
            By[Key(file)] = new KeyValuePair<string, DateTime>(string.IsNullOrWhiteSpace(who) ? "" : Clip(who, 40), Clock());
        }

        internal static void Forget(string file) { By.Remove(Key(file)); }

        internal static void Clear() { By.Clear(); }

        /// <summary>Who saved last and how many seconds ago; false when nobody has since the mod started.</summary>
        internal static bool Get(string file, out string who, out double secondsAgo)
        {
            if (By.TryGetValue(Key(file), out KeyValuePair<string, DateTime> v)) { who = v.Key; secondsAgo = Math.Max(0.0, (Clock() - v.Value).TotalSeconds); return true; }
            who = ""; secondsAgo = 0.0;
            return false;
        }

        /// <summary>"just now", "40 seconds ago", "2 minutes ago", "3 hours ago" (English; the editor has its own words for these).</summary>
        internal static string Ago(double seconds)
        {
            if (seconds < 10.0) return "just now";
            if (seconds < 90.0) return Math.Round(seconds) + " seconds ago";
            if (seconds < 5400.0) return Math.Round(seconds / 60.0) + " minutes ago";
            return Math.Round(seconds / 3600.0) + " hours ago";
        }

        /// <summary>The refusal's words: who changed the script and when ("Ann changed this script 2 minutes ago. Reload it first.").</summary>
        internal static string StaleMessage(string file)
        {
            if (Get(file, out string who, out double ago) && who.Length > 0) return who + " changed this script " + Ago(ago) + ". Reload it first.";
            return "Someone changed this script. Reload it first.";
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
