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
        public const string Guid = "com.dhack.portalhub";
        public const string Name = "PortalHub";
        public const string Version = "1.1.0";

        internal static Plugin Instance;

        private ConfigEntry<bool> _enabled, _showHoverLine, _linkBothWays;
        private ConfigEntry<string> _favorites;
        private Harmony _harmony;
        private int _awakeFrame;

        internal bool Enabled => _enabled.Value;
        internal bool ShowHoverLine => _showHoverLine.Value;

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Use the portal menu when you press E on a portal. (Off: portals work like in the base game.)");
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
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
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
