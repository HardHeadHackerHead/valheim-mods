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

        internal static float TestUntil;   // (the test command's: run as at full speed, parked)

        private static AudioClip _engineClip;
        // (AudioClip.SetData has an overload the build can't see the types of: called by name)
        private static readonly System.Reflection.MethodInfo SetData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        private readonly Transform _root;
        private readonly List<Object> _made = new List<Object>();
        private AudioSource _engine;
        private ParticleSystem[] _plume, _sprayL, _sprayR, _bowL, _bowR;
        private ParticleSystem _foam;
        private readonly Dictionary<ParticleSystem, float> _perRate = new Dictionary<ParticleSystem, float>();   // (droplets: several for each puff)
        private readonly Dictionary<ParticleSystem, Vector3> _from = new Dictionary<ParticleSystem, Vector3>();   // where each comes from on the jet ski (its height is kept to the water's)
        private Light _light;
        private Material _glow, _paint;
        private readonly Dictionary<Renderer, Material> _shared = new Dictionary<Renderer, Material>();   // the materials it had before its own copies
        private static readonly int _sprayLayer = LayerMask.NameToLayer("effect");
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
                if (_light != null && _sprayLayer >= 0) _light.cullingMask &= ~(1 << _sprayLayer);   // (the spray stays white, not lit orange by the fire beside it)
                Renderer glow = body.Find("Glow")?.GetComponent<Renderer>(), paint = body.Find("Paint")?.GetComponent<Renderer>();
                if (glow != null) { _glow = Own(glow, "Glow"); _glowBase = _glow.HasProperty("_EmissionColor") ? _glow.GetColor("_EmissionColor") : Color.black; }
                if (paint != null) _paint = Own(paint, "Paint");
            }
        }

        internal void Dispose()
        {
            foreach (KeyValuePair<Renderer, Material> kv in _shared) if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;   // (the model gets its own back: a hot reload's next copy starts from it)
            _shared.Clear();
            foreach (Object o in _made) if (o != null) Object.Destroy(o);
            _made.Clear();
        }

        /// <summary>This jet ski's own copy of a part's material (its paint and glow change on it alone), made from the shared one.</summary>
        private Material Own(Renderer r, string look)
        {
            if (r.sharedMaterial == null) r.sharedMaterial = Look.Of(look);   // (an older copy's, thrown away)
            _shared[r] = r.sharedMaterial;
            Material mine = r.material;
            _made.Add(mine);
            return mine;
        }

        /// <summary>Each frame: speed forwards (m/s), the throttle, the steering (-1 to 1), whether anyone rides, whether it's in the water and the water's height there.</summary>
        internal void Tick(float speed, Ship.Speed throttle, float rudder, bool ridden, bool inWater, float water)
        {
            float dt = Time.deltaTime;
            if (Time.time < TestUntil) { speed = Plugin.TopSpeed.Value; throttle = Ship.Speed.Full; rudder = Mathf.Sin(Time.time * 0.8f); ridden = inWater = true; }
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

            // spray, from the water's surface wherever the jet ski bobs: the rooster tail grows with speed, the bow curls at speed, a hard
            // turn throws a sheet off its outside, and foam is left behind, so much per metre
            foreach (KeyValuePair<ParticleSystem, Vector3> kv in _from)
            {
                Vector3 at = _root.TransformPoint(new Vector3(kv.Value.x, 0f, kv.Value.z));
                at.y = water + kv.Value.y;
                kv.Key.transform.position = at;
            }
            bool going = inWater && speed > 2f;
            Rate(_plume, going ? Mathf.Pow(frac, 1.2f) * 70f : 0f);
            Rate(_bowL, going && speed > 4f ? frac * 22f : 0f);
            Rate(_bowR, going && speed > 4f ? frac * 22f : 0f);
            float turn = going && speed > 3f ? Mathf.Abs(rudder) * frac * 55f : 0f;
            Rate(_sprayL, rudder > 0.15f ? turn : 0f);   // (turning right, the hull skids left: spray off the left)
            Rate(_sprayR, rudder < -0.15f ? turn : 0f);
            Rate(_foam, inWater && Mathf.Abs(speed) > 1.5f ? Mathf.Abs(speed) * 1.6f : 0f);

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

        private void Rate(ParticleSystem[] spray, float perSecond)
        {
            if (spray != null) foreach (ParticleSystem ps in spray) Rate(ps, perSecond * (_perRate.TryGetValue(ps, out float k) ? k : 1f));
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

        // Spray is made of the game's own splash (what a float or a fish makes landing in the water): soft white puffs of mist and fine
        // droplets, both lit by the world as everything else is (the raft's water particles are a darkening film meant to lie on water).
        // Fresh particle systems with its materials (a copy of the splash itself would bring its network object along). The foam left
        // on the water is the raft's own wake.
        private void Spray()
        {
            GameObject splash = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("fx_float_hitwater") : null;
            Material mist = splash != null ? splash.GetComponent<ParticleSystemRenderer>()?.sharedMaterial : null;
            Material drop = splash != null ? splash.transform.Find("GameObject")?.GetComponent<ParticleSystemRenderer>()?.sharedMaterial : null;
            if (mist != null && drop != null)
            {
                // the rooster tail: a tall arc of water out of the nozzle, spreading into mist as it falls
                _plume = Pair(mist, drop, "Plume", new Vector3(0f, 0.12f, -1.6f), new Vector3(0f, 0.62f, -1f), 6f, 8f, 11.5f, 0.45f, 0.9f, 0.9f, 1.3f, 3.2f);
                // a sheet off the outside of a hard turn
                _sprayL = Pair(mist, drop, "SprayLeft", new Vector3(-0.6f, 0.05f, -0.2f), new Vector3(-1f, 0.75f, -0.35f), 20f, 4.5f, 7.5f, 0.35f, 0.7f, 0.6f, 0.9f, 2.6f);
                _sprayR = Pair(mist, drop, "SprayRight", new Vector3(0.6f, 0.05f, -0.2f), new Vector3(1f, 0.75f, -0.35f), 20f, 4.5f, 7.5f, 0.35f, 0.7f, 0.6f, 0.9f, 2.6f);
                // the bow cutting the water: a low curl off each side of the nose
                _bowL = Pair(mist, drop, "BowLeft", new Vector3(-0.42f, 0.05f, 1.0f), new Vector3(-1f, 0.5f, -0.5f), 12f, 2.5f, 4f, 0.2f, 0.4f, 0.35f, 0.55f, 2f);
                _bowR = Pair(mist, drop, "BowRight", new Vector3(0.42f, 0.05f, 1.0f), new Vector3(1f, 0.5f, -0.5f), 12f, 2.5f, 4f, 0.2f, 0.4f, 0.35f, 0.55f, 2f);
            }
            Transform foam = _root.Find("WaterEffects/SpeedWake/aft_particles");
            if (foam != null && foam.GetComponent<ParticleSystem>() != null) _foam = Foam(foam);
        }

        /// <summary>One spray: puffs of mist (that grow as they fall) and, with them, more fine droplets thrown a bit harder.</summary>
        private ParticleSystem[] Pair(Material mist, Material drop, string name, Vector3 at, Vector3 toward, float spread, float speedMin, float speedMax,
                                      float sizeMin, float sizeMax, float lifeMin, float lifeMax, float grow) => new[]
        {
            Emitter(mist, name, at, toward, spread, speedMin, speedMax, sizeMin, sizeMax, lifeMin, lifeMax, 0.75f, grow, 1f),
            Emitter(drop, name + "Drops", at, toward, spread * 1.3f, speedMin * 1.05f, speedMax * 1.15f, 0.02f, 0.045f, lifeMin, lifeMax * 1.2f, 0.95f, 1f, 2.5f),
        };

        /// <summary>
        /// Particles thrown out one way and falling back as water does, fading at the end. Every module it uses is set here.
        /// 'perRate' is how many of these for each one the spray asks for (droplets come thicker than puffs).
        /// </summary>
        private ParticleSystem Emitter(Material material, string name, Vector3 at, Vector3 toward, float spread, float speedMin, float speedMax,
                                       float sizeMin, float sizeMax, float lifeMin, float lifeMax, float alpha, float grow, float perRate)
        {
            var go = new GameObject(name);
            go.SetActive(false);   // (set up before it first runs)
            if (_sprayLayer >= 0) go.layer = _sprayLayer;
            go.transform.SetParent(_root, false);
            _made.Add(go);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(toward.normalized, Vector3.up);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            _from[ps] = at;
            _perRate[ps] = perRate;

            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.startColor = new Color(0.94f, 0.97f, 1f, alpha);
            main.gravityModifier = 0.5f;   // (the game's gravity is strong: water arcs and falls)
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spread;
            shape.radius = 0.08f;

            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;   // (a little drag: the arc tails off)
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(speedMax);
            limit.dampen = 0.08f;
            ParticleSystem.InheritVelocityModule inherit = ps.inheritVelocity;   // (thrown from a moving boat: a little of its speed)
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = new ParticleSystem.MinMaxCurve(0.3f);

            ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.06f), new GradientAlphaKey(0.75f, 0.55f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            if (grow > 1f)
            {
                ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f / grow), new Keyframe(1f, 1f)));   // (spreads into mist)
            }

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = 2f;

            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            go.SetActive(true);
            ps.Play();
            return ps;
        }

        /// <summary>The foam left on the water behind: the raft's own wake patches, laid as it goes (so many per metre travelled), lasting a while.</summary>
        private ParticleSystem Foam(Transform like)
        {
            GameObject go = Object.Instantiate(like.gameObject, _root, false);
            go.name = "Foam";
            go.SetActive(true);
            _made.Add(go);
            go.transform.localPosition = new Vector3(0f, 0.04f, -1.4f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            _from[ps] = go.transform.localPosition;
            foreach (ParticleSystem child in go.GetComponentsInChildren<ParticleSystem>(true)) if (child != ps) Object.Destroy(child.gameObject);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = 300;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            shape.scale = new Vector3(1f, 0.01f, 1f);
            ParticleSystem.EmissionModule e = ps.emission;
            e.enabled = true;
            e.rateOverTime = 0f;
            e.rateOverDistance = 0f;
            ps.Play();
            return ps;
        }
    }
}
