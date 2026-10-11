using System;
using System.Linq;
using static DHack.Shared.Translations;

namespace BetterCraftingStations
{
    /// <summary>
    /// What kind of thing a recipe makes (weapon, armor, tool...) and how far into the game it is (wood and flint, bronze, iron...), worked out
    /// from the item itself and from what it costs, so it follows the game's own recipes (and other mods' items) without a list of them.
    /// </summary>
    internal static class Classify
    {
        public const string Weapons = "Weapons", Armor = "Armor", Tools = "Tools", Ammo = "Ammo", Food = "Food", Potions = "Potions", Materials = "Materials", Other = "Other";

        /// <summary>The order the type chips come in.</summary>
        public static readonly string[] TypeOrder = { Weapons, Armor, Tools, Ammo, Food, Potions, Materials, Other };

        /// <summary>A type's name on its chip, in the player's language (the type itself stays English: it is what the chips compare).</summary>
        public static string Label(string type) => T(TypeKeys[Array.IndexOf(TypeOrder, type)]);

        private static readonly string[] TypeKeys =
        {
            "bettercraftingstations_type_weapons", "bettercraftingstations_type_armor", "bettercraftingstations_type_tools", "bettercraftingstations_type_ammo",
            "bettercraftingstations_type_food", "bettercraftingstations_type_potions", "bettercraftingstations_type_materials", "bettercraftingstations_type_other",
        };

        public static string TypeOf(Recipe r)
        {
            ItemDrop.ItemData.SharedData s = r?.m_item?.m_itemData?.m_shared;
            if (s == null) return Other;
            string kind = s.m_itemType.ToString();
            string skill = s.m_skillType.ToString();
            switch (kind)
            {
                case "Ammo":
                case "AmmoNonEquipable": return Ammo;
                case "Shield":
                case "Helmet":
                case "Chest":
                case "Legs":
                case "Shoulder":
                case "Utility": return Armor;
                case "Tool":
                case "Torch": return Tools;
                case "Material": return Materials;
                case "Consumable":
                    return s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f ? Food : Potions;
                case "OneHandedWeapon":
                    // axes and pickaxes are what you chop and mine with; a one-handed axe is a tool before it is a weapon
                    return skill == "Pickaxes" || skill == "Axes" ? Tools : Weapons;
                case "Bow":
                case "TwoHandedWeapon":
                case "TwoHandedWeaponLeft":
                case "Attach_Atgeir": return Weapons;
                default: return s.m_damages.GetTotalDamage() > 0f ? Weapons : Other;
            }
        }

        // ---- how far into the game ----------------------------------------------------------------------------------

        /// <summary>Each tier's name on its chip (translation keys).</summary>
        public static readonly string[] TierNames =
        {
            "bettercraftingstations_tier_wood", "bettercraftingstations_tier_bronze", "bettercraftingstations_tier_iron", "bettercraftingstations_tier_silver",
            "bettercraftingstations_tier_blackmetal", "bettercraftingstations_tier_mistlands", "bettercraftingstations_tier_ashlands",
        };

        // Item names (without "$item_") that mark a tier; a recipe is of the highest tier among the things it costs.
        private static readonly string[][] Marks =
        {
            new string[0],
            new[] { "copper", "tin", "tinore", "bronze" },
            new[] { "iron", "ironscrap", "ironnails", "ironore" },
            new[] { "silver", "wolfpelt", "wolffang", "wolfhairbundle" },
            new[] { "blackmetal", "loxpelt" },
            new[] { "carapace", "blackwood", "yggdrasilwood", "softtissue", "sap", "mechanicalspring", "eitr", "blackcore", "royaljelly", "dvergrnails", "mistlandsmaterial" },
            new[] { "flametal", "ashwood", "charredbone", "morgenheart", "asksvinhide", "fireadept" },
        };

        private static string Plain(string itemName)
        {
            string n = (itemName ?? "").ToLowerInvariant();
            if (n.StartsWith("$item_")) n = n.Substring(6);
            return n.Replace("_", "").Replace(" ", "");
        }

        public static int TierOf(Recipe r)
        {
            if (r?.m_resources == null) return 0;
            int tier = 0;
            foreach (Piece.Requirement req in r.m_resources)
            {
                string name = Plain(req?.m_resItem?.m_itemData?.m_shared?.m_name);
                for (int t = Marks.Length - 1; t > tier; t--)
                    if (Marks[t].Any(m => name == m.Replace("_", "") || name.StartsWith(m.Replace("_", "")) && m.Length >= 5)) { tier = t; break; }
            }
            return tier;
        }
    }
}
