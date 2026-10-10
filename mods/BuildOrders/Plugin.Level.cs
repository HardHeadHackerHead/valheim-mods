using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Level ground for a plan: while placing a blueprint, L flattens the ground under it to the plan's floor level (cutting the high side and
    /// filling the low side), so it needs fewer posts. It starts as soon as the plan is placed and uses the hoe's own "Level ground" operation
    /// spot by spot (no hoe or stamina needed). Warded ground, places the game keeps unbuildable and the ground on and next to pieces already
    /// standing there are left alone.
    /// A plan waiting for its ground is kept in a file per world (BepInEx/blueprints/_levelling), so it still appears after you walk away, die,
    /// log out or restart: the levelling carries on when you are back in that world.
    /// </summary>
    public partial class Plugin
    {
        private const string LevelPiece = "mud_road_v2";   // the hoe's "Level ground"
        private const float TerrainLimit = 8f;              // the game lets ground be raised or lowered this far from where it started
        private const float LevelMargin = 1f;               // flatten this far beyond the plan's bottom corners

        internal class LevelJob
        {
            public string Key, Title;
            public string World;                  // the world it levels (never another)
            public bool Interrupted;              // stopped because you died or left, not by you: carries on by itself
            public List<Vector3> Points;
            public List<Vector3> Checks;          // where flatness is measured after each pass (the spots and the points between them)
            public int Next, Done, Skipped, Pass = 1;
            public bool Running, Finished;
            public float Worst;                   // how far the ground was off at the last check
            public string Status = "";
            public System.Action OnDone;
            public Vector3 Anchor;
            public float Yaw, Target;
            public Vector2 Lo, Hi;                 // the area, in the plan's own frame          // what to do once the ground is level (place the plan's ghosts)
        }

        private const int MaxPasses = 4;
        private const float FlatEnough = 0.06f;   // metres

        private readonly Dictionary<string, LevelJob> _levelJobs = new Dictionary<string, LevelJob>();
        internal LevelJob ActiveLevelJob => _levelJobs.Values.FirstOrDefault(j => j.Running);

        private float _levelRadius = -1f;
        private bool _levelSquare;

        private GameObject _levelPrefab;

        /// <summary>The hoe's Level ground piece. Terrain pieces are not in the scene's prefab list, only in the hoe's build list.</summary>
        private GameObject LevelPrefab()
        {
            if (_levelPrefab != null) return _levelPrefab;
            if (ZNetScene.instance != null) _levelPrefab = ZNetScene.instance.GetPrefab(LevelPiece);
            if (_levelPrefab == null && ObjectDB.instance != null)
                foreach (GameObject item in ObjectDB.instance.m_items)
                {
                    PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                    GameObject found = table != null ? table.m_pieces.FirstOrDefault(p => p != null && p.name == LevelPiece) : null;
                    if (found != null) { _levelPrefab = found; break; }
                }
            return _levelPrefab;
        }

        private void ReadLevelOp()
        {
            if (_levelRadius > 0f) return;
            _levelRadius = 2f;
            GameObject prefab = LevelPrefab();
            TerrainOp op = prefab != null ? prefab.GetComponent<TerrainOp>() : null;
            object settings = op != null ? AccessTools.Field(typeof(TerrainOp), "m_settings")?.GetValue(op) : null;
            if (settings == null) return;
            if (AccessTools.Field(settings.GetType(), "m_levelRadius")?.GetValue(settings) is float r && r > 0.2f) _levelRadius = r;
            if (AccessTools.Field(settings.GetType(), "m_square")?.GetValue(settings) is bool sq) _levelSquare = sq;
        }

        /// <summary>The spots to level, in the world, covering the plan's bottom (its ground-level corners plus a margin), all at the target height.</summary>
        private List<Vector3> LevelPoints(List<Entry> entries, List<Foot> feet, Vector3 anchor, float yaw, float targetY)
        {
            ReadLevelOp();
            // everything that touches the ground: the bottom corners of the plan, and every ground-following piece (walls round a keep...)
            var pts = feet.Select(f => new Vector2(f.Local.x, f.Local.z)).ToList();
            var shapes = Shapes();
            foreach (Entry e in entries)
            {
                if (!e.Ground && e.Local.y >= 1f) continue;
                pts.Add(new Vector2(e.Local.x, e.Local.z));
                if (e.Ground && shapes.TryGetValue(e.Prefab, out Shape s))
                    foreach (Vector3 f in s.Feet) { Vector3 p = e.Local + e.Rot * f; pts.Add(new Vector2(p.x, p.z)); }
            }
            var list = new List<Vector3>();
            if (pts.Count == 0) return list;
            float x0 = pts.Min(p => p.x) - LevelMargin, x1 = pts.Max(p => p.x) + LevelMargin;
            float z0 = pts.Min(p => p.y) - LevelMargin, z1 = pts.Max(p => p.y) + LevelMargin;
            float step = Mathf.Max(0.75f, _levelRadius * (_levelSquare ? 1.2f : 1.0f)); // overlapping, so no seams are left between strokes
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / step)), nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / step));
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                {
                    Vector3 p = anchor + turn * new Vector3(Mathf.Lerp(x0, x1, i / (float)nx), 0f, Mathf.Lerp(z0, z1, k / (float)nz));
                    p.y = targetY;
                    list.Add(p);
                }
            return list.Take(4000).ToList();     // a big site (a 40 m churchyard at a 1 m stroke) needs a couple of thousand
        }

        /// <summary>The ground there once levelled to targetY (as far as the game allows ground to move).</summary>
        private static float LevelledGround(float ground, float targetY) => Mathf.Clamp(targetY, ground - TerrainLimit, ground + TerrainLimit);

        private static void LevelStats(List<Vector3> points, out int strokes, out float cut, out float fill)
        {
            strokes = 0; cut = 0f; fill = 0f;
            foreach (Vector3 p in points)
            {
                float d = ZoneSystem.instance.GetGroundHeight(p) - p.y;
                if (Mathf.Abs(d) < 0.08f) continue;
                strokes++;
                if (d > 0f) cut = Mathf.Max(cut, d); else fill = Mathf.Max(fill, -d);
            }
        }


        private const float LevelBlend = 3f;      // metres of gentle slope from the levelled area back to the natural ground
        private const string PaintPiece = "path_v2"; // the hoe's Pathen: paints dirt (so grass does not grow through floors), leaves heights alone

        internal void StartLevel(string key, string title, List<Vector3> points, Vector3 anchor, float yaw, System.Action onDone = null)
        {
            StopLevel(key);
            if (points.Count == 0) { onDone?.Invoke(); return; }
            // the area in the plan's own frame: the rectangle the spots cover
            Quaternion back = Quaternion.Inverse(Quaternion.Euler(0f, yaw, 0f));
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (Vector3 p in points) { Vector3 l = back * (p - anchor); lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); }
            var job = new LevelJob
            {
                Key = key, Title = title, World = _loadedWorld, Points = points, Checks = points, Running = true, OnDone = onDone,
                Anchor = anchor, Yaw = yaw, Target = points[0].y, Lo = new Vector2(lo.x, lo.z), Hi = new Vector2(hi.x, hi.z),
            };
            _levelJobs[key] = job;
            Logger.LogInfo($"Levelling '{title}': {hi.x - lo.x:0}x{hi.z - lo.z:0} m to height {job.Target:0.00}");
            StartCoroutine(RunLevel(job));
        }

        internal void StopLevel(string key)
        {
            if (_levelJobs.TryGetValue(key, out LevelJob job)) { job.Running = false; job.Interrupted = false; job.Status = "stopped"; }
        }

        internal void ResumeLevel(string key)
        {
            if (!_levelJobs.TryGetValue(key, out LevelJob job) || job.Running || job.Finished) return;
            job.Running = true;
            job.Interrupted = false;
            StartCoroutine(RunLevel(job));
        }

        /// <summary>
        /// Level the ground the way the game's own level operation does (no tool in the game uses it: the hoe only smooths, at most 1 m):
        /// every ground point inside the area is set to the target height (as far as the game's 8 m limit allows), with a smooth slope back
        /// to the natural ground around it. Then the hoe's path stroke paints it as dirt.
        /// </summary>
        private IEnumerator RunLevel(LevelJob job)
        {
            bool Current() => job.Running && _levelJobs.TryGetValue(job.Key, out LevelJob current) && current == job && WorldKnown && job.World == _loadedWorld;
            Quaternion back = Quaternion.Inverse(Quaternion.Euler(0f, job.Yaw, 0f));
            if (!TerrainAccess())
            {
                // this game version's ground can't be written: the plan goes down on the ground as it is
                job.Running = false; job.Finished = true; job.Next = job.Points.Count; job.Status = "the ground can't be levelled in this game version";
                System.Action placeAnyway = job.OnDone;
                job.OnDone = null;
                placeAnyway?.Invoke();
                yield break;
            }

            // the ground is only loaded near you: wait for it, and for everything standing on it (a zone's ground loads before its pieces,
            // and levelling must see every building there to leave its ground alone)
            while (Current())
            {
                Player p = Player.m_localPlayer;
                if (p == null || p.IsDead()) { job.Running = false; job.Interrupted = true; job.Status = "stopped: carries on when you are back"; yield break; }
                Vector3 flat = job.Anchor - p.transform.position; flat.y = 0f;
                if (flat.magnitude <= 60f && AreaReady(job.Points)) break;
                job.Status = flat.magnitude <= 60f ? "waiting for everything around it to load (come closer)" : "paused: walk back within 60 m";
                yield return new WaitForSeconds(1f);
            }
            if (!Current()) yield break;

            job.Status = "levelling";
            var maps = new List<Heightmap>();
            foreach (Vector3 pt in job.Points) Heightmap.FindHeightmap(pt, LevelBlend + 2f, maps);
            var wardCache = new Dictionary<long, bool>();
            bool Allowed(Vector3 w)
            {
                long cell = ((long)Mathf.FloorToInt(w.x / 2f) << 32) ^ (uint)Mathf.FloorToInt(w.z / 2f);
                if (!wardCache.TryGetValue(cell, out bool ok)) wardCache[cell] = ok = PrivateArea.CheckAccess(w, 0f, false) && !Location.IsInsideNoBuildLocation(w);
                return ok;
            }
            // the ground on and next to pieces already standing there (anyone's) is left as it is: they would lose their footing or be buried
            float reach = LevelBlend + _levelRadius + 3f;
            HashSet<long> taken = GroundUnderPieces(job.Points.Min(p => p.x) - reach, job.Points.Max(p => p.x) + reach, job.Points.Min(p => p.z) - reach, job.Points.Max(p => p.z) + reach);

            int changed = 0, blocked = 0;
            List<Heightmap> distinct = maps.Distinct().ToList();
            foreach (Heightmap hm in distinct)
            {
                TerrainComp tc = hm != null ? hm.GetAndCreateTerrainCompiler() : null;
                if (tc == null || !TcInit(tc)) continue;
                ZNetView view = TcView(tc);
                if (view == null || !view.IsValid()) continue;
                if (!view.IsOwner()) view.ClaimOwnership(); // only the owner may write the ground
                SnapshotTerrain(job.Key, job.Points, hm, tc); // first, so removing the plan can put the ground back (only now is it loaded)
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
                        Vector3 l = back * (w - job.Anchor);
                        // how far outside the area (0 inside): fully level inside, easing back to the natural ground over LevelBlend metres
                        float dx = Mathf.Max(job.Lo.x - l.x, 0f, l.x - job.Hi.x), dz = Mathf.Max(job.Lo.y - l.z, 0f, l.z - job.Hi.y);
                        float outside = Mathf.Sqrt(dx * dx + dz * dz);
                        if (outside >= LevelBlend) continue;
                        if (!Allowed(w) || taken.Contains(Cell(w.x, w.z))) { blocked++; continue; }
                        float t = outside <= 0f ? 1f : Mathf.SmoothStep(1f, 0f, outside / LevelBlend);
                        float height = hm.GetHeight(j, i) + origin.y;          // the ground there now
                        float want = Mathf.Lerp(height, job.Target, t);
                        if (Mathf.Abs(want - height) < 0.005f) continue;
                        int k = i * pitch + j;
                        level[k] = Mathf.Clamp(level[k] + smooth[k] + (want - height), -TerrainLimit, TerrainLimit);
                        smooth[k] = 0f;
                        modH[k] = true;
                        any = true; changed++;
                    }
                if (any)
                {
                    TcSave?.Invoke(tc, new object[] { false });
                    hm.Poke(0, false); // rebuild the ground now
                    ClutterSystem.instance?.ResetGrass(hm.transform.position, hm.m_width * hm.m_scale / 2f);
                }
                RecordLevelled(job.Key, hm, tc); // what the levelling left, so the undo only puts back ground nobody has worked since
                job.Next = Mathf.Min(job.Points.Count, job.Next + job.Points.Count / Mathf.Max(1, distinct.Count));
                yield return null;
                if (!Current()) yield break;
            }

            // dirt on top, with the hoe's paint-only stroke (a few a frame)
            GameObject paint = PaintPrefab();
            if (paint != null)
            {
                job.Status = "painting";
                int n = 0;
                TerrainOp stroke = paint.GetComponent<TerrainOp>();
                int r = Mathf.CeilToInt(stroke != null ? stroke.GetRadius() : 3f);
                bool NearPiece(Vector3 pt)
                {
                    // (dirt over cultivated ground would wither the crops planted there)
                    for (int dx = -r; dx <= r; dx++)
                        for (int dz = -r; dz <= r; dz++)
                            if (taken.Contains(Cell(pt.x + dx, pt.z + dz))) return true;
                    return false;
                }
                foreach (Vector3 pt in job.Points)
                {
                    if (!Allowed(pt) || NearPiece(pt)) continue;
                    Instantiate(paint, new Vector3(pt.x, job.Target, pt.z), Quaternion.identity);
                    if (++n % 8 == 0) yield return null;
                }
            }
            yield return null;

            // check: how far the ground under the area is from the target (more than 8 m away cannot be reached: posts make up the rest)
            job.Worst = 0f;
            foreach (Vector3 c in job.Points)
            {
                float d = Mathf.Abs(ZoneSystem.instance.GetGroundHeight(c) - job.Target);
                if (d < TerrainLimit - 0.1f) job.Worst = Mathf.Max(job.Worst, d);
            }
            job.Next = job.Points.Count;
            job.Done = changed; job.Skipped = blocked;
            job.Finished = true;
            job.Running = false;
            Logger.LogInfo($"Levelling '{job.Title}' finished: {changed} ground points set, ground within {job.Worst:0.00} m" + (blocked > 0 ? $", {blocked} points warded, not allowed or by a building" : ""));
            job.Status = blocked > 0 ? "done (some ground is warded, not allowed or by a building)" : "done";
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"Ground levelled for \"{job.Title}\"");
            System.Action done = job.OnDone;
            job.OnDone = null;
            done?.Invoke();
        }

        // ---- plans waiting for their ground (kept per world, so they are not lost) ----

        private static string PendingLevelDir => Path.Combine(BlueprintDir, "_levelling");
        private string PendingLevelFile => Path.Combine(PendingLevelDir, Safe(_loadedWorld ?? "world") + ".json");
        private JArray _pendingLevels = new JArray();

        private IEnumerable<string> PendingLevelKeys() => _pendingLevels.OfType<JObject>().Select(j => (string)j["key"]);

        private void LoadPendingLevels()
        {
            _pendingLevels = new JArray();
            try { if (File.Exists(PendingLevelFile)) _pendingLevels = JArray.Parse(File.ReadAllText(PendingLevelFile)); }
            catch (Exception e) { Logger.LogWarning("Could not read the plans waiting for their ground: " + e.Message); }
        }

        private void SavePendingLevels()
        {
            try
            {
                if (_pendingLevels.Count == 0) { File.Delete(PendingLevelFile); return; }
                Directory.CreateDirectory(PendingLevelDir);
                File.WriteAllText(PendingLevelFile, _pendingLevels.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception e) { Logger.LogWarning("Could not save the plans waiting for their ground: " + e.Message); }
        }

        /// <summary>Remember a plan waiting for its ground (everything needed to level it and place it again later).</summary>
        private void RememberPendingLevel(string title, string file, List<Entry> entries, Vector3 anchor, float yaw, float offset, float baseY)
        {
            ForgetPendingLevel(BlueprintPrefix + title, save: false);
            _pendingLevels.Add(new JObject
            {
                ["key"] = BlueprintPrefix + title, ["title"] = title, ["file"] = file, ["anchor"] = new JArray(anchor.x, anchor.y, anchor.z),
                ["yaw"] = yaw, ["offset"] = offset, ["baseY"] = baseY,
                ["pieces"] = new JArray(entries.Select(e =>
                {
                    Vector3 r = e.Rot.eulerAngles;
                    return new JObject { ["p"] = e.Prefab, ["x"] = e.Local.x, ["y"] = e.Local.y, ["z"] = e.Local.z, ["rx"] = r.x, ["ry"] = r.y, ["rz"] = r.z, ["g"] = e.Ground };
                })),
            });
            SavePendingLevels();
        }

        private void ForgetPendingLevel(string key, bool save = true)
        {
            List<JObject> gone = _pendingLevels.OfType<JObject>().Where(j => (string)j["key"] == key).ToList();
            if (gone.Count == 0) return;
            foreach (JObject j in gone) j.Remove();
            if (save) SavePendingLevels();
        }

        /// <summary>Level the ground for a waiting plan, then place its ghosts (only in the world it was placed in).</summary>
        private void StartPlanLevel(JObject p)
        {
            string title = (string)p["title"], file = (string)p["file"], key = BlueprintPrefix + title, world = _loadedWorld;
            List<Entry> entries = EntriesFrom((JArray)p["pieces"]);
            var a = (JArray)p["anchor"];
            var anchor = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            float yaw = (float)p["yaw"], offset = (float)p["offset"], baseY = (float)p["baseY"];
            List<Vector3> points = LevelPoints(entries, GroundFeet(entries), anchor, yaw, baseY);
            StartLevel(key, title, points, anchor, yaw, () =>
            {
                Player me = Player.m_localPlayer;
                if (me == null || !WorldKnown || _loadedWorld != world) return; // never into another world: it waits for you to be back in its own
                ForgetPendingLevel(key);
                PlaceEntries(me, title, file, entries, anchor, yaw, offset, fixedBaseY: baseY, fresh: false);
            });
        }

        /// <summary>
        /// Every few seconds in a world: start the waiting plans with no levelling going (just loaded, or their ghosts could not be placed), and
        /// carry on with levelling that stopped because you died or left.
        /// </summary>
        private void UpdatePendingLevels(Player player)
        {
            foreach (JObject p in _pendingLevels.OfType<JObject>().ToList())
                if (!_levelJobs.TryGetValue((string)p["key"], out LevelJob job) || (job.Finished && job.OnDone == null)) StartPlanLevel(p);
            if (player.IsDead()) return;
            foreach (LevelJob job in _levelJobs.Values.Where(j => j.Interrupted && !j.Running && !j.Finished).ToList()) ResumeLevel(job.Key);
        }

        private GameObject _paintPrefab;

        private GameObject PaintPrefab()
        {
            if (_paintPrefab != null) return _paintPrefab;
            if (ObjectDB.instance != null)
                foreach (GameObject item in ObjectDB.instance.m_items)
                {
                    PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                    GameObject found = table != null ? table.m_pieces.FirstOrDefault(p => p != null && p.name == PaintPiece) : null;
                    if (found != null) { _paintPrefab = found; break; }
                }
            return _paintPrefab;
        }

        private static readonly Color PadColor = new Color(0.85f, 0.65f, 0.35f);

        /// <summary>While placing with Level ground on: the spots to level, what it takes, and a flat pad showing where the ground will be.</summary>
        private void UpdatePreviewLevel(Placement pl, Vector3 anchor, float baseY)
        {
            pl.LevelSpots = LevelPoints(pl.Entries, pl.Feet, anchor, pl.Yaw, baseY);
            LevelStats(pl.LevelSpots, out pl.Strokes, out pl.Cut, out pl.Fill);
            if (pl.LevelSpots.Count == 0) return;

            if (pl.Pad == null)
            {
                pl.Pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pl.Pad.name = "level_pad_preview";
                Destroy(pl.Pad.GetComponent<Collider>());
                Shader shader = GhostShader();
                if (shader != null)
                {
                    var mat = new Material(shader);
                    pl.Materials.Add(mat);
                    pl.Pad.GetComponent<Renderer>().sharedMaterial = mat;
                }
                TintWith(pl.Pad, new Color(PadColor.r, PadColor.g, PadColor.b, 0.35f));
            }
            if (!pl.Pad.activeSelf) pl.Pad.SetActive(true);
            // the pad covers the spots' rectangle in the plan's own frame
            Quaternion turn = Quaternion.Euler(0f, pl.Yaw, 0f), back = Quaternion.Inverse(turn);
            Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
            foreach (Vector3 p in pl.LevelSpots) { Vector3 l = back * (p - anchor); lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); }
            Vector3 mid = (lo + hi) / 2f;
            pl.Pad.transform.SetPositionAndRotation(anchor + turn * new Vector3(mid.x, 0f, mid.z) + Vector3.up * (baseY - anchor.y - 0.05f), turn);
            pl.Pad.transform.localScale = new Vector3(hi.x - lo.x + 0.5f, 0.1f, hi.z - lo.z + 0.5f);
        }

        private string LevelBannerText(Placement pl)
        {
            if (pl.Strokes == 0) return "The ground here is already level";
            string limit = pl.Cut > TerrainLimit || pl.Fill > TerrainLimit ? $"   ·   more than {TerrainLimit:0} m: the game cannot move ground that far, find flatter ground" : "";
            return $"The ground is levelled when you place it: cutting up to {pl.Cut:0.0} m, filling up to {pl.Fill:0.0} m{limit}";
        }

        private void DrawLevelBanner(float sw)
        {
            LevelJob job = ActiveLevelJob;
            if (job == null || _placing != null) return;
            var r = new Rect(sw / 2f - 260f, 70f, 520f, 30f);
            Round(r, new Color(0.12f, 0.09f, 0.04f, 0.88f), 7f);
            Outline(r, new Color(0.85f, 0.65f, 0.35f, 0.9f), 7f);
            Label(r, $"LEVELLING \"{job.Title}\"   {(job.OnDone != null ? "(the ghosts appear when it is done)   " : "")}{job.Next}/{job.Points.Count}   {job.Status}   ({_blueprintKey.Value}: stop it in Placed plans)", _bold, new Color(1f, 0.85f, 0.6f), TextAnchor.MiddleCenter);
        }
    }
}
