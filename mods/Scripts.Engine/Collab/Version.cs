using System;
using System.Security.Cryptography;
using System.Text;

namespace Scripts.Collab
{
    internal enum SaveCheck { Ok, Stale }

    /// <summary>
    /// A save that began from an older version is refused instead of silently winning (docs/scripting-collab.md, "stale saves"): for text
    /// scripts and Paste, and for any write that is not a live co-edit. A hash of the text (line ends and the last newline ignored)
    /// says which version a save began from.
    /// </summary>
    internal static class Version
    {
        internal static string Hash(string text)
        {
            string norm = (text ?? "").Replace("\r\n", "\n").TrimEnd();
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(norm))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Ok when nothing has changed since the save began (or it did not say where it began: an old copy of the editor).</summary>
        internal static SaveCheck Check(string currentHash, string baseHash) =>
            string.IsNullOrEmpty(baseHash) || string.Equals(currentHash, baseHash, StringComparison.OrdinalIgnoreCase) ? SaveCheck.Ok : SaveCheck.Stale;

        /// <summary>The words for the refusal; `lastBy` is who saved last (the caller keeps it) or null.</summary>
        internal static string StaleMessage(string lastBy) =>
            (string.IsNullOrWhiteSpace(lastBy) ? "Someone" : lastBy) + " changed this script since you opened it. Reload it to see their version, then make your change again.";
    }
}
