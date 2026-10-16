using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// Your last contests and duels (the Arena Master's History tab): when, which, how far, how it ended, and what it paid. Kept on the
    /// character (its custom data), newest first, the last fifteen. The prize is kept as data (Contest.PrizeText), so each language reads it.
    /// </summary>
    internal static class History
    {
        private const string Key = "dh_arena_history";
        private const int Keep = 15;
        private const char Rows = '\u001e', Fields = '\u001f';

        internal sealed class Entry
        {
            public DateTime When;
            public string Kind = "", Outcome = "", Prize = "";
            public int Tier, Round, Rounds, Coins;
            public bool Won;
        }

        internal static void Add(Player player, string kind, int tier, int round, int rounds, string outcome, bool won, string prize, int coins = 0)
        {
            if (player == null) return;
            var rows = new List<string> { string.Join(Fields.ToString(), new[]
            {
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture), Clean(kind), tier.ToString(CultureInfo.InvariantCulture),
                round.ToString(CultureInfo.InvariantCulture), rounds.ToString(CultureInfo.InvariantCulture), Clean(outcome), won ? "1" : "0",
                Clean(prize), coins.ToString(CultureInfo.InvariantCulture),
            }) };
            if (player.m_customData.TryGetValue(Key, out string had) && !string.IsNullOrEmpty(had)) rows.AddRange(had.Split(Rows).Take(Keep - 1));
            player.m_customData[Key] = string.Join(Rows.ToString(), rows);
        }

        internal static List<Entry> All(Player player)
        {
            var list = new List<Entry>();
            if (player == null || !player.m_customData.TryGetValue(Key, out string data) || string.IsNullOrEmpty(data)) return list;
            foreach (string row in data.Split(Rows))
            {
                string[] f = row.Split(Fields);
                if (f.Length < 9 || !long.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) continue;
                int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier);
                int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int round);
                int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int rounds);
                int.TryParse(f[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins);
                list.Add(new Entry { When = new DateTime(ticks, DateTimeKind.Utc), Kind = f[1], Tier = tier, Round = round, Rounds = rounds, Outcome = f[5], Won = f[6] == "1", Prize = f[7], Coins = coins });
            }
            return list;
        }

        /// <summary>Which contest, and how far it went, in words: "The Long Road: Plains, round 2 of 3", "Duel with Bob"...</summary>
        internal static string What(Entry e)
        {
            if (e.Kind.StartsWith("duel:")) return T("arena_hist_duel", e.Kind.Substring(5));
            string kind = T($"arena_kind_{e.Kind}");
            if (e.Round <= 0) return kind;
            switch (e.Kind)
            {
                case "road": return kind + ": " + T("arena_hud_land_round", Roster.LandName((e.Round - 1) / 3), (e.Round - 1) % 3 + 1);
                case "endless": return kind + ": " + Roster.LandName(e.Tier) + ", " + T("arena_wave", e.Round);
                default: return kind + ": " + Roster.LandName(e.Tier);
            }
        }

        /// <summary>How it ended, in words.</summary>
        internal static string How(Entry e) => T($"arena_hist_{e.Outcome}");

        /// <summary>What it paid, in words.</summary>
        internal static string Paid(Entry e) =>
            e.Kind.StartsWith("duel:") ? (e.Coins > 0 ? T("arena_coins", e.Coins) : "") : e.Prize.Length > 0 ? Contest.PrizeText(e.Prize) : "";

        private static string Clean(string s) => (s ?? "").Replace(Rows.ToString(), " ").Replace(Fields.ToString(), " ");
    }
}
