using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PortalHub
{
    /// <summary>
    /// Portals you can actually use: press E on a portal and pick where it goes from a list of every portal in the world
    /// (nearest first, with search and favourites), instead of typing matching names on two portals. Link it both ways in
    /// one click, rename it, or look at it on the map.
    ///
    /// How it works: each portal remembers its destination, and the host's game connects it there (the game itself does the
    /// teleporting). Portals you never pick a destination for keep working the old way, by matching names.
    ///
    /// Split across files: Plugin.cs (setup), Plugin.Net.cs (the list, links and messages), Plugin.Window.cs (the menu),
    /// Patches.cs (game hooks).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.portalhub";
        public const string OldGuid = "com.dhack.portalhub"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "PortalHub";
        public const string Version = "1.2.1";

        internal static Plugin Instance;
        internal static DHack.Shared.ServerSettings Synced;   // the settings the server decides in multiplayer

        private ConfigEntry<bool> _enabled, _showHoverLine, _linkBothWays, _hideWarded;
        private ConfigEntry<string> _favorites;
        private Harmony _harmony;
        private int _awakeFrame;

        internal bool Enabled => _enabled.Value && !XPortalInstalled;
        internal bool HideWarded => _hideWarded.Value;

        private bool? _xportal;

        /// <summary>
        /// XPortal (yay.spikehimself.xportal) does the same job its own way and skips the game's portal pairing PortalHub's links rely on
        /// (they would stop working after a restart), and its menu is on E too: with it installed PortalHub stands down. Worked out once the
        /// world is up (mods ScriptEngine loads aren't listed yet when this one wakes).
        /// </summary>
        internal bool XPortalInstalled
        {
            get
            {
                if (_xportal.HasValue) return _xportal.Value;
                if (ZNet.instance == null) return false;
                _xportal = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("yay.spikehimself.xportal");
                if (_xportal.Value) Logger.LogWarning("XPortal is installed: PortalHub stands down and leaves portals to it (both link portals, in ways that undo each other).");
                return _xportal.Value;
            }
        }
        internal bool ShowHoverLine => _showHoverLine.Value;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            Instance = this;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            _enabled = Synced.Add(Config.Bind("General", "Enabled", true, "Use the portal menu when you press E on a portal. (Off: portals work like in the base game.) In multiplayer the server's value applies."));
            _hideWarded = Synced.Add(Config.Bind("General", "HideWardedPortals", false,
                "Leave portals inside someone else's protected area (a switched-on ward you are not on) out of each player's list and map, and refuse links to them. " +
                "(Portals in such an area can never be changed by others either way.) In multiplayer the server's value applies."));
            _showHoverLine = Config.Bind("General", "ShowDestinationOnHover", true, "When you look at a portal, show where it goes.");
            _linkBothWays = Config.Bind("Menu", "LinkBothWays", true, "Picking a destination also links that portal back to this one.");
            _favorites = Config.Bind("Menu", "Favorites", "", "Portals you starred (managed by the menu; you do not need to edit this).");

            BindMapConfig();
            _awakeFrame = Time.frameCount;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo($"{Name} {Version} loaded");
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        // ScriptEngine destroys this copy on a reload: put back everything we hooked into the game.
        private void OnDestroy()
        {
            WindowOpen = false;
            ClearPins(Minimap.instance);
            UnregisterRpc();
            _harmony?.UnpatchSelf();
            DestroyStyles();
            Synced?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            Synced?.Update();
            UpdateNetwork();
            UpdateWindow();
            UpdateMap();
        }

        private void Tell(string message)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.Center, message);
        }

        // ---- favourites (kept per player in the config) ----

        private HashSet<string> Favorites() => new HashSet<string>(_favorites.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));

        private void ToggleFavorite(string id)
        {
            HashSet<string> set = Favorites();
            if (!set.Remove(id)) set.Add(id);
            _favorites.Value = string.Join(",", set.ToArray());
            _rowsKey = null;
        }
    }
}
