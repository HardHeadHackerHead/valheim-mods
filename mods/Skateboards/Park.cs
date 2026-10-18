using System.Collections.Generic;
using DHack.Shared;
using UnityEngine;

namespace Skateboards
{
    /// <summary>
    /// The skate park: four pieces built with the hammer at a workbench (Misc), each the server's cost.
    ///
    ///   Grind Rail     a 4 m iron-capped rail on three posts, its top half a metre up: ollie onto it to grind.
    ///   Skate Ledge    a stone box 3 m long and half a metre high with iron edges: grind along its edges.
    ///   Kicker         a short curved launch ramp (0.6 m high at 37 degrees): roll up it and fly.
    ///   Quarter Pipe   a curved ramp 4 m wide that turns from flat to straight up at 2.6 m, with a deck on top and an iron coping
    ///                  along its lip (grindable): roll up it for a vert air and come back down; two facing each other make a half pipe.
    /// Each is a copy of the game's wood floor (so it is placed, worn, broken, supported and saved like the game's pieces) with its own
    /// shapes: the ramps' curved surfaces are generated meshes, which are also what the board rolls on (their colliders). The grindable
    /// tops are thin box colliders, which is what Rails.cs looks for. Nothing of ours runs on them: no code to reload.
    /// Registered from the scene (and again whenever the game swaps its lists), and replaced in place on a hot reload
    /// (Shared/PieceReload.cs) so none in a loading area is ever lost; out of the build menu on a server without the mod.
    /// </summary>
    internal static class Park
    {
        public const string RailName = "skateboards_rail", LedgeName = "skateboards_ledge", KickerName = "skateboards_kicker", PipeName = "skateboards_quarterpipe";

        private static GameObject _holder;
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static Material _wood, _iron, _stone, _dark;

        private static PieceTable Hammer(ZNetScene scene) => scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;

        public static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (Prefabs.Count == 0)
            {
                GameObject floor = scene.GetPrefab("wood_floor");
                if (floor == null) { Plugin.Log.LogWarning("The game's wood floor was not found, so there is no skate park"); return; }
                _holder = new GameObject("SkateboardsPark");
                _holder.SetActive(false);   // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
                try { Materials(scene); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Could not take the game's materials, the skate park looks plain: " + e.Message); }
                Add(scene, floor, RailName, "$skateboards_rail", Plugin.RailCost, BuildRail);
                Add(scene, floor, LedgeName, "$skateboards_ledge", Plugin.LedgeCost, BuildLedge);
                Add(scene, floor, KickerName, "$skateboards_kicker", Plugin.KickerCost, BuildKicker);
                Add(scene, floor, PipeName, "$skateboards_quarterpipe", Plugin.PipeCost, BuildPipe);
            }
            PieceTable hammer = Hammer(scene);
            foreach (GameObject prefab in Prefabs.Values)
            {
                PieceReload.ReplacePrefab(scene, prefab, hammer);
                Plugin.Synced.NeedsServer(prefab);   // not in the build menu on a server without the mod (it would delete them)
            }
        }

        /// <summary>When this copy unloads: out of the build menu, but still registered (Shared/PieceReload.cs).</summary>
        public static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            PieceTable hammer = scene != null ? Hammer(scene) : null;
            foreach (GameObject prefab in Prefabs.Values) PieceReload.UnloadPiece(prefab, hammer);
            Prefabs.Clear();
            _holder = null;
            _wood = _iron = _stone = _dark = null;
        }

        public static void ApplyCosts()
        {
            Apply(RailName, Plugin.RailCost.Value);
            Apply(LedgeName, Plugin.LedgeCost.Value);
            Apply(KickerName, Plugin.KickerCost.Value);
            Apply(PipeName, Plugin.PipeCost.Value);
        }

        private static void Apply(string name, string cost)
        {
            if (Prefabs.TryGetValue(name, out GameObject go) && go != null) Costs.Apply(go.GetComponent<Piece>(), cost, Plugin.Log);
        }

        public static bool Registered => Prefabs.Count > 0;

        // ---- building one ----

        private delegate void Shape(GameObject go, int layer);

        private static void Add(ZNetScene scene, GameObject floor, string name, string key, BepInEx.Configuration.ConfigEntry<string> cost, Shape shape)
        {
            GameObject go = Object.Instantiate(floor, _holder.transform);
            go.name = name;
            // what it was: its pictures and hit boxes go, the game's behaviours (placed, worn, broken, supported, saved) stay
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.enabled = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            SnapPoints.Clear(go);
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = go.layer;
            // the shape (if it fails, the piece is still registered with a plain box, so the ones in the world are never deleted)
            try { shape(go, layer); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not build the {name}'s shape: " + e);
                Box(go, layer, "fallback", new Vector3(0f, 0.25f, 0f), new Vector3(1f, 0.5f, 1f), null);
            }

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = key;
            piece.m_description = key + "_desc";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false;
            piece.m_allowedInDungeons = false;
            if (!Costs.Apply(piece, cost.Value, Plugin.Log)) Costs.Apply(piece, (string)cost.DefaultValue, Plugin.Log);
            try
            {
                Sprite icon = Icon.Make(go, new Vector3(1f, 0.6f, -1f), null, null);
                if (icon != null) piece.m_icon = icon;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"Could not draw the {name}'s build-menu picture: " + e.Message); }
            Prefabs[name] = go;
        }

        // ---- the four ----

        private const float RailLength = 4f, RailTop = 0.5f;

        private static void BuildRail(GameObject go, int layer)
        {
            // three posts and their feet, the rail on top (a round iron pipe), and its grindable top: a thin box along it
            for (int i = -1; i <= 1; i++)
            {
                float z = i * (RailLength / 2f - 0.25f);
                Part(go, PrimitiveType.Cube, _dark, new Vector3(0f, (RailTop - 0.04f) / 2f, z), Vector3.zero, new Vector3(0.07f, RailTop - 0.04f, 0.07f));
                Part(go, PrimitiveType.Cube, _dark, new Vector3(0f, 0.015f, z), Vector3.zero, new Vector3(0.3f, 0.03f, 0.2f));
                Box(go, layer, "post", new Vector3(0f, (RailTop - 0.05f) / 2f, z), new Vector3(0.08f, RailTop - 0.05f, 0.08f), null);
            }
            Part(go, PrimitiveType.Cylinder, _iron, new Vector3(0f, RailTop - 0.03f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.06f, RailLength / 2f, 0.06f));
            Box(go, layer, "grind", new Vector3(0f, RailTop - 0.03f, 0f), new Vector3(0.06f, 0.06f, RailLength), null);
            SnapPoints.Add(go, "end", new Vector3(0f, 0f, -RailLength / 2f), new Vector3(0f, 0f, RailLength / 2f), new Vector3(0f, RailTop, -RailLength / 2f), new Vector3(0f, RailTop, RailLength / 2f));
        }

        private const float LedgeLength = 3f, LedgeWidth = 1f, LedgeHeight = 0.45f;

        private static void BuildLedge(GameObject go, int layer)
        {
            Box(go, layer, "ledge", new Vector3(0f, LedgeHeight / 2f, 0f), new Vector3(LedgeWidth, LedgeHeight, LedgeLength), _wood);
            for (int side = -1; side <= 1; side += 2)   // the iron edges it grinds on
                Part(go, PrimitiveType.Cube, _iron, new Vector3(side * (LedgeWidth / 2f - 0.02f), LedgeHeight - 0.02f, 0f), Vector3.zero, new Vector3(0.05f, 0.05f, LedgeLength + 0.002f));
            SnapPoints.Box(go, new Vector3(-LedgeWidth / 2f, 0f, -LedgeLength / 2f), new Vector3(LedgeWidth / 2f, LedgeHeight, LedgeLength / 2f), top: true);
        }

        private const float KickerRadius = 3f, KickerRun = 1.8f, KickerWidth = 1.6f, KickerTop = 0.3f;

        private static void BuildKicker(GameObject go, int layer)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= 14; i++)
            {
                float z = KickerRun * i / 14f;
                profile.Add(new Vector2(z, KickerRadius - Mathf.Sqrt(KickerRadius * KickerRadius - z * z)));
            }
            float h = profile[profile.Count - 1].y;
            profile.Add(new Vector2(KickerRun + KickerTop, h));
            Ramp(go, layer, "kicker", profile, KickerWidth, -(KickerRun + KickerTop) / 2f);
            float half = (KickerRun + KickerTop) / 2f;
            SnapPoints.Add(go, "corner", new Vector3(-KickerWidth / 2f, 0f, -half), new Vector3(KickerWidth / 2f, 0f, -half), new Vector3(-KickerWidth / 2f, 0f, half), new Vector3(KickerWidth / 2f, 0f, half));
        }

        private const float PipeRadius = 2.4f, PipeHeight = 2.6f, PipeWidth = 4f, PipeDeck = 1.2f;

        private static void BuildPipe(GameObject go, int layer)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float a = 90f * i / 24f * Mathf.Deg2Rad;
                profile.Add(new Vector2(PipeRadius * Mathf.Sin(a), PipeRadius * (1f - Mathf.Cos(a))));   // flat at the front, straight up at the back
            }
            profile.Add(new Vector2(PipeRadius, PipeHeight));
            profile.Add(new Vector2(PipeRadius + PipeDeck, PipeHeight));
            float shift = -(PipeRadius + PipeDeck) / 2f;
            Ramp(go, layer, "pipe", profile, PipeWidth, shift);
            // the coping: an iron pipe along the lip, and its grindable top
            float lipZ = PipeRadius + shift;
            Part(go, PrimitiveType.Cylinder, _iron, new Vector3(0f, PipeHeight + 0.02f, lipZ + 0.03f), new Vector3(0f, 0f, 90f), new Vector3(0.07f, PipeWidth / 2f, 0.07f));
            Box(go, layer, "coping", new Vector3(0f, PipeHeight + 0.02f, lipZ + 0.03f), new Vector3(PipeWidth, 0.07f, 0.07f), null);
            float back = PipeRadius + PipeDeck + shift;
            SnapPoints.Add(go, "corner", new Vector3(-PipeWidth / 2f, 0f, shift), new Vector3(PipeWidth / 2f, 0f, shift), new Vector3(-PipeWidth / 2f, 0f, back), new Vector3(PipeWidth / 2f, 0f, back),
                new Vector3(-PipeWidth / 2f, PipeHeight, back), new Vector3(PipeWidth / 2f, PipeHeight, back));
        }

        // ---- shapes ----

        /// <summary>
        /// A solid ramp: a side profile (z forward, y up, from the front lip at the ground to the top of the back) swept across the width,
        /// closed down the back and underneath, with both sides capped. Its mesh is both what is drawn and what is ridden (a mesh collider).
        /// </summary>
        private static void Ramp(GameObject go, int layer, string name, List<Vector2> profile, float width, float shift)
        {
            var outline = new List<Vector2>(profile);
            Vector2 last = profile[profile.Count - 1];
            outline.Add(new Vector2(last.x, 0f));   // down the back to the ground
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float hw = width / 2f, along = 0f;

            // the top and the back: each edge of the outline a strip across the width, facing out (the solid is to its right going round)
            for (int i = 0; i < outline.Count - 1; i++)
            {
                Vector2 a = outline[i], b = outline[i + 1];
                float len = (b - a).magnitude;
                int at = verts.Count;
                verts.Add(new Vector3(-hw, a.y, a.x + shift)); verts.Add(new Vector3(hw, a.y, a.x + shift));
                verts.Add(new Vector3(-hw, b.y, b.x + shift)); verts.Add(new Vector3(hw, b.y, b.x + shift));
                uvs.Add(new Vector2(0f, along / 2f)); uvs.Add(new Vector2(width / 2f, along / 2f));
                uvs.Add(new Vector2(0f, (along + len) / 2f)); uvs.Add(new Vector2(width / 2f, (along + len) / 2f));
                along += len;
                tris.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
            }
            // the bottom, from the back to the front lip, facing down
            {
                Vector2 a = outline[outline.Count - 1], b = outline[0];
                int at = verts.Count;
                verts.Add(new Vector3(-hw, a.y, a.x + shift)); verts.Add(new Vector3(hw, a.y, a.x + shift));
                verts.Add(new Vector3(-hw, b.y, b.x + shift)); verts.Add(new Vector3(hw, b.y, b.x + shift));
                uvs.Add(Vector2.zero); uvs.Add(new Vector2(width / 2f, 0f)); uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(width / 2f, 1f));
                tris.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
            }
            // the two sides: a fan from the bottom back corner (every point of the outline can see it)
            for (int side = -1; side <= 1; side += 2)
            {
                int at = verts.Count;
                foreach (Vector2 p in outline) { verts.Add(new Vector3(side * hw, p.y, p.x + shift)); uvs.Add(new Vector2(p.x / 2f, p.y / 2f)); }
                int corner = at + outline.Count - 1;
                for (int i = 0; i < outline.Count - 2; i++)
                {
                    if (side < 0) tris.AddRange(new[] { corner, at + i + 1, at + i });
                    else tris.AddRange(new[] { corner, at + i, at + i + 1 });
                }
            }
            var mesh = new Mesh { name = "skateboards_" + name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var part = new GameObject(name) { layer = layer };
            part.transform.SetParent(go.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = part.AddComponent<MeshRenderer>();
            if (_wood != null) mr.sharedMaterial = _wood;
            part.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static BoxCollider Box(GameObject go, int layer, string name, Vector3 center, Vector3 size, Material look)
        {
            GameObject part;
            if (look != null)
            {
                part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.GetComponent<MeshRenderer>().sharedMaterial = look;
                Object.DestroyImmediate(part.GetComponent<Collider>());
            }
            else part = new GameObject();
            part.name = name;
            part.layer = layer;
            part.transform.SetParent(go.transform, false);
            part.transform.localPosition = center;
            if (look != null) part.transform.localScale = size;
            var box = part.AddComponent<BoxCollider>();
            box.size = look != null ? Vector3.one : size;
            return box;
        }

        private static void Part(GameObject go, PrimitiveType shape, Material mat, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            GameObject g = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.layer = go.layer;
            g.transform.SetParent(go.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            if (mat != null) g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material FindMat(ZNetScene scene, string prefab, string name = null)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (r is MeshRenderer && r.sharedMaterial != null && (name == null || r.sharedMaterial.name == name)) return r.sharedMaterial;
            return null;
        }

        private static void Materials(ZNetScene scene)
        {
            Material clubWood = FindMat(scene, "Club");
            Material basis = clubWood ?? FindMat(scene, "Wood");
            Material Tinted(Material from, Color c, float metal, bool texture)
            {
                if (from == null) return null;
                var m = new Material(from) { color = c };
                if (!texture && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
                if (!texture && m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", metal > 0.5f ? 0.6f : 0.15f);
                return m;
            }
            // the wood floor's planks (the game's piece shader, on plain cubes there: it takes our meshes' metre-based mapping too)
            Material planks = FindMat(scene, "wood_floor", "woodwall");
            _wood = planks != null ? planks : Tinted(basis, new Color(0.9f, 0.78f, 0.62f), 0f, true);
            _iron = Tinted(basis, new Color(0.55f, 0.56f, 0.58f), 0.85f, false);
            _dark = Tinted(basis, new Color(0.2f, 0.2f, 0.21f), 0.6f, false);
            Material rock = FindMat(scene, "stone_floor_2x2", "stonefloor_2x2");
            _stone = rock != null ? new Material(rock) : Tinted(basis, new Color(0.55f, 0.54f, 0.5f), 0f, false);
        }
    }
}
