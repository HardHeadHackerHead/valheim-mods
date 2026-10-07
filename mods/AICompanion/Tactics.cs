using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Fighting the way a good player fights:
    ///   - with you: it joins in on what you are hitting, gets round to the other side of it (so it cannot face you both), and when you are
    ///     badly hurt it goes for whatever is on you first;
    ///   - sneaking: a creature that has not noticed it, it walks up to from behind (not running: that is heard) and strikes, for the game's
    ///     backstab bonus;
    ///   - its weapon's special attack (the secondary attack): a sweep or slam into a bunch of enemies, and every few blows a stagger on a
    ///     tough one; never a throw (it would lose its weapon);
    ///   - hit and run against heavy hitters (a big hit for its health, or a creature that has hurt or killed it before): a blow, then a step
    ///     back out of reach while the creature swings at nothing, then in again;
    ///   - meads at the right moment: stamina mead when it runs out mid-fight, poison, frost or fire resistance against that kind of harm, eitr
    ///     for a staff;
    ///   - remembering what hurt it (the journal's tally): it opens with the bow on those, keeps its distance, and warns you;
    ///   - a bow that kites: it backs off while something closes in, and draws its blade only when cornered.
    /// </summary>
    internal static class Tactics
    {
        // ---- with you ----------------------------------------------------------------------------------------

        public static Character YourTarget;
        public static float YourTargetAt;

        /// <summary>What you hit lately (the last few seconds) and is still alive.</summary>
        public static Character Yours() => YourTarget != null && !YourTarget.IsDead() && Time.time - YourTargetAt < 6f ? YourTarget : null;

        /// <summary>To get round to the far side of the creature you are fighting: where to stand, or null when it is there already (or need not).</summary>
        public static Vector3? Pincer(Humanoid me, Player master, Character target, float reach)
        {
            if (master == null || target != Yours() || Brain.TargetOf(target) != master) return null;
            Vector3 fromYou = target.transform.position - master.transform.position; fromYou.y = 0f;
            Vector3 fromMe = me.transform.position - target.transform.position; fromMe.y = 0f;
            if (fromYou.sqrMagnitude < 0.01f || fromYou.magnitude > 6f) return null;
            if (Vector3.Angle(fromYou, fromMe) < 80f) return null; // on the far side already
            return target.transform.position + fromYou.normalized * (target.GetRadius() + reach * 0.7f);
        }

        // ---- sneaking ----------------------------------------------------------------------------------------

        /// <summary>It has not noticed anyone (the game gives a backstab against such a creature).</summary>
        public static bool Unaware(Character c) => c.GetBaseAI() is BaseAI ai && !ai.IsAlerted() && Brain.TargetOf(c) == null && !c.IsBoss();

        /// <summary>Behind it, or null when it is behind it already.</summary>
        public static Vector3? Behind(Humanoid me, Character target, float reach)
        {
            Vector3 fromIt = me.transform.position - target.transform.position; fromIt.y = 0f;
            if (Vector3.Angle(target.transform.forward, fromIt) > 120f) return null;
            return target.transform.position - target.transform.forward * (target.GetRadius() + reach * 0.6f);
        }

        // ---- the weapon's special --------------------------------------------------------------------------

        /// <summary>Its secondary attack now? A sweep or slam into three or more, or every third blow on a tough one to stagger it. Never a throw.</summary>
        public static bool Special(BrainState st, ItemDrop.ItemData weapon, Character target, float dist)
        {
            if (weapon == null || !weapon.HaveSecondaryAttack()) return false;
            Attack special = weapon.m_shared.m_secondaryAttack;
            if (special == null || special.m_attackType == Attack.AttackType.Projectile || special.m_attackProjectile != null) return false;
            if (Stamina.Get(st.Body) < special.m_attackStamina + 15f || dist > Mathf.Max(1.2f, special.m_attackRange * 0.9f)) return false;
            bool wide = special.m_attackType == Attack.AttackType.Area || special.m_attackAngle >= 180f;
            int near = st.Enemies.Count(e => e != null && !e.IsDead() && Vector3.Distance(e.transform.position, st.Body.transform.position) < 3.5f);
            if (wide && near >= 3) return true;
            return !target.IsStaggering() && target.GetMaxHealth() >= 100f && st.Blows % 3 == 2;
        }

        // ---- heavy hitters and what hurt it before ------------------------------------------------------------

        /// <summary>Has hurt it badly or killed it before (its journal): it is careful with those.</summary>
        public static bool Feared(Humanoid me, Character c)
        {
            var tally = Journal.Tally(me);
            string name = Localization.instance.Localize(c.m_name);
            return (tally.TryGetValue("killedby:" + name, out int k) && k > 0) || (tally.TryGetValue("hurtby:" + name, out int h) && h > me.GetMaxHealth() * 2f);
        }

        /// <summary>One of its blows is a big part of its health (a quarter or more), or it is feared: hit and run against it.</summary>
        public static bool Heavy(Humanoid me, Character c)
        {
            if (c.IsBoss() || Feared(me, c)) return true;
            return HitOf(c) >= me.GetMaxHealth() * 0.25f;
        }

        /// <summary>A blow's harm (not its chop or pickaxe part, which only trees and rocks feel).</summary>
        private static float Harm(HitData.DamageTypes d) => d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;

        /// <summary>A creature's hardest blow (its attacks are its items), with its stars (half again for each).</summary>
        public static float HitOf(Character c)
        {
            if (!(c is Humanoid h)) return 0f;
            float best = h.GetCurrentWeapon() is ItemDrop.ItemData w ? Harm(w.GetDamage()) : 0f;
            foreach (GameObject go in (h.m_defaultItems ?? new GameObject[0]).Concat(h.m_randomWeapon ?? new GameObject[0]))
                if (go != null && go.GetComponent<ItemDrop>() is ItemDrop drop) best = Mathf.Max(best, Harm(drop.m_itemData.GetDamage()));
            return best * (1f + (Mathf.Max(1, c.GetLevel()) - 1) * 0.5f);
        }

        /// <summary>
        /// Clearly too strong for it: at its health and with its weapon it would fall several times over before the creature did (a bear
        /// against a stone axe and 25 health). Then it gets clear instead of trading blows.
        /// </summary>
        public static bool TooStrong(Humanoid me, Character c)
        {
            if (c == null || c.IsDead()) return false;
            float itsHit = HitOf(c);
            if (itsHit <= 0f) return false;
            ItemDrop.ItemData weapon = me.GetCurrentWeapon() ?? Companion.BestMelee(me);
            float myHit = Mathf.Max(3f, weapon != null ? Harm(weapon.GetDamage()) : 3f);
            float blowsToFell = c.GetHealth() / myHit, blowsToFall = me.GetHealth() / itsHit;
            return blowsToFell > blowsToFall * 3f;
        }

        /// <summary>Nothing eaten (it is at its bare health), or just up after a fall: it does not pick fights, and gets clear early.</summary>
        public static bool Careful(BrainState st) => Food.Meals(st.Body).Count == 0 || Time.time - st.WokeAt < 90f;

        // ---- meads -----------------------------------------------------------------------------------------

        /// <summary>A mead it carries whose effect fits, and that is not on it already: drunk. True if it drank.</summary>
        private static bool Drink(Humanoid h, Func<SE_Stats, bool> fits, string what, BrainState st)
        {
            SEMan seman = h.GetSEMan();
            foreach (ItemDrop.ItemData item in h.GetInventory().GetAllItems().ToList())
            {
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable || item.m_shared.m_food > 0f || !(item.m_shared.m_consumeStatusEffect is SE_Stats se) || !fits(se)) continue;
                if (seman.HaveStatusEffect(se.NameHash()) || (!string.IsNullOrEmpty(se.m_category) && seman.HaveStatusEffectCategory(se.m_category))) continue;
                seman.AddStatusEffect(se, true);
                h.m_consumeItemEffects.Create(h.transform.position, Quaternion.identity);
                h.GetInventory().RemoveOneItem(item);
                st.Remember($"drank {what}");
                Activity.Log(h, $"drank {Localization.instance.Localize(item.m_shared.m_name)} ({what})");
                return true;
            }
            return false;
        }

        private static bool Resists(SE_Stats se, HitData.DamageType type) =>
            se.m_mods != null && se.m_mods.Any(m => m.m_type == type && (m.m_modifier == HitData.DamageModifier.Resistant || m.m_modifier == HitData.DamageModifier.VeryResistant || m.m_modifier == HitData.DamageModifier.Immune));

        /// <summary>Once a second in a fight: the mead the moment calls for, if it has one.</summary>
        public static void Meads(BrainState st)
        {
            if (Time.time < st.NextMeadLook) return;
            st.NextMeadLook = Time.time + 1f;
            Humanoid me = st.Body;
            SEMan seman = me.GetSEMan();
            if (Stamina.Get(me) < Stamina.Max(me) * 0.2f && Drink(me, se => se.m_staminaUpFront > 0f || se.m_staminaOverTime > 0f, "a stamina mead", st)) return;
            if (Eitr.Max(me) > 0f && Companion.IsStaff(me.GetCurrentWeapon()) && Eitr.Get(me) < Eitr.Max(me) * 0.2f && Drink(me, se => se.m_eitrUpFront > 0f || se.m_eitrOverTime > 0f, "an eitr mead", st)) return;
            string boss = st.Enemies.FirstOrDefault(e => e != null && e.IsBoss())?.m_name ?? "";
            bool poison = seman.GetStatusEffects().Any(s => s is SE_Poison) || boss.Contains("bonemass");
            bool fire = seman.GetStatusEffects().Any(s => s is SE_Burning) || boss.Contains("yagluth") || boss.Contains("fader");
            bool frost = seman.HaveStatusEffect(SEMan.s_statusEffectFrost) || seman.HaveStatusEffect(SEMan.s_statusEffectFreezing) || boss.Contains("dragon");
            if (poison && Drink(me, se => Resists(se, HitData.DamageType.Poison), "a poison resistance mead", st)) return;
            if (fire && Drink(me, se => Resists(se, HitData.DamageType.Fire), "a fire resistance mead", st)) return;
            if (frost) Drink(me, se => Resists(se, HitData.DamageType.Frost), "a frost resistance mead", st);
        }

        // ---- the dodge -------------------------------------------------------------------------------------

        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static Player _playerPrefab;
        private static Player PlayerPrefab => _playerPrefab != null ? _playerPrefab : _playerPrefab = ZNetScene.instance?.GetPrefab("Player")?.GetComponent<Player>();

        /// <summary>A player's dodge cost (10).</summary>
        public static float DodgeCost => PlayerPrefab != null ? PlayerPrefab.m_dodgeStaminaUsage : 10f;

        /// <summary>
        /// A roll as a player rolls: it turns to the way it goes, the game's roll (the "dodge" animation and its sound), the stamina it costs,
        /// and a moment no hit lands (the game's own dodge window, which every game checks: melee, area attacks, arrows).
        /// </summary>
        public static void Roll(BrainState st, Vector3 dir)
        {
            Humanoid me = st.Body;
            Stamina.Use(me, DodgeCost);
            me.transform.rotation = Quaternion.LookRotation(dir);
            Anim(me)?.SetTrigger("dodge");
            PlayerPrefab?.m_dodgeEffects.Create(me.transform.position, Quaternion.identity, me.transform);
            Companion.Zdo(me)?.Set(ZDOVars.s_dodgeinv, true);
            st.InvulnUntil = Time.time + 0.4f;
            st.FightDodges++;
            Journal.Count(me, "dodges");
        }

        /// <summary>Every frame on the game running it: the roll's moment over.</summary>
        public static void Tick(BrainState st)
        {
            if (st.InvulnUntil > 0f && Time.time >= st.InvulnUntil) { st.InvulnUntil = 0f; Companion.Zdo(st.Body)?.Set(ZDOVars.s_dodgeinv, false); }
        }
    }

    /// <summary>A companion mid-roll cannot be hit (the game's own check, as for a rolling player), on every game.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.IsDodgeInvincible))]
    internal static class Character_IsDodgeInvincible_Companion
    {
        private static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || !(__instance is Humanoid h) || !Companion.Is(h)) return;
            // Its own game: the roll's live timer (a flag left set by a brain that stopped mid-roll must not make it unhittable); others: the save.
            __result = h.GetComponent<ZNetView>().IsOwner() ? Brain.Get(h).InvulnUntil > Time.time : Companion.Zdo(h)?.GetBool(ZDOVars.s_dodgeinv, false) ?? false;
        }
    }

    /// <summary>What you hit (for it to join in), on your own game.</summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage_YourTarget
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            if (hit == null || __instance.IsPlayer() || Companion.Is(__instance)) return;
            if (hit.GetAttacker() is Player p && p == Player.m_localPlayer) { Tactics.YourTarget = __instance; Tactics.YourTargetAt = Time.time; }
        }
    }
}
