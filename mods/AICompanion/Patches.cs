using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    internal static class Patches
    {
        internal static bool OpeningGear; // set while the menu's "Open its inventory" opens the gear chest
    }

    // ---- the prefab, registered before any saved companion loads (docs/modding-pitfalls.md) ----

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance) => Prefab.Register(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake
    {
        private static void Postfix() { if (ZNetScene.instance != null) Prefab.Register(ZNetScene.instance); }
    }

    // ---- the body ----

    // Its gear chest and its hands share one inventory.
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake
    {
        private static void Postfix(Container __instance) { if (Companion.Is(__instance)) Companion.ShareInventory(__instance); }
    }

    // Our brain instead of the game's monster AI, for companions only.
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class MonsterAI_UpdateAI
    {
        private static float _lastError;

        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            if (!Companion.Is(__instance)) return true;
            try { __result = Brain.Update(__instance, dt); }
            catch (System.Exception e)
            {
                __result = true;
                if (Time.time - _lastError > 10f) { _lastError = Time.time; Plugin.Instance?.Warn("Companion brain: " + e); }
            }
            return false;
        }
    }

    // E on a companion opens its menu; the menu's own button opens the gear (for its owner only).
    [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
    internal static class Container_Interact
    {
        private static bool Prefix(Container __instance, Humanoid character, bool hold, ref bool __result)
        {
            if (Patches.OpeningGear || !Companion.Is(__instance)) return true;
            __result = true;
            if (!hold && character is Player p && p == Player.m_localPlayer && Plugin.Instance != null)
                Plugin.Instance.OpenMenuFor(p, __instance.GetComponent<Humanoid>());
            return false;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
    internal static class Character_GetHoverText
    {
        private static void Postfix(Character __instance, ref string __result)
        {
            if (!Companion.Is(__instance)) return;
            string status = Companion.StatusOf(__instance);
            __result = Localization.instance.Localize($"{Companion.NameOf(__instance)}\n[<color=yellow><b>$KEY_Use</b></color>] Menu") +
                       (string.IsNullOrEmpty(status) ? "" : $"\n<size=14>{status}</size>");
        }
    }

    [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
    internal static class Container_GetHoverText
    {
        private static void Postfix(Container __instance, ref string __result)
        {
            if (Companion.Is(__instance)) __result = Localization.instance.Localize($"{Companion.NameOf(__instance)}\n[<color=yellow><b>$KEY_Use</b></color>] Menu");
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
    internal static class Character_GetHoverName
    {
        private static void Postfix(Character __instance, ref string __result) { if (Companion.Is(__instance)) __result = Companion.NameOf(__instance); }
    }

    // Its armour counts (the game applies armour to players only), and players' swings never hurt it.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class Character_RPC_Damage
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return true;
            Character attacker = hit.GetAttacker();
            if (attacker != null && attacker.IsPlayer()) return false;
            float armor = Companion.Armor(h);
            if (armor > 0f) hit.ApplyArmor(armor);
            return true;
        }
    }

    // It falls: its gear goes into a crate where it stood.
    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath
    {
        private static void Prefix(Character __instance)
        {
            if (!(__instance is Humanoid h) || !Companion.Is(h)) return;
            ZNetView view = h.GetComponent<ZNetView>();
            if (!view.IsOwner()) return;
            try
            {
                Companion.DropGear(h);
                Player master = Companion.Master(h);
                if (master == Player.m_localPlayer) Plugin.Tell($"{Companion.NameOf(h)} has fallen. Their gear is in a crate where they fell. Press {Plugin.MenuKey.Value} to summon them again.");
            }
            catch (System.Exception e) { Plugin.Instance?.Warn("Could not put the fallen companion's gear in a crate: " + e); }
        }
    }

    // ---- while the menu is open: the game ignores clicks, keys and the wheel, and Escape closes only the menu ----

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Plugin.MenuOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!Plugin.MenuOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Plugin.MenuOpen) __result = 0f; }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!Plugin.MenuOpen) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Plugin.CloseFromEscape();
            return false;
        }
    }
}
