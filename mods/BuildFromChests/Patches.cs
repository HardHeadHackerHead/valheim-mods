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
        private static void Prefix(out bool __state) { __state = ChestScanner.Consuming; ChestScanner.Consuming = true; }
        private static void Finalizer(bool __state) => ChestScanner.Consuming = __state;
    }

    // Placing a piece. The game places it first and charges after, so this is where a piece the chests can no longer pay for (someone
    // emptied one since the build menu counted it) is refused: by the time the game charges it's too late.
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class Player_TryPlacePiece
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Player __instance, Piece piece, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal) return false; // another mod already said no
            if (piece == null || __instance != Player.m_localPlayer || !ChestScanner.Applies(__instance.GetInventory())) return true;
            if (__instance.NoCostCheat() || ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return true; // nothing will be charged

            string missing = ChestScanner.FindShortfall(__instance.GetInventory(), piece.m_resources);
            if (missing == null) return true;
            __instance.Message(MessageHud.MessageType.Center, $"Not enough {Localization.instance.Localize(missing)} in the chests any more");
            __result = false;
            return false;
        }
    }

    // While paying for a piece: take from the player's inventory first, then top up from nearby chests. Runs after every other mod's
    // prefix: one that pays from somewhere else (Adventure Backpacks pays from the backpack) lowers the amount or skips the method, and
    // only what's left is ours to find.
    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class Inventory_RemoveItem
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased, bool __runOriginal)
        {
            if (!__runOriginal) return false; // another mod has paid all of it
            if (!ChestScanner.Consuming || !ChestScanner.Applies(__instance)) return true;

            int own = ChestScanner.OwnCount(__instance, name, itemQuality, worldLevelBased);
            if (own >= amount) return true; // player has enough, run vanilla

            int left = ChestScanner.RemoveFromChests(name, amount - own, itemQuality, worldLevelBased);
            if (left > 0) Plugin.Log.LogWarning($"Building: the chests were {left} {name} short after all"); // (checked just before, so it shouldn't happen)
            amount = own; // the game takes the rest from the inventory
            return own > 0;
        }
    }
}
