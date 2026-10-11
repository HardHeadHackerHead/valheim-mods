using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static AICompanion.Words;

namespace AICompanion
{
    /// <summary>
    /// Its saga: a tally of what it has done (kills by creature, parries, blocks, bosses, deaths, gear made and upgraded, trips, places seen)
    /// and the moments worth remembering (its first kill of each creature, first parry, every boss, every fall, gear it made, skill milestones,
    /// new lands, dungeons). Kept in its save and in its profile, so it lasts through falls and restarts. Shown in the Journal tab.
    /// </summary>
    internal static class Journal
    {
        public const string EntriesKey = "dhc_journal", TallyKey = "dhc_tally", SinceKey = "dhc_since";
        private const int MaxEntries = 150;

        /// <summary>The in-game day now (the game's own day count).</summary>
        public static int Day => EnvMan.instance != null ? EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds()) : 0;

        /// <summary>An entry (packed text, Words.P; it is saved, and each player reads it in their own language).</summary>
        public static void Add(Humanoid c, string text)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null || string.IsNullOrEmpty(text) || !c.GetComponent<ZNetView>().IsOwner()) return;
            var lines = Entries(c);
            lines.Add($"{Day}|{text.Replace('\n', ' ').Replace('|', '/')}");
            if (lines.Count > MaxEntries) lines.RemoveRange(0, lines.Count - MaxEntries);
            z.Set(EntriesKey, string.Join("\n", lines));
            Activity.Log(c, "journal: " + text);
        }

        public static List<string> Entries(Humanoid c) =>
            (Companion.Zdo(c)?.GetString(EntriesKey, "") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        public static Dictionary<string, int> Tally(Humanoid c)
        {
            var map = new Dictionary<string, int>();
            foreach (string part in (Companion.Zdo(c)?.GetString(TallyKey, "") ?? "").Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq > 0 && int.TryParse(part.Substring(eq + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) map[part.Substring(0, eq)] = n;
            }
            return map;
        }

        /// <summary>Count one more of something; the new count (1: the first time).</summary>
        public static int Count(Humanoid c, string what, int n = 1)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null || !c.GetComponent<ZNetView>().IsOwner()) return 0;
            var map = Tally(c);
            map[what] = (map.TryGetValue(what, out int had) ? had : 0) + n;
            z.Set(TallyKey, string.Join(";", map.Select(kv => $"{kv.Key.Replace(';', ',').Replace('=', '-')}={kv.Value}")));
            return map[what];
        }

        /// <summary>
        /// A count in its tally by a creature's (or land's) token ("kill:$enemy_boar"), with what was counted under its name before 0.24
        /// ("kill:Boar", the name in its owner's language then) added in: old saves keep their counts.
        /// </summary>
        public static int Tallied(Dictionary<string, int> tally, string prefix, string token, string old = null)
        {
            int n = tally.TryGetValue(prefix + token, out int now) ? now : 0;
            if (old == null && token.StartsWith("$") && Localization.instance != null) old = Localization.instance.Localize(token);
            if (old != null && old != token && tally.TryGetValue(prefix + old, out int before)) n += before;
            return n;
        }

        // ---- what goes in it (packed text, Words.P: each player reads it in their own language) ------------------

        public static void Kill(Humanoid c, Character victim)
        {
            string name = victim.m_name;   // (its token: "$enemy_boar")
            Count(c, "kill:" + name);
            int n = Tallied(Tally(c), "kill:", name);
            Count(c, "kills");
            if (victim.IsBoss())
            {
                Count(c, "boss:" + name);
                Add(c, P("aicompanion_journal_boss", name));
                Idle.Queue(Brain.Get(c), "cheer", 1.5f);
                Banter.Say(c, "bosskill", 0f, P("aicompanion_journal_boss_say_1", name), P("aicompanion_journal_boss_say_2", name), P("aicompanion_journal_boss_say_3", name));
            }
            else if (Tactics.Feared(c, victim))
            {
                Idle.Queue(Brain.Get(c), "cheer", 1f);
                Banter.Say(c, "revenge:" + name, 5f, P("aicompanion_journal_revenge_1", Low(name)), P("aicompanion_journal_revenge_2"), P("aicompanion_journal_revenge_3", Low(name)));
            }
            if (!victim.IsBoss() && n == 1) Add(c, P("aicompanion_journal_first_kill", Low(name)));
            else if (!victim.IsBoss() && (n == 10 || n == 50 || n == 100)) Add(c, P("aicompanion_journal_nth_kill", n, Low(name)));
        }

        public static void Parried(Humanoid c, Character attacker)
        {
            if (Count(c, "parries") == 1) Add(c, P("aicompanion_journal_first_parry", Low(attacker.m_name)));
        }

        /// <param name="by">What felled it: a creature's token ("$enemy_troll"), a damage-over-time effect's token, a kind of hit ("fall",
        /// "drowning"...), or "?".</param>
        public static void Fell(Humanoid c, string by, Vector3 where)
        {
            int n = Count(c, "deaths");
            bool effect = by.StartsWith("$se_");
            if (by.StartsWith("$") ? !effect : by.Length > 0 && char.IsUpper(by[0])) Count(c, "killedby:" + by); // a creature (not poison or a fall)
            string what = by == "?" ? P("aicompanion_journal_something") : effect ? Low(by) : by.StartsWith("$") ? by
                        : HitWords.Contains(by) ? P("aicompanion_journal_hit_" + by) : by;
            Add(c, n > 1 ? P("aicompanion_journal_fell_again", what, n) : P("aicompanion_journal_fell", what));
        }

        /// <summary>The kinds of hit (HitData.HitType, in lower case) its journal has words for; others show as the game names them.</summary>
        private static readonly HashSet<string> HitWords = new HashSet<string> { "fall", "drowning", "burning", "freezing", "poisoned", "smoke", "impact", "tree", "cart", "boat", "water", "edgeofworld", "structural" };

        /// <summary>It made a piece of gear (its m_name token, "$item_axe_flint").</summary>
        public static void Made(Humanoid c, string itemToken) { Count(c, "made"); Add(c, P("aicompanion_journal_made", Low(itemToken))); }

        /// <summary>It upgraded a piece of gear (its m_name token) to a level.</summary>
        public static void Upgraded(Humanoid c, string itemToken, int level) { Count(c, "upgraded"); Add(c, P("aicompanion_journal_upgraded", Low(itemToken), level)); }

        /// <summary>A trip: the line as given (packed text).</summary>
        public static void Trip(Humanoid c, string packedLine) { Count(c, "trips"); Add(c, packedLine); }

        /// <summary>A skill (its token, "$skill_swords") at a new level: every tenth goes in.</summary>
        public static void Skill(Humanoid c, string skill, int level)
        {
            if (level % 10 != 0) return;
            Add(c, P("aicompanion_journal_skill", level, skill));
            Banter.Say(c, "skill", 3f, P("aicompanion_journal_skill_say_1", Low(skill)), P("aicompanion_journal_skill_say_2", skill, level), P("aicompanion_journal_skill_say_3"));
        }

        /// <summary>
        /// A land it had not been to before (its token, "$biome_swamp"), or a dungeon ("dungeon"). <paramref name="old"/>: the word the tally
        /// used for that land before 0.24, so a land seen then does not count as new.
        /// </summary>
        public static bool Place(Humanoid c, string place, bool dungeon, string old = null)
        {
            Count(c, "place:" + place);
            if (Tallied(Tally(c), "place:", place, old ?? place) != 1) return false;
            Add(c, dungeon ? P("aicompanion_journal_dungeon") : P("aicompanion_journal_new_land", place));
            return true;
        }

        public static void Started(Humanoid c)
        {
            ZDO z = Companion.Zdo(c);
            if (z != null && z.GetInt(SinceKey, -1) < 0) z.Set(SinceKey, Day);
        }

        public static int DaysWithYou(Humanoid c) => Mathf.Max(0, Day - (Companion.Zdo(c)?.GetInt(SinceKey, Day) ?? Day));
    }

    /// <summary>
    /// Small talk: a line above its head now and then that fits the moment (a close fight, a boss, a dungeon, rain, nightfall, you badly hurt,
    /// a new land, a quiet moment at home). Hand-written lines, at most one every 40 seconds, each kind of moment only so often. Off with
    /// "Tells you what it is up to" (Orders tab).
    /// </summary>
    internal static class Banter
    {
        private static readonly Dictionary<long, float> LastAny = new Dictionary<long, float>();
        private static readonly Dictionary<string, float> LastTopic = new Dictionary<string, float>();

        public static void Forget() { LastAny.Clear(); LastTopic.Clear(); }

        /// <summary>One of the lines (packed text, Words.P), unless it spoke a moment ago or said this kind of thing lately (minutes).</summary>
        public static void Say(Humanoid c, string topic, float minutes, params string[] lines)
        {
            if (c == null || lines.Length == 0 || !Talk.Chatty(c)) return;
            long id = Companion.IdOf(c);
            if (LastAny.TryGetValue(id, out float any) && Time.time - any < 40f) return;
            string key = id + ":" + topic;
            if (minutes > 0f && LastTopic.TryGetValue(key, out float last) && Time.time - last < minutes * 60f) return;
            LastAny[id] = Time.time;
            LastTopic[key] = Time.time;
            string line = lines[UnityEngine.Random.Range(0, lines.Length)];
            Talk.Say(c, line);
            Activity.Log(c, "small talk: " + line);
        }

        /// <summary>Every few seconds on the game running it: the moments worth a word.</summary>
        public static void Tick(BrainState st, Player master)
        {
            if (Time.time < st.NextBanterLook) return;
            st.NextBanterLook = Time.time + 3f;
            Humanoid me = st.Body;
            SEMan se = me.GetSEMan();
            if (se.HaveStatusEffect(SEMan.s_statusEffectWet)) Say(me, "wet", 15f, Lines("wet", 3));
            else if (se.HaveStatusEffect(SEMan.s_statusEffectFreezing)) Say(me, "cold", 10f, Lines("cold", 3));
            if (EnvMan.instance != null && EnvMan.IsNight() && !st.SaidNight) { st.SaidNight = true; Say(me, "night", 20f, Lines("night", 3)); }
            if (EnvMan.instance != null && !EnvMan.IsNight()) st.SaidNight = false;
            if (master != null && st.InCombat && master.GetHealthPercentage() < 0.3f && Vector3.Distance(master.transform.position, me.transform.position) < 20f)
                Say(me, "youhurt", 2f, Lines("youhurt", 3));
            if (!st.InCombat && Companion.OrderOf(me) == Order.Gather && !st.Asleep && UnityEngine.Random.value < 0.05f)
                Say(me, "idle", 12f, Lines("idle", 6));
        }

        public static void CloseFight(Humanoid c) => Say(c, "close", 3f, Lines("close", 4));
        /// <param name="boss">The boss's token ("$enemy_eikthyr").</param>
        public static void BossSeen(Humanoid c, string boss) => Say(c, "boss:" + boss, 10f, Lines("boss", 3, boss));
        public static void Dungeon(Humanoid c) => Say(c, "dungeon", 5f, Lines("dungeon", 3));
        /// <param name="land">The land's token ("$biome_swamp").</param>
        public static void NewLand(Humanoid c, string land) => Say(c, "land:" + land, 60f, Lines("land", 3, land));

        /// <summary>A moment's lines: aicompanion_banter_&lt;topic&gt;_1 to _n, packed (Words.P), each with the same values.</summary>
        private static string[] Lines(string topic, int n, params object[] args) =>
            Enumerable.Range(1, n).Select(i => P($"aicompanion_banter_{topic}_{i}", args)).ToArray();
    }
}
