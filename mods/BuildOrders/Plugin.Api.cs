using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildOrders
{
    public partial class Plugin
    {
        // Only game/BCL types cross this boundary, so callers can reconnect after ScriptEngine reloads.
        public const int PlanningApiVersion = 1;
        public const int MaximumApiPieces = 256;
        private const string AddonPlanPrefix = "Blueprint: Addon ";
        private static bool IsAddonGhostPlan(string key) => key != null && key.StartsWith(AddonPlanPrefix, StringComparison.Ordinal);

        /// <summary>Create ordinary shared ghosts at absolute world poses. No terrain edits or real pieces are created.
        /// Call on the Unity main thread with the local player. Validate the entire bounded batch before adding anything.
        /// The returned world-specific key identifies this submission, including when its title is reused.</summary>
        public bool TryCreateGhostPlan(Player player, string title, string[] prefabs, Vector3[] positions,
            Quaternion[] rotations, out string planKey, out string error)
        {
            planKey = null;
            if (!ApiReady(player, out error)) return false;
            if (string.IsNullOrWhiteSpace(title) || title.Length > 80 || title.IndexOfAny(new[] { '|', '\r', '\n' }) >= 0)
            { error = "Invalid plan title."; return false; }
            if (prefabs == null || positions == null || rotations == null || prefabs.Length < 1 || prefabs.Length > MaximumApiPieces ||
                positions.Length != prefabs.Length || rotations.Length != prefabs.Length)
            { error = "Expected 1–256 matching prefab, position, and rotation entries."; return false; }

            var turns = new Quaternion[rotations.Length];
            HashSet<string> buildable = Buildable();
            for (int i = 0; i < prefabs.Length; i++)
            {
                string name = prefabs[i]; Vector3 pos = positions[i]; Quaternion turn = rotations[i];
                if (string.IsNullOrEmpty(name) || name.Length > 128 || name.IndexOfAny(new[] { '|', '\r', '\n' }) >= 0 ||
                    !ApiFinite(pos.x) || !ApiFinite(pos.y) || !ApiFinite(pos.z) || (pos - player.transform.position).sqrMagnitude > 80f * 80f)
                { error = "Invalid or out-of-reach plan pose."; return false; }
                double norm = (double)turn.x * turn.x + (double)turn.y * turn.y + (double)turn.z * turn.z + (double)turn.w * turn.w;
                if (!ApiFinite(turn.x) || !ApiFinite(turn.y) || !ApiFinite(turn.z) || !ApiFinite(turn.w) || norm < 0.0001 || norm > 10000)
                { error = "Invalid plan rotation."; return false; }
                float scale = (float)(1.0 / Math.Sqrt(norm));
                turns[i] = new Quaternion(turn.x * scale, turn.y * scale, turn.z * scale, turn.w * scale);
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null || name == BridgeToolPrefab || !buildable.Contains(name) ||
                    prefab.GetComponentInChildren<TerrainOp>(true) != null || prefab.GetComponentInChildren<TerrainModifier>(true) != null)
                { error = "The plan contains an unavailable or non-building piece."; return false; }
                if (!player.IsRecipeKnown(piece.m_name)) { error = "Unlock the selected building piece first."; return false; }
                if (!PrivateArea.CheckAccess(pos, 0f, false, false) || Location.IsInsideNoBuildLocation(pos))
                { error = "The plan includes protected ground."; return false; }
            }

            // Never reuse blueprint names: stale terrain snapshots belong to earlier plans.
            string key = AddonPlanPrefix + title.Trim() + " [" + System.Guid.NewGuid().ToString("N") + "]";
            var orders = new List<Order>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (_orders.Values.Any(o => o.Prefab == prefabs[i] && (o.Pos - positions[i]).sqrMagnitude < 0.01f && Quaternion.Angle(o.Rot, turns[i]) < 5f) ||
                    orders.Any(o => o.Prefab == prefabs[i] && (o.Pos - positions[i]).sqrMagnitude < 0.01f && Quaternion.Angle(o.Rot, turns[i]) < 5f)) continue;
                orders.Add(new Order { Id = System.Guid.NewGuid().ToString("N"), Prefab = prefabs[i], Pos = positions[i], Rot = turns[i], By = key });
            }
            if (orders.Count == 0) { error = "Those pieces are already planned."; return false; }
            foreach (Order order in orders) _orders.Add(order.Id, order);
            _stabilityDirty = true;
            Save();
            foreach (Order order in orders) Send("A|" + Encode(order));
            RecordPlan(key);
            planKey = key; error = null;
            return true;
        }

        /// <summary>Remove the remaining ghosts of an API-created plan. Real pieces and terrain are left intact.
        /// Returns success with zero removed for an already completed/removed plan. Does not erase built-piece records.</summary>
        public bool TryRemoveGhostPlan(Player player, string planKey, out int removed, out string error)
        {
            removed = 0;
            if (!ApiReady(player, out error)) return false;
            if (string.IsNullOrEmpty(planKey) || planKey.Length > 160 || !IsAddonGhostPlan(planKey))
            { error = "Not an add-on ghost plan."; return false; }
            List<Order> orders = _orders.Values.Where(o => o.By == planKey).Take(MaximumApiPieces + 1).ToList();
            if (orders.Count > MaximumApiPieces) { error = "Too many ghosts in this plan."; return false; }
            foreach (Order order in orders)
                if ((order.Pos - player.transform.position).sqrMagnitude > 80f * 80f || !PrivateArea.CheckAccess(order.Pos, 0f, false, false))
                { error = "The plan is out of reach or protected."; return false; }
            foreach (Order order in orders) RemoveOrder(order.Id, broadcast: true, save: false);
            if (orders.Count > 0) SaveOrders();
            removed = orders.Count; error = null;
            return true;
        }

        private bool ApiReady(Player player, out string error)
        {
            error = "BuildOrders is disabled or the local world is not ready.";
            if (Instance != this || !_enabled.Value || player == null || player != Player.m_localPlayer || player.IsDead() ||
                ZNetScene.instance == null || ObjectDB.instance == null) return false;
            EnsureWorldLoaded();
            if (!WorldKnown) return false;
            error = null; return true;
        }
        private static bool ApiFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
