using System.Collections.Generic;
using System.Linq;

namespace LedgerChest
{
    /// <summary>
    /// The kinds the ledger shows things under (its category buttons): finer than the chest categories (Categories.cs, which say where
    /// things are sent), so the building materials, the hides, the meat and the seeds each get their own button.
    /// </summary>
    internal static class Kinds
    {
        public const string All = "All";
        public const string Wood = "Wood";
        public const string Stone = "Stone & minerals";
        public const string Metal = "Ores & metals";
        public const string Hides = "Hides & cloth";
        public const string Drops = "Monster drops";
        public const string Valuables = "Valuables";
        public const string Meat = "Cooking";
        public const string Farming = "Plants & seeds";
        public const string Food = "Food";
        public const string Potions = "Potions & mead";
        public const string Fish = "Fish & bait";
        public const string Weapons = "Weapons";
        public const string Bows = "Bows & ammo";
        public const string Shields = "Shields";
        public const string Armor = "Armor & capes";
        public const string Trinkets = "Trinkets";
        public const string Tools = "Tools";
        public const string Trophies = "Trophies";
        public const string Smithing = "Moulds & upgrades";
        public const string Other = "Everything else";

        /// <summary>In the order the buttons are shown (and "All" lists things).</summary>
        public static readonly string[] Order =
        {
            Wood, Stone, Metal, Hides, Drops, Valuables, Meat, Farming, Food, Potions, Fish,
            Weapons, Bows, Shields, Armor, Trinkets, Tools, Smithing, Trophies, Other,
        };

        private static readonly Dictionary<string, int> Rank = Order.Select((k, i) => new { k, i }).ToDictionary(x => x.k, x => x.i);
        public static int RankOf(string kind) => kind != null && Rank.TryGetValue(kind, out int r) ? r : Order.Length;

        // by the game's item names (without "$item_")
        private static readonly HashSet<string> WoodNames = new HashSet<string> { "wood", "finewood", "roundlog", "elderbark", "yggdrasilwood", "blackwood", "frostwood", "barkabranch" };
        private static readonly HashSet<string> StoneNames = new HashSet<string>
        {
            "stone", "flint", "obsidian", "crystal", "blackmarble", "grausten", "coal", "sulfurstone", "sulfur", "tar", "sap", "softtissue", "stonerock", "ice",
        };
        private static readonly HashSet<string> MetalNames = new HashSet<string>
        {
            "copper", "tin", "bronze", "iron", "silver", "blackmetal", "flametal", "flametalnew", "flametal_old", "bronzenails", "ironnails", "chain", "gold", "ironpit",
        };

        private static string Plain(string itemName)
        {
            string n = (itemName ?? "").ToLowerInvariant();
            return n.StartsWith("$item_") ? n.Substring(6) : n;
        }

        private static bool Has(string plain, params string[] parts) => parts.Any(plain.Contains);

        public static string Of(ItemDrop.ItemData.SharedData s)
        {
            string plain = Plain(s.m_name);
            if (Has(plain, "bait")) return Fish;
            if (Has(plain, "meadbase", "winebase")) return Potions;
            switch (Categories.Of(s))
            {
                case Categories.Weapons: return s.m_itemType == ItemDrop.ItemData.ItemType.Torch ? Tools : Weapons;
                case Categories.BowsAmmo: return Bows;
                case Categories.Shields: return Shields;
                case Categories.Armor: return Armor;
                case Categories.Trinkets: return Trinkets;
                case Categories.Tools: return Tools;
                case Categories.Food: return Food;
                case Categories.Potions: return Potions;
                case Categories.Fish: return Fish;
                case Categories.Trophies: return Trophies;
                case Categories.Ores:
                case Categories.Metals: return Metal;
                case Categories.Materials: return Material(s, plain, Drops);
                default: return Material(s, plain, s.m_itemType == ItemDrop.ItemData.ItemType.Material ? Drops : Other);
            }
        }

        /// <summary>A material (or an odd thing) by its name; "none" if nothing fits.</summary>
        private static string Material(ItemDrop.ItemData.SharedData s, string plain, string none)
        {
            if (Has(plain, "mold_", "_gold_uncooked", "upgrader", "smallparts", "staff_", "frostorbs_")) return Smithing;
            if (Has(plain, "firework", "catapult", "key", "saddle", "hildir", "dragon", "yagluth", "totem")) return Other;
            if (Has(plain, "feast")) return Food;
            if (WoodNames.Contains(plain)) return Wood;
            if (StoneNames.Contains(plain)) return Stone;
            if (MetalNames.Contains(plain) || (plain.EndsWith("ore") && !plain.EndsWith("core")) || plain.EndsWith("ore_old") || plain.EndsWith("scrap")) return Metal;
            if (s.m_value > 0 || Has(plain, "coins", "amber", "ruby", "pearl", "necklace", "goldnugget", "gemstone", "jewel")) return Valuables;
            if (Has(plain, "hide", "pelt", "leather", "fur", "wolfhair", "jute", "linen", "thread")) return Hides;
            if (Has(plain, "meat", "entrails", "flour", "dough", "necktail", "raw", "bloodbag", "uncooked", "spice", "egg", "honey", "asksvintail")) return Meat;
            if (Has(plain, "seed", "cone", "acorn", "sapling", "barley", "flax", "onion", "carrot", "turnip", "dandelion", "thistle", "seaweed")) return Farming;
            return none;
        }
    }
}
