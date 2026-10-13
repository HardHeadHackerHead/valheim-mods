using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Scripts.Blocks;
using Scripts.Collab;
using TMPro;
using UnityEngine;

namespace Scripts.Editor
{
    /// <summary>
    /// The block editor itself: a palette of blocks on the left, a workspace on the right, and the dragging between them. It draws with a
    /// Painter and answers a pointer (down, move, up, wheel) in the editor's own 1280 x 720 space, so the same code runs on a screen in the
    /// world (the Terminal) and in an ordinary window. It changes the Program it is given and says so (Changed); saving, compiling and
    /// running are the Screen's business.
    ///
    /// How it works: every picture it measures each block (Size), draws it (DrawBlock), and records, in the order drawn, what a pointer
    /// can hit (Nodes) and where a dragged block could be dropped (Targets). The next pointer event looks at those lists, so there is
    /// no second copy of the layout to keep right.
    ///
    /// Two spaces: the screen (the palette, the menus, the hand) and the workspace ("w-space"), which is the screen at zoom 1 and is scaled
    /// by the painter about the workspace's top left when zoomed. Everything recorded for the workspace (nodes, targets, positions) is in
    /// w-space; ToWork and FromWork convert. Sizes are measured once and kept until the program changes (a version number), and only what
    /// can be seen is drawn, so the work per picture follows the screen, not the size of the program.
    /// </summary>
    internal sealed class BlockView
    {
        // ---- what the Screen sets ----
        internal Program Prog = new Program();
        internal Rect PaletteRect = new Rect(0f, 56f, 300f, 592f), WorkRect = new Rect(300f, 56f, 660f, 592f);
        internal Action Changed = () => { };
        internal Action<string, string, int, Action<string>> Ask = (title, current, limit, done) => { };
        internal Action<string> Note = s => { };
        internal Func<BlockDef, List<Part>> PartsOf = d => d.Parts;
        internal Func<string, string> Words = s => s;
        internal bool ReadOnly;
        internal Dictionary<int, bool> Problems = new Dictionary<int, bool>();   // block id -> the problem is fatal
        internal string Category = "events";
        internal string Query = "";                                              // the palette search
        internal Block Selected;

        // ---- working together (Mp/CollabClient.cs): set by the screen while a script is open live ----
        internal Func<bool> UndoHook, RedoHook;               // undo and redo of this editor's own work, through the session (true: done)
        internal Func<bool?> CanUndoHook, CanRedoHook;        // whether the session can; null: use the local undo
        internal Action<List<Block>> DragStarted;             // blocks of the workspace were picked up (the screen tells the others they are held)
        internal Action DragEnded;                            // the drop is done (after the change was sent)
        internal Func<Block, string> HeldBy;                  // the name of another editor who holds this block, or null
        internal readonly List<Mark> Others = new List<Mark>();   // what the others are doing, drawn over the workspace

        /// <summary>Another editor, as the view draws them: a coloured outline on the block they have selected, dimmed blocks they are moving, and their pointer.</summary>
        internal sealed class Mark
        {
            internal string Name = "", Selected = "";
            internal int Color;
            internal bool HasPointer;
            internal float X, Y;
            internal readonly List<string> Grabbed = new List<string>();
        }

        private readonly Painter _p;

        // ---- what a picture finds ----
        private enum NodeKind { Block, Slot, Tab, Button, Toggle, Scroll }
        private sealed class Node { internal NodeKind Kind; internal Rect R; internal Block B; internal Slot S; internal bool Palette; internal string Text; internal Action Do; internal Program.Top T; }
        private enum TargetKind { After, BodyStart, Socket }
        private sealed class Target { internal TargetKind Kind; internal List<Block> List; internal int Index; internal Vector2 P; internal Rect R; internal Block Owner; internal Slot S; }

        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Target> _targets = new List<Target>();
        private readonly Dictionary<Block, Vector2> _size = new Dictionary<Block, Vector2>();
        private readonly Dictionary<Block, List<Seg>> _segs = new Dictionary<Block, List<Seg>>();
        private readonly Dictionary<Block, Vector2> _where = new Dictionary<Block, Vector2>();   // where each block was drawn, in workspace space
        private readonly Dictionary<string, Block> _templates = new Dictionary<string, Block>();
        private readonly Dictionary<string, Block> _chips = new Dictionary<string, Block>();
        private readonly Dictionary<Block, List<string>> _notes = new Dictionary<Block, List<string>>();
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        private readonly HashSet<Block> _collapsed = new HashSet<Block>();

        private sealed class Seg { internal bool Body; internal string Key = ""; internal int From, To; internal float W, H; }

        // measured sizes are kept until the program, or what they depend on, changes
        private int _layoutVersion, _cacheVersion = -1;

        // ---- the view ----
        private float _wx, _wy, _palY, _contentW, _contentH, _palContent, _palHeader;
        private float _zoom = 1f;
        private Rect _vis;           // what can be seen of the workspace, in w-space
        private Rect _palVis;        // what can be seen of the palette's list

        // ---- the pointer ----
        private enum Mode { None, PanWork, PanPalette, ScrollPalette, Pressed, Dragging }
        private Mode _mode;
        private Vector2 _down, _pointer;
        private Node _pressed;
        private List<Block> _held;
        private Vector2 _grab;       // where in the block it was taken, at its own size (unscaled)
        private Target _drop;
        private Menu _menu;
        private Block _clipBlock;    // Ctrl+C

        // ---- what the selected event gives (shown at the top of the palette) ----
        private Block _hatSel, _hatOf;   // the selected block the hat was found for, and its stack's hat
        private int _hatVersion = -1;
        private string _shownHat;        // the hat whose section the palette showed last (a new one scrolls the palette to the top)

        private sealed class Menu { internal Block B; internal Slot S; internal string[] Choices; internal Rect Anchor; internal float Scroll; internal bool NewVar; }

        // ---- the names panel (variables, lists, my blocks) ----
        private bool _panel;
        private int _panelScroll;
        private string _armed;
        private float _armedUntil;
        private sealed class PBtn { internal Rect R; internal Action Do; }
        private readonly List<PBtn> _pbtns = new List<PBtn>();
        private PBtn _pdown;

        // ---- undo ----
        private readonly List<string> _undo = new List<string>(), _redo = new List<string>();

        // ---- keeping up with big programs ----
        private readonly Stopwatch _clock = new Stopwatch();
        private float _avgMs;
        private int _slowFrames, _frame;
        private bool _drawn;

        internal BlockView(Painter painter) { _p = painter; }

        internal bool Dragging => _mode == Mode.Dragging;
        internal bool CanUndo => CanUndoHook?.Invoke() ?? _undo.Count > 0;
        internal bool CanRedo => CanRedoHook?.Invoke() ?? _redo.Count > 0;
        internal float Zoom => _zoom;

        internal void Load(Program program)
        {
            Prog = program ?? new Program();
            _undo.Clear(); _redo.Clear();
            Selected = null; _menu = null; _held = null; _mode = Mode.None; _panel = false;
            _wx = _wy = _palY = 0f;
            _zoom = 1f;
            Touch();
        }

        /// <summary>The program changed (or what the sizes depend on): measure again.</summary>
        private void Touch() { _layoutVersion++; }

        /// <summary>Every change made here goes through this: measure again, then tell the screen (it compiles and saves).</summary>
        private void Fire() { Touch(); Changed(); }

        private int TabRows => Mathf.Max(1, Mathf.CeilToInt(Defs.Categories.Length / 6f));
        private float TabH => 26f;
        private float HeaderH => TabH * TabRows + 40f;

        // ================================================================ spaces

        private Vector2 ToWork(Vector2 s) => _zoom == 1f ? s : WorkRect.position + (s - WorkRect.position) / _zoom;
        private Rect FromWork(Rect r) => _zoom == 1f ? r : new Rect(WorkRect.position + (r.position - WorkRect.position) * _zoom, r.size * _zoom);

        // ================================================================ measuring

        private float W(string text, float size = Style.Font) => _p.Width(text, size);

        private List<Seg> SegsOf(Block b, List<Part> parts)
        {
            if (_segs.TryGetValue(b, out List<Seg> segs)) return segs;
            segs = new List<Seg>();
            int start = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Slot == null || parts[i].Slot.Kind != SlotKind.Body) continue;
                if (i > start || segs.Count == 0) segs.Add(new Seg { From = start, To = i });
                segs.Add(new Seg { Body = true, Key = parts[i].Slot.Key });
                start = i + 1;
            }
            if (start < parts.Count) segs.Add(new Seg { From = start, To = parts.Count });
            _segs[b] = segs;
            return segs;
        }

        private static string Shown(Block b, Slot s)
        {
            string v = b.Fields.TryGetValue(s.Key, out string x) ? x : s.Default;
            return v;
        }

        private string PickShown(Block b, Slot s)
        {
            string v = Shown(b, s);
            string[] choices = BlockLists.Get(s.List, Prog);
            if (v.Length == 0 && s.List != "creaturesAny" && choices.Length > 0) v = choices[0];
            if (v.Length == 0 && choices.Length == 0) return "(none)";
            if (v.Length == 0 && s.List == "var") return "(pick)";
            return BlockLists.Show(s.List, v);
        }

        private Vector2 PartSize(Block b, Part part)
        {
            if (part.Text != null) return new Vector2(W(part.Text), 20f);
            Slot s = part.Slot;
            if (s.Kind == SlotKind.Body) return Vector2.zero;
            if (s.Kind != SlotKind.Pick && b.Inputs.TryGetValue(s.Key, out Block child)) return Size(child);
            switch (s.Kind)
            {
                case SlotKind.Num: case SlotKind.Text:
                    string t = Shown(b, s);
                    return new Vector2(Mathf.Clamp(W(t.Length == 0 ? "..." : t) + 16f, 30f, 320f), Style.Pill);   // (long words are cut with ... in the pill)
                case SlotKind.Pick: return new Vector2(W(PickShown(b, s)) + 30f, Style.Pill);
                default: return new Vector2(40f, Style.Pill);
            }
        }

        private Vector2 Size(Block b)
        {
            if (_size.TryGetValue(b, out Vector2 size)) return size;
            size = Measure(b);
            _size[b] = size;
            return size;
        }

        private Vector2 ChainSize(List<Block> list)
        {
            float w = 0f, h = 0f;
            foreach (Block c in list) { Vector2 s = Size(c); w = Mathf.Max(w, s.x); h += s.y; }
            return new Vector2(w, h);
        }

        private static bool IsNote(BlockDef def) => def != null && def.Id == "note";

        private static Slot NoteSlot(BlockDef def) => def.Parts.Where(p => p.Slot != null && p.Slot.Kind == SlotKind.Text).Select(p => p.Slot).FirstOrDefault();

        /// <summary>A comment block: its words wrapped on a sticky note.</summary>
        private List<string> NoteLines(Block b)
        {
            if (_notes.TryGetValue(b, out List<string> lines)) return lines;
            lines = new List<string>();
            Slot slot = NoteSlot(b.Def);
            string text = slot != null ? Shown(b, slot) : "";
            if (text.Length == 0) text = Words("scripts_note_empty");
            string cur = "";
            foreach (string word in text.Split(' '))
            {
                string t = cur.Length == 0 ? word : cur + " " + word;
                if (cur.Length > 0 && W(t, 15f) > 224f) { lines.Add(cur); cur = word; } else cur = t;
                if (lines.Count >= 8) break;
            }
            if (cur.Length > 0 && lines.Count < 8) lines.Add(cur);
            _notes[b] = lines;
            return lines;
        }

        private Vector2 Measure(Block b)
        {
            BlockDef def = b.Def;
            if (def == null) return new Vector2(Mathf.Max(90f, W("? " + b.Type) + 20f), 30f);
            if (IsNote(def) && NoteSlot(def) != null) return new Vector2(244f, 18f + NoteLines(b).Count * 19f);
            List<Part> parts = PartsOf(def);
            List<Seg> segs = SegsOf(b, parts);
            bool value = def.Shape == Shape.Value;
            bool hidden = _collapsed.Contains(b);
            float vpad = value ? 3f : 6f;
            float width = 0f, height = 0f;
            foreach (Seg seg in segs)
            {
                if (seg.Body)
                {
                    if (hidden) { seg.W = 0f; seg.H = 0f; continue; }
                    List<Block> list = b.Bodies.TryGetValue(seg.Key, out List<Block> l) ? l : new List<Block>();
                    Vector2 cs = ChainSize(list);
                    seg.W = Style.Indent + Mathf.Max(60f, cs.x) + 4f;
                    seg.H = Mathf.Max(list.Count == 0 ? 26f : 0f, cs.y);
                    width = Mathf.Max(width, seg.W);
                    height += seg.H;
                    continue;
                }
                float rw = Style.Pad, rh = 20f;
                for (int i = seg.From; i < seg.To; i++)
                {
                    Vector2 ps = PartSize(b, parts[i]);
                    rw += ps.x + Style.Gap;
                    rh = Mathf.Max(rh, ps.y);
                }
                rw += Style.Pad - Style.Gap;
                seg.W = rw;
                seg.H = value ? rh + vpad * 2f : Mathf.Max(Style.Row, rh + vpad * 2f);
                width = Mathf.Max(width, rw);
                height += seg.H;
            }
            if (def.Shape == Shape.C || def.Shape == Shape.Hat) height += Style.Foot;
            return new Vector2(Mathf.Max(width, 40f), Mathf.Max(height, value ? 24f : Style.Row));
        }

        // ================================================================ drawing

        private void AddNode(NodeKind kind, Rect r, Block b, Slot s, bool palette, string text = null, Action act = null, Program.Top top = null)
        {
            _nodes.Add(new Node { Kind = kind, R = r, B = b, S = s, Palette = palette, Text = text, Do = act, T = top });
        }

        private string Container(bool reg, bool palette) => palette ? "palette" : (reg ? "work" : "held");

        private static bool Overlaps(Rect visible, float x, float y, Vector2 size) =>
            x < visible.xMax && x + size.x > visible.x && y < visible.yMax && y + size.y > visible.y;

        /// <summary>Draws one block at x, y (the container's space). reg: record what the pointer can hit and where blocks can be dropped (not for the one in the hand).</summary>
        private void DrawBlock(Block b, float x, float y, int z, bool reg, bool palette)
        {
            BlockDef def = b.Def;
            Vector2 size = Size(b);
            string cont = Container(reg, palette);
            if (!palette && reg) _where[b] = new Vector2(x - WorkRect.x + _wx, y - WorkRect.y + _wy);
            if (def == null)
            {
                _p.To(cont, z);
                _p.Rect(x, y, size.x, size.y, Style.Bad);
                _p.Text("? " + b.Type, x + 8f, y, size.x - 10f, size.y, Style.Font, Color.white);
                if (reg) AddNode(NodeKind.Block, new Rect(x, y, size.x, size.y), b, null, palette);
                return;
            }
            Color color = def.Shape == Shape.Value ? Style.ByType(def.Type) : Style.Category(def.Category);
            _p.To(cont, z);
            if (b == Selected && !palette && reg) _p.Rect(x - 3f, y - 3f, size.x + 6f, size.y + 6f, Color.white);
            if (!palette && Problems.TryGetValue(b.Id, out bool fatal)) _p.Rect(x - 3f, y - 3f, size.x + 6f, size.y + 6f, fatal ? Style.Bad : Style.Warn);
            if (reg) AddNode(NodeKind.Block, new Rect(x, y, size.x, size.y), b, null, palette);

            if (IsNote(def) && NoteSlot(def) != null)
            {
                _p.Rect(x, y, size.x, size.y, new Color(0.96f, 0.86f, 0.42f));
                _p.Rect(x, y, 5f, size.y, new Color(0.85f, 0.7f, 0.2f), false);
                List<string> lines = NoteLines(b);
                for (int i = 0; i < lines.Count; i++) _p.Text(lines[i], x + 12f, y + 9f + i * 19f, size.x - 18f, 19f, 15f, new Color(0.2f, 0.17f, 0.05f));
                return;
            }

            List<Part> parts = PartsOf(def);
            List<Seg> segs = SegsOf(b, parts);
            bool container = def.Shape == Shape.C || def.Shape == Shape.Hat;
            bool hidden = _collapsed.Contains(b);
            float cy = y;
            if (def.Shape == Shape.Value)
            {
                _p.Rect(x, y, size.x, size.y, color);
            }
            else if (!container)
            {
                _p.Rect(x, y, size.x, size.y, color);
                _p.Rect(x + 4f, y + size.y - 3f, size.x - 8f, 3f, Style.Darker(color, 0.7f), false);
                if (def.Danger) _p.Rect(x, y + 4f, 4f, size.y - 8f, Style.Bad, false);
            }
            else
            {
                float total = size.y;
                _p.Rect(x, y, Style.Indent, total, color);
                float yy = y;
                foreach (Seg seg in segs)
                {
                    if (seg.Body) { yy += seg.H; continue; }
                    _p.Rect(x, yy, size.x, seg.H, color);
                    yy += seg.H;
                }
                _p.Rect(x, y + total - Style.Foot, size.x, Style.Foot, color);
                if (def.Shape == Shape.Hat) _p.Rect(x + 2f, y, 6f, 4f, Style.Lighter(color, 0.35f), false);
            }

            foreach (Seg seg in segs)
            {
                if (seg.Body)
                {
                    if (hidden)
                    {
                        // a collapsed stack: only its first line, and how much is folded away
                        int count = b.Bodies.Values.Sum(l => l.Sum(c => c.CountAll()));
                        _p.To(cont, z);
                        _p.Text(Words("scripts_folded").Replace("{0}", count.ToString()), x + Style.Indent + 4f, y + size.y - Style.Foot - 1f, 200f, Style.Foot + 6f, 12f, new Color(1f, 1f, 1f, 0.8f));
                        continue;
                    }
                    List<Block> list = b.Body(seg.Key);
                    float bx = x + Style.Indent;
                    if (list.Count == 0) _p.Rect(bx + 2f, cy + 2f, Mathf.Max(60f, size.x - Style.Indent - 8f), 22f, Style.Inset);
                    if (reg && !palette) _targets.Add(new Target { Kind = TargetKind.BodyStart, List = list, Index = 0, Owner = b, P = new Vector2(bx, cy), R = new Rect(bx, cy, 80f, 26f) });
                    DrawChain(list, bx, cy, z, reg, palette);
                    cy += seg.H;
                    continue;
                }
                float cx = x + Style.Pad;
                for (int i = seg.From; i < seg.To; i++)
                {
                    Part part = parts[i];
                    Vector2 ps = PartSize(b, part);
                    float py = cy + (seg.H - ps.y) / 2f;
                    _p.To(cont, z);
                    if (part.Text != null)
                        _p.Text(part.Text, cx, cy, ps.x + 2f, seg.H, Style.Font, Color.white);
                    else
                        DrawSlot(b, part.Slot, color, cx, py, ps, z, reg, palette);
                    cx += ps.x + Style.Gap;
                }
                cy += seg.H;
            }
            _p.To(cont, z);
        }

        private void DrawSlot(Block b, Slot s, Color blockColor, float x, float y, Vector2 size, int z, bool reg, bool palette)
        {
            Rect r = new Rect(x, y, size.x, size.y);
            bool hasChild = s.Kind != SlotKind.Pick && b.Inputs.ContainsKey(s.Key);
            if (reg && !palette && s.Kind != SlotKind.Pick) _targets.Add(new Target { Kind = TargetKind.Socket, Owner = b, S = s, R = r });
            if (hasChild)
            {
                DrawBlock(b.Inputs[s.Key], x, y, z + 1, reg, palette);
                return;
            }
            _p.To(Container(reg, palette), z);
            switch (s.Kind)
            {
                case SlotKind.Num: case SlotKind.Text:
                    {
                        string t = Shown(b, s);
                        bool outside = s.Kind == SlotKind.Num && Gen.OutOfRange(t, s) != null;   // (a number the slot can't take: red, and the problems list says why)
                        _p.Rect(x, y, size.x, size.y, outside ? Style.Bad : Style.PillFill);
                        _p.Text(t.Length == 0 ? "..." : t, x + 2f, y, size.x - 4f, size.y, Style.Font - 1f, t.Length == 0 ? Style.Dim : outside ? Color.white : Style.PillText, TextAlignmentOptions.Midline);
                        break;
                    }
                case SlotKind.Pick:
                    {
                        _p.Rect(x, y, size.x, size.y, Style.Darker(blockColor, 0.72f));
                        _p.Text(PickShown(b, s), x + 8f, y, size.x - 22f, size.y, Style.Font - 1f, Color.white);
                        float ax = x + size.x - 16f, ay = y + size.y / 2f - 3f;
                        _p.Rect(ax, ay, 9f, 2f, Color.white, false); _p.Rect(ax + 2f, ay + 2f, 5f, 2f, Color.white, false); _p.Rect(ax + 4f, ay + 4f, 1f, 2f, Color.white, false);
                        break;
                    }
                default:
                    _p.Rect(x, y, size.x, size.y, Style.Inset);
                    break;
            }
            if (reg && !palette && s.Kind != SlotKind.Socket) AddNode(NodeKind.Slot, r, b, s, false);
        }

        /// <summary>A list of blocks one under the other. Returns its size. Blocks wholly out of sight are measured but not drawn.</summary>
        private Vector2 DrawChain(List<Block> list, float x, float y, int z, bool reg, bool palette)
        {
            float cy = y, w = 0f;
            Rect visible = palette ? _palVis : _vis;
            bool cull = reg || palette;
            for (int i = 0; i < list.Count; i++)
            {
                Block b = list[i];
                Vector2 s = Size(b);
                bool seen = !cull || Overlaps(visible, x, cy, s);
                if (seen) DrawBlock(b, x, cy, z, reg, palette);
                BlockDef def = b.Def;
                cy += s.y;
                w = Mathf.Max(w, s.x);
                if (seen && reg && !palette && def != null && def.Shape != Shape.Hat && def.Shape != Shape.Value && !def.Terminal)
                    _targets.Add(new Target { Kind = TargetKind.After, List = list, Index = i + 1, P = new Vector2(x, cy), R = new Rect(x, cy - 8f, Mathf.Max(60f, s.x), 16f) });
            }
            return new Vector2(w, cy - y);
        }

        private Block Template(string id)
        {
            if (!_templates.TryGetValue(id, out Block t)) _templates[id] = t = new Block(id);
            return t;
        }

        private Block Chip(string name)
        {
            if (!_chips.TryGetValue(name, out Block c)) { c = new Block("var_get"); c.Fields["v"] = name; _chips[name] = c; }
            return c;
        }

        private void RebuildCollapsed()
        {
            _collapsed.Clear();
            foreach (Program.Top top in Prog.Tops) if (top.Collapsed && top.Chain.Count > 0) _collapsed.Add(top.Chain[0]);
        }

        /// <summary>Draws the palette and the workspace (and what is in the hand, and a menu that is open).</summary>
        internal void Draw()
        {
            _clock.Restart();
            _frame++;
            // a program with a lot in it can make a picture slow: then the palette and workspace are redrawn every second picture (they keep what they
            // showed in between), nothing else changes, and nothing is said
            bool resting = _slowFrames > 30 && (_frame & 1) == 1 && _mode == Mode.None && _menu == null && !_panel && _drawn;
            if (resting)
            {
                _p.Keep("palette"); _p.Keep("work");
                DrawPanel();
                return;
            }
            if (_cacheVersion != _layoutVersion)
            {
                _size.Clear(); _segs.Clear(); _notes.Clear(); _labels.Clear(); _chips.Clear(); _templates.Clear();
                _cacheVersion = _layoutVersion;
            }
            _nodes.Clear(); _targets.Clear();
            if (_mode != Mode.Dragging) _where.Clear();
            RebuildCollapsed();
            if (!Defs.Categories.Contains(Category) && Defs.Categories.Length > 0) Category = Defs.Categories[0];
            _vis = new Rect(WorkRect.x, WorkRect.y, WorkRect.width / _zoom, WorkRect.height / _zoom);
            DrawPalette();
            DrawWorkspace();
            DrawHand();
            DrawMenu();
            DrawPanel();
            _drawn = true;
            float ms = (float)_clock.Elapsed.TotalMilliseconds;
            _avgMs = Mathf.Lerp(_avgMs, ms, 0.1f);
            if (_avgMs > 8f) _slowFrames++; else _slowFrames = Mathf.Max(0, _slowFrames - 2);
        }

        // ---------------------------------------------------------------- the palette

        /// <summary>The words of a block, lower case, for the search (its label text and its id).</summary>
        private string LabelText(BlockDef d)
        {
            if (_labels.TryGetValue(d.Id, out string s)) return s;
            s = (d.Id.Replace('_', ' ') + " " + string.Join(" ", PartsOf(d).Where(p => p.Text != null).Select(p => p.Text.Trim()).ToArray())).ToLowerInvariant();
            _labels[d.Id] = s;
            return s;
        }

        private List<BlockDef> SearchResults()
        {
            string[] words = Query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var found = new List<BlockDef>();
            foreach (BlockDef d in Defs.All)
            {
                if (d.Id == "var_get") continue;
                string text = LabelText(d);
                bool all = true;
                foreach (string w in words) if (!text.Contains(w)) { all = false; break; }
                if (all) { found.Add(d); if (found.Count >= 80) break; }
            }
            return found;
        }

        /// <summary>A category's name for a tab: the translation, or the id with a capital (a category the language files do not know yet).</summary>
        private string CatName(string cat)
        {
            string key = "scripts_cat_" + cat, w = Words(key);
            return w != key ? w : char.ToUpperInvariant(cat[0]) + cat.Substring(1);
        }

        private string GroupName(string group)
        {
            string key = "scripts_group_" + group.ToLowerInvariant().Replace(' ', '_'), w = Words(key);
            return w != key ? w : group;
        }

        private void DrawPalette()
        {
            Rect r = PaletteRect;
            string[] cats = Defs.Categories;
            _palHeader = HeaderH;
            _p.To("palette", 0);
            _p.Rect(r.x, r.y, r.width, r.height, Style.Panel, false);
            float listTop = r.y + _palHeader;
            _palVis = new Rect(r.x, listTop - 40f, r.width, r.height - _palHeader + 80f);

            float y = listTop + 6f - _palY, x = r.x + 14f;
            // the blocks, first (the header is drawn over what scrolls under it, and its nodes are found first)
            bool searching = Query.Length > 0;
            Block hat = searching ? null : SelectedHat();
            string hatType = hat != null ? hat.Type : null;
            if (hatType != _shownHat) { _shownHat = hatType; if (hatType != null) _palY = 0f; }
            if (hat != null) y = DrawGives(hat, x, y, r);
            if (!ReadOnly && !searching && (Category == "memory" || Category == "lists" || Category == "functions"))
            {
                _p.To("palette", 1);
                float bx = x;
                if (Category == "memory")
                {
                    Rect br = new Rect(bx, y, 150f, 28f);
                    _p.Rect(br.x, br.y, br.width, br.height, Style.Button);
                    _p.Text(Words("scripts_make_variable"), br.x, br.y, br.width, br.height, 14f, Color.white, TextAlignmentOptions.Midline);
                    AddNode(NodeKind.Button, br, null, null, true, "newvar", () => NewVariable(null));
                    bx += 158f;
                }
                Rect mr = new Rect(bx, y, 112f, 28f);
                _p.Rect(mr.x, mr.y, mr.width, mr.height, Style.Button);
                _p.Text(Words("scripts_manage_names"), mr.x, mr.y, mr.width, mr.height, 14f, Color.white, TextAlignmentOptions.Midline);
                AddNode(NodeKind.Button, mr, null, null, true, "names", () => { _panel = true; _panelScroll = 0; });
                y += 38f;
                if (Category == "memory")
                    foreach (string v in Prog.Vars)
                    {
                        Block chip = Chip(v);
                        Vector2 cs = Size(chip);
                        if (Overlaps(_palVis, x, y, cs)) DrawBlock(chip, x, y, 1, true, true);
                        y += cs.y + 8f;
                    }
            }

            IEnumerable<BlockDef> list = searching ? (IEnumerable<BlockDef>)SearchResults() : Defs.In(Category);
            string lastGroup = null, lastCat = null;
            int shown = 0;
            foreach (BlockDef d in list)
            {
                if (d.Id == "var_get") continue;
                shown++;
                string heading = searching ? (d.Category != lastCat ? CatName(d.Category) : null) : (!string.IsNullOrEmpty(d.Group) && d.Group != lastGroup ? d.Group : null);
                lastCat = d.Category;
                if (!searching && !string.IsNullOrEmpty(d.Group)) lastGroup = d.Group;
                if (heading != null)
                {
                    _p.To("palette", 1);
                    if (y > listTop - 24f && y < r.yMax)
                    {
                        _p.Text(searching ? heading : GroupName(heading), x, y, r.width - 40f, 20f, 13f, Style.Dim);
                        _p.Rect(x, y + 20f, r.width - 40f, 1f, new Color(1f, 1f, 1f, 0.12f), false);
                    }
                    y += 26f;
                }
                Block t = Template(d.Id);
                Vector2 s = Size(t);
                if (Overlaps(_palVis, x, y, s)) DrawBlock(t, x, y, 1, true, true);
                y += s.y + 12f;
            }
            if (searching && shown == 0)
            {
                _p.To("palette", 1);
                _p.Text(Words("scripts_search_none"), x, y, r.width - 40f, 40f, 15f, Style.Dim, TextAlignmentOptions.TopLeft);
                y += 50f;
            }
            _palContent = y + _palY - listTop + 20f;
            ClampPalette();

            // cover what scrolled out of the palette's top under the header, then the header itself
            _p.To("palette", 4);
            _p.Rect(r.x, r.y, r.width, _palHeader, Style.Panel, false);
            _p.To("palette", 5);
            int rows = TabRows;
            int perRow = Mathf.CeilToInt(cats.Length / (float)rows);
            for (int i = 0; i < cats.Length; i++)
            {
                string cat = cats[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, cats.Length - row * perRow);
                float tw = r.width / inRow;
                bool on = cat == Category && !searching;
                Rect tr = new Rect(r.x + col * tw, r.y + row * TabH, tw, TabH);
                Color c = Style.Category(cat);
                _p.Rect(tr.x + 1f, tr.y + 1f, tr.width - 2f, tr.height - 2f, on ? c : Style.Darker(c, 0.45f));
                _p.Text(CatName(cat), tr.x + 1f, tr.y + 1f, tr.width - 2f, tr.height - 2f, 12f, Color.white, TextAlignmentOptions.Midline);
                string chosen = cat;
                AddNode(NodeKind.Tab, tr, null, null, true, cat, () => { Category = chosen; Query = ""; _palY = 0f; });
            }
            // the search box: a button that opens the game's text box, and a clear button
            Rect sr = new Rect(r.x + 8f, r.y + TabH * rows + 6f, r.width - (searching ? 56f : 16f), 28f);
            _p.Rect(sr.x, sr.y, sr.width, sr.height, searching ? new Color(0.25f, 0.3f, 0.5f) : Style.Inset);
            _p.Text(searching ? Query : Words("scripts_search"), sr.x + 8f, sr.y, sr.width - 12f, sr.height, 14f, searching ? Color.white : Style.Dim);
            AddNode(NodeKind.Button, sr, null, null, true, "search", OpenSearch);
            if (searching)
            {
                Rect cr = new Rect(sr.xMax + 6f, sr.y, 34f, 28f);
                _p.Rect(cr.x, cr.y, cr.width, cr.height, Style.Button);
                _p.Text("x", cr.x, cr.y, cr.width, cr.height, 16f, Color.white, TextAlignmentOptions.Midline);
                AddNode(NodeKind.Button, cr, null, null, true, "clear", () => { Query = ""; _palY = 0f; });
            }

            // the scrollbar
            float view = r.height - _palHeader;
            if (_palContent > view + 1f)
            {
                float trackY = listTop, trackH = view;
                float thumbH = Mathf.Max(28f, trackH * view / _palContent);
                float t01 = _palY / Mathf.Max(1f, _palContent - view);
                _p.To("palette", 6);
                _p.Rect(r.xMax - 8f, trackY, 5f, trackH, new Color(1f, 1f, 1f, 0.06f));
                _p.Rect(r.xMax - 8f, trackY + t01 * (trackH - thumbH), 5f, thumbH, new Color(1f, 1f, 1f, 0.35f));
                AddNode(NodeKind.Scroll, new Rect(r.xMax - 18f, trackY, 18f, trackH), null, null, true);
            }
        }

        /// <summary>The 'when' block of the stack the selected block is in (the hat itself, a block under it, or a round block in one), or null.</summary>
        private Block SelectedHat()
        {
            if (Selected == _hatSel && _hatVersion == _layoutVersion) return _hatOf;
            _hatSel = Selected; _hatVersion = _layoutVersion; _hatOf = null;
            if (Selected == null) return null;
            foreach (Program.Top top in Prog.Tops)
            {
                if (top.Chain.Count == 0) continue;
                Block first = top.Chain[0];
                if (first.Def == null || first.Def.Shape != Shape.Hat) continue;
                foreach (Block b in top.Chain)
                    if (b == Selected || b.All().Contains(Selected)) { _hatOf = first; return first; }
            }
            return null;
        }

        /// <summary>The hat's words for a heading: its label without the slots ("when a player says ...").</summary>
        private string HatWords(BlockDef def)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Part part in PartsOf(def))
            {
                if (part.Text != null) sb.Append(part.Text);
                else if (part.Slot != null && part.Slot.Kind != SlotKind.Body) sb.Append("...");
            }
            return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        /// <summary>
        /// "This event gives you": the blocks that only work under the selected stack's 'when' block (the player, what was said, the place...),
        /// at the top of the palette, ready to drag like any palette block. Returns the y below it.
        /// </summary>
        private float DrawGives(Block hat, float x, float y, Rect r)
        {
            List<BlockDef> gives = Defs.Gives(hat.Type);
            float w = r.width - 28f;
            float h = 50f + (gives.Count == 0 ? 24f : 0f);
            foreach (BlockDef d in gives) h += Size(Template(d.Id)).y + 8f;
            if (gives.Count > 0) h += 22f;
            _p.To("palette", 0);
            _p.Rect(x - 8f, y - 4f, w + 8f, h, new Color(Style.Good.r, Style.Good.g, Style.Good.b, 0.12f), false);
            _p.Rect(x - 8f, y - 4f, 3f, h, Style.Good, false);
            _p.To("palette", 1);
            _p.Text(Words("scripts_event_gives"), x, y, w - 8f, 20f, 15f, Style.Good);
            _p.Text(hat.Def != null ? HatWords(hat.Def) : hat.Type, x, y + 20f, w - 8f, 20f, 12f, Style.Dim);
            y += 46f;
            if (gives.Count == 0)
            {
                _p.Text(Words("scripts_event_gives_none"), x, y, w - 8f, 22f, 13f, Style.Dim);
                y += 24f;
            }
            foreach (BlockDef d in gives)
            {
                Block t = Template(d.Id);
                Vector2 s = Size(t);
                if (Overlaps(_palVis, x, y, s)) DrawBlock(t, x, y, 1, true, true);
                y += s.y + 8f;
            }
            if (gives.Count > 0)
            {
                _p.To("palette", 1);
                _p.Text(Words("scripts_event_gives_hint"), x, y - 2f, w - 8f, 20f, 12f, Style.Dim);
                y += 22f;
            }
            return y + 14f;
        }

        private void OpenSearch()
        {
            Ask(Words("scripts_search_title"), Query, 30, v => { Query = (v ?? "").Trim(); _palY = 0f; });
        }

        // ---------------------------------------------------------------- the workspace

        private void DrawWorkspace()
        {
            Rect r = WorkRect;
            _p.Scale("work", _zoom);
            _p.To("work", -1);
            _p.Rect(r.x, r.y, r.width / _zoom, r.height / _zoom, Style.Back, false);
            // faint dots so scrolling can be seen
            float step = _zoom < 0.7f ? 120f : _zoom < 1f ? 80f : 40f;
            float gx = step - (_wx % step), gy = step - (_wy % step);
            for (float yy = gy; yy < r.height / _zoom; yy += step)
                for (float xx = gx; xx < r.width / _zoom; xx += step)
                    _p.Rect(r.x + xx, r.y + yy, 2f, 2f, new Color(1f, 1f, 1f, 0.07f), false);
            _contentW = _contentH = 0f;
            foreach (Program.Top top in Prog.Tops)
            {
                float x = r.x + top.X - _wx, y = r.y + top.Y - _wy;
                Vector2 s = ChainSize(top.Chain);
                _contentW = Mathf.Max(_contentW, top.X + s.x + 24f); _contentH = Mathf.Max(_contentH, top.Y + s.y);
                if (!Overlaps(_vis, x, y, new Vector2(s.x + 24f, s.y))) continue;
                DrawChain(top.Chain, x, y, 0, true, false);
                // the fold arrow, at the right of a stack that has a hat
                Block first = top.Chain.Count > 0 ? top.Chain[0] : null;
                if (first != null && first.Def != null && first.Def.Shape == Shape.Hat && !ReadOnly)
                {
                    Rect ar = new Rect(x + Size(first).x + 4f, y + 6f, 18f, 18f);
                    _p.To("work", 2);
                    _p.Rect(ar.x, ar.y, ar.width, ar.height, new Color(1f, 1f, 1f, 0.12f));
                    if (top.Collapsed) { _p.Rect(ar.x + 6f, ar.y + 3f, 2f, 12f, Color.white, false); _p.Rect(ar.x + 8f, ar.y + 5f, 2f, 8f, Color.white, false); _p.Rect(ar.x + 10f, ar.y + 7f, 2f, 4f, Color.white, false); }
                    else { _p.Rect(ar.x + 3f, ar.y + 6f, 12f, 2f, Color.white, false); _p.Rect(ar.x + 5f, ar.y + 8f, 8f, 2f, Color.white, false); _p.Rect(ar.x + 7f, ar.y + 10f, 4f, 2f, Color.white, false); }
                    Program.Top t = top;
                    AddNode(NodeKind.Toggle, ar, null, null, false, null, null, t);
                }
                if (first != null && first == Selected && first.Def != null && first.Def.Shape == Shape.Hat && _mode != Mode.Dragging)
                {
                    _p.To("work", 2);
                    _p.Text(Words("scripts_event_gives_see"), x + Size(first).x + 28f, y + 3f, 300f, 24f, 13f, Style.Good);
                }
            }
            ClampView();
            if (Prog.Tops.Count == 0 && _mode != Mode.Dragging)
            {
                _p.To("work", 1);
                _p.Text(Words("scripts_empty_hint"), r.x + 30f, r.y + 30f, r.width / _zoom - 60f, 60f, 18f, Style.Dim, TextAlignmentOptions.TopLeft);
            }
            DrawOthers();
            // the drop mark
            if (_drop != null)
            {
                _p.To("work", 40);
                if (_drop.Kind == TargetKind.Socket) { Rect s = _drop.R; _p.Rect(s.x - 3f, s.y - 3f, s.width + 6f, s.height + 6f, Color.white); }
                else _p.Rect(_drop.P.x, _drop.P.y - 2f, 140f, 5f, Color.white);
            }
        }

        private float HeldScale => WorkRect.Contains(_pointer) ? _zoom : 1f;

        private void DrawHand()
        {
            if (_held == null || _mode != Mode.Dragging) { _p.Scale("held", 1f); return; }
            float scale = HeldScale;
            _p.Scale("held", scale);
            Vector2 at = _pointer / scale - _grab;
            float y = at.y;
            foreach (Block b in _held)
            {
                DrawBlock(b, at.x, y, 0, false, false);
                y += Size(b).y;
            }
        }

        // ================================================================ menus

        private void OpenMenu(Block b, Slot s, Rect anchor)
        {
            string[] choices = BlockLists.Get(s.List, Prog);
            _menu = new Menu { B = b, S = s, Choices = choices, Anchor = anchor, NewVar = s.List == "var" };
        }

        private const float MenuRow = 28f;
        private int MenuCount => _menu == null ? 0 : _menu.Choices.Length + (_menu.NewVar ? 1 : 0);

        private Rect MenuRect()
        {
            float w = 90f;
            foreach (string c in _menu.Choices) w = Mathf.Max(w, W(BlockLists.Show(_menu.S.List, c)) + 30f);
            if (_menu.NewVar) w = Mathf.Max(w, W(Words("scripts_new_variable")) + 30f);
            int rows = Mathf.Min(MenuCount, 10);
            float h = rows * MenuRow + 8f;
            float x = Mathf.Min(_menu.Anchor.x, 1280f - w - 6f), y = _menu.Anchor.yMax + 2f;
            if (y + h > 672f) y = Mathf.Max(4f, _menu.Anchor.y - h - 2f);
            return new Rect(x, y, w, h);
        }

        private void DrawMenu()
        {
            if (_menu == null) return;
            Rect r = MenuRect();
            _p.To("popup", 0);
            _p.Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f, new Color(0f, 0f, 0f, 0.6f));
            _p.Rect(r.x, r.y, r.width, r.height, new Color(0.17f, 0.18f, 0.25f));
            int rows = Mathf.Min(MenuCount, 10), first = Mathf.Clamp((int)_menu.Scroll, 0, Mathf.Max(0, MenuCount - rows));
            string current = _menu.B.Fields.TryGetValue(_menu.S.Key, out string c) ? c : _menu.S.Default;
            for (int i = 0; i < rows; i++)
            {
                int index = first + i;
                Rect rr = new Rect(r.x + 4f, r.y + 4f + i * MenuRow, r.width - 8f, MenuRow - 2f);
                bool newVar = _menu.NewVar && index == _menu.Choices.Length;
                bool hover = rr.Contains(_pointer);
                string value = newVar ? "" : _menu.Choices[index];
                if (hover) _p.Rect(rr.x, rr.y, rr.width, rr.height, Style.Accent);
                else if (!newVar && value == current) _p.Rect(rr.x, rr.y, rr.width, rr.height, new Color(1f, 1f, 1f, 0.1f));
                _p.To("popup", 1);
                _p.Text(newVar ? Words("scripts_new_variable") : BlockLists.Show(_menu.S.List, value), rr.x + 8f, rr.y, rr.width - 10f, rr.height, 16f, newVar ? Style.Good : Color.white);
                _p.To("popup", 0);
            }
            if (MenuCount > rows)
            {
                float f = (float)first / (MenuCount - rows);
                _p.Rect(r.xMax - 6f, r.y + 4f + f * (r.height - 40f), 3f, 32f, new Color(1f, 1f, 1f, 0.4f), false);
            }
        }

        // ================================================================ the names panel (variables, lists, my blocks)

        private sealed class NameRow { internal string Kind, List, Name; internal int Uses; }

        private IEnumerable<Block> AllBlocks()
        {
            foreach (Program.Top top in Prog.Tops)
                foreach (Block b in top.Chain)
                    foreach (Block x in b.All()) yield return x;
        }

        /// <summary>Every place a name is used: the blocks that pick it in a dropdown of that list.</summary>
        private IEnumerable<KeyValuePair<Block, string>> Uses(string list, string name)
        {
            foreach (Block b in AllBlocks())
            {
                BlockDef d = b.Def;
                if (d == null) continue;
                foreach (Part part in d.Parts)
                    if (part.Slot != null && part.Slot.Kind == SlotKind.Pick && part.Slot.List == list && b.Fields.TryGetValue(part.Slot.Key, out string v) && v == name)
                        yield return new KeyValuePair<Block, string>(b, part.Slot.Key);
            }
        }

        private List<NameRow> NameRows()
        {
            var rows = new List<NameRow>();
            foreach (string v in Prog.Vars) rows.Add(new NameRow { Kind = "var", List = "var", Name = v, Uses = Uses("var", v).Count() });
            foreach (string l in BlockLists.Get("lists", Prog)) if (!Prog.Vars.Contains(l)) rows.Add(new NameRow { Kind = "list", List = "lists", Name = l, Uses = Uses("lists", l).Count() });
            foreach (string pr in BlockLists.Get("procedures", Prog)) rows.Add(new NameRow { Kind = "proc", List = "procedures", Name = pr, Uses = Uses("procedures", pr).Count() });
            return rows;
        }

        private static readonly Rect PanelRect = new Rect(310f, 70f, 580f, 560f);
        private const float PanelRow = 38f;
        private const int PanelRows = 11;

        private void DrawPanel()
        {
            _pbtns.Clear();
            if (!_panel) return;
            List<NameRow> rows = NameRows();
            Rect r = PanelRect;
            _p.To("popup", 2);
            _p.Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f, new Color(0f, 0f, 0f, 0.65f));
            _p.Rect(r.x, r.y, r.width, r.height, new Color(0.15f, 0.16f, 0.23f));
            _p.To("popup", 3);
            _p.Text(Words("scripts_names_title"), r.x + 16f, r.y + 8f, r.width - 120f, 34f, 20f, Color.white);
            _p.Text(Words("scripts_names_hint"), r.x + 16f, r.y + 40f, r.width - 32f, 22f, 13f, Style.Dim);
            Rect close = new Rect(r.xMax - 92f, r.y + 8f, 80f, 32f);
            _p.To("popup", 4);
            _p.Rect(close.x, close.y, close.width, close.height, Style.Button);
            _p.Text(Words("scripts_close"), close.x, close.y, close.width, close.height, 15f, Color.white, TextAlignmentOptions.Midline);
            _pbtns.Add(new PBtn { R = close, Do = () => _panel = false });
            _panelScroll = Mathf.Clamp(_panelScroll, 0, Mathf.Max(0, rows.Count - PanelRows));
            if (rows.Count == 0)
            {
                _p.To("popup", 3);
                _p.Text(Words("scripts_names_none"), r.x + 16f, r.y + 80f, r.width - 32f, 30f, 16f, Style.Dim);
            }
            for (int i = 0; i < PanelRows && _panelScroll + i < rows.Count; i++)
            {
                NameRow row = rows[_panelScroll + i];
                Rect rr = new Rect(r.x + 12f, r.y + 70f + i * PanelRow, r.width - 24f, PanelRow - 4f);
                _p.To("popup", 3);
                _p.Rect(rr.x, rr.y, rr.width, rr.height, Style.Panel);
                Color kc = row.Kind == "var" ? Style.Category("memory") : row.Kind == "list" ? Style.Category("lists") : Style.Category("functions");
                _p.Rect(rr.x, rr.y, 5f, rr.height, kc, false);
                _p.To("popup", 4);
                _p.Text(row.Name, rr.x + 14f, rr.y, rr.width - 330f, rr.height, 17f, Color.white);
                _p.Text(Words(row.Kind == "var" ? "scripts_kind_var" : row.Kind == "list" ? "scripts_kind_list" : "scripts_kind_proc") + "  -  " + Words("scripts_uses").Replace("{0}", row.Uses.ToString()), rr.xMax - 320f, rr.y, 200f, rr.height, 12f, Style.Dim);
                Rect rn = new Rect(rr.xMax - 106f, rr.y + 3f, 48f, rr.height - 6f);
                Rect dl = new Rect(rr.xMax - 54f, rr.y + 3f, 48f, rr.height - 6f);
                NameRow captured = row;
                bool armed = _armed == row.Kind + "|" + row.Name && Time.unscaledTime < _armedUntil;
                if (!ReadOnly)
                {
                    _p.Rect(rn.x, rn.y, rn.width, rn.height, Style.Button);
                    _p.Text(Words("scripts_rename_short"), rn.x, rn.y, rn.width, rn.height, 12f, Color.white, TextAlignmentOptions.Midline);
                    _pbtns.Add(new PBtn { R = rn, Do = () => RenameName(captured) });
                    bool canDelete = row.Kind == "var";
                    _p.Rect(dl.x, dl.y, dl.width, dl.height, !canDelete ? Style.ButtonOff : armed ? Style.Bad : Style.Darker(Style.Bad, 0.6f));
                    _p.Text(armed ? Words("scripts_sure_short") : Words("scripts_delete_short"), dl.x, dl.y, dl.width, dl.height, 12f, canDelete ? Color.white : Style.Dim, TextAlignmentOptions.Midline);
                    if (canDelete) _pbtns.Add(new PBtn { R = dl, Do = () => DeleteName(captured) });
                    else _pbtns.Add(new PBtn { R = dl, Do = () => Note(Words("scripts_delete_block_hint")) });
                }
            }
            if (rows.Count > PanelRows)
            {
                _p.To("popup", 4);
                _p.Text(Words("scripts_names_more").Replace("{0}", (_panelScroll + 1).ToString()).Replace("{1}", Mathf.Min(rows.Count, _panelScroll + PanelRows).ToString()).Replace("{2}", rows.Count.ToString()), r.x + 16f, r.yMax - 28f, r.width - 32f, 22f, 13f, Style.Dim);
            }
        }

        private PBtn HitPanel(Vector2 p)
        {
            for (int i = _pbtns.Count - 1; i >= 0; i--) if (_pbtns[i].R.Contains(p)) return _pbtns[i];
            return null;
        }

        /// <summary>
        /// A new name for a variable (all the blocks that pick it follow), or for a list or a block of the player's own: the blocks that
        /// pick it follow, and so do the blocks that make it (a text box holding the old name).
        /// </summary>
        private void RenameName(NameRow row)
        {
            Ask(Words("scripts_rename_to"), row.Name, 20, v =>
            {
                string name = Naming.Var(v);
                if (name == null || name == row.Name) return;
                if (Prog.Vars.Contains(name) || BlockLists.Get(row.List, Prog).Contains(name)) { Note(Words("scripts_name_taken")); return; }
                Snapshot();
                foreach (KeyValuePair<Block, string> use in Uses(row.List, row.Name).ToList()) use.Key.Fields[use.Value] = name;
                if (row.Kind == "var") { int i = Prog.Vars.IndexOf(row.Name); if (i >= 0) Prog.Vars[i] = name; }
                else
                    foreach (Block b in AllBlocks())
                    {
                        BlockDef d = b.Def;
                        if (d == null || !(d.Id.StartsWith("define") || d.Id.StartsWith("make_") || d.Id.StartsWith("list_new"))) continue;
                        foreach (Part part in d.Parts)
                            if (part.Slot != null && part.Slot.Kind == SlotKind.Text && b.Fields.TryGetValue(part.Slot.Key, out string f) && f == row.Name) b.Fields[part.Slot.Key] = name;
                    }
                Fire();
            });
        }

        /// <summary>Deleting asks once more; the blocks that used the variable are left empty, and the compiler marks them red.</summary>
        private void DeleteName(NameRow row)
        {
            string key = row.Kind + "|" + row.Name;
            if (_armed != key || Time.unscaledTime >= _armedUntil) { _armed = key; _armedUntil = Time.unscaledTime + 3f; Note(Words("scripts_delete_again")); return; }
            _armed = null;
            Snapshot();
            foreach (KeyValuePair<Block, string> use in Uses(row.List, row.Name).ToList()) use.Key.Fields[use.Value] = "";
            Prog.Vars.Remove(row.Name);
            Fire();
        }

        // ================================================================ pointer

        private Node Hit(Vector2 p)
        {
            bool inPalette = PaletteRect.Contains(p), inWork = !inPalette && WorkRect.Contains(p);
            if (!inPalette && !inWork) return null;
            Vector2 q = inWork ? ToWork(p) : p;
            float listTop = PaletteRect.y + _palHeader;
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                Node n = _nodes[i];
                if (n.Palette != inPalette || !n.R.Contains(q)) continue;
                if (n.Palette && n.Kind == NodeKind.Block && p.y < listTop) continue;   // (a block scrolled under the header)
                return n;
            }
            return null;
        }

        internal bool Pointer(Vector2 p, bool down, bool up)
        {
            _pointer = p;
            if (_panel)
            {
                if (down) { _pdown = HitPanel(p); if (_pdown == null && !PanelRect.Contains(p)) _panel = false; }
                else if (up && _pdown != null) { PBtn b = HitPanel(p); PBtn d = _pdown; _pdown = null; if (b != null && b.R == d.R) b.Do(); }
                return true;
            }
            if (_menu != null)
            {
                if (down) ClickMenu(p);
                return true;
            }
            if (down)
            {
                _down = p;
                _pressed = Hit(p);
                if (_pressed != null && _pressed.Kind == NodeKind.Tab) { _pressed.Do(); _pressed = null; _mode = Mode.None; return true; }
                if (_pressed != null && _pressed.Kind == NodeKind.Button) { if (!ReadOnly || _pressed.Text == "search" || _pressed.Text == "clear") _pressed.Do(); _pressed = null; _mode = Mode.None; return true; }
                if (_pressed != null && _pressed.Kind == NodeKind.Toggle)
                {
                    _pressed.T.Collapsed = !_pressed.T.Collapsed;
                    _pressed = null; _mode = Mode.None;
                    Fire();
                    return true;
                }
                if (_pressed != null && _pressed.Kind == NodeKind.Scroll) { _mode = Mode.ScrollPalette; ScrollPaletteTo(p); _pressed = null; return true; }
                if (_pressed != null) _mode = Mode.Pressed;
                else if (WorkRect.Contains(p) && !PaletteRect.Contains(p)) { _mode = Mode.PanWork; Selected = null; }
                else if (PaletteRect.Contains(p)) _mode = Mode.PanPalette;
                else _mode = Mode.None;
                return _mode != Mode.None;
            }
            switch (_mode)
            {
                case Mode.PanWork:
                    if (!up) { _wx -= (p.x - _down.x) / _zoom; _wy -= (p.y - _down.y) / _zoom; _down = p; ClampView(); }
                    break;
                case Mode.PanPalette:
                    if (!up) { _palY -= p.y - _down.y; _down = p; ClampPalette(); }
                    break;
                case Mode.ScrollPalette:
                    if (!up) ScrollPaletteTo(p);
                    break;
                case Mode.Pressed:
                    if (!up && !ReadOnly && (p - _down).sqrMagnitude > 49f) StartDrag(p);
                    break;
                case Mode.Dragging:
                    if (!up) UpdateDrop();
                    break;
            }
            if (up)
            {
                Mode was = _mode;
                _mode = Mode.None;
                if (was == Mode.Dragging) EndDrag(p);
                else if (was == Mode.Pressed && _pressed != null) Clicked(_pressed);
                _pressed = null;
            }
            return true;
        }

        private void ScrollPaletteTo(Vector2 p)
        {
            float view = PaletteRect.height - _palHeader;
            float thumbH = Mathf.Max(28f, view * view / Mathf.Max(view, _palContent));
            float t = Mathf.Clamp01((p.y - (PaletteRect.y + _palHeader) - thumbH / 2f) / Mathf.Max(1f, view - thumbH));
            _palY = t * Mathf.Max(0f, _palContent - view);
            ClampPalette();
        }

        internal void Wheel(Vector2 p, float delta)
        {
            if (Mathf.Abs(delta) < 0.001f) return;
            if (_panel) { _panelScroll -= delta > 0f ? 1 : -1; return; }
            if (_menu != null) { _menu.Scroll = Mathf.Clamp(_menu.Scroll - Mathf.Sign(delta), 0, Mathf.Max(0, MenuCount - 10)); return; }
            if (PaletteRect.Contains(p)) { _palY -= delta * 70f; ClampPalette(); }
            else if (WorkRect.Contains(p)) { _wy -= delta * 70f / _zoom; ClampView(); }
        }

        private void ClampView()
        {
            _wx = Mathf.Clamp(_wx, -60f, Mathf.Max(-60f, _contentW - WorkRect.width / _zoom + 120f));
            _wy = Mathf.Clamp(_wy, -60f, Mathf.Max(-60f, _contentH - WorkRect.height / _zoom + 120f));
        }

        private void ClampPalette() { _palY = Mathf.Clamp(_palY, 0f, Mathf.Max(0f, _palContent - (PaletteRect.height - _palHeader))); }

        // ---- clicks on a slot or a block ----

        private void Clicked(Node n)
        {
            if (n.Kind == NodeKind.Block)
            {
                if (n.Palette) return;
                Selected = n.B;
                if (!ReadOnly && IsNote(n.B.Def) && NoteSlot(n.B.Def) != null) EditText(n.B, NoteSlot(n.B.Def));
                return;
            }
            if (n.Kind != NodeKind.Slot || ReadOnly) return;
            Block b = n.B; Slot s = n.S;
            Selected = b;
            switch (s.Kind)
            {
                case SlotKind.Num:
                    Ask(Words("scripts_type_number"), Shown(b, s), 9, v =>
                    {
                        string t = (v ?? "").Trim().Replace(',', '.');
                        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d)) { Note(Words("scripts_not_a_number")); return; }
                        Snapshot();
                        d = Math.Round(d, 4);
                        b.Fields[s.Key] = d.ToString("0.####", CultureInfo.InvariantCulture);
                        Fire();
                        if (d > s.Max) Note(Words("scripts_num_at_most").Replace("{0}", s.Max.ToString("0.####", CultureInfo.InvariantCulture)));
                        else if (d < s.Min) Note(Words("scripts_num_at_least").Replace("{0}", s.Min.ToString("0.####", CultureInfo.InvariantCulture)));
                    });
                    break;
                case SlotKind.Text:
                    EditText(b, s);
                    break;
                case SlotKind.Pick:
                    OpenMenu(b, s, FromWork(n.R));
                    break;
            }
        }

        private void EditText(Block b, Slot s)
        {
            Ask(Words("scripts_type_words"), Shown(b, s), IsNote(b.Def) ? 400 : 120, v =>
            {
                Snapshot();
                b.Fields[s.Key] = (v ?? "").Replace('\n', ' ');
                Fire();
            });
        }

        private void ClickMenu(Vector2 p)
        {
            Rect r = MenuRect();
            Menu m = _menu;
            _menu = null;
            if (!r.Contains(p)) return;
            int rows = Mathf.Min(m.Choices.Length + (m.NewVar ? 1 : 0), 10);
            int first = Mathf.Clamp((int)m.Scroll, 0, Mathf.Max(0, m.Choices.Length + (m.NewVar ? 1 : 0) - rows));
            int index = first + Mathf.FloorToInt((p.y - r.y - 4f) / MenuRow);
            if (index < 0 || index >= m.Choices.Length + (m.NewVar ? 1 : 0)) return;
            if (m.NewVar && index == m.Choices.Length) { NewVariable(m.B); return; }
            Snapshot();
            m.B.Fields[m.S.Key] = m.Choices[index];
            Fire();
        }

        /// <summary>Makes a variable (asks for its name) and, if a block asked, puts it in that block.</summary>
        private void NewVariable(Block into)
        {
            Ask(Words("scripts_name_variable"), "", 20, v =>
            {
                string name = Naming.Var(v);
                if (name == null) return;
                if (Prog.Vars.Count >= Program.MaxVars && !Prog.Vars.Contains(name)) { Note(Words("scripts_too_many_variables")); return; }
                Snapshot();
                if (!Prog.Vars.Contains(name)) Prog.Vars.Add(name);
                if (into != null) into.Fields["v"] = name;
                Fire();
            });
        }

        // ---- dragging ----

        private void StartDrag(Vector2 p)
        {
            Node n = _pressed;
            if (n == null || n.Kind != NodeKind.Block) { _mode = Mode.None; return; }
            Block b = n.B;
            if (n.Palette)
            {
                if (b.Def == null) return;
                Block clone = b.Clone();
                _held = new List<Block> { clone };
                _grab = p - n.R.position;
                Snapshot();
            }
            else
            {
                string holder = HeldBy != null ? HeldBy(b) : null;
                if (holder != null) { Note(string.Format(Words("scripts_co_held_by"), holder)); _mode = Mode.None; return; }
                Snapshot();
                _held = Detach(b);
                if (_held == null) { _undo.RemoveAt(_undo.Count - 1); _mode = Mode.None; return; }
                _grab = ToWork(p) - n.R.position;
                Selected = b;
                _mode = Mode.Dragging;   // (before Fire: the screen sends nothing while a drag goes on, it sends the whole move when it ends)
                DragStarted?.Invoke(_held);
                Fire();
            }
            _mode = Mode.Dragging;
            _pointer = p;
            UpdateDrop();
        }

        private struct Where { internal List<Block> List; internal int Index; internal Block Owner; internal string Key; internal Program.Top Top; }

        private bool Locate(Block target, out Where w)
        {
            foreach (Program.Top top in Prog.Tops)
                if (LocateIn(top.Chain, target, top, out w)) return true;
            w = default(Where);
            return false;
        }

        private bool LocateIn(List<Block> list, Block target, Program.Top top, out Where w)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Block b = list[i];
                if (b == target) { w = new Where { List = list, Index = i, Top = top }; return true; }
                foreach (KeyValuePair<string, Block> kv in b.Inputs)
                    if (InputHas(kv.Value, target)) { w = FindInput(b, target); return true; }
                foreach (List<Block> body in b.Bodies.Values)
                    if (LocateIn(body, target, top, out w)) return true;
            }
            w = default(Where);
            return false;
        }

        private static bool InputHas(Block root, Block target) => root.All().Contains(target);

        private static Where FindInput(Block owner, Block target)
        {
            foreach (KeyValuePair<string, Block> kv in owner.Inputs)
            {
                if (kv.Value == target) return new Where { Owner = owner, Key = kv.Key };
                if (InputHas(kv.Value, target))
                    foreach (Block inner in kv.Value.All())
                        foreach (KeyValuePair<string, Block> kv2 in inner.Inputs)
                            if (kv2.Value == target) return new Where { Owner = inner, Key = kv2.Key };
            }
            return default(Where);
        }

        /// <summary>Takes a block out of the program: a stack block with everything under it, a round block alone.</summary>
        private List<Block> Detach(Block b)
        {
            if (!Locate(b, out Where w)) return null;
            if (w.Owner != null)
            {
                w.Owner.Inputs.Remove(w.Key);
                return new List<Block> { b };
            }
            bool isHat = b.Def != null && b.Def.Shape == Shape.Hat;
            List<Block> taken;
            if (isHat || (b.Def != null && b.Def.Shape == Shape.Value)) { taken = new List<Block> { b }; w.List.RemoveAt(w.Index); }
            else
            {
                taken = w.List.GetRange(w.Index, w.List.Count - w.Index);
                w.List.RemoveRange(w.Index, w.List.Count - w.Index);
            }
            if (w.Top != null && w.Top.Chain.Count == 0) Prog.Tops.Remove(w.Top);
            return taken;
        }

        private void UpdateDrop()
        {
            _drop = null;
            if (_held == null || _held.Count == 0) return;
            Block first = _held[0];
            BlockDef def = first.Def;
            if (PaletteRect.Contains(_pointer) || !WorkRect.Contains(_pointer)) return;
            Vector2 pw = ToWork(_pointer);
            if (def != null && def.Shape == Shape.Value)
            {
                Target best = null; float area = float.MaxValue;
                foreach (Target t in _targets)
                {
                    if (t.Kind != TargetKind.Socket || !t.R.Contains(pw)) continue;
                    if (t.S != null && !BlockDef.Fits(t.S.Type, def.Type) && t.S.Type != "any") continue;
                    if (_held.Contains(t.Owner)) continue;
                    float a = t.R.width * t.R.height;
                    if (a < area) { area = a; best = t; }
                }
                _drop = best;
                return;
            }
            if (def == null || def.Shape == Shape.Hat) return;
            Vector2 tl = ToWork(_pointer - _grab * _zoom);
            float bestD = 40f * 40f;
            foreach (Target t in _targets)
            {
                if (t.Kind == TargetKind.Socket) continue;
                float d = (t.P - tl).sqrMagnitude;
                if (d < bestD) { bestD = d; _drop = t; }
            }
        }

        private void EndDrag(Vector2 p)
        {
            try { EndDragCore(p); }
            finally { DragEnded?.Invoke(); }   // (after the move was sent: the others can change those blocks again)
        }

        private void EndDragCore(Vector2 p)
        {
            List<Block> held = _held;
            _held = null;
            Target drop = _drop;
            _drop = null;
            if (held == null || held.Count == 0) { Fire(); return; }
            if (PaletteRect.Contains(p)) { Note(Words("scripts_deleted_blocks")); Selected = null; Fire(); return; }
            if (drop != null && drop.Kind == TargetKind.Socket)
            {
                if (drop.Owner.Inputs.TryGetValue(drop.S.Key, out Block old)) AddTop(old, drop.R.x - WorkRect.x + _wx + 20f, drop.R.y - WorkRect.y + _wy + 34f);
                drop.Owner.Inputs[drop.S.Key] = held[0];
            }
            else if (drop != null && drop.Kind == TargetKind.BodyStart)
            {
                drop.List.InsertRange(0, held);
            }
            else if (drop != null && drop.Kind == TargetKind.After)
            {
                int index = Mathf.Min(drop.Index, drop.List.Count);
                drop.List.InsertRange(index, held);
            }
            else
            {
                Vector2 tl = ToWork(p - _grab * _zoom);
                // let go over the side panel or the toolbar and it still lands inside the workspace, where it can be found
                tl.x = Mathf.Clamp(tl.x, WorkRect.x + 8f, WorkRect.x + Mathf.Max(20f, WorkRect.width / _zoom - 140f));
                tl.y = Mathf.Clamp(tl.y, WorkRect.y + 8f, WorkRect.y + Mathf.Max(20f, WorkRect.height / _zoom - 50f));
                var top = new Program.Top { X = tl.x - WorkRect.x + _wx, Y = tl.y - WorkRect.y + _wy };
                top.Chain.AddRange(held);
                Prog.Tops.Add(top);
            }
            Fire();
        }

        private void AddTop(Block b, float x, float y)
        {
            var top = new Program.Top { X = x, Y = y };
            top.Chain.Add(b);
            Prog.Tops.Add(top);
        }

        // ================================================================ edits from the toolbar and the keyboard

        internal void Snapshot()
        {
            _undo.Add(Prog.ToJson());
            if (_undo.Count > 60) _undo.RemoveAt(0);
            _redo.Clear();
        }

        internal void Undo()
        {
            if (UndoHook != null && UndoHook()) return;
            if (_undo.Count == 0) return;
            _redo.Add(Prog.ToJson());
            Restore(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }

        internal void Redo()
        {
            if (RedoHook != null && RedoHook()) return;
            if (_redo.Count == 0) return;
            _undo.Add(Prog.ToJson());
            Restore(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }

        private void Restore(string json)
        {
            Program p = Program.FromJson(json, out _);
            if (p == null) return;
            p.Name = Prog.Name; p.About = Prog.About;
            Prog = p;
            Selected = null; _menu = null;
            Fire();
        }

        // ---- other people's changes (the session changed the program in place) ----

        /// <summary>Somebody else's changes were done to the program: measure again, and let go of what no longer exists.</summary>
        internal void External(IEnumerable<string> ids, bool structural)
        {
            Touch();
            if (Selected != null && Selected.Uid.Length > 0 && !Tree.Has(Prog, Selected.Uid)) Selected = null;
            if (_menu != null && _menu.B != null && _menu.B.Uid.Length > 0 && !Tree.Has(Prog, _menu.B.Uid)) _menu = null;
        }

        /// <summary>A snapshot replaced the program's blocks (the same program object): find the selection again by its id.</summary>
        internal void Rebind()
        {
            Touch();
            string sel = Selected != null ? Selected.Uid : "";
            Selected = sel.Length > 0 ? Tree.Get(Prog, sel) : null;
            _menu = null;
        }

        internal string SelectedUid => Selected != null ? Selected.Uid : "";

        /// <summary>Where this editor's pointer is on the workspace, in workspace coordinates (the others see it there); false when it is elsewhere.</summary>
        internal bool TryPointerWork(out float x, out float y)
        {
            x = y = 0f;
            if (!WorkRect.Contains(_pointer) || PaletteRect.Contains(_pointer)) return false;
            Vector2 q = ToWork(_pointer);
            x = q.x - WorkRect.x + _wx;
            y = q.y - WorkRect.y + _wy;
            return true;
        }

        /// <summary>The others: an outline on what they have selected (with their name), what they are moving dimmed, and their pointer.</summary>
        private void DrawOthers()
        {
            if (Others.Count == 0) return;
            _p.To("work", 30);
            foreach (Mark m in Others)
            {
                Color c = Style.Presence(m.Color);
                if (m.Selected.Length > 0 && Tree.Find(Prog, m.Selected, out Loc loc) && _where.TryGetValue(loc.Block, out Vector2 wp))
                {
                    Vector2 s = Size(loc.Block);
                    Rect r = new Rect(WorkRect.x + wp.x - _wx, WorkRect.y + wp.y - _wy, s.x, s.y);
                    if (Overlaps(_vis, r.x, r.y, s)) { Border(r, c); Tag(r.x - 3f, r.y - 19f, m.Name, c); }
                }
                bool first = true;
                foreach (string uid in m.Grabbed)
                    if (Tree.Find(Prog, uid, out Loc held) && _where.TryGetValue(held.Block, out Vector2 hp))
                    {
                        Vector2 s = Size(held.Block);
                        Rect r = new Rect(WorkRect.x + hp.x - _wx, WorkRect.y + hp.y - _wy, s.x, s.y);
                        if (!Overlaps(_vis, r.x, r.y, s)) continue;
                        _p.To("work", 30);
                        _p.Rect(r.x, r.y, r.width, r.height, new Color(0f, 0f, 0f, 0.5f));
                        Border(r, c);
                        if (first) { Tag(r.x, r.y + r.height / 2f - 9f, string.Format(Words("scripts_co_moving"), m.Name), c); first = false; }
                    }
                if (m.HasPointer)
                {
                    float px = WorkRect.x + m.X - _wx, py = WorkRect.y + m.Y - _wy;
                    if (px > _vis.x && py > _vis.y && px < _vis.xMax && py < _vis.yMax)
                    {
                        _p.To("work", 32);
                        _p.Rect(px, py, 3f, 15f, c, false); _p.Rect(px + 3f, py + 3f, 3f, 10f, c, false); _p.Rect(px + 6f, py + 6f, 3f, 5f, c, false);
                        Tag(px + 12f, py + 12f, m.Name, c);
                    }
                }
            }
        }

        private void Border(Rect r, Color c)
        {
            _p.To("work", 30);
            _p.Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f, c, false);
            _p.Rect(r.x - 3f, r.yMax, r.width + 6f, 3f, c, false);
            _p.Rect(r.x - 3f, r.y, 3f, r.height, c, false);
            _p.Rect(r.xMax, r.y, 3f, r.height, c, false);
        }

        private void Tag(float x, float y, string text, Color c)
        {
            float w = _p.Width(text, 12f) + 10f;
            _p.To("work", 33);
            _p.Rect(x, y, w, 17f, c);
            _p.Text(text, x + 5f, y, w, 17f, 12f, Color.white, TextAlignmentOptions.MidlineLeft);
        }

        internal void DeleteSelected()
        {
            if (Selected == null || ReadOnly || !Locate(Selected, out Where w)) return;
            Snapshot();
            if (w.Owner != null) w.Owner.Inputs.Remove(w.Key);
            else { w.List.RemoveAt(w.Index); if (w.Top != null && w.Top.Chain.Count == 0) Prog.Tops.Remove(w.Top); }
            Selected = null;
            Fire();
        }

        internal void DuplicateSelected()
        {
            if (Selected == null || ReadOnly) return;
            Snapshot();
            Block copy = Selected.Clone();
            Vector2 at = _where.TryGetValue(Selected, out Vector2 v) ? v : new Vector2(40f, 40f);
            AddTop(copy, at.x + 30f, at.y + 30f);
            Selected = copy;
            Fire();
        }

        /// <summary>Keys: undo, redo, copy (remember the selected block), paste (a copy of what was remembered), delete, search.</summary>
        internal void Shortcut(string what)
        {
            if (_panel || _menu != null) return;
            switch (what)
            {
                case "undo": if (!ReadOnly) Undo(); break;
                case "redo": if (!ReadOnly) Redo(); break;
                case "delete": DeleteSelected(); break;
                case "search": OpenSearch(); break;
                case "copy": if (Selected != null) { _clipBlock = Selected.Clone(); Note(Words("scripts_copied_block")); } break;
                case "paste":
                    if (_clipBlock != null && !ReadOnly)
                    {
                        Snapshot();
                        Block copy = _clipBlock.Clone();
                        Vector2 at = Selected != null && _where.TryGetValue(Selected, out Vector2 v) ? v + new Vector2(30f, 30f) : new Vector2(_wx + 60f, _wy + 60f);
                        AddTop(copy, at.x, at.y);
                        Selected = copy;
                        Fire();
                    }
                    break;
            }
        }

        // ---- the view ----

        internal void SetZoom(float z)
        {
            z = Mathf.Clamp(Mathf.Round(z * 10f) / 10f, 0.5f, 1.5f);
            if (Mathf.Approximately(z, _zoom)) return;
            float cx = _wx + WorkRect.width / 2f / _zoom, cy = _wy + WorkRect.height / 2f / _zoom;   // (keep the middle where it is)
            _zoom = z;
            _wx = cx - WorkRect.width / 2f / _zoom;
            _wy = cy - WorkRect.height / 2f / _zoom;
            ClampView();
        }

        internal void ZoomIn() => SetZoom(_zoom + 0.1f);
        internal void ZoomOut() => SetZoom(_zoom - 0.1f);

        /// <summary>Zoom and scroll so every stack is in view (never bigger than life).</summary>
        internal void FitAll()
        {
            if (Prog.Tops.Count == 0) { _zoom = 1f; _wx = _wy = 0f; return; }
            RebuildCollapsed();
            if (_cacheVersion != _layoutVersion) { _size.Clear(); _segs.Clear(); _notes.Clear(); _labels.Clear(); _chips.Clear(); _templates.Clear(); _cacheVersion = _layoutVersion; }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = 0f, maxY = 0f;
            foreach (Program.Top t in Prog.Tops)
            {
                Vector2 s = ChainSize(t.Chain);
                minX = Mathf.Min(minX, t.X); minY = Mathf.Min(minY, t.Y);
                maxX = Mathf.Max(maxX, t.X + s.x + 24f); maxY = Mathf.Max(maxY, t.Y + s.y);
            }
            float z = Mathf.Min(WorkRect.width / (maxX - minX + 80f), WorkRect.height / (maxY - minY + 80f));
            _zoom = Mathf.Clamp(Mathf.Floor(z * 10f) / 10f, 0.5f, 1f);
            _wx = minX - 40f; _wy = minY - 40f;
            _contentW = maxX; _contentH = maxY;
            ClampView();
        }

        /// <summary>Arranges the stacks in columns, top to bottom in the order they were (by place), without overlap.</summary>
        internal void Tidy()
        {
            if (ReadOnly || Prog.Tops.Count == 0) return;
            Snapshot();
            RebuildCollapsed();
            if (_cacheVersion != _layoutVersion) { _size.Clear(); _segs.Clear(); _notes.Clear(); _labels.Clear(); _chips.Clear(); _templates.Clear(); _cacheVersion = _layoutVersion; }
            List<Program.Top> order = Prog.Tops.OrderBy(t => Mathf.Round(t.X / 400f)).ThenBy(t => t.Y).ToList();
            float x = 30f, y = 30f, colW = 0f;
            const float Gap = 26f, ColumnHeight = 1100f;
            foreach (Program.Top t in order)
            {
                Vector2 s = ChainSize(t.Chain);
                if (y > 30f && y + s.y > ColumnHeight) { x += colW + 60f; y = 30f; colW = 0f; }
                t.X = x; t.Y = y;
                y += s.y + Gap;
                colW = Mathf.Max(colW, s.x + 24f);
            }
            Fire();
            Note(Words("scripts_tidied"));
        }

        /// <summary>Scrolls the workspace so a block is in view.</summary>
        internal void Show(Block b)
        {
            if (!_where.TryGetValue(b, out Vector2 at))
            {
                foreach (Program.Top top in Prog.Tops) if (top.Chain.Any(c => c.All().Contains(b))) { at = new Vector2(top.X, top.Y); if (top.Collapsed) { top.Collapsed = false; Fire(); } break; }
            }
            _wx = at.x - 40f; _wy = at.y - 40f;
            ClampView();
            Selected = b;
        }

        internal void CloseMenu() { _menu = null; _panel = false; }
        internal bool MenuOpen => _menu != null || _panel;
    }
}
