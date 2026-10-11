using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// It registers a saved prefab (the dungeon entrance): reloaded in a world, the entrance would be missing for a moment and a host loading
// an area then would delete the entrances there. So it reloads only at the main menu.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Dungeons
{
    /// <summary>
    /// Quad's Dungeons: one dungeon system, with each kind of dungeon plugged in as a pack (docs/dungeons-design.md in the valheim-mods repo).
    /// A dungeon is a seed and a pack: every player's game builds the same rooms from them, 5000 m above the entrance, and only the entrance's
    /// state is saved.
    ///
    /// Split across files: Plugin.cs (settings, setup), Model.cs (the shared data), Packs.cs (the packs), Layouts.cs (laying a dungeon out
    /// from a seed), Builder.cs (building it in the world), Entrance.cs (the entrance prefabs and the way out), EntranceLooks.cs (what an
    /// entrance looks like), Dungeon.cs (where each dungeon goes, building and removing it, its owner's jobs, going in and out), Mechs.cs,
    /// MechState.cs and MechLever.cs (the moving parts: levers, plates, gates, stairs, platforms, rotating rooms), Safety.cs (nobody
    /// falls), Catalogue.cs (the game's own rooms), Tools.cs (Claude Tools commands).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.dungeons";
        public const string Name = "Dungeons";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        // the server's
        internal static ConfigEntry<int> ResetDays;
        internal static DHack.Shared.ServerSettings Synced;   // when dungeons come back new is the server's in multiplayer

        private Harmony _harmony;

        private void Awake()
        {
            DHack.Shared.Translations.Load(this);   // translations/*.json: its words in the player's language
            Instance = this;
            Log = Logger;
            Synced = new DHack.Shared.ServerSettings(Guid, Config, Logger);
            ResetDays = Synced.Add(Config.Bind("1 - Dungeons", "ResetDays", 3, new ConfigDescription("In-game days after a dungeon is first entered before it comes back new: its enemies, loot and puzzle (only once nobody is inside). 0: never. [Synced with Server]", new AcceptableValueRange<int>(0, 100))));

            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            if (ZNetScene.instance != null) Entrance.Register(ZNetScene.instance);   // a reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Tools.Update();
            Dungeons.Update();
        }

        private void FixedUpdate() => Dungeons.FixedUpdate();

        private void OnDestroy()
        {
            Dungeons.Clear();
            Builder.RemoveAll();   // (any build no dungeon kept: one still starting)
            Safety.LetGo();
            Safety.Reset();
            Tools.Unregister();
            _harmony?.UnpatchSelf();
            Entrance.Unregister();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }
}
