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
        public const string Version = "1.6.0";

        internal static Plugin Instance;
        internal static DHack.Shared.ServerSettings Synced; // settings the server decides in multiplayer

        private ConfigEntry<bool> _enabled, _autoFeed, _alwaysOpenMenu, _stationAuto, _takeOffCooked, _refuelLights, _refuelFires, _collectHoney;
        private ConfigEntry<int> _keepFuel;
        private ConfigEntry<bool> _fuelShields, _reloadBallistas, _collectSap;
        internal int KeepFuel => Mathf.Max(0, _keepFuel.Value);
        private ConfigEntry<float> _radius, _autoInterval, _autoRange, _outputRadius, _autoRadius;
        private ConfigEntry<int> _fillLimit;

        private Harmony _harmony;

        internal bool AutoEnabled => _stationAuto != null && _stationAuto.Value;
        internal float ChestRadius => _radius.Value;
        internal float OutputRadius => _outputRadius.Value;
        internal float FeedRadius => _autoRadius.Value;
        internal bool CollectsHoney => _enabled.Value && _collectHoney.Value;
        internal bool HiveInfo => _enabled.Value;
        internal bool RefuelsItself(Fireplace f) => _enabled.Value && Lights.Refuels(f, _refuelLights.Value, _refuelFires.Value);
        internal void Info(string message) => Logger.LogInfo(message);

        private void Awake()
        {
            Instance = this;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string server = " In multiplayer the server's value applies.";
            _enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off.");
            _radius = Synced.Add(Config.Bind("General", "Radius", 15f,
                new ConfigDescription("How far (in metres) from the station a chest can be and still be used." + server, new AcceptableValueRange<float>(2f, 50f))));
            _autoFeed = Config.Bind("General", "AutoFeedOnUse", true,
                "Pressing E at a station when you carry nothing it takes, but a nearby chest has some: add it for you (or open the menu if there's a choice).");
            _alwaysOpenMenu = Config.Bind("General", "AlwaysOpenMenu", false,
                "Pressing E at a station when you rely on chests: off = add one automatically if there's only one kind to add (menu only for a choice); on = always open the menu (so Fill is always at hand).");
            _fillLimit = Config.Bind("General", "FillLimit", 100,
                "Safety limit: the most of one item the menu's Fill button will put in at once. (Fill stops sooner when the station is full or you run out.)");

            _stationAuto = Config.Bind("AutoFeed", "Enabled", true,
                "Allow smelters, kilns and furnaces to be set to keep themselves stocked from nearby chests (set up in the station's menu).");
            _autoRadius = Synced.Add(Config.Bind("AutoFeed", "FeedRadius", 30f, new ConfigDescription(
                "How far (in metres) from a smelter or kiln a chest can be and still be used to keep it stocked automatically." + server, new AcceptableValueRange<float>(2f, 60f))));
            _outputRadius = Synced.Add(Config.Bind("AutoFeed", "OutputRadius", 30f, new ConfigDescription(
                "How far (in metres) from a smelter or kiln a chest can be and still receive what it makes (only chests assigned to it, with the chest assign menu, K)." + server,
                new AcceptableValueRange<float>(2f, 60f))));
            _autoInterval = Config.Bind("AutoFeed", "Interval", 1f, "Seconds between automatic top-ups of each station.");
            _autoRange = Synced.Add(Config.Bind("AutoFeed", "PlayerRange", 40f, new ConfigDescription(
                "Automatic feeding only runs while you are within this many metres of the station (the game only loads chests near players)." + server,
                new AcceptableValueRange<float>(5f, 100f))));
            _takeOffCooked = Config.Bind("Cooking", "TakeOffCooked", true,
                "Food on a cooking station comes off by itself the moment it is done, so it never burns: into a chest assigned to it or to Food " +
                "(the chest assign menu, K), else it slides off the side of the spit. Each station can also be switched off in its menu (E).");
            _refuelLights = Config.Bind("Fires", "RefuelTorches", true,
                "Every torch, sconce and brazier keeps itself lit: when it has room for more fuel (resin, coal...), one is taken from a chest " +
                "within FeedRadius of it. For all of them at once, no setup per torch. Runs while you are within PlayerRange; never uses your inventory.");
            _collectHoney = Config.Bind("Beehives", "CollectHoney", true,
                "Beehives near you put their honey into a chest (one assigned to honey or to Food with K, else one that already has honey), " +
                "so they never sit full: a full hive stops making honey. With no such chest within OutputRadius the honey stays in the hive.");
            _refuelFires = Config.Bind("Fires", "RefuelCampfires", false,
                "The same for fires that burn wood (campfires, hearths, bonfires): keep them topped up with wood from nearby chests.");
            _fuelShields = Config.Bind("Defenses", "FuelShieldGenerators", true,
                "Shield generators near you take their fuel (bones and the like) from chests within FeedRadius as they have room, so the shield never runs dry. All of them at once.");
            _reloadBallistas = Config.Bind("Defenses", "ReloadBallistas", true,
                "Ballistas near you are reloaded from chests within FeedRadius: with the ammo they hold, or when empty the first ammo they take that the chests have.");
            _collectSap = Config.Bind("Defenses", "CollectSap", true,
                "Sap extractors near you empty themselves into a chest assigned to sap or Materials (K), else one that already holds sap, so they never sit full.");
            _keepFuel = Config.Bind("Fires", "KeepFuel", 10,
                "Torches and fires never take the last of a fuel: this many of it (resin, coal, wood...) always stay in the chests, for crafting.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            ContainerRegistry.Seed(); // chests that already exist; new ones are added as they appear
            AutoFeed.Seed();          // so do the stations
            Cooking.Seed();
            Lights.Seed();
            Hives.Seed();
            Ferment.Seed();

            Logger.LogInfo($"{Name} {Version} loaded");
            StartCoroutine(Warmup());

            // Awake with a player already in the world means this was a hot reload.
            if (Player.m_localPlayer != null && Chat.instance != null)
                Chat.instance.AddString("[Mod]", $"{Name} v{Version} reloaded", Talker.Type.Normal);
        }

        private void OnDestroy()
        {
            MenuOpen = false;
            AutoFeed.Clear();
            Cooking.Clear();
            Lights.Clear();
            Hives.Clear();
            Ferment.Clear();
            Feed.Prepaid = null;
            ContainerRegistry.Clear();
            if (Instance == this) Instance = null;
            _harmony?.UnpatchSelf();
            Synced?.Dispose();
            DestroyMenuResources();
        }

        // ---- finding slowness -------------------------------------------------------------------------

        private string _lastAction = "";
        private float _lastActionAt = -100f;
        private int _lastGc;

        /// <summary>Note what we just did, so a slow frame right after it can be put down to it.</summary>
        private void Mark(string action)
        {
            _lastAction = action;
            _lastActionAt = Time.unscaledTime;
        }

        /// <summary>If the previous frame was slow soon after one of our actions, say so (with whether memory cleanup happened).</summary>
        private void WatchForSpikes()
        {
            int gc = GC.CollectionCount(0);
            float frame = Time.unscaledDeltaTime;
            if (frame > 0.1f && Time.unscaledTime - _lastActionAt < 3f)
                Logger.LogInfo($"Slow frame: {frame * 1000f:0} ms, {gc - _lastGc} memory cleanup(s), shortly after: {_lastAction}");
            _lastGc = gc;
        }

        /// <summary>
        /// The first time Unity runs a piece of new code it has to compile it, which can take a noticeable moment. Do that for all of
        /// our code a little while after loading, a few methods per frame, so the first click on a station isn't the one that pays for it.
        /// </summary>
        private System.Collections.IEnumerator Warmup()
        {
            yield return new WaitForSeconds(3f); // let the game settle first
            var methods = new List<System.Reflection.MethodBase>();
            foreach (Type t in typeof(Plugin).Assembly.GetTypes())
            {
                if (t.IsGenericTypeDefinition) continue;
                const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
                foreach (System.Reflection.MethodBase m in t.GetMethods(all)) methods.Add(m);
            }

            int done = 0;
            foreach (System.Reflection.MethodBase m in methods)
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                try { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle); } catch (Exception) { /* some can't be pre-compiled: fine */ }
                if (++done % 4 == 0) yield return null; // four per frame
            }
            Logger.LogInfo($"Warmed up {done} methods");
        }

        private void Update()
        {
            Synced?.Update(); // notices joining and leaving a server, for the settings it decides
            WatchForSpikes();
            if (_pending != null) { Action a = _pending; _pending = null; a(); }

            Player player = Player.m_localPlayer;
            if (!_enabled.Value || player == null || player.IsDead()) return;

            if (_stationAuto.Value && !_filling) AutoFeed.Tick(this, player, _autoInterval.Value, _autoRange.Value); // stations set to feed themselves
            if (_stationAuto.Value && !_filling) Cooking.Tick(this, player, _autoRange.Value, _takeOffCooked.Value); // spits: done food off, raw food on
            if (!_filling) Lights.Tick(this, player, _autoRange.Value, _refuelLights.Value, _refuelFires.Value);   // torches keep themselves lit
            if (!_filling && _collectHoney.Value) Hives.Tick(this, player, _autoRange.Value, _outputRadius.Value); // hives never sit full
            if (!_filling) Defenses.Tick(this, player, _autoRange.Value, _autoRadius.Value, _outputRadius.Value, _fuelShields.Value, _reloadBallistas.Value, _collectSap.Value);
            if (_stationAuto.Value && !_filling) Ferment.Tick(this, player, _autoRange.Value);                    // fermenters reload and tap themselves

            if (MenuOpen) UpdateMenu(player);
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
        internal bool AddOne(Player player, StationInfo info, ItemDrop drop, bool isFuel, List<Container> chests = null, bool chestsOnly = false)
        {
            if (info == null || !info.Alive) return false;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            Feed.Chests = chests ?? Chests.Near(info.Position, _radius.Value);
            string name = drop.m_itemData.m_shared.m_name;
            long lookup = clock.ElapsedMilliseconds;

            // Pay first: from your inventory if you carry one, else take one out of a chest now (a chest can be opened or emptied by
            // someone else at any moment, so it's taken before the station is asked, never after). If the station then doesn't take it,
            // it goes back.
            Inventory inventory = player.GetInventory();
            bool fromInventory = !chestsOnly && Chests.OwnCount(inventory, name) > 0;
            Container from = null;
            ItemDrop.ItemData taken = fromInventory ? null : Chests.TakeOne(Feed.Chests, name, out from);
            if (!fromInventory && taken == null) return false;
            long counted = clock.ElapsedMilliseconds;

            Outcome outcome;
            Feed.Prepaid = taken != null ? name : null;
            Feed.Active = true; // the game's questions about your inventory now include the item taken out
            Feed.ChestsOnly = chestsOnly;
            try { outcome = (isFuel ? info.AddFuel : info.AddInput)(player, Stations.StandIn(drop)); }
            finally { Feed.Active = false; Feed.ChestsOnly = false; }
            bool unused = Feed.Prepaid != null; // the game's "use one" took it (the fuel route), or not
            Feed.Prepaid = null;
            long station = clock.ElapsedMilliseconds;

            if (outcome == Outcome.AddedTakeOne)
            {
                // A stand-in went in, so the game used nothing real: the item taken out is the one it used, or one from your inventory.
                if (taken != null) unused = false;
                else inventory.RemoveItem(name, 1);
            }
            else if (outcome == Outcome.Failed && taken != null) unused = true;
            if (unused && taken != null) Chests.PutBack(taken, from, player, info.Position);
            long total = clock.ElapsedMilliseconds;

            // For tracking down slowness: say where the time went whenever one add takes noticeably long.
            if (total >= 12) Logger.LogInfo($"Slow add of {name}: {total} ms total (chest lookup {lookup}, taking from a chest {counted - lookup}, the station's own add {station - counted}, settling {total - station})");
            return outcome != Outcome.Failed;
        }

        // ---- filling (many at once) -----------------------------------------------------------------------

        private bool _filling;

        /// <summary>
        /// Fill up with the given items, a couple per frame (so the station's sounds and effects don't all fire in one frame). Each one is
        /// paid for as it goes in, so stopping half way (the station is destroyed, the mod reloads) never leaves anything unpaid.
        /// </summary>
        private System.Collections.IEnumerator FillRoutine(Player player, StationInfo info, List<Row> rows)
        {
            _filling = true;
            List<Container> chests = Chests.Near(info.Position, _radius.Value);
            var parts = new List<string>();
            try
            {
                foreach (Row row in rows)
                {
                    int added = 0;
                    for (int i = 0; i < _fillLimit.Value; i++)
                    {
                        if (!info.Alive || player == null || !AddOne(player, info, row.Drop, row.IsFuel, chests)) break;
                        added++;
                        if (added % 2 == 0) yield return null; // two per frame
                    }
                    if (added > 0) parts.Add($"{added} {row.Display}");
                }
            }
            finally
            {
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
        private float _lastHintAt = -100f;

        internal bool TryAutoFeed(Player player, GameObject go, bool alt = false)
        {
            if (!_enabled.Value) return false;

            // Cooking stations: E takes everything that is done straight into your inventory; otherwise it opens the menu (what is
            // cooking, Add 1 / Fill, auto-feed). Alt+E leaves E to the game (put food on by hand).
            CookingStation cooking = go != null ? go.GetComponentInParent<CookingStation>() : null;
            if (cooking != null && !alt)
            {
                if (Cooking.HasDone(cooking))
                {
                    int taken = Cooking.TakeAllInto(player, cooking);
                    Mark($"took {taken} off a cooking station");
                    return taken > 0;
                }
                if (_stationAuto.Value && Stations.TryGet(go, out StationInfo cookInfo, out _))
                {
                    Mark("pressed E at a cooking station");
                    OpenMenu(player, cookInfo);
                    return true;
                }
            }
            if (cooking != null && alt) return false;

            // Fermenters: E on an empty one opens the menu (status, Add 1, auto-load); tapping a ready one and the rest are the game's.
            Fermenter fermenter = go != null ? go.GetComponentInParent<Fermenter>() : null;
            if (fermenter != null)
            {
                if (alt || !_stationAuto.Value || Ferment.StateOf(fermenter, out _) != Ferment.State.Empty) return false;
                if (Stations.TryGet(go, out StationInfo fermentInfo, out _))
                {
                    Mark("pressed E at a fermenter");
                    OpenMenu(player, fermentInfo);
                    return true;
                }
            }

            if (!Stations.TryGet(go, out StationInfo info, out Purpose? purpose) || purpose == null) return false;

            // Smelters, kilns and furnaces: E always opens the menu, whatever you carry, so you can always set up auto-feed.
            if (_stationAuto.Value && AutoFeed.Supported(info))
            {
                Mark("pressed E at a smelter");
                OpenMenu(player, info);
                return true;
            }

            if (!_autoFeed.Value) return false;

            bool fuel = purpose == Purpose.Fuel;
            List<ItemDrop> candidates = fuel ? (info.Fuel != null ? new List<ItemDrop> { info.Fuel } : new List<ItemDrop>()) : info.Inputs;
            if (candidates.Count == 0) return false;

            Inventory inventory = player.GetInventory();
            if (candidates.Any(d => inventory.CountItems(d.m_itemData.m_shared.m_name) > 0)) return false; // you have some: the game handles it

            List<Container> chests = Chests.Near(info.Position, _radius.Value);
            List<ItemDrop> inChests = candidates.Where(d => Chests.Count(chests, d.m_itemData.m_shared.m_name) > 0).ToList();
            if (inChests.Count == 0) return false; // nothing in the chests either: the game says "nothing to add" as usual

            Mark("pressed E at a station");
            if (inChests.Count == 1 && !_alwaysOpenMenu.Value)
            {
                AddOne(player, info, inChests[0], fuel);

                // With only one kind there's no menu, so nothing would tell you that Fill exists. Say so (not every single time).
                if (Time.unscaledTime - _lastHintAt > 30f)
                {
                    _lastHintAt = Time.unscaledTime;
                    player.Message(MessageHud.MessageType.TopLeft, "Tip: turn on AlwaysOpenMenu in the FeedFromChests config to get the menu (Fill, Add 1) every time");
                }
            }
            else OpenMenu(player, info); // a choice to make (or you asked to always see the menu)
            return true;
        }
    }

    // Pressing E (or the gamepad's use button) on something goes through here.
    [HarmonyPatch(typeof(Player), "Interact")]
    internal static class Player_Interact
    {
        private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || hold || __instance != Player.m_localPlayer) return true;
            try { return !plugin.TryAutoFeed(__instance, go, alt); }
            catch (Exception e) { plugin.Log("Auto-feed failed, using the normal behaviour: " + e.Message); return true; }
        }
    }
}
