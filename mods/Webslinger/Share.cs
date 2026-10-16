using HarmonyLib;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// The web we have out, for the other players with the mod to draw (Look.cs): what kind, where it's stuck, and a count that rises with each
    /// new web (so a new web to a spot near the last is drawn flying out again). Kept on the local player's own ZDO (its owner is this game,
    /// the only one that writes it), under keys of our own, and written only when a web starts or ends: a few writes per swing, nothing in
    /// between (the body's own position and rotation already go to everyone). A web ending, and the mod unloading, write "none": a key
    /// removed from a ZDO is never removed on the other games, so removing it would leave them drawing the last web. Players without the mod
    /// never read them; they see the body lean and the game's own grappling arm pose.
    /// </summary>
    internal static class Share
    {
        // what is shared: the format's version times ten plus the kind (an unknown version is not drawn)
        public const int Version = 1;
        public const int None = 0, Swing = 1, Zip = 2, Loose = 3;
        private const int NoWeb = Version * 10 + None;

        public static readonly int WebKey = "webslinger_web".GetStableHashCode(), AnchorKey = "webslinger_anchor".GetStableHashCode(),
            ShotKey = "webslinger_shot".GetStableHashCode();
        /// <summary>The move now (Move as a number, with the version: version times ten plus the move) and the flips done, for the poses (Body.cs).</summary>
        public static readonly int MoveKey = "webslinger_move".GetStableHashCode(), FlipKey = "webslinger_flips".GetStableHashCode();
        private static int _move = -1, _flips = -1;

        /// <summary>Webs shot since the mod loaded (the count shared with each).</summary>
        public static int Shots;
        private static int _kind, _shot;

        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        public static ZDO ZdoOf(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        public static void NewWeb() => Shots++;

        /// <summary>Every physics step on the local player's game: the web now, written only when it changed.</summary>
        public static void Publish(Player p)
        {
            ZDO zdo = ZdoOf(p);
            if (zdo == null || !zdo.IsOwner()) return;
            int move = Version * 10 + (int)Web.State;
            if (move != _move) { zdo.Set(MoveKey, move); _move = move; }
            if (Body.Flips != _flips) { zdo.Set(FlipKey, Body.Flips); _flips = Body.Flips; }
            int kind = None, shot = _shot;
            Vector3 anchor = Vector3.zero;
            if (Web.State == Move.Swing || Web.State == Move.Zip) { kind = Web.State == Move.Swing ? Swing : Zip; anchor = Web.Anchor; shot = Shots; }
            else if (Time.time - Web.LooseAt < 0.3f) { kind = Loose; anchor = Web.LooseEnd; shot = Mathf.RoundToInt(Web.LooseAt * 10f); }
            if (kind == None)
            {
                // (also a web left on the ZDO by a game that no longer remembers it: a reload, a new character)
                if (_kind != None || zdo.GetInt(WebKey, NoWeb) != NoWeb) Clear(zdo);
                return;
            }
            if (kind == _kind && shot == _shot) return;
            zdo.Set(AnchorKey, anchor);
            zdo.Set(ShotKey, shot);
            zdo.Set(WebKey, Version * 10 + kind);
            _kind = kind;
            _shot = shot;
        }

        public static void Clear(ZDO zdo)
        {
            if (zdo != null && zdo.IsOwner()) { zdo.Set(WebKey, NoWeb); zdo.Set(MoveKey, Version * 10); }
            _kind = None;
            _move = -1;
        }

        public static void Forget() { _kind = None; _shot = 0; _move = _flips = -1; }

        /// <summary>Another player's move now (None when they share none, or in a format this version doesn't know).</summary>
        public static Move MoveOf(Player p)
        {
            ZDO zdo = ZdoOf(p);
            int value = zdo != null ? zdo.GetInt(MoveKey, 0) : 0;
            if (value / 10 != Version) return Move.None;
            int m = value % 10;
            return m >= 0 && m <= (int)Move.Fly ? (Move)m : Move.None;
        }

        public static int FlipsOf(Player p)
        {
            ZDO zdo = ZdoOf(p);
            return zdo != null ? zdo.GetInt(FlipKey, 0) : 0;
        }

        public static void Reset() => Clear(ZdoOf(Player.m_localPlayer));

        /// <summary>Another player's web (None when they have none, or in a format this version doesn't know).</summary>
        public static int Read(ZDO zdo, out Vector3 anchor, out int shot)
        {
            anchor = Vector3.zero;
            shot = 0;
            int value = zdo.GetInt(WebKey, 0);
            if (value / 10 != Version || value % 10 == None) return None;
            anchor = zdo.GetVec3(AnchorKey, Vector3.zero);
            shot = zdo.GetInt(ShotKey, 0);
            return value % 10;
        }
    }
}
