using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// The body's pose on a web, drawn over the game's own animation each frame (after the animator, before the webs are drawn from the
    /// hands), for every player in sight doing a move: ours from Web.cs, the others' from what they share (Share.cs: the move and the
    /// flips), so players with the mod see each other swing, crawl and flip. Players without the mod see the game's own pose and the lean.
    ///
    ///   Swing  the web arm straight up the web, the other arm out and back for balance, legs trailing on the way down and tucked as the
    ///          swing rises to its top.
    ///   Zip    both arms up the web, legs straight out behind.
    ///   Fly    arms out, legs a little tucked; on a perfect release a flip (a front flip or a corkscrew), knees hugged.
    ///   Crawl  hands up on the wall and feet on it, knees out like a spider's, hand over hand as the body moves.
    ///
    /// Each limb is a two-bone reach (upper arm, forearm, hand; thigh, shin, foot) to a target in the body's own space, eased in and out so
    /// the pose blends from and back to the animation. The flip turns the whole visual round the body's middle (the collider stays upright).
    /// The bones are the animator's humanoid ones (else found by their names).
    /// </summary>
    internal static class Body
    {
        private enum Limb { LeftArm, RightArm, LeftLeg, RightLeg }

        private sealed class Rig
        {
            public Animator Anim;
            public Transform Visual;
            public readonly Transform[][] Chains = new Transform[4][];   // (upper, middle, end) per limb
            public bool Ok;
            public readonly Vector3[] Target = new Vector3[4];           // in the body's own space (eased)
            public readonly bool[] Has = new bool[4];
            public readonly float[] Weight = new float[4];
            public float Tuck, Phase;
            public float FlipAt = -10f;
            public int FlipKind, FlipsSeen = -1;
            public Vector3 VisualPos;
            public Quaternion VisualRot = Quaternion.identity;
            public bool Flipping;
            public Vector3 LastPos, Vel;   // (another player's speed, from how their body moves: their rigidbody isn't simulated here)
        }

        private static readonly Dictionary<Player, Rig> Rigs = new Dictionary<Player, Rig>();
        private const float FlipSeconds = 0.6f;

        /// <summary>The flips the local player has done (shared, so the others play them too).</summary>
        public static int Flips;

        private static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");

        public static void Reset()
        {
            foreach (KeyValuePair<Player, Rig> kv in Rigs) if (kv.Key != null) Restore(kv.Value);
            Rigs.Clear();
        }

        /// <summary>A flip off a perfect release (the local player's): shared with the others through the flip count.</summary>
        public static void Flip(Player p)
        {
            Flips++;
            Rig rig = RigOf(p);
            if (rig != null) StartFlip(rig, Flips);
        }

        private static void StartFlip(Rig rig, int count)
        {
            rig.FlipAt = Time.time;
            rig.FlipKind = count % 3 == 2 ? 1 : 0;   // (mostly front flips, a corkscrew every third)
            rig.FlipsSeen = count;
        }

        // ---- every frame ----

        public static void Late()
        {
            Player local = Player.m_localPlayer;
            Camera cam = Utils.GetMainCamera();
            List<Player> gone = null;
            foreach (KeyValuePair<Player, Rig> kv in Rigs) if (kv.Key == null) (gone = gone ?? new List<Player>()).Add(kv.Key);
            if (gone != null) foreach (Player g in gone) Rigs.Remove(g);

            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null || p.IsDead()) continue;
                if (cam != null && Vector3.Distance(cam.transform.position, p.transform.position) > 80f) continue;
                Move move = p == local ? Web.State : Share.MoveOf(p);
                Rig rig;
                if (move == Move.None && !Rigs.ContainsKey(p)) continue;
                rig = RigOf(p);
                if (rig == null || !rig.Ok) continue;
                if (p != local)
                {
                    int flips = Share.FlipsOf(p);
                    if (rig.FlipsSeen < 0) rig.FlipsSeen = flips;   // (seen first now: not a flip to play)
                    else if (flips != rig.FlipsSeen) StartFlip(rig, flips);
                }
                if (!Plugin.BodyLean.Value) move = Move.None;
                Pose(p, rig, move);
            }
        }

        private static Rig RigOf(Player p)
        {
            if (Rigs.TryGetValue(p, out Rig rig) && rig.Anim != null) return rig;
            rig = new Rig { Anim = AnimatorOf(p), Visual = p.GetVisual() != null ? p.GetVisual().transform : null };
            if (rig.Anim != null && rig.Visual != null)
            {
                rig.Chains[(int)Limb.LeftArm] = Bones(rig.Anim, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "LeftArm", "LeftForeArm", "LeftHand");
                rig.Chains[(int)Limb.RightArm] = Bones(rig.Anim, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "RightArm", "RightForeArm", "RightHand");
                rig.Chains[(int)Limb.LeftLeg] = Bones(rig.Anim, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "LeftUpLeg", "LeftLeg", "LeftFoot");
                rig.Chains[(int)Limb.RightLeg] = Bones(rig.Anim, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "RightUpLeg", "RightLeg", "RightFoot");
                rig.Ok = rig.Chains[0] != null && rig.Chains[1] != null && rig.Chains[2] != null && rig.Chains[3] != null;
                rig.VisualPos = rig.Visual.localPosition;
                rig.VisualRot = rig.Visual.localRotation;
            }
            Rigs[p] = rig;
            return rig;
        }

        /// <summary>For the bones command: the bones found for a player, by limb.</summary>
        public static string Describe(Player p)
        {
            Rig rig = RigOf(p);
            if (rig == null || rig.Anim == null) return "no animator";
            var parts = new List<string> { "humanoid " + rig.Anim.isHuman };
            for (int i = 0; i < 4; i++)
                parts.Add(((Limb)i) + ": " + (rig.Chains[i] == null ? "not found" : string.Join(" > ", System.Array.ConvertAll(rig.Chains[i], t => t.name))));
            return string.Join("; ", parts);
        }

        private static Transform[] Bones(Animator anim, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c, string na, string nb, string nc)
        {
            if (anim.isHuman)
            {
                Transform ta = anim.GetBoneTransform(a), tb = anim.GetBoneTransform(b), tc = anim.GetBoneTransform(c);
                if (ta != null && tb != null && tc != null) return new[] { ta, tb, tc };
            }
            Transform fa = Find(anim.transform, na), fb = Find(anim.transform, nb), fc = Find(anim.transform, nc);
            return fa != null && fb != null && fc != null ? new[] { fa, fb, fc } : null;
        }

        private static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name || t.name.EndsWith(":" + name)) return t;
            return null;
        }

        private static void Restore(Rig rig)
        {
            if (rig.Visual == null) return;
            rig.Visual.localPosition = rig.VisualPos;
            rig.Visual.localRotation = rig.VisualRot;
            rig.Flipping = false;
        }

        // ---- the pose ----

        private static void Pose(Player p, Rig rig, Move move)
        {
            // (the flip of the last frame off first: the reaches are worked out on the upright body)
            if (rig.Flipping) Restore(rig);
            float dt = Time.deltaTime;
            Transform body = p.transform;
            Vector3 up = body.up, fwd = body.forward, right = body.right;
            Vector3 v;
            if (p == Player.m_localPlayer) v = Web.Body(p).linearVelocity;
            else
            {
                if (dt > 0f && rig.LastPos != Vector3.zero) rig.Vel = Vector3.Lerp(rig.Vel, (body.position - rig.LastPos) / dt, 1f - Mathf.Exp(-10f * dt));
                rig.LastPos = body.position;
                v = rig.Vel;
            }
            bool hasWeb = Look.WebOf(p, out Vector3 anchor, out bool rightHand);
            float flipT = (Time.time - rig.FlipAt) / FlipSeconds;
            bool flipping = flipT >= 0f && flipT < 1f && move != Move.Crawl;

            // how tucked the legs are: rising toward a swing's top, or in a flip
            float tuck = 0f;
            if (move == Move.Swing) tuck = Mathf.Clamp01(v.y / 7f);
            else if (move == Move.Fly) tuck = 0.3f;
            if (flipping) tuck = 1f;
            rig.Tuck = Mathf.MoveTowards(rig.Tuck, tuck, dt * 4f);
            if (move == Move.Crawl) rig.Phase += dt * (1.5f + v.magnitude * 3.5f);

            for (int i = 0; i < 4; i++)
            {
                Transform[] c = rig.Chains[i];
                bool arm = i < 2, isRight = i == (int)Limb.RightArm || i == (int)Limb.RightLeg;
                Vector3 side = isRight ? right : -right;
                float len = Vector3.Distance(c[0].position, c[1].position) + Vector3.Distance(c[1].position, c[2].position);
                Vector3 root = c[0].position;
                bool want = move != Move.None || flipping;
                Vector3 target = root, pole = root + fwd;

                if (want && arm)
                {
                    bool webArm = hasWeb && isRight == rightHand && (move == Move.Swing || move == Move.Zip);
                    Vector3 toWeb = hasWeb ? (anchor - root).normalized : up;
                    if (webArm) { target = root + toWeb * len * 1.2f; pole = root - up * 0.3f - fwd * 0.3f; }
                    else if (move == Move.Zip && hasWeb) { target = root + toWeb * len * 0.75f; pole = root - up * 0.5f + side * 0.3f; }
                    else if (move == Move.Crawl)
                    {
                        float step = Mathf.Sin(rig.Phase + (isRight ? Mathf.PI : 0f));
                        target = root + fwd * len * 0.7f + up * len * (0.35f + 0.2f * step) + side * len * 0.12f;
                        pole = root + side * 0.6f - up * 0.6f;
                    }
                    else if (flipping)
                    {
                        // hugging the knees
                        target = root + fwd * len * 0.55f - up * len * 0.55f;
                        pole = root + side * 0.5f;
                    }
                    else
                    {
                        // out and back for balance (swinging), out to the sides (flying)
                        target = root + (side * 0.6f - up * 0.6f - fwd * (move == Move.Swing ? 0.4f : 0.1f)).normalized * len * 0.95f;
                        pole = root - fwd * 0.6f;
                    }
                }
                else if (want)
                {
                    if (move == Move.Crawl)
                    {
                        float step = Mathf.Sin(rig.Phase + (isRight ? 0f : Mathf.PI));
                        target = root + fwd * len * 0.42f - up * len * (0.55f + 0.12f * step) + side * len * 0.28f;
                        pole = root + fwd * 0.8f + side * 0.9f;   // (knees out, like a spider's)
                    }
                    else
                    {
                        float scissor = move == Move.Swing ? (isRight ? 0.08f : -0.08f) : 0f;
                        Vector3 trail = (-up - fwd * (move == Move.Zip ? 0.3f : 0.1f) + fwd * scissor).normalized * len * 0.97f;
                        Vector3 tucked = (-up * 0.35f + fwd * 0.5f) * len;
                        target = root + Vector3.Lerp(trail, tucked, rig.Tuck) + side * len * 0.05f;
                        pole = root + fwd * 1f;
                    }
                }

                // eased: the target moves in the body's space, the weight blends from the animation and back to it
                float k = 1f - Mathf.Exp(-14f * dt);
                Vector3 local = body.InverseTransformPoint(target);
                if (!rig.Has[i]) { rig.Target[i] = body.InverseTransformPoint(c[2].position); rig.Has[i] = true; }
                if (want) rig.Target[i] = Vector3.Lerp(rig.Target[i], local, k);
                rig.Weight[i] = Mathf.MoveTowards(rig.Weight[i], want ? 1f : 0f, dt * 5f);
                if (rig.Weight[i] <= 0f) { rig.Has[i] = false; continue; }
                Reach(c[0], c[1], c[2], body.TransformPoint(rig.Target[i]), pole, rig.Weight[i]);
            }

            if (flipping)
            {
                // the whole visual turned round the body's middle (eased: fast through the middle of the turn)
                float t = flipT * flipT * (3f - 2f * flipT);
                Vector3 axis = rig.FlipKind == 0 ? right : fwd;
                Vector3 middle = body.position + up * 0.95f;
                Quaternion turn = Quaternion.AngleAxis(360f * t, axis);
                rig.Visual.position = middle + turn * (rig.Visual.position - middle);
                rig.Visual.rotation = turn * rig.Visual.rotation;
                rig.Flipping = true;
            }
        }

        /// <summary>
        /// Two bones reaching for a target (the shoulder or hip, the elbow or knee, the hand or foot), the middle joint bent toward the pole,
        /// blended over the animation by the weight. (The analytic two-bone reach: the bend first for the distance, then the aim, then the
        /// turn round the aim toward the pole.)
        /// </summary>
        private static void Reach(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float weight)
        {
            Quaternion a0 = a.rotation, b0 = b.rotation;
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude;
            if (lab < 0.001f || lcb < 0.001f) return;
            float lat = Mathf.Clamp((target - pa).magnitude, 0.01f, lab + lcb - 0.01f);

            float acab0 = Angle(pc - pa, pb - pa), babc0 = Angle(pa - pb, pc - pb);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float babc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
            Vector3 axis = Vector3.Cross(pc - pa, pb - pa);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(pc - pa, pole - pa);
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            a.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * a.rotation;
            b.rotation = Quaternion.AngleAxis((babc1 - babc0) * Mathf.Rad2Deg, axis) * b.rotation;

            a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;

            Vector3 aim = (target - pa).normalized;
            Vector3 mid = Vector3.ProjectOnPlane(b.position - pa, aim), toPole = Vector3.ProjectOnPlane(pole - pa, aim);
            if (mid.sqrMagnitude > 1e-6f && toPole.sqrMagnitude > 1e-6f)
                a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(mid, toPole, aim), aim) * a.rotation;

            if (weight < 0.999f)
            {
                a.rotation = Quaternion.Slerp(a0, a.rotation, weight);
                b.rotation = Quaternion.Slerp(b0, b.rotation, weight);
            }
        }

        private static float Angle(Vector3 x, Vector3 y) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(x.normalized, y.normalized), -1f, 1f));
    }
}
