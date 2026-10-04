using HarmonyLib;

namespace BuildFromChests
{
    // Track every chest that exists in the world.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) => ChestScanner.Register(__instance);
    }

    // "How many do I have?" -> include nearby chests. Drives the build menu's requirement counts and the
    // "can I place this?" check.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems), typeof(string), typeof(int), typeof(bool))]
    internal static class Inventory_CountItems
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (name == null || !ChestScanner.Applies(__instance)) return;
            __result += ChestScanner.CountInChests(name, quality, matchWorldLevel);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
    internal static class Inventory_HaveItem
    {
        private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            if (__result || !ChestScanner.Applies(__instance)) return;
            __result = ChestScanner.CountInChests(name, -1, matchWorldLevel) > 0;
        }
    }

    // Mark the window in which placing a piece actually spends materials.
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class Player_ConsumeResources
    {
        private static void Prefix() => ChestScanner.Consuming = true;
        private static void Finalizer() => ChestScanner.Consuming = false;
    }

    // While paying for a piece: take from the player's inventory first, then top up from nearby chests.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem
    {
        private static bool Prefix(Inventory __instance, string name, int amount, int itemQuality, bool worldLevelBased)
        {
            if (!ChestScanner.Consuming || !ChestScanner.Applies(__instance)) return true;

            ChestScanner.Suspend = true; // so our own calls below hit the original methods
            try
            {
                int have = __instance.CountItems(name, itemQuality, worldLevelBased);
                if (have >= amount) return true; // player has enough, run vanilla

                if (have > 0) __instance.RemoveItem(name, have, itemQuality, worldLevelBased);
                ChestScanner.RemoveFromChests(name, amount - have, itemQuality, worldLevelBased);
                return false;
            }
            finally
            {
                ChestScanner.Suspend = false;
            }
        }
    }
}
