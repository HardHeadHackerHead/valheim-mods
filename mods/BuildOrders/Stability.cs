using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// An estimate of how stable the planned structure would be, using the same rules the game uses for real pieces (each material's
    /// strength, how much support is lost across a distance, ground counting as full support). The ghosts have no physics of their own,
    /// so this works from their outlines instead; it is a guide, not a promise. A piece the estimate says nothing supports would fall
    /// once built, which the game does on its own.
    /// </summary>
    public partial class Plugin
    {
        internal class Stab
        {
            public float Support, Max, Min;
            public bool Collapses => Support < Min;
            /// <summary>0 = about to fall, 1 = comfortable; -1 = fully solid.</summary>
            public float Value => Support >= Max ? -1f : Mathf.Clamp01((Support - Min) / Mathf.Max(0.01f, Max * 0.5f - Min));
            public int Percent => Mathf.RoundToInt(Mathf.Clamp01(Support / Max) * 100f);
        }

        private class Node
        {
            public Order Order;
            public Bounds Box;
            public Vector3 Com;
            public float Max, Min, HLoss, VLoss, Support;
            public bool Supports, Grounded;
            public readonly List<Node> Near = new List<Node>();
            public readonly List<Real> Real = new List<Real>();
        }

        /// <summary>A real piece already standing next to a ghost, with the support the game says it has.</summary>
        private struct Real { public Vector3 Com, Point; public float Support; }

        private readonly Dictionary<string, Stab> _stability = new Dictionary<string, Stab>();
        private readonly Dictionary<string, float[]> _materials = new Dictionary<string, float[]>(); // prefab -> max, min, horizontal loss, vertical loss, supports
        private bool _stabilityDirty = true, _stabilityShown;
        private float _nextStability;

        private static readonly System.Reflection.MethodInfo MaterialProps = AccessTools.Method(typeof(WearNTear), "GetMaterialProperties");
        private static readonly System.Reflection.MethodInfo ComOf = AccessTools.Method(typeof(WearNTear), "GetCOM");
        private static readonly System.Reflection.MethodInfo SupportOf = AccessTools.Method(typeof(WearNTear), "GetSupport");
        private readonly Collider[] _near = new Collider[48];

        private float[] MaterialOf(Order o, GameObject ghost)
        {
            if (_materials.TryGetValue(o.Prefab, out float[] cached)) return cached;
            float[] result = null;
            WearNTear wear = ghost.GetComponent<WearNTear>();
            if (wear != null && MaterialProps != null)
            {
                var args = new object[] { 0f, 0f, 0f, 0f };
                MaterialProps.Invoke(wear, args);
                result = new[] { (float)args[0], (float)args[1], (float)args[2], (float)args[3], wear.m_supports ? 1f : 0f };
            }
            _materials[o.Prefab] = result;
            return result;
        }

        /// <summary>Work out the support of every ghost near you (and tint them if the colours are showing).</summary>
        internal void ComputeStability(Player player)
        {
            _stability.Clear();
            var nodes = new List<Node>();
            Vector3 me = player.transform.position;
            foreach (Order o in _orders.Values)
            {
                if ((o.Pos - me).sqrMagnitude > 80f * 80f) continue;
                if (!_ghosts.TryGetValue(o.Id, out GameObject ghost) || ghost == null || !GhostBounds(o, out Bounds box)) continue;
                float[] m = MaterialOf(o, ghost);
                if (m == null) continue; // not a structural piece (a table, a torch...)
                nodes.Add(new Node
                {
                    Order = o, Box = box, Com = ghost.GetComponent<WearNTear>() != null ? (Vector3)ComOf.Invoke(ghost.GetComponent<WearNTear>(), null) : box.center,
                    Max = m[0], Min = m[1], HLoss = m[2], VLoss = m[3], Supports = m[4] > 0f,
                });
                if (nodes.Count >= 400) break;
            }

            int ground = LayerMask.GetMask("terrain", "Default", "static_solid", "Default_small");
            int pieces = LayerMask.GetMask("piece");

            foreach (Node n in nodes)
            {
                Vector3 reach = n.Box.extents + Vector3.one * 0.06f;
                n.Grounded = Physics.CheckBox(n.Box.center, reach, Quaternion.identity, ground, QueryTriggerInteraction.Ignore);
                n.Support = n.Grounded ? n.Max : 0f;

                // real pieces already touching it
                int count = Physics.OverlapBoxNonAlloc(n.Box.center, reach, _near, Quaternion.identity, pieces, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    WearNTear real = _near[i].GetComponentInParent<WearNTear>();
                    if (real == null || !real.m_supports || SupportOf == null || ComOf == null) continue;
                    n.Real.Add(new Real
                    {
                        Com = (Vector3)ComOf.Invoke(real, null),
                        Point = _near[i].bounds.ClosestPoint(n.Com),
                        Support = (float)SupportOf.Invoke(real, null),
                    });
                }
            }

            // which ghosts touch which
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    Bounds a = nodes[i].Box, b = nodes[j].Box;
                    a.Expand(0.12f);
                    if (!a.Intersects(b)) continue;
                    if (nodes[j].Supports) nodes[i].Near.Add(nodes[j]);
                    if (nodes[i].Supports) nodes[j].Near.Add(nodes[i]);
                }

            // let support flow outwards from the ground and from real pieces until nothing changes
            var points = new List<Vector3>();
            var values = new List<float>();
            for (int pass = 0; pass < 30; pass++)
            {
                bool changed = false;
                foreach (Node n in nodes)
                {
                    if (n.Grounded) continue;
                    float best = 0f;
                    points.Clear();
                    values.Clear();

                    void Consider(Vector3 com, Vector3 point, float s)
                    {
                        if (s <= 0f) return;
                        float dist = Vector3.Distance(n.Com, com) + 0.1f;
                        best = Mathf.Max(best, s - n.HLoss * dist * s);
                        if (point.y < n.Com.y + 0.05f)
                        {
                            Vector3 dir = (point - n.Com).normalized;
                            if (dir.y < 0f)
                            {
                                float t = Mathf.Acos(1f - Mathf.Abs(dir.y)) / (Mathf.PI / 2f);
                                best = Mathf.Max(best, s - Mathf.Lerp(n.HLoss, n.VLoss, t) * dist * s);
                            }
                            points.Add(point);
                            values.Add(s - n.VLoss * dist * s);
                        }
                    }

                    foreach (Node other in n.Near) Consider(other.Com, other.Box.ClosestPoint(n.Com), other.Support);
                    foreach (Real r in n.Real) Consider(r.Com, r.Point, r.Support);

                    // two supports on opposite sides hold a piece up better than either alone
                    for (int l = 0; l < points.Count - 1; l++)
                    {
                        Vector3 from = points[l] - n.Com; from.y = 0f;
                        for (int m = l + 1; m < points.Count; m++)
                        {
                            float average = (values[l] + values[m]) * 0.5f;
                            if (average <= best) continue;
                            Vector3 to = points[m] - n.Com; to.y = 0f;
                            if (Vector3.Angle(from, to) >= 100f) best = average;
                        }
                    }

                    best = Mathf.Min(best, n.Max);
                    if (best > n.Support + 0.01f) { n.Support = best; changed = true; }
                }
                if (!changed) break;
            }

            foreach (Node n in nodes) _stability[n.Order.Id] = new Stab { Support = n.Support, Max = n.Max, Min = n.Min };
            _stabilityDirty = false;
        }

        /// <summary>Called every frame: decide whether the colours are showing, and refresh the estimate now and then.</summary>
        private void UpdateStability(Player player)
        {
            if (Input.GetKeyDown(_stabilityKey.Value) && !TypingOrMenuOpen())
            {
                _stabilityToggle = !_stabilityToggle;
                player.Message(MessageHud.MessageType.TopLeft, _stabilityToggle ? "Stability colours shown on the ghosts" : "Stability colours hidden");
            }

            bool show = _stabilityToggle || (_stabilityInPlan.Value && PlanKeyHeld);
            if (show != _stabilityShown)
            {
                _stabilityShown = show;
                _stabilityDirty = true;
                if (!show) { _stability.Clear(); RetintAll(); }
            }
            if (!show && !_building) return;

            if (Time.unscaledTime >= _nextStability && (_stabilityDirty || Time.unscaledTime - _nextStability > 3f))
            {
                _nextStability = Time.unscaledTime + 1.5f;
                ComputeStability(player);
                RetintAll();
            }
        }

        private bool _stabilityToggle;

        /// <summary>The colour the game itself uses for a piece's support: blue when solid, then green down to red.</summary>
        private static Color StabilityColor(Stab s)
        {
            float v = s.Value;
            if (v < 0f) return new Color(0.6f, 0.8f, 1f);
            if (s.Collapses) return new Color(1f, 0.1f, 0.1f);
            Color c = Color.Lerp(new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), v);
            Color.RGBToHSV(c, out float h, out float sat, out float val);
            return Color.HSVToRGB(h, Mathf.Lerp(1f, 0.5f, v), Mathf.Lerp(1.2f, 0.9f, v));
        }

        internal Stab StabilityOf(string orderId) => _stability.TryGetValue(orderId, out Stab s) ? s : null;
    }
}
