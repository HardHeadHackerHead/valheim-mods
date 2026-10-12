using UnityEngine;

namespace Guns
{
    /// <summary>
    /// The guns' sounds, made in code (no sound files to ship, license or load).
    /// A shot is layered: the firing pin's tick, the supersonic crack (bright noise gone in a few milliseconds), the muzzle blast (noise whose
    /// brightness falls as it dies), the boom (a falling sine with its octave) and a rumbling tail, soft-clipped, then sent through a bank of
    /// damped comb filters that gives the echo off the hills; each gun has its own mix (GunDef.Sound). A few variations with different noise,
    /// so a burst doesn't sound like one sample repeated.
    /// The mechanism's sounds are built from four kinds of impact: a tick (noise and a ringing tone), a thud (a falling sine), a metallic ring
    /// (inharmonic partials) and a scrape (band-passed noise), laid one after another into each click (magazine out, slide racked, pump, bolt...).
    /// </summary>
    internal static class Sound
    {
        private const int Rate = 32000;

        // ---- real recordings ------------------------------------------------------------------------------------------

        /// <summary>A real recording built into the DLL (sounds/*.wav: mono, 16-bit PCM), or null when there is none by that name.</summary>
        internal static AudioClip Recording(string file)
        {
            try
            {
                using (System.IO.Stream s = typeof(Sound).Assembly.GetManifestResourceStream("sounds/" + file))
                {
                    if (s == null) return null;
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    // the RIFF chunks: "fmt " (rate, channels, bits) and "data"
                    int pos = 12, rate = 44100, channels = 1, bits = 16, dataAt = -1, dataLen = 0;
                    while (pos + 8 <= bytes.Length)
                    {
                        string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                        int size = System.BitConverter.ToInt32(bytes, pos + 4);
                        if (id == "fmt ") { channels = System.BitConverter.ToInt16(bytes, pos + 10); rate = System.BitConverter.ToInt32(bytes, pos + 12); bits = System.BitConverter.ToInt16(bytes, pos + 22); }
                        else if (id == "data") { dataAt = pos + 8; dataLen = Mathf.Min(size, bytes.Length - dataAt); break; }
                        pos += 8 + size + (size & 1);
                    }
                    if (dataAt < 0 || bits != 16 || channels != 1) { Plugin.Log.LogWarning("the recording " + file + " is not mono 16-bit"); return null; }
                    int count = dataLen / 2;
                    var data = new float[count];
                    for (int i = 0; i < count; i++) data[i] = System.BitConverter.ToInt16(bytes, dataAt + i * 2) / 32768f;
                    int at = 0;
                    return AudioClip.Create(file, count, 1, rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = at < count ? data[at++] : 0f; });
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("could not load the recording " + file + ": " + e.Message); return null; }
        }

        internal static AudioClip[] Shots(GunDef d, int count = 3)
        {
            // real recordings first (shot_<gun>_<n>.wav); the synthesised shot is the fallback for a gun without any
            var real = new System.Collections.Generic.List<AudioClip>();
            for (int i = 0; i < 6; i++) { AudioClip c = Recording("shot_" + d.Id + "_" + i + ".wav"); if (c == null) break; real.Add(c); }
            if (real.Count > 0) return real.ToArray();
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Shot(d.Id.GetHashCode() + 1337 + i * 7919, d.Sound, i, d.Id);
            return clips;
        }

        private static float OnePole(float hz) => 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);

        private static AudioClip Shot(int seed, SoundSpec s, int variation, string name)
        {
            int n = (int)(Rate * s.Length);
            var dry = new float[n];
            var rng = new System.Random(seed);
            float hp = 0f, blast = 0f, rumble = 0f, phase = 0f;
            float crackTime = s.CrackT * (1f + 0.15f * (variation - 1)), boomHz = s.ThumpHz * (1f + 0.04f * (variation - 1));
            float kHp = OnePole(2800f), kRumble = OnePole(260f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float tick = noise * Mathf.Exp(-t / 0.0007f) * 0.7f;                                           // the firing pin and the action: a single broadband tick
                hp += (noise - hp) * kHp;
                float crack = (noise - hp) * Mathf.Exp(-t / crackTime) * s.Crack * 1.1f;                       // the supersonic crack: gone in a few ms
                float fc = Mathf.Lerp(5000f, 650f, 1f - Mathf.Exp(-t / 0.035f));                              // the blast darkens as it dies
                blast += (noise - blast) * OnePole(fc);
                float body = blast * 3.4f * Mathf.Exp(-t / s.BodyT) * s.Body;
                float f = 36f + boomHz * 0.55f * Mathf.Exp(-t / 0.03f);                                        // the boom: falls to a deep note
                phase += 2f * Mathf.PI * f / Rate;
                float boom = (Mathf.Sin(phase) + 0.35f * Mathf.Sin(phase * 2f)) * Mathf.Exp(-t / s.ThumpT) * s.Thump;
                rumble += (noise - rumble) * kRumble;
                float tail = rumble * 7f * Mathf.Exp(-t / s.TailT) * s.Tail;                                   // the low roll that follows
                dry[i] = (float)System.Math.Tanh((tick + crack + body * 0.8f + boom * 0.95f + tail * 0.5f) * s.Drive);
            }
            AddEcho(dry, s);
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) { dry[i] = (float)System.Math.Tanh(dry[i] * 1.15f); peak = Mathf.Max(peak, Mathf.Abs(dry[i])); }
            Normalise(dry, 0.9f / peak);
            return Make("dh_shot_" + name + "_" + variation, dry, false);
        }

        /// <summary>The place the shot is fired in: four damped comb filters (prime-ish delays) give the echo off hills and trees, longer and louder for the guns with a long tail.</summary>
        private static void AddEcho(float[] x, SoundSpec s)
        {
            int[] delays = { 29, 41, 53, 71 };   // ms
            float feedback = Mathf.Clamp(0.45f + s.TailT * 0.5f, 0.5f, 0.8f), wet = 0.20f + 0.18f * Mathf.Clamp01(s.Tail);
            var dry = (float[])x.Clone();
            var echo = new float[x.Length];
            foreach (int ms in delays)
            {
                int len = ms * Rate / 1000;
                var buf = new float[len];
                float damp = 0f;
                for (int i = 0, at = 0; i < x.Length; i++, at = (at + 1) % len)
                {
                    float y = buf[at];
                    damp += (y - damp) * 0.42f;   // (each trip round the loop loses its brightness)
                    buf[at] = dry[i] + damp * feedback;
                    echo[i] += y * 0.25f;
                }
            }
            for (int i = 0; i < x.Length; i++) x[i] = dry[i] + echo[i] * wet * 2.2f;
        }

        private static void Normalise(float[] data, float gain)
        {
            int n = data.Length, fade = Rate / 100;   // 10 ms out, so the end never clicks
            for (int i = 0; i < n; i++)
            {
                data[i] *= gain;
                if (i > n - fade) data[i] *= (n - i) / (float)fade;
            }
        }

        private static AudioClip Make(string name, float[] data, bool loop)
        {
            int n = data.Length, at = 0;   // (the clip is filled through a read callback: SetData's span overloads need a library this build doesn't reference)
            return AudioClip.Create(name, n, 1, Rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = at < n ? data[at++] : 0f; });
        }

        // ---- the mechanism --------------------------------------------------------------------------------------------

        /// <summary>A click of a gun's mechanism.</summary>
        internal enum Click
        {
            Dry,        // the trigger pulled on an empty gun: a hammer on nothing
            MagOut,     // the release, the magazine sliding out and dropping
            MagIn,      // the magazine sliding in, seating, latching
            Rack,       // a slide or charging handle pulled back and let go
            Shell,      // one round going in
            Pump,       // a shotgun's pump: back, forward
            Lever,      // a lever worked: down, up
            Bolt,       // a bolt lifted, drawn back, pushed forward, turned down
            CylOpen,    // a revolver's cylinder swung out
            CylDump,    // the ejector rod: brass rattling out
            CylLoad,    // rounds dropping in from a speedloader
            CylClose,   // the cylinder closing and ratcheting
            Belt,       // a belt of rounds rattling in its box
            Casing,     // brass bouncing on the ground
            Draw,       // taking the gun up
            Dive,       // a body flung through the air: cloth and a rush of air
            Land,       // landing: a thud and a slide
            Spool,      // the minigun motor engaging: a clunk and a rising whine
        }

        private static float _pm = 1f;   // the pitch of the click being made

        private static float Length(Click kind)
        {
            switch (kind)
            {
                case Click.Pump: return 0.40f;
                case Click.Lever: return 0.46f;
                case Click.Bolt: return 0.52f;
                case Click.Belt: return 0.52f;
                case Click.Rack: return 0.34f;
                case Click.MagIn: return 0.26f;
                case Click.MagOut: return 0.26f;
                case Click.CylDump: return 0.32f;
                case Click.CylLoad: return 0.30f;
                case Click.CylClose: return 0.32f;
                case Click.CylOpen: return 0.22f;
                case Click.Casing: return 0.34f;
                case Click.Draw: return 0.18f;
                case Click.Spool: return 0.55f;
                case Click.Dive: return 0.45f;
                case Click.Land: return 0.30f;
                default: return 0.14f;
            }
        }

        /// <summary>One click, laid out as its impacts in time (seconds from the start).</summary>
        internal static AudioClip Make(Click kind, int variant = 0)
        {
            AudioClip real = Recording("click_" + kind + "_" + variant + ".wav");   // a real recording where there is one
            if (real != null) return real;
            int n = (int)(Rate * Length(kind));
            var d = new float[n];
            var rng = new System.Random(77 + (int)kind * 31 + variant * 1009);
            _pm = 1f + 0.11f * (variant - 1);   // (each variant a little higher or lower in pitch, so brass doesn't ring the same note every time)
            switch (kind)
            {
                case Click.Dry: Tick(d, rng, 0f, 2400f, 0.9f, 0.0025f); Thud(d, 0f, 320f, 0.45f, 0.012f); Ring(d, 0.002f, 3800f, 0.16f, 0.02f); break;
                case Click.MagOut:
                    Tick(d, rng, 0f, 2800f, 0.6f, 0.002f); Scrape(d, rng, 0.03f, 0.09f, 0.28f, 0.45f);
                    Tick(d, rng, 0.13f, 1200f, 0.45f, 0.004f); Thud(d, 0.13f, 170f, 0.5f, 0.03f); break;
                case Click.MagIn:
                    Scrape(d, rng, 0f, 0.07f, 0.22f, 0.45f); Thud(d, 0.075f, 140f, 0.9f, 0.03f); Tick(d, rng, 0.075f, 1800f, 0.8f, 0.003f);
                    Ring(d, 0.078f, 3200f, 0.25f, 0.04f); Tick(d, rng, 0.12f, 2600f, 0.5f, 0.002f); break;
                case Click.Rack:
                    Scrape(d, rng, 0f, 0.09f, 0.38f, 0.5f); Tick(d, rng, 0.09f, 1500f, 0.8f, 0.004f); Thud(d, 0.09f, 150f, 0.5f, 0.02f);
                    Scrape(d, rng, 0.15f, 0.06f, 0.26f, 0.5f); Thud(d, 0.21f, 120f, 0.95f, 0.04f); Tick(d, rng, 0.21f, 1900f, 0.9f, 0.004f); Ring(d, 0.21f, 3500f, 0.22f, 0.05f); break;
                case Click.Shell:   // a round pushed into the magazine or tube: a dull click of brass on metal and the spring giving
                    Scrape(d, rng, 0f, 0.03f, 0.18f, 0.3f); Tick(d, rng, 0.025f, 1200f, 0.8f, 0.004f); Thud(d, 0.025f, 190f, 0.75f, 0.02f); Ping(d, 0.027f, 2700f, 0.12f, 0.03f); break;
                case Click.Pump:
                    Scrape(d, rng, 0f, 0.10f, 0.5f, 0.4f); Tick(d, rng, 0.10f, 900f, 1f, 0.006f); Thud(d, 0.10f, 100f, 1f, 0.05f);
                    Scrape(d, rng, 0.19f, 0.08f, 0.45f, 0.4f); Tick(d, rng, 0.27f, 1100f, 1f, 0.006f); Thud(d, 0.27f, 90f, 1f, 0.06f); Ring(d, 0.27f, 2800f, 0.3f, 0.08f); break;
                case Click.Lever:
                    Tick(d, rng, 0f, 1500f, 0.7f, 0.004f); Scrape(d, rng, 0.01f, 0.12f, 0.32f, 0.6f);
                    Tick(d, rng, 0.14f, 1100f, 0.9f, 0.005f); Thud(d, 0.14f, 110f, 0.9f, 0.04f);
                    Scrape(d, rng, 0.23f, 0.10f, 0.3f, 0.6f); Tick(d, rng, 0.34f, 1700f, 1f, 0.005f); Thud(d, 0.34f, 100f, 0.9f, 0.04f); Ring(d, 0.34f, 3300f, 0.25f, 0.06f); break;
                case Click.Bolt:
                    Tick(d, rng, 0f, 1300f, 0.7f, 0.004f); Scrape(d, rng, 0.03f, 0.07f, 0.32f, 0.5f);
                    Tick(d, rng, 0.11f, 1000f, 0.9f, 0.005f); Thud(d, 0.11f, 120f, 0.8f, 0.03f);
                    Scrape(d, rng, 0.30f, 0.08f, 0.32f, 0.5f); Tick(d, rng, 0.38f, 1600f, 1f, 0.005f); Thud(d, 0.38f, 100f, 1f, 0.05f); Tick(d, rng, 0.43f, 1200f, 0.7f, 0.004f); break;
                case Click.CylOpen: Tick(d, rng, 0f, 2200f, 0.7f, 0.003f); Scrape(d, rng, 0.02f, 0.06f, 0.2f, 0.5f); Tick(d, rng, 0.09f, 1400f, 0.9f, 0.005f); Thud(d, 0.09f, 130f, 0.8f, 0.03f); break;
                case Click.CylDump:
                    Tick(d, rng, 0f, 1800f, 0.8f, 0.004f); Thud(d, 0.04f, 150f, 0.5f, 0.02f);
                    Ring(d, 0.03f, 4800f, 0.4f, 0.08f); Ring(d, 0.07f, 5400f, 0.35f, 0.06f); Ring(d, 0.11f, 4400f, 0.35f, 0.07f); Ring(d, 0.16f, 5000f, 0.25f, 0.06f); break;
                case Click.CylLoad:
                    for (int i = 0; i < 3; i++) { Tick(d, rng, i * 0.08f, 2000f, 0.6f, 0.003f); Ring(d, i * 0.08f, 3600f + i * 250f, 0.2f, 0.04f); Thud(d, i * 0.08f, 220f, 0.3f, 0.012f); }
                    break;
                case Click.CylClose:
                    Scrape(d, rng, 0f, 0.04f, 0.2f, 0.5f); Tick(d, rng, 0.05f, 1700f, 1f, 0.004f); Thud(d, 0.05f, 120f, 0.9f, 0.04f); Ring(d, 0.05f, 3000f, 0.2f, 0.05f);
                    for (int i = 0; i < 4; i++) Tick(d, rng, 0.13f + i * 0.035f, 2600f, 0.35f, 0.002f);
                    break;
                case Click.Belt:
                    Thud(d, 0f, 90f, 0.6f, 0.06f); Scrape(d, rng, 0.03f, 0.28f, 0.22f, 0.8f);
                    for (int i = 0; i < 7; i++) Ring(d, 0.02f + (float)rng.NextDouble() * 0.38f, 3000f + (float)rng.NextDouble() * 2400f, 0.25f, 0.03f);
                    break;
                case Click.Casing:   // brass dropping and bouncing: each bounce a dull tick and a short bright ping, closer together and quieter
                {
                    float[] at = { 0f, 0.075f, 0.13f, 0.17f, 0.20f, 0.22f }, amp = { 1f, 0.6f, 0.38f, 0.25f, 0.16f, 0.1f };
                    for (int i = 0; i < at.Length; i++)
                    {
                        float jitter = 1f + ((float)rng.NextDouble() - 0.5f) * 0.12f;
                        Tick(d, rng, at[i], 1700f * jitter, 0.45f * amp[i], 0.002f);
                        Ping(d, at[i], 2900f * jitter * (1f - i * 0.02f), 0.7f * amp[i], 0.05f);
                    }
                    break;
                }
                case Click.Dive:   // a rush of air that swells and fades, with the rustle of cloth
                    Scrape(d, rng, 0f, 0.40f, 0.5f, 0.2f); Scrape(d, rng, 0.05f, 0.25f, 0.25f, 0.9f); break;
                case Click.Land: Thud(d, 0f, 80f, 1f, 0.07f); Scrape(d, rng, 0f, 0.22f, 0.4f, 0.7f); Tick(d, rng, 0f, 600f, 0.4f, 0.01f); break;
                case Click.Spool:
                {
                    Thud(d, 0f, 110f, 0.9f, 0.05f); Tick(d, rng, 0f, 900f, 0.6f, 0.006f);   // the clutch
                    float phase = 0f;
                    for (int i = (int)(0.03f * Rate); i < d.Length; i++)                       // the servo chirp, climbing
                    {
                        float t = (i / (float)Rate) - 0.03f;
                        float f = 140f + 900f * t * t / 0.27f;
                        phase += 2f * Mathf.PI * f / Rate;
                        d[i] += (Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 2f)) * Mathf.Sin(Mathf.Clamp01(t / 0.52f) * Mathf.PI) * 0.35f;
                    }
                    break;
                }
                case Click.Draw: Scrape(d, rng, 0f, 0.06f, 0.2f, 0.3f); Tick(d, rng, 0.07f, 1500f, 0.6f, 0.004f); Thud(d, 0.07f, 140f, 0.6f, 0.03f); break;
            }
            float peak = 0.0001f;
            for (int i = 0; i < n; i++) { d[i] = (float)System.Math.Tanh(d[i] * 1.4f); peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
            Normalise(d, 0.8f / peak);
            return Make("dh_click_" + kind, d, false);
        }

        /// <summary>A tick: a burst of noise and a short ringing tone, dying fast.</summary>
        private static void Tick(float[] d, System.Random rng, float at, float freq, float amp, float decay)
        {
            int start = (int)(at * Rate);
            float lp = 0f;
            freq *= _pm;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                float env = Mathf.Exp(-t / decay);
                if (env < 0.003f) break;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.5f;
                d[i] += ((noise - lp) * 0.7f + Mathf.Sin(2f * Mathf.PI * freq * t) * 0.3f) * env * amp;
            }
        }

        /// <summary>A thud: a sine that falls in pitch, the weight of a part landing.</summary>
        private static void Thud(float[] d, float at, float freq, float amp, float decay)
        {
            int start = (int)(at * Rate);
            float phase = 0f;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                float env = Mathf.Exp(-t / decay);
                if (env < 0.003f) break;
                phase += 2f * Mathf.PI * (freq * 0.55f + freq * 0.45f * Mathf.Exp(-t / 0.012f)) / Rate;
                d[i] += Mathf.Sin(phase) * env * amp;
            }
        }

        /// <summary>A ring: the inharmonic partials of a struck piece of metal (brass, a latch).</summary>
        private static void Ring(float[] d, float at, float freq, float amp, float decay)
        {
            int start = (int)(at * Rate);
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                float env = Mathf.Exp(-t / decay);
                if (env < 0.003f) break;
                float v = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * freq * 2.76f * t) * Mathf.Exp(-t / (decay * 0.5f)) + 0.35f * Mathf.Sin(2f * Mathf.PI * freq * 5.4f * t) * Mathf.Exp(-t / (decay * 0.3f));
                d[i] += v * env * amp * 0.5f;
            }
        }

        /// <summary>A ping: a small piece of brass tapped, a clear note with two softer overtones that die faster (a hollow tube's, not a bell's).</summary>
        private static void Ping(float[] d, float at, float freq, float amp, float decay)
        {
            int start = (int)(at * Rate);
            freq *= _pm;
            for (int i = start; i < d.Length; i++)
            {
                float t = (i - start) / (float)Rate;
                float env = Mathf.Exp(-t / decay);
                if (env < 0.003f) break;
                float v = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.45f * Mathf.Sin(2f * Mathf.PI * freq * 2.32f * t) * Mathf.Exp(-t / (decay * 0.6f)) + 0.2f * Mathf.Sin(2f * Mathf.PI * freq * 4.18f * t) * Mathf.Exp(-t / (decay * 0.35f));
                d[i] += v * env * amp;
            }
        }

        /// <summary>A scrape: band-passed noise swelling and fading, metal sliding on metal. grit: how much of it is rough.</summary>
        private static void Scrape(float[] d, System.Random rng, float at, float length, float amp, float grit)
        {
            int start = (int)(at * Rate), count = (int)(length * Rate);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < count && start + i < d.Length; i++)
            {
                float t = i / (float)count;
                float env = Mathf.Sin(t * Mathf.PI);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.55f;      // (take off the highest hiss)
                lp2 += (lp - lp2) * 0.08f;       // (and the lowest: what is left is the middle)
                float rough = 1f - grit * 0.5f * (1f + Mathf.Sin(i * 0.9f));   // (the stick and slip of rubbing metal)
                d[start + i] += (lp - lp2) * env * amp * 2.2f * rough;
            }
        }

        // ---- the minigun's motor ------------------------------------------------------------------------------------

        /// <summary>
        /// The minigun's three loops, played together with their own pitch and loudness as the barrels speed up (Fx.Whirr): the motor hum (a low
        /// buzzy tone with the six barrels going by), the gear whine (a higher tone with a little wobble that climbs much further as it spins) and
        /// the air the barrels push (noise that only builds near full speed). Each is whole cycles, so it joins up.
        /// </summary>
        internal static AudioClip[] SpinLoops()
        {
            const int n = Rate;   // one second: every frequency below is a whole number of cycles in it
            var hum = new float[n];
            var whine = new float[n];
            var air = new float[n + Rate / 10];
            var rng = new System.Random(4242);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, v = 0f;
                for (int k = 1; k <= 12; k++) v += Mathf.Sin(2f * Mathf.PI * 55f * k * t) / Mathf.Pow(k, 0.8f);
                hum[i] = v * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 11f * t)) * 0.2f;   // (the barrels: 11 passes a second at this pitch)
                float vib = Mathf.Sin(2f * Mathf.PI * 5f * t) * 0.8f;
                whine[i] = (Mathf.Sin(2f * Mathf.PI * 520f * t + vib) + 0.45f * Mathf.Sin(2f * Mathf.PI * 1040f * t + vib * 2f) + 0.2f * Mathf.Sin(2f * Mathf.PI * 1560f * t)) * 0.18f;
            }
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < air.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.35f;
                lp2 += (lp - lp2) * 0.04f;
                air[i] = (lp - lp2) * 1.6f;
            }
            var airLoop = new float[n];   // (the extra tenth of a second is faded over the start, so the loop has no seam)
            int over = Rate / 10;
            for (int i = 0; i < n; i++)
            {
                airLoop[i] = air[i];
                if (i < over) { float f = i / (float)over; airLoop[i] = air[i] * f + air[n + i] * (1f - f); }
            }
            return new[] { Loop("dh_minigun_hum", hum), Loop("dh_minigun_whine", whine), Loop("dh_minigun_air", airLoop) };
        }

        private static AudioClip Loop(string name, float[] d)
        {
            int n = d.Length, at = 0;
            return AudioClip.Create(name, n, 1, Rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = d[at++ % n]; });
        }
    }
}
