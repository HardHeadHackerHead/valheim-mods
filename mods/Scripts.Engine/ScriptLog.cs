using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Scripts
{
    internal enum LogLevel { Debug = 0, Info = 1, Did = 2, Limit = 3, Warn = 4, Error = 5 }

    /// <summary>One line of a script's log, with everything a script's author needs to find the cause.</summary>
    internal sealed class LogEntry
    {
        internal long Seq;
        internal DateTime Time;
        internal LogLevel Level;
        internal string Text = "";
        internal string Detail = "";      // a stack, the arguments of a call: shown when the line is opened
        internal string Event = "";       // the handler it came from ("kill", "timer", "start")
        internal int Line;                // a line of the script's code, 0 if none
        internal int BlockId;             // the block that made that line (block scripts), 0 if none
        internal int Count = 1;           // the same line in a row is one entry with a count
        internal bool User;               // written by the script itself with log(), print() or assert(), not by the host
    }

    /// <summary>
    /// A script's log: structured entries (time, level, the handler, the line and block when known), the same message in a row folded into
    /// "x12", a fixed size, filters for the viewer, and a plain-text form for the file on disk and the clipboard. No Unity in it, so it is
    /// tested on its own (tests/Scripts.Log.Tests). The host adds to it; the editor and the Claude Tools command read it.
    /// </summary>
    internal sealed class ScriptLog
    {
        internal const int Capacity = 500;
        private const double FoldSeconds = 5.0;

        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private long _seq;
        private readonly int[] _counts = new int[6];

        internal Func<DateTime> Clock = () => DateTime.Now;
        /// <summary>Called with every new line (not folded repeats), for the file on disk.</summary>
        internal Action<LogEntry> Sink;

        internal IReadOnlyList<LogEntry> All => _entries;
        internal long Seq => _seq;
        internal int Count(LogLevel level) => _counts[(int)level];
        internal int Errors => _counts[(int)LogLevel.Error];

        internal LogEntry Add(LogLevel level, string text, string detail = "", string evt = "", int line = 0, int blockId = 0, bool user = false)
        {
            text = Clip(Clean(text), 400);
            DateTime now = Clock();
            LogEntry last = _entries.Count > 0 ? _entries[_entries.Count - 1] : null;
            if (last != null && last.Level == level && last.User == user && last.Text == text && last.Event == evt && last.Line == line && (now - last.Time).TotalSeconds <= FoldSeconds)
            {
                last.Count++;
                last.Time = now;
                last.Seq = ++_seq;
                _counts[(int)level]++;
                return last;
            }
            var e = new LogEntry { Seq = ++_seq, Time = now, Level = level, Text = text, Detail = Clip(detail ?? "", 4000), Event = evt ?? "", Line = line, BlockId = blockId, User = user };
            if (_entries.Count >= Capacity) _entries.RemoveAt(0);
            _entries.Add(e);
            _counts[(int)level]++;
            try { Sink?.Invoke(e); } catch (Exception) { }
            return e;
        }

        internal void Clear()
        {
            _entries.Clear();
            Array.Clear(_counts, 0, _counts.Length);
        }

        /// <summary>The lines a viewer shows: at or above a level (or one level only), containing some text, newest last, at most `max`.</summary>
        internal List<LogEntry> Query(LogLevel min = LogLevel.Debug, string contains = "", int max = 200, bool onlyThisLevel = false, bool onlyUser = false)
        {
            IEnumerable<LogEntry> q = _entries.Where(e => onlyThisLevel ? e.Level == min : e.Level >= min);
            if (onlyUser) q = q.Where(e => e.User);
            if (!string.IsNullOrEmpty(contains)) q = q.Where(e => e.Text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0 || e.Event.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);
            List<LogEntry> list = q.ToList();
            return list.Count > max ? list.GetRange(list.Count - max, max) : list;
        }

        // ---- text forms ----

        internal static string Tag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Did: return "DID";
                case LogLevel.Limit: return "LIMIT";
                case LogLevel.Warn: return "WARN";
                case LogLevel.Error: return "ERROR";
                default: return "INFO";
            }
        }

        internal static string Line_(LogEntry e)
        {
            var sb = new StringBuilder();
            sb.Append(e.Time.ToString("HH:mm:ss")).Append(' ').Append(Tag(e.Level).PadRight(5)).Append(' ');
            if (e.Event.Length > 0) sb.Append('[').Append(e.Event).Append("] ");
            sb.Append(e.Text);
            if (e.Line > 0) sb.Append(" (line ").Append(e.Line).Append(')');
            if (e.Count > 1) sb.Append(" x").Append(e.Count);
            return sb.ToString();
        }

        /// <summary>The whole log as text, one entry per line with its detail under it: for the clipboard and the file.</summary>
        internal static string ToText(IEnumerable<LogEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (LogEntry e in entries)
            {
                sb.AppendLine(Line_(e));
                if (e.Detail.Length > 0) foreach (string l in e.Detail.Replace("\r", "").Split('\n')) sb.Append("      ").AppendLine(l);
            }
            return sb.ToString();
        }

        private static string Clean(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
        private static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    /// <summary>
    /// A script's log on disk: BepInEx/config/Blockscript/logs/NAME.log, appended as it happens, cut to the last half when it passes 256 KB
    /// (and the older half kept once as NAME.old.log), so what happened before a crash or a reload can still be read.
    /// </summary>
    internal sealed class LogFile
    {
        internal const long MaxBytes = 256 * 1024;
        private readonly string _path;
        private long _size = -1;

        internal LogFile(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, Safe(name) + ".log");
        }

        internal string Path_ => _path;

        internal void Append(LogEntry e)
        {
            string text = ScriptLog.ToText(new[] { e });
            if (_size < 0) _size = File.Exists(_path) ? new FileInfo(_path).Length : 0;
            if (_size > MaxBytes) Rotate();
            File.AppendAllText(_path, text, new UTF8Encoding(false));
            _size += Encoding.UTF8.GetByteCount(text);
        }

        private void Rotate()
        {
            try
            {
                string old = _path.Substring(0, _path.Length - 4) + ".old.log";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_path, old);
            }
            catch (Exception) { try { File.Delete(_path); } catch (Exception) { } }
            _size = 0;
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in System.IO.Path.GetFileNameWithoutExtension(name ?? "script")) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            return sb.Length == 0 ? "script" : sb.ToString();
        }
    }
}
