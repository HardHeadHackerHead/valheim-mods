using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// Finding walls and ledges around the player with short casts. The game doesn't treat a vertical wall as ground (only contacts facing
    /// up count, in Character.OnCollisionStay), so it can't tell us about them. Anything solid counts: built pieces, rocks, cliffs, trees
    /// (the game's own ground layers); never water, creatures or triggers, and never anything that moves (ships, carts, ValheimRAFT's
    /// vehicles: a collider with a rigidbody), or the player would slide off a moving wall.
    /// </summary>
    internal static class Walls
    {
        internal struct Wall
        {
            public Vector3 Point, Normal;   // Normal: flat, pointing out of the wall toward the player
            public float Gap;               // between the player's body and the wall
            public Collider Collider;
        }

        internal struct Ledge
        {
            public Vector3 Edge;     // on the top, at the edge
            public Vector3 Normal;   // the wall's face, out toward the player
            public float Height;     // of the top above the player's feet
            public Vector3 Hang;     // the feet while hanging
            public Vector3 Stand;    // the feet once pulled up
            public bool Over;        // a thin top (a lone wall, a fence): over it, to drop on the far side
            public Collider Top, Face;   // what the top and the face are (for the sound of hands and feet on them)
        }

        // the layers the game's own ground checks use (Character.s_groundRayMask)
        private static int _mask;
        public static int Mask => _mask != 0 ? _mask : (_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle"));

        /// <summary>Something that stays put, and not a creature.</summary>
        private static bool Solid(Collider c) => c.attachedRigidbody == null && c.GetComponentInParent<Character>() == null;

        /// <summary>Steep enough to run on: within about 25 degrees of upright (normal.y between -0.45 and 0.45).</summary>
        private static bool Steep(Vector3 normal) => Mathf.Abs(normal.y) < 0.45f;

        private static float Radius(Player p) => p.GetCollider() != null ? p.GetCollider().radius : 0.4f;
        private static float Height(Player p) => p.GetCollider() != null ? p.GetCollider().height : 1.85f;

        private static readonly float[] Heights = { 0.45f, 0.75f };   // waist and chest, as parts of the body's height

        /// <summary>A wall this way (flat direction), within reach of the body, at waist or chest height.</summary>
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
                Vector3 n = hit.normal; n.y = 0f;
                wall = new Wall { Point = hit.point, Normal = n.normalized, Gap = hit.distance - r, Collider = hit.collider };
            }
            return best < float.MaxValue;
        }

        /// <summary>The nearest wall in any direction around the player (eight casts).</summary>
        public static bool Around(Player p, float reach, out Wall wall)
        {
            wall = default;
            bool found = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (Find(p, dir, reach, out Wall w) && (!found || w.Gap < wall.Gap)) { wall = w; found = true; }
            }
            return found;
        }

        /// <summary>
        /// An edge in front of the player within reach (its top between chest and above-head height) that the body fits on top of, and room
        /// to pull up past it.
        /// </summary>
        public static bool FindLedge(Player p, Vector3 forward, out Ledge ledge, float minHeight = 1.0f, float maxHeight = 2.25f, Vector3? from = null)
        {
            ledge = default;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) return false;
            forward.Normalize();
            Vector3 feet = from ?? p.transform.position;   // (another spot: a shimmy looks a little way along the edge)
            float r = Radius(p), h = Height(p);

            // the top: straight down just in front of the body, the first flat-ish surface in reach
            for (float ahead = r + 0.3f; ahead <= r + 0.81f; ahead += 0.25f)
            {
                Vector3 above = feet + forward * ahead + Vector3.up * (maxHeight + 0.3f);
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit top, maxHeight + 0.3f - minHeight + 0.05f, Mask, QueryTriggerInteraction.Ignore)) continue;
                if (top.normal.y < 0.7f || !Solid(top.collider)) continue;
                float height = top.point.y - feet.y;
                if (height < minHeight || height > maxHeight) return false;

                // the face under that top: a wall between the player and it, or none (a floor on posts, an overhang: open underneath)
                Vector3 chest = new Vector3(feet.x, top.point.y - 0.15f, feet.z);
                Vector3 facePoint, n;
                Collider faceCollider;
                if (Physics.Raycast(chest, forward, out RaycastHit face, ahead + 0.1f, Mask, QueryTriggerInteraction.Ignore))
                {
                    if (!Steep(face.normal) || !Solid(face.collider)) return false;
                    facePoint = face.point;
                    n = face.normal; n.y = 0f; n.Normalize();
                    faceCollider = face.collider;
                }
                else
                {
                    // no wall: the edge is where the top ends, toward the player (stepping back along it from where it was found)
                    Vector3 edge = top.point;
                    for (float d = 0.05f; d <= ahead; d += 0.05f)
                    {
                        Vector3 probe = top.point - forward * d;
                        probe.y = top.point.y + 0.3f;
                        if (!Physics.Raycast(probe, Vector3.down, out RaycastHit onTop, 0.45f, Mask, QueryTriggerInteraction.Ignore) || onTop.normal.y < 0.7f) break;
                        edge = onTop.point;
                    }
                    facePoint = new Vector3(edge.x, top.point.y - 0.15f, edge.z);
                    n = -forward;
                    faceCollider = top.collider;
                }

                // the wall must end there: stacked wall pieces have a seam where one stops and the next starts, and the look down
                // from above (begun inside the upper piece) sees it as a top. Something solid just above it, in front: not a ledge.
                for (float rise = 0.25f; rise <= 0.95f; rise += 0.35f)
                    if (Physics.Raycast(new Vector3(feet.x, top.point.y + rise, feet.z), forward, out RaycastHit higher, ahead + 0.15f, Mask, QueryTriggerInteraction.Ignore)
                        && Steep(higher.normal) && Solid(higher.collider)) return false;

                // where the body goes: hanging off the face, then stood on the top a little way in
                Vector3 hang = facePoint + n * (r + 0.02f);   // (the body just off the face: closer and it would push into the wall)
                hang.y = top.point.y - (h + 0.35f);   // (arms nearly straight: the shoulders an arm's length under the hands, the head just under the edge)
                Vector3 stand = facePoint - n * (r + 0.25f);
                bool over = false;
                if (Physics.Raycast(new Vector3(stand.x, top.point.y + 0.5f, stand.z), Vector3.down, out RaycastHit standHit, 0.8f, Mask, QueryTriggerInteraction.Ignore)
                    && standHit.normal.y >= 0.7f)
                    stand.y = standHit.point.y;
                else
                {
                    // nothing to stand on behind the edge: a thin top (a lone wall, a fence) is vaulted, over to the far side
                    float depth = TopDepth(facePoint, n, top.point.y);
                    if (depth <= 0f || depth > 0.9f) return false;
                    stand = facePoint - n * (depth + r + 0.2f);
                    stand.y = top.point.y + 0.05f;
                    over = true;
                }

                // room for the body: raised beside the wall to the top, and stood on it
                Vector3 raised = new Vector3(hang.x, top.point.y + 0.08f, hang.z);
                if (!Fits(raised, r, h) || !Fits(stand + Vector3.up * 0.05f, r, h)) return false;

                ledge = new Ledge { Edge = new Vector3(facePoint.x, top.point.y, facePoint.z), Normal = n, Height = height, Hang = hang, Stand = stand, Over = over, Top = top.collider, Face = faceCollider };
                return true;
            }
            return false;
        }

        /// <summary>How deep a wall's top is, from its face inward (0: no flat top there); a thin wall's is its thickness.</summary>
        private static float TopDepth(Vector3 facePoint, Vector3 n, float topY)
        {
            float depth = 0f;
            for (float d = 0.05f; d <= 1.2f; d += 0.05f)
            {
                Vector3 from = facePoint - n * d;
                from.y = topY + 0.3f;
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 0.45f, Mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f) break;
                depth = d;
            }
            return depth;
        }

        /// <summary>Would the player's body fit standing with its feet here (nothing solid in the way)?</summary>
        public static bool Fits(Vector3 feet, float r, float h)
        {
            float rr = r * 0.9f;
            return !Physics.CheckCapsule(feet + Vector3.up * (r + 0.02f), feet + Vector3.up * (h - r), rr, Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
