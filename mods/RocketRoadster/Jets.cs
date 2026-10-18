using System;
using UnityEngine;

namespace RocketRoadster
{
    /// <summary>
    /// What the roadster's thrusters look and sound like, on every game (a dedicated server makes none of it). The gas is cold (no flame):
    /// white plumes from the nozzles, made from the game's own splash mist (the material the Jet Ski's spray and the land vehicles' dust use),
    /// and when the lift jets fire near the ground a white cloud rolls out across it, as in the photos. Sounds are made in code: a roar of
    /// rushing gas while any jet fires, a "pssht" when the lift jets kick in, and the electric motor's whine rising with speed. They go
    /// through the game's mixer so its volume settings apply.
    /// </summary>
    internal sealed class Jets : IDisposable
    {
        private static Material _mist;
        private static AudioClip _roarClip, _whineClip, _popClip;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;
        private static int _mixerTries;
        private static readonly System.Reflection.MethodInfo SetData = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });

        private readonly GameObject _root;
        private readonly ParticleSystem[] _lift, _rear, _top, _side;
        private readonly Vector3[] _liftTurn, _topTurn, _sideTurn;   // which way each jet turns the car (its torque's direction, in the car's own axes)
        private readonly ParticleSystem _cloud;
        private AudioSource _roar, _whine;
        private int _shown;
        private float _whineLevel;

        internal Jets(Transform car)
        {
            _root = new GameObject("Jets");
            _root.transform.SetParent(car, false);
            if (_mist == null)
            {
                GameObject splash = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("fx_float_hitwater") : null;
                _mist = splash != null ? splash.GetComponent<ParticleSystemRenderer>()?.sharedMaterial : null;
            }
            int effect = LayerMask.NameToLayer("effect");

            _lift = new ParticleSystem[_mist != null ? Model.LiftJets.Length : 0];
            for (int i = 0; i < _lift.Length; i++)
            {
                Vector3 at = Model.LiftJets[i];
                Vector3 aim = (Vector3.down + new Vector3(Mathf.Sign(at.x) * 0.12f, 0f, 0f)).normalized;
                _lift[i] = Plume("lift" + i, at, aim, 15f, 22f, 0.16f, effect);
            }
            _liftTurn = new Vector3[Model.LiftJets.Length];
            for (int i = 0; i < _liftTurn.Length; i++) _liftTurn[i] = Turn(Model.LiftJets[i], (Vector3.down + new Vector3(Mathf.Sign(Model.LiftJets[i].x) * 0.12f, 0f, 0f)).normalized);
            _top = new ParticleSystem[_mist != null ? Model.TopJets.Length : 0];
            for (int i = 0; i < _top.Length; i++) _top[i] = Plume("top" + i, Model.TopJets[i] + new Vector3(0f, 0.03f, 0f), Vector3.up, 12f, 18f, 0.12f, effect);
            _topTurn = new Vector3[Model.TopJets.Length];
            for (int i = 0; i < _topTurn.Length; i++) _topTurn[i] = Turn(Model.TopJets[i], Vector3.up);
            _side = new ParticleSystem[_mist != null ? Model.Wheels.Length : 0];
            _sideTurn = new Vector3[Model.Wheels.Length];
            for (int i = 0; i < Model.Wheels.Length; i++)
            {
                float sx = Mathf.Sign(Model.Wheels[i].x);
                _sideTurn[i] = Turn(Model.Wheels[i] + new Vector3(sx * Model.SideJetOut, 0f, 0f), new Vector3(sx, 0f, 0f));
                if (i >= _side.Length) continue;
                _side[i] = Plume("side" + i, Model.Wheels[i] + new Vector3(sx * Model.SideJetOut, 0f, 0f), new Vector3(sx, 0f, 0f), 12f, 18f, 0.12f, effect);
                Transform hub = Find(car, "jet_side" + i);   // (in the wheel's hub: it moves with the springs and steering)
                if (hub != null) { _side[i].transform.SetParent(hub, false); _side[i].transform.localPosition = Vector3.zero; _side[i].transform.localRotation = Quaternion.LookRotation(new Vector3(sx, 0f, 0f)); }
            }
            _rear = new ParticleSystem[_mist != null ? Model.RearJets.Length : 0];
            for (int i = 0; i < _rear.Length; i++) _rear[i] = Plume("rear" + i, Model.RearJets[i] + new Vector3(0f, 0f, -0.05f), Vector3.back, 20f, 30f, 0.18f, effect);
            _cloud = _mist != null ? Cloud(effect) : null;

            try
            {
                _roar = Source("Roar", RoarClip(), 70f);
                _whine = Source("Whine", WhineClip(), 40f);
            }
            catch (Exception e) { Debug.LogWarning("[RocketRoadster] no thruster sound: " + e.Message); }
        }

        /// <summary>Which way a jet at <paramref name="at"/> blowing gas along <paramref name="gas"/> turns the car: r x F, F the push opposite the gas.</summary>
        private static Vector3 Turn(Vector3 at, Vector3 gas) => Vector3.Cross(at - Model.Centre, -gas).normalized;

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { Transform f = Find(c, name); if (f != null) return f; }
            return null;
        }

        /// <summary>The turn the car's attitude jets are asked for (Roadster's bits 4 to 128): x pitch (+ nose down), y yaw (+ nose right), z roll (+ right side up).</summary>
        internal static Vector3 TurnOf(int jets) => new Vector3((jets & 4) != 0 ? 1f : (jets & 8) != 0 ? -1f : 0f, (jets & 16) != 0 ? 1f : (jets & 32) != 0 ? -1f : 0f, (jets & 64) != 0 ? 1f : (jets & 128) != 0 ? -1f : 0f);

        /// <summary>One nozzle's plume: fast white gas that spreads and fades in a fraction of a second, left behind in the world as the car moves.</summary>
        private ParticleSystem Plume(string name, Vector3 at, Vector3 aim, float speedMin, float speedMax, float size, int layer)
        {
            var go = new GameObject(name);
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(aim);   // (a cone shoots along its z)
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(size, size * 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.97f, 0.98f, 1f, 0.9f));
            main.maxParticles = 220;
            ParticleSystem.EmissionModule emit = ps.emission;
            emit.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 6f;
            shape.radius = 0.05f;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 6f)));
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 2f;
            drag.dampen = 0.08f;
            Render(go);
            return ps;
        }

        /// <summary>The cloud the lift jets blow across the ground under the car: big soft puffs rolling outward and slowly rising.</summary>
        private ParticleSystem Cloud(int layer)
        {
            var go = new GameObject("cloud");
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(_root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.0f, 2.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.96f, 0.98f, 0.6f));
            main.gravityModifier = -0.03f;
            main.maxParticles = 260;
            ParticleSystem.EmissionModule emit = ps.emission;
            emit.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.4f;
            shape.rotation = new Vector3(90f, 0f, 0f);   // (the circle lies flat on the ground; its puffs go out across it)
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.12f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(fade);
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(1f, 3.2f)));
            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0.6f;
            drag.dampen = 0.06f;
            Render(go);
            return ps;
        }

        private static void Render(GameObject go)
        {
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _mist;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private AudioSource Source(string name, AudioClip clip, float range)
        {
            var s = _root.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 5f;
            s.maxDistance = range;
            s.dopplerLevel = 0.3f;
            if (_mixer == null && _mixerTries++ < 20)
                foreach (AudioSource like in Resources.FindObjectsOfTypeAll<AudioSource>())
                    if (like != null && like.outputAudioMixerGroup != null) { _mixer = like.outputAudioMixerGroup; break; }
            if (_mixer != null) s.outputAudioMixerGroup = _mixer;
            return s;
        }

        /// <summary>
        /// Every frame: <paramref name="jets"/> which fire (1 lift, 2 boost), <paramref name="speedFraction"/> of the boost top speed, and how
        /// high it is over <paramref name="ground"/> (the cloud only shows within a few metres of it).
        /// </summary>
        internal void Tick(int jets, bool ridden, float speedFraction, float altitude, Vector3 ground)
        {
            bool lift = (jets & 1) != 0, boost = (jets & 2) != 0;
            bool liftStarted = lift && (_shown & 1) == 0;
            _shown = jets;
            // the attitude jets: each fires when the way it turns the car is (mostly) the way it is asked to turn
            Vector3 turn = TurnOf(jets);
            bool turning = turn != Vector3.zero;
            if (turning) turn.Normalize();
            for (int i = 0; i < _lift.Length; i++) Rate(_lift[i], lift ? 110f : (turning && Vector3.Dot(_liftTurn[i], turn) > 0.4f ? 85f : 0f));
            for (int i = 0; i < _top.Length; i++) Rate(_top[i], turning && Vector3.Dot(_topTurn[i], turn) > 0.4f ? 85f : 0f);
            for (int i = 0; i < _side.Length; i++) if (_side[i] != null) Rate(_side[i], turning && Vector3.Dot(_sideTurn[i], turn) > 0.4f ? 85f : 0f);
            foreach (ParticleSystem ps in _rear) Rate(ps, boost ? 120f : 0f);
            if (_cloud != null)
            {
                const float reach = 8f;
                _cloud.transform.position = ground + Vector3.up * 0.15f;
                _cloud.transform.rotation = Quaternion.identity;
                Rate(_cloud, lift && altitude < reach ? 70f * (1f - altitude / reach) + 10f : 0f);
                if (liftStarted && altitude < 2f) _cloud.Emit(45);
            }
            if (liftStarted && altitude < 2f) foreach (ParticleSystem ps in _lift) ps.Emit(12);

            float vol = Plugin.ThrusterVolume.Value;
            if (_roar != null)
            {
                float want = (lift ? 0.75f : 0f) + (boost ? 0.5f : 0f) + (turning ? 0.35f : 0f);
                _roar.volume = Mathf.MoveTowards(_roar.volume, Mathf.Min(1f, want) * vol, Time.deltaTime * (want > _roar.volume ? 8f : 3f));
                _roar.pitch = 0.85f + (lift && boost ? 0.15f : 0f) + Mathf.Sin(Time.time * 9.3f) * 0.02f;
                Run(_roar);
                if (liftStarted)
                {
                    try { _roar.PlayOneShot(PopClip(), Mathf.Clamp01(0.9f * vol)); } catch (Exception) { }
                }
            }
            if (_whine != null)
            {
                _whineLevel = Mathf.MoveTowards(_whineLevel, ridden ? speedFraction : 0f, Time.deltaTime * 1.5f);
                _whine.volume = ridden ? (0.04f + 0.2f * _whineLevel) * Plugin.EngineVolume.Value : Mathf.MoveTowards(_whine.volume, 0f, Time.deltaTime);
                _whine.pitch = 0.45f + 1.5f * _whineLevel;
                Run(_whine);
            }
        }

        private static void Rate(ParticleSystem ps, float rate)
        {
            ParticleSystem.EmissionModule e = ps.emission;
            e.rateOverTime = rate;
            if (rate > 0f && !ps.isPlaying) ps.Play();
        }

        private static void Run(AudioSource s)
        {
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        public void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        // ---- the sounds, made once ---------------------------------------------------------------------------------------------

        /// <summary>Two seconds of rushing gas: a deep rumble under a bright hiss, the end faded into the start so it loops cleanly.</summary>
        private static AudioClip RoarClip()
        {
            if (_roarClip != null) return _roarClip;
            const int rate = 22050, n = rate * 2, fade = 3000;
            var data = new float[n];
            var rnd = new System.Random(21);
            float low = 0f, low2 = 0f, high = 0f, peak = 0.001f;
            for (int k = 0; k < n; k++)
            {
                float white = (float)rnd.NextDouble() * 2f - 1f;
                low += (white - low) * 0.04f;
                low2 += (low - low2) * 0.08f;
                high += (white - high) * 0.6f;
                float hiss = white - high * 0.7f;
                data[k] = low2 * 3.2f + hiss * 0.35f;
            }
            for (int k = 0; k < fade; k++) { float a = k / (float)fade; data[k] = data[k] * a + data[n - fade + k] * (1f - a); }
            var cut = new float[n - fade];
            Array.Copy(data, 0, cut, 0, n - fade);
            foreach (float v in cut) peak = Mathf.Max(peak, Mathf.Abs(v));
            for (int k = 0; k < cut.Length; k++) cut[k] *= 0.85f / peak;
            _roarClip = AudioClip.Create("dh_roadster_roar", cut.Length, 1, rate, false);
            SetData.Invoke(_roarClip, new object[] { cut, 0 });
            return _roarClip;
        }

        /// <summary>A second of an electric motor: a clean tone with a few harmonics and a faint shimmer, whole cycles so it loops.</summary>
        private static AudioClip WhineClip()
        {
            if (_whineClip != null) return _whineClip;
            const int rate = 22050, n = rate;
            var data = new float[n];
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)rate;
                data[k] = 0.55f * Mathf.Sin(2f * Mathf.PI * 240f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 480f * t)
                        + 0.12f * Mathf.Sin(2f * Mathf.PI * 960f * t) + 0.06f * Mathf.Sin(2f * Mathf.PI * 1440f * t) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * t));
            }
            _whineClip = AudioClip.Create("dh_roadster_whine", n, 1, rate, false);
            SetData.Invoke(_whineClip, new object[] { data, 0 });
            return _whineClip;
        }

        /// <summary>Half a second of "pssht": a burst of hiss that dies away, with a thump at its start.</summary>
        private static AudioClip PopClip()
        {
            if (_popClip != null) return _popClip;
            const int rate = 22050, n = rate / 2;
            var data = new float[n];
            var rnd = new System.Random(8);
            float lp = 0f, phase = 0f;
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)rate;
                float white = (float)rnd.NextDouble() * 2f - 1f;
                lp += (white - lp) * 0.5f;
                phase += 2f * Mathf.PI * Mathf.Lerp(110f, 45f, Mathf.Clamp01(t / 0.15f)) / rate;
                data[k] = Mathf.Exp(-t / 0.16f) * 0.75f * (white - lp * 0.4f) + Mathf.Exp(-t / 0.05f) * 0.8f * Mathf.Sin(phase);
            }
            _popClip = AudioClip.Create("dh_roadster_pop", n, 1, rate, false);
            SetData.Invoke(_popClip, new object[] { data, 0 });
            return _popClip;
        }
    }
}
