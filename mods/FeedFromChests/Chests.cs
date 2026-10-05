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

        internal static ZNetView ViewOf(Container c) => NView.GetValue(c) as ZNetView;

        /// <summary>Can this player use this chest (not in use by someone else, not warded off, not someone's private chest)?</summary>
        private static bool Usable(Container c)
        {
            if (c == null || c.GetInventory() == null || c.IsInUse()) return false;
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
            foreach (Container c in chests) total += c.GetInventory().CountItems(itemName);
            return total;
        }

        /// <summary>Take up to <paramref name="amount"/> of an item out of the chests (nearest first). Returns how many were taken.</summary>
        public static int Take(IEnumerable<Container> chests, string itemName, int amount)
        {
            int taken = 0;
            foreach (Container c in chests)
            {
                if (taken >= amount) break;
                Inventory inventory = c.GetInventory();
                int n = Math.Min(inventory.CountItems(itemName), amount - taken);
                if (n <= 0) continue;

                // Only the owner of a chest can save its contents, so take ownership first (matters in multiplayer).
                ZNetView view = NView.GetValue(c) as ZNetView;
                if (view != null && !view.IsOwner()) view.ClaimOwnership();

                inventory.RemoveItem(itemName, n);
                taken += n;
            }
            return taken;
        }
    }
}
