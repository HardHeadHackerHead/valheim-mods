using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;
using Scripts.Collab;
using Scripts.Mp;

namespace Scripts.CollabNet.Tests
{
    /// <summary>A network that delays messages (never reordering one connection), can drop some, and runs on a clock the test moves.</summary>
    internal sealed class Net
    {
        private sealed class Packet { internal double At; internal long Order; internal Action Do; }

        private readonly List<Packet> _q = new List<Packet>();
        private readonly Dictionary<string, double> _last = new Dictionary<string, double>();
        private readonly Random _r;
        private long _order;
        internal double Now;
        internal double MinDelay = 0.02, MaxDelay = 0.15;
        internal static readonly DateTime Start = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
        internal DateTime Date => Start.AddSeconds(Now);

        internal Net(int seed) { _r = new Random(seed); }

        internal void Post(string connection, Action deliver)
        {
            double at = Now + MinDelay + _r.NextDouble() * (MaxDelay - MinDelay);
            if (_last.TryGetValue(connection, out double last) && at <= last) at = last + 0.0001;
            _last[connection] = at;
            _q.Add(new Packet { At = at, Order = _order++, Do = deliver });
        }

        internal void DeliverDue()
        {
            while (true)
            {
                Packet next = null;
                foreach (Packet p in _q) if (p.At <= Now && (next == null || p.At < next.At || (p.At == next.At && p.Order < next.Order))) next = p;
                if (next == null) return;
                _q.Remove(next);
                next.Do();
            }
        }

        internal int InFlight => _q.Count;
    }

    internal sealed class FakeHost : ICollabHost
    {
        internal readonly Net Net;
        internal readonly Dictionary<string, string> Disk = new Dictionary<string, string>();
        internal readonly HashSet<string> Admins = new HashSet<string>();
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        internal readonly Dictionary<string, CollabSession> Clients = new Dictionary<string, CollabSession>();
        internal readonly List<string> Audits = new List<string>(), WroteBy = new List<string>();
        internal int Saves;
        internal string DropOneBatchTo = "";
        internal int Sent;

        internal FakeHost(Net net) { Net = net; }

        public DateTime Now => Net.Date;
        public bool IsAdmin(string editor) => Admins.Contains(editor);
        public string NameOf(string editor) => Names.TryGetValue(editor, out string n) ? n : editor;
        public bool Exists(string file) => Disk.ContainsKey(file);

        public Program Load(string file, out string error)
        {
            error = null;
            return Disk.TryGetValue(file, out string json) ? Program.FromJson(json, out error) : null;
        }

        public string Save(string file, Program program) { Disk[file] = program.ToJson(); Saves++; return ""; }
        public void Audit(string line) { Audits.Add(line); }
        public void Wrote(string file, string who) { WroteBy.Add(who); LastWriter.Set(file, who); }

        public void Send(string editor, string kind, string json)
        {
            Sent++;
            if (kind == Co.Batch && DropOneBatchTo == editor) { DropOneBatchTo = ""; return; }
            Net.Post("s>" + editor, () => { if (Clients.TryGetValue(editor, out CollabSession s)) s.OnMessage(kind, json); });
        }
    }

    internal sealed class Ed
    {
        internal string Key = "";
        internal CollabSession S;
        internal readonly List<string> Messages = new List<string>();
        internal int Changed, Resynced;
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

        private const string File = "demo.js";

        // ================================================================== the setup

        private sealed class World
        {
            internal Net Net;
            internal FakeHost Host;
            internal CollabServer Server;
            internal readonly Dictionary<string, Ed> Eds = new Dictionary<string, Ed>();
            internal double NextTick;

            internal World(int seed, bool ids = true)
            {
                Net = new Net(seed);
                Host = new FakeHost(Net);
                Server = new CollabServer(Host);
                LastWriter.Clear();
                LastWriter.Clock = () => Net.Date;
                Program p = Start();
                if (ids) Uids.Ensure(p);
                Host.Disk[File] = p.ToJson();
            }

            internal static Program Start()
            {
                var p = new Program { Name = "Demo", About = "a script" };
                for (int t = 0; t < 3; t++)
                {
                    var hat = new Block("on_join");
                    for (int i = 0; i < 3; i++) { var say = new Block("announce"); say.Fields["text"] = "hello " + t + "." + i; hat.Body("do").Add(say); }
                    var top = new Program.Top { X = 30 + t * 300, Y = 30 };
                    top.Chain.Add(hat);
                    p.Tops.Add(top);
                }
                return p;
            }

            internal Ed Join(string key, string name, bool admin = true, bool open = true)
            {
                Host.Names[key] = name;
                if (admin) Host.Admins.Add(key);
                var ed = new Ed { Key = key };
                var s = new CollabSession(File) { Clock = () => Net.Date };
                s.Send = (kind, json) => { Net.Post(key + ">s", () => Server.OnMessage(key, kind, json)); return true; };
                s.Message += m => ed.Messages.Add(m);
                s.Changed += (ids, structural) => ed.Changed++;
                s.Resynced += () => ed.Resynced++;
                ed.S = s;
                Host.Clients[key] = s;
                Eds[key] = ed;
                if (open) s.Open();
                return ed;
            }

            /// <summary>Time passes: messages arrive, the server ticks four times a second, every editor says it is still here.</summary>
            internal void Run(double seconds)
            {
                double end = Net.Now + seconds;
                while (Net.Now < end)
                {
                    Net.Now += 0.05;
                    Net.DeliverDue();
                    if (Net.Now >= NextTick) { Server.Tick(); NextTick = Net.Now + 0.25; }
                    foreach (Ed e in Eds.Values.ToList()) e.S.Tick();
                }
            }

            internal bool Converged()
            {
                Program master = Server.ProgramOf(File);
                if (master == null) return false;
                foreach (Ed e in Eds.Values)
                    if (e.S.Active && !Canon.Equal(e.S.Live, master)) return false;
                return true;
            }
        }

        // ================================================================== what an editor does (as BlockView does to its program)

        private static readonly string[] Words = { "", "one", "two", "storm", "Boar", "a b", "9", "x" };

        private static void Edit(Ed e, Random r)
        {
            Program p = e.S.Live;
            switch (r.Next(6))
            {
                case 0:
                    {
                        var hat = new Block("on_chat"); hat.Fields["text"] = Words[r.Next(Words.Length)];
                        var say = new Block("announce"); say.Fields["text"] = "new " + r.Next(100); hat.Body("do").Add(say);
                        var top = new Program.Top { X = r.Next(800), Y = r.Next(800) };
                        top.Chain.Add(hat);
                        p.Tops.Add(top);
                        break;
                    }
                case 1:
                    {
                        List<Block> all = AllBlocks(p);
                        if (all.Count > 0) all[r.Next(all.Count)].Fields["text"] = Words[r.Next(Words.Length)];
                        break;
                    }
                case 2:
                    {
                        List<List<Block>> bodies = Bodies(p);
                        if (bodies.Count > 0) { var b = new Block("announce"); b.Fields["text"] = "ins " + r.Next(100); List<Block> list = bodies[r.Next(bodies.Count)]; list.Insert(r.Next(list.Count + 1), b); }
                        break;
                    }
                case 3:
                    {
                        List<List<Block>> bodies = Bodies(p).Where(l => l.Count > 0).ToList();
                        if (bodies.Count > 0) { List<Block> list = bodies[r.Next(bodies.Count)]; list.RemoveAt(r.Next(list.Count)); }
                        break;
                    }
                case 4:
                    {
                        if (p.Tops.Count > 0) { Program.Top t = p.Tops[r.Next(p.Tops.Count)]; t.X += r.Next(-50, 50); t.Y += r.Next(-50, 50); }
                        break;
                    }
                default:
                    {
                        if (p.Tops.Count > 0) p.Tops[r.Next(p.Tops.Count)].Collapsed ^= true;
                        break;
                    }
            }
            e.S.Commit();
        }

        private static List<Block> AllBlocks(Program p) => p.Tops.SelectMany(t => t.Chain.SelectMany(b => b.All())).ToList();

        private static List<List<Block>> Bodies(Program p)
        {
            var lists = new List<List<Block>>();
            foreach (Block b in AllBlocks(p)) foreach (List<Block> l in b.Bodies.Values) lists.Add(l);
            return lists;
        }

        private static Block FirstSay(Program p) => AllBlocks(p).First(b => b.Type == "announce");

        // ================================================================== tests

        private static void Joining()
        {
            var w = new World(1);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob"), c = w.Join("p3", "Cat", admin: false);
            w.Run(1.0);
            Check(a.S.Active && b.S.Active, "the admins are in with a snapshot");
            Check(a.S.Me == "host" && b.S.Me == "p2", "each editor learns its own id from the server, not from itself");
            Check(!c.S.Active && c.S.Status == CollabSession.State.Closed && c.S.ClosedWhy.Contains("admins"), "a player who is not an admin is refused: " + c.S.ClosedWhy);
            Check(a.S.All.Count == 2 && b.S.All.Count == 2, "everyone sees who is here: " + a.S.All.Count);
            Check(a.S.Others.Single().Name == "Bob" && b.S.Others.Single().Name == "Ann", "the names are the server's");
            Check(a.S.All.Select(p => p.Color).Distinct().Count() == 2, "each editor has its own colour");
            Check(w.Server.NamesOf(File).Length == 2 && w.Server.NamesOf(File, "host").Single() == "Bob", "the library can say who is editing");
            Check(w.Converged(), "both start with the master's copy");
        }

        private static void IdsGivenOnFirstOpen()
        {
            var w = new World(2, ids: false);
            Check(!w.Host.Disk[File].Contains("\"u\""), "the file starts with no ids");
            Ed a = w.Join("host", "Ann");
            w.Run(1.0);
            Check(a.S.Active && Tree.IdsOk(a.S.Live), "the copy has an id on every block and stack");
            Check(w.Host.Saves == 1 && w.Host.Disk[File].Contains("\"u\""), "the ids were saved once, at open");
            Check(w.Host.Audits.Any(x => x.Contains("gave every block an id")), "and the audit says why");
        }

        private static void Editing()
        {
            var w = new World(3);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            Block say = FirstSay(a.S.Live);
            string bobSees = Tree.Get(b.S.Live, say.Uid).Fields["text"];
            say.Fields["text"] = "changed by Ann";
            Check(a.S.Commit(), "a commit has something to send");
            w.Run(1.0);
            Check(Tree.Get(b.S.Live, say.Uid).Fields["text"] == "changed by Ann" && bobSees != "changed by Ann", "Bob's copy shows Ann's change");
            Check(b.Changed > 0, "the view is told what changed");
            Check(w.Converged(), "the master has it too");
            Check(a.S.Pending == 0, "Ann's change was acknowledged");

            // two people change the same field: one wins, everywhere
            Block aSay = Tree.Get(a.S.Live, say.Uid), bSay = Tree.Get(b.S.Live, say.Uid);
            aSay.Fields["text"] = "Ann's words"; bSay.Fields["text"] = "Bob's words";
            a.S.Commit(); b.S.Commit();
            w.Run(1.5);
            Check(w.Converged(), "the same field changed twice ends the same everywhere: " + Tree.Get(a.S.Live, say.Uid).Fields["text"]);
            Check(Tree.Get(b.S.Live, say.Uid).Fields["text"] == Tree.Get(a.S.Live, say.Uid).Fields["text"], "both show one winner");

            // a new stack
            var hat = new Block("on_start"); var top = new Program.Top { X = 5, Y = 5 }; top.Chain.Add(hat); b.S.Live.Tops.Add(top);
            b.S.Commit();
            w.Run(1.0);
            Check(a.S.Live.Tops.Count == 4 && w.Converged(), "a new stack appears for Ann");

            // undo is only my own
            Block mine = Tree.Get(b.S.Live, say.Uid);
            mine.Fields["text"] = "Bob again"; b.S.Commit(); w.Run(0.5);
            Tree.Get(a.S.Live, say.Uid).Fields["note"] = "Ann's note"; a.S.Commit(); w.Run(0.5);
            b.S.Undo(); w.Run(1.0);
            Check(Tree.Get(a.S.Live, say.Uid).Fields.ContainsKey("note") && w.Converged(), "Bob's undo does not undo Ann's work");
        }

        private static void Dragging()
        {
            var w = new World(4);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            Block say = FirstSay(a.S.Live);
            string uid = say.Uid;
            b.S.Grab(new[] { uid });
            w.Run(1.0);
            Check(a.S.HeldBy(Tree.Get(a.S.Live, uid)) == "Bob", "Ann sees that Bob is holding it: " + a.S.HeldBy(Tree.Get(a.S.Live, uid)));
            Presence bobSeenByAnn = a.S.Others.Single();
            Check(bobSeenByAnn.Grabbed.Contains(uid), "the presence says what he holds");

            // Ann tries to change it anyway: refused, and her copy is put right
            Tree.Get(a.S.Live, uid).Fields["text"] = "Ann sneaks";
            a.S.Commit();
            w.Run(1.5);
            Check(a.Messages.Any(m => m.Contains("Bob") && m.Contains("holding")), "Ann is told why: " + string.Join("|", a.Messages));
            Check(Tree.Get(a.S.Live, uid).Fields["text"] != "Ann sneaks" && w.Converged(), "the held block was not changed, and Ann's copy is right again");

            // let go: now it works
            b.S.Release();
            w.Run(1.0);
            Tree.Get(a.S.Live, uid).Fields["text"] = "Ann after";
            a.S.Commit();
            w.Run(1.0);
            Check(Tree.Get(b.S.Live, uid).Fields["text"] == "Ann after" && w.Converged(), "after the drop Ann can change it");

            // Bob grabs something that Ann holds: refused with Ann's name
            a.S.Grab(new[] { uid });
            w.Run(1.0);
            b.Messages.Clear();
            b.S.Grab(new[] { uid });
            w.Run(1.0);
            Check(b.Messages.Any(m => m.Contains("Ann")), "a grab that fails says who has it: " + string.Join("|", b.Messages));
        }

        private static void LeavingMidDrag()
        {
            var w = new World(5);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            string uid = FirstSay(a.S.Live).Uid;
            b.S.Grab(new[] { uid });
            w.Run(0.5);
            w.Server.EditorGone("p2");
            w.Eds.Remove("p2"); w.Host.Clients.Remove("p2");   // (his game is gone)
            w.Run(0.5);
            Check(w.Server.EditorsIn(File).Length == 1, "the one who left is out");
            Tree.Get(a.S.Live, uid).Fields["text"] = "free now";
            a.S.Commit();
            w.Run(1.0);
            Check(w.Converged() && Tree.Get(w.Server.ProgramOf(File), uid).Fields["text"] == "free now", "the hold went with him: converged=" + w.Converged() + ", master text=" + Tree.Get(w.Server.ProgramOf(File), uid).Fields["text"] + ", Ann's messages: " + string.Join("|", a.Messages));

            // a silent editor: the hold ends after 10 seconds, the editor after 15
            var w2 = new World(6);
            Ed c = w2.Join("host", "Ann"), d = w2.Join("p2", "Bob");
            w2.Run(1.0);
            string uid2 = FirstSay(c.S.Live).Uid;
            d.S.Grab(new[] { uid2 });
            w2.Run(0.5);
            w2.Eds.Remove("p2");   // (Bob's game stops answering: no more "still here")
            w2.Run(11.0);
            Tree.Get(c.S.Live, uid2).Fields["text"] = "after 10 s";
            c.S.Commit();
            w2.Run(1.0);
            Check(Tree.Get(w2.Server.ProgramOf(File), uid2).Fields["text"] == "after 10 s", "a hold that is not renewed ends after 10 seconds");
            w2.Run(6.0);
            Check(w2.Server.EditorsIn(File).Length == 1, "an editor that stays silent is dropped after 15 seconds");
        }

        private static void LostMessage()
        {
            var w = new World(7);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            w.Host.DropOneBatchTo = "p2";
            Tree.Get(a.S.Live, FirstSay(a.S.Live).Uid).Fields["text"] = "lost to Bob";
            a.S.Commit();
            w.Run(0.5);
            Check(!w.Converged(), "Bob missed the batch");
            w.Run(4.0);
            Check(b.S.Resyncs >= 1 && w.Converged(), "the heartbeat found it, Bob asked for a snapshot, and now they match (resyncs " + b.S.Resyncs + ")");
        }

        private static void Autosave()
        {
            var w = new World(8);
            Ed a = w.Join("host", "Ann");
            w.Run(1.0);
            int before = w.Host.Saves;
            Tree.Get(a.S.Live, FirstSay(a.S.Live).Uid).Fields["text"] = "one change";
            a.S.Commit();
            w.Run(0.6);
            Check(w.Host.Saves == before, "not saved while it is still being changed");
            w.Run(1.5);
            Check(w.Host.Saves == before + 1, "saved once, a second after the last change (" + (w.Host.Saves - before) + ")");
            w.Run(5.0);
            Check(w.Host.Saves == before + 1, "not saved again when nothing changed");
            Check(w.Host.WroteBy.Count > 0 && w.Host.WroteBy.Last() == "Ann", "the last writer is recorded");
            Check(w.Host.Audits.Any(x => x.StartsWith("co-edit: saved " + File) && x.Contains("Ann")), "the audit says who");
            Check(Program.FromJson(w.Host.Disk[File], out _).Tops[0].Chain[0].Body("do")[0].Fields["text"] == "one change", "the saved file has the change");

            // it keeps changing: saved at least every five seconds
            before = w.Host.Saves;
            for (int i = 0; i < 18; i++) { Tree.Get(a.S.Live, FirstSay(a.S.Live).Uid).Fields["text"] = "busy " + i; a.S.Commit(); w.Run(0.4); }
            int busySaves = w.Host.Saves - before;
            Check(busySaves >= 1 && busySaves <= 3, "while it keeps changing it is saved now and then, not every time (" + busySaves + " in 7 s)");

            // a save asked for
            w.Run(1.0);
            before = w.Host.Saves;
            Tree.Get(a.S.Live, FirstSay(a.S.Live).Uid).Fields["text"] = "before Run";
            a.S.Commit(); a.S.Save();
            w.Run(0.4);
            Check(w.Host.Saves == before + 1, "a save asked for (before the script runs) is done at once");
        }

        private static void Stale()
        {
            LastWriter.Clear();
            DateTime t = Net.Start;
            LastWriter.Clock = () => t;
            LastWriter.Set("x.js", "Ann");
            t = t.AddMinutes(2);
            Check(LastWriter.StaleMessage("x.js") == "Ann changed this script 2 minutes ago. Reload it first.", "the refusal names who and when: " + LastWriter.StaleMessage("x.js"));
            Check(LastWriter.StaleMessage("never.js") == "Someone changed this script. Reload it first.", "and copes when nobody is known");
            Check(Scripts.Collab.Version.Check(Scripts.Collab.Version.Hash("old text"), Scripts.Collab.Version.Hash("new text")) == SaveCheck.Stale, "a save that began from older text is stale");
            Check(Scripts.Collab.Version.Check(Scripts.Collab.Version.Hash("same\r\n"), Scripts.Collab.Version.Hash("same")) == SaveCheck.Ok, "line ends and the last newline do not count");
            Check(Scripts.Collab.Version.Check(Scripts.Collab.Version.Hash("anything"), "") == SaveCheck.Ok, "a save that does not say where it began is allowed (an older editor)");
            Check(LastWriter.Ago(5) == "just now" && LastWriter.Ago(40) == "40 seconds ago" && LastWriter.Ago(7200) == "2 hours ago", "the words for how long ago");
        }

        private static void BadMessages()
        {
            var w = new World(9);
            Ed a = w.Join("host", "Ann");
            w.Run(1.0);
            int d = w.Server.Dropped;
            w.Server.OnMessage("host", Co.Ops, "{not json");
            Check(w.Server.Dropped == d + 1, "malformed JSON is dropped and counted");
            w.Server.OnMessage("host", Co.Ops, new string('x', Co.MaxMessage + 10));
            Check(w.Server.Dropped == d + 2, "an oversized message is dropped");
            w.Server.OnMessage("host", Co.Ops, "{\"f\":\"../../evil\",\"ops\":[]}");
            Check(w.Server.Dropped == d + 3, "a script name that is not a script's is dropped");
            w.Server.OnMessage("host", "co.nonsense", "{\"f\":\"demo.js\"}");
            Check(w.Server.Dropped == d + 4, "an unknown kind of message is dropped");
            w.Server.OnMessage("host", "chat.hello", "{}");
            Check(w.Server.Dropped == d + 4, "a message that is not for co-editing is ignored without a fuss");
            w.Server.OnMessage("host", Co.Ops, "{\"f\":\"demo.js\",\"seq\":1,\"ops\":[{\"k\":\"nonsense\"},42,null,{\"k\":\"remove\",\"b\":\"zzzzzzzz\"}]}");
            w.Run(1.0);
            Check(w.Converged() && a.S.Active, "garbage ops do nothing and break nothing");

            // someone who is not in the session
            var seen = new List<string>();
            w.Host.Clients["p9"] = new CollabSession(File);
            w.Host.Names["p9"] = "Eve"; w.Host.Admins.Add("p9");
            w.Server.OnMessage("p9", Co.Ops, "{\"f\":\"demo.js\",\"seq\":1,\"ops\":[]}");
            w.Run(0.5);
            Check(w.Server.EditorsIn(File).Length == 1, "an editor who never opened the script is not let in by sending ops");

            // a flood
            int dropped = w.Server.Dropped;
            for (int i = 0; i < 100; i++) w.Server.OnMessage("host", Co.Pres, "{\"f\":\"demo.js\",\"page\":\"blocks\",\"x\":1,\"y\":2}");
            Check(w.Server.Dropped - dropped >= 85, "a flood of presence is cut to the rate limit (" + (w.Server.Dropped - dropped) + " of 100 dropped)");
            dropped = w.Server.Dropped;
            for (int i = 0; i < 60; i++) w.Server.OnMessage("host", Co.Ops, "{\"f\":\"demo.js\",\"seq\":" + (100 + i) + ",\"ops\":[]}");
            Check(w.Server.Dropped - dropped >= 35, "and so is a flood of ops (" + (w.Server.Dropped - dropped) + " of 60 dropped)");

            // an editor does not name itself: the server uses the connection
            w.Server.OnMessage("host", Co.Pres, "{\"f\":\"demo.js\",\"id\":\"p2\",\"name\":\"Eve\",\"page\":\"x\",\"x\":1,\"y\":1}");
            Check(w.Server.EditorsIn(File).SequenceEqual(new[] { "host" }), "a message cannot say it is someone else");
        }

        private static void Closing()
        {
            var w = new World(10);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            b.S.Close();
            w.Run(1.0);
            Check(w.Server.EditorsIn(File).Length == 1 && a.S.All.Count == 1, "an editor who leaves is gone for the others at once");
            Check(a.S.Others.Count() == 0, "Ann is alone");
            a.S.Live.Name = "Renamed by Ann"; a.S.Commit(); w.Run(0.5);
            a.S.Close();
            w.Run(1.0);
            Check(!w.Server.HasSession(File), "the last one out ends the session");
            Check(Program.FromJson(w.Host.Disk[File], out _).Name == "Renamed by Ann", "and the script is saved on the way out");

            // the script is removed while two edit it
            var w2 = new World(11);
            Ed c = w2.Join("host", "Ann"), d = w2.Join("p2", "Bob");
            w2.Run(1.0);
            w2.Host.Disk.Remove(File);
            w2.Run(1.0);
            Check(c.S.Status == CollabSession.State.Closed && d.S.Status == CollabSession.State.Closed && c.S.ClosedWhy.Contains("removed"), "when the script is removed, everyone is told: " + c.S.ClosedWhy);
            Check(!w2.Server.HasSession(File), "and the session is gone");

            // the server stops
            var w3 = new World(12);
            Ed e = w3.Join("host", "Ann");
            w3.Run(1.0);
            w3.Server.Shutdown("The server is stopping.");
            w3.Run(0.5);
            Check(e.S.Status == CollabSession.State.Closed && e.S.ClosedWhy.Contains("stopping"), "a stopping server says so");
        }

        private static void Presence_()
        {
            var w = new World(13);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob");
            w.Run(1.0);
            string uid = FirstSay(a.S.Live).Uid;
            b.S.SendPresence("blocks", uid, true, 120f, 340f);
            w.Run(1.0);
            Presence bob = a.S.Others.Single();
            Check(bob.Selected == uid && Math.Abs(bob.X - 120f) < 1f && Math.Abs(bob.Y - 340f) < 1f && bob.Page == "blocks", "Ann sees Bob's selection and pointer");
            // not changed: not sent again (only the keep-alive)
            int sent = w.Host.Sent;
            for (int i = 0; i < 20; i++) b.S.SendPresence("blocks", uid, true, 120f, 340f);
            w.Run(0.3);
            Check(w.Host.Sent - sent <= 2, "an unchanged presence is not sent again (" + (w.Host.Sent - sent) + ")");
            // moves faster than 4 a second are held back
            int before = w.Server.Handled;
            for (int i = 0; i < 20; i++) { b.S.SendPresence("blocks", uid, true, 200f + i * 10f, 340f); w.Run(0.05); }
            Check(w.Server.Handled - before <= 6, "pointer moves go out at most four a second (" + (w.Server.Handled - before) + " in a second)");
        }

        // ================================================================== fuzz: three editors, random edits, a slow network

        private static void Fuzz()
        {
            int seeds = 0, commits = 0, resyncs = 0;
            for (int seed = 100; seed < 130; seed++)
            {
                var w = new World(seed);
                w.Net.MaxDelay = 0.4;
                var r = new Random(seed);
                Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob"), c = w.Join("p3", "Cat");
                w.Run(1.0);
                Ed[] eds = { a, b, c };
                for (int step = 0; step < 150; step++)
                {
                    Ed e = eds[r.Next(3)];
                    if (r.Next(10) == 0 && e.S.Live.Tops.Count > 0)
                    {
                        // a drag: hold a block for a moment
                        List<Block> all = AllBlocks(e.S.Live);
                        if (all.Count > 0) { e.S.Grab(new[] { all[r.Next(all.Count)].Uid }); w.Run(0.2); e.S.Release(); }
                    }
                    else if (r.Next(12) == 0 && e.S.CanUndo) e.S.Undo();
                    else Edit(e, r);
                    commits++;
                    w.Run(r.NextDouble() * 0.4);
                }
                w.Run(8.0);
                bool ok = w.Converged();   // (messages are always in flight: keep-alives and heartbeats)
                Check(ok, "seed " + seed + ": the three copies and the master agree after the network settles");
                foreach (Ed e in eds) { resyncs += e.S.Resyncs; Check(Tree.IdsOk(e.S.Live), "seed " + seed + ": ids are fine in " + e.S.Me); }
                // the saved file is the master's
                Program disk = Program.FromJson(w.Host.Disk[File], out _);
                w.Run(2.0);
                Program saved = Program.FromJson(w.Host.Disk[File], out _);
                Check(Canon.Equal(saved, w.Server.ProgramOf(File)), "seed " + seed + ": what was saved is what the master has");
                seeds++;
            }
            Console.WriteLine("fuzz: " + seeds + " seeds, " + commits + " steps, " + resyncs + " snapshots asked for");
        }

        private static void Debug(int seed)
        {
            var w = new World(seed);
            w.Net.MaxDelay = 0.4;
            var r = new Random(seed);
            Ed a = w.Join("host", "Ann"), b = w.Join("p2", "Bob"), c = w.Join("p3", "Cat");
            w.Run(1.0);
            Ed[] eds = { a, b, c };
            for (int step = 0; step < 150; step++)
            {
                Ed e = eds[r.Next(3)];
                if (r.Next(10) == 0 && e.S.Live.Tops.Count > 0)
                {
                    List<Block> all = AllBlocks(e.S.Live);
                    if (all.Count > 0) { e.S.Grab(new[] { all[r.Next(all.Count)].Uid }); w.Run(0.2); e.S.Release(); }
                }
                else if (r.Next(12) == 0 && e.S.CanUndo) e.S.Undo();
                else Edit(e, r);
                w.Run(r.NextDouble() * 0.4);
            }
            w.Run(8.0);
            string master = Canon.Json(w.Server.ProgramOf(File));
            foreach (Ed e in eds)
            {
                string mine = Canon.Json(e.S.Live);
                Console.WriteLine(e.Key + " equal=" + (mine == master) + " pending=" + e.S.Pending + " uncommitted=" + e.S.HasUncommitted + " rev=" + e.S.Rev + " masterRev=" + w.Server.Rev(File) + " inflight=" + w.Net.InFlight);
                if (mine != master)
                {
                    int i = 0; while (i < mine.Length && i < master.Length && mine[i] == master[i]) i++;
                    Console.WriteLine("  first difference at " + i + " (mine, then master):");
                    Console.WriteLine("   " + mine.Substring(Math.Max(0, i - 120), Math.Min(260, mine.Length - Math.Max(0, i - 120))));
                    Console.WriteLine("   " + master.Substring(Math.Max(0, i - 120), Math.Min(260, master.Length - Math.Max(0, i - 120))));
                }
            }
        }

        private static int Main(string[] args)
        {
            if (args.Length > 1 && args[0] == "--debug") { Debug(int.Parse(args[1])); return 0; }
            Joining();
            IdsGivenOnFirstOpen();
            Editing();
            Dragging();
            LeavingMidDrag();
            LostMessage();
            Autosave();
            Stale();
            BadMessages();
            Closing();
            Presence_();
            Fuzz();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
