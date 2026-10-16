using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// A "web" command for the Claude Tools mod (its request mailbox), so an AI assistant can test the moves with nobody at the keyboard:
    ///   web status                         what the player could do now: the spot a swing web would take, the zip target under the crosshair,
    ///                                      the wall in front, the skill, the settings, the last moves
    ///   web try swing [lead=0.45] [hold=2.5] [yaw=deg] [pitch=deg] [forward=1] [shots=0.5,1,1.5]
    ///                                      jumps, presses Jump again in the air and holds it (with forward held), then lets go: the moves done,
    ///                                      top speed, how far and high, the release
    ///   web try zip [lead=0.4] [yaw=deg] [pitch=deg] [shots=...]   jumps and presses Sprint in the air: where it went
    ///   web try crawl [hold=2] [side=0] [yaw=deg] [shots=...]   facing a wall: jumps at it with forward held, crawls (forward = up, side=-1/1)
    ///   web try chain [count=4] [hold=1.2] [gap=0.35] [yaw=deg]     swing, let go, swing again: a run through the trees
    ///   web skill &lt;level&gt;                   sets the Webslinging skill (0-100)
    ///   web sense                          a spider-sense tingle from behind (to see the look)
    ///   web pose &lt;flag|none&gt; [crawl]       the animator flag held on a web (or on a wall), to try the game's poses
    ///   web hand left|right                the hand webs leave from
    ///   web watch &lt;move&gt; [shots=0.2,0.6,1] [for=60]   waits for the player to do the move, then takes pictures from its start
    /// Everything but status changes the game, so it follows the game's cheat rule (Shared/TestCommands.cs).
    /// </summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "web", "web status | try swing|zip|crawl|chain [lead=] [hold=] [yaw=] [pitch=] [shots=] | skill <level> | sense | pose <flag|none> [crawl] | hand left|right | watch <move>: what a web would take now; press the keys for a move and report what happened",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: web command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the web command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
            _claudeTools = null;
            _next = 0f;
        }

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray();   // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            if (sub != "status" && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("web " + sub)); yield break; }

            switch (sub)
            {
                case "status": output(Status(player)); yield break;
                case "skill":
                {
                    if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float level)) { error("web skill <level 0-100>"); yield break; }
                    object entry = AccessTools.Method(typeof(Skills), "GetSkill")?.Invoke(player.GetSkills(), new object[] { Skill.Type });
                    if (!(entry is Skills.Skill s)) { error("could not reach the skill"); yield break; }
                    s.m_level = Mathf.Clamp(level, 0f, 100f);
                    s.m_accumulator = 0f;
                    output(new JObject { ["skill"] = Skill.Level(player), ["factor"] = Math.Round(Skill.Factor(player), 2) });
                    yield break;
                }
                case "sense":
                    Sense.Tingle(player.transform.position - Anchors.LookFlat(player) * 8f + Vector3.up, "test");
                    output(new JObject { ["tingled"] = true, ["from"] = "behind" });
                    yield break;
                case "pose":
                {
                    string flag = args.Length > 1 ? args[1] : "grappling";
                    if (flag == "none") flag = "";
                    if (args.Contains("crawl")) Look.CrawlPose = flag; else Look.WebPose = flag;
                    output(new JObject { ["webPose"] = Look.WebPose, ["crawlPose"] = Look.CrawlPose });
                    yield break;
                }
                case "hand":
                {
                    string which = args.Length > 1 ? args[1].ToLowerInvariant() : "auto";
                    Look.HandTest = which == "left" ? 1 : which == "right" ? 2 : 0;
                    output(new JObject { ["hand"] = HandName() });
                    yield break;
                }
                case "bones":
                    output(new JObject { ["bones"] = Body.Describe(player) });
                    yield break;
                case "flip":
                    Body.Flip(player);
                    output(new JObject { ["flipped"] = true, ["flips"] = Body.Flips });
                    yield break;
                case "splat":
                {
                    // a web patch on whatever is under the crosshair (only the look: nothing moves)
                    Camera cam = Utils.GetMainCamera();
                    if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit at, 60f, Anchors.Mask, QueryTriggerInteraction.Ignore)) { error("nothing under the crosshair within 60 m"); yield break; }
                    Fx.SplatAt(Look.Hand(player), at.point);
                    output(new JObject { ["splat"] = Vec(at.point), ["on"] = Name(at.collider) });
                    yield break;
                }
                case "watch":
                {
                    if (args.Length < 2 || !Enum.TryParse(args[1], true, out Move target) || target == Move.None) { error("web watch <Swing|Zip|Crawl|Fly> [shots=0.2,0.6,1] [for=60]"); yield break; }
                    float seconds = Option(args, "for", 60f), until = Time.time + seconds;
                    while (Time.time < until && Player.m_localPlayer == player && Web.State != target) yield return null;
                    if (Web.State != target) { error($"no {target} within {seconds:0} s"); yield break; }
                    string times = args.FirstOrDefault(x => x.StartsWith("shots="))?.Substring(6) ?? "0.2,0.6,1";
                    IEnumerator shots = Shots(target.ToString().ToLowerInvariant(), times);
                    while (shots.MoveNext()) yield return shots.Current;
                    output(new JObject { ["watched"] = target.ToString(), ["shots"] = times, ["folder"] = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots") });
                    yield break;
                }
                case "sound":
                {
                    // every sound in turn (or one), a second apart, by the player: to hear what they sound like
                    var names = new JArray();
                    foreach (Sfx sfx in (Sfx[])Enum.GetValues(typeof(Sfx)))
                    {
                        if (args.Length > 1 && !sfx.ToString().Equals(args[1], StringComparison.OrdinalIgnoreCase)) continue;
                        Sounds.Play(sfx, player.transform.position + Vector3.up * 1.5f);
                        names.Add(sfx.ToString());
                        float next = Time.time + 1f;
                        while (Time.time < next) yield return null;
                    }
                    output(new JObject { ["played"] = names });
                    yield break;
                }
                case "try": break;
                default: error("web status | try swing|zip|crawl|chain | skill <level> | sense | pose <flag> | hand left|right|auto | watch <move> | sound [name] | bones | flip | splat"); yield break;
            }

            string move = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (Array.IndexOf(new[] { "swing", "zip", "crawl", "chain" }, move) < 0) { error("web try swing|zip|crawl|chain [lead=] [hold=] [yaw=] [pitch=] [shots=]"); yield break; }
            if (Controls.Driving != null) { error("a try is already running"); yield break; }
            if (!player.IsOnGround()) { error("stand on the ground first"); yield break; }
            if (args.Any(a => a.StartsWith("yaw=") || a.StartsWith("pitch=")))
            {
                // face this way first (as the mouse would turn the player and the camera)
                float yaw = Option(args, "yaw", player.transform.rotation.eulerAngles.y);
                player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                player.SetMouseLookForward(true);
                player.SetMouseLook(new Vector2(0f, Option(args, "pitch", 0f)));
                yield return new WaitForSeconds(0.2f);   // (the camera follows)
            }
            string shotsArg = args.FirstOrDefault(x => x.StartsWith("shots="));
            if (shotsArg != null && Plugin.Instance != null) Plugin.Instance.StartCoroutine(Shots(move, shotsArg.Substring(6)));

            float now = Time.time, stamina = player.GetStamina(), skill = Skill.Level(player);
            Vector3 from = player.transform.position;
            int before = Web.Done.Count;
            float lastAt = Web.Done.Count > 0 ? Web.Done[Web.Done.Count - 1].Started : -1f;
            var drive = new Controls.Drive { Move = new Vector3(Option(args, "side", 0f), 0f, Option(args, "forward", 1f)) };
            float end;
            if (move == "swing")
            {
                float lead = Option(args, "lead", 0.45f), hold = Option(args, "hold", 2.5f);
                drive.Jumps.Add(now);
                drive.Jumps.Add(now + lead);
                drive.HoldJumpFrom = now + lead;
                drive.HoldJumpUntil = now + lead + hold;
                end = now + lead + hold + 0.1f;
            }
            else if (move == "chain")
            {
                int count = Mathf.Clamp((int)Option(args, "count", 4f), 1, 12);
                float hold = Option(args, "hold", 1.2f), gap = Option(args, "gap", 0.35f), t = now + Option(args, "lead", 0.45f);
                drive.Jumps.Add(now);
                drive.HoldJumpFrom = t;
                for (int i = 0; i < count; i++) { drive.Jumps.Add(t); t += hold + gap; }
                // (held through each swing: the gaps let go; Drive holds one span, so the chain is driven below)
                drive.HoldJumpUntil = -1f;
                end = t;
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Chain(drive, now + Option(args, "lead", 0.45f), count, hold, gap));
            }
            else if (move == "zip")
            {
                float lead = Option(args, "lead", 0.4f);
                drive.Move = Vector3.zero;
                drive.Jumps.Add(now);
                drive.Zips.Add(now + lead);
                end = now + lead + 0.1f;
            }
            else
            {
                float hold = Option(args, "hold", 2f);
                drive.Jumps.Add(now);
                drive.Move = new Vector3(0f, 0f, 1f);
                end = now + 0.6f;
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Crawl(drive, now + 0.6f, hold, Option(args, "side", 0f)));
                end = now + 0.6f + hold;
            }
            drive.Until = end;
            Controls.Driving = drive;

            float topSpeed = 0f, topY = from.y;
            // then wait for the landing (or a wall), at most 10 s more
            while (Time.time < end + 10f)
            {
                topSpeed = Mathf.Max(topSpeed, player.GetVelocity().magnitude);
                topY = Mathf.Max(topY, player.transform.position.y);
                if (Time.time > end && (player.IsOnGround() || Web.State == Move.Crawl) && Web.State != Move.Swing && Web.State != Move.Zip) break;
                yield return new WaitForFixedUpdate();
            }
            Controls.Driving = null;
            yield return new WaitForSeconds(0.3f);
            Vector3 to = player.transform.position;
            var moves = new JArray();
            foreach (Web.Record r in Web.Done.Where(r => r.Started >= now - 0.01f && r.Started > lastAt))
                moves.Add(new JObject
                {
                    ["move"] = r.Move, ["ended"] = r.Ended, ["seconds"] = Math.Round(r.Seconds, 2), ["topSpeed"] = Math.Round(r.TopSpeed, 1),
                    ["distance"] = Math.Round(r.Distance, 1), ["rise"] = Math.Round(r.Rise, 1), ["stamina"] = Math.Round(r.Stamina, 1),
                    ["releaseSpeed"] = Math.Round(r.ReleaseSpeed, 1), ["perfect"] = r.Perfect,
                });
            output(new JObject
            {
                ["try"] = move,
                ["moves"] = moves,
                ["state"] = Web.State.ToString(),
                ["from"] = Vec(from), ["to"] = Vec(to),
                ["distance"] = Math.Round(Flat(to - from), 1), ["highest"] = Math.Round(topY - from.y, 1), ["topSpeed"] = Math.Round(topSpeed, 1),
                ["stamina"] = Math.Round(stamina - player.GetStamina(), 1), ["health"] = Math.Round(player.GetHealth(), 1),
                ["skill"] = Math.Round(Skill.Level(player) - skill, 2), ["refused"] = Web.LastRefusal,
            });
        }

        /// <summary>A chain: Jump held for each swing, let go between them (Drive has one hold span, so each is set as the last ends).</summary>
        private static IEnumerator Chain(Controls.Drive drive, float start, int count, float hold, float gap)
        {
            float t = start;
            for (int i = 0; i < count; i++)
            {
                drive.HoldJumpFrom = t;
                drive.HoldJumpUntil = t + hold;
                while (Time.time < t + hold + gap) { if (Controls.Driving != drive) yield break; yield return null; }
                t += hold + gap;
            }
        }

        /// <summary>A crawl: once on the wall, forward (up) and the side held for a while.</summary>
        private static IEnumerator Crawl(Controls.Drive drive, float from, float hold, float side)
        {
            while (Time.time < from) { if (Controls.Driving != drive) yield break; yield return null; }
            drive.Move = new Vector3(side, 0f, 1f);
        }

        private static JObject Status(Player p)
        {
            float f = Skill.Factor(p);
            float reach = Mathf.Lerp(Plugin.WebReach.Value, Plugin.WebReachAt100.Value, f);
            float zipReach = Mathf.Lerp(Plugin.ZipReach.Value, Plugin.ZipReachAt100.Value, f);
            var o = new JObject
            {
                ["state"] = Web.State.ToString(), ["since"] = Math.Round(Web.Since, 2), ["stuck"] = Web.Stuck, ["rope"] = Math.Round(Web.Length, 1),
                ["canAct"] = Web.CanAct(p, out string why), ["why"] = why ?? "", ["otherModBusy"] = Compat.Busy(p),
                ["onGround"] = p.IsOnGround(), ["velocity"] = Vec(p.GetVelocity()), ["stamina"] = Math.Round(p.GetStamina(), 1),
                ["skill"] = Math.Round(Skill.Level(p), 2), ["webReach"] = Math.Round(reach, 1), ["zipReach"] = Math.Round(zipReach, 1),
                ["airZipsUsed"] = Web.AirZipsUsed, ["spiderSenseTingles"] = Sense.Tingles, ["lastTingle"] = Sense.Last,
                ["webPose"] = Look.WebPose, ["crawlPose"] = Look.CrawlPose, ["hand"] = HandName(), ["cruise"] = Math.Round(Web.Cruise(p), 1),
                ["shootersWorn"] = Shooters.WornBy(p), ["shootersItem"] = Shooters.Registered, ["wristsDressed"] = Shooters.Dressed,
            };
            if (Anchors.SwingAnchor(p, reach, out Anchors.Spot spot))
                o["swingAnchor"] = new JObject { ["at"] = Vec(spot.Point), ["what"] = Name(spot.What), ["distance"] = Math.Round(Vector3.Distance(Anchors.Pivot(p), spot.Point), 1),
                    ["up"] = Math.Round(spot.Point.y - Anchors.Pivot(p).y, 1), ["rope"] = Math.Round(spot.Rope, 1), ["arcClear"] = spot.ArcClear };
            else { o["swingAnchor"] = null; o["whyNoSwing"] = Anchors.LastMiss; }
            if (Anchors.ZipTarget(p, zipReach, out RaycastHit hit, out Anchors.Kind kind))
                o["zipTarget"] = new JObject { ["at"] = Vec(hit.point), ["kind"] = kind.ToString(), ["what"] = Name(hit.collider), ["distance"] = Math.Round(Vector3.Distance(Anchors.Pivot(p), hit.point), 1) };
            else o["zipTarget"] = null;
            if (Anchors.Find(p, Anchors.LookFlat(p), 1.5f, out Anchors.Wall wall))
                o["wallAhead"] = new JObject { ["what"] = Name(wall.Collider), ["gap"] = Math.Round(wall.Gap, 2), ["normal"] = Vec(wall.Normal) };
            var last = new JArray();
            foreach (Web.Record r in Web.Done.Skip(Math.Max(0, Web.Done.Count - 12)))
                last.Add($"{r.Move}{(r.Detail.Length > 0 ? " (" + r.Detail + ")" : "")}: {r.Ended} ({r.Seconds:0.0} s, top {r.TopSpeed:0} m/s, {r.Distance:0} m, rise {r.Rise:0.0} m{(r.Perfect ? ", perfect" : "")})");
            o["lastMoves"] = last;
            return o;
        }

        private static string HandName() => Look.HandTest == 1 ? "left" : Look.HandTest == 2 ? "right" : "the anchor's side";

        internal static string Name(Collider c)
        {
            if (c == null) return "";
            ZNetView nv = c.GetComponentInParent<ZNetView>();
            return nv != null ? Utils.GetPrefabName(nv.gameObject) : c.gameObject.name;
        }

        private static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private static IEnumerator Shots(string move, string times)
        {
            float start = Time.time;
            string dir = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots");
            System.IO.Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("HHmmss");
            int n = 0;
            // the game's ScreenCapture module, found by name (the mod doesn't reference it just for this test option)
            MethodInfo capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")?.GetMethod("CaptureScreenshot", new[] { typeof(string) });
            foreach (string t in times.Split(','))
            {
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float at)) continue;
                while (Time.time - start < at) yield return null;
                capture?.Invoke(null, new object[] { System.IO.Path.Combine(dir, $"web-{move}-{stamp}-{++n}.png") });
            }
        }

        private static float Option(string[] args, string name, float fallback)
        {
            string a = args.FirstOrDefault(x => x.StartsWith(name + "="));
            return a != null && float.TryParse(a.Substring(name.Length + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
