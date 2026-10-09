using UnityEngine;

namespace Rainbows
{
    /// <summary>The buff for looking up at a rainbow, and its registration with the game's status effects.</summary>
    internal static class Blessing
    {
        public const string EffectName = "SE_RainbowBlessing", DoubleName = "SE_DoubleRainbowBlessing";
        public static readonly int Hash = EffectName.GetStableHashCode(), DoubleHash = DoubleName.GetStableHashCode();
        private static SE_Rainbow _effect, _double;
        private static Sprite _icon, _doubleIcon;

        public static void Register()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items.Count == 0) return;   // (the game's own lists are not loaded yet: asked again)
            if (_effect == null)
            {
                _effect = ScriptableObject.CreateInstance<SE_Rainbow>();
                _effect.name = EffectName;
                _effect.m_name = "Rainbow's Blessing";
                _effect.m_icon = _icon = MakeIcon(false);
                _double = ScriptableObject.CreateInstance<SE_DoubleRainbow>();
                _double.name = DoubleName;
                _double.m_name = "Double Rainbow's Blessing";
                _double.m_icon = _doubleIcon = MakeIcon(true);
            }
            foreach (SE_Rainbow e in new[] { _effect, _double })
            {
                if (db.m_StatusEffects.Contains(e)) continue;
                db.m_StatusEffects.RemoveAll(x => x == null || x.name == e.name);
                db.m_StatusEffects.Add(e);
            }
        }

        public static void Unregister()
        {
            ObjectDB db = ObjectDB.instance;
            Player player = Player.m_localPlayer;
            if (player != null && _effect != null) { player.GetSEMan().RemoveStatusEffect(Hash, true); player.GetSEMan().RemoveStatusEffect(DoubleHash, true); }   // (a copy left in the player would point at what is destroyed below)
            if (db != null) db.m_StatusEffects.RemoveAll(e => e == null || e == _effect || e == _double);
            if (_effect != null) Object.Destroy(_effect);
            if (_double != null) Object.Destroy(_double);
            foreach (Sprite icon in new[] { _icon, _doubleIcon }) if (icon != null) { Object.Destroy(icon.texture); Object.Destroy(icon); }
            _effect = null; _double = null; _icon = null; _doubleIcon = null;
        }

        /// <summary>Gives the blessing (again, if it is already on: its time starts over).</summary>
        public static void Give(Player player, bool doubled)
        {
            if (!Plugin.Buff.Value || player == null || ObjectDB.instance == null) return;
            Register();
            SEMan sem = player.GetSEMan();
            // (one blessing at a time; a copy left from before this mod reloaded has no icon, so it is always replaced)
            sem.RemoveStatusEffect(Hash, true);
            sem.RemoveStatusEffect(DoubleHash, true);
            sem.AddStatusEffect(doubled ? DoubleHash : Hash, true);
        }

        public static bool Has(Player player) => player != null && (player.GetSEMan().HaveStatusEffect(Hash) || player.GetSEMan().HaveStatusEffect(DoubleHash));

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
        protected virtual string Lead => "Seen a rainbow.";

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
            $"{Lead}\nStamina comes back {30 * Strength:0}% faster, health {20 * Strength:0}% faster, and running and jumping cost {20 * Strength:0}% less stamina.";
    }

    /// <summary>Double Rainbow's Blessing: the same, half as strong again.</summary>
    public class SE_DoubleRainbow : SE_Rainbow
    {
        protected override float Strength => 1.5f;
        protected override string Lead => "A double rainbow!";
    }
}
