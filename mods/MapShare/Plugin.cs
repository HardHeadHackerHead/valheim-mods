using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MapShare
{
    /// <summary>
    /// Shares the part of the map you have explored with everyone else in the world, live, as you run through the fog.
    /// (The cartography table only does it by hand: you write your map to the table and others read it, one visit at a time.)
    ///   * New cells you uncover are sent to the other players a couple of seconds later and appear on their map.
    ///   * Someone who joins gets the whole map from one of the players already there, and sends theirs back.
    /// Only the explored area is shared, not pins. Players without this mod simply do not take part.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.mapshare";
        public const string OldGuid = "com.dhack.mapshare"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "MapShare";
        public const string Version = "1.1.1";

        private const string RpcCells = "DHack_MapCells";   // newly explored cells
        private const string RpcHello = "DHack_MapHello";   // "I just joined: send me the map"
        private const string RpcFull = "DHack_MapFull";     // a piece of a whole map

        internal static Plugin Instance;
        internal static readonly List<int> Pending = new List<int>(); // cells we explored that we have not sent yet

        internal static DHack.Shared.ServerSettings Synced; // settings the server decides in multiplayer
        private ConfigEntry<bool> _send, _receive, _allowed;
        private ConfigEntry<float> _interval;
        private Harmony _harmony;

        private static readonly FieldInfo Explored = AccessTools.Field(typeof(Minimap), "m_explored");
        private static readonly FieldInfo ExploredOthers = AccessTools.Field(typeof(Minimap), "m_exploredOthers");
        private static readonly FieldInfo Fog = AccessTools.Field(typeof(Minimap), "m_fogTexture");
        private static readonly Func<Minimap, int, int, bool> ExploreOthers =
            (Func<Minimap, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Minimap, int, int, bool>), AccessTools.Method(typeof(Minimap), "ExploreOthers", new[] { typeof(int), typeof(int) }));

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            Instance = this;
            _send = Config.Bind("General", "ShareMyMap", true, "Send the parts of the map you uncover to the other players in the world.");
            _receive = Config.Bind("General", "ReceiveSharedMap", true, "Show the parts of the map other players uncover on your own map.");
            _interval = Config.Bind("General", "SendInterval", 2f, "Seconds between sending newly uncovered map cells.");
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            _allowed = Synced.Add(Config.Bind("General", "AllowSharing", true,
                "Players with this mod share the map they uncover with each other. A server that wants everyone to explore for themselves turns " +
                "this off. In multiplayer the server's value applies."));

            _awakeFrame = Time.frameCount;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            Logger.LogInfo($"{Name} {Version} loaded");

            if (Player.m_localPlayer != null && Chat.instance != null) // a hot reload while in a world
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            UnregisterRpc();
            Pending.Clear();
            _incoming.Clear();
            Synced?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ---- networking setup (same pattern as our other mods: re-register on every world, and survive hot reloads) -------------

        private int _awakeFrame;
        private ZRoutedRpc _registeredOn;
        private bool _helloPending;
        private float _nextSend, _helloAt;

        private bool Sending => _send.Value && _allowed.Value;
        private bool Receiving => _receive.Value && _allowed.Value;

        private void Update()
        {
            Synced?.Update(); // notices joining and leaving a server, for the settings it decides
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _awakeFrame + 2) return; // after a reload, let the old copy remove its handlers first

            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                UnregisterRpc(); // Valheim throws if a name is registered twice
                rpc.Register<ZPackage>(RpcCells, OnCells);
                rpc.Register(RpcHello, OnHello);
                rpc.Register<ZPackage>(RpcFull, OnFull);
                _helloPending = true;
                _helloAt = Time.unscaledTime + 4f; // let the world and the map finish loading
                Pending.Clear();
            }

            if (Minimap.instance == null || Player.m_localPlayer == null) return;

            if (_helloPending && Time.unscaledTime >= _helloAt)
            {
                _helloPending = false;
                if (Receiving) Send(RpcHello, null);                            // ask for the map
                StartCoroutine(SendOwnMapLater());                              // and give ours
            }

            if (Sending && Pending.Count > 0 && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + Mathf.Max(0.5f, _interval.Value);
                FlushPending();
            }
            else if (!Sending) Pending.Clear();
        }

        private static void UnregisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            if (table == null) return;
            foreach (string name in new[] { RpcCells, RpcHello, RpcFull }) table.Remove(name.GetStableHashCode());
        }

        private void Send(string rpc, ZPackage package, long target = 0L)
        {
            if (ZRoutedRpc.instance == null || Player.m_localPlayer == null) return;
            try
            {
                if (package == null) ZRoutedRpc.instance.InvokeRoutedRPC(target, rpc);
                else ZRoutedRpc.instance.InvokeRoutedRPC(target, rpc, package);
            }
            catch (Exception e) { Logger.LogWarning("Could not share the map: " + e.Message); }
        }

        // ---- live updates -----------------------------------------------------------------------------------------

        private void FlushPending()
        {
            while (Pending.Count > 0)
            {
                int take = Mathf.Min(Pending.Count, 4000);
                var package = new ZPackage();
                package.Write(1);
                package.Write(Minimap.instance.m_textureSize);
                package.Write(take);
                for (int i = 0; i < take; i++) package.Write(Pending[i]);
                Pending.RemoveRange(0, take);
                Send(RpcCells, package);
            }
        }

        private void OnCells(long sender, ZPackage package)
        {
            if (!Receiving || Minimap.instance == null || sender == ZDOMan.GetSessionID()) return;
            try
            {
                package.ReadInt(); // version
                int size = package.ReadInt();
                int count = package.ReadInt();
                if (size != Minimap.instance.m_textureSize || count < 0 || count > 200000) return; // a different map size: not ours to merge

                bool any = false;
                for (int i = 0; i < count; i++)
                {
                    int cell = package.ReadInt();
                    if (cell < 0 || cell >= size * size) continue;
                    if (ExploreOthers(Minimap.instance, cell % size, cell / size)) any = true;
                }
                if (any) Finish();
            }
            catch (Exception e) { Logger.LogWarning("Bad map update: " + e.Message); }
        }

        /// <summary>Show the new cells, and hide the "shared map" icon the game raises each time it receives shared cells.</summary>
        private static void Finish()
        {
            var fog = Fog.GetValue(Minimap.instance) as Texture2D;
            fog?.Apply();
            if (Minimap.instance.m_sharedMapHint != null) Minimap.instance.m_sharedMapHint.SetActive(false);
        }

        // ---- the whole map, for someone who just joined -------------------------------------------------------------

        private readonly Dictionary<long, Assembly_> _incoming = new Dictionary<long, Assembly_>();
        private class Assembly_ { public int Total; public byte[][] Parts; public int Have; public float Started; }

        // A whole map is a few hundred KB at most (one bit per cell of a 2048 x 2048 map, before compressing): anything far bigger is
        // not a map. A set of pieces that never completes (its sender left) is forgotten after a minute.
        private const int MaxParts = 64, MaxPiece = 64 * 1024, MaxSets = 16;
        private const float SetTimeout = 60f;

        // Every player who shares their map answers a newcomer. (Picking just one could pick a player without MapShare, or with
        // sharing off, and then nobody answered.) The maps merge, so getting several does no harm; a map is a few KB.
        private void OnHello(long sender)
        {
            if (!Sending || sender == ZDOMan.GetSessionID() || Minimap.instance == null) return;
            StartCoroutine(SendFullMap(sender));
        }

        private IEnumerator SendOwnMapLater()
        {
            yield return new WaitForSeconds(12f); // after the map we asked for has arrived
            if (Sending && Minimap.instance != null) yield return SendFullMap(0L);
        }

        /// <summary>Everything explored (by us or by others), as a compressed bit array, sent in small pieces.</summary>
        private IEnumerator SendFullMap(long target)
        {
            Minimap map = Minimap.instance;
            var mine = (BitArray)Explored.GetValue(map);
            var others = (BitArray)ExploredOthers.GetValue(map);
            int n = mine.Length;
            var bits = new byte[(n + 7) / 8];
            const int slice = 250000;
            for (int start = 0; start < n; start += slice) // a slice per frame, so the game does not hitch
            {
                int end = Mathf.Min(n, start + slice);
                for (int i = start; i < end; i++) if (mine[i] || others[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
                yield return null;
            }

            byte[] packed = Utils.Compress(bits);
            const int chunk = 30000;
            int parts = (packed.Length + chunk - 1) / chunk;
            Logger.LogInfo($"Sending the whole map to {(target == 0L ? "everyone" : "one player")}: {packed.Length / 1024} KB in {parts} piece(s)");
            int id = UnityEngine.Random.Range(1, int.MaxValue);
            for (int i = 0; i < parts; i++)
            {
                int length = Mathf.Min(chunk, packed.Length - i * chunk);
                var piece = new byte[length];
                Buffer.BlockCopy(packed, i * chunk, piece, 0, length);
                var package = new ZPackage();
                package.Write(id);
                package.Write(i);
                package.Write(parts);
                package.Write(map.m_textureSize);
                package.Write(piece);
                Send(RpcFull, package, target);
                yield return null; // one piece per frame
            }
        }

        private void OnFull(long sender, ZPackage package)
        {
            if (!Receiving || Minimap.instance == null || sender == ZDOMan.GetSessionID()) return;
            try
            {
                int id = package.ReadInt(), index = package.ReadInt(), total = package.ReadInt(), size = package.ReadInt();
                byte[] piece = package.ReadByteArray();
                if (size != Minimap.instance.m_textureSize || total <= 0 || total > MaxParts || index < 0 || index >= total) return;
                if (piece == null || piece.Length > MaxPiece) return;

                foreach (long stale in _incoming.Where(kv => Time.unscaledTime - kv.Value.Started > SetTimeout).Select(kv => kv.Key).ToList())
                    _incoming.Remove(stale);
                long key = sender * 31 + id;
                if (!_incoming.TryGetValue(key, out Assembly_ a))
                {
                    if (_incoming.Count >= MaxSets) return; // too many maps arriving at once: skip this one (live updates still come)
                    _incoming[key] = a = new Assembly_ { Total = total, Parts = new byte[total][], Started = Time.unscaledTime };
                }
                if (a.Total != total) return;
                if (a.Parts[index] == null) { a.Parts[index] = piece; a.Have++; }
                if (a.Have < a.Total) return;

                _incoming.Remove(key);
                byte[] packed = a.Parts.SelectMany(p => p).ToArray();
                byte[] bits = Unpack(packed, (size * size + 7) / 8);
                if (bits == null) { Logger.LogWarning("A shared map was not the size of this world's map: ignored"); return; }
                StartCoroutine(Merge(bits));
            }
            catch (Exception e) { Logger.LogWarning("Bad map piece: " + e.Message); }
        }

        /// <summary>
        /// The game's own compression (gzip), unpacked only up to the size a map of this world is, so a sender can't make this game unpack
        /// something huge. Null when it isn't exactly that size.
        /// </summary>
        private static byte[] Unpack(byte[] packed, int expected)
        {
            using (var input = new System.IO.MemoryStream(packed))
            using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
            {
                var bits = new byte[expected];
                int have = 0, read;
                while (have < expected && (read = gzip.Read(bits, have, expected - have)) > 0) have += read;
                if (have < expected || gzip.ReadByte() >= 0) return null; // too short, or more than a map
                return bits;
            }
        }

        /// <summary>Add everything in the received map that is not on ours yet, a slice per frame.</summary>
        private IEnumerator Merge(byte[] bits)
        {
            Minimap map = Minimap.instance;
            var mine = (BitArray)Explored.GetValue(map);
            var others = (BitArray)ExploredOthers.GetValue(map);
            int size = map.m_textureSize, n = size * size, added = 0;
            if (bits.Length * 8 < n) yield break;

            const int slice = 150000;
            for (int start = 0; start < n; start += slice)
            {
                int end = Mathf.Min(n, start + slice);
                for (int i = start; i < end; i++)
                {
                    if ((bits[i >> 3] & (1 << (i & 7))) == 0 || mine[i] || others[i]) continue;
                    if (ExploreOthers(map, i % size, i / size)) added++;
                }
                yield return null;
            }
            if (added > 0) { Finish(); Logger.LogInfo($"Added {added} map cells shared by other players"); }
        }
    }

    // Every time the game uncovers a new cell of the fog around you, remember it so it can be sent to the others.
    [HarmonyPatch(typeof(Minimap), "Explore", new[] { typeof(int), typeof(int) })]
    internal static class Minimap_Explore
    {
        private static void Postfix(int x, int y, bool __result)
        {
            if (!__result || Plugin.Instance == null || Minimap.instance == null) return;
            if (Plugin.Pending.Count < 200000) Plugin.Pending.Add(y * Minimap.instance.m_textureSize + x);
        }
    }
}
