using UnityEngine;

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

        public static void Drink(float amount)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            float before = Level;
            // too much, too fast: the drink comes back up (and it counts for less)
            if (amount > 0f && before >= 85f && Random.value < 0.4f) { Puke(player); before = Level; amount *= 0.5f; }
            Level = Mathf.Min(130f, Level + amount);
            _peak = Mathf.Max(_peak, Level);
            SEMan sem = player.GetSEMan();
            if (!sem.HaveStatusEffect(Drinks.TipsyHash)) sem.AddStatusEffect(Drinks.TipsyHash, false);
            if (Stage(before) != Stage(Level)) player.Message(MessageHud.MessageType.TopLeft, Stage(Level) + (Level >= 60f ? "..." : ""));
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
                    player.Message(MessageHud.MessageType.Center, "You are sober, and your head knows it.");
                }
                _peak = 0f;
                return;
            }

            if (Level >= 100f && Plugin.PassOutOn.Value)
            {
                Puke(player);   // (and down you go)
                Level = Mathf.Min(Level, 62f);
                player.Stagger(-player.transform.forward);
                player.Message(MessageHud.MessageType.Center, "The floor comes up to meet you.");
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
            player.Message(MessageHud.MessageType.TopLeft, "You throw up.");
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

    /// <summary>One drink: raises how drunk you are, then is gone (the Tipsy effect is what stays).</summary>
    public class SE_Drink : StatusEffect
    {
        public float Amount;

        public override void Setup(Character character)
        {
            base.Setup(character);
            if (character == Player.m_localPlayer) Tipsy.Drink(Amount);
        }
    }

    /// <summary>The lasting effect of drinking: a little warmth and stamina when you are merry, none of it (and worse) when you are not.</summary>
    public class SE_Tipsy : SE_Stats
    {
        public override void Setup(Character character)
        {
            m_ttl = 0f;   // (stays until you are sober)
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

        public override string GetIconText() => Tipsy.Stage(Tipsy.Level);

        public override string GetTooltipString() =>
            Tipsy.Stage(Tipsy.Level) + "\nThe more you drink the more the world sways. A little warms you and gives some stamina; too much slows you down and knocks you over.";
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

        public override string GetTooltipString() => "A sore head. Stamina and health come back slower for a few minutes.";
    }
}
