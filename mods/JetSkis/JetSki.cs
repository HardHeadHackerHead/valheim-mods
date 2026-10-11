using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// What a jet ski does, on top of the game's boat (Ship): its motor, its steering, getting on from the water, and the lean.
    ///
    /// Who does what, as with the game's boats: the rider's game reads the keys and sends the throttle (our RPC) and the steering (the
    /// game's "Rudder" RPC) to the game that owns the jet ski; the owner (usually the rider: the game hands a boat to whoever is aboard)
    /// keeps them in the jet ski's saved data, where every game reads them, and pushes the jet ski along. Every game draws the lean and the
    /// handlebars from that data.
    ///
    /// Reads everything it needs in Awake, so a copy put on after a hot reload (Shared/PieceReload.cs) works at once.
    /// </summary>
    internal class JetSki : MonoBehaviour
    {
        private const string RpcThrottle = "dh_jetski_Throttle", RpcPaint = "dh_jetski_Paint";
        private const string PaintKey = "dh_jetski_paint";   // (saved on the jet ski: its colour)
        private const float Waterline = -0.02f;   // where the water comes up to on the hull (its body's y)

        private static readonly Dictionary<Ship, JetSki> All = new Dictionary<Ship, JetSki>();
        private static readonly AccessTools.FieldRef<Ship, Ship.Speed> SpeedRef = AccessTools.FieldRefAccess<Ship, Ship.Speed>("m_speed");
        private static readonly AccessTools.FieldRef<Ship, float> RudderRef = AccessTools.FieldRefAccess<Ship, float>("m_rudderValue");
        private static readonly AccessTools.FieldRef<Ship, float> RudderSentRef = AccessTools.FieldRefAccess<Ship, float>("m_sendRudderTime");
        private static bool _helped;

        private Ship _ship;
        private ZNetView _nview;
        private Rigidbody _body;
        private ShipControlls _controls;
        private Transform _model, _bars;
        private WaterVolume _waterCache, _effectsWater;
        private bool _floatSet;
        private Effects _effects;
        private Transform _passengerSeat;
        private float _paintReadAt;

        // the rider's side
        private float _steer, _steerSentAt, _steerSent, _throttleSentAt;
        private Ship.Speed _throttleSent = Ship.Speed.Stop;
        // every game: how fast it moves, from where it is (a boat its game doesn't own has no speed of its own)
        private Vector3 _lastPos, _velocity;

        internal static JetSki Of(Ship ship) => ship != null && All.TryGetValue(ship, out JetSki j) && j != null ? j : null;
        internal static IEnumerable<JetSki> Loaded => All.Values;
        internal Transform Body => _model;
        internal Transform Bars => _bars;
        internal ShipControlls Controls => _controls;
        internal float Speed { get; private set; }   // forwards, metres a second (every game: from how it moves)
        internal Ship.Speed Throttle => _ship.GetSpeedSetting();
        internal float Rudder => _ship.GetRudderValue();

        /// <summary>Who sits on it now (anyone's game can tell: the rider is in its saved data), or null.</summary>
        internal Player RiderNow()
        {
            if (_controls == null || !_nview.IsValid()) return null;
            long user = _controls.GetUser();
            if (user == 0L) return null;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerID() == user)
                {
                    if (p == Player.m_localPlayer && !p.IsAttached()) return null;
                    return Vector3.Distance(p.transform.position, _controls.m_attachPoint.position) < 1f ? p : null;
                }
            return null;
        }

        /// <summary>Who sits behind the rider now, or null (another game's player: whoever is seated right there).</summary>
        internal Player PassengerNow()
        {
            if (_passengerSeat == null) return null;
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null || Vector3.Distance(p.transform.position, _passengerSeat.position) > 0.35f) continue;
                if (p == Player.m_localPlayer ? p.IsAttached() : Seated(p)) return p;
            }
            return null;
        }

        internal Transform PassengerSeat => _passengerSeat;

        /// <summary>Another game's player is sitting (their seat animation is shared with everyone).</summary>
        private static bool Seated(Player p)
        {
            Animator a = p.GetComponentInChildren<Animator>();
            return a != null && a.GetBool(Things.AttachAnimation);
        }

        /// <summary>The jet ski this player sits behind the rider on now, or null.</summary>
        internal static JetSki PassengerOf(Player p)
        {
            foreach (JetSki ski in All.Values) if (ski != null && ski.PassengerNow() == p) return ski;
            return null;
        }

        internal int PaintIndex => _nview.IsValid() ? _nview.GetZDO().GetInt(PaintKey) : 0;

        /// <summary>Shift+E: the next colour (asked of the game that owns it, which saves it on the jet ski for everyone).</summary>
        internal void NextPaint(Player player)
        {
            if (!_nview.IsValid() || player == null) return;
            if (!PrivateArea.CheckAccess(transform.position)) return;   // (someone else's ward: not theirs to paint)
            _nview.InvokeRPC(RpcPaint);
        }

        private void RPC_Paint(long sender)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner()) return;
                _nview.GetZDO().Set(PaintKey, (_nview.GetZDO().GetInt(PaintKey) + 1) % Effects.Paints.Length);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Paint: " + e); }
        }

        /// <summary>The jet ski this player rides now, or null.</summary>
        internal static JetSki RidingOf(Player p)
        {
            foreach (JetSki ski in All.Values) if (ski != null && ski.RiderNow() == p) return ski;
            return null;
        }

        private void Awake()
        {
            _ship = GetComponent<Ship>();
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Rigidbody>();
            _controls = _ship != null ? _ship.m_shipControlls : null;
            _model = transform.Find(Model.BodyName);
            _bars = _model != null ? _model.Find(Model.BarsName) : null;
            if (_controls != null && _controls.m_attachPoint != null) _controls.m_attachPoint.localPosition = Model.SeatPoint;   // (jet skis already in the world get a moved seat too)
            _lastPos = transform.position;
            if (_ship == null || _nview == null || _nview.GetZDO() == null) { enabled = false; return; }
            All[_ship] = this;
            _nview.Register<int>(RpcThrottle, RPC_Throttle);
            _nview.Register(RpcPaint, RPC_Paint);
            _passengerSeat = GetComponentInChildren<Chair>(true)?.m_attachPoint;
            if (_passengerSeat != null) _passengerSeat.localPosition = Model.PassengerPoint;
            _effects = new Effects(transform, _model);
            _effects.Paint(_nview.GetZDO().GetInt(PaintKey));
        }

        private void OnDestroy()
        {
            if (_ship != null && All.TryGetValue(_ship, out JetSki j) && j == this) All.Remove(_ship);
            if (_nview != null && _nview.GetZDO() != null) { _nview.Unregister(RpcThrottle); _nview.Unregister(RpcPaint); }   // (a hot reload's copy registers them again)
            _effects?.Dispose();
        }

        // ---- the rider's game ---------------------------------------------------------------------------------------------

        /// <summary>Each frame the rider's game hands the keys over (in place of the game's own boat controls).</summary>
        internal void Drive(Vector3 move, bool run)
        {
            if (!_nview.IsValid()) return;
            Ship.Speed want = move.z > 0.5f ? (run ? Ship.Speed.Full : Ship.Speed.Half) : move.z < -0.5f ? Ship.Speed.Back : Ship.Speed.Stop;
            if (want != _ship.GetSpeedSetting() && (want != _throttleSent || Time.time - _throttleSentAt > 0.3f))
            {
                _nview.InvokeRPC(RpcThrottle, (int)want);
                _throttleSent = want;
                _throttleSentAt = Time.time;
            }

            // the handlebars come back to the middle by themselves, quicker than they turn
            float target = Mathf.Clamp(move.x, -1f, 1f);
            bool easing = Mathf.Abs(target) < Mathf.Abs(_steer) || target * _steer < 0f;
            _steer = Mathf.MoveTowards(_steer, target, (easing ? 5f : 3f) * Time.deltaTime);
            if (_nview.IsOwner()) RudderRef(_ship) = _steer;
            if (Time.time - _steerSentAt > 0.1f && (Mathf.Abs(_steer - _steerSent) > 0.001f || Time.time - _steerSentAt > 0.5f))
            {
                _nview.InvokeRPC("Rudder", _steer);   // the game's own: the owner keeps it in the saved data
                _steerSent = _steer;
                _steerSentAt = Time.time;
                RudderSentRef(_ship) = Time.time;    // (so this game doesn't read the older saved value back meanwhile)
            }
        }

        /// <summary>E on the jet ski: from the water or the shore, you climb onto the saddle first.</summary>
        internal void Mount(Player player, bool anyDistance = false)
        {
            if (player == null || player.IsAttached() || !_nview.IsValid()) return;
            if (player.IsEncumbered()) { player.Message(MessageHud.MessageType.Center, "$jetskis_too_heavy"); return; }
            if (!anyDistance && Vector3.Distance(player.transform.position, _controls.m_attachPoint.position) > _controls.m_maxUseRange)
            {
                player.Message(MessageHud.MessageType.Center, "$jetskis_too_far");
                return;
            }
            if (!_ship.IsPlayerInBoat(player))
            {
                Vector3 onto = _controls.m_attachPoint.position + Vector3.up * 0.3f;
                Rigidbody body = player.GetComponent<Rigidbody>();
                player.transform.position = onto;
                if (body != null) { body.position = onto; body.linearVelocity = Vector3.zero; }
            }
            StartCoroutine(AskToRide(player));
        }

        /// <summary>The game lets you take the controls once it counts you aboard (a frame or two after climbing on).</summary>
        private IEnumerator AskToRide(Player player)
        {
            for (float waited = 0f; waited < 1.5f; waited += Time.fixedDeltaTime)
            {
                if (player == null || !_nview.IsValid()) yield break;
                if (_ship.IsPlayerInBoat(player))
                {
                    _nview.InvokeRPC("RequestControl", player.GetPlayerID());   // the game's own: the owner says yes unless someone else rides
                    player.HideHandItems();   // both hands on the grips
                    if (!_helped && Plugin.Help.Value)
                    {
                        _helped = true;
                        player.Message(MessageHud.MessageType.TopLeft, "$jetskis_help");
                    }
                    _steer = 0f;
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
        }

        // ---- the owner's game -------------------------------------------------------------------------------------------------

        private void RPC_Throttle(long sender, int speed)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner()) return;
                var want = (Ship.Speed)speed;
                if (want != Ship.Speed.Stop && want != Ship.Speed.Half && want != Ship.Speed.Full && want != Ship.Speed.Back) return;
                if (want != Ship.Speed.Stop && !IsRider(sender)) return;
                SpeedRef(_ship) = want;
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Throttle: " + e); }
        }

        /// <summary>The one who sent it rides this jet ski (when this game can tell: it knows the riders near it).</summary>
        private bool IsRider(long sender)
        {
            long user = _controls.GetUser();
            if (user == 0L) return false;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p.GetPlayerID() == user) return p.GetOwner() == sender;
            return true;
        }

        /// <summary>After the game's own boat physics each step, on the owner's game: the motor and the steering.</summary>
        internal int MotorCalls;   // (for the test command)
        internal bool FloatSet => _floatSet;

        internal void Motor(float dt)
        {
            MotorCalls++;
            if (!_nview.IsValid() || !_nview.IsOwner()) return;
            if (!_floatSet)
            {
                // floating: the game's boat pushes up harder the deeper its centre of mass sits below (water + offset), and settles where that
                // push equals gravity (this far under). The offset puts the waterline just under the red band, whatever the centre of mass
                // (read here, once physics has worked it out).
                float settle = _ship.m_forceDistance * -Physics.gravity.y / (50f * Mathf.Max(0.01f, _ship.m_force));
                _ship.m_waterLevelOffset = _body.centerOfMass.y + settle - Waterline;
                _floatSet = true;
            }
            if (!_controls.HaveValidUser() && _ship.GetSpeedSetting() != Ship.Speed.Stop) SpeedRef(_ship) = Ship.Speed.Stop;   // nobody at the handlebars

            Vector3 com = _body.worldCenterOfMass;
            float depth = com.y - Floating.GetWaterLevel(com, ref _waterCache) - _ship.m_waterLevelOffset;
            if (depth > _ship.m_disableLevel) return;   // in the air off a wave, or up on the shore: no grip on the water

            Transform t = transform;
            Vector3 forward = Vector3.ProjectOnPlane(t.forward, Vector3.up), right = Vector3.ProjectOnPlane(t.right, Vector3.up);
            if (forward.sqrMagnitude < 0.25f) return;   // standing on its nose or tail
            forward.Normalize();
            right.Normalize();

            Vector3 v = _body.linearVelocity;
            float along = Vector3.Dot(v, forward), side = Vector3.Dot(v, right);
            float target;
            switch (_ship.GetSpeedSetting())
            {
                case Ship.Speed.Full: target = Plugin.TopSpeed.Value; break;
                case Ship.Speed.Half: case Ship.Speed.Slow: target = Plugin.Cruise.Value; break;
                case Ship.Speed.Back: target = -Plugin.Reverse.Value; break;
                default: target = 0f; break;
            }
            float rate = Mathf.Abs(target) > Mathf.Abs(along) && target * along >= 0f
                ? Plugin.Accel.Value * (target < 0f ? 0.6f : 1f)              // picking up speed
                : target == 0f ? Plugin.Coast.Value : Plugin.Accel.Value * 1.3f;   // coasting, or braking against the way it moves
            float newAlong = Mathf.MoveTowards(along, target, rate * dt);

            // it holds its line in a turn: most of the sideways slide goes, some of it carried on forwards (it carves)
            float newSide = side * Mathf.Exp(-Plugin.Grip.Value * dt);
            if (Mathf.Abs(newAlong) > 1f) newAlong += Mathf.Sign(newAlong) * Mathf.Abs(side - newSide) * 0.5f;
            float cap = Mathf.Max(Mathf.Abs(along), Mathf.Abs(target));   // (carving never takes it past what the throttle asks)
            newAlong = Mathf.Clamp(newAlong, -cap, cap);
            _body.linearVelocity = v + forward * (newAlong - along) + right * (newSide - side);

            // steering: it turns at its full rate once moving, slower when nearly still; backwards, the other way
            float steer = RudderRef(_ship);
            if (along < -0.5f) steer = -steer;
            float wanted = steer * Plugin.TurnRate.Value * Mathf.Deg2Rad * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(Mathf.Abs(along) / 5f));
            Vector3 up = t.up, spin = _body.angularVelocity;
            float yaw = Vector3.Dot(spin, up);
            _body.angularVelocity = spin + up * (Mathf.MoveTowards(yaw, wanted, 8f * dt) - yaw);

            // it stays nearly level in a turn (the body leans for show: Update), so a hard turn doesn't tip the hull or the view with it
            Vector3 axis = t.forward;
            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, axis), t.up, axis) * Mathf.Deg2Rad;
            float rolling = Vector3.Dot(_body.angularVelocity, axis);
            _body.angularVelocity += axis * (Mathf.MoveTowards(rolling, -roll * 5f, 14f * dt) - rolling);
        }

        // ---- every game ---------------------------------------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 pos = transform.position;
            _velocity = Vector3.Lerp(_velocity, (pos - _lastPos) / dt, Mathf.Clamp01(dt * 6f));
            _lastPos = pos;
            float along = Vector3.Dot(_velocity, transform.forward);
            Speed = along;
            float rudder = _ship.GetRudderValue();
            if (_effects != null)
            {
                bool inWater = transform.position.y - Floating.GetWaterLevel(transform.position, ref _effectsWater) < 0.4f;
                _effects.Tick(along, _ship.GetSpeedSetting(), rudder, _controls != null && _controls.HaveValidUser(), inWater);
                if (Time.time > _paintReadAt) { _paintReadAt = Time.time + 0.5f; _effects.Paint(PaintIndex); }
            }

            if (_bars != null) _bars.localRotation = Quaternion.Euler(0f, rudder * 25f, 0f);
            if (_model != null)
            {
                Quaternion pose = Quaternion.identity;
                if (Plugin.Lean.Value)
                {
                    float lean = -rudder * Mathf.Clamp01(along / 8f) * 16f;       // into the turn
                    float nose = -Mathf.Clamp01(along / 12f) * 4f;               // up a little at speed, as it planes
                    pose = Quaternion.Euler(nose, 0f, lean);
                }
                _model.localRotation = Quaternion.Slerp(_model.localRotation, pose, Mathf.Clamp01(dt * 5f));
            }
        }
    }
}
