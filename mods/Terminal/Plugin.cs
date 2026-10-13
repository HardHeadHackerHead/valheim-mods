using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[assembly: System.Reflection.AssemblyMetadata("AI_Assisted_Creation", "This assembly was partially or fully created with the assistance of Generative AI.")]
[assembly: System.Reflection.AssemblyMetadata("AI_Model_Vendor", "Anthropic")]
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Terminal
{
    /// <summary>
    /// Quad's Terminal (design: docs/terminal-design.md). Build a Terminal with the hammer and sit down at it: you steer the Proxy, a
    /// second shared character out in the world, and the screen shows the world through the Proxy's eyes for everyone nearby.
    ///
    /// Files: Plugin.cs (settings, setup), Things.cs and Model.cs (the Terminal piece), TerminalPiece.cs (what the piece does),
    /// Proxy.cs (the Proxy's body and prefab), View.cs (the camera and the picture on the screens), Drive.cs (sitting down and
    /// steering), Tools.cs (the "terminal" test command).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.terminal";
        public const string Name = "Terminal";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static DHack.Shared.ServerSettings Synced;   // the gameplay settings and the cost the server decides in multiplayer

        internal static ConfigEntry<float> MouseSpeed, ScreenBrightness, Tether, ProxyHealth, CameraDistance, RespawnSeconds, DriverFps, ViewerFps, ViewerRange;
        internal static ConfigEntry<int> DriverWidth, ViewerWidth;
        internal static ConfigEntry<string> Cost, BrowserViewerHosts;
        internal static ConfigEntry<bool> BrowserEnabled, BrowserImages;
        private static bool _tellNoServer;

        /// <summary>What a terminal costs before anyone changes it.</summary>
        internal const string DefaultCost = "Wood:10, Bronze:2, Resin:4, LeatherScraps:4";

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            Tether = Synced.Add(Config.Bind("Proxy", "Tether", 120f, new ConfigDescription("How far from its terminal the Proxy may go (metres). The game only loads the world around real players, so past this the Proxy would stand in ground that isn't there. In multiplayer the server's value applies.", new AcceptableValueRange<float>(30f, 200f))));
            ProxyHealth = Synced.Add(Config.Bind("Proxy", "Health", 75f, new ConfigDescription("The Proxy's health. In multiplayer the server's value applies.", new AcceptableValueRange<float>(25f, 500f))));
            RespawnSeconds = Synced.Add(Config.Bind("Proxy", "RespawnSeconds", 60f, new ConfigDescription("How long the Proxy takes to come back after it dies (seconds). In multiplayer the server's value applies.", new AcceptableValueRange<float>(5f, 3600f))));
            CameraDistance = Config.Bind("Screen", "CameraDistance", 4f, new ConfigDescription("How far behind the Proxy the camera floats (metres), like the game's own third-person view.", new AcceptableValueRange<float>(1.5f, 8f)));
            MouseSpeed = Config.Bind("Screen", "MouseSpeed", 1f, new ConfigDescription("How fast the mouse pointer moves on a terminal's desktop and in its programs (1 is normal).", new AcceptableValueRange<float>(0.3f, 4f)));
            ScreenBrightness = Config.Bind("Screen", "DesktopBrightness", 0.65f, new ConfigDescription("How bright the desktop and its programs are (1 is full). Bright text glows in the game's bloom; lower this if it still looks overexposed. The Valheim view isn't changed by it.", new AcceptableValueRange<float>(0.2f, 1f)));
            DriverWidth = Config.Bind("Screen", "DriverWidth", 1024, new ConfigDescription("The screen's picture width while you are sitting at it (pixels; the height follows).", new AcceptableValueRange<int>(256, 1920)));
            DriverFps = Config.Bind("Screen", "DriverFps", 30f, new ConfigDescription("How many pictures a second while you are sitting at it.", new AcceptableValueRange<float>(5f, 60f)));
            ViewerWidth = Config.Bind("Screen", "ViewerWidth", 384, new ConfigDescription("The screen's picture width for someone just looking at it (pixels).", new AcceptableValueRange<int>(128, 1280)));
            ViewerFps = Config.Bind("Screen", "ViewerFps", 8f, new ConfigDescription("How many pictures a second for someone just looking at it.", new AcceptableValueRange<float>(1f, 30f)));
            ViewerRange = Config.Bind("Screen", "ViewerRange", 25f, new ConfigDescription("How near a terminal must be for its screen to draw (metres). Further off, the picture stops and costs nothing.", new AcceptableValueRange<float>(5f, 60f)));
            BrowserEnabled = Synced.Add(Config.Bind("Browser", "Enabled", true, "Whether the Browser program on a terminal can load web pages at all. Off, it shows a note instead. In multiplayer the server's value applies."));
            BrowserImages = Config.Bind("Browser", "Pictures", true, "Whether the Browser loads the pictures on a page (PNG and JPEG). Each is fetched from the page's own site under the same rules as the page.");
            BrowserViewerHosts = Config.Bind("Browser", "ViewerHosts", "wikipedia.org,wikimedia.org,duckduckgo.com,example.com", "When somebody else browses on a terminal near you, your game loads the same page only if its site is on this list (comma separated; a site matches its sub-sites; * means any site). Otherwise you see a note with the site's name and your game never contacts it. The sitter's own game always loads what they ask for.");
            Cost = Synced.Bind("Building", "TerminalCost", DefaultCost, "What a terminal costs to build with the hammer (at a workbench). " + DHack.Shared.Costs.Help);
            Cost.SettingChanged += (s, e) => Things.ApplyCost();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == DHack.Shared.ServerSettings.ServerMod.Missing;

            Apps.Register();
            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            if (ZNetScene.instance != null) { Things.Register(); ProxyPrefab.Register(ZNetScene.instance); }   // a reload in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Tools.Update();
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$terminal_server_missing");
            }
            Drive.Update();
        }

        private void OnGUI() => Drive.Overlay();

        private void OnDestroy()
        {
            Drive.End("reload");
            _harmony?.UnpatchSelf();
            Things.Unregister();
            ProxyPrefab.Unregister();
            View.Clear();
            Desktop.Clear();
            BrowserApp.ClearSessions();
            Apps.Clear();
            Tools.Unregister();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) { Things.Register(); ProxyPrefab.Register(__instance); }
    }

    // Whichever of ZNetScene and ObjectDB wakes second, the prefabs are registered before any saved one is loaded.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() { if (ZNetScene.instance != null) { Things.Register(); ProxyPrefab.Register(ZNetScene.instance); } }
    }
}
