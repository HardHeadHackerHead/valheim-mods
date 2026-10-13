using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// The engine's side of the plugin. The BepInEx plugin class is in the thin shell (mods/Scripts); it loads this assembly after the
    /// dependencies are ready and calls Entry. Everything that names Jint lives in this assembly and nowhere in the shell.
    /// </summary>
    internal static class Core
    {
        public const string Guid = "com.quad.blockscript", Name = "Quad's Blockscript", Version = "0.1.7";
        internal static ManualLogSource Log;
        internal static MonoBehaviour Plugin;   // (for coroutines)
    }

    public static class Entry
    {
        private static Harmony _harmony;
        private static float _serverSince = -1f;
        private static bool _autostarted;

        /// <summary>Called by the shell's Awake, after Jint and Esprima can be found.</summary>
        public static void Start(ManualLogSource log, MonoBehaviour plugin)
        {
            Core.Log = log;
            Core.Plugin = plugin;
            if (plugin is BepInEx.BaseUnityPlugin bp) Limits.Bind(bp.Config);
            if (plugin is BepInEx.BaseUnityPlugin bp2)
                Editor.ScriptsWindow.Hotkey = bp2.Config.Bind("Editor", "Open the Blockscript window", new BepInEx.Configuration.KeyboardShortcut(KeyCode.Home), "Opens and closes the Blockscript window (blocks, code, powers, log). The console command blockscript does the same.");
            new Terminal.ConsoleCommand("blockscript", "opens the Blockscript window", args => Editor.ScriptsWindow.Open());
            Blocks.HostData.EventsProvider = () => System.Linq.Enumerable.ToArray(Limits.Events);
            Examples.Install();
            Approvals.Load();
            Host.Scan();
            _harmony = Harmony.CreateAndPatchAll(typeof(Entry).Assembly, Core.Guid);
            log.LogInfo($"{Core.Name} {Core.Version} loaded: {Host.Scripts.Count} script(s) in {Host.Folder}");
        }

        /// <summary>Every frame.</summary>
        public static void Update()
        {
            Tools.Update();
            Editor.ScriptsWindow.Tick();
            Mp.Wire.Tick();    // (finding each other: every game)
            Mp.Agent.Tick();   // (a player's game on a server with the mod: the report, the weather the server forced)
            Mp.CollabGame.Update();   // (live co-editing: the server's clock and the host's own editor)
            if (ZNet.instance != null) NewEvents.Hook();   // (every game: a player's game forwards what only it sees: pickups, building, hits, eating...)
            if (ZNet.instance != null && !ZNet.instance.IsServer()) return;   // (scripts run on the server: a client only shows what they say)
            EffectSweep.Tick();
            Mp.Remote.Tick();
            Autostart();
            Host.CheckFiles();
            Events.Poll();
            Host.Update();
            Players.Tick();
            Store.Tick();
        }

        /// <summary>A server starts the scripts its owner listed, once, shortly after its world is ready.</summary>
        private static void Autostart()
        {
            if (ZNet.instance == null) { _serverSince = -1f; _autostarted = false; return; }
            if (_autostarted) return;
            if (_serverSince < 0f) _serverSince = Time.unscaledTime;
            if (Time.unscaledTime - _serverSince < 10f || ZoneSystem.instance == null) return;
            _autostarted = true;
            foreach (string name in Limits.Autostart)
            {
                Script s = Host.Find(name);
                if (s == null) { Core.Log.LogWarning("Scripts: '" + name + "' is on the start list but there is no such script"); continue; }
                string why = Host.Start(s);
                Core.Log.LogInfo("Scripts: started " + s.Name + (string.IsNullOrEmpty(why) ? "" : " (" + why + ")"));
            }
        }

        /// <summary>The shell is being destroyed (a reload, or the game closing).</summary>
        public static void Stop()
        {
            Editor.ScriptsWindow.Destroy();
            Mp.CollabGame.Shutdown("The server is stopping.");   // (everyone editing live is told, and what changed is saved)
            Mp.Wire.Stop();
            _harmony?.UnpatchSelf();
            Host.StopAll("The mod was reloaded.");
            Store.Flush();
            Tools.Unregister();
        }
    }
}
