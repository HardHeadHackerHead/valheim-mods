using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// The player's input, as the game hands it to the player (Player.SetControls: keys or gamepad, already off while typing or in a menu),
    /// plus the held state of Jump and the press of Sprint, which SetControls doesn't carry. Every move reads it from here and nowhere else,
    /// so a test command can drive it instead (Tools.cs): the moves then happen exactly as if a player pressed the keys.
    ///
    /// Our SetControls prefix runs before Parkour's (Priority.High against its Low): a jump in the air that can be a swing (a swing spot in
    /// reach) is ours, and Parkour never sees it; any other jump goes on to Parkour (a wall kick) and the game. While a Parkour
    /// move runs, or just after, nothing is ours (Compat.Busy). Falling past a building, Parkour used to take Space for a wall kick.
    /// </summary>
    internal static class Controls
    {
        /// <summary>The move keys or stick: x right, z forward (as the game gets them).</summary>
        public static Vector3 Move;
        /// <summary>Jump held down now.</summary>
        public static bool JumpHeld;
        /// <summary>Sprint held down now (or the game's toggle-run on).</summary>
        public static bool RunHeld;
        private static float _jumpAt = -10f, _zipAt = -10f, _crouchAt = -10f;

        /// <summary>A scripted input for testing (Tools.cs): while it lasts, it replaces the keys.</summary>
        public static Drive Driving;

        public static void Reset()
        {
            Move = Vector3.zero;
            JumpHeld = RunHeld = false;
            _jumpAt = _zipAt = _crouchAt = -10f;
            Driving = null;
        }

        /// <summary>A press of Jump, Sprint (a zip) or Crouch in the last moments, taken once.</summary>
        public static bool TakeJump() => Take(ref _jumpAt);
        public static bool TakeZip() => Take(ref _zipAt);
        public static bool TakeCrouch() => Take(ref _crouchAt);
        public static bool JumpPending => Time.time - _jumpAt <= 0.2f;

        private static bool Take(ref float at)
        {
            bool pressed = Time.time - at <= 0.2f;
            if (pressed) at = -10f;
            return pressed;
        }

        internal sealed class Drive
        {
            public Vector3 Move;
            public float Until;
            public float HoldJumpFrom = -1f, HoldJumpUntil = -1f;   // Jump held down meanwhile (Time.time)
            public readonly List<float> Jumps = new List<float>(), Zips = new List<float>(), Crouches = new List<float>();   // presses (Time.time)
            public bool Over => Time.time > Until;
            public bool JumpDown => Time.time >= HoldJumpFrom && Time.time <= HoldJumpUntil;

            public static bool Take(List<float> presses)
            {
                for (int i = 0; i < presses.Count; i++)
                    if (Time.time >= presses[i]) { presses.RemoveAt(i); return true; }
                return false;
            }
        }

        /// <summary>
        /// Each frame: Sprint's press (a zip) is read here, from the keys themselves: SetControls only carries whether it is held (or the
        /// game's toggle-run), and a zip is a fresh press in the air.
        /// </summary>
        public static void Update()
        {
            Player p = Player.m_localPlayer;
            if (p == null) return;
            Drive drive = Driving;
            if (drive != null)
            {
                if (Drive.Take(drive.Zips)) _zipAt = Time.time;
                return;
            }
            if (!Web.TakingInput(p)) return;
            if (ZInput.GetButtonDown("Run") || ZInput.GetButtonDown("JoyRun")) _zipAt = Time.time;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls
        {
            [HarmonyPriority(Priority.High)]
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold,
                ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
            {
                if (__instance != Player.m_localPlayer) return;
                Drive drive = Driving;
                if (drive != null && drive.Over) Driving = drive = null;
                if (drive != null)
                {
                    movedir = drive.Move;
                    run = false;
                    jump = Drive.Take(drive.Jumps);
                    crouch = Drive.Take(drive.Crouches);
                    attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = autoRun = dodge = false;
                }

                Move = movedir;
                RunHeld = run;
                JumpHeld = drive != null ? drive.JumpDown
                    : Web.TakingInput(__instance) && !InventoryGui.IsVisible() && (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));
                // A press on the ground is the game's jump (or a sprint's leap), never a web: it isn't kept for the moves, which would
                // otherwise see it a step later, already in the air, and shoot a web at once.
                if (jump && (!__instance.IsOnGround() || Web.OwnsMovement)) _jumpAt = Time.time;
                if (crouch) _crouchAt = Time.time;

                // what a move takes is ours: the game doesn't jump or crouch too (nor another mod's air jump)
                if (jump && Web.TakesJump(__instance)) jump = false;
                if (crouch && Web.TakesCrouch(__instance)) crouch = false;
                // While on a web, on a wall or flying off one, the game's own steering is off: in the air it pulls the body toward walking
                // speed (Character.UpdateWalking's air control, 10% a step), which would stop a swing dead. The moves steer instead.
                if (Web.OwnsMovement) { movedir = Vector3.zero; autoRun = false; }
            }
        }
    }
}
