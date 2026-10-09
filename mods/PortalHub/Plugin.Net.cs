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
        private const string RpcRequest = "DH_PortalsRequest", RpcList = "DH_PortalsList", RpcSet = "DH_PortalsSet";
        private const string DestKey = "dh_dest";
        // A portal's own name for links and favourites. The game gives every object a new ZDOID each time the world loads, so a ZDOID
        // saved in a link points at some unrelated object after a restart. The host gives each portal a random id stored on it instead.
        private const string PidKey = "dh_pid";

        /// <summary>The id the menu, links and favourites use for a portal: its stored id, or (until the host has given it one) its ZDOID.</summary>
        internal static string PortalId(ZDO zdo)
        {
            string pid = zdo.GetString(PidKey, "");
            return pid.Length > 0 ? pid : Key(zdo.m_uid);
        }

        // Host only: make sure the portal has a stored id.
        private static string EnsurePid(ZDO zdo)
        {
            string pid = zdo.GetString(PidKey, "");
            if (pid.Length > 0) return pid;
            pid = System.Guid.NewGuid().ToString("N").Substring(0, 12);
            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set(PidKey, pid);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            return pid;
        }

        // Host only: the portal an id names (a stored id, or a ZDOID of this session).
        private static ZDO ResolvePortal(string id)
        {
            if (string.IsNullOrEmpty(id) || ZDOMan.instance == null) return null;
            if (id.IndexOf(':') >= 0)
            {
                ZDO byUid = TryParse(id, out ZDOID uid) ? ZDOMan.instance.GetZDO(uid) : null;
                return byUid != null && IsPortalZdo(byUid) ? byUid : null;
            }
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
                if (zdo.GetString(PidKey, "") == id) return zdo;
            return null;
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
                UnregisterRpc(); // Valheim throws if a name is registered twice
                rpc.Register<string>(RpcRequest, OnRequest);
                rpc.Register<string>(RpcList, OnList);
                rpc.Register<string>(RpcSet, OnSet);
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
            foreach (string name in new[] { RpcRequest, RpcList, RpcSet }) table.Remove(name.GetStableHashCode());
        }

        // ---- the list ----

        internal void RequestList()
        {
            if (ZRoutedRpc.instance == null) return;
            if (IsServer) { SetList(BuildList()); return; }
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcRequest, ""); // to the server
        }

        private static string BuildList()
        {
            var lines = new List<string>();
            foreach (ZDO zdo in ZDOMan.instance.GetPortalList())
            {
                Vector3 p = zdo.GetPosition();
                string name = (zdo.GetString(ZDOVars.s_tag) ?? "").Replace('|', ' ').Replace('\n', ' ');
                lines.Add(string.Join("|", new[]
                {
                    EnsurePid(zdo), name,
                    p.x.ToString("0.#", CultureInfo.InvariantCulture), p.y.ToString("0.#", CultureInfo.InvariantCulture), p.z.ToString("0.#", CultureInfo.InvariantCulture),
                    zdo.GetString(DestKey, ""),
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
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcList, BuildList());
        }

        private void OnList(long sender, string payload) => SetList(payload);

        internal PortalInfo Find(string id) => Portals.FirstOrDefault(p => p.Id == id);

        // ---- linking ----

        /// <summary>Ask the host to make <paramref name="portal"/> lead to <paramref name="dest"/> (empty = unlink).</summary>
        internal void SetDestination(string portal, string dest, bool both)
        {
            string payload = portal + "|" + dest + "|" + (both ? "1" : "0");
            if (IsServer) ApplySet(payload);
            else ZRoutedRpc.instance.InvokeRoutedRPC(RpcSet, payload);
            _nextAsk = Time.time + 0.8f; // fetch the updated list shortly
        }

        private void OnSet(long sender, string payload)
        {
            if (IsServer) ApplySet(payload);
        }

        private static void ApplySet(string payload)
        {
            string[] f = payload.Split('|');
            if (f.Length < 3) return;
            ZDO portal = ResolvePortal(f[0]);
            if (portal == null) return;

            bool unlink = string.IsNullOrEmpty(f[1]);
            ZDO target = null;
            if (!unlink)
            {
                target = ResolvePortal(f[1]);
                if (target == null || target == portal) return;
            }

            SetDest(portal, unlink ? "" : EnsurePid(target));
            if (!unlink && f[2] == "1") SetDest(target, EnsurePid(portal));
            if (unlink) Connect(portal, ZDOID.None); // the game's own name matching takes over again on its next pass
            ApplyDestinations();
        }

        private static bool IsPortalZdo(ZDO zdo) => Game.instance != null && Game.instance.PortalPrefabHash.Contains(zdo.GetPrefab());

        private static void SetDest(ZDO zdo, string dest)
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set(DestKey, dest);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
        }

        private static readonly System.Reflection.MethodInfo SetConnectionMethod = AccessTools.Method(typeof(Game), "SetConnection");

        private static void Connect(ZDO portal, ZDOID target)
        {
            if (Game.instance == null || SetConnectionMethod == null) return;
            SetConnectionMethod.Invoke(Game.instance, new object[] { portal, target, false });
        }

        /// <summary>
        /// Host only, every few seconds (the game's own portal pass calls this first): portals with a chosen destination are
        /// connected straight to it; if that portal is gone the choice is dropped.
        /// </summary>
        internal static void ApplyDestinations()
        {
            if (!IsServer || Instance == null || !Instance.Enabled || ZDOMan.instance == null) return;
            foreach (ZDO portal in ZDOMan.instance.GetPortalList())
            {
                EnsurePid(portal);
                string dest = portal.GetString(DestKey, "");
                if (dest.Length == 0) continue;

                // A link saved by version 1.1.0 or older names a ZDOID from an earlier session, which now means some other object:
                // drop it (the portal goes back to pairing by name) rather than send players somewhere random.
                ZDO target = dest.IndexOf(':') >= 0 ? null : ResolvePortal(dest);
                if (target == null) { SetDest(portal, ""); Connect(portal, ZDOID.None); continue; }
                if (portal.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != target.m_uid) Connect(portal, target.m_uid);
            }
        }

        internal static bool HasDest(ZDO zdo) => zdo.GetString(DestKey, "").Length > 0;
    }
}
