using System;
using System.IO;
using System.Linq;
using Scripts;

namespace Scripts.Log.Tests
{
    internal static class Tests
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL: " + what);
        }

        private static int Main()
        {
            DateTime now = new DateTime(2026, 10, 12, 20, 0, 0);
            var log = new ScriptLog { Clock = () => now };

            // lines have a time, a level and a handler; the same line in a row folds into a count
            log.Add(LogLevel.Info, "started");
            log.Add(LogLevel.Limit, "too many spawns a minute", evt: "kill");
            now = now.AddSeconds(1);
            log.Add(LogLevel.Limit, "too many spawns a minute", evt: "kill");
            now = now.AddSeconds(1);
            log.Add(LogLevel.Limit, "too many spawns a minute", evt: "kill");
            Check(log.All.Count == 2, "a repeat folds into one entry: " + log.All.Count);
            Check(log.All[1].Count == 3 && ScriptLog.Line_(log.All[1]).EndsWith("x3"), "the fold shows its count: " + ScriptLog.Line_(log.All[1]));
            Check(log.Count(LogLevel.Limit) == 3, "the level counter counts every one, not every entry");

            // a repeat after a long pause is a new entry, and a different line is never folded
            now = now.AddSeconds(30);
            log.Add(LogLevel.Limit, "too many spawns a minute", evt: "kill");
            log.Add(LogLevel.Limit, "20 creatures alive", evt: "kill");
            Check(log.All.Count == 4, "after a pause it is new, and a different text is new: " + log.All.Count);

            // errors carry their place
            LogEntry err = log.Add(LogLevel.Error, "x is not defined", "at on kill (line 41)\nat <script>", "kill", 41, 7);
            Check(err.Line == 41 && err.BlockId == 7 && ScriptLog.Line_(err).Contains("(line 41)") && ScriptLog.Line_(err).Contains("[kill]"), "an error knows its line, block and handler: " + ScriptLog.Line_(err));
            Check(log.Errors == 1, "errors are counted");

            // filters
            Check(log.Query(LogLevel.Error).Count == 1 && log.Query(LogLevel.Limit).Count == 4, "at or above a level");
            Check(log.Query(LogLevel.Limit, onlyThisLevel: true).Count == 3, "one level only");
            Check(log.Query(contains: "alive").Count == 1 && log.Query(contains: "KILL").Count == 4, "search, any case, in the text and the handler");
            Check(log.Query(max: 2).Count == 2 && log.Query(max: 2).Last() == log.All.Last(), "the newest come last when cut");

            // text form
            string text = ScriptLog.ToText(log.All);
            Check(text.Contains("ERROR") && text.Contains("      at on kill (line 41)"), "the text form has the detail under the line");

            // size and cleaning
            for (int i = 0; i < 700; i++) { now = now.AddSeconds(10); log.Add(LogLevel.Info, "line " + i); }
            Check(log.All.Count == ScriptLog.Capacity, "the log keeps the last " + ScriptLog.Capacity);
            LogEntry odd = log.Add(LogLevel.Info, "a\nb\r\nc\td");
            Check(!odd.Text.Contains('\n') && !odd.Text.Contains('\t'), "a line is one line");
            Check(log.Add(LogLevel.Info, new string('x', 5000)).Text.Length <= 400, "a long line is cut");

            // what the script wrote itself is marked, and never folds into a host line
            log.Clear();
            log.Add(LogLevel.Info, "hello", user: true);
            log.Add(LogLevel.Info, "hello");
            Check(log.All.Count == 2 && log.Query(onlyUser: true).Count == 1, "your own lines are marked and kept apart: " + log.All.Count);

            // clear
            log.Clear();
            Check(log.All.Count == 0 && log.Errors == 0, "clear empties it and the counts");

            // the file: appended, rotated at the size cap
            string dir = Path.Combine(Path.GetTempPath(), "scripts-log-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var file = new LogFile(dir, "My Script!");
                var sinkLog = new ScriptLog { Clock = () => now };
                sinkLog.Sink = file.Append;
                sinkLog.Add(LogLevel.Info, "hello");
                sinkLog.Add(LogLevel.Error, "boom", "stack line");
                string[] lines = File.ReadAllLines(file.Path_);
                Check(Path.GetFileName(file.Path_) == "My-Script-.log", "the file name is safe: " + Path.GetFileName(file.Path_));
                Check(lines.Length == 3 && lines[0].Contains("hello") && lines[1].Contains("boom") && lines[2].Contains("stack line"), "the file has the lines and their detail: " + string.Join("|", lines));
                var big = new ScriptLog { Clock = () => now };
                big.Sink = file.Append;
                for (int i = 0; i < 4000; i++) { now = now.AddSeconds(10); big.Add(LogLevel.Info, "padding line number " + i + " " + new string('p', 80)); }
                Check(new FileInfo(file.Path_).Length <= LogFile.MaxBytes + 4096, "the file stays under its cap: " + new FileInfo(file.Path_).Length);
                Check(File.Exists(Path.Combine(dir, "My-Script-.old.log")), "the older half is kept once");
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
