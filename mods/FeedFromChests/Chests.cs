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

        /// <summary>Can this player use this chest (not in use by someone else, not warded off, not someone's private chest)?</summary>
        private static bool Usable(Container c)
        {
            if (c == null || c.GetInventory() == null || InUse(c)) return false;
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
    }
}
