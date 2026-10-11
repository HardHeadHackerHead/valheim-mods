using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// The version check, and the server's packs for its players (PackSync.cs holds the sets). Done the way ServerSync does it, in the
    /// connection's own handshake (the peer's ZRpc, before the game adds the player to the world), so it's settled before any of the
    /// world's objects arrive:
    ///   1. As a connection opens (ZNet.OnNewConnection), each side sends Dungeons_Version: this protocol, the mod's version and the hash of
    ///      its own set of packs and looks.
    ///   2. The server, given the player's PeerInfo, refuses a player whose Dungeons version differs, or who sent none (no mod, or 0.1.0,
    ///      which had no check), with the game's own "incompatible version" error. The player's game adds which versions (if it has the
    ///      mod) under the game's message. A client refuses a server with another version the same way (in case it didn't).
    ///      Why refuse a player without the mod rather than let them in: on their game an entrance is nothing (no prefab), so they can't go
    ///      in, but whatever their game owns stands 5000 m up with no floor under it there: the dungeon's creatures, a gravestone, items
    ///      dropped inside would fall out of it onto the ground. Every player needs the mod, at the server's version.
    ///   3. Once the player is in, the server sends Dungeons_Packs: its whole set (gzipped, in parts of 64 KB), or, if the player's own
    ///      set has the same hash, just "use your own". The player uses exactly that set while connected (PackSync.UseServer), and has
    ///      no packs at all until it's in (PackSync.Waiting).
    ///   4. On a server without a matching Dungeons (none, or 0.1.0), or when the server's set never comes or none of it can be used,
    ///      dungeons are off for that connection (PackSync.TurnOff: no packs, so nothing laid out, spawned or entered), and the player is
    ///      told why. Never the player's own packs on a server: the world would split.
    /// Settings: the server's ResetDays (Plugin.cs, through ServerSettings); it's the only gameplay setting.
    /// </summary>
    internal static partial class SyncNet   // (HotReload.cs: its state and handlers carried over a reload)
    {
        // What the two sides must agree on to play together: 1 (0.2.0-0.2.2) the version check and the packs; 2 (0.2.3) and the acts with
        // ids and answers (Acts.Protocol). A connection with another one is refused; one already connected when a reload in a world changes
        // it (HotReload.cs) is kept, and the host is told who needs to update (AfterReload).
        public const int Protocol = 2;
        private const string VersionRpc = "Dungeons_Version";   // ZPackage: protocol, the mod's version, the sender's own set's hash
        private const string SetRpc = "Dungeons_Packs";         // ZPackage: protocol, the set's hash, part, parts, bytes (0 parts: "the same as yours")
        private const int PartSize = 64 * 1024;
        private const int MaxParts = PackSync.MaxSetBytes / PartSize;

        private static readonly AccessTools.FieldRef<ZNet.ConnectionStatus> Status =
            AccessTools.StaticFieldRefAccess<ZNet.ConnectionStatus>(AccessTools.Field(typeof(ZNet), "m_connectionStatus"));

        // ---- a client: its server

        public static string ServerVersion;     // the server's Dungeons version (null: none heard: no mod there, or 0.1.0)
        public static string ServerHash;        // the hash of the server's set
        public static int ServerProtocol;       // the server's protocol (0: none heard)
        public static string MismatchText;      // why this game left the last server (shown under the game's connection error)
        private static bool _serverAnswered;    // the server's PeerInfo came (so ServerVersion null means it has no mod)
        private static float _expectedSince;
        private static byte[][] _parts;
        private static string _partsHash;
        private static int _partsIn;
        public static string Failed;            // the server's set came but couldn't be read, or never came
        private static int _connection;         // goes up with each connection (a watch or a notice for an older one stops)
        private const float SetWait = 30f;      // seconds after the server's version before its set counts as never coming

        // ---- the server: its players

        private class Peer
        {
            public ZRpc Rpc;
            public string Version, Hash;
            public int Protocol;
            public string Sent;
        }
        private static readonly List<Peer> Peers = new List<Peer>();
        private static readonly List<string> Refused = new List<string>();
        private static byte[] _packed;
        private static string _packedHash;

        private static ZPackage Mine()
        {
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(Plugin.Version);
            pkg.Write(PackSync.Local.Hash);
            return pkg;
        }

        private static ZNetPeer PeerOf(ZNet znet, ZRpc rpc) => znet.GetPeers().FirstOrDefault(p => p.m_rpc == rpc);

        /// <summary>A connection opens (both sides): Dungeons_Version both ways.</summary>
        public static void OnNewConnection(ZNet znet, ZNetPeer peer)
        {
            try
            {
                peer.m_rpc.Register<ZPackage>(VersionRpc, OnVersion);
                if (znet.IsServer())
                {
                    Peers.RemoveAll(p => !znet.GetPeers().Any(z => z.m_rpc == p.Rpc));
                    Peers.Add(new Peer { Rpc = peer.m_rpc });
                }
                else
                {
                    // a new server: forget the last one's
                    _connection++;
                    ServerVersion = ServerHash = MismatchText = Failed = null;
                    ServerProtocol = 0;
                    _serverAnswered = false;
                    _parts = null;
                    _partsHash = null;
                    PackSync.Reset();
                    peer.m_rpc.Register<ZPackage>(SetRpc, OnSetPart);
                }
                peer.m_rpc.Invoke(VersionRpc, Mine());
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeons version check: " + e); }
        }

        private static void OnVersion(ZRpc rpc, ZPackage pkg)
        {
            int protocol;
            string version, hash;
            try
            {
                protocol = pkg.ReadInt();
                version = pkg.ReadString();
                hash = pkg.ReadString();
            }
            catch (Exception) { protocol = 0; version = "(unreadable)"; hash = ""; }
            ZNet znet = ZNet.instance;
            if (znet == null) return;
            if (znet.IsServer())
            {
                Peer p = Peers.FirstOrDefault(x => x.Rpc == rpc);
                if (p == null) Peers.Add(p = new Peer { Rpc = rpc });
                ZNetPeer from = PeerOf(znet, rpc);
                bool update = p.Version != null && from != null && from.IsReady();   // (a player already in, whose game reloaded the mod)
                p.Protocol = protocol;
                p.Version = version;
                p.Hash = hash;
                if (update)
                {
                    Plugin.Log.LogInfo($"{from.m_playerName} now has Quad's Dungeons {version} (protocol {protocol})");
                    Compare(from, p);
                }
                return;
            }
            ZNetPeer server = PeerOf(znet, rpc);
            if (server == null || !server.m_server) return;   // (only the server's)
            if (_serverAnswered && ServerVersion != null)
            {
                // the server reloaded the mod while this game is connected: noted, never a refusal or a new wait (the set comes if it changed)
                ServerVersion = version;
                ServerHash = hash;
                ServerProtocol = protocol;
                if (protocol != Protocol || version != Plugin.Version)
                    Plugin.Log.LogWarning($"The server reloaded Quad's Dungeons {version} (protocol {protocol}), this game has {Plugin.Version} (protocol {Protocol}): the same version is needed to play together; update and rejoin");
                return;
            }
            ServerVersion = version;
            ServerHash = hash;
            ServerProtocol = protocol;
            if (version != Plugin.Version || protocol != Protocol)
            {
                MismatchText = T("dungeons_sync_version", version, Plugin.Version);
                Plugin.Log.LogWarning($"The server has Quad's Dungeons {version}, this game {Plugin.Version}: every player needs the server's version (the connection is refused)");
                return;
            }
            PackSync.Expect();
            _expectedSince = Time.realtimeSinceStartup;
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(WatchSet(_connection));
            Plugin.Log.LogInfo($"The server has Quad's Dungeons {version}: its packs and looks are used while playing there" + (hash == PackSync.Local.Hash ? " (the same as this game's own)" : " (they come next)"));
        }

        /// <summary>Before the game takes a PeerInfo: the server refuses a player with another version or none; a client a server with
        /// another version. False: refused (the game's own ErrorVersion, as for a wrong game version).</summary>
        public static bool Admit(ZNet znet, ZRpc rpc)
        {
            try
            {
                if (!znet.IsServer())
                {
                    _serverAnswered = true;
                    if (ServerVersion == null)
                    {
                        // Not this game's own packs: the server can't check anyone's version, its other players may have other packs or no
                        // mod (things they own would fall out of a dungeon), and a server without the mod doesn't know our prefabs (it
                        // deletes what it doesn't know). So no dungeons here at all.
                        PackSync.TurnOff("the server doesn't have Quad's Dungeons (or has 0.1.0, which had no version check)");
                        Plugin.Log.LogWarning("Dungeons are off on this server: it doesn't have Quad's Dungeons (or has 0.1.0, which had no version check)");
                        Notice(T("dungeons_sync_noserver"));
                        return true;
                    }
                    if (MismatchText == null) return true;
                    Status() = ZNet.ConnectionStatus.ErrorVersion;
                    return false;
                }
                ZNetPeer peer = PeerOf(znet, rpc);
                Peer p = Peers.FirstOrDefault(x => x.Rpc == rpc);
                string who = peer?.m_socket?.GetEndPointString() ?? "?";
                string why = null;
                if (p == null || p.Version == null) why = "has no Quad's Dungeons (or 0.1.0, which had no version check)";
                else if (p.Version != Plugin.Version || p.Protocol != Protocol) why = $"has Quad's Dungeons {p.Version}";
                if (why == null) return true;
                rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
                string line = $"{DateTime.Now:HH:mm:ss} {who} {why}";
                Refused.Add(line);
                if (Refused.Count > 10) Refused.RemoveAt(0);
                Plugin.Log.LogWarning($"A player ({who}) {why}, the server {Plugin.Version}: refused (every player needs the server's version)");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Dungeons version check: " + e);
                return true;
            }
        }

        /// <summary>After the game took a PeerInfo: on the server, a player who was let in gets the packs.</summary>
        public static void AfterPeerInfo(ZNet znet, ZRpc rpc)
        {
            if (!znet.IsServer()) return;
            try
            {
                ZNetPeer peer = PeerOf(znet, rpc);
                Peer p = Peers.FirstOrDefault(x => x.Rpc == rpc);
                if (peer == null || !peer.IsReady() || p == null || p.Version == null || p.Sent != null) return;   // (refused, or sent)
                SendSet(peer, p);
            }
            catch (Exception e) { Plugin.Log.LogError("Sending the packs failed: " + e); }
        }

        /// <summary>The server's set changed (`dungeon reload`): every player in the world gets it again (or "the same as yours").</summary>
        public static int Resend()
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer()) return 0;
            int sent = 0;
            foreach (ZNetPeer peer in znet.GetPeers())
            {
                Peer p = Peers.FirstOrDefault(x => x.Rpc == peer.m_rpc);
                if (!peer.IsReady() || p == null || p.Version == null) continue;
                try { SendSet(peer, p); sent++; }
                catch (Exception e) { Plugin.Log.LogError($"Sending the packs to {peer.m_playerName} again failed: " + e); }
            }
            return sent;
        }

        private static void SendSet(ZNetPeer peer, Peer p)
        {
            ZRpc rpc = peer.m_rpc;
            PackSync.Set set = PackSync.Local;
            if (p.Hash == set.Hash)
            {
                rpc.Invoke(SetRpc, Part(set.Hash, 0, 0, new byte[0]));
                p.Sent = "the same as its own";
                return;
            }
            if (_packed == null || _packedHash != set.Hash) { _packed = PackSync.Pack(set); _packedHash = set.Hash; }
            int parts = Math.Max(1, (_packed.Length + PartSize - 1) / PartSize);
            for (int i = 0; i < parts; i++)
            {
                int from = i * PartSize, length = Math.Min(PartSize, _packed.Length - from);
                var bytes = new byte[length];
                Buffer.BlockCopy(_packed, from, bytes, 0, length);
                rpc.Invoke(SetRpc, Part(set.Hash, i, parts, bytes));
            }
            p.Sent = $"{set.Packs.Count} packs, {set.Looks.Count} looks, set {set.Hash} ({_packed.Length / 1024} KB in {parts} parts)";
            Plugin.Log.LogInfo($"Sent {peer.m_playerName} the packs and looks: {p.Sent}");
        }

        private static ZPackage Part(string hash, int index, int count, byte[] bytes)
        {
            var pkg = new ZPackage();
            pkg.Write(Protocol);
            pkg.Write(hash);
            pkg.Write(index);
            pkg.Write(count);
            pkg.Write(bytes);
            return pkg;
        }

        /// <summary>A client: a part of the server's set (or "the same as yours"). Once every part is in, it's checked and used.</summary>
        private static void OnSetPart(ZRpc rpc, ZPackage pkg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || znet.IsServer()) return;
            ZNetPeer server = PeerOf(znet, rpc);
            if (server == null || !server.m_server || ServerVersion == null || MismatchText != null) return;
            try
            {
                int protocol = pkg.ReadInt();
                string hash = pkg.ReadString();
                int index = pkg.ReadInt(), count = pkg.ReadInt();
                byte[] bytes = pkg.ReadByteArray();
                if (protocol != Protocol) return;
                if (count == 0)
                {
                    PackSync.UseSame();
                    Plugin.Log.LogInfo($"The server's packs and looks are this game's own (set {hash})");
                    return;
                }
                if (count < 0 || count > MaxParts || index < 0 || index >= count) throw new InvalidOperationException($"part {index} of {count}");
                if (_parts == null || _partsHash != hash || _parts.Length != count) { _parts = new byte[count][]; _partsHash = hash; _partsIn = 0; }
                if (_parts[index] == null) _partsIn++;
                _parts[index] = bytes;
                if (_partsIn < count) return;
                byte[] all = _parts.SelectMany(b => b).ToArray();
                _parts = null;
                PackSync.Set set = PackSync.Unpack(all);
                if (set.Hash != hash)
                    Plugin.Log.LogWarning($"The server's set is {hash}, but what this game made of it is {set.Hash} (left out here: {string.Join("; ", set.LeftOut)})");
                PackSync.UseServer(set);
                Failed = set.Packs.Count == 0 ? "none of the server's packs could be used here" : null;
                if (Failed != null)
                {
                    PackSync.TurnOff(Failed);
                    Plugin.Log.LogError($"Dungeons are off on this server: none of the server's packs could be used here ({string.Join("; ", set.LeftOut)})");
                    Notice(T("dungeons_sync_failed"));
                }
                Plugin.Log.LogInfo($"The server's packs and looks are in ({all.Length / 1024} KB): {string.Join(", ", set.Packs.Keys)}; looks {string.Join(", ", set.Looks.Keys)}");
            }
            catch (Exception e)
            {
                // Never the client's own instead: a dungeon laid out from another pack would stand elsewhere than the server's.
                Failed = e.GetType().Name + ": " + e.Message;
                _parts = null;
                Plugin.Log.LogError("The server's packs can't be read (no dungeon can be built here until reconnecting): " + e);
                PackSync.TurnOff("the server's packs couldn't be read: " + Failed);
                Notice(T("dungeons_sync_failed"));
            }
        }

        /// <summary>
        /// After a reload of the mod in a world (HotReload.cs): this copy's version goes to every connected game that understands it being
        /// sent again (protocol 2 or more: an older copy would take it as a new connection's and wait for a set), and the host compares
        /// every player's protocol with its own: whoever has an older one needs to update and rejoin (told on the host's screen and in the
        /// log; they stay connected, and Acts.cs keeps their levers working), whoever has a newer one means the host should update.
        /// </summary>
        public static void AfterReload()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return;
            try
            {
                foreach (ZNetPeer peer in znet.GetPeers())
                {
                    if (!peer.IsReady()) continue;
                    int theirs = znet.IsServer() ? (Peers.FirstOrDefault(x => x.Rpc == peer.m_rpc)?.Protocol ?? 0) : (peer.m_server ? ServerProtocol : 0);
                    if (theirs >= 2) peer.m_rpc.Invoke(VersionRpc, Mine());
                }
                if (!znet.IsServer()) return;
                foreach (ZNetPeer peer in znet.GetPeers())
                {
                    Peer p = Peers.FirstOrDefault(x => x.Rpc == peer.m_rpc);
                    if (peer.IsReady() && p?.Version != null) Compare(peer, p);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Dungeons version check after a reload: " + e.Message); }
        }

        /// <summary>The host: a connected player whose Dungeons differs from this game's (after a reload in a world on either side).</summary>
        private static void Compare(ZNetPeer peer, Peer p)
        {
            if (p.Protocol < Protocol)
            {
                Plugin.Log.LogWarning($"{peer.m_playerName} has Quad's Dungeons {p.Version} (protocol {p.Protocol}), this game {Plugin.Version} (protocol {Protocol}): they need to update and rejoin (kept connected meanwhile)");
                HostNotice(T("dungeons_sync_peer_old", peer.m_playerName, p.Version, Plugin.Version));
            }
            else if (p.Protocol > Protocol)
            {
                Plugin.Log.LogWarning($"{peer.m_playerName} has Quad's Dungeons {p.Version} (protocol {p.Protocol}), newer than this game's {Plugin.Version} (protocol {Protocol}): update this game's and load the world again");
                HostNotice(T("dungeons_sync_peer_new", peer.m_playerName, p.Version));
            }
            else if (p.Version != Plugin.Version)
                Plugin.Log.LogInfo($"{peer.m_playerName} has Quad's Dungeons {p.Version}, this game {Plugin.Version} (the same protocol {Protocol}: they play together; the next join needs the same version)");
        }

        /// <summary>A line at the top left of the host's own screen (a dedicated server has none: the log has it).</summary>
        private static void HostNotice(string text)
        {
            if (Player.m_localPlayer != null && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>
        /// A client waiting for its server's set never waits for good: if it hasn't come 30 s after the server's version did (the server
        /// failed to send it), and the server's set has the same hash as this game's own, that's used (it's the same); otherwise there are
        /// no dungeons until reconnecting, and the player is told. A new connection or leaving the world ends the watch (and Waiting).
        /// </summary>
        private static IEnumerator WatchSet(int connection)
        {
            while (connection == _connection && PackSync.Waiting && Time.realtimeSinceStartup - _expectedSince < SetWait) yield return null;
            if (connection != _connection || !PackSync.Waiting || ZNet.instance == null) yield break;
            if (ServerHash != null && ServerHash == PackSync.Local.Hash)
            {
                Plugin.Log.LogWarning($"The server's packs didn't come in {SetWait:0} s, but its set is this game's own ({ServerHash}): that's used");
                PackSync.UseSame();
                yield break;
            }
            Failed = $"the server's packs didn't come in {SetWait:0} s";
            PackSync.TurnOff(Failed);
            Plugin.Log.LogError($"The server's packs didn't come in {SetWait:0} s, and its set ({ServerHash}) isn't this game's own ({PackSync.Local.Hash}): no dungeon can be built here until reconnecting");
            Notice(T("dungeons_sync_failed"));
        }

        /// <summary>A message at the top left once the player is in the world (for this connection only).</summary>
        private static void Notice(string text)
        {
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ShowNotice(_connection, text));
        }

        private static IEnumerator ShowNotice(int connection, string text)
        {
            float until = Time.realtimeSinceStartup + 300f;
            while (connection == _connection && (Player.m_localPlayer == null || MessageHud.instance == null) && Time.realtimeSinceStartup < until) yield return null;
            if (connection != _connection || Player.m_localPlayer == null || MessageHud.instance == null) yield break;
            yield return new WaitForSeconds(3f);   // (after the game's own "welcome" messages)
            if (connection == _connection && MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text);
        }

        /// <summary>Leaving the world: the server's set goes (MismatchText stays, for the connection error shown at the main menu).</summary>
        public static void Left()
        {
            _connection++;
            ServerVersion = ServerHash = Failed = null;
            ServerProtocol = 0;
            _serverAnswered = false;
            _parts = null;
            Peers.Clear();
            _packed = null;
            PackSync.Reset();
        }

        /// <summary>`dungeon sync`: who decides the packs here, which set is in use, and (on the server) each player's version.</summary>
        public static JObject Report()
        {
            ZNet znet = ZNet.instance;
            PackSync.Set current = PackSync.Current, local = PackSync.Local;
            string role = znet == null ? "no world" : znet.IsServer() ? (znet.IsDedicated() ? "dedicated server" : znet.GetPeers().Count > 0 ? "host" : "single player or host") : "client";
            string state;
            if (znet == null || znet.IsServer()) state = "this game's own packs and looks (built in, and its config folder)";
            else if (MismatchText != null) state = $"refused: the server has {ServerVersion}, this game {Plugin.Version}";
            else if (PackSync.OffReason != null) state = "dungeons are off on this server (until reconnecting): " + PackSync.OffReason;
            else if (ServerVersion == null) state = _serverAnswered ? "the server has no Quad's Dungeons" : "connecting";
            else if (PackSync.Waiting) state = $"waiting for the server's set ({Time.realtimeSinceStartup - _expectedSince:0} s): no packs meanwhile";
            else if (PackSync.SameAsServer) state = "the server's set, which is this game's own (not sent)";
            else state = "the server's set";
            var report = new JObject
            {
                ["version"] = Plugin.Version,
                ["protocol"] = Protocol,
                ["role"] = role,
                ["packsFrom"] = state,
                ["set"] = PackSync.Waiting ? "(none yet)" : current.Hash,
                ["ownSet"] = local.Hash,
                ["packs"] = new JArray(current.Packs.Values.OrderBy(e => e.Name).Select(e => $"{e.Name} ({PackSync.Describe(e)}, {e.Hash})")),
                ["looks"] = new JArray(current.Looks.Values.OrderBy(e => e.Name).Select(e => $"{e.Name} ({PackSync.Describe(e)}, {e.Hash})")),
                ["configFolder"] = PackSync.ConfigFolder,
                ["generation"] = PackSync.Generation,
            };
            if (current.LeftOut.Count > 0) report["leftOut"] = new JArray(current.LeftOut);
            if (znet != null && !znet.IsServer())
            {
                report["serverVersion"] = ServerVersion ?? "(none)";
                if (ServerProtocol > 0) report["serverProtocol"] = ServerProtocol;
                if (ServerHash != null) report["serverSet"] = ServerHash;
            }
            if (znet != null && znet.IsServer())
            {
                var players = new JArray();
                foreach (ZNetPeer peer in znet.GetPeers())
                {
                    Peer p = Peers.FirstOrDefault(x => x.Rpc == peer.m_rpc);
                    players.Add(new JObject
                    {
                        ["player"] = peer.m_playerName,
                        ["version"] = p?.Version ?? "(none)",
                        ["protocol"] = p?.Protocol,
                        ["needsUpdate"] = p != null && p.Version != null && p.Protocol < Protocol ? true : (bool?)null,
                        ["set"] = p?.Hash,
                        ["sent"] = p?.Sent ?? "(nothing)",
                    });
                }
                report["players"] = players;
                if (Refused.Count > 0) report["refused"] = new JArray(Refused);
            }
            report["settings"] = new JObject
            {
                ["ResetDays"] = Plugin.ResetDays.Value,
                ["fromServer"] = Plugin.Synced != null && Plugin.Synced.FromServer,
            };
            return report;
        }
    }

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    internal static class ZNet_OnNewConnection_Sync
    {
        private static void Postfix(ZNet __instance, ZNetPeer peer) => SyncNet.OnNewConnection(__instance, peer);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    internal static class ZNet_RPC_PeerInfo_Sync
    {
        // Refuses only for this mod's own reason; a PeerInfo another mod already refused is left to it.
        private static bool Prefix(ZNet __instance, ZRpc rpc, bool __runOriginal) => !__runOriginal || SyncNet.Admit(__instance, rpc);

        private static void Postfix(ZNet __instance, ZRpc rpc) => SyncNet.AfterPeerInfo(__instance, rpc);
    }

    // Under the game's "incompatible version": which Dungeons versions (when it was this mod's check that refused).
    [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
    internal static class FejdStartup_ShowConnectError_Sync
    {
        private static void Postfix(FejdStartup __instance, ZNet.ConnectionStatus statusOverride)
        {
            ZNet.ConnectionStatus status = statusOverride == ZNet.ConnectionStatus.None ? ZNet.GetConnectionStatus() : statusOverride;
            if (SyncNet.MismatchText == null || status != ZNet.ConnectionStatus.ErrorVersion || __instance.m_connectionFailedError == null) return;
            if (!__instance.m_connectionFailedError.text.Contains(SyncNet.MismatchText))
                __instance.m_connectionFailedError.text += "\n\n" + SyncNet.MismatchText;
        }
    }

    // A host or server reads its packs as its world starts (so the log says at once what its config folder added or left out).
    [HarmonyPatch(typeof(ZNet), "Awake")]
    internal static class ZNet_Awake_Sync
    {
        private static void Postfix(ZNet __instance)
        {
            if (!__instance.IsServer()) return;
            try { _ = PackSync.Local; } catch (Exception e) { Plugin.Log.LogWarning("Reading the packs: " + e.Message); }
        }
    }

    // Leaving a world: the server's packs go, and this game's own are read again next time.
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    internal static class ZNetScene_Shutdown_Sync
    {
        private static void Postfix() => SyncNet.Left();
    }
}
