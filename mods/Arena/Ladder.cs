using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// What you have won, kept on your character (its custom data, saved with it), and the Hall of Champions: one champion for each contest
    /// (the best run of the Long Road, the Champion Bout, the Endless Horde and Today's Trial in this world), kept as a world key (so everyone
    /// sees the same, and the Hall of Fame shows them as statues).
    /// </summary>
    internal static class Ladder
    {
        private const string Prefix = "dh_arena_", HallKey = "dharenahall";
        private static readonly string[] Titles = { "Pit Rat", "Brawler", "Pit Fighter", "Gladiator", "Champion of the Pit", "Hero of the Arena", "Legend of the Arena" };
        private static readonly int[] TitleAt = { 0, 1, 3, 8, 15, 25, 40 };

        internal static int Get(string key)
        {
            Player p = Player.m_localPlayer;
            return p != null && p.m_customData.TryGetValue(Prefix + key, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        internal static void Set(string key, int value)
        {
            Player p = Player.m_localPlayer;
            if (p != null) p.m_customData[Prefix + key] = value.ToString(CultureInfo.InvariantCulture);
        }

        internal static void Add(string key, int by) => Set(key, Get(key) + by);
        internal static void Best(string key, int value) { if (value > Get(key)) Set(key, value); }

        internal static int Wins => Get("wins");

        internal static string Title()
        {
            int wins = Wins, i = 0;
            for (int k = 0; k < TitleAt.Length; k++) if (wins >= TitleAt[k]) i = k;
            return Titles[i];
        }

        /// <summary>The next title and the wins it needs (null at the top).</summary>
        internal static string Next()
        {
            for (int k = 0; k < TitleAt.Length; k++) if (Wins < TitleAt[k]) return $"{Titles[k]} at {TitleAt[k]} wins";
            return null;
        }

        // ---- today's challenge ---------------------------------------------------------------------------------------------

        internal static int Today => int.Parse(DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        internal static bool DailyDone => Get("daily") == Today;

        // ---- the Hall of Champions -----------------------------------------------------------------------------------------

        internal struct Entry { public string Name, What, Look, Kind; public int Score; }

        /// <summary>The contests with a champion each, in the order of the plinths.</summary>
        internal static readonly string[] Kinds = { "road", "champion", "endless", "trial" };
        internal static readonly string[] KindTitles = { "The Long Road", "Champion Bout", "The Endless Horde", "Today's Trial" };

        /// <summary>The champion of a contest, if it has one.</summary>
        internal static bool Champion(string kind, out Entry entry)
        {
            foreach (Entry e in Hall()) if (e.Kind == kind) { entry = e; return true; }
            entry = default;
            return false;
        }

        // The hall is a world key (saved with the world, known to all): its entries written as hex, as world keys are lower-cased.
        // (Entries from before there was a champion for each contest have no contest and are left out.)
        internal static List<Entry> Hall()
        {
            var list = new List<Entry>();
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(HallKey, out string hex) || string.IsNullOrEmpty(hex)) return list;
            string text;
            try { text = FromHex(hex); } catch (Exception) { return list; }
            foreach (string line in text.Split('\n'))
            {
                string[] f = line.Split('|');
                if (f.Length < 5 || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int score) || !Kinds.Contains(f[4])) continue;
                if (list.Any(e => e.Kind == f[4])) continue;
                list.Add(new Entry { Name = f[0], What = f[1], Score = score, Look = f[3], Kind = f[4] });
            }
            return list;
        }

        /// <summary>Makes a run the champion of its contest if it beats the one there (or there is none). True when it did.</summary>
        internal static bool Enter(string kind, string name, string what, int score, string look = "")
        {
            if (ZoneSystem.instance == null || score <= 0 || !Kinds.Contains(kind)) return false;
            List<Entry> list = Hall();
            if (list.Any(e => e.Kind == kind && e.Score >= score)) return false;
            list.RemoveAll(e => e.Kind == kind);
            list.Add(new Entry { Name = Clean(name), What = Clean(what), Score = score, Look = Clean(look), Kind = kind });
            string text = string.Join("\n", list.Select(e => e.Name + "|" + e.What + "|" + e.Score.ToString(CultureInfo.InvariantCulture) + "|" + (e.Look ?? "") + "|" + e.Kind));
            ZoneSystem.instance.SetGlobalKey(HallKey + " " + ToHex(text));
            return true;
        }

        private static string ToHex(string s) => BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(s)).Replace("-", "").ToLowerInvariant();

        private static string FromHex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static string Clean(string s) => (s ?? "").Replace("|", "/").Replace("\n", " ");
    }
}
