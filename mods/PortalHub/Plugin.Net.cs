using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PortalHub
{
    /// <summary>One portal as the menu sees it.</summary>
    internal class PortalInfo
    {
        public string Id, Name, Dest;
        public Vector3 Pos;
    }

    /// <summary>
    /// The host's game knows every portal. Players ask it for the list, and ask it to link two portals; the host stores the link
    /// on the portal and keeps the game's own portal connection pointing at it.
    /// </summary>
    public partial class Plugin
    {
        private const string RpcRequest = "DH_PortalsRequest", RpcList = "DH_PortalsList", RpcSet = "DH_PortalsSet", RpcMsg = "DH_PortalsMsg";
        private const string DestKey = "dh_dest";
        // A portal's own name for links and favourites. The game gives every object a new ZDOID each time the world loads, so a ZDOID
        // saved in a link points at some unrelated object after a restart. The host gives each portal a random id stored on it instead.
        private const string PidKey = "dh_pid";

        // Who made a portal's link (their character id), so the host can keep checking that they may lead into the place it goes.
        private const string DestByKey = "dh_destby";

        /// <summary>The id the menu, links and favourites use for a portal: its stored id, or (until the host has given it one) its ZDOID.</summary>
        internal static string PortalId(ZDO zdo)
        {
            string pid = Stored(zdo, PidKey);
            return pid.Length > 0 ? pid : Key(zdo.m_uid);
        }

        // Host only: make sure the portal has a stored id.
        private static string EnsurePid(ZDO zdo)
        {
            string pid = Stored(zdo, PidKey);
            if (pid.Length > 0) return pid;
            pid = System.Guid.NewGuid().ToString("N").Substring(0, 12);
            Write(zdo, PidKey, pid);
            return pid;
        }

        // Host only: the portal an id names (a stored id, or a ZDOID of this session). With byPid (made once per pass) no list is built.
        private static ZDO ResolvePortal(string id, Dictionary<string, ZDO> byPid = null)
        {
            if (string.IsNullOrEmpty(id) || ZDOMan.instance == null) return null;
            if (id.IndexOf(':') >= 0)
            {
                ZDO byUid = TryParse(id, out ZDOID uid) ? ZDOMan.instance.GetZDO(uid) : null;
                return byUid != null && IsPortalZdo(byUid) ? byUid : null;
            }
            if (byPid != null) return byPid.TryGetValue(id, out ZDO found) ? found : null;
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
                if (Stored(zdo, PidKey) == id) return zdo;
            return null;
        }

        // ---- writing to a portal (host) ----
        // A portal belongs to the game nearest it (its owner), which writes its name (TeleportWorld.RPC_SetTag) and sends it on. If the
        // host took it over to write its own keys while that player is on, a rename made at the same moment could be lost. So, as the game
        // does with its own portal connection (Game.SetConnection), the host asks the owner to write, and writes itself only when it owns the
        // portal or nobody connected does. Until the owner's write comes back, the host reads what it asked for from here.

        private const string RpcWrite = "DH_PortalsWrite";
        private const float WriteTimeout = 4f; // an owner with PortalHub 1.4.0 or older ignores the request: after this the host writes itself

        private class PendingWrite
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public float At;
        }

        private static readonly Dictionary<ZDOID, PendingWrite> _pendingWrites = new Dictionary<ZDOID, PendingWrite>();

        /// <summary>A portal's value: what the host has asked its owner to write, if that hasn't arrived yet, else what is saved.</summary>
        private static string Stored(ZDO zdo, string key)
        {
            if (_pendingWrites.Count > 0 && _pendingWrites.TryGetValue(zdo.m_uid, out PendingWrite w) && w.Values.TryGetValue(key, out string v)) return v;
            return zdo.GetString(key, "");
        }

        // Host: write these keys (key, value, key, value...) on a portal, through its owner when another connected game owns it.
        private static void Write(ZDO zdo, params string[] keyValues)
        {
            long me = ZDOMan.GetSessionID(), owner = zdo.GetOwner();
            if (owner == me || owner == 0L || ZNet.instance == null || ZNet.instance.GetPeer(owner) == null)
            {
                WriteHere(zdo, keyValues);
                return;
            }
            if (!_pendingWrites.TryGetValue(zdo.m_uid, out PendingWrite w)) _pendingWrites[zdo.m_uid] = w = new PendingWrite();
            for (int i = 0; i + 1 < keyValues.Length; i += 2) w.Values[keyValues[i]] = keyValues[i + 1];
            w.At = Time.time;
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, RpcWrite, zdo.m_uid, string.Join("|", keyValues));
        }

        // Take the portal over and write (the host owns it, or no connected game does), as Game.SetConnection does.
        private static void WriteHere(ZDO zdo, string[] keyValues)
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
            _pendingWrites.TryGetValue(zdo.m_uid, out PendingWrite w);
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
            {
                zdo.Set(keyValues[i], keyValues[i + 1]);
                w?.Values.Remove(keyValues[i]);
            }
            if (w != null && w.Values.Count == 0) _pendingWrites.Remove(zdo.m_uid);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }

        // Host, each pass: forget requests the owner has carried out; write the rest itself once the owner has had its time.
        private static void SettlePending()
        {
            if (_pendingWrites.Count == 0) return;
            foreach (ZDOID id in _pendingWrites.Keys.ToList())
            {
                PendingWrite w = _pendingWrites[id];
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null) { _pendingWrites.Remove(id); continue; }
                foreach (string key in w.Values.Keys.ToList())
                    if (zdo.GetString(key, "") == w.Values[key]) w.Values.Remove(key);
                if (w.Values.Count == 0) { _pendingWrites.Remove(id); continue; }
                if (Time.time - w.At < WriteTimeout) continue;
                var keyValues = new List<string>();
                foreach (KeyValuePair<string, string> kv in w.Values) { keyValues.Add(kv.Key); keyValues.Add(kv.Value); }
                _pendingWrites.Remove(id);
                WriteHere(zdo, keyValues.ToArray());
            }
        }

        // The owner's game: the host asks it to write PortalHub's keys on a portal it owns.
        private void OnWrite(long sender, ZDOID id, string payload)
        {
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (server == null || sender != server.m_uid) return; // only the host decides links and ids
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            if (zdo == null || !zdo.IsOwner()) return; // no longer ours: the host writes it itself after a moment
            string[] f = (payload ?? "").Split('|');
            for (int i = 0; i + 1 < f.Length; i += 2)
                if (f[i] == PidKey || f[i] == DestKey || f[i] == DestByKey) zdo.Set(f[i], f[i + 1]);
            ZDOMan.instance.ForceSendZDO(id);
        }

        internal List<PortalInfo> Portals = new List<PortalInfo>();
        internal float PortalsAt = -999f;
        private float _nextAsk;
        private float _nextNearCheck;
        private ZRoutedRpc _registeredOn;

        internal static string Key(ZDOID id) => id.UserID.ToString(CultureInfo.InvariantCulture) + ":" + id.ID.ToString(CultureInfo.InvariantCulture);

        internal static bool TryParse(string key, out ZDOID id)
        {
            id = ZDOID.None;
            string[] parts = (key ?? "").Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user)
                && uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint num))
            {
                id = new ZDOID(user, num);
                return true;
            }
            return false;
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        private void UpdateNetwork()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _awakeFrame + 2) return; // after a hot reload the old copy removes its handlers first

            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                Portals = new List<PortalInfo>(); // a new world: the last one's portals must not show on its map
                PortalsAt = -999f;
                _rowsKey = null;
                _pendingWrites.Clear();
                UnregisterRpc(); // Valheim throws if a name is registered twice
                rpc.Register<string>(RpcRequest, OnRequest);
                rpc.Register<string>(RpcList, OnList);
                rpc.Register<string>(RpcSet, OnSet);
                rpc.Register<string>(RpcMsg, OnMsg);
                rpc.Register<ZDOID, string>(RpcWrite, OnWrite);
            }

            // While a portal is close by (or the menu is open), keep the list fresh.
            if (Time.time >= _nextAsk && Player.m_localPlayer != null && (WindowOpen || NearAPortalThrottled()))
            {
                _nextAsk = Time.time + (WindowOpen ? 5f : 20f);
                RequestList();
            }
        }

        // Looking for portals walks the scene, so at most once a second (it would run every frame while none is near).
        private bool NearAPortalThrottled()
        {
            if (Time.time < _nextNearCheck) return false;
            _nextNearCheck = Time.time + 1f;
            return NearAPortal();
        }

        private static bool NearAPortal()
        {
            Player p = Player.m_localPlayer;
            foreach (TeleportWorld t in UnityEngine.Object.FindObjectsOfType<TeleportWorld>())
                if (t != null && (t.transform.position - p.transform.position).sqrMagnitude < 12f * 12f) return true;
            return false;
        }

        private static void UnregisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            if (table == null) return;
            foreach (string name in new[] { RpcRequest, RpcList, RpcSet, RpcMsg, RpcWrite }) table.Remove(name.GetStableHashCode());
        }

        // ---- the list ----

        internal void RequestList()
        {
            if (ZRoutedRpc.instance == null) return;
            if (IsServer) { SetList(BuildList(CharacterOf(ZDOMan.GetSessionID()))); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRequest, ""); // to the server
        }

        /// <param name="character">who asks: with HideWardedPortals on, portals in others' protected areas are left out for them</param>
        private static string BuildList(long character)
        {
            var lines = new List<string>();
            bool hide = Instance != null && Instance.HideWarded;
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
            {
                Vector3 p = zdo.GetPosition();
                if (hide && !MayUse(p, character)) continue;
                string name = (zdo.GetString(ZDOVars.s_tag) ?? "").Replace('|', ' ').Replace('\n', ' ');
                lines.Add(string.Join("|", new[]
                {
                    EnsurePid(zdo), name,
                    p.x.ToString("0.#", CultureInfo.InvariantCulture), p.y.ToString("0.#", CultureInfo.InvariantCulture), p.z.ToString("0.#", CultureInfo.InvariantCulture),
                    Stored(zdo, DestKey),
                }));
            }
            return string.Join("\n", lines.ToArray());
        }

        private void SetList(string payload)
        {
            var list = new List<PortalInfo>();
            foreach (string line in (payload ?? "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = line.Split('|');
                if (f.Length < 6) continue;
                list.Add(new PortalInfo
                {
                    Id = f[0], Name = f[1], Dest = f[5],
                    Pos = new Vector3(Num(f[2]), Num(f[3]), Num(f[4])),
                });
            }
            Portals = list;
            PortalsAt = Time.time;
            _rowsKey = null;
        }

        private static float Num(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        private void OnRequest(long sender, string _)
        {
            if (!IsServer) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcList, BuildList(CharacterOf(sender)));
        }

        private void OnList(long sender, string payload) => SetList(payload);

        internal PortalInfo Find(string id) => Portals.FirstOrDefault(p => p.Id == id);

        // ---- linking ----

        /// <summary>Ask the host to make <paramref name="portal"/> lead to <paramref name="dest"/> (empty = unlink).</summary>
        internal void SetDestination(string portal, string dest, bool both)
        {
            string payload = portal + "|" + dest + "|" + (both ? "1" : "0");
            if (IsServer) ApplySet(ZDOMan.GetSessionID(), payload);
            else ZRoutedRpc.instance.InvokeRoutedRPC(RpcSet, payload);
            _nextAsk = Time.time + 0.8f; // fetch the updated list shortly
        }

        private void OnSet(long sender, string payload)
        {
            if (IsServer) ApplySet(sender, payload);
        }

        private void OnMsg(long sender, string text) => Tell(text);

        /// <summary>
        /// Host: a message to the player who asked for something. It is sent as a translation key ($portalhub_...), and each player's game (MessageHud)
        /// shows it in their own language. (A host with an older PortalHub sends English sentences: those are shown as they come.)
        /// </summary>
        private static void TellPlayer(long to, string text)
        {
            if (to == ZDOMan.GetSessionID()) Instance?.Tell(text);
            else ZRoutedRpc.instance?.InvokeRoutedRPC(to, RpcMsg, text);
        }

        /// <summary>
        /// Host only: change a link, for the player who asked, only where their wards let them (the host checks, whatever their game says):
        /// the portal they change and the portal it leads to must both be free to them. A link into someone else's protected area would let
        /// anyone walk into it (the game's teleport never asks the ward), so it is refused whatever HideWardedPortals says.
        /// </summary>
        private static void ApplySet(long sender, string payload)
        {
            string[] f = payload.Split('|');
            if (f.Length < 3) return;
            ZDO portal = ResolvePortal(f[0]);
            if (portal == null) return;
            long who = CharacterOf(sender);
            if (!MayUse(portal.GetPosition(), who)) { TellPlayer(sender, "$portalhub_msg_warded"); return; }

            bool unlink = string.IsNullOrEmpty(f[1]);
            ZDO target = null;
            if (!unlink)
            {
                target = ResolvePortal(f[1]);
                if (target == null || target == portal) return;
                if (!MayUse(target.GetPosition(), who)) { TellPlayer(sender, "$portalhub_msg_warded"); return; }
            }

            SetDest(portal, unlink ? "" : EnsurePid(target), who);
            if (!unlink && f[2] == "1") SetDest(target, EnsurePid(portal), who);
            if (unlink) Connect(portal, ZDOID.None); // the game's own name matching takes over again on its next pass
            ApplyDestinations();
        }

        private static bool IsPortalZdo(ZDO zdo) => Game.instance != null && Game.instance.PortalPrefabHash.Contains(zdo.GetPrefab());

        private static void SetDest(ZDO zdo, string dest, long by) =>
            Write(zdo, DestKey, dest, DestByKey, dest.Length > 0 ? by.ToString(CultureInfo.InvariantCulture) : "");

        /// <summary>
        /// Host: who made a portal's link, or 0 for a link made by PortalHub 1.4.0 or older (who made it wasn't kept). Those are left alone:
        /// guessing (the portal's builder, say) would drop a friend's link that was fine.
        /// </summary>
        private static long LinkedBy(ZDO portal)
        {
            string by = Stored(portal, DestByKey);
            return by.Length > 0 && long.TryParse(by, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : 0L;
        }

        private static readonly System.Reflection.MethodInfo SetConnectionMethod = AccessTools.Method(typeof(Game), "SetConnection");

        private static void Connect(ZDO portal, ZDOID target)
        {
            if (Game.instance == null || SetConnectionMethod == null) return;
            SetConnectionMethod.Invoke(Game.instance, new object[] { portal, target, false });
        }

        /// <summary>
        /// Host only, every few seconds (the game's own portal pass calls this first): portals with a chosen destination are
        /// connected straight to it. The choice is dropped if that portal is gone, or if it now stands in a protected area its link's maker
        /// may not enter (a ward built or switched on since).
        /// </summary>
        internal static void ApplyDestinations()
        {
            if (!IsServer || Instance == null || !Instance.Enabled || ZDOMan.instance == null) return;
            SettlePending();
            List<ZDO> portals = ZDOMan.instance.GetPortalList(); // once per pass, with every portal by its id
            var byPid = new Dictionary<string, ZDO>(portals.Count);
            foreach (ZDO portal in portals) byPid[EnsurePid(portal)] = portal;

            foreach (ZDO portal in portals)
            {
                string dest = Stored(portal, DestKey);
                if (dest.Length == 0) continue;

                // A link saved by version 1.1.0 or older names a ZDOID from an earlier session, which now means some other object:
                // drop it (the portal goes back to pairing by name) rather than send players somewhere random.
                ZDO target = dest.IndexOf(':') >= 0 ? null : ResolvePortal(dest, byPid);
                long by = LinkedBy(portal);
                if (target == null || target == portal || (by != 0L && !MayUse(target.GetPosition(), by)))
                {
                    SetDest(portal, "", 0L);
                    Connect(portal, ZDOID.None);
                    continue;
                }
                if (portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != target.m_uid) Connect(portal, target.m_uid);
            }
        }

        internal static bool HasDest(ZDO zdo) => Stored(zdo, DestKey).Length > 0;

        // ---- who may change what (worked out on the host) ----

        /// <summary>
        /// Host: the character (its player id) playing on the connection a message came from, from the connection itself, never from what the
        /// message says. 0 when it can't be told.
        /// </summary>
        private static long CharacterOf(long sender)
        {
            if (sender == ZDOMan.GetSessionID()) return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
            ZNetPeer peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
            if (peer == null) return 0L;
            ZDO character = !peer.m_characterID.IsNone() ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            long id = character != null ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
            return id != 0L ? id : peer.m_playerID;
        }

        private static Dictionary<int, float> _wardRadius; // prefab hash -> radius of every piece that is a ward (the game's and other mods')
        private static readonly List<ZDO> _near = new List<ZDO>(); // (reused: MayUse runs for many portals at a time)

        /// <summary>
        /// Host: may this character change things at this spot? The game's own rule (PrivateArea.CheckAccess), worked out from the saved
        /// wards, since the host may not have the area loaded (a dedicated server never does): free where no switched-on ward covers the spot;
        /// where one does, allowed if any ward covering it was built by them or has them on its list.
        /// </summary>
        private static bool MayUse(Vector3 point, long character)
        {
            if (ZNetScene.instance == null || ZDOMan.instance == null) return true;
            if (_wardRadius == null)
            {
                _wardRadius = new Dictionary<int, float>();
                foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
                {
                    PrivateArea ward = prefab != null ? prefab.GetComponent<PrivateArea>() : null;
                    if (ward != null) _wardRadius[prefab.name.GetStableHashCode()] = ward.m_radius;
                }
            }
            _near.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(point), new SimulationDistance(1, 0), _near, null); // its zone and the ones around it
            bool covered = false;
            foreach (ZDO zdo in _near)
            {
                if (!_wardRadius.TryGetValue(zdo.GetPrefab(), out float radius) || !zdo.GetBool(ZDOVars.s_enabled)) continue;
                if (Utils.DistanceXZ(zdo.GetPosition(), point) >= radius) continue;
                if (character != 0L && (zdo.GetLong(ZDOVars.s_creator, 0L) == character || Permitted(zdo, character))) return true;
                covered = true;
            }
            return !covered;
        }

        /// <summary>Is the character on this ward's list? (Kept as the game keeps it: a count, then pu_id0, pu_id1...)</summary>
        private static bool Permitted(ZDO ward, long character)
        {
            int count = ward.GetInt(ZDOVars.s_permitted, 0);
            for (int i = 0; i < count; i++)
                if (ward.GetLong("pu_id" + i, 0L) == character) return true;
            return false;
        }
    }
}
