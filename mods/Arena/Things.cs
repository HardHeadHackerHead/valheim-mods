using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The Arena Standard, the one piece this mod adds: a copy of the game's wooden pole (so it has what a building piece needs: it can be placed,
    /// broken, saved and loaded) dressed as a tall standard with a banner, a horned skull and crossed axes, and put in the hammer's menu. Put
    /// where the game looks for it again and again: ZNetScene (needed to load a saved one) and ObjectDB wake in either order, and the game swaps
    /// its lists when a world loads (see docs/modding-pitfalls.md).
    /// </summary>
    internal static class Things
    {
        internal const string StandPrefab = "dh_arena_standard";
        internal const float Height = 5.9f;

        private static GameObject _holder, _stand;
        private static bool _built;

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static GameObject StandObject => _stand;

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;   // (nothing to copy yet: asked again when it wakes)
            if (_holder == null)
            {
                _holder = new GameObject("ArenaPrefabs");
                _holder.SetActive(false);   // keeps the copy from waking up as a real object
                Object.DontDestroyOnLoad(_holder);
            }
            if (!_built)
            {
                _stand = BuildStand(scene);
                if (_stand == null) return;
                _built = true;
            }
            if (!scene.m_prefabs.Contains(_stand)) scene.m_prefabs.Add(_stand);
            Named(scene)[_stand.name.GetStableHashCode()] = _stand;   // lets it be placed and loaded from saves
            PieceTable table = scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
            if (table != null && !table.m_pieces.Contains(_stand)) table.m_pieces.Add(_stand);
            AttachToPlaced();
        }

        /// <summary>After a reload the standards already in the world have lost the behaviour of the copy that was unloaded: each is given the new one.</summary>
        private static void AttachToPlaced()
        {
            int hash = StandPrefab.GetStableHashCode();
            foreach (ZNetView view in Object.FindObjectsOfType<ZNetView>())
            {
                if (view == null || !view.IsValid() || view.GetZDO().GetPrefab() != hash) continue;
                foreach (MonoBehaviour old in view.GetComponents<MonoBehaviour>())
                    if (old != null && old.GetType() != typeof(Stand) && old.GetType().FullName == typeof(Stand).FullName) Object.DestroyImmediate(old);
                if (view.GetComponent<Stand>() == null) view.gameObject.AddComponent<Stand>();
            }
        }

        internal static void Unregister()
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene != null && _stand != null)
            {
                scene.m_prefabs.Remove(_stand);
                int hash = _stand.name.GetStableHashCode();
                Dictionary<int, GameObject> named = Named(scene);
                if (named.TryGetValue(hash, out GameObject current) && current == _stand) named.Remove(hash);   // never another copy's entry
                PieceTable table = scene.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                table?.m_pieces.RemoveAll(p => p == null || p == _stand);
            }
            _stand = null;
            _built = false;
            if (_holder != null) Object.Destroy(_holder);
            _holder = null;
        }

        // ---- the standard --------------------------------------------------------------------------------------------

        private static Material Find(ZNetScene scene, string prefab, string material)
        {
            GameObject go = scene.GetPrefab(prefab);
            if (go == null) return null;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    if (m != null && (material == null || m.name == material)) return m;
            return null;
        }

        private static GameObject BuildStand(ZNetScene scene)
        {
            GameObject src = scene.GetPrefab("wood_pole2");
            if (src == null) { Plugin.Log.LogWarning("The wood_pole2 prefab was not found, so there is no Arena Standard"); return null; }
            GameObject go = Object.Instantiate(src, _holder.transform);
            go.name = StandPrefab;
            int layer = LayerMask.NameToLayer("piece");
            if (layer < 0) layer = go.layer;
            // the look, in a try: should a model or material the game no longer has break it, the piece is still registered (plain), so the
            // host never deletes the ones already standing (docs/modding-pitfalls.md)
            try
            {
                Material wood = Plain(scene, new Color(0.42f, 0.28f, 0.16f), 0.05f), woodDark = Plain(scene, new Color(0.27f, 0.18f, 0.1f), 0.05f),
                         stone = Plain(scene, new Color(0.5f, 0.49f, 0.46f), 0.05f), stoneDark = Plain(scene, new Color(0.36f, 0.35f, 0.33f), 0.05f),
                         iron = Plain(scene, new Color(0.3f, 0.3f, 0.32f), 0.75f), steel = Plain(scene, new Color(0.75f, 0.76f, 0.8f), 0.85f),
                         cloth = Plain(scene, new Color(0.6f, 0.08f, 0.07f), 0f), blue = Plain(scene, new Color(0.13f, 0.23f, 0.42f), 0f),
                         gold = Plain(scene, new Color(0.88f, 0.68f, 0.22f), 0.65f), bone = Plain(scene, new Color(0.86f, 0.82f, 0.7f), 0f),
                         black = Plain(scene, new Color(0.04f, 0.035f, 0.03f), 0f), ember = Fx.Glow(new Color(1f, 0.45f, 0.12f));

                foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.enabled = false;
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                Quaternion none = Quaternion.identity;

                // the game's own stone portal (from the Ashlands), smaller, its fire burning: the waystone
                if (PortalModel(scene, go, layer)) goto Done;

                // (without it: a standard made by hand) the plinth: three stone steps
                Part(go, PrimitiveType.Cylinder, "Step1", new Vector3(2.0f, 0.13f, 2.0f), new Vector3(0f, 0.13f, 0f), none, stoneDark);
                Part(go, PrimitiveType.Cylinder, "Step2", new Vector3(1.5f, 0.12f, 1.5f), new Vector3(0f, 0.37f, 0f), none, stone);
                Part(go, PrimitiveType.Cylinder, "Step3", new Vector3(0.9f, 0.1f, 0.9f), new Vector3(0f, 0.59f, 0f), none, stoneDark);

                // the pole: a thick post bound with iron, capped
                Part(go, PrimitiveType.Cylinder, "Pole", new Vector3(0.28f, 2.15f, 0.28f), new Vector3(0f, 2.84f, 0f), none, wood);
                foreach (float y in new[] { 1.2f, 2.3f, 3.4f, 4.55f }) Part(go, PrimitiveType.Cylinder, "Band", new Vector3(0.31f, 0.035f, 0.31f), new Vector3(0f, y, 0f), none, iron);
                Part(go, PrimitiveType.Cylinder, "Cap", new Vector3(0.38f, 0.06f, 0.38f), new Vector3(0f, 5.02f, 0f), none, iron);

                // the crossbar, with iron ends
                Part(go, PrimitiveType.Cube, "Crossbar", new Vector3(1.8f, 0.14f, 0.16f), new Vector3(0f, 4.3f, 0f), none, woodDark);
                foreach (float side in new[] { -1f, 1f }) Part(go, PrimitiveType.Cube, "BarEnd", new Vector3(0.18f, 0.2f, 0.2f), new Vector3(0.9f * side, 4.3f, 0f), none, iron);

                // the banner: red with gold edges, three swallowtail points, and crossed swords on a black and gold disc on both faces
                Part(go, PrimitiveType.Cube, "Banner", new Vector3(1.4f, 2.0f, 0.03f), new Vector3(0f, 3.22f, 0.12f), none, cloth);
                foreach (float side in new[] { -1f, 1f }) Part(go, PrimitiveType.Cube, "Trim", new Vector3(0.06f, 2.0f, 0.04f), new Vector3(0.7f * side, 3.22f, 0.12f), none, gold);
                Part(go, PrimitiveType.Cube, "Hem", new Vector3(1.46f, 0.08f, 0.045f), new Vector3(0f, 4.18f, 0.12f), none, gold);
                foreach (float x in new[] { -0.47f, 0f, 0.47f }) Part(go, PrimitiveType.Cube, "Tail", new Vector3(0.33f, 0.33f, 0.03f), new Vector3(x, 2.22f, 0.12f), Quaternion.Euler(0f, 0f, 45f), cloth);
                foreach (float face in new[] { 1f, -1f })
                {
                    float z = 0.12f + 0.022f * face;
                    Quaternion disc = Quaternion.Euler(90f, 0f, 0f);
                    Part(go, PrimitiveType.Cylinder, "EmblemRim", new Vector3(0.8f, 0.006f, 0.8f), new Vector3(0f, 3.35f, z), disc, gold);
                    Part(go, PrimitiveType.Cylinder, "Emblem", new Vector3(0.7f, 0.008f, 0.7f), new Vector3(0f, 3.35f, z + 0.004f * face), disc, black);
                    foreach (float s2 in new[] { -1f, 1f })
                    {
                        float a = 38f * s2 * Mathf.Deg2Rad;
                        Vector3 down = new Vector3(Mathf.Sin(a), -Mathf.Cos(a), 0f), centre = new Vector3(0f, 3.4f, z + 0.012f * face);
                        Quaternion turn = Quaternion.Euler(0f, 0f, 38f * s2);
                        Part(go, PrimitiveType.Cube, "Blade", new Vector3(0.07f, 0.8f, 0.012f), centre, turn, steel);
                        Part(go, PrimitiveType.Cube, "Guard", new Vector3(0.26f, 0.045f, 0.016f), centre + down * 0.4f, turn, gold);
                        Part(go, PrimitiveType.Cube, "Grip", new Vector3(0.045f, 0.16f, 0.014f), centre + down * 0.5f, turn, woodDark);
                        Part(go, PrimitiveType.Sphere, "Pommel", new Vector3(0.07f, 0.07f, 0.03f), centre + down * 0.6f, turn, gold);
                    }
                }

                // two round shields on the front of the pole, and two axes crossed on its back
                foreach (float side in new[] { -1f, 1f })
                {
                    Quaternion face = Quaternion.Euler(0f, 22f * side, 0f) * Quaternion.Euler(90f, 0f, 0f);
                    Vector3 at = new Vector3(0.33f * side, 1.62f, 0.2f);
                    Part(go, PrimitiveType.Cylinder, "ShieldRim", new Vector3(0.78f, 0.02f, 0.78f), at, face, iron);
                    Part(go, PrimitiveType.Cylinder, "Shield", new Vector3(0.72f, 0.03f, 0.72f), at + face * Vector3.up * 0.012f, face, side < 0f ? blue : cloth);
                    Part(go, PrimitiveType.Cube, "ShieldBand", new Vector3(0.72f, 0.035f, 0.08f), at + face * Vector3.up * 0.02f, face, gold);
                    Part(go, PrimitiveType.Sphere, "ShieldBoss", new Vector3(0.17f, 0.1f, 0.17f), at + face * Vector3.up * 0.04f, face, gold);

                    Quaternion lean = Quaternion.Euler(0f, 0f, 34f * side);
                    Part(go, PrimitiveType.Cylinder, "Haft", new Vector3(0.05f, 0.75f, 0.05f), new Vector3(0f, 2.2f, -0.22f), lean, woodDark);
                    Part(go, PrimitiveType.Cube, "AxeHead", new Vector3(0.42f, 0.34f, 0.04f), new Vector3(-0.52f * side, 2.8f, -0.22f), lean, steel);
                }

                // the horned skull on top, embers burning in its eyes
                Part(go, PrimitiveType.Sphere, "Skull", new Vector3(0.44f, 0.38f, 0.48f), new Vector3(0f, 5.3f, 0.02f), none, bone);
                Part(go, PrimitiveType.Cube, "Brow", new Vector3(0.4f, 0.08f, 0.12f), new Vector3(0f, 5.38f, 0.2f), none, bone);
                Part(go, PrimitiveType.Cube, "Snout", new Vector3(0.24f, 0.18f, 0.24f), new Vector3(0f, 5.17f, 0.21f), none, bone);
                foreach (float side in new[] { -1f, 1f })
                {
                    Part(go, PrimitiveType.Sphere, "Socket", new Vector3(0.12f, 0.11f, 0.06f), new Vector3(0.11f * side, 5.31f, 0.235f), none, black);
                    Part(go, PrimitiveType.Sphere, "Ember", new Vector3(0.05f, 0.05f, 0.03f), new Vector3(0.11f * side, 5.31f, 0.26f), none, ember);
                    Part(go, PrimitiveType.Sphere, "Nostril", new Vector3(0.04f, 0.05f, 0.03f), new Vector3(0.04f * side, 5.2f, 0.335f), none, black);
                    Part(go, PrimitiveType.Cube, "Tooth", new Vector3(0.04f, 0.07f, 0.03f), new Vector3(0.06f * side, 5.06f, 0.31f), none, bone);
                    Part(go, PrimitiveType.Cylinder, "Horn1", new Vector3(0.11f, 0.13f, 0.11f), new Vector3(0.27f * side, 5.38f, 0f), Quaternion.Euler(0f, 0f, -70f * side), bone);
                    Part(go, PrimitiveType.Cylinder, "Horn2", new Vector3(0.085f, 0.12f, 0.085f), new Vector3(0.45f * side, 5.53f, 0.02f), Quaternion.Euler(0f, 0f, -40f * side), bone);
                    Part(go, PrimitiveType.Cylinder, "Horn3", new Vector3(0.06f, 0.11f, 0.06f), new Vector3(0.55f * side, 5.73f, 0.04f), Quaternion.Euler(0f, 0f, -10f * side), bone);
                    Part(go, PrimitiveType.Sphere, "HornTip", new Vector3(0.06f, 0.08f, 0.06f), new Vector3(0.565f * side, 5.86f, 0.05f), none, bone);
                }

                // two braziers on the bottom step, burning
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 b = new Vector3(0.72f * side, 0f, 0.42f);
                    Part(go, PrimitiveType.Cylinder, "BrazierStand", new Vector3(0.07f, 0.42f, 0.07f), b + new Vector3(0f, 0.68f, 0f), none, iron);
                    Part(go, PrimitiveType.Cylinder, "BrazierFoot", new Vector3(0.26f, 0.03f, 0.26f), b + new Vector3(0f, 0.28f, 0f), none, iron);
                    Part(go, PrimitiveType.Cylinder, "Bowl", new Vector3(0.4f, 0.07f, 0.4f), b + new Vector3(0f, 1.12f, 0f), none, iron);
                    Part(go, PrimitiveType.Cylinder, "BowlRim", new Vector3(0.45f, 0.025f, 0.45f), b + new Vector3(0f, 1.19f, 0f), none, gold);
                    Part(go, PrimitiveType.Sphere, "Coals", new Vector3(0.33f, 0.12f, 0.33f), b + new Vector3(0f, 1.19f, 0f), none, ember);
                    Fx.Flame(go.transform, b + new Vector3(0f, 1.24f, 0f), 1f, true);
                }

                // hit boxes: the steps, the pole and the banner
                Hitbox(go, layer, new Vector3(0f, 0.35f, 0f), new Vector3(2.0f, 0.7f, 2.0f));
                Hitbox(go, layer, new Vector3(0f, 3.0f, 0f), new Vector3(0.5f, 4.6f, 0.5f));
                Hitbox(go, layer, new Vector3(0f, 3.25f, 0.12f), new Vector3(1.5f, 2.2f, 0.2f));
                foreach (Transform child in go.transform) if (child.GetComponent<MeshRenderer>() != null) child.gameObject.layer = layer;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("The Arena Waystone's look could not be made (it is plain, but works): " + e.Message);
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;   // (at least the pole shows)
            }
        Done:
            // (something to aim at and stand on, whatever became of the look)
            if (go.GetComponentInChildren<Collider>(true) == null) Hitbox(go, layer, new Vector3(0f, 1.5f, 0f), new Vector3(1.2f, 3f, 1.2f));

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "Arena Waystone";
            piece.m_description = "A waystone of the Arena, with burning braziers. Build it at home and press E to travel to the Arena's forecourt, where the Arena Master takes your name; the waystone there brings you back.";
            piece.m_icon = Icon();
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_resources = new[] { Req(scene, "Wood", 20), Req(scene, "Stone", 12), Req(scene, "Flint", 6), Req(scene, "LeatherScraps", 6), Req(scene, "Resin", 6) }.Where(r => r.m_resItem != null).ToArray();
            go.AddComponent<Stand>();
            return go;
        }

        internal const float PortalScale = 0.55f;

        /// <summary>
        /// The waystone's look: the stone portal's arch, its burning swirl (fire, runes and the light and hum that go with them) and its
        /// solid shape, copied from the game's own portal and made smaller. Nothing of the portal's behaviour comes with it.
        /// </summary>
        private static bool PortalModel(ZNetScene scene, GameObject go, int layer)
        {
            GameObject portal = scene.GetPrefab("portal_stone");
            if (portal == null) return false;
            bool any = false;
            foreach (string part in new[] { "New", "_target_found_red", "Mesh collider", "floor collider" })
            {
                Transform src = portal.transform.Find(part);
                if (src == null) continue;
                GameObject copy = Object.Instantiate(src.gameObject, go.transform, false);
                copy.name = part;
                copy.transform.localPosition = src.localPosition * PortalScale;
                copy.transform.localRotation = src.localRotation;
                copy.transform.localScale = src.localScale * PortalScale;
                // the swirl is shown by the portal when it is linked: here it is always on, so its fade (which starts it hidden) goes
                foreach (EffectFade fade in copy.GetComponentsInChildren<EffectFade>(true)) Object.DestroyImmediate(fade);
                foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true)) { ParticleSystem.MainModule main = ps.main; main.playOnAwake = true; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) if (t.GetComponent<Collider>() != null || t.GetComponent<MeshRenderer>() != null) t.gameObject.layer = layer;
                if (part == "New") any = true;
            }
            return any;
        }

        /// <summary>A flat colour: the game's wood item material (a Standard one) with its picture taken off, as Quad's Cigars does.</summary>
        internal static Material Plain(ZNetScene scene, Color color, float metal, bool twoSided = false)
        {
            Material basis = Find(scene, "Wood", "wood_item") ?? new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default"));
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

        private static void Hitbox(GameObject go, int layer, Vector3 centre, Vector3 size)
        {
            var hit = new GameObject("Hitbox") { layer = layer };
            hit.transform.SetParent(go.transform, false);
            var box = hit.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
        }

        private static Piece.Requirement Req(ZNetScene scene, string item, int amount) =>
            new Piece.Requirement { m_resItem = scene.GetPrefab(item)?.GetComponent<ItemDrop>(), m_amount = amount, m_recover = true };

        /// <summary>The build menu's picture: a red banner on a post with a skull on top.</summary>
        private static Sprite Icon()
        {
            const int n = 96;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) tex.SetPixel(x, y, clear);
            Color brown = new Color(0.45f, 0.30f, 0.17f), red = new Color(0.7f, 0.12f, 0.1f), bone = new Color(0.9f, 0.86f, 0.74f), gold = new Color(0.95f, 0.75f, 0.25f), grey = new Color(0.5f, 0.5f, 0.55f);
            void Fill(int x0, int y0, int x1, int y1, Color c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) tex.SetPixel(x, y, c); }
            Fill(36, 4, 60, 10, grey);       // the plinth
            Fill(44, 10, 52, 74, brown);     // the post
            Fill(22, 62, 74, 68, brown);     // the crossbar
            Fill(26, 26, 70, 62, red);       // the banner
            Fill(26, 26, 70, 31, gold);      // its hem
            for (int y = 70; y <= 84; y++) for (int x = 40; x <= 56; x++) if ((x - 48) * (x - 48) + (y - 77) * (y - 77) <= 49) tex.SetPixel(x, y, bone);   // the skull
            Fill(36, 82, 39, 90, bone); Fill(57, 82, 60, 90, bone);   // its horns
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
