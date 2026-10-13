using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;
using Scripts.Editor;
using static Scripts.Editor.Words;

namespace Scripts.Mp
{
    /// <summary>
    /// Where the editor's scripts are. The editor never touches the host or the disk itself: it asks a backend, so the same screens work on the
    /// game that runs the scripts (Local), on a game connected to a server that has the mod (Remote: an admin edits the server's scripts, everyone
    /// else sees them read-only), and on one connected to a server that does not (Disconnected, which says so).
    /// Reads answer at once (a remote backend answers from its cache and refreshes it in the background: never a wait in a frame). Things that
    /// give an answer (start, write, new, delete, load, save) take a callback, called at once by Local and a moment later by Remote.
    /// </summary>
    internal interface IScriptsBackend
    {
        bool IsRemote { get; }
        /// <summary>"This game" or "Server: NAME".</summary>
        string Origin { get; }
        bool Connected { get; }
        /// <summary>May change what runs (start, stop, write, allow, new, delete).</summary>
        bool CanChange { get; }
        /// <summary>May read code, blocks and logs.</summary>
        bool CanRead { get; }
        /// <summary>Why the editor is read-only, in words ("" when it is not).</summary>
        string ReadOnlyReason { get; }
        /// <summary>A standing message about the connection ("" when all is well).</summary>
        string Note { get; }
        /// <summary>The limits shown are the server's, not this game's.</summary>
        bool LimitsFromServer { get; }

        /// <summary>Every picture: sends what is due, ends what waited too long.</summary>
        void Pump();

        string[][] List();                 // file, name, state, why, needs, approved, about, refused count, blocks ("1"/"0"), who is editing it live (names, comma separated)
        bool HasBlocks(string file);
        string Source(string file);
        string[] Log(string file, int count);
        string[][] LogRows(string file, int minLevel, bool onlyLevel, bool mine, string contains, int max);
        long[] LogCounts(string file);
        string Stats(string file);
        string[] Refused(string file);
        bool Trace(string file);
        string LogPath(string file);
        string[] LimitLines(string[] needs);

        void LogText(string file, int minLevel, bool onlyLevel, bool mine, string contains, Action<string> done);
        void LogClear(string file);
        void SetTrace(string file, bool on);
        void Stop(string file);
        void Allow(string file, string power, bool on);
        void Start(string file, Action<string> done);                 // done(why): "" when it started
        void Write(string file, string text, string baseHash, Action<string, bool> done);    // done(why, stale): "" when written; stale: it began from an older version (baseHash: the version it began from, "" if unknown)
        void New(string name, Action<string> done);                   // done(file): "" when it could not
        void Delete(string file, Action<bool> done);
        void LoadBlocks(string file, Action<Program, string> done);   // done(program, error)
        void SaveBlocks(string file, Program program, Compiled compiled, Action<string> done);   // done(why): "" when saved
        void CreateBlocks(string title, Action<string> done);         // done(file): "" when it could not

        /// <summary>Opens a block script for live editing with whoever else is in it; null when this game cannot (not an admin, no server with the mod, not connected). The editor then loads the program from the session, not with LoadBlocks.</summary>
        Scripts.Mp.CollabSession OpenCollab(string file);
    }

    /// <summary>This game runs the scripts: single player, the host, or a dedicated server. What the editor did before, unchanged.</summary>
    internal sealed class LocalBackend : IScriptsBackend
    {
        private readonly Dictionary<string, bool> _blocks = new Dictionary<string, bool>();

        public bool IsRemote => false;
        public string Origin => L("scripts_origin_local", "This game");
        public bool Connected => true;
        public bool CanChange => HostBridge.CanChange();
        public bool CanRead => true;
        public string ReadOnlyReason => CanChange ? "" : L("scripts_readonly", "Only the server's owner can change scripts.");
        public string Note => "";
        public bool LimitsFromServer => false;
        public void Pump() { }

        public string[][] List()
        {
            string[][] rows = HostBridge.List();
            _blocks.Clear();
            return rows.Select(r =>
            {
                bool has = Files.HasBlocks(r[0]);
                _blocks[r[0]] = has;
                string editing = has ? string.Join(", ", CollabGame.Server.NamesOf(r[0], Editors.Host)) : "";
                return r.Concat(new[] { has ? "1" : "0", editing }).ToArray();
            }).ToArray();
        }

        public bool HasBlocks(string file) => file != null && (_blocks.TryGetValue(file, out bool has) ? has : Files.HasBlocks(file));
        public string Source(string file) => HostBridge.Source(file);
        public string[] Log(string file, int count) => HostBridge.Log(file, count);
        public string[][] LogRows(string file, int minLevel, bool onlyLevel, bool mine, string contains, int max) => HostBridge.LogRows(file, minLevel, onlyLevel, mine, contains, max);
        public long[] LogCounts(string file) => HostBridge.LogCounts(file);
        public string Stats(string file) => HostBridge.Stats(file);
        public string[] Refused(string file) => HostBridge.Refused(file);
        public bool Trace(string file) => HostBridge.Trace(file);
        public string LogPath(string file) => HostBridge.LogPath(file);
        public string[] LimitLines(string[] needs) => Limits.Lines(needs);

        public void LogText(string file, int minLevel, bool onlyLevel, bool mine, string contains, Action<string> done) { done(HostBridge.LogText(file, minLevel, onlyLevel, mine, contains)); }
        public void LogClear(string file) { HostBridge.LogClear(file); }
        public void SetTrace(string file, bool on) { HostBridge.SetTrace(file, on); }
        public void Stop(string file) { HostBridge.Stop(file); }
        public void Allow(string file, string power, bool on) { HostBridge.Allow(file, power, on); }
        public void Start(string file, Action<string> done) { done(HostBridge.Start(file)); }
        public void Write(string file, string text, string baseHash, Action<string, bool> done)
        {
            if (CollabGame.Server.HasSession(file)) { done(L("scripts_co_live", "Somebody is editing this script live: open it in the editor to join them."), false); return; }
            if (!string.IsNullOrEmpty(baseHash) && Host.Scripts.Exists(s => s.File == file) && Scripts.Collab.Version.Check(Scripts.Collab.Version.Hash(HostBridge.Source(file)), baseHash) == Scripts.Collab.SaveCheck.Stale)
            {
                done(LastWriter.StaleMessage(file), true);
                return;
            }
            done(HostBridge.Write(file, text), false);
        }
        public void New(string name, Action<string> done) { done(HostBridge.New(name)); }
        public void Delete(string file, Action<bool> done) { Files.Delete(file); done(true); }

        public void LoadBlocks(string file, Action<Program, string> done)
        {
            Program p = Files.LoadLocal(file, out string error);
            done(p, error);
        }

        public void SaveBlocks(string file, Program program, Compiled compiled, Action<string> done)
        {
            if (CollabGame.Server.HasSession(file)) { done(L("scripts_co_live", "Somebody is editing this script live: open it in the editor to join them.")); return; }
            string why = Files.SaveLocal(file, program, compiled);
            if (why.Length == 0) LastWriter.Set(file, CollabGame.LocalName());
            done(why);
        }

        public void CreateBlocks(string title, Action<string> done) { done(Files.CreateLocal(title)); }

        public CollabSession OpenCollab(string file) => CanChange && Files.HasBlocks(file) ? CollabGame.OpenLocal(file) : null;
    }

    /// <summary>Connected to a server, but not one that has the mod (or it has not answered yet): nothing to show, and it says why.</summary>
    internal sealed class DisconnectedBackend : IScriptsBackend
    {
        private float _since = -1f;

        internal void Reset() { _since = -1f; }

        private string Say()
        {
            if (_since < 0f) _since = UnityEngine.Time.unscaledTime;
            return UnityEngine.Time.unscaledTime - _since < 8f
                ? L("scripts_asking_server", "Asking the server whether it has Quad's Blockscript...")
                : L("scripts_server_missing", "The server does not have Quad's Blockscript: scripts only run in single player or on a server that has it.");
        }

        public bool IsRemote => true;
        public string Origin => L("scripts_origin_none", "Not connected to Quad's Blockscript");
        public bool Connected => false;
        public bool CanChange => false;
        public bool CanRead => false;
        public string ReadOnlyReason => Say();
        public string Note => Say();
        public bool LimitsFromServer => false;
        public void Pump() { Say(); }

        public string[][] List() => new string[0][];
        public bool HasBlocks(string file) => false;
        public string Source(string file) => "";
        public string[] Log(string file, int count) => new string[0];
        public string[][] LogRows(string file, int minLevel, bool onlyLevel, bool mine, string contains, int max) => new string[0][];
        public long[] LogCounts(string file) => new long[7];
        public string Stats(string file) => "";
        public string[] Refused(string file) => new string[0];
        public bool Trace(string file) => false;
        public string LogPath(string file) => "";
        public string[] LimitLines(string[] needs) => new string[0];

        public void LogText(string file, int minLevel, bool onlyLevel, bool mine, string contains, Action<string> done) { done(""); }
        public void LogClear(string file) { }
        public void SetTrace(string file, bool on) { }
        public void Stop(string file) { }
        public void Allow(string file, string power, bool on) { }
        public void Start(string file, Action<string> done) { done(Say()); }
        public void Write(string file, string text, string baseHash, Action<string, bool> done) { done(Say(), false); }
        public void New(string name, Action<string> done) { done(""); }
        public void Delete(string file, Action<bool> done) { done(false); }
        public void LoadBlocks(string file, Action<Program, string> done) { done(null, Say()); }
        public void SaveBlocks(string file, Program program, Compiled compiled, Action<string> done) { done(Say()); }
        public void CreateBlocks(string title, Action<string> done) { done(""); }
        public CollabSession OpenCollab(string file) => null;
    }

    /// <summary>Which backend the editor uses right now.</summary>
    internal static class Backend
    {
        internal static readonly LocalBackend Local = new LocalBackend();
        internal static readonly RemoteBackend Remote = new RemoteBackend();
        internal static readonly DisconnectedBackend Off = new DisconnectedBackend();
        private static bool _hooked;

        /// <summary>Listens to the transport (the server's answers, joining and leaving). Safe to call every frame.</summary>
        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Wire.ClientGot += Remote.OnServerMessage;
            Wire.Changed += Remote.Reset;
            Wire.Changed += Off.Reset;
            AdminServer.Hook();
            CollabGame.Hook();
        }

        internal static IScriptsBackend Current
        {
            get
            {
                Hook();
                if (Wire.IsServer) return Local;
                return Wire.ServerHasMod ? (IScriptsBackend)Remote : Off;
            }
        }
    }
}
