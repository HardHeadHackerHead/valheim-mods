using DHack.Shared;
using UnityEngine;

namespace CigarSmoking
{
    /// <summary>
    /// The status effect of a lit cigar (one per type). It gives that type's bonuses for as long as the cigar lasts, and writes when the cigar
    /// started, when it ends and which type it is into the player's ZDO, which is what every game (including other players') reads to draw the
    /// cigar and the smoke. An end time rather than an on/off flag, so a player who quits or crashes while smoking cannot be left smoking.
    /// Lighting another cigar (of any type) puts out the one before: only one effect runs at a time. Made and registered with
    /// Shared/StatusEffects.cs; after a reload of the mod it is put back with the time it had left, and tells the others how much is left.
    /// </summary>
    public class SE_Smoking : SE_Stats, StatusEffects.ICarry
    {
        internal static readonly int KeyStart = "dh_smoke_start".GetStableHashCode();
        internal static readonly int KeyEnd = "dh_smoke_end".GetStableHashCode();
        internal static readonly int KeyType = "dh_smoke_type".GetStableHashCode();

        public int TypeIndex;

        public override void Setup(Character character)
        {
            // Pipes and other registered add-ons share the slot with cigars.
            Plugin.Instance?.StopOtherSmoking(character, name);

            CigarType t = Types.ByIndex(TypeIndex);
            float s = Plugin.EffectStrength.Value / 100f;
            m_ttl = Plugin.Minutes.Value * 60f;
            m_staminaRegenMultiplier = 1f + (t.StaminaRegen - 1f) * s;
            m_healthRegenMultiplier = 1f + (t.HealthRegen - 1f) * s;
            m_attackStaminaUseModifier = t.AttackUse * s;
            m_runStaminaDrainModifier = t.RunDrain * s;
            m_noiseModifier = t.Noise * s;
            m_stealthModifier = t.Stealth * s;
            base.Setup(character);
            if (!StatusEffects.PuttingBack) Publish();   // (put back after a reload: PutBack says it, with the time it had left)
        }

        // Only its time is carried over a reload (the helper does that): how much has burned away follows from it.
        public float Carry => 0f;
        public void PutBack(float carry) => Publish(true);

        public override void ResetTime()
        {
            base.ResetTime();
            Publish();
        }

        public override void Stop()
        {
            base.Stop();
            ZDO zdo = Zdo();
            if (zdo != null) zdo.Set(KeyEnd, 0L);
        }

        private ZDO Zdo()
        {
            ZNetView view = m_character != null ? m_character.GetComponent<ZNetView>() : null;
            return view != null && view.IsValid() && view.IsOwner() ? view.GetZDO() : null;
        }

        /// <summary>When it was lit and when it ends, for every game to draw. m_time is 0 when it is lit; put back after a reload of the mod, it is
        /// how long the cigar had burned, and the time it was lit is kept (so no game draws it being lit again).</summary>
        private void Publish(bool putBack = false)
        {
            ZDO zdo = Zdo();
            if (zdo == null || ZNet.instance == null) return;
            double now = ZNet.instance.GetTimeSeconds();
            long start = (long)((now - m_time) * 1000.0), lit = zdo.GetLong(KeyStart, 0L);
            if (putBack && System.Math.Abs(lit - start) < 2000L) start = lit;
            zdo.Set(KeyStart, start);
            zdo.Set(KeyEnd, (long)((now + m_ttl - m_time) * 1000.0));
            zdo.Set(KeyType, TypeIndex);
        }
    }

    internal static class Smoking
    {
        /// <summary>A cigar type's effect, kept by Shared/StatusEffects.cs. Its name stays SE_dh_smoking_&lt;type&gt; (not renamed after the mod: add-ons
        /// and the one-smoke rule know it by that name, and "dh_smoking" is ours alone).</summary>
        internal static SE_Smoking Make(CigarType t, Sprite icon)
        {
            SE_Smoking se = StatusEffects.Create<SE_Smoking>(Plugin.EffectPrefix + t.Id, t.Status, icon, t.Flavour);
            se.TypeIndex = Types.IndexOf(t);
            se.m_category = "dh_cigar"; // one cigar at a time
            se.m_startMessageType = MessageHud.MessageType.TopLeft;
            se.m_startMessage = t.Start;
            se.m_stopMessageType = MessageHud.MessageType.TopLeft;
            se.m_stopMessage = t.Stop;
            return se;
        }
    }
}
