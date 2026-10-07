using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// A viking companion who follows you and fights beside you. The mod does the fighting itself (moving, swinging, blocking, at full
    /// speed); in a fight, about once a second, it asks Jev (TypeSafe's decision model, with your API key) what to do: attack whom, defend
    /// you, back off, fall back, flee, bow or melee, drink a potion. Without a key, or if Jev does not answer in time, a simple built-in
    /// brain decides instead, so it never stands idle.
    ///
    /// Split across files: Plugin.cs (setup, settings, keys), Prefab.cs (the companion's body, made from the player's), Companion.cs (its
    /// saved settings, gear, summoning and death), Brain.cs (what it does each frame), Jev.cs (asking Jev), Menu.cs (its menu, J or E),
    /// Patches.cs (the game hooks), Tools.cs (commands for Claude Tools).
    ///
    /// Nothing of ours is put on the companion itself (only the game's own components), so a hot reload simply hands the companions
    /// already in the world to the new code.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.aicompanion";
        public const string Name = "AICompanion";
        public const string Version = "0.5.0";

        internal static Plugin Instance;
        internal static ConfigEntry<string> ApiKey, Endpoint, Model;
        internal static ConfigEntry<bool> UseJev, ShowDecisions, LogToFile;
        internal static ConfigEntry<float> DecisionSeconds, MinConfidence, Timeout, PricePerMillion, EngageRange, RespawnSeconds, BaseHealth, BaseStamina, StartingSkill;
        internal static ConfigEntry<KeyboardShortcut> MenuKey;
        internal static ConfigEntry<AwayMode> WhileAway;
        internal static ConfigEntry<int> MaxCompanions;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            // Settings, in the order they show (the names stay the same, so nobody loses what they set). Most are also in the menu.
            MenuKey = Config.Bind("General", "MenuKey", new KeyboardShortcut(KeyCode.J),
                "Opens your companion's menu (or the summon panel when you have none nearby). E on a companion opens it too.");
            MaxCompanions = Config.Bind("Companion", "MaxCompanions", 3, new ConfigDescription(
                "How many companions each player can have.", new AcceptableValueRange<int>(1, 10)));
            WhileAway = Config.Bind("Companion", "WhileAway", AwayMode.Mild,
                "What a companion living at home does while nobody is near. It always catches up on its work when you come back. " +
                "Mild: it also fights off a few creatures and keeps their drops, and never falls. Real: those fights can go badly and it can fall. Off: work only.");
            RespawnSeconds = Config.Bind("Companion", "RespawnSeconds", 30f, new ConfigDescription(
                "Seconds after falling before it wakes in its bed (or beside you, without a bed).", new AcceptableValueRange<float>(5f, 600f)));
            EngageRange = Config.Bind("Companion", "EngageRange", 20f, new ConfigDescription(
                "It fights enemies that come this close (in metres) to it or to you. Also in its menu.", new AcceptableValueRange<float>(5f, 50f)));
            ShowDecisions = Config.Bind("Companion", "ShowDecisions", true,
                "Show what it decides in a fight above its head, such as \"attack Greyling, Jev 87%\". Also in its menu.");
            BaseHealth = Config.Bind("Companion", "BaseHealth", 25f, new ConfigDescription(
                "Its health before food, as a player's (25). Food adds to it.", new AcceptableValueRange<float>(5f, 500f)));
            BaseStamina = Config.Bind("Companion", "BaseStamina", 75f, new ConfigDescription(
                "Its stamina before food, as a player's (75). Food adds to it.", new AcceptableValueRange<float>(10f, 500f)));
            StartingSkill = Config.Bind("Companion", "StartingSkill", 0f, new ConfigDescription(
                "The skill level a new companion starts at (a new player: 0). Its skills rise as it fights.", new AcceptableValueRange<float>(0f, 100f)));

            UseJev = Config.Bind("Jev", "Enabled", true,
                "Let Jev decide how companions fight. Off, or without a key, a simple built-in brain fights. Also in the menu.");
            ApiKey = Config.Bind("Jev", "ApiKey", "",
                "Your Jev key from console.typesafe.ai (easiest: copy it and press Paste in the companion's Brain tab). Only the companion's owner needs one; it stays on your PC.");
            DecisionSeconds = Config.Bind("Jev", "DecisionSeconds", 1.5f, new ConfigDescription(
                "How often Jev is asked during a fight, in seconds (also at once when something big happens). Also in the menu.", new AcceptableValueRange<float>(0.5f, 10f)));
            MinConfidence = Config.Bind("Jev", "MinConfidence", 0.3f, new ConfigDescription(
                "When Jev is less sure than this (0 to 1) about what to do, the built-in brain decides that moment. Also in the menu.", new AcceptableValueRange<float>(0f, 1f)));
            LogToFile = Config.Bind("Jev", "LogToFile", false,
                "Write every Jev request and answer to BepInEx/AICompanion/jev-decisions.jsonl, for tuning. Also in the menu.");
            Timeout = Config.Bind("Jev", "TimeoutSeconds", 4f, new ConfigDescription(
                "Advanced: give up waiting for an answer after this many seconds (it keeps doing what it was doing).", new AcceptableValueRange<float>(1f, 20f)));
            Endpoint = Config.Bind("Jev", "Endpoint", "https://api.typesafe.ai/v1/systemone", "Advanced: TypeSafe's address for Jev. Leave as it is.");
            Model = Config.Bind("Jev", "Model", "jev-latest", "Advanced: which Jev model to ask. Leave as it is.");
            PricePerMillion = Config.Bind("Jev", "PricePerMillionTokens", 0.042f,
                "Advanced: Jev's price per million tokens in US dollars, only for the cost shown in the menu.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) Prefab.Register(ZNetScene.instance); // hot reload while in a world
            Net.Start();
            Portraits.Start();

            Logger.LogInfo($"{Name} {Version} loaded (menu: {MenuKey.Value}, Jev key {(string.IsNullOrEmpty(ApiKey.Value) ? "not set" : "set")})");
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
            Brain.Forget();
            Stamina.Forget();
            Food.Forget();
            Eitr.Forget();
            Rest.Forget();
            Skill.Forget();
            Weather.Forget();
            Ride.Forget();
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
            if (player == null) { if (MenuOpen) CloseMenu(); return; }
            Companion.KeepOwnership(player);
            Home.Tick(player);
            Portraits.Tick();
            UpdateMenu(player);
            if (!MenuOpen && MenuKey.Value.IsDown() && !TypingOrBusy()) OpenMenuFor(player, Companion.MineNear(player, 100f));
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
