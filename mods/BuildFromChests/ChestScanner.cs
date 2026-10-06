using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildFromChests
{
    /// <summary>
    /// Keeps track of every loaded Container and finds the ones near the player while building.
    ///
    /// Speed matters here: the build menu asks "can you afford this piece?" for many pieces every frame, and every one of those
    /// questions ends up here. So the expensive answers (are we building? which chests are in range? how many of an item do they
    /// hold?) are worked out once and reused, not recomputed per question.
    /// </summary>
    internal static class ChestScanner
    {
        private static readonly List<Container> AllContainers = new List<Container>();
        private static readonly List<Container> Nearby = new List<Container>();

        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        /// <summary>True while a patch is doing its own inventory work, so we don't recurse into ourselves.</summary>
        internal static bool Suspend;
        /// <summary>True only while Player.ConsumeResources is running (i.e. actually paying for a piece).</summary>
        internal static bool Consuming;

        internal static void Register(Container c)
        {
            if (c != null && !c.name.StartsWith("piece_recycler") && !AllContainers.Contains(c)) AllContainers.Add(c); // the Recycler is not a storage chest
        }

        // ---- are we building? (worked out once per frame) ---------------------------------------------

        private static int _contextFrame = -1;
        private static bool _contextActive;

        /// <summary>
        /// A build tool is equipped. Not while a crafting window is open: that's CraftFromChests' job, and counting both would
        /// double-count chests. Decided once per frame.
        /// </summary>
        private static bool BuildingNow()
        {
            // Another of our mods (BuildOrders: walk up to a ghost and press E) builds without a hammer in hand and says so here.
            if (AppDomain.CurrentDomain.GetData("DHack.BuildFromChests.ForceContext") is bool forced && forced) return true;
            if (_contextFrame == Time.frameCount) return _contextActive;
            _contextFrame = Time.frameCount;
            Player p = Player.m_localPlayer;
            _contextActive = p != null && p.InPlaceMode() && p.GetCurrentCraftingStation() == null && !InventoryGui.IsVisible();
            return _contextActive;
        }

        /// <summary>Is this the local player's own inventory while they're building?</summary>
        internal static bool Applies(Inventory inv)
        {
            if (Suspend || !Plugin.Enabled.Value) return false;
            Player p = Player.m_localPlayer;
            if (p == null || inv != p.GetInventory()) return false;
            return BuildingNow();
        }

        // ---- which chests are in range (refreshed a few times a second) -------------------------------

        private static float _nearbyAt = -10f;
        private static Vector3 _nearbyOrigin;

        /// <summary>Chests near the player that we're allowed to use. Rebuilt at most four times a second, or when you move.</summary>
        internal static List<Container> GetNearby()
        {
            Player p = Player.m_localPlayer;
            if (p == null) { Nearby.Clear(); return Nearby; }

            Vector3 origin = p.transform.position;
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
                if (c.GetInventory() == null || InUse(c)) continue;
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (!(bool)CheckAccess.Invoke(c, new object[] { playerId })) continue;
                Nearby.Add(c);
            }
            return Nearby;
        }

        /// <summary>
        /// Someone has this chest open. IsInUse() is only right on the chest's owner; everyone else has to read the flag the owner
        /// keeps in the chest's save data.
        /// </summary>
        private static bool InUse(Container c)
        {
            if (c.IsInUse()) return true;
            ZNetView nview = NView.GetValue(c) as ZNetView;
            return nview != null && nview.IsValid() && !nview.IsOwner() && nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>
        /// Only the owner can save a chest's contents, so take ownership first (matters in multiplayer). Then reload the contents from
        /// the save data: until now we only had a copy that can be up to a second old, and saving that would undo another player's changes.
        /// </summary>
        private static void TakeOwnership(Container c)
        {
            ZNetView nview = NView.GetValue(c) as ZNetView;
            if (nview == null || !nview.IsValid() || nview.IsOwner()) return;
            nview.ClaimOwnership();
            Load?.Invoke(c, null); // the game's own reload: does nothing if our copy is already the latest
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
                if (c == null || InUse(c)) continue; // someone opened it since we last looked
                Inventory inv = c.GetInventory();
                if (inv.CountItems(name, quality, matchWorldLevel) <= 0) continue;

                TakeOwnership(c);
                int take = Math.Min(inv.CountItems(name, quality, matchWorldLevel), amount); // counted again: the reload may have changed it
                if (take <= 0) continue;

                inv.RemoveItem(name, take, quality, matchWorldLevel);
                amount -= take;
            }
            InvalidateCounts(); // the chests just changed: forget what we remembered
            return amount;
        }
    }
}

namespace BuildFromChests
{
    /// <summary>
    /// A small public door for our other mods (BuildOrders uses it to fetch materials from the chests around you). Mods load as separate
    /// assemblies, so instead of a reference the two plain functions are left in the app domain's shared data under these names.
    /// </summary>
    internal static class Api
    {
        internal const string TakeKey = "DHack.BuildFromChests.Take";   // Func<string,int,int>: item name, wanted amount -> amount taken out of the chests
        internal const string CountKey = "DHack.BuildFromChests.Count"; // Func<string,int>: item name -> how many the chests in range hold

        private static readonly System.Func<string, int, int> Take = (name, amount) =>
        {
            if (!Plugin.Enabled.Value || amount <= 0) return 0;
            int left = ChestScanner.RemoveFromChests(name, amount, -1, false);
            return amount - left;
        };

        private static readonly System.Func<string, int> Count = name => Plugin.Enabled.Value ? ChestScanner.CountInChests(name, -1, false) : 0;

        internal static void Publish()
        {
            System.AppDomain.CurrentDomain.SetData(TakeKey, Take);
            System.AppDomain.CurrentDomain.SetData(CountKey, Count);
        }

        /// <summary>On unload: remove the functions, but only if they are still ours (a reloaded copy may already have replaced them).</summary>
        internal static void Withdraw()
        {
            if (ReferenceEquals(System.AppDomain.CurrentDomain.GetData(TakeKey), Take)) System.AppDomain.CurrentDomain.SetData(TakeKey, null);
            if (ReferenceEquals(System.AppDomain.CurrentDomain.GetData(CountKey), Count)) System.AppDomain.CurrentDomain.SetData(CountKey, null);
        }
    }
}
