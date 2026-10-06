using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Piece shapes (from the piece list the mod writes, _pieces.json) and a plan's footprint: the bottom corners of the pieces meant to stand
    /// on the ground, which is the area levelled when a plan is placed.
    /// </summary>
    public partial class Plugin
    {
        internal const string SupportIdPrefix = "s-"; // support posts placed by older versions (left out when a plan is moved)
        private const float GroundBand = 0.6f;        // feet this close to the blueprint's ground level count as "meant to stand on the ground"
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

        // ---- the footprint ----

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

    }
}
