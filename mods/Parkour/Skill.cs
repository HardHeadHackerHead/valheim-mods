using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// The Parkour skill. Every move raises it; it makes wall runs longer, climbs higher and every move cheaper.
    ///
    /// The game's skills are an enum, so a mod's skill is a number of its own: a stable hash of our id (as blaxxun's SkillManager makes
    /// them), far from the game's 0-999 and from other mods' (Smooth Climbing's "Climbing" is 1879153461). For the game to keep it:
    ///   * a SkillDef (icon, description) in the player's Skills list, and handed out when the game looks ours up (GetSkillDef);
    ///   * Skills.IsSkillValid accepting the number (it asks Enum.IsDefined, so a saved level would be dropped on load);
    ///   * its name under the key the game makes from it, "$skill_" + the number ("skill_195522812" in translations).
    /// Without the mod the saved level is simply dropped the next time the character loads: no harm.
    /// </summary>
    internal static class Skill
    {
        public static readonly Skills.SkillType Type = MakeType();
        private static Skills.SkillDef _def;

        private static Skills.SkillType MakeType()
        {
            int hash = Plugin.Guid.GetStableHashCode() & 0x7FFFFFFF;   // (positive: the key "skill_<number>" must not have a minus sign)
            if (hash < 1000) hash += 1000;
            return (Skills.SkillType)hash;                              // 195522812
        }

        public static Skills.SkillDef Def => _def ?? (_def = new Skills.SkillDef
        {
            m_skill = Type,
            m_icon = LoadIcon(),
            m_description = "$parkour_skill_description",
            m_increseStep = 1f,
        });

        /// <summary>0 to 1: the player's Parkour level over 100.</summary>
        public static float Factor(Player p) => p != null ? p.GetSkillFactor(Type) : 0f;

        public static float Level(Player p) => p != null && p.GetSkills() != null ? p.GetSkills().GetSkillLevel(Type) : 0f;

        public static void Register(Player p)
        {
            if (p != null && p.GetSkills() != null) Register(p.GetSkills());
        }

        private static void Register(Skills skills)
        {
            Skills.SkillDef existing = skills.m_skills.Find(d => d.m_skill == Type);
            if (existing == null) skills.m_skills.Add(Def);
            else if (existing != Def) { existing.m_icon = Def.m_icon; existing.m_description = Def.m_description; }   // (an earlier copy of the mod's, after a reload)
        }

        public static void Reset() => _def = null;   // (the def stays on the player: the skill keeps showing until the game restarts)

        [HarmonyPatch(typeof(Skills), nameof(Skills.Awake))]
        private static class Skills_Awake { private static void Postfix(Skills __instance) => Register(__instance); }

        [HarmonyPatch(typeof(Skills), "IsSkillValid")]
        private static class Skills_IsSkillValid { private static void Postfix(Skills.SkillType type, ref bool __result) { if (type == Type) __result = true; } }

        // The game makes a skill's entry from its def: never let ours be made without one (a missing def breaks the skills window).
        [HarmonyPatch(typeof(Skills), "GetSkillDef")]
        private static class Skills_GetSkillDef { private static void Postfix(Skills.SkillType type, ref Skills.SkillDef __result) { if (__result == null && type == Type) __result = Def; } }

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
