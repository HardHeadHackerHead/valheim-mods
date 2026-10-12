using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DHack.Shared
{
    /// <summary>
    /// Settings the server decides in multiplayer, and a check that the server has the mod at all. A mod marks its gameplay settings with
    /// Add or Bind (rewards, ranges, timers, costs, strengths: anything a player could set for themselves to gain an edge); look-and-feel
    /// settings stay each player's own. In single player and on the host nothing changes. On a client connected to a server that has the
    /// mod, the server's values replace the player's own while they play there (the config file keeps the player's own, even when it is
    /// saved meanwhile), and come back when they leave. A server without the mod sends nothing, and the player's own values stay.
    ///
    /// Also:
    ///   - The server has the mod? Server says Installed, Missing or Checking (asked, no answer yet: Missing after 20 seconds), with the
    ///     server's version. NeedsServer(piece prefab or recipe): it can't be built or crafted on a server without the mod (the host
    ///     deletes pieces and dropped items it doesn't know). Nobody is ever kicked: vanilla players and other versions still join.
    ///   - Edits to the .cfg apply while the game runs (a dedicated server's owner edits the file: everyone gets the new values).
    ///   - The server's admins (adminlist.txt) and the host change the server's values from their own game, with Configuration Manager:
    ///     the server checks the admin list on the connection itself, saves its file and sends everyone the new values. For other players
    ///     the server's settings show read-only there.
    ///   - Server data (AddText): a text the server decides that isn't one setting (a list, a table, a JSON file), any size up to 8 MB
    ///     packed. On a client connected to a server that has it, the mod's use(text) gets the server's text at join and whenever the
    ///     server calls TextChanged; on leaving, its own text again. Players never send any back. Older copies on either side just
    ///     don't get it (each side keeps its own).
    ///
    /// Shared source (mods/Shared/ServerSettings.cs in the valheim-mods repo), compiled into each mod that uses it. Claude Tools also ships it as
    /// BepInEx/claude/templates/ServerSettings.cs: copy it into your mod and change the namespace to yours. Then:
    ///
    ///   Synced = new ServerSettings(Guid, Config, Logger);   // (the mod's version is read from its [BepInPlugin])
    ///   Reward = Synced.Bind("1 - Rewards", "Percent", 100, "How much ...", new AcceptableValueRange&lt;int&gt;(0, 500));
    ///   Synced.NeedsServer(myPiecePrefab);                   // after building the prefab; again for a new prefab after a hot reload
    ///   Synced.AddText("Contracts", () => File.ReadAllText(path), text => UseContracts(text));   // (use runs at once with your own text)
    ///   Synced.TextChanged("Contracts");                     // your own text changed (on the server: everyone gets it again)
    ///   void Update() => Synced.Update();
    ///   void OnDestroy() => Synced.Dispose();
    /// </summary>
    internal sealed class ServerSettings : IDisposable
    {
        public enum ServerMod { NotConnected, Checking, Installed, Missing }

        private const int Protocol = 2;                 // what the hello says this copy understands (1: admin edits, 2: server data)
        private const float ChangeSendGap = 0.5f;       // the least time between two sends of a changed value to everyone (a slider sends every frame)
        private const float AnswerWait = 20f;           // how long a server has to answer before it counts as not having the mod
        private const float FileWait = 1f;              // a saved .cfg is read again this long after its last change

        private const int PartSize = 64 * 1024;         // server data goes in parts of this size (Steam refuses a message over 512 KB: see "server data")
        private const int CompressFrom = 1024;          // server data longer than this many characters goes gzipped
        private const int WarnBytes = 256 * 1024;       // server data bigger than this (as sent) gets a warning in the server's log
        private const int MaxBytes = 8 << 20;           // server data bigger than this (as sent) isn't sent: the players keep their own
        private const int PaceBelow = 10 * 1024;        // the next part goes once a player's connection has less than this waiting

        private readonly string _send, _ask, _dAsk, _dHello, _dSend, _edit, _dTextAsk, _dText, _dAgain;
        private readonly ConfigFile _config;
        private readonly BepInEx.Logging.ManualLogSource _log;
        private readonly string _name;
        private readonly List<ConfigEntryBase> _entries = new List<ConfigEntryBase>();
        private readonly Dictionary<ConfigEntryBase, object> _own = new Dictionary<ConfigEntryBase, object>();     // the player's values while the server's apply
        private readonly Dictionary<ConfigEntryBase, object> _server = new Dictionary<ConfigEntryBase, object>();  // the server's values
        private readonly List<GameObject> _guardedPieces = new List<GameObject>();
        private readonly List<Recipe> _guardedRecipes = new List<Recipe>();
        private ZRoutedRpc _registeredOn;
        private bool _applying, _reloading;
        private bool _changePending;   // the host changed a setting that everyone hasn't been sent yet
        private float _nextChangeSend; // when the next "everyone gets it" may go out
        private float _askedAt = -1f;  // when this client asked the server (Checking)
        private int _serverProtocol;   // what the server's hello said
        private bool _directSeen;      // a client: the server's hello came over its connection (an older copy's routed values are ignored then)
        private readonly HashSet<ZRpc> _directPeers = new HashSet<ZRpc>();   // the server: players whose copy asked over their connection
        private readonly List<SyncedText> _texts = new List<SyncedText>();   // server data (AddText)
        private readonly Dictionary<ZRpc, HashSet<string>> _textPeers = new Dictionary<ZRpc, HashSet<string>>();   // the server: who asked for which
        private bool _textPending;     // the server: a text changed that everyone hasn't been sent yet
        private float _nextTextSend;
        private bool _readOnlyShown;
        private float _nextAdminCheck;
        private FileSystemWatcher _watcher;
        private volatile bool _fileTouched;
        private float _reloadAt = -1f;
        private string _fileText;      // the file as last read or written here (a change event for our own save reads nothing)

        /// <summary>This mod's version (from its [BepInPlugin]), sent to players who join.</summary>
        public string Version { get; }

        /// <summary>True while the server's values apply (on a client connected to a server that has the mod).</summary>
        public bool FromServer => _server.Count > 0;

        /// <summary>Whether the server this client is connected to has the mod (NotConnected in single player, on the host and at the menu).</summary>
        public ServerMod Server { get; private set; } = ServerMod.NotConnected;

        /// <summary>The server's version of the mod (null when unknown: not connected, or a server copy too old to say).</summary>
        public string ServerVersion { get; private set; }

        /// <summary>Server or ServerVersion changed (joining, leaving, the answer arriving, no answer in time).</summary>
        public event Action ServerChanged;

        /// <summary>This player may change the server's values from their game: the host, or an admin on a server that takes edits.</summary>
        public bool CanEditServer => IsServer || (Server == ServerMod.Installed && _serverProtocol >= 1 && ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost());

        public ServerSettings(string guid, ConfigFile config, BepInEx.Logging.ManualLogSource log)
        {
            _send = guid + ".settings";
            _ask = guid + ".settings.ask";
            _dAsk = guid + ".settings.d.ask";
            _dHello = guid + ".settings.d.hello";
            _dSend = guid + ".settings.d.values";
            _edit = guid + ".settings.d.edit";
            _dTextAsk = guid + ".settings.d.textask";
            _dAgain = guid + ".settings.d.again";
            _dText = guid + ".settings.d.text";
            _config = config;
            _log = log;
            BepInPlugin plugin = PluginOf(guid);
            _name = plugin?.Name ?? guid;
            Version = plugin?.Version?.ToString() ?? "";
            Live.Add(this);
            if (Live.Count == 1) PatchSave();
            WatchFile();
        }

        /// <summary>Mark a setting as the server's in multiplayer. Say so in its description yourself ("[Synced with Server]"), or use Bind.</summary>
        public ConfigEntry<T> Add<T>(ConfigEntry<T> entry)
        {
            if (entry == null || _entries.Contains(entry)) return entry;
            _entries.Add(entry);
            entry.SettingChanged += OnChanged;
            GiveManagerAttributes(entry);
            return entry;
        }

        /// <summary>An entry without a ConfigurationManagerAttributes gets ours, so it can show read-only (as ServerSync does: the
        /// description's tag list is read-only, so through its field). Never the shared empty description every bare Bind gets.</summary>
        private static void GiveManagerAttributes(ConfigEntryBase entry)
        {
            ConfigDescription d = entry.Description;
            if (d == null || ReferenceEquals(d, ConfigDescription.Empty) || (d.Tags ?? new object[0]).Any(IsManagerAttributes)) return;
            try
            {
                object[] tags = new object[] { new ConfigurationManagerAttributes() }.Concat(d.Tags ?? new object[0]).ToArray();
                AccessTools.Field(typeof(ConfigDescription), "<Tags>k__BackingField")?.SetValue(d, tags);
            }
            catch (Exception) { } // (it just won't show read-only)
        }

        /// <summary>Config.Bind and Add in one: the description gets " [Synced with Server]". (Configuration Manager shows every synced
        /// setting read-only while the server's value applies to a player who can't change it.)</summary>
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description, AcceptableValueBase range = null, params object[] tags)
        {
            const string Mark = " [Synced with Server]";
            if (!description.Contains("[Synced with Server]")) description += Mark;
            return Add(_config.Bind(section, key, value, new ConfigDescription(description, range, tags ?? new object[0])));
        }

        /// <summary>
        /// A piece prefab (or a Recipe) that only works on a server with the mod: while this client plays on a server without it, it
        /// leaves the build menu (the crafting list), and comes back in single player or on a server that has the mod. Without this the
        /// player could build one there, and the host would delete it the next time its area loads (it doesn't know the prefab).
        /// Safe to call again after rebuilding the prefab.
        /// </summary>
        public void NeedsServer(GameObject piecePrefab)
        {
            if (piecePrefab == null) return;
            _guardedPieces.RemoveAll(p => p == null || p.name == piecePrefab.name);
            _guardedPieces.Add(piecePrefab);
            Guard();
        }

        public void NeedsServer(Recipe recipe)
        {
            if (recipe == null) return;
            _guardedRecipes.RemoveAll(r => r == null || r == recipe || (!string.IsNullOrEmpty(recipe.name) && r.name == recipe.name));
            _guardedRecipes.Add(recipe);
            Guard();
        }

        /// <summary>
        /// Server data: a text the server decides (a list, a table, a JSON file), up to 8 MB as sent (gzipped when long). <paramref name="own"/>
        /// gives this game's own text; <paramref name="use"/> is called with the text that applies: at once with the own text, with the
        /// server's while this client plays on a server that has it (at join, and when the server calls TextChanged), and with the own text
        /// again on leaving. Only when the text differs from the one it got last. In single player, on the host and on a dedicated server:
        /// always the own text, and the server sends it to every player whose copy asks for this key. Safe to call again with the same key.
        /// </summary>
        public void AddText(string key, Func<string> own, Action<string> use)
        {
            if (string.IsNullOrEmpty(key) || own == null || use == null) { _log?.LogError("AddText needs a key, own and use"); return; }
            _texts.RemoveAll(x => x.Key == key);
            var t = new SyncedText { Key = key, Own = own, Use = use };
            _texts.Add(t);
            Give(t, OwnText(t));
            ZRpc server = ZNet.instance != null && !IsServer ? ZNet.instance.GetServerPeer()?.m_rpc : null;
            if (_registeredOn != null && server != null) { server.Register<ZPackage>(_dText, OnText); AskTexts(server); }   // (added after joining)
        }

        /// <summary>This game's own text for the key changed: use gets it now (unless the server's applies), and on the server (or host)
        /// every player gets it again (at most twice a second).</summary>
        public void TextChanged(string key)
        {
            SyncedText t = _texts.FirstOrDefault(x => x.Key == key);
            if (t == null) return;
            t.Packed = null;
            if (!t.FromServer) Give(t, OwnText(t));
            if (IsServer) { t.Pending = true; _textPending = true; }
        }

        /// <summary>True while the server's text for the key applies (a client on a server that sent it).</summary>
        public bool TextFromServer(string key) => _texts.Any(x => x.Key == key && x.FromServer);

        /// <summary>Call every frame (the plugin's Update): it notices joining and leaving a world, sends the host's last change, reads an
        /// edited .cfg, and gives up waiting for a server that doesn't answer.</summary>
        public void Update()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc != _registeredOn)
            {
                Unregister();
                Restore();
                RestoreTexts(true);
                _changePending = false;
                _textPending = false;
                SetServer(ServerMod.NotConnected, null, 0);
                if (rpc != null) Register(rpc);
            }
            float now = Time.unscaledTime;
            if (_changePending && now >= _nextChangeSend) SendChange();
            if (_textPending && now >= _nextTextSend) SendTexts();
            if (_outbox.Count > 0) Pump();
            if (Server == ServerMod.Checking && _askedAt >= 0f && now - _askedAt > AnswerWait)
            {
                if (_guardedPieces.Count + _guardedRecipes.Count > 0) _log?.LogWarning($"The server didn't answer: {_name} isn't installed there (or its copy is older than 2026-10-11), so its pieces and items can't be made on this server.");
                else _log?.LogInfo($"The server didn't answer: {_name} isn't installed there, or its copy is older" + (FromServer ? " (its settings apply)." : " (your own settings apply)."));
                SetServer(ServerMod.Missing, null, 0);
            }
            if (_fileTouched) { _fileTouched = false; _reloadAt = now + FileWait; }
            if (_reloadAt >= 0f && now >= _reloadAt) { _reloadAt = -1f; ReloadFile(); }
            if (FromServer && now >= _nextAdminCheck) { _nextAdminCheck = now + 1f; ShowReadOnly(); }   // (the admin list can arrive after the values)
        }

        public void Dispose()
        {
            Unregister();
            Restore();
            RestoreTexts(false);   // (the mod is going away: its use isn't called again)
            SetServer(ServerMod.NotConnected, null, 0);
            foreach (ConfigEntryBase e in _entries) Unsubscribe(e);
            _entries.Clear();
            _texts.Clear();
            try { _watcher?.Dispose(); } catch (Exception) { }
            _watcher = null;
            Live.Remove(this);
            if (Live.Count == 0) UnpatchSave();
        }

        // ---- the network ----
        // Hello and values go over the server's own connection (ZRpc), which a client can't fake: a routed RPC carries whatever sender id
        // its packet claims, so a modded client could pretend to be the server ("it has the mod", and pieces built there get deleted).
        // The routed ask and values stay for copies from before 2026-10-11 (they only ever carry settings, never "the server has the mod").

        private void Register(ZRoutedRpc rpc)
        {
            Forget(rpc, _send);
            Forget(rpc, _ask);
            rpc.Register<ZPackage>(_send, OnRoutedValues);
            rpc.Register(_ask, OnRoutedAsk);
            rpc.m_onNewPeer += OnNewPeer;
            _registeredOn = rpc;
            if (IsServer)
            {
                // a reload while players are on: listen again, and ask their copies to ask again (this copy doesn't know who asked for what)
                foreach (ZNetPeer peer in ZNet.instance.GetPeers()) { ListenTo(peer); peer?.m_rpc?.Invoke(_dAgain); }
            }
            else if (ZNet.instance != null)
            {
                SetServer(ServerMod.Checking, null, 0);
                if (ZNet.instance.GetServerPeer() != null) AskServer();   // (a reload while connected: the server is already there)
            }
        }

        private void Unregister()
        {
            if (_registeredOn == null) return;
            _registeredOn.m_onNewPeer -= OnNewPeer;
            Forget(_registeredOn, _send);
            Forget(_registeredOn, _ask);
            if (ZNet.instance != null)
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer?.m_rpc != null) foreach (string name in new[] { _dAsk, _dHello, _dSend, _edit, _dTextAsk, _dText, _dAgain }) ForgetDirect(peer.m_rpc, name);
            _directPeers.Clear();
            _textPeers.Clear();
            _outbox.Clear();
            _directSeen = false;
            _registeredOn = null;
        }

        /// <summary>Remove a routed RPC by name (the game throws when a name is registered twice, as after a hot reload).</summary>
        private static void Forget(ZRoutedRpc rpc, string name)
        {
            if (AccessTools.Field(typeof(ZRoutedRpc), "m_functions")?.GetValue(rpc) is IDictionary table) table.Remove(name.GetStableHashCode());
        }

        /// <summary>Remove a connection's RPC by name (so nothing calls into an unloaded copy).</summary>
        private static void ForgetDirect(ZRpc rpc, string name)
        {
            if (AccessTools.Field(typeof(ZRpc), "m_functions")?.GetValue(rpc) is IDictionary table) table.Remove(name.GetStableHashCode());
        }

        private static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        private void OnNewPeer(long uid)
        {
            if (IsServer)
            {
                // A player joined: listen on their connection, and send them everything (a new copy asks again once it listens itself, so
                // nothing is lost if this arrives too early; an older copy takes the routed values).
                ZNetPeer peer = ZNet.instance.GetPeer(uid);
                ListenTo(peer);
                if (peer?.m_rpc != null) SendDirect(peer.m_rpc);
                SendRouted(uid);
            }
            else if (ZNet.instance != null && ZNet.instance.GetServerPeer() is ZNetPeer server && server.m_uid == uid) AskServer(); // (the server was just added)
        }

        // ---- the server ----

        private void ListenTo(ZNetPeer peer)
        {
            if (peer?.m_rpc == null) return;
            peer.m_rpc.Register(_dAsk, OnDirectAsk);
            peer.m_rpc.Register<ZPackage>(_edit, OnEdit);
            peer.m_rpc.Register<ZPackage>(_dTextAsk, OnTextAsk);
        }

        private void OnDirectAsk(ZRpc rpc)
        {
            if (!IsServer) return;
            _directPeers.Add(rpc);
            SendDirect(rpc);
        }

        private void OnRoutedAsk(long sender)
        {
            if (IsServer) SendRouted(sender);   // (an older copy)
        }

        /// <summary>The hello (this copy's protocol and version), then the values, over a player's own connection.</summary>
        private void SendDirect(ZRpc rpc)
        {
            var hello = new ZPackage();
            hello.Write(Protocol);
            hello.Write(Version);
            rpc.Invoke(_dHello, hello);
            if (_entries.Count > 0) rpc.Invoke(_dSend, Values());
        }

        private void SendRouted(long target)
        {
            if (ZRoutedRpc.instance != null && _entries.Count > 0) ZRoutedRpc.instance.InvokeRoutedRPC(target, _send, Values());
        }

        private ZPackage Values()
        {
            var pkg = new ZPackage();
            pkg.Write(_entries.Count);
            foreach (ConfigEntryBase e in _entries)
            {
                pkg.Write(e.Definition.Section);
                pkg.Write(e.Definition.Key);
                pkg.Write(TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType));
            }
            return pkg;
        }

        // ---- a client ----

        /// <summary>Listen on the server's connection, then ask it (over that, and routed for an older server copy).</summary>
        private void AskServer()
        {
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (server?.m_rpc == null) return;
            _askedAt = Time.unscaledTime;
            server.m_rpc.Register<ZPackage>(_dHello, OnDirectHello);
            server.m_rpc.Register<ZPackage>(_dSend, OnDirectValues);
            server.m_rpc.Register<ZPackage>(_dText, OnText);
            server.m_rpc.Register(_dAgain, OnAskAgain);
            server.m_rpc.Invoke(_dAsk);
            AskTexts(server.m_rpc);
            try { ZRoutedRpc.instance?.InvokeRoutedRPC(server.m_uid, _ask); } catch (Exception) { }
        }

        private static bool IsServerConnection(ZRpc rpc) => !IsServer && ZNet.instance?.GetServerPeer()?.m_rpc == rpc;

        // the server's copy was reloaded and asks again: the settings and the data once more (it answers with the hello and everything)
        private void OnAskAgain(ZRpc rpc)
        {
            if (!IsServerConnection(rpc)) return;
            rpc.Invoke(_dAsk);
            AskTexts(rpc);
        }

        // the server says it has the mod, and which version
        private void OnDirectHello(ZRpc rpc, ZPackage pkg)
        {
            if (!IsServerConnection(rpc)) return;
            try
            {
                int protocol = pkg.ReadInt();
                string version = pkg.ReadString();
                if (version != Version && version != ServerVersion)
                    _log?.LogWarning($"The server has {_name} {version}, you have {Version}: some things may not match. Use the same version as the server.");
                _directSeen = true;
                SetServer(ServerMod.Installed, version, protocol);
            }
            catch (Exception e) { _log?.LogWarning("Could not read the server's hello: " + e.Message); }
        }

        private void OnDirectValues(ZRpc rpc, ZPackage pkg)
        {
            if (IsServerConnection(rpc)) TakeValues(pkg);
        }

        // an older server copy: its settings apply, but it can't prove it has the mod (anyone can send this), so Server stays as it is
        private void OnRoutedValues(long sender, ZPackage pkg)
        {
            if (_directSeen || IsServer) return;
            ZNetPeer server = ZNet.instance != null ? ZNet.instance.GetServerPeer() : null;
            if (server != null && server.m_uid == sender) TakeValues(pkg);
        }

        // take the server's values
        private void TakeValues(ZPackage pkg)
        {
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
                ShowReadOnly();
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
            ShowReadOnly();
            _log?.LogInfo("Your own settings apply again");
        }

        private void OnChanged(object sender, EventArgs args)
        {
            if (_applying || !(sender is ConfigEntryBase e)) return;
            if (_server.TryGetValue(e, out object value))
            {
                if (!_reloading && CanEditServer) SendEdit(e);   // an admin changed it in game: the server decides, and sends it back to everyone
                else _own[e] = e.BoxedValue;                     // the player changed their own value while the server's applies: keep it for later
                Apply(e, value);
            }
            else if (IsServer)
            {
                // The host changed one: everyone gets it. At once the first time, then at most every half second while it keeps changing,
                // and always once more after the last change (Update sends it), so everyone ends up with the final value.
                _changePending = true;
                if (Time.unscaledTime >= _nextChangeSend) SendChange();
            }
        }

        /// <summary>Send every synced value to everyone (the same message a joining player gets), and hold the next send back a moment.</summary>
        private void SendChange()
        {
            _changePending = false;
            _nextChangeSend = Time.unscaledTime + ChangeSendGap;
            if (!IsServer) return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer?.m_rpc == null) continue;
                if (_directPeers.Contains(peer.m_rpc)) { if (_entries.Count > 0) peer.m_rpc.Invoke(_dSend, Values()); }
                else SendRouted(peer.m_uid);   // (an older copy, or a player without the mod: it ignores it)
            }
        }

        // ---- admins changing the server's values ----

        // Over the connection itself (ListenTo), not a routed RPC, so the server knows who is asking.
        private void SendEdit(ConfigEntryBase e)
        {
            ZNetPeer server = ZNet.instance?.GetServerPeer();
            if (server?.m_rpc == null) return;
            var pkg = new ZPackage();
            pkg.Write(e.Definition.Section);
            pkg.Write(e.Definition.Key);
            pkg.Write(TomlTypeConverter.ConvertToString(e.BoxedValue, e.SettingType));
            server.m_rpc.Invoke(_edit, pkg);
            _log?.LogInfo($"Asked the server to set {e.Definition.Section}/{e.Definition.Key}");
        }

        // the server: an admin's change, checked against the admin list on the connection it came from
        private void OnEdit(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                if (!IsServer) return;
                string who = rpc.GetSocket()?.GetHostName() ?? "";
                ZNetPeer peer = ZNet.instance.GetPeers().FirstOrDefault(p => p.m_rpc == rpc);
                string section = pkg.ReadString(), key = pkg.ReadString(), text = pkg.ReadString();
                if (!ZNet.instance.IsAdmin(who))
                {
                    _log?.LogWarning($"{peer?.m_playerName ?? who} tried to change {section}/{key} but isn't on the admin list");
                    if (_entries.Count > 0) rpc.Invoke(_dSend, Values());   // (their game shows the server's value again)
                    return;
                }
                ConfigEntryBase e = _entries.FirstOrDefault(x => x.Definition.Section == section && x.Definition.Key == key);
                if (e == null) return;
                e.BoxedValue = TomlTypeConverter.ConvertToValue(text, e.SettingType);   // (saved to the server's file as any change; OnChanged sends it to everyone)
                _log?.LogInfo($"Admin {peer?.m_playerName ?? who} set {section}/{key} = {text}");
            }
            catch (Exception ex) { _log?.LogWarning("Could not take an admin's change: " + ex.Message); }
        }

        /// <summary>Configuration Manager shows the server's settings read-only to a player who can't change them.</summary>
        private void ShowReadOnly()
        {
            bool readOnly = FromServer && !CanEditServer;
            if (readOnly == _readOnlyShown) return;
            _readOnlyShown = readOnly;
            foreach (ConfigEntryBase e in _entries)
                foreach (object tag in e.Description?.Tags ?? new object[0])
                    if (IsManagerAttributes(tag))
                        try { AccessTools.Field(tag.GetType(), "ReadOnly")?.SetValue(tag, readOnly ? (object)true : null); }
                        catch (Exception) { } // (a mod's own attributes class with a plain bool)
        }

        private static bool IsManagerAttributes(object tag) => tag != null && tag.GetType().Name == "ConfigurationManagerAttributes";

        /// <summary>Found by its name by Configuration Manager (any mod's class of this name works the same).</summary>
        private sealed class ConfigurationManagerAttributes
        {
#pragma warning disable 0649
            public bool? ReadOnly;
#pragma warning restore 0649
        }

        // ---- server data (AddText) ----
        // Over the connection only, never routed (no older copy has it, and a routed sender can be faked). A client's copy asks for the
        // keys it has (<guid>.settings.d.textask: int count, then each key) after its plain ask; the server answers each key it has with
        // <guid>.settings.d.text, and sends it again to whoever asked for that key when it changes. An older server copy ignores the ask (the
        // game drops a ZRpc name nobody registered, ZRpc.HandlePackage), and an older client copy never asks, so it's never sent anything.
        // Its own message, not an extra parameter on the plain ask: a server reading a parameter an older client didn't send would throw
        // EndOfStream there, which the game takes as "incompatible version" and drops the connection.
        //
        // A value as sent: bool gzipped, then the text in a ZPackage (written with the game's ZPackage.Write/WriteCompressed, read with
        // ReadPackage/ReadCompressedPackage). It goes in parts of 64 KB: string key, int part, int parts, byte[] bytes, on one reliable,
        // ordered connection, so they arrive in order; a value sent again starts over at part 0 (the player drops what it had of the old
        // one), and a part out of order drops the value.
        // Why parts: Steam refuses a message over 512 KB (k_cbMaxSteamNetworkingSocketsMessageSizeSend), and ZSteamSocket then retries it
        // forever, so everything after it on that connection stalls until the player times out. Dungeons' packs, the first big user, are
        // 0.8-1.3 MB of JSON each, 90-210 KB gzipped each (the game's fast gzip) and ~750 KB for all of them. Paced (Pump): Steam's
        // send buffer is 512 KB too, and the game sends a player about 150 KB a second (ZSteamSocket's send rate), so 750 KB is ~5 s of
        // that connection, during which the world's objects (ZDOMan sends only while less than 10 KB waits) arrive slower: hence the
        // warning above 256 KB, and nothing over 8 MB (~55 s).

        private sealed class SyncedText
        {
            public string Key;
            public Func<string> Own;
            public Action<string> Use;
            public string Given;            // what Use got last
            public bool FromServer;         // a client: the server's text applies
            public bool Pending;            // the server: changed, not sent to everyone yet
            public byte[] Packed;           // the server: the value as sent (null: pack it again)
            public bool TooBig;             // the server: Packed is over MaxBytes (not sent)
            public MemoryStream Incoming;   // a client: the parts in so far
            public int NextPart;
        }

        private string OwnText(SyncedText t)
        {
            try { return t.Own() ?? ""; }
            catch (Exception e) { _log?.LogWarning($"Could not read {t.Key}: " + e.Message); return t.Given ?? ""; }
        }

        private void Give(SyncedText t, string text)
        {
            if (t.Given != null && t.Given == text) return;
            t.Given = text;
            try { t.Use(text); }
            catch (Exception e) { _log?.LogWarning($"Could not use {t.Key}: " + e); }
        }

        /// <summary>Leaving the server: each text's own value again (give: false while the mod unloads).</summary>
        private void RestoreTexts(bool give)
        {
            foreach (SyncedText t in _texts)
            {
                t.Incoming = null;
                if (!t.FromServer) continue;
                t.FromServer = false;
                if (give) { Give(t, OwnText(t)); _log?.LogInfo($"Your own {t.Key} applies again"); }
            }
        }

        // a client: ask the server for the keys this copy has
        private void AskTexts(ZRpc server)
        {
            if (_texts.Count == 0 || server == null) return;
            var pkg = new ZPackage();
            pkg.Write(_texts.Count);
            foreach (SyncedText t in _texts) pkg.Write(t.Key);
            server.Invoke(_dTextAsk, pkg);
        }

        // the server: a player's copy asks for these keys (it sends no data: only names, matched against this copy's own)
        private void OnTextAsk(ZRpc rpc, ZPackage pkg)
        {
            try
            {
                if (!IsServer) return;
                int count = pkg.ReadInt();
                var keys = new HashSet<string>();
                for (int i = 0; i < count && i < 1000; i++)
                {
                    string key = pkg.ReadString();
                    if (_texts.Any(x => x.Key == key)) keys.Add(key);
                }
                if (_textPeers.TryGetValue(rpc, out HashSet<string> had)) keys.UnionWith(had);
                foreach (ZRpc gone in _textPeers.Keys.Where(r => !r.IsConnected()).ToList()) _textPeers.Remove(gone);   // (players who left)
                _textPeers[rpc] = keys;
                foreach (SyncedText t in _texts) if (keys.Contains(t.Key)) SendText(rpc, t);
            }
            catch (Exception e) { _log?.LogWarning("Could not answer a player's ask for server data: " + e.Message); }
        }

        // the server: changed texts to everyone who asked for them
        private void SendTexts()
        {
            _textPending = false;
            _nextTextSend = Time.unscaledTime + ChangeSendGap;
            if (!IsServer) return;
            foreach (SyncedText t in _texts)
            {
                if (!t.Pending) continue;
                t.Pending = false;
                foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    if (peer?.m_rpc != null && _textPeers.TryGetValue(peer.m_rpc, out HashSet<string> keys) && keys.Contains(t.Key)) SendText(peer.m_rpc, t);
            }
        }

        private sealed class Sending
        {
            public ZRpc Rpc;
            public string Key;
            public byte[] Data;    // (Packed when it started: a change makes a new array, and a new Sending)
            public int Next, Parts;
        }

        private readonly List<Sending> _outbox = new List<Sending>();   // the server: values going out part by part

        /// <summary>Start sending a value to a player (one already going out to them for the same key starts over, so parts never mix).</summary>
        private void SendText(ZRpc rpc, SyncedText t)
        {
            byte[] data = Packed(t);
            if (data == null) return;
            _outbox.RemoveAll(s => s.Rpc == rpc && s.Key == t.Key);
            _outbox.Add(new Sending { Rpc = rpc, Key = t.Key, Data = data, Parts = Math.Max(1, (data.Length + PartSize - 1) / PartSize) });
            Pump();
        }

        /// <summary>Send the next parts, each only while that player's connection has less than PaceBelow waiting (as ZDOMan waits for
        /// world objects), so the world's objects take turns with it and Steam's send buffer (512 KB) never fills.</summary>
        private void Pump()
        {
            for (int n = _outbox.Count - 1; n >= 0; n--)
            {
                Sending s = _outbox[n];
                try
                {
                    while (s.Next < s.Parts && s.Rpc.IsConnected() && s.Rpc.GetSocket().GetSendQueueSize() < PaceBelow)
                    {
                        int from = s.Next * PartSize, length = Math.Min(PartSize, s.Data.Length - from);
                        var bytes = new byte[length];
                        Buffer.BlockCopy(s.Data, from, bytes, 0, length);
                        var pkg = new ZPackage();
                        pkg.Write(s.Key);
                        pkg.Write(s.Next);
                        pkg.Write(s.Parts);
                        pkg.Write(bytes);
                        s.Rpc.Invoke(_dText, pkg);
                        s.Next++;
                    }
                    if (s.Next >= s.Parts || !s.Rpc.IsConnected()) _outbox.RemoveAt(n);
                }
                catch (Exception e) { _outbox.RemoveAt(n); _log?.LogWarning($"Could not send {s.Key} to a player: " + e.Message); }
            }
        }

        /// <summary>The value as sent, packed once per change (null when it's too big to send).</summary>
        private byte[] Packed(SyncedText t)
        {
            if (t.Packed == null)
            {
                string text = OwnText(t);
                var inner = new ZPackage();
                inner.Write(text);
                var pkg = new ZPackage();
                bool gzip = text.Length > CompressFrom;
                pkg.Write(gzip);
                if (gzip) pkg.WriteCompressed(inner); else pkg.Write(inner);
                t.Packed = pkg.GetArray();
                t.TooBig = t.Packed.Length > MaxBytes;
                string size = $"{t.Packed.Length / 1024} KB" + (gzip ? $" gzipped ({text.Length / 1024} KB of text)" : "");
                if (t.TooBig) _log?.LogError($"{t.Key} is too big to send to players ({size}, at most {MaxBytes >> 20} MB): they keep their own.");
                else if (t.Packed.Length > WarnBytes) _log?.LogWarning($"{t.Key} is big to send to every player who joins ({size}): it takes about {t.Packed.Length / (150f * 1024):0} s of their connection, and their world loads slower meanwhile.");
            }
            return t.TooBig ? null : t.Packed;
        }

        // a client: a part of the server's text, from the server's own connection only
        private void OnText(ZRpc rpc, ZPackage pkg)
        {
            if (!IsServerConnection(rpc)) return;
            SyncedText t = null;
            try
            {
                string key = pkg.ReadString();
                int part = pkg.ReadInt(), parts = pkg.ReadInt();
                byte[] bytes = pkg.ReadByteArray();
                t = _texts.FirstOrDefault(x => x.Key == key);
                if (t == null) return;   // (a key this version doesn't have)
                if (parts < 1 || parts > MaxBytes / PartSize + 1 || part < 0 || part >= parts) throw new InvalidDataException($"part {part} of {parts}");
                if (part == 0) { t.Incoming = new MemoryStream(); t.NextPart = 0; }
                if (t.Incoming == null || part != t.NextPart) throw new InvalidDataException($"part {part} came out of order");
                t.Incoming.Write(bytes, 0, bytes.Length);
                t.NextPart = part + 1;
                if (part < parts - 1) return;
                var value = new ZPackage(t.Incoming.ToArray());
                t.Incoming = null;
                ZPackage inner = value.ReadBool() ? value.ReadCompressedPackage() : value.ReadPackage();
                string text = inner.ReadString();
                t.FromServer = true;
                Give(t, text);
                _log?.LogInfo($"The server's {key} applies while you play here ({text.Length / 1024} KB)");
            }
            catch (Exception e)
            {
                if (t != null) t.Incoming = null;
                _log?.LogWarning("Could not read the server's data" + (t != null ? " for " + t.Key : "") + ": " + e.Message);
            }
        }

        // ---- the server has the mod? ----

        private void SetServer(ServerMod state, string version, int protocol)
        {
            if (state != ServerMod.Checking) _askedAt = -1f;   // (the wait for an answer is over)
            bool changed = state != Server || version != ServerVersion;
            Server = state;
            ServerVersion = version;
            _serverProtocol = protocol;
            if (!changed) return;
            Guard();
            try { ServerChanged?.Invoke(); }
            catch (Exception e) { _log?.LogWarning("ServerChanged: " + e); }
        }

        private static readonly MethodInfo UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

        /// <summary>Pieces and recipes that need the server: off while the server doesn't have the mod (Piece.m_enabled, Recipe.m_enabled
        /// hide them from the game's menus), on everywhere else.</summary>
        private void Guard()
        {
            bool on = Server == ServerMod.NotConnected || Server == ServerMod.Installed;   // (off while asking too: an answer takes a moment)
            bool any = false;
            foreach (GameObject p in _guardedPieces)
            {
                Piece piece = p != null ? p.GetComponent<Piece>() : null;
                if (piece != null && piece.m_enabled != on) { piece.m_enabled = on; any = true; }
            }
            foreach (Recipe r in _guardedRecipes)
                if (r != null && r.m_enabled != on) { r.m_enabled = on; any = true; }
            if (any && Player.m_localPlayer != null)
                try { UpdateAvailablePieces?.Invoke(Player.m_localPlayer, null); } catch (Exception) { }
        }

        // ---- the .cfg edited while the game runs ----

        private void WatchFile()
        {
            try
            {
                string path = _config.ConfigFilePath;
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                };
                FileSystemEventHandler touched = (s, e) => _fileTouched = true;   // (another thread: Update reads it)
                _watcher.Changed += touched;
                _watcher.Created += touched;
                _watcher.Renamed += (s, e) => _fileTouched = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { _log?.LogWarning("Edits to the config file apply after a restart (can't watch it: " + e.Message + ")"); }
        }

        private void ReloadFile()
        {
            string text = ReadFile(_config.ConfigFilePath);
            if (text == null) { if (File.Exists(_config.ConfigFilePath)) _reloadAt = Time.unscaledTime + FileWait; return; }   // (still being written: again in a moment)
            if (text == _fileText) return;   // (our own save)
            _fileText = text;
            bool save = _config.SaveOnConfigSet;
            _reloading = true;
            try { _config.SaveOnConfigSet = false; _config.Reload(); }
            catch (Exception e) { _log?.LogWarning("Could not read the edited config file (check the line you changed): " + e.Message); }
            finally { _config.SaveOnConfigSet = save; _reloading = false; }
        }

        // ---- the player's own values in their file, even when it's saved while the server's apply ----

        private static readonly List<ServerSettings> Live = new List<ServerSettings>();   // (this copy of the mod's)
        private static Harmony _saveHarmony;

        private void PatchSave()
        {
            try
            {
                _saveHarmony = new Harmony(_send + ".save");
                _saveHarmony.Patch(AccessTools.Method(typeof(ConfigFile), nameof(ConfigFile.Save)),
                    prefix: new HarmonyMethod(typeof(ServerSettings), nameof(BeforeSave)),
                    finalizer: new HarmonyMethod(typeof(ServerSettings), nameof(AfterSave)));
            }
            catch (Exception e) { _log?.LogWarning("Could not keep the server's values out of your config file: " + e.Message); }
        }

        private static void UnpatchSave()
        {
            try { _saveHarmony?.UnpatchSelf(); } catch (Exception) { }
            _saveHarmony = null;
        }

        // While the file is written, our entries hold the player's own values (set underneath the events: nothing else notices).
        private static void BeforeSave(ConfigFile __instance)
        {
            foreach (ServerSettings s in Live) if (s._config == __instance) foreach (var kv in s._own) SetQuietly(kv.Key, kv.Value);
        }

        private static Exception AfterSave(ConfigFile __instance, Exception __exception)
        {
            foreach (ServerSettings s in Live)
            {
                if (s._config != __instance) continue;
                foreach (var kv in s._server) SetQuietly(kv.Key, kv.Value);
                s._fileText = ReadFile(__instance.ConfigFilePath);
            }
            return __exception;
        }

        private static string ReadFile(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception) { return null; }   // (being written: the next change event reads it)
        }

        private static void SetQuietly(ConfigEntryBase e, object value)
        {
            AccessTools.Field(e.GetType(), "_typedValue")?.SetValue(e, value);
        }

        // ---- the rest ----

        private static BepInPlugin PluginOf(string guid)
        {
            Type[] types;
            try { types = typeof(ServerSettings).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (Type t in types)
                foreach (BepInPlugin p in t.GetCustomAttributes(typeof(BepInPlugin), false))
                    if (p.GUID == guid || guid.StartsWith(p.GUID + ".")) return p;   // (a second instance: "<guid>.more")
            return null;
        }

        private void Unsubscribe(ConfigEntryBase e)
        {
            // ConfigEntry<T>.SettingChanged is per type: remove through reflection on the entry's own event
            var ev = e.GetType().GetEvent("SettingChanged");
            ev?.RemoveEventHandler(e, new EventHandler(OnChanged));
        }
    }
}
