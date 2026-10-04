using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>Equipping and un-equipping gear, shared by the quick set and the hammer key.</summary>
    public partial class Plugin
    {
        private Coroutine _running;

        private static string NameOf(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);

        /// <summary>Everything you currently have on (weapons, shield, armor, trinkets...).</summary>
        private static List<ItemDrop.ItemData> EquippedNow(Player player) =>
            player.GetInventory().GetAllItems().Where(player.IsItemEquiped).ToList();

        private void Run(IEnumerator routine)
        {
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(routine);
        }

        /// <summary>
        /// Take off <paramref name="remove"/>, then equip <paramref name="items"/> (weapons first, then shields, then the rest, so
        /// a shield isn't knocked off by the weapon that follows it). Each item is retried for a moment: the game refuses to
        /// change gear mid-attack or mid-dodge.
        /// </summary>
        private IEnumerator Equip(Player player, List<ItemDrop.ItemData> items, List<ItemDrop.ItemData> remove, string doneMessage)
        {
            if (remove != null)
                foreach (ItemDrop.ItemData item in remove)
                    if (player.IsItemEquiped(item)) player.UnequipItem(item);

            var failed = new List<string>();
            foreach (ItemDrop.ItemData item in items.OrderBy(EquipOrder))
            {
                float deadline = Time.time + 1.5f;
                while (!player.IsItemEquiped(item) && Time.time < deadline)
                {
                    player.EquipItem(item);
                    if (player.IsItemEquiped(item)) break;
                    yield return null;
                }
                if (!player.IsItemEquiped(item)) failed.Add(NameOf(item));
            }

            _running = null;
            Tell(player, failed.Count == 0 ? doneMessage : $"{doneMessage} (couldn't equip: {string.Join(", ", failed)})");
        }

        private static int EquipOrder(ItemDrop.ItemData item)
        {
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Torch:
                    return 0;
                case ItemDrop.ItemData.ItemType.Shield:
                    return 1;
                default:
                    return 2;
            }
        }
    }
}
