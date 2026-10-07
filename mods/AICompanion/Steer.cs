using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Watching where it walks, every frame after its brain has picked where to go (the game's pathfinding knows walls, not what hurts):
    ///   - hurtful things: the damaging area of anything (the pointed side of sharpened stakes, fires, an enemy's lingering poison or fire) is
    ///     looked for a few times a second; when its way runs into one it steers round it (keeping to one side, so it does not dither), and
    ///     when there is no safe way at all it stops (following you, the stuck check then hops it over to you). Standing in one, it steps out.
    ///   - drops: it does not walk off an edge high enough to hurt (fall damage starts at about 4 m), unless there is water below.
    ///   - jumping: pressed against something knee to chest high with room above it and a safe landing (a log, a step, a low wall), it jumps,
    ///     as a player would (it costs stamina).
    /// Only on the game that runs it; never while it rides a boat or swims.
    /// </summary>
    internal static class Steer
    {
        private class Hazard { public Collider Col; public Vector3 Center; public float Radius; }
        private class State { public float NextScan, NextJump, NextMoveCheck, Side, SideUntil, PressedSince = -1f; public Vector3 LastPos; public readonly List<Hazard> Near = new List<Hazard>(); }

        private static readonly Dictionary<Humanoid, State> States = new Dictionary<Humanoid, State>();
        private static readonly AccessTools.FieldRef<Aoe, Character> Owner = AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        private static readonly float[] Angles = { 30f, 60f, 90f, 120f, 150f };

        public static void Forget() => States.Clear();

        /// <summary>For Claude Tools: the hurtful things around a spot, by name (prefab), and the nearest's distance.</summary>
        public static string Around(Vector3 pos, float radius)
        {
            var found = new Dictionary<Aoe, float>();
            foreach (Collider col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Collide))
            {
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe != null && !found.ContainsKey(aoe) && Dangerous(aoe, null)) found[aoe] = Vector3.Distance(aoe.transform.position, pos);
            }
            if (found.Count == 0) return "none";
            return string.Join(", ", found.GroupBy(kv => Utils.GetPrefabName(kv.Key.transform.root.gameObject)).Select(g => $"{g.Count()} {g.Key}")) + $"; nearest {found.Values.Min():0.0} m";
        }

        public static int HazardsNear(Humanoid h) => h != null && States.TryGetValue(h, out State s) ? s.Near.Count : 0;

        private static State Of(Humanoid h)
        {
            if (States.TryGetValue(h, out State s)) return s;
            foreach (Humanoid gone in States.Keys.Where(k => k == null).ToList()) States.Remove(gone);
            return States[h] = new State { LastPos = h.transform.position };
        }

        public static void Tick(Humanoid me, BrainState st)
        {
            if (me == null || me.IsDead() || st.Riding != null || me.IsSwimming()) return;
            State s = Of(me);
            Vector3 pos = me.transform.position;
            if (Time.time >= s.NextScan) { s.NextScan = Time.time + 0.3f; Scan(me, s, pos); }
            Vector3 dir = me.GetMoveDir();
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { s.PressedSince = -1f; s.LastPos = pos; return; }
            if (Time.time >= s.NextMoveCheck) // trying to move but getting nowhere: pressed against something
            {
                s.NextMoveCheck = Time.time + 0.25f;
                bool still = Flat(pos - s.LastPos) < 0.1f;
                s.LastPos = pos;
                s.PressedSince = !still ? -1f : s.PressedSince < 0f ? Time.time : s.PressedSince;
            }
            dir.Normalize();

            // In one already: straight out.
            Hazard inside = s.Near.FirstOrDefault(h => Hurts(h, pos, 0.3f));
            if (inside != null)
            {
                Vector3 away = pos - Closest(inside, pos + Vector3.up * 0.9f);
                away.y = 0f;
                Go(me, st, away.sqrMagnitude > 0.001f ? away.normalized : -dir);
                return;
            }

            if (Clear(s, pos, dir)) { Jump(me, s, pos, dir); return; }

            // Round it: the side it chose a moment ago first, then the other.
            float first = Time.time < s.SideUntil ? s.Side : 1f;
            foreach (float a in Angles)
                foreach (float sign in new[] { first, -first })
                {
                    Vector3 d = Quaternion.Euler(0f, a * sign, 0f) * dir;
                    if (!Clear(s, pos, d)) continue;
                    s.Side = sign;
                    s.SideUntil = Time.time + 1.5f;
                    Go(me, st, d);
                    return;
                }
            me.SetMoveDir(Vector3.zero); // no safe way: it waits (or, following you, hops over to you)
            if (!st.InCombat) Brain.Status(st, "looking for a safe way round");
        }

        private static void Go(Humanoid me, BrainState st, Vector3 d)
        {
            me.SetMoveDir(d);
            if (!st.InCombat) me.SetLookDir(d, 0f); // in a fight it keeps facing its enemy and steps sideways
        }

        /// <summary>No hurtful thing along the next 2 m of this way, and no drop that would hurt.</summary>
        private static bool Clear(State s, Vector3 pos, Vector3 dir)
        {
            for (float d = 0.5f; d <= 2.01f; d += 0.5f)
            {
                Vector3 p = pos + dir * d;
                foreach (Hazard h in s.Near) if (Hurts(h, p, 0.5f)) return false;
            }
            return !Drop(pos, pos + dir * 1.2f);
        }

        private static bool Drop(Vector3 from, Vector3 to)
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetSolidHeight(to, out float ground)) return false;
            if (ground < ZoneSystem.instance.m_waterLevel - 0.5f) return false; // into water: no harm
            return from.y - ground > 3.5f;
        }

        /// <summary>Would its body (feet to head) at this spot be within "margin" of the hurtful thing?</summary>
        private static bool Hurts(Hazard h, Vector3 feet, float margin)
        {
            for (float y = 0.3f; y <= 1.6f; y += 0.65f)
            {
                Vector3 body = feet + Vector3.up * y;
                if ((Closest(h, body) - body).sqrMagnitude < (margin + 0.4f) * (margin + 0.4f)) return true;
            }
            return false;
        }

        private static Vector3 Closest(Hazard h, Vector3 p)
        {
            if (h.Col != null) return h.Col.ClosestPoint(p);
            Vector3 off = p - h.Center;
            return off.magnitude <= h.Radius ? p : h.Center + off.normalized * h.Radius;
        }

        private static void Scan(Humanoid me, State s, Vector3 pos)
        {
            s.Near.Clear();
            var seen = new HashSet<Aoe>();
            foreach (Collider col in Physics.OverlapSphere(pos + Vector3.up, 7f, ~0, QueryTriggerInteraction.Collide))
            {
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe == null || !seen.Add(aoe) || !Dangerous(aoe, me)) continue;
                Collider own = aoe.m_useCollider != null ? (Collider)aoe.m_useCollider : aoe.GetComponent<Collider>();
                s.Near.Add(own != null && own.enabled ? new Hazard { Col = own } : new Hazard { Center = aoe.transform.position, Radius = Mathf.Max(0.5f, aoe.m_radius) });
            }
        }

        /// <summary>Something that would hurt it: it hits creatures, does damage (or puts on an effect), and is not its own side's.</summary>
        private static bool Dangerous(Aoe aoe, Humanoid me)
        {
            if (!aoe.isActiveAndEnabled || !aoe.m_hitCharacters) return false;
            if (aoe.m_damage.GetTotalDamage() <= 0f && string.IsNullOrEmpty(aoe.m_statusEffect)) return false;
            Character owner = Owner(aoe);
            return owner == null ? aoe.GetComponentInParent<Character>() == null : owner != me && !owner.IsPlayer() && !Companion.Is(owner);
        }

        /// <summary>Is this spot free of hurtful things? (for putting it beside its player)</summary>
        public static bool Safe(Vector3 feet, Humanoid me)
        {
            foreach (Collider col in Physics.OverlapSphere(feet + Vector3.up * 0.9f, 1f, ~0, QueryTriggerInteraction.Collide))
            {
                Aoe aoe = col.GetComponent<Aoe>() ?? col.GetComponentInParent<Aoe>();
                if (aoe != null && Dangerous(aoe, me)) return false;
            }
            return true;
        }

        /// <summary>
        /// Pressed against something low (it has not moved for a moment while trying to): a jump, when the thing is knee to chest high, there is
        /// room above it, and the landing is safe.
        /// </summary>
        private static void Jump(Humanoid me, State s, Vector3 pos, Vector3 dir)
        {
            if (s.PressedSince < 0f || Time.time - s.PressedSince < 0.25f || Time.time < s.NextJump || !me.IsOnGround() || me.InAttack()) return;
            if (!Physics.Raycast(pos + Vector3.up * 0.45f, dir, out RaycastHit knee, 1f, Solid)) return;
            if (knee.collider.GetComponentInParent<Character>() != null) return;                 // never over people
            if (Physics.Raycast(pos + Vector3.up * 1.7f, dir, 1.3f, Solid)) return;               // too high (a wall), or no room above
            Vector3 over = knee.point + dir * 0.3f;
            if (!Physics.Raycast(new Vector3(over.x, pos.y + 1.7f, over.z), Vector3.down, out RaycastHit top, 2.5f, Solid)) return;
            float height = top.point.y - pos.y;
            if (height < 0.3f || height > 1.3f) return;
            Vector3 land = pos + dir * 1.6f;
            if (s.Near.Any(h => Hurts(h, land, 0.4f)) || Drop(pos, land)) return;               // never into stakes or off an edge
            s.NextJump = Time.time + 1.2f;
            s.PressedSince = -1f;
            me.Jump(false);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
