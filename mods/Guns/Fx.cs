using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// What a shot looks and sounds like: for each gun the fire effect prefabs (the sound, built on a copy of the Arbalest's own so it plays
    /// through the game's sound-effects volume; and a flash), the clicks of reloading, the parts of the model in the player's hand (the
    /// muzzle, where brass comes out, the moving part), a brass casing that flies out, the camera kick and the model's own recoil.
    /// The sound and flash go in the item's trigger effect list, which Fire.cs plays at the muzzle.
    /// </summary>
    internal static class Fx
    {
        private sealed class Effects { public GameObject Sound, Flash; }
        private static readonly Dictionary<string, Effects> ByGun = new Dictionary<string, Effects>();
        private static readonly Dictionary<string, KeyValuePair<EffectList, float>> Reloads = new Dictionary<string, KeyValuePair<EffectList, float>>();   // a recording of a whole reload, and its length
        private sealed class Burst { public AudioClip Loop, Tail; }
        private static readonly Dictionary<string, Burst> Bursts = new Dictionary<string, Burst>();   // a real burst of fire, looped, and its tail
        private sealed class Voice { public GameObject Go; public AudioSource Burst, Tail; }
        private static readonly Dictionary<Player, Voice> Voices = new Dictionary<Player, Voice>();   // a burst of fire for each player who is firing one (others' too, Share.cs)
        /// <summary>What a round makes where it lands (the sound of it hitting, and a spark): put on every round, so the hit is heard where it happens, not at the gun.</summary>
        internal static EffectList Impact;
        private static readonly Dictionary<Sound.Click, EffectList> Clicks = new Dictionary<Sound.Click, EffectList>();
        private static readonly List<Object> Owned = new List<Object>();   // clips and materials made here, destroyed with the prefabs
        private static readonly Dictionary<Player, Dictionary<string, Transform>> Found = new Dictionary<Player, Dictionary<string, Transform>>();
        private static readonly Dictionary<Color, Material> Brass = new Dictionary<Color, Material>();
        private static readonly AudioSource[] _spin = new AudioSource[3];

        /// <summary>The model's own kick (0 to 1, dies away): Aim.Pose pushes the gun back and up with it.</summary>
        internal static float Recoil;

        /// <summary>Builds the effect prefabs of every gun under the holder. glow: a bright additive material to paint the flashes with.</summary>
        internal static void Build(ZNetScene scene, Transform holder, Material glow)
        {
            GameObject source = scene.GetPrefab("sfx_arbalest_fire");
            if (source == null) Plugin.Log.LogWarning("the sfx_arbalest_fire prefab was not found, so the guns have no sound");
            foreach (GunDef d in Arsenal.All)
            {
                var e = new Effects();
                if (source != null)
                {
                    e.Sound = SoundPrefab(source, holder, "dh_sfx_" + d.Id + "_fire", Sound.Shots(d), d.Sound.MinPitch, d.Sound.MaxPitch, true, 6f * Mathf.Sqrt(d.Noise / 25f), 90f + d.Noise * 3f);
                }
                e.Flash = FlashPrefab(holder, glow, d);
                ByGun[d.Id] = e;
            }
            foreach (GunDef gd in Arsenal.All)
            {
                if (gd.LoopRate <= 0f) continue;
                AudioClip loop = Sound.Recording("loop_" + gd.Id + ".wav"), tail = Sound.Recording("tail_" + gd.Id + ".wav");
                if (loop != null && tail != null) { Owned.Add(loop); Owned.Add(tail); Bursts[gd.Id] = new Burst { Loop = loop, Tail = tail }; }
            }
            if (source != null)
            {
                var hits = new List<AudioClip>();
                for (int i = 0; i < 4; i++) { AudioClip c = Sound.Recording("impact_" + i + ".wav"); if (c == null) break; hits.Add(c); }
                var impact = new List<EffectList.EffectData>();
                if (hits.Count > 0)
                {
                    GameObject sound = SoundPrefab(source, holder, "dh_sfx_gun_impact", hits.ToArray(), 0.9f, 1.15f, false, 4f, 70f);
                    ZSFX iz = sound.GetComponent<ZSFX>();
                    if (iz != null) iz.m_maxConcurrentSources = 6;   // (a minigun lands twenty-five a second: only a few at once are heard)
                    impact.Add(new EffectList.EffectData { m_prefab = sound, m_enabled = true, m_variant = -1 });
                }
                GameObject spark = new GameObject("dh_gun_spark");
                spark.transform.SetParent(holder, false);
                spark.layer = LayerMask.NameToLayer("effect") >= 0 ? LayerMask.NameToLayer("effect") : 0;
                var timed = spark.AddComponent<TimedDestruction>();
                timed.m_timeout = 0.07f;
                timed.m_triggerOnAwake = true;
                Flare(spark.transform, glow, "core", Vector3.zero, Vector3.one * 0.06f, new Color(1f, 0.8f, 0.45f));
                impact.Add(new EffectList.EffectData { m_prefab = spark, m_enabled = true, m_variant = -1 });
                Impact = new EffectList { m_effectPrefabs = impact.ToArray() };
            }
            if (source != null)
                foreach (string name in new[] { "pistol", "rifle" })
                {
                    AudioClip whole = Sound.Recording("reload_" + name + ".wav");
                    if (whole == null) continue;
                    GameObject go = SoundPrefab(source, holder, "dh_sfx_reload_" + name, new[] { whole }, 0.98f, 1.02f, false, 3f, 40f);
                    Reloads[name] = new KeyValuePair<EffectList, float>(new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = go, m_enabled = true, m_variant = -1 } } }, whole.length);
                }
            if (source != null)
                foreach (Sound.Click kind in System.Enum.GetValues(typeof(Sound.Click)))
                {
                    int variants = kind == Sound.Click.Casing || kind == Sound.Click.Shell ? 3 : 2;   // (brass and rounds never sound the same twice)
                    var clips = new AudioClip[variants];
                    for (int v = 0; v < variants; v++) clips[v] = Sound.Make(kind, v);
                    bool brass = kind == Sound.Click.Casing;
                    GameObject click = SoundPrefab(source, holder, "dh_sfx_gun_" + kind, clips, brass ? 0.9f : 0.96f, brass ? 1.12f : 1.04f, false, 3f, 40f);
                    Clicks[kind] = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = click, m_enabled = true, m_variant = -1 } } };
                }
        }

        private static GameObject SoundPrefab(GameObject source, Transform holder, string name, AudioClip[] clips, float minPitch, float maxPitch, bool isShot, float minDistance, float maxDistance)
        {
            GameObject go = Object.Instantiate(source, holder);
            go.name = name;
            // Local only: the copy isn't in ZNetScene, so a networked one made a ZDO per shot that no other game could build
            // ("Missing prefab hash" logged every frame until it died). Every player plays the sound themselves (Share.OnShot).
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null) Object.DestroyImmediate(view);
            ZSFX z = go.GetComponent<ZSFX>();
            if (z != null)
            {
                Owned.AddRange(clips);
                z.m_audioClips = clips;
                z.m_minPitch = minPitch; z.m_maxPitch = maxPitch;
                z.m_maxConcurrentSources = 0;   // (many a second: the game would thin them out)
                ApplyVolume(z, isShot);
                Plugin.Volume.SettingChanged += (a, e) => { if (z != null) ApplyVolume(z, isShot); };
            }
            AudioSource a2 = go.GetComponent<AudioSource>();
            if (a2 != null) { a2.minDistance = minDistance; a2.maxDistance = maxDistance; }
            return go;
        }

        private static void ApplyVolume(ZSFX z, bool isShot) { z.m_minVol = z.m_maxVol = Mathf.Clamp(Plugin.Volume.Value * (isShot ? 1f : 0.7f), 0f, 2f); }

        private static GameObject FlashPrefab(Transform holder, Material glow, GunDef d)
        {
            var flash = new GameObject("dh_flash_" + d.Id);
            flash.transform.SetParent(holder, false);
            flash.layer = LayerMask.NameToLayer("effect") >= 0 ? LayerMask.NameToLayer("effect") : 0;
            var timed = flash.AddComponent<TimedDestruction>();   // the game's own: removes the flash after a moment
            timed.m_timeout = 0.045f;
            timed.m_triggerOnAwake = true;
            float s = d.FlashSize;
            Color hot = new Color(1f, 0.78f, 0.38f);
            Flare(flash.transform, glow, "core", Vector3.zero, Vector3.one * 0.10f * s, hot);
            Flare(flash.transform, glow, "spike", new Vector3(0f, 0f, 0.20f * s), new Vector3(0.05f, 0.05f, 0.40f) * s, hot);
            Flare(flash.transform, glow, "side1", new Vector3(0f, 0f, 0.04f * s), new Vector3(0.30f * s, 0.025f, 0.05f), hot);
            Flare(flash.transform, glow, "side2", new Vector3(0f, 0f, 0.04f * s), new Vector3(0.025f, 0.30f * s, 0.05f), hot);
            var light = new GameObject("light").AddComponent<Light>();
            light.transform.SetParent(flash.transform, false);
            light.color = new Color(1f, 0.72f, 0.35f);
            light.range = 3.5f * s;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            return flash;
        }

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

        /// <summary>The effects a gun plays when it fires: its sound and flash.</summary>
        internal static EffectList.EffectData[] FireEffects(GunDef d)
        {
            var list = new List<EffectList.EffectData>();
            if (ByGun.TryGetValue(d.Id, out Effects e))
            {
                if (e.Sound != null && !Bursts.ContainsKey(d.Id)) list.Add(new EffectList.EffectData { m_prefab = e.Sound, m_enabled = true, m_variant = -1 });
                if (e.Flash != null) list.Add(new EffectList.EffectData { m_prefab = e.Flash, m_enabled = true, m_variant = -1 });
            }
            return list.ToArray();
        }

        /// <summary>The click of a gun's mechanism, at a place.</summary>
        internal static void Click(Sound.Click kind, Vector3 at)
        {
            if (Clicks.TryGetValue(kind, out EffectList list)) list.Create(at, Quaternion.identity);
        }

        private static readonly Dictionary<string, EffectList> Played = new Dictionary<string, EffectList>();

        /// <summary>What another player's shot makes at their muzzle (the gun's sound and flash, as FireEffects lists them for the item).</summary>
        internal static void Shot(Player p, GunDef d, bool second)
        {
            Transform muzzle = Muzzle(p, second);
            if (muzzle == null) return;
            if (!Played.TryGetValue(d.Id, out EffectList list)) Played[d.Id] = list = new EffectList { m_effectPrefabs = FireEffects(d) };
            list.Create(muzzle.position, muzzle.rotation);
        }

        internal static bool HasBurst(GunDef d) => Bursts.ContainsKey(d.Id);

        /// <summary>
        /// A gun that fires fast plays a real recorded burst instead of a sound for each round (twenty-five single shots a second sound like
        /// rounds hitting a wall): the loop while it fires, at the pitch that makes its rate match the recording, and the tail when it stops.
        /// </summary>
        internal static void Fire(Player p, GunDef d, bool firing)
        {
            if (p == null || !Bursts.TryGetValue(d.Id, out Burst b)) return;
            Voices.TryGetValue(p, out Voice v);
            if (!firing)
            {
                if (v != null && v.Burst != null && v.Burst.isPlaying)
                {
                    v.Burst.Stop();
                    v.Tail.transform.position = v.Burst.transform.position;
                    v.Tail.clip = b.Tail;
                    v.Tail.pitch = v.Burst.pitch;
                    v.Tail.volume = v.Burst.volume;
                    v.Tail.Play();
                }
                return;
            }
            if (v == null || v.Go == null)
            {
                var go = new GameObject("QuadsGunsBurst");
                Object.DontDestroyOnLoad(go);
                v = new Voice { Go = go, Burst = go.AddComponent<AudioSource>(), Tail = go.AddComponent<AudioSource>() };
                foreach (AudioSource s in new[] { v.Burst, v.Tail })
                {
                    s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 6f; s.maxDistance = 160f; s.dopplerLevel = 0f;
                }
                v.Burst.loop = true;
                Voices[p] = v;
            }
            Transform muzzle = Muzzle(p);
            if (muzzle != null) v.Burst.transform.position = muzzle.position;
            v.Burst.clip = b.Loop;
            v.Burst.pitch = Mathf.Clamp(d.RateNow / d.LoopRate, 0.6f, 2.5f);
            v.Burst.volume = Mathf.Clamp01(0.95f * Plugin.Volume.Value * AudioMan.GetSFXVolume());
            if (!v.Burst.isPlaying) { if (v.Tail.isPlaying) v.Tail.Stop(); v.Burst.Play(); }
        }

        /// <summary>This player's burst stops at once, with no tail (the gun was put away).</summary>
        internal static void StopBurst(Player p)
        {
            if (p != null && Voices.TryGetValue(p, out Voice v) && v.Burst != null && v.Burst.isPlaying) v.Burst.Stop();
        }

        /// <summary>Forgets what belongs to players who are gone (their parts, their burst's sound source).</summary>
        internal static void Prune()
        {
            List<Player> gone = null;
            foreach (Player p in Found.Keys) if (p == null) (gone ?? (gone = new List<Player>())).Add(p);
            if (gone != null) foreach (Player p in gone) Found.Remove(p);
            gone = null;
            foreach (Player p in Voices.Keys) if (p == null) (gone ?? (gone = new List<Player>())).Add(p);
            if (gone != null) foreach (Player p in gone) { if (Voices[p].Go != null) Object.Destroy(Voices[p].Go); Voices.Remove(p); }
        }

        internal static bool HasReload(string name) => name != null && Reloads.ContainsKey(name);

        /// <summary>A recording of a whole reload, at a place; returns how long it is.</summary>
        internal static float Reload(string name, Vector3 at)
        {
            if (!Reloads.TryGetValue(name, out KeyValuePair<EffectList, float> r)) return 0f;
            r.Key.Create(at, Quaternion.identity);
            return r.Value;
        }

        internal static void Clear()
        {
            Whirr(0f);
            if (_spin[0] != null) { Object.Destroy(_spin[0].gameObject); _spin[0] = _spin[1] = _spin[2] = null; }
            foreach (Object o in Owned) if (o != null) Object.Destroy(o);
            Owned.Clear();
            ByGun.Clear(); Clicks.Clear(); Reloads.Clear(); Bursts.Clear(); Impact = null;
            foreach (Voice v in Voices.Values) if (v.Go != null) Object.Destroy(v.Go);
            Voices.Clear(); Found.Clear(); Brass.Clear(); Played.Clear();
            Model.Clear();
        }

        // ---- at the player ----------------------------------------------------------------------------------------

        /// <summary>A named empty or part of the model in this player's hand (the game copies the model there when the gun is equipped). Null when the gun isn't shown yet.</summary>
        internal static Transform Part(Player p, string name)
        {
            if (!Found.TryGetValue(p, out Dictionary<string, Transform> parts)) Found[p] = parts = new Dictionary<string, Transform>();
            if (parts.TryGetValue(name, out Transform t) && t != null && t.IsChildOf(p.transform)) return t;
            t = Search(p.transform, name);
            parts[name] = t;
            return t;
        }

        internal static Transform Muzzle(Player p, bool second = false) => Part(p, second ? "dh_muzzle2" : "dh_muzzle");
        internal static Transform Eject(Player p, bool second = false) => Part(p, second ? "dh_eject2" : "dh_eject");

        /// <summary>The root of the model in the hand (the game names the copy "model(Clone)").</summary>
        internal static Transform ModelRoot(Player p)
        {
            Transform t = Muzzle(p);
            while (t != null && !t.name.StartsWith("model")) t = t.parent;
            return t;
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
        internal static void Kick(Player p, GunDef d)
        {
            Recoil = Mathf.Min(1.5f, Recoil + Mathf.Clamp(d.Kick, 0.4f, 2f) * 0.6f);
            float k = Plugin.Recoil.Value * d.Kick * (Aim.Active ? 0.6f : 1f);
            if (k <= 0f) return;
            p.SetMouseLook(new Vector2(Random.Range(-0.10f, 0.10f) * k, Random.Range(0.18f, 0.30f) * k));
        }

        /// <summary>A brass casing flies out of the ejection port.</summary>
        internal static void Casing(Player p, GunDef d, bool second)
        {
            if (d.CasingSize <= 0f) return;
            Transform port = Eject(p, second);
            if (port == null) return;
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(c.GetComponent<Collider>());
            c.name = "dh_casing";
            c.transform.position = port.position;
            c.transform.rotation = Random.rotation;
            c.transform.localScale = new Vector3(0.012f, 0.02f, 0.012f) * d.CasingSize;
            if (!Brass.TryGetValue(d.CasingColor, out Material m) || m == null)
            {
                if (Gun.BaseMaterial == null) { Object.Destroy(c); return; }   // (no game material to use: no casing rather than a pink one)
                Model.Paint(c, Gun.BaseMaterial, d.CasingColor, emissive: false, metallic: 1f, gloss: 0.9f);   // shiny
                m = c.GetComponent<Renderer>().sharedMaterial;
                Brass[d.CasingColor] = m;
                Owned.Add(m);
            }
            c.GetComponent<Renderer>().sharedMaterial = m;
            var motion = c.AddComponent<CasingMotion>();
            motion.Velocity = port.right * Random.Range(1.6f, 2.6f) + port.up * Random.Range(1.2f, 2.0f) - port.forward * Random.Range(0.1f, 0.6f) + p.GetVelocity();
            motion.Spin = Random.insideUnitSphere * 900f;
            Object.Destroy(c, 1.3f);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Land(port.position + motion.Velocity * 0.25f, p.transform.position.y));
        }

        /// <summary>The brass hits the ground a moment later: a few bright bounces where it lands.</summary>
        private static System.Collections.IEnumerator Land(Vector3 at, float groundY)
        {
            yield return new WaitForSeconds(Random.Range(0.35f, 0.55f));
            Click(Sound.Click.Casing, new Vector3(at.x, groundY, at.z));
        }

        /// <summary>
        /// The minigun motor, three loops that follow how fast the barrels turn (0 stops them): the hum climbs gently, the whine climbs much
        /// further, and the air only builds near full speed, so winding up is a rising howl and winding down falls away.
        /// </summary>
        internal static void Whirr(float spin)
        {
            if (spin <= 0.01f)
            {
                foreach (AudioSource s in _spin) if (s != null && s.isPlaying) s.Stop();
                return;
            }
            if (_spin[0] == null)
            {
                var go = new GameObject("QuadsGunsWhirr");
                Object.DontDestroyOnLoad(go);
                AudioClip[] clips = Sound.SpinLoops();
                for (int i = 0; i < 3; i++)
                {
                    Owned.Add(clips[i]);
                    AudioSource s = go.AddComponent<AudioSource>();
                    s.clip = clips[i];
                    s.loop = true;
                    s.spatialBlend = 0f;
                    _spin[i] = s;
                }
            }
            float master = Plugin.Volume.Value * AudioMan.GetSFXVolume();
            SetLoop(_spin[0], Mathf.Lerp(0.5f, 1.5f, spin), Mathf.Sqrt(spin) * 0.55f * master);
            SetLoop(_spin[1], Mathf.Lerp(0.3f, 2.4f, Mathf.Pow(spin, 1.3f)), Mathf.Pow(spin, 1.4f) * 0.38f * master);
            SetLoop(_spin[2], Mathf.Lerp(0.7f, 1.3f, spin), Mathf.Pow(spin, 2.5f) * 0.45f * master);
        }

        private static void SetLoop(AudioSource s, float pitch, float volume)
        {
            s.pitch = pitch;
            s.volume = Mathf.Clamp01(volume);
            if (!s.isPlaying) s.Play();
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
