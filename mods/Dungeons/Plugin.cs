using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// Reloads in a world (HotReload.cs): its prefabs and items stay registered across the reload and are replaced by name (items through
// ItemReload.cs, which modcheck looks for), the standing entrances and the items in bags get the new copy's, built dungeons are handed
// over as they stand and built again, nobody falls.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

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
    /// falls), Catalogue.cs (the game's own rooms), Tools.cs (Claude Tools commands), HotReload.cs (reloading in a world).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.dungeons";
        public const string Name = "Dungeons";
        public const string Version = "0.3.2";

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

            BossSettings.Bind(Config);   // (BossSettings.cs) each player's own "Screen shake", not synced
            PackWatch.Setup(Config);   // (SyncReload.cs) pack makers: reload the config folder's packs on save, off by default
            HotReload.Arrive();        // a reload in a world: what the last copy handed over (HotReload.cs)
            _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
            HotReload.EndGap();        // (the last copy's guard for the frame between, now that ours are on)
            if (ZNetScene.instance != null) Entrance.Register(ZNetScene.instance);   // a reload while in a world
            BlockKeys.Define();        // the packs' key items (BlockKeys.cs), made with the reward items
            Builder.RegisterItems();   // the reward items (BuildRewards.cs): nothing until ZNetScene or ObjectDB exists
            HotReload.Settle();        // a reload in a world: the standing entrances and the items here become this copy's
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Synced?.Update();
            Tools.Update();
            Dungeons.Update();
            HotReload.Update();   // (the last copy's builds, after a reload: removed once this copy's stand)
        }

        private void FixedUpdate() => Dungeons.FixedUpdate();

        private void OnDestroy()
        {
            bool hot = HotReload.Leave();   // a reload in a world: built dungeons, prefabs, items and state handed to the next copy
            Dungeons.Clear();
            if (!hot) Builder.RemoveAll();   // (any build no dungeon kept: one still starting)
            else
            {
                // only the builds still going (the handed-over ones aren't in the list), and the shared collider shapes stay: the
                // handed-over builds use them
                foreach (Built built in Builder.All.ToList()) Builder.Remove(built);
                try { Packs.Clear(); } catch (System.Exception) { }   // (the game's rooms it loaded let go: the next copy loads its own)
            }
            Safety.LetGo();
            Safety.Reset();
            Tools.Unregister();
            _harmony?.UnpatchSelf();
            Entrance.Unregister();
            Builder.UnregisterItems();
            PackWatch.Dispose();
            Synced?.Dispose();
            DHack.Shared.Translations.Unload();
            if (Instance == this) Instance = null;
        }
    }
}
