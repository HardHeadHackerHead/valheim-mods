using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// Hold a key next to a boat (while you are not in it) to shove it the way the camera is facing, so beaching and
    /// launching a boat is not a slow shuffle. The push is gentle and has a top speed, so it is a nudge, not a motor.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _pushEnabled;
        private ConfigEntry<KeyCode> _pushKey;
        private ConfigEntry<float> _pushStrength, _pushMaxSpeed, _pushRange, _pushSeconds;

        private Ship[] _ships = new Ship[0];
        private float _nextShipScan;
        private Ship _pushing;

        private void BindBoatPushConfig()
        {
            _pushEnabled = Config.Bind("BoatPush", "Enabled", true, "Turn boat pushing on or off.");
            _pushKey = Config.Bind("BoatPush", "Key", KeyCode.G, "Tap (or hold) this next to a boat, not in it, to push it the way you are looking.");
            _pushStrength = Config.Bind("BoatPush", "Strength", 3f, new ConfigDescription(
                "How hard you push (metres per second, per second). Higher gets a heavy boat moving faster.", new AcceptableValueRange<float>(0.5f, 10f)));
            _pushMaxSpeed = Config.Bind("BoatPush", "MaxSpeed", 2.5f, new ConfigDescription(
                "The fastest your pushing can move a boat (metres per second). Walking pace is about 5.", new AcceptableValueRange<float>(0.5f, 6f)));
            _pushSeconds = Config.Bind("BoatPush", "Seconds", 3f, new ConfigDescription(
                "One tap of the key keeps pushing for this long (holding it keeps pushing as long as you hold).", new AcceptableValueRange<float>(0.5f, 10f)));
            _pushRange = Config.Bind("BoatPush", "Range", 3f, new ConfigDescription(
                "How close you have to be to the boat's hull (metres).", new AcceptableValueRange<float>(1f, 6f)));
        }

        private float _pushUntil;

        private void UpdateBoatPush(Player player)
        {
            if (!_pushEnabled.Value || _pushKey.Value == KeyCode.None) { _pushing = null; return; }
            bool pressed = Input.GetKeyDown(_pushKey.Value), held = Input.GetKey(_pushKey.Value);
            if (!pressed && !held)
            {
                if (Time.time >= _pushUntil) _pushing = null;
                return;
            }
            if (TypingOrMenuOpen() || InventoryGui.IsVisible()) return;
            if (player.IsAttached() || player.GetControlledShip() != null || player.GetStandingOnShip() != null)
            {
                if (pressed) Tell(player, "Step off the boat to push it");
                return;
            }

            if (pressed || Time.time >= _nextShipScan)
            {
                _nextShipScan = Time.time + 1f;
                _ships = FindObjectsOfType<Ship>();
            }

            Ship best = null;
            float bestDistance = _pushRange.Value;
            float nearest = float.MaxValue;
            foreach (Ship ship in _ships)
            {
                if (ship == null) continue; // (not IsPlayerInBoat: that is true anywhere inside the boat's big "onboard" zone, even on the beach next to it)
                float d = DistanceToHull(ship, player.transform.position);
                nearest = Mathf.Min(nearest, d);
                if (d < bestDistance) { bestDistance = d; best = ship; }
            }
            if (best == null)
            {
                if (pressed)
                {
                    Tell(player, nearest < float.MaxValue ? "Too far from the boat to push it" : "No boat nearby");
                    Logger.LogInfo($"Boat push: no boat in range (ships found: {_ships.Length}, nearest hull {nearest:0.0} m, range {_pushRange.Value:0.0} m)");
                }
                return;
            }

            _pushing = best;
            _pushUntil = Time.time + _pushSeconds.Value; // one press keeps pushing for a few seconds; hold the key to keep going
            ZNetView view = best.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership(); // only the owner's game moves the boat
            if (pressed) Logger.LogInfo($"Boat push: pushing {best.name}, hull {bestDistance:0.0} m away, owner: {(view != null && view.IsValid() && view.IsOwner())}");
        }

        private static float DistanceToHull(Ship ship, Vector3 point)
        {
            // Solid colliders if the boat has any; otherwise what it looks like; otherwise just where it is.
            Bounds bounds = new Bounds(ship.transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider c in ship.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            if (!any)
                foreach (Renderer r in ship.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
            return Vector3.Distance(bounds.ClosestPoint(point), point);
        }

        private void FixedUpdate()
        {
            Ship ship = _pushing;
            Player player = Player.m_localPlayer;
            if (ship == null || player == null || Time.time > _pushUntil) return;

            ZNetView view = ship.GetComponent<ZNetView>();
            Rigidbody body = ship.GetComponent<Rigidbody>();
            if (body == null || view == null || !view.IsValid() || !view.IsOwner()) return;

            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : (Camera.main != null ? Camera.main.transform : null);
            if (cam == null) return;
            Vector3 dir = cam.forward; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();

            // Add speed along the push direction until the cap, then stop adding (the boat's own drag slows it as usual).
            float along = Vector3.Dot(body.velocity, dir);
            if (along >= _pushMaxSpeed.Value) return;
            body.AddForce(dir * _pushStrength.Value, ForceMode.Acceleration);
        }
    }
}
