using HarmonyLib;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// The ride. You are attached to a little carriage (the game's own way of putting a player on a seat or a ship, so you move with it and the
    /// others see you glide) that runs along the rope from one post to the other: it lifts you up to the rope, speeds up (faster downhill, a
    /// minimum on the flat and uphill), and eases you down to the ground at the far end. Jump to let go. Done on the game of the player who rides.
    /// </summary>
    internal static class Ride
    {
        private static Player _rider;
        private static Transform _trolley;
        private static Vector3 _a, _b, _start, _land;
        private static float _t, _speed, _length, _phaseAt, _boost, _waitedForGround;
        private static int _phase;   // 0 lifting on, 1 riding, 2 coming down
        private static AudioSource _wind;
        private static AudioClip _windClip;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;

        /// <summary>Where the rope is above the carriage now.</summary>
        public static Vector3 RopePoint;

        public static bool Active => _rider != null;

        /// <summary>Every frame after the animation has posed the rider: arms up and legs hanging, the handle in the hands.</summary>
        public static void Late()
        {
            if (_rider == null || _trolley == null) return;
            Vector3 forward = Flat(_b - _a), right = Vector3.Cross(Vector3.up, forward);
            Vector3 centre = RopePoint - Vector3.up * Grip.HandleDrop;   // (the handle hangs straight down from the rope)
            bool vertical = Grip.IsAxe;
            Pose.Apply(_trolley, centre, vertical ? Vector3.up : right, vertical);
            Grip.Update(_trolley, RopePoint, centre);
        }
        public static float Speed => _speed;

        public static void Start(Player player, Post from, Vector3 toAnchor, Post to)
        {
            Plugin.Log.LogInfo($"Ride asked for: active {Active}, dead {player.IsDead()}, attached {player.IsAttached()}, building {player.InPlaceMode()}, swimming {player.IsSwimming()}");
            ItemDrop.ItemData axe = Grip.FindAxe(player);
            if (Plugin.NeedAxe.Value && axe == null && !Active) { player.Message(MessageHud.MessageType.Center, "You need an axe to ride: hook it over the rope and hang from its handle."); return; }
            if (Active) { Plugin.Log.LogInfo("  already riding (a ride that did not end?)"); return; }
            if (player.IsDead() || player.IsAttached() || player.InPlaceMode() || player.IsSwimming())
            {
                player.Message(MessageHud.MessageType.Center, player.InPlaceMode() ? "Put your hammer away to ride." : "You cannot ride just now.");
                return;
            }
            _a = Line.Anchor(from); _b = toAnchor;
            if (_a.y - _b.y < 0.5f) { player.Message(MessageHud.MessageType.Center, "A zipline only runs downhill: ride this one from its other end."); return; }
            string why = Line.Clearance(_a, _b, from, to);
            if (why != null) { Plugin.Log.LogInfo("  refused: " + why); player.Message(MessageHud.MessageType.Center, why); return; }
            Plugin.Log.LogInfo($"  riding {Vector3.Distance(_a, _b):0} m");

            _length = Vector3.Distance(_a, _b);
            _boost = Plugin.LongFaster.Value ? Mathf.Clamp(1f + _length / 300f, 1f, 8f) : 1f;   // (a line of kilometres is flown at many times the speed)
            _t = Mathf.Min(0.5f, 0.9f / _length);                 // (a step along the rope, clear of the post)
            _speed = Plugin.MinSpeed.Value * 0.6f;
            _waitedForGround = 0f;
            _start = player.transform.position;
            _phase = 0; _phaseAt = Time.time;
            _rider = player;
            _trolley = new GameObject("ZipCarriage").transform;
            _trolley.position = _start;
            _trolley.rotation = Quaternion.LookRotation(Flat(_b - _a));
            player.AttachStart(_trolley, null, true, false, false, "attach_mast", Vector3.zero, null);
            Pose.Begin(player);
            Grip.Begin(player, Plugin.NeedAxe.Value ? axe : null);
            RopePoint = Line.Point(_a, _b, _t);
            StartWind();
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized; }

        /// <summary>Where the carriage is at this point of the line: the rope's height less how far you hang.</summary>
        private static Vector3 Carriage(float t) { RopePoint = Line.Point(_a, _b, Mathf.Clamp01(t)); return RopePoint + Vector3.down * Plugin.Hang.Value - Flat(_b - _a) * 0.24f; }   // (a little behind the rope: the handle is held out in front)

        public static void Tick()
        {
            if (_rider == null) return;
            if (_rider.IsDead() || !_rider.IsAttached() || _trolley == null) { Finish(); return; }
            // let go with the game's Jump (whatever key or gamepad button the player has it on)
            if ((ZInput.GetButtonDown("Jump") || ZInput.GetButtonDown("JoyJump")) && _phase == 1) { _rider.AttachStop(); Finish(); return; }

            float dt = Time.deltaTime;
            _trolley.rotation = Quaternion.Slerp(_trolley.rotation, Quaternion.LookRotation(Flat(_b - _a)), dt * 6f);
            switch (_phase)
            {
                case 0:   // lifted up onto the rope
                {
                    float k = Mathf.SmoothStep(0f, 1f, (Time.time - _phaseAt) / 0.7f);
                    _trolley.position = Vector3.Lerp(_start, Carriage(_t), k);
                    if (k >= 1f) _phase = 1;
                    break;
                }
                case 1:   // riding: speeds up toward what the slope gives
                {
                    float slope = (_a.y - _b.y) / _length;   // downhill is positive
                    float scale = Plugin.SpeedScale.Value / 100f;
                    float target = Mathf.Clamp(Plugin.MinSpeed.Value + 55f * slope, Plugin.MinSpeed.Value, Plugin.TopSpeed.Value) * _boost * scale;
                    // slow for the last stretch (the ground at the far end may only just be loading): to a gentle glide over the last 250 m
                    float left = (1f - _t) * _length;
                    if (left < 250f) target = Mathf.Min(target, Mathf.Lerp(Plugin.MinSpeed.Value * 1.2f * scale, target, Mathf.Clamp01((left - 20f) / 230f)));
                    _speed += (target - _speed) * Mathf.Min(1f, dt * (_speed > target ? 0.9f : 1.2f));
                    _t += _speed * dt / _length;
                    _trolley.position = Carriage(_t);
                    if (_t >= 1f - 1.2f / _length)
                    {
                        _phase = 2; _phaseAt = Time.time; _land = _trolley.position;
                    }
                    break;
                }
                default:  // eased down to the ground just beyond the far post
                {
                    Vector3 beyond = _b + Flat(_b - _a) * 1.5f;   // (not onto the post itself)
                    float ground = GroundAt(beyond, float.NaN);
                    // the far end may not be loaded yet (a long line is flown fast): held there until its ground is, or a few seconds have gone
                    if (float.IsNaN(ground))
                    {
                        _waitedForGround += dt;
                        if (_waitedForGround < 6f) { _phaseAt = Time.time; _trolley.position = _land; break; }
                        ground = _b.y - Things.Height;
                    }
                    Vector3 down = new Vector3(beyond.x, ground + 0.05f, beyond.z);
                    float k = Mathf.SmoothStep(0f, 1f, (Time.time - _phaseAt) / 0.9f);
                    _speed = Mathf.Lerp(_speed, 0f, dt * 2.5f);
                    _trolley.position = Vector3.Lerp(_land, down, k);
                    if (k >= 1f)
                    {
                        _rider.AttachStop();
                        Finish();
                    }
                    break;
                }
            }
            UpdateWind();
        }

        /// <summary>The ground at this spot: the highest solid thing under it that is not a post or a creature, else the fallback.</summary>
        private static float GroundAt(Vector3 at, float fallback)
        {
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
            float best = float.MinValue;
            foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 6f, Vector3.down, 80f, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<Post>() != null || hit.collider.GetComponentInParent<Character>() != null) continue;
                best = Mathf.Max(best, hit.point.y);
            }
            return best > float.MinValue ? best : fallback;   // (NaN means: not loaded here yet)
        }

        public static void End(string why)
        {
            if (_rider != null && _rider.IsAttached()) _rider.AttachStop();
            Finish();
        }

        private static void Finish()
        {
            if (_trolley != null) Object.Destroy(_trolley.gameObject);
            _trolley = null;
            _rider = null;
            _speed = 0f;
            Pose.End();
            Grip.End();
            if (_wind != null) Object.Destroy(_wind.gameObject);
            _wind = null;
        }

        // ---- the sound of it ---------------------------------------------------------------------------------------------

        private static void StartWind()
        {
            if (!Plugin.Wind.Value) return;
            if (_windClip == null)
            {
                // two seconds of soft noise that loops: filtered a little so it is wind and not a hiss
                const int rate = 22050, n = rate * 2;
                var data = new float[n];
                var random = new System.Random(7);
                float low = 0f;
                for (int i = 0; i < n; i++)
                {
                    low += ((float)(random.NextDouble() * 2.0 - 1.0) - low) * 0.12f;
                    data[i] = low * 1.6f;
                }
                for (int i = 0; i < 1000; i++) { float k = i / 1000f; data[i] *= k; data[n - 1 - i] *= k; }   // (no click where it loops)
                int at = 0;
                _windClip = AudioClip.Create("ZipWind", n, 1, rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = data[at++ % n]; }, position => at = position);
            }
            var go = new GameObject("ZipWind");
            go.transform.SetParent(_trolley, false);
            _wind = go.AddComponent<AudioSource>();
            _wind.clip = _windClip; _wind.loop = true; _wind.spatialBlend = 0f; _wind.volume = 0f;
            // through the game's sound effects channel, so the player's volume slider applies
            if (_mixer == null && ZNetScene.instance != null)
                _mixer = ZNetScene.instance.GetPrefab("sfx_chest_open")?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;
            _wind.outputAudioMixerGroup = _mixer;
            _wind.Play();
        }

        private static void UpdateWind()
        {
            if (_wind == null) return;
            float speed01 = Mathf.InverseLerp(0f, Plugin.TopSpeed.Value * Plugin.SpeedScale.Value / 100f * 2f, _speed);
            _wind.volume = Mathf.Lerp(_wind.volume, Plugin.WindVolume.Value * speed01, Time.deltaTime * 4f);
            _wind.pitch = 0.8f + 0.7f * speed01;
        }
    }

    // The view widens as you pick up speed.
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class GameCamera_Speed
    {
        private static float _lastSet = -1f, _lastOffset;

        private static void Postfix(GameCamera __instance)
        {
            Ride.Late();
            Camera camera = __instance.GetComponent<Camera>();
            if (camera == null) return;
            float offset = Ride.Active && Plugin.WideView.Value ? 12f * Mathf.InverseLerp(Plugin.MinSpeed.Value, Plugin.TopSpeed.Value, Ride.Speed) : 0f;
            if (offset <= 0.01f && _lastOffset <= 0.01f) return;
            // the game sets the field of view itself; if it did not this frame, take back what was added last frame first
            float current = camera.fieldOfView;
            float baseFov = Mathf.Approximately(current, _lastSet) ? current - _lastOffset : current;
            _lastOffset = Mathf.MoveTowards(_lastOffset, offset, 40f * Time.deltaTime);
            camera.fieldOfView = baseFov + _lastOffset;
            _lastSet = camera.fieldOfView;
        }
    }
}
