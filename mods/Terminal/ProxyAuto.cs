using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The Proxy on its own: walking to a place by the game's own pathfinding (BaseAI.MoveTo, the call every creature's AI uses), one
    /// swing at a target, using what is in reach, wearing and wielding from its bag, talking, moving things to and from a chest. These are
    /// the calls a script makes through ScriptsBridge. A player taking the keys cancels every order (the driver always wins).
    /// </summary>
    internal partial class ProxyBody
    {
        private static readonly MethodInfo MoveToMethod = AccessTools.Method(typeof(BaseAI), "MoveTo");
        private Vector3? _goal;
        private bool _goalRun, _arrived, _wasDriven;
        private float _lastProgress, _goalBestDistance;

        internal bool Arrived => _arrived;
        internal bool HasGoal => _goal.HasValue;

        /// <summary>Walk there. Replaces the last order. False when this game doesn't own the Proxy or a player is driving it.</summary>
        internal bool GoTo(Vector3 point, bool run)
        {
            if (!IsOwner || Body == null || Body.IsDead() || Driven) return false;
            _goal = point;
            _goalRun = run;
            _arrived = false;
            _goalBestDistance = Vector3.Distance(transform.position, point);
            _lastProgress = Time.time;
            return true;
        }

        internal void StopGoal()
        {
            _goal = null;
            if (IsOwner && Body != null) { Body.SetMoveDir(Vector3.zero); Body.SetRun(false); }
        }

        /// <summary>Every frame on the owner: follow the order if nobody is at the keys, and tell the scripts when that changes.</summary>
        private void AutoTick()
        {
            bool driven = Driven;
            if (driven != _wasDriven) { _wasDriven = driven; ScriptsBridge.Raise(driven ? "taken" : "released"); }
            if (!IsOwner || Body == null || Body.IsDead()) return;
            if (driven) { _goal = null; return; }
            if (!_goal.HasValue) return;
            BaseAI ai = GetComponent<BaseAI>();
            if (ai == null || MoveToMethod == null) { _goal = null; return; }
            bool done = false;
            try { done = (bool)MoveToMethod.Invoke(ai, new object[] { Time.deltaTime, _goal.Value, 1.8f, _goalRun }); }
            catch (System.Exception e) { Plugin.Log.LogWarning("The Proxy could not walk: " + e.Message); _goal = null; return; }
            if (done) { _goal = null; _arrived = true; Body.SetMoveDir(Vector3.zero); Body.SetRun(false); ScriptsBridge.Raise("arrived"); return; }
            float left = Vector3.Distance(transform.position, _goal.Value);
            if (left < _goalBestDistance - 0.5f) { _goalBestDistance = left; _lastProgress = Time.time; }
            else if (Time.time - _lastProgress > 6f) { _goal = null; Body.SetMoveDir(Vector3.zero); Body.SetRun(false); ScriptsBridge.Raise("blocked"); }
        }

        // ---- acting ----

        private static readonly int ReachMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "item", "character", "vehicle");

        /// <summary>One swing of what it holds at a place (a creature, a tree, a rock) within reach: it turns to face it first.</summary>
        internal bool SwingAt(Vector3 at)
        {
            if (!IsOwner || Body == null || Body.IsDead() || Driven) return false;
            if (Vector3.Distance(transform.position, at) > 4.5f) return false;
            Vector3 flat = at - transform.position; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) Body.SetLookDir(flat.normalized, 0f);
            if (!Spend(10f)) return false;
            return Body.StartAttack(null, false);
        }

        /// <summary>Use what is at a place within reach: a door, a plant, a lever, a loose item (picked up).</summary>
        internal bool UseAt(Vector3 at)
        {
            if (!IsOwner || Body == null || Body.IsDead() || Driven) return false;
            if (Vector3.Distance(transform.position, at) > 4.5f) return false;
            Collider[] hits = Physics.OverlapSphere(at, 1.2f, ReachMask, QueryTriggerInteraction.Ignore);
            foreach (Collider c in hits.OrderBy(h => Vector3.Distance(h.transform.position, at)))
            {
                if (c.transform.IsChildOf(transform)) continue;
                ItemDrop drop = c.GetComponentInParent<ItemDrop>();
                if (drop != null && Body.Pickup(drop.gameObject, false, true)) return true;
                Interactable it = c.GetComponentInParent<Interactable>();
                if (it == null || !(it is Door || it is Pickable || it is Switch || it is Beehive)) continue;
                try { return it.Interact(Body, false, false) || true; } catch (System.Exception) { return false; }
            }
            return false;
        }

        internal void LookAt(Vector3 at)
        {
            if (!IsOwner || Body == null) return;
            Vector3 flat = at - transform.position; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) Body.SetLookDir(flat.normalized, 0f);
        }

        internal bool Equip(string slot, string prefab)
        {
            if (!IsOwner || Body == null || Body.IsDead() || Driven) return false;
            foreach (ItemDrop.ItemData item in Body.GetInventory().GetAllItems())
            {
                if (item.m_dropPrefab == null || Utils.GetPrefabName(item.m_dropPrefab) != prefab) continue;
                var type = item.m_shared.m_itemType;
                bool ok = slot == "hand" ? (type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.Tool || type == ItemDrop.ItemData.ItemType.Bow || type == ItemDrop.ItemData.ItemType.Shield)
                    : slot == "chest" ? type == ItemDrop.ItemData.ItemType.Chest
                    : slot == "legs" ? type == ItemDrop.ItemData.ItemType.Legs
                    : slot == "head" ? type == ItemDrop.ItemData.ItemType.Helmet : false;
                if (!ok) return false;
                return Body.EquipItem(item, true);
            }
            return false;
        }

        internal int CountOf(string prefab)
        {
            int n = 0;
            foreach (ItemDrop.ItemData item in Body.GetInventory().GetAllItems())
                if (item.m_dropPrefab != null && Utils.GetPrefabName(item.m_dropPrefab) == prefab) n += item.m_stack;
            return n;
        }

        internal void Speak(string text)
        {
            if (Chat.instance == null || Body == null) return;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2.2f, 30f, 5f, "", text.Length > 120 ? text.Substring(0, 120) : text, false);
        }

        // ---- chests ----

        private Container ChestAt(Vector3 at)
        {
            if (Vector3.Distance(transform.position, at) > 5f) return null;
            foreach (Collider c in Physics.OverlapSphere(at, 1.0f, ReachMask, QueryTriggerInteraction.Ignore))
            {
                Container container = c.GetComponentInParent<Container>();
                if (container == null || container.GetComponentInParent<Character>() != null || container.GetComponentInParent<Piece>() == null) continue;
                if (container.GetComponentInParent<Incinerator>() != null) continue;
                return container;
            }
            return null;
        }

        private static bool Open(Container container)
        {
            ZNetView view = container.GetComponent<ZNetView>() ?? container.GetComponentInParent<ZNetView>();
            if (view == null || view.GetZDO() == null) return false;
            if (view.GetZDO().GetInt(ZDOVars.s_inUse, 0) == 1) return false;   // (a player has it open: pitfall "is this chest open")
            view.ClaimOwnership();
            AccessTools.Method(typeof(Container), "Load")?.Invoke(container, null);   // (what the saved data says, before touching it)
            return true;
        }

        /// <summary>Items from its bag into a chest within reach (never its starting gear), all or only the named ones. How many moved.</summary>
        internal int Deposit(Vector3 chestAt, string[] only)
        {
            if (!IsOwner || Driven) return 0;
            Container chest = ChestAt(chestAt);
            if (chest == null || !Open(chest)) return 0;
            Inventory bag = Body.GetInventory(), to = chest.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList())
            {
                if (item.m_equipped || item.m_customData.ContainsKey("dh_tm_start")) continue;
                string name = item.m_dropPrefab != null ? Utils.GetPrefabName(item.m_dropPrefab) : "";
                if (only != null && !only.Contains(name)) continue;
                int stack = item.m_stack;
                if (!to.CanAddItem(item)) continue;
                if (to.AddItem(item)) { bag.RemoveItem(item); moved += stack; }
            }
            return moved;
        }

        /// <summary>Up to n of a prefab from a chest within reach into its bag, as far as fits.</summary>
        internal int Withdraw(Vector3 chestAt, string prefab, int n)
        {
            if (!IsOwner || Driven || n <= 0) return 0;
            Container chest = ChestAt(chestAt);
            if (chest == null || !Open(chest)) return 0;
            Inventory bag = Body.GetInventory(), from = chest.GetInventory();
            int moved = 0;
            foreach (ItemDrop.ItemData item in from.GetAllItems().ToList())
            {
                if (moved >= n) break;
                if (item.m_dropPrefab == null || Utils.GetPrefabName(item.m_dropPrefab) != prefab) continue;
                int take = System.Math.Min(item.m_stack, n - moved);
                ItemDrop.ItemData copy = item.Clone();
                copy.m_stack = take;
                if (!bag.CanAddItem(copy)) continue;
                if (bag.AddItem(copy)) { from.RemoveItem(item, take); moved += take; }
            }
            return moved;
        }

        // ---- looking around ----

        /// <summary>The nearest thing of a kind within a radius: a place, and an id for the script to keep. Null when there is none.</summary>
        internal object[] Nearest(string kind, float radius)
        {
            Vector3 here = transform.position;
            Component best = null; float bestD = float.MaxValue;
            void Consider(Component c) { if (c == null) return; float d = Vector3.Distance(here, c.transform.position); if (d < bestD) { bestD = d; best = c; } }
            if (kind == "creature" || kind == "player")
            {
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c == null || c.IsDead() || c.gameObject == gameObject || c is Player != (kind == "player") || c.IsTamed()) continue;
                    if (Vector3.Distance(here, c.transform.position) <= radius) Consider(c);
                }
            }
            else
            {
                foreach (Collider col in Physics.OverlapSphere(here, radius, ReachMask, QueryTriggerInteraction.Ignore))
                {
                    if (kind == "tree") Consider(col.GetComponentInParent<TreeBase>());
                    else if (kind == "rock") { Consider(col.GetComponentInParent<MineRock5>()); Consider(col.GetComponentInParent<MineRock>()); }
                    else if (kind == "pickup") Consider(col.GetComponentInParent<ItemDrop>());
                    else if (kind == "chest")
                    {
                        Container c = col.GetComponentInParent<Container>();
                        if (c != null && c.GetComponentInParent<Character>() == null && c.GetComponentInParent<Piece>() != null && c.GetComponentInParent<Incinerator>() == null) Consider(c);
                    }
                }
            }
            if (best == null) return null;
            Vector3 p = best.transform.position;
            return new object[] { best.GetInstanceID().ToString(), p.x, p.y, p.z };
        }
    }

    /// <summary>
    /// The one door scripts use to reach the Proxy (the Scripts mod finds this class by name and calls it through reflection, so the two
    /// mods need not be installed together). Every call is plain numbers and text. It picks the first terminal that has a running Proxy
    /// loaded; events (arrived, blocked, taken, released, died...) wait here until the Scripts mod collects them.
    /// </summary>
    public static class ScriptsBridge
    {
        private static readonly List<string> Events = new List<string>();

        internal static void Raise(string kind)
        {
            if (Events.Count < 50) Events.Add(kind);
        }

        public static string[] TakeEvents() { var all = Events.ToArray(); Events.Clear(); return all; }

        private static TerminalPiece _terminal;

        private static ProxyBody Proxy()
        {
            if (_terminal != null && _terminal.FindProxy() != null) return _terminal.FindProxy();
            foreach (TerminalPiece t in TerminalPiece.All) { ProxyBody p = t.FindProxy(); if (p != null) { _terminal = t; return p; } }
            return null;
        }

        public static float[] Pos() { ProxyBody p = Proxy(); return p == null ? null : new[] { p.transform.position.x, p.transform.position.y, p.transform.position.z }; }
        public static bool Busy() { ProxyBody p = Proxy(); return p == null || p.Driven || !p.IsOwner; }
        public static bool IsDead() { ProxyBody p = Proxy(); return p == null || p.Body.IsDead(); }
        public static float Hp() { ProxyBody p = Proxy(); return p == null ? 0f : p.Body.GetHealth(); }
        public static float MaxHp() { ProxyBody p = Proxy(); return p == null ? 0f : p.Body.GetMaxHealth(); }
        public static string Driver() { ProxyBody p = Proxy(); return p != null && p.Driven ? p.Driver : ""; }
        public static bool Arrived() { ProxyBody p = Proxy(); return p != null && p.Arrived; }

        public static bool WalkTo(float x, float z, bool run)
        {
            ProxyBody p = Proxy();
            if (p == null || _terminal == null) return false;
            if (Vector3.Distance(new Vector3(x, 0f, z), new Vector3(_terminal.transform.position.x, 0f, _terminal.transform.position.z)) > Mathf.Min(Plugin.Tether.Value, 120f)) return false;   // (beyond the tether)
            float y = ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(new Vector3(x, 0f, z), out float h) ? h : p.transform.position.y;
            return p.GoTo(new Vector3(x, y, z), run);
        }

        public static void Stop() { Proxy()?.StopGoal(); }
        public static void Home() { ProxyBody p = Proxy(); if (p != null && _terminal != null && _terminal.SpawnPoint != null) p.GoTo(_terminal.SpawnPoint.position, false); }
        public static void Say(string text) { Proxy()?.Speak(text); }
        public static bool Has(string prefab, int n) { ProxyBody p = Proxy(); return p != null && p.CountOf(prefab) >= n; }
        public static bool Equip(string slot, string prefab) { ProxyBody p = Proxy(); return p != null && p.Equip(slot, prefab); }
        public static bool Use(float x, float y, float z) { ProxyBody p = Proxy(); return p != null && p.UseAt(new Vector3(x, y, z)); }
        public static bool Attack(float x, float y, float z) { ProxyBody p = Proxy(); return p != null && p.SwingAt(new Vector3(x, y, z)); }
        public static void Look(float x, float y, float z) { Proxy()?.LookAt(new Vector3(x, y, z)); }
        public static object[] Nearest(string kind, float radius) { ProxyBody p = Proxy(); return p?.Nearest(kind, radius); }
        public static int Deposit(float x, float y, float z, string[] only) { ProxyBody p = Proxy(); return p == null ? 0 : p.Deposit(new Vector3(x, y, z), only); }
        public static int Withdraw(float x, float y, float z, string prefab, int n) { ProxyBody p = Proxy(); return p == null ? 0 : p.Withdraw(new Vector3(x, y, z), prefab, n); }
    }
}
