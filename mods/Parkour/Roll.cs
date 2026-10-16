using System;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// The landing roll (and the landing slide). Press crouch just before a low landing (4 m, a setting) while moving forward and you
    /// land straight in a slide (Moves); before a higher one, landing (within the roll window: longer with the skill) and you land in the game's own
    /// dodge roll, which every other player sees, paying its dodge stamina the game's way. A roll takes no fall damage up to the safe height
    /// and half (a setting) of the rest above it. From Parkour level 50 (a setting) a hard landing while moving forward rolls by itself.
    ///
    /// The game hurts a fall in Character.UpdateGroundContact, a moment before its movement step (where Moves runs), so whether this
    /// landing rolls is decided once, by whichever of the two comes first: the fall damage (SEMan.ModifyFallDamage, below) or the landing
    /// seen in Moves' step. A press that comes too early, or a landing without the stamina, does nothing.
    /// </summary>
    internal static class Roll
    {
        private static float _pressAt = -10f, _decidedAt = -10f, _airTop;
        private static bool _decision, _wasGrounded = true, _slideLanding;
        private static Vector3 _airVel;
        private static string _how = "", _fall = "";

        private static readonly Action<Player, Vector3> Dodge = AccessTools.MethodDelegate<Action<Player, Vector3>>(AccessTools.Method(typeof(Player), "Dodge"));
        private static readonly Func<Player, float> DodgeCost = AccessTools.MethodDelegate<Func<Player, float>>(AccessTools.Method(typeof(Player), "GetDodgeStaminaUse"));
        private static readonly AccessTools.FieldRef<SEMan, Character> Owner = AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        public static void Reset()
        {
            _pressAt = _decidedAt = -10f;
            _decision = false;
            _wasGrounded = true;
            _how = _fall = "";
        }

        public static float Window(Player p) => Mathf.Lerp(Plugin.RollWindowMin.Value, Plugin.RollWindowMax.Value, Skill.Factor(p));
        public static float SafeHeight(Player p) => Mathf.Lerp(Plugin.RollSafeMin.Value, Plugin.RollSafeMax.Value, Skill.Factor(p));
        public static bool AutoUnlocked(Player p) => Plugin.AutoRollLevel.Value <= 100 && Skill.Level(p) >= Plugin.AutoRollLevel.Value;

        /// <summary>Crouch in the air: the roll is wanted for a landing soon (the game doesn't crouch for it).</summary>
        public static bool TakesCrouch(Player p)
        {
            if (!Plugin.Enabled.Value || !(Plugin.LandingRoll.Value || Plugin.Slide.Value) || p.IsOnGround() || Moves.Why(p) != null) return false;
            _pressAt = Time.time;
            return true;
        }

        /// <summary>Every physics step (from Moves): remember the fall, and roll on landing.</summary>
        public static void Step(Player p, Rigidbody body, bool grounded, string why)
        {
            if (!grounded)
            {
                _airTop = Moves.MaxAirAltitude(p);   // (the game's measure of the fall, which the wall moves keep resetting)
                _airVel = body.linearVelocity;
            }
            else if (!_wasGrounded) Landed(p, why);
            _wasGrounded = grounded;
        }

        private static void Landed(Player p, string why)
        {
            float height = _airTop - p.transform.position.y;
            if (!Decide(p, height, why))
            {
                Vector3 flat = new Vector3(_airVel.x, 0f, _airVel.z);
                if (_slideLanding) Moves.SlideFromLanding(p, flat);   // a low landing with crouch: straight into a slide
                else if (Plugin.Enabled.Value && Plugin.Slide.Value && why == null && height > 0.6f && height <= Plugin.LandingSlideMax.Value && Forward(p, flat, p.m_walkSpeed))
                    Moves.Hint(p, "parkour_hint_landing_slide");
                _slideLanding = false;
                return;
            }
            Vector3 dir = new Vector3(_airVel.x, 0f, _airVel.z);
            if (dir.magnitude < 1f) dir = Moves.MoveDir(p);
            if (dir.sqrMagnitude < 0.01f) dir = p.transform.forward;
            dir.y = 0f;
            Dodge?.Invoke(p, dir.normalized);   // the game's dodge roll, in the direction of travel (it pays its stamina as it starts)
            Feel.RollLanding(p, height);
            Moves.Raise(p, 1f);
            Moves.Note(new Moves.Record
            {
                Move = "Roll", Started = Time.time, From = p.transform.position, To = p.transform.position, Rise = -Mathf.Max(0f, height),
                Ended = $"{_how}, fell {Mathf.Max(0f, height):0.0} m{_fall}", Stamina = DodgeCost != null ? DodgeCost(p) : 0f,
            });
            _fall = "";
        }

        /// <summary>Does this landing roll? Decided once per landing.</summary>
        private static bool Decide(Player p, float height, string why)
        {
            if (_decidedAt == Time.time) return _decision;   // (the same physics step)
            _decidedAt = Time.time;
            _decision = false;
            _slideLanding = false;
            if (!Plugin.Enabled.Value || why != null || p.InDodge() || Dodge == null) return false;
            bool crouched = Time.time - _pressAt <= Window(p);
            Vector3 flat = new Vector3(_airVel.x, 0f, _airVel.z);
            // crouch before a low landing: a slide when moving forward faster than walking (the landing is the slide's), else nothing
            if (crouched && height <= Plugin.LandingSlideMax.Value)
            {
                _pressAt = -10f;
                _slideLanding = Plugin.Slide.Value && Forward(p, flat, p.m_walkSpeed);
                return false;
            }
            bool pressed = crouched && Plugin.LandingRoll.Value;
            bool forward = Forward(p, flat, 2f);
            // (the game's falls hurt from 4 m; a landing from a Webslinger web doesn't hurt at all, and rolling would only stop the player dead)
            bool auto = !pressed && Plugin.AutoRoll.Value && AutoUnlocked(p) && height > 4f && forward && Compat.WebMove() == 0 && !Compat.WebLately(1f);
            if (!pressed && !auto) return false;
            float cost = DodgeCost != null ? DodgeCost(p) : p.m_dodgeStaminaUsage;
            if (!p.HaveStamina(cost))
            {
                Moves.LastRefusal = "not enough stamina to roll";
                if (Hud.instance != null) Hud.instance.StaminaBarEmptyFlash();
                return false;
            }
            _pressAt = -10f;
            _how = pressed ? "crouch" : "auto";
            return _decision = true;
        }

        /// <summary>Moving along the way the body faces, faster than this.</summary>
        private static bool Forward(Player p, Vector3 flat, float faster) => flat.magnitude > faster && Vector3.Dot(flat.normalized, p.transform.forward) > 0.5f;

        /// <summary>The fall damage of a rolled landing: none up to the safe height, a share of what the rest would do.</summary>
        private static void OnFall(Player p, float baseDamage, ref float damage)
        {
            float height = Moves.MaxAirAltitude(p) - p.transform.position.y;   // (not yet reset: the game resets it after the damage)
            if (!Decide(p, height, Moves.Why(p)))
            {
                if (damage > 0f && Plugin.LandingRoll.Value && Plugin.Enabled.Value) Moves.Hint(p, "parkour_hint_roll");
                return;
            }
            // the game's fall damage grows by 100/16 per metre over 4 m; a roll counts only the metres over the safe height, at a share
            float rolled = Mathf.Clamp01((height - SafeHeight(p)) / 16f) * 100f * (Plugin.RollDamageShare.Value / 100f);
            float was = damage;
            damage = baseDamage > 0f ? damage * Mathf.Clamp01(rolled / baseDamage) : 0f;
            _fall = $", damage {was:0} -> {damage:0}";
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
        private static class SEMan_ModifyFallDamage
        {
            private static void Postfix(SEMan __instance, float baseDamage, ref float damage)
            {
                if (!(Owner(__instance) is Player p) || !ReferenceEquals(p, Player.m_localPlayer)) return;
                try { OnFall(p, baseDamage, ref damage); }
                catch (Exception e) { Plugin.Log.LogError("Parkour roll: " + e); }
            }
        }
    }
}
