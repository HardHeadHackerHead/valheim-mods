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
        public const string Guid = "com.dhack.bettercraftingstations";
        public const string Name = "BetterCraftingStations";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        private static ConfigEntry<bool> _enabled;
        private Harmony _harmony;

        internal static bool Enabled => _enabled == null || _enabled.Value;

        private void Awake()
        {
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

    // The list the game is about to show: remember all of it (the chips count from it) and show only what the chips pass.
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGui_UpdateRecipeList
    {
        private static void Prefix(InventoryGui __instance, ref List<Recipe> recipes)
        {
            Bar.All = recipes != null ? new List<Recipe>(recipes) : null;
            if (!Plugin.Enabled || recipes == null || __instance.InUpradeTab()) return;
            recipes = Filters.Apply(Bar.All, Filters.Of(Filters.StationKey()));
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
