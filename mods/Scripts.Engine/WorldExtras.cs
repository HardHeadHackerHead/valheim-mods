using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Scripts
{
    /// <summary>Wind, random events (raids) and the web: the world-level actions of docs/scripting-expansion.md section 3.</summary>
    internal static class WorldExtras
    {
        private static double _windUntil;

        internal static bool SetWind(Script s, double degrees, double strength, double seconds)
        {
            if (EnvMan.instance == null && !Mp.Remote.AnyAgent) return false;
            if (!Players.Rate(s, "wind", 0.1, 1.0)) { Host.Refuse(s, "wind changes more than once in ten seconds"); return false; }
            seconds = Math.Min(600.0, Math.Max(10.0, seconds));
            if (EnvMan.instance != null) EnvMan.instance.SetDebugWind((float)(((degrees % 360.0) + 360.0) % 360.0), (float)Math.Max(0.0, Math.Min(1.0, strength)));
            Mp.Remote.FxWind(degrees, strength, seconds);
            _windUntil = Time.timeAsDouble + seconds;
            Audit.Add(s, $"set the wind to {degrees:0} degrees, strength {strength:0.0}, for {seconds:0} s");
            return true;
        }

        private static bool Server => ZNet.instance == null || ZNet.instance.IsServer();
        private static bool _listed;

        internal static bool StartEvent(Script s, string name)
        {
            if (RandEventSystem.instance == null || !Server) { Host.Refuse(s, "events can only be started by the game that runs the world"); return false; }
            if (!_listed)
            {
                _listed = true;
                Core.Log.LogInfo("Scripts: this game's random events: " + string.Join(", ", RandEventSystem.instance.m_events.Select(x => x.m_name).ToArray()));
            }
            if (!Limits.Events.Contains(name)) { Host.Refuse(s, "'" + name + "' is not one of the events scripts may start (the owner lists them in the Blockscript settings)"); return false; }
            if (!RandEventSystem.instance.HaveEvent(name)) { Host.Refuse(s, "this game has no event called '" + name + "'"); return false; }
            Player first = Player.m_localPlayer != null ? Player.m_localPlayer : Player.GetAllPlayers().FirstOrDefault();
            if (first == null) return false;
            if (!Players.Rate(s, "event", 1.0 / 120.0, 1.0)) { Host.Refuse(s, "events more than once in two minutes"); return false; }
            RandEventSystem.instance.SetRandomEventByName(name, first.transform.position);
            Audit.Add(s, $"started the event {name}");
            return true;
        }

        internal static bool StopEvent(Script s)
        {
            if (RandEventSystem.instance == null || !Server) return false;
            RandEventSystem.instance.ResetRandomEvent();
            Audit.Add(s, "ended the event");
            return true;
        }

        /// <summary>Every frame: puts the wind back when a script's time for it is up.</summary>
        internal static void Tick()
        {
            if (_windUntil > 0.0 && Time.timeAsDouble >= _windUntil)
            {
                _windUntil = 0.0;
                if (EnvMan.instance != null) EnvMan.instance.ResetDebugWind();
            }
        }

        internal static void Reset()
        {
            if (_windUntil > 0.0 && EnvMan.instance != null) EnvMan.instance.ResetDebugWind();
            _windUntil = 0.0;
        }

        // ---- the web ----

        internal static bool Send(Script s, string endpoint, string text)
        {
            if (!Limits.Endpoints.TryGetValue(endpoint, out string url)) { Host.Refuse(s, "no web address called '" + endpoint + "' (the owner lists them in the Blockscript settings)"); return false; }
            if (!Players.Rate(s, "net", 0.1, 1.0)) { Host.Refuse(s, "web messages more than once in ten seconds"); return false; }
            string world = ZNet.instance != null ? ZNet.instance.GetWorldName() : "";
            string body = new JObject { ["text"] = text, ["script"] = s.Name, ["world"] = world }.ToString(Newtonsoft.Json.Formatting.None);
            if (Core.Plugin == null) return false;
            Core.Plugin.StartCoroutine(Post(url, body));
            Audit.Add(s, $"sent a web message to {endpoint}");
            return true;
        }

        private static IEnumerator Post(string url, string body)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.redirectLimit = 0;
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) Core.Log.LogInfo("Scripts: a web message did not go through: " + request.error);
            }
        }
    }
}
