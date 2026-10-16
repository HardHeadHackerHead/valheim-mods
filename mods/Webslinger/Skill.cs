using System.Globalization;
using System.IO;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Webslinger
{
    /// <summary>
    /// The Webslinging skill. Every web, zip and crawl raises it; it makes webs reach further, zips faster, swings stronger and every move
    /// cheaper. The skill itself is Shared/Skill.cs: a number of its own (a stable hash of our GUID), kept by Skills.Load, named
    /// "$webslinger_skill", found by raiseskill / resetskill, and backed up in the character's custom data ("com.quad.webslinger.skill").
    /// Never change the id: the number is its hash.
    /// </summary>
    internal static class Skill
    {
        public static readonly Skills.SkillType Type = DHack.Shared.Skill.TypeFor(Plugin.Guid);
        private static DHack.Shared.Skill _skill;

        /// <summary>0 to 1: the player's Webslinging level over 100.</summary>
        public static float Factor(Player p) => p != null && p.GetSkills() != null ? p.GetSkillFactor(Type) : 0f;

        public static float Level(Player p) => p != null && p.GetSkills() != null ? p.GetSkills().GetSkillLevel(Type) : 0f;

        /// <summary>How fast it rises (the server's SkillGain setting), live.</summary>
        public static float Gain { set { if (_skill != null && _skill.Gain != value) _skill.Gain = value; } }

        /// <summary>In Awake, after the translations and the settings (also on the player already in the world, after a hot reload).</summary>
        public static void Create()
        {
            _skill?.Dispose();
            _skill = new DHack.Shared.Skill(Plugin.Guid, "Webslinging", "$webslinger_skill", "$webslinger_skill_description", LoadIcon());
            const string key = "webslinger_skill_description";
            Fill(key, "reach0", Plugin.WebReach, Number);
            Fill(key, "reach100", Plugin.WebReachAt100, Number);
            Fill(key, "zip0", Plugin.ZipReach, Number);
            Fill(key, "zip100", Plugin.ZipReachAt100, Number);
            Fill(key, "cut", Plugin.SkillCostCut, Number);
        }

        private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        public static void Reset()
        {
            _skill?.Dispose();   // (the def stays on the player: the skill keeps showing until the game restarts)
            _skill = null;
        }

        // ---- the icon ----

        // Texture2D.LoadImage lives in a module the build can't reference cleanly, so it is called by name (as Parkour does).
        private static readonly System.Reflection.MethodInfo LoadImage =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        /// <summary>The artist's icon (skill-icon.png, built in when it's in the project), else a web drawn here.</summary>
        private static Sprite LoadIcon()
        {
            Texture2D tex = null;
            try
            {
                using (Stream s = typeof(Skill).Assembly.GetManifestResourceStream("skill-icon.png"))
                    if (s != null)
                    {
                        var bytes = new byte[s.Length];
                        s.Read(bytes, 0, bytes.Length);
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (LoadImage == null || !(bool)LoadImage.Invoke(null, new object[] { tex, bytes })) tex = null;
                    }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("Webslinging skill icon: " + e.Message); tex = null; }
            if (tex == null) tex = DrawIcon();
            tex.name = "webslinger_skill_icon";
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A pale glyph like the game's own: a spider web, eight threads from the middle and four rings sagging between them.</summary>
        private static Texture2D DrawIcon()
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            var ink = new Color32(236, 222, 190, 255);
            void Dot(float x, float y, float r)
            {
                for (int iy = Mathf.Max(0, (int)(y - r - 1)); iy <= Mathf.Min(N - 1, (int)(y + r + 1)); iy++)
                    for (int ix = Mathf.Max(0, (int)(x - r - 1)); ix <= Mathf.Min(N - 1, (int)(x + r + 1)); ix++)
                    {
                        float d = Mathf.Sqrt((ix - x) * (ix - x) + (iy - y) * (iy - y));
                        byte a = (byte)(Mathf.Clamp01(r + 0.5f - d) * 255f);
                        if (a > px[iy * N + ix].a) px[iy * N + ix] = new Color32(ink.r, ink.g, ink.b, a);
                    }
            }
            const float cx = 32f, cy = 32f, outer = 29f;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f + Mathf.PI / 8f;
                for (float t = 0f; t <= 1f; t += 0.01f) Dot(cx + Mathf.Cos(a) * outer * t, cy + Mathf.Sin(a) * outer * t, 1.3f);
            }
            foreach (float ring in new[] { 0.3f, 0.52f, 0.74f, 0.96f })
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * Mathf.PI / 4f + Mathf.PI / 8f, a1 = a0 + Mathf.PI / 4f;
                    for (float t = 0f; t <= 1f; t += 0.02f)
                    {
                        // a thread between two spokes, sagging toward the middle
                        float a = Mathf.Lerp(a0, a1, t), r = outer * ring * (1f - 0.12f * Mathf.Sin(t * Mathf.PI));
                        Dot(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r, 1.1f);
                    }
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
