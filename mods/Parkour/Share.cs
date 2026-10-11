using HarmonyLib;
using UnityEngine;

namespace Parkour
{
    /// <summary>
    /// What the other players with the mod need to pose us on a ledge (Look.cs): which move we are in, the edge and its facing, and how we
    /// shimmy along it. Kept on the local player's own ZDO (its owner is this game, the only one that writes it), under keys of our own, and
    /// written only when something changes enough to see: a new move, the facing turning more than 3 degrees, the edge rising or falling
    /// (or moving off its line) more than 5 cm, every 0.5 m along it, the shimmy changing. Between writes the other games slide the edge
    /// along its line with the body (Look.cs), so a shimmy needs no write for each step. Players without the mod never read them. A move
    /// ending, and the mod unloading, clear them.
    /// </summary>
    internal static class Share
    {
        // poses as shared: the format's version times ten plus the move (an unknown version is not posed)
        public const int Version = 1;
        public const int None = 0, Hang = 1, PullUp = 2, WallRun = 3, Climb = 4, Slide = 5, Vault = 6;   // (Vault: a pull-up over a thin top)

        public static readonly int PoseKey = "parkour_pose".GetStableHashCode(), EdgeKey = "parkour_edge".GetStableHashCode(),
            NormalKey = "parkour_normal".GetStableHashCode(), ShimmyKey = "parkour_shimmy".GetStableHashCode();

        private static int _pose;
        private static Vector3 _edge, _normal;
        private static float _shimmy;

        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        public static ZDO ZdoOf(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        private static int PoseOf(Move m)
        {
            switch (m)
            {
                case Move.Hang: return Hang;
                case Move.PullUp: return Moves.Vaulting ? Vault : PullUp;
                case Move.WallRun: return WallRun;
                case Move.Climb: return Climb;
                case Move.Slide: return Slide;
                default: return None;
            }
        }

        /// <summary>Every physics step on the local player's game: the move now, written only when it changed enough to see.</summary>
        public static void Publish(Player p)
        {
            ZDO zdo = ZdoOf(p);
            if (zdo == null || !zdo.IsOwner()) return;
            int pose = PoseOf(Moves.State);
            Moves.Shared(out Vector3 edge, out Vector3 normal);
            float shimmy = pose == Hang ? Look.LocalShimmy : 0f;
            if (pose == None)
            {
                if (_pose != None) Clear(zdo);
                return;
            }
            bool newPose = pose != _pose, turned = Vector3.Dot(normal, _normal) <= 0.9986f;   // (3 degrees)
            Vector3 moved = edge - _edge, along = Vector3.Cross(Vector3.up, -_normal);
            float alongBy = Mathf.Abs(Vector3.Dot(moved, along));
            bool edgeMoved = pose != Slide && (Mathf.Abs(moved.y) > 0.05f || (moved - along * Vector3.Dot(moved, along)).magnitude > 0.05f || alongBy > 0.5f);
            bool shimmied = Mathf.Abs(shimmy - _shimmy) > 0.1f;
            if (!newPose && !turned && !edgeMoved && !shimmied) return;
            if (newPose || turned || edgeMoved) { zdo.Set(EdgeKey, edge); zdo.Set(NormalKey, normal); _edge = edge; _normal = normal; }
            if (newPose || shimmied) { zdo.Set(ShimmyKey, shimmy); _shimmy = shimmy; }
            if (newPose) zdo.Set(PoseKey, Version * 10 + pose);
            _pose = pose;
        }

        /// <summary>Our keys off the local player's ZDO (a move ended, or the mod unloads).</summary>
        public static void Clear(ZDO zdo)
        {
            if (zdo != null && zdo.IsOwner())
            {
                zdo.RemoveInt(PoseKey);
                zdo.RemoveVec3(EdgeKey);
                zdo.RemoveVec3(NormalKey);
                zdo.RemoveFloat(ShimmyKey);
            }
            _pose = None;
            _edge = _normal = Vector3.zero;
            _shimmy = 0f;
        }

        public static void Reset() => Clear(ZdoOf(Player.m_localPlayer));

        /// <summary>Another player's shared move (None when they have none, or in a format this version doesn't know).</summary>
        public static int Read(ZDO zdo, out Vector3 edge, out Vector3 normal, out float shimmy)
        {
            edge = normal = Vector3.zero;
            shimmy = 0f;
            int value = zdo.GetInt(PoseKey, 0);
            if (value / 10 != Version) return None;
            edge = zdo.GetVec3(EdgeKey, Vector3.zero);
            normal = zdo.GetVec3(NormalKey, Vector3.zero);
            shimmy = zdo.GetFloat(ShimmyKey, 0f);
            return normal.sqrMagnitude > 0.5f ? value % 10 : None;
        }
    }
}
