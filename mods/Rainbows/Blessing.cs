using UnityEngine;
using DHack.Shared;
using static DHack.Shared.Translations;

namespace Rainbows
{
    /// <summary>The buff for looking up at a rainbow, and its registration with the game's status effects (Shared/StatusEffects.cs).</summary>
    internal static class Blessing
    {
        public const string EffectName = "Rainbows_SE_Blessing", DoubleName = "Rainbows_SE_DoubleBlessing";   // (named after the mod: one space with every other mod's)
        public static readonly int Hash = EffectName.GetStableHashCode(), DoubleHash = DoubleName.GetStableHashCode();
        private static SE_Rainbow _effect, _double;
        private static Sprite _icon, _doubleIcon;

        public static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items.Count == 0) return;   // (the game's own lists are not loaded yet: asked again)
            if (_effect == null)
            {
                _effect = StatusEffects.Create<SE_Rainbow>(EffectName, "$rainbows_blessing", _icon = MakeIcon(false));
                _double = StatusEffects.Create<SE_DoubleRainbow>(DoubleName, "$rainbows_double_blessing", _doubleIcon = MakeIcon(true));
            }
            StatusEffects.Register();   // (in ObjectDB again, and after a reload back on whoever had one, with the time they had left)
        }

        public static void Unregister()
        {
            StatusEffects.Unload();   // off every character this game has (a copy left on one would point at what is destroyed), out of ObjectDB
            foreach (Sprite icon in new[] { _icon, _doubleIcon }) if (icon != null) { Object.Destroy(icon.texture); Object.Destroy(icon); }
            _effect = null; _double = null; _icon = null; _doubleIcon = null;
        }

        /// <summary>Gives the blessing (again, if it is already on: its time starts over).</summary>
        public static void Give(Player player, bool doubled)
        {
            if (!Plugin.Buff.Value || player == null || ObjectDB.instance == null) return;
            Register();
            // (one blessing at a time)
            StatusEffects.Remove(player, Hash);
            StatusEffects.Remove(player, DoubleHash);
            StatusEffects.Apply(player, doubled ? DoubleHash : Hash);
        }

        public static bool Has(Player player) => StatusEffects.Has(player, Hash) || StatusEffects.Has(player, DoubleHash);

        /// <summary>A small rainbow in a ring, drawn here so the mod needs no art.</summary>
        private static Sprite MakeIcon(bool twice)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - n / 2f + 0.5f, dy = y - n * 0.22f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = Mathf.InverseLerp(26f, 13f, r);   // outer red to inner violet
                    Color c = clear;
                    if (t > 0f && t < 1f && dy > -4f)
                    {
                        c = Sky.Spectrum(t);
                        c.a = Mathf.Clamp01(Mathf.Min(t, 1f - t) * 8f);
                    }
                    if (twice && c.a < 0.5f)   // a second, fainter one outside, the colours the other way round
                    {
                        float t2 = Mathf.InverseLerp(36f, 28f, r);
                        if (t2 > 0f && t2 < 1f && dy > -4f)
                        {
                            Color o = Sky.Spectrum(1f - t2);
                            o.a = Mathf.Clamp01(Mathf.Min(t2, 1f - t2) * 8f) * 0.7f;
                            if (o.a > c.a) c = o;
                        }
                    }
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>Rainbow's Blessing: stamina and health come back faster, and running and jumping cost less.</summary>
    public class SE_Rainbow : SE_Stats
    {
        protected virtual float Strength => 1f;
        protected virtual string Lead => T("rainbows_seen");

        public override void Setup(Character character)
        {
            m_ttl = Plugin.BuffMinutes.Value * 60f;
            m_staminaRegenMultiplier = 1f + 0.3f * Strength;
            m_healthRegenMultiplier = 1f + 0.2f * Strength;
            m_runStaminaDrainModifier = -0.2f * Strength;
            m_jumpStaminaUseModifier = -0.2f * Strength;
            base.Setup(character);
        }

        public override string GetTooltipString() =>
            Lead + "\n" + T("rainbows_tooltip", (30 * Strength).ToString("0"), (20 * Strength).ToString("0"), (20 * Strength).ToString("0"));
    }

    /// <summary>Double Rainbow's Blessing: the same, half as strong again.</summary>
    public class SE_DoubleRainbow : SE_Rainbow
    {
        protected override float Strength => 1.5f;
        protected override string Lead => T("rainbows_double");
    }
}
