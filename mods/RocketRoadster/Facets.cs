using System.Collections.Generic;
using UnityEngine;

namespace RocketRoadster
{
    /// <summary>
    /// Hard-edged shapes for the roadster's angular body, the folded-metal look of the real car (VehicleMesh's lofts are smooth, for a
    /// sculpted quad). A hull is a series of cross-sections along z; each section is a half outline on the right (x from 0 out and back to 0,
    /// from the bottom middle round the side to the top middle), mirrored to the left. Every face between two sections is flat and keeps its
    /// own corners, so each fold catches the light on its own. Units are metres, +z forward, y up.
    /// </summary>
    internal static class Facets
    {
        /// <summary>One cross-section: where along z, and its right half outline from the bottom middle (x = 0) round to the top middle (x = 0).</summary>
        internal struct Section
        {
            public float Z;
            public Vector2[] Half;
            public Section(float z, params Vector2[] half) { Z = z; Half = half; }
        }

        /// <summary>A body section from five (height, half width) pairs: the bottom, the lower side, the fender's step out over the wheels, the shoulder and the top.</summary>
        internal static Section Body(float z, float bottom, float wBottom, float lowY, float wLow, float stepY, float wStep, float shoulderY, float wShoulder, float topY, float wTop) =>
            new Section(z, new Vector2(0f, bottom), new Vector2(wBottom, bottom), new Vector2(wLow, lowY), new Vector2(wStep, stepY),
                        new Vector2(wShoulder, shoulderY), new Vector2(wTop, topY), new Vector2(0f, topY));

        /// <summary>A plain section: bottom and top, with a half width at the bottom, at the waist and at the top (a slab with chamfered corners).</summary>
        internal static Section Slab(float z, float bottom, float top, float wBottom, float wWaist, float wTop, float waistY = -1f)
        {
            if (waistY < 0f) waistY = (bottom + top) * 0.5f;
            return new Section(z, new Vector2(0f, bottom), new Vector2(wBottom, bottom), new Vector2(wWaist, waistY), new Vector2(wTop, top), new Vector2(0f, top));
        }

        /// <summary>The hull through the sections (all with the same number of points, in order of z either way), its ends closed.</summary>
        internal static Mesh Hull(Section[] sections, bool capFirst = true, bool capLast = true, bool mirror = true)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            var rings = new List<Vector3[]>();
            foreach (Section s in sections) rings.Add(mirror ? Ring(s) : Loop(s));
            float zMid = (sections[0].Z + sections[sections.Length - 1].Z) * 0.5f;

            for (int k = 0; k < rings.Count - 1; k++)
            {
                Vector3[] a = rings[k], b = rings[k + 1];
                Vector3 axisA = Centre(a), axisB = Centre(b);
                for (int j = 0; j < a.Length; j++)
                {
                    int j2 = (j + 1) % a.Length;
                    Vector3 mid = (a[j] + a[j2] + b[j] + b[j2]) * 0.25f;
                    Vector3 outward = mid - (axisA + axisB) * 0.5f;
                    outward.z = 0f;
                    if (outward.sqrMagnitude < 1e-8f) outward = new Vector3(0f, 0f, mid.z - zMid);
                    Quad(verts, norms, tris, a[j], a[j2], b[j2], b[j], outward);
                }
            }
            if (capFirst) Cap(verts, norms, tris, rings[0], sections[0].Z < sections[sections.Length - 1].Z ? -1f : 1f);
            if (capLast) Cap(verts, norms, tris, rings[rings.Count - 1], sections[0].Z < sections[sections.Length - 1].Z ? 1f : -1f);

            var m = new Mesh { name = "dh_roadster_hull" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The whole outline of a section: the right half, then the left half mirrored back down (the middle points once).</summary>
        private static Vector3[] Ring(Section s)
        {
            int n = s.Half.Length;
            var ring = new List<Vector3>();
            for (int i = 0; i < n; i++) ring.Add(new Vector3(s.Half[i].x, s.Half[i].y, s.Z));
            for (int i = n - 2; i >= 1; i--) ring.Add(new Vector3(-s.Half[i].x, s.Half[i].y, s.Z));
            return ring.ToArray();
        }

        /// <summary>A section's points as they are, a closed loop (a one-sided part such as a door: no mirroring).</summary>
        private static Vector3[] Loop(Section s)
        {
            var ring = new Vector3[s.Half.Length];
            for (int i = 0; i < ring.Length; i++) ring[i] = new Vector3(s.Half[i].x, s.Half[i].y, s.Z);
            return ring;
        }

        private static Vector3 Centre(Vector3[] ring)
        {
            Vector3 c = Vector3.zero;
            foreach (Vector3 p in ring) c += p;
            return c / ring.Length;
        }

        /// <summary>A flat four-cornered face (two triangles with their own corners), turned to face <paramref name="outward"/>.</summary>
        private static void Quad(List<Vector3> verts, List<Vector3> norms, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
            if (n.sqrMagnitude < 1e-12f) return;   // (a face pinched to nothing)
            n.Normalize();
            bool flip = Vector3.Dot(n, outward) < 0f;
            if (flip) n = -n;
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            // (the winding agrees with the normal the way VehicleMesh's Tri does it)
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        /// <summary>A flat end: a fan from the outline's middle, facing along z (<paramref name="dir"/>: +1 or -1).</summary>
        private static void Cap(List<Vector3> verts, List<Vector3> norms, List<int> tris, Vector3[] ring, float dir)
        {
            Vector3 n = new Vector3(0f, 0f, dir);
            int centre = verts.Count;
            verts.Add(Centre(ring)); norms.Add(n);
            int first = verts.Count;
            foreach (Vector3 p in ring) { verts.Add(p); norms.Add(n); }
            for (int j = 0; j < ring.Length; j++)
            {
                int a = centre, b = first + j, c = first + (j + 1) % ring.Length;
                Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (Vector3.Dot(cross, n) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
                else { tris.Add(a); tris.Add(c); tris.Add(b); }
            }
        }

        /// <summary>A flat panel (a wing, a fin): a box whose corners are given as 8 points, bottom four then top four, flat shaded.</summary>
        internal static Mesh Block(Vector3[] c)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            Vector3 mid = Vector3.zero;
            foreach (Vector3 p in c) mid += p;
            mid /= 8f;
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            foreach (int[] f in faces)
            {
                Vector3 fm = (c[f[0]] + c[f[1]] + c[f[2]] + c[f[3]]) * 0.25f;
                Quad(verts, norms, tris, c[f[0]], c[f[1]], c[f[2]], c[f[3]], fm - mid);
            }
            var m = new Mesh { name = "dh_roadster_block" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
