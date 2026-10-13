using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scripts.Mp
{
    /// <summary>
    /// The multiplayer transport (docs/scripting-multiplayer.md section 2): messages between the server that runs the scripts and the games
    /// of the players who have the mod, over each player's direct connection (the ZRpc of the peer, which cannot be forged the way a routed
    /// RPC's sender can). One RPC name, one package with a part of a packed frame in it. Nothing is ever kicked or broken for a player who
    /// does not have the mod: the server just never hears a hello from them. Every handler is wrapped: a bad message is dropped, never thrown
    /// into the game.
    /// </summary>
    internal static class Wire
    {
        internal const int Protocol = 1;
        private const string RpcName = "blockscript.m";

        private sealed class PeerInfo
        {
            public ZRpc Rpc;
            public long Uid;
            public string Host = "", Name = "";
            public bool Hello;
            public int Version;
            public readonly PeerLimiter Limiter = new PeerLimiter();
            public ushort Number;
            public float LastDropLog;
        }

        private static readonly Dictionary<ZRpc, PeerInfo> Peers = new Dictionary<ZRpc, PeerInfo>();
        private static readonly Frames.Reassembler ServerParts = new Frames.Reassembler(), ClientParts = new Frames.Reassembler();
        private static readonly PeerLimiter ClientLimiter = new PeerLimiter();

        private static ZRpc _serverRpc;
        private static bool _serverHello, _admin;
        private static string _serverVersion = "";
        private static ushort _clientNumber;
        private static float _nextPoll, _helloAt;
        private static int _helloTries;
        private static bool _warned;

        /// <summary>This game runs the scripts: single player, the host of a listen server, or a dedicated server.</summary>
        internal static bool IsServer => ZNet.instance == null || ZNet.instance.IsServer();
        /// <summary>A client: the server answered hello (always true when IsServer).</summary>
        internal static bool ServerHasMod => IsServer || _serverHello;
        /// <summary>A client: the server said this player is an admin (the server's own game: always true).</summary>
        internal static bool LocalIsAdmin => IsServer || _admin;
        internal static string ServerVersion => _serverVersion;

        /// <summary>This game is a client of a server that has the mod: the Agent is on duty.</summary>
        internal static bool IsAgent => !IsServer && _serverHello;

        /// <summary>Joined, left, the answer arrived, the admin flag changed.</summary>
        internal static event Action Changed;

        // ================================================================ server side

        internal static IEnumerable<long> AgentPeers() => Peers.Values.Where(p => p.Hello).Select(p => p.Uid).ToList();

        internal static bool HasAgent(long peer) => Find(peer)?.Hello == true;

        /// <summary>On the connection itself: the server's own admin list, asked about the host name of that connection. The host's own game is always one.</summary>
        internal static bool IsAdmin(long peer)
        {
            if (peer == LocalPeer) return true;
            PeerInfo info = Find(peer);
            return info != null && info.Host.Length > 0 && ZNet.instance != null && ZNet.instance.IsAdmin(info.Host);
        }

        internal static string NameOf(long peer) => Find(peer)?.Name ?? "";

        /// <summary>A player for the log: the character's name once the game knows it (it does not yet when the mods first greet), and always the account id, so a line can be matched to a player.</summary>
        private static string Who(PeerInfo info)
        {
            string name = info.Name;
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(info.Uid) : null;
            if (string.IsNullOrEmpty(name) && peer != null) name = info.Name = peer.m_playerName ?? "";
            return (string.IsNullOrEmpty(name) ? "a player" : name) + " (" + info.Host + ")";
        }

        /// <summary>This game's own connection id (what a character this game owns says its owner is).</summary>
        internal static long LocalPeer => ZDOMan.GetSessionID();

        internal static void SendToPeer(long peer, string kind, string json, long id = 0)
        {
            JObject d = ParseData(json);
            if (d != null) Send(peer, new Frame(kind, id, d));
        }

        internal static void SendToAgents(string kind, string json)
        {
            JObject d = ParseData(json);
            if (d == null) return;
            foreach (long peer in AgentPeers()) Send(peer, new Frame(kind, 0, (JObject)d.DeepClone()));
        }

        /// <summary>(peer, kind, json, id): a client's message reached the server (rate limited, parsed as a frame, the sender is the connection).</summary>
        internal static event Action<long, string, string, long> ServerGot;

        // ================================================================ client side

        internal static void SendToServer(string kind, string json, long id = 0)
        {
            JObject d = ParseData(json);
            if (d != null) SendToServer(new Frame(kind, id, d));
        }

        /// <summary>(kind, json, id): the server's message reached this client.</summary>
        internal static event Action<string, string, long> ClientGot;

        // ================================================================ raising

        internal static void RaiseChanged() { try { Changed?.Invoke(); } catch (Exception e) { Warn("a listener of Changed failed: " + e.Message); } }
        internal static void RaiseServerGot(long peer, string kind, string json, long id) { try { ServerGot?.Invoke(peer, kind, json, id); } catch (Exception e) { Warn("a listener of ServerGot failed: " + e.Message); } }
        internal static void RaiseClientGot(string kind, string json, long id) { try { ClientGot?.Invoke(kind, json, id); } catch (Exception e) { Warn("a listener of ClientGot failed: " + e.Message); } }

        private static void Warn(string text)
        {
            if (Core.Log != null) Core.Log.LogWarning("Scripts (multiplayer): " + text);
        }

        private static JObject ParseData(string json)
        {
            try { return string.IsNullOrEmpty(json) ? new JObject() : JObject.Parse(json); }
            catch (Exception) { Warn("a message with bad data was not sent"); return null; }
        }

        private static PeerInfo Find(long uid) { foreach (PeerInfo p in Peers.Values) if (p.Uid == uid) return p; return null; }

        // ================================================================ sending

        internal static void Send(long peer, Frame f)
        {
            PeerInfo info = Find(peer);
            if (info == null || !info.Hello && f.K != "welcome") return;
            SendOn(info.Rpc, f, ref info.Number);
        }

        internal static void SendToServer(Frame f)
        {
            if (_serverRpc == null) return;
            if (f.K != "hello" && !_serverHello) return;
            SendOn(_serverRpc, f, ref _clientNumber);
        }

        private static void SendOn(ZRpc rpc, Frame f, ref ushort number)
        {
            try
            {
                if (rpc == null) return;
                byte[] packed = Frames.Pack(f);
                number++;
                foreach (byte[] part in Frames.Split(packed, number))
                {
                    var pkg = new ZPackage();
                    pkg.Write(part);
                    rpc.Invoke(RpcName, pkg);
                }
            }
            catch (Exception e) { Warn("a '" + f.K + "' message could not be sent: " + e.Message); }
        }

        // ================================================================ finding each other (every half second, like ServerSettings)

        /// <summary>Every frame (the plugin's Update).</summary>
        internal static void Tick()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            try
            {
                ZNet net = ZNet.instance;
                if (net == null) { Leave(); return; }
                if (net.IsServer()) PollServer(net); else PollClient(net);
            }
            catch (Exception e) { if (!_warned) { _warned = true; Warn("polling failed: " + e); } }
        }

        private static void PollServer(ZNet net)
        {
            var live = new HashSet<ZRpc>();
            foreach (ZNetPeer peer in net.GetPeers())
            {
                if (peer?.m_rpc == null) continue;
                live.Add(peer.m_rpc);
                if (Peers.ContainsKey(peer.m_rpc)) continue;
                Peers[peer.m_rpc] = new PeerInfo { Rpc = peer.m_rpc, Uid = peer.m_uid, Host = peer.m_rpc.GetSocket()?.GetHostName() ?? "", Name = peer.m_playerName ?? "" };
                peer.m_rpc.Register<ZPackage>(RpcName, OnServerPackage);
            }
            foreach (ZRpc gone in Peers.Keys.Where(k => !live.Contains(k)).ToList())
            {
                PeerInfo info = Peers[gone];
                Peers.Remove(gone);
                ServerParts.Forget(info.Uid.ToString());
                Remote.PeerLeft(info.Uid);
                if (info.Hello) { Core.Log.LogInfo("Scripts (multiplayer): " + Who(info) + " left"); RaiseChanged(); }
            }
        }

        private static void PollClient(ZNet net)
        {
            ZRpc rpc = net.GetServerPeer()?.m_rpc;
            if (rpc != _serverRpc)
            {
                LeaveClient();
                _serverRpc = rpc;
                _helloTries = 0;
                if (rpc != null) { rpc.Register<ZPackage>(RpcName, OnClientPackage); SendHello(); }
                RaiseChanged();
            }
            else if (rpc != null && !_serverHello && _helloTries < 12 && Time.unscaledTime - _helloAt > 2f) SendHello();   // (the server registers a new connection up to half a second after it opens)
        }

        private static void SendHello()
        {
            _helloAt = Time.unscaledTime;
            _helloTries++;
            SendToServer(new Frame("hello", 0, new JObject { ["v"] = Protocol, ["version"] = Core.Version }));
        }

        private static void LeaveClient()
        {
            bool was = _serverHello || _admin;
            _serverRpc = null; _serverHello = false; _admin = false; _serverVersion = "";
            ClientParts.Forget("server");
            Limits.ClearServer();
            Lists.Reset();
            Agent.Leave();
            if (was) RaiseChanged();
        }

        /// <summary>The world was left (or never joined): forget everyone.</summary>
        private static void Leave()
        {
            if (_serverRpc != null || _serverHello || _admin) { LeaveClient(); }
            if (Peers.Count > 0)
            {
                foreach (PeerInfo p in Peers.Values) Remote.PeerLeft(p.Uid);
                Peers.Clear();
                RaiseChanged();
            }
        }

        /// <summary>The mod is going away.</summary>
        internal static void Stop()
        {
            try { Leave(); } catch (Exception) { }
            Agent.Leave();
            Remote.Reset();
        }

        // ================================================================ receiving

        private static void OnServerPackage(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                if (!IsServer || !Peers.TryGetValue(rpc, out PeerInfo info)) return;
                byte[] part = pkg.ReadByteArray();
                double now = Time.timeAsDouble;
                if (!info.Limiter.Allow(now, part.Length))
                {
                    if (Time.unscaledTime - info.LastDropLog > 30f) { info.LastDropLog = Time.unscaledTime; Warn(info.Name + " is sending too fast: messages are being dropped"); }
                    return;
                }
                if (!ServerParts.Add(info.Uid.ToString(), part, now, out byte[] whole, out string why)) return;
                if (!Frames.TryUnpack(whole, out Frame f, out why)) return;
                HandleServer(info, f);
            }
            catch (Exception e) { Warn("a message from a player could not be read: " + e.Message); }
        }

        private static void HandleServer(PeerInfo info, Frame f)
        {
            if (f.K == "hello")
            {
                bool first = !info.Hello;
                info.Hello = true;
                info.Version = Math.Max(1, Math.Min(Protocol, (int)f.N("v", 1)));
                if (first) Core.Log.LogInfo("Scripts (multiplayer): " + Who(info) + " has the mod (version " + f.S("version") + ")");
                Send(info.Uid, new Frame("welcome", 0, Remote.WelcomeFor(info.Uid)));
                if (first) RaiseChanged();
                return;
            }
            if (!info.Hello) return;   // (a hello must come first)
            switch (f.K)
            {
                case "state": Remote.OnState(info.Uid, f); return;
                case "event": Remote.OnEvent(info.Uid, f); return;
                case "done": Remote.OnDone(info.Uid, f); return;
            }
            RaiseServerGot(info.Uid, f.K, f.D.ToString(Newtonsoft.Json.Formatting.None), f.I);
        }

        private static void OnClientPackage(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                if (rpc != _serverRpc) return;   // only what came over the connection to the server
                byte[] part = pkg.ReadByteArray();
                double now = Time.timeAsDouble;
                if (!ClientLimiter.Allow(now, part.Length)) return;
                if (!ClientParts.Add("server", part, now, out byte[] whole, out string why)) return;
                if (!Frames.TryUnpack(whole, out Frame f, out why)) return;
                HandleClient(f);
            }
            catch (Exception e) { Warn("a message from the server could not be read: " + e.Message); }
        }

        private static void HandleClient(Frame f)
        {
            if (f.K == "welcome")
            {
                bool first = !_serverHello;
                _serverHello = true;
                _serverVersion = f.S("version");
                _admin = f.B("admin");
                Limits.ApplyServer(f.D["limits"] as JObject);
                if (f.D["lists"] is JObject lists) Lists.Apply(lists);
                Agent.OnWelcome(f);
                if (first) Core.Log.LogInfo("Scripts (multiplayer): this server runs Quad's Blockscript " + _serverVersion + (_admin ? " (you are an admin)" : ""));
                RaiseChanged();
                return;
            }
            if (!_serverHello) return;
            switch (f.K)
            {
                case "limits":
                    Limits.ApplyServer(f.D["limits"] as JObject);
                    if (f.D["lists"] is JObject lists) Lists.Apply(lists);
                    RaiseChanged();
                    return;
                case "act": Agent.OnAct(f); return;
                case "fx": Agent.OnFx(f); return;
                case "listen": Agent.OnListen(f); return;
            }
            RaiseClientGot(f.K, f.D.ToString(Newtonsoft.Json.Formatting.None), f.I);
        }
    }
}
