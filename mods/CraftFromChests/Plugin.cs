using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CraftFromChests
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.craftfromchests";
        public const string Name = "CraftFromChests";
        public const string Version = "1.3.2";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Radius;

        internal static ConfigEntry<bool> ShowHaveCounts;
        internal static ConfigEntry<bool> ShowLines;
        internal static ConfigEntry<float> ToggleOffsetX;
        internal static ConfigEntry<float> ToggleOffsetY;
        internal static ConfigEntry<float> ToggleScale;
        internal static ConfigEntry<float> ToggleWidth;
        internal static ConfigEntry<float> RangeButtonWidth;

        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            ShowHaveCounts = Config.Bind("Display", "ShowHaveCounts", true,
                "In the crafting window, show how many of each material you HAVE (inventory + chests) next to how many you need.");
            ShowLines = Config.Bind("Display", "ShowLines", false, "Draw lines from the crafting station to every chest it can use.");
            ToggleOffsetX = Config.Bind("Display", "ToggleOffsetX", 0f, "Move the 'Chest lines' button horizontally from its default spot beside the Upgrade tab (UI pixels).");
            ToggleOffsetY = Config.Bind("Display", "ToggleOffsetY", 0f, "Move the 'Chest lines' button vertically (UI pixels). Negative = down.");
            ToggleScale = Config.Bind("Display", "ToggleScale", 1f, "Size multiplier for the 'Chest lines' button.");
            ToggleWidth = Config.Bind("Display", "ToggleWidth", 2f, "'Chest lines' button width as a multiple of the Upgrade tab's width.");
            RangeButtonWidth = Config.Bind("Display", "RangeButtonWidth", 1.4f, "'Range' button width as a multiple of the Upgrade tab's width.");
            gameObject.AddComponent<ChestOverlay>();

            Enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            Radius = Config.Bind("General", "Radius", 20f,
                "How far (in meters) from the crafting station a chest can be and still be used.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            // On a hot reload, chests already exist and won't run Awake again, so pick them up here.
            foreach (Container c in FindObjectsOfType<Container>()) ChestScanner.Register(c);

            Logger.LogInfo($"{Name} {Version} loaded");

            // Awake with a player already in the world means this was a hot reload (F6 / ScriptEngine).
            if (Player.m_localPlayer != null) Announce("reloaded");
        }

        /// <summary>Local-only chat line + top-left popup (not sent to other players).</summary>
        internal static void Announce(string what)
        {
            string text = $"{Name} v{Version} {what} | " +
                          $"{(Enabled.Value ? "on" : "off")}, radius {Radius.Value:0.#}m | built {BuildTime}";
            if (Chat.instance != null)
            {
                Chat.instance.AddString("[Mod]", text, Talker.Type.Normal);
                // The chat window is hidden until its timer resets, so reset it to make the line visible.
                AccessTools.Field(typeof(Chat), "m_hideTimer").SetValue(Chat.instance, 0f);
                Chat.instance.m_chatWindow.gameObject.SetActive(true);
            }
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"{Name} {what}");
        }

        // Timestamp of the DLL, so you can tell at a glance that your newest build is the one running.
        private static string BuildTime
        {
            get
            {
                try
                {
                    // ScriptEngine loads from memory, so Assembly.Location is empty; look in scripts, then plugins.
                    string file = Name + ".dll";
                    string scripts = System.IO.Path.Combine(Paths.BepInExRootPath, "scripts", file);
                    string plugins = System.IO.Path.Combine(Paths.PluginPath, Name, file);
                    string path = System.IO.File.Exists(scripts) ? scripts : plugins;
                    return System.IO.File.GetLastWriteTime(path).ToString("HH:mm:ss");
                }
                catch { return "?"; }
            }
        }

        // ScriptEngine destroys the old plugin instance on reload; remove our patches so they don't stack.
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            ChestScanner.Suspend = false;
            ChestScanner.Consuming = false;
            HaveLabel.DestroyAll(); // the "you have" numbers live in the game's UI, so remove them ourselves
        }

        private Harmony _harmony;
    }
}
