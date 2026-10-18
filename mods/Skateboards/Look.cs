using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Skateboards
{
    /// <summary>
    /// How a skater looks, animated over the game's own (standing) animation each frame for every player in sight on a board: ours from
    /// Skate.cs, the others' from what they share (Share.cs), so players with the mod see each other skate. Players without it see a
    /// player gliding along standing up, with the board in the hand. (The game can't take new animation clips from a mod without an
    /// asset bundle, so every move here is worked out in code, bone by bone.)
    ///
    ///   The stance  side-on to the board (regular: left foot forward; goofy: right), knees bent, arms out along the board, shoulders
    ///               a little open and the head turned to look where the board is going (over the front shoulder, or the back one fakie);
    ///               the whole body turned and tilted with the surface under the board (up a quarter pipe to vertical), and leaning into
    ///               a carve.
    ///   Pushing     the body turns to face the nose, the front foot points ahead on the front bolts, the back foot plants beside the
    ///               board, kicks back along the ground and swings forward again, the arms swinging against it.
    ///   The ollie   winding up crouches deeper; the pop snaps the tail down, the front foot slides up the board and the knees tuck up;
    ///               in the air the legs tuck most at the top of the jump. A landing squashes the knees.
    ///   Tricks      flips spin the board under the feet as they hop off it (a double spins it twice, the Ragnarök Flip three times
    ///               round and once about); grabs pull the board up to a hand (the Valkyrie Air holds both arms out like wings); grinds sit
    ///               the trucks (or the deck, for a boardslide) on the rail; manuals tip it onto two wheels, the hips over them; a revert
    ///               pivots the board round.
    ///   A bail      the body tips over and lies a moment before getting up, while the board flies off and clatters to a stop.
    ///   The special a gold glow under the board while the meter is full (the Bifröst Grind's is every colour of the rainbow bridge).
    /// Each limb is a two-bone reach (upper arm, forearm, hand; thigh, shin, foot), as in Webslinger's Body.cs.
    /// The board's rolling and grinding sound plays from it (Sounds.cs), and for the other players the pop, landing and bail sounds when
    /// their pose changes.
    /// </summary>
    internal static class Look
    {
        private enum Limb { LeftArm, RightArm, LeftLeg, RightLeg }

        private sealed class Rig
        {
            public Animator Anim;
            public Transform Visual, Hips, Spine, Chest, Neck, Head, LeftToes, RightToes;
            public Vector3 VisualPos;
            public Quaternion VisualRot = Quaternion.identity;
            public readonly Transform[][] Chains = new Transform[4][];
            public bool Ok, Posed;
            public GameObject Board;
            public Light Glow;
            public AudioSource Rolling, Grinding;
            public Vector3 N = Vector3.up;
            public float Yaw, Squash, Grab, Push, PushPhase, Lean;
            public bool HasYaw;
            public Ride Last = Ride.Off;
            public int FlipSeen = -1;
            public float FlipAt = -10f, PopAt = -10f, BailAt = -10f;
            public Tricks.Flip FlipKind;
            public bool FlipDouble;
            public Vector3 BailDir, BailChest;
            public Vector3 LastPos, Vel;
            // the bones the pose changes, as the animation left them and as the pose left them (Fresh, Keep)
            public Transform[] Touched;
            public Quaternion[] Orig, Set;
            public Vector3 HipsOrig, HipsSet;
            public bool Saved;
            // a bail: the board on its own
            public GameObject Loose;
            public Vector3 LooseV, LooseSpin;
            public float LooseUntil;
            public bool LooseRest;
        }

        private static readonly Dictionary<Player, Rig> Rigs = new Dictionary<Player, Rig>();
        private static readonly List<Player> Gone = new List<Player>();
        private static readonly HashSet<string> Hinted = new HashSet<string>();

        private static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisOf = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> RightInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");

        private const float BailFall = 0.35f, BailLie = 1.1f, BailUp = 1.6f;   // (seconds into a bail: down, lying, up again; Skate.cs waits as long)

        public static void Reset()
        {
            foreach (KeyValuePair<Player, Rig> kv in Rigs) Unpose(kv.Key, kv.Value, true);
            Rigs.Clear();
            Hinted.Clear();
        }

        public static void Hint(Player p, string key)
        {
            if (!Plugin.Hints.Value || p == null || !Hinted.Add(key)) return;
            p.Message(MessageHud.MessageType.TopLeft, T(key));
        }

        // ---- directions on a surface ----

        /// <summary>A direction on a surface with this normal, taken back to the level (the yaw it has there).</summary>
        public static Vector3 Level(Vector3 n, Vector3 v)
        {
            Vector3 flat = Quaternion.Inverse(Quaternion.FromToRotation(Vector3.up, n)) * v;
            flat.y = 0f;
            return flat.sqrMagnitude < 1e-6f ? Vector3.forward : flat.normalized;
        }

        /// <summary>A yaw on the level, laid onto a surface with this normal (a nose pointing at the ramp on the level points up it).</summary>
        public static Vector3 NoseOnSurface(Vector3 n, float yaw)
        {
            Vector3 v = Quaternion.FromToRotation(Vector3.up, n) * (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            v = Vector3.ProjectOnPlane(v, n);
            return v.sqrMagnitude < 1e-6f ? Vector3.ProjectOnPlane(Vector3.forward, n).normalized : v.normalized;
        }

        // ---- every frame ----

        public static void Late()
        {
            Player local = Player.m_localPlayer;
            Camera cam = Utils.GetMainCamera();
            Gone.Clear();
            foreach (KeyValuePair<Player, Rig> kv in Rigs) if (kv.Key == null) Gone.Add(kv.Key);
            foreach (Player g in Gone) { if (Rigs.TryGetValue(g, out Rig dead)) Unpose(null, dead, true); Rigs.Remove(g); }

            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                bool far = cam != null && Vector3.Distance(cam.transform.position, p.transform.position) > 90f;
                Pose pose = ReferenceEquals(p, local) ? Share.Local() : Share.Read(p);
                if (p.IsDead()) pose.State = Ride.Off;
                Rigs.TryGetValue(p, out Rig rig);
                if (rig == null && (!pose.On || far)) continue;
                if (rig == null) { rig = RigOf(p); if (rig == null) continue; }
                Loose(rig);
                if (!pose.On || far || !rig.Ok)
                {
                    if (rig.Posed) Unpose(p, rig, false);
                    rig.Last = Ride.Off;
                    if (rig.Loose == null) Rigs.Remove(p);
                    continue;
                }
                try { Draw(p, rig, pose, ReferenceEquals(p, local)); }
                catch (System.Exception e) { Plugin.Log.LogWarning("Skateboards look: " + e.Message); Unpose(p, rig, false); }
            }
        }

        private static Rig RigOf(Player p)
        {
            var rig = new Rig { Anim = AnimatorOf(p), Visual = p.GetVisual() != null ? p.GetVisual().transform : null };
            if (rig.Anim == null || rig.Visual == null || !rig.Anim.isHuman) return null;
            rig.Chains[(int)Limb.LeftArm] = Bones(rig.Anim, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            rig.Chains[(int)Limb.RightArm] = Bones(rig.Anim, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
            rig.Chains[(int)Limb.LeftLeg] = Bones(rig.Anim, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            rig.Chains[(int)Limb.RightLeg] = Bones(rig.Anim, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
            rig.Hips = rig.Anim.GetBoneTransform(HumanBodyBones.Hips);
            rig.Spine = rig.Anim.GetBoneTransform(HumanBodyBones.Spine);
            rig.Chest = rig.Anim.GetBoneTransform(HumanBodyBones.Chest);
            rig.Neck = rig.Anim.GetBoneTransform(HumanBodyBones.Neck);
            rig.Head = rig.Anim.GetBoneTransform(HumanBodyBones.Head);
            rig.LeftToes = rig.Anim.GetBoneTransform(HumanBodyBones.LeftToes);
            rig.RightToes = rig.Anim.GetBoneTransform(HumanBodyBones.RightToes);
            rig.Ok = rig.Chains[0] != null && rig.Chains[1] != null && rig.Chains[2] != null && rig.Chains[3] != null && rig.Hips != null;
            rig.VisualPos = rig.Visual.localPosition;
            rig.VisualRot = rig.Visual.localRotation;
            rig.LastPos = p.transform.position;
            Rigs[p] = rig;
            return rig;
        }

        private static Transform[] Bones(Animator anim, HumanBodyBones a, HumanBodyBones b, HumanBodyBones c)
        {
            Transform ta = anim.GetBoneTransform(a), tb = anim.GetBoneTransform(b), tc = anim.GetBoneTransform(c);
            return ta != null && tb != null && tc != null ? new[] { ta, tb, tc } : null;
        }

        private static void Unpose(Player p, Rig rig, bool all)
        {
            if (rig.Visual != null)
            {
                rig.Visual.localPosition = rig.VisualPos;
                rig.Visual.localRotation = rig.VisualRot;
            }
            DropBoard(rig);
            if (all && rig.Loose != null) { Object.Destroy(rig.Loose); rig.Loose = null; }
            if (p != null) HandBoard(p, true);
            rig.Posed = false;
            rig.HasYaw = false;
            rig.Saved = false;
        }

        /// <summary>
        /// Before posing: the bones the animation didn't move since the last frame (it doesn't run every frame) go back to where the
        /// animation had them, so the crouch and the twists, which are added to what is there, never stack up frame after frame (the
        /// body shook, standing still). Then what the animation has now is kept, to go back to next time.
        /// </summary>
        private static void Fresh(Rig rig)
        {
            if (rig.Touched == null)
            {
                var list = new List<Transform> { rig.Spine, rig.Chest, rig.Neck, rig.Head };
                foreach (Transform[] chain in rig.Chains) if (chain != null) list.AddRange(chain);
                list.RemoveAll(t => t == null);
                rig.Touched = list.ToArray();
                rig.Orig = new Quaternion[rig.Touched.Length];
                rig.Set = new Quaternion[rig.Touched.Length];
            }
            if (rig.Saved)
            {
                if ((rig.Hips.localPosition - rig.HipsSet).sqrMagnitude < 1e-12f) rig.Hips.localPosition = rig.HipsOrig;
                for (int i = 0; i < rig.Touched.Length; i++)
                    if (rig.Touched[i].localRotation == rig.Set[i]) rig.Touched[i].localRotation = rig.Orig[i];
            }
            rig.HipsOrig = rig.Hips.localPosition;
            for (int i = 0; i < rig.Touched.Length; i++) rig.Orig[i] = rig.Touched[i].localRotation;
        }

        /// <summary>After posing: what the pose left, to know next frame whether the animation moved them since.</summary>
        private static void Keep(Rig rig)
        {
            rig.HipsSet = rig.Hips.localPosition;
            for (int i = 0; i < rig.Touched.Length; i++) rig.Set[i] = rig.Touched[i].localRotation;
            rig.Saved = true;
        }

        private static void DropBoard(Rig rig)
        {
            if (rig.Board != null) Object.Destroy(rig.Board);
            rig.Board = null;
            rig.Glow = null;
            rig.Rolling = rig.Grinding = null;
        }

        /// <summary>The board in the hand shown or hidden (it is under the feet while riding).</summary>
        private static void HandBoard(Player p, bool show)
        {
            VisEquipment vis = VisOf(p);
            GameObject held = vis != null ? RightInstance(vis) : null;
            if (held != null && held.activeSelf != show && (show || Board.InHandOf(p))) held.SetActive(show);
        }

        // ---- a skater ----

        private static void Draw(Player p, Rig rig, Pose pose, bool local)
        {
            float dt = Time.deltaTime;
            Transform body = p.transform;
            if (dt > 0f) rig.Vel = Vector3.Lerp(rig.Vel, (body.position - rig.LastPos) / dt, 1f - Mathf.Exp(-10f * dt));
            rig.LastPos = body.position;
            HandBoard(p, false);
            Fresh(rig);

            // what changed: sounds, the pop, the squash of a landing, a bail
            if (pose.State != rig.Last)
            {
                if (!local) Changed(p, rig.Last, pose.State);
                if (pose.State == Ride.Air && (rig.Last == Ride.Roll || rig.Last == Ride.Manual || rig.Last == Ride.Grind)) rig.PopAt = Time.time;
                if (rig.Last == Ride.Air && (pose.State == Ride.Roll || pose.State == Ride.Grind)) rig.Squash = 1f;
                if (pose.State == Ride.Bail)
                {
                    Vector3 v = local ? Skate.BailVelocity : rig.Vel;
                    Bailed(p, rig, v);
                    rig.BailAt = Time.time;
                    Vector3 chestNow = rig.Posed ? Vector3.ProjectOnPlane(rig.Visual.forward, Vector3.up) : body.forward;
                    rig.BailChest = chestNow.sqrMagnitude > 0.01f ? chestNow.normalized : body.forward;
                    Vector3 flat = new Vector3(v.x, 0f, v.z);
                    // falling the way it was going if fast, else onto the chest side
                    rig.BailDir = flat.magnitude > 3f ? flat.normalized : rig.BailChest;
                }
                if (rig.Last == Ride.Bail && rig.Loose != null) { Object.Destroy(rig.Loose); rig.Loose = null; }
                rig.Last = pose.State;
            }
            if (pose.FlipCount != rig.FlipSeen)
            {
                if (rig.FlipSeen >= 0 && pose.Flip != Tricks.Flip.None)
                {
                    if (!(pose.Double && rig.FlipKind == pose.Flip && rig.FlipAt > 0f && !local)) rig.FlipAt = local ? Tricks.FlipStart : Time.time;
                    rig.FlipKind = pose.Flip;
                    rig.FlipDouble = pose.Double;
                    if (!local) Sounds.Play(Sfx.Flick, body.position, 0.6f);
                }
                rig.FlipSeen = pose.FlipCount;
            }
            rig.FlipDouble = pose.Double || (rig.FlipDouble && pose.Flip != Tricks.Flip.None);

            if (pose.State == Ride.Bail) { Fall(p, rig); Keep(rig); return; }
            if (rig.Board == null)
            {
                rig.Board = Board.Model(p.gameObject.layer);
                rig.Rolling = Sounds.Loop(rig.Board, Sfx.Rolling);
                rig.Grinding = Sounds.Loop(rig.Board, Sfx.Grinding);
                var glow = new GameObject("skateboards_glow");
                glow.transform.SetParent(rig.Board.transform, false);
                glow.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                rig.Glow = glow.AddComponent<Light>();
                rig.Glow.type = LightType.Point;
                rig.Glow.range = 2.2f;
                rig.Glow.shadows = LightShadows.None;
                rig.Glow.enabled = false;
            }
            rig.Posed = true;

            // the surface and the nose (a revert or a change of stance pivots round, not snapping)
            float k = 1f - Mathf.Exp(-14f * dt);
            rig.N = Vector3.Slerp(rig.N, pose.Normal, pose.State == Ride.Air ? 1f - Mathf.Exp(-6f * dt) : k).normalized;
            if (!rig.HasYaw || (local && pose.State == Ride.Air)) rig.Yaw = pose.NoseYaw;
            else rig.Yaw = Mathf.MoveTowardsAngle(rig.Yaw, pose.NoseYaw, Mathf.Max(900f * dt, Mathf.Abs(Mathf.DeltaAngle(rig.Yaw, pose.NoseYaw)) * 14f * dt));
            rig.HasYaw = true;
            Vector3 n = rig.N;
            Vector3 nose = NoseOnSurface(n, rig.Yaw);
            float stance = pose.Goofy ? -1f : 1f;
            Vector3 chest = Vector3.Cross(n, nose).normalized * stance;   // (regular: the chest faces to the right of the nose)

            float r = Skate.Radius(p);
            Vector3 pivot = p.GetCollider() is CapsuleCollider cap ? Skate.Bottom(p, cap, r) : body.position + Vector3.up * r;
            Vector3 contact = pivot - n * r;

            // timers
            float flipT = rig.FlipKind != Tricks.Flip.None ? (Time.time - rig.FlipAt) / Mathf.Max(0.1f, local ? Tricks.FlipLength : Plugin.FlipSeconds.Value * (rig.FlipDouble ? 1.45f : 1f)) : 2f;
            bool flipping = pose.State == Ride.Air && flipT >= 0f && flipT < 1f;
            if (!flipping && flipT >= 1f) rig.FlipKind = Tricks.Flip.None;
            rig.Grab = Mathf.MoveTowards(rig.Grab, pose.Grab != Tricks.Grab.None ? 1f : 0f, dt * 6f);
            rig.Squash = Mathf.MoveTowards(rig.Squash, 0f, dt * 4f);
            rig.Push = Mathf.MoveTowards(rig.Push, pose.Pushing ? 1f : 0f, dt * 5f);
            if (rig.Push > 0f) rig.PushPhase = (rig.PushPhase + dt / 0.85f) % 1f; else rig.PushPhase = 0f;
            rig.Lean = Mathf.MoveTowards(rig.Lean, pose.Lean, dt * 3f);
            float popT = (Time.time - rig.PopAt) / 0.42f;
            bool popping = pose.State == Ride.Air && popT >= 0f && popT < 1f;
            float vy = local ? Skate.BodyOf(p).linearVelocity.y : rig.Vel.y;
            float tuck = pose.State == Ride.Air ? Mathf.Clamp01(1f - Mathf.Abs(vy) / 7f) : 0f;   // (most at the top of the jump)

            // the board: where, turned how, lifted how far, for the trick going on
            Quaternion frame = Quaternion.LookRotation(nose, n);
            Quaternion trick = Quaternion.identity;
            Vector3 lift = Vector3.zero;
            float drop = 0f;   // how far the deck sits below the usual (on a rail)
            float feetOver = 0f;   // how far the feet ride above the board (hopping off it in a flip)
            if (flipping)
            {
                float t = flipT * flipT * (3f - 2f * flipT);
                float turns = rig.FlipDouble ? 2f : 1f;
                switch (rig.FlipKind)
                {
                    case Tricks.Flip.Kickflip: trick = Quaternion.Euler(0f, 0f, -360f * turns * t); break;
                    case Tricks.Flip.Heelflip: trick = Quaternion.Euler(0f, 0f, 360f * turns * t); break;
                    case Tricks.Flip.Impossible: trick = Quaternion.Euler(360f * turns * t, 0f, 0f); break;
                    case Tricks.Flip.PopShoveIt: trick = Quaternion.Euler(0f, 180f * turns * t, 0f); break;
                    case Tricks.Flip.Hardflip: trick = Quaternion.Euler(-360f * turns * t, 0f, 0f) * Quaternion.Euler(0f, 0f, -360f * turns * t); break;
                    case Tricks.Flip.Ragnarok: trick = Quaternion.Euler(0f, 360f * t, 0f) * Quaternion.Euler(0f, 0f, -1080f * t); break;
                }
                lift = n * (0.3f * Mathf.Sin(Mathf.PI * flipT));
                feetOver = 0.12f * Mathf.Sin(Mathf.PI * flipT);
            }
            else if (rig.Grab > 0f)
            {
                lift = n * (0.38f * rig.Grab);
                if (pose.Grab == Tricks.Grab.Method) { trick = Quaternion.Euler(0f, 0f, -35f * rig.Grab * stance); lift -= chest * 0.18f * rig.Grab; }
                else if (pose.Grab == Tricks.Grab.Nosegrab) trick = Quaternion.Euler(-25f * rig.Grab, 0f, 0f);
                else if (pose.Grab == Tricks.Grab.Tailgrab) trick = Quaternion.Euler(25f * rig.Grab, 0f, 0f);
                else if (pose.Grab == Tricks.Grab.Valkyrie) { trick = Quaternion.Euler(0f, 0f, 20f * rig.Grab * stance); lift -= chest * 0.25f * rig.Grab; }
            }
            else if (pose.State == Ride.Air) lift = n * (0.06f + 0.16f * tuck);
            if (popping)
            {
                // the pop: the tail snaps down to the ground and the board rises with the feet as they tuck
                float snap = Mathf.Sin(Mathf.PI * Mathf.Clamp01(popT / 0.55f));
                trick = trick * Pivot(-1f, 24f * snap, ref lift, n, nose);
                lift += n * (0.18f * Mathf.Sin(Mathf.PI * popT));
            }
            if (pose.State == Ride.Manual) trick = Pivot(pose.NoseManual ? 1f : -1f, 13f, ref lift, n, nose);
            if (pose.State == Ride.Grind)
            {
                switch (pose.Grind)
                {
                    case Tricks.Grind.Boardslide:
                    case Tricks.Grind.Lipslide: drop = 0.085f; break;
                    case Tricks.Grind.Nosegrind: drop = 0.03f; trick = Pivot(1f, 9f, ref lift, n, nose); break;
                    case Tricks.Grind.FiveO: drop = 0.03f; trick = Pivot(-1f, 9f, ref lift, n, nose); break;
                    case Tricks.Grind.Crooked: drop = 0.03f; trick = Pivot(1f, 8f, ref lift, n, nose) * Quaternion.Euler(0f, 14f, 0f); break;
                    default: drop = 0.04f; break;
                }
            }
            // a carve tips the board onto its edge a little, and the body more
            Quaternion carve = Quaternion.AngleAxis(rig.Lean * 13f, nose);
            Vector3 boardPos = contact + lift - n * drop;
            rig.Board.transform.SetPositionAndRotation(boardPos, Quaternion.AngleAxis(rig.Lean * 5f, nose) * frame * trick);
            Glow(rig, pose);

            // the body: side-on (turning to face the nose to push), tilted with the surface and leaning into the carve, soles on the deck
            float faceNose = -62f * rig.Push * stance;
            Vector3 bodyFwd = Quaternion.AngleAxis(faceNose, n) * chest;
            float crouch = 0.07f + 0.17f * pose.Crouch + 0.13f * rig.Squash + 0.12f * rig.Push + (pose.State == Ride.Manual ? 0.04f : 0f)
                + (pose.State == Ride.Grind ? 0.06f : 0f) + (popping ? 0.06f * Mathf.Sin(Mathf.PI * popT) : 0f);
            Vector3 deck = contact + n * (Board.DeckTop - drop);
            rig.Visual.rotation = carve * Quaternion.LookRotation(bodyFwd, n);
            Vector3 hipsShift = pose.State == Ride.Manual ? nose * (pose.NoseManual ? 0.07f : -0.07f) : Vector3.zero;
            rig.Visual.position = deck + hipsShift + (flipping ? lift * 0.7f + n * feetOver : Vector3.zero) + (rig.Grab > 0f ? lift * 0.3f : Vector3.zero)
                + (popping ? n * (0.1f * Mathf.Sin(Mathf.PI * popT)) : Vector3.zero);
            rig.Hips.position -= n * crouch;

            // the shoulders open a little toward the nose, and more while pushing
            Twist(rig.Spine, n, -8f * stance - 10f * rig.Push * stance);
            Twist(rig.Chest, n, -6f * stance);

            // the feet: on the deck (on the board as it is turned for a manual or grind; above the flipping board, hopping off it), the
            // back one pushing along the ground beside the board while pushing
            Transform deckFrame = rig.Board.transform;
            bool leftFront = !pose.Goofy;
            for (int i = 2; i < 4; i++)
            {
                Transform[] c = rig.Chains[i];
                bool isLeft = i == (int)Limb.LeftLeg;
                bool front = isLeft == leftFront;
                float along = (front ? 1f : -1f) * 0.19f;
                if (front && popping) along += 0.09f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(popT * 1.5f));   // (sliding up the board)
                if (front && rig.Push > 0f) along = Mathf.Lerp(along, 0.2f, rig.Push);
                Vector3 foot;
                if (flipping) foot = contact + lift * 0.7f + n * (Board.DeckTop + 0.085f + feetOver) + nose * along;
                else foot = deckFrame.TransformPoint(new Vector3(0f, Board.DeckTop + 0.075f, along));
                Vector3 toes = Vector3.ProjectOnPlane(chest, n);
                if (!front && rig.Push > 0f)
                {
                    // the push: planted beside the board on the toe side, kicked back along the ground, swung forward again
                    float ph = rig.PushPhase;
                    float on = ph < 0.55f ? ph / 0.55f : 0f, swing = ph >= 0.55f ? (ph - 0.55f) / 0.45f : 0f;
                    float back = ph < 0.55f ? Mathf.Lerp(0.15f, -0.6f, on) : Mathf.Lerp(-0.6f, 0.15f, swing);
                    float up = ph < 0.55f ? 0f : Mathf.Sin(Mathf.PI * swing) * 0.16f;
                    Vector3 ground = contact + chest * 0.27f + nose * back + n * (0.08f + up);
                    foot = Vector3.Lerp(foot, ground, rig.Push);
                    toes = Vector3.Lerp(toes, nose, rig.Push);
                }
                else if (front && rig.Push > 0f) toes = Vector3.Lerp(toes, nose, rig.Push * 0.8f);
                Vector3 pole = c[0].position + bodyFwd * 1f + nose * Mathf.Sign(along) * 0.25f;
                Reach(c[0], c[1], c[2], foot, pole, 1f);
                Transform toe = isLeft ? rig.LeftToes : rig.RightToes;
                if (toe != null)
                {
                    Vector3 toeNow = toe.position - c[2].position;
                    if (toeNow.sqrMagnitude > 1e-6f && toes.sqrMagnitude > 1e-6f) c[2].rotation = Quaternion.FromToRotation(toeNow, toes.normalized * toeNow.magnitude) * c[2].rotation;
                }
            }

            // the arms: out along the board for balance, higher in the air and on a rail, swinging against a push; a grab's hand on the
            // board; the Valkyrie Air's both out like wings
            float swingArm = rig.Push > 0f ? Mathf.Sin(rig.PushPhase * Mathf.PI * 2f) * rig.Push : 0f;
            for (int i = 0; i < 2; i++)
            {
                Transform[] c = rig.Chains[i];
                bool isLeft = i == (int)Limb.LeftArm;
                bool front = isLeft == leftFront;
                Vector3 side = front ? nose : -nose;
                float len = Vector3.Distance(c[0].position, c[1].position) + Vector3.Distance(c[1].position, c[2].position);
                float raise = pose.State == Ride.Air || pose.State == Ride.Grind || pose.State == Ride.Manual ? 0.55f : 0.2f;
                Vector3 target = c[0].position + (side * (0.55f + raise * 0.4f) - n * (0.8f - raise) + bodyFwd * (0.15f + 0.35f * swingArm * (front ? 1f : -1f))).normalized * len * 0.95f;
                if (pose.Crouch > 0f) target = Vector3.Lerp(target, c[0].position + (bodyFwd * 0.6f - n * 0.5f + side * 0.3f).normalized * len * 0.95f, pose.Crouch);
                Vector3 pole = c[0].position - n * 0.4f - bodyFwd * 0.3f;
                if (pose.Grab == Tricks.Grab.Valkyrie && rig.Grab > 0.01f)
                {
                    target = Vector3.Lerp(target, c[0].position + (side * 0.8f + n * 0.55f - bodyFwd * 0.1f).normalized * len * 0.97f, rig.Grab);
                    pole = c[0].position - n * 0.3f - bodyFwd * 0.4f;
                }
                else if (rig.Grab > 0.01f && GrabHand(pose.Grab, front))
                {
                    Vector3 hold = deckFrame.TransformPoint(GrabSpot(pose.Grab, stance));
                    target = Vector3.Lerp(target, hold, rig.Grab);
                    pole = c[0].position + side * 0.5f + n * 0.2f;
                }
                Reach(c[0], c[1], c[2], target, pole, 1f);
            }

            // the head: looking where the board is going (over the front shoulder, or the back one when fakie)
            Vector3 going = new Vector3(rig.Vel.x, 0f, rig.Vel.z);
            Vector3 look = going.magnitude > 1.2f ? going.normalized : Vector3.ProjectOnPlane(nose, Vector3.up).normalized;
            LookAt(rig, n, bodyFwd, look);

            // the wheels: rolling louder and higher the faster, grinding on a rail
            float speed = local ? Skate.Speed : going.magnitude;
            bool rolling = pose.State == Ride.Roll || pose.State == Ride.Manual;
            Sounds.Set(rig.Rolling, rolling ? Mathf.Clamp01(speed / 10f) * 0.65f : 0f, 0.7f + Mathf.Clamp01(speed / 15f) * 0.6f);
            Sounds.Set(rig.Grinding, pose.State == Ride.Grind ? 0.75f : 0f, 0.85f + Mathf.Clamp01(speed / 12f) * 0.4f);
            Keep(rig);
        }

        /// <summary>A bone turned round the body's up by some degrees (after the animation), on top of what it has.</summary>
        private static void Twist(Transform bone, Vector3 up, float degrees)
        {
            if (bone != null && Mathf.Abs(degrees) > 0.01f) bone.rotation = Quaternion.AngleAxis(degrees, up) * bone.rotation;
        }

        /// <summary>The neck and head turned toward a direction, as far as a head turns (80 degrees each way from the chest).</summary>
        private static void LookAt(Rig rig, Vector3 n, Vector3 chest, Vector3 look)
        {
            Vector3 c = Vector3.ProjectOnPlane(chest, n), l = Vector3.ProjectOnPlane(look, n);
            if (c.sqrMagnitude < 1e-4f || l.sqrMagnitude < 1e-4f) return;
            float a = Mathf.Clamp(Vector3.SignedAngle(c, l, n), -80f, 80f);
            Twist(rig.Neck, n, a * 0.4f);
            Twist(rig.Head, n, a * 0.5f);
        }

        /// <summary>The special's glow under the board: gold while the meter is full, the rainbow on a Bifröst Grind.</summary>
        private static void Glow(Rig rig, Pose pose)
        {
            if (rig.Glow == null) return;
            bool bifrost = pose.State == Ride.Grind && pose.Grind == Tricks.Grind.Bifrost;
            bool on = pose.Special || bifrost;
            if (rig.Glow.enabled != on) rig.Glow.enabled = on;
            if (!on) return;
            rig.Glow.color = bifrost ? Color.HSVToRGB((Time.time * 0.8f) % 1f, 0.8f, 1f) : new Color(1f, 0.78f, 0.3f);
            rig.Glow.intensity = bifrost ? 3f : 1.6f + 0.4f * Mathf.Sin(Time.time * 6f);
        }

        /// <summary>
        /// A bail: no board under the feet; the body tips over the way it was going (round the feet), lies a moment and gets up again,
        /// the hands reaching for the ground. Over the game's standing pose (Skate.cs keeps it standing).
        /// </summary>
        private static void Fall(Player p, Rig rig)
        {
            DropBoard(rig);
            rig.Posed = true;
            HandBoard(p, false);
            float t = Time.time - rig.BailAt;
            float angle = t < BailFall ? 82f * (t / BailFall) * (t / BailFall)
                : t < BailLie ? 82f
                : t < BailUp ? 82f * (1f - Mathf.SmoothStep(0f, 1f, (t - BailLie) / (BailUp - BailLie))) : 0f;
            Vector3 axis = Vector3.Cross(Vector3.up, rig.BailDir);
            if (axis.sqrMagnitude < 1e-4f) axis = Vector3.right;
            Quaternion tip = Quaternion.AngleAxis(angle, axis.normalized);
            Vector3 feet = p.transform.position;
            rig.Visual.rotation = tip * Quaternion.LookRotation(rig.BailChest, Vector3.up);
            rig.Visual.position = feet;
            // the knees give, the arms reach out to catch the fall, toward the ground
            rig.Hips.position -= Vector3.up * (0.08f * Mathf.Clamp01(angle / 40f));
            for (int i = 0; i < 2; i++)
            {
                Transform[] c = rig.Chains[i];
                float len = Vector3.Distance(c[0].position, c[1].position) + Vector3.Distance(c[1].position, c[2].position);
                Vector3 side = (i == (int)Limb.LeftArm ? -1f : 1f) * rig.Visual.right;
                Vector3 target = c[0].position + (rig.BailDir * 0.7f + Vector3.down * 0.6f + side * 0.35f).normalized * len * 0.9f;
                Reach(c[0], c[1], c[2], target, c[0].position - rig.BailDir * 0.3f + Vector3.down * 0.3f, Mathf.Clamp01(angle / 30f));
            }
        }

        /// <summary>The board tipped onto its nose (1) or tail (-1) wheels by some degrees, round that truck.</summary>
        private static Quaternion Pivot(float end, float degrees, ref Vector3 lift, Vector3 n, Vector3 nose)
        {
            // turning round the truck at that end: the middle rises by the truck's distance times the angle's sine
            lift += n * (Board.TruckZ * Mathf.Sin(Mathf.Abs(degrees) * Mathf.Deg2Rad));
            return Quaternion.Euler(end * degrees, 0f, 0f);
        }

        private static bool GrabHand(Tricks.Grab g, bool front)
        {
            switch (g)
            {
                case Tricks.Grab.Indy: case Tricks.Grab.Tailgrab: return !front;
                case Tricks.Grab.Melon: case Tricks.Grab.Method: case Tricks.Grab.Nosegrab: return front;
                default: return false;
            }
        }

        /// <summary>Where on the board a grab holds it, in the board's own space (z along the nose, x to the toe side when regular).</summary>
        private static Vector3 GrabSpot(Tricks.Grab g, float stance)
        {
            float toe = 0.11f * stance;   // (the chest faces +x of the board for regular riders: the toe edge)
            switch (g)
            {
                case Tricks.Grab.Indy: return new Vector3(toe, Board.DeckTop, -0.02f);
                case Tricks.Grab.Melon: return new Vector3(-toe, Board.DeckTop, 0.04f);
                case Tricks.Grab.Method: return new Vector3(-toe, Board.DeckTop, -0.05f);
                case Tricks.Grab.Nosegrab: return new Vector3(0f, Board.DeckTop + 0.04f, 0.38f);
                case Tricks.Grab.Tailgrab: return new Vector3(0f, Board.DeckTop + 0.04f, -0.38f);
                default: return new Vector3(0f, Board.DeckTop, 0f);
            }
        }

        /// <summary>Another player's pose changed: the sound of it.</summary>
        private static void Changed(Player p, Ride from, Ride to)
        {
            Vector3 at = p.transform.position;
            if (to == Ride.Air && from != Ride.Off) Sounds.Play(Sfx.Pop, at, 0.7f);
            else if ((to == Ride.Roll || to == Ride.Grind) && from == Ride.Air) Sounds.Play(Sfx.Land, at, 0.7f);
            else if (to == Ride.Bail) Sounds.Play(Sfx.Bail, at, 0.9f);
            else if (to == Ride.Roll && from == Ride.Off) Sounds.Play(Sfx.Land, at, 0.5f);
        }

        // ---- a bail: the board on its own ----

        public static void Bailed(Player p, Vector3 v)
        {
            if (p != null && Rigs.TryGetValue(p, out Rig rig)) Bailed(p, rig, v);
        }

        private static void Bailed(Player p, Rig rig, Vector3 v)
        {
            if (rig.Loose != null) return;
            Vector3 at = rig.Board != null ? rig.Board.transform.position : p.transform.position;
            Quaternion rot = rig.Board != null ? rig.Board.transform.rotation : p.transform.rotation;
            rig.Loose = Board.Model(p.gameObject.layer);
            rig.Loose.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, rot);
            rig.LooseV = new Vector3(v.x, 0f, v.z) * 0.8f + Vector3.up * 3f;
            rig.LooseSpin = new Vector3(Random.Range(-720f, 720f), Random.Range(-360f, 360f), Random.Range(-720f, 720f));
            rig.LooseUntil = Time.time + 4f;
            rig.LooseRest = false;
        }

        private static void Loose(Rig rig)
        {
            if (rig.Loose == null) return;
            if (Time.time > rig.LooseUntil) { Object.Destroy(rig.Loose); rig.Loose = null; return; }
            if (rig.LooseRest) return;
            float dt = Time.deltaTime;
            Transform t = rig.Loose.transform;
            rig.LooseV += Physics.gravity * dt;
            Vector3 step = rig.LooseV * dt;
            if (Physics.Raycast(t.position + Vector3.up * 0.1f, step.normalized, out RaycastHit hit, step.magnitude + 0.12f, Skate.Mask, QueryTriggerInteraction.Ignore) && rig.LooseV.y < 0f)
            {
                // down: wheels up or down, lying flat on what it hit
                bool upsideDown = Vector3.Dot(t.up, hit.normal) < 0f;
                Vector3 fwd = Vector3.ProjectOnPlane(t.forward, hit.normal);
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(t.right, hit.normal);
                t.SetPositionAndRotation(hit.point + hit.normal * (upsideDown ? Board.DeckTop : 0f), Quaternion.LookRotation(fwd.normalized, upsideDown ? -hit.normal : hit.normal));
                rig.LooseRest = true;
                Sounds.Play(Sfx.Clatter, hit.point, 0.8f);
                return;
            }
            t.position += step;
            t.rotation = Quaternion.Euler(rig.LooseSpin * dt) * t.rotation;
        }

        // ---- the reach ----

        /// <summary>
        /// Two bones reaching for a target (the shoulder or hip, the elbow or knee, the hand or foot), the middle joint bent toward the pole,
        /// blended over the animation by the weight (the analytic two-bone reach: the bend first for the distance, then the aim, then the
        /// turn round the aim toward the pole).
        /// </summary>
        private static void Reach(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float weight)
        {
            if (weight <= 0f) return;
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

        /// <summary>For the status command.</summary>
        public static string Describe(Player p)
        {
            if (p == null || !Rigs.TryGetValue(p, out Rig rig)) return "no rig";
            float floor = Physics.Raycast(p.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2f, Skate.Mask, QueryTriggerInteraction.Ignore) ? hit.point.y - p.transform.position.y : 99f;
            return $"posed {rig.Posed}, board {(rig.Board != null)} at {(rig.Board != null ? rig.Board.transform.position.y - p.transform.position.y : 0f):0.00} over the feet, floor {floor:0.00}, push {rig.Push:0.00}, lean {rig.Lean:0.00}, loose {(rig.Loose != null)}, yaw {rig.Yaw:0}";
        }
    }
}
