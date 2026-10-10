using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BountyBoard
{
    internal enum Kind { Hunt, Elite, Sweep, Gather }

    /// <summary>One contract: what to do, how hard it is, what it pays, and (once taken) how far along the group is.</summary>
    internal class Bounty
    {
        public string Id;
        public Kind Kind;
        public string Target;           // creature prefab (hunt, elite), the tier number (sweep) or item prefab (gather)
        public int Count, Tier, Stars, Coins, Progress;
        public List<KeyValuePair<string, int>> Items = new List<KeyValuePair<string, int>>(); // reward materials
        public HashSet<long> Claimed = new HashSet<long>();                                    // players who have collected the reward (finished contracts)
        // characters that earned the reward: took the contract, made a kill for it, handed in for it, or were playing when it was finished
        // (null: finished before this was kept, so anyone who hasn't collected it may, as before)
        public HashSet<long> Earned = new HashSet<long>();

        public Bounty Copy() => Parse(Serialize());

        public string Serialize() => string.Join("~", new[]
        {
            Id, ((int)Kind).ToString(), Target, Count.ToString(), Tier.ToString(), Stars.ToString(), Coins.ToString(), Progress.ToString(),
            string.Join(",", Items.Select(i => i.Key + ":" + i.Value).ToArray()), string.Join(",", Claimed.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()),
            Earned == null ? "*" : string.Join(",", Earned.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()), // (older versions read only the fields before it)
        });

        public static Bounty Parse(string text)
        {
            string[] f = text.Split('~');
            if (f.Length < 10) return null;
            try
            {
                var b = new Bounty
                {
                    Id = f[0], Kind = (Kind)int.Parse(f[1]), Target = f[2], Count = int.Parse(f[3]), Tier = int.Parse(f[4]), Stars = int.Parse(f[5]),
                    Coins = int.Parse(f[6]), Progress = int.Parse(f[7]),
                };
                foreach (string item in f[8].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] kv = item.Split(':');
                    if (kv.Length == 2) b.Items.Add(new KeyValuePair<string, int>(kv[0], int.Parse(kv[1])));
                }
                foreach (string c in f[9].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) b.Claimed.Add(long.Parse(c, CultureInfo.InvariantCulture));
                if (f.Length < 11 || f[10] == "*") b.Earned = null; // saved by an older version
                else foreach (string c in f[10].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) b.Earned.Add(long.Parse(c, CultureInfo.InvariantCulture));
                return b;
            }
            catch (FormatException) { return null; }
        }
    }

    /// <summary>Everything the group shares: today's notices, the contracts being worked on, finished ones waiting to be collected, and the rank.</summary>
    internal class State
    {
        public int Day = -1, Total;
        public List<Bounty> Posted = new List<Bounty>(), Active = new List<Bounty>(), Done = new List<Bounty>();

        public string Serialize()
        {
            var lines = new List<string> { "S~" + Day + "~" + Total };
            lines.AddRange(Posted.Select(b => "P~" + b.Serialize()));
            lines.AddRange(Active.Select(b => "A~" + b.Serialize()));
            lines.AddRange(Done.Select(b => "D~" + b.Serialize()));
            return string.Join("\n", lines.ToArray());
        }

        public static State Parse(string text)
        {
            var s = new State();
            foreach (string line in (text ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("S~"))
                {
                    string[] f = line.Split('~');
                    if (f.Length >= 3) { int.TryParse(f[1], out s.Day); int.TryParse(f[2], out s.Total); }
                }
                else if (line.Length > 2)
                {
                    Bounty b = Bounty.Parse(line.Substring(2));
                    if (b == null) continue;
                    if (line[0] == 'P') s.Posted.Add(b); else if (line[0] == 'A') s.Active.Add(b); else if (line[0] == 'D') s.Done.Add(b);
                }
            }
            return s;
        }
    }

    /// <summary>The rules: what can be posted at each stage of the game, how it is made harder and what it pays.</summary>
    internal static class Rules
    {
        // The boss you have to have beaten for each tier (tier 0 is always open). More bosses beaten means harder contracts and better pay.
        private static readonly string[] TierKeys = { "", "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };
        internal static readonly string[] TierNames = { "Meadows", "Black Forest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands" };
        internal static readonly string[] StarNames = { "", "Standard", "Hard", "Brutal" };

        private class Hunt { public string Prefab; public int Min, Max, Value; public Hunt(string p, int min, int max, int v) { Prefab = p; Min = min; Max = max; Value = v; } }
        private class Gather { public string Item; public int Min, Max, Value; public Gather(string i, int min, int max, int v) { Item = i; Min = min; Max = max; Value = v; } }
        private class Reward { public string Item; public int Value, Min, Max; public Reward(string i, int v, int min, int max) { Item = i; Value = v; Min = min; Max = max; } }

        // Creatures (prefab, how many at least/at most, coins each) and loot (item, how many, coins each), per tier. Names the game does not
        // know are skipped when notices are made, so a wrong or removed name never causes trouble.
        private static readonly Hunt[][] Hunts =
        {
            new[] { new Hunt("Boar", 3, 6, 9), new Hunt("Neck", 4, 8, 6), new Hunt("Greyling", 4, 8, 6), new Hunt("Deer", 3, 5, 7) },
            new[] { new Hunt("Greydwarf", 5, 10, 10), new Hunt("Greydwarf_Shaman", 3, 5, 16), new Hunt("Skeleton", 4, 8, 12), new Hunt("Greydwarf_Elite", 2, 4, 30), new Hunt("Troll", 1, 2, 90) },
            new[] { new Hunt("Draugr", 4, 8, 20), new Hunt("Draugr_Elite", 2, 3, 48), new Hunt("Blob", 5, 10, 14), new Hunt("Leech", 4, 8, 16), new Hunt("Wraith", 2, 4, 36), new Hunt("Surtling", 3, 5, 30) },
            new[] { new Hunt("Wolf", 3, 6, 32), new Hunt("Hatchling", 3, 5, 30), new Hunt("Fenring", 2, 4, 55), new Hunt("Ulv", 3, 5, 36), new Hunt("StoneGolem", 1, 2, 120) },
            new[] { new Hunt("Goblin", 4, 8, 30), new Hunt("GoblinArcher", 3, 6, 32), new Hunt("GoblinShaman", 2, 4, 52), new Hunt("GoblinBrute", 2, 3, 75), new Hunt("Deathsquito", 4, 8, 26), new Hunt("Lox", 1, 2, 125) },
            new[] { new Hunt("Seeker", 4, 7, 42), new Hunt("Tick", 4, 7, 32), new Hunt("SeekerBrute", 1, 2, 145), new Hunt("Gjall", 1, 2, 165), new Hunt("Dverger", 2, 4, 62) },
            new[] { new Hunt("Asksvin", 3, 5, 62), new Hunt("Charred_Melee", 4, 8, 48), new Hunt("Charred_Archer", 3, 6, 52), new Hunt("Volture", 2, 4, 48), new Hunt("Morgen", 1, 2, 210) },
        };

        private static readonly Gather[][] Gathers =
        {
            new[] { new Gather("LeatherScraps", 6, 12, 5), new Gather("DeerHide", 3, 6, 9), new Gather("Feathers", 8, 16, 3), new Gather("RawMeat", 5, 10, 4), new Gather("BoneFragments", 8, 14, 3) },
            new[] { new Gather("Resin", 10, 20, 3), new Gather("GreydwarfEye", 8, 14, 5), new Gather("Coal", 15, 25, 3), new Gather("TrollHide", 2, 4, 28), new Gather("Bronze", 4, 8, 12) },
            new[] { new Gather("Bloodbag", 5, 10, 10), new Gather("Ooze", 5, 10, 9), new Gather("Entrails", 5, 10, 8), new Gather("IronScrap", 8, 14, 8), new Gather("ElderBark", 10, 16, 8), new Gather("WitheredBone", 2, 4, 42) },
            new[] { new Gather("WolfPelt", 3, 6, 24), new Gather("WolfFang", 4, 8, 12), new Gather("Obsidian", 8, 14, 8), new Gather("FreezeGland", 4, 8, 14), new Gather("Silver", 3, 6, 26), new Gather("Crystal", 4, 8, 14) },
            new[] { new Gather("LoxPelt", 3, 5, 42), new Gather("Needle", 6, 10, 14), new Gather("BlackMetalScrap", 8, 14, 14), new Gather("Flax", 10, 20, 5), new Gather("Barley", 10, 20, 5), new Gather("Tar", 5, 10, 12) },
            new[] { new Gather("Carapace", 6, 12, 18), new Gather("Mandible", 2, 4, 55), new Gather("RoyalJelly", 4, 8, 20), new Gather("YggdrasilWood", 8, 14, 12), new Gather("Sap", 6, 10, 12), new Gather("Eitr", 2, 4, 40) },
            new[] { new Gather("CharredBone", 6, 12, 15), new Gather("AskHide", 4, 8, 20), new Gather("MoltenCore", 1, 2, 200), new Gather("Flametal", 3, 6, 40) },
        };

        // What a contract of each tier can pay in materials (item, coin value of one, fewest, most). A tier only pays what that stage of the
        // game has opened up: bronze after the first boss, iron after the second, silver after the third, and so on.
        private static readonly Reward[][] Rewards =
        {
            new[] { new Reward("Resin", 3, 10, 40), new Reward("Flint", 2, 10, 40), new Reward("Honey", 6, 3, 12), new Reward("LeatherScraps", 5, 6, 24) },
            new[] { new Reward("Copper", 14, 2, 16), new Reward("Tin", 14, 2, 16), new Reward("Bronze", 22, 2, 14), new Reward("BronzeNails", 3, 10, 60), new Reward("CoreWood", 6, 10, 40) },
            new[] { new Reward("Iron", 30, 2, 14), new Reward("IronNails", 3, 10, 60), new Reward("Chain", 25, 1, 6), new Reward("Bronze", 22, 2, 10), new Reward("ElderBark", 8, 10, 30) },
            new[] { new Reward("Silver", 45, 2, 12), new Reward("Iron", 30, 3, 14), new Reward("WolfPelt", 25, 1, 6), new Reward("FreezeGland", 14, 2, 10), new Reward("Obsidian", 8, 5, 30) },
            new[] { new Reward("BlackMetal", 60, 2, 12), new Reward("LinenThread", 10, 5, 30), new Reward("Silver", 45, 2, 10), new Reward("Flax", 5, 10, 40) },
            new[] { new Reward("BlackMarble", 35, 3, 20), new Reward("YggdrasilWood", 25, 3, 16), new Reward("Eitr", 70, 1, 6), new Reward("Carapace", 18, 3, 16), new Reward("BlackMetal", 60, 2, 10) },
            new[] { new Reward("Flametal", 100, 1, 8), new Reward("Grausten", 40, 3, 16), new Reward("MoltenCore", 120, 1, 3), new Reward("AskHide", 20, 3, 16) },
        };

        private static bool Reached(int tier)
        {
            if (tier <= 0) return true;
            ZoneSystem zone = ZoneSystem.instance;
            return zone != null && zone.GetGlobalKey(TierKeys[tier]);
        }

        /// <summary>How far the world has got: the highest tier whose boss has been beaten (0 if none).</summary>
        internal static int HighestTier()
        {
            int top = 0;
            for (int t = 1; t < TierKeys.Length; t++) if (Reached(t)) top = t;
            return top;
        }

        private static bool CreatureExists(string prefab) => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;
        private static bool ItemExists(string item) => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(item) != null;

        /// <summary>Is this creature one of the tier's (for "clear out the region" contracts)?</summary>
        internal static bool InTier(int tier, string prefab) => tier >= 0 && tier < Hunts.Length && Hunts[tier].Any(h => h.Prefab == prefab);

        // ---- making a day's notices ----

        internal static List<Bounty> MakeNotices(int day, int count)
        {
            var rng = new System.Random(unchecked(day * 7919 + 104729));
            int top = HighestTier();
            var list = new List<Bounty>();
            var used = new HashSet<string>();
            Kind[] plan = { Kind.Hunt, Kind.Hunt, Kind.Sweep, Kind.Gather, Kind.Elite, Kind.Hunt, Kind.Gather, Kind.Sweep };

            for (int slot = 0; slot < count; slot++)
            {
                Kind kind = plan[slot % plan.Length];
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int tier = rng.NextDouble() < 0.6 ? top : rng.Next(0, top + 1);
                    int stars = kind == Kind.Elite ? 3 : RollStars(rng, top);
                    Bounty b = Make(rng, tier, kind, stars, "d" + day + "s" + slot);
                    if (b == null || !used.Add(b.Kind + b.Target + b.Stars)) continue;
                    list.Add(b);
                    break;
                }
            }
            return list;
        }

        /// <summary>Early on nearly everything is standard; the further the world has got, the more hard and brutal contracts appear.</summary>
        private static int RollStars(System.Random rng, int top)
        {
            double roll = rng.NextDouble();
            double brutal = Math.Min(0.30, 0.02 + 0.045 * top), hard = Math.Min(0.45, 0.14 + 0.07 * top);
            return roll < brutal ? 3 : roll < brutal + hard ? 2 : 1;
        }

        private static readonly float[] CountFactor = { 0f, 1f, 1.5f, 2.2f };
        private static readonly float[] PayFactor = { 0f, 1f, 1.7f, 2.8f };

        private static Bounty Make(System.Random rng, int tier, Kind kind, int stars, string id)
        {
            float baseValue; int count; string target;

            if (kind == Kind.Gather)
            {
                List<Gather> pool = Gathers[tier].Where(g => ItemExists(g.Item)).ToList();
                if (pool.Count == 0) return null;
                Gather g = pool[rng.Next(pool.Count)];
                count = Mathf.Max(1, Mathf.RoundToInt(rng.Next(g.Min, g.Max + 1) * CountFactor[stars]));
                target = g.Item; baseValue = g.Value * count * 1.15f;
            }
            else if (kind == Kind.Sweep)
            {
                List<Hunt> pool = Hunts[tier].Where(h => CreatureExists(h.Prefab)).ToList();
                if (pool.Count < 2) return null;
                count = Mathf.RoundToInt((10 + rng.Next(0, 6)) * CountFactor[stars] * (1f + tier * 0.1f));
                target = tier.ToString(); baseValue = (float)pool.Average(h => h.Value) * count * 0.9f;
            }
            else
            {
                List<Hunt> pool = Hunts[tier].Where(h => CreatureExists(h.Prefab)).ToList();
                if (pool.Count == 0) return null;
                Hunt h = pool[rng.Next(pool.Count)];
                target = h.Prefab;
                if (kind == Kind.Elite) { count = rng.Next(1, 3 + (tier >= 3 ? 1 : 0)); baseValue = h.Value * count * 2.6f; }
                else { count = Mathf.Max(1, Mathf.RoundToInt(rng.Next(h.Min, h.Max + 1) * CountFactor[stars])); baseValue = h.Value * count; }
            }

            baseValue *= PayFactor[stars] * Plugin.RewardPercent.Value / 100f * (0.9f + (float)rng.NextDouble() * 0.25f);
            var b = new Bounty { Id = id, Kind = kind, Target = target, Count = count, Tier = tier, Stars = stars };
            Pay(rng, b, baseValue);
            return b;
        }

        /// <summary>Turn the value of the job into coins and a few materials from what that tier's stage of the game offers.</summary>
        private static void Pay(System.Random rng, Bounty b, float value)
        {
            const float coinShare = 0.4f;
            List<Reward> pool = Rewards[b.Tier].Where(r => ItemExists(r.Item)).OrderBy(_ => rng.Next()).ToList();
            int kinds = Mathf.Min(pool.Count, b.Stars >= 2 ? 2 : 1);
            float spent = 0f;
            for (int i = 0; i < kinds; i++)
            {
                Reward r = pool[i];
                int amount = Mathf.Clamp(Mathf.RoundToInt(value * (1f - coinShare) / kinds / r.Value), r.Min, r.Max * b.Stars);
                b.Items.Add(new KeyValuePair<string, int>(r.Item, amount));
                spent += amount * r.Value;
            }
            // coins are the share left over, so the total always matches the value of the job
            b.Coins = Mathf.Max(5, Mathf.RoundToInt(Mathf.Max(value * coinShare, value - spent) / 5f) * 5);
        }

        // ---- rank (the whole group's) ----

        internal static readonly int[] RankAt = { 0, 5, 15, 30, 60, 100 };
        internal static readonly string[] RankName = { "Newcomers", "Hunters", "Trackers", "Slayers", "Wardens", "Legends" };
        internal static readonly int[] RankBonus = { 0, 5, 10, 15, 20, 25 };

        internal static int Rank(int total)
        {
            int r = 0;
            for (int i = 0; i < RankAt.Length; i++) if (total >= RankAt[i]) r = i;
            return r;
        }

        internal static int WithBonus(int amount, int total) => Mathf.CeilToInt(amount * (1f + RankBonus[Rank(total)] / 100f));

        // ---- names for the screen ----

        internal static string ItemName(string itemPrefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : itemPrefab;
        }

        internal static string TargetName(Bounty b)
        {
            if (b.Kind == Kind.Gather) return Localization.instance.Localize(ItemName(b.Target));
            if (b.Kind == Kind.Sweep) return "creatures of the " + TierNames[Mathf.Clamp(int.Parse(b.Target), 0, TierNames.Length - 1)];
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(b.Target) : null;
            Character c = go != null ? go.GetComponent<Character>() : null;
            return c != null ? Localization.instance.Localize(c.m_name) : b.Target;
        }

        internal static string Describe(Bounty b)
        {
            string verb = b.Kind == Kind.Gather ? "Bring" : b.Kind == Kind.Elite ? "Slay starred" : b.Kind == Kind.Sweep ? "Clear out" : "Hunt";
            return verb + "  " + b.Count + " × " + TargetName(b);
        }
    }
}
