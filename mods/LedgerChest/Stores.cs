using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LedgerChest
{
    /// <summary>One kind of item across the chests: its name, picture, how many in all, and in which chests.</summary>
    internal class Line
    {
        public string Key, Name, Kind;   // Kind: its ledger button (Kinds)
        public Sprite Icon;
        public int Total, MaxStack = 1;
        public readonly List<KeyValuePair<Container, int>> Where = new List<KeyValuePair<Container, int>>();
    }

    /// <summary>
    /// The chests around a Ledger Chest and what is in them, and taking from them. Only built chests you may open (not a cart's or a ship's,
    /// not a tombstone, not a companion's bag or its own chests, nothing a ward keeps you out of). Taking moves the stacks themselves, so
    /// an upgraded or worn item comes out as it went in; a chest someone has open is left alone.
    /// </summary>
    internal static class Stores
    {
        private static readonly FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        private static ZNetView View(Container c) => NView.GetValue(c) as ZNetView;

        private static bool InUse(Container c)
        {
            if (c.IsInUse()) return true;
            ZNetView v = View(c);
            return v != null && v.IsValid() && !v.IsOwner() && v.GetZDO().GetInt(ZDOVars.s_inUse, 0) == 1;
        }

        private static bool Counts(Container c, Container ledger, Vector3 at, float radius, Player p)
        {
            if (c == null || c == ledger || !c.isActiveAndEnabled || c.GetInventory() == null) return false;
            if (c.GetComponentInParent<Piece>() == null || c.GetComponent<TombStone>() != null || c.GetComponentInParent<Character>() != null) return false;
            if (c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            if (c.gameObject.name.StartsWith("piece_recycler", StringComparison.Ordinal)) return false;
            if ((c.transform.position - at).sqrMagnitude > radius * radius) return false;
            ZNetView v = View(c);
            if (v == null || !v.IsValid() || v.GetZDO().GetLong("dhc_home", 0L) != 0L) return false; // (a companion's own chest: its stock)
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;
            return CheckAccess == null || (bool)CheckAccess.Invoke(c, new object[] { p.GetPlayerID() });
        }

        public static List<Container> Around(Container ledger, Player p)
        {
            Vector3 at = ledger.transform.position;
            float r = Plugin.Radius.Value;
            return UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => Counts(c, ledger, at, r, p))
                .OrderBy(c => (c.transform.position - at).sqrMagnitude).ToList();
        }

        /// <summary>Everything in those chests, one line per kind of item (and quality, for gear), by name.</summary>
        public static List<Line> Lines(List<Container> chests)
        {
            var lines = new Dictionary<string, Line>();
            foreach (Container c in chests)
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    if (item == null || item.m_shared == null || item.m_stack <= 0) continue;
                    bool gear = item.m_shared.m_maxQuality > 1;
                    string key = item.m_shared.m_name + (gear ? "#" + item.m_quality : "");
                    if (!lines.TryGetValue(key, out Line line))
                    {
                        string name = Localization.instance.Localize(item.m_shared.m_name);
                        lines[key] = line = new Line
                        {
                            Key = key, Name = gear && item.m_quality > 1 ? $"{name} (level {item.m_quality})" : name, Icon = item.GetIcon(),
                            MaxStack = Mathf.Max(1, item.m_shared.m_maxStackSize), Kind = Kinds.Of(item.m_shared),
                        };
                    }
                    line.Total += item.m_stack;
                    int at = line.Where.FindIndex(kv => kv.Key == c);
                    if (at >= 0) line.Where[at] = new KeyValuePair<Container, int>(c, line.Where[at].Value + item.m_stack);
                    else line.Where.Add(new KeyValuePair<Container, int>(c, item.m_stack));
                }
            return lines.Values.ToList();
        }

        private static bool Same(ItemDrop.ItemData item, string key) =>
            item.m_shared.m_name + (item.m_shared.m_maxQuality > 1 ? "#" + item.m_quality : "") == key;

        /// <summary>
        /// Up to "want" of that item out of the chests (nearest first) into "into" (your inventory, or the Ledger Chest itself). What was
        /// moved, and from how many chests. Chests someone has open are left alone.
        /// </summary>
        public static int Take(Line line, int want, Inventory into, out int fromChests, out bool busy) => Take(line, want, into, out fromChests, out busy, out _);

        public static int Take(Line line, int want, Inventory into, out int fromChests, out bool busy, out Vector2i landed) => Take(line, want, into, null, out fromChests, out busy, out landed);

        /// <summary>The same, filling the slot "first" (where you dropped it) before any other.</summary>
        public static int Take(Line line, int want, Inventory into, Vector2i? first, out int fromChests, out bool busy, out Vector2i landed)
        {
            fromChests = 0;
            busy = false;
            landed = new Vector2i(-1, -1);
            int moved = 0;
            foreach (Container c in line.Where.Select(kv => kv.Key).Where(c => c != null).ToList())
            {
                if (moved >= want) break;
                if (InUse(c)) { busy = true; continue; }
                ZNetView v = View(c);
                if (v == null || !v.IsValid()) continue;
                if (!v.IsOwner()) { v.ClaimOwnership(); Load?.Invoke(c, null); } // only the owner may change a chest: take it over, with its latest contents
                Inventory from = c.GetInventory();
                int before = moved;
                foreach (ItemDrop.ItemData item in from.GetAllItems().Where(i => Same(i, line.Key)).OrderBy(i => i.m_stack).ToList())
                {
                    while (moved < want && from.ContainsItem(item) && item.m_stack > 0)
                    {
                        Vector2i slot = Room(into, item, first);
                        if (slot.x < 0) return Done(moved, before, ref fromChests);
                        if (landed.x < 0 && into.GetItemAt(slot.x, slot.y) == null) landed = slot;
                        int had = item.m_stack;
                        into.MoveItemToThis(from, item, Mathf.Min(want - moved, item.m_stack), slot.x, slot.y);
                        int n = had - (from.ContainsItem(item) ? item.m_stack : 0);
                        if (n <= 0) return Done(moved, before, ref fromChests);
                        moved += n;
                    }
                }
                if (moved > before) fromChests++;
            }
            return moved;
        }

        // ---- sending what is put in the Ledger Chest to the chests assigned it ---------------------------------------------------------

        private static readonly int RulesKey = "DHack_StackRules".GetStableHashCode();   // QualityOfLife's chest assignments (its K menu)

        /// <summary>A chest's assignment: the item names and the categories it takes ("C=Food,Tools|I=$item_wood").</summary>
        private static void Rules(Container c, out HashSet<string> items, out HashSet<string> cats)
        {
            items = new HashSet<string>();
            cats = new HashSet<string>();
            string text = View(c)?.GetZDO()?.GetString(RulesKey, "") ?? "";
            foreach (string part in text.Split('|'))
            {
                if (part.Length < 2) continue;
                HashSet<string> into = part.StartsWith("C=") ? cats : part.StartsWith("I=") ? items : null;
                if (into == null) continue;
                foreach (string v in part.Substring(2).Split(','))
                {
                    if (v.Length == 0) continue;
                    if (into == cats && v == "Ores & Metals") { cats.Add(Categories.Ores); cats.Add(Categories.Metals); } // (an older name for both)
                    else into.Add(v);
                }
            }
        }

        /// <summary>
        /// Up to "amount" of this item (in "from": your inventory or the Ledger Chest) into the chests around the Ledger Chest: a chest assigned
        /// that item first, then its category, then an unassigned chest (one already holding it first); nearest first within each. How many
        /// went; "sent" says where.
        /// </summary>
        public static int Route(Container ledger, Player p, ItemDrop.ItemData item, Inventory from, int amount, List<string> sent)
        {
            if (item == null || item.m_shared.m_questItem || amount <= 0 || !from.ContainsItem(item)) return 0;
            var chests = Around(ledger, p).Where(c => !InUse(c)).ToList();
            var rules = chests.ToDictionary(c => c, c => { Rules(c, out var i, out var k); return new KeyValuePair<HashSet<string>, HashSet<string>>(i, k); });
            bool Unassigned(Container c) => rules[c].Key.Count == 0 && rules[c].Value.Count == 0;
            string name = item.m_shared.m_name, cat = Categories.Of(item.m_shared);
            var homes = chests.Where(c => rules[c].Key.Contains(name))
                .Concat(chests.Where(c => !rules[c].Key.Contains(name) && rules[c].Value.Contains(cat)))
                .Concat(chests.Where(c => Unassigned(c) && c.GetInventory().ContainsItemByName(name)))
                .Concat(chests.Where(c => Unassigned(c) && !c.GetInventory().ContainsItemByName(name)));
            int total = 0;
            foreach (Container to in homes)
            {
                if (total >= amount || !from.ContainsItem(item)) break;
                ZNetView v = View(to);
                if (v == null || !v.IsValid()) continue;
                if (!v.IsOwner()) { v.ClaimOwnership(); Load?.Invoke(to, null); }
                Inventory into = to.GetInventory();
                int moved = 0;
                while (total + moved < amount && from.ContainsItem(item) && item.m_stack > 0)
                {
                    Vector2i slot = Room(into, item);
                    if (slot.x < 0) break;
                    int had = item.m_stack;
                    into.MoveItemToThis(from, item, Mathf.Min(item.m_stack, amount - total - moved), slot.x, slot.y);
                    int n = had - (from.ContainsItem(item) ? item.m_stack : 0);
                    if (n <= 0) break;
                    moved += n;
                }
                total += moved;
                if (moved > 0) sent.Add($"{moved} {Localization.instance.Localize(name)} to a chest {Vector3.Distance(to.transform.position, ledger.transform.position):0} m away");
            }
            return total;
        }

        /// <summary>
        /// The Ledger Chest keeps nothing: everything put in it goes to a chest around it. A chest assigned that item first, then one assigned
        /// its category, then an unassigned chest (one already holding it first); nearest first within each. What fits in no chest goes back
        /// to you ("refused"). Returns what went where ("50 Wood to a chest 6 m away").
        /// </summary>
        public static List<string> SendOut(Container ledger, Player p, List<string> refused)
        {
            var sent = new List<string>();
            Inventory from = ledger.GetInventory();
            if (from == null || from.NrOfItems() == 0) return sent;
            var chests = Around(ledger, p).Where(c => !InUse(c)).ToList();
            int Total(string name) => from.CountItems(name) + chests.Sum(c => c.GetInventory().CountItems(name));
            var before = from.GetAllItems().Select(i => i.m_shared.m_name).Distinct().ToDictionary(n => n, n => Total(n) + p.GetInventory().CountItems(n));

            foreach (ItemDrop.ItemData item in from.GetAllItems().ToList())
                Route(ledger, p, item, from, item.m_stack, sent);
            // No chest had room: back to you (it is not a place to keep things)
            Inventory yours = p.GetInventory();
            foreach (ItemDrop.ItemData item in from.GetAllItems().ToList())
            {
                string name = Localization.instance.Localize(item.m_shared.m_name);
                int back = 0;
                while (from.ContainsItem(item) && item.m_stack > 0)
                {
                    Vector2i slot = Room(yours, item);
                    if (slot.x < 0) break;
                    int had = item.m_stack;
                    yours.MoveItemToThis(from, item, item.m_stack, slot.x, slot.y);
                    int n = had - (from.ContainsItem(item) ? item.m_stack : 0);
                    if (n <= 0) break;
                    back += n;
                }
                if (back > 0) refused.Add($"{back} {name}");
            }
            int Everywhere(string n) => Total(n) + yours.CountItems(n);
            foreach (var kv in before)
            {
                int now = Everywhere(kv.Key);
                if (now != kv.Value) Debug.LogError($"[{Plugin.Name}] SENDING CHANGED AN ITEM COUNT: {Localization.instance.Localize(kv.Key)} {kv.Value} before, {now} after");
            }
            return sent;
        }

        private static int Done(int moved, int before, ref int fromChests) { if (moved > before) fromChests++; return moved; }

        /// <summary>Where a stack of this goes in that inventory: a stack of the same with room, else an empty slot; (-1, -1) if none.</summary>
        private static Vector2i Room(Inventory into, ItemDrop.ItemData item, Vector2i? first = null)
        {
            if (first is Vector2i f && f.x >= 0 && f.y >= 0 && f.x < into.GetWidth() && f.y < NormalRows(into))
            {
                ItemDrop.ItemData there = into.GetItemAt(f.x, f.y);
                if (there == null || (there.m_shared.m_name == item.m_shared.m_name && there.m_quality == item.m_quality && there.m_stack < there.m_shared.m_maxStackSize)) return f;
            }
            ItemDrop.ItemData same = into.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality
                                                                            && i.m_shared.m_maxStackSize > 1 && i.m_stack < i.m_shared.m_maxStackSize);
            if (same != null) return same.m_gridPos;
            // An empty slot in the ordinary rows (not the GearSlots mod's gear rows below them), your hotbar last.
            int rows = NormalRows(into);
            bool player = into == Player.m_localPlayer?.GetInventory();
            for (int pass = 0; pass < 2; pass++)
                for (int y = pass == 0 && player ? 1 : 0; y < (pass == 0 || !player ? rows : 1); y++)
                    for (int x = 0; x < into.GetWidth(); x++)
                        if (into.GetItemAt(x, y) == null) return new Vector2i(x, y);
            return new Vector2i(-1, -1);
        }

        /// <summary>Your inventory's ordinary rows (not the GearSlots mod's gear rows below them); all of any other inventory.</summary>
        private static int NormalRows(Inventory into) =>
            AppDomain.CurrentDomain.GetData("DHack.GearSlots.NormalRows") is int n && n <= into.GetHeight() && into == Player.m_localPlayer?.GetInventory() ? n : into.GetHeight();
    }
}
