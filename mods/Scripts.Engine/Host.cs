using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Jint;
using Jint.Native;
using UnityEngine;

namespace Scripts
{
    internal enum RunState { Stopped, Running, Paused, Failed }

    /// <summary>One script: its text, manifest, engine and everything it registered. Only the host touches it (on the game's thread).</summary>
    internal sealed class Script
    {
        internal string File = "", Source = "";
        internal Manifest Manifest;
        internal RunState State = RunState.Stopped;
        internal string Why = "";                         // why it is paused or failed, in words
        internal Engine Engine;
        internal Jint.Native.Object.ObjectInstance Json;   // the real JSON object, kept before the script can touch it
        internal Budget Budget;
        internal readonly HashSet<string> Approved = new HashSet<string>();   // the powers the player or admin allowed
        internal readonly Dictionary<string, List<JsValue>> Handlers = new Dictionary<string, List<JsValue>>();
        internal readonly Dictionary<string, JsValue> Commands = new Dictionary<string, JsValue>();
        internal readonly Dictionary<string, List<JsValue>> Signals = new Dictionary<string, List<JsValue>>();   // onSignal(name, fn): messages a script sends itself
        internal readonly List<Timer> Timers = new List<Timer>();
        internal readonly Queue<Pending> Queue = new Queue<Pending>();
        internal readonly ScriptLog Log = new ScriptLog();
        internal bool Trace;                              // record every event and timer this script is given (a switch for whoever is writing it)
        internal readonly Dictionary<string, int> Refused = new Dictionary<string, int>();
        internal readonly List<double> ErrorTimes = new List<double>();
        internal DateTime Stamp, ChangeSeen;              // when the file was last written, and when a change to it was first noticed
        internal bool TopLevel;                           // while the top level runs, actions are an error
        internal int OverBudget, Dropped;
        internal double SpentMs;                          // this frame
        internal double TotalMs; internal long Calls;

        internal string Name => Manifest != null && Manifest.Name.Length > 0 ? Manifest.Name : File;

        internal sealed class Timer { public int Id; public double Next, Every; public bool Repeat; public JsValue Fn; }
        internal sealed class Pending { public string Event; public JsValue Arg; public JsValue Fn; public Func<Engine, JsValue> Make; }

        internal readonly Dictionary<string, float> RefusedLogged = new Dictionary<string, float>();

        private bool _sinkSet;

        /// <summary>A line in the script's log. The old "ERROR: ", "LIMIT: ", "did: " prefixes still choose the level.</summary>
        internal void Say(string line)
        {
            LogLevel level = LogLevel.Info;
            if (line.StartsWith("ERROR: ")) { level = LogLevel.Error; line = line.Substring(7); }
            else if (line.StartsWith("PAUSED: ")) { level = LogLevel.Warn; line = "paused: " + line.Substring(8); }
            else if (line.StartsWith("LIMIT: ")) { level = LogLevel.Limit; line = line.Substring(7); }
            else if (line.StartsWith("refused: ")) { level = LogLevel.Limit; line = line.Substring(9); }
            else if (line.StartsWith("did: ")) { level = LogLevel.Did; line = line.Substring(5); }
            Say(level, line);
        }

        internal void Say(LogLevel level, string text, string detail = "", string evt = "", int line = 0, bool user = false)
        {
            if (!_sinkSet)
            {
                _sinkSet = true;
                try
                {
                    var file = new LogFile(System.IO.Path.Combine(Host.Folder, "logs"), File);
                    Log.Sink = e => { if (e.Level != LogLevel.Debug) file.Append(e); };
                }
                catch (Exception) { }
            }
            Log.Add(level, text, detail, evt, line, 0, user);
        }
    }

    /// <summary>
    /// The host: loads scripts, runs them in turn, hands them events and timers, and keeps each inside its budget. A script is never given
    /// more than its slice of a frame (default 1 ms for all its handlers together, 2 ms for every script together), is paused when it goes
    /// over three frames in a row, and stops on its fifth error in a minute. Nothing here throws into the game's own code.
    /// </summary>
    internal static class Host
    {
        internal static readonly List<Script> Scripts = new List<Script>();
        internal const double ScriptFrameMs = 1.0, AllFrameMs = 2.0, CallMs = 60.0, StartCallMs = 250.0, HitCallMs = 0.2;   // (a handler's longest single call: a script's start runs once, and its first calls into the game are slow, so it gets more)
        internal const int MaxTimers = 120;   // every/after pending at once, per script (a wait in blocks is an after)
        internal const long CallGrowthBytes = 8L * 1024 * 1024, CallStatements = 100000;
        private static int _turn;
        private static int _nextTimerId = 1;

        internal static string Folder
        {
            get
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "Blockscript");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // ---- loading and running ----

        /// <summary>Finds the scripts in the folder (new ones are added, known ones keep their state).</summary>
        internal static void Scan()
        {
            foreach (string path in Directory.GetFiles(Folder, "*.js"))
            {
                string file = Path.GetFileName(path);
                Script existing = Scripts.Find(s => s.File == file);
                if (existing != null) continue;
                var script = new Script { File = file };
                Reload(script);
                Scripts.Add(script);
            }
            Scripts.RemoveAll(s => s.State == RunState.Stopped && !File.Exists(Path.Combine(Folder, s.File)));
        }

        internal static void Reload(Script s)
        {
            try { s.Source = File.ReadAllText(Path.Combine(Folder, s.File)); }
            catch (Exception e) { s.Source = ""; s.Why = "Could not read the file: " + e.Message; }
            s.Manifest = Manifest.Read(s.Source, Path.GetFileNameWithoutExtension(s.File));
            try { s.Stamp = File.GetLastWriteTimeUtc(Path.Combine(Folder, s.File)); } catch (Exception) { }
            s.ChangeSeen = default(DateTime);
        }

        /// <summary>
        /// A script whose file has been saved again is read again, and if it was running it is restarted with the new text. A change has to
        /// stand for a second first, so a file that is still being written never starts half done. Powers are per script file and name: a
        /// new version that asks for more than was allowed gets only what was allowed, so editing never grants anything.
        /// </summary>
        internal static bool AutoReload = true;
        private static float _nextFileCheck;

        internal static void CheckFiles()
        {
            if (UnityEngine.Time.unscaledTime < _nextFileCheck) return;
            _nextFileCheck = UnityEngine.Time.unscaledTime + 1f;
            foreach (Script s in Scripts.ToList())
            {
                string path = Path.Combine(Folder, s.File);
                DateTime written;
                try { if (!File.Exists(path)) continue; written = File.GetLastWriteTimeUtc(path); } catch (Exception) { continue; }
                if (written == s.Stamp) { s.ChangeSeen = default(DateTime); continue; }
                if (s.ChangeSeen == default(DateTime) || written != s.ChangeSeen) { s.ChangeSeen = written; continue; }   // (seen now: it must stay the same for another check)
                bool wasRunning = s.State == RunState.Running || s.State == RunState.Paused;
                if (wasRunning && AutoReload)
                {
                    s.Say("the file changed: starting the new version");
                    Start(s);   // (reads the file again)
                }
                else Reload(s);   // (only the text and the manifest, so the program shows what is on disk)
            }
        }

        internal static Script Find(string text) => Scripts.Find(s => string.Equals(s.File, text, StringComparison.OrdinalIgnoreCase) || string.Equals(s.Name, text, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(s.File), text, StringComparison.OrdinalIgnoreCase));

        /// <summary>Starts a script: reads it once, runs its top level (registering handlers) and queues "start". Returns null, or why it did not.</summary>
        internal static string Start(Script s)
        {
            Stop(s, quiet: true);
            Reload(s);
            if (s.Manifest.Problems.Count > 0) { s.State = RunState.Failed; s.Why = s.Manifest.Problems[0]; return s.Why; }
            if (s.Manifest.Api > Api.Version) { s.State = RunState.Failed; s.Why = "This script is for a newer version of the scripting (api " + s.Manifest.Api + ")."; return s.Why; }
            s.Handlers.Clear(); s.Commands.Clear(); s.Signals.Clear(); s.Timers.Clear(); s.Queue.Clear(); s.Refused.Clear(); s.ErrorTimes.Clear();
            s.Budget = new Budget();
            try
            {
                s.Engine = Sandbox.Create(s.Budget);
                s.Json = s.Engine.Global.Get("JSON").AsObject();
                Api.Bind(s);
                s.TopLevel = true;
                Arm(s, 100.0);
                s.Engine.Execute(s.Source, s.File);
            }
            catch (Exception e)
            {
                Sandbox.Describe(e, out int failLine, out string failDetail);
                Fail(s, Sandbox.Plain(e), failDetail, failLine);
                return s.Why;
            }
            finally { s.TopLevel = false; s.Budget?.Disarm(); }
            s.State = RunState.Running;
            s.Why = "";
            s.Say("started");
            Enqueue(s, "start", null);
            return null;
        }

        internal static void Stop(Script s, bool quiet = false)
        {
            if (s.State == RunState.Running || s.State == RunState.Paused)
            {
                if (s.Handlers.TryGetValue("stop", out List<JsValue> list) && s.Engine != null)
                    foreach (JsValue fn in list.ToList()) Call(s, fn, "stop", null, 20.0);
                if (!quiet) s.Say("stopped");
            }
            s.State = RunState.Stopped;
            s.Handlers.Clear(); s.Commands.Clear(); s.Signals.Clear(); s.Timers.Clear(); s.Queue.Clear();
            s.Engine = null;
            Api.Forget(s);
        }

        internal static void StopAll(string why) {
            WorldExtras.Reset(); foreach (Script s in Scripts) { if (s.State == RunState.Running || s.State == RunState.Paused) { Stop(s); s.Why = why; } } }

        internal static void Fail(Script s, string why, string detail = "", int line = 0)
        {
            s.State = RunState.Failed;
            s.Why = why;
            s.Say(LogLevel.Error, why, detail, "start", line);
            s.Engine = null;
            s.Handlers.Clear(); s.Signals.Clear(); s.Timers.Clear(); s.Queue.Clear();
            Core.Log.LogInfo($"Script {s.Name}: {why}");
        }

        // ---- events and timers ----

        /// <summary>Queue an event for every running script that listens to it. The event's value is made inside each script's own engine.</summary>
        internal static void Fire(string name, Func<Engine, JsValue> make)
        {
            foreach (Script s in Scripts)
                if (s.State == RunState.Running && s.Handlers.ContainsKey(name)) Enqueue(s, name, make);
        }

        internal static void Enqueue(Script s, string name, Func<Engine, JsValue> make)
        {
            if (s.Queue.Count >= 100) { s.Queue.Dequeue(); s.Dropped++; }
            s.Queue.Enqueue(new Script.Pending { Event = name, Make = make });
        }

        /// <summary>Queues a call of a script's own function with one value (a signal).</summary>
        internal static void EnqueueSignal(Script s, JsValue fn, JsValue data)
        {
            if (s.Queue.Count >= 100) { s.Queue.Dequeue(); s.Dropped++; }
            s.Queue.Enqueue(new Script.Pending { Event = "signal", Fn = fn, Arg = data });
        }

        internal static int AddTimer(Script s, JsValue fn, double seconds, bool repeat)
        {
            if (s.Timers.Count >= MaxTimers) return -1;
            var t = new Script.Timer { Id = _nextTimerId++, Fn = fn, Every = Math.Max(repeat ? 1.0 : 0.0, seconds), Repeat = repeat, Next = Time.timeAsDouble + Math.Max(repeat ? 1.0 : 0.0, seconds) };
            s.Timers.Add(t);
            return t.Id;
        }

        internal static void Cancel(Script s, int id) => s.Timers.RemoveAll(t => t.Id == id);

        /// <summary>Every frame: due timers become events, then queued events run, scripts taking turns, inside the frame's budget.</summary>
        internal static void Update()
        {
            if (Scripts.Count == 0) return;
            double now = Time.timeAsDouble;
            var frame = Stopwatch.StartNew();
            foreach (Script s in Scripts) s.SpentMs = 0;
            int count = Scripts.Count;
            for (int k = 0; k < count && frame.Elapsed.TotalMilliseconds < AllFrameMs; k++)
            {
                Script s = Scripts[(_turn + k) % count];
                if (s.State != RunState.Running) continue;
                for (int i = s.Timers.Count - 1; i >= 0; i--)
                {
                    Script.Timer t = s.Timers[i];
                    if (now < t.Next) continue;
                    if (t.Repeat) t.Next = now + t.Every; else s.Timers.RemoveAt(i);
                    if (s.Queue.Count >= 100) { s.Queue.Dequeue(); s.Dropped++; }
                    s.Queue.Enqueue(new Script.Pending { Event = "timer", Fn = t.Fn });
                }
                var slice = Stopwatch.StartNew();
                while (s.Queue.Count > 0 && s.State == RunState.Running && slice.Elapsed.TotalMilliseconds < ScriptFrameMs && frame.Elapsed.TotalMilliseconds < AllFrameMs)
                {
                    Script.Pending p = s.Queue.Dequeue();
                    Run(s, p);
                }
                if (s.Queue.Count > 20 && slice.Elapsed.TotalMilliseconds >= ScriptFrameMs)
                {
                    if (++s.OverBudget >= 3) { s.State = RunState.Paused; s.Why = "It used its whole share of the time three frames in a row."; s.Say("PAUSED: " + s.Why); }
                }
                else s.OverBudget = 0;
            }
            _turn = count == 0 ? 0 : (_turn + 1) % count;
        }

        private static void Run(Script s, Script.Pending p)
        {
            try
            {
                if (p.Event == "timer") { Call(s, p.Fn, "timer", null, CallMs); return; }
                if (p.Event == "signal") { Call(s, p.Fn, "signal", p.Arg ?? JsValue.Undefined, CallMs); return; }
                if (!s.Handlers.TryGetValue(p.Event, out List<JsValue> list)) return;
                JsValue arg = p.Make != null ? p.Make(s.Engine) : JsValue.Undefined;
                foreach (JsValue fn in list.ToList()) { if (s.State != RunState.Running) break; Call(s, fn, p.Event, arg, p.Event == "hit" ? HitCallMs : p.Event == "start" ? StartCallMs : CallMs); }
            }
            catch (Exception e) { Error(s, e, p.Event); }
        }

        /// <summary>One call into a script's function, inside its budget. A script that breaks the rules is stopped for that call only.</summary>
        internal static void Call(Script s, JsValue fn, string what, JsValue arg, double ms)
        {
            if (s.Engine == null || fn == null || !fn.IsObject()) return;
            s.Budget.Arm(ms, CallGrowthBytes, CallStatements);
            var timer = Stopwatch.StartNew();
            if (s.Trace) s.Say(LogLevel.Debug, "run " + what, Sandbox.Summary(arg), what);
            try { if (arg == null) s.Engine.Invoke(fn); else s.Engine.Invoke(fn, JsValue.Undefined, new[] { arg }); }
            catch (Exception e) { Error(s, e, what); }
            finally
            {
                s.Budget.Disarm();
                double spent = timer.Elapsed.TotalMilliseconds;
                s.SpentMs += spent; s.TotalMs += spent; s.Calls++;
                if (spent > 8.0) s.Say(LogLevel.Warn, "slow " + what + ": " + spent.ToString("0.0") + " ms (a script gets about 1 ms a frame)", "", what);
            }
        }

        private static void Error(Script s, Exception e, string evt = "")
        {
            string plain = Sandbox.Plain(e);
            Sandbox.Describe(e, out int line, out string detail);
            if (!(e is BudgetException) && !(e is ScriptError) && !(e is Jint.Runtime.JavaScriptException) && !(e is Jint.Runtime.RecursionDepthOverflowException) && !(e is Jint.Runtime.StatementsCountOverflowException)) Core.Log.LogWarning("Script " + s.Name + ": " + e);   // (a fault in the host, not in the script: the stack helps)
            string shownText = line > 0 && plain.EndsWith(" (line " + line + ")") ? plain.Substring(0, plain.Length - (" (line " + line + ")").Length) : plain;   // (the line is its own column in the log)
            s.Say(LogLevel.Error, shownText, detail, evt, line);
            double now = Time.timeAsDouble;
            s.ErrorTimes.RemoveAll(t => now - t > 60.0);
            s.ErrorTimes.Add(now);
            if (s.ErrorTimes.Count >= 5 && s.State == RunState.Running)
            {
                s.State = RunState.Paused;
                s.Why = "Five errors in a minute: " + plain;
                s.Say(LogLevel.Warn, "paused: " + s.Why);
            }
        }

        internal static void Arm(Script s, double ms) => s.Budget.Arm(ms, CallGrowthBytes, CallStatements * 2);

        private static readonly Dictionary<string, float> Told = new Dictionary<string, float>();

        /// <summary>
        /// A refusal is not an error, so nothing else would say it happened: the player who is playing (the world's owner, who runs the
        /// scripts) sees it at the top left of the screen, naming the script and the reason, the first time and then at most every 30 seconds.
        /// </summary>
        private static void Notify(Script s, string reason)
        {
            try
            {
                if (Player.m_localPlayer == null || MessageHud.instance == null) return;
                string key = s.File + "|" + reason;
                float now = UnityEngine.Time.unscaledTime;
                if (Told.TryGetValue(key, out float last) && now - last < 30f) return;
                Told[key] = now;
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, s.Name + ": " + reason);
            }
            catch (Exception) { }
        }

        internal static void Refuse(Script s, string reason)
        {
            s.Refused.TryGetValue(reason, out int n);
            s.Refused[reason] = n + 1;
            // the log says it the first time, then once every ten seconds with the running count, so it shows a limit being hit
            // for as long as it goes on without one line for every blocked call
            float now = UnityEngine.Time.unscaledTime;
            if (!s.RefusedLogged.TryGetValue(reason, out float last) || now - last >= 10f)
            {
                s.RefusedLogged[reason] = now;
                s.Say("LIMIT: " + reason + (n == 0 ? "" : " - refused " + (n + 1) + " times so far"));
            }
            Notify(s, reason);
        }
    }
}
