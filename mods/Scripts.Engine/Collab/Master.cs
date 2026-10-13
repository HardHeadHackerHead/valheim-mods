using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>What the master sends after it has done a client's ops: the ops that took effect, in order, with their revisions.</summary>
    internal sealed class Batch
    {
        internal int RevFrom, RevTo;           // the revisions of the first and the last op that took effect; RevTo = RevFrom - 1 when none did
        internal string By = "";               // the editor whose ops these are
        internal long Seq;                     // that editor's number for the commit (its acknowledgement)
        internal List<Op> Ops = new List<Op>();
        internal int Dropped;                  // how many submitted ops did nothing (the sender's copy has drifted)
        internal bool Resync;                  // the sender should take a snapshot (some op was dropped or adjusted)
        internal string Hash = "";             // Canon.Hash of the program after this batch: a copy that has not drifted has the same
        internal List<string> Reasons = new List<string>();   // why ops were dropped (for the sender's message)

        internal JObject ToJson()
        {
            var o = new JObject { ["from"] = RevFrom, ["to"] = RevTo, ["by"] = By, ["seq"] = Seq, ["hash"] = Hash };
            if (Dropped > 0) o["dropped"] = Dropped;
            if (Resync) o["resync"] = true;
            o["ops"] = new JArray(Ops.Select(op => (object)op.ToJson()).ToArray());
            if (Reasons.Count > 0) o["why"] = new JArray(Reasons.Take(5).Cast<object>().ToArray());
            return o;
        }

        internal static Batch FromJson(JToken j)
        {
            try
            {
                var o = j as JObject;
                if (o == null) return null;
                var b = new Batch
                {
                    RevFrom = o.Value<int?>("from") ?? 0, RevTo = o.Value<int?>("to") ?? 0, By = o.Value<string>("by") ?? "", Seq = o.Value<long?>("seq") ?? 0,
                    Hash = o.Value<string>("hash") ?? "", Dropped = o.Value<int?>("dropped") ?? 0, Resync = o.Value<bool?>("resync") ?? false,
                };
                if (o["ops"] is JArray ops) foreach (JToken t in ops.Take(2000)) { Op op = Op.FromJson(t); if (op != null) b.Ops.Add(op); }
                if (o["why"] is JArray why) foreach (JToken t in why.Take(5)) b.Reasons.Add((string)t);
                return b;
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>What is known about one person editing: shown to the others (a name, a colour, where they are, what they hold).</summary>
    internal sealed class Presence
    {
        internal string Id = "", Name = "";
        internal int Color;                    // 0 to 7, the lowest number nobody here has
        internal string Page = "";             // "blocks", "code", ...
        internal string Selected = "";         // the id of the block they have selected
        internal float X, Y;                   // where their pointer is on the workspace
        internal readonly List<string> Grabbed = new List<string>();   // the ids they hold (a drag in progress): nobody else may change them
        internal DateTime LastSeen;

        internal JObject ToJson()
        {
            var o = new JObject { ["id"] = Id, ["name"] = Name, ["color"] = Color, ["page"] = Page, ["sel"] = Selected, ["x"] = Math.Round(X), ["y"] = Math.Round(Y) };
            if (Grabbed.Count > 0) o["grab"] = new JArray(Grabbed.Cast<object>().ToArray());
            return o;
        }

        internal static Presence FromJson(JToken t)
        {
            var o = t as JObject;
            if (o == null) return null;
            var p = new Presence { Id = o.Value<string>("id") ?? "", Name = o.Value<string>("name") ?? "", Color = o.Value<int?>("color") ?? 0, Page = o.Value<string>("page") ?? "", Selected = o.Value<string>("sel") ?? "", X = (float)(o.Value<double?>("x") ?? 0), Y = (float)(o.Value<double?>("y") ?? 0) };
            if (o["grab"] is JArray g) foreach (JToken u in g.Take(50)) p.Grabbed.Add((string)u);
            return p;
        }
    }

    internal enum SaveWhy { None, Quiet, TooLong }

    /// <summary>
    /// The server's copy of one script being edited by several people (docs/scripting-collab.md, "the master"). It takes each editor's ops in
    /// the order they arrive, does them (last writer wins on one field), numbers them, says who holds what and who is where, and tells the
    /// caller when to save. Nothing here knows about the network or the editor: the caller passes editor ids, ops and the time, and sends what comes back.
    /// </summary>
    internal sealed class Master
    {
        internal const double LockSeconds = 10, GoneSeconds = 15, SaveQuiet = 1.0, SaveLongest = 5.0;
        internal const int MaxEditors = 16;

        private readonly Func<DateTime> _clock;
        private readonly Dictionary<string, Presence> _editors = new Dictionary<string, Presence>();
        private readonly List<string> _joinOrder = new List<string>();
        private readonly Dictionary<string, string> _locks = new Dictionary<string, string>();   // id of a block or stack -> the editor holding it
        private DateTime _dirtySince, _lastChange;

        /// <summary>The program as it is now. The caller compiles and saves it; it must not change it (edits come in as ops).</summary>
        internal Program Program { get; private set; }
        internal int Rev { get; private set; }
        internal bool Dirty { get; private set; }

        /// <summary>The master takes the program over (giving ids to anything that has none).</summary>
        internal Master(Program program, Func<DateTime> clock = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            Program = program;
            Uids.Ensure(Program);
        }

        // ---- people ----

        internal IReadOnlyCollection<Presence> Editors => _editors.Values;
        internal Presence Editor(string id) => _editors.TryGetValue(id ?? "", out Presence p) ? p : null;

        /// <summary>An editor opens the script. Returns its presence (a colour is given), or null if there are too many.</summary>
        internal Presence Join(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_editors.TryGetValue(id, out Presence existing)) { existing.LastSeen = _clock(); if (!string.IsNullOrEmpty(name)) existing.Name = Clip(name, 24); return existing; }
            if (_editors.Count >= MaxEditors) return null;
            var used = new HashSet<int>(_editors.Values.Select(e => e.Color));
            int color = 0;
            while (used.Contains(color) && color < 7) color++;
            var p = new Presence { Id = id, Name = Clip(string.IsNullOrWhiteSpace(name) ? "editor" : name, 24), Color = color, LastSeen = _clock() };
            _editors[id] = p;
            _joinOrder.Add(id);
            return p;
        }

        internal void Leave(string id)
        {
            ReleaseAll(id);   // (first: Release reads the editor's own list of holds, so it must still be known)
            foreach (string held in _locks.Where(kv => kv.Value == id).Select(kv => kv.Key).ToList()) _locks.Remove(held);
            _editors.Remove(id ?? "");
            _joinOrder.Remove(id);
        }

        /// <summary>The editor says where it is and what it has selected; this also counts as "still here".</summary>
        internal bool UpdatePresence(string id, string page, string selected, float x, float y)
        {
            Presence p = Editor(id);
            if (p == null) return false;
            p.Page = Clip(page ?? "", 20); p.Selected = Clip(selected ?? "", 16); p.X = x; p.Y = y; p.LastSeen = _clock();
            return true;
        }

        /// <summary>Lets go of what has been quiet too long, and forgets editors that have been gone longer. Call about once a second. Returns the editors that were dropped.</summary>
        internal List<string> Tick()
        {
            DateTime now = _clock();
            var gone = new List<string>();
            foreach (Presence p in _editors.Values.ToList())
            {
                double quiet = (now - p.LastSeen).TotalSeconds;
                if (quiet > LockSeconds && p.Grabbed.Count > 0) ReleaseAll(p.Id);
                if (quiet > GoneSeconds) { gone.Add(p.Id); Leave(p.Id); }
            }
            return gone;
        }

        // ---- locks: "I am dragging these" ----

        /// <summary>
        /// The editor takes hold of blocks or stacks (a drag): until released nobody else's op may change them or anything inside them.
        /// Returns the ids that were refused because somebody else holds them or something inside them (or something around them).
        /// </summary>
        internal List<string> Grab(string editor, IEnumerable<string> uids)
        {
            var refused = new List<string>();
            Presence p = Editor(editor);
            if (p == null) return uids.ToList();
            p.LastSeen = _clock();
            foreach (string uid in uids)
            {
                if (string.IsNullOrEmpty(uid) || !Tree.Has(Program, uid)) { refused.Add(uid); continue; }
                if (HeldByOther(editor, uid, out _) != null) { refused.Add(uid); continue; }
                _locks[uid] = editor;
                if (!p.Grabbed.Contains(uid)) p.Grabbed.Add(uid);
            }
            return refused;
        }

        internal void Release(string editor, IEnumerable<string> uids = null)
        {
            Presence p = Editor(editor);
            foreach (string uid in (uids ?? (p != null ? p.Grabbed.ToList() : new List<string>())).ToList())
            {
                if (_locks.TryGetValue(uid, out string owner) && owner == editor) _locks.Remove(uid);
                p?.Grabbed.Remove(uid);
            }
        }

        private void ReleaseAll(string editor) => Release(editor);

        /// <summary>The editor that holds this id, something around it, or (if `inside`) something inside it; null if nobody but `editor`.</summary>
        private string HeldByOther(string editor, string uid, out string which, bool inside = true)
        {
            which = null;
            if (_locks.Count == 0) return null;
            if (_locks.TryGetValue(uid, out string o) && o != editor) { which = uid; return o; }
            foreach (string a in Tree.Ancestors(Program, uid))
                if (_locks.TryGetValue(a, out string ao) && ao != editor) { which = a; return ao; }
            if (inside)
            {
                IEnumerable<string> below = null;
                Program.Top t = Tree.FindTop(Program, uid);
                if (t != null) below = t.Chain.SelectMany(b => b.All()).Select(b => b.Uid);
                else if (Tree.Find(Program, uid, out Loc loc)) below = loc.Block.All().Select(b => b.Uid);
                if (below != null)
                    foreach (string b in below)
                        if (_locks.TryGetValue(b, out string bo) && bo != editor) { which = b; return bo; }
            }
            return null;
        }

        // ---- ops ----

        /// <summary>
        /// Does an editor's ops, in order. An op on something another editor holds is dropped (with the reason); an op whose target is gone,
        /// or that would break a limit, is dropped; the rest take effect. The returned batch is what everyone is sent (or only the sender
        /// when nothing took effect, see ToEveryone).
        /// </summary>
        internal Batch Submit(string editor, long seq, IEnumerable<Op> ops, out bool toEveryone)
        {
            DateTime now = _clock();
            Presence who = Editor(editor);
            if (who != null) who.LastSeen = now;
            var batch = new Batch { By = editor ?? "", Seq = seq, RevFrom = Rev + 1 };
            int adjusted = 0;
            foreach (Op op in ops.Take(2000))
            {
                if (who == null) { batch.Dropped++; Note(batch, "you are not in this editing session"); continue; }
                string held = null;
                foreach (string target in op.Targets())
                {
                    string owner = HeldByOther(editor, target, out _, inside: op.Kind == OpKind.Remove || op.Kind == OpKind.RemoveTop);
                    if (owner != null) { held = Editor(owner)?.Name ?? "someone"; break; }
                }
                if (held != null) { batch.Dropped++; Note(batch, held + " is holding that"); continue; }
                Op copy = Op.FromJson(op.ToJson());   // (what is applied and sent on is what the wire carries: nothing the sender kept a hold of)
                if (copy == null) { batch.Dropped++; Note(batch, "unreadable op"); continue; }
                copy.By = editor;
                ApplyResult r = Ops.Apply(Program, copy);
                if (!r.Done) { batch.Dropped++; Note(batch, r.Reason); continue; }
                if (r.Status == ApplyStatus.Adjusted) adjusted++;
                Rev++;
                batch.Ops.Add(copy);
            }
            batch.RevTo = Rev;
            if (batch.Ops.Count > 0) Ops.Gc(Program);
            batch.Resync = batch.Dropped > 0;   // (an adjusted op is found by the hash if the sender's copy really differs)
            batch.Hash = batch.Ops.Count > 0 ? Canon.Hash(Program) : "";
            if (batch.Ops.Count > 0)
            {
                if (!Dirty) _dirtySince = now;
                Dirty = true; _lastChange = now;
            }
            toEveryone = batch.Ops.Count > 0;
            return batch;
        }

        private static void Note(Batch b, string why) { if (b.Reasons.Count < 5 && !b.Reasons.Contains(why)) b.Reasons.Add(why); }

        /// <summary>The whole state, for an editor that joins or has drifted: the program (with ids), the revision, and who is here.</summary>
        internal JObject Snapshot()
        {
            return new JObject
            {
                ["rev"] = Rev,
                ["program"] = JObject.Parse(Program.ToJson()),
                ["hash"] = Canon.Hash(Program),
                ["editors"] = new JArray(_joinOrder.Where(id => _editors.ContainsKey(id)).Select(id => (object)_editors[id].ToJson()).ToArray()),
            };
        }

        // ---- the heartbeat ----

        private int _hashRev = -1; private string _hash = "";

        /// <summary>The revision and the hash of the program now: sent to every editor every second or two, so one that missed the last batch finds out.</summary>
        internal JObject Heartbeat()
        {
            if (_hashRev != Rev) { _hash = Canon.Hash(Program); _hashRev = Rev; }
            return new JObject { ["rev"] = Rev, ["hash"] = _hash };
        }

        // ---- saving ----

        /// <summary>
        /// True when the script should be saved now: it has changed, and either nobody has changed it for a second or it has been
        /// changing for five. (The caller compiles, writes the blocks and the code, and so tells the host to restart the script.)
        /// </summary>
        internal bool TakeSaveRequest()
        {
            if (!Dirty) return false;
            DateTime now = _clock();
            if ((now - _lastChange).TotalSeconds >= SaveQuiet || (now - _dirtySince).TotalSeconds >= SaveLongest) { Dirty = false; return true; }
            return false;
        }

        private static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}
