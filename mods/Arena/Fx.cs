using System.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Fire and other small effects made in code: a flame (particles with the game's own fire material, and a flickering light), plain and
    /// glowing materials, and the game's spawn effect where it has one.
    /// </summary>
    internal static class Fx
    {
        private static Material _fire, _glow;

        /// <summary>The game's torch fire material (from its wooden standing torch), else a soft additive one of our own.</summary>
        internal static Material FireMaterial()
        {
            if (_fire != null) return _fire;
            GameObject torch = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("piece_groundtorch_wood") : null;
            if (torch != null)
                foreach (ParticleSystemRenderer r in torch.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (r.sharedMaterial != null && r.sharedMaterial.name.ToLowerInvariant().Contains("fire") || r.sharedMaterial != null && r.sharedMaterial.name.ToLowerInvariant().Contains("flame")) { _fire = r.sharedMaterial; return _fire; }
            if (torch != null)
            {
                ParticleSystemRenderer any = torch.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(r => r.sharedMaterial != null);
                if (any != null) { _fire = any.sharedMaterial; return _fire; }
            }
            Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;   // (none to be had: the particles keep the default)
            _fire = new Material(shader) { name = "ArenaFire", mainTexture = SoftDot() };
            return _fire;
        }

        /// <summary>An unlit colour that glows (embers, the ring on the ground); without that shader, a copy of a game material in the colour (null: none at all).</summary>
        internal static Material Glow(Color c)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) return new Material(shader) { color = c, name = "ArenaGlow" };
            Material basis = ZNetScene.instance?.GetPrefab("Wood")?.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
            return basis != null ? new Material(basis) { color = c, name = "ArenaGlow", mainTexture = null } : null;
        }

        private static Texture2D SoftDot()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        /// <summary>A flame at a point on a parent: rising particles of fire, and (if asked) a warm flickering light.</summary>
        internal static void Flame(Transform parent, Vector3 at, float size, bool light)
        {
            var go = new GameObject("Flame");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // (a particle cone points along its z: up)
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.duration = 1f; main.loop = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * size, 0.9f * size);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f * size, 0.42f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f, 0.9f), new Color(1f, 0.45f, 0.12f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 50;
            main.gravityModifier = -0.12f;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 28f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f; shape.radius = 0.09f * size;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.9f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.15f), 0.45f), new GradientColorKey(new Color(0.6f, 0.15f, 0.05f), 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(gradient);
            ParticleSystem.SizeOverLifetimeModule sizeOver = ps.sizeOverLifetime;
            sizeOver.enabled = true;
            sizeOver.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = FireMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();

            if (!light) return;
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = at + Vector3.up * 0.25f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.3f); l.range = 6f * size; l.intensity = 1.3f; l.shadows = LightShadows.None;
            lightGo.AddComponent<Flicker>();
        }

        /// <summary>The game's own spawn puff (smoke and sound), where it has them.</summary>
        internal static void SpawnPuff(Vector3 at)
        {
            if (ZNetScene.instance == null) return;
            foreach (string name in new[] { "vfx_spawn", "sfx_spawn" })
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab != null) Object.Instantiate(prefab, at, Quaternion.identity);
            }
        }

        internal static void Clear()
        {
            if (_glow != null) Object.Destroy(_glow);
            _glow = null;
            if (_fire != null && _fire.name == "ArenaFire") Object.Destroy(_fire);
            _fire = null;
        }
    }

    /// <summary>A light that flickers like a fire.</summary>
    internal class Flicker : MonoBehaviour
    {
        private Light _light;
        private float _base, _seed;

        private void Awake() { _light = GetComponent<Light>(); _base = _light != null ? _light.intensity : 1f; _seed = Random.value * 100f; }

        private void Update()
        {
            if (_light == null) return;
            float t = Time.time * 7f + _seed;
            _light.intensity = _base * (0.82f + 0.12f * Mathf.PerlinNoise(t, 0.3f) + 0.08f * Mathf.Sin(t * 2.7f));
        }
    }
}
