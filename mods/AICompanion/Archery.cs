using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Aiming a bow as a good archer does. Valheim fires an arrow along the archer's look direction (Humanoid.GetAimDir), at the bow's speed
    /// (a full draw: its top speed), and the arrow then falls under its own gravity (Projectile). Its spread narrows with its Bows skill
    /// (20 degrees at 0, 1 at 100), as a player's does: that part is up to practice.
    /// So it aims:
    ///   - ahead of a moving target: the arrow's flight time is worked out and the target's motion over that time added (refined a few times,
    ///     as the flight time depends on the distance);
    ///   - above it for the drop: the launch angle that carries the arrow to that spot at that speed and gravity (the flatter of the two arcs,
    ///     the quick one), from about where the arrow leaves its hands.
    /// Null when the spot is beyond what the arrow can reach (it closes in instead).
    /// </summary>
    internal static class Archery
    {
        public static Vector3? Aim(Humanoid me, ItemDrop.ItemData bow, Character target)
        {
            if (bow == null || target == null) return null;
            // The game (Attack.FireProjectileBurst): the bow's speed, plus the arrow's own (0 for arrows: they bring the projectile), and the
            // arrow's projectile (its gravity: 5 for every arrow, no drag). Crude bow 35 m/s, fine wood bow 50.
            ItemDrop.ItemData ammo = me.GetAmmoItem();
            Attack attack = bow.m_shared.m_attack, extra = ammo?.m_shared.m_attack?.m_attackProjectile != null ? ammo.m_shared.m_attack : null;
            if (attack == null) return null;
            float speed = Mathf.Max(5f, attack.m_projectileVel + (extra?.m_projectileVel ?? 0f));
            GameObject flying = extra?.m_attackProjectile ?? attack.m_attackProjectile;
            Projectile arrow = flying != null ? flying.GetComponent<Projectile>() : null;
            float gravity = arrow != null ? Mathf.Max(0f, arrow.m_gravity) : 0f;

            Vector3 from = me.transform.position + Vector3.up * 1.5f;          // about where the arrow leaves (its hands, at chest height)
            Vector3 at = target.GetCenterPoint();
            Vector3 moving = target.GetVelocity();
            if (target.IsOnGround()) moving.y = 0f;                          // walking: it stays on the ground

            // Lead: where it will be when the arrow arrives.
            Vector3 aimed = at;
            for (int i = 0; i < 3; i++)
            {
                float t = Vector3.Distance(from, aimed) / speed;
                aimed = at + moving * t;
            }

            Vector3 flat = aimed - from;
            float height = flat.y;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d < 0.5f) return (aimed - from).normalized;
            Vector3 dir = flat / d;
            if (gravity <= 0.01f) return (aimed - from).normalized;           // no drop: straight at it

            // Drop: tan(angle) = (v^2 - sqrt(v^4 - g(g d^2 + 2 h v^2))) / (g d), the flatter arc.
            float v2 = speed * speed;
            float under = v2 * v2 - gravity * (gravity * d * d + 2f * height * v2);
            if (under < 0f) return null;                                      // too far for this arrow
            float angle = Mathf.Atan((v2 - Mathf.Sqrt(under)) / (gravity * d));
            return (dir * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
        }
    }
}
