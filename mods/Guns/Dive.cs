using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// The dive: with a light gun in hand, block + jump throws the player into a dive instead of the game's roll (which would stop the shooting).
    /// Moving forward it is a dive forward, head first; to a side, a sideways dive, landing on the shoulder; backward, a dive backward. The
    /// player keeps their gun up and can fire the whole way through. There is no animation for it in the game, so the body is made to lie
    /// down while it flies, slide along the ground and get up again (the character's visual tilted about the hips and lowered to the ground in
    /// LateUpdate, with the legs kept from running: the animation's speeds are held at zero while it lasts), and the motion is a hop with a push
    /// along the direction, easing into a slide: set on the physics body each step (the game's own walking would put it back).
    /// The game's roll gives a moment of not being hit; the dive does not.
    /// </summary>
    internal static class Dive
    {
        private const float Duration = 0.82f, Cooldown = 0.35f;
        private static bool _on, _launched;
        private static float _start, _next, _grounded;
        private static Vector3 _dir;
        private static int _kind;   // 0 forward, 1 to the right, 2 to the left, 3 backward
        private static Transform _visual;
        private static Vector3 _visualPos;
        private static Quaternion _visualRot;

        internal static bool Active => _on;

        /// <summary>
        /// Block + jump with a light gun: begins a dive in the way the player is moving (forward when standing still). move is the game's input
        /// for the step, not a direction in the world: x is to the right, z is forward, as the player faces where they look.
        /// Returns whether it began.
        /// </summary>
        internal static bool TryStart(Player p, Vector3 move)
        {
            if (_on || Time.time < _next || p.IsDead() || !p.IsOnGround() || p.IsAttached() || p.IsSwimming() || p.InDodge() || p.IsStaggering() || p.InMinorAction()) return false;
            float cost = Plugin.DiveStamina.Value;
            if (cost > 0f && !p.HaveStamina(cost)) return false;
            Vector3 fwd = Vector3.ProjectOnPlane(p.GetLookDir(), Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = p.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);   // (as the game turns the input into a direction: z along the look, x across it)
            Vector2 input = new Vector2(move.x, move.z);
            if (input.magnitude < 0.2f) { _dir = fwd; _kind = 0; }
            else
            {
                input.Normalize();
                _dir = (fwd * input.y + right * input.x).normalized;
                _kind = input.y > 0.6f ? 0 : input.y < -0.6f ? 3 : input.x > 0f ? 1 : 2;   // (nearer straight on than 53 degrees off: forward or back; else to the side it leans)
            }
            if (cost > 0f) p.UseStamina(cost);
            _on = true; _launched = false; _start = Time.time; _grounded = 0f;
            _visual = p.GetVisual() != null ? p.GetVisual().transform : null;
            if (_visual != null) { _visualPos = _visual.localPosition; _visualRot = _visual.localRotation; }
            Fx.Click(Sound.Click.Dive, p.transform.position + Vector3.up);
            return true;
        }

        internal static void End(Player p)
        {
            if (!_on) return;
            _on = false;
            _next = Time.time + Cooldown;
            if (_visual != null) { _visual.localPosition = _visualPos; _visual.localRotation = _visualRot; }
        }

        /// <summary>One physics step of the dive: the hop, then the push, easing into a slide.</summary>
        internal static void Step(Player p)
        {
            if (!_on) return;
            float t = Time.time - _start;
            if (t >= Duration || p.IsDead() || p.IsAttached() || p.IsSwimming()) { End(p); return; }
            Rigidbody body = p.GetComponent<Rigidbody>();
            if (body == null) { End(p); return; }
            float u = t / Duration;
            float speed = Mathf.Lerp(9.5f, 0.4f, Mathf.Pow(u, 1.3f));   // fast in the air, scraping along the ground to a stop
            Vector3 v = body.velocity;
            if (!_launched) { v.y = 3.6f; _launched = true; }
            v.x = _dir.x * speed;
            v.z = _dir.z * speed;
            body.velocity = v;
            if (u > 0.3f && p.IsOnGround() && !_landed) { _landed = true; Fx.Click(Sound.Click.Land, p.transform.position + Vector3.up * 0.3f); }
        }

        private static bool _landed;

        /// <summary>
        /// Every frame, before the arms are posed: the body leans into the dive, lies down in the air, drops to the ground and slides, then
        /// gets up. The lean follows one curve (quick down, a while along, slowly up) and the lowering waits for the ground.
        /// </summary>
        internal static void Pose(Player p)
        {
            if (!_on) { _landed = false; return; }
            if (_visual == null) return;
            float u = Mathf.Clamp01((Time.time - _start) / Duration);
            float lean = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u / 0.2f, (1f - u) / 0.38f)));
            _grounded = Mathf.MoveTowards(_grounded, p.IsOnGround() && u > 0.25f ? 1f : 0f, Time.deltaTime * 7f);   // lying on the ground: the body comes right down
            Vector3 fwd = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Quaternion tilt;
            float lowest;   // how far the hips come down when lying on the ground
            switch (_kind)
            {
                case 0: tilt = Quaternion.AngleAxis(84f * lean, right); lowest = 0.74f; break;       // head first, chest to the ground
                case 3: tilt = Quaternion.AngleAxis(-78f * lean, right); lowest = 0.72f; break;      // on the back
                case 1: tilt = Quaternion.AngleAxis(-84f * lean, fwd); lowest = 0.70f; break;         // falling to the right
                default: tilt = Quaternion.AngleAxis(84f * lean, fwd); lowest = 0.70f; break;       // falling to the left
            }
            _visual.localPosition = _visualPos;
            _visual.localRotation = _visualRot;
            Vector3 pivot = p.transform.position + Vector3.up * 0.95f;   // (about the hips, so the feet leave the ground)
            _visual.position = pivot + tilt * (_visual.position - pivot) + Vector3.down * (lowest * lean * _grounded);
            _visual.rotation = tilt * _visual.rotation;
        }
    }

    // After the game's walking has set the player's motion, the dive sets it again.
    [HarmonyPatch(typeof(Character), nameof(Character.CustomFixedUpdate))]
    internal static class Character_CustomFixedUpdate_Dive
    {
        private static void Postfix(Character __instance)
        {
            if (Dive.Active && __instance == Player.m_localPlayer) Dive.Step((Player)__instance);
        }
    }

    // While diving the legs must not run: the animation's speeds (how fast it goes forward, sideways, turns) are held at nothing.
    [HarmonyPatch(typeof(ZSyncAnimation), nameof(ZSyncAnimation.SetFloat), typeof(int), typeof(float))]
    internal static class ZSyncAnimation_SetFloat_Dive
    {
        private static readonly int Forward = ZSyncAnimation.GetHash("forward_speed"), Sideways = ZSyncAnimation.GetHash("sideway_speed"), Turn = ZSyncAnimation.GetHash("turn_speed");

        private static void Prefix(ZSyncAnimation __instance, int hash, ref float value)
        {
            if (!Dive.Active || Player.m_localPlayer == null || __instance.gameObject != Player.m_localPlayer.gameObject) return;
            if (hash == Forward || hash == Sideways || hash == Turn) value = 0f;
        }
    }
}
