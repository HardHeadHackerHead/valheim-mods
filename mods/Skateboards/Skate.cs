using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Skateboards
{
    internal enum Ride { Off, Roll, Air, Grind, Manual, Bail }

    /// <summary>
    /// The riding, run on the player's own game each physics step right after the game's own movement step (Character.UpdateMotion):
    /// the game has moved the player as if no key were held (Controls takes them all), and here the body's velocity is set for the
    /// board. Nothing touches the body's gravity or kinematic flag: gravity is cancelled by adding the velocity it would take away this
    /// step. The player's capsule stays upright and does the colliding; the board, the stance and the tilt on a ramp are drawn
    /// (Look.cs).
    ///
    ///   Roll    on the ground: the board follows the surface under it (any slope, up a quarter pipe to vertical), speed from pushing
    ///           and from the slope, friction from what it rolls on (built floors roll best). Rolling back down a ramp rides fakie.
    ///   Air     off the ground (an ollie, the lip of a ramp, the top of a hill): spins, flips and grabs. Off a near-vertical ramp it is
    ///           a vert air, which turns you round by itself to come back down it.
    ///   Grind   landed on a rail or ledge (Rails.cs): slides along it, balanced with A and D.
    ///   Manual  rolling on two wheels while Crouch is held, balanced with W and S.
    ///   Bail    landed crooked, mid-flip or fell off: the board flies off and the game's stagger plays; you are back on it a moment
    ///           later. The combo is lost.
    /// </summary>
    internal static class Skate
    {
        public static Ride State;
        public static bool Active => State != Ride.Off;

        // rolling
        private static Vector3 _dir = Vector3.forward, _n = Vector3.up, _lastPos;
        private static float _speed, _onFor, _chargeFrom = -1f, _lastPushTell;
        public static bool Fakie;
        private static Collider _ground, _takeoffGround;
        /// <summary>For the look: pushing with a foot now, and how hard it carves (-1 left to 1 right, eased by speed).</summary>
        public static bool Pushing;
        public static float Lean;
        private static float _landedAt = -10f;
        private static bool _landedRamp;

        // in the air: the board's nose as a yaw on the level (Look.NoseOnSurface maps it back onto a ramp), the spin given by the keys,
        // the turn a vert air makes by itself, the surface it left
        private static float _airYaw, _peakY, _airFor;
        public static float Spin, AutoSpin;
        private static bool _vert;
        private static int _autoSign = 1;
        private static Vector3 _takeoffN = Vector3.up;

        // grinding
        private static Rails.Rail _rail;
        private static float _railU, _railSign;
        public static float Balance;
        private static float _balanceFor, _noiseSeed;
        private static Collider _leftRail, _lastGrindRail;
        private static float _leftRailAt = -10f, _grindDist;
        private static bool _sameRail, _longGiven;

        // bailing
        private static float _bailUntil;
        public static string LastBail = "";
        public static Vector3 BailVelocity;
        public static int Bails;

        // what tests and the status command read
        public static float TopSpeedSeen, MaxHeightSeen;
        public static readonly List<string> Events = new List<string>();

        private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<Character, CapsuleCollider> Capsule = AccessTools.FieldRefAccess<Character, CapsuleCollider>("m_collider");
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly AccessTools.FieldRef<Character, Vector3> CurrentVel = AccessTools.FieldRefAccess<Character, Vector3>("m_currentVel");
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        private static readonly System.Reflection.MethodInfo SetCrouch = AccessTools.Method(typeof(Player), "SetCrouch");
        private static readonly Func<Character, bool> InSwimDepth = AccessTools.MethodDelegate<Func<Character, bool>>(AccessTools.Method(typeof(Character), "InLiquidSwimDepth", Type.EmptyTypes));
        private static readonly int OnGround = ZSyncAnimation.GetHash("onGround"), Falling = ZSyncAnimation.GetHash("falling"),
            ForwardSpeed = ZSyncAnimation.GetHash("forward_speed"), SidewaySpeed = ZSyncAnimation.GetHash("sideway_speed"), Crouching = ZSyncAnimation.GetHash("crouching");

        private static int _mask = -1;
        /// <summary>What the board rolls on: the game's own ground mask (Character.s_groundRayMask).</summary>
        public static int Mask
        {
            get
            {
                if (_mask != -1 && _mask != 0) return _mask;
                try { _mask = (int)AccessTools.Field(typeof(Character), "s_groundRayMask").GetValue(null); } catch (Exception) { _mask = 0; }
                if (_mask == 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                return _mask;
            }
        }

        public static Rigidbody BodyOf(Player p) => Body(p);
        public static float Speed => State == Ride.Air ? 0f : _speed;
        public static Vector3 Dir => _dir;
        public static Vector3 Normal => _n;
        public static Rails.Rail Rail => _rail;
        public static float AirTime => _airFor;
        public static bool Vert => _vert;

        /// <summary>0 to 1 while Jump is held on the ground, winding up an ollie.</summary>
        public static float Charge => _chargeFrom < 0f ? 0f : Mathf.Clamp01((Time.time - _chargeFrom) / 0.6f);

        /// <summary>The board's nose as a yaw on the level, wherever it is (Look.cs and Share.cs).</summary>
        public static float NoseYaw
        {
            get
            {
                switch (State)
                {
                    case Ride.Air: return _airYaw + Spin + AutoSpin;
                    case Ride.Grind:
                    {
                        Vector3 along = _rail.Dir * _railSign;
                        Vector3 nose = Tricks.GrindNow == Tricks.Grind.Boardslide || Tricks.GrindNow == Tricks.Grind.Lipslide ? Vector3.Cross(Vector3.up, along) : along;
                        if (Fakie) nose = -nose;
                        return Yaw(nose);
                    }
                    default: return Yaw(Look.Level(_n, Fakie ? -_dir : _dir));
                }
            }
        }

        /// <summary>The surface the board stands on (the tilt), for Look and Share.</summary>
        public static Vector3 PoseNormal => State == Ride.Roll || State == Ride.Manual ? _n : Vector3.up;

        private static float Yaw(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
        private static Vector3 FromYaw(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        // ---- on and off ----

        /// <summary>Every frame: the drop-in (the attack button with the board in the hand), stepping off with E, the camera.</summary>
        public static void Update()
        {
            Player p = Player.m_localPlayer;
            if (p == null) { if (Active) State = Ride.Off; return; }
            Controls.Frame(p);
            if (!Active)
            {
                if (Controls.TakeAttack() && Board.InHand(p)) Start(p);
                return;
            }
            if (Controls.TakeUse()) { Stop(p, "stepped off"); return; }
            Follow(p);
        }

        private static string CanStart(Player p)
        {
            if (p.IsDead() || p.IsAttached() || p.IsTeleporting() || p.IsDebugFlying() || p.InPlaceMode() || p.IsStaggering()) return "busy";
            if (InSwimDepth != null && InSwimDepth(p)) return "skateboards_no_water";
            if (p.IsEncumbered()) return "skateboards_too_heavy";
            return null;
        }

        public static void Start(Player p)
        {
            string no = CanStart(p);
            if (no != null) { if (no != "busy") p.Message(MessageHud.MessageType.Center, T(no)); return; }
            Rigidbody body = Body(p);
            Vector3 v = body != null ? body.linearVelocity : Vector3.zero;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            Vector3 look = LookYaw(p) * Vector3.forward;
            look.y = 0f;
            // a running start keeps its speed; from a standstill, a push off where you look
            _dir = flat.magnitude > 1.5f ? flat.normalized : (look.sqrMagnitude > 0.01f ? look.normalized : p.transform.forward);
            _speed = Mathf.Max(flat.magnitude, 2.5f);
            _n = Vector3.up;
            Fakie = false;
            _lastPos = p.transform.position;
            _onFor = 0f;
            _chargeFrom = -1f;
            Spin = AutoSpin = 0f;
            Controls.ForgetPresses();
            Tricks.Clear();
            if (p.IsOnGround()) State = Ride.Roll;
            else { State = Ride.Roll; ToAir(p, v, Vector3.up, ollie: false); }
            SetCrouch?.Invoke(p, new object[] { false });   // (out of a sneak)
            TopSpeedSeen = MaxHeightSeen = 0f;
            Note("on");
            Look.Hint(p, "skateboards_hint_start");
            Sounds.Play(Sfx.Land, p.transform.position, 0.6f);
        }

        /// <summary>Off the board: the game moves the player again, the board goes back in the hand.</summary>
        public static void Stop(Player p, string why, bool quiet = false)
        {
            if (!Active) return;
            if (State != Ride.Bail && !quiet && Tricks.HasCombo) Tricks.Bank(p);
            State = Ride.Off;
            _chargeFrom = -1f;
            Tricks.Clear();
            Note("off: " + why);
            if (p != null && !quiet && Body(p) != null)
            {
                // a gentle step off: what's left of the speed goes into the body, the game slows it as it would a run
                Rigidbody body = Body(p);
                Vector3 v = body.linearVelocity;
                body.linearVelocity = new Vector3(v.x * 0.5f, v.y, v.z * 0.5f);
                CurrentVel(p) = new Vector3(v.x * 0.5f, 0f, v.z * 0.5f);   // (the game's walking picks up from there, not from where it left off)
            }
        }

        // ---- every physics step ----

        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class Character_UpdateMotion
        {
            private static void Postfix(Character __instance, float dt)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                Player p = (Player)__instance;
                if (Active)
                {
                    try { Tick(p, dt); }
                    catch (Exception e) { Plugin.Log.LogError("Skateboards step: " + e); Stop(p, "error", quiet: true); }
                }
                try { Share.Publish(p); }   // (off too: the others see you step off)
                catch (Exception e) { Plugin.Log.LogError("Skateboards share: " + e); }
            }
        }

        // The game's walking step ends with a velocity change queued on the body (AddForce, VelocityChange) toward the speed it wants for
        // the keys held, none: it lands in the physics step after the board's velocity is set, and took most of it away (for 0.2 s after
        // leaving the ground the game still counts the body as on it, and kept an ollie's rise to 3 m/s). On the board (not bailing) the
        // board does all of the moving and turning, so the game's step doesn't run. Other mods' prefixes on it still run.
        [HarmonyPatch(typeof(Character), "UpdateWalking")]
        private static class Character_UpdateWalking
        {
            private static bool Prefix(Character __instance) =>
                !(State != Ride.Off && State != Ride.Bail && ReferenceEquals(__instance, Player.m_localPlayer));
        }

        // A new body (a respawn, another character, another world): off the board.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                State = Ride.Off;
                Tricks.Clear();
                Share.Forget();
            }
        }

        private static void Tick(Player p, float dt)
        {
            Rigidbody body = Body(p);
            if (body == null || dt <= 0f) return;
            if (p.IsDead() || p.IsAttached() || p.IsTeleporting() || p.IsDebugFlying() || p.InPlaceMode()) { Stop(p, "busy", quiet: true); return; }
            if (InSwimDepth != null && InSwimDepth(p)) { Stop(p, "water"); return; }
            if (!Board.InHand(p)) { Stop(p, "board put away"); return; }

            Vector3 pos = p.transform.position;
            Vector3 measured = (pos - _lastPos) / dt;
            if (measured.sqrMagnitude > 3600f) measured = Vector3.ClampMagnitude(measured, 60f);   // (a teleport, or the physics pushing us out of something)

            // the wheels roll: no friction from the capsule on what it touches (the game sets it again at the start of every step)
            CapsuleCollider col = Capsule(p);
            if (col != null && State != Ride.Bail)
            {
                PhysicsMaterial mat = col.material;
                mat.staticFriction = mat.dynamicFriction = 0f;
                mat.frictionCombine = PhysicsMaterialCombine.Minimum;
            }

            switch (State)
            {
                case Ride.Roll: Roll(p, body, dt, measured, manual: false); break;
                case Ride.Manual: Roll(p, body, dt, measured, manual: true); break;
                case Ride.Air: Air(p, body, dt, measured); break;
                case Ride.Grind: Grind(p, body, dt); break;
                case Ride.Bail: Bailing(p, dt); break;
            }
            if (State != Ride.Off) Animate(p);   // (in a bail too: Look.cs draws the fall over the standing pose)
            Tricks.Step(p, dt);
            _lastPos = p.transform.position;
            float flatSpeed = new Vector3(measured.x, 0f, measured.z).magnitude;
            if (State != Ride.Bail) TopSpeedSeen = Mathf.Max(TopSpeedSeen, Mathf.Max(flatSpeed, _speed));
        }

        /// <summary>Set the body's velocity for this step, with the gravity this step will take away added back.</summary>
        private static void Drive(Rigidbody body, Vector3 v, float dt) => body.linearVelocity = v - Physics.gravity * dt;

        private static void Face(Player p, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.04f) return;
            p.transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        // The game's walking step has set the walking animation for a body standing still: standing it is, with the feet down (Look.cs
        // poses the stance over it). Shared with the other players by the game, so they see the player standing, not falling.
        private static void Animate(Player p)
        {
            ZSyncAnimation anim = Anim(p);
            if (anim == null) return;
            anim.SetBool(OnGround, true);
            anim.SetBool(Falling, false);
            anim.SetFloat(ForwardSpeed, 0f);
            anim.SetFloat(SidewaySpeed, 0f);
            anim.SetBool(Crouching, false);
        }

        // ---- the ground under the board ----

        /// <summary>
        /// The surface under the capsule's lower sphere, looked for along "down" (the last surface's normal on a ramp). The gap is how
        /// far the capsule's bottom is above it (0: touching). A sphere cast, so a seam between two pieces doesn't fall through.
        /// </summary>
        public static bool Probe(Player p, Vector3 down, float reach, out RaycastHit hit, out float gap)
        {
            CapsuleCollider col = Capsule(p);
            float r = col != null ? col.radius * Mathf.Abs(col.transform.lossyScale.x) : 0.4f;
            Vector3 c = Bottom(p, col, r);
            const float probe = 0.2f;
            gap = 99f;
            if (!Physics.SphereCast(c, probe, down, out hit, r - probe + reach, Mask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider != null && hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody == Body(p)) return false;
            gap = hit.distance - (r - probe);
            return true;
        }

        /// <summary>The middle of the capsule's lower sphere (its radius above the feet).</summary>
        public static Vector3 Bottom(Player p, CapsuleCollider col, float r)
        {
            if (col == null) return p.transform.position + Vector3.up * r;
            Vector3 local = col.center + Vector3.down * Mathf.Max(0f, col.height * 0.5f - col.radius);
            return col.transform.TransformPoint(local);
        }

        public static float Radius(Character c)
        {
            CapsuleCollider col = c != null ? Capsule(c) : null;
            return col != null ? col.radius * Mathf.Abs(col.transform.lossyScale.x) : 0.4f;
        }

        /// <summary>How much a surface slows the wheels (metres per second, each second): built floors roll best, then paths and stone.</summary>
        private static float Rolling(Collider c)
        {
            if (c == null) return 0.8f;
            if (c.GetComponentInParent<Piece>() != null) return 0.15f;
            Heightmap hm = c.GetComponent<Heightmap>();
            if (hm != null)
            {
                Vector3 at = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : c.transform.position;
                try { if (hm.IsCleared(at)) return 0.35f; } catch (Exception) { }   // (paths, paved and dug ground)
                return 0.9f;   // grass and earth
            }
            return 0.3f;   // rock, the game's own stone
        }

        // ---- rolling (and the manual) ----

        private static void Roll(Player p, Rigidbody body, float dt, Vector3 measured, bool manual)
        {
            bool found = Probe(p, -_n, 0.6f, out RaycastHit hit, out float gap);
            if ((!found || gap > 0.3f) && _n.y < 0.97f) found = Probe(p, Vector3.down, 0.6f, out hit, out gap);
            Vector3 tangentV = _dir * _speed;
            if (!found || gap > 0.3f || hit.normal.y < -0.3f)
            {
                // off the end of a ramp, a ledge or over a hill: in the air with the speed we had (and its rise)
                ToAir(p, tangentV, _n, ollie: false);
                return;
            }
            Vector3 n = hit.normal;
            _ground = hit.collider;
            _n = Vector3.Slerp(_n, n, 1f - Mathf.Exp(-30f * dt)).normalized;
            _dir = Vector3.ProjectOnPlane(_dir, n);
            if (_dir.sqrMagnitude < 1e-4f) _dir = Vector3.ProjectOnPlane(p.transform.forward, n);
            _dir.Normalize();
            _onFor += dt;

            // what really happened last step: run into something (a wall, a tree, a step too high), the speed is what's left of it
            float real = Vector3.Dot(measured, _dir);
            if (_onFor > 0.15f && _speed > 2f && real < _speed * 0.45f)
            {
                Note($"blocked at {_speed:0.0} m/s");
                _speed = Mathf.Max(0f, real);
            }

            Vector3 g = Physics.gravity;
            _speed += Vector3.Dot(g, _dir) * dt;
            float skill = Skill.Factor(p);
            bool pushing = false;
            if (!manual)
            {
                if (Controls.Move.z > 0.3f && n.y > 0.7f)
                {
                    float top = (Controls.Run ? Plugin.SprintSpeed.Value : Plugin.PushSpeed.Value) * (1f + Plugin.PushBonusAt100.Value / 100f * skill) * (Tricks.SpecialOn ? 1.15f : 1f);
                    float cost = Plugin.PushStamina.Value * (Controls.Run ? 2.5f : 1f) * dt;
                    if (_speed < top)
                    {
                        if (cost <= 0f || p.HaveStamina(cost))
                        {
                            if (cost > 0f) p.UseStamina(cost);
                            _speed = Mathf.MoveTowards(_speed, top, Plugin.PushAccel.Value * (Controls.Run ? 1.3f : 1f) * dt);
                            pushing = true;
                        }
                        else if (Time.time - _lastPushTell > 2f && Hud.instance != null) { Hud.instance.StaminaBarEmptyFlash(); _lastPushTell = Time.time; }
                    }
                }
                else if (Controls.Move.z < -0.3f) _speed = Mathf.MoveTowards(_speed, 0f, Plugin.Brake.Value * dt);
            }
            Pushing = pushing;
            // the wheels and the air (and a manual slowly losing speed)
            float friction = Rolling(_ground) + 0.0025f * _speed * _speed + (manual ? 0.6f : 0f);
            _speed = Mathf.MoveTowards(_speed, 0f, friction * dt);
            if (_speed < 0f)
            {
                // rolling back down: now it's the tail first
                _speed = -_speed;
                _dir = -_dir;
                Fakie = !Fakie;
                Note("fakie " + Fakie);
            }
            _speed = Mathf.Min(_speed, Plugin.TopSpeed.Value);
            // standing still on a gentle slope stays still (the wheels' own friction), instead of creeping away
            if (_speed < 0.4f && !pushing && Mathf.Abs(Vector3.Dot(g, _dir)) < 2.5f) _speed = 0f;

            // carving
            float steer = Controls.Move.x;
            if (Mathf.Abs(steer) > 0.1f)
            {
                float rate = Plugin.TurnRate.Value;
                if (_speed > 1f) rate = Mathf.Min(rate, 16f / _speed * Mathf.Rad2Deg);   // (a grip of about 1.6 g: wider circles when fast)
                if (manual) rate *= 0.6f;
                _dir = Quaternion.AngleAxis(steer * rate * dt, n) * _dir;
            }
            Lean = Mathf.MoveTowards(Lean, steer * Mathf.Clamp01(_speed / 6f), dt * 4f);

            Vector3 v = _dir * _speed;
            v -= n * (gap > 0.01f ? Mathf.Min(gap / dt, 4f) : 0.3f);   // stays on the surface over little bumps (a launch is a gap the pull can't close)
            Drive(body, v, dt);
            Face(p, _dir);
            SafeFall(p);

            // the ollie: hold Jump to wind up (the stance crouches), let go to pop; a quick tap pops at once
            bool jumpPressed = Controls.TakeJump();
            if (Controls.JumpHeld && _chargeFrom < 0f) _chargeFrom = Time.time;
            if (_chargeFrom >= 0f && !Controls.JumpHeld)
            {
                float charge = Charge;
                _chargeFrom = -1f;
                Ollie(p, v - n * Vector3.Dot(v, n), n, charge);
                return;
            }
            if (jumpPressed && _chargeFrom < 0f) { Ollie(p, v - n * Vector3.Dot(v, n), n, 0f); return; }

            // a revert: Crouch just after landing on a ramp (or out of a vert air) pivots the board and keeps the combo going
            if (Controls.TakeCrouch() && !manual && _landedRamp && Time.time - _landedAt < 0.4f && Tricks.HasCombo)
            {
                Fakie = !Fakie;
                _landedRamp = false;
                _onFor = 0f;
                Tricks.Revert();
                Sounds.Play(Sfx.Land, p.transform.position, 0.5f);
                Look.Hint(p, "skateboards_hint_revert");
            }

            if (manual)
            {
                if (!Controls.CrouchHeld || _speed < 0.5f) { State = Ride.Roll; Tricks.EndManual(); }
                else
                {
                    Wobble(p, dt, Controls.Move.z);
                    if (Mathf.Abs(Balance) >= 1f) { Bail(p, "skateboards_bail_manual", v); return; }
                }
            }
            else if (Controls.CrouchHeld && _speed > 1.5f && _chargeFrom < 0f)
            {
                State = Ride.Manual;
                StartBalance();
                Tricks.StartManual(nose: Controls.Move.z > 0.3f);
                Look.Hint(p, "skateboards_hint_manual");
            }
            else if (Tricks.HasCombo && _onFor > 0.75f) Tricks.Bank(p);   // rolled away clean
        }

        private static void Ollie(Player p, Vector3 along, Vector3 n, float charge)
        {
            float speed = Plugin.OllieSpeed.Value * (1f + Plugin.OllieCharge.Value / 100f * charge) * (1f + 0.2f * Skill.Factor(p)) * (Tricks.SpecialOn ? 1.1f : 1f);
            Vector3 up = (n + Vector3.up).normalized;   // (off a ramp, partly out of it)
            Note($"ollie {charge:0.00}");
            Sounds.Play(Sfx.Pop, p.transform.position, 0.6f + 0.4f * charge);
            ToAir(p, along + up * speed, n, ollie: true);
            Look.Hint(p, "skateboards_hint_air");
        }

        // ---- the air ----

        private static void ToAir(Player p, Vector3 v, Vector3 n, bool ollie)
        {
            Vector3 nose = Fakie ? -_dir : _dir;
            if (State == Ride.Grind)
            {
                Vector3 along = _rail.Dir * _railSign;
                nose = Fakie ? -along : along;
                n = Vector3.up;
            }
            _takeoffN = n;
            _takeoffGround = State == Ride.Grind ? null : _ground;
            Pushing = false;
            _airYaw = Yaw(Look.Level(n, nose));
            _vert = n.y < 0.4f && v.y > 2f;
            if (_vert)
            {
                // straight up out of the ramp: nothing carries you over its lip or away from it, so you come back down onto it
                Vector3 outOf = new Vector3(n.x, 0f, n.z).normalized;
                v -= outOf * Vector3.Dot(v, outOf);
                _autoSign = Controls.Move.x < -0.1f ? -1 : 1;
                Note("vert air");
            }
            Spin = AutoSpin = 0f;
            _airFor = 0f;
            _peakY = _takeoffY = p.transform.position.y;
            State = Ride.Air;
            Tricks.TookOff(ollie);
            Body(p).linearVelocity = v - Physics.gravity * Time.fixedDeltaTime;
        }

        private static void Air(Player p, Rigidbody body, float dt, Vector3 measured)
        {
            _airFor += dt;
            Vector3 pos = p.transform.position;
            _peakY = Mathf.Max(_peakY, pos.y);
            MaxHeightSeen = Mathf.Max(MaxHeightSeen, _peakY - _takeoffY);
            Vector3 v = measured;   // the body keeps what it had, gravity adds the fall
            Controls.TakeJump();    // (a jump pressed in the air does nothing, and isn't kept for an ollie on landing)

            // spins (A and D), and a vert air's own half turn
            float skill = Skill.Factor(p);
            float rate = Mathf.Lerp(Plugin.SpinSpeed.Value, Plugin.SpinSpeedAt100.Value, skill);
            if (Mathf.Abs(Controls.Move.x) > 0.1f) Spin += Controls.Move.x * rate * dt;
            if (_vert) AutoSpin = Mathf.MoveTowards(AutoSpin, 180f * _autoSign, 300f * dt);

            // landing: on what is under the body, or (coming down a vert air) on the ramp beside it
            bool found = Probe(p, Vector3.down, 0.3f + Mathf.Max(0f, -v.y) * dt, out RaycastHit hit, out float gap);
            if (_vert && v.y < 0f)
            {
                if (Probe(p, -new Vector3(_takeoffN.x, 0f, _takeoffN.z).normalized, 0.35f, out RaycastHit side, out float sideGap) && side.normal.y < 0.6f && (!found || sideGap < gap))
                { hit = side; gap = sideGap; found = true; }
            }
            if (found && hit.normal.y > -0.3f && gap < 0.08f + Mathf.Max(0f, -Vector3.Dot(v, hit.normal)) * dt && Vector3.Dot(v, hit.normal) <= 0.5f && _airFor > 0.08f)
            {
                Land(p, body, hit, v, dt);
                return;
            }

            // a rail or ledge under the board, coming down onto it (or level with it)
            if (v.y < 2f && _airFor > 0.05f && Rails.Find(p.transform.position, v, Time.time - _leftRailAt < 0.35f ? _leftRail : null, out Rails.Rail rail, out float u))
            {
                StartGrind(p, body, rail, u, v);
                return;
            }

            body.linearVelocity = v;   // (gravity this step makes the fall)
            SafeFall(p);
            if (_airFor > 0.4f) Look.Hint(p, "skateboards_hint_tricks");
        }

        private static void Land(Player p, Rigidbody body, RaycastHit hit, Vector3 v, float dt)
        {
            Vector3 n = hit.normal;
            Vector3 nose = Look.NoseOnSurface(n, NoseYaw);
            Vector3 along = Vector3.ProjectOnPlane(v, n);
            float drop = _peakY - p.transform.position.y;
            float turned = Spin;
            string bail = null;
            float off = 0f;   // how far the board points off the way it goes (nose or tail first)
            if (Tricks.Flipping) bail = "skateboards_bail_flip";
            if (along.magnitude > 1.5f)
            {
                float angle = Vector3.Angle(nose, along);
                off = Mathf.Min(angle, 180f - angle);
                if (angle <= Plugin.LandAngle.Value) Fakie = false;
                else if (angle >= 180f - Plugin.LandAngle.Value) Fakie = true;
                else bail = bail ?? "skateboards_bail_sideways";
                _dir = along.normalized;
                _speed = along.magnitude;
            }
            else
            {
                _dir = nose;
                _speed = along.magnitude;
            }
            _n = n;
            _ground = hit.collider;
            Note($"landed: turned {turned:0} (vert {AutoSpin:0}), fakie {Fakie}, drop {drop:0.0}, air {_airFor:0.00}s" + (bail != null ? ", bail " + bail : ""));
            if (bail != null) { Bail(p, bail, v); return; }
            State = Ride.Roll;
            _onFor = 0f;
            _landedAt = Time.time;
            _landedRamp = _vert || n.y < 0.9f;
            Tricks.Landed(p, turned, _airFor, _vert, off, Plugin.LandAngle.Value);
            // from one ramp to another
            if (_takeoffN.y < 0.8f && n.y < 0.95f && _takeoffGround != null && hit.collider != null && hit.collider != _takeoffGround && _airFor > 0.4f
                && hit.collider.GetComponentInParent<Piece>() != _takeoffGround.GetComponentInParent<Piece>())
                Tricks.Gap("transfer", 300f);
            Sounds.Play(Sfx.Land, p.transform.position, Mathf.Clamp01(0.4f + drop * 0.15f));
            Spin = AutoSpin = 0f;
            _vert = false;
            Drive(body, _dir * _speed - n * 0.3f, dt);
            Face(p, _dir);
        }

        private static float _takeoffY;

        /// <summary>
        /// Falls on the board hurt only beyond the safe drop: the game measures a fall from the highest point in the air
        /// (m_maxAirAltitude) when the body touches down (Character.UpdateGroundContact, before the step this runs after), so that
        /// point is kept no higher than the safe drop above where the body is now.
        /// </summary>
        private static void SafeFall(Player p)
        {
            float y = p.transform.position.y;
            float drop = Mathf.Max(0f, _peakY - y);
            float safe = Plugin.SafeDrop.Value;
            float counted = drop > safe ? drop - safe + 4f : 0f;   // (the game's falls hurt from 4 m: the rest hurts as it would)
            if (State != Ride.Air) { _peakY = y; counted = 0f; }
            MaxAirAltitude(p) = Mathf.Min(MaxAirAltitude(p), y + counted);
        }

        // ---- grinding ----

        private static void StartGrind(Player p, Rigidbody body, Rails.Rail rail, float u, Vector3 v)
        {
            _rail = rail;
            _railU = u;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            _railSign = Vector3.Dot(flat, rail.Dir) >= 0f ? 1f : -1f;
            Vector3 along = rail.Dir * _railSign;
            _speed = Mathf.Max(Vector3.Dot(v, along), 2f) + 1f;   // (landing a grind speeds you up)
            bool railToRail = _leftRail != null && rail.Collider != _leftRail && Time.time - _leftRailAt < 1.5f;
            _sameRail = rail.Collider == _lastGrindRail;
            _lastGrindRail = rail.Collider;
            _grindDist = 0f;
            _longGiven = false;
            // the board's nose: which end leads, kept from the air (a 180 onto a rail grinds it fakie)
            Vector3 nose = Look.NoseOnSurface(Vector3.up, NoseYaw);
            Fakie = Vector3.Dot(nose, along) < 0f;
            State = Ride.Grind;
            StartBalance();
            // on it: the feet on the rail's top
            Vector3 at = rail.Point(u);
            p.transform.position = at;
            body.position = at;
            Drive(body, along * _speed, Time.fixedDeltaTime);
            Face(p, along);
            Tricks.LandedOnRail(p, Spin, _airFor);
            Tricks.StartGrind(Tricks.PickGrind(Controls.Move), rail.Ledge);
            if (railToRail) Tricks.Gap("rail2rail", 150f);
            Spin = AutoSpin = 0f;
            _vert = false;
            Note($"grind {(rail.Ledge ? "ledge" : "rail")} {rail.Length:0.0} m at {_speed:0.0} m/s");
            Sounds.Play(Sfx.Land, at, 0.7f);
            Look.Hint(p, "skateboards_hint_grind");
        }

        private static void Grind(Player p, Rigidbody body, float dt)
        {
            if (_rail.Collider == null) { LeaveRail(p, body, 0f, "gone"); return; }
            Vector3 along = _rail.Dir * _railSign;
            _speed += Vector3.Dot(Physics.gravity, along) * dt - 1.0f * dt;
            if (_speed < 0.3f)
            {
                if (Vector3.Dot(Physics.gravity, along) < -0.5f) { _railSign = -_railSign; _speed = 0.3f; Fakie = !Fakie; along = -along; }   // (back down a sloped rail)
                else { LeaveRail(p, body, 0.5f, "stopped"); return; }
            }
            _speed = Mathf.Min(_speed, Plugin.TopSpeed.Value);
            _railU += _railSign * _speed * dt;
            _grindDist += _speed * dt;
            if (_grindDist >= 8f && !_longGiven) { _longGiven = true; Tricks.Gap("longgrind", 200f); }
            if (_railU < 0f || _railU > _rail.Length) { LeaveRail(p, body, 0.8f, "end"); return; }

            Vector3 target = _rail.Point(_railU);
            Vector3 fix = Vector3.ClampMagnitude((target - p.transform.position) * 12f, 3f);
            Drive(body, along * _speed + fix, dt);
            Face(p, along);
            SafeFall(p);

            Wobble(p, dt, Controls.Move.x);
            if (Mathf.Abs(Balance) >= 1f)
            {
                Vector3 side = Vector3.Cross(Vector3.up, along) * Mathf.Sign(Balance);
                Bail(p, "skateboards_bail_rail", along * _speed * 0.6f + side * 2.5f);
                return;
            }
            if (Controls.TakeJump() || (_chargeFrom >= 0f && !Controls.JumpHeld))
            {
                _chargeFrom = -1f;
                Tricks.EndGrind();
                _leftRail = _rail.Collider;
                _leftRailAt = Time.time;
                Note("ollie off the rail");
                Sounds.Play(Sfx.Pop, p.transform.position, 0.8f);
                ToAir(p, along * _speed + Vector3.up * Plugin.OllieSpeed.Value * 0.9f, Vector3.up, ollie: true);
            }
        }

        private static void LeaveRail(Player p, Rigidbody body, float up, string why)
        {
            Tricks.EndGrind();
            _leftRail = _rail.Collider;
            _leftRailAt = Time.time;
            Note("off the rail: " + why);
            Vector3 along = _rail.Dir * _railSign;
            ToAir(p, along * _speed + Vector3.up * up, Vector3.up, ollie: false);
        }

        // ---- balance (grinds and manuals) ----

        private static void StartBalance()
        {
            Balance = UnityEngine.Random.Range(-0.08f, 0.08f);
            _balanceFor = 0f;
            _noiseSeed = UnityEngine.Random.Range(0f, 1000f);
        }

        /// <summary>The needle drifts, more the longer you hold it, and tips further the further it is off; the keys push it back.</summary>
        private static void Wobble(Player p, float dt, float keys)
        {
            _balanceFor += dt;
            float wobble = Mathf.Lerp(Plugin.Wobble.Value, Plugin.WobbleAt100.Value, Skill.Factor(p)) * Tricks.BalanceHarder(State == Ride.Grind && _sameRail);
            if (State == Ride.Grind && Tricks.GrindNow == Tricks.Grind.Bifrost) wobble *= 0.5f;
            float drift = (Mathf.PerlinNoise(_balanceFor * 0.8f, _noiseSeed) - 0.5f) * 2.2f * (1f + _balanceFor * 0.12f);
            Balance += (drift * 0.55f + Balance * 1.4f) * wobble * dt;
            Balance += keys * 2.2f * dt;
            Balance = Mathf.Clamp(Balance, -1.05f, 1.05f);
        }

        // ---- bailing ----

        private static void Bail(Player p, string why, Vector3 v)
        {
            Note("bail: " + why);
            LastBail = why;
            Bails++;
            BailVelocity = v;
            Tricks.Lose(p, T(why));
            State = Ride.Bail;
            _bailUntil = Time.time + 1.6f;
            _chargeFrom = -1f;
            Spin = AutoSpin = 0f;
            _vert = false;
            Rigidbody body = Body(p);
            if (body != null) body.linearVelocity = new Vector3(v.x * 0.5f, Mathf.Min(v.y, 0f), v.z * 0.5f);
            CurrentVel(p) = new Vector3(v.x * 0.3f, 0f, v.z * 0.3f);   // (the game's walking takes over, from about there)
            Sounds.Play(Sfx.Bail, p.transform.position, 1f);
            Look.Bailed(p, v);
        }

        private static void Bailing(Player p, float dt)
        {
            SafeFall(p);
            if (Time.time < _bailUntil || !p.IsOnGround()) return;
            // back on the board, standing
            Rigidbody body = Body(p);
            Vector3 v = body.linearVelocity;
            _dir = new Vector3(v.x, 0f, v.z).sqrMagnitude > 0.5f ? new Vector3(v.x, 0f, v.z).normalized : p.transform.forward;
            _speed = 0f;
            _n = Vector3.up;
            Fakie = false;
            _onFor = 0f;
            State = Ride.Roll;
            Controls.ForgetPresses();
            Note("back on");
            Sounds.Play(Sfx.Land, p.transform.position, 0.5f);
        }

        // ---- the camera ----

        private static Quaternion _setYaw;
        private static float _mouseAt;

        /// <summary>The camera swings round behind the way you roll, a moment after the mouse last moved it (CameraFollow).</summary>
        private static void Follow(Player p)
        {
            Quaternion now = LookYaw(p);
            if (Quaternion.Angle(now, _setYaw) > 0.05f) _mouseAt = Time.time;
            if (!Plugin.CameraFollow.Value || Time.time - _mouseAt < 1f || (State != Ride.Roll && State != Ride.Manual && State != Ride.Grind)) { _setYaw = now; return; }
            Vector3 dir = State == Ride.Grind ? _rail.Dir * _railSign : _dir;
            dir.y = 0f;
            float speed = _speed;
            if (dir.sqrMagnitude < 0.04f || speed < 2f) { _setYaw = now; return; }
            Quaternion want = Quaternion.LookRotation(dir.normalized);
            Quaternion next = Quaternion.RotateTowards(now, want, 70f * Mathf.Clamp01(speed / 8f) * Time.deltaTime);
            LookYaw(p) = next;
            _setYaw = next;
        }

        // ---- the log of what happened, for tests ----

        public static int EventCount;

        public static void Note(string what)
        {
            EventCount++;
            Events.Add($"{Time.time:0.00} {what}");
            if (Events.Count > 60) Events.RemoveAt(0);
        }
    }
}
