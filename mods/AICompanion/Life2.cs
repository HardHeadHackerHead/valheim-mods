using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Getting its things back, as a player does: after it falls and wakes, it remembers where its tombstone is, and when the tombstone is near
    /// (around its home, or near you while it follows you) it walks there, takes everything back and wears its gear again. A tombstone it
    /// cannot find where it should be (someone emptied it) is forgotten.
    /// </summary>
    internal static class Grave
    {
        public const string HasKey = "dhc_hasgrave", PosKey = "dhc_gravepos", OfKey = "dhc_tombof";

        /// <summary>
        /// E on a companion's tombstone: its things go back to it (if it is alive and near), and it wears them again; otherwise the tombstone
        /// opens like a chest. The game would otherwise hand everything to its player ("recovered"), and the companion woke with nothing.
        /// </summary>
        public static bool Interact(TombStone tomb, Humanoid user)
        {
            ZDO z = Companion.Zdo(tomb);
            long id = z?.GetLong(OfKey, 0L) ?? 0L;
            if (id == 0L && !string.IsNullOrEmpty(z?.GetString(Net.CrateKey, ""))) // (a tombstone from before it remembered whose it was: by name and player)
                id = Companion.All().Where(h => Companion.NameOf(h) == z.GetString(Net.CrateKey, "") && Companion.MasterId(h) == z.GetLong(ZDOVars.s_owner, 0L))
                                    .Select(Companion.IdOf).FirstOrDefault();
            if (id == 0L || !(user is Player p)) return true; // not a companion's: the game's own
            Container grave = tomb.GetComponent<Container>();
            if (grave == null) return true;
            Humanoid owner = Companion.All().FirstOrDefault(h => Companion.IdOf(h) == id && !h.IsDead() && Vector3.Distance(h.transform.position, tomb.transform.position) < 60f);
            if (owner == null || !Companion.CanCommand(owner, p))
            {
                grave.Interact(user, false, false); // look inside, take what you like; it collects the rest itself when it wakes
                return false;
            }
            ZNetView view = tomb.GetComponent<ZNetView>();
            if (grave.IsInUse()) { p.Message(MessageHud.MessageType.Center, "Someone has it open"); return false; }
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!Companion.Write(owner, _ => { })) { p.Message(MessageHud.MessageType.Center, $"Someone is going through {Companion.NameOf(owner)}'s things"); return false; }
            Inventory its = grave.GetInventory(), theirs = owner.GetInventory();
            int gave = 0, left = 0;
            foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (theirs.CanAddItem(item)) { theirs.MoveItemToThis(its, item); gave++; } else left++;
            }
            if (left == 0) Companion.Write(owner, cz => cz.Set(HasKey, false));
            BrainState st = Brain.Get(owner);
            st.NextGear = 0f;
            st.Remember($"you gave it its things back from its tombstone ({gave})");
            p.Message(MessageHud.MessageType.Center, left == 0 ? $"Gave {Companion.NameOf(owner)} their things back" : $"Gave {Companion.NameOf(owner)} {gave} things back; {left} did not fit");
            Plugin.Instance?.Note($"{p.GetPlayerName()} gave {Companion.NameOf(owner)} {gave} item stacks back from its tombstone ({left} left)");
            return false;
        }

        public static bool Has(Component c) => Companion.Zdo(c)?.GetBool(HasKey, false) ?? false;
        public static Vector3 Pos(Component c) => Companion.Zdo(c)?.GetVec3(PosKey, Vector3.zero) ?? Vector3.zero;

        private static TombStone Find(Humanoid me)
        {
            Vector3 at = Pos(me);
            long master = Companion.MasterId(me);
            string name = Companion.NameOf(me);
            return UnityEngine.Object.FindObjectsOfType<TombStone>().FirstOrDefault(t =>
            {
                ZDO z = Companion.Zdo(t);
                return z != null && Vector3.Distance(t.transform.position, at) < 12f && z.GetLong(ZDOVars.s_owner, 0L) == master && z.GetString(Net.CrateKey, "") == name;
            });
        }

        /// <summary>True while it is going to its grave or emptying it (the rest of its peaceful behaviour waits).</summary>
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo, Action stop)
        {
            Humanoid me = st.Body;
            if (!Has(me) || Time.time < st.NextGraveLook) return false;
            Vector3 at = Pos(me);
            float far = Vector3.Distance(at, me.transform.position);
            if (far > 80f) { st.NextGraveLook = Time.time + 5f; return false; } // too far: when it (or you with it) comes near
            TombStone tomb = Find(me);
            if (tomb == null)
            {
                if (far < 30f) { Companion.Write(me, z => z.Set(HasKey, false)); st.Remember("its tombstone is gone (someone emptied it)"); }
                st.NextGraveLook = Time.time + 5f;
                return false;
            }
            float d = Vector3.Distance(tomb.transform.position, me.transform.position);
            if (d > 2.2f)
            {
                // Getting no closer (no path the last few metres: furniture, a bed, a wall): close by, it reaches over; further, it tries later.
                if (st.GraveBest - d > 0.3f) { st.GraveBest = d; st.GraveSince = Time.time; }
                bool stuck = Time.time - st.GraveSince > 6f;
                if (stuck && d >= 8f)
                {
                    st.GraveBest = float.MaxValue;
                    st.NextGraveLook = Time.time + 30f;
                    st.Remember("could not find a way to its tombstone; it tries again soon");
                    return false;
                }
                if (!stuck) { moveTo(tomb.transform.position, 1.5f, d > 6f); Brain.Status(st, "going to get its things back from its tombstone"); return true; }
            }
            st.GraveBest = float.MaxValue;
            stop();
            Container grave = tomb.GetComponent<Container>();
            ZNetView view = tomb.GetComponent<ZNetView>();
            if (grave == null || view == null || grave.IsInUse()) return true;
            if (!view.IsOwner()) view.ClaimOwnership();
            Inventory mine = me.GetInventory(), its = grave.GetInventory();
            int took = 0, left = 0;
            foreach (ItemDrop.ItemData item in its.GetAllItems().ToList())
            {
                item.m_equipped = false;
                if (mine.CanAddItem(item)) { mine.MoveItemToThis(its, item); took++; } else left++;
            }
            if (left == 0) Companion.Write(me, z => z.Set(HasKey, false)); // the game removes the empty tombstone itself
            st.NextGear = 0f; // wear it again now
            st.Remember(left == 0 ? $"got all its things back from its tombstone ({took})" : $"took {took} things from its tombstone; {left} did not fit");
            Plugin.Instance?.Note($"{Companion.NameOf(me)} took {took} item stacks back from its tombstone ({left} left)");
            Talk.Tell(me, "Got my things back from my tombstone.");
            st.NextGraveLook = Time.time + (left > 0 ? 30f : 2f);
            return false;
        }
    }

    /// <summary>
    /// Picking up, as a helper would: what the world drops near it is picked up when it is not fighting, before it goes back to following or
    /// its work: ore and stone from rocks you (or it) mine, wood from trees, berries, and what creatures drop when they die. Only what the
    /// world drops (the game's ItemDrop.OnCreateNew, and creatures' deaths), never what a player drops from their inventory, and never what was
    /// lying there already. Per companion, on unless switched off (Orders tab). It keeps what it picks up: take it from its Inventory tab, or
    /// at home it goes into its chests.
    /// </summary>
    internal static class Loot
    {
        public const string Key = "dhc_loot", ListKey = "dhc_pick";
        public static bool On(Component c) => Companion.Zdo(c)?.GetBool(Key, true) ?? true;

        // ---- its pick-up list ----
        [Flags] public enum Kinds { None = 0, Ore = 1, Wood = 2, Stone = 4, Food = 8, Creature = 16, Gear = 32, Other = 64 }
        public const Kinds Default = Kinds.Ore | Kinds.Wood | Kinds.Stone | Kinds.Food | Kinds.Creature;
        public static Kinds List(Component c) => (Kinds)(Companion.Zdo(c)?.GetInt(ListKey, (int)Default) ?? (int)Default);

        private static readonly string[] WoodWords = { "Wood", "RoundLog", "ElderBark", "Resin" };
        private static readonly string[] StoneWords = { "Stone", "Flint", "Obsidian", "Marble", "Grausten", "Crystal" };
        private static readonly string[] CreatureWords = { "Hide", "Leather", "Pelt", "Scale", "Feather", "Bone", "Entrails", "Chitin", "Fang", "Tooth", "Horn", "Antler", "Ooze", "Wisp", "Carapace", "Mandible", "Guck", "Bloodbag", "Neck", "Tail" };

        public static Kinds KindOf(ItemDrop.ItemData i)
        {
            string name = i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name;
            var t = i.m_shared.m_itemType;
            if (t == ItemDrop.ItemData.ItemType.Trophy) return Kinds.Creature;
            if (i.IsWeapon() || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest
                || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility || t == ItemDrop.ItemData.ItemType.Ammo) return Kinds.Gear;
            if (t == ItemDrop.ItemData.ItemType.Consumable || Work.IsCookable(i) || Food.IsFood(i)) return Kinds.Food;
            if (name.Contains("Ore") || name.Contains("Scrap") || name.Contains("Flametal") || (!i.m_shared.m_teleportable && t == ItemDrop.ItemData.ItemType.Material)) return Kinds.Ore;
            if (WoodWords.Any(w => name.Contains(w))) return Kinds.Wood;
            if (StoneWords.Any(w => name.Contains(w))) return Kinds.Stone;
            if (CreatureWords.Any(w => name.Contains(w))) return Kinds.Creature;
            return Kinds.Other;
        }

        /// <summary>On its list, and not something a player dropped (the game marks items that have been in someone's inventory).</summary>
        public static bool Wants(Humanoid c, ItemDrop d) => d != null && On(c) && !d.m_itemData.m_pickedUp && (List(c) & KindOf(d.m_itemData)) != 0;

        /// <summary>
        /// As a player's auto-pickup: what is on its list within reach as it goes by is picked up, without stopping (in a fight too). A few
        /// times a second on the game that runs it.
        /// </summary>
        public static void PassBy(BrainState st)
        {
            if (Time.time < st.NextPassBy) return;
            st.NextPassBy = Time.time + 0.3f;
            Humanoid me = st.Body;
            if (!On(me)) return;
            Inventory inv = me.GetInventory();
            Vector3 at = me.transform.position;
            foreach (ItemDrop d in Drops())
            {
                if (d == null || Vector3.Distance(d.transform.position, at) > 2.2f || !Wants(me, d) || !inv.CanAddItem(d.m_itemData)) continue;
                if (Carry.Weight(me) + d.m_itemData.GetWeight() > Carry.Max(me)) continue;
                if (!d.CanPickup(false)) { d.RequestOwn(); continue; }
                string name = Localization.instance.Localize(d.m_itemData.m_shared.m_name);
                int n = d.m_itemData.m_stack;
                if (me.Pickup(d.gameObject, false, false)) st.Gathered[name] = (st.Gathered.TryGetValue(name, out int had) ? had : 0) + n;
            }
        }

        internal class Spot { public Vector3 Pos; public float At; public HashSet<int> Before = new HashSet<int>(); }

        private static readonly AccessTools.FieldRef<List<ItemDrop>> Instances = AccessTools.StaticFieldRefAccess<List<ItemDrop>>(AccessTools.Field(typeof(ItemDrop), "s_instances"));
        private static IEnumerable<ItemDrop> Drops() => Instances() ?? new List<ItemDrop>();

        /// <summary>The world just dropped this (a rock broke, a tree fell, a bush was picked): companions this game runs near it will pick it up.</summary>
        public static void Fresh(ItemDrop drop)
        {
            if (drop == null) return;
            Vector3 pos = drop.transform.position;
            foreach (Humanoid c in Companion.All())
            {
                if (!c.GetComponent<ZNetView>().IsOwner() || Vector3.Distance(c.transform.position, pos) > 30f || !Wants(c, drop)) continue;
                BrainState st = Brain.Get(c);
                if (st.LootDrops.Count < 200) st.LootDrops.Add(new KeyValuePair<ItemDrop, float>(drop, Time.time));
            }
        }

        /// <summary>A creature died near a companion this game runs: remember the spot, and what was lying there already.</summary>
        public static void Died(Character victim)
        {
            if (victim == null || Companion.Is(victim) || victim.IsPlayer()) return;
            Vector3 pos = victim.transform.position;
            foreach (Humanoid c in Companion.All())
            {
                if (!c.GetComponent<ZNetView>().IsOwner() || Vector3.Distance(c.transform.position, pos) > 25f || !On(c)) continue;
                BrainState st = Brain.Get(c);
                var spot = new Spot { Pos = pos, At = Time.time };
                foreach (ItemDrop d in Drops()) if (d != null && Vector3.Distance(d.transform.position, pos) < 8f) spot.Before.Add(d.GetInstanceID());
                st.LootSpots.Add(spot);
            }
        }

        /// <summary>True while it is picking up loot.</summary>
        public static bool Tick(BrainState st, Action<Vector3, float, bool> moveTo)
        {
            Humanoid me = st.Body;
            st.LootSpots.RemoveAll(s => Time.time - s.At > 90f);
            st.LootDrops.RemoveAll(kv => kv.Key == null || Time.time - kv.Value > 180f);
            if (st.LootSpots.Count == 0 && st.LootDrops.Count == 0) return false;
            Inventory inv = me.GetInventory();
            ItemDrop next = null;
            float best = float.MaxValue;
            foreach (var kv in st.LootDrops)
            {
                ItemDrop d = kv.Key;
                if (Time.time - kv.Value < 0.6f || !inv.CanAddItem(d.m_itemData)) continue; // (let it land first)
                float dist = Vector3.Distance(d.transform.position, me.transform.position);
                if (dist < 40f && dist < best) { best = dist; next = d; }
            }
            foreach (ItemDrop d in Drops())
            {
                if (d == null || !inv.CanAddItem(d.m_itemData) || !Wants(me, d)) continue;
                foreach (Spot s in st.LootSpots)
                {
                    float fromSpot = Vector3.Distance(d.transform.position, s.Pos);
                    if (fromSpot > 6f || s.Before.Contains(d.GetInstanceID())) continue;
                    float dist = Vector3.Distance(d.transform.position, me.transform.position);
                    if (dist < best) { best = dist; next = d; }
                }
            }
            if (next == null) { if (st.LootSpots.All(s => Time.time - s.At > 3f)) st.LootSpots.Clear(); return false; } // (drops appear a moment after death)
            if (Carry.Weight(me) + next.m_itemData.GetWeight() > Carry.Max(me)) { st.LootDrops.RemoveAll(kv => kv.Key == next); return false; } // too heavy to carry more
            if (best > 1.2f) { moveTo(next.transform.position, 0.5f, best > 6f); Brain.Status(st, "picking up the loot"); return true; }
            if (!next.CanPickup(false)) { next.RequestOwn(); return true; }
            string name = Localization.instance.Localize(next.m_itemData.m_shared.m_name);
            int n = next.m_itemData.m_stack;
            if (me.Pickup(next.gameObject, false, false)) { st.Remember($"picked up {n} {name}"); st.Gathered[name] = (st.Gathered.TryGetValue(name, out int had) ? had : 0) + n; }
            st.LootDrops.RemoveAll(kv => kv.Key == next);
            return true;
        }
    }

    /// <summary>
    /// Cooking its own food at home, as a player does: raw food it has (in its bag, or fetched from its chests) goes on a cooking station near
    /// its home (a spit over a fire, an oven with fuel), and it stays by it and takes each piece off when it is done, before it burns.
    /// </summary>
    internal static class Kitchen
    {
        private static readonly System.Reflection.MethodInfo HaveDone = AccessTools.Method(typeof(CookingStation), "HaveDoneItem");
        private static readonly System.Reflection.MethodInfo FreeSlot = AccessTools.Method(typeof(CookingStation), "GetFreeSlot");
        private static readonly System.Reflection.MethodInfo FireLit = AccessTools.Method(typeof(CookingStation), "IsFireLit");
        private static readonly System.Reflection.MethodInfo Slot = AccessTools.Method(typeof(CookingStation), "GetSlot");

        public static bool Raw(CookingStation s, ItemDrop.ItemData i) => s.m_conversion.Any(c => c.m_from != null && c.m_from.m_itemData.m_shared.m_name == i.m_shared.m_name);

        private static bool Busy(CookingStation s)
        {
            for (int i = 0; i < s.m_slots.Length; i++)
            {
                object[] a = { i, null, null, null, null };
                Slot.Invoke(s, a);
                if (!string.IsNullOrEmpty(a[1] as string)) return true;
            }
            return false;
        }

        /// <summary>
        /// Catching up (CatchUp): its raw food (in its bag and its chests) cooked on a working cooking station near home, as if it had stood
        /// there turning it (up to twenty). The cooked food goes into its bag. Returns how many.
        /// </summary>
        public static int CookAll(Humanoid me, Vector3 center, float radius)
        {
            CookingStation stove = UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None)
                .Where(s => Vector3.Distance(s.transform.position, center) < radius + 10f && Usable(s)).OrderBy(s => Vector3.Distance(s.transform.position, center)).FirstOrDefault();
            if (stove == null) return 0;
            int done = 0;
            var sources = new List<Inventory> { me.GetInventory() };
            foreach (Container c in Home.Chests(me).Where(c => !c.IsInUse()))
            {
                ZNetView v = c.GetComponent<ZNetView>();
                if (v != null && !v.IsOwner()) v.ClaimOwnership();
                sources.Add(c.GetInventory());
            }
            foreach (Inventory inv in sources)
                foreach (ItemDrop.ItemData raw in inv.GetAllItems().ToList())
                {
                    CookingStation.ItemConversion conv = stove.m_conversion.FirstOrDefault(c => c.m_from != null && c.m_to != null && c.m_from.m_itemData.m_shared.m_name == raw.m_shared.m_name);
                    if (conv == null) continue;
                    int n = Mathf.Min(raw.m_stack, 20 - done);
                    if (n <= 0) return done;
                    if (!me.GetInventory().CanAddItem(conv.m_to.gameObject, n)) continue;
                    inv.RemoveItem(raw, n);
                    me.GetInventory().AddItem(conv.m_to.gameObject, n);
                    done += n;
                }
            return done;
        }

        internal static bool Usable(CookingStation s) => s != null && (!s.m_requireFire || (bool)FireLit.Invoke(s, null));

        /// <summary>A station near home it could cook on with what it carries (null if none).</summary>
        public static CookingStation Find(Humanoid me, Vector3 center, float radius)
        {
            var raw = me.GetInventory().GetAllItems();
            if (raw.Count == 0) return null;
            return UnityEngine.Object.FindObjectsOfType<CookingStation>()
                .Where(s => Vector3.Distance(s.transform.position, center) < radius + 10f && Usable(s) && raw.Any(i => Raw(s, i)) && (int)FreeSlot.Invoke(s, null) >= 0)
                .OrderBy(s => Vector3.Distance(s.transform.position, me.transform.position)).FirstOrDefault();
        }

        /// <summary>At the station: put raw food on, take done food off. Returns false when it has nothing left to cook or wait for.</summary>
        public static bool Cook(BrainState st, CookingStation s)
        {
            Humanoid me = st.Body;
            ZNetView view = s.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if ((bool)HaveDone.Invoke(s, null))
            {
                view.InvokeRPC("RPC_RemoveDoneItem", me.transform.position, 1); // it lands at its feet; Work picks it up
                st.CookedAt = s.transform.position;
                return true;
            }
            if (Usable(s) && (int)FreeSlot.Invoke(s, null) >= 0)
            {
                ItemDrop.ItemData raw = me.GetInventory().GetAllItems().FirstOrDefault(i => Raw(s, i));
                if (raw != null && s.UseItem(me, raw)) { st.Remember($"put {Localization.instance.Localize(raw.m_shared.m_name)} on to cook"); return true; }
            }
            return Busy(s); // keep waiting by it while something is cooking
        }
    }

    /// <summary>
    /// Rested, as a player gets it, without sleeping: a while sheltered by a fire (under a roof, walls round it, a fire near) makes it rested
    /// for a time, and rested it heals and gets its breath back faster. (The game's own Rested effect is made for players only.)
    /// </summary>
    internal static class Rest
    {
        public const float Duration = 480f, Mult = 1.5f;
        private static readonly Dictionary<Humanoid, float> Sheltered = new Dictionary<Humanoid, float>(), Until = new Dictionary<Humanoid, float>();

        public static bool IsRested(Humanoid c) => Until.TryGetValue(c, out float u) && Time.time < u;
        public static float Left(Humanoid c) => Until.TryGetValue(c, out float u) ? Mathf.Max(0f, u - Time.time) : 0f;

        /// <summary>Once a second, from the weather check (which knows about shelter and fire).</summary>
        public static void Update(Humanoid c, bool shelter, bool fire)
        {
            if (shelter && fire)
            {
                if (!Sheltered.TryGetValue(c, out float since)) Sheltered[c] = since = Time.time;
                if (Time.time - since > 20f)
                {
                    bool was = IsRested(c);
                    Until[c] = Time.time + Duration;
                    if (!was) Brain.Get(c)?.Remember("rested by the fire");
                }
            }
            else Sheltered.Remove(c);
        }

        public static void Forget() { Sheltered.Clear(); Until.Clear(); }
    }

    /// <summary>
    /// Eitr, as a player's: it comes from food (mushrooms, Mistlands dishes), fills over time, and staffs use it, so a companion can fight with
    /// magic. The game asks every character "have you the eitr?" before a staff attack; companions answer from this pool.
    /// </summary>
    internal static class Eitr
    {
        private class Pool { public float Value, LastUse = -99f; }
        private static readonly Dictionary<Character, Pool> Pools = new Dictionary<Character, Pool>();

        public static float Max(Character c) => c is Humanoid h ? Food.MaxEitr(h) : 0f;

        private static Pool Of(Character c)
        {
            if (!Pools.TryGetValue(c, out Pool p)) Pools[c] = p = new Pool { Value = 0f };
            return p;
        }

        public static float Get(Character c) => Mathf.Min(Max(c), Of(c).Value);
        public static void Use(Character c, float v) { Pool p = Of(c); p.Value = Mathf.Max(0f, p.Value - v); p.LastUse = Time.time; }
        public static void Add(Character c, float v) { Pool p = Of(c); p.Value = Mathf.Min(Max(c), p.Value + v); }

        public static void Tick(Humanoid c, float dt)
        {
            Pool p = Of(c);
            float max = Max(c);
            if (p.Value > max) p.Value = max;
            if (Time.time - p.LastUse < 2f || p.Value >= max) return;
            float mult = 1f;
            c.GetSEMan().ModifyEitrRegen(ref mult);
            p.Value = Mathf.Min(max, p.Value + 2f * mult * dt);
        }

        public static void Forget() => Pools.Clear();
    }

    /// <summary>Carry weight, as a player's: 300, plus what a belt (Megingjord) adds. Over it, it cannot run.</summary>
    internal static class Carry
    {
        public static float Max(Humanoid c) => 300f + Companion.Worn(c).Select(i => i.m_shared.m_equipStatusEffect as SE_Stats).Where(se => se != null).Sum(se => se.m_addMaxCarryWeight);
        public static float Weight(Humanoid c) => c.GetInventory().GetTotalWeight();
        public static bool Over(Humanoid c) => Weight(c) > Max(c);
    }

    // E on a companion's tombstone gives its things back to it (see Grave.Interact).
    [HarmonyPatch(typeof(TombStone), nameof(TombStone.Interact))]
    internal static class TombStone_Interact
    {
        private static bool Prefix(TombStone __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (hold) return true;
            try
            {
                if (Grave.Interact(__instance, character)) return true;
                __result = true;
                return false;
            }
            catch (Exception e) { Plugin.Instance?.Warn("Companion tombstone: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(TombStone), nameof(TombStone.GetHoverText))]
    internal static class TombStone_GetHoverText
    {
        private static void Postfix(TombStone __instance, ref string __result)
        {
            if ((Companion.Zdo(__instance)?.GetLong(Grave.OfKey, 0L) ?? 0L) != 0L)
                __result += "\n<size=14>A companion's things: [E] gives them back to it (it also comes for them itself)</size>";
        }
    }

    // ---- what the world drops, for companions to pick up ----

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(ItemDrop), typeof(bool) })]
    internal static class ItemDrop_OnCreateNew_Item
    {
        private static void Postfix(ItemDrop item) { try { Loot.Fresh(item); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.OnCreateNew), new[] { typeof(GameObject), typeof(bool) })]
    internal static class ItemDrop_OnCreateNew_Object
    {
        private static void Postfix(GameObject go) { try { Loot.Fresh(go != null ? go.GetComponent<ItemDrop>() : null); } catch (Exception) { } }
    }

    // ---- eitr for companions (staffs) ----

    [HarmonyPatch(typeof(Character), nameof(Character.HaveEitr))]
    internal static class Character_HaveEitr
    {
        private static bool Prefix(Character __instance, float amount, ref bool __result)
        {
            if (!Companion.Is(__instance)) return true;
            __result = Eitr.Get(__instance) > amount;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UseEitr))]
    internal static class Character_UseEitr
    {
        private static bool Prefix(Character __instance, float eitr) { if (!Companion.Is(__instance)) return true; Eitr.Use(__instance, eitr); return false; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddEitr))]
    internal static class Character_AddEitr
    {
        private static bool Prefix(Character __instance, float v) { if (!Companion.Is(__instance)) return true; Eitr.Add(__instance, v); return false; }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetMaxEitr))]
    internal static class Character_GetMaxEitr
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Eitr.Max(__instance); }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetEitrPercentage))]
    internal static class Character_GetEitrPercentage
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Eitr.Max(__instance) > 0f ? Eitr.Get(__instance) / Eitr.Max(__instance) : 0f; }
    }
}
