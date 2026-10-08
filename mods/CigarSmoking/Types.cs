using UnityEngine;

namespace CigarSmoking
{
    /// <summary>One kind of cigar: how it looks, what it does while it burns, and what it takes to roll one.</summary>
    internal sealed class CigarType
    {
        public string Id, Prefab, Display, Status, Flavour, Start, Stop;
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
        public string Id, Display;
        public Heightmap.Biome Wild, Plant;          // where it grows in the wild; where a planted seed will grow
        public float Height;                          // how tall the plant stands (for the hit box)
        public string Seeds, Fresh, Dried, Aged;      // item prefab names
        public string Sapling, Pickable, WildPickable; // plant prefab names

        public string Item(string stage) => stage == "fresh" ? Fresh : stage == "dried" ? Dried : stage == "aged" ? Aged : Seeds;

        internal Strain(string id, string display, Heightmap.Biome wild, Heightmap.Biome plant, float height)
        {
            Id = id; Display = display; Wild = wild; Plant = plant; Height = height;
            Seeds = "dh_seeds_" + id; Fresh = "dh_leaf_" + id; Dried = "dh_dried_" + id; Aged = "dh_aged_" + id;
            Sapling = "sapling_dh_" + id; Pickable = "Pickable_dh_" + id; WildPickable = "Pickable_dh_" + id + "_wild";
        }
    }

    internal static class Strains
    {
        // each one can be planted in its own biome and the ones before it: the hardier strains can be farmed at home, but finding one takes exploring
        internal static readonly Strain[] All =
        {
            new Strain("meadow", "Meadow", Heightmap.Biome.Meadows, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.1f),
            new Strain("forest", "Forest", Heightmap.Biome.BlackForest, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.35f),
            new Strain("plains", "Plains", Heightmap.Biome.Plains, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest | Heightmap.Biome.Plains, 1.2f),
        };

        internal static Strain ById(string id) { foreach (Strain s in All) if (s.Id == id) return s; return All[0]; }
    }

    internal static class Types
    {
        // the first keeps the item name the first version of the mod used
        internal static readonly CigarType[] All =
        {
            new CigarType
            {
                Id = "connecticut", Prefab = "dh_cigar", Display = "Connecticut Cigar", Status = "Relaxed",
                Flavour = "Light and golden, rolled from shade-grown meadow leaf. Mild and mellow: a cigar for taking it easy.",
                Start = "You light a Connecticut and take it easy.", Stop = "The Connecticut has burned down.",
                Body = new Color(0.72f, 0.55f, 0.32f), Band = new Color(0.85f, 0.15f, 0.12f),
                StaminaRegen = 1.15f, Strain = "meadow", Stage = "dried", Extra = new[] { "Resin" }, ExtraAmount = new[] { 1 }, Level = 1,
            },
            new CigarType
            {
                Id = "maduro", Prefab = "dh_cigar_maduro", Display = "Maduro Cigar", Status = "Smooth",
                Flavour = "The darkest and slowest to make. Long ageing and a touch of honey leave it sweet and smooth, and it mends the body a little faster.",
                Start = "You light a Maduro. Smooth and sweet.", Stop = "The Maduro has burned down.",
                Body = new Color(0.15f, 0.09f, 0.05f), Band = new Color(0.10f, 0.10f, 0.10f),
                HealthRegen = 1.40f, StaminaRegen = 1.05f, Strain = "forest", Stage = "aged", Extra = new[] { "Honey", "Resin" }, ExtraAmount = new[] { 1, 1 }, Level = 2,
            },
            new CigarType
            {
                Id = "habano", Prefab = "dh_cigar_habano", Display = "Habano Cigar", Status = "Fired Up",
                Flavour = "Oily and spicy, rolled from plains leaf with a cloudberry bite. It gets the blood going: swings and sprints cost less breath.",
                Start = "You light a Habano. Your blood runs hot.", Stop = "The Habano has burned down.",
                Body = new Color(0.55f, 0.27f, 0.12f), Band = new Color(0.10f, 0.55f, 0.22f),
                AttackUse = -0.25f, RunDrain = -0.15f, StaminaRegen = 1.05f, Strain = "plains", Stage = "aged",
                Extra = new[] { "Cloudberry", "Resin" }, ExtraAmount = new[] { 2, 1 }, Level = 2,
            },
            new CigarType
            {
                Id = "corojo", Prefab = "dh_cigar_corojo", Display = "Corojo Cigar", Status = "Shadow",
                Flavour = "Reddish and peppery, rolled from black forest leaf with thistle in the filler. The smoke hangs about you and you move quietly in it.",
                Start = "You light a Corojo and slip into the shadows.", Stop = "The Corojo has burned down.",
                Body = new Color(0.42f, 0.28f, 0.17f), Band = new Color(0.15f, 0.30f, 0.72f),
                Noise = -0.35f, Stealth = -0.35f, Strain = "forest", Stage = "aged", Extra = new[] { "Thistle", "Resin" }, ExtraAmount = new[] { 2, 1 }, Level = 2,
            },
        };

        internal static CigarType ByIndex(int i) => i >= 0 && i < All.Length ? All[i] : All[0];

        internal static int IndexOf(CigarType t) => System.Array.IndexOf(All, t);
    }
}
