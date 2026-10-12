using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Its pieces reload while you play: they stay registered across a reload and the ones standing get the new copy (Shared/PieceReload.cs).
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Recycler
{
    /// <summary>
    /// Adds a buildable Recycler (and a Press that upgrades it). Both are copies of the wooden chest drawn with new shapes, so no 3D model is needed.
    /// Use the Recycler like a crafting station: pick gear from your inventory, see what it returns, and recycle it.
    ///
    /// Split across files: Plugin.cs (setup, registering the pieces), Model.cs (looks), Station.cs (the pieces' behaviour),
    /// Calc.cs (what an item returns), Window.cs (the menu).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.recycler";
        public const string OldGuid = "com.dhack.recycler"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "Recycler";
        public const string Version = "1.3.3";
        public const string RecyclerPrefab = "piece_recycler";
        public const string PressPrefab = "piece_recycler_press";

        internal static ConfigEntry<int> BasePercent, Press1Bonus, Press2Bonus;
        
        internal static ConfigEntry<bool> ChanceRounding, RequireStation, ConfirmValuable, AllowWeapons, AllowArmor, AllowTools, ShowMessages;
        internal static ConfigEntry<string> RecyclerCost, PressCost;
        internal static BepInEx.Logging.ManualLogSource Log;
        private static bool _tellNoServer;   // the server hasn't the mod: say so once the player is in the world

        /// <summary>What the pieces cost before anyone changes it.</summary>
        internal const string DefaultRecyclerCost = "FineWood:10, Bronze:8, Stone:10";
        internal const string DefaultPressCost = "Iron:15, Bronze:10, Stone:10";
        internal static DHack.Shared.ServerSettings Synced; // settings and costs the server decides in multiplayer

        private static GameObject _holder;
        private static readonly List<GameObject> Prefabs = new List<GameObject>();
        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            // What it gives back and what it takes are the server's to decide in multiplayer; the rest (asking twice, messages) is each player's.
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string server = " In multiplayer the server's value applies.";
            var pct = new AcceptableValueRange<int>(0, 100);
            BasePercent = Synced.Add(Config.Bind("Refund", "BasePercent", 50, new ConfigDescription("Share of the materials you get back with no Press nearby (0-100)." + server, pct)));
            Press1Bonus = Synced.Add(Config.Bind("Refund", "OnePressBonus", 10, new ConfigDescription("Extra percentage points with one Press within 8 m." + server, pct)));
            Press2Bonus = Synced.Add(Config.Bind("Refund", "TwoPressBonus", 20, new ConfigDescription("Extra percentage points with two Presses within 8 m." + server, pct)));
            // The build menu's texts say the real shares (base, with one Press, with two), from the settings (the server's in multiplayer)
            foreach (string key in new[] { "recycler_piece_desc", "recycler_press_desc" })
            {
                DHack.Shared.Translations.Fill(key, "base", BasePercent, v => v.ToString());
                DHack.Shared.Translations.Fill(key, "one", () => Mathf.Clamp(BasePercent.Value + Press1Bonus.Value, 0, 100).ToString(), BasePercent, Press1Bonus);
                DHack.Shared.Translations.Fill(key, "two", () => Mathf.Clamp(BasePercent.Value + Press2Bonus.Value, 0, 100).ToString(), BasePercent, Press2Bonus);
            }
            ChanceRounding = Synced.Add(Config.Bind("Refund", "ChanceRounding", true, "Instead of always rounding down, a leftover fraction becomes a matching chance of one more (so 50% averages out to 50%)." + server));
            RequireStation = Synced.Add(Config.Bind("Rules", "RequireCraftingStation", true, "Gear can only be recycled with the crafting station (and level) it was made at nearby." + server));
            ConfirmValuable = Config.Bind("Rules", "ConfirmValuable", true, "Ask for a second click before recycling equipped or upgraded gear, or gear carrying other mods' data (enchantments, a bag's contents).");
            AllowWeapons = Synced.Add(Config.Bind("Rules", "AllowWeapons", true, "Weapons, bows and ammo launchers." + server));
            AllowArmor = Synced.Add(Config.Bind("Rules", "AllowArmor", true, "Armor, capes and shields." + server));
            AllowTools = Synced.Add(Config.Bind("Rules", "AllowTools", true, "Tools and torches." + server));
            ShowMessages = Config.Bind("General", "ShowMessages", true, "Show a short message in the top-left when something is recycled.");
            RecyclerCost = Synced.Bind("Building", "RecyclerCost", DefaultRecyclerCost, "What a Recycler costs to build with the hammer. " + DHack.Shared.Costs.Help);
            PressCost = Synced.Bind("Building", "PressCost", DefaultPressCost, "What a Recycler Press costs to build with the hammer. " + DHack.Shared.Costs.Help);
            RecyclerCost.SettingChanged += (s, e) => ApplyCost();
            PressCost.SettingChanged += (s, e) => ApplyCost();
            Synced.ServerChanged += () => _tellNoServer = Synced.Server == DHack.Shared.ServerSettings.ServerMod.Missing;

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) Register(ZNetScene.instance); // hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            Window.Close();
            GameUi.Destroy();
            DHack.Shared.Translations.Unload();
            _harmony?.UnpatchSelf();
            Synced?.Dispose();
            Unregister();
        }

        private void Update()
        {
            Synced?.Update(); // notices joining and leaving a server, for the settings it decides
            if (_tellNoServer && Player.m_localPlayer != null)
            {
                _tellNoServer = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$recycler_server_missing");
            }
            Window.Tick();
        }

        internal static void Register(ZNetScene scene)
        {
            GameObject source = scene.GetPrefab("piece_chest_wood");
            GameObject hammer = scene.GetPrefab("Hammer"); // items are in the scene too, and ObjectDB may not be awake yet
            if (source == null || hammer == null) { Debug.LogWarning("Recycler: chest or hammer prefab not found"); return; }

            if (Prefabs.Count == 0)
            {
                _holder = new GameObject("RecyclerPrefabs");
                _holder.SetActive(false); // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
                Material mat = source.GetComponentInChildren<Renderer>(true).sharedMaterial;
                Look.Harvest(scene, mat); // the game's own wood, stone and metal materials

                Prefabs.Add(Make(RecyclerPrefab, source, mat, "$recycler_piece", "$recycler_piece_desc",
                    true, Model.RecyclerHitCenter, Model.RecyclerHitSize, typeof(RecyclerStation),
                    RecyclerCost, DefaultRecyclerCost));
                Prefabs.Add(Make(PressPrefab, source, mat, "$recycler_press", "$recycler_press_desc",
                    false, Model.PressHitCenter, Model.PressHitSize, typeof(RecyclerPress),
                    PressCost, DefaultPressCost));
            }

            // registered in place of an older copy's (a hot reload while playing), and the pieces standing get this copy's code
            PieceTable table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            int swapped = 0;
            foreach (GameObject prefab in Prefabs)
            {
                DHack.Shared.PieceReload.ReplacePrefab(scene, prefab, table);
                Synced.NeedsServer(prefab);   // not in the build menu on a server without the mod (it would delete the piece)
                swapped += DHack.Shared.PieceReload.Reattach(scene, prefab);
            }
            if (swapped > 0) Debug.Log($"Recycler: {swapped} piece(s) in the world now run this copy");
        }

        private static GameObject Make(string prefabName, GameObject source, Material mat, string title, string description, bool recycler,
                                       Vector3 hitCenter, Vector3 hitSize, System.Type logic, ConfigEntry<string> cost, string defaultCost)
        {
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = prefabName;
            Container container = go.GetComponent<Container>();
            if (container != null) Object.DestroyImmediate(container); // these are not storage

            // The look can fail (a game update renames a material) but the piece must still be registered, or the host deletes the placed ones.
            try { Model.Build(go, mat, recycler, hitCenter, hitSize); }
            catch (System.Exception e) { Debug.LogWarning($"Recycler: could not build the look of {prefabName}, it keeps the chest's: {e.Message}"); }

            // snap points on the corners of its base, as far as the model reaches (the chest has none): the Recycler (its crank on the
            // right, the chute in front) and the Press line up with floor corners, wall ends and each other
            DHack.Shared.SnapPoints.Clear(go);
            if (recycler) DHack.Shared.SnapPoints.Box(go, new Vector3(-0.75f, 0f, -0.91f), new Vector3(0.94f, 0f, 0.77f));
            else DHack.Shared.SnapPoints.Box(go, new Vector3(-0.45f, 0f, -0.45f), new Vector3(0.45f, 0f, 0.45f));

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = title;
            piece.m_description = description;
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_usage = Piece.UsageTagFlags.Crafting;   // the game's build-menu tags: a station, not storage like the chest it's copied from
            // the cost setting (a server owner can change it); if neither it nor the default can be read, the chest's stays: never free to build
            if (!DHack.Shared.Costs.Apply(piece, cost.Value, Log)) DHack.Shared.Costs.Apply(piece, defaultCost, Log);
            try
            {
                Sprite picture = DHack.Shared.Icon.Make(go); // the build menu shows the Recycler itself, not the chest it was copied from
                if (picture != null) piece.m_icon = picture;
            }
            catch (System.Exception e) { Debug.LogWarning($"Recycler: could not draw the build-menu picture of {prefabName}: {e.Message}"); }
            go.AddComponent(logic);
            return go;
        }


        internal static void Unregister()
        {
            if (Prefabs.Count == 0) return;
            // out of the build menu, but still registered, so no area loading before the next copy is up loses its pieces
            // (Shared/PieceReload.cs); the next copy replaces them
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            foreach (GameObject prefab in Prefabs)
                DHack.Shared.PieceReload.UnloadPiece(prefab, hammer != null ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null);
            Prefabs.Clear();
            _holder = null;
        }

        /// <summary>The cost settings (the server's in multiplayer) on the pieces. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCost()
        {
            foreach (GameObject prefab in Prefabs)
            {
                Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece == null) continue;
                ConfigEntry<string> cost = prefab.name == PressPrefab ? PressCost : RecyclerCost;
                DHack.Shared.Costs.Apply(piece, cost.Value, Log);
            }
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) => Plugin.Register(__instance);
    }

    // Whichever of ZNetScene and ObjectDB wakes second, the piece is registered before any saved one is loaded.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() { if (ZNetScene.instance != null) Plugin.Register(ZNetScene.instance); }
    }

}
