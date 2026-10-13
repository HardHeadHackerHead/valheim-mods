using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>What an editor sends after a change: its ops, in order, with its number for them (the master's acknowledgement names it).</summary>
    internal sealed class Commit
    {
        internal long Seq;
        internal List<Op> Ops = new List<Op>();
        internal List<Op> Inverse = new List<Op>();   // what undoes it (with "only if it is still what I made it")
    }

    /// <summary>
    /// One editor's copy of a script being edited together (docs/scripting-collab.md, "the client").
    ///
    /// Three states are kept: the MASTER's (`_base`, what the server has as of Rev: every batch is done to it, in the master's order), the
    /// COMMITTED one (the master's plus this editor's ops that the master has not acknowledged yet), and LIVE (the committed one plus what
    /// the editor has changed since its last commit). The editor changes Live as it always has; CommitChanges() turns that into ops to send.
    ///
    /// When another editor's batch arrives while this editor has changes of its own that the master has not seen, the master will do the other
    /// batch FIRST and this editor's ops after, so the copy is rebased: master state, then this editor's ops, then its unsent edits, and
    /// Live is brought to that in place (a block that is in both stays the same object: selection and a drag in progress survive). That
    /// is also why two people changing the same field, or inserting at the same place, end the same on every copy: one rule, applied in
    /// the master's order everywhere. An op of this editor's that the master dropped simply is not in the rebuilt copy.
    ///
    /// A copy asks for a snapshot only when a batch went missing or the master's hash says the master's own state is not what it computed.
    /// </summary>
    internal sealed class Client
    {
        private sealed class Entry { internal List<Op> Inverse; }

        private readonly List<Commit> _pending = new List<Commit>();
        private readonly List<Entry> _undo = new List<Entry>(), _redo = new List<Entry>();
        private Program _base, _committed;
        private long _seq;
        private bool _resyncAsked;

        internal string Id { get; }
        /// <summary>The program the editor shows and changes. The same object for the whole session (a snapshot replaces what is in it).</summary>
        internal Program Live { get; }
        /// <summary>The last revision of the master that this copy has taken in.</summary>
        internal int Rev { get; private set; }
        /// <summary>The editor is in the middle of a change it has not finished (a drag): for the editor's own use (nothing here waits for it any more).</summary>
        internal bool Busy { get; set; }
        /// <summary>This copy needs a snapshot (until one arrives, batches are ignored and nothing is sent).</summary>
        internal bool NeedsResync { get; private set; }
        internal int Pending => _pending.Count;
        /// <summary>The editor has changed Live since the last commit (a commit would send something).</summary>
        internal bool HasUncommitted => !Canon.Equal(_committed, Live);
        /// <summary>The master's state as this copy has it, and the committed one, as canonical text (a debugging aid for the tests).</summary>
        internal string MasterJson => Canon.Json(_base);
        internal string CommittedJson => Canon.Json(_committed);
        internal bool CanUndo => _undo.Count > 0;
        internal bool CanRedo => _redo.Count > 0;
        internal int Resyncs { get; private set; }
        internal int Rebases { get; private set; }
        /// <summary>Why this copy last needed a snapshot ("missed", "hash"): for the log and the tests.</summary>
        internal string DriftReason { get; private set; } = "";

        /// <summary>Other people's ops (or the master's order) changed Live: the ids of what changed, and whether blocks came or went. The view redraws and keeps its selection by id.</summary>
        internal event Action<IReadOnlyCollection<string>, bool> Changed;
        /// <summary>Live was replaced by a snapshot (the blocks in it are new objects: anything that holds one must look it up again by id).</summary>
        internal event Action Resynced;
        /// <summary>The master dropped some of this editor's ops, with its reasons (for a short message): the change is gone from Live.</summary>
        internal event Action<IReadOnlyList<string>> Refused;

        /// <summary>`live` is the program as the editor has it when the session opens (the same state the master has, from its snapshot).</summary>
        internal Client(Program live, string id, int rev = 0)
        {
            Live = live; Id = id; Rev = rev;
            Uids.Ensure(Live);
            _base = Live.Copy();
            _committed = Live.Copy();
        }

        // ---- sending ----

        /// <summary>
        /// Turns what changed in `Live` since the last commit into ops. Returns null when nothing did (or while a snapshot has been asked for:
        /// the changes wait, and go on top of it). The ops are kept as pending until the master's batch with the same number comes back.
        /// New blocks get their ids here.
        /// </summary>
        internal Commit CommitChanges() => CommitCore(true);

        private Commit CommitCore(bool record)
        {
            if (_resyncAsked) return null;   // the master would do it after the snapshot was made, and neither copy would know
            List<Op> ops = Diff.Compute(_committed, Live, Id);
            if (ops.Count == 0) return null;
            var c = new Commit { Seq = ++_seq, Ops = ops, Inverse = Diff.Compute(Live, _committed, Id, expect: true) };
            _committed = Live.Copy();
            _pending.Add(c);
            if (record) { _undo.Add(new Entry { Inverse = c.Inverse }); _redo.Clear(); if (_undo.Count > 100) _undo.RemoveAt(0); }
            return c;
        }

        /// <summary>The edited program, if the editor works on a copy rather than changing Live: its blocks are copied into Live in place (ids kept), then committed.</summary>
        internal Commit Commit(Program edited)
        {
            if (!ReferenceEquals(edited, Live))
            {
                Uids.Ensure(edited, Uids.All(_committed));
                Replace(Live, edited);
            }
            return CommitCore(true);
        }

        // ---- undo and redo of this editor's own work ----

        /// <summary>Undoes the editor's last commit as a new commit (only what is still as it left it: someone else's work is never undone). Null when there is nothing to do.</summary>
        internal Commit Undo()
        {
            while (_undo.Count > 0)
            {
                Entry e = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
                foreach (Op op in e.Inverse) Ops.Apply(Live, op);
                Commit c = CommitCore(false);
                if (c == null) continue;   // nothing was left to undo: try the one before
                _redo.Add(new Entry { Inverse = c.Inverse });
                return c;
            }
            return null;
        }

        internal Commit Redo()
        {
            while (_redo.Count > 0)
            {
                Entry e = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
                foreach (Op op in e.Inverse) Ops.Apply(Live, op);
                Commit c = CommitCore(false);
                if (c == null) continue;
                _undo.Add(new Entry { Inverse = c.Inverse });
                return c;
            }
            return null;
        }

        // ---- receiving ----

        /// <summary>A batch from the master (every batch, in the order they came, including the editor's own: those are the acknowledgements).</summary>
        internal void OnBatch(Batch b)
        {
            if (b == null || NeedsResync) return;
            bool mine = b.By == Id;
            if (b.Ops.Count > 0 && b.RevFrom != Rev + 1) { Drifted("missed"); return; }   // a batch went missing

            // the master's state moves first, exactly as the master moved
            foreach (Op op in b.Ops) Ops.Apply(_base, op);
            if (b.Ops.Count > 0) { Ops.Gc(_base); Rev = b.RevTo; }
            if (b.Hash.Length > 0 && Canon.Hash(_base) != b.Hash) { Drifted("hash"); return; }

            if (mine)
            {
                int i = _pending.FindIndex(c => c.Seq == b.Seq);
                if (i >= 0) _pending.RemoveRange(0, i + 1);   // (batches of one editor come back in order: anything before it is done too)
                if (b.Dropped > 0 && b.Reasons.Count > 0) Refused?.Invoke(b.Reasons);
                // when everything sent is acknowledged, the committed copy must be the master's: if not, an op was dropped or placed differently
                if ((_pending.Count == 0 && !Canon.Equal(_committed, _base)) || b.Dropped > 0) Rebase(new List<Op>());
                return;
            }
            if (b.Ops.Count == 0) return;
            if (_pending.Count == 0 && !HasUncommitted)
            {
                // nothing of ours in the way: the other editor's ops go straight into Live (and the committed copy, which is the master's)
                var changed = new HashSet<string>(); bool structural = false;
                foreach (Op op in b.Ops) { Ops.Apply(_committed, op); Ops.Apply(Live, op); Note(op, changed, ref structural); }
                Ops.Gc(_committed); Ops.Gc(Live);
                if (changed.Count > 0) Changed?.Invoke(changed, structural);
            }
            else Rebase(b.Ops);
        }

        /// <summary>
        /// The master's state, then this editor's sent ops, then what it has done since: that is what Live must be. Built on a copy, then
        /// Live is brought to it by a diff done in place (a block in both stays the same object).
        /// </summary>
        private void Rebase(List<Op> remote)
        {
            Rebases++;
            List<Op> unsent = Diff.Compute(_committed, Live, Id);       // what the editor did since the last commit (this also gives new blocks their ids)
            Program target = _base.Copy();
            foreach (Commit c in _pending) foreach (Op op in c.Ops) Ops.Apply(target, op);
            Ops.Gc(target);
            Program committed = target.Copy();
            foreach (Op op in unsent) Ops.Apply(target, op);
            Ops.Gc(target);
            List<Op> toLive = Diff.Compute(Live, target, "");
            var changed = new HashSet<string>(); bool structural = false;
            foreach (Op op in remote) Note(op, changed, ref structural);
            foreach (Op op in toLive) { Ops.Apply(Live, op); Note(op, changed, ref structural); }
            Ops.Gc(Live);
            _committed = committed;
            if (changed.Count > 0) Changed?.Invoke(changed, structural);
        }

        private static void Note(Op op, HashSet<string> changed, ref bool structural)
        {
            foreach (string t in op.Targets()) if (!string.IsNullOrEmpty(t)) changed.Add(t);
            switch (op.Kind)
            {
                case OpKind.Insert: foreach (JObject j in op.Blocks) AddIds(j, changed); structural = true; break;
                case OpKind.AddTop: AddIds(op.TopJson, changed); structural = true; break;
                case OpKind.Remove: case OpKind.RemoveTop: case OpKind.SetInput: case OpKind.Move: structural = true; break;
            }
        }

        private static void AddIds(JToken j, HashSet<string> ids)
        {
            if (j == null) return;
            foreach (JToken t in j.SelectTokens("..u")) if (t.Type == JTokenType.String) ids.Add((string)t);
        }

        /// <summary>The master's regular "this is where I am": a copy that is behind (a batch never arrived) or whose master state differs asks for a snapshot.</summary>
        internal void OnHeartbeat(int rev, string hash)
        {
            if (NeedsResync) return;
            if (rev > Rev) Drifted("missed");
            else if (rev == Rev && hash != null && hash.Length > 0 && Canon.Hash(_base) != hash) Drifted("hash");
        }

        /// <summary>The master's whole state (a join, or the answer to a request for one): replaces everything, then what the editor did meanwhile goes on top.</summary>
        internal void OnSnapshot(JObject snapshot)
        {
            Program p = Program.FromJson(((JObject)snapshot["program"]).ToString(), out string error);
            if (p == null) return;
            // what the editor did since the last commit (and could not send while it waited) is kept: it goes on top of the snapshot
            List<Op> mine = Diff.Compute(_committed, Live, Id);
            Replace(Live, p);
            Rev = snapshot.Value<int?>("rev") ?? 0;
            _base = Live.Copy();
            _committed = Live.Copy();
            _pending.Clear(); _undo.Clear(); _redo.Clear();
            NeedsResync = false; _resyncAsked = false;
            foreach (Op op in mine) Ops.Apply(Live, op);   // (what no longer fits, because someone removed its target, is lost)
            Ops.Gc(Live);
            Resynced?.Invoke();
        }

        /// <summary>True once, when a snapshot should be asked for. (Commit what is waiting first, so it reaches the master before the request.)</summary>
        internal bool TakeResyncRequest()
        {
            if (!NeedsResync || _resyncAsked) return false;
            _resyncAsked = true; Resyncs++;
            return true;
        }

        private void Drifted(string why) { NeedsResync = true; DriftReason = why; }

        /// <summary>Puts a program's contents into another object (the same one the editor holds), keeping that object.</summary>
        private static void Replace(Program into, Program from)
        {
            into.Name = from.Name; into.About = from.About;
            into.Vars.Clear(); into.Vars.AddRange(from.Vars);
            into.Tops.Clear(); into.Tops.AddRange(from.Tops);
        }
    }
}
