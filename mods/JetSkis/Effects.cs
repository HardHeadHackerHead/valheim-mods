using System.Collections.Generic;
using UnityEngine;

namespace JetSkis
{
    /// <summary>
    /// What a jet ski sounds and looks like as it goes, on every game (each works it out from how the jet ski moves and its throttle):
    ///   * the engine: a two-stroke burble made here in code (the game has no motor sounds), rising in pitch and volume with the throttle
    ///     and the speed, through the game's own sound mixer, so the player's volume settings apply;
    ///   * the spray: a plume of water thrown up behind the nozzle at speed, and a sheet off the outside of a hard turn (copies of the
    ///     raft's own wake particles, so they look like the game's water);
    ///   * the surtling core and the nozzle's fire glowing brighter, and its light reaching further, as the throttle opens;
    ///   * its paint: the band and cowl in the colour saved on it.
    /// Made when a jet ski wakes and taken away with it (a hot reload's copy makes its own).
    /// </summary>
    internal sealed class Effects
    {
        internal static readonly Color[] Paints =
        {
            new Color(0.75f, 0.12f, 0.08f),   // red
            new Color(0.14f, 0.3f, 0.75f),    // blue
            new Color(0.16f, 0.5f, 0.22f),    // green
            new Color(0.12f, 0.6f, 0.6f),     // teal
            new Color(0.5f, 0.16f, 0.62f),    // purple
            new Color(0.13f, 0.12f, 0.11f),   // black
            new Color(1f, 1f, 1f),            // bare wood
        };
        internal static readonly string[] PaintKeys = { "jetskis_paint_red", "jetskis_paint_blue", "jetskis_paint_green", "jetskis_paint_teal", "jetskis_paint_purple", "jetskis_paint_black", "jetskis_paint_wood" };

        private static AudioClip _engineClip;
        // (AudioClip.SetData has an overload the build can't see the types of: called by name)
        private static readonly System.Reflection.MethodInfo SetData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        private readonly Transform _root;
        private readonly List<Object> _made = new List<Object>();
        private AudioSource _engine;
        private ParticleSystem _plume, _sprayL, _sprayR;
        private Light _light;
        private Material _glow, _paint;
        private Color _glowBase;
        private float _rev, _flicker;
        private int _paintShown = -1;

        internal Effects(Transform root, Transform body)
        {
            _root = root;
            try { Engine(); } catch (System.Exception e) { Plugin.Log.LogWarning("No engine sound: " + e.Message); }
            try { Spray(); } catch (System.Exception e) { Plugin.Log.LogWarning("No spray: " + e.Message); }
            if (body != null)
            {
                _light = body.GetComponentInChildren<Light>(true);
                Renderer glow = body.Find("Glow")?.GetComponent<Renderer>(), paint = body.Find("Paint")?.GetComponent<Renderer>();
                if (glow != null) { _glow = glow.material; _made.Add(_glow); _glowBase = _glow.HasProperty("_EmissionColor") ? _glow.GetColor("_EmissionColor") : Color.black; }
                if (paint != null) { _paint = paint.material; _made.Add(_paint); }
            }
        }

        internal void Dispose()
        {
            foreach (Object o in _made) if (o != null) Object.Destroy(o);
            _made.Clear();
        }

        /// <summary>Each frame: speed forwards (m/s), the throttle, the steering (-1 to 1), whether anyone rides and whether it's in the water.</summary>
        internal void Tick(float speed, Ship.Speed throttle, float rudder, bool ridden, bool inWater)
        {
            float dt = Time.deltaTime;
            float top = Mathf.Max(1f, Plugin.TopSpeed.Value);
            float want = !ridden ? 0f : throttle == Ship.Speed.Full ? 1f : throttle == Ship.Speed.Half || throttle == Ship.Speed.Slow ? 0.65f : throttle == Ship.Speed.Back ? 0.45f : 0.08f;
            float frac = Mathf.Clamp01(Mathf.Abs(speed) / top);
            _rev = Mathf.MoveTowards(_rev, Mathf.Max(want * 0.6f + frac * 0.4f, ridden ? 0.08f : 0f), dt * (want > _rev ? 1.6f : 0.9f));

            if (_engine != null)
            {
                float vol = ridden ? (0.16f + 0.42f * _rev) * Plugin.EngineVolume.Value : 0f;
                _engine.volume = Mathf.MoveTowards(_engine.volume, vol, dt * 0.8f);
                _engine.pitch = 0.75f + 1.75f * _rev + Mathf.Sin(Time.time * 7.3f) * 0.015f;   // (a little waver, as a real engine has)
                if (_engine.volume > 0.001f && !_engine.isPlaying) _engine.Play();
                else if (_engine.volume <= 0.001f && _engine.isPlaying) _engine.Stop();
            }

            // spray: the plume behind grows with speed; a hard turn throws a sheet off its outside
            float plume = inWater && speed > 2f ? Mathf.Pow(frac, 1.4f) * 90f : 0f;
            Rate(_plume, plume);
            float turn = inWater && speed > 3f ? Mathf.Abs(rudder) * frac * 70f : 0f;
            Rate(_sprayL, rudder > 0.15f ? turn : 0f);   // (turning right, the hull skids left: spray off the left)
            Rate(_sprayR, rudder < -0.15f ? turn : 0f);

            // the core's fire
            _flicker = Mathf.Lerp(_flicker, Random.Range(-1f, 1f), dt * 12f);
            float bright = ridden ? 0.85f + 1.6f * _rev + _flicker * 0.12f : 0.7f;
            if (_glow != null && _glow.HasProperty("_EmissionColor")) _glow.SetColor("_EmissionColor", _glowBase * bright);
            if (_light != null) { _light.intensity = 1.0f * bright; _light.range = 2.5f + 2.5f * _rev; }
        }

        internal void Paint(int index)
        {
            index = Mathf.Clamp(index, 0, Paints.Length - 1);
            if (index == _paintShown || _paint == null) return;
            _paint.color = Paints[index];
            _paintShown = index;
        }

        private static void Rate(ParticleSystem ps, float perSecond)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule e = ps.emission;
            e.rateOverTime = perSecond;
            if (perSecond > 0f && !ps.isPlaying) ps.Play();
        }

        // ---- the engine -------------------------------------------------------------------------------------------------

        private void Engine()
        {
            var go = new GameObject("Engine");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, -1.2f);
            _made.Add(go);
            _engine = go.AddComponent<AudioSource>();
            _engine.clip = EngineClip();
            _engine.loop = true;
            _engine.playOnAwake = false;
            _engine.volume = 0f;
            _engine.spatialBlend = 1f;
            _engine.rolloffMode = AudioRolloffMode.Linear;
            _engine.minDistance = 4f;
            _engine.maxDistance = 50f;
            _engine.dopplerLevel = 0.4f;
            // through the game's own mixer (its volume settings), as the boat's water sounds are
            foreach (AudioSource like in _root.GetComponentsInChildren<AudioSource>(true))
                if (like != _engine && like.outputAudioMixerGroup != null) { _engine.outputAudioMixerGroup = like.outputAudioMixerGroup; break; }
        }

        /// <summary>
        /// A second of a two-stroke engine, made once: 50 firings, each a short burst of a low tone and noise dying away, over a hum at the
        /// firing rate, softened. It loops cleanly (whole firings), and the pitch sets how fast it runs.
        /// </summary>
        private static AudioClip EngineClip()
        {
            if (_engineClip != null) return _engineClip;
            const int rate = 44100, firings = 50, per = rate / firings, n = per * firings;
            var data = new float[n];
            var rnd = new System.Random(7);
            for (int f = 0; f < firings; f++)
            {
                float amp = 0.75f + 0.25f * (float)rnd.NextDouble(), tone = 150f + 30f * (float)rnd.NextDouble();
                for (int i = 0; i < per; i++)
                {
                    float t = i / (float)rate, env = Mathf.Exp(-t / 0.0065f);
                    float noise = (float)rnd.NextDouble() * 2f - 1f;
                    data[f * per + i] = amp * env * (0.6f * Mathf.Sin(2f * Mathf.PI * tone * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * tone * t) + 0.45f * noise);
                }
            }
            float lp = 0f, lp2 = 0f, peak = 0.001f;
            for (int k = 0; k < n; k++)
            {
                data[k] += 0.22f * Mathf.Sin(2f * Mathf.PI * firings * k / rate);   // the hum
                lp += (data[k] - lp) * 0.3f;
                lp2 += (lp - lp2) * 0.35f;
                data[k] = lp2;
                peak = Mathf.Max(peak, Mathf.Abs(lp2));
            }
            for (int k = 0; k < n; k++) data[k] *= 0.8f / peak;
            _engineClip = AudioClip.Create("dh_jetski_engine", n, 1, rate, false);
            SetData.Invoke(_engineClip, new object[] { data, 0 });
            return _engineClip;
        }

        // ---- the spray --------------------------------------------------------------------------------------------------

        private void Spray()
        {
            Transform wake = _root.Find("WaterEffects/SpeedWake/aft_particles");
            if (wake == null || wake.GetComponent<ParticleSystem>() == null) return;
            _plume = Copy(wake, "Plume", new Vector3(0f, 0.05f, -1.55f), new Vector3(0f, 0.75f, -1f), 12f, 5.5f, 8.5f, 0.3f, 0.8f, 0.7f, 1.1f);
            _sprayL = Copy(wake, "SprayLeft", new Vector3(-0.55f, 0.02f, 0.25f), new Vector3(-1f, 0.55f, -0.25f), 25f, 3f, 5.5f, 0.25f, 0.6f, 0.5f, 0.8f);
            _sprayR = Copy(wake, "SprayRight", new Vector3(0.55f, 0.02f, 0.25f), new Vector3(1f, 0.55f, -0.25f), 25f, 3f, 5.5f, 0.25f, 0.6f, 0.5f, 0.8f);
        }

        /// <summary>A copy of the raft's wake particles thrown out one way, falling back as water does.</summary>
        private ParticleSystem Copy(Transform like, string name, Vector3 at, Vector3 toward, float spread, float speedMin, float speedMax,
                                    float sizeMin, float sizeMax, float lifeMin, float lifeMax)
        {
            GameObject go = Object.Instantiate(like.gameObject, _root, false);
            go.name = name;
            _made.Add(go);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(toward.normalized, Vector3.up);
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            foreach (ParticleSystem child in go.GetComponentsInChildren<ParticleSystem>(true)) if (child != ps) Object.Destroy(child.gameObject);
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.gravityModifier = 0.45f;   // (the game's gravity is strong)
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.loop = true;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spread;
            shape.radius = 0.06f;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = false;
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            e.rateOverDistance = 0f;
            ps.Play();
            return ps;
        }
    }
}
