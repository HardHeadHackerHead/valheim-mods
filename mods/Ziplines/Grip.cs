using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// What you hold on to: your own axe, hooked over the rope, held with both hands (the ride needs an axe, unless the setting says
    /// otherwise), or else a wooden triangle on a pulley. It is drawn between the rope and your two hands, following them, so it is where your
    /// hands are whatever the rider's size.
    /// </summary>
    internal static class Grip
    {
        private static GameObject _root;
        private static Transform _axe, _bar, _left, _right, _block, _wheel;

        /// <summary>How far below the rope the hands are on the handle.</summary>
        public const float HandleDrop = 0.42f;

        public static bool IsAxe => _axe != null;
        private static Material _wood, _iron;

        /// <summary>The axe the rider would hook on the rope: the one they hold, else the best they carry.</summary>
        public static ItemDrop.ItemData FindAxe(Player player) =>
            player.GetInventory().GetAllItems()
                  .Where(i => i.m_dropPrefab != null && i.m_shared.m_skillType == Skills.SkillType.Axes && i.m_shared.m_damages.m_chop > 0f)
                  .OrderByDescending(i => player.IsItemEquiped(i)).ThenByDescending(i => i.m_shared.m_damages.m_chop).FirstOrDefault();

        public static void Begin(Player player, ItemDrop.ItemData axe)
        {
            End();
            MakeMaterials();
            _root = new GameObject("ZipGrip");
            if (axe != null && BuildAxe(axe)) return;
            // a wooden triangle: a block on the rope, two straps down to a bar
            _block = Prim(PrimitiveType.Cube, "Block", _wood); _wheel = Prim(PrimitiveType.Cylinder, "Wheel", _iron);
            _left = Prim(PrimitiveType.Cylinder, "StrapL", _iron); _right = Prim(PrimitiveType.Cylinder, "StrapR", _iron);
            _bar = Prim(PrimitiveType.Cylinder, "Bar", _wood);
        }

        public static void End()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null; _axe = _bar = _left = _right = _block = _wheel = null;
        }

        /// <summary>Every frame, after the pose: the handle between the rope and the hands.</summary>
        public static void Update(Transform carriage, Vector3 rope, Vector3 hands)
        {
            if (_root == null) return;
            Vector3 up = Vector3.up, forward = Vector3.ProjectOnPlane(carriage.forward, up).normalized, right = Vector3.Cross(up, forward);

            if (_axe != null)
            {
                _axe.position = hands;   // (the model's own origin is the place on its handle that is held)
                _axe.rotation = Quaternion.LookRotation(forward, up);
                return;
            }
            // the triangle: the bar across the hands, the straps up to the block on the rope
            Vector3 barMid = hands + up * 0.04f;
            Stretch(_bar, barMid - right * 0.26f, barMid + right * 0.26f, 0.022f);
            Vector3 apex = rope - up * 0.07f;
            Stretch(_left, apex, barMid - right * 0.26f, 0.008f);
            Stretch(_right, apex, barMid + right * 0.26f, 0.008f);
            _block.position = rope - up * 0.04f; _block.rotation = Quaternion.LookRotation(forward, up); _block.localScale = new Vector3(0.09f, 0.09f, 0.16f);
            _wheel.position = rope + up * 0.045f; _wheel.rotation = Quaternion.LookRotation(right, up) * Quaternion.Euler(0f, 0f, 90f); _wheel.localScale = new Vector3(0.14f, 0.012f, 0.14f);
        }

        // ---- the pieces ----------------------------------------------------------------------------------------------------

        private static Transform Prim(PrimitiveType shape, string name, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.transform.SetParent(_root.transform, false);
            return go.transform;
        }

        /// <summary>A thin cylinder from one point to another.</summary>
        private static void Stretch(Transform t, Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            t.position = (a + b) * 0.5f;
            t.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            t.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        }

        private static void MakeMaterials()
        {
            if (_wood != null && _iron != null) return;
            Material basis = null;
            GameObject wood = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("Wood") : null;
            if (wood != null) foreach (Renderer r in wood.GetComponentsInChildren<Renderer>(true)) basis = basis ?? r.sharedMaterials.FirstOrDefault(m => m != null && m.name == "wood_item");
            if (basis == null)
            {
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (shader == null) return; // (no material to copy: the handle keeps Unity's default look)
                basis = new Material(shader);
            }
            _wood = Plain(basis, new Color(0.52f, 0.35f, 0.2f), 0.05f);
            _iron = Plain(basis, new Color(0.17f, 0.17f, 0.18f), 0.5f);
        }

        private static Material Plain(Material basis, Color color, float metal)
        {
            var m = new Material(basis) { color = color };
            if (m.HasProperty("_MainTex")) m.mainTexture = null;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.2f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        // ---- the axe -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A copy of the axe's own model, turned so its handle is upright with the head on top and the blade facing the way you ride. Which way
        /// the model's handle runs, and which end is the head (the end that is wider), is worked out from its shape.
        /// </summary>
        private static bool BuildAxe(ItemDrop.ItemData axe)
        {
            GameObject prefab = axe.m_dropPrefab;
            Renderer[] parts = prefab.GetComponentInChildren<LODGroup>() is LODGroup lod && lod.GetLODs().Length > 0 ? lod.GetLODs()[0].renderers : prefab.GetComponentsInChildren<Renderer>();
            var meshes = new List<(MeshFilter Filter, MeshRenderer Renderer, Matrix4x4 Frame)>();
            var points = new List<Vector3>();
            foreach (Renderer r in parts)
            {
                MeshFilter mf = r != null ? r.GetComponent<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null || !(r is MeshRenderer mr) || r is ParticleSystemRenderer) continue;
                Matrix4x4 frame = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                meshes.Add((mf, mr, frame));
                Vector3[] vertices = mf.sharedMesh.vertices;
                int step = Mathf.Max(1, vertices.Length / 800);
                for (int i = 0; i < vertices.Length; i += step) points.Add(frame.MultiplyPoint3x4(vertices[i]));
            }
            if (meshes.Count == 0 || points.Count < 4) return false;

            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) bounds.Encapsulate(p);
            int along = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
            Vector3 axis = along == 0 ? Vector3.right : along == 1 ? Vector3.up : Vector3.forward;
            float low = Vector3.Dot(bounds.min, axis), high = Vector3.Dot(bounds.max, axis), length = Mathf.Max(0.05f, high - low);
            Vector3 u = along == 0 ? Vector3.up : Vector3.right, v = along == 2 ? Vector3.up : Vector3.forward;
            if (along == 1) { u = Vector3.right; v = Vector3.forward; }

            // how wide is each end across the handle: the head is the wide one
            float Spread(bool top, Vector3 across)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vector3 p in points)
                {
                    float t = (Vector3.Dot(p, axis) - low) / length;
                    if (top ? t < 0.78f : t > 0.22f) continue;
                    float c = Vector3.Dot(p, across);
                    lo = Mathf.Min(lo, c); hi = Mathf.Max(hi, c);
                }
                return hi > lo ? hi - lo : 0f;
            }
            float topWide = Mathf.Max(Spread(true, u), Spread(true, v)), bottomWide = Mathf.Max(Spread(false, u), Spread(false, v));
            Vector3 upAxis = topWide >= bottomWide ? axis : -axis;
            bool topIsHead = topWide >= bottomWide;
            Vector3 bladeAcross = Spread(topIsHead, u) >= Spread(topIsHead, v) ? u : v;   // (the way the head is widest)

            Vector3 across = Vector3.ProjectOnPlane(bladeAcross, upAxis);
            if (across.sqrMagnitude < 0.01f) across = Vector3.ProjectOnPlane(u, upAxis);
            Quaternion basis = Quaternion.LookRotation(across.normalized, upAxis);
            Quaternion toHolder = Quaternion.Inverse(basis);
            float scale = Mathf.Min(1f, 0.95f / length);
            // the model's origin is where it is held: a little over half way down from the head, on the handle's own line
            float fromHead = 0.55f, tGrip = topIsHead ? 1f - fromHead : fromHead;
            Vector3 sum = Vector3.zero; int taken = 0;
            foreach (Vector3 p in points)
            {
                float t = (Vector3.Dot(p, axis) - low) / length;
                if (Mathf.Abs(t - tGrip) > 0.07f) continue;
                sum += p; taken++;
            }
            Vector3 pivot = taken > 0 ? sum / taken : bounds.center;

            var holder = new GameObject("Axe");
            holder.transform.SetParent(_root.transform, false);
            var model = new GameObject("Model");
            model.transform.SetParent(holder.transform, false);
            model.transform.localRotation = toHolder;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = -(toHolder * pivot) * scale;
            foreach (var m in meshes)
            {
                var part = new GameObject(m.Filter.name);
                part.transform.SetParent(model.transform, false);
                part.transform.localPosition = m.Frame.GetColumn(3);
                part.transform.localRotation = m.Frame.rotation;
                part.transform.localScale = m.Frame.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = m.Filter.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = m.Renderer.sharedMaterials;
            }
            _axe = holder.transform;
            return true;
        }
    }
}
