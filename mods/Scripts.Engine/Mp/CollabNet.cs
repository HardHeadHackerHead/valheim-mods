using System;
using System.Collections.Generic;
using Scripts.Blocks;

namespace Scripts.Mp
{
    /// <summary>
    /// The message kinds of live co-editing (docs/scripting-collab.md, "wiring"). They travel on the multiplayer wire (Wire.SendToServer /
    /// SendToPeer) and, for the game that runs the scripts, straight between its own editor and its own CollabServer. Every message is a
    /// JSON object that names the script (`"f"`): one connection can edit several scripts.
    /// </summary>
    internal static class Co
    {
        // editor to server
        internal const string Open = "co.open";         // {f}                         I want to edit this script
        internal const string Ops = "co.ops";           // {f, seq, ops[]}             what I changed (the commit of a gesture)
        internal const string Pres = "co.pres";         // {f, page, sel, x, y}        where I am; also my "still here" (sent at least every 5 s)
        internal const string Grab = "co.grab";         // {f, uids[]}                 I am dragging these: nobody else may change them
        internal const string Release = "co.release";   // {f, uids[]?}                I let go
        internal const string Resync = "co.resync";     // {f}                         I missed something: send me everything
        internal const string Save = "co.save";         // {f}                         write the script now (before it runs)
        internal const string Close = "co.close";       // {f}                         I am leaving the script

        // server to editor ("co.pres" is also the server's list of everyone's presence)
        internal const string Snap = "co.snap";         // {f, you, rev, program, hash, editors[]}
        internal const string Batch = "co.batch";       // {f, from, to, by, seq, hash, ops[], dropped?, why[]?}   the ops that took effect (the sender's acknowledgement too)
        internal const string Hb = "co.hb";             // {f, rev, hash}                where the master is, so a copy that missed a batch finds out
        internal const string Refused = "co.refused";   // {f, why}                      a message to show (a grab that was refused, a limit)
        internal const string Closed = "co.closed";     // {f, why}                      the session is over for you (not allowed, removed, the server stopped)

        internal const int MaxMessage = 256 * 1024;     // characters; a longer message is dropped
        internal const int MaxOpsPerMessage = 2000;
        internal const int MaxGrab = 50;
        internal const double OpsPerSecond = 20, PresPerSecond = 8, MiscPerSecond = 10;
        internal const double HeartbeatSeconds = 2.0, PresenceBroadcastSeconds = 0.25, KeepAliveSeconds = 5.0;

        internal static bool IsCo(string kind) => kind != null && kind.StartsWith("co.", StringComparison.Ordinal);
    }

    /// <summary>Who an editor is, on the server: the game's own editor is "host", a remote one is "p" plus its connection's id (never taken from a message).</summary>
    internal static class Editors
    {
        internal const string Host = "host";
        internal static string Peer(long uid) => "p" + uid;
        internal static bool IsPeer(string editor, out long uid)
        {
            uid = 0;
            return editor != null && editor.Length > 1 && editor[0] == 'p' && long.TryParse(editor.Substring(1), out uid);
        }
    }

    /// <summary>What the server needs from the game: who is allowed, who is who, the way to send, and the disk. The game and the tests each give one.</summary>
    internal interface ICollabHost
    {
        DateTime Now { get; }
        bool IsAdmin(string editor);
        string NameOf(string editor);
        void Send(string editor, string kind, string json);
        /// <summary>Whether a block script by this name exists.</summary>
        bool Exists(string file);
        Program Load(string file, out string error);
        /// <summary>Compiles and writes the blocks and the code; "" when it went well, else why not.</summary>
        string Save(string file, Program program);
        void Audit(string line);
        /// <summary>The script was written by these editors (the stale-save record).</summary>
        void Wrote(string file, string who);
    }
}
