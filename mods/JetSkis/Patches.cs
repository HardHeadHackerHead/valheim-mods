using HarmonyLib;
using UnityEngine;

namespace JetSkis
{
    // The game's boat code hands over to the jet ski for jet skis only: every other boat runs exactly as before.

    /// <summary>The rider's keys: a jet ski reads them its own way (hold to go, the handlebars centre by themselves).</summary>
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.ApplyControlls))]
    internal static class ShipControlls_ApplyControlls
    {
        private static bool Prefix(ShipControlls __instance, Vector3 moveDir, bool run)
        {
            JetSki ski = JetSki.Of(__instance.m_ship);
            if (ski == null) return true;
            ski.Drive(moveDir, run);
            return false;
        }
    }

    /// <summary>E on a jet ski: climb on from the water or the shore (the game's boats want you standing on them first).</summary>
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.Interact))]
    internal static class ShipControlls_Interact
    {
        private static bool Prefix(ShipControlls __instance, Humanoid character, bool repeat, ref bool __result)
        {
            JetSki ski = JetSki.Of(__instance.m_ship);
            if (ski == null) return true;
            __result = false;
            if (!repeat && character is Player player && player == Player.m_localPlayer) ski.Mount(player);
            return false;
        }
    }

    /// <summary>After the game's boat physics each step: a jet ski's motor and steering.</summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate
    {
        private static void Postfix(Ship __instance, float fixedDeltaTime) => JetSki.Of(__instance)?.Motor(fixedDeltaTime);
    }
}
