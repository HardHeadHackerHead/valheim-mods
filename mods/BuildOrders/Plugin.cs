using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Ghost build orders. Plan a piece instead of building it (hold the plan key and place with the hammer) and it appears
    /// as a glowing ghost for everyone in the party. Anyone can then build it: the placement ghost snaps onto the order, and
    /// the order disappears once the real piece stands there.
    ///
    /// Split across files: Plugin.cs (setup + the per-frame work), Orders.cs (the orders, saving, syncing),
    /// Ghosts.cs (the see-through copies), Patches.cs (hooks into building), Panel.cs (the on-screen panel).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.buildorders";
        public const string Name = "BuildOrders";
        public const string Version = "1.0.1";

        internal static Plugin Instance;

        private ConfigEntry<bool> _enabled, _showGhosts, _alwaysShowPanel;
        private ConfigEntry<float> _ghostOpacity;
        private ConfigEntry<string> _shaderOverride;
        private ConfigEntry<float> _viewDistance, _snapDistance;
        private ConfigEntry<KeyCode> _planKey, _selectKey, _removeKey, _toggleGhostsKey;
        private ConfigEntry<float> _panelX, _panelY;

        private Harmony _harmony;
        private int _awakeFrame;

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            _showGhosts = Config.Bind("General", "ShowGhosts", true, "Show the glowing ghosts of planned pieces.");
            _ghostOpacity = Config.Bind("Look", "GhostOpacity", 0.18f,
                "How solid the ghosts are: 0.05 = barely there, 0.3 = clearly visible, 1 = solid. (Aimed-at ghosts are shown more solid.)");
            _shaderOverride = Config.Bind("Look", "GhostShader", "",
                "Advanced: force a particular shader name for the ghosts. Leave blank to pick the best transparent one automatically (the choice is written to the BepInEx log).");
            _viewDistance = Config.Bind("General", "ViewDistance", 60f, "Ghosts further than this many metres away are hidden (saves performance).");
            _snapDistance = Config.Bind("General", "SnapDistance", 1.5f,
                "When you're placing the same piece as a nearby order, your placement ghost snaps onto the order if it's within this many metres.");
            _planKey = Config.Bind("Keys", "PlanKey", KeyCode.LeftAlt,
                "Hold this while placing with the hammer to PLAN the piece (a build order) instead of building it. Costs nothing.");
            _selectKey = Config.Bind("Keys", "SelectKey", KeyCode.G,
                "Aim at a build order and press this to select that piece in your hammer.");
            _removeKey = Config.Bind("Keys", "RemoveKey", KeyCode.Delete,
                "Aim at a build order and press this to remove it. Hold Shift to remove every order within 8 m of it.");
            _toggleGhostsKey = Config.Bind("Keys", "ToggleGhostsKey", KeyCode.F9, "Show or hide all the ghosts.");
            _alwaysShowPanel = Config.Bind("Panel", "AlwaysShow", false,
                "Show the materials panel all the time. Off = only while you're building (hammer equipped).");
            _panelX = Config.Bind("Panel", "OffsetX", 10f, "Gap from the left edge of the screen (UI pixels).");
            _panelY = Config.Bind("Panel", "OffsetY", 260f, "Gap from the top of the screen (UI pixels).");

            _awakeFrame = Time.frameCount;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Logger.LogInfo($"{Name} {Version} loaded (plan: hold {_planKey.Value} + place)");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | plan: hold {_planKey.Value} + place", Talker.Type.Normal);
        }

        // ScriptEngine destroys this copy when mods reload: undo everything we hooked into the game.
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            UnregisterRpc();
            DestroyAllGhosts();
            if (Instance == this) Instance = null;
        }

        private float _nextGhostUpdate, _nextCompletion, _lastOpacity = -1f;

        private void Update()
        {
            if (!_enabled.Value) { DestroyAllGhosts(); return; }

            UpdateNetwork();
            EnsureWorldLoaded();

            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) return;

            if (Input.GetKeyDown(_toggleGhostsKey.Value) && !TypingOrMenuOpen())
            {
                _showGhosts.Value = !_showGhosts.Value;
                player.Message(MessageHud.MessageType.TopLeft, _showGhosts.Value ? "Build order ghosts shown" : "Build order ghosts hidden");
            }

            if (Time.time >= _nextGhostUpdate)
            {
                _nextGhostUpdate = Time.time + 0.5f;
                UpdateGhosts(player);
                if (!Mathf.Approximately(_lastOpacity, _ghostOpacity.Value)) { _lastOpacity = _ghostOpacity.Value; RetintAll(); } // setting changed
            }
            if (Time.time >= _nextCompletion)
            {
                _nextCompletion = Time.time + 1f;
                CheckCompletion(player);
            }

            UpdateAim(player);
        }

        // ---- aiming at an order -----------------------------------------------------------------

        /// <summary>The order the player is looking at (only while building), or null.</summary>
        internal Order Aimed { get; private set; }

        private void UpdateAim(Player player)
        {
            Order previous = Aimed;
            Aimed = null;

            if (player.InPlaceMode() && !TypingOrMenuOpen() && !InventoryGui.IsVisible() && Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                float best = float.MaxValue;
                foreach (Order o in _orders.Values)
                {
                    if (!_ghosts.ContainsKey(o.Id)) continue;
                    Vector3 to = o.Pos - cam.position;
                    float along = Vector3.Dot(to, cam.forward);
                    if (along < 0.5f || along > 30f) continue;
                    float off = (to - cam.forward * along).magnitude;
                    if (off < 1.0f + along * 0.03f && off < best) { best = off; Aimed = o; }
                }
            }

            if (Aimed != previous)
            {
                if (previous != null) SetHighlight(previous, false);
                if (Aimed != null) SetHighlight(Aimed, true);
            }

            if (Aimed == null) return;

            if (Input.GetKeyDown(_selectKey.Value)) SelectPieceOf(player, Aimed);
            if (Input.GetKeyDown(_removeKey.Value))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    Order center = Aimed;
                    List<Order> cluster = _orders.Values.Where(o => (o.Pos - center.Pos).sqrMagnitude <= 64f).ToList();
                    foreach (Order o in cluster) RemoveOrder(o.Id, broadcast: true);
                    player.Message(MessageHud.MessageType.TopLeft, $"Removed {cluster.Count} build order(s)");
                }
                else
                {
                    string name = PieceName(Aimed.Prefab);
                    RemoveOrder(Aimed.Id, broadcast: true);
                    player.Message(MessageHud.MessageType.TopLeft, $"Removed build order: {name}");
                }
            }
        }

        private void SelectPieceOf(Player player, Order order)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(order.Prefab);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece != null && player.SetSelectedPiece(piece))
                player.Message(MessageHud.MessageType.TopLeft, $"Selected: {PieceName(order.Prefab)}");
            else
                player.Message(MessageHud.MessageType.TopLeft, "That piece isn't in your hammer's build menu yet.");
        }

        // ---- finishing orders -------------------------------------------------------------------

        /// <summary>An order is done when a real piece of the same kind (and about the same rotation) stands on it.</summary>
        private void CheckCompletion(Player player)
        {
            if (_orders.Count == 0) return;
            var found = new List<Piece>();
            foreach (Order o in _orders.Values.ToList())
            {
                if ((o.Pos - player.transform.position).sqrMagnitude > 80f * 80f) continue; // pieces out there aren't loaded for us

                found.Clear();
                Piece.GetAllPiecesInRadius(o.Pos, 0.5f, found);
                foreach (Piece p in found)
                {
                    if (p == null) continue;
                    ZNetView view = p.GetComponent<ZNetView>();
                    if (view == null || !view.IsValid()) continue; // our own ghosts have no network object: not real pieces
                    if (Plain(p.gameObject.name) != o.Prefab) continue;
                    if (Quaternion.Angle(p.transform.rotation, o.Rot) > 25f) continue;

                    RemoveOrder(o.Id, broadcast: true);
                    player.Message(MessageHud.MessageType.TopLeft, $"Build order done: {PieceName(o.Prefab)}");
                    break;
                }
            }
        }

        // ---- helpers ----------------------------------------------------------------------------

        internal static string Plain(string objectName) => (objectName ?? "").Replace("(Clone)", "").Trim();

        internal static string PieceName(string prefabName)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabName) : null;
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            return piece != null ? Localization.instance.Localize(piece.m_name) : prefabName;
        }

        /// <summary>True when the key press belongs to something else (chat, console, a text box, the game menu).</summary>
        private static bool TypingOrMenuOpen() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            Minimap.InTextInput() || Menu.IsVisible();

        internal bool PlanKeyHeld => _enabled.Value && Input.GetKey(_planKey.Value);
    }
}
