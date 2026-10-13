using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;

namespace Scripts
{
    /// <summary>
    /// The caps a world's owner may change (docs/scripting-api.md: caps are data, not API). The defaults are the safe ones the API reference
    /// lists; a script that is refused says which cap and where to change it, so nobody has to guess why "two boars" became one.
    /// </summary>
    internal static class Limits
    {
        private static ConfigEntry<int> _spawns, _alive, _effects, _pieces, _piecesAlive, _statusEffects, _itemsHour;
        private static ConfigEntry<string> _events, _endpoints, _autostart;
        private static ConfigEntry<bool> _mayGive, _mayHealHurt, _mayEffects, _maySee, _mayTake, _mayTeleport, _mayShove;

        // the server's numbers, while this game is connected to a server that sent them (docs/scripting-multiplayer.md section 8)
        private static JObject _server;

        private static int Num(string key, ConfigEntry<int> own, int fallback)
        {
            if (_server != null && _server.TryGetValue(key, out JToken t) && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)) return (int)t;
            return own != null ? own.Value : fallback;
        }

        internal static int SpawnsPerMinute => Num("spawns", _spawns, 5);
        internal static int Alive => Num("alive", _alive, 20);
        internal static int EffectsPerSecond => Num("effects", _effects, 10);
        internal static int PiecesPerMinute => Num("pieces", _pieces, 20);
        internal static int PiecesAlive => Num("piecesAlive", _piecesAlive, 200);
        internal static int StatusEffectsPerMinute => Num("status", _statusEffects, 20);
        internal static int ItemsPerHour => Num("items", _itemsHour, 50);

        /// <summary>True while the numbers shown are a server's, not this game's own.</summary>
        internal static bool FromServer => _server != null;

        /// <summary>The numbers a server sends to the players who have the mod (never the web addresses).</summary>
        internal static JObject ToJson() => new JObject
        {
            ["spawns"] = SpawnsPerMinute, ["alive"] = Alive, ["effects"] = EffectsPerSecond, ["pieces"] = PiecesPerMinute, ["piecesAlive"] = PiecesAlive, ["status"] = StatusEffectsPerMinute, ["items"] = ItemsPerHour,
            ["events"] = new JArray(Events.Cast<object>().ToArray()),
        };

        /// <summary>A server's numbers apply while connected to it.</summary>
        internal static void ApplyServer(JObject d) { _server = d; }

        internal static void ClearServer() { _server = null; }

        /// <summary>The random events (raids) a script may start: names from the game, comma separated.</summary>
        internal static HashSet<string> Events
        {
            get
            {
                if (_server != null && _server["events"] is JArray list) return new HashSet<string>(list.Where(x => x.Type == JTokenType.String).Select(x => (string)x).Take(50));
                return new HashSet<string>(((_events != null ? _events.Value : "army_eikthyr,army_theelder,foresttrolls")).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));
            }
        }

        /// <summary>The web addresses a script may send to, by the name it uses: "discord=https://...;stats=https://...".</summary>
        internal static Dictionary<string, string> Endpoints
        {
            get
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string text = _endpoints != null ? _endpoints.Value : "";
                foreach (string part in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = part.Substring(0, eq).Trim(), url = part.Substring(eq + 1).Trim();
                    if (name.Length > 0 && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) d[name] = url;
                }
                return d;
            }
        }

        /// <summary>
        /// The limits that matter for the powers a script uses, in short lines for the editor's side panel (the coder has to know them: a
        /// refusal is not an error). The numbers that are settings are read live; the rest are the API reference's caps.
        /// </summary>
        internal static string[] Lines(string[] needs)
        {
            var lines = new List<string>();
            lines.Add(Editor.Words.L("scripts_limit_time", "Time: 1 ms a frame for each script"));
            foreach (string n in needs)
            {
                switch (n)
                {
                    case "messages": lines.Add(Editor.Words.L("scripts_limit_messages", "Messages: 5 a second; announcements: 1 a second")); break;
                    case "effects": lines.Add(Editor.Words.L("scripts_limit_effects", "Effects and sounds: {0} a second, within 100 m of a player", EffectsPerSecond)); break;
                    case "items": lines.Add(Editor.Words.L("scripts_limit_items", "Give: 10 at a time, {0} an hour for each player", ItemsPerHour)); break;
                    case "takeItems": lines.Add(Editor.Words.L("scripts_limit_take", "Take: 20 an hour for each player")); break;
                    case "spawn":
                        lines.Add(Editor.Words.L("scripts_limit_spawn", "Spawn: {0} a minute, {1} alive, within 60 m of a player", SpawnsPerMinute, Alive));
                        lines.Add(Editor.Words.L("scripts_limit_drop", "Drop items: 10 at a time, {0} an hour", ItemsPerHour));
                        break;
                    case "combat": lines.Add(Editor.Words.L("scripts_limit_combat", "Hurting a player: once a second, never the last point")); break;
                    case "weather": lines.Add(Editor.Words.L("scripts_limit_weather", "Weather: once a minute; time of day: once every 20 seconds")); break;
                    case "world": lines.Add(Editor.Words.L("scripts_limit_world", "Pieces: {0} a minute, {1} alive, decorations only", PiecesPerMinute, PiecesAlive)); break;
                    case "teleport": lines.Add(Editor.Words.L("scripts_limit_teleport", "Teleport: once in 10 seconds for each player")); break;
                    case "store": lines.Add(Editor.Words.L("scripts_limit_store", "Memory: 64 KB for each script")); break;
                    case "net": lines.Add(Editor.Words.L("scripts_limit_net", "Web: one message every 10 seconds, only to addresses the owner listed")); break;
                }
            }
            return lines.ToArray();
        }

        /// <summary>What this player lets a server's scripts do to their character (the risky ones start off).</summary>
        internal static Mp.ClientPolicy Policy() => new Mp.ClientPolicy
        {
            Give = _mayGive == null || _mayGive.Value, HealHurt = _mayHealHurt == null || _mayHealHurt.Value, Effects = _mayEffects == null || _mayEffects.Value,
            Banner = _maySee == null || _maySee.Value, Take = _mayTake != null && _mayTake.Value, Teleport = _mayTeleport != null && _mayTeleport.Value, Launch = _mayShove != null && _mayShove.Value,
        };

        /// <summary>The player lets a server change what they see (weather, effects, banners, speech bubbles).</summary>
        internal static bool MaySee => _maySee == null || _maySee.Value;

        /// <summary>Scripts a server starts by itself when its world is ready (a dedicated server has no window to press Run in).</summary>
        internal static string[] Autostart => _autostart == null ? new string[0] : _autostart.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        internal static void Bind(ConfigFile config)
        {
            _autostart = config.Bind("Server", "Scripts to start when the world starts", "", "Names (comma separated, as in the scripts folder) of scripts this game starts by itself about ten seconds after its world is ready, when it runs the world (single player, a host, or a dedicated server, which has no window to press Run in). The powers they need must already be allowed (approvals.txt next to the scripts).");
            const string mine = "My character on other servers";
            _mayGive = config.Bind(mine, "Servers may give me items", true, "A server's scripts may put small amounts of ordinary items in your bag (never boss items or trophies, only what fits).");
            _mayHealHurt = config.Bind(mine, "Servers may heal and hurt me", true, "A server's scripts may heal you, restore stamina, or hurt you (never to the last point of health, never more than 50 at once).");
            _mayEffects = config.Bind(mine, "Servers may give me status effects", true, "A server's scripts may put on you effects like Rested or Wet, for a short time.");
            _maySee = config.Bind(mine, "Servers may change what I see", true, "A server's scripts may force the weather and wind on your screen, play effects and sounds, shake the camera, show banners and speech bubbles.");
            _mayTake = config.Bind(mine, "Servers may take items from me", false, "Off by default: a server's scripts may take small amounts of ordinary items from your bag (never tools or keys).");
            _mayTeleport = config.Bind(mine, "Servers may teleport me", false, "Off by default: a server's scripts may move you to a place on the ground.");
            _mayShove = config.Bind(mine, "Servers may shove me", false, "Off by default: a server's scripts may push you (a short shove, never more than 15 metres a second).");
            _spawns = config.Bind("Limits", "Spawns per minute per script", 5, new ConfigDescription("How many creatures one script may spawn in a minute (it can use that many at once, then gets one back every 60/N seconds). Raise it for your own world; every script on a server shares the owner's choice.", new AcceptableValueRange<int>(1, 120)));
            _alive = config.Bind("Limits", "Creatures alive per script", 20, new ConfigDescription("How many creatures spawned by one script may be alive at once.", new AcceptableValueRange<int>(1, 200)));
            _effects = config.Bind("Limits", "Effects and sounds per second per script", 10, new ConfigDescription("How many visual or sound effects one script may play in a second.", new AcceptableValueRange<int>(1, 60)));
            _pieces = config.Bind("Limits", "Pieces placed per minute per script", 20, new ConfigDescription("How many decorations one script may place in a minute.", new AcceptableValueRange<int>(1, 200)));
            _piecesAlive = config.Bind("Limits", "Pieces alive per script", 200, new ConfigDescription("How many decorations placed by one script may stand at once.", new AcceptableValueRange<int>(1, 1000)));
            _statusEffects = config.Bind("Limits", "Status effects per minute per script", 20, new ConfigDescription("How many status effects (rested, wet, burning...) one script may hand out in a minute.", new AcceptableValueRange<int>(1, 200)));
            _itemsHour = config.Bind("Limits", "Items given per hour per script", 50, new ConfigDescription("How many items one script may give each player in an hour, and drop on the ground in an hour (for everyone together). Raise it for scripts that reward work, like extra wood for every log.", new AcceptableValueRange<int>(10, 5000)));
            _events = config.Bind("Allowed", "Random events a script may start", "army_eikthyr,army_theelder,foresttrolls", "The game's random event names (raids) that world.startEvent may start, comma separated. Keep to the ones your players can survive.");
            _endpoints = config.Bind("Allowed", "Web addresses scripts may send to", "", "Names for web addresses, like discord=https://example.com/hook;stats=https://example.com/in . Scripts use the name (net.send(\"discord\", text)); only https; nothing is read back.");
        }
    }
}
