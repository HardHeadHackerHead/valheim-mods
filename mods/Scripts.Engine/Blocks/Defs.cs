using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Scripts.Blocks
{
    internal enum Shape { Hat, Stack, C, Value }
    internal enum SlotKind { Num, Text, Pick, Socket, Body }

    /// <summary>One place in a block's label: a number, some text, a dropdown, a socket for another block, or a body for blocks inside.</summary>
    internal sealed class Slot
    {
        internal string Key = "", Type = "any", List = "", Default = "";
        internal SlotKind Kind;
        internal bool Optional;   // a socket that may stay empty (an input of a my-block, a branch of a chain)
        internal bool Raw;        // a text slot whose plugged block keeps its own kind (a list's items stay numbers, players, places) instead of becoming words
        internal double Min = double.MinValue, Max = double.MaxValue;
    }

    /// <summary>A piece of a block's label: plain words or a slot.</summary>
    internal sealed class Part
    {
        internal string Text;
        internal Slot Slot;
    }

    /// <summary>
    /// What a kind of block is: where it shows in the palette, its shape, its words and slots, what power it needs, and how it becomes
    /// JavaScript. One list (Defs) drives the palette, the validation and the compiler, so a block can't be offered that the compiler does
    /// not know. Values (the round blocks that give an answer) have a type; sockets only take what fits.
    /// </summary>
    internal sealed class BlockDef
    {
        internal string Id = "", Category = "", Group = "", Label = "", Needs;   // Group: a heading inside the palette tab (optional)
        internal Shape Shape;
        internal string Type = "any";                      // a Value block's answer: number, text, bool, actor, player, creature, pos, scope, any
        internal bool Danger, Terminal;                    // red in the permission list / nothing can follow it in a stack
        internal bool Inert;                               // a note: it makes no code and a loose one is not a warning
        internal HashSet<string> Scope;                    // blocks that only make sense under some hats (null: anywhere)
        internal Action<Gen, Block> Emit;                  // Hat, Stack, C
        internal Func<Gen, Block, string> Expr;            // Value
        private List<Part> _parts;

        internal List<Part> Parts => _parts ?? (_parts = ParseLabel(Label));

        internal Slot SlotOf(string key) => Parts.Where(p => p.Slot != null && p.Slot.Key == key).Select(p => p.Slot).FirstOrDefault();

        /// <summary>
        /// "give {who:sock:player} {n:num:1:1:10} {item:pick:items}": words and {key:kind:args} slots. num: default:min:max. text: default.
        /// pick: list:default. sock: type. body: nothing more. Translations use the same markup, and must keep the same slot keys.
        /// </summary>
        internal static List<Part> ParseLabel(string label)
        {
            var parts = new List<Part>();
            int i = 0;
            while (i < label.Length)
            {
                int open = label.IndexOf('{', i);
                if (open < 0) { parts.Add(new Part { Text = label.Substring(i) }); break; }
                if (open > i) parts.Add(new Part { Text = label.Substring(i, open - i) });
                int close = label.IndexOf('}', open);
                if (close < 0) { parts.Add(new Part { Text = label.Substring(open) }); break; }
                string[] a = label.Substring(open + 1, close - open - 1).Split(':');
                var slot = new Slot { Key = a[0] };
                string kind = a.Length > 1 ? a[1] : "text";
                switch (kind)
                {
                    case "num":
                        slot.Kind = SlotKind.Num; slot.Type = "number";
                        slot.Default = a.Length > 2 ? a[2] : "0";
                        if (a.Length > 3) double.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out slot.Min);
                        if (a.Length > 4) double.TryParse(a[4], NumberStyles.Float, CultureInfo.InvariantCulture, out slot.Max);
                        break;
                    case "text": slot.Kind = SlotKind.Text; slot.Type = "text"; slot.Default = a.Length > 2 ? a[2] : ""; slot.Raw = a.Length > 3 && a[3] == "raw"; break;
                    case "pick": slot.Kind = SlotKind.Pick; slot.List = a.Length > 2 ? a[2] : ""; slot.Default = a.Length > 3 ? a[3] : ""; break;
                    case "sock":
                        slot.Kind = SlotKind.Socket;
                        slot.Type = a.Length > 2 ? a[2] : "any";
                        if (slot.Type.EndsWith("?")) { slot.Optional = true; slot.Type = slot.Type.Substring(0, slot.Type.Length - 1); }
                        break;
                    case "body": slot.Kind = SlotKind.Body; break;
                    default: slot.Kind = SlotKind.Text; slot.Type = "text"; break;
                }
                parts.Add(new Part { Slot = slot });
                i = close + 1;
            }
            return parts;
        }

        /// <summary>Whether a block that answers with valueType fits a socket of slotType.</summary>
        internal static bool Fits(string slotType, string valueType)
        {
            if (slotType == valueType || slotType == "any" || valueType == "any") return true;
            switch (slotType)
            {
                case "text": return valueType == "number" || valueType == "bool";
                case "pos": return valueType == "actor" || valueType == "player" || valueType == "creature" || valueType == "piece";
                case "actor": return valueType == "player" || valueType == "creature";
                case "player": return valueType == "actor";
                case "creature": return valueType == "actor";
                case "scope": return valueType == "actor" || valueType == "player" || valueType == "creature";
                case "list": return valueType == "list";
                default: return false;
            }
        }
    }

    /// <summary>The blocks. Order is the palette's order inside each category.</summary>
    internal static partial class Defs
    {
        internal static readonly string[] Categories = { "events", "control", "values", "text", "lists", "players", "world", "effects", "actions", "memory", "functions" };

        private static string _group = "";
        private static void Grp(string group) { _group = group; }
        internal static readonly List<BlockDef> All = new List<BlockDef>();
        private static readonly Dictionary<string, BlockDef> ById = new Dictionary<string, BlockDef>();

        internal static BlockDef Find(string id) => id != null && ById.TryGetValue(id, out BlockDef d) ? d : null;

        private static HashSet<string> Under(params string[] hats) => new HashSet<string>(hats);

        private static BlockDef Add(BlockDef d) { if (d.Group.Length == 0) d.Group = _group; All.Add(d); ById[d.Id] = d; return d; }

        private static BlockDef Hat(string id, string label, string provides, Action<Gen, Block> emit) =>
            Add(new BlockDef { Id = id, Category = "events", Shape = Shape.Hat, Label = label + "{do:body}", Emit = emit });

        private static BlockDef Stack(string id, string category, string label, string needs, string call, bool danger = false, bool terminal = false) =>
            Add(new BlockDef { Id = id, Category = category, Shape = Shape.Stack, Label = label, Needs = needs, Danger = danger, Terminal = terminal, Emit = (g, b) => g.Line(b, g.Tpl(b, call) + ";") });

        private static BlockDef Value(string id, string type, string label, string needs, string expr, HashSet<string> scope = null, string category = "values") =>
            Add(new BlockDef { Id = id, Category = category, Shape = Shape.Value, Type = type, Label = label, Needs = needs, Scope = scope, Expr = (g, b) => g.Tpl(b, expr) });

        private static void Handler(Gen g, Block b, string open)
        {
            g.Open(b, open);
            g.Body(b, "do");
            g.Close("});");
        }

        static Defs()
        {
            // ---- events: what starts a stack ----
            Hat("on_start", "when this script starts", "", (g, b) => Handler(g, b, "on(\"start\", () => {"));
            Hat("on_stop", "when this script stops", "", (g, b) => Handler(g, b, "on(\"stop\", () => {"));
            Hat("on_join", "when a player joins", "player", (g, b) => Handler(g, b, "on(\"playerJoin\", e => {"));
            Hat("on_leave", "when a player leaves", "player", (g, b) => Handler(g, b, "on(\"playerLeave\", e => {"));
            Hat("on_death", "when a player dies", "player", (g, b) => Handler(g, b, "on(\"playerDeath\", e => {"));
            Hat("on_kill", "when {creature:pick:creaturesAny} is killed", "killer", (g, b) =>
            {
                g.Open(b, "on(\"kill\", e => {");
                string kind = g.Pick(b, "creature");
                if (kind.Length > 2) g.Line(b, "if (_bKind(e.victim) !== " + kind + ") return;");
                g.Body(b, "do");
                g.Close("});");
            });
            Hat("on_boss_appears", "when a boss appears", "boss", (g, b) => Handler(g, b, "on(\"bossSpawn\", e => {"));
            Hat("on_boss_falls", "when a boss falls", "boss", (g, b) => Handler(g, b, "on(\"bossDeath\", e => {"));
            Hat("on_chat", "when someone says {text:text}", "player", (g, b) =>
            {
                g.Open(b, "on(\"chat\", e => {");
                string text = g.Ex(b, "text");
                if (text != "\"\"") g.Line(b, "if (!e.text.toLowerCase().includes(String(" + text + ").toLowerCase())) return;");
                g.Body(b, "do");
                g.Close("});");
            });
            Hat("on_morning", "when morning begins", "day", (g, b) => Handler(g, b, "on(\"dayStart\", e => {"));
            Hat("on_night", "when night falls", "day", (g, b) => Handler(g, b, "on(\"nightStart\", e => {"));
            Hat("every", "every {n:num:20:1:3600} {unit:pick:units}", "", (g, b) =>
            {
                string n = g.Ex(b, "n");
                g.Open(b, "every(" + (g.Pick(b, "unit") == "\"minutes\"" ? "minutes(" + n + ")" : n) + ", () => {");
                g.Body(b, "do");
                g.Close("});");
            });

            // ---- control ----
            Add(new BlockDef { Id = "if", Category = "control", Shape = Shape.C, Label = "if {cond:sock:bool}{do:body}", Emit = (g, b) => g.EmitIfFamily(b, null) });
            Add(new BlockDef { Id = "if_else", Category = "control", Shape = Shape.C, Label = "if {cond:sock:bool}{do:body}otherwise{else:body}", Emit = (g, b) => g.EmitIfFamily(b, null) });
            Add(new BlockDef { Id = "repeat", Category = "control", Shape = Shape.C, Label = "repeat {n:num:3:1:1000} times{do:body}", Emit = (g, b) => g.EmitLoop(b, false, null) });
            Add(new BlockDef { Id = "for_player", Category = "control", Shape = Shape.C, Label = "for each player{do:body}", Emit = (g, b) => g.EmitLoop(b, false, null) });
            Add(new BlockDef { Id = "for_creature", Category = "control", Shape = Shape.C, Label = "for each creature within {r:num:20:1:50} of {pos:sock:pos}{do:body}", Emit = (g, b) => g.EmitLoop(b, false, null) });
            Add(new BlockDef
            {
                Id = "after", Category = "control", Shape = Shape.C, Label = "after {n:num:5:0:3600} {unit:pick:units}{do:body}",
                Emit = (g, b) =>
                {
                    string n = g.Ex(b, "n");
                    g.Open(b, "after(" + (g.Pick(b, "unit") == "\"minutes\"" ? "minutes(" + n + ")" : n) + ", () => {");
                    g.Closure(() => g.Body(b, "do"));
                    g.Close("});");
                },
            });
            Add(new BlockDef { Id = "stop_here", Category = "control", Shape = Shape.Stack, Label = "stop here", Terminal = true, Emit = (g, b) => g.Line(b, g.StopCode()) });

            // ---- values: what the event tells (round blocks) ----
            Value("ev_player", "player", "the player", null, "e.player", Under("on_join", "on_leave", "on_death", "on_chat"));
            Value("ev_killer", "actor", "the killer", null, "e.killer", Under("on_kill", "on_death", "on_boss_falls"));
            Value("ev_victim", "actor", "the one killed", null, "e.victim", Under("on_kill"));
            Value("ev_boss", "creature", "the boss", null, "e.boss", Under("on_boss_appears", "on_boss_falls"));
            Value("ev_text", "text", "what was said", null, "e.text", Under("on_chat"));
            Value("ev_pos", "pos", "where it happened", null, "e.pos", Under("on_kill", "on_death", "on_boss_appears", "on_boss_falls"));
            Value("ev_biome", "text", "the biome", null, "e.biome", Under("on_death", "on_boss_appears", "on_boss_falls"));
            Value("ev_cause", "text", "the cause of death", null, "e.cause", Under("on_death"));
            Value("ev_day", "number", "the day", null, "e.day", Under("on_morning", "on_night"));
            Add(new BlockDef { Id = "each_player", Category = "values", Shape = Shape.Value, Type = "player", Label = "that player", Expr = (g, b) => g.Loop(b, "player") });
            Add(new BlockDef { Id = "each_creature", Category = "values", Shape = Shape.Value, Type = "creature", Label = "that creature", Expr = (g, b) => g.Loop(b, "creature") });

            // ---- values: the world ----
            Value("day_number", "number", "the day number", null, "day()");
            Value("time_of_day", "number", "time of day (0 to 1)", null, "timeOfDay()");
            Value("players_online", "number", "players online", null, "players().length");
            Value("is_night", "bool", "it is night", null, "isNight()");
            Value("is_day", "bool", "it is day", null, "isDay()");
            Value("weather_is", "bool", "the weather is {kind:pick:weathers}", null, "(weather() === @kind)");
            Value("weather_now", "text", "the weather", null, "weather()");
            Value("player_named", "player", "the player named {name:text}", null, "findPlayer(@name)");

            // ---- values: numbers, text and logic ----
            Value("random_num", "number", "a random number from {a:num:1} to {b:num:10}", null, "randomInt(@a, @b)");
            Add(new BlockDef
            {
                Id = "math", Category = "values", Shape = Shape.Value, Type = "number", Label = "{a:num:0} {op:pick:mathops} {b:num:0}",
                Expr = (g, b) =>
                {
                    string a = g.Ex(b, "a"), c = g.Ex(b, "b");
                    switch (b.Field("op", "+"))
                    {
                        case "-": return "(" + a + " - " + c + ")";
                        case "x": return "(" + a + " * " + c + ")";
                        case "/": return "_bDiv(" + a + ", " + c + ")";
                        case "mod": return "_bMod(" + a + ", " + c + ")";
                        default: return "(" + a + " + " + c + ")";
                    }
                },
            });
            Value("round", "number", "round {a:num:0}", null, "Math.round(@a)");
            Value("chance", "bool", "{p:num:10:0:100} % chance", null, "chance(@p / 100)");
            Add(new BlockDef
            {
                Id = "compare", Category = "values", Shape = Shape.Value, Type = "bool", Label = "{a:num:0} {op:pick:cmp} {b:num:0}",
                Expr = (g, b) =>
                {
                    string a = g.Ex(b, "a"), c = g.Ex(b, "b");
                    string op = b.Field("op", "=");
                    string js = op == "=" ? "===" : op == "!=" ? "!==" : op;
                    return "(" + a + " " + js + " " + c + ")";
                },
            });
            Add(new BlockDef
            {
                Id = "text_test", Category = "values", Shape = Shape.Value, Type = "bool", Label = "{a:text} {op:pick:textops} {b:text}",
                Expr = (g, b) =>
                {
                    string a = "String(" + g.Ex(b, "a") + ").toLowerCase()", c = "String(" + g.Ex(b, "b") + ").toLowerCase()";
                    switch (b.Field("op", "is"))
                    {
                        case "contains": return "(" + a + ".includes(" + c + "))";
                        case "starts with": return "(" + a + ".startsWith(" + c + "))";
                        default: return "(" + a + " === " + c + ")";
                    }
                },
            });
            Value("and", "bool", "{a:sock:bool} and {b:sock:bool}", null, "(@a && @b)");
            Value("or", "bool", "{a:sock:bool} or {b:sock:bool}", null, "(@a || @b)");
            Value("not", "bool", "not {a:sock:bool}", null, "(!@a)");
            Value("join", "text", "join {a:text} {b:text}", null, "(String(@a) + String(@b))");
            Value("one_of", "text", "a random one of {list:text:red, green, blue}", null, "_bOneOf(@list)");

            // ---- values: players and creatures ----
            Value("is_player", "bool", "{who:sock:actor} is a player", null, "_bP(@who)");
            Value("is_kind", "bool", "{who:sock:actor} is a {kind:pick:creaturesAny}", null, "(_bKind(@who) === @kind)");
            Value("name_of", "text", "name of {who:sock:actor}", null, "_bName(@who)");
            Value("kind_of", "text", "kind of {who:sock:actor}", null, "_bKind(@who)");
            Value("health_of", "number", "health of {who:sock:actor}", null, "_bHp(@who)");
            Value("pos_of", "pos", "position of {who:sock:actor}", null, "_bPos(@who)");
            Value("pos_moved", "pos", "{pos:sock:pos} moved by {dx:num:0} {dy:num:0} {dz:num:0}", null, "_bOff(@pos, @dx, @dy, @dz)");
            Value("pos_random", "pos", "a random spot within {r:num:10:1:200} of {pos:sock:pos}", null, "_bAround(@pos, @r)");
            Value("count_items", "number", "how many {item:pick:items} {who:sock:player} has", "items", "_bCount(@who, @item)");
            Value("has_item", "bool", "{who:sock:player} has at least {n:num:1:1:1000} {item:pick:items}", "items", "_bHas(@who, @item, @n)");

            // ---- actions ----
            Stack("say", "actions", "say {text:text:Hello!} to {who:sock:player}", "messages", "_bSay(@who, @text)");
            Stack("announce", "actions", "announce {text:text:Hello!}", "messages", "broadcast(@text)");
            Stack("log", "actions", "write {text:text} in the log", null, "log(@text)");
            Stack("give", "actions", "give {who:sock:player} {n:num:1:1:10} {item:pick:items}", "items", "_bGive(@who, @item, @n)");
            Stack("take", "actions", "take {n:num:1:1:10} {item:pick:items} from {who:sock:player}", "takeItems", "_bTake(@who, @item, @n)", danger: true);
            Stack("drop_item", "actions", "drop {n:num:1:1:10} {item:pick:items} at {pos:sock:pos}", "spawn", "_bDrop(@item, @n, @pos)");
            Stack("spawn", "actions", "spawn a {creature:pick:creatures} near {pos:sock:pos}", "spawn", "_bSpawn(@creature, @pos)");
            Stack("spawn_star", "actions", "spawn a {stars:num:1:1:3}-star {creature:pick:creatures} near {pos:sock:pos}", "spawn", "_bSpawn(@creature, @pos, @stars)");
            Stack("weather", "actions", "set the weather to {kind:pick:weathers} for {n:num:2:1:30} minutes", "weather", "world.setWeather(@kind, @n * 60)");
            Stack("weather_random", "actions", "turn the weather to a random one for {n:num:2:1:30} minutes", "weather", "world.setWeather(pick([\"rain\", \"mist\", \"storm\"]), @n * 60)");
            Add(new BlockDef
            {
                Id = "set_time", Category = "actions", Shape = Shape.Stack, Label = "set the time to {t:pick:times}", Needs = "weather",
                Emit = (g, b) =>
                {
                    string t = b.Field("t", "morning");
                    string fraction = t == "noon" ? "0.5" : t == "evening" ? "0.75" : t == "midnight" ? "0.0" : "0.25";
                    if (!BlockLists.Has("times", t, g.Program)) g.Error(b, "there is no time called '" + t + "'.");
                    g.Line(b, "world.setTime(" + fraction + ");");
                },
            });
            Stack("heal", "actions", "heal {who:sock:actor} by {n:num:10:1:500}", "combat", "_bHeal(@who, @n)");
            Stack("hurt", "actions", "hurt {who:sock:actor} by {n:num:10:1:500}", "combat", "_bHurt(@who, @n)");
            Stack("teleport", "actions", "teleport {who:sock:player} to {pos:sock:pos}", "teleport", "_bTeleport(@who, @pos)", danger: true);

            // ---- memory ----
            Add(new BlockDef
            {
                Id = "set_var", Category = "memory", Shape = Shape.Stack, Label = "set {v:pick:var} to {val:num:0}",
                Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = " + g.Ex(b, "val") + ";"),
            });
            Add(new BlockDef
            {
                Id = "set_var_text", Category = "memory", Shape = Shape.Stack, Label = "set {v:pick:var} to the words {val:text}",
                Emit = (g, b) => g.Line(b, g.VarId(b, "v") + " = " + g.Ex(b, "val") + ";"),
            });
            Add(new BlockDef
            {
                Id = "change_var", Category = "memory", Shape = Shape.Stack, Label = "change {v:pick:var} by {n:num:1}",
                Emit = (g, b) => { string v = g.VarId(b, "v"); g.Line(b, v + " = _bNum(" + v + ") + " + g.Ex(b, "n") + ";"); },
            });
            Add(new BlockDef
            {
                Id = "var_get", Category = "memory", Shape = Shape.Value, Type = "any", Label = "{v:pick:var}",
                Expr = (g, b) => g.VarId(b, "v"),
            });
            Value("the_world", "scope", "the world", null, "\"world\"", null, "memory");
            Stack("remember", "memory", "remember {val:num:0} for {who:sock:scope} as {name:text:score}", "store", "_bSet(@who, @name, @val)");
            Stack("remember_text", "memory", "remember the words {val:text} for {who:sock:scope} as {name:text:note}", "store", "_bSet(@who, @name, @val)");
            Value("recall", "any", "recall {name:text:score} of {who:sock:scope}", "store", "_bGet(@who, @name, 0)", null, "memory");
            Value("recall_text", "any", "recall the words {name:text:note} of {who:sock:scope}", "store", "_bGet(@who, @name, \"\")", null, "memory");

            Language();
            Api();
            Regroup();
        }

        internal static IEnumerable<BlockDef> In(string category) => All.Where(d => d.Category == category);
    }
}
