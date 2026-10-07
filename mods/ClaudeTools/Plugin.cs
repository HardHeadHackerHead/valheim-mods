using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// Claude Tools: lets an AI assistant (Claude Code, or anything that can read and write files) see and help with your game through a
    /// "request mailbox", without any network port and without ever moving your character or pressing keys.
    ///
    ///   * The assistant writes a text file into BepInEx/claude/requests, one command per line. While you are in a world (and AllowRequests is
    ///     on), the game carries the commands out and writes the answer to a .done.json file beside it; pictures go to BepInEx/claude/shots.
    ///   * Built-in commands: pictures (your view, any camera spot, all round a place, straight down), the ground around a spot, your status,
    ///     inventory, what is nearby and what you are looking at, the mods loaded, their settings, the log, a message on your screen, a map pin.
    ///   * Other mods add their own commands (BuildOrders adds blueprint commands) and named places to point cameras at; "help" lists them all.
    ///   * Keys: F12 saves a screenshot for the assistant, Ctrl+F12 surveys the ground where you look.
    ///
    /// The guide for assistants is written to BepInEx/claude/CLAUDE.md, so an assistant started in that folder knows all of this.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.claudetools";
        public const string Name = "ClaudeTools";
        public const string Version = "1.1.1";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private ConfigEntry<bool> _allowRequests, _allowConfigChanges;
        private ConfigEntry<KeyCode> _shotKey;
        private ConfigEntry<KeyboardShortcut> _surveyKey;
        private ConfigEntry<float> _surveyRadius;

        internal static string Folder => Path.Combine(Paths.BepInExRootPath, "claude");
        internal static string RequestDir => Path.Combine(Folder, "requests");
        internal static string ShotDir => Path.Combine(Folder, "shots");

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _allowRequests = Config.Bind("Requests", "AllowRequests", false,
                "Carry out request files an AI assistant drops into BepInEx/claude/requests (pictures, surveys, what you carry and see, and commands other mods add, " +
                "such as placing blueprint ghosts). Only files on this computer can do this; nothing moves your character or presses keys. Turn it on while you work with an assistant.");
            _allowConfigChanges = Config.Bind("Requests", "AllowConfigChanges", false,
                "Also let requests change mods' settings (config set). Reading settings is always allowed.");
            _shotKey = Config.Bind("Keys", "ScreenshotKey", KeyCode.F12, "Save a screenshot (and where you stand and look) into BepInEx/claude/shots for the assistant.");
            _surveyKey = Config.Bind("Keys", "SurveyKey", new KeyboardShortcut(KeyCode.F12, KeyCode.LeftControl),
                "Write the ground heights, water and buildings around where you look into BepInEx/claude/_survey.json.");
            _surveyRadius = Config.Bind("Keys", "SurveyRadius", 24f, new ConfigDescription("How far a survey reaches (metres).", new AcceptableValueRange<float>(4f, 64f)));
            RegisterBuiltIns();
            Logger.LogInfo($"{Name} {Version} loaded (requests {( _allowRequests.Value ? "on" : "off")}, folder {Folder})");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private float _nextScan;
        private bool _busy, _guideWritten;

        private void Update()
        {
            if (!_guideWritten) { _guideWritten = true; WriteGuide(); }
            Player player = Player.m_localPlayer;
            if (player == null) return;

            if (!TypingOrMenuOpen())
            {
                KeyboardShortcut survey = _surveyKey.Value;
                bool surveyPressed = survey.MainKey != KeyCode.None && Input.GetKeyDown(survey.MainKey) && survey.Modifiers.All(Input.GetKey);
                if (surveyPressed)
                {
                    Vector3 at = LookPoint(out Vector3 hit) ? hit : player.transform.position;
                    Survey(at, _surveyRadius.Value, 1f);
                    player.Message(MessageHud.MessageType.Center, $"Surveyed {(int)_surveyRadius.Value} m around this spot for the assistant");
                }
                else if (Input.GetKeyDown(_shotKey.Value) && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl))
                    StartCoroutine(Screenshot(Path.Combine(ShotDir, Stamp() + ".png"), 0, p => player.Message(MessageHud.MessageType.TopLeft, "Screenshot saved for the assistant")));
            }

            if (_allowRequests.Value && !_busy && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 1f;
                if (Directory.Exists(RequestDir))
                {
                    string next = Directory.GetFiles(RequestDir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (next != null) StartCoroutine(RunRequest(next));
                }
            }
        }

        internal static bool TypingOrMenuOpen() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || global::Console.IsVisible() || TextInput.IsVisible() || Minimap.InTextInput() || Menu.IsVisible();

        internal static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        internal static JArray Vec(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));

        /// <summary>The point you are looking at (terrain, buildings, rocks), up to 60 m away.</summary>
        internal static bool LookPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (GameCamera.instance == null) return false;
            Transform cam = GameCamera.instance.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 60f, LayerMask.GetMask("terrain", "Default", "static_solid", "Default_small", "piece"), QueryTriggerInteraction.Ignore)) return false;
            point = hit.point;
            return true;
        }

        /// <summary>Write CLAUDE.md (the assistants' guide) into BepInEx/claude, refreshed whenever it changes.</summary>
        private void WriteGuide()
        {
            try
            {
                Directory.CreateDirectory(RequestDir);
                Directory.CreateDirectory(ShotDir);
                using (Stream s = typeof(Plugin).Assembly.GetManifestResourceStream("guide/CLAUDE.md"))
                {
                    if (s == null) return;
                    var reader = new StreamReader(s);
                    string text = reader.ReadToEnd();
                    string path = Path.Combine(Folder, "CLAUDE.md");
                    if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
                }
            }
            catch (Exception e) { Logger.LogWarning("Could not write the guide: " + e.Message); }
        }
    }
}
