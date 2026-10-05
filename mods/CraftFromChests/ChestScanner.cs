using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CraftFromChests
{
    /// <summary>
    /// Keeps track of every loaded Container and finds the ones near the player's crafting station.
    ///
    /// Speed matters here: opening the inventory makes the game ask "do you have the materials?" for every recipe, thousands of times
    /// in a single frame, and every one of those questions ends up here. So the expensive answers (are we crafting? which chests
    /// are in range? how many of an item do they hold?) are worked out once and reused, not recomputed per question.
    /// </summary>
    internal static class ChestScanner
    {
        private static readonly List<Container> AllContainers = new List<Container>();
        private static readonly List<Container> Nearby = new List<Container>();

        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");

        /// <summary>True while a patch is doing its own inventory work, so we don't recurse into ourselves.</summary>
        internal static bool Suspend;
        /// <summary>True only while Player.ConsumeResources is running (i.e. actually crafting).</summary>
        internal static bool Consuming;

        internal static void Register(Container c)
        {
            if (c != null && !c.name.StartsWith("piece_recycler") && !AllContainers.Contains(c)) AllContainers.Add(c); // the Recycler is not a storage chest
        }

        // ---- are we crafting? (worked out once per frame) ---------------------------------------------

        private static int _contextFrame = -1;
        private static bool _contextActive;

        /// <summary>At a crafting station, or hand-crafting from the inventory screen. Decided once per frame.</summary>
        private static bool CraftingNow()
        {
            if (_contextFrame == Time.frameCount) return _contextActive;
            _contextFrame = Time.frameCount;
            Player p = Player.m_localPlayer;
            _contextActive = p != null && (p.GetCurrentCraftingStation() != null || HandCrafting());
            return _contextActive;
        }

        /// <summary>Is this the local player's own inventory while they're crafting?</summary>
        internal static bool Applies(Inventory inv)
        {
            if (Suspend || !Plugin.Enabled.Value) return false;
            Player p = Player.m_localPlayer;
            if (p == null || inv != p.GetInventory()) return false;
            return CraftingNow();
        }

        /// <summary>The inventory screen is open on its crafting tab with no crafting station involved.</summary>
        internal static bool HandCrafting()
        {
            InventoryGui gui = InventoryGui.instance;
            return gui != null && InventoryGui.IsVisible() && gui.InCraftTab();
        }

        // ---- which chests are in range (refreshed a few times a second) -------------------------------

        private static float _nearbyAt = -10f;
        private static Vector3 _nearbyOrigin;

        /// <summary>Chests in range that we're allowed to use. Rebuilt at most four times a second, or when you move.</summary>
        internal static List<Container> GetNearby()
        {
            Player p = Player.m_localPlayer;
            if (p == null || !CraftingNow()) { Nearby.Clear(); return Nearby; }

            // At a station, chests are measured from the station; hand-crafting from the inventory, from the player.
            CraftingStation station = p.GetCurrentCraftingStation();
            Vector3 origin = station != null ? station.transform.position : p.transform.position;

            if (Time.unscaledTime - _nearbyAt < 0.25f && (origin - _nearbyOrigin).sqrMagnitude < 0.04f)
            {
                Nearby.RemoveAll(c => c == null);
                return Nearby;
            }
            _nearbyAt = Time.unscaledTime;
            _nearbyOrigin = origin;
            InvalidateCounts();
            Nearby.Clear();

            float maxSqr = Plugin.Radius.Value * Plugin.Radius.Value;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();

            AllContainers.RemoveAll(c => c == null); // destroyed chests
            foreach (Container c in AllContainers)
            {
                if ((c.transform.position - origin).sqrMagnitude > maxSqr) continue; // cheapest test first
                if (c.GetInventory() == null || c.IsInUse()) continue;
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (!(bool)CheckAccess.Invoke(c, new object[] { playerId })) continue;
                Nearby.Add(c);
            }
            return Nearby;
        }

        // ---- how many of an item the chests hold (remembered within a frame) --------------------------

        private static int _countFrame = -1;
        private static readonly Dictionary<string, Dictionary<int, int>> Counts = new Dictionary<string, Dictionary<int, int>>();

        private static void InvalidateCounts() { _countFrame = -1; }

        internal static int CountInChests(string name, int quality, bool matchWorldLevel)
        {
            if (_countFrame != Time.frameCount) { Counts.Clear(); _countFrame = Time.frameCount; }

            if (!Counts.TryGetValue(name, out Dictionary<int, int> byKind)) Counts[name] = byKind = new Dictionary<int, int>();
            int kind = quality * 2 + (matchWorldLevel ? 1 : 0);
            if (byKind.TryGetValue(kind, out int cached)) return cached; // the game asks the same question many times in a frame

            int total = 0;
            foreach (Container c in GetNearby()) total += c.GetInventory().CountItems(name, quality, matchWorldLevel);
            byKind[kind] = total;
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
            InvalidateCounts(); // the chests just changed: forget what we remembered
            return amount;
        }
    }
}
