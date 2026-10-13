using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Jint;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// The "blockscript" command for the Claude Tools mod (its request mailbox and console): list, run, stop, allow, log, audit and the spike.
    /// Commands that change what runs follow the game's cheat rule (single player, the host, or devcommands), like the other mods' tests.
    /// </summary>
    internal static class Tools
    {
        private const string Help = "blockscript list | run <name> | stop <name|all> | allow <name> <power|all> | deny <name> <power|all> | log <name> [n] [debug|info|did|limit|warn|error|mine] [grep=text] | trace <name> on|off | stats | collab | audit | chat <text> | say <text> | autoreload [on|off] | scan | spike | panel [open|close|page ...] | server";
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            Mp.Backend.Hook();   // (listens for a server's answers and an admin's requests; every frame is cheap)
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Core.Name, "blockscript", Help, (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Core.Log.LogInfo("Claude Tools found: blockscript command added");
            }
            catch (Exception e) { Core.Log.LogWarning("Could not add the blockscript command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Core.Name }); }
            catch (Exception) { }
        }

        /// <summary>
        /// Who is connected and whether each is on the server's admin list (blockscript players), and putting one on it or taking one off
        /// (blockscript admin NAME [on|off]) through the game's own list, which saves adminlist.txt and is read again by the game within
        /// seconds, so nobody has to restart. The id comes from the player's own connection, never from what they typed.
        /// </summary>
        private static JObject Admins(string[] args)
        {
            var result = new JObject();
            ZNet net = ZNet.instance;
            if (net == null) { result["problem"] = "not in a world"; return result; }
            bool isAdminSub = args.Length > 0 && args[0].ToLowerInvariant() == "admin";
            if (isAdminSub && !(net.IsServer() || Cheats())) { result["problem"] = "only the server's owner can change the admin list"; return result; }
            System.Reflection.FieldInfo listField = typeof(ZNet).GetField("m_adminList", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object adminList = listField?.GetValue(net);
            var rows = new JArray();
            var found = new System.Collections.Generic.List<(string name, string id)>();
            foreach (ZNetPeer peer in net.GetPeers())
            {
                string host = peer?.m_rpc?.GetSocket()?.GetHostName() ?? "";
                string name = peer?.m_playerName ?? "";
                if (host.Length == 0) continue;
                found.Add((name, host));
                rows.Add(new JObject { ["name"] = name, ["id"] = host, ["admin"] = net.IsAdmin(host) });
            }
            string me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";
            result["players"] = rows;
            result["host"] = me;
            if (!isAdminSub) return result;

            string want = args.Length > 1 ? args[1] : "";
            bool turnOff = args.Length > 2 && args[2].ToLowerInvariant() == "off";
            var matches = found.Where(f => string.Equals(f.name, want, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) matches = found.Where(f => f.name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (want.Length == 0 || matches.Count != 1) { result["problem"] = want.Length == 0 ? "name a player: blockscript admin <name> [on|off]" : matches.Count == 0 ? "no connected player called '" + want + "' (blockscript players lists who is here)" : "more than one player matches '" + want + "': use the full name"; return result; }
            if (adminList == null) { result["problem"] = "this game version keeps its admin list somewhere else"; return result; }

            string id = matches[0].id;
            string plain = id.StartsWith("Steam_") ? id.Substring(6) : id;    // (adminlist.txt lines are the plain SteamID64)
            try
            {
                System.Type t = adminList.GetType();
                if (turnOff) { t.GetMethod("Remove")?.Invoke(adminList, new object[] { plain }); t.GetMethod("Remove")?.Invoke(adminList, new object[] { id }); }
                else if (!net.IsAdmin(id)) t.GetMethod("Add")?.Invoke(adminList, new object[] { plain });
                t.GetMethod("Save")?.Invoke(adminList, null);
                Core.Log.LogInfo("Blockscript: " + (turnOff ? "removed " : "added ") + matches[0].name + " (" + plain + ") " + (turnOff ? "from" : "to") + " the admin list");
                result["changed"] = matches[0].name;
                result["id"] = plain;
                result["admin"] = !turnOff;
                result["note"] = "saved to adminlist.txt; the game reads it again within a few seconds";
            }
            catch (Exception e) { result["problem"] = e.Message; }
            return result;
        }

        private static bool IsLogFilter(string a) => a.StartsWith("grep=") || Array.IndexOf(new[] { "debug", "info", "did", "limit", "warn", "error", "mine" }, a.ToLowerInvariant()) >= 0;

        private static bool Cheats() =>
            ZNet.instance == null || ZNet.instance.IsServer() || (global::Console.instance != null && global::Console.instance.IsCheatsEnabled());

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            args = args.Skip(1).ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            if (Array.IndexOf(new[] { "list", "log", "audit", "spike", "players" }, sub) < 0 && !(sub == "autoreload" && args.Length < 2) && !Cheats()) { error("blockscript " + sub + " changes what runs, so it only works in single player, for the host, or with devcommands on"); yield break; }

            if (sub == "panel")
            {
                string what = args.Length > 1 ? args[1].ToLowerInvariant() : "open";
                if (what == "close") Editor.ScriptsWindow.Close();
                else if (what == "page") output(new JObject { ["problem"] = Editor.ScriptsWindow.Go(args.Length > 2 ? args[2] : "library", args.Length > 3 ? args[3] : "", args.Length > 4 ? args[4] : ""), ["open"] = Editor.ScriptsWindow.IsOpen });
                else Editor.ScriptsWindow.Open();
                if (what != "page") output(new JObject { ["open"] = Editor.ScriptsWindow.IsOpen });
                yield break;
            }
            if (sub == "collab")
            {
                // live co-editing: which scripts are being edited together, by whom, how far they have got, and what the server dropped
                var c = new JObject
                {
                    ["sessions"] = new JArray(Mp.CollabGame.Describe().Cast<object>().ToArray()),
                    ["messagesHandled"] = Mp.CollabGame.Server.Handled, ["messagesDropped"] = Mp.CollabGame.Server.Dropped, ["saves"] = Mp.CollabGame.Server.Saved,
                };
                output(c);
                yield break;
            }
            if (sub == "server")
            {
                // for whoever tests multiplayer: what this game thinks of the server it is on, and what remote editing has seen
                var info = new JObject
                {
                    ["thisGameRunsScripts"] = Mp.Wire.IsServer,
                    ["serverHasMod"] = Mp.Wire.ServerHasMod,
                    ["admin"] = Mp.Wire.LocalIsAdmin,
                    ["serverVersion"] = Mp.Wire.ServerVersion,
                    ["backend"] = Mp.Backend.Current.IsRemote ? (Mp.Wire.ServerHasMod ? "remote" : "disconnected") : "local",
                    ["adminRequestsHandled"] = Mp.AdminServer.Handled, ["adminRequestsRefused"] = Mp.AdminServer.Refused, ["adminRequestsDropped"] = Mp.AdminServer.Dropped,
                };
                if (Mp.Backend.Current is Mp.RemoteBackend remote) info["remote"] = new JArray(remote.Describe().Cast<object>().ToArray());
                output(info);
                yield break;
            }
            if (sub == "spike") { output(Spike.Run()); yield break; }
            if (sub == "unstick")
            {
                // a teleport that never finished leaves the game's black screen up: read the state, and end it where the player stands
                Player me = Player.m_localPlayer;
                if (me == null) { error("no local player"); yield break; }
                var f = typeof(Player);
                System.Reflection.FieldInfo tp = f.GetField("m_teleporting", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    tt = f.GetField("m_teleportTimer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    td = f.GetField("m_distantTeleport", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                    tg = f.GetField("m_teleportTargetPos", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var before = new JObject { ["teleporting"] = (bool)(tp?.GetValue(me) ?? false), ["timer"] = (float)(tt?.GetValue(me) ?? 0f), ["distant"] = (bool)(td?.GetValue(me) ?? false), ["target"] = tg?.GetValue(me)?.ToString(), ["pos"] = me.transform.position.ToString(), ["dead"] = me.IsDead(), ["sleeping"] = me.IsSleeping() };
                bool fix = args.Length > 1 && args[1] == "fix";
                if (fix && (bool)(tp?.GetValue(me) ?? false))
                {
                    if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(me.transform.position, out float h)) { Vector3 p = me.transform.position; p.y = h + 0.2f; me.transform.position = p; }
                    typeof(Character).GetField("m_maxAirAltitude", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(me, me.transform.position.y);   // (no fall damage from where it stood)
                    tp.SetValue(me, false); tt?.SetValue(me, 0f);
                }
                output(new JObject { ["before"] = before, ["fixed"] = fix, ["teleportingNow"] = (bool)(tp?.GetValue(me) ?? false) });
                yield break;
            }
            if (sub == "players" || sub == "admin")
            {
                output(Admins(args));
                yield break;
            }
            if (sub == "scan") { Host.Scan(); output(new JObject { ["scripts"] = Host.Scripts.Count }); yield break; }
            if (sub == "audit") { output(new JObject { ["audit"] = new JArray(Audit.Lines.Skip(Math.Max(0, Audit.Lines.Count - 40)).ToArray()) }); yield break; }
            if (sub == "list")
            {
                var list = new JArray();
                foreach (Script s in Host.Scripts)
                    list.Add(new JObject
                    {
                        ["file"] = s.File, ["name"] = s.Name, ["state"] = s.State.ToString(), ["why"] = s.Why,
                        ["needs"] = new JArray(s.Manifest?.Needs.ToArray() ?? new string[0]), ["approved"] = new JArray(s.Approved.ToArray()),
                        ["handlers"] = new JArray(s.Handlers.Keys.ToArray()), ["timers"] = s.Timers.Count, ["queued"] = s.Queue.Count, ["calls"] = s.Calls,
                        ["avgMs"] = s.Calls > 0 ? Math.Round(s.TotalMs / s.Calls, 3) : 0.0, ["refused"] = new JObject(s.Refused.Select(kv => new JProperty(kv.Key, kv.Value)).ToArray()),
                    });
                output(new JObject { ["scripts"] = list });
                yield break;
            }
            if (sub == "autoreload")
            {
                if (args.Length > 1) Host.AutoReload = args[1].ToLowerInvariant() != "off";
                output(new JObject { ["autoreload"] = Host.AutoReload });
                yield break;
            }
            if (sub == "say")
            {
                if (Chat.instance == null || Player.m_localPlayer == null) { error("no chat in the world"); yield break; }
                Chat.instance.SendText(Talker.Type.Normal, string.Join(" ", args.Skip(1).ToArray()));   // (the game's own chat, as if typed)
                output(new JObject { ["sent"] = true });
                yield break;
            }
            if (sub == "chat")
            {
                if (Player.m_localPlayer == null) { error("no player in the world"); yield break; }
                Events.Said(Player.m_localPlayer, string.Join(" ", args.Skip(1).ToArray()), "normal");
                output(new JObject { ["said"] = true });
                yield break;
            }
            if (sub == "stop" && args.Length > 1 && args[1] == "all") { Host.StopAll("Stopped."); output(new JObject { ["stopped"] = "all" }); yield break; }
            Script target = args.Length > 1 ? Host.Find(string.Join(" ", args.Skip(1).Where(a => !IsPower(a) && !int.TryParse(a, out _) && !IsLogFilter(a) && a != "on" && a != "off").ToArray())) : null;
            if (target == null) { error(Help); yield break; }
            switch (sub)
            {
                case "run": { string why = Host.Start(target); output(new JObject { ["state"] = target.State.ToString(), ["problem"] = why }); break; }
                case "stop": Host.Stop(target); output(new JObject { ["state"] = target.State.ToString() }); break;
                case "log":
                    {
                        int count = args.Skip(2).Select(x => int.TryParse(x, out int c) ? c : -1).FirstOrDefault(c => c > 0);
                        if (count <= 0) count = 30;
                        LogLevel min = LogLevel.Debug; bool only = false, mine = false; string grep = "";
                        foreach (string t in args.Skip(2))
                        {
                            string w = t.ToLowerInvariant();
                            if (w == "mine") mine = true;
                            else if (w.StartsWith("grep=")) grep = t.Substring(5);
                            else if (Enum.TryParse(t, true, out LogLevel lv) && Array.IndexOf(new[] { "debug", "info", "did", "limit", "warn", "error" }, w) >= 0) { min = lv; only = true; }
                        }
                        output(new JObject
                        {
                            ["log"] = new JArray(target.Log.Query(min, grep, count, only, mine).Select(ScriptLog.Line_).ToArray()),
                            ["counts"] = new JObject { ["debug"] = target.Log.Count(LogLevel.Debug), ["info"] = target.Log.Count(LogLevel.Info), ["did"] = target.Log.Count(LogLevel.Did), ["limit"] = target.Log.Count(LogLevel.Limit), ["warn"] = target.Log.Count(LogLevel.Warn), ["error"] = target.Log.Count(LogLevel.Error) },
                            ["file"] = HostBridge.LogPath(target.File),
                        });
                        break;
                    }
                case "trace":
                    {
                        bool on = args.Length > 2 ? args[2] != "off" : !target.Trace;
                        HostBridge.SetTrace(target.File, on);
                        output(new JObject { ["trace"] = target.Trace });
                        break;
                    }
                case "stats": output(new JObject { ["stats"] = HostBridge.Stats(target.File), ["state"] = target.State.ToString(), ["refused"] = new JArray(HostBridge.Refused(target.File)) }); break;
                case "allow":
                case "deny":
                    {
                        string power = args.Last();
                        foreach (string cap in power == "all" ? target.Manifest.Needs.ToArray() : new[] { power })
                            if (sub == "allow") target.Approved.Add(cap); else target.Approved.Remove(cap);
                        Approvals.Save();
                        output(new JObject { ["approved"] = new JArray(target.Approved.ToArray()) });
                        break;
                    }
                default: error(Help); break;
            }
        }

        private static bool IsPower(string word) => word == "all" || Array.IndexOf(Manifest.Capabilities, word) >= 0;
    }

    /// <summary>
    /// Risk 1 and 2 of the design: Jint on the game's runtime, and whether its limits stop a hostile script. Each case reports what
    /// happened and how long it took. (Findings 2026-10-12: it runs here; the infinite-loop, statement and recursion limits bite in
    /// 11 to 95 ms; Jint's own memory limit does nothing on this runtime, so Sandbox.cs / Budget does that; the engine needs
    /// System.Buffers, embedded with it, and a restart after the first load of a missing dependency.)
    /// </summary>
    internal static class Spike
    {
        internal static JObject Run()
        {
            var result = new JObject();
            result["engineLoaded"] = typeof(Engine).Assembly.GetName().Version.ToString();
            result["basic"] = Case("1+1", "1 + 1");
            result["es2020"] = Case("let, const, arrow, template, destructuring, spread, class, Map, JSON",
                "const [a, b] = [1, 2]; class P { constructor(x) { this.x = x; } get d() { return this.x * 2; } } const m = new Map([[1, 'one']]); " +
                "const f = (...r) => r.reduce((s, n) => s + n, 0); `${a + b}:${f(...[1,2,3])}:${new P(4).d}:${m.get(1)}:${JSON.stringify({q: [1,2]})}`");
            result["hardened"] = Case("what is removed", "[typeof eval, typeof Function, typeof Date, typeof Reflect, typeof Proxy, typeof Math.random, typeof System, typeof importNamespace].join(',')");
            result["eval"] = Case("eval and Function", "let r = []; try { eval('1'); r.push('eval works'); } catch (e) { r.push('eval blocked'); } try { new Function('return 1')(); r.push('Function works'); } catch (e) { r.push('Function blocked'); } r.join(', ')");
            result["bigRepeat"] = Case("repeat is capped", "let r; try { 'x'.repeat(500000000); r = 'NOT CAPPED'; } catch (e) { r = 'capped: ' + e.message; } r");
            result["infiniteLoop"] = Hostile("while (true) {}");
            result["deepRecursion"] = Hostile("function f(n) { return f(n + 1) + 1; } f(0)");
            result["stringDoubling"] = Hostile("let s = 'x'; while (true) { s = s + s; }");
            result["arrayGrowth"] = Hostile("const a = []; while (true) { a.push(new Array(1000).fill(1)); }");
            result["objectGrowth"] = Hostile("const o = {}; for (let i = 0; i < 1e9; i++) { o['k' + i] = i; }");
            return result;
        }

        private static JObject Case(string what, string code)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var budget = new Budget();
                Engine engine = Sandbox.Create(budget);
                budget.Arm(50.0, 8L * 1024 * 1024, 100000);
                string value = engine.Evaluate(code).ToString();
                budget.Disarm();
                return new JObject { ["what"] = what, ["value"] = value, ["ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1) };
            }
            catch (Exception e) { return new JObject { ["what"] = what, ["threw"] = e.GetType().Name + ": " + Sandbox.Plain(e), ["ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1) }; }
        }

        /// <summary>A hostile script, run the way the host runs a handler (the budget armed): how it ended, how long it took, the memory.</summary>
        private static JObject Hostile(string code)
        {
            long before = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            string ended = "RAN TO THE END (not stopped)";
            var worker = new System.Threading.Thread(() =>
            {
                try { var budget = new Budget(); Engine engine = Sandbox.Create(budget); budget.Arm(25.0, 8L * 1024 * 1024, 100000); engine.Execute(code); }
                catch (System.Threading.ThreadAbortException) { ended = "ABORTED BY WATCHDOG (the budget did not stop it)"; }
                catch (Exception e) { ended = e is BudgetException b ? "BudgetException(" + b.Kind + ")" : e.GetType().Name; }
            });
            worker.Start();
            if (!worker.Join(4000)) { try { worker.Abort(); } catch (Exception) { } worker.Join(1000); ended = "ABORTED BY WATCHDOG (the budget did not stop it)"; }
            return new JObject { ["ended"] = ended, ["ms"] = Math.Round(sw.Elapsed.TotalMilliseconds, 1), ["memoryGrewMB"] = Math.Round((GC.GetTotalMemory(false) - before) / 1048576.0, 1) };
        }
    }
}
