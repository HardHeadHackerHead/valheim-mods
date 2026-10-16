using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// What a web sticks to, and the walls a player crawls on. Anything solid that stays put counts: trees, rocks, cliffs, the ground,
    /// built pieces (the game's own ground layers). Never water, creatures, triggers, invisible blockers or anything that moves (ships, carts:
    /// a collider with a rigidbody), or the web would hang in the air when it moves away.
    /// </summary>
    internal static class Anchors
    {
        internal struct Wall
        {
            public Vector3 Point, Normal;   // Normal: out of the wall toward the player (may lean: an overhang or a slope)
            public float Gap;               // between the player's body and the wall
            public Collider Collider;
        }

        private static int _mask;
        public static int Mask => _mask != 0 ? _mask : (_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain"));

        public static bool Solid(Collider c) => c != null && c.attachedRigidbody == null && c.GetComponentInParent<Character>() == null;

        /// <summary>Steep enough to crawl on: within about 37 degrees of upright, a little overhang allowed.</summary>
        public static bool Steep(Vector3 normal) => normal.y < 0.6f && normal.y > -0.35f;

        public static float Radius(Player p) => p.GetCollider() != null ? p.GetCollider().radius : 0.4f;
        public static float Height(Player p) => p.GetCollider() != null ? p.GetCollider().height : 1.85f;

        /// <summary>Where a web leaves the body (about the raised hand): the swing turns round this point.</summary>
        public static Vector3 Pivot(Player p) => p.transform.position + Vector3.up * 1.6f;

        /// <summary>The way the player looks, flat (the camera's way: where the mouse points).</summary>
        public static Vector3 LookFlat(Player p)
        {
            Vector3 look = p.GetLookDir();
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) look = p.transform.forward;
            look.y = 0f;
            return look.normalized;
        }

        // ---- swing webs ----

        private static readonly float[] Pitches = { 25f, 35f, 45f, 55f, 65f, 78f };
        private static readonly float[] Yaws = { 0f, -15f, 15f, -30f, 30f, -45f, 45f, -60f, 60f };

        /// <summary>The body's lowest point keeps this far above the ground at the bottom of a swing (metres, feet to ground).</summary>
        public const float Clear = 1.5f;
        /// <summary>The shortest rope worth swinging on.</summary>
        public const float MinRope = 5f;
        /// <summary>
        /// How much of an anchor's offset to the side of the way the body flies the swing ignores (0: a real pendulum, round the anchor;
        /// 1: straight on under it). The games swing you straight down the street on webs to the buildings either side: the web is drawn
        /// to the real spot, the body swings under a point above its own path (Virtual).
        /// </summary>
        public const float Bend = 0.65f;

        /// <summary>A spot a swing web sticks to.</summary>
        internal struct Spot
        {
            public Vector3 Point, Normal;   // where the web is drawn to, and the surface's facing there
            public Vector3 Hold;            // the swing's pivot: a little out from the surface (a rope hangs off a trunk, not inside it)
            public Collider What;
            public float Rope;              // the rope's length: keeps the bottom of the arc clear of the ground
            public bool ArcClear;           // nothing (a trunk, a wall) in the way of the swing it gives
        }

        /// <summary>Why the last search found nothing (for the status command).</summary>
        public static string LastMiss = "";

        private struct Candidate { public Vector3 Point, Normal; public Collider What; public float Score; }
        private static readonly System.Collections.Generic.List<Candidate> Found = new System.Collections.Generic.List<Candidate>();

        /// <summary>
        /// The best spot for a swing web: a fan of casts up and ahead (25-78 degrees up, 60 either side of the look, tilted toward where the
        /// body is already flying). The best is far ahead and high, near the look, wants little reeling in, and gives a swing nothing is in
        /// the way of (a trunk straight ahead would be flown into: a tree to the side is better). It must be high enough over the ground for a
        /// real swing (Rope at least MinRope). As in the games, the player never picks it: holding Jump is enough.
        /// </summary>
        public static bool SwingAnchor(Player p, float reach, out Spot spot)
        {
            spot = default;
            LastMiss = "no surface in reach (nothing within " + reach.ToString("0") + " m up and ahead)";
            float highest = float.MinValue;
            Vector3 from = Pivot(p), ahead = Ahead(p);
            Vector3 right = Vector3.Cross(Vector3.up, ahead);
            Found.Clear();
            foreach (float yaw in Yaws)
                foreach (float pitch in Pitches)
                {
                    Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * (Quaternion.AngleAxis(-pitch, right) * ahead);
                    if (!Physics.Raycast(from, dir, out RaycastHit hit, reach, Mask, QueryTriggerInteraction.Ignore)) continue;
                    if (!Solid(hit.collider)) continue;
                    Vector3 to = hit.point - from;
                    float up = to.y;
                    if (up < 2f) { if (LastMiss.StartsWith("no surface")) LastMiss = "only spots below or level with you"; continue; }
                    float forward = Vector3.Dot(to, ahead), side = Mathf.Abs(Vector3.Dot(to, right));
                    if (forward < -2f) { if (LastMiss.StartsWith("no surface")) LastMiss = "only spots behind you"; continue; }
                    // ahead and high, near the look, not too close
                    float score = forward * 1.0f + Mathf.Min(up, 30f) * 0.9f - side * 0.3f - Mathf.Max(0f, 10f - to.magnitude) * 2f;
                    Found.Add(new Candidate { Point = hit.point, Normal = hit.normal, What = hit.collider, Score = score });
                }
            Found.Sort((a, b) => b.Score.CompareTo(a.Score));
            float best = float.MinValue;
            for (int i = 0; i < Found.Count && i < 10; i++)
            {
                Candidate c = Found[i];
                Vector3 hold = HoldFor(c.Point, c.Normal);
                Vector3 pivot = Virtual(hold, from, ahead);
                float rope = RopeFor(p, pivot);
                if (rope < MinRope)
                {
                    if (rope > highest) highest = rope;
                    LastMiss = "spots too low over the ground for a swing (the best gives a " + Mathf.Max(0f, highest).ToString("0.0") + " m rope, needs " + MinRope.ToString("0") + ")";
                    continue;
                }
                bool clear = ArcIsClear(p, pivot, rope, ahead);
                // little reeling in (a long reel is a yank, not a swing), and nothing in the way
                float score = c.Score - Mathf.Max(0f, Vector3.Distance(from, pivot) - rope) * 0.6f - (clear ? 0f : 30f);
                if (score <= best) continue;
                best = score;
                spot = new Spot { Point = c.Point, Normal = c.Normal, Hold = hold, What = c.What, Rope = rope, ArcClear = clear };
            }
            return best > float.MinValue;
        }

        /// <summary>The swing's pivot for a web stuck here: out from a wall or a trunk, so the body hanging under it misses the surface.</summary>
        public static Vector3 HoldFor(Vector3 point, Vector3 normal)
        {
            if (Steep(normal))
            {
                Vector3 flat = normal; flat.y = 0f;
                return point + flat.normalized * 0.8f;
            }
            return point + normal * 0.2f;
        }

        /// <summary>
        /// The point the body swings under: the pivot moved most of the way (Bend) toward the vertical plane the body flies in (from its hand,
        /// along travel, flat). An anchor off to the side then swings the body on, not round it.
        /// </summary>
        public static Vector3 Virtual(Vector3 hold, Vector3 hand, Vector3 travel)
        {
            travel.y = 0f;
            if (travel.sqrMagnitude < 0.01f) return hold;
            travel.Normalize();
            Vector3 off = hold - hand; off.y = 0f;
            Vector3 side = off - travel * Vector3.Dot(off, travel);
            return hold - side * Bend;
        }

        /// <summary>
        /// Whether the swing from this pivot is free: the body's way down to the bottom of the arc, and on up past it, swept with a ball the
        /// body's size. A trunk or a wall in the way is flown into.
        /// </summary>
        public static bool ArcIsClear(Player p, Vector3 pivot, float rope, Vector3 travel)
        {
            travel.y = 0f;
            travel = travel.sqrMagnitude > 0.01f ? travel.normalized : LookFlat(p);
            const float hang = 0.7f;   // (the body's middle, below the hand)
            Vector3 now = Pivot(p) - Vector3.up * hang;
            Vector3 bottom = pivot - Vector3.up * (rope + hang);
            Vector3 past = pivot + travel * (rope * 0.75f) - Vector3.up * (rope * 0.65f + hang);
            return Free(now, bottom) && Free(bottom, past);
        }

        private static bool Free(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            int n = Physics.SphereCastNonAlloc(a, 0.35f, d / len, Hits, len, Mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (Hits[i].distance > 0f && Solid(Hits[i].collider)) return false;   // (0: touching at the start, as the body may be)
            return true;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[32];

        /// <summary>Ahead: the way the player looks, turned a little toward the way the body already flies.</summary>
        public static Vector3 Ahead(Player p)
        {
            Vector3 ahead = LookFlat(p);
            Vector3 vel = p.GetVelocity(); vel.y = 0f;
            if (vel.magnitude > 4f) ahead = Vector3.Slerp(ahead, vel.normalized, 0.35f).normalized;
            return ahead;
        }

        /// <summary>
        /// How long a rope from this pivot may be so the bottom of its arc (straight under it) keeps the body Clear above the ground there,
        /// and above the ground under the player now. The rope never starts longer than the distance to the pivot: a shorter one reels the
        /// player in (the yank of a web).
        /// </summary>
        public static float RopeFor(Player p, Vector3 pivotPoint)
        {
            Vector3 hand = Pivot(p);
            float ground = Mathf.Max(GroundAt(pivotPoint, pivotPoint.y), GroundAt(p.transform.position + Vector3.up * 0.5f, p.transform.position.y + 0.5f));
            float hang = hand.y - p.transform.position.y;   // (the rope ends at the hand: the feet hang this far below it)
            return Mathf.Min(Vector3.Distance(hand, pivotPoint), pivotPoint.y - ground - Clear - hang);
        }

        /// <summary>The height of the ground (or water) under a spot, looking down from the given height; far below when there is none.</summary>
        public static float GroundAt(Vector3 at, float fromY)
        {
            Vector3 from = new Vector3(at.x, fromY - 0.3f, at.z);
            float y = Physics.Raycast(from, Vector3.down, out RaycastHit hit, 150f, Mask, QueryTriggerInteraction.Ignore) ? hit.point.y : fromY - 150f;
            float water = Floating.GetLiquidLevel(at, 1f, LiquidType.Water);
            if (water > -1000f) y = Mathf.Max(y, water);
            return y;
        }

        /// <summary>
        /// Nothing in reach (the SkyWebs setting): a spot up and ahead stuck to nothing, high enough for a full swing over the ground ahead,
        /// as the Amazing Spider-Man games do.
        /// </summary>
        public static Vector3 SkyAnchor(Player p)
        {
            Vector3 ahead = Ahead(p), pivot = Pivot(p);
            Vector3 spot = pivot + ahead * 14f;
            float ground = Mathf.Max(GroundAt(spot, pivot.y + 30f), GroundAt(p.transform.position, pivot.y));
            float y = Mathf.Max(pivot.y + 12f, ground + 1.6f + Clear + 14f);
            return new Vector3(spot.x, y, spot.z);
        }

        // ---- zips ----

        internal enum Kind { Top, Wall, Under }

        /// <summary>The spot under the crosshair (the camera's ray), within reach of the player: a top, a wall, or the underside of something.</summary>
        public static bool ZipTarget(Player p, float reach, out RaycastHit target, out Kind kind)
        {
            target = default;
            kind = Kind.Top;
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            Vector3 from = cam != null ? cam.position : Pivot(p);
            Vector3 dir = cam != null ? cam.forward : p.GetLookDir();
            float behind = Vector3.Distance(from, Pivot(p));   // (the camera sits behind the player: reach is measured from the player)
            RaycastHit[] hits = Physics.RaycastAll(from, dir, reach + behind + 2f, Mask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (!Solid(h.collider) || h.distance >= nearest) continue;
                if (Vector3.Dot(h.point - Pivot(p), dir) < 0.5f) continue;   // (between the camera and the player)
                nearest = h.distance;
                target = h;
            }
            if (nearest == float.MaxValue || Vector3.Distance(target.point, Pivot(p)) > reach) return false;
            kind = target.normal.y >= 0.6f ? Kind.Top : (Steep(target.normal) ? Kind.Wall : Kind.Under);
            return true;
        }

        // ---- walls ----

        private static readonly float[] Heights = { 0.45f, 0.75f };   // waist and chest, as parts of the body's height

        /// <summary>A crawlable wall this way (flat direction), within reach of the body, at waist or chest height.</summary>
        public static bool Find(Player p, Vector3 dir, float reach, out Wall wall)
        {
            wall = default;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();
            float r = Radius(p), h = Height(p), best = float.MaxValue;
            foreach (float at in Heights)
            {
                Vector3 from = p.transform.position + Vector3.up * (h * at);
                if (!Physics.Raycast(from, dir, out RaycastHit hit, r + reach, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Steep(hit.normal) || !Solid(hit.collider) || hit.distance >= best) continue;
                best = hit.distance;
                wall = new Wall { Point = hit.point, Normal = hit.normal, Gap = hit.distance - r, Collider = hit.collider };
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// The wall the player crawls on, again this step: casts into it (along its last facing) from the waist, the chest and the head, and the
        /// nearest steep hit wins. None within reach: the wall ended (its top, its side, or the player crawled off it).
        /// </summary>
        public static bool Track(Player p, Vector3 normal, out Wall wall)
        {
            wall = default;
            Vector3 into = -normal;
            float r = Radius(p), h = Height(p), best = float.MaxValue;
            foreach (float at in new[] { 0.3f, 0.6f, 0.9f })
            {
                Vector3 from = p.transform.position + Vector3.up * (h * at);
                if (!Physics.Raycast(from, into, out RaycastHit hit, r + 0.9f, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Steep(hit.normal) || !Solid(hit.collider) || hit.distance >= best) continue;
                best = hit.distance;
                wall = new Wall { Point = hit.point, Normal = hit.normal, Gap = hit.distance - r, Collider = hit.collider };
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// A top to climb onto at the head of a wall: ground (facing up) just beyond the wall's face, no higher than 2 m above the player's
        /// feet, with room for the body standing on it. Stand: where the feet go.
        /// </summary>
        public static bool TopOver(Player p, Vector3 normal, out Vector3 stand)
        {
            stand = Vector3.zero;
            Vector3 into = -normal; into.y = 0f;
            if (into.sqrMagnitude < 0.01f) return false;
            into.Normalize();
            float r = Radius(p), h = Height(p);
            Vector3 above = p.transform.position + Vector3.up * (h + 1.2f) + into * (r * 2f + 0.35f);
            if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, h + 1.2f, Mask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.6f || !Solid(hit.collider)) return false;
            if (hit.point.y < p.transform.position.y - 0.2f) return false;
            Vector3 bottom = hit.point + Vector3.up * (r + 0.05f), top = hit.point + Vector3.up * (h - r);
            if (Physics.CheckCapsule(bottom, top, r * 0.9f, Mask, QueryTriggerInteraction.Ignore)) return false;
            stand = hit.point;
            return true;
        }

        /// <summary>How high the player is above whatever is under them (or ahead of them, moving fast): 99 when nothing within 30 m.</summary>
        public static float Clearance(Player p, Vector3 vel)
        {
            Vector3 flat = vel; flat.y = 0f;
            float best = 99f;
            foreach (float t in new[] { 0f, 0.25f, 0.5f })
            {
                Vector3 from = p.transform.position + flat * t + Vector3.up * 0.2f;
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f, Mask, QueryTriggerInteraction.Ignore))
                    best = Mathf.Min(best, hit.distance - 0.2f);
            }
            // water counts as ground: no swinging through the sea
            float water = Floating.GetLiquidLevel(p.transform.position, 1f, LiquidType.Water);
            if (water > -10000f) best = Mathf.Min(best, p.transform.position.y - water);
            return best;
        }
    }
}
