using DHack.Shared;
using UnityEngine;
using static DHack.Shared.Translations;

namespace SkalTavern
{
    /// <summary>
    /// How drunk you are (0 sober, 100 very drunk, past it you fall), what each stage does, and the status effects that show it. It rises with
    /// every drink and falls with time (Drinking: MinutesToSober). Stages: Warm (a little stamina), Merry, Tipsy (the view sways, your feet
    /// wander, and the cold no longer bites), Drunk (more of both, and now and then you stagger), Sloshed. Falling down sobers you a little; a big night ends in a
    /// hangover.
    /// </summary>
    internal static class Tipsy
    {
        public static float Level;
        private static float _peak, _nextStumble, _nextPuke;
        private static readonly int PukeHash = "Puke".GetStableHashCode();   // (the game's own: the vomit, and the sounds)

        public static string Stage(float level) => level < 12f ? "Warm" : level < 35f ? "Merry" : level < 60f ? "Tipsy" : level < 85f ? "Drunk" : "Sloshed";

        /// <summary>The stage's name in the player's language (Stage stays English: it is compared, and the skal command reports it).</summary>
        public static string StageName(float level)
        {
            switch (Stage(level))
            {
                case "Warm": return T("skaltavern_stage_warm");
                case "Merry": return T("skaltavern_stage_merry");
                case "Tipsy": return T("skaltavern_stage_tipsy");
                case "Drunk": return T("skaltavern_stage_drunk");
                default: return T("skaltavern_stage_sloshed");
            }
        }

        /// <summary>A drink, or a swig of one: share is how much of a whole drink it is (a swig is a quarter).</summary>
        public static void Drink(float amount, float share = 1f)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            float before = Level;
            // too much, too fast: the drink comes back up (and it counts for less). Two in five for a whole drink, spread over its swigs.
            if (amount > 0f && before >= 85f && Random.value < 1f - Mathf.Pow(0.6f, share)) { Puke(player); before = Level; amount *= 0.5f; }
            Level = Mathf.Min(130f, Level + amount);
            _peak = Mathf.Max(_peak, Level);
            SEMan sem = player.GetSEMan();
            if (!sem.HaveStatusEffect(Drinks.TipsyHash)) sem.AddStatusEffect(Drinks.TipsyHash, false);
            if (Stage(before) != Stage(Level)) player.Message(MessageHud.MessageType.TopLeft, StageName(Level) + (Level >= 60f ? "..." : ""));
        }

        /// <summary>Every frame: sobering up, and what too much does.</summary>
        public static void Tick(Player player, float dt)
        {
            SEMan sem = player.GetSEMan();
            if (player.IsDead()) { Level = 0f; _peak = 0f; }
            bool has = sem.HaveStatusEffect(Drinks.TipsyHash);
            if (Level <= 0f)
            {
                if (has) sem.RemoveStatusEffect(Drinks.TipsyHash, true);   // (it was saved with you from before the game was closed)
                return;
            }
            if (!has) sem.AddStatusEffect(Drinks.TipsyHash, false);

            Level -= 100f / (Plugin.SoberMinutes.Value * 60f) * dt;
            if (Level <= 0f)
            {
                Level = 0f;
                sem.RemoveStatusEffect(Drinks.TipsyHash, true);
                if (Plugin.HangoverOn.Value && _peak >= 55f)
                {
                    sem.AddStatusEffect(Drinks.HangoverHash, true);
                    player.Message(MessageHud.MessageType.Center, T("skaltavern_sober_hangover"));
                }
                _peak = 0f;
                return;
            }

            if (Level >= 100f && Plugin.PassOutOn.Value)
            {
                Puke(player);   // (and down you go)
                Level = Mathf.Min(Level, 62f);
                player.Stagger(-player.transform.forward);
                player.Message(MessageHud.MessageType.Center, T("skaltavern_pass_out"));
            }
            else if (Level >= 88f && Time.time >= _nextPuke)
            {
                _nextPuke = Time.time + Random.Range(22f, 42f);
                if (Random.value < 0.5f) Puke(player);
            }
            else if (Level >= 75f && Plugin.StumbleOn.Value && Time.time >= _nextStumble)
            {
                _nextStumble = Time.time + Random.Range(10f, 22f);
                if (Random.value < 0.6f && player.IsOnGround() && !player.InPlaceMode())
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    player.Stagger(new Vector3(dir.x, 0f, dir.y));
                }
            }
        }

        /// <summary>You throw up: the game's own vomit effect and sounds, and you are a little less drunk for it.</summary>
        public static void Puke(Player player)
        {
            if (!Plugin.PukeOn.Value || player == null) return;
            SEMan sem = player.GetSEMan();
            if (!sem.HaveStatusEffect(PukeHash)) sem.AddStatusEffect(PukeHash, true);
            Level = Mathf.Max(0f, Level - 14f);
            player.Message(MessageHud.MessageType.TopLeft, T("skaltavern_puke"));
        }

        /// <summary>How drunk you were when the mod was reloaded, from the Tipsy effect it put back (Shared/StatusEffects.cs).</summary>
        public static void PutBack(float level)
        {
            Level = Mathf.Clamp(level, 0f, 130f);
            _peak = Mathf.Max(_peak, Level);
        }

        public static void Clear()
        {
            Level = 0f; _peak = 0f;
            Player player = Player.m_localPlayer;
            if (player == null) return;
            foreach (int hash in new[] { Drinks.TipsyHash, Drinks.HangoverHash, Drinks.SkalHash }) player.GetSEMan().RemoveStatusEffect(hash, true);
            Fx.Reset();
        }

        /// <summary>How far into the sway, 0 to 1: nothing until about a fifth of the way drunk.</summary>
        public static float Wobble => Mathf.InverseLerp(20f, 100f, Level);
    }

    // ---- the status effects ----------------------------------------------------------------------------------------

    /// <summary>One swig of a drink: raises how drunk you are, then is gone (the Tipsy effect is what stays). Put back after a reload of the
    /// mod (in its last second) it isn't drunk again.</summary>
    public class SE_Drink : StatusEffect
    {
        public float Amount;
        public float Share = 1f;   // (how much of a whole drink this is: a quarter, one swig)

        public override void Setup(Character character)
        {
            base.Setup(character);
            if (character == Player.m_localPlayer && !StatusEffects.PuttingBack) Tipsy.Drink(Amount, Share);
        }
    }

    /// <summary>The lasting effect of drinking: a little warmth and stamina when you are merry, none of it (and worse) when you are not. It
    /// carries how drunk you are over a reload of the mod (that lives in Tipsy.Level, which a reloaded copy starts again at 0).</summary>
    public class SE_Tipsy : SE_Stats, StatusEffects.ICarry
    {
        public float Carry => m_character == Player.m_localPlayer ? Tipsy.Level : float.NaN;

        public void PutBack(float carry)
        {
            if (!float.IsNaN(carry) && m_character == Player.m_localPlayer) Tipsy.PutBack(carry);
            Apply();
        }

        public override void Setup(Character character)
        {
            m_ttl = 0f;   // (stays until you are sober)
            m_mods = new System.Collections.Generic.List<HitData.DamageModPair>();   // its own list: Clone shares the stored effect's with every copy
            base.Setup(character);
            Apply();
        }

        public override void UpdateStatusEffect(float dt)
        {
            Apply();
            base.UpdateStatusEffect(dt);
        }

        private void Apply()
        {
            float level = Tipsy.Level;
            m_staminaRegenMultiplier = level < 8f ? 1f
                : level < 70f ? 1f + 0.25f * Mathf.InverseLerp(8f, 45f, Mathf.Min(level, 45f))
                : Mathf.Lerp(1.15f, 0.85f, Mathf.InverseLerp(70f, 110f, level));
            m_healthRegenMultiplier = level < 8f ? 1f : 1.1f;
            m_mods.Clear();
            // warm against the cold only once you are properly tipsy (and your feet wander): the game lets any frost resistance, even slight,
            // keep the cold and freezing off entirely, so a single ale must not do it
            if (level >= 35f) m_mods.Add(new HitData.DamageModPair { m_type = HitData.DamageType.Frost, m_modifier = HitData.DamageModifier.Resistant });
        }

        public override string GetIconText() => Tipsy.StageName(Tipsy.Level);

        public override string GetTooltipString() =>
            Tipsy.StageName(Tipsy.Level) + "\n" + T("skaltavern_tipsy_tooltip");
    }

    /// <summary>A few minutes of a sore head after a big night.</summary>
    public class SE_Hangover : SE_Stats
    {
        public override void Setup(Character character)
        {
            m_ttl = 180f;
            m_staminaRegenMultiplier = 0.7f;
            m_healthRegenMultiplier = 0.85f;
            base.Setup(character);
        }

        public override string GetTooltipString() => T("skaltavern_hangover_tooltip");
    }
}
