using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// A boss creature on this game: what the fight's record says about it this frame (BossShow sets it), and what follows from that on
    /// every game (its size, glow, aura, shield bubble, enraged speed) and on the game that owns it (held still, its leaps and dashes,
    /// its guard against hits, its enraged blows). Kept in a table here, never as a component on the creature: the creature is a saved
    /// object, and a component of ours on it would outlive a reload of the mod. The look's objects are plain children, found by name
    /// again after a reload. A body no fight has spoken for in 2 s (its dungeon not built here, the fight over) is an ordinary creature.
    /// </summary>
    internal class BossBody
    {
        public const string LookName = "quaddungeon_bosslook", ShieldName = "quaddungeon_bossshield";
        private static readonly Dictionary<Character, BossBody> Bodies = new Dictionary<Character, BossBody>();
        private static readonly Dictionary<BaseAI, BossBody> ByAi = new Dictionary<BaseAI, BossBody>();

        public Character C;
        public Rigidbody Body;
        public string Tag;              // "<dungeon id>|<mech key>" (MechSet.EncounterTag's value)
        public float Scale = 1f, BaseSpeed, BaseRun, BaseWalk;
        public long Fight;              // its fight's stamp (MechSet.Stamp; 0: from before the stamp)
        // this frame's word from the fight (BossShow)
        public float Said = -10f;       // Time.time it was last said
        public bool Held, Shielded, Enraged;
        public float Surge;             // Time.time until which its light surges (a second wind)
        public float Guard = 1f, Enrage = 1f;
        public double LeapAt;
        public Vector3 LeapFrom, LeapTo;
        public float LeapDur, LeapArc;
        public BossLook Look;
        private Light _light;
        private Collider[] _own;
        private readonly Dictionary<Collider, bool> _ignoring = new Dictionary<Collider, bool>();
        private bool _lookMade, _auraMade;
        private GameObject _shield;

        public bool Fresh => Time.time - Said < 2f;

        // ---- the table

        public static BossBody Of(Character c) => c != null && Bodies.TryGetValue(c, out BossBody b) ? b : null;

        public static IEnumerable<BossBody> All => Bodies.Values;

        /// <summary>A character whose ZDO marks it as one of our bosses (spawned, woken in its zone, or found after a reload): into the table,
        /// at its size.</summary>
        public static BossBody Apply(Character c, ZDO z)
        {
            if (c == null || z == null || string.IsNullOrEmpty(z.GetString(MechSet.BossTagHash))) return null;
            if (!Bodies.TryGetValue(c, out BossBody b))
            {
                Sweep();
                b = new BossBody { C = c, Body = c.GetComponent<Rigidbody>(), Tag = z.GetString(MechSet.EncounterTagHash) ?? "" };
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(z.GetPrefab()) : null;
                Character pc = prefab != null ? prefab.GetComponent<Character>() : null;
                b.BaseSpeed = pc != null ? pc.m_speed : c.m_speed;
                b.BaseRun = pc != null ? pc.m_runSpeed : c.m_runSpeed;
                b.BaseWalk = pc != null ? pc.m_walkSpeed : c.m_walkSpeed;
                Vector3 baseScale = prefab != null ? prefab.transform.localScale : Vector3.one;
                b.Scale = Mathf.Clamp(z.GetFloat(MechSet.BossScaleHash, 1f), 0.5f, 3f);
                b.Fight = z.GetLong(MechSet.BossFightHash, 0L);
                c.transform.localScale = baseScale * b.Scale;
                Bodies[c] = b;
                BaseAI ai = c.GetComponent<BaseAI>();
                if (ai != null) ByAi[ai] = b;
            }
            return b;
        }

        private static void Sweep()
        {
            foreach (Character dead in Bodies.Keys.Where(k => k == null).ToList()) Bodies.Remove(dead);
            foreach (BaseAI dead in ByAi.Keys.Where(k => k == null).ToList()) ByAi.Remove(dead);
        }

        /// <summary>Every boss loaded here (after a reload the table is empty, and Awake won't run again for them).</summary>
        public static void Find()
        {
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c is Player || Bodies.ContainsKey(c)) continue;
                ZNetView v = c.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                ZDO z = v.GetZDO();
                if (string.IsNullOrEmpty(z.GetString(MechSet.BossTagHash))) continue;
                MechSet.MakeBoss(c, z);
                Apply(c, z);
            }
        }

        // ---- every game, every frame: its look

        public void Show()
        {
            if (C == null) return;
            bool fresh = Fresh;
            if (!_lookMade)
            {
                // a reload's look and bubble, made by the last copy: made again by this one
                _lookMade = true;
                foreach (string name in new[] { LookName, ShieldName })
                {
                    Transform old = C.transform.Find(name);
                    if (old != null) Object.Destroy(old.gameObject);
                }
            }
            if (Look != null && !_auraMade)
            {
                _auraMade = true;
                var look = new GameObject(LookName);
                look.transform.SetParent(C.transform, false);
                look.transform.localPosition = Vector3.up * 1.2f;
                if (Look.Range > 0f)
                {
                    _light = look.AddComponent<Light>();
                    _light.type = LightType.Point;
                    _light.range = Look.Range;
                    _light.intensity = 1.6f;
                    _light.color = Look.Light;
                    _light.shadows = LightShadows.None;
                }
                if (!string.IsNullOrEmpty(Look.Aura)) BossMarks.Attach(Look.Aura, look.transform, Vector3.zero, 1f);
            }
            if (_light != null && Look != null)
            {
                _light.color = fresh && Enraged ? Look.Enraged : Look.Light;
                float surge = Mathf.Clamp01((Surge - Time.time) / 2.5f);
                _light.intensity = 1.6f + 4f * surge;
                _light.range = Look.Range * (1f + surge);
            }
            bool shield = fresh && Shielded;
            if (shield != (_shield != null))
            {
                if (_shield != null) { Object.Destroy(_shield); _shield = null; }
                else
                {
                    _shield = BossMarks.Attach("vfx_StaffShield", C.transform, Vector3.zero, 1.6f);
                    if (_shield != null) _shield.name = ShieldName;
                }
            }
            float k = fresh ? Mathf.Clamp(Enrage, 1f, 2f) : 1f;
            if (Math.Abs(C.m_runSpeed - BaseRun * k) > 0.01f) { C.m_speed = BaseSpeed * k; C.m_runSpeed = BaseRun * k; C.m_walkSpeed = BaseWalk * k; }
        }

        /// <summary>Its body passes through these colliders (a hammer coming down on it, or down with it under it) or stops at them again:
        /// it's never caught inside a weakTo mechanism's block. On every game (the one that owns it moves it).</summary>
        public void PassThrough(List<Collider> colliders, bool pass)
        {
            if (C == null) return;
            if (_own == null) _own = C.GetComponentsInChildren<Collider>(true).Where(c => c != null && !c.isTrigger).ToArray();
            foreach (Collider col in colliders)
            {
                if (col == null || (_ignoring.TryGetValue(col, out bool now) && now == pass)) continue;
                _ignoring[col] = pass;
                foreach (Collider mine in _own) if (mine != null) Physics.IgnoreCollision(mine, col, pass);
            }
        }

        /// <summary>Where a leap or a dash has it at this moment, or null.</summary>
        public Vector3? Moving(double now)
        {
            if (!Fresh || LeapDur <= 0f || now < LeapAt || now > LeapAt + LeapDur + 0.15) return null;
            float s = Mathf.Clamp01((float)((now - LeapAt) / LeapDur));
            return Vector3.Lerp(LeapFrom, LeapTo, s) + Vector3.up * (4f * LeapArc * s * (1f - s));
        }

        // ---- the patches

        // On every game: a character marked as one of our bosses is the game's boss (its bar) with our name, at its size, in the table.
        [HarmonyPatch(typeof(Character), "Awake")]
        internal static class Character_Awake
        {
            private static void Postfix(Character __instance)
            {
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) return;
                MechSet.MakeBoss(__instance, v.GetZDO());
                Apply(__instance, v.GetZDO());
            }
        }

        // On the game that owns the hit's victim: our boss takes only its share of each hit (its toughness: boss "health"), none or a share
        // while its shield or armour is up (but the toolkit's own blows, WeakMark, go through); an enraged boss hits harder.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        internal static class Character_RPC_Damage
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                if (hit == null) return true;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) return true;
                BossBody me = Of(__instance);
                if (me != null && hit.m_toolTier != MechSet.WeakMark && me.Fresh)
                {
                    if (me.Guard <= 0f) return false;
                    if (me.Guard < 1f) hit.m_damage.Modify(me.Guard);
                }
                // (DamageTypes.Modify, not HitData.ApplyModifier: that one leaves m_damage, the untyped part, alone, so the toolkit's own
                // blows, which are all m_damage, took the boss's whole toughness times their share: a shield's 8% was 48%)
                float x = MechSet.BossToughness(v.GetZDO());
                if (x > 1f) hit.m_damage.Modify(1f / x);
                if (me == null && hit.HaveAttacker() && Of(hit.GetAttacker()) is BossBody boss && boss.Fresh && boss.Enrage > 1f)
                    hit.m_damage.Modify(boss.Enrage);
                return true;
            }
        }

        // On the game that strikes: a hit on a boss whose shield is up (or that can't be hurt yet) clangs off it, here, and goes nowhere.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        internal static class Character_Damage
        {
            private static bool Prefix(Character __instance, HitData hit)
            {
                BossBody me = Of(__instance);
                if (me == null || hit == null || hit.m_toolTier == MechSet.WeakMark || !me.Fresh || me.Guard > 0f) return true;
                if (DamageText.instance != null) DamageText.instance.ShowText(DamageText.TextType.Immune, hit.m_point, 0f);
                MechSet.PlayAt("fx_StaffShield_Hit", hit.m_point);
                return false;
            }
        }

        // On the game that owns it: held still (its intro, a windup, channelling a shield, a leap) it neither moves nor attacks.
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        internal static class MonsterAI_UpdateAI
        {
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                if (!ByAi.TryGetValue(__instance, out BossBody b) || !b.Fresh || !b.Held || b.C == null) return true;
                b.C.SetMoveDir(Vector3.zero);
                __result = false;
                return false;
            }
        }

        // On the game that owns it: a leap (an arc) or a dash (along the floor) moves it, step by step, from where it was to where it lands.
        [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
        internal static class Character_CustomFixedUpdate
        {
            private static void Postfix(Character __instance)
            {
                BossBody b = Of(__instance);
                if (b?.Body == null) return;
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v == null || !v.IsValid() || !v.IsOwner()) return;
                Vector3? at = b.Moving(MechClock.Now);
                if (at == null) return;
                Vector3 flat = Vector3.ProjectOnPlane(b.LeapTo - b.LeapFrom, Vector3.up);
                b.Body.position = at.Value;
                b.Body.linearVelocity = Vector3.zero;
                __instance.transform.position = at.Value;
                if (flat.sqrMagnitude > 0.01f) b.Body.rotation = Quaternion.LookRotation(flat.normalized);
            }
        }
    }
}
