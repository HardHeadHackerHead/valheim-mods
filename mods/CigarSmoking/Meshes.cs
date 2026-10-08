using System.Collections.Generic;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The shapes models are made of: Unity's own cube, cylinder and sphere, and two made here, the tobacco "Leaf" and its "Rib" (midrib and veins).
    /// The leaf formulas are the same as in tools/modelkit/modelkit.py (mesh_leaf, mesh_rib): change both together.
    /// </summary>
    internal static class Meshes
    {
        private const int NT = 12, NU = 6;
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        internal static Mesh Of(string shape)
        {
            if (Cache.TryGetValue(shape, out Mesh m) && m != null) return m;
            switch (shape)
            {
                case "Leaf": m = Leaf(); break;
                case "Rib": m = Rib(); break;
                default:
                    PrimitiveType type = shape == "Cube" ? PrimitiveType.Cube : shape == "Cylinder" ? PrimitiveType.Cylinder : PrimitiveType.Sphere;
                    GameObject g = GameObject.CreatePrimitive(type);
                    m = g.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(g);
                    break;
            }
            Cache[shape] = m;
            return m;
        }

        /// <summary>Make the two shapes made here again (the next Of builds them anew): for a mesh that came out with invalid points.</summary>
        internal static void Rebuild()
        {
            Cache.Remove("Leaf");
            Cache.Remove("Rib");
        }

        internal static void Clear()
        {
            foreach (KeyValuePair<string, Mesh> kv in Cache)
                if (kv.Key == "Leaf" || kv.Key == "Rib") Object.Destroy(kv.Value);   // the built-in shapes are not ours to destroy
            Cache.Clear();
        }

        private static float HalfWidth(float t) => 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f))), 0.75f) * (1f - 0.3f * t);

        private static float Y(float t, float u, float lift = 0f) =>
            0.10f * (1f - Mathf.Abs(u)) - 0.16f * t * t + 0.03f * Mathf.Sin(13f * t + 2.5f * u) * Mathf.Abs(u) * Mathf.Abs(u) + lift;

        private static Vector3 OnLeaf(float t, float u, float lift) => new Vector3(u * HalfWidth(t), Y(t, u, lift), t);

        /// <summary>Adds a grid of points as a sheet seen from both sides (a leaf is paper thin).</summary>
        private static void AddGrid(List<Vector3> verts, List<int> tris, Vector3[][] grid)
        {
            int rows = grid.Length, cols = grid[0].Length;
            int front = verts.Count;
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) verts.Add(grid[i][j]);
            int back = verts.Count;
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) verts.Add(grid[i][j]);
            for (int i = 0; i < rows - 1; i++)
                for (int j = 0; j < cols - 1; j++)
                {
                    int a = i * cols + j, b = a + 1, c = a + cols + 1, d = a + cols;
                    // facing up, and the same two triangles the other way round facing down
                    tris.AddRange(new[] { front + a, front + c, front + b, front + a, front + d, front + c });
                    tris.AddRange(new[] { back + a, back + b, back + c, back + a, back + c, back + d });
                }
        }

        private static Mesh Finish(List<Vector3> verts, List<int> tris, string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Leaf()
        {
            var grid = new Vector3[NT + 1][];
            for (int i = 0; i <= NT; i++)
            {
                float t = (float)i / NT, w = HalfWidth(t);
                grid[i] = new Vector3[NU + 1];
                for (int j = 0; j <= NU; j++)
                {
                    float u = -1f + 2f * j / NU;
                    grid[i][j] = new Vector3(u * w, Y(t, u), t);
                }
            }
            var verts = new List<Vector3>(); var tris = new List<int>();
            AddGrid(verts, tris, grid);
            return Finish(verts, tris, "dh_leaf");
        }

        private static void Ribbon(List<Vector3> verts, List<int> tris, Vector3[] points, float width)
        {
            var grid = new Vector3[points.Length][];
            for (int k = 0; k < points.Length; k++)
            {
                Vector3 along = points[Mathf.Min(k + 1, points.Length - 1)] - points[Mathf.Max(k - 1, 0)];
                Vector3 side = Vector3.Cross(Vector3.up, along);
                side = side / (side.magnitude + 1e-9f) * width / 2f;
                grid[k] = new[] { points[k] - side, points[k] + side };
            }
            AddGrid(verts, tris, grid);
        }

        private static Mesh Rib()
        {
            var verts = new List<Vector3>(); var tris = new List<int>();
            var mid = new Vector3[11];
            for (int i = 0; i <= 10; i++) mid[i] = OnLeaf(i / 10f * 0.96f, 0f, 0.012f);
            Ribbon(verts, tris, mid, 0.035f);
            for (int k = 1; k <= 6; k++)
                foreach (int sgn in new[] { -1, 1 })
                {
                    float t0 = 0.08f + k * 0.12f;
                    var pts = new Vector3[5];
                    for (int s = 0; s < 5; s++) pts[s] = OnLeaf(Mathf.Min(0.97f, t0 + 0.14f * s / 4f), sgn * 0.9f * s / 4f, 0.012f);
                    Ribbon(verts, tris, pts, 0.016f);
                }
            return Finish(verts, tris, "dh_rib");
        }
    }
}
