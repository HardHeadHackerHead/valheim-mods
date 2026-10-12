using System.Collections.Generic;
using HarmonyLib;

namespace QualityOfLife
{
    public partial class Plugin
    {
        /// <summary>
        /// An upgrade at a station makes a new item: InventoryGui.DoCrafting takes the old one away and adds a fresh one by name, with none of
        /// the old one's custom data. The KEEP lock and the quick-set tag are put on the new one (in the old one's slot, where the game puts it).
        /// Only our own keys, and the dictionary is never replaced (other mods keep their data in it too).
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        internal static class InventoryGui_DoCrafting_KeepTags
        {
            private static readonly string[] Ours = { KeepTag, Tag };

            internal class Note
            {
                public ItemDrop.ItemData Old;
                public Vector2i Slot;
                public string Name;
                public readonly List<KeyValuePair<string, string>> Tags = new List<KeyValuePair<string, string>>();
            }

            private static void Prefix(ItemDrop.ItemData ___m_craftUpgradeItem, out Note __state)
            {
                __state = null;
                ItemDrop.ItemData old = ___m_craftUpgradeItem;   // (null when making a new item, not upgrading)
                if (old?.m_customData == null) return;
                foreach (string key in Ours)
                    if (old.m_customData.TryGetValue(key, out string value))
                    {
                        if (__state == null) __state = new Note { Old = old, Slot = old.m_gridPos, Name = old.m_shared.m_name };
                        __state.Tags.Add(new KeyValuePair<string, string>(key, value));
                    }
            }

            private static void Postfix(Player player, Note __state)
            {
                if (__state == null || player == null) return;
                Inventory inventory = player.GetInventory();
                if (inventory == null || inventory.ContainsItem(__state.Old)) return;   // (not upgraded: missing materials, or another mod stopped it)
                ItemDrop.ItemData now = inventory.GetItemAt(__state.Slot.x, __state.Slot.y);
                // (an upgrader station that broke the item hands back materials, which may land in that slot: those are left alone)
                if (now == null || now == __state.Old || now.m_shared.m_name != __state.Name) return;
                if (now.m_customData == null) now.m_customData = new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> tag in __state.Tags)
                    if (!now.m_customData.ContainsKey(tag.Key)) now.m_customData[tag.Key] = tag.Value;
            }
        }
    }
}
