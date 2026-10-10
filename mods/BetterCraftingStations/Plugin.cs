using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace BetterCraftingStations
{
    /// <summary>
    /// A tidier crafting list at the workbench, forge, cauldron and the rest: chips above it to pick a type (weapons, armor, tools, ammo,
    /// food...), how far into the game (wood and flint, bronze, iron, silver...) or just what you can make now, each with how many there are.
    /// The list below is the game's own, narrowed; nothing about crafting itself changes.
    ///
    /// Split across files: Plugin.cs (setup, game hooks), Classify.cs (what a recipe is), Filters.cs (what is chosen), Bar.cs (the chips).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.quad.bettercraftingstations";
        public const string OldGuid = "com.dhack.bettercraftingstations"; // (its id until 2026-10: settings move over by themselves, see Shared/Migration.cs)
        public const string Name = "BetterCraftingStations";
        public const string Version = "0.1.2";

        internal static ManualLogSource Log;
        private static ConfigEntry<bool> _enabled;
        private Harmony _harmony;

        internal static bool Enabled => _enabled == null || _enabled.Value;

        private void Awake()
        {
            DHack.Shared.Migration.FromOldGuid(this, OldGuid); // first: before any setting is read
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true, "Show the filter chips above the crafting list. Off, the list is the game's own.");
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Bar.Destroy();
        }
    }

    // The list the game is about to show: remember all of it (the chips count from it) and show only what the chips pass. The list is
    // narrowed in place, never replaced: other mods' patches on this method hold the same list and must see what will be shown.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGui_UpdateRecipeList
    {
        private static void Prefix(InventoryGui __instance, List<Recipe> recipes)
        {
            Bar.All = recipes != null ? new List<Recipe>(recipes) : null;
            if (!Plugin.Enabled || recipes == null || __instance.InUpradeTab()) return;
            Filters.State state = Filters.Of(Filters.StationKey());
            if (!Filters.Active(state)) return;
            Player player = Player.m_localPlayer;
            recipes.RemoveAll(r => !Filters.Passes(r, state, player));
        }
    }

    // After the game has set the panel up: the chips for this station.
    [HarmonyPatch(typeof(InventoryGui), "UpdateCraftingPanel")]
    internal static class InventoryGui_UpdateCraftingPanel
    {
        private static void Postfix(InventoryGui __instance) => Bar.Refresh(__instance);
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class InventoryGui_Hide
    {
        private static void Postfix() => Bar.Hide();
    }
}
