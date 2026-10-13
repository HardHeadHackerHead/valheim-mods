using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>
    /// What changed between two states of one program, as ops. The editor keeps changing its program the way it always has (BlockView
    /// moves, inserts and removes blocks in the tree); a commit hands the before and the after to Diff and gets the ops to send.
    ///
    /// By id: a block that is in both and still in place (in a list, the longest run of blocks that kept their order) is compared inside;
    /// one that moved to another place in the same script is MOVED (a move op: the block itself goes, with everything inside it, so
    /// somebody typing in it meanwhile loses nothing); a new one is inserted with the ids it was given. Order of the ops: moves, then
    /// removals, then changes and inserts, then new stacks, so an op never needs something a later op makes. The order of stacks is not part
    /// of a program's identity.
    ///
    /// The result is checked before it is returned: the ops are done to a copy of the old program and must give the new one. If they do
    /// not (a move that would put a block inside itself, say) the diff is made again without moves, and if that fails too the stacks are
    /// replaced whole. So Apply(old, Diff(old, new)) is the same as new always, not only in the tests.
    /// </summary>
    internal static class Diff
    {
        private sealed class Ctx
        {
            internal bool UseMoves, Expect;
            internal readonly HashSet<string> Moved = new HashSet<string>();
            internal readonly Dictionary<string, Block> OldInLists = new Dictionary<string, Block>();   // every block of the old program that sits in a list (not in a socket)
            internal readonly List<Op> Moves = new List<Op>(), Removals = new List<Op>(), Updates = new List<Op>();
        }

        /// <summary>
        /// The ops that turn `old` into `neu`. `neu` gets ids for anything new first (never an id that `old` has had). With expect, a field
        /// change carries the old value, so the op does nothing if somebody else has changed the field since (an undo uses this).
        /// </summary>
        internal static List<Op> Compute(Program old, Program neu, string by = "", bool expect = false)
        {
            Uids.Ensure(neu, Uids.All(old));
            List<Op> ops = Build(old, neu, expect, true);
            if (!Replays(old, neu, ops)) ops = Build(old, neu, expect, false);
            if (!Replays(old, neu, ops)) ops = Replace(old, neu);
            if (by.Length > 0) foreach (Op op in ops) op.By = by;
            return ops;
        }

        /// <summary>Whether the ops, done to a copy of `old`, give `neu`, each one taking full effect.</summary>
        internal static bool Replays(Program old, Program neu, List<Op> ops)
        {
            if (ops.Count == 0) return Canon.Equal(old, neu);
            Program scratch = old.Copy();
            foreach (Op op in ops)
                if (Ops.Apply(scratch, op).Status != ApplyStatus.Applied) return false;
            Ops.Gc(scratch);
            return Canon.Equal(scratch, neu);
        }

        private static List<Op> Replace(Program old, Program neu)
        {
            var ops = new List<Op>();
            foreach (Program.Top t in old.Tops) ops.Add(Op.RemoveTop(t.Uid));
            if (old.Name != neu.Name || old.About != neu.About) ops.Add(Op.Meta(neu.Name, neu.About));
            if (!old.Vars.SequenceEqual(neu.Vars)) ops.Add(Op.SetVars(neu.Vars));
            foreach (Program.Top t in neu.Tops) if (t.Chain.Count > 0) ops.Add(Op.AddTop(Program.TopJson(t)));
            return ops;
        }

        private static List<Op> Build(Program old, Program neu, bool expect, bool moves)
        {
            var c = new Ctx { UseMoves = moves, Expect = expect };
            var adds = new List<Op>();
            var oldTops = old.Tops.Where(t => t.Chain.Count > 0).ToDictionary(t => t.Uid);
            var newTops = neu.Tops.Where(t => t.Chain.Count > 0).ToDictionary(t => t.Uid);

            if (moves)
            {
                foreach (Program.Top t in oldTops.Values) IndexLists(t.Chain, c.OldInLists);
                foreach (Program.Top n in neu.Tops)
                    if (oldTops.TryGetValue(n.Uid, out Program.Top o)) Collect(c, o.Chain, n.Chain);
            }

            if (old.Name != neu.Name || old.About != neu.About) c.Updates.Add(Op.Meta(neu.Name, neu.About));
            if (!old.Vars.SequenceEqual(neu.Vars)) c.Updates.Add(Op.SetVars(neu.Vars));
            foreach (Program.Top t in old.Tops) if (!newTops.ContainsKey(t.Uid)) c.Removals.Add(Op.RemoveTop(t.Uid));
            foreach (Program.Top n in neu.Tops)
            {
                if (n.Chain.Count == 0) continue;
                if (!oldTops.TryGetValue(n.Uid, out Program.Top o)) { adds.Add(Op.AddTop(Program.TopJson(n))); continue; }
                if (Math.Round(o.X) != Math.Round(n.X) || Math.Round(o.Y) != Math.Round(n.Y)) c.Updates.Add(Op.MoveTop(n.Uid, (float)Math.Round(n.X), (float)Math.Round(n.Y)));
                if (o.Collapsed != n.Collapsed) c.Updates.Add(Op.Collapse(n.Uid, n.Collapsed));
                List(c, o.Chain, n.Chain, n.Uid, null, null);
            }

            var all = new List<Op>(c.Moves.Count + c.Removals.Count + c.Updates.Count + adds.Count);
            all.AddRange(c.Moves); all.AddRange(c.Removals); all.AddRange(c.Updates); all.AddRange(adds);
            return all;
        }

        // ---- pass 1: which blocks are moved (a block of a list that is in another list now, among lists that exist on both sides) ----

        private static void IndexLists(List<Block> list, Dictionary<string, Block> into)
        {
            foreach (Block b in list)
            {
                into[b.Uid] = b;
                foreach (Block child in b.Inputs.Values) IndexInside(child, into);
                foreach (List<Block> body in b.Bodies.Values) IndexLists(body, into);
            }
        }

        private static void IndexInside(Block b, Dictionary<string, Block> into)
        {
            foreach (Block child in b.Inputs.Values) IndexInside(child, into);
            foreach (List<Block> body in b.Bodies.Values) IndexLists(body, into);
        }

        private static void Collect(Ctx c, List<Block> ol, List<Block> nl)
        {
            if (ol.Count == 0 && nl.Count == 0) return;
            Lcs(ol, nl, out bool[] keepOld, out bool[] keepNew);
            var keptOld = new List<Block>(); for (int i = 0; i < ol.Count; i++) if (keepOld[i]) keptOld.Add(ol[i]);
            int k = 0;
            for (int j = 0; j < nl.Count; j++)
            {
                Block n = nl[j];
                if (keepNew[j]) { CollectInside(c, keptOld[k++], n); continue; }
                if (c.OldInLists.TryGetValue(n.Uid, out Block ob) && ob.Type == n.Type && c.Moved.Add(n.Uid)) CollectInside(c, ob, n);
            }
        }

        private static void CollectInside(Ctx c, Block o, Block n)
        {
            foreach (string key in Block.BodyKeys) Collect(c, Body(o, key), Body(n, key));
            foreach (KeyValuePair<string, Block> kv in n.Inputs)
                if (o.Inputs.TryGetValue(kv.Key, out Block oc) && oc.Uid == kv.Value.Uid && oc.Type == kv.Value.Type) CollectInside(c, oc, kv.Value);
        }

        private static List<Block> Body(Block b, string key) => b.Bodies.TryGetValue(key, out List<Block> l) ? l : new List<Block>();

        // ---- pass 2: the ops ----

        /// <summary>The blocks of one list (a stack's chain or a body): which stay, which go, which come in, which are inside changed.</summary>
        private static void List(Ctx c, List<Block> oldList, List<Block> newList, string topUid, string ownerUid, string key)
        {
            if (oldList.Count == 0 && newList.Count == 0) return;
            Lcs(oldList, newList, out bool[] keepOld, out bool[] keepNew);
            for (int i = 0; i < oldList.Count; i++)
                if (!keepOld[i] && !c.Moved.Contains(oldList[i].Uid)) c.Removals.Add(Op.Remove(oldList[i].Uid));
            var keptOld = new List<Block>(); for (int i = 0; i < oldList.Count; i++) if (keepOld[i]) keptOld.Add(oldList[i]);
            int k = 0;
            string existing = null; bool atStart = true;          // the last block that is in the list by the time this list's moves and inserts happen
            var run = new List<JObject>(); string runAfter = null; bool runAtStart = true;
            for (int j = 0; j < newList.Count; j++)
            {
                Block n = newList[j];
                string next = j + 1 < newList.Count ? newList[j + 1].Uid : null;
                if (keepNew[j])
                {
                    Flush(c, run, topUid, ownerUid, key, runAfter, runAtStart, n.Uid); run = new List<JObject>();
                    Inside(c, keptOld[k++], n);
                    existing = n.Uid; atStart = false;
                }
                else if (c.Moved.Contains(n.Uid))
                {
                    Flush(c, run, topUid, ownerUid, key, runAfter, runAtStart, n.Uid); run = new List<JObject>();
                    c.Moves.Add(ownerUid == null ? Op.MoveToTop(n.Uid, topUid, existing, atStart, next) : Op.MoveToBody(n.Uid, ownerUid, key, existing, atStart, next));
                    Inside(c, c.OldInLists[n.Uid], n);
                    existing = n.Uid; atStart = false;
                }
                else
                {
                    if (run.Count == 0) { runAfter = existing; runAtStart = atStart; }
                    run.Add(n.ToJson());
                }
            }
            Flush(c, run, topUid, ownerUid, key, runAfter, runAtStart, null);
        }

        private static void Flush(Ctx c, List<JObject> run, string topUid, string ownerUid, string key, string after, bool atStart, string before)
        {
            if (run.Count == 0) return;
            c.Updates.Add(ownerUid == null ? Op.InsertInTop(topUid, after, atStart, run, before) : Op.InsertInBody(ownerUid, key, after, atStart, run, before));
        }

        /// <summary>Two blocks with the same id and type that stayed (or moved): what changed inside.</summary>
        private static void Inside(Ctx c, Block o, Block n)
        {
            foreach (KeyValuePair<string, string> kv in n.Fields)
                if (!o.Fields.TryGetValue(kv.Key, out string was) || was != kv.Value)
                {
                    Op op = Op.SetField(n.Uid, kv.Key, kv.Value);
                    if (c.Expect) { op.HasExpect = true; op.Expect = o.Fields.TryGetValue(kv.Key, out string e) ? e : null; }
                    c.Updates.Add(op);
                }
            foreach (KeyValuePair<string, string> kv in o.Fields)
                if (!n.Fields.ContainsKey(kv.Key))
                {
                    Op op = Op.SetField(n.Uid, kv.Key, null);
                    if (c.Expect) { op.HasExpect = true; op.Expect = kv.Value; }
                    c.Updates.Add(op);
                }
            foreach (string key in o.Inputs.Keys.Union(n.Inputs.Keys).ToList())
            {
                bool had = o.Inputs.TryGetValue(key, out Block oc), has = n.Inputs.TryGetValue(key, out Block nc);
                if (had && has && oc.Uid == nc.Uid && oc.Type == nc.Type) { Inside(c, oc, nc); continue; }
                if (had) c.Removals.Add(Op.Remove(oc.Uid));
                if (has) c.Updates.Add(Op.SetInput(n.Uid, key, nc.ToJson()));
            }
            foreach (string key in Block.BodyKeys) List(c, Body(o, key), Body(n, key), null, n.Uid, key);
        }

        /// <summary>The longest run of blocks that kept their order (same id and type): those stay, the rest of the old go, the rest of the new come in.</summary>
        private static void Lcs(List<Block> a, List<Block> b, out bool[] keepA, out bool[] keepB)
        {
            int n = a.Count, m = b.Count;
            keepA = new bool[n]; keepB = new bool[m];
            // trim what both ends share (the common case: one change in the middle)
            int lo = 0;
            while (lo < n && lo < m && Same(a[lo], b[lo])) { keepA[lo] = true; keepB[lo] = true; lo++; }
            int hiA = n, hiB = m;
            while (hiA > lo && hiB > lo && Same(a[hiA - 1], b[hiB - 1])) { hiA--; hiB--; keepA[hiA] = true; keepB[hiB] = true; }
            int x = hiA - lo, y = hiB - lo;
            if (x == 0 || y == 0) return;
            var dp = new int[x + 1, y + 1];
            for (int i = x - 1; i >= 0; i--)
                for (int j = y - 1; j >= 0; j--)
                    dp[i, j] = Same(a[lo + i], b[lo + j]) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            int p = 0, q = 0;
            while (p < x && q < y)
            {
                if (Same(a[lo + p], b[lo + q])) { keepA[lo + p] = true; keepB[lo + q] = true; p++; q++; }
                else if (dp[p + 1, q] >= dp[p, q + 1]) p++;
                else q++;
            }
        }

        private static bool Same(Block a, Block b) => a.Uid == b.Uid && a.Type == b.Type;
    }
}
