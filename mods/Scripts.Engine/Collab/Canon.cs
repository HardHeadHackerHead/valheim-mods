using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Scripts.Blocks;

namespace Scripts.Collab
{
    /// <summary>
    /// One program as one exact text, the same on every copy that holds the same blocks: keys sorted, stacks in id order (the order of stacks
    /// is not shared, see docs/scripting-collab.md), empty lists left out, places rounded as they are saved. Two copies are "the same" when
    /// this text is. Its SHA-1 rides on every batch the master sends, so a copy that has drifted finds out.
    /// </summary>
    internal static class Canon
    {
        internal static string Json(Program p)
        {
            var o = new JObject
            {
                ["name"] = p.Name, ["about"] = p.About,
                ["vars"] = new JArray(p.Vars.Cast<object>().ToArray()),
                ["tops"] = new JArray(p.Tops.Where(t => t.Chain.Count > 0).OrderBy(t => t.Uid, StringComparer.Ordinal).Select(t => TopOf(t)).Cast<object>().ToArray()),
            };
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static string Hash(Program p)
        {
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json(p)))).Replace("-", "").ToLowerInvariant();
        }

        internal static bool Equal(Program a, Program b) => Json(a) == Json(b);

        private static JObject TopOf(Program.Top t)
        {
            var o = new JObject { ["u"] = t.Uid, ["x"] = Math.Round(t.X), ["y"] = Math.Round(t.Y), ["c"] = t.Collapsed };
            o["blocks"] = new JArray(t.Chain.Select(b => BlockOf(b)).Cast<object>().ToArray());
            return o;
        }

        private static JObject BlockOf(Block b)
        {
            var o = new JObject { ["u"] = b.Uid, ["type"] = b.Type };
            if (b.Fields.Count > 0) o["f"] = new JObject(b.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new JProperty(kv.Key, kv.Value)));
            if (b.Inputs.Count > 0) o["in"] = new JObject(b.Inputs.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new JProperty(kv.Key, BlockOf(kv.Value))));
            foreach (KeyValuePair<string, List<Block>> kv in b.Bodies.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (kv.Value.Count > 0) o["b_" + kv.Key] = new JArray(kv.Value.Select(c => BlockOf(c)).Cast<object>().ToArray());
            return o;
        }
    }
}
