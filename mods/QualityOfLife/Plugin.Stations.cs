using BepInEx.Configuration;
using HarmonyLib;

namespace QualityOfLife
{
    /// <summary>
    /// Longer reach for crafting stations while you build: a workbench, stonecutter, forge or any other station counts as "near" for building
    /// out to Stations/BuildRange metres (when that is more than its own range), so a big build at the edge of the base needs no second
    /// workbench or stonecutter. Only the building check and the circle you see change: the station's base area (where monsters will not
    /// spawn) and the range for crafting at it stay as the game has them. Each player's own setting counts for their own building.
    /// </summary>
    public partial class Plugin
    {
        internal static ConfigEntry<float> StationBuildRange;

        private void BindStationsConfig()
        {
            StationBuildRange = Config.Bind("Stations", "BuildRange", 0f, new ConfigDescription(
                "How far (in metres) from a crafting station (workbench, stonecutter, forge, ...) you can build with it, when that is more than the " +
                "station's own range (20 m, more with upgrades). 0 leaves the game's ranges. 60 reaches across a big base. Takes effect at once.",
                new AcceptableValueRange<float>(0f, 150f)));
        }
    }

    // The station's build range, as the building check asks for it: at least the setting.
    [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetStationBuildRange))]
    internal static class CraftingStation_GetStationBuildRange
    {
        private static readonly AccessTools.FieldRef<CraftingStation, CircleProjector> Circle =
            AccessTools.FieldRefAccess<CraftingStation, CircleProjector>("m_areaMarkerCircle");

        private static void Postfix(CraftingStation __instance, ref float __result)
        {
            float wanted = Plugin.StationBuildRange?.Value ?? 0f;
            if (wanted <= __result) return;
            __result = wanted;
            CircleProjector circle = Circle(__instance);
            if (circle != null) circle.m_radius = wanted;   // (the circle shown when you build near it)
        }
    }
}
