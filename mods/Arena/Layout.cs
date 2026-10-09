using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// The Arena's layout: every game piece it is built from and the named points the mod uses (gates, pens, seats, the Hall of Fame, the
    /// Arena Master, where you arrive...), designed offline with the blueprint kit (design/arena.py) and carried in the mod as Layout.json.
    /// Positions are in the arena's own frame: metres from the middle of the fighting floor, y up from the levelled ground, z towards the
    /// Arena Master's box (the main gate is at -z).
    /// </summary>
    internal static class Layout
    {
        internal sealed class Part { public string Prefab; public Vector3 Pos; public Quaternion Rot; }
        internal sealed class Mark { public Vector3 Pos; public float Yaw; public int Piece = -1, Gate = -1; public bool Main; }

        internal static readonly List<Part> Parts = new List<Part>();
        internal static readonly Dictionary<string, List<Part>> Props = new Dictionary<string, List<Part>>();
        internal static readonly List<Mark> Gates = new List<Mark>(), Pens = new List<Mark>(), Seats = new List<Mark>(), Hall = new List<Mark>();
        internal static readonly List<Vector4> Bumps = new List<Vector4>();
        internal static Mark Master, Desk, MasterSpot, Arrival, MainGate, Tunnel, Upstairs, Stands, Thor, Freya;
        internal static float Floor = 14f, Podium = 15.2f, Facade = 27.2f, Top = 8.16f, Footprint = 30f;
        internal static float CourtX0, CourtX1, CourtZ0, CourtZ1;
        internal static bool Loaded;

        internal static void Load()
        {
            if (Loaded) return;
            using (Stream s = typeof(Layout).Assembly.GetManifestResourceStream("Layout.json"))
            {
                if (s == null) { Plugin.Log.LogError("Layout.json is missing from the mod"); return; }
                using (var reader = new StreamReader(s)) Parse(JObject.Parse(reader.ReadToEnd()));
            }
            Loaded = true;
            Plugin.Log.LogInfo($"Arena layout: {Parts.Count} pieces, {Seats.Count} seats, {Gates.Count} gates, {Props.Count} sets of cover");
        }

        private static void Parse(JObject j)
        {
            Floor = (float)j["floor"]; Podium = (float)j["podium"]; Facade = (float)j["facade"]; Top = (float)j["top"]; Footprint = (float)j["footprint"];
            JObject court = (JObject)j["forecourt"];
            CourtX0 = (float)court["x0"]; CourtX1 = (float)court["x1"]; CourtZ0 = (float)court["z0"]; CourtZ1 = (float)court["z1"];
            Parts.Clear(); Props.Clear(); Gates.Clear(); Pens.Clear(); Seats.Clear(); Hall.Clear(); Bumps.Clear();
            Parts.AddRange(((JArray)j["pieces"]).Select(ToPart));
            foreach (JProperty set in ((JObject)j["props"]).Properties()) Props[set.Name] = ((JArray)set.Value).Select(ToPart).ToList();
            foreach (JArray b in (JArray)j["bumps"]) Bumps.Add(new Vector4((float)b[0], (float)b[1], (float)b[2], (float)b[3]));
            JObject m = (JObject)j["markers"];
            Gates.AddRange(((JArray)m["gates"]).Select(t => ToMark((JObject)t)));
            Pens.AddRange(((JArray)m["pens"]).Select(t => ToMark((JObject)t)));
            Hall.AddRange(((JArray)m["hall"]).Select(t => ToMark((JObject)t)));
            foreach (JObject seat in (JArray)m["seats"])
            {
                float r = (float)seat["r"], a = (float)seat["a"] * Mathf.Deg2Rad;
                Seats.Add(new Mark { Pos = new Vector3(r * Mathf.Sin(a), (float)seat["y"], r * Mathf.Cos(a)), Yaw = (float)seat["a"] + 180f });
            }
            Master = ToMark((JObject)m["master"]); Desk = ToMark((JObject)m["desk"]); MasterSpot = ToMark((JObject)m["masterspot"]);
            Arrival = ToMark((JObject)m["arrival"]); MainGate = ToMark((JObject)m["maingate"]); Tunnel = ToMark((JObject)m["tunnel"]);
            Upstairs = ToMark((JObject)m["upstairs"]); Stands = ToMark((JObject)m["stands"]);
            Thor = m["thor"] is JObject th ? ToMark(th) : null; Freya = m["freya"] is JObject fr ? ToMark(fr) : null;
        }

        private static Part ToPart(JToken t) => new Part
        {
            Prefab = (string)t["p"],
            Pos = new Vector3((float)t["x"], (float)t["y"], (float)t["z"]),
            Rot = Quaternion.Euler((float?)t["rx"] ?? 0f, (float?)t["ry"] ?? 0f, (float?)t["rz"] ?? 0f),
        };

        private static Mark ToMark(JObject t) => new Mark
        {
            Pos = new Vector3((float)t["x"], (float?)t["y"] ?? 0f, (float)t["z"]), Yaw = (float?)t["yaw"] ?? 0f,
            Piece = (int?)t["piece"] ?? -1, Gate = (int?)t["gate"] ?? -1, Main = (bool?)t["main"] ?? false,
        };

        /// <summary>How high the fighting floor is at a point (its mounds and dips), flat near the wall so the gates open onto level ground.</summary>
        internal static float FloorHeight(float x, float z)
        {
            float r = Mathf.Sqrt(x * x + z * z);
            if (r >= Floor) return 0f;
            float h = 0f;
            foreach (Vector4 b in Bumps)
            {
                float dx = x - b.x, dz = z - b.y;
                h += b.w * Mathf.Exp(-(dx * dx + dz * dz) / (b.z * b.z));
            }
            return h * Mathf.SmoothStep(0f, 1f, (Floor - r) / 2.5f);
        }

        /// <summary>Is a point (in the arena's frame) inside what the arena stands on: the round building and its forecourt?</summary>
        internal static bool InFootprint(Vector3 local, float margin)
        {
            if (new Vector2(local.x, local.z).magnitude <= Footprint + margin) return true;
            return local.x >= CourtX0 - margin && local.x <= CourtX1 + margin && local.z >= CourtZ0 - margin && local.z <= CourtZ1 + margin;
        }

        /// <summary>How far a point is outside the footprint (0 inside).</summary>
        internal static float OutsideBy(Vector3 local)
        {
            float round = Mathf.Max(0f, new Vector2(local.x, local.z).magnitude - Footprint);
            float dx = Mathf.Max(CourtX0 - local.x, 0f, local.x - CourtX1), dz = Mathf.Max(CourtZ0 - local.z, 0f, local.z - CourtZ1);
            return Mathf.Min(round, Mathf.Sqrt(dx * dx + dz * dz));
        }
    }
}
