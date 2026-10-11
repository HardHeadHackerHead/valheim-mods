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
        private static readonly Func<Container, long, bool> CheckAccess = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
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
        /// Ours to take from or put into now: only the owner of a chest can save its contents, and one we claimed only a moment ago may
        /// still be changed by its old owner (see <see cref="Claims"/>). The contents are reloaded from the save data first: until then
        /// our copy can be a second old, and saving that would undo another player's changes.
        /// </summary>
        internal static bool Ready(Container c)
        {
            if (c == null || !Claims.Settled(ViewOf(c))) return false;
            Load?.Invoke(c, null); // the game's own reload: does nothing if our copy is already the latest
            return true;
        }

        /// <summary>Claim a chest another player's game owns, so it is ours to use a moment from now (never one that someone has open).</summary>
        internal static void Request(Container c)
        {
            if (c != null && !InUse(c)) Claims.Request(ViewOf(c));
        }

        /// <summary>
        /// A chest someone built. Graves, carts, ships, a companion's bag, its own chest (its stock) and a backpack carried by a player
        /// (Adventure Backpacks) have a Container too, but their things aren't there to feed stations with, or to fill.
        /// </summary>
        private static bool IsStorage(Container c)
        {
            if (c.GetComponentInParent<Piece>() == null || c.GetComponent<TombStone>() != null) return false;
            if (c.GetComponentInParent<Character>() != null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            if (DHack.Shared.SpecialContainers.NotStorage(c)) return false; // (the Obliterator, a Ledger Chest, the Arena's chests)
            ZNetView view = ViewOf(c);
            return view != null && view.IsValid() && view.GetZDO().GetLong("dhc_home", 0L) == 0L; // (a companion's own chest)
        }

        /// <summary>Can this player use this chest (not in use by someone else, not warded off, not someone's private chest)?</summary>
        private static bool Usable(Container c)
        {
            if (c == null || c.GetInventory() == null || !IsStorage(c) || InUse(c)) return false;
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            return CheckAccess(c, playerId);
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

        /// <summary>
        /// How many of an item the chests can give now (what Add and Fill can use): only chests that are ours to take from. One another
        /// player's game owns is claimed as it is counted, and counts a moment later.
        /// </summary>
        public static int Count(IEnumerable<Container> chests, string itemName)
        {
            int total = 0;
            foreach (Container c in chests)
            {
                if (c == null || InUse(c)) continue; // a chest opened since the lookup can't be taken from, so don't count it
                int n = c.GetInventory().CountItems(itemName);
                if (n <= 0) continue;
                if (Claims.Settled(ViewOf(c))) total += n;
                else Request(c);
            }
            return total;
        }

        /// <summary>How many of an item the chests hold, whichever game runs them: for stock limits ("always leave 20", "until the chests hold 100").</summary>
        public static int Stock(IEnumerable<Container> chests, string itemName)
        {
            int total = 0;
            foreach (Container c in chests)
                if (c != null && !InUse(c)) total += c.GetInventory().CountItems(itemName);
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
                if (!Ready(c)) { Request(c); continue; } // another player's game runs it: ours to take from a moment from now

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
                if (!Ready(c)) { Request(c); continue; } // another player's game runs it: ours to take from a moment from now

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
            if (from != null && !InUse(from) && Ready(from) && from.GetInventory().AddItem(item)) return;
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
                if (!Ready(chest)) { Request(chest); continue; } // another player's game runs it: ours to fill a moment from now
                Inventory inventory = chest.GetInventory();
                if (!inventory.CanAddItem(item.gameObject, count)) continue;
                int before = inventory.CountItems(name);
                inventory.AddItem(item.gameObject, count);
                return inventory.CountItems(name) - before;
            }
            return 0;
        }
    }

    /// <summary>
    /// Whose game may change a chest or a station (multiplayer). Only the owner's game can save a chest's contents, and a station's own
    /// "add" runs on its owner's game; so we claim what we use. But a claim takes a moment to reach the other games, and until it does
    /// the old owner may still change its copy: both games then take the same items out of a chest (only one save survives, and the
    /// other player's take is free), or this game adds to a station that is already full on the other one (and the item is lost). So
    /// something we claimed is only used once it has been ours for Settle seconds, by when the old owner's last changes have reached
    /// us. Something taken from us again (another player using it too) is left to them for Retry seconds before we claim it back, so
    /// two games don't keep taking it from each other. What this game has owned all along (single player, or your own base when you
    /// got there first) is used at once, as before.
    /// </summary>
    internal static class Claims
    {
        private const float Settle = 1f, Retry = 5f, Forget = 10f;

        private class Claim { public float At; public float RetryAt = -1f; }
        private static readonly Dictionary<ZDOID, Claim> Recent = new Dictionary<ZDOID, Claim>();
        private static float _prunedAt;

        /// <summary>Our recent claim on something, or null (an old one no longer matters and is forgotten).</summary>
        private static Claim Find(ZDOID id, float now)
        {
            if (!Recent.TryGetValue(id, out Claim claim)) return null;
            if (claim.RetryAt >= 0f ? now < claim.RetryAt : now - claim.At < Forget) return claim;
            Recent.Remove(id);
            return null;
        }

        /// <summary>Ours to change: this game owns it, and didn't claim it only a moment ago.</summary>
        public static bool Settled(ZNetView view)
        {
            if (view == null || !view.IsValid()) return false;
            float now = Time.unscaledTime;
            Claim claim = Find(view.GetZDO().m_uid, now);
            if (!view.IsOwner())
            {
                if (claim != null && claim.RetryAt < 0f) claim.RetryAt = now + Retry; // another player's game has taken it from us
                return false;
            }
            return claim == null || now - claim.At >= Settle;
        }

        /// <summary>Claim it so that it is ours a moment from now, unless it was taken from us only a moment ago.</summary>
        public static void Request(ZNetView view)
        {
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            float now = Time.unscaledTime;
            if (now - _prunedAt > 30f)
            {
                _prunedAt = now;
                foreach (ZDOID id in new List<ZDOID>(Recent.Keys)) Find(id, now);
            }
            Claim claim = Find(view.GetZDO().m_uid, now);
            if (claim != null)
            {
                if (claim.RetryAt < 0f) claim.RetryAt = now + Retry; // (taken from us a moment ago: leave it to them for now)
                return;
            }
            Take(view);
        }

        /// <summary>Claim it now, whoever has it (the player asked for it: Fill, or a station's settings).</summary>
        public static void Take(ZNetView view)
        {
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            view.ClaimOwnership();
            Recent[view.GetZDO().m_uid] = new Claim { At = Time.unscaledTime };
        }

        public static void Clear() => Recent.Clear();
    }
}
