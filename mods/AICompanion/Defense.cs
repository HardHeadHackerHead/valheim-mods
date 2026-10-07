using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Defending itself as a good player does, when something swings at it:
    ///   - a timed block: each kind of creature's swing has its own wind-up, learned by watching (the moment its attack starts, the moment the
    ///     game deals the hit: Humanoid.StartAttack, Attack.OnAttackTrigger). It raises its shield just before the hit lands, inside the game's
    ///     parry window (the window starts when the block goes up: Humanoid.UpdateBlock), so a good block is a parry, which staggers the
    ///     attacker, and it hits back at once. A creature it has not seen swing yet it simply blocks.
    ///   - stepping aside instead of blocking (the rule RuneFellowship uses): when the attack sweeps an area, cannot be blocked, comes from a
    ///     bow or a spell, would hit harder than about 80% of its block, or it is short of stamina or has nothing to block with. A quick
    ///     sideways step to a safe spot (ground, no stakes or fire, no drop, no water), away from the attacker's line.
    /// </summary>
    internal static class Defense
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        internal static readonly AccessTools.FieldRef<Attack, Humanoid> AttackOwner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        private static readonly Dictionary<int, KeyValuePair<string, float>> Started = new Dictionary<int, KeyValuePair<string, float>>(); // attacker -> (kind, when)
        private static readonly Dictionary<string, float> WindUp = new Dictionary<string, float>();                                       // kind -> seconds to the hit
        private const float ParryLead = 0.15f;                                                                                              // raise this long before the hit

        public static void Forget() { Started.Clear(); WindUp.Clear(); }

        private static string Kind(Humanoid attacker) => Utils.GetPrefabName(attacker.gameObject) + "|" + (CurrentAttack(attacker)?.m_attackAnimation ?? "?");

        internal static void OnStart(Humanoid attacker)
        {
            if (attacker == null || attacker.IsPlayer() || Companion.Is(attacker)) return;
            Started[attacker.GetInstanceID()] = new KeyValuePair<string, float>(Kind(attacker), Time.time);
            if (Started.Count > 300) Started.Clear();
        }

        internal static void OnTrigger(Attack attack)
        {
            Humanoid owner = AttackOwner(attack);
            if (owner == null || owner.IsPlayer() || !Started.TryGetValue(owner.GetInstanceID(), out var start)) return;
            Started.Remove(owner.GetInstanceID()); // the first hit of a swing only (a combo's later hits do not teach it)
            float delay = Time.time - start.Value;
            if (delay < 0.05f || delay > 3f) return;
            WindUp[start.Key] = WindUp.TryGetValue(start.Key, out float had) ? Mathf.Lerp(had, delay, 0.3f) : delay;
        }

        /// <summary>How far away a swing can reach it: a bow or spell about 30 m, else the attacker's reach.</summary>
        public static float Reach(Character attacker)
        {
            Attack attack = attacker is Humanoid h ? CurrentAttack(h) : null;
            if (attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null)) return 30f;
            return Mathf.Max(4.5f, (attack?.m_attackRange ?? 3f) + 1.5f) + attacker.GetRadius();
        }

        /// <summary>Should the block go up now? Before a learned hit time: not yet (it waits, facing it). Unknown: yes.</summary>
        public static bool RaiseNow(Character attacker)
        {
            if (!(attacker is Humanoid h) || !Started.TryGetValue(h.GetInstanceID(), out var start) || !WindUp.TryGetValue(start.Key, out float windUp)) return true;
            return Time.time - start.Value >= windUp - ParryLead;
        }

        public static bool Learned(Character attacker) => attacker is Humanoid h && Started.TryGetValue(h.GetInstanceID(), out var s) && WindUp.ContainsKey(s.Key);

        /// <summary>Step aside rather than block? (area, unblockable, ranged, too hard a hit, out of breath, nothing to block with)</summary>
        public static bool ShouldEvade(Humanoid me, Character attacker, out string why)
        {
            why = null;
            Attack attack = attacker is Humanoid h ? CurrentAttack(h) : null;
            ItemDrop.ItemData weapon = attacker is Humanoid hw ? hw.GetCurrentWeapon() : null;
            if (attack != null && (attack.m_attackType == Attack.AttackType.Area || attack.m_attackAngle > 150f)) { why = "a sweeping attack"; return true; }
            if (attack != null && (attack.m_attackType == Attack.AttackType.Projectile || attack.m_attackProjectile != null)) { why = "a shot"; return true; }
            if (weapon != null && !weapon.m_shared.m_blockable) { why = "an attack it cannot block"; return true; }
            if (!Companion.CanBlock(me)) { why = "nothing to block with"; return true; }
            if (Stamina.Get(me) < 20f && Stamina.Get(me) >= Tactics.DodgeCost) { why = "too tired to block"; return true; }
            ItemDrop.ItemData blocker = Companion.Shield(me) ?? me.GetCurrentWeapon();
            float block = blocker != null ? blocker.GetBlockPower(Skill.Get(me, Skills.SkillType.Blocking) / 100f) : 0f;
            float incoming = weapon != null ? weapon.GetDamage().GetTotalDamage() * (attack?.m_damageMultiplier ?? 1f) * Mathf.Max(1, attacker.GetLevel()) : 0f;
            if (block > 0f && incoming > block * 0.8f && !Learned(attacker)) { why = "a hit harder than its block"; return true; } // (a timed parry takes it whatever its size)
            return false;
        }

        /// <summary>A safe spot about 3 m aside from the attacker's line (or back), and a quick step there. False when there is none.</summary>
        public static bool StepAside(BrainState st, Character attacker)
        {
            Humanoid me = st.Body;
            if (Stamina.Get(me) < Tactics.DodgeCost) return false; // (no breath for a roll: it blocks or takes it)
            Vector3 away = me.transform.position - attacker.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -me.transform.forward;
            away.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, away);
            if (st.EvadeSide == 0f) st.EvadeSide = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            foreach (Vector3 dir in new[] { side * st.EvadeSide, -side * st.EvadeSide, (side * st.EvadeSide + away).normalized, (-side * st.EvadeSide + away).normalized, away })
            {
                Vector3 to = me.transform.position + dir * 3f;
                if (!Safe(me, to)) continue;
                Tactics.Roll(st, dir);      // a player's roll: its animation, its stamina, its moment no hit lands
                me.ApplyPushback(dir, 25f); // (and the push that carries it)
                me.SetMoveDir(dir);
                me.SetRun(Stamina.CanRun(me));
                st.EvadeUntil = Time.time + 0.6f;
                st.EvadeTo = to;
                st.EvadeSide = Vector3.Dot(dir, side) >= 0f ? 1f : -1f;
                return true;
            }
            return false;
        }

        private static readonly int Ground = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

        private static bool Safe(Humanoid me, Vector3 to)
        {
            if (!Physics.Raycast(to + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3.5f, Ground)) return false;  // ground there
            if (Mathf.Abs(hit.point.y - me.transform.position.y) > 1f) return false;                                     // no drop, no climb
            if (ZoneSystem.instance != null && hit.point.y < ZoneSystem.instance.m_waterLevel - 0.5f) return false;       // no water
            if (Physics.CheckCapsule(me.transform.position + Vector3.up * 0.8f, to + Vector3.up * 0.8f, 0.3f, LayerMask.GetMask("Default", "static_solid", "piece"))) return false; // a wall
            return Steer.Safe(hit.point, me);                                                                              // no stakes, no fire
        }

        /// <summary>For the log: a block that came inside the parry window (the game staggers the attacker for it).</summary>
        internal static void OnBlocked(Humanoid me, Character attacker, bool blocked)
        {
            if (!blocked || !Companion.Is(me) || attacker == null) return;
            bool parry = BlockTimer(me) >= 0f && BlockTimer(me) < 0.25f;
            BrainState st = Brain.Get(me);
            if (parry) { st.Parries++; st.CounterUntil = Time.time + 1.2f; Journal.Parried(me, attacker); }
            else st.Blocks++;
            Activity.Log(me, (parry ? "parried " : "blocked ") + Localization.instance.Localize(attacker.m_name) + (Learned(attacker) ? " (timed)" : ""));
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack_Learn
    {
        private static void Postfix(Humanoid __instance, bool __result) { if (__result) try { Defense.OnStart(__instance); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class Attack_OnAttackTrigger_Learn
    {
        private static void Prefix(Attack __instance) { try { Defense.OnTrigger(__instance); } catch (Exception) { } }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Humanoid_BlockAttack_Log
    {
        private static void Postfix(Humanoid __instance, Character attacker, bool __result) { try { Defense.OnBlocked(__instance, attacker, __result); } catch (Exception) { } }
    }
}
