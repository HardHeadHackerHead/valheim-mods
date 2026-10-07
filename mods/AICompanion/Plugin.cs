using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// A viking companion who plays like a player: follows you and fights beside you (its own brain: Brain, Defense, Archery, Steer), lives its
    /// own life at home (Work, Goals, Home, Mending, CatchUp), and keeps a journal (Journal). Nothing of ours is put on the companion itself
    /// (only the game's own components), so a hot reload simply hands the companions already in the world to the new code.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.aicompanion";
        public const string Name = "AICompanion";
        public const string Version = "0.15.4";

        internal static Plugin Instance;
        internal static ConfigEntry<bool> ShowDecisions;
        internal static ConfigEntry<float> EngageRange, RespawnSeconds, BaseHealth, BaseStamina, StartingSkill;
        internal static ConfigEntry<KeyboardShortcut> MenuKey, CommandKey;
        internal static ConfigEntry<AwayMode> WhileAway;
        internal static ConfigEntry<int> MaxCompanions;

        private Harmony _harmony;
        private ConfigFile _more;   // the settings changed in the menu (not listed in the mod manager)

        /// <summary>
        /// A setting kept in the second file. One set in the mod's own file by an older version is moved over once (and taken out of it).
        /// </summary>
        private ConfigEntry<T> More<T>(string section, string key, T fallback, string description, AcceptableValueBase range = null)
        {
            ConfigEntry<T> entry = _more.Bind(section, key, fallback, new ConfigDescription(description, range));
            var def = new ConfigDefinition(section, key);
            ConfigEntry<T> old = Config.Bind(def, fallback, new ConfigDescription("", range));
            if (!Equals(old.Value, fallback) && Equals(entry.Value, fallback)) entry.Value = old.Value;
            Config.Remove(def);
            return entry;
        }

        /// <summary>Save a setting changed in the menu.</summary>
        internal void SaveSettings() { Config.Save(); _more?.Save(); }

        private void Awake()
        {
            Instance = this;
            // Only a few settings in the mod's own file (and the mod manager): the menu and command keys, how many companions, and life while you
            // are away. Everything else is changed in the companion's menu, where it is explained, and kept in a second file the manager does
            // not list (com.dhack.aicompanion.more.cfg). Values set before 0.6.0 move over by themselves.
            MenuKey = Config.Bind("General", "MenuKey", new KeyboardShortcut(KeyCode.J),
                "Tap: your companion's menu (or the summon panel). Hold: all your companions near you come with you, or go home. E on a companion opens its menu too.");
            CommandKey = Config.Bind("General", "CommandKey", new KeyboardShortcut(KeyCode.H),
                "Point at something and press it: an enemy (they attack it), a tree, rock or plant (it works it), your chest (it puts its things in), its tombstone, a free bed (its bed), the ground (it waits there), or the sky (it comes back).");
            MaxCompanions = Config.Bind("Companion", "MaxCompanions", 3, new ConfigDescription(
                "How many companions each player can have.", new AcceptableValueRange<int>(1, 10)));
            WhileAway = Config.Bind("Companion", "WhileAway", AwayMode.Mild,
                "What a companion living at home does while nobody is near. It always catches up on its work when you come back. " +
                "Mild: it also fights off a few creatures and keeps their drops, and never falls. Real: those fights can go badly and it can fall. Off: work only.");

            _more = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, Guid + ".more.cfg"), true);
            EngageRange = More("Companion", "FightRange", 12f, "It fights enemies that come this close (in metres) to it or to you. In the menu: Orders.", new AcceptableValueRange<float>(5f, 50f));
            _more.Bind("Companion", "EngageRange", 20f, ""); _more.Remove(new ConfigDefinition("Companion", "EngageRange")); // (0.6.0's 20 m: too eager)
            ShowDecisions = More("Companion", "ShowDecisions", true, "Show what it decides in a fight above its head. In the menu: Brain.");
            RespawnSeconds = More("Companion", "RespawnSeconds", 30f, "Seconds after falling before it wakes in its bed (or beside you).", new AcceptableValueRange<float>(5f, 600f));
            BaseHealth = More("Companion", "BaseHealth", 25f, "Its health before food, as a player's (25).", new AcceptableValueRange<float>(5f, 500f));
            BaseStamina = More("Companion", "BaseStamina", 75f, "Its stamina before food, as a player's (75).", new AcceptableValueRange<float>(10f, 500f));
            StartingSkill = More("Companion", "StartingSkill", 0f, "The skill level a new companion starts at (a new player: 0).", new AcceptableValueRange<float>(0f, 100f));
            foreach (string gone in new[] { "Health", "Stamina" }) { Config.Bind("Companion", gone, 0f, ""); Config.Remove(new ConfigDefinition("Companion", gone)); } // from 0.1.0
            Config.Save();   // without the settings that moved

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) Prefab.Register(ZNetScene.instance); // hot reload while in a world
            Net.Start();
            Portraits.Start();

            Logger.LogInfo($"{Name} {Version} loaded (menu: {MenuKey.Value}, commands: {CommandKey.Value})");
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            CloseMenu();
            UnregisterClaudeCommands();
            Net.Stop();
            Home.Stop();
            Portraits.Stop();
            Idle.Forget();  // (up from its chair, before its brain is forgotten)
            Brain.Forget();
            Stamina.Forget();
            Food.Forget();
            Eitr.Forget();
            Rest.Forget();
            Skill.Forget();
            Weather.Forget();
            Ride.Forget();
            Talk.Forget();
            Goals.Forget();
            Steer.Forget();
            Activity.Forget();
            Passing.Forget();
            Defense.Forget();
            Banter.Forget();
            Marks.Forget();
            BagPanel.Destroy();
            Container_Load_Companion.Forget();
            _harmony?.UnpatchSelf();
            Prefab.Unregister();
            DestroyMenuResources();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            UpdateClaudeLink();
            Player player = Player.m_localPlayer;
            Net.Update(player);
            Passing.Tick();     // players and companions walk through each other
            if (player == null) { if (MenuOpen) CloseMenu(); return; }
            Companion.KeepOwnership(player);
            Home.Tick(player);
            Portraits.Tick();
            UpdateMenu(player);
            MenuKeyPressed(player);
            if (!MenuOpen && Home.AssignFor == null && !TypingOrBusy() && CommandKey.Value.IsDown()) Pointing.Command(player);
            Marks.Tick(); // what you pointed it at glows, with a line above it
        }

        private float _keyDownAt = -1f;
        private bool _keyHeld;

        /// <summary>The menu key: a tap opens the menu (on release), holding it half a second calls your companions to you or sends them home.</summary>
        private void MenuKeyPressed(Player player)
        {
            if (MenuOpen || Home.AssignFor != null || TypingOrBusy()) { _keyDownAt = -1f; return; }
            if (MenuKey.Value.IsDown()) { _keyDownAt = Time.time; _keyHeld = false; return; }
            if (_keyDownAt < 0f) return;
            if (MenuKey.Value.IsPressed())
            {
                if (!_keyHeld && Time.time - _keyDownAt > 0.45f) { _keyHeld = true; Home.ToggleAll(player); }
                return;
            }
            if (!_keyHeld) OpenMenuFor(player, Companion.MineNear(player, 100f));
            _keyDownAt = -1f;
        }

        internal static bool TypingOrBusy() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() || Menu.IsVisible() ||
            InventoryGui.IsVisible() || Minimap.IsOpen() || StoreGui.IsVisible() || Hud.IsPieceSelectionVisible();

        internal void Note(string text) => Logger.LogInfo(text);
        internal void Warn(string text) => Logger.LogWarning(text);

        internal static void Tell(string text)
        {
            if (Player.m_localPlayer != null) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, text);
        }

        internal static string Mask(string key) =>
            string.IsNullOrEmpty(key) ? "(none)" : (key.Length <= 4 ? "••••" : new string('•', Math.Min(12, key.Length - 4)) + key.Substring(key.Length - 4));
    }
}
