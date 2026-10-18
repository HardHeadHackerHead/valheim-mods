using System.Collections.Generic;
using DHack.Shared;
using UnityEngine;
using static RocketRoadster.Facets;

namespace RocketRoadster
{
    /// <summary>
    /// The roadster's body, made in code after the photos of the 2026 reveal: a low white wedge of flat folded panels, a long bronze-tinted
    /// glass canopy from the windscreen down onto the rear deck, scissor doors (they lift up and forward from a hinge at the front), a thin
    /// white light bar across the nose and a full-width red one across the tail, a black diffuser with four bronze thruster nozzles under it,
    /// a tall rear wing, black six-spoke wheels with white aero inserts, and inside two white bucket seats, a yoke and a screen.
    ///
    /// About 1.1 times the real car (Valheim's people are tall): 5 m long, 2.2 m wide, 1.5 m high. Parts named paint* take the paint colour,
    /// lens_head and lens_tail light up (GroundVehicle), door_l and door_r swing (Roadster), and the thruster nozzles are marked jet_* (Jets).
    /// Units are metres, y up, +z forward; the wheels' hubs sit at the tyres' radius so they touch the ground at rest.
    /// </summary>
    internal static class Model
    {
        internal const float WheelRadius = 0.42f, WheelWidth = 0.34f;
        private const float HubX = 0.9f, HubZ = 1.55f;
        internal static readonly Vector3[] Wheels =
        {
            new Vector3(-HubX, WheelRadius, HubZ), new Vector3(HubX, WheelRadius, HubZ),     // front (steered)
            new Vector3(-HubX, WheelRadius, -HubZ), new Vector3(HubX, WheelRadius, -HubZ),   // back
        };
        // the driver sits on the left, the passenger on the right (the seat animation lifts the hips about half a metre over these points)
        internal static readonly Vector3 SeatPoint = new Vector3(-0.42f, 0.0f, -0.32f);
        internal static readonly Vector3 PassengerPoint = new Vector3(0.42f, 0.0f, -0.32f);

        /// <summary>Where the thrusters are and which way their gas goes (Jets.cs). Lift jets point down; the four at the back point back.</summary>
        internal static readonly Vector3[] LiftJets =
        {
            new Vector3(-0.78f, 0.2f, 2.15f), new Vector3(0.78f, 0.2f, 2.15f),
            new Vector3(-1.0f, 0.22f, 0f), new Vector3(1.0f, 0.22f, 0f),
            new Vector3(-0.78f, 0.24f, -2.15f), new Vector3(0.78f, 0.24f, -2.15f),
        };
        /// <summary>Attitude jets that blow straight up, on the fenders over each wheel (they push that corner down).</summary>
        internal static readonly Vector3[] TopJets =
        {
            new Vector3(-0.88f, 0.895f, HubZ), new Vector3(0.88f, 0.895f, HubZ), new Vector3(-0.92f, 0.99f, -HubZ), new Vector3(0.92f, 0.99f, -HubZ),
        };
        /// <summary>How far out from each wheel's centre its side jet is (in the hub, not turning with the wheel: it blows outward).</summary>
        internal const float SideJetOut = 0.21f;
        /// <summary>Roughly its centre of mass, for which way each jet turns it.</summary>
        internal static readonly Vector3 Centre = new Vector3(0f, 0.55f, 0f);

        internal static readonly Vector3[] RearJets =
        {
            new Vector3(-0.62f, 0.3f, -2.62f), new Vector3(-0.22f, 0.3f, -2.62f), new Vector3(0.22f, 0.3f, -2.62f), new Vector3(0.62f, 0.3f, -2.62f),
        };

        internal static Transform Marker(Transform root, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.localPosition = at;
            return t;
        }

        private static Light Lamp(Transform parent, string name, Vector3 at, Vector3 aim, LightType type, float range, float angle, float intensity, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(aim);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.range = range;
            l.spotAngle = angle;
            l.intensity = intensity;
            l.color = colour;
            l.shadows = LightShadows.None;
            l.enabled = false;   // (GroundVehicle switches them on at night while it is ridden)
            return l;
        }

        internal static void Build(Transform root)
        {
            Material white = Look.Of("White"), black = Look.Of("Black"), glass = Look.Of("Glass"), bronze = Look.Of("Bronze");
            Material seat = Look.Of("SeatWhite"), dark = Look.Of("Dark"), tyre = Look.Of("Tyre");
            Material lensHead = Look.Of("LensHead"), lensTail = Look.Of("LensTail"), plate = Look.Of("Plate");
            var body = new GameObject("Model").transform;   // (the build menu's picture is drawn from "Model")
            body.SetParent(root, false);
            Transform Mesh(Transform parent, string n, Mesh mesh, Material m, Vector3 at = default, Vector3 euler = default) => VehicleMesh.Add(parent, n, mesh, m, at, euler);
            Transform Box(Transform parent, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(parent, PrimitiveType.Cube, n, at, size, m, euler);
            Transform Cyl(Transform parent, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(parent, PrimitiveType.Cylinder, n, at, size, m, euler);

            // ---- the core: narrow below (the wheels sit outside it), wide above the wheels (the fenders). Seen from the side the hood is
            // one straight slope from the low sharp nose up to the windscreen; from above the nose is chamfered at its corners and the body
            // is widest over the rear wheels (the haunches).
            Mesh(body, "paint_core", Hull(new[]
            {
                Body(2.58f, 0.30f, 0.50f, 0.38f, 0.55f, 0.42f, 0.78f, 0.47f, 0.78f, 0.49f, 0.55f),
                Body(2.25f, 0.22f, 0.60f, 0.50f, 0.64f, 0.56f, 0.97f, 0.64f, 0.97f, 0.63f, 0.62f),
                Body(1.95f, 0.20f, 0.64f, 0.62f, 0.66f, 0.70f, 1.04f, 0.78f, 1.04f, 0.72f, 0.62f),
                Body(1.55f, 0.20f, 0.66f, 0.84f, 0.68f, 0.88f, 1.08f, 0.93f, 1.08f, 0.82f, 0.60f),
                Body(1.15f, 0.20f, 0.68f, 0.84f, 0.68f, 0.88f, 1.08f, 0.93f, 1.07f, 0.88f, 0.66f),
                Body(0.00f, 0.20f, 0.68f, 0.84f, 0.68f, 0.88f, 1.08f, 0.93f, 1.07f, 0.92f, 0.76f),
                Body(-0.90f, 0.20f, 0.68f, 0.84f, 0.68f, 0.88f, 1.11f, 0.98f, 1.10f, 0.95f, 0.78f),
                Body(-1.55f, 0.24f, 0.66f, 0.84f, 0.68f, 0.88f, 1.13f, 1.00f, 1.12f, 0.98f, 0.80f),
                Body(-2.20f, 0.32f, 0.62f, 0.80f, 0.64f, 0.86f, 1.10f, 0.97f, 1.09f, 0.97f, 0.82f),
                Body(-2.52f, 0.36f, 0.60f, 0.78f, 0.62f, 0.84f, 1.06f, 0.94f, 1.04f, 0.95f, 0.84f),
            }), white);

            // the nose below the fenders, full width and chamfered like them, with a black band and splitter under it, the white light bar
            // along its sharp top edge and a slit of light at each corner
            Mesh(body, "paint_nose", Hull(new[]
            {
                Slab(2.02f, 0.20f, 0.66f, 1.00f, 1.03f, 1.02f, 0.44f),
                Slab(2.36f, 0.22f, 0.55f, 0.90f, 0.92f, 0.88f, 0.40f),
                Slab(2.60f, 0.28f, 0.44f, 0.74f, 0.76f, 0.72f, 0.36f),
            }), white);
            Mesh(body, "bumper", Hull(new[] { Slab(2.10f, 0.19f, 0.43f, 0.94f, 0.96f, 0.95f), Slab(2.625f, 0.25f, 0.435f, 0.69f, 0.71f, 0.70f) }), black);
            Mesh(body, "splitter", Hull(new[] { Slab(2.10f, 0.16f, 0.22f, 0.98f, 1.0f, 0.98f), Slab(2.68f, 0.17f, 0.21f, 0.74f, 0.76f, 0.74f) }), black);
            Box(body, "lens_head", new Vector3(0f, 0.452f, 2.612f), new Vector3(1.36f, 0.024f, 0.02f), lensHead, new Vector3(-12f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Box(body, "lens_head", new Vector3(side * 0.82f, 0.5f, 2.4f), new Vector3(0.03f, 0.03f, 0.3f), lensHead, new Vector3(0f, side * -32f, 0f));

            // the tail: white under the deck with the red light bar along its top edge and the number plate, a tall black diffuser below
            Mesh(body, "paint_tail", Hull(new[]
            {
                Slab(-2.04f, 0.30f, 0.86f, 1.06f, 1.10f, 1.10f, 0.50f),
                Slab(-2.56f, 0.40f, 0.84f, 1.02f, 1.04f, 1.03f, 0.55f),
            }), white);
            Box(body, "lens_tail", new Vector3(0f, 0.815f, -2.575f), new Vector3(2.06f, 0.03f, 0.025f), lensTail);
            Box(body, "plate", new Vector3(0f, 0.64f, -2.575f), new Vector3(0.48f, 0.13f, 0.012f), plate);
            Box(body, "plate_frame", new Vector3(0f, 0.64f, -2.567f), new Vector3(0.52f, 0.16f, 0.01f), black);
            Mesh(body, "diffuser", Hull(new[] { Slab(-1.80f, 0.16f, 0.30f, 0.9f, 0.92f, 0.9f), Slab(-2.64f, 0.17f, 0.52f, 0.98f, 1.02f, 1.0f, 0.36f) }), black);
            for (int i = 0; i < 3; i++)   // (the diffuser's fins)
                Box(body, "fin", new Vector3(-0.42f + i * 0.42f, 0.2f, -2.3f), new Vector3(0.02f, 0.1f, 0.7f), black);
            foreach (float side in new[] { -1f, 1f })   // (and the tall fins at its outer corners)
                Mesh(body, "corner_fin", Facets.Block(new[]
                {
                    new Vector3(side * 0.96f, 0.15f, -2.2f), new Vector3(side * 0.99f, 0.15f, -2.2f), new Vector3(side * 0.99f, 0.15f, -2.74f), new Vector3(side * 0.96f, 0.15f, -2.74f),
                    new Vector3(side * 0.96f, 0.5f, -2.3f), new Vector3(side * 0.99f, 0.5f, -2.3f), new Vector3(side * 0.99f, 0.5f, -2.62f), new Vector3(side * 0.96f, 0.5f, -2.62f),
                }), black);
            foreach (Vector3 j in RearJets)
            {
                Cyl(body, "nozzle", j + new Vector3(0f, 0f, 0.04f), new Vector3(0.2f, 0.06f, 0.2f), bronze, new Vector3(90f, 0f, 0f));
                Cyl(body, "nozzle_in", j + new Vector3(0f, 0f, -0.005f), new Vector3(0.14f, 0.02f, 0.14f), dark, new Vector3(90f, 0f, 0f));
            }
            // the attitude jets on the fenders: bronze rings facing up
            foreach (Vector3 j in TopJets)
            {
                Cyl(body, "top_nozzle", j, new Vector3(0.15f, 0.02f, 0.15f), bronze);
                Cyl(body, "top_in", j + new Vector3(0f, 0.012f, 0f), new Vector3(0.1f, 0.01f, 0.1f), dark);
            }
            // the lift jets: bronze rings set into the floor
            foreach (Vector3 j in LiftJets)
            {
                Cyl(body, "lift_nozzle", j + new Vector3(0f, 0.01f, 0f), new Vector3(0.16f, 0.025f, 0.16f), bronze);
                Cyl(body, "lift_in", j + new Vector3(0f, -0.005f, 0f), new Vector3(0.11f, 0.02f, 0.11f), dark);
            }

            // black sills under the doors, and behind each rear wheel a black lower quarter whose top edge rises toward the tail
            Mesh(body, "sills", Hull(new[] { Slab(1.02f, 0.17f, 0.42f, 1.02f, 1.07f, 1.08f), Slab(-1.02f, 0.17f, 0.42f, 1.02f, 1.07f, 1.08f) }), black);
            // the wheel arches' ceilings, dark (the fenders' undersides, so an arch reads as a hole round the wheel, not a white box)
            foreach (float side in new[] { -1f, 1f })
            {
                // (along the fender's underside, which slopes from the core's side up to the fender's edge: flat, it poked up through the hood)
                void Liner(float z0, float low0, float step0, float w0, float z1, float low1, float step1, float w1) => Mesh(body, "arch", Facets.Block(new[]
                {
                    new Vector3(side * 0.69f, low0 - 0.022f, z0), new Vector3(side * w0, step0 - 0.022f, z0), new Vector3(side * w1, step1 - 0.022f, z1), new Vector3(side * 0.69f, low1 - 0.022f, z1),
                    new Vector3(side * 0.69f, low0 - 0.008f, z0), new Vector3(side * w0, step0 - 0.008f, z0), new Vector3(side * w1, step1 - 0.008f, z1), new Vector3(side * 0.69f, low1 - 0.008f, z1),
                }), black);
                Liner(1.95f, 0.62f, 0.70f, 1.03f, 1.55f, 0.84f, 0.88f, 1.07f);
                Liner(1.55f, 0.84f, 0.88f, 1.07f, 1.03f, 0.84f, 0.88f, 1.07f);
                Liner(-1.03f, 0.84f, 0.88f, 1.10f, -1.55f, 0.84f, 0.88f, 1.12f);
                Liner(-1.55f, 0.84f, 0.88f, 1.12f, -2.05f, 0.82f, 0.87f, 1.10f);
            }
            foreach (float side in new[] { -1f, 1f })
                Mesh(body, "quarter", Facets.Block(new[]
                {
                    new Vector3(side * 1.07f, 0.3f, -2.02f), new Vector3(side * 1.115f, 0.3f, -2.02f), new Vector3(side * 1.05f, 0.4f, -2.575f), new Vector3(side * 1.005f, 0.4f, -2.575f),
                    new Vector3(side * 1.07f, 0.56f, -2.02f), new Vector3(side * 1.115f, 0.56f, -2.02f), new Vector3(side * 1.05f, 0.78f, -2.575f), new Vector3(side * 1.005f, 0.78f, -2.575f),
                }), black);

            // ---- the canopy: one long piece of bronze glass, wide at the windscreen (far forward, steeply raked) and narrowing to a point
            // on the rear deck between two white fins
            Mesh(body, "canopy", Hull(new[]
            {
                new Section(1.22f, new Vector2(0f, 0.905f), new Vector2(0.80f, 0.905f), new Vector2(0.64f, 0.925f), new Vector2(0f, 0.925f)),   // (clear of the hood: lower, it showed through in shards)
                new Section(0.45f, new Vector2(0f, 0.91f), new Vector2(0.90f, 0.91f), new Vector2(0.56f, 1.36f), new Vector2(0f, 1.37f)),
                new Section(-0.25f, new Vector2(0f, 0.93f), new Vector2(0.86f, 0.93f), new Vector2(0.50f, 1.47f), new Vector2(0f, 1.48f)),
                new Section(-0.85f, new Vector2(0f, 0.95f), new Vector2(0.64f, 0.95f), new Vector2(0.44f, 1.36f), new Vector2(0f, 1.37f)),
                new Section(-1.60f, new Vector2(0f, 0.97f), new Vector2(0.46f, 0.97f), new Vector2(0.34f, 1.12f), new Vector2(0f, 1.13f)),
                new Section(-2.30f, new Vector2(0f, 0.975f), new Vector2(0.24f, 0.975f), new Vector2(0.16f, 0.995f), new Vector2(0f, 1.0f)),
            }), glass);
            // white A-pillars framing the windscreen (the glass runs on over the roof without a bar)
            foreach (float side in new[] { -1f, 1f })
                VehicleKit.Bar(body, "paint_pillar", new Vector3(side * 0.81f, 0.9f, 1.2f), new Vector3(side * 0.57f, 1.37f, 0.45f), 0.035f, white);

            // the fins (flying buttresses): from the roof's back corners down to the tail either side of the narrowing glass, their outer
            // flanks sloping down to the haunches, a black air scoop in the front of each
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 bf = new Vector3(side * 1.0f, 0.97f, -0.85f), br = new Vector3(side * 0.97f, 0.97f, -2.45f);   // (the flank's foot)
                Vector3 tf = new Vector3(side * 0.6f, 1.33f, -0.85f), tr = new Vector3(side * 0.68f, 1.0f, -2.45f);    // (its ridge)
                Mesh(body, "paint_fin", Facets.Block(new[]
                {
                    new Vector3(side * 0.62f, 0.95f, -0.85f), bf, br, new Vector3(side * 0.30f, 0.97f, -2.45f),
                    new Vector3(side * 0.50f, 1.34f, -0.85f), tf, tr, new Vector3(side * 0.36f, 1.0f, -2.45f),
                }), white);
                Vector3 F(float z, float up, float outward)
                {
                    float k = (z - (-0.85f)) / (-2.45f - (-0.85f));
                    Vector3 foot = Vector3.Lerp(bf, br, k), ridge = Vector3.Lerp(tf, tr, k);
                    Vector3 across = ridge - foot;
                    Vector3 normal = Vector3.Cross(across, Vector3.forward).normalized;
                    if (normal.x * side < 0f) normal = -normal;
                    return Vector3.Lerp(foot, ridge, up) + normal * outward;
                }
                Mesh(body, "scoop", Facets.Block(new[]
                {
                    F(-0.95f, 0.3f, 0.005f), F(-0.95f, 0.85f, 0.005f), F(-1.55f, 0.8f, 0.005f), F(-1.55f, 0.42f, 0.005f),
                    F(-0.95f, 0.3f, 0.02f), F(-0.95f, 0.85f, 0.02f), F(-1.55f, 0.8f, 0.02f), F(-1.55f, 0.42f, 0.02f),
                }), black);
            }

            // ---- the scissor doors: each the side panel between the wheels, hinged at its front top corner (Roadster swings them)
            foreach (float side in new[] { -1f, 1f })
            {
                var hinge = new GameObject(side < 0 ? "door_l" : "door_r").transform;
                hinge.SetParent(body, false);
                hinge.localPosition = new Vector3(side * 0.95f, 0.9f, 1.0f);
                Mesh(hinge, "paint_door", Hull(new[]
                {
                    new Section(-0.02f, new Vector2(side * -0.25f, -0.50f), new Vector2(side * 0.05f, -0.50f), new Vector2(side * 0.12f, -0.40f), new Vector2(side * 0.13f, -0.02f), new Vector2(side * -0.25f, -0.02f)),
                    new Section(-0.10f, new Vector2(side * -0.25f, -0.50f), new Vector2(side * 0.08f, -0.50f), new Vector2(side * 0.13f, -0.40f), new Vector2(side * 0.14f, -0.02f), new Vector2(side * -0.25f, -0.02f)),
                    new Section(-1.90f, new Vector2(side * -0.25f, -0.50f), new Vector2(side * 0.08f, -0.50f), new Vector2(side * 0.13f, -0.40f), new Vector2(side * 0.14f, -0.02f), new Vector2(side * -0.25f, -0.02f)),
                    new Section(-1.98f, new Vector2(side * -0.25f, -0.50f), new Vector2(side * 0.05f, -0.50f), new Vector2(side * 0.12f, -0.40f), new Vector2(side * 0.13f, -0.02f), new Vector2(side * -0.25f, -0.02f)),
                }, true, true, mirror: false), white);
                // the door's window: a bronze pane over the canopy's side, which lifts with it
                Mesh(hinge, "door_glass", Facets.Block(new[]
                {
                    new Vector3(side * -0.07f, 0.0f, -0.25f), new Vector3(side * -0.04f, 0.0f, -0.25f), new Vector3(side * -0.09f, 0.04f, -1.45f), new Vector3(side * -0.12f, 0.04f, -1.45f),
                    new Vector3(side * -0.41f, 0.46f, -0.55f), new Vector3(side * -0.38f, 0.46f, -0.55f), new Vector3(side * -0.43f, 0.55f, -1.2f), new Vector3(side * -0.46f, 0.55f, -1.2f),
                }), glass);
                // a black handle slot and the door's lower edge
                Box(hinge, "handle", new Vector3(side * 0.145f, -0.12f, -1.55f), new Vector3(0.01f, 0.03f, 0.22f), black);
            }

            // ---- the rear wing on two struts
            foreach (float side in new[] { -1f, 1f })
                Mesh(body, "wing_strut", Facets.Block(new[]
                {
                    new Vector3(side * 0.52f, 0.97f, -2.05f), new Vector3(side * 0.56f, 0.97f, -2.05f), new Vector3(side * 0.56f, 0.97f, -2.25f), new Vector3(side * 0.52f, 0.97f, -2.25f),
                    new Vector3(side * 0.52f, 1.27f, -2.28f), new Vector3(side * 0.56f, 1.27f, -2.28f), new Vector3(side * 0.56f, 1.27f, -2.42f), new Vector3(side * 0.52f, 1.27f, -2.42f),
                }), black);
            Mesh(body, "paint_wing", Facets.Block(new[]
            {
                new Vector3(-1.08f, 1.26f, -2.18f), new Vector3(1.08f, 1.26f, -2.18f), new Vector3(1.08f, 1.30f, -2.64f), new Vector3(-1.08f, 1.30f, -2.64f),
                new Vector3(-1.08f, 1.30f, -2.18f), new Vector3(1.08f, 1.30f, -2.18f), new Vector3(1.08f, 1.36f, -2.64f), new Vector3(-1.08f, 1.36f, -2.64f),
            }), white);
            foreach (float side in new[] { -1f, 1f })
                Mesh(body, "wing_end", Facets.Block(new[]
                {
                    new Vector3(side * 1.08f, 1.12f, -2.14f), new Vector3(side * 1.11f, 1.12f, -2.14f), new Vector3(side * 1.11f, 1.18f, -2.68f), new Vector3(side * 1.08f, 1.18f, -2.68f),
                    new Vector3(side * 1.08f, 1.38f, -2.20f), new Vector3(side * 1.11f, 1.38f, -2.20f), new Vector3(side * 1.11f, 1.44f, -2.68f), new Vector3(side * 1.08f, 1.44f, -2.68f),
                }), black);

            // ---- inside (seen through the glass): two white buckets, the yoke, the dash and the screen
            foreach (Vector3 s in new[] { SeatPoint, PassengerPoint })
            {
                Vector3 at = new Vector3(s.x, 0f, s.z);
                Mesh(body, "seat_base", VehicleMesh.RoundedBox(new Vector3(0.5f, 0.14f, 0.55f), 0.05f), seat, at + new Vector3(0f, 0.32f, 0.05f));
                Mesh(body, "seat_back", VehicleMesh.RoundedBox(new Vector3(0.5f, 0.78f, 0.13f), 0.06f), seat, at + new Vector3(0f, 0.72f, -0.26f), new Vector3(-14f, 0f, 0f));
                Box(body, "seat_badge", at + new Vector3(0f, 1.02f, -0.335f), new Vector3(0.2f, 0.05f, 0.01f), lensTail, new Vector3(-14f, 0f, 0f));
            }
            Mesh(body, "dash", VehicleMesh.RoundedBox(new Vector3(1.55f, 0.16f, 0.4f), 0.05f), black, new Vector3(0f, 0.86f, 0.78f));
            Box(body, "screen", new Vector3(0f, 1.0f, 0.62f), new Vector3(0.36f, 0.22f, 0.02f), dark, new Vector3(-20f, 0f, 0f));
            Box(body, "console", new Vector3(0f, 0.5f, 0.15f), new Vector3(0.22f, 0.3f, 1.1f), black);
            var yoke = new GameObject("yoke").transform;   // (turns with the steering: Roadster)
            yoke.SetParent(body, false);
            yoke.localPosition = new Vector3(SeatPoint.x, 0.84f, 0.42f);
            yoke.localRotation = Quaternion.Euler(-22f, 0f, 0f);
            Box(yoke, "yoke_bar", new Vector3(0f, 0.06f, 0f), new Vector3(0.36f, 0.04f, 0.04f), black);
            Box(yoke, "yoke_l", new Vector3(-0.17f, -0.02f, 0f), new Vector3(0.045f, 0.18f, 0.045f), black);
            Box(yoke, "yoke_r", new Vector3(0.17f, -0.02f, 0f), new Vector3(0.045f, 0.18f, 0.045f), black);
            Box(yoke, "yoke_hub", new Vector3(0f, 0f, 0.03f), new Vector3(0.12f, 0.1f, 0.06f), dark);
            Cyl(body, "column", new Vector3(SeatPoint.x, 0.8f, 0.62f), new Vector3(0.06f, 0.2f, 0.06f), black, new Vector3(68f, 0f, 0f));
            // where the driver's hands and feet go (VehicleRider), and the passenger's
            Marker(yoke, "grip_l", new Vector3(-0.17f, -0.02f, 0f));
            Marker(yoke, "grip_r", new Vector3(0.17f, -0.02f, 0f));
            Marker(body, "foot_l", new Vector3(SeatPoint.x - 0.14f, 0.26f, 0.98f));
            Marker(body, "foot_r", new Vector3(SeatPoint.x + 0.14f, 0.26f, 0.98f));
            Marker(body, "pgrip_l", new Vector3(PassengerPoint.x - 0.14f, 0.56f, 0.12f));
            Marker(body, "pgrip_r", new Vector3(PassengerPoint.x + 0.14f, 0.56f, 0.12f));
            Marker(body, "pfoot_l", new Vector3(PassengerPoint.x - 0.14f, 0.26f, 0.98f));
            Marker(body, "pfoot_r", new Vector3(PassengerPoint.x + 0.14f, 0.26f, 0.98f));

            // ---- wheels: black rims with six spokes over white aero inserts, low tyres
            for (int i = 0; i < Wheels.Length; i++) Wheel(body, i, Wheels[i], tyre, black, white, dark);

            // ---- lights (GroundVehicle switches them on at night while it is ridden)
            Lamp(body, "head_l", new Vector3(-0.6f, 0.47f, 2.62f), new Vector3(-0.05f, -0.1f, 1f), LightType.Spot, 34f, 60f, 2.4f, new Color(0.92f, 0.95f, 1f));
            Lamp(body, "head_r", new Vector3(0.6f, 0.47f, 2.62f), new Vector3(0.05f, -0.1f, 1f), LightType.Spot, 34f, 60f, 2.4f, new Color(0.92f, 0.95f, 1f));
            Lamp(body, "tail", new Vector3(0f, 0.8f, -2.75f), Vector3.back, LightType.Point, 4f, 0f, 0.5f, new Color(1f, 0.1f, 0.05f));

            // the seats' attach points (kept when the model is rebuilt on a car someone sits in: they are attached to them)
            if (root.Find("seat") == null) Marker(root, "seat", SeatPoint);
            if (root.Find("passenger") == null) Marker(root, "passenger", PassengerPoint);
            Marker(body, Stamp, Vector3.zero);
        }

        /// <summary>Which build made a model (a marker inside it): a car standing in the world whose model another build made gets a fresh one (Roadster.PrepareModel).</summary>
        internal static readonly string Stamp = "build_" + typeof(Model).Assembly.ManifestModule.ModuleVersionId.ToString("N");

        /// <summary>A wheel: wheelN steers and rides the springs (GroundVehicle), its "spin" child turns. The axle is along x.</summary>
        private static void Wheel(Transform parent, int index, Vector3 hub, Material tyre, Material rim, Material insert, Material dark)
        {
            float side = Mathf.Sign(hub.x);
            var w = new GameObject("wheel" + index).transform;
            w.SetParent(parent, false);
            w.localPosition = hub;
            var spin = new GameObject("spin").transform;
            spin.SetParent(w, false);
            VehicleMesh.Add(spin, "tyre", VehicleMesh.Tyre(WheelRadius, WheelWidth, WheelRadius * 0.8f), tyre);
            float face = side * WheelWidth * 0.43f, rimR = WheelRadius * 0.8f;   // (the inserts just outside the tyre's inner edge, or the tyre hides them)
            VehicleKit.Shape(spin, PrimitiveType.Cylinder, "rim", Vector3.zero, new Vector3(rimR * 2f, WheelWidth * 0.4f, rimR * 2f), dark, new Vector3(0f, 0f, 90f));
            VehicleKit.Shape(spin, PrimitiveType.Cylinder, "insert", new Vector3(face, 0f, 0f), new Vector3(rimR * 1.9f, 0.012f, rimR * 1.9f), insert, new Vector3(0f, 0f, 90f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f;
                Vector3 dir = Quaternion.Euler(a, 0f, 0f) * Vector3.up;
                VehicleKit.Shape(spin, PrimitiveType.Cube, "spoke", new Vector3(face + side * 0.012f, 0f, 0f) + dir * rimR * 0.5f,
                    new Vector3(0.025f, rimR * 1.0f, 0.06f), rim, new Vector3(a, 0f, 0f));
            }
            // the side jet in the hub (on the wheel, not its spin: it steers with the wheel but doesn't turn round), a bronze nozzle facing out
            VehicleKit.Shape(w, PrimitiveType.Cylinder, "side_nozzle", new Vector3(side * (SideJetOut - 0.03f), 0f, 0f), new Vector3(0.13f, 0.04f, 0.13f), Look.Of("Bronze"), new Vector3(0f, 0f, 90f));
            VehicleKit.Shape(w, PrimitiveType.Cylinder, "side_in", new Vector3(side * (SideJetOut - 0.005f), 0f, 0f), new Vector3(0.08f, 0.01f, 0.08f), dark, new Vector3(0f, 0f, 90f));
            Marker(w, "jet_side" + index, new Vector3(side * SideJetOut, 0f, 0f));
        }
    }

    /// <summary>
    /// The roadster's materials: plain colours made from the game's matte flat metal (Shared VehicleLook's base, so nothing needs
    /// Shader.Find). The glass is a dark glossy bronze, not see-through (the game's crystal wall material, tinted, came out as orange plaster).
    /// </summary>
    internal static class Look
    {
        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();

        private static readonly Dictionary<string, (Color colour, float metal, float gloss)> Colours = new Dictionary<string, (Color, float, float)>
        {
            { "White", (new Color(0.7f, 0.72f, 0.75f), 0.15f, 0.45f) },      // "Ano White" (brighter blooms to a glare in daylight)
            { "Black", (new Color(0.05f, 0.05f, 0.055f), 0.1f, 0.35f) },
            { "Trim", (new Color(0.09f, 0.09f, 0.1f), 0.0f, 0.2f) },
            { "Dark", (new Color(0.02f, 0.02f, 0.025f), 0.0f, 0.5f) },
            { "Tyre", (new Color(0.06f, 0.06f, 0.07f), 0.0f, 0.12f) },
            { "Bronze", (new Color(0.72f, 0.5f, 0.26f), 0.85f, 0.6f) },
            { "SeatWhite", (new Color(0.7f, 0.7f, 0.68f), 0.0f, 0.15f) },
            { "Plate", (new Color(0.7f, 0.7f, 0.68f), 0.3f, 0.4f) },
            { "LensHead", (new Color(0.95f, 0.97f, 1f), 0.0f, 0.6f) },
            { "LensTail", (new Color(0.75f, 0.04f, 0.03f), 0.0f, 0.6f) },
            { "Glass", (new Color(0.2f, 0.13f, 0.065f), 0.7f, 0.9f) },      // (dark gold-tinted glass, like an Apollo visor; opaque: the game's crystal shows as orange plaster)
        };

        internal static void Harvest(ZNetScene scene)
        {
            Made.Clear();
            Material basis = VehicleLook.Of("Grey");
            if (basis == null) return;
            foreach (var kv in Colours)
            {
                var m = new Material(basis) { name = "dh_roadster_" + kv.Key, color = kv.Value.colour };
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", kv.Value.metal);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", kv.Value.gloss);
                if (m.HasProperty("_MetallicAlphaGloss")) m.SetFloat("_MetallicAlphaGloss", kv.Value.gloss);
                if (kv.Key.StartsWith("Lens")) m.EnableKeyword("_EMISSION");   // (lit by a property block when the lights are on)
                Made[kv.Key] = m;
            }
        }

        internal static Material Of(string name) => Made.TryGetValue(name, out Material m) ? m : VehicleLook.Of("Grey");

        internal static void Clear() => Made.Clear();
    }
}
