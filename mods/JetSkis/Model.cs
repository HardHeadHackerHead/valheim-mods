using System.Collections.Generic;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// The jet ski's body, made in code. The hull is lofted from cross-sections (a V bottom, a crowned deck, a square stern, a bow that
    /// sweeps up to a point): dark wood below a band of red-stained wood studded with bronze rivets, a dark wooden rub rail along its edge,
    /// a planked deck with grip pads, and a curled prow like a longship's. On it: a sculpted leather saddle on a dark plinth, a red cowl
    /// that rises from the deck to the iron handlebars (which turn with the steering), and at the back a surtling core in an iron cage
    /// feeding a glowing jet nozzle.
    ///
    /// Shapes are gathered by material and baked into one mesh each (a handful of renderers per jet ski, however many parts). Units are
    /// metres, y up, the bow points +z (a boat's forward, unlike a building piece's). The waterline is about y = 0.
    /// </summary>
    internal static class Model
    {
        internal static readonly Vector3 SeatPoint = new Vector3(0f, 0.34f, -0.35f);   // the rider's feet-level origin: the seat animation lifts the hips about 0.5 above it, onto the saddle
        internal static readonly Vector3 PassengerPoint = new Vector3(0f, 0.33f, -0.82f);   // the passenger's, behind
        internal const string BarsName = "Bars", BodyName = "Model";

        private const float Stern = -1.35f, Bow = 1.65f;
        private const int Rings = 30;

        // ---- the hull's shape: along its length t (0 at the stern, 1 at the bow tip)
        private static float HalfWidth(float t) => t < 0.62f
            ? 0.55f * (0.9f + 0.1f * Mathf.Sin(t / 0.62f * Mathf.PI * 0.5f))
            : 0.55f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos((t - 0.62f) / 0.38f * Mathf.PI * 0.5f)), 0.7f);
        private static float Keel(float t) => -0.24f + (t > 0.55f ? 0.46f * Sq((t - 0.55f) / 0.45f) : 0f);
        private static float DeckY(float t) => 0.30f + 0.10f * t * t;
        private static float Crown(float t) => DeckY(t) + 0.05f * HalfWidth(t) / 0.55f;   // the deck is a touch higher in the middle
        private static float T(float z) => Mathf.Clamp01((z - Stern) / (Bow - Stern));
        private static float Sq(float x) => x * x;

        /// <summary>The top of the deck at a point, in the body's own space (where the rider's feet go).</summary>
        internal static float DeckHeight(float x, float z) => DeckAt(x, z);

        /// <summary>The top of the deck at a point (it rises from the gunwale to the middle).</summary>
        private static float DeckAt(float x, float z)
        {
            float t = T(z), edge = 0.9f * HalfWidth(t);
            return edge <= 0f ? DeckY(t) : Mathf.Lerp(Crown(t), DeckY(t), Mathf.Clamp01(Mathf.Abs(x) / edge));
        }

        /// <summary>One cross-section, from the left gunwale down round the keel to the right gunwale (9 points).</summary>
        private static Vector3[] Section(float t)
        {
            float z = Mathf.Lerp(Stern, Bow, t), w = HalfWidth(t), k = Keel(t), d = DeckY(t);
            var right = new[]
            {
                new Vector2(0f, k),                                   // the keel
                new Vector2(0.5f * w, k + 0.07f),                     // the V bottom
                new Vector2(0.92f * w, k + 0.15f),                    // the chine
                new Vector2(w, Mathf.Lerp(k + 0.15f, d, 0.55f)),      // the widest point of the side
                new Vector2(0.9f * w, d),                             // the gunwale
            };
            var p = new Vector3[9];
            for (int i = 0; i < 5; i++)
            {
                p[4 - i] = new Vector3(-right[i].x, right[i].y, z);
                p[4 + i] = new Vector3(right[i].x, right[i].y, z);
            }
            return p;
        }

        internal static Transform Build(Transform parent, int layer)
        {
            var body = new GameObject(BodyName) { layer = layer };
            body.transform.SetParent(parent, false);
            Transform root = body.transform;
            var parts = new Parts();
            var rings = new Vector3[Rings][];
            for (int i = 0; i < Rings; i++) rings[i] = Section(i / (float)(Rings - 1));

            Hull(parts, rings);
            Trim(parts, rings);
            Saddle(parts);
            Cowl(parts);
            Engine(parts);
            parts.Bake(root, layer);

            // the handlebars: their own object, turned by the steering (JetSki.cs)
            var bars = new GameObject(BarsName) { layer = layer };
            bars.transform.SetParent(root, false);
            bars.transform.localPosition = new Vector3(0f, 0.98f, 0.06f);
            var b = new Parts();
            b.Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.035f, 0.33f, 0.035f), new Vector3(0f, 0f, 90f), "Iron");
            foreach (float side in new[] { -1f, 1f })
            {
                b.Prim(PrimitiveType.Cylinder, new Vector3(side * 0.27f, 0f, 0f), new Vector3(0.058f, 0.075f, 0.058f), new Vector3(0f, 0f, 90f), "Leather");
                b.Prim(PrimitiveType.Sphere, new Vector3(side * 0.355f, 0f, 0f), new Vector3(0.06f, 0.06f, 0.06f), Vector3.zero, "Iron");
                b.Prim(PrimitiveType.Cylinder, new Vector3(side * 0.19f, 0f, 0f), new Vector3(0.07f, 0.008f, 0.07f), new Vector3(0f, 0f, 90f), "Bronze");
            }
            b.Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f), new Vector3(0.11f, 0.05f, 0.11f), Vector3.zero, "Dark");   // the hub
            b.Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.065f, 0f), new Vector3(0.08f, 0.006f, 0.08f), Vector3.zero, "Bronze");
            b.Bake(bars.transform, layer);

            // the core's glow on the water at night
            var light = new GameObject("CoreLight");
            light.transform.SetParent(root, false);
            light.transform.localPosition = CorePos + new Vector3(0f, 0.2f, 0f);
            Light l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.2f);
            l.range = 3f;
            l.intensity = 1.2f;
            l.shadows = LightShadows.None;
            return root;
        }

        // ---- the hull ---------------------------------------------------------------------------------------------------

        private static void Hull(Parts parts, Vector3[][] rings)
        {
            // the sides and bottom: dark wood below the widest point, the red band above it
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int i = 0; i < Rings; i++)
            {
                float u = 0f;
                for (int j = 0; j < 9; j++)
                {
                    if (j > 0) u += Vector3.Distance(rings[i][j], rings[i][j - 1]);
                    verts.Add(rings[i][j]);
                    uvs.Add(new Vector2(u, rings[i][j].z));
                }
            }
            var lower = new List<int>();
            var band = new List<int>();
            for (int i = 0; i < Rings - 1; i++)
            {
                float t = (i + 0.5f) / (Rings - 1);
                float mid = (Keel(t) + DeckY(t)) * 0.5f;
                for (int j = 0; j < 8; j++)
                {
                    int a = i * 9 + j, b = a + 1, c = a + 10, d = a + 9;
                    List<int> into = j < 1 || j > 6 ? band : lower;   // the band: gunwale to the widest point
                    Tri(verts, into, a, b, c, p => p - new Vector3(0f, mid, p.z));
                    Tri(verts, into, a, c, d, p => p - new Vector3(0f, mid, p.z));
                }
            }
            parts.Add(Finish(NewMesh(verts, uvs, lower)), Matrix4x4.identity, "Dark");
            parts.Add(Finish(NewMesh(verts, uvs, band)), Matrix4x4.identity, "Paint");

            // the deck: gunwale to gunwale, a little crowned
            verts = new List<Vector3>();
            uvs = new List<Vector2>();
            for (int i = 0; i < Rings; i++)
            {
                float t = i / (float)(Rings - 1);
                Vector3 l = rings[i][0], r = rings[i][8], c = new Vector3(0f, Crown(t), l.z);
                verts.Add(l); verts.Add(c); verts.Add(r);
                uvs.Add(new Vector2(l.x, l.z * 0.5f)); uvs.Add(new Vector2(0f, l.z * 0.5f)); uvs.Add(new Vector2(r.x, l.z * 0.5f));
            }
            var deck = new List<int>();
            for (int i = 0; i < Rings - 1; i++)
                for (int j = 0; j < 2; j++)
                {
                    int a = i * 3 + j, b = a + 1, c = a + 4, d = a + 3;
                    Tri(verts, deck, a, b, c, p => Vector3.up);
                    Tri(verts, deck, a, c, d, p => Vector3.up);
                }
            parts.Add(Finish(NewMesh(verts, uvs, deck)), Matrix4x4.identity, "Planks");

            // the square stern: a fan from its middle
            verts = new List<Vector3>(rings[0]) { new Vector3(0f, Crown(0f), Stern) };
            Vector3 centre = Vector3.zero;
            foreach (Vector3 v in verts) centre += v;
            centre /= verts.Count;
            int n = verts.Count;
            verts.Add(centre);
            uvs = new List<Vector2>();
            foreach (Vector3 v in verts) uvs.Add(new Vector2(v.x, v.y));
            var stern = new List<int>();
            for (int k = 0; k < n; k++) Tri(verts, stern, n, k, (k + 1) % n, p => Vector3.back);
            parts.Add(Finish(NewMesh(verts, uvs, stern)), Matrix4x4.identity, "Dark");
        }

        /// <summary>The rub rail, the rivets, the grip pads and the prow.</summary>
        private static void Trim(Parts parts, Vector3[][] rings)
        {
            // a rounded rail of dark wood along the gunwale, round the bow and back, and across the top of the stern
            var rail = new List<Vector3>();
            for (int i = 0; i < Rings; i++) rail.Add(OutBy(rings[i][0], 0.012f) + Vector3.down * 0.02f);
            for (int i = Rings - 2; i >= 0; i--) rail.Add(OutBy(rings[i][8], 0.012f) + Vector3.down * 0.02f);
            parts.Add(Tube(rail, i => 0.03f, 7, false), Matrix4x4.identity, "Dark");
            var transom = new List<Vector3> { rail[0] + new Vector3(0f, 0f, -0.01f), new Vector3(0f, Crown(0f) - 0.01f, Stern - 0.02f), rail[rail.Count - 1] + new Vector3(0f, 0f, -0.01f) };
            parts.Add(Tube(transom, i => 0.028f, 7, false), Matrix4x4.identity, "Dark");

            // bronze rivets along the middle of the red band, both sides
            for (int i = 2; i < Rings - 4; i += 3)
                foreach (int a in new[] { 0, 8 })
                {
                    int b = a == 0 ? 1 : 7;
                    Vector3 at = Vector3.Lerp(rings[i][a], rings[i][b], 0.5f);
                    parts.Prim(PrimitiveType.Sphere, OutBy(at, 0.02f), new Vector3(0.04f, 0.04f, 0.04f), Vector3.zero, "Bronze");
                }

            // grip pads on the deck, either side of the saddle (tilted with the deck's crown, raised clear of it)
            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 0.34f, z = -0.45f;
                float slope = Mathf.Atan2(DeckAt(0.2f, z) - DeckAt(0.45f, z), 0.25f) * Mathf.Rad2Deg;
                parts.Prim(PrimitiveType.Cube, new Vector3(x, DeckAt(x, z) + 0.014f, z), new Vector3(0.17f, 0.02f, 1.05f), new Vector3(0f, 0f, side * -slope), "Dark");
            }

            // a round shield hung on each side by the saddle, as on a longship: wood, an iron rim, a bronze boss
            foreach (float side in new[] { -1f, 1f })
            {
                var at = new Vector3(side * 0.56f, 0.12f, -0.45f);
                var axis = new Vector3(side, 0f, 0f);
                parts.Prim(PrimitiveType.Cylinder, at, new Vector3(0.34f, 0.012f, 0.34f), new Vector3(0f, 0f, 90f), "Planks");
                parts.Add(Ring(at + axis * 0.006f, 0.17f, 0.014f, axis), Matrix4x4.identity, "Iron");
                parts.Prim(PrimitiveType.Sphere, at + axis * 0.02f, new Vector3(0.04f, 0.08f, 0.08f), Vector3.zero, "Bronze");
                parts.Prim(PrimitiveType.Cube, at + axis * 0.016f, new Vector3(0.006f, 0.3f, 0.03f), Vector3.zero, "Paint");   // a painted cross
                parts.Prim(PrimitiveType.Cube, at + axis * 0.016f, new Vector3(0.006f, 0.03f, 0.3f), Vector3.zero, "Paint");
            }

            // saddlebags hung either side of the stern, a strap across the deck between them (the storage: Things.cs)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 bag = BagCentre(side);
                parts.Prim(PrimitiveType.Cube, bag, new Vector3(0.13f, 0.27f, 0.3f), Vector3.zero, "Leather");
                parts.Prim(PrimitiveType.Cylinder, bag + new Vector3(0f, -0.135f, 0f), new Vector3(0.13f, 0.15f, 0.13f), new Vector3(90f, 0f, 0f), "Leather");   // (a rounded bottom)
                parts.Prim(PrimitiveType.Cube, bag + new Vector3(side * 0.012f, 0.1f, 0f), new Vector3(0.15f, 0.09f, 0.32f), new Vector3(0f, 0f, side * -6f), "LeatherDark");   // the flap
                parts.Prim(PrimitiveType.Cube, bag + new Vector3(side * 0.077f, 0.035f, 0f), new Vector3(0.012f, 0.05f, 0.05f), Vector3.zero, "Bronze");   // its buckle
            }
            parts.Prim(PrimitiveType.Cube, new Vector3(0f, DeckAt(0f, BagZ) + 0.012f, BagZ), new Vector3(1.0f, 0.016f, 0.07f), Vector3.zero, "LeatherDark");

            // the prow: a post rising from the bow tip and curling back, as a longship's does
            var prow = new List<Vector3>
            {
                new Vector3(0f, 0.28f, 1.56f), new Vector3(0f, 0.38f, 1.665f), new Vector3(0f, 0.5f, 1.715f), new Vector3(0f, 0.62f, 1.72f),
                new Vector3(0f, 0.72f, 1.68f), new Vector3(0f, 0.78f, 1.61f), new Vector3(0f, 0.78f, 1.54f), new Vector3(0f, 0.74f, 1.5f),
                new Vector3(0f, 0.695f, 1.52f), new Vector3(0f, 0.69f, 1.565f),
            };
            parts.Add(Tube(prow, i => Mathf.Lerp(0.055f, 0.022f, i / 9f), 8, true), Matrix4x4.identity, "Dark");
            parts.Prim(PrimitiveType.Sphere, new Vector3(0f, 0.705f, 1.565f), new Vector3(0.05f, 0.05f, 0.05f), Vector3.zero, "Bronze");
        }

        /// <summary>The saddle: a rounded leather cushion, higher at the back, on a dark wooden plinth.</summary>
        private static void Saddle(Parts parts)
        {
            const float back = -1.06f, front = 0.06f;   // (long enough for two)
            var rings = new List<Vector3[]>();
            var centres = new List<Vector3>();
            const int steps = 18;
            for (int i = 0; i <= steps; i++)
            {
                float s = i / (float)steps;                                    // 0 at the back, 1 at the front
                float z = Mathf.Lerp(back, front, s);
                float end = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(2f * s - 1f), 5f)), 0.2f);   // rounded ends
                float top = 0.74f - 0.04f * s + 0.015f * Mathf.Sin(s * Mathf.PI), bottom = 0.54f;
                float rx = (0.215f - 0.03f * s) * end, ry = (top - bottom) * 0.5f * end, cy = (top + bottom) * 0.5f;
                rings.Add(SuperEllipse(new Vector3(0f, cy, z), rx, ry, 4.5f, 16));
                centres.Add(new Vector3(0f, cy, z));
            }
            parts.Add(Loft(rings, centres), Matrix4x4.identity, "Leather");
            parts.Prim(PrimitiveType.Cube, new Vector3(0f, 0.43f, -0.5f), new Vector3(0.34f, 0.26f, 0.96f), Vector3.zero, "Dark");   // (tall enough that the rider's knees sit below the hips)
        }

        /// <summary>The cowl: rises out of the deck ahead of the rider to the steering column, painted red, with a bronze strip on top.</summary>
        private static void Cowl(Parts parts)
        {
            const float back = 0.03f, front = 1.2f;
            var rings = new List<Vector3[]>();
            var centres = new List<Vector3>();
            var ridge = new List<Vector3>();
            const int steps = 20;
            for (int i = 0; i <= steps; i++)
            {
                float s = i / (float)steps;                                    // 0 at the back, 1 at the front
                float z = Mathf.Lerp(back, front, s), deck = DeckAt(0f, z);
                float round = s < 0.1f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Sq((0.1f - s) / 0.1f))) : 1f;   // the back face is rounded off
                float top = Mathf.Lerp(0.68f, deck + 0.01f, Mathf.Pow(s, 1.25f)), bottom = deck - 0.04f;
                float rx = Mathf.Lerp(0.29f, 0.1f, s) * Mathf.Lerp(0.6f, 1f, round), ry = (top - bottom) * 0.5f * round, cy = bottom + ry;   // (its foot stays on the deck)
                rings.Add(SuperEllipse(new Vector3(0f, cy, z), rx, ry, 3.2f, 18));
                centres.Add(new Vector3(0f, cy, z));
                if (s > 0.12f && s < 0.9f) ridge.Add(new Vector3(0f, cy + ry + 0.006f, z));
            }
            parts.Add(Loft(rings, centres), Matrix4x4.identity, "Paint");
            parts.Add(Tube(ridge, i => 0.018f, 6, true), Matrix4x4.identity, "Bronze");
            // the steering column, out of the cowl to the handlebars
            parts.Add(Tube(new List<Vector3> { new Vector3(0f, 0.6f, 0.16f), new Vector3(0f, 0.98f, 0.06f) }, i => 0.032f, 8, false), Matrix4x4.identity, "Iron");
            // a gauge on the cowl's back face: a bronze ring round a glowing rune
            parts.Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.63f, 0.115f), new Vector3(0.1f, 0.012f, 0.1f), new Vector3(-62f, 0f, 0f), "Bronze");
            parts.Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.632f, 0.108f), new Vector3(0.07f, 0.012f, 0.07f), new Vector3(-62f, 0f, 0f), "RuneGlow");
        }

        internal const float BagZ = -1.1f;
        internal static Vector3 BagCentre(float side) => new Vector3(side * (0.9f * HalfWidth(T(BagZ)) + 0.075f), DeckAt(0.4f, BagZ) - 0.1f, BagZ);

        private static readonly Vector3 CorePos = new Vector3(0f, 0.5f, -1.23f);

        /// <summary>The surtling core in its cage, and the jet nozzle under the stern.</summary>
        private static void Engine(Parts parts)
        {
            Vector3 c = CorePos;
            parts.Prim(PrimitiveType.Cylinder, c + new Vector3(0f, -0.155f, 0f), new Vector3(0.34f, 0.025f, 0.34f), Vector3.zero, "Iron");
            parts.Prim(PrimitiveType.Sphere, c, new Vector3(0.2f, 0.2f, 0.2f), Vector3.zero, "Glow");
            parts.Add(Ring(c + new Vector3(0f, -0.12f, 0f), 0.14f, 0.018f), Matrix4x4.identity, "Iron");
            parts.Add(Ring(c + new Vector3(0f, 0.13f, 0f), 0.11f, 0.018f), Matrix4x4.identity, "Iron");
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var bar = new List<Vector3>();   // bowed out round the core
                for (int k = 0; k <= 6; k++)
                {
                    float h = Mathf.Lerp(-0.12f, 0.13f, k / 6f), r = Mathf.Lerp(0.14f, 0.11f, k / 6f) + 0.03f * Mathf.Sin(k / 6f * Mathf.PI);
                    bar.Add(c + dir * r + Vector3.up * h);
                }
                parts.Add(Tube(bar, k => 0.012f, 5, false), Matrix4x4.identity, "Iron");
            }
            parts.Add(Tube(new List<Vector3> { c + Vector3.up * 0.13f, c + Vector3.up * 0.2f }, k => k == 0 ? 0.03f : 0.012f, 6, true), Matrix4x4.identity, "Iron");

            // the nozzle: a flared iron pipe out of the stern, a bronze ring round its mouth, the fire inside
            var nozzle = new List<Vector3> { new Vector3(0f, -0.04f, -1.25f), new Vector3(0f, -0.04f, -1.42f), new Vector3(0f, -0.04f, -1.5f) };
            parts.Add(Tube(nozzle, k => k == 2 ? 0.16f : 0.13f, 14, false), Matrix4x4.identity, "Iron");
            parts.Add(Ring(new Vector3(0f, -0.04f, -1.5f), 0.16f, 0.02f, Vector3.forward), Matrix4x4.identity, "Bronze");
            parts.Prim(PrimitiveType.Cylinder, new Vector3(0f, -0.04f, -1.47f), new Vector3(0.24f, 0.01f, 0.24f), new Vector3(90f, 0f, 0f), "Glow");
        }

        // ---- shapes ------------------------------------------------------------------------------------------------------

        /// <summary>A rounded-box cross-section round a centre (the higher the power, the squarer), starting at the bottom (the loft's seam).</summary>
        private static Vector3[] SuperEllipse(Vector3 centre, float rx, float ry, float power, int sides)
        {
            var p = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = -Mathf.PI * 0.5f + i * 2f * Mathf.PI / sides, c = Mathf.Cos(a), s = Mathf.Sin(a);
                p[i] = centre + new Vector3(rx * Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / power), ry * Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / power), 0f);
            }
            return p;
        }

        /// <summary>A surface through closed rings, each facing out from its centre; ends that shrink to a point close themselves.</summary>
        private static Mesh Loft(IList<Vector3[]> rings, IList<Vector3> centres)
        {
            int sides = rings[0].Length;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            float v = 0f;
            for (int i = 0; i < rings.Count; i++)
            {
                if (i > 0) v += Vector3.Distance(centres[i], centres[i - 1]);
                float u = 0f;
                for (int j = 0; j < sides; j++)
                {
                    if (j > 0) u += Vector3.Distance(rings[i][j], rings[i][j - 1]);
                    verts.Add(rings[i][j]);
                    uvs.Add(new Vector2(u, v));
                }
            }
            var tris = new List<int>();
            for (int i = 0; i < rings.Count - 1; i++)
            {
                Vector3 mid = (centres[i] + centres[i + 1]) * 0.5f;
                for (int j = 0; j < sides; j++)
                {
                    int a = i * sides + j, b = i * sides + (j + 1) % sides, c = b + sides, d = a + sides;
                    Tri(verts, tris, a, b, c, p => p - mid);
                    Tri(verts, tris, a, c, d, p => p - mid);
                }
            }
            return Finish(NewMesh(verts, uvs, tris));
        }

        /// <summary>A round tube along a line of points (its radius may change along it), its ends closed or open.</summary>
        private static Mesh Tube(IList<Vector3> line, System.Func<int, float> radius, int sides, bool capped)
        {
            var rings = new List<Vector3[]>();
            var centres = new List<Vector3>();
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 tangent = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                if (i == 0)
                {
                    normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                }
                normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;   // carried along the line without twisting
                Vector3 binormal = Vector3.Cross(tangent, normal);
                var ring = new Vector3[sides];
                for (int j = 0; j < sides; j++)
                {
                    float a = j * 2f * Mathf.PI / sides;
                    ring[j] = line[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * radius(i);
                }
                if (capped && i == 0) { rings.Add(Point(line[0], sides)); centres.Add(line[0] - tangent * 0.001f); }
                rings.Add(ring);
                centres.Add(line[i]);
                if (capped && i == line.Count - 1) { rings.Add(Point(line[i], sides)); centres.Add(line[i] + tangent * 0.001f); }
            }
            return Loft(rings, centres);
        }

        /// <summary>A ring of tube round a centre, lying flat (or round the given axis).</summary>
        private static Mesh Ring(Vector3 centre, float radius, float thickness, Vector3? axis = null)
        {
            Vector3 up = axis ?? Vector3.up;
            Vector3 a = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized, b = Vector3.Cross(up, a);
            var line = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float t = i * 2f * Mathf.PI / 24;
                line.Add(centre + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius);
            }
            line.Add(line[1]);   // (overlapping the start a little, so there's no gap)
            return Tube(line, i => thickness, 6, false);
        }

        private static Vector3[] Point(Vector3 p, int n)
        {
            var r = new Vector3[n];
            for (int i = 0; i < n; i++) r[i] = p;
            return r;
        }

        private static Vector3 OutBy(Vector3 p, float by) => p + new Vector3(Mathf.Sign(p.x) * by, 0f, 0f);

        /// <summary>A triangle facing outwards (away from the shape's middle, given by <paramref name="outward"/> at its centre); slivers are skipped.</summary>
        private static void Tri(List<Vector3> v, List<int> into, int a, int b, int c, System.Func<Vector3, Vector3> outward)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward((v[a] + v[b] + v[c]) / 3f)) < 0f) { int s = b; b = c; c = s; }
            into.Add(a); into.Add(b); into.Add(c);
        }

        private static Mesh NewMesh(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            return m;
        }

        private static Mesh Finish(Mesh m)
        {
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Shapes gathered by material, then baked into one mesh (and one renderer) per material.</summary>
        private sealed class Parts
        {
            private readonly Dictionary<string, List<CombineInstance>> _byMaterial = new Dictionary<string, List<CombineInstance>>();
            private static readonly Dictionary<PrimitiveType, Mesh> Primitives = new Dictionary<PrimitiveType, Mesh>();

            internal void Add(Mesh mesh, Matrix4x4 at, string material)
            {
                if (!_byMaterial.TryGetValue(material, out List<CombineInstance> list)) _byMaterial[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = at });
            }

            internal void Prim(PrimitiveType shape, Vector3 at, Vector3 scale, Vector3 euler, string material) =>
                Add(PrimitiveMesh(shape), Matrix4x4.TRS(at, Quaternion.Euler(euler), scale), material);

            private static Mesh PrimitiveMesh(PrimitiveType shape)
            {
                if (Primitives.TryGetValue(shape, out Mesh m) && m != null) return m;
                GameObject go = GameObject.CreatePrimitive(shape);   // Unity's own shape, borrowed once
                m = go.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(go);
                return Primitives[shape] = m;
            }

            internal void Bake(Transform parent, int layer)
            {
                foreach (KeyValuePair<string, List<CombineInstance>> kv in _byMaterial)
                {
                    var mesh = new Mesh { name = "dh_jetski_" + kv.Key.ToLowerInvariant() };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject(kv.Key) { layer = layer };
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = Look.Of(kv.Key);
                }
            }
        }
    }
}
