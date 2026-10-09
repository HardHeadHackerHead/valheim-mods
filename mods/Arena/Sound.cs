using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Networking;

namespace Arena
{
    /// <summary>
    /// The arena's sound. Real crowd recordings ("Free Crowd Cheering Sounds" by Gregor Quendel, CC-BY 4.0, and "Crowd Shouting" by
    /// StarNinjas, CC0, both from OpenGameArt; carried in the mod) for the murmur of the stands, its cheers, roars and chants, with the game's
    /// own sounds on top: vikings yelling "yeah!" and laughing (Haldor's voice, many at once and each a little different), and the great bell
    /// that starts each round. Everything plays through the game's sound effects volume.
    /// </summary>
    internal static class Sound
    {
        private sealed class Voice { public AudioSource Src; public float Born, Fade, Until, Peak; }

        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly List<Voice> Voices = new List<Voice>();
        private static GameObject _root;
        private static AudioSource _ambience, _chant;
        private static AudioMixerGroup _mixer;
        private static AudioClip[] _yea, _laugh;
        private static AudioClip _bell;
        private static bool _loading;

        internal static bool Ready => Clips.Count > 0;

        // ---- loading ------------------------------------------------------------------------------------------------------

        internal static void Load()
        {
            if (_loading || Ready || Plugin.Instance == null) return;
            _loading = true;
            Plugin.Instance.StartCoroutine(LoadAll());
        }

        private static IEnumerator LoadAll()
        {
            string dir = Path.Combine(BepInEx.Paths.CachePath, "Arena");
            Directory.CreateDirectory(dir);
            foreach (string name in new[] { "crowd_ambience", "crowd_cheer", "crowd_cheer_short", "crowd_soft", "crowd_chant", "crowd_shout" })
            {
                string path = Path.Combine(dir, name + ".ogg");
                using (Stream s = typeof(Sound).Assembly.GetManifestResourceStream("Sounds." + name + ".ogg"))
                {
                    if (s == null) { Plugin.Log.LogWarning("The arena's sound " + name + " is missing from the mod"); continue; }
                    using (FileStream f = File.Create(path)) s.CopyTo(f);
                }
                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip("file:///" + path.Replace('\\', '/'), AudioType.OGGVORBIS))
                {
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success) { Plugin.Log.LogWarning($"Could not load {name}: {req.error}"); continue; }
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
                    clip.name = "Arena_" + name;
                    Clips[name] = clip;
                }
            }
            _loading = false;
            Plugin.Log.LogInfo($"Arena sounds loaded: {Clips.Count}");
        }

        /// <summary>The game's own sounds and its effects volume, found once the world's prefabs are there.</summary>
        private static void FindGameSounds()
        {
            if (_mixer != null || ZNetScene.instance == null) return;
            AudioClip[] Of(string prefab)
            {
                ZSFX z = ZNetScene.instance.GetPrefab(prefab)?.GetComponentInChildren<ZSFX>(true);
                if (z != null && _mixer == null) _mixer = z.GetComponent<AudioSource>()?.outputAudioMixerGroup;
                return z != null && z.m_audioClips != null ? z.m_audioClips.Where(c => c != null).ToArray() : new AudioClip[0];
            }
            _yea = Of("sfx_haldor_yea");
            _laugh = Of("sfx_haldor_laugh").Concat(Of("sfx_haldor_greet")).ToArray();
            _bell = Of("sfx_fader_bell").FirstOrDefault();
        }

        private static void EnsureRoot()
        {
            if (_root != null) return;
            FindGameSounds();
            _root = new GameObject("ArenaSound");
            Object.DontDestroyOnLoad(_root);
            _ambience = Loop("crowd_ambience");
            _chant = Loop("crowd_chant");
        }

        private static AudioSource Loop(string clip)
        {
            var src = _root.AddComponent<AudioSource>();
            src.loop = true; src.spatialBlend = 0f; src.volume = 0f; src.playOnAwake = false;
            src.outputAudioMixerGroup = _mixer;
            if (Clips.TryGetValue(clip, out AudioClip c)) { src.clip = c; src.time = Random.Range(0f, c.length * 0.8f); }
            return src;
        }

        internal static void Clear()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null; Voices.Clear(); _ambience = _chant = null;
            foreach (AudioClip c in Clips.Values) if (c != null) Object.Destroy(c);
            Clips.Clear();
        }

        // ---- every frame: the stands' murmur rises and falls with the crowd ----------------------------------------------------

        internal static void Tick(bool open, float excitement)
        {
            if (!Ready) { Load(); return; }
            if (!open && _root == null) return;
            EnsureRoot();
            float vol = Plugin.CrowdVolume.Value;
            Fade(_ambience, open ? vol * (0.25f + 0.5f * excitement) : 0f);
            Fade(_chant, open && excitement > 0.75f ? vol * 0.6f * (excitement - 0.75f) * 4f : 0f);
            for (int i = Voices.Count - 1; i >= 0; i--)
            {
                Voice v = Voices[i];
                if (v.Src == null) { Voices.RemoveAt(i); continue; }
                float now = Time.time;
                if (now < v.Born) continue;
                if (!v.Src.isPlaying && now - v.Born < 0.05f) v.Src.Play();
                float t = Mathf.Clamp01((now - v.Born) / 0.15f) * Mathf.Clamp01((v.Until - now) / v.Fade);
                v.Src.volume = v.Peak * t;
                if (now >= v.Until || (!v.Src.isPlaying && now - v.Born > 0.2f)) { Object.Destroy(v.Src); Voices.RemoveAt(i); }
            }
        }

        private static void Fade(AudioSource s, float want)
        {
            if (s == null || s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, want, Time.deltaTime * 0.35f);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        /// <summary>A sound that plays for a while and fades out: a stretch of a recording (from a random place in it), or a whole short one.</summary>
        private static void Play(AudioClip clip, float volume, float seconds, float fade, float delay = 0f, float pitch = 1f, float from = -1f)
        {
            if (clip == null) return;
            EnsureRoot();
            var src = _root.AddComponent<AudioSource>();
            src.clip = clip; src.loop = false; src.spatialBlend = 0f; src.pitch = pitch; src.volume = 0f; src.playOnAwake = false;
            src.outputAudioMixerGroup = _mixer;
            if (from < 0f) from = clip.length > seconds + 1f ? Random.Range(0f, clip.length - seconds - 0.5f) : 0f;
            src.time = Mathf.Clamp(from, 0f, Mathf.Max(0f, clip.length - 0.1f));
            float now = Time.time + delay;
            Voices.Add(new Voice { Src = src, Born = now, Fade = fade, Until = now + Mathf.Min(seconds, clip.length / Mathf.Max(0.1f, pitch)), Peak = volume * Plugin.CrowdVolume.Value });
        }

        private static AudioClip Clip(string name) => Clips.TryGetValue(name, out AudioClip c) ? c : null;
        private static AudioClip Any(AudioClip[] list) => list != null && list.Length > 0 ? list[Random.Range(0, list.Length)] : null;

        /// <summary>A few vikings in the stands yelling "yeah!", each a little higher or lower, a moment apart.</summary>
        private static void Yells(int count, float volume)
        {
            for (int i = 0; i < count; i++) Play(Any(_yea), volume * Random.Range(0.6f, 1f), 2f, 0.4f, Random.Range(0f, 0.6f), Random.Range(0.82f, 1.18f), 0f);
        }

        // ---- what the crowd does ---------------------------------------------------------------------------------------------

        internal static void Cheer() { Play(Clip("crowd_cheer_short"), 0.9f, 3.5f, 1.2f); Yells(3, 0.5f); }
        internal static void Roar() { Play(Clip("crowd_cheer"), 1f, 6f, 2f); Play(Clip("crowd_shout"), 0.55f, 5f, 2f); Yells(6, 0.6f); }
        internal static void Applause() => Play(Clip("crowd_soft"), 0.8f, 4.5f, 1.5f);
        internal static void Chant() => Play(Clip("crowd_chant"), 0.9f, 7f, 2f);

        internal static void Boo()
        {
            // a low, sullen grumble from the stands, and laughter at you
            Play(Clip("crowd_soft"), 0.7f, 3.5f, 1.2f, 0f, 0.72f);
            for (int i = 0; i < 3; i++) Play(Any(_laugh), 0.45f, 2f, 0.4f, Random.Range(0f, 0.8f), Random.Range(0.75f, 0.95f), 0f);
        }

        internal static void Gong() => Play(_bell, 0.55f, 5f, 2.5f, 0f, 1f, 0f);
    }
}
