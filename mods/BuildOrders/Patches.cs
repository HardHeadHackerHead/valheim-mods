using HarmonyLib;
using UnityEngine;

namespace BuildOrders
{
    // While the plan key is held, pretend the player can afford any piece, so planning is free.
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    internal static class Player_HaveRequirements
    {
        private static void Postfix(Player __instance, Player.RequirementMode mode, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || __result || mode != Player.RequirementMode.CanBuild || __instance != Player.m_localPlayer) return;
            if (plugin.PlanKeyHeld) __result = true;
        }
    }

    // Placing with the plan key held records a build order instead of placing the piece.
    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    internal static class Player_TryPlacePiece
    {
        private static bool Prefix(Player __instance, Piece piece, ref bool __result, GameObject ___m_placementGhost)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || __instance != Player.m_localPlayer || !plugin.PlanKeyHeld) return true; // not planning: build normally

            __result = false; // nothing is built, nothing is paid for
            if (___m_placementGhost == null) return false;

            if (__instance.GetPlacementStatus() != Player.PlacementStatus.Valid)
            {
                __instance.Message(MessageHud.MessageType.Center, "Can't plan a piece there");
                return false;
            }

            plugin.Plan(__instance, piece, ___m_placementGhost.transform.position, ___m_placementGhost.transform.rotation);
            return false;
        }
    }

    // After the game positions your placement ghost, nudge it onto a nearby build order for the same piece.
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class Player_UpdatePlacementGhost
    {
        private static void Postfix(Player __instance, GameObject ___m_placementGhost)
        {
            if (__instance != Player.m_localPlayer) return;
            Plugin.Instance?.SnapToOrder(___m_placementGhost);
        }
    }
}
