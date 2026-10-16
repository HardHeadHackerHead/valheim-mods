using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Arena
{
    /// <summary>
    /// What players at the arena tell each other. The game running a fight says so every couple of seconds (who, which contest, the round,
    /// the crowd; a duel too), so the Arena Master will not start a second one, and everyone near the arena sees and hears it: the announcer's calls, the
    /// crowd's roars, the grates rising, the cover on the floor. Each game draws its own arena, so these keep them showing the same fight.
    /// </summary>
    internal static class Net
    {
        private const string RpcState = "DHArenaState", RpcEvent = "DHArenaEvent";
        private const float Near = 140f;

        private static ZRoutedRpc _registeredOn;
        private static int _awakeFrame = -100;
        private static float _nextState, _remoteAt;
        private static long _remoteFrom;

        // what another player's fight looks like (for the spectators); its title as Words make it
        internal static string RemoteName = "", RemoteTitle = "";
        internal static string RemoteKind = "";   // (road, champion, endless or trial; "" for a duel, or from an Arena before 0.4.4)
        internal static int RemoteRound, RemoteRounds, RemoteFoes;
        internal static float RemoteFavour;

        internal static void Init() => _awakeFrame = Time.frameCount;

        /// <summary>Someone else is fighting in the arena (heard from in the last few seconds).</summary>
        internal static bool RemoteFight => Time.time - _remoteAt < 6f && _remoteFrom != 0L;
        internal static bool Busy => Contest.Active || Duel.Active || RemoteFight;
        internal static string BusyText => Contest.Active ? T("arena_busy_you") : RemoteFight ? T("arena_busy_remote", RemoteName, Words.Show(RemoteTitle)) : T("arena_busy_duel");

        private static bool NearArena(Vector3 p) => Site.Known && new Vector2(p.x - Site.Origin.x, p.z - Site.Origin.z).magnitude < Near;
        private static bool MeNear => Player.m_localPlayer != null && NearArena(Player.m_localPlayer.transform.position);
        /// <summary>This player is near enough the arena to see and hear its fights.</summary>
        internal static bool InRange => MeNear;

        internal static void Tick()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount > _awakeFrame + 2 && rpc != _registeredOn)
            {
                _registeredOn = rpc;
                Unregister();
                rpc.Register<ZPackage>(RpcState, OnState);
                rpc.Register<ZPackage>(RpcEvent, OnEvent);
            }
            if ((Contest.Active || Duel.Active) && Time.time > _nextState)
            {
                // (a duel says so too: no contest or other duel may start in the ring while it is on)
                _nextState = Time.time + 2f;
                var pkg = new ZPackage();
                // (the title in Words, after the rest: an older Arena reads as far as the favour, and finds no title in the old place)
                pkg.Write(Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
                pkg.Write("");
                if (Contest.Active) { pkg.Write(Contest.Round); pkg.Write(Contest.Rounds); pkg.Write(Contest.FoesLeft); }
                else { pkg.Write(0); pkg.Write(0); pkg.Write(0); }
                pkg.Write(Crowd.Favour);
                pkg.Write(Contest.Active ? Contest.TitleLine : Words.Line("arena_title_duel", Duel.OpponentName));
                pkg.Write(Contest.Active ? Contest.KindWord(Contest.Kind) : "");   // (after the title: an older Arena stops reading there)
                Send(RpcState, pkg);
            }
            // a fight we were watching has ended: the crowd goes home
            if (!Contest.Active && !Duel.Active && _watching && !RemoteFight) { _watching = false; Crowd.Close(true); }
        }

        private static bool _watching;

        internal static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            if (table == null) return;
            foreach (string name in new[] { RpcState, RpcEvent }) table.Remove(name.GetStableHashCode());
        }

        private static void Send(string rpc, ZPackage pkg)
        {
            if (ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, rpc, pkg); }
            catch (Exception e) { Plugin.Log.LogWarning("Arena message failed: " + e.Message); }
        }

        private static void OnState(long sender, ZPackage pkg)
        {
            if (sender == ZDOMan.GetSessionID() || Duel.Involves(sender)) return;
            RemoteName = pkg.ReadString(); RemoteTitle = pkg.ReadString(); RemoteRound = pkg.ReadInt(); RemoteRounds = pkg.ReadInt(); RemoteFoes = pkg.ReadInt(); RemoteFavour = pkg.ReadSingle();
            if (pkg.GetPos() < pkg.Size()) RemoteTitle = pkg.ReadString();   // (from an older Arena: its English title, as it is)
            RemoteKind = pkg.GetPos() < pkg.Size() ? pkg.ReadString() : "";
            _remoteFrom = sender; _remoteAt = Time.time;
            if (MeNear && !Contest.Active && !Duel.Active)
            {
                if (!Crowd.IsOpen) { Crowd.Open(); _watching = true; }
                Crowd.Set(RemoteFavour);
            }
        }

        // ---- events: a call, a sound, a grate, the cover ---------------------------------------------------------------------

        // (Shout: a call in words, from an older Arena; Say: a call in Words, each game showing it in its player's language. An older Arena
        // ignores a kind it does not know.)
        internal enum Kind { Shout = 1, Sound = 2, Gate = 3, Props = 4, Fireworks = 5, Celebrate = 6, Door = 7, Effect = 8, Say = 9, Result = 10 }

        /// <summary>A contest has ended: every game hears it (for other mods, through Api), wherever its player is.</summary>
        internal static void Result(string fighter, string data)
        {
            Api.Heard(fighter, data);
            var pkg = new ZPackage();
            pkg.Write((int)Kind.Result); pkg.Write(data ?? ""); pkg.Write(fighter ?? ""); pkg.Write(0f);
            Send(RpcEvent, pkg);
        }

        /// <summary>
        /// Does it here and tells the players near the arena. A call can say something else to those watching (watchText and watchSub: the
        /// fighter is told to pick up their coins, the stands that a round is won); a null watchText keeps it from them altogether.
        /// </summary>
        internal static void Event(Kind kind, string a, string b = "", float f = 0f, bool forWatchers = false, string watchText = null, string watchSub = null)
        {
            Apply(kind, a, b, f, true);
            var pkg = new ZPackage();
            pkg.Write((int)kind); pkg.Write(a ?? ""); pkg.Write(b ?? ""); pkg.Write(f);
            // (after the rest: an older Arena stops reading before it, and shows the fighter's words)
            pkg.Write(forWatchers);
            if (forWatchers) { pkg.Write(watchText != null); pkg.Write(watchText ?? ""); pkg.Write(watchSub ?? ""); }
            Send(RpcEvent, pkg);
        }

        private static void OnEvent(long sender, ZPackage pkg)
        {
            if (sender == ZDOMan.GetSessionID()) return;
            var kind = (Kind)pkg.ReadInt(); string a = pkg.ReadString(), b = pkg.ReadString(); float f = pkg.ReadSingle();
            if (kind == Kind.Result) { Api.Heard(b, a); return; }   // (for every game, near the arena or not: Api)
            if (!MeNear) return;
            if (pkg.GetPos() < pkg.Size() && pkg.ReadBool())
            {
                bool shown = pkg.ReadBool(); string wa = pkg.ReadString(), wb = pkg.ReadString();
                if (!shown) return;   // (for the fighter alone)
                a = wa; b = wb;
            }
            Apply(kind, a, b, f, false);
        }

        private static void Apply(Kind kind, string a, string b, float f, bool mine)
        {
            switch (kind)
            {
                case Kind.Shout: Hud.Shout(a, b, f > 0f ? f : 4f); break;
                case Kind.Say: Hud.Shout(Words.Show(a), Words.ShowStacked(b), f > 0f ? f : 4f); break;
                case Kind.Sound: Crowd.Play(a); break;
                case Kind.Gate: if (int.TryParse(a, out int g)) { if (f > 0f) Scenery.OpenGate(g, f); else Scenery.CloseGate(g); } break;
                case Kind.Props: if (string.IsNullOrEmpty(a)) Scenery.ClearProps(); else Scenery.ShowProps(a); break;
                case Kind.Fireworks: if (int.TryParse(a, out int n)) Show.Fireworks(n, f); break;
                case Kind.Celebrate: Crowd.Celebrate(); break;
                case Kind.Door: Scenery.OpenDoor(f); break;
                case Kind.Effect:
                    {
                        // a show effect (fire, sparks: none of the game's harmful ones), by prefab name, at a point: only those the arena uses
                        // (another game could otherwise make anything appear here, a creature or a bomb)
                        if (!ShowEffects.Contains(a)) break;
                        string[] v = b.Split(';');
                        GameObject fx = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(a) : null;
                        if (fx != null && v.Length == 3 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x)
                            && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y)
                            && float.TryParse(v[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                            UnityEngine.Object.Instantiate(fx, new Vector3(x, y, z), Quaternion.identity);
                        break;
                    }
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> ShowEffects = new System.Collections.Generic.HashSet<string> { "fx_fireball_staff_explosion", "fx_fireskeleton_nova" };

        /// <summary>The announcer, to everyone near the arena: the call and the line under it as Words (Words.Line, Words.Then).</summary>
        internal static void Shout(string text, string sub = "", float seconds = 4f) => Event(Kind.Say, text, sub, seconds);

        /// <summary>
        /// The announcer, with other words for those watching (the stands, the forecourt) than for the fighter: watchText null keeps the call
        /// from them altogether, watchSub "" gives them the call alone.
        /// </summary>
        internal static void Shout(string text, string sub, float seconds, string watchText, string watchSub) => Event(Kind.Say, text, sub, seconds, true, watchText, watchSub);
        internal static void Sound(string name) => Event(Kind.Sound, name);
        internal static void Gate(int index, float seconds) => Event(Kind.Gate, index.ToString(), "", seconds);
        internal static void Props(string set) => Event(Kind.Props, set ?? "");
        internal static void Fireworks(int count, float over) => Event(Kind.Fireworks, count.ToString(), "", over);
        internal static void Celebrate() => Event(Kind.Celebrate, "");
        internal static void Door(float seconds) => Event(Kind.Door, "", "", seconds);
        internal static void Effect(string prefab, Vector3 at) => Event(Kind.Effect, prefab, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0};{1};{2}", at.x, at.y, at.z));
    }
}
