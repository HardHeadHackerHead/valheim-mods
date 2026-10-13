using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts.Blocks
{
    /// <summary>The language blocks: waiting, signals, my blocks, lists, loops, math, text, notes and variables (docs/scripting-expansion.md, section 7).</summary>
    internal static partial class Defs
    {
        private static BlockDef Control(string id, Shape shape, string label, Action<Gen, Block> emit, bool terminal = false) =>
            Add(new BlockDef { Id = id, Category = "control", Shape = shape, Label = label, Emit = emit, Terminal = terminal });

        private static BlockDef Fn(string id, string category, string type, string label, string needs, Func<Gen, Block, string> expr) =>
            Add(new BlockDef { Id = id, Category = category, Shape = Shape.Value, Type = type, Label = label, Needs = needs, Expr = expr });

        private static void Language()
        {
            // ---------------------------------------------------------------- control
            Grp("Waiting");
            Control("wait", Shape.Stack, "wait {n:num:1:0.05:3600} seconds", (g, b) => g.Error(b, "A wait only works inside something that starts (a 'when' block, an 'after', a my-block)."));

            Grp("Loops");
            Control("repeat_until", Shape.C, "repeat until {cond:sock:bool}{do:body}", (g, b) => g.EmitLoop(b, false, null));
            Control("while", Shape.C, "repeat while {cond:sock:bool}{do:body}", (g, b) => g.EmitLoop(b, false, null));
            Control("for_range", Shape.C, "for {v:pick:var} from {a:num:1} to {b:num:10}{do:body}", (g, b) => g.EmitLoop(b, false, null));
            Control("for_each_item", Shape.C, "for each item in {list:sock:list}{do:body}", (g, b) => g.EmitLoop(b, false, null));
            Control("break", Shape.Stack, "break out of the loop", (g, b) => g.Line(b, g.BreakCode(b)), terminal: true);
            Control("continue", Shape.Stack, "skip to the next turn", (g, b) => g.Line(b, g.ContinueCode(b)), terminal: true);

            Grp("Choices");
            Control("if_chain", Shape.C, "if {c1:sock:bool}{do:body}else if {c2:sock:bool?}{do2:body}else if {c3:sock:bool?}{do3:body}otherwise{else:body}", (g, b) => g.EmitIfFamily(b, null));

            Grp("Notes");
            Add(new BlockDef { Id = "note", Category = "control", Shape = Shape.Stack, Label = "note: {text:text:what this part does}", Inert = true, Emit = (g, b) => { } });

            // ---------------------------------------------------------------- functions: my blocks and signals
            Grp("My blocks");
            Add(new BlockDef { Id = "define", Category = "functions", Shape = Shape.Hat, Label = "define {name:text:my block} with inputs {p1:text} {p2:text} {p3:text} {p4:text}{do:body}", Emit = (g, b) => { } });
            Add(new BlockDef
            {
                Id = "call_proc", Category = "functions", Shape = Shape.Stack,
                Label = "do {name:pick:procedures} {a1:sock:any?} {a2:sock:any?} {a3:sock:any?} {a4:sock:any?}",
                Emit = (g, b) => { Block d = g.ProcOf(b, "name"); if (d != null) g.Line(b, g.ProcId(d) + "(" + g.CallArgs(b, d, null) + ");"); },
            });
            Fn("proc_value", "functions", "any", "result of {name:pick:procedures} {a1:sock:any?} {a2:sock:any?} {a3:sock:any?} {a4:sock:any?}", null, (g, b) =>
            {
                Block d = g.ProcOf(b, "name");
                if (d == null) return "undefined";
                if (g.IsAsyncCall(d)) { g.Error(b, "A my-block that waits can't give back a value: use it as a stack block."); return "undefined"; }
                return g.ProcId(d) + "(" + g.CallArgs(b, d, null) + ")";
            });
            Add(new BlockDef
            {
                Id = "return_value", Category = "functions", Shape = Shape.Stack, Label = "give back {v:sock:any}", Terminal = true,
                Emit = (g, b) =>
                {
                    if (!g.InProc) g.Error(b, "'give back' only works inside a 'define' block.");
                    else if (g.InAsyncProc) g.Error(b, "A my-block that waits can't give back a value.");
                    else g.Line(b, "return " + g.Ex(b, "v") + ";");
                },
            });
            Fn("param", "functions", "any", "input {p:pick:params}", null, (g, b) => g.ParamRef(b, "p"));

            Grp("Signals");
            Add(new BlockDef { Id = "send_signal", Category = "functions", Shape = Shape.Stack, Label = "send signal {name:text:go} with {val:sock:any?}", Emit = (g, b) => g.Line(b, "signal(" + g.SignalName(b, "name") + ", " + g.Ex(b, "val") + ");") });
            Hat("on_signal", "when signal {name:text:go} arrives", "", (g, b) => { g.Open(b, "onSignal(" + g.SignalName(b, "name") + ", e => {"); g.Body(b, "do"); g.Close("});"); });
            Value("ev_signal", "any", "the signal's value", null, "e", Under("on_signal"), "functions");

            // ---------------------------------------------------------------- memory: variables
            Grp("Variables");
            Add(new BlockDef { Id = "set_var_any", Category = "memory", Shape = Shape.Stack, Label = "set {v:pick:var} to {val:sock:any}", Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = " + g.Ex(b, "val") + ";") });
            Add(new BlockDef { Id = "make_list", Category = "memory", Shape = Shape.Stack, Label = "make {v:pick:var} an empty list", Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = [];") });
            Grp("Local variables");
            Add(new BlockDef { Id = "local_var", Category = "memory", Shape = Shape.Stack, Label = "make local {name:text:x} = {val:sock:any}", Emit = (g, b) => { string value = g.Ex(b, "val"); g.Line(b, "let " + g.LocalDeclare(b, "name") + " = " + value + ";"); } });
            Add(new BlockDef { Id = "set_local", Category = "memory", Shape = Shape.Stack, Label = "set local {name:pick:locals} to {val:sock:any}", Emit = (g, b) => g.Line(b, g.LocalRef(b, "name") + " = " + g.Ex(b, "val") + ";") });
            Fn("local_get", "memory", "any", "local {name:pick:locals}", null, (g, b) => g.LocalRef(b, "name"));

            // ---------------------------------------------------------------- lists
            Grp("Make a list");
            Fn("list_of", "lists", "list", "list of {items:text:red, green, blue}", null, (g, b) => "_bSplit(" + g.Ex(b, "items") + ")");
            Fn("split_text", "lists", "list", "split {t:text} by {sep:text:,}", null, (g, b) => "_bSplitRaw(" + g.Ex(b, "t") + ", " + g.Ex(b, "sep") + ")");
            Fn("players_list", "lists", "list", "all players", null, (g, b) => "_bArr(players())");
            Grp("Change a list");
            Stack("list_add", "lists", "add {x:text::raw} to {l:sock:list}", null, "_bPush(@l, @x)");
            Stack("list_add_num", "lists", "add the number {x:num:0} to {l:sock:list}", null, "_bPush(@l, @x)");
            Stack("list_insert", "lists", "insert {x:text::raw} at place {n:num:1} in {l:sock:list}", null, "_bInsert(@l, @n, @x)");
            Stack("list_remove_at", "lists", "remove item {n:num:1} of {l:sock:list}", null, "_bRemoveAt(@l, @n)");
            Stack("list_clear", "lists", "delete all items of {l:sock:list}", null, "_bClear(@l)");
            Stack("list_sort", "lists", "sort {l:sock:list}", null, "_bSort(@l)");
            Stack("list_reverse", "lists", "reverse {l:sock:list}", null, "_bReverse(@l)");
            Grp("Look in a list");
            Fn("list_item", "lists", "any", "item {n:num:1} of {l:sock:list}", null, (g, b) => g.Tpl(b, "_bItem(@l, @n)"));
            Fn("list_length", "lists", "number", "length of {l:sock:list}", null, (g, b) => g.Tpl(b, "_bLen(@l)"));
            Fn("list_has", "lists", "bool", "{l:sock:list} contains {x:text::raw}", null, (g, b) => g.Tpl(b, "_bListHas(@l, @x)"));
            Fn("list_index", "lists", "number", "place of {x:text::raw} in {l:sock:list}", null, (g, b) => g.Tpl(b, "_bIndex(@l, @x)"));
            Fn("list_random", "lists", "any", "random item of {l:sock:list}", null, (g, b) => g.Tpl(b, "_bRandItem(@l)"));
            Fn("list_sum", "lists", "number", "sum of {l:sock:list}", null, (g, b) => g.Tpl(b, "_bSum(@l)"));
            Fn("list_join", "lists", "text", "join {l:sock:list} with {sep:text:, }", null, (g, b) => g.Tpl(b, "_bJoinList(@l, @sep)"));
            Fn("list_this", "lists", "any", "that item", null, (g, b) => g.Loop(b, "item"));

            // ---------------------------------------------------------------- numbers
            Grp("Numbers");
            Fn("math_fn", "values", "number", "{fn:pick:mathfns} of {a:num:0}", null, (g, b) =>
            {
                string a = g.Ex(b, "a");
                switch (b.Field("fn", "absolute value"))
                {
                    case "round down": return "Math.floor(" + a + ")";
                    case "round up": return "Math.ceil(" + a + ")";
                    case "round": return "Math.round(" + a + ")";
                    case "square root": return "Math.sqrt(Math.max(0, " + a + "))";
                    case "sine": return "Math.sin(" + a + " * Math.PI / 180)";
                    case "cosine": return "Math.cos(" + a + " * Math.PI / 180)";
                    case "tangent": return "_bNum(Math.tan(" + a + " * Math.PI / 180))";
                    default: return "Math.abs(" + a + ")";
                }
            });
            Fn("math_fn2", "values", "number", "the {fn:pick:mathfns2} of {a:num:0} and {b:num:0}", null, (g, b) =>
            {
                string a = g.Ex(b, "a"), c = g.Ex(b, "b");
                switch (b.Field("fn", "smaller"))
                {
                    case "larger": return "Math.max(" + a + ", " + c + ")";
                    case "power": return "_bNum(Math.pow(" + a + ", " + c + "))";
                    case "angle": return "(Math.atan2(" + a + ", " + c + ") * 180 / Math.PI)";
                    default: return "Math.min(" + a + ", " + c + ")";
                }
            });
            Fn("round_to", "values", "number", "round {a:num:0} to {d:num:0:0:6} places", null, (g, b) => g.Tpl(b, "_bRound(@a, @d)"));
            Fn("clamp", "values", "number", "keep {x:num:0} between {a:num:0} and {b:num:10}", null, (g, b) => g.Tpl(b, "_bClamp(@x, @a, @b)"));
            Fn("map_range", "values", "number", "turn {x:num:0} from {a:num:0}..{b:num:10} into {c:num:0}..{d:num:100}", null, (g, b) => g.Tpl(b, "_bMap(@x, @a, @b, @c, @d)"));
            Fn("pi", "values", "number", "pi", null, (g, b) => "Math.PI");
            Fn("rand_decimal", "values", "number", "random decimal (0 to 1)", null, (g, b) => "random(0, 1)");
            Fn("distance_between", "values", "number", "distance between {a:sock:pos} and {b:sock:pos}", null, (g, b) => g.Tpl(b, "_bDist(@a, @b)"));
            Fn("direction_from", "values", "number", "direction from {a:sock:pos} to {b:sock:pos}", null, (g, b) => g.Tpl(b, "_bDir(@a, @b)"));

            // ---------------------------------------------------------------- text
            Grp("Words");
            Fn("join_many", "text", "text", "join {a:text} {b:text} {c:text} {d:text}", null, (g, b) => g.Tpl(b, "(String(@a) + String(@b) + String(@c) + String(@d))"));
            Fn("text_length", "text", "number", "length of {t:text}", null, (g, b) => g.Tpl(b, "String(@t).length"));
            Fn("text_letter", "text", "text", "letter {n:num:1} of {t:text}", null, (g, b) => g.Tpl(b, "_bLetter(@t, @n)"));
            Fn("text_replace", "text", "text", "replace {a:text} with {b:text} in {t:text}", null, (g, b) => g.Tpl(b, "_bReplace(@t, @a, @b)"));
            Fn("text_fn", "text", "text", "{fn:pick:textfns} {t:text}", null, (g, b) =>
            {
                string t = g.Ex(b, "t");
                switch (b.Field("fn", "uppercase"))
                {
                    case "lowercase": return "String(" + t + ").toLowerCase()";
                    case "trim": return "String(" + t + ").trim()";
                    default: return "String(" + t + ").toUpperCase()";
                }
            });
            Fn("text_before", "text", "text", "the words before {s:text} in {t:text}", null, (g, b) => g.Tpl(b, "_bBefore(@t, @s)"));
            Fn("text_after", "text", "text", "the words after {s:text} in {t:text}", null, (g, b) => g.Tpl(b, "_bAfterText(@t, @s)"));
            Fn("text_to_number", "text", "number", "{t:text} as a number", null, (g, b) => g.Tpl(b, "_bToNum(@t)"));
            Fn("number_to_text", "text", "text", "{n:num:0} as words with {d:num:0:0:10} decimals", null, (g, b) => g.Tpl(b, "_bFixed(@n, @d)"));
        }

        /// <summary>Puts the older blocks where the bigger palette wants them: a tab and a heading each.</summary>
        private static void Regroup()
        {
            Move("events", "Start and stop", "on_start", "on_stop");
            Move("events", "Players", "on_join", "on_leave", "on_death");
            Move("events", "Fights", "on_kill", "on_boss_appears", "on_boss_falls");
            Move("events", "Talk and time", "on_chat", "on_morning", "on_night", "every");

            Move("control", "Choices", "if", "if_else");
            Move("control", "Loops", "repeat", "for_player", "for_creature");
            Move("control", "Waiting", "after");
            Move("control", "Stopping", "stop_here");

            Move("values", "This event", "ev_player", "ev_killer", "ev_victim", "ev_boss", "ev_text", "ev_pos", "ev_biome", "ev_cause", "ev_day");
            Move("values", "This loop", "each_player", "each_creature");
            Move("values", "Numbers", "random_num", "math", "round", "chance");
            Move("values", "Logic", "compare", "and", "or", "not");
            Move("text", "Words", "join", "one_of", "text_test");
            Move("players", "Who", "is_player", "is_kind", "name_of", "kind_of", "health_of", "pos_of", "player_named");
            Move("players", "Items", "count_items", "has_item", "give", "take");
            Move("players", "Messages", "say");
            Move("players", "Health", "heal", "hurt");
            Move("players", "Movement", "teleport");
            Move("world", "Time and weather", "day_number", "time_of_day", "players_online", "is_night", "is_day", "weather_is", "weather_now", "weather", "weather_random", "set_time");
            Move("world", "Places", "pos_moved", "pos_random");
            Move("world", "Creatures and items", "spawn", "spawn_star", "drop_item");
            Move("actions", "Messages", "announce");
            Move("actions", "Debug", "log");
            Move("memory", "Variables", "set_var", "set_var_text", "change_var", "var_get");
            Move("memory", "Saved in the world", "the_world", "remember", "remember_text", "recall", "recall_text");

            // the event values that older blocks offered also work under the newer events
            AlsoUnder("ev_player", "on_piece_built", "on_pickup", "on_eat", "on_skill_up", "on_respawn", "on_command", "on_tamed");
            AlsoUnder("ev_pos", "on_hit");
        }

        private static void Move(string category, string group, params string[] ids)
        {
            foreach (string id in ids)
            {
                BlockDef d = Find(id);
                if (d == null) throw new InvalidOperationException("Regroup: no block called " + id);
                d.Category = category; d.Group = group;
            }
        }

        private static void AlsoUnder(string valueId, params string[] hats)
        {
            BlockDef d = Find(valueId);
            if (d == null || d.Scope == null) return;
            foreach (string h in hats) d.Scope.Add(h);
        }
    }
}
