using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Blueprint commands for an AI assistant, through the Claude Tools mod (its request mailbox, pictures and surveys). When Claude Tools is
    /// installed, BuildOrders adds its commands to it (place, remove, take down, build, check, level, open the window, list the plans) and lets
    /// its cameras point at placed blueprints by name. Without Claude Tools nothing here runs and BuildOrders works as usual. The two find each
    /// other while the game runs (no reference between them), and BuildOrders registers again whenever Claude Tools is (re)loaded.
    /// </summary>
    public partial class Plugin
    {
        private const string ClaudeToolsGuid = "com.dhack.claudetools";
        private static string ImportsFile => Path.Combine(BlueprintDir, "_imports.json");
        private BaseUnityPlugin _claudeTools;
        private float _nextClaudeCheck;

        internal bool ClaudeToolsInstalled => _claudeTools != null;

        private void UpdateClaudeLink()
        {
            if (Time.unscaledTime < _nextClaudeCheck) return;
            _nextClaudeCheck = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().Where(p => p != null && p.gameObject.scene.IsValid()).FirstOrDefault(p => MetadataHelper.GetMetadata(p)?.GUID == ClaudeToolsGuid);
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                Type api = found.GetType();
                MethodInfo register = api.GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                MethodInfo frame = api.GetMethod("RegisterFrame", BindingFlags.Public | BindingFlags.Static);
                if (register == null) { Logger.LogWarning("Claude Tools is installed but too old for BuildOrders' commands"); return; }
                void Add(string name, string usage, Func<string[], Action<JObject>, Action<string>, IEnumerator> run) => register.Invoke(null, new object[] { Name, name, usage, run });

                Add("import", "import <file.json> [look|here|x z yaw]: place a blueprint from BepInEx/blueprints (the ground is levelled first, the ghosts follow; not when the file says \"level\": false)", CmdImport);
                Add("remove", "remove <blueprint name|last>: take its ghosts away (pieces already built stay)", CmdRemove);
                Add("takedown", "takedown <blueprint name>: remove it and take down what was built of it (materials back to the player)", CmdTakeDown);
                Add("build", "build <count> <blueprint name>: build that many of its ghosts as E does (uses the player's materials; the workbench first)", CmdBuild);
                Add("check", "check <blueprint name|last>: post columns that do not reach the ground, and how high doors and gates sit", CmdCheck);
                Add("level", "level <blueprint name|last>: level the ground under a placed blueprint again", CmdLevel);
                Add("plans", "plans: every placed plan (pieces left, distance) and any levelling in progress", CmdPlans);
                Add("ui", "ui <blueprints|plans|settings|close> | ui place <file.json> | ui height <m> | ui turn <deg> | ui confirm | ui cancel: the Plans window and placement preview", CmdUi);
                Add("bridge", "bridge <from> <to> [width=2|4|6] [material=wood|corewood|darkwood|stone] [sides=rails|halfwalls|none] [ends=sloped|steps] [roof=on|off] [supports=auto|2|4] [shape=straight|arched] [torches=on|off]: plan a bridge between two spots (each: here, look, or x,z with no space); unset options use the player's last bridge settings", CmdBridge);
                Add("selfshare", "selfshare <file.json>: send a blueprint to yourself as if a friend had shared it (tests sharing alone)", CmdSelfShare);
                frame?.Invoke(null, new object[] { Name, (Func<string, float[]>)BlueprintFrame });
                Logger.LogInfo("Claude Tools found: blueprint commands added");
            }
            catch (Exception e) { Logger.LogWarning("Could not add blueprint commands to Claude Tools: " + e.Message); }
        }

        private void UnregisterClaudeCommands()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Name }); }
            catch (Exception) { }
        }

        // ---- where blueprints were placed (so cameras, Move and removals can find them) ----

        private static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));

        // Each world keeps its own list under "_worlds" -> its UID (plan names repeat between worlds). The newest placement is also written at the
        // top level by its name, as before, for the blueprint tools. Entries from before worlds were told apart (no "uid") count only in a world
        // of the same name.

        private static JObject ReadImports() => File.Exists(ImportsFile) ? JObject.Parse(File.ReadAllText(ImportsFile)) : new JObject();

        /// <summary>This world's own list in _imports.json (made if asked to).</summary>
        private static JObject WorldImports(JObject all, bool make = false)
        {
            string uid = Instance?._loadedWorld ?? "";
            if (!(all["_worlds"] is JObject worlds)) { if (!make) return null; all["_worlds"] = worlds = new JObject(); }
            if (!(worlds[uid] is JObject mine)) { if (!make) return null; worlds[uid] = mine = new JObject(); }
            return mine;
        }

        private static bool LegacyHere(JToken rec) => rec is JObject o && o["uid"] == null && (string)o["world"] == (Instance?._loadedWorldName ?? "");

        /// <summary>Where a blueprint of this name was placed in this world, or null.</summary>
        private static JObject ImportRecord(JObject all, string title)
        {
            if (title == null) return null;
            if (WorldImports(all)?[title] is JObject rec) return rec;
            return LegacyHere(all[title]) ? (JObject)all[title] : null;
        }

        /// <summary>The newest blueprint placed in this world.</summary>
        private static string LastImport(JObject all)
        {
            string last = (string)WorldImports(all)?["_last"];
            if (last != null) return last;
            last = (string)all["_last"];
            return LegacyHere(all[last ?? ""]) ? last : null;
        }

        /// <summary>Every placement in this world, by name.</summary>
        private static IEnumerable<KeyValuePair<string, JObject>> WorldImportRecords(JObject all)
        {
            var seen = new Dictionary<string, JObject>();
            foreach (JProperty p in all.Properties()) if (!p.Name.StartsWith("_") && LegacyHere(p.Value)) seen[p.Name] = (JObject)p.Value;
            JObject mine = WorldImports(all);
            if (mine != null) foreach (JProperty p in mine.Properties()) if (p.Value is JObject o) seen[p.Name] = o;
            return seen;
        }

        private void RememberImport(string title, string file, Vector3 origin, float yaw, int count, float offsetY = 0f)
        {
            try
            {
                JObject all = ReadImports();
                var rec = new JObject { ["file"] = Path.GetFileName(file), ["origin"] = Vec(origin), ["yaw"] = Math.Round(yaw, 2), ["offsetY"] = Math.Round(offsetY, 2), ["pieces"] = count, ["time"] = DateTime.Now.ToString("s"), ["world"] = _loadedWorldName ?? "", ["uid"] = _loadedWorld ?? "" };
                JObject mine = WorldImports(all, make: true);
                mine[title] = rec;
                mine["_last"] = title;
                all[title] = rec.DeepClone();
                all["_last"] = title;
                File.WriteAllText(ImportsFile, all.ToString());
            }
            catch (Exception e) { Logger.LogWarning("Could not record the blueprint placement: " + e.Message); }
        }

        private static float ImportOffset(string title)
        {
            try { return ImportRecord(ReadImports(), title) is JObject rec ? (float)(rec["offsetY"] ?? 0f) : 0f; }
            catch (Exception) { return 0f; }
        }

        /// <summary>Where a placed blueprint is: its anchor point and turn ("last" for the newest).</summary>
        private static bool Frame(string token, out Vector3 origin, out float yaw, out string error)
        {
            origin = Vector3.zero; yaw = 0f; error = null;
            try
            {
                JObject all = ReadImports();
                string name = token == "last" ? LastImport(all) : token;
                if (!(ImportRecord(all, name) is JObject rec)) { error = "no placed blueprint called '" + token + "'"; return false; }
                var o = (JArray)rec["origin"];
                origin = new Vector3((float)o[0], (float)o[1], (float)o[2]);
                yaw = (float)rec["yaw"];
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static float[] BlueprintFrame(string token) =>
            Frame(token.Replace('_', ' '), out Vector3 o, out float yaw, out string _) || Frame(token, out o, out yaw, out _) ? new[] { o.x, o.y, o.z, yaw } : null;

        // ---- the commands ----

        private static string NameArg(string[] a, int from, string fallback = "last")
        {
            string name = a.Length > from ? string.Join(" ", a.Skip(from).ToArray()) : fallback;
            if (name == "last") { try { name = LastImport(ReadImports()); } catch { } }
            return name ?? "";
        }

        private static float Num(string[] a, int i, float fallback) =>
            a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        private IEnumerator CmdImport(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            string path = a.Length > 1 ? Path.Combine(BlueprintDir, Path.GetFileName(a[1])) : null;
            if (path == null || !File.Exists(path)) { error("no blueprint file " + (a.Length > 1 ? a[1] : "")); return null; }
            JObject doc = Read(path);
            if (doc == null) { error("could not read " + a[1]); return null; }
            Vector3 at; float yaw = player.transform.eulerAngles.y;
            if (a.Length >= 5) { at = new Vector3(Num(a, 2, 0), 0f, Num(a, 3, 0)); yaw = Num(a, 4, 0); }
            else if (a.Length > 2 && a[2] == "here") at = player.transform.position;
            else if (!LookPoint(player, out at)) at = player.transform.position + player.transform.forward * 6f;
            bool keeps = KeepsGround(path);
            output(new JObject { ["import"] = a[1], ["placed"] = Import(player, path, doc, at, yaw),
                                 ["note"] = keeps ? "\"level\": false: placed straight onto the ground as it is" : "the ghosts appear when the ground is levelled (about a second)" });
            return null;
        }

        private IEnumerator CmdRemove(string[] a, Action<JObject> output, Action<string> error)
        {
            string name = NameArg(a, 1);
            output(new JObject { ["removed"] = RemovePlan(BlueprintPrefix + name), ["blueprint"] = name });
            return null;
        }

        private IEnumerator CmdTakeDown(string[] a, Action<JObject> output, Action<string> error)
        {
            string name = NameArg(a, 1, "");
            int built = BuiltPieces(BlueprintPrefix + name).Count;
            output(new JObject { ["takedown"] = name, ["builtPieces"] = built, ["ghostsRemoved"] = RemovePlan(BlueprintPrefix + name, takeDownBuilt: true) });
            return null;
        }

        private IEnumerator CmdBuild(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            int count = a.Length > 1 && int.TryParse(a[1], out int c) ? c : 1;
            string name = NameArg(a, 2, "");
            var mine = _orders.Values.Where(o => o.By == BlueprintPrefix + name)
                .OrderBy(o => o.Prefab.StartsWith("piece_workbench") ? 0 : 1).ThenBy(o => (PieceOf(o)?.m_resources.Sum(r => r.m_amount)) ?? 99).ThenBy(o => o.Pos.y).ToList();
            int built = 0;
            foreach (Order o in mine) { if (built >= count) break; if (TryBuild(player, o, quiet: true)) built++; }
            output(new JObject { ["built"] = built, ["of"] = mine.Count });
            return null;
        }

        private IEnumerator CmdCheck(string[] a, Action<JObject> output, Action<string> error)
        {
            string name = NameArg(a, 1);
            var shapes = Shapes();
            var mine = _orders.Values.Where(o => o.By == BlueprintPrefix + name).ToList();
            if (mine.Count == 0) { error("no placed plan called '" + name + "'"); return null; }
            var columns = new Dictionary<string, float>();   // lowest post bottom per column
            var doors = new JArray();
            // tops of the plan's other pieces (floors, walls...): a post standing on one of them (a rail post on a deck) is not meant to reach the ground
            var tops = mine.Where(o => shapes.TryGetValue(o.Prefab, out Shape t) && !t.IsPost && t.Max != t.Min)
                           .Select(o => { Shape t = shapes[o.Prefab]; return new { o.Pos, R = new Vector2(t.Max.x - t.Min.x, t.Max.z - t.Min.z).magnitude * 0.5f + 0.1f, Top = o.Pos.y + t.Max.y }; }).ToList();
            foreach (Order o in mine)
            {
                if (!shapes.TryGetValue(o.Prefab, out Shape s)) continue;
                float bottom = o.Pos.y + (o.Rot * new Vector3(0f, s.Bottom, 0f)).y;
                float ground = ZoneSystem.instance.GetGroundHeight(o.Pos);
                if (s.IsPost && !tops.Any(t => Mathf.Abs(t.Top - bottom) < 0.35f && new Vector2(t.Pos.x - o.Pos.x, t.Pos.z - o.Pos.z).magnitude < t.R))
                {
                    string col = Mathf.Round(o.Pos.x * 4f) + "," + Mathf.Round(o.Pos.z * 4f);
                    float gap = bottom - ground;
                    if (!columns.TryGetValue(col, out float g) || gap < g) columns[col] = gap;
                }
                if (o.Prefab.Contains("door") || o.Prefab.Contains("gate"))
                    doors.Add(new JObject { ["piece"] = o.Prefab, ["bottomAboveGround"] = Math.Round(o.Pos.y + s.Min.y - ground, 2) });
            }
            var floating = columns.Where(kv => kv.Value > 0.15f).OrderByDescending(kv => kv.Value).Select(kv => Math.Round(kv.Value, 2)).ToList();
            // would it stand once built? (the same estimate as the stability colours, from the game's support rules)
            ComputeStability(Player.m_localPlayer, new HashSet<string>(mine.Select(o => o.Id)));
            var weak = mine.Select(o => _stability.TryGetValue(o.Id, out Stab st) ? st : null).Where(st => st != null).ToList();
            _stabilityDirty = true;
            output(new JObject
            {
                ["check"] = name, ["pieces"] = mine.Count, ["postColumns"] = columns.Count, ["floatingColumns"] = new JArray(floating), ["doors"] = doors,
                ["wouldFall"] = weak.Count(st => st.Collapses), ["weakestSupport"] = weak.Count > 0 ? weak.Min(st => st.Percent) + "%" : null,
                ["falling"] = new JArray(mine.Where(o => _stability.TryGetValue(o.Id, out Stab st) && st.Collapses).Take(10).Select(o => o.Prefab + " at " + Vec(o.Pos))),
            });
            return null;
        }

        private IEnumerator CmdLevel(string[] a, Action<JObject> output, Action<string> error)
        {
            string name = NameArg(a, 1);
            Player player = Player.m_localPlayer;
            PlanInfo plan = Plans(player.transform.position).FirstOrDefault(pi => pi.Key == BlueprintPrefix + name);
            if (plan == null) { error("no placed plan called '" + name + "'"); return null; }
            output(new JObject { ["level"] = name, ["spots"] = LevelPlacedPlan(plan) });
            return null;
        }

        private IEnumerator CmdPlans(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            output(new JObject
            {
                ["plans"] = new JArray(Plans(player.transform.position).Select(p => new JObject { ["name"] = p.Title, ["blueprint"] = p.IsBlueprint, ["piecesLeft"] = p.Orders.Count, ["distance"] = Math.Round(p.Distance, 1) })),
                ["levelling"] = new JArray(_levelJobs.Values.Select(j => new JObject { ["plan"] = j.Title, ["running"] = j.Running, ["status"] = j.Status })),
            });
            return null;
        }

        private IEnumerator CmdUi(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            string what = a.Length > 1 ? a[1].ToLowerInvariant() : "blueprints";
            switch (what)
            {
                case "close": PlansWindowOpen = false; break;
                case "cancel": CancelPlacement(); break;
                case "confirm":
                    if (_placing == null || !_placing.HaveAnchor) { error("not placing anything (or not looking at the ground)"); return null; }
                    ConfirmPlacement(player);
                    break;
                case "height":
                case "turn":
                    if (_placing == null) { error("not placing anything"); return null; }
                    if (what == "height") _placing.OffsetY = Num(a, 2, 0f); else _placing.Yaw = Mathf.Repeat(Num(a, 2, 0f), 360f);
                    output(new JObject { ["ui"] = what, ["levelSpots"] = _placing.Strokes, ["cut"] = Math.Round(_placing.Cut, 2), ["fill"] = Math.Round(_placing.Fill, 2) });
                    return null;
                case "place":
                    string path = a.Length > 2 ? Path.Combine(BlueprintDir, Path.GetFileName(a[2])) : null;
                    JObject doc = path != null && File.Exists(path) ? Read(path) : null;
                    if (doc == null) { error("no blueprint file " + (a.Length > 2 ? a[2] : "")); return null; }
                    StartPlacement((string)doc["name"] ?? Path.GetFileNameWithoutExtension(path), path, EntriesFrom(doc["pieces"] as JArray ?? new JArray()), player.transform.eulerAngles.y, 0f, null);
                    break;
                default:
                    _plansTab = what == "plans" ? 1 : what == "settings" ? 2 : 0;
                    _plansScroll = Vector2.zero;
                    _libraryAt = -99f;
                    PlansWindowOpen = true;
                    break;
            }
            output(new JObject { ["ui"] = what });
            return null;
        }

        private IEnumerator CmdBridge(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            bool Spot(string token, out Vector3 at)
            {
                at = player.transform.position;
                if (token == "here") { at.y = ZoneSystem.instance.GetGroundHeight(at); return true; }
                if (token == "look") return LookPoint(player, out at);
                string[] xz = token.Split(',');
                if (xz.Length == 2 && float.TryParse(xz[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(xz[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                { at = new Vector3(x, 0f, z); at.y = ZoneSystem.instance.GetGroundHeight(at); return true; }
                return false;
            }
            if (a.Length < 3 || !Spot(a[1], out Vector3 from) || !Spot(a[2], out Vector3 to)) { error("bridge <here|look|x,z> <here|look|x,z>"); return null; }
            BridgeOptions o = CurrentBridgeOptions();
            foreach (string kv in a.Skip(3))
            {
                string[] p = kv.ToLowerInvariant().Split('=');
                if (p.Length != 2) continue;
                bool on = p[1] == "on" || p[1] == "yes" || p[1] == "true";
                switch (p[0])
                {
                    case "width": if (int.TryParse(p[1], out int wv)) o.Width = wv; break;
                    case "material": o.Material = p[1].StartsWith("core") ? BridgeMaterial.CoreWood : p[1].StartsWith("dark") ? BridgeMaterial.Darkwood : p[1].StartsWith("stone") ? BridgeMaterial.Stone : BridgeMaterial.Wood; break;
                    case "rails": o.Sides = on ? BridgeSides.Rails : BridgeSides.None; break;
                    case "sides": o.Sides = p[1].StartsWith("half") ? BridgeSides.HalfWalls : p[1] == "none" ? BridgeSides.None : BridgeSides.Rails; break;
                    case "ends": o.Ends = p[1].StartsWith("step") ? BridgeEnds.Steps : BridgeEnds.Sloped; break;
                    case "roof": o.Roof = on; break;
                    case "torches": o.Torches = on; break;
                    case "supports": o.Supports = p[1] == "2" ? BridgeSupports.Every2m : p[1] == "4" ? BridgeSupports.Every4m : BridgeSupports.Auto; break;
                    case "shape": o.Shape = p[1].StartsWith("arch") ? BridgeShape.Arched : BridgeShape.Straight; break;
                }
            }
            BridgeDesign d = PlanBridge(player, from, to, o);
            output(new JObject { ["bridge"] = LastPlacedTitle(), ["length"] = Math.Round(d.Length, 1), ["pieces"] = d.Entries.Count,
                ["parts"] = new JObject(d.Entries.GroupBy(e => e.Prefab).OrderByDescending(g => g.Count()).Select(g => new JProperty(g.Key, g.Count()))), ["warnings"] = new JArray(d.Warnings), ["from"] = Vec(from), ["to"] = Vec(to) });
            return null;
        }

        private IEnumerator CmdSelfShare(string[] a, Action<JObject> output, Action<string> error)
        {
            string path = a.Length > 1 ? Path.Combine(BlueprintDir, Path.GetFileName(a[1])) : null;
            if (path == null || !File.Exists(path)) { error("no blueprint file " + (a.Length > 1 ? a[1] : "")); return null; }
            string code = ToCode(Tame(JObject.Parse(File.ReadAllText(path)), "Test friend"));
            byte[] pic = PictureForSharing(path);
            OnSharedBlueprint("Test friend|" + code + (pic != null ? "|" + Convert.ToBase64String(pic) : ""));
            output(new JObject { ["selfshare"] = a[1], ["codeLength"] = code.Length, ["pictureBytes"] = pic != null ? pic.Length : 0 });
            return null;
        }
    }
}
