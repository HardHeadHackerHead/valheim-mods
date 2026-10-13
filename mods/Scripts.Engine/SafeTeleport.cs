using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Moving a player for a script without ever leaving them under the game's black teleport screen. The game's short-range teleport
    /// (TeleportTo with distant = false) only ends once it finds a floor under the exact target, and when it does not it never ends: the screen
    /// stays black (found in the game, 2026-10-12). So a short move puts the player straight on the ground, and a long one uses the game's
    /// long-range teleport (which loads the area) with a watchdog that ends it after 25 seconds whatever happens. Either way the fall height is
    /// reset, so landing never counts as a fall.
    /// </summary>
    internal static class SafeTeleport
    {
        private const float Near = 100f, Watchdog = 25f;
        private static readonly FieldInfo Teleporting = typeof(Player).GetField("m_teleporting", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo TeleportTimer = typeof(Player).GetField("m_teleportTimer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo MaxAir = typeof(Character).GetField("m_maxAirAltitude", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>Puts the player on the ground at pos (its height is the ground's: the caller has worked it out). False if the game refused.</summary>
        internal static bool Go(Player p, Vector3 pos)
        {
            if (p == null) return false;
            if (Vector3.Distance(p.transform.position, pos) <= Near)
            {
                Place(p, pos);
                return true;
            }
            if (!p.TeleportTo(pos, p.transform.rotation, true)) return false;
            if (Core.Plugin != null) Core.Plugin.StartCoroutine(Watch(p, pos));
            return true;
        }

        /// <summary>The character's physics body holds its position: move the body and the transform together, stop it, and reset the fall height.</summary>
        private static void Place(Player p, Vector3 pos)
        {
            Rigidbody body = p.GetComponent<Rigidbody>();
            if (body != null) { body.position = pos; body.linearVelocity = Vector3.zero; }
            p.transform.position = pos;
            MaxAir?.SetValue(p, pos.y);
        }

        private static IEnumerator Watch(Player p, Vector3 pos)
        {
            float until = Time.time + Watchdog;
            while (Time.time < until) { if (p == null || !p.IsTeleporting()) yield break; yield return null; }
            if (p == null || !p.IsTeleporting()) yield break;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h)) pos.y = h + 0.2f;
            Place(p, pos);
            Teleporting?.SetValue(p, false);
            TeleportTimer?.SetValue(p, 0f);
            Core.Log.LogWarning("Blockscript: a teleport did not finish in " + Watchdog + " s, so it was ended where it was going");
        }
    }
}
