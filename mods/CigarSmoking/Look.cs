using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The materials a model is dressed in. Wood, stone and iron are the game's own (borrowed by name from existing pieces, so they get the
    /// real textures); the rest are plain colours on a flat material. Names match tools/modelkit/cigars.py.
    /// </summary>
    internal static class Look
    {
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<Color, Material> Tints = new Dictionary<Color, Material>();

        // every other colour a model names comes from the model kit (Palette.cs, made by tools/modelkit/build_cigars.py)

        private static Material Find(ZNetScene scene, string prefab, string material)
        {
            GameObject go = scene != null ? scene.GetPrefab(prefab) : null;
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == material) return m;
            return null;
        }

        internal static bool Ready => Materials.Count > 0;

        internal static void Harvest(ZNetScene scene)
        {
            Materials.Clear();
            Tints.Clear();
            Materials["Stone"] = Find(scene, "stone_wall_1x1", "stone_mat");
            Materials["Planks"] = Find(scene, "wood_floor", "woodwall");
            Materials["Dark"] = Find(scene, "darkwood_pole", "DarkWood_mat");
            Materials["Iron"] = Find(scene, "iron_grate", "metalwall");

            // a plain material that takes a colour: the game's wood item (a Standard material), with its texture taken off
            Material flat = Find(scene, "Wood", "wood_item") ?? Find(scene, "piece_cauldron", "cauldron_chain");
            Material basis = flat != null ? flat : new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
            foreach (KeyValuePair<string, Color> kv in Palette.All)
            {
                Materials[kv.Key] = Plainly(basis, kv.Value);
            }
            var fallbacks = new Dictionary<string, Color>
            {
                { "Stone", new Color(0.5f, 0.5f, 0.52f) }, { "Planks", new Color(0.5f, 0.34f, 0.2f) },
                { "Dark", new Color(0.25f, 0.17f, 0.11f) }, { "Iron", new Color(0.38f, 0.38f, 0.42f) },
            };
            foreach (KeyValuePair<string, Color> kv in fallbacks)
                if (Materials[kv.Key] == null) Materials[kv.Key] = new Material(basis) { color = kv.Value };
        }

        /// <summary>
        /// A plain colour on the same flat material (used for the cigars, whose colours depend on the type). One per colour, kept: a cigar is
        /// built each time someone lights one, and a new material each time would never be freed.
        /// </summary>
        internal static Material Tint(Color color)
        {
            if (Tints.TryGetValue(color, out Material made) && made != null) return made;
            Material basis = Materials.TryGetValue("Seed", out Material seed) ? seed : new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
            return Tints[color] = Plainly(basis, color);
        }

        /// <summary>A flat, matt colour on the given material.</summary>
        private static Material Plainly(Material basis, Color color)
        {
            var m = new Material(basis) { color = color };
            if (m.HasProperty("_MainTex")) m.mainTexture = null;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        private static readonly HashSet<string> Warned = new HashSet<string>();

        internal static Material Of(string name)
        {
            if (Materials.TryGetValue(name, out Material m)) return m;
            if (Warned.Add(name)) Debug.LogWarning("[" + Plugin.Name + "] a model uses a material called '" + name + "' that is not in the palette");
            return Materials.Values.FirstOrDefault();
        }

        internal static void Clear()
        {
            Materials.Clear();
            Tints.Clear();
        }
    }
}
