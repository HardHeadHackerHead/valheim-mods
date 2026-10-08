using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    public enum Duty { Food, Wood, Mining }

    /// <summary>
    /// Its home duties: a short list in the order you set (Home tab), each with a stockpile it keeps up in its own chests: collect wood,
    /// hunt and forage food, mine stone and ore. The stockpile is kept in your chests at home (it works for you; what is there already counts),
    /// or in a chest of its own. At home it works the first duty that is switched on and whose stockpile is below what you
    /// asked for, carrying what it gathers to its chest. A duty with nothing to find near home rests a minute and the next one is worked.
    /// Every duty full (or none switched on), it goes back to living its own life: its gear goals, repairs, the fire, a stroll.
    /// Kept in its ZDO as "Wood:1:100,Food:0:40,Mining:0:100" (duty, on, stockpile), in priority order.
    /// </summary>
    internal static class Duties
    {
        public const string Key = "dhc_duties", DestKey = "dhc_dutydest";

        /// <summary>Where the stockpile is: in your chests at home (the default: it works for you) or in a chest of its own.</summary>
        public static bool ToYours(Component c) => (Companion.Zdo(c)?.GetInt(DestKey, 0) ?? 0) == 0;

        private static readonly Dictionary<long, KeyValuePair<float, bool>> Places = new Dictionary<long, KeyValuePair<float, bool>>();

        /// <summary>There is somewhere to put it: a chest of yours near home it may use (putting things in yours allowed), or a chest of its own. Looked up every few seconds.</summary>
        public static bool HasPlace(Humanoid me)
        {
            long id = Companion.IdOf(me);
            if (Places.TryGetValue(id, out var hit) && Time.time - hit.Key < 3f) return hit.Value;
            bool ok = ToYours(me) ? Work.Stows(me) && Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).Any() : Home.Chests(me).Count > 0;
            Places[id] = new KeyValuePair<float, bool>(Time.time, ok);
            return ok;
        }

        internal class Entry
        {
            public Duty Duty;
            public bool On;
            public int Target;

            public Job Jobs => Duty == Duty.Wood ? Job.Wood : Duty == Duty.Food ? Job.Forage | Job.Hunt | Job.Cook : Job.Stone | Job.Ore;
        }

        public static string Label(Duty d) => d == Duty.Wood ? "Collect wood" : d == Duty.Food ? "Hunt and forage food" : "Mine stone and ore";
        public static string Unit(Duty d) => d == Duty.Wood ? "wood" : d == Duty.Food ? "good meals" : "stone and ore";
        public static int Step(Duty d) => d == Duty.Food ? 10 : 20;
        public static int Max(Duty d) => d == Duty.Food ? 300 : 600;
        private static int Default(Duty d) => d == Duty.Food ? 60 : 100;

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

        /// <summary>Any duty switched on and a chest to keep the stockpile in: then these, not the plain job ticks, say what it does at home.</summary>
        public static bool Configured(Humanoid c) => Cached(c).Any(e => e.On) && HasPlace(c);

        /// <summary>Any duty switched on (for its menu).</summary>
        public static bool AnyOn(Component c) => Cached(c).Any(e => e.On);

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

        /// <summary>
        /// How much of a meal one of this food is: cooked meat and the like (50 health and stamina between them or more) a whole one, a berry or a
        /// mushroom a third, raw food still to cook half. So a larder of mushrooms never counts as stocked, and it goes on hunting and cooking.
        /// </summary>
        public static float MealWeight(ItemDrop.ItemData i) =>
            Food.IsFood(i) ? Mathf.Clamp((i.m_shared.m_food + i.m_shared.m_foodStamina) / 50f, 0.2f, 1f) : 0.5f;

        private static readonly Dictionary<long, int[]> Haves = new Dictionary<long, int[]>();
        private static readonly Dictionary<long, float> HaveAt = new Dictionary<long, float>();
        private static readonly Dictionary<long, float[]> Idle = new Dictionary<long, float[]>();
        private static readonly Dictionary<long, int[]> Strikes = new Dictionary<long, int[]>();
        private static readonly Dictionary<long, float> LastDelivery = new Dictionary<long, float>();

        /// <summary>How much of the duty's stock the chests it keeps it in hold (yours at home, or its own), and what it carries to put there (not its food: that is its own to eat). Looked up every few seconds.</summary>
        public static int Have(Humanoid me, Duty d)
        {
            long id = Companion.IdOf(me);
            if (!Haves.TryGetValue(id, out int[] have)) Haves[id] = have = new int[Enum.GetValues(typeof(Duty)).Length];
            if (!HaveAt.TryGetValue(id, out float at) || Time.time - at > 3f)
            {
                HaveAt[id] = Time.time;
                Array.Clear(have, 0, have.Length);
                var kinds = new Dictionary<string, float>();
                foreach (Container chest in ToYours(me) ? Work.YourChests(me, Work.Center(me), Work.RadiusOf(me) + 20f).ToList() : Home.Chests(me))
                    foreach (ItemDrop.ItemData i in chest.GetInventory().GetAllItems())
                        foreach (Duty each in Enum.GetValues(typeof(Duty)))
                            if (each == Duty.Food) { if (Counts(each, i)) kinds[i.m_shared.m_name] = (kinds.TryGetValue(i.m_shared.m_name, out float k) ? k : 0f) + i.m_stack * MealWeight(i); }
                            else if (Counts(each, i)) have[(int)each] += i.m_stack;
                // Food is counted in good meals: a mushroom is a fraction of a meal, and no one food counts for more than 60% of the target (variety).
                float cap = Mathf.Max(1f, (Cached(me).FirstOrDefault(e => e.Duty == Duty.Food)?.Target ?? 40) * 0.6f);
                have[(int)Duty.Food] = Mathf.FloorToInt(kinds.Values.Sum(v => Mathf.Min(v, cap)));
                foreach (ItemDrop.ItemData i in me.GetInventory().GetAllItems())
                    if (!Work.Keeps(me, i))
                        foreach (Duty each in Enum.GetValues(typeof(Duty)))
                            if (each != Duty.Food && Counts(each, i)) have[(int)each] += i.m_stack; // (what it carries is on its way to the chest)
            }
            return have[(int)d];
        }

        // ---- which one it works now -----------------------------------------------------------------------------------

        public static bool Resting(Humanoid me, Duty d) => Idle.TryGetValue(Companion.IdOf(me), out float[] until) && until[(int)d] > Time.time;

        /// <summary>The first duty switched on whose stockpile is below its target and that is not resting. Null: its own life.</summary>
        public static Entry Active(Humanoid me)
        {
            if (!HasPlace(me)) return null;
            foreach (Entry e in Cached(me))
                if (e.On && Have(me, e.Duty) < e.Target && !Resting(me, e.Duty)) return e;
            return null;
        }

        /// <summary>
        /// It found nothing to gather for this duty near home: the next one for a minute, and each time in a row twice as long (five minutes at most).
        /// How many times in a row.
        /// </summary>
        public static int Rest(Humanoid me, Entry e)
        {
            long id = Companion.IdOf(me);
            int n = Enum.GetValues(typeof(Duty)).Length;
            if (!Idle.TryGetValue(id, out float[] until)) Idle[id] = until = new float[n];
            if (!Strikes.TryGetValue(id, out int[] strikes)) Strikes[id] = strikes = new int[n];
            int k = ++strikes[(int)e.Duty];
            until[(int)e.Duty] = Time.time + Mathf.Min(300f, 60f * (1 << Mathf.Min(k - 1, 3)));
            return k;
        }

        /// <summary>It found something to do for this duty: its rests start from a minute again.</summary>
        public static void Worked(Humanoid me, Entry e)
        {
            if (Strikes.TryGetValue(Companion.IdOf(me), out int[] strikes)) strikes[(int)e.Duty] = 0;
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

        /// <summary>The food duty is on and stocks your chests: its own chests then keep only a little food for itself (Work.StockCap), the rest is for you.</summary>
        public static bool FoodForYou(Humanoid me) => ToYours(me) && Cached(me).Any(e => e.Duty == Duty.Food && e.On);

        /// <summary>How many of this item its own chests keep for a duty (0 when it is none of its duties, or it has no chest of its own).</summary>
        public static int Cap(Humanoid me, ItemDrop.ItemData i)
        {
            List<Entry> list = Cached(me);
            if (!list.Any(e => e.On) || ToYours(me) || Home.Chests(me).Count == 0) return 0; // (stock in your chests is just yours: Work delivers it there)
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
            if (!HasPlace(me)) return false;
            Inventory inv = me.GetInventory();
            foreach (Entry e in Cached(me))
            {
                if (!e.On) continue;
                int carried = inv.GetAllItems().Where(i => Counts(e.Duty, i) && !Work.Keeps(me, i)).Sum(i => i.m_stack) - (e.Duty == Duty.Food ? 10 : 0);
                if (carried <= 0) continue;
                if (carried >= (e.Duty == Duty.Food ? 8 : 20) || e != active || Have(me, e.Duty) >= e.Target) { LastDelivery[id] = Time.time; return true; }
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
