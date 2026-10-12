using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Aiming down the sights: while a Machine Gun is in hand, the block button (right mouse, or the left trigger) zooms the camera in and
    /// tightens the spread (Fire.cs). No new key: the button is the game's own, which a gun has no other use for. The zoom is the game's
    /// own temporary field of view (GameCamera.SetTempFOV), eased in and out, and put back whenever aiming stops for any reason.
    /// </summary>
    internal static class Aim
    {
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        internal static bool Active;

        /// <summary>Called every frame from the plugin.</summary>
        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            bool want = p != null && !p.IsDead() && MachineGun.Is(p.GetCurrentWeapon()) && InputAllowed(p) &&
                        (ZInput.GetButton("Block") || ZInput.GetButton("JoyBlock"));
            if (want == Active) return;
            Active = want;
            GameCamera cam = GameCamera.instance;
            if (cam == null) return;
            if (want) cam.SetTempFOV(cam.m_fov / Mathf.Max(1f, Plugin.AimZoom.Value), 0.2f);
            else cam.ResetTempFOV();
        }

        /// <summary>
        /// After the animation has posed the arms (the game's crossbow carry lowers the gun across the chest when you walk), the gun in the
        /// hand is turned to point where you look, pivoting about the grip so it stays in the hand. Called every frame (LateUpdate).
        /// </summary>
        internal static void Pose()
        {
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || !MachineGun.Is(p.GetCurrentWeapon())) return;
            Transform muzzle = Fx.Muzzle(p);
            Transform model = muzzle != null ? muzzle.parent : null;   // (the model's root: its +Z is the barrel)
            if (model == null) return;
            Vector3 look = p.GetLookDir();
            if (look.sqrMagnitude < 0.0001f) return;
            var grip = new Vector3(0f, -0.12f, -0.10f);
            Vector3 held = model.TransformPoint(grip);
            model.rotation = Quaternion.LookRotation(look, Vector3.up);
            model.position += held - model.TransformPoint(grip);
        }

        /// <summary>Stops aiming and gives the camera its field of view back (the plugin unloading, the gun put away).</summary>
        internal static void Stop()
        {
            if (!Active) return;
            Active = false;
            GameCamera.instance?.ResetTempFOV();
        }

        private static bool InputAllowed(Player p)
        {
            try { return TakeInput == null || (bool)TakeInput.Invoke(p, null); }   // (no window open, not typing in chat)
            catch { return true; }
        }
    }

    // The block button is how a gun aims, but the game must not take it as blocking: that raises the weapon across the chest (a block
    // pose), and block + jump is a dodge. For a gun the game is told the button isn't pressed; Aim.Tick reads the button itself.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls_Aim
    {
        private static void Prefix(Player __instance, ref bool block, ref bool blockHold)
        {
            if (__instance != Player.m_localPlayer || !MachineGun.Is(__instance.GetCurrentWeapon())) return;
            block = false;
            blockHold = false;
        }
    }

    // With a gun in hand the body always faces where the camera looks (as it does while drawing a bow, strafing when it walks), so the gun
    // points where you look instead of swinging to the side as you walk, and there is nothing to shoot through behind you.
    [HarmonyPatch(typeof(Player), "AlwaysRotateCamera")]
    internal static class Player_AlwaysRotateCamera_Aim
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__result || __instance != Player.m_localPlayer) return;
            if (MachineGun.Is(__instance.GetCurrentWeapon())) __result = true;
        }
    }
}
