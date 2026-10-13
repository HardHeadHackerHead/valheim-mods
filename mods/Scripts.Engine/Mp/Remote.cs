using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scripts.Mp
{
    /// <summary>
    /// The server's side of multiplayer (docs/scripting-multiplayer.md sections 4 to 6): the routing every script action that touches another
    /// player goes through, the world-visible things sent to every player's game, the reports and events players' games send back, and the
    /// welcome. The decisions are in RemoteCore (tested without Unity); this is the part that talks to the game. A player on this game (the
    /// host, or single player) never comes through here: the local code runs as it always did.
    /// </summary>
    internal static class Remote
    {
        private sealed class Link : IServerLink
        {
            public bool HasAgent(long peer) => Wire.HasAgent(peer);
            public void Send(long peer, Frame f) => Wire.Send(peer, f);
            public double Now => Time.timeAsDouble;
        }

        internal static readonly RemoteCore Asker = new RemoteCore(new Link());
        internal static readonly StateCache States = new StateCache();
        internal static readonly EventGate Gate = new EventGate();

        private static bool _anyAgentEver;
        private static string _listenKey = "", _limitsKey = "";
        private static float _nextTick, _nextLimits;
        private static string _envName = "", _windText = "";
        private static double _envUntil, _windUntil, _envSeconds, _windSeconds;
        private static readonly HashSet<long> WarnedNoState = new HashSet<long>();

        // ================================================================ who is where

        /// <summary>A player on another game: this game runs the scripts, and the player's own game owns them.</summary>
        internal static bool IsRemote(Player p) => p != null && p != Player.m_localPlayer && ZNet.instance != null && ZNet.instance.IsServer();

        internal static bool AnyAgent => Wire.AgentPeers().Any();

        internal static Player PlayerOf(long peer) => Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetOwner() == peer);

        internal static PlayerState StateOf(Player p) => p == null ? null : States.Get(p.GetOwner());

        private static string NoAgent(string name) => name + "'s game does not have Quad's Blockscript, so this cannot reach them";

        private static void Fail(Script s, string who, string why) => Host.Refuse(s, who + ": " + why);

        // ================================================================ asking a player's game

        /// <summary>What a `done` means for a script: nothing to do when it went well (the audit says so), a refusal naming the player when it did not.</summary>
        private static void Done(Script s, string who, string what, Frame f, string audit)
        {
            if (f == null) { Fail(s, who, "no answer from their game (" + what + ")"); return; }
            if (!f.B("ok")) { Fail(s, who, what + " - " + f.S("why")); return; }
            Audit.Add(s, string.Format(audit, f.N("n")));
        }

        internal static int Give(Script s, Player p, string prefab, int n)
        {
            long peer = p.GetOwner(); string who = Handles.Id(p), name = p.GetPlayerName();
            Outcome o = Asker.Give(peer, prefab, n, Players.HourlyLeft(s, who, "give", 50), k => Players.HourlyUse(s, who, "give", k), (got, why) =>
            {
                if (got > 0) Audit.Add(s, $"gave {got} {prefab} to {name}");
                else Fail(s, name, "no " + prefab + " given - " + why);
            });
            if (!o.Sent) { Fail(s, name, o.Why == RemoteCore.NoAgent ? NoAgent(name) : o.Why); return 0; }
            return (int)o.Value;
        }

        internal static int Take(Script s, Player p, string prefab, int n)
        {
            long peer = p.GetOwner(); string who = Handles.Id(p), name = p.GetPlayerName();
            PlayerState st = States.Get(peer);
            Outcome o = Asker.Take(peer, prefab, n, st != null ? st.Count(prefab) : -1, Players.HourlyLeft(s, who, "take", 20), k => Players.HourlyUse(s, who, "take", k), (got, why) =>
            {
                if (got > 0) Audit.Add(s, $"took {got} {prefab} from {name}");
                else Fail(s, name, "no " + prefab + " taken - " + why);
            });
            if (!o.Sent) { Fail(s, name, o.Why == RemoteCore.NoAgent ? NoAgent(name) : o.Why); return 0; }
            return (int)o.Value;
        }

        /// <summary>How many of an item a player has, from the report their game sent (up to a few seconds old; 0 and one note in the log when none came yet).</summary>
        internal static int Count(Player p, string prefab)
        {
            PlayerState st = States.Get(p.GetOwner());
            if (st == null)
            {
                if (WarnedNoState.Add(p.GetOwner())) Core.Log.LogInfo("Scripts (multiplayer): no report from " + p.GetPlayerName() + "'s game yet, so their bag reads as empty until one comes");
                return 0;
            }
            return st.Count(prefab);
        }

        private static bool Ask(Script s, Player p, string op, JObject args, string what, string audit)
        {
            string name = p.GetPlayerName();
            string why = Asker.Act(p.GetOwner(), op, args, f => Done(s, name, what, f, audit));
            if (why.Length > 0) { Fail(s, name, NoAgent(name)); return false; }
            return true;
        }

        internal static double Heal(Script s, Player p, double n)
        {
            Verdict v = Validate.Heal(n);
            if (!v.Ok) return 0.0;
            return Ask(s, p, "heal", new JObject { ["n"] = v.N }, "heal", "healed " + p.GetPlayerName() + " by {0:0}") ? v.N : 0.0;
        }

        internal static double Hurt(Script s, Player p, double n)
        {
            if (!Players.Rate(s, "hurt-" + Handles.Id(p), 1.0, 1.0)) { Host.Refuse(s, "damage to a player more than once a second"); return 0.0; }
            Verdict v = Validate.Hurt(n, p.GetHealth());
            if (!v.Ok) return 0.0;
            return Ask(s, p, "hurt", new JObject { ["n"] = v.N }, "hurt", "hurt " + p.GetPlayerName() + " by {0:0}") ? v.N : 0.0;
        }

        internal static bool Teleport(Script s, Player p, Vector3 pos)
        {
            if (!Players.Rate(s, "tp-" + Handles.Id(p), 0.1, 1.0)) { Host.Refuse(s, "teleports more than once in ten seconds"); return false; }
            return Ask(s, p, "teleport", new JObject { ["x"] = pos.x, ["z"] = pos.z }, "teleport", "teleported " + p.GetPlayerName() + " to " + pos.x.ToString("0") + "," + pos.z.ToString("0"));
        }

        internal static bool Stamina(Script s, Player p, double value)
        {
            Verdict v = Validate.Stamina(value, 10000.0);
            return v.Ok && Ask(s, p, "stamina", new JObject { ["n"] = v.N }, "stamina", "set " + p.GetPlayerName() + "'s stamina to {0:0}");
        }

        internal static bool Effect(Script s, Player p, string name, double seconds)
        {
            Verdict v = Validate.Effect(name, seconds);
            if (!v.Ok) { Host.Refuse(s, v.Why); return false; }
            int per = Limits.StatusEffectsPerMinute;
            if (!Players.Rate(s, "effects", per / 60.0, per)) { Host.Refuse(s, "too many status effects a minute (" + per + " is the limit; the owner can change it in the Scripts settings)"); return false; }
            return Ask(s, p, "effect+", new JObject { ["name"] = name, ["s"] = v.N }, "status effect", "gave " + name + " to " + p.GetPlayerName() + " for {0:0} s");
        }

        internal static bool RemoveEffect(Script s, Player p, string name)
        {
            Verdict v = Validate.EffectOff(name);
            if (!v.Ok) { Host.Refuse(s, v.Why); return false; }
            return Ask(s, p, "effect-", new JObject { ["name"] = name }, "removing an effect", "took " + name + " off " + p.GetPlayerName());
        }

        internal static bool Launch(Script s, Player p, Vector3 dir, double force)
        {
            if (!Players.Rate(s, "launch", 2.0, 2.0)) { Host.Refuse(s, "shoves more than twice a second"); return false; }
            Verdict v = Validate.Launch(dir.x, dir.y, dir.z, force);
            if (!v.Ok) return false;
            return Ask(s, p, "launch", new JObject { ["dx"] = dir.x, ["dy"] = dir.y, ["dz"] = dir.z, ["f"] = v.N }, "a shove", "shoved " + p.GetPlayerName() + " at {0:0} m/s");
        }

        /// <summary>The big banner on one player's screen: their game shows it (when it allows), else the plain centre message every game understands.</summary>
        internal static bool Banner(Script s, Player p, string text)
        {
            text = Validate.Text(text, 60);
            if (Wire.HasAgent(p.GetOwner())) return Ask(s, p, "banner", new JObject { ["text"] = text }, "a banner", "showed a banner to " + p.GetPlayerName());
            p.Message(MessageHud.MessageType.Center, text);   // (a vanilla game: the game's own message, which reaches them)
            return true;
        }

        // ================================================================ creatures another game owns

        /// <summary>
        /// A creature another game owns (the nearest player's, usually): take it over, and do `act` on the next frame it is ours. If it does
        /// not become ours within a second, say it is busy. True when the attempt was started.
        /// </summary>
        internal static bool OnceOwned(Script s, Character c, string what, Action<Character> act)
        {
            ZNetView view = c != null ? c.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid() || Core.Plugin == null) { Host.Refuse(s, what + " something that cannot be taken over"); return false; }
            view.ClaimOwnership();
            Core.Plugin.StartCoroutine(WaitForOwnership(s, Handles.Id(c), what, act));
            return true;
        }

        private static IEnumerator WaitForOwnership(Script s, string id, string what, Action<Character> act)
        {
            float until = Time.unscaledTime + 1f;
            while (Time.unscaledTime < until)
            {
                yield return null;
                Character c = Handles.Find(id);
                if (c == null) yield break;
                ZNetView view = c.GetComponent<ZNetView>();
                if (view != null && view.IsOwner())
                {
                    try { act(c); } catch (Exception e) { Core.Log.LogWarning("Scripts (multiplayer): " + what + " failed: " + e.Message); }
                    yield break;
                }
            }
            Host.Refuse(s, "that creature is busy (another game keeps it): " + what);
        }

        // ================================================================ what every player's game shows

        private static IEnumerable<long> AgentsNear(Vector3 pos, float range) =>
            Wire.AgentPeers().Where(peer => PlayerOf(peer) is Player pl && Vector3.Distance(pl.transform.position, pos) <= range).ToList();

        private static void Fx(IEnumerable<long> to, JObject args) { if (Wire.IsServer) Asker.Fx(to, args); }

        internal static void FxEnv(string friendly, double seconds)
        {
            _envName = friendly; _envSeconds = seconds; _envUntil = Time.timeAsDouble + seconds;
            Fx(Wire.AgentPeers(), new JObject { ["op"] = "env", ["name"] = friendly, ["s"] = seconds });
        }

        internal static void FxWind(double degrees, double strength, double seconds)
        {
            _windText = degrees.ToString("0.##") + "|" + strength.ToString("0.##"); _windSeconds = seconds; _windUntil = Time.timeAsDouble + seconds;
            Fx(Wire.AgentPeers(), new JObject { ["op"] = "wind", ["deg"] = degrees, ["str"] = strength, ["s"] = seconds });
        }

        internal static void FxEffect(string name, Vector3 pos, double scale, bool sound) =>
            Fx(AgentsNear(pos, 110f), new JObject { ["op"] = sound ? "sound" : "effect", ["name"] = name, ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z, ["sc"] = scale });

        internal static void FxShake(Vector3 pos, double range, double strength) =>
            Fx(AgentsNear(pos, (float)Math.Min(60.0, range) + 10f), new JObject { ["op"] = "shake", ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z, ["r"] = range, ["st"] = strength });

        internal static void FxBanner(string text) => Fx(Wire.AgentPeers(), new JObject { ["op"] = "banner", ["text"] = Validate.Text(text, 60) });

        internal static void FxTalk(Character c, string name, string text) =>
            Fx(AgentsNear(c.transform.position, 60f), new JObject { ["op"] = "talk", ["id"] = Handles.Id(c), ["name"] = Validate.Text(name, 30), ["text"] = Validate.Text(text, 120) });

        internal static void FxName(Character c, string text) => Fx(Wire.AgentPeers(), new JObject { ["op"] = "name", ["id"] = Handles.Id(c), ["text"] = Validate.Text(text, 24) });

        // ================================================================ the welcome, and keeping the players' games up to date

        internal static JObject WelcomeFor(long peer)
        {
            _anyAgentEver = true;
            double now = Time.timeAsDouble;
            var d = new JObject
            {
                ["v"] = Wire.Protocol, ["version"] = Core.Version, ["admin"] = Wire.IsAdmin(peer),
                ["limits"] = Limits.ToJson(), ["lists"] = Lists.ToJson(), ["listen"] = new JArray(ListenNames().Cast<object>().ToArray()),
            };
            if (_envUntil > now) d["env"] = new JObject { ["name"] = _envName, ["s"] = _envUntil - now };
            if (_windUntil > now && _windText.Length > 0) { string[] w = _windText.Split('|'); d["wind"] = new JObject { ["deg"] = double.Parse(w[0]), ["str"] = double.Parse(w[1]), ["s"] = _windUntil - now }; }
            return d;
        }

        /// <summary>The events some running script listens to (the only ones a player's game needs to forward).</summary>
        internal static string[] ListenNames() =>
            Host.Scripts.Where(x => x.State == RunState.Running).SelectMany(x => x.Handlers.Keys).Distinct().Where(EventGate.Names.Contains).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        /// <summary>Every frame on the game that runs the scripts.</summary>
        internal static void Tick()
        {
            Asker.Tick();
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            if (!_anyAgentEver || !AnyAgent) return;
            string listen = string.Join(",", ListenNames());
            if (listen != _listenKey)
            {
                _listenKey = listen;
                Wire.SendToAgents("listen", new JObject { ["events"] = new JArray(ListenNames().Cast<object>().ToArray()) }.ToString(Newtonsoft.Json.Formatting.None));
            }
            if (Time.unscaledTime >= _nextLimits)
            {
                _nextLimits = Time.unscaledTime + 5f;
                JObject limits = Limits.ToJson(), lists = Lists.ToJson();
                string key = limits.ToString(Newtonsoft.Json.Formatting.None) + lists.ToString(Newtonsoft.Json.Formatting.None);
                if (key != _limitsKey)
                {
                    bool first = _limitsKey.Length == 0;
                    _limitsKey = key;
                    if (!first) Wire.SendToAgents("limits", new JObject { ["limits"] = limits, ["lists"] = lists }.ToString(Newtonsoft.Json.Formatting.None));
                }
            }
        }

        internal static void PeerLeft(long peer)
        {
            States.Remove(peer);
            Asker.PeerLeft(peer);
            Gate.PeerLeft(peer);
            WarnedNoState.Remove(peer);
        }

        internal static void Reset()
        {
            _anyAgentEver = false; _listenKey = ""; _limitsKey = ""; _envUntil = _windUntil = 0.0;
            WarnedNoState.Clear();
        }

        // ================================================================ what players' games send

        internal static void OnState(long peer, Frame f) { States.Update(peer, f.D, Time.timeAsDouble); }

        internal static void OnDone(long peer, Frame f) { Asker.OnDone(peer, f); }

        /// <summary>The server's own game saw a death: false when the player's game already told us (the same kill is one event).</summary>
        internal static bool FirstOwn(string name, string id) => !_anyAgentEver || Gate.Own(name, id, Time.timeAsDouble);

        internal static void OnEvent(long peer, Frame f)
        {
            try
            {
                if (!Gate.Accept(peer, f, (name, subject) => Owns(peer, subject), Time.timeAsDouble, out string why)) return;
                Dispatch(f);
            }
            catch (Exception e) { Core.Log.LogWarning("Scripts (multiplayer): an event from a player could not be used: " + e.Message); }
        }

        /// <summary>Does that connection own what the event is about? A player (by id), or a creature (by its object id: its owner, or gone already).</summary>
        private static bool Owns(long peer, string subject)
        {
            if (subject.IndexOf(':') < 0)
            {
                // a player: the connection's own player id (unforgeable, and still there when the player has just died), else the character it owns
                if (subject.Length > 1 && subject[0] == 'p' && long.TryParse(subject.Substring(1), out long pid))
                {
                    ZNetPeer zp = ZNet.instance != null ? ZNet.instance.GetPeer(peer) : null;
                    if (zp != null && zp.m_playerID == pid) return true;
                }
                Player pl = Handles.Find(subject) as Player;
                return pl != null && pl.GetOwner() == peer;
            }
            ZDOID id;
            if (!TryZdoid(subject, out id)) return false;
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(id) : null;
            return zdo == null || zdo.GetOwner() == peer;
        }

        internal static bool TryZdoid(string text, out ZDOID id)
        {
            id = ZDOID.None;
            int colon = text.IndexOf(':');
            if (colon <= 0 || colon > 20 || text.Length > 40) return false;
            if (!long.TryParse(text.Substring(0, colon), out long user) || !uint.TryParse(text.Substring(colon + 1), out uint number)) return false;
            id = new ZDOID(user, number);
            return true;
        }

        internal static Handles.Snap SnapOf(JToken t)
        {
            if (!(t is JObject o) || o.Count == 0) return null;
            string S(string k) => o[k] != null && o[k].Type == JTokenType.String ? Validate.Text((string)o[k], 60) : "";
            double N(string k) => o[k] != null && (o[k].Type == JTokenType.Float || o[k].Type == JTokenType.Integer) ? (double)o[k] : 0.0;
            bool B(string k) => o[k] != null && o[k].Type == JTokenType.Boolean && (bool)o[k];
            string id = S("id");
            if (id.Length == 0) return null;
            return new Handles.Snap
            {
                Id = id, Prefab = S("pf"), Name = S("nm"), SpawnedBy = S("by").Length > 0 ? S("by") : null, Pos = new Vector3((float)N("x"), (float)N("y"), (float)N("z")),
                MaxHp = (float)N("mhp"), Level = (int)N("lv"), IsPlayer = B("pl"), IsBoss = B("bs"), IsTamed = B("tm"),
            };
        }

        private static Vector3 PosOf(Frame f) => new Vector3((float)f.N("x"), (float)f.N("y"), (float)f.N("z"));

        private static NewEvents.ItemInfo ItemOf(Frame f) => new NewEvents.ItemInfo
        {
            Prefab = Validate.Text(f.D["item"] is JObject i && i["pf"] != null ? (string)i["pf"] : "", 60), Name = Validate.Text(f.D["item"] is JObject j && j["nm"] != null ? (string)j["nm"] : "", 60),
            Count = f.D["item"] is JObject k && k["n"] != null ? Math.Max(0, Math.Min(100000, (int)k["n"])) : 0, Quality = f.D["item"] is JObject q && q["q"] != null ? Math.Max(0, Math.Min(10, (int)q["q"])) : 1,
        };

        /// <summary>A forwarded event, fired for the scripts exactly as if this game had seen it itself.</summary>
        private static void Dispatch(Frame f)
        {
            string name = f.S("e"), subject = f.S("sub");
            Player player = subject.IndexOf(':') < 0 ? Handles.Find(subject) as Player : null;
            switch (name)
            {
                case "kill":
                    {
                        Handles.Snap v = SnapOf(f.D["v"]), k = SnapOf(f.D["k"]);
                        if (v == null) return;
                        Events.FireDeath(v, k, PosOf(f), f.B("boss"), f.S("cause", "other"));
                        return;
                    }
                case "playerRespawn": if (player != null) NewEvents.FireRespawn(player); return;
                case "itemPickup": if (player != null) NewEvents.FirePickup(player, ItemOf(f)); return;
                case "eat": if (player != null) NewEvents.FireEat(player, ItemOf(f)); return;
                case "skillUp": if (player != null) NewEvents.FireSkill(player, Validate.Text(f.S("skill"), 30), (int)f.N("level")); return;
                case "pieceBuilt":
                    {
                        if (player == null || !TryZdoid(f.S("piece"), out _)) return;
                        NewEvents.FirePiece(player, f.S("piece"));
                        return;
                    }
                case "tamed":
                    {
                        Character creature = Handles.Find(subject);
                        Player tamer = Handles.Find(f.S("by")) as Player;
                        if (creature != null && tamer != null) NewEvents.FireTamed(creature, tamer);
                        return;
                    }
                case "hit":
                    {
                        // told, not asked: the damage was settled on the owner's game, so a script's change to it is ignored
                        Handles.Snap target = SnapOf(f.D["t"]), attacker = SnapOf(f.D["a"]);
                        if (target != null) NewEvents.FireHit(target, attacker, PosOf(f), Validate.Text(f.S("weapon"), 60), f.N("damage"));
                        return;
                    }
            }
        }
    }
}
