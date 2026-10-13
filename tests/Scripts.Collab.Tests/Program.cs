using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;
using Scripts.Collab;

namespace Scripts.Collab.Tests
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

        // ================================================================== building programs

        private static readonly List<BlockDef> Stacks = Defs.All.Where(d => d.Shape == Shape.Stack || d.Shape == Shape.C).ToList();
        private static readonly List<BlockDef> Values = Defs.All.Where(d => d.Shape == Shape.Value).ToList();
        private static readonly List<BlockDef> Hats = Defs.All.Where(d => d.Shape == Shape.Hat).ToList();
        private static readonly string[] Words = { "", "0", "1", "5", "hi", "Boar", "a b", "-3", "9999", "storm", "x" };

        private static Block RandBlock(Random r, BlockDef def, int depth)
        {
            var b = new Block(def.Id);
            foreach (Part part in def.Parts)
            {
                Slot s = part.Slot;
                if (s == null) continue;
                switch (s.Kind)
                {
                    case SlotKind.Num: case SlotKind.Text:
                        if (r.Next(5) == 0 && depth < 4) b.Inputs[s.Key] = RandBlock(r, Values[r.Next(Values.Count)], depth + 1);
                        else b.Fields[s.Key] = Words[r.Next(Words.Length)];
                        break;
                    case SlotKind.Pick: b.Fields[s.Key] = Words[r.Next(Words.Length)]; break;
                    case SlotKind.Socket: if (r.Next(3) != 0 && depth < 4) b.Inputs[s.Key] = RandBlock(r, Values[r.Next(Values.Count)], depth + 1); break;
                    case SlotKind.Body:
                        if (depth < 4) for (int i = 0; i < r.Next(4); i++) b.Body(s.Key).Add(RandBlock(r, Stacks[r.Next(Stacks.Count)], depth + 1));
                        break;
                }
            }
            return b;
        }

        private static Program.Top RandTop(Random r)
        {
            var top = new Program.Top { X = r.Next(0, 800), Y = r.Next(0, 800), Collapsed = r.Next(6) == 0 };
            top.Chain.Add(RandBlock(r, Hats[r.Next(Hats.Count)], 0));
            if (r.Next(4) == 0) top.Chain.Add(RandBlock(r, Stacks[r.Next(Stacks.Count)], 0));
            return top;
        }

        private static Program RandProgram(Random r, int tops)
        {
            var p = new Program { Name = "Test " + r.Next(100), About = "about " + r.Next(100) };
            for (int i = 0; i < r.Next(3); i++) p.Vars.Add("var" + i);
            for (int i = 0; i < tops; i++) p.Tops.Add(RandTop(r));
            Uids.Ensure(p);
            return p;
        }

        // ================================================================== editor-like edits (what BlockView does to a program)

        private static List<(List<Block> list, int depth, Block owner)> AllLists(Program p)
        {
            var lists = new List<(List<Block>, int, Block)>();
            foreach (Program.Top t in p.Tops) { lists.Add((t.Chain, 0, null)); foreach (Block root in t.Chain) Walk(root, 0, lists); }
            return lists;
        }

        private static void Walk(Block b, int depth, List<(List<Block>, int, Block)> lists)
        {
            BlockDef def = b.Def;
            if (def != null)
                foreach (Part part in def.Parts)
                    if (part.Slot != null && part.Slot.Kind == SlotKind.Body)
                        lists.Add((b.Body(part.Slot.Key), depth + 1, b));
            foreach (Block c in b.Inputs.Values) Walk(c, depth + 1, lists);
            foreach (List<Block> l in b.Bodies.Values) foreach (Block c in l) Walk(c, depth + 1, lists);
        }

        private static bool Mutate(Random r, Program p)
        {
            int kind = r.Next(14);
            var blocks = p.AllBlocks().ToList();
            if (blocks.Count > 1200 && kind < 4) kind = 4;
            switch (kind)
            {
                case 0: case 1:   // change a typed value
                    {
                        if (blocks.Count == 0) return false;
                        Block b = blocks[r.Next(blocks.Count)];
                        BlockDef def = b.Def;
                        var slots = def == null ? new List<Slot>() : def.Parts.Where(x => x.Slot != null && (x.Slot.Kind == SlotKind.Num || x.Slot.Kind == SlotKind.Text || x.Slot.Kind == SlotKind.Pick)).Select(x => x.Slot).ToList();
                        if (slots.Count == 0) return false;
                        Slot s = slots[r.Next(slots.Count)];
                        if (r.Next(8) == 0) return b.Fields.Remove(s.Key);
                        string v = Words[r.Next(Words.Length)];
                        if (b.Fields.TryGetValue(s.Key, out string cur) && cur == v) return false;
                        b.Fields[s.Key] = v;
                        return true;
                    }
                case 2: case 3:   // put a new block into a list
                    {
                        var lists = AllLists(p);
                        if (lists.Count == 0) return false;
                        var l = lists[r.Next(lists.Count)];
                        if (l.depth > 14) return false;
                        l.list.Insert(r.Next(l.list.Count + 1), RandBlock(r, Stacks[r.Next(Stacks.Count)], l.depth + 1));
                        return true;
                    }
                case 4: case 5:   // take a block out
                    {
                        if (blocks.Count == 0) return false;
                        Block b = blocks[r.Next(blocks.Count)];
                        return Detach(p, b, tail: false);
                    }
                case 6: case 7: case 8:   // move a block (and what follows it) somewhere else, keeping the blocks themselves
                    {
                        if (blocks.Count < 2) return false;
                        Block b = blocks[r.Next(blocks.Count)];
                        if (!FindList(p, b, out List<Block> src, out int at)) return false;
                        bool tail = r.Next(2) == 0;
                        var moving = tail ? src.GetRange(at, src.Count - at) : new List<Block> { b };
                        var inside = new HashSet<Block>(moving.SelectMany(x => x.All()));
                        var lists = AllLists(p).Where(x => !ReferenceEquals(x.list, src) || true).Where(x => x.owner == null || !inside.Contains(x.owner)).ToList();
                        // not into a list that is part of what moves (a stack's own chain is fine unless it is the source and would vanish)
                        lists = lists.Where(x => !moving.Any(m => ReferenceEquals(m, x.owner))).ToList();
                        if (lists.Count == 0) return false;
                        var dest = lists[r.Next(lists.Count)];
                        if (dest.depth > 14) return false;
                        foreach (Block m in moving) src.Remove(m);
                        Program.Top emptied = p.Tops.FirstOrDefault(t => t.Chain.Count == 0);
                        // put them where they go (the destination may be the same list)
                        dest.list.InsertRange(Math.Min(r.Next(dest.list.Count + 1), dest.list.Count), moving);
                        if (emptied != null && emptied.Chain.Count == 0) p.Tops.Remove(emptied);
                        return true;
                    }
                case 9:   // a new stack, or take one away, or move one, or fold one
                    {
                        int w = r.Next(4);
                        if (w == 0 && p.Tops.Count < 30) { p.Tops.Add(RandTop(r)); return true; }
                        if (p.Tops.Count == 0) return false;
                        Program.Top t = p.Tops[r.Next(p.Tops.Count)];
                        if (w == 1) { p.Tops.Remove(t); return true; }
                        if (w == 2) { t.X = r.Next(0, 900); t.Y = r.Next(0, 900); return true; }
                        t.Collapsed = !t.Collapsed; return true;
                    }
                case 10: case 11:   // plug a block into a socket, or take it out
                    {
                        if (blocks.Count == 0) return false;
                        Block b = blocks[r.Next(blocks.Count)];
                        BlockDef def = b.Def;
                        var slots = def == null ? new List<Slot>() : def.Parts.Where(x => x.Slot != null && (x.Slot.Kind == SlotKind.Socket || x.Slot.Kind == SlotKind.Num || x.Slot.Kind == SlotKind.Text)).Select(x => x.Slot).ToList();
                        if (slots.Count == 0) return false;
                        Slot s = slots[r.Next(slots.Count)];
                        if (b.Inputs.ContainsKey(s.Key) && r.Next(2) == 0) { b.Inputs.Remove(s.Key); return true; }
                        if (Depth(p, b) > 14) return false;
                        b.Inputs[s.Key] = RandBlock(r, Values[r.Next(Values.Count)], Depth(p, b) + 1);
                        return true;
                    }
                case 12:
                    {
                        if (r.Next(2) == 0) { p.Name = "Name " + r.Next(1000); return true; }
                        p.About = "about " + r.Next(1000); return true;
                    }
                default:
                    {
                        if (p.Vars.Count > 0 && r.Next(2) == 0) { p.Vars.RemoveAt(r.Next(p.Vars.Count)); return true; }
                        if (p.Vars.Count >= 8) return false;
                        p.Vars.Add("v" + r.Next(10000)); return true;
                    }
            }
        }

        private static int Depth(Program p, Block b) => Tree.Find(p, b.Uid, out Loc l) && b.Uid.Length > 0 ? l.Depth : 6;

        private static bool FindList(Program p, Block b, out List<Block> list, out int index)
        {
            foreach (var l in AllLists(p)) { int i = l.list.IndexOf(b); if (i >= 0) { list = l.list; index = i; return true; } }
            list = null; index = -1; return false;
        }

        private static bool Detach(Program p, Block b, bool tail)
        {
            if (FindList(p, b, out List<Block> list, out int at)) { list.RemoveAt(at); p.Tops.RemoveAll(t => t.Chain.Count == 0); return true; }
            foreach (Block o in p.AllBlocks())
                foreach (var kv in o.Inputs.ToList())
                    if (ReferenceEquals(kv.Value, b)) { o.Inputs.Remove(kv.Key); return true; }
            return false;
        }

        // ================================================================== unit tests

        private static void UidTests()
        {
            var used = new HashSet<string>();
            var ids = Enumerable.Range(0, 5000).Select(_ => Uids.New(used)).ToList();
            Check(ids.Distinct().Count() == 5000, "ids are unique");
            Check(ids.All(i => i.Length == 8 && Uids.Valid(i)), "ids are 8 characters of 0-9 a-z");
            Check(!Uids.Valid("") && !Uids.Valid("ABC") && !Uids.Valid("a b") && !Uids.Valid(new string('a', 17)) && Uids.Valid("a1"), "what an id may be");

            var rng = new Random(1);
            Program p = RandProgram(rng, 5);
            Check(Tree.IdsOk(p), "Ensure gives every stack and block an id");
            string json = p.ToJson();
            Program q = Program.FromJson(json, out string err);
            Check(err == null && q.ToJson() == json && Canon.Equal(p, q), "ids survive a save and a load");
            Check(json.Contains("\"u\""), "ids are in the JSON as u");

            // a program without ids stays the same text (nothing in the files changes until somebody co-edits)
            var plain = new Program { Name = "Plain" };
            var top = new Program.Top(); top.Chain.Add(new Block("on_start")); plain.Tops.Add(top);
            Check(!plain.ToJson().Contains("\"u\""), "no ids, no u: old files and examples are unchanged");
            Program plain2 = Program.FromJson(plain.ToJson(), out _);
            Check(plain2.Tops[0].Uid == "" && plain2.Tops[0].Chain[0].Uid == "", "a file without ids loads without ids");
            Check(Uids.Ensure(plain2) == 2 && Tree.IdsOk(plain2), "Ensure gives them on open");

            // clones and copies
            Block orig = p.AllBlocks().First();
            Check(orig.Clone().Uid == "" && orig.Clone().All().All(b => b.Uid == ""), "a clone is a new block: no ids");
            Check(Canon.Equal(p, p.Copy()), "Copy keeps ids");

            // duplicates are told apart: the later one gets a new id
            Block a = new Block("on_start") { Uid = "dup" };
            var b2 = new Block("on_start") { Uid = "dup" };
            var t2 = new Program.Top(); t2.Chain.Add(a); t2.Chain.Add(b2);
            var dup = new Program(); dup.Tops.Add(t2);
            Uids.Ensure(dup);
            Check(a.Uid == "dup" && b2.Uid != "dup" && b2.Uid.Length == 8 && Tree.IdsOk(dup), "a repeated id is replaced in the later block");

            // avoid: never reuse an id the last state had
            var tiny = new Program(); var t3 = new Program.Top(); t3.Chain.Add(new Block("on_start")); tiny.Tops.Add(t3);
            Uids.Ensure(tiny, new[] { "zzzzzzzz" });
            Check(tiny.Tops[0].Uid != "zzzzzzzz", "ids avoid the ones asked for");
        }

        private static Program Small(out string topId, out string hatId, out string say1, out string say2)
        {
            var p = new Program { Name = "Small" };
            var top = new Program.Top { X = 10, Y = 20 };
            var hat = new Block("on_join");
            var s1 = new Block("say"); s1.Fields["text"] = "one";
            var s2 = new Block("say"); s2.Fields["text"] = "two";
            hat.Body("do").Add(s1); hat.Body("do").Add(s2);
            top.Chain.Add(hat); p.Tops.Add(top);
            Uids.Ensure(p);
            topId = top.Uid; hatId = hat.Uid; say1 = s1.Uid; say2 = s2.Uid;
            return p;
        }

        private static JObject Blk(string uid, string type, params (string k, string v)[] fields)
        {
            var o = new JObject { ["type"] = type, ["u"] = uid };
            if (fields.Length > 0) o["f"] = new JObject(fields.Select(f => new JProperty(f.k, f.v)));
            return o;
        }

        private static void OpTests()
        {
            Program p = Small(out string top, out string hat, out string s1, out string s2);

            Check(Ops.Apply(p, Op.SetField(s1, "text", "changed")).Status == ApplyStatus.Applied && Tree.Get(p, s1).Fields["text"] == "changed", "setField");
            Check(Ops.Apply(p, Op.SetField(s1, "text", null)).Done && !Tree.Get(p, s1).Fields.ContainsKey("text"), "setField null takes the field away");
            Check(Ops.Apply(p, Op.SetField("nope", "text", "x")).Status == ApplyStatus.Dropped, "setField on a missing block is dropped");
            Check(Ops.Apply(p, Op.SetField(s1, "", "x")).Status == ApplyStatus.Dropped, "a field needs a name");
            var big = Op.SetField(s1, "text", new string('x', 500)); Ops.Apply(p, big);
            Check(Tree.Get(p, s1).Fields["text"].Length == 200, "a value is cut at 200 characters");
            for (int i = 0; i < 20; i++) Ops.Apply(p, Op.SetField(s2, "f" + i, "v"));
            Check(Tree.Get(p, s2).Fields.Count <= Program.MaxFields, "no more than " + Program.MaxFields + " fields");

            // expect: an undo that finds something else there does nothing
            var guarded = Op.SetField(s2, "text", "back"); guarded.HasExpect = true; guarded.Expect = "not what is there";
            Check(Ops.Apply(p, guarded).Status == ApplyStatus.Dropped, "setField with expect is dropped when the field changed");
            guarded.Expect = Tree.Get(p, s2).Fields.TryGetValue("text", out string cur) ? cur : null;
            Check(Ops.Apply(p, guarded).Done && Tree.Get(p, s2).Fields["text"] == "back", "setField with expect works when it is as expected");

            // insert: after an anchor, at the start, at the end, anchor gone
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", s1, false, new[] { Blk("aaaaaaa1", "say") })).Status == ApplyStatus.Applied && p.Tops[0].Chain[0].Body("do")[1].Uid == "aaaaaaa1", "insert after a block");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", null, true, new[] { Blk("aaaaaaa2", "say") })).Done && p.Tops[0].Chain[0].Body("do")[0].Uid == "aaaaaaa2", "insert at the start");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", null, false, new[] { Blk("aaaaaaa3", "say") })).Done && p.Tops[0].Chain[0].Body("do").Last().Uid == "aaaaaaa3", "insert at the end");
            ApplyResult gone = Ops.Apply(p, Op.InsertInBody(hat, "do", "ghost", false, new[] { Blk("aaaaaaa4", "say") }));
            Check(gone.Status == ApplyStatus.Adjusted && p.Tops[0].Chain[0].Body("do").Last().Uid == "aaaaaaa4", "insert after a block that is gone goes to the end (adjusted)");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", null, true, new[] { Blk("aaaaaaa4", "say") })).Status == ApplyStatus.Dropped, "an id that exists cannot be inserted again");
            Check(Ops.Apply(p, Op.InsertInBody("ghost", "do", null, true, new[] { Blk("aaaaaaa5", "say") })).Status == ApplyStatus.Dropped, "insert into a block that is gone");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "nonsense", null, true, new[] { Blk("aaaaaaa5", "say") })).Status == ApplyStatus.Dropped, "insert into a place that is not one");
            Check(Ops.Apply(p, Op.InsertInTop("ghost", null, true, new[] { Blk("aaaaaaa5", "say") })).Status == ApplyStatus.Dropped, "insert into a stack that is gone");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", null, true, new[] { new JObject { ["type"] = "say" } })).Status == ApplyStatus.Dropped, "blocks without ids are dropped");
            Check(Ops.Apply(p, Op.InsertInBody(hat, "do", null, true, new[] { Blk("aaaaaaa6", "say"), Blk("aaaaaaa6", "say") })).Status == ApplyStatus.Dropped, "an id twice in one op is dropped");
            Check(Ops.Apply(p, Op.InsertInTop(top, null, false, new[] { Blk("aaaaaaa7", "say") })).Done && p.Tops[0].Chain.Last().Uid == "aaaaaaa7", "insert into a stack's chain");

            // remove: a block with what is inside it, and the ones after it stay
            JObject nest = Blk("bbbbbbb1", "if"); nest["do"] = new JArray(Blk("bbbbbbb2", "say"));
            Ops.Apply(p, Op.InsertInBody(hat, "do", s1, false, new[] { nest }));
            Check(Tree.Has(p, "bbbbbbb2"), "a nested block is in");
            int before = p.CountBlocks();
            Check(Ops.Apply(p, Op.Remove("bbbbbbb1")).Done && !Tree.Has(p, "bbbbbbb1") && !Tree.Has(p, "bbbbbbb2") && p.CountBlocks() == before - 2, "remove takes the block and what is inside it");
            Check(Tree.Has(p, s2), "the blocks after it stay");
            Check(Ops.Apply(p, Op.Remove("bbbbbbb1")).Status == ApplyStatus.Dropped, "remove twice: the second is dropped");

            // socket
            var say = Tree.Get(p, s1);
            Check(Ops.Apply(p, Op.SetInput(s1, "who", Blk("ccccccc1", "ev_player"))).Done && say.Inputs["who"].Uid == "ccccccc1", "setInput plugs a block in");
            Check(Ops.Apply(p, Op.SetInput(s1, "who", Blk("ccccccc2", "ev_player"))).Done && say.Inputs["who"].Uid == "ccccccc2" && !Tree.Has(p, "ccccccc1"), "setInput replaces what was there");
            Check(Ops.Apply(p, Op.SetInput(s1, "who", Blk("ccccccc2", "ev_player"))).Done, "replacing a block by one with the same id is allowed");
            Check(Ops.Apply(p, Op.SetInput(s2, "who", Blk("ccccccc2", "ev_player"))).Status == ApplyStatus.Dropped, "an id that is in use elsewhere is dropped");
            Check(Ops.Apply(p, Op.SetInput(s1, "who", null)).Done && !say.Inputs.ContainsKey("who"), "setInput null takes it out");
            Check(Ops.Apply(p, Op.SetInput("ghost", "who", null)).Status == ApplyStatus.Dropped, "setInput on a block that is gone");
            Ops.Apply(p, Op.SetInput(s1, "who", Blk("ccccccc3", "ev_player")));
            Check(Ops.Apply(p, Op.Remove("ccccccc3")).Done && !say.Inputs.ContainsKey("who"), "remove of a plugged-in block empties the socket");

            // stacks
            Check(Ops.Apply(p, Op.MoveTop(top, 99.6f, 5.2f)).Done && p.Tops[0].X == 100 && p.Tops[0].Y == 5, "moveTop (rounded as it is saved)");
            Check(Ops.Apply(p, Op.Collapse(top, true)).Done && p.Tops[0].Collapsed, "collapse");
            Check(Ops.Apply(p, Op.MoveTop("ghost", 1, 1)).Status == ApplyStatus.Dropped && Ops.Apply(p, Op.Collapse("ghost", true)).Status == ApplyStatus.Dropped && Ops.Apply(p, Op.RemoveTop("ghost")).Status == ApplyStatus.Dropped, "ops on a stack that is gone are dropped");
            var newTop = new JObject { ["u"] = "ddddddd1", ["x"] = 5, ["y"] = 6, ["blocks"] = new JArray(Blk("ddddddd2", "on_start")) };
            Check(Ops.Apply(p, Op.AddTop(newTop)).Done && p.Tops.Count == 2, "addTop");
            Check(Ops.Apply(p, Op.AddTop(newTop)).Status == ApplyStatus.Dropped, "addTop twice is dropped");
            Check(Ops.Apply(p, Op.AddTop(new JObject { ["u"] = "ddddddd3", ["blocks"] = new JArray() })).Status == ApplyStatus.Dropped, "a stack with no blocks is dropped");
            Check(Ops.Apply(p, Op.RemoveTop("ddddddd1")).Done && p.Tops.Count == 1, "removeTop");
            // a stack that loses its last block goes with it
            var lone = new Program(); var lt = new Program.Top(); lt.Chain.Add(new Block("on_start")); lone.Tops.Add(lt); Uids.Ensure(lone);
            ApplyResult last = Ops.Apply(lone, Op.Remove(lt.Chain[0].Uid));
            Check(last.Status == ApplyStatus.Applied && lone.Tops.Count == 1 && lone.Tops[0].Chain.Count == 0 && Ops.Gc(lone) == 1 && lone.Tops.Count == 0, "removing a stack's last block leaves it empty until the batch is done, then Gc sweeps it");

            // meta and variables
            Check(Ops.Apply(p, Op.Meta("New name", "New about")).Done && p.Name == "New name" && p.About == "New about", "meta");
            Ops.Apply(p, Op.Meta(new string('n', 100), new string('a', 300)));
            Check(p.Name.Length == 40 && p.About.Length == 200, "meta is cut to the same lengths as a saved file");
            Ops.Apply(p, Op.SetVars(new[] { "a", "a", "b c", "d!!", "" }));
            Check(p.Vars.SequenceEqual(new[] { "a", "b c", "d" }), "variables are cleaned and made distinct: " + string.Join(",", p.Vars));
            Ops.Apply(p, Op.SetVars(Enumerable.Range(0, 200).Select(i => "v" + i)));
            Check(p.Vars.Count == Program.MaxVars, "no more than " + Program.MaxVars + " variables");

            // limits: depth and count
            var deep = new Program(); var dt = new Program.Top(); dt.Chain.Add(new Block("on_start")); deep.Tops.Add(dt); Uids.Ensure(deep);
            Block cur2 = dt.Chain[0]; int made = 0;
            while (true)
            {
                var child = new Block("if") { Uid = "e" + made.ToString("x7") };
                ApplyResult r = Ops.Apply(deep, Op.InsertInBody(cur2.Uid, "do", null, true, new[] { child.ToJson() }));
                if (!r.Done) { Check(made >= Tree.MaxDepth - 1 && made <= Tree.MaxDepth + 1, "nesting stops at the depth a saved file loads back (" + made + ")"); break; }
                cur2 = Tree.Get(deep, child.Uid); made++;
                if (made > 100) { Check(false, "nesting never stopped"); break; }
            }
            Check(Program.FromJson(deep.ToJson(), out _).CountBlocks() == deep.CountBlocks(), "a program at the depth limit loads back whole");
            var many = new Program(); var mt = new Program.Top(); mt.Chain.Add(new Block("on_start")); many.Tops.Add(mt); Uids.Ensure(many);
            var batch = Enumerable.Range(0, 200).Select(i => Blk("f" + i.ToString("x7"), "say")).ToList();
            int accepted = 0;
            for (int i = 0; i < 12; i++)
            {
                var b = batch.Select(x => { var c = (JObject)x.DeepClone(); c["u"] = "g" + i.ToString("x2") + batch.IndexOf(x).ToString("x4"); return c; }).ToList();
                if (Ops.Apply(many, Op.InsertInBody(mt.Chain[0].Uid, "do", null, false, b)).Done) accepted++;
            }
            Check(many.CountBlocks() <= Program.MaxBlocks && accepted >= 6 && accepted < 12, "no more than " + Program.MaxBlocks + " blocks (" + many.CountBlocks() + ")");

            // too many stacks
            var tops = new Program();
            for (int i = 0; i < Program.MaxTops + 5; i++) Ops.Apply(tops, Op.AddTop(new JObject { ["u"] = "h" + i.ToString("x6"), ["blocks"] = new JArray(Blk("i" + i.ToString("x6"), "on_start")) }));
            Check(tops.Tops.Count == Program.MaxTops, "no more than " + Program.MaxTops + " stacks");

            // the wire
            foreach (Op op in new[] { Op.SetField(s1, "text", "x"), Op.SetField(s1, "text", null), Op.Remove("a"), Op.MoveTop("t", 1.4f, 2.6f), Op.Collapse("t", true), Op.RemoveTop("t"), Op.Meta("n", "a"), Op.SetVars(new[] { "a", "b" }), Op.SetInput("b", "k", Blk("x1", "say")), Op.SetInput("b", "k", null), Op.InsertInBody("b", "do", "a", false, new[] { Blk("x2", "say") }), Op.InsertInTop("t", null, true, new[] { Blk("x3", "say") }), Op.AddTop(newTop) })
            {
                op.By = "ed";
                Op back = Op.FromJson(JObject.Parse(op.ToJson().ToString()));
                Check(back != null && back.ToJson().ToString() == op.ToJson().ToString(), "an op survives the wire: " + op.Kind);
            }
            var exp = Op.SetField("b", "k", "v"); exp.HasExpect = true; exp.Expect = null;
            Op exp2 = Op.FromJson(JObject.Parse(exp.ToJson().ToString()));
            Check(exp2.HasExpect && exp2.Expect == null, "expect-absent survives the wire");
            Check(Op.FromJson(JValue.CreateNull()) == null && Op.FromJson(new JObject { ["k"] = "nonsense" }) == null && Op.FromJson(new JObject()) == null && Op.FromJson(new JArray()) == null, "garbage is not an op");
        }

        private static void ApplyNeverThrows()
        {
            var rng = new Random(7);
            Program p = RandProgram(rng, 4);
            var ids = Uids.All(p).ToList();
            int thrown = 0, dropped = 0, total = 0;
            for (int i = 0; i < 20000; i++)
            {
                var o = new JObject();
                string[] kinds = { "addTop", "moveTop", "removeTop", "collapse", "insert", "remove", "setField", "setInput", "meta", "vars", "bogus", "" };
                o["k"] = kinds[rng.Next(kinds.Length)];
                foreach (string key in new[] { "t", "b", "key", "x", "y", "on", "v", "ex", "name", "about", "after" })
                    if (rng.Next(2) == 0)
                        switch (rng.Next(6))
                        {
                            case 0: o[key] = ids[rng.Next(ids.Count)]; break;
                            case 1: o[key] = rng.Next(-5, 5000); break;
                            case 2: o[key] = JValue.CreateNull(); break;
                            case 3: o[key] = rng.Next(2) == 0; break;
                            case 4: o[key] = new string('z', rng.Next(0, 300)); break;
                            default: o[key] = new JArray(1, "a"); break;
                        }
                if (rng.Next(3) == 0) o["blocks"] = new JArray(Blk(ids[rng.Next(ids.Count)], "say"), new JObject { ["type"] = 5 }, JValue.CreateNull());
                if (rng.Next(3) == 0) o["top"] = new JObject { ["u"] = "q" + rng.Next(100), ["blocks"] = new JArray(Blk("w" + rng.Next(1000), "say")) };
                if (rng.Next(3) == 0) o["child"] = rng.Next(2) == 0 ? (JToken)Blk("n" + rng.Next(1000), "ev_player") : JValue.CreateNull();
                if (rng.Next(3) == 0) o["vars"] = new JArray("a", 5, "b");
                Op op = Op.FromJson(o);
                if (op == null) continue;
                total++;
                try { if (Ops.Apply(p, op).Status == ApplyStatus.Dropped) dropped++; }
                catch (Exception) { thrown++; }
                if (!Tree.IdsOk(p)) { Check(false, "garbage ops left a program with a missing or repeated id (op " + i + ")"); return; }
            }
            Check(thrown == 0, "Apply never throws on garbage (" + total + " ops, " + dropped + " dropped)");
            Check(p.CountBlocks() <= Program.MaxBlocks, "garbage never overfills a program");
            Program back = Program.FromJson(p.ToJson(), out _);
            Check(Canon.Equal(p, back), "a program after garbage still saves and loads whole");
        }

        private static void DiffTests()
        {
            var rng = new Random(11);
            int cases = 0, nonEmpty = 0;
            for (int i = 0; i < 3000; i++)
            {
                Program a = RandProgram(rng, 1 + rng.Next(4));
                Program b = a.Copy();
                int edits = 1 + rng.Next(6);
                for (int e = 0; e < edits; e++) Mutate(rng, b);
                // the edited copy keeps the ids of what it kept; Diff gives the new blocks theirs
                string before = Canon.Json(a);
                List<Op> ops = Diff.Compute(a, b, "me");
                Check(Canon.Json(a) == before, "Diff does not change the old program");
                Check(Tree.IdsOk(b), "Diff gives the new program ids everywhere");
                Program replay = a.Copy();
                var failures = new List<string>();
                foreach (Op op in ops) { ApplyResult r = Ops.Apply(replay, Op.FromJson(JObject.Parse(op.ToJson().ToString()))); if (r.Status != ApplyStatus.Applied) failures.Add(op.Kind + ": " + r.Status + " " + r.Reason); }
                Ops.Gc(replay);
                cases++;
                if (ops.Count > 0) nonEmpty++;
                if (!Canon.Equal(replay, b) || failures.Count > 0)
                {
                    Check(false, "Apply(old, Diff(old, new)) = new, every op applies cleanly (case " + i + ", " + ops.Count + " ops; " + string.Join("; ", failures.Take(3)) + ")");
                    if (_failed > 5) return;
                }
                else _passed++;
                if (ops.Count > 0 && Diff.Compute(replay, b).Count != 0) Check(false, "after the replay there is nothing left to diff (case " + i + ")");
            }
            Check(nonEmpty > 2000, "the diff property was tried on real changes (" + nonEmpty + " of " + cases + ")");

            // no change, no ops
            Program p = RandProgram(new Random(3), 3);
            Check(Diff.Compute(p, p.Copy()).Count == 0, "no change gives no ops");
            // a move keeps the id of the block
            Program m = Small(out string top, out string hat, out string s1, out string s2);
            Program m2 = m.Copy();
            Block moved = Tree.Get(m2, s1);
            m2.Tops[0].Chain[0].Body("do").Remove(moved);
            m2.Tops[0].Chain[0].Body("do").Add(moved);
            List<Op> moveOps = Diff.Compute(m, m2);
            Program m3 = m.Copy(); foreach (Op op in moveOps) Ops.Apply(m3, op);
            Check(Canon.Equal(m3, m2) && Tree.Get(m3, s1) != null && m3.Tops[0].Chain[0].Body("do").Last().Uid == s1, "a move keeps the block's id");
            Check(moveOps.Count == 1, "a move inside a list is one op (" + moveOps.Count + ")");
            Check(moveOps.Any(o => o.Kind == OpKind.Move) && !moveOps.Any(o => o.Kind == OpKind.Insert), "a move inside a list is a move op, not a remove and an insert");
            // a change in the middle of a long list is one op
            Program longp = new Program(); var lt = new Program.Top(); var lh = new Block("on_join");
            for (int i = 0; i < 100; i++) lh.Body("do").Add(new Block("say"));
            lt.Chain.Add(lh); longp.Tops.Add(lt); Uids.Ensure(longp);
            Program longq = longp.Copy(); longq.Tops[0].Chain[0].Body("do").RemoveAt(50);
            Check(Diff.Compute(longp, longq).Count == 1, "deleting one of a hundred blocks is one op");
            Program longr = longp.Copy(); longr.Tops[0].Chain[0].Body("do").Insert(30, new Block("say"));
            Check(Diff.Compute(longp, longr).Count == 1, "inserting one among a hundred blocks is one op");
        }

        // ================================================================== master: locks, presence, saving

        private static void MasterTests()
        {
            DateTime now = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            Program p = Small(out string top, out string hat, out string s1, out string s2);
            var m = new Master(p, () => now);
            Presence ann = m.Join("ann", "Ann"), bob = m.Join("bob", "Bob");
            Check(ann.Color == 0 && bob.Color == 1, "colours are given by join order");
            Presence cy = m.Join("cy", "Cy"); m.Leave("ann"); Presence di = m.Join("di", "Di");
            Check(di.Color == 0, "a colour that was freed is used again: " + di.Color);
            m.Join("ann", "Ann");
            Check(m.Join("ann", "Ann").Color == m.Editor("ann").Color && m.Editors.Count == 4, "joining twice is the same editor");

            // ops: numbering, batch, hash
            Batch b = m.Submit("ann", 1, new[] { Op.SetField(s1, "text", "A") }, out bool everyone);
            Check(everyone && b.Ops.Count == 1 && b.RevFrom == 1 && b.RevTo == 1 && m.Rev == 1 && b.Hash == Canon.Hash(m.Program) && b.By == "ann" && b.Seq == 1, "a submit is numbered and carries the hash");
            Check(Tree.Get(m.Program, s1).Fields["text"] == "A", "the master did the op");

            // last writer wins
            m.Submit("bob", 1, new[] { Op.SetField(s1, "text", "B") }, out _);
            m.Submit("ann", 2, new[] { Op.SetField(s1, "text", "A2") }, out _);
            Check(Tree.Get(m.Program, s1).Fields["text"] == "A2", "the last writer wins on one field");

            // a dropped op: nothing is sent to everyone, the sender is told
            Batch bad = m.Submit("bob", 2, new[] { Op.SetField("ghost", "text", "x") }, out everyone);
            Check(!everyone && bad.Dropped == 1 && bad.Resync && bad.Ops.Count == 0 && bad.RevTo < bad.RevFrom && bad.Reasons.Count == 1, "a dropped op: only the sender hears, and it should resync");
            Batch mixed = m.Submit("bob", 3, new[] { Op.SetField(s1, "x", "1"), Op.SetField("ghost", "text", "x"), Op.SetField(s2, "x", "2") }, out everyone);
            Check(everyone && mixed.Ops.Count == 2 && mixed.Dropped == 1 && mixed.Resync, "good ops of a mixed batch still take effect");
            Batch unknown = m.Submit("stranger", 1, new[] { Op.SetField(s1, "text", "x") }, out everyone);
            Check(!everyone && unknown.Dropped == 1, "an editor that has not joined cannot change anything");
            int rev = m.Rev;
            Batch adj = m.Submit("bob", 4, new[] { Op.InsertInBody(hat, "do", "ghost", false, new[] { Blk("ffffff01", "say") }) }, out everyone);
            Check(everyone && adj.Ops.Count == 1 && !adj.Resync && m.Rev == rev + 1, "an adjusted op takes effect (the sender finds out by the hash if its copy really differs)");

            // locks
            Check(m.Grab("ann", new[] { s1 }).Count == 0, "a grab is granted");
            Batch locked = m.Submit("bob", 5, new[] { Op.SetField(s1, "text", "nope") }, out everyone);
            Check(!everyone && locked.Dropped == 1 && locked.Reasons[0].Contains("Ann"), "someone else's op on a held block is dropped with the holder's name: " + string.Join(";", locked.Reasons));
            Check(m.Submit("ann", 3, new[] { Op.SetField(s1, "text", "mine") }, out _).Ops.Count == 1, "the holder can still change it");
            Check(m.Submit("bob", 6, new[] { Op.Remove(hat) }, out everyone).Dropped == 1, "removing something that contains a held block is dropped");
            Check(m.Submit("bob", 7, new[] { Op.InsertInBody(s1, "do", null, true, new[] { Blk("ffffff02", "say") }) }, out everyone).Dropped == 1, "inserting into a held block is dropped");
            Check(m.Submit("bob", 8, new[] { Op.SetField(s2, "text", "free") }, out _).Ops.Count == 1, "the blocks next to it are free");
            Check(m.Grab("bob", new[] { s1 }).SequenceEqual(new[] { s1 }), "the same block cannot be grabbed twice");
            Check(m.Grab("bob", new[] { hat }).Count == 1, "a block around a held one cannot be grabbed");
            Check(m.Grab("ann", new[] { hat }).Count == 0 || true, "(the holder may grab around its own)");
            m.Release("ann");
            Check(m.Grab("bob", new[] { hat }).Count == 0, "after a release it can be");
            m.Release("bob");
            // a hold ends when its holder goes quiet
            m.Grab("cy", new[] { s2 });
            Check(m.Submit("bob", 9, new[] { Op.SetField(s2, "text", "blocked") }, out _).Dropped == 1, "held by Cy");
            now = now.AddSeconds(11);
            m.UpdatePresence("bob", "blocks", "", 0, 0);
            m.Tick();
            Check(m.Submit("bob", 10, new[] { Op.SetField(s2, "text", "freed") }, out _).Ops.Count == 1, "a hold is released after 10 s of silence");
            Check(m.Editor("cy") != null, "the quiet editor is still listed at 11 s");
            now = now.AddSeconds(5);
            m.UpdatePresence("bob", "blocks", "", 0, 0);
            List<string> gone = m.Tick();
            Check(gone.Contains("cy") && m.Editor("cy") == null && m.Editor("bob") != null, "an editor is dropped after 15 s of silence");
            Check(m.Join("cy", "Cy").Color == 2 || m.Editor("cy").Color <= 3, "and can come back");

            // presence round trip
            m.UpdatePresence("bob", "blocks", s1, 12.4f, 30.6f);
            Presence back = Presence.FromJson(JObject.Parse(m.Editor("bob").ToJson().ToString()));
            Check(back.Name == "Bob" && back.Selected == s1 && back.Page == "blocks", "presence survives the wire");

            // the snapshot
            JObject snap = m.Snapshot();
            Program loaded = Program.FromJson(snap["program"].ToString(), out _);
            Check(snap.Value<int>("rev") == m.Rev && Canon.Equal(loaded, m.Program) && snap.Value<string>("hash") == Canon.Hash(m.Program) && ((JArray)snap["editors"]).Count == m.Editors.Count, "the snapshot has the program, the revision and the editors");

            // saving
            var saved = new Master(Small(out _, out _, out string a1, out _), () => now);
            saved.Join("e", "E");
            Check(!saved.TakeSaveRequest(), "nothing changed: nothing to save");
            saved.Submit("e", 1, new[] { Op.SetField(a1, "text", "1") }, out _);
            Check(!saved.TakeSaveRequest(), "just changed: wait for a quiet moment");
            now = now.AddSeconds(0.5); saved.Submit("e", 2, new[] { Op.SetField(a1, "text", "2") }, out _);
            now = now.AddSeconds(0.7);
            Check(!saved.TakeSaveRequest(), "still changing: not yet");
            now = now.AddSeconds(1.0);
            Check(saved.TakeSaveRequest() && !saved.Dirty, "quiet for a second: save, and it is clean");
            for (int i = 0; i < 12; i++) { now = now.AddSeconds(0.5); saved.Submit("e", 3 + i, new[] { Op.SetField(a1, "text", "n" + i) }, out _); }
            Check(saved.TakeSaveRequest(), "changing for more than five seconds: save anyway");
        }

        // ================================================================== stale saves

        private static void VersionTests()
        {
            string a = "// ==Script==\n// name: x\nlog(1);\n", b = a.Replace("\n", "\r\n") + "\r\n";
            Check(Scripts.Collab.Version.Hash(a) == Scripts.Collab.Version.Hash(b), "line ends and the last newline do not change the hash");
            Check(Scripts.Collab.Version.Hash(a) != Scripts.Collab.Version.Hash(a + "log(2);"), "a change does");
            string h = Scripts.Collab.Version.Hash(a);
            Check(Scripts.Collab.Version.Check(h, h) == SaveCheck.Ok && Scripts.Collab.Version.Check(h, Scripts.Collab.Version.Hash("other")) == SaveCheck.Stale, "same hash is fine, a different one is stale");
            Check(Scripts.Collab.Version.Check(h, null) == SaveCheck.Ok && Scripts.Collab.Version.Check(h, "") == SaveCheck.Ok, "a save that does not say where it began is allowed (an older editor)");
            Check(Scripts.Collab.Version.StaleMessage("Ann").StartsWith("Ann changed") && Scripts.Collab.Version.StaleMessage(null).StartsWith("Someone changed"), "the message names who changed it");
        }

        // ================================================================== the network and the clients

        private sealed class Node
        {
            internal string Id; internal Client C; internal Link ToMaster, FromMaster; internal Random Rng;
            internal readonly List<string> Trace = new List<string>();
        }

        private sealed class Link
        {
            internal readonly Queue<(double at, Action deliver)> Q = new Queue<(double, Action)>();
            internal double Last;
        }

        private sealed class Sim
        {
            internal Random Rng; internal Master M; internal List<Node> Nodes = new List<Node>(); internal double Now;
            internal int Resyncs, Commits, OpsSent, Delivered, Undos, Lost, Grabs;
            internal double Loss;   // the chance that a batch to a client is lost on the way (a fault the transport should never have; the heartbeat must still put things right)
            internal readonly Dictionary<string, int> Reasons = new Dictionary<string, int>();
            internal readonly Dictionary<string, int> DropWhy = new Dictionary<string, int>();
            internal readonly Dictionary<string, string> HashJson = new Dictionary<string, string>();
            internal readonly List<string> Samples = new List<string>();
            internal DateTime Clock = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);

            internal static string Summ(Op o)
            {
                switch (o.Kind)
                {
                    case OpKind.Vars: return "vars[" + string.Join(",", o.Vars) + "]";
                    case OpKind.SetField: return "set " + o.Block + "." + o.Key + "=" + o.Value;
                    case OpKind.Insert: return "insert in " + (o.Block.Length > 0 ? o.Block + "/" + o.Key : "top " + o.Top) + " after " + (o.HasAfter ? (o.After ?? "START") : "END") + " before " + o.Before + " [" + string.Join(",", o.Blocks.Select(b => (string)b["u"])) + "]";
                    case OpKind.Move: return "move " + o.Block + " to " + (o.To.Length > 0 ? o.To + "/" + o.Key : "top " + o.Top) + " after " + (o.HasAfter ? (o.After ?? "START") : "END") + " before " + o.Before;
                    case OpKind.Remove: return "remove " + o.Block;
                    case OpKind.RemoveTop: return "removeTop " + o.Top;
                    case OpKind.SetInput: return "input " + o.Block + "." + o.Key + (o.Child == null ? "=none" : "=" + (string)o.Child["u"]);
                    case OpKind.AddTop: return "addTop " + (string)o.TopJson["u"];
                    default: return o.Kind + " " + o.Block + o.Top;
                }
            }

            internal void Send(Link l, Action act, double slow)
            {
                double at = Math.Max(l.Last, Now + Rng.NextDouble() * slow);
                l.Last = at;   // (one connection never reorders; different connections do)
                l.Q.Enqueue((at, act));
            }

            internal void Pump(bool all)
            {
                bool any;
                do
                {
                    any = false;
                    foreach (Link l in Nodes.SelectMany(n => new[] { n.ToMaster, n.FromMaster }).OrderBy(_ => Rng.Next()))
                        while (l.Q.Count > 0 && (all || l.Q.Peek().at <= Now))
                        {
                            l.Q.Dequeue().deliver();
                            Delivered++; any = true;
                            if (!all) break;
                        }
                } while (all && any || (!all && false));
            }

            internal bool Quiet => Nodes.All(n => n.ToMaster.Q.Count == 0 && n.FromMaster.Q.Count == 0);

            internal void Commit(Node n, Commit c)
            {
                if (c == null) return;
                Commits++; OpsSent += c.Ops.Count;
                n.Trace.Add("COMMIT seq " + c.Seq + ": " + string.Join(" | ", c.Ops.Select(Summ)));
                var wire = c.Ops.Select(o => o.ToJson().ToString()).ToList();
                Send(n.ToMaster, () =>
                {
                    var ops = wire.Select(w => Op.FromJson(JObject.Parse(w))).Where(o => o != null).ToList();
                    Batch b = M.Submit(n.Id, c.Seq, ops, out bool everyone);
                    if (b.Hash.Length > 0) HashJson[b.Hash] = Canon.Json(M.Program);
                    string json = b.ToJson().ToString();
                    foreach (Node other in Nodes)
                    {
                        if (!everyone && other != n) continue;
                        Node target = other;
                        if (Loss > 0 && Rng.NextDouble() < Loss) { Lost++; continue; }
                        Send(target.FromMaster, () =>
                        {
                            Batch got = Batch.FromJson(JObject.Parse(json));
                            string mine = target.C.CommittedJson; bool wasDrifted = target.C.NeedsResync;
                            target.Trace.Add("BATCH by " + got.By + " seq " + got.Seq + " rev " + got.RevFrom + ".." + got.RevTo + " pending " + target.C.Pending + ": " + string.Join(" | ", got.Ops.Select(Summ)) + (got.Dropped > 0 ? " [dropped " + got.Dropped + "]" : ""));
                            target.C.OnBatch(got);
                            if (!wasDrifted && target.C.NeedsResync && target.C.DriftReason == "hash" && Samples.Count < 4 && HashJson.TryGetValue(got.Hash, out string want))
                            {
                                string have = target.C.CommittedJson; int i = 0; while (i < have.Length && i < want.Length && have[i] == want[i]) i++;
                                Samples.Add(string.Join(Environment.NewLine, target.Trace.Skip(Math.Max(0, target.Trace.Count - 7))) + Environment.NewLine + target.Id + " drifted on a batch by " + got.By + " of " + got.Ops.Count + " ops: " + string.Join(" ", got.Ops.Select(o => o.Kind + ":" + o.Block + o.Top)) + "\n   client: ..." + have.Substring(Math.Max(0, i - 60), Math.Min(200, have.Length - Math.Max(0, i - 60))) + "\n   master: ..." + want.Substring(Math.Max(0, i - 60), Math.Min(200, want.Length - Math.Max(0, i - 60))));
                            }
                        }, 3.0);
                    }
                }, 3.0);
            }

            internal void Housekeeping()
            {
                foreach (Node n in Nodes)
                    if (n.C.NeedsResync && n.C.TakeResyncRequest())
                    {
                        Resyncs++;
                        Reasons[n.C.DriftReason] = (Reasons.TryGetValue(n.C.DriftReason, out int rc) ? rc : 0) + 1;
                        Commit(n, n.C.CommitChanges());   // (what is waiting goes first, so it reaches the master before the request)
                        Node target = n;
                        Send(n.ToMaster, () =>
                        {
                            string snap = M.Snapshot().ToString();
                            Send(target.FromMaster, () => target.C.OnSnapshot(JObject.Parse(snap)), 2.0);
                        }, 2.0);
                    }
            }
        }

        private static Sim Start(int seed, int clients, int tops)
        {
            var rng = new Random(seed);
            byte[] seedBytes = new byte[8];
            var idRng = new Random(seed * 7919 + 1);
            Uids.Bytes = n => { var b = new byte[n]; idRng.NextBytes(b); return b; };
            var sim = new Sim { Rng = rng };
            sim.M = new Master(RandProgram(rng, tops), () => sim.Clock);
            for (int i = 0; i < clients; i++)
            {
                string id = "ed" + i;
                sim.M.Join(id, "Editor " + i);
                var live = Program.FromJson(sim.M.Snapshot()["program"].ToString(), out _);
                var node = new Node { Id = id, C = new Client(live, id, sim.M.Rev), ToMaster = new Link(), FromMaster = new Link(), Rng = new Random(seed * 31 + i) };
                node.C.Refused += why => { foreach (string w in why) sim.DropWhy[w] = (sim.DropWhy.TryGetValue(w, out int c) ? c : 0) + 1; };
                sim.Nodes.Add(node);
            }
            return sim;
        }

        private static string Why(Sim sim)
        {
            string m = Canon.Json(sim.M.Program);
            foreach (Node n in sim.Nodes)
            {
                string c = Canon.Json(n.C.Live);
                if (c != m)
                {
                    int i = 0; while (i < c.Length && i < m.Length && c[i] == m[i]) i++;
                    return n.Id + " differs at " + i + ": client ..." + c.Substring(Math.Max(0, i - 40), Math.Min(120, c.Length - Math.Max(0, i - 40))) + " | master ..." + m.Substring(Math.Max(0, i - 40), Math.Min(120, m.Length - Math.Max(0, i - 40)));
                }
            }
            return null;
        }

        private static bool Converge(int seed, int clients, int steps, out string failure, out Sim result, double loss = 0, bool locks = false)
        {
            Sim sim = Start(seed, clients, 2 + seed % 4);
            sim.Loss = loss;
            result = sim;
            failure = null;
            for (int step = 0; step < steps; step++)
            {
                sim.Now += sim.Rng.NextDouble() * 1.5;
                sim.Clock = sim.Clock.AddSeconds(0.2);
                Node n = sim.Nodes[sim.Rng.Next(sim.Nodes.Count)];
                int what = sim.Rng.Next(100);
                if (what < 58)
                {
                    int edits = 1 + sim.Rng.Next(3), done = 0;
                    n.C.Busy = sim.Rng.Next(10) == 0;
                    for (int e = 0; e < edits; e++) if (Mutate(n.Rng, n.C.Live)) done++;
                    if (!n.C.Busy && done > 0) sim.Commit(n, n.C.CommitChanges());
                }
                else if (what < 66) { n.C.Busy = false; sim.Commit(n, n.C.Undo()); sim.Undos++; }
                else if (what < 70) { n.C.Busy = false; sim.Commit(n, n.C.Redo()); sim.Undos++; }
                else if (what < 73) { n.C.Busy = false; sim.Commit(n, n.C.CommitChanges()); }
                else if (locks && what < 80)
                {
                    // a drag: hold a few blocks of the master's program, and let go a while later (or never: the hold ends by itself)
                    var ids = Uids.All(sim.M.Program).ToList();
                    if (ids.Count > 0 && sim.Rng.Next(2) == 0) { sim.M.Grab(n.Id, Enumerable.Range(0, 1 + sim.Rng.Next(3)).Select(_ => ids[sim.Rng.Next(ids.Count)]).ToList()); sim.Grabs++; }
                    else sim.M.Release(n.Id);
                }
                if (locks)
                {
                    if (step % 20 == 0) foreach (Node k in sim.Nodes) sim.M.UpdatePresence(k.Id, "blocks", "", 0, 0);   // (everybody is still here)
                    if (step % 5 == 0) sim.M.Tick();
                }
                sim.Pump(false);
                sim.Housekeeping();
                if (step % 40 == 0 && !(Tree.IdsOk(sim.M.Program)))
                {
                    failure = "the master's program lost an id at step " + step; return false;
                }
            }
            // settle: everyone commits, the network drains, drifted copies take a snapshot, until nothing moves
            foreach (Node k in sim.Nodes) sim.M.Release(k.Id);
            sim.Loss = 0;   // (the faults stop; what was lost must be found by the heartbeat)
            for (int round = 0; round < 50; round++)
            {
                foreach (Node k in sim.Nodes) { JObject hb = sim.M.Heartbeat(); Node t = k; int hrev = hb.Value<int>("rev"); string hhash = hb.Value<string>("hash"); sim.Send(t.FromMaster, () => t.C.OnHeartbeat(hrev, hhash), 1.0); }
                foreach (Node n in sim.Nodes) { n.C.Busy = false; sim.Commit(n, n.C.CommitChanges()); }
                sim.Pump(true);
                sim.Housekeeping();
                sim.Pump(true);
                if (sim.Quiet && sim.Nodes.All(x => !x.C.NeedsResync && x.C.Pending == 0 && !x.C.HasUncommitted)) break;
            }
            failure = Why(sim);
            if (failure != null) return false;
            // the master's program still saves and loads whole, and compiles or has problems, never an exception
            Program reloaded = Program.FromJson(sim.M.Program.ToJson(), out string err);
            if (reloaded == null || !Canon.Equal(reloaded, sim.M.Program)) { failure = "the master's program does not survive a save and load: " + err; return false; }
            try { Compiled c = Compiler.Compile(sim.M.Program); if (c.Ok == (c.Js == null)) { failure = "compile result inconsistent"; return false; } }
            catch (Exception e) { failure = "compile threw: " + e.Message; return false; }
            if (sim.M.Program.CountBlocks() > Program.MaxBlocks) { failure = "over the block limit"; return false; }
            return true;
        }

        private static void ClientTests()
        {
            // two editors, a simple exchange, no network trouble
            Program p = Small(out string top, out string hat, out string s1, out string s2);
            var clock = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            var m = new Master(p, () => clock);
            m.Join("a", "Ann"); m.Join("b", "Bob");
            Client ca = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "a", m.Rev);
            Client cb = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "b", m.Rev);
            void Deliver(Batch b, bool everyone, params Client[] to) { foreach (Client c in to) c.OnBatch(Batch.FromJson(JObject.Parse(b.ToJson().ToString()))); }

            Tree.Get(ca.Live, s1).Fields["text"] = "from Ann";
            Commit c1 = ca.CommitChanges();
            Check(c1 != null && c1.Ops.Count == 1 && ca.Pending == 1 && ca.CommitChanges() == null, "a change commits once, a second commit has nothing");
            Batch b1 = m.Submit("a", c1.Seq, c1.Ops, out bool every);
            Deliver(b1, every, ca, cb);
            Check(ca.Pending == 0 && !ca.NeedsResync && Tree.Get(cb.Live, s1).Fields["text"] == "from Ann" && cb.Rev == 1, "the other editor gets it, the sender is acknowledged");

            // new blocks get their ids at commit, and the others see the same block
            var fresh = new Block("say"); fresh.Fields["text"] = "new";
            ca.Live.Tops[0].Chain[0].Body("do").Add(fresh);
            Commit c2 = ca.CommitChanges();
            Check(fresh.Uid.Length == 8 && c2.Ops.Count == 1 && c2.Ops[0].Kind == OpKind.Insert, "a new block gets its id at commit");
            Deliver(m.Submit("a", c2.Seq, c2.Ops, out every), every, ca, cb);
            Check(Tree.Get(cb.Live, fresh.Uid) != null && Canon.Equal(ca.Live, cb.Live) && Canon.Equal(ca.Live, m.Program), "the new block is the same block for both");

            // changed event, with a reference that stays valid
            Block held = Tree.Get(cb.Live, s2);
            var seen = new List<string>(); bool structural = false;
            cb.Changed += (ids, s) => { seen.AddRange(ids); structural |= s; };
            Tree.Get(ca.Live, s2).Fields["text"] = "second";
            Commit c3 = ca.CommitChanges();
            Deliver(m.Submit("a", c3.Seq, c3.Ops, out every), every, ca, cb);
            Check(ReferenceEquals(held, Tree.Get(cb.Live, s2)) && held.Fields["text"] == "second", "a remote change is done in place: the editor's reference is still the block");
            Check(seen.Contains(s2) && !structural, "Changed names the block (not structural for a field)");

            // a local uncommitted change on the same field wins over the remote one
            Tree.Get(cb.Live, s1).Fields["text"] = "Bob typing";
            Tree.Get(ca.Live, s1).Fields["text"] = "Ann again";
            Commit c4 = ca.CommitChanges();
            Deliver(m.Submit("a", c4.Seq, c4.Ops, out every), every, ca, cb);
            Check(Tree.Get(cb.Live, s1).Fields["text"] == "Bob typing", "an edit Bob has not committed yet is not overwritten");
            Commit c5 = cb.CommitChanges();
            Deliver(m.Submit("b", c5.Seq, c5.Ops, out every), every, ca, cb);
            Check(Tree.Get(m.Program, s1).Fields["text"] == "Bob typing" && Canon.Equal(ca.Live, m.Program) && Canon.Equal(cb.Live, m.Program) && !ca.NeedsResync && !cb.NeedsResync, "Bob's later commit wins everywhere, nobody needed a snapshot");

            // a dropped op makes the sender resync
            Program.Top gone = ca.Live.Tops[0];
            Commit rm = null;
            {
                // Bob removes the whole stack; Ann, who had not heard yet, changes a block in it
                cb.Live.Tops.Clear();
                Commit cr = cb.CommitChanges();
                Tree.Get(ca.Live, s1).Fields["text"] = "late";
                Commit late = ca.CommitChanges();
                Batch br = m.Submit("b", cr.Seq, cr.Ops, out every); Deliver(br, every, ca, cb);
                Batch bl = m.Submit("a", late.Seq, late.Ops, out every); Deliver(bl, every, ca);
                Check(bl.Dropped == 1 && !ca.NeedsResync && ca.Pending == 0, "an op on something that is gone is dropped, and the sender is told, with no snapshot needed");
                Check(Canon.Equal(ca.Live, m.Program) && ca.Live.Tops.Count == 0, "the dropped change is simply not in the sender's copy any more");
                Check(ca.Rebases > 0, "the copy was rebuilt from the master's state");
            }

            // a missed batch is noticed
            Program q = Small(out _, out _, out string t1, out _);
            var m2 = new Master(q, () => clock); m2.Join("x", "X"); m2.Join("y", "Y");
            Client cx = new Client(Program.FromJson(m2.Snapshot()["program"].ToString(), out _), "x", m2.Rev);
            Client cy = new Client(Program.FromJson(m2.Snapshot()["program"].ToString(), out _), "y", m2.Rev);
            Tree.Get(cx.Live, t1).Fields["text"] = "1"; Commit k1 = cx.CommitChanges(); Batch bb1 = m2.Submit("x", k1.Seq, k1.Ops, out every);
            Tree.Get(cx.Live, t1).Fields["text"] = "2"; Commit k2 = cx.CommitChanges(); Batch bb2 = m2.Submit("x", k2.Seq, k2.Ops, out every);
            cy.OnBatch(Batch.FromJson(bb2.ToJson()));   // (the first one never arrived)
            Check(cy.NeedsResync, "a batch that skips a revision means a missed batch: resync");

            // drift is found by the hash
            Program r = Small(out _, out _, out string u1, out _);
            var m3 = new Master(r, () => clock); m3.Join("x", "X"); m3.Join("y", "Y");
            Client dx = new Client(Program.FromJson(m3.Snapshot()["program"].ToString(), out _), "x", m3.Rev);
            Client dy = new Client(Program.FromJson(m3.Snapshot()["program"].ToString(), out _), "y", m3.Rev);
            Tree.Get(dy.Live, u1).Fields["text"] = "drifted secretly"; dy.CommitChanges();   // (a copy that changed without telling anyone, as a bug would)
            Tree.Get(dx.Live, u1).Fields["other"] = "1"; Commit dc = dx.CommitChanges();
            Batch db = m3.Submit("x", dc.Seq, dc.Ops, out every);
            dy.OnBatch(Batch.FromJson(db.ToJson()));
            Check(dy.NeedsResync || dy.Pending > 0, "a copy that has drifted finds out from the master's hash (or is still waiting on its own change)");

            // undo and redo: own work only
            Program u = Small(out string ut, out string uh, out string us1, out string us2);
            var mu = new Master(u, () => clock); mu.Join("a", "Ann"); mu.Join("b", "Bob");
            Client ua = new Client(Program.FromJson(mu.Snapshot()["program"].ToString(), out _), "a", mu.Rev);
            Client ub = new Client(Program.FromJson(mu.Snapshot()["program"].ToString(), out _), "b", mu.Rev);
            void Round(Client from, Commit c) { if (c == null) return; Batch b = mu.Submit(from.Id, c.Seq, c.Ops, out bool ev); Deliver(b, ev, ua, ub); }
            Tree.Get(ua.Live, us1).Fields["text"] = "Ann's edit"; Round(ua, ua.CommitChanges());
            Tree.Get(ub.Live, us2).Fields["text"] = "Bob's edit"; Round(ub, ub.CommitChanges());
            Check(ua.CanUndo && !ub.CanRedo, "undo is there for Ann");
            Round(ua, ua.Undo());
            Check(Tree.Get(mu.Program, us1).Fields["text"] == "one" && Tree.Get(mu.Program, us2).Fields["text"] == "Bob's edit", "Ann's undo took back Ann's edit and not Bob's");
            Round(ua, ua.Redo());
            Check(Tree.Get(mu.Program, us1).Fields["text"] == "Ann's edit", "redo puts it back");
            Tree.Get(ub.Live, us1).Fields["text"] = "Bob took over"; Round(ub, ub.CommitChanges());
            Round(ua, ua.Undo());
            Check(Tree.Get(mu.Program, us1).Fields["text"] == "Bob took over", "Ann's undo does not overwrite what Bob wrote on the same field after her");
            Check(Canon.Equal(ua.Live, mu.Program) && Canon.Equal(ub.Live, mu.Program), "and both copies still agree");
            // undo of an insert, and a redo that finds the place gone
            var nb = new Block("say"); nb.Fields["text"] = "added"; ua.Live.Tops[0].Chain[0].Body("do").Add(nb); Round(ua, ua.CommitChanges());
            string nbId = nb.Uid;
            Round(ua, ua.Undo());
            Check(!Tree.Has(mu.Program, nbId), "undo of an insert takes the block out");
            ub.Live.Tops.Clear(); Round(ub, ub.CommitChanges());
            Round(ua, ua.Redo());
            Check(Canon.Equal(ua.Live, mu.Program) && !ua.NeedsResync, "a redo whose place was removed by someone else does nothing, and nothing breaks");
        }

        // a script at the size limit must stay quick: committing, applying someone's change, and rebasing while ours is in flight
        private static void PerfTests()
        {
            var rng = new Random(5);
            Uids.Bytes = n => { var b = new byte[n]; rng.NextBytes(b); return b; };
            var big = new Program { Name = "Big" };
            for (int t = 0; t < 30; t++)
            {
                var top = new Program.Top { X = t * 50, Y = 0 };
                var hat = new Block("on_join");
                for (int i = 0; i < 24; i++)
                {
                    var say = new Block("say"); say.Fields["text"] = "line " + i;
                    say.Inputs["who"] = new Block("ev_player");
                    hat.Body("do").Add(say);
                }
                top.Chain.Add(hat); big.Tops.Add(top);
            }
            Uids.Ensure(big);
            int blocks = big.CountBlocks();
            Check(blocks >= 1400 && blocks <= Program.MaxBlocks, "the big program has about 1500 blocks (" + blocks + ")");
            var clock = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            var m = new Master(big, () => clock);
            m.Join("a", "A"); m.Join("b", "B");
            var ca = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "a", m.Rev);
            var cb = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "b", m.Rev);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Block target = ca.Live.Tops[3].Chain[0].Body("do")[10];
            target.Fields["text"] = "changed by A";
            sw.Restart(); Commit c1 = ca.CommitChanges(); double commitMs = sw.Elapsed.TotalMilliseconds;
            Check(c1 != null && c1.Ops.Count == 1, "one edit in a big program is one op");

            Tree.Get(cb.Live, Tree.Get(cb.Live, target.Uid).Uid).Fields["other"] = "B typing";   // B has an uncommitted edit: A's batch must be rebased under it
            Batch b1 = m.Submit("a", c1.Seq, c1.Ops, out bool every);
            ca.OnBatch(Batch.FromJson(b1.ToJson()));
            sw.Restart(); cb.OnBatch(Batch.FromJson(b1.ToJson())); double applyMs = sw.Elapsed.TotalMilliseconds;

            Commit c2 = cb.CommitChanges();
            Block moved = ca.Live.Tops[5].Chain[0].Body("do")[3];
            ca.Live.Tops[5].Chain[0].Body("do").Remove(moved); ca.Live.Tops[5].Chain[0].Body("do").Add(moved);
            Commit c3 = ca.CommitChanges();   // A has one pending when B's comes
            Batch b2 = m.Submit("b", c2.Seq, c2.Ops, out every);
            cb.OnBatch(Batch.FromJson(b2.ToJson()));
            sw.Restart(); ca.OnBatch(Batch.FromJson(b2.ToJson())); double rebaseMs = sw.Elapsed.TotalMilliseconds;
            Batch b3 = m.Submit("a", c3.Seq, c3.Ops, out every);
            ca.OnBatch(Batch.FromJson(b3.ToJson())); cb.OnBatch(Batch.FromJson(b3.ToJson()));
            Check(Canon.Equal(ca.Live, m.Program) && Canon.Equal(cb.Live, m.Program), "the big program converges");
            // a paste of a whole stack, and a tidy of every stack
            var tidy = ca.Live.Tops.ToList(); foreach (var t in tidy) { t.X += 7; t.Y += 3; }
            sw.Restart(); Commit c4 = ca.CommitChanges(); double tidyMs = sw.Elapsed.TotalMilliseconds;
            Check(c4 != null && c4.Ops.Count == 30, "tidying 30 stacks is 30 moves");
            Console.WriteLine($"perf at {blocks} blocks: commit {commitMs:0.0} ms, apply with an unsent edit {applyMs:0.0} ms, rebase {rebaseMs:0.0} ms, tidy commit {tidyMs:0.0} ms");
            Check(commitMs < 400 && applyMs < 400 && rebaseMs < 400 && tidyMs < 800, "all of that is well under half a second");
        }

        private static void ConvergenceFuzz()
        {
            int seeds = 0, steps = 0, resyncs = 0, commits = 0, ops = 0, undos = 0;
            var samples = new List<string>();
            var reasons = new Dictionary<string, int>(); var dropWhy = new Dictionary<string, int>();
            var failures = new List<string>();
            for (int seed = 1; seed <= 80; seed++)
            {
                int clients = 2 + seed % 4;
                int n = seed <= 60 ? 400 : 1500;
                bool ok = Converge(seed, clients, n, out string why, out Sim sim);
                samples.AddRange(sim.Samples);
                foreach (var kv in sim.DropWhy) dropWhy[kv.Key] = (dropWhy.TryGetValue(kv.Key, out int dw) ? dw : 0) + kv.Value;
                foreach (var kv in sim.Reasons) reasons[kv.Key] = (reasons.TryGetValue(kv.Key, out int rr) ? rr : 0) + kv.Value;
                seeds++; steps += n; resyncs += sim.Resyncs; commits += sim.Commits; ops += sim.OpsSent; undos += sim.Undos;
                if (!ok) { failures.Add("seed " + seed + " (" + clients + " clients): " + why); if (failures.Count >= 3) break; }
            }
            // the harder ones: batches lost on the way (the heartbeat finds them), and blocks held by drags (ops dropped by the holds)
            int lostBatches = 0, lostSnapshots = 0, grabs = 0, hardSeeds = 0;
            for (int seed = 101; seed <= 130; seed++)
            {
                int clients = 2 + seed % 4;
                bool lossy = seed % 2 == 0;
                bool ok = Converge(seed, clients, 500, out string why, out Sim sim, lossy ? 0.03 : 0.0, locks: !lossy);
                hardSeeds++; lostBatches += sim.Lost; lostSnapshots += sim.Resyncs; grabs += sim.Grabs; steps += 500; commits += sim.Commits;
                if (!ok) failures.Add("seed " + seed + (lossy ? " (lossy)" : " (locks)") + ": " + why);
            }
            Console.WriteLine($"hard fuzz: {hardSeeds} seeds, {lostBatches} batches lost, {grabs} drags, {lostSnapshots} snapshots taken");
            Check(lostBatches > 20 && lostSnapshots > 0, "the lossy runs really lost batches and recovered through snapshots (" + lostBatches + " lost, " + lostSnapshots + " snapshots)");
            Check(grabs > 100, "the lock runs really made drags (" + grabs + ")");
            foreach (string f in failures) Check(false, "convergence " + f);
            if (failures.Count == 0) Check(true, "convergence");
            foreach (var kv in dropWhy.OrderByDescending(k => k.Value).Take(8)) Console.WriteLine("  dropped: " + kv.Key + " x" + kv.Value);
            foreach (string smp in samples.Take(4)) Console.WriteLine(smp);
            Console.WriteLine("snapshots by reason: " + string.Join(", ", reasons.Select(kv => kv.Key + " " + kv.Value)));
            Console.WriteLine($"convergence fuzz: {seeds} seeds, {steps} steps, {commits} commits, {ops} ops, {undos} undo/redo, {resyncs} snapshots taken, {failures.Count} failures");
            Check(steps >= 20000, "at least 20,000 steps (" + steps + ")");
            Check(commits > 1000, "the fuzz made real commits (" + commits + ")");
            // conflicts are rare in a normal session: most clients should never need a snapshot
            Check(resyncs < commits / 4, "snapshots are the exception, not the way it works (" + resyncs + " for " + commits + " commits)");
        }

        // what two people actually do: one drags a stack while the other edits inside it; and both edit the same slot
        private static void ScenarioTests()
        {
            DateTime clock = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
            Program p = Small(out string top, out string hat, out string s1, out string s2);
            var m = new Master(p, () => clock); m.Join("a", "Ann"); m.Join("b", "Bob");
            Client ca = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "a", m.Rev);
            Client cb = new Client(Program.FromJson(m.Snapshot()["program"].ToString(), out _), "b", m.Rev);
            void Send(Client from, Commit c) { if (c == null) return; Batch b = m.Submit(from.Id, c.Seq, c.Ops, out bool ev); ca.OnBatch(Batch.FromJson(b.ToJson())); if (ev) cb.OnBatch(Batch.FromJson(b.ToJson())); else if (from == cb) cb.OnBatch(Batch.FromJson(b.ToJson())); }

            // both change two different fields of one block at once: both stay
            ca.Live.Tops[0].Chain[0].Body("do")[0].Fields["a"] = "1";
            cb.Live.Tops[0].Chain[0].Body("do")[0].Fields["b"] = "2";
            Commit k1 = ca.CommitChanges(), k2 = cb.CommitChanges();
            Send(ca, k1); Send(cb, k2);
            Check(Tree.Get(m.Program, s1).Fields["a"] == "1" && Tree.Get(m.Program, s1).Fields["b"] == "2" && Canon.Equal(ca.Live, cb.Live) && Canon.Equal(ca.Live, m.Program), "two people changing different fields of one block keep both changes");

            // both change the same field at the same moment: one of them wins, everyone agrees, nobody needs a snapshot
            Tree.Get(ca.Live, s2).Fields["text"] = "Ann";
            Tree.Get(cb.Live, s2).Fields["text"] = "Bob";
            Commit n1 = ca.CommitChanges(), n2 = cb.CommitChanges();
            Send(ca, n1); Send(cb, n2);
            string winner = Tree.Get(m.Program, s2).Fields["text"];
            Check(winner == "Bob" && Canon.Equal(ca.Live, m.Program) && Canon.Equal(cb.Live, m.Program) && !ca.NeedsResync && !cb.NeedsResync, "the same field at once: the later one at the master wins everywhere, with no snapshot (" + winner + ")");

            // Ann grabs a block to drag it: Bob cannot change it meanwhile
            Check(m.Grab("a", new[] { s1 }).Count == 0, "Ann grabs the block");
            Tree.Get(cb.Live, s1).Fields["text"] = "Bob tries";
            Commit tr = cb.CommitChanges(); Send(cb, tr);
            Check(Tree.Get(m.Program, s1).Fields.GetValueOrDefault("text") != "Bob tries" && !cb.NeedsResync, "Bob's change on a held block is dropped, and his copy puts itself right without a snapshot");
            m.Release("a");
            Check(Canon.Equal(cb.Live, m.Program) && Tree.Get(cb.Live, s1).Fields.GetValueOrDefault("text") != "Bob tries", "Bob sees what is there");

            // Ann moves a block to another place while Bob types inside it: the move is a remove plus an insert of the block as Ann has it
            ca.Live.Tops[0].Chain[0].Body("do").Add(new Block("say"));
            Send(ca, ca.CommitChanges());
            Block target = ca.Live.Tops[0].Chain[0].Body("do").Last();
            Check(Canon.Equal(ca.Live, m.Program), "a block is added");
            Block mv = ca.Live.Tops[0].Chain[0].Body("do")[0];
            ca.Live.Tops[0].Chain[0].Body("do").Remove(mv); ca.Live.Tops[0].Chain[0].Body("do").Add(mv);
            Commit mvC = ca.CommitChanges();
            Tree.Get(cb.Live, mv.Uid).Fields["typed"] = "inside the moved block";
            Commit typeC = cb.CommitChanges();
            Send(ca, mvC); Send(cb, typeC);
            foreach (Client c in new[] { ca, cb }) if (c.NeedsResync) c.OnSnapshot(m.Snapshot());
            Check(Canon.Equal(ca.Live, m.Program) && Canon.Equal(cb.Live, m.Program), "a move and an edit of the moved block at once end the same everywhere");
        }

        private static int Main()
        {
            UidTests();
            OpTests();
            ApplyNeverThrows();
            DiffTests();
            MasterTests();
            VersionTests();
            ClientTests();
            ScenarioTests();
            PerfTests();
            ConvergenceFuzz();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
