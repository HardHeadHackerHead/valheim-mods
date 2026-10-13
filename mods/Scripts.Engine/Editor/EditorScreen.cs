using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Scripts.Blocks;
using TMPro;
using UnityEngine;
using Scripts.Mp;
using static Scripts.Editor.Words;

namespace Scripts.Editor
{
    /// <summary>What the editor needs from whatever shows it: a screen in the world (the Terminal) or a window.</summary>
    internal interface IEditorHost
    {
        /// <summary>The X button: leave the editor (close the window, or the program on the computer).</summary>
        void Close();
        /// <summary>The script that was picked last (the Terminal keeps it with the computer, a window in the editor's memory).</summary>
        string Picked { get; set; }
    }

    /// <summary>
    /// The scripting program: the list of scripts, the block editor, the code, the powers and the log. It draws a 1280 x 720 picture
    /// with a Painter and takes a pointer (position, button down, button up, wheel), and nothing else, so one copy of it runs on the
    /// Terminal's screen and in a window. It talks to the scripting host directly (the Terminal reaches it through HostBridge).
    /// </summary>
    internal sealed partial class EditorScreen
    {
        internal const float W = 1280f, H = 720f;
        private enum Page { Library, Blocks, Code, Log }

        private readonly Painter _p;
        private readonly BlockView _view;
        private readonly IEditorHost _host;

        /// <summary>Where the scripts are now: this game, or a server that has the mod (Mp/Backend.cs).</summary>
        private static IScriptsBackend B => Backend.Current;
        private IScriptsBackend _beSeen;

        private Page _page = Page.Library;
        private string _file = "";                       // the script open (its .js name)
        private Program _prog;
        private Compiled _compiled;
        private bool _dirty, _blocksScript;
        private float _saveAt, _nextList, _noteUntil, _deleteArmedUntil;
        private string _note = "";
        private string[][] _rows = new string[0][];
        private int _libTop, _codeTop, _codeLines;
        private string _codeShown;
        private string[] _codeSplit = new string[0];
        private string[] _log = new string[0], _refused = new string[0];
        private string _refusedKey;
        private int _probScroll, _limitScroll, _logScroll;   // scrolls in the side panel (the log counts lines back from the newest)
        private string _clearedLine;                          // the log line that was newest when Clear was pressed
        private int _blockCount, _codeBytes;
        private string _codeQuery = "";
        private readonly Dictionary<int, List<Tok>> _tokens = new Dictionary<int, List<Tok>>();

        // buttons: drawn and recorded each picture, hit on release
        private sealed class Btn { internal Rect R; internal Action Do; }
        private readonly List<Btn> _btns = new List<Btn>();
        private Btn _downBtn;
        private Vector2 _pointer;

        // the "run?" box
        private string[] _modalNeeds;
        private string _modalFile;

        internal EditorScreen(RectTransform root, IEditorHost host, TMP_FontAsset font)
        {
            _host = host;
            _p = new Painter(root, font);
            _p.Area("back", new Rect(0f, 0f, W, H), false);
            _p.Area("palette", new Rect(0f, 56f, 300f, 592f));
            _p.Area("work", new Rect(300f, 56f, 660f, 592f));
            _p.Area("ui", new Rect(0f, 0f, W, H), false);
            _p.Area("held", new Rect(0f, 0f, W, H), false);
            _p.Area("popup", new Rect(0f, 0f, W, H), false);
            _p.Area("modal", new Rect(0f, 0f, W, H), false);
            _view = new BlockView(_p)
            {
                Changed = OnChanged,
                Ask = Ask,
                Note = s => Note(s),
                PartsOf = Words.Parts,
                Words = key => L(key, English(key)),
            };
            HookCollab();
            string picked = host.Picked;
            if (!string.IsNullOrEmpty(picked) && B.HasBlocks(picked)) OpenBlocks(picked, false);
            else _file = picked ?? "";
        }

        internal void Destroy() { Flush(); CloseSession(); _p.Clear(); }

        // ================================================================ the program

        private void Note(string text, float seconds = 4f) { _note = text; _noteUntil = Time.unscaledTime + seconds; }

        private void OnChanged()
        {
            if (_co != null)
            {
                // a live script: what changed is sent when a gesture ends (not while a block is being dragged), and the server saves
                if (_view.Dragging) return;
                _prog = _view.Prog;
                Recompile();
                if (_co.Active) _co.Commit();
                return;
            }
            _prog = _view.Prog;
            Recompile();
            _dirty = true;
            _saveAt = Time.unscaledTime + 0.6f;
        }

        private void Recompile()
        {
            _compiled = Compiler.Compile(_prog);
            _blockCount = _prog.CountBlocks();
            _codeBytes = _compiled.Ok ? Encoding.UTF8.GetByteCount(_compiled.Js) : 0;
            _view.Problems = new Dictionary<int, bool>();
            foreach (Problem pr in _compiled.Problems) if (pr.Block != null) { _view.Problems.TryGetValue(pr.Block.Id, out bool f); _view.Problems[pr.Block.Id] = f || pr.Fatal; }
        }

        /// <summary>Writes the changes now (when leaving, running, or a moment after the last edit).</summary>
        private void Flush(bool save = false)
        {
            if (_co != null)
            {
                if (_co.Active) { _co.Commit(); if (save) _co.Save(); }
                return;
            }
            if (!_dirty || !_blocksScript || _file.Length == 0 || _prog == null) return;
            _dirty = false;
            B.SaveBlocks(_file, _prog, _compiled, why => { if (!string.IsNullOrEmpty(why)) Note(why); });
        }

        private void OpenBlocks(string file, bool show)
        {
            Flush();
            CloseSession();
            IScriptsBackend be = B;
            CollabSession live = be.OpenCollab(file);   // the script as people edit it together (the host and admins); null: this game cannot
            if (live != null) { StartSession(live, file, be); return; }
            be.LoadBlocks(file, (p, error) =>
            {
                if (!ReferenceEquals(B, be)) return;   // (the connection changed while it was loading)
                if (p == null) { Note(!string.IsNullOrEmpty(error) ? error : L("scripts_cant_open", "Could not open that script.")); return; }
                _file = file; _prog = p; _blocksScript = true;
                _clearedLine = null; _logScroll = _probScroll = _limitScroll = 0;
                _view.Load(p);
                _view.ReadOnly = !be.CanChange;
                Recompile();
                _dirty = false;
                _host.Picked = file;
                _page = Page.Blocks;
            });
        }

        private void Pick(string file)
        {
            Flush();
            _file = file;
            _clearedLine = null; _logScroll = 0;
            _blocksScript = B.HasBlocks(file);
            _host.Picked = file;
        }

        private void Back()
        {
            Flush();
            CloseSession();
            _page = Page.Library;
            _nextList = 0f;
        }

        // ================================================================ text boxes

        private sealed class Receiver : TextReceiver
        {
            private readonly string _current;
            private readonly Action<string> _done;
            internal Receiver(string current, Action<string> done) { _current = current; _done = done; }
            public string GetText() => _current;
            public void SetText(string text) { _done(text); }
        }

        private void Ask(string title, string current, int limit, Action<string> done)
        {
            if (TextInput.instance == null) { Note(L("scripts_no_text_box", "The game's text box is not ready.")); return; }
            TextInput.instance.RequestText(new Receiver(current, done), title, limit);
        }

        // ================================================================ one picture

        internal void Draw()
        {
            IScriptsBackend backend = B;
            if (!ReferenceEquals(backend, _beSeen))
            {
                if (_beSeen != null) BackendChanged();
                _beSeen = backend;
            }
            backend.Pump();
            if (_co != null) UpdateSession();
            if (_dirty && Time.unscaledTime >= _saveAt && !_view.Dragging) Flush();
            if (Time.unscaledTime >= _nextList)
            {
                _nextList = Time.unscaledTime + 0.4f;
                Refresh();
            }
            _p.Begin();
            _btns.Clear();
            _p.To("back", 0);
            _p.Rect(0f, 0f, W, H, Style.Back, false);
            switch (_page)
            {
                case Page.Library: DrawLibrary(); break;
                case Page.Blocks: DrawBlocksPage(); break;
                case Page.Code: DrawCodePage(); break;
                case Page.Log: DrawLogPage(); break;
            }
            if (_modalNeeds != null) DrawModal();
            _p.End();
        }

        /// <summary>Joined or left a server: what was open belonged to the other place.</summary>
        private void BackendChanged()
        {
            CloseSession();
            _page = Page.Library;
            _file = ""; _prog = null; _compiled = null; _blocksScript = false; _dirty = false;
            _rows = new string[0][]; _log = new string[0]; _refused = new string[0]; _modalNeeds = null;
            _view.Load(new Program());
            _host.Picked = "";
            _nextList = 0f;
        }

        private void Refresh()
        {
            IScriptsBackend be = B;
            _rows = be.List();
            string[] row = _rows.FirstOrDefault(r => r[0] == _file);
            if (row == null && _page == Page.Library) { _file = _rows.Length > 0 ? _rows[0][0] : ""; _blocksScript = be.HasBlocks(_file); }
            if (_page == Page.Library && _file.Length > 0 && !be.HasBlocks(_file) && be.CanRead) BaseHash(_file);   // (the version of this text script that is on screen now)
            _log = _file.Length > 0 ? be.Log(_file, 200) : new string[0];
            _refused = _file.Length > 0 ? be.Refused(_file) : new string[0];
            string refusedKey = _file + "|" + string.Join("|", _refused);
            if (refusedKey != _refusedKey)
            {
                // a refusal is not an error, so nothing else would tell the coder: say it where they are looking, and what to do about it
                if (_refusedKey != null && _refusedKey.StartsWith(_file + "|") && _refused.Length > 0) Note(L("scripts_refused", "refused: ") + _refused[_refused.Length - 1], 9f);
                _refusedKey = refusedKey;
            }
            if (_page == Page.Code)
            {
                string source = _blocksScript && _compiled != null && _compiled.Ok ? _compiled.Js : be.Source(_file);
                if (!ReferenceEquals(source, _codeShown) && source != _codeShown) { _codeShown = source; _tokens.Clear(); _codeSplit = (source ?? "").Replace("\r", "").Split('\n'); _codeLines = _codeSplit.Length; }
            }
        }

        // ---- small drawing helpers ----

        private void Text(string text, float x, float y, float w, float h, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            _p.Text(text, x, y, w, h, size, color, align);
        }

        private void Button(Rect r, string text, Action act, bool enabled = true, Color? fill = null, float size = 17f)
        {
            bool hover = enabled && r.Contains(_pointer) && _modalNeeds == null;
            Color c = !enabled ? Style.ButtonOff : (fill ?? Style.Button);
            if (hover) c = Style.Lighter(c, 0.18f);
            _p.To("ui", 2);
            _p.Rect(r.x, r.y, r.width, r.height, c);
            _p.Text(text, r.x + 4f, r.y, r.width - 8f, r.height, size, enabled ? Color.white : Style.Dim, TextAlignmentOptions.Midline);
            if (enabled && act != null) _btns.Add(new Btn { R = r, Do = act });
        }

        private void Bar(float x, float y, float w, float h)
        {
            _p.To("ui", 0);
            _p.Rect(x, y, w, h, Style.Bar, false);
        }

        private string StateOf(string[] row) => row == null ? "" : row[2];

        private static string StateWord(string state)
        {
            switch (state)
            {
                case "Running": return L("scripts_running", "running");
                case "Paused": return L("scripts_paused", "paused");
                case "Failed": return L("scripts_failed", "failed");
                default: return L("scripts_stopped", "stopped");
            }
        }

        private static Color StateColor(string state) => state == "Running" ? Style.Good : state == "Paused" ? Style.Warn : state == "Failed" ? Style.Bad : Style.Dim;

        // ================================================================ the library

        private void DrawLibrary()
        {
            Bar(0f, 0f, W, 56f);
            _p.To("ui", 1);
            Text(L("scripts_title", "Blockscript"), 16f, 0f, 130f, 56f, 26f, Color.white);
            IScriptsBackend be = B;
            DrawOrigin(be, 150f, 0f, 480f, 56f);
            bool can = be.CanChange;
            Button(new Rect(640f, 8f, 150f, 40f), L("scripts_new_blocks", "New blocks"), NewBlocks, can);
            Button(new Rect(798f, 8f, 150f, 40f), L("scripts_paste", "Paste code"), PasteCode, can);
            Button(new Rect(1184f, 8f, 84f, 40f), L("scripts_close", "Close"), _host.Close);

            // the list
            const float RowH = 54f;
            int rowsVisible = 10;
            _libTop = Mathf.Clamp(_libTop, 0, Mathf.Max(0, _rows.Length - rowsVisible));
            for (int i = 0; i < rowsVisible && _libTop + i < _rows.Length; i++)
            {
                string[] row = _rows[_libTop + i];
                Rect r = new Rect(8f, 64f + i * RowH, 612f, RowH - 4f);
                bool on = row[0] == _file;
                string file = row[0];
                _p.To("ui", 1);
                _p.Rect(r.x, r.y, r.width, r.height, on ? new Color(0.24f, 0.27f, 0.45f) : Style.Panel);
                Text(row[1], r.x + 12f, r.y + 2f, 380f, 28f, 20f, Color.white);
                string others = OthersEditing(row);
                Text(others.Length > 0 ? L("scripts_co_row", "{0} editing now", others) + "  -  " + row[6] : row[6], r.x + 12f, r.y + 28f, 450f, 22f, 14f, others.Length > 0 ? Style.Accent : Style.Dim);
                Text(StateWord(row[2]), r.xMax - 170f, r.y + 2f, 90f, 28f, 16f, StateColor(row[2]), TextAlignmentOptions.MidlineRight);
                if (row.Length > 7 && row[7] != "0") { _p.Rect(r.xMax - 170f, r.y + 28f, 100f, 20f, Style.Warn.WithAlpha(0.6f)); Text(L("scripts_tag_limit", "limit hit"), r.xMax - 170f, r.y + 28f, 100f, 20f, 13f, Color.white, TextAlignmentOptions.Midline); }
                if (be.HasBlocks(file)) { _p.Rect(r.xMax - 70f, r.y + 8f, 58f, 22f, Style.Category("events")); Text(L("scripts_tag_blocks", "blocks"), r.xMax - 70f, r.y + 8f, 58f, 22f, 14f, Color.white, TextAlignmentOptions.Midline); }
                _btns.Add(new Btn { R = r, Do = () => Pick(file) });
            }
            if (_rows.Length == 0) Text(L("scripts_none", "No scripts yet. Press New blocks."), 24f, 90f, 580f, 40f, 18f, Style.Dim);
            if (_rows.Length > rowsVisible) Text(L("scripts_more", "{0} - {1} of {2}  (mouse wheel)", _libTop + 1, Mathf.Min(_rows.Length, _libTop + rowsVisible), _rows.Length), 8f, 612f, 600f, 24f, 14f, Style.Dim);

            // the one picked
            string[] cur = _rows.FirstOrDefault(r => r[0] == _file);
            _p.To("ui", 0);
            _p.Rect(640f, 64f, 632f, 576f, Style.Panel);
            if (cur == null) { Text(L("scripts_pick", "Pick a script on the left."), 660f, 80f, 580f, 30f, 18f, Style.Dim); return; }
            _p.To("ui", 1);
            Text(cur[1], 660f, 70f, 590f, 36f, 24f, Color.white);
            Text(cur[6], 660f, 106f, 590f, 24f, 15f, Style.Dim);
            Text(StateWord(cur[2]) + (cur[3].Length > 0 ? ": " + cur[3] : ""), 660f, 130f, 590f, 26f, 15f, StateColor(cur[2]));
            bool blocks = be.HasBlocks(_file);
            string file2 = _file;
            Button(new Rect(660f, 166f, 150f, 40f), blocks ? L("scripts_edit_blocks", "Edit blocks") : L("scripts_view_code", "View code"), () => { if (blocks) OpenBlocks(file2, true); else { _page = Page.Code; _codeTop = 0; _codeShown = null; _nextList = 0f; } }, be.CanRead, blocks ? Style.Category("events") : null);
            Button(new Rect(818f, 166f, 100f, 40f), L("scripts_run", "Run"), () => RunFile(file2), can, Style.Good.WithAlpha(0.55f));
            Button(new Rect(926f, 166f, 100f, 40f), L("scripts_stop", "Stop"), () => { be.Stop(file2); Note(L("scripts_stopped_note", "Stopped.")); }, can);
            Button(new Rect(1034f, 166f, 120f, 40f), Time.unscaledTime < _deleteArmedUntil ? L("scripts_sure", "Sure?") : L("scripts_delete", "Delete"), DeleteArmed, can, Style.Bad.WithAlpha(0.5f));
            Button(new Rect(1162f, 166f, 100f, 40f), L("scripts_replace", "Replace"), () => ReplaceCode(file2), can && !blocks, null, 15f);
            DrawStale(file2);

            DrawPowers(cur, 660f, 214f, 590f, 3);
            _p.To("ui", 1);
            Text(be.LimitsFromServer ? L("scripts_limits_server", "This server's limits:") : L("scripts_limits", "Limits (set by the world's owner):"), 660f, 360f, 590f, 22f, 14f, Style.Dim);
            string[] needsNow = cur[4].Length > 0 ? cur[4].Split(',') : new string[0];
            string[] limitLines = be.LimitLines(needsNow);
            for (int i = 0; i < Mathf.Min(limitLines.Length, 6); i++) Text(limitLines[i], 668f, 382f + i * 18f, 580f, 18f, 13f, Style.Text);
            if (be.CanRead)
            {
                LogHeader(660f, 494f, 590f, _refused.Length > 0 ? L("scripts_limit_hit_title", "Limit hit - something was refused:") : L("scripts_log", "Log"), _refused.Length > 0 ? Style.Warn : Style.Dim);
                _p.To("ui", 0);
                _p.Rect(654f, 518f, 606f, 116f, Style.Back);
                DrawLog(662f, 520f, 590f, 112f, 7);
            }
            else
            {
                _p.To("ui", 1);
                Text(L("scripts_admin_logs", "Only admins can see a server's code and logs."), 660f, 500f, 590f, 28f, 15f, Style.Dim);
            }
            DrawStatus();
        }

        private void DrawPowers(string[] row, float x, float y, float w, int max)
        {
            string[] needs = row[4].Length > 0 ? row[4].Split(',') : new string[0];
            string[] approved = row[5].Length > 0 ? row[5].Split(',') : new string[0];
            _p.To("ui", 1);
            Text(needs.Length > 0 ? L("scripts_needs", "This script wants to:") : L("scripts_needs_none", "This script needs no special powers."), x, y, w, 24f, 16f, Style.Dim);
            for (int i = 0; i < Mathf.Min(needs.Length, max); i++)
            {
                string power = needs[i];
                bool on = approved.Contains(power);
                Rect r = new Rect(x, y + 28f + i * 38f, w, 34f);
                bool risky = Risky(power);
                _p.To("ui", 1);
                _p.Rect(r.x, r.y, r.width, r.height, on ? new Color(0.2f, 0.36f, 0.28f) : (risky ? new Color(0.4f, 0.22f, 0.2f) : new Color(0.3f, 0.26f, 0.2f)));
                Text(Power(power), r.x + 10f, r.y, r.width - 110f, r.height, 15f, Color.white);
                Text(on ? L("scripts_allowed", "allowed") : L("scripts_allow", "allow"), r.xMax - 100f, r.y, 90f, r.height, 15f, on ? Style.Good : Style.Warn, TextAlignmentOptions.MidlineRight);
                string file = row[0];
                if (B.CanChange) _btns.Add(new Btn { R = r, Do = () => { B.Allow(file, power, !on); _nextList = 0f; } });
            }
        }

        /// <summary>The log since Clear was pressed.</summary>
        private string[] VisibleLog()
        {
            if (_clearedLine == null) return _log;
            for (int i = _log.Length - 1; i >= 0; i--)
                if (_log[i] == _clearedLine) return _log.Skip(i + 1).ToArray();
            return _log;
        }

        private void DrawLog(float x, float y, float w, float h, int lines)
        {
            _p.To("ui", 1);
            float lh = h / lines;
            int top = 0;
            // what the host refused to do comes first, in the warning colour: "two boars" that became one is a cap, and it says so here
            for (int i = 0; i < Mathf.Min(_refused.Length, 3); i++, top++)
                Text(L("scripts_refused", "refused: ") + _refused[i], x, y + top * lh, w, lh, 13f, Style.Warn);
            string[] log = VisibleLog();
            int room = lines - top;
            int maxBack = Mathf.Max(0, log.Length - room);
            _logScroll = Mathf.Clamp(_logScroll, 0, maxBack);
            int end = log.Length - _logScroll, start = Mathf.Max(0, end - room);
            for (int i = start; i < end; i++)
            {
                string line = log[i];
                string body = line.Length > 9 && line[2] == ':' && line[5] == ':' ? line.Substring(9) : line;   // (after the time)
                Color c;
                if (body.StartsWith("ERROR")) c = Style.Bad;
                else if (body.StartsWith("WARN") || body.StartsWith("LIMIT")) c = Style.Warn;
                else if (body.StartsWith("DID")) c = new Color(0.6f, 0.75f, 0.95f);
                else if (body.StartsWith("DEBUG")) c = Style.Dim;
                else c = new Color(0.7f, 0.82f, 0.7f);
                Text(line, x, y + (top + i - start) * lh, w, lh, 13f, c);
            }
            if (_logScroll > 0) Text(L("scripts_log_older", "older lines - {0} newer below", _logScroll), x, y + h - lh, w, lh, 12f, Style.Dim, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>The log's heading, with the Clear button.</summary>
        private void LogHeader(float x, float y, float w, string title, Color color)
        {
            _p.To("ui", 1);
            Text(title, x, y, w - 160f, 22f, 14f, color);
            Button(new Rect(x + w - 152f, y, 74f, 22f), L("scripts_open", "Open"), OpenLogPage, _file.Length > 0, null, 12f);
            Button(new Rect(x + w - 74f, y, 74f, 22f), L("scripts_clear", "Clear"), () => { _clearedLine = _log.Length > 0 ? _log[_log.Length - 1] : null; _logScroll = 0; }, _log.Length > 0, null, 12f);
        }

        private void DrawStatus()
        {
            _p.To("ui", 0);
            _p.Rect(0f, 648f, W, 28f, Style.Bar, false);
            _p.To("ui", 1);
            IScriptsBackend be = B;
            string standing = be.Note.Length > 0 ? be.Note : be.ReadOnlyReason;
            string msg = Time.unscaledTime < _noteUntil ? _note : standing;
            Text(msg, 14f, 648f, W - 28f, 28f, 15f, Time.unscaledTime < _noteUntil ? Style.Text : Style.Warn);
        }

        private void NewBlocks()
        {
            Ask(L("scripts_name_script", "Name the script"), "", 40, name =>
            {
                B.CreateBlocks(name, file =>
                {
                    if (file.Length == 0) { Note(L("scripts_cant", "Could not make that script.")); return; }
                    _nextList = 0f;
                    Refresh();
                    OpenBlocks(file, true);
                });
            });
        }

        private void PasteCode()
        {
            string text = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text)) { Note(L("scripts_clipboard_empty", "The clipboard is empty.")); return; }
            IScriptsBackend be = B;
            be.New("pasted", file =>
            {
                if (file.Length == 0) { Note(L("scripts_cant", "Could not make that script.")); return; }
                be.Write(file, text, "", (why, stale) =>
                {
                    if (why.Length > 0) { Note(why); return; }
                    Pick(file);
                    _nextList = 0f;
                    Note(L("scripts_pasted", "Pasted."));
                });
            });
        }

        private void DeleteArmed()
        {
            if (_file.Length == 0) return;
            if (Time.unscaledTime < _deleteArmedUntil)
            {
                B.Delete(_file, ok => { if (!ok) Note(L("scripts_cant_delete", "Could not delete that script.")); });
                _file = ""; _blocksScript = false; _host.Picked = "";
                _deleteArmedUntil = 0f; _nextList = 0f;
                Note(L("scripts_deleted", "Deleted."));
            }
            else { _deleteArmedUntil = Time.unscaledTime + 3f; Note(L("scripts_delete_again", "Press Delete again to delete it for good.")); }
        }

        // ================================================================ running

        private void RunFile(string file)
        {
            Flush(true);   // (a live script is written by the server now, so what runs is what is on screen)
            string[] row = _rows.FirstOrDefault(r => r[0] == file);
            if (row == null) { Refresh(); row = _rows.FirstOrDefault(r => r[0] == file); }
            if (row == null) return;
            string[] needs = row[4].Length > 0 ? row[4].Split(',') : new string[0];
            string[] approved = row[5].Length > 0 ? row[5].Split(',') : new string[0];
            string[] missing = needs.Where(n => !approved.Contains(n) && n != "messages" && n != "store").ToArray();
            if (missing.Length > 0) { _modalNeeds = missing; _modalFile = file; return; }
            Start(file);
        }

        private void Start(string file)
        {
            B.Start(file, why =>
            {
                Note(string.IsNullOrEmpty(why) ? L("scripts_started", "Started.") : why);
                _nextList = 0f;
            });
        }

        private void DrawModal()
        {
            _p.To("modal", 0);
            _p.Rect(0f, 0f, W, H, new Color(0f, 0f, 0f, 0.6f), false);
            float h = 190f + _modalNeeds.Length * 34f;
            Rect box = new Rect(340f, (H - h) / 2f, 600f, h);
            _p.Rect(box.x, box.y, box.width, box.height, new Color(0.15f, 0.16f, 0.23f));
            _p.To("modal", 1);
            Text(L("scripts_run_asks", "This script wants to:"), box.x + 24f, box.y + 14f, 550f, 34f, 22f, Color.white);
            for (int i = 0; i < _modalNeeds.Length; i++)
            {
                string power = _modalNeeds[i];
                Color c = Risky(power) ? Style.Bad : Style.Text;
                Text("- " + Power(power) + (Risky(power) ? "  (" + L("scripts_risky", "careful") + ")" : ""), box.x + 36f, box.y + 56f + i * 34f, 540f, 30f, 18f, c);
            }
            float by = box.yMax - 62f;
            ModalButton(new Rect(box.x + 24f, by, 190f, 44f), L("scripts_allow_run", "Allow and run"), () => { foreach (string n in _modalNeeds) B.Allow(_modalFile, n, true); string f = _modalFile; _modalNeeds = null; Start(f); }, Style.Good.WithAlpha(0.6f));
            ModalButton(new Rect(box.x + 224f, by, 190f, 44f), L("scripts_run_anyway", "Run without"), () => { string f = _modalFile; _modalNeeds = null; Start(f); }, Style.Button);
            ModalButton(new Rect(box.x + 424f, by, 150f, 44f), L("scripts_cancel", "Cancel"), () => { _modalNeeds = null; }, Style.ButtonOff);
        }

        private void ModalButton(Rect r, string text, Action act, Color fill)
        {
            bool hover = r.Contains(_pointer);
            _p.To("modal", 2);
            _p.Rect(r.x, r.y, r.width, r.height, hover ? Style.Lighter(fill, 0.18f) : fill);
            _p.Text(text, r.x, r.y, r.width, r.height, 17f, Color.white, TextAlignmentOptions.Midline);
            _btns.Add(new Btn { R = r, Do = act });
        }

        // ================================================================ the block editor page

        private static readonly Rect ProblemsArea = new Rect(970f, 88f, 300f, 108f), LimitsArea = new Rect(970f, 368f, 300f, 108f), SideLogArea = new Rect(966f, 502f, 312f, 142f), LibLogArea = new Rect(654f, 518f, 606f, 116f);

        private void DrawBlocksPage()
        {
            Bar(0f, 0f, W, 56f);
            IScriptsBackend be = B;
            bool can = be.CanChange;
            string[] row = _rows.FirstOrDefault(r => r[0] == _file);
            string state = StateOf(row);
            bool live = state == "Running" || state == "Paused";
            Button(new Rect(8f, 8f, 100f, 40f), "< " + L("scripts_title", "Blockscript"), Back);
            _p.To("ui", 1);
            Rect nameR = new Rect(116f, 6f, 250f, 44f);
            Text(_prog.Name, nameR.x + 4f, nameR.y, nameR.width - 8f, 26f, 18f, Color.white);
            // how big the program is, against what a script may be: the coder sees the walls coming
            bool nearFull = _blockCount > Program.MaxBlocks * 0.85f;
            Text((be.IsRemote ? be.Origin + " - " : "") + L("scripts_info", "{0} blocks, {1} KB of code", _blockCount, (_codeBytes / 1024f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), nameR.x + 4f, nameR.y + 24f, nameR.width - 8f, 18f, 12f, nearFull ? Style.Warn : Style.Dim);
            if (can) _btns.Add(new Btn { R = nameR, Do = () => Ask(L("scripts_rename", "Name the script"), _prog.Name, 40, v => { if (!string.IsNullOrWhiteSpace(v)) { _view.Snapshot(); _prog.Name = Program.Clip(v.Trim(), 40); OnChanged(); } }) });
            Button(new Rect(372f, 8f, 64f, 40f), L("scripts_undo", "Undo"), _view.Undo, can && _view.CanUndo);
            Button(new Rect(442f, 8f, 64f, 40f), L("scripts_redo", "Redo"), _view.Redo, can && _view.CanRedo);
            Button(new Rect(512f, 8f, 84f, 40f), L("scripts_copy", "Copy"), _view.DuplicateSelected, can && _view.Selected != null);
            Button(new Rect(602f, 8f, 84f, 40f), L("scripts_remove", "Remove"), _view.DeleteSelected, can && _view.Selected != null, Style.Bad.WithAlpha(0.5f));
            Button(new Rect(720f, 8f, 80f, 40f), L("scripts_code", "Code"), () => { Flush(); _page = Page.Code; _codeTop = 0; _codeShown = null; _tokens.Clear(); _nextList = 0f; });
            // Run says what it will do: start, or start again
            string runWord = state == "Running" ? L("scripts_restart", "Restart") : state == "Paused" ? L("scripts_resume", "Resume") : state == "Failed" ? L("scripts_retry", "Retry") : L("scripts_run", "Run");
            Color runFill = state == "Running" ? new Color(0.25f, 0.5f, 0.75f) : state == "Paused" || state == "Failed" ? Style.Warn : Style.Good;
            Button(new Rect(1000f, 8f, 90f, 40f), runWord, () => { if (!_compiled.Ok) Note(L("scripts_fix_first", "Fix the red blocks first.")); else RunFile(_file); }, can, runFill.WithAlpha(0.7f), runWord.Length > 6 ? 14f : 17f);
            Button(new Rect(1098f, 8f, 80f, 40f), L("scripts_stop", "Stop"), () => { be.Stop(_file); Note(L("scripts_stopped_note", "Stopped.")); }, can && live);
            Button(new Rect(1186f, 8f, 82f, 40f), L("scripts_close", "Close"), () => { Flush(); _host.Close(); });
            _p.To("ui", 1);
            if (_refused.Length > 0)
            {
                Rect chip = new Rect(806f, 8f, 190f, 40f);
                _p.Rect(chip.x, chip.y, chip.width, chip.height, Style.Warn.WithAlpha(0.7f));
                Text(L("scripts_limit_chip", "LIMIT HIT - see below"), chip.x, chip.y, chip.width, chip.height, 14f, Color.white, TextAlignmentOptions.Midline);
                string full = _refused[_refused.Length - 1];
                _btns.Add(new Btn { R = chip, Do = () => Note(L("scripts_refused", "refused: ") + full, 12f) });
            }
            else
            {
                _p.Rect(806f, 8f, 8f, 40f, StateColor(state), false);
                Text(StateWord(state), 820f, 8f, 176f, 40f, 16f, StateColor(state), TextAlignmentOptions.MidlineLeft);
            }

            // zoom, fit and tidy, over the top right of the workspace
            _p.To("ui", 1);
            _p.Rect(724f, 60f, 232f, 30f, new Color(0f, 0f, 0f, 0.4f));
            Button(new Rect(728f, 62f, 28f, 26f), "-", _view.ZoomOut, _view.Zoom > 0.51f, null, 18f);
            _p.To("ui", 3);
            Text(Mathf.RoundToInt(_view.Zoom * 100f) + "%", 758f, 62f, 46f, 26f, 14f, Color.white, TextAlignmentOptions.Midline);
            Button(new Rect(806f, 62f, 28f, 26f), "+", _view.ZoomIn, _view.Zoom < 1.49f, null, 18f);
            Button(new Rect(838f, 62f, 44f, 26f), L("scripts_fit", "Fit"), _view.FitAll, true, null, 13f);
            Button(new Rect(886f, 62f, 66f, 26f), L("scripts_tidy", "Tidy up"), _view.Tidy, can, null, 13f);
            DrawEditors(308f, 60f, 410f);

            // the side panel
            float sx = 970f;
            _p.To("ui", 0);
            _p.Rect(960f, 56f, 320f, 592f, Style.Panel, false);
            _p.To("ui", 1);
            var problems = _compiled.Problems.OrderByDescending(pr => pr.Fatal).ToList();
            int fatal = problems.Count(pr => pr.Fatal), warns = problems.Count - fatal;
            Text(fatal == 0 && warns == 0 ? L("scripts_no_problems", "No problems.") : L("scripts_problems", "Problems: {0}", fatal + warns), sx, 60f, 300f, 26f, 16f, fatal > 0 ? Style.Bad : warns > 0 ? Style.Warn : Style.Good);
            _probScroll = Mathf.Clamp(_probScroll, 0, Mathf.Max(0, problems.Count - 3));
            for (int i = 0; i < 3 && _probScroll + i < problems.Count; i++)
            {
                Problem pr = problems[_probScroll + i];
                Rect r = new Rect(sx, ProblemsArea.y + i * 36f, 290f, 34f);
                _p.To("ui", 1);
                _p.Rect(r.x, r.y, r.width, r.height, pr.Fatal ? new Color(0.35f, 0.17f, 0.17f) : new Color(0.33f, 0.27f, 0.14f));
                Text(pr.Message, r.x + 6f, r.y, r.width - 10f, r.height, 12f, Color.white);
                Block b = pr.Block;
                if (b != null) _btns.Add(new Btn { R = r, Do = () => _view.Show(b) });
            }
            if (problems.Count > 3)
            {
                // a thin bar at the right edge of the list: where you are in it
                float f = (float)_probScroll / Mathf.Max(1, problems.Count - 3);
                _p.Rect(sx + 294f, ProblemsArea.y, 4f, ProblemsArea.height, new Color(1f, 1f, 1f, 0.08f), false);
                _p.Rect(sx + 294f, ProblemsArea.y + f * (ProblemsArea.height - 30f), 4f, 30f, new Color(1f, 1f, 1f, 0.4f), false);
            }
            if (row != null) DrawPowers(row, sx, 200f, 300f, 3);
            // the limits that apply to what this script does: the coder has to know them, because a refusal is not an error
            _p.To("ui", 1);
            Text(be.LimitsFromServer ? L("scripts_limits_server", "This server's limits:") : L("scripts_limits", "Limits (set by the world's owner):"), sx, 346f, 300f, 22f, 14f, Style.Dim);
            string[] limits = be.LimitLines(_compiled.Needs);
            _limitScroll = Mathf.Clamp(_limitScroll, 0, Mathf.Max(0, limits.Length - 6));
            for (int i = 0; i < 6 && _limitScroll + i < limits.Length; i++) Text(limits[_limitScroll + i], sx + 6f, LimitsArea.y + i * 18f, 290f, 18f, 12f, Style.Text);
            if (limits.Length > 6)
            {
                float f = (float)_limitScroll / Mathf.Max(1, limits.Length - 6);
                _p.Rect(sx + 294f, LimitsArea.y, 4f, LimitsArea.height, new Color(1f, 1f, 1f, 0.08f), false);
                _p.Rect(sx + 294f, LimitsArea.y + f * (LimitsArea.height - 24f), 4f, 24f, new Color(1f, 1f, 1f, 0.4f), false);
            }
            LogHeader(sx, 478f, 296f, L("scripts_log", "Log"), Style.Dim);
            _p.To("ui", 0);
            _p.Rect(SideLogArea.x, SideLogArea.y, SideLogArea.width, SideLogArea.height, Style.Back);
            DrawLog(sx, 505f, 300f, 136f, 9);
            DrawStatus();

            // palette and workspace
            _view.ReadOnly = !can || (_co != null && !_co.Active);
            _view.Draw();
            if (!can)
            {
                // read only: the palette is dimmed, so nobody tries to pick up a block that will not go anywhere
                _p.To("ui", 5);
                _p.Rect(_view.PaletteRect.x, _view.PaletteRect.y, _view.PaletteRect.width, _view.PaletteRect.height, new Color(0f, 0f, 0f, 0.45f), false);
            }
        }

        /// <summary>Where the scripts on screen are (this game, or a server), with a dot for how the connection is.</summary>
        private void DrawOrigin(IScriptsBackend be, float x, float y, float w, float h)
        {
            _p.To("ui", 1);
            Color dot = !be.IsRemote ? Style.Good : be.Connected ? Style.Good : be.Note.Contains("No answer") ? Style.Bad : Style.Warn;
            _p.Rect(x, y + h / 2f - 5f, 10f, 10f, dot);
            Text(be.Origin, x + 18f, y, w - 18f, h, 15f, be.IsRemote ? Style.Text : Style.Dim);
        }

        // ================================================================ the code page

        private struct Tok { internal string Text; internal Color Color; }

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "const", "let", "var", "function", "return", "if", "else", "for", "while", "of", "in", "new", "async", "await", "true", "false", "null",
            "undefined", "break", "continue", "this", "try", "catch", "throw", "typeof", "switch", "case", "default", "do",
        };

        private static readonly Color CodeText = new Color(0.88f, 0.9f, 0.95f), CodeKeyword = new Color(0.55f, 0.7f, 1f), CodeString = new Color(0.95f, 0.75f, 0.45f),
            CodeNumber = new Color(0.6f, 0.9f, 0.9f), CodeHelper = new Color(0.5f, 0.8f, 0.65f), CodePunct = new Color(0.72f, 0.75f, 0.84f);

        /// <summary>
        /// One line of code cut into coloured pieces (comments dim, strings and numbers, keywords, the blocks' helper names). Each piece keeps
        /// the spaces before it, so the pieces can be laid end to end. Only the lines on screen are ever cut up.
        /// </summary>
        private static List<Tok> Tokenize(string line)
        {
            var toks = new List<Tok>();
            if (line.Length > 240) line = line.Substring(0, 240);
            int i = 0, n = line.Length;
            while (i < n && toks.Count < 70)
            {
                int start = i;
                while (i < n && (line[i] == ' ' || line[i] == '\t')) i++;
                if (i >= n) break;
                char c = line[i];
                Color color = CodePunct;
                if (c == '/' && i + 1 < n && line[i + 1] == '/') { i = n; color = Style.Dim; }
                else if (c == '"' || c == '\'' || c == '`')
                {
                    char q = c; i++;
                    while (i < n && line[i] != q) { if (line[i] == '\\') i++; i++; }
                    i = Mathf.Min(n, i + 1);
                    color = CodeString;
                }
                else if (char.IsDigit(c)) { while (i < n && (char.IsDigit(line[i]) || line[i] == '.')) i++; color = CodeNumber; }
                else if (char.IsLetter(c) || c == '_' || c == '$')
                {
                    int w = i;
                    while (i < n && (char.IsLetterOrDigit(line[i]) || line[i] == '_' || line[i] == '$')) i++;
                    string word = line.Substring(w, i - w);
                    color = Keywords.Contains(word) ? CodeKeyword : word.StartsWith("_b") ? CodeHelper : CodeText;
                }
                else i++;
                toks.Add(new Tok { Text = line.Substring(start, i - start).Replace("\t", "  "), Color = color });
            }
            return toks;
        }

        private void FindInCode()
        {
            Ask(L("scripts_find_title", "Find in the code"), _codeQuery, 40, v =>
            {
                _codeQuery = (v ?? "").Trim();
                NextInCode(0);
            });
        }

        private void NextInCode(int from)
        {
            if (_codeQuery.Length == 0 || _codeSplit.Length == 0) return;
            for (int k = 0; k < _codeSplit.Length; k++)
            {
                int i = (from + k) % _codeSplit.Length;
                if (_codeSplit[i].IndexOf(_codeQuery, StringComparison.OrdinalIgnoreCase) >= 0) { _codeTop = Mathf.Max(0, i - 3); return; }
            }
            Note(L("scripts_find_none", "Not found."));
        }

        private void DrawCodePage()
        {
            Bar(0f, 0f, W, 56f);
            Button(new Rect(8f, 8f, 100f, 40f), "< " + L("scripts_title", "Blockscript"), () => { _page = _blocksScript ? Page.Blocks : Page.Library; _nextList = 0f; });
            string[] row = _rows.FirstOrDefault(r => r[0] == _file);
            _p.To("ui", 1);
            Text(row != null ? row[1] : _file, 120f, 0f, 300f, 56f, 22f, Color.white);
            Text(L("scripts_code_size", "{0} lines, {1} KB", _codeLines, ((_codeShown ?? "").Length / 1024f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), 420f, 0f, 150f, 56f, 13f, Style.Dim);
            if (_blocksScript) Text(L("scripts_code_made", "Made from the blocks. Change the blocks, not this."), 570f, 0f, 300f, 56f, 13f, Style.Dim);
            Button(new Rect(880f, 8f, 70f, 40f), L("scripts_find", "Find"), FindInCode);
            Button(new Rect(956f, 8f, 84f, 40f), L("scripts_find_next", "Next"), () => NextInCode(_codeTop + 4), _codeQuery.Length > 0);
            Button(new Rect(1050f, 8f, 120f, 40f), L("scripts_copy_code", "Copy code"), () => { GUIUtility.systemCopyBuffer = _codeShown ?? ""; Note(L("scripts_copied", "Copied to the clipboard.")); });
            Button(new Rect(1178f, 8f, 90f, 40f), L("scripts_close", "Close"), () => { Flush(); _host.Close(); });
            _p.To("ui", 0);
            _p.Rect(8f, 64f, W - 16f, 580f, Style.Panel);
            const float LineH = 20f;
            int visible = 28;
            _codeTop = Mathf.Clamp(_codeTop, 0, Mathf.Max(0, _codeLines - visible));
            if (_tokens.Count > 400) _tokens.Clear();
            for (int i = 0; i < visible && _codeTop + i < _codeSplit.Length; i++)
            {
                int index = _codeTop + i;
                string line = _codeSplit[index];
                float ly = 70f + i * LineH;
                if (_codeQuery.Length > 0 && line.IndexOf(_codeQuery, StringComparison.OrdinalIgnoreCase) >= 0) { _p.To("ui", 1); _p.Rect(56f, ly, W - 90f, LineH, new Color(1f, 0.85f, 0.3f, 0.14f), false); }
                _p.To("ui", 2);
                Text((index + 1).ToString().PadLeft(4), 10f, ly, 42f, LineH, 13f, Style.Dim, TextAlignmentOptions.MidlineRight);
                if (!_tokens.TryGetValue(index, out List<Tok> toks)) _tokens[index] = toks = Tokenize(line);
                float x = 62f;
                foreach (Tok t in toks)
                {
                    float tw = _p.Width(t.Text, 14f);
                    if (x + tw > W - 30f) { Text(t.Text, x, ly, W - 30f - x, LineH, 14f, t.Color); break; }
                    Text(t.Text, x, ly, tw + 4f, LineH, 14f, t.Color);
                    x += tw;
                }
            }
            if (_codeLines > visible)
            {
                float f = (float)_codeTop / Mathf.Max(1, _codeLines - visible), th = Mathf.Max(30f, 570f * visible / _codeLines);
                _p.To("ui", 3);
                _p.Rect(W - 16f, 68f, 5f, 572f, new Color(1f, 1f, 1f, 0.06f), false);
                _p.Rect(W - 16f, 68f + f * (572f - th), 5f, th, new Color(1f, 1f, 1f, 0.35f), false);
            }
            DrawStatus();
        }

        // ================================================================ the pointer

        internal void Pointer(Vector2 p, bool down, bool up)
        {
            _pointer = p;
            if (_modalNeeds != null)
            {
                if (up && _downBtn != null) { Btn b = HitBtn(p); Btn d = _downBtn; _downBtn = null; if (b != null && b.R == d.R) b.Do(); }
                else if (down) _downBtn = HitBtn(p);
                return;
            }
            bool view = _page == Page.Blocks;
            if (view && _view.MenuOpen) { _view.Pointer(p, down, up); return; }   // (an open menu or panel takes the click first, even from the toolbar)
            if (down)
            {
                Btn b = HitBtn(p);
                if (b != null) { _downBtn = b; return; }
            }
            if (_downBtn != null)
            {
                if (up) { Btn b = HitBtn(p); Btn d = _downBtn; _downBtn = null; if (b != null && b.R == d.R) b.Do(); }
                return;
            }
            if (view) _view.Pointer(p, down, up);
        }

        private Btn HitBtn(Vector2 p)
        {
            for (int i = _btns.Count - 1; i >= 0; i--) if (_btns[i].R.Contains(p)) return _btns[i];
            return null;
        }

        internal void Wheel(Vector2 p, float delta)
        {
            if (Mathf.Abs(delta) < 0.001f || _modalNeeds != null) return;
            int step = delta > 0f ? 1 : -1;
            if (_page == Page.Blocks)
            {
                if (!_view.MenuOpen && ProblemsArea.Contains(p)) _probScroll -= step;
                else if (!_view.MenuOpen && LimitsArea.Contains(p)) _limitScroll -= step;
                else if (!_view.MenuOpen && SideLogArea.Contains(p)) _logScroll += step;
                else _view.Wheel(p, delta);
            }
            else if (_page == Page.Library) { if (LibLogArea.Contains(p)) _logScroll += step; else _libTop -= step; }
            else if (_page == Page.Log) LogWheel(p, step);
            else _codeTop -= step * 3;
        }

        /// <summary>A key the window sees (the Terminal has no keys): "undo", "redo", "copy", "paste", "delete" or "search".</summary>
        internal void Shortcut(string what)
        {
            if (_modalNeeds != null) return;
            if (_page == Page.Blocks) _view.Shortcut(what);
            else if (_page == Page.Code && what == "search") FindInCode();
        }

        internal bool TypingBlocked => _modalNeeds != null;

        /// <summary>For Claude Tools (`scripts panel page ...`): jump to a page of a script, to take a picture of it.</summary>
        internal string Go(string page, string file, string extra)
        {
            _nextList = 0f;
            Refresh();
            if (!string.IsNullOrEmpty(file))
            {
                string match = _rows.Select(r => r[0]).FirstOrDefault(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase) || string.Equals(Files.BaseOf(f), file, StringComparison.OrdinalIgnoreCase));
                if (match == null) return "no script called " + file;
                file = match;
            }
            switch ((page ?? "").ToLowerInvariant())
            {
                case "library": _page = Page.Library; if (!string.IsNullOrEmpty(file)) Pick(file); if (extra == "run" && !string.IsNullOrEmpty(file)) { _nextList = 0f; Refresh(); RunFile(file); } break;   // ("run": for a picture of the permission box)
                case "blocks":
                    if (string.IsNullOrEmpty(file) || !B.HasBlocks(file)) return "that script has no blocks";
                    OpenBlocks(file, true);
                    if (!string.IsNullOrEmpty(extra)) _view.Category = extra;
                    break;
                case "code": if (!string.IsNullOrEmpty(file)) Pick(file); _page = Page.Code; _codeTop = 0; _codeShown = null; _tokens.Clear(); break;
                case "log": if (!string.IsNullOrEmpty(file)) Pick(file); _page = Page.Library; OpenLogPage(); break;
                default: return "pages: library, blocks, code, log";
            }
            return "";
        }

        internal void Escape()
        {
            if (_modalNeeds != null) { _modalNeeds = null; return; }
            if (_view.MenuOpen) { _view.CloseMenu(); return; }
            if (_page == Page.Log) { LeaveLogPage(); return; }
            if (_page != Page.Library) Back(); else _host.Close();
        }

        // ================================================================ words for the block editor

        private static string English(string key)
        {
            switch (key)
            {
                case "scripts_co_held_by": return "{0} is moving that.";
                case "scripts_co_moving": return "{0} is moving these";
                case "scripts_cat_text": return "Text";
                case "scripts_cat_lists": return "Lists";
                case "scripts_cat_players": return "Players";
                case "scripts_cat_world": return "World";
                case "scripts_cat_effects": return "Effects";
                case "scripts_cat_functions": return "My blocks";
                case "scripts_search": return "Search blocks...";
                case "scripts_search_title": return "Search the blocks";
                case "scripts_search_none": return "No block matches that.";
                case "scripts_manage_names": return "Names...";
                case "scripts_names_title": return "Variables, lists and my blocks";
                case "scripts_names_hint": return "Renaming changes every block that uses the name.";
                case "scripts_names_none": return "Nothing here yet. Make a variable first.";
                case "scripts_kind_var": return "variable";
                case "scripts_kind_list": return "list";
                case "scripts_kind_proc": return "my block";
                case "scripts_uses": return "used {0}x";
                case "scripts_rename_short": return "Rename";
                case "scripts_delete_short": return "Delete";
                case "scripts_sure_short": return "Sure?";
                case "scripts_rename_to": return "New name";
                case "scripts_name_taken": return "That name is already used.";
                case "scripts_delete_block_hint": return "Remove the block that makes it, and it is gone.";
                case "scripts_delete_again": return "Press again to delete it for good.";
                case "scripts_names_more": return "{0} - {1} of {2}";
                case "scripts_folded": return "{0} blocks folded";
                case "scripts_note_empty": return "(empty note - click to write)";
                case "scripts_copied_block": return "Copied. Ctrl+V pastes it.";
                case "scripts_tidied": return "Tidied up.";
                case "scripts_close": return "Close";
                case "scripts_cat_events": return "Events";
                case "scripts_cat_control": return "Control";
                case "scripts_cat_values": return "Values";
                case "scripts_cat_actions": return "Actions";
                case "scripts_cat_memory": return "Memory";
                case "scripts_make_variable": return "Make a variable";
                case "scripts_new_variable": return "new variable...";
                case "scripts_name_variable": return "Name the variable";
                case "scripts_too_many_variables": return "That is enough variables.";
                case "scripts_type_number": return "Type a number";
                case "scripts_type_words": return "Type the words";
                case "scripts_not_a_number": return "That is not a number.";
                case "scripts_num_at_most": return "At most {0} here: it runs as {0}.";
                case "scripts_num_at_least": return "At least {0} here: it runs as {0}.";
                case "scripts_event_gives": return "This event gives you:";
                case "scripts_event_gives_none": return "Nothing extra: any block works here.";
                case "scripts_event_gives_hint": return "Drag one into a slot under this 'when' block.";
                case "scripts_event_gives_see": return "What it gives you: top of the block list";
                case "scripts_deleted_blocks": return "Removed.";
                case "scripts_empty_hint": return "Drag a 'when' block here to start. Everything under it runs when that happens.";
                default: return key;
            }
        }
    }

    internal static class ColorExt
    {
        internal static Color WithAlpha(this Color c, float a) => Color.Lerp(Style.Bar, c, a);
    }
}
