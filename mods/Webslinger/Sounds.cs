using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Webslinger
{
    internal enum Sfx { Thwip, ZipThwip, Stick, Cling, Snap, Whoosh, Kick, Tingle }

    /// <summary>
    /// The web's own sounds, made in code when the mod starts (no recordings: nothing anyone else owns): the thwip of a web shot, the
    /// sticky thunk as it holds, the snap as it's let go, the whoosh of a perfect release, a tingle for spider-sense, and the wind rushing
    /// past at speed. Each plays where it happens (other players hear your webs), a little higher or lower each time, through the game's
    /// sound effects volume (the mixer group taken from one of the game's own sounds).
    ///
    /// The clips are noise and tones shaped by a filter whose pitch sweeps (a state-variable band-pass), with short envelopes: a thwip is a
    /// burst of noise falling from a hiss to a soft "ff"; a thunk a low falling tone with a crackle; the wind brown noise, looped.
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = 44100;
        private static readonly Dictionary<Sfx, AudioClip[]> Clips = new Dictionary<Sfx, AudioClip[]>();
        private static AudioClip _windClip;
        private static AudioMixerGroup _group;
        private static bool _groupLooked;
        private static GameObject _root;
        private static AudioSource _wind;

        public static void Reset()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _wind = null;
            foreach (AudioClip[] set in Clips.Values) foreach (AudioClip c in set) if (c != null) Object.Destroy(c);
            Clips.Clear();
            if (_windClip != null) Object.Destroy(_windClip);
            _windClip = null;
            _group = null;
            _groupLooked = false;
        }

        public static void Play(Sfx sfx, Vector3 at, float volume = 1f)
        {
            if (!Plugin.Sounds.Value || Plugin.SoundVolume.Value <= 0f) return;
            AudioClip[] set = Get(sfx);
            if (set == null || set.Length == 0) return;
            AudioClip clip = set[Random.Range(0, set.Length)];
            var go = new GameObject("webslinger_sfx");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.outputAudioMixerGroup = Group();
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = sfx == Sfx.Tingle ? 10f : 45f;
            src.dopplerLevel = 0f;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.volume = Mathf.Clamp01(volume * Plugin.SoundVolume.Value);
            src.Play();
            Object.Destroy(go, clip.length / src.pitch + 0.1f);
        }

        /// <summary>Every frame: the wind past your ears, louder and higher the faster you fly (on a web or off one).</summary>
        public static void Wind(Player p)
        {
            float speed = p != null && Web.State != Move.None && Web.State != Move.Crawl ? Web.Body(p).linearVelocity.magnitude : 0f;
            float want = Plugin.Sounds.Value ? Mathf.Clamp01((speed - 9f) / 22f) * 0.55f * Plugin.SoundVolume.Value : 0f;
            if (_wind == null)
            {
                if (want <= 0f) return;
                Root();
                _wind = _root.AddComponent<AudioSource>();
                _wind.clip = _windClip = _windClip ?? WindClip();
                _wind.loop = true;
                _wind.spatialBlend = 0f;
                _wind.outputAudioMixerGroup = Group();
                _wind.volume = 0f;
                _wind.Play();
            }
            _wind.volume = Mathf.MoveTowards(_wind.volume, want, Time.deltaTime * 1.5f);
            _wind.pitch = 0.8f + Mathf.Clamp01((speed - 9f) / 25f) * 0.6f;
        }

        private static void Root()
        {
            if (_root != null) return;
            _root = new GameObject("webslinger_sounds");
            Object.DontDestroyOnLoad(_root);
        }

        /// <summary>The game's sound effects mixer group (its volume slider), from one of its own sounds.</summary>
        private static AudioMixerGroup Group()
        {
            if (_groupLooked) return _group;
            if (ZNetScene.instance == null) return null;
            _groupLooked = true;
            foreach (string name in new[] { "sfx_arrow_hit", "sfx_grapplinghook_fire", "sfx_wood_hit" })
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                AudioSource src = prefab != null ? prefab.GetComponentInChildren<AudioSource>(true) : null;
                if (src != null && src.outputAudioMixerGroup != null) { _group = src.outputAudioMixerGroup; break; }
            }
            return _group;
        }

        private static AudioClip[] Get(Sfx sfx)
        {
            if (Clips.TryGetValue(sfx, out AudioClip[] set)) return set;
            var made = new AudioClip[3];
            for (int i = 0; i < made.Length; i++) made[i] = Make(sfx, 1000 + (int)sfx * 10 + i);
            Clips[sfx] = made;
            return made;
        }

        // ---- making the clips ----

        private static AudioClip Make(Sfx sfx, int seed)
        {
            var rng = new System.Random(seed);
            switch (sfx)
            {
                case Sfx.Thwip: return Thwip("webslinger_thwip", rng, 0.17f, 5200f, 900f, 1f);
                case Sfx.ZipThwip: return Thwip("webslinger_zip", rng, 0.28f, 4200f, 600f, 1f);
                case Sfx.Stick: return Thunk("webslinger_stick", rng, 1f);
                case Sfx.Cling: return Thunk("webslinger_cling", rng, 0.6f);
                case Sfx.Snap: return Snap("webslinger_snap", rng);
                case Sfx.Whoosh: return Whoosh("webslinger_whoosh", rng, 0.55f);
                case Sfx.Kick: return Whoosh("webslinger_kick", rng, 0.3f);
                case Sfx.Tingle: return Tingle("webslinger_tingle", rng);
            }
            return null;
        }

        private static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        /// <summary>A band-pass that can sweep (Chamberlin's state-variable filter).</summary>
        private sealed class Filter
        {
            private float _low, _band;
            public float Q = 0.5f;   // (damping: lower rings more)
            public float Band(float input, float freq)
            {
                float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(freq, Rate / 6f) / Rate);
                _low += f * _band;
                float high = input - _low - Q * _band;
                _band += f * high;
                return _band;
            }
            public float Low => _low;
        }

        /// <summary>A web shot: a burst of noise whose ring falls fast from a hiss to a soft "ff", over a quick tone.</summary>
        private static AudioClip Thwip(string name, System.Random rng, float seconds, float from, float to, float gain)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var filter = new Filter { Q = 0.35f };
            float phase = 0f, jitter = 0.9f + (float)rng.NextDouble() * 0.2f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float freq = Mathf.Lerp(from, to, Mathf.Pow(t, 0.45f)) * jitter;
                float env = Mathf.Min(1f, i / (Rate * 0.004f)) * Mathf.Pow(1f - t, 2.2f);
                float hiss = filter.Band(Noise(rng), freq) * 2.2f;
                phase += 2f * Mathf.PI * freq * 0.35f / Rate;
                float tone = Mathf.Sin(phase) * 0.25f * Mathf.Pow(1f - t, 4f);
                data[i] = (hiss + tone) * env * gain;
            }
            return Clip(name, Normalize(data, 0.8f));
        }

        /// <summary>The web holding: a low falling thump with a short sticky crackle.</summary>
        private static AudioClip Thunk(string name, System.Random rng, float gain)
        {
            int n = (int)(Rate * 0.16f);
            var data = new float[n];
            var filter = new Filter { Q = 0.6f };
            float phase = 0f, basis = 130f + (float)rng.NextDouble() * 30f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                phase += 2f * Mathf.PI * basis * (1f - 0.4f * t) / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 9f);
                float crackle = rng.NextDouble() < 0.02 * (1f - t) ? Noise(rng) * 0.9f : 0f;
                float click = filter.Band(Noise(rng), 1800f) * Mathf.Exp(-t * 40f) * 1.5f;
                data[i] = (thump + crackle + click) * Mathf.Min(1f, i / (Rate * 0.002f)) * gain;
            }
            return Clip(name, Normalize(data, 0.75f * gain));
        }

        /// <summary>The web let go: a short bright snap with a little twang.</summary>
        private static AudioClip Snap(string name, System.Random rng)
        {
            int n = (int)(Rate * 0.1f);
            var data = new float[n];
            var filter = new Filter { Q = 0.3f };
            float phase = 0f, twang = 520f + (float)rng.NextDouble() * 80f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float snap = filter.Band(Noise(rng), 3200f) * Mathf.Exp(-t * 30f) * 2f;
                phase += 2f * Mathf.PI * twang * (1f + 0.3f * (1f - t)) / Rate;
                data[i] = snap + Mathf.Sin(phase) * 0.3f * Mathf.Exp(-t * 12f);
            }
            return Clip(name, Normalize(data, 0.5f));
        }

        /// <summary>Air rushing past: noise through a band that sweeps up and back down, swelling and fading.</summary>
        private static AudioClip Whoosh(string name, System.Random rng, float seconds)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var filter = new Filter { Q = 0.5f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float freq = 350f + 1500f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.7f));
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.6f));
                data[i] = filter.Band(Noise(rng), freq) * env * env;
            }
            return Clip(name, Normalize(data, 0.7f));
        }

        /// <summary>Spider-sense: a shimmer of high tones, trembling, rising out of nothing and fading.</summary>
        private static AudioClip Tingle(string name, System.Random rng)
        {
            int n = (int)(Rate * 0.7f);
            var data = new float[n];
            float[] freqs = { 2093f, 3136f, 4186f, 5274f };
            var phases = new float[freqs.Length];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Min(1f, t / 0.05f) * Mathf.Pow(1f - t, 1.5f);
                float tremble = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 28f * i / Rate);
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    phases[k] += 2f * Mathf.PI * freqs[k] * (1f + 0.004f * Mathf.Sin(2f * Mathf.PI * (6f + k) * i / Rate)) / Rate;
                    s += Mathf.Sin(phases[k]) / (k + 1.5f);
                }
                data[i] = s * env * tremble;
            }
            return Clip(name, Normalize(data, 0.45f));
        }

        /// <summary>Two seconds of wind (brown noise, softly swelling) that loops without a seam.</summary>
        private static AudioClip WindClip()
        {
            var rng = new System.Random(7);
            int n = Rate * 2, fade = Rate / 4;
            var raw = new float[n + fade];
            var filter = new Filter { Q = 0.8f };
            float brown = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                brown = Mathf.Clamp(brown + Noise(rng) * 0.04f, -1f, 1f) * 0.995f;
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * i / n * 2f);
                raw[i] = (brown * 0.7f + filter.Band(Noise(rng), 700f + 300f * swell) * 0.5f) * swell;
            }
            // (the tail crossfaded into the head: the loop's end runs straight into its start)
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = raw[i];
                if (i < fade) { float k = i / (float)fade; v = raw[i] * k + raw[n + i] * (1f - k); }
                data[i] = v;
            }
            return Clip("webslinger_wind", Normalize(data, 0.6f));
        }

        private static float[] Normalize(float[] data, float peak)
        {
            float max = 0.0001f;
            foreach (float v in data) max = Math.Max(max, Math.Abs(v));
            for (int i = 0; i < data.Length; i++) data[i] = data[i] / max * peak;
            return data;
        }

        private static AudioClip Clip(string name, float[] data)
        {
            // (filled through the reader callback: SetData's newer overloads don't compile against the game's netstandard)
            int at = 0;
            return AudioClip.Create(name, data.Length, 1, Rate, false, buffer =>
            {
                for (int i = 0; i < buffer.Length; i++) buffer[i] = at + i < data.Length ? data[at + i] : 0f;
                at += buffer.Length;
            }, position => at = position);
        }
    }
}
