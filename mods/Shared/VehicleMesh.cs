using System.Collections.Generic;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// Shapes for code-built vehicles that cubes and cylinders can't make: a rounded box, a lofted body shell (a nose, a tank, a seat: a
    /// smooth skin through cross-sections along z), a curved fender arch and a turned tyre. Meshes are made once when the piece is built
    /// (a few hundred triangles each) and have no UVs: they take the plain colours of <see cref="VehicleLook"/>.
    /// Shared source (mods/Shared/VehicleMesh.cs), linked into FourWheelers (and MountainBikes when it wants it). Units are metres, +z is
    /// forward, y up.
    /// </summary>
    internal static class VehicleMesh
    {
        /// <summary>One cross-section of a loft: where along z, how wide (half), the bottom and top, and how square (2 = an ellipse, 6 = nearly a rectangle).</summary>
        internal struct Station
        {
            public float Z, HalfW, Bottom, Top, Square, X;
            public Station(float z, float halfW, float bottom, float top, float square = 3f, float x = 0f) { Z = z; HalfW = halfW; Bottom = bottom; Top = top; Square = square; X = x; }
        }

        /// <summary>A mesh made into a visible object (a mesh filter and renderer, no collider).</summary>
        internal static Transform Add(Transform parent, string name, Mesh mesh, Material material, Vector3 at = default, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localEulerAngles = euler;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }

        // ---- rounded box ---------------------------------------------------------------------------------------------------

        /// <summary>A box with rounded edges and corners (radius r), centred on the origin.</summary>
        internal static Mesh RoundedBox(Vector3 size, float r, int seg = 3)
        {
            Vector3 h = size * 0.5f;
            r = Mathf.Max(0.001f, Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z))));
            Vector3 inner = h - Vector3.one * r;
            float[][] axes = { Axis(h.x, inner.x, seg), Axis(h.y, inner.y, seg), Axis(h.z, inner.z, seg) };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int a = 0; a < 3; a++)
                foreach (float sign in new[] { 1f, -1f })
                {
                    int u = (a + 1) % 3, v = (a + 2) % 3;
                    int baseIndex = verts.Count, nu = axes[u].Length, nv = axes[v].Length;
                    for (int i = 0; i < nu; i++)
                        for (int j = 0; j < nv; j++)
                        {
                            Vector3 p = Vector3.zero;
                            p[a] = sign * h[a]; p[u] = axes[u][i]; p[v] = axes[v][j];
                            Vector3 c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                            Vector3 d = p - c;
                            Vector3 n = d.sqrMagnitude > 1e-10f ? d.normalized : AxisVector(a, sign);
                            verts.Add(c + n * r);
                            norms.Add(n);
                        }
                    for (int i = 0; i < nu - 1; i++)
                        for (int j = 0; j < nv - 1; j++)
                        {
                            int p00 = baseIndex + i * nv + j, p01 = p00 + 1, p10 = p00 + nv, p11 = p10 + 1;
                            Tri(tris, verts, norms, p00, p10, p11);
                            Tri(tris, verts, norms, p00, p11, p01);
                        }
                }
            var m = new Mesh { name = "dh_roundedbox" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static float[] Axis(float h, float inner, int seg)
        {
            var list = new List<float>();
            for (int k = 0; k <= seg; k++) list.Add(-h + (h - inner) * k / seg);
            for (int k = 0; k <= seg; k++) list.Add(inner + (h - inner) * k / seg);
            return list.ToArray();
        }

        private static Vector3 AxisVector(int a, float sign) { var v = Vector3.zero; v[a] = sign; return v; }

        /// <summary>A triangle wound so that it faces the way its vertices' normals point.</summary>
        private static void Tri(List<int> tris, List<Vector3> verts, List<Vector3> norms, int a, int b, int c)
        {
            Vector3 cross = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Vector3.Dot(cross, norms[a] + norms[b] + norms[c]) >= 0f) { tris.Add(a); tris.Add(b); tris.Add(c); }
            else { tris.Add(a); tris.Add(c); tris.Add(b); }
        }

        // ---- loft ------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A smooth skin through cross-sections along z (a nose, a tank, a seat, a body): each station is a rounded rectangle of its own
        /// width, bottom and top; the ends are closed. Stations must be in order of z.
        /// </summary>
        internal static Mesh Loft(Station[] st, int ring = 20, int smooth = 3)
        {
            st = Smooth(st, smooth);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s < st.Length; s++)
            {
                Station t = st[s];
                float cy = (t.Top + t.Bottom) * 0.5f, hy = Mathf.Max(0.001f, (t.Top - t.Bottom) * 0.5f), hx = Mathf.Max(0.001f, t.HalfW);
                float e = 2f / Mathf.Max(2f, t.Square);
                for (int j = 0; j < ring; j++)
                {
                    float ang = j * Mathf.PI * 2f / ring;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    var p = new Vector3(t.X + hx * Mathf.Sign(cs) * Mathf.Pow(Mathf.Abs(cs), e), cy + hy * Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), e), t.Z);
                    verts.Add(p);
                    norms.Add(new Vector3(p.x - t.X, p.y - cy, 0f).normalized);
                }
            }
            for (int s = 0; s < st.Length - 1; s++)
                for (int j = 0; j < ring; j++)
                {
                    int a = s * ring + j, b = s * ring + (j + 1) % ring, c = (s + 1) * ring + j, d = (s + 1) * ring + (j + 1) % ring;
                    Tri(tris, verts, norms, a, c, d);
                    Tri(tris, verts, norms, a, d, b);
                }
            // the two ends: a flat cap each (its own vertices, so its edge stays crisp)
            foreach (int s in new[] { 0, st.Length - 1 })
            {
                float dir = s == 0 ? -1f : 1f;
                Station t = st[s];
                int centre = verts.Count;
                verts.Add(new Vector3(t.X, (t.Top + t.Bottom) * 0.5f, t.Z)); norms.Add(new Vector3(0f, 0f, dir));
                int first = verts.Count;
                for (int j = 0; j < ring; j++) { verts.Add(verts[s * ring + j]); norms.Add(new Vector3(0f, 0f, dir)); }
                for (int j = 0; j < ring; j++) Tri(tris, verts, norms, centre, first + j, first + (j + 1) % ring);
            }
            var m = new Mesh { name = "dh_loft" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals();   // (the true surface, so a waist or a bulge shades right; the caps have their own vertices and stay flat)
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Extra stations between each pair (Catmull-Rom through the given ones), so a loft curves smoothly instead of creasing at each station.</summary>
        private static Station[] Smooth(Station[] st, int per)
        {
            if (per <= 1 || st.Length < 3) return st;
            var list = new List<Station>();
            for (int i = 0; i < st.Length - 1; i++)
            {
                Station p0 = st[Mathf.Max(0, i - 1)], p1 = st[i], p2 = st[i + 1], p3 = st[Mathf.Min(st.Length - 1, i + 2)];
                for (int k = 0; k < per; k++)
                {
                    float t = k / (float)per;
                    list.Add(new Station(CR(p0.Z, p1.Z, p2.Z, p3.Z, t), Mathf.Max(0.001f, CR(p0.HalfW, p1.HalfW, p2.HalfW, p3.HalfW, t)),
                                         CR(p0.Bottom, p1.Bottom, p2.Bottom, p3.Bottom, t), CR(p0.Top, p1.Top, p2.Top, p3.Top, t),
                                         Mathf.Max(2f, CR(p0.Square, p1.Square, p2.Square, p3.Square, t)), CR(p0.X, p1.X, p2.X, p3.X, t)));
                }
            }
            list.Add(st[st.Length - 1]);
            return list.ToArray();
        }

        private static float CR(float a, float b, float c, float d, float t) =>
            0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (-a + 3f * b - 3f * c + d) * t * t * t);

        // ---- fender arch -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// A curved fender over a wheel: a thin shell swept round the x axis through the origin (put it at the wheel's hub), from angle a0
        /// to a1 in degrees (0 forward, 90 straight up, 180 back), across a width, with a slight crown and a flange that turns down on the
        /// outside edge (outer: +1 or -1, the side of the vehicle it is on). Flat shaded, like the rest of the low-poly look.
        /// </summary>
        internal static Mesh Arch(float radius, float width, float a0, float a1, float thickness = 0.025f, float crown = 0.03f, float outer = 1f, float flange = 0.1f, int seg = 14)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            const int across = 5;
            Vector3 Pt(float ang, float x, float r) => new Vector3(x, Mathf.Sin(ang * Mathf.Deg2Rad) * r, Mathf.Cos(ang * Mathf.Deg2Rad) * r);
            float R(float x, float inset) => radius + inset + crown * (1f - Mathf.Pow(2f * x / width, 2f));
            // outer and inner skins
            for (int skin = 0; skin < 2; skin++)
            {
                float inset = skin == 0 ? thickness : 0f;
                for (int i = 0; i < seg; i++)
                {
                    float ang0 = Mathf.Lerp(a0, a1, i / (float)seg), ang1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                    for (int k = 0; k < across; k++)
                    {
                        float x0 = Mathf.Lerp(-width * 0.5f, width * 0.5f, k / (float)across), x1 = Mathf.Lerp(-width * 0.5f, width * 0.5f, (k + 1) / (float)across);
                        Vector3 p00 = Pt(ang0, x0, R(x0, inset)), p01 = Pt(ang0, x1, R(x1, inset)), p10 = Pt(ang1, x0, R(x0, inset)), p11 = Pt(ang1, x1, R(x1, inset));
                        float sgn = skin == 0 ? 1f : -1f;
                        Vector3 outward = Pt((ang0 + ang1) * 0.5f, 0f, 1f) * sgn;
                        QuadSmooth(verts, norms, tris, p00, p10, p11, p01, outward, Pt(ang0, 0f, 1f) * sgn, Pt(ang1, 0f, 1f) * sgn);
                    }
                }
            }
            // the flange on the outside edge, turned down toward the axle, and the thin edges
            float xe = outer * width * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float ang0 = Mathf.Lerp(a0, a1, i / (float)seg), ang1 = Mathf.Lerp(a0, a1, (i + 1) / (float)seg);
                float r = R(xe, 0f);
                Vector3 top0 = Pt(ang0, xe, r + thickness), top1 = Pt(ang1, xe, r + thickness);
                Vector3 low0 = Pt(ang0, xe, r - flange), low1 = Pt(ang1, xe, r - flange);
                Vector3 outx = new Vector3(outer, 0f, 0f);
                Quad(verts, norms, tris, top0, low0, low1, top1, outx);
                Quad(verts, norms, tris, top0, low0, low1, top1, -outx);   // (seen from inside too)
            }
            var m = new Mesh { name = "dh_arch" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A quad (a, b, c, d) whose edges a-d and b-c carry the given normals (n0 at a and d, n1 at b and c): smooth along the sweep.</summary>
        private static void QuadSmooth(List<Vector3> verts, List<Vector3> norms, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Vector3 n0, Vector3 n1)
        {
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n0); norms.Add(n1); norms.Add(n1); norms.Add(n0);
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        private static void Quad(List<Vector3> verts, List<Vector3> norms, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            bool flip = Vector3.Dot(n, outward) < 0f;
            Vector3 nn = (flip ? -n : n).normalized;
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++) norms.Add(nn);
            if (!flip) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }

        // ---- tube --------------------------------------------------------------------------------------------------------------

        /// <summary>A tapering tube (a frame tube, a fork leg) along +z from 0 to length, radius r0 at the start and r1 at the end, closed at both ends.</summary>
        internal static Mesh Tube(float length, float r0, float r1, int sides = 10)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float slope = (r0 - r1) / Mathf.Max(0.001f, length);   // (how much the wall leans: the normal tilts toward +z as it narrows)
            for (int ring = 0; ring < 2; ring++)
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    verts.Add(dir * (ring == 0 ? r0 : r1) + new Vector3(0f, 0f, ring == 0 ? 0f : length));
                    norms.Add((dir + new Vector3(0f, 0f, slope)).normalized);
                }
            for (int j = 0; j < sides; j++)
            {
                int a = j, b = (j + 1) % sides, c = sides + j, d = sides + (j + 1) % sides;
                Tri(tris, verts, norms, a, c, d);
                Tri(tris, verts, norms, a, d, b);
            }
            foreach (float z in new[] { 0f, length })
            {
                float dirZ = z == 0f ? -1f : 1f, r = z == 0f ? r0 : r1;
                int centre = verts.Count;
                verts.Add(new Vector3(0f, 0f, z)); norms.Add(new Vector3(0f, 0f, dirZ));
                int first = verts.Count;
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z)); norms.Add(new Vector3(0f, 0f, dirZ));
                }
                for (int j = 0; j < sides; j++) Tri(tris, verts, norms, centre, first + j, first + (j + 1) % sides);
            }
            var m = new Mesh { name = "dh_tube" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ---- tyre --------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// A tyre turned on a lathe, round the x axis: outer radius R, width w, down to the rim at rimR, with rounded shoulders and a
        /// slightly bulging sidewall (what makes it look like rubber and not a drum).
        /// </summary>
        internal static Mesh Tyre(float R, float w, float rimR, int around = 32)
        {
            float hw = w * 0.5f, shoulder = Mathf.Min(w * 0.3f, (R - rimR) * 0.6f);
            var profile = new List<Vector2>();   // (x along the axle, radius)
            profile.Add(new Vector2(-hw * 0.82f, rimR));
            profile.Add(new Vector2(-hw * 1.0f, rimR + (R - rimR) * 0.3f));
            profile.Add(new Vector2(-hw * 1.0f, R - shoulder * 1.2f));
            profile.Add(new Vector2(-hw * 0.86f, R - shoulder * 0.45f));
            profile.Add(new Vector2(-hw * 0.6f, R - shoulder * 0.08f));
            profile.Add(new Vector2(-hw * 0.3f, R));
            profile.Add(new Vector2(hw * 0.3f, R));
            profile.Add(new Vector2(hw * 0.6f, R - shoulder * 0.08f));
            profile.Add(new Vector2(hw * 0.86f, R - shoulder * 0.45f));
            profile.Add(new Vector2(hw * 1.0f, R - shoulder * 1.2f));
            profile.Add(new Vector2(hw * 1.0f, rimR + (R - rimR) * 0.3f));
            profile.Add(new Vector2(hw * 0.82f, rimR));
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            int np = profile.Count;
            for (int i = 0; i < np; i++)
                for (int j = 0; j < around; j++)
                {
                    float phi = j * Mathf.PI * 2f / around;
                    Vector3 radial = new Vector3(0f, Mathf.Cos(phi), Mathf.Sin(phi));
                    verts.Add(new Vector3(profile[i].x, 0f, 0f) + radial * profile[i].y);
                    // outward from the tyre's own cross-section centre (smooth, and right for a round section)
                    Vector2 mid = new Vector2(0f, (R + rimR) * 0.5f);
                    Vector2 o = (profile[i] - mid);
                    o = new Vector2(o.x / Mathf.Max(0.01f, hw), o.y / Mathf.Max(0.01f, (R - rimR) * 0.5f)).normalized;
                    norms.Add(new Vector3(o.x, 0f, 0f) + radial * o.y);
                }
            for (int i = 0; i < np - 1; i++)
                for (int j = 0; j < around; j++)
                {
                    int a = i * around + j, b = i * around + (j + 1) % around, c = (i + 1) * around + j, d = (i + 1) * around + (j + 1) % around;
                    Tri(tris, verts, norms, a, c, d);
                    Tri(tris, verts, norms, a, d, b);
                }
            var m = new Mesh { name = "dh_tyre" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
