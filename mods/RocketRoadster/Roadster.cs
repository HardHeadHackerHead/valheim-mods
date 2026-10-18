using System.Collections.Generic;
using DHack.Shared;
using UnityEngine;
using static DHack.Shared.Translations;

namespace RocketRoadster
{
    /// <summary>
    /// The roadster: a GroundVehicle (springs, motor, steering, seats: Shared/GroundVehicle.cs) with what the real car showed at its reveal on
    /// top: cold-gas thrusters fed from a tank of compressed air, fans that suck it onto the road, and enough control in the air to roll and
    /// flip it and land on its wheels.
    ///
    ///  - Space: the lift jets under the floor. A tap with the wheels down hops it (over a cart, a wall, an enemy); held, they lift it (they
    ///    push along the car's own up, so tilting it in the air moves it that way). They run on the tank, which refills on the ground.
    ///  - Shift+W: the four rear jets as well (boost): faster, to a higher top speed, also in the air.
    ///  - In the air: A and D roll it (a barrel roll is a second or so), W and S pitch it (front and back flips). Let go and it turns the
    ///    shortest way back to wheels down. Landing a roll or flip says what you did.
    ///  - On the ground at speed the fans press it down, so it holds steep slopes and can go round a loop if fast enough.
    ///
    /// Who does what: the rider's game owns it and runs all of this; it saves only which jets are firing (one int, when that changes) so
    /// everyone sees the plumes. Every game works out its height above the ground (for the gauge, the doors, and the rule that nobody gets
    /// out in the air) and draws the jets, the doors and the yoke.
    /// </summary>
    internal class Roadster : GroundVehicle
    {
        internal static readonly List<Roadster> All = new List<Roadster>();

        /// <summary>The local rider holds Space (Plugin's SetControls patch: jump is the thrusters, never "get off").</summary>
        internal static bool LiftHeld;

        private static int _groundMask = -1;
        private string _jetsKey;
        private int _jetsWritten = -1, _jetsShown;
        private bool _liftWas, _boosting;
        private Transform _doorL, _doorR, _yoke;
        private float _door;
        private Jets _jets;
        private Vector3 _groundPoint;
        private bool _groundFound;

        // tricks, on the rider's game
        private float _airFor, _rollSum, _pitchSum;
        // the attitude jets: which way they turn it now (bits 4 to 128, Jets.TurnOf), each held a moment so a puff shows; when last saved
        private int _turnBits;
        private readonly float[] _turnUntil = new float[6];
        private float _jetsWrittenAt;

        /// <summary>The owner's game: how full the air tank is, 0 to 1.</summary>
        internal float Tank { get; private set; } = 1f;
        /// <summary>Every game: metres from its underside to whatever is below it (300 when nothing is).</summary>
        internal float Altitude { get; private set; }
        internal bool Airborne => Altitude > 0.9f;
        internal bool HighUp => Altitude > 2.5f;
        internal bool Boosting => _boosting;
        internal int JetsNow => _jetsShown;

        /// <summary>The roadster this player sits in as the passenger, or null.</summary>
        internal static Roadster PassengerIn(Player p)
        {
            if (p == null || !p.IsAttached()) return null;
            foreach (Roadster r in All) if (r != null && r.PassengerNow() == p) return r;
            return null;
        }

        /// <summary>The roadster this player drives or sits in, or null.</summary>
        internal static Roadster Aboard(Player p) => Riding(p) as Roadster ?? PassengerIn(p);

        /// <summary>
        /// A car standing in the world keeps the model it was made with. After a hot reload (Shared/PieceReload.cs gives it this copy's
        /// component) its model is from the old build: replace it with this build's, so model changes show without placing a new car. The
        /// seats are left alone (whoever sits in it is attached to them), and this runs before the wheels and lights are looked up.
        /// </summary>
        protected override void PrepareModel()
        {
            Transform model = transform.Find("Model");
            if (model == null || model.Find(Model.Stamp) != null) return;
            DestroyImmediate(model.gameObject);
            try { Model.Build(transform); }
            catch (System.Exception e) { Plugin.Log.LogWarning("Could not rebuild a roadster's model: " + e); }
        }

        protected override void Setup()
        {
            All.Add(this);
            _jetsKey = Spec.Id + "_jets";
            _doorL = FindChild(transform, "door_l");
            _doorR = FindChild(transform, "door_r");
            _yoke = FindChild(transform, "yoke");
            if (_groundMask < 0) _groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) _jets = new Jets(transform);
            TakeOverFromOldCopy();
        }

        /// <summary>
        /// A hot reload swaps this component on cars already standing (Shared/PieceReload.cs): the old one is destroyed and this one added in
        /// the same frame. The driver's game still holds the old, now destroyed, one as what they control, and would drop them out of the car
        /// on its next update. If this game's player is the car's driver and still sits in its seat, hand them to this copy instead.
        /// </summary>
        private void TakeOverFromOldCopy()
        {
            Player me = Player.m_localPlayer;
            if (me == null || !NView.IsValid() || GetUser() != me.GetZDOID().UserID || !me.IsAttached() || Seat == null) return;
            if (me.GetAttachPoint() != Seat) return;
            IDoodadController held = me.GetDoodadController();
            if (held is GroundVehicle) return;                            // (already this copy's)
            if (held != null && (held as Object) != null) return;         // (something else, still alive: not ours to take)
            me.StartDoodadControl(this);
            Plugin.Log.LogInfo("Reloaded with you at the wheel: you keep driving");
        }

        protected override void Teardown()
        {
            All.Remove(this);
            _jets?.Dispose();
            _jets = null;
        }

        private static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindChild(c, name);
                if (f != null) return f;
            }
            return null;
        }

        protected override bool RightsItselfInAir => false;    // (it rolls on purpose; its own air control levels it)
        protected override bool LetsGoWhenFlipped => false;    // (on its roof it rolls itself back over)
        protected override float TopSpeedNow(float top) => _boosting ? Mathf.Max(top, Plugin.BoostSpeed.Value) : top;

        private bool LocalRides => Player.m_localPlayer != null && GetUser() == Player.m_localPlayer.GetZDOID().UserID;

        // ---- the owner's game: thrusters, fans, air control ------------------------------------------------------------------

        protected override void PhysicsStep(float dt, int grounded, bool ridden)
        {
            Rigidbody body = Body;
            Transform t = transform;
            float g = -Physics.gravity.y;
            bool fresh = ridden && InputFresh;
            bool mine = ridden && LocalRides;
            float throttle = fresh ? ThrottleIn : 0f, steer = fresh ? SteerIn : 0f;
            bool liftIn = mine && LiftHeld;
            bool boostIn = fresh && RunIn && throttle > 0.1f;

            // the tank: the jets drain it, it fills again with the wheels on the ground
            bool lift = liftIn && Tank > 0f;
            bool boost = boostIn && Tank > 0f;
            float drain = (lift ? 1f : 0f) + (boost ? 0.6f : 0f);
            if (drain > 0f) Tank = Mathf.Max(0f, Tank - drain * dt / Mathf.Max(1f, Plugin.TankSeconds.Value));
            else if (grounded > 0) Tank = Mathf.Min(1f, Tank + dt / Mathf.Max(1f, Plugin.RefillSeconds.Value));
            _boosting = boost;

            // a tap with the wheels down: a hop
            if (lift && !_liftWas && grounded > 0)
            {
                body.linearVelocity += t.up * Plugin.HopSpeed.Value;
                Tank = Mathf.Max(0f, Tank - 0.05f);
                _airFor = 0f; _rollSum = 0f; _pitchSum = 0f;
            }
            _liftWas = liftIn;

            if (lift) body.AddForce(t.up * (body.mass * g * Plugin.LiftStrength.Value));
            if (boost)
            {
                // the rear jets push less as it nears the boost top speed, so they can't run it away
                float speed = Vector3.Dot(body.linearVelocity, t.forward);
                float left = Mathf.Clamp01(1f - speed / Mathf.Max(5f, Plugin.BoostSpeed.Value));
                body.AddForce(t.forward * (body.mass * Plugin.BoostPush.Value * left));
            }

            // the fans: pressed onto the road at speed, so it holds steep ground (and a loop, fast enough)
            if (grounded >= 2 && ridden && Plugin.Downforce.Value > 0f)
            {
                float speed = body.linearVelocity.magnitude;
                float k = Mathf.Clamp01((speed - 6f) / 18f) * Plugin.Downforce.Value;
                body.AddForce(-t.up * (body.mass * g * k));
            }

            Vector3 asked = Vector3.zero;
            if (grounded == 0) asked = AirControl(dt, throttle, steer, ridden, mine);
            else
            {
                if (_airFor > 0.45f && mine) Landed();
                _airFor = 0f; _rollSum = 0f; _pitchSum = 0f;
            }
            if (grounded == 0 && Altitude < 0.6f && transform.up.y > 0.85f) asked = Vector3.zero;   // (resting upright on its belly, a step or a rock: no puffing)
            _turnBits = TurnBits(asked);

            // which jets fire: saved for everyone's plumes when it changes (the attitude jets at most ten times a second: one small int)
            int jets = (lift ? 1 : 0) | (boost ? 2 : 0) | _turnBits;
            bool mainChanged = (jets & 3) != (_jetsWritten & 3);
            if (jets != _jetsWritten && NView.IsValid() && (mainChanged || Time.time - _jetsWrittenAt > 0.1f))
            {
                _jetsWritten = jets;
                _jetsWrittenAt = Time.time;
                NView.GetZDO().Set(_jetsKey, jets);
            }
        }

        /// <summary>
        /// The attitude jets' bits from the turn asked for (x pitch, y yaw, z roll, in rad/s still wanted), each axis held 0.15 s after it was
        /// last asked for, so even a quick correction shows a puff.
        /// </summary>
        private int TurnBits(Vector3 asked)
        {
            int bits = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                float v = asked[axis];
                if (v > 0.35f) { _turnUntil[axis * 2] = Time.time + 0.15f; _turnUntil[axis * 2 + 1] = 0f; }
                else if (v < -0.35f) { _turnUntil[axis * 2 + 1] = Time.time + 0.15f; _turnUntil[axis * 2] = 0f; }
                if (Time.time < _turnUntil[axis * 2]) bits |= 4 << (axis * 2);
                else if (Time.time < _turnUntil[axis * 2 + 1]) bits |= 8 << (axis * 2);
            }
            return bits;
        }

        /// <summary>
        /// In the air: W/S pitch it and A/D roll it at the set rates (the turn eases in quickly, so a tap nudges and a hold spins). With
        /// nothing held it turns the shortest way back to wheels down, so a roll let go of past halfway finishes itself. The landing guard
        /// takes over from the rider when the ground is close: if it would land before it could turn wheels-down at its normal pace, it
        /// turns there now, harder, so it always comes down on its wheels. Returns the turn still wanted on each axis (for the jets).
        /// </summary>
        private Vector3 AirControl(float dt, float throttle, float steer, bool ridden, bool mine)
        {
            Rigidbody body = Body;
            Transform t = transform;
            _airFor += dt;
            Vector3 w = body.angularVelocity;
            float pitchNow = Vector3.Dot(w, t.right), rollNow = Vector3.Dot(w, t.forward), yawNow = Vector3.Dot(w, t.up);
            float pitchWant, rollWant, levelGain = Plugin.SelfLevel.Value, levelCap = 7f;
            bool turning = ridden && _airFor > 0.1f && (Mathf.Abs(throttle) > 0.1f || Mathf.Abs(steer) > 0.1f);
            if (Plugin.LandingGuard.Value)
            {
                // how long until it meets the ground (falling under gravity from its height), against how long it needs to turn wheels-down
                float g = -Physics.gravity.y, down = -body.linearVelocity.y, alt = Altitude;
                float impact = (-down + Mathf.Sqrt(Mathf.Max(0f, down * down + 2f * g * alt))) / g;
                float tilt = Vector3.Angle(t.up, Vector3.up) * Mathf.Deg2Rad;
                if (alt < 30f && tilt > 0.15f && impact < tilt / 9f + 0.3f)
                {
                    turning = false;
                    levelGain = Mathf.Max(levelGain, 7f);
                    levelCap = 12f;
                }
            }
            if (turning)
            {
                pitchWant = throttle * Plugin.PitchRate.Value * Mathf.Deg2Rad;   // W: nose down (a front flip); S: nose up (a back flip)
                rollWant = -steer * Plugin.RollRate.Value * Mathf.Deg2Rad;       // A: roll left; D: roll right
            }
            else
            {
                Vector3 axis = Vector3.Cross(t.up, Vector3.up);
                float angle = Vector3.Angle(t.up, Vector3.up) * Mathf.Deg2Rad;
                if (axis.sqrMagnitude < 1e-4f) axis = angle > 1f ? t.forward : Vector3.zero;   // (upside down exactly: roll over)
                else axis.Normalize();
                Vector3 want = axis * Mathf.Min(angle * levelGain, levelCap);
                pitchWant = Vector3.Dot(want, t.right);
                rollWant = Vector3.Dot(want, t.forward);
            }
            float newPitch = Mathf.MoveTowards(pitchNow, pitchWant, 28f * dt);
            float newRoll = Mathf.MoveTowards(rollNow, rollWant, 34f * dt);
            float newYaw = Mathf.MoveTowards(yawNow, 0f, 3f * dt);
            body.angularVelocity = t.right * newPitch + t.forward * newRoll + t.up * newYaw;
            if (mine)
            {
                _rollSum += newRoll * dt * Mathf.Rad2Deg;
                _pitchSum += newPitch * dt * Mathf.Rad2Deg;
            }
            return new Vector3(pitchWant - pitchNow, -yawNow, rollWant - rollNow);
        }

        /// <summary>The rider's game, wheels back on the ground: what they pulled off in the air, if anything.</summary>
        private void Landed()
        {
            if (!Plugin.TrickCallouts.Value || Player.m_localPlayer == null) return;
            int rolls = Mathf.FloorToInt((Mathf.Abs(_rollSum) + 45f) / 360f);
            int flips = Mathf.FloorToInt((Mathf.Abs(_pitchSum) + 45f) / 360f);
            var parts = new List<string>();
            if (rolls == 1) parts.Add(T("roadster_trick_roll"));
            else if (rolls > 1) parts.Add(T("roadster_trick_rolls", rolls));
            if (flips > 0)
            {
                string key = _pitchSum > 0f ? "roadster_trick_frontflip" : "roadster_trick_backflip";
                parts.Add(flips == 1 ? T(key) : T(key + "s", flips));
            }
            if (parts.Count == 0)
            {
                if (_airFor > 3f) Player.m_localPlayer.Message(MessageHud.MessageType.Center, T("roadster_trick_air", _airFor.ToString("0.0")));
                return;
            }
            string text = string.Join(" + ", parts);
            if (transform.up.y > 0.8f) text += "\n" + T("roadster_trick_clean");
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }

        // ---- every game: height, plumes, doors, yoke -------------------------------------------------------------------------

        protected override void FrameTick(float dt)
        {
            if (!NView.IsValid()) return;
            Transform t = transform;
            Vector3 centre = t.TransformPoint(new Vector3(0f, 0.6f, 0f));
            _groundFound = Physics.Raycast(centre, Vector3.down, out RaycastHit hit, 300f, _groundMask, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody != Body;
            if (!_groundFound)
            {
                // (the ray found this car itself first: look again from under it)
                _groundFound = Physics.Raycast(centre + Vector3.down * 1.6f, Vector3.down, out hit, 300f, _groundMask, QueryTriggerInteraction.Ignore);
                if (_groundFound) hit.distance += 1.6f;
            }
            Altitude = _groundFound ? Mathf.Max(0f, hit.distance - 0.6f) : 300f;
            _groundPoint = _groundFound ? hit.point : centre + Vector3.down * 300f;
            float water = Floating.GetLiquidLevel(centre);
            if (water > _groundPoint.y && water < centre.y) { _groundPoint.y = water; Altitude = Mathf.Max(0f, centre.y - water - 0.6f); }

            _jetsShown = NView.IsOwner() ? Mathf.Max(0, _jetsWritten) : NView.GetZDO().GetInt(_jetsKey, 0);
            bool ridden = GetUser() != 0L;
            _jets?.Tick(_jetsShown, ridden, Mathf.Clamp01(Mathf.Abs(Speed) / Mathf.Max(1f, Plugin.BoostSpeed.Value)), Altitude, _groundPoint);

            // the scissor doors: up when it stands empty and someone is near, down while anyone is in it or it moves
            Player me = Player.m_localPlayer;
            bool near = me != null && Vector3.Distance(me.transform.position, t.position) < 6f;
            bool open = near && !ridden && PassengerNow() == null && !Airborne && t.up.y > 0.7f && Mathf.Abs(Speed) < 0.5f;
            _door = Mathf.MoveTowards(_door, open ? 1f : 0f, dt * 1.4f);
            float a = Mathf.SmoothStep(0f, 1f, _door) * 70f;
            if (_doorL != null) _doorL.localRotation = Quaternion.Euler(a, 0f, a * 0.12f);
            if (_doorR != null) _doorR.localRotation = Quaternion.Euler(a, 0f, -a * 0.12f);

            // the yoke turns as it steers
            if (_yoke != null) _yoke.localRotation = Quaternion.Euler(-22f, 0f, 0f) * Quaternion.Euler(0f, 0f, -Steer * 80f);
        }
    }
}
