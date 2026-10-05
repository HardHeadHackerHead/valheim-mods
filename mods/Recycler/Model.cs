using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Recycler
{
    /// <summary>What a part of the model is made of. Each one borrows a real material from the game, so it has a proper wood, stone or metal look.</summary>
    internal enum Mat { Stone, Planks, Dark, Iron, Coal, Teal }

    /// <summary>The game's own materials (stone wall, wooden floor planks, dark wood, grate metal...), found by name from existing pieces.</summary>
    internal static class Look
    {
        private static readonly Dictionary<Mat, Material> Materials = new Dictionary<Mat, Material>();
        private static readonly Dictionary<Mat, Color> Fallback = new Dictionary<Mat, Color>
        {
            { Mat.Stone, new Color(0.45f, 0.45f, 0.47f) }, { Mat.Planks, new Color(0.45f, 0.30f, 0.17f) }, { Mat.Dark, new Color(0.22f, 0.15f, 0.10f) },
            { Mat.Iron, new Color(0.35f, 0.35f, 0.38f) }, { Mat.Coal, new Color(0.16f, 0.16f, 0.18f) }, { Mat.Teal, new Color(0.25f, 0.75f, 0.8f) },
        };

        private static Material Find(ZNetScene scene, string prefab, string material)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == material) return m;
            return null;
        }

        internal static void Harvest(ZNetScene scene, Material plain)
        {
            Materials.Clear();
            Materials[Mat.Stone] = Find(scene, "stone_wall_1x1", "stone_mat");
            Materials[Mat.Planks] = Find(scene, "wood_floor", "woodwall");
            Materials[Mat.Dark] = Find(scene, "darkwood_pole", "DarkWood_mat");
            Materials[Mat.Iron] = Find(scene, "iron_grate", "metalwall");

            Material coal = Find(scene, "piece_cauldron", "cauldron_chain"); // plain dark metal, no texture
            Materials[Mat.Coal] = coal;

            Material baseForTeal = coal != null ? coal : plain;
            var teal = new Material(baseForTeal) { color = Fallback[Mat.Teal] };
            if (teal.HasProperty("_EmissionColor")) { teal.EnableKeyword("_EMISSION"); teal.SetColor("_EmissionColor", Fallback[Mat.Teal] * 0.9f); }
            Materials[Mat.Teal] = teal;

            // anything we could not find: a plain colour on the chest's material, so the model is still complete
            foreach (Mat m in Fallback.Keys.ToList())
                if (Materials.TryGetValue(m, out Material found) && found != null) continue;
                else
                {
                    var flat = new Material(plain) { color = Fallback[m] };
                    if (flat.HasProperty("_MainTex")) flat.mainTexture = null;
                    Materials[m] = flat;
                }
            Debug.Log("[Recycler] materials: " + string.Join(", ", Materials.Select(kv => kv.Key + "=" + kv.Value.name).ToArray()));
        }

        internal static Material Of(Mat m) => Materials.TryGetValue(m, out Material found) ? found : null;
    }

    /// <summary>
    /// The look of the Recycler and its Press, built from simple shapes (boxes, cylinders, spheres) wearing the game's real materials.
    /// The chest's own mesh and colliders are replaced by these and one box collider. Positions are metres from the middle of the base, y up.
    /// </summary>
    internal static class Model
    {
        internal static readonly Vector3 RecyclerHitCenter = new Vector3(0, 0.7f, 0), RecyclerHitSize = new Vector3(1.8f, 1.4f, 1.3f);
        internal static readonly Vector3 PressHitCenter = new Vector3(0, 0.65f, 0), PressHitSize = new Vector3(1.0f, 1.3f, 1.0f);

        private static GameObject Add(Transform parent, string name, PrimitiveType shape, Vector3 pos, Vector3 size, Vector3 euler, Mat mat)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // the one hitbox does the work
            go.layer = parent.gameObject.layer;
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = size;
            t.localRotation = Quaternion.Euler(euler);
            go.GetComponent<Renderer>().sharedMaterial = Look.Of(mat);
            return go;
        }

        private static GameObject Box(Transform p, string n, float x, float y, float z, float sx, float sy, float sz, Mat m, float rx = 0, float ry = 0, float rz = 0) =>
            Add(p, n, PrimitiveType.Cube, new Vector3(x, y, z), new Vector3(sx, sy, sz), new Vector3(rx, ry, rz), m);

        /// <summary>A toothed roller lying along the front-to-back direction that turns when the Recycler works.</summary>
        private static void Roller(Transform parent, string name, float x, float y, float radius, float length, Mat body, Mat teeth)
        {
            var axis = new GameObject(name) { layer = parent.gameObject.layer };
            axis.transform.SetParent(parent, false);
            axis.transform.localPosition = new Vector3(x, y, 0);
            axis.transform.localRotation = Quaternion.Euler(90, 0, 0); // its own up is now the front-to-back direction
            Add(axis.transform, "drum", PrimitiveType.Cylinder, Vector3.zero, new Vector3(radius * 2f, length * 0.5f, radius * 2f), Vector3.zero, body);
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f;
                float rad = a * Mathf.Deg2Rad;
                Add(axis.transform, "tooth", PrimitiveType.Cube, new Vector3(Mathf.Cos(rad) * radius, 0, Mathf.Sin(rad) * radius),
                    new Vector3(0.07f, length * 0.92f, 0.1f), new Vector3(0, -a, 0), teeth);
            }
            axis.AddComponent<Spinner>();
        }

        /// <summary>A crank wheel on the side with spokes and a handle, turning with the rollers.</summary>
        private static void Wheel(Transform parent, string name, float x, float y)
        {
            var axis = new GameObject(name) { layer = parent.gameObject.layer };
            axis.transform.SetParent(parent, false);
            axis.transform.localPosition = new Vector3(x, y, 0);
            axis.transform.localRotation = Quaternion.Euler(0, 0, 90); // its own up is now sideways
            Add(axis.transform, "hub", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.14f, 0.1f, 0.14f), Vector3.zero, Mat.Coal);
            Add(axis.transform, "rim", PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(0.62f, 0.03f, 0.62f), Vector3.zero, Mat.Iron);
            for (int k = 0; k < 4; k++)
                Add(axis.transform, "spoke", PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(0.58f, 0.03f, 0.05f), new Vector3(0, k * 45f, 0), Mat.Iron);
            Add(axis.transform, "handle", PrimitiveType.Cylinder, new Vector3(0.24f, 0.13f, 0), new Vector3(0.06f, 0.1f, 0.06f), Vector3.zero, Mat.Dark);
            axis.AddComponent<Spinner>();
        }

        internal static void BuildRecycler(Transform root)
        {
            // stone plinth and a step in front
            Box(root, "plinth", 0, 0.1f, 0, 1.5f, 0.2f, 1.2f, Mat.Stone);
            Box(root, "step", 0, 0.04f, -0.72f, 0.8f, 0.08f, 0.3f, Mat.Stone);

            // the plank body with dark corner posts and iron bands
            Box(root, "body", 0, 0.6f, 0, 1.2f, 0.8f, 0.9f, Mat.Planks);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(root, "post", sx * 0.62f, 0.64f, sz * 0.47f, 0.12f, 0.92f, 0.12f, Mat.Dark);
            Box(root, "bandLow", 0, 0.38f, 0, 1.26f, 0.07f, 0.96f, Mat.Iron);
            Box(root, "bandHigh", 0, 0.86f, 0, 1.26f, 0.07f, 0.96f, Mat.Iron);
            foreach (float x in new[] { -0.45f, -0.15f, 0.15f, 0.45f })
                foreach (float y in new[] { 0.38f, 0.86f })
                    Add(root, "rivet", PrimitiveType.Sphere, new Vector3(x, y, -0.49f), Vector3.one * 0.07f, Vector3.zero, Mat.Coal);

            // a glowing window at the front, where the work happens
            Box(root, "glow", 0, 0.62f, -0.46f, 0.52f, 0.14f, 0.03f, Mat.Teal);
            Box(root, "glowFrame", 0, 0.62f, -0.455f, 0.58f, 0.2f, 0.02f, Mat.Coal);

            // the hopper: four sloping planks that open upwards, and an iron rim
            Box(root, "hopperFront", 0, 1.12f, -0.36f, 1.2f, 0.06f, 0.5f, Mat.Planks, -30);
            Box(root, "hopperBack", 0, 1.12f, 0.36f, 1.2f, 0.06f, 0.5f, Mat.Planks, 30);
            Box(root, "hopperLeft", -0.5f, 1.12f, 0, 0.5f, 0.06f, 0.8f, Mat.Planks, 0, 0, 30);
            Box(root, "hopperRight", 0.5f, 1.12f, 0, 0.5f, 0.06f, 0.8f, Mat.Planks, 0, 0, -30);
            Box(root, "rimFront", 0, 1.34f, -0.52f, 1.4f, 0.06f, 0.08f, Mat.Iron);
            Box(root, "rimBack", 0, 1.34f, 0.52f, 1.4f, 0.06f, 0.08f, Mat.Iron);
            Box(root, "rimLeft", -0.7f, 1.34f, 0, 0.08f, 0.06f, 1.12f, Mat.Iron);
            Box(root, "rimRight", 0.7f, 1.34f, 0, 0.08f, 0.06f, 1.12f, Mat.Iron);

            // two toothed rollers at the bottom of the hopper
            Roller(root, "rollerL", -0.17f, 1.02f, 0.16f, 0.8f, Mat.Teal, Mat.Iron);
            Roller(root, "rollerR", 0.17f, 1.02f, 0.16f, 0.8f, Mat.Coal, Mat.Iron);

            // the crank on the side, joined to the rollers by an axle
            Add(root, "axle", PrimitiveType.Cylinder, new Vector3(0.72f, 0.95f, 0), new Vector3(0.07f, 0.12f, 0.07f), new Vector3(0, 0, 90), Mat.Coal);
            Wheel(root, "wheel", 0.86f, 0.95f);

            // a chute at the front for what comes out
            Box(root, "chute", 0, 0.28f, -0.72f, 0.62f, 0.05f, 0.4f, Mat.Planks, -18);
            Box(root, "chuteLeft", -0.33f, 0.31f, -0.72f, 0.05f, 0.1f, 0.4f, Mat.Iron, -18);
            Box(root, "chuteRight", 0.33f, 0.31f, -0.72f, 0.05f, 0.1f, 0.4f, Mat.Iron, -18);

            // a short stone chimney at the back
            Add(root, "chimney", PrimitiveType.Cylinder, new Vector3(-0.42f, 1.1f, 0.62f), new Vector3(0.24f, 0.45f, 0.24f), Vector3.zero, Mat.Stone);
            Add(root, "chimneyCap", PrimitiveType.Cylinder, new Vector3(-0.42f, 1.56f, 0.62f), new Vector3(0.3f, 0.03f, 0.3f), Vector3.zero, Mat.Iron);
        }

        internal static void BuildPress(Transform root)
        {
            Box(root, "base", 0, 0.1f, 0, 0.9f, 0.2f, 0.9f, Mat.Stone);
            Box(root, "plate", 0, 0.26f, 0, 0.62f, 0.1f, 0.62f, Mat.Iron);
            Box(root, "postL", -0.34f, 0.7f, 0, 0.12f, 1.0f, 0.12f, Mat.Dark);
            Box(root, "postR", 0.34f, 0.7f, 0, 0.12f, 1.0f, 0.12f, Mat.Dark);
            Box(root, "beam", 0, 1.22f, 0, 0.84f, 0.16f, 0.22f, Mat.Planks);
            Box(root, "beamBand", 0, 1.22f, 0, 0.88f, 0.05f, 0.26f, Mat.Iron);
            Add(root, "piston", PrimitiveType.Cylinder, new Vector3(0, 0.78f, 0), new Vector3(0.22f, 0.4f, 0.22f), Vector3.zero, Mat.Teal);
            Add(root, "head", PrimitiveType.Cylinder, new Vector3(0, 0.4f, 0), new Vector3(0.46f, 0.04f, 0.46f), Vector3.zero, Mat.Iron);
            Add(root, "screw", PrimitiveType.Cylinder, new Vector3(0, 1.38f, 0), new Vector3(0.1f, 0.12f, 0.1f), Vector3.zero, Mat.Coal);
            Box(root, "lever", 0, 1.38f, 0, 0.7f, 0.05f, 0.05f, Mat.Dark);
        }

        internal static void Build(GameObject prefab, Material plain, bool recycler, Vector3 hitCenter, Vector3 hitSize)
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
            if (recycler) BuildRecycler(root.transform); else BuildPress(root.transform);
        }
    }

    /// <summary>Turns a roller or the crank around its own axis while its Recycler is working (everyone nearby sees it, via the Recycler's synced timestamp).</summary>
    internal class Spinner : MonoBehaviour
    {
        private RecyclerStation _station;

        private void Update()
        {
            if (_station == null) { _station = GetComponentInParent<RecyclerStation>(); if (_station == null) return; }
            if (!_station.Working) return;
            float direction = name == "rollerR" ? -1f : 1f;
            transform.Rotate(0f, direction * 180f * Time.deltaTime, 0f, Space.Self);
        }
    }
}
