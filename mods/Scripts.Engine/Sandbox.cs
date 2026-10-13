using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using Jint;
using Jint.Native;

namespace Scripts
{
    /// <summary>A script went over a budget (time, memory, statements): the call is stopped, the script is not.</summary>
    internal sealed class BudgetException : Exception
    {
        public readonly string Kind;   // "time", "memory"
        public BudgetException(string kind, string message) : base(message) { Kind = kind; }
    }

    /// <summary>
    /// Stops a script that goes over its budget. Jint calls Check() on every statement and function call, and the budget is armed by the
    /// host just before each call (Arm), so a handler gets a fixed slice of time, whatever the engine's own timer does.
    ///
    /// The memory guard is ours because Jint's LimitMemory does nothing on this runtime (measured by the spike on 2026-10-12: 32 MB went
    /// past an 8 MB limit): it compares the managed heap now with the heap when the call began, on every statement (a script can double
    /// a string in one statement, so checking now and then would let it past).
    /// </summary>
    internal sealed class Budget : Constraint
    {
        private long _deadline;
        private long _memoryStart;
        private long _maxGrowth = 16L * 1024 * 1024;
        private long _statements;
        private long _maxStatements = 200000;
        private bool _armed;

        internal long Used;          // statements in the call that just ended
        internal double LastMs;      // time of the call that just ended

        /// <summary>Starts the clock for one call into the script.</summary>
        internal void Arm(double milliseconds, long maxGrowthBytes, long maxStatements)
        {
            _deadline = Stopwatch.GetTimestamp() + (long)(milliseconds * Stopwatch.Frequency / 1000.0);
            _memoryStart = GC.GetTotalMemory(false);
            _maxGrowth = maxGrowthBytes;
            _maxStatements = maxStatements;
            _statements = 0;
            _armed = true;
            _start = Stopwatch.GetTimestamp();
        }

        private long _start;

        internal void Disarm()
        {
            if (!_armed) return;
            _armed = false;
            Used = _statements;
            LastMs = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
        }

        public override void Reset() { /* the host arms each call itself */ }

        public override void Check()
        {
            if (!_armed) return;
            if (++_statements > _maxStatements) throw new BudgetException("statements", "ran too many steps in one go");
            if (Stopwatch.GetTimestamp() > _deadline) throw new BudgetException("time", "took too long");
            if (GC.GetTotalMemory(false) - _memoryStart > _maxGrowth) throw new BudgetException("memory", "used too much memory");
        }
    }

    /// <summary>Makes the engine a script runs in: the limits on, the dangerous parts of the language out, and nothing of the game reachable.</summary>
    internal static class Sandbox
    {
        // Run once in every new engine, before the script. The originals are captured in a closure no script can reach.
        private const string Hardening = @"
(function () {
  var cap = 100000;
  var repeat = String.prototype.repeat, padStart = String.prototype.padStart, padEnd = String.prototype.padEnd;
  String.prototype.repeat = function (n) { if (this.length * n > cap) throw new RangeError('That text would be too long'); return repeat.call(this, n); };
  String.prototype.padStart = function (n, f) { if (n > cap) throw new RangeError('That text would be too long'); return padStart.call(this, n, f); };
  String.prototype.padEnd = function (n, f) { if (n > cap) throw new RangeError('That text would be too long'); return padEnd.call(this, n, f); };
  var names = ['Date', 'Reflect', 'Proxy', 'WebAssembly', 'Intl', 'SharedArrayBuffer', 'Atomics', 'FinalizationRegistry', 'WeakRef', 'escape', 'unescape'];
  for (var i = 0; i < names.length; i++) { try { delete globalThis[names[i]]; } catch (e) { } }
  try { Math.random = undefined; delete Math.random; } catch (e) { }
})();
";

        internal static Engine Create(Budget budget)
        {
            var engine = new Engine(o =>
            {
                o.Constraint(budget);
                o.LimitRecursion(64);
                o.MaxArraySize(100000);
                o.DisableStringCompilation();          // no eval, no new Function('...')
                o.Strict(false);
                o.CatchClrExceptions(ex => false);     // a game-side fault is not turned into something the script can catch
            });
            // Hardening runs unlimited but it is ours and short
            engine.Execute(Hardening);
            return engine;
        }

        /// <summary>The text of a script error for a person: the first line, no engine internals.</summary>
        /// <summary>Where an error came from (the line in the script's code) and its stack, for the log.</summary>
        internal static void Describe(Exception e, out int line, out string detail)
        {
            line = 0; detail = "";
            try
            {
                if (e is Jint.Runtime.JavaScriptException js)
                {
                    line = js.Location.Start.Line;
                    detail = js.JavaScriptStackTrace ?? "";
                }
                else if (e is Esprima.ParserException pe) line = pe.LineNumber;
                else if (!(e is ScriptError) && !(e is BudgetException) && !(e is Jint.Runtime.RecursionDepthOverflowException) && !(e is Jint.Runtime.StatementsCountOverflowException))
                    detail = (e.GetType().Name + ": " + e.Message + "\n" + string.Join("\n", (e.StackTrace ?? "").Split('\n').Take(6).ToArray())).Trim();
            }
            catch (Exception) { }
        }

        /// <summary>A short look at the value an event handler was given, for the trace: its fields and the plain values in them.</summary>
        internal static string Summary(Jint.Native.JsValue v)
        {
            try
            {
                if (v == null || v.IsUndefined()) return "";
                if (!v.IsObject()) return v.ToString();
                var parts = new List<string>();
                foreach (var kv in v.AsObject().GetOwnProperties())
                {
                    Jint.Native.JsValue value = kv.Value.Value;
                    string shown = value == null ? "" : value.IsString() || value.IsNumber() || value.IsBoolean() ? value.ToString() : value.IsObject() ? (value.AsObject().Get("name") is Jint.Native.JsValue n && n.IsString() ? n.AsString() : value.AsObject().Get("prefab") is Jint.Native.JsValue p && p.IsString() ? p.AsString() : "{...}") : "";
                    if (shown.Length > 0) parts.Add(kv.Key + ": " + shown);
                    if (parts.Count >= 12) break;
                }
                return string.Join(", ", parts.ToArray());
            }
            catch (Exception) { return ""; }
        }

        internal static string Plain(Exception e)
        {
            if (e is BudgetException b) return "This script " + b.Message + ".";
            if (e is Jint.Runtime.JavaScriptException js) return js.Message + (js.Location.Start.Line > 0 ? " (line " + js.Location.Start.Line + ")" : "");
            if (e is Jint.Runtime.RecursionDepthOverflowException) return "This script calls itself too deeply.";
            if (e is Jint.Runtime.StatementsCountOverflowException) return "This script ran too many steps in one go.";
            if (e is Esprima.ParserException pe) return "The script could not be read: " + pe.Description + (pe.LineNumber > 0 ? " (line " + pe.LineNumber + ")" : "");
            string m = e.Message ?? e.GetType().Name;
            int nl = m.IndexOf('\n');
            return nl > 0 ? m.Substring(0, nl) : m;
        }
    }
}
