using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    // Whichever of ZNetScene and ObjectDB wakes first, the standard is registered before a saved one is loaded (docs/modding-pitfalls.md).
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetScene_Awake { private static void Postfix() => Things.Register(); }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDB_Awake { private static void Postfix() { if (ZNetScene.instance != null) Things.Register(); } }

    /// <summary>
    /// Health in the ring. In a duel nobody dies: health is held at a fifth and that player has lost. In a contest you can die (unless dying in
    /// the ring is switched off: then you are carried out at a third instead). Every hit taken in a contest also costs a little of the crowd's favour.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.SetHealth))]
    internal static class Character_SetHealth_Ring
    {
        private static void Prefix(Character __instance, ref float health)
        {
            if (__instance != Player.m_localPlayer) return;
            float max = __instance.GetMaxHealth(), now = __instance.GetHealth();
            if (Duel.Fighting && health < max * 0.2f)
            {
                health = Mathf.Max(health, max * 0.2f);
                Duel.Lose();
                return;
            }
            if (!Contest.Active) return;
            if (health < now && max > 0f) Contest.OnHurt((now - health) / max);
            if (!Plugin.RealDeath.Value && health <= 0.5f)
            {
                health = max * 0.3f;
                Contest.Knockout();
            }
        }
    }

    /// <summary>Blocks and parries in a contest please the crowd.</summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class Humanoid_BlockAttack_Crowd
    {
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        private static void Prefix(Humanoid __instance, out bool __state)
        {
            float t = __instance == Player.m_localPlayer ? BlockTimer(__instance) : -1f;
            __state = t >= 0f && t < 0.25f;
        }

        private static void Postfix(Humanoid __instance, bool __result, bool __state)
        {
            if (__result && __instance == Player.m_localPlayer && Contest.Active) Contest.OnBlock(__state);
        }
    }

    /// <summary>
    /// Dying in the ring: your tombstone is carried just outside it (on the side where you fell), so the next fight there does not stand on your
    /// things and you can walk in and pick them up. Your death marker on the map and the game's "where you died" point move with it.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    internal static class Player_OnDeath_Tomb
    {
        internal static Vector3? MovedTo;
        internal static Vector3 DiedAt;

        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        private static void Prefix(Player __instance)
        {
            MovedTo = null;
            if (__instance == Player.m_localPlayer && Site.Known && Travel.Distance(__instance.transform.position) < 120f)
            {
                HitData hit = LastHit(__instance);
                Character by = hit?.GetAttacker();
                Plugin.Log.LogWarning($"Died at the arena (in a contest: {Contest.Active}): {(hit != null ? hit.m_hitType + " " + hit.GetTotalDamage().ToString("0.0") + " from " + (by != null ? by.name : "nothing") + " at " + hit.m_point : "no hit known")}, at {Site.Local(__instance.transform.position)} in the arena's frame");
            }
            // the arena's lent things go back to the armourer, not into your tombstone (your own come back when you rise)
            if (__instance == Player.m_localPlayer && Kit.Stowed(__instance)) Kit.TakeBack(__instance);
            if (__instance != Player.m_localPlayer || !Contest.Active) return;
            DiedAt = __instance.transform.position;
            Contest.NoteDeath();
        }

        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer || MovedTo == null) return;
            Vector3 spot = MovedTo.Value;
            MovedTo = null;
            Game.instance?.GetPlayerProfile()?.SetDeathPoint(spot);
            if (Minimap.instance == null) return;
            var pins = AccessTools.Field(typeof(Minimap), "m_pins").GetValue(Minimap.instance) as List<Minimap.PinData>;
            Minimap.PinData pin = pins?.Where(p => p.m_type == Minimap.PinType.Death && (p.m_pos - DiedAt).sqrMagnitude < 9f).LastOrDefault();
            if (pin != null) pin.m_pos = spot;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.CreateTombStone))]
    internal static class Player_CreateTombStone_Outside
    {
        private static void Prefix(Player __instance, out HashSet<TombStone> __state)
        {
            __state = __instance == Player.m_localPlayer && Contest.TombPending ? new HashSet<TombStone>(Object.FindObjectsOfType<TombStone>()) : null;
        }

        private static void Postfix(Player __instance, HashSet<TombStone> __state)
        {
            if (__state == null) return;
            TombStone tomb = Object.FindObjectsOfType<TombStone>().FirstOrDefault(t => t != null && !__state.Contains(t));
            if (tomb == null) return;
            Vector3 spot = Contest.TombSpot(__instance.transform.position);
            tomb.transform.position = spot;
            Rigidbody body = tomb.GetComponent<Rigidbody>();
            if (body != null) { body.position = spot; body.velocity = Vector3.zero; }
            ZNetView view = tomb.GetComponent<ZNetView>();
            if (view != null && view.IsValid()) view.GetZDO().SetPosition(spot);
            Player_OnDeath_Tomb.MovedTo = spot;
            Plugin.Log.LogInfo($"Died in the ring: the tombstone was carried out to {spot}");
        }
    }

    // Logging out mid-fight: put things back (the weapon in the hand, the stake, the purse) before the character is saved.
    [HarmonyPatch(typeof(Game), "Shutdown")]
    internal static class Game_Shutdown
    {
        private static void Prefix() { Contest.Abort("You left the game."); Duel.Abort(true); }
    }

    // While the menu is open: keep the game from reacting to clicks and typing, show the mouse, and let Escape close only the menu.
    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class PlayerController_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Player_TakeInput
    {
        private static void Postfix(ref bool __result) { if (Window.IsOpen) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class GameCamera_UpdateMouseCapture
    {
        private static bool Prefix()
        {
            if (!Window.IsOpen) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInput_GetMouseScrollWheel
    {
        private static void Postfix(ref float __result) { if (Window.IsOpen) __result = 0f; }
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class Menu_Update_EscapeCloses
    {
        private static bool Prefix()
        {
            if (!Window.IsOpen) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Window.Close();
            return false;
        }
    }
}
