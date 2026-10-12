using System.Collections.Generic;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// How a rider sits on a land vehicle. The game's seat animation (attach_chair, which the game shares with every player) does the hips;
    /// on top of it, every frame after the animation, the bones are turned so the rider's hands are on the grips and their feet on the
    /// pegs or pedals (found in the model by name: grip_l, grip_r, foot_l, foot_r; on a bike the foot spots ride on the pedals, so the
    /// legs pedal round), leaning forward over the bars, the head up to see where they're going, the body and head leaning into turns.
    /// A passenger behind (pgrip_l, pgrip_r, pfoot_l, pfoot_r) holds on round the rider's waist, or sits with hands in front of them.
    ///
    /// Every game does this for every rider it can see (it knows who rides from the vehicle's saved data), so everyone with the mod sees
    /// the same. Adapted from JetSkis' Rider.cs (the same bones, found and turned the same way as Parkour does).
    /// Shared source (mods/Shared/VehicleRider.cs), linked into FourWheelers and MountainBikes; each mod's copy keeps its own numbers
    /// (<see cref="Tune"/>, from the vehicle's spec) and is driven by the plugin's LateUpdate (<see cref="Late"/>).
    /// </summary>
    internal static class VehicleRider
    {
        /// <summary>The pose's numbers, changeable while testing (the mod's test command's "tune"): degrees, and metres in the vehicle's body.</summary>
        internal static readonly Dictionary<string, float> Tune = new Dictionary<string, float>
        {
            { "lean", 20f },        // the back bends forward this much over the bars
            { "head", 14f },        // ...and the neck back up by this much, to look ahead
            { "gripBack", 0.03f },  // the wrist is this far toward the rider from the grip's middle
            { "gripDown", 0.03f },  // ...and this far below it
            { "kneeOut", 0.5f },    // how far the knees splay out (against forward)
            { "elbowOut", 0.7f },   // how far the elbows point out (against down)
            { "hipsDown", 0.0f },   // the hips moved down onto the seat (the seat animation's height can sit them too high)
            { "hipsBack", 0.0f },   // ...and back along it
            { "footUp", 0.0f },     // the ankles this far above the foot spots
            { "steerLean", 6f },    // the body leans this many degrees at full lock and full speed (on top of the vehicle's own lean)
            { "toeDown", 0.25f },   // how far the toes point down
            { "rock", 0.0f },       // the hips rock side to side with the pedals (a bike)
        };
        internal static bool Enabled = true;
        private static bool _tuned;

        private sealed class Body
        {
            public Player Player;
            public Transform Root, Hips, Spine1, Spine2, Neck, Head;
            public Transform LArm, LFore, LHand, RArm, RFore, RHand;
            public Transform LLeg, LKnee, LFoot, LToe, RLeg, RKnee, RFoot, RToe;
            public Vector3 LToeAxis, LTopAxis, RToeAxis, RTopAxis;
            public float W;   // how far into the pose (eases in as they climb on)
            public Transform[] Touched;                 // every bone the pose turns (and the hips, which it moves)
            public Quaternion[] AnimRot, WroteRot;      // each one's rotation as the animation left it, and as the pose left it
            public Vector3 AnimHips, WroteHips;
            public bool Wrote;
            public Animator Anim;
            public AnimatorUpdateMode OldMode;
            public bool ModeSet;   // animated every frame while riding (see Riding)
            public int PosedAt;    // the frame it was last posed in
            public float Seed;     // (each body bobs its own way)
        }

        private static readonly Dictionary<Player, Body> Bodies = new Dictionary<Player, Body>();
        private static readonly List<Player> Gone = new List<Player>();
        private static long _markShown;

        /// <summary>The vehicle's own numbers on top of the defaults (once, when the mod starts).</summary>
        internal static void Init(VehicleSpec spec)
        {
            if (_tuned || spec == null || spec.Pose == null) return;
            foreach (KeyValuePair<string, float> kv in spec.Pose) Tune[kv.Key] = kv.Value;
            _tuned = true;
        }

        internal static float Val(string key) => Tune.TryGetValue(key, out float v) ? v : 0f;

        internal static void Reset()
        {
            MarkLocal(leaving: true);
            foreach (Body b in Bodies.Values) Riding(b, false);
            Bodies.Clear();
            _tuned = false;
        }

        /// <summary>
        /// The game animates its characters in its physics steps, not every frame drawn. The cape (MagicaCloth) reads the bones at its own
        /// moment in the frame: in frames with a physics step it saw the animation's upright back, in the others our leant one, and shook
        /// between them. While riding, the rider is animated every frame, so every frame is the same; put back when they get off.
        /// </summary>
        private static void Riding(Body b, bool on)
        {
            if (on && !b.ModeSet)
            {
                if (b.Anim == null) b.Anim = b.Player.GetComponentInChildren<Animator>();
                if (b.Anim == null) return;
                b.OldMode = b.Anim.updateMode;
                b.Anim.updateMode = AnimatorUpdateMode.Normal;
                b.ModeSet = true;
            }
            else if (!on && b.ModeSet)
            {
                if (b.Anim != null) b.Anim.updateMode = b.OldMode;
                b.ModeSet = false;
            }
        }

        /// <summary>Every frame after the animation: each rider near enough to see is posed on their vehicle.</summary>
        internal static void Late()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // (a dedicated server draws nobody: no bones to pose)
            MarkLocal();
            Gone.Clear();
            foreach (KeyValuePair<Player, Body> kv in Bodies) if (kv.Key == null) Gone.Add(kv.Key);
            foreach (Player p in Gone) Bodies.Remove(p);
            if (!Enabled) return;
            foreach (GroundVehicle v in GroundVehicle.Loaded)
            {
                if (v == null) continue;
                Player rider = v.RiderNow();
                if (rider != null) Seat(rider, v.Seat);
                Body rb = rider != null ? Prepare(rider) : null;
                if (rb != null) { PoseRider(rb, v); Remember(rb); if (rider == Player.m_localPlayer) Snapshot(rb, v); }
                Player passenger = v.PassengerSeat != null ? v.PassengerNow() : null;
                if (passenger != null && passenger != rider) Seat(passenger, v.PassengerSeat);
                Body pb = passenger != null && passenger != rider ? Prepare(passenger) : null;
                if (pb != null) { PosePassenger(pb, v, rb); Remember(pb); }
            }
            // (anyone not on a vehicle this frame starts from nothing next time)
            foreach (KeyValuePair<Player, Body> kv in Bodies)
                if (kv.Key != null && kv.Value.PosedAt != Time.frameCount) { kv.Value.W = 0f; kv.Value.Wrote = false; Riding(kv.Value, false); }
        }

        /// <summary>Every frame on each game: marks (or unmarks) this game's player as a passenger, on their own data, when that changes.</summary>
        internal static void MarkLocal(bool leaving = false)
        {
            Player me = Player.m_localPlayer;
            ZNetView view = me != null ? me.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || GroundVehicle.Spec == null) return;
            long want = 0L;   // (the vehicle's own id, or 0: not a passenger)
            if (!leaving)
                foreach (GroundVehicle v in GroundVehicle.Loaded)
                    if (v != null && v.PassengerSeat != null && v.PassengerNow() == me) { want = v.VehicleId; break; }
            if (want == _markShown) return;
            view.GetZDO().Set(GroundVehicle.Spec.Id + "_passenger", want);
            _markShown = want;
        }

        /// <summary>The vehicle this player sits behind the rider on now, or null.</summary>
        internal static GroundVehicle PassengerOf(Player p)
        {
            foreach (GroundVehicle v in GroundVehicle.Loaded) if (v != null && v.PassengerSeat != null && v.PassengerNow() == p) return v;
            return null;
        }

        /// <summary>The vehicle this player has the controls of now, or null.</summary>
        internal static GroundVehicle RidingOf(Player p)
        {
            foreach (GroundVehicle v in GroundVehicle.Loaded) if (v != null && v.RiderNow() == p) return v;
            return null;
        }

        /// <summary>
        /// Another game's player on a vehicle is put exactly on this game's copy of their seat: their place reaches this game a moment
        /// late, so on a moving vehicle they'd trail behind it. (This game's own player is already there: the game holds them to the seat.)
        /// </summary>
        private static void Seat(Player p, Transform seat)
        {
            if (p == Player.m_localPlayer || seat == null) return;
            p.transform.SetPositionAndRotation(seat.position, seat.rotation);
        }

        /// <summary>A player about to be posed this frame: rigged, animated every frame, back to the animation's own pose (null: can't be).</summary>
        private static Body Prepare(Player p)
        {
            if (!Bodies.TryGetValue(p, out Body b)) Bodies[p] = b = new Body { Player = p, Seed = Random.value * 100f };
            if (b.Root == null) Rig(b);
            if (b.Root == null || b.LHand == null || b.RHand == null || b.LFoot == null || b.RFoot == null) return null;
            b.W = Mathf.MoveTowards(b.W, 1f, Time.deltaTime / 0.25f);
            b.PosedAt = Time.frameCount;
            Riding(b, true);
            FromAnimation(b);
            return b;
        }

        /// <summary>How the ride moves a body: a little bobbing at speed, the steering (-1 to 1), how fast (0 to 1).</summary>
        private static void Ride(Body b, GroundVehicle v, Vector3 up, out float steer, out float fast)
        {
            steer = v.Steer;
            fast = Mathf.Clamp01(Mathf.Abs(v.Speed) / Mathf.Max(1f, GroundVehicle.Spec.Top()));
            float t = Time.time;
            float bob = ((Mathf.PerlinNoise(t * 3.7f, b.Seed) - 0.5f) * 0.05f + Mathf.Sin(t * 13f + b.Seed) * 0.006f) * fast;
            b.Hips.position += up * (bob * b.W);
        }

        /// <summary>
        /// The game animates its characters in its physics steps (about 50 a second), but this runs every frame drawn. In a frame with no
        /// physics step the bones still hold last frame's pose, ours included: turning them again would add the lean twice, then snap back
        /// at the next step (the body shook back and forth). So each bone that still holds what we wrote is put back to the animation's own
        /// pose first; one the animation has rewritten is taken as its new pose.
        /// </summary>
        private static void FromAnimation(Body b)
        {
            if (b.Touched == null)
            {
                b.Touched = new[] { b.Spine1, b.Spine2, b.Neck, b.Head, b.LArm, b.LFore, b.LHand, b.RArm, b.RFore, b.RHand, b.LLeg, b.LKnee, b.LFoot, b.RLeg, b.RKnee, b.RFoot };
                b.AnimRot = new Quaternion[b.Touched.Length];
                b.WroteRot = new Quaternion[b.Touched.Length];
                b.Wrote = false;
            }
            for (int i = 0; i < b.Touched.Length; i++)
            {
                Transform t = b.Touched[i];
                if (t == null) continue;
                if (b.Wrote && t.localRotation == b.WroteRot[i]) t.localRotation = b.AnimRot[i];   // (not animated since: back to the animation's)
                else b.AnimRot[i] = t.localRotation;                                               // (animated: this is the animation's now)
            }
            if (b.Wrote && b.Hips.localPosition == b.WroteHips) b.Hips.localPosition = b.AnimHips;
            else b.AnimHips = b.Hips.localPosition;
        }

        private static void Remember(Body b)
        {
            for (int i = 0; i < b.Touched.Length; i++) if (b.Touched[i] != null) b.WroteRot[i] = b.Touched[i].localRotation;
            b.WroteHips = b.Hips.localPosition;
            b.Wrote = true;
        }

        private static void PoseRider(Body b, GroundVehicle v)
        {
            Transform frame = v.PoseFrame;
            Transform gl = v.Marker("grip_l"), gr = v.Marker("grip_r"), fl = v.Marker("foot_l"), fr = v.Marker("foot_r");
            if (frame == null || gl == null || gr == null) return;
            Vector3 fwd = frame.forward, up = frame.up, right = frame.right;
            float w = b.W;

            // the hips settle onto the seat; on a bike they rock a little with the pedals
            Vector3 shift = (-up * Val("hipsDown") - fwd * Val("hipsBack")) * w;
            if (fl != null && fr != null && Val("rock") != 0f) shift += right * ((fl.position.y - fr.position.y) * Val("rock")) * w;
            if (shift.sqrMagnitude > 0f) b.Hips.position += shift;

            Ride(b, v, up, out float steer, out float fast);

            // lean forward over the bars, the head back up to look ahead; into a turn, the body leans with it and the head looks where it goes
            Turn(b.Spine1, right, Val("lean") * 0.5f * w);
            Turn(b.Spine2, right, Val("lean") * 0.5f * w);
            Turn(b.Spine1, fwd, -steer * (1f + Val("steerLean") * fast) * w);
            Turn(b.Neck, right, -Val("head") * 0.6f * w);
            Turn(b.Head, right, -Val("head") * 0.4f * w);
            Turn(b.Neck, up, steer * 10f * w);
            Turn(b.Head, up, steer * 14f * w);

            // hands on the grips (they follow the bars as they turn), elbows out and down
            foreach (int side in new[] { -1, 1 })
            {
                Transform grip = side < 0 ? gl : gr;
                Vector3 target = grip.position - fwd * Val("gripBack") - up * Val("gripDown");
                Vector3 elbow = -up + right * side * Val("elbowOut") - fwd * 0.3f;
                if (side < 0) Reach(b.LArm, b.LFore, b.LHand, target, elbow, w);
                else Reach(b.RArm, b.RFore, b.RHand, target, elbow, w);
            }

            // feet on the pegs or the pedals, knees bent forward and out
            if (fl == null || fr == null) return;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 foot = (side < 0 ? fl : fr).position + up * Val("footUp");
                Vector3 knee = fwd + right * side * Val("kneeOut") + up * 0.3f;
                Vector3 toes = fwd - up * Val("toeDown") + right * side * 0.12f;
                if (side < 0) LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, foot, knee, toes, up, w);
                else LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, foot, knee, toes, up, w);
            }
        }

        /// <summary>
        /// The passenger behind, holding on: leant forward against the rider's back, head turned to the side (resting on it), arms wrapped
        /// round the rider with the hands clasped in front of their waist; feet on the pegs behind the rider's. Alone, they sit up with
        /// their hands on the seat in front of them.
        /// </summary>
        private static void PosePassenger(Body b, GroundVehicle v, Body rider)
        {
            Transform frame = v.PoseFrame;
            if (frame == null) return;
            Vector3 fwd = frame.forward, up = frame.up, right = frame.right;
            float w = b.W;
            Ride(b, v, up, out float steer, out float fast);
            bool hug = rider != null && rider.Hips != null;

            Turn(b.Spine1, right, (hug ? 16f : 6f) * w);
            Turn(b.Spine2, right, (hug ? 14f : 6f) * w);
            Turn(b.Spine1, fwd, -steer * (1f + Val("steerLean") * fast) * w);
            Turn(b.Neck, right, (hug ? -12f : -6f) * w);
            Turn(b.Head, up, (hug ? 28f : 8f + steer * 12f) * w);   // (the cheek against the rider's back, or looking past them)
            Turn(b.Head, fwd, (hug ? -8f : 0f) * w);

            foreach (int side in new[] { -1, 1 })
            {
                Transform grip = v.Marker(side < 0 ? "pgrip_l" : "pgrip_r");
                Vector3 hand, elbow;
                if (hug)
                {
                    // round the rider's waist, the hands meeting in front of their belly
                    Vector3 waist = rider.Hips.position;
                    hand = waist + fwd * 0.15f + right * side * 0.06f + up * 0.13f;
                    elbow = right * side - up * 0.35f - fwd * 0.2f;
                }
                else
                {
                    hand = grip != null ? grip.position : b.Hips.position + fwd * 0.3f + right * side * 0.18f;
                    elbow = right * side * 0.6f - up - fwd * 0.3f;
                }
                if (side < 0) Reach(b.LArm, b.LFore, b.LHand, hand, elbow, w);
                else Reach(b.RArm, b.RFore, b.RHand, hand, elbow, w);

                Transform peg = v.Marker(side < 0 ? "pfoot_l" : "pfoot_r");
                if (peg == null) continue;
                Vector3 foot = peg.position + up * Val("footUp");
                Vector3 knee = fwd + right * side * Val("kneeOut") + up * 0.3f;
                Vector3 toes = fwd - up * Val("toeDown") + right * side * 0.12f;
                if (side < 0) LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, foot, knee, toes, up, w);
                else LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, foot, knee, toes, up, w);
            }
        }

        /// <summary>Where the local rider's bones ended up this frame, after the pose (for the test command), in the vehicle's frame.</summary>
        internal static readonly Dictionary<string, Vector3> Last = new Dictionary<string, Vector3>();

        private static void Snapshot(Body b, GroundVehicle v)
        {
            Transform frame = v.PoseFrame;
            Last.Clear();
            void Put(string name, Transform t) { if (t != null) Last[name] = frame.InverseTransformPoint(t.position); }
            Put("Hips", b.Hips); Put("Head", b.Head); Put("LeftHand", b.LHand); Put("RightHand", b.RHand);
            Put("LeftKnee", b.LKnee); Put("RightKnee", b.RKnee); Put("LeftFoot", b.LFoot); Put("RightFoot", b.RFoot);
            foreach (string m in new[] { "grip_l", "grip_r", "foot_l", "foot_r" }) { Transform t = v.Marker(m); if (t != null) Last["marker " + m] = frame.InverseTransformPoint(t.position); }
        }

        // ---- bones (as Parkour finds and turns them) ---------------------------------------------------------------------------

        private static void Rig(Body b)
        {
            b.Root = b.Player.transform.Find("Visual/Armature");
            if (b.Root == null) return;
            b.Hips = Find(b.Root, "Hips"); b.Spine1 = Find(b.Root, "Spine1"); b.Spine2 = Find(b.Root, "Spine2");
            b.Neck = Find(b.Root, "Neck"); b.Head = Find(b.Root, "Head");
            b.LArm = Find(b.Root, "LeftArm"); b.LFore = Find(b.Root, "LeftForeArm"); b.LHand = Find(b.Root, "LeftHand");
            b.RArm = Find(b.Root, "RightArm"); b.RFore = Find(b.Root, "RightForeArm"); b.RHand = Find(b.Root, "RightHand");
            b.LLeg = Find(b.Root, "LeftUpLeg"); b.LKnee = Find(b.Root, "LeftLeg"); b.LFoot = Find(b.Root, "LeftFoot"); b.LToe = Find(b.Root, "LeftToeBase");
            b.RLeg = Find(b.Root, "RightUpLeg"); b.RKnee = Find(b.Root, "RightLeg"); b.RFoot = Find(b.Root, "RightFoot"); b.RToe = Find(b.Root, "RightToeBase");
            FootAxes(b.Player, b.LFoot, b.LToe, out b.LToeAxis, out b.LTopAxis);
            FootAxes(b.Player, b.RFoot, b.RToe, out b.RToeAxis, out b.RTopAxis);
        }

        /// <summary>A bone's place, for the test command (null when the player isn't rigged yet).</summary>
        internal static Transform Bone(Player p, string name)
        {
            Transform root = p.transform.Find("Visual/Armature");
            return root != null ? Find(root, name) : null;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>A foot's own axes, toward its toes and out of its top, from the rig as the animation has it now.</summary>
        private static void FootAxes(Player p, Transform foot, Transform toe, out Vector3 toeAxis, out Vector3 topAxis)
        {
            toeAxis = topAxis = Vector3.zero;
            if (foot == null) return;
            Transform tip = toe != null ? toe : (foot.childCount > 0 ? foot.GetChild(0) : null);
            Vector3 toward = tip != null && (tip.position - foot.position).sqrMagnitude > 1e-6f ? tip.position - foot.position : p.transform.forward;
            toeAxis = foot.InverseTransformDirection(toward).normalized;
            topAxis = Vector3.ProjectOnPlane(foot.InverseTransformDirection(p.transform.up), toeAxis);
            if (topAxis.sqrMagnitude < 1e-4f) topAxis = Vector3.ProjectOnPlane(foot.InverseTransformDirection(-p.transform.forward), toeAxis);
            topAxis.Normalize();
        }

        private static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>A leg reaches a spot, the knee bent toward "pole", the toes pointing "toes", the sole down ("w" of the way).</summary>
        private static void LegAt(Transform hip, Transform knee, Transform foot, Vector3 toeAxis, Vector3 topAxis, Vector3 target, Vector3 pole, Vector3 toes, Vector3 up, float w)
        {
            Reach(hip, knee, foot, target, pole, w);
            if (w <= 0f || toeAxis == Vector3.zero) return;
            Vector3 top = Vector3.ProjectOnPlane(up, toes);
            if (top.sqrMagnitude < 1e-4f || toes.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(toes.normalized, top.normalized) * Quaternion.Inverse(Quaternion.LookRotation(toeAxis, topAxis));
            foot.rotation = w >= 1f ? want : Quaternion.Slerp(foot.rotation, want, w);
        }

        /// <summary>Turn a bone so the line from it to its child points this way.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-6f || direction.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(now.normalized, direction.normalized) * bone.rotation;
        }

        /// <summary>Two bones reach the target (laid out by the law of cosines, the middle joint bent toward "pole"), "weight" of the way.</summary>
        private static void Reach(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float weight)
        {
            if (upper == null || lower == null || end == null || weight <= 0f) return;
            Quaternion upperWas = upper.rotation, lowerWas = lower.rotation;
            Vector3 root = upper.position;
            float l1 = Vector3.Distance(upper.position, lower.position), l2 = Vector3.Distance(lower.position, end.position);
            Vector3 toTarget = target - root;
            float d = Mathf.Clamp(toTarget.magnitude, 0.08f, l1 + l2 - 0.003f);
            Vector3 dir = toTarget.normalized;
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.down;
            Vector3 joint = root + dir * a + bend.normalized * h;
            Aim(upper, lower, joint - root);
            Aim(lower, end, root + dir * d - lower.position);
            if (weight >= 1f) return;
            upper.rotation = Quaternion.Slerp(upperWas, upper.rotation, weight);
            lower.rotation = Quaternion.Slerp(lowerWas, lower.rotation, weight);
        }
    }
}
