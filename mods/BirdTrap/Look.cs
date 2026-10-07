using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BirdTrap
{
    /// <summary>
    /// The materials a model is dressed in. Wood, stone and iron are the game's own (borrowed by name from existing pieces, so they get
    /// the real textures); the rest are plain colours on a flat metal material.
    /// </summary>
    internal static class Look
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        // colour (and how much it glows) for the materials that are not borrowed from the game
        private static readonly Dictionary<string, Color> Plain = new Dictionary<string, Color>
        {
            { "StoneDark", new Color(0.36f, 0.36f, 0.38f) }, { "Coal", new Color(0.15f, 0.15f, 0.17f) }, { "Parchment", new Color(0.90f, 0.82f, 0.63f) },
            { "Ink", new Color(0.16f, 0.11f, 0.08f) }, { "Bone", new Color(0.92f, 0.89f, 0.80f) }, { "Red", new Color(0.66f, 0.10f, 0.08f) },
            { "Gold", new Color(0.93f, 0.72f, 0.20f) }, { "Leather", new Color(0.42f, 0.26f, 0.13f) }, { "Glow", new Color(1.0f, 0.72f, 0.28f) },
            { "Cream", new Color(0.95f, 0.92f, 0.84f) }, { "Black", new Color(0.06f, 0.06f, 0.07f) }, { "Green", new Color(0.18f, 0.5f, 0.25f) },
            { "Blue", new Color(0.2f, 0.35f, 0.7f) }, { "Teal", new Color(0.25f, 0.75f, 0.8f) }, { "RedDark", new Color(0.38f, 0.05f, 0.05f) },
            { "Chrome", new Color(0.78f, 0.8f, 0.84f) },
            { "Wicker", new Color(0.68f, 0.52f, 0.31f) }, { "Grey", new Color(0.64f, 0.66f, 0.70f) },
        };
        private static readonly Dictionary<string, float> Glows = new Dictionary<string, float> { { "Glow", 1.1f }, { "Gold", 0.18f } };

        private static Material Find(ZNetScene scene, string prefab, string material)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == material) return m;
            return null;
        }

        internal static void Harvest(ZNetScene scene, Material fallbackBase)
        {
            Materials.Clear();
            Materials["Stone"] = Find(scene, "stone_wall_1x1", "stone_mat");
            Materials["Planks"] = Find(scene, "wood_floor", "woodwall");
            Materials["Dark"] = Find(scene, "darkwood_pole", "DarkWood_mat");
            Materials["Iron"] = Find(scene, "iron_grate", "metalwall");

            Material flat = Find(scene, "piece_cauldron", "cauldron_chain"); // plain dark metal with no texture
            Material basis = flat != null ? flat : fallbackBase;
            foreach (KeyValuePair<string, Color> kv in Plain)
            {
                var m = new Material(basis) { color = kv.Value };
                if (m.HasProperty("_MainTex")) m.mainTexture = null;
                if (Glows.TryGetValue(kv.Key, out float glow) && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", kv.Value * glow);
                }
                Materials[kv.Key] = m;
            }
            foreach (string key in new[] { "Stone", "Planks", "Dark", "Iron" })
                if (Materials[key] == null) Materials[key] = new Material(basis) { color = key == "Stone" ? new Color(0.5f, 0.5f, 0.52f) : key == "Planks" ? new Color(0.5f, 0.34f, 0.2f) : key == "Dark" ? new Color(0.25f, 0.17f, 0.11f) : new Color(0.38f, 0.38f, 0.42f) };
            Debug.Log("[" + Plugin.Name + "] materials: " + string.Join(", ", Materials.Select(kv => kv.Key + "=" + kv.Value.name).ToArray()));
        }

        internal static Material Of(string name) => Materials.TryGetValue(name, out Material m) ? m : Materials.Values.FirstOrDefault();

        internal static void Clear() => Materials.Clear();
    }
}
