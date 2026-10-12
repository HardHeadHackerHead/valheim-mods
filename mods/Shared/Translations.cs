using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// A mod's words in the player's language, through the game's own translations (Localization), so they follow the game's Language
    /// setting like everything else. Each mod keeps translations/English.json (every key, the words the mod was written with) and one file
    /// per language (German.json, Russian.json...: the game's language names), built into its DLL. A player can add or correct any language
    /// with a file of the same name in BepInEx/config/translations/&lt;mod GUID&gt;/ (it wins over the built-in one, line by line).
    ///
    ///   In Awake:      DHack.Shared.Translations.Load(this);      In OnDestroy:  DHack.Shared.Translations.Unload();
    ///   In code:       T("recycler_button")  or  T("recycler_recycled", name, list)   (the English.json text, with {0} {1} filled in)
    ///   In the game's own fields (a piece's name, a hover text with keys): "$recycler_piece": the game translates it there.
    ///   A setting in a text the game shows from a $key ("mymod_potion_desc": "Heals {heal} health." in every language file):
    ///                  Fill("mymod_potion_desc", "heal", HealSetting)  or  Fill(key, "heal", () => text, settings...)  (again when one changes)
    ///   Words that come from data at runtime (a pack file, a server's list), "English" being every language's fallback:
    ///                  AddWords("mymod_pack_halls", "German", words)  (replaces that group's German words)    RemoveWords("mymod_pack_halls")
    ///
    /// Keys start with the mod's name ("recycler_" for Recycler), so two mods never share one. A missing translation shows the English
    /// text, never "[key]". Shared source (mods/Shared/Translations.cs), compiled into every mod by mods/Directory.Build.props.
    /// </summary>
    internal static class Translations
    {
        /// <summary>The languages every mod ships (Valheim's names). Others come from players' own files.</summary>
        public static readonly string[] Shipped = { "English", "Russian", "German", "Chinese", "Portuguese_Brazilian", "Spanish", "French", "Polish", "Turkish" };

        private static readonly Dictionary<string, Dictionary<string, string>> Languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _english = new Dictionary<string, string>();
        private static string _overrideDir;
        private static bool _hooked;
        private static readonly MethodInfo AddWord = AccessTools.Method(typeof(Localization), "AddWord");
        private static readonly FieldInfo Cache = AccessTools.Field(typeof(Localization), "m_cache");
        private static readonly FieldInfo GameWords = AccessTools.Field(typeof(Localization), "m_translations");

        // What the game was last given, for its current language: every word before the fills, and the files' words alone.
        private static Dictionary<string, string> _given = new Dictionary<string, string>();
        private static Dictionary<string, string> _files = new Dictionary<string, string>();

        // AddWords: group -> language -> key -> text; and the keys of theirs the game has now (to take back the ones that go).
        private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Runtime = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
        private static readonly HashSet<string> _runtimeGiven = new HashSet<string>();

        // Fill: key -> its blanks ("{heal}" and how to fill it).
        private sealed class Blank { public string Tag; public Func<string> Value; public Action Unhook; }
        private static readonly Dictionary<string, List<Blank>> Fills = new Dictionary<string, List<Blank>>();

        /// <summary>Read this mod's translations and give the game the words for the current language (and again whenever it changes).</summary>
        public static void Load(BaseUnityPlugin plugin)
        {
            Languages.Clear();
            Assembly asm = plugin.GetType().Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(n => n.StartsWith("translations/") && n.EndsWith(".json")))
                using (var reader = new StreamReader(asm.GetManifestResourceStream(res)))
                    Languages[Path.GetFileNameWithoutExtension(res.Substring("translations/".Length))] = Parse(reader.ReadToEnd());
            _english = Languages.TryGetValue("English", out var en) ? en : new Dictionary<string, string>();
            _overrideDir = Path.Combine(Paths.ConfigPath, "translations", plugin.Info.Metadata.GUID);
            if (!_hooked) { Localization.OnLanguageChange += Apply; _hooked = true; }
            Apply();
        }

        /// <summary>Stops following the language; the fills stop listening to their settings, and the runtime words leave the game.</summary>
        public static void Unload()
        {
            if (_hooked) { Localization.OnLanguageChange -= Apply; _hooked = false; }
            foreach (List<Blank> blanks in Fills.Values) foreach (Blank b in blanks) b.Unhook?.Invoke();
            Fills.Clear();   // (the texts keep their last values filled in: never a bare "{heal}" on a def the game still has)
            Runtime.Clear();
            Localization loc = _runtimeGiven.Count > 0 ? Localization.instance : null;
            if (loc != null && AddWord != null)
            {
                var game = GameWords?.GetValue(loc) as Dictionary<string, string>;
                foreach (string key in _runtimeGiven)
                    if (_files.TryGetValue(key, out string text)) AddWord.Invoke(loc, new object[] { key, text });   // (a file's word it covered)
                    else game?.Remove(key);
                EvictCache(loc);
            }
            _runtimeGiven.Clear();
        }

        /// <summary>This mod's text for a key in the player's language, with {0}, {1}... filled in.</summary>
        public static string T(string key, params object[] args)
        {
            string text = Localization.instance != null ? Localization.instance.Localize("$" + key) : null;
            if (string.IsNullOrEmpty(text) || text == "[" + key + "]") text = _english.TryGetValue(key, out var en) ? FillIn(key, en) : key;
            if (args == null || args.Length == 0) return text;
            try { return string.Format(text, args); }
            catch (FormatException) { return _english.TryGetValue(key, out var en) ? SafeFormat(FillIn(key, en), args) : text; }   // (a translation that broke a {n})
        }

        private static string SafeFormat(string text, object[] args) { try { return string.Format(text, args); } catch (FormatException) { return text; } }

        // ---- Fill: a setting's value in a text the game shows itself ----

        /// <summary>
        /// Puts a setting's current value into the key's text in every language, where it says {placeholder} ("heal" or "{heal}"), and again
        /// whenever the setting changes (the server's values arriving through ServerSettings too) or the language does. For a text the game
        /// shows from "$key" (an item's, piece's, skill's or status effect's description); T(key) gets it filled as well. Numbers are written
        /// the invariant way (1.5); format: your own ("0.#", a percent...).
        /// </summary>
        public static void Fill<TValue>(string key, string placeholder, ConfigEntry<TValue> setting, Func<TValue, string> format = null)
        {
            if (setting == null) return;
            Fill(key, placeholder, () => format != null ? format(setting.Value) : Text(setting.Value), setting);
        }

        /// <summary>
        /// The same with a value worked out in code (from several settings, a game value...). It is asked again when the language changes,
        /// when any of the settings given changes, and on Refresh(). Filling the same key and placeholder again replaces the earlier one.
        /// </summary>
        public static void Fill(string key, string placeholder, Func<string> value, params ConfigEntryBase[] settings)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(placeholder) || value == null) return;
            key = key.TrimStart('$');
            string tag = placeholder.StartsWith("{") ? placeholder : "{" + placeholder + "}";
            if (!Fills.TryGetValue(key, out List<Blank> blanks)) Fills[key] = blanks = new List<Blank>();
            int old = blanks.FindIndex(b => b.Tag == tag);
            if (old >= 0) { blanks[old].Unhook?.Invoke(); blanks.RemoveAt(old); }
            var blank = new Blank { Tag = tag, Value = value };
            if (settings != null)
            {
                EventHandler changed = (s, e) => Show(key);
                foreach (ConfigEntryBase setting in settings.Where(s => s != null).Distinct())
                {
                    // ConfigEntry<T>.SettingChanged is per type: through the entry's own event
                    EventInfo ev = setting.GetType().GetEvent("SettingChanged");
                    if (ev == null) continue;
                    ev.AddEventHandler(setting, changed);
                    blank.Unhook += () => ev.RemoveEventHandler(setting, changed);
                }
            }
            blanks.Add(blank);
            Show(key);
        }

        /// <summary>Every filled text again (for values that change without a setting: call it when they do).</summary>
        public static void Refresh()
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null) return;
            foreach (string key in Fills.Keys) if (_given.TryGetValue(key, out string text)) AddWord.Invoke(loc, new object[] { key, FillIn(key, text) });
            EvictCache(loc);
        }

        private static void Show(string key)
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null || !_given.TryGetValue(key, out string text)) return;
            AddWord.Invoke(loc, new object[] { key, FillIn(key, text) });
            EvictCache(loc);
        }

        private static string FillIn(string key, string text)
        {
            if (text == null || !Fills.TryGetValue(key, out List<Blank> blanks)) return text;
            foreach (Blank b in blanks)
            {
                if (text.IndexOf(b.Tag, StringComparison.Ordinal) < 0) continue;   // (a player's own line may have left it out)
                try { text = text.Replace(b.Tag, b.Value() ?? ""); }
                catch (Exception e) { Debug.LogWarning($"Translations: filling {b.Tag} in {key}: {e.Message}"); }
            }
            return text;
        }

        private static string Text(object value) => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";

        // ---- AddWords: words from data at runtime ----

        /// <summary>
        /// Words that come from data at runtime (a pack file, a server's list) for one language, under a group name of the mod's
        /// ("mymod_pack_halls"): they replace that group's earlier words in that language (none or null: removes them). "English" is the
        /// fallback for every language. Kept through language changes; the player's own translation files still win over them.
        /// </summary>
        public static void AddWords(string group, string language, IDictionary<string, string> words)
        {
            if (string.IsNullOrEmpty(group) || string.IsNullOrEmpty(language)) return;
            SetWords(group, language, words);
            Apply();
        }

        /// <summary>The same for several languages at once (language -> key -> text): replaces the whole group.</summary>
        public static void AddWords(string group, IDictionary<string, Dictionary<string, string>> languages)
        {
            if (string.IsNullOrEmpty(group)) return;
            Runtime.Remove(group);
            if (languages != null) foreach (var kv in languages) if (!string.IsNullOrEmpty(kv.Key)) SetWords(group, kv.Key, kv.Value);
            Apply();
        }

        /// <summary>Takes a group's words out of the game (every language).</summary>
        public static void RemoveWords(string group)
        {
            if (group != null && Runtime.Remove(group)) Apply();
        }

        /// <summary>A flat JSON object of strings ({"key": "text", ...}), as the translation files are: for words kept in a data file.</summary>
        public static Dictionary<string, string> ParseJson(string json) => string.IsNullOrEmpty(json) ? new Dictionary<string, string>() : Parse(json);

        private static void SetWords(string group, string language, IDictionary<string, string> words)
        {
            if (!Runtime.TryGetValue(group, out var languages))
                Runtime[group] = languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (words == null || words.Count == 0) { languages.Remove(language); return; }
            var copy = new Dictionary<string, string>();
            foreach (var kv in words) if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null) copy[kv.Key.TrimStart('$')] = kv.Value;
            languages[language] = copy;
        }

        private static void AddRuntime(Dictionary<string, string> words, string language)
        {
            foreach (var languages in Runtime.Values)
                if (languages.TryGetValue(language, out var group)) foreach (var kv in group) words[kv.Key] = kv.Value;
        }

        // ---- giving the game the words ----

        /// <summary>
        /// Each a layer over the last: the English file (every key), runtime English, the built-in language, runtime words in it, then the
        /// player's own file. The fills go in last, into whatever text won.
        /// </summary>
        private static void Apply()
        {
            Localization loc = Localization.instance;
            if (loc == null || AddWord == null) return;
            string language = loc.GetSelectedLanguage();
            var words = new Dictionary<string, string>(_english);
            var files = new Dictionary<string, string>(_english);
            AddRuntime(words, "English");
            if (!string.Equals(language, "English", StringComparison.OrdinalIgnoreCase))
            {
                if (Languages.TryGetValue(language, out var built)) foreach (var kv in built) { words[kv.Key] = kv.Value; files[kv.Key] = kv.Value; }
                AddRuntime(words, language);
            }
            try
            {
                string own = Path.Combine(_overrideDir ?? "", language + ".json");
                if (_overrideDir != null && File.Exists(own)) foreach (var kv in Parse(File.ReadAllText(own))) { words[kv.Key] = kv.Value; files[kv.Key] = kv.Value; }
            }
            catch (Exception e) { Debug.LogWarning($"Translations: couldn't read the player's {language} file: {e.Message}"); }

            // runtime words no longer given (their group replaced or removed) leave the game
            var game = GameWords?.GetValue(loc) as Dictionary<string, string>;
            foreach (string key in _runtimeGiven) if (!words.ContainsKey(key)) game?.Remove(key);
            _runtimeGiven.Clear();
            foreach (var languages in Runtime.Values) foreach (var group in languages.Values) foreach (string key in group.Keys) _runtimeGiven.Add(key);

            _given = words;
            _files = files;
            foreach (var kv in words) AddWord.Invoke(loc, new object[] { kv.Key, FillIn(kv.Key, kv.Value) });
            EvictCache(loc);   // texts already looked up (and cached, maybe as "[key]") are looked up again
        }

        private static void EvictCache(Localization loc)
        {
            object cache = Cache?.GetValue(loc);
            cache?.GetType().GetMethod("EvictAll")?.Invoke(cache, null);
        }

        /// <summary>A flat JSON object of strings ({"key": "text", ...}); no library needed.</summary>
        private static Dictionary<string, string> Parse(string json)
        {
            var result = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(json, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                result[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
            return result;
        }

        private static string Unescape(string s) => Regex.Replace(s, @"\\(u[0-9a-fA-F]{4}|.)", m =>
        {
            string e = m.Groups[1].Value;
            switch (e[0])
            {
                case 'n': return "\n";
                case 't': return "\t";
                case 'u': return ((char)Convert.ToInt32(e.Substring(1), 16)).ToString();
                default: return e;   // \" \\ \/
            }
        });
    }
}
