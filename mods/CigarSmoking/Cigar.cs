using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The model of a cigar, made from primitives: used for the item lying on the ground, the one the hand shows while it is lit, and the one
    /// worn in the mouth or held in the hand while it burns (which gets shorter as it does).
    /// </summary>
    internal static class Cigar
    {
        internal const float Length = 0.16f, Radius = 0.011f;

        /// <summary>The parts of a cigar model that move as it burns down. It lies along +Z with its mouth end at the origin.</summary>
        internal class Parts
        {
            public Transform Root, Body, Band, Tip;
            public Renderer TipRenderer;

            /// <summary>burn: how much of the cigar is left, 0 to 1.</summary>
            public void SetBurn(float burn)
            {
                float len = Length * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(burn));
                Body.localScale = new Vector3(Radius * 2f, len * 0.5f, Radius * 2f);
                Body.localPosition = new Vector3(0f, 0f, len * 0.5f);
                Tip.localPosition = new Vector3(0f, 0f, len);
            }
        }

        /// <summary>A cigar model of one type, lying along +Z with its mouth end at the origin. lit: an ember at the far end, otherwise ash.</summary>
        internal static Parts Build(Transform parent, string name, CigarType type, bool lit)
        {
            var p = new Parts();
            p.Root = new GameObject(name).transform;
            p.Root.SetParent(parent, false);
            p.Body = Piece(p.Root, PrimitiveType.Cylinder, Look.Tint(type.Body), Quaternion.Euler(90f, 0f, 0f));
            p.Band = Piece(p.Root, PrimitiveType.Cylinder, Look.Tint(type.Band), Quaternion.Euler(90f, 0f, 0f));
            p.Band.localPosition = new Vector3(0f, 0f, 0.03f);
            p.Band.localScale = new Vector3(Radius * 2.25f, 0.008f, Radius * 2.25f);
            p.Tip = Piece(p.Root, PrimitiveType.Sphere, lit ? EmberMat() : Look.Of("Ash"), Quaternion.identity);
            p.Tip.localScale = Vector3.one * Radius * 2.1f;
            p.TipRenderer = p.Tip.GetComponent<Renderer>();
            p.SetBurn(1f);
            return p;
        }

        private static Transform Piece(Transform parent, PrimitiveType type, Material mat, Quaternion rotation)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localRotation = rotation;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g.transform;
        }

        private static Material _ember;
        internal static Material EmberMat()
        {
            if (_ember == null)
            {
                _ember = new Material(Shader.Find("Sprites/Default")); // unlit, so it glows at night
                _ember.color = new Color(1f, 0.45f, 0.1f);
            }
            return _ember;
        }

        /// <summary>The item's own model: a cigar lying on the ground, and "attach", the one the hand shows while it is lit.</summary>
        internal static void AddModels(GameObject item, CigarType type)
        {
            Parts lying = Build(item.transform, "model", type, false);
            lying.Root.localRotation = Quaternion.Euler(0f, 90f, 0f);
            lying.Root.localPosition = new Vector3(-Length * 0.5f, 0f, 0f);
            Parts held = Build(item.transform, "attach", type, false);
            held.Root.gameObject.SetActive(false); // the game copies it onto the hand
        }
    }
}
