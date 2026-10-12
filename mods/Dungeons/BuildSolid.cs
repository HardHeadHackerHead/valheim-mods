using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // Scenery parts players can't walk through. Some of the game's prefabs keep their colliders on layers characters pass (an item's
    // collider is on "item": players walked through the giant CharredCogwheel gears), and an item's collider is a small box for lying on the
    // ground, not its shape. So every scenery part (our rooms' and the mechanisms' moving parts, both made by MakePart):
    // - its solid colliders on the "item" layer (characters pass through it: the game means items to be picked up) go to "static_solid", one
    //   of the layers the game's characters stand on and bump into (Character's ground mask: Default, static_solid, Default_small, piece,
    //   terrain, blocker, vehicle). The other layers characters pass through are left alone (see PassThrough);
    // - an item gets a collider of its own shape when its own is much smaller than its mesh: its mesh when Unity can read it, else a
    //   16-sided disc for a flat round one (a gear), else a box over its mesh. Added as a child, so they move with the part (Runtime moves a
    //   mechanism's pivot and finds what a player stands on in its table of the colliders under it, made after MakePart returns).
    internal static partial class Builder
    {
        /// <summary>Layers characters pass through that a scenery part's solid collider may still be on, and should not. Only "item": the game's
        /// collision matrix (PhysicsManager, read with tools/dungeons/palette_tool.py) also lets characters through TransparentFX, Ignore
        /// Raycast, UI, effect, ghost, piece_nonsolid, character_ghost, hitbox, skybox, pathblocker, viewblock and smoke, but those are meant
        /// to be walked through (effects, helpers, the AI's path blockers, camera blockers): made solid they'd be invisible walls. An item is
        /// the one thing the game makes non-solid because it's a pickup, which as scenery it isn't.</summary>
        private static readonly string[] PassThrough = { "item" };

        /// <summary>Items that are hoops, not discs (their meshes can't be read, so their shape is told here): a ring of boxes round the rim,
        /// open in the middle (pack-starfall's armillary hoops are BarrelRings scaled up to 18 m).</summary>
        private static readonly HashSet<string> RingItems = new HashSet<string> { "BarrelRings" };

        private static HashSet<int> _passLayers;
        private static int _solidLayer = -2;
        private static Mesh _disc;

        private static int SolidLayer => _solidLayer != -2 ? _solidLayer : (_solidLayer = LayerMask.NameToLayer("static_solid"));

        private static HashSet<int> PassLayers =>
            _passLayers ?? (_passLayers = new HashSet<int>(PassThrough.Select(LayerMask.NameToLayer).Where(l => l >= 0)));

        /// <summary>A scenery part made solid (see above). item: it's one of the game's items (an ItemDrop prefab).</summary>
        private static void Solidify(GameObject go, bool item)
        {
            int solid = SolidLayer;
            if (solid < 0) return;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger && PassLayers.Contains(c.gameObject.layer)) c.gameObject.layer = solid;
            if (item) FitItemCollider(go, solid);
        }

        /// <summary>An item's own collider made to fit it when the colliders it has cover less than half its mesh's box.</summary>
        private static void FitItemCollider(GameObject go, int solid)
        {
            Transform root = go.transform;
            Bounds? shape = null;
            var filters = new List<MeshFilter>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                if (!(r is SkinnedMeshRenderer)) filters.Add(r.GetComponent<MeshFilter>());
                Bounds b = LocalBounds(root, r.transform, mesh.bounds);
                if (shape == null) shape = b; else { Bounds a = shape.Value; a.Encapsulate(b); shape = a; }
            }
            if (shape == null) return;
            Bounds local = shape.Value;

            Bounds? have = null;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || !c.enabled) continue;
                if (have == null) have = c.bounds; else { Bounds a = have.Value; a.Encapsulate(c.bounds); have = a; }
            }
            Bounds world = WorldBounds(root, local);
            if (have != null && Volume(have.Value) >= 0.5f * Volume(world)) return;

            // its own mesh, when there's one and Unity can read it
            if (filters.Count == 1 && filters[0].sharedMesh.isReadable)
            {
                MeshCollider mc = SolidChild(filters[0].transform, solid).AddComponent<MeshCollider>();
                mc.sharedMesh = filters[0].sharedMesh;
                return;
            }

            // a flat round thing (a gear, a wheel, a shield): a disc; anything else: its box
            Vector3 size = local.size;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            float t = size[thin], a1 = size[(thin + 1) % 3], a2 = size[(thin + 2) % 3];
            bool disc = t < 0.5f * Mathf.Min(a1, a2) && Mathf.Min(a1, a2) > 0.7f * Mathf.Max(a1, a2);
            GameObject child = SolidChild(root, solid);
            child.transform.localPosition = local.center;
            if (disc && RingItems.Contains(go.name))
            {
                // a hoop: 16 boxes round the rim, each a little longer than its arc, a tenth of the radius wide
                child.transform.localRotation = thin == 0 ? Quaternion.Euler(0f, 0f, 90f) : thin == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                float r = 0.25f * (a1 + a2) * 0.95f;    // (near round: the disc test above)
                const int n = 16;
                for (int i = 0; i < n; i++)
                {
                    // (a BoxCollider can't be turned on its own object: each segment is a child, its x along the radius)
                    float ang = i * 360f / n, rad = ang * Mathf.Deg2Rad;
                    var piece = new GameObject("quaddungeon_solid_rim") { layer = solid };
                    piece.transform.SetParent(child.transform, false);
                    piece.transform.localPosition = new Vector3(Mathf.Cos(rad) * r, 0f, Mathf.Sin(rad) * r);
                    piece.transform.localRotation = Quaternion.Euler(0f, -ang, 0f);
                    BoxCollider box = piece.AddComponent<BoxCollider>();
                    box.size = new Vector3(Mathf.Max(0.02f, 0.1f * r), Mathf.Max(t, 0.02f), 2f * Mathf.PI * r / n * 1.15f);
                }
                return;
            }
            if (disc)
            {
                // the unit disc lies along y: turn y onto the thin axis, and scale it to the item's box
                child.transform.localRotation = thin == 0 ? Quaternion.Euler(0f, 0f, 90f) : thin == 2 ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
                Vector3 s = thin == 0 ? new Vector3(size.y, size.x, size.z) : thin == 2 ? new Vector3(size.x, size.z, size.y) : size;
                child.transform.localScale = s;
                MeshCollider mc = child.AddComponent<MeshCollider>();
                mc.sharedMesh = Disc();
                mc.convex = true;
            }
            else
            {
                BoxCollider box = child.AddComponent<BoxCollider>();
                box.size = size;
            }
        }

        private static GameObject SolidChild(Transform parent, int layer)
        {
            var go = new GameObject("quaddungeon_solid") { layer = layer };
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>A mesh's box in root's frame (without root's own scale: a collider on a child of root gets that from root).</summary>
        private static Bounds LocalBounds(Transform root, Transform t, Bounds b)
        {
            Matrix4x4 m = root.worldToLocalMatrix * t.localToWorldMatrix;
            var out_ = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                out_.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return out_;
        }

        private static Bounds WorldBounds(Transform root, Bounds local)
        {
            Matrix4x4 m = root.localToWorldMatrix;
            var out_ = new Bounds(m.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                out_.Encapsulate(m.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return out_;
        }

        private static float Volume(Bounds b) => Mathf.Max(b.size.x, 0.01f) * Mathf.Max(b.size.y, 0.01f) * Mathf.Max(b.size.z, 0.01f);

        /// <summary>A 16-sided disc 1 across and 1 high along y, centred: made once, readable, for the convex colliders of round items.</summary>
        private static Mesh Disc()
        {
            if (_disc != null) return _disc;
            const int n = 16;
            var v = new Vector3[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float x = 0.5f * Mathf.Cos(a), z = 0.5f * Mathf.Sin(a);
                v[i] = new Vector3(x, -0.5f, z);
                v[i + n] = new Vector3(x, 0.5f, z);
            }
            var tris = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                tris.AddRange(new[] { i, i + n, j, j, i + n, j + n });             // the rim
                if (i > 0 && i < n - 1)
                {
                    tris.AddRange(new[] { 0, i + 1, i });                          // the bottom
                    tris.AddRange(new[] { n, n + i, n + i + 1 });                  // the top
                }
            }
            _disc = new Mesh { name = "quaddungeon_disc", vertices = v, triangles = tris.ToArray() };
            _disc.RecalculateBounds();
            _disc.RecalculateNormals();
            return _disc;
        }

        /// <summary>The disc mesh goes with the mod (the plugin unloading).</summary>
        private static void ForgetSolid()
        {
            if (_disc != null) Object.Destroy(_disc);
            _disc = null;
            _passLayers = null;
            _solidLayer = -2;
        }
    }
}
