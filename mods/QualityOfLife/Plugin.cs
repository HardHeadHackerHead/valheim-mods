using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
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
        public const string Version = "1.5.1";

        private ConfigEntry<bool> _quickSetEnabled, _showBadges, _hammerEnabled, _showMessages;
        private ConfigEntry<KeyboardShortcut> _quickSetKey, _hammerKey;

        internal static Plugin Instance;

        private void Awake()
        {
            Instance = this;
            _showMessages = Config.Bind("General", "ShowMessages", true, "Show a short message in the top-left when something happens.");

            _quickSetEnabled = Config.Bind("QuickSet", "Enabled", true, "Turn the quick-set feature on or off.");
            _quickSetKey = Config.Bind("QuickSet", "Key", new KeyboardShortcut(KeyCode.Q),
                "Inventory open: hover an item and press to add/remove it from your quick set. " +
                "Inventory closed: press to swap to the quick set, press again to go back to what you had.");
            _showBadges = Config.Bind("QuickSet", "ShowBadges", true, "Mark quick-set items in your inventory with a small gold badge.");

            BindQuickStackConfig();
            BindBoatPushConfig();
            BindSortConfig();

            // The buttons under the inventory take this much room (UI pixels); other mods that put a panel there (GearSlots) keep clear of it.
            AppDomain.CurrentDomain.SetData("DHack.QoL.UnderInventoryHeight", 36f);

            _harmony = new Harmony(Guid); // keeps the game from reacting to clicks while the assign menu is open, and tracks chests
            _harmony.PatchAll();
            ContainerRegistry.Seed(); // chests that already exist; new ones are added as they appear

            _hammerEnabled = Config.Bind("Hammer", "Enabled", true, "Turn the hammer shortcut on or off.");
            _hammerKey = Config.Bind("Hammer", "Key", new KeyboardShortcut(KeyCode.B),
                "Press to equip your hammer and start building. Press again to go back to what you had equipped.");

            Logger.LogInfo($"{Name} {Version} loaded (quick set: {_quickSetKey.Value}, hammer: {_hammerKey.Value})");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | quick set: {_quickSetKey.Value}, hammer: {_hammerKey.Value}", Talker.Type.Normal);
        }

        private Harmony _harmony;

        private void OnDestroy()
        {
            if (_running != null) StopCoroutine(_running);
            AppDomain.CurrentDomain.SetData("DHack.QoL.UnderInventoryHeight", null);
            RulesWindowOpen = false;
            ContainerRegistry.Clear();
            if (Instance == this) Instance = null;
            _harmony?.UnpatchSelf();
            DestroyMenuResources();
        }

        private void Update()
        {
            Player player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;

            UpdateBoatPush(player); // hold the push key next to a boat
            UpdateQuickStack(player); // the buttons' clicks, the chest scan, the lock key and the assign key
            if (UpdateRulesWindow(player)) return; // while the assign menu is open, other shortcuts are off (you may be typing)
            if (TypingOrMenuOpen()) return;

            if (_quickSetEnabled.Value && Pressed(_quickSetKey.Value))
            {
                if (InventoryGui.IsVisible()) AssignHovered(player);
                else SwapQuickSet(player);
            }
            else if (_hammerEnabled.Value && Pressed(_hammerKey.Value) && !InventoryGui.IsVisible())
            {
                ToggleHammer(player);
            }
        }

        /// <summary>
        /// Was this shortcut just pressed? Unlike BepInEx's own IsDown(), extra modifier keys being held don't cancel it.
        /// That matters because running is Shift: IsDown() treats "Q while holding Shift" as a different shortcut and ignores it.
        /// </summary>
        private static bool Pressed(KeyboardShortcut shortcut) =>
            shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);

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
