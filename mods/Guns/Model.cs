using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>The Machine Gun's model, put together from plain shapes: barrel along +Z, up is +Y, about 1.4 m long with the stock.</summary>
    internal static class Model
    {
        private static readonly Color Steel = new Color(0.17f, 0.18f, 0.20f), Black = new Color(0.07f, 0.07f, 0.08f),
                                       Wood = new Color(0.36f, 0.22f, 0.11f), Brass = new Color(0.62f, 0.48f, 0.20f);

        /// <summary>Builds the model under parent; returns its bounds in its own space. A cylinder's Y scale is half its length.</summary>
        internal static Bounds Build(Transform parent, Material baseMaterial, int layer)
        {
            var root = new GameObject("model") { layer = layer };
            root.transform.SetParent(parent, false);
            Part(root, baseMaterial, PrimitiveType.Cube, "receiver", new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.075f, 0.12f, 0.42f), Steel);
            Part(root, baseMaterial, PrimitiveType.Cube, "rail", new Vector3(0f, 0.075f, 0.03f), Vector3.zero, new Vector3(0.035f, 0.03f, 0.34f), Black);
            Part(root, baseMaterial, PrimitiveType.Cylinder, "shroud", new Vector3(0f, 0.01f, 0.38f), new Vector3(90f, 0f, 0f), new Vector3(0.075f, 0.18f, 0.075f), Steel);
            Part(root, baseMaterial, PrimitiveType.Cylinder, "barrel", new Vector3(0f, 0.01f, 0.62f), new Vector3(90f, 0f, 0f), new Vector3(0.032f, 0.22f, 0.032f), Black);
            Part(root, baseMaterial, PrimitiveType.Cylinder, "muzzle", new Vector3(0f, 0.01f, 0.86f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.04f, 0.05f), Black);
            Part(root, baseMaterial, PrimitiveType.Cube, "stock", new Vector3(0f, -0.02f, -0.36f), new Vector3(-6f, 0f, 0f), new Vector3(0.06f, 0.11f, 0.30f), Wood);
            Part(root, baseMaterial, PrimitiveType.Cube, "buttplate", new Vector3(0f, -0.035f, -0.52f), new Vector3(-6f, 0f, 0f), new Vector3(0.065f, 0.13f, 0.02f), Black);
            Part(root, baseMaterial, PrimitiveType.Cube, "grip", new Vector3(0f, -0.13f, -0.10f), new Vector3(18f, 0f, 0f), new Vector3(0.05f, 0.15f, 0.06f), Wood);
            Part(root, baseMaterial, PrimitiveType.Cube, "foregrip", new Vector3(0f, -0.085f, 0.32f), Vector3.zero, new Vector3(0.05f, 0.12f, 0.055f), Wood);
            Part(root, baseMaterial, PrimitiveType.Cube, "magazine", new Vector3(0f, -0.17f, 0.10f), new Vector3(-8f, 0f, 0f), new Vector3(0.055f, 0.22f, 0.10f), Steel);
            Part(root, baseMaterial, PrimitiveType.Cube, "sight", new Vector3(0f, 0.105f, 0.17f), Vector3.zero, new Vector3(0.015f, 0.04f, 0.015f), Black);
            Part(root, baseMaterial, PrimitiveType.Cube, "bolt", new Vector3(0.042f, 0.03f, 0.02f), Vector3.zero, new Vector3(0.012f, 0.02f, 0.06f), Brass);

            // Where the round leaves and where the casing flies out: empties, found on the model in the player's hand (Fx.Muzzle, Fx.Eject).
            new GameObject("dh_muzzle").transform.SetParent(root.transform, false);
            root.transform.Find("dh_muzzle").localPosition = new Vector3(0f, 0.01f, 0.91f);
            new GameObject("dh_eject").transform.SetParent(root.transform, false);
            root.transform.Find("dh_eject").localPosition = new Vector3(0.045f, 0.04f, 0.06f);
            return new Bounds(new Vector3(0f, -0.08f, 0.19f), new Vector3(0.1f, 0.4f, 1.43f));   // (of the parts above, in the model's own space)
        }

        private static void Part(GameObject root, Material baseMaterial, PrimitiveType shape, string name, Vector3 at, Vector3 euler, Vector3 size, Color color)
        {
            GameObject p = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.name = name;
            p.layer = root.layer;
            p.transform.SetParent(root.transform, false);
            p.transform.localPosition = at;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = size;
            Paint(p, baseMaterial, color, emissive: false);
        }

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
