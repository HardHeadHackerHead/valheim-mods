using System;
using System.Collections.Generic;
using System.Linq;

namespace QualityOfLife
{
    /// <summary>
    /// The high-level groups an item can belong to ("Food", "Weapons", ...). Used so a chest can be assigned a whole kind of item
    /// at once, instead of picking every item one by one.
    /// </summary>
    internal static class Categories
    {
        public const string Weapons = "Weapons";
        public const string BowsAmmo = "Bows & Ammo";
        public const string Shields = "Shields";
        public const string Armor = "Armor & Capes";
        public const string Trinkets = "Trinkets & Utility";
        public const string Tools = "Tools";
        public const string Food = "Food";
        public const string Potions = "Potions & Mead";
        public const string Fish = "Fish";
        public const string Trophies = "Trophies";
        public const string Ores = "Ores";
        public const string Metals = "Metals";
        /// <summary>The single category these two used to be; chests saved with it count as both (see ChestRules).</summary>
        public const string LegacyOresAndMetals = "Ores & Metals";
        public const string Materials = "Materials";
        public const string Misc = "Everything else";

        /// <summary>In the order they're shown in the menu.</summary>
        public static readonly string[] All =
        {
            Weapons, BowsAmmo, Shields, Armor, Trinkets, Tools, Food, Potions, Fish, Trophies, Ores, Metals, Materials, Misc,
        };

        public static readonly Dictionary<string, string> Help = new Dictionary<string, string>
        {
            { Weapons, "Swords, axes, maces, spears, atgeirs, torches" },
            { BowsAmmo, "Bows, crossbows and all arrows and bolts" },
            { Shields, "Round shields, tower shields, bucklers" },
            { Armor, "Helmets, chest and leg armor, capes" },
            { Trinkets, "Belts, rings, trinkets and other worn extras" },
            { Tools, "Pickaxes, hammers, hoes, cultivators, fishing rods" },
            { Food, "Anything you eat for health and stamina" },
            { Potions, "Meads, potions and drinks" },
            { Fish, "Raw fish" },
            { Trophies, "Boss and creature trophies" },
            { Ores, "Raw ore and scrap: copper ore, tin ore, iron scrap, silver ore, black metal scrap, flametal ore" },
            { Metals, "Refined bars: copper, tin, bronze, iron, silver, black metal, flametal" },
            { Materials, "Wood, stone, hides, leather, resin, flint and the like" },
            { Misc, "Anything that fits nowhere else" },
        };

        // The refined metals, by the game's item names (without the "$item_" prefix). Raw ore and scrap are recognised by their endings.
        private static readonly HashSet<string> MetalBars = new HashSet<string>
        {
            "copper", "tin", "bronze", "iron", "silver", "blackmetal", "flametal",
        };

        private static string Plain(string itemName)
        {
            string n = (itemName ?? "").ToLowerInvariant();
            return n.StartsWith("$item_") ? n.Substring(6) : n;
        }

        /// <summary>Raw ore or scrap: names ending in "ore" (but not "core", e.g. Surtling Core) or "scrap".</summary>
        private static bool IsOre(string plain) => (plain.EndsWith("ore") && !plain.EndsWith("core")) || plain.EndsWith("scrap");

        public static string Of(ItemDrop.ItemData.SharedData s)
        {
            switch (s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.Torch:
                    return Weapons;

                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return BowsAmmo;

                case ItemDrop.ItemData.ItemType.Shield:
                    return Shields;

                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return Armor;

                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return Trinkets;

                case ItemDrop.ItemData.ItemType.Tool:
                    return Tools;

                case ItemDrop.ItemData.ItemType.Fish:
                    return Fish;

                case ItemDrop.ItemData.ItemType.Trophy:
                    return Trophies;

                case ItemDrop.ItemData.ItemType.Consumable:
                    // Food gives health/stamina/Eitr for a while; everything else you drink or use is a potion.
                    return !s.m_isDrink && (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f) ? Food : Potions;

                case ItemDrop.ItemData.ItemType.Material:
                    string plain = Plain(s.m_name);
                    if (IsOre(plain)) return Ores;
                    return MetalBars.Contains(plain) ? Metals : Materials;

                default:
                    return Misc;
            }
        }
    }
}
