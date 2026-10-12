using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// What the gun models are made of: plain shapes put together by a Builder (barrel along +Z, up +Y, the grip hand at the origin), painted
    /// with the game's own material in the colours below. Each gun's parts are in Models.cs.
    /// </summary>
    internal static class Model
    {
        internal static readonly Color Steel = new Color(0.17f, 0.18f, 0.20f), Black = new Color(0.07f, 0.07f, 0.08f), Poly = new Color(0.11f, 0.11f, 0.12f),
                                       Wood = new Color(0.36f, 0.22f, 0.11f), DarkWood = new Color(0.24f, 0.14f, 0.07f), Brass = new Color(0.66f, 0.50f, 0.20f),
                                       Nickel = new Color(0.62f, 0.64f, 0.67f), Blued = new Color(0.09f, 0.11f, 0.16f), Olive = new Color(0.20f, 0.24f, 0.16f),
                                       Lens = new Color(0.12f, 0.28f, 0.42f), Red = new Color(0.62f, 0.10f, 0.08f);

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static readonly List<Material> Made = new List<Material>();

        /// <summary>Destroys the materials the models were painted with (the models are destroyed with their holder).</summary>
        internal static void Clear()
        {
            foreach (Material m in Made) if (m != null) Object.Destroy(m);
            Made.Clear();
            Cache.Clear();
        }

        private static Material Shared(Material baseMaterial, Renderer r, Color color, float metal, float gloss)
        {
            string key = $"{color.r:F3}/{color.g:F3}/{color.b:F3}/{metal:F2}/{gloss:F2}";
            if (Cache.TryGetValue(key, out Material m) && m != null) return m;
            m = baseMaterial != null ? new Material(baseMaterial) : new Material(r.sharedMaterial);
            m.mainTexture = null;
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            Cache[key] = m;
            Made.Add(m);
            return m;
        }

        /// <summary>Puts the model together: shapes with a position (in design coordinates, moved by the shift so the grip lands on the origin), a size and a colour.</summary>
        internal sealed class Builder
        {
            internal readonly Transform Root;
            private readonly Material _base;
            private readonly Builder _top;
            private readonly Vector3 _shift;          // top level only: moves every part
            private readonly Vector3 _originInModel;  // where this group sits in the model
            private Vector3 _min = Vector3.positiveInfinity, _max = Vector3.negativeInfinity;

            internal Builder(Transform root, Material baseMaterial, Vector3 shift)
            {
                Root = root; _base = baseMaterial; _top = this; _shift = shift;
            }

            private Builder(Transform root, Builder parent, Vector3 originInModel)
            {
                Root = root; _base = parent._base; _top = parent._top; _originInModel = originInModel;
            }

            internal Bounds Bounds => new Bounds((_min + _max) * 0.5f, _max - _min);

            private Vector3 At(Vector3 p) => _top == this ? p + _shift : p;

            /// <summary>A part that moves or turns as one (a pump, a lever, a bolt, the barrels): at, in the design coordinates.</summary>
            internal Builder Group(string name, Vector3 at)
            {
                var go = new GameObject(name) { layer = Root.gameObject.layer };
                go.transform.SetParent(Root, false);
                go.transform.localPosition = At(at);
                return new Builder(go.transform, this, _originInModel + At(at));
            }

            /// <summary>An empty marking a place on the model (the muzzle, where brass comes out).</summary>
            internal void Point(string name, Vector3 at)
            {
                var go = new GameObject(name) { layer = Root.gameObject.layer };
                go.transform.SetParent(Root, false);
                go.transform.localPosition = At(at);
            }

            internal GameObject Cube(string name, Vector3 at, Vector3 size, Color color, Vector3 euler = default, float metal = 0.4f, float gloss = 0.45f) =>
                Shape(PrimitiveType.Cube, name, at, size, size * 0.5f, euler, color, metal, gloss);

            /// <summary>A round bar: diameter and length, along Z (the barrel's way) unless the axis says 'x' or 'y'.</summary>
            internal GameObject Tube(string name, Vector3 at, float diameter, float length, Color color, char axis = 'z', float metal = 0.4f, float gloss = 0.45f)
            {
                Vector3 euler = axis == 'z' ? new Vector3(90f, 0f, 0f) : axis == 'x' ? new Vector3(0f, 0f, 90f) : Vector3.zero;
                return Shape(PrimitiveType.Cylinder, name, at, new Vector3(diameter, length * 0.5f, diameter), new Vector3(diameter, length, diameter) * 0.5f, euler, color, metal, gloss);
            }

            internal GameObject Ball(string name, Vector3 at, Vector3 size, Color color, float metal = 0.4f, float gloss = 0.45f) =>
                Shape(PrimitiveType.Sphere, name, at, size, size * 0.5f, Vector3.zero, color, metal, gloss);

            private GameObject Shape(PrimitiveType type, string name, Vector3 at, Vector3 scale, Vector3 half, Vector3 euler, Color color, float metal, float gloss)
            {
                GameObject p = GameObject.CreatePrimitive(type);
                Object.DestroyImmediate(p.GetComponent<Collider>());
                p.name = name;
                p.layer = Root.gameObject.layer;
                p.transform.SetParent(Root, false);
                p.transform.localPosition = At(at);
                Quaternion rot = Quaternion.Euler(euler);
                p.transform.localRotation = rot;
                p.transform.localScale = scale;
                Renderer r = p.GetComponent<Renderer>();
                r.sharedMaterial = Shared(_base, r, color, metal, gloss);
                // the part's corners, in the model's space (a group sits where it was put, without turning)
                Vector3 centre = _originInModel + At(at);
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = centre + rot * Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    _top._min = Vector3.Min(_top._min, corner);
                    _top._max = Vector3.Max(_top._max, corner);
                }
                return p;
            }
        }

        /// <summary>One colour on one part, with the game's material as the base: used by the effects (flashes, tracers, casings).</summary>
        internal static void Paint(GameObject part, Material baseMaterial, Color color, bool emissive, float metallic = 0.4f, float gloss = 0.45f)
        {
            Renderer r = part.GetComponent<Renderer>();
            Material m = baseMaterial != null ? new Material(baseMaterial) : new Material(r.sharedMaterial);
            m.mainTexture = null;
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
            if (emissive && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2f);
            }
            r.sharedMaterial = m;
        }
    }
}
