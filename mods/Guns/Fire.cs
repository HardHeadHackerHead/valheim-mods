using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Firing. The game calls Humanoid.StartAttack every physics step while the attack button is held (and once for a tap), and refuses
    /// while an attack animation is running, which would cap a crossbow-style weapon at about one shot a second. For a gun this takes the
    /// call over: it fires a round when the rate allows, from the spawn point the Arbalest's own attack uses, with the spread the trigger
    /// has built up, and answers "started" so the game clears its queued click (a tap would otherwise go on firing for half a second).
    /// The shot itself is the game's: a Projectile with a HitData made the way Attack.FireProjectileBurst makes it.
    /// </summary>
    internal static class Fire
    {
        private static float _nextShot, _lastShot, _heat;   // heat: 0 = first-shot spread, 1 = fully climbed

        /// <summary>Spread in degrees right now, after the barrel has had time to settle.</summary>
        internal static float Spread(float now)
        {
            float settled = Mathf.Max(0f, _heat - Mathf.Max(0f, now - _lastShot) * Plugin.SpreadRecovery.Value);
            float spread = Mathf.Lerp(Plugin.MinSpread.Value, Plugin.MaxSpread.Value, settled);
            return Aim.Active ? spread * Plugin.AimSpread.Value : spread;
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

        /// <summary>One call from the game's attack input. Returns what StartAttack should answer.</summary>
        internal static bool Try(Player p, ItemDrop.ItemData weapon)
        {
            if (!CanShoot(p) || !FacingAim(p)) return false;
            float now = Time.time;
            if (now < _nextShot) return true;   // between rounds: "started", so nothing is left queued

            float stamina = Plugin.ShotStamina.Value;
            if (stamina > 0f)
            {
                if (!p.HaveStamina(stamina)) return false;
                p.UseStamina(stamina);
            }

            float interval = 1f / Mathf.Max(1f, Plugin.RoundsPerSecond.Value);
            _nextShot = Mathf.Max(_nextShot + interval, now - Time.fixedDeltaTime);   // (keeps the average rate when a step lands late)
            float spread = Spread(now);
            _heat = Mathf.Min(1f, Mathf.Max(0f, _heat - Mathf.Max(0f, now - _lastShot) * Plugin.SpreadRecovery.Value) + Plugin.SpreadPerShot.Value);
            _lastShot = now;

            Shoot(p, weapon, spread);
            return true;
        }

        /// <summary>
        /// The direction from the muzzle to whatever is under the crosshair. The game aims a shot along the camera's direction from the
        /// player's chest, which lands it below the crosshair up close (the camera sits higher and behind); this finds the spot the camera
        /// looks at (skipping the player's own body) and aims there, then lifts the aim a little for the round's drop over that distance.
        /// </summary>
        private static Vector3 AimAtCrosshair(Player p, Vector3 spawn)
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
            float lift = Mathf.Atan(MachineGun.Gravity * distance / (2f * MachineGun.BulletSpeed * MachineGun.BulletSpeed)) * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(-lift, Vector3.Cross(Vector3.up, dir)) * dir;
        }

        private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "vehicle");

        private static void Shoot(Player p, ItemDrop.ItemData weapon, float spread)
        {
            GameObject bullet = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(MachineGun.BulletPrefab) : null;
            if (bullet == null) return;

            Attack a = weapon.m_shared.m_attack;   // (the Arbalest's: where its rounds leave the player)
            Transform t = p.transform;
            Vector3 spawn = t.position + t.up * a.m_attackHeight + t.forward * a.m_attackRange + t.right * a.m_attackOffset;
            Transform muzzle = Fx.Muzzle(p);
            if (muzzle != null) spawn = muzzle.position;   // the round leaves the barrel in the player's hand
            Vector3 aim = AimAtCrosshair(p, spawn);
            Vector2 off = Random.insideUnitCircle * spread;
            Vector3 dir = Quaternion.LookRotation(aim) * Quaternion.Euler(-off.y, off.x, 0f) * Vector3.forward;

            weapon.m_shared.m_triggerEffect.Create(spawn, Quaternion.LookRotation(aim), null, 1f, -1, p.GetZDOID());   // sound and flash (Fx.cs)
            Fx.Casing(p);
            Fx.Kick(p);

            float skill = p.GetRandomSkillFactor(weapon.m_shared.m_skillType);
            var hit = new HitData
            {
                m_toolTier = (short)weapon.m_shared.m_toolTier,
                m_pushForce = weapon.m_shared.m_attackForce * skill,
                m_backstabBonus = weapon.m_shared.m_backstabBonus,
                m_staggerMultiplier = 0.2f,
                m_blockable = weapon.m_shared.m_blockable,
                m_dodgeable = weapon.m_shared.m_dodgeable,
                m_skill = weapon.m_shared.m_skillType,
                m_skillRaiseAmount = 0.05f,   // (the game's is 1 a shot at a few a second; this fires ten)
                m_skillLevel = p.GetSkillLevel(weapon.m_shared.m_skillType),
                m_itemLevel = (short)weapon.m_quality,
                m_hitType = HitData.HitType.PlayerHit,
                m_variant = weapon.m_shared.m_hitVariant,
            };
            hit.m_damage.Add(weapon.GetDamage());
            hit.m_damage.Modify(skill);   // (whole hit: ApplyModifier leaves untyped damage alone)
            hit.SetAttacker(p);
            p.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hit);

            GameObject shot = Object.Instantiate(bullet, spawn, Quaternion.LookRotation(dir));
            shot.GetComponent<IProjectile>()?.Setup(p, dir * MachineGun.BulletSpeed, 15f, hit, weapon, null);
            p.AddNoise(25f);
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
            if (!MachineGun.Is(weapon)) return true;
            __result = !secondaryAttack && Fire.Try((Player)__instance, weapon);
            return false;   // (skips only the game's own attack: other mods' prefixes still run)
        }
    }
}
