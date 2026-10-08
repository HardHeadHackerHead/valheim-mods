using System.Collections.Generic;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// Builds a model described in Models/*.cs (made by tools/modelkit). The parts of one material (and one group) are merged into a single
    /// mesh, so a plant with hundreds of leaf, vein and flower parts is a handful of objects, cheap enough to spawn all over the world.
    /// </summary>
    internal static class ModelBuilder
    {
        private static Matrix4x4 Local(object[] p) =>
            Matrix4x4.TRS(new Vector3((float)p[3], (float)p[4], (float)p[5]), Quaternion.Euler((float)p[9], (float)p[10], (float)p[11]), Vector3.one);

        private static Matrix4x4 WithScale(object[] p) =>
            Matrix4x4.TRS(new Vector3((float)p[3], (float)p[4], (float)p[5]), Quaternion.Euler((float)p[9], (float)p[10], (float)p[11]),
                          new Vector3((float)p[6], (float)p[7], (float)p[8]));

        internal static GameObject Build(Transform parent, object[][] parts, int layer, string name = "Model")
        {
            var root = new GameObject(name) { layer = layer };
            root.transform.SetParent(parent, false);
            var groups = new Dictionary<string, Transform>();
            var buckets = new Dictionary<string, List<CombineInstance>>();   // "group|material" -> shapes
            var order = new List<string>();

            foreach (object[] p in parts)
            {
                string partName = (string)p[0], shape = (string)p[1], group = (string)p[2], material = (string)p[12];
                if (shape == "Group")
                {
                    var g = new GameObject(partName) { layer = layer };
                    g.transform.SetParent(root.transform, false);
                    g.transform.localPosition = new Vector3((float)p[3], (float)p[4], (float)p[5]);
                    g.transform.localRotation = Quaternion.Euler((float)p[9], (float)p[10], (float)p[11]);
                    groups[partName] = g.transform;
                    continue;
                }
                string key = group + "|" + material;
                if (!buckets.TryGetValue(key, out List<CombineInstance> list)) { list = new List<CombineInstance>(); buckets[key] = list; order.Add(key); }
                list.Add(new CombineInstance { mesh = Meshes.Of(shape), transform = WithScale(p) });
            }

            foreach (string key in order)
            {
                int bar = key.IndexOf('|');
                string group = key.Substring(0, bar), material = key.Substring(bar + 1);
                var mesh = new Mesh { name = "dh_" + (group.Length > 0 ? group + "_" : "") + material, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(buckets[key].ToArray(), true, true);
                mesh.RecalculateBounds();
                if (!Finite(mesh.bounds))
                {
                    // Invalid points (seen once, at game start): the shapes it was made of are made again, and it is combined once more.
                    Debug.LogWarning($"[Quad's Cigars] the mesh {mesh.name} of {name} came out with invalid points: making its shapes again");
                    Meshes.Rebuild();
                    var again = new List<CombineInstance>();
                    foreach (object[] p in parts)
                        if ((string)p[1] != "Group" && (string)p[2] + "|" + (string)p[12] == key) again.Add(new CombineInstance { mesh = Meshes.Of((string)p[1]), transform = WithScale(p) });
                    mesh.Clear();
                    mesh.CombineMeshes(again.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    if (!Finite(mesh.bounds)) Debug.LogWarning($"[Quad's Cigars] the mesh {mesh.name} of {name} is still invalid after making its shapes again");
                }
                var go = new GameObject(material) { layer = layer };
                go.transform.SetParent(group.Length > 0 && groups.TryGetValue(group, out Transform gt) ? gt : root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = Look.Of(material);
            }
            return root;
        }

        private static bool Finite(Bounds b) =>
            !(float.IsNaN(b.center.x) || float.IsNaN(b.center.y) || float.IsNaN(b.center.z) || float.IsInfinity(b.extents.x) || float.IsInfinity(b.extents.y) ||
              float.IsInfinity(b.extents.z) || float.IsNaN(b.extents.x) || float.IsNaN(b.extents.y) || float.IsNaN(b.extents.z));

        /// <summary>The box a model fits in (from its parts' corners, so it needs nothing built), in the model's own space.</summary>
        internal static Bounds BoundsOf(object[][] parts)
        {
            var groups = new Dictionary<string, Matrix4x4>();
            Bounds b = default;
            bool any = false;
            foreach (object[] p in parts)
            {
                string shape = (string)p[1], group = (string)p[2];
                Matrix4x4 parent = group.Length > 0 && groups.TryGetValue(group, out Matrix4x4 gm) ? gm : Matrix4x4.identity;
                if (shape == "Group") { groups[(string)p[0]] = parent * Local(p); continue; }
                Matrix4x4 world = parent * WithScale(p);
                // unit boxes: a cube and sphere are centred and 1 across, a cylinder is 1 across and 2 tall, a leaf runs from 0 to 1 along z
                Vector3 centre = Vector3.zero, half = Vector3.one * 0.5f;
                if (shape == "Cylinder") half = new Vector3(0.5f, 1f, 0.5f);
                else if (shape == "Leaf" || shape == "Rib") { centre = new Vector3(0f, -0.03f, 0.5f); half = new Vector3(0.5f, 0.13f, 0.5f); }
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = world.MultiplyPoint3x4(centre + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z));
                    if (!any) { b = new Bounds(corner, Vector3.zero); any = true; } else b.Encapsulate(corner);
                }
            }
            return b;
        }
    }
}
