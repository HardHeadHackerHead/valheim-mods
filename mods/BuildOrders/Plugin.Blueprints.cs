using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Blueprints: a build described in a file (a list of pieces and where they go) becomes build orders, so the ghosts of a whole
    /// structure appear and you build them the usual way (walk up and press E, hold E to build many, fetch materials). Nothing is
    /// built for free: the pieces still cost what they cost.
    ///
    /// Files live in BepInEx/blueprints. A blueprint can place itself (anchor "bed", with "auto": true) or wait for the import key.
    /// The first time you are in a world the mod also writes blueprints/_pieces.json, the size and snap points of every buildable
    /// piece, which is what a blueprint designer needs to line pieces up.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<KeyCode> _blueprintKey;
        private ConfigEntry<bool> _blueprintAuto;
        private float _nextBlueprintScan;
        private bool _dumpStarted;

        private static string BlueprintDir => Path.Combine(Paths.BepInExRootPath, "blueprints");

        private void BindBlueprintConfig()
        {
            _blueprintKey = Config.Bind("Blueprints", "ImportKey", KeyCode.F11,
                "Opens the Plans window: place blueprints from BepInEx/blueprints with a preview, see and remove placed plans, change build settings. (Blueprints marked auto place themselves.)");
            _blueprintAuto = Config.Bind("Blueprints", "AllowAutoPlace", true,
                "Let blueprint files that say \"auto\": true place their ghosts by themselves (at your bed, or at coordinates the file gives).");
            BindSupportConfig();
            BindLevelConfig();
            BindEyesConfig();
        }

        private void UpdateBlueprints(Player player)
        {
            if (!_dumpStarted) { _dumpStarted = true; WriteGuides(); StartCoroutine(DumpPieces()); }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); // Ctrl + the key is the survey
            if (!ctrl && Input.GetKeyDown(_blueprintKey.Value) && (PlansWindowOpen || Placing || !TypingOrMenuOpen())) TogglePlansWindow();
            if (Input.GetKeyDown(KeyCode.Escape) && (PlansWindowOpen || Placing))
            {
                PlansPatches.EscapeFrame = Time.frameCount;
                if (Placing) { CancelPlacement(); player.Message(MessageHud.MessageType.TopLeft, "Placement cancelled"); }
                PlansWindowOpen = false;
            }
            UpdatePlacement(player);
            UpdateEyes(player);

            if (_blueprintAuto.Value && Time.time >= _nextBlueprintScan)
            {
                _nextBlueprintScan = Time.time + 5f;
                foreach (string file in PendingFiles())
                {
                    JObject doc = Read(file);
                    if (doc == null || !(bool?)doc["auto"] == true) continue;
                    if (Anchor(player, doc, out Vector3 at, out float yaw)) Import(player, file, doc, at, yaw, auto: true);
                }
            }
        }

        /// <summary>
        /// Put the design guide (CLAUDE.md, which Claude Code reads automatically when started in this folder) and the helper scripts next to the
        /// blueprints, so anyone who installs the mod can have an AI assistant design builds for them. Refreshed when the mod updates.
        /// </summary>
        private void WriteGuides()
        {
            try
            {
                string stampFile = Path.Combine(BlueprintDir, ".guide");
                var asm = typeof(Plugin).Assembly;
                // refreshed whenever the bundled files change (not only with the version number)
                long hash = 17;
                foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith("guide/")).OrderBy(n => n))
                    using (Stream s = asm.GetManifestResourceStream(name))
                    {
                        int b;
                        hash = hash * 31 + name.GetHashCode();
                        while ((b = s.ReadByte()) >= 0) hash = hash * 31 + b;
                    }
                string stamp = Version + " " + hash.ToString("x");
                if (File.Exists(stampFile) && File.ReadAllText(stampFile).Trim() == stamp) return;
                foreach (string name in asm.GetManifestResourceNames().Where(n => n.StartsWith("guide/")))
                {
                    string target = Path.Combine(BlueprintDir, name.Substring("guide/".Length).Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream s = asm.GetManifestResourceStream(name))
                    using (FileStream f = File.Create(target)) s.CopyTo(f);
                }
                File.WriteAllText(stampFile, stamp);
            }
            catch (Exception e) { Logger.LogWarning("Could not write the blueprint guide: " + e.Message); }
        }

        // ---- finding blueprints ----

        private static IEnumerable<string> PendingFiles()
        {
            if (!Directory.Exists(BlueprintDir)) return new string[0];
            return Directory.GetFiles(BlueprintDir, "*.json")
                .Where(f => !Path.GetFileName(f).StartsWith("_") && !File.Exists(f + ".imported"))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private JObject Read(string file)
        {
            try { return JObject.Parse(File.ReadAllText(file)); }
            catch (Exception e) { Logger.LogWarning("Could not read blueprint " + Path.GetFileName(file) + ": " + e.Message); return null; }
        }

        // ---- the key: place the next blueprint where you look ----

        private void ImportNext(Player player)
        {
            string file = PendingFiles().FirstOrDefault();
            if (file == null)
            {
                player.Message(MessageHud.MessageType.Center, "No new blueprints in BepInEx/blueprints");
                return;
            }
            JObject doc = Read(file);
            if (doc == null) { File.WriteAllText(file + ".imported", "unreadable"); return; }

            Vector3 at;
            float yaw = player.transform.eulerAngles.y;
            if (!LookPoint(player, out at)) at = player.transform.position + player.transform.forward * 6f;
            Import(player, file, doc, at, yaw, auto: false);
        }

        private static bool LookPoint(Player player, out Vector3 point)
        {
            point = Vector3.zero;
            if (GameCamera.instance == null) return false;
            Transform cam = GameCamera.instance.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 60f, LayerMask.GetMask("terrain", "Default", "static_solid", "Default_small"), QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        /// <summary>Where a blueprint that places itself goes: at your bed, or at coordinates the file gives.</summary>
        private static bool Anchor(Player player, JObject doc, out Vector3 at, out float yaw)
        {
            at = Vector3.zero;
            yaw = doc["yaw"] != null ? (float)doc["yaw"] : 0f;
            string anchor = (string)doc["anchor"] ?? "bed";

            if (anchor == "world" && doc["at"] is JArray w && w.Count >= 2)
            {
                at = new Vector3((float)w[0], 0f, (float)w[w.Count - 1]);
                return true;
            }
            if (anchor == "bed")
            {
                PlayerProfile profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                if (profile == null || !profile.HaveCustomSpawnPoint()) return false; // no bed yet: wait for one
                at = profile.GetCustomSpawnPoint();
                return true;
            }
            if (anchor == "player") { at = player.transform.position; return true; }
            return false;
        }

        // ---- turning the file into orders ----

        private int Import(Player player, string file, JObject doc, Vector3 anchor, float yaw, bool auto)
        {
            var pieces = doc["pieces"] as JArray;
            string title = (string)doc["name"] ?? Path.GetFileNameWithoutExtension(file);
            if (pieces == null || pieces.Count == 0) { File.WriteAllText(file + ".imported", "empty"); return 0; }
            Vector3 offset = doc["offset"] is JArray o && o.Count >= 3 ? new Vector3((float)o[0], (float)o[1], (float)o[2]) : Vector3.zero;
            Vector3 origin = anchor + Quaternion.Euler(0f, yaw, 0f) * new Vector3(offset.x, 0f, offset.z);
            return PlaceEntries(player, title, file, EntriesFrom(pieces), origin, yaw, offset.y, auto);
        }

        // ---- the piece list a blueprint designer needs ----

        /// <summary>Write the size and snap points of every buildable piece to blueprints/_pieces.json (once per mod version).</summary>
        private IEnumerator DumpPieces()
        {
            string path = Path.Combine(BlueprintDir, "_pieces.json");
            string stamp = Version;
            try
            {
                if (File.Exists(path) && File.ReadAllText(path, System.Text.Encoding.UTF8).Contains("\"dump\": \"" + stamp + "\"")) yield break;
            }
            catch (Exception) { }
            while (ZNetScene.instance == null || ObjectDB.instance == null || Player.m_localPlayer == null) yield return null;
            yield return new WaitForSeconds(3f);

            var seen = new HashSet<string>();
            var list = new JArray();
            int count = 0;
            foreach (string tool in new[] { "Hammer", "Hoe", "Cultivator" })
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(tool);
                PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                if (table == null) continue;
                foreach (GameObject prefab in table.m_pieces.ToList())
                {
                    if (prefab == null || !seen.Add(prefab.name)) continue;
                    JObject entry = Describe(prefab, tool);
                    if (entry != null) list.Add(entry);
                    if (++count % 15 == 0) yield return null; // a few at a time, so there is no stutter
                }
            }

            try
            {
                Directory.CreateDirectory(BlueprintDir);
                var doc = new JObject
                {
                    ["dump"] = stamp,
                    ["note"] = "Sizes are in metres from each piece's own origin (the point a blueprint's x, y, z puts at the spot); snaps are the points it clicks to.",
                    ["stations"] = new JObject(list.OfType<JObject>().Where(p => p["isStation"] != null).Select(p => new JProperty((string)p["p"], p["isStation"]))),
                    ["pieces"] = list,
                };
                File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.Indented));
                Logger.LogInfo($"Wrote {list.Count} pieces to {path}");
            }
            catch (Exception e) { Logger.LogWarning("Could not write the piece list: " + e.Message); }
        }

        private static readonly string[] RuleFlags =
        {
            "m_groundPiece", "m_waterPiece", "m_noInWater", "m_notOnWood", "m_notOnTiltingSurface", "m_inCeilingOnly", "m_notOnFloor", "m_onlyInTeleportArea",
            "m_allowedInDungeons", "m_clipEverything", "m_clipGround", "m_noClipping", "m_mustBeAboveConnected", "m_vegetationGroundOnly", "m_cultivatedGroundOnly", "m_isUpgrade",
        };

        /// <summary>
        /// Everything a blueprint designer needs about one piece, in the same form as the offline extractor (tools/blueprints/tools/extract_pieces.py):
        /// size, named snap points, the way it climbs, its solid boxes, material, cost, the station it needs and its placement rules.
        /// </summary>
        private static JObject Describe(GameObject prefab, string tool)
        {
            GameObject copy = null;
            try
            {
                ZNetView.m_forceDisableInit = true;
                copy = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, -5000f, 0f), Quaternion.identity);
                ZNetView.m_forceDisableInit = false;
                Vector3 origin = copy.transform.position;

                Bounds box = default;
                bool any = false;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r is LineRenderer || !r.enabled) continue;
                    if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds);
                }
                Piece piece = copy.GetComponent<Piece>();
                WearNTear wear = copy.GetComponent<WearNTear>();
                JArray V(Vector3 v) => new JArray(Math.Round(v.x, 3), Math.Round(v.y, 3), Math.Round(v.z, 3));

                // named snap points: the switched-off "$hud_snappoint_..." children
                var snaps = new JObject();
                var tops = new List<Vector3>(); var bottoms = new List<Vector3>();
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                {
                    if (!t.name.StartsWith("$hud_snappoint_")) continue;
                    string name = t.name.Replace("$hud_snappoint_", "");
                    Vector3 local = t.position - origin;
                    snaps[name] = V(local);
                    if (name.StartsWith("top")) tops.Add(local);
                    if (name.StartsWith("bottom")) bottoms.Add(local);
                }

                var boxes = new JArray();
                foreach (BoxCollider c in copy.GetComponentsInChildren<BoxCollider>())
                {
                    if (c.isTrigger || boxes.Count >= 24) continue;
                    Quaternion q = c.transform.rotation;
                    boxes.Add(new JObject
                    {
                        ["c"] = V(c.transform.TransformPoint(c.center) - origin),
                        ["s"] = V(Vector3.Scale(c.size, c.transform.lossyScale)),
                        ["r"] = new JArray(Math.Round(q.x, 4), Math.Round(q.y, 4), Math.Round(q.z, 4), Math.Round(q.w, 4)),
                    });
                }

                var entry = new JObject
                {
                    ["p"] = prefab.name,
                    ["title"] = piece != null ? piece.m_name : prefab.name,
                    ["tool"] = tool,
                    ["category"] = piece != null ? piece.m_category.ToString() : "",
                    ["material"] = wear != null ? wear.m_materialType.ToString() : "",
                    ["supports"] = wear != null && wear.m_supports,
                    ["groundOnly"] = piece != null && (piece.m_groundOnly || piece.m_groundPiece),
                    ["min"] = any ? V(box.min - origin) : null,
                    ["max"] = any ? V(box.max - origin) : null,
                    ["snaps"] = snaps,
                };
                if (boxes.Count > 0) entry["boxes"] = boxes;
                if (tops.Count > 0 && bottoms.Count > 0)
                    entry["ascend"] = V(tops.Aggregate(Vector3.zero, (a, b) => a + b) / tops.Count - bottoms.Aggregate(Vector3.zero, (a, b) => a + b) / bottoms.Count);
                if (wear != null && wear.m_comOffset != Vector3.zero) entry["comOffset"] = V(wear.m_comOffset);
                if (piece != null)
                {
                    entry["cost"] = new JArray(piece.m_resources.Where(r => r.m_resItem != null).Select(r => new JArray(r.m_resItem.gameObject.name, r.m_amount)));
                    if (piece.m_craftingStation != null) entry["station"] = piece.m_craftingStation.gameObject.name;
                    var rules = new JObject();
                    foreach (string flag in RuleFlags)
                    {
                        var field = typeof(Piece).GetField(flag);
                        if (field != null && field.GetValue(piece) is bool on && on) rules[char.ToLower(flag[2]) + flag.Substring(3)] = true;
                    }
                    if (piece.m_onlyInBiome != Heightmap.Biome.None)
                        rules["onlyInBiome"] = new JArray(piece.m_onlyInBiome.ToString().Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries));
                    if (piece.m_spaceRequirement > 0f) rules["spaceRequirement"] = piece.m_spaceRequirement;
                    if (piece.m_comfort > 0) rules["comfort"] = piece.m_comfort;
                    if (piece.m_mustConnectTo != null) rules["mustConnectTo"] = piece.m_mustConnectTo.gameObject.name;
                    if (rules.Count > 0) entry["rules"] = rules;
                }
                if (wear != null) entry["health"] = wear.m_health;
                CraftingStation station = copy.GetComponent<CraftingStation>();
                if (station != null)
                    entry["isStation"] = new JObject
                    {
                        ["range"] = station.m_rangeBuild, ["extraRangePerLevel"] = station.m_extraRangePerLevel,
                        ["needsRoof"] = station.m_craftRequireRoof, ["needsFire"] = station.m_craftRequireFire,
                    };
                return entry;
            }
            catch (Exception) { return null; }
            finally
            {
                ZNetView.m_forceDisableInit = false;
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
