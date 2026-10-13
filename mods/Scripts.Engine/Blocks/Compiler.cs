using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Scripts.Blocks
{
    /// <summary>Something wrong (Fatal: the program cannot run) or worth a look (not fatal), and the block to point at.</summary>
    internal sealed class Problem
    {
        internal Block Block;
        internal string Message = "";
        internal bool Fatal;
    }

    internal sealed class Compiled
    {
        internal string Js;                       // null when there is a fatal problem
        internal string[] Needs = new string[0];
        internal readonly List<Problem> Problems = new List<Problem>();
        internal int[] LineBlock = new int[0];     // block id for each line of Js (0: not from a block); the editor uses it to find the block an error came from
        internal bool Ok => Js != null;
    }

    /// <summary>
    /// Blocks to JavaScript. Safe by construction: every word the player typed goes in as an escaped string, every number is checked and
    /// printed by us, every dropdown choice is checked against its list, and every name (variables, my-blocks, inputs, local variables)
    /// becomes a hex identifier, so nothing a player types can become code. The result is the same text a person could type, and runs in
    /// the same sandbox with the same budgets.
    /// </summary>
    internal static class Compiler
    {
        internal static Compiled Compile(Program program)
        {
            var g = new Gen(program);
            g.Run();
            return g.Result();
        }
    }

    /// <summary>The helper code the generated script carries: only the helpers a script uses are written into it.</summary>
    internal static class Prelude
    {
        // Each line declares one helper (const _bName = ... or let _bName = ...). They do nothing when something is missing (a killer that
        // is not there, a list that is not a list), so blocks never throw.
        internal static readonly string[] Lines =
        {
            "const _bNoop = () => {};",
            "const _bP = x => !!x && x.isPlayer === true;",
            "const _bName = x => x ? String(x.name) : \"\";",
            "const _bKind = x => x ? String(x.prefab) : \"\";",
            "const _bHp = x => x && typeof x.hp === \"number\" ? x.hp : 0;",
            "const _bPos = x => x ? x.pos : null;",
            "const _bNum = x => typeof x === \"number\" && isFinite(x) ? x : 0;",
            "const _bDiv = (a, b) => b === 0 ? 0 : a / b;",
            "const _bMod = (a, b) => b === 0 ? 0 : a % b;",
            "const _bOff = (p, x, y, z) => p ? offset(p, x, y, z) : null;",
            "const _bAround = (p, r) => p ? around(p, r) : null;",
            "const _bNear = (p, r) => p ? nearbyCreatures(p, r) : [];",
            "const _bNearP = (p, r) => p ? nearbyPlayers(p, r) : [];",
            "const _bNearPieces = (p, r) => p ? nearbyPieces(p, r) : [];",
            "const _bOneOf = t => pick(String(t).split(\",\").map(s => s.trim()).filter(s => s)) || \"\";",
            "const _bSay = (w, t) => { if (_bP(w)) w.say(String(t)); };",
            "const _bGive = (w, item, n) => _bP(w) ? w.give(item, n) : 0;",
            "const _bTake = (w, item, n) => _bP(w) ? w.take(item, n) : 0;",
            "const _bCount = (w, item) => _bP(w) ? w.count(item) : 0;",
            "const _bHas = (w, item, n) => _bP(w) && w.has(item, n);",
            "const _bDrop = (item, n, p) => p ? dropItem(item, n, p) : 0;",
            "let _bLastSpawn = null;",
            "const _bSpawn = (c, p, l) => { _bLastSpawn = p ? (l === undefined ? spawn(c, p) : spawn(c, p, { level: l })) : null; return _bLastSpawn; };",
            "const _bHeal = (w, n) => w && typeof w.heal === \"function\" ? w.heal(n) : 0;",
            "const _bHurt = (w, n) => w && typeof w.damage === \"function\" ? w.damage(n) : 0;",
            "const _bTeleport = (w, p) => _bP(w) && p ? w.teleport(p) : false;",
            "const _bScope = x => typeof x === \"string\" ? x : (x && x.id ? String(x.id) : null);",
            "const _bGet = (x, k, d) => { const s = _bScope(x); return s === null ? d : store.get(s, String(k), d); };",
            "const _bSet = (x, k, v) => { const s = _bScope(x); if (s !== null) store.set(s, String(k), v); };",
            // waiting, and loops that wait
            "const _bAfter = (s, f) => { if (after(Math.max(0.05, _bNum(s)), f) < 0) log(\"A wait could not start: too many things are waiting at once.\"); };",
            "const _bLoop = (cond, body, done) => { let sync = false, again = false; const step = () => { if (sync) { again = true; return; } sync = true; do { again = false; if (!cond()) { sync = false; done(); return; } body(step); } while (again); sync = false; }; step(); };",
            // lists (500 items at most) and equality across numbers and words
            "const _bArr = x => Array.isArray(x) ? x.slice(0, 1000) : [];",
            "const _bEq = (a, b) => (a && typeof a === \"object\" && a.id !== undefined ? a.id : a) + \"\" === (b && typeof b === \"object\" && b.id !== undefined ? b.id : b) + \"\";",
            "const _bSplit = (t, sep) => String(t).split(sep === undefined ? \",\" : String(sep)).map(s => s.trim()).filter(s => s.length > 0).slice(0, 500);",
            "const _bSplitRaw = (t, sep) => String(sep).length ? String(t).split(String(sep)).slice(0, 500) : [String(t)];",
            "const _bPush = (l, x) => { if (Array.isArray(l) && l.length < 500) l.push(x); };",
            "const _bInsert = (l, n, x) => { if (Array.isArray(l) && l.length < 500) l.splice(Math.max(0, Math.min(l.length, Math.floor(_bNum(n)) - 1)), 0, x); };",
            "const _bRemoveAt = (l, n) => { if (Array.isArray(l)) { const i = Math.floor(_bNum(n)) - 1; if (i >= 0 && i < l.length) l.splice(i, 1); } };",
            "const _bClear = l => { if (Array.isArray(l)) l.length = 0; };",
            "const _bItem = (l, n) => { if (!Array.isArray(l)) return \"\"; const v = l[Math.floor(_bNum(n)) - 1]; return v === undefined ? \"\" : v; };",
            "const _bLen = l => Array.isArray(l) ? l.length : 0;",
            "const _bListHas = (l, x) => Array.isArray(l) && l.some(v => _bEq(v, x));",
            "const _bIndex = (l, x) => Array.isArray(l) ? l.findIndex(v => _bEq(v, x)) + 1 : 0;",
            "const _bRandItem = l => Array.isArray(l) && l.length ? pick(l) : \"\";",
            "const _bSort = l => { if (Array.isArray(l)) l.sort((a, b) => (typeof a === \"number\" && typeof b === \"number\") ? a - b : String(a) < String(b) ? -1 : String(a) > String(b) ? 1 : 0); };",
            "const _bReverse = l => { if (Array.isArray(l)) l.reverse(); };",
            "const _bJoinList = (l, sep) => Array.isArray(l) ? l.map(String).join(String(sep)) : \"\";",
            "const _bSum = l => Array.isArray(l) ? l.reduce((a, v) => a + (isFinite(Number(v)) ? Number(v) : 0), 0) : 0;",
            // math
            "const _bRound = (x, d) => { const m = Math.pow(10, Math.max(0, Math.min(6, Math.floor(_bNum(d))))); return Math.round(_bNum(x) * m) / m; };",
            "const _bClamp = (x, a, b) => Math.min(Math.max(_bNum(x), Math.min(_bNum(a), _bNum(b))), Math.max(_bNum(a), _bNum(b)));",
            "const _bMap = (x, a, b, c, d) => _bNum(b) === _bNum(a) ? _bNum(c) : _bNum(c) + (_bNum(x) - _bNum(a)) * (_bNum(d) - _bNum(c)) / (_bNum(b) - _bNum(a));",
            "const _bDist = (a, b) => a && b ? distance(a, b) : 0;",
            "const _bDir = (a, b) => a && b ? bearing(a, b) : 0;",
            // text
            "const _bLetter = (t, n) => { const s = String(t), i = Math.floor(_bNum(n)) - 1; return i >= 0 && i < s.length ? s[i] : \"\"; };",
            "const _bReplace = (t, a, b) => String(a).length ? String(t).split(String(a)).join(String(b)) : String(t);",
            "const _bToNum = t => { const n = parseFloat(String(t).replace(\",\", \".\")); return isFinite(n) ? n : 0; };",
            "const _bFixed = (n, d) => _bNum(n).toFixed(Math.max(0, Math.min(10, Math.floor(_bNum(d)))));",
            "const _bBefore = (t, s) => { const x = String(t), i = x.indexOf(String(s)); return i < 0 || !String(s).length ? x : x.slice(0, i); };",
            "const _bAfterText = (t, s) => { const x = String(t), i = x.indexOf(String(s)); return i < 0 || !String(s).length ? \"\" : x.slice(i + String(s).length); };",
            // players and creatures
            "const _bStat = (w, k) => w && typeof w[k] === \"number\" ? w[k] : 0;",
            "const _bFlag = (w, k) => !!(w && w[k]);",
            "const _bText = (w, k) => w && w[k] !== undefined && w[k] !== null ? String(w[k]) : \"\";",
            "const _bProp = (w, k) => w && w[k] !== undefined ? w[k] : null;",
            "const _bField = (o, k) => o && o[k] !== undefined && o[k] !== null ? o[k] : \"\";",
            "const _bSkill = (w, s) => _bP(w) && w.skill ? _bNum(w.skill(s)) : 0;",
            "const _bEquip = (w, slot) => { if (!_bP(w) || !w.equipped) return \"\"; const q = w.equipped(); return q && q[slot] ? String(q[slot]) : \"\"; };",
            "const _bInv = w => _bP(w) && w.inventory ? w.inventory().map(i => i.prefab) : [];",
            "const _bLook = w => _bP(w) ? lookingAt(w) : null;",
            "const _bAhead = (w, m) => w ? ahead(w, _bNum(m)) : null;",
            "const _bTalk = (w, t) => { if (w && w.talk) w.talk(String(t)); };",
            "const _bBannerTo = (w, t) => { if (_bP(w)) w.banner(String(t)); };",
            "const _bStam = (w, n) => { if (_bP(w)) w.setStamina(_bNum(n)); };",
            "const _bApply = (w, e, s) => w && w.applyEffect ? w.applyEffect(e, _bNum(s)) : false;",
            "const _bUnapply = (w, e) => w && w.removeEffect ? w.removeEffect(e) : false;",
            "const _bLaunchDir = (w, d, f) => { if (!w || !w.launch) return; const c = w.facing || { x: 0, y: 0, z: 1 }; w.launch(d === \"up\" ? { x: 0, y: 1, z: 0 } : d === \"backward\" ? { x: -c.x, y: 0.2, z: -c.z } : { x: c.x, y: 0.2, z: c.z }, _bNum(f)); };",
            "const _bLaunchAway = (w, p, f) => { if (!w || !w.launch || !p || !w.pos) return; const q = p.pos || p; w.launch({ x: w.pos.x - q.x, y: 0.4, z: w.pos.z - q.z }, _bNum(f)); };",
            "const _bTame = c => c && c.tame ? c.tame() : false;",
            "const _bRemoveC = c => { if (c && c.remove) c.remove(); };",
            "const _bNameC = (c, t) => { if (c && c.setName) c.setName(String(t)); };",
            // effects and the world
            "const _bFx = (n, p) => p ? effect(n, p) : false;",
            "const _bSound = (n, p) => p ? sound(n, p) : false;",
            "const _bLightning = p => { if (p) lightning(p); };",
            "const _bShake = (p, r, s) => { if (p) shake(p, _bNum(r), _bNum(s)); };",
            "const _bPing = (p, t) => { if (p) ping(p, String(t)); };",
            "const _bPlace = (n, p) => p ? world.spawnPiece(n, p) : null;",
            "const _bRemoveP = p => p && p.remove ? p.remove() : false;",
        };

        private static readonly Regex Declared = new Regex(@"^(?:const|let) (_b\w+)", RegexOptions.Compiled);

        /// <summary>The helper lines a script needs: the ones its code names, and the ones those name, in the order above.</summary>
        internal static List<string> Used(string code)
        {
            var names = new List<string>();
            foreach (string line in Lines) names.Add(Declared.Match(line).Groups[1].Value);
            var used = new bool[Lines.Length];
            var text = new StringBuilder(code);
            bool grew = true;
            while (grew)
            {
                grew = false;
                string all = text.ToString();
                for (int i = 0; i < Lines.Length; i++)
                {
                    if (used[i]) continue;
                    if (!Regex.IsMatch(all, @"\b" + names[i] + @"\b")) continue;
                    used[i] = true; grew = true;
                    text.Append('\n').Append(Lines[i]);
                }
            }
            var result = new List<string>();
            for (int i = 0; i < Lines.Length; i++) if (used[i]) result.Add(Lines[i]);
            return result;
        }
    }

    internal sealed partial class Gen
    {
        internal readonly Program Program;
        private readonly List<string> _lines = new List<string>();
        private readonly List<int> _lineBlock = new List<int>();
        private readonly List<Problem> _problems = new List<Problem>();
        private readonly HashSet<string> _needs = new HashSet<string>();
        private readonly List<KeyValuePair<string, string>> _loops = new List<KeyValuePair<string, string>>();
        private int _indent, _depth, _fresh;
        private string _hat = "";

        internal Gen(Program program) { Program = program; }

        // ---- the whole program ----

        internal void Run()
        {
            int total = Program.CountBlocks();
            if (total > Program.MaxBlocks) Error(null, "There are " + total + " blocks; a script can have " + Program.MaxBlocks + " at most.");
            CollectProcedures();
            ComputeAsync();
            // my blocks first (a call can come from anywhere), then everything that starts something
            foreach (Program.Top top in Program.Tops)
                if (top.Chain.Count > 0 && top.Chain[0].Type == "define") EmitProc(top.Chain[0]);
            foreach (Program.Top top in Program.Tops)
            {
                if (top.Chain.Count == 0) continue;
                Block first = top.Chain[0];
                BlockDef def = first.Def;
                if (def == null) { Error(first, "I don't know a block called '" + first.Type + "'."); continue; }
                if (def.Id == "define") continue;
                if (def.Shape != Shape.Hat)
                {
                    if (!def.Inert) Warn(first, "These blocks are not under a 'when' block, so they never run.");
                    continue;
                }
                BeginTop(def.Id);
                if ((def.Id == "on_hit" || def.Id == "on_stop") && FirstWait(first) is Block wait)
                    Error(wait, def.Id == "on_hit" ? "A hit is settled at once, so it can't wait." : "The script is stopping, so it can't wait.");
                Visit(first);
                for (int i = 1; i < top.Chain.Count; i++) Warn(top.Chain[i], "Put this inside the 'when' block, or it never runs.");
            }
            _hat = "";
        }

        /// <summary>A fresh start for a stack: no loops, no locals, no input names of a my-block.</summary>
        private void BeginTop(string hat)
        {
            _hat = hat;
            _depth = 0;
            _frames.Clear();
            _scopes.Clear();
            _scopes.Add(new HashSet<string>());
            _proc = null; _procParams = null; _procAsync = false;
        }

        internal Compiled Result()
        {
            var c = new Compiled();
            c.Problems.AddRange(_problems);
            c.Needs = Manifest.Capabilities.Where(n => _needs.Contains(n)).ToArray();
            if (_problems.Any(p => p.Fatal)) return c;

            var head = new List<string>
            {
                "// ==Script==",
                "// name:    " + OneLine(Program.Name, 40),
                "// version: 1.0",
                "// about:   " + OneLine(Program.About, 200),
                "// needs:   " + string.Join(", ", c.Needs),
                "// api:     1",
                "// ==/Script==",
                "// Made from blocks. Edit the blocks, not this file: the next save writes it again.",
                "",
            };
            head.AddRange(Prelude.Used(string.Join("\n", _lines)));
            foreach (string v in Program.Vars) head.Add("let " + Naming.VarId(v) + " = 0;   // " + OneLine(v, 20));
            head.Add("");

            var all = new StringBuilder();
            var map = new List<int>();
            foreach (string line in head) { all.Append(line).Append('\n'); map.Add(0); }
            for (int i = 0; i < _lines.Count; i++) { all.Append(_lines[i]).Append('\n'); map.Add(_lineBlock[i]); }
            c.Js = all.ToString();
            c.LineBlock = map.ToArray();
            return c;
        }

        private static string OneLine(string s, int max)
        {
            s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ');
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // ---- what the block definitions use ----

        internal void Line(Block b, string text)
        {
            _lines.Add(new string(' ', _indent * 2) + text);
            _lineBlock.Add(b != null ? b.Id : 0);
        }

        internal void Open(Block b, string text) { Line(b, text); _indent++; }

        internal void Close(string text, bool reopen = false)
        {
            _indent--;
            Line(null, text);
            if (reopen) _indent++;
        }

        internal string Fresh(string prefix) => "_" + prefix + (++_fresh);

        internal void PushLoop(string kind, string name) => _loops.Add(new KeyValuePair<string, string>(kind, name));
        internal void PopLoop(string kind) { for (int i = _loops.Count - 1; i >= 0; i--) if (_loops[i].Key == kind) { _loops.RemoveAt(i); return; } }

        internal string Loop(Block b, string kind)
        {
            for (int i = _loops.Count - 1; i >= 0; i--) if (_loops[i].Key == kind) return _loops[i].Value;
            Error(b, kind == "item" ? "This only works inside a 'for each item' block." : "This only works inside a 'for each " + kind + "' block.");
            return "null";
        }

        internal void Error(Block b, string message) => _problems.Add(new Problem { Block = b, Message = message, Fatal = true });
        internal void Warn(Block b, string message)
        {
            if (_problems.Any(p => p.Block == b && p.Message == message)) return;   // (a block's code can be made twice, e.g. for a wait)
            _problems.Add(new Problem { Block = b, Message = message, Fatal = false });
        }

        /// <summary>One block as lines (a stack block, a C block or a hat).</summary>
        private void Visit(Block b)
        {
            BlockDef def = b.Def;
            if (def == null) { Error(b, "I don't know a block called '" + b.Type + "'."); return; }
            if (def.Shape == Shape.Value) { Error(b, "'" + def.Label + "' gives an answer; it goes in a socket, not in a stack."); return; }
            if (def.Inert) { Line(b, "// " + Comment(b.Field("text"))); return; }
            if (def.Scope != null && def.Shape != Shape.Hat && !def.Scope.Contains(_hat)) { Error(b, "'" + def.Label.Replace("{do:body}", "") + "' is not available under this 'when' block."); return; }
            if (def.Needs != null) _needs.Add(def.Needs);
            if (_depth > Program.MaxDepth) { Error(b, "Blocks are nested too deep."); return; }
            if (def.Shape != Shape.Hat && IsAsync(b)) { Error(b, "This block waits, and can't be used here."); return; }
            _depth++;
            try { def.Emit(this, b); }
            catch (Exception e) { Error(b, "Could not make code from this block: " + e.Message); }
            _depth--;
        }

        /// <summary>A note's words as a comment on one line (it can't end the line, so it can't be code).</summary>
        private static string Comment(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "") sb.Append(c < 32 || c == (char)0x2028 || c == (char)0x2029 ? ' ' : c);
            string s = sb.ToString();
            return s.Length > 160 ? s.Substring(0, 160) : s;
        }

        /// <summary>The blocks inside a body, one after another (a hat's, a closure's, a plain C block's).</summary>
        internal void Body(Block b, string key)
        {
            if (b.Bodies.TryGetValue(key, out List<Block> list)) SeqScoped(list, null);
        }

        /// <summary>A template with @key where a slot's code goes: "_bGive(@who, @item, @n)".</summary>
        internal string Tpl(Block b, string template)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < template.Length; i++)
            {
                if (template[i] != '@') { sb.Append(template[i]); continue; }
                int j = i + 1;
                while (j < template.Length && (char.IsLetterOrDigit(template[j]) || template[j] == '_')) j++;
                string key = template.Substring(i + 1, j - i - 1);
                sb.Append(SlotCode(b, key));
                i = j - 1;
            }
            return sb.ToString();
        }

        private string SlotCode(Block b, string key)
        {
            Slot slot = b.Def.SlotOf(key);
            if (slot == null) { Error(b, "Internal: no slot '" + key + "'."); return "null"; }
            return slot.Kind == SlotKind.Pick ? Pick(b, key) : Ex(b, key);
        }

        // ---- values ----

        /// <summary>The code for a slot: the block plugged into it, or what was typed in it.</summary>
        internal string Ex(Block b, string key)
        {
            Slot slot = b.Def.SlotOf(key);
            if (slot == null) { Error(b, "Internal: no slot '" + key + "'."); return "null"; }
            if (b.Inputs.TryGetValue(key, out Block child)) return Plugged(b, slot, child);
            switch (slot.Kind)
            {
                case SlotKind.Num:
                    {
                        string typed = b.Field(key, slot.Default);
                        string range = OutOfRange(typed, slot);
                        if (range != null) Warn(b, range);
                        return Number(typed, slot);
                    }
                case SlotKind.Text: return Str(Program.Clip(b.Field(key, slot.Default), 120));
                case SlotKind.Socket:
                    if (slot.Optional) return "undefined";
                    Error(b, "Fill in the empty slot in '" + OneLine(b.Def.Label.Replace("{do:body}", ""), 60) + "'.");
                    return "null";
                default: return "null";
            }
        }

        /// <summary>Whether an optional socket has something in it.</summary>
        internal bool Has(Block b, string key) => b.Inputs.ContainsKey(key);

        private string Plugged(Block parent, Slot slot, Block child)
        {
            BlockDef def = child.Def;
            if (def == null) { Error(child, "I don't know a block called '" + child.Type + "'."); return "null"; }
            if (def.Shape != Shape.Value) { Error(child, "'" + def.Label.Replace("{do:body}", "") + "' can't go in a socket."); return "null"; }
            if (def.Scope != null && !def.Scope.Contains(_hat)) { Error(child, "'" + def.Label + "' is not available under this 'when' block."); return "null"; }
            if (!slot.Raw && !BlockDef.Fits(slot.Type, def.Type)) { Error(child, "'" + def.Label + "' gives " + Article(def.Type) + " but this slot wants " + Article(slot.Type) + "."); return "null"; }
            if (_depth > Program.MaxDepth) { Error(child, "Blocks are nested too deep."); return "null"; }
            if (def.Needs != null) _needs.Add(def.Needs);
            _depth++;
            string js;
            try { js = def.Expr(this, child); }
            catch (Exception e) { Error(child, "Could not make code from this block: " + e.Message); js = "null"; }
            _depth--;
            if (slot.Raw) return js;
            if (slot.Type == "text" && def.Type != "text") return "String(" + js + ")";
            if (slot.Type == "number" && def.Type == "any") return "_bNum(" + js + ")";
            return js;
        }

        private static string Article(string type)
        {
            switch (type)
            {
                case "number": return "a number";
                case "text": return "words";
                case "bool": return "a yes/no";
                case "pos": return "a place";
                case "player": return "a player";
                case "creature": return "a creature";
                case "actor": return "a player or creature";
                case "scope": return "a player, creature or the world";
                case "list": return "a list";
                case "piece": return "a piece";
                case "item": return "an item";
                default: return "something";
            }
        }

        /// <summary>A dropdown choice as a string literal, checked against its list.</summary>
        internal string Pick(Block b, string key)
        {
            Slot slot = b.Def.SlotOf(key);
            string[] choices = BlockLists.Get(slot.List, Program);
            string value = b.Fields.TryGetValue(key, out string v) ? v : slot.Default;
            if (value.Length == 0 && slot.List != "creaturesAny" && choices.Length > 0) value = choices[0];
            if (!choices.Contains(value)) { Error(b, "'" + OneLine(value, 30) + "' is not one of the choices here."); return "\"\""; }
            return Str(value);
        }

        /// <summary>The identifier of a variable chosen in a dropdown.</summary>
        internal string VarId(Block b, string key)
        {
            string name = b.Field(key);
            if (!Program.Vars.Contains(name)) { Error(b, name.Length == 0 ? "Pick a variable (make one first)." : "There is no variable called '" + OneLine(name, 20) + "'."); return "_bBad"; }
            return Naming.VarId(name);
        }

        internal static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    case (char)0x2028: case (char)0x2029: sb.Append(' '); break;
                    default: if (c < 32) sb.Append(' '); else sb.Append(c); break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>
        /// Why a typed number does not fit its slot ("at most 10 here"), or null when it fits (or is not a number: that falls back to the
        /// default, as before). The code still runs with the nearest number that fits, so this is a warning the editor shows, not a stop.
        /// </summary>
        internal static string OutOfRange(string text, Slot slot)
        {
            if (slot == null || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d)) return null;
            d = Math.Round(d, 4);
            if (d > slot.Max) return "This number is " + Fmt(d) + ", but here it can be at most " + Fmt(slot.Max) + ", so it runs as " + Fmt(slot.Max) + ".";
            if (d < slot.Min) return "This number is " + Fmt(d) + ", but here it must be at least " + Fmt(slot.Min) + ", so it runs as " + Fmt(slot.Min) + ".";
            return null;
        }

        private static string Fmt(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);

        internal static string Number(string text, Slot slot)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d))
                double.TryParse(slot.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            d = Math.Max(slot.Min, Math.Min(slot.Max, Math.Round(d, 4)));
            if (d == 0) d = 0;   // (no minus zero)
            string s = d.ToString("0.####", CultureInfo.InvariantCulture);
            return d < 0 ? "(" + s + ")" : s;
        }
    }
}
