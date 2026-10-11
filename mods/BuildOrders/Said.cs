using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using static DHack.Shared.Translations;

namespace BuildOrders
{
    /// <summary>
    /// A sentence kept as its translation key and what fills it in, so it can be shown in the player's language (<see cref="Shown"/>) and
    /// still be given in English where the words stay English (<see cref="English"/>: Claude Tools' answers, the log). A bridge's warnings and
    /// a levelling's state are kept this way. An argument may be a Said too (a word inside the sentence).
    /// </summary>
    internal sealed class Said
    {
        public readonly string Key;
        public readonly object[] Args;

        public Said(string key, params object[] args) { Key = key; Args = args ?? new object[0]; }

        public string Shown => T(Key, Args);

        public string English
        {
            get
            {
                string text = EnglishWords.TryGetValue(Key, out string en) ? en : Key;
                if (Args.Length == 0) return text;
                object[] args = new object[Args.Length];
                for (int i = 0; i < args.Length; i++) args[i] = Args[i] is Said said ? said.English : Args[i];   // (a Said inside: in English too)
                try { return string.Format(text, args); } catch (System.FormatException) { return text; }
            }
        }

        public override string ToString() => Shown;

        private static Dictionary<string, string> _english;

        /// <summary>translations/English.json, built into the DLL (read once).</summary>
        private static Dictionary<string, string> EnglishWords
        {
            get
            {
                if (_english != null) return _english;
                _english = new Dictionary<string, string>();
                try
                {
                    using (Stream s = typeof(Said).Assembly.GetManifestResourceStream("translations/English.json"))
                    using (var reader = new StreamReader(s))
                        foreach (Match m in Regex.Matches(reader.ReadToEnd(), "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                            _english[Regex.Unescape(m.Groups[1].Value)] = Regex.Unescape(m.Groups[2].Value);
                }
                catch (System.Exception) { }
                return _english;
            }
        }
    }
}
