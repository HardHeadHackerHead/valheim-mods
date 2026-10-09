using UnityEngine;

namespace Ziplines
{
    /// <summary>The shape of a line: a rope between the tops of two posts that sags a little in the middle, and whether you could ride it.</summary>
    internal static class Line
    {
        /// <summary>Where a post's rope is held: on top of its pulley.</summary>
        public static Vector3 Anchor(Post post) => post.transform.position + post.transform.up * Things.Height;

        /// <summary>How far the rope drops in the middle: a little, more the longer it is.</summary>
        public static float Sag(float length) => Mathf.Clamp(length * 0.015f, 0.15f, 1.6f);

        /// <summary>A point along the rope, t from 0 (at a) to 1 (at b).</summary>
        public static Vector3 Point(Vector3 a, Vector3 b, float t) => Vector3.Lerp(a, b, t) + Vector3.down * Sag(Vector3.Distance(a, b)) * 4f * t * (1f - t);

        /// <summary>
        /// Whether this line can be ridden: null when it can, else why not. Your feet (the rope's height less how far you hang) must clear the
        /// ground all the way, and nothing solid may stand between the posts at the rope's height.
        /// </summary>
        public static string Clearance(Vector3 a, Vector3 b, Post ignoreA, Post ignoreB)
        {
            float length = Vector3.Distance(a, b);
            float hang = Plugin.Hang.Value;
            int mask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small", "vehicle");
            Vector3 previous = a;
            int steps = Mathf.Clamp(Mathf.RoundToInt(length / 15f), 48, 1200);   // (about every fifteen metres: a hill between two posts a few kilometres apart must not slip through)
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = Point(a, b, t);
                // the ground under the line: what is built and grows there where it is loaded, and the land itself (known everywhere) where it is not
                float ground = WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : float.MinValue;
                try
                {
                    if (ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(p) && ZoneSystem.instance.GetSolidHeight(p, out float solid) && !float.IsNaN(solid) && Mathf.Abs(solid - ground) < 40f) ground = Mathf.Max(ground, solid);
                }
                catch (System.Exception) { /* (ground the game cannot tell us about: the land itself is used) */ }
                if (t > 0.06f && t < 0.94f && ground > float.MinValue && p.y - hang < ground + 0.25f)
                    return $"The line would drag along the ground {length * t:0} m from here. Make the posts higher, or the line shorter.";
                if (Physics.Linecast(previous, p, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
                {
                    bool mine = hit.collider.GetComponentInParent<Post>() is Post post && (post == ignoreA || post == ignoreB);
                    if (!mine) return $"Something is in the way {length * t:0} m along the line ({Describe(hit.collider)}).";
                }
                previous = p;
            }
            return null;
        }

        private static string Describe(Collider c)
        {
            Piece piece = c.GetComponentInParent<Piece>();
            if (piece != null) return Localization.instance.Localize(piece.m_name).ToLowerInvariant();
            return c.GetComponentInParent<TreeBase>() != null || c.GetComponentInParent<TreeLog>() != null ? "a tree" : c.name.ToLowerInvariant().Contains("terrain") ? "the ground" : "a rock or a tree";
        }
    }
}
