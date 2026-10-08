using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// How stable the planned structure would be, worked out as the game itself does it for real pieces (WearNTear.UpdateSupport): each
    /// piece's own solid colliders, turned with it, grown by 0.15 m into the boxes it takes support through; the ground (or any solid that
    /// is not a piece) inside those boxes gives full support; from a touching piece, its support less the material's loss over the distance
    /// between their centres of mass (sideways loss, or nearer the vertical loss for a support below), two supports on opposite sides
    /// averaged; under the material's minimum it breaks and what rested on it is worked out again. Build all only builds what stands this way,
    /// so nothing is built over a gap where a piece below is still missing (out of materials, or of a station's reach).
    /// </summary>
    public partial class Plugin
    {
        internal class Stab
        {
            public float Support, Max, Min;
            /// <summary>The ghosts its support comes from (none: the ground, or a piece already built). Build those first.</summary>
            public List<string> From = new List<string>();
            public bool Collapses => Support < Min;
            /// <summary>0 = about to fall, 1 = comfortable; -1 = fully solid.</summary>
            public float Value => Support >= Max ? -1f : Mathf.Clamp01((Support - Min) / Mathf.Max(0.01f, Max * 0.5f - Min));
            public int Percent => Mathf.RoundToInt(Mathf.Clamp01(Support / Max) * 100f);
        }

        private class Node
        {
            public Order Order;
            public Bounds Box;                                    // all its collider boxes together (for a quick first test)
            public Vector3 Com, Pos;
            public float Max, Min, HLoss, VLoss, Support;
            public bool Supports, Grounded, Broken;
            public List<Node> From = new List<Node>();            // where its support comes from (empty: the ground or real pieces)
            public List<Obb> Cols, Reach;                         // its solid colliders, and those grown by 0.15 m (where it takes support)
            public readonly List<KeyValuePair<Node, Obb>> Near = new List<KeyValuePair<Node, Obb>>();   // a touching piece, and its collider
            public readonly List<Real> Real = new List<Real>();
        }

        /// <summary>An oriented box: centre, rotation, half sizes.</summary>
        internal struct Obb
        {
            public Vector3 C, H;
            public Quaternion R;
            public Bounds Aabb()
            {
                Vector3 ax = R * new Vector3(H.x, 0f, 0f), ay = R * new Vector3(0f, H.y, 0f), az = R * new Vector3(0f, 0f, H.z);
                Vector3 e = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
                return new Bounds(C, e * 2f);
            }
            public Vector3 Closest(Vector3 p)
            {
                Vector3 d = Quaternion.Inverse(R) * (p - C);
                d = new Vector3(Mathf.Clamp(d.x, -H.x, H.x), Mathf.Clamp(d.y, -H.y, H.y), Mathf.Clamp(d.z, -H.z, H.z));
                return C + R * d;
            }
            public static bool Overlap(Obb a, Obb b)
            {
                Vector3[] A = { a.R * Vector3.right, a.R * Vector3.up, a.R * Vector3.forward };
                Vector3[] B = { b.R * Vector3.right, b.R * Vector3.up, b.R * Vector3.forward };
                Vector3 t = b.C - a.C;
                bool Sep(Vector3 axis)
                {
                    if (axis.sqrMagnitude < 1e-8f) return false;
                    float ra = a.H.x * Mathf.Abs(Vector3.Dot(A[0], axis)) + a.H.y * Mathf.Abs(Vector3.Dot(A[1], axis)) + a.H.z * Mathf.Abs(Vector3.Dot(A[2], axis));
                    float rb = b.H.x * Mathf.Abs(Vector3.Dot(B[0], axis)) + b.H.y * Mathf.Abs(Vector3.Dot(B[1], axis)) + b.H.z * Mathf.Abs(Vector3.Dot(B[2], axis));
                    return Mathf.Abs(Vector3.Dot(t, axis)) > ra + rb + 1e-4f;
                }
                for (int i = 0; i < 3; i++) { if (Sep(A[i]) || Sep(B[i])) return false; }
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) if (Sep(Vector3.Cross(A[i], B[j]))) return false;
                return true;
            }
        }

        /// <summary>A prefab's solid colliders in its own frame, as the game's support check takes them (boxes turned; other shapes by bounds).</summary>
        private readonly Dictionary<string, List<KeyValuePair<Obb, bool>>> _colliderShapes = new Dictionary<string, List<KeyValuePair<Obb, bool>>>();

        private List<KeyValuePair<Obb, bool>> ShapesOf(string prefabName)
        {
            if (_colliderShapes.TryGetValue(prefabName, out var cached)) return cached;
            var list = new List<KeyValuePair<Obb, bool>>();
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            if (prefab != null)
            {
                Transform root = prefab.transform;
                foreach (Collider col in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (col.isTrigger || col.attachedRigidbody != null) continue;
                    Transform t = col.transform;
                    Matrix4x4 m = root.worldToLocalMatrix * t.localToWorldMatrix;
                    Quaternion r = Quaternion.Inverse(root.rotation) * t.rotation;
                    Vector3 scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                    if (col is BoxCollider bc)
                        list.Add(new KeyValuePair<Obb, bool>(new Obb { C = m.MultiplyPoint3x4(bc.center), R = r, H = Vector3.Scale(scale, bc.size) * 0.5f }, true));
                    else
                    {
                        Bounds lb = col is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.bounds
                            : col is CapsuleCollider cc ? new Bounds(cc.center, cc.direction == 0 ? new Vector3(cc.height, cc.radius * 2f, cc.radius * 2f) : cc.direction == 1 ? new Vector3(cc.radius * 2f, cc.height, cc.radius * 2f) : new Vector3(cc.radius * 2f, cc.radius * 2f, cc.height))
                            : col is SphereCollider sc ? new Bounds(sc.center, Vector3.one * sc.radius * 2f) : new Bounds(Vector3.zero, Vector3.zero);
                        list.Add(new KeyValuePair<Obb, bool>(new Obb { C = m.MultiplyPoint3x4(lb.center), R = r, H = Vector3.Scale(scale, lb.size) * 0.5f }, false));
                    }
                }
            }
            _colliderShapes[prefabName] = list;
            return list;
        }

        /// <summary>A real piece already standing next to a ghost, with the support the game says it has.</summary>
        private struct Real { public Vector3 Com, Pos, Point; public float Support; }

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
        /// <param name="only">if given, only these ghosts count (the rest are treated as not built): "would this part stand on its own?"</param>
        internal void ComputeStability(Player player, HashSet<string> only = null)
        {
            _stability.Clear();
            var nodes = new List<Node>();
            Vector3 me = player.transform.position;
            foreach (Order o in _orders.Values)
            {
                if ((o.Pos - me).sqrMagnitude > 80f * 80f) continue;
                if (only != null && !only.Contains(o.Id)) continue;
                if (!_ghosts.TryGetValue(o.Id, out GameObject ghost) || ghost == null) continue;
                float[] m = MaterialOf(o, ghost);
                if (m == null) continue; // not a structural piece (a table, a torch...)
                var shapes = ShapesOf(o.Prefab);
                if (shapes.Count == 0) continue;
                var cols = new List<Obb>(shapes.Count);
                var reach = new List<Obb>(shapes.Count);
                Bounds all = default;
                foreach (var kv in shapes)
                {
                    Obb local = kv.Key;
                    var w = new Obb { C = o.Pos + o.Rot * local.C, R = o.Rot * local.R, H = local.H };
                    cols.Add(w);
                    Obb grown = kv.Value ? new Obb { C = w.C, R = w.R, H = w.H + Vector3.one * 0.15f }
                        : new Obb { C = w.Aabb().center, R = Quaternion.identity, H = w.Aabb().extents + Vector3.one * 0.15f }; // (the game: world bounds)
                    reach.Add(grown);
                    Bounds gb = grown.Aabb();
                    if (all.size == Vector3.zero) all = gb; else all.Encapsulate(gb);
                }
                WearNTear wnt = ghost.GetComponent<WearNTear>();
                nodes.Add(new Node
                {
                    Order = o, Box = all, Pos = o.Pos, Cols = cols, Reach = reach,
                    Com = wnt != null && ComOf != null ? (Vector3)ComOf.Invoke(wnt, null) : o.Pos,
                    Max = m[0], Min = m[1], HLoss = m[2], VLoss = m[3], Supports = m[4] > 0f,
                });
            }

            int ground = LayerMask.GetMask("terrain", "Default", "static_solid", "Default_small");
            int pieces = LayerMask.GetMask("piece");
            foreach (Node n in nodes)
            {
                foreach (Obb r in n.Reach)
                {
                    if (!n.Grounded && Physics.CheckBox(r.C, r.H, r.R, ground, QueryTriggerInteraction.Ignore)) n.Grounded = true;
                    // real pieces already touching it
                    int count = Physics.OverlapBoxNonAlloc(r.C, r.H, _near, r.R, pieces, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        WearNTear real = _near[i].GetComponentInParent<WearNTear>();
                        if (real == null || !real.m_supports || SupportOf == null || ComOf == null) continue;
                        n.Real.Add(new Real { Com = (Vector3)ComOf.Invoke(real, null), Pos = real.transform.position, Point = _near[i].ClosestPoint(n.Com), Support = (float)SupportOf.Invoke(real, null) });
                    }
                }
                n.Support = n.Grounded ? n.Max : 0f;
            }

            // which ghosts touch which: where a piece's grown boxes reach into another's colliders
            for (int i = 0; i < nodes.Count; i++)
                for (int j = 0; j < nodes.Count; j++)
                {
                    if (i == j || !nodes[j].Supports) continue;
                    Node a = nodes[i], b = nodes[j];
                    if (!a.Box.Intersects(b.Box)) continue;
                    foreach (Obb c in b.Cols)
                    {
                        bool touches = false;
                        foreach (Obb r in a.Reach) if (Obb.Overlap(r, c)) { touches = true; break; }
                        if (touches) a.Near.Add(new KeyValuePair<Node, Obb>(b, c));
                    }
                }

            // let support flow outwards from the ground and from real pieces; what falls below its minimum breaks, and the rest is worked out
            // again without it, until nothing more breaks
            var points = new List<Vector3>();
            var values = new List<float>();
            var pointFrom = new List<Node>();
            for (int cascade = 0; cascade < 60; cascade++)
            {
                for (int pass = 0; pass < 60; pass++)
                {
                    bool changed = false;
                    foreach (Node n in nodes)
                    {
                        if (n.Grounded || n.Broken) continue;
                        float best = 0f;
                        points.Clear();
                        values.Clear();
                        pointFrom.Clear();
                        var bestFrom = new List<Node>();

                        void Take(float v, Node from)
                        {
                            if (v <= best) return;
                            best = v;
                            bestFrom.Clear();
                            if (from != null) bestFrom.Add(from);
                        }

                        void Consider(Vector3 com, Vector3 pos, Vector3 point, float s, Node from)
                        {
                            if (s <= 0f) return;
                            float dist = Mathf.Min(Vector3.Distance(n.Com, com), Vector3.Distance(n.Com, pos)) + 0.1f;
                            Take(s - n.HLoss * dist * s, from);
                            if (point.y < n.Com.y + 0.05f)
                            {
                                Vector3 dir = (point - n.Com).normalized;
                                if (dir.y < 0f)
                                {
                                    float t = Mathf.Acos(1f - Mathf.Abs(dir.y)) / (Mathf.PI / 2f);
                                    Take(s - Mathf.Lerp(n.HLoss, n.VLoss, t) * dist * s, from);
                                }
                                points.Add(point);
                                values.Add(s - n.VLoss * dist * s);
                                pointFrom.Add(from);
                            }
                        }

                        foreach (var kv in n.Near) if (!kv.Key.Broken) Consider(kv.Key.Com, kv.Key.Pos, kv.Value.Closest(n.Com), kv.Key.Support, kv.Key);
                        foreach (Real r in n.Real) Consider(r.Com, r.Pos, r.Point, r.Support, null);

                        // two supports on opposite sides hold a piece up better than either alone
                        for (int l = 0; l < points.Count - 1; l++)
                        {
                            Vector3 from = points[l] - n.Com; from.y = 0f;
                            for (int k = l + 1; k < points.Count; k++)
                            {
                                float average = (values[l] + values[k]) * 0.5f;
                                if (average <= best) continue;
                                Vector3 to = points[k] - n.Com; to.y = 0f;
                                if (Vector3.Angle(from, to) >= 100f)
                                {
                                    best = average;
                                    bestFrom.Clear();
                                    if (pointFrom[l] != null) bestFrom.Add(pointFrom[l]);
                                    if (pointFrom[k] != null && pointFrom[k] != pointFrom[l]) bestFrom.Add(pointFrom[k]);
                                }
                            }
                        }

                        best = Mathf.Min(best, n.Max);
                        if (Mathf.Abs(best - n.Support) > 0.01f) { n.Support = best; n.From = bestFrom; changed = true; }
                    }
                    if (!changed) break;
                }
                bool broke = false;
                foreach (Node n in nodes) if (!n.Broken && !n.Grounded && n.Support < n.Min) { n.Broken = true; n.Support = 0f; broke = true; }
                if (!broke) break;
            }

            foreach (Node n in nodes)
                _stability[n.Order.Id] = new Stab { Support = n.Support, Max = n.Max, Min = n.Min, From = n.Grounded ? new List<string>() : n.From.Select(f => f.Order.Id).ToList() };
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
