using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    internal enum GearKind { Weapon, Spare, Shield, Bow, Ammo, Axe, Pickaxe, Hammer, Head, Chest, Legs, Cape, Belt, Food }

    /// <summary>
    /// Its gear in a place of its own (as GearSlots gives you): two rows of named slots under its bag (weapon, spare weapon, shield, bow,
    /// arrows, axe, pickaxe, hammer; helmet, chest, legs, cape, belt). They are cells of its real inventory (the game only lets it wear
    /// and wield what is in its inventory), kept apart: what it picks up, loots and gathers only ever goes into its bag (the four rows above),
    /// and its bag beside your inventory shows only the bag. What is in its gear slots it keeps when it falls; the rest goes into its
    /// tombstone. It moves the best of what it has into the slots by itself (a better axe it made, armour you gave it); you change its gear
    /// from its menu (Gear tab), from anywhere. Its bag only by hand, beside it.
    /// </summary>
    internal static class Gear
    {
        public const int BagRows = 4, Rows = 6, Width = 8;

        public class Slot { public GearKind Kind; public string Label; public int X, Y; }

        public static readonly Slot[] All =
        {
            new Slot { Kind = GearKind.Weapon,  Label = "Weapon",  X = 0, Y = 4 },
            new Slot { Kind = GearKind.Spare,   Label = "Spare",   X = 1, Y = 4 },
            new Slot { Kind = GearKind.Shield,  Label = "Shield",  X = 2, Y = 4 },
            new Slot { Kind = GearKind.Bow,     Label = "Bow",     X = 3, Y = 4 },
            new Slot { Kind = GearKind.Ammo,    Label = "Arrows",  X = 4, Y = 4 },
            new Slot { Kind = GearKind.Axe,     Label = "Axe",     X = 5, Y = 4 },
            new Slot { Kind = GearKind.Pickaxe, Label = "Pickaxe", X = 6, Y = 4 },
            new Slot { Kind = GearKind.Hammer,  Label = "Hammer",  X = 7, Y = 4 },
            new Slot { Kind = GearKind.Head,    Label = "Helmet",  X = 0, Y = 5 },
            new Slot { Kind = GearKind.Chest,   Label = "Chest",   X = 1, Y = 5 },
            new Slot { Kind = GearKind.Legs,    Label = "Legs",    X = 2, Y = 5 },
            new Slot { Kind = GearKind.Cape,    Label = "Cape",    X = 3, Y = 5 },
            new Slot { Kind = GearKind.Belt,    Label = "Belt",    X = 4, Y = 5 },
            new Slot { Kind = GearKind.Food,    Label = "Food",    X = 5, Y = 5 },
            new Slot { Kind = GearKind.Food,    Label = "Food",    X = 6, Y = 5 },
            new Slot { Kind = GearKind.Food,    Label = "Food",    X = 7, Y = 5 },
        };

        /// <summary>What its worn gear resists (a wolf cape: frost...), as a player's armour does, plus its status effects (meads).</summary>
        public static HitData.DamageModifiers Modifiers(Humanoid c)
        {
            var mods = new HitData.DamageModifiers();
            foreach (ItemDrop.ItemData item in Companion.Worn(c)) mods.Apply(item.m_shared.m_damageModifiers);
            c.GetSEMan().ApplyDamageMods(ref mods);
            return mods;
        }

        /// <summary>A companion's inventory (its gear chest's): the one these rules are for.</summary>
        public static bool IsCompanion(Inventory inv) => inv != null && inv.GetName() == "Companion";

        public static Slot At(int x, int y) => All.FirstOrDefault(s => s.X == x && s.Y == y);
        public static bool InSlot(ItemDrop.ItemData i) => i != null && i.m_gridPos.y >= BagRows;
        public static ItemDrop.ItemData In(Humanoid h, Slot s) => h.GetInventory().GetItemAt(s.X, s.Y);

        // ---- what goes where -------------------------------------------------------------------------

        private static bool IsAxe(ItemDrop.ItemData i) => i.m_shared.m_damages.m_chop > 0f && i.m_shared.m_skillType == Skills.SkillType.Axes;
        private static bool IsPick(ItemDrop.ItemData i) => i.m_shared.m_damages.m_pickaxe > 0f;
        internal static bool IsTool(ItemDrop.ItemData i) => IsAxe(i) || IsPick(i);

        public static bool Fits(Slot s, ItemDrop.ItemData i)
        {
            if (i == null) return false;
            var t = i.m_shared.m_itemType;
            switch (s.Kind)
            {
                case GearKind.Weapon:
                case GearKind.Spare: return Companion.IsMelee(i) || IsAxe(i);
                case GearKind.Shield: return t == ItemDrop.ItemData.ItemType.Shield;
                case GearKind.Bow: return Companion.IsRanged(i) && i.m_shared.m_skillType != Skills.SkillType.Fishing;
                case GearKind.Ammo: return t == ItemDrop.ItemData.ItemType.Ammo;
                case GearKind.Axe: return IsAxe(i);
                case GearKind.Pickaxe: return IsPick(i);
                case GearKind.Hammer: return Mending.IsHammer(i);
                case GearKind.Head: return t == ItemDrop.ItemData.ItemType.Helmet;
                case GearKind.Chest: return t == ItemDrop.ItemData.ItemType.Chest;
                case GearKind.Legs: return t == ItemDrop.ItemData.ItemType.Legs;
                case GearKind.Cape: return t == ItemDrop.ItemData.ItemType.Shoulder;
                case GearKind.Belt: return t == ItemDrop.ItemData.ItemType.Utility && i.m_shared.m_maxStackSize <= 1;
                case GearKind.Food: return Food.IsFood(i);
            }
            return false;
        }

        /// <summary>A gear item at all (something one of the slots takes).</summary>
        public static bool IsGear(ItemDrop.ItemData i) => All.Any(s => Fits(s, i));

        internal static float Harm(ItemDrop.ItemData i)
        {
            HitData.DamageTypes d = i.GetDamage();
            return d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
        }

        /// <summary>How good an item is in that slot (higher is better).</summary>
        internal static float Score(Humanoid h, Slot s, ItemDrop.ItemData i)
        {
            switch (s.Kind)
            {
                case GearKind.Weapon:
                case GearKind.Spare:
                case GearKind.Bow: return Harm(i) + i.m_quality * 0.01f;
                case GearKind.Shield: return i.GetBlockPower(1f) + i.m_quality * 0.01f;
                case GearKind.Ammo:
                    ItemDrop.ItemData bow = In(h, All.First(x => x.Kind == GearKind.Bow));
                    bool matches = bow == null || bow.m_shared.m_ammoType == i.m_shared.m_ammoType;
                    return (matches ? 100000f : 0f) + Harm(i) * 100f + i.m_stack;
                case GearKind.Axe: return i.m_shared.m_toolTier * 1000f + i.GetDamage().m_chop;
                case GearKind.Pickaxe: return i.m_shared.m_toolTier * 1000f + i.GetDamage().m_pickaxe;
                case GearKind.Hammer: return i.m_durability;
                default: return i.GetArmor() + i.m_quality * 0.01f;
            }
        }

        // ---- keeping the slots filled ----------------------------------------------------------------

        /// <summary>
        /// The best of what is in its bag goes into its empty or weaker gear slots (the one it had goes into the bag where the new one was).
        /// Weapons and the spare: not its axes and pickaxes (those have their own slots; you can still put one there yourself). Returns whether
        /// anything moved.
        /// </summary>
        public static bool Arrange(Humanoid h)
        {
            Inventory inv = h.GetInventory();
            if (!IsCompanion(inv)) return false;
            Grow(inv); // (a companion already about when the mod was updated)
            bool moved = false;
            moved |= ArrangeFood(inv);
            foreach (Slot s in All.Where(x => x.Kind != GearKind.Food).OrderBy(x => x.Kind == GearKind.Weapon ? 1 : x.Kind == GearKind.Spare ? 2 : x.Kind == GearKind.Ammo ? 3 : 0))
            {
                ItemDrop.ItemData current = In(h, s);
                if (current != null && !Fits(s, current)) { if (ToBag(inv, current)) { moved = true; current = null; } else continue; }
                ItemDrop.ItemData best = inv.GetAllItems().Where(i => !InSlot(i) && Fits(s, i) && !(IsTool(i) && (s.Kind == GearKind.Weapon || s.Kind == GearKind.Spare)))
                                           .OrderByDescending(i => Score(h, s, i)).FirstOrDefault();
                if (best == null) continue;
                if (current == null) { best.m_gridPos = new Vector2i(s.X, s.Y); moved = true; continue; }
                if (Score(h, s, best) <= Score(h, s, current)) continue;
                Vector2i was = best.m_gridPos;
                best.m_gridPos = new Vector2i(s.X, s.Y);
                current.m_gridPos = was; // (swapped: the old one where the new one was)
                moved = true;
            }
            if (moved) Companion.SaveBag(h);
            return moved;
        }

        private static float Worth(ItemDrop.ItemData i) => i.m_shared.m_food + i.m_shared.m_foodStamina + i.m_shared.m_foodEitr;
        private static IEnumerable<Slot> FoodSlots => All.Where(s => s.Kind == GearKind.Food);

        /// <summary>
        /// Its three food slots: its three best different foods (as a player keeps three to eat), each stack topped up from the same food in its
        /// bag; a better food in its bag takes the place of the weakest. It eats from them (the best first) as a player does.
        /// </summary>
        private static bool ArrangeFood(Inventory inv)
        {
            bool moved = false;
            var slots = FoodSlots.ToList();
            // Something else in a food slot (moved there by hand): into the bag.
            foreach (Slot s in slots)
            {
                ItemDrop.ItemData there = inv.GetItemAt(s.X, s.Y);
                if (there != null && !Food.IsFood(there) && ToBag(inv, there)) moved = true;
            }
            // The three best different foods it has.
            var best = inv.GetAllItems().Where(Food.IsFood).GroupBy(i => i.m_shared.m_name).OrderByDescending(g => Worth(g.First())).Take(slots.Count).Select(g => g.Key).ToList();
            foreach (Slot s in slots)
            {
                ItemDrop.ItemData there = inv.GetItemAt(s.X, s.Y);
                if (there == null || best.Contains(there.m_shared.m_name)) continue;
                if (ToBag(inv, there)) moved = true; // (a weaker one: out, for a better)
            }
            foreach (string name in best)
            {
                if (slots.Any(s => inv.GetItemAt(s.X, s.Y)?.m_shared.m_name == name)) continue;
                Slot free = slots.FirstOrDefault(s => inv.GetItemAt(s.X, s.Y) == null);
                ItemDrop.ItemData from = inv.GetAllItems().Where(i => !InSlot(i) && i.m_shared.m_name == name).OrderByDescending(i => i.m_stack).FirstOrDefault();
                if (free == null || from == null) continue;
                from.m_gridPos = new Vector2i(free.X, free.Y);
                moved = true;
            }
            // Topped up from the bag.
            foreach (Slot s in slots)
            {
                ItemDrop.ItemData there = inv.GetItemAt(s.X, s.Y);
                if (there == null || there.m_stack >= there.m_shared.m_maxStackSize) continue;
                foreach (ItemDrop.ItemData more in inv.GetAllItems().Where(i => !InSlot(i) && i.m_shared.m_name == there.m_shared.m_name && i.m_quality == there.m_quality).ToList())
                {
                    int n = Mathf.Min(more.m_stack, there.m_shared.m_maxStackSize - there.m_stack);
                    if (n <= 0) break;
                    there.m_stack += n;
                    more.m_stack -= n;
                    if (more.m_stack <= 0) inv.RemoveItem(more);
                    moved = true;
                }
            }
            return moved;
        }

        /// <summary>Its food slots running low (fewer than three foods, or under five of one): it would take food from its chests.</summary>
        public static bool FoodLow(Humanoid h)
        {
            var food = FoodSlots.Select(s => In(h, s)).Where(i => i != null).ToList();
            return food.Count < 3 || food.Any(i => i.m_stack < 5);
        }

        /// <summary>From its chest: food it would put in its food slots (a food it has too little of, or a new one while a slot is free or weaker).</summary>
        public static bool WantsFood(Humanoid h, ItemDrop.ItemData item)
        {
            if (!Food.IsFood(item)) return false;
            var food = FoodSlots.Select(s => In(h, s)).Where(i => i != null).ToList();
            ItemDrop.ItemData same = food.FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name);
            int carried = h.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == item.m_shared.m_name).Sum(i => i.m_stack);
            if (same != null) return carried < Mathf.Min(10, same.m_shared.m_maxStackSize);
            return food.Count < 3 || food.Any(i => Worth(i) < Worth(item));
        }

        private static bool ToBag(Inventory inv, ItemDrop.ItemData item)
        {
            Vector2i free = FreeBagCell(inv, true);
            if (free.x < 0) return false;
            item.m_gridPos = free;
            return true;
        }

        public static int BagCount(Inventory inv) => inv.GetAllItems().Count(i => !InSlot(i));

        public static Vector2i FreeBagCell(Inventory inv, bool topFirst)
        {
            int rows = Mathf.Min(BagRows, inv.GetHeight()), width = inv.GetWidth();
            for (int i = 0; i < rows; i++)
            {
                int y = topFirst ? i : rows - 1 - i;
                for (int x = 0; x < width; x++)
                    if (inv.GetItemAt(x, y) == null) return new Vector2i(x, y);
            }
            return new Vector2i(-1, -1);
        }

        /// <summary>
        /// You give it a piece of gear (its menu, from anywhere): into the slot it belongs in (the first free one of its kind, else the main one);
        /// what was there goes into its bag, or back to you when its bag is full. False (with why) when it cannot.
        /// </summary>
        public static bool Put(Humanoid c, Player p, ItemDrop.ItemData item, out string why)
        {
            why = null;
            Inventory mine = p.GetInventory(), its = c.GetInventory();
            if (!mine.ContainsItem(item)) return false;
            var fits = All.Where(s => Fits(s, item)).ToList();
            if (fits.Count == 0) { why = "That isn't gear."; return false; }
            if (!Companion.Write(c, _ => { })) { why = "Someone is going through its things."; return false; }
            Slot slot = fits.FirstOrDefault(s => In(c, s) == null) ?? fits[0];
            if (fits[0].Kind == GearKind.Food)
            {
                Slot same = fits.FirstOrDefault(s => In(c, s) is ItemDrop.ItemData f && f.m_shared.m_name == item.m_shared.m_name && f.m_stack + item.m_stack <= f.m_shared.m_maxStackSize);
                slot = same ?? fits.FirstOrDefault(s => In(c, s) == null);
                if (slot == null) // its food slots are full of other food: into its bag (it moves the best into its slots itself)
                {
                    if (!its.CanAddItem(item)) { why = $"{Companion.NameOf(c)}'s bag is full."; return false; }
                    its.MoveItemToThis(mine, item);
                    Companion.SaveBag(c);
                    Activity.Log(c, $"you gave it {Localization.instance.Localize(item.m_shared.m_name)} (into its bag)");
                    return true;
                }
            }
            ItemDrop.ItemData old = In(c, slot);
            if (old != null && old.m_shared.m_name != item.m_shared.m_name)
            {
                if (c.IsItemEquiped(old)) c.UnequipItem(old, false);
                old.m_equipped = false;
                if (!ToBag(its, old))
                {
                    if (!mine.CanAddItem(old)) { why = $"{Companion.NameOf(c)}'s bag and yours are full."; return false; }
                    mine.MoveItemToThis(its, old);
                }
            }
            if (p.IsItemEquiped(item)) p.UnequipItem(item, false);
            item.m_equipped = false;
            if (!its.MoveItemToThis(mine, item, item.m_stack, slot.X, slot.Y)) { why = "It didn't fit."; Activity.Log(c, $"could not take {Localization.instance.Localize(item.m_shared.m_name)} into its {slot.Label.ToLowerInvariant()} slot"); return false; }
            Brain.Get(c).NextGear = 0f; // wear it now
            Companion.SaveBag(c);
            Activity.Log(c, $"you gave it {Localization.instance.Localize(item.m_shared.m_name)}: into its {slot.Label.ToLowerInvariant()} slot");
            return true;
        }

        /// <summary>Old companions (four rows): the gear rows added.</summary>
        public static void Grow(Inventory inv)
        {
            if (inv == null || inv.GetHeight() >= Rows) return;
            AccessTools.Field(typeof(Inventory), "m_height").SetValue(inv, Rows);
        }
    }

    // ---- what it picks up only goes into its bag --------------------------------------------------------------

    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    internal static class Inventory_FindEmptySlot_Gear
    {
        private static void Postfix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            if (Gear.IsCompanion(__instance) && __result.y >= Gear.BagRows) __result = Gear.FreeBagCell(__instance, topFirst);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
    internal static class Inventory_HaveEmptySlot_Gear
    {
        private static void Postfix(Inventory __instance, ref bool __result)
        {
            if (Gear.IsCompanion(__instance)) __result = Gear.BagCount(__instance) < __instance.GetWidth() * Gear.BagRows;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
    internal static class Inventory_GetEmptySlots_Gear
    {
        private static void Postfix(Inventory __instance, ref int __result)
        {
            if (Gear.IsCompanion(__instance)) __result = __instance.GetWidth() * Gear.BagRows - Gear.BagCount(__instance);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(ItemDrop.ItemData), typeof(int))]
    internal static class Inventory_CanAddItem_Gear
    {
        private static readonly System.Reflection.MethodInfo FreeStack = AccessTools.Method(typeof(Inventory), "FindFreeStackSpace");

        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, int stack, ref bool __result)
        {
            if (!__result || !Gear.IsCompanion(__instance) || FreeStack == null) return;
            if (stack <= 0) stack = item.m_stack;
            int freeCells = __instance.GetWidth() * Gear.BagRows - Gear.BagCount(__instance);
            int stackSpace = (int)FreeStack.Invoke(__instance, new object[] { item.m_shared.m_name, item.m_worldLevel });
            __result = stackSpace + freeCells * item.m_shared.m_maxStackSize >= stack;
        }
    }

    /// <summary>
    /// Its bag and gear beside your inventory: the two gear rows a little apart under the bag, each slot labelled (as your GearSlots panel),
    /// the unused cells hidden.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class InventoryGrid_UpdateGui_Bag
    {
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> Elements = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");
        public const float Gap = 14f;
        private static readonly HashSet<int> Shifted = new HashSet<int>();

        private static void Postfix(InventoryGrid __instance)
        {
            if (!BagPanel.IsBagGrid(__instance) || !Gear.IsCompanion(__instance.GetInventory())) return;
            foreach (InventoryElement e in Elements(__instance))
            {
                if (e == null || e.Position.y < Gear.BagRows) continue;
                Gear.Slot slot = Gear.At(e.Position.x, e.Position.y);
                if (slot == null) { if (e.gameObject.activeSelf) e.gameObject.SetActive(false); continue; }
                if (Shifted.Add(e.GetInstanceID())) (e.transform as RectTransform).anchoredPosition += new Vector2(0f, -Gap);
                bool empty = __instance.GetInventory().GetItemAt(slot.X, slot.Y) == null;
                TMPro.TMP_Text label = Label(e);
                if (label != null) { label.text = slot.Label; label.enabled = empty; }
            }
        }

        /// <summary>The slot's name written faintly at the bottom of the cell, as your GearSlots panel does (made once per cell).</summary>
        private static TMPro.TMP_Text Label(InventoryElement e)
        {
            Transform have = e.transform.Find("CompanionSlotLabel");
            if (have != null) return have.GetComponent<TMPro.TMP_Text>();
            TMPro.TMP_Text font = e.m_amount;
            if (font == null) return null;
            var go = new GameObject("CompanionSlotLabel", typeof(RectTransform));
            go.transform.SetParent(e.transform, false);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            t.font = font.font;
            t.fontSharedMaterial = font.fontSharedMaterial;
            t.fontSize = 11f;
            t.color = new Color(1f, 1f, 1f, 0.4f);
            t.alignment = TMPro.TextAlignmentOptions.Bottom;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(0f, 4f); rt.offsetMax = Vector2.zero;
            return t;
        }

        public static void Forget() => Shifted.Clear();
    }

    /// <summary>Into one of its gear slots only what belongs there (a helmet in the helmet slot...), as your GearSlots does.</summary>
    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.DropItem))]
    internal static class InventoryGrid_DropItem_Gear
    {
        private static bool Prefix(InventoryGrid __instance, Inventory fromInventory, ItemDrop.ItemData item, Vector2i pos, ref bool __result)
        {
            Inventory target = __instance.GetInventory();
            if (Gear.IsCompanion(target) && pos.y >= Gear.BagRows)
            {
                Gear.Slot slot = Gear.At(pos.x, pos.y);
                if (slot == null || !Gear.Fits(slot, item)) return Refuse(item, slot, out __result);
            }
            // Out of one of its gear slots onto something that would be swapped back into it: that has to belong there too.
            if (Gear.IsCompanion(fromInventory) && item.m_gridPos.y >= Gear.BagRows)
            {
                Gear.Slot origin = Gear.At(item.m_gridPos.x, item.m_gridPos.y);
                ItemDrop.ItemData other = target.GetItemAt(pos.x, pos.y);
                if (origin != null && other != null && other != item && !Gear.Fits(origin, other)) return Refuse(other, origin, out __result);
            }
            return true;
        }

        private static bool Refuse(ItemDrop.ItemData item, Gear.Slot slot, out bool result)
        {
            result = false;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, slot == null ? "That isn't a gear slot" : $"{Localization.instance.Localize(item.m_shared.m_name)} doesn't go in the {slot.Label.ToLowerInvariant()} slot");
            return false;
        }
    }
}
