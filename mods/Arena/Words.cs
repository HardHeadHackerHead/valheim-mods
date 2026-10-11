using System;
using System.Globalization;
using System.Linq;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// The announcer's calls (and the other words players tell each other: who is fighting, a champion's run in the Hall), each seen in the
    /// language of whoever reads it. What is sent or saved is the call's key and its arguments, never its words, and each game puts it into
    /// words (Show). A call is one or more lines (Then: shown one after another), each a key with its arguments (Line); an argument is plain
    /// (a player's name, a number) or something each game names itself: a land, an item, a word of ours, a champion, the rules, a prize or a
    /// purse. Anything that is not a key (a call from an older Arena, a dash) is shown as it is.
    /// </summary>
    internal static class Words
    {
        private const char Lines = '\u001e', Args = '\u001f', Typed = '\u0001';

        /// <summary>A line between two others: "   -   ".</summary>
        internal const string Dash = "-";

        internal static string Line(string key, params object[] args) =>
            args.Length == 0 ? key : key + Args + string.Join(Args.ToString(), args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)));

        internal static string Then(params string[] lines) => string.Join(Lines.ToString(), lines.Where(l => !string.IsNullOrEmpty(l)));

        internal static string Land(int tier) => Typed + "L" + tier.ToString(CultureInfo.InvariantCulture);
        internal static string Item(string prefab) => Typed + "I" + prefab;
        internal static string Word(string key) => Typed + "K" + key;
        internal static string Champion(int first, int epithet) => Typed + "C" + first.ToString(CultureInfo.InvariantCulture) + "." + epithet.ToString(CultureInfo.InvariantCulture);
        internal static string Rules(bool fists, bool noFood, bool hard, bool timed) => Typed + "R" + (fists ? "1" : "0") + (noFood ? "1" : "0") + (hard ? "1" : "0") + (timed ? "1" : "0");
        internal static string Prize(string data) => Typed + "P" + data;
        internal static string Purse(string data) => Typed + "U" + data;

        /// <summary>A call in this player's language.</summary>
        internal static string Show(string said) => string.IsNullOrEmpty(said) ? "" : string.Join("   ", said.Split(Lines).Select(ShowLine));

        private static string ShowLine(string line)
        {
            if (line.Length > 0 && line[0] == Typed) return Arg(line);
            string[] parts = line.Split(Args);
            if (!IsKey(parts[0])) return line;
            return T(parts[0], parts.Skip(1).Select(a => (object)Arg(a)).ToArray());
        }

        private static bool IsKey(string s) => s.StartsWith("arena_") && s.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_');

        private static string Arg(string a)
        {
            if (a.Length < 2 || a[0] != Typed) return a;
            string v = a.Substring(2);
            switch (a[1])
            {
                case 'L': return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tier) ? Roster.LandName(tier) : v;
                case 'I': return Contest.ItemName(v);
                case 'K': return IsKey(v) ? T(v) : v;
                case 'C': return Roster.ChampionName(v);
                case 'R': return v.Length == 4 ? Arena.Rules.TextOf(v[0] == '1', v[1] == '1', v[2] == '1', v[3] == '1') : v;
                case 'P': return Contest.PrizeText(v);
                case 'U': return Contest.PurseText(v);
                default: return v;
            }
        }

        private static string Language => Localization.instance != null ? Localization.instance.GetSelectedLanguage() : "English";

        /// <summary>In capitals, as the announcer's big calls are (Turkish has its own dotted and dotless i).</summary>
        internal static string Upper(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (Language == "Turkish")
                try { return s.ToUpper(CultureInfo.GetCultureInfo("tr-TR")); } catch (Exception) { }
            return s.ToUpperInvariant();
        }

        /// <summary>A thing's name in the middle of a sentence ("a fresh deer stew"): German keeps its nouns' capitals.</summary>
        internal static string Lower(string s)
        {
            if (string.IsNullOrEmpty(s) || Language == "German") return s;
            if (Language == "Turkish")
                try { return s.ToLower(CultureInfo.GetCultureInfo("tr-TR")); } catch (Exception) { }
            return s.ToLowerInvariant();
        }
    }
}
