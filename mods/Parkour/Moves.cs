using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Parkour
{
    internal enum Move { None, WallRun, Climb, Hang, PullUp, Slide }

    /// <summary>
    /// The four moves, run on the player's own game each physics step, right after the game's own movement step (Character.UpdateMotion):
    /// the game has moved the player for the keys pressed, and a move then sets the body's velocity for the wall. Nothing here touches the
    /// body's gravity or kinematic flag (other climbing mods save and restore them, so a changed flag could stick): gravity is cancelled by
    /// adding the velocity it would take away this step.
    ///
    ///   Wall run   sprinting, jump at a wall at an angle: run along it, sinking slowly, for a while.
    ///   Wall jump  jump on a wall, or just after touching one: kick off it, up and away. Never the same wall twice in a row, and only so
    ///              many in a row (more with the skill) before touching the ground.
    ///   Wall climb sprinting straight at a wall, jump: run up it, then grab the top or drop off.
    ///   Ledge      in the air with forward held, an edge in reach: catch it (Hang), move along it with left or right (a shimmy), then pull
    ///              up onto it (PullUp) or let go.
    ///   Slide      on the ground, sprinting, crouch: a burst of speed, sliding low for as long as crouch is held (and the bar lasts).
    /// The landing roll is in Roll.cs.
    /// </summary>
    internal static class Moves
    {
        public static Move State;
        private static float _hangFor = 3f;   // how long this hang may last (the blue bar)
        private static bool _forwardFresh;     // forward let go since the grab (held from the climb, it mustn't pull up at once)
        private static float _since;                 // seconds in this move
        private static Walls.Wall _wall;             // the wall a run or climb is on
        private static Walls.Ledge _ledge;
        private static Vector3 _runDir, _pullFrom;
        private static float _runSpeed, _runFor, _vy, _climbTop, _climbHeight, _missing, _drained, _nextRaise, _lastFlash;
        private static bool _canPullUp;
        private static float _nextGrabSound;
        // the slide: which way, how fast, for how long at most, and when the last one ended
        private static Vector3 _slideDir;
        private static float _slideSpeed, _slideTop, _slideFor, _slideAt = -10f, _slideEndAt = -10f;

        // the wall last touched in the air (for kicks just after), and the wall last kicked (not the same one twice in a row)
        private static Walls.Wall _touch;
        private static float _touchAt = -10f;
        private static bool _kicked;
        private static int _kicks;                   // wall jumps since the ground
        private static Vector3 _kickedNormal, _kickedPoint;
        private static float _kickAt = -10f, _letGoAt = -10f, _climbEndAt = -10f, _otherClimbAt = -10f;

        // a jump or crouch a move took from the game (Controls), waiting for this step
        private static float _jumpAt = -10f, _crouchAt = -10f;

        private static readonly HashSet<string> Hinted = new HashSet<string>();

        public const float KickGrace = 0.25f;        // seconds after touching a wall that a jump still kicks off it

        internal static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        internal static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Character, bool> WallRunning = AccessTools.FieldRefAccess<Character, bool>("m_wallRunning");
        private static readonly AccessTools.FieldRef<Character, Vector3> CurrentVel = AccessTools.FieldRefAccess<Character, Vector3>("m_currentVel");
        internal static readonly AccessTools.FieldRef<Character, Vector3> MoveDir = AccessTools.FieldRefAccess<Character, Vector3>("m_moveDir");
        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly Func<Character, float> RunSpeedFactor = AccessTools.MethodDelegate<Func<Character, float>>(AccessTools.Method(typeof(Character), "GetRunSpeedFactor"));
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        // ---- what tests and the status command read ----

        internal sealed class Record
        {
            public string Move, Ended = "";
            public float Started, Seconds, Rise, Distance, Stamina;
            public Vector3 From, To;
        }
        public static readonly List<Record> Done = new List<Record>();

        /// <summary>The wall a run or climb is on: out of it, toward the player (for the camera's lean).</summary>
        public static Vector3 WallNormal => _wall.Normal;
        private static Record _record;
        private static float _staminaAtStart;
        public static string LastRefusal = "";

        public static void Reset()
        {
            Player p = Player.m_localPlayer;
            if (p != null && State != Move.None) WallRunning(p) = false;
            Fresh();
            Done.Clear();
            Hinted.Clear();
        }

        /// <summary>No move going on, and nothing kept of the last one (its wall, its edge, the hands it put away, the kicks).</summary>
        private static void Fresh()
        {
            State = Move.None;
            _since = _missing = 0f;
            _wall = _touch = default;
            _ledge = default;
            _sheathed = _kicked = _forwardFresh = false;
            _offLedgeAt = -10f;
            _putAwayAgain = 0;
            _touchAt = _kickAt = _letGoAt = _climbEndAt = _otherClimbAt = _jumpAt = _crouchAt = _slideAt = _slideEndAt = -10f;
            _kicks = 0;
            _record = null;
            LastRefusal = "";
        }

        // A new body (a respawn, another character, another world): nothing of the last one's moves carries over, or the new one could be
        // pulled to the old one's ledge. (Unloading the mod resets too, but logging out doesn't unload it.)
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                Fresh();
                Roll.Reset();
                Share.Forget();
                Compat.ForgetDungeons();   // (a new world: look for Quad's Dungeons again, the newest copy if it was reloaded)
                Feel.Reset();
            }
        }

        // ---- the input a move takes from the game (called from Controls, before the game acts on the keys) ----

        public static bool TakesJump(Player p)
        {
            if (!Plugin.Enabled.Value) return false;
            bool nearWall = State == Move.None && !p.IsOnGround() && Time.time - _touchAt <= KickGrace;
            bool ours = State != Move.None || (nearWall && Plugin.WallJump.Value && !SameAsKicked(_touch) && KicksLeft(p) > 0 && !TooFastToCatch(p));
            if (ours && State == Move.None && DungeonSaysNo(p)) ours = false;   // (the game's own jump, then: no wall jump in here)
            if (ours) _jumpAt = Time.time;
            else if (nearWall && TooFastToCatch(p)) LastRefusal = TooFast;
            else if (nearWall && SameAsKicked(_touch)) LastRefusal = "the same wall twice in a row";
            else if (nearWall && KicksLeft(p) <= 0) LastRefusal = "no wall jumps left: touch the ground";
            return ours;
        }

        public static bool TakesCrouch(Player p)
        {
            if (State == Move.Hang) { _crouchAt = Time.time; return true; }   // let go
            if (State == Move.Slide) return true;                              // (already sliding)
            if (p.IsOnGround()) return CanSlide(p);                            // on the ground, sprinting: a slide (else the game's sneak)
            return Roll.TakesCrouch(p);                                        // in the air: a roll on landing (also pressed from a wall)
        }

        private static bool Jumped() { bool j = Time.time - _jumpAt <= 0.2f; if (j) _jumpAt = -10f; return j; }
        private static bool Crouched() { bool c = Time.time - _crouchAt <= 0.2f; if (c) _crouchAt = -10f; return c; }

        // ---- every physics step ----

        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class Character_UpdateMotion
        {
            private static void Postfix(Character __instance, float dt, bool __runOriginal)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                Player p = (Player)__instance;
                try { Tick(p, dt, __runOriginal); }
                catch (Exception e) { Plugin.Log.LogError("Parkour step: " + e); End(p, "error"); }
                // (also after a move that ended in an error: what a hang put away comes back out, and the others see it end)
                try { Hands(p); Share.Publish(p); }
                catch (Exception e) { Plugin.Log.LogError("Parkour hands: " + e); }
            }
        }

        // ---- the hands: weapons away while on a ledge ----

        private static readonly Action<Humanoid, bool, bool> ShowHandItems =
            AccessTools.MethodDelegate<Action<Humanoid, bool, bool>>(AccessTools.Method(typeof(Humanoid), "ShowHandItems"));
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        private static bool _sheathed;   // we put the weapons away for a hang: they come back out when it's over
        private static float _offLedgeAt = -10f;   // when the hands left the edge with the weapons still away (below 0: on it, or nothing away)
        private static int _putAwayAgain;          // weapons drawn and put away again during this hang
        private static bool _toldFight, _toldRegrab;
        private const float DrawAfter = 0.3f;      // off the edge this long before the weapons come back out
        private const int PutAwayAgainMax = 2;     // then whatever keeps drawing them wins (no fight, every step, with another mod)

        /// <summary>
        /// Hanging and pulling up, the hands hold the edge: what they held goes on the back, as the game's own "hide weapons" does (shared with
        /// everyone), and again if a hotbar key draws something meanwhile. Off the ledge a moment, it comes back out: a hang that ends and
        /// starts again at once (a step that refused the move, then caught the edge again) left them drawn and put away every step.
        /// </summary>
        private static void Hands(Player p)
        {
            bool onLedge = State == Move.Hang || State == Move.PullUp;
            if (onLedge)
            {
                if (_offLedgeAt >= 0f && !_toldRegrab)
                {
                    _toldRegrab = true;
                    Plugin.Log.LogInfo($"Parkour: back on a ledge {Time.time - _offLedgeAt:0.00} s after the last move ended ({LastEnded()}): the weapons stay away");
                }
                _offLedgeAt = -10f;
                if (!_sheathed) { if (p.HideHandItems(false, false)) { _sheathed = true; _putAwayAgain = 0; } return; }   // (false: the hands were empty)
                if (RightItem(p) == null && LeftItem(p) == null) return;
                // drawn again on the edge: a hotbar key, or another mod drawing them every step (then they stay out, and the log says so)
                if (_putAwayAgain >= PutAwayAgainMax)
                {
                    if (!_toldFight) { _toldFight = true; Plugin.Log.LogWarning($"Parkour: something keeps drawing {Held(p)} during a hang (another mod?): left in the hands"); }
                    return;
                }
                _putAwayAgain++;
                p.HideHandItems(false, false);
                return;
            }
            if (!_sheathed) { _offLedgeAt = -10f; return; }
            if (_offLedgeAt < 0f) _offLedgeAt = Time.time;
            if (Time.time - _offLedgeAt < DrawAfter && !p.IsDead()) return;
            _sheathed = false;
            _offLedgeAt = -10f;
            if (!p.IsDead()) ShowHandItems?.Invoke(p, false, true);
        }

        private static string Held(Player p)
        {
            ItemDrop.ItemData right = RightItem(p), left = LeftItem(p);
            return right != null && left != null ? right.m_shared.m_name + " and " + left.m_shared.m_name : (right ?? left).m_shared.m_name;
        }

        private static string LastEnded() => Done.Count > 0 ? Done[Done.Count - 1].Move + ": " + Done[Done.Count - 1].Ended : "none";

        /// <summary>On unload: what a hang put away comes back out.</summary>
        public static void GiveHandsBack()
        {
            Player p = Player.m_localPlayer;
            if (_sheathed && p != null && !p.IsDead()) ShowHandItems?.Invoke(p, false, false);
            _sheathed = false;
        }

        // Killed on a ledge: what the hang put away goes back in the hands before the game makes the grave (Player.OnDeath calls
        // CreateTombStone), so it counts as equipped there. Put away, it is "unequipped": deleted on a world whose death penalty deletes
        // unequipped items (Hard), and in the grave instead of kept with DeathKeepEquip. First, before other mods' death prefixes; and
        // again before the grave, for a mod that makes it some other way. (The player is already dead here: EquipItem allows that.)
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class Player_OnDeath
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance) => HandsBackForDeath(__instance);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
        private static class Player_CreateTombStone
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance) => HandsBackForDeath(__instance);
        }

        private static void HandsBackForDeath(Player p)
        {
            if (!_sheathed || !ReferenceEquals(p, Player.m_localPlayer)) return;
            _sheathed = false;
            try { ShowHandItems?.Invoke(p, false, false); }
            catch (Exception e) { Plugin.Log.LogError("Parkour hands at death: " + e); }
        }

        private static void Tick(Player p, float dt, bool gameMoved)
        {
            Compat.Check();
            Rigidbody body = Body(p);
            if (body == null) return;
            string why = Blocked(p, body, gameMoved);
            bool grounded = p.IsOnGround();
            if (grounded) { _kicked = false; _kicks = 0; }   // the ground: any wall may be kicked again, and the count starts again
            Roll.Step(p, body, grounded, why);

            if (State != Move.None && why != null) End(p, why);
            if (why != null) { LastRefusal = why; return; }
            // a move that carried the player into a dungeon with parkour off ends there (a slide may go on: it climbs nothing)
            if (State != Move.None && State != Move.Slide && Compat.DungeonForbids(p)) { End(p, Forbidden); LastRefusal = Forbidden; return; }

            if (!grounded) Touching(p);

            // a kick off the wall (from a run, a climb, or just after touching one)
            if (State == Move.WallRun || State == Move.Climb || (State == Move.None && !grounded))
            {
                if (Time.time - _jumpAt <= 0.2f && State == Move.None && Time.time - _touchAt > KickGrace) _jumpAt = -10f;   // (grace ran out)
                else if (Jumped())
                {
                    Walls.Wall from = State == Move.None ? _touch : _wall;
                    Vector3 carry = State == Move.WallRun ? _runDir * _runSpeed * 0.6f : Vector3.zero;
                    if (Plugin.WallJump.Value) Kick(p, body, from, carry);
                    else if (State != Move.None) End(p, "jumped off");
                }
            }

            switch (State)
            {
                case Move.WallRun: TickRun(p, body, dt); break;
                case Move.Climb: TickClimb(p, body, dt); break;
                case Move.Hang: TickHang(p, body, dt); break;
                case Move.PullUp: TickPullUp(p, body, dt); break;
                case Move.Slide: TickSlide(p, body, dt); break;
            }
            if (State == Move.None && !grounded) TryStart(p, body);
            if (State == Move.None && grounded && Time.time - _slideAt <= 0.2f) { _slideAt = -10f; StartSlide(p, body); }

            if (State != Move.None)
            {
                WallRunning(p) = true;                                    // the game: no stamina back while on a wall, and its lean (Look.cs)
                MaxAirAltitude(p) = p.transform.position.y;               // a fall is measured from where a move left the wall, not from the jump's peak
                if (_record != null) _record.Rise = Mathf.Max(_record.Rise, p.transform.position.y - _record.From.y);
            }
        }

        /// <summary>A pull-up that vaults over a thin top (shared: the others swing the legs over the side).</summary>
        internal static bool Vaulting => State == Move.PullUp && _ledge.Over;

        /// <summary>The edge or wall the move is at, and its facing (shared with the other players: Share.cs).</summary>
        internal static void Shared(out Vector3 edge, out Vector3 normal)
        {
            switch (State)
            {
                case Move.Hang:
                case Move.PullUp: edge = _ledge.Edge; normal = _ledge.Normal; return;
                case Move.WallRun:
                case Move.Climb: edge = _wall.Point; normal = _wall.Normal; return;
                case Move.Slide: edge = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero; normal = -_slideDir; return;
                default: edge = normal = Vector3.zero; return;
            }
        }

        /// <summary>The game takes the player's keys now (not typing, not in a menu).</summary>
        internal static bool TakingInput(Player p) => TakeInput == null || TakeInput(p);

        /// <summary>Why no move may start now, or null (for the status command).</summary>
        public static string WhyNot(Player p)
        {
            Rigidbody body = p != null ? Body(p) : null;
            if (body == null) return "no player";
            return Blocked(p, body, true) ?? (TakeInput != null && !TakeInput(p) ? "typing or in a menu" : null);
        }

        /// <summary>Why no move may run now (null: one may).</summary>
        private static string Blocked(Player p, Rigidbody body, bool gameMoved)
        {
            if (!Plugin.Enabled.Value) return "off in the settings";
            if (!gameMoved) return "another mod is moving the player";
            if (p.IsDead()) return "dead";
            if (p.IsEncumbered()) return "over-encumbered";
            if (p.IsSwimming()) return "swimming";
            if (p.IsAttached() || p.IsRiding()) return "attached or riding";
            if (p.InAttack()) return "attacking";
            if (p.IsBlocking()) return "blocking";
            if (p.InDodge()) return "dodging";
            if (p.IsStaggering() || p.IsKnockedBack()) return "staggered";
            if (p.IsDebugFlying()) return "flying";
            if (p.IsTeleporting() || p.InCutscene()) return "teleporting";
            if (!p.CanMove()) return "can't move";                       // (also while Just Vault vaults)
            if (body.isKinematic || !body.useGravity) return "another mod holds the body";
            int web = Compat.WebMove();
            if (web >= 1 && web <= 3) return "Webslinger is moving the player";
            if (Compat.SmoothClimbing)
            {
                if (Compat.SmoothClimbingBusy(p, NView(p) != null ? NView(p).GetZDO() : null)) _otherClimbAt = Time.time;
                if (Time.time - _otherClimbAt < 1.5f) return "Smooth Climbing is climbing";
            }
            return null;
        }

        /// <summary>Why no move may run now, or null (for the roll).</summary>
        internal static string Why(Player p) => Body(p) != null ? Blocked(p, Body(p), true) : "no player";

        /// <summary>Remember the wall the player brushes in the air: a jump just after kicks off it.</summary>
        private static void Touching(Player p)
        {
            if (!Walls.Around(p, 0.3f, out Walls.Wall w)) return;
            if (_kicked && !SameAsKicked(w)) _kicked = false;   // another wall: the last one may be kicked again later
            _touch = w;
            _touchAt = Time.time;
        }

        private static bool SameAsKicked(Walls.Wall w) =>
            _kicked && Vector3.Dot(w.Normal, _kickedNormal) > 0.7f && Mathf.Abs(Vector3.Dot(w.Point - _kickedPoint, _kickedNormal)) < 1f;

        // ---- starting ----

        private static void TryStart(Player p, Rigidbody body)
        {
            if (TakeInput != null && !TakeInput(p) && Controls.Driving == null) { LastRefusal = "typing or in a menu"; return; }
            if (TooFastToCatch(p)) { LastRefusal = TooFast; return; }   // (no grab, run or climb: the fall goes on to the ground)
            if (Compat.WebMove() == 4) { LastRefusal = "flying off a web (Webslinger's wall crawl takes walls)"; return; }
            Vector3 vel = body.linearVelocity, flat = new Vector3(vel.x, 0f, vel.z);
            Vector3 wish = MoveDir(p);
            Vector3 heading = flat.magnitude > 2f ? flat.normalized : (wish.sqrMagnitude > 0.01f ? wish.normalized : p.transform.forward);
            bool jumpedLately = Controls.JumpPressed(0.7f) || Time.time - _kickAt < 0.7f;

            // ledge: holding forward, an edge in reach in front, from waist to above the head (a low one only at the top of a jump that
            // didn't clear it: one the jump is still carrying the player over is left alone)
            if (LedgeOn && Controls.Forward && Time.time - _letGoAt > 0.5f && vel.y < 6f)
            {
                Vector3 facing = wish.sqrMagnitude > 0.01f ? wish : p.transform.forward;
                if (Walls.FindLedge(p, facing, out Walls.Ledge ledge, 0.6f) && (vel.y < 1.5f || ledge.Height > 1.6f)) { StartHang(p, body, ledge); return; }
            }
            if (!jumpedLately || !p.IsRunning()) return;

            // run up a wall: sprinting at it, within about 20 degrees of straight on
            if (ClimbOn && Controls.Forward && Time.time - _climbEndAt > 0.5f && Walls.Find(p, heading, 0.55f, out Walls.Wall front)
                && Vector3.Dot(heading, -front.Normal) > 0.94f && !SameAsKicked(front))
            {
                StartClimb(p, body, front);
                return;
            }

            // run along a wall: sprinting at it at an angle of about 20 to 70 degrees (a little less is forgiven)
            if (WallRunOn && flat.magnitude > 3f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, heading);
                foreach (Vector3 dir in new[] { right, -right, (right + heading).normalized, (heading - right).normalized })
                {
                    if (!Walls.Find(p, dir, 0.5f, out Walls.Wall side) || SameAsKicked(side)) continue;
                    float into = Vector3.Dot(heading, -side.Normal);                               // 0 along the wall, 1 straight at it
                    float angle = Mathf.Asin(Mathf.Clamp(into, -1f, 1f)) * Mathf.Rad2Deg;          // between the heading and the wall
                    if (angle < 12f || angle > 70f) continue;
                    StartRun(p, body, side, heading, flat.magnitude);
                    return;
                }
            }
        }

        /// <summary>
        /// A move that starts in the air (a ledge grab, a wall run or climb, a kick off a wall just touched) ends a fall without the game's
        /// fall damage: each move step measures the fall again from where the player is. So it may only stop a fall a landing roll takes
        /// without damage: up to the roll's safe height (RollSafeHeight: 8 m at skill 0, 10 m at 100, the server's). The game's falls hurt
        /// from 4 m, 100/16 per metre more, so a move spares at most 25 (37 at skill 100), as a roll does. Normal parkour (a drop off a
        /// wall run, a kick, a ledge onto the next one below) falls far less. From higher the hands and feet can't hold it: the player falls
        /// on and lands with the game's fall damage (a roll still softens it).
        /// </summary>
        internal static bool TooFastToCatch(Player p) => MaxAirAltitude(p) - p.transform.position.y > Roll.SafeHeight(p);
        private const string TooFast = "falling too fast to catch";
        private const string Forbidden = "a dungeon forbids it";

        /// <summary>Inside a dungeon of Quad's Dungeons that has parkour off (its puzzle would be skipped): no move starts, and the player
        /// is told once.</summary>
        private static bool DungeonSaysNo(Player p)
        {
            if (!Compat.DungeonForbids(p)) return false;
            LastRefusal = Forbidden;
            Hint(p, "parkour_dungeon");
            return true;
        }

        private static bool WallRunOn => Plugin.WallRun.Value;
        private static bool ClimbOn => Plugin.WallClimb.Value && !Compat.ClimbOff;
        private static bool LedgeOn => Plugin.LedgeGrab.Value && !Compat.LedgeOff;

        private static void StartRun(Player p, Rigidbody body, Walls.Wall wall, Vector3 heading, float speed)
        {
            if (DungeonSaysNo(p)) return;
            if (!Pay(p, Cost(p, Plugin.WallRunCost.Value, true, heading))) return;
            Begin(p, Move.WallRun, wall);
            _runDir = Vector3.ProjectOnPlane(heading, wall.Normal);
            _runDir.y = 0f;
            _runDir.Normalize();
            float skill = Skill.Factor(p);
            _runSpeed = Mathf.Max(speed, RunSpeed(p));   // the take-off speed, so other mods' speed changes carry over
            _runSpeed *= 1f + Plugin.WallRunSpeedBonus.Value / 100f * skill;
            _runFor = Mathf.Lerp(Plugin.WallRunMin.Value, Plugin.WallRunMax.Value, skill);
            if (p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet)) _runFor *= 1f - Plugin.WetShorter.Value / 100f;
            _vy = body.linearVelocity.y;
            Feel.Touch(p, wall);
            Raise(p, 1f);
            Hint(p, "parkour_hint_wallrun");
        }

        private static void StartClimb(Player p, Rigidbody body, Walls.Wall wall)
        {
            if (DungeonSaysNo(p)) return;
            if (!Pay(p, Cost(p, Plugin.ClimbCost.Value, true, -wall.Normal))) return;
            Begin(p, Move.Climb, wall);
            _climbHeight = Mathf.Lerp(Plugin.ClimbMin.Value, Plugin.ClimbMax.Value, Skill.Factor(p));
            _climbTop = p.transform.position.y + _climbHeight;
            _vy = Mathf.Max(body.linearVelocity.y, 3f);
            Feel.Touch(p, wall);
            Raise(p, 1f);
            Hint(p, "parkour_hint_climb");
        }

        private static void StartHang(Player p, Rigidbody body, Walls.Ledge ledge)
        {
            if (DungeonSaysNo(p)) return;
            Move was = State;
            if (was != Move.None) Finish(p, "caught the edge");
            Begin(p, Move.Hang, new Walls.Wall { Point = ledge.Edge, Normal = ledge.Normal, Collider = ledge.Face });
            Feel.Grab(p, ledge);
            _ledge = ledge;
            _hangFor = Mathf.Lerp(Plugin.HangMin.Value, Plugin.HangMax.Value, Skill.Factor(p));   // (the blue bar, full again)
            _forwardFresh = !Controls.Forward;
            if (ledge.Height < 1.3f) _ledge.Hang.y = Mathf.Max(ledge.Hang.y, p.transform.position.y);   // (a low edge: held where the body is, not dropped to arm's length)
            _canPullUp = Plugin.PullUp.Value;
            Hint(p, _canPullUp ? "parkour_hint_hang" : Compat.JustVault ? "parkour_hint_hang_vault" : "parkour_hint_hang_kick");
            if (Plugin.Shimmy.Value) Hint(p, "parkour_hint_shimmy");
        }

        private static void Begin(Player p, Move move, Walls.Wall wall)
        {
            if (_kicked && !SameAsKicked(wall)) _kicked = false;
            State = move;
            _since = 0f;
            _wall = wall;
            _missing = 0f;
            _drained = 0f;
            _nextRaise = 1f;
            _record = new Record { Move = move.ToString(), Started = Time.time, From = p.transform.position };
            _staminaAtStart = p.GetStamina();
        }

        // ---- the moves, each step ----

        private static void TickRun(Player p, Rigidbody body, float dt)
        {
            _since += dt;
            // still on the wall? (a short gap between two pieces is forgiven)
            if (Walls.Find(p, -_wall.Normal, 0.6f, out Walls.Wall w))
            {
                _wall.Normal = Vector3.Slerp(_wall.Normal, w.Normal, 0.5f).normalized;
                _wall.Point = w.Point; _wall.Gap = w.Gap; _wall.Collider = w.Collider;
                _missing = 0f;
            }
            else if ((_missing += dt) > 0.12f) { End(p, "the wall ended"); return; }

            Vector3 wish = MoveDir(p);
            if (_since >= _runFor) { End(p, "time"); return; }
            if (!Controls.Run || !p.IsRunning()) { End(p, Controls.Run ? "out of stamina" : "let go of sprint"); return; }
            if (Vector3.Dot(wish, _runDir) < 0.1f || Vector3.Dot(wish, _wall.Normal) > 0.5f) { End(p, "turned away"); return; }
            if (p.IsOnGround() && _since > 0.15f) { End(p, "landed"); return; }
            if (Walls.Find(p, _runDir, 0.15f, out _)) { End(p, "something in the way"); return; }
            if (!Drain(p, Cost(p, Plugin.WallRunCostPerSecond.Value, false, _runDir) * dt)) { End(p, "out of stamina"); return; }

            // along the wall (following its curve), held to it, sinking slowly and then faster
            _runDir = Vector3.ProjectOnPlane(_runDir, _wall.Normal);
            _runDir.y = 0f;
            _runDir.Normalize();
            float k = _since / _runFor;
            _vy = Mathf.MoveTowards(_vy, -Mathf.Lerp(0.4f, 4f, k * k), 20f * dt);
            Vector3 v = _runDir * _runSpeed - _wall.Normal * Mathf.Clamp((_wall.Gap - 0.05f) * 6f, -1f, 3f) + Vector3.up * _vy;
            Drive(p, body, v, _runDir, dt);
            CurrentVel(p) = _runDir * _runSpeed;
            Look.Lean(_wall.Normal, 22f);
            Look.Running(p, _runSpeed);
            Feel.OnWall(p, _wall);
            if (_since >= _nextRaise) { _nextRaise += 1f; Raise(p, 0.5f); }
        }

        private static void TickClimb(Player p, Rigidbody body, float dt)
        {
            _since += dt;
            // the top in reach: catch it
            if (LedgeOn && Walls.FindLedge(p, -_wall.Normal, out Walls.Ledge ledge, 1f, 1.75f)) { StartHang(p, body, ledge); return; }
            bool onWall = Walls.Find(p, -_wall.Normal, 0.7f, out Walls.Wall w);
            if (onWall) { _wall.Normal = w.Normal; _wall.Point = w.Point; _wall.Gap = w.Gap; _wall.Collider = w.Collider; }
            if (!onWall) { DropOff(p, body, "the wall ended"); return; }
            if (p.transform.position.y >= _climbTop)
            {
                // as high as it goes: the top if it's in reach, else drop back off
                if (LedgeOn && Walls.FindLedge(p, -_wall.Normal, out ledge)) StartHang(p, body, ledge);
                else DropOff(p, body, "as high as it goes");
                return;
            }
            if (!Controls.Forward) { DropOff(p, body, "let go of forward"); return; }
            if (_since > 2.5f) { DropOff(p, body, "time"); return; }
            if (!p.HaveStamina()) { DropOff(p, body, "out of stamina"); return; }

            // straight up the wall, slowing near the top
            float k = Mathf.Clamp01(1f - (_climbTop - p.transform.position.y) / Mathf.Max(0.5f, Plugin.ClimbMax.Value));
            _vy = Mathf.MoveTowards(_vy, Mathf.Lerp(6f, 2.5f, k), 30f * dt);
            Vector3 v = Vector3.up * _vy - _wall.Normal * Mathf.Clamp((_wall.Gap - 0.05f) * 6f, -1f, 2f);
            Drive(p, body, v, -_wall.Normal, dt);
            CurrentVel(p) = Vector3.zero;
            Look.Lean(_wall.Normal, 12f);   // (leaning back a little, feet on the wall)
            Look.Running(p, 6f);
            Feel.OnWall(p, _wall);
        }

        private static void TickHang(Player p, Rigidbody body, float dt)
        {
            _since += dt;
            // the edge still there? (a piece knocked down, a tree felled)
            if (_since > 0.3f && Mathf.Repeat(_since, 0.3f) < dt)
            {
                if (!Walls.FindLedge(p, -_ledge.Normal, out Walls.Ledge still, 0.5f, 2.6f)) { LetGo(p, body, "the edge is gone"); return; }
                still.Hang = _ledge.Hang;   // (where the body hangs stays: a shimmy moves it)
                _ledge = still;
            }
            if (_since >= _hangFor) { LetGo(p, body, "the bar ran out"); return; }
            if (!Drain(p, Cost(p, Plugin.HangCostPerSecond.Value, true, -_ledge.Normal) * dt)) { LetGo(p, body, "out of stamina"); return; }
            bool jump = Jumped();
            if (Crouched() || Controls.Back) { LetGo(p, body, "let go"); return; }
            // up with a fresh press of forward (let go of it first: it is still held from the climb or the run), or at once with jump
            if (!Controls.Forward) _forwardFresh = true;
            // jump while looking away from the wall: a kick off it, toward where you look (backwards off the ledge); looking at it: up
            Vector3 look = p.GetLookDir(); look.y = 0f;
            bool lookingAway = look.sqrMagnitude > 0.01f && Vector3.Dot(look.normalized, _ledge.Normal) > 0.2f;
            if (jump && _since > 0.1f && Plugin.WallJump.Value && (lookingAway || !_canPullUp)) { Kick(p, body, _wall, Vector3.zero); if (State != Move.Hang) return; }
            else if (_canPullUp && ((_since > 0.25f && _forwardFresh && Controls.Forward) || (_since > 0.1f && jump))) { StartPullUp(p, body); if (State != Move.Hang) return; }

            if (Plugin.Shimmy.Value && Mathf.Abs(Controls.Move.x) > 0.3f) Shimmy(p, Mathf.Sign(Controls.Move.x), dt);

            // held at the edge: the hands on it, the body hanging off the face
            Vector3 to = _ledge.Hang - p.transform.position;
            Drive(p, body, Vector3.ClampMagnitude(to / Mathf.Max(dt, 0.001f) * 0.25f, 6f), -_ledge.Normal, dt);
            CurrentVel(p) = Vector3.zero;
            Look.Lean(_ledge.Normal, 0f);    // (upright, close to the wall: leaning out left the body floating off it)
            Look.Hanging(p, _ledge.Edge, _ledge.Normal);
        }

        /// <summary>
        /// Along the edge, sideways (side 1: to the right, facing the wall). The edge is looked for again a little way ahead, from where the
        /// body would hang there, with the same facing: the move stops at its end, at an inside corner (the face turns), or where the body
        /// wouldn't fit. Never into nothing.
        /// </summary>
        private static void Shimmy(Player p, float side, float dt)
        {
            Vector3 along = Vector3.Cross(Vector3.up, -_ledge.Normal) * side;
            float speed = Mathf.Lerp(Plugin.ShimmyMin.Value, Plugin.ShimmyMax.Value, Skill.Factor(p));
            Vector3 look = _ledge.Hang + along * 0.35f;   // (ahead of the hands, so the body stops before the end)
            if (!Walls.FindLedge(p, -_ledge.Normal, out Walls.Ledge ahead, 0.4f, 2.6f, look)) { LastRefusal = "the end of the edge"; return; }
            if (Vector3.Dot(ahead.Normal, _ledge.Normal) < 0.8f || Mathf.Abs(ahead.Edge.y - _ledge.Edge.y) > 0.4f) { LastRefusal = "a corner"; return; }
            Vector3 hang = _ledge.Hang + along * speed * dt;
            hang += ahead.Normal * ((p.GetCollider().radius + 0.02f) - Vector3.Dot(hang - ahead.Edge, ahead.Normal));   // (kept off the face)
            hang.y = Mathf.MoveTowards(hang.y, Mathf.Max(ahead.Hang.y, _ledge.Hang.y - 0.3f), dt);
            if (!Walls.Fits(hang, p.GetCollider().radius, p.GetCollider().height)) { LastRefusal = "something in the way"; return; }
            Vector3 step = hang - _ledge.Hang;
            _ledge.Hang = hang;
            _ledge.Edge = new Vector3(_ledge.Edge.x + step.x, ahead.Edge.y, _ledge.Edge.z + step.z);
            _ledge.Stand = ahead.Stand - along * 0.35f;
            _ledge.Normal = Vector3.Slerp(_ledge.Normal, ahead.Normal, 0.5f).normalized;
            _ledge.Top = ahead.Top; _ledge.Face = ahead.Face; _ledge.Over = ahead.Over;
            _wall.Normal = _ledge.Normal; _wall.Point = _ledge.Edge; _wall.Collider = ahead.Face;
            if (Time.time >= _nextGrabSound) { _nextGrabSound = Time.time + 0.4f; Feel.Grab(p, _ledge); }
            Look.Shimmying(side, speed);
        }

        private static void StartPullUp(Player p, Rigidbody body)
        {
            if (DungeonSaysNo(p)) { LetGo(p, body, Forbidden); return; }
            Vector3 raised = new Vector3(_ledge.Hang.x, _ledge.Edge.y + 0.08f, _ledge.Hang.z);
            if (!Walls.Fits(raised, p.GetCollider().radius, p.GetCollider().height) || !Walls.Fits(_ledge.Stand + Vector3.up * 0.05f, p.GetCollider().radius, p.GetCollider().height))
            { LetGo(p, body, "no room on top"); return; }
            float cost = Cost(p, Plugin.MantleCost.Value, true, -_ledge.Normal);
            if (!Pay(p, cost)) return;   // (stays hanging; let go or rest)
            Finish(p, "pulling up");
            Begin(p, Move.PullUp, _wall);
            _pullFrom = p.transform.position;
            Feel.PullUp(p);
            Raise(p, 1f);
        }

        private static void TickPullUp(Player p, Rigidbody body, float dt)
        {
            const float Seconds = 0.4f;
            _since += dt;
            float t = _since / Seconds;
            // up beside the wall first, then over the top
            Vector3 raised = new Vector3(_ledge.Hang.x, _ledge.Edge.y + 0.08f, _ledge.Hang.z);
            Vector3 stand = _ledge.Stand + Vector3.up * 0.05f;
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f)), over = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.6f) / 0.4f));
            Vector3 target = over <= 0f ? Vector3.Lerp(_pullFrom, raised, up) : Vector3.Lerp(raised, stand, over);
            Vector3 off = target - p.transform.position;
            if (t > 0.5f && off.magnitude > 0.7f) { End(p, "stuck"); return; }
            if ((t >= 1f && off.magnitude < 0.25f) || t > 2f)
            {
                End(p, _ledge.Over ? "vaulted over" : "pulled up");
                if (!_ledge.Over) Feel.OnTop(p, _ledge);
                // over a thin wall: on over the top and down the far side (the fall counts from the top, a roll softens it)
                body.linearVelocity = _ledge.Over ? -_ledge.Normal * 2.5f : Vector3.zero;
                _letGoAt = Time.time;   // (not straight onto the next edge in front)
                return;
            }
            Drive(p, body, Vector3.ClampMagnitude(off / Mathf.Max(dt, 0.001f), 16f), -_ledge.Normal, dt);   // (from arm's length: up fast)
            CurrentVel(p) = Vector3.zero;
            Look.Lean(-_ledge.Normal, 0f);
            Look.PullingUp(p, _ledge.Edge, _ledge.Normal, over, t, _ledge.Over);
        }

        /// <summary>Kick off a wall: up, and out the way the player looks (never into the wall), keeping some of a run's speed along it.</summary>
        private static void Kick(Player p, Rigidbody body, Walls.Wall wall, Vector3 carry)
        {
            if (DungeonSaysNo(p)) return;
            if (SameAsKicked(wall)) { LastRefusal = "the same wall twice in a row"; return; }
            if (State == Move.None && TooFastToCatch(p)) { LastRefusal = TooFast; return; }   // (from a move: the fall is measured from it)
            if (KicksLeft(p) <= 0) { LastRefusal = "no wall jumps left: touch the ground"; return; }
            if (!p.HaveStamina(p.m_jumpStaminaUsage))
            {
                LastRefusal = "not enough stamina";
                if (Hud.instance != null) Hud.instance.StaminaBarEmptyFlash();
                return;
            }
            float before = p.GetStamina();
            End(p, "kicked off");
            float skill = Skill.Factor(p);
            Vector3 v = Steer(wall.Normal, p.GetLookDir()) * 6f + Vector3.up * (p.m_jumpForce * (1f + skill * 0.2f)) + carry;
            p.GetSEMan().ApplyStatusEffectJumpMods(ref v);
            MaxAirAltitude(p) = p.transform.position.y;
            // the game's own jump: its animation and effects, and its jump stamina (with armour, gear and effects) through Player.OnJump
            p.ForceJump(v, true);
            Feel.Touch(p, wall);   // (a foot pushing off the wall)
            Feel.Widen();
            float cost = before - p.GetStamina();
            _kicks++;
            _kicked = true;
            _kickedNormal = wall.Normal;
            _kickedPoint = wall.Point;
            _kickAt = Time.time;
            _touchAt = -10f;
            Raise(p, 1f);
            Done.Add(new Record { Move = "WallJump", Started = Time.time, From = p.transform.position, To = p.transform.position, Ended = $"kicked off at {v.magnitude:0.0} m/s", Stamina = cost });
            Trim();
        }

        // ---- the slide ----

        /// <summary>Crouch on the ground: a slide, when sprinting fast enough (else the game crouches, as ever).</summary>
        private static bool CanSlide(Player p)
        {
            if (!Plugin.Enabled.Value || !Plugin.Slide.Value || State != Move.None || Time.time - _slideEndAt < 0.5f) return false;
            Rigidbody body = Body(p);
            if (body == null || Blocked(p, body, true) != null || p.IsCrouching() || !p.IsRunning()) return false;
            Vector3 flat = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            if (flat.magnitude < p.m_walkSpeed) return false;
            _slideAt = Time.time;
            return true;
        }

        private static void StartSlide(Player p, Rigidbody body) => StartSlide(p, new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z), false);

        /// <summary>A landing straight into a slide (Roll.cs: crouch just before a low landing, moving forward).</summary>
        internal static void SlideFromLanding(Player p, Vector3 flat)
        {
            Rigidbody body = Body(p);
            if (State != Move.None || body == null || Blocked(p, body, true) != null) return;
            StartSlide(p, flat, true);
        }

        /// <summary>
        /// A slide this way: a quarter faster than the sprint (or the speed it came with, if more). Landed into, it keeps the landing's
        /// speed and gets no burst on top when that is already faster than a slide's start.
        /// </summary>
        private static void StartSlide(Player p, Vector3 flat, bool landing)
        {
            flat.y = 0f;
            if (flat.magnitude < 1f) return;
            Begin(p, Move.Slide, default);
            _slideDir = flat.normalized;
            float start = RunSpeed(p) * (1f + Plugin.SlideBoost.Value / 100f);
            _slideSpeed = landing ? Mathf.Max(flat.magnitude, start) : Mathf.Max(flat.magnitude, RunSpeed(p)) * (1f + Plugin.SlideBoost.Value / 100f);
            _slideTop = _slideSpeed * 1.6f;
            _slideFor = Mathf.Lerp(Plugin.SlideMin.Value, Plugin.SlideMax.Value, Skill.Factor(p));
            Feel.Ground(p, FootStep.MotionType.Land);
            Raise(p, 1f);
            Hint(p, "parkour_hint_slide");
        }

        private static void TickSlide(Player p, Rigidbody body, float dt)
        {
            _since += dt;
            if (Jumped())
            {
                // a slide-jump: the slide's speed carried, with a boost forward and up (the game's jump: its sound, dust and jump stamina)
                Vector3 leap = _slideDir * (_slideSpeed * (1f + Plugin.SlideJumpBoost.Value / 100f)) + Vector3.up * (p.m_jumpForce * (1f + Plugin.SlideJumpHeight.Value / 100f));
                p.GetSEMan().ApplyStatusEffectJumpMods(ref leap);
                EndSlide(p, "jumped");
                p.ForceJump(leap, true);
                Feel.Widen();
                Raise(p, 1f);
                return;
            }
            if (_since >= _slideFor) { EndSlide(p, "the bar ran out"); return; }
            if (!Controls.CrouchHeld) { EndSlide(p, "let go of crouch"); return; }
            if (!p.IsOnGround()) { EndSlide(p, "left the ground"); return; }
            if (Walls.Find(p, _slideDir, 0.25f, out _)) { EndSlide(p, "something in front"); return; }

            // along the ground: easing off on the flat, keeping or gaining speed down a slope of more than 10 degrees, losing it uphill
            Vector3 dir = Vector3.ProjectOnPlane(_slideDir, p.GetLastGroundNormal()).normalized;
            float downhill = -dir.y;   // (the sine of the slope along the slide)
            float accel = downhill > 0.17f ? -Physics.gravity.y * downhill * 0.8f : -_slideSpeed * 0.5f / Mathf.Max(0.1f, _slideFor) + Mathf.Min(0f, -Physics.gravity.y * downhill);
            _slideSpeed = Mathf.Min(_slideTop, _slideSpeed + accel * dt);
            if (_slideSpeed < p.m_walkSpeed) { EndSlide(p, "slowed down"); return; }
            Vector3 v = dir * _slideSpeed;
            v.y = Mathf.Min(v.y, body.linearVelocity.y);   // (gravity keeps it on the ground)
            body.linearVelocity = v;
            p.transform.rotation = Quaternion.RotateTowards(p.transform.rotation, Quaternion.LookRotation(_slideDir), 720f * dt);
            CurrentVel(p) = _slideDir * _slideSpeed;
            Look.Lean(-_slideDir, 18f);   // (leaning back, feet first)
            Look.Sliding(p, _slideDir);
            Feel.SlideDust(p);
        }

        private static void EndSlide(Player p, string why)
        {
            End(p, why);
            _slideEndAt = Time.time;
            Feel.Ground(p, FootStep.MotionType.Run);
        }

        // Crouched on the ground and moving, the game counts the player as sneaking (sneak stamina, sneak skill): not while sliding.
        [HarmonyPatch(typeof(Player), "OnSneaking")]
        private static class Player_OnSneaking
        {
            private static bool Prefix(Player __instance) => State != Move.Slide || !ReferenceEquals(__instance, Player.m_localPlayer);
        }

        /// <summary>
        /// Which way a kick goes, flat: where the player looks, blended with straight out of the wall, and at least 20 degrees out of the
        /// wall's plane. Looking back out along the wall (or at the wall itself, as on a climb) kicks straight out.
        /// </summary>
        private static Vector3 Steer(Vector3 n, Vector3 look)
        {
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f || Vector3.Dot(look.normalized, n) < -0.7f) return n;
            Vector3 d = (look.normalized + n * 0.5f).normalized;
            const float MinOut = 0.342f;   // sin 20 degrees
            float outward = Vector3.Dot(d, n);
            if (outward >= MinOut) return d;
            Vector3 along = d - n * outward;
            if (along.sqrMagnitude < 0.0001f) return n;
            return (along.normalized * Mathf.Sqrt(1f - MinOut * MinOut) + n * MinOut).normalized;
        }

        // ---- ending ----

        private static void DropOff(Player p, Rigidbody body, string why)
        {
            End(p, why);
            body.linearVelocity = _wall.Normal * 1.5f + Vector3.up * Mathf.Min(body.linearVelocity.y, 0f);
            _climbEndAt = Time.time;
        }

        private static void LetGo(Player p, Rigidbody body, string why)
        {
            End(p, why);
            body.linearVelocity = _ledge.Normal * 1f;
            _letGoAt = Time.time;
        }

        public static void End(Player p, string why)
        {
            if (State == Move.None) return;
            Finish(p, why);
            State = Move.None;
            _since = 0f;
            if (p != null) WallRunning(p) = false;
        }

        /// <summary>Note how a move went (for the status and test commands).</summary>
        private static void Finish(Player p, string why)
        {
            if (_record == null || p == null) { _record = null; Look.Stop(); return; }
            _record.Ended = why;
            _record.Seconds = Time.time - _record.Started;
            _record.To = p.transform.position;
            _record.Distance = Vector3.Distance(new Vector3(_record.From.x, 0f, _record.From.z), new Vector3(_record.To.x, 0f, _record.To.z));
            _record.Stamina = _staminaAtStart - p.GetStamina();
            Done.Add(_record);
            Trim();
            _record = null;
            Look.Stop();
        }

        private static void Trim() { while (Done.Count > 20) Done.RemoveAt(0); }

        internal static void Note(Record r) { Done.Add(r); Trim(); }

        // ---- what the blue bar shows (Bar.cs) ----

        public static int KicksAllowed(Player p) => KicksAt(Skill.Level(p));

        /// <summary>Wall jumps in a row at a Parkour level (also in the skill's description: Skill.Describe).</summary>
        public static int KicksAt(float level) =>
            Mathf.Min(Plugin.KicksMax.Value, Plugin.KicksAtSkill0.Value + Mathf.FloorToInt(level / Mathf.Max(1, Plugin.KickEveryLevels.Value)));

        public static int KicksLeft(Player p) => Mathf.Max(0, KicksAllowed(p) - _kicks);

        public static int KicksUsed => _kicks;

        /// <summary>How full the bar is (0 to 1) for the move going on, or below 0 for no bar.</summary>
        public static float BarValue(Player p)
        {
            switch (State)
            {
                case Move.WallRun: return 1f - Mathf.Clamp01(_since / Mathf.Max(0.01f, _runFor));
                case Move.Climb: return Mathf.Clamp01((_climbTop - p.transform.position.y) / Mathf.Max(0.1f, _climbHeight));
                case Move.Hang: return 1f - Mathf.Clamp01(_since / Mathf.Max(0.01f, _hangFor));
                case Move.PullUp: return 1f;
                case Move.Slide: return 1f - Mathf.Clamp01(_since / Mathf.Max(0.01f, _slideFor));
                case Move.None:
                    // right after a wall jump, in the air: the kicks left in the chain, in steps (after any other move: nothing, so it fades out)
                    if (_kicks > 0 && !p.IsOnGround() && Time.time - _kickAt < 1.5f) return KicksLeft(p) / (float)Mathf.Max(1, KicksAllowed(p));
                    return -1f;
                default: return -1f;
            }
        }

        // ---- helpers ----

        /// <summary>Set the body's velocity for this step (and face this way), with the gravity this step will take away added back.</summary>
        private static void Drive(Player p, Rigidbody body, Vector3 v, Vector3 face, float dt)
        {
            body.linearVelocity = v - Physics.gravity * dt;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
                p.transform.rotation = Quaternion.RotateTowards(p.transform.rotation, Quaternion.LookRotation(face.normalized), 720f * dt);
        }

        private static float RunSpeed(Player p)
        {
            float factor = 1f;
            try { if (RunSpeedFactor != null) factor = RunSpeedFactor(p); } catch (Exception) { }
            return p.m_runSpeed * factor;
        }

        /// <summary>
        /// What a move costs, the way the game prices a jump or a sprint: more in armour that slows you, with the gear's jump or run
        /// stamina bonus, other effects that change jump or run stamina, the world's stamina setting, and less as the skill grows.
        /// </summary>
        private static float Cost(Player p, float cost, bool jumpLike, Vector3 dir)
        {
            if (cost <= 0f) return 0f;
            cost = cost - cost * p.GetEquipmentMovementModifier() + cost * (jumpLike ? p.GetEquipmentJumpStaminaModifier() : p.GetEquipmentRunStaminaModifier());
            if (jumpLike) p.GetSEMan().ModifyJumpStaminaUsage(cost, ref cost);
            else p.GetSEMan().ModifyRunStaminaDrain(cost, ref cost, dir);
            cost *= Game.m_moveStaminaRate;
            cost *= 1f - Plugin.SkillCostCut.Value / 100f * Skill.Factor(p);
            return Mathf.Max(0f, cost);
        }

        /// <summary>Pay a move's cost up front, or refuse it (the stamina bar flashes, as for a jump without the stamina).</summary>
        private static bool Pay(Player p, float cost)
        {
            if (!p.HaveStamina(cost))
            {
                LastRefusal = "not enough stamina";
                if (Time.time - _lastFlash > 1f && Hud.instance != null) { Hud.instance.StaminaBarEmptyFlash(); _lastFlash = Time.time; }
                return false;
            }
            p.UseStamina(cost);
            return true;
        }

        /// <summary>Pay a running cost for this step; false once the stamina is gone.</summary>
        private static bool Drain(Player p, float cost)
        {
            if (cost > 0f) { p.UseStamina(cost); _drained += cost; }
            return p.HaveStamina();
        }

        internal static void Raise(Player p, float amount)
        {
            if (amount * Plugin.SkillGain.Value <= 0f) return;
            float before = Skill.Level(p);
            p.RaiseSkill(Skill.Type, amount);   // (SkillGain is in the skill's step: Skill.Gain)
            int unlock = Plugin.AutoRollLevel.Value;
            if (before < unlock && Skill.Level(p) >= unlock && unlock <= 100) p.Message(MessageHud.MessageType.Center, T("parkour_autoroll_unlocked"));
        }

        internal static void Hint(Player p, string key)
        {
            if (!Plugin.Hints.Value || !Hinted.Add(key)) return;
            p.Message(MessageHud.MessageType.TopLeft, T(key));
        }
    }
}
