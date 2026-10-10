using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Better gear from your chests, as a player picks theirs: living at home, now and then (when it gets home, and every few minutes there)
    /// it looks through your chests for a weapon, armour, shield, bow, arrows, axe or pickaxe better than what is in its gear slots, walks
    /// to the chest, takes it, wears it, and puts the one it replaced back in that chest. Pointing it at a chest (H) does the same there.
    /// Only your chests at home (not a companion's), only gear (never its hammer, food or your materials), and you can switch it off on its
    /// Home tab.
    /// </summary>
    internal static class Armory
    {
        public const string Key = "dhc_armory";
        public static bool Allowed(Component c) => Companion.Zdo(c)?.GetBool(Key, true) ?? true;

        /// <summary>The slots it fills from your chests (its hammer and food slots are not; its spare weapon follows its weapon).</summary>
        private static readonly GearKind[] Kinds =
            { GearKind.Weapon, GearKind.Shield, GearKind.Bow, GearKind.Ammo, GearKind.Axe, GearKind.Pickaxe, GearKind.Head, GearKind.Chest, GearKind.Legs, GearKind.Cape };

        private static bool Worn(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_shared.m_maxDurability > 0f && i.m_durability < i.GetMaxDurability() * 0.25f;

        /// <summary>What it has for that slot now: the item in the slot (for a weapon, the hardest-hitting melee weapon it carries, its axe too).</summary>
        private static float Current(Humanoid me, Gear.Slot s)
        {
            if (s.Kind == GearKind.Weapon)
                return me.GetInventory().GetAllItems().Where(Companion.IsMelee).Select(i => Gear.Score(me, s, i)).DefaultIfEmpty(0f).Max();
            ItemDrop.ItemData there = Gear.In(me, s);
            return there != null ? Gear.Score(me, s, there) : -1f;
        }

        /// <summary>Is this item of yours clearly better than what it has for that slot?</summary>
        private static bool Better(Humanoid me, Gear.Slot s, ItemDrop.ItemData i)
        {
            if (!Gear.Fits(s, i) || Worn(i) || i.m_shared.m_questItem) return false;
            if (s.Kind == GearKind.Weapon && (Gear.IsTool(i) || !Companion.IsMelee(i))) return false; // (axes and pickaxes have their own slots)
            if (s.Kind == GearKind.Ammo)
            {
                ItemDrop.ItemData bow = Gear.In(me, Gear.All.First(x => x.Kind == GearKind.Bow));
                if (bow == null || bow.m_shared.m_ammoType != i.m_shared.m_ammoType) return false;
                ItemDrop.ItemData arrows = Gear.In(me, s);
                return arrows == null || arrows.m_shared.m_ammoType != bow.m_shared.m_ammoType || Gear.Harm(i) > Gear.Harm(arrows) + 0.5f;
            }
            float now = Current(me, s), then = Gear.Score(me, s, i);
            if (now < 0f) return true;                                          // an empty slot: anything that fits
            return then > now * 1.05f + 0.5f;                                   // clearly better, not a hair
        }

        /// <summary>Your chests at home it may open (not a companion's, no ward against it), nearest first.</summary>
        private static IEnumerable<Container> YourChests(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed) =>
            Work.YourChests(me, center, radius).Where(c => (allowed == null || allowed(c)) && (!c.m_checkGuardStone || PrivateArea.CheckAccess(c.transform.position, 0f, false)));

        /// <summary>
        /// What it would take, from all these chests together: for each slot the best item there is that beats its own (the nearest of equals),
        /// and the chest it is in. Looked at across every chest, so it never takes a leather helmet from one chest with a bronze one in the next.
        /// </summary>
        private static Dictionary<ItemDrop.ItemData, Container> Best(Humanoid me, IEnumerable<Container> chests)
        {
            var all = chests.SelectMany(c => c.GetInventory().GetAllItems().Select(i => new KeyValuePair<ItemDrop.ItemData, Container>(i, c))).ToList();
            var picks = new Dictionary<ItemDrop.ItemData, Container>();
            foreach (GearKind kind in Kinds)
            {
                Gear.Slot s = Gear.All.First(x => x.Kind == kind);
                var best = all.Where(p => !picks.ContainsKey(p.Key) && Better(me, s, p.Key))
                              .OrderByDescending(p => Gear.Score(me, s, p.Key)).ThenBy(p => Vector3.Distance(p.Value.transform.position, me.transform.position)).FirstOrDefault();
                if (best.Key != null) picks[best.Key] = best.Value;
            }
            return picks;
        }

        /// <summary>What it would take from this chest: of the best across your chests near it, what is in this one.</summary>
        private static List<ItemDrop.ItemData> Picks(Humanoid me, Container chest) =>
            Best(me, Work.YourChests(me, chest.transform.position, 30f)).Where(kv => kv.Value == chest).Select(kv => kv.Key).ToList();

        /// <summary>The nearest chest of yours holding one of the best upgrades for it, or null. It goes round them one after another.</summary>
        public static Container Find(Humanoid me, Vector3 center, float radius, Func<Container, bool> allowed = null, bool force = false)
        {
            if (!force && !Allowed(me)) return null; // (force: you sent it, from its menu)
            return Best(me, YourChests(me, center, radius, allowed)).Values.Distinct()
                .OrderBy(c => Vector3.Distance(c.transform.position, me.transform.position)).Take(4).FirstOrDefault(c => Brain.CanReach(me, c.transform.position));
        }

        /// <summary>
        /// At a chest of yours: takes what beats its gear, wears it, and puts back in the chest what it replaced. What it changed ("bronze
        /// helmet for its leather helmet, ..."), or null.
        /// </summary>
        public static string TakeFrom(BrainState st, Container chest, bool force = false)
        {
            Humanoid me = st.Body;
            if (chest == null || !force && !Allowed(me) || Home.IdOn(chest) != 0L) return null;
            if (!Containers.Take(chest)) return null; // (someone has it open; else what is really in it, before it picks)
            Inventory mine = me.GetInventory(), its = chest.GetInventory();
            var changes = new List<string>();
            foreach (ItemDrop.ItemData pick in Picks(me, chest))
            {
                if (!its.ContainsItem(pick)) continue;
                if (mine.GetEmptySlots() == 0) { Talk.Tell(me, "There's better gear in your chest, but my bag is full.", "armoryfull", 60f); break; }
                Gear.Slot slot = Gear.All.First(s => Gear.Fits(s, pick) && Kinds.Contains(s.Kind) && (s.Kind != GearKind.Weapon || !Gear.IsTool(pick)));
                ItemDrop.ItemData old = Gear.In(me, slot); // (what it replaces: back to your chest, unless it moves to its spare slot)
                pick.m_equipped = false;
                mine.MoveItemToThis(its, pick);
                if (!mine.ContainsItem(pick)) continue;
                Gear.Arrange(me); // into its slot; the old one into its bag
                string name = NameOf(pick);
                if (old != null && mine.ContainsItem(old) && !Gear.InSlot(old))
                {
                    if (me.IsItemEquiped(old)) me.UnequipItem(old, false);
                    old.m_equipped = false;
                    its.MoveItemToThis(mine, old); // the one it replaced, back where the new one was
                    string oldName = NameOf(old);
                    changes.Add(its.ContainsItem(old) ? $"{name} for its {oldName}" : $"{name} (its {oldName} stays in its bag: the chest is full)");
                }
                else changes.Add(name);
            }
            if (changes.Count == 0) return null;
            Companion.Maintain(me, false); // wear it now
            Companion.SaveBag(me);
            string list = string.Join(", ", changes);
            st.Remember($"took better gear from your chest: {list}");
            Talk.Say(me, $"Found better gear in your chest: {list}.");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} took better gear from the chest at {chest.transform.position:F0}: {list}");
            return list;
        }

        /// <summary>An item's name with its level when it has been upgraded ("club (level 2)"), so a swap of two of the same reads right.</summary>
        private static string NameOf(ItemDrop.ItemData i) =>
            Localization.instance.Localize(i.m_shared.m_name).ToLowerInvariant() + (i.m_shared.m_maxQuality > 1 ? $" (level {i.m_quality})" : "");

        /// <summary>While you were away (its area not loaded): the same, straight from your chests near home, without the walk.</summary>
        public static string CatchUp(BrainState st, Vector3 center, float radius)
        {
            Humanoid me = st.Body;
            if (!Allowed(me)) return null;
            var all = new List<string>();
            for (int round = 0; round < 10; round++)
            {
                Container chest = Best(me, Work.YourChests(me, center, radius)).Values.FirstOrDefault();
                string got = chest != null ? TakeFrom(st, chest) : null;
                if (got == null) break;
                all.Add(got);
            }
            return all.Count > 0 ? string.Join(", ", all) : null;
        }
    }
}
