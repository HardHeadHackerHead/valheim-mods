using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Skateboards
{
    /// <summary>
    /// The player's input, as the game hands it to the player (Player.SetControls: keys or gamepad, already off while typing or in a
    /// menu). The riding reads it from here and nowhere else, so a test command can drive it instead (Tools.cs).
    ///
    /// On the board every key is ours: the game gets no move, attack, block, jump, crouch, sprint or dodge, so it neither walks nor
    /// fights nor jumps (nor do other mods that read these keys after us: Parkour's wall runs, Webslinger's webs). With the board in the
    /// hand and off it, the attack button is the drop-in, never a swing.
    /// </summary>
    internal static class Controls
    {
        /// <summary>The move keys or stick: x right, z forward.</summary>
        public static Vector3 Move;
        public static bool Run, JumpHeld, CrouchHeld, GrabHeld;
        private static bool _attack, _grab, _jump, _use, _crouch;

        // the move keys pressed lately (W, A, S, D), for the specials' two taps
        private static readonly List<(char Key, float At)> Taps = new List<(char, float)>();
        private static char _held;

        /// <summary>The last two move keys pressed were these, in this order, the first within this many seconds.</summary>
        public static bool Tapped(char first, char second, float within)
        {
            int n = Taps.Count;
            return n >= 2 && Taps[n - 1].Key == second && Taps[n - 2].Key == first && Time.time - Taps[n - 2].At <= within;
        }

        private static void Track(Vector3 move)
        {
            char now = move.z > 0.5f ? 'W' : move.z < -0.5f ? 'S' : move.x < -0.5f ? 'A' : move.x > 0.5f ? 'D' : ' ';
            if (now != _held && now != ' ')
            {
                Taps.Add((now, Time.time));
                if (Taps.Count > 6) Taps.RemoveAt(0);
            }
            _held = now;
        }

        /// <summary>A scripted input for testing (Tools.cs): while it lasts, it replaces the keys.</summary>
        public static Drive Driving;

        private static readonly Func<Player, bool> TakeInput = AccessTools.MethodDelegate<Func<Player, bool>>(AccessTools.Method(typeof(Player), "TakeInput"));

        /// <summary>A press since it was last taken (each is taken once, by the step that acts on it).</summary>
        public static bool TakeAttack() { bool a = _attack; _attack = false; return a; }
        public static bool TakeGrab() { bool g = _grab; _grab = false; return g; }
        public static bool TakeJump() { bool j = _jump; _jump = false; return j; }
        public static bool TakeUse() { bool u = _use; _use = false; return u; }
        public static bool TakeCrouch() { bool c = _crouch; _crouch = false; return c; }
        public static void ForgetPresses() => _attack = _grab = _jump = _use = _crouch = false;

        public static void Reset()
        {
            Move = Vector3.zero;
            Run = JumpHeld = CrouchHeld = GrabHeld = false;
            ForgetPresses();
            Taps.Clear();
            _held = ' ';
            Driving = null;
        }

        public static bool Taking(Player p)
        {
            try { return TakeInput == null || TakeInput(p); } catch (Exception) { return true; }
        }

        /// <summary>Every frame (from the plugin): the Use key, which the game reads on its own (Player.Update), not through SetControls.</summary>
        public static void Frame(Player p)
        {
            if (Driving != null) { if (Drive.Take(Driving.Uses)) _use = true; return; }
            if (!Skate.Active || !Taking(p) || InventoryGui.IsVisible() || Hud.InRadial()) return;
            if (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")) _use = true;
        }

        internal sealed class Drive
        {
            public float Start, Until;
            public readonly List<(float From, float To, Vector3 Move)> Moves = new List<(float, float, Vector3)>();
            public readonly List<(float From, float To)> Runs = new List<(float, float)>(), Jumps = new List<(float, float)>(),
                Grabs = new List<(float, float)>(), Crouches = new List<(float, float)>();
            public readonly List<float> Attacks = new List<float>(), Uses = new List<float>();

            public bool Over => Time.time > Until;
            private float T => Time.time - Start;

            public Vector3 MoveNow()
            {
                Vector3 m = Vector3.zero;
                foreach (var span in Moves) if (T >= span.From && T <= span.To) m += span.Move;
                return Vector3.ClampMagnitude(m, 1f);
            }

            public static bool Held(List<(float From, float To)> spans, Drive d)
            {
                foreach (var s in spans) if (d.T >= s.From && d.T <= s.To) return true;
                return false;
            }

            /// <summary>A press now, from a list of times since the start (each used once).</summary>
            public static bool Take(List<float> presses)
            {
                Drive d = Driving;
                if (d == null) return false;
                for (int i = 0; i < presses.Count; i++)
                    if (d.T >= presses[i]) { presses.RemoveAt(i); return true; }
                return false;
            }
        }

        // First, so the mods that read these keys after us (Parkour, Webslinger, double jumps) see none of them while riding.
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class Player_SetControls
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold,
                ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, ref bool dodge)
            {
                if (!ReferenceEquals(__instance, Player.m_localPlayer)) return;
                bool riding = Skate.Active;
                bool holding = Board.InHand(__instance);
                Drive drive = Driving;
                if (drive != null && drive.Over) Driving = drive = null;
                if (!riding && !holding && drive == null) return;

                bool taking = Taking(__instance) && !InventoryGui.IsVisible();
                if (drive != null)
                {
                    Move = drive.MoveNow();
                    Run = Drive.Held(drive.Runs, drive);
                    bool jumpHeld = Drive.Held(drive.Jumps, drive), grabHeld = Drive.Held(drive.Grabs, drive);
                    if (jumpHeld && !JumpHeld) _jump = true;
                    if (grabHeld && !GrabHeld) _grab = true;
                    JumpHeld = jumpHeld;
                    GrabHeld = grabHeld;
                    bool crouchHeld = Drive.Held(drive.Crouches, drive);
                    if (crouchHeld && !CrouchHeld) _crouch = true;
                    CrouchHeld = crouchHeld;
                    if (Drive.Take(drive.Attacks)) _attack = true;
                }
                else
                {
                    Move = movedir;
                    Run = run;
                    JumpHeld = taking && (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump"));
                    bool crouchHeld = taking && (ZInput.GetButton("Crouch") || ZInput.GetButton("JoyCrouch"));
                    if (crouchHeld && !CrouchHeld) _crouch = true;
                    CrouchHeld = crouchHeld;
                    GrabHeld = blockHold;
                    if (attack) _attack = true;
                    if (block) _grab = true;
                    if (jump) _jump = true;
                }

                Track(Move);
                if (riding)
                {
                    movedir = Vector3.zero;
                    attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = jump = crouch = run = autoRun = dodge = false;
                }
                else
                {
                    // the board in the hand: the attack button drops it and you hop on (Skate.Update), never a swing
                    attack = attackHold = secondaryAttack = secondaryAttackHold = false;
                    if (drive != null) { movedir = Move; run = Run; }
                }
            }
        }

        // On the board, E steps off (Skate.Update): it doesn't also open the door or chest in front of you.
        [HarmonyPatch(typeof(Player), "Interact")]
        private static class Player_Interact
        {
            private static bool Prefix(Player __instance) => !(Skate.Active && ReferenceEquals(__instance, Player.m_localPlayer));
        }
    }
}
