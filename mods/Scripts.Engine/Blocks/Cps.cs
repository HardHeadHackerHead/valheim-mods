using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts.Blocks
{
    /// <summary>
    /// Waiting, loops, my-blocks and the rest of the compiler's control flow. A script can't pause the game, and the scripting engine's own
    /// async/await would stop it (waiting on something that only the game's clock can finish), so a wait is compiled into plain code that
    /// carries on later: everything after a wait goes into a function that the host's own timer (after) calls. A loop that waits keeps the
    /// same shape (a loop helper that asks for the next turn when the body is done), and a my-block that waits takes a "what next"
    /// function as its last input. Nothing here needs more from the engine than ordinary functions.
    /// </summary>
    internal sealed partial class Gen
    {
        private sealed class Frame { internal bool Cps; internal string Done = "", Next = ""; }

        private readonly List<Frame> _frames = new List<Frame>();               // the loops around the block being made, innermost last
        private readonly List<HashSet<string>> _scopes = new List<HashSet<string>>();   // the local variables made so far, one set for each body we are inside
        private readonly Dictionary<string, Block> _procs = new Dictionary<string, Block>();
        private readonly HashSet<string> _asyncProcs = new HashSet<string>();  // my-blocks that wait (they take "what next" as a last input)
        private readonly Dictionary<Block, bool> _asyncMemo = new Dictionary<Block, bool>();
        private string _proc;                                                    // the my-block being made, or null
        private HashSet<string> _procParams;
        private bool _procAsync;

        // ================================================================ my blocks

        private void CollectProcedures()
        {
            var seen = new HashSet<Block>();
            foreach (Program.Top top in Program.Tops)
            {
                if (top.Chain.Count == 0 || top.Chain[0].Type != "define") continue;
                Block b = top.Chain[0];
                seen.Add(b);
                string name = Naming.Word(b.Field("name"));
                if (name == null) { Error(b, "Give this my-block a name (letters, digits and spaces)."); continue; }
                if (_procs.ContainsKey(name)) { Error(b, "There are two my-blocks called '" + name + "'."); continue; }
                _procs[name] = b;
            }
            foreach (Block b in Program.AllBlocks())
                if (b.Type == "define" && !seen.Contains(b)) Error(b, "A 'define' block goes on its own on the workspace, not inside another block.");
        }

        /// <summary>The names of the inputs a my-block was given, in order (blank ones are left out).</summary>
        private List<string> ParamNames(Block define)
        {
            var list = new List<string>();
            foreach (string key in new[] { "p1", "p2", "p3", "p4" })
            {
                string w = Naming.Word(define.Field(key));
                if (w == null) continue;
                if (list.Contains(w)) { Error(define, "Two inputs are called '" + w + "'."); continue; }
                list.Add(w);
            }
            return list;
        }

        private void EmitProc(Block define)
        {
            string name = Naming.Word(define.Field("name"));
            if (name == null || !_procs.TryGetValue(name, out Block owner) || owner != define) return;
            List<string> ps = ParamNames(define);
            bool async = _asyncProcs.Contains(name);
            string sig = string.Join(", ", ps.Select(Naming.ParamId).ToArray()) + (async ? (ps.Count > 0 ? ", " : "") + "_k" : "");
            BeginTop("define");
            _proc = name; _procParams = new HashSet<string>(ps); _procAsync = async;
            Open(define, "function " + Naming.ProcId(name) + "(" + sig + ") {");
            if (async) Line(null, "_k = _k || _bNoop;");
            if (define.Bodies.TryGetValue("do", out List<Block> body)) SeqScoped(body, async ? "_k" : null);
            else if (async) Line(null, "_k();");
            Close("}");
            _proc = null; _procParams = null; _procAsync = false;
        }

        /// <summary>The my-block a call names (it must exist), or null with a problem.</summary>
        internal Block ProcOf(Block call, string key)
        {
            string name = Naming.Word(call.Field(key));
            if (name == null || !_procs.TryGetValue(name, out Block define)) { Error(call, name == null ? "Pick the my-block to use." : "There is no my-block called '" + name + "'."); return null; }
            return define;
        }

        /// <summary>The arguments of a call, in the order the my-block declared its inputs.</summary>
        internal string CallArgs(Block call, Block define, string k)
        {
            List<string> ps = ParamNames(define);
            var args = new List<string>();
            for (int i = 0; i < ps.Count; i++) args.Add(Ex(call, "a" + (i + 1)));
            if (k != null && _asyncProcs.Contains(Naming.Word(define.Field("name")))) args.Add(k);
            return string.Join(", ", args.ToArray());
        }

        internal bool IsAsyncCall(Block define) => define != null && _asyncProcs.Contains(Naming.Word(define.Field("name")));

        internal string ProcId(Block define) => Naming.ProcId(Naming.Word(define.Field("name")));

        /// <summary>The identifier of an input of the my-block being made.</summary>
        internal string ParamRef(Block b, string key)
        {
            string name = Naming.Word(b.Field(key));
            if (_procParams == null || name == null || !_procParams.Contains(name)) { Error(b, "This input belongs to a my-block: use it inside that 'define' block."); return "undefined"; }
            return Naming.ParamId(name);
        }

        internal bool InProc => _proc != null;
        internal bool InAsyncProc => _procAsync;

        // ================================================================ local variables, signals, commands

        internal string LocalDeclare(Block b, string key)
        {
            string name = Naming.Word(b.Field(key));
            if (name == null) { Error(b, "Name the local variable (letters, digits and spaces)."); return "_bBad"; }
            if (_scopes.Count == 0) _scopes.Add(new HashSet<string>());
            if (!_scopes[_scopes.Count - 1].Add(name)) Error(b, "A local variable called '" + name + "' is already made in this part.");
            return Naming.LocalId(name);
        }

        /// <summary>A local variable can be read below where it was made, in the same part or any part inside it (like the "let" it becomes).</summary>
        internal string LocalRef(Block b, string key)
        {
            string name = Naming.Word(b.Field(key));
            if (name == null || !_scopes.Any(set => set.Contains(name))) { Error(b, "Make this local variable first, above this block, in the same part (or one around it)."); return "undefined"; }
            return Naming.LocalId(name);
        }

        /// <summary>Blocks in a part of their own: the local variables made in it stay in it.</summary>
        internal void SeqScoped(List<Block> list, string k)
        {
            _scopes.Add(new HashSet<string>());
            try { Seq(list, k); }
            finally { _scopes.RemoveAt(_scopes.Count - 1); }
        }

        internal string SignalName(Block b, string key)
        {
            string name = Naming.Word(b.Field(key));
            if (name == null) { Error(b, "Name the signal (letters, digits and spaces)."); return "\"\""; }
            return Str(name);
        }

        /// <summary>A chat command's name: letters and digits, lower case, 20 at most (what the host accepts).</summary>
        internal string CommandName(Block b, string key)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in b.Field(key).TrimStart('/')) if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            string name = sb.Length > 20 ? sb.ToString(0, 20) : sb.ToString();
            if (name.Length == 0) { Error(b, "Name the command with letters and digits."); return "\"x\""; }
            return Str(name);
        }

        // ================================================================ loops: break, continue, stop

        internal void PushFrame(bool cps, string done, string next) => _frames.Add(new Frame { Cps = cps, Done = done, Next = next });
        internal void PopFrame() { if (_frames.Count > 0) _frames.RemoveAt(_frames.Count - 1); }

        internal string BreakCode(Block b)
        {
            if (_frames.Count == 0) { Error(b, "'break' only works inside a loop."); return "return;"; }
            Frame f = _frames[_frames.Count - 1];
            return f.Cps ? f.Done + "(); return;" : "break;";
        }

        internal string ContinueCode(Block b)
        {
            if (_frames.Count == 0) { Error(b, "'next turn' only works inside a loop."); return "return;"; }
            Frame f = _frames[_frames.Count - 1];
            return f.Cps ? f.Next + "(); return;" : "continue;";
        }

        /// <summary>"Stop here": ends the handler, or a my-block that waits (which must still say it is done).</summary>
        internal string StopCode() => _procAsync ? "_k(); return;" : "return;";

        /// <summary>A function boundary (an after block's inside): the loops outside it can't be broken from in there.</summary>
        internal void Closure(Action body)
        {
            var saved = _frames.ToList();
            _frames.Clear();
            try { body(); }
            finally { _frames.Clear(); _frames.AddRange(saved); }
        }

        // ================================================================ which blocks wait

        private void ComputeAsync()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                _asyncMemo.Clear();
                foreach (KeyValuePair<string, Block> kv in _procs)
                {
                    if (_asyncProcs.Contains(kv.Key)) continue;
                    if (kv.Value.Bodies.TryGetValue("do", out List<Block> body) && body.Any(IsAsync)) { _asyncProcs.Add(kv.Key); changed = true; }
                }
            }
            _asyncMemo.Clear();
        }

        /// <summary>Whether running this block can pause: a wait, a my-block that waits, or a block with one inside (not past an "after", which has a function of its own).</summary>
        internal bool IsAsync(Block b)
        {
            if (_asyncMemo.TryGetValue(b, out bool known)) return known;
            bool result = false;
            BlockDef def = b.Def;
            if (def != null)
            {
                if (def.Id == "wait") result = true;
                else if (def.Id == "call_proc") { string name = Naming.Word(b.Field("name")); result = name != null && _asyncProcs.Contains(name); }
                else if (def.Id != "after" && def.Shape != Shape.Hat)
                    foreach (List<Block> list in b.Bodies.Values)
                        if (list.Any(IsAsync)) { result = true; break; }
            }
            _asyncMemo[b] = result;
            return result;
        }

        private Block FirstWait(Block hat)
        {
            foreach (List<Block> list in hat.Bodies.Values)
                foreach (Block c in list)
                    if (IsAsync(c)) return c.Def != null && (c.Def.Id == "wait" || c.Def.Id == "call_proc") ? c : (FirstWait(c) ?? c);
            return null;
        }

        // ================================================================ a list of blocks, in order, with what comes next

        /// <summary>
        /// Blocks one after another. k: what to call when they are done (null: nothing follows, as in a handler). The first block that
        /// waits splits the list: what follows it goes into a function that the wait (or loop, or branch) calls when it finishes.
        /// </summary>
        internal void Seq(List<Block> list, string k)
        {
            bool ended = false;
            for (int i = 0; i < list.Count; i++)
            {
                Block child = list[i];
                BlockDef def = child.Def;
                if (def != null && def.Shape == Shape.Hat) { Error(child, "A 'when' block can't go inside another block."); continue; }
                if (ended) Warn(child, "Nothing after 'stop here', 'break' or 'next turn' can run.");
                if (def != null && def.Shape != Shape.Value && IsAsync(child))
                {
                    string next = k;
                    if (i + 1 < list.Count)
                    {
                        next = Fresh("k");
                        Open(null, "const " + next + " = () => {");
                        Seq(list.GetRange(i + 1, list.Count - i - 1), k);
                        Close("};");
                    }
                    EmitAsync(child, next);
                    return;
                }
                Visit(child);
                if (def != null && def.Terminal) ended = true;
            }
            if (k != null) Line(null, k + "();");
        }

        private static readonly HashSet<string> LoopIds = new HashSet<string> { "repeat", "repeat_until", "while", "for_range", "for_player", "for_creature", "for_each_item" };

        private void EmitAsync(Block b, string k)
        {
            BlockDef def = b.Def;
            if (def.Needs != null) _needs.Add(def.Needs);
            if (_depth > Program.MaxDepth) { Error(b, "Blocks are nested too deep."); return; }
            _depth++;
            try
            {
                if (def.Id == "wait") { if (k != null) Line(b, "_bAfter(" + Ex(b, "n") + ", " + k + ");"); }
                else if (def.Id == "if" || def.Id == "if_else" || def.Id == "if_chain") EmitIfFamily(b, k);
                else if (LoopIds.Contains(def.Id)) EmitLoop(b, true, k);
                else if (def.Id == "call_proc")
                {
                    Block define = ProcOf(b, "name");
                    if (define != null) Line(b, ProcId(define) + "(" + CallArgs(b, define, k) + ");");
                }
                else Error(b, "'" + def.Label.Replace("{do:body}", "") + "' can't have a wait inside it.");
            }
            catch (Exception e) { Error(b, "Could not make code from this block: " + e.Message); }
            _depth--;
        }

        /// <summary>if, if/otherwise and the chain. k is null for the ordinary form; for a branch that waits, every branch ends by calling k.</summary>
        internal void EmitIfFamily(Block b, string k)
        {
            string id = b.Def.Id;
            var branches = new List<KeyValuePair<string, string>>();
            if (id == "if_chain")
            {
                branches.Add(new KeyValuePair<string, string>(Ex(b, "c1"), "do"));
                if (Has(b, "c2")) branches.Add(new KeyValuePair<string, string>(Ex(b, "c2"), "do2"));
                if (Has(b, "c3")) branches.Add(new KeyValuePair<string, string>(Ex(b, "c3"), "do3"));
            }
            else branches.Add(new KeyValuePair<string, string>(Ex(b, "cond"), "do"));
            bool hasElse = id != "if";
            for (int i = 0; i < branches.Count; i++)
            {
                if (i == 0) Open(b, "if (" + branches[i].Key + ") {");
                else Close("} else if (" + branches[i].Key + ") {", true);
                SeqScoped(b.Bodies.TryGetValue(branches[i].Value, out List<Block> body) ? body : new List<Block>(), k);
            }
            if (hasElse)
            {
                Close("} else {", true);
                SeqScoped(b.Bodies.TryGetValue("else", out List<Block> els) ? els : new List<Block>(), k);
            }
            else if (k != null)
            {
                Close("} else {", true);
                Line(null, k + "();");
            }
            Close("}");
        }

        // ================================================================ loops

        private sealed class LoopSpec
        {
            internal string[] Init = new string[0], Each = new string[0];
            internal string Cond = "false", VarKind, VarName;
        }

        /// <summary>
        /// A loop in either shape. Ordinary: a while loop with its set-up before it. One that waits: the loop helper asks the body for each
        /// turn and the body says when it is done. Break and next turn follow whichever shape is in use. A loop runs 1000 turns at most.
        /// </summary>
        internal void EmitLoop(Block b, bool cps, string k)
        {
            LoopSpec s = SpecOf(b);
            if (s == null) return;
            if (!cps)
            {
                Open(b, "{");
                foreach (string line in s.Init) Line(b, line);
                Open(b, "while (" + s.Cond + ") {");
                foreach (string line in s.Each) Line(b, line);
                PushFrame(false, "", "");
                if (s.VarKind != null) PushLoop(s.VarKind, s.VarName);
                Body(b, "do");
                if (s.VarKind != null) PopLoop(s.VarKind);
                PopFrame();
                Close("}");
                Close("}");
                return;
            }
            string done = Fresh("d"), next = Fresh("x");
            Open(b, "{");
            foreach (string line in s.Init) Line(b, line);
            Line(b, "const " + done + " = " + (k ?? "_bNoop") + ";");
            Open(b, "_bLoop(() => " + s.Cond + ", (" + next + ") => {");
            foreach (string line in s.Each) Line(b, line);
            PushFrame(true, done, next);
            if (s.VarKind != null) PushLoop(s.VarKind, s.VarName);
            SeqScoped(b.Bodies.TryGetValue("do", out List<Block> body) ? body : new List<Block>(), next);
            if (s.VarKind != null) PopLoop(s.VarKind);
            PopFrame();
            Close("}, " + done + ");");
            Close("}");
        }

        private LoopSpec SpecOf(Block b)
        {
            string i = Fresh("i"), n = Fresh("n"), a = Fresh("a");
            switch (b.Def.Id)
            {
                case "repeat":
                    return new LoopSpec { Init = new[] { "let " + i + " = 0;", "const " + n + " = Math.min(1000, _bNum(" + Ex(b, "n") + "));" }, Cond = i + " < " + n, Each = new[] { i + "++;" } };
                case "repeat_until":
                    return new LoopSpec { Init = new[] { "let " + n + " = 0;" }, Cond = "(" + n + " < 1000 && !(" + Ex(b, "cond") + "))", Each = new[] { n + "++;" } };
                case "while":
                    return new LoopSpec { Init = new[] { "let " + n + " = 0;" }, Cond = "(" + n + " < 1000 && (" + Ex(b, "cond") + "))", Each = new[] { n + "++;" } };
                case "for_range":
                    {
                        string e = Fresh("e");
                        return new LoopSpec
                        {
                            Init = new[] { "let " + i + " = _bNum(" + Ex(b, "a") + ");", "const " + e + " = _bNum(" + Ex(b, "b") + ");", "let " + n + " = 0;" },
                            Cond = "(" + i + " <= " + e + " && " + n + " < 1000)",
                            Each = new[] { VarId(b, "v") + " = " + i + ";", i + "++;", n + "++;" },
                        };
                    }
                case "for_player":
                    {
                        string p = Fresh("p");
                        return new LoopSpec { Init = new[] { "const " + a + " = _bArr(players());", "let " + i + " = 0;" }, Cond = i + " < " + a + ".length", Each = new[] { "const " + p + " = " + a + "[" + i + "++];" }, VarKind = "player", VarName = p };
                    }
                case "for_creature":
                    {
                        string c = Fresh("c");
                        return new LoopSpec { Init = new[] { "const " + a + " = _bArr(_bNear(" + Ex(b, "pos") + ", " + Ex(b, "r") + "));", "let " + i + " = 0;" }, Cond = i + " < " + a + ".length", Each = new[] { "const " + c + " = " + a + "[" + i + "++];" }, VarKind = "creature", VarName = c };
                    }
                case "for_each_item":
                    {
                        string it = Fresh("it");
                        return new LoopSpec { Init = new[] { "const " + a + " = _bArr(" + Ex(b, "list") + ");", "let " + i + " = 0;" }, Cond = i + " < " + a + ".length", Each = new[] { "const " + it + " = " + a + "[" + i + "++];" }, VarKind = "item", VarName = it };
                    }
                default:
                    Error(b, "Internal: not a loop.");
                    return null;
            }
        }
    }
}
