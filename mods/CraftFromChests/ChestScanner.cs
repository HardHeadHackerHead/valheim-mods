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
            if (c == null || c.name.StartsWith("piece_recycler")) return; // the Recycler is not a storage chest
            if (!AllContainers.Contains(c)) AllContainers.Add(c);
            RegisterRpcs(c);
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
        /// keeps in the chest's save data. With Multi User Chest several players use a chest at once and the chest's owner decides
        /// what changes, so an open chest is no reason to leave it out there.
        /// </summary>
        private static bool InUse(Container c)
        {
            if (OtherChestMods.MultiUserChest) return false;
            if (c.IsInUse()) return true;
            ZNetView nview = NView.GetValue(c) as ZNetView;
            return nview != null && nview.IsValid() && !nview.IsOwner() && nview.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        // ---- paying ------------------------------------------------------------------------------------
        //
        // Only the game that owns a chest can change it, so a chest is never taken from its owner. Chests this game owns are paid from
        // directly. For any other chest the owner is asked to take the items out (RPC "CFC_Take": which item, how many), and answers with
        // what it really took ("CFC_Took"). Two players crafting from the same chest at once are served one after the other by that
        // owner, so no item is paid twice, and nobody takes the chest from anybody. What the owners took waits in Prepaid for the
        // few moments until the craft is paid, and whatever is left over is given back.

        private const string TakeRpc = "CFC_Take", TookRpc = "CFC_Took";
        private const float AnswerWithin = 3f;

        /// <summary>A material to fetch from a chest another game owns.</summary>
        internal struct Ask
        {
            public Container Chest;
            public string Name;
            public int Quality, Amount;
        }

        private class PendingCraft
        {
            public int Id, Waiting;
            public float At;
            public Player Player;
            public Recipe Recipe;
            public ItemDrop.ItemData Upgrade;
        }

        private static PendingCraft Pending;
        private static int _nextId;
        private static float _creditedAt;
        /// <summary>True while the craft is run again with the fetched materials: it must not fetch a second time.</summary>
        internal static bool Replaying;
        /// <summary>A craft is waiting for the chests' owners to answer.</summary>
        internal static bool Waiting => Pending != null;

        private static readonly Dictionary<(string, int), int> Prepaid = new Dictionary<(string, int), int>();

        private static void RegisterRpcs(Container c)
        {
            ZNetView nview = NView.GetValue(c) as ZNetView;
            if (nview == null || !nview.IsValid()) return;
            try
            {
                nview.Unregister(TakeRpc); // (a hot reload registers again: the old copy's handler goes first)
                nview.Unregister(TookRpc);
                nview.Register<ZPackage>(TakeRpc, (sender, pkg) => OnTake(c, nview, sender, pkg));
                nview.Register<ZPackage>(TookRpc, (sender, pkg) => OnTook(pkg));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not register the chest messages: " + e.Message); }
        }

        /// <summary>The chest's owner: take up to the asked amount out of the chest and say what was taken.</summary>
        private static void OnTake(Container c, ZNetView nview, long sender, ZPackage pkg)
        {
            int id = pkg.ReadInt();
            string name = pkg.ReadString();
            int quality = pkg.ReadInt();
            int amount = pkg.ReadInt();
            bool worldLevel = pkg.ReadBool();

            var took = new Dictionary<int, int>();
            try
            {
                if (c != null && nview.IsValid() && nview.IsOwner() && amount > 0 && IsStorage(c) && !InUse(c))
                {
                    Load?.Invoke(c, null); // the latest saved contents
                    Inventory inv = c.GetInventory();
                    foreach (ItemDrop.ItemData item in new List<ItemDrop.ItemData>(inv.GetAllItems()))
                    {
                        if (amount <= 0) break;
                        if (item.m_shared.m_name != name || (quality >= 0 && item.m_quality != quality) || (worldLevel && item.m_worldLevel < Game.m_worldLevel)) continue;
                        int q = item.m_quality, take = Math.Min(item.m_stack, amount);
                        if (!inv.RemoveItem(item, take)) continue;   // (the chest saves itself when its contents change)
                        amount -= take;
                        took[q] = (took.TryGetValue(q, out int n) ? n : 0) + take;
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not take from a chest for another player: " + e.Message); }

            var reply = new ZPackage();
            reply.Write(id);
            reply.Write(name);
            reply.Write(took.Count);
            foreach (KeyValuePair<int, int> kv in took) { reply.Write(kv.Key); reply.Write(kv.Value); }
            nview.InvokeRPC(sender, TookRpc, reply);
        }

        /// <summary>The crafter: an owner has taken (some of) what we asked for.</summary>
        private static void OnTook(ZPackage pkg)
        {
            int id = pkg.ReadInt();
            string name = pkg.ReadString();
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                int quality = pkg.ReadInt(), n = pkg.ReadInt();
                if (n > 0) Prepaid[(name, quality)] = (Prepaid.TryGetValue((name, quality), out int have) ? have : 0) + n;
            }
            _creditedAt = Time.unscaledTime;
            if (Pending != null && Pending.Id == id) Pending.Waiting--;   // (an answer after we stopped waiting is given back by Tick)
        }

        private static int PrepaidCount(string name, int quality)
        {
            int total = 0;
            foreach (KeyValuePair<(string, int), int> kv in Prepaid)
                if (kv.Key.Item1 == name && (quality < 0 || kv.Key.Item2 == quality)) total += kv.Value;
            return total;
        }

        /// <summary>Use up to <paramref name="amount"/> of what the owners already took; returns what is still to pay.</summary>
        private static int UsePrepaid(string name, int amount, int quality)
        {
            foreach ((string, int) key in new List<(string, int)>(Prepaid.Keys))
            {
                if (amount <= 0) break;
                if (key.Item1 != name || (quality >= 0 && key.Item2 != quality)) continue;
                int take = Math.Min(Prepaid[key], amount);
                amount -= take;
                if (Prepaid[key] == take) Prepaid.Remove(key); else Prepaid[key] -= take;
            }
            return amount;
        }

        /// <summary>This game owns the chest and can change it now. (A chest nobody owns is taken: there is no one to ask.)</summary>
        private static bool Owned(Container c)
        {
            ZNetView nview = NView.GetValue(c) as ZNetView;
            if (nview == null || !nview.IsValid()) return false;
            if (nview.IsOwner()) return true;
            if (nview.GetZDO().HasOwner()) return false;
            nview.ClaimOwnership();
            return nview.IsOwner();
        }

        // ---- how many of an item the chests hold (remembered within a frame) --------------------------

        private static int _countFrame = -1;
        private static readonly Dictionary<string, Dictionary<int, int>> Counts = new Dictionary<string, Dictionary<int, int>>();

        private static void InvalidateCounts() { _countFrame = -1; }

        /// <summary>
        /// What the chests in range hold, as every game sees it (a chest another game owns is at most a second old). Whether it can really
        /// be paid is settled when Craft is pressed, so the numbers shown don't change when someone else crafts from the same chests.
        /// </summary>
        internal static int CountInChests(string name, int quality, bool matchWorldLevel)
        {
            if (_countFrame != Time.frameCount) { Counts.Clear(); _countFrame = Time.frameCount; }

            if (!Counts.TryGetValue(name, out Dictionary<int, int> byKind)) Counts[name] = byKind = new Dictionary<int, int>();
            int kind = quality * 2 + (matchWorldLevel ? 1 : 0);
            if (byKind.TryGetValue(kind, out int cached)) return cached; // the game asks the same question many times in a frame

            int total = PrepaidCount(name, quality);
            foreach (Container c in GetNearby())
                if (!InUse(c)) total += c.GetInventory().CountItems(name, quality, matchWorldLevel);
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
        /// pay) must really be there: in what the owners already took, in chests this game owns (each reloaded first, another player may
        /// have emptied it), or in chests another game owns, which are listed in <paramref name="asks"/> to be asked for. Returns the
        /// first material that's short, or null when everything can be had.
        /// </summary>
        internal static string FindShortfall(Inventory inv, List<Need> needs, List<Ask> asks)
        {
            foreach (Need need in needs)
            {
                int others;
                Suspend = true; // what you have without our chests (the game's count, plus other mods' sources)
                try { others = inv.CountItems(need.Name, need.Quality, true); }
                finally { Suspend = false; }
                int fromChests = need.Amount - others;
                if (fromChests <= 0) continue;

                int found = PrepaidCount(need.Name, need.Quality);
                foreach (Container c in GetNearby())   // chests this game owns first: no asking
                {
                    if (found >= fromChests) break;
                    if (c == null || InUse(c) || c.GetInventory().CountItems(need.Name, need.Quality, true) <= 0) continue;
                    if (!Owned(c)) continue;
                    Load?.Invoke(c, null);
                    found += c.GetInventory().CountItems(need.Name, need.Quality, true); // counted after the reload
                }
                foreach (Container c in GetNearby())   // then the others', through their owners
                {
                    if (found >= fromChests) break;
                    if (c == null || InUse(c) || Owned(c)) continue;
                    int n = c.GetInventory().CountItems(need.Name, need.Quality, true);
                    if (n <= 0) continue;
                    int want = Math.Min(n, fromChests - found);
                    asks.Add(new Ask { Chest = c, Name = need.Name, Quality = need.Quality, Amount = want });
                    found += want;
                }
                InvalidateCounts();
                if (found < fromChests) return need.Name;
            }
            return null;
        }

        /// <summary>Ask the owners of these chests for their items. The craft is run again by <see cref="Tick"/> when they have answered.</summary>
        internal static void Fetch(List<Ask> asks, Player player, Recipe recipe, ItemDrop.ItemData upgrade)
        {
            var pending = new PendingCraft { Id = ++_nextId, At = Time.unscaledTime, Player = player, Recipe = recipe, Upgrade = upgrade };
            foreach (Ask ask in asks)
            {
                ZNetView nview = NView.GetValue(ask.Chest) as ZNetView;
                if (nview == null || !nview.IsValid()) continue;
                var pkg = new ZPackage();
                pkg.Write(pending.Id);
                pkg.Write(ask.Name);
                pkg.Write(ask.Quality);
                pkg.Write(ask.Amount);
                pkg.Write(true);
                nview.InvokeRPC(nview.GetZDO().GetOwner(), TakeRpc, pkg);
                pending.Waiting++;
            }
            Pending = pending;
        }

        private static readonly System.Reflection.MethodInfo DoCrafting = AccessTools.Method(typeof(InventoryGui), "DoCrafting");
        private static readonly AccessTools.FieldRef<InventoryGui, Recipe> CraftRecipe = AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
        private static readonly AccessTools.FieldRef<InventoryGui, ItemDrop.ItemData> CraftUpgrade = AccessTools.FieldRefAccess<InventoryGui, ItemDrop.ItemData>("m_craftUpgradeItem");

        /// <summary>
        /// Called every frame. Once the owners have answered (or three seconds have gone by) the craft is run again, now with the
        /// materials in hand; what is left over afterwards, or what came too late, is given back.
        /// </summary>
        internal static void Tick()
        {
            PendingCraft p = Pending;
            if (p == null)
            {
                if (Prepaid.Count > 0 && Time.unscaledTime - _creditedAt > 0.5f) GiveBack();
                return;
            }
            bool answered = p.Waiting <= 0;
            if (!answered && Time.unscaledTime - p.At < AnswerWithin) return;
            Pending = null;

            InventoryGui gui = InventoryGui.instance;
            bool same = gui != null && InventoryGui.IsVisible() && p.Player != null && p.Player == Player.m_localPlayer
                        && CraftRecipe(gui) == p.Recipe && CraftUpgrade(gui) == p.Upgrade;
            if (!same || DoCrafting == null) { GiveBack(); return; }   // (the window was closed or the recipe changed meanwhile)

            Replaying = true;
            try { DoCrafting.Invoke(gui, new object[] { p.Player }); }
            catch (Exception e) { Plugin.Log.LogWarning("Crafting from chests: " + e.Message); }
            finally { Replaying = false; GiveBack(); }
        }

        private static Dictionary<string, GameObject> _prefabs;

        private static GameObject PrefabOf(string name)
        {
            if (_prefabs == null && ObjectDB.instance != null)
            {
                _prefabs = new Dictionary<string, GameObject>();
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (drop != null && !_prefabs.ContainsKey(drop.m_itemData.m_shared.m_name)) _prefabs[drop.m_itemData.m_shared.m_name] = go;
                }
            }
            return _prefabs != null && _prefabs.TryGetValue(name, out GameObject prefab) ? prefab : null;
        }

        /// <summary>Hand back what the owners took and the craft did not use: into the inventory, or on the ground if it is full.</summary>
        private static void GiveBack()
        {
            if (Prepaid.Count == 0) return;
            Player player = Player.m_localPlayer;
            foreach (KeyValuePair<(string, int), int> kv in new List<KeyValuePair<(string, int), int>>(Prepaid))
            {
                GameObject prefab = PrefabOf(kv.Key.Item1);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (player == null || drop == null) { Plugin.Log.LogWarning($"Could not give back {kv.Value} {kv.Key.Item1} taken from a chest"); continue; }
                int left = kv.Value, max = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                while (left > 0)
                {
                    int stack = Math.Min(left, max);
                    left -= stack;
                    if (player.GetInventory().AddItem(prefab.name, stack, kv.Key.Item2, 0, 0L, "", false, true) != null) continue;
                    ItemDrop.ItemData item = drop.m_itemData.Clone();
                    item.m_quality = kv.Key.Item2;
                    ItemDrop.DropItem(item, stack, player.transform.position + player.transform.forward + Vector3.up, Quaternion.identity);
                }
            }
            Prepaid.Clear();
            InvalidateCounts();
        }

        /// <summary>An item of this kind in a chest in range (for the game to read its name and quality from), or null.</summary>
        internal static ItemDrop.ItemData FindInChests(string name, int quality)
        {
            foreach (Container c in GetNearby())
            {
                ItemDrop.ItemData item = c != null ? c.GetInventory().GetItem(name, quality) : null;
                if (item != null) return item;
            }
            return null;
        }

        /// <summary>Pay up to <paramref name="amount"/> of an item from what the owners took, then from the chests this game owns; returns how many were left unfound.</summary>
        internal static int RemoveFromChests(string name, int amount, int quality, bool matchWorldLevel)
        {
            amount = UsePrepaid(name, amount, quality);
            foreach (Container c in GetNearby())
            {
                if (amount <= 0) break;
                if (c == null || InUse(c)) continue; // someone opened it since we last looked
                Inventory inv = c.GetInventory();
                if (inv.CountItems(name, quality, matchWorldLevel) <= 0) continue;
                if (!Owned(c)) continue;             // another game's chest: asked for before (Fetch), never taken
                Load?.Invoke(c, null);
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
