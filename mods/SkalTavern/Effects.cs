using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;

namespace SkalTavern
{
    /// <summary>
    /// What being drunk looks and feels like, in layers that come in as you drink more (levels are 0 sober, 100 very drunk):
    ///   the screen: a darkening vignette, colour fringing, blur, double vision (the game's motion-blur frame blending), a colour that drifts,
    ///     a pulsing field of view and a swaying camera;
    ///   your body: it leans and weaves, stars circle your head, you hiccup, and very drunk you give a drunken cheer;
    ///   the sound: muffled and wobbling;
    ///   your controls (Controls.cs): you slide, your aim floats, your walk wanders, and sloshed they reverse for a moment;
    ///   your chat: it slurs.
    /// All of it scales with Drinking: EffectStrength, and each layer can be switched off.
    /// </summary>
    internal static class Fx
    {
        public static float Strength => Plugin.Strength.Value / 100f;

        /// <summary>0 at "from", 1 at "to": how far into a stage the drink has taken you.</summary>
        public static float F(float from, float to) => Mathf.Clamp01((Tipsy.Level - from) / (to - from));

        // ---- the camera ----------------------------------------------------------------------------------------------

        private static float _lastFovSet = -1f, _lastFovOffset;
        public static float Kick;   // a hiccup's jolt, dying away

        public static void Camera(GameCamera cam)
        {
            Player player = Player.m_localPlayer;
            float level = Tipsy.Level;
            if (player == null || cam == null) return;
            float s = Strength;

            // the swaying view (a little from "merry", a lot when drunk), a hiccup's jolt, and a pulsing field of view
            float sway = F(12f, 100f) * Plugin.Sway.Value / 100f * s;
            float t = Time.time;
            if (sway > 0f || Mathf.Abs(Kick) > 0.01f)
            {
                cam.transform.Rotate(new Vector3(Mathf.Sin(t * 0.53f) * 3.2f * sway + Kick * 4f, Mathf.Sin(t * 0.37f + 1.3f) * 5.5f * sway, Mathf.Sin(t * 0.71f) * 7.5f * sway), Space.Self);
                Kick = Mathf.MoveTowards(Kick, 0f, Time.deltaTime * 3f);
            }
            Camera unity = cam.GetComponent<Camera>();
            if (unity != null)
            {
                float offset = F(30f, 100f) * s * (Mathf.Sin(t * 0.9f) * 6f + Mathf.Sin(t * 2.3f) * 2f) * Plugin.Sway.Value / 100f;
                // the game sets the field of view itself; if it did not this frame, take back what was added last frame first
                float current = unity.fieldOfView;
                float baseFov = Mathf.Approximately(current, _lastFovSet) ? current - _lastFovOffset : current;
                unity.fieldOfView = baseFov + offset;
                _lastFovSet = unity.fieldOfView;
                _lastFovOffset = offset;
            }

            if (Plugin.ScreenFx.Value) Screen.Apply(cam, level, s); else Screen.Restore();
        }

        public static void Reset()
        {
            Screen.Restore();
            Body.Reset();
            Sound.Reset();
            Kick = 0f;
        }
    }

    // ---- the picture: the game's own post-processing, taken over while you are drunk and handed back exactly ----------

    internal static class Screen
    {
        private class Saved
        {
            public PostProcessingProfile Profile;
            public VignetteModel.Settings Vignette; public bool VignetteOn;
            public ChromaticAberrationModel.Settings Chroma; public bool ChromaOn;
            public DepthOfFieldModel.Settings Blur; public bool BlurOn;
            public MotionBlurModel.Settings Ghost; public bool GhostOn;
            public ColorGradingModel.Settings Colour; public bool ColourOn;
        }

        private static Saved _saved;
        private static bool _held;

        public static void Apply(GameCamera cam, float level, float s)
        {
            if (level <= 0f) { Restore(); return; }
            PostProcessingProfile profile = cam.GetComponent<PostProcessingBehaviour>()?.profile;
            if (profile == null) return;
            if (_saved == null || _saved.Profile != profile) { Restore(); _saved = Take(profile); }
            _held = true;
            float t = Time.time;

            // the edges darken and pulse
            float vignette = Fx.F(10f, 100f) * s;
            VignetteModel.Settings v = _saved.Vignette;
            v.intensity = Mathf.Clamp01(Mathf.Max(v.intensity, 0.18f + 0.5f * vignette + 0.07f * Mathf.Sin(t * 1.3f) * vignette));
            v.smoothness = Mathf.Max(v.smoothness, 0.55f);
            profile.vignette.settings = v;
            profile.vignette.enabled = vignette > 0.01f || _saved.VignetteOn;

            // colours split at the edges
            float fringe = Fx.F(22f, 100f) * s;
            ChromaticAberrationModel.Settings c = _saved.Chroma;
            c.intensity = Mathf.Clamp01(Mathf.Max(c.intensity, 0.9f * fringe));
            profile.chromaticAberration.settings = c;
            profile.chromaticAberration.enabled = fringe > 0.01f || _saved.ChromaOn;

            // what is not close goes soft
            float soft = Fx.F(38f, 100f) * s;
            DepthOfFieldModel.Settings b = _saved.Blur;
            b.focusDistance = Mathf.Lerp(b.focusDistance, 3.5f, Mathf.Clamp01(soft * 1.2f));
            b.aperture = Mathf.Lerp(b.aperture, 2.4f, Mathf.Clamp01(soft));
            b.focalLength = Mathf.Max(b.focalLength, 50f);
            b.useCameraFov = false;
            profile.depthOfField.settings = b;
            profile.depthOfField.enabled = soft > 0.02f || _saved.BlurOn;

            // double vision: what was on screen a moment ago stays a moment
            float ghost = Fx.F(42f, 100f) * s;
            MotionBlurModel.Settings m = _saved.Ghost;
            m.frameBlending = Mathf.Clamp01(Mathf.Max(m.frameBlending, 0.85f * ghost));
            m.shutterAngle = Mathf.Max(m.shutterAngle, 200f * ghost);
            m.sampleCount = Mathf.Max(m.sampleCount, 8);
            profile.motionBlur.settings = m;
            profile.motionBlur.enabled = ghost > 0.02f || _saved.GhostOn;

            // the colours get richer, then slide round the wheel
            float gaudy = Fx.F(25f, 100f) * s, hue = Fx.F(62f, 100f) * s;
            ColorGradingModel.Settings g = _saved.Colour;
            g.basic.saturation = g.basic.saturation + 0.45f * gaudy;
            g.basic.hueShift = g.basic.hueShift + 40f * hue * Mathf.Sin(t * 0.45f);
            g.basic.contrast = g.basic.contrast + 0.12f * gaudy;
            profile.colorGrading.settings = g;
        }

        private static Saved Take(PostProcessingProfile p) => new Saved
        {
            Profile = p,
            Vignette = p.vignette.settings, VignetteOn = p.vignette.enabled,
            Chroma = p.chromaticAberration.settings, ChromaOn = p.chromaticAberration.enabled,
            Blur = p.depthOfField.settings, BlurOn = p.depthOfField.enabled,
            Ghost = p.motionBlur.settings, GhostOn = p.motionBlur.enabled,
            Colour = p.colorGrading.settings, ColourOn = p.colorGrading.enabled,
        };

        /// <summary>The picture as the game had it (sober, or the mod unloading).</summary>
        public static void Restore()
        {
            if (_saved != null && _held && _saved.Profile != null)
            {
                PostProcessingProfile p = _saved.Profile;
                p.vignette.settings = _saved.Vignette; p.vignette.enabled = _saved.VignetteOn;
                p.chromaticAberration.settings = _saved.Chroma; p.chromaticAberration.enabled = _saved.ChromaOn;
                p.depthOfField.settings = _saved.Blur; p.depthOfField.enabled = _saved.BlurOn;
                p.motionBlur.settings = _saved.Ghost; p.motionBlur.enabled = _saved.GhostOn;
                p.colorGrading.settings = _saved.Colour; p.colorGrading.enabled = _saved.ColourOn;
            }
            _held = false;
            _saved = null;
        }
    }

    // ---- sound: muffled and wobbling ------------------------------------------------------------------------------

    internal static class Sound
    {
        private static AudioLowPassFilter _low;
        private static AudioChorusFilter _chorus;

        public static void Tick(float level)
        {
            float muffle = Plugin.Muffle.Value ? Fx.F(22f, 100f) * Fx.Strength : 0f;
            if (muffle <= 0.01f) { Reset(); return; }
            if (_low == null)
            {
                AudioListener listener = Object.FindObjectOfType<AudioListener>();
                if (listener == null) return;
                _low = listener.gameObject.AddComponent<AudioLowPassFilter>();
                _chorus = listener.gameObject.AddComponent<AudioChorusFilter>();
                _chorus.dryMix = 0.7f; _chorus.wetMix1 = 0.4f; _chorus.wetMix2 = 0f; _chorus.wetMix3 = 0f; _chorus.delay = 28f;
            }
            _low.cutoffFrequency = Mathf.Lerp(22000f, 1700f, Mathf.Clamp01(muffle));
            _low.lowpassResonanceQ = 1f;
            _chorus.depth = Mathf.Lerp(0.05f, 0.8f, Mathf.Clamp01(muffle));
            _chorus.rate = 0.6f + 0.5f * Mathf.Sin(Time.time * 0.3f);
        }

        public static void Reset()
        {
            if (_low != null) Object.Destroy(_low);
            if (_chorus != null) Object.Destroy(_chorus);
            _low = null; _chorus = null;
        }
    }

    // ---- your body: leaning and weaving, stars, hiccups, a drunken cheer -------------------------------------------

    internal static class Body
    {
        private static Transform _visual;
        private static Quaternion _baseRotation;
        private static GameObject _stars;
        private static ParticleSystem _starSystem;
        private static float _nextHiccup, _nextCheer;

        /// <summary>Every frame, after the animations have posed your body: it leans and weaves.</summary>
        public static void Pose(Player player)
        {
            float lean = Plugin.LeanOn.Value ? Fx.F(25f, 100f) * Fx.Strength : 0f;
            Transform visual = player != null ? VisualOf(player) : null;
            if (visual == null || lean <= 0.01f) { Reset(); return; }
            if (_visual != visual) { Reset(); _visual = visual; _baseRotation = visual.localRotation; }
            float t = Time.time;
            float roll = (Mathf.Sin(t * 1.1f) + 0.5f * Mathf.Sin(t * 2.3f + 1f)) * 9f * lean;
            float pitch = Mathf.Sin(t * 0.8f + 2f) * 4f * lean;
            visual.localRotation = _baseRotation * Quaternion.Euler(pitch, 0f, roll);
        }

        public static void Tick(Player player, float dt)
        {
            float t = Time.time;
            float level = Tipsy.Level;

            // stars circling your head
            float stars = Plugin.StarsOn.Value ? Fx.F(42f, 100f) * Fx.Strength : 0f;
            if (stars > 0.01f) { MakeStars(player); if (_starSystem != null) { var em = _starSystem.emission; em.rateOverTime = Mathf.Lerp(3f, 22f, Mathf.Clamp01(stars)); } }
            else if (_stars != null) { Object.Destroy(_stars); _stars = null; _starSystem = null; }

            // a hiccup now and then: a jolt, a word
            if (Plugin.HiccupsOn.Value && level >= 28f)
            {
                if (_nextHiccup == 0f) _nextHiccup = t + Random.Range(8f, 18f);
                if (t >= _nextHiccup)
                {
                    _nextHiccup = t + Mathf.Lerp(30f, 11f, Fx.F(28f, 100f)) * Random.Range(0.7f, 1.3f);
                    Fx.Kick = Random.value < 0.5f ? 1f : -1f;
                    player.Message(MessageHud.MessageType.TopLeft, Random.value < 0.5f ? "*hic*" : "*hic!*");
                }
            }

            // very drunk, standing still: a drunken cheer
            if (Plugin.HiccupsOn.Value && level >= 60f && t >= _nextCheer)
            {
                _nextCheer = t + Random.Range(45f, 100f);
                if (player.GetVelocity().sqrMagnitude < 0.1f && !player.InAttack() && !player.IsBlocking() && !player.InBed()) player.StartEmote("cheer");
            }
        }

        private static void MakeStars(Player player)
        {
            if (_stars != null || player == null) return;
            Transform head = Find(player.transform, "Head") ?? player.transform;
            _stars = new GameObject("SkalStars");
            _stars.transform.SetParent(head, false);
            _stars.transform.localPosition = head == player.transform ? new Vector3(0f, 1.9f, 0f) : new Vector3(0f, 0.12f, 0f);
            var ps = _stars.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true; main.startLifetime = 1.6f; main.startSpeed = 0f; main.startSize = 0.11f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.3f, 0.95f), new Color(1f, 0.55f, 0.75f, 0.95f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 60;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.4f;
            var orbit = ps.velocityOverLifetime; orbit.enabled = true; orbit.space = ParticleSystemSimulationSpace.Local;
            orbit.x = 0f; orbit.y = 0.12f; orbit.z = 0f; orbit.orbitalX = 0f; orbit.orbitalY = 2.8f; orbit.orbitalZ = 0f;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));
            var renderer = _stars.GetComponent<ParticleSystemRenderer>();
            Material glow = ZNetScene.instance?.GetPrefab("Tankard")?.GetComponentInChildren<ParticleSystemRenderer>(true)?.sharedMaterial;
            if (glow != null) renderer.sharedMaterial = glow;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            _starSystem = ps;
            ps.Play();
        }

        private static Transform _found;

        /// <summary>The body's model: the player's "Visual" child (found once).</summary>
        private static Transform VisualOf(Player player)
        {
            if (_found == null || !_found.IsChildOf(player.transform)) _found = Find(player.transform, "Visual");
            return _found;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { Transform found = Find(child, name); if (found != null) return found; }
            return null;
        }

        public static void Reset()
        {
            if (_visual != null) _visual.localRotation = _baseRotation;
            _visual = null;
            if (_stars != null) Object.Destroy(_stars);
            _stars = null; _starSystem = null;
            _nextHiccup = _nextCheer = 0f;
        }
    }

    // ---- hooks -------------------------------------------------------------------------------------------------------

    // After the game has put the camera and your body where they go this frame.
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class GameCamera_Drunk
    {
        private static void Postfix(GameCamera __instance)
        {
            Fx.Camera(__instance);
            Body.Pose(Player.m_localPlayer);
        }
    }
}
