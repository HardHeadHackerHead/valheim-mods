using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Bends the player's arms to reach a point (a two-bone solve on the rig's upper arm and forearm), after the animation has posed them:
    /// the game has no stance with two arms out in front each holding its own gun, so a pair of pistols is held by moving the hands there
    /// ourselves every frame (Aim.Pose). The animation puts the arms back by itself the next frame, so nothing needs undoing.
    /// </summary>
    internal static class Arms
    {
        private static bool _logged;

        /// <summary>How far the arm reaches (upper arm and forearm), or 0.</summary>
        internal static float Length(Transform joint)
        {
            Transform fore = Fore(joint);
            Transform upper = fore != null ? fore.parent : null;
            return upper == null ? 0f : (fore.position - upper.position).magnitude + (joint.position - fore.position).magnitude;
        }

        /// <summary>The forearm bone above a hand joint (the game's joint is the hand or a child of it), or null.</summary>
        internal static Transform Fore(Transform joint)
        {
            for (Transform t = joint; t != null; t = t.parent)
                if (t.name.IndexOf("ForeArm", System.StringComparison.OrdinalIgnoreCase) >= 0) return t;
            return null;
        }

        /// <summary>The upper arm bone (the shoulder end of the arm) above a hand joint, or null.</summary>
        internal static Transform Upper(Transform joint) => Fore(joint)?.parent;

        /// <summary>Moves the hand joint to the target if the arm can reach (as near as it can when it can't); pole is the way the elbow points.</summary>
        internal static bool Reach(Transform joint, Vector3 target, Vector3 pole)
        {
            Transform fore = Fore(joint);
            Transform upper = fore != null ? fore.parent : null;
            if (upper == null) return false;
            if (!_logged)
            {
                _logged = true;
                Plugin.Log.LogInfo($"arm bones: {upper.name} > {fore.name} > {joint.name}");
            }
            Vector3 s = upper.position, e = fore.position, h = joint.position;
            float a = (e - s).magnitude, b = (h - e).magnitude;
            if (a < 0.01f || b < 0.01f) return false;
            Vector3 toTarget = target - s;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(a - b) + 0.01f, (a + b) * 0.999f);
            Vector3 dir = toTarget.normalized;
            Vector3 side = Vector3.ProjectOnPlane(pole, dir);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.down;
            side.Normalize();
            float along = (a * a - b * b + d * d) / (2f * d);
            float out_ = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
            Vector3 elbow = s + dir * along + side * out_;

            upper.rotation = Quaternion.FromToRotation(e - s, elbow - s) * upper.rotation;   // the upper arm turns to point at the new elbow
            Vector3 hand = joint.position;                                                   // (the forearm and hand went with it)
            fore.rotation = Quaternion.FromToRotation(hand - fore.position, s + dir * d - fore.position) * fore.rotation;   // the forearm turns to the hand's place
            return true;
        }
    }
}
