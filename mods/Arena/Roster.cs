using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>Who you fight, and what they pay, by how far into the game you are (the bosses you have beaten, as the Bounty Board counts them).</summary>
    internal static class Roster
    {
        internal static readonly string[] TierNames = { "Meadows", "Black Forest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands" };
        private static readonly string[] BossKeys = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen", "defeated_fader" };
        private static readonly string[] Origins = { "From the green Meadows", "Out of the dark of the Black Forest", "Risen from the stinking Swamp", "Down from the frozen Mountains",
                                                     "Across the burning Plains", "Out of the Mist", "Forged in the fires of the Ashlands" };

        // Prefab names by tier, easiest first: those the game does not have are left out.
        private static readonly string[][] Fodder =
        {
            new[] { "Boar", "Neck", "Greyling", "Greydwarf" },
            new[] { "Greydwarf", "Skeleton", "Greydwarf_Shaman", "Greydwarf_Elite" },
            new[] { "Draugr", "Skeleton", "Blob", "Draugr_Ranged", "Draugr_Elite" },
            new[] { "Wolf", "Hatchling", "Ulv", "Fenring" },
            new[] { "Goblin", "Deathsquito", "GoblinArcher", "Lox", "GoblinBrute" },
            new[] { "Seeker", "Tick", "Gjall", "SeekerBrute" },
            new[] { "Charred_Melee", "Asksvin", "Charred_Archer", "Charred_Mage" },
        };

        // Each land's champions, one picked at random for each champion round (not the same one twice running, where there is a choice).
        // Only those that walk: a flyer would be over the wall and gone.
        private static readonly string[][] Champions =
        {
            new[] { "Boar", "Greydwarf", "Neck" },
            new[] { "Troll", "Bjorn", "Greydwarf_Elite", "Skeleton_Poison" },
            new[] { "Draugr_Elite", "Abomination", "Wraith", "BlobElite", "Unbjorn" },
            new[] { "Fenring", "StoneGolem", "Fenring_Cultist", "Ulv" },
            new[] { "GoblinBrute", "Lox", "GoblinShaman" },
            new[] { "SeekerBrute" },   // (a seeker: too little health for the hit it has, to be a champion)
            new[] { "Charred_Melee", "Morgen", "Asksvin", "Charred_Mage" },
        };
        private static string _lastChampion;

        private static readonly Dictionary<string, string> Trophies = new Dictionary<string, string>
        {
            ["Troll"] = "TrophyForestTroll", ["Draugr_Elite"] = "TrophyDraugrElite", ["GoblinBrute"] = "TrophyGoblinBrute", ["SeekerBrute"] = "TrophySeekerBrute",
            ["Charred_Melee"] = "TrophyCharredMelee", ["Greydwarf_Elite"] = "TrophyGreydwarfBrute", ["Goblin"] = "TrophyGoblin",
            ["Bjorn"] = "TrophyBjorn", ["Unbjorn"] = "TrophyBjornUndead", ["Skeleton_Poison"] = "TrophySkeletonPoison", ["BlobElite"] = "TrophyBlob",
            ["StoneGolem"] = "TrophySGolem", ["Fenring_Cultist"] = "TrophyCultist", ["Charred_Mage"] = "TrophyCharredMage",
        };

        private static readonly string[] FirstNames = { "Grimtooth", "Ironjaw", "Bloodaxe", "Skullsplitter", "Stormhide", "Nightmaw", "Ragnok", "Hrafn", "Thundergut", "Old Scar", "Wyrmbane", "Fenwick", "Grundr", "Ulfgar", "Morgrim", "Hakon" };
        private static readonly string[] Epithets = { "the Mauler", "the Unbroken", "Crowd-Eater", "the Brute", "of the Pit", "Kneebreaker", "the Hungry", "the Relentless", "Bonecruncher", "the Undefeated" };

        // Materials a tier pays beside coins.
        private static readonly string[] Materials = { "Flint", "Bronze", "Iron", "Silver", "BlackMetal", "Eitr", "FlametalNew" };

        /// <summary>How far on you are: the number of bosses beaten, never more than the last tier.</summary>
        internal static int Stage()
        {
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null) return 0;
            if (Plugin.AllTiers.Value) return Fodder.Length - 1;   // (for testing, or a world where you would rather choose)
            int n = 0;
            foreach (string key in BossKeys) if (zone.GetGlobalKey(key)) n++;
            return Mathf.Min(n, Fodder.Length - 1);
        }

        internal static bool Has(string prefab) => !string.IsNullOrEmpty(prefab) && ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;

        internal static List<string> Pool(int tier) => Fodder[Mathf.Clamp(tier, 0, Fodder.Length - 1)].Where(Has).Distinct().ToList();

        /// <summary>The names a player would know the tier's fighters by.</summary>
        internal static string PoolText(int tier)
        {
            var names = new List<string>();
            foreach (string p in Pool(tier))
            {
                Character c = ZNetScene.instance.GetPrefab(p)?.GetComponent<Character>();
                string n = c != null && Localization.instance != null ? Localization.instance.Localize(c.m_name) : p;
                if (!names.Contains(n)) names.Add(n);
            }
            return names.Count == 0 ? "nobody" : string.Join(", ", names);
        }

        /// <summary>A land's champions (those the game has).</summary>
        internal static List<string> ChampionPool(int tier) => Champions[Mathf.Clamp(tier, 0, Champions.Length - 1)].Where(Has).ToList();

        /// <summary>A champion for a land, picked at random from its champions (or its hardest fighter when the game has none of them).</summary>
        internal static string Champion(int tier)
        {
            List<string> pool = ChampionPool(tier);
            if (pool.Count > 1) pool.Remove(_lastChampion);
            if (pool.Count > 0) return _lastChampion = pool[Random.Range(0, pool.Count)];
            List<string> fodder = Pool(tier);
            return fodder.Count > 0 ? fodder[fodder.Count - 1] : null;
        }

        // How tough a land's champion may be, after its stars: its health (the game's stars multiply it: two at a star, three at two) and its
        // hardest hit (half again at a star, double at two), against what that land's gear can take.
        private static readonly float[] ChampionHealth = { 120f, 600f, 900f, 1300f, 1900f, 2700f, 3600f };
        private static readonly float[] ChampionHit = { 30f, 140f, 200f, 260f, 340f, 450f, 550f };
        private static readonly Dictionary<string, float> Hits = new Dictionary<string, float>();

        /// <summary>The hardest single attack a creature has (its attack items, set or random).</summary>
        internal static float HardestHit(string prefab)
        {
            if (Hits.TryGetValue(prefab, out float known)) return known;
            Humanoid h = ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<Humanoid>();
            float best = 0f;
            void Look(GameObject w) { ItemDrop.ItemData d = w != null ? w.GetComponent<ItemDrop>()?.m_itemData : null; if (d != null) best = Mathf.Max(best, d.m_shared.m_damages.GetTotalDamage()); }
            if (h != null)
            {
                foreach (GameObject w in h.m_defaultItems ?? new GameObject[0]) Look(w);
                foreach (GameObject w in h.m_randomWeapon ?? new GameObject[0]) Look(w);
                foreach (Humanoid.ItemSet set in h.m_randomSets ?? new Humanoid.ItemSet[0]) foreach (GameObject w in set.m_items ?? new GameObject[0]) Look(w);
            }
            Hits[prefab] = best;
            return best;
        }

        /// <summary>
        /// A champion's level (1; 2 and 3 for one and two stars): as many stars as its land's gear can take, by its health and by its hardest
        /// hit, whichever allows fewer. A boar or a greydwarf gets both stars; a bear, a troll or an abomination none (they are a land's
        /// champion as they are). Tougher in a Champion Bout (you bring your best) and as the Endless Horde goes on; a star more with Hard.
        /// </summary>
        internal static int ChampionLevel(int tier, string prefab, Contest.KindOf kind, int wave, bool hard = false)
        {
            int t = Mathf.Clamp(tier, 0, ChampionHealth.Length - 1);
            float health = Mathf.Max(1f, ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<Character>()?.m_health ?? 100f);
            float hit = HardestHit(prefab);
            float more = kind == Contest.KindOf.Champion ? 1.6f : kind == Contest.KindOf.Endless ? 1f + wave / 15f : 1f;
            int byHealth = Mathf.RoundToInt(ChampionHealth[t] * more / health);
            int byHit = hit <= 0f ? 3 : Mathf.FloorToInt(1f + 2f * (ChampionHit[t] * more / hit - 1f));
            int level = Mathf.Clamp(Mathf.Min(byHealth, byHit), 1, 3);
            return Mathf.Min(3, level + (hard ? 1 : 0));
        }

        /// <summary>The names a player would know a land's champions by.</summary>
        internal static string ChampionText(int tier)
        {
            var names = ChampionPool(tier).Select(p =>
            {
                Character c = ZNetScene.instance.GetPrefab(p)?.GetComponent<Character>();
                return c != null && Localization.instance != null ? Localization.instance.Localize(c.m_name) : p;
            }).Distinct().ToList();
            return names.Count == 0 ? "none" : names.Count == 1 ? "a great " + names[0] : "a great " + string.Join(", ", names.Take(names.Count - 1)) + " or " + names.Last();
        }

        internal static string ChampionName() => FirstNames[Random.Range(0, FirstNames.Length)] + " " + Epithets[Random.Range(0, Epithets.Length)];
        internal static string Origin(int tier) => Origins[Mathf.Clamp(tier, 0, Origins.Length - 1)];

        /// <summary>The prefab for one fighter of a round: later rounds lean on the harder ones in the tier's list.</summary>
        internal static string Pick(int tier, float lean)
        {
            List<string> pool = Pool(tier);
            if (pool.Count == 0) return null;
            int top = Mathf.Clamp(Mathf.CeilToInt(Mathf.Lerp(1.5f, pool.Count, Mathf.Clamp01(lean))), 1, pool.Count);
            return pool[Random.Range(0, top)];
        }

        internal static string Material(int tier) { string m = Materials[Mathf.Clamp(tier, 0, Materials.Length - 1)]; return Has(m) ? m : null; }

        internal static string Trophy(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            if (Trophies.TryGetValue(prefab, out string known) && Has(known)) return known;
            foreach (string name in new[] { "Trophy" + prefab, "Trophy" + prefab.Replace("_", "") })
                if (Has(name)) return name;
            return null;
        }
    }
}
