using System.Globalization;
using System.IO;
using UnityEngine;

namespace Skateboards
{
    /// <summary>
    /// The Skateboarding skill. Every banked combo raises it (more for bigger combos); it makes pushing faster, ollies higher, spins
    /// quicker, flips shorter and grinds and manuals steadier.
    ///
    /// The skill itself is Shared/Skill.cs: a number of its own (a stable hash of our GUID, far from the game's 0-999), kept by
    /// Skills.Load, named "$skateboards_skill", found by raiseskill / resetskill, and backed up in the character's custom data
    /// ("com.quad.skateboards.skill"), so a load without the mod doesn't lose it for good. Never change the id: the number is its hash.
    /// </summary>
    internal static class Skill
    {
        public static readonly Skills.SkillType Type = DHack.Shared.Skill.TypeFor(Plugin.Guid);
        private static DHack.Shared.Skill _skill;

        /// <summary>0 to 1: the player's Skateboarding level over 100.</summary>
        public static float Factor(Player p) => p != null && p.GetSkills() != null ? p.GetSkillFactor(Type) : 0f;

        public static float Level(Player p) => p != null && p.GetSkills() != null ? p.GetSkills().GetSkillLevel(Type) : 0f;

        /// <summary>How fast it rises (the server's SkillGain setting), live.</summary>
        public static float Gain { set { if (_skill != null && _skill.Gain != value) _skill.Gain = value; } }

        public static void Create()
        {
            _skill?.Dispose();
            _skill = new DHack.Shared.Skill(Plugin.Guid, "Skateboarding", "$skateboards_skill", "$skateboards_skill_description", LoadIcon());
            const string key = "skateboards_skill_description";
            DHack.Shared.Translations.Fill(key, "spin0", Plugin.SpinSpeed, Number);
            DHack.Shared.Translations.Fill(key, "spin100", Plugin.SpinSpeedAt100, Number);
            DHack.Shared.Translations.Fill(key, "push100", Plugin.PushBonusAt100, Number);
        }

        private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        public static void Raise(Player p, float amount)
        {
            if (p == null || amount <= 0f || Plugin.SkillGain.Value <= 0f) return;
            p.RaiseSkill(Type, amount);   // (SkillGain is in the skill's step: Gain)
        }

        public static void Reset()
        {
            _skill?.Dispose();   // (the def stays on the player: the skill keeps showing until the game restarts)
            _skill = null;
        }

        // ---- the icon ----

        private static readonly System.Reflection.MethodInfo LoadImage =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        /// <summary>The artist's icon (skill-icon.png, built in when it's in the project), else a simple one drawn here.</summary>
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
            catch (System.Exception e) { Plugin.Log.LogWarning("Skateboarding skill icon: " + e.Message); tex = null; }
            if (tex == null) tex = DrawIcon();
            tex.name = "skateboards_skill_icon";
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A pale glyph like the game's own: a board seen from the side, kicked up at both ends, on two wheels.</summary>
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
            // the deck, tilted a little as in an ollie, its ends kicked up
            for (float t = 0f; t <= 1f; t += 0.005f)
            {
                float x = Mathf.Lerp(8f, 56f, t);
                float y = 30f + (t - 0.5f) * 10f;
                if (t < 0.12f) y += (0.12f - t) * 60f;
                if (t > 0.88f) y += (t - 0.88f) * 60f;
                Dot(x, y, 2.6f);
            }
            Dot(19f, 21f, 4.5f);   // the wheels
            Dot(45f, 26f, 4.5f);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
