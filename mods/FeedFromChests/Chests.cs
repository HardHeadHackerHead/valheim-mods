using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>Finding the chests near a station, counting what's in them, and taking items out.</summary>
    internal static class Chests
    {
        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        internal static ZNetView ViewOf(Container c) => NView.GetValue(c) as ZNetView;

        /// <summary>
        /// Someone has this chest open. IsInUse() is only right on the chest's owner; everyone else has to read the flag the owner
        /// keeps in the chest's save data.
        /// </summary>
        internal static bool InUse(Container c)
        {
            if (c.IsInUse()) return true;
            ZNetView view = ViewOf(c);
            return view != null && view.IsValid() && !view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>
        /// Only the owner of a chest can save its contents, so take ownership first (matters in multiplayer). Then reload the contents
        /// from the save data: until now we only had a copy that can be up to a second old, and saving that would undo another player's changes.
        /// </summary>
        internal static void TakeOwnership(Container c)
        {
            ZNetView view = ViewOf(c);
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            view.ClaimOwnership();
            Load?.Invoke(c, null); // the game's own reload: does nothing if our copy is already the latest
        }

        /// <summary>
        /// A chest someone built. Graves, carts, ships, a companion's bag, its own chest (its stock) and a backpack carried by a player
        /// (Adventure Backpacks) have a Container too, but their things aren't there to feed stations with, or to fill.
        /// </summary>
        private static bool IsStorage(Container c)
        {
            if (c.GetComponentInParent<Piece>() == null || c.GetComponent<TombStone>() != null) return false;
            if (c.GetComponentInParent<Character>() != null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            ZNetView view = ViewOf(c);
            return view != null && view.IsValid() && view.GetZDO().GetLong("dhc_home", 0L) == 0L; // (a companion's own chest)
        }

        /// <summary>Can this player use this chest (not in use by someone else, not warded off, not someone's private chest)?</summary>
        private static bool Usable(Container c)
        {
            if (c == null || c.GetInventory() == null || !IsStorage(c) || InUse(c)) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            return (bool)CheckAccess.Invoke(c, new object[] { playerId });
        }

        // The last answer, reused for a moment: a click does several lookups for the same spot, and each one asks every chest in
        // range whether you may use it (which is the slow part).
        private static List<Container> _cached;
        private static Vector3 _cachedOrigin;
        private static float _cachedRadius, _cachedAt = -10f;

        /// <summary>Usable chests within <paramref name="radius"/> metres of a point, nearest first.</summary>
        public static List<Container> Near(Vector3 origin, float radius)
        {
            if (_cached != null && Time.unscaledTime - _cachedAt < 0.5f && radius == _cachedRadius &&
                (origin - _cachedOrigin).sqrMagnitude < 0.01f && _cached.All(c => c != null))
                return _cached;

            float max = radius * radius;
            _cached = ContainerRegistry.Alive()
                .Where(c => c != null && (c.transform.position - origin).sqrMagnitude <= max && Usable(c))
                .OrderBy(c => (c.transform.position - origin).sqrMagnitude)
                .ToList();
            _cachedOrigin = origin;
            _cachedRadius = radius;
            _cachedAt = Time.unscaledTime;
            return _cached;
        }

        /// <summary>Forget the cached answer (after a chest's contents or access may have changed).</summary>
        public static void ForgetCache() => _cached = null;

        public static int Count(IEnumerable<Container> chests, string itemName)
        {
            int total = 0;
            foreach (Container c in chests)
                if (c != null && !InUse(c)) total += c.GetInventory().CountItems(itemName); // a chest opened since the lookup can't be taken from, so don't count it
            return total;
        }

        /// <summary>Take up to <paramref name="amount"/> of an item out of the chests (nearest first). Returns how many were taken.</summary>
        public static int Take(IEnumerable<Container> chests, string itemName, int amount)
        {
            int taken = 0;
            foreach (Container c in chests)
            {
                if (taken >= amount) break;
                if (c == null || InUse(c)) continue; // someone opened it since we looked it up
                Inventory inventory = c.GetInventory();
                if (inventory.CountItems(itemName) <= 0) continue;

                TakeOwnership(c);
                int n = Math.Min(inventory.CountItems(itemName), amount - taken); // counted again: the reload may have changed it
                if (n <= 0) continue;

                inventory.RemoveItem(itemName, n);
                taken += n;
            }
            return taken;
        }
    
        /// <summary>
        /// Take one of an item out of the chests (nearest first) before a station is asked to take it, so whatever goes in is already paid
        /// for. Returns a copy of the item taken (null when no chest could give one) and the chest it came from, for <see cref="PutBack"/>.
        /// </summary>
        public static ItemDrop.ItemData TakeOne(IEnumerable<Container> chests, string itemName, out Container from)
        {
            from = null;
            foreach (Container c in chests)
            {
                if (c == null || InUse(c)) continue; // someone opened it since we looked it up
                if (c.GetInventory().CountItems(itemName) <= 0) continue;

                TakeOwnership(c);
                Inventory inventory = c.GetInventory(); // (looked up again after the reload)
                ItemDrop.ItemData stack = inventory.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == itemName && i.m_worldLevel >= Game.m_worldLevel);
                if (stack == null) continue; // the reload changed it

                ItemDrop.ItemData one = stack.Clone();
                one.m_stack = 1;
                if (!inventory.RemoveItem(stack, 1)) continue;
                from = c;
                return one;
            }
            return null;
        }

        /// <summary>A station didn't take what we took out for it: back into its chest, else your inventory, else on the ground by the station.</summary>
        public static void PutBack(ItemDrop.ItemData item, Container from, Player player, Vector3 at)
        {
            if (item == null) return;
            if (from != null && !InUse(from))
            {
                TakeOwnership(from);
                if (from.GetInventory().AddItem(item)) return;
            }
            if (player != null && player.GetInventory().AddItem(item)) return;
            ItemDrop.DropItem(item, 1, at + Vector3.up, Quaternion.identity);
        }

        /// <summary>How many of an item the player carries, counted item by item (other mods add their own sources to Inventory.CountItems).</summary>
        public static int OwnCount(Inventory inventory, string itemName)
        {
            int total = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (item.m_shared.m_name == itemName && item.m_worldLevel >= Game.m_worldLevel) total += item.m_stack;
            return total;
        }

        /// <summary>Put items into a chest near <paramref name="origin"/> that already holds some of them (for when no chest is assigned).
        /// Returns how many went in, checked by counting before and after.</summary>
        public static int AddToChestHolding(Vector3 origin, ItemDrop item, int count, float radius)
        {
            string name = item.m_itemData.m_shared.m_name;
            foreach (Container chest in Near(origin, radius))
            {
                if (chest == null || InUse(chest) || chest.GetInventory().CountItems(name) <= 0) continue;
                TakeOwnership(chest);
                Inventory inventory = chest.GetInventory();
                if (!inventory.CanAddItem(item.gameObject, count)) continue;
                int before = inventory.CountItems(name);
                inventory.AddItem(item.gameObject, count);
                return inventory.CountItems(name) - before;
            }
            return 0;
        }
    }
}
