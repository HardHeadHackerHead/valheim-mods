using UnityEngine;

namespace Guns
{
    /// <summary>
    /// The Machine Gun's shots, made in code (no sound files to ship, license or load): a sharp crack, a body of low-passed noise, a short
    /// falling thump and a rumbling tail, soft-clipped. A few variations with different noise, so ten shots a second don't sound like one
    /// sample repeated.
    /// </summary>
    internal static class Sound
    {
        private const int Rate = 44100;

        internal static AudioClip[] Shots(int count = 4)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Shot(1337 + i * 7919, 0.42f, i);
            return clips;
        }

        private static AudioClip Shot(int seed, float length, int variation)
        {
            int n = (int)(Rate * length);
            var data = new float[n];
            var rng = new System.Random(seed);
            float lp1 = 0f, lp2 = 0f, lp3 = 0f, phase = 0f, peak = 0.0001f;
            float crackTime = 0.005f + 0.0015f * variation, thumpFreq = 150f + 12f * variation;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp1 += (noise - lp1) * 0.30f;
                float crack = (noise - lp1) * Mathf.Exp(-t / crackTime);                          // the sharp edge: high-passed noise, gone in a few ms
                lp2 += (noise - lp2) * 0.07f;
                float body = lp2 * 3.2f * Mathf.Exp(-t / 0.045f);                                  // the blast: low-passed noise
                phase += 2f * Mathf.PI * (52f + thumpFreq * Mathf.Exp(-t / 0.028f)) / Rate;
                float thump = Mathf.Sin(phase) * Mathf.Exp(-t / 0.075f);                           // the punch: a sine falling from ~200 Hz to ~50
                lp3 += (noise - lp3) * 0.018f;
                float tail = lp3 * 6f * Mathf.Exp(-t / 0.16f);                                     // the rumble that rolls off over the land
                float s = crack * 0.85f + body * 0.8f + thump * 0.95f + tail * 0.45f;
                s = (float)System.Math.Tanh(s * 1.6f);                                             // soft clip: loud without harsh edges
                data[i] = s;
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }
            int fade = Rate / 200;   // 5 ms out, so the end never clicks
            for (int i = 0; i < n; i++)
            {
                data[i] *= 0.9f / peak;
                if (i > n - fade) data[i] *= (n - i) / (float)fade;
            }
            int at = 0;   // (the clip is filled through a read callback: SetData's span overloads need a library this build doesn't reference)
            return AudioClip.Create("dh_machinegun_shot_" + variation, n, 1, Rate, false, buffer => { for (int i = 0; i < buffer.Length; i++) buffer[i] = at < n ? data[at++] : 0f; });
        }
    }
}
