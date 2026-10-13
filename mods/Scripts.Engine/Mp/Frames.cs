using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Scripts.Mp
{
    /// <summary>One message: a kind, an id that ties an answer to its question, and plain data.</summary>
    internal sealed class Frame
    {
        internal string K = "";
        internal long I;
        internal JObject D = new JObject();

        internal Frame() { }
        internal Frame(string kind, long id, JObject data) { K = kind; I = id; D = data ?? new JObject(); }

        internal string S(string key, string fallback = "") => D.TryGetValue(key, out JToken t) && t.Type == JTokenType.String ? (string)t : fallback;
        internal double N(string key, double fallback = 0.0) => D.TryGetValue(key, out JToken t) && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) ? (double)t : fallback;
        internal bool B(string key, bool fallback = false) => D.TryGetValue(key, out JToken t) && t.Type == JTokenType.Boolean ? (bool)t : fallback;
    }

    /// <summary>
    /// How a frame travels (docs/scripting-multiplayer.md section 2): JSON, gzipped over 1 KB, 256 KB at most, cut into parts of 60 KB for
    /// the connection and put together again on the other side. Nothing here trusts what it reads: depth, size, the kind's spelling and the
    /// number of parts a stranger may have half sent are all capped, and a bad message is a refusal with a reason, never an exception.
    /// </summary>
    internal static class Frames
    {
        internal const int MaxRaw = 256 * 1024, GzipFrom = 1024, PartSize = 60 * 1024, MaxParts = 8, MaxDepth = 16, MaxKind = 20;

        internal static byte[] Pack(Frame f)
        {
            string json = new JObject { ["k"] = f.K, ["i"] = f.I, ["d"] = f.D }.ToString(Formatting.None);
            byte[] raw = Encoding.UTF8.GetBytes(json);
            if (raw.Length > MaxRaw) throw new InvalidOperationException("a message of " + raw.Length + " bytes is over the " + MaxRaw + " limit");
            if (raw.Length < GzipFrom) return Concat(new byte[] { 0 }, raw);
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(1);
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        internal static bool TryUnpack(byte[] data, out Frame frame, out string why)
        {
            frame = null; why = "";
            try
            {
                if (data == null || data.Length < 2) { why = "empty"; return false; }
                byte[] raw;
                if (data[0] == 0) raw = Slice(data, 1, data.Length - 1);
                else if (data[0] == 1)
                {
                    using (var input = new MemoryStream(data, 1, data.Length - 1))
                    using (var gz = new GZipStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int n;
                        while ((n = gz.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + n > MaxRaw) { why = "too big when unpacked"; return false; }
                            output.Write(buffer, 0, n);
                        }
                        raw = output.ToArray();
                    }
                }
                else { why = "unknown packing"; return false; }
                if (raw.Length > MaxRaw) { why = "too big"; return false; }
                JObject o;
                using (var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(raw))) { MaxDepth = MaxDepth })
                    o = JObject.Load(reader);
                string kind = o["k"] != null && o["k"].Type == JTokenType.String ? (string)o["k"] : "";
                if (!ValidKind(kind)) { why = "bad kind"; return false; }
                long id = o["i"] != null && o["i"].Type == JTokenType.Integer ? (long)o["i"] : 0L;
                JObject d = o["d"] as JObject ?? new JObject();
                frame = new Frame(kind, id, d);
                return true;
            }
            catch (Exception e)
            {
                why = "unreadable: " + e.Message;
                return false;
            }
        }

        internal static bool ValidKind(string kind)
        {
            if (string.IsNullOrEmpty(kind) || kind.Length > MaxKind) return false;
            foreach (char c in kind) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '.' && c != '+' && c != '-') return false;
            return true;
        }

        // ---- parts ----

        /// <summary>A packed message cut into parts: each is [message number 2 bytes][index 1][count 1][the bytes]. One part for most messages.</summary>
        internal static List<byte[]> Split(byte[] packed, ushort number)
        {
            int count = Math.Max(1, (packed.Length + PartSize - 1) / PartSize);
            if (count > MaxParts) throw new InvalidOperationException("a message of " + packed.Length + " bytes needs " + count + " parts");
            var parts = new List<byte[]>();
            for (int i = 0; i < count; i++)
            {
                int from = i * PartSize, len = Math.Min(PartSize, packed.Length - from);
                var part = new byte[4 + len];
                part[0] = (byte)(number & 0xff); part[1] = (byte)(number >> 8); part[2] = (byte)i; part[3] = (byte)count;
                Buffer.BlockCopy(packed, from, part, 4, len);
                parts.Add(part);
            }
            return parts;
        }

        /// <summary>Puts parts back together, per sender. A sender may have at most 4 messages half arrived, each for 10 seconds.</summary>
        internal sealed class Reassembler
        {
            private sealed class Partial { public byte[][] Parts; public int Have; public double Started; public int Bytes; }
            private readonly Dictionary<string, Dictionary<ushort, Partial>> _by = new Dictionary<string, Dictionary<ushort, Partial>>();

            /// <summary>True when `whole` is a finished message. A false with an empty `why` just means more parts are to come.</summary>
            internal bool Add(string sender, byte[] part, double now, out byte[] whole, out string why)
            {
                whole = null; why = "";
                if (part == null || part.Length < 5) { why = "short part"; return false; }
                ushort number = (ushort)(part[0] | (part[1] << 8));
                int index = part[2], count = part[3];
                if (count < 1 || count > MaxParts || index >= count) { why = "bad part numbers"; return false; }
                if (count == 1) { whole = Slice(part, 4, part.Length - 4); return true; }
                if (!_by.TryGetValue(sender, out Dictionary<ushort, Partial> mine)) _by[sender] = mine = new Dictionary<ushort, Partial>();
                foreach (ushort old in new List<ushort>(mine.Keys)) if (now - mine[old].Started > 10.0) mine.Remove(old);
                if (!mine.TryGetValue(number, out Partial p))
                {
                    if (mine.Count >= 4) { why = "too many half-sent messages"; return false; }
                    mine[number] = p = new Partial { Parts = new byte[count][], Started = now };
                }
                if (p.Parts.Length != count) { mine.Remove(number); why = "the parts disagree"; return false; }
                if (p.Parts[index] == null) { p.Parts[index] = Slice(part, 4, part.Length - 4); p.Have++; p.Bytes += p.Parts[index].Length; }
                if (p.Bytes > MaxRaw + 1024) { mine.Remove(number); why = "too big"; return false; }
                if (p.Have < count) return false;
                mine.Remove(number);
                var all = new MemoryStream();
                foreach (byte[] b in p.Parts) all.Write(b, 0, b.Length);
                whole = all.ToArray();
                return true;
            }

            internal void Forget(string sender) { _by.Remove(sender); }
        }

        internal static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        internal static byte[] Slice(byte[] a, int from, int len)
        {
            var r = new byte[len];
            Buffer.BlockCopy(a, from, r, 0, len);
            return r;
        }
    }
}
