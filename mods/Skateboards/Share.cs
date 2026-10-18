using HarmonyLib;
using UnityEngine;

namespace Skateboards
{
    /// <summary>What Look.cs needs to draw a skater: on the board or not, how, which way the nose points, the tilt, the trick.</summary>
    internal struct Pose
    {
        public Ride State;
        public float NoseYaw;
        public Vector3 Normal;
        public Tricks.Flip Flip;
        public int FlipCount;
        public Tricks.Grab Grab;
        public Tricks.Grind Grind;
        public float Crouch;   // 0 to 1: winding up an ollie
        public bool Goofy, NoseManual, Pushing, Double, Special;
        public float Lean;     // -1 to 1: carving left or right

        public bool On => State != Ride.Off;
    }

    /// <summary>
    /// The local player's pose, kept on their own ZDO (its owner is this game, the only one that writes it) under keys of our own, so
    /// the other players with the mod see them skate: the state, the nose's yaw (when it turns more than 3 degrees), the tilt (more than 4
    /// degrees), and the trick, stance and crouch packed in one number. Written only when something changes; the body's position the game
    /// shares itself. Getting off, and the mod unloading, write "off": a key removed from a ZDO is never removed on the other games.
    /// </summary>
    internal static class Share
    {
        public const int Version = 1;
        private static readonly int StateKey = "skateboards_state".GetStableHashCode(), PoseKey = "skateboards_pose".GetStableHashCode(),
            YawKey = "skateboards_yaw".GetStableHashCode(), NormalKey = "skateboards_normal".GetStableHashCode();

        private static int _state = -1, _pose = -1;
        private static float _yaw;
        private static Vector3 _normal;

        private static readonly AccessTools.FieldRef<Character, ZNetView> NView = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        public static ZDO ZdoOf(Player p)
        {
            ZNetView nview = p != null ? NView(p) : null;
            return nview != null && nview.IsValid() ? nview.GetZDO() : null;
        }

        /// <summary>The local player's pose now.</summary>
        public static Pose Local()
        {
            var pose = new Pose
            {
                State = Skate.State,
                Goofy = Plugin.Goofy.Value,
            };
            if (!pose.On) return pose;
            pose.NoseYaw = Skate.NoseYaw;
            pose.Normal = Skate.PoseNormal;
            pose.Flip = Tricks.Flipping ? Tricks.FlipNow : Tricks.Flip.None;
            pose.FlipCount = Tricks.Flips;
            pose.Grab = Tricks.GrabNow;
            pose.Grind = Tricks.GrindNow;
            pose.Crouch = Skate.Charge;
            pose.NoseManual = Tricks.NoseManual;
            pose.Pushing = Skate.Pushing && pose.State == Ride.Roll;
            pose.Double = Tricks.FlipDouble;
            pose.Special = Tricks.SpecialOn;
            pose.Lean = pose.State == Ride.Roll || pose.State == Ride.Manual ? Skate.Lean : 0f;
            return pose;
        }

        private static int Pack(Pose p) =>
            (p.Goofy ? 1 : 0) | ((int)p.Flip & 7) << 1 | (p.FlipCount & 63) << 4 | ((int)p.Grab & 7) << 10 | ((int)p.Grind & 7) << 13
            | (Mathf.Clamp(Mathf.RoundToInt(p.Crouch * 15f), 0, 15) << 16) | (p.NoseManual ? 1 << 20 : 0)
            | (p.Pushing ? 1 << 21 : 0) | (p.Double ? 1 << 22 : 0) | (p.Special ? 1 << 23 : 0)
            | ((Mathf.Clamp(Mathf.RoundToInt(p.Lean * 7f), -7, 7) + 7) << 24);

        /// <summary>Every physics step on the local player's game: the pose, written only when it changed enough to see.</summary>
        public static void Publish(Player p)
        {
            ZDO zdo = ZdoOf(p);
            if (zdo == null || !zdo.IsOwner()) return;
            Pose pose = Local();
            int state = Version * 10 + (int)pose.State;
            if (!pose.On)
            {
                if (_state != state || zdo.GetInt(StateKey, state) != state) { zdo.Set(StateKey, state); _state = state; }
                return;
            }
            int packed = Pack(pose);
            if (packed != _pose) { zdo.Set(PoseKey, packed); _pose = packed; }
            if (Mathf.Abs(Mathf.DeltaAngle(pose.NoseYaw, _yaw)) > 3f || _state != state) { zdo.Set(YawKey, pose.NoseYaw); _yaw = pose.NoseYaw; }
            if (Vector3.Dot(pose.Normal, _normal) < 0.9976f || _state != state) { zdo.Set(NormalKey, pose.Normal); _normal = pose.Normal; }
            if (state != _state) { zdo.Set(StateKey, state); _state = state; }
        }

        /// <summary>Off the board on the local player's ZDO (getting off happens in Publish; this is for the mod unloading).</summary>
        public static void Reset()
        {
            ZDO zdo = ZdoOf(Player.m_localPlayer);
            if (zdo != null && zdo.IsOwner()) zdo.Set(StateKey, Version * 10 + (int)Ride.Off);
            Forget();
        }

        /// <summary>What was last written, forgotten (a new character's ZDO is a new one).</summary>
        public static void Forget()
        {
            _state = _pose = -1;
            _yaw = 0f;
            _normal = Vector3.zero;
        }

        /// <summary>Another player's pose (off when they aren't skating, or share a format this version doesn't know).</summary>
        public static Pose Read(Player p)
        {
            var pose = new Pose();
            ZDO zdo = ZdoOf(p);
            if (zdo == null) return pose;
            int state = zdo.GetInt(StateKey, 0);
            if (state / 10 != Version) return pose;
            pose.State = (Ride)(state % 10);
            if (!pose.On) return pose;
            int packed = zdo.GetInt(PoseKey, 0);
            pose.Goofy = (packed & 1) != 0;
            pose.Flip = (Tricks.Flip)((packed >> 1) & 7);
            pose.FlipCount = (packed >> 4) & 63;
            pose.Grab = (Tricks.Grab)((packed >> 10) & 7);
            pose.Grind = (Tricks.Grind)((packed >> 13) & 7);
            pose.Crouch = ((packed >> 16) & 15) / 15f;
            pose.NoseManual = (packed & (1 << 20)) != 0;
            pose.Pushing = (packed & (1 << 21)) != 0;
            pose.Double = (packed & (1 << 22)) != 0;
            pose.Special = (packed & (1 << 23)) != 0;
            pose.Lean = (((packed >> 24) & 15) - 7) / 7f;
            pose.NoseYaw = zdo.GetFloat(YawKey, 0f);
            pose.Normal = zdo.GetVec3(NormalKey, Vector3.up);
            if (pose.Normal.sqrMagnitude < 0.5f) pose.Normal = Vector3.up;
            return pose;
        }
    }
}
