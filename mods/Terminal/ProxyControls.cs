using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// What it costs a Proxy to act, and the rest of what a driver can do: stamina (running, jumping, swinging and blocking wear it down,
    /// standing still gives it back; out of breath it can't run, jump or swing), and crouching. The stamina is the owner's own number;
    /// every game reads it from the Proxy's saved data (a small whole percent, written when it changes by a few points) so the bar on
    /// the screen is the same for the people watching.
    /// </summary>
    internal partial class ProxyBody
    {
        internal const string KeyStamina = "dh_tm_stam";
        internal const float StaminaMax = 60f;
        private static readonly MethodInfo CrouchMethod = AccessTools.Method(typeof(Character), "SetCrouch");

        internal float Stamina = StaminaMax;
        private float _staminaRest, _nextStaminaWrite;
        private int _lastStaminaWritten = 100;
        private bool _crouching;

        /// <summary>0 to 100: this game's own number if it owns the Proxy, else what the owner last wrote.</summary>
        internal int StaminaPercent => IsOwner ? Mathf.RoundToInt(100f * Stamina / StaminaMax) : (View != null && View.GetZDO() != null ? View.GetZDO().GetInt(KeyStamina, 100) : 100);

        /// <summary>Spends stamina for an action; false (and nothing spent) when there is too little.</summary>
        private bool Spend(float cost)
        {
            if (Stamina < Mathf.Min(cost, 6f)) return false;
            Stamina = Mathf.Max(0f, Stamina - cost);
            _staminaRest = Time.time + 1.0f;
            return true;
        }

        /// <summary>Wear while something is held (running, blocking), per second.</summary>
        private bool Drain(float perSecond)
        {
            if (Stamina <= 0.5f) return false;
            Stamina = Mathf.Max(0f, Stamina - perSecond * Time.fixedDeltaTime);
            _staminaRest = Time.time + 0.8f;
            return true;
        }

        /// <summary>Every frame on the owner: rest gives stamina back, and the number goes to the saved data when it has moved.</summary>
        private void StaminaTick()
        {
            if (!IsOwner) return;
            if (Time.time >= _staminaRest && Stamina < StaminaMax) Stamina = Mathf.Min(StaminaMax, Stamina + 12f * Time.deltaTime);
            if (Time.time < _nextStaminaWrite) return;
            int percent = Mathf.RoundToInt(100f * Stamina / StaminaMax);
            if (Mathf.Abs(percent - _lastStaminaWritten) >= 3 || (percent != _lastStaminaWritten && (percent == 0 || percent == 100)))
            {
                _nextStaminaWrite = Time.time + 0.5f;
                _lastStaminaWritten = percent;
                View.GetZDO().Set(KeyStamina, percent);
            }
        }

        private void Crouch(bool on)
        {
            _crouching = on;
            try { CrouchMethod?.Invoke(Body, new object[] { on }); } catch (System.Exception) { }
        }
    }
}
