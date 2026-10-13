using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Scripts.Blocks;

namespace Scripts.Blocks.Tests
{
    /// <summary>One test per family of blocks: compile a program, run the code it makes in Jint against stand-ins for the host, look at the log.</summary>
    internal static partial class Tests
    {
        // ---- small builders ----

        private static Block Hat(string type, string text, params Block[] body) => Do(text == null ? B(type) : B(type, ("text", text)), body);
        private static Block Say(Block who, Block text) => In(In(B("say"), "who", who), "text", text);
        private static Block SayT(string text) => In(B("say", ("text", text)), "who", B("ev_player"));
        private static Block Shout(string text) => B("announce", ("text", text));
        private static Block Var(string name) => B("var_get", ("v", name));
        private static Block Set(string name, string value) => B("set_var", ("v", name), ("val", value));
        private static Block Wait(string seconds) => B("wait", ("n", seconds));
        private static Block IfB(Block cond, params Block[] body) => Do(In(B("if"), "cond", cond), body);
        private static Block Cmp(Block a, string op, string b) => In(B("compare", ("op", op), ("b", b)), "a", a);
        private static Block Chat(params Block[] body) => Do(B("on_chat", ("text", "")), body);

        private static Compiled Comp(Program p, string what)
        {
            Compiled c = Compiler.Compile(p);
            Check(c.Ok, what + " compiles: " + string.Join("; ", c.Problems.Where(x => x.Fatal).Select(x => x.Message)));
            return c;
        }

        private static Engine Boot(Program p, string what)
        {
            Compiled c = Comp(p, what);
            return c.Ok ? Run(c.Js) : null;
        }

        private static void Fatal(Program p, Block at, string what)
        {
            Compiled c = Compiler.Compile(p);
            Check(!c.Ok && c.Js == null && (at == null || c.Problems.Any(x => x.Fatal && x.Block == at)), what + " is refused" + (at != null ? " and points at the block" : "") + ": " + string.Join("; ", c.Problems.Select(x => x.Message)));
        }

        private static string Said(Engine e, string chat = "{ player: mkPlayer('Ann'), text: 'hello' }")
        {
            e.Execute("_log.length = 0; handlers.chat[0](" + chat + ");");
            return Log(e);
        }

        private static string Pumped(Engine e)
        {
            e.Execute("_pump();");
            return Log(e);
        }

        /// <summary>Compiles `announce (value)` under a start hat, runs it, and returns the announcement.</summary>
        private static string Eval(Block value, params string[] vars)
        {
            var p = Prog(Do(B("on_start"), In(B("announce"), "text", value)));
            foreach (string v in vars) p.Vars.Add(v);
            Compiled c = Compiler.Compile(p);
            if (!c.Ok) { Check(false, "evaluating " + value.Type + " compiles: " + string.Join("; ", c.Problems.Select(x => x.Message))); return null; }
            Engine e = Run(c.Js);
            e.Execute("handlers.start[0]();");
            string log = Log(e);
            return log.StartsWith("broadcast:") ? log.Substring("broadcast:".Length) : log;
        }

        private static Block Num(string type, params (string, string)[] f) => B(type, f);

        private static Program Also(this Program p, Action<Program> a) { a(p); return p; }

        // ================================================================ families

        private static void Families()
        {
            WaitFamily();
            LoopFamily();
            ProcedureFamily();
            SignalFamily();
            ListFamily();
            MathTextFamily();
            NoteChainLocalFamily();
            ApiFamily();
            Showpieces();
            PaletteSanity();
        }

        private static void WaitFamily()
        {
            // a plain run of waits
            Engine e = Boot(Prog(Chat(SayT("A"), Wait("2"), SayT("B"), Wait("1.5"), SayT("C"))), "waits in a row");
            if (e != null)
            {
                Check(Said(e) == "say:Ann:A", "nothing after the first wait runs yet: " + Log(e));
                Check(Pumped(e) == "say:Ann:A|@2|say:Ann:B|@1.5|say:Ann:C", "each wait carries on at its time: " + Log(e));
            }

            // a wait inside an if, with something after the if
            Program branch = Prog(Chat(IfB(B("chance", ("p", "50")), SayT("X"), Wait("1"), SayT("Y")), SayT("Z")));
            e = Boot(branch, "a wait in an if");
            if (e != null)
            {
                Said(e); Check(Pumped(e) == "say:Ann:X|@1|say:Ann:Y|say:Ann:Z", "the rest waits for the branch: " + Log(e));
                e.Execute("_chance = false;"); Check(Said(e) == "say:Ann:Z", "and does not wait when the branch is skipped: " + Log(e));
            }

            // if / otherwise
            Block ie = Do(In(B("if_else"), "cond", B("chance", ("p", "50"))), Wait("3"), SayT("then"));
            ie.Body("else").Add(SayT("else"));
            e = Boot(Prog(Chat(ie, SayT("after"))), "a wait in if/otherwise");
            if (e != null)
            {
                Said(e); Check(Pumped(e) == "@3|say:Ann:then|say:Ann:after", "then-branch waits and the rest follows: " + Log(e));
                e.Execute("_chance = false;"); Check(Said(e) == "say:Ann:else|say:Ann:after", "the other branch needs no wait: " + Log(e));
            }

            // repeat with a wait inside
            e = Boot(Prog(Chat(Do(B("repeat", ("n", "3")), SayT("tick"), Wait("1")), SayT("done"))), "a wait in repeat");
            if (e != null) { Said(e); Check(Pumped(e) == "say:Ann:tick|@1|say:Ann:tick|@1|say:Ann:tick|@1|say:Ann:done", "three turns, a wait each, then done: " + Log(e)); }

            // for each player, wait between players
            e = Boot(Prog(Chat(Do(B("for_player"), Say(B("each_player"), B("join", ("a", "hi"))), Wait("2")), SayT("end"))), "a wait in for each player");
            if (e != null) { Said(e); Check(Pumped(e) == "say:Ann:hi|@2|say:Bob:hi|@2|say:Ann:end", "players one at a time: " + Log(e)); }

            // wait inside after
            e = Boot(Prog(Do(B("on_start"), Do(B("after", ("n", "5"), ("unit", "seconds")), Shout("a"), Wait("2"), Shout("b")))), "a wait in after");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Pumped(e) == "@5|broadcast:a|@2|broadcast:b", "an after block can wait: " + Log(e)); }

            // waits are refused where they cannot work
            Block w1 = Wait("1");
            Fatal(Prog(Do(B("on_hit"), w1)), w1, "a wait under 'when something is hit'");
            Block w2 = Wait("1");
            Fatal(Prog(Do(B("on_stop"), w2)), w2, "a wait under 'when this script stops'");
            Block w3 = Wait("1");
            Compiled loose = Compiler.Compile(new Program { Tops = { new Program.Top { Chain = { w3 } } } });
            Check(loose.Ok && loose.Problems.Any(x => !x.Fatal && x.Block == w3), "a loose wait is only a warning");
        }

        private static void LoopFamily()
        {
            Program p = Prog(Do(B("on_start"), Set("i", "0"), Set("sum", "0"),
                Do(B("while"), Change("i", "1"), IfB(Cmp(Var("i"), ">=", "3"), B("break")), Wait("1")),
                Shout("out")));
            p.Tops[0].Chain[0].Body("do")[2].Inputs["cond"] = B("is_day");
            p.Vars.Add("i"); p.Vars.Add("sum");
            Engine e = Boot(p, "while with a break and a wait");
            if (e != null)
            {
                e.Execute("handlers.start[0]();");
                Check(Pumped(e) == "@1|@1|broadcast:out", "break leaves a loop that waits: " + Log(e));
            }

            // continue in a loop that waits
            Block sayI = Say(B("ev_player"), Var("i"));
            Program pc = Prog(Chat(Do(B("for_range", ("v", "i"), ("a", "1"), ("b", "4")), IfB(Cmp(Var("i"), "=", "2"), B("continue")), sayI, Wait("1")), SayT("end")));
            pc.Vars.Add("i");
            e = Boot(pc, "continue in a loop that waits");
            if (e != null) { Said(e); Check(Pumped(e) == "say:Ann:1|@1|say:Ann:3|@1|say:Ann:4|@1|say:Ann:end", "continue skips a turn: " + Log(e)); }

            // plain loops: caps, until, range, break, continue, for each item
            Program cap = Prog(Do(B("on_start"), Do(B("while"), Change("n", "1"))));
            cap.Tops[0].Chain[0].Body("do")[0].Inputs["cond"] = B("is_day");
            cap.Vars.Add("n");
            e = Boot(cap, "an endless while");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(e.Evaluate("v_6e").AsNumber() == 1000, "a loop that never ends runs 1000 turns: " + e.Evaluate("v_6e").AsNumber()); }

            Program until = Prog(Do(B("on_start"), Do(In(B("repeat_until"), "cond", Cmp(Var("n"), ">=", "5")), Change("n", "1")), Shout("n")));
            until.Vars.Add("n");
            until.Tops[0].Chain[0].Body("do")[1].Inputs["text"] = Var("n");
            e = Boot(until, "repeat until");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:5", "repeat until stops when it is true: " + Log(e)); }

            Block range = Do(B("for_range", ("v", "i"), ("a", "1"), ("b", "10")),
                IfB(Cmp(Var("i"), "=", "3"), B("continue")),
                IfB(Cmp(Var("i"), "=", "6"), B("break")),
                In(B("change_var", ("v", "sum")), "n", Var("i")));
            Program pr = Prog(Do(B("on_start"), Set("sum", "0"), range, In(B("announce"), "text", Var("sum"))));
            pr.Vars.Add("i"); pr.Vars.Add("sum");
            e = Boot(pr, "for from..to with break and continue");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:12", "1+2+4+5 = 12: " + Log(e)); }

            Block each = Do(B("for_each_item"), In(B("announce"), "text", B("list_this")));
            each.Inputs["list"] = L("red, green, blue");
            Program pe = Prog(Do(B("on_start"), each));
            e = Boot(pe, "for each item");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:red|broadcast:green|broadcast:blue", "each item of a list: " + Log(e)); }

            // a body that may wait but did not: 800 turns without deep recursion
            Program deep = Prog(Do(B("on_start"), Do(B("repeat", ("n", "800")), IfB(B("chance", ("p", "0")), Wait("1")), Change("n", "1"))));
            deep.Vars.Add("n");
            Compiled dc = Comp(deep, "800 turns that might wait");
            if (dc.Ok)
            {
                var small = new Engine(new Options().LimitRecursion(60));
                small.Execute(Stubs); small.Execute("_chance = false;"); small.Execute(dc.Js);
                try { small.Execute("handlers.start[0]();"); Check(small.Evaluate("v_6e").AsNumber() == 800, "800 quick turns of a loop that could wait do not pile up calls: " + small.Evaluate("v_6e").AsNumber()); }
                catch (Exception ex) { Check(false, "a loop that could wait but does not overflowed: " + ex.Message); }
            }

            Block br = B("break");
            Fatal(Prog(Do(B("on_start"), br)), br, "break outside a loop");
            Block ct = B("continue");
            Fatal(Prog(Do(B("on_start"), Do(B("after", ("n", "1"), ("unit", "seconds")), ct))), ct, "continue in an after, outside its loop");
        }

        private static Block Change(string v, string by) => B("change_var", ("v", v), ("n", by));

        private static void ProcedureFamily()
        {
            Block define = Do(B("define", ("name", "greet"), ("p1", "who2")),
                Say(B("param", ("p", "who2")), B("join", ("a", "Hello"))),
                Wait("1"),
                Say(B("param", ("p", "who2")), B("join", ("a", "Bye"))));
            Block call = In(B("call_proc", ("name", "greet")), "a1", B("ev_player"));
            Engine e = Boot(Prog(define, Chat(call, SayT("After"))), "a my-block that waits");
            if (e != null) { Said(e); Check(Pumped(e) == "say:Ann:Hello|@1|say:Ann:Bye|say:Ann:After", "the caller carries on when the my-block is done: " + Log(e)); }

            Block twice = Do(B("define", ("name", "double"), ("p1", "x")),
                In(B("return_value"), "v", In(In(B("math", ("op", "+")), "a", B("param", ("p", "x"))), "b", B("param", ("p", "x")))));
            Block useIt = Do(B("on_start"), In(B("announce"), "text", In(B("proc_value", ("name", "double")), "a1", B("math", ("op", "+"), ("a", "21"), ("b", "0")))));
            e = Boot(Prog(twice, useIt), "a my-block that gives back a value");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:42", "result of a my-block: " + Log(e)); }

            // a my-block that calls itself
            Block count = Do(B("define", ("name", "count"), ("p1", "n")),
                IfB(Cmp(B("param", ("p", "n")), ">", "0"),
                    In(B("announce"), "text", B("param", ("p", "n"))),
                    In(B("call_proc", ("name", "count")), "a1", In(B("math", ("op", "-"), ("b", "1")), "a", B("param", ("p", "n"))))));
            Block go = Do(B("on_start"), In(B("call_proc", ("name", "count")), "a1", B("math", ("op", "+"), ("a", "3"), ("b", "0"))));
            e = Boot(Prog(count, go), "a my-block that calls itself");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:3|broadcast:2|broadcast:1", "recursion counts down: " + Log(e)); }

            // problems
            Block orphan = B("param", ("p", "x"));
            Fatal(Prog(Do(B("on_start"), In(B("announce"), "text", orphan))), orphan, "an input used outside its my-block");
            Block back = B("return_value");
            Fatal(Prog(Do(B("on_start"), back)), back, "give back outside a my-block");
            Block dup = Do(B("define", ("name", "same")));
            Fatal(Prog(Do(B("define", ("name", "same"))), dup), dup, "two my-blocks with one name");
            Block ghost = B("call_proc", ("name", "nothing"));
            Fatal(Prog(Do(B("on_start"), ghost)), ghost, "calling a my-block that does not exist");
            Block waits = Do(B("define", ("name", "slow")), Wait("1"));
            Block asValue = B("proc_value", ("name", "slow"));
            Fatal(Prog(waits, Do(B("on_start"), In(B("announce"), "text", asValue))), asValue, "a my-block that waits used for its value");
            Block nested = Do(B("define", ("name", "inner")));
            Fatal(Prog(Do(B("on_start"), nested)), nested, "a define inside another block");
        }

        private static void SignalFamily()
        {
            Block recv = Do(B("on_signal", ("name", "go")), In(B("announce"), "text", In(B("join", ("a", "got ")), "b", B("ev_signal"))));
            Block send = Do(B("on_start"), In(B("send_signal", ("name", "go")), "val", Num("math", ("op", "+"), ("a", "40"), ("b", "2"))));
            Engine e = Boot(Prog(recv, send), "send and receive a signal");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "signal:go:42|broadcast:got 42", "a signal carries its value: " + Log(e)); }

            // a signal sent from a script that waits wakes another handler later
            Block recv2 = Do(B("on_signal", ("name", "late")), Shout("woke"));
            Block send2 = Do(B("on_start"), Wait("2"), B("send_signal", ("name", "late")));
            e = Boot(Prog(recv2, send2), "a signal after a wait");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Pumped(e) == "@2|signal:late:undefined|broadcast:woke", "the signal comes after the wait: " + Log(e)); }

            Block badName = B("send_signal", ("name", "!!!"));
            Fatal(Prog(Do(B("on_start"), badName)), badName, "a signal with no usable name");
            Block outside = B("ev_signal");
            Fatal(Prog(Do(B("on_start"), In(B("announce"), "text", outside))), outside, "the signal's value outside its hat");
        }

        private static Block L(string items) => B("list_of", ("items", items));

        private static void ListFamily()
        {
            // a list in a variable, the operations on it
            Block fill = Do(B("on_start"),
                B("make_list", ("v", "L")),
                In(B("list_add", ("x", "a")), "l", Var("L")),
                In(B("list_add_num", ("x", "5")), "l", Var("L")),
                In(B("list_add", ("x", "c")), "l", Var("L")),
                In(B("announce"), "text", In(B("list_join", ("sep", "-")), "l", Var("L"))));
            Engine e = Boot(Prog(fill).Also(p => p.Vars.Add("L")), "list add and join");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:a-5-c", "add and join: " + Log(e)); }

            Check(Eval(In(B("list_length"), "l", L("a, b, c"))) == "3", "length of a list");
            Check(Eval(In(B("list_item", ("n", "2")), "l", L("a, b, c"))) == "b", "item 2");
            Check(Eval(In(B("list_item", ("n", "9")), "l", L("a, b, c"))) == "", "an item past the end is nothing");
            Check(Eval(In(B("list_has", ("x", "c")), "l", L("a, b, c"))) == "true", "contains");
            Check(Eval(In(B("list_has", ("x", "z")), "l", L("a, b, c"))) == "false", "does not contain");
            Check(Eval(In(B("list_index", ("x", "c")), "l", L("a, b, c"))) == "3", "place of");
            Check(Eval(In(B("list_sum"), "l", L("1, 2, 3"))) == "6", "sum");
            Check(Eval(In(B("list_random"), "l", L("x, y"))) == "x", "random item");
            Check(Eval(In(B("list_length"), "l", B("split_text", ("t", "a;b;c;d"), ("sep", ";")))) == "4", "split text");

            Block ops = Do(B("on_start"),
                B("make_list", ("v", "L")),
                In(B("list_add", ("x", "b")), "l", Var("L")), In(B("list_add", ("x", "a")), "l", Var("L")), In(B("list_add", ("x", "c")), "l", Var("L")),
                In(B("list_sort"), "l", Var("L")),
                In(B("announce"), "text", In(B("list_join", ("sep", ",")), "l", Var("L"))),
                In(B("list_reverse"), "l", Var("L")),
                In(B("announce"), "text", In(B("list_join", ("sep", ",")), "l", Var("L"))),
                In(B("list_remove_at", ("n", "1")), "l", Var("L")),
                In(B("list_insert", ("x", "z"), ("n", "1")), "l", Var("L")),
                In(B("announce"), "text", In(B("list_join", ("sep", ",")), "l", Var("L"))),
                In(B("list_clear"), "l", Var("L")),
                In(B("announce"), "text", In(B("list_length"), "l", Var("L"))));
            e = Boot(Prog(ops).Also(p => p.Vars.Add("L")), "list sort, reverse, insert, remove, clear");
            if (e != null)
            {
                e.Execute("handlers.start[0]();");
                string[] said = Log(e).Split('|');
                Check(said.Length == 4 && said[0] == "broadcast:a,b,c" && said[1] == "broadcast:c,b,a" && said[2] == "broadcast:z,b,a" && said[3] == "broadcast:0", "sort, reverse, remove, insert, clear: " + Log(e));
            }

            // 500 items at most
            Block big = Do(B("on_start"), B("make_list", ("v", "L")), Do(B("repeat", ("n", "900")), In(B("list_add", ("x", "x")), "l", Var("L"))), In(B("announce"), "text", In(B("list_length"), "l", Var("L"))));
            e = Boot(Prog(big).Also(p => p.Vars.Add("L")), "a long list");
            if (e != null) { e.Execute("handlers.start[0]();"); Check(Log(e) == "broadcast:500", "a list keeps 500 items: " + Log(e)); }

        }

        private static void MathTextFamily()
        {
            Check(Eval(B("math_fn", ("fn", "absolute value"), ("a", "-5"))) == "5", "absolute value");
            Check(Eval(B("math_fn", ("fn", "round down"), ("a", "2.7"))) == "2", "round down");
            Check(Eval(B("math_fn", ("fn", "round up"), ("a", "2.1"))) == "3", "round up");
            Check(Eval(B("math_fn", ("fn", "square root"), ("a", "16"))) == "4", "square root");
            Check(Eval(B("math_fn", ("fn", "square root"), ("a", "-4"))) == "0", "square root of a negative is 0, not an error");
            Check(Eval(B("math_fn", ("fn", "sine"), ("a", "90"))) == "1", "sine in degrees");
            Check(Eval(B("math_fn2", ("fn", "smaller"), ("a", "3"), ("b", "7"))) == "3", "smaller");
            Check(Eval(B("math_fn2", ("fn", "larger"), ("a", "3"), ("b", "7"))) == "7", "larger");
            Check(Eval(B("math_fn2", ("fn", "power"), ("a", "2"), ("b", "10"))) == "1024", "power");
            Check(Eval(B("math_fn2", ("fn", "angle"), ("a", "1"), ("b", "0"))) == "90", "angle");
            Check(Eval(B("round_to", ("a", "3.14159"), ("d", "2"))) == "3.14", "round to places");
            Check(Eval(B("clamp", ("x", "15"), ("a", "0"), ("b", "10"))) == "10", "clamp");
            Check(Eval(B("map_range", ("x", "5"), ("a", "0"), ("b", "10"), ("c", "0"), ("d", "100"))) == "50", "map a range");
            Check(Eval(B("pi")).StartsWith("3.14159"), "pi");
            Check(Eval(B("rand_decimal")) == "0", "random decimal");

            Check(Eval(B("text_length", ("t", "hello"))) == "5", "length of text");
            Check(Eval(B("text_letter", ("t", "hello"), ("n", "2"))) == "e", "letter 2");
            Check(Eval(B("text_letter", ("t", "hello"), ("n", "20"))) == "", "a letter past the end is nothing");
            Check(Eval(B("text_replace", ("t", "hello"), ("a", "l"), ("b", "L"))) == "heLLo", "replace");
            Check(Eval(B("text_replace", ("t", "hello"), ("a", ""), ("b", "L"))) == "hello", "replace of nothing changes nothing");
            Check(Eval(B("text_fn", ("fn", "uppercase"), ("t", "Hi"))) == "HI", "uppercase");
            Check(Eval(B("text_fn", ("fn", "lowercase"), ("t", "Hi"))) == "hi", "lowercase");
            Check(Eval(B("text_fn", ("fn", "trim"), ("t", "  hi "))) == "hi", "trim");
            Check(Eval(B("text_before", ("t", "ab-cd"), ("s", "-"))) == "ab", "words before");
            Check(Eval(B("text_after", ("t", "ab-cd"), ("s", "-"))) == "cd", "words after");
            Check(Eval(In(B("math", ("op", "+"), ("b", "1")), "a", B("text_to_number", ("t", "12,5")))) == "13.5", "text as a number");
            Check(Eval(B("number_to_text", ("n", "3.14159"), ("d", "2"))) == "3.14", "number as words");
            Check(Eval(B("join_many", ("a", "a"), ("b", "b"), ("c", "c"), ("d", "d"))) == "abcd", "join four");
        }

        private static void NoteChainLocalFamily()
        {
            Block note = B("note", ("text", "line one\nevil(); //"));
            Compiled c = Compiler.Compile(Prog(Do(B("on_start"), note, Shout("x"))));
            Check(c.Ok && c.Js.Contains("// line one evil(); //") && !c.Js.Split('\n').Any(l => l.StartsWith("evil")), "a note is a comment on one line");
            Compiled loose = Compiler.Compile(new Program { Tops = { new Program.Top { Chain = { B("note", ("text", "just a note")) } } } });
            Check(loose.Ok && loose.Problems.Count == 0, "a loose note is not a warning");

            for (int pick = 1; pick <= 4; pick++)
            {
                Block ch = Do(In(B("if_chain"), "c1", Cmp(Var("n"), "=", "1")), Shout("one"));
                ch.Inputs["c2"] = Cmp(Var("n"), "=", "2"); ch.Body("do2").Add(Shout("two"));
                ch.Inputs["c3"] = Cmp(Var("n"), "=", "3"); ch.Body("do3").Add(Shout("three"));
                ch.Body("else").Add(Shout("other"));
                Program pc = Prog(Do(B("on_start"), Set("n", pick.ToString()), ch));
                pc.Vars.Add("n");
                Engine e = Boot(pc, "an if chain");
                if (e == null) continue;
                e.Execute("handlers.start[0]();");
                string want = pick == 1 ? "one" : pick == 2 ? "two" : pick == 3 ? "three" : "other";
                Check(Log(e) == "broadcast:" + want, "if chain picks " + want + ": " + Log(e));
            }
            Block chWait = Do(In(B("if_chain"), "c1", B("is_day")), Wait("1"), Shout("slept"));
            chWait.Body("else").Add(Shout("no"));
            Engine ew = Boot(Prog(Do(B("on_start"), chWait, Shout("after"))), "an if chain with a wait");
            if (ew != null) { ew.Execute("handlers.start[0]();"); Check(Pumped(ew) == "@1|broadcast:slept|broadcast:after", "a chain branch can wait: " + Log(ew)); }

            // local variables
            Block mk = B("local_var", ("name", "x")); mk.Inputs["val"] = B("math", ("op", "+"), ("a", "5"), ("b", "0"));
            Block bump = B("set_local", ("name", "x")); bump.Inputs["val"] = In(B("math", ("op", "+"), ("b", "1")), "a", B("local_get", ("name", "x")));
            Engine el = Boot(Prog(Do(B("on_start"), mk, bump, In(B("announce"), "text", B("local_get", ("name", "x"))))), "local variables");
            if (el != null) { el.Execute("handlers.start[0]();"); Check(Log(el) == "broadcast:6", "a local variable: " + Log(el)); }
            Block early = B("local_get", ("name", "y"));
            Fatal(Prog(Do(B("on_start"), In(B("announce"), "text", early))), early, "a local variable read before it is made");
            Block anyVar = B("set_var_any", ("v", "w")); anyVar.Inputs["val"] = L("a,b");
            Program pa = Prog(Do(B("on_start"), anyVar, In(B("announce"), "text", In(B("list_length"), "l", Var("w")))));
            pa.Vars.Add("w");
            Engine ea = Boot(pa, "a variable that holds a list");
            if (ea != null) { ea.Execute("handlers.start[0]();"); Check(Log(ea) == "broadcast:2", "any kind of value in a variable: " + Log(ea)); }
        }

        // ================================================================ the host's API

        private static string Fire(string eventName, string eventJs, Program p, string what)
        {
            Engine e = Boot(p, what);
            if (e == null) return null;
            e.Execute("_log.length = 0; (handlers['" + eventName + "'] || []).forEach(function (f) { f(" + eventJs + "); });");
            return Log(e);
        }

        private static void ApiCheck(string what, Block hat, string eventName, string expect, string eventJs = "{ player: mkPlayer('Ann'), killer: mkPlayer('Bob'), victim: mkCreature('Boar'), boss: mkCreature('Eikthyr'), creature: mkCreature('Boar'), pos: {x:1,y:2,z:3} }")
        {
            string log = Fire(eventName, eventJs, Prog(hat), what);
            if (log != null) Check(log == expect, what + ": " + log + "  (wanted " + expect + ")");
        }

        private static Block Each() => B("each_player");
        private static Block Actor(string id) => B(id);

        private static void ApiFamily()
        {
            ApiCheck("talk", Do(B("on_join"), In(In(B("talk", ("text", "Hi")), "who", B("ev_player")), "who", B("ev_player"))), "playerJoin", "talk:Ann:Hi");
            ApiCheck("banner to a player", Do(B("on_join"), In(B("banner_to", ("text", "Welcome")), "who", B("ev_player"))), "playerJoin", "banner:Ann:Welcome");
            ApiCheck("banner for everyone", Do(B("on_join"), B("banner_all", ("text", "Hear ye"))), "playerJoin", "banner:Hear ye");
            ApiCheck("ping the map", Do(B("on_join"), In(B("ping", ("text", "Here!")), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "ping:Here!");
            ApiCheck("name a creature", Do(B("on_boss_appears"), In(B("name_creature", ("text", "Fluffy")), "who", B("ev_boss"))), "bossSpawn", "name:Eikthyr:Fluffy");
            ApiCheck("set stamina", Do(B("on_join"), In(B("set_stamina", ("n", "50")), "who", B("ev_player"))), "playerJoin", "stamina:Ann:50");
            ApiCheck("apply an effect", Do(B("on_join"), In(B("apply_effect", ("effect", "Rested"), ("n", "30")), "who", B("ev_player"))), "playerJoin", "effect:Ann:Rested:30");
            ApiCheck("take an effect off", Do(B("on_join"), In(B("remove_effect", ("effect", "Rested")), "who", B("ev_player"))), "playerJoin", "uneffect:Ann:Rested");
            ApiCheck("launch up", Do(B("on_join"), In(B("launch", ("dir", "up"), ("n", "8")), "who", B("ev_player"))), "playerJoin", "launch:Ann:0,1,0:8");
            ApiCheck("launch away", Do(B("on_join"), In(In(B("launch_away", ("n", "5")), "who", B("ev_player")), "from", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "launch:Ann:0,0.4,0:5");
            ApiCheck("tame", Do(B("on_tamed"), In(B("tame"), "who", B("ev_creature"))), "tamed", "tame:Boar");
            ApiCheck("remove a creature", Do(B("on_tamed"), In(B("remove_creature"), "who", B("ev_creature"))), "tamed", "remove:Boar");
            ApiCheck("play an effect", Do(B("on_join"), In(B("play_effect", ("effect", HostData.VisualEffects[0])), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "effect:" + HostData.VisualEffects[0]);
            ApiCheck("play a sound", Do(B("on_join"), In(B("play_sound", ("sound", HostData.Sounds[0])), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "sound:" + HostData.Sounds[0]);
            ApiCheck("lightning", Do(B("on_join"), In(B("lightning"), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "lightning");
            ApiCheck("shake", Do(B("on_join"), In(B("shake", ("r", "30"), ("s", "1")), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "shake:30:1");
            ApiCheck("wind", Do(B("on_join"), B("set_wind", ("deg", "90"), ("s", "0.8"), ("n", "60"))), "playerJoin", "wind:90:0.8:60");
            ApiCheck("start an event", Do(B("on_join"), B("start_event", ("name", HostData.Events[0]))), "playerJoin", "event:" + HostData.Events[0]);
            ApiCheck("stop the event", Do(B("on_join"), B("stop_event")), "playerJoin", "event:stop");
            ApiCheck("place a piece", Do(B("on_join"), In(B("place_piece", ("piece", HostData.Pieces[0])), "pos", In(B("pos_of"), "who", B("ev_player")))), "playerJoin", "piece:" + HostData.Pieces[0]);
            ApiCheck("send to the web", Do(B("on_join"), B("send_web", ("text", "Hello"), ("endpoint", "discord"))), "playerJoin", "net:discord:Hello");

            Block place = In(B("place_piece_into", ("piece", HostData.Pieces[0]), ("v", "pc")), "pos", In(B("pos_of"), "who", B("ev_player")));
            Block take = In(B("remove_piece"), "p", Var("pc"));
            Program pp = Prog(Do(B("on_join"), place, take)); pp.Vars.Add("pc");
            string pl = Fire("playerJoin", "{ player: mkPlayer('Ann') }", pp, "place and remove a piece");
            if (pl != null) Check(pl == "piece:" + HostData.Pieces[0] + "|piece:removed", "place and remove a piece: " + pl);

            Block sp = In(B("spawn_into", ("stars", "2"), ("creature", "Boar"), ("v", "boar")), "pos", In(B("pos_of"), "who", B("ev_player")));
            Program ps = Prog(Do(B("on_join"), sp, In(B("announce"), "text", In(B("name_of"), "who", Var("boar"))), In(B("announce"), "text", In(B("name_of"), "who", B("spawned_creature")))));
            ps.Vars.Add("boar");
            string sl = Fire("playerJoin", "{ player: mkPlayer('Ann') }", ps, "spawn into a variable");
            if (sl != null) Check(sl == "spawn:Boar*2|broadcast:Boar|broadcast:Boar", "spawn and keep it: " + sl);

            // reads
            Check(Eval(In(B("stamina_of"), "who", FirstPlayer())) == "80", "stamina_of");
            Check(Eval(In(B("max_stamina_of"), "who", FirstPlayer())) == "100", "max stamina");
            Check(Eval(In(B("speed_of"), "who", FirstPlayer())) == "2.5", "speed");
            Check(Eval(In(B("deaths_of"), "who", FirstPlayer())) == "2", "deaths");
            Check(Eval(In(B("skill_of", ("skill", "Swords")), "who", FirstPlayer())) == "42", "a skill");
            Check(Eval(In(B("faction_of"), "who", FirstPlayer())) == "players", "faction");
            Check(Eval(In(B("is_sitting"), "who", FirstPlayer())) == "true", "is sitting");
            Check(Eval(In(B("is_swimming"), "who", FirstPlayer())) == "false", "is swimming");
            Check(Eval(In(B("equipped_in", ("slot", "right")), "who", FirstPlayer())) == "AxeStone", "equipped in right");
            Check(Eval(In(B("list_join", ("sep", ",")), "l", In(B("inventory_of"), "who", FirstPlayer()))) == "Wood,Stone", "inventory as a list");
            Check(Eval(B("real_hour")) == "13" && Eval(B("real_minute")) == "37", "the real clock");
            Check(Eval(In(B("list_length"), "l", In(B("nearby_creatures", ("r", "20")), "pos", In(B("pos_of"), "who", FirstPlayer())))) == "2", "nearby creatures");
            Check(Eval(In(B("list_length"), "l", In(B("nearby_players", ("r", "20")), "pos", In(B("pos_of"), "who", FirstPlayer())))) == "1", "nearby players");
            Check(Eval(In(B("list_length"), "l", In(B("nearby_pieces", ("r", "20")), "pos", In(B("pos_of"), "who", FirstPlayer())))) == "1", "nearby pieces");
            Check(Eval(In(B("distance_between"), "a", In(B("pos_of"), "who", FirstPlayer())).Also(b => b.Inputs["b"] = In(B("pos_of"), "who", FirstPlayer()))) == "5", "distance between");
            Check(Eval(In(B("direction_from"), "a", In(B("pos_of"), "who", FirstPlayer())).Also(b => b.Inputs["b"] = In(B("pos_of"), "who", FirstPlayer()))) == "90", "direction from");

            // events and what they tell
            ApiCheck("when something is hit", Do(B("on_hit"), In(B("announce"), "text", In(In(B("join"), "a", B("ev_weapon")), "b", B("ev_damage")))), "hit", "broadcast:Club10", "{ attacker: mkCreature('Wolf'), target: mkPlayer('Ann'), damage: 10, weapon: 'Club', pos: {x:1,y:1,z:1} }");
            Engine eh = Boot(Prog(Do(B("on_hit"), B("set_damage", ("n", "3")), B("multiply_damage", ("n", "2")))), "set and multiply the damage");
            if (eh != null) { eh.Execute("var ev = { damage: 10 }; handlers.hit[0](ev);"); Check(eh.Evaluate("ev.damage").AsNumber() == 6, "damage set then multiplied: " + eh.Evaluate("ev.damage").AsNumber()); }
            Block badScope = B("set_damage", ("n", "1"));
            Fatal(Prog(Do(B("on_join"), badScope)), badScope, "set damage outside 'when something is hit'");
            ApiCheck("a piece built", Do(B("on_piece_built"), In(B("announce"), "text", In(B("piece_kind"), "p", B("ev_piece")))), "pieceBuilt", "broadcast:wood_wall", "{ player: mkPlayer('Ann'), piece: { id: 'pc', prefab: 'wood_wall', pos: {x:1,y:1,z:1} } }");
            ApiCheck("an item picked up", Do(B("on_pickup"), In(B("announce"), "text", In(B("item_of", ("field", "count")), "item", B("ev_item")))), "itemPickup", "broadcast:3", "{ player: mkPlayer('Ann'), item: { prefab: 'Wood', name: 'Wood', count: 3, quality: 1 } }");
            ApiCheck("food eaten", Do(B("on_eat"), In(B("announce"), "text", In(B("item_of", ("field", "name")), "item", B("ev_item")))), "eat", "broadcast:Wood", "{ player: mkPlayer('Ann'), item: { prefab: 'Wood', name: 'Wood', count: 1, quality: 1 } }");
            ApiCheck("a skill goes up", Do(B("on_skill_up"), In(B("announce"), "text", In(In(B("join"), "a", B("ev_skill")), "b", B("ev_level")))), "skillUp", "broadcast:Swords7", "{ player: mkPlayer('Ann'), skill: 'Swords', level: 7 }");
            ApiCheck("a raid begins", Do(B("on_raid_start"), In(B("announce"), "text", B("ev_event_name"))), "raidStart", "broadcast:army_eikthyr", "{ name: 'army_eikthyr' }");
            ApiCheck("a raid ends", Do(B("on_raid_end"), In(B("announce"), "text", B("ev_event_name"))), "raidEnd", "broadcast:army_eikthyr", "{ name: 'army_eikthyr' }");
            ApiCheck("the weather changes", Do(B("on_weather_change"), In(B("announce"), "text", In(In(B("join"), "a", B("ev_from")), "b", B("ev_to")))), "weatherChange", "broadcast:clearrain", "{ from: 'clear', to: 'rain' }");
            ApiCheck("a player comes back", Do(B("on_respawn"), In(In(B("say", ("text", "Welcome back")), "who", B("ev_player")), "who", B("ev_player"))), "playerRespawn", "say:Ann:Welcome back", "{ player: mkPlayer('Ann') }");

            Engine ec = Boot(Prog(Do(B("on_command", ("name", "/Hello")), In(B("announce"), "text", In(In(B("join"), "a", B("ev_arg", ("n", "2"))), "b", In(B("name_of"), "who", B("ev_player")))))), "a chat command");
            if (ec != null) { ec.Execute("commands['hello'](mkPlayer('Ann'), ['x', 'y']);"); Check(Log(ec) == "broadcast:yAnn", "a chat command and its words: " + Log(ec)); }
            Block badCmd = Do(B("on_command", ("name", "!!!")));
            Fatal(Prog(badCmd), null, "a command with no usable name");
        }

        private static Block FirstPlayer() => In(B("list_item", ("n", "1")), "l", B("players_list"));

        private static string ExampleJs(string file) => Compiler.Compile(BlockExamples.All().First(x => x.Key == file).Value).Js;

        private static void Showpieces()
        {
            // ---- The Wild Hunt ----
            Engine e = Run(ExampleJs("wild-hunt.js"));
            e.Execute("handlers.start[0]();");
            string start = Log(e);
            Check(start.StartsWith("weather:storm:360|time:0|spawn:Boar|spawn:Boar|banner:THE WILD HUNT|lightning|shake:40:1.5|lightning|shake:40:1.5"), "the hunt sets the scene at once: " + start);
            e.Execute("_log.length = 0; timers[0].f();");
            string wave1 = Log(e);
            Check(wave1.StartsWith("broadcast:WAVE 1 - 1 star hunters") && wave1.Split('|').Count(x => x == "spawn:Boar") == 4, "wave 1: boars, two around each player: " + wave1);
            e.Execute("_log.length = 0; timers[0].f(); timers[0].f();");
            Check(Log(e).Contains("spawn:Greyling") && Log(e).Contains("spawn:Greydwarf"), "the kinds take turns: " + Log(e));
            e.Execute("_log.length = 0; timers[0].f();");
            string wave4 = Log(e);
            Check(wave4.StartsWith("broadcast:WAVE 4 - 2 star hunters") && wave4.Split('|').Count(x => x == "spawn:Skeleton*2") == 6, "wave 4: three two-star skeletons around each player: " + wave4);
            e.Execute("_log.length = 0; timers[0].f();");
            string alpha = Log(e);
            Check(alpha.StartsWith("banner:THE ALPHA HAS ARRIVED") && alpha.Split('|').Count(x => x == "spawn:Greydwarf_Elite*3") == 2 && alpha.Contains("effect:fx_summon_start|talk:Greydwarf_Elite:You dare enter my forest?") && alpha.Contains("ping:THE ALPHA") && alpha.Split('|').Count(x => x == "lightning") >= 2, "wave 4 brings the Alpha with a taunt, a ping and lightning: " + alpha);
            Check(alpha.Split('|').Count(x => x == "spawn:Neck*2") == 6, "with its pack of three necks for each player: " + alpha);
            e.Execute("for (var i = 0; i < 3; i++) timers[0].f(); _log.length = 0; timers[0].f();");
            Check(Log(e).Contains("3 star hunters"), "by wave 9 the hunters have three stars: " + Log(e));

            e.Execute("_pump(6); _log.length = 0; for (var i = 0; i < 5; i++) handlers.kill[0]({ victim: mkCreature('Boar'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e).Contains("say:Bob:Hunt kills: 5") && Log(e).Contains("broadcast:Bob has slain 5 hunters!"), "kills are counted and the fifth is called out: " + Log(e));
            e.Execute("_log.length = 0; handlers.kill.forEach(function (f) { f({ victim: mkCreature('Greydwarf_Elite'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} }); });");
            Check(Log(e).Contains("banner:THE ALPHA IS DEAD") && Log(e).Contains("effect:vfx_Firework_Rocket_Red") && Log(e).Contains("give:Bob:Honey:2"), "the Alpha is worth honey and extra kills: " + Log(e));
            Check(e.Evaluate("_store['p_Bob|hunt']").AsNumber() == 8, "the Alpha counted three: " + e.Evaluate("_store['p_Bob|hunt']").AsNumber());
            e.Execute("_log.length = 0; handlers.kill[0]({ victim: mkPlayer('Ann'), killer: mkPlayer('Bob'), pos: {x:0,y:0,z:0} });");
            Check(Log(e) == "", "a player kill does not count");
            e.Execute("_log.length = 0; _pump();");
            string rest = Log(e);
            Check(rest.Contains("broadcast:LEADER: Bob with 8 kills") && rest.Contains("broadcast:HALFWAY THROUGH THE NIGHT") && rest.Contains("broadcast:ONE MINUTE until dawn!") && rest.Contains("broadcast:THIRTY SECONDS"), "the clock of the night calls out the leader and the time left: " + rest);
            Check(rest.Contains("banner:DAWN BREAKS") && rest.Contains("weather:clear:60") && rest.Contains("time:0.25") && rest.Contains("broadcast:MVP: Bob with 8 kills!") && rest.Contains("broadcast:NEW WORLD RECORD: 8 kills by Bob"), "dawn names the MVP and the record: " + rest);
            Check(rest.Contains("give:Bob:Coins:5") && !rest.Contains("give:Ann:Coins"), "the one who earned it is paid: " + rest);
            Check(e.Evaluate("_store['world|hunt_record']").AsNumber() == 8, "the world record is kept");
            e.Execute("_log.length = 0; timers[0].f();");
            Check(Log(e) == "", "no more waves after dawn: " + Log(e));
            e.Execute("_log.length = 0; handlers.playerJoin[0]({ player: mkPlayer('Cid') });");
            Check(Log(e) == "", "a player who joins after dawn is told nothing");

            // ---- Meteor Shower ----
            e = Run(ExampleJs("meteor-shower.js"));
            e.Execute("handlers.start[0]();");
            Check(Log(e).StartsWith("banner:METEOR SHOWER|time:0|wind:90:0.9:70"), "the shower starts at once with a banner, the night and a wind: " + Log(e));
            e.Execute("_log.length = 0; timers[0].f();");
            Check(Log(e) == "ping:METEOR INCOMING!|ping:METEOR INCOMING!", "every player is given a marker: " + Log(e));
            e.Execute("_log.length = 0; _pump();");
            string shower = Log(e);
            Check(shower.Contains("lightning|effect:vfx_FireballHit|shake:40:2") && shower.Contains("banner:THE SKY CLEARS") && shower.Contains("say:Ann:You dodged 1 meteors."), "the strikes land and the dodges are counted: " + shower);
            Check(!shower.Contains("launch:"), "nobody near the strike is thrown");
            e = Run(ExampleJs("meteor-shower.js"));
            e.Execute("handlers.start[0](); timers[0].f(); nearbyPlayers = function () { return [mkPlayer('Ann')]; }; _log.length = 0; _pump(2);");
            Check(Log(e).Contains("launch:Ann:") && Log(e).Contains("say:Ann:A meteor hit you!"), "a player at the strike is thrown and told: " + Log(e));
            e.Execute("_pump();");
            e.Execute("_log.length = 0; timers[0].f();");
            Check(Log(e) == "", "no meteors after the shower ends: " + Log(e));

            // ---- Treasure Hunt ----
            e = Run(ExampleJs("treasure-hunt.js"));
            e.Execute("handlers.start[0]();");
            Check(Log(e).Contains("banner:TREASURE HUNT"), "the hunt announces itself at once: " + Log(e));
            e.Execute("var _dist = 10; distance = function (a, b) { return _dist; }; _log.length = 0; timers[0].f();");
            Check(Log(e) == "say:Ann:Burning hot! Go east|say:Bob:Burning hot! Go east", "hot and a direction: " + Log(e));
            e.Execute("_dist = 20; _log.length = 0; timers[0].f();");
            Check(Log(e).StartsWith("say:Ann:Warm... go east"), "warm: " + Log(e));
            e.Execute("_dist = 40; _log.length = 0; timers[0].f();");
            Check(Log(e).StartsWith("say:Ann:Cool. Try east"), "cool: " + Log(e));
            e.Execute("_dist = 100; _log.length = 0; timers[0].f();");
            Check(Log(e).StartsWith("say:Ann:Freezing. Head east"), "freezing: " + Log(e));
            e.Execute("_dist = 2; _log.length = 0; timers[0].f();");
            Check(Log(e) == "lightning|shake:40:2|effect:vfx_Firework_Rocket_Red|effect:vfx_Firework_Rocket_Yellow|sound:sfx_firework_explode|banner:TREASURE FOUND|broadcast:Ann found the treasure!|drop:Coins:10|drop:Honey:5|ping:TREASURE!", "found: lightning, a banner, the loot and a ping, once: " + Log(e));
            e.Execute("_log.length = 0; timers[0].f();");
            Check(Log(e) == "", "and the hunt is over");
            Check(e.Evaluate("_store['world|treasures']").AsNumber() == 1, "the finds are counted in the world");
            e = Run(ExampleJs("treasure-hunt.js"));
            e.Execute("handlers.start[0](); _log.length = 0; _pump();");
            Check(Log(e).Contains("ping:Somewhere around here..."), "after a minute the map is pinged: " + Log(e));
        }

        private static void PaletteSanity()
        {
            var ids = new HashSet<string>();
            foreach (BlockDef d in Defs.All)
            {
                Check(ids.Add(d.Id), "block id " + d.Id + " is unique");
                Check(Defs.Categories.Contains(d.Category), d.Id + " is in a known category (" + d.Category + ")");
                var keys = new HashSet<string>();
                foreach (Part part in d.Parts) if (part.Slot != null) Check(keys.Add(part.Slot.Key), d.Id + " has a slot key once: " + part.Slot.Key);
                Check(d.Needs == null || Manifest.Capabilities.Contains(d.Needs) || d.Needs == "effects", d.Id + " needs a power the host knows: " + d.Needs);
                foreach (Part part in d.Parts)
                {
                    if (part.Slot == null || part.Slot.Kind != SlotKind.Pick) continue;
                    bool dynamic = new[] { "var", "lists", "procedures", "params", "locals", "signals" }.Contains(part.Slot.List);
                    Check(dynamic || BlockLists.Get(part.Slot.List, null).Length > 0, d.Id + " dropdown '" + part.Slot.List + "' has choices");
                }
                if (d.Shape == Shape.Value) Check(d.Expr != null, d.Id + " has code");
                else Check(d.Emit != null, d.Id + " has code");
            }
            Console.WriteLine($"palette: {Defs.All.Count} blocks in {Defs.Categories.Length} tabs: " + string.Join(", ", Defs.Categories.Select(c => c + " " + Defs.All.Count(d => d.Category == c))));
        }

        // ================================================================ the fuzzer

        private static Program _fp;

        private static void Fuzz()
        {
            var rng = new Random(2026);
            var stacks = Defs.All.Where(d => d.Shape == Shape.Stack || d.Shape == Shape.C).ToList();
            var values = Defs.All.Where(d => d.Shape == Shape.Value).ToList();
            var hats = Defs.All.Where(d => d.Shape == Shape.Hat).ToList();
            int ok = 0, refused = 0, stopped = 0;
            const string ev = "{ player: mkPlayer('Ann'), killer: mkPlayer('Bob'), victim: mkCreature('Boar'), boss: mkCreature('Eikthyr'), text: 'hi', pos: {x:1,y:2,z:3}, biome: 'Meadows', cause: 'creature', day: 2, attacker: mkCreature('Wolf'), target: mkPlayer('Ann'), damage: 10, weapon: 'Club', piece: { id: 'pc', prefab: 'wood_wall', pos: {x:1,y:1,z:1} }, item: { prefab: 'Wood', name: 'Wood', count: 3, quality: 1 }, skill: 'Swords', level: 5, name: 'army_eikthyr', from: 'clear', to: 'rain', args: ['a', 'b'], creature: mkCreature('Boar') }";
            for (int n = 0; n < 4000; n++)
            {
                _fp = new Program { Name = "fuzz" };
                _fp.Vars.Add("a"); _fp.Vars.Add("b"); _fp.Vars.Add("c");
                var p = _fp;
                if (rng.Next(2) == 0)
                {
                    Block def = B("define", ("name", "go"), ("p1", "x1"), ("p2", "x2"));
                    for (int i = 0; i < rng.Next(4); i++) def.Body("do").Add(RandomBlock(rng, stacks[rng.Next(stacks.Count)], stacks, values, 0));
                    p.Tops.Add(new Program.Top { Chain = { def } });
                }
                for (int t = 0; t < 1 + rng.Next(3); t++)
                {
                    BlockDef hd = hats[rng.Next(hats.Count)];
                    if (hd.Id == "define") continue;
                    p.Tops.Add(new Program.Top { Chain = { RandomBlock(rng, hd, stacks, values, 0) } });
                }
                Compiled c = Compiler.Compile(p);
                bool fatal = c.Problems.Any(x => x.Fatal);
                if (fatal) { refused++; Check(c.Js == null, "a fatal program gives no code"); continue; }
                ok++;
                try
                {
                    var e = new Engine(new Options().LimitRecursion(120).MaxStatements(400000).TimeoutInterval(TimeSpan.FromSeconds(3)));
                    e.Execute(Stubs + "function evil() { throw new Error('INJECTED'); }");
                    e.Execute(c.Js);
                    e.Execute("Object.keys(handlers).forEach(function (k) { handlers[k].slice().forEach(function (f) { f(" + ev + "); }); });");
                    e.Execute("Object.keys(commands).forEach(function (k) { commands[k](mkPlayer('Ann'), ['a', 'b']); });");
                    e.Execute("timers.filter(function (t) { return !t.once; }).forEach(function (t) { t.f(); }); _pump(300);");
                    Check(true, "ran");
                }
                catch (Jint.Runtime.RecursionDepthOverflowException) { stopped++; }
                catch (Jint.Runtime.StatementsCountOverflowException) { stopped++; }
                catch (TimeoutException) { stopped++; }
                catch (Exception ex)
                {
                    Check(false, "a program the compiler accepted failed to run: " + ex.Message + "\n" + c.Js);
                    return;
                }
            }
            Console.WriteLine($"fuzz: {ok} programs compiled and ran ({stopped} stopped by the sandbox's limits), {refused} refused");
            Check(ok > 400, "the fuzzer makes enough good programs (" + ok + ")");
        }

        private static readonly string[] FuzzNumbers = { "0", "1", "5", "-3", "9999", "abc", "", "2.5" };
        private static readonly string[] FuzzWords = { "hi", "\"); evil(); //", "a, b, c", "", "x\ny", "go", "x1", "go x" };

        private static Block RandomBlock(Random rng, BlockDef def, List<BlockDef> stacks, List<BlockDef> values, int depth)
        {
            var b = new Block(def.Id);
            foreach (Part part in def.Parts)
            {
                Slot s = part.Slot;
                if (s == null) continue;
                switch (s.Kind)
                {
                    case SlotKind.Num:
                        if (rng.Next(4) == 0 && depth < 4) b.Inputs[s.Key] = RandomValue(rng, values, depth + 1);
                        else b.Fields[s.Key] = FuzzNumbers[rng.Next(FuzzNumbers.Length)];
                        break;
                    case SlotKind.Text:
                        if (rng.Next(4) == 0 && depth < 4) b.Inputs[s.Key] = RandomValue(rng, values, depth + 1);
                        else b.Fields[s.Key] = FuzzWords[rng.Next(FuzzWords.Length)];
                        break;
                    case SlotKind.Pick:
                        string[] list = BlockLists.Get(s.List, _fp);
                        b.Fields[s.Key] = rng.Next(12) == 0 ? "bogus" : list.Length == 0 ? "" : list[rng.Next(list.Length)];
                        break;
                    case SlotKind.Socket:
                        if ((s.Optional ? rng.Next(3) == 0 : rng.Next(12) != 0) && depth < 5) b.Inputs[s.Key] = RandomValue(rng, values, depth + 1);
                        break;
                    case SlotKind.Body:
                        if (depth < 4)
                            for (int i = 0; i < rng.Next(4); i++)
                                b.Body(s.Key).Add(RandomBlock(rng, stacks[rng.Next(stacks.Count)], stacks, values, depth + 1));
                        break;
                }
            }
            return b;
        }

        private static Block RandomValue(Random rng, List<BlockDef> values, int depth) =>
            RandomBlock(rng, values[rng.Next(values.Count)], new List<BlockDef> { Defs.Find("log"), Defs.Find("wait") }, values, depth);
    }
}
