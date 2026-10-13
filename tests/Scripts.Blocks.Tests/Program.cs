using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Scripts.Blocks;

namespace Scripts.Blocks.Tests
{
    internal static partial class Tests
    {
        private static int _failed, _passed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL: " + what);
        }

        // Stand-ins for the host's API: they only record what a script did, so the generated code can be run and looked at.
        private const string Stubs = @"
var _log = [], handlers = {}, timers = [], signalHandlers = {}, commands = {};
function on(n, f) { (handlers[n] = handlers[n] || []).push(f); }
function every(s, f) { timers.push({ s: s, f: f }); }
function after(s, f) { if (timers.length >= 400) return -1; timers.push({ s: s, f: f, once: true }); return timers.length; }
function minutes(n) { return n * 60; }
var _chance = true; function chance(p) { return _chance; }
function randomInt(a, b) { return a; } function random(a, b) { return a; } function pick(l) { return l.length ? l[0] : null; }
function day() { return 3; } function timeOfDay() { return 0.5; } function isNight() { return false; } function isDay() { return true; }
function weather() { return 'clear'; } function players() { return [mkPlayer('Ann'), mkPlayer('Bob')]; } function findPlayer(n) { return null; }
function offset(p, x, y, z) { return { x: p.x + x, y: p.y + y, z: p.z + z }; } function around(p, r) { return p; }
function nearbyCreatures(p, r) { return [mkCreature('Boar'), mkCreature('Neck')]; }
function nearbyPlayers(p, r) { return [mkPlayer('Cid')]; }
function nearbyPieces(p, r) { return [{ id: 'pc1', prefab: 'wood_wall', pos: { x: 1, y: 1, z: 1 }, valid: true }]; }
function broadcast(t) { _log.push('broadcast:' + t); } function log(t) { _log.push('log:' + t); }
function dropItem(i, n, p) { _log.push('drop:' + i + ':' + n); return n; }
function spawn(c, p, o) { _log.push('spawn:' + c + (o && o.level > 1 ? '*' + o.level : '')); return mkCreature(c); }
var world = { setWeather: function (k, s) { _log.push('weather:' + k + ':' + s); return true; }, setTime: function (t) { _log.push('time:' + t); return true; },
  setWind: function (d, s, n) { _log.push('wind:' + d + ':' + s + ':' + n); return true; },
  startEvent: function (n) { _log.push('event:' + n); return true; }, stopEvent: function () { _log.push('event:stop'); return true; },
  spawnPiece: function (n, p) { _log.push('piece:' + n); return { id: 'np', prefab: n, pos: p, valid: true, remove: function () { _log.push('piece:removed'); return true; } }; } };
var net = { send: function (e, t) { _log.push('net:' + e + ':' + t); return true; } };
var _store = {}; var store = { get: function (s, k, d) { var v = _store[s + '|' + k]; return v === undefined ? d : v; }, set: function (s, k, v) { _store[s + '|' + k] = v; return true; } };
function signal(n, d) { _log.push('signal:' + n + ':' + d); (signalHandlers[n] || []).slice().forEach(function (f) { f(d); }); }
function onSignal(n, f) { (signalHandlers[n] = signalHandlers[n] || []).push(f); }
function command(n, f) { commands[n] = f; }
function banner(t) { _log.push('banner:' + t); }
function effect(n, p, o) { _log.push('effect:' + n); return true; }
function sound(n, p) { _log.push('sound:' + n); return true; }
function lightning(p) { _log.push('lightning'); }
function shake(p, r, s) { _log.push('shake:' + r + ':' + s); }
function ping(p, t) { _log.push('ping:' + t); }
function clock() { return { hour: 13, minute: 37, second: 5 }; }
function lookingAt(p) { return { x: 9, y: 0, z: 9 }; }
function ahead(a, m) { return { x: m, y: 0, z: 0 }; }
function bearing(a, b) { return 90; }
function distance(a, b) { return 5; }
function _actor(o) {
  o.stamina = 80; o.maxStamina = 100; o.speed = 2.5; o.isSwimming = false; o.isInWater = false; o.isSitting = true; o.isRunning = false; o.isBlocking = false;
  o.faction = o.isPlayer ? 'players' : 'wild'; o.facing = { x: 0, y: 0, z: 1 }; o.deaths = 2; o.valid = true;
  o.heal = function (n) { return n; }; o.damage = function (n) { return n; };
  o.talk = function (t) { _log.push('talk:' + o.name + ':' + t); };
  o.applyEffect = function (e, s) { _log.push('effect:' + o.name + ':' + e + ':' + s); return true; };
  o.removeEffect = function (e) { _log.push('uneffect:' + o.name + ':' + e); return true; };
  o.launch = function (d, f) { _log.push('launch:' + o.name + ':' + d.x + ',' + d.y + ',' + d.z + ':' + f); };
  return o;
}
function mkPlayer(name) {
  var o = _actor({ id: 'p_' + name, name: name, prefab: 'Player', isPlayer: true, pos: { x: 1, y: 2, z: 3 }, hp: 50,
    say: function (t) { _log.push('say:' + name + ':' + t); }, give: function (i, n) { _log.push('give:' + name + ':' + i + ':' + n); return n; },
    take: function (i, n) { _log.push('take:' + name + ':' + i + ':' + n); return n; }, count: function (i) { return 7; }, has: function (i, n) { return true; },
    teleport: function (p) { _log.push('tp:' + name); return true; },
    banner: function (t) { _log.push('banner:' + name + ':' + t); }, setStamina: function (n) { _log.push('stamina:' + name + ':' + n); },
    skill: function (s) { return 42; }, inventory: function () { return [{ prefab: 'Wood', count: 5 }, { prefab: 'Stone', count: 2 }]; },
    equipped: function () { return { right: 'AxeStone', left: '', chest: 'ArmorRagsChest' }; } });
  return o;
}
function mkCreature(prefab) {
  var o = _actor({ id: 'c_' + prefab, name: prefab, prefab: prefab, isPlayer: false, pos: { x: 5, y: 0, z: 5 }, hp: 20 });
  o.tame = function () { _log.push('tame:' + prefab); return true; }; o.remove = function () { _log.push('remove:' + prefab); return true; };
  o.setName = function (t) { _log.push('name:' + prefab + ':' + t); };
  return o;
}
// runs what was waiting (timers made by a wait or an after), in order, until nothing waits: each shows as @seconds
function _pump(limit) {
  for (var i = 0; i < (limit || 400); i++) {
    var idx = -1; for (var k = 0; k < timers.length; k++) if (timers[k].once) { idx = k; break; }
    if (idx < 0) return;
    var t = timers.splice(idx, 1)[0];
    _log.push('@' + t.s); t.f();
  }
}
";

        private static Engine Run(string js)
        {
            var engine = new Engine();
            engine.Execute(Stubs);
            engine.Execute(js);
            return engine;
        }

        private static string Log(Engine e) => e.Evaluate("_log.join('|')").AsString();

        private static Block B(string type, params (string key, string value)[] fields)
        {
            var b = new Block(type);
            foreach (var f in fields) b.Fields[f.key] = f.value;
            return b;
        }

        private static Block In(Block b, string key, Block child) { b.Inputs[key] = child; return b; }

        private static Block Do(Block b, params Block[] children) { b.Body("do").AddRange(children); return b; }

        private static Program Prog(params Block[] hats)
        {
            var p = new Program { Name = "Test" };
            foreach (Block h in hats) { var top = new Program.Top(); top.Chain.Add(h); p.Tops.Add(top); }
            return p;
        }

        private static Program LuckyBoars() => Prog(
            Do(B("on_kill", ("creature", "Boar")),
                Do(In(B("if"), "cond", B("chance", ("p", "10"))),
                    In(In(B("give", ("n", "2"), ("item", "Honey")), "who", B("ev_killer")), "who", B("ev_killer")),
                    In(In(B("say", ("text", "Lucky boar!")), "who", B("ev_killer")), "who", B("ev_killer")))));

        private static void LuckyBoarsRuns()
        {
            Compiled c = Compiler.Compile(LuckyBoars());
            Check(c.Ok, "Lucky Boars compiles: " + string.Join("; ", c.Problems.Select(p => p.Message)));
            if (!c.Ok) return;
            Check(c.Needs.SequenceEqual(new[] { "messages", "items" }) || c.Needs.OrderBy(x => x).SequenceEqual(new[] { "items", "messages" }), "needs are items and messages: " + string.Join(",", c.Needs));
            Check(c.Js.Contains("// needs:   "), "has a needs line");
            Engine e = Run(c.Js);
            e.Execute("handlers.kill[0]({ victim: mkCreature('Boar'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "give:Bob:Honey:2|say:Bob:Lucky boar!", "boar kill gives honey and says: " + Log(e));
            e.Execute("_log.length = 0; handlers.kill[0]({ victim: mkCreature('Neck'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "", "a neck kill does nothing: " + Log(e));
            e.Execute("_log.length = 0; handlers.kill[0]({ victim: mkCreature('Boar'), killer: null, pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "", "no killer: nothing, and no error: " + Log(e));
            e.Execute("_log.length = 0; handlers.kill[0]({ victim: mkCreature('Boar'), killer: mkCreature('Wolf'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "", "a creature killer is not given honey: " + Log(e));
        }

        private static void TextIsNeverCode()
        {
            string evil = "\"); evil(); //\n2028'\\`${evil()}";
            var p = Prog(Do(B("on_join"), In(B("say", ("text", evil)), "who", B("ev_player")), B("announce", ("text", evil))));
            Compiled c = Compiler.Compile(p);
            Check(c.Ok, "evil text compiles");
            var e = new Engine();
            e.Execute(Stubs + "function evil() { throw new Error('INJECTED'); }");
            try
            {
                e.Execute(c.Js);
                e.Execute("handlers.playerJoin[0]({ player: mkPlayer('Ann') });");
                Check(Log(e).Contains("INJECTED") == false && Log(e).Contains("say:Ann:"), "evil text was only text: " + Log(e));
            }
            catch (Exception ex) { Check(false, "evil text broke the script: " + ex.Message); }

            var v = new Program { Name = "x\n// ==/Script==\nevil();" };
            v.Vars.Add(Naming.Var("a b); evil(); //") ?? "x");
            var vc = Compiler.Compile(v);
            Check(vc.Ok && !vc.Js.Split('\n').Any(l => l.StartsWith("evil")), "a name with a newline cannot start a line of code");
            Check(Naming.VarId("a b").StartsWith("v_") && Naming.VarId("a b").All(ch => char.IsLetterOrDigit(ch) || ch == '_'), "variable ids are hex");
        }

        private static void MistakesAreFatalAndPointAtTheBlock()
        {
            Block killer = B("ev_killer");
            Compiled scope = Compiler.Compile(Prog(Do(B("on_morning"), In(B("say", ("text", "x")), "who", killer))));
            Check(!scope.Ok && scope.Problems.Any(p => p.Fatal && p.Block == killer), "the killer is not available when morning begins");

            Block day = B("day_number");
            Compiled type = Compiler.Compile(Prog(Do(B("on_join"), In(B("say"), "who", day))));
            Check(!type.Ok && type.Problems.Any(p => p.Block == day), "a number does not fit a player socket");

            Block empty = B("say", ("text", "hi"));
            Compiled gap = Compiler.Compile(Prog(Do(B("on_join"), empty)));
            Check(!gap.Ok && gap.Problems.Any(p => p.Block == empty), "an empty socket is a problem on the block");

            Block bad = B("give", ("n", "1"), ("item", "SwordCheat"));
            Compiled item = Compiler.Compile(Prog(Do(B("on_join"), In(bad, "who", B("ev_player")))));
            Check(!item.Ok && item.Problems.Any(p => p.Block == bad), "an item that is not on the allow list cannot be named");

            Block unknown = B("launch_missiles");
            Compiled u = Compiler.Compile(Prog(Do(B("on_join"), unknown)));
            Check(!u.Ok && u.Problems.Any(p => p.Block == unknown), "an unknown block is a problem");

            Compiled loose = Compiler.Compile(new Program { Tops = { new Program.Top { Chain = { B("announce", ("text", "x")) } } } });
            Check(loose.Ok && loose.Problems.Any(p => !p.Fatal), "loose blocks are only a warning");
            Check(!loose.Js.Contains("broadcast"), "loose blocks make no code");

            Block pick = B("weather", ("kind", "hail"), ("n", "2"));
            Check(!Compiler.Compile(Prog(Do(B("on_start"), pick))).Ok, "a weather that is not in the list cannot be used");
        }

        private static void NumbersAreClamped()
        {
            Compiled c = Compiler.Compile(Prog(Do(B("on_join"), In(In(B("give", ("n", "99999"), ("item", "Wood")), "who", B("ev_player")), "who", B("ev_player")))));
            Check(c.Ok && c.Js.Contains("_bGive(e.player, \"Wood\", 10)"), "the amount is cut to the slot's top (10): " + c.Js);
            Compiled w = Compiler.Compile(Prog(Do(B("on_start"), B("weather", ("kind", "rain"), ("n", "-4")))));
            Check(w.Ok && w.Js.Contains("world.setWeather(\"rain\", 1 * 60)"), "a negative time is raised to the lowest");
            Compiled nan = Compiler.Compile(Prog(Do(B("on_start"), B("weather", ("kind", "rain"), ("n", "abc")))));
            Check(nan.Ok && nan.Js.Contains("world.setWeather(\"rain\", 2 * 60)"), "text in a number slot falls back to the default");
        }

        private static void ManyThingsWork()
        {
            // every 5 minutes: set the weather, announce it; variables; math; loops
            var p = new Program { Name = "Many", About = "A mix." };
            p.Vars.Add("streak");
            Block chg = B("change_var", ("v", "streak"), ("n", "1"));
            Block ann = B("announce"); In(ann, "text", In(In(B("join"), "a", B("weather_now")), "b", B("var_get", ("v", "streak"))));
            Block each = Do(B("for_player"), In(In(B("give", ("n", "1"), ("item", "Coins")), "who", B("each_player")), "who", B("each_player")));
            Block tp = Do(B("repeat", ("n", "2")), B("log", ("text", "tick")));
            Block cmp = Do(In(B("if"), "cond", B("compare", ("op", ">=")).Also(x => x.Inputs["a"] = B("var_get", ("v", "streak")))), B("weather", ("kind", "storm"), ("n", "3")));
            Block hat = Do(B("every", ("n", "5"), ("unit", "minutes")), chg, ann, each, tp, cmp,
                In(B("remember", ("name", "seen"), ("val", "1")), "who", B("the_world")),
                B("set_time", ("t", "noon")),
                Do(B("after", ("n", "1"), ("unit", "seconds")), B("log", ("text", "later"))));
            p.Tops.Add(new Program.Top { Chain = { hat } });
            Compiled c = Compiler.Compile(p);
            Check(c.Ok, "the mix compiles: " + string.Join("; ", c.Problems.Select(x => x.Message)));
            if (!c.Ok) return;
            Engine e = Run(c.Js);
            e.Execute("timers[0].f();");
            string log = Log(e);
            Check(log.Contains("broadcast:clear1") && log.Contains("give:Ann:Coins:1") && log.Contains("give:Bob:Coins:1") && log.Contains("log:tick|log:tick"), "the mix did its things: " + log);
            Check(e.Evaluate("timers[0].s").AsNumber() == 300, "every 5 minutes is 300 seconds");
            Check(e.Evaluate("_store['world|seen']").AsNumber() == 1, "remember for the world stored");
            Check(c.LineBlock.Length == c.Js.Split('\n').Length - 1, "the line map covers every line");
            Check(c.LineBlock.Contains(chg.Id), "a line points back at its block");
            Check(c.Js.Contains("// ==Script==") && c.Js.Contains("// needs:   "), "the manifest block is first");
            var m = Scripts.Manifest.Read(c.Js, "x");
            Check(m.Problems.Count == 0 && m.Name == "Many" && m.Needs.Contains("messages") && m.Needs.Contains("items") && m.Needs.Contains("store") && m.Needs.Contains("weather"), "the host's own manifest reader accepts it: " + string.Join(",", m.Problems) + " " + string.Join(",", m.Needs));
        }

        private static Block Also(this Block b, Action<Block> a) { a(b); return b; }

        private static void JsonRoundTrips()
        {
            Program p = LuckyBoars();
            p.Vars.Add("score");
            string json = p.ToJson();
            Program q = Program.FromJson(json, out string error);
            Check(error == null && q != null, "reads what it wrote: " + error);
            Check(q.ToJson() == json, "writes the same again");
            Check(Compiler.Compile(q).Js == Compiler.Compile(p).Js, "compiles to the same code");
            Check(Program.FromJson("{ not json", out string err2) == null && err2 != null, "bad text is an error, not a crash");
            Check(Program.FromJson("{\"format\": 99}", out string err3) == null && err3 != null, "a newer format is refused");
            Check(Program.FromJson("{\"tops\":[{\"blocks\":[{\"type\":\"x\".repeat}]}]}", out _) == null, "broken json again");
            Program odd = Program.FromJson("{\"tops\":[{\"x\":1,\"y\":2,\"blocks\":[{\"type\":\"nonsense\",\"f\":{\"a\":null,\"b\":5}},{\"nothing\":1},7]}]}", out string err4);
            Check(odd != null && odd.Tops.Count == 1 && odd.Tops[0].Chain.Count == 1, "odd entries are skipped: " + err4);
            Check(!Compiler.Compile(odd).Ok, "a block that does not exist cannot run");
        }

        private static void LimitsHold()
        {
            var big = new Program();
            for (int i = 0; i < 80; i++)
            {
                var hat = B("on_join");
                for (int j = 0; j < 10; j++) hat.Body("do").Add(In(B("say", ("text", "x")), "who", B("ev_player")));
                big.Tops.Add(new Program.Top { Chain = { hat } });
            }
            Check(big.CountBlocks() > Program.MaxBlocks && !Compiler.Compile(big).Ok, "more than " + Program.MaxBlocks + " blocks is refused");

            var fine = new Program();
            for (int i = 0; i < 60; i++)
            {
                var hat = B("on_join");
                for (int j = 0; j < 10; j++) hat.Body("do").Add(In(B("say", ("text", "x")), "who", B("ev_player")));
                fine.Tops.Add(new Program.Top { Chain = { hat } });
            }
            Compiled big2 = Compiler.Compile(fine);
            Check(fine.CountBlocks() > 300 && fine.CountBlocks() <= Program.MaxBlocks && big2.Ok, "a program well over 300 blocks is fine: " + fine.CountBlocks() + " blocks, " + string.Join("; ", big2.Problems.Select(x => x.Message)));
            Check(big2.Ok && big2.Js.Length < 120 * 1024, "and its code fits the file limit: " + (big2.Js == null ? 0 : big2.Js.Length));

            Block deep = B("on_join"); Block cur = deep;
            for (int i = 0; i < Program.MaxDepth + 8; i++) { Block next = B("if"); In(next, "cond", B("is_night")); cur.Body("do").Add(next); cur = next; }
            Check(!Compiler.Compile(Prog(deep)).Ok, "blocks nested " + (Program.MaxDepth + 8) + " deep are refused");
        }

        private static void ShippedExamples()
        {
            var all = BlockExamples.All();
            Check(all.Count >= 7, "there are example scripts");
            foreach (var kv in all)
            {
                Compiled c = Compiler.Compile(kv.Value);
                Check(c.Ok, kv.Key + " compiles: " + string.Join("; ", c.Problems.Where(x => x.Fatal).Select(x => x.Message)));
                Check(c.Problems.Count == 0, kv.Key + " has no warnings: " + string.Join("; ", c.Problems.Select(x => x.Message)));
                if (!c.Ok) continue;
                Program again = Program.FromJson(kv.Value.ToJson(), out _);
                Check(Compiler.Compile(again).Js == c.Js, kv.Key + " survives a save and load");
                Check(Scripts.Manifest.Read(c.Js, "x").Problems.Count == 0, kv.Key + " has a manifest the host reads");
            }
            string Js(string file) => Compiler.Compile(all.First(x => x.Key == file).Value).Js;

            Engine e = Run(Js("welcome-wagon.js"));
            e.Execute("handlers.playerJoin[0]({ player: mkPlayer('Ann') });");
            Check(Log(e) == "say:Ann:Welcome, Ann! It is day 3, and the weather is clear.", "welcome wagon: " + Log(e));

            e = Run(Js("boss-announcer.js"));
            e.Execute("handlers.bossSpawn[0]({ boss: mkCreature('Eikthyr'), biome: 'Meadows', pos: {x:0,y:0,z:0} }); handlers.bossDeath[0]({ boss: mkCreature('Eikthyr'), biome: 'Meadows', pos: {x:0,y:0,z:0}, killer: null });");
            Check(Log(e) == "broadcast:Eikthyr has appeared in the Meadows|broadcast:Eikthyr has fallen!", "boss announcer: " + Log(e));

            e = Run(Js("kill-streaks.js"));
            e.Execute("for (var i = 0; i < 5; i++) handlers.kill[0]({ victim: mkCreature('Boar'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "say:Bob:5 kills!", "kill streaks cheers on the fifth: " + Log(e));
            e.Execute("_log.length = 0; handlers.kill[0]({ victim: mkPlayer('Ann'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "" && e.Evaluate("_store['p_Bob|kills']").AsNumber() == 5, "a player kill is not counted");

            e = Run(Js("weather-event.js"));
            e.Execute("timers[0].f();");
            Check(e.Evaluate("timers[0].s").AsNumber() == 1200 && Log(e) == "weather:rain:120|broadcast:The weather is turning.", "weather event: " + Log(e));

            e = Run(Compiler.Compile(BlockExamples.Hello()).Js);
            e.Execute("handlers.chat[0]({ player: mkPlayer('Ann'), text: 'Hello there' });");
            Check(Log(e) == "say:Ann:Hello, Ann", "hello answers: " + Log(e));
            e.Execute("_log.length = 0; handlers.chat[0]({ player: mkPlayer('Ann'), text: 'bye' });");
            Check(Log(e) == "", "hello ignores other words");

            e = Run(Js("lucky-boars.js"));
            e.Execute("handlers.kill[0]({ victim: mkCreature('Boar'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "give:Bob:Honey:2|say:Bob:Lucky boar!", "lucky boars: " + Log(e));
        }

        /// <summary>The translation key of a palette heading: lower case, letters and digits, other runs become one underscore ("Time and weather" is time_and_weather).</summary>
        internal static string GroupKey(string group)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in group.ToLowerInvariant()) { if (char.IsLetterOrDigit(c)) sb.Append(c); else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_'); }
            return sb.ToString().Trim('_');
        }

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "labels")
            {
                // for tools/scripts_keys.py: every block's English words as translation keys
                var o = new Newtonsoft.Json.Linq.JObject();
                foreach (BlockDef d in Defs.All)
                {
                    o["scripts_block_" + d.Id] = d.Label;
                    if (d.Group.Length > 0) o["scripts_group_" + GroupKey(d.Group)] = d.Group;
                    if (d.Category == "functions") o["scripts_cat_functions"] = "My blocks";
                    else if (o["scripts_cat_" + d.Category] == null) o["scripts_cat_" + d.Category] = char.ToUpperInvariant(d.Category[0]) + d.Category.Substring(1);
                }
                Console.WriteLine(o.ToString());
                return 0;
            }
            if (args.Length > 0 && args[0] == "palette")
            {
                // for docs/scripting-design.md: the whole palette, by tab and heading
                var sb = new System.Text.StringBuilder();
                foreach (string cat in Defs.Categories)
                {
                    sb.AppendLine("PALETTE-TAB " + cat + " " + Defs.All.Count(d => d.Category == cat));
                    foreach (var grp in Defs.All.Where(d => d.Category == cat).GroupBy(d => d.Group))
                    {
                        string labels = string.Join("; ", grp.Select(d => System.Text.RegularExpressions.Regex.Replace(d.Label.Replace("{do:body}", "").Replace("{do2:body}", "").Replace("{do3:body}", "").Replace("{else:body}", ""), @"\{(\w+):[^}]*\}", "[$1]").Trim() + (d.Danger ? " (red)" : "") + (d.Needs != null ? " [" + d.Needs + "]" : "")));
                        sb.AppendLine("PALETTE-GROUP " + (grp.Key.Length == 0 ? "(no heading)" : grp.Key) + " | " + labels);
                    }
                }
                Console.Write(sb.ToString());
                return 0;
            }
            ShippedExamples();
            Families();
            Fuzz();
            LuckyBoarsRuns();
            TextIsNeverCode();
            MistakesAreFatalAndPointAtTheBlock();
            NumbersAreClamped();
            ManyThingsWork();
            JsonRoundTrips();
            LimitsHold();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
