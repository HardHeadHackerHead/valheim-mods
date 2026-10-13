using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;
using TMPro;
using UnityEngine;
using Scripts.Mp;
using static Scripts.Editor.Words;

namespace Scripts.Editor
{
    /// <summary>
    /// The log page: the whole log of the script that is open, for whoever is writing it. Filters (everything, only what the script wrote,
    /// errors, warnings and up, limits, what it did, the trace), a search, follow (stay at the newest) or pause, a line opens to show its
    /// detail (the stack, or the arguments), a line that came from a block has a button that goes to the block, and Copy puts what the
    /// filters show on the clipboard as text. The numbers above it are the counts since the script started; the line below is how it runs.
    /// </summary>
    internal sealed partial class EditorScreen
    {
        private enum LogFilter { All, Mine, Errors, Warnings, Limits, Did, Trace }

        private LogFilter _lfilter = LogFilter.All;
        private string _lquery = "";
        private bool _lfollow = true;
        private int _ltop;
        private long _lopen = -1;                 // the line whose detail is open (by sequence number)
        private Page _lfrom = Page.Library;
        private string[][] _lrows = new string[0][];
        private long[] _lcounts = new long[7];
        private string _lstats = "";
        private bool _ltrace;
        private float _lnext;

        private static readonly Rect LogList = new Rect(8f, 100f, 1264f, 540f);

        private void OpenLogPage()
        {
            if (_page == Page.Log) return;
            Flush();
            _lfrom = _page;
            _page = Page.Log;
            _lfollow = true;
            _lopen = -1;
            _lnext = 0f;
            RefreshLogPage();
        }

        private void LeaveLogPage()
        {
            _page = _lfrom == Page.Log ? Page.Library : _lfrom;
            _nextList = 0f;
        }

        private void RefreshLogPage()
        {
            if (Time.unscaledTime < _lnext) return;
            _lnext = Time.unscaledTime + 0.25f;
            if (_file.Length == 0) { _lrows = new string[0][]; return; }
            int min = 0; bool only = false, mine = false;
            switch (_lfilter)
            {
                case LogFilter.Mine: mine = true; break;
                case LogFilter.Errors: min = 5; only = true; break;
                case LogFilter.Warnings: min = 4; break;
                case LogFilter.Limits: min = 3; only = true; break;
                case LogFilter.Did: min = 2; only = true; break;
                case LogFilter.Trace: min = 0; only = true; break;
            }
            IScriptsBackend be = Backend.Current;
            _lrows = be.LogRows(_file, min, only, mine, _lquery, 500);
            _lcounts = be.LogCounts(_file);
            _lstats = be.Stats(_file);
            _ltrace = be.Trace(_file);
        }

        private static Color LevelColor(int level)
        {
            switch (level)
            {
                case 5: return Style.Bad;
                case 4: return Style.Warn;
                case 3: return new Color(1f, 0.62f, 0.3f);
                case 2: return new Color(0.6f, 0.75f, 0.95f);
                case 0: return Style.Dim;
                default: return new Color(0.78f, 0.88f, 0.78f);
            }
        }

        /// <summary>The block that made line `line` of the script's code, if the code on screen is the code this program makes now.</summary>
        private Block BlockAtLine(int line)
        {
            if (!_blocksScript || _compiled == null || !_compiled.Ok || line <= 0 || line > _compiled.LineBlock.Length) return null;
            if (Backend.Current.Source(_file).Replace("\r", "") != _compiled.Js.Replace("\r", "")) return null;   // the script running is an older version
            int id = _compiled.LineBlock[line - 1];
            if (id == 0 || _prog == null) return null;
            foreach (Program.Top top in _prog.Tops)
                foreach (Block root in top.Chain)
                    foreach (Block b in root.All())
                        if (b.Id == id) return b;
            return null;
        }

        private void DrawLogPage()
        {
            RefreshLogPage();
            Bar(0f, 0f, W, 56f);
            string[] row = _rows.FirstOrDefault(r => r[0] == _file);
            Button(new Rect(8f, 8f, 100f, 40f), "< " + L("scripts_back", "Back"), LeaveLogPage);
            _p.To("ui", 1);
            Text(L("scripts_log_of", "Log of {0}", row != null ? row[1] : _file), 120f, 0f, 560f, 56f, 22f, Color.white);
            Button(new Rect(700f, 8f, 120f, 40f), _ltrace ? L("scripts_trace_on", "Trace: on") : L("scripts_trace_off", "Trace: off"), () => { Backend.Current.SetTrace(_file, !_ltrace); _lnext = 0f; }, Backend.Current.CanChange && _file.Length > 0, _ltrace ? Style.Accent : null);
            Button(new Rect(828f, 8f, 100f, 40f), L("scripts_copy", "Copy"), () =>
            {
                Backend.Current.LogText(_file, FilterMin(), FilterOnly(), _lfilter == LogFilter.Mine, _lquery, text =>
                {
                    GUIUtility.systemCopyBuffer = text;
                    Note(L("scripts_log_copied", "The log is on the clipboard."));
                });
            }, _lrows.Length > 0);
            Button(new Rect(936f, 8f, 100f, 40f), L("scripts_clear", "Clear"), () => { Backend.Current.LogClear(_file); _lopen = -1; _lnext = 0f; }, _lrows.Length > 0);
            Button(new Rect(1044f, 8f, 120f, 40f), L("scripts_log_file", "Where is it?"), () => Note(Backend.Current.LogPath(_file), 15f), _file.Length > 0);
            Button(new Rect(1186f, 8f, 82f, 40f), L("scripts_close", "Close"), () => { Flush(); _host.Close(); });

            // filters, with the counts on them
            _p.To("ui", 0);
            _p.Rect(0f, 56f, W, 40f, Style.Panel, false);
            string[] names = { L("scripts_f_all", "All"), L("scripts_f_mine", "Mine"), L("scripts_f_errors", "Errors"), L("scripts_f_warnings", "Warnings"), L("scripts_f_limits", "Limits"), L("scripts_f_did", "Did"), L("scripts_f_trace", "Trace") };
            long[] counts = { -1, -1, _lcounts[5], _lcounts[4] + _lcounts[5], _lcounts[3], _lcounts[2], _lcounts[0] };
            float fx = 8f;
            for (int i = 0; i < names.Length; i++)
            {
                LogFilter f = (LogFilter)i;
                string label = names[i] + (counts[i] > 0 ? " " + counts[i] : "");
                float w = Mathf.Max(70f, _p.Width(label, 15f) + 26f);
                Color fill = _lfilter == f ? Style.Accent : Style.ButtonOff;
                if (i == 2 && counts[i] > 0 && _lfilter != f) fill = new Color(0.4f, 0.2f, 0.2f);
                Button(new Rect(fx, 62f, w, 28f), label, () => { _lfilter = f; _ltop = 0; _lfollow = true; _lopen = -1; _lnext = 0f; }, true, fill, 15f);
                fx += w + 6f;
            }
            Button(new Rect(860f, 62f, 250f, 28f), _lquery.Length > 0 ? L("scripts_search", "Search") + ": " + _lquery : L("scripts_search", "Search") + "...", () => Ask(L("scripts_search", "Search"), _lquery, 40, v => { _lquery = (v ?? "").Trim(); _lnext = 0f; _lfollow = true; }), true, null, 15f);
            if (_lquery.Length > 0) Button(new Rect(1114f, 62f, 30f, 28f), "x", () => { _lquery = ""; _lnext = 0f; }, true, null, 15f);
            Button(new Rect(1152f, 62f, 120f, 28f), _lfollow ? L("scripts_follow_on", "Following") : L("scripts_follow_off", "Paused"), () => { _lfollow = !_lfollow; }, true, _lfollow ? Style.Good.WithAlpha(0.6f) : null, 15f);

            // the lines (a line that is open is taller)
            _p.To("ui", 0);
            _p.Rect(LogList.x, LogList.y, LogList.width, LogList.height, Style.Panel);
            const float LineH = 22f;
            var heights = new float[_lrows.Length];
            for (int i = 0; i < _lrows.Length; i++) heights[i] = LineH + (OpenAt(i) ? DetailHeight(_lrows[i]) : 0f);
            float total = heights.Sum();
            if (_lfollow) _ltop = LastTopFor(heights);
            _ltop = Mathf.Clamp(_ltop, 0, Mathf.Max(0, _lrows.Length - 1));
            float y = LogList.y + 4f;
            int shown = 0;
            for (int i = _ltop; i < _lrows.Length && y < LogList.yMax - LineH; i++)
            {
                string[] r = _lrows[i];
                int level = int.Parse(r[10]);
                Color c = LevelColor(level);
                bool open = OpenAt(i);
                Rect rr = new Rect(LogList.x + 4f, y, LogList.width - 22f, LineH);
                _p.To("ui", 1);
                if (open) _p.Rect(rr.x, y, rr.width, heights[i], new Color(1f, 1f, 1f, 0.07f));
                else if (rr.Contains(_pointer)) _p.Rect(rr.x, y, rr.width, LineH, new Color(1f, 1f, 1f, 0.05f));
                string time = r[1], tag = r[2], evt = r[3], text = r[4];
                int count = int.Parse(r[8]); int line = int.Parse(r[6]);
                Text(time, rr.x + 6f, y, 70f, LineH, 13f, Style.Dim);
                Text(tag, rr.x + 78f, y, 56f, LineH, 12f, c);
                float tx = rr.x + 140f;
                if (evt.Length > 0) { Text(evt, tx, y, 90f, LineH, 12f, Style.Dim); tx += 96f; }
                string tail = (line > 0 ? "   (line " + line + ")" : "") + (count > 1 ? "   x" + count : "");
                Text((r[9] == "1" ? "> " : "") + text + tail, tx, y, rr.xMax - tx - 4f, LineH, 14f, level == 0 ? Style.Dim : Color.white);
                long seq = long.Parse(r[0]);
                string[] rowCopy = r;
                _btns.Add(new Btn { R = new Rect(rr.x, y, rr.width, LineH), Do = () => { _lopen = _lopen == seq ? -1 : seq; _lfollow = false; } });
                if (open)
                {
                    float dy = y + LineH;
                    string[] detail = rowCopy[5].Length > 0 ? rowCopy[5].Replace("\r", "").Split('\n') : new string[0];
                    _p.To("ui", 1);
                    for (int d = 0; d < Mathf.Min(detail.Length, 9); d++) Text(detail[d], rr.x + 140f, dy + d * 18f, rr.width - 150f, 18f, 12f, Style.Dim);
                    float by = dy + Mathf.Min(detail.Length, 9) * 18f + 4f;
                    Block block = BlockAtLine(line);
                    if (block != null) Button(new Rect(rr.x + 140f, by, 150f, 26f), L("scripts_go_block", "Go to the block"), () => { Block b = block; Flush(); _page = Page.Blocks; _view.Show(b); }, true, Style.Category("events"), 14f);
                    Button(new Rect(rr.x + (block != null ? 298f : 140f), by, 120f, 26f), L("scripts_copy_line", "Copy line"), () => { GUIUtility.systemCopyBuffer = rowCopy[1] + " " + rowCopy[2] + " " + (rowCopy[3].Length > 0 ? "[" + rowCopy[3] + "] " : "") + rowCopy[4] + (rowCopy[6] != "0" ? " (line " + rowCopy[6] + ")" : "") + (rowCopy[5].Length > 0 ? "\n" + rowCopy[5] : ""); Note(L("scripts_copied", "Copied to the clipboard.")); }, true, null, 14f);
                }
                y += heights[i];
                shown++;
            }
            if (_lrows.Length == 0) { _p.To("ui", 1); Text(L("scripts_log_empty", "Nothing here yet. Run the script, or change the filter."), LogList.x + 16f, LogList.y + 10f, 900f, 28f, 17f, Style.Dim); }

            // a scrollbar
            if (_lrows.Length > 0 && total > LogList.height)
            {
                float f = _lrows.Length <= 1 ? 0f : (float)_ltop / (_lrows.Length - 1);
                _p.To("ui", 2);
                _p.Rect(LogList.xMax - 10f, LogList.y + 4f, 4f, LogList.height - 8f, new Color(1f, 1f, 1f, 0.08f), false);
                _p.Rect(LogList.xMax - 10f, LogList.y + 4f + f * (LogList.height - 60f), 4f, 48f, new Color(1f, 1f, 1f, 0.45f), false);
            }

            // how it runs
            _p.To("ui", 0);
            _p.Rect(0f, 644f, W, 32f, Style.Bar, false);
            _p.To("ui", 1);
            string state = row != null ? StateWord(row[2]) : "";
            Text(state + "   |   " + _lstats, 14f, 644f, 1000f, 32f, 14f, Style.Text);
            Text(L("scripts_log_count", "{0} of {1} lines", _lrows.Length, _lcounts[6] > 0 ? _lcounts[0] + _lcounts[1] + _lcounts[2] + _lcounts[3] + _lcounts[4] + _lcounts[5] : 0), 1000f, 644f, 266f, 32f, 13f, Style.Dim, TextAlignmentOptions.MidlineRight);
            string msg = Time.unscaledTime < _noteUntil ? _note : "";
            if (msg.Length > 0) { _p.To("ui", 2); _p.Rect(0f, 644f, W, 32f, Style.Bar, false); _p.To("ui", 3); Text(msg, 14f, 644f, W - 28f, 32f, 15f, Style.Text); }
        }

        private int FilterMin() { switch (_lfilter) { case LogFilter.Errors: return 5; case LogFilter.Warnings: return 4; case LogFilter.Limits: return 3; case LogFilter.Did: return 2; default: return 0; } }
        private bool FilterOnly() => _lfilter == LogFilter.Errors || _lfilter == LogFilter.Limits || _lfilter == LogFilter.Did || _lfilter == LogFilter.Trace;

        private bool OpenAt(int i) => _lopen >= 0 && _lrows[i][0] == _lopen.ToString();

        private float DetailHeight(string[] row)
        {
            int lines = row[5].Length > 0 ? Mathf.Min(9, row[5].Replace("\r", "").Split('\n').Length) : 0;
            return lines * 18f + 34f;
        }

        /// <summary>The first line to draw so that the newest is in view (when following).</summary>
        private int LastTopFor(float[] heights)
        {
            float room = LogList.height - 8f, used = 0f;
            int top = heights.Length;
            while (top > 0 && used + heights[top - 1] <= room) { used += heights[top - 1]; top--; }
            return Mathf.Max(0, top);
        }

        private void LogWheel(Vector2 p, int step)
        {
            if (!LogList.Contains(p)) return;
            _lfollow = false;
            _ltop -= step * 3;
            if (_ltop < 0) _ltop = 0;
            // scrolled back to the end: follow again
            var heights = new float[_lrows.Length];
            for (int i = 0; i < _lrows.Length; i++) heights[i] = 22f + (OpenAt(i) ? DetailHeight(_lrows[i]) : 0f);
            if (_ltop >= LastTopFor(heights)) { _ltop = LastTopFor(heights); _lfollow = true; }
        }
    }
}
