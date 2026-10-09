using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// What drink does to the way you play: you slide and overshoot when you walk (your steering lags), your walk wanders to one side and the
    /// other, your aim floats, and sloshed your controls reverse for a moment now and then. Chat slurs. Each patch is only applied if the game
    /// has the method it hooks (the names are the game's, and can change).
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetControls")]
    internal static class Player_SetControls_Drunk
    {
        private static Vector3 _smooth;
        private static float _flipUntil, _nextFlip;

        private static bool Prepare() => AccessTools.Method(typeof(Player), "SetControls") != null;

        private static void Prefix(Player __instance, ref Vector3 movedir)
        {
            if (__instance != Player.m_localPlayer) return;
            float level = Tipsy.Level;
            if (level < 8f) { _smooth = movedir; return; }
            float s = Fx.Strength, t = Time.time;

            // steering lags: you keep going a moment after you let go, and take a moment to turn
            float lag = Fx.F(20f, 100f) * s * Plugin.Drift.Value / 100f;
            _smooth = Vector3.Lerp(_smooth, movedir, 1f - Mathf.Exp(-Time.deltaTime * Mathf.Lerp(24f, 2.6f, Mathf.Clamp01(lag))));
            if (lag > 0.02f) movedir = _smooth;
            if (movedir.sqrMagnitude < 0.0004f) return;

            // the walk wanders
            float wander = Fx.F(22f, 100f) * s * Plugin.Drift.Value / 100f;
            float angle = (Mathf.Sin(t * 0.8f) + 0.5f * Mathf.Sin(t * 1.9f + 0.7f)) * 30f * wander;
            movedir = Quaternion.Euler(0f, angle, 0f) * movedir;

            // sloshed: everything goes the wrong way for a moment
            if (Plugin.ConfusionOn.Value && level >= 85f)
            {
                if (_nextFlip == 0f) _nextFlip = t + Random.Range(8f, 16f);
                if (t >= _nextFlip)
                {
                    _nextFlip = t + Random.Range(16f, 32f);
                    _flipUntil = t + 1.3f;
                    __instance.Message(MessageHud.MessageType.TopLeft, "The room spins...");
                }
                if (t < _flipUntil) movedir = -movedir;
            }
            else _nextFlip = 0f;
        }
    }

    // Your aim floats: it follows the mouse a little late and drifts.
    [HarmonyPatch(typeof(Player), "SetMouseLook")]
    internal static class Player_SetMouseLook_Drunk
    {
        private static Vector2 _look;

        private static bool Prepare() => AccessTools.Method(typeof(Player), "SetMouseLook") != null;

        private static void Prefix(Player __instance, ref Vector2 mouseLook)
        {
            if (__instance != Player.m_localPlayer) return;
            float k = Fx.F(25f, 100f) * Fx.Strength * Plugin.Drift.Value / 100f;
            if (k <= 0.01f) { _look = mouseLook; return; }
            _look = Vector2.Lerp(_look, mouseLook, Mathf.Lerp(1f, 0.16f, Mathf.Clamp01(k)));
            float t = Time.time;
            mouseLook = _look + new Vector2(Mathf.Sin(t * 0.9f), Mathf.Sin(t * 0.6f + 1f) * 0.6f) * 0.05f * k;
        }
    }

    // What you say comes out slurred (your own chat, to everyone).
    [HarmonyPatch(typeof(Chat), "SendText")]
    internal static class Chat_SendText_Slur
    {
        private static bool Prepare() => AccessTools.Method(typeof(Chat), "SendText") != null;

        private static void Prefix(ref string text)
        {
            if (!Plugin.SlurOn.Value || string.IsNullOrEmpty(text) || text.StartsWith("/") || Tipsy.Level < 30f) return;
            text = Slur(text, Fx.F(30f, 100f));
        }

        private static string Slur(string text, float k)
        {
            var sb = new StringBuilder();
            string[] words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string w = words[i];
                if (Random.value < 0.25f + 0.55f * k)
                {
                    w = w.Replace("s", "sh").Replace("S", "Sh").Replace("th", "d").Replace("r", "rr");
                    if (Random.value < 0.35f * k && w.Length > 2) w = w + w.Substring(w.Length - 1, 1) + w.Substring(w.Length - 1, 1);
                }
                sb.Append(w);
                if (Random.value < 0.12f + 0.2f * k) sb.Append(" *hic*");
                if (i < words.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }
    }
}
