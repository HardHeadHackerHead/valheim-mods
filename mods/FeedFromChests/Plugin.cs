using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FeedFromChests
{
    /// <summary>
    /// Smelters, kilns, cooking racks, fires and fermenters can use items from nearby chests.
    ///   * Press E at a station when you're carrying none of what it takes but a chest has some: one kind is added for you,
    ///     several kinds open a menu to choose from.
    ///   * Look at a station and press the menu key to open that menu any time (inventory and chests together).
    /// The stations' own code does the adding, so all their rules (limits, queues, effects) stay exactly as in the game.
    ///
    /// Split across files: Plugin.cs (setup, E and the hotkey), Menu.cs (the window), Stations.cs (what each station is),
    /// Feed.cs (making the game look in chests while we add), Chests.cs (finding and emptying chests).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.dhack.feedfromchests";
        public const string Name = "FeedFromChests";
        public const string Version = "1.2.1";

        internal static Plugin Instance;

        private ConfigEntry<bool> _enabled, _autoFeed;
        private ConfigEntry<float> _radius;
        private ConfigEntry<KeyCode> _menuKey;
        private ConfigEntry<int> _fillLimit;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            _radius = Config.Bind("General", "Radius", 15f, "How far (in metres) from the station a chest can be and still be used.");
            _autoFeed = Config.Bind("General", "AutoFeedOnUse", true,
                "Pressing E at a station when you carry nothing it takes, but a nearby chest has some: add it for you (or open the menu if there's a choice).");
            _menuKey = Config.Bind("General", "MenuKey", KeyCode.F,
                "Look at a station and press this to open the add-items menu any time (it lists your inventory and nearby chests).");
            _fillLimit = Config.Bind("General", "FillLimit", 100,
                "Safety limit: the most of one item the menu's Fill button will put in at once. (Fill stops sooner when the station is full or you run out.)");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            ContainerRegistry.Seed(); // chests that already exist; new ones are added as they appear

            Logger.LogInfo($"{Name} {Version} loaded (menu key: {_menuKey.Value})");

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded | menu key: {_menuKey.Value}", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            MenuOpen = false;
            ContainerRegistry.Clear();
            if (Instance == this) Instance = null;
            _harmony?.UnpatchSelf();
            DestroyMenuResources();
        }

        private void Update()
        {
            if (_pending != null) { Action a = _pending; _pending = null; a(); }

            Player player = Player.m_localPlayer;
            if (!_enabled.Value || player == null || player.IsDead()) return;

            if (MenuOpen) { UpdateMenu(player); return; }

            if (Input.GetKeyDown(_menuKey.Value) && !TypingOrMenuOpen())
            {
                if (Stations.TryGet(player.GetHoverObject(), out StationInfo info, out _)) OpenMenu(player, info);
            }
        }

        private static bool TypingOrMenuOpen() =>
            (Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() || TextInput.IsVisible() ||
            Minimap.InTextInput() || Menu.IsVisible() || InventoryGui.IsVisible();

        private Action _pending; // button clicks run from Update, not mid-draw, so the layout never changes under IMGUI

        // ---- adding things --------------------------------------------------------------------------

        /// <summary>
        /// Ask the station to take one of an item, drawing on your inventory and the chests. True if it took it.
        /// Pass <paramref name="chests"/> when adding many in a row, so the chests are only looked up once.
        /// </summary>
        internal bool AddOne(Player player, StationInfo info, ItemDrop drop, bool isFuel, List<Container> chests = null)
        {
            if (info == null || !info.Alive) return false;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            Feed.Chests = chests ?? Chests.Near(info.Position, _radius.Value);
            string name = drop.m_itemData.m_shared.m_name;
            long lookup = clock.ElapsedMilliseconds;

            // Stations that take a stand-in item don't check that you really have one, so we must: never add what we couldn't pay for.
            int have = player.GetInventory().CountItems(name) + Chests.Count(Feed.Chests, name) - Feed.ReservedFor(name);
            if (have <= 0) return false;
            long counted = clock.ElapsedMilliseconds;

            Outcome outcome;
            Feed.Active = true; // the game's questions about your inventory now include the chests
            try { outcome = (isFuel ? info.AddFuel : info.AddInput)(player, Stations.StandIn(drop)); }
            finally { Feed.Active = false; }
            long station = clock.ElapsedMilliseconds;

            if (outcome == Outcome.AddedTakeOne) TakeOne(player, name);
            long total = clock.ElapsedMilliseconds;

            // For tracking down slowness: say where the time went whenever one add takes noticeably long.
            if (total >= 12) Logger.LogInfo($"Slow add of {name}: {total} ms total (chest lookup {lookup}, counting {counted - lookup}, the station's own add {station - counted}, taking from chests {total - station})");
            return outcome != Outcome.Failed;
        }

        /// <summary>For stand-in items the game used nothing real, so we take one real item: from your inventory, else a chest.</summary>
        private void TakeOne(Player player, string itemName)
        {
            Inventory inventory = player.GetInventory();
            if (inventory.CountItems(itemName) > 0) inventory.RemoveItem(itemName, 1);
            else if (Feed.Reserved != null) Feed.Reserve(itemName, 1);   // during a big fill the chest is emptied once, at the end
            else Chests.Take(Feed.Chests, itemName, 1);
        }

        // ---- filling (many at once) -----------------------------------------------------------------------

        private bool _filling;

        /// <summary>
        /// Fill up with the given items, a couple per frame (so the station's sounds and effects don't all fire in one frame),
        /// then take everything we used out of the chests in one go (so each chest is only saved once).
        /// </summary>
        private System.Collections.IEnumerator FillRoutine(Player player, StationInfo info, List<Row> rows)
        {
            _filling = true;
            List<Container> chests = Chests.Near(info.Position, _radius.Value);
            Feed.Reserved = new Dictionary<string, int>();
            var parts = new List<string>();
            try
            {
                foreach (Row row in rows)
                {
                    int added = 0;
                    for (int i = 0; i < _fillLimit.Value; i++)
                    {
                        if (!info.Alive || !AddOne(player, info, row.Drop, row.IsFuel, chests)) break;
                        added++;
                        if (added % 2 == 0) yield return null; // two per frame
                    }
                    if (added > 0) parts.Add($"{added} {row.Display}");
                }
            }
            finally
            {
                // Settle up with the chests: one removal per item type, however many were used.
                Dictionary<string, int> used = Feed.Reserved;
                Feed.Reserved = null;
                if (used != null) foreach (var kv in used) Chests.Take(chests, kv.Key, kv.Value);
                _filling = false;
            }

            _status = parts.Count > 0 ? "Filled: " + string.Join(", ", parts.ToArray()) : "Nothing more fits right now.";
            RefreshRows(player);
            if (_rows.Count == 0) CloseMenu();
        }

        // ---- pressing E ------------------------------------------------------------------------------

        /// <summary>
        /// Called when you press E on something. Returns true if we handled it (so the game shouldn't). We only step in when you carry
        /// none of what the station takes but a chest has some; otherwise the game does exactly what it always did.
        /// </summary>
        internal bool TryAutoFeed(Player player, GameObject go)
        {
            if (!_enabled.Value || !_autoFeed.Value) return false;
            if (!Stations.TryGet(go, out StationInfo info, out Purpose? purpose) || purpose == null) return false;

            bool fuel = purpose == Purpose.Fuel;
            List<ItemDrop> candidates = fuel ? (info.Fuel != null ? new List<ItemDrop> { info.Fuel } : new List<ItemDrop>()) : info.Inputs;
            if (candidates.Count == 0) return false;

            Inventory inventory = player.GetInventory();
            if (candidates.Any(d => inventory.CountItems(d.m_itemData.m_shared.m_name) > 0)) return false; // you have some: the game handles it

            List<Container> chests = Chests.Near(info.Position, _radius.Value);
            List<ItemDrop> inChests = candidates.Where(d => Chests.Count(chests, d.m_itemData.m_shared.m_name) > 0).ToList();
            if (inChests.Count == 0) return false; // nothing in the chests either: the game says "nothing to add" as usual

            if (inChests.Count == 1) AddOne(player, info, inChests[0], fuel);
            else OpenMenu(player, info); // a choice to make
            return true;
        }
    }

    // Pressing E (or the gamepad's use button) on something goes through here.
    [HarmonyPatch(typeof(Player), "Interact")]
    internal static class Player_Interact
    {
        private static bool Prefix(Player __instance, GameObject go, bool hold)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || hold || __instance != Player.m_localPlayer) return true;
            try { return !plugin.TryAutoFeed(__instance, go); }
            catch (Exception e) { plugin.Log("Auto-feed failed, using the normal behaviour: " + e.Message); return true; }
        }
    }
}
