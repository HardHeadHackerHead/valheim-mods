using System.Collections.Generic;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The terminal's look, built in code from boxes in the game's own materials (wood for the desk, dark metal for the case): a desk, a
    /// monitor on it with a screen quad, a keyboard and a stool. The piece faces +Z: the stool is at the back (-Z), the monitor looks at it.
    /// The named children (seat, view, spawn, screen) are what TerminalPiece finds when the object wakes.
    /// </summary>
    internal static class Model
    {
        // metres, in the piece's own space
        internal static readonly Vector3 SeatPoint = new Vector3(0f, 0.5f, -0.75f);
        internal static readonly Vector3 ViewPoint = new Vector3(0f, 1.12f, -0.2f);    // the driver's camera: just above the keyboard
        internal static readonly Vector3 SpawnPoint = new Vector3(1.6f, 0.05f, 0.3f);  // where the Proxy appears
        internal const float ScreenWidth = 0.8f, ScreenHeight = 0.45f;
        internal static readonly Vector3 ScreenCentre = new Vector3(0f, 1.12f, 0.19f);

        private static Material _wood, _metal, _case;
        private static Texture2D _black;

        /// <summary>
        /// The look's materials, from the game's own pieces (the ones SlotMachine uses): woodwall planks for the desk, metalwall for the
        /// keyboard, and a plain dark copy of the cauldron's chain for the monitor's case. A game update that renames one falls back to
        /// the chest's material, never to no piece.
        /// </summary>
        internal static void Harvest(ZNetScene scene, Material chest)
        {
            _wood = Find(scene, "wood_floor", "woodwall") ?? chest;
            _metal = Find(scene, "iron_grate", "metalwall") ?? chest;
            Material flat = Find(scene, "piece_cauldron", "cauldron_chain") ?? chest;   // plain dark metal with no texture
            _case = new Material(flat) { name = "TerminalCase", color = new Color(0.1f, 0.1f, 0.12f) };
            if (_case.HasProperty("_MainTex")) _case.mainTexture = null;
            if (_black == null)
            {
                _black = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "TerminalBlack", hideFlags = HideFlags.HideAndDontSave };
                _black.SetPixel(0, 0, new Color(0.02f, 0.03f, 0.03f, 1f));
                _black.Apply();
            }
        }

        /// <summary>
        /// Forget the materials. The case material is NOT destroyed: terminals already standing in the world still draw with it after
        /// a reload (destroyed, their case turned magenta); the new copy makes its own for new ones.
        /// </summary>
        internal static void Clear() { _wood = _metal = _case = null; }

        internal static Texture2D Black => _black;

        private static Material Find(ZNetScene scene, string prefab, string materialName)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && m.name == materialName) return m;
            return null;
        }

        private static Transform Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        private static Transform Empty(Transform parent, string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            return go.transform;
        }

        internal static void Build(Transform root)
        {
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);

            // the desk: a top and four legs
            Box(model, "desktop", new Vector3(0f, 0.75f, 0f), new Vector3(1.7f, 0.06f, 0.85f), _wood);
            foreach (float x in new[] { -0.78f, 0.78f })
                foreach (float z in new[] { -0.38f, 0.38f })
                    Box(model, "leg", new Vector3(x, 0.36f, z), new Vector3(0.07f, 0.72f, 0.07f), _wood);
            Box(model, "drawers", new Vector3(0.55f, 0.5f, 0f), new Vector3(0.5f, 0.4f, 0.7f), _wood);

            // the monitor: a case, a stand and a screen quad on its front (facing the stool)
            Box(model, "case", ScreenCentre + new Vector3(0f, 0f, 0.025f), new Vector3(ScreenWidth + 0.1f, ScreenHeight + 0.1f, 0.05f), _case);
            Box(model, "neck", new Vector3(0f, 0.9f, 0.3f), new Vector3(0.06f, 0.25f, 0.05f), _case);   // (behind the case: in front of the screen it would block the picture)
            Box(model, "foot", new Vector3(0f, 0.785f, 0.3f), new Vector3(0.3f, 0.02f, 0.2f), _case);
            GameObject screen = GameObject.CreatePrimitive(PrimitiveType.Quad);   // its front faces -Z, towards the stool
            Object.DestroyImmediate(screen.GetComponent<Collider>());
            screen.name = "screen";
            screen.transform.SetParent(model, false);
            screen.transform.localPosition = ScreenCentre + new Vector3(0f, 0f, -0.004f);   // (in front of the case, not on the same plane: that z-fights)
            screen.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1f);
            Shader unlit = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            screen.GetComponent<MeshRenderer>().sharedMaterial = new Material(unlit) { name = "TerminalScreen", mainTexture = _black };

            // the keyboard and the computer under the desk
            Box(model, "keyboard", new Vector3(0f, 0.795f, -0.22f), new Vector3(0.46f, 0.02f, 0.15f), _case);
            Box(model, "tower", new Vector3(-0.55f, 0.5f, 0f), new Vector3(0.28f, 0.42f, 0.55f), _metal);

            // a stool
            Box(model, "seat", new Vector3(0f, 0.45f, -0.75f), new Vector3(0.45f, 0.06f, 0.45f), _wood);
            foreach (float x in new[] { -0.17f, 0.17f })
                foreach (float z in new[] { -0.92f, -0.58f })
                    Box(model, "stoolleg", new Vector3(x, 0.21f, z), new Vector3(0.05f, 0.42f, 0.05f), _wood);

            // the places the code finds: where the driver sits, the driver's camera, where the Proxy appears
            Empty(root, "seat", SeatPoint);
            Empty(root, "view", ViewPoint).localRotation = Quaternion.identity;   // (looks along the piece's +Z, at the monitor)
            Empty(root, "spawn", SpawnPoint);
        }

        internal static List<Renderer> Renderers(Transform root)
        {
            var list = new List<Renderer>();
            root.GetComponentsInChildren(true, list);
            return list;
        }
    }
}
