using UnityEngine;

namespace Recycler
{
    /// <summary>
    /// The look of the Recycler and its Press, built from plain Unity shapes in flat colours. Every part is one row in a table
    /// (shape, position, size, rotation, colour). The chest's own mesh and colliders are replaced by these shapes and one box collider.
    /// Positions are metres from the middle of the base, y up.
    /// </summary>
    internal static class Model
    {
        internal struct Part
        {
            public string Name; public PrimitiveType Shape; public Vector3 Pos, Size, Rot; public Color Color; public bool Spin;
            public Part(string n, PrimitiveType s, Vector3 p, Vector3 sz, Vector3 r, Color c, bool spin = false)
            { Name = n; Shape = s; Pos = p; Size = sz; Rot = r; Color = c; Spin = spin; }
        }

        private static readonly Color Stone = new Color(0.45f, 0.45f, 0.47f);
        private static readonly Color Wood = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color Iron = new Color(0.22f, 0.22f, 0.25f);
        private static readonly Color Teal = new Color(0.25f, 0.75f, 0.8f);

        private static Part Cube(string n, float x, float y, float z, float sx, float sy, float sz, Color c, float rx = 0) =>
            new Part(n, PrimitiveType.Cube, new Vector3(x, y, z), new Vector3(sx, sy, sz), new Vector3(rx, 0, 0), c);

        internal static readonly Part[] Recycler =
        {
            Cube("base", 0, 0.12f, 0, 1.4f, 0.24f, 1.1f, Stone),
            Cube("body", 0, 0.56f, 0, 1.15f, 0.64f, 0.85f, Wood),
            Cube("band", 0, 0.62f, 0, 1.19f, 0.07f, 0.89f, Teal),
            Cube("postFL", -0.56f, 0.7f, -0.4f, 0.1f, 0.9f, 0.1f, Iron),
            Cube("postFR", 0.56f, 0.7f, -0.4f, 0.1f, 0.9f, 0.1f, Iron),
            Cube("postBL", -0.56f, 0.7f, 0.4f, 0.1f, 0.9f, 0.1f, Iron),
            Cube("postBR", 0.56f, 0.7f, 0.4f, 0.1f, 0.9f, 0.1f, Iron),
            Cube("frontLip", 0, 1.0f, -0.4f, 1.15f, 0.1f, 0.08f, Iron),
            Cube("backLip", 0, 1.0f, 0.4f, 1.15f, 0.1f, 0.08f, Iron),
            new Part("rollerL", PrimitiveType.Cylinder, new Vector3(-0.2f, 1.04f, 0), new Vector3(0.34f, 0.4f, 0.34f), new Vector3(90, 0, 0), Teal, true),
            new Part("rollerR", PrimitiveType.Cylinder, new Vector3(0.2f, 1.04f, 0), new Vector3(0.34f, 0.4f, 0.34f), new Vector3(90, 0, 0), Iron, true),
            Cube("chute", 0, 0.22f, -0.66f, 0.6f, 0.08f, 0.35f, Iron, -20),
        };
        internal static readonly Vector3 RecyclerHitCenter = new Vector3(0, 0.6f, 0), RecyclerHitSize = new Vector3(1.4f, 1.2f, 1.1f);

        internal static readonly Part[] Press =
        {
            Cube("base", 0, 0.1f, 0, 0.9f, 0.2f, 0.9f, Stone),
            Cube("plate", 0, 0.26f, 0, 0.6f, 0.1f, 0.6f, Iron),
            Cube("postL", -0.32f, 0.65f, 0, 0.12f, 0.9f, 0.12f, Iron),
            Cube("postR", 0.32f, 0.65f, 0, 0.12f, 0.9f, 0.12f, Iron),
            Cube("beam", 0, 1.1f, 0, 0.8f, 0.14f, 0.2f, Iron),
            new Part("plunger", PrimitiveType.Cylinder, new Vector3(0, 0.67f, 0), new Vector3(0.2f, 0.35f, 0.2f), Vector3.zero, Teal),
        };
        internal static readonly Vector3 PressHitCenter = new Vector3(0, 0.6f, 0), PressHitSize = new Vector3(0.9f, 1.2f, 0.9f);

        internal static void Build(GameObject prefab, Material baseMaterial, Part[] parts, Vector3 hitCenter, Vector3 hitSize)
        {
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = prefab.layer;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // hide the chest
            foreach (Collider c in prefab.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c); // and its hitbox

            var hit = new GameObject("Hitbox") { layer = layer };
            hit.transform.SetParent(prefab.transform, false);
            var box = hit.AddComponent<BoxCollider>();
            box.center = hitCenter; box.size = hitSize;

            var root = new GameObject("Model") { layer = prefab.layer };
            root.transform.SetParent(prefab.transform, false);
            foreach (Part part in parts)
            {
                GameObject go = GameObject.CreatePrimitive(part.Shape);
                go.name = part.Name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.layer = prefab.layer;
                Transform t = go.transform;
                t.SetParent(root.transform, false);
                t.localPosition = part.Pos;
                t.localScale = part.Size;
                t.localRotation = Quaternion.Euler(part.Rot);

                var mat = new Material(baseMaterial) { color = part.Color };
                if (mat.HasProperty("_MainTex")) mat.mainTexture = null; // flat colour, not wood grain
                go.GetComponent<Renderer>().sharedMaterial = mat;
                if (part.Spin) go.AddComponent<Spinner>();
            }
        }
    }

    /// <summary>Turns a roller around its own axis while its Recycler is working (everyone nearby sees it, via the Recycler's synced timestamp).</summary>
    internal class Spinner : MonoBehaviour
    {
        private RecyclerStation _station;

        private void Update()
        {
            if (_station == null) { _station = GetComponentInParent<RecyclerStation>(); if (_station == null) return; }
            if (!_station.Working) return;
            transform.Rotate(0f, (name == "rollerL" ? 1f : -1f) * 180f * Time.deltaTime, 0f, Space.Self);
        }
    }
}
