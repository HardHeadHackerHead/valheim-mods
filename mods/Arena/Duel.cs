using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// A duel between two players, with a wager. One challenges another at the arena; the other accepts in the menu; both pay the wager and the
    /// gate opens. Each, walking into the ring, is taken to their own end of it (the challenger and the challenged facing each other across
    /// the floor) and held there until both are in; a countdown runs, and at FIGHT! both are put in PvP (kept on for the fight: it cannot be
    /// switched off to dodge blows), set back as it was after. Whoever drops to a fifth of their health loses (nobody can die in a duel: the
    /// health patch holds them there) and tells the other, who takes both wagers less a tenth to the arena. Leaving the ring, or the game, forfeits.
    /// Each game does its own part (its own wager, its own health, its own winnings) and tells the other by routed RPC, so it needs the mod on both.
    /// </summary>
    internal static class Duel
    {
        private const string RpcChallenge = "DHArenaChallenge", RpcAnswer = "DHArenaAnswer", RpcEnd = "DHArenaEnd";
        private enum PhaseKind { None, Walking, Countdown, Fighting }

        private static PhaseKind _phase;
        private static long _opponent;
        private static string _opponentName = "";
        private static int _wager, _count;
        private static Vector3 _at;
        private static float _timer, _outside, _missing, _closeAt;
        private static bool _prevPvp, _ending, _placed, _pvpSet;
        private const float Apart = 8f;   // (each stands this far from the middle, on their own side)

        private static long _inFrom, _outTo;
        private static string _inName = "";
        private static int _inWager, _outWager;
        private static Vector3 _inAt, _outAt;
        private static float _inUntil, _outUntil;

        private static ZRoutedRpc _registeredOn;
        private static int _awakeFrame = -100;

        internal static bool Active => _phase != PhaseKind.None;
        internal static bool Fighting => _phase == PhaseKind.Fighting;
        internal static string OpponentName => _opponentName;
        internal static int Wager => _wager;
        internal static Vector3 Centre => Site.Centre;
        internal static Player Opponent => Player.GetAllPlayers().FirstOrDefault(p => p != null && p.GetOwner() == _opponent);
        internal static bool HasIncoming => _inFrom != 0L && Time.unscaledTime < _inUntil;
        internal static string IncomingName => _inName;
        internal static int IncomingWager => _inWager;

        internal static void Init() => _awakeFrame = Time.frameCount;

        // ---- the network -----------------------------------------------------------------------------------------------

        private static long MyId => ZDOMan.GetSessionID();

        private static void Register()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (Time.frameCount <= _awakeFrame + 2 || rpc == _registeredOn) return;
            _registeredOn = rpc;
            Unregister();   // (the game throws if a name is registered twice)
            rpc.Register<ZPackage>(RpcChallenge, OnChallenge);
            rpc.Register<ZPackage>(RpcAnswer, OnAnswer);
            rpc.Register<ZPackage>(RpcEnd, OnEnd);
        }

        internal static void Unregister()
        {
            if (ZRoutedRpc.instance == null) return;
            var table = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(ZRoutedRpc.instance) as IDictionary;
            if (table == null) return;
            foreach (string name in new[] { RpcChallenge, RpcAnswer, RpcEnd }) table.Remove(name.GetStableHashCode());
        }

        private static void Send(long to, string rpc, ZPackage pkg)
        {
            if (ZRoutedRpc.instance == null) return;
            try { ZRoutedRpc.instance.InvokeRoutedRPC(to, rpc, pkg); }
            catch (Exception e) { Plugin.Log.LogWarning("Arena duel message failed: " + e.Message); }
        }

        private static string NameOf(long owner)
        {
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x != null && x.GetOwner() == owner);
            return p != null ? p.GetPlayerName() : "your opponent";
        }

        // ---- challenging --------------------------------------------------------------------------------------------------

        /// <summary>Asks another player to a duel. Returns why not, or null when the challenge is sent.</summary>
        internal static string Challenge(Player other, int wager, Vector3 at)
        {
            Player me = Player.m_localPlayer;
            if (me == null || other == null) return "Not now.";
            if (Active || Contest.Active) return "You are already in a fight.";
            if (me.GetInventory().CountItems("$item_coins") < wager) return "You have not got the coins for that wager.";
            _outTo = other.GetOwner(); _outWager = wager; _outAt = at; _outUntil = Time.unscaledTime + 30f;
            var pkg = new ZPackage();
            pkg.Write(wager); pkg.Write(me.GetPlayerName()); pkg.Write(at);
            Send(_outTo, RpcChallenge, pkg);
            return null;
        }

        private static void OnChallenge(long sender, ZPackage pkg)
        {
            int wager = pkg.ReadInt(); string name = pkg.ReadString(); Vector3 at = pkg.ReadVector3();
            if (Player.m_localPlayer == null) return;
            if (Active || Contest.Active || HasIncoming) { Reply(sender, false); return; }
            _inFrom = sender; _inName = name; _inWager = wager; _inAt = at; _inUntil = Time.unscaledTime + 30f;
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, name + " challenges you to a duel!");
            Window.Open(Window.Context.Master, null, 1);
        }

        private static void Reply(long to, bool accept)
        {
            var pkg = new ZPackage();
            pkg.Write(accept);
            Send(to, RpcAnswer, pkg);
        }

        internal static string Accept()
        {
            Player me = Player.m_localPlayer;
            if (me == null || !HasIncoming) return "That challenge has run out.";
            if (Active || Contest.Active) return "You are already in a fight.";
            if (me.GetInventory().CountItems("$item_coins") < _inWager) { Decline(); return "You have not got the coins for that wager."; }
            if (Travel.Distance(me.transform.position) > 90f) return "Come to the arena first.";
            long from = _inFrom; string name = _inName; int wager = _inWager; Vector3 at = _inAt;
            _inFrom = 0L;
            Reply(from, true);
            Begin(from, name, wager, at);
            Window.Close();
            return null;
        }

        internal static void Decline()
        {
            if (_inFrom != 0L) Reply(_inFrom, false);
            _inFrom = 0L;
        }

        private static void OnAnswer(long sender, ZPackage pkg)
        {
            bool accepted = pkg.ReadBool();
            Player me = Player.m_localPlayer;
            if (me == null || sender != _outTo || Time.unscaledTime > _outUntil) return;
            _outTo = 0L;
            if (!accepted) { Hud.Say(NameOf(sender) + " did not take the challenge."); return; }
            if (Active || Contest.Active || me.GetInventory().CountItems("$item_coins") < _outWager) { End(sender, false, "cancel"); return; }
            Begin(sender, NameOf(sender), _outWager, _outAt);
        }

        // ---- the fight -------------------------------------------------------------------------------------------------------

        private static void Begin(long opponent, string name, int wager, Vector3 at)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return;
            if (wager > 0) me.GetInventory().RemoveItem("$item_coins", wager);
            _opponent = opponent; _opponentName = name; _wager = wager; _at = at;
            _phase = PhaseKind.Walking; _timer = 90f; _count = 6; _outside = 0f; _missing = 0f; _ending = false; _closeAt = 0f; _placed = false;
            me.Heal(me.GetMaxHealth(), true);
            Crowd.Open();
            Crowd.Gong();
            Scenery.OpenGate(Scenery.MainGrate, 90f);
            Net.Door(90f);   // (for the watchers too)
            Hud.Shout("DUEL!", "You against " + name + (wager > 0 ? "   -   " + wager + " coins each" : "") + ". Walk into the ring: you will be taken to your side.", 5f);
        }

        internal static void Tick(float dt)
        {
            Register();
            if (_closeAt > 0f && Time.time >= _closeAt) { _closeAt = 0f; if (!Contest.Active) Crowd.Close(true); }
            if (!Active) return;
            Player me = Player.m_localPlayer;
            if (me == null) return;

            if (_phase == PhaseKind.Walking)
            {
                // each walks in through the grate and is taken to their side; the countdown starts when both are in
                _timer -= dt;
                Player them = Opponent;
                if (!_placed && Site.OnFloor(me.transform.position, -1f))
                {
                    _placed = true;
                    Place(me);
                    Hud.Say("Wait for " + _opponentName + " to come in");
                }
                if (_placed) Hold(me);
                if (_placed && them != null && Site.OnFloor(them.transform.position, -1f))
                {
                    Scenery.CloseGate(Scenery.MainGrate);
                    Net.Door(0f);
                    _phase = PhaseKind.Countdown; _timer = 5.5f; _count = 6;
                    Hud.Shout("DUEL!", "You against " + _opponentName + (_wager > 0 ? "   -   " + _wager * 2 + " coins in the pot" : ""), 3.5f);
                    Crowd.Gong();
                }
                else if (_timer <= 0f)
                {
                    if (_wager > 0) Contest.Give("Coins", _wager, me);
                    End(_opponent, false, "cancel");
                    Finish(null, "Not both in the ring in time: the duel is off, and your wager is back.");
                }
                return;
            }

            if (_phase == PhaseKind.Countdown)
            {
                // held at your mark while it counts down (no blows yet: PvP is not on until FIGHT!)
                Hold(me);
                _timer -= dt;
                int n = Mathf.CeilToInt(_timer);
                if (n < _count && n >= 1 && n <= 5) { _count = n; Hud.Count(n.ToString()); }
                if (_timer <= 0f)
                {
                    _prevPvp = me.IsPVPEnabled();
                    _pvpSet = true;
                    me.SetPVP(true);
                    _phase = PhaseKind.Fighting;
                    Hud.Count("FIGHT!");
                    Crowd.Cheer();
                }
                return;
            }

            // PvP stays on for the fight (switched off in the inventory, it is put straight back)
            if (!me.IsPVPEnabled()) { me.SetPVP(true); Hud.Say("PvP stays on until the duel is over"); }

            if (!Player.GetAllPlayers().Any(p => p != null && p.GetOwner() == _opponent)) _missing += dt; else _missing = 0f;
            if (_missing > 8f) { Finish(true, "Your opponent left."); return; }
            if (!Site.OnFloor(me.transform.position, 1.5f))
            {
                _outside += dt;
                if (_outside > 6f) { End(_opponent, true, "forfeit"); Finish(false, "You left the ring."); }
            }
            else _outside = Mathf.Max(0f, _outside - dt);
        }

        /// <summary>My mark: the challenger at the west end of the floor, the challenged at the east (by session id: both games agree).</summary>
        private static Vector3 Mark(out Quaternion facing)
        {
            float side = MyId < _opponent ? -1f : 1f;
            float x = side * Apart;
            facing = Site.Turn * Quaternion.LookRotation(new Vector3(-side, 0f, 0f));
            return Site.World(new Vector3(x, Layout.FloorHeight(x, 0f) + 0.15f, 0f));
        }

        /// <summary>Taken to your side of the ring, facing your opponent's.</summary>
        private static void Place(Player me)
        {
            Vector3 to = Mark(out Quaternion facing);
            to.y = Site.Ground(to, to.y) + 0.15f;
            me.transform.SetPositionAndRotation(to, facing);
            Rigidbody body = me.GetComponent<Rigidbody>();
            if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
            me.SetLookDir(facing * Vector3.forward);
            Fx.SpawnPuff(to);
            Crowd.Cheer();
        }

        /// <summary>Kept on your mark until FIGHT! (wander more than a step off it and you are put back).</summary>
        private static void Hold(Player me)
        {
            Vector3 mark = Mark(out _);
            Vector3 off = me.transform.position - mark; off.y = 0f;
            if (off.magnitude > 1.5f) Place(me);
        }

        /// <summary>The local player's health fell to a fifth in a duel (the health patch calls this): they have lost.</summary>
        internal static void Lose()
        {
            if (_phase != PhaseKind.Fighting || _ending) return;
            _ending = true;
            End(_opponent, true, "beaten");
            Finish(false, "You were beaten.");
        }

        private static void End(long to, bool theyWin, string reason)
        {
            var pkg = new ZPackage();
            pkg.Write(theyWin); pkg.Write(reason);
            Send(to, RpcEnd, pkg);
        }

        private static void OnEnd(long sender, ZPackage pkg)
        {
            bool iWin = pkg.ReadBool(); string reason = pkg.ReadString();
            if (!Active || sender != _opponent) return;
            if (reason == "cancel")
            {
                Player me = Player.m_localPlayer;
                if (me != null && _wager > 0) Contest.Give("Coins", _wager, me);   // (their game could not take its half: yours comes back)
                Finish(null, "The duel was called off.");
                return;
            }
            _ending = true;
            Finish(iWin, iWin ? (reason == "forfeit" ? _opponentName + " left the ring." : _opponentName + " is beaten.") : "You were beaten.");
        }

        private static void Finish(bool? win, string why)
        {
            Player me = Player.m_localPlayer;
            bool fought = _phase == PhaseKind.Fighting;
            _phase = PhaseKind.None;
            Scenery.OpenGate(Scenery.MainGrate, 30f);
            Net.Door(45f);
            Guard.Grace(60f);
            if (me != null)
            {
                if (_pvpSet) me.SetPVP(_prevPvp);
                _pvpSet = false;
                me.Heal(me.GetMaxHealth(), true);
                if (win == true)
                {
                    int pot = _wager * 2, cut = pot / 10;
                    if (pot - cut > 0) Contest.Give("Coins", pot - cut, me);
                    Ladder.Add("duelwins", 1);
                    Hud.Shout("YOU WIN!", why + (pot > 0 ? "   -   " + (pot - cut) + " coins" : ""), 5f);
                    Crowd.Roar(); Crowd.Horn();
                }
                else if (win == false)
                {
                    Ladder.Add("duellosses", 1);
                    Hud.Shout("DEFEATED", why, 4f);
                    Crowd.Boo();
                }
                else Hud.Shout("DUEL OFF", why, 3f);
            }
            _closeAt = Time.time + 6f;
        }

        /// <summary>Ends a duel without a result (the mod unloaded, the player left): the other is told they won, and the wager is not lost to nobody.</summary>
        internal static void Abort(bool tell)
        {
            if (!Active) return;
            Player me = Player.m_localPlayer;
            if (tell) End(_opponent, true, "forfeit");
            if (me != null && _pvpSet) me.SetPVP(_prevPvp);
            _pvpSet = false;
            _phase = PhaseKind.None;
        }
    }
}
