using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

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

        // ---- what goes in it ------------------------------------------------------------------------------------

        public static void Kill(Humanoid c, Character victim)
        {
            string name = Localization.instance.Localize(victim.m_name);
            int n = Count(c, "kill:" + name);
            Count(c, "kills");
            if (victim.IsBoss())
            {
                Count(c, "boss:" + name);
                Add(c, $"Helped bring down {name}!");
                Idle.Queue(Brain.Get(c), "cheer", 1.5f);
                Banter.Say(c, "bosskill", 0f, $"{name} is dead! What a fight.", $"We did it! {name} is no more.", $"Ha! {name} won't trouble us again.");
            }
            else if (Tactics.Feared(c, victim))
            {
                Idle.Queue(Brain.Get(c), "cheer", 1f);
                Banter.Say(c, "revenge:" + name, 5f, $"Ha! Not this time, {name.ToLowerInvariant()}.", "That's for last time!", $"One {name.ToLowerInvariant()} fewer.");
            }
            if (!victim.IsBoss() && n == 1) Add(c, $"Killed its first {name.ToLowerInvariant()}.");
            else if (!victim.IsBoss() && (n == 10 || n == 50 || n == 100)) Add(c, $"Killed its {n}th {name.ToLowerInvariant()}.");
        }

        public static void Parried(Humanoid c, Character attacker)
        {
            if (Count(c, "parries") == 1) Add(c, $"Parried a blow for the first time (a {Localization.instance.Localize(attacker.m_name).ToLowerInvariant()}).");
        }

        public static void Fell(Humanoid c, string by, Vector3 where)
        {
            int n = Count(c, "deaths");
            if (by != "?" && by.Length > 0 && char.IsUpper(by[0])) Count(c, "killedby:" + by); // a creature (not poison or a fall)
            Add(c, $"Fell to {(by == "?" ? "something" : by)}{(n > 1 ? $" (fall number {n})" : "")}.");
        }

        public static void Made(Humanoid c, string what) { Count(c, "made"); Add(c, $"Made {what}."); }
        public static void Upgraded(Humanoid c, string what) { Count(c, "upgraded"); Add(c, $"Upgraded {what}."); }
        public static void Trip(Humanoid c, string what) { Count(c, "trips"); Add(c, what); }

        public static void Skill(Humanoid c, string skill, int level)
        {
            if (level % 10 != 0) return;
            Add(c, $"Reached {level} in {skill}.");
            Banter.Say(c, "skill", 3f, $"I'm getting the hang of {skill.ToLowerInvariant()}.", $"{skill} {level}! Getting better.", "Practice pays off.");
        }

        /// <summary>A land it had not been to before (by biome), or a dungeon.</summary>
        public static bool Place(Humanoid c, string place, bool dungeon)
        {
            if (Count(c, "place:" + place) != 1) return false;
            Add(c, dungeon ? $"Went into a {place} with you." : $"Set foot in the {place} for the first time.");
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

        /// <summary>One of the lines, unless it spoke a moment ago or said this kind of thing lately (minutes).</summary>
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
            if (se.HaveStatusEffect(SEMan.s_statusEffectWet)) Say(me, "wet", 15f, "Rain again... my beard's soaked.", "Wet to the bone.", "Could do with a roof right now.");
            else if (se.HaveStatusEffect(SEMan.s_statusEffectFreezing)) Say(me, "cold", 10f, "Freezing! We need a fire.", "My fingers are numb.", "Odin's beard, it's cold.");
            if (EnvMan.instance != null && EnvMan.IsNight() && !st.SaidNight) { st.SaidNight = true; Say(me, "night", 20f, "It's getting dark.", "Night's falling. Watch for greylings.", "The stars are out."); }
            if (EnvMan.instance != null && !EnvMan.IsNight()) st.SaidNight = false;
            if (master != null && st.InCombat && master.GetHealthPercentage() < 0.3f && Vector3.Distance(master.transform.position, me.transform.position) < 20f)
                Say(me, "youhurt", 2f, "Careful, you're hurt!", "Get back, I'll hold them!", "Eat something, quick!");
            if (!st.InCombat && Companion.OrderOf(me) == Order.Gather && !st.Asleep && UnityEngine.Random.value < 0.05f)
                Say(me, "idle", 12f, "Nice day for it.", "I could use a mead.", "The fire's warm today.", "Another log for the pile.", "Quiet. I like quiet.", "Wonder what's past those hills.");
        }

        public static void CloseFight(Humanoid c) => Say(c, "close", 3f, "That was too close.", "Still standing!", "That hurt more than I'll admit.", "Phew. Next time, more mead first.");
        public static void BossSeen(Humanoid c, string boss) => Say(c, "boss:" + boss, 10f, $"That's {boss}! Keep moving, I'll take its side.", $"{boss}... stay out of its reach!", $"Here comes {boss}. Together, then!");
        public static void Dungeon(Humanoid c) => Say(c, "dungeon", 5f, "Smells like death in here.", "Stay close. It's dark.", "Treasure, or a grave. Maybe both.");
        public static void NewLand(Humanoid c, string land) => Say(c, "land:" + land, 60f, $"So this is the {land}.", $"The {land}... I've heard stories.", $"First time in the {land}. Watch your step.");
    }
}
