using System.Globalization;
using System.IO;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Parkour
{
    /// <summary>
    /// The Parkour skill. Every move raises it; it makes wall runs longer, climbs higher and every move cheaper.
    ///
    /// The skill itself is Shared/Skill.cs: a number of its own (a stable hash of our GUID, far from the game's 0-999 and from other mods':
    /// Smooth Climbing's "Climbing" is 1879153461), kept by Skills.Load, named "$parkour_skill" where the game shows "$skill_195522812",
    /// found by raiseskill / resetskill, and backed up in the character's custom data ("com.quad.parkour.skill"). Without the backup a single
    /// load without the mod (a second mod-manager profile, the mod switched off) dropped the level for good at the next save; now it comes
    /// back the next time the character loads with the mod. Never change the id: the number is its hash (195522812).
    /// </summary>
    internal static class Skill
    {
        public static readonly Skills.SkillType Type = DHack.Shared.Skill.TypeFor(Plugin.Guid);   // 195522812, as in 0.1.0
        private static DHack.Shared.Skill _skill;

        /// <summary>0 to 1: the player's Parkour level over 100.</summary>
        public static float Factor(Player p) => p != null && p.GetSkills() != null ? p.GetSkillFactor(Type) : 0f;

        public static float Level(Player p) => p != null && p.GetSkills() != null ? p.GetSkills().GetSkillLevel(Type) : 0f;

        /// <summary>How fast it rises (the server's SkillGain setting), live.</summary>
        public static float Gain { set { if (_skill != null && _skill.Gain != value) _skill.Gain = value; } }

        /// <summary>In Awake, after the translations and the settings (also on the player already in the world, after a hot reload).</summary>
        public static void Create()
        {
            _skill?.Dispose();
            _skill = new DHack.Shared.Skill(Plugin.Guid, "Parkour", "$parkour_skill", "$parkour_skill_description", LoadIcon());
            Describe();
        }

        /// <summary>
        /// The description (the skills window's tooltip) says what skill 0 and 100 give in numbers, from the strengths' settings: the
        /// server's on a server, and again whenever one changes or the language does (Translations.Fill; Unload lets go of them).
        /// </summary>
        private static void Describe()
        {
            const string key = "parkour_skill_description";
            Fill(key, "run0", Plugin.WallRunMin, Number);
            Fill(key, "run100", Plugin.WallRunMax, Number);
            Fill(key, "kicks0", () => Moves.KicksAt(0f).ToString(CultureInfo.InvariantCulture), Plugin.KicksAtSkill0, Plugin.KickEveryLevels, Plugin.KicksMax);
            Fill(key, "kicks100", () => Moves.KicksAt(100f).ToString(CultureInfo.InvariantCulture), Plugin.KicksAtSkill0, Plugin.KickEveryLevels, Plugin.KicksMax);
            Fill(key, "climb0", Plugin.ClimbMin, Number);
            Fill(key, "climb100", Plugin.ClimbMax, Number);
            Fill(key, "roll0", Plugin.RollSafeMin, Number);
            Fill(key, "roll100", Plugin.RollSafeMax, Number);
        }

        private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

        public static void Reset()
        {
            _skill?.Dispose();   // (the def stays on the player: the skill keeps showing until the game restarts)
            _skill = null;
        }

        // ---- the icon ----

        // Texture2D.LoadImage lives in a module the build can't reference cleanly, so it is called by name (as BirdTrap does).
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
            catch (System.Exception e) { Plugin.Log.LogWarning("Parkour skill icon: " + e.Message); tex = null; }
            if (tex == null) tex = DrawIcon();
            tex.name = "parkour_skill_icon";
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A pale glyph like the game's own: a wall on the right and an arc leaping off it, with a head at its top.</summary>
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
            // the wall: a tall block of stones on the right
            for (int y = 6; y < 58; y++)
                for (int x = 48; x < 58; x++)
                    if ((y % 10 != 0) && !((y / 10) % 2 == 0 ? x == 53 : x == 50)) px[y * N + x] = ink;
            // the leap: an arc from low on the wall up and away to the left, thick and tapering
            for (float t = 0f; t <= 1f; t += 0.01f)
            {
                float x = Mathf.Lerp(45f, 15f, t), y = 12f + 32f * Mathf.Sin(t * Mathf.PI / 2f);
                Dot(x, y, Mathf.Lerp(4.5f, 2.5f, t));
            }
            Dot(13f, 52f, 6f);   // the head, where the arc ends
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
