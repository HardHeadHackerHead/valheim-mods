using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Skateboards
{
    internal enum Sfx { Pop, Land, Flick, Bail, Clatter, Rolling, Grinding }

    /// <summary>
    /// The board's own sounds, made in code when first needed (no recordings: nothing anyone else owns): the woody pop of an ollie (the
    /// tail striking the ground), the clack of the wheels landing, the flick of a flip, the thud of a bail and the board clattering away,
    /// and two loops that play from every skater's board, louder and higher with speed: the wheels' roar and the grind's scrape. Each
    /// plays where it happens (others hear your board), through the game's sound effects volume (the mixer group taken from one of the
    /// game's own sounds).
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = 44100;
        private static readonly Dictionary<Sfx, AudioClip[]> Clips = new Dictionary<Sfx, AudioClip[]>();
        private static AudioMixerGroup _group;
        private static bool _groupLooked;

        public static void Reset()
        {
            foreach (AudioClip[] set in Clips.Values) foreach (AudioClip c in set) if (c != null) Object.Destroy(c);
            Clips.Clear();
            _group = null;
            _groupLooked = false;
        }

        private static float Volume => Plugin.Sounds.Value ? Plugin.SoundVolume.Value : 0f;

        public static void Play(Sfx sfx, Vector3 at, float volume = 1f)
        {
            if (Volume <= 0f) return;
            AudioClip[] set = Get(sfx);
            if (set == null || set.Length == 0) return;
            AudioClip clip = set[Random.Range(0, set.Length)];
            var go = new GameObject("skateboards_sfx");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.outputAudioMixerGroup = Group();
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 40f;
            src.dopplerLevel = 0f;
            src.pitch = Random.Range(0.92f, 1.08f);
            src.volume = Mathf.Clamp01(volume * Volume);
            src.Play();
            Object.Destroy(go, clip.length / src.pitch + 0.1f);
        }

        /// <summary>A looping sound on something (a board), silent until Set gives it a volume.</summary>
        public static AudioSource Loop(GameObject host, Sfx sfx)
        {
            AudioClip[] set = Get(sfx);
            if (set == null || set.Length == 0) return null;
            var src = host.AddComponent<AudioSource>();
            src.clip = set[0];
            src.loop = true;
            src.outputAudioMixerGroup = Group();
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 30f;
            src.dopplerLevel = 0f;
            src.volume = 0f;
            src.time = Random.Range(0f, set[0].length * 0.9f);
            return src;
        }

        public static void Set(AudioSource src, float volume, float pitch)
        {
            if (src == null) return;
            float want = volume * Volume;
            src.volume = Mathf.MoveTowards(src.volume, want, Time.deltaTime * 3f);
            src.pitch = pitch;
            if (src.volume > 0.001f) { if (!src.isPlaying) src.Play(); }
            else if (src.isPlaying) src.Pause();
        }

        /// <summary>The game's sound effects mixer group (its volume slider), from one of its own sounds.</summary>
        private static AudioMixerGroup Group()
        {
            if (_groupLooked) return _group;
            if (ZNetScene.instance == null) return null;
            _groupLooked = true;
            foreach (string name in new[] { "sfx_wood_hit", "sfx_arrow_hit", "sfx_club_hit" })
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
            int count = sfx == Sfx.Rolling || sfx == Sfx.Grinding ? 1 : 3;
            var made = new AudioClip[count];
            for (int i = 0; i < count; i++) made[i] = Make(sfx, 2000 + (int)sfx * 10 + i);
            Clips[sfx] = made;
            return made;
        }

        // ---- making the clips ----

        private static AudioClip Make(Sfx sfx, int seed)
        {
            var rng = new System.Random(seed);
            switch (sfx)
            {
                case Sfx.Pop: return Knock("skateboards_pop", rng, 0.14f, 260f, 1400f, 1f);
                case Sfx.Land: return Land("skateboards_land", rng);
                case Sfx.Flick: return Flick("skateboards_flick", rng);
                case Sfx.Bail: return Knock("skateboards_bail", rng, 0.3f, 90f, 500f, 1f);
                case Sfx.Clatter: return Clatter("skateboards_clatter", rng);
                case Sfx.Rolling: return Rolling("skateboards_rolling", rng);
                case Sfx.Grinding: return Grinding("skateboards_grinding", rng);
            }
            return null;
        }

        private static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        /// <summary>A band-pass that can sweep (Chamberlin's state-variable filter).</summary>
        private sealed class Filter
        {
            private float _low, _band;
            public float Q = 0.5f;
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

        /// <summary>Wood struck: a short falling tone (the deck's body) under a bright click of noise.</summary>
        private static AudioClip Knock(string name, System.Random rng, float seconds, float tone, float click, float gain)
        {
            int n = (int)(Rate * seconds);
            var data = new float[n];
            var filter = new Filter { Q = 0.25f };
            float phase = 0f, basis = tone * (0.92f + (float)rng.NextDouble() * 0.16f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                phase += 2f * Mathf.PI * basis * (1f - 0.35f * t) / Rate;
                float body = (Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 2.7f)) * Mathf.Exp(-t * 14f);
                float hit = filter.Band(Noise(rng), click) * Mathf.Exp(-t * 45f) * 2.5f;
                data[i] = (body + hit) * Mathf.Min(1f, i / (Rate * 0.0015f)) * gain;
            }
            return Clip(name, Normalize(data, 0.8f));
        }

        /// <summary>The wheels landing: two quick clacks (the trucks) and a low thump.</summary>
        private static AudioClip Land(string name, System.Random rng)
        {
            int n = (int)(Rate * 0.22f);
            var data = new float[n];
            var filter = new Filter { Q = 0.3f };
            int second = (int)(Rate * (0.018f + (float)rng.NextDouble() * 0.02f));
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float c1 = Mathf.Exp(-i / (Rate * 0.006f)), c2 = i >= second ? Mathf.Exp(-(i - second) / (Rate * 0.006f)) : 0f;
                float clack = filter.Band(Noise(rng), 2300f) * (c1 + c2 * 0.8f) * 2.4f;
                phase += 2f * Mathf.PI * 75f / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t * 16f) * 0.8f;
                data[i] = clack + thump;
            }
            return Clip(name, Normalize(data, 0.85f));
        }

        /// <summary>A flip: a quick airy whirr, the board turning past the feet.</summary>
        private static AudioClip Flick(string name, System.Random rng)
        {
            int n = (int)(Rate * 0.3f);
            var data = new float[n];
            var filter = new Filter { Q = 0.4f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float flutter = 0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 2f * 14f);
                float env = Mathf.Sin(Mathf.PI * t) * flutter;
                data[i] = filter.Band(Noise(rng), Mathf.Lerp(900f, 2400f, t)) * env;
            }
            return Clip(name, Normalize(data, 0.45f));
        }

        /// <summary>The board on its own, clattering to a stop: a few wooden knocks, quieter and closer together.</summary>
        private static AudioClip Clatter(string name, System.Random rng)
        {
            int n = (int)(Rate * 0.6f);
            var data = new float[n];
            var filter = new Filter { Q = 0.3f };
            float next = 0f, gap = 0.12f, level = 1f, phase = 0f, at = -1f;
            for (int i = 0; i < n; i++)
            {
                float s = i / (float)Rate;
                if (s >= next) { at = s; next = s + gap; gap *= 0.7f; level *= 0.7f; }
                float since = s - at;
                phase += 2f * Mathf.PI * 330f / Rate;
                float knock = (Mathf.Sin(phase) * 0.6f + filter.Band(Noise(rng), 1600f) * 2f) * Mathf.Exp(-since * 60f) * level;
                data[i] = knock;
            }
            return Clip(name, Normalize(data, 0.7f));
        }

        /// <summary>The wheels' roar: low rumbling noise with the clack of the seams going by, a loop.</summary>
        private static AudioClip Rolling(string name, System.Random rng)
        {
            int n = Rate * 2;
            var data = new float[n];
            float brown = 0f;
            var filter = new Filter { Q = 0.7f };
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + Noise(rng) * 0.04f, -1f, 1f) * 0.995f;
                float hum = filter.Band(Noise(rng), 420f) * 0.6f;
                float seam = (i % (Rate / 3)) < Rate * 0.004f ? Noise(rng) * 0.5f : 0f;
                data[i] = brown * 1.2f + hum + seam;
            }
            data = Loopable(data);
            return Clip(name, Normalize(data, 0.6f));
        }

        /// <summary>A grind: metal scraping, a hiss with a squealing ring that wavers, a loop.</summary>
        private static AudioClip Grinding(string name, System.Random rng)
        {
            int n = Rate * 2;
            var data = new float[n];
            var hiss = new Filter { Q = 0.5f };
            var ring = new Filter { Q = 0.04f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float wobble = 2800f + 260f * Mathf.Sin(t * 2f * Mathf.PI * 3f) + 120f * Mathf.Sin(t * 2f * Mathf.PI * 7.3f);
                data[i] = hiss.Band(Noise(rng), 4200f) * 0.8f + ring.Band(Noise(rng), wobble) * 0.35f;
            }
            data = Loopable(data);
            return Clip(name, Normalize(data, 0.5f));
        }

        /// <summary>The end faded into the start, so the loop has no click.</summary>
        private static float[] Loopable(float[] data)
        {
            int fade = Rate / 20;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                int j = data.Length - fade + i;
                data[i] = data[i] * w + data[j] * (1f - w);
            }
            System.Array.Resize(ref data, data.Length - fade);   // (the end now lives in the start)
            return data;
        }

        private static float[] Normalize(float[] data, float peak)
        {
            float max = 0f;
            foreach (float v in data) max = Mathf.Max(max, Mathf.Abs(v));
            if (max > 0f) for (int i = 0; i < data.Length; i++) data[i] *= peak / max;
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
