using System.Collections.Generic;
using System.Linq;

namespace QualityOfLife
{
    /// <summary>
    /// Hammer key: equip the hammer (which is what puts you in construction mode) with one press, and press again to go
    /// back to whatever you were holding.
    /// </summary>
    public partial class Plugin
    {
        private const string HammerName = "$item_hammer";

        /// <summary>What we had equipped before switching to the hammer, so a second press can put it back.</summary>
        private List<ItemDrop.ItemData> _hammerBefore;

        private static bool IsHammer(ItemDrop.ItemData item) =>
            item.m_shared.m_name == HammerName && item.m_shared.m_buildPieces != null;

        private void ToggleHammer(Player player)
        {
            ItemDrop.ItemData hammer = player.GetInventory().GetAllItems().FirstOrDefault(IsHammer);
            if (hammer == null)
            {
                Tell(player, "You don't have a hammer.");
                return;
            }

            if (!player.IsItemEquiped(hammer))
            {
                // Into construction mode: remember what we had, then take out the hammer.
                _hammerBefore = EquippedNow(player);
                Run(Equip(player, new List<ItemDrop.ItemData> { hammer }, null, "Construction mode"));
            }
            else if (_hammerBefore != null)
            {
                // Out of it: put back what we were holding (anything already on, like armor, is left alone).
                List<ItemDrop.ItemData> back = _hammerBefore.Where(i => i != hammer && player.GetInventory().ContainsItem(i)).ToList();
                _hammerBefore = null;
                Run(Equip(player, back, new List<ItemDrop.ItemData> { hammer }, "Construction mode off"));
            }
            else
            {
                // We never saw what you had before (e.g. you equipped the hammer yourself): just put it away.
                player.UnequipItem(hammer);
                Tell(player, "Construction mode off");
            }
        }
    }
}
