using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    // Everything in this file is plain C# (no Unity, no game types) so that tests/Scripts.Admin.Tests can run it: the request and answer
    // frames of remote editing (docs/scripting-multiplayer.md, section 7), who may ask what, the rate limit, name cleaning, size caps, the
    // client's cache and its table of requests waiting for an answer, and the server's dispatcher. The game-facing parts are in
    // Backend.cs, RemoteBackend.cs and AdminServer.cs, which only hand things to and from these.

    /// <summary>The requests an editor can send to the server that runs the scripts.</summary>
    internal static class AdminOps
    {
        internal const string List = "list", Settings = "settings", Source = "source", Log = "log", LogRows = "logrows", LogText = "logtext",
            LogCounts = "logcounts", Stats = "stats", Refused = "refused", Trace = "trace", Status = "status", Start = "start", Stop = "stop",
            Allow = "allow", Write = "write", WriteBlocks = "writeBlocks", New = "new", Delete = "delete", Blocks = "blocks", LogClear = "logclear", LogPath = "logpath";

        private static readonly HashSet<string> All = new HashSet<string>
        {
            List, Settings, Source, Log, LogRows, LogText, LogCounts, Stats, Refused, Trace, Status, Start, Stop, Allow, Write, WriteBlocks, New, Delete, Blocks, LogClear, LogPath,
        };

        internal static bool Known(string op) => op != null && All.Contains(op);

        /// <summary>What anyone with the mod may ask (to see which scripts a server runs and what they may do); everything else is for admins.</summary>
        internal static bool IsPublic(string op) => op == List || op == Settings;

        /// <summary>Requests that only read (the editor repeats these on a timer; the rest are things somebody pressed).</summary>
        internal static bool IsRead(string op) => op == List || op == Settings || op == Source || op == Log || op == LogRows || op == LogText || op == LogCounts || op == Stats || op == Refused || op == Status || op == Blocks || op == LogPath;
    }

    internal static class AdminGate
    {
        internal static bool Allowed(string op, bool isAdmin) => AdminOps.Known(op) && (AdminOps.IsPublic(op) || isAdmin);
    }

    /// <summary>The checks on what a request carries, before the server does anything with it (the game side checks again where it matters).</summary>
    internal static class AdminValidate
    {
        /// <summary>Null when the request is fine, else why not, in words.</summary>
        internal static string Check(string op, JObject a)
        {
            string file = a.Value<string>("file");
            bool makes = op == AdminOps.Write || op == AdminOps.WriteBlocks || op == AdminOps.New || op == AdminOps.List || op == AdminOps.Settings;
            if (!makes && file != null && !NameClean.IsScriptFile(file)) return "That is not a script's name.";
            if (!makes && file == null && op != AdminOps.Settings) return "That request needs a script.";
            switch (op)
            {
                case AdminOps.Write:
                    if ((a.Value<string>("base") ?? "").Length > 64) return "That is not a version of the script.";
                    if (!NameClean.TextFits(a.Value<string>("text") ?? "", AdminFrames.MaxText)) return "That script is too long (120 KB at most).";
                    if (string.IsNullOrEmpty(a.Value<string>("text"))) return "There is nothing to write.";
                    break;
                case AdminOps.WriteBlocks:
                    if (!NameClean.TextFits(a.Value<string>("blocks") ?? "", AdminFrames.MaxBlocks)) return "Those blocks are too big (400 KB at most).";
                    if (!NameClean.TextFits(a.Value<string>("js") ?? "", AdminFrames.MaxText)) return "That script is too long (120 KB at most).";
                    if (string.IsNullOrEmpty(a.Value<string>("blocks"))) return "There is nothing to write.";
                    break;
                case AdminOps.New:
                    if ((a.Value<string>("name") ?? "").Length > 40) return "That name is too long (40 characters at most).";
                    break;
                case AdminOps.Allow:
                    if ((a.Value<string>("power") ?? "").Length > 20) return "There is no such power.";
                    break;
            }
            return null;
        }
    }

    /// <summary>A refusal the server gives in words (the editor shows them as they are).</summary>
    internal sealed class AdminError : Exception
    {
        internal AdminError(string message) : base(message) { }
    }

    /// <summary>What the server does for an admin. The game implements it over its scripts; the tests implement it over a list.</summary>
    internal interface IAdminOps
    {
        /// <summary>Does one request and returns its answer's data. Throws AdminError to refuse in words.</summary>
        JObject Run(string op, JObject args, long peer);
    }

    // ---------------------------------------------------------------------------------------------------------------- frames

    internal static class AdminFrames
    {
        internal const int MaxFrame = 512 * 1024;       // a request or answer over this is not read
        internal const int MaxText = 120 * 1024;        // a script's code, in bytes
        internal const int MaxBlocks = 400 * 1024;      // a block program's JSON, in bytes
        internal const int MaxAnswerBytes = 240 * 1024; // log rows are cut to fit this
        internal const int MaxRows = 300, MaxDetail = 600, MaxLogLines = 200;

        internal static string Req(string op, JObject args = null)
        {
            var o = new JObject { ["op"] = op };
            if (args != null && args.HasValues) o["a"] = args;
            return o.ToString(Formatting.None);
        }

        internal static bool TryReq(string json, out string op, out JObject args)
        {
            op = null; args = new JObject();
            JObject o = Parse(json);
            if (o == null) return false;
            op = o.Value<string>("op");
            if (string.IsNullOrEmpty(op) || op.Length > 20) return false;
            if (o["a"] is JObject a) args = a;
            return true;
        }

        internal static string Res(bool ok, string why = "", JObject data = null)
        {
            var o = new JObject { ["ok"] = ok };
            if (!string.IsNullOrEmpty(why)) o["why"] = why;
            if (data != null) o["d"] = data;
            return o.ToString(Formatting.None);
        }

        internal static bool TryRes(string json, out bool ok, out string why, out JObject data)
        {
            ok = false; why = ""; data = new JObject();
            JObject o = Parse(json);
            if (o == null) return false;
            ok = o.Value<bool?>("ok") ?? false;
            why = o.Value<string>("why") ?? "";
            if (o["d"] is JObject d) data = d;
            return true;
        }

        /// <summary>JSON with a depth limit and a size limit; null when it is not JSON or is too big.</summary>
        internal static JObject Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxFrame) return null;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 16 })
                    return JObject.Load(reader);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Log rows cut so that the answer fits: the newest rows, with long details shortened.</summary>
        internal static string[][] CapRows(string[][] rows)
        {
            if (rows == null) return new string[0][];
            IEnumerable<string[]> q = rows.Skip(Math.Max(0, rows.Length - MaxRows));
            var list = new List<string[]>();
            foreach (string[] row in q)
            {
                string[] r = row;
                if (r.Length > 5 && r[5].Length > MaxDetail) { r = (string[])r.Clone(); r[5] = r[5].Substring(0, MaxDetail) + "..."; }
                list.Add(r);
            }
            long bytes = list.Sum(r => r.Sum(c => (long)c.Length + 3));
            while (bytes > MaxAnswerBytes && list.Count > 1) { bytes -= list[0].Sum(c => (long)c.Length + 3); list.RemoveAt(0); }
            return list.ToArray();
        }

        internal static JArray ToJson(string[][] rows) => new JArray(rows.Select(r => new JArray(r.Cast<object>().ToArray())).Cast<object>().ToArray());

        internal static string[][] Rows(JToken t)
        {
            if (!(t is JArray a)) return new string[0][];
            return a.Select(r => r is JArray ra ? ra.Select(c => c.Type == JTokenType.Null ? "" : c.ToString()).ToArray() : new string[0]).Where(r => r.Length > 0).ToArray();
        }

        internal static string[] Strings(JToken t) => t is JArray a ? a.Select(c => c.Type == JTokenType.Null ? "" : c.ToString()).ToArray() : new string[0];
    }

    // ---------------------------------------------------------------------------------------------------------------- names and sizes

    internal static class NameClean
    {
        private static readonly Regex ScriptFileName = new Regex("^[A-Za-z0-9_-]{1,40}\\.js$", RegexOptions.Compiled);

        /// <summary>A script's file name as HostBridge makes it: letters, digits, - and _, at most 40, with .js.</summary>
        internal static string ScriptFile(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in Path.GetFileNameWithoutExtension(name ?? "script")) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
            string s = sb.Length == 0 ? "script" : sb.ToString();
            return (s.Length > 40 ? s.Substring(0, 40) : s) + ".js";
        }

        /// <summary>Whether this is already exactly a script's file name (what an existing script's `file` is): nothing to clean.</summary>
        internal static bool IsScriptFile(string file) => file != null && ScriptFileName.IsMatch(file);

        internal static bool TextFits(string text, int maxBytes) => text != null && Encoding.UTF8.GetByteCount(text) <= maxBytes;
    }

    /// <summary>A token bucket for each connection: 20 requests a second, a burst of 20. Over it the request is dropped (and counted).</summary>
    internal sealed class AdminLimiter
    {
        private sealed class Bucket { internal double Tokens, Last; }
        private readonly Dictionary<long, Bucket> _peers = new Dictionary<long, Bucket>();
        private readonly double _perSecond, _burst;
        private int _calls;
        internal int Dropped { get; private set; }

        internal AdminLimiter(double perSecond = 20.0, double burst = 20.0) { _perSecond = perSecond; _burst = burst; }

        internal bool Allow(long peer, double now)
        {
            if (!_peers.TryGetValue(peer, out Bucket b)) _peers[peer] = b = new Bucket { Tokens = _burst, Last = now };
            b.Tokens = Math.Min(_burst, b.Tokens + Math.Max(0.0, now - b.Last) * _perSecond);
            b.Last = now;
            if (++_calls % 200 == 0) foreach (long gone in _peers.Where(kv => now - kv.Value.Last > 120.0).Select(kv => kv.Key).ToList()) _peers.Remove(gone);
            if (b.Tokens < 1.0) { Dropped++; return false; }
            b.Tokens -= 1.0;
            return true;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- the server's side

    /// <summary>What the server does with a request: limits it, reads it, checks who asked, runs it through IAdminOps, and builds the answer. Never throws.</summary>
    internal sealed class AdminDispatcher
    {
        private readonly AdminLimiter _limiter = new AdminLimiter();
        private readonly IAdminOps _ops;
        internal int Handled, Refused;
        internal int Dropped => _limiter.Dropped;

        internal AdminDispatcher(IAdminOps ops) { _ops = ops; }

        /// <summary>The answer's JSON, or null when the request is dropped (too many, so no answer is sent).</summary>
        internal string Handle(long peer, string json, bool isAdmin, double now)
        {
            try
            {
                if (!_limiter.Allow(peer, now)) return null;
                Handled++;
                if (!AdminFrames.TryReq(json, out string op, out JObject args)) { Refused++; return AdminFrames.Res(false, "That request could not be read."); }
                if (!AdminOps.Known(op)) { Refused++; return AdminFrames.Res(false, "The server does not know that request."); }
                if (!AdminGate.Allowed(op, isAdmin)) { Refused++; return AdminFrames.Res(false, "Only admins can do that."); }
                string bad = AdminValidate.Check(op, args);
                if (bad != null) { Refused++; return AdminFrames.Res(false, bad); }
                try
                {
                    JObject d = _ops.Run(op, args, peer) ?? new JObject();
                    return AdminFrames.Res(true, "", d);
                }
                catch (AdminError e) { Refused++; return AdminFrames.Res(false, e.Message); }
                catch (Exception) { Refused++; return AdminFrames.Res(false, "The server could not do that."); }
            }
            catch (Exception) { return AdminFrames.Res(false, "The server could not do that."); }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- the client's side

    /// <summary>Requests sent and not answered yet. An answer finishes its request; one that waits too long finishes it as "no answer".</summary>
    internal sealed class RequestBook
    {
        internal const double Timeout = 5.0;

        internal sealed class Pending
        {
            internal long Id;
            internal string Op, Key = "";
            internal double Deadline;
            internal Action<bool, string, JObject> Done;
        }

        private long _next = 1;
        private readonly Dictionary<long, Pending> _pending = new Dictionary<long, Pending>();

        internal int Count => _pending.Count;

        internal long Add(string op, string key, Action<bool, string, JObject> done, double now)
        {
            long id = _next++;
            _pending[id] = new Pending { Id = id, Op = op, Key = key ?? "", Done = done, Deadline = now + Timeout };
            return id;
        }

        internal bool KeyPending(string key) => _pending.Values.Any(p => p.Key == key);

        internal bool Complete(long id, bool ok, string why, JObject data)
        {
            if (!_pending.TryGetValue(id, out Pending p)) return false;
            _pending.Remove(id);
            try { p.Done?.Invoke(ok, why ?? "", data ?? new JObject()); } catch (Exception) { }
            return true;
        }

        /// <summary>Finishes the requests whose time is up; returns how many.</summary>
        internal int Expire(double now)
        {
            List<Pending> late = _pending.Values.Where(p => now >= p.Deadline).ToList();
            foreach (Pending p in late) Complete(p.Id, false, "no answer from the server", null);
            return late.Count;
        }

        internal void Clear() { _pending.Clear(); }
    }

    /// <summary>
    /// The last answer to each question the editor keeps asking (the list, a script's status, its log...). Asking again (Want) keeps a
    /// question interesting for a few seconds; the ones that are interesting and older than their time are due to be asked again.
    /// Reading never waits: no answer yet is null.
    /// </summary>
    internal sealed class QueryCache
    {
        internal sealed class Entry
        {
            internal string Op = "";
            internal JObject Args = new JObject();
            internal object Value;
            internal double At = -1e9, InterestUntil;
            internal bool Has;
        }

        internal const double Interest = 3.0;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        internal static string Key(string op, JObject args) => op + (args != null && args.HasValues ? ":" + args.ToString(Formatting.None) : "");

        internal int Count => _entries.Count;

        /// <summary>The last answer (null if none yet), and the wish to have it kept fresh.</summary>
        internal object Want(string op, JObject args, double now)
        {
            string key = Key(op, args);
            if (!_entries.TryGetValue(key, out Entry e)) _entries[key] = e = new Entry { Op = op, Args = args ?? new JObject() };
            e.InterestUntil = now + Interest;
            return e.Has ? e.Value : null;
        }

        internal void Put(string key, object value, double now)
        {
            if (!_entries.TryGetValue(key, out Entry e)) return;
            e.Value = value; e.Has = true; e.At = now;
        }

        /// <summary>Entries somebody still wants whose answer is older than maxAge(op): the questions to ask now, oldest first.</summary>
        internal List<Entry> Due(double now, Func<string, double> maxAge, Func<string, bool> asking, int max)
        {
            var list = new List<Entry>();
            foreach (KeyValuePair<string, Entry> kv in _entries.ToList())
            {
                Entry e = kv.Value;
                if (now > e.InterestUntil) { if (now - e.At > 30.0) _entries.Remove(kv.Key); continue; }
                if (asking(kv.Key)) continue;
                if (now - e.At >= maxAge(e.Op)) list.Add(e);
            }
            return list.OrderBy(e => e.At).Take(max).ToList();
        }

        /// <summary>After something was changed: every answer is out of date (the old ones stay in view until the new ones come).</summary>
        internal void Stale() { foreach (Entry e in _entries.Values) e.At = -1e9; }

        internal void Clear() { _entries.Clear(); }
    }

    internal sealed class SettingsInfo
    {
        internal string World = "", Version = "";
        internal bool Admin;
        internal readonly Dictionary<string, string[]> Lines = new Dictionary<string, string[]>();
    }

    internal sealed class StatusInfo
    {
        internal long[] Counts = new long[7];
        internal string Stats = "", Path = "";
        internal string[] Refused = new string[0];
        internal bool Trace;
    }

    /// <summary>Turns an answer's data into the plain values the editor reads (done once, when the answer arrives).</summary>
    internal static class RemoteParse
    {
        internal static object Convert(string op, JObject d)
        {
            switch (op)
            {
                case AdminOps.List: return AdminFrames.Rows(d["rows"]);
                case AdminOps.LogRows: return AdminFrames.Rows(d["rows"]);
                case AdminOps.Log: return AdminFrames.Strings(d["lines"]);
                case AdminOps.Source: return d.Value<string>("text") ?? "";
                case AdminOps.Status:
                    {
                        var s = new StatusInfo();
                        if (d["counts"] is JArray c) for (int i = 0; i < Math.Min(7, c.Count); i++) s.Counts[i] = c[i].Value<long>();
                        s.Stats = d.Value<string>("stats") ?? "";
                        s.Path = d.Value<string>("path") ?? "";
                        s.Refused = AdminFrames.Strings(d["refused"]);
                        s.Trace = d.Value<bool?>("trace") ?? false;
                        return s;
                    }
                case AdminOps.Settings:
                    {
                        var s = new SettingsInfo { World = d.Value<string>("world") ?? "", Version = d.Value<string>("version") ?? "", Admin = d.Value<bool?>("admin") ?? false };
                        if (d["lines"] is JObject lines) foreach (JProperty p in lines.Properties()) s.Lines[p.Name] = AdminFrames.Strings(p.Value);
                        return s;
                    }
                default: return d;
            }
        }
    }

    /// <summary>
    /// The client's half, with no game in it: wants become requests, answers fill the cache, a request nobody answers times out. The game's
    /// RemoteBackend gives it a way to send (Send) and calls Pump every picture and OnRes for every answer.
    /// </summary>
    internal sealed class RemoteClientCore
    {
        internal readonly QueryCache Cache = new QueryCache();
        internal readonly RequestBook Book = new RequestBook();

        /// <summary>Sends one request (op, json, id). False: it could not be sent.</summary>
        internal Func<string, string, long, bool> Send = (op, json, id) => false;
        internal double LastOk = -1e9, LastFail = -1e9;
        internal string Error = "";
        internal int MaxAsking = 6;

        internal bool Answering(double now) => now - LastOk < 8.0;

        internal static double MaxAge(string op)
        {
            switch (op)
            {
                case AdminOps.Settings: return 3.0;
                case AdminOps.Source: return 1.0;
                default: return 0.5;
            }
        }

        /// <summary>The last answer to a question (converted), keeping it fresh. Null if there is none yet.</summary>
        internal object Want(string op, JObject args, double now) => Cache.Want(op, args, now);

        /// <summary>Sends the due questions (only the public ones when the player is not an admin) and ends the ones that waited too long.</summary>
        internal void Pump(double now, bool admin)
        {
            _now = now;
            if (Book.Expire(now) > 0) { LastFail = now; if (!Answering(now)) Error = "no answer from the server"; }
            if (Book.Count >= MaxAsking) return;
            foreach (QueryCache.Entry e in Cache.Due(now, MaxAge, key => Book.KeyPending(key), MaxAsking - Book.Count))
            {
                if (!admin && !AdminOps.IsPublic(e.Op)) continue;
                string key = QueryCache.Key(e.Op, e.Args), op = e.Op;
                long id = Book.Add(op, key, (ok, why, d) => OnAnswer(key, op, ok, why, d), now);
                if (!Send(op, AdminFrames.Req(op, e.Args), id)) { Book.Complete(id, false, "not connected", null); LastFail = now; }
            }
        }

        private double _now;

        private void OnAnswer(string key, string op, bool ok, string why, JObject d)
        {
            if (ok)
            {
                LastOk = _now; Error = "";
                Cache.Put(key, RemoteParse.Convert(op, d), _now);
            }
            else if (why != "no answer from the server") { Error = why; LastOk = _now; }   // the server answered, with a refusal
            else Error = why;
        }

        /// <summary>A request somebody pressed (a write, a start): sent at once, the answer goes to done (also "no answer" after the timeout).</summary>
        internal void Call(string op, JObject args, Action<bool, string, JObject> done, double now)
        {
            _now = now;
            long id = Book.Add(op, "", (ok, why, d) =>
            {
                if (ok) { LastOk = now; Cache.Stale(); }
                done?.Invoke(ok, why, d);
            }, now);
            if (!Send(op, AdminFrames.Req(op, args), id)) Book.Complete(id, false, "not connected", null);
        }

        /// <summary>An answer reached the client.</summary>
        internal void OnRes(long id, string json, double now)
        {
            _now = now;
            if (!AdminFrames.TryRes(json, out bool ok, out string why, out JObject d)) { Book.Complete(id, false, "The answer could not be read.", null); return; }
            Book.Complete(id, ok, why, d);
        }

        internal void Reset()
        {
            Cache.Clear();
            Book.Clear();
            LastOk = LastFail = -1e9;
            Error = "";
        }
    }
}
