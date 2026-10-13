using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>Where a block is: the stack, the list or socket that holds it, and how deep it is (a stack's own blocks are depth 0).</summary>
    internal sealed class Loc
    {
        internal Program.Top Top;
        internal List<Block> List;      // the chain or body list that holds it; null when it is plugged into a socket
        internal int Index;
        internal Block Owner;           // the block whose body or socket holds it; null when it is in the stack's own chain
        internal string Key = "";       // the body key or the socket key
        internal bool IsInput;
        internal Block Block;
        internal int Depth;
    }

    /// <summary>Finding things by id. Each call walks the program: a program is a few hundred blocks, and a walk is cheaper than keeping an index right.</summary>
    internal static class Tree
    {
        /// <summary>The deepest a block may sit (the same bound Block.FromJson keeps, so a saved program always loads back whole).</summary>
        internal static int MaxDepth => Program.MaxDepth + 4;

        internal static Program.Top FindTop(Program p, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            foreach (Program.Top t in p.Tops) if (t.Uid == uid) return t;
            return null;
        }

        internal static bool Find(Program p, string uid, out Loc loc)
        {
            loc = null;
            if (string.IsNullOrEmpty(uid)) return false;
            foreach (Program.Top t in p.Tops)
                for (int i = 0; i < t.Chain.Count; i++)
                {
                    Block b = t.Chain[i];
                    if (b.Uid == uid) { loc = new Loc { Top = t, List = t.Chain, Index = i, Block = b, Depth = 0 }; return true; }
                    if (Search(b, 0, uid, t, out loc)) return true;
                }
            return false;
        }

        private static bool Search(Block b, int depth, string uid, Program.Top top, out Loc loc)
        {
            foreach (KeyValuePair<string, Block> kv in b.Inputs)
            {
                Block c = kv.Value;
                if (c.Uid == uid) { loc = new Loc { Top = top, Owner = b, Key = kv.Key, IsInput = true, Block = c, Depth = depth + 1 }; return true; }
                if (Search(c, depth + 1, uid, top, out loc)) return true;
            }
            foreach (KeyValuePair<string, List<Block>> kv in b.Bodies)
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    Block c = kv.Value[i];
                    if (c.Uid == uid) { loc = new Loc { Top = top, List = kv.Value, Index = i, Owner = b, Key = kv.Key, Block = c, Depth = depth + 1 }; return true; }
                    if (Search(c, depth + 1, uid, top, out loc)) return true;
                }
            loc = null;
            return false;
        }

        /// <summary>The chain of ids above a block, nearest first, ending with its stack's id ("" when the program has none).</summary>
        internal static List<string> Ancestors(Program p, string uid)
        {
            var list = new List<string>();
            string cur = uid;
            for (int guard = 0; guard < 64; guard++)
            {
                if (!Find(p, cur, out Loc loc)) return list;
                if (loc.Owner != null) { list.Add(loc.Owner.Uid); cur = loc.Owner.Uid; }
                else { if (loc.Top.Uid.Length > 0) list.Add(loc.Top.Uid); return list; }
            }
            return list;
        }

        /// <summary>The ids of a block and everything inside it.</summary>
        internal static IEnumerable<string> UidsIn(Block root)
        {
            foreach (Block b in root.All()) yield return b.Uid;
        }

        /// <summary>How many levels are inside this block (a block with nothing inside is 0).</summary>
        internal static int SubDepth(Block b)
        {
            int d = 0;
            foreach (Block c in b.Inputs.Values) d = Math.Max(d, 1 + SubDepth(c));
            foreach (List<Block> list in b.Bodies.Values) foreach (Block c in list) d = Math.Max(d, 1 + SubDepth(c));
            return d;
        }

        internal static bool Has(Program p, string uid) => FindTop(p, uid) != null || Find(p, uid, out _);

        /// <summary>The block with this id, or null.</summary>
        internal static Block Get(Program p, string uid) => Find(p, uid, out Loc l) ? l.Block : null;

        /// <summary>Whether a program has every id it needs: none empty, none twice.</summary>
        internal static bool IdsOk(Program p)
        {
            var seen = new HashSet<string>();
            foreach (Program.Top t in p.Tops)
            {
                if (t.Uid.Length == 0 || !seen.Add(t.Uid)) return false;
                foreach (Block r in t.Chain) foreach (Block b in r.All()) if (b.Uid.Length == 0 || !seen.Add(b.Uid)) return false;
            }
            return true;
        }
    }
}
