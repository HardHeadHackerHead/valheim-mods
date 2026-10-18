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

namespace Skateboards
{
    /// <summary>
    /// A "skate" command for the Claude Tools mod (its request mailbox), so an AI assistant can test the riding with nobody at the keyboard:
    ///   skate status                       on the board or not, speed, surface, the combo, the score, the last things that happened
    ///   skate drive [start] [yaw=deg] [w=0-2] [s=..] [a=..] [d=..] [run=..] [jump=from-to] [grab=from-to] [crouch=from-to]
    ///               [attack=t,t] [use=t] [for=5] [shots=t,t]
    ///                                      presses the keys for that long (seconds from now: held from-to, pressed at t), then reports
    ///                                      what happened: the events, top speed, highest air, where it ended, the combo and score
    ///   skate skill &lt;level&gt;                 sets the Skateboarding skill (0-100)
    /// Everything but status changes the game, so it follows the game's cheat rule (Shared/TestCommands.cs).
    /// </summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;
        private static readonly AccessTools.FieldRef<Character, Quaternion> LookYaw = AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");

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
                register?.Invoke(null, new object[] { Plugin.Name, "skate", "skate status | drive [start] [yaw=] [w=a-b] [s=] [a=] [d=] [run=] [jump=a-b] [grab=a-b] [crouch=a-b] [attack=t,t] [use=t] [for=5] [shots=t,t] | skill <level>: the riding now; press the keys for a run and report what happened",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: skate command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the skate command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
            _claudeTools = null;
            _next = 0f;
        }

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray();   // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            if (sub != "status" && sub != "rails" && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("skate " + sub)); yield break; }

            switch (sub)
            {
                case "status": output(Status(player)); yield break;
                case "skill":
                {
                    if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float level)) { error("skate skill <level 0-100>"); yield break; }
                    object entry = AccessTools.Method(typeof(Skills), "GetSkill")?.Invoke(player.GetSkills(), new object[] { Skill.Type });
                    if (!(entry is Skills.Skill s)) { error("could not reach the skill"); yield break; }
                    s.m_level = Mathf.Clamp(level, 0f, 100f);
                    s.m_accumulator = 0f;
                    output(new JObject { ["skill"] = Skill.Level(player), ["factor"] = Math.Round(Skill.Factor(player), 2) });
                    yield break;
                }
                case "rails":
                {
                    float radius = args.Length > 1 ? F(args[1]) : 8f;
                    Vector3 at = player.transform.position;
                    var list = new JArray();
                    foreach (Rails.Rail r in Rails.All(at, radius).OrderBy(r => Vector3.Distance(at, r.Point(Vector3.Dot(at - r.A, r.Dir)))).Take(25))
                    {
                        Vector3 mid = (r.A + r.B) / 2f;
                        list.Add($"{(r.Ledge ? "ledge" : "rail")} {r.Length:0.0} m on {r.Collider.transform.root.name}/{r.Collider.name}, middle ({mid.x:0.0}, {mid.y:0.00}, {mid.z:0.0}), {(mid - at).magnitude:0.0} m away, {Mathf.Atan2(r.Dir.x, r.Dir.z) * Mathf.Rad2Deg:0} deg, top {mid.y - at.y:+0.00;-0.00} over the feet");
                    }
                    output(new JObject { ["rails"] = list });
                    yield break;
                }
                case "drive":
                {
                    IEnumerator run = Drive(player, args.Skip(1).ToArray(), output, error);
                    while (run.MoveNext()) yield return run.Current;
                    yield break;
                }
                default: error("skate status | drive ... | skill <level>"); yield break;
            }
        }

        private static IEnumerator Drive(Player p, string[] args, Action<JObject> output, Action<string> error)
        {
            if (!Board.InHand(p)) { error("hold a skateboard first (give skateboards_board, then equip it)"); yield break; }
            var d = new Controls.Drive { Start = Time.time };
            float length = 5f;
            string shots = null;
            bool start = false;
            try
            {
                foreach (string a in args)
                {
                    string[] kv = a.Split(new[] { '=' }, 2);
                    string key = kv[0].ToLowerInvariant(), val = kv.Length > 1 ? kv[1] : "";
                    (float, float) Span() { string[] ab = val.Split('-'); return (F(ab[0]), ab.Length > 1 ? F(ab[1]) : F(ab[0]) + 0.1f); }
                    switch (key)
                    {
                        case "start": start = true; break;
                        case "for": length = F(val); break;
                        case "shots": shots = val; break;
                        case "yaw":
                        {
                            Quaternion yaw = Quaternion.Euler(0f, F(val), 0f);
                            LookYaw(p) = yaw;
                            p.transform.rotation = yaw;
                            break;
                        }
                        case "w": { var s = Span(); d.Moves.Add((s.Item1, s.Item2, Vector3.forward)); break; }
                        case "s": { var s = Span(); d.Moves.Add((s.Item1, s.Item2, Vector3.back)); break; }
                        case "a": { var s = Span(); d.Moves.Add((s.Item1, s.Item2, Vector3.left)); break; }
                        case "d": { var s = Span(); d.Moves.Add((s.Item1, s.Item2, Vector3.right)); break; }
                        case "run": d.Runs.Add(Span()); break;
                        case "jump": d.Jumps.Add(Span()); break;
                        case "grab": d.Grabs.Add(Span()); break;
                        case "crouch": d.Crouches.Add(Span()); break;
                        case "attack": foreach (string t in val.Split(',')) d.Attacks.Add(F(t)); break;
                        case "use": d.Uses.Add(F(val)); break;
                        default: throw new FormatException("unknown option " + a);
                    }
                }
            }
            catch (Exception e) { error("skate drive: " + e.Message); yield break; }
            if (start && !Skate.Active) d.Attacks.Insert(0, 0f);
            d.Until = Time.time + length;
            int eventsFrom = Skate.Events.Count;
            long sessionBefore = Tricks.Session;
            int bailsBefore = Skate.Bails;
            Vector3 from = p.transform.position;
            Skate.TopSpeedSeen = Skate.MaxHeightSeen = 0f;
            Skate.Note("-- drive");
            int mark = Skate.EventCount;
            Controls.Driving = d;
            if (shots != null && Plugin.Instance != null) Plugin.Instance.StartCoroutine(Shots(shots));
            float maxY = from.y, minY = from.y;
            while (Time.time <= d.Until + 0.05f)
            {
                maxY = Mathf.Max(maxY, p.transform.position.y);
                minY = Mathf.Min(minY, p.transform.position.y);
                yield return null;
            }
            Controls.Driving = null;
            Vector3 to = p.transform.position;
            JObject o = Status(p);
            int fresh = Mathf.Min(Skate.EventCount - mark, Skate.Events.Count);
            o["events"] = new JArray(Skate.Events.Skip(Skate.Events.Count - fresh).Cast<object>().ToArray());
            o["topSpeed"] = Math.Round(Skate.TopSpeedSeen, 1);
            o["highestAir"] = Math.Round(Skate.MaxHeightSeen, 2);
            o["rose"] = Math.Round(maxY - from.y, 2);
            o["fell"] = Math.Round(from.y - minY, 2);
            o["moved"] = Math.Round(new Vector3(to.x - from.x, 0f, to.z - from.z).magnitude, 1);
            o["banked"] = Tricks.Session - sessionBefore;
            o["bails"] = Skate.Bails - bailsBefore;
            if (shots != null) o["shots"] = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots");
            output(o);
        }

        private static JObject Status(Player p)
        {
            Vector3 pos = p.transform.position;
            Skate.Probe(p, -Skate.Normal, 1f, out RaycastHit hit, out float gap);
            return new JObject
            {
                ["state"] = Skate.State.ToString(),
                ["holding"] = Board.InHand(p),
                ["position"] = new JArray(Math.Round(pos.x, 2), Math.Round(pos.y, 2), Math.Round(pos.z, 2)),
                ["speed"] = Math.Round(Skate.Speed, 2),
                ["velocity"] = new JArray(Math.Round(Skate.BodyOf(p).linearVelocity.x, 2), Math.Round(Skate.BodyOf(p).linearVelocity.y, 2), Math.Round(Skate.BodyOf(p).linearVelocity.z, 2)),
                ["fakie"] = Skate.Fakie,
                ["normal"] = new JArray(Math.Round(Skate.Normal.x, 2), Math.Round(Skate.Normal.y, 2), Math.Round(Skate.Normal.z, 2)),
                ["noseYaw"] = Math.Round(Skate.NoseYaw, 0),
                ["ground"] = hit.collider != null ? hit.collider.name + " gap " + gap.ToString("0.00", CultureInfo.InvariantCulture) : "none",
                ["balance"] = Math.Round(Skate.Balance, 2),
                ["combo"] = Tricks.Text(20),
                ["comboPoints"] = Math.Round(Tricks.Points),
                ["multiplier"] = Tricks.Multiplier,
                ["session"] = Tricks.Session,
                ["best"] = Tricks.Best(p),
                ["skill"] = Math.Round(Skill.Level(p), 1),
                ["look"] = Look.Describe(p),
                ["lastBail"] = Skate.LastBail,
                ["recent"] = new JArray(Skate.Events.Skip(Math.Max(0, Skate.Events.Count - 12)).Cast<object>().ToArray()),
            };
        }

        private static IEnumerator Shots(string times)
        {
            float start = Time.time;
            string dir = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots");
            System.IO.Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("HHmmss");
            int n = 0;
            MethodInfo capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule")?.GetMethod("CaptureScreenshot", new[] { typeof(string) });
            foreach (string t in times.Split(','))
            {
                if (!float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float at)) continue;
                while (Time.time - start < at) yield return null;
                capture?.Invoke(null, new object[] { System.IO.Path.Combine(dir, $"skate-{stamp}-{++n}.png") });
            }
        }
    }
}
