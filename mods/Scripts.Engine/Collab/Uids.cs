using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>
    /// The stable ids that let several editors point at the same block (docs/scripting-collab.md, "ids"): eight characters of base 36 from
    /// a cryptographic random source, never a counter (two editors adding blocks at once must not both make "7"). A program that nobody
    /// has co-edited has none (and its JSON has no "u"); a session gives every block and stack one when it opens.
    /// </summary>
    internal static class Uids
    {
        private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
        private static readonly RandomNumberGenerator Crypto = RandomNumberGenerator.Create();
        private static readonly object Gate = new object();

        /// <summary>Where the random bytes come from. Tests put a seeded source here so a failure can be repeated.</summary>
        internal static Func<int, byte[]> Bytes = n => { var b = new byte[n]; lock (Gate) Crypto.GetBytes(b); return b; };

        /// <summary>A new id that is not in `used` (which it joins).</summary>
        internal static string New(HashSet<string> used)
        {
            for (int tries = 0; tries < 1000; tries++)
            {
                byte[] raw = Bytes(16);
                var chars = new char[8];
                int n = 0;
                for (int i = 0; i < raw.Length && n < 8; i++)
                    if (raw[i] < 252) chars[n++] = Alphabet[raw[i] % 36];   // (252 = 7 x 36: no modulo bias)
                if (n < 8) continue;
                string id = new string(chars);
                if (used.Add(id)) return id;
            }
            throw new InvalidOperationException("could not make an id");
        }

        /// <summary>An id read from a file or the wire: 1 to 16 characters of 0-9 and a-z, else "" (none).</summary>
        internal static string Clean(string u) => Naming.Uid(u);

        internal static bool Valid(string u) => Clean(u).Length > 0;

        /// <summary>Every id in the program (stacks and blocks, inside bodies and sockets too).</summary>
        internal static IEnumerable<string> All(Program p)
        {
            foreach (Program.Top t in p.Tops)
            {
                if (t.Uid.Length > 0) yield return t.Uid;
                foreach (Block root in t.Chain) foreach (Block b in root.All()) if (b.Uid.Length > 0) yield return b.Uid;
            }
        }

        /// <summary>
        /// Gives an id to every stack and block that has none, and a new one to the second and later holders of an id (a pasted copy that
        /// kept its ids). `avoid` are ids that must not be reused (the ones in the last committed state). Returns how many were given.
        /// </summary>
        internal static int Ensure(Program p, IEnumerable<string> avoid = null)
        {
            var used = new HashSet<string>();
            if (avoid != null) foreach (string a in avoid) used.Add(a);
            var seen = new HashSet<string>();
            int given = 0;
            foreach (Program.Top t in p.Tops)
            {
                if (t.Uid.Length == 0 || !seen.Add(t.Uid)) { t.Uid = New(used); seen.Add(t.Uid); given++; }
                else used.Add(t.Uid);
                foreach (Block root in t.Chain)
                    foreach (Block b in root.All())
                    {
                        if (b.Uid.Length == 0 || !seen.Add(b.Uid)) { b.Uid = New(used); seen.Add(b.Uid); given++; }
                        else used.Add(b.Uid);
                    }
            }
            return given;
        }
    }
}
