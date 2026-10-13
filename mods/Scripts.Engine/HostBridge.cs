using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Scripts
{
    /// <summary>
    /// What the Scripts program on the Terminal (in the Terminal mod) calls: one public class of plain values, found by name through
    /// reflection, so the Terminal needs this mod only for that program and this mod works with no computer at all. Everything here
    /// checks who may do it: changing what runs is for the server's owner (the host, or single player), like the console commands.
    /// </summary>
    public static class HostBridge
    {
        private const int MaxFile = 120 * 1024;

        private static bool MayChange() =>
            ZNet.instance == null || ZNet.instance.IsServer() || (global::Console.instance != null && global::Console.instance.IsCheatsEnabled());

        /// <summary>For the program: whether this game may change what runs.</summary>
        public static bool CanChange() => MayChange();

        /// <summary>One row per script: file, name, state, why, needs (comma separated), approved (comma separated), about, refused (a count).</summary>
        public static string[][] List()
        {
            Host.Scan();
            return Host.Scripts.OrderBy(s => s.Name).Select(s => new[]
            {
                s.File, s.Name, s.State.ToString(), s.Why ?? "",
                string.Join(",", s.Manifest?.Needs.ToArray() ?? new string[0]), string.Join(",", s.Approved.ToArray()), s.Manifest?.About ?? "", s.Refused.Values.Sum().ToString(),
            }).ToArray();
        }

        /// <summary>What the script was refused, as lines: "reason (n times)". Empty when nothing was.</summary>
        public static string[] Refused(string file)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            return s == null ? new string[0] : s.Refused.Select(kv => kv.Key + " (" + kv.Value + "x)").ToArray();
        }

        public static string Source(string file) => Host.Scripts.Find(s => s.File == file)?.Source ?? "";

        public static string[] Log(string file, int count)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            return s == null ? new string[0] : s.Log.Query(max: count).Select(ScriptLog.Line_).ToArray();
        }

        /// <summary>
        /// The log for a viewer. Rows: seq, time, level tag, handler, text, detail, line, block, count, mine ("1"/"0"), level number.
        /// minLevel 0 to 5 (debug, info, did, limit, warn, error); onlyLevel shows that level alone; mine shows only what the script wrote.
        /// </summary>
        public static string[][] LogRows(string file, int minLevel, bool onlyLevel, bool mine, string contains, int max)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null) return new string[0][];
            return s.Log.Query((LogLevel)Math.Max(0, Math.Min(5, minLevel)), contains ?? "", max, onlyLevel, mine).Select(e => new[]
            {
                e.Seq.ToString(), e.Time.ToString("HH:mm:ss"), ScriptLog.Tag(e.Level), e.Event, e.Text, e.Detail, e.Line.ToString(), e.BlockId.ToString(), e.Count.ToString(), e.User ? "1" : "0", ((int)e.Level).ToString(),
            }).ToArray();
        }

        /// <summary>The log as text for the clipboard (with the same filters).</summary>
        public static string LogText(string file, int minLevel, bool onlyLevel, bool mine, string contains)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            return s == null ? "" : ScriptLog.ToText(s.Log.Query((LogLevel)Math.Max(0, Math.Min(5, minLevel)), contains ?? "", ScriptLog.Capacity, onlyLevel, mine));
        }

        public static void LogClear(string file) { Host.Scripts.Find(x => x.File == file)?.Log.Clear(); }

        /// <summary>How many lines of each level so far (debug, info, did, limit, warn, error), and the sequence number of the newest line.</summary>
        public static long[] LogCounts(string file)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null) return new long[7];
            return new long[] { s.Log.Count(LogLevel.Debug), s.Log.Count(LogLevel.Info), s.Log.Count(LogLevel.Did), s.Log.Count(LogLevel.Limit), s.Log.Count(LogLevel.Warn), s.Log.Count(LogLevel.Error), s.Log.Seq };
        }

        /// <summary>One line of numbers about how the script runs: calls, average ms, queued events, dropped events, timers, handlers.</summary>
        public static string Stats(string file)
        {
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null) return "";
            double avg = s.Calls > 0 ? s.TotalMs / s.Calls : 0.0;
            return $"{s.Calls} calls, {avg:0.00} ms each, {s.Queue.Count} queued, {s.Dropped} dropped, {s.Timers.Count} timers, {s.Handlers.Count} handlers";
        }

        public static bool Trace(string file) => Host.Scripts.Find(x => x.File == file)?.Trace ?? false;

        public static void SetTrace(string file, bool on)
        {
            if (!MayChange()) return;
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null) return;
            s.Trace = on;
            s.Say(on ? "tracing on: every event and timer is written to the log" : "tracing off");
        }

        /// <summary>Where the script's log is kept on disk.</summary>
        public static string LogPath(string file) => System.IO.Path.Combine(Host.Folder, "logs", System.IO.Path.GetFileNameWithoutExtension(file) + ".log");

        /// <summary>Starts a script; "" when it started, else the reason in words.</summary>
        public static string Start(string file)
        {
            if (!MayChange()) return "Only the server's owner can run scripts.";
            Script s = Host.Scripts.Find(x => x.File == file);
            return s == null ? "No such script." : Host.Start(s) ?? "";
        }

        public static void Stop(string file)
        {
            if (!MayChange()) return;
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s != null) Host.Stop(s);
        }

        public static void Allow(string file, string power, bool on)
        {
            if (!MayChange()) return;
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null || Array.IndexOf(Manifest.Capabilities, power) < 0) return;
            if (on) s.Approved.Add(power); else s.Approved.Remove(power);
            Approvals.Save();
        }

        /// <summary>Writes text as a script (a new file, or over the one given). "" on success, else the reason.</summary>
        public static string Write(string file, string text)
        {
            if (!MayChange()) return "Only the server's owner can change scripts.";
            if (text == null || text.Length == 0) return "There is nothing to write.";
            if (Encoding.UTF8.GetByteCount(text) > MaxFile) return "That script is too long (120 KB at most).";
            file = Clean(file);
            try
            {
                File.WriteAllText(Path.Combine(Host.Folder, file), text.Replace("\r\n", "\n"));
                Script s = Host.Scripts.Find(x => x.File == file);
                if (s == null) { s = new Script { File = file }; Host.Scripts.Add(s); }
                else if (s.State == RunState.Running || s.State == RunState.Paused) Host.Stop(s);
                Host.Reload(s);
                Mp.LastWriter.Set(file, Mp.CollabGame.LocalName());
                return "";
            }
            catch (Exception e) { return e.Message; }
        }

        /// <summary>A new script from a template: returns its file name.</summary>
        public static string New(string name)
        {
            if (!MayChange()) return "";
            string title = string.IsNullOrWhiteSpace(name) ? "My script" : name.Trim();
            string file = Clean(title.ToLowerInvariant().Replace(' ', '-'));
            int n = 2; string unique = file;
            while (File.Exists(Path.Combine(Host.Folder, unique))) unique = Path.GetFileNameWithoutExtension(file) + "-" + n++ + ".js";
            string text = "// ==Script==\n// name:    " + title + "\n// version: 1.0\n// about:   What it does.\n// needs:   messages\n// api:     1\n// ==/Script==\n\n" +
                          "// Runs when a player says hello in chat.\non(\"chat\", e => {\n  if (e.text.toLowerCase() === \"hello\") e.player.say(\"Hello, \" + e.player.name + \"!\");\n});\n";
            return Write(unique, text) == "" ? unique : "";
        }

        public static bool Delete(string file)
        {
            if (!MayChange()) return false;
            Script s = Host.Scripts.Find(x => x.File == file);
            if (s == null) return false;
            Host.Stop(s);
            try { File.Delete(Path.Combine(Host.Folder, file)); } catch (Exception) { return false; }
            Host.Scripts.Remove(s);
            return true;
        }

        // ---- the editor, for a screen in the world (the Terminal makes one on its window and feeds it the pointer) ----

        private sealed class Adapter : Editor.IEditorHost
        {
            internal Action Closed; internal Func<string> Get; internal Action<string> Set;
            public void Close() { Closed?.Invoke(); }
            public string Picked { get { return Get != null ? Get() ?? "" : ""; } set { Set?.Invoke(value); } }
        }

        /// <summary>Makes the editor on a 1280 x 720 area (top left at the transform's top left). Returns it, to give back to the calls below.</summary>
        public static object EditorCreate(UnityEngine.RectTransform root, TMPro.TMP_FontAsset font, Action close, Func<string> picked, Action<string> pick)
        {
            return new Editor.EditorScreen(root, new Adapter { Closed = close, Get = picked, Set = pick }, font);
        }

        /// <summary>Draws one picture of it.</summary>
        public static void EditorDraw(object editor) { (editor as Editor.EditorScreen)?.Draw(); }

        /// <summary>The pointer: a place in the editor's own space (1280 x 720, y down), and whether the button went down or came up this moment.</summary>
        public static void EditorPointer(object editor, float x, float y, bool down, bool up) { (editor as Editor.EditorScreen)?.Pointer(new UnityEngine.Vector2(x, y), down, up); }

        public static void EditorWheel(object editor, float x, float y, float delta) { (editor as Editor.EditorScreen)?.Wheel(new UnityEngine.Vector2(x, y), delta); }

        /// <summary>Escape: back a step, or close.</summary>
        public static void EditorEscape(object editor) { (editor as Editor.EditorScreen)?.Escape(); }

        public static void EditorDestroy(object editor) { (editor as Editor.EditorScreen)?.Destroy(); }

        /// <summary>Opens the scripts window (the one with no computer).</summary>
        public static void OpenWindow() { Editor.ScriptsWindow.Open(); }

        private static string Clean(string file)
        {
            var sb = new StringBuilder();
            foreach (char c in Path.GetFileNameWithoutExtension(file ?? "script")) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            string name = sb.Length == 0 ? "script" : sb.ToString();
            return (name.Length > 40 ? name.Substring(0, 40) : name) + ".js";
        }
    }
}
