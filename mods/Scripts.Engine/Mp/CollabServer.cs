using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;
using Scripts.Collab;

namespace Scripts.Mp
{
    /// <summary>
    /// The server's side of live co-editing: one Master per block script that anybody has open (docs/scripting-collab.md). This class has no
    /// game in it: it takes a message from an editor (the game's own, or one that came over the wire), checks it (admin, size, rate), does
    /// it through the Master, and sends what comes back through the host it was given. Every message is handled inside a try: a bad one is
    /// dropped and counted, never thrown. Tick() (about four times a second) expires editors and locks, broadcasts who is where, sends the
    /// heartbeat, and saves a script that has been changed (a second after the last change, or every five seconds while it keeps changing).
    /// </summary>
    internal sealed class CollabServer
    {
        private sealed class Bucket { internal double Tokens, Last; }

        private sealed class EditorLimits
        {
            internal Bucket Ops = new Bucket { Tokens = Co.OpsPerSecond }, Pres = new Bucket { Tokens = Co.PresPerSecond }, Misc = new Bucket { Tokens = Co.MiscPerSecond };
        }

        private sealed class Session
        {
            internal string File = "";
            internal Master M;
            internal DateTime LastHb, LastPres;
            internal bool PresDirty;
            internal readonly Dictionary<string, string> Seen = new Dictionary<string, string>();   // presence as last sent, to see what changed
            internal readonly HashSet<string> Names = new HashSet<string>();                         // everybody who edited it since it was opened (for the audit line)
            internal int Saves;
        }

        private readonly ICollabHost _host;
        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<string, EditorLimits> _limits = new Dictionary<string, EditorLimits>();

        /// <summary>Messages dropped (too big, too many, unreadable, not an admin) and handled: for the status command.</summary>
        internal int Dropped, Handled, Saved;

        internal CollabServer(ICollabHost host) { _host = host; }

        // ---- what the game asks ----

        internal bool HasSession(string file) => file != null && _sessions.ContainsKey(file);

        /// <summary>The names of the people editing a script now (empty when nobody is).</summary>
        internal string[] NamesOf(string file, string exceptEditor = null)
        {
            if (file == null || !_sessions.TryGetValue(file, out Session s)) return new string[0];
            return s.M.Editors.Where(e => e.Id != exceptEditor).Select(e => e.Name).ToArray();
        }

        /// <summary>The ids of the editors in a script (the tests and the status command).</summary>
        internal string[] EditorsIn(string file) => file != null && _sessions.TryGetValue(file, out Session s) ? s.M.Editors.Select(e => e.Id).ToArray() : new string[0];

        /// <summary>Every editor in any script (the game checks that each remote one is still connected).</summary>
        internal string[] AllEditors() => _sessions.Values.SelectMany(s => s.M.Editors.Select(e => e.Id)).Distinct().ToArray();

        internal int Rev(string file) => file != null && _sessions.TryGetValue(file, out Session s) ? s.M.Rev : -1;
        internal Program ProgramOf(string file) => file != null && _sessions.TryGetValue(file, out Session s) ? s.M.Program : null;
        internal Master MasterOf(string file) => file != null && _sessions.TryGetValue(file, out Session s) ? s.M : null;

        /// <summary>One line per open script, for `scripts collab`.</summary>
        internal string[] Describe() => _sessions.Values.Select(s => s.File + ": rev " + s.M.Rev + ", " + s.M.Editors.Count + " editing (" + string.Join(", ", s.M.Editors.Select(e => e.Name).ToArray()) + "), " + (s.M.Dirty ? "unsaved" : "saved") + ", " + s.Saves + " saves").ToArray();

        // ---- messages ----

        /// <summary>A message from an editor. `editor` is who the connection says it is (never read from the message).</summary>
        internal void OnMessage(string editor, string kind, string json)
        {
            try
            {
                if (!Co.IsCo(kind) || string.IsNullOrEmpty(editor)) return;
                if (json == null || json.Length > Co.MaxMessage) { Dropped++; return; }
                JObject o = Parse(json);
                if (o == null) { Dropped++; return; }
                string file = o.Value<string>("f");
                if (!NameClean.IsScriptFile(file)) { Dropped++; return; }
                Handled++;
                if (kind == Co.Open) { Open(editor, file); return; }
                if (!_sessions.TryGetValue(file, out Session s) || s.M.Editor(editor) == null)
                {
                    // not in a session (it ended, or never began): say so, once per message, so the editor does not wait for ever
                    if (kind != Co.Close) Reply(editor, Co.Closed, file, new JObject { ["why"] = "You are not editing this script any more." });
                    return;
                }
                switch (kind)
                {
                    case Co.Ops: if (Take(editor, b => b.Ops, Co.OpsPerSecond)) DoOps(s, editor, o); else Dropped++; break;
                    case Co.Pres: if (Take(editor, b => b.Pres, Co.PresPerSecond)) DoPres(s, editor, o); else Dropped++; break;
                    case Co.Grab: if (Take(editor, b => b.Misc, Co.MiscPerSecond)) DoGrab(s, editor, o); else Dropped++; break;
                    case Co.Release: if (Take(editor, b => b.Misc, Co.MiscPerSecond)) DoRelease(s, editor, o); else Dropped++; break;
                    case Co.Resync: if (Take(editor, b => b.Misc, Co.MiscPerSecond)) SendSnapshot(s, editor); else Dropped++; break;
                    case Co.Save: if (Take(editor, b => b.Misc, Co.MiscPerSecond)) SaveNow(s); else Dropped++; break;
                    case Co.Close: Leave(s, editor); break;
                    default: Dropped++; break;
                }
            }
            catch (Exception) { Dropped++; }
        }

        private static JObject Parse(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 40 })
                    return JObject.Load(reader);
            }
            catch (Exception) { return null; }
        }

        private bool Take(string editor, Func<EditorLimits, Bucket> which, double perSecond)
        {
            if (!_limits.TryGetValue(editor, out EditorLimits l)) _limits[editor] = l = new EditorLimits();
            Bucket b = which(l);
            double now = (_host.Now - new DateTime(2000, 1, 1)).TotalSeconds;
            if (b.Last == 0) b.Last = now;
            b.Tokens = Math.Min(perSecond, b.Tokens + (now - b.Last) * perSecond);
            b.Last = now;
            if (b.Tokens < 1.0) return false;
            b.Tokens -= 1.0;
            return true;
        }

        private void Reply(string editor, string kind, string file, JObject body)
        {
            body["f"] = file;
            _host.Send(editor, kind, body.ToString(Formatting.None));
        }

        private void ToAll(Session s, string kind, JObject body)
        {
            body["f"] = s.File;
            string json = body.ToString(Formatting.None);
            foreach (Presence p in s.M.Editors.ToList()) _host.Send(p.Id, kind, json);
        }

        // ---- opening and leaving ----

        private void Open(string editor, string file)
        {
            if (!_host.IsAdmin(editor)) { Dropped++; Reply(editor, Co.Closed, file, new JObject { ["why"] = "Only admins can edit scripts on a server." }); return; }
            if (!_host.Exists(file)) { Reply(editor, Co.Closed, file, new JObject { ["why"] = "That script has no blocks on the server." }); return; }
            if (!_sessions.TryGetValue(file, out Session s))
            {
                Program program = _host.Load(file, out string error);
                if (program == null) { Reply(editor, Co.Closed, file, new JObject { ["why"] = string.IsNullOrEmpty(error) ? "The server could not open that script." : error }); return; }
                bool needsIds = !Tree.IdsOk(program);
                s = new Session { File = file, M = new Master(program, () => _host.Now), LastHb = _host.Now, LastPres = _host.Now };
                _sessions[file] = s;
                if (needsIds) Save(s, "gave every block an id");   // (the ids go into the file once, so every later open has them)
            }
            Presence p = s.M.Join(editor, _host.NameOf(editor));
            if (p == null) { Reply(editor, Co.Closed, file, new JObject { ["why"] = "Too many people are editing this script." }); return; }
            s.Names.Add(p.Name);
            SendSnapshot(s, editor);
            s.PresDirty = true;
        }

        private void SendSnapshot(Session s, string editor)
        {
            JObject snap = s.M.Snapshot();
            snap["you"] = editor;
            Reply(editor, Co.Snap, s.File, snap);
        }

        private void Leave(Session s, string editor)
        {
            s.M.Leave(editor);
            _limits.Remove(editor);
            s.PresDirty = true;
            if (s.M.Editors.Count == 0) Drop(s);
        }

        /// <summary>An editor's connection is gone (it left the server): out of every script it was in.</summary>
        internal void EditorGone(string editor)
        {
            foreach (Session s in _sessions.Values.ToList())
                if (s.M.Editor(editor) != null) Leave(s, editor);
            _limits.Remove(editor);
        }

        private void Drop(Session s)
        {
            if (s.M.Dirty) Save(s, "last one out");
            _sessions.Remove(s.File);
        }

        /// <summary>The session is over for everybody (the script was deleted, or the server is stopping): tell them, save, forget it.</summary>
        internal void CloseFile(string file, string why, bool save = true)
        {
            if (file == null || !_sessions.TryGetValue(file, out Session s)) return;
            ToAll(s, Co.Closed, new JObject { ["why"] = why });
            if (save && s.M.Dirty) Save(s, "closing");
            _sessions.Remove(file);
        }

        internal void Shutdown(string why)
        {
            foreach (string file in _sessions.Keys.ToList()) CloseFile(file, why);
        }

        // ---- ops, presence, locks ----

        private void DoOps(Session s, string editor, JObject o)
        {
            var ops = new List<Op>();
            if (o["ops"] is JArray arr)
                foreach (JToken t in arr.Take(Co.MaxOpsPerMessage)) { Op op = Op.FromJson(t); if (op != null) ops.Add(op); }
            long seq = o.Value<long?>("seq") ?? 0;
            Batch b = s.M.Submit(editor, seq, ops, out bool everyone);
            JObject bj = b.ToJson();
            if (everyone) ToAll(s, Co.Batch, bj);
            else Reply(editor, Co.Batch, s.File, bj);
        }

        private void DoPres(Session s, string editor, JObject o)
        {
            float x = (float)(o.Value<double?>("x") ?? 0), y = (float)(o.Value<double?>("y") ?? 0);
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) { Dropped++; return; }
            s.M.UpdatePresence(editor, o.Value<string>("page"), o.Value<string>("sel"), Math.Max(-1e6f, Math.Min(1e6f, x)), Math.Max(-1e6f, Math.Min(1e6f, y)));
            s.PresDirty = true;
        }

        private void DoGrab(Session s, string editor, JObject o)
        {
            var uids = Uids(o);
            List<string> refused = s.M.Grab(editor, uids);
            if (refused.Count > 0) Reply(editor, Co.Refused, s.File, new JObject { ["why"] = HolderMessage(s, editor, refused) });
            s.PresDirty = true;
        }

        private void DoRelease(Session s, string editor, JObject o)
        {
            var uids = Uids(o);
            s.M.Release(editor, uids.Count > 0 ? uids : null);
            s.PresDirty = true;
        }

        private static List<string> Uids(JObject o)
        {
            var list = new List<string>();
            if (o["uids"] is JArray a) foreach (JToken t in a.Take(Co.MaxGrab)) { string u = (string)t; if (!string.IsNullOrEmpty(u) && u.Length <= 16) list.Add(u); }
            return list;
        }

        /// <summary>"Bob is moving that": whose hold made the grab fail (a hold on the block, something around it or inside it).</summary>
        private static string HolderMessage(Session s, string editor, List<string> refused)
        {
            foreach (string uid in refused)
            {
                var chain = new List<string> { uid };
                chain.AddRange(Tree.Ancestors(s.M.Program, uid));
                foreach (Presence p in s.M.Editors)
                    if (p.Id != editor && p.Grabbed.Any(g => chain.Contains(g))) return p.Name + " is moving that.";
            }
            return "That cannot be picked up right now.";
        }

        // ---- the clock ----

        /// <summary>About four times a second: expire silent editors and old holds, tell everyone who is where, send the heartbeat, save what changed.</summary>
        internal void Tick()
        {
            DateTime now = _host.Now;
            foreach (Session s in _sessions.Values.ToList())
            {
                try
                {
                    if (!_host.Exists(s.File)) { CloseFile(s.File, "That script was removed.", save: false); continue; }
                    List<string> gone = s.M.Tick();
                    if (gone.Count > 0) { s.PresDirty = true; foreach (string g in gone) _limits.Remove(g); }
                    if (s.M.Editors.Count == 0) { Drop(s); continue; }
                    if (s.PresDirty && (now - s.LastPres).TotalSeconds >= Co.PresenceBroadcastSeconds) BroadcastPresence(s, now);
                    if ((now - s.LastHb).TotalSeconds >= Co.HeartbeatSeconds) { s.LastHb = now; ToAll(s, Co.Hb, s.M.Heartbeat()); }
                    if (s.M.TakeSaveRequest()) Save(s, "autosave");
                }
                catch (Exception) { Dropped++; }
            }
        }

        private void BroadcastPresence(Session s, DateTime now)
        {
            s.LastPres = now;
            s.PresDirty = false;
            ToAll(s, Co.Pres, new JObject { ["editors"] = new JArray(s.M.Editors.Select(p => (object)p.ToJson()).ToArray()) });
        }

        // ---- saving ----

        private void SaveNow(Session s)
        {
            if (s.M.Dirty) Save(s, "asked");   // (it stays dirty: the autosave writes it once more, and an unchanged file is not rewritten)
        }

        private void Save(Session s, string why)
        {
            string names = string.Join(", ", s.M.Editors.Select(e => e.Name).Union(s.Names).Distinct().ToArray());
            string problem = _host.Save(s.File, s.M.Program);
            if (problem.Length == 0)
            {
                s.Saves++; Saved++;
                _host.Wrote(s.File, string.Join(", ", s.M.Editors.Select(e => e.Name).ToArray()));
                _host.Audit("co-edit: saved " + s.File + " (" + why + "), editors: " + names);
            }
            else _host.Audit("co-edit: could not save " + s.File + ": " + problem);
        }
    }
}
