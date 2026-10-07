using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Its own way-finding, for when the game's has none (its walk map comes out empty in some places, a big base among them: then the game
    /// finds no way anywhere there, and walking straight took it into the stakes). A search over a grid of 0.75 m squares between it and
    /// where it goes (up to about 70 m): a square it can stand on is ground or a floor a step away from the last (stairs too), with room for
    /// its body (no wall, post or fence there; a door is fine, it opens it), and nothing that hurts within reach (stakes, fire: Steer). It
    /// walks the way square by square, and looks again when the target moves or it is held up.
    /// </summary>
    internal static class Wayfinding
    {
        private const float Cell = 0.75f, Step = 0.8f, Margin = 10f, MaxRange = 90f;
        private const int MaxNodes = 15000;
        private static readonly int Ground = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
        private static readonly int Solid = LayerMask.GetMask("Default", "static_solid", "piece", "vehicle");
        private static readonly Collider[] Hits = new Collider[16];

        /// <summary>Following its own way to the point: true while it walks it (then the rest of MoveTo waits).</summary>
        public static bool Follow(BrainState st, Humanoid me, Vector3 to, bool run)
        {
            Vector3 pos = me.transform.position;
            bool fresh = st.PathTo.HasValue && Utils.DistanceXZ(st.PathTo.Value, to) < 2f && st.Path != null && Time.time < st.PathUntil;
            if (!fresh)
            {
                if (Time.time < st.NextPathTry) return st.Path != null && Walk(st, me, run);
                st.NextPathTry = Time.time + 1.5f;
                st.Path = Find(me, pos, to, 12f); // (the way it walks: a longer search than a quick "can it get there")
                if (st.Path == null && TimedOut) st.NextPathTry = Time.time + 0.3f; // (no time this frame: again in a moment)
                st.PathTo = to;
                st.PathIndex = 0;
                st.PathUntil = Time.time + 20f;
                st.PathProgressAt = Time.time; st.PathBest = float.MaxValue;
                if (st.Path == null) return false;
                Activity.Log(me, $"found its own way to {to:F0} ({st.Path.Count} steps; the game's way-finding has none here)");
            }
            return Walk(st, me, run);
        }

        private static bool Walk(BrainState st, Humanoid me, bool run)
        {
            List<Vector3> path = st.Path;
            Vector3 pos = me.transform.position;
            while (st.PathIndex < path.Count && Utils.DistanceXZ(path[st.PathIndex], pos) < 0.6f) st.PathIndex++;
            if (st.PathIndex >= path.Count) { st.Path = null; return false; } // there
            // Held up (a door it could not open, someone in the way): a new way in a moment.
            float left = Utils.DistanceXZ(path[path.Count - 1], pos) + (path.Count - st.PathIndex) * 0.01f;
            if (left < st.PathBest - 0.3f) { st.PathBest = left; st.PathProgressAt = Time.time; }
            else if (Time.time - st.PathProgressAt > 3f) { st.Path = null; st.NextPathTry = Time.time + 0.5f; return false; }
            // Cut corners: the furthest of the next few squares it can walk to straight.
            int aim = st.PathIndex;
            for (int i = st.PathIndex + 1; i < Mathf.Min(path.Count, st.PathIndex + 5); i++)
                if (Straight(me, pos, path[i])) aim = i; else break;
            Vector3 dir = path[aim] - pos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return true;
            dir.Normalize();
            me.SetMoveDir(dir);
            me.SetRun(run && Stamina.CanRun(me));
            me.SetLookDir(dir, 0f);
            return true;
        }

        private static bool Straight(Humanoid me, Vector3 a, Vector3 b)
        {
            float len = Utils.DistanceXZ(a, b);
            for (float t = 0.5f; t < len; t += 0.5f)
            {
                Vector3 p = Vector3.Lerp(a, b, t / len);
                if (!Room(me, p) || !Steer.Clear(p, me)) return false;
            }
            return true;
        }

        private static readonly Dictionary<Vector3Int, (bool ok, float until)> Reach = new Dictionary<Vector3Int, (bool, float)>();

        /// <summary>
        /// Can it get there by its own way-finding? Remembered (a way: 15 s; none: a minute), by where it is (to 8 m) and where to: the search is
        /// not free. When the search runs out of its time, it takes it as a yes and tries (it finds out on the way) rather than hold the game up.
        /// </summary>
        public static bool CanReach(BrainState st, Humanoid me, Vector3 to)
        {
            if (Utils.DistanceXZ(me.transform.position, to) < 2.5f) return true;
            Vector3 p = me.transform.position;
            var key = new Vector3Int(Mathf.RoundToInt(to.x) * 1000 + Mathf.RoundToInt(p.x / 8f), Mathf.RoundToInt(to.y), Mathf.RoundToInt(to.z) * 1000 + Mathf.RoundToInt(p.z / 8f));
            if (Reach.TryGetValue(key, out var r) && Time.time < r.until) return r.ok;
            if (Reach.Count > 500) Reach.Clear();
            List<Vector3> way = Find(me, p, to);
            if (way == null && TimedOut) return true; // (no time to be sure: it tries)
            Reach[key] = (way != null, Time.time + (way != null ? 15f : 60f));
            return way != null;
        }

        /// <summary>The last search ran out of its time (4 ms to ask, 12 ms for the way it walks; 14 ms for all of them in one frame) before it was sure.</summary>
        public static bool TimedOut;
        private static int _frame;
        private static float _frameMs;

        private static readonly Comparer<(float f, int n, Vector2Int k)> Order = Comparer<(float f, int n, Vector2Int k)>.Create((a, b) => a.f != b.f ? a.f.CompareTo(b.f) : a.n.CompareTo(b.n));

        /// <summary>The way (squares to walk through), or null when there is none it can find (or it is too far for this).</summary>
        public static List<Vector3> Find(Humanoid me, Vector3 from, Vector3 to, float budgetMs = 4f)
        {
            TimedOut = false;
            if (Utils.DistanceXZ(from, to) > MaxRange) return null;
            if (_frame != Time.frameCount) { _frame = Time.frameCount; _frameMs = 0f; }
            if (_frameMs >= 14f) { TimedOut = true; return null; } // (this frame has had its share)
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Vector2 min = new Vector2(Mathf.Min(from.x, to.x) - Margin, Mathf.Min(from.z, to.z) - Margin);
            Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt((p.x - min.x) / Cell), Mathf.RoundToInt((p.z - min.y) / Cell));
            Vector3 At(Vector2Int k, float y) => new Vector3(min.x + k.x * Cell, y, min.y + k.y * Cell);
            int w = Mathf.CeilToInt((Mathf.Abs(from.x - to.x) + Margin * 2f) / Cell), h = Mathf.CeilToInt((Mathf.Abs(from.z - to.z) + Margin * 2f) / Cell);

            Vector2Int start = Key(from), goal = Key(to);
            var height = new Dictionary<Vector2Int, float> { [start] = from.y };
            var cost = new Dictionary<Vector2Int, float> { [start] = 0f };
            var came = new Dictionary<Vector2Int, Vector2Int>();
            var open = new SortedSet<(float f, int n, Vector2Int k)>(Order); // (by cost, then by when added: a square is never compared)
            int counter = 0;
            open.Add((Vector2.Distance(start, goal), counter++, start));
            Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1) };
            int expanded = 0;
            Vector2Int? reached = null;
            while (open.Count > 0 && expanded++ < MaxNodes)
            {
                if ((expanded & 63) == 0 && watch.Elapsed.TotalMilliseconds > budgetMs) { TimedOut = true; break; } // (the game must not stutter for it)
                var cur = open.Min;
                open.Remove(cur);
                Vector2Int k = cur.k;
                float y = height[k];
                if ((k - goal).sqrMagnitude <= 2 && Mathf.Abs(y - to.y) < 1.5f) { reached = k; break; }
                foreach (Vector2Int d in dirs)
                {
                    Vector2Int n = k + d;
                    if (n.x < 0 || n.y < 0 || n.x > w || n.y > h) continue;
                    if (!Stand(me, At(n, y), y, out float ny)) continue;
                    if (d.x != 0 && d.y != 0 && (!Stand(me, At(new Vector2Int(k.x + d.x, k.y), y), y, out _) || !Stand(me, At(new Vector2Int(k.x, k.y + d.y), y), y, out _))) continue; // (no squeezing past a corner)
                    float g = cost[k] + (d.x != 0 && d.y != 0 ? 1.414f : 1f) + Mathf.Abs(ny - y) * 0.5f;
                    if (cost.TryGetValue(n, out float had) && had <= g) continue;
                    cost[n] = g; height[n] = ny; came[n] = k;
                    open.Add((g + Vector2.Distance(n, goal), counter++, n));
                }
            }
            _frameMs += (float)watch.Elapsed.TotalMilliseconds;
            if (reached == null) return null;
            var path = new List<Vector3>();
            for (Vector2Int k = reached.Value; came.ContainsKey(k); k = came[k]) path.Add(At(k, height[k]));
            path.Reverse();
            path.Add(to);
            return path;
        }

        /// <summary>Can it stand there, coming from height y: ground or a floor a step up or down, room for its body, nothing that hurts.</summary>
        private static bool Stand(Humanoid me, Vector3 p, float y, out float ny)
        {
            ny = y;
            if (!Physics.Raycast(new Vector3(p.x, y + 1.3f, p.z), Vector3.down, out RaycastHit hit, 1.3f + Step + 0.6f, Ground, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.GetComponentInParent<Character>() != null) return false;
            ny = hit.point.y;
            if (Mathf.Abs(ny - y) > Step) return false;
            Vector3 feet = new Vector3(p.x, ny, p.z);
            return Room(me, feet) && Steer.Clear(feet, me);
        }

        /// <summary>
        /// A map of what is around it (companion map): a square a metre wide for each character; X something that hurts (stakes, fire), # no
        /// room (a wall, a post), D a door, . ground or floor it can stand on, : a floor well above or below it, blank nothing to stand on,
        /// * the way it finds to the target, C it, P you, B the target.
        /// </summary>
        public static List<string> Map(Humanoid me, Vector3 target, Vector3 you, int radius)
        {
            Vector3 c = me.transform.position;
            var way = new HashSet<Vector2Int>();
            List<Vector3> path = Find(me, c, target);
            if (path != null) foreach (Vector3 p in path) way.Add(new Vector2Int(Mathf.RoundToInt(p.x - c.x), Mathf.RoundToInt(p.z - c.z)));
            var rows = new List<string> { $"north is up; 1 m a square; around {c:F0}; way to {target:F0}: {(path == null ? "none found" : path.Count + " steps")}" };
            for (int dz = radius; dz >= -radius; dz--)
            {
                var row = new System.Text.StringBuilder();
                for (int dx = -radius; dx <= radius; dx++)
                {
                    Vector3 p = new Vector3(c.x + dx, c.y, c.z + dz);
                    char ch;
                    if (dx == 0 && dz == 0) ch = 'C';
                    else if (Mathf.RoundToInt(you.x - c.x) == dx && Mathf.RoundToInt(you.z - c.z) == dz) ch = 'P';
                    else if (Mathf.RoundToInt(target.x - c.x) == dx && Mathf.RoundToInt(target.z - c.z) == dz) ch = 'B';
                    else if (!Physics.Raycast(new Vector3(p.x, c.y + 3f, p.z), Vector3.down, out RaycastHit hit, 8f, Ground, QueryTriggerInteraction.Ignore)) ch = ' ';
                    else
                    {
                        Vector3 feet = hit.point;
                        bool door = Physics.OverlapBox(feet + Vector3.up * 1f, new Vector3(0.5f, 0.8f, 0.5f), Quaternion.identity, Solid, QueryTriggerInteraction.Ignore).Any(x => x.GetComponentInParent<Door>() != null);
                        ch = door ? 'D' : !Steer.Clear(feet, me) ? 'X' : !Room(me, feet) ? '#' : Mathf.Abs(feet.y - c.y) > 2.5f ? ':' : way.Contains(new Vector2Int(dx, dz)) ? '*' : '.';
                    }
                    row.Append(ch);
                }
                rows.Add(row.ToString());
            }
            return rows;
        }

        /// <summary>Room for its body there (a door counts as room: it opens it).</summary>
        internal static bool Room(Humanoid me, Vector3 feet)
        {
            int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.55f, feet + Vector3.up * 1.6f, 0.3f, Hits, Solid, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i];
                if (c.transform.root == me.transform.root || c.GetComponentInParent<Door>() != null || c.GetComponentInParent<Character>() != null || c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return false;
            }
            return true;
        }
    }
}
