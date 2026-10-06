using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Auto-supports: a blueprint is designed on level ground, but the world is not level. When it is placed, every bottom corner of a piece
    /// that was meant to stand on the ground (its feet are at the blueprint's ground level) and now hangs in the air, because the hill falls
    /// away or the player raised the plan, gets a post down to the ground (or to a building already there), in the piece's own material.
    /// The posts are part of the plan (shown in the preview, built like any ghost) and are worked out again whenever the plan moves.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<bool> _autoSupports;
        private ConfigEntry<float> _supportMaxHeight;

        internal const string SupportIdPrefix = "s-"; // build orders that are auto-supports (so moving a plan works them out afresh)
        private const float GroundBand = 0.6f;        // feet this close to the blueprint's ground level count as "meant to stand on the ground"
        private const float MinGap = 0.25f;           // a gap smaller than this is left alone (pieces may sit a little into or above the ground)

        private void BindSupportConfig()
        {
            _autoSupports = Config.Bind("Blueprints", "AutoSupports", true,
                "When a blueprint is placed on uneven ground (or raised), add posts under the pieces meant to stand on the ground, down to the ground.");
            _supportMaxHeight = Config.Bind("Blueprints", "SupportMaxHeight", 12f,
                new ConfigDescription("The tallest post auto-supports will add (metres). Wood and stone posts much taller than this do not hold.", new AcceptableValueRange<float>(2f, 30f)));
        }

        // ---- piece shapes, from the piece list the mod writes (_pieces.json) ----

        private class Shape
        {
            public string Material = "";
            public bool Supports;
            public List<Vector3> Feet = new List<Vector3>(); // the lowest snap points: the corners a piece stands on
            public float Top, Bottom;                         // highest and lowest snap (for posts: where they join)
            public bool IsPost;                               // a pole or pillar: just a top and a bottom, one above the other
            public Vector3 Min, Max;                          // its size around its origin
        }

        private Dictionary<string, Shape> _pieceShapes;

        private Dictionary<string, Shape> Shapes()
        {
            if (_pieceShapes != null) return _pieceShapes;
            var shapes = new Dictionary<string, Shape>();
            try
            {
                string path = Path.Combine(BlueprintDir, "_pieces.json");
                if (!File.Exists(path)) return shapes; // not written yet: try again later
                JObject doc = JObject.Parse(File.ReadAllText(path));
                foreach (JObject p in doc["pieces"].OfType<JObject>())
                {
                    var s = new Shape { Material = (string)p["material"] ?? "", Supports = (bool?)p["supports"] == true };
                    if (p["min"] is JArray mn && p["max"] is JArray mx && mn.Count >= 3 && mx.Count >= 3)
                    {
                        s.Min = new Vector3((float)mn[0], (float)mn[1], (float)mn[2]);
                        s.Max = new Vector3((float)mx[0], (float)mx[1], (float)mx[2]);
                    }
                    if (p["snaps"] is JObject sn && sn["top"] is JArray tp && sn["bottom"] is JArray bt && sn.Count == 2)
                        s.IsPost = Mathf.Abs((float)tp[0] - (float)bt[0]) < 0.05f && Mathf.Abs((float)tp[2] - (float)bt[2]) < 0.05f && (float)tp[1] - (float)bt[1] > 0.9f;
                    var snaps = (p["snaps"] as JObject)?.Properties().Select(q => q.Value as JArray).Where(a => a != null && a.Count >= 3)
                        .Select(a => new Vector3((float)a[0], (float)a[1], (float)a[2])).ToList() ?? new List<Vector3>();
                    if (snaps.Count > 0)
                    {
                        s.Top = snaps.Max(v => v.y);
                        s.Bottom = snaps.Min(v => v.y);
                        var low = snaps.Where(v => v.y < s.Bottom + 0.05f).ToList();
                        if (low.Count > 4)
                        {
                            // many points on the bottom (big stone pieces): the four outer corners are enough
                            var corners = new List<Vector3>();
                            foreach (var d in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
                                corners.Add(low.OrderByDescending(v => v.x * d.x + v.z * d.y).First());
                            low = corners;
                        }
                        foreach (Vector3 v in low) if (!s.Feet.Any(f => (f - v).sqrMagnitude < 0.01f)) s.Feet.Add(v);
                    }
                    shapes[(string)p["p"]] = s;
                }
            }
            catch (Exception e) { Logger.LogWarning("Auto-supports could not read the piece list: " + e.Message); }
            if (shapes.Count > 0) _pieceShapes = shapes;
            return shapes;
        }

        /// <summary>The posts for a material, longest first: (prefab, length between its end snaps, its top snap above its origin).</summary>
        private List<KeyValuePair<string, Vector2>> PostsFor(string material)
        {
            string[] names;
            switch (material)
            {
                case "HardWood": names = new[] { "wood_pole_log_4", "wood_pole_log" }; break;
                case "Stone": case "Marble": case "Ashstone": case "Ancient": case "Ice": names = new[] { "stone_pillar" }; break;
                case "Iron": names = new[] { "woodiron_pole" }; break;
                case "Timberwood": names = new[] { "stave_pole_2m" }; break;
                default: names = new[] { "wood_pole2", "wood_pole" }; break;
            }
            var shapes = Shapes();
            var list = new List<KeyValuePair<string, Vector2>>();
            foreach (string n in names)
                if (shapes.TryGetValue(n, out Shape s) && s.Top - s.Bottom > 0.4f && ZNetScene.instance.GetPrefab(n) != null)
                    list.Add(new KeyValuePair<string, Vector2>(n, new Vector2(s.Top - s.Bottom, s.Top)));
            if (list.Count == 0 && material != "Wood") return PostsFor("Wood");
            return list.OrderByDescending(kv => kv.Value.x).ToList();
        }

        // ---- which feet need a post ----

        internal class Foot
        {
            public Vector3 Local;     // in the blueprint's own frame (y relative to its ground level)
            public string Material;
        }

        /// <summary>The bottom corners of the pieces meant to stand on the ground (one per spot). Worked out once per placement.</summary>
        private List<Foot> GroundFeet(List<Entry> entries)
        {
            var feet = new List<Foot>();
            var shapes = Shapes();

            // every piece's box in the plan's frame, to tell whether a post stands on something of the plan
            var boxes = new List<KeyValuePair<Vector3, Vector3>>(entries.Count);
            foreach (Entry e in entries)
            {
                if (!shapes.TryGetValue(e.Prefab, out Shape s) || s.Max == s.Min) { boxes.Add(new KeyValuePair<Vector3, Vector3>(Vector3.zero, Vector3.zero)); continue; }
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = e.Local + e.Rot * new Vector3((c & 1) == 0 ? s.Min.x : s.Max.x, (c & 2) == 0 ? s.Min.y : s.Max.y, (c & 4) == 0 ? s.Min.z : s.Max.z);
                    lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner);
                }
                boxes.Add(new KeyValuePair<Vector3, Vector3>(lo, hi));
            }
            bool RestsOnPlan(Vector3 foot, int self)
            {
                for (int k = 0; k < entries.Count; k++)
                {
                    if (k == self) continue;
                    var b = boxes[k];
                    if (b.Key == b.Value) continue;
                    if (foot.x < b.Key.x - 0.05f || foot.x > b.Value.x + 0.05f || foot.z < b.Key.z - 0.05f || foot.z > b.Value.z + 0.05f) continue;
                    if (b.Value.y >= foot.y - 0.35f && b.Key.y < foot.y - 0.02f) return true; // it reaches up to where the post stands (or the post is set into it)
                }
                return false;
            }

            for (int idx = 0; idx < entries.Count; idx++)
            {
                Entry e = entries[idx];
                if (e.Ground || !shapes.TryGetValue(e.Prefab, out Shape s) || !s.Supports || s.Feet.Count == 0) continue;
                foreach (Vector3 f in s.Feet)
                {
                    Vector3 p = e.Local + e.Rot * f;
                    // pieces meant to stand on the ground (their feet at the plan's ground level), and any post that does not stand on
                    // something of the plan: a post is meant to reach the ground, however long the designer made it
                    bool groundLevel = Mathf.Abs(p.y) <= GroundBand;
                    bool loosePost = s.IsPost && !RestsOnPlan(p, idx);
                    if (!groundLevel && !loosePost) continue;
                    int same = feet.FindIndex(o => (o.Local.x - p.x) * (o.Local.x - p.x) + (o.Local.z - p.z) * (o.Local.z - p.z) < 0.09f);
                    if (same >= 0) { if (p.y < feet[same].Local.y) feet[same] = new Foot { Local = p, Material = s.Material }; continue; } // the lowest one in a column
                    feet.Add(new Foot { Local = p, Material = s.Material });
                }
            }
            return feet;
        }

        internal struct Post { public string Prefab; public Vector3 Pos; public Quaternion Rot; }

        private static readonly int SupportMask = LayerMask.GetMask("terrain", "piece", "static_solid");

        /// <summary>The posts needed for a plan at an anchor, turn and base height, and how many feet were too high to hold up.</summary>
        private List<Post> Supports(List<Foot> feet, Vector3 anchor, float yaw, float baseY, out int tooHigh, bool level = false)
        {
            var posts = new List<Post>();
            tooHigh = 0;
            if (!_autoSupports.Value || feet == null) return posts;
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            float max = _supportMaxHeight.Value;
            var cache = new Dictionary<string, List<KeyValuePair<string, Vector2>>>();
            foreach (Foot f in feet)
            {
                Vector3 top = anchor + turn * new Vector3(f.Local.x, 0f, f.Local.z);
                top.y = baseY + f.Local.y;
                float ground = ZoneSystem.instance.GetGroundHeight(top);
                if (level) ground = LevelledGround(ground, baseY); // the ground once levelled to the plan's floor
                if (Physics.Raycast(top + Vector3.down * 0.05f, Vector3.down, out RaycastHit hit, max + 2f, SupportMask, QueryTriggerInteraction.Ignore))
                    ground = Mathf.Max(ground, hit.point.y); // stand on a floor or rock that is already there
                float gap = top.y - ground;
                if (gap < MinGap) continue;
                if (gap > max) { tooHigh++; continue; }
                if (!cache.TryGetValue(f.Material, out var kinds)) cache[f.Material] = kinds = PostsFor(f.Material);
                if (kinds.Count == 0) continue;
                float y = top.y;
                for (int n = 0; n < 16 && y - ground > 0.05f; n++)
                {
                    float left = y - ground;
                    var kind = kinds.FirstOrDefault(k => k.Value.x <= left + 0.6f); // the longest that does not sink far into the ground
                    if (kind.Key == null) kind = kinds[kinds.Count - 1];
                    posts.Add(new Post { Prefab = kind.Key, Pos = new Vector3(top.x, y - kind.Value.y, top.z), Rot = turn });
                    y -= kind.Value.x;
                }
            }
            return posts;
        }

        /// <summary>What a list of posts costs, for the preview banner.</summary>
        private string PostCost(List<Post> posts)
        {
            if (posts.Count == 0) return "";
            return string.Join(", ", MaterialsOf(posts.Select(p => p.Prefab)).Select(kv => $"{kv.Value} {Localization.instance.Localize(kv.Key)}").ToArray());
        }
    }
}
