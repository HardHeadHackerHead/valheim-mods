using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    internal enum OpKind { AddTop, MoveTop, RemoveTop, Collapse, Insert, Remove, SetField, SetInput, Meta, Vars, Move }
    internal enum ApplyStatus { Applied, Adjusted, Dropped }

    /// <summary>What happened to one op: it did what it says, it did it a little differently (an anchor that was gone), or it did nothing.</summary>
    internal struct ApplyResult
    {
        internal ApplyStatus Status;
        internal string Reason;
        internal static ApplyResult Ok => new ApplyResult { Status = ApplyStatus.Applied, Reason = "" };
        internal static ApplyResult Adjust(string why) => new ApplyResult { Status = ApplyStatus.Adjusted, Reason = why };
        internal static ApplyResult Drop(string why) => new ApplyResult { Status = ApplyStatus.Dropped, Reason = why };
        internal bool Done => Status != ApplyStatus.Dropped;
    }

    /// <summary>
    /// One small change to a block program, addressed by the ids of the things it changes (docs/scripting-collab.md, "ops"). An op means the
    /// same on every copy of the program, whatever else has changed meanwhile: that is what lets people edit one script together.
    /// </summary>
    internal sealed class Op
    {
        internal OpKind Kind;
        internal string By = "";                       // the editor that made it
        internal string Top = "", Block = "", Key = "";
        internal float X, Y;
        internal bool On;
        internal bool HasAfter; internal string After;  // insert and move: after this block (none = the end, null with HasAfter = the start)
        internal string Before = "";                   // insert and move: if the block to go after is gone, before this one (else the end)
        internal string To = "";                       // move: the block whose body it goes into (or Top: a stack's own chain)
        internal List<JObject> Blocks = new List<JObject>();   // insert: the blocks, with their ids
        internal JObject TopJson;                      // addTop
        internal JObject Child;                        // setInput: the block to plug in; null = take the block out
        internal string Value;                         // setField: the new text; null = take the field away
        internal bool HasExpect; internal string Expect;   // setField: only if the field is still this (null = absent): an undo never overwrites another editor
        internal string Name = "", About = "";
        internal List<string> Vars = new List<string>();

        // ---- the ones the editor and the diff make ----
        internal static Op AddTop(JObject top) => new Op { Kind = OpKind.AddTop, TopJson = top };
        internal static Op MoveTop(string top, float x, float y) => new Op { Kind = OpKind.MoveTop, Top = top, X = x, Y = y };
        internal static Op RemoveTop(string top) => new Op { Kind = OpKind.RemoveTop, Top = top };
        internal static Op Collapse(string top, bool on) => new Op { Kind = OpKind.Collapse, Top = top, On = on };
        internal static Op InsertInTop(string top, string after, bool atStart, IEnumerable<JObject> blocks, string before = null) => new Op { Kind = OpKind.Insert, Top = top, HasAfter = atStart || after != null, After = after, Before = before ?? "", Blocks = blocks.ToList() };
        internal static Op InsertInBody(string block, string key, string after, bool atStart, IEnumerable<JObject> blocks, string before = null) => new Op { Kind = OpKind.Insert, Block = block, Key = key, HasAfter = atStart || after != null, After = after, Before = before ?? "", Blocks = blocks.ToList() };
        internal static Op Remove(string block) => new Op { Kind = OpKind.Remove, Block = block };
        /// <summary>Moves a block (with everything inside it, edits and all) to a stack's chain or a body, without making it a new block.</summary>
        internal static Op MoveToTop(string block, string top, string after, bool atStart, string before) => new Op { Kind = OpKind.Move, Block = block, Top = top, HasAfter = atStart || after != null, After = after, Before = before ?? "" };
        internal static Op MoveToBody(string block, string to, string key, string after, bool atStart, string before) => new Op { Kind = OpKind.Move, Block = block, To = to, Key = key, HasAfter = atStart || after != null, After = after, Before = before ?? "" };
        internal static Op SetField(string block, string key, string value) => new Op { Kind = OpKind.SetField, Block = block, Key = key, Value = value };
        internal static Op SetInput(string block, string key, JObject child) => new Op { Kind = OpKind.SetInput, Block = block, Key = key, Child = child };
        internal static Op Meta(string name, string about) => new Op { Kind = OpKind.Meta, Name = name ?? "", About = about ?? "" };
        internal static Op SetVars(IEnumerable<string> vars) => new Op { Kind = OpKind.Vars, Vars = vars.ToList() };

        /// <summary>The ids this op changes or puts something into: what a lock on another editor can stop.</summary>
        internal IEnumerable<string> Targets()
        {
            switch (Kind)
            {
                case OpKind.MoveTop: case OpKind.RemoveTop: case OpKind.Collapse: yield return Top; break;
                case OpKind.Insert: if (Block.Length > 0) yield return Block; else yield return Top; break;
                case OpKind.Remove: case OpKind.SetField: case OpKind.SetInput: yield return Block; break;
                case OpKind.Move: yield return Block; yield return To.Length > 0 ? To : Top; break;
            }
        }

        // ---- the wire ----

        private static readonly string[] Names = { "addTop", "moveTop", "removeTop", "collapse", "insert", "remove", "setField", "setInput", "meta", "vars", "move" };

        internal JObject ToJson()
        {
            var o = new JObject { ["k"] = Names[(int)Kind] };
            if (By.Length > 0) o["by"] = By;
            switch (Kind)
            {
                case OpKind.AddTop: o["top"] = TopJson; break;
                case OpKind.MoveTop: o["t"] = Top; o["x"] = Math.Round(X); o["y"] = Math.Round(Y); break;
                case OpKind.RemoveTop: o["t"] = Top; break;
                case OpKind.Collapse: o["t"] = Top; o["on"] = On; break;
                case OpKind.Insert:
                    if (Block.Length > 0) { o["b"] = Block; o["key"] = Key; } else o["t"] = Top;
                    if (HasAfter) o["after"] = After == null ? JValue.CreateNull() : (JToken)After;
                    if (Before.Length > 0) o["before"] = Before;
                    o["blocks"] = new JArray(Blocks.Cast<object>().ToArray());
                    break;
                case OpKind.Move:
                    o["b"] = Block;
                    if (To.Length > 0) { o["to"] = To; o["key"] = Key; } else o["t"] = Top;
                    if (HasAfter) o["after"] = After == null ? JValue.CreateNull() : (JToken)After;
                    if (Before.Length > 0) o["before"] = Before;
                    break;
                case OpKind.Remove: o["b"] = Block; break;
                case OpKind.SetField:
                    o["b"] = Block; o["key"] = Key; o["v"] = Value == null ? JValue.CreateNull() : (JToken)Value;
                    if (HasExpect) o["ex"] = Expect == null ? JValue.CreateNull() : (JToken)Expect;
                    break;
                case OpKind.SetInput: o["b"] = Block; o["key"] = Key; o["child"] = Child == null ? JValue.CreateNull() : (JToken)Child; break;
                case OpKind.Meta: o["name"] = Name; o["about"] = About; break;
                case OpKind.Vars: o["vars"] = new JArray(Vars.Cast<object>().ToArray()); break;
            }
            return o;
        }

        /// <summary>An op from the wire, or null if it is not one. Never throws.</summary>
        internal static Op FromJson(JToken j)
        {
            try
            {
                var o = j as JObject;
                if (o == null) return null;
                int kind = Array.IndexOf(Names, o.Value<string>("k"));
                if (kind < 0) return null;
                var op = new Op { Kind = (OpKind)kind, By = Str(o, "by", 40) };
                switch (op.Kind)
                {
                    case OpKind.AddTop: op.TopJson = o["top"] as JObject; if (op.TopJson == null) return null; break;
                    case OpKind.MoveTop: op.Top = Str(o, "t", 16); op.X = (float)(o.Value<double?>("x") ?? 0); op.Y = (float)(o.Value<double?>("y") ?? 0); break;
                    case OpKind.RemoveTop: op.Top = Str(o, "t", 16); break;
                    case OpKind.Collapse: op.Top = Str(o, "t", 16); op.On = o.Value<bool?>("on") ?? false; break;
                    case OpKind.Insert:
                        op.Top = Str(o, "t", 16); op.Block = Str(o, "b", 16); op.Key = Str(o, "key", 12);
                        if (o.TryGetValue("after", out JToken after)) { op.HasAfter = true; op.After = after.Type == JTokenType.Null ? null : (string)after; }
                        op.Before = Str(o, "before", 16);
                        if (o["blocks"] is JArray arr) foreach (JToken b in arr.Take(200)) if (b is JObject bo) op.Blocks.Add(bo);
                        break;
                    case OpKind.Remove: op.Block = Str(o, "b", 16); break;
                    case OpKind.Move:
                        op.Block = Str(o, "b", 16); op.Top = Str(o, "t", 16); op.To = Str(o, "to", 16); op.Key = Str(o, "key", 12);
                        if (o.TryGetValue("after", out JToken mafter)) { op.HasAfter = true; op.After = mafter.Type == JTokenType.Null ? null : (string)mafter; }
                        op.Before = Str(o, "before", 16);
                        break;
                    case OpKind.SetField:
                        op.Block = Str(o, "b", 16); op.Key = Str(o, "key", 40);
                        op.Value = o["v"] == null || o["v"].Type == JTokenType.Null ? null : Program.Clip((string)o["v"], 200);
                        if (o.TryGetValue("ex", out JToken ex)) { op.HasExpect = true; op.Expect = ex.Type == JTokenType.Null ? null : (string)ex; }
                        break;
                    case OpKind.SetInput: op.Block = Str(o, "b", 16); op.Key = Str(o, "key", 40); op.Child = o["child"] as JObject; break;
                    case OpKind.Meta: op.Name = Str(o, "name", 200); op.About = Str(o, "about", 400); break;
                    case OpKind.Vars: if (o["vars"] is JArray v) foreach (JToken t in v.Take(200)) if (t.Type == JTokenType.String) op.Vars.Add((string)t); break;
                }
                return op;
            }
            catch (Exception) { return null; }
        }

        private static string Str(JObject o, string key, int max)
        {
            JToken t = o[key];
            string s = t != null && t.Type == JTokenType.String ? (string)t : "";
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }

    internal static class Ops
    {
        /// <summary>
        /// Sweeps away stacks with no blocks. Done by everyone at the end of each batch of ops (never between two ops of one batch), so a stack
        /// that a batch empties and refills survives, and one that it empties for good goes everywhere at the same moment.
        /// </summary>
        internal static int Gc(Program p) => p.Tops.RemoveAll(t => t.Chain.Count == 0);

        /// <summary>
        /// Does one op to a program, in place. Never throws. A target that is gone, an id that is taken, a list that is full or too deep
        /// is Dropped (nothing changed); an anchor that is gone puts the blocks at the end of the list (Adjusted). The limits are the ones a
        /// saved program loads back within (Program.MaxBlocks, Tree.MaxDepth, MaxTops, MaxFields), so the master's snapshot never loses a block.
        /// </summary>
        internal static ApplyResult Apply(Program p, Op op)
        {
            try
            {
                switch (op.Kind)
                {
                    case OpKind.AddTop: return AddTop(p, op);
                    case OpKind.MoveTop: return WithTop(p, op, t => { t.X = (float)Math.Round(op.X); t.Y = (float)Math.Round(op.Y); });
                    case OpKind.RemoveTop: return RemoveTop(p, op);
                    case OpKind.Collapse: return WithTop(p, op, t => t.Collapsed = op.On);
                    case OpKind.Insert: return Insert(p, op);
                    case OpKind.Remove: return Remove(p, op);
                    case OpKind.Move: return Move(p, op);
                    case OpKind.SetField: return SetField(p, op);
                    case OpKind.SetInput: return SetInput(p, op);
                    case OpKind.Meta: p.Name = Program.Clip(op.Name ?? "", 40); p.About = Program.Clip(op.About ?? "", 200); return ApplyResult.Ok;
                    case OpKind.Vars: return SetVars(p, op);
                }
                return ApplyResult.Drop("unknown op");
            }
            catch (Exception e) { return ApplyResult.Drop("error: " + e.Message); }
        }

        private static ApplyResult WithTop(Program p, Op op, Action<Program.Top> change)
        {
            Program.Top t = Tree.FindTop(p, op.Top);
            if (t == null) return ApplyResult.Drop("that stack is gone");
            change(t);
            return ApplyResult.Ok;
        }

        private static ApplyResult RemoveTop(Program p, Op op)
        {
            Program.Top t = Tree.FindTop(p, op.Top);
            if (t == null) return ApplyResult.Drop("that stack is gone");
            p.Tops.Remove(t);
            return ApplyResult.Ok;
        }

        private static ApplyResult AddTop(Program p, Op op)
        {
            if (p.Tops.Count >= Program.MaxTops) return ApplyResult.Drop("too many stacks");
            Program.Top top = Program.TopFromJson(op.TopJson);
            if (top == null) return ApplyResult.Drop("the stack has no blocks");
            if (!Uids.Valid(top.Uid) || Tree.Has(p, top.Uid)) return ApplyResult.Drop("that stack id is taken or missing");
            string why = CheckNew(p, top.Chain, 0, 0, null);
            if (why != null) return ApplyResult.Drop(why);
            foreach (Block b in top.Chain) if (!Fresh(p, b, null, top.Uid)) return ApplyResult.Drop("a block id is taken or missing");
            p.Tops.Add(top);
            return ApplyResult.Ok;
        }

        /// <summary>Checks that new blocks fit: not too many in all, not too deep, ids present and not repeated among themselves.</summary>
        private static string CheckNew(Program p, IList<Block> blocks, int depth, int removedCount, HashSet<string> alsoFree)
        {
            int add = 0;
            var own = new HashSet<string>();
            foreach (Block root in blocks)
            {
                if (depth + Tree.SubDepth(root) > Tree.MaxDepth) return "too deep";
                foreach (Block b in root.All())
                {
                    add++;
                    if (!Uids.Valid(b.Uid) || !own.Add(b.Uid)) return "a block id is missing or repeated";
                }
            }
            if (p.CountBlocks() - removedCount + add > Program.MaxBlocks) return "too many blocks";
            return null;
        }

        /// <summary>No id in this block's subtree is already in the program (apart from `except`, a block that is being replaced), and not the stack's.</summary>
        private static bool Fresh(Program p, Block root, Block except, string alsoNot = null)
        {
            var taken = new HashSet<string>();
            foreach (Program.Top t in p.Tops)
            {
                if (t.Uid.Length > 0) taken.Add(t.Uid);
                foreach (Block r in t.Chain) foreach (Block b in r.All()) if (b.Uid.Length > 0) taken.Add(b.Uid);
            }
            if (except != null) foreach (Block b in except.All()) taken.Remove(b.Uid);
            if (alsoNot != null) taken.Add(alsoNot);
            foreach (Block b in root.All()) if (taken.Contains(b.Uid)) return false;
            return true;
        }

        private static ApplyResult Insert(Program p, Op op)
        {
            List<Block> list; int depth; Program.Top top;
            string why = Resolve(p, op, out list, out depth, out top);
            if (why != null) return ApplyResult.Drop(why);
            var blocks = new List<Block>();
            foreach (JObject j in op.Blocks) { Block b = Block.FromJson(j, 0); if (b == null) return ApplyResult.Drop("a block could not be read"); blocks.Add(b); }
            if (blocks.Count == 0) return ApplyResult.Drop("nothing to insert");
            why = CheckNew(p, blocks, depth, 0, null);
            if (why != null) return ApplyResult.Drop(why);
            foreach (Block b in blocks) if (!Fresh(p, b, null)) return ApplyResult.Drop("a block id is taken");
            // two blocks of the op may not share an id either (CheckNew checked within the op)
            ApplyResult result = Place(list, op, out int at);
            list.InsertRange(at, blocks);
            return result;
        }

        /// <summary>Where in a list an insert or a move goes: after its anchor; if that is gone, before the next one; if that is gone too, the end.</summary>
        private static ApplyResult Place(List<Block> list, Op op, out int at)
        {
            if (!op.HasAfter) { at = list.Count; return ApplyResult.Ok; }
            if (op.After == null) { at = 0; return ApplyResult.Ok; }
            int i = list.FindIndex(b => b.Uid == op.After);
            if (i >= 0) { at = i + 1; return ApplyResult.Ok; }
            if (op.Before.Length > 0)
            {
                int j = list.FindIndex(b => b.Uid == op.Before);
                if (j >= 0) { at = j; return ApplyResult.Adjust("the block to go after is gone: put before the next one"); }
            }
            at = list.Count;
            return ApplyResult.Adjust("the block to go after is gone: put at the end");
        }

        private static ApplyResult Move(Program p, Op op)
        {
            if (!Tree.Find(p, op.Block, out Loc loc)) return ApplyResult.Drop("that block is gone");
            if (loc.IsInput) return ApplyResult.Drop("a block in a socket does not move this way");
            List<Block> dest; int depth; Program.Top top;
            var probe = new Op { Kind = OpKind.Insert, Top = op.To.Length > 0 ? "" : op.Top, Block = op.To, Key = op.Key };
            string why = Resolve(p, probe, out dest, out depth, out top);
            if (why != null) return ApplyResult.Drop(why);
            Block moving = loc.Block;
            if (op.To.Length > 0 && Tree.UidsIn(moving).Contains(op.To)) return ApplyResult.Drop("a block cannot go inside itself");
            if (depth + Tree.SubDepth(moving) > Tree.MaxDepth) return ApplyResult.Drop("too deep");
            loc.List.RemoveAt(loc.Index);
            ApplyResult result = Place(dest, op, out int at);
            dest.Insert(at, moving);
            return result;
        }

        /// <summary>The list an insert goes into (a stack's chain or a body), and how deep its blocks sit.</summary>
        private static string Resolve(Program p, Op op, out List<Block> list, out int depth, out Program.Top top)
        {
            list = null; depth = 0; top = null;
            if (op.Block.Length == 0)
            {
                top = Tree.FindTop(p, op.Top);
                if (top == null) return "that stack is gone";
                list = top.Chain;
                return null;
            }
            if (!Tree.Find(p, op.Block, out Loc loc)) return "that block is gone";
            if (Array.IndexOf(Block.BodyKeys, op.Key) < 0) return "that is not a place blocks go";
            top = loc.Top;
            depth = loc.Depth + 1;
            list = loc.Block.Body(op.Key);   // (made if it was not there: an empty list is the same as none)
            return null;
        }

        private static ApplyResult Remove(Program p, Op op)
        {
            if (!Tree.Find(p, op.Block, out Loc loc)) return ApplyResult.Drop("that block is gone");
            if (loc.IsInput) { loc.Owner.Inputs.Remove(loc.Key); return ApplyResult.Ok; }
            loc.List.RemoveAt(loc.Index);   // (a stack left with no blocks stays until Gc: a batch may put blocks back in it)
            return ApplyResult.Ok;
        }

        private static ApplyResult SetField(Program p, Op op)
        {
            if (!Tree.Find(p, op.Block, out Loc loc)) return ApplyResult.Drop("that block is gone");
            if (op.Key.Length == 0 || op.Key.Length > 40) return ApplyResult.Drop("bad field name");
            Block b = loc.Block;
            if (op.HasExpect)
            {
                string cur = b.Fields.TryGetValue(op.Key, out string c) ? c : null;
                if (cur != op.Expect) return ApplyResult.Drop("someone changed it since");
            }
            if (op.Value == null) { b.Fields.Remove(op.Key); return ApplyResult.Ok; }
            if (!b.Fields.ContainsKey(op.Key) && b.Fields.Count >= Program.MaxFields) return ApplyResult.Drop("too many fields");
            b.Fields[op.Key] = Program.Clip(op.Value, 200);
            return ApplyResult.Ok;
        }

        private static ApplyResult SetInput(Program p, Op op)
        {
            if (!Tree.Find(p, op.Block, out Loc loc)) return ApplyResult.Drop("that block is gone");
            if (op.Key.Length == 0 || op.Key.Length > 40) return ApplyResult.Drop("bad socket name");
            Block owner = loc.Block;
            owner.Inputs.TryGetValue(op.Key, out Block old);
            if (op.Child == null)
            {
                if (old != null) owner.Inputs.Remove(op.Key);
                return ApplyResult.Ok;
            }
            Block child = Block.FromJson(op.Child, 0);
            if (child == null) return ApplyResult.Drop("the block could not be read");
            if (old == null && owner.Inputs.Count >= Program.MaxFields) return ApplyResult.Drop("too many sockets");
            string why = CheckNew(p, new[] { child }, loc.Depth + 1, old != null ? old.CountAll() : 0, null);
            if (why != null) return ApplyResult.Drop(why);
            if (!Fresh(p, child, old)) return ApplyResult.Drop("a block id is taken");
            owner.Inputs[op.Key] = child;
            return ApplyResult.Ok;
        }

        private static ApplyResult SetVars(Program p, Op op)
        {
            var clean = new List<string>();
            foreach (string v in op.Vars)
            {
                string s = Naming.Var(v);
                if (s != null && !clean.Contains(s) && clean.Count < Program.MaxVars) clean.Add(s);
            }
            p.Vars.Clear();
            p.Vars.AddRange(clean);
            return ApplyResult.Ok;
        }
    }
}
