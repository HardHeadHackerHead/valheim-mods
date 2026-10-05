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

namespace BuildOrders
{
    // Swimming normally puts your hammer away every frame, which ends building. With BuildWhileSwimming on, the hammer stays in your
    // hand in the water, so you can plan and build from it. (Equip the hammer before you jump in: the game does not let you equip things
    // while swimming.) Only the automatic put-away is skipped; the hide-hands key still works.
    [HarmonyPatch(typeof(Humanoid), "UpdateEquipment")]
    internal static class Humanoid_UpdateEquipment
    {
        internal static bool Running;
        private static void Prefix(Humanoid __instance) => Running = __instance == Player.m_localPlayer;
        private static void Finalizer() => Running = false;
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
    internal static class Humanoid_HideHandItems
    {
        private static readonly System.Reflection.FieldInfo RightItem = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            if (!Humanoid_UpdateEquipment.Running) return true; // not the automatic swimming put-away
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.BuildWhileSwimming) return true;
            ItemDrop.ItemData right = RightItem.GetValue(__instance) as ItemDrop.ItemData;
            if (right == null || right.m_shared.m_buildPieces == null) return true; // only the hammer (a build tool) stays out
            __result = false;
            return false;
        }
    }
}
