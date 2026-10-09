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

        private static readonly string[] Champions = { "Boar", "Troll", "Draugr_Elite", "Fenring", "GoblinBrute", "SeekerBrute", "Charred_Melee" };
        private static readonly string[] ChampionFallbacks = { "Greydwarf_Elite", "Greydwarf_Elite", "Draugr", "Wolf", "Goblin", "Seeker", "Charred_Archer" };

        private static readonly Dictionary<string, string> Trophies = new Dictionary<string, string>
        {
            ["Troll"] = "TrophyFrostTroll", ["Draugr_Elite"] = "TrophyDraugrElite", ["GoblinBrute"] = "TrophyGoblinBrute", ["SeekerBrute"] = "TrophySeekerBrute",
            ["Charred_Melee"] = "TrophyCharredMelee", ["Greydwarf_Elite"] = "TrophyGreydwarfBrute", ["Goblin"] = "TrophyGoblin",
        };

        private static readonly string[] FirstNames = { "Grimtooth", "Ironjaw", "Bloodaxe", "Skullsplitter", "Stormhide", "Nightmaw", "Ragnok", "Hrafn", "Thundergut", "Old Scar", "Wyrmbane", "Fenwick", "Grundr", "Ulfgar", "Morgrim", "Hakon" };
        private static readonly string[] Epithets = { "the Mauler", "the Unbroken", "Crowd-Eater", "the Brute", "of the Pit", "Kneebreaker", "the Hungry", "the Relentless", "Bonecruncher", "the Undefeated" };

        // Materials a tier pays beside coins.
        private static readonly string[] Materials = { "Flint", "Bronze", "Iron", "Silver", "BlackMetal", "Eitr", "FlametalNew" };

        // What the crowd throws you when it loves you, by tier (the first that exists of each tier's list).
        private static readonly string[][] Gifts =
        {
            new[] { "CookedMeat", "NeckTailGrilled", "Raspberry" },
            new[] { "CookedMeat", "QueensJam", "MeadHealthMinor" },
            new[] { "Sausages", "TurnipStew", "MeadHealthMinor" },
            new[] { "Sausages", "OnionSoup", "MeadHealthMedium" },
            new[] { "CookedLoxMeat", "FishWraps", "MeadHealthMedium" },
            new[] { "MisthareSupreme", "MeatPlatter", "MeadHealthMajor" },
            new[] { "MashedMeat", "PiquantPie", "MeadHealthMajor" },
        };

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

        /// <summary>The champion's prefab for a tier (or a stand-in when the game has not got it).</summary>
        internal static string Champion(int tier)
        {
            tier = Mathf.Clamp(tier, 0, Champions.Length - 1);
            if (Has(Champions[tier])) return Champions[tier];
            if (Has(ChampionFallbacks[tier])) return ChampionFallbacks[tier];
            List<string> pool = Pool(tier);
            return pool.Count > 0 ? pool[pool.Count - 1] : null;
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

        internal static string Gift(int tier)
        {
            string[] list = Gifts[Mathf.Clamp(tier, 0, Gifts.Length - 1)].Where(Has).ToArray();
            return list.Length > 0 ? list[Random.Range(0, list.Length)] : null;
        }
    }
}
