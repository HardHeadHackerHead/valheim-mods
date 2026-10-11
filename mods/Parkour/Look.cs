using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// How the moves look. The game has no wall-run or climbing animations, so for now (custom animations come later, here):
    ///   * Runs and climbs play the game's run animation, the body leaning into the wall. The lean is the game's own: while a character is
    ///     "wall running" (m_wallRunning, set by Moves), Character.UpdateGroundTilt tilts its body toward the surface and shares the tilt
    ///     with every other player (the ZDO's tiltrot), so everyone sees it, with the mod or without. Moves says which way and how far.
    ///   * A wall jump is the game's own jump (Character.ForceJump: its animation, sound and dust), a landing roll the game's dodge roll.
    ///   * On a ledge the hands hold the edge (the arm bones turned after the animation), hand over hand along it in a shimmy, and the
    ///     pull-up is the body moved up and over in Moves, the arms bending as it rises. Other players with the mod see it too: each game
    ///     shares its ledge on its player's ZDO (Share.cs) and poses the others from it.
    /// </summary>
    internal static class Look
    {
        private static Vector3 _leanNormal;
        private static float _leanDegrees;
        private static bool _leaning;

        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly AccessTools.FieldRef<Character, Vector3> GroundNormal = AccessTools.FieldRefAccess<Character, Vector3>("m_lastGroundNormal");
        private static readonly int OnGround = ZSyncAnimation.GetHash("onGround"), Falling = ZSyncAnimation.GetHash("falling"),
            ForwardSpeed = ZSyncAnimation.GetHash("forward_speed"), SidewaySpeed = ZSyncAnimation.GetHash("sideway_speed");

        public static void Reset()
        {
            Stop();
            Bodies.Clear();
            Gone.Clear();
            _nextSweep = 0f;
        }

        public static void Stop()
        {
            _leaning = false;
            Player me = Player.m_localPlayer;
            if (me != null && Bodies.TryGetValue(me, out Body b)) b.Hold = 0f;
        }

        /// <summary>Lean the body this many degrees from upright, its head away from the wall (toward its normal).</summary>
        public static void Lean(Vector3 normal, float degrees)
        {
            _leanNormal = normal;
            _leanDegrees = degrees;
            _leaning = true;
        }

        /// <summary>The run animation at this speed, feet down, while the body is really in the air.</summary>
        public static void Running(Player p, float speed) => Animate(p, true, speed);

        /// <summary>Sliding this way: the game's crouched pose, still (its legs don't walk), the body leant back (Lean), and the slide's
        /// own legs and arms on top (SlidingBody).</summary>
        public static void Sliding(Player p, Vector3 dir)
        {
            Animate(p, true, 0f);
            Body b = BodyOf(p);
            b.SlideDir = dir;
        }

        public static void Hanging(Player p, Vector3 edge, Vector3 normal)
        {
            Animate(p, true, 0f);
            Body b = BodyOf(p);
            if (Moves.State == Move.Hang && b.Hold <= 0f) b.GripsSet = false;   // (a new grab: the hands take hold where they reach)
            b.Edge = edge; b.EdgeNormal = normal; b.Hold = 1f;
        }

        /// <summary>This step the hang moved along the edge (side 1: right, facing the wall), at this speed.</summary>
        public static void Shimmying(float side, float speed)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            Body b = BodyOf(me);
            b.ShimmySide = side;
            b.ShimmySpeed = speed;
            b.ShimmyAt = Time.time;
        }

        /// <summary>Pulling up: the hands stay on the edge as the body rises, and let go as it goes over ("over" from 0 to 1).</summary>
        public static void PullingUp(Player p, Vector3 edge, Vector3 normal, float over, float progress, bool vault)
        {
            Animate(p, true, 0f);
            Body b = BodyOf(p);
            b.Edge = edge; b.EdgeNormal = normal; b.Hold = 1f - over;
            b.PullT = progress; b.PullOver = over; b.Vault = vault;
        }

        private static void Animate(Player p, bool feetDown, float speed)
        {
            ZSyncAnimation anim = Anim(p);
            if (anim == null) return;
            // (after the game's own walking step set them for a body in the air; these are shared with the other players)
            anim.SetBool(OnGround, feetDown);
            anim.SetBool(Falling, false);
            anim.SetFloat(ForwardSpeed, speed);
            anim.SetFloat(SidewaySpeed, 0f);
        }

        // The game sets its "crouching" animation from the crouch toggle every step (Player.UpdateCrouch): during a slide, crouched.
        [HarmonyPatch(typeof(Player), "UpdateCrouch")]
        private static class Player_UpdateCrouch
        {
            private static readonly int Crouching = ZSyncAnimation.GetHash("crouching");

            private static void Postfix(Player __instance)
            {
                if (Moves.State == Move.Slide && ReferenceEquals(__instance, Player.m_localPlayer)) Anim(__instance)?.SetBool(Crouching, true);
            }
        }

        // ---- the lean, through the game's own tilt ----

        // The game tilts a wall-running player toward m_lastGroundNormal (by 65% of the way from upright). For the length of the game's tilt
        // step only, that normal is swapped for one that gives our lean, then put back.
        [HarmonyPatch(typeof(Character), "UpdateGroundTilt")]
        private static class Character_UpdateGroundTilt
        {
            private static Vector3 _saved;
            private static bool _swapped;

            private static void Prefix(Character __instance)
            {
                if (!_leaning || Moves.State == Move.None || !ReferenceEquals(__instance, Player.m_localPlayer)) return;
                float a = _leanDegrees * Mathf.Deg2Rad;
                Vector3 up = Vector3.up * Mathf.Cos(a) + _leanNormal * Mathf.Sin(a);   // where the body's up should point
                _saved = GroundNormal(__instance);
                GroundNormal(__instance) = (up - Vector3.up * 0.35f) / 0.65f;           // (so that the game's Lerp(up, it, 0.65) lands on it)
                _swapped = true;
            }

            private static void Finalizer(Character __instance)
            {
                if (!_swapped) return;
                GroundNormal(__instance) = _saved;
                _swapped = false;
            }
        }

        // ---- the hands on the edge, for every player on a ledge ----

        /// <summary>
        /// One player's pose on a ledge: their bones, their hands' holds and the shimmy's stepping. The local player's comes from Moves; every
        /// other player's from what their game shares on their ZDO (Share.cs), so players with the mod see each other hang and shimmy.
        /// </summary>
        private sealed class Body
        {
            public Player Player;
            public Transform Root, LArm, LFore, LHand, RArm, RFore, RHand, Hips, LLeg, RLeg;
            public Transform LKnee, LFoot, LToe, RKnee, RFoot, RToe, Neck, Head;
            public Vector3 LToeAxis, LTopAxis, RToeAxis, RTopAxis;   // each foot's own axes (local): toward its toes, and out of its top
            public float ArmW, HangW, PullW;       // how far the arms, the hanging body and the pulling-up body are posed (eased in and out)
            public float PullT, PullOver;          // the pull-up's progress (0 to 1, a little past) and its "over the top" part (0 to 1)
            public bool Vault;                     // over a thin top: the legs swing over the side
            public float SlideW;                   // how far the slide's pose is in
            public Vector3 SlideDir;
            public Transform Spine1, Spine2;
            // the feet: each planted on the wall under the edge (or hanging loose without one), stepping along in turn in a shimmy
            public Vector3 LPlant, RPlant, FootFrom, FootTo;
            public bool PlantsSet, LWall, RWall;
            public int FootStepping, LastFoot;
            public float FootT;
            public int Pose;                       // Share.Hang or Share.PullUp while posed
            public float PoseAt;                   // when this pose was first seen (a remote pull-up lets go by it)
            public Vector3 Edge, EdgeNormal;
            public float Hold;                     // 0 hands free, 1 hands on the edge
            // the shimmy: each hand planted on the edge until the body has moved on, then a quick reach to a new hold (hand over hand)
            public Vector3 LGrip, RGrip, StepFrom, StepTo;
            public bool GripsSet;
            public int Stepping;                   // 0 none, -1 the left hand moving, 1 the right
            public int LastHand;                   // the hand that moved last (they take turns)
            public float StepT, ShimmySide, ShimmySpeed, ShimmyAt = -10f, Swing;
        }

        private static readonly Dictionary<Player, Body> Bodies = new Dictionary<Player, Body>();
        private static readonly List<Player> Gone = new List<Player>();
        private static float _nextSweep;
        private const float PoseRange = 40f;
        private const float Blend = 0.15f;      // seconds to ease a pose in or out

        /// <summary>The local player's shimmy as shared: side times speed while moving along the edge, else 0.</summary>
        public static float LocalShimmy
        {
            get
            {
                Player me = Player.m_localPlayer;
                return me != null && Bodies.TryGetValue(me, out Body b) && Time.time - b.ShimmyAt < 0.15f ? b.ShimmySide * b.ShimmySpeed : 0f;
            }
        }

        private static Body BodyOf(Player p)
        {
            if (!Bodies.TryGetValue(p, out Body b)) Bodies[p] = b = new Body { Player = p };
            return b;
        }

        /// <summary>Every frame after the animation: the arms of every player on a ledge near by reach up to it.</summary>
        public static void Late()
        {
            Player me = Player.m_localPlayer;
            if (me != null && Bodies.TryGetValue(me, out Body mine))
            {
                if (Moves.State == Move.Hang || Moves.State == Move.PullUp) mine.Pose = Moves.State == Move.Hang ? Share.Hang : Share.PullUp;
                else { mine.Pose = Moves.State == Move.Slide ? Share.Slide : Share.None; mine.Hold = 0f; }
                if (mine.Pose != Share.None || mine.ArmW > 0f || mine.HangW > 0f || mine.PullW > 0f || mine.SlideW > 0f) Pose(mine);
            }

            // the others, only from what their games share (never from Moves)
            Vector3 eye = me != null ? me.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p == me || (p.transform.position - eye).sqrMagnitude > PoseRange * PoseRange) continue;
                ZDO zdo = Share.ZdoOf(p);
                Vector3 edge = Vector3.zero, normal = Vector3.zero;
                float shimmy = 0f;
                int pose = zdo != null ? Share.Read(zdo, out edge, out normal, out shimmy) : Share.None;
                // (an edge far from the body is stale or odd: not posed)
                bool vault = pose == Share.Vault;
                if (vault) pose = Share.PullUp;
                // (a slide's "edge" is where it began, the body slides on from it: no distance check for it)
                if ((pose != Share.Hang && pose != Share.PullUp && pose != Share.Slide) || (pose != Share.Slide && (p.transform.position - edge).sqrMagnitude > 9f))
                {
                    if (Bodies.TryGetValue(p, out Body was))
                    {
                        was.Pose = Share.None;
                        was.Hold = 0f;
                        if (was.ArmW > 0f || was.HangW > 0f || was.PullW > 0f || was.SlideW > 0f) Pose(was);
                    }
                    continue;
                }
                Body b = BodyOf(p);
                if (b.Pose != pose)
                {
                    if (pose == Share.Hang) b.GripsSet = false;   // (a new grab: the hands take hold where they reach)
                    else if (b.Pose != Share.Hang) b.PlantsSet = false;   // (a pull-up seen without its hang: the feet start where they are)
                    b.Pose = pose;
                    b.PoseAt = Time.time;
                }
                if (pose == Share.Slide)
                {
                    b.SlideDir = -normal;   // (shared as the slide's facing back)
                    b.Hold = 0f;
                    Pose(b);
                    continue;
                }
                // the edge is written now and then: between writes it slides along its line with the body (a shimmy)
                Vector3 along = Vector3.Cross(Vector3.up, -normal);
                b.Edge = edge + along * Vector3.Dot(p.transform.position - edge, along);
                b.EdgeNormal = normal;
                if (Mathf.Abs(shimmy) > 0.05f) { b.ShimmySide = Mathf.Sign(shimmy); b.ShimmySpeed = Mathf.Abs(shimmy); b.ShimmyAt = Time.time; }
                // a pull-up: the hands hold while the body rises, and let go as it goes over the top (timed as Moves times its own)
                b.Vault = vault;
                b.PullT = (Time.time - b.PoseAt) / 0.4f;
                b.PullOver = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((b.PullT - 0.6f) / 0.4f));
                b.Hold = pose == Share.Hang ? 1f : 1f - b.PullOver;
                Pose(b);
            }

            // players gone (logged out, or out of range and unloaded), and others no longer posed, are forgotten now and then
            if (Time.time < _nextSweep) return;
            _nextSweep = Time.time + 2f;
            foreach (KeyValuePair<Player, Body> kv in Bodies)
                if (kv.Key == null || (kv.Key != me && kv.Value.Pose == Share.None && kv.Value.ArmW <= 0f && kv.Value.HangW <= 0f && kv.Value.PullW <= 0f && kv.Value.SlideW <= 0f)) Gone.Add(kv.Key);
            for (int i = 0; i < Gone.Count; i++) Bodies.Remove(Gone[i]);
            Gone.Clear();
        }

        private static void Pose(Body b)
        {
            if (b.LHand == null) Rig(b);
            if (b.LHand == null || b.RHand == null) return;
            Vector3 forward = -b.EdgeNormal, right = Vector3.Cross(Vector3.up, forward);
            Vector3 grip = b.Edge + b.EdgeNormal * 0.06f + Vector3.up * 0.03f;
            Vector3 lHome = grip - right * 0.22f, rHome = grip + right * 0.22f;
            bool hanging = b.Pose == Share.Hang;
            bool moving = hanging && Time.time - b.ShimmyAt < 0.15f;
            b.ArmW = Mathf.MoveTowards(b.ArmW, b.Hold, Time.deltaTime / Blend);
            b.HangW = Mathf.MoveTowards(b.HangW, hanging ? 1f : 0f, Time.deltaTime / Blend);
            b.PullW = Mathf.MoveTowards(b.PullW, b.Pose == Share.PullUp ? 1f : 0f, Time.deltaTime / Blend);
            b.SlideW = Mathf.MoveTowards(b.SlideW, b.Pose == Share.Slide ? 1f : 0f, Time.deltaTime / (b.Pose == Share.Slide ? 0.12f : Blend));
            if (!b.GripsSet) { b.LGrip = lHome; b.RGrip = rHome; b.GripsSet = true; b.Stepping = 0; b.PlantsSet = false; }
            if (hanging) Shuffle(b, lHome, rHome, right, moving);
            else if (b.Pose == Share.PullUp)
            {
                // pulling up: both hands where the body is, then pressing down on the top beside it as the body goes over
                float press = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.PullOver * 2f));
                Vector3 onTop = b.Edge - b.EdgeNormal * 0.18f + Vector3.up * 0.02f;
                b.LGrip = Vector3.Lerp(lHome, onTop - right * 0.28f, press);
                b.RGrip = Vector3.Lerp(rHome, onTop + right * 0.28f, press);
            }

            // the body swings a little with each reach, the legs against it, while moving; settling when it stops
            b.Swing = Mathf.MoveTowards(b.Swing, moving ? 1f : 0f, Time.deltaTime * 4f);
            if (b.Swing > 0f && b.Hips != null)
            {
                float phase = Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Max(1.2f, b.ShimmySpeed * 1.4f));
                b.Hips.rotation = Quaternion.AngleAxis(phase * 6f * b.Swing, forward) * Quaternion.AngleAxis(b.ShimmySide * 4f * b.Swing, Vector3.up) * b.Hips.rotation;
                if (b.LLeg != null) b.LLeg.rotation = Quaternion.AngleAxis(phase * 12f * b.Swing, right) * b.LLeg.rotation;
                if (b.RLeg != null) b.RLeg.rotation = Quaternion.AngleAxis(-phase * 12f * b.Swing, right) * b.RLeg.rotation;
            }

            if (b.HangW > 0f) HangingBody(b, forward, right, moving);
            if (b.PullW > 0f) PullingBody(b, forward, right);
            if (b.SlideW > 0f) SlidingBody(b);
            Reach(b.LArm, b.LFore, b.LHand, Lifted(b, b.LGrip, -1), -right - Vector3.up * 0.4f + b.EdgeNormal * 0.3f, b.ArmW);
            Reach(b.RArm, b.RFore, b.RHand, Lifted(b, b.RGrip, 1), right - Vector3.up * 0.4f + b.EdgeNormal * 0.3f, b.ArmW);
            // (the head is left to the game: it turns it toward where the player looks, and a tilt of ours on top fought it: shaking)
        }

        /// <summary>
        /// Hanging: a slow small sway when still, and the legs: knees bent, each foot planted on the wall under the edge (the knee out from
        /// it), stepping along in turn in a shimmy; with no wall under the edge (an overhang, a floor on posts) they hang loose.
        /// </summary>
        private static void HangingBody(Body b, Vector3 forward, Vector3 right, bool moving)
        {
            float w = b.HangW;
            if (b.Hips != null)
            {
                float sway = Mathf.Sin(Time.time * Mathf.PI * 2f / 2.5f) * 2.5f * (1f - b.Swing) * w;
                b.Hips.rotation = Quaternion.AngleAxis(sway, forward) * b.Hips.rotation;
            }
            if (b.LLeg == null || b.LKnee == null || b.LFoot == null || b.RLeg == null || b.RKnee == null || b.RFoot == null) return;
            Vector3 n = b.EdgeNormal;
            Vector3 lHome = FootHome(b.LLeg.position, LegLength(b.LLeg, b.LKnee, b.LFoot), n, right, -1f, out b.LWall);
            Vector3 rHome = FootHome(b.RLeg.position, LegLength(b.RLeg, b.RKnee, b.RFoot), n, right, 1f, out b.RWall);
            if (!b.PlantsSet) { b.LPlant = lHome; b.RPlant = rHome; b.PlantsSet = true; b.FootStepping = 0; }
            Feet(b, lHome, rHome, right, moving);
            Leg(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, Raised(b, b.LPlant, -1), n, b.LWall, w);
            Leg(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, Raised(b, b.RPlant, 1), n, b.RWall, w);
        }

        /// <summary>
        /// Pulling up. Rising: the feet push off where they stood on the wall (the knees bend a moment, then straighten as the body rises
        /// past them). Over the top: the chest leans forward over it, the leading knee comes up onto the top just past the edge, and the
        /// other leg follows; over a thin top (a vault), the legs swing over together, out to the side.
        /// </summary>
        private static void PullingBody(Body b, Vector3 forward, Vector3 right)
        {
            float w = b.PullW;
            if (b.LLeg == null || b.LKnee == null || b.LFoot == null || b.RLeg == null || b.RKnee == null || b.RFoot == null) return;
            Vector3 n = b.EdgeNormal;
            if (!b.PlantsSet)
            {
                b.LPlant = FootHome(b.LLeg.position, LegLength(b.LLeg, b.LKnee, b.LFoot), n, right, -1f, out b.LWall);
                b.RPlant = FootHome(b.RLeg.position, LegLength(b.RLeg, b.RKnee, b.RFoot), n, right, 1f, out b.RWall);
                b.PlantsSet = true;
            }
            float over = b.PullOver;
            float push = Mathf.Sin(Mathf.Clamp01(b.PullT / 0.35f) * Mathf.PI) * 0.15f;   // (the knees bend as the push begins)
            Vector3 lFrom = b.LPlant + Vector3.up * push, rFrom = b.RPlant + Vector3.up * push;
            Vector3 top = b.Edge - n * 0.28f + Vector3.up * 0.03f;

            float lean = (b.Vault ? 12f : 25f) * over * w;   // the chest forward over the top
            Turn(b.Spine1, right, lean * 0.5f);
            Turn(b.Spine2, right, lean * 0.5f);

            Vector3 outFrom = n + Vector3.up * 0.2f;
            if (b.Vault)
            {
                // both legs swing over together, out to the side, and down beyond the top
                float arc = Mathf.Sin(over * Mathf.PI);
                Vector3 swing = top - n * (0.4f * over) + right * (0.45f * arc) + Vector3.up * (0.2f * arc);
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(over * 1.5f));
                Vector3 pole = Vector3.Lerp(outFrom, right + Vector3.up, k);
                LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, Vector3.Lerp(lFrom, swing - right * 0.1f, k), pole, Vector3.Lerp(-n, right, k), w);
                LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, Vector3.Lerp(rFrom, swing + right * 0.06f, k), pole, Vector3.Lerp(-n, right, k), w);
                return;
            }
            // the leading (right) knee comes up onto the top, then the other foot follows onto it
            float knee = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(over * 2f));
            float follow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(over * 2f - 1f));
            LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, Vector3.Lerp(rFrom, top + right * 0.1f, knee),
                Vector3.Lerp(outFrom, forward * 0.3f + Vector3.up, knee), Vector3.Lerp(-n, forward - Vector3.up * 0.5f, knee), w);
            LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, Vector3.Lerp(lFrom, top - n * 0.15f - right * 0.1f, follow),
                Vector3.Lerp(outFrom, forward * 0.3f + Vector3.up, follow), Vector3.Lerp(-n, forward - Vector3.up * 0.5f, follow), w);
        }

        /// <summary>
        /// Sliding: the leading (right) leg stretched out in front, its foot a little off the ground, toes up; the other knee bent under the
        /// body, its shin along the ground; the chest further back; the trailing (left) hand down on the ground behind the hip, dragging
        /// along it; the other arm out for balance.
        /// </summary>
        private static void SlidingBody(Body b)
        {
            float w = b.SlideW;
            Vector3 dir = b.SlideDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float ground = b.Player.transform.position.y;

            Turn(b.Spine1, right, -6f * w);   // (the chest further back than the body's lean)
            Turn(b.Spine2, right, -6f * w);

            if (b.RLeg != null && b.RKnee != null && b.RFoot != null)
            {
                Vector3 hip = b.RLeg.position;
                Vector3 front = new Vector3(hip.x, ground + 0.12f, hip.z) + dir * 0.75f;
                LegAt(b.RLeg, b.RKnee, b.RFoot, b.RToeAxis, b.RTopAxis, front, Vector3.up + dir * 0.2f, Vector3.up + dir * 0.4f, w);
            }
            if (b.LLeg != null && b.LKnee != null && b.LFoot != null)
            {
                Vector3 hip = b.LLeg.position;
                Vector3 under = new Vector3(hip.x, ground + 0.08f, hip.z) - dir * 0.35f - right * 0.05f;
                LegAt(b.LLeg, b.LKnee, b.LFoot, b.LToeAxis, b.LTopAxis, under, dir + Vector3.down * 0.5f, -dir, w);
            }
            if (b.LArm != null && b.LFore != null && b.LHand != null)
            {
                // the trailing hand on the ground behind and beside the hip, wherever the ground is there
                Vector3 reach = b.Player.transform.position - dir * 0.35f - right * 0.35f + Vector3.up * 0.6f;
                Vector3 onGround = Physics.Raycast(reach, Vector3.down, out RaycastHit hit, 1.2f, Walls.Mask, QueryTriggerInteraction.Ignore)
                    ? hit.point + Vector3.up * 0.03f : new Vector3(reach.x, ground + 0.03f, reach.z);
                Reach(b.LArm, b.LFore, b.LHand, onGround, -right + Vector3.up * 0.3f, w);
            }
            if (b.RArm != null && b.RFore != null && b.RHand != null)
                Reach(b.RArm, b.RFore, b.RHand, b.RArm.position + right * 0.45f + dir * 0.25f + Vector3.up * 0.05f, Vector3.down - dir * 0.3f, w);
        }

        private const bool PlantFeet = false;   // feet on the wall while hanging (off: they hang loose)

        /// <summary>Where a foot rests: on the wall a little below and in front of its hip, or, with no wall there, hanging under it.</summary>
        private static Vector3 FootHome(Vector3 hip, float leg, Vector3 n, Vector3 right, float side, out bool wall)
        {
            // the wall's distance below the hip; the foot goes where a nearly straight leg meets it (knee bent some 25-40 degrees), 0.55
            // to 0.85 m under the hip; a wall out of the leg's reach is no wall
            // (feet planted on the wall looked wrong in play, so the legs hang loose: PlantFeet brings the wall back if ever wanted)
            Vector3 from = hip + right * (side * 0.11f) + Vector3.down * 0.7f + n * 0.4f;
            RaycastHit hit = default;
            wall = PlantFeet && Physics.Raycast(from, -n, out hit, 1.2f, Walls.Mask, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.6f;
            float away = wall ? Vector3.Dot(hip - hit.point, n) : 0f;
            if (wall && away > 0f && away < leg * 0.9f)
            {
                float drop = Mathf.Clamp(Mathf.Sqrt(leg * leg - away * away) - 0.12f, 0.55f, 0.85f);
                Vector3 on = hit.point;
                on.y = hip.y - drop;
                return on + n * 0.07f;
            }
            wall = false;
            return hip + Vector3.down * (leg * 0.96f) + right * (side * 0.05f) - n * 0.05f;   // (hanging nearly straight, slightly bent)
        }

        private static float LegLength(Transform hip, Transform knee, Transform foot) =>
            Vector3.Distance(hip.position, knee.position) + Vector3.Distance(knee.position, foot.position);

        /// <summary>
        /// The feet in a shimmy, like the hands: each stays planted on the wall while the body moves on; once it falls behind, the leading foot
        /// steps along, then the trailing one. Stopped, they ease back under the hips. A foot with no wall to stand on just hangs.
        /// </summary>
        private static void Feet(Body b, Vector3 lHome, Vector3 rHome, Vector3 right, bool moving)
        {
            b.LPlant = b.LWall ? lHome + right * Vector3.Dot(b.LPlant - lHome, right) : lHome;
            b.RPlant = b.RWall ? rHome + right * Vector3.Dot(b.RPlant - rHome, right) : rHome;
            if (b.FootStepping != 0)
            {
                b.FootT += Time.deltaTime / 0.22f;
                Vector3 at = Vector3.Lerp(b.FootFrom, b.FootTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.FootT)));
                if (b.FootStepping < 0) b.LPlant = at; else b.RPlant = at;
                if (b.FootT >= 1f) b.FootStepping = 0;
                return;
            }
            if (!moving)
            {
                b.LPlant = Vector3.MoveTowards(b.LPlant, lHome, Time.deltaTime * 0.8f);
                b.RPlant = Vector3.MoveTowards(b.RPlant, rHome, Time.deltaTime * 0.8f);
                return;
            }
            float side = b.ShimmySide;
            int lead = side > 0f ? 1 : -1;
            int next = b.LastFoot == lead ? -lead : lead;
            if (next > 0 ? !b.RWall : !b.LWall) { b.LastFoot = next; return; }   // (a loose foot doesn't step: the other one goes next)
            Vector3 plant = next > 0 ? b.RPlant : b.LPlant, home = next > 0 ? rHome : lHome;
            float behind = -Vector3.Dot(plant - home, right * side);
            if (next == lead && behind > 0.04f) StepFoot(b, lead, plant, home + right * side * 0.12f);
            else if (next != lead && behind > 0.08f) StepFoot(b, -lead, plant, home + right * side * 0.04f);
        }

        private static void StepFoot(Body b, int foot, Vector3 from, Vector3 to)
        {
            b.FootStepping = foot;
            b.LastFoot = foot;
            b.FootT = 0f;
            b.FootFrom = from;
            b.FootTo = to;
        }

        /// <summary>A foot stepping along the wall comes off it a little and back.</summary>
        private static Vector3 Raised(Body b, Vector3 plant, int foot) =>
            b.FootStepping != foot ? plant : plant + b.EdgeNormal * (0.1f * Mathf.Sin(Mathf.Clamp01(b.FootT) * Mathf.PI));

        /// <summary>A leg reaches its foot's spot, the knee out from the wall (or bent a little forward, hanging loose), the toes to the wall.</summary>
        private static void Leg(Transform hip, Transform knee, Transform foot, Vector3 toeAxis, Vector3 topAxis, Vector3 target, Vector3 n, bool wall, float w) =>
            LegAt(hip, knee, foot, toeAxis, topAxis, target, wall ? n + Vector3.down * 0.5f : -n,
                wall ? -n * 0.8f + Vector3.down * 0.4f : Vector3.down * 0.8f - n * 0.5f, w);   // (toes into the wall and a little down; loose: down and forward)   // (the knee out from the wall and down, never into it)

        /// <summary>A leg reaches a spot, the knee bent toward "pole", the toes pointing "toes" ("w" of the way).</summary>
        private static void LegAt(Transform hip, Transform knee, Transform foot, Vector3 toeAxis, Vector3 topAxis, Vector3 target, Vector3 pole, Vector3 toes, float w)
        {
            Reach(hip, knee, foot, target, pole, w);
            if (w <= 0f || toeAxis == Vector3.zero) return;
            // the reach only places the ankle: the foot is turned explicitly, its own toe axis to "toes" and its top up (as near as it goes)
            Vector3 top = Vector3.ProjectOnPlane(Vector3.up, toes);
            if (top.sqrMagnitude < 1e-4f) top = Vector3.ProjectOnPlane(pole, toes);
            if (top.sqrMagnitude < 1e-4f || toes.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(toes.normalized, top.normalized) * Quaternion.Inverse(Quaternion.LookRotation(toeAxis, topAxis));
            foot.rotation = w >= 1f ? want : Quaternion.Slerp(foot.rotation, want, w);
        }

        /// <summary>
        /// A foot's own axes, from the rig rather than assumed: toward its toes (the toe bone, else its first child, else the body's
        /// forward), and out of its top (the body's up, square to that), measured in the pose the animation has it in now.
        /// </summary>
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

        /// <summary>Turn a bone about an axis by this many degrees (nothing when it isn't there).</summary>
        private static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null && degrees != 0f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>
        /// Hand over hand: a hand stays where it holds the edge (kept on the edge's line as it is re-found) while the body moves; once the
        /// body has moved past it, the leading hand reaches ahead, then the trailing hand follows. One hand at a time, 0.18 s a reach.
        /// Stopped, the hands ease back to the body's sides.
        /// </summary>
        private static void Shuffle(Body b, Vector3 lHome, Vector3 rHome, Vector3 right, bool moving)
        {
            // on the edge's line: the same height and depth as the edge now, only their place along it kept
            b.LGrip = lHome + right * Vector3.Dot(b.LGrip - lHome, right);
            b.RGrip = rHome + right * Vector3.Dot(b.RGrip - rHome, right);
            if (b.Stepping != 0)
            {
                b.StepT += Time.deltaTime / Mathf.Lerp(0.2f, 0.13f, Mathf.Clamp01((b.ShimmySpeed - 1f) / 1.5f));
                Vector3 at = Vector3.Lerp(b.StepFrom, b.StepTo, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.StepT)));
                if (b.Stepping < 0) b.LGrip = at; else b.RGrip = at;
                if (b.StepT >= 1f) b.Stepping = 0;
                return;
            }
            if (!moving)
            {
                b.LGrip = Vector3.MoveTowards(b.LGrip, lHome, Time.deltaTime * 0.8f);
                b.RGrip = Vector3.MoveTowards(b.RGrip, rHome, Time.deltaTime * 0.8f);
                return;
            }
            // the hands take turns: after the leading hand reaches ahead, the other follows, then the leading again (never one hand twice)
            float side = b.ShimmySide;
            int lead = side > 0f ? 1 : -1;
            int next = b.LastHand == lead ? -lead : lead;
            Vector3 grip = next > 0 ? b.RGrip : b.LGrip, home = next > 0 ? rHome : lHome;
            float behind = -Vector3.Dot(grip - home, right * side);
            float reach = 0.1f + 0.06f * Mathf.Clamp(b.ShimmySpeed, 0f, 3f);   // (further at speed, so the hands keep up)
            if (next == lead && behind > -0.02f) Step(b, lead, grip, home + right * side * reach);              // the leading hand reaches ahead
            else if (next != lead && behind > 0.03f) Step(b, -lead, grip, home + right * side * reach * 0.35f); // the other comes up beside it
        }

        private static void Step(Body b, int hand, Vector3 from, Vector3 to)
        {
            b.Stepping = hand;
            b.LastHand = hand;
            b.StepT = 0f;
            b.StepFrom = from;
            b.StepTo = to;
        }

        /// <summary>A hand on its way to a new hold rises a little off the edge and comes down on it.</summary>
        private static Vector3 Lifted(Body b, Vector3 grip, int hand)
        {
            if (b.Stepping != hand) return grip;
            float arc = Mathf.Sin(Mathf.Clamp01(b.StepT) * Mathf.PI);
            return grip + Vector3.up * (0.07f * arc) + b.EdgeNormal * (0.05f * arc);
        }

        private static void Rig(Body b)
        {
            b.Root = b.Player.transform.Find("Visual/Armature");
            if (b.Root == null) return;
            b.LArm = Find(b.Root, "LeftArm"); b.LFore = Find(b.Root, "LeftForeArm"); b.LHand = Find(b.Root, "LeftHand");
            b.RArm = Find(b.Root, "RightArm"); b.RFore = Find(b.Root, "RightForeArm"); b.RHand = Find(b.Root, "RightHand");
            b.Hips = Find(b.Root, "Hips"); b.LLeg = Find(b.Root, "LeftUpLeg"); b.RLeg = Find(b.Root, "RightUpLeg");
            b.LKnee = Find(b.Root, "LeftLeg"); b.LFoot = Find(b.Root, "LeftFoot"); b.LToe = Find(b.Root, "LeftToeBase");
            b.RKnee = Find(b.Root, "RightLeg"); b.RFoot = Find(b.Root, "RightFoot"); b.RToe = Find(b.Root, "RightToeBase");
            FootAxes(b.Player, b.LFoot, b.LToe, out b.LToeAxis, out b.LTopAxis);   // (now, before anything here has turned a bone this frame)
            FootAxes(b.Player, b.RFoot, b.RToe, out b.RToeAxis, out b.RTopAxis);
            b.Neck = Find(b.Root, "Neck"); b.Head = Find(b.Root, "Head");   // (any not found is left alone)
            b.Spine1 = Find(b.Root, "Spine1"); b.Spine2 = Find(b.Root, "Spine2");
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

        /// <summary>Turn a bone so the line from it to its child points this way.</summary>
        private static void Aim(Transform bone, Transform child, Vector3 direction)
        {
            Vector3 now = child.position - bone.position;
            if (now.sqrMagnitude < 1e-6f || direction.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.FromToRotation(now.normalized, direction.normalized) * bone.rotation;
        }

        /// <summary>The arm reaches the target (two bones laid out by the law of cosines, the elbow bent toward "pole"), "weight" of the way.</summary>
        private static void Reach(Transform arm, Transform fore, Transform hand, Vector3 target, Vector3 pole, float weight)
        {
            if (arm == null || fore == null || hand == null || weight <= 0f) return;
            Quaternion armWas = arm.rotation, foreWas = fore.rotation;
            Vector3 shoulder = arm.position;
            float l1 = Vector3.Distance(arm.position, fore.position), l2 = Vector3.Distance(fore.position, hand.position);
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
            if (weight >= 1f) return;
            arm.rotation = Quaternion.Slerp(armWas, arm.rotation, weight);
            fore.rotation = Quaternion.Slerp(foreWas, fore.rotation, weight);
        }
    }
}
