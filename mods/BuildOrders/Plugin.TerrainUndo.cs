using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Undo for Level ground: before a plan's ground is levelled, the terrain edits under it are saved to a file (the game keeps terrain as
    /// edits per ground point: how far each was raised or lowered, and its paint), and so is what the levelling wrote there. Removing the
    /// plan, or moving it somewhere else, puts back only the points that are still exactly as the levelling left them and that no piece
    /// stands on or next to: later digging is never undone, and no other building on the pad loses its ground or gets buried. If pieces of
    /// the plan have already been built, the level ground is kept. Finishing a build never resets anything. When in doubt the level ground
    /// stays: leaving it does no harm, putting the ground back under a building would.
    /// The ground can only be read and written where it is loaded (near you), and only once everything standing there has loaded too (a
    /// zone's ground loads before its pieces, so until then "nothing is built here" can be wrong): a plan removed from far away has its
    /// ground put back the next time you are near it. Only the game of the player who placed the plan has its undo: when someone else
    /// removes it, or everything of it is built, that player's game notices its ghosts are gone and does the same (now if they are playing,
    /// otherwise next time).
    /// </summary>
    public partial class Plugin
    {
        // The game's private terrain fields, looked up the first time they are needed: if a game update renames one, levelling and its undo
        // switch off with a warning instead of the whole mod failing to load.
        private static AccessTools.FieldRef<TerrainComp, bool> TcInit;
        private static AccessTools.FieldRef<TerrainComp, int> TcWidth;
        private static AccessTools.FieldRef<TerrainComp, bool[]> TcModH;
        private static AccessTools.FieldRef<TerrainComp, float[]> TcLevel;
        private static AccessTools.FieldRef<TerrainComp, float[]> TcSmooth;
        private static AccessTools.FieldRef<TerrainComp, bool[]> TcModP;
        private static AccessTools.FieldRef<TerrainComp, Color[]> TcPaint;
        private static AccessTools.FieldRef<TerrainComp, Heightmap> TcHmap;
        private static AccessTools.FieldRef<TerrainComp, ZNetView> TcView;
        private static System.Reflection.MethodInfo TcSave;
        private static bool? _terrainAccess;

        /// <summary>True when this game version's ground can be read and written (worked out once).</summary>
        private static bool TerrainAccess()
        {
            if (_terrainAccess.HasValue) return _terrainAccess.Value;
            try
            {
                TcInit = AccessTools.FieldRefAccess<TerrainComp, bool>("m_initialized");
                TcWidth = AccessTools.FieldRefAccess<TerrainComp, int>("m_width");
                TcModH = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");
                TcLevel = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_levelDelta");
                TcSmooth = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_smoothDelta");
                TcModP = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedPaint");
                TcPaint = AccessTools.FieldRefAccess<TerrainComp, Color[]>("m_paintMask");
                TcHmap = AccessTools.FieldRefAccess<TerrainComp, Heightmap>("m_hmap");
                TcView = AccessTools.FieldRefAccess<TerrainComp, ZNetView>("m_nview");
                TcSave = AccessTools.Method(typeof(TerrainComp), "Save") ?? throw new MissingMethodException("TerrainComp", "Save");
                _terrainAccess = true;
            }
            catch (Exception e)
            {
                _terrainAccess = false;
                Log?.LogWarning("Level ground is off: this game version keeps its ground differently from what BuildOrders knows (" + e.Message + ")");
            }
            return _terrainAccess.Value;
        }

        private static string TerrainDir => Path.Combine(BlueprintDir, "_terrain");

        private string WorldPrefix => Safe((_loadedWorld ?? "world") + "__");

        private string UndoFile(string key) => Path.Combine(TerrainDir, Safe((_loadedWorld ?? "world") + "__" + key) + ".json");

        // ---- the ground near standing pieces: never levelled, never put back ----

        /// <summary>How far around a piece's own shape the ground is left alone (the ground under a piece is shaped by the points around it).</summary>
        private const float PieceMargin = 2f;

        private static long Cell(float x, float z) => ((long)Mathf.FloorToInt(x) << 32) ^ (uint)Mathf.FloorToInt(z);

        /// <summary>
        /// The 1 m ground cells inside the rectangle x0..x1, z0..z1 that a real piece stands on or next to: each piece's colliders, grown by
        /// <see cref="PieceMargin"/>. Every kind of piece counts (walls, floors, fences, crops, a cart), and only real ones: our own ghosts and
        /// pieces already taken down have no live network object.
        /// </summary>
        private static HashSet<long> GroundUnderPieces(float x0, float x1, float z0, float z1)
        {
            var cells = new HashSet<long>();
            var all = new List<Piece>();
            var centre = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            // (the game measures from the piece's centre in 3D: wide enough for any height, then the rectangle decides)
            Piece.GetAllPiecesInRadius(centre, (x1 - x0) + (z1 - z0) + 4000f, all);
            foreach (Piece p in all)
            {
                if (p == null) continue;
                ZNetView view = p.GetComponent<ZNetView>();
                if (view == null || !view.IsValid()) continue;
                Bounds shape = new Bounds(p.transform.position, Vector3.one);
                bool first = true;
                foreach (Collider c in p.GetComponentsInChildren<Collider>())
                {
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    if (first) { shape = c.bounds; first = false; } else shape.Encapsulate(c.bounds);
                }
                float ax = shape.min.x - PieceMargin, bx = shape.max.x + PieceMargin, az = shape.min.z - PieceMargin, bz = shape.max.z + PieceMargin;
                if (bx < x0 || ax > x1 || bz < z0 || az > z1) continue;
                ax = Mathf.Max(ax, x0 - 1f); bx = Mathf.Min(bx, x1 + 1f); az = Mathf.Max(az, z0 - 1f); bz = Mathf.Min(bz, z1 + 1f);
                for (int x = Mathf.FloorToInt(ax); x <= Mathf.FloorToInt(bx); x++)
                    for (int z = Mathf.FloorToInt(az); z <= Mathf.FloorToInt(bz); z++)
                        cells.Add(Cell(x, z));
            }
            return cells;
        }

        /// <summary>
        /// Everything around these spots has loaded: their zones and the zones next to them, ground and objects (the game loads a zone's
        /// ground first and its pieces a few at a time after it).
        /// </summary>
        private static bool AreaReady(IEnumerable<Vector3> spots)
        {
            if (ZNetScene.instance == null || ZoneSystem.instance == null) return false;
            var seen = new HashSet<long>();
            foreach (Vector3 p in spots)
            {
                if (!seen.Add(((long)Mathf.FloorToInt((p.x + 32f) / 64f) << 32) ^ (uint)Mathf.FloorToInt((p.z + 32f) / 64f))) continue; // one per zone
                if (!ZNetScene.instance.IsAreaReady(p)) return false;
            }
            return true;
        }

        /// <summary>
        /// Save the terrain edits of one piece of ground (a heightmap) under the spots about to be levelled, just before it is levelled for the
        /// first time (the ground is only there once it is loaded). Merged into the plan's file; points already saved are never overwritten.
        /// </summary>
        private void SnapshotTerrain(string key, List<Vector3> points, Heightmap hm, TerrainComp tc)
        {
            try
            {
                if (points.Count == 0 || hm == null || tc == null || !TcInit(tc)) return;
                ReadLevelOp();
                float pad = _levelRadius + 2.5f;
                float x0 = points.Min(p => p.x) - pad, x1 = points.Max(p => p.x) + pad, z0 = points.Min(p => p.z) - pad, z1 = points.Max(p => p.z) + pad;

                string path = UndoFile(key);
                JObject doc = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject
                {
                    ["plan"] = key,
                    ["orders"] = new JArray(_orders.Values.Where(o => o.By == key).Select(o => new JArray(o.Prefab, Math.Round(o.Pos.x, 2), Math.Round(o.Pos.y, 2), Math.Round(o.Pos.z, 2)))),
                    ["comps"] = new JArray(),
                };
                var comps = (JArray)doc["comps"];

                Vector3 hp = hm.transform.position;
                JObject rec = comps.OfType<JObject>().FirstOrDefault(c => Mathf.Abs((float)c["hx"] - hp.x) < 0.5f && Mathf.Abs((float)c["hz"] - hp.z) < 0.5f);
                if (rec == null)
                {
                    rec = new JObject { ["hx"] = hp.x, ["hz"] = hp.z, ["i"] = new JArray(), ["mh"] = new JArray(), ["ld"] = new JArray(), ["sd"] = new JArray(), ["mp"] = new JArray(), ["pc"] = new JArray() };
                    comps.Add(rec);
                }
                var known = new HashSet<int>(((JArray)rec["i"]).Select(t => (int)t));
                int width = TcWidth(tc), pitch = width + 1;
                float scale = hm.m_scale;
                bool[] modH = TcModH(tc), modP = TcModP(tc);
                float[] level = TcLevel(tc), smooth = TcSmooth(tc);
                Color[] paint = TcPaint(tc);
                for (int i = 0; i < modH.Length; i++)
                {
                    float wx = hp.x + (i % pitch - width / 2) * scale, wz = hp.z + (i / pitch - width / 2) * scale;
                    if (wx < x0 || wx > x1 || wz < z0 || wz > z1 || known.Contains(i)) continue;
                    ((JArray)rec["i"]).Add(i);
                    ((JArray)rec["mh"]).Add(modH[i] ? 1 : 0);
                    ((JArray)rec["ld"]).Add(Math.Round(level[i], 4));
                    ((JArray)rec["sd"]).Add(Math.Round(smooth[i], 4));
                    bool mp = i < modP.Length && modP[i];
                    ((JArray)rec["mp"]).Add(mp ? 1 : 0);
                    Color c = i < paint.Length ? paint[i] : Color.clear;
                    ((JArray)rec["pc"]).Add(new JArray(Math.Round(c.r, 3), Math.Round(c.g, 3), Math.Round(c.b, 3), Math.Round(c.a, 3)));
                }
                Directory.CreateDirectory(TerrainDir);
                File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception e) { Logger.LogWarning("Could not save the ground before levelling (removing the plan will not reset it): " + e.Message); }
        }

        /// <summary>
        /// Right after levelling one piece of ground: remember what the levelling left at each saved point, so putting the ground back can tell
        /// the points nobody has changed since from the ones someone dug, raised or levelled again (those are kept as they are).
        /// </summary>
        private void RecordLevelled(string key, Heightmap hm, TerrainComp tc)
        {
            string path = UndoFile(key);
            try
            {
                if (hm == null || tc == null || !TcInit(tc) || !File.Exists(path)) return;
                JObject doc = JObject.Parse(File.ReadAllText(path));
                Vector3 hp = hm.transform.position;
                JObject rec = ((JArray)doc["comps"] ?? new JArray()).OfType<JObject>().FirstOrDefault(c => Mathf.Abs((float)c["hx"] - hp.x) < 0.5f && Mathf.Abs((float)c["hz"] - hp.z) < 0.5f);
                if (rec == null) return;
                bool[] modH = TcModH(tc);
                float[] level = TcLevel(tc), smooth = TcSmooth(tc);
                JArray amh = new JArray(), ald = new JArray(), asd = new JArray();
                foreach (JToken t in (JArray)rec["i"])
                {
                    int i = (int)t;
                    bool ok = i >= 0 && i < modH.Length;
                    amh.Add(ok && modH[i] ? 1 : 0);
                    ald.Add(ok ? Math.Round(level[i], 4) : 0.0);
                    asd.Add(ok ? Math.Round(smooth[i], 4) : 0.0);
                }
                rec["amh"] = amh; rec["ald"] = ald; rec["asd"] = asd;
                File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception e) { Logger.LogWarning("Could not note the levelled ground (removing the plan will keep it level): " + e.Message); }
        }

        /// <summary>Remember which pieces a levelled plan has (levelling can come before its ghosts are placed).</summary>
        private void RecordSnapshotOrders(string key)
        {
            string path = UndoFile(key);
            if (!File.Exists(path)) return;
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(path));
                if (doc["orders"] is JArray have && have.Count > 0) return;
                doc["orders"] = new JArray(_orders.Values.Where(o => o.By == key).Select(o => new JArray(o.Prefab, Math.Round(o.Pos.x, 2), Math.Round(o.Pos.y, 2), Math.Round(o.Pos.z, 2))));
                File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception) { }
        }

        /// <summary>Put the ground under a removed plan back as it was before it was levelled. Returns a message, or null if there was nothing to do.</summary>
        /// <param name="nothingBuilt">the caller has just taken down everything built of it (pieces taken down on another player's game go a moment later)</param>
        private string RestoreTerrain(string key, bool nothingBuilt = false) => RestoreTerrainFile(UndoFile(key), key, nothingBuilt, out bool _);

        /// <summary>Keep the file as a pending restore under a name of its own (so a new plan of the same name cannot clear it), done by <see cref="RetryPendingTerrain"/>.</summary>
        private void MakePending(string path, JObject doc, string key)
        {
            doc["plan"] = key;
            File.WriteAllText(Path.Combine(TerrainDir, WorldPrefix + "pending_" + Guid_() + ".json"), doc.ToString(Newtonsoft.Json.Formatting.None));
            File.Delete(path);
        }

        /// <summary>
        /// Put back the ground saved in a file. Where it is not loaded (far from you), or what stands there has not all loaded yet, nothing can
        /// be read or written there, not even whether pieces of the plan are built: the file is kept as a pending restore and done once you are
        /// near it. Only points still exactly as the levelling left them, and with no piece on or next to them, are put back.
        /// </summary>
        private string RestoreTerrainFile(string path, string key, bool nothingBuilt, out bool restoredAny)
        {
            restoredAny = false;
            if (!File.Exists(path) || !TerrainAccess()) return null;
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(path));
                var comps = ((JArray)doc["comps"] ?? new JArray()).OfType<JObject>().ToList();
                var centres = comps.Select(rec => new Vector3((float)rec["hx"], 0f, (float)rec["hz"])).ToList();
                bool loaded = centres.All(hp =>
                {
                    TerrainComp at = TerrainComp.FindTerrainCompiler(hp);
                    ZNetView v = at != null && TcInit(at) ? TcView(at) : null;
                    return v != null && v.IsValid();
                }) && AreaReady(centres);
                bool pending = Path.GetFileName(path).StartsWith(WorldPrefix + "pending_");
                if (!loaded)
                {
                    if (!pending)
                    {
                        MakePending(path, doc, key);
                        Logger.LogInfo($"Ground under '{key}' is not loaded: put back once you are near it");
                    }
                    return "The ground there goes back as it was the next time you are near it";
                }

                // pieces of the plan that still stand: keep the level ground under them
                int built = 0;
                var near = new List<Piece>();
                foreach (JArray o in ((JArray)doc["orders"] ?? new JArray()).OfType<JArray>().ToList())
                {
                    var pos = new Vector3((float)o[1], (float)o[2], (float)o[3]);
                    if (!pending && _orders.Values.Any(x => x.By == key && x.Prefab == (string)o[0] && (x.Pos - pos).sqrMagnitude < 0.04f)) continue; // still a ghost (a pending plan's ghosts are gone: these would be a new plan's)
                    near.Clear();
                    Piece.GetAllPiecesInRadius(pos, 0.3f, near);
                    if (near.Any(pc => pc != null && Utils.GetPrefabName(pc.gameObject) == (string)o[0] && pc.GetComponent<ZNetView>() is ZNetView v && v.IsValid())) built++;
                }
                if (built > 0)
                {
                    if (nothingBuilt)
                    {
                        // just taken down, but the pieces are someone else's and go when their game says so: look again in a moment
                        if (!pending) MakePending(path, doc, key);
                        return "The ground goes back as it was once the pieces are down";
                    }
                    File.Delete(path);
                    return $"Kept the levelled ground: {built} piece(s) of it are already built";
                }

                // levelled by a version that did not note what it left: nothing can tell whether the ground was changed since, so it stays
                if (comps.Any(rec => !(rec["ald"] is JArray)))
                {
                    File.Delete(path);
                    Logger.LogInfo($"Ground under '{key}' kept level: it was levelled by an older BuildOrders, which did not note what it changed");
                    return "Kept the levelled ground (levelled by an older version: it cannot tell what changed there since)";
                }

                int restored = 0, underPieces = 0, changedSince = 0;
                foreach (JObject rec in comps)
                {
                    var hp = new Vector3((float)rec["hx"], 0f, (float)rec["hz"]);
                    TerrainComp tc = TerrainComp.FindTerrainCompiler(hp);
                    if (tc == null || !TcInit(tc)) continue;
                    ZNetView view = TcView(tc);
                    if (view == null || !view.IsValid()) continue;
                    Heightmap hm = TcHmap(tc);
                    int width = TcWidth(tc), pitch = width + 1;
                    float scale = hm != null ? hm.m_scale : 1f;
                    bool[] modH = TcModH(tc), modP = TcModP(tc);
                    float[] level = TcLevel(tc), smooth = TcSmooth(tc);
                    Color[] paint = TcPaint(tc);
                    var idx = (JArray)rec["i"]; var mh = (JArray)rec["mh"]; var ld = (JArray)rec["ld"]; var sd = (JArray)rec["sd"]; var mp = (JArray)rec["mp"]; var pc = (JArray)rec["pc"];
                    var amh = (JArray)rec["amh"]; var ald = (JArray)rec["ald"]; var asd = (JArray)rec["asd"];
                    if (ald.Count != idx.Count || amh == null || amh.Count != idx.Count || asd == null || asd.Count != idx.Count) { changedSince += idx.Count; continue; }

                    float WX(int i) => hp.x + (i % pitch - width / 2) * scale;
                    float WZ(int i) => hp.z + (i / pitch - width / 2) * scale;
                    var xs = idx.Select(t => WX((int)t)).ToList(); var zs = idx.Select(t => WZ((int)t)).ToList();
                    if (xs.Count == 0) continue;
                    HashSet<long> taken = GroundUnderPieces(xs.Min(), xs.Max(), zs.Min(), zs.Max());

                    int here = 0;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int i = (int)idx[k];
                        if (i < 0 || i >= modH.Length) continue;
                        if (taken.Contains(Cell(WX(i), WZ(i)))) { underPieces++; continue; }
                        // still exactly what the levelling left? (otherwise someone has worked the ground since: keep their work)
                        if (modH[i] != ((int)amh[k] == 1) || Mathf.Abs(level[i] - (float)ald[k]) > 0.01f || Mathf.Abs(smooth[i] - (float)asd[k]) > 0.01f) { changedSince++; continue; }
                        bool sameH = modH[i] == ((int)mh[k] == 1) && Mathf.Abs(level[i] - (float)ld[k]) < 0.0001f && Mathf.Abs(smooth[i] - (float)sd[k]) < 0.0001f;
                        if (!view.IsOwner()) view.ClaimOwnership(); // only the owner may write the ground
                        modH[i] = (int)mh[k] == 1;
                        level[i] = (float)ld[k];
                        smooth[i] = (float)sd[k];
                        if (i < modP.Length) modP[i] = (int)mp[k] == 1;
                        if (i < paint.Length) { var c = (JArray)pc[k]; paint[i] = new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]); }
                        here++;
                        if (!sameH) restored++;
                    }
                    if (here == 0) continue;
                    TcSave?.Invoke(tc, new object[] { false });
                    if (hm != null)
                    {
                        hm.Poke(0, false);
                        ClutterSystem.instance?.ResetGrass(hm.transform.position, hm.m_width * hm.m_scale / 2f);
                    }
                }
                File.Delete(path);
                Logger.LogInfo($"Ground under '{key}' put back as it was ({restored} ground points; kept level: {underPieces} by pieces, {changedSince} changed since)");
                restoredAny = restored > 0;
                string kept = underPieces > 0 ? " (except next to buildings standing there)" : changedSince > 0 ? " (except where it was dug or levelled since)" : "";
                if (restored > 0) return "The ground is back as it was before levelling" + kept;
                return underPieces + changedSince > 0 ? "Kept the levelled ground: buildings stand on it, or it was worked since" : null;
            }
            catch (Exception e) { Logger.LogWarning("Could not put the ground back: " + e.Message); return null; }
        }

        /// <summary>Pending restores (plans removed from far away): done once their ground is loaded. Called every few seconds while in a world.</summary>
        private void RetryPendingTerrain()
        {
            if (!Directory.Exists(TerrainDir)) return;
            foreach (string path in Directory.GetFiles(TerrainDir, WorldPrefix + "pending_*.json"))
            {
                string key;
                try { key = (string)JObject.Parse(File.ReadAllText(path))["plan"] ?? ""; }
                catch (Exception) { continue; }
                RestoreTerrainFile(path, key, false, out bool restored);
                if (restored) Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"The ground under \"{TitleOf(key)}\" is back as it was before levelling");
            }
        }

        private readonly Dictionary<string, string> _recordPlans = new Dictionary<string, string>(); // record file -> its plan (read once)

        /// <summary>
        /// Records of plans with no ghosts left (everything built, or removed by another player) that no levelling waits on: the piece list is
        /// not needed any more, and the ground undo is used as removing the plan would (built pieces keep their level ground; where nothing of it
        /// was built the ground goes back). Called every few seconds while in a world.
        /// </summary>
        private void SweepFinishedPlans()
        {
            var live = new HashSet<string>(_orders.Values.Select(o => o.By ?? ""));
            live.UnionWith(_levelJobs.Values.Where(j => !j.Finished).Select(j => j.Key));
            live.UnionWith(PendingLevelKeys());
            foreach (string dir in new[] { PlanRecordDir, TerrainDir })
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string path in Directory.GetFiles(dir, WorldPrefix + "*.json"))
                {
                    if (Path.GetFileName(path).StartsWith(WorldPrefix + "pending_")) continue;
                    if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < 30) continue; // just written: its orders may still be on the way
                    if (!_recordPlans.TryGetValue(path, out string key))
                    {
                        try { key = (string)JObject.Parse(File.ReadAllText(path))["plan"] ?? ""; }
                        catch (Exception) { key = ""; }
                        _recordPlans[path] = key;
                    }
                    if (key.Length == 0 || live.Contains(key)) continue;
                    // Add-on shells may have a cellar whose ground recovery belongs to the add-on.
                    // Keep the piece list so a completed shell can still be taken down after F6.
                    if (dir == PlanRecordDir && IsAddonGhostPlan(key) && BuiltPieces(key).Count > 0) continue;
                    _recordPlans.Remove(path);
                    if (dir == PlanRecordDir) { try { File.Delete(path); } catch (Exception) { } continue; }
                    RestoreTerrainFile(path, key, false, out bool restored);
                    if (restored) Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"\"{TitleOf(key)}\" was removed: the ground under it is back as it was");
                }
            }
        }

        private static string TitleOf(string key) => key.StartsWith(BlueprintPrefix) ? key.Substring(BlueprintPrefix.Length) : key;
    }
}
