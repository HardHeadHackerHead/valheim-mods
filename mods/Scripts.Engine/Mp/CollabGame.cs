using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Blocks;
using Scripts.Editor;

namespace Scripts.Mp
{
    /// <summary>
    /// Joins live co-editing to the game (docs/scripting-collab.md): the server's CollabServer gets the `co.*` messages that arrive over the
    /// wire from admins' games, and the game's own editor (single player, a listen host) talks to it in-process, with no wire in between.
    /// The pure logic is in CollabServer.cs and CollabClient.cs (tested without the game); this only supplies who is an admin, how to send,
    /// and the disk: compile and save through Files, the stale-save record, the audit log.
    /// </summary>
    internal static class CollabGame
    {
        private static CollabServer _server;
        private static bool _hooked;
        private static float _nextTick;
        private static readonly Queue<KeyValuePair<string, string>> ToHost = new Queue<KeyValuePair<string, string>>();
        private static readonly List<CollabSession> LocalSessions = new List<CollabSession>();

        internal static CollabServer Server
        {
            get
            {
                if (_server == null) _server = new CollabServer(new GameHost());
                return _server;
            }
        }

        /// <summary>Starts listening to the wire (once). Safe to call every frame.</summary>
        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Wire.ServerGot += OnServerGot;
        }

        private static void OnServerGot(long peer, string kind, string json, long id)
        {
            if (!Co.IsCo(kind) || !Wire.IsServer) return;
            try { Server.OnMessage(Editors.Peer(peer), kind, json); }
            catch (Exception e) { Core.Log.LogWarning("A co-editing message failed: " + e.Message); }
        }

        /// <summary>Every frame: the host's own editor gets what the server sent it, and (four times a second) the server ticks.</summary>
        internal static void Update()
        {
            Hook();
            if (ToHost.Count > 0)
            {
                // (queued, not called from inside the server: an editor never runs inside the server's own handler)
                var batch = ToHost.ToArray(); ToHost.Clear();
                foreach (var m in batch)
                    foreach (CollabSession s in LocalSessions.ToList())
                        try { s.OnMessage(m.Key, m.Value); } catch (Exception) { }
                LocalSessions.RemoveAll(s => s.Status == CollabSession.State.Closed);
            }
            if (_server == null || !Wire.IsServer) return;
            if (UnityEngine.Time.unscaledTime < _nextTick) return;
            _nextTick = UnityEngine.Time.unscaledTime + 0.25f;
            try
            {
                foreach (string editor in Server.AllEditors())
                    if (Editors.IsPeer(editor, out long uid) && !Wire.HasAgent(uid)) Server.EditorGone(editor);   // (left the server)
                Server.Tick();
            }
            catch (Exception e) { Core.Log.LogWarning("Co-editing tick failed: " + e.Message); }
        }

        /// <summary>The game's own editor opens a script for live editing: the session talks to the server in this process.</summary>
        internal static CollabSession OpenLocal(string file)
        {
            Hook();
            var s = new CollabSession(file);
            s.Send = (kind, json) => { Server.OnMessage(Editors.Host, kind, json); return true; };
            LocalSessions.Add(s);
            s.Open();
            return s;
        }

        /// <summary>A script was deleted or is going away: everybody editing it is told and sent out.</summary>
        internal static void CloseFile(string file, string why)
        {
            if (_server != null) _server.CloseFile(file, why, save: false);
            LastWriter.Forget(file);
        }

        internal static void Shutdown(string why)
        {
            try { if (_server != null) _server.Shutdown(why); } catch (Exception) { }
            ToHost.Clear(); LocalSessions.Clear();
            if (_hooked) { Wire.ServerGot -= OnServerGot; _hooked = false; }
            _server = null;
        }

        /// <summary>The name the game gives its own editor.</summary>
        internal static string LocalName()
        {
            try { if (Player.m_localPlayer != null) return Player.m_localPlayer.GetPlayerName(); } catch (Exception) { }
            return "the server's owner";
        }

        /// <summary>For `scripts collab`.</summary>
        internal static string[] Describe() => _server == null ? new[] { "no script is being edited live" } : (_server.Describe().Length == 0 ? new[] { "no script is being edited live" } : _server.Describe());

        private sealed class GameHost : ICollabHost
        {
            public DateTime Now => DateTime.UtcNow;

            public bool IsAdmin(string editor)
            {
                if (editor == Editors.Host) return true;
                return Editors.IsPeer(editor, out long uid) && Wire.IsAdmin(uid);
            }

            public string NameOf(string editor)
            {
                if (editor == Editors.Host) return LocalName();
                if (Editors.IsPeer(editor, out long uid)) { string n = Wire.NameOf(uid); return string.IsNullOrEmpty(n) ? "a player" : n; }
                return "someone";
            }

            public void Send(string editor, string kind, string json)
            {
                if (editor == Editors.Host) { ToHost.Enqueue(new KeyValuePair<string, string>(kind, json)); return; }
                if (Editors.IsPeer(editor, out long uid)) Wire.SendToPeer(uid, kind, json);
            }

            public bool Exists(string file) => Files.HasBlocks(file);

            public Program Load(string file, out string error) => Files.LoadLocal(file, out error);

            public string Save(string file, Program program)
            {
                Compiled compiled = Compiler.Compile(program);
                return Files.SaveLocal(file, program, compiled);
            }

            public void Audit(string line)
            {
                string text = DateTime.Now.ToString("HH:mm:ss") + " [co-edit] " + line;
                if (Scripts.Audit.Lines.Count >= 300) Scripts.Audit.Lines.RemoveAt(0);
                Scripts.Audit.Lines.Add(text);
                Core.Log.LogInfo("Audit: " + text);
            }

            public void Wrote(string file, string who) { LastWriter.Set(file, who); }
        }
    }
}
