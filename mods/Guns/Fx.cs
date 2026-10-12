using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// What a shot looks and sounds like: the fire effect prefabs (the sound, built on a copy of the Arbalest's own so it plays through the
    /// game's sound-effects volume; and a small flash), the muzzle of the model in the player's hand, a brass casing that flies out, and
    /// the camera kick. The sound and flash go in the item's trigger effect list, which Fire.cs plays at the muzzle.
    /// </summary>
    internal static class Fx
    {
        internal static GameObject SoundPrefab, FlashPrefab;
        private static readonly List<Object> Owned = new List<Object>();   // clips and materials made here, destroyed with the prefabs
        private static Transform _muzzle, _eject;
        private static Material _brass;

        /// <summary>Builds the two effect prefabs under the holder. glow: a bright additive material to paint the flash with.</summary>
        internal static void Build(ZNetScene scene, Transform holder, Material glow)
        {
            GameObject source = scene.GetPrefab("sfx_arbalest_fire");
            if (source != null)
            {
                SoundPrefab = Object.Instantiate(source, holder);
                SoundPrefab.name = "dh_sfx_machinegun_fire";
                ZSFX z = SoundPrefab.GetComponent<ZSFX>();
                if (z != null)
                {
                    AudioClip[] clips = Sound.Shots();
                    Owned.AddRange(clips);
                    z.m_audioClips = clips;
                    z.m_minPitch = 0.94f; z.m_maxPitch = 1.06f;
                    z.m_maxConcurrentSources = 0;   // (ten a second: the game would thin them out)
                    ApplyVolume(z);
                    Plugin.Volume.SettingChanged += (a, e) => { if (z != null) ApplyVolume(z); };
                }
                AudioSource a = SoundPrefab.GetComponent<AudioSource>();
                if (a != null) { a.minDistance = 6f; a.maxDistance = 160f; }
            }
            else Plugin.Log.LogWarning("the sfx_arbalest_fire prefab was not found, so the gun has no sound");

            FlashPrefab = new GameObject("dh_gun_flash");
            FlashPrefab.transform.SetParent(holder, false);
            FlashPrefab.layer = LayerMask.NameToLayer("effect") >= 0 ? LayerMask.NameToLayer("effect") : 0;
            var timed = FlashPrefab.AddComponent<TimedDestruction>();   // the game's own: removes the flash after a moment
            timed.m_timeout = 0.045f;
            timed.m_triggerOnAwake = true;
            Color hot = new Color(1f, 0.78f, 0.38f);
            Flare(FlashPrefab.transform, glow, "core", Vector3.zero, Vector3.one * 0.10f, hot);
            Flare(FlashPrefab.transform, glow, "spike", new Vector3(0f, 0f, 0.20f), new Vector3(0.05f, 0.05f, 0.40f), hot);
            Flare(FlashPrefab.transform, glow, "side1", new Vector3(0f, 0f, 0.04f), new Vector3(0.30f, 0.025f, 0.05f), hot);
            Flare(FlashPrefab.transform, glow, "side2", new Vector3(0f, 0f, 0.04f), new Vector3(0.025f, 0.30f, 0.05f), hot);
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(FlashPrefab.transform, false);
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = 3.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }

        private static void ApplyVolume(ZSFX z) { z.m_minVol = z.m_maxVol = Mathf.Clamp(Plugin.Volume.Value, 0f, 2f); }

        private static void Flare(Transform parent, Material glow, string name, Vector3 at, Vector3 size, Color color)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name;
            g.layer = parent.gameObject.layer;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = at;
            g.transform.localScale = size;
            Model.Paint(g, glow, color, emissive: true);
            g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        internal static void Clear()
        {
            foreach (Object o in Owned) if (o != null) Object.Destroy(o);
            Owned.Clear();
            SoundPrefab = FlashPrefab = null;
            _muzzle = _eject = null;
        }

        // ---- at the player ----------------------------------------------------------------------------------------

        /// <summary>The "dh_muzzle" and "dh_eject" empties of the model in this player's hand (the game copies the model there when the gun
        /// is equipped). Null when the gun isn't shown yet.</summary>
        internal static Transform Muzzle(Player p) => Find(p, "dh_muzzle", ref _muzzle);
        internal static Transform Eject(Player p) => Find(p, "dh_eject", ref _eject);

        private static Transform Find(Player p, string name, ref Transform cached)
        {
            if (cached != null && cached.IsChildOf(p.transform)) return cached;
            cached = Search(p.transform, name);
            return cached;
        }

        private static Transform Search(Transform t, string name)
        {
            foreach (Transform c in t)
            {
                if (c.name == name) return c;
                Transform r = Search(c, name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>The little push the camera gets with every shot: up, and a touch sideways. Less when aiming.</summary>
        internal static void Kick(Player p)
        {
            float k = Plugin.Recoil.Value * (Aim.Active ? 0.6f : 1f);
            if (k <= 0f) return;
            p.SetMouseLook(new Vector2(Random.Range(-0.10f, 0.10f) * k, Random.Range(0.18f, 0.30f) * k));
        }

        /// <summary>A brass casing flies out of the ejection port.</summary>
        internal static void Casing(Player p)
        {
            Transform port = Eject(p);
            if (port == null) return;
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "dh_casing";
            c.transform.position = port.position;
            c.transform.rotation = Random.rotation;
            c.transform.localScale = new Vector3(0.012f, 0.02f, 0.012f);
            if (_brass == null && MachineGun.BaseMaterial != null)
            {
                Model.Paint(c, MachineGun.BaseMaterial, new Color(0.86f, 0.62f, 0.20f), emissive: false, metallic: 1f, gloss: 0.9f);   // brass, shiny
                _brass = c.GetComponent<Renderer>().sharedMaterial;
                Owned.Add(_brass);
            }
            if (_brass == null) { Object.Destroy(c); return; }   // (no game material to use: no casing rather than a pink one)
            c.GetComponent<Renderer>().sharedMaterial = _brass;
            var motion = c.AddComponent<CasingMotion>();
            motion.Velocity = port.right * Random.Range(1.6f, 2.6f) + port.up * Random.Range(1.2f, 2.0f) - port.forward * Random.Range(0.1f, 0.6f) + p.GetVelocity();
            motion.Spin = Random.insideUnitSphere * 900f;
            Object.Destroy(c, 1.3f);
        }
    }

    /// <summary>A casing in flight: gravity and a tumble, no physics (a few of these a second must never collide with anything).</summary>
    internal sealed class CasingMotion : MonoBehaviour
    {
        internal Vector3 Velocity, Spin;
        private void Update()
        {
            Velocity += Physics.gravity * Time.deltaTime;
            transform.position += Velocity * Time.deltaTime;
            transform.Rotate(Spin * Time.deltaTime, Space.Self);
        }
    }
}
