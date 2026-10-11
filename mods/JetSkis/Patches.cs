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
        private static bool Prefix(ShipControlls __instance, Humanoid character, bool repeat, bool alt, ref bool __result)
        {
            JetSki ski = JetSki.Of(__instance.m_ship);
            if (ski == null) return true;
            __result = false;
            if (repeat || !(character is Player player) || player != Player.m_localPlayer) return false;
            if (alt) ski.NextPaint(player);   // Shift+E: the next colour
            else ski.Mount(player);
            return false;
        }
    }

    /// <summary>The hover on a jet ski also says Shift+E paints it, and its colour now.</summary>
    [HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
    internal static class ShipControlls_GetHoverText
    {
        private static void Postfix(ShipControlls __instance, ref string __result)
        {
            JetSki ski = JetSki.Of(__instance.m_ship);
            if (ski == null) return;
            string keys = !ZInput.IsNonClassicFunctionality() || !ZInput.IsGamepadActive() ? "$KEY_AltPlace + $KEY_Use" : "$KEY_AltKeys + $KEY_Use";   // (as the game's own item stand)
            int paint = ((ski.PaintIndex % Effects.Paints.Length) + Effects.Paints.Length) % Effects.Paints.Length;
            __result += Localization.instance.Localize("\n[<color=yellow><b>" + keys + "</b></color>] $jetskis_paint ($" + Effects.PaintKeys[paint] + ")");
        }
    }

    /// <summary>After the game's boat physics each step: a jet ski's motor and steering.</summary>
    [HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
    internal static class Ship_CustomFixedUpdate
    {
        private static void Postfix(Ship __instance, float fixedDeltaTime) => JetSki.Of(__instance)?.Motor(fixedDeltaTime);
    }

    /// <summary>
    /// While driving, the game lets you use nothing (E only lets go of the handlebars), so the saddlebags open with the inventory: on a
    /// jet ski, rider or passenger, opening it (Tab) asks for the bags too, as a chest's would show beside it.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryGui_Show
    {
        private static void Postfix(Container container)
        {
            Player me = Player.m_localPlayer;
            if (container != null || me == null) return;
            JetSki ski = JetSki.RidingOf(me) ?? JetSki.PassengerOf(me);
            Container bags = ski != null ? ski.GetComponentInChildren<Container>() : null;
            if (bags != null) bags.Interact(me, false, false);   // (the owner's answer shows the inventory again, with them)
        }
    }
}
