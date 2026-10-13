using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// One player's gun in the hand, as the posing code (Aim.PoseOn) needs it: where they look, whether they aim, sprint or reload, the
    /// gun's kick, and the eased amounts of the carry. The local player's is filled from the game's own state (Aim.Pose); every other
    /// player's from what their game shares (Share.Late).
    /// </summary>
    internal sealed class Carry
    {
        public GunDef Def;
        public Vector3 Look;
        public bool Aiming, Sprinting, Arms = true;
        public float Dip, Recoil, LastShot = -10f, SpinAngle;
        public bool Second;                 // the last shot was the second (left) pistol's
        public float AimT, SprintT, Run;    // eased amounts of the carry
        public Transform Model;
        // others only: what their game's shots and reloads add up to here
        public float Spin, BurstUntil, NextFlash, CasingAt, CycleAt;
        public bool CasingSecond, Reloading, DiveLock;
        public float ReloadAt, ReloadLen = 1f;
        public readonly DiveBody Dive = new DiveBody();
    }

    /// <summary>
    /// What the other players with the mod need to see our guns (the game shares where we stand and what we wear, but not how we hold
    /// it): one number on our own player's ZDO (aiming, sprinting, reloading, a dive and which, whether our arms are posed) with the pitch
    /// of our aim and the length of a reload beside it, and a routed "shot" call to everybody so the sound, flash, brass and kick happen on
    /// their screens too. Their games pose our arms, our gun's moving parts and our dive from that (Late), each as ours are posed here.
    /// Written only when something changes. The state is "none" when nothing is held, never removed (a ZDO key removed here is never
    /// removed on the other games: modding-pitfalls, "A removed ZDO key stays on every other game"). Players without the mod see the game's
    /// own crossbow carry, and hear and see nothing of the shots.
    /// </summary>
    internal static class Share
    {
        private const int Version = 1;   // the state is Version * 256 + flags + dive * 16 (an unknown version is not posed)
        private const int FAim = 1, FSprint = 2, FReload = 4, FArms = 8, None = Version * 256;
        private const string ShotRpc = "dh_guns_shot";
        private const float PoseRange = 60f, ShotRange = 120f;

        private static readonly int StateKey = "dh_guns_st".GetStableHashCode(), PitchKey = "dh_guns_pitch".GetStableHashCode(), ReloadKey = "dh_guns_rl".GetStableHashCode();
        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        private static ZDO _zdo;
        private static int _state = -1;
        private static float _pitch, _nextPitch, _nextBurst, _nextSweep;
        private static readonly Dictionary<Player, Carry> Others = new Dictionary<Player, Carry>();
        private static readonly List<Player> Gone = new List<Player>();

        internal static ZDO ZdoOf(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        // ---- ours, out ----------------------------------------------------------------------------------------------------

        /// <summary>Every frame on the local player's game: the state as it is now, written when it changed.</summary>
        internal static void Publish()
        {
            Player p = Player.m_localPlayer;
            ZDO zdo = ZdoOf(p);
            if (zdo == null || !zdo.IsOwner()) return;
            if (zdo != _zdo) { _zdo = zdo; _state = -1; _pitch = 999f; }   // (a new character's ZDO: written afresh)
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            int state = None;
            float pitch = 0f;
            if (d != null && !p.IsDead())
            {
                if (Aim.Active || Dive.Active) state |= FAim;
                if (p.IsRunning()) state |= FSprint;
                if (Ammo.Reloading) state |= FReload;
                if (Plugin.ArmPosing.Value) state |= FArms;
                state |= Dive.Shared << 4;
                pitch = Mathf.Asin(Mathf.Clamp(p.GetLookDir().y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            if (state != _state)
            {
                if ((state & FReload) != 0 && (_state & FReload) == 0 && d != null) zdo.Set(ReloadKey, d.ReloadNow);   // (before the state: the others read it when they see the flag)
                zdo.Set(StateKey, state);
                _state = state;
            }
            if (d != null && Mathf.Abs(pitch - _pitch) > 2f && Time.time >= _nextPitch)
            {
                _nextPitch = Time.time + 0.2f;
                _pitch = pitch;
                zdo.Set(PitchKey, pitch);
            }
        }

        /// <summary>No gun on the local player's ZDO (the mod unloads, or the player is gone).</summary>
        internal static void Clear()
        {
            ZDO zdo = ZdoOf(Player.m_localPlayer);
            if (zdo != null && zdo.IsOwner() && zdo.GetInt(StateKey, None) != None) zdo.Set(StateKey, None);
            _zdo = null;
            _state = -1;
        }

        /// <summary>A shot on the local player's game: everybody's game plays its sound, flash and brass. A fast gun sends a tick a few times a second instead.</summary>
        internal static void SendShot(Player p, GunDef d, bool second)
        {
            ZNetView nview = NView(p);
            if (nview == null || !nview.IsValid()) return;
            int kind = 0;
            if (d.Stream)
            {
                if (Time.time < _nextBurst) return;
                _nextBurst = Time.time + 0.2f;
                kind = 1;
            }
            nview.InvokeRPC(ZNetView.Everybody, ShotRpc, kind, second);
        }

        // ---- the call ------------------------------------------------------------------------------------------------------

        /// <summary>Takes the shot call on this player (every player's own ZNetView answers for its player).</summary>
        internal static void Register(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            if (nview == null || !nview.IsValid()) return;
            nview.Unregister(ShotRpc);   // (a hot reload: the old copy's is still there, and Register would throw)
            nview.Register<int, bool>(ShotRpc, (sender, kind, second) => OnShot(p, nview, kind, second));
        }

        internal static void Unregister(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            if (nview != null && nview.IsValid()) nview.Unregister(ShotRpc);
        }

        private static Carry CarryOf(Player p)
        {
            if (!Others.TryGetValue(p, out Carry c)) Others[p] = c = new Carry();
            return c;
        }

        private static void OnShot(Player p, ZNetView nview, int kind, bool second)
        {
            if (p == null || p == Player.m_localPlayer || nview.IsOwner()) return;   // (our own shot: already played where we fired it)
            Player me = Player.m_localPlayer;
            if (me != null && (me.transform.position - p.transform.position).sqrMagnitude > ShotRange * ShotRange) return;
            GunDef d = Arsenal.HeldBy(p);
            if (d == null) return;
            Carry c = CarryOf(p);
            c.Def = d;
            float now = Time.time;
            if (kind == 1) { c.BurstUntil = now + 0.45f; return; }   // (a burst: Late flashes and kicks at the gun's rate while ticks keep coming)
            c.LastShot = now;
            c.Second = second;
            Kick(c, d);
            Fx.Shot(p, d, second);
            if (d.CycleClick.HasValue) c.CycleAt = now + d.Cycle * 0.3f;
            if (d.Cycle >= 0.4f) { c.CasingAt = now + d.Cycle * 0.4f; c.CasingSecond = second; }   // (worked by hand: the brass comes out as it is worked)
            else Fx.Casing(p, d, second);
        }

        private static void Kick(Carry c, GunDef d)
        {
            if (d.Kick > 0f) c.Recoil = Mathf.Min(1.5f, c.Recoil + Mathf.Clamp(d.Kick, 0.4f, 2f) * 0.6f);   // (a flamethrower doesn't kick)
        }

        // ---- theirs, in ----------------------------------------------------------------------------------------------------

        /// <summary>Every frame after the animation: every other player near by with a gun is dived, kicked and posed from what their game shares.</summary>
        internal static void Late()
        {
            Player me = Player.m_localPlayer;
            Vector3 eye = me != null ? me.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players[i];
                if (p == null || p == me) continue;
                Carry c = null;
                GunDef d = (p.transform.position - eye).sqrMagnitude > PoseRange * PoseRange || p.IsDead() ? null : Arsenal.HeldBy(p);
                if (d != null) c = CarryOf(p);
                else if (!Others.TryGetValue(p, out c)) continue;
                if (d == null) { Release(p, c); continue; }
                ZDO zdo = ZdoOf(p);
                int state = zdo != null ? zdo.GetInt(StateKey, 0) : 0;
                bool shared = state / 256 == Version;
                float dt = Time.deltaTime;

                c.Def = d;
                c.Aiming = shared && (state & FAim) != 0;
                c.Sprinting = shared && (state & FSprint) != 0;
                c.Arms = !shared || (state & FArms) != 0;
                bool reload = shared && (state & FReload) != 0;
                if (reload)
                {
                    if (!c.Reloading) { c.Reloading = true; c.ReloadAt = Time.time; c.ReloadLen = Mathf.Max(0.1f, zdo.GetFloat(ReloadKey, 1f)); }
                    float u = (Time.time - c.ReloadAt) / c.ReloadLen;
                    if (d.PerRound) u -= Mathf.Floor(u);   // (a round at a time: the dip repeats)
                    c.Dip = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
                }
                else { c.Reloading = false; c.Dip = 0f; }

                // where they look: their body faces it (the game turns a gun's holder to the camera: Aim.cs), the pitch they share
                Vector3 flat = Vector3.ProjectOnPlane(p.transform.forward, Vector3.up).normalized;
                float pitch = (shared && zdo != null ? zdo.GetFloat(PitchKey, 0f) : 0f) * Mathf.Deg2Rad;
                c.Look = flat * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);

                // a dive: begun when they begin, over after its length (their games' own visuals are tilted, as ours are)
                int dive = shared ? (state >> 4) & 7 : 0;
                if (dive > 0) { if (!c.Dive.On && !c.DiveLock) Dive.Begin(p, c.Dive, dive - 1); }
                else c.DiveLock = false;
                if (c.Dive.On && (dive == 0 || Time.time - c.Dive.Start >= Dive.Duration)) { Dive.End(c.Dive); if (dive > 0) c.DiveLock = true; }
                c.Aiming |= c.Dive.On;

                // the shots: the kick dies away, a burst of fire flashes and kicks at the gun's rate, the barrels of a rotary gun turn
                c.Recoil = Mathf.Max(0f, c.Recoil - dt * 7f);
                bool burst = Time.time < c.BurstUntil;
                if (burst && Time.time >= c.NextFlash)
                {
                    c.NextFlash = Time.time + 1f / Mathf.Max(1f, d.RateNow);
                    c.LastShot = Time.time;
                    Kick(c, d);
                    Fx.Shot(p, d, false);
                    Fx.Casing(p, d, false);
                }
                if (Fx.HasBurst(d)) Fx.Fire(p, d, burst);
                if (d.Flame) Flame.Stream(p, burst);
                if (d.SpinUp > 0f)
                {
                    c.Spin = Mathf.Clamp01(c.Spin + (burst ? 1f / d.SpinUp : -1f / (d.SpinUp * 2.4f)) * dt);
                    c.SpinAngle = (c.SpinAngle + c.Spin * 1500f * dt) % 360f;
                }
                if (c.CycleAt > 0f && Time.time >= c.CycleAt) { c.CycleAt = 0f; if (d.CycleClick.HasValue) Fx.Click(d.CycleClick.Value, p.transform.position + Vector3.up); }
                if (c.CasingAt > 0f && Time.time >= c.CasingAt) { c.CasingAt = 0f; Fx.Casing(p, d, c.CasingSecond); }

                Dive.Pose(p, c.Dive);
                Aim.PoseOn(p, c);
            }

            if (Time.time < _nextSweep) return;
            _nextSweep = Time.time + 2f;
            foreach (Player p in Others.Keys) if (p == null) Gone.Add(p);
            for (int i = 0; i < Gone.Count; i++) Others.Remove(Gone[i]);
            Gone.Clear();
            Fx.Prune();
            Aim.PruneBases();
        }

        /// <summary>Another player has no gun in hand (put away, out of range, down): whatever of theirs was in progress stops.</summary>
        private static void Release(Player p, Carry c)
        {
            if (c.Def == null && !c.Dive.On) return;
            if (c.Dive.On) Dive.End(c.Dive);
            Fx.StopBurst(p);
            c.Def = null; c.Model = null;
            c.Reloading = false; c.BurstUntil = 0f; c.CasingAt = c.CycleAt = 0f; c.Spin = 0f; c.Recoil = 0f;
            c.AimT = c.SprintT = c.Run = 0f;
        }

        /// <summary>The mod unloads: every other player's dive and burst stop, and what we shared is "none".</summary>
        internal static void Reset()
        {
            foreach (KeyValuePair<Player, Carry> kv in Others)
            {
                if (kv.Value.Dive.On) Dive.End(kv.Value.Dive);
                Fx.StopBurst(kv.Key);
            }
            Others.Clear();
            Clear();
            foreach (Player p in Player.GetAllPlayers()) Unregister(p);
        }
    }

    // Every player's ZNetView takes the shot call (the others' shots arrive on their players).
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class Player_Awake_Share
    {
        private static void Postfix(Player __instance) => Share.Register(__instance);
    }
}
