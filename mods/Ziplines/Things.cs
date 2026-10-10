using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// The Zipline Post, the one piece this mod adds: a copy of the game's wooden pole (so it has what a building piece needs: it can be placed,
    /// broken, saved and loaded) dressed as a tall post with a pulley on top, and put in the hammer's menu. Built once, and put where the game
    /// looks for it again and again: ZNetScene (needed to load a saved post) and ObjectDB wake in either order, and the game swaps its lists when a
    /// world loads (see docs/modding-pitfalls.md).
    /// </summary>
    internal static class Things
    {
        internal const string PostPrefab = "dh_zip_post";
        internal const float Height = 4.2f;   // the post, from the ground to the top of its pulley block

        private static GameObject _holder, _post;
        private static bool _built;

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static GameObject PostObject => _post;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_holder == null)
            {
                _holder = new GameObject("ZiplinePrefabs");
                _holder.SetActive(false); // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
            }
            if (!_built)
            {
                _post = BuildPost(scene);
                if (_post == null) return;
                _built = true;
            }
            if (!scene.m_prefabs.Contains(_post)) scene.m_prefabs.Add(_post);
            Named(scene)[_post.name.GetStableHashCode()] = _post;   // lets it be placed and loaded from saves
            PieceTable table = scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (table != null && !table.m_pieces.Contains(_post)) table.m_pieces.Add(_post);
            AttachToPlaced();
        }

        /// <summary>
        /// After a reload (a hot reload, or the manager updating the mod mid-game) the posts already standing in the world have lost the
        /// behaviour of the copy that was unloaded: each is given the new one again.
        /// </summary>
        private static void AttachToPlaced()
        {
            int hash = PostPrefab.GetStableHashCode(), fixedPosts = 0, stale = 0;
            foreach (ZNetView view in Object.FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid() || view.GetZDO().GetPrefab() != hash) continue;
                // the copies of this behaviour from the mod as it was before the reload are still on the post, and the game hands "press E" and the
                // hover text to the first it finds: those are taken off, so only this copy answers
                foreach (MonoBehaviour old in view.GetComponents<MonoBehaviour>())
                    if (old != null && old.GetType() != typeof(Post) && old.GetType().FullName == typeof(Post).FullName) { Object.DestroyImmediate(old); stale++; }
                if (view.GetComponent<Post>() != null) continue;
                view.gameObject.AddComponent<Post>();
                fixedPosts++;
            }
            if (fixedPosts > 0 || stale > 0) Plugin.Log.LogInfo($"{fixedPosts} post(s) already in the world were attached to the reloaded mod ({stale} old copies of its behaviour taken off)");
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && _post != null)
            {
                scene.m_prefabs.Remove(_post);
                int hash = _post.name.GetStableHashCode();
                Dictionary<int, GameObject> named = Named(scene);
                if (named.TryGetValue(hash, out GameObject current) && current == _post) named.Remove(hash);   // never another copy's entry
                PieceTable table = scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                table?.m_pieces.RemoveAll(p => p == null || p == _post);
            }
            _post = null;
            _built = false;
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
        }

        // ---- the post ------------------------------------------------------------------------------------------------

        private static Material Find(ZNetScene scene, string prefab, string material)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && (material == null || m.name == material)) return m;
            return null;
        }

        private static GameObject BuildPost(ZNetScene scene)
        {
            GameObject src = scene.GetPrefab("wood_pole2");
            if (src == null) { Plugin.Log.LogWarning("The wood_pole2 prefab was not found, so there is no Zipline Post"); return null; }
            // plain tinted materials (the game's pole material needs the pole's own mesh and looks white on ours)
            Material wood = null, iron = null;
            try { wood = Plain(scene, new Color(0.50f, 0.34f, 0.19f), 0.05f); iron = Plain(scene, new Color(0.15f, 0.15f, 0.16f), 0.5f); }
            catch (System.Exception e) { Plugin.Log.LogWarning("No materials for the Zipline Post (it looks plain): " + e.Message); }

            GameObject go = Object.Instantiate(src, _holder.transform);
            go.name = PostPrefab;
            // what it was: its pictures and hit boxes go, the game's behaviours (a piece that can be placed, broken, saved) stay
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.enabled = false;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = go.layer;

            // the post: a round pole, a block on top, and a pulley wheel for the rope (the look never stops the piece being registered: without
            // it, the host would delete the posts standing in the world)
            try
            {
                Part(go, PrimitiveType.Cylinder, "Pole", new Vector3(0.28f, 1.95f, 0.28f), new Vector3(0f, 1.95f, 0f), Quaternion.identity, wood);
                Part(go, PrimitiveType.Cube, "Block", new Vector3(0.34f, 0.3f, 0.34f), new Vector3(0f, 4.05f, 0f), Quaternion.identity, wood);
                Part(go, PrimitiveType.Cylinder, "Wheel", new Vector3(0.34f, 0.025f, 0.34f), new Vector3(0f, 4.2f, 0f), Quaternion.Euler(90f, 0f, 0f), iron);
                Part(go, PrimitiveType.Cube, "Bracket", new Vector3(0.05f, 0.24f, 0.05f), new Vector3(0f, 4.17f, 0f), Quaternion.identity, iron);
                Part(go, PrimitiveType.Cube, "Foot", new Vector3(0.5f, 0.12f, 0.5f), new Vector3(0f, 0.06f, 0f), Quaternion.identity, wood);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not build the Zipline Post's model: " + e.Message); }
            foreach (Transform child in go.transform) if (child.name != "Pole" && child.name != "Block" && child.name != "Wheel" && child.name != "Bracket" && child.name != "Foot" && child.GetComponent<MeshRenderer>() != null) child.gameObject.layer = layer;

            var hit = new GameObject("Hitbox") { layer = layer };
            hit.transform.SetParent(go.transform, false);
            var box = hit.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, Height * 0.5f, 0f);
            box.size = new Vector3(0.36f, Height, 0.36f);

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "Zipline Post";
            piece.m_description = "A tall post with a pulley on top. Build two, press E on one and then on the other to run a rope between them, then press E to ride it.";
            try { piece.m_icon = Icon(); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not draw the Zipline Post's build-menu picture: " + e.Message); }
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_resources = new[] { Req(scene, "Wood", 15), Req(scene, "Resin", 4), Req(scene, "LeatherScraps", 4) }.Where(r => r.m_resItem != null).ToArray();
            go.AddComponent<Post>();
            return go;
        }

        /// <summary>A flat colour: the game's wood item material (a Standard one) with its picture taken off, as Quad's Cigars does.</summary>
        private static Material Plain(ZNetScene scene, Color color, float metal)
        {
            Material basis = Find(scene, "Wood", "wood_item");
            if (basis == null)
            {
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (shader == null) return null; // (the parts keep Unity's default look)
                basis = new Material(shader);
            }
            var m = new Material(basis) { color = color };
            if (m.HasProperty("_MainTex")) m.mainTexture = null;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        private static void Part(GameObject parent, PrimitiveType shape, string name, Vector3 scale, Vector3 at, Quaternion turn, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(shape);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = at;
            part.transform.localRotation = turn;
            part.transform.localScale = scale;
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Piece.Requirement Req(ZNetScene scene, string item, int amount) =>
            new Piece.Requirement { m_resItem = scene.GetPrefab(item)?.GetComponent<ItemDrop>(), m_amount = amount, m_recover = true };

        /// <summary>The build menu's picture: a post with a rope running away from its top.</summary>
        private static Sprite Icon()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) tex.SetPixel(x, y, clear);
            Color brown = new Color(0.45f, 0.30f, 0.17f), dark = new Color(0.16f, 0.14f, 0.13f), rope = new Color(0.86f, 0.78f, 0.56f);
            void Fill(int x0, int y0, int x1, int y1, Color c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) tex.SetPixel(x, y, c); }
            Fill(40, 6, 50, 84, brown);          // the pole
            Fill(34, 80, 56, 90, dark);          // the block on top
            Fill(30, 4, 60, 10, brown);          // its foot
            for (int i = 0; i <= 60; i++)        // the rope, sagging away to the right
            {
                float t = i / 60f;
                int x = Mathf.RoundToInt(Mathf.Lerp(52f, 92f, t)), y = Mathf.RoundToInt(Mathf.Lerp(86f, 36f, t) - 10f * Mathf.Sin(t * Mathf.PI));
                Fill(Mathf.Max(0, x - 1), Mathf.Max(0, y - 1), Mathf.Min(n - 1, x + 1), Mathf.Min(n - 1, y + 1), rope);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
