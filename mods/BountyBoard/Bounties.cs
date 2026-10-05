using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BountyBoard
{
    internal enum Kind { Hunt, Elite, Gather }

    /// <summary>One contract. Everything needed to show, track and pay it is in here, so a contract survives the board posting new ones.</summary>
    internal class Bounty
    {
        public string Id;
        public Kind Kind;
        public string Target;   // creature prefab (hunt, elite) or item prefab (gather)
        public int Count, Coins, Tier, Progress;

        public string Serialize() => string.Join("~", new[] { Id, ((int)Kind).ToString(), Target, Count.ToString(), Coins.ToString(), Tier.ToString(), Progress.ToString() });

        public static Bounty Parse(string text)
        {
            string[] f = text.Split('~');
            if (f.Length < 7) return null;
            try
            {
                return new Bounty { Id = f[0], Kind = (Kind)int.Parse(f[1]), Target = f[2], Count = int.Parse(f[3]), Coins = int.Parse(f[4]), Tier = int.Parse(f[5]), Progress = int.Parse(f[6]) };
            }
            catch (FormatException) { return null; }
        }
    }

    /// <summary>What the board posts, what you have taken on, and how kills are counted.</summary>
    internal static class Bounties
    {
        private const string ActiveKey = "DHack_BB_active", DoneKey = "DHack_BB_done", TotalKey = "DHack_BB_total";

        // ---- the world's progress decides what can be posted ----

        // The boss you have to have beaten for each tier (tier 0 is always open).
        private static readonly string[] TierKeys = { "", "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };
        internal static readonly string[] TierNames = { "Meadows", "Black Forest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands" };

        private class Hunt { public string Prefab; public int Min, Max, Value; public Hunt(string p, int min, int max, int v) { Prefab = p; Min = min; Max = max; Value = v; } }
        private class Gather { public string Item; public int Min, Max, Value; public Gather(string i, int min, int max, int v) { Item = i; Min = min; Max = max; Value = v; } }

        // Creatures (prefab, how many at least/at most, coins each) and loot (item, how many, coins each) for each tier. Names the game
        // does not know are skipped when the list is built, so a wrong or removed name never causes trouble.
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

        internal static bool Reached(int tier)
        {
            if (tier <= 0) return true;
            ZoneSystem zone = ZoneSystem.instance;
            return zone != null && zone.GetGlobalKey(TierKeys[tier]);
        }

        /// <summary>The highest tier whose boss has been beaten (0 if none).</summary>
        internal static int HighestTier()
        {
            int top = 0;
            for (int t = 1; t < TierKeys.Length; t++) if (Reached(t)) top = t;
            return top;
        }

        private static bool CreatureExists(string prefab) => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;
        private static bool ItemExists(string item) => ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(item) != null;

        // ---- posting ----

        /// <summary>
        /// The notices on a board right now. They are worked out from the board and the in-game day, so every player sees the same ones
        /// without anything being sent between games.
        /// </summary>
        internal static List<Bounty> Posted(string boardKey, int count)
        {
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() / Math.Max(1, Plugin.RefreshDays.Value) : 0;
            var rng = new System.Random(unchecked(boardKey.GetStableHashCode() * 31 + day * 7919));
            int top = HighestTier();
            var list = new List<Bounty>();
            var used = new HashSet<string>();

            for (int slot = 0; slot < count; slot++)
            {
                // most notices are for where you are now, some for earlier places
                int tier = rng.NextDouble() < 0.65 ? top : rng.Next(0, top + 1);
                Kind kind = slot == count - 1 ? Kind.Elite : slot == count - 2 ? Kind.Gather : Kind.Hunt;
                Bounty b = Make(rng, tier, kind, boardKey + ":" + day + ":" + slot);
                if (b == null && (b = Make(rng, top, Kind.Hunt, boardKey + ":" + day + ":" + slot)) == null) continue;
                if (!used.Add(b.Kind + b.Target)) continue; // no two notices for the same thing
                list.Add(b);
            }
            return list;
        }

        private static Bounty Make(System.Random rng, int tier, Kind kind, string id)
        {
            if (kind == Kind.Gather)
            {
                List<Gather> pool = Gathers[tier].Where(g => ItemExists(g.Item)).ToList();
                if (pool.Count == 0) return null;
                Gather g = pool[rng.Next(pool.Count)];
                int n = rng.Next(g.Min, g.Max + 1);
                return new Bounty { Id = id, Kind = kind, Target = g.Item, Count = n, Tier = tier, Coins = Pay(rng, g.Value * n * 1.15f) };
            }

            List<Hunt> creatures = Hunts[tier].Where(h => CreatureExists(h.Prefab)).ToList();
            if (creatures.Count == 0) return null;
            Hunt h = creatures[rng.Next(creatures.Count)];
            if (kind == Kind.Elite)
            {
                int n = rng.Next(1, 3 + (tier >= 3 ? 1 : 0));
                return new Bounty { Id = id, Kind = kind, Target = h.Prefab, Count = n, Tier = tier, Coins = Pay(rng, h.Value * n * 2.6f) };
            }
            int count = rng.Next(h.Min, h.Max + 1);
            return new Bounty { Id = id, Kind = kind, Target = h.Prefab, Count = count, Tier = tier, Coins = Pay(rng, h.Value * count) };
        }

        private static int Pay(System.Random rng, float basePay)
        {
            float pay = basePay * Plugin.RewardPercent.Value / 100f * (0.9f + (float)rng.NextDouble() * 0.25f);
            return Mathf.Max(5, (int)(Mathf.Round(pay / 5f) * 5f));
        }

        // ---- what you have taken on (kept in your character, so it is yours and survives logging out) ----

        private static Dictionary<string, string> Data => Player.m_localPlayer?.m_customData;

        internal static List<Bounty> Active()
        {
            var list = new List<Bounty>();
            if (Data == null || !Data.TryGetValue(ActiveKey, out string raw) || raw.Length == 0) return list;
            foreach (string s in raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Bounty b = Bounty.Parse(s);
                if (b != null) list.Add(b);
            }
            return list;
        }

        private static void SaveActive(List<Bounty> list)
        {
            if (Data != null) Data[ActiveKey] = string.Join(";", list.Select(b => b.Serialize()).ToArray());
        }

        internal static HashSet<string> Done()
        {
            if (Data == null || !Data.TryGetValue(DoneKey, out string raw)) return new HashSet<string>();
            return new HashSet<string>(raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static int Total() => Data != null && Data.TryGetValue(TotalKey, out string v) && int.TryParse(v, out int n) ? n : 0;

        internal static bool Take(Bounty b)
        {
            List<Bounty> active = Active();
            if (active.Count >= Plugin.MaxActive.Value || active.Any(a => a.Id == b.Id) || Done().Contains(b.Id)) return false;
            active.Add(new Bounty { Id = b.Id, Kind = b.Kind, Target = b.Target, Count = b.Count, Coins = b.Coins, Tier = b.Tier, Progress = 0 });
            SaveActive(active);
            return true;
        }

        internal static void Abandon(string id) => SaveActive(Active().Where(a => a.Id != id).ToList());

        // ---- counting kills ----

        private static readonly HashSet<int> Counted = new HashSet<int>();

        internal static void OnKill(Character victim)
        {
            Player me = Player.m_localPlayer;
            if (me == null || victim == null || victim.IsPlayer() || victim.IsTamed()) return;
            HitData hit = LastHit(victim);
            if (hit == null || hit.GetAttacker() != me) return;
            if (!Counted.Add(victim.GetInstanceID())) return; // death can be reported more than once
            if (Counted.Count > 200) Counted.Clear();

            string prefab = Utils.GetPrefabName(victim.gameObject);
            bool starred = victim.GetLevel() >= 2;
            List<Bounty> active = Active();
            bool changed = false;
            foreach (Bounty b in active)
            {
                if (b.Kind == Kind.Gather || b.Progress >= b.Count || b.Target != prefab) continue;
                if (b.Kind == Kind.Elite && !starred) continue;
                b.Progress++;
                changed = true;
                string name = Localization.instance.Localize(victim.m_name);
                me.Message(MessageHud.MessageType.TopLeft, b.Progress >= b.Count
                    ? "Contract done: " + b.Count + " " + name + ". Hand it in at a Bounty Board."
                    : "Contract: " + name + " " + b.Progress + "/" + b.Count);
            }
            if (changed) SaveActive(active);
        }

        private static readonly AccessTools.FieldRef<Character, HitData> LastHitField = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        private static HitData LastHit(Character c) => LastHitField(c);

        // ---- handing in ----

        internal static bool IsComplete(Bounty b, Player player) =>
            b.Kind == Kind.Gather ? player.GetInventory().CountItems(ItemName(b.Target)) >= b.Count : b.Progress >= b.Count;

        /// <summary>The name the game uses inside an item (what the inventory counts by).</summary>
        internal static string ItemName(string itemPrefab)
        {
            GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
            ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
            return drop != null ? drop.m_itemData.m_shared.m_name : itemPrefab;
        }

        internal static string TargetName(Bounty b)
        {
            if (b.Kind == Kind.Gather) return Localization.instance.Localize(ItemName(b.Target));
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(b.Target) : null;
            Character c = go != null ? go.GetComponent<Character>() : null;
            return c != null ? Localization.instance.Localize(c.m_name) : b.Target;
        }

        /// <summary>Rank: more completed contracts, a bigger share of the pay.</summary>
        internal static readonly int[] RankAt = { 0, 5, 15, 30, 60, 100 };
        internal static readonly string[] RankName = { "Newcomer", "Hunter", "Tracker", "Slayer", "Warden", "Legend" };
        internal static readonly int[] RankBonus = { 0, 5, 10, 15, 20, 25 };

        internal static int Rank(int total)
        {
            int r = 0;
            for (int i = 0; i < RankAt.Length; i++) if (total >= RankAt[i]) r = i;
            return r;
        }

        /// <summary>Pay out a finished contract: take the loot (gather), give the coins, record it. Returns the coins paid, or 0 if it is not finished.</summary>
        internal static int Claim(Bounty posted, Player player)
        {
            List<Bounty> active = Active();
            Bounty b = active.FirstOrDefault(a => a.Id == posted.Id);
            if (b == null || !IsComplete(b, player)) return 0;

            if (b.Kind == Kind.Gather)
            {
                string name = ItemName(b.Target);
                if (player.GetInventory().CountItems(name) < b.Count) return 0;
                player.GetInventory().RemoveItem(name, b.Count);
            }

            int total = Total();
            int coins = Mathf.RoundToInt(b.Coins * (1f + RankBonus[Rank(total)] / 100f));
            Give(player, coins);

            active.Remove(b);
            SaveActive(active);
            HashSet<string> done = Done();
            done.Add(b.Id);
            Data[DoneKey] = string.Join(",", done.Skip(Math.Max(0, done.Count - 80)).ToArray());
            Data[TotalKey] = (total + 1).ToString();
            return coins;
        }

        private static void Give(Player player, int coins)
        {
            GameObject prefab = ObjectDB.instance.GetItemPrefab("Coins");
            if (prefab == null) return;
            int max = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
            while (coins > 0)
            {
                int n = Mathf.Min(coins, max);
                coins -= n;
                if (player.GetInventory().CanAddItem(prefab, n)) player.GetInventory().AddItem(prefab, n);
                else
                {
                    GameObject drop = UnityEngine.Object.Instantiate(prefab, player.transform.position + Vector3.up, Quaternion.identity);
                    ItemDrop itemDrop = drop.GetComponent<ItemDrop>();
                    itemDrop.SetStack(n);
                }
            }
        }
    }

    // Count a kill for the contracts when something dies by your hand.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath
    {
        private static void Prefix(Character __instance) => Bounties.OnKill(__instance);
    }
}
