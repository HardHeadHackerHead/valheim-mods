using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// Where the Arena stands in this world. The first time the world runs with the mod, the game hosting it picks a spot: flat, dry
    /// meadow (or forest) ground near the middle of the world, clear of anyone's buildings and of the game's own places, with its gate
    /// facing the world's spawn. The spot is kept in a world key ("dharena x,y,z,yaw"), saved with the world and known to everyone who joins.
    /// </summary>
    internal static class Site
    {
        private const string Key = "dharena";
        internal static bool Known;
        internal static Vector3 Origin;
        internal static float Yaw;
        internal static Quaternion Turn = Quaternion.identity;
        private static float _next, _spawnedAt;
        private static bool _choosing;
        private static Minimap.PinData _pin;

        internal static Vector3 World(Vector3 local) => Origin + Turn * local;
        internal static Quaternion World(Quaternion local) => Turn * local;
        internal static Vector3 Local(Vector3 world) => Quaternion.Inverse(Turn) * (world - Origin);
        internal static Vector3 Centre => Origin;

        internal static Vector3 Point(Layout.Mark m, float up = 0f) => World(m.Pos + Vector3.up * up);
        internal static Quaternion Facing(Layout.Mark m) => Turn * Quaternion.Euler(0f, m.Yaw, 0f);

        /// <summary>Is a world point on the fighting floor (inside the podium wall)?</summary>
        internal static bool OnFloor(Vector3 world, float margin = 0f)
        {
            if (!Known) return false;
            Vector3 l = Local(world);
            return new Vector2(l.x, l.z).magnitude <= Layout.Floor + margin && l.y > -3f && l.y < 6f;
        }

        internal static float Ground(Vector3 p, float near)
        {
            try { if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(new Vector3(p.x, near + 50f, p.z), out float h, 1000)) return h; }
            catch (Exception) { }
            return near;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            ZoneSystem zone = ZoneSystem.instance;
            if (zone == null || ZNet.instance == null) { Known = false; _pin = null; return; }
            if (zone.GetGlobalKey(Key, out string value) && Parse(value)) { Known = true; Pin(); return; }
            Known = false;
            // only the game hosting the world chooses, and only once the world (and its keys) are surely loaded
            bool settled = Player.m_localPlayer != null ? Time.time - _spawnedAt > 10f : ZNet.instance.IsDedicated() && Time.realtimeSinceStartup > 60f;
            if (Player.m_localPlayer == null) _spawnedAt = Time.time;
            if (ZNet.instance.IsServer() && settled && !_choosing && WorldGenerator.instance != null) Choose();
        }

        internal static void Forget() { Known = false; _pin = null; }

        private static bool Parse(string value)
        {
            string[] f = value.Split(',');
            if (f.Length < 4) return false;
            float[] n = new float[4];
            for (int i = 0; i < 4; i++) if (!float.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return false;
            Origin = new Vector3(n[0], n[1], n[2]);
            Yaw = n[3];
            Turn = Quaternion.Euler(0f, Yaw, 0f);
            return true;
        }

        private static void Pin()
        {
            if (!Plugin.MapPin.Value || Minimap.instance == null || _pin != null) return;
            _pin = Minimap.instance.AddPin(Origin, Minimap.PinType.Icon3, "The Arena", false, false, 0L);
        }

        internal static void Unpin()
        {
            if (_pin != null && Minimap.instance != null) Minimap.instance.RemovePin(_pin);
            _pin = null;
        }

        // ---- choosing the spot (on the game hosting the world) ------------------------------------------------------------

        internal static string Stats = "";

        private static void Choose(bool dryRun = false)
        {
            _choosing = true;
            int nBiome = 0, nQuick = 0, nFit = 0, nBuilt = 0, nPlace = 0, nAll = 0; float bestSpread = 999f;
            try
            {
                WorldGenerator gen = WorldGenerator.instance;
                float water = ZoneSystem.instance.m_waterLevel;
                List<Vector3> built = BuiltPlaces();
                var places = ZoneSystem.instance.GetLocationList().Select(l => new Vector4(l.m_position.x, l.m_position.y, l.m_position.z, l.m_location != null ? l.m_location.m_exteriorRadius : 20f)).ToList();
                Vector3 best = Vector3.zero; float bestYaw = 0f, bestScore = float.MaxValue; int found = 0;
                for (float r = 250f; r <= 4500f && found < 24; r += 45f)
                {
                    int around = Mathf.Max(12, Mathf.RoundToInt(2f * Mathf.PI * r / 70f));
                    for (int k = 0; k < around; k++)
                    {
                        float a = (k + (r % 90f) / 90f) / around * Mathf.PI * 2f;
                        float cx = Mathf.Sin(a) * r, cz = Mathf.Cos(a) * r;
                        nAll++;
                        Heightmap.Biome biome = gen.GetBiome(cx, cz);
                        if (biome != Heightmap.Biome.Meadows && biome != Heightmap.Biome.BlackForest) { nBiome++; continue; }
                        // a quick look first (the middle and four points round it), the full survey only for spots that pass
                        float h0 = gen.GetHeight(cx, cz);
                        if (h0 < water + 3f) continue;
                        bool rough = false;
                        for (int q = 0; q < 4 && !rough; q++)
                        {
                            float qa = q * Mathf.PI / 2f;
                            float hq = gen.GetHeight(cx + Mathf.Sin(qa) * 22f, cz + Mathf.Cos(qa) * 22f);
                            rough = hq < water + 2.5f || Mathf.Abs(hq - h0) > 5f;
                        }
                        if (rough) { nQuick++; continue; }
                        float yaw = Mathf.Atan2(cx, cz) * Mathf.Rad2Deg;   // the main gate (at -z) faces the middle of the world
                        bool fits = Fits(gen, cx, cz, yaw, water, out float target, out float spread);
                        if (spread > 0f) bestSpread = Mathf.Min(bestSpread, spread);
                        if (!fits) { nFit++; continue; }
                        var centre = new Vector3(cx, target, cz);
                        if (built.Any(p => (new Vector2(p.x - cx, p.z - cz)).magnitude < Layout.Footprint + 60f)) { nBuilt++; continue; }
                        if (places.Any(p => (new Vector2(p.x - cx, p.z - cz)).magnitude < Layout.Footprint + p.w + 12f)) { nPlace++; continue; }   // (clear of each place's own grounds)
                        float score = spread * 30f + r / 60f + (biome == Heightmap.Biome.Meadows ? 0f : 25f);
                        found++;
                        if (score < bestScore) { bestScore = score; best = centre; bestYaw = yaw; }
                    }
                }
                Stats = $"looked at {nAll}: not meadow/forest {nBiome}, steep {nQuick}, not flat enough {nFit} (flattest spread {bestSpread:0.0} m), near buildings {nBuilt}, near the game's places {nPlace}, good {found}";
                Plugin.Log.LogInfo("Arena site search: " + Stats);
                if (dryRun) return;
                if (bestScore == float.MaxValue) { Plugin.Log.LogWarning("The Arena found no flat ground to stand on in this world"); _next = Time.unscaledTime + 120f; return; }
                string v = string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3:0.0}", best.x, best.y, best.z, bestYaw);
                ZoneSystem.instance.SetGlobalKey(Key + " " + v);
                Plugin.Log.LogInfo($"The Arena will stand at {best} facing {bestYaw:0} (out of {found} good spots)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Choosing the Arena's spot failed: " + e); }
            finally { _choosing = false; }
        }

        /// <summary>Would the arena fit here: dry everywhere, and close enough to one height that the ground can be levelled (the game moves ground at most 8 m)?</summary>
        internal static bool Fits(WorldGenerator gen, float cx, float cz, float yaw, float water, out float target, out float spread)
        {
            target = 0f; spread = 0f;
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            var heights = new List<float>();
            foreach (float r in new[] { 0f, 8f, 16f, 24f, Layout.Footprint })
            {
                int n = r == 0f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r * 0.8f));
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    Vector3 p = new Vector3(cx, 0f, cz) + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    float h = gen.GetHeight(p.x, p.z);
                    if (h < water + 2.5f) return false;
                    heights.Add(h);
                }
            }
            for (float x = Layout.CourtX0; x <= Layout.CourtX1; x += 7f)
                for (float z = Layout.CourtZ0; z <= Layout.CourtZ1; z += 7f)
                {
                    Vector3 p = new Vector3(cx, 0f, cz) + turn * new Vector3(x, 0f, z);
                    float h = gen.GetHeight(p.x, p.z);
                    if (h < water + 2.5f) return false;
                    heights.Add(h);
                }
            heights.Sort();
            float lo = heights[0], hi = heights[heights.Count - 1];
            spread = hi - lo;
            if (spread > 9f) return false;
            target = heights[heights.Count / 2];
            return target - lo < 7.5f && hi - target < 7.5f;
        }

        /// <summary>Where players have built: every piece the world knows of (the hosting game knows them all).</summary>
        private static List<Vector3> BuiltPlaces()
        {
            var list = new List<Vector3>();
            if (ZDOMan.instance == null || ZNetScene.instance == null) return list;
            var all = AccessTools.Field(typeof(ZDOMan), "m_objectsByID").GetValue(ZDOMan.instance) as Dictionary<ZDOID, ZDO>;
            if (all == null) return list;
            var isPiece = new Dictionary<int, bool>();
            foreach (ZDO zdo in all.Values)
            {
                int hash = zdo.GetPrefab();
                if (!isPiece.TryGetValue(hash, out bool piece))
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(hash);
                    isPiece[hash] = piece = prefab != null && prefab.GetComponent<Piece>() != null && prefab.GetComponent<WearNTear>() != null;
                }
                if (piece) list.Add(zdo.GetPosition());
            }
            return list;
        }

        internal static void Scan() => Choose(true);

        /// <summary>For testing: move the arena (or put it here, where the player stands).</summary>
        internal static void Set(Vector3 at, float yaw)
        {
            float target = ZoneSystem.instance.GetGroundHeight(at);
            string v = string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00},{3:0.0}", at.x, target, at.z, yaw);
            ZoneSystem.instance.RemoveGlobalKey(Key);
            ZoneSystem.instance.RemoveGlobalKey("dharenaground");
            ZoneSystem.instance.SetGlobalKey(Key + " " + v);
            Unpin();
            _next = 0f;
        }
    }
}
