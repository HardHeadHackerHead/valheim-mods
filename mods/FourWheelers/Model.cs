using DHack.Shared;
using UnityEngine;
using Station = DHack.Shared.VehicleMesh.Station;

namespace FourWheelers
{
    /// <summary>
    /// The four wheeler's body, made in code, after a sport quad: a sculpted red front body (nose, fairing and tank in one skin) with a
    /// lens each side, curved front fenders, a one-piece rear body with wheel arches and a rear rack, a long tapered leather seat, an
    /// exposed tube frame with A-arm front suspension, coil-over shocks and a rear swingarm, a silver engine with a finned head and a
    /// chrome exhaust, floorboards, a tubular front bumper and rack, handlebars with hand guards, mirrors and a gauge (they turn with
    /// the steering: the "handlebars" node), and fat knobbly tyres on black rims. Parts named paint* take the paint colour; head*, tail*,
    /// lens_head and lens_tail are the lights (GroundVehicle lights them at night while it is ridden). Units are metres, y up, +z is
    /// forward. The wheels' hubs sit at the tyres' radius so the bottoms touch the ground at rest (GroundVehicle's suspension moves
    /// them from there).
    /// </summary>
    internal static class Model
    {
        internal const float WheelRadius = 0.30f, WheelWidth = 0.26f, RimRadius = 0.185f;
        private const float HubX = 0.54f, HubZ = 0.62f;
        internal static readonly Vector3[] Wheels =
        {
            new Vector3(-HubX, WheelRadius, HubZ), new Vector3(HubX, WheelRadius, HubZ),     // front (steered)
            new Vector3(-HubX, WheelRadius, -HubZ), new Vector3(HubX, WheelRadius, -HubZ),   // back
        };
        internal static readonly Vector3 SeatPoint = new Vector3(0f, 0.47f, -0.18f);      // the rider's feet-level origin: the seat animation lifts the hips about 0.5 above it
        internal static readonly Vector3 PassengerPoint = new Vector3(0f, 0.49f, -0.62f);

        /// <summary>An empty marker where a rider sits.</summary>
        internal static Transform Seat(Transform root, string name, Vector3 at)
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
            Material frame = VehicleLook.Of("Frame"), paint = VehicleLook.Of("Red"), seat = VehicleLook.Of("Seat"), chrome = VehicleLook.Of("Chrome");
            Material tyre = VehicleLook.Of("Tyre"), silver = VehicleLook.Of("Silver"), grey = VehicleLook.Of("Grey");
            Material lensHead = VehicleLook.Of("LensHead"), lensTail = VehicleLook.Of("LensTail"), spring = VehicleLook.Of("Red");
            var body = new GameObject("Model").transform;   // (the build menu's picture is drawn from "Model")
            body.SetParent(root, false);
            Transform S(PrimitiveType type, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(body, type, n, at, size, m, euler);
            Transform Bar(string n, Vector3 a, Vector3 b, float thick, Material m) => VehicleKit.Bar(body, n, a, b, thick, m);
            Transform Mesh(string n, Mesh mesh, Material m, Vector3 at = default, Vector3 euler = default) => VehicleMesh.Add(body, n, mesh, m, at, euler);

            // ---- the tube frame, suspension and engine (what shows between the bodywork) ------------------------------------------
            foreach (float side in new[] { -1f, 1f })
            {
                Bar("rail", new Vector3(side * 0.20f, 0.44f, 0.86f), new Vector3(side * 0.20f, 0.46f, -0.80f), 0.05f, frame);
                Bar("rail_up", new Vector3(side * 0.20f, 0.46f, 0.30f), new Vector3(side * 0.16f, 0.70f, 0.58f), 0.045f, frame);
                // front A-arms to the hub, and the coil-over shock from the lower arm up to the frame
                Vector3 hub = new Vector3(side * (HubX - 0.13f), 0.34f, HubZ);
                Bar("arm_upper", new Vector3(side * 0.20f, 0.60f, HubZ + 0.04f), hub + new Vector3(0f, 0.12f, 0f), 0.04f, frame);
                Bar("arm_lower_a", new Vector3(side * 0.20f, 0.40f, HubZ + 0.14f), hub, 0.045f, frame);
                Bar("arm_lower_b", new Vector3(side * 0.20f, 0.40f, HubZ - 0.14f), hub, 0.045f, frame);
                Vector3 lo = new Vector3(side * 0.36f, 0.38f, HubZ), hi = new Vector3(side * 0.17f, 0.86f, HubZ - 0.04f);
                Bar("shock", lo, hi, 0.035f, frame);
                Bar("shock_spring", Vector3.Lerp(lo, hi, 0.25f), Vector3.Lerp(lo, hi, 0.85f), 0.075f, spring);
                // rear swingarm and axle
                Bar("swingarm", new Vector3(side * 0.22f, 0.42f, -0.12f), new Vector3(side * (HubX - 0.13f), WheelRadius, -HubZ), 0.065f, frame);
                // floorboard with a grip plate and an outer lip
                Mesh("floor", VehicleMesh.RoundedBox(new Vector3(0.22f, 0.045f, 0.78f), 0.018f), frame, new Vector3(side * 0.32f, 0.40f, 0.02f));
                Mesh("floor_lip", VehicleMesh.RoundedBox(new Vector3(0.03f, 0.1f, 0.78f), 0.012f), frame, new Vector3(side * 0.43f, 0.43f, 0.02f));
                for (int k = 0; k < 4; k++) S(PrimitiveType.Cube, "grip_bar", new Vector3(side * 0.32f, 0.425f, -0.26f + k * 0.18f), new Vector3(0.17f, 0.012f, 0.045f), silver);
            }
            Bar("cross_front", new Vector3(-0.2f, 0.42f, 0.55f), new Vector3(0.2f, 0.42f, 0.55f), 0.045f, frame);
            Bar("cross_mid", new Vector3(-0.2f, 0.42f, 0.0f), new Vector3(0.2f, 0.42f, 0.0f), 0.045f, frame);
            Bar("cross_rear", new Vector3(-0.2f, 0.44f, -0.55f), new Vector3(0.2f, 0.44f, -0.55f), 0.045f, frame);
            Bar("axle_rear", new Vector3(-HubX + 0.12f, WheelRadius, -HubZ), new Vector3(HubX - 0.12f, WheelRadius, -HubZ), 0.08f, frame);
            Mesh("skid", VehicleMesh.RoundedBox(new Vector3(0.44f, 0.04f, 1.25f), 0.015f), silver, new Vector3(0f, 0.36f, 0.25f));
            // the rear monoshock, between the swingarm and the frame
            Bar("monoshock", new Vector3(0f, 0.80f, -0.26f), new Vector3(0f, 0.44f, -0.56f), 0.04f, frame);
            Bar("monoshock_spring", new Vector3(0f, 0.74f, -0.31f), new Vector3(0f, 0.52f, -0.49f), 0.085f, spring);

            // engine: a crankcase, a finned cylinder head leaning forward, a drive chain to the rear axle
            Mesh("crankcase", VehicleMesh.RoundedBox(new Vector3(0.36f, 0.3f, 0.42f), 0.07f), silver, new Vector3(0f, 0.5f, -0.04f));
            Mesh("cylinder", VehicleMesh.RoundedBox(new Vector3(0.2f, 0.24f, 0.2f), 0.04f), silver, new Vector3(0f, 0.7f, 0.1f), new Vector3(-18f, 0f, 0f));
            for (int k = 0; k < 4; k++) S(PrimitiveType.Cube, "fin", new Vector3(0f, 0.66f + k * 0.045f, 0.12f + k * 0.012f), new Vector3(0.25f, 0.014f, 0.25f), grey, new Vector3(-18f, 0f, 0f));
            Bar("chain", new Vector3(0.1f, 0.5f, -0.12f), new Vector3(0.1f, 0.36f, -0.55f), 0.03f, frame);

            // exhaust: head pipe, down the right side, a chrome muffler out the back
            Bar("exhaust_a", new Vector3(0.1f, 0.74f, 0.2f), new Vector3(0.3f, 0.54f, 0.1f), 0.045f, chrome);
            Bar("exhaust_b", new Vector3(0.3f, 0.54f, 0.1f), new Vector3(0.3f, 0.52f, -0.5f), 0.05f, chrome);
            S(PrimitiveType.Cylinder, "muffler", new Vector3(0.3f, 0.54f, -0.72f), new Vector3(0.11f, 0.26f, 0.11f), chrome, new Vector3(90f, 0f, 0f));
            S(PrimitiveType.Cylinder, "muffler_tip", new Vector3(0.3f, 0.54f, -0.99f), new Vector3(0.07f, 0.02f, 0.07f), frame, new Vector3(90f, 0f, 0f));

            // ---- the bodywork (red: parts named paint*) -----------------------------------------------------------------------------
            // the body: nose, fairing and fuel tank, a waist where the seat sits, then the rear deck and tail, in one skin
            Mesh("paint_body", VehicleMesh.Loft(new[]
            {
                new Station(-1.04f, 0.28f, 0.70f, 0.80f, 4.5f),
                new Station(-0.92f, 0.34f, 0.66f, 0.84f, 4f),
                new Station(-0.62f, 0.34f, 0.64f, 0.86f, 4f),
                new Station(-0.30f, 0.29f, 0.62f, 0.88f, 4f),
                new Station(-0.06f, 0.26f, 0.64f, 0.955f, 4f),
                new Station(0.16f, 0.30f, 0.60f, 1.02f, 4f),
                new Station(0.46f, 0.33f, 0.58f, 0.98f, 3.8f),
                new Station(0.74f, 0.32f, 0.58f, 0.92f, 4.5f),
                new Station(0.92f, 0.30f, 0.58f, 0.88f, 5f),
                new Station(1.00f, 0.27f, 0.60f, 0.82f, 5.5f),
            }, 24), paint);
            // two cream rally stripes down the tank and nose, flat on the skin
            foreach (float sx in new[] { -0.045f, 0.045f })
            {
                Vector3 from = new Vector3(sx, 1.05f, 0.14f), to = new Vector3(sx, 0.915f, 0.9f);
                Transform stripe = S(PrimitiveType.Cube, "stripe", (from + to) * 0.5f, new Vector3(0.04f, 0.01f, (to - from).magnitude), VehicleLook.Of("Cream"));
                stripe.localRotation = Quaternion.LookRotation((to - from).normalized, Vector3.up);
            }
            // a black lower skirt under the body, side plates between the fenders and the body, mudflaps and a number plate
            Mesh("skirt", VehicleMesh.Loft(new[]
            {
                new Station(-0.95f, 0.26f, 0.60f, 0.68f, 4f),
                new Station(-0.5f, 0.30f, 0.58f, 0.66f, 4f),
                new Station(0.0f, 0.27f, 0.56f, 0.64f, 4f),
                new Station(0.5f, 0.31f, 0.54f, 0.62f, 4f),
                new Station(0.95f, 0.26f, 0.56f, 0.64f, 4f),
            }, 14), frame);
            foreach (float side in new[] { -1f, 1f })
            {
                Mesh("flap_front", VehicleMesh.RoundedBox(new Vector3(0.2f, 0.12f, 0.012f), 0.005f), frame, new Vector3(side * HubX, 0.19f, HubZ - 0.33f));
                Mesh("flap_rear", VehicleMesh.RoundedBox(new Vector3(0.24f, 0.14f, 0.012f), 0.005f), frame, new Vector3(side * HubX, 0.19f, -HubZ - 0.34f));
                // footpegs: a heel guard and a peg at the back of each floorboard
                S(PrimitiveType.Cube, "heel_guard", new Vector3(side * 0.45f, 0.5f, -0.3f), new Vector3(0.012f, 0.16f, 0.2f), silver);
            }
            Mesh("plate", VehicleMesh.RoundedBox(new Vector3(0.2f, 0.1f, 0.012f), 0.004f), VehicleLook.Of("Cream"), new Vector3(0f, 0.7f, -1.06f));
            Mesh("plate_mount", VehicleMesh.RoundedBox(new Vector3(0.05f, 0.04f, 0.03f), 0.004f), frame, new Vector3(0f, 0.74f, -1.05f));

            // curved front fenders over the wheels, a stay to the body
            foreach (float side in new[] { -1f, 1f })
            {
                Mesh("paint_fender_front", VehicleMesh.Arch(WheelRadius + 0.05f, 0.27f, 24f, 156f, 0.022f, 0.025f, side, 0.1f), paint, new Vector3(side * HubX, WheelRadius, HubZ));
                Bar("fender_stay", new Vector3(side * 0.30f, 0.84f, HubZ + 0.04f), new Vector3(side * (HubX - 0.1f), WheelRadius + 0.34f, HubZ + 0.04f), 0.03f, frame);
            }
            // the rear wheel arches, joined to the deck
            foreach (float side in new[] { -1f, 1f })
                Mesh("paint_fender_rear", VehicleMesh.Arch(WheelRadius + 0.05f, 0.31f, 8f, 172f, 0.022f, 0.025f, side, 0.1f), paint, new Vector3(side * HubX, WheelRadius, -HubZ));

            // the seat: long, tapering to the front, rising a little to the back
            Mesh("seat_cushion", VehicleMesh.Loft(new[]
            {
                new Station(0.30f, 0.12f, 0.85f, 0.96f, 3.5f),
                new Station(0.05f, 0.21f, 0.84f, 0.99f, 4f),
                new Station(-0.35f, 0.26f, 0.84f, 1.01f, 4f),
                new Station(-0.68f, 0.25f, 0.85f, 1.01f, 4f),
                new Station(-0.80f, 0.2f, 0.86f, 0.98f, 3f),
            }, 18), seat);

            // racks of chrome tube: in front over the nose, behind the seat
            foreach (float side in new[] { -1f, 1f })
            {
                Bar("frack_rail", new Vector3(side * 0.22f, 1.02f, 0.38f), new Vector3(side * 0.22f, 1.02f, 0.76f), 0.028f, chrome);
                Bar("frack_leg", new Vector3(side * 0.22f, 1.02f, 0.38f), new Vector3(side * 0.26f, 0.9f, 0.3f), 0.028f, chrome);
                Bar("frack_leg", new Vector3(side * 0.22f, 1.02f, 0.76f), new Vector3(side * 0.24f, 0.86f, 0.8f), 0.028f, chrome);
                Bar("rrack_rail", new Vector3(side * 0.34f, 0.95f, -0.62f), new Vector3(side * 0.34f, 0.95f, -1.0f), 0.03f, chrome);
                Bar("rrack_leg", new Vector3(side * 0.34f, 0.95f, -0.62f), new Vector3(side * 0.34f, 0.84f, -0.62f), 0.03f, chrome);
                Bar("rrack_leg", new Vector3(side * 0.34f, 0.95f, -1.0f), new Vector3(side * 0.3f, 0.78f, -1.0f), 0.03f, chrome);
            }
            Bar("frack_cross", new Vector3(-0.22f, 1.02f, 0.76f), new Vector3(0.22f, 1.02f, 0.76f), 0.028f, chrome);
            Bar("frack_cross", new Vector3(-0.22f, 1.02f, 0.38f), new Vector3(0.22f, 1.02f, 0.38f), 0.028f, chrome);
            for (int k = 0; k < 3; k++) Bar("rrack_cross", new Vector3(-0.34f, 0.95f, -0.62f - k * 0.19f), new Vector3(0.34f, 0.95f, -0.62f - k * 0.19f), 0.026f, chrome);
            Bar("rrack_cross", new Vector3(-0.34f, 0.95f, -1.0f), new Vector3(0.34f, 0.95f, -1.0f), 0.03f, chrome);

            // front bumper: a loop of chrome tube round the nose, braced to the frame
            Vector3 b0 = new Vector3(-0.30f, 0.48f, 0.74f), b1 = new Vector3(-0.38f, 0.50f, 1.08f), b2 = new Vector3(0.38f, 0.50f, 1.08f), b3 = new Vector3(0.30f, 0.48f, 0.74f);
            Bar("bumper_l", b0, b1, 0.04f, chrome); Bar("bumper_front", b1, b2, 0.045f, chrome); Bar("bumper_r", b2, b3, 0.04f, chrome);
            Bar("bumper_up_l", b1, new Vector3(-0.31f, 0.70f, 1.03f), 0.035f, chrome);
            Bar("bumper_up_r", b2, new Vector3(0.31f, 0.70f, 1.03f), 0.035f, chrome);
            Bar("bumper_top", new Vector3(-0.31f, 0.70f, 1.03f), new Vector3(0.31f, 0.70f, 1.03f), 0.03f, chrome);

            // headlights: a chrome bezel, a lens and the beam, each side of the nose (off by day); tail lights on the end of the rear body
            foreach (float side in new[] { -1f, 1f })
            {
                Mesh("lamp_bezel", VehicleMesh.RoundedBox(new Vector3(0.22f, 0.16f, 0.07f), 0.035f), chrome, new Vector3(side * 0.14f, 0.70f, 0.98f));
                S(PrimitiveType.Sphere, "lens_head", new Vector3(side * 0.14f, 0.70f, 1.015f), new Vector3(0.18f, 0.12f, 0.06f), lensHead);
                Lamp(body, "headlight_" + (side < 0 ? "L" : "R"), new Vector3(side * 0.14f, 0.72f, 1.06f), new Vector3(side * 0.04f, -0.12f, 1f), LightType.Spot, 34f, 62f, 2.3f, new Color(1f, 0.95f, 0.8f));
                S(PrimitiveType.Cube, "lens_tail", new Vector3(side * 0.22f, 0.745f, -1.05f), new Vector3(0.12f, 0.05f, 0.03f), lensTail);
                Lamp(body, "taillight_" + (side < 0 ? "L" : "R"), new Vector3(side * 0.22f, 0.745f, -1.1f), new Vector3(0f, 0f, -1f), LightType.Point, 3.5f, 0f, 0.5f, new Color(1f, 0.1f, 0.05f));
            }

            // ---- handlebars (they turn with the steering: GroundVehicle turns this node about its own up axis) -------------------
            var pivot = new Vector3(0f, 0f, 0.26f);
            var bars = new GameObject("handlebars").transform;
            bars.SetParent(body, false);
            bars.localPosition = pivot;
            Transform SB(PrimitiveType type, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(bars, type, n, at - pivot, size, m, euler);
            Transform BB(string n, Vector3 a, Vector3 b, float thick, Material m) => VehicleKit.Bar(bars, n, a - pivot, b - pivot, thick, m);
            BB("steer_column", new Vector3(0f, 0.92f, 0.30f), new Vector3(0f, 1.07f, 0.26f), 0.06f, frame);
            BB("bar_mid", new Vector3(-0.16f, 1.09f, 0.26f), new Vector3(0.16f, 1.09f, 0.26f), 0.035f, chrome);
            foreach (float side in new[] { -1f, 1f })
            {
                BB("bar_arm", new Vector3(side * 0.16f, 1.09f, 0.26f), new Vector3(side * 0.40f, 1.08f, 0.16f), 0.03f, chrome);
                BB("grip", new Vector3(side * 0.40f, 1.08f, 0.16f), new Vector3(side * 0.52f, 1.075f, 0.11f), 0.062f, frame);
                VehicleMesh.Add(bars, "paint_hand_guard", VehicleMesh.RoundedBox(new Vector3(0.02f, 0.075f, 0.17f), 0.008f), paint, new Vector3(side * 0.51f, 1.125f, 0.09f) - pivot, new Vector3(0f, side * -8f, 0f));
                BB("lever", new Vector3(side * 0.36f, 1.075f, 0.2f), new Vector3(side * 0.34f, 1.045f, 0.29f), 0.014f, chrome);
                BB("mirror_stalk", new Vector3(side * 0.28f, 1.09f, 0.24f), new Vector3(side * 0.33f, 1.22f, 0.2f), 0.014f, frame);
                VehicleMesh.Add(bars, "mirror", VehicleMesh.RoundedBox(new Vector3(0.1f, 0.065f, 0.016f), 0.008f), frame, new Vector3(side * 0.34f, 1.25f, 0.19f) - pivot, new Vector3(-12f, side * 10f, 0f));
            }
            SB(PrimitiveType.Cylinder, "gauge_bezel", new Vector3(0f, 1.14f, 0.3f), new Vector3(0.12f, 0.03f, 0.12f), chrome, new Vector3(-60f, 0f, 0f));
            SB(PrimitiveType.Cylinder, "gauge_face", new Vector3(0f, 1.145f, 0.298f), new Vector3(0.10f, 0.03f, 0.10f), frame, new Vector3(-60f, 0f, 0f));

            // ---- the wheels: a turned tyre with chevron tread on a black rim with chrome lug nuts --------------------------------------
            Mesh tyreMesh = VehicleMesh.Tyre(WheelRadius, WheelWidth, RimRadius);
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = new GameObject("wheel" + i).transform;
                w.SetParent(body, false);
                w.localPosition = Wheels[i];
                var spin = new GameObject("spin").transform;
                spin.SetParent(w, false);
                float side = Wheels[i].x < 0f ? -1f : 1f;
                VehicleMesh.Add(spin, "tyre", tyreMesh, tyre);
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "rim", new Vector3(side * WheelWidth * 0.04f, 0f, 0f), new Vector3(RimRadius * 2f * 1.04f, WheelWidth * 0.4f, RimRadius * 2f * 1.04f), silver, new Vector3(0f, 0f, 90f));
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "rim_dish", new Vector3(side * WheelWidth * 0.07f, 0f, 0f), new Vector3(RimRadius * 2f * 0.8f, WheelWidth * 0.4f, RimRadius * 2f * 0.8f), frame, new Vector3(0f, 0f, 90f));
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "hub", new Vector3(side * WheelWidth * 0.44f, 0f, 0f), new Vector3(0.1f, 0.012f, 0.1f), chrome, new Vector3(0f, 0f, 90f));
                for (int k = 0; k < 5; k++)
                {
                    float a = k * 72f * Mathf.Deg2Rad;
                    VehicleKit.Shape(spin, PrimitiveType.Cylinder, "nut", new Vector3(side * WheelWidth * 0.46f, Mathf.Cos(a) * 0.075f, Mathf.Sin(a) * 0.075f), new Vector3(0.026f, 0.012f, 0.026f), chrome, new Vector3(0f, 0f, 90f));
                }
                // tread: chevron blocks round the tyre
                for (int k = 0; k < 20; k++)
                {
                    var turn = new GameObject("tread").transform;
                    turn.SetParent(spin, false);
                    turn.localRotation = Quaternion.Euler(k * 18f, 0f, 0f);
                    foreach (float half in new[] { -1f, 1f })
                    {
                        Transform block = VehicleKit.Shape(turn, PrimitiveType.Cube, "lug", new Vector3(half * WheelWidth * 0.17f, WheelRadius + 0.002f, 0f), new Vector3(WheelWidth * 0.3f, 0.026f, 0.05f), tyre);
                        block.localRotation = Quaternion.Euler(0f, half * 28f, 0f);
                    }
                }
            }

            Seat(root, "seat", SeatPoint);
            Seat(root, "passenger", PassengerPoint);
        }
    }
}
