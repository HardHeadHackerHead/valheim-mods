using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Mp;

namespace Scripts.Admin.Tests
{
    /// <summary>A server's scripts in memory: what the game's own operations do, as far as the rules around them can tell.</summary>
    internal sealed class FakeOps : IAdminOps
    {
        internal readonly Dictionary<string, string> Code = new Dictionary<string, string>();       // file -> code
        internal readonly Dictionary<string, string> Blocks = new Dictionary<string, string>();     // file -> blocks json
        internal readonly Dictionary<string, string> State = new Dictionary<string, string>();
        internal readonly List<string> Did = new List<string>();
        internal int Runs;
        internal bool Throw;

        public JObject Run(string op, JObject a, long peer)
        {
            Runs++;
            if (Throw) throw new InvalidOperationException("boom");
            string file = a.Value<string>("file");
            switch (op)
            {
                case AdminOps.List:
                    return new JObject { ["rows"] = AdminFrames.ToJson(Code.Keys.OrderBy(k => k).Select(f => new[] { f, f.Replace(".js", ""), State.TryGetValue(f, out string st) ? st : "Stopped", "", "messages", "", "about " + f, "0", Blocks.ContainsKey(f) ? "1" : "0" }).ToArray()) };
                case AdminOps.Settings:
                    return new JObject { ["world"] = "TestWorld", ["version"] = "0.1.0", ["admin"] = peer == 1, ["lines"] = new JObject { [""] = new JArray("Time: 1 ms a frame"), ["spawn"] = new JArray("Spawn: 5 a minute") } };
                case AdminOps.Source:
                    if (!Code.ContainsKey(file)) throw new AdminError("The server has no script called " + file + ".");
                    return new JObject { ["text"] = Code[file] };
                case AdminOps.Status:
                    return new JObject { ["counts"] = new JArray(0, 2, 1, 0, 0, 0, 3), ["stats"] = "4 calls", ["refused"] = new JArray("spawn (2x)"), ["trace"] = false, ["path"] = "logs/" + file };
                case AdminOps.LogRows:
                    return new JObject { ["rows"] = AdminFrames.ToJson(new[] { new[] { "1", "20:00:00", "INFO", "", "started", "", "0", "0", "1", "0", "1" } }) };
                case AdminOps.Start: Did.Add("start " + file); State[file] = "Running"; return new JObject { ["why"] = "" };
                case AdminOps.Stop: Did.Add("stop " + file); State[file] = "Stopped"; return new JObject { ["ok"] = true };
                case AdminOps.Write: Did.Add("write " + NameClean.ScriptFile(file)); Code[NameClean.ScriptFile(file)] = a.Value<string>("text"); return new JObject { ["why"] = "", ["file"] = NameClean.ScriptFile(file) };
                case AdminOps.WriteBlocks:
                    Did.Add("writeBlocks " + file); Blocks[file] = a.Value<string>("blocks"); Code[file] = a.Value<string>("js") ?? ""; return new JObject { ["why"] = "" };
                case AdminOps.Blocks:
                    if (!Blocks.ContainsKey(file)) throw new AdminError("That script has no blocks.");
                    return new JObject { ["json"] = Blocks[file] };
                case AdminOps.New: Did.Add("new"); Code["new-one.js"] = "// new"; return new JObject { ["file"] = "new-one.js" };
                case AdminOps.Delete: Did.Add("delete " + file); Code.Remove(file); Blocks.Remove(file); return new JObject { ["ok"] = true };
                default: return new JObject();
            }
        }
    }

    internal static class Tests
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL: " + what);
        }

        /// <summary>A client wired to a server in memory. Answers wait in a queue until Deliver (latency), or are lost (Drop).</summary>
        private sealed class Loop
        {
            internal readonly FakeOps Ops = new FakeOps();
            internal readonly AdminDispatcher Server;
            internal readonly RemoteClientCore Client = new RemoteClientCore();
            internal bool IsAdmin = true, Drop;
            internal double Now = 100.0;
            internal long Peer = 1;
            internal readonly List<string> Sent = new List<string>();
            private readonly List<KeyValuePair<long, string>> _answers = new List<KeyValuePair<long, string>>();

            internal Loop()
            {
                Server = new AdminDispatcher(Ops);
                Client.Send = (op, json, id) =>
                {
                    Sent.Add(op);
                    string answer = Server.Handle(Peer, json, IsAdmin, Now);
                    if (answer != null && !Drop) _answers.Add(new KeyValuePair<long, string>(id, answer));
                    return true;
                };
            }

            internal void Deliver() { foreach (var a in _answers.ToList()) Client.OnRes(a.Key, a.Value, Now); _answers.Clear(); }

            internal void Step(double seconds = 0.6)
            {
                Now += seconds;
                Client.Pump(Now, IsAdmin);
                Deliver();
            }
        }

        private static void Frames()
        {
            string req = AdminFrames.Req("source", new JObject { ["file"] = "a.js" });
            Check(AdminFrames.TryReq(req, out string op, out JObject args) && op == "source" && args.Value<string>("file") == "a.js", "a request survives framing");
            Check(AdminFrames.TryReq(AdminFrames.Req("list"), out op, out args) && op == "list" && !args.HasValues, "a request with no arguments");
            Check(!AdminFrames.TryReq("not json", out _, out _), "text that is not JSON is not a request");
            Check(!AdminFrames.TryReq("{\"a\":{}}", out _, out _), "a frame without an op is not a request");
            Check(!AdminFrames.TryReq("{\"op\":\"" + new string('x', 50) + "\"}", out _, out _), "a very long op is not a request");
            string deep = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40);
            Check(!AdminFrames.TryReq("{\"op\":\"list\",\"a\":" + deep + "}", out _, out _), "JSON nested too deep is refused");
            Check(!AdminFrames.TryReq("{\"op\":\"write\",\"a\":{\"text\":\"" + new string('x', AdminFrames.MaxFrame) + "\"}}", out _, out _), "a frame over the size cap is refused");

            string res = AdminFrames.Res(true, "", new JObject { ["x"] = 5 });
            Check(AdminFrames.TryRes(res, out bool ok, out string why, out JObject d) && ok && why == "" && d.Value<int>("x") == 5, "an answer survives framing");
            Check(AdminFrames.TryRes(AdminFrames.Res(false, "Only admins can do that."), out ok, out why, out d) && !ok && why == "Only admins can do that.", "a refusal carries its words");
            Check(!AdminFrames.TryRes("<html>", out _, out _, out _), "an answer that is not JSON is not read");
        }

        private static void Gate()
        {
            Check(AdminGate.Allowed(AdminOps.List, false) && AdminGate.Allowed(AdminOps.Settings, false), "anyone may ask for the list and the settings");
            foreach (string op in new[] { "source", "log", "logrows", "logtext", "logcounts", "stats", "refused", "trace", "status", "start", "stop", "allow", "write", "writeBlocks", "new", "delete", "blocks", "logclear", "logpath" })
                Check(!AdminGate.Allowed(op, false) && AdminGate.Allowed(op, true), op + " is for admins");
            Check(!AdminGate.Allowed("format-disk", true) && !AdminGate.Allowed(null, true), "an unknown request is refused even for an admin");
        }

        private static void Names()
        {
            Check(NameClean.ScriptFile("My Script!") == "My-Script-.js", "a name is cleaned: " + NameClean.ScriptFile("My Script!"));
            Check(NameClean.ScriptFile("../../etc/passwd") == "passwd.js", "a path becomes a name: " + NameClean.ScriptFile("../../etc/passwd"));
            Check(NameClean.ScriptFile("a\\b\\c.txt") == "c.js" || NameClean.ScriptFile("a\\b\\c.txt") == "a-b-c.js", "a Windows path: " + NameClean.ScriptFile("a\\b\\c.txt"));
            Check(NameClean.ScriptFile(null) == "script.js" && NameClean.ScriptFile("") == "script.js", "nothing becomes script.js");
            Check(NameClean.ScriptFile(new string('a', 100)).Length == 43, "a long name is cut to 40 and .js");
            Check(NameClean.IsScriptFile("lucky-boars.js") && NameClean.IsScriptFile("a_1.js"), "real file names pass");
            Check(!NameClean.IsScriptFile("../a.js") && !NameClean.IsScriptFile("a/b.js") && !NameClean.IsScriptFile("a.txt") && !NameClean.IsScriptFile(".js") && !NameClean.IsScriptFile(null) && !NameClean.IsScriptFile(new string('a', 41) + ".js"), "anything that could leave the folder, or is not a script, does not");
            Check(NameClean.TextFits(new string('x', 120 * 1024), AdminFrames.MaxText) && !NameClean.TextFits(new string('x', 120 * 1024 + 1), AdminFrames.MaxText), "the code size cap is 120 KB");
            Check(!NameClean.TextFits(new string('é', 70 * 1024), AdminFrames.MaxText), "the cap counts bytes, not letters");
        }

        private static void Limiter()
        {
            var l = new AdminLimiter();
            int ok = 0;
            for (int i = 0; i < 30; i++) if (l.Allow(7, 10.0)) ok++;
            Check(ok == 20 && l.Dropped == 10, "a burst of 30 gets 20 through: " + ok);
            Check(l.Allow(8, 10.0), "another connection has its own allowance");
            int back = 0;
            Check(!l.Allow(7, 10.0), "nothing is left at that instant");
            for (int i = 0; i < 20; i++) if (l.Allow(7, 10.5)) back++;
            Check(back == 10, "half a second gives ten back: " + back);
            Check(l.Allow(7, 20.0), "and a pause gives all of it back");
        }

        private static void Dispatcher()
        {
            var loop = new Loop();
            AdminDispatcher d = loop.Server;
            string json = AdminFrames.Req("write", new JObject { ["file"] = "x.js", ["text"] = "code" });

            Check(d.Handle(2, json, false, 1.0) is string r1 && AdminFrames.TryRes(r1, out bool ok, out string why, out _) && !ok && why == "Only admins can do that." && loop.Ops.Runs == 0, "a player who is not an admin cannot write, and nothing ran");
            Check(AdminFrames.TryRes(d.Handle(2, AdminFrames.Req("list"), false, 1.0), out ok, out _, out _) && ok, "but can ask for the list");
            Check(AdminFrames.TryRes(d.Handle(1, json, true, 1.0), out ok, out why, out JObject data) && ok && data.Value<string>("file") == "x.js" && loop.Ops.Code["x.js"] == "code", "an admin can");
            Check(AdminFrames.TryRes(d.Handle(1, "garbage", true, 1.0), out ok, out why, out _) && !ok, "garbage gets a refusal");
            Check(AdminFrames.TryRes(d.Handle(1, AdminFrames.Req("format-disk"), true, 1.0), out ok, out why, out _) && !ok && why.Contains("does not know"), "an unknown request gets a refusal");
            Check(AdminFrames.TryRes(d.Handle(1, AdminFrames.Req("source", new JObject { ["file"] = "../../secret.js" }), true, 1.0), out ok, out why, out _) && !ok && why.Contains("not a script"), "a name that leaves the folder is refused before anything runs");
            Check(AdminFrames.TryRes(d.Handle(1, AdminFrames.Req("source"), true, 1.0), out ok, out why, out _) && !ok, "a request that needs a script and has none is refused");
            string big = AdminFrames.Req("write", new JObject { ["file"] = "x.js", ["text"] = new string('x', AdminFrames.MaxText + 1) });
            int runs = loop.Ops.Runs;
            Check(AdminFrames.TryRes(d.Handle(1, big, true, 1.0), out ok, out why, out _) && !ok && why.Contains("too long") && loop.Ops.Runs == runs, "code over 120 KB is refused before it is written");
            string bigBlocks = AdminFrames.Req("writeBlocks", new JObject { ["file"] = "x.js", ["blocks"] = new string('x', AdminFrames.MaxBlocks + 1) });
            Check(AdminFrames.TryRes(d.Handle(1, bigBlocks, true, 1.0), out ok, out why, out _) && !ok && why.Contains("too big"), "blocks over 400 KB are refused");
            Check(AdminFrames.TryRes(d.Handle(1, AdminFrames.Req("new", new JObject { ["name"] = new string('n', 41) }), true, 1.0), out ok, out why, out _) && !ok, "a name over 40 characters is refused");

            loop.Ops.Throw = true;
            Check(AdminFrames.TryRes(d.Handle(1, AdminFrames.Req("list"), true, 2.0), out ok, out why, out _) && !ok && why.Contains("could not"), "a fault in the server's own work is an answer, not an exception");
            loop.Ops.Throw = false;

            // the rate limit: more than 20 in an instant and the rest get no answer at all
            var fresh = new Loop();
            int answered = 0, dropped = 0;
            for (int i = 0; i < 40; i++) { if (fresh.Server.Handle(3, AdminFrames.Req("list"), true, 50.0) != null) answered++; else dropped++; }
            Check(answered == 20 && dropped == 20 && fresh.Server.Dropped == 20, "40 requests in an instant: 20 answered, 20 dropped: " + answered);
        }

        private static void Book()
        {
            var book = new RequestBook();
            bool done1 = false, ok1 = true; string why1 = "";
            long a = book.Add("list", "list", (ok, why, d) => { done1 = true; ok1 = ok; why1 = why; }, 10.0);
            long b = book.Add("source", "source:x", (ok, why, d) => { throw new Exception("a callback that fails"); }, 10.0);
            Check(a != b && a < b, "ids count up");
            Check(book.Count == 2 && book.KeyPending("list") && !book.KeyPending("nope"), "waiting requests are known by key");
            Check(book.Complete(b, true, "", null) && book.Count == 1, "a callback that throws does not break the book");
            Check(!book.Complete(b, true, "", null), "an answer is taken once");
            Check(!book.Complete(999, true, "", null), "an answer nobody asked for is ignored");
            Check(book.Expire(14.9) == 0 && !done1, "not late yet");
            Check(book.Expire(15.1) == 1 && done1 && !ok1 && why1 == "no answer from the server" && book.Count == 0, "after five seconds it ends as 'no answer from the server'");
        }

        private static void Cache()
        {
            var cache = new QueryCache();
            var args = new JObject { ["file"] = "a.js" };
            Check(cache.Want("source", args, 10.0) == null, "nothing known yet is null");
            string key = QueryCache.Key("source", args);
            Check(key == "source:{\"file\":\"a.js\"}", "the key is the op and the arguments: " + key);
            Check(QueryCache.Key("list", null) == "list" && QueryCache.Key("list", new JObject()) == "list", "no arguments: just the op");
            var due = cache.Due(10.0, op => 0.5, k => false, 10);
            Check(due.Count == 1 && due[0].Op == "source", "a wanted question with no answer is due");
            Check(cache.Due(10.0, op => 0.5, k => true, 10).Count == 0, "but not while it is being asked");
            cache.Put(key, "the code", 10.0);
            Check((string)cache.Want("source", args, 10.2) == "the code", "the last answer is kept");
            Check(cache.Due(10.3, op => 0.5, k => false, 10).Count == 0 && cache.Due(10.8, op => 0.5, k => false, 10).Count == 1, "it is asked again after its time");
            Check(cache.Due(14.0, op => 0.5, k => false, 10).Count == 0, "a question nobody wants any more is not asked");
            cache.Want("source", args, 20.0);
            cache.Stale();
            Check(cache.Due(20.0, op => 0.5, k => false, 10).Count == 1 && (string)cache.Want("source", args, 20.0) == "the code", "after a change everything is asked again, and the old answer stays in view");
            var many = new QueryCache();
            for (int i = 0; i < 10; i++) many.Want("source", new JObject { ["file"] = "f" + i }, 1.0);
            Check(many.Due(1.0, op => 0.5, k => false, 3).Count == 3, "at most as many as asked for");
        }

        private static void Parse()
        {
            var status = (StatusInfo)RemoteParse.Convert(AdminOps.Status, new JObject { ["counts"] = new JArray(1, 2, 3, 4, 5, 6, 7), ["stats"] = "s", ["refused"] = new JArray("a", "b"), ["trace"] = true, ["path"] = "p" });
            Check(status.Counts[5] == 6 && status.Counts[6] == 7 && status.Stats == "s" && status.Refused.Length == 2 && status.Trace && status.Path == "p", "a status is read");
            var settings = (SettingsInfo)RemoteParse.Convert(AdminOps.Settings, new JObject { ["world"] = "W", ["version"] = "1", ["admin"] = true, ["lines"] = new JObject { [""] = new JArray("a"), ["spawn"] = new JArray("b", "c") } });
            Check(settings.World == "W" && settings.Admin && settings.Lines[""].Length == 1 && settings.Lines["spawn"].Length == 2, "the settings are read");
            Check(((string[][])RemoteParse.Convert(AdminOps.List, new JObject { ["rows"] = new JArray(new JArray("a.js", "A"), new JArray()) })).Length == 1, "an empty row is skipped");
            Check(RemoteParse.Convert(AdminOps.List, new JObject { ["rows"] = "oops" }) is string[][] none && none.Length == 0, "rows that are not a list are an empty list");
            Check(RemoteParse.Convert(AdminOps.Source, new JObject()) is string s && s == "", "no text is empty text");
            var rows = Enumerable.Range(0, 1000).Select(i => new[] { i.ToString(), "t", "INFO", "", "text", new string('d', 2000), "0", "0", "1", "0", "1" }).ToArray();
            string[][] capped = AdminFrames.CapRows(rows);
            Check(capped.Length <= AdminFrames.MaxRows && capped.Last()[0] == "999" && capped.All(r => r[5].Length <= AdminFrames.MaxDetail + 3), "log rows are cut to the newest, with short details");
            Check(capped.Sum(r => r.Sum(c => c.Length)) <= AdminFrames.MaxAnswerBytes + 4096, "and to what fits an answer");
        }

        private static void Remote()
        {
            // an ordinary player sees the list and the settings, and asks for nothing else
            var loop = new Loop { IsAdmin = false, Peer = 2 };
            loop.Ops.Code["a.js"] = "// a"; loop.Ops.Blocks["a.js"] = "{}";
            loop.Client.Want(AdminOps.List, null, loop.Now);
            loop.Client.Want(AdminOps.Settings, null, loop.Now);
            loop.Client.Want(AdminOps.Source, new JObject { ["file"] = "a.js" }, loop.Now);
            loop.Client.Want(AdminOps.Status, new JObject { ["file"] = "a.js" }, loop.Now);
            loop.Step();
            Check(loop.Sent.OrderBy(x => x).SequenceEqual(new[] { "list", "settings" }), "a non-admin's editor asks only for the list and settings: " + string.Join(",", loop.Sent));
            var rows = loop.Client.Want(AdminOps.List, null, loop.Now) as string[][];
            Check(rows != null && rows.Length == 1 && rows[0][0] == "a.js" && rows[0][8] == "1", "and gets the list, with which are block scripts");
            Check(loop.Client.Want(AdminOps.Source, new JObject { ["file"] = "a.js" }, loop.Now) == null, "but never the code");
            Check(loop.Client.Answering(loop.Now) && loop.Client.Error == "", "the connection counts as answering");

            // an admin also reads, and a change is one request with an answer
            var admin = new Loop { IsAdmin = true, Peer = 1 };
            admin.Ops.Code["a.js"] = "// the code"; admin.Ops.Blocks["a.js"] = "{\"tops\":[]}";
            admin.Client.Want(AdminOps.Source, new JObject { ["file"] = "a.js" }, admin.Now);
            admin.Client.Want(AdminOps.Status, new JObject { ["file"] = "a.js" }, admin.Now);
            admin.Client.Want(AdminOps.LogRows, new JObject { ["file"] = "a.js", ["min"] = 0 }, admin.Now);
            admin.Step();
            Check((admin.Client.Want(AdminOps.Source, new JObject { ["file"] = "a.js" }, admin.Now) as string) == "// the code", "an admin reads the code");
            Check(admin.Client.Want(AdminOps.Status, new JObject { ["file"] = "a.js" }, admin.Now) is StatusInfo si && si.Stats == "4 calls" && si.Refused[0] == "spawn (2x)", "and the status");
            Check(admin.Client.Want(AdminOps.LogRows, new JObject { ["file"] = "a.js", ["min"] = 0 }, admin.Now) is string[][] lr && lr.Length == 1 && lr[0][4] == "started", "and the log");

            string startedWhy = "unset";
            admin.Client.Call(AdminOps.Start, new JObject { ["file"] = "a.js" }, (ok, why, d) => startedWhy = ok ? d.Value<string>("why") : why, admin.Now);
            Check(startedWhy == "unset", "an answer takes its trip");
            admin.Deliver();
            Check(startedWhy == "" && admin.Ops.State["a.js"] == "Running" && admin.Ops.Did.Contains("start a.js"), "starting a script is one request, and its answer reaches the callback");

            // saving blocks sends the blocks and the code together, and a load reads them back
            string saved = "unset";
            admin.Client.Call(AdminOps.WriteBlocks, new JObject { ["file"] = "b.js", ["blocks"] = "{\"tops\":[1]}", ["js"] = "// generated" }, (ok, why, d) => saved = ok ? d.Value<string>("why") : why, admin.Now);
            admin.Deliver();
            Check(saved == "" && admin.Ops.Blocks["b.js"] == "{\"tops\":[1]}" && admin.Ops.Code["b.js"] == "// generated", "saving blocks writes both files on the server");
            string loaded = null;
            admin.Client.Call(AdminOps.Blocks, new JObject { ["file"] = "b.js" }, (ok, why, d) => loaded = ok ? d.Value<string>("json") : "refused: " + why, admin.Now);
            admin.Deliver();
            Check(loaded == "{\"tops\":[1]}", "and they load back the same");
            admin.Client.Call(AdminOps.Blocks, new JObject { ["file"] = "none.js" }, (ok, why, d) => loaded = ok ? "ok" : why, admin.Now);
            admin.Deliver();
            Check(loaded == "That script has no blocks.", "a refusal comes with the server's words: " + loaded);

            // a change makes the cached answers stale, so the list refreshes
            admin.Client.Want(AdminOps.List, null, admin.Now);
            admin.Step();
            admin.Client.Call(AdminOps.Delete, new JObject { ["file"] = "b.js" }, (ok, why, d) => { }, admin.Now);
            admin.Deliver();
            admin.Step(0.01);
            Check((admin.Client.Want(AdminOps.List, null, admin.Now) as string[][]).All(r => r[0] != "b.js"), "after a delete the list is asked again at once");

            // a non-admin's change is refused by the server even if the editor were to send it
            var sneaky = new Loop { IsAdmin = false, Peer = 2 };
            string reply = null;
            sneaky.Client.Call(AdminOps.Write, new JObject { ["file"] = "evil.js", ["text"] = "x" }, (ok, why, d) => reply = ok ? "done" : why, sneaky.Now);
            sneaky.Deliver();
            Check(reply == "Only admins can do that." && !sneaky.Ops.Code.ContainsKey("evil.js") && sneaky.Ops.Runs == 0, "the server says no, whatever the editor sends");
        }

        private static void Timeouts()
        {
            var loop = new Loop { Drop = true };
            loop.Client.Want(AdminOps.List, null, loop.Now);
            loop.Step();
            Check(loop.Client.Book.Count == 1 && !loop.Client.Answering(loop.Now), "a question with no answer is waiting");
            string reply = null;
            loop.Client.Call(AdminOps.Stop, new JObject { ["file"] = "a.js" }, (ok, why, d) => reply = ok ? "ok" : why, loop.Now);
            loop.Step(5.5);
            Check(reply == "no answer from the server" && loop.Client.Error == "no answer from the server" && loop.Client.LastFail > 0, "a request nobody answers ends in five seconds with words for it: " + reply);
            Check(!loop.Client.Answering(loop.Now), "and the connection does not count as answering");

            // the server comes back: the same loop answers, and the connection is good again
            loop.Drop = false;
            loop.Client.Want(AdminOps.List, null, loop.Now);
            loop.Step(); loop.Step();
            Check(loop.Client.Answering(loop.Now) && loop.Client.Error == "", "when answers come again, so does the connection: error '" + loop.Client.Error + "'");

            // not able to send at all
            var dead = new RemoteClientCore { Send = (op, json, id) => false };
            dead.Want(AdminOps.List, null, 1.0);
            dead.Pump(1.0, true);
            string why2 = null;
            dead.Call(AdminOps.Stop, new JObject { ["file"] = "a.js" }, (ok, why, d) => why2 = why, 1.0);
            Check(why2 == "not connected" && dead.Book.Count == 0, "a link that cannot send ends the request at once");

            // reset on joining or leaving a server
            loop.Client.Reset();
            Check(loop.Client.Cache.Count == 0 && loop.Client.Book.Count == 0 && !loop.Client.Answering(loop.Now), "joining another server starts over");

            // an answer that is a refusal still means the server is there
            var refuse = new Loop { IsAdmin = false, Peer = 2 };
            refuse.Client.Want(AdminOps.List, null, refuse.Now);
            refuse.Step();
            Check(refuse.Client.Answering(refuse.Now), "a server that answers is answering");
        }

        private static void Pacing()
        {
            // however many things the editor wants, only a few questions are out at once
            var loop = new Loop();
            for (int i = 0; i < 30; i++) loop.Client.Want(AdminOps.Source, new JObject { ["file"] = "f" + i + ".js" }, loop.Now);
            loop.Client.Pump(loop.Now + 1.0, true);
            Check(loop.Client.Book.Count == loop.Client.MaxAsking && loop.Sent.Count == loop.Client.MaxAsking, "at most " + loop.Client.MaxAsking + " questions at once: " + loop.Sent.Count);
        }

        private static int Main()
        {
            Frames();
            Gate();
            Names();
            Limiter();
            Dispatcher();
            Book();
            Cache();
            Parse();
            Remote();
            Timeouts();
            Pacing();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
