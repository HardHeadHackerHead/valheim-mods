using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Rainbows
{
    /// <summary>A "rainbow" command for the Claude Tools mod (its request mailbox), so an AI assistant can test: what the weather is doing, and a rainbow on demand.</summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.dhack.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "rainbow", "rainbow status | now [sun height degrees] [double] | stop | blessing [double]: what the weather is doing; a rainbow now in front of the camera; end it; give the blessing",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: rainbow command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the rainbow command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
        }

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray();   // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

            if (sub == "now")
            {
                float h = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : 18f;
                Sky.Force(Mathf.Clamp(h, 5f, 38f), args.Length > 2 && args[2].ToLowerInvariant() == "double");
            }
            else if (sub == "stop") Sky.Stop();
            else if (sub == "shader") { output(new JObject { ["shader"] = Sky.UseShader(string.Join(" ", args.Skip(1))) }); yield break; }
            else if (sub == "blessing") Blessing.Give(player, args.Length > 1 && args[1].ToLowerInvariant() == "double");

            EnvSetup env = EnvMan.instance != null ? EnvMan.instance.GetCurrentEnvironment() : null;
            output(new JObject
            {
                ["weather"] = env?.m_name, ["wet"] = env != null && env.m_isWet, ["rainedFor"] = Sky.WetFor, ["waitingForSun"] = Sky.Waiting,
                ["sunHeight"] = Sky.SunHeight, ["why"] = Sky.Why, ["showing"] = Sky.Showing, ["double"] = Sky.Double, ["fade"] = Sky.Fade,
                ["debug"] = Sky.Debug, ["blessing"] = Blessing.Has(player), ["rainbowYaw"] = Mathf.Atan2(Sky.Anti.x, Sky.Anti.z) * Mathf.Rad2Deg,
            });
        }
    }
}
