using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// What a land vehicle is, set by the mod that makes it (FourWheelers, MountainBikes): where its wheels are, how heavy it is, and its
    /// driving numbers as functions, so a setting changed in the .cfg (or by the server) reaches a vehicle on the road at once.
    /// </summary>
    internal sealed class VehicleSpec
    {
        public string Id;                       // "dh_atv": the start of its RPC names and saved keys
        public Vector3[] Wheels;                // hub positions at rest, in the vehicle's own space (+z is forward, y up); the first Front are steered
        public int Front = 2;
        public float WheelRadius = 0.3f, Travel = 0.3f, Mass = 400f;   // Travel: how far a wheel can move up and down
        public Func<float> Cruise, Top, Reverse, Accel, Coast, TurnRate, Grip;   // metres per second, m/s each second, degrees per second
        public Func<float> Stamina;             // pedalling: the stamina a second at a cruise (null: an engine, free)
        public float Upright = 1.5f;            // how hard it rights itself (a bike needs a lot, a four wheeler a little)
        public string RideKey, TooFarKey, TooHeavyKey, PassengerKey;   // "$mymod_ride" ...
        public float UseRange = 3.5f;
        public Dictionary<string, float> Pose;   // the rider's pose numbers (VehicleRider.cs): lean, head, kneeOut ... (null: its defaults)
        public string Command;                  // the Claude Tools test command's name (VehicleTools.cs): "atv", "bike"
        public Func<float> RamDamage;           // blunt damage to an enemy it runs over, for each m/s over 3 (null or 0: none)
        public Vector3 RamCenter, RamSize;      // the box in front of it that hits (in its own space)
        public float MinRadius = 4f;            // its tightest turning circle (metres); wider at speed
        public float LateralAccel = 14f;        // the most sideways acceleration the tyres hold (m/s^2): the turn rate falls as speed rises
        public bool LeansWithTurn;              // the body tips into turns (a bike)
        public bool Engine;                     // an engine sound (a pedal bike has only the rolling of its tyres)
        public Func<float> EngineVolume;
        public Color[] Paints;                  // the colours E on the paint spot cycles through (parts named paint* take them)
        public string PaintKey, RightKey;       // "$mymod_paint", "$mymod_right" (set it upright)
        public float DustSize = 1f;
        public string HelpKey;                  // the controls, shown the first time you get on in a session
        public Func<bool> ShowHelp;
    }

    /// <summary>
    /// A vehicle that drives on the ground: the physics of a car in the simplest form that feels right in Valheim's hills. Each wheel is
    /// a ray down from its mount with a spring and a damper (no WheelColliders), and the drive sets the speed along the ground and takes
    /// the sideways slide out of it, like the Jet Ski's motor does on water. A rider takes the controls the way the game's boats and
    /// saddles hand them over (IDoodadController): E to climb on, jump or E to get off.
    ///
    /// Who does what: the rider's game owns the vehicle (the owner decides, as with Sadle: it hands the ZDO to whoever takes the controls),
    /// so the physics runs there; every game draws the wheels from the vehicle's synced movement and steering.
    ///
    /// Shared source (mods/Shared/GroundVehicle.cs), linked into FourWheelers and MountainBikes: each mod's copy is private and keeps its
    /// own <see cref="Spec"/>. The component reads what it needs in Awake (Shared/PieceReload.cs puts a fresh one on pieces already
    /// standing after a hot reload). Wheels are children named wheel0..N (an optional "spin" child turns, the wheel itself steers), an
    /// optional "lean" child tips into turns, "seat" is where the rider sits and "passenger" the second seat's attach point.
    /// Written for two mods at once: not yet driven in the game.
    /// </summary>
    internal class GroundVehicle : MonoBehaviour, Interactable, Hoverable, IDoodadController
    {
        internal static VehicleSpec Spec;   // (this mod's private copy of the class: one spec each)
        internal static string AttachAnimation = "attach_chair";   // the game's seat animation the rider sits in (the pose goes on top: VehicleRider.cs)

        private static readonly List<GroundVehicle> All = new List<GroundVehicle>();
        internal static IEnumerable<GroundVehicle> Loaded => All;
        private static int _mask = -1;
        private static bool _helped;

        private ZNetView _nview;
        private Rigidbody _body;
        private Transform _seat, _passenger, _lean;
        private Transform[] _wheel, _spin;
        private Vector3[] _mount;
        private string _rpcRequest, _rpcRelease, _rpcResponse, _rpcControls, _userKey, _steerKey;
        private WaterVolume _water;

        // the rider's input (their game) and what the owner makes of it
        private float _throttle, _steerIn, _steer, _sentAt, _sentThrottle, _sentSteer;
        private bool _run, _brake, _sentRun;
        private float _inputAt = -99f;
        private bool _validUser;
        private float _userCheckAt;
        private float _steerWrittenAt, _steerWritten;

        // every game: how it moves (for the wheels), drawn from where it is
        private Vector3 _lastPos, _velocity;
        private float _spinAngle, _steerShown;
        private readonly float[] _drop = new float[8];   // each wheel's hub, how far below its mount (the owner's: set by the suspension)
        private float _wheelsOnGround;

        internal float Speed { get; private set; }
        internal float Steer => _steerShown;                      // -1 to 1, as drawn (every game)
        internal Transform Seat => _seat;                         // where the rider sits
        internal Transform PassengerSeat => _passenger;           // and the passenger (null: none)
        internal Transform PoseFrame => _lean != null ? _lean : transform;   // the part that leans: the pose is measured in it
        internal Rigidbody Body => _body;

        private readonly Dictionary<string, Transform> _markers = new Dictionary<string, Transform>();

        /// <summary>A named spot in the model (grip_l, foot_r, pgrip_l...: where hands and feet go), or null.</summary>
        internal Transform Marker(string name)
        {
            if (!_markers.TryGetValue(name, out Transform t) || t == null) _markers[name] = t = FindDeep(transform, name);
            return t;
        }

        /// <summary>Who sits at the controls now (anyone's game can tell: the rider is in its saved data), or null.</summary>
        internal Player RiderNow()
        {
            if (_seat == null || !_nview.IsValid()) return null;
            long user = GetUser();
            if (user == 0L) return null;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p.GetZDOID().UserID == user)
                {
                    if (p == Player.m_localPlayer) return p.IsAttached() && Vector3.Distance(p.transform.position, _seat.position) < 1f ? p : null;
                    return Vector3.Distance(p.transform.position, _seat.position) < 6f ? p : null;   // (another game's: their place arrives late)
                }
            return null;
        }

        internal string PassengerMarkKey => Spec.Id + "_passenger";

        /// <summary>
        /// Who sits behind the rider now, or null. This game's player: attached next to the passenger seat. Another game's: marked so on
        /// their own data by their game (VehicleRider.MarkLocal), as their body's place reaches this game late and lags behind a moving vehicle.
        /// </summary>
        internal Player PassengerNow()
        {
            if (_passenger == null || !_nview.IsValid()) return null;
            long mine = VehicleId;
            if (mine == 0L) return null;
            foreach (Player p in Player.GetAllPlayers())
            {
                if (p == null) continue;
                if (p == Player.m_localPlayer)
                {
                    if (p.IsAttached() && Vector3.Distance(p.transform.position, _passenger.position) < 0.35f) return p;
                    continue;
                }
                ZDO zdo = p.GetComponent<ZNetView>()?.GetZDO();
                if (zdo != null && zdo.GetLong(PassengerMarkKey, 0L) == mine && Vector3.Distance(p.transform.position, _passenger.position) < 6f) return p;
            }
            return null;
        }

        /// <summary>
        /// This vehicle's own random id (saved on it by its owner's game the first time: 0 until then), for the passenger mark on a player's own
        /// data. Never a ZDOID: the game numbers those again every time the world loads.
        /// </summary>
        internal long VehicleId => _nview.IsValid() ? _nview.GetZDO().GetLong(Spec.Id + "_vid", 0L) : 0L;

        private void EnsureId()
        {
            if (_nview.IsOwner() && _nview.GetZDO().GetLong(Spec.Id + "_vid", 0L) == 0L)
                _nview.GetZDO().Set(Spec.Id + "_vid", (UnityEngine.Random.Range(1, int.MaxValue) * 4294967296L) | (long)UnityEngine.Random.Range(1, int.MaxValue));
        }
        internal float Lean { get; private set; }   // degrees it leans into the turn now
        internal float WheelsOnGround => _wheelsOnGround;

        /// <summary>What the rider is asking for, for the gauge (their game): 0 idle, 1 cruise, 2 full, 3 reverse, 4 braking.</summary>
        internal int Gear => Time.time - _inputAt > 0.5f ? 0 : _throttle > 0.1f ? (_run ? 2 : 1) : (_throttle < -0.1f || _brake) ? (Speed > 1f ? 4 : 3) : 0;

        private float _lastYaw, _yawRate;
        private Renderer[] _headLens, _tailLens;
        private Light[] _headLights, _tailLights;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _block;
        private VehicleSound _sound;
        private VehicleDust _dust;
        private Renderer[] _paintParts;
        private Transform _bars, _cranks, _kickstand;
        private Transform[] _pedals;
        private string _rpcPaint, _rpcRight, _paintKey;
        private int _paintShown = -1;
        private float _paintReadAt, _crankAngle, _flippedFor, _stuckFor, _airTime, _airVy;
        private bool[] _contact;
        private Vector3[] _foot;

        /// <summary>The vehicle this player has the controls of now, or null (for the gauge).</summary>
        internal static GroundVehicle Riding(Player p) => p != null ? p.GetDoodadController() as GroundVehicle : null;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _body = GetComponent<Rigidbody>();
            if (Spec == null || _nview == null || _body == null || _nview.GetZDO() == null) { enabled = false; return; }   // (a placement ghost has none)

            _rpcRequest = Spec.Id + "_Request"; _rpcRelease = Spec.Id + "_Release"; _rpcResponse = Spec.Id + "_Response"; _rpcControls = Spec.Id + "_Controls";
            _userKey = Spec.Id + "_user"; _steerKey = Spec.Id + "_steer";
            _nview.Register<long>(_rpcRequest, RPC_Request);
            _nview.Register<long>(_rpcRelease, RPC_Release);
            _nview.Register<bool>(_rpcResponse, RPC_Response);
            _nview.Register<float, float, bool>(_rpcControls, RPC_Controls);

            int n = Mathf.Min(Spec.Wheels.Length, _drop.Length);
            _mount = new Vector3[n];
            _wheel = new Transform[n];
            _spin = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                _mount[i] = Spec.Wheels[i] + Vector3.up * (Spec.Travel * 0.5f);
                _wheel[i] = FindDeep(transform, "wheel" + i);
                _spin[i] = _wheel[i] != null ? _wheel[i].Find("spin") : null;
                _drop[i] = Spec.Travel * 0.5f;
            }
            _seat = FindDeep(transform, "seat");
            _passenger = FindDeep(transform, "passenger");
            _lean = FindDeep(transform, "lean");
            _headLights = GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("head")).ToArray();
            _tailLights = GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("tail")).ToArray();
            _headLens = GetComponentsInChildren<Renderer>(true).Where(r => r.name == "lens_head").ToArray();
            _tailLens = GetComponentsInChildren<Renderer>(true).Where(r => r.name == "lens_tail").ToArray();
            _block = new MaterialPropertyBlock();
            _paintParts = GetComponentsInChildren<Renderer>(true).Where(r => r.name.StartsWith("paint")).ToArray();
            _bars = FindDeep(transform, "handlebars");
            _cranks = FindDeep(transform, "cranks");
            _kickstand = FindDeep(transform, "kickstand");
            _pedals = _cranks != null ? _cranks.GetComponentsInChildren<Transform>(true).Where(x => x.name == "pedal").ToArray() : new Transform[0];
            _rpcPaint = Spec.Id + "_Paint"; _rpcRight = Spec.Id + "_Right"; _paintKey = Spec.Id + "_paint";
            _nview.Register(_rpcPaint, RPC_Paint);
            _nview.Register(_rpcRight, RPC_Right);
            _contact = new bool[n];
            _foot = new Vector3[n];
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                _sound = new VehicleSound(transform, Spec);
                _dust = new VehicleDust(transform, n, Spec.DustSize);
            }
            if (_seat == null) { enabled = false; return; }

            Piece piece = GetComponent<Piece>();
            if (piece != null) piece.m_canBeRemoved = true;   // (also on ones placed before this was fixed: the raft's is false)
            _body.mass = Spec.Mass;
            _body.linearDamping = 0.02f;
            _body.angularDamping = 2f;
            var centre = Vector3.zero;
            foreach (Vector3 w in Spec.Wheels) centre += w;
            _body.centerOfMass = centre / Mathf.Max(1, Spec.Wheels.Length) + Vector3.up * 0.05f;   // low: it doesn't tip easily
            if (_mask < 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            _lastPos = transform.position;
            All.Add(this);
        }

        private void OnDestroy()
        {
            All.Remove(this);
            _sound?.Dispose();
            _dust?.Dispose();
            if (_nview != null && _nview.GetZDO() != null && _rpcRequest != null)
            {
                _nview.Unregister(_rpcPaint); _nview.Unregister(_rpcRight);
                _nview.Unregister(_rpcRequest); _nview.Unregister(_rpcRelease); _nview.Unregister(_rpcResponse); _nview.Unregister(_rpcControls);   // (a hot reload's copy registers them again)
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        // ---- getting on and off -------------------------------------------------------------------------------------------

        public string GetHoverName() => Localization.instance.Localize(Spec != null ? Spec.RideKey : "");

        public string GetHoverText()
        {
            if (_seat == null || Player.m_localPlayer == null) return "";
            if (Vector3.Distance(Player.m_localPlayer.transform.position, _seat.position) > Spec.UseRange)
                return Localization.instance.Localize("<color=#888888>$piece_toofar</color>");
            return Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] " + (IsFlipped() ? Spec.RightKey : Spec.RideKey));
        }

        /// <summary>On its side or upside down with nobody on it: E sets it back on its wheels.</summary>
        private bool IsFlipped() => transform.up.y < 0.45f && _nview.IsValid() && GetUser() == 0L;

        /// <summary>E on the paint spot: the next colour (asked of the owner, which saves it on the vehicle for everyone).</summary>
        internal void NextPaint(Player player)
        {
            if (!_nview.IsValid() || player == null || Spec.Paints == null || Spec.Paints.Length == 0) return;
            if (!PrivateArea.CheckAccess(transform.position)) return;   // (someone else's ward: not theirs to paint)
            _nview.InvokeRPC(_rpcPaint);
        }

        private void RPC_Paint(long sender)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner() || Spec.Paints == null || Spec.Paints.Length == 0) return;
                _nview.GetZDO().Set(_paintKey, (_nview.GetZDO().GetInt(_paintKey, 0) + 1) % Spec.Paints.Length);
            }
            catch (Exception e) { Debug.LogWarning("[" + Spec.Id + "] Paint: " + e); }
        }

        /// <summary>The owner's game: stand it back on its wheels where it lies (a little higher, so it doesn't catch the ground).</summary>
        private void RPC_Right(long sender)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner() || GetUser() != 0L) return;
                Vector3 at = transform.position + Vector3.up * 0.7f;
                Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.position = at;
                _body.rotation = upright;
                transform.SetPositionAndRotation(at, upright);
            }
            catch (Exception e) { Debug.LogWarning("[" + Spec.Id + "] Right: " + e); }
        }

        public float GetHoverOffset() => 0f;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !_nview.IsValid()) return false;
            var player = user as Player;
            if (player == null || player.IsAttached()) return false;
            if (IsFlipped())
            {
                if (PrivateArea.CheckAccess(transform.position)) _nview.InvokeRPC(_rpcRight);
                return true;
            }
            Mount(player, false);
            return true;
        }

        /// <summary>Climb on (what E does): <paramref name="anyDistance"/> skips the reach check (for the test command).</summary>
        internal void Mount(Player player, bool anyDistance)
        {
            if (player == null || player.IsAttached() || !_nview.IsValid()) return;
            if (player.IsEncumbered()) { player.Message(MessageHud.MessageType.Center, Spec.TooHeavyKey); return; }
            if (!anyDistance && Vector3.Distance(player.transform.position, _seat.position) > Spec.UseRange) { player.Message(MessageHud.MessageType.Center, Spec.TooFarKey); return; }
            if (!PrivateArea.CheckAccess(transform.position)) return;   // (someone else's ward: the game says so)
            _nview.InvokeRPC(_rpcRequest, player.GetZDOID().UserID);
        }

        /// <summary>The owner's game: the rider is free to take it, or already the rider. The owner hands the vehicle (its physics) to them.</summary>
        private void RPC_Request(long sender, long playerId)
        {
            try
            {
                if (!_nview.IsValid() || !_nview.IsOwner()) return;
                CalculateValidUser();
                long user = GetUser();
                if (user == playerId || !_validUser)
                {
                    _nview.GetZDO().Set(_userKey, playerId);
                    _throttle = 0f; _steerIn = 0f; _steer = 0f; _run = false; _brake = false;
                    _nview.InvokeRPC(sender, _rpcResponse, true);
                    _nview.GetZDO().SetOwner(sender);   // the rider's game runs the physics now
                }
                else _nview.InvokeRPC(sender, _rpcResponse, false);
            }
            catch (Exception e) { Debug.LogWarning("[" + Spec.Id + "] Request: " + e); }
        }

        private void RPC_Release(long sender, long playerId)
        {
            try
            {
                if (_nview.IsValid() && _nview.IsOwner() && GetUser() == playerId)
                {
                    _nview.GetZDO().Set(_userKey, 0L);
                    _throttle = 0f; _steerIn = 0f; _run = false; _brake = false;
                }
            }
            catch (Exception e) { Debug.LogWarning("[" + Spec.Id + "] Release: " + e); }
        }

        private void RPC_Response(long sender, bool granted)
        {
            if (Player.m_localPlayer == null) return;
            if (!granted) { Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_inuse"); return; }
            Player.m_localPlayer.StartDoodadControl(this);
            Player.m_localPlayer.AttachStart(_seat, null, hideWeapons: true, isBed: false, onShip: false, AttachAnimation, new Vector3(0f, 0.5f, 0f));
            if (!_helped && Spec.HelpKey != null && (Spec.ShowHelp == null || Spec.ShowHelp()))
            {
                _helped = true;
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Spec.HelpKey);
            }
        }

        public void OnUseStop(Player player)
        {
            if (!_nview.IsValid()) return;
            _nview.InvokeRPC(_rpcRelease, player.GetZDOID().UserID);
            player.AttachStop();
        }

        public bool IsValid() => this != null;
        public Component GetControlledComponent() => this;
        public Vector3 GetPosition() => _seat != null ? _seat.position : transform.position;

        internal long GetUser() => _nview.IsValid() ? _nview.GetZDO().GetLong(_userKey, 0L) : 0L;

        /// <summary>The owner's game: the one who holds the controls (by connection id, the ZDOID.UserID the game uses for saddles) is a player still in range (not logged out or far away).</summary>
        private void CalculateValidUser()
        {
            _validUser = false;
            long user = GetUser();
            if (user == 0L || ZNet.instance == null) return;
            foreach (ZDO z in ZNet.instance.GetAllCharacterZDOS())
                if (z.m_uid.UserID == user) { _validUser = Vector3.Distance(z.GetPosition(), transform.position) < 12f; break; }
        }

        // ---- the keys -----------------------------------------------------------------------------------------------------

        /// <summary>The rider's game, every physics step (the game's own call for whatever the player controls).</summary>
        public void ApplyControlls(Vector3 moveDir, Vector3 lookDir, bool run, bool autoRun, bool block)
        {
            _inputAt = Time.time;
            _throttle = Mathf.Clamp(moveDir.z, -1f, 1f);
            _steerIn = Mathf.Clamp(moveDir.x, -1f, 1f);
            _run = run;
            _brake = block;
            if (_nview.IsValid() && !_nview.IsOwner()
                && (Time.time - _sentAt > 0.5f || (Time.time - _sentAt > 0.08f && (Mathf.Abs(_throttle - _sentThrottle) > 0.05f || Mathf.Abs(_steerIn - _sentSteer) > 0.05f || _run != _sentRun))))
            {
                _sentAt = Time.time; _sentThrottle = _throttle; _sentSteer = _steerIn; _sentRun = _run;
                _nview.InvokeRPC(_rpcControls, _throttle, _steerIn, _run);   // (until the ownership change arrives: then it's all local)
            }
        }

        private void RPC_Controls(long sender, float throttle, float steer, bool run)
        {
            if (!_nview.IsValid() || !_nview.IsOwner()) return;
            _inputAt = Time.time;
            _throttle = Mathf.Clamp(throttle, -1f, 1f);
            _steerIn = Mathf.Clamp(steer, -1f, 1f);
            _run = run;
        }

        // ---- the owner's game: suspension, drive, steering -----------------------------------------------------------------

        private void FixedUpdate()
        {
            if (!_nview.IsValid() || !_nview.IsOwner()) return;
            float dt = Time.fixedDeltaTime;
            Transform t = transform;
            if (Time.time >= _userCheckAt) { _userCheckAt = Time.time + 0.5f; CalculateValidUser(); }
            bool ridden = _validUser;
            float throttle = ridden && Time.time - _inputAt < 0.4f ? _throttle : 0f;
            float steerIn = ridden && Time.time - _inputAt < 0.4f ? _steerIn : 0f;
            bool run = ridden && _run, brake = ridden && _brake;

            // in deep water it floats and stalls
            Vector3 com = _body.worldCenterOfMass;
            float depth = Floating.GetWaterLevel(com, ref _water) - com.y;
            if (depth > 0.2f)
            {
                _body.AddForce(Vector3.up * (_body.mass * -Physics.gravity.y * Mathf.Clamp(depth * 1.5f, 0f, 1.6f)));
                _body.linearVelocity *= Mathf.Exp(-1.5f * dt);
                _body.angularVelocity *= Mathf.Exp(-1f * dt);
                for (int i = 0; i < _mount.Length; i++) { _drop[i] = Spec.Travel * 0.5f; _contact[i] = false; }
                _wheelsOnGround = 0f;
                return;
            }

            // each wheel: a spring and a damper along the ray from its mount
            int n = _mount.Length, grounded = 0, front = 0;
            // Each ray starts above the top of the wheel's travel (Lift), so on a steep slope, where the ground ahead rises above the
            // suspension's top, it still starts in the air and finds the slope (from the mount itself it started inside the ground, found
            // nothing, and the wheel sank). A sphere is cast, not a line: the wheel rolls over a rise instead of meeting it edge-on. The
            // spring keeps pushing past its normal travel (Lift of extra), up to a limit, so the wheel climbs the slope.
            const float lift = 0.45f;
            float sphere = Mathf.Min(0.18f, Spec.WheelRadius * 0.5f);
            float length = lift + Spec.Travel + Spec.WheelRadius;
            float k = _body.mass * -Physics.gravity.y / (n * Spec.Travel * 0.5f);   // (a wheel holds its share at half travel)
            float c = 2f * 0.6f * Mathf.Sqrt(k * _body.mass / n);
            float maxForce = _body.mass * -Physics.gravity.y * 2.5f;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 origin = t.TransformPoint(_mount[i]) + t.up * lift;
                if (Physics.SphereCast(origin, sphere, -t.up, out RaycastHit hit, length - sphere, _mask, QueryTriggerInteraction.Ignore))
                {
                    float reach = hit.distance + sphere;                                   // from the cast's start down to the ground under the wheel
                    float compress = Mathf.Clamp(length - reach, 0f, Spec.Travel + lift);   // (past Spec.Travel: the ground is above the top of the travel)
                    Vector3 hub = origin - t.up * (reach - Spec.WheelRadius);
                    float vUp = Vector3.Dot(_body.GetPointVelocity(hub), t.up);
                    float force = Mathf.Clamp(k * compress - c * vUp, 0f, maxForce);
                    _body.AddForceAtPosition(t.up * force, hub);
                    _drop[i] = Mathf.Clamp(Spec.Travel - compress, 0f, Spec.Travel);
                    _contact[i] = true;
                    grounded++;
                    if (i < Spec.Front) front++;
                    normal += hit.normal;
                }
                else { _drop[i] = Spec.Travel; _contact[i] = false; }
            }
            _wheelsOnGround = grounded;

            // holding the throttle but not moving at all for a moment (wedged on a ledge or a rock): a small hop up and forward gets it free
            _stuckFor = ridden && throttle > 0.1f && _body.linearVelocity.sqrMagnitude < 0.09f ? _stuckFor + dt : 0f;
            if (_stuckFor > 1.2f)
            {
                _stuckFor = 0f;
                _body.position += Vector3.up * 0.35f;
                _body.linearVelocity = t.forward * 1.5f + Vector3.up * 1.5f;
            }

            // rolled onto its back with someone on it: they let go (rather than being pinned under it)
            _flippedFor = t.up.y < 0.1f && ridden ? _flippedFor + dt : 0f;
            if (_flippedFor > 0.6f && Player.m_localPlayer != null && GetUser() == Player.m_localPlayer.GetZDOID().UserID)
            {
                _flippedFor = 0f;
                Player.m_localPlayer.StopDoodadControl();
            }

            Vector3 up = grounded > 0 ? normal.normalized : Vector3.up;
            // it rights itself (a bike is held up by it: parked, it stands)
            {
                Vector3 axis = t.forward;
                float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(up, axis), t.up, axis) * Mathf.Deg2Rad;
                float rolling = Vector3.Dot(_body.angularVelocity, axis);
                float gain = ridden ? Spec.Upright : Spec.Upright * 2f;
                _body.angularVelocity += axis * (Mathf.MoveTowards(rolling, -roll * 5f * gain, 14f * gain * dt) - rolling);
            }
            if (grounded == 0)
            {
                _airTime += dt;
                _airVy = _body.linearVelocity.y;
                return;   // in the air: gravity and what it has
            }
            if (_airTime > 0.25f && ridden) Landed(Mathf.Abs(_airVy));
            _airTime = 0f;

            Vector3 forward = Vector3.ProjectOnPlane(t.forward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);
            Vector3 v = _body.linearVelocity;
            float along = Vector3.Dot(v, forward), side = Vector3.Dot(v, right);
            float share = grounded / (float)n;
            if (ridden && Spec.RamDamage != null && along > 4f) Ram(along);

            // pedalling tires a rider out; an engine doesn't. Out of breath it drops to a walking pace.
            float cruise = Spec.Cruise(), top = Spec.Top();
            if (Spec.Stamina != null && ridden && throttle > 0.1f && Player.m_localPlayer != null && GetUser() == Player.m_localPlayer.GetZDOID().UserID)
            {
                float cost = Spec.Stamina() * (run ? 3f : 1f) * throttle * dt;
                if (Player.m_localPlayer.HaveStamina(cost)) Player.m_localPlayer.UseStamina(cost);
                else { run = false; cruise = Mathf.Min(cruise, 3f); }
            }
            if (Spec.Stamina != null && !run) top = cruise;

            float target, rate;
            if (!ridden) { target = 0f; rate = 12f; }                                             // parked: held where it stands
            else if (throttle > 0.1f) { target = (run ? top : cruise) * throttle; rate = along < target ? Spec.Accel() : Spec.Accel() * 1.5f; }
            else if (throttle < -0.1f || brake) { target = along > 1f ? 0f : -Spec.Reverse() * Mathf.Max(0.3f, -throttle); rate = along > 0f ? Spec.Accel() * 2f : Spec.Accel() * 0.6f; }
            else { target = 0f; rate = Spec.Coast(); }
            float newAlong = Mathf.MoveTowards(along, target, rate * dt * share);
            float cap = Mathf.Max(Mathf.Abs(along), Mathf.Abs(target));
            newAlong = Mathf.Clamp(newAlong, -cap, cap);

            // steering: the handlebars ease toward the keys (quickly back to the middle). It turns on a circle that opens up with speed,
            // and never harder than the tyres could hold (so it can't spin on the spot at speed), with only the back wheels down not at all.
            float easing = Mathf.Abs(steerIn) < Mathf.Abs(_steer) || steerIn * _steer < 0f ? 9f : 5f;
            _steer = Mathf.MoveTowards(_steer, steerIn, easing * dt);
            float yawNow = Vector3.Dot(_body.angularVelocity, up), yawNew = yawNow;
            if (front > 0)
            {
                float speed = Mathf.Abs(along);
                float radius = Mathf.Lerp(Spec.MinRadius, Spec.MinRadius * 2.5f, Mathf.Clamp01(speed / Mathf.Max(1f, top)));
                float maxRate = Mathf.Min(Mathf.Max(speed, 2f) / radius, Spec.LateralAccel / Mathf.Max(speed, 1f), Spec.TurnRate() * Mathf.Deg2Rad);
                float wanted = _steer * maxRate * (along < -0.5f ? -1f : 1f);
                yawNew = Mathf.MoveTowards(yawNow, wanted, 16f * dt);
                _body.angularVelocity += up * (yawNew - yawNow);
            }

            // the slide is taken out, and the velocity turns with the heading by as much as it turns this step, so it follows its nose
            // (otherwise every turn makes a sideways slide that the grip then has to take out, and it drifts through every corner)
            float newSide = side * Mathf.Exp(-Spec.Grip() * dt * share * (ridden ? 1f : 4f));
            Vector3 newV = v + forward * (newAlong - along) + right * (newSide - side);
            _body.linearVelocity = Quaternion.AngleAxis(yawNew * Mathf.Rad2Deg * dt, up) * newV;
            if (Mathf.Abs(_steer - _steerWritten) > 0.02f && Time.time - _steerWrittenAt > 0.15f)
            {
                _steerWritten = _steer; _steerWrittenAt = Time.time;
                _nview.GetZDO().Set(_steerKey, _steer);   // (what every other game draws the front wheels from)
            }
        }

        /// <summary>The owner's game: the wheels meet the ground again after a jump (a thump and a puff of dust, more for a harder landing).</summary>
        private void Landed(float speed)
        {
            if (speed < 2.5f) return;
            for (int i = 0; i < _wheel.Length; i++) _foot[i] = _wheel[i] != null ? _wheel[i].position - transform.up * (Spec.WheelRadius * 0.85f) : transform.position;
            _dust?.Burst(_foot, Mathf.Clamp((int)(speed * 2.5f), 6, 30));
            _sound?.Thud(speed / 12f);
        }

        // ---- running things over ------------------------------------------------------------------------------------------

        private static readonly Collider[] RamHits = new Collider[24];
        private readonly Dictionary<Character, float> _rammed = new Dictionary<Character, float>();
        private static int _characterMask = -1;

        /// <summary>
        /// The rider's game (the owner): an enemy in the box in front of it takes blunt damage for each metre a second over 3 (once every
        /// 0.8 s each), is knocked away, and the vehicle loses a little speed. Players, tamed animals and friends are never hurt.
        /// </summary>
        private void Ram(float along)
        {
            float per = Spec.RamDamage();
            Player rider = Player.m_localPlayer;
            if (per <= 0f || rider == null || GetUser() != rider.GetZDOID().UserID) return;
            if (_characterMask < 0) _characterMask = LayerMask.GetMask("character", "character_net");
            Transform t = transform;
            int n = Physics.OverlapBoxNonAlloc(t.TransformPoint(Spec.RamCenter), Spec.RamSize * 0.5f, RamHits, t.rotation, _characterMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Character c = RamHits[i].GetComponentInParent<Character>();
                if (c == null || c.IsPlayer() || c.IsDead() || c.IsTamed() || !BaseAI.IsEnemy(rider, c)) continue;
                if (_rammed.TryGetValue(c, out float until) && Time.time < until) continue;
                _rammed[c] = Time.time + 0.8f;
                var hit = new HitData();
                hit.m_damage.m_blunt = (along - 3f) * per;
                hit.m_pushForce = along * 14f;
                hit.m_point = c.GetCenterPoint();
                hit.m_dir = Vector3.ProjectOnPlane(c.transform.position - t.position, Vector3.up).normalized + Vector3.up * 0.25f;
                hit.m_attacker = rider.GetZDOID();
                c.Damage(hit);
                _body.linearVelocity *= 0.9f;
            }
            if (_rammed.Count > 40) foreach (Character dead in _rammed.Where(kv => kv.Key == null || Time.time > kv.Value).Select(kv => kv.Key).ToList()) _rammed.Remove(dead);
        }

        /// <summary>Someone is on it (a player with the controls, in range): it can't be taken apart with the hammer meanwhile.</summary>
        internal bool Occupied()
        {
            long user = GetUser();
            if (user == 0L) return false;
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && p.GetZDOID().UserID == user && Vector3.Distance(p.transform.position, transform.position) < 12f) return true;
            return false;
        }


        // ---- every game: the wheels ---------------------------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !_nview.IsValid()) return;
            EnsureId();
            Vector3 pos = transform.position;
            _velocity = Vector3.Lerp(_velocity, (pos - _lastPos) / dt, Mathf.Clamp01(dt * 6f));
            _lastPos = pos;
            float along = Vector3.Dot(_velocity, transform.forward);
            Speed = along;

            float steer = _nview.IsOwner() ? _steer : _nview.GetZDO().GetFloat(_steerKey, 0f);
            if (!_validUser && _nview.IsOwner()) steer = 0f;
            _steerShown = Mathf.MoveTowards(_steerShown, steer, 6f * dt);
            _spinAngle = (_spinAngle + along / Mathf.Max(0.05f, Spec.WheelRadius) * Mathf.Rad2Deg * dt) % 360f;

            for (int i = 0; i < _wheel.Length; i++)
            {
                Transform w = _wheel[i];
                if (w == null) continue;
                w.localPosition = Spec.Wheels[i] + Vector3.up * (Spec.Travel * 0.5f - _drop[i]);
                w.localRotation = Quaternion.Euler(0f, i < Spec.Front ? _steerShown * 28f : 0f, 0f);
                if (_spin[i] != null) _spin[i].localRotation = Quaternion.Euler(_spinAngle, 0f, 0f);
            }

            // how fast it turns, from how it is pointing (any game can tell), and so how far a bike leans into it
            float yawDeg = transform.eulerAngles.y;
            _yawRate = Mathf.Lerp(_yawRate, Mathf.DeltaAngle(_lastYaw, yawDeg) * Mathf.Deg2Rad / dt, Mathf.Clamp01(dt * 8f));
            _lastYaw = yawDeg;
            Lean = Mathf.Clamp(-Mathf.Atan2(along * _yawRate, 22f) * Mathf.Rad2Deg, -32f, 32f);
            if (_lean != null)
                _lean.localRotation = Quaternion.Slerp(_lean.localRotation, Quaternion.Euler(0f, 0f, Spec.LeansWithTurn ? Lean : 0f), Mathf.Clamp01(dt * 8f));

            // lights: on when someone rides it and it is dark (night, or in a cave); the tail lights brighten when braking
            bool ridden = GetUser() != 0L;
            bool dark = !EnvMan.IsDaylight() || (Player.m_localPlayer != null && Player.m_localPlayer.InInterior());
            bool on = ridden && dark;
            bool braking = ridden && Gear >= 3;
            foreach (Light l in _headLights) l.enabled = on;
            foreach (Light l in _tailLights) { l.enabled = ridden && (dark || braking); l.intensity = braking ? 1.6f : 0.5f; }
            Glow(_headLens, on ? 2.2f : 0f, new Color(1f, 0.93f, 0.75f));
            Glow(_tailLens, ridden ? (braking ? 3f : (dark ? 1f : 0.35f)) : 0f, new Color(1f, 0.1f, 0.05f));

            // handlebars turn with the steering; pedals turn when it is pedalled (the pedals stay flat as the cranks go round)
            if (_bars != null) _bars.localRotation = Quaternion.Euler(0f, _steerShown * 30f, 0f);
            if (_cranks != null)
            {
                if (ridden && along > 0.8f) _crankAngle = (_crankAngle + along * 0.9f * Mathf.Rad2Deg * dt) % 360f;
                _cranks.localRotation = Quaternion.Euler(_crankAngle, 0f, 0f);
                foreach (Transform p in _pedals) p.localRotation = Quaternion.Euler(-_crankAngle, 0f, 0f);
            }

            // a kickstand (a bike's): down while it is parked, tucked up while someone rides it
            if (_kickstand != null)
                _kickstand.localRotation = Quaternion.Slerp(_kickstand.localRotation, Quaternion.Euler(ridden ? 80f : 0f, 0f, 0f), Mathf.Clamp01(dt * 7f));

            // the colour it was painted (E on its paint spot)
            if (Spec.Paints != null && Spec.Paints.Length > 0 && Time.time > _paintReadAt)
            {
                _paintReadAt = Time.time + 0.5f;
                int paint = _nview.GetZDO().GetInt(_paintKey, 0) % Spec.Paints.Length;
                if (paint != _paintShown)
                {
                    _paintShown = paint;
                    foreach (Renderer r in _paintParts)
                    {
                        if (r == null) continue;
                        r.GetPropertyBlock(_block);
                        _block.SetColor("_Color", Spec.Paints[paint]);
                        r.SetPropertyBlock(_block);
                    }
                }
            }

            // the dust: from the bottom of each wheel in contact with the ground
            if (_dust != null)
            {
                for (int i = 0; i < _wheel.Length; i++)
                {
                    _foot[i] = _wheel[i] != null ? _wheel[i].position - transform.up * (Spec.WheelRadius * 0.85f) : transform.position;
                    if (!_nview.IsOwner()) _contact[i] = Mathf.Abs(_velocity.y) < 2f;   // (only the owner knows the springs: the rest take it as on the ground while it rolls along)
                }
                bool spinning = ridden && Gear >= 1 && Mathf.Abs(along) < 3.5f && _nview.IsOwner();
                _dust.Tick(_foot, _contact, Mathf.Clamp01(Mathf.Abs(along) / Mathf.Max(1f, Spec.Top())), Mathf.Abs(_steerShown), spinning);
            }

            float cornering = Mathf.Abs(along * _yawRate);   // (sideways acceleration, m/s^2)
            float slip = Mathf.Clamp01((cornering - 0.5f * Spec.LateralAccel) / (0.5f * Spec.LateralAccel));
            _sound?.Tick(ridden, Mathf.Clamp01(Mathf.Abs(along) / Mathf.Max(1f, Spec.Top())), _nview.IsOwner() ? _wheelsOnGround > 0f : true, slip);
        }

        private void Glow(Renderer[] lenses, float strength, Color colour)
        {
            foreach (Renderer r in lenses)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, colour * strength);
                r.SetPropertyBlock(_block);
            }
        }
    }

    /// <summary>
    /// The sounds of a land vehicle, made in code (no sound files): a one-cylinder engine thump for those with an engine, and the roll of
    /// the tyres on the ground (louder with speed) for all. They go through the game's own mixer so its volume settings apply. A dedicated
    /// server makes none of this.
    /// </summary>
    internal sealed class VehicleSound : IDisposable
    {
        private static AudioClip _engineClip, _rollClip, _skidClip, _thudClip;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;
        private static int _mixerTries;
        // (AudioClip.SetData has an overload the build can't see the types of: called by name)
        private static readonly System.Reflection.MethodInfo SetData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        private readonly VehicleSpec _spec;
        private readonly GameObject _go;
        private AudioSource _engine, _roll, _skid;
        private float _rev;
        private int _gear;

        internal VehicleSound(Transform root, VehicleSpec spec)
        {
            _spec = spec;
            _go = new GameObject("Sounds");
            _go.transform.SetParent(root, false);
            _go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            try
            {
                if (spec.Engine) _engine = Make("Engine", EngineClip());
                _roll = Make("Roll", RollClip());
                _skid = Make("Skid", SkidClip());
            }
            catch (Exception e) { Debug.LogWarning("[" + spec.Id + "] no sound: " + e.Message); }
        }

        private AudioSource Make(string name, AudioClip clip)
        {
            var s = _go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 4f;
            s.maxDistance = 45f;
            s.dopplerLevel = 0.3f;
            if (_mixer == null && _mixerTries++ < 20)
                foreach (AudioSource like in Resources.FindObjectsOfTypeAll<AudioSource>())
                    if (like != null && like.outputAudioMixerGroup != null) { _mixer = like.outputAudioMixerGroup; break; }
            if (_mixer != null) s.outputAudioMixerGroup = _mixer;
            return s;
        }

        /// <summary>
        /// Every frame: the engine climbs through four gears (the revs fall back at each change), the tyres roll louder with speed and
        /// squeal when cornering hard (<paramref name="slip"/>: 0 to 1).
        /// </summary>
        internal void Tick(bool ridden, float speedFraction, bool grounded, float slip = 0f)
        {
            float dt = Time.deltaTime;
            if (_engine != null)
            {
                float g = speedFraction * 4.2f;
                int gear = Mathf.Min(3, (int)g);
                float inGear = Mathf.Clamp01(g - gear);
                if (ridden && gear > _gear) _rev = Mathf.Max(0.1f, _rev - 0.3f);   // (a gear change: the revs drop, then climb again)
                _gear = ridden ? gear : 0;
                float target = ridden ? 0.2f + 0.8f * inGear : 0f;
                _rev = Mathf.MoveTowards(_rev, target, dt * (target > _rev ? 2.4f : 1.2f));
                float vol = ridden ? (0.14f + 0.4f * _rev) * (_spec.EngineVolume != null ? _spec.EngineVolume() : 1f) : 0f;
                _engine.volume = Mathf.MoveTowards(_engine.volume, vol, dt * 0.8f);
                _engine.pitch = 0.7f + 1.6f * _rev + Mathf.Sin(Time.time * 6.1f) * 0.015f;
                Run(_engine);
            }
            if (_roll != null)
            {
                float vol = grounded ? Mathf.Clamp01(speedFraction * 1.6f) * 0.3f : 0f;
                _roll.volume = Mathf.MoveTowards(_roll.volume, vol, dt * 1.2f);
                _roll.pitch = 0.75f + 0.7f * speedFraction;
                Run(_roll);
            }
            if (_skid != null)
            {
                float vol = grounded ? slip * 0.35f : 0f;
                _skid.volume = Mathf.MoveTowards(_skid.volume, vol, dt * 3f);
                _skid.pitch = 0.9f + 0.3f * speedFraction;
                Run(_skid);
            }
        }

        /// <summary>A landing after a jump: a thump, louder for a harder landing.</summary>
        internal void Thud(float strength)
        {
            if (_roll == null) return;
            try { _roll.PlayOneShot(ThudClip(), Mathf.Clamp01(strength)); }
            catch (Exception) { }
        }

        private static void Run(AudioSource s)
        {
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
        }

        /// <summary>A second of a one-cylinder four-stroke: 26 slow thumps, each a low tone and a puff of noise dying away, softened. Loops cleanly (whole thumps); the pitch sets how fast it runs.</summary>
        private static AudioClip EngineClip()
        {
            if (_engineClip != null) return _engineClip;
            const int rate = 44100, firings = 26, per = rate / firings, n = per * firings;
            var data = new float[n];
            var rnd = new System.Random(11);
            for (int f = 0; f < firings; f++)
            {
                float amp = 0.8f + 0.2f * (float)rnd.NextDouble(), tone = 88f + 14f * (float)rnd.NextDouble();
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)rate, env = Mathf.Exp(-t / 0.014f);
                    float noise = (float)rnd.NextDouble() * 2f - 1f;
                    data[f * per + i] = amp * env * (0.8f * Mathf.Sin(2f * Mathf.PI * tone * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * tone * t) + 0.3f * noise);
                }
            }
            float lp = 0f, lp2 = 0f, peak = 0.001f;
            for (int k = 0; k < n; k++)
            {
                data[k] += 0.12f * Mathf.Sin(2f * Mathf.PI * firings * k / rate);
                lp += (data[k] - lp) * 0.25f;
                lp2 += (lp - lp2) * 0.3f;
                data[k] = lp2;
                peak = Mathf.Max(peak, Mathf.Abs(lp2));
            }
            for (int k = 0; k < n; k++) data[k] *= 0.8f / peak;
            _engineClip = AudioClip.Create("dh_vehicle_engine", n, 1, rate, false);
            SetData.Invoke(_engineClip, new object[] { data, 0 });
            return _engineClip;
        }

        /// <summary>A second of tyre squeal: noise with the low and high ends taken off (a band round a kilohertz), loops cleanly.</summary>
        private static AudioClip SkidClip()
        {
            if (_skidClip != null) return _skidClip;
            const int rate = 22050, n = rate * 2, fade = 2000;
            var data = new float[n];
            var rnd = new System.Random(9);
            float lo = 0f, lo2 = 0f, peak = 0.001f;
            for (int k = 0; k < n; k++)
            {
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lo += (white - lo) * 0.5f;          // (the top end off)
                lo2 += (white - lo2) * 0.08f;       // (the low end, taken away again)
                data[k] = (lo - lo2) * (0.75f + 0.25f * Mathf.Sin(k * 0.0031f));
                peak = Mathf.Max(peak, Mathf.Abs(data[k]));
            }
            for (int k = 0; k < fade; k++) { float a = k / (float)fade; data[k] = data[k] * a + data[n - fade + k] * (1f - a); }
            var cut = new float[n - fade];
            System.Array.Copy(data, fade, cut, 0, n - fade);
            for (int k = 0; k < cut.Length; k++) cut[k] *= 0.8f / peak;
            _skidClip = AudioClip.Create("dh_vehicle_skid", n - fade, 1, rate, false);
            SetData.Invoke(_skidClip, new object[] { cut, 0 });
            return _skidClip;
        }

        /// <summary>A short low thump: a sine that falls from 90 to 40 Hz with a puff of noise, dying away in a third of a second.</summary>
        private static AudioClip ThudClip()
        {
            if (_thudClip != null) return _thudClip;
            const int rate = 22050, n = rate / 3;
            var data = new float[n];
            var rnd = new System.Random(3);
            float phase = 0f, lp = 0f;
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)rate, env = Mathf.Exp(-t / 0.07f);
                float freq = Mathf.Lerp(90f, 40f, Mathf.Clamp01(t / 0.2f));
                phase += 2f * Mathf.PI * freq / rate;
                lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.25f;
                data[k] = env * (0.9f * Mathf.Sin(phase) + 0.5f * lp);
            }
            _thudClip = AudioClip.Create("dh_vehicle_thud", n, 1, rate, false);
            SetData.Invoke(_thudClip, new object[] { data, 0 });
            return _thudClip;
        }

        /// <summary>Two seconds of tyres on dirt: softened noise, the end faded into the start so it loops without a click.</summary>
        private static AudioClip RollClip()
        {
            if (_rollClip != null) return _rollClip;
            const int rate = 22050, n = rate * 2, fade = 2000;
            var data = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f, lp2 = 0f, peak = 0.001f;
            for (int k = 0; k < n; k++)
            {
                lp += (((float)rnd.NextDouble() * 2f - 1f) - lp) * 0.18f;
                lp2 += (lp - lp2) * 0.4f;
                data[k] = lp2;
            }
            for (int k = 0; k < fade; k++)
            {
                float a = k / (float)fade;
                data[k] = data[k] * a + data[n - fade + k] * (1f - a);
            }
            for (int k = 0; k < n; k++) peak = Mathf.Max(peak, Mathf.Abs(data[k]));
            for (int k = 0; k < n; k++) data[k] *= 0.8f / peak;
            _rollClip = AudioClip.Create("dh_vehicle_roll", n - fade, 1, rate, false);
            var cut = new float[n - fade];
            System.Array.Copy(data, fade, cut, 0, n - fade);
            SetData.Invoke(_rollClip, new object[] { cut, 0 });
            return _rollClip;
        }
    }

    /// <summary>
    /// The dust a land vehicle throws up: a soft puff of the game's own splash mist (the Jet Ski's spray material) from the bottom of each
    /// wheel in contact with the ground, tinted to the ground it is on (brown earth, pale sand on the plains, white snow in the mountains,
    /// grey ash), more of it the faster it goes, in hard turns and when the wheels spin up from a standstill. A dedicated server makes none.
    /// </summary>
    internal sealed class VehicleDust : IDisposable
    {
        private readonly ParticleSystem[] _puffs;
        private readonly GameObject _root;
        private Color _colour = new Color(0.55f, 0.47f, 0.34f, 0.7f);
        private float _biomeAt;
        private readonly float _size;

        internal VehicleDust(Transform vehicle, int wheels, float size)
        {
            _size = size;
            GameObject splash = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("fx_float_hitwater") : null;
            Material mist = splash != null ? splash.GetComponent<ParticleSystemRenderer>()?.sharedMaterial : null;
            _root = new GameObject("Dust");
            _root.transform.SetParent(vehicle, false);
            _puffs = new ParticleSystem[mist != null ? wheels : 0];
            for (int i = 0; i < _puffs.Length; i++)
            {
                var go = new GameObject("dust" + i);
                go.transform.SetParent(_root.transform, false);
                go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // (a cone points along its z: up)
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop();
                ParticleSystem.MainModule main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.22f * size, 0.5f * size);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
                main.gravityModifier = -0.04f;
                main.maxParticles = 70;
                ParticleSystem.EmissionModule emit = ps.emission;
                emit.rateOverTime = 0f;
                ParticleSystem.ShapeModule shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 28f;
                shape.radius = 0.1f * size;
                ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
                col.enabled = true;
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(fade);
                ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 2.3f)));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mist;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _puffs[i] = ps;
            }
        }

        private static Color Ground(Vector3 at)
        {
            switch (Heightmap.FindBiome(at))
            {
                case Heightmap.Biome.Meadows: return new Color(0.58f, 0.5f, 0.36f, 0.7f);
                case Heightmap.Biome.BlackForest: return new Color(0.42f, 0.36f, 0.28f, 0.7f);
                case Heightmap.Biome.Swamp: return new Color(0.34f, 0.32f, 0.25f, 0.7f);
                case Heightmap.Biome.Mountain: case Heightmap.Biome.DeepNorth: return new Color(0.94f, 0.96f, 1f, 0.8f);
                case Heightmap.Biome.Plains: return new Color(0.76f, 0.63f, 0.4f, 0.7f);
                case Heightmap.Biome.AshLands: return new Color(0.3f, 0.28f, 0.28f, 0.75f);
                default: return new Color(0.55f, 0.55f, 0.58f, 0.6f);
            }
        }

        /// <summary>Every frame: <paramref name="foot"/> the bottom of each wheel, <paramref name="onGround"/> which touch the ground now.</summary>
        internal void Tick(Vector3[] foot, bool[] onGround, float speedFraction, float turning, bool spinning)
        {
            if (_puffs.Length == 0) return;
            if (Time.time > _biomeAt)
            {
                _biomeAt = Time.time + 0.6f;
                _colour = Ground(foot[0]);
            }
            float rate = speedFraction > 0.08f ? (4f + 22f * speedFraction + 28f * turning * speedFraction) * _size : 0f;
            if (spinning) rate = Mathf.Max(rate, 20f * _size);
            for (int i = 0; i < _puffs.Length && i < foot.Length; i++)
            {
                ParticleSystem ps = _puffs[i];
                ps.transform.position = foot[i];
                ParticleSystem.MainModule main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(_colour);
                ParticleSystem.EmissionModule e = ps.emission;
                e.rateOverTime = onGround[i] ? rate : 0f;
                if (e.rateOverTime.constant > 0f && !ps.isPlaying) ps.Play();
            }
        }

        /// <summary>A landing: a puff from every wheel at once, <paramref name="count"/> particles each.</summary>
        internal void Burst(Vector3[] foot, int count)
        {
            for (int i = 0; i < _puffs.Length && i < foot.Length; i++)
            {
                ParticleSystem ps = _puffs[i];
                ps.transform.position = foot[i];
                ParticleSystem.MainModule main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(_colour);
                ps.Emit(count);
            }
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }
    }

    /// <summary>
    /// A spot on a vehicle (the hood, the frame): look at it and press E to give the vehicle the next colour, for everyone. Its collider is
    /// in the Model, on a layer nothing bumps into; it only passes the press on to <see cref="GroundVehicle"/> (which owns the saved colour).
    /// </summary>
    internal class VehiclePaintSpot : MonoBehaviour, Interactable, Hoverable
    {
        public float GetHoverOffset() => 0f;
        public string GetHoverName() => Localization.instance.Localize(GroundVehicle.Spec != null ? GroundVehicle.Spec.PaintKey : "");
        public string GetHoverText() => Localization.instance.Localize("[<color=yellow><b>$KEY_Use</b></color>] " + GetHoverName());
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            GetComponentInParent<GroundVehicle>()?.NextPaint(user as Player);
            return true;
        }
    }

    /// <summary>Making the piece itself and drawing it with plain shapes (used by FourWheelers' and MountainBikes' Things.cs and Model.cs).</summary>
    internal static class VehicleKit
    {
        /// <summary>
        /// A copy of the game's Raft with everything of a boat taken off: what's left is what any moving, saved piece needs (the networked
        /// object that follows its owner, a rigid body, the piece with its cost and hammer-menu entry, wear). The body, seats and wheels are
        /// the mod's to add. <paramref name="layer"/>: the layer for its solid parts (the raft's own, "vehicle").
        /// </summary>
        internal static GameObject CloneBase(ZNetScene scene, Transform holder, string name, out int layer)
        {
            layer = 0;
            GameObject raft = scene.GetPrefab("Raft");
            if (raft == null) return null;
            layer = raft.transform.Find("ship/colliders/Cube")?.gameObject.layer ?? raft.layer;
            GameObject go = UnityEngine.Object.Instantiate(raft, holder);
            go.name = name;
            foreach (Transform c in go.transform.Cast<Transform>().ToList()) UnityEngine.Object.DestroyImmediate(c.gameObject);
            foreach (Component comp in go.GetComponents<Component>().ToList())
            {
                if (comp is Transform || comp is ZNetView || comp is ZSyncTransform || comp is Rigidbody || comp is Piece || comp is WearNTear) continue;
                UnityEngine.Object.DestroyImmediate(comp);
            }
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_new = null; wear.m_worn = null; wear.m_broken = null;
                wear.m_supports = false;
                wear.m_noRoofWear = false;
                wear.m_noSupportWear = false;
            }
            ZSyncTransform sync = go.GetComponent<ZSyncTransform>();
            if (sync != null) { sync.m_syncPosition = true; sync.m_syncRotation = true; sync.m_syncBodyVelocity = true; }
            Rigidbody body = go.GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            Piece piece = go.GetComponent<Piece>();
            piece.m_waterPiece = false;
            piece.m_groundOnly = false;
            piece.m_noInWater = true;
            piece.m_canBeRemoved = true;   // (the raft's is false: the hammer would ignore it)
            return go;
        }

        /// <summary>A solid box on the vehicle (its hit box: what you bump into and look at to press E).</summary>
        internal static BoxCollider Box(Transform parent, string name, Vector3 centre, Vector3 size, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            return box;
        }

        /// <summary>A visible shape without a collider.</summary>
        internal static Transform Shape(Transform parent, PrimitiveType type, string name, Vector3 at, Vector3 size, Material material, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = size;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        /// <summary>A tube between two points (a bike's frame): a cylinder laid from a to b.</summary>
        internal static Transform Bar(Transform parent, string name, Vector3 a, Vector3 b, float thickness, Material material)
        {
            Vector3 d = b - a;
            Transform t = Shape(parent, PrimitiveType.Cylinder, name, (a + b) * 0.5f, new Vector3(thickness, d.magnitude * 0.5f, thickness), material);
            if (d.sqrMagnitude > 1e-8f) t.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            return t;
        }

        /// <summary>
        /// A tapering tube from a to b (radius r0 at a, r1 at b), with a ball at each end when <paramref name="joints"/> is set, so tubes
        /// meeting at a point look welded (a bike's frame).
        /// </summary>
        internal static Transform Tube(Transform parent, string name, Vector3 a, Vector3 b, float r0, float r1, Material material, bool joints = true)
        {
            Vector3 d = b - a;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = a;
            if (d.sqrMagnitude > 1e-8f) go.transform.localRotation = Quaternion.LookRotation(d.normalized, Mathf.Abs(d.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
            go.AddComponent<MeshFilter>().sharedMesh = VehicleMesh.Tube(d.magnitude, r0, r1);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (joints)
            {
                Shape(parent, PrimitiveType.Sphere, name + "_ja", a, Vector3.one * r0 * 2f, material);
                Shape(parent, PrimitiveType.Sphere, name + "_jb", b, Vector3.one * r1 * 2f, material);
            }
            return go.transform;
        }

        /// <summary>A wheel at its rest position: wheelN steers and rides the suspension, its "spin" child turns. The tyre's axle is along x.</summary>
        internal static Transform Wheel(Transform parent, int index, Vector3 hub, float radius, float width, Material tyre, Material rim, float rimFraction = 0.575f)
        {
            var w = new GameObject("wheel" + index).transform;
            w.SetParent(parent, false);
            w.localPosition = hub;
            var spin = new GameObject("spin").transform;
            spin.SetParent(w, false);
            Shape(spin, PrimitiveType.Cylinder, "tyre", Vector3.zero, new Vector3(radius * 2f, width * 0.5f, radius * 2f), tyre, new Vector3(0f, 0f, 90f));
            Shape(spin, PrimitiveType.Cylinder, "rim", Vector3.zero, new Vector3(radius * 2f * rimFraction, width * 0.56f, radius * 2f * rimFraction), rim, new Vector3(0f, 0f, 90f));
            Shape(spin, PrimitiveType.Cube, "spoke", Vector3.zero, new Vector3(width * 0.62f, radius * 2f * rimFraction * 0.9f, radius * 0.1f), rim);   // (so the turning shows)
            return w;
        }
    }

    /// <summary>
    /// Plain-coloured materials for code-built vehicles. Nothing here uses Shader.Find (the game strips shaders it doesn't use, and a
    /// dedicated server draws nothing): every material starts from one the game already has, the matte flat metal of its cauldron chain
    /// (the way JetSkis' Look.cs does it).
    /// </summary>
    internal static class VehicleLook
    {
        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();

        private static readonly Dictionary<string, Color> Colours = new Dictionary<string, Color>
        {
            { "Tyre", new Color(0.06f, 0.06f, 0.07f) }, { "Chrome", new Color(0.78f, 0.8f, 0.84f) }, { "Frame", new Color(0.14f, 0.14f, 0.16f) },
            { "Seat", new Color(0.12f, 0.08f, 0.05f) }, { "Red", new Color(0.66f, 0.10f, 0.08f) }, { "Green", new Color(0.18f, 0.5f, 0.25f) },
            { "Blue", new Color(0.2f, 0.35f, 0.7f) }, { "Cream", new Color(0.95f, 0.92f, 0.84f) }, { "Bronze", new Color(0.66f, 0.43f, 0.2f) },
            { "Grey", new Color(0.4f, 0.4f, 0.43f) }, { "Silver", new Color(0.62f, 0.64f, 0.68f) }, { "LensHead", new Color(0.95f, 0.92f, 0.8f) }, { "LensTail", new Color(0.6f, 0.05f, 0.04f) },
        };
        private static readonly HashSet<string> Metals = new HashSet<string> { "Chrome", "Bronze", "Silver" };

        internal static void Harvest(ZNetScene scene, Material fallbackBase)
        {
            Made.Clear();
            Material flat = null;
            GameObject cauldron = scene.GetPrefab("piece_cauldron");
            if (cauldron != null)
                foreach (Renderer r in cauldron.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        if (flat == null && m != null && m.name == "cauldron_chain") flat = m;
            Material basis = flat != null ? flat : fallbackBase;
            foreach (KeyValuePair<string, Color> kv in Colours)
            {
                var m = new Material(basis) { color = kv.Value };
                if (m.HasProperty("_MainTex")) m.mainTexture = null;
                bool metal = Metals.Contains(kv.Key);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal ? 0.8f : 0f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", metal ? 0.55f : 0.2f);
                if (m.HasProperty("_MetallicAlphaGloss")) m.SetFloat("_MetallicAlphaGloss", metal ? 0.55f : 0.2f);
                if (m.HasProperty("_ValueNoise")) m.SetFloat("_ValueNoise", 0f);
                foreach (string tex in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_DetailAlbedoMap", "_DetailNormalMap", "_OcclusionMap" })
                    if (m.HasProperty(tex)) m.SetTexture(tex, null);   // (the base's grain: it speckles a flat colour)
                if (kv.Key.StartsWith("Lens")) m.EnableKeyword("_EMISSION");   // (lit by a property block when the lights are on)
                Made[kv.Key] = m;
            }
        }

        internal static Material Of(string name) => Made.TryGetValue(name, out Material m) ? m : Made.Values.FirstOrDefault();

        internal static void Clear() => Made.Clear();
    }
}

namespace DHack.Shared
{
    /// <summary>The hammer can't take a vehicle apart with someone on it (the game says it can't be removed now).</summary>
    [HarmonyPatch(typeof(Piece), nameof(Piece.CanBeRemoved))]
    internal static class Piece_CanBeRemoved_Vehicle
    {
        private static void Postfix(Piece __instance, ref bool __result)
        {
            if (!__result) return;
            GroundVehicle v = __instance.GetComponent<GroundVehicle>();
            if (v != null && v.Occupied()) __result = false;
        }
    }
}
