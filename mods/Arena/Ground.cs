using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Arena
{
    /// <summary>
    /// The ground the arena stands on, made ready by the first game to come near it (and kept so, saved with the world like any change to
    /// the land): the whole footprint is levelled to the arena's height, the fighting floor is shaped into its mounds and dips and painted as
    /// dirt, and the trees, rocks and bushes inside the footprint are taken away (again whenever more grow there).
    /// The ground is changed the way the game's own levelling does it (BuildOrders does the same), as far as the game allows ground to move (8 m).
    /// </summary>
    internal static class Ground
    {
        private const string DoneKey = "dharenaground";
        private const int Version = 2;   // (2: the forecourt painted too, so grass does not grow through its paving)
        private const float Blend = 5f, Limit = 8f;

        private static readonly AccessTools.FieldRef<TerrainComp, bool> TcInit = AccessTools.FieldRefAccess<TerrainComp, bool>("m_initialized");
        private static readonly AccessTools.FieldRef<TerrainComp, int> TcWidth = AccessTools.FieldRefAccess<TerrainComp, int>("m_width");
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> TcModH = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");
        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcLevel = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_levelDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcSmooth = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_smoothDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, ZNetView> TcView = AccessTools.FieldRefAccess<TerrainComp, ZNetView>("m_nview");
        private static readonly System.Reflection.MethodInfo TcSave = AccessTools.Method(typeof(TerrainComp), "Save");

        private static bool _running;
        private static float _nextClear, _nextTry;
        internal static string Status = "";

        internal static bool Done => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(DoneKey, out string v) && v == Version.ToString();

        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (!Site.Known || p == null) return;
            float away = new Vector2(p.transform.position.x - Site.Origin.x, p.transform.position.z - Site.Origin.z).magnitude;
            if (away < 150f && Time.time > _nextClear) { _nextClear = Time.time + 4f; Clear(); }
            if (!Done && !_running && away < 70f && Time.time > _nextTry) { _nextTry = Time.time + 20f; Plugin.Instance.StartCoroutine(Shape()); }
        }

        /// <summary>How high the ground is wanted at a point (in the arena's frame): the floor's shape inside the podium, level elsewhere.</summary>
        private static float Want(Vector3 local)
        {
            float r = new Vector2(local.x, local.z).magnitude;
            if (r < Layout.Podium) return Layout.FloorHeight(local.x, local.z);
            if (r > Layout.Footprint - 1f && local.z < Layout.CourtZ1) return -0.15f;    // under the forecourt's paving
            return -0.05f;
        }

        private static IEnumerator Shape()
        {
            _running = true;
            Status = "shaping the ground";
            try
            {
                var maps = new List<Heightmap>();
                var corners = new List<Vector3>();
                for (float x = -Layout.Footprint - Blend; x <= Layout.Footprint + Blend; x += 16f)
                    for (float z = Layout.CourtZ0 - Blend; z <= Layout.Footprint + Blend; z += 16f)
                        corners.Add(Site.World(new Vector3(x, 0f, z)));
                foreach (Vector3 c in corners) Heightmap.FindHeightmap(c, 12f, maps);
                List<Heightmap> distinct = maps.Where(m => m != null).Distinct().ToList();
                int changed = 0, owned = 0;
                foreach (Heightmap hm in distinct)
                {
                    TerrainComp tc = hm.GetAndCreateTerrainCompiler();
                    if (tc == null || !TcInit(tc)) continue;
                    ZNetView view = TcView(tc);
                    if (view == null || !view.IsValid()) continue;
                    if (!view.IsOwner()) view.ClaimOwnership();
                    owned++;
                    int width = TcWidth(tc), pitch = width + 1;
                    float scale = hm.m_scale;
                    Vector3 origin = tc.transform.position;
                    bool[] modH = TcModH(tc);
                    float[] level = TcLevel(tc), smooth = TcSmooth(tc);
                    bool any = false;
                    for (int i = 0; i < pitch; i++)
                        for (int j = 0; j < pitch; j++)
                        {
                            var w = new Vector3(origin.x + (j - width / 2) * scale, 0f, origin.z + (i - width / 2) * scale);
                            Vector3 local = Site.Local(new Vector3(w.x, Site.Origin.y, w.z));
                            float outside = Layout.OutsideBy(local);
                            if (outside >= Blend) continue;
                            float t = outside <= 0f ? 1f : Mathf.SmoothStep(1f, 0f, outside / Blend);
                            int k = i * pitch + j;
                            float height = hm.GetHeight(j, i) + origin.y;                 // the ground there now (with any change already made)
                            float natural = height - level[k] - smooth[k];                // the ground as the world made it
                            float goal = Mathf.Lerp(natural, Site.Origin.y + Want(local), t);
                            float delta = Mathf.Clamp(goal - natural, -Limit, Limit);
                            if (Mathf.Abs(level[k] + smooth[k] - delta) < 0.005f) continue;
                            level[k] = delta;
                            smooth[k] = 0f;
                            modH[k] = true;
                            any = true; changed++;
                        }
                    if (any)
                    {
                        TcSave?.Invoke(tc, new object[] { false });
                        hm.Poke(0, false);
                        ClutterSystem.instance?.ResetGrass(hm.transform.position, hm.m_width * hm.m_scale / 2f);
                    }
                }
                Plugin.Log.LogInfo($"Arena ground: {changed} ground points set in {owned} of {distinct.Count} pieces of land");
                if (owned == 0 || owned < distinct.Count) { Status = "waiting for the land to load"; _running = false; _nextTry = Time.time + 5f; yield break; }
            }
            finally { if (Status == "shaping the ground") Status = ""; }

            // dirt over the floor and under the building, with the hoe's paint-only stroke, a few a frame
            Status = "painting the floor";
            GameObject paint = PaintPrefab();
            if (paint != null)
            {
                int n = 0;
                for (float x = Mathf.Min(-Layout.Facade, Layout.CourtX0); x <= Mathf.Max(Layout.Facade, Layout.CourtX1); x += 1.6f)
                    for (float z = Layout.CourtZ0; z <= Layout.Facade; z += 1.6f)
                    {
                        Vector3 l = new Vector3(x, 0f, z);
                        bool court = x >= Layout.CourtX0 && x <= Layout.CourtX1 && z >= Layout.CourtZ0 && z <= Layout.CourtZ1;
                        if (!court && new Vector2(x, z).magnitude > Layout.Facade + 0.5f) continue;
                        Vector3 w = Site.World(new Vector3(x, Want(new Vector3(x, 0f, z)), z));
                        Object.Instantiate(paint, w, Quaternion.identity);
                        if (++n % 12 == 0) yield return null;
                    }
            }
            ZoneSystem.instance.SetGlobalKey(DoneKey + " " + Version);
            Status = "";
            _running = false;
            Plugin.Log.LogInfo("Arena ground is ready");
        }

        private static GameObject _paint;

        /// <summary>The hoe's "Pathen" stroke: paints dirt (so no grass grows through), leaves the height alone.</summary>
        private static GameObject PaintPrefab()
        {
            if (_paint != null) return _paint;
            if (ObjectDB.instance == null) return null;
            foreach (GameObject item in ObjectDB.instance.m_items)
            {
                PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                GameObject found = table != null ? table.m_pieces.FirstOrDefault(p => p != null && p.name == "path_v2") : null;
                if (found != null) { _paint = found; break; }
            }
            return _paint;
        }

        /// <summary>Takes away the trees, rocks, bushes and anything else the world grew inside the footprint.</summary>
        private static void Clear()
        {
            int gone = 0;
            var seen = new HashSet<GameObject>();
            void Take(Component c)
            {
                if (c == null || !seen.Add(c.gameObject)) return;
                GameObject go = c.gameObject;
                if (go.GetComponent<Piece>() != null || go.GetComponent<WearNTear>() != null || go.GetComponent<Character>() != null || go.GetComponent<ItemDrop>() != null) return;
                if (!Layout.InFootprint(Site.Local(go.transform.position), 1.5f)) return;
                ZNetView view = go.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) return;
                if (!view.IsOwner()) view.ClaimOwnership();
                ZNetScene.instance.Destroy(go);
                gone++;
            }
            foreach (TreeBase t in Object.FindObjectsByType<TreeBase>(FindObjectsSortMode.None)) Take(t);
            foreach (TreeLog t in Object.FindObjectsByType<TreeLog>(FindObjectsSortMode.None)) Take(t);
            foreach (MineRock t in Object.FindObjectsByType<MineRock>(FindObjectsSortMode.None)) Take(t);
            foreach (MineRock5 t in Object.FindObjectsByType<MineRock5>(FindObjectsSortMode.None)) Take(t);
            foreach (Destructible t in Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None)) Take(t);
            foreach (Pickable t in Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)) Take(t);
            if (gone > 0) Plugin.Log.LogInfo($"Cleared {gone} trees, rocks and plants from the arena's ground");
        }

        /// <summary>For testing: shape the ground again.</summary>
        internal static void Redo()
        {
            ZoneSystem.instance?.RemoveGlobalKey(DoneKey);
            _nextTry = 0f;
        }
    }
}
