using System.Linq;
using UnityEngine;

namespace Rainbows
{
    /// <summary>
    /// When a rainbow comes and what it looks like. It rains for a while and stops (and it is day, the sun low enough to throw a rainbow above
    /// the horizon): a rainbow is drawn on a dome around the camera, a ring of colour round the point of the sky opposite the sun, so it
    /// follows the sun as it moves, fades into the horizon, and goes when the sun gets too high. Mountains and trees that are nearer than the
    /// dome hide it, as they should.
    /// </summary>
    internal static class Sky
    {
        private const int Around = 220, Across = 14;
        private const float MinSun = 4f, MaxSun = 40f;   // degrees above the horizon

        private static GameObject _root;
        private static Mesh _mesh;
        private static Material _material;
        private static Vector3[] _local;      // each vertex as a direction round +Z
        private static float[] _weight;       // how strong it is across the band
        private static bool[] _outer;         // the second rainbow's vertices
        private static bool _double;
        private static Color[] _hue;
        private static Color[] _colors;
        private static AudioClip _chime;
        private static UnityEngine.Audio.AudioMixerGroup _mixer;   // the game's sound effects, so the chime follows its volume slider

        private static float _wetFor, _waited, _shownFor, _fade;
        private static bool _waiting, _showing, _forced, _blessed;
        private static Vector3 _forcedSun;

        public static bool Showing => _showing;
        public static bool Double => _double;
        public static float Fade => _fade;
        public static Vector3 Anti { get; private set; } = Vector3.forward;
        public static float SunHeight { get; private set; }
        public static string Why { get; private set; } = "";
        public static float WetFor => _wetFor;
        public static bool Waiting => _waiting;
        public static string Debug => _root == null ? "no dome" : $"shader {(_material != null && _material.shader != null ? _material.shader.name : "none")}, radius {_root.transform.localScale.x:0}, at {_root.transform.position}, active {_root.activeInHierarchy}, camera far {(Camera.main != null ? Camera.main.farClipPlane : 0f):0}, verts {_local?.Length}, alpha0 {(_colors != null && _colors.Length > 0 ? _colors.Max(c => c.a) : 0f):0.00}";

        // ---- when ----------------------------------------------------------------------------------------------------------

        public static void Tick(float dt)
        {
            Player player = Player.m_localPlayer;
            if (!Plugin.Enabled.Value || player == null || EnvMan.instance == null) { if (_showing) Stop(); _wetFor = 0f; _waiting = false; return; }

            bool wet = EnvMan.instance.GetCurrentEnvironment().m_isWet;
            if (wet) { _wetFor += dt; _waiting = false; }
            else
            {
                if (_wetFor >= Plugin.MinRainSeconds.Value && !_showing && Random.value <= Plugin.Chance.Value) { _waiting = true; _waited = 0f; }
                _wetFor = 0f;
            }
            if (_waiting) { _waited += dt; if (_waited > Plugin.WaitMinutes.Value * 60f) _waiting = false; }

            Vector3 sun = Sun();
            SunHeight = Mathf.Asin(Mathf.Clamp(sun.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool sunOk = (_forced || EnvMan.IsDay()) && SunHeight >= MinSun && SunHeight <= MaxSun;
            Why = wet ? "raining" : !sunOk ? $"sun at {SunHeight:0} degrees (needs {MinSun:0} to {MaxSun:0}, in daylight)" : player.InInterior() ? "indoors" : "ok";

            if (_waiting && !wet && sunOk && !player.InInterior()) Begin(player);
            if (!_showing) return;

            _shownFor += dt;
            float seconds = Plugin.ShowMinutes.Value * 60f;
            float inOut = Mathf.Clamp01(_shownFor / 14f) * Mathf.Clamp01((seconds - _shownFor) / 20f);
            float sunFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MinSun - 2f, MinSun + 3f, SunHeight)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MaxSun + 2f, MaxSun - 4f, SunHeight));
            float target = (wet && !_forced) || (!_forced && !EnvMan.IsDay()) ? 0f : inOut * sunFade;
            _fade = Mathf.MoveTowards(_fade, target, dt / (target < _fade ? 4f : 8f));
            Anti = -sun;

            if (_shownFor > seconds || (_fade <= 0.001f && _shownFor > 20f)) { Stop(); return; }
            if (!_blessed && _fade > 0.45f && !player.InInterior() && Camera.main != null && Vector3.Angle(Camera.main.transform.forward, new Vector3(Anti.x, Mathf.Max(Anti.y, 0.15f), Anti.z)) < 75f)
            {
                _blessed = true;
                Blessing.Give(player, _double);
                if (Plugin.Buff.Value) player.Message(MessageHud.MessageType.Center, _double ? "Double Rainbow's Blessing" : "Rainbow's Blessing");
            }
            Draw();
        }

        /// <summary>Where the sun is (a direction): the game's own light by day. When testing, a made-up sun that puts the rainbow in front of you.</summary>
        private static Vector3 Sun()
        {
            if (_forced) return _forcedSun;
            Light light = EnvMan.instance.m_dirLight;
            return light != null ? -light.transform.forward : Vector3.up;
        }

        private static void Begin(Player player, bool? forceDouble = null)
        {
            _waiting = false; _showing = true; _shownFor = 0f; _fade = 0f; _blessed = false;
            _double = forceDouble ?? Random.value <= Plugin.DoubleChance.Value;
            Anti = -Sun();
            Build();
            if (!player.InInterior())
            {
                player.Message(MessageHud.MessageType.TopLeft, _double ? "A double rainbow!" : "A rainbow comes out.");
                if (Plugin.Chime.Value) Chime();
            }
            Plugin.Log.LogInfo($"Rainbow: sun {SunHeight:0} degrees, rain lasted {_wetFor:0} s");
        }

        /// <summary>For testing: a rainbow now, in front of the camera, whatever the weather.</summary>
        public static void Force(float heightDegrees, bool twice)
        {
            Vector3 look = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            look.y = 0f; if (look.sqrMagnitude < 0.01f) look = Vector3.forward;
            float h = heightDegrees * Mathf.Deg2Rad;
            _forcedSun = (-look.normalized * Mathf.Cos(h) + Vector3.up * Mathf.Sin(h)).normalized;
            if (_showing) Stop();
            _forced = true;
            _wetFor = 0f;
            Begin(Player.m_localPlayer, twice);
            _shownFor = 14f; _fade = 1f;
        }

        public static void Stop()
        {
            _showing = false; _forced = false; _fade = 0f;
            if (_root != null) _root.SetActive(false);
        }

        public static void Clear()
        {
            Stop();
            if (_root != null) Object.Destroy(_root);
            if (_mesh != null) Object.Destroy(_mesh);
            if (_material != null) Object.Destroy(_material);
            if (_chime != null) Object.Destroy(_chime);
            _root = null; _mesh = null; _material = null; _chime = null; _mixer = null;
        }

        // ---- the drawing ---------------------------------------------------------------------------------------------------

        /// <summary>For testing: draws the rainbow with another shader (by name).</summary>
        public static string UseShader(string name)
        {
            Shader shader = Shader.Find(name);
            if (shader == null || _material == null) return "not found";
            _material.shader = shader;
            return shader.name;
        }

        public static Color Spectrum(float t)
        {
            t = Mathf.Pow(Mathf.Clamp01(t), 1.3f);   // (the red takes more of the band)
            // 0 is the red outside, 1 the violet inside
            Color[] keys = { new Color(1f, 0.05f, 0.04f), new Color(1f, 0.5f, 0.05f), new Color(1f, 0.92f, 0.2f), new Color(0.3f, 0.85f, 0.3f), new Color(0.2f, 0.65f, 1f), new Color(0.35f, 0.3f, 0.95f), new Color(0.6f, 0.25f, 0.85f) };
            float x = Mathf.Clamp01(t) * (keys.Length - 1);
            int i = Mathf.Min((int)x, keys.Length - 2);
            return Color.Lerp(keys[i], keys[i + 1], x - i);
        }

        /// <summary>The mesh: a primary rainbow 38 to 44 degrees round the point opposite the sun, and a fainter second one 50 to 56.</summary>
        private static void Build()
        {
            if (_root != null && _mesh != null) { _root.SetActive(true); return; }
            const int bands = 2;
            int perBand = (Across + 1) * Around;
            _local = new Vector3[bands * perBand]; _outer = new bool[bands * perBand]; _weight = new float[_local.Length]; _hue = new Color[_local.Length]; _colors = new Color[_local.Length];
            var triangles = new int[bands * Across * Around * 6];
            int v = 0, tri = 0;
            for (int b = 0; b < bands; b++)
            {
                float from = b == 0 ? 44f : 56f, to = b == 0 ? 38f : 50f;   // outside to inside
                int first = v;
                for (int k = 0; k <= Across; k++)
                {
                    float t = k / (float)Across, angle = Mathf.Lerp(from, to, t) * Mathf.Deg2Rad;
                    Color hue = Spectrum(b == 0 ? t : 1f - t);   // (the second one has its colours the other way round)
                    float w = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI) * 1.7f) * (b == 0 ? 1f : 0.4f);   // (a flat middle, soft edges)
                    for (int a = 0; a < Around; a++, v++)
                    {
                        float phi = a / (float)Around * Mathf.PI * 2f;
                        _local[v] = new Vector3(Mathf.Sin(angle) * Mathf.Cos(phi), Mathf.Sin(angle) * Mathf.Sin(phi), Mathf.Cos(angle));
                        _weight[v] = w; _hue[v] = hue; _outer[v] = b == 1;
                    }
                }
                for (int k = 0; k < Across; k++)
                    for (int a = 0; a < Around; a++)
                    {
                        int i0 = first + k * Around + a, i1 = first + k * Around + (a + 1) % Around, i2 = i0 + Around, i3 = i1 + Around;
                        triangles[tri++] = i0; triangles[tri++] = i2; triangles[tri++] = i1;
                        triangles[tri++] = i1; triangles[tri++] = i2; triangles[tri++] = i3;
                    }
            }
            _mesh = new Mesh { name = "Rainbow" };
            _mesh.vertices = _local;
            _mesh.triangles = triangles;
            _mesh.colors = _colors;
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // (it is drawn round the camera whatever the camera looks at)
            _mesh.MarkDynamic();

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Unlit/Color");
            _material = new Material(shader) { name = "RainbowMaterial", renderQueue = 3100 };
            _root = new GameObject("Rainbow") { layer = 0 };
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Turns the ring round the point opposite the sun and fades each vertex into the horizon.</summary>
        private static void Draw()
        {
            if (_root == null || _mesh == null) return;
            Quaternion turn = Quaternion.FromToRotation(Vector3.forward, Anti);
            Vector3 down = Quaternion.Inverse(turn) * Vector3.up;   // "up" as the ring sees it
            float strength = _fade * Plugin.Brightness.Value * 0.9f;
            for (int i = 0; i < _local.Length; i++)
            {
                float y = Vector3.Dot(down, _local[i]);   // how high that part of the arc is in the sky
                float horizon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.16f, y));
                Color c = _hue[i];
                c.a = _outer[i] && !_double ? 0f : Mathf.Clamp01(_weight[i] * horizon * strength);
                _colors[i] = c;
            }
            _mesh.colors = _colors;
            _root.transform.rotation = turn;
        }

        /// <summary>Every frame: the dome stays round the camera, as far out as the camera sees (but inside it).</summary>
        public static void Follow()
        {
            if (!_showing || _root == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            float radius = Mathf.Clamp(cam.farClipPlane * 0.7f, 200f, 450f);
            _root.transform.position = cam.transform.position;
            _root.transform.localScale = Vector3.one * radius;
        }

        // ---- the chime -----------------------------------------------------------------------------------------------------

        private static void Chime()
        {
            if (_chime == null)
            {
                const int rate = 44100;
                float seconds = 2.4f;
                var data = new float[(int)(rate * seconds)];
                float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };   // C E G C
                for (int n = 0; n < notes.Length; n++)
                {
                    int start = (int)(n * 0.22f * rate);
                    for (int i = start; i < data.Length; i++)
                    {
                        float t = (i - start) / (float)rate;
                        float env = Mathf.Exp(-t * 2.6f) * Mathf.Clamp01(t * 80f);
                        float s = Mathf.Sin(2f * Mathf.PI * notes[n] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[n] * t) * Mathf.Exp(-t * 4f);
                        data[i] += s * env * 0.18f;
                    }
                }
                int at = 0;   // (the clip asks for its samples in pieces)
                _chime = AudioClip.Create("RainbowChime", data.Length, 1, rate, false, block => { for (int i = 0; i < block.Length; i++) block[i] = at < data.Length ? data[at++] : 0f; }, position => at = position);
            }
            if (_mixer == null && ZNetScene.instance != null)
                _mixer = ZNetScene.instance.GetPrefab("sfx_chest_open")?.GetComponentInChildren<AudioSource>(true)?.outputAudioMixerGroup;
            var go = new GameObject("RainbowChime");
            var source = go.AddComponent<AudioSource>();
            source.clip = _chime; source.spatialBlend = 0f; source.volume = Plugin.Volume.Value;
            source.outputAudioMixerGroup = _mixer;
            source.Play();
            Object.Destroy(go, 3.5f);
        }
    }
}
