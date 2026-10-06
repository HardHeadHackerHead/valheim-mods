using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>The build orders: who plans what where, saved per world and shared with the other players.</summary>
    public partial class Plugin
    {
        internal class Order
        {
            public string Id, Prefab, By;
            public Vector3 Pos;
            public Quaternion Rot;
        }

        private const string Rpc = "DHack_BuildOrders";

        private readonly Dictionary<string, Order> _orders = new Dictionary<string, Order>();
        private readonly HashSet<string> _removed = new HashSet<string>(); // ids deleted, so a stale copy can't bring one back
        private string _loadedWorld, _loadedWorldName;   // the world's UID (its files are named after it), and its name
        private ZNet _worldOf;                           // the session that world belongs to
        private ZRoutedRpc _registeredOn;
        private bool _announcePending;

        internal IEnumerable<Order> AllOrders => _orders.Values;

        // ---- planning and removing --------------------------------------------------------------

        /// <summary>Record an order for the selected piece at the placement ghost's position. Returns false if it wasn't added.</summary>
        internal bool Plan(Player player, Piece piece, Vector3 pos, Quaternion rot)
        {
            string prefab = Plain(piece.gameObject.name);

            // Holding the mouse down would plan the same spot over and over.
            if (_orders.Values.Any(o => o.Prefab == prefab && (o.Pos - pos).sqrMagnitude < 0.02f && Quaternion.Angle(o.Rot, rot) < 5f))
                return false;

            var order = new Order { Id = Guid_(), Prefab = prefab, Pos = pos, Rot = rot, By = player.GetPlayerName() };
            _orders[order.Id] = order;
            _stabilityDirty = true;
            Save();
            Send("A|" + Encode(order));
            player.Message(MessageHud.MessageType.TopLeft, $"Planned: {PieceName(prefab)}  ({_orders.Count} order(s))");
            return true;
        }

        internal void RemoveOrder(string id, bool broadcast, bool save = true)
        {
            if (!_orders.Remove(id) && _removed.Contains(id)) return;
            _stabilityDirty = true;
            _removed.Add(id);
            DestroyGhost(id);
            if (save) Save();
            if (broadcast) Send("R|" + id);
        }

        /// <summary>Save to disk now (for callers that removed several orders without saving each time).</summary>
        internal void SaveOrders() => Save();

        private static string Guid_() => System.Guid.NewGuid().ToString("N").Substring(0, 10);

        // ---- text format (used for the save file and for messages) --------------------------------

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Encode(Order o) =>
            string.Join("|", new[] { o.Id, o.Prefab, F(o.Pos.x), F(o.Pos.y), F(o.Pos.z), F(o.Rot.x), F(o.Rot.y), F(o.Rot.z), F(o.Rot.w), o.By ?? "" });

        private static Order Decode(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 10) return null;
            try
            {
                float P(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
                return new Order
                {
                    Id = f[0], Prefab = f[1],
                    Pos = new Vector3(P(2), P(3), P(4)),
                    Rot = new Quaternion(P(5), P(6), P(7), P(8)),
                    By = f[9],
                };
            }
            catch (Exception) { return null; }
        }

        private void MergeOrder(Order o)
        {
            if (o == null || _removed.Contains(o.Id) || _orders.ContainsKey(o.Id)) return;
            _orders[o.Id] = o;
        }

        // ---- saving (one file per world, next to the other config files) ---------------------------

        /// <summary>
        /// True once we know which world this session is in. The game keeps the world in a static field, so right after joining a server it
        /// still holds the last world until the server's answer arrives; the local player only appears after that, so we wait for it.
        /// </summary>
        internal bool WorldKnown => ZNet.instance != null && ZNet.instance == _worldOf && _loadedWorld != null;

        private static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

        private static string NamePath(string world)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) world = world.Replace(c, '_');
            return Path.Combine(Paths.ConfigPath, "BuildOrders", world + ".txt");
        }

        private string SavePath() => NamePath(_loadedWorld ?? "world");

        private void EnsureWorldLoaded()
        {
            if (ZNet.instance == null || ZNet.instance == _worldOf) return;
            if (Player.m_localPlayer == null || ZNet.World == null) return; // not in the world yet: what the game says may still be the last world
            _worldOf = ZNet.instance;
            string world = ZNet.World.m_uid.ToString(CultureInfo.InvariantCulture);
            if (world == _loadedWorld) return;

            // Joined a different world: start from that world's saved file.
            _loadedWorld = world;
            _loadedWorldName = ZNet.World.m_name ?? "";
            DropPlacing();
            ForgetWorld();
            DestroyAllGhosts();
            _orders.Clear();
            _removed.Clear();
            AdoptNamedFiles();
            LoadPendingLevels();
            try
            {
                string path = SavePath();
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("T|")) _removed.Add(line.Substring(2));
                    else MergeOrder(Decode(line));
                }
            }
            catch (Exception e) { Logger.LogWarning("Could not read saved build orders: " + e.Message); }
        }

        /// <summary>
        /// Files from before worlds were told apart by their UID are named after the world. The first time a world is loaded it takes over the
        /// files of its name that no world has taken yet (moved, not copied, so another world of the same name does not get them too).
        /// </summary>
        private void AdoptNamedFiles()
        {
            if (_loadedWorldName.Length == 0) return;
            try
            {
                string named = NamePath(_loadedWorldName), mine = SavePath();
                if (!File.Exists(mine) && File.Exists(named)) { File.Move(named, mine); Logger.LogInfo($"Build orders of '{_loadedWorldName}' now kept as {Path.GetFileName(mine)}"); }
                string from = Safe(_loadedWorldName + "__"), to = Safe(_loadedWorld + "__");
                foreach (string dir in new[] { PlanRecordDir, TerrainDir })
                {
                    if (!Directory.Exists(dir) || from == to) continue;
                    foreach (string file in Directory.GetFiles(dir, from + "*.json"))
                    {
                        string target = Path.Combine(dir, to + Path.GetFileName(file).Substring(from.Length));
                        if (!File.Exists(target)) File.Move(file, target);
                    }
                }
            }
            catch (Exception e) { Logger.LogWarning("Could not take over this world's older files: " + e.Message); }
        }

        private void Save()
        {
            try
            {
                string path = SavePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = _orders.Values.Select(Encode).Concat(_removed.Select(id => "T|" + id));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e) { Logger.LogWarning("Could not save build orders: " + e.Message); }
        }

        // ---- sharing with the other players -----------------------------------------------------

        private void UpdateNetwork()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }

            // After a hot reload the previous copy removes its handler at the end of the frame; wait a moment so we never
            // register first and then get our own handler removed.
            if (Time.frameCount <= _awakeFrame + 2) return;

            if (rpc != _registeredOn)
            {
                _registeredOn = rpc;
                UnregisterRpc(); // Valheim throws if a handler name is registered twice
                rpc.Register<string>(Rpc, OnMessage);
                _announcePending = true;
            }

            // Once we're really in the world, ask everyone for their orders (they answer with their full list).
            if (_announcePending && Player.m_localPlayer != null && WorldKnown)
            {
                _announcePending = false;
                Send("Q");
            }
        }

        private static void UnregisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            table?.Remove(Rpc.GetStableHashCode());
        }

        private void Send(string message, long target = 0L)
        {
            if (ZRoutedRpc.instance == null || Player.m_localPlayer == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(target, Rpc, message); }
            catch (Exception e) { Logger.LogWarning("Could not share build orders: " + e.Message); }
        }

        private void OnMessage(long sender, string message)
        {
            if (sender == ZDOMan.GetSessionID() || string.IsNullOrEmpty(message)) return;
            if (!WorldKnown && message[0] != 'B') return; // not sure yet which world we are in: never mix its orders into another world's (we ask for them all once in)
            try
            {
                switch (message[0])
                {
                    case 'A': // someone planned a piece
                        MergeOrder(Decode(message.Substring(2)));
                        Save();
                        break;

                    case 'R': // someone removed or finished one
                        string id = message.Substring(2);
                        _removed.Add(id);
                        if (_orders.Remove(id)) DestroyGhost(id);
                        Save();
                        break;

                    case 'Q': // someone joined and wants everything we know
                        Send(BuildState(), sender);
                        SendPlanRecords(sender);
                        break;

                    case 'P': // the pieces of a placed plan (so anyone can take down what was built of it)
                        OnPlanRecord(message.Substring(2));
                        break;

                    case 'B': // someone shared a blueprint with everyone
                        OnSharedBlueprint(message.Substring(2));
                        break;

                    case 'S': // here is everything someone knows
                        foreach (string line in message.Substring(1).Split('\n'))
                        {
                            if (line.StartsWith("T|")) _removed.Add(line.Substring(2));
                            else if (line.Length > 0) MergeOrder(Decode(line));
                        }
                        foreach (string gone in _removed) if (_orders.Remove(gone)) DestroyGhost(gone);
                        Save();
                        break;
                }
            }
            catch (Exception e) { Logger.LogWarning("Bad build-order message: " + e.Message); }
        }

        private string BuildState()
        {
            var sb = new StringBuilder("S");
            foreach (Order o in _orders.Values) sb.Append('\n').Append(Encode(o));
            foreach (string id in _removed) sb.Append("\nT|").Append(id);
            return sb.ToString();
        }
    }
}
