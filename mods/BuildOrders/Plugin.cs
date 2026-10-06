using System.Collections;
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
        public const string Version = "1.9.1";

        internal static Plugin Instance;

        private ConfigEntry<bool> _enabled, _showGhosts, _alwaysShowPanel, _buildByHand;
        private ConfigEntry<float> _buildReach, _buildAllRadius;
        private ConfigEntry<int> _maxGhosts;
        private ConfigEntry<KeyCode> _stabilityKey;
        private ConfigEntry<bool> _stabilityInPlan;
        private ConfigEntry<float> _ghostOpacity;
        private ConfigEntry<string> _shaderOverride;
        private ConfigEntry<float> _viewDistance, _snapDistance;
        private ConfigEntry<KeyCode> _planKey, _selectKey, _removeKey, _toggleGhostsKey;
        private ConfigEntry<bool> _planToggle, _swimBuild;
        internal bool BuildWhileSwimming => _enabled.Value && _swimBuild.Value;
        private ConfigEntry<float> _panelX, _panelY;

        private Harmony _harmony;
        private int _awakeFrame;

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            BindBlueprintConfig();
            _showGhosts = Config.Bind("General", "ShowGhosts", true, "Show the glowing ghosts of planned pieces.");
            _buildByHand = Config.Bind("General", "BuildByPressingUse", true,
                "Walk up to a ghost and press E to build it, no hammer needed. It costs the normal materials (from your inventory, then nearby chests if BuildFromChests is installed).");
            BindFetch();
            _swimBuild = Config.Bind("General", "BuildWhileSwimming", true,
                "Keep your hammer in your hand while swimming so you can plan and build from the water (equip it before you jump in: the game does not let you equip things while swimming).");
            _buildAllRadius = Config.Bind("General", "BuildAllRadius", 24f, new ConfigDescription("Holding E at a ghost (or Build nearby in the Plans window) builds every ghost within this many metres, lowest first, as far as your materials go.", new AcceptableValueRange<float>(4f, 64f)));
            _stabilityKey = Config.Bind("Keys", "StabilityKey", KeyCode.F10, "Show or hide the estimated stability colours on the ghosts (blue = solid, green to red = weaker, red = would fall).");
            _stabilityInPlan = Config.Bind("General", "StabilityInPlanMode", true, "Show the stability colours automatically while plan mode is on.");
            _buildReach = Config.Bind("General", "UseReach", 6f, "How close (in metres) you must be to a ghost to build it by pressing E.");
            _ghostOpacity = Config.Bind("Look", "GhostOpacity", 0.18f,
                "How solid the ghosts are: 0.05 = barely there, 0.3 = clearly visible, 1 = solid. (Aimed-at ghosts are shown more solid.)");
            _shaderOverride = Config.Bind("Look", "GhostShader", "",
                "Advanced: force a particular shader name for the ghosts. Leave blank to pick the best transparent one automatically (the choice is written to the BepInEx log).");
            _viewDistance = Config.Bind("General", "ViewDistance", 80f, "Ghosts further than this many metres away are hidden (saves performance).");
            _maxGhosts = Config.Bind("General", "MaxGhosts", 600, new ConfigDescription("Most ghosts shown at once (the nearest first). Big plans need more; very high numbers can cost frame rate.", new AcceptableValueRange<int>(50, 2000)));
            _snapDistance = Config.Bind("General", "SnapDistance", 1.5f,
                "When you're placing the same piece as a nearby order, your placement ghost snaps onto the order if it's within this many metres.");
            _planKey = Config.Bind("Keys", "PlanKey", KeyCode.LeftAlt,
                "With the hammer out, press this to turn plan mode on or off (or hold it, see PlanIsToggle). In plan mode, placing a piece records a build order instead of building it. Costs nothing.");
            _planToggle = Config.Bind("Keys", "PlanIsToggle", true,
                "On: press the plan key once to turn plan mode on, again to turn it off (it also ends when you put the hammer away). Off: plan mode only while the key is held.");
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
            Log = Logger;
            BindBridgeConfig();
            PlansPatches.Apply(_harmony);
            if (ZNetScene.instance != null) RegisterBridgeTool(ZNetScene.instance);   // hot reload while in a world

            Logger.LogInfo($"{Name} {Version} loaded ({PlanHint})");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | {PlanHint}", Talker.Type.Normal);
        }

        // ScriptEngine destroys this copy when mods reload: undo everything we hooked into the game.
        internal static BepInEx.Logging.ManualLogSource Log;

        private void OnDestroy()
        {
            CancelPlacement();
            PlansWindowOpen = false;
            DestroyWindowResources();
            _harmony?.UnpatchSelf();
            UnregisterRpc();
            UnregisterClaudeCommands();
            UnregisterBridgeTool();
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
            if (player == null || ZNetScene.instance == null) { DropPlacing(); return; }

            UpdatePlanMode(player);
            UpdateFetch(player);
            UpdateBlueprints(player);
            SetGhostColliders(PlanKeyHeld); // while planning, ghosts can be snapped onto like real pieces
            UpdateStability(player);

            if (Input.GetKeyDown(_toggleGhostsKey.Value) && !TypingOrMenuOpen())
            {
                _showGhosts.Value = !_showGhosts.Value;
                player.Message(MessageHud.MessageType.TopLeft, _showGhosts.Value ? "Build order ghosts shown" : "Build order ghosts hidden");
            }

            if (Time.time >= _nextGhostUpdate)
            {
                _nextGhostUpdate = Time.time + 0.25f; // a few ghosts at a time (see UpdateGhosts), so a big plan appears gradually
                UpdateGhosts(player);
                if (!Mathf.Approximately(_lastOpacity, _ghostOpacity.Value)) { _lastOpacity = _ghostOpacity.Value; RetintAll(); } // setting changed
            }
            if (Time.time >= _nextCompletion)
            {
                _nextCompletion = Time.time + 1f;
                CheckCompletion(player);
            }
            if (Time.time >= _nextRecords && WorldKnown)
            {
                _nextRecords = Time.time + 5f;
                UpdatePendingLevels(player);
                RetryPendingTerrain();
                SweepFinishedPlans();
            }

            UpdateAim(player);
        }

        private float _nextRecords;

        /// <summary>
        /// No player (died, logged out, disconnected) or another world: placing, the Plans window and drawing a bridge end, or the next world
        /// would start with the wheel and attacks held off and a click placing the blueprint.
        /// </summary>
        private void DropPlacing()
        {
            if (_placing != null) CancelPlacement();
            PlansWindowOpen = false;
            if (_bridgeStart.HasValue || BridgeOptionsOpen) CancelBridge();
        }

        /// <summary>Joined another world: forget what belonged to the last one (its levelling, and what was learnt from its pieces).</summary>
        private void ForgetWorld()
        {
            foreach (LevelJob job in _levelJobs.Values) job.Running = false;
            _levelJobs.Clear();
            _badPrefabs.Clear();
            _recordPlans.Clear();
            _bridgeTitlesAt = -99f;
            _nextRecords = 0f;
        }

        // ---- aiming at an order -----------------------------------------------------------------

        /// <summary>The order the player is looking at (only while building), or null.</summary>
        internal Order Aimed { get; private set; }

        private void UpdateAim(Player player)
        {
            Order previous = Aimed;
            Aimed = null;

            // With the hammer out you can aim at any ghost in view; without it, only close ones (to build them by pressing E).
            bool placing = player.InPlaceMode();
            if ((placing || _buildByHand.Value) && !TypingOrMenuOpen() && !InventoryGui.IsVisible() && Camera.main != null)
            {
                Transform cam = Camera.main.transform;
                float best = float.MaxValue;
                if (placing)
                {
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
                else
                {
                    // On foot: aim at the ghost's whole shape (not just its centre, which is far from the edge of a big wall or floor),
                    // for any ghost you are standing close to.
                    var ray = new Ray(cam.position, cam.forward);
                    Vector3 me = player.transform.position;
                    foreach (Order o in _orders.Values)
                    {
                        if ((o.Pos - me).sqrMagnitude > (_buildReach.Value + 8f) * (_buildReach.Value + 8f)) continue;
                        if (!GhostBounds(o, out Bounds shape)) continue;
                        if ((shape.ClosestPoint(me) - me).magnitude > _buildReach.Value) continue; // too far from you
                        Bounds grown = shape;
                        grown.Expand(0.5f); // a little forgiving
                        if (grown.IntersectRay(ray, out float hit) && hit < best) { best = hit; Aimed = o; }
                    }
                }
            }

            if (Aimed != previous)
            {
                if (previous != null) SetHighlight(previous, false);
                if (Aimed != null) SetHighlight(Aimed, true);
            }

            if (Aimed == null) { _holdStart = -1f; HoldProgress = 0f; return; }

            if (!placing)
            {
                RefreshBuildHint(player, Aimed);

                // E, unless there is something under the crosshair that E would actually operate (a door, a chest...). Not "nothing is
                // hovered": the game counts the floor, a wall or the terrain as hovered too.
                GameObject hovered = player.GetHoverObject();
                bool somethingToUse = hovered != null && hovered.GetComponentInParent<Interactable>() != null;
                if (_buildByHand.Value && !somethingToUse && HandleUseKey(player))
                {
                    Aimed = null; // start fresh next frame
                    return;
                }
            }

            if (Input.GetKeyDown(_selectKey.Value)) SelectPieceOf(player, Aimed);
            if (Input.GetKeyDown(_removeKey.Value))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    Order center = Aimed;
                    List<Order> cluster = _orders.Values.Where(o => (o.Pos - center.Pos).sqrMagnitude <= 64f).ToList();
                    foreach (Order o in cluster) RemoveOrder(o.Id, broadcast: true, save: false);
                    SaveOrders(); // once, not once per order
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

        // ---- building a ghost by pressing E ----------------------------------------------------------

        private readonly Dictionary<string, KeyValuePair<GameObject, Bounds>> _shapes = new Dictionary<string, KeyValuePair<GameObject, Bounds>>();

        /// <summary>The box around a ghost (worked out once per ghost).</summary>
        private bool GhostBounds(Order order, out Bounds bounds)
        {
            bounds = default;
            if (!_ghosts.TryGetValue(order.Id, out GameObject ghost) || ghost == null) return false;
            if (_shapes.TryGetValue(order.Id, out var cached) && cached.Key == ghost) { bounds = cached.Value; return true; }

            Renderer[] parts = ghost.GetComponentsInChildren<Renderer>();
            bounds = parts.Length > 0 ? parts[0].bounds : new Bounds(order.Pos, Vector3.one);
            for (int i = 1; i < parts.Length; i++) bounds.Encapsulate(parts[i].bounds);
            _shapes[order.Id] = new KeyValuePair<GameObject, Bounds>(ghost, bounds);
            return true;
        }

        private const string ForceKey = "DHack.BuildFromChests.ForceContext";
        private float _hintAt;
        private string _hintFor;
        internal bool HintAffordable;

        /// <summary>Tell BuildFromChests that we are building (it normally only counts chests while a hammer is out).</summary>
        private static void ForceBuildContext(bool on) => System.AppDomain.CurrentDomain.SetData(ForceKey, on ? (object)true : null);

        private bool CanAfford(Player player, Piece piece)
        {
            ForceBuildContext(true);
            try { return player.HaveRequirements(piece, Player.RequirementMode.CanBuild); }
            finally { ForceBuildContext(false); }
        }

        private static Piece PieceOf(Order order)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(order.Prefab) : null;
            return prefab != null ? prefab.GetComponent<Piece>() : null;
        }

        /// <summary>Do we have what the aimed ghost costs? Checked a couple of times a second (it also asks the chests).</summary>
        private void RefreshBuildHint(Player player, Order order)
        {
            if (_hintFor == order.Id && Time.unscaledTime - _hintAt < 0.4f) return;
            _hintFor = order.Id;
            _hintAt = Time.unscaledTime;
            Piece piece = PieceOf(order);
            if (!_stabilityShown && Time.unscaledTime - _promptStabilityAt > 3f) { _promptStabilityAt = Time.unscaledTime; ComputeStability(player); } // the prompt shows how well it would hold
            HintKnown = piece != null && player.IsRecipeKnown(piece.m_name);
            HintAffordable = HintKnown && CanAfford(player, piece);

            // What it costs and what you have (your inventory plus the nearby chests, the same count the build uses).
            HintPieceIcon = piece != null ? piece.m_icon : null;
            HintLines.Clear();
            if (piece == null) return;
            ForceBuildContext(true);
            try
            {
                Inventory inventory = player.GetInventory();
                foreach (Piece.Requirement req in piece.m_resources)
                {
                    if (req.m_resItem == null) continue;
                    int need = req.GetAmount(1);
                    if (need <= 0) continue;
                    string item = req.m_resItem.m_itemData.m_shared.m_name;
                    HintLines.Add(new HintLine { Name = Localization.instance.Localize(item), Have = inventory.CountItems(item), Need = need, Icon = req.m_resItem.m_itemData.GetIcon() });
                }

                // the whole plan this ghost belongs to: everything it still needs, against what you have
                PlanLines.Clear();
                var plan = _orders.Values.Where(x => (x.By ?? "") == (order.By ?? "")).ToList();
                PlanLeft = plan.Count;
                PlanTitle = (order.By ?? "").StartsWith(BlueprintPrefix) ? order.By.Substring(BlueprintPrefix.Length) : "Planned by " + (string.IsNullOrEmpty(order.By) ? "someone" : order.By);
                var totals = new Dictionary<string, HintLine>();
                foreach (Order x in plan)
                {
                    Piece p = PieceOf(x);
                    if (p == null) continue;
                    foreach (Piece.Requirement req in p.m_resources)
                    {
                        if (req.m_resItem == null || req.GetAmount(1) <= 0) continue;
                        string item = req.m_resItem.m_itemData.m_shared.m_name;
                        if (!totals.TryGetValue(item, out HintLine line))
                            totals[item] = line = new HintLine { Name = Localization.instance.Localize(item), Have = inventory.CountItems(item), Icon = req.m_resItem.m_itemData.GetIcon() };
                        line.Need += req.GetAmount(1);
                    }
                }
                PlanLines.AddRange(totals.Values.OrderByDescending(l => l.Need).Take(6));
            }
            finally { ForceBuildContext(false); }
            HoldCount = AffordableNearby(player, out int _, out HoldAll).Count;
        }

        internal readonly List<HintLine> PlanLines = new List<HintLine>();
        internal string PlanTitle = "";
        internal int PlanLeft, HoldCount, HoldAll;

        internal class HintLine { public string Name; public int Have, Need; public Sprite Icon; }
        internal Sprite HintPieceIcon;
        internal readonly List<HintLine> HintLines = new List<HintLine>();
        internal bool HintKnown;
        private float _promptStabilityAt;

        // ---- E: tap to build one, hold to build everything nearby -------------------------------------

        private const float HoldSeconds = 0.6f;
        private float _holdStart = -1f;
        private string _holdOrderId;
        private bool _holdFired, _building;

        /// <summary>0 to 1 while E is held toward "build everything nearby" (for the prompt).</summary>
        internal float HoldProgress;

        /// <summary>
        /// Tapping E builds the ghost you are aiming at; holding it a moment builds every ghost you can build within reach, supports
        /// first. Returns true when something was built or started (so the caller starts afresh next frame).
        /// </summary>
        private bool HandleUseKey(Player player)
        {
            if (_building) return false;
            bool down = ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse");
            bool held = ZInput.GetButton("Use") || ZInput.GetButton("JoyUse");

            if (down)
            {
                _holdStart = Time.unscaledTime;
                _holdOrderId = Aimed.Id;
                _holdFired = false;
            }
            if (_holdStart < 0f) { HoldProgress = 0f; return false; }

            if (held)
            {
                float t = Time.unscaledTime - _holdStart;
                HoldProgress = _holdFired ? 0f : Mathf.Clamp01(t / HoldSeconds);
                if (!_holdFired && t >= HoldSeconds)
                {
                    _holdFired = true;
                    HoldProgress = 0f;
                    StartCoroutine(BuildMany(player));
                    return true;
                }
                return false;
            }

            // released
            bool wasTap = !_holdFired;
            string id = _holdOrderId;
            _holdStart = -1f;
            HoldProgress = 0f;
            if (wasTap && id != null && _orders.TryGetValue(id, out Order order)) { BuildByHand(player, order); return true; }
            return false;
        }

        /// <summary>Build every ghost within reach, lowest first, as far as your materials go. Pieces nothing supports yet are skipped.</summary>
        private IEnumerator BuildMany(Player player)
        {
            _building = true;
            int built = 0, noMaterials = 0, unsupported = 0;
            try
            {
                // what you can afford, lowest first; then only the part of that which stands up by itself (on the ground, on what is
                // already built, or on other pieces built now), so building a plan bit by bit never wastes materials on a piece that falls
                List<Order> chosen = AffordableNearby(player, out noMaterials, out int all);
                var keep = new HashSet<string>(chosen.Select(o => o.Id));
                for (int round = 0; round < 6 && keep.Count > 0; round++)
                {
                    ComputeStability(player, keep);
                    var falls = keep.Where(id => _stability.TryGetValue(id, out Stab s) && s.Collapses).ToList();
                    if (falls.Count == 0) break;
                    foreach (string id in falls) keep.Remove(id);
                    unsupported += falls.Count;
                }

                foreach (Order o in chosen)
                {
                    if (!keep.Contains(o.Id) || !_orders.ContainsKey(o.Id)) continue;
                    if (TryBuild(player, o, quiet: true)) { built++; if (built % 2 == 0) yield return null; } // two per frame, so the effects are not all at once
                }
            }
            finally { _building = false; }

            string extra = (noMaterials > 0 ? $", {noMaterials} need materials" : "") + (unsupported > 0 ? $", {unsupported} skipped: nothing supports them yet" : "");
            player.Message(MessageHud.MessageType.Center, built > 0 ? $"Built {built} piece(s){extra}" : $"Nothing built{extra}");
            _stabilityDirty = true;
        }

        private void BuildByHand(Player player, Order order) => TryBuild(player, order, quiet: false);

        /// <summary>
        /// The ghosts within the build-all radius that your materials (inventory and nearby chests) cover, picked lowest first and counting the
        /// materials down as they would be used. <paramref name="short_"/> is how many more there are that you cannot afford yet.
        /// </summary>
        private List<Order> AffordableNearby(Player player, out int short_, out int all)
        {
            float radius = _buildAllRadius.Value;
            List<Order> batch = _orders.Values
                .Where(o => (o.Pos - player.transform.position).sqrMagnitude <= radius * radius && _ghosts.ContainsKey(o.Id))
                .OrderBy(o => Mathf.Round(o.Pos.y * 2f))                                        // bottom first
                .ThenBy(o => (o.Pos - player.transform.position).sqrMagnitude)
                .Take(600).ToList();
            all = batch.Count;
            short_ = 0;
            var chosen = new List<Order>();
            var budget = new Dictionary<string, int>();
            ForceBuildContext(true); // count the nearby chests too, as building does
            try
            {
                Inventory inventory = player.GetInventory();
                bool free = false;
                foreach (Order o in batch)
                {
                    Piece piece = PieceOf(o);
                    if (piece == null || !player.IsRecipeKnown(piece.m_name)) continue;
                    free = ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
                    bool ok = true;
                    foreach (Piece.Requirement req in piece.m_resources)
                    {
                        if (req.m_resItem == null || free) continue;
                        string item = req.m_resItem.m_itemData.m_shared.m_name;
                        if (!budget.ContainsKey(item)) budget[item] = inventory.CountItems(item);
                        if (budget[item] < req.GetAmount(1)) { ok = false; break; }
                    }
                    if (!ok) { short_++; continue; }
                    if (!free)
                        foreach (Piece.Requirement req in piece.m_resources)
                            if (req.m_resItem != null) budget[req.m_resItem.m_itemData.m_shared.m_name] -= req.GetAmount(1);
                    chosen.Add(o);
                }
            }
            finally { ForceBuildContext(false); }
            return chosen;
        }

        /// <summary>Build one ghost. Returns true if it was built. <paramref name="quiet"/> keeps the per-piece messages off.</summary>
        private bool TryBuild(Player player, Order order, bool quiet)
        {
            void Say(string text) { if (!quiet) player.Message(MessageHud.MessageType.Center, text); }
            Piece piece = PieceOf(order);
            string name = PieceName(order.Prefab);
            if (piece == null) { Say("That piece isn't available"); return false; }
            if (!player.IsRecipeKnown(piece.m_name)) { Say($"You haven't unlocked {name} yet"); return false; }
            if (!PrivateArea.CheckAccess(order.Pos, 0f, !quiet, false)) { Say("That area is protected"); return false; }
            if (Location.IsInsideNoBuildLocation(order.Pos)) { Say("You can't build there"); return false; }
            if (!CanAfford(player, piece)) { Say($"Missing materials for {name}"); return false; }

            ForceBuildContext(true); // so the materials come from nearby chests too, exactly as when building with the hammer
            try
            {
                player.PlacePiece(piece, order.Pos, order.Rot, doAttack: false);
                if (!ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) player.ConsumeResources(piece.m_resources, 0);
            }
            catch (System.Exception e)
            {
                Logger.LogError($"Could not build {name}: {e}");
                Say($"Could not build {name} (see the BepInEx log)");
                return false;
            }
            finally { ForceBuildContext(false); }

            RemoveOrder(order.Id, broadcast: true);
            if (!quiet) player.Message(MessageHud.MessageType.TopLeft, $"Built: {name}");
            _stabilityDirty = true;
            return true;
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
        private int _completionCursor;
        private readonly Collider[] _hits = new Collider[32];
        private int _pieceMask = -1;

        private void CheckCompletion(Player player)
        {
            if (_orders.Count == 0) return;
            if (_pieceMask == -1) _pieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "Default_small");

            // Ask the physics system what's standing at each order (it only looks nearby), instead of walking the game's list of
            // every piece in the world. And look at a few orders per second, taking turns, so a big plan never costs a whole frame.
            List<Order> all = _orders.Values.ToList();
            int budget = Mathf.Min(8, all.Count);
            for (int n = 0; n < budget; n++)
            {
                Order o = all[_completionCursor++ % all.Count];
                if ((o.Pos - player.transform.position).sqrMagnitude > 80f * 80f) continue; // pieces out there aren't loaded for us

                int count = Physics.OverlapSphereNonAlloc(o.Pos, 0.6f, _hits, _pieceMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Piece p = _hits[i] != null ? _hits[i].GetComponentInParent<Piece>() : null;
                    if (p == null) continue;
                    if ((p.transform.position - o.Pos).sqrMagnitude > 0.5f * 0.5f) continue;     // standing on the order, not just near it
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

        // ---- plan mode (a toggle, or held, as you prefer) ----------------------------------------

        private bool _planMode;

        /// <summary>True while plan mode is on: placing records a build order instead of building. (Named for when it was only a held key.)</summary>
        internal bool PlanKeyHeld => _enabled.Value && _planMode;

        private void UpdatePlanMode(Player player)
        {
            bool before = _planMode;
            if (!_enabled.Value || !player.InPlaceMode()) _planMode = false; // putting the hammer away always ends it, so you never plan by mistake later
            else if (_planToggle.Value)
            {
                if (Input.GetKeyDown(_planKey.Value) && !TypingOrMenuOpen()) _planMode = !_planMode;
            }
            else _planMode = Input.GetKey(_planKey.Value);

            if (_planMode != before)
                player.Message(MessageHud.MessageType.TopLeft, _planMode ? "Plan mode ON: placing now plans a build order" : "Plan mode off: placing builds as usual");
        }

        private string PlanHint => _planToggle.Value ? $"Press {_planKey.Value} to plan" : $"Hold {_planKey.Value} + place to plan";
    }
}
