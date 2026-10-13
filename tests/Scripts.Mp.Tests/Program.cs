using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Scripts;
using Scripts.Mp;

namespace Scripts.Mp.Tests
{
    internal static class Tests
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL: " + what);
        }

        // ================================================================ a fake player's game

        private sealed class FakeGame : IGame
        {
            public double Health = 30, MaxHealthV = 30, MaxStaminaV = 50, Stamina = 20;
            public Dictionary<string, int> Bag = new Dictionary<string, int>();
            public int Room = 100;                       // how many more items fit
            public List<string> Log = new List<string>();
            public bool Zone = true, HasGround = true; public double GroundY = 12.0;
            public double LastTpY = double.NaN;
            public HashSet<string> Effects = new HashSet<string> { "Rested", "Wet", "Burning" };
            public HashSet<string> Known = new HashSet<string> { "Honey", "Wood", "Coins", "Wishbone", "TrophyEikthyr", "SwordCheat", "Hammer" };
            public bool Throw;

            public double Health_ => Health;
            double IGame.Health => Health;
            double IGame.MaxHealth => MaxHealthV;
            double IGame.MaxStamina => MaxStaminaV;
            public bool KnowsItem(string prefab) => Known.Contains(prefab);
            public bool KnowsEffect(string name) => Effects.Contains(name);
            public int AddItem(string prefab, int n) { if (Throw) throw new InvalidOperationException("boom"); int put = Math.Min(n, Room); if (put > 0) { Bag.TryGetValue(prefab, out int have); Bag[prefab] = have + put; Room -= put; } Log.Add("add " + prefab + " " + put); return put; }
            public int RemoveItem(string prefab, int n) { Bag.TryGetValue(prefab, out int have); int took = Math.Min(n, have); if (took > 0) { Bag[prefab] = have - took; Room += took; } Log.Add("remove " + prefab + " " + took); return took; }
            public double Heal(double n) { double d = Math.Min(n, MaxHealthV - Health); Health += d; Log.Add("heal " + d); return d; }
            public double Hurt(double n) { Health -= n; Log.Add("hurt " + n); return n; }
            public void SetStamina(double v) { Stamina = v; Log.Add("stamina " + v); }
            public bool AddEffect(string name, double seconds) { Log.Add("effect+ " + name + " " + seconds); return true; }
            public bool RemoveEffect(string name) { Log.Add("effect- " + name); return name == "Wet"; }
            public bool Launch(double dx, double dy, double dz, double force) { Log.Add("launch " + force); return true; }
            public bool ZoneLoaded(double x, double z) => Zone;
            public bool Ground(double x, double z, out double y) { y = GroundY; return HasGround; }
            public bool TeleportTo(double x, double y, double z) { LastTpY = y; Log.Add("tp " + x + " " + y + " " + z); return true; }
            public void Banner(string text) { Log.Add("banner " + text); }
        }

        // ================================================================ a loopback between a server and players

        private sealed class Loop : IServerLink
        {
            public double Clock = 100.0;
            public readonly Dictionary<long, FakeGame> Games = new Dictionary<long, FakeGame>();
            public readonly Dictionary<long, ClientPolicy> Policies = new Dictionary<long, ClientPolicy>();
            public readonly HashSet<long> Agents = new HashSet<long>();
            public readonly HashSet<long> Silent = new HashSet<long>();      // players whose game never answers
            public RemoteCore Server;
            public int SentFrames;
            public readonly List<Frame> FxSeen = new List<Frame>();

            public double Now => Clock;
            public bool HasAgent(long peer) => Agents.Contains(peer);

            public void Send(long peer, Frame f)
            {
                SentFrames++;
                // through the real packing, as on the wire
                if (!Frames.TryUnpack(Frames.Pack(f), out Frame got, out string why)) throw new Exception("could not pack: " + why);
                if (got.K == "fx") { FxSeen.Add(got); return; }
                if (got.K != "act" || Silent.Contains(peer)) return;
                Frame done = ActEngine.Handle(Games[peer], Policies[peer], got);
                if (!Frames.TryUnpack(Frames.Pack(done), out Frame back, out why)) throw new Exception("could not pack the answer: " + why);
                Server.OnDone(peer, back);
            }
        }

        private static Loop NewLoop(out ClientPolicy policy)
        {
            var loop = new Loop();
            loop.Server = new RemoteCore(loop);
            policy = new ClientPolicy();
            loop.Games[1] = new FakeGame(); loop.Policies[1] = policy; loop.Agents.Add(1);
            return loop;
        }

        // ================================================================ tests

        private static void FrameTests()
        {
            var small = new Frame("act", 7, new JObject { ["op"] = "give", ["p"] = "Honey" });
            byte[] packed = Frames.Pack(small);
            Check(packed[0] == 0, "a small message is not gzipped");
            Check(Frames.TryUnpack(packed, out Frame back, out _) && back.K == "act" && back.I == 7 && back.S("p") == "Honey", "a small message survives");

            var bigData = new JObject { ["text"] = new string('x', 50000) };
            byte[] bigPacked = Frames.Pack(new Frame("state", 1, bigData));
            Check(bigPacked[0] == 1 && bigPacked.Length < 2000, "a big message is gzipped small: " + bigPacked.Length);
            Check(Frames.TryUnpack(bigPacked, out Frame big, out _) && big.S("text").Length == 50000, "a big message survives");

            // what a stranger might send
            Check(!Frames.TryUnpack(null, out _, out _) && !Frames.TryUnpack(new byte[0], out _, out _) && !Frames.TryUnpack(new byte[] { 0 }, out _, out _), "empty is refused");
            Check(!Frames.TryUnpack(Concat(new byte[] { 0 }, Encoding.UTF8.GetBytes("{ not json")), out _, out string w1) && w1.Length > 0, "malformed JSON is refused: " + w1);
            Check(!Frames.TryUnpack(Concat(new byte[] { 0 }, Encoding.UTF8.GetBytes("{\"k\":\"Bad Kind!\",\"i\":1,\"d\":{}}")), out _, out _), "a bad kind is refused");
            Check(!Frames.TryUnpack(Concat(new byte[] { 0 }, Encoding.UTF8.GetBytes("{\"k\":\"" + new string('a', 40) + "\",\"i\":1,\"d\":{}}")), out _, out _), "a long kind is refused");
            Check(!Frames.TryUnpack(Concat(new byte[] { 9 }, Encoding.UTF8.GetBytes("{}")), out _, out _), "an unknown packing is refused");
            string deep = "{\"k\":\"x\",\"i\":1,\"d\":" + string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "1" + new string('}', 40) + "}";
            Check(!Frames.TryUnpack(Concat(new byte[] { 0 }, Encoding.UTF8.GetBytes(deep)), out _, out string w2), "a deeply nested message is refused: " + w2);
            // a gzip bomb: tiny packed, huge unpacked
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(1);
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) { var zeros = new byte[1024 * 1024]; for (int i = 0; i < 4; i++) gz.Write(zeros, 0, zeros.Length); }
                Check(ms.Length < 10000 && !Frames.TryUnpack(ms.ToArray(), out _, out string w3) && w3.Contains("big"), "a gzip bomb is refused");
            }
            bool threw = false;
            try { Frames.Pack(new Frame("state", 1, new JObject { ["t"] = string.Join(",", Enumerable.Range(0, 120000).Select(i => Guid.NewGuid().ToString("N"))) })); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "an oversized message is not sent");

            // parts
            var rng = new Random(4);
            var noise = new byte[200000]; rng.NextBytes(noise);
            List<byte[]> parts = Frames.Split(noise, 77);
            Check(parts.Count == 4, "200 KB goes in 4 parts: " + parts.Count);
            var re = new Frames.Reassembler();
            byte[] whole = null; bool done = false;
            foreach (int i in new[] { 2, 0, 3, 1 }) done = re.Add("peer", parts[i], 1.0, out whole, out _);
            Check(done && whole.SequenceEqual(noise), "parts come together in any order");
            Check(re.Add("peer", new byte[] { 1, 0, 9, 3, 1 }, 1.0, out _, out string wp) == false && wp.Length > 0, "a part with bad numbers is refused");
            // a flood of half-sent messages
            var re2 = new Frames.Reassembler();
            int refused = 0;
            for (ushort n = 1; n <= 10; n++) { re2.Add("flood", Frames.Split(noise, n)[0], 1.0, out _, out string why); if (why.Contains("too many")) refused++; }
            Check(refused == 6, "a sender may have only 4 messages half arrived: refused " + refused);
            re2.Add("flood", Frames.Split(noise, 99)[0], 20.0, out _, out string stale);
            Check(stale == "", "old half-sent messages are forgotten after 10 seconds");
            Check(new Frames.Reassembler().Add("s", Frames.Split(new byte[10], 1)[0], 0.0, out byte[] one, out _) && one.Length == 10, "a single part is whole");
        }

        private static byte[] Concat(byte[] a, byte[] b) => a.Concat(b).ToArray();

        private static void LimiterTests()
        {
            var limiter = new PeerLimiter();
            int allowed = 0;
            for (int i = 0; i < 1000; i++) if (limiter.Allow(5.0, 100)) allowed++;
            Check(allowed == 120 && limiter.Dropped == 880, "a flood is cut to the burst: " + allowed);
            Check(limiter.Allow(6.0, 100), "and it refills with time");
            var bytes = new PeerLimiter();
            Check(bytes.Allow(1.0, 1500000) && !bytes.Allow(1.0, 1500000), "bytes are limited too");

            var dedup = new Dedup();
            Check(dedup.IsNew("kill|1", 0.0) && !dedup.IsNew("kill|1", 30.0) && dedup.IsNew("kill|1", 61.0), "dedup remembers for a minute");
            var many = new Dedup(); int fine = 0;
            for (int i = 0; i < 20000; i++) if (many.IsNew("id" + i, 1.0)) fine++;
            Check(fine <= 5000, "a flood of ids cannot grow it without bound: " + fine);

            var p = new PendingTable(); Frame got = null; bool called = false;
            long id = p.Ask(5, 0.0, f => { got = f; called = true; });
            Check(!p.Answer(id, 6, new Frame("done", id, null)) && !called, "an answer from somebody else is ignored");
            Check(p.Answer(id, 5, new Frame("done", id, new JObject { ["ok"] = true })) && called && got.B("ok"), "the right peer's answer is taken");
            Check(!p.Answer(id, 5, new Frame("done", id, null)), "and only once");
            bool expired = false;
            p.Ask(5, 0.0, f => expired = f == null);
            p.Expire(11.0);
            Check(expired && p.Count == 0, "no answer in time gives null");
            bool dropped = false; p.Ask(7, 0.0, f => dropped = f == null); p.Drop(7);
            Check(dropped, "a player who leaves ends their questions");
        }

        private static void ValidateTests()
        {
            Check(Validate.Give("Honey", 99).Ok && Validate.Give("Honey", 99).N == 10, "give is cut to the list's most");
            Check(!Validate.Give("Wishbone", 1).Ok && !Validate.Give("TrophyEikthyr", 1).Ok && !Validate.Give("SwordCheat", 1).Ok && !Validate.Give("Nonsense", 1).Ok && !Validate.Give("", 1).Ok && !Validate.Give(new string('a', 100), 1).Ok, "never-give, trophies and strangers are refused");
            Check(Validate.Give("Honey", -5).N == 1 && Validate.Give("Honey", 0).N == 1, "a bad count becomes one");
            Check(!Validate.Take("Hammer", 1).Ok && Validate.Take("Wood", 999).N == 20, "take: a tool is never taken, the amount is cut");
            Check(Validate.Hurt(100, 30).N == 29 && Validate.Hurt(100, 200).N == 50 && !Validate.Hurt(5, 1).Ok && !Validate.Hurt(-1, 30).Ok && !Validate.Hurt(double.NaN, 30).Ok, "hurt never kills and never more than 50");
            Check(!Validate.Heal(0).Ok && Validate.Heal(9999).N == 500, "heal is capped");
            Check(Validate.Launch(1, 0, 0, 99).N == 15 && !Validate.Launch(0, 0, 0, 5).Ok && !Validate.Launch(1, 0, 0, 0).Ok && !Validate.Launch(double.NaN, 0, 0, 5).Ok, "launch force is capped and needs a direction");
            Check(!Validate.Teleport(false, true, 5).Ok && !Validate.Teleport(true, false, 5).Ok && Validate.Teleport(true, true, 12).N == 13, "teleport lands one metre over the ground");
            Check(Validate.Effect("Rested", 99999).N == 600 && Validate.Effect("Rested", 1).N == 5 && !Validate.Effect("Berserker", 30).Ok && !Validate.Effect(null, 30).Ok, "effects: only the list, 5 s up to the longest");
            Check(Validate.EnvOf("storm") == "ThunderStorm" && Validate.EnvOf("lava") == "" && !Validate.Env("lava", 60).Ok && Validate.Env("rain", 99999).N == 1800, "weather names");
            Check(!Validate.Fx("fx_Lightning", true).Ok && Validate.Fx("fx_Lightning", false).Ok && !Validate.Fx("Boar", false).Ok && Validate.Fx("sfx_firework_explode", true).Ok, "effect and sound names are kept apart");
            Check(Validate.Text("a\nb\r\tc", 3) == "a b" && Validate.Text(null, 5) == "", "text is one clean line");
            Check(Validate.Scale(99) == 3 && Validate.Scale(double.NaN) == 1 && Validate.Range(1000) == 60, "scale and range are capped");
        }

        private static void ActTests()
        {
            var game = new FakeGame();
            var policy = new ClientPolicy();
            Frame Act(string op, JObject args) { args["op"] = op; return ActEngine.Handle(game, policy, new Frame("act", 9, args)); }

            Frame r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 4 });
            Check(r.K == "done" && r.I == 9 && r.B("ok") && r.N("n") == 4 && game.Bag["Honey"] == 4, "give puts items in the bag");
            game.Room = 2;
            r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 10 });
            Check(r.B("ok") && r.N("n") == 2 && game.Bag["Honey"] == 6, "only what fits goes in, and the answer says how many");
            game.Room = 0;
            r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 3 });
            Check(!r.B("ok") && r.S("why") == "the bag is full" && game.Bag["Honey"] == 6, "a full bag loses nothing");
            game.Room = 100;
            // a rogue server
            foreach (string bad in new[] { "Wishbone", "TrophyEikthyr", "SwordCheat", "Hammer" })
            {
                int before = game.Bag.Values.Sum();
                r = Act("give", new JObject { ["p"] = bad, ["n"] = 1 });
                Check(!r.B("ok") && game.Bag.Values.Sum() == before, "a rogue server cannot give " + bad + ": " + r.S("why"));
            }
            r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 100000 });
            Check(r.N("n") <= 10, "a rogue amount is cut to the list's most: " + r.N("n"));

            // the risky ones are off by default
            r = Act("take", new JObject { ["p"] = "Honey", ["n"] = 1 });
            Check(!r.B("ok") && r.S("why") == "the player does not allow it", "take is off by default");
            r = Act("teleport", new JObject { ["x"] = 10, ["z"] = 10 });
            Check(!r.B("ok") && double.IsNaN(game.LastTpY), "teleport is off by default");
            r = Act("launch", new JObject { ["dx"] = 1, ["f"] = 10 });
            Check(!r.B("ok") && !game.Log.Any(l => l.StartsWith("launch")), "launch is off by default");
            policy.Take = policy.Teleport = policy.Launch = true;

            int bag = game.Bag["Honey"];
            r = Act("take", new JObject { ["p"] = "Honey", ["n"] = 3 });
            Check(r.B("ok") && r.N("n") == 3 && game.Bag["Honey"] == bag - 3, "take removes what is there and says how much");
            r = Act("take", new JObject { ["p"] = "Hammer", ["n"] = 1 });
            Check(!r.B("ok"), "a rogue server cannot take a hammer");
            r = Act("take", new JObject { ["p"] = "Coins", ["n"] = 1 });
            Check(!r.B("ok") && r.S("why") == "they have none", "taking what is not there is a no, not a fault");

            // health: never lethal
            game.Health = 30;
            r = Act("hurt", new JObject { ["n"] = 100 });
            Check(r.B("ok") && game.Health == 1 || game.Health >= 1, "a lethal hurt leaves a point: " + game.Health);
            game.Health = 1;
            r = Act("hurt", new JObject { ["n"] = 100 });
            Check(!r.B("ok") && game.Health == 1, "at one point of health, hurt does nothing");
            game.Health = 10;
            r = Act("heal", new JObject { ["n"] = 5 });
            Check(r.B("ok") && r.N("n") == 5 && game.Health == 15, "heal");
            game.Health = 30;
            r = Act("heal", new JObject { ["n"] = 5 });
            Check(!r.B("ok") && r.S("why") == "already at full health", "heal at full is a no");
            r = Act("stamina", new JObject { ["n"] = 9999 });
            Check(r.B("ok") && game.Stamina == 50, "stamina is cut to the maximum");

            r = Act("effect+", new JObject { ["name"] = "Rested", ["s"] = 99999 });
            Check(r.B("ok") && r.N("n") == 600, "an effect is cut to its longest time");
            r = Act("effect+", new JObject { ["name"] = "Berserker", ["s"] = 30 });
            Check(!r.B("ok"), "an effect not on the list is refused");
            game.Effects.Remove("Wet");
            r = Act("effect+", new JObject { ["name"] = "Wet", ["s"] = 30 });
            Check(!r.B("ok") && r.S("why").Contains("no status effect"), "an effect the game does not have is refused");
            r = Act("effect-", new JObject { ["name"] = "Wet" });
            Check(r.B("ok"), "effect- ");

            r = Act("launch", new JObject { ["dx"] = 0, ["dy"] = 1, ["dz"] = 0, ["f"] = 500 });
            Check(r.B("ok") && game.Log.Contains("launch 15"), "launch force is capped at 15");
            r = Act("launch", new JObject { ["f"] = 5 });
            Check(!r.B("ok"), "a launch with no direction is refused");

            // the teleport ignores the height the server gave
            game.GroundY = 12;
            r = Act("teleport", new JObject { ["x"] = 10, ["y"] = 9999, ["z"] = 20 });
            Check(r.B("ok") && game.LastTpY == 13, "a teleport lands one metre over the ground, never at the server's height: " + game.LastTpY);
            game.Zone = false;
            r = Act("teleport", new JObject { ["x"] = 10, ["z"] = 20 });
            Check(!r.B("ok") && r.S("why") == "no ground to land on there", "an unloaded zone is a no");
            game.Zone = true; game.HasGround = false;
            r = Act("teleport", new JObject { ["x"] = 10, ["z"] = 20 });
            Check(!r.B("ok"), "no ground is a no");

            r = Act("banner", new JObject { ["text"] = new string('B', 500) });
            Check(r.B("ok") && game.Log.Last() == "banner " + new string('B', 60), "a banner is cut to 60 characters");
            r = Act("banner", new JObject { ["text"] = "  " });
            Check(!r.B("ok"), "an empty banner is a no");

            r = Act("explode", new JObject());
            Check(!r.B("ok") && r.S("why") == "unknown action", "an unknown action is a no");
            policy.Give = false;
            r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 1 });
            Check(!r.B("ok") && r.S("why") == "the player does not allow it", "the player's own setting wins");
            policy.Give = true; game.Throw = true;
            r = Act("give", new JObject { ["p"] = "Honey", ["n"] = 1 });
            Check(!r.B("ok") && r.S("why").StartsWith("failed"), "a fault in the game is an answer, never an exception: " + r.S("why"));
        }

        private static void RemoteTests()
        {
            var loop = NewLoop(out ClientPolicy policy);
            FakeGame game = loop.Games[1];
            int hourly = 0;
            var notes = new List<string>();

            // a give: asked 5, the bag fits 3
            game.Room = 3;
            Outcome o = loop.Server.Give(1, "Honey", 5, 50 - hourly, n => hourly += n, (got, why) => notes.Add("gave " + got + " " + why));
            Check(o.Sent && o.Value == 5, "give returns what was asked after the caps at once");
            Check(hourly == 3 && notes.Count == 1 && notes[0].StartsWith("gave 3"), "the hourly count is settled from the answer (reserved 5, gave 3): " + hourly + " " + string.Join("|", notes));

            // cut by the hourly count
            hourly = 48; notes.Clear(); game.Room = 100;
            o = loop.Server.Give(1, "Honey", 10, 50 - hourly, n => hourly += n, (got, why) => notes.Add("gave " + got));
            Check(o.Sent && o.Value == 2 && hourly == 50, "give is cut to what is left of the hour: " + o.Value);
            o = loop.Server.Give(1, "Honey", 10, 50 - hourly, n => hourly += n, (got, why) => { });
            Check(!o.Sent && o.Why.Contains("hourly"), "none left of the hour is a refusal");
            // never-give on the server side too
            o = loop.Server.Give(1, "Wishbone", 1, 50, n => { }, (g, w) => { });
            Check(!o.Sent && loop.SentFrames == 2, "a never-give item is not even sent");
            // no agent
            o = loop.Server.Give(2, "Honey", 1, 50, n => { }, (g, w) => { });
            Check(!o.Sent && o.Why == RemoteCore.NoAgent, "a player without the mod is a refusal that says so");

            // take: off on the player's side, refunded
            hourly = 0; notes.Clear();
            o = loop.Server.Take(1, "Honey", 5, 9, 20 - hourly, n => hourly += n, (got, why) => notes.Add("took " + got + " " + why));
            Check(o.Sent && o.Value == 5 && hourly == 0 && notes[0] == "took 0 the player does not allow it", "a take the player does not allow is refunded and says why: " + hourly + " " + string.Join("|", notes));
            policy.Take = true; game.Bag["Honey"] = 4; notes.Clear();
            o = loop.Server.Take(1, "Honey", 10, 4, 20 - hourly, n => hourly += n, (got, why) => notes.Add("took " + got));
            Check(o.Value == 4 && hourly == 4 && notes[0] == "took 4", "take counts what the player's game really took: " + hourly);

            // no answer
            loop.Silent.Add(1); hourly = 0; notes.Clear();
            o = loop.Server.Give(1, "Honey", 5, 50, n => hourly += n, (got, why) => notes.Add("gave " + got + " " + why));
            Check(o.Sent && hourly == 5 && notes.Count == 0, "while waiting, the hour is reserved");
            loop.Clock += 11; loop.Server.Tick();
            Check(hourly == 0 && notes.Count == 1 && notes[0] == "gave 0 no answer from their game", "no answer gives the reservation back: " + string.Join("|", notes));
            loop.Silent.Clear();

            // an answer from somebody else's game is ignored
            loop.Agents.Add(2); loop.Games[2] = new FakeGame(); loop.Policies[2] = new ClientPolicy();
            loop.Silent.Add(1); notes.Clear();
            loop.Server.Give(1, "Honey", 5, 50, n => { }, (got, why) => notes.Add("gave " + got));
            Check(!loop.Server.OnDone(2, new Frame("done", 1, new JObject { ["ok"] = true, ["n"] = 5 })) && notes.Count == 0, "peer 2 cannot answer peer 1's question");
            loop.Silent.Clear();

            // the other ops through the same path
            var settled = new List<string>();
            string why2 = loop.Server.Act(1, "heal", new JObject { ["n"] = 5 }, f => settled.Add(f == null ? "none" : f.S("why") + f.N("n")));
            Check(why2 == "" && settled.Count == 1, "an action with an answer is settled");
            Check(loop.Server.Act(3, "heal", new JObject(), null) == RemoteCore.NoAgent, "no agent: no send");

            // fx goes only to those who have the mod
            loop.FxSeen.Clear();
            int sent = loop.Server.Fx(new long[] { 1, 2, 3, 1 }, new JObject { ["op"] = "banner", ["text"] = "hi" });
            Check(sent == 2 && loop.FxSeen.Count == 2 && loop.FxSeen[0].S("op") == "banner", "fx reaches each game with the mod once: " + sent);

            // a player who leaves ends their questions
            loop = NewLoop(out policy);
            loop.Silent.Add(1); notes.Clear();
            loop.Server.Give(1, "Honey", 5, 50, n => { }, (got, why) => notes.Add("gave " + got + " " + why));
            loop.Server.PeerLeft(1);
            Check(notes.Count == 1 && notes[0].Contains("no answer"), "leaving settles what was waiting");
        }

        private static void EventTests()
        {
            var gate = new EventGate();
            bool Yes(string e, string s) => true;
            bool No(string e, string s) => false;
            Frame Ev(string name, string sub, string id) => new Frame("event", 0, new JObject { ["e"] = name, ["sub"] = sub, ["id"] = id });

            Check(gate.Accept(1, Ev("kill", "c1", "c1"), Yes, 0.0, out _), "a kill about what they own is accepted");
            Check(!gate.Accept(1, Ev("kill", "c1", "c1"), Yes, 1.0, out string dup) && dup == "duplicate", "the same kill again is a duplicate");
            Check(!gate.Accept(1, Ev("kill", "c2", "c2"), No, 1.0, out string notmine) && notmine == "not theirs", "an event about what they do not own is dropped");
            Check(!gate.Accept(1, Ev("startScript", "c2", "x"), Yes, 1.0, out string unk) && unk.Contains("not an event"), "an event that is not on the list is dropped (nothing a client sends can start a script)");
            Check(!gate.Accept(1, Ev("kill", "", "x"), Yes, 1.0, out _), "an event with no subject is dropped");
            Check(gate.Accept(1, Ev("itemPickup", "p1", ""), Yes, 2.0, out _) && gate.Accept(1, Ev("itemPickup", "p1", ""), Yes, 2.0, out _), "events without an id are not folded");

            // the server's own game saw the kill first
            Check(gate.Own("kill", "c9", 3.0) && !gate.Accept(1, Ev("kill", "c9", "c9"), Yes, 3.5, out string late) && late == "duplicate", "the server's own kill makes the player's copy a duplicate");
            Check(gate.Accept(1, Ev("kill", "c10", "c10"), Yes, 4.0, out _) && !gate.Own("kill", "c10", 4.1), "and the other way round");
            Check(gate.Own("kill", "", 5.0) && gate.Own("kill", "", 5.1) && gate.Own("kill", "0:0", 5.2) && gate.Own("kill", "0:0", 5.3), "kills with no id (a record already removed) are never taken for duplicates of each other");
            Check(gate.Accept(1, Ev("kill", "0:0", "0:0"), Yes, 6.0, out _) && gate.Accept(1, Ev("kill", "0:0", "0:0"), Yes, 6.1, out _), "and neither are forwarded ones");

            // a flood
            var flood = new EventGate(); int ok = 0;
            for (int i = 0; i < 1000; i++) if (flood.Accept(7, Ev("hit", "c1", "h" + i), Yes, 10.0, out _)) ok++;
            Check(ok == 100 && flood.Refused == 900, "50 events a second (burst 100): " + ok);
            Check(flood.Accept(8, Ev("hit", "c1", "other"), Yes, 10.0, out _), "another player's budget is their own");
        }

        private static void StateTests()
        {
            var cache = new StateCache();
            var inv = new JArray(); for (int i = 0; i < 300; i++) inv.Add(new JArray("Item" + i, i + 1, 1, "Name" + i));
            var state = new JObject { ["inv"] = inv, ["eq"] = new JArray("Club", "", "", "", ""), ["sk"] = new JObject { ["Swords"] = 33.3, ["Run"] = 120.0 }, ["de"] = 2, ["st"] = new JArray(40, 50), ["fl"] = 5, ["lk"] = new JArray(1, 2, 3) };
            Check(cache.Update(5, state, 100.0), "a report is taken");
            PlayerState s = cache.Get(5);
            Check(s.Inventory.Count == StateCache.MaxItems && s.Count("Item0") == 1 && s.Count("Item119") == 120 && s.Count("Item200") == 0, "a long bag is cut at " + StateCache.MaxItems);
            Check(s.Equipped[0] == "Club" && s.Skills["Swords"] == 33.3 && s.Skills["Run"] == 100.0, "worn items and skills (a skill never over 100)");
            Check(s.Deaths == 2 && s.Stamina == 40 && s.MaxStamina == 50 && s.Swimming && !s.Sitting && s.Running && !s.Blocking && s.HasLook && s.LookY == 2, "flags, stamina, look");
            Check(cache.Age(5, 107.0) == 7.0 && cache.Age(6, 107.0) > 1e9, "its age");
            Check(!cache.Update(6, new JObject { ["inv"] = new JArray { new JArray("Honey", "many") } }, 1.0) && cache.Get(6) == null, "a report that makes no sense changes nothing");
            cache.Update(6, new JObject { ["inv"] = new JArray { new JArray(new string('x', 500), 2) } }, 1.0);
            Check(cache.Get(6).Inventory[0].Prefab.Length == StateCache.MaxText, "long text is cut");
            cache.Remove(5);
            Check(cache.Get(5) == null, "a player who leaves is forgotten");
        }

        private static void ListTests()
        {
            var own = new HashSet<string>(Lists.NeverGive);
            int ownAllow = Lists.Allow.Count;
            var server = new JObject
            {
                ["allow"] = new JObject { ["Honey"] = 99, ["Wishbone"] = 10, ["TrophyEikthyr"] = 5, ["SwordCheat"] = 3, ["Wood"] = 4, ["Custom"] = 2 },
                ["neverGive"] = new JArray("Honey2"),
                ["neverTake"] = new JArray("Coins"),
                ["status"] = new JObject { ["Rested"] = 9999, ["Berserker"] = 30 },
                ["creatures"] = new JArray("Boar"),
                ["visual"] = new JArray("fx_Lightning"), ["sounds"] = new JArray("sfx_spawn"), ["pieces"] = new JArray("piece_banner01"),
            };
            Lists.Apply(server);
            Check(!Lists.Allow.ContainsKey("Wishbone") && !Lists.Allow.ContainsKey("TrophyEikthyr") && !Lists.Allow.ContainsKey("SwordCheat"), "a server cannot allow what this game forbids: boss items, trophies, cheats");
            Check(Lists.Allow["Honey"] == 10 && Lists.Allow["Wood"] == 4 && Lists.Allow["Custom"] == 2, "the server's amounts apply, never over ten");
            Check(Lists.NeverGive.Contains("Wishbone") && Lists.NeverGive.Contains("Honey2") && Lists.NeverTake.Contains("Coins") && Lists.NeverTake.Contains("Hammer"), "a server can only add to the never-lists");
            Check(Lists.StatusEffects["Rested"] == 600 && Lists.Creatures.Count == 1 && Lists.VisualEffects.Count == 1, "other lists are the server's, effects cut to ten minutes");
            Check(!Validate.Give("Wishbone", 1).Ok && Validate.Give("Custom", 5).N == 2 && !Validate.Give("Coins", 1).Ok, "the checks follow the server's lists");
            Lists.Reset();
            Check(Lists.Allow.Count == ownAllow && Lists.Allow.ContainsKey("Coins") && !Lists.NeverGive.Contains("Honey2") && Lists.NeverGive.SetEquals(own) && !Lists.NeverTake.Contains("Coins"), "leaving a server puts this game's own lists back");
            Lists.Apply(new JObject { ["allow"] = new JObject { ["Honey"] = "many" } });
            Check(Lists.Allow.Count == ownAllow && Lists.Allow.ContainsKey("Coins"), "lists that make no sense put this game's own back");
            Lists.Reset();
            JObject round = Lists.ToJson();
            Check(round["allow"]["Honey"] != null && ((JArray)round["neverGive"]).Count == own.Count, "the lists go over the wire as data");
        }

        private static int Main()
        {
            FrameTests();
            LimiterTests();
            ValidateTests();
            ActTests();
            RemoteTests();
            EventTests();
            StateTests();
            ListTests();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
