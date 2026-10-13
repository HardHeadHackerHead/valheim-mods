using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;
using Scripts.Editor;
using static Scripts.Editor.Words;

namespace Scripts.Mp
{
    /// <summary>
    /// The scripts of a server this game is connected to (docs/scripting-multiplayer.md, section 7). Everyone with the mod sees the list of what
    /// the server runs and its limits, read only; an admin also reads code, blocks and logs and changes things. Every read answers from a cache
    /// that RemoteClientCore keeps fresh in the background (about twice a second while the window is open), so a frame never waits for the
    /// network; a write is one request whose answer comes to its callback. The logic is in AdminCore.cs (tested); this class only joins it to the
    /// transport (Wire) and to the editor.
    /// </summary>
    internal sealed class RemoteBackend : IScriptsBackend
    {
        private readonly RemoteClientCore _core = new RemoteClientCore();
        private readonly List<CollabSession> _sessions = new List<CollabSession>();
        private string[][] _listSeen;
        private HashSet<string> _blocksSeen = new HashSet<string>();

        internal RemoteBackend()
        {
            _core.Send = (op, json, id) =>
            {
                if (Wire.IsServer || !Wire.ServerHasMod) return false;
                Wire.SendToServer("req", json, id);
                return true;
            };
        }

        private static double Now => UnityEngine.Time.realtimeSinceStartupAsDouble;

        /// <summary>The server's answer to a request (the transport calls this for every message from the server).</summary>
        internal void OnServerMessage(string kind, string json, long id)
        {
            if (kind == "res") { _core.OnRes(id, json, Now); return; }
            if (!Co.IsCo(kind)) return;
            foreach (CollabSession s in _sessions.ToList())
            {
                try { s.OnMessage(kind, json); } catch (Exception) { }
                if (s.Status == CollabSession.State.Closed) _sessions.Remove(s);
            }
        }

        /// <summary>Joined or left a server, or the answer to hello arrived: start over.</summary>
        internal void Reset()
        {
            foreach (CollabSession s in _sessions.ToList()) { try { s.Close(); } catch (Exception) { } }
            _sessions.Clear();
            _core.Reset();
            _listSeen = null;
            _blocksSeen = new HashSet<string>();
        }

        public void Pump() { _core.Pump(Now, Admin); }

        private object Want(string op, JObject args = null) => _core.Want(op, args, Now);
        private static JObject A(params object[] kv)
        {
            var o = new JObject();
            for (int i = 0; i + 1 < kv.Length; i += 2) o[(string)kv[i]] = JToken.FromObject(kv[i + 1]);
            return o;
        }

        private SettingsInfo Settings => Want(AdminOps.Settings) as SettingsInfo;

        /// <summary>This player is an admin: what the server's hello said, and (once it has answered) what its settings say, whichever is stricter.</summary>
        private bool Admin => Wire.LocalIsAdmin && (Settings == null || Settings.Admin);

        // ---- who and where ----

        public bool IsRemote => true;
        public string Origin
        {
            get
            {
                SettingsInfo s = Settings;
                string name = s != null && s.World.Length > 0 ? s.World : L("scripts_origin_server", "the server");
                return L("scripts_origin_remote", "Server: {0}", name) + (s != null && s.Version.Length > 0 ? " (" + s.Version + ")" : "");
            }
        }

        public bool Connected => _core.Answering(Now);
        public bool CanChange => Admin && Connected;
        public bool CanRead => Admin && Connected;
        public bool LimitsFromServer => true;

        public string ReadOnlyReason
        {
            get
            {
                if (!Connected) return Note;
                return Admin ? "" : L("scripts_admin_only", "Only admins can change scripts on a server. You can see what it runs.");
            }
        }

        public string Note
        {
            get
            {
                if (Connected) return "";
                if (_core.Error.Length > 0 && _core.LastFail > -1e8) return L("scripts_no_answer", "No answer from the server.") + " (" + _core.Error + ")";
                return L("scripts_asking_server_scripts", "Asking the server for its scripts...");
            }
        }

        // ---- reads (from the cache) ----

        public string[][] List()
        {
            var rows = Want(AdminOps.List) as string[][];
            if (rows == null) return _listSeen ?? new string[0][];
            if (!ReferenceEquals(rows, _listSeen))
            {
                _listSeen = rows;
                _blocksSeen = new HashSet<string>(rows.Where(r => r.Length > 8 && r[8] == "1").Select(r => r[0]));
            }
            return rows;
        }

        public bool HasBlocks(string file) { List(); return file != null && _blocksSeen.Contains(file); }

        public string Source(string file) => CanRead && !string.IsNullOrEmpty(file) ? Want(AdminOps.Source, A("file", file)) as string ?? "" : "";

        public string[] Log(string file, int count) => CanRead && !string.IsNullOrEmpty(file) ? Want(AdminOps.Log, A("file", file, "count", count)) as string[] ?? new string[0] : new string[0];

        public string[][] LogRows(string file, int minLevel, bool onlyLevel, bool mine, string contains, int max)
        {
            if (!CanRead || string.IsNullOrEmpty(file)) return new string[0][];
            return Want(AdminOps.LogRows, A("file", file, "min", minLevel, "only", onlyLevel, "mine", mine, "contains", contains ?? "", "max", max)) as string[][] ?? new string[0][];
        }

        private StatusInfo Status(string file) => CanRead && !string.IsNullOrEmpty(file) ? Want(AdminOps.Status, A("file", file)) as StatusInfo : null;

        public long[] LogCounts(string file) => Status(file)?.Counts ?? new long[7];
        public string Stats(string file) => Status(file)?.Stats ?? "";
        public string[] Refused(string file) => Status(file)?.Refused ?? new string[0];
        public bool Trace(string file) => Status(file)?.Trace ?? false;
        public string LogPath(string file) => Status(file)?.Path ?? "";

        public string[] LimitLines(string[] needs)
        {
            SettingsInfo s = Settings;
            if (s == null) return new[] { L("scripts_asking_server_limits", "Asking the server for its limits...") };
            var lines = new List<string>();
            if (s.Lines.TryGetValue("", out string[] baseLines)) lines.AddRange(baseLines);
            foreach (string n in needs) if (s.Lines.TryGetValue(n, out string[] more)) lines.AddRange(more);
            return lines.ToArray();
        }

        // ---- changes (one request each) ----

        private void Call(string op, JObject args, Action<bool, string, JObject> done)
        {
            if (!CanChange) { done(false, ReadOnlyReason, null); return; }
            _core.Call(op, args, done, Now);
        }

        private static string Why(bool ok, string why) => ok ? "" : (why.Length > 0 ? why : "The server did not do that.");

        public void LogText(string file, int minLevel, bool onlyLevel, bool mine, string contains, Action<string> done)
        {
            if (!CanRead) { done(""); return; }
            _core.Call(AdminOps.LogText, A("file", file, "min", minLevel, "only", onlyLevel, "mine", mine, "contains", contains ?? ""), (ok, why, d) => done(ok ? d.Value<string>("text") ?? "" : ""), Now);
        }

        public void LogClear(string file) { Call(AdminOps.LogClear, A("file", file), (ok, why, d) => { }); }
        public void SetTrace(string file, bool on) { Call(AdminOps.Trace, A("file", file, "on", on), (ok, why, d) => { }); }
        public void Stop(string file) { Call(AdminOps.Stop, A("file", file), (ok, why, d) => { }); }
        public void Allow(string file, string power, bool on) { Call(AdminOps.Allow, A("file", file, "power", power, "on", on), (ok, why, d) => { }); }

        public void Start(string file, Action<string> done)
        {
            Call(AdminOps.Start, A("file", file), (ok, why, d) => done(ok ? d.Value<string>("why") ?? "" : Why(ok, why)));
        }

        public void Write(string file, string text, string baseHash, Action<string, bool> done)
        {
            // the server checks that the script is still the version this began from, and says who changed it if not
            Call(AdminOps.Write, A("file", file, "text", text ?? "", "base", baseHash ?? ""), (ok, why, d) =>
            {
                if (!ok) { done(Why(ok, why), false); return; }
                done(d.Value<string>("why") ?? "", d.Value<bool?>("stale") ?? false);
            });
        }

        public void New(string name, Action<string> done)
        {
            Call(AdminOps.New, A("name", name ?? ""), (ok, why, d) => done(ok ? d.Value<string>("file") ?? "" : ""));
        }

        public void Delete(string file, Action<bool> done)
        {
            Call(AdminOps.Delete, A("file", file), (ok, why, d) => done(ok && (d.Value<bool?>("ok") ?? true)));
        }

        public void LoadBlocks(string file, Action<Program, string> done)
        {
            if (!CanRead) { done(null, ReadOnlyReason); return; }
            _core.Call(AdminOps.Blocks, A("file", file), (ok, why, d) =>
            {
                if (!ok) { done(null, Why(ok, why)); return; }
                Program p = Program.FromJson(d.Value<string>("json") ?? "", out string error);
                done(p, error);
            }, Now);
        }

        public void SaveBlocks(string file, Program program, Compiled compiled, Action<string> done)
        {
            // the blocks and the code made from them go together; the server makes the code again itself and keeps its own
            Call(AdminOps.WriteBlocks, A("file", file, "blocks", program.ToJson(), "js", compiled != null && compiled.Ok ? compiled.Js : ""),
                (ok, why, d) => done(ok ? d.Value<string>("why") ?? "" : Why(ok, why)));
        }

        public void CreateBlocks(string title, Action<string> done)
        {
            Call(AdminOps.New, A("name", title ?? "", "blocks", true), (ok, why, d) => done(ok ? d.Value<string>("file") ?? "" : ""));
        }

        /// <summary>An admin joins the server's copy of a block script: the same session the host's own editor has, over the wire.</summary>
        public CollabSession OpenCollab(string file)
        {
            if (!CanChange || !Connected || string.IsNullOrEmpty(file)) return null;
            var s = new CollabSession(file);
            s.Send = (kind, json) =>
            {
                if (Wire.IsServer || !Wire.ServerHasMod) return false;
                Wire.SendToServer(kind, json);
                return true;
            };
            _sessions.Add(s);
            s.Open();
            return s;
        }

        /// <summary>For `scripts server`: what this game knows about the server, in a few lines.</summary>
        internal string[] Describe()
        {
            SettingsInfo s = Settings;
            return new[]
            {
                "connected: " + Connected + ", answering since " + (Now - _core.LastOk).ToString("0.0") + " s ago, last problem: " + (_core.Error.Length > 0 ? _core.Error : "none"),
                "server world: " + (s != null ? s.World : "?") + ", version " + (s != null ? s.Version : "?") + ", admin by the server's word: " + (s != null ? s.Admin.ToString() : "?") + ", admin by hello: " + Wire.LocalIsAdmin,
                "scripts the server runs: " + (_listSeen != null ? string.Join(", ", _listSeen.Select(r => r[1] + " (" + r[2] + ")").ToArray()) : "(not known yet)"),
                "requests waiting: " + _core.Book.Count + ", questions cached: " + _core.Cache.Count,
            };
        }
    }
}
