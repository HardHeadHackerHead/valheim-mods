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

namespace Parkour
{
    /// <summary>
    /// A "parkour" command for the Claude Tools mod (its request mailbox), so an AI assistant can test the moves with nobody at the keyboard:
    ///   parkour status                      what the player can do now: the walls and ledge around, the skill, the settings, the last moves
    ///   parkour try wallrun|walljump|climb|ledge [left|right] [yaw=degrees] [lead=seconds] [hold=seconds] [shots=0.8,1.2]
    ///                                       presses the keys for the move (sprint, forward, jump...) through Controls, as a player would,
    ///                                       and reports what happened: the moves done, how high and far, the stamina, the skill
    ///   parkour try roll [height=6] [auto]  lifts the player that high, moving forward, and presses crouch just before the landing (or
    ///                                       not, with "auto", to try the auto-roll): did it roll, the fall damage taken
    ///   parkour try shimmy [left|right] [hold=2]   facing a ledge: jumps and catches it with forward, then holds left or right along
    ///                                       it, and lets go with crouch: how far it went along the edge, and why it stopped
    ///   parkour try slide [lead=0.8] [hold=1.6] [side=degrees]    sprints forward, then presses and holds crouch: the slide's distance, top speed, and
    ///                                       why it ended
    ///   parkour watch &lt;move&gt; [shots=0.15,0.35,0.6] [for=60]   waits for the player to do the move, then takes pictures from its start
    ///   parkour skill &lt;level&gt;              sets the Parkour skill (0-100)
    /// "try" and "skill" change the game, so they follow the game's cheat rule (Shared/TestCommands.cs).
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
                register?.Invoke(null, new object[] { Plugin.Name, "parkour", "parkour status | try <wallrun|walljump|climb|ledge> [left|right] [yaw=degrees] [lead=seconds] [hold=seconds] | try roll [height=6] [auto] | try shimmy [left|right] [hold=2] | try slide [lead=0.8] [hold=1.6] | skill <level>: what the player can do now (walls, ledge, skill, bar, settings, last moves); press the keys for a move and report what happened; set the Parkour skill",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: parkour command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the parkour command to Claude Tools: " + e.Message); }
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
            // only status just reads; the rest changes the game, so it follows the game's cheat rule (Shared/TestCommands.cs)
            if (sub != "status" && !DHack.Shared.TestCommands.CheatsAllowed()) { error(DHack.Shared.TestCommands.Refusal("parkour " + sub)); yield break; }

            if (sub == "status") { output(Status(player)); yield break; }
            if (sub == "skill")
            {
                if (args.Length < 2 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float level)) { error("parkour skill <level 0-100>"); yield break; }
                object entry = AccessTools.Method(typeof(Skills), "GetSkill")?.Invoke(player.GetSkills(), new object[] { Skill.Type });
                if (!(entry is Skills.Skill s)) { error("could not reach the skill"); yield break; }
                s.m_level = Mathf.Clamp(level, 0f, 100f);
                s.m_accumulator = 0f;
                output(new JObject { ["skill"] = Skill.Level(player), ["factor"] = Math.Round(Skill.Factor(player), 2) });
                yield break;
            }
            if (sub == "watch")
            {
                // parkour watch <move> [shots=0.15,0.35,0.6] [for=60]: wait for the player to do the move themselves, then take pictures
                // (requests run one at a time and arrive late: this times them from the move's own start)
                if (args.Length < 2 || !Enum.TryParse(args[1], true, out Move target) || target == Move.None) { error("parkour watch <" + string.Join("|", Enum.GetNames(typeof(Move)).Where(n => n != "None")) + "> [shots=0.15,0.35,0.6] [for=60]"); yield break; }
                float seconds = Option(args, "for", 60f), until = Time.time + seconds;
                while (Time.time < until && Player.m_localPlayer == player && Moves.State != target) yield return null;
                if (Moves.State != target) { error($"no {target} within {seconds:0} s"); yield break; }
                string times = args.FirstOrDefault(x => x.StartsWith("shots="))?.Substring(6) ?? "0.15,0.35,0.6";
                IEnumerator shots = Shots(target.ToString().ToLowerInvariant(), times);
                while (shots.MoveNext()) yield return shots.Current;
                output(new JObject { ["watched"] = target.ToString(), ["shots"] = times, ["folder"] = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots") });
                yield break;
            }
            if (sub != "try") { error("parkour status | try <wallrun|walljump|climb|ledge|roll> ... | skill <level>"); yield break; }

            string move = args.Length > 1 ? args[1].ToLowerInvariant() : "";
            if (Array.IndexOf(new[] { "wallrun", "walljump", "climb", "ledge", "roll", "shimmy", "slide" }, move) < 0) { error("parkour try <wallrun|walljump|climb|ledge> [left|right] [yaw=degrees] [lead=seconds] [hold=seconds] | try roll [height=6] [auto] | try shimmy [left|right] [hold=2] | try slide [lead=0.8] [hold=1.6]"); yield break; }
            if (Controls.Driving != null) { error("a try is already running"); yield break; }
            // pictures during the move (requests run one at a time, so a "shot" request can only come after it): shots=0.8,1.2 seconds
            string shotsArg = args.FirstOrDefault(x => x.StartsWith("shots="));
            if (shotsArg != null && Plugin.Instance != null) Plugin.Instance.StartCoroutine(Shots(move, shotsArg.Substring(6)));
            if (move == "roll")
            {
                IEnumerator roll = TryRoll(player, args, output, error);
                while (roll.MoveNext()) yield return roll.Current;
                yield break;
            }
            float side = args.Contains("left") ? -1f : 1f;
            float lead = Option(args, "lead", move == "ledge" ? 0.1f : 0.6f), hold = Option(args, "hold", move == "ledge" ? 2f : 2.5f);
            if (args.Any(a => a.StartsWith("yaw=")))
            {
                // face this way first (as the mouse would turn the player)
                player.transform.rotation = Quaternion.Euler(0f, Option(args, "yaw", 0f), 0f);
                player.SetMouseLookForward(false);
                yield return new WaitForFixedUpdate();
            }
            if (move == "shimmy" || move == "slide")
            {
                IEnumerator run = move == "shimmy" ? TryShimmy(player, args, side, output, error) : TrySlide(player, args, output, error);
                while (run.MoveNext()) yield return run.Current;
                yield break;
            }

            // the keys: sprint and forward (angled toward the wall's side for a wall run), jump after the run-up, and for a wall jump a second
            // jump half a second into the run (and a third, which must be refused: the same wall twice)
            float now = Time.time;
            var drive = new Controls.Drive { Run = move != "ledge", Until = now + lead + hold };
            drive.Move = move == "wallrun" || move == "walljump" ? new Vector3(0.6f * side, 0f, 1f).normalized : Vector3.forward;
            drive.Jumps.Add(now + lead);
            if (move == "walljump") { drive.Jumps.Add(now + lead + 0.55f); drive.Jumps.Add(now + lead + 0.8f); }

            Vector3 from = player.transform.position;
            float stamina = player.GetStamina(), skill = Skill.Level(player), top = from.y, farthest = 0f;
            int before = Moves.Done.Count;
            var states = new JArray();
            Move last = Moves.State;
            Controls.Driving = drive;
            while (Controls.Driving == drive && Time.time <= drive.Until + 0.1f && Player.m_localPlayer == player)
            {
                if (Moves.State != last) { last = Moves.State; states.Add($"{Time.time - now:0.00}s {last}"); }
                top = Mathf.Max(top, player.transform.position.y);
                farthest = Mathf.Max(farthest, Flat(player.transform.position - from));
                yield return null;
            }
            if (Controls.Driving == drive) Controls.Driving = null;
            // let it land
            float settle = Time.time + 2f;
            while (Time.time < settle && Player.m_localPlayer == player && (Moves.State != Move.None || !player.IsOnGround()))
            {
                if (Moves.State != last) { last = Moves.State; states.Add($"{Time.time - now:0.00}s {last}"); }
                top = Mathf.Max(top, player.transform.position.y);
                yield return null;
            }

            var done = Moves.Done.Skip(before).ToList();
            string wanted = move == "wallrun" ? "WallRun" : move == "walljump" ? "WallJump" : move == "climb" ? "Climb" : "Hang";
            output(new JObject
            {
                ["move"] = move,
                ["happened"] = done.Any(r => r.Move == wanted),
                ["pulledUp"] = done.Any(r => r.Move == "PullUp" && r.Ended == "pulled up"),
                ["states"] = states,
                ["moves"] = new JArray(done.Select(Json)),
                ["rose"] = Math.Round(top - from.y, 2),
                ["endedHigher"] = Math.Round(player.transform.position.y - from.y, 2),
                ["farthest"] = Math.Round(farthest, 2),
                ["stamina"] = Math.Round(stamina - player.GetStamina(), 1),
                ["skill"] = $"{skill:0} -> {Skill.Level(player):0}",
                ["health"] = Math.Round(player.GetHealth(), 1),
                ["lastRefusal"] = Moves.LastRefusal,
            });
        }

        /// <summary>Jump at the ledge in front holding forward; once hanging, hold left or right along it; then let go with crouch.</summary>
        private static IEnumerator TryShimmy(Player player, string[] args, float side, Action<JObject> output, Action<string> error)
        {
            float hold = Option(args, "hold", 2f), now = Time.time;
            int before = Moves.Done.Count;
            var drive = new Controls.Drive { Move = Vector3.forward, Until = now + 2f + hold + 1f };
            drive.Jumps.Add(now + 0.05f);
            Controls.Driving = drive;
            float caught = -1f;
            Vector3 hangFrom = Vector3.zero;
            string stopped = "";
            while (Controls.Driving == drive && Player.m_localPlayer == player)
            {
                if (caught < 0f)
                {
                    if (Moves.State == Move.Hang) { caught = Time.time; hangFrom = player.transform.position; drive.Move = new Vector3(side, 0f, 0f); }
                    else if (Time.time - now > 2f) break;   // (never caught an edge)
                }
                else if (Moves.State != Move.Hang) break;
                else if (Time.time - caught > hold)
                {
                    stopped = Moves.LastRefusal;
                    drive.Move = Vector3.zero;
                    drive.Crouches.Add(Time.time);   // let go
                    caught = float.MaxValue;
                }
                yield return null;
            }
            float along = caught > 0f ? Flat(player.transform.position - hangFrom) : 0f;
            if (Controls.Driving == drive) Controls.Driving = null;
            var done = Moves.Done.Skip(before).ToList();
            output(new JObject
            {
                ["move"] = "shimmy",
                ["caught"] = caught > 0f,
                ["along"] = Math.Round(along, 2),
                ["stoppedBy"] = stopped,
                ["moves"] = new JArray(done.Select(Json)),
                ["lastRefusal"] = Moves.LastRefusal,
            });
        }

        /// <summary>Sprint forward, then press and hold crouch: a slide.</summary>
        private static IEnumerator TrySlide(Player player, string[] args, Action<JObject> output, Action<string> error)
        {
            float lead = Option(args, "lead", 0.8f), hold = Option(args, "hold", 1.6f), now = Time.time;
            int before = Moves.Done.Count;
            // side=degrees: slide at that angle to where the camera looks (a picture from the side)
            var drive = new Controls.Drive { Move = Quaternion.Euler(0f, Option(args, "side", 0f), 0f) * Vector3.forward, Run = true, Until = now + lead + hold + 0.3f, HoldCrouchFrom = now + lead, HoldCrouchUntil = now + lead + hold };
            drive.Crouches.Add(now + lead);
            Controls.Driving = drive;
            Rigidbody body = Moves.Body(player);
            float sprint = 0f, top = 0f, slid = 0f;
            Vector3 last = player.transform.position;
            while (Controls.Driving == drive && Player.m_localPlayer == player && Time.time <= drive.Until + 0.1f)
            {
                float speed = body != null ? new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z).magnitude : 0f;
                if (Moves.State == Move.Slide) { top = Mathf.Max(top, speed); slid += Flat(player.transform.position - last); }
                else if (Time.time < now + lead) sprint = speed;
                last = player.transform.position;
                yield return null;
            }
            if (Controls.Driving == drive) Controls.Driving = null;
            var done = Moves.Done.Skip(before).ToList();
            output(new JObject
            {
                ["move"] = "slide",
                ["slid"] = done.Any(r => r.Move == "Slide"),
                ["sprintSpeed"] = Math.Round(sprint, 2),
                ["topSpeed"] = Math.Round(top, 2),
                ["distance"] = Math.Round(slid, 2),
                ["moves"] = new JArray(done.Select(Json)),
                ["lastRefusal"] = Moves.LastRefusal,
            });
        }

        /// <summary>Up into the air, moving forward, and crouch pressed just before the ground comes (unless "auto").</summary>
        private static IEnumerator TryRoll(Player player, string[] args, Action<JObject> output, Action<string> error)
        {
            float height = Mathf.Clamp(Option(args, "height", 6f), 1f, 40f);
            bool auto = args.Contains("auto");
            Rigidbody body = Moves.Body(player);
            if (body == null) { error("no body"); yield break; }
            if (!player.IsOnGround()) { error("stand on the ground first"); yield break; }
            Vector3 from = player.transform.position;
            float health = player.GetHealth(), stamina = player.GetStamina(), skill = Skill.Level(player), now = Time.time, window = Roll.Window(player);
            int before = Moves.Done.Count;
            player.transform.position = from + Vector3.up * height;
            body.position = player.transform.position;
            body.linearVelocity = player.transform.forward * 4f;
            Physics.SyncTransforms();

            var drive = new Controls.Drive { Move = Vector3.forward, Until = now + 8f };
            Controls.Driving = drive;
            float pressedAt = -1f, landedAt = -1f, fallStart = now;
            bool airborne = false;
            while (Controls.Driving == drive && Player.m_localPlayer == player && Time.time < drive.Until)
            {
                bool ground = player.IsOnGround();
                if (!ground) airborne = true;
                if (!auto && pressedAt < 0f && !ground
                    && Physics.Raycast(player.transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 60f, Walls.Mask, QueryTriggerInteraction.Ignore))
                {
                    // when the ground is about half the roll window away (falling speed and gravity), press crouch
                    float down = Mathf.Max(0f, -body.linearVelocity.y), g = -Physics.gravity.y, d = Mathf.Max(0f, hit.distance - 0.1f);
                    float t = (-down + Mathf.Sqrt(down * down + 2f * g * d)) / g;
                    if (t <= window * 0.5f) { drive.Crouches.Add(Time.time); pressedAt = Time.time; }
                }
                if (airborne && ground && landedAt < 0f) landedAt = Time.time;
                if (landedAt > 0f && Time.time - landedAt > 1.2f) break;
                yield return null;
            }
            if (Controls.Driving == drive) Controls.Driving = null;
            var done = Moves.Done.Skip(before).ToList();
            output(new JObject
            {
                ["move"] = auto ? "roll (auto)" : "roll",
                ["rolled"] = done.Any(r => r.Move == "Roll"),
                ["moves"] = new JArray(done.Select(Json)),
                ["fell"] = Math.Round(height, 1),
                ["crouchBeforeLanding"] = pressedAt > 0f && landedAt > 0f ? Math.Round(landedAt - pressedAt, 2) : -1,
                ["window"] = Math.Round(window, 2),
                ["safeHeight"] = Math.Round(Roll.SafeHeight(player), 1),
                ["autoUnlocked"] = Roll.AutoUnlocked(player),
                ["healthLost"] = Math.Round(health - player.GetHealth(), 1),
                ["stamina"] = Math.Round(stamina - player.GetStamina(), 1),
                ["skill"] = $"{skill:0} -> {Skill.Level(player):0}",
                ["seconds"] = Math.Round(Time.time - fallStart, 2),
                ["lastRefusal"] = Moves.LastRefusal,
            });
        }

        private static JObject Status(Player p)
        {
            Vector3 fwd = p.transform.forward, right = p.transform.right;
            var walls = new JObject();
            foreach (var (name, dir) in new[] { ("front", fwd), ("left", -right), ("right", right), ("back", -fwd) })
                walls[name] = Walls.Find(p, dir, 3f, out Walls.Wall w)
                    ? new JObject { ["gap"] = Math.Round(w.Gap, 2), ["angleToWall"] = Math.Round(Mathf.Asin(Mathf.Clamp(Vector3.Dot(fwd, -w.Normal), -1f, 1f)) * Mathf.Rad2Deg), ["layer"] = LayerMask.LayerToName(w.Collider.gameObject.layer), ["object"] = w.Collider.transform.root.name }
                    : null;
            JObject ledge = null;
            if (Walls.FindLedge(p, fwd, out Walls.Ledge l))
                ledge = new JObject { ["height"] = Math.Round(l.Height, 2), ["away"] = Math.Round(Flat(l.Edge - p.transform.position), 2) };
            var settings = new JObject
            {
                ["enabled"] = Plugin.Enabled.Value, ["wallRun"] = Plugin.WallRun.Value, ["wallJump"] = Plugin.WallJump.Value, ["wallClimb"] = Plugin.WallClimb.Value,
                ["ledgeGrab"] = Plugin.LedgeGrab.Value, ["pullUp"] = Plugin.PullUp.Value, ["landingRoll"] = Plugin.LandingRoll.Value, ["autoRoll"] = Plugin.AutoRoll.Value,
                ["bar"] = Plugin.ShowBar.Value, ["hints"] = Plugin.Hints.Value, ["fromServer"] = Plugin.Synced != null && Plugin.Synced.FromServer,
                ["wallRunSeconds"] = $"{Plugin.WallRunMin.Value}-{Plugin.WallRunMax.Value}", ["wallRunSpeedPercentAt100"] = Plugin.WallRunSpeedBonus.Value,
                ["climbHeight"] = $"{Plugin.ClimbMin.Value}-{Plugin.ClimbMax.Value}",
                ["wallJumpsInARow"] = $"{Plugin.KicksAtSkill0.Value} +1 every {Plugin.KickEveryLevels.Value} levels, at most {Plugin.KicksMax.Value}",
                ["roll"] = $"window {Plugin.RollWindowMin.Value}-{Plugin.RollWindowMax.Value} s, safe {Plugin.RollSafeMin.Value}-{Plugin.RollSafeMax.Value} m, {Plugin.RollDamageShare.Value}% above, auto from level {Plugin.AutoRollLevel.Value}",
                ["wetShorterPercent"] = Plugin.WetShorter.Value, ["skillGain"] = Plugin.SkillGain.Value,
                ["stamina"] = $"run {Plugin.WallRunCost.Value} + {Plugin.WallRunCostPerSecond.Value}/s, kick: the game's jump, climb {Plugin.ClimbCost.Value}, hang {Plugin.HangCostPerSecond.Value}/s, pull-up {Plugin.MantleCost.Value}, roll: the game's dodge, skill cut {Plugin.SkillCostCut.Value}%",
            };
            return new JObject
            {
                ["state"] = Moves.State.ToString(),
                ["canMove"] = Moves.WhyNot(p) ?? "yes",
                ["lastRefusal"] = Moves.LastRefusal,
                ["onGround"] = p.IsOnGround(),
                ["running"] = p.IsRunning(),
                ["wet"] = p.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectWet),
                ["stamina"] = $"{p.GetStamina():0}/{p.GetMaxStamina():0}",
                ["skill"] = new JObject { ["type"] = (int)Skill.Type, ["level"] = Skill.Level(p), ["factor"] = Math.Round(Skill.Factor(p), 2), ["name"] = Localization.instance != null ? Localization.instance.Localize("$skill_" + ((int)Skill.Type)) : "" },
                ["wallJumps"] = $"{Moves.KicksLeft(p)} left of {Moves.KicksAllowed(p)}",
                ["roll"] = new JObject { ["window"] = Math.Round(Roll.Window(p), 2), ["safeHeight"] = Math.Round(Roll.SafeHeight(p), 1), ["autoUnlocked"] = Roll.AutoUnlocked(p) },
                ["bar"] = new JObject { ["showing"] = Bar.Showing, ["value"] = Math.Round(Bar.Value, 2) },
                ["walls"] = walls,
                ["ledgeInFront"] = ledge,
                ["otherMods"] = Compat.Summary(),
                ["settings"] = settings,
                ["lastMoves"] = new JArray(Moves.Done.Skip(Math.Max(0, Moves.Done.Count - 6)).Select(Json)),
            };
        }

        private static JObject Json(Moves.Record r) => new JObject
        {
            ["move"] = r.Move, ["seconds"] = Math.Round(r.Seconds, 2), ["rise"] = Math.Round(r.Rise, 2), ["distance"] = Math.Round(r.Distance, 2),
            ["stamina"] = Math.Round(r.Stamina, 1), ["ended"] = r.Ended,
        };

        /// <summary>Screenshots (with the HUD) at these seconds after the try starts, into BepInEx/claude/shots/parkour-&lt;move&gt;-&lt;n&gt;.png.</summary>
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
                capture?.Invoke(null, new object[] { System.IO.Path.Combine(dir, $"parkour-{move}-{stamp}-{++n}.png") });
            }
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private static float Option(string[] args, string name, float fallback)
        {
            string a = args.FirstOrDefault(x => x.StartsWith(name + "="));
            return a != null && float.TryParse(a.Substring(name.Length + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
