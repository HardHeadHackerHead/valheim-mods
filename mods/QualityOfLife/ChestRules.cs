using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace QualityOfLife
{
    /// <summary>
    /// What a chest has been assigned to receive. Stored in the chest's own save data, so it's shared with the other players,
    /// survives restarts, and goes away with the chest. Two levels: whole categories ("Food") and individual items.
    /// </summary>
    internal class ChestRules
    {
        public readonly HashSet<string> Categories = new HashSet<string>();
        public readonly HashSet<string> Items = new HashSet<string>();

        public bool IsEmpty => Categories.Count == 0 && Items.Count == 0;

        private static readonly int Key = "DHack_StackRules".GetStableHashCode();
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");

        private static ZDO ZdoOf(Container chest) => (NView.GetValue(chest) as ZNetView)?.GetZDO();

        /// <summary>Format: "C=Weapons,Food|I=$item_wood,$item_stone".</summary>
        public static ChestRules Read(Container chest)
        {
            var rules = new ChestRules();
            string text = ZdoOf(chest)?.GetString(Key, "") ?? "";

            foreach (string part in text.Split('|'))
            {
                if (part.Length < 2) continue;
                HashSet<string> target = part.StartsWith("C=") ? rules.Categories : part.StartsWith("I=") ? rules.Items : null;
                if (target == null) continue;
                foreach (string value in part.Substring(2).Split(','))
                {
                    if (value.Length == 0) continue;
                    // Chests assigned before "Ores" and "Metals" were split keep working: the old tag means both.
                    if (target == rules.Categories && value == QualityOfLife.Categories.LegacyOresAndMetals)
                    {
                        target.Add(QualityOfLife.Categories.Ores);
                        target.Add(QualityOfLife.Categories.Metals);
                    }
                    else target.Add(value);
                }
            }
            return rules;
        }

        /// <summary>Save the rules on the chest. False (nothing saved) if the chest is gone or someone else has it open.</summary>
        public static bool Write(Container chest, ChestRules rules)
        {
            var view = NView.GetValue(chest) as ZNetView;
            ZDO zdo = view?.GetZDO();
            if (zdo == null) return false;

            // Only the owner of a chest can save changes to it, so take ownership first (matters in multiplayer). Not while another
            // player has it open: taking it from them would lose what they move in it from then on.
            if (ContainerRegistry.InUse(chest)) return false;
            ContainerRegistry.TakeOwnership(chest);

            string text = rules.IsEmpty ? ""
                : "C=" + string.Join(",", rules.Categories.OrderBy(x => x).ToArray()) + "|I=" + string.Join(",", rules.Items.OrderBy(x => x).ToArray());
            zdo.Set(Key, text);
            return true;
        }

        /// <summary>Does this chest want this item (by its exact name, or by its category)?</summary>
        public bool WantsItem(string itemName) => Items.Contains(itemName);
        public bool WantsCategory(string category) => Categories.Contains(category);
    }
}
