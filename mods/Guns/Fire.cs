using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Firing. The game calls Humanoid.StartAttack every physics step while the attack button is held (and once for a tap), and refuses
    /// while an attack animation is running, which would cap a crossbow-style weapon at about one shot a second. For a gun this takes the
    /// call over: it fires a round when the rate and the magazine allow, from the spawn point the Arbalest's own attack uses, with the spread
    /// the trigger has built up, and answers "started" so the game clears its queued click (a tap would otherwise go on firing for half a
    /// second). The shot itself is the game's: a Projectile with a HitData made the way Attack.FireProjectileBurst makes it.
    /// An automatic gun fires while the button is held; any other fires once for each pull. A rotary gun first has to spin up.
    /// </summary>
    internal static class Fire
    {
        private static float _nextShot, _lastShot, _lastShotBefore, _heat;   // heat: 0 = first-shot spread, 1 = fully climbed
        private static bool _released = true;               // the trigger has been let go since the last shot
        private static bool _spooled;                       // the motor engaging sound has played for this pull
        private static bool _second;                        // a pair of guns: the next shot is the left one
        private static GunDef _last;
        private static float _casingAt, _cycleAt;   // the action being worked (a bolt, pump, lever) sounds a moment after the shot
        private static float _unusedCasing;                     // brass of a gun that is worked by hand comes out when it is worked
        private static bool _casingSecond;

        /// <summary>How fast a rotary gun's barrels turn, 0 to 1.</summary>
        internal static float Spin { get; private set; }
        internal static float SpinAngle { get; private set; }
        internal static float LastShot => _lastShot;
        internal static bool LastWasSecond { get; private set; }

        /// <summary>The attack button is down (and no window has the keyboard).</summary>
        internal static bool TriggerHeld(Player p) => Aim.InputAllowed(p) && (ZInput.GetButton("Attack") || ZInput.GetButton("JoyAttack"));

        /// <summary>Every frame from the plugin: the trigger being let go, a rotary gun spinning, brass coming out.</summary>
        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            if (d != _last)
            {
                _last = d;
                Fx.StopBurst(p);
                _heat = 0f; _released = true; Spin = 0f; _casingAt = 0f; _cycleAt = 0f; _second = true;
                if (d != null && p != null) Fx.Click(Sound.Click.Draw, p.transform.position + Vector3.up);
                Fx.Recoil = 0f;
            }
            if (d == null) { Fx.Whirr(0f); Fx.StopBurst(p); return; }
            if (Fx.HasBurst(d)) Fx.Fire(p, d, !Ammo.Reloading && Time.time - _lastShot < 2.2f / d.RateNow);
            bool held = TriggerHeld(p);
            if (!held) _released = true;
            if (Aim.InputAllowed(p) && (ZInput.GetButtonDown("SecondaryAttack") || ZInput.GetButtonDown("JoySecondaryAttack")) && CanShoot(p)) Ammo.Begin(p, item, d);   // reload (the game's own attack input also asks, in StartAttack)
            if (d.SpinUp > 0f)
            {
                bool want = held && !Ammo.Reloading;   // (spins up even when empty, so the trigger gets as far as the empty click and the reload)
                if (!held) _spooled = false;
                if (want && !_spooled) { _spooled = true; if (Spin < 0.6f) Fx.Click(Sound.Click.Spool, p.transform.position + Vector3.up); }   // the motor engages on every new pull (not when it is still spinning fast)
                Spin = Mathf.Clamp01(Spin + (want ? 1f / d.SpinUp : -1f / (d.SpinUp * 2.4f)) * Time.deltaTime);   // (it takes a few seconds to wind down)
                SpinAngle = (SpinAngle + Spin * 1500f * Time.deltaTime) % 360f;
                Fx.Whirr(Spin);
            }
            Fx.Recoil = Mathf.Max(0f, Fx.Recoil - Time.deltaTime * 7f);
            if (_cycleAt > 0f && Time.time >= _cycleAt)
            {
                _cycleAt = 0f;
                if (d.CycleClick.HasValue) Fx.Click(d.CycleClick.Value, p.transform.position + Vector3.up);
            }
            if (_casingAt > 0f && Time.time >= _casingAt)
            {
                _casingAt = 0f;
                Fx.Casing(p, d, _casingSecond);
            }
        }

        /// <summary>Spread in degrees right now, after the barrel has had time to settle.</summary>
        internal static float Spread(GunDef d, float now)
        {
            float settled = Mathf.Max(0f, _heat - Mathf.Max(0f, now - _lastShot) * d.Recovery);
            float spread = Mathf.Lerp(d.MinSpread, d.MaxSpread, settled) * Plugin.SpreadScale.Value;
            return Aim.Active ? spread * d.AimSpread : spread;
        }

        /// <summary>True when the player's state lets them shoot at all.</summary>
        private static bool CanShoot(Player p) =>
            !(p.IsDead() || p.InDodge() || p.IsStaggering() || p.IsKnockedBack() || p.InMinorAction() || p.IsTeleporting() || p.IsAttached() ||
              p.InAttack());   // (blocking is how a gun aims: Aim.cs)

        /// <summary>The body is turned towards where the camera looks (it turns to follow, a moment after a quick flick): no shooting out of the side or the back.</summary>
        private static bool FacingAim(Player p)
        {
            Vector3 look = p.GetLookDir();
            look.y = 0f;
            return look.sqrMagnitude < 0.0001f || Vector3.Angle(p.transform.forward, look) < 55f;
        }

        /// <summary>The reload button (the secondary attack). Returns what StartAttack should answer.</summary>
        internal static bool TryReload(Player p, ItemDrop.ItemData weapon, GunDef d)
        {
            if (CanShoot(p)) Ammo.Begin(p, weapon, d);
            return true;
        }

        /// <summary>One call from the game's attack input. Returns what StartAttack should answer.</summary>
        internal static bool Try(Player p, ItemDrop.ItemData weapon, GunDef d)
        {
            if (!CanShoot(p) || !FacingAim(p)) return false;
            float now = Time.time;
            if (Ammo.Reloading)
            {
                if (d.PerRound && Ammo.Rounds(weapon, d) > 0) Ammo.Cancel();   // a round or two in: fire, the rest can wait
                else return true;
            }
            if (now < _nextShot) return true;   // between rounds: "started", so nothing is left queued
            if (!d.Auto && !_released) return true;

            if (Ammo.Rounds(weapon, d) <= 0)
            {
                if (_released)
                {
                    _released = false;
                    Fx.Click(Sound.Click.Dry, p.transform.position + Vector3.up);
                    Ammo.Begin(p, weapon, d);   // the trigger on an empty gun reloads it
                }
                return true;
            }

            if (d.SpinUp > 0f && Spin < 0.9f) return true;   // a rotary gun fires only once its barrels are up to speed

            float stamina = Plugin.ShotStamina.Value;
            if (stamina > 0f)
            {
                if (!p.HaveStamina(stamina)) return false;
                p.UseStamina(stamina);
            }

            float interval = d.Interval;
            float basis = now - _nextShot <= Time.fixedDeltaTime ? _nextShot : now;   // (on schedule: keeps the average rate when a step lands late)
            _nextShot = basis + interval;
            float spread = Spread(d, now);
            _heat = Mathf.Min(1f, Mathf.Max(0f, _heat - Mathf.Max(0f, now - _lastShot) * d.Recovery) + d.PerShot);
            _lastShotBefore = _lastShot;
            _lastShot = now;
            _released = false;

            Ammo.Spend(weapon, d);
            if (now - _lastShotBefore > 1f) _second = true;   // a pair starts with the left, and again after a pause
            bool second = d.Dual && _second;
            if (d.Dual) _second = !_second;
            LastWasSecond = second;
            Shoot(p, weapon, d, spread, second);
            Share.SendShot(p, d, second);   // (the other players hear and see it too)
            if (d.CycleClick.HasValue) _cycleAt = now + d.Cycle * 0.3f;
            if (d.Cycle >= 0.4f) { _casingAt = now + d.Cycle * 0.4f; _casingSecond = second; }   // (worked by hand: the brass comes out as it is worked)
            else Fx.Casing(p, d, second);
            return true;
        }

        /// <summary>
        /// The direction from the muzzle to whatever is under the crosshair. The game aims a shot along the camera's direction from the
        /// player's chest, which lands it below the crosshair up close (the camera sits higher and behind); this finds the spot the camera
        /// looks at (skipping the player's own body) and aims there, then lifts the aim a little for the round's drop over that distance.
        /// </summary>
        private static Vector3 AimAtCrosshair(Player p, GunDef d, Vector3 spawn)
        {
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return p.GetAimDir(spawn);
            const float far = 250f;
            Vector3 target = cam.position + cam.forward * far;
            float nearest = far;
            foreach (RaycastHit h in Physics.RaycastAll(cam.position, cam.forward, far, Mask, QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= nearest) continue;
                Character c = h.collider.GetComponentInParent<Character>();
                if (c == p) continue;   // (the camera sits behind the player: its own body is not the target)
                nearest = h.distance;
                target = h.point;
            }
            Vector3 to = target - spawn;
            float distance = to.magnitude;
            if (distance < 2f || Vector3.Dot(to, cam.forward) <= 0f) return cam.forward;   // (something right in front of the muzzle: the camera's own line)
            Vector3 dir = to / distance;
            float lift = Mathf.Atan(d.Gravity * distance / (2f * d.Speed * d.Speed)) * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(-lift, Vector3.Cross(Vector3.up, dir)) * dir;
        }

        private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "vehicle");

        private static HitData MakeHit(Player p, ItemDrop.ItemData weapon, GunDef d)
        {
            float skill = p.GetRandomSkillFactor(weapon.m_shared.m_skillType);
            var hit = new HitData
            {
                m_toolTier = (short)weapon.m_shared.m_toolTier,
                m_pushForce = weapon.m_shared.m_attackForce * skill,
                m_backstabBonus = weapon.m_shared.m_backstabBonus,
                m_staggerMultiplier = d.Stagger,
                m_blockable = weapon.m_shared.m_blockable,
                m_dodgeable = weapon.m_shared.m_dodgeable,
                m_skill = weapon.m_shared.m_skillType,
                m_skillRaiseAmount = Mathf.Clamp(0.6f * d.Interval, 0.02f, 1f) / d.Pellets,   // (the game's is 1 a shot at a shot or so a second: about the same a second however fast it fires)
                m_skillLevel = p.GetSkillLevel(weapon.m_shared.m_skillType),
                m_itemLevel = (short)weapon.m_quality,
                m_hitType = HitData.HitType.PlayerHit,
                m_variant = weapon.m_shared.m_hitVariant,
            };
            hit.m_damage.Add(weapon.GetDamage());
            hit.m_damage.Modify(skill);   // (whole hit: ApplyModifier leaves untyped damage alone)
            hit.SetAttacker(p);
            p.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hit);
            return hit;
        }

        private static void Shoot(Player p, ItemDrop.ItemData weapon, GunDef d, float spread, bool second)
        {
            GameObject bullet = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(d.BulletId) : null;
            if (bullet == null) return;

            Attack a = weapon.m_shared.m_attack;   // (the Arbalest's: where its rounds leave the player)
            Transform t = p.transform;
            Vector3 spawn = t.position + t.up * a.m_attackHeight + t.forward * a.m_attackRange + t.right * a.m_attackOffset;
            Transform muzzle = Fx.Muzzle(p, second);
            if (muzzle != null) spawn = muzzle.position;   // the round leaves the barrel in the player's hand
            Vector3 aim = AimAtCrosshair(p, d, spawn);

            weapon.m_shared.m_triggerEffect.Create(spawn, Quaternion.LookRotation(aim), null, 1f, -1, p.GetZDOID());   // sound and flash (Fx.cs)
            Fx.Kick(p, d);

            for (int i = 0; i < d.Pellets; i++)
            {
                Vector2 off = Random.insideUnitCircle * spread;
                Vector3 dir = Quaternion.LookRotation(aim) * Quaternion.Euler(-off.y, off.x, 0f) * Vector3.forward;
                GameObject shot = Object.Instantiate(bullet, spawn, Quaternion.LookRotation(dir));
                shot.GetComponent<IProjectile>()?.Setup(p, dir * d.Speed, 15f, MakeHit(p, weapon, d), weapon, null);
            }
            p.AddNoise(d.Noise);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class Humanoid_StartAttack
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal || __instance != Player.m_localPlayer) return true;
            ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
            GunDef d = Arsenal.Of(weapon);
            if (d == null) return true;
            __result = secondaryAttack ? Fire.TryReload((Player)__instance, weapon, d) : Fire.Try((Player)__instance, weapon, d);
            return false;   // (skips only the game's own attack: other mods' prefixes still run)
        }
    }

    // A heavy gun slows whoever carries it.
    [HarmonyPatch(typeof(Player), nameof(Player.GetEquipmentMovementModifier))]
    internal static class Player_GetEquipmentMovementModifier_Gun
    {
        private static void Postfix(Player __instance, ref float __result)
        {
            if (__instance != Player.m_localPlayer) return;
            GunDef d = Arsenal.Of(__instance.GetCurrentWeapon());
            if (d != null && d.MoveSpeed != 0f) __result += d.MoveSpeed;
        }
    }
}
