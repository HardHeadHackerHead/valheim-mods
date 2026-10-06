using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// "Eyes" for whoever designs your blueprints (you, or an AI assistant working in the blueprints folder), without any network port:
    ///
    ///   * a screenshot key, saving to BepInEx/blueprints/shots (with where you stood and looked),
    ///   * a survey key, writing the ground heights, water and existing pieces around where you look to blueprints/_survey.json,
    ///   * request files (off unless AllowRequests is on): drop a .txt into blueprints/requests and the game carries it out and writes a
    ///     .done.json next to it. Commands: shot, view, orbit, top, survey, import, remove, wait, status (see CLAUDE.md in the blueprints folder).
    ///
    /// Nothing here moves your character or presses keys; views are drawn by a separate camera.
    /// </summary>
    public partial class Plugin
    {
        private ConfigEntry<KeyCode> _shotKey;
        private ConfigEntry<KeyboardShortcut> _surveyKey;
        private ConfigEntry<bool> _allowRequests;
        private ConfigEntry<float> _surveyRadius;
        private float _nextRequestScan;
        private bool _requestBusy;

        private static string ShotDir => Path.Combine(BlueprintDir, "shots");
        private static string RequestDir => Path.Combine(BlueprintDir, "requests");
        private static string ImportsFile => Path.Combine(BlueprintDir, "_imports.json");

        private void BindEyesConfig()
        {
            _shotKey = Config.Bind("Blueprints", "ScreenshotKey", KeyCode.F12, "Save a screenshot (and where you stand and look) into BepInEx/blueprints/shots, for whoever is designing with you.");
            _surveyKey = Config.Bind("Blueprints", "SurveyKey", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl),
                "Write the ground heights, water and existing pieces around where you look into BepInEx/blueprints/_survey.json (for designing on uneven ground).");
            _surveyRadius = Config.Bind("Blueprints", "SurveyRadius", 24f, new ConfigDescription("How far the survey reaches (metres).", new AcceptableValueRange<float>(4f, 64f)));
            _allowRequests = Config.Bind("Blueprints", "AllowRequests", false,
                "Carry out request files dropped into BepInEx/blueprints/requests (screenshots, views from a separate camera, surveys, placing or removing blueprint ghosts). Only files on this computer can do this; nothing moves your character.");
        }

        private void UpdateEyes(Player player)
        {
            if (!TypingOrMenuOpen())
            {
                if (Input.GetKeyDown(_shotKey.Value)) StartCoroutine(Screenshot(Path.Combine(ShotDir, Stamp() + ".png"), 0, path => player.Message(MessageHud.MessageType.TopLeft, "Screenshot saved for your blueprints")));
                KeyboardShortcut survey = _surveyKey.Value;
                if (survey.MainKey != KeyCode.None && Input.GetKeyDown(survey.MainKey) && survey.Modifiers.All(Input.GetKey))
                {
                    Vector3 at = LookPoint(player, out Vector3 hit) ? hit : player.transform.position;
                    string path = Survey(at, _surveyRadius.Value, 1f, player);
                    player.Message(MessageHud.MessageType.Center, "Surveyed " + (int)_surveyRadius.Value + " m around this spot for your blueprints");
                    Logger.LogInfo("Survey written to " + path);
                }
            }

            if (_allowRequests.Value && !_requestBusy && Time.time >= _nextRequestScan)
            {
                _nextRequestScan = Time.time + 1f;
                if (Directory.Exists(RequestDir))
                {
                    string next = Directory.GetFiles(RequestDir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (next != null) StartCoroutine(RunRequest(next, player));
                }
            }
        }

        private static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

        // ---- pictures ----

        /// <summary>What you see, saved as a PNG at the end of this frame (with a .json beside it saying where you were).</summary>
        private IEnumerator Screenshot(string path, int maxWidth, Action<string> done)
        {
            yield return new WaitForEndOfFrame();
            string saved = null;
            try
            {
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (maxWidth > 0 && tex.width > maxWidth) tex = Shrink(tex, maxWidth);
                saved = SavePng(tex, path);
                Destroy(tex);
                WriteShotInfo(path, Player.m_localPlayer);
            }
            catch (Exception e) { Logger.LogWarning("Screenshot failed: " + e.Message); }
            done?.Invoke(saved);
        }

        private static Texture2D Shrink(Texture2D tex, int width)
        {
            int height = Mathf.RoundToInt(tex.height * (width / (float)tex.width));
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(tex, rt);
            Texture2D small = ReadBack(rt);
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
            return small;
        }

        private static Texture2D ReadBack(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }

        private static string SavePng(Texture2D tex, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            byte[] png = ImageConversion.EncodeToPNG(tex);
            File.WriteAllBytes(path, png);
            File.Copy(path, Path.Combine(Path.GetDirectoryName(path), "latest.png"), true);
            return path;
        }

        private static void WriteShotInfo(string png, Player player)
        {
            if (player == null) return;
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            var info = new JObject
            {
                ["time"] = DateTime.Now.ToString("s"),
                ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                ["player"] = Vec(player.transform.position),
                ["playerYaw"] = Math.Round(player.transform.eulerAngles.y, 1),
                ["camera"] = cam != null ? Vec(cam.position) : null,
                ["cameraYaw"] = cam != null ? Math.Round(cam.eulerAngles.y, 1) : 0,
                ["cameraPitch"] = cam != null ? Math.Round(cam.eulerAngles.x, 1) : 0,
            };
            if (LookPoint(player, out Vector3 hit)) info["lookingAt"] = Vec(hit);
            File.WriteAllText(Path.ChangeExtension(png, ".json"), info.ToString());
        }

        private static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));

        /// <summary>A picture from a separate camera at any spot (the character does not move). The world near the player is what is loaded.</summary>
        private string RenderView(Vector3 position, Quaternion rotation, int width, int height, float fov, string path, bool orthographic = false, float orthoSize = 20f)
        {
            Camera main = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            var go = new GameObject("BlueprintViewCamera");
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            try
            {
                Camera cam = go.AddComponent<Camera>();
                if (main != null) cam.CopyFrom(main);
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
                cam.orthographic = orthographic;
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);
                cam.targetTexture = rt;
                cam.Render();
                Texture2D tex = ReadBack(rt);
                string saved = SavePng(tex, path);
                Destroy(tex);
                return saved;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Destroy(go);
            }
        }

        // ---- the lie of the land ----

        /// <summary>Ground heights on a grid around a point, the water level, the biome and the pieces already there: what a designer needs for a real site.</summary>
        private string Survey(Vector3 centre, float radius, float step, Player player)
        {
            ZoneSystem zone = ZoneSystem.instance;
            float baseY = zone.GetGroundHeight(centre);
            int n = Mathf.Clamp(Mathf.RoundToInt(radius / step), 1, 128);
            var ground = new JArray();
            var solid = new JArray();
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int iz = -n; iz <= n; iz++)
            {
                var gRow = new JArray();
                var sRow = new JArray();
                for (int ix = -n; ix <= n; ix++)
                {
                    var p = new Vector3(centre.x + ix * step, 0f, centre.z + iz * step);
                    float g = zone.GetGroundHeight(p) - baseY;
                    float s = zone.GetSolidHeight(p, out float sh, 1000) ? sh - baseY : g;
                    gRow.Add(Math.Round(g, 2));
                    sRow.Add(Math.Round(s, 2));
                    lowest = Mathf.Min(lowest, g); highest = Mathf.Max(highest, g);
                }
                ground.Add(gRow);
                solid.Add(sRow);
            }

            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(centre, radius, pieces);
            var existing = new JArray();
            foreach (Piece pc in pieces.Take(2000))
            {
                Vector3 local = pc.transform.position - new Vector3(centre.x, baseY, centre.z);
                existing.Add(new JObject { ["p"] = Utils.GetPrefabName(pc.gameObject), ["x"] = Math.Round(local.x, 2), ["y"] = Math.Round(local.y, 2), ["z"] = Math.Round(local.z, 2), ["ry"] = Math.Round(pc.transform.eulerAngles.y, 1) });
            }

            var doc = new JObject
            {
                ["note"] = "Heights are metres above the ground at the centre. Rows run from -z (south) to +z, columns from -x (west) to +x, one every 'step' metres. 'solid' includes rocks, trees and buildings; 'ground' is the terrain only. Pieces are relative to the centre at its ground height.",
                ["time"] = DateTime.Now.ToString("s"),
                ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                ["centre"] = Vec(new Vector3(centre.x, baseY, centre.z)),
                ["playerYaw"] = player != null ? Math.Round(player.transform.eulerAngles.y, 1) : 0,
                ["biome"] = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(centre).ToString() : "",
                ["radius"] = radius, ["step"] = step,
                ["waterAboveCentre"] = Math.Round(zone.m_waterLevel - baseY, 2),
                ["lowest"] = Math.Round(lowest, 2), ["highest"] = Math.Round(highest, 2),
                ["ground"] = ground, ["solid"] = solid, ["pieces"] = existing,
            };
            string path = Path.Combine(BlueprintDir, "_survey.json");
            Directory.CreateDirectory(BlueprintDir);
            File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            return path;
        }

        // ---- where blueprints were placed (so views and removals can find them) ----

        private void RememberImport(string title, string file, Vector3 origin, float yaw, int count, float offsetY = 0f)
        {
            try
            {
                JObject all = File.Exists(ImportsFile) ? JObject.Parse(File.ReadAllText(ImportsFile)) : new JObject();
                all[title] = new JObject { ["file"] = Path.GetFileName(file), ["origin"] = Vec(origin), ["yaw"] = Math.Round(yaw, 2), ["offsetY"] = Math.Round(offsetY, 2), ["pieces"] = count, ["time"] = DateTime.Now.ToString("s"), ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "" };
                all["_last"] = title;
                File.WriteAllText(ImportsFile, all.ToString());
            }
            catch (Exception e) { Logger.LogWarning("Could not record the blueprint placement: " + e.Message); }
        }

        /// <summary>How far a placed blueprint was raised or lowered from the ground at its anchor (0 if not recorded).</summary>
        private static float ImportOffset(string title)
        {
            try
            {
                JObject all = File.Exists(ImportsFile) ? JObject.Parse(File.ReadAllText(ImportsFile)) : new JObject();
                return all[title] is JObject rec ? (float)(rec["offsetY"] ?? 0f) : 0f;
            }
            catch (Exception) { return 0f; }
        }

        /// <summary>"last", a blueprint's name or "world": the frame that view coordinates are measured in.</summary>
        private static bool Frame(string token, out Vector3 origin, out float yaw, out string error)
        {
            origin = Vector3.zero; yaw = 0f; error = null;
            if (token == "world") return true;
            try
            {
                JObject all = File.Exists(ImportsFile) ? JObject.Parse(File.ReadAllText(ImportsFile)) : new JObject();
                string name = token == "last" ? (string)all["_last"] : token;
                if (name == null || !(all[name] is JObject rec)) { error = "no placed blueprint called '" + token + "'"; return false; }
                var o = (JArray)rec["origin"];
                origin = new Vector3((float)o[0], (float)o[1], (float)o[2]);
                yaw = (float)rec["yaw"];
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        // ---- request files ----

        private IEnumerator RunRequest(string file, Player player)
        {
            _requestBusy = true;
            string taken = Path.ChangeExtension(file, ".taken");
            var result = new JObject { ["request"] = Path.GetFileName(file), ["time"] = DateTime.Now.ToString("s") };
            var outputs = new JArray(); var errors = new JArray();
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(taken); File.Move(file, taken); }
            catch (Exception e) { _requestBusy = false; Logger.LogWarning("Could not read request " + file + ": " + e.Message); yield break; }

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] a = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string cmd = a[0].ToLowerInvariant();
                float F(int i, float d) => a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : d;
                int I(int i, int d) => a.Length > i && int.TryParse(a[i], out int v) ? v : d;

                if (cmd == "shot")
                {
                    string path = Path.Combine(ShotDir, Stamp() + ".png");
                    string got = null;
                    yield return Screenshot(path, I(1, 0), p => got = p);
                    if (got != null) outputs.Add(new JObject { ["shot"] = got }); else errors.Add("shot failed");
                    continue;
                }
                if (cmd == "wait")
                {
                    // wait <seconds>: let ghosts appear before taking pictures of them
                    yield return new WaitForSeconds(Mathf.Clamp(F(1, 2f), 0f, 30f));
                    continue;
                }
                yield return new WaitForEndOfFrame();
                try
                {
                    switch (cmd)
                    {
                        case "view":
                        {
                            // view <frame> x y z yaw pitch [fov] [width] [height]: a camera at a spot in a placed blueprint's frame (or "world")
                            if (!Frame(a.Length > 1 ? a[1] : "last", out Vector3 origin, out float yaw, out string err)) { errors.Add(err); break; }
                            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                            Vector3 pos = origin + turn * new Vector3(F(2, 0), F(3, 10), F(4, -20));
                            Quaternion rot = turn * Quaternion.Euler(F(6, 20), F(5, 0), 0f);
                            outputs.Add(new JObject { ["view"] = RenderView(pos, rot, I(8, 1280), I(9, 720), F(7, 55), Path.Combine(ShotDir, Stamp() + "-view.png")) });
                            break;
                        }
                        case "orbit":
                        {
                            // orbit <frame> [radius] [pitch] [count] [width] [height]: pictures from all round a placed blueprint
                            if (!Frame(a.Length > 1 ? a[1] : "last", out Vector3 origin, out float yaw, out string err)) { errors.Add(err); break; }
                            float radius = F(2, 28), pitch = F(3, 30);
                            int count = Mathf.Clamp(I(4, 4), 1, 12);
                            Vector3 target = origin + Vector3.up * 2f;
                            for (int k = 0; k < count; k++)
                            {
                                float angle = yaw + 180f + 45f + k * 360f / count;
                                Quaternion look = Quaternion.Euler(pitch, angle, 0f);
                                Vector3 pos = target - look * Vector3.forward * radius;
                                outputs.Add(new JObject { ["orbit"] = RenderView(pos, look, I(5, 1280), I(6, 720), 50f, Path.Combine(ShotDir, Stamp() + "-orbit" + k + ".png")) });
                            }
                            break;
                        }
                        case "top":
                        {
                            // top <frame> [size] [width]: straight down over a placed blueprint
                            if (!Frame(a.Length > 1 ? a[1] : "last", out Vector3 origin, out float yaw, out string err)) { errors.Add(err); break; }
                            float size = F(2, 30);
                            Quaternion rot = Quaternion.Euler(90f, yaw, 0f);
                            outputs.Add(new JObject { ["top"] = RenderView(origin + Vector3.up * 80f, rot, I(3, 1024), I(3, 1024), 50f, Path.Combine(ShotDir, Stamp() + "-top.png"), true, size / 2f) });
                            break;
                        }
                        case "survey":
                        {
                            // survey [radius] [step] [frame|look|here]
                            Vector3 at = player.transform.position;
                            string where = a.Length > 3 ? a[3] : "look";
                            if (where == "look" && LookPoint(player, out Vector3 hit)) at = hit;
                            else if (where != "here" && where != "look" && Frame(where, out Vector3 o, out float _, out string _)) at = o;
                            outputs.Add(new JObject { ["survey"] = Survey(at, F(1, _surveyRadius.Value), Mathf.Max(0.25f, F(2, 1f)), player) });
                            break;
                        }
                        case "import":
                        {
                            // import <file.json> [look|here|x z yaw]
                            string path = a.Length > 1 ? Path.Combine(BlueprintDir, Path.GetFileName(a[1])) : null;
                            if (path == null || !File.Exists(path)) { errors.Add("no blueprint file " + (a.Length > 1 ? a[1] : "")); break; }
                            JObject doc = Read(path);
                            if (doc == null) { errors.Add("could not read " + a[1]); break; }
                            Vector3 at; float yaw = player.transform.eulerAngles.y;
                            if (a.Length >= 5) { at = new Vector3(F(2, 0), 0f, F(3, 0)); yaw = F(4, 0); }
                            else if (a.Length > 2 && a[2] == "here") at = player.transform.position;
                            else if (!LookPoint(player, out at)) at = player.transform.position + player.transform.forward * 6f;
                            int placed = Import(player, path, doc, at, yaw);
                            outputs.Add(new JObject { ["import"] = a[1], ["placed"] = placed });
                            break;
                        }
                        case "remove":
                        {
                            // remove <blueprint name or "last">: takes its ghosts away again (pieces already built stay)
                            string name = a.Length > 1 ? string.Join(" ", a.Skip(1).ToArray()) : "last";
                            if (name == "last") { try { name = (string)JObject.Parse(File.ReadAllText(ImportsFile))["_last"]; } catch { } }
                            outputs.Add(new JObject { ["removed"] = RemovePlan(BlueprintPrefix + name), ["blueprint"] = name });
                            break;
                        }
                        case "selfshare":
                        {
                            // selfshare <file.json>: pack a blueprint as Share does and receive it as if a friend had sent it (tests sharing alone)
                            string path = a.Length > 1 ? Path.Combine(BlueprintDir, Path.GetFileName(a[1])) : null;
                            if (path == null || !File.Exists(path)) { errors.Add("no blueprint file " + (a.Length > 1 ? a[1] : "")); break; }
                            string code = ToCode(Tame(JObject.Parse(File.ReadAllText(path)), "Test friend"));
                            byte[] pic = PictureForSharing(path);
                            OnSharedBlueprint("Test friend|" + code + (pic != null ? "|" + Convert.ToBase64String(pic) : ""));
                            outputs.Add(new JObject { ["pictureBytes"] = pic != null ? pic.Length : 0 });
                            outputs.Add(new JObject { ["selfshare"] = a[1], ["codeLength"] = code.Length, ["inbox"] = Directory.Exists(InboxDir) ? Directory.GetFiles(InboxDir, "*.json").Length : 0 });
                            break;
                        }
                        case "check":
                        {
                            // check <blueprint name|last>: does each post column of a placed plan reach the ground, and where are its doors?
                            string name = a.Length > 1 ? string.Join(" ", a.Skip(1).ToArray()) : "last";
                            if (name == "last") { try { name = (string)JObject.Parse(File.ReadAllText(ImportsFile))["_last"]; } catch { } }
                            var shapes = Shapes();
                            var mine = _orders.Values.Where(o => o.By == BlueprintPrefix + name).ToList();
                            if (mine.Count == 0) { errors.Add("no placed plan called '" + name + "'"); break; }
                            var columns = new Dictionary<string, float>();   // lowest post bottom per column
                            var doors = new JArray();
                            foreach (Order o in mine)
                            {
                                if (!shapes.TryGetValue(o.Prefab, out Shape s)) continue;
                                float bottom = o.Pos.y + (o.Rot * new Vector3(0f, s.Bottom, 0f)).y;
                                float ground = ZoneSystem.instance.GetGroundHeight(o.Pos);
                                if (s.IsPost)
                                {
                                    string col = Mathf.Round(o.Pos.x * 4f) + "," + Mathf.Round(o.Pos.z * 4f);
                                    float gap = bottom - ground;
                                    if (!columns.TryGetValue(col, out float g) || gap < g) columns[col] = gap;
                                }
                                if (o.Prefab.Contains("door") || o.Prefab.Contains("gate"))
                                    doors.Add(new JObject { ["piece"] = o.Prefab, ["bottomAboveGround"] = Math.Round(o.Pos.y + s.Min.y - ground, 2) });
                            }
                            var floating = columns.Where(kv => kv.Value > 0.15f).OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " (x4) " + Math.Round(kv.Value, 2) + " m").ToList();
                            outputs.Add(new JObject { ["check"] = name, ["pieces"] = mine.Count, ["postColumns"] = columns.Count, ["floatingColumns"] = new JArray(floating), ["doors"] = doors });
                            break;
                        }
                        case "level":
                        {
                            // level <blueprint name|last>: level the ground under a placed blueprint (as its Level ground button does)
                            string name = a.Length > 1 ? string.Join(" ", a.Skip(1).ToArray()) : "last";
                            if (name == "last") { try { name = (string)JObject.Parse(File.ReadAllText(ImportsFile))["_last"]; } catch { } }
                            PlanInfo plan = Plans(player.transform.position).FirstOrDefault(pi => pi.Key == BlueprintPrefix + name);
                            if (plan == null) { errors.Add("no placed plan called '" + name + "' (placed: " + string.Join(", ", Plans(player.transform.position).Select(pi => pi.Key).ToArray()) + ")"); break; }
                            outputs.Add(new JObject { ["level"] = name, ["spots"] = LevelPlacedPlan(plan) });
                            break;
                        }
                        case "ui":
                        {
                            // ui <blueprints|plans|settings|close> or ui place <file.json> / ui cancel: open the Plans window or a placement
                            // preview, so a "shot" right after shows what the player would see
                            string what = a.Length > 1 ? a[1].ToLowerInvariant() : "blueprints";
                            if (what == "close") PlansWindowOpen = false;
                            else if (what == "cancel") CancelPlacement();
                            else if (what == "confirm")
                            {
                                // ui confirm: what a click does while placing
                                if (_placing == null || !_placing.HaveAnchor) { errors.Add("not placing anything (or not looking at the ground)"); break; }
                                ConfirmPlacement(player);
                                outputs.Add(new JObject { ["ui"] = "confirm" });
                                break;
                            }
                            else if (what == "height" || what == "turn")
                            {
                                // ui height <metres> / ui turn <degrees>: what PgUp/PgDn and the mouse wheel do while placing
                                if (_placing == null) { errors.Add("not placing anything"); break; }
                                if (what == "height") _placing.OffsetY = F(2, 0f); else _placing.Yaw = Mathf.Repeat(F(2, 0f), 360f);
                                outputs.Add(new JObject { ["ui"] = what, ["levelSpots"] = _placing.Strokes, ["cut"] = Math.Round(_placing.Cut, 2), ["fill"] = Math.Round(_placing.Fill, 2) });
                                break;
                            }
                            else if (what == "place")
                            {
                                string path = a.Length > 2 ? Path.Combine(BlueprintDir, Path.GetFileName(a[2])) : null;
                                JObject doc = path != null && File.Exists(path) ? Read(path) : null;
                                if (doc == null) { errors.Add("no blueprint file " + (a.Length > 2 ? a[2] : "")); break; }
                                StartPlacement((string)doc["name"] ?? Path.GetFileNameWithoutExtension(path), path, EntriesFrom(doc["pieces"] as JArray ?? new JArray()), player.transform.eulerAngles.y, 0f, null);
                            }
                            else
                            {
                                _plansTab = what == "plans" ? 1 : what == "settings" ? 2 : 0;
                                _plansScroll = Vector2.zero;
                                _libraryAt = -99f;
                                PlansWindowOpen = true;
                            }
                            outputs.Add(new JObject { ["ui"] = what });
                            break;
                        }
                        case "status":
                        {
                            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
                            var counts = new JObject();
                            foreach (var g in _orders.Values.GroupBy(o => o.By ?? "")) counts[g.Key] = g.Count();
                            outputs.Add(new JObject
                            {
                                ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                                ["player"] = Vec(player.transform.position), ["playerYaw"] = Math.Round(player.transform.eulerAngles.y, 1),
                                ["camera"] = cam != null ? Vec(cam.position) : null,
                                ["biome"] = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(player.transform.position).ToString() : "",
                                ["day"] = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0,
                                ["orders"] = counts,
                                ["levelling"] = new JArray(_levelJobs.Values.Select(j => new JObject { ["plan"] = j.Title, ["next"] = j.Next, ["of"] = j.Points.Count, ["done"] = j.Done, ["skipped"] = j.Skipped, ["running"] = j.Running, ["status"] = j.Status })),
                            });
                            break;
                        }
                        default:
                            errors.Add("unknown command: " + cmd);
                            break;
                    }
                }
                catch (Exception e) { errors.Add(cmd + ": " + e.Message); }
            }

            result["outputs"] = outputs;
            result["errors"] = errors;
            result["ok"] = errors.Count == 0;
            try { File.WriteAllText(Path.ChangeExtension(file, ".done.json"), result.ToString()); } catch (Exception e) { Logger.LogWarning("Could not write the request result: " + e.Message); }
            Logger.LogInfo($"Request {Path.GetFileName(file)}: {outputs.Count} result(s), {errors.Count} error(s)");
            _requestBusy = false;
        }
    }
}
