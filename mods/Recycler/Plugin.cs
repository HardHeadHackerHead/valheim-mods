using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

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
        public const string Guid = "com.dhack.recycler";
        public const string Name = "Recycler";
        public const string Version = "1.1.0";
        public const string RecyclerPrefab = "piece_recycler";
        public const string PressPrefab = "piece_recycler_press";

        internal static ConfigEntry<int> BasePercent, Press1Bonus, Press2Bonus;
        
        internal static ConfigEntry<bool> ChanceRounding, RequireStation, ConfirmValuable, AllowWeapons, AllowArmor, AllowTools, ShowMessages;
        internal static DHack.Shared.ServerSettings Synced; // settings the server decides in multiplayer

        private static GameObject _holder;
        private static readonly List<GameObject> Prefabs = new List<GameObject>();
        private Harmony _harmony;

        private void Awake()
        {
            // What it gives back and what it takes are the server's to decide in multiplayer; the rest (asking twice, messages) is each player's.
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            const string server = " In multiplayer the server's value applies.";
            var pct = new AcceptableValueRange<int>(0, 100);
            BasePercent = Synced.Add(Config.Bind("Refund", "BasePercent", 50, new ConfigDescription("Share of the materials you get back with no Press nearby (0-100)." + server, pct)));
            Press1Bonus = Synced.Add(Config.Bind("Refund", "OnePressBonus", 10, new ConfigDescription("Extra percentage points with one Press within 8 m." + server, pct)));
            Press2Bonus = Synced.Add(Config.Bind("Refund", "TwoPressBonus", 20, new ConfigDescription("Extra percentage points with two Presses within 8 m." + server, pct)));
            ChanceRounding = Synced.Add(Config.Bind("Refund", "ChanceRounding", true, "Instead of always rounding down, a leftover fraction becomes a matching chance of one more (so 50% averages out to 50%)." + server));
            RequireStation = Synced.Add(Config.Bind("Rules", "RequireCraftingStation", true, "Gear can only be recycled with the crafting station (and level) it was made at nearby." + server));
            ConfirmValuable = Config.Bind("Rules", "ConfirmValuable", true, "Ask for a second click before recycling equipped or upgraded gear, or gear carrying other mods' data (enchantments, a bag's contents).");
            AllowWeapons = Synced.Add(Config.Bind("Rules", "AllowWeapons", true, "Weapons, bows and ammo launchers." + server));
            AllowArmor = Synced.Add(Config.Bind("Rules", "AllowArmor", true, "Armor, capes and shields." + server));
            AllowTools = Synced.Add(Config.Bind("Rules", "AllowTools", true, "Tools and torches." + server));
            ShowMessages = Config.Bind("General", "ShowMessages", true, "Show a short message in the top-left when something is recycled.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();
            if (ZNetScene.instance != null) Register(ZNetScene.instance); // hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            Window.Close();
            _harmony?.UnpatchSelf();
            Synced?.Dispose();
            Unregister();
        }

        private void Update()
        {
            Synced?.Update(); // notices joining and leaving a server, for the settings it decides
            Window.Tick();
        }
        private void OnGUI() => Window.Draw();

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

                Prefabs.Add(Make(RecyclerPrefab, source, mat, "Recycler", "Turns old gear back into a share of its crafting materials. Presses nearby raise the share.",
                    true, Model.RecyclerHitCenter, Model.RecyclerHitSize, typeof(RecyclerStation),
                    Req("FineWood", 10), Req("Bronze", 8), Req("Stone", 10)));
                Prefabs.Add(Make(PressPrefab, source, mat, "Recycler Press", "Place within 8 m of a Recycler to raise the share it returns. Up to two count.",
                    false, Model.PressHitCenter, Model.PressHitSize, typeof(RecyclerPress),
                    Req("Iron", 15), Req("Bronze", 10), Req("Stone", 10)));
            }

            var named = Named(scene);
            PieceTable table = hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
            foreach (GameObject prefab in Prefabs)
            {
                if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                named[prefab.name.GetStableHashCode()] = prefab; // lets it be placed and loaded from saves
                if (!table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
            }
        }

        private static GameObject Make(string prefabName, GameObject source, Material mat, string title, string description, bool recycler,
                                       Vector3 hitCenter, Vector3 hitSize, System.Type logic, params Piece.Requirement[] cost)
        {
            GameObject go = Object.Instantiate(source, _holder.transform);
            go.name = prefabName;
            Container container = go.GetComponent<Container>();
            if (container != null) Object.DestroyImmediate(container); // these are not storage

            // The look can fail (a game update renames a material) but the piece must still be registered, or the host deletes the placed ones.
            try { Model.Build(go, mat, recycler, hitCenter, hitSize); }
            catch (System.Exception e) { Debug.LogWarning($"Recycler: could not build the look of {prefabName}, it keeps the chest's: {e.Message}"); }

            Piece piece = go.GetComponent<Piece>();
            piece.m_name = title;
            piece.m_description = description;
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_resources = cost;
            try
            {
                Sprite picture = Icon.Make(go); // the build menu shows the Recycler itself, not the chest it was copied from
                if (picture != null) piece.m_icon = picture;
            }
            catch (System.Exception e) { Debug.LogWarning($"Recycler: could not draw the build-menu picture of {prefabName}: {e.Message}"); }
            go.AddComponent(logic);
            return go;
        }

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static void Unregister()
        {
            if (Prefabs.Count == 0) return;
            GameObject hammer = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            foreach (GameObject prefab in Prefabs)
            {
                if (ZNetScene.instance != null)
                {
                    ZNetScene.instance.m_prefabs.Remove(prefab);
                    var named = Named(ZNetScene.instance);
                    int hash = prefab.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == prefab) named.Remove(hash);
                }
                if (hammer != null) hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.Remove(prefab);
            }
            Prefabs.Clear();
            Object.Destroy(_holder);
            _holder = null;
        }

        private static Piece.Requirement Req(string item, int amount)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(item) : null;
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(item);
            return new Piece.Requirement { m_resItem = go != null ? go.GetComponent<ItemDrop>() : null, m_amount = amount, m_recover = true };
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

    // While the menu is open: keep the game from reacting to clicks and typing, and show the mouse.
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        // Skip the game's own cursor handling while our window is open (locking would snap the pointer to the middle of the screen on Linux).
        private static bool Prefix()
        {
            if (!Window.IsOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }
}
