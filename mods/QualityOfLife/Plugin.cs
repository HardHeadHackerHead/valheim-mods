using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace QualityOfLife
{
    /// <summary>
    /// A collection of small controls that make the game nicer to play. Each feature has its own on/off switch and key.
    ///   * Quick set  - hover items in your inventory and press a key to build a set; press it in the world to swap to it and back.
    ///   * Hammer key - press a key to jump into construction mode (equips your hammer); press again to go back.
    ///
    /// Split across files: Plugin.cs (setup + input), Plugin.QuickSet.cs, Plugin.Hammer.cs, Plugin.Gear.cs (shared equipping).
    /// No game code is patched, so there's nothing to clean up on a hot reload beyond stopping our own work.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.qualityoflife";
        public const string Name = "QualityOfLife";
        public const string Version = "1.0.0";

        private ConfigEntry<bool> _quickSetEnabled, _showBadges, _hammerEnabled, _showMessages;
        private ConfigEntry<KeyboardShortcut> _quickSetKey, _hammerKey;

        private void Awake()
        {
            _showMessages = Config.Bind("General", "ShowMessages", true, "Show a short message in the top-left when something happens.");

            _quickSetEnabled = Config.Bind("QuickSet", "Enabled", true, "Turn the quick-set feature on or off.");
            _quickSetKey = Config.Bind("QuickSet", "Key", new KeyboardShortcut(KeyCode.Q),
                "Inventory open: hover an item and press to add/remove it from your quick set. " +
                "Inventory closed: press to swap to the quick set, press again to go back to what you had.");
            _showBadges = Config.Bind("QuickSet", "ShowBadges", true, "Mark quick-set items in your inventory with a small gold badge.");

            _hammerEnabled = Config.Bind("Hammer", "Enabled", true, "Turn the hammer shortcut on or off.");
            _hammerKey = Config.Bind("Hammer", "Key", new KeyboardShortcut(KeyCode.B),
                "Press to equip your hammer and start building. Press again to go back to what you had equipped.");

            Logger.LogInfo($"{Name} {Version} loaded (quick set: {_quickSetKey.Value}, hammer: {_hammerKey.Value})");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | quick set: {_quickSetKey.Value}, hammer: {_hammerKey.Value}", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            if (_running != null) StopCoroutine(_running);
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;
            if (TypingOrMenuOpen()) return;

            if (_quickSetEnabled.Value && _quickSetKey.Value.IsDown())
            {
                if (InventoryGui.IsVisible()) AssignHovered(player);
                else SwapQuickSet(player);
            }
            else if (_hammerEnabled.Value && _hammerKey.Value.IsDown() && !InventoryGui.IsVisible())
            {
                ToggleHammer(player);
            }
        }

        /// <summary>True when the key press belongs to something else (chat, console, a text box, the game menu).</summary>
        private static bool TypingOrMenuOpen() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            Minimap.InTextInput() || Menu.IsVisible();

        private void Tell(Player player, string message)
        {
            if (_showMessages.Value) player.Message(MessageHud.MessageType.TopLeft, message);
        }
    }
}
