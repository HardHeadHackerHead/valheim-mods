using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace DHack.Shared
{
    /// <summary>
    /// Settings the server decides in multiplayer. A mod marks its gameplay settings with Add (rewards, ranges, timers, costs, strengths:
    /// anything a player could set for themselves to gain an edge); look-and-feel settings stay each player's own. In single player and on
    /// the host nothing changes. On a client connected to a server that has the mod, the server's values replace the player's own while
    /// they play there (the config file keeps the player's own), and come back when they leave. A server without the mod sends nothing, and
    /// the player's own values stay.
    ///
    /// Shared source (mods/Shared/ServerSettings.cs), compiled into each mod that uses it:
    ///
    ///   Synced = new ServerSettings(Guid, Config, Logger);
    ///   Reward = Synced.Add(Config.Bind("Rewards", "Percent", 100, "..."));
    ///   void Update() => Synced.Update();
    ///   void OnDestroy() => Synced.Dispose();
    /// </summary>
    internal sealed class ServerSettings : IDisposable
    {
        private readonly string _send, _ask;
        private readonly ConfigFile _config;
        private readonly BepInEx.Logging.ManualLogSource _log;
        private readonly List<ConfigEntryBase> _entries = new List<ConfigEntryBase>();
        private readonly Dictionary<ConfigEntryBase, object> _own = new Dictionary<ConfigEntryBase, object>();     // the player's values while the server's apply
        private readonly Dictionary<ConfigEntryBase, object> _server = new Dictionary<ConfigEntryBase, object>();  // the server's values
        private ZRoutedRpc _registeredOn;
        private bool _applying;

        /// <summary>True while the server's values apply (on a client connected to a server that has the mod).</summary>
        public bool FromServer => _server.Count > 0;

        public ServerSettings(string guid, ConfigFile config, BepInEx.Logging.ManualLogSource log)
        {
            _send = guid + ".settings";
            _ask = guid + ".settings.ask";
            _config = config;
            _log = log;
        }

        /// <summary>Mark a setting as the server's in multiplayer (its description says so in the config file).</summary>
        public ConfigEntry<T> Add<T>(ConfigEntry<T> entry)
        {
            if (entry == null || _entries.Contains(entry)) return entry;
            _entries.Add(entry);
            entry.SettingChanged += OnChanged;
            return entry;
        }

        /// <summary>Call every frame (the plugin's Update): it notices joining and leaving a world.</summary>
        public void Update()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == _registeredOn) return;
            Unregister();
            Restore();
            if (rpc != null) Register(rpc);
        }

        public void Dispose()
        {
            Unregister();
            Restore();
            foreach (ConfigEntryBase e in _entries) Unsubscribe(e);
            _entries.Clear();
        }

        // ---- the network ----

        private void Register(ZRoutedRpc rpc)
        {
            Forget(rpc, _send);
            Forget(rpc, _ask);
            rpc.Register<ZPackage>(_send, OnValues);
            rpc.Register(_ask, OnAsk);
            rpc.m_onNewPeer += OnNewPeer;
            _registeredOn = rpc;
        }

        private void Unregister()
        {
            if (_registeredOn == null) return;
            _registeredOn.m_onNewPeer -= OnNewPeer;
            Forget(_registeredOn, _send);
            Forget(_registeredOn, _ask);
            _registeredOn = null;
        }

        /// <summary>Remove a routed RPC by name (the game throws when a name is registered twice, as after a hot reload).</summary>
        private static void Forget(ZRoutedRpc rpc, string name)
        {
            if (AccessTools.Field(typeof(ZRoutedRpc), "m_functions")?.GetValue(rpc) is IDictionary table) table.Remove(name.GetStableHashCode());
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        // the server: send its values to each player who joins, to anyone who asks, and to everyone when one changes
        private void OnNewPeer(long peer)
        {
            if (IsServer) Send(peer);
            else if (ZNet.instance != null && ZNet.instance.GetServerPeer() is ZNetPeer server && server.m_uid == peer) Ask(); // (the server was just added)
        }

        private void OnAsk(long sender)
        {
            if (IsServer) Send(sender);
        }

        private void Ask()
        {
            try { ZRoutedRpc.instance?.InvokeRoutedRPC(ZNet.instance.GetServerPeer().m_uid, _ask); }
            catch (Exception) { } // (the server doesn't have the mod)
        }

        private void Send(long target)
        {
            if (ZRoutedRpc.instance == null || _entries.Count == 0) return;
            var pkg = new ZPackage();
            pkg.Write(_entries.Count);
            foreach (ConfigEntryBase e in _entries)
            {
                pkg.Write(e.Definition.Section);
                pkg.Write(e.Definition.Key);
                pkg.Write(TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType));
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(target, _send, pkg);
        }

        // a client: take the server's values (only from the server)
        private void OnValues(long sender, ZPackage pkg)
        {
            if (IsServer) return;
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (server == null || server.m_uid != sender) return;
            try
            {
                int count = pkg.ReadInt();
                var changed = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    string section = pkg.ReadString(), key = pkg.ReadString(), text = pkg.ReadString();
                    ConfigEntryBase e = _entries.FirstOrDefault(x => x.Definition.Section == section && x.Definition.Key == key);
                    if (e == null) continue; // (a setting this version doesn't have)
                    object value;
                    try { value = TomlTypeConverter.ConvertToValue(text, e.SettingType); }
                    catch (Exception) { continue; }
                    if (!_own.ContainsKey(e)) _own[e] = e.BoxedValue;
                    _server[e] = value;
                    if (!Equals(e.BoxedValue, value)) { Apply(e, value); changed.Add($"{section}/{key} = {text}"); }
                }
                _log?.LogInfo($"Settings from the server apply while you play here ({_server.Count})" + (changed.Count > 0 ? ": " + string.Join(", ", changed.ToArray()) : ""));
            }
            catch (Exception e) { _log?.LogWarning("Could not read the server's settings: " + e.Message); }
        }

        /// <summary>Set a value without writing it to the player's config file.</summary>
        private void Apply(ConfigEntryBase e, object value)
        {
            bool save = _config.SaveOnConfigSet;
            _applying = true;
            try { _config.SaveOnConfigSet = false; e.BoxedValue = value; }
            finally { _config.SaveOnConfigSet = save; _applying = false; }
        }

        /// <summary>Leaving the server: the player's own values again.</summary>
        private void Restore()
        {
            if (_server.Count == 0) return;
            foreach (var kv in _own) Apply(kv.Key, kv.Value);
            _own.Clear();
            _server.Clear();
            _log?.LogInfo("Your own settings apply again");
        }

        private void OnChanged(object sender, EventArgs args)
        {
            if (_applying || !(sender is ConfigEntryBase e)) return;
            if (_server.TryGetValue(e, out object value))
            {
                _own[e] = e.BoxedValue; // the player changed their own value while the server's applies: keep it for later
                Apply(e, value);
            }
            else if (IsServer && ZNet.instance.GetPeers().Count > 0) Send(ZRoutedRpc.Everybody); // the host changed one: everyone gets it
        }

        private void Unsubscribe(ConfigEntryBase e)
        {
            // ConfigEntry<T>.SettingChanged is per type: remove through reflection on the entry's own event
            var ev = e.GetType().GetEvent("SettingChanged");
            ev?.RemoveEventHandler(e, new EventHandler(OnChanged));
        }
    }
}
