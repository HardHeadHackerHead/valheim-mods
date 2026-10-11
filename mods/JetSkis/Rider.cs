using System.Collections.Generic;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// How a rider sits on a jet ski. The game's seat animation (the attach animation the jet ski asks for, which the game shares with
    /// every player) does the hips; on top of it, every frame after the animation, the bones are turned so the rider straddles the saddle:
    /// leaning forward over the handlebars, both hands on the grips (they follow the bars as they turn), knees bent and spread, feet on the
    /// pads either side of the saddle, the head up to see where they're going.
    ///
    /// Every game does this for every rider it can see (it knows who rides from the jet ski's saved data), so everyone with the mod (which
    /// is everyone: the jet ski needs it) sees the same.
    /// </summary>
    internal static class Rider
    {
        // the pose's numbers, changeable while testing (the "jetski tune" command); metres in the jet ski's body, degrees
        internal static readonly Dictionary<string, float> Tune = new Dictionary<string, float>
        {
            { "lean", 32f },        // the back bends forward this much over the bars
            { "head", 24f },        // ...and the neck back up by this much, to look ahead
            { "gripIn", 0.05f },    // the hands hold the grips this far in from their middles
            { "gripBack", 0.03f },  // ...and this far toward the rider (the wrist is behind the fist)
            { "gripDown", 0.03f },
            { "footX", 0.30f },     // the feet: out from the middle line
            { "footY", 0.08f },     // ...the ankle above the deck
            { "footZ", -0.21f },    // ...along the jet ski (the saddle's middle is at -0.5, the bars at +0.06)
            { "kneeOut", 0.7f },    // how far the knees splay out (against forward)
            { "elbowOut", 0.8f },   // how far the elbows point out (against down)
            { "hipsDown", 0.0f },   // the hips moved down onto the saddle (the seat animation's height can sit them too high)
            { "hipsBack", 0.0f },   // ...and back along it
        };
        internal static bool Enabled = true;

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

        internal static float T(string key) => Tune.TryGetValue(key, out float v) ? v : 0f;

        internal static void Reset()
        {
            JetSki.MarkLocal(leaving: true);
            foreach (Body b in Bodies.Values) Riding(b, false);
            Bodies.Clear();
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

        /// <summary>Every frame after the animation: each rider near enough to see is posed on their jet ski.</summary>
        internal static void Late()
        {
            JetSki.MarkLocal();
            Gone.Clear();
            foreach (KeyValuePair<Player, Body> kv in Bodies) if (kv.Key == null) Gone.Add(kv.Key);
            foreach (Player p in Gone) Bodies.Remove(p);
            if (!Enabled) return;
            foreach (JetSki ski in JetSki.Loaded)
            {
                if (ski == null) continue;
                Player rider = ski.RiderNow();
                if (rider != null) Seat(rider, ski.Controls.m_attachPoint);
                Body rb = rider != null ? Prepare(rider) : null;
                if (rb != null) { Pose(rb, ski); Remember(rb); if (rider == Player.m_localPlayer) Snapshot(rb, ski); }
                Player passenger = ski.PassengerNow();
                if (passenger != null && passenger != rider) Seat(passenger, ski.PassengerSeat);
                Body pb = passenger != null && passenger != rider ? Prepare(passenger) : null;
                if (pb != null) { PosePassenger(pb, ski, rb); Remember(pb); }
            }
            // (anyone not on a jet ski this frame starts from nothing next time)
            foreach (KeyValuePair<Player, Body> kv in Bodies)
                if (kv.Key != null && kv.Value.PosedAt != Time.frameCount) { kv.Value.W = 0f; kv.Value.Wrote = false; Riding(kv.Value, false); }
        }

        /// <summary>
        /// Another game's player on a jet ski is put exactly on this game's copy of their seat: their place reaches this game a moment late,
        /// so on a moving jet ski they'd trail behind it. (This game's own player is already there: the game holds them to the seat.)
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

        /// <summary>How the ride moves a body: bobbing over the waves at speed (more the faster), the steering (-1 to 1), how fast (0 to 1).</summary>
        private static void Ride(Body b, JetSki ski, Vector3 up, out float steer, out float fast)
        {
            steer = ski.Rudder;
            fast = Mathf.Clamp01(Mathf.Abs(ski.Speed) / Mathf.Max(1f, Plugin.TopSpeed.Value));
            if (!Plugin.Lean.Value) { steer = 0f; return; }
            float t = Time.time;
            float bob = ((Mathf.PerlinNoise(t * 3.1f, b.Seed) - 0.5f) * 0.05f + Mathf.Sin(t * 11f + b.Seed) * 0.005f) * fast;
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

        private static void Pose(Body b, JetSki ski)
        {
            Transform body = ski.Body, bars = ski.Bars;
            if (body == null || bars == null) return;
            Vector3 fwd = body.forward, up = body.up, right = body.right;
            float w = b.W;

            // the hips settle onto the saddle
            Vector3 shift = (-up * T("hipsDown") - fwd * T("hipsBack")) * w;
            if (shift.sqrMagnitude > 0f) b.Hips.position += shift;

            Ride(b, ski, up, out float steer, out float fast);

            // lean forward over the bars, the head back up to look ahead; into a turn, the body leans with it and the head looks where it goes
            Turn(b.Spine1, right, T("lean") * 0.5f * w);
            Turn(b.Spine2, right, T("lean") * 0.5f * w);
            Turn(b.Spine1, fwd, -steer * (4f + 8f * fast) * w);
            Turn(b.Neck, right, -T("head") * 0.6f * w);
            Turn(b.Head, right, -T("head") * 0.4f * w);
            Turn(b.Neck, up, steer * 12f * w);
            Turn(b.Head, up, steer * 16f * w);

            // hands on the grips, elbows out and down
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 grip = bars.TransformPoint(new Vector3(side * (0.27f - T("gripIn")), 0f, 0f)) - fwd * T("gripBack") - up * T("gripDown");
                Vector3 elbow = -up + right * side * T("elbowOut") - fwd * 0.3f;
                if (side < 0) Reach(b.LArm, b.LFore, b.LHand, grip, elbow, w);
                else Reach(b.RArm, b.RFore, b.RHand, grip, elbow, w);
            }

            // feet on the pads, knees bent forward and splayed out round the saddle
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 foot = body.TransformPoint(new Vector3(side * T("footX"), Model.DeckHeight(side * T("footX"), T("footZ")) + T("footY"), T("footZ")));
                Vector3 knee = fwd + right * side * T("kneeOut") + up * 0.3f;
                Vector3 toes = fwd - up * 0.25f + right * side * 0.15f;
                if (side < 0) LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, foot, knee, toes, up, w);
                else LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, foot, knee, toes, up, w);
            }
        }

        /// <summary>
        /// The passenger behind, holding on: leant forward against the rider's back, head turned to the side (resting on it), arms wrapped
        /// round the rider with the hands clasped in front of their waist and the elbows hugging their sides; feet on the pads behind the
        /// rider's. Alone, they sit up with their hands on the saddle in front of them.
        /// </summary>
        private static void PosePassenger(Body b, JetSki ski, Body rider)
        {
            Transform body = ski.Body;
            if (body == null) return;
            Vector3 fwd = body.forward, up = body.up, right = body.right;
            float w = b.W;
            Ride(b, ski, up, out float steer, out float fast);
            bool hug = rider != null && rider.Hips != null;

            Turn(b.Spine1, right, (hug ? 16f : 6f) * w);
            Turn(b.Spine2, right, (hug ? 14f : 6f) * w);
            Turn(b.Spine1, fwd, -steer * (4f + 8f * fast) * w);
            Turn(b.Neck, right, (hug ? -12f : -6f) * w);
            Turn(b.Head, up, (hug ? 28f : 8f + steer * 12f) * w);   // (the cheek against the rider's back, or looking past them)
            Turn(b.Head, fwd, (hug ? -8f : 0f) * w);

            foreach (int side in new[] { -1, 1 })
            {
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
                    hand = body.TransformPoint(new Vector3(side * 0.16f, 0.76f, Model.PassengerPoint.z + 0.24f));
                    elbow = right * side * 0.6f - up - fwd * 0.3f;
                }
                if (side < 0) Reach(b.LArm, b.LFore, b.LHand, hand, elbow, w);
                else Reach(b.RArm, b.RFore, b.RHand, hand, elbow, w);

                float fz = Model.PassengerPoint.z + 0.15f, fx = side * 0.3f;
                Vector3 foot = body.TransformPoint(new Vector3(fx, Model.DeckHeight(fx, fz) + T("footY"), fz));
                Vector3 knee = fwd + right * side * T("kneeOut") + up * 0.3f;
                Vector3 toes = fwd - up * 0.25f + right * side * 0.15f;
                if (side < 0) LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, foot, knee, toes, up, w);
                else LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, foot, knee, toes, up, w);
            }
        }

        /// <summary>Where the local rider's bones ended up this frame, after the pose (for the test command), in the jet ski's body.</summary>
        internal static readonly Dictionary<string, Vector3> Last = new Dictionary<string, Vector3>();

        private static void Snapshot(Body b, JetSki ski)
        {
            Last.Clear();
            void Put(string name, Transform t) { if (t != null) Last[name] = ski.Body.InverseTransformPoint(t.position); }
            Put("Hips", b.Hips); Put("Head", b.Head); Put("LeftArm", b.LArm); Put("LeftHand", b.LHand); Put("RightHand", b.RHand);
            Put("LeftKnee", b.LKnee); Put("RightKnee", b.RKnee); Put("LeftFoot", b.LFoot); Put("RightFoot", b.RFoot);
            Last["LeftGrip"] = ski.Body.InverseTransformPoint(ski.Bars.TransformPoint(new Vector3(-0.27f, 0f, 0f)));
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
