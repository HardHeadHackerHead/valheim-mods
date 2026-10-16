using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Webslinger
{
    internal enum Move { None, Swing, Zip, Crawl, Fly }

    /// <summary>
    /// The moves, run on the player's own game each physics step, right after the game's own movement step (Character.UpdateMotion): a move
    /// then sets the body's velocity. While one runs the game's steering is off (Controls), so its air control doesn't stop a swing dead.
    /// Gravity stays the game's (the body's own): a swing is gravity and a rope; a zip and a crawl cancel it by adding back what it takes
    /// each step. Nothing here touches the body's gravity or kinematic flags (other mods save and restore them, so a changed flag could stick).
    ///
    ///   Swing  hold Jump in the air: a web to the best spot up ahead (Anchors.SwingAnchor) and a rope that only pulls. Holding a move key
    ///          pumps the swing; the rope shortens to keep the arc off the ground. Let go: fly off, with a fling (more near the top).
    ///   Zip    Sprint in the air: pulled to the spot under the crosshair. A top: hop onto it; a wall: stick to it (Crawl); nothing in reach:
    ///          a burst forward (a few per air time).
    ///   Crawl  move into a wall in the air: stuck to it, move keys crawl along it, over the top at its head, Jump kicks off, Crouch lets go.
    ///   Fly    after a release, a kick or a zip forward: the speed is kept (the game's air control off) until the ground, the water, or slow.
    /// </summary>
    internal static class Web
    {
        public static Move State;
        /// <summary>Where the web is stuck (swing and zip).</summary>
        public static Vector3 Anchor;
        /// <summary>When the web left the hand, and how long it flies before it sticks (it pulls only once stuck).</summary>
        public static float ShotAt = -10f, Travel;
        public static bool Stuck;
        /// <summary>A web shot into nothing (a zip forward): drawn for a moment, pulls nothing.</summary>
        public static Vector3 LooseEnd;
        public static float LooseAt = -10f;

        private static float _length, _ropeTarget, _since, _chainAt = -10f;
        /// <summary>The swing's pivot (out from the surface), the point the body swings under now (Anchors.Virtual), and the way it flies.</summary>
        private static Vector3 _hold, _virt, _travel;
        /// <summary>When the web stuck; and whether Jump has been held all along since (a tap: the short swing, never the next web).</summary>
        private static float _stuckAt = -10f;
        private static bool _heldAll;
        /// <summary>The last time a move ran (a landing just after one takes the web landing damage: none by default).</summary>
        public static float FlowAt = -10f;
        private static Vector3 _zipTo, _zipDir, _zipNormal;
        private static Anchors.Kind _zipKind;
        private static float _zipBest, _zipStuckFor;
        private static Anchors.Wall _wall;
        private static int _airZips;
        private static float _kickAt = -10f, _endAt = -10f, _lastFlash = -10f, _lastPerfect = -10f;
        private static Collider _kicked;

        private static readonly HashSet<string> Hinted = new HashSet<string>();

        internal static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        internal static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        // ---- what tests and the status command read ----

        internal sealed class Record
        {
            public string Move, Ended = "", Detail = "";
            public float Started, Seconds, TopSpeed, Rise, Distance, Stamina, ReleaseSpeed;
            public Vector3 From, To;
            public bool Perfect;
        }
        public static readonly List<Record> Done = new List<Record>();
        private static Record _record;
        public static string LastRefusal = "";

        public static bool OwnsMovement => State != Move.None;

        public static void Reset()
        {
            Player p = Player.m_localPlayer;
            if (p != null && State != Move.None) Look.StandUp(p);
            Fresh();
            Done.Clear();
            Hinted.Clear();
        }

        private static void Fresh()
        {
            State = Move.None;
            Stuck = false;
            ShotAt = LooseAt = _kickAt = _endAt = -10f;
            _airZips = 0;
            _kicked = null;
            _wall = default;
            _record = null;
            LastRefusal = "";
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class Player_OnSpawned
        {
            private static void Postfix(Player __instance)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                Fresh();
                Share.Forget();
                Compat.Forget();
            }
        }

        public static bool TakingInput(Player p) => p != null && TakeInput(p);

        /// <summary>No webs while dead, swimming, sitting or riding, too heavy, building, flying with the debug fly, or in a cutscene.</summary>
        public static bool CanAct(Player p, out string why)
        {
            why = null;
            if (!Plugin.Enabled.Value) why = "Webslinger is off";
            else if (p.IsDead()) why = "dead";
            else if (p.IsSwimming() || p.InWater() && p.IsOnGround()) why = "in the water";
            else if (p.IsAttached() || p.IsRiding()) why = "sitting or riding";
            else if (p.IsEncumbered()) why = "carrying too much";
            else if (p.InPlaceMode()) why = "building";
            else if (p.IsDebugFlying()) why = "debug flying";
            else if (p.InCutscene() || p.IsTeleporting()) why = "teleporting";
            else if (p.IsStaggering()) why = "staggered";
            return why == null;
        }

        // ---- the input a move takes from the game (called from Controls, before the game acts on the keys) ----

        public static bool TakesJump(Player p)
        {
            if (!Plugin.Enabled.Value) return false;
            if (State == Move.Crawl || State == Move.Swing || State == Move.Zip) return true;
            if (p.IsOnGround() || !Plugin.Swing.Value || !CanAct(p, out _) || Compat.Busy(p)) return false;
            // in the air, only when there is something to web to: otherwise the press is the game's (and other mods' double jumps)
            if (Plugin.SkyWebs.Value) return true;
            float f = Skill.Factor(p);
            return Anchors.SwingAnchor(p, Mathf.Lerp(Plugin.WebReach.Value, Plugin.WebReachAt100.Value, f), out _);   // (Jump only ever swings: zips are Sprint's)
        }

        /// <summary>A swing's top speed before the air holds it back (the server's, by the skill).</summary>
        public static float Cruise(Player p) => Mathf.Lerp(Plugin.SwingSpeed.Value, Plugin.SwingSpeedAt100.Value, Skill.Factor(p));

        public static bool TakesCrouch(Player p) => State == Move.Crawl || State == Move.Swing || State == Move.Zip;

        // ---- every physics step ----

        [HarmonyPatch(typeof(Character), "UpdateMotion")]
        private static class Character_UpdateMotion
        {
            private static void Postfix(Character __instance, float dt)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                Player p = (Player)__instance;
                try { Tick(p, dt); }
                catch (Exception e) { Plugin.Log.LogError("Webslinger step: " + e); End(p, "error", Move.None); }
                try { Share.Publish(p); }
                catch (Exception e) { Plugin.Log.LogError("Webslinger share: " + e); }
            }
        }

        private static void Tick(Player p, float dt)
        {
            if (!CanAct(p, out string why))
            {
                if (State != Move.None) End(p, why, Move.None);
                Controls.TakeJump(); Controls.TakeZip(); Controls.TakeCrouch();
                return;
            }
            bool ground = p.IsOnGround();
            if (ground) _airZips = 0;
            if (_record != null) _record.Seconds = Time.time - _record.Started;
            _since += dt;
            if (State != Move.None) FlowAt = Time.time;

            switch (State)
            {
                case Move.Swing: SwingStep(p, dt); break;
                case Move.Zip: ZipStep(p, dt); break;
                case Move.Crawl: CrawlStep(p, dt); break;
                default: Free(p, dt, ground); break;
            }
            Look.Pose(p, dt);
            if (_record != null)
            {
                float speed = Body(p).linearVelocity.magnitude;
                if (speed > _record.TopSpeed) _record.TopSpeed = speed;
            }
        }

        /// <summary>Out of a web (or flying off one): what may start now.</summary>
        private static void Free(Player p, float dt, bool ground)
        {
            if (State == Move.Fly && FlyStep(p, dt, ground)) return;
            if (ground) { Controls.TakeJump(); Controls.TakeZip(); return; }
            bool busy = Compat.Busy(p);
            if (busy) { Controls.TakeJump(); Controls.TakeZip(); return; }

            // a fresh press of Jump in the air (a jump off the ground is only a jump, a Spider-Man one: SpiderJump)
            if (Controls.TakeJump() && Plugin.Swing.Value && StartSwing(p)) return;
            if (Plugin.Zip.Value && Controls.TakeZip() && StartZip(p)) return;
            if (Plugin.Crawl.Value && TryStick(p)) return;
        }

        // ---- the swing ----

        /// <summary>
        /// A web up ahead (also the next one of a held swing); true when one was shot. Nothing in reach high enough to swing from: no web
        /// at all (the jump is just a jump), unless the server turned on SkyWebs.
        /// </summary>
        private static bool StartSwing(Player p)
        {
            float f = Skill.Factor(p);
            float reach = Mathf.Lerp(Plugin.WebReach.Value, Plugin.WebReachAt100.Value, f);
            bool sky = false;
            if (!Anchors.SwingAnchor(p, reach, out Anchors.Spot spot))
            {
                if (!Plugin.SkyWebs.Value) { Refuse(p, Anchors.LastMiss); return false; }
                spot.Point = spot.Hold = Anchors.SkyAnchor(p);
                spot.Rope = Mathf.Max(Anchors.MinRope, Anchors.RopeFor(p, spot.Hold));
                spot.ArcClear = true;
                sky = true;
            }
            if (!Pay(p, Plugin.WebCost.Value)) return false;
            Begin(p, Move.Swing);
            if (_record != null)
                _record.Detail = sky ? "sky" : $"{Tools.Name(spot.What)}, rope {spot.Rope:0} m{(spot.ArcClear ? "" : ", something in the way")}";
            Anchor = spot.Point;
            _hold = spot.Hold;
            _travel = Anchors.Ahead(p);
            _virt = Anchors.Virtual(_hold, Anchors.Pivot(p), _travel);
            ShotAt = Time.time;
            Travel = Vector3.Distance(Anchors.Pivot(p), Anchor) / Look.WebSpeed;
            Stuck = false;
            _ropeTarget = spot.Rope;
            _heldAll = true;
            _length = Vector3.Distance(Anchors.Pivot(p), _virt);
            Vector3 anchor = Anchor;
            Look.Shot(p, anchor);
            Raise(p, 0.5f);
            Hint(p, "swing");
            return true;
        }

        private static void SwingStep(Player p, float dt)
        {
            // Let go of Jump: off the web. A quick tap still gives a swing: the web holds on until the body rises out of the bottom of the
            // arc (or a second), so a tap is never a web that vanishes before it sticks.
            if (!Controls.JumpHeld)
            {
                float held = Stuck ? Time.time - _stuckAt : 0f;
                if (_heldAll && Stuck) { Release(p); return; }
                _heldAll = false;
                if (Stuck && ((held > 0.35f && Body(p).linearVelocity.y > 1f) || held > 1f)) { Release(p); return; }
            }
            if (Plugin.Zip.Value && Controls.TakeZip() && StartZip(p)) return;
            if (Controls.TakeCrouch()) { End(p, "let go (crouch)", Move.Fly); return; }
            Controls.TakeJump();

            Rigidbody body = Body(p);
            Vector3 v = body.linearVelocity;
            float f = Skill.Factor(p);
            Vector3 ahead = Anchors.Ahead(p);
            if (!Stuck)
            {
                if (Time.time - ShotAt < Travel) return;   // the web is still flying: the body falls as it was
                Stuck = true;
                _stuckAt = Time.time;
                Look.Stick(p, Anchor);
                _virt = Anchors.Virtual(_hold, Anchors.Pivot(p), _travel);
                _length = Vector3.Distance(Anchors.Pivot(p), _virt);
                // a swing from standing still gets going: at least some speed ahead (the games' web pull)
                float along = Vector3.Dot(Flat(v), ahead), want = 9f + 3f * f;
                if (along < want) v += ahead * (want - Mathf.Max(0f, along));
            }
            MaxAirAltitude(p) = p.transform.position.y;   // the web holds you: a fall counts from where you let go
            if (p.IsOnGround() && _since > 0.4f && v.y <= 0.5f) { End(p, "landed", Move.None); return; }
            float cost = Plugin.SwingCostPerSecond.Value * CostFactor(p) * dt;
            if (cost > 0f) { p.UseStamina(cost); if (_record != null) _record.Stamina += cost; }

            // flying into a wall: onto it (the games turn a swing into a wall run)
            Vector3 flat = Flat(v);
            if (Plugin.Crawl.Value && flat.magnitude > 3f && Anchors.Find(p, flat, 0.3f + flat.magnitude * dt * 2f, out Anchors.Wall wall)
                && Vector3.Dot(Flat(wall.Normal).normalized, flat.normalized) < -0.5f)
            {
                End(p, "into a wall", Move.None);
                StartCrawl(p, wall);
                return;
            }

            // the point the body swings under follows the way it flies (an anchor to the side swings it on, not round), never in a jump
            Vector3 pivot = Anchors.Pivot(p);
            if (flat.magnitude > 2f) _travel = flat.normalized;
            _virt = Vector3.MoveTowards(_virt, Anchors.Virtual(_hold, pivot, _travel), 6f * dt);
            Vector3 r = pivot - _virt;
            float d = r.magnitude;
            if (d < 1f) { Release(p); return; }
            Vector3 n = r / d;   // out from the pivot to the body

            // The rope reels in to its length (the yank as a web sticks: up and forward), and shortens further whenever the body would come
            // closer than Clear to the ground under or ahead of it: a swing never scrapes the ground.
            _length = Mathf.Max(_ropeTarget, _length - 10f * dt);
            float clear = Anchors.Clearance(p, v), keep = Anchors.Clear + 0.5f;
            if (clear < keep && v.y < 2f) _length = Mathf.Max(3f, Mathf.Min(_length, d) - (keep - clear + 0.5f) * 20f * dt);

            // steering: the way the body flies turns toward the keys (the games' weight shift)
            Vector3 input = InputDir(p);
            if (input.sqrMagnitude > 0.01f && flat.magnitude > 1f)
            {
                Vector3 turned = Vector3.RotateTowards(flat.normalized, input.normalized, Plugin.Steer.Value * Mathf.Deg2Rad * dt, 0f) * flat.magnitude;
                v = turned + Vector3.up * v.y;
            }
            // pumping: speed along the arc, the way the keys point (or, with none held, the way it already goes), up to the cruising speed
            float cruise = Cruise(p), speed = v.magnitude;
            Vector3 push = input.sqrMagnitude > 0.01f ? input : (flat.magnitude > 1f ? flat.normalized * 0.5f : Vector3.zero);
            if (push.sqrMagnitude > 0.0001f && d >= _length * 0.95f && speed < cruise)
            {
                Vector3 along = Vector3.ProjectOnPlane(push, n);
                float room = Mathf.Clamp01((cruise - speed) / 4f);   // (eases off near the cruising speed)
                if (along.sqrMagnitude > 0.0001f) v += along.normalized * (Plugin.Pump.Value * (1f + 0.3f * f) * push.magnitude * room * dt);
            }
            // faster than cruising (a dive, a long fall): the air holds it back, a little at a time
            if (speed > cruise) v *= Mathf.Max(0f, speed - (speed - cruise) * 0.8f * dt) / speed;

            // the rope: taut, it takes away whatever would stretch it, and pulls the body back onto its length (gently: a swing, not a zip)
            if (d > _length)
            {
                float outward = Vector3.Dot(v, n);
                if (outward > 0f) v -= n * outward;
                v -= n * Mathf.Min((d - _length) * 6f, 10f);
            }
            body.linearVelocity = Cap(v);

            // Jump still held at the top of the arc, past the pivot: the next web ahead, at once (hold to keep swinging)
            if (Plugin.HoldToSwing.Value && _heldAll && Controls.JumpHeld && _since - Travel > 0.5f && Time.time - _chainAt > 0.6f)
            {
                Vector3 past = Flat(pivot - _virt);
                if (Vector3.Dot(past, flat) > 0f && v.y < 0.5f && past.magnitude > 1.5f)
                {
                    _chainAt = Time.time;
                    if (_record != null) _record.Ended = "next web";
                    StartSwing(p);
                }
            }
        }

        private static void Release(Player p)
        {
            Rigidbody body = Body(p);
            Vector3 v = body.linearVelocity;
            bool perfect = false;
            if (Stuck && _length > 0.5f)
            {
                // near the top of the arc (the body at least half way up from its bottom toward the anchor) and still rising
                float bottom = _virt.y - _length, above = (Anchors.Pivot(p).y - bottom) / _length;
                perfect = v.y > 0.5f && above > 0.45f && v.magnitude > 8f;
                Vector3 flat = v; flat.y = 0f;
                float up = perfect ? Plugin.PerfectBoost.Value * (1f + 0.5f * Skill.Factor(p)) : Plugin.ReleaseBoost.Value;
                v += Vector3.up * up + flat * (perfect ? 0.15f : 0.08f);
                body.linearVelocity = Cap(v);
            }
            if (_record != null) { _record.ReleaseSpeed = v.magnitude; _record.Perfect = perfect; }
            if (perfect)
            {
                Raise(p, 1.5f);
                Look.Perfect(p);
                if (Time.time - _lastPerfect > 2f) { _lastPerfect = Time.time; p.Message(MessageHud.MessageType.TopLeft, T("webslinger_perfect")); }
            }
            End(p, perfect ? "let go: perfect release" : "let go", Move.Fly);
        }

        // ---- flying off a web ----

        /// <summary>The speed is kept and steered gently; true while it lasts (a new web or zip can still start from it: Free goes on).</summary>
        private static bool FlyStep(Player p, float dt, bool ground)
        {
            Rigidbody body = Body(p);
            Vector3 v = body.linearVelocity, flat = v; flat.y = 0f;
            if ((ground && _since > 0.15f) || p.InWater()) { End(p, ground ? "landed" : "in the water", Move.None); return false; }
            Vector3 input = InputDir(p);
            if (input.sqrMagnitude > 0.01f) v += input * (5f * dt);
            // well over cruising speed, the air slows the flight (gently: a release carries far)
            float over = flat.magnitude - (Cruise(p) + 4f);
            if (over > 0f) v -= flat.normalized * (over * 0.3f * dt);
            body.linearVelocity = Cap(v);
            return false;
        }

        // ---- the zip ----

        /// <summary>A zip to the spot under the crosshair; with none in reach, a zip forward.</summary>
        private static bool StartZip(Player p)
        {
            float f = Skill.Factor(p);
            float reach = Mathf.Lerp(Plugin.ZipReach.Value, Plugin.ZipReachAt100.Value, f);
            Rigidbody body = Body(p);
            if (Anchors.ZipTarget(p, reach, out RaycastHit hit, out Anchors.Kind kind))
            {
                if (!Pay(p, Plugin.ZipCost.Value)) return false;
                if (State == Move.Swing && _record != null) _record.Ended = "zip";
                Begin(p, Move.Zip);
                Anchor = hit.point;
                _zipNormal = hit.normal;
                _zipKind = kind;
                float r = Anchors.Radius(p);
                // where the feet go: on a top, a little above it (a hop lands on it); on a wall, the chest at the spot; under something, below it
                _zipTo = kind == Anchors.Kind.Top ? hit.point + Vector3.up * 0.6f
                    : kind == Anchors.Kind.Wall ? hit.point + Flat(hit.normal) * (r + 0.1f) - Vector3.up * 1.2f
                    : hit.point - Vector3.up * 2.2f + Flat(hit.normal) * 0.5f;
                _zipDir = (_zipTo - p.transform.position).normalized;
                _zipBest = float.MaxValue;
                _zipStuckFor = 0f;
                ShotAt = Time.time;
                Travel = Vector3.Distance(Anchors.Pivot(p), Anchor) / Look.WebSpeed;
                Stuck = false;
                Look.Shot(p, Anchor, zip: true);
                Raise(p, 0.5f);
                Hint(p, "zip");
                return true;
            }
            // nothing in reach: a burst forward, along the look (a little up), a few per air time
            int zips = Mathf.RoundToInt(Mathf.Lerp(Plugin.AirZips.Value, Plugin.AirZipsAt100.Value, f));
            if (_airZips >= zips) { Refuse(p, "no zips forward left: touch the ground"); return false; }
            if (!Pay(p, Plugin.AirZipCost.Value)) return false;
            _airZips++;
            Vector3 look = p.GetLookDir();
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg, -10f, 30f);
            Vector3 dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, Anchors.LookFlat(p))) * Anchors.LookFlat(p);
            float speed = 16f + 6f * f;
            Vector3 v = body.linearVelocity;
            float along = Mathf.Max(0f, Vector3.Dot(v, dir));
            v = dir * Mathf.Max(speed, along + 6f) + Vector3.up * 3f;
            body.linearVelocity = Cap(v);
            if (State == Move.Swing && _record != null) _record.Ended = "zip forward";
            Begin(p, Move.Fly);
            if (_record != null) _record.Move = "AirZip";
            LooseEnd = Anchors.Pivot(p) + dir * 18f;
            LooseAt = Time.time;
            Look.Loose(p, LooseEnd);
            MaxAirAltitude(p) = p.transform.position.y;
            Raise(p, 0.3f);
            Hint(p, "airzip");
            return true;
        }

        private static void ZipStep(Player p, float dt)
        {
            if (Controls.TakeCrouch()) { End(p, "let go (crouch)", Move.None); return; }
            Controls.TakeJump(); Controls.TakeZip();
            Rigidbody body = Body(p);
            MaxAirAltitude(p) = p.transform.position.y;
            Vector3 v = body.linearVelocity;
            if (!Stuck)
            {
                // the web flies out: the body hangs a beat (slowing, no gravity), then the yank
                v *= 0.9f;
                body.linearVelocity = v - Physics.gravity * dt;
                if (Time.time - ShotAt < Travel) return;
                Stuck = true;
                Look.Stick(p, Anchor);
            }
            Vector3 to = _zipTo - p.transform.position;
            float dist = to.magnitude;
            if (dist < 0.7f || Vector3.Dot(to, _zipDir) < 0f) { Arrive(p, "arrived"); return; }
            // not getting closer (something in the way) for a moment: as far as it goes
            if (dist < _zipBest - 0.05f) { _zipBest = dist; _zipStuckFor = 0f; }
            else if ((_zipStuckFor += dt) > 0.25f) { Arrive(p, "blocked"); return; }
            if (_since > 4f) { Arrive(p, "too long"); return; }
            float speed = Plugin.ZipSpeed.Value * (1f + 0.3f * Skill.Factor(p));
            v = to / dist * Mathf.Min(speed, dist / dt * 0.6f);
            body.linearVelocity = v - Physics.gravity * dt;
        }

        private static void Arrive(Player p, string how)
        {
            Rigidbody body = Body(p);
            Vector3 v = body.linearVelocity;
            if (_zipKind == Anchors.Kind.Top)
            {
                // a hop up and onto the top (the spot is on its far side of any edge)
                Vector3 onto = Flat(Anchor - p.transform.position);
                body.linearVelocity = Vector3.up * 5f + (onto.sqrMagnitude > 0.01f ? onto.normalized * 2.5f : Vector3.zero);
                End(p, how + ": perched", Move.None);
                Hint(p, "perch");
                return;
            }
            if (_zipKind == Anchors.Kind.Wall && Plugin.Crawl.Value && Anchors.Find(p, -Flat(_zipNormal), 1.2f, out Anchors.Wall wall))
            {
                End(p, how + ": on the wall", Move.None);
                StartCrawl(p, wall);
                return;
            }
            body.linearVelocity = v * 0.2f;
            End(p, how, Move.None);
        }

        // ---- the wall crawl ----

        /// <summary>In the air, moving into a wall (with forward held, or flying at it): stick to it.</summary>
        private static bool TryStick(Player p)
        {
            Vector3 vel = p.GetVelocity(), flat = Flat(vel);
            Vector3 dir = Vector3.zero;
            if (Controls.Move.z > 0.3f) dir = InputDir(p);
            else if (flat.magnitude > 4f) dir = flat.normalized;
            if (dir.sqrMagnitude < 0.01f) return false;
            if (!Anchors.Find(p, dir, 0.35f + Mathf.Min(flat.magnitude * Time.fixedDeltaTime, 0.6f), out Anchors.Wall wall)) return false;
            if (Vector3.Dot(Flat(wall.Normal).normalized, dir.normalized) > -0.5f) return false;   // (a wall we only brush along)
            if (wall.Collider == _kicked && Time.time - _kickAt < 0.6f) return false;   // (the wall just kicked off)
            if (Time.time - _kickAt < 0.25f) return false;
            StartCrawl(p, wall);
            return true;
        }

        private static void StartCrawl(Player p, Anchors.Wall wall)
        {
            Begin(p, Move.Crawl);
            _wall = wall;
            Body(p).linearVelocity = Vector3.zero;
            Look.Cling(p, wall);
            Raise(p, 0.3f);
            Hint(p, "crawl");
        }

        private static void CrawlStep(Player p, float dt)
        {
            Rigidbody body = Body(p);
            if (Controls.TakeJump()) { KickOff(p); return; }
            if (Controls.TakeCrouch()) { body.linearVelocity = Flat(_wall.Normal).normalized * 1.5f; End(p, "let go", Move.None); return; }
            if (Plugin.Zip.Value && Controls.TakeZip() && StartZip(p)) return;
            MaxAirAltitude(p) = p.transform.position.y;

            Vector3 move = Controls.Move;
            if (!Anchors.Track(p, _wall.Normal, out Anchors.Wall wall))
            {
                // the wall ended: over its top when climbing, else off it
                if (move.z > 0.3f && Anchors.TopOver(p, _wall.Normal, out Vector3 stand))
                {
                    float rise = Mathf.Max(0f, stand.y - p.transform.position.y);
                    body.linearVelocity = Vector3.up * Mathf.Max(4f, Mathf.Sqrt(2f * 9.81f * (rise + 0.5f))) - Flat(_wall.Normal).normalized * 2.5f;
                    End(p, "over the top", Move.None);
                    return;
                }
                body.linearVelocity = Flat(_wall.Normal).normalized * 1f;
                End(p, "the wall ended", Move.None);
                return;
            }
            _wall = wall;
            Vector3 n = wall.Normal;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            Vector3 right = Vector3.Cross(up, -n).normalized;

            // round an inside corner: a wall beside, the way we crawl
            if (Mathf.Abs(move.x) > 0.3f && Anchors.Find(p, right * Mathf.Sign(move.x), 0.3f, out Anchors.Wall side) && Vector3.Dot(side.Normal, n) < 0.8f)
            {
                _wall = side;
                n = side.Normal;
                up = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
                right = Vector3.Cross(up, -n).normalized;
            }

            float speed = Plugin.CrawlSpeed.Value * (Controls.RunHeld ? 1.6f : 1f) * (1f + 0.3f * Skill.Factor(p));
            Vector3 want = Vector3.ClampMagnitude(up * move.z + right * move.x, 1f) * speed;
            // on the ground, crawling down or not at all: standing (the crawl is over)
            if (p.IsOnGround() && move.z <= 0.1f && _since > 0.3f) { End(p, "on the ground", Move.None); return; }
            // a body's width off the wall: closer when too far, out when too close
            float hold = Mathf.Clamp((_wall.Gap - 0.05f) * 8f, -2f, 4f);
            Vector3 v = want - n * hold;
            body.linearVelocity = v - Physics.gravity * dt;   // (gravity cancelled this step)

            if (want.sqrMagnitude > 0.01f)
            {
                float cost = Plugin.CrawlCostPerSecond.Value * CostFactor(p) * (Controls.RunHeld ? 1.5f : 1f) * dt;
                if (cost > 0f)
                {
                    if (p.GetStamina() < cost) { Flash(); body.linearVelocity = Flat(n).normalized; End(p, "out of stamina", Move.None); return; }
                    p.UseStamina(cost);
                    if (_record != null) _record.Stamina += cost;
                }
                Raise(p, 0.15f * dt * 10f);
            }
        }

        private static void KickOff(Player p)
        {
            Vector3 n = Flat(_wall.Normal).normalized;
            Vector3 look = Anchors.LookFlat(p);
            // away from the wall and up; toward the look when it points away from the wall
            Vector3 away = Vector3.Dot(look, n) > 0f ? Vector3.Slerp(n, look, 0.5f).normalized : n;
            Body(p).linearVelocity = away * 6.5f + Vector3.up * 7f;
            _kicked = _wall.Collider;
            _kickAt = Time.time;
            Look.Kick(p);
            End(p, "kicked off", Move.Fly);
        }

        // ---- the parts every move shares ----

        private static void Begin(Player p, Move move)
        {
            if (State != Move.None && State != move) Finish(p, _record?.Ended is string e && e.Length > 0 ? e : "into " + move);
            if (State == Move.Swing || State == Move.Zip) Look.LetGo(p);
            State = move;
            _since = 0f;
            Stuck = false;
            _record = new Record { Move = move.ToString(), Started = Time.time, From = p.transform.position };
            MaxAirAltitude(p) = p.transform.position.y;
        }

        /// <summary>The move ends; next: Fly (the speed kept) or None.</summary>
        public static void End(Player p, string why, Move next)
        {
            if (State == Move.None) return;
            Finish(p, why);
            if (State == Move.Swing || State == Move.Zip) Look.LetGo(p);
            if (State == Move.Crawl) Look.Unstick(p);
            State = next;
            Stuck = false;
            _since = 0f;
            _endAt = Time.time;
            if (next == Move.Fly) _record = new Record { Move = "Fly", Started = Time.time, From = p.transform.position };
            if (next == Move.None) Look.StandUp(p);
        }

        private static void Finish(Player p, string why)
        {
            if (_record == null) return;
            _record.Ended = string.IsNullOrEmpty(_record.Ended) ? why : _record.Ended;
            _record.Seconds = Time.time - _record.Started;
            _record.To = p.transform.position;
            _record.Rise = _record.To.y - _record.From.y;
            _record.Distance = Flat(_record.To - _record.From).magnitude;
            Done.Add(_record);
            if (Done.Count > 20) Done.RemoveAt(0);
            _record = null;
        }

        private static void Refuse(Player p, string why)
        {
            LastRefusal = why;
            Done.Add(new Record { Move = "Refused", Ended = why, Started = Time.time, From = p.transform.position, To = p.transform.position });
            if (Done.Count > 20) Done.RemoveAt(0);
        }

        private static float CostFactor(Player p) => 1f - Plugin.SkillCostCut.Value / 100f * Skill.Factor(p);

        private static bool Pay(Player p, float cost)
        {
            cost *= CostFactor(p);
            if (cost <= 0f) return true;
            if (!p.HaveStamina(cost)) { Flash(); Refuse(p, "out of stamina"); return false; }
            p.UseStamina(cost);
            return true;
        }

        private static void Flash()
        {
            if (Time.time - _lastFlash > 1f && Hud.instance != null) { Hud.instance.StaminaBarEmptyFlash(); _lastFlash = Time.time; }
        }

        private static void Raise(Player p, float amount) => p.RaiseSkill(Skill.Type, amount);

        private static void Hint(Player p, string move)
        {
            if (!Plugin.Hints.Value || !Hinted.Add(move)) return;
            p.Message(MessageHud.MessageType.TopLeft, T("webslinger_hint_" + move));
        }

        private static Vector3 Cap(Vector3 v) => Vector3.ClampMagnitude(v, Plugin.MaxSpeed.Value);

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>The move keys as a direction in the world, turned with the camera (flat): forward is where the player looks.</summary>
        public static Vector3 InputDir(Player p)
        {
            Vector3 fwd = Anchors.LookFlat(p), right = Vector3.Cross(Vector3.up, fwd);
            return Vector3.ClampMagnitude(fwd * Controls.Move.z + right * Controls.Move.x, 1f);
        }

        // Spider agility: falls hurt less (the server's FallDamagePercent), after the game's own effects have had their say.
        private static readonly AccessTools.FieldRef<SEMan, Character> SeOwner = AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
        private static class SEMan_ModifyFallDamage
        {
            [HarmonyPriority(Priority.Low)]
            private static void Postfix(SEMan __instance, ref float damage)
            {
                if (!Plugin.Enabled.Value || !ReferenceEquals(SeOwner(__instance), Player.m_localPlayer)) return;
                // landing from a swing, a zip, a wall or a flight off one: the web landing (none by default, as in the games)
                bool fromWeb = Time.time - FlowAt < 1f;
                damage *= Mathf.Clamp01((fromWeb ? Plugin.WebLandingDamage.Value : Plugin.FallDamage.Value) / 100f);
            }
        }

        // The Spider-Man jump: a jump off the ground goes higher and a little further (the server's SpiderJumpPercent), for the local
        // player only. The game's jump force is raised for that one call and put back after (other mods' jump changes still apply on top).
        private static readonly AccessTools.FieldRef<Character, float> JumpForce = AccessTools.FieldRefAccess<Character, float>("m_jumpForce");
        private static readonly AccessTools.FieldRef<Character, float> JumpForward = AccessTools.FieldRefAccess<Character, float>("m_jumpForceForward");

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        private static class Character_Jump
        {
            private static void Prefix(Character __instance, out Vector2 __state)
            {
                __state = Vector2.zero;
                if (!ReferenceEquals(__instance, Player.m_localPlayer) || !Plugin.Enabled.Value || Plugin.SpiderJump.Value <= 0f || !__instance.IsOnGround()) return;
                __state = new Vector2(JumpForce(__instance), JumpForward(__instance));
                float more = 1f + Plugin.SpiderJump.Value / 100f;
                JumpForce(__instance) = __state.x * more;
                JumpForward(__instance) = __state.y * Mathf.Sqrt(more);
            }

            private static void Postfix(Character __instance, Vector2 __state)
            {
                if (__state == Vector2.zero) return;
                JumpForce(__instance) = __state.x;
                JumpForward(__instance) = __state.y;
            }
        }

        /// <summary>For the status command: the rope's length now.</summary>
        public static float Length => _length;
        public static float Since => _since;
        public static Anchors.Wall Wall => _wall;
        public static int AirZipsUsed => _airZips;
    }
}
