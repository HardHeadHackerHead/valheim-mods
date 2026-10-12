using DHack.Shared;
using UnityEngine;
using Station = DHack.Shared.VehicleMesh.Station;

namespace MountainBikes
{
    /// <summary>
    /// The mountain bike's body, made in code after a hardtail trail bike: tapered frame tubes with welded-looking joints (parts named
    /// paint*: E on its paint spot changes the colour), a sloping top tube, a suspension fork with chrome stanchions and white lowers, thin
    /// tyres with knobbly tread on spoked rims, disc brakes, a chain ring, chain, cassette and rear derailleur, a slim saddle on a long
    /// seat post, flat handlebars with grips, brake levers, a bell and a headlamp that turn with the steering ("handlebars"), pedals and
    /// cranks that turn when it is pedalled ("cranks"), a bottle, a kickstand that tucks up when it is ridden ("kickstand"), and a tail
    /// reflector (the lamps light up at night while it is ridden: GroundVehicle looks for the parts named head*, tail*, lens_head and
    /// lens_tail). Units are metres, y up, +z is forward. The whole bike (wheels too) sits in a "lean" part that tips into turns,
    /// pivoting at the ground (GroundVehicle). The wheels' hubs sit at the tyres' radius so the bottoms touch the ground at rest.
    /// </summary>
    internal static class Model
    {
        internal const float WheelRadius = 0.36f, WheelWidth = 0.10f, RimRadius = 0.30f;
        internal static readonly Vector3[] Wheels =
        {
            new Vector3(0f, 0.36f, 0.62f),    // front (steered)
            new Vector3(0f, 0.36f, -0.62f),   // back
        };
        internal static readonly Vector3 SeatPoint = new Vector3(0f, 0.51f, -0.13f);   // the rider's feet-level origin: the seat animation lifts the hips about 0.5 above it, onto the saddle

        /// <summary>An empty marker where the rider sits.</summary>
        internal static Transform Seat(Transform parent, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        internal static void Build(Transform root)
        {
            Material frame = VehicleLook.Of("Green"), dark = VehicleLook.Of("Frame"), seat = VehicleLook.Of("Seat"), chrome = VehicleLook.Of("Chrome");
            Material tyre = VehicleLook.Of("Tyre"), cream = VehicleLook.Of("Cream"), red = VehicleLook.Of("Red"), silver = VehicleLook.Of("Silver");
            Material lensHead = VehicleLook.Of("LensHead"), lensTail = VehicleLook.Of("LensTail");
            var model = new GameObject("Model").transform;   // (the build menu's picture is drawn from "Model")
            model.SetParent(root, false);
            var lean = new GameObject("lean").transform;
            lean.SetParent(model, false);
            Transform Tu(string n, Vector3 a, Vector3 b, float r0, float r1, Material m, bool joints = true) => VehicleKit.Tube(lean, n, a, b, r0, r1, m, joints);
            Transform S(PrimitiveType type, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(lean, type, n, at, size, m, euler);

            // ---- the frame: a sloping-top-tube hardtail diamond ---------------------------------------------------------------
            var bb = new Vector3(0f, 0.31f, -0.02f);
            var headTop = new Vector3(0f, 0.925f, 0.40f);
            var headLow = new Vector3(0f, 0.785f, 0.45f);
            var seatTop = new Vector3(0f, 0.905f, -0.125f);
            var seatJoint = new Vector3(0f, 0.855f, -0.11f);
            var rear = Wheels[1];
            var front = Wheels[0];
            Tu("paint_headtube", headLow, headTop, 0.036f, 0.036f, frame);
            Tu("paint_toptube", new Vector3(0f, 0.885f, 0.41f), seatJoint, 0.027f, 0.022f, frame);
            Tu("paint_downtube", new Vector3(0f, 0.80f, 0.44f), bb, 0.04f, 0.03f, frame);
            Tu("paint_seattube", bb, seatTop, 0.027f, 0.022f, frame);
            foreach (float side in new[] { -1f, 1f })
            {
                Tu("paint_chainstay", bb + new Vector3(side * 0.03f, 0f, 0f), rear + new Vector3(side * 0.058f, 0f, 0f), 0.021f, 0.015f, frame);
                Tu("paint_seatstay", seatJoint + new Vector3(side * 0.02f, 0f, 0f), rear + new Vector3(side * 0.058f, 0.01f, 0f), 0.014f, 0.011f, frame);
                S(PrimitiveType.Cube, "paint_dropout_" + side, rear + new Vector3(side * 0.058f, 0.0f, 0.0f), new Vector3(0.012f, 0.07f, 0.05f), frame);
            }
            // seat post and a slim saddle
            Tu("seatpost", seatTop, new Vector3(0f, 1.075f, -0.165f), 0.015f, 0.015f, dark);
            S(PrimitiveType.Cube, "seat_clamp", seatTop + new Vector3(0f, 0.012f, 0f), new Vector3(0.05f, 0.022f, 0.03f), silver);
            VehicleMesh.Add(lean, "saddle", VehicleMesh.Loft(new[]
            {
                new Station(-0.29f, 0.05f, 1.075f, 1.104f, 2.8f),
                new Station(-0.24f, 0.075f, 1.072f, 1.108f, 3f),
                new Station(-0.14f, 0.05f, 1.073f, 1.105f, 3f),
                new Station(-0.04f, 0.026f, 1.078f, 1.1f, 2.6f),
                new Station(0.0f, 0.016f, 1.08f, 1.098f, 2.4f),
            }, 14), seat);
            S(PrimitiveType.Cube, "saddle_rails", new Vector3(0f, 1.07f, -0.16f), new Vector3(0.04f, 0.01f, 0.2f), silver);

            // ---- the fork: a crown, chrome stanchions into white lowers, a brace, a disc brake -----------------------------------
            var crown = new Vector3(0f, 0.755f, 0.46f);
            Tu("fork_crown", crown + new Vector3(-0.058f, 0f, 0f), crown + new Vector3(0.058f, 0f, 0f), 0.02f, 0.02f, dark);
            Tu("fork_steerer", headLow, crown, 0.016f, 0.016f, dark, false);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 up = new Vector3(side * 0.058f, 0.755f, 0.46f), mid = new Vector3(side * 0.06f, 0.575f, 0.535f), hub = front + new Vector3(side * 0.062f, 0f, 0f);
                Tu("fork_stanchion", up, mid, 0.0175f, 0.0175f, chrome);
                Tu("fork_lower", mid, hub, 0.026f, 0.021f, cream);
            }
            Tu("fork_brace", new Vector3(-0.06f, 0.52f, 0.555f), new Vector3(0.06f, 0.52f, 0.555f), 0.011f, 0.011f, cream, false);
            // disc brakes: a ring rotor with a hub spider and a small caliper on each wheel (the left side)
            Mesh rotorFront = VehicleMesh.Tyre(0.088f, 0.004f, 0.062f, 28), rotorRear = VehicleMesh.Tyre(0.078f, 0.004f, 0.055f, 28);
            VehicleMesh.Add(lean, "rotor_front", rotorFront, silver, front + new Vector3(-0.07f, 0f, 0f));
            VehicleMesh.Add(lean, "rotor_rear", rotorRear, silver, rear + new Vector3(-0.07f, 0f, 0f));
            S(PrimitiveType.Cylinder, "rotor_hub_front", front + new Vector3(-0.07f, 0f, 0f), new Vector3(0.12f, 0.004f, 0.12f), dark, new Vector3(0f, 0f, 90f));
            S(PrimitiveType.Cylinder, "rotor_hub_rear", rear + new Vector3(-0.07f, 0f, 0f), new Vector3(0.105f, 0.004f, 0.105f), dark, new Vector3(0f, 0f, 90f));
            S(PrimitiveType.Cube, "caliper_front", front + new Vector3(-0.078f, 0.07f, -0.035f), new Vector3(0.022f, 0.036f, 0.04f), dark);
            S(PrimitiveType.Cube, "caliper_rear", rear + new Vector3(-0.073f, 0.062f, 0.035f), new Vector3(0.022f, 0.036f, 0.04f), dark);

            // ---- handlebars: they turn with the steering (GroundVehicle turns this node about its own up axis) -------------------------
            var pivot = new Vector3(0f, 0f, 0.40f);
            var bars = new GameObject("handlebars").transform;
            bars.SetParent(lean, false);
            bars.localPosition = pivot;
            Transform TB(string n, Vector3 a, Vector3 b, float r0, float r1, Material m, bool joints = true) => VehicleKit.Tube(bars, n, a - pivot, b - pivot, r0, r1, m, joints);
            Transform SB(PrimitiveType type, string n, Vector3 at, Vector3 size, Material m, Vector3 euler = default) => VehicleKit.Shape(bars, type, n, at - pivot, size, m, euler);
            TB("stem", headTop + new Vector3(0f, 0.012f, 0f), new Vector3(0f, 0.972f, 0.46f), 0.021f, 0.019f, dark);
            SB(PrimitiveType.Cylinder, "headset_cap", headTop + new Vector3(0f, 0.03f, 0f), new Vector3(0.05f, 0.012f, 0.05f), silver);
            foreach (float side in new[] { -1f, 1f })
            {
                TB("bar", new Vector3(side * 0.04f, 0.978f, 0.46f), new Vector3(side * 0.34f, 0.99f, 0.405f), 0.0115f, 0.0115f, dark);
                TB("grip", new Vector3(side * 0.22f, 0.9865f, 0.4275f), new Vector3(side * 0.37f, 0.992f, 0.4f), 0.0185f, 0.0185f, dark, false);
                TB("lever", new Vector3(side * 0.205f, 0.985f, 0.43f), new Vector3(side * 0.2f, 0.965f, 0.52f), 0.007f, 0.005f, silver);
                Seat(bars, side < 0 ? "grip_l" : "grip_r", new Vector3(side * 0.295f, 0.989f, 0.414f) - pivot);
                SB(PrimitiveType.Cube, "shifter", new Vector3(side * 0.15f, 0.97f, 0.445f), new Vector3(0.04f, 0.022f, 0.045f), dark);
                TB("cable", new Vector3(side * 0.2f, 0.968f, 0.47f), new Vector3(side * 0.03f, 0.88f, 0.44f), 0.003f, 0.003f, dark, false);
            }
            SB(PrimitiveType.Sphere, "bell", new Vector3(-0.1f, 0.995f, 0.45f), new Vector3(0.045f, 0.035f, 0.045f), chrome);
            SB(PrimitiveType.Cube, "lamp_housing", new Vector3(0f, 1.005f, 0.515f), new Vector3(0.06f, 0.046f, 0.05f), dark);
            SB(PrimitiveType.Sphere, "lens_head", new Vector3(0f, 1.005f, 0.54f), new Vector3(0.05f, 0.04f, 0.03f), lensHead);
            var beam = new GameObject("headlight").transform;
            beam.SetParent(bars, false);
            beam.localPosition = new Vector3(0f, 1.01f, 0.58f) - pivot;
            beam.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.1f, 1f));
            var head = beam.gameObject.AddComponent<Light>();
            head.type = LightType.Spot; head.range = 26f; head.spotAngle = 55f; head.intensity = 1.8f;
            head.color = new Color(1f, 0.96f, 0.85f); head.shadows = LightShadows.None; head.enabled = false;

            // ---- drivetrain: cranks and pedals that turn when it is pedalled (the pedals stay flat), chain ring, chain, cassette, derailleur ----
            var cranks = new GameObject("cranks").transform;
            cranks.SetParent(lean, false);
            cranks.localPosition = bb;
            foreach (float side in new[] { -1f, 1f })
            {
                VehicleKit.Tube(cranks, "crank", new Vector3(side * 0.095f, 0f, 0f), new Vector3(side * 0.095f, -side * 0.17f, 0f), 0.014f, 0.011f, chrome);
                var pedal = VehicleKit.Shape(cranks, PrimitiveType.Cube, "pedal", new Vector3(side * 0.175f, -side * 0.17f, 0f), new Vector3(0.1f, 0.022f, 0.095f), dark);
                Seat(pedal, side < 0 ? "foot_l" : "foot_r", new Vector3(0f, 0.09f / 0.022f, 0f));   // (the ankle, above the pedal: it rides the pedal, so the legs pedal round)
            }
            VehicleKit.Shape(cranks, PrimitiveType.Cylinder, "chainring", new Vector3(0.085f, 0f, 0f), new Vector3(0.2f, 0.007f, 0.2f), silver, new Vector3(0f, 0f, 90f));
            VehicleKit.Shape(cranks, PrimitiveType.Cylinder, "chainring_inner", new Vector3(0.07f, 0f, 0f), new Vector3(0.13f, 0.007f, 0.13f), dark, new Vector3(0f, 0f, 90f));
            for (int k = 0; k < 5; k++)
            {
                float a = k * 72f * Mathf.Deg2Rad;
                VehicleKit.Shape(cranks, PrimitiveType.Cylinder, "ring_bolt", new Vector3(0.093f, Mathf.Cos(a) * 0.05f, Mathf.Sin(a) * 0.05f), new Vector3(0.014f, 0.004f, 0.014f), chrome, new Vector3(0f, 0f, 90f));
            }
            var cassetteAt = rear + new Vector3(0.04f, 0f, 0f);
            for (int k = 0; k < 5; k++)
                S(PrimitiveType.Cylinder, "cassette", cassetteAt + new Vector3(k * 0.009f, 0f, 0f), Vector3.one * 0.0f + new Vector3(0.11f - k * 0.014f, 0.004f, 0.11f - k * 0.014f), k % 2 == 0 ? silver : dark, new Vector3(0f, 0f, 90f));
            Tu("chain_top", new Vector3(0.085f, 0.405f, -0.02f), cassetteAt + new Vector3(0.015f, 0.075f, 0f), 0.006f, 0.006f, dark, false);
            Tu("chain_low", new Vector3(0.085f, 0.215f, -0.02f), rear + new Vector3(0.05f, -0.16f, -0.05f), 0.006f, 0.006f, dark, false);
            S(PrimitiveType.Cube, "derailleur", rear + new Vector3(0.055f, -0.07f, -0.02f), new Vector3(0.03f, 0.1f, 0.04f), dark);
            Tu("derailleur_cage", rear + new Vector3(0.055f, -0.1f, -0.03f), rear + new Vector3(0.055f, -0.19f, -0.05f), 0.006f, 0.006f, silver, false);
            S(PrimitiveType.Cylinder, "pulley", rear + new Vector3(0.055f, -0.19f, -0.05f), new Vector3(0.04f, 0.006f, 0.04f), silver, new Vector3(0f, 0f, 90f));
            S(PrimitiveType.Cylinder, "pulley", rear + new Vector3(0.055f, -0.1f, -0.03f), new Vector3(0.03f, 0.006f, 0.03f), silver, new Vector3(0f, 0f, 90f));

            // ---- the rest: a rear brake lever's cable run, a bottle on the down tube, the kickstand, the tail reflector --------------------------
            Vector3 dt0 = new Vector3(0f, 0.80f, 0.44f), dt1 = bb;
            Vector3 along = (dt1 - dt0).normalized, perp = new Vector3(0f, -along.z, along.y).normalized * (along.y < 0 ? 1f : -1f);
            Vector3 cage = Vector3.Lerp(dt0, dt1, 0.5f) + perp * 0.043f;
            Tu("bottle", cage - along * 0.095f, cage + along * 0.095f, 0.032f, 0.032f, cream);
            S(PrimitiveType.Cylinder, "bottle_cap", cage - along * 0.108f, new Vector3(0.03f, 0.014f, 0.03f), red, along.y < 0 ? new Vector3(-90f, 0f, 0f) : Vector3.zero);
            // kickstand: down while parked (GroundVehicle tucks it up when it is ridden)
            var kick = new GameObject("kickstand").transform;
            kick.SetParent(lean, false);
            kick.localPosition = new Vector3(-0.04f, 0.30f, -0.10f);
            VehicleKit.Tube(kick, "kickstand_leg", Vector3.zero, new Vector3(-0.01f, -0.30f, -0.09f), 0.012f, 0.012f, dark, false);
            VehicleKit.Shape(kick, PrimitiveType.Cube, "kickstand_foot", new Vector3(-0.012f, -0.298f, -0.095f), new Vector3(0.05f, 0.012f, 0.05f), dark);

            S(PrimitiveType.Cube, "lens_tail", new Vector3(0f, 0.97f, -0.205f), new Vector3(0.05f, 0.045f, 0.02f), lensTail);
            var tail = new GameObject("taillight").transform;
            tail.SetParent(lean, false);
            tail.localPosition = new Vector3(0f, 0.97f, -0.25f);
            var tl = tail.gameObject.AddComponent<Light>();
            tl.type = LightType.Point; tl.range = 2.5f; tl.intensity = 0.4f; tl.color = new Color(1f, 0.1f, 0.05f); tl.shadows = LightShadows.None; tl.enabled = false;

            // ---- wheels: wheelN steers and rides the suspension (GroundVehicle), its "spin" child turns. A thin tyre with knobby
            // tread, a chrome rim ring, laced spokes, a hub. ------------------------------------------------------------------------
            Mesh tyreMesh = VehicleMesh.Tyre(WheelRadius, WheelWidth, RimRadius);
            Mesh rimMesh = VehicleMesh.Tyre(RimRadius + 0.004f, 0.034f, RimRadius - 0.022f, 36);
            for (int i = 0; i < Wheels.Length; i++)
            {
                var w = new GameObject("wheel" + i).transform;
                w.SetParent(lean, false);
                w.localPosition = Wheels[i];
                var spin = new GameObject("spin").transform;
                spin.SetParent(w, false);
                VehicleMesh.Add(spin, "tyre", tyreMesh, tyre);
                VehicleMesh.Add(spin, "rim", rimMesh, silver);
                for (int k = 0; k < 40; k++)
                {
                    var turn = new GameObject("tread").transform;
                    turn.SetParent(spin, false);
                    turn.localRotation = Quaternion.Euler(k * (360f / 40f), 0f, 0f);
                    foreach (float half in new[] { -1f, 1f })
                    {
                        Transform block = VehicleKit.Shape(turn, PrimitiveType.Cube, "lug", new Vector3(half * WheelWidth * 0.2f, WheelRadius - 0.001f, 0f), new Vector3(WheelWidth * 0.3f, 0.011f, 0.022f), tyre);
                        block.localRotation = Quaternion.Euler(0f, half * 30f, 0f);
                    }
                }
                for (int k = 0; k < 18; k++)
                {
                    var turn = new GameObject("spoke").transform;
                    turn.SetParent(spin, false);
                    turn.localRotation = Quaternion.Euler(k * 20f, 0f, 0f);
                    float lace = k % 2 == 0 ? 1f : -1f;
                    Transform wire = VehicleKit.Shape(turn, PrimitiveType.Cube, "spoke_wire", new Vector3(lace * 0.012f, (RimRadius - 0.02f) * 0.5f + 0.012f, 0f), new Vector3(0.0045f, RimRadius - 0.04f, 0.0045f), silver);
                    wire.localRotation = Quaternion.Euler(0f, 0f, lace * -2.5f);
                }
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "hub", Vector3.zero, new Vector3(0.05f, 0.05f, 0.05f), silver, new Vector3(0f, 0f, 90f));
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "hub_flange", new Vector3(0.028f, 0f, 0f), new Vector3(0.065f, 0.006f, 0.065f), silver, new Vector3(0f, 0f, 90f));
                VehicleKit.Shape(spin, PrimitiveType.Cylinder, "hub_flange", new Vector3(-0.028f, 0f, 0f), new Vector3(0.065f, 0.006f, 0.065f), silver, new Vector3(0f, 0f, 90f));
            }

            Seat(lean, "seat", SeatPoint);
        }
    }
}
