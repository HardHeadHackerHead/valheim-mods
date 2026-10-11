using UnityEngine;

namespace CigarSmoking
{
    /// <summary>One kind of cigar: how it looks, what it does while it burns, and what it takes to roll one.</summary>
    internal sealed class CigarType
    {
        public string Id, Prefab, Display, Status, Flavour, Start, Stop;   // the words: "$keys" in translations/*.json
        public Color Body, Band;
        // what it does (1 = no change for the regeneration multipliers, 0 = none for the others; scaled by the EffectStrength setting)
        public float StaminaRegen = 1f, HealthRegen = 1f, AttackUse, RunDrain, Noise, Stealth;
        // rolled at the Rolling Table: the strain and stage of the leaf, the extras, how many cigars, and the table level it needs (2 = with a Humidor)
        public string Strain, Stage;
        public string[] Extra = new string[0];
        public int[] ExtraAmount = new int[0];
        public int Leaves = 3, Makes = 3, Level = 1;
    }

    /// <summary>A strain of tobacco: where it grows wild, where it can be planted, and the names of everything it gives.</summary>
    internal sealed class Strain
    {
        public string Id;
        public Heightmap.Biome Wild, Plant;          // where it grows in the wild; where a planted seed will grow
        public float Height;                          // how tall the plant stands (for the hit box)
        public string Seeds, Fresh, Dried, Aged;      // item prefab names
        public string Sapling, Pickable, WildPickable; // plant prefab names

        public string Item(string stage) => stage == "fresh" ? Fresh : stage == "dried" ? Dried : stage == "aged" ? Aged : Seeds;

        /// <summary>The "$key" of one of its words in translations/*.json: "seeds" is $cigarsmoking_meadow_seeds for the meadow strain.</summary>
        public string Key(string what) => "$" + "cigarsmoking_" + Id + "_" + what;   // ("$" apart: tools/check_translations.py would take the start for a key)

        internal Strain(string id, Heightmap.Biome wild, Heightmap.Biome plant, float height)
        {
            Id = id; Wild = wild; Plant = plant; Height = height;
            Seeds = "dh_seeds_" + id; Fresh = "dh_leaf_" + id; Dried = "dh_dried_" + id; Aged = "dh_aged_" + id;
            Sapling = "sapling_dh_" + id; Pickable = "Pickable_dh_" + id; WildPickable = "Pickable_dh_" + id + "_wild";
        }
    }

    internal static class Strains
    {
        // each one can be planted in its own biome and the ones before it: the hardier strains can be farmed at home, but finding one takes exploring
        internal static readonly Strain[] All =
        {
            new Strain("meadow", Heightmap.Biome.Meadows, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.1f),
            new Strain("forest", Heightmap.Biome.BlackForest, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.35f),
            new Strain("plains", Heightmap.Biome.Plains, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.2f),
        };

        internal static Strain ById(string id) { foreach (Strain s in All) if (s.Id == id) return s; return All[0]; }
    }

    internal static class Types
    {
        // the first keeps the prefab name (dh_cigar) the first version of the mod used
        internal static readonly CigarType[] All =
        {
            new CigarType
            {
                Id = "connecticut", Prefab = "dh_cigar", Display = "$cigarsmoking_connecticut", Status = "$cigarsmoking_connecticut_status",
                Flavour = "$cigarsmoking_connecticut_desc", Start = "$cigarsmoking_connecticut_start", Stop = "$cigarsmoking_connecticut_stop",
                Body = new Color(0.72f, 0.55f, 0.32f), Band = new Color(0.85f, 0.15f, 0.12f),
                StaminaRegen = 1.15f, Strain = "meadow", Stage = "dried", Extra = new[] { "Resin" }, ExtraAmount = new[] { 1 }, Level = 1,
            },
            new CigarType
            {
                Id = "maduro", Prefab = "dh_cigar_maduro", Display = "$cigarsmoking_maduro", Status = "$cigarsmoking_maduro_status",
                Flavour = "$cigarsmoking_maduro_desc", Start = "$cigarsmoking_maduro_start", Stop = "$cigarsmoking_maduro_stop",
                Body = new Color(0.15f, 0.09f, 0.05f), Band = new Color(0.10f, 0.10f, 0.10f),
                HealthRegen = 1.40f, StaminaRegen = 1.05f, Strain = "forest", Stage = "aged", Extra = new[] { "Honey", "Resin" }, ExtraAmount = new[] { 1, 1 }, Level = 2,
            },
            new CigarType
            {
                Id = "habano", Prefab = "dh_cigar_habano", Display = "$cigarsmoking_habano", Status = "$cigarsmoking_habano_status",
                Flavour = "$cigarsmoking_habano_desc", Start = "$cigarsmoking_habano_start", Stop = "$cigarsmoking_habano_stop",
                Body = new Color(0.55f, 0.27f, 0.12f), Band = new Color(0.10f, 0.55f, 0.22f),
                AttackUse = -0.25f, RunDrain = -0.15f, StaminaRegen = 1.05f, Strain = "plains", Stage = "aged",
                Extra = new[] { "Cloudberry", "Resin" }, ExtraAmount = new[] { 2, 1 }, Level = 2,
            },
            new CigarType
            {
                Id = "corojo", Prefab = "dh_cigar_corojo", Display = "$cigarsmoking_corojo", Status = "$cigarsmoking_corojo_status",
                Flavour = "$cigarsmoking_corojo_desc", Start = "$cigarsmoking_corojo_start", Stop = "$cigarsmoking_corojo_stop",
                Body = new Color(0.42f, 0.28f, 0.17f), Band = new Color(0.15f, 0.30f, 0.72f),
                Noise = -0.35f, Stealth = -0.35f, Strain = "forest", Stage = "aged", Extra = new[] { "Thistle", "Resin" }, ExtraAmount = new[] { 2, 1 }, Level = 2,
            },
        };

        internal static CigarType ByIndex(int i) => i >= 0 && i < All.Length ? All[i] : All[0];

        internal static int IndexOf(CigarType t) => System.Array.IndexOf(All, t);
    }
}
