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
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ConfigEntry<string> ApiKey, Endpoint, Model;
        internal static ConfigEntry<bool> UseJev, ShowDecisions;
        internal static ConfigEntry<float> DecisionSeconds, MinConfidence, Timeout, PricePerMillion, Health, EngageRange;
        internal static ConfigEntry<KeyboardShortcut> MenuKey;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            MenuKey = Config.Bind("General", "MenuKey", new KeyboardShortcut(KeyCode.J),
                "Opens your companion's menu (or, if you have none here, the menu to summon one). E on the companion opens it too.");
            Health = Config.Bind("Companion", "Health", 150f, new ConfigDescription("The companion's health. Its armour (what you give it to wear) protects it like a player's.", new AcceptableValueRange<float>(25f, 2000f)));
            EngageRange = Config.Bind("Companion", "EngageRange", 20f, new ConfigDescription("Enemies this close to the companion or to you (metres) start a fight.", new AcceptableValueRange<float>(5f, 50f)));
            ShowDecisions = Config.Bind("Companion", "ShowDecisions", true, "Show what the companion decided above its head (e.g. \"attack Greyling, Jev 87%\").");

            ApiKey = Config.Bind("Jev", "ApiKey", "", "Your TypeSafe API key for Jev (from console.typesafe.ai). Only the companion's owner needs one; it stays in this file on your PC.");
            UseJev = Config.Bind("Jev", "Enabled", true, "Ask Jev how to fight. Off: the built-in brain decides (no key needed).");
            Endpoint = Config.Bind("Jev", "Endpoint", "https://api.typesafe.ai/v1/systemone", "TypeSafe's decision endpoint.");
            Model = Config.Bind("Jev", "Model", "jev-latest", "The Jev model to ask.");
            DecisionSeconds = Config.Bind("Jev", "DecisionSeconds", 1.5f, new ConfigDescription("How often to ask Jev during a fight (it is also asked at once when something big happens).", new AcceptableValueRange<float>(0.5f, 10f)));
            MinConfidence = Config.Bind("Jev", "MinConfidence", 0.3f, new ConfigDescription("If Jev is less sure than this about what to do, the built-in brain decides that round.", new AcceptableValueRange<float>(0f, 1f)));
            Timeout = Config.Bind("Jev", "TimeoutSeconds", 4f, new ConfigDescription("Give up on an answer after this long (the companion keeps doing what it was doing meanwhile).", new AcceptableValueRange<float>(1f, 20f)));
            PricePerMillion = Config.Bind("Jev", "PricePerMillionTokens", 0.042f, "For the cost shown in the menu: Jev's price per million input tokens, in US dollars (output is free).");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) Prefab.Register(ZNetScene.instance); // hot reload while in a world

            Logger.LogInfo($"{Name} {Version} loaded (menu: {MenuKey.Value}, Jev key {(string.IsNullOrEmpty(ApiKey.Value) ? "not set" : "set")})");
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            CloseMenu();
            UnregisterClaudeCommands();
            Brain.Forget();
            _harmony?.UnpatchSelf();
            Prefab.Unregister();
            DestroyMenuResources();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            UpdateClaudeLink();
            Player player = Player.m_localPlayer;
            if (player == null) { if (MenuOpen) CloseMenu(); return; }
            Companion.KeepOwnership(player);
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
