using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// The pose of a rider: both hands on the handle, gripping it, and the legs hanging and swinging a little. The game has no hanging
    /// animation, so the bones are turned by hand every frame after the animation has run: each bone is aimed at where its child bone should be
    /// (worked out from the bone positions, so the rig's own axes do not matter). The arms reach for the handle with a two-bone solve (the
    /// elbows bend out and down), and each finger wraps round the handle in three turns.
    /// </summary>
    internal static class Pose
    {
        private static Transform _root, _lArm, _lFore, _lHand, _rArm, _rFore, _rHand, _lUp, _lLeg, _lFoot, _rUp, _rLeg, _rFoot;
        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Pinky" };

        public static bool Ready => _lHand != null && _rHand != null;

        public static void Begin(Player player)
        {
            _root = player.transform.Find("Visual/Armature");
            if (_root == null) return;
            _lArm = Find("LeftArm"); _lFore = Find("LeftForeArm"); _lHand = Find("LeftHand");
            _rArm = Find("RightArm"); _rFore = Find("RightForeArm"); _rHand = Find("RightHand");
            _lUp = Find("LeftUpLeg"); _lLeg = Find("LeftLeg"); _lFoot = Find("LeftFoot");
            _rUp = Find("RightUpLeg"); _rLeg = Find("RightLeg"); _rFoot = Find("RightFoot");
        }

        public static void End() { _lHand = _rHand = null; _root = null; }

        private static Transform Find(string name) => Search(_root, name);

        private static Transform Search(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = Search(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Turn a bone so that the line from it to its child points this way.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            if (bone == null || child == null) return;
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-6f || direction.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(now.normalized, direction.normalized) * bone.rotation;
        }

        /// <summary>
        /// Both hands on the handle, which runs along "axis" through "centre" (up for an axe, across you for a bar), gripping it, the legs
        /// hanging. Every frame while riding, after the animation.
        /// </summary>
        public static void Apply(Transform carriage, Vector3 centre, Vector3 axis, bool vertical)
        {
            if (!Ready) return;
            Vector3 up = Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(carriage.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);

            // the hands: one above the other on a vertical handle, side by side on a bar
            Vector3 leftTarget = vertical ? centre + up * 0.13f : centre - right * 0.20f;
            Vector3 rightTarget = vertical ? centre - up * 0.09f : centre + right * 0.20f;
            Reach(_lArm, _lFore, _lHand, leftTarget, -right * 0.8f - up * 0.5f + forward * 0.2f);
            Reach(_rArm, _rFore, _rHand, rightTarget, right * 0.8f - up * 0.5f + forward * 0.2f);
            Wrap(_lHand, "Left", centre, axis, -1f);
            Wrap(_rHand, "Right", centre, axis, 1f);

            // the legs hang, the knees a little forward, swinging with the ride
            float swing = Mathf.Sin(Time.time * 2.3f) * 0.22f + Mathf.Sin(Time.time * 4.1f) * 0.06f;
            Aim(_lUp, _lLeg, -up + forward * (0.26f + swing * 0.6f) - right * 0.04f);
            Aim(_lLeg, _lFoot, -up - forward * (0.30f - swing) - right * 0.02f);
            Aim(_rUp, _rLeg, -up + forward * (0.26f - swing * 0.6f) + right * 0.04f);
            Aim(_rLeg, _rFoot, -up - forward * (0.30f + swing) + right * 0.02f);
        }

        /// <summary>The arm reaches the target: the two bones are laid out by the law of cosines, the elbow bent toward "pole".</summary>
        private static void Reach(Transform arm, Transform fore, Transform hand, Vector3 target, Vector3 pole)
        {
            if (arm == null || fore == null || hand == null) return;
            Vector3 shoulder = arm.position;
            float l1 = Vector3.Distance(arm.position, fore.position), l2 = Vector3.Distance(fore.position, hand.position) + 0.03f;
            Vector3 toTarget = target - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, 0.08f, l1 + l2 - 0.003f);
            Vector3 dir = toTarget.normalized;
            float a = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.down;
            Vector3 elbow = shoulder + dir * a + bend.normalized * h;
            Aim(arm, fore, elbow - shoulder);
            Aim(fore, hand, shoulder + dir * d - fore.position);
        }

        /// <summary>The fingers wrap round the handle: each joint turned a little further round it, the thumb the other way.</summary>
        private static void Wrap(Transform hand, string side, Vector3 centre, Vector3 axis, float sign)
        {
            if (hand == null) return;
            foreach (string finger in Fingers) Curl(hand, side + "Hand" + finger, centre, axis, sign, new[] { 18f, 70f, 120f });
            Curl(hand, side + "HandThumb", centre, axis, -sign, new[] { 20f, 50f, 80f });
        }

        private static void Curl(Transform hand, string name, Vector3 centre, Vector3 axis, float sign, float[] angles)
        {
            Transform b1 = Search(hand, name + "1"), b2 = Search(hand, name + "2"), b3 = Search(hand, name + "3"), tip = Search(hand, name + "3_end");
            if (b1 == null || b2 == null || b3 == null) return;
            Vector3 onLine = centre + axis * Vector3.Dot(b1.position - centre, axis);   // the nearest point of the handle to the knuckle
            Vector3 toward = onLine - b1.position;
            if (toward.sqrMagnitude < 1e-6f) return;
            toward.Normalize();
            Aim(b1, b2, Quaternion.AngleAxis(angles[0] * sign, axis) * toward);
            Aim(b2, b3, Quaternion.AngleAxis(angles[1] * sign, axis) * toward);
            if (tip != null) Aim(b3, tip, Quaternion.AngleAxis(angles[2] * sign, axis) * toward);
        }
    }
}
