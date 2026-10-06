using System;
using System.Reflection;
using HarmonyLib;

namespace BuildOrders
{
    // While the Plans window is open the game keeps its hands off the mouse and keyboard (no walking, looking or swinging, a free cursor,
    // Esc closes the window instead of opening the game menu). While a blueprint preview is being placed you can still walk and look, but
    // the mouse wheel turns the preview instead of zooming, and clicks place it instead of attacking or building with the hammer.
    // Patched by hand, one at a time, so a game update that renames one of these leaves the rest (and the mod) working.
    internal static class PlansPatches
    {
        private static bool WindowOpen => Plugin.PlansWindowOpen || Plugin.BridgeOptionsOpen;
        private static bool Busy => Plugin.PlansWindowOpen || Plugin.Placing || Plugin.BridgeOptionsOpen;
        internal static int EscapeFrame = -1; // the frame our window or preview used Esc, so the game menu does not open on the same press

        internal static void Apply(Harmony harmony)
        {
            Patch(harmony, AccessTools.Method(typeof(PlayerController), "TakeInput"), postfix: nameof(NoInputPostfix));
            Patch(harmony, AccessTools.Method(typeof(Player), "TakeInput"), postfix: nameof(NoInputPostfix));
            Patch(harmony, AccessTools.Method(typeof(GameCamera), "UpdateMouseCapture"), prefix: nameof(SkipWhenWindow));
            Patch(harmony, AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel"), postfix: nameof(NoScrollPostfix));
            Patch(harmony, AccessTools.Method(typeof(Menu), "Update"), prefix: nameof(MenuPrefix));
            Patch(harmony, AccessTools.Method(typeof(Humanoid), nameof(Humanoid.StartAttack)), prefix: nameof(NoAttackPrefix));
            Patch(harmony, AccessTools.Method(typeof(Player), "UpdatePlacement"), prefix: nameof(HammerPrefix));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null)
        {
            if (target == null) { Plugin.Log.LogWarning("Plans window: a game method to patch was not found (game update?); carrying on without it"); return; }
            try
            {
                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(typeof(PlansPatches), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(PlansPatches), postfix) : null);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Plans window: could not patch {target.DeclaringType?.Name}.{target.Name}: {e.Message}"); }
        }

        private static void NoInputPostfix(ref bool __result) { if (WindowOpen) __result = false; }

        private static bool SkipWhenWindow() => !WindowOpen;

        private static void NoScrollPostfix(ref float __result) { if (Busy) __result = 0f; }

        private static bool MenuPrefix() => !Busy && !Plugin.BridgeDrawing && UnityEngine.Time.frameCount != EscapeFrame && UnityEngine.Time.frameCount != EscapeFrame + 1;

        private static bool NoAttackPrefix(Humanoid __instance, ref bool __result)
        {
            if (!Busy || __instance != Player.m_localPlayer) return true;
            __result = false;
            return false;
        }

        private static void HammerPrefix(Player __instance, ref bool takeInput)
        {
            if (Busy && __instance == Player.m_localPlayer) takeInput = false;
        }
    }
}
