using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    public enum Duty { Wood, Food, Mining }

    /// <summary>
    /// Its home duties: a short list in the order you set (Home tab), each with a stockpile it keeps up in its own chests: collect wood,
    /// hunt and forage food, mine stone and ore. At home it works the first duty that is switched on and whose stockpile is below what you
    /// asked for, carrying what it gathers to its chest. A duty with nothing to find near home rests a minute and the next one is worked.
    /// Every duty full (or none switched on), it goes back to living its own life: its gear goals, repairs, the fire, a stroll.
    /// Kept in its ZDO as "Wood:1:100,Food:0:40,Mining:0:100" (duty, on, stockpile), in priority order.
    /// </summary>
    internal static class Duties
    {
        public const string Key = "dhc_duties";

        internal class Entry
        {
            public Duty Duty;
            public bool On;
            public int Target;

            public Job Jobs => Duty == Duty.Wood ? Job.Wood : Duty == Duty.Food ? Job.Forage | Job.Hunt | Job.Cook : Job.Stone | Job.Ore;
        }

        public static string Label(Duty d) => d == Duty.Wood ? "Collect wood" : d == Duty.Food ? "Hunt and forage food" : "Mine stone and ore";
        public static string Unit(Duty d) => d == Duty.Wood ? "wood" : d == Duty.Food ? "food" : "stone and ore";
        public static int Step(Duty d) => d == Duty.Food ? 10 : 20;
        public static int Max(Duty d) => d == Duty.Food ? 200 : 600;
        private static int Default(Duty d) => d == Duty.Food ? 40 : 100;

        // ---- the saved list ---------------------------------------------------------------------------------------

        /// <summary>A fresh copy of its duties, in priority order (always all of them), to change and Write.</summary>
        public static List<Entry> Read(ZDO z)
        {
            var list = new List<Entry>();
            foreach (string part in (z?.GetString(Key, "") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = part.Split(':');
                if (f.Length < 3 || !Enum.TryParse(f[0], out Duty d) || list.Any(e => e.Duty == d)) continue;
                int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int target);
                list.Add(new Entry { Duty = d, On = f[1] == "1", Target = Mathf.Clamp(target > 0 ? target : Default(d), Step(d), Max(d)) });
            }
            foreach (Duty d in Enum.GetValues(typeof(Duty))) if (list.All(e => e.Duty != d)) list.Add(new Entry { Duty = d, Target = Default(d) });
            return list;
        }

        public static void Write(ZDO z, List<Entry> list) => z.Set(Key, string.Join(",", list.Select(e => $"{e.Duty}:{(e.On ? 1 : 0)}:{e.Target}")));

        private static readonly Dictionary<string, List<Entry>> Parsed = new Dictionary<string, List<Entry>>();

        /// <summary>Its duties for reading often (every frame): parsed once per saved value. Never change what it returns.</summary>
        private static List<Entry> Cached(Component c)
        {
            ZDO z = Companion.Zdo(c);
            if (z == null) return new List<Entry>();
            string raw = z.GetString(Key, "");
            if (!Parsed.TryGetValue(raw, out List<Entry> list))
            {
                if (Parsed.Count > 40) Parsed.Clear();
                Parsed[raw] = list = Read(z);
            }
            return list;
        }

        /// <summary>Any duty switched on: then these, not the plain job ticks, say what it does at home.</summary>
        public static bool Configured(Component c) => Cached(c).Any(e => e.On);

        // ---- what counts toward a stockpile -------------------------------------------------------------------------

        private static readonly HashSet<string> WoodNames = new HashSet<string>
            { "$item_wood", "$item_finewood", "$item_corewood", "$item_elderbark", "$item_yggdrasilwood", "$item_blackwood", "$item_ashwood" };

        public static bool Counts(Duty d, ItemDrop.ItemData i)
        {
            string name = i.m_shared.m_name;
            bool material = i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material;
            switch (d)
            {
                case Duty.Wood: return material && WoodNames.Contains(name);
                case Duty.Food: return Food.IsFood(i) || Work.IsCookable(i);
                default: return material && (name == "$item_stone" || name.EndsWith("ore", StringComparison.Ordinal) || name.EndsWith("scrap", StringComparison.Ordinal));
            }
        }

        private static readonly Dictionary<long, int[]> Haves = new Dictionary<long, int[]>();
        private static readonly Dictionary<long, float> HaveAt = new Dictionary<long, float>();
        private static readonly Dictionary<long, float[]> Idle = new Dictionary<long, float[]>();
        private static readonly Dictionary<long, float> LastDelivery = new Dictionary<long, float>();

        /// <summary>How much of what the duty gathers it has: in its bag and in its own chests (with none of its own, in yours). Looked up every few seconds.</summary>
        public static int Have(Humanoid me, Duty d)
        {
            long id = Companion.IdOf(me);
            if (!Haves.TryGetValue(id, out int[] have)) Haves[id] = have = new int[Enum.GetValues(typeof(Duty)).Length];
            if (!HaveAt.TryGetValue(id, out float at) || Time.time - at > 3f)
            {
                HaveAt[id] = Time.time;
                Array.Clear(have, 0, have.Length);
                List<Container> chests = Home.Chests(me);
                IEnumerable<Container> where = chests.Count > 0 ? chests : Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f);
                IEnumerable<ItemDrop.ItemData> items = me.GetInventory().GetAllItems().Concat(where.SelectMany(c => c.GetInventory().GetAllItems()));
                foreach (ItemDrop.ItemData i in items)
                    foreach (Duty each in Enum.GetValues(typeof(Duty)))
                        if (Counts(each, i)) have[(int)each] += i.m_stack;
            }
            return have[(int)d];
        }

        // ---- which one it works now -----------------------------------------------------------------------------------

        public static bool Resting(Humanoid me, Duty d) => Idle.TryGetValue(Companion.IdOf(me), out float[] until) && until[(int)d] > Time.time;

        /// <summary>The first duty switched on whose stockpile is below its target and that is not resting. Null: its own life.</summary>
        public static Entry Active(Humanoid me)
        {
            foreach (Entry e in Cached(me))
                if (e.On && Have(me, e.Duty) < e.Target && !Resting(me, e.Duty)) return e;
            return null;
        }

        /// <summary>It found nothing to gather for this duty near home: the next one for a minute.</summary>
        public static void Rest(Humanoid me, Entry e)
        {
            long id = Companion.IdOf(me);
            if (!Idle.TryGetValue(id, out float[] until)) Idle[id] = until = new float[Enum.GetValues(typeof(Duty)).Length];
            until[(int)e.Duty] = Time.time + 60f;
        }

        /// <summary>Whether some duty not yet stocked is on, even if resting (for catching up while you were away).</summary>
        public static bool Open(Humanoid me, Duty d) => Cached(me).Any(e => e.Duty == d && e.On && Have(me, d) < e.Target);

        /// <summary>The jobs of every duty still short of its stockpile.</summary>
        public static Job OpenJobs(Humanoid me)
        {
            Job j = Job.None;
            foreach (Entry e in Cached(me)) if (e.On && Have(me, e.Duty) < e.Target) j |= e.Jobs;
            return j;
        }

        // ---- the stockpile in its chests ------------------------------------------------------------------------------

        /// <summary>How many of this item its own chests keep for a duty (0 when it is none of its duties, or it has no chest of its own).</summary>
        public static int Cap(Humanoid me, ItemDrop.ItemData i)
        {
            List<Entry> list = Cached(me);
            if (!list.Any(e => e.On) || Home.Chests(me).Count == 0) return 0;
            foreach (Entry e in list) if (e.On && Counts(e.Duty, i)) return e.Target;
            return 0;
        }

        /// <summary>
        /// Time to carry it to its chest: it has a load of what a duty gathers (20), or what it carries would fill the stockpile, or it carries
        /// some of what another duty (not the one it works now) gathers. No more than once a minute, and food it keeps for itself (10) does not count.
        /// </summary>
        public static bool Deliver(Humanoid me, Entry active)
        {
            long id = Companion.IdOf(me);
            if (LastDelivery.TryGetValue(id, out float last) && Time.time - last < 60f) return false;
            if (Home.Chests(me).Count == 0) return false;
            Inventory inv = me.GetInventory();
            foreach (Entry e in Cached(me))
            {
                if (!e.On) continue;
                int carried = inv.GetAllItems().Where(i => Counts(e.Duty, i) && !Work.Keeps(me, i)).Sum(i => i.m_stack) - (e.Duty == Duty.Food ? 10 : 0);
                if (carried <= 0) continue;
                if (carried >= 20 || e != active || Have(me, e.Duty) >= e.Target) { LastDelivery[id] = Time.time; return true; }
            }
            return false;
        }

        // ---- for its menu ---------------------------------------------------------------------------------------------

        public static string Status(Humanoid me, Entry e, Entry active)
        {
            int have = Have(me, e.Duty);
            if (!e.On) return "Off";
            if (have >= e.Target) return $"Stocked: {have} of {e.Target}";
            if (e == active) return $"Working on it now: {have} of {e.Target}";
            if (Resting(me, e.Duty)) return $"Nothing to find near home just now: {have} of {e.Target}";
            return $"Waiting its turn: {have} of {e.Target}";
        }
    }
}
