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

        /// <summary>
        /// A cart's or a ship's container. Never assigned: the rules would live in its save data, which the game sends whole to every player
        /// near it each time it moves (see "Keep a moving object's ZDO small" in docs/modding-pitfalls.md).
        /// </summary>
        public static bool OnTheMove(Container chest) => chest != null && (chest.GetComponentInParent<Vagon>() != null || chest.GetComponentInParent<Ship>() != null);

        /// <summary>Format: "C=Weapons,Food|I=$item_wood,$item_stone".</summary>
        public static ChestRules Read(Container chest)
        {
            var rules = new ChestRules();
            if (OnTheMove(chest)) return rules; // (rules 1.13.0 and older let you put on a cart or ship count for nothing)
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
            // player has it open: taking it from them would lose what they move in it from then on. The chest you have open yourself is
            // fine (the assign menu beside it): you own it while it is open, and the game doesn't reload a chest's items while it is in use.
            bool mine = chest == Plugin.OpenChest() && view.IsOwner();
            if (ContainerRegistry.InUse(chest) && !mine) return false;
            ContainerRegistry.TakeOwnership(chest);

            string text = rules.IsEmpty ? ""
                : "C=" + string.Join(",", rules.Categories.OrderBy(x => x).ToArray()) + "|I=" + string.Join(",", rules.Items.OrderBy(x => x).ToArray());
            zdo.Set(Key, text);
            return true;
        }

        /// <summary>
        /// Take rules 1.13.0 and older saved on a cart or ship off it: they count for nothing now and can't be seen or changed, but every move
        /// still resends them. Only on the game that already owns it (never take a cart from the player pulling it) and not while it is open.
        /// </summary>
        public static void ForgetOnMover(Container chest)
        {
            if (!OnTheMove(chest)) return;
            var view = NView.GetValue(chest) as ZNetView;
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            if (zdo == null || !view.IsOwner() || ContainerRegistry.InUse(chest) || zdo.GetString(Key, "").Length == 0) return;
            zdo.Set(Key, "");
        }

        /// <summary>Does this chest want this item (by its exact name, or by its category)?</summary>
        public bool WantsItem(string itemName) => Items.Contains(itemName);
        public bool WantsCategory(string category) => Categories.Contains(category);
    }
}
