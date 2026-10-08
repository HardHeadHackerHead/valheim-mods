using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// The skills the game only trains for players, trained for companions at the same moments. The game raises Jump in Character.Jump
    /// only through the Skills a player has (a companion has none), and Run, Swim, Dodge and Sneak in Player's own code; cooking and crafting
    /// credit the local player. Running, sneaking, rolling, cooking and crafting are raised where the companion does them (Stamina, Brain,
    /// Tactics, Kitchen, Upgrades, Repair); jumping, swimming and the perfect dodge here.
    /// </summary>
    internal static class SkillMoments
    {
        public static bool Mine(Character c, out Humanoid h)
        {
            h = c as Humanoid;
            return h != null && Companion.Is(h) && (h.GetComponent<ZNetView>()?.IsOwner() ?? false);
        }
    }

    // A jump: the game's jump went off (it left the ground, upward).
    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class Character_Jump_Skill
    {
        private static void Prefix(Character __instance, out float __state) => __state = __instance.GetVelocity().y;

        private static void Postfix(Character __instance, float __state)
        {
            if (!SkillMoments.Mine(__instance, out Humanoid h)) return;
            if (__instance.GetVelocity().y > __state + 1f) Skill.Raise(h, Skills.SkillType.Jump, 1f);
        }
    }

    // Swimming: every second it swims somewhere raises Swim (Player.OnSwimming's timer).
    [HarmonyPatch(typeof(Character), "OnSwimming")]
    internal static class Character_OnSwimming_Skill
    {
        private static void Postfix(Character __instance, Vector3 targetVel, float dt)
        {
            if (targetVel.magnitude > 0.1f && SkillMoments.Mine(__instance, out Humanoid h)) Skill.Doing(h, Skills.SkillType.Swim, dt);
        }
    }

    // A perfect dodge: a hit that came while it was rolling through it (Player.HitWhileDodging), once a roll.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_Dodge
    {
        private static void Prefix(Character __instance)
        {
            if (!SkillMoments.Mine(__instance, out Humanoid h) || !__instance.IsDodgeInvincible()) return;
            BrainState st = Brain.Get(h);
            if (st == null || st.RollCredited) return;
            st.RollCredited = true;
            Skill.Raise(h, Skills.SkillType.Dodge, 1f);
        }
    }
}
