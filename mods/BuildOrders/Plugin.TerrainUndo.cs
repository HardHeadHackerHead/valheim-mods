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

        private static string UndoFile(string key)
        {
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "world";
            string safe = new string((world + "__" + key).Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            return Path.Combine(TerrainDir, safe + ".json");
        }

        /// <summary>Save the terrain edits under the spots about to be levelled (kept from the first time, if levelled again).</summary>
        private void SnapshotTerrain(string key, List<Vector3> points)
        {
            try
            {
                if (points.Count == 0) return;
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

                var maps = new List<Heightmap>();
                foreach (Vector3 p in points) Heightmap.FindHeightmap(p, pad, maps);
                foreach (Heightmap hm in maps.Distinct())
                {
                    TerrainComp tc = hm.GetAndCreateTerrainCompiler();
                    if (tc == null || !TcInit(tc)) continue;
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
        private string RestoreTerrain(string key, bool nothingBuilt = false)
        {
            string path = UndoFile(key);
            if (!File.Exists(path)) return null;
            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(path));
                // pieces of the plan that are already built: keep the level ground under them
                int built = 0;
                var near = new List<Piece>();
                foreach (JArray o in nothingBuilt ? new List<JArray>() : ((JArray)doc["orders"] ?? new JArray()).OfType<JArray>().ToList())
                {
                    var pos = new Vector3((float)o[1], (float)o[2], (float)o[3]);
                    if (_orders.Values.Any(x => x.By == key && x.Prefab == (string)o[0] && (x.Pos - pos).sqrMagnitude < 0.04f)) continue; // still a ghost
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
                foreach (JObject rec in ((JArray)doc["comps"]).OfType<JObject>())
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
                return restored > 0 ? "The ground is back as it was before levelling" : null;
            }
            catch (Exception e) { Logger.LogWarning("Could not put the ground back: " + e.Message); return null; }
        }
    }
}
