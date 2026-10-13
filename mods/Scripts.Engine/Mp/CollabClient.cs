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
    /// One editor's session in a script being edited together (docs/scripting-collab.md): the core's `Client` plus the messages. It has no
    /// game in it: whoever creates it gives it a way to send (the wire, or the game's own server for the host's editor) and calls
    /// OnMessage for every `co.*` message that arrives for its script. The editor holds `Live` (the program, the same object for the
    /// whole session), calls Commit() at the end of each gesture, and listens to the events to redraw.
    ///
    /// States: Opening (asked, no snapshot yet), Active, Closed (the server said so, or this editor left).
    /// </summary>
    internal sealed class CollabSession
    {
        internal enum State { Opening, Active, Closed }

        private Client _client;
        private DateTime _lastPres = DateTime.MinValue;
        private string _pPage = "", _pSel = ""; private float _pX, _pY; private bool _pHas;

        internal readonly string File;
        internal State Status { get; private set; } = State.Opening;
        /// <summary>Why the session ended, in words, when it did.</summary>
        internal string ClosedWhy { get; private set; } = "";
        /// <summary>My id in this session (the server says it in the snapshot; it is what `by` is in my own batches).</summary>
        internal string Me { get; private set; } = "";
        internal readonly List<Presence> All = new List<Presence>();

        /// <summary>Sends (kind, json) to the server. False: it could not be sent.</summary>
        internal Func<string, string, bool> Send = (kind, json) => false;
        internal Func<DateTime> Clock = () => DateTime.UtcNow;

        /// <summary>The snapshot came: Live is the program now (the first time only).</summary>
        internal event Action Ready;
        /// <summary>Other people's changes were done to Live in place: the ids of what changed, and whether blocks came or went.</summary>
        internal event Action<IReadOnlyCollection<string>, bool> Changed;
        /// <summary>Live was replaced by a snapshot: anything that holds one of its blocks must look it up again by id.</summary>
        internal event Action Resynced;
        /// <summary>A message to show the person ("Bob is holding that", "Not allowed").</summary>
        internal event Action<string> Message;
        internal event Action Closed;
        internal event Action PresenceChanged;

        internal CollabSession(string file) { File = file; }

        internal Program Live => _client != null ? _client.Live : null;
        internal bool Active => Status == State.Active && _client != null;
        internal bool CanUndo => Active && _client.CanUndo;
        internal bool CanRedo => Active && _client.CanRedo;
        internal bool HasUncommitted => Active && _client.HasUncommitted;
        internal int Pending => _client != null ? _client.Pending : 0;
        internal int Rev => _client != null ? _client.Rev : 0;
        internal int Resyncs => _client != null ? _client.Resyncs : 0;
        internal bool Busy { set { if (_client != null) _client.Busy = value; } }
        internal IEnumerable<Presence> Others => All.Where(p => p.Id != Me);
        /// <summary>The ones who share the script with me (others, not counting me).</summary>
        internal int OtherCount => All.Count(p => p.Id != Me);

        // ---- starting and ending ----

        internal void Open() { SendJson(Co.Open, new JObject()); }

        internal void Close()
        {
            if (Status == State.Closed) return;
            SendJson(Co.Close, new JObject());
            End("");
        }

        private void End(string why)
        {
            if (Status == State.Closed) return;
            Status = State.Closed; ClosedWhy = why ?? "";
            try { Closed?.Invoke(); } catch (Exception) { }
        }

        // ---- messages in ----

        internal void OnMessage(string kind, string json)
        {
            try
            {
                if (!Co.IsCo(kind) || json == null || json.Length > Co.MaxMessage || Status == State.Closed) return;
                JObject o = Parse(json);
                if (o == null || o.Value<string>("f") != File) return;
                switch (kind)
                {
                    case Co.Snap: OnSnap(o); break;
                    case Co.Batch:
                        if (_client == null) break;
                        Batch b = Batch.FromJson(o);
                        if (b != null) _client.OnBatch(b);
                        Pump();
                        break;
                    case Co.Hb:
                        if (_client == null) break;
                        _client.OnHeartbeat(o.Value<int?>("rev") ?? 0, o.Value<string>("hash") ?? "");
                        Pump();
                        break;
                    case Co.Pres: ReadPresence(o["editors"]); break;
                    case Co.Refused: Say(o.Value<string>("why")); break;
                    case Co.Closed: End(o.Value<string>("why") ?? ""); break;
                }
            }
            catch (Exception) { }
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

        private void Say(string text) { if (!string.IsNullOrEmpty(text)) { try { Message?.Invoke(text); } catch (Exception) { } } }

        private void OnSnap(JObject o)
        {
            if (!(o["program"] is JObject pj)) return;
            ReadPresence(o["editors"]);
            if (_client == null)
            {
                Program p = Program.FromJson(pj.ToString(), out string error);
                if (p == null) { End(string.IsNullOrEmpty(error) ? "The server's copy could not be read." : error); return; }
                Me = o.Value<string>("you") ?? "";
                _client = new Client(p, Me, o.Value<int?>("rev") ?? 0);
                _client.Changed += (ids, structural) => { try { Changed?.Invoke(ids, structural); } catch (Exception) { } };
                _client.Resynced += () => { try { Resynced?.Invoke(); } catch (Exception) { } };
                _client.Refused += reasons => Say(reasons != null && reasons.Count > 0 ? reasons[0] : "");
                Status = State.Active;
                try { Ready?.Invoke(); } catch (Exception) { }
            }
            else _client.OnSnapshot(o);
        }

        private void ReadPresence(JToken editors)
        {
            if (!(editors is JArray a)) return;
            All.Clear();
            foreach (JToken t in a.Take(40)) { Presence p = Presence.FromJson(t); if (p != null) All.Add(p); }
            try { PresenceChanged?.Invoke(); } catch (Exception) { }
        }

        // ---- messages out ----

        private void SendJson(string kind, JObject body)
        {
            body["f"] = File;
            Send(kind, body.ToString(Formatting.None));
        }

        /// <summary>Sends what changed since the last commit. Call it when a gesture ends (a drop, a typed value, a delete), not while one goes on.</summary>
        internal bool Commit()
        {
            if (!Active) return false;
            Commit c = _client.CommitChanges();
            if (c == null) { Pump(); return false; }
            SendOps(c);
            Pump();
            return true;
        }

        private void SendOps(Commit c)
        {
            var body = new JObject { ["seq"] = c.Seq, ["ops"] = new JArray(c.Ops.Select(op => (object)op.ToJson()).ToArray()) };
            body["f"] = File;
            string json = body.ToString(Formatting.None);
            if (json.Length > Co.MaxMessage)
            {
                // too much for one message (a huge paste): it is not shared. The master's copy comes back and replaces ours.
                Say("That change is too big to share live, so it was not kept.");
                SendJson(Co.Open, new JObject());
                return;
            }
            Send(Co.Ops, json);
        }

        /// <summary>If this copy missed something, commit what is waiting first, then ask for the snapshot.</summary>
        internal void Pump()
        {
            if (_client == null || !_client.NeedsResync) return;
            Commit c = _client.CommitChanges();
            if (c != null) SendOps(c);
            if (_client.TakeResyncRequest()) SendJson(Co.Resync, new JObject());
        }

        internal bool Undo()
        {
            if (!Active) return false;
            Commit c = _client.Undo();
            if (c == null) return false;
            SendOps(c);
            return true;
        }

        internal bool Redo()
        {
            if (!Active) return false;
            Commit c = _client.Redo();
            if (c == null) return false;
            SendOps(c);
            return true;
        }

        /// <summary>Called every picture: asks for a snapshot if needed, and says "still here" now and then.</summary>
        internal void Tick()
        {
            if (!Active) return;
            Pump();
            DateTime now = Clock();
            if ((now - _lastPres).TotalSeconds >= Co.KeepAliveSeconds) SendPresenceNow(now);
        }

        /// <summary>Where I am: the page, the block I have selected (its id, or ""), and my pointer on the workspace. Sent when it changed (at most 4 times a second).</summary>
        internal void SendPresence(string page, string selected, bool pointerOnWork, float x, float y)
        {
            if (!Active) return;
            DateTime now = Clock();
            bool changed = page != _pPage || (selected ?? "") != _pSel || pointerOnWork != _pHas || (pointerOnWork && (Math.Abs(x - _pX) > 3f || Math.Abs(y - _pY) > 3f));
            if (!changed || (now - _lastPres).TotalSeconds < 0.25) return;   // (a change that waits is seen again by the next call: the stored values are only those last sent)
            _pPage = page ?? ""; _pSel = selected ?? ""; _pHas = pointerOnWork; if (pointerOnWork) { _pX = x; _pY = y; }
            SendPresenceNow(now);
        }

        private void SendPresenceNow(DateTime now)
        {
            _lastPres = now;
            SendJson(Co.Pres, new JObject { ["page"] = _pPage, ["sel"] = _pSel, ["x"] = _pHas ? _pX : -1e5f, ["y"] = _pHas ? _pY : -1e5f });
        }

        /// <summary>I am dragging these blocks (their ids): nobody else may change them until I let go.</summary>
        internal void Grab(IEnumerable<string> uids)
        {
            if (!Active) return;
            var list = uids.Where(u => !string.IsNullOrEmpty(u)).Take(Co.MaxGrab).ToList();
            if (list.Count == 0) return;
            SendJson(Co.Grab, new JObject { ["uids"] = new JArray(list.Cast<object>().ToArray()) });
        }

        internal void Release()
        {
            if (!Active) return;
            SendJson(Co.Release, new JObject());
        }

        /// <summary>Have the server write the script now (before it runs).</summary>
        internal void Save()
        {
            if (!Active) return;
            SendJson(Co.Save, new JObject());
        }

        // ---- who holds what (for the editor) ----

        /// <summary>The name of another editor who holds this block, something around it, or something inside it; null when nobody does.</summary>
        internal string HeldBy(Block b)
        {
            if (!Active || b == null || OtherCount == 0) return null;
            var mine = new HashSet<string>();
            if (b.Uid.Length > 0) { mine.Add(b.Uid); foreach (string a in Tree.Ancestors(Live, b.Uid)) mine.Add(a); }
            foreach (Block inner in b.All()) if (inner.Uid.Length > 0) mine.Add(inner.Uid);
            foreach (Presence p in Others)
                if (p.Grabbed.Any(g => mine.Contains(g))) return p.Name;
            return null;
        }

        /// <summary>The presence of another editor by id.</summary>
        internal Presence Find(string id) => All.FirstOrDefault(p => p.Id == id);
    }
}
