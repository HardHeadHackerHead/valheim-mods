using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The companion's stamina, which the game's monsters do not have. The game already asks every character "have you the stamina?" before
    /// a swing or a block and tells it what one cost; for monsters the answer is always yes. Companions answer from their own pool instead
    /// (see the patches below), so swings, blocks and guard breaks work exactly as they do for players. Running drains it, and it comes back
    /// after a short pause; status effects that change stamina (Eikthyr's power, meads) apply through the game's own modifiers.
    ///
    /// The pool is kept by the game that runs the companion, and copied into its ZDO a few times a second for the menu and the party panel
    /// (and for whoever runs it next).
    /// </summary>
    internal static class Stamina
    {
        private const string Key = "dhc_stamina";
        private const float RunDrain = 10f, Regen = 8f, RegenDelay = 1f;

        private class Pool { public float Value, LastUse = -99f, LastSave = -99f, Saved = -1f; public bool Winded; }
        private static readonly Dictionary<Character, Pool> Pools = new Dictionary<Character, Pool>();

        public static float Max(Character c) => Food.MaxStamina(c);

        private static Pool Of(Character c)
        {
            if (!Pools.TryGetValue(c, out Pool p))
            {
                foreach (Character gone in new List<Character>(Pools.Keys)) if (gone == null) Pools.Remove(gone);
                Pools[c] = p = new Pool { Value = Companion.Zdo(c)?.GetFloat(Key, Max(c)) ?? Max(c) };
            }
            return p;
        }

        /// <summary>The current value: from the pool on the game that runs it, from its ZDO elsewhere.</summary>
        public static float Get(Character c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v != null && v.IsValid() && !v.IsOwner()) return Mathf.Min(Max(c), v.GetZDO().GetFloat(Key, Max(c)));
            return Mathf.Min(Max(c), Of(c).Value);
        }

        public static void Use(Character c, float amount)
        {
            if (amount <= 0f) return;
            Pool p = Of(c);
            p.Value = Mathf.Max(0f, p.Value - amount);
            p.LastUse = Time.time;
            if (p.Value <= 0.5f) p.Winded = true;
            Save(c, p, false);
        }

        public static void Add(Character c, float amount)
        {
            Pool p = Of(c);
            p.Value = Mathf.Min(Max(c), p.Value + amount);
        }

        /// <summary>Too tired to run: once emptied, it walks until it has a third back.</summary>
        public static bool CanRun(Character c)
        {
            Pool p = Of(c);
            if (p.Winded && p.Value > Max(c) * 0.33f) p.Winded = false;
            return !p.Winded && p.Value > 5f;
        }

        /// <summary>Every frame on the game that runs it: running costs, standing still (after a pause) brings it back.</summary>
        public static void Tick(Humanoid c, float dt)
        {
            Pool p = Of(c);
            Rigidbody body = c.GetComponent<Rigidbody>();
            Vector3 v = body != null ? body.linearVelocity : Vector3.zero;
            v.y = 0f;
            if (c.IsRunning() && v.magnitude > 1f)
            {
                float drain = RunDrain;
                c.GetSEMan().ModifyRunStaminaDrain(RunDrain, ref drain, v.normalized);
                Use(c, drain * dt);
            }
            else if (Time.time - p.LastUse > RegenDelay && p.Value < Max(c))
            {
                float mult = 1f;
                c.GetSEMan().ModifyStaminaRegen(ref mult);
                p.Value = Mathf.Min(Max(c), p.Value + Regen * mult * dt * (c.IsBlocking() ? 0.5f : 1f));
            }
            Save(c, p, false);
        }

        private static void Save(Character c, Pool p, bool now)
        {
            if (!now && Time.time - p.LastSave < 0.25f && Mathf.Abs(p.Value - p.Saved) < 5f) return;
            ZNetView v = c.GetComponent<ZNetView>();
            if (v == null || !v.IsValid() || !v.IsOwner()) return;
            p.LastSave = Time.time;
            p.Saved = p.Value;
            v.GetZDO().Set(Key, p.Value);
        }

        public static void Forget() => Pools.Clear();
    }

    // ---- the game's stamina questions, answered for companions ----

    [HarmonyPatch(typeof(Character), nameof(Character.HaveStamina))]
    internal static class Character_HaveStamina
    {
        private static bool Prefix(Character __instance, float amount, ref bool __result)
        {
            if (!Companion.Is(__instance)) return true;
            __result = Stamina.Get(__instance) > amount;
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    internal static class Character_UseStamina
    {
        private static bool Prefix(Character __instance, float stamina)
        {
            if (!Companion.Is(__instance)) return true;
            Stamina.Use(__instance, stamina);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
    internal static class Character_AddStamina
    {
        private static bool Prefix(Character __instance, float v)
        {
            if (!Companion.Is(__instance)) return true;
            Stamina.Add(__instance, v);
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetMaxStamina))]
    internal static class Character_GetMaxStamina
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Stamina.Max(__instance); }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetStaminaPercentage))]
    internal static class Character_GetStaminaPercentage
    {
        private static void Postfix(Character __instance, ref float __result) { if (Companion.Is(__instance)) __result = Stamina.Get(__instance) / Mathf.Max(1f, Stamina.Max(__instance)); }
    }

    // ---- the boss powers (Forsaken powers) reach companions too ----

    // The game gives a power to every player within 10 m of whoever used it; companions in that range get it as well. (Through the game's
    // own status-effect message, so it reaches a companion run by another player's game.)
    [HarmonyPatch(typeof(Player), nameof(Player.ActivateGuardianPower))]
    internal static class Player_ActivateGuardianPower
    {
        private static readonly AccessTools.FieldRef<Player, StatusEffect> Power = AccessTools.FieldRefAccess<Player, StatusEffect>("m_guardianSE");

        private static void Prefix(Player __instance, out bool __state) => __state = __instance.m_guardianPowerCooldown <= 0f && Power(__instance) != null;

        private static void Postfix(Player __instance, bool __state)
        {
            if (!__state || __instance.m_guardianPowerCooldown <= 0f) return; // it did not fire
            StatusEffect se = Power(__instance);
            foreach (Humanoid c in Companion.All())
            {
                if (Vector3.Distance(c.transform.position, __instance.transform.position) > 10f) continue;
                c.GetSEMan().AddStatusEffect(se.NameHash(), true, 0, 0f, -1);
                Plugin.Instance?.Note($"{Companion.NameOf(c)} shares {__instance.GetPlayerName()}'s power: {Localization.instance.Localize(se.m_name)}");
                Brain.Get(c)?.Remember("blessed by " + Localization.instance.Localize(se.m_name));
            }
        }
    }
}
