using System;
using System.Collections.Generic;
using System.Linq;
using Jint;
using Jint.Native;
using UnityEngine;

namespace Scripts
{
    /// <summary>
    /// Sights and sounds a script can make (docs/scripting-expansion.md section 3): a list of the game's effect prefabs that is data, and a
    /// check on every prefab before it is used: it may carry only parts that draw or play something (particles, lights, audio, a timed
    /// destroy, the network view that shows it to everyone). A prefab with anything else is refused by name, even if somebody puts it on
    /// the list, so an effect can never hurt, spawn, build or change anything. Banners, pings and the camera shake are here too: all are
    /// things the player sees.
    /// </summary>
    internal static class Effects
    {
        // What an effect prefab may carry (measured 2026-10-12 with Claude Tools' inspect on every name in the lists).
        private static readonly HashSet<string> Harmless = new HashSet<string>
        {
            "Transform", "RectTransform", "ParticleSystem", "ParticleSystemRenderer", "AudioSource", "AudioLowPassFilter", "AudioHighPassFilter", "AudioReverbFilter",
            "Light", "LightFlicker", "LightLod", "CamShaker", "TimedDestruction", "ZNetView", "ZSFX", "ZSyncTransform", "ParticleDecal", "VortexParticles",
            "LODGroup", "MeshRenderer", "MeshFilter", "TrailRenderer", "LineRenderer", "Animator", "Billboard",
        };

        private static readonly Dictionary<string, string> Checked = new Dictionary<string, string>();

        /// <summary>Why a prefab can't be used as an effect, or "" when it is purely visual or audio.</summary>
        internal static string Unsafe(GameObject prefab, string name)
        {
            if (Checked.TryGetValue(name, out string known)) return known;
            string why = "";
            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string type = c.GetType().Name;
                if (Harmless.Contains(type)) continue;
                why = "'" + name + "' carries '" + type + "', which could do more than show something";
                break;
            }
            if (why.Length > 0) Core.Log.LogWarning("Scripts: " + why + " (refused as an effect)");
            Checked[name] = why;
            return why;
        }

        /// <summary>Plays an effect or a sound at a place. Refusals say why.</summary>
        internal static bool Play(Script s, string name, Vector3 pos, JsValue opts, bool sound)
        {
            HashSet<string> list = sound ? Lists.Sounds : Lists.VisualEffects;
            if (!list.Contains(name)) { Host.Refuse(s, "'" + name + "' is not on the list of " + (sound ? "sounds" : "effects")); return false; }
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (prefab == null) { Host.Refuse(s, "this game has no effect called '" + name + "'"); return false; }
            string why = Unsafe(prefab, name);
            if (why.Length > 0) { Host.Refuse(s, why); return false; }
            if (!Players.NearAPlayer(pos, 100f)) { Host.Refuse(s, "that place is too far from every player"); return false; }
            int per = Limits.EffectsPerSecond;
            if (!Players.Rate(s, "effect", per, per)) { Host.Refuse(s, "more than " + per + " effects a second (the owner can change it in the Blockscript settings)"); return false; }
            float scale = 1f;
            if (opts != null && opts.IsObject() && opts.AsObject().Get("scale") is JsValue sc && sc.IsNumber()) scale = Mathf.Clamp((float)sc.AsNumber(), 0.5f, 3f);
            // one with a network view is shown to everyone by the game itself; one without is only on the screen that makes it, so every
            // player's game that has the mod is asked to make its own copy (a server with no screen of its own makes none)
            // an effect is never a world object: each screen makes its own copy with no network record (one made with a record leaves that
            // record behind when the effect ends, and the game then waits for it forever: a respawn next to it never finished). This game's
            // screen plays it here, and every player's game that has the mod is asked to play its own copy.
            if (Player.m_localPlayer != null) SpawnLocal(prefab, pos, scale);
            Mp.Remote.FxEffect(name, pos, scale, sound);
            return true;
        }

        /// <summary>Makes an effect on this game's screen only, with no network record (ZNetView.m_forceDisableInit while it is made).</summary>
        internal static void SpawnLocal(GameObject prefab, Vector3 pos, float scale)
        {
            GameObject go;
            bool was = ZNetView.m_forceDisableInit;
            ZNetView.m_forceDisableInit = true;
            try { go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = was; }
            if (Math.Abs(scale - 1f) > 0.01f) go.transform.localScale *= scale;
            UnityEngine.Object.Destroy(go, 30f);   // (they destroy themselves; this is only for one that doesn't)
        }

        /// <summary>Camera shake for players near a place. Shown on this game (the host's, or the single player's).</summary>
        internal static bool Shake(Script s, Vector3 pos, double range, double strength)
        {
            if (GameCamera.instance == null && !Mp.Remote.AnyAgent) return false;
            if (!Players.Rate(s, "shake", 2.0, 2.0)) { Host.Refuse(s, "camera shakes more than twice a second"); return false; }
            if (GameCamera.instance != null) GameCamera.instance.AddShake(pos, (float)Math.Max(1.0, Math.Min(60.0, range)), (float)Math.Max(0.0, Math.Min(3.0, strength)), false);
            Mp.Remote.FxShake(pos, range, strength);
            return true;
        }

        private static bool _pinging;

        /// <summary>True while a script's own ping is going round, so the chat patch doesn't take it for a player's words.</summary>
        internal static bool Sending => _pinging;

        /// <summary>The map ping everyone sees (the game's own shout ping at a place).</summary>
        internal static bool Ping(Script s, Vector3 pos, string text)
        {
            if (ZRoutedRpc.instance == null) return false;   // (a dedicated server has no player of its own: the ping is the server's)
            if (!Players.Rate(s, "ping", 1.0 / 3.0, 1.0)) { Host.Refuse(s, "pings more than once in three seconds"); return false; }
            _pinging = true;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(0L, "ChatMessage", pos, (int)Talker.Type.Ping, UserInfo.GetLocalUser(), text.Length > 60 ? text.Substring(0, 60) : text); }
            finally { _pinging = false; }
            Audit.Add(s, $"pinged {pos.x:0},{pos.z:0}");
            return true;
        }

        /// <summary>The big banner in the middle of the screen (the one a new biome gets). To one player, or to everyone with player == null.</summary>
        internal static bool Banner(Script s, Player player, string text)
        {
            if (!Players.Rate(s, player == null ? "banner" : "banner-" + Handles.Id(player), 1.0 / 3.0, 1.0)) { Host.Refuse(s, "banners more than once in three seconds"); return false; }
            if (player != null)
            {
                if (Mp.Remote.IsRemote(player)) return Mp.Remote.Banner(s, player, text);
                if (MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(text, true);
                return true;
            }
            if (Player.m_localPlayer != null && MessageHud.instance != null) MessageHud.instance.ShowBiomeFoundMsg(text, true);
            Mp.Remote.FxBanner(text);   // (the games that have the mod show the banner; the others get the plain centre message)
            foreach (Player p in Player.GetAllPlayers().ToArray())
                if (p != Player.m_localPlayer && !Mp.Wire.HasAgent(p.GetOwner())) p.Message(MessageHud.MessageType.Center, text);
            return true;
        }

        /// <summary>A speech bubble above a player or creature, on this game (the game's own NPC text).</summary>
        internal static bool Talk(Script s, Character c, string text)
        {
            if (Chat.instance == null && !Mp.Remote.AnyAgent) return false;
            if (!Players.Rate(s, "talk", 5.0, 5.0)) { Host.Refuse(s, "speech bubbles more than five times a second"); return false; }
            string name = c is Player p ? p.GetPlayerName() : Localization.instance.Localize(c.m_name);
            string cut = text.Length > 120 ? text.Substring(0, 120) : text;
            if (Chat.instance != null) Chat.instance.SetNpcText(c.gameObject, Vector3.up * 2.2f, 30f, 6f, name, cut, false);
            Mp.Remote.FxTalk(c, name, cut);
            return true;
        }
    }
}
