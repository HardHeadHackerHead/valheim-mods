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
    /// edits per ground point: how far each was raised or lowered, and its paint). Removing the plan, or moving it somewhere else, puts those
    /// points back exactly as they were, unless pieces of the plan have already been built there (then the level ground is kept, so nothing
    /// is left hanging or buried). Finishing a build never resets anything.
    /// The ground can only be read and written where it is loaded (near you): a plan removed from far away has its ground put back the next
    /// time you are near it. Only the game of the player who placed the plan has its undo: when someone else removes it, or everything of it
    /// is built, that player's game notices its ghosts are gone and does the same (now if they are playing, otherwise next time).
    /// </summary>
    public partial class Plugin
    {
        private static readonly AccessTools.FieldRef<TerrainComp, bool> TcInit = AccessTools.FieldRefAccess<TerrainComp, bool>("m_initialized");
        private static readonly AccessTools.FieldRef<TerrainComp, int> TcWidth = AccessTools.FieldRefAccess<TerrainComp, int>("m_width");
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> TcModH = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedHeight");
        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcLevel = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_levelDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, float[]> TcSmooth = AccessTools.FieldRefAccess<TerrainComp, float[]>("m_smoothDelta");
        private static readonly AccessTools.FieldRef<TerrainComp, bool[]> TcModP = AccessTools.FieldRefAccess<TerrainComp, bool[]>("m_modifiedPaint");
        private static readonly AccessTools.FieldRef<TerrainComp, Color[]> TcPaint = AccessTools.FieldRefAccess<TerrainComp, Color[]>("m_paintMask");
        private static readonly AccessTools.FieldRef<TerrainComp, Heightmap> TcHmap = AccessTools.FieldRefAccess<TerrainComp, Heightmap>("m_hmap");
        private static readonly AccessTools.FieldRef<TerrainComp, ZNetView> TcView = AccessTools.FieldRefAccess<TerrainComp, ZNetView>("m_nview");
        private static readonly System.Reflection.MethodInfo TcSave = AccessTools.Method(typeof(TerrainComp), "Save");

        private static string TerrainDir => Path.Combine(BlueprintDir, "_terrain");

        private string WorldPrefix => Safe((_loadedWorld ?? "world") + "__");

        private string UndoFile(string key) => Path.Combine(TerrainDir, Safe((_loadedWorld ?? "world") + "__" + key) + ".json");

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
        /// <param name="nothingBuilt">the caller knows nothing of it stands any more (just taken down: the pieces go at the end of the frame)</param>
        private string RestoreTerrain(string key, bool nothingBuilt = false) => RestoreTerrainFile(UndoFile(key), key, nothingBuilt, out bool _);

        /// <summary>
        /// Put back the ground saved in a file. Where it is not loaded (far from you) nothing can be read or written there, not even whether pieces
        /// of the plan are built: the file is kept as a pending restore under a name of its own (so a new plan of the same name cannot clear it),
        /// and done by <see cref="RetryPendingTerrain"/> once you are near it.
        /// </summary>
        private string RestoreTerrainFile(string path, string key, bool nothingBuilt, out bool restoredAny)
        {
            restoredAny = false;
            if (!File.Exists(path)) return null;
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(path));
                var comps = ((JArray)doc["comps"] ?? new JArray()).OfType<JObject>().ToList();
                bool loaded = comps.All(rec =>
                {
                    TerrainComp at = TerrainComp.FindTerrainCompiler(new Vector3((float)rec["hx"], 0f, (float)rec["hz"]));
                    ZNetView v = at != null && TcInit(at) ? TcView(at) : null;
                    return v != null && v.IsValid();
                });
                bool pending = Path.GetFileName(path).StartsWith(WorldPrefix + "pending_");
                if (!loaded)
                {
                    if (!pending)
                    {
                        doc["plan"] = key;
                        File.WriteAllText(Path.Combine(TerrainDir, WorldPrefix + "pending_" + Guid_() + ".json"), doc.ToString(Newtonsoft.Json.Formatting.None));
                        File.Delete(path);
                        Logger.LogInfo($"Ground under '{key}' is not loaded: put back once you are near it");
                    }
                    return "The ground there goes back as it was the next time you are near it";
                }

                // pieces of the plan that are already built: keep the level ground under them
                int built = 0;
                var near = new List<Piece>();
                foreach (JArray o in nothingBuilt ? new List<JArray>() : ((JArray)doc["orders"] ?? new JArray()).OfType<JArray>().ToList())
                {
                    var pos = new Vector3((float)o[1], (float)o[2], (float)o[3]);
                    if (!pending && _orders.Values.Any(x => x.By == key && x.Prefab == (string)o[0] && (x.Pos - pos).sqrMagnitude < 0.04f)) continue; // still a ghost (a pending plan's ghosts are gone: these would be a new plan's)
                    near.Clear();
                    Piece.GetAllPiecesInRadius(pos, 0.3f, near);
                    if (near.Any(pc => pc != null && Utils.GetPrefabName(pc.gameObject) == (string)o[0])) built++;
                }
                if (built > 0)
                {
                    File.Delete(path);
                    return $"Kept the levelled ground: {built} piece(s) of it are already built";
                }

                int restored = 0;
                foreach (JObject rec in comps)
                {
                    var hp = new Vector3((float)rec["hx"], 0f, (float)rec["hz"]);
                    TerrainComp tc = TerrainComp.FindTerrainCompiler(hp);
                    if (tc == null || !TcInit(tc)) continue;
                    ZNetView view = TcView(tc);
                    if (view == null || !view.IsValid()) continue;
                    if (!view.IsOwner()) view.ClaimOwnership();
                    bool[] modH = TcModH(tc), modP = TcModP(tc);
                    float[] level = TcLevel(tc), smooth = TcSmooth(tc);
                    Color[] paint = TcPaint(tc);
                    var idx = (JArray)rec["i"]; var mh = (JArray)rec["mh"]; var ld = (JArray)rec["ld"]; var sd = (JArray)rec["sd"]; var mp = (JArray)rec["mp"]; var pc = (JArray)rec["pc"];
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int i = (int)idx[k];
                        if (i < 0 || i >= modH.Length) continue;
                        modH[i] = (int)mh[k] == 1;
                        level[i] = (float)ld[k];
                        smooth[i] = (float)sd[k];
                        if (i < modP.Length) modP[i] = (int)mp[k] == 1;
                        if (i < paint.Length) { var c = (JArray)pc[k]; paint[i] = new Color((float)c[0], (float)c[1], (float)c[2], (float)c[3]); }
                        restored++;
                    }
                    TcSave?.Invoke(tc, new object[] { false });
                    Heightmap hm = TcHmap(tc);
                    if (hm != null)
                    {
                        hm.Poke(0, false);
                        ClutterSystem.instance?.ResetGrass(hm.transform.position, hm.m_width * hm.m_scale / 2f);
                    }
                }
                File.Delete(path);
                Logger.LogInfo($"Ground under '{key}' put back as it was ({restored} ground points)");
                restoredAny = restored > 0;
                return restored > 0 ? "The ground is back as it was before levelling" : null;
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
