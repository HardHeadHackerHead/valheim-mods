using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PortalHub
{
    // Pressing E on a portal opens our menu instead of the name box. (Shift+E, the game's "alternate use", still gives the plain name box.)
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorld_Interact
    {
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, bool alt, ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.Enabled || hold || alt || human != Player.m_localPlayer) return true;

            if (!PrivateArea.CheckAccess(__instance.transform.position))
            {
                human.Message(MessageHud.MessageType.Center, "$piece_noaccess");
                __result = true;
                return false;
            }
            plugin.OpenWindow(__instance);
            __result = true;
            return false;
        }
    }

    // Looking at a portal: say where it goes.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorld_GetHoverText
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.Enabled || !plugin.ShowHoverLine || string.IsNullOrEmpty(__result)) return;

            ZNetView view = __instance.GetComponent<ZNetView>();
            ZDO zdo = view != null ? view.GetZDO() : null;
            if (zdo == null) return;
            string dest = zdo.GetString("dh_dest", "");
            if (dest.Length == 0) return;

            PortalInfo target = plugin.Find(dest);
            string name = target != null ? (string.IsNullOrWhiteSpace(target.Name) ? "an unnamed portal" : target.Name) : "another portal";
            __result += "\n<color=#F2C75A>Goes to:</color> " + name;
        }
    }

    // Host side: before the game's own pass that pairs portals by name, connect the portals that have a chosen destination,
    // and hide those portals from that pass so it does not undo them.
    [HarmonyPatch(typeof(Game), nameof(Game.ConnectPortals))]
    internal static class Game_ConnectPortals
    {
        internal static bool Filtering;

        private static void Prefix()
        {
            Filtering = false;
            Plugin.ApplyDestinations();
            Filtering = Plugin.Instance != null && Plugin.Instance.Enabled;
        }

        private static void Postfix() => Filtering = false;
        private static void Finalizer() => Filtering = false;
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.GetPortalList))]
    internal static class ZDOMan_GetPortalList
    {
        private static void Postfix(List<ZDO> __result)
        {
            if (Game_ConnectPortals.Filtering) __result.RemoveAll(Plugin.HasDest);
        }
    }

    // ---- while the menu is open: the game must not react to clicks and typing, and the mouse must be free ----

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.WindowOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.WindowOpen) __result = false; }
    }

    // Other mods treat "typing in a box" as a reason to ignore their hotkeys; the search box is one.
    [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
    internal static class Minimap_InTextInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.WindowOpen) __result = true; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!Plugin.WindowOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Plugin.WindowOpen) __result = 0f; }
    }

    // Escape closes our menu, and only that (the game is not paused).
    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!Plugin.WindowOpen) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Plugin.CloseFromEscape();
            return false;
        }
    }
}
