using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jint;
using Jint.Native;
using UnityEngine;
using static Scripts.Api;

namespace Scripts
{
    /// <summary>
    /// The events a script can listen to (docs/scripting-api.md section 3): where the game's own code is patched to tell the host, and what
    /// is watched by looking once a second (who is online, morning and night, bosses). Each event object is made inside the engine of
    /// every script that listens, from plain values and handles. Events the API lists but that are not hooked yet are refused at on(), by
    /// name, so a script never waits for something that will not come.
    /// </summary>
    internal static class Events
    {
        private static readonly HashSet<string> Built = new HashSet<string> { "start", "stop", "playerJoin", "playerLeave", "playerDeath", "kill", "chat", "dayStart", "nightStart", "bossSpawn", "bossDeath" };
        private static readonly HashSet<string> Listed = new HashSet<string>
        {
            "start", "stop", "playerJoin", "playerLeave", "playerDeath", "kill", "hit", "pieceBuilt", "itemPickup", "bossSpawn", "bossDeath", "chat", "dayStart", "nightStart", "proxyEvent",
            "eat", "skillUp", "raidStart", "raidEnd", "weatherChange", "playerRespawn", "command", "tamed",
        };

        /// <summary>An event the patches in NewEvents managed to hook (or that is watched, not patched).</summary>
        internal static void Mark(string name) { Built.Add(name); }

        private static readonly Dictionary<string, int> DeathCounts = new Dictionary<string, int>();

        /// <summary>How many times a player has died since the game started (handle ids are for one session).</summary>
        internal static int Deaths(string id) => DeathCounts.TryGetValue(id, out int n) ? n : 0;

        /// <summary>A name from the API's list; one that is listed but not built yet is an error that says so.</summary>
        internal static bool Known(string name)
        {
            NewEvents.Hook();
            if (!Listed.Contains(name)) return false;
            if (!Built.Contains(name) && name != "proxyEvent") throw new ScriptError("The event '" + name + "' is not built yet in this version.");
            return true;
        }

        private static readonly System.Reflection.FieldInfo Commands = AccessTools.Field(typeof(global::Terminal), "commands");

        /// <summary>A console command of the game (or of a mod that adds to it) has this name.</summary>
        internal static bool GameCommand(string name) => Commands?.GetValue(null) is System.Collections.IDictionary d && d.Contains(name);

        // ---- watching ----

        private static float _next;
        private static HashSet<long> _online = new HashSet<long>();
        private static readonly Dictionary<long, string> _names = new Dictionary<long, string>();   // (a player who has left is gone: their name is kept from when they were here)
        private static HashSet<string> _bosses = new HashSet<string>();
        private static bool? _night;
        private static string _weather;

        internal static void Poll()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            NewEvents.Hook();
            bool serverMode = ZNet.instance != null && ZNet.instance.IsServer();   // (a dedicated server has no local player but plenty of players)
            if (ZNet.instance == null || (Player.m_localPlayer == null && !serverMode)) { _online.Clear(); _night = null; _weather = null; return; }
            WatchChat();

            string weather = Api.CurrentWeather();
            if (_weather != null && weather != _weather) { string from = _weather, to = weather; Host.Fire("weatherChange", e => Obj(e, ("from", Text(from)), ("to", Text(to)))); }
            _weather = weather;

            if (Proxies.Bridge != null && Proxies.Call("TakeEvents") is string[] proxyEvents)
                foreach (string kind in proxyEvents) { string k = kind; Host.Fire("proxyEvent", e => Obj(e, ("kind", Text(k)))); }

            var now = new HashSet<long>(Player.GetAllPlayers().Select(p => p.GetPlayerID()));
            foreach (Player p in Player.GetAllPlayers())
            {
                string pn = p.GetPlayerName();
                if (!string.IsNullOrEmpty(pn)) _names[p.GetPlayerID()] = pn;
                if (!_online.Contains(p.GetPlayerID())) { Player joined = p; Host.Fire("playerJoin", e => Obj(e, ("player", Handles.Actor(Script(e), joined)))); }
            }
            foreach (long id in _online.Where(x => !now.Contains(x)).ToList())
            {
                string name = _names.TryGetValue(id, out string known) ? known : "";
                _names.Remove(id);
                Host.Fire("playerLeave", e => Obj(e, ("player", Obj(e, ("id", Text("p" + id)), ("name", Text(name)), ("valid", Bool(false)), ("prefab", Text("Player"))))));
            }
            _online = now;

            if (EnvMan.instance != null)
            {
                bool night = EnvMan.IsNight();
                if (_night.HasValue && _night.Value != night)
                {
                    int day = DayNumber();
                    Host.Fire(night ? "nightStart" : "dayStart", e => Obj(e, ("day", Num(day))));
                }
                _night = night;
            }

            var seen = new HashSet<string>();
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || !c.IsBoss() || c.IsDead()) continue;
                string id = Handles.Id(c);
                seen.Add(id);
                if (_bosses.Add(id))
                {
                    Character boss = c;
                    Host.Fire("bossSpawn", e => Obj(e, ("boss", Handles.Actor(Script(e), boss)), ("biome", Text(BiomeName(boss.transform.position))), ("pos", Vec(e, boss.transform.position))));
                }
            }
            _bosses.IntersectWith(seen);
        }

        internal static int DayNumber() => EnvMan.instance == null || ZNet.instance == null ? 1 : (int)(ZNet.instance.GetTimeSeconds() / EnvMan.instance.m_dayLengthSec) + 1;

        internal static string BiomeName(Vector3 p)
        {
            Heightmap.Biome b = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(p) : Heightmap.Biome.Meadows;
            return b == Heightmap.Biome.None ? "Meadows" : b.ToString();
        }

        /// <summary>The script an event is being made for (an engine belongs to exactly one script).</summary>
        private static Script Script(Engine e) => Host.Scripts.Find(s => ReferenceEquals(s.Engine, e));

        // ---- the game's own deaths ----

        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        /// <summary>
        /// Before the game handles a death: what died and who killed it, while the creature still has its network record (OnDeath removes
        /// it, and after that every dead creature has the same empty id, so kills could not be told apart).
        /// </summary>
        internal static Handles.Snap[] BeforeDeath(Character victim)
        {
            try
            {
                if (victim == null) return null;
                Character killer = null;
                try { killer = LastHit(victim)?.GetAttacker(); } catch (Exception) { }
                return new[] { Handles.Take(victim), Handles.Take(killer) };
            }
            catch (Exception) { return null; }
        }

        internal static void Died(Character victim, Handles.Snap[] before = null)
        {
            try
            {
                if (victim == null) return;
                Character killer = null;
                try { killer = LastHit(victim)?.GetAttacker(); } catch (Exception) { }
                // Snapshots taken before the death (the record still there), else now: scripts get the event a frame later, when what died is already gone.
                Handles.Snap v = before != null && before[0] != null ? before[0] : Handles.Take(victim);
                Handles.Snap k = before != null && before[1] != null ? before[1] : Handles.Take(killer);
                Vector3 pos = victim.transform.position;
                bool boss = victim.IsBoss();
                string cause = "other";
                if (victim is Player)
                {
                    HitData hit = null; try { hit = LastHit(victim); } catch (Exception) { }
                    cause = hit == null ? "other" : hit.m_hitType == HitData.HitType.Fall ? "fall" : hit.m_hitType == HitData.HitType.Drowning ? "drown" : hit.m_hitType == HitData.HitType.Burning ? "fire" : hit.m_hitType == HitData.HitType.Freezing ? "cold" : hit.m_hitType == HitData.HitType.Poisoned ? "poison" : k != null ? "creature" : "other";
                }
                if (Mp.Wire.IsAgent) Mp.Agent.ForwardDeath(victim, v, k, pos, boss, cause);   // (a player's game tells the server what died here: it cannot see it itself)
                else if (!Mp.Remote.FirstOwn("kill", v.Id)) return;                           // (the player's game told the server first: one event, not two)
                FireDeath(v, k, pos, boss, cause);
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: a death could not be passed on: " + ex.Message); }
        }

        /// <summary>The death events for the scripts, from snapshots: the game's own deaths come here, and so do the ones a player's game forwards.</summary>
        internal static void FireDeath(Handles.Snap v, Handles.Snap k, Vector3 pos, bool boss, string cause)
        {
            Host.Fire("kill", e => { Script s = Script(e); return Obj(e, ("victim", Handles.ActorOrFrozen(s, v)), ("killer", Handles.ActorOrFrozen(s, k)), ("pos", Vec(e, pos))); });
            if (boss)
                Host.Fire("bossDeath", e => { Script s = Script(e); return Obj(e, ("boss", Handles.ActorOrFrozen(s, v)), ("biome", Text(BiomeName(pos))), ("pos", Vec(e, pos)), ("killer", Handles.ActorOrFrozen(s, k))); });
            if (v.IsPlayer) DeathCounts[v.Id] = Deaths(v.Id) + 1;
            if (v.IsPlayer)
                Host.Fire("playerDeath", e => { Script s = Script(e); return Obj(e, ("player", Handles.ActorOrFrozen(s, v)), ("pos", Vec(e, pos)), ("biome", Text(BiomeName(pos))), ("cause", Text(cause)), ("killer", Handles.ActorOrFrozen(s, k))); });
        }

        internal static void Said(Player player, string text, string channel)
        {
            try
            {
                if (player == null || text == null) return;
                if (text.Length > 300) text = text.Substring(0, 300);
                Host.Fire("chat", e => Obj(e, ("player", Handles.Actor(Script(e), player)), ("text", Text(text)), ("channel", Text(channel))));
            }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: a chat line could not be passed on: " + ex.Message); }
        }

        /// <summary>"/name args" typed by the local player: a script's command, if one has that name. True when it was one (the game does not also see it).</summary>
        internal static bool Command(string text) => Player.m_localPlayer != null && RunCommand(Player.m_localPlayer, text);

        /// <summary>"/name args" typed by a player (on this game, or on another one: the chat line reaches the server too).</summary>
        internal static bool RunCommand(Player me, string text)
        {
            if (text == null || !text.StartsWith("/") || me == null) return false;
            string[] parts = text.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string name = parts[0].ToLowerInvariant();
            foreach (Script s in Host.Scripts)
            {
                if (s.State != RunState.Running || !s.Commands.TryGetValue(name, out JsValue fn)) continue;
                string[] args = parts.Skip(1).ToArray();
                if (s.Handlers.ContainsKey("command"))
                {
                    Player who = me; string typed = name; string[] words = args; Script owner = s;
                    Host.Enqueue(s, "command", e => Obj(e, ("player", Handles.Actor(owner, who)), ("name", Text(typed)), ("args", Array(e, words.Select(w => Text(w)).ToArray()))));
                }
                try
                {
                    s.Budget.Arm(Host.CallMs, Host.CallGrowthBytes, Host.CallStatements);
                    s.Engine.Invoke(fn, JsValue.Undefined, new JsValue[] { Handles.Actor(s, me), Array(s.Engine, args.Select(a => Text(a)).ToArray()) });
                }
                catch (Exception ex) { s.Say("ERROR: " + Sandbox.Plain(ex)); }
                finally { s.Budget.Disarm(); }
                return true;
            }
            return false;
        }

        private static ZRoutedRpc _chatWatchOn;

        /// <summary>
        /// A dedicated server has no chat window, so the game's chat patch never runs there. When there is none, listen to the same routed
        /// message the chat window would (the game throws when a name is registered twice, so a game that does have one just ignores this).
        /// </summary>
        internal static void WatchChat()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null || rpc == _chatWatchOn || Chat.instance != null || ZNet.instance == null || !ZNet.instance.IsServer()) return;
            _chatWatchOn = rpc;
            try { rpc.Register<Vector3, int, UserInfo, string>("ChatMessage", ServerChat); }
            catch (Exception) { }
        }

        private static void ServerChat(long sender, Vector3 pos, int type, UserInfo user, string text)
        {
            try { ChatLine(sender, (Talker.Type)type, text); }
            catch (Exception ex) { Core.Log.LogWarning("Scripts: a chat line could not be passed on: " + ex.Message); }
        }

        /// <summary>A chat line, from the game's chat window or from the server's own listener: the chat event, and a "/command" from another player.</summary>
        internal static void ChatLine(long senderID, Talker.Type type, string text)
        {
            if (Effects.Sending || type == Talker.Type.Ping || text == null) return;
            Player sender = Player.GetAllPlayers().FirstOrDefault(p => p.GetOwner() == senderID);
            string channel = type == Talker.Type.Shout ? "shout" : type == Talker.Type.Whisper ? "whisper" : "normal";
            Said(sender, text, channel);
            if (sender != null && sender != Player.m_localPlayer && Mp.Remote.IsRemote(sender) && text.StartsWith("/")) RunCommand(sender, text);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Character_OnDeath_Scripts
    {
        private static void Prefix(Character __instance, out Handles.Snap[] __state) { __state = Events.BeforeDeath(__instance); }
        private static void Postfix(Character __instance, Handles.Snap[] __state) => Events.Died(__instance, __state);
    }

    [HarmonyPatch(typeof(Chat), nameof(Chat.OnNewChatMessage))]
    internal static class Chat_OnNewChatMessage_Scripts
    {
        private static void Postfix(long senderID, Talker.Type type, string text) => Events.ChatLine(senderID, type, text);
    }

    // A "/name" that belongs to a script is the script's, not the game's.
    [HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
    internal static class Chat_SendText_Scripts
    {
        private static bool Prefix(string text) => !Events.Command(text);
    }
}
