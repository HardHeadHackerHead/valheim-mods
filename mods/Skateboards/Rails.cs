using UnityEngine;

namespace Skateboards
{
    /// <summary>
    /// What a board can grind: the top of anything built from a box collider that is thin (a beam, a pole laid flat, the top of a wall or
    /// a fence, a rail), and the edges of a wide one where it drops away (a floor, a step, a table: a ledge; an edge with another floor
    /// beside it is not one). Long enough to slide on (a metre), and level-ish (up to about 50 degrees along its length). Looked for only
    /// near the feet and only when the board is coming down onto it, a few colliders a step.
    /// </summary>
    internal static class Rails
    {
        internal struct Rail
        {
            public Vector3 A, B;
            public Collider Collider;
            public bool Ledge;
            public float Length => (B - A).magnitude;
            public Vector3 Dir => (B - A).normalized;
            public Vector3 Point(float u) => A + Dir * Mathf.Clamp(u, 0f, Length);
        }

        private const float Thin = 0.55f, MinLength = 1f;
        private static readonly Collider[] Hits = new Collider[48];
        private static int _mask;
        private static int Mask => _mask != 0 ? _mask : (_mask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "vehicle"));

        /// <summary>The best rail or ledge for feet here moving like this (skip: the one just left), and how far along it they are.</summary>
        public static bool Find(Vector3 feet, Vector3 v, Collider skip, out Rail best, out float bestU)
        {
            best = default;
            bestU = 0f;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            if (flat.magnitude < 1.2f) return false;
            Vector3 heading = flat.normalized;
            float bestScore = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(feet, 1.2f, Hits, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!(Hits[i] is BoxCollider box) || !box.enabled || box == skip) continue;
                if (box.attachedRigidbody != null && !box.attachedRigidbody.isKinematic && box.GetComponentInParent<Ship>() == null) continue;   // (loose things: items, carts)
                if (box.GetComponentInParent<Character>() != null || box.GetComponentInParent<ItemDrop>() != null) continue;
                Consider(box, feet, heading, ref best, ref bestU, ref bestScore);
            }
            return bestScore < float.MaxValue;
        }

        /// <summary>For the test command: every rail and ledge within a radius (ignoring how the board moves).</summary>
        public static System.Collections.Generic.List<Rail> All(Vector3 at, float radius)
        {
            var found = new System.Collections.Generic.List<Rail>();
            foreach (Collider c in Physics.OverlapSphere(at, radius, Mask, QueryTriggerInteraction.Ignore))
            {
                if (!(c is BoxCollider box) || !box.enabled) continue;
                if (box.GetComponentInParent<Character>() != null || box.GetComponentInParent<ItemDrop>() != null) continue;
                _collect = found;
                Rail none = default; float u = 0f, score = float.MaxValue;
                Consider(box, at, Vector3.forward, ref none, ref u, ref score);
                _collect = null;
            }
            return found;
        }

        private static System.Collections.Generic.List<Rail> _collect;

        private static void Consider(BoxCollider box, Vector3 feet, Vector3 heading, ref Rail best, ref float bestU, ref float bestScore)
        {
            Transform t = box.transform;
            Vector3 scale = t.lossyScale;
            Vector3 c = t.TransformPoint(box.center);
            Vector3[] axis = { t.right, t.up, t.forward };
            float[] half = { Mathf.Abs(box.size.x * scale.x) / 2f, Mathf.Abs(box.size.y * scale.y) / 2f, Mathf.Abs(box.size.z * scale.z) / 2f };
            int vi = 0;
            float vd = 0f;
            for (int k = 0; k < 3; k++) { float d = Mathf.Abs(Vector3.Dot(axis[k], Vector3.up)); if (d > vd) { vd = d; vi = k; } }
            if (vd < 0.6f) return;   // (on its edge: no top to stand on)
            Vector3 up = axis[vi] * Mathf.Sign(Vector3.Dot(axis[vi], Vector3.up));
            Vector3 top = c + up * half[vi];
            int i1 = (vi + 1) % 3, i2 = (vi + 2) % 3;
            int li = half[i1] >= half[i2] ? i1 : i2, si = li == i1 ? i2 : i1;
            Vector3 L = axis[li], S = axis[si];
            float hl = half[li], hs = half[si];
            if (hl * 2f < MinLength) return;

            if (hs * 2f <= Thin)
            {
                Try(box, top - L * hl, top + L * hl, false, feet, heading, ref best, ref bestU, ref bestScore);
                return;
            }
            if (hs * 2f > 12f) return;   // (a huge box is a floor of the world, not a ledge)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 mid = top + S * hs * side;
                if (IsLedge(mid, S * side, L * hl)) Try(box, mid - L * hl, mid + L * hl, true, feet, heading, ref best, ref bestU, ref bestScore);
                if (hs * 2f >= MinLength)
                {
                    Vector3 end = top + L * hl * side;
                    if (IsLedge(end, L * side, S * hs)) Try(box, end - S * hs, end + S * hs, true, feet, heading, ref best, ref bestU, ref bestScore);
                }
            }
        }

        /// <summary>
        /// An edge with a drop beside it: nothing within a quarter metre below its top, just outside it and a little further out, at
        /// two points along it (a seam between two floors has the next floor just beyond it; a floor laid on the ground has the ground).
        /// </summary>
        private static bool IsLedge(Vector3 middle, Vector3 outward, Vector3 halfAlong)
        {
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) return false;
            outward.Normalize();
            int mask = Mask | LayerMask.GetMask("terrain");
            for (int i = -1; i <= 1; i += 2)
                foreach (float outBy in new[] { 0.35f, 0.9f })
                {
                    Vector3 from = middle + halfAlong * (0.5f * i) + outward * outBy + Vector3.up * 0.15f;
                    if (Physics.Raycast(from, Vector3.down, 0.4f, mask, QueryTriggerInteraction.Ignore)) return false;
                }
            return true;
        }

        private static void Try(BoxCollider box, Vector3 a, Vector3 b, bool ledge, Vector3 feet, Vector3 heading, ref Rail best, ref float bestU, ref float bestScore)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < MinLength) return;
            Vector3 dir = ab / len;
            if (Mathf.Abs(dir.y) > 0.77f) return;   // (steeper than 50 degrees)
            if (_collect != null) { _collect.Add(new Rail { A = a, B = b, Collider = box, Ledge = ledge }); return; }
            float u = Vector3.Dot(feet - a, dir);
            if (u < 0.1f || u > len - 0.1f) return;
            Vector3 q = a + dir * u;
            Vector3 d = feet - q;
            float rise = d.y, across = new Vector3(d.x, 0f, d.z).magnitude;
            if (across > 0.5f || rise < -0.3f || rise > 0.55f) return;
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z).normalized;
            float along = Mathf.Abs(Vector3.Dot(heading, flatDir));
            if (along < 0.55f) return;   // (crossing it, not going along it)
            float score = across + Mathf.Abs(rise) * 0.5f - along * 0.3f + (ledge ? 0.1f : 0f);
            if (score >= bestScore) return;
            bestScore = score;
            best = new Rail { A = a, B = b, Collider = box, Ledge = ledge };
            bestU = u;
        }
    }
}
