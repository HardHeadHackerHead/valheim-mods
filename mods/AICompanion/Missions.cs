using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>One thing it set out to do: make a piece of gear, or upgrade one.</summary>
    internal class Mission
    {
        public string Key = "", What = "", Status = "active", Why = "";  // Status: active, done, dropped
        public int Day, StartCost, Cost;                                 // the game day it began; how much it lacked then, and now
    }

    /// <summary>
    /// Its missions: what it works toward, kept and seen. It sets itself one (its best next piece of gear, Goals) and sticks to it until it is
    /// done (it no longer switches to whatever is cheapest that minute). It decides for itself to give one up, and says why: when it has waited
    /// a long time for something only you can give and has other missions it can do itself, when it has made no headway for a long while, or
    /// when the mission cannot be done any more (the station gone). A mission it gave up it does not take up again for an hour. Each one goes
    /// into its journal (begun, done, given up); the last ones are shown in its menu (Home tab), with what the current one still needs.
    /// </summary>
    internal static class Missions
    {
        public const string Key = "dhc_missions";
        private const int Keep = 12;

        public static List<Mission> All(Humanoid c)
        {
            var list = new List<Mission>();
            foreach (string line in (Companion.Zdo(c)?.GetString(Key, "") ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('|');
                if (f.Length < 7) continue;
                list.Add(new Mission { Status = f[0], Key = f[1], What = f[2], Day = Int(f[3]), StartCost = Int(f[4]), Cost = Int(f[5]), Why = f[6] });
            }
            return list;
        }

        private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');

        private static void Save(Humanoid c, List<Mission> list)
        {
            // the active one, and the last of the rest
            var keep = list.Where(m => m.Status == "active").Concat(list.Where(m => m.Status != "active").Reverse().Take(Keep).Reverse()).ToList();
            Companion.Zdo(c)?.Set(Key, string.Join("\n", keep.Select(m => string.Join("|", m.Status, Clean(m.Key), Clean(m.What), m.Day, m.StartCost, m.Cost, Clean(m.Why)))));
        }

        public static Mission Current(Humanoid c) => All(c).LastOrDefault(m => m.Status == "active");

        /// <summary>A mission it gave up within the hour: not taken up again yet.</summary>
        public static bool Blocked(BrainState st, string key) => key != null && st.DroppedUntil.TryGetValue(key, out float until) && Time.time < until;

        /// <summary>The goal's key: "up:$item_axe_stone:2", "make:Recipe_AxeFlint".</summary>
        public static string KeyOf(Goal g) => g == null ? null : g.Item != null ? $"up:{g.Item.m_shared.m_name}:{g.Item.m_quality + 1}" : g.Recipe != null ? $"make:{g.Recipe.name}" : null;

        /// <summary>Its mission done (the piece made, or upgraded to that level), by what it has (on it or in its chests).</summary>
        private static bool Achieved(Humanoid me, Mission m)
        {
            IEnumerable<ItemDrop.ItemData> have = me.GetInventory().GetAllItems().Concat(Home.Chests(me).SelectMany(ch => ch.GetInventory().GetAllItems()));
            string[] f = m.Key.Split(':');
            if (f[0] == "up" && f.Length >= 3) return have.Any(i => i.m_shared.m_name == f[1] && i.m_quality >= Int(f[2]));
            if (f[0] == "make" && f.Length >= 2)
            {
                Recipe r = ObjectDB.instance?.m_recipes.FirstOrDefault(x => x != null && x.name == f[1]);
                return r?.m_item != null && have.Any(i => i.m_shared.m_name == r.m_item.m_itemData.m_shared.m_name);
            }
            return false;
        }

        /// <summary>Everything its mission needs is in its bag or its chests (it only has to make it).</summary>
        private static bool Ready(Humanoid me, Mission m)
        {
            string[] f = m.Key.Split(':');
            Recipe r = null;
            int quality = 1;
            if (f[0] == "up" && f.Length >= 3)
            {
                quality = Int(f[2]);
                ItemDrop.ItemData item = me.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == f[1] && i.m_quality == quality - 1);
                if (item == null) return false; // (the piece is gone)
                r = ObjectDB.instance?.GetRecipe(item);
            }
            else if (f[0] == "make" && f.Length >= 2) r = ObjectDB.instance?.m_recipes.FirstOrDefault(x => x != null && x.name == f[1]);
            if (r == null) return false;
            List<Container> chests = Home.Chests(me);
            return Upgrades.Needs(r).All(q => q.m_resItem == null
                || me.GetInventory().CountItems(q.m_resItem.m_itemData.m_shared.m_name) + chests.Sum(c => c.GetInventory().CountItems(q.m_resItem.m_itemData.m_shared.m_name)) >= q.GetAmount(quality));
        }

        /// <summary>
        /// After it looks for its goal (Work, every 30 s): the mission kept up to date. A new goal with none under way: a new mission. Its
        /// mission no longer among what it can work toward: done (it has it) or given up (it cannot be done now). Its mission stuck: given up.
        /// </summary>
        public static void Track(BrainState st, Goal g, IList<Goal> others)
        {
            Humanoid me = st.Body;
            if (Companion.Zdo(me) == null || !me.GetComponent<ZNetView>().IsOwner()) return;
            List<Mission> list = All(me);
            Mission cur = list.LastOrDefault(m => m.Status == "active");
            string key = KeyOf(g);

            if (cur != null && cur.Key != key)
            {
                if (Achieved(me, cur)) Finish(st, list, cur);
                else if (Ready(me, cur))
                {
                    // It has all it needs: it makes it at the station (Upgrades) in a moment. Unless that does not happen for a long while.
                    if (st.MissionReadySince == 0f) st.MissionReadySince = Time.time;
                    if (Time.time - st.MissionReadySince < 600f) return;
                    Drop(st, list, cur, "it had everything but could not get it made (could it not reach the station?)");
                }
                else Drop(st, list, cur, g == null ? "there is nothing it can do toward it from here any more" : "it cannot be done from here any more (the station or the piece is gone)");
                st.MissionReadySince = 0f;
                cur = null;
            }
            else st.MissionReadySince = 0f;
            if (cur == null && g != null && key != null)
            {
                cur = new Mission { Key = key, What = g.What, Day = Journal.Day, StartCost = Mathf.Max(1, g.Cost), Cost = g.Cost };
                list.Add(cur);
                st.MissionSince = Time.time; st.MissionBest = g.Cost; st.MissionAskSince = 0f;
                Journal.Add(me, $"Set itself a mission: {(g.Item != null ? "upgrade its " : "make a ")}{g.What}.");
                Talk.Mention(me, $"New mission: {g.What}.", "mission:" + key, 30f);
                Save(me, list);
                return;
            }
            if (cur == null) return;

            // Under way: how much it still lacks, and whether it is stuck.
            cur.Cost = g.Cost;
            if (st.MissionSince == 0f) { st.MissionSince = Time.time; st.MissionBest = g.Cost; }
            if (g.Cost < st.MissionBest) { st.MissionBest = g.Cost; st.MissionSince = Time.time; } // headway
            bool otherOwn = others.Any(o => o != g && o.Ask.Count == 0 && !Blocked(st, KeyOf(o)));
            if (g.Ask.Count > 0) { if (st.MissionAskSince == 0f) st.MissionAskSince = Time.time; } else st.MissionAskSince = 0f;
            if (st.MissionAskSince > 0f && Time.time - st.MissionAskSince > 1200f && otherOwn)
                Drop(st, list, cur, $"it waited a long time for {string.Join(" and ", g.Ask)}, which it cannot get itself");
            else if (Time.time - st.MissionSince > 1800f && otherOwn)
                Drop(st, list, cur, "it made no headway for a long while");
            else Save(me, list);
        }

        private static void Finish(BrainState st, List<Mission> list, Mission m)
        {
            m.Status = "done"; m.Cost = 0; m.Why = $"day {Journal.Day}";
            Journal.Add(st.Body, $"Mission done: {m.What}.");
            Talk.Mention(st.Body, $"Done: {m.What}!", "missiondone:" + m.Key, 10f);
            st.MissionSince = 0f;
            Save(st.Body, list);
        }

        private static void Drop(BrainState st, List<Mission> list, Mission m, string why)
        {
            m.Status = "dropped"; m.Why = why;
            st.DroppedUntil[m.Key] = Time.time + 3600f;
            Journal.Add(st.Body, $"Gave up on its mission ({m.What}): {why}.");
            Talk.Mention(st.Body, $"I'm giving up on the {m.What} for now: {why}.", "missiondrop:" + m.Key, 30f);
            st.MissionSince = 0f;
            st.NextUpgradeLook = 0f; // (a new one at once)
            Save(st.Body, list);
        }
    }
}
