using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ClaudeTools
{
    /// <summary>
    /// Just enough of the zip format to take a few files out of a large zip without downloading all of it: read the end of the file (the
    /// central directory says where each file is), then fetch only the byte ranges of the files wanted. Mods with big asset bundles are
    /// 100+ MB, while their DLLs are a few hundred KB.
    /// </summary>
    internal static class Zip
    {
        public class Entry
        {
            public string Name;
            public int Method;          // 0 stored, 8 deflate
            public long Compressed, Size, LocalOffset;
        }

        /// <summary>
        /// Where the central directory is, from the last bytes of the zip (<paramref name="tail"/> starts at <paramref name="tailStart"/>).
        /// False if the end record is not in the tail.
        /// </summary>
        public static bool FindDirectory(byte[] tail, long tailStart, out long offset, out long size)
        {
            offset = size = 0;
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (U32(tail, i) != 0x06054b50) continue;
                size = U32(tail, i + 12);
                offset = U32(tail, i + 16);
                if (offset == 0xFFFFFFFF || size == 0xFFFFFFFF) return Zip64(tail, i, out offset, out size);
                return true;
            }
            return false;
        }

        // A zip64 end record sits just before the locator that sits before the normal end record.
        private static bool Zip64(byte[] tail, int end, out long offset, out long size)
        {
            offset = size = 0;
            int locator = end - 20;
            if (locator < 0 || U32(tail, locator) != 0x07064b50) return false;
            for (int i = locator - 56; i >= 0; i--)
            {
                if (U32(tail, i) != 0x06064b50) continue;
                size = (long)U64(tail, i + 40);
                offset = (long)U64(tail, i + 48);
                return true;
            }
            return false;
        }

        /// <summary>The files listed in a central directory.</summary>
        public static List<Entry> Entries(byte[] dir)
        {
            var list = new List<Entry>();
            int p = 0;
            while (p + 46 <= dir.Length && U32(dir, p) == 0x02014b50)
            {
                int nameLen = U16(dir, p + 28), extraLen = U16(dir, p + 30), commentLen = U16(dir, p + 32);
                var e = new Entry
                {
                    Method = U16(dir, p + 10), Compressed = U32(dir, p + 20), Size = U32(dir, p + 24), LocalOffset = U32(dir, p + 42),
                    Name = Encoding.UTF8.GetString(dir, p + 46, nameLen).Replace('\\', '/'),
                };
                if (e.Compressed == 0xFFFFFFFF || e.Size == 0xFFFFFFFF || e.LocalOffset == 0xFFFFFFFF) Extra64(dir, p + 46 + nameLen, extraLen, e);
                list.Add(e);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return list;
        }

        private static void Extra64(byte[] d, int at, int len, Entry e)
        {
            for (int p = at; p + 4 <= at + len;)
            {
                int id = U16(d, p), size = U16(d, p + 2), q = p + 4;
                if (id == 1)
                {
                    if (e.Size == 0xFFFFFFFF) { e.Size = (long)U64(d, q); q += 8; }
                    if (e.Compressed == 0xFFFFFFFF) { e.Compressed = (long)U64(d, q); q += 8; }
                    if (e.LocalOffset == 0xFFFFFFFF) e.LocalOffset = (long)U64(d, q);
                    return;
                }
                p += 4 + size;
            }
        }

        /// <summary>How many bytes to fetch from the entry's local offset to be sure of having all of it (its local header may be longer).</summary>
        public static long Span(Entry e) => 30 + 1024 + e.Name.Length * 2 + e.Compressed;

        /// <summary>
        /// The file's contents, from bytes fetched at its local offset. Null if they don't reach the end of the data (fetch more:
        /// <paramref name="need"/> says how many).
        /// </summary>
        public static byte[] Extract(byte[] chunk, Entry e, out long need)
        {
            need = 0;
            if (chunk.Length < 30 || U32(chunk, 0) != 0x04034b50) throw new InvalidDataException("no file header at " + e.LocalOffset);
            long start = 30 + U16(chunk, 26) + U16(chunk, 28);
            need = start + e.Compressed;
            if (chunk.Length < need) return null;
            if (e.Method == 0)
            {
                var stored = new byte[e.Size];
                Array.Copy(chunk, start, stored, 0, e.Size);
                return stored;
            }
            if (e.Method != 8) throw new InvalidDataException($"{e.Name}: compression method {e.Method} is not supported");
            using (var input = new MemoryStream(chunk, (int)start, (int)e.Compressed))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream((int)Math.Max(0, e.Size)))
            {
                inflate.CopyTo(output);
                return output.ToArray();
            }
        }

        private static int U16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
        private static long U32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
        private static ulong U64(byte[] b, int i) => (ulong)U32(b, i) | ((ulong)U32(b, i + 4) << 32);
    }
}
