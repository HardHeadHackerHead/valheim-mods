using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CraftFromChests
{
    /// <summary>Keeps track of every loaded Container and finds the ones near the player's crafting station.</summary>
    internal static class ChestScanner
    {
        private static readonly List<Container> AllContainers = new List<Container>();
        private static readonly List<Container> Nearby = new List<Container>();
        private static int _cachedFrame = -1;

        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");

        /// <summary>True while a patch is doing its own inventory work, so we don't recurse into ourselves.</summary>
        internal static bool Suspend;
        /// <summary>True only while Player.ConsumeResources is running (i.e. actually crafting).</summary>
        internal static bool Consuming;

        internal static void Register(Container c)
        {
            if (!AllContainers.Contains(c)) AllContainers.Add(c);
        }

        /// <summary>Is this the local player's own inventory, and are we standing at a station?</summary>
        internal static bool Applies(Inventory inv)
        {
            if (Suspend || !Plugin.Enabled.Value) return false;
            Player p = Player.m_localPlayer;
            return p != null && inv == p.GetInventory() && p.GetCurrentCraftingStation() != null;
        }

        /// <summary>Chests near the current crafting station that we're allowed to use (cached per frame).</summary>
        internal static List<Container> GetNearby()
        {
            if (_cachedFrame == Time.frameCount) return Nearby;
            _cachedFrame = Time.frameCount;
            Nearby.Clear();

            Player p = Player.m_localPlayer;
            CraftingStation station = p != null ? p.GetCurrentCraftingStation() : null;
            if (station == null) return Nearby;

            Vector3 origin = station.transform.position;
            float maxSqr = Plugin.Radius.Value * Plugin.Radius.Value;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();

            AllContainers.RemoveAll(c => c == null); // destroyed chests
            foreach (Container c in AllContainers)
            {
                if (c.GetInventory() == null || c.IsInUse()) continue;
                if ((c.transform.position - origin).sqrMagnitude > maxSqr) continue;
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (!(bool)CheckAccess.Invoke(c, new object[] { playerId })) continue;
                Nearby.Add(c);
            }
            return Nearby;
        }

        internal static int CountInChests(string name, int quality, bool matchWorldLevel)
        {
            int total = 0;
            foreach (Container c in GetNearby()) total += c.GetInventory().CountItems(name, quality, matchWorldLevel);
            return total;
        }

        /// <summary>Remove up to <paramref name="amount"/> of an item from nearby chests; returns how many were left unfound.</summary>
        internal static int RemoveFromChests(string name, int amount, int quality, bool matchWorldLevel)
        {
            foreach (Container c in GetNearby())
            {
                if (amount <= 0) break;
                Inventory inv = c.GetInventory();

                int take = Math.Min(inv.CountItems(name, quality, matchWorldLevel), amount);
                if (take <= 0) continue;

                // Only the ZDO owner can save a chest's contents, so grab ownership first (matters in multiplayer).
                ZNetView nview = NView.GetValue(c) as ZNetView;
                if (nview != null && !nview.IsOwner()) nview.ClaimOwnership();

                inv.RemoveItem(name, take, quality, matchWorldLevel);
                amount -= take;
            }
            return amount;
        }
    }
}
