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

        private static readonly Func<Container, long, bool> CheckAccess = AccessTools.MethodDelegate<Func<Container, long, bool>>(AccessTools.Method(typeof(Container), "CheckAccess"));
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        /// <summary>True while a patch is doing its own inventory work, so we don't recurse into ourselves.</summary>
        internal static bool Suspend;
        /// <summary>True only while the game is paying for a craft (Player.ConsumeResources, or InventoryGui.DoCrafting for "any one of these" recipes).</summary>
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
            return CraftingNow() && OtherChestMods.Which == null; // another chest-crafting mod doing it too would count every chest twice
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
                if (c.GetInventory() == null || !IsStorage(c) || InUse(c)) continue;
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (!CheckAccess(c, playerId)) continue;
                Nearby.Add(c);
            }
            return Nearby;
        }

        /// <summary>
        /// A chest someone built. Graves, carts, ships, a companion's bag, its own chest (its stock) and a backpack carried by a player
        /// (Adventure Backpacks) have a Container too, but they aren't storage to craft from.
        /// </summary>
        private static bool IsStorage(Container c)
        {
            if (c.GetComponentInParent<Piece>() == null || c.GetComponent<TombStone>() != null) return false;
            if (c.GetComponentInParent<Character>() != null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            if (DHack.Shared.SpecialContainers.NotStorage(c)) return false; // (the Obliterator, a Ledger Chest, the Arena's chests)
            ZNetView nview = NView.GetValue(c) as ZNetView;
            return nview != null && nview.IsValid() && nview.GetZDO().GetLong("dhc_home", 0L) == 0L; // (a companion's own chest)
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

        // ---- whose chest is it? (multiplayer) ---------------------------------------------------------

        // Only the game that owns a chest can save its contents, so a chest is claimed before it is paid from. But a claim takes a
        // moment to reach the other games: until it does, the old owner may still pay from its copy, and then both payments come out
        // of the same items (only one save survives: a free craft). So a chest we claimed is only paid from once it has been ours for
        // Settle seconds, by when anything the old owner did has reached us. Chests are claimed ahead, as soon as a count asks for
        // their items (when the crafting window opens), and only count once they are ours, so what you are shown is what can really
        // be paid. A chest taken from us again (another player crafting from it as well) is left to them for Retry seconds, so two
        // games don't keep taking it from each other. In single player, and for chests this game has owned all along, nothing changes.
        private const float Settle = 1f, Retry = 5f, Forget = 10f;

        private class Claim { public float At; public float RetryAt = -1f; }
        // (by the chest's object id as its two numbers: a ZDOID works out its hash from a table the game empties each time you join a
        // world, so one kept from before you left for the main menu can't be looked up again, and can throw)
        private static readonly Dictionary<(long, uint), Claim> Claims = new Dictionary<(long, uint), Claim>();
        private static (long, uint) Id(ZDOID uid) => (uid.UserID, uid.ID);
        private static float _prunedAt, _refreshAt = -1f;

        /// <summary>Our recent claim on a chest, or null (an old one no longer matters and is forgotten).</summary>
        private static Claim ClaimOn((long, uint) id, float now)
        {
            if (!Claims.TryGetValue(id, out Claim claim)) return null;
            if (claim.RetryAt >= 0f ? now < claim.RetryAt : now - claim.At < Forget) return claim;
            Claims.Remove(id);
            return null;
        }

        /// <summary>This chest is ours to change: this game owns it, and didn't claim it only a moment ago.</summary>
        private static bool Settled(Container c)
        {
            ZNetView nview = NView.GetValue(c) as ZNetView;
            if (nview == null || !nview.IsValid()) return false;
            float now = Time.unscaledTime;
            Claim claim = ClaimOn(Id(nview.GetZDO().m_uid), now);
            if (!nview.IsOwner())
            {
                if (claim != null && claim.RetryAt < 0f) claim.RetryAt = now + Retry; // another player's game has taken it from us
                return false;
            }
            return claim == null || now - claim.At >= Settle;
        }

        /// <summary>Ours to pay from now (see <see cref="Settled"/>), with its contents reloaded from the save data first.</summary>
        private static bool Ready(Container c)
        {
            if (!Settled(c)) return false;
            Load?.Invoke(c, null); // the game's own reload: does nothing if our copy is already the latest
            return true;
        }

        /// <summary>Claim a chest another game owns, so that it is ours to pay from in a moment (not one taken from us just now).</summary>
        private static void Request(Container c)
        {
            ZNetView nview = NView.GetValue(c) as ZNetView;
            if (nview == null || !nview.IsValid() || nview.IsOwner() || InUse(c)) return;
            float now = Time.unscaledTime;
            if (now - _prunedAt > 30f)
            {
                _prunedAt = now;
                foreach (var id in new List<(long, uint)>(Claims.Keys)) ClaimOn(id, now);
            }
            var uid = Id(nview.GetZDO().m_uid);
            Claim claim = ClaimOn(uid, now);
            if (claim != null)
            {
                if (claim.RetryAt < 0f) claim.RetryAt = now + Retry; // (taken from us a moment ago: leave it to them for now)
                return;
            }
            nview.ClaimOwnership();
            Claims[uid] = new Claim { At = now };
            _refreshAt = now + Settle + 0.1f;
        }

        private static readonly System.Reflection.MethodInfo UpdateCraftingPanel = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");

        /// <summary>
        /// Called every frame: once chests claimed for the open crafting window are ours, list the recipes again. (The game decides which
        /// recipes look affordable only when the window opens; the materials and the Craft button update by themselves.)
        /// </summary>
        internal static void RefreshWhenSettled()
        {
            if (_refreshAt < 0f || Time.unscaledTime < _refreshAt) return;
            _refreshAt = -1f;
            if (UpdateCraftingPanel == null || InventoryGui.instance == null || !InventoryGui.IsVisible() || !CraftingNow()) return;
            try { UpdateCraftingPanel.Invoke(InventoryGui.instance, new object[] { false }); }
            catch (Exception e) { Plugin.Log.LogWarning("Could not refresh the recipe list: " + e.Message); }
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
            foreach (Container c in GetNearby())
            {
                int n = c.GetInventory().CountItems(name, quality, matchWorldLevel);
                if (n <= 0 || InUse(c)) continue; // (opened since the list was made: it can't pay)
                if (Settled(c)) total += n;
                else Request(c); // another game's chest: claimed now, it counts (and pays) once it's ours, a moment from now
            }
            byKind[kind] = total;
            return total;
        }

        /// <summary>
        /// How many of an item the player carries, counted item by item. Inventory.CountItems isn't enough: while crafting, this mod and
        /// others (a backpack mod) add their own sources to it.
        /// </summary>
        internal static int OwnCount(Inventory inv, string name, int quality, bool matchWorldLevel)
        {
            int total = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (item.m_shared.m_name == name && (quality < 0 || item.m_quality == quality) && (!matchWorldLevel || item.m_worldLevel >= Game.m_worldLevel))
                    total += item.m_stack;
            return total;
        }

        /// <summary>
        /// Does the game charge this requirement at this station? Battle idols (m_upgraderResource) are charged only at an upgrader
        /// station, everything else everywhere but there (as Player.ConsumeResources decides).
        /// </summary>
        internal static bool Charged(Piece.Requirement req, CraftingStation station)
        {
            if (req.m_resItem == null) return false;
            return station != null ? station.m_upgrader == req.m_upgraderResource : !req.m_upgraderResource;
        }

        /// <summary>A material the game is about to charge: its name, how many, and which quality (-1 = any).</summary>
        internal struct Need
        {
            public string Name;
            public int Amount, Quality;
        }

        /// <summary>
        /// The game hands over what you craft before it charges for it, so a shortfall found while paying is too late to refuse. So check
        /// first: what the chests must supply (what's needed minus what you carry and what other mods' sources, such as a backpack, will
        /// pay) must really be in them, counted only in chests that are ours to pay from (<see cref="Settled"/>), each reloaded first
        /// (another player may have emptied it). Returns the first material that's short, or null when everything is there.
        /// </summary>
        internal static string FindShortfall(Inventory inv, List<Need> needs)
        {
            foreach (Need need in needs)
            {
                int others;
                Suspend = true; // what you have without our chests (the game's count, plus other mods' sources)
                try { others = inv.CountItems(need.Name, need.Quality, true); }
                finally { Suspend = false; }
                int fromChests = need.Amount - others;
                if (fromChests <= 0) continue;

                int found = 0;
                foreach (Container c in GetNearby())
                {
                    if (found >= fromChests) break;
                    if (c == null || InUse(c)) continue;
                    Inventory chest = c.GetInventory();
                    if (chest.CountItems(need.Name, need.Quality, true) <= 0) continue;
                    if (!Ready(c)) { Request(c); continue; } // not ours yet: it can't pay this time
                    found += chest.CountItems(need.Name, need.Quality, true); // counted again: the reload may have changed it
                }
                InvalidateCounts();
                if (found < fromChests) return need.Name;
            }
            return null;
        }

        /// <summary>An item of this kind in a chest in range (for the game to read its name and quality from), or null.</summary>
        internal static ItemDrop.ItemData FindInChests(string name, int quality)
        {
            foreach (Container c in GetNearby())
            {
                ItemDrop.ItemData item = c != null && Settled(c) ? c.GetInventory().GetItem(name, quality) : null;
                if (item != null) return item;
            }
            return null;
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
                if (!Ready(c)) { Request(c); continue; } // only chests that are ours to pay from
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
