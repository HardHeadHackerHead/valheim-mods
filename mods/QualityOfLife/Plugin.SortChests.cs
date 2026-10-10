using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// The Sort chests button under your inventory: every chest in range that you can use gives up what it holds that belongs somewhere
    /// else, by the same assignments as Stack to chests (a chest assigned that item first, then one assigned its category). An item already
    /// in a chest that wants it stays; one in a chest assigned only its category moves up to a chest assigned the item itself when there is
    /// room; anything with no assigned chest stays where it is. Only chests take part: never your inventory, a companion's chest, a cart, a
    /// ship, a tombstone, or a chest someone has open. Every kind of item is counted before and after, as Stack to chests does.
    /// </summary>
    public partial class Plugin
    {
        private static readonly System.Reflection.MethodInfo InventoryChanged = AccessTools.Method(typeof(Inventory), "Changed");

        /// <summary>Does any chest in range have an assignment (so the button means something)?</summary>
        private bool AnyAssignedChest() => _chestRules.Values.Any(r => !r.IsEmpty);

        private void SortChests(Player player)
        {
            List<Container> chests = _stackChests.Where(c => c != null && c.GetInventory() != null && !ContainerRegistry.Busy(c)
                                                           && c.GetComponentInParent<Vagon>() == null && c.GetComponentInParent<Ship>() == null
                                                           && c.GetComponent<TombStone>() == null).ToList();
            if (chests.Count == 0) { Tell(player, _stackChests.Count > 0 ? "The chests in range are in use. Try again in a moment." : "No chest in range."); return; }

            // Own them all and load their latest contents first: taking a chest over reloads it, which would swap the items under us mid-move.
            foreach (Container c in chests) { ContainerRegistry.TakeOwnership(c); ContainerRegistry.Reload(c); }
            var rules = chests.ToDictionary(c => c, c => ChestRules.Read(c));
            if (rules.Values.All(r => r.IsEmpty)) { Tell(player, $"No chest in range is assigned anything yet (look at one and press {_assignKey.Value})."); return; }

            int Total(string itemName) => chests.Sum(c => c.GetInventory().CountItems(itemName));
            var totalsBefore = new Dictionary<string, int>();
            foreach (Container c in chests)
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                    if (!totalsBefore.ContainsKey(item.m_shared.m_name)) totalsBefore[item.m_shared.m_name] = Total(item.m_shared.m_name);

            // 1 = the chest is assigned this item, 2 = its category, 3 = neither.
            int Rank(Container c, string name, string category) => rules[c].WantsItem(name) ? 1 : rules[c].WantsCategory(category) ? 2 : 3;

            int moved = 0;
            var touched = new HashSet<Container>();
            var changed = new HashSet<Inventory>();
            var moves = new Dictionary<string, int>();
            // A few rounds: an item moving out of a chest can make room for another that belongs there.
            for (int round = 0; round < 4; round++)
            {
                int movedThisRound = 0;
                foreach (Container from in chests)
                {
                    Inventory source = from.GetInventory();
                    foreach (ItemDrop.ItemData item in source.GetAllItems().ToList())
                    {
                        if (item.m_shared.m_questItem) continue;
                        string name = item.m_shared.m_name, category = Categories.Of(item.m_shared);
                        int here = Rank(from, name, category);
                        if (here == 1) continue; // already home

                        int before = item.m_stack;
                        // Better homes than where it is, the item's own chests before its category's, nearest you first within each.
                        foreach (Container to in chests.Where(c => c != from && Rank(c, name, category) < here).OrderBy(c => Rank(c, name, category)))
                        {
                            if (!source.ContainsItem(item)) break;
                            int n = Deposit(to, source, item);
                            if (n <= 0) continue;
                            touched.Add(to);
                            touched.Add(from);
                            moves[name] = (moves.TryGetValue(name, out int m) ? m : 0) + n;
                        }
                        int gone = before - (source.ContainsItem(item) ? item.m_stack : 0);
                        if (gone > 0 && source.ContainsItem(item)) changed.Add(source); // part of the stack left: the chest must save the smaller stack
                        movedThisRound += gone;
                    }
                }
                moved += movedThisRound;
                if (movedThisRound == 0) break;
            }

            // A stack that only partly moved changed in place: tell its chest, or the smaller stack is never saved (and the moved part doubles).
            foreach (Inventory inv in changed)
            {
                try { InventoryChanged?.Invoke(inv, new object[] { false, false }); }
                catch (Exception) { inv.m_onChanged?.Invoke(); }
            }

            // What could not go home (its chests are full), and what has no chest at all.
            var full = new HashSet<string>();
            var homeless = new HashSet<string>();
            foreach (Container c in chests)
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    string name = item.m_shared.m_name, category = Categories.Of(item.m_shared);
                    int here = Rank(c, name, category);
                    if (here == 1) continue;
                    bool hasHome = chests.Any(o => Rank(o, name, category) < here);
                    if (hasHome) full.Add(Localization.instance.Localize(name));
                    else if (here == 3) homeless.Add(Localization.instance.Localize(name));
                }

            if (moves.Count > 0)
                Logger.LogInfo($"Sorted chests: {moved} item(s) moved between {touched.Count} chest(s): " +
                               string.Join(", ", moves.Select(kv => $"{kv.Value} {Localization.instance.Localize(kv.Key)}").ToArray()));
            if (full.Count > 0) Logger.LogInfo("Sorted chests: no room left in their chests for " + string.Join(", ", full.OrderBy(x => x).ToArray()));
            if (homeless.Count > 0) Logger.LogInfo("Sorted chests: no chest is assigned " + string.Join(", ", homeless.OrderBy(x => x).ToArray()) + " (left where they were)");

            int missing = 0;
            foreach (var kv in totalsBefore)
            {
                int now = Total(kv.Key);
                if (now == kv.Value) continue;
                missing += Math.Abs(kv.Value - now);
                Logger.LogError($"SORT CHESTS CHANGED AN ITEM COUNT: {Localization.instance.Localize(kv.Key)} had {kv.Value} in the chests before and {now} after");
            }

            string summary = moved > 0 ? $"Sorted {moved} item(s) into their chests ({touched.Count} chests)" : "Everything is already in its chest.";
            if (full.Count > 0) summary += $"   No room for: {string.Join(", ", full.OrderBy(x => x).Take(4).ToArray())}{(full.Count > 4 ? "..." : "")}";
            if (homeless.Count > 0) summary += $"   Not assigned anywhere: {homeless.Count} kind(s), left as they were";
            if (missing > 0) summary += "   WARNING: an item count changed, see the log";
            Tell(player, summary);

            if (moved > 0) PlayChestSounds(touched, opening: false);
        }
    }
}
