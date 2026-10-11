using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// The player's input, as the game hands it to the player (Player.SetControls: keys or gamepad, already off while typing or in a
    /// menu). Every move reads it from here and nowhere else, so a test command can drive it instead: sprint, forward, jump for a few
    /// seconds, and the moves happen exactly as if a player pressed the keys.
    /// </summary>
    internal static class Controls
    {
        /// <summary>The move keys or stick: x right, z forward (as the game gets them).</summary>
        public static Vector3 Move;
        /// <summary>Sprint held (and the game lets the player sprint: it has stamina, it is moving).</summary>
        public static bool Run;
        /// <summary>Crouch held down (the game only hands over the press; a slide lasts while it is held).</summary>
        public static bool CrouchHeld;
        private static float _jumpAt = -10f;

        /// <summary>A scripted input for testing (Tools.cs): while it lasts, it replaces the keys.</summary>
        public static Drive Driving;

        /// <summary>Jump was pressed in the last moments (whether the game or a move used it).</summary>
        public static bool JumpPressed(float within) => Time.time - _jumpAt <= within;
        public static bool Forward => Move.z > 0.3f;
        public static bool Back => Move.z < -0.3f;

        public static void Reset()
        {
            Move = Vector3.zero;
            Run = false;
            _jumpAt = -10f;
            Driving = null;
        }

        internal sealed class Drive
        {
            public Vector3 Move;
            public bool Run;
            public float Until;
            public readonly List<float> Jumps = new List<float>(), Crouches = new List<float>();   // when to press them (Time.time)
            public float HoldCrouchFrom = -1f, HoldCrouchUntil = -1f;                              // crouch held down meanwhile

            public bool CrouchDown => Time.time >= HoldCrouchFrom && Time.time <= HoldCrouchUntil;

            public bool Over => Time.time > Until;

            public static bool Take(List<float> presses)
            {
                for (int i = 0; i < presses.Count; i++)
                    if (Time.time >= presses[i]) { presses.RemoveAt(i); return true; }
                return false;
            }
        }

        // Late, so the mods that use these keys for something else first (riding, flying, aiming, Just Vault's vault) have had their say.
        // During a move (wall run, climb, hang, pull-up) the blue parkour bar is the limit: the game's sprint drain (Player.CheckRun,
        // every step sprint is held, in the air too) stops, and the player still counts as running.
        [HarmonyPatch(typeof(Player), "CheckRun")]
        private static class Player_CheckRun
        {
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer || Moves.State == Parkour.Move.None) return true;
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls
        {
            [HarmonyPriority(Priority.Low)]
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold,
                ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
            {
                if (__instance != Player.m_localPlayer) return;
                Drive drive = Driving;
                if (drive != null && drive.Over) Driving = drive = null;
                if (drive != null)
                {
                    movedir = drive.Move;
                    run = drive.Run;
                    jump = Drive.Take(drive.Jumps);
                    crouch = Drive.Take(drive.Crouches);
                    attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = autoRun = dodge = false;
                }

                Move = movedir;
                Run = run;
                CrouchHeld = drive != null ? drive.CrouchDown
                    : Moves.TakingInput(__instance) && !InventoryGui.IsVisible() && (ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch"));
                if (jump) _jumpAt = Time.time;
                // a jump or crouch that is a parkour move (a kick off the wall, a pull up, letting go) is ours: the game doesn't jump or crouch
                // too (nor does another mod's air jump: EpicLoot's double jump)
                if (jump && Moves.TakesJump(__instance)) jump = false;
                if (crouch && Moves.TakesCrouch(__instance)) crouch = false;
            }
        }
    }
}
