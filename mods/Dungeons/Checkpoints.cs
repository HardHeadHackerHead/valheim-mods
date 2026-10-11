using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using static DHack.Shared.Translations;

namespace Dungeons
{
    /// <summary>
    /// Checkpoints (a mechanism of kind "checkpoint", where the pack puts one): E makes it the player's own. A player who dies inside with
    /// one comes back there instead of at their bed: Game.FindSpawnPoint, after a death only (a login still uses the logout point, which
    /// Safety moves to the entrance), waits for the area as the game does and spawns them there, 5000 m up; Safety holds them still until
    /// the dungeon is built here. Their gravestone stays where they died (the game's own).
    /// Per player, in their character's custom data ("quaddungeon_checkpoint": dungeon id | its resets | where | facing), so it survives a
    /// relog; it's dropped when they leave by the way out (Dungeons.Leave), and when the dungeon has been reset since (its resets differ).
    /// </summary>
    internal static class Checkpoints
    {
        public const string DataKey = "quaddungeon_checkpoint";

        internal struct Point
        {
            public string Dungeon;
            public int Resets;
            public Vector3 At;
            public float Yaw;
            public string Mech;
        }

        private static Point? _mine;        // the local player's, kept here too (the player object is gone when the game picks a spawn point)
        private static bool _rose;          // the last spawn was at the checkpoint (a message once the player is up)

        public static Point? Mine => _mine;

        /// <summary>This mechanism is the local player's checkpoint.</summary>
        public static bool IsMine(Site site, Mech m) => _mine is Point p && site != null && p.Dungeon == site.Id && p.Mech == m.Key;

        /// <summary>The local player makes a checkpoint theirs: 1.5 m in front of it, facing away from it.</summary>
        public static void Set(Site site, Mech m, Player p)
        {
            if (site?.Zdo == null || m?.Base == null || p == null) return;
            Vector3 front = Vector3.ProjectOnPlane(m.Base.forward, Vector3.up).normalized;
            if (front.sqrMagnitude < 0.01f) front = Vector3.forward;
            var point = new Point { Dungeon = site.Id, Resets = site.Zdo.GetInt(Keys.Resets), At = m.Base.position + front * 1.5f + Vector3.up * 0.3f, Yaw = Quaternion.LookRotation(front).eulerAngles.y, Mech = m.Key };
            _mine = point;
            p.m_customData[DataKey] = Format(point);
            p.Message(MessageHud.MessageType.Center, T("dungeons_checkpoint_saved"));
            Plugin.Log.LogInfo($"Dungeon {site.Id}: {p.GetPlayerName()}'s checkpoint is {m.Key} ({point.At})");
        }

        /// <summary>Forgotten: the player left by the way out (Dungeons.Leave), the dungeon was reset, or a command.</summary>
        public static void Clear(Player p, string why)
        {
            if (_mine == null && (p == null || !p.m_customData.ContainsKey(DataKey))) return;
            _mine = null;
            p?.m_customData.Remove(DataKey);
            Plugin.Log.LogInfo($"The player's dungeon checkpoint is cleared: {why}");
        }

        /// <summary>Dungeons.Leave: leaving a dungeon by its way out clears the checkpoint in it.</summary>
        public static void Left(Site site)
        {
            if (_mine is Point p && site != null && p.Dungeon == site.Id) Clear(Player.m_localPlayer, "left by the way out");
        }

        /// <summary>Once a second (BlockKeys.Tick): read back from the character after a login, and dropped once its dungeon was reset.</summary>
        public static void Tick(Player p)
        {
            if (_mine == null && p.m_customData.TryGetValue(DataKey, out string text) && Parse(text, out Point read)) _mine = read;
            if (_rose && !p.IsTeleporting() && !Safety.Holding)
            {
                _rose = false;
                p.Message(MessageHud.MessageType.Center, T("dungeons_checkpoint_respawn"));
            }
            if (!(_mine is Point mine)) return;
            foreach (Site site in Dungeons.Sites)
                if (site.Id == mine.Dungeon && site.Zdo != null && site.Zdo.GetInt(Keys.Resets) != mine.Resets) { Clear(p, "its dungeon was reset"); return; }
        }

        /// <summary>The world changed (a different character or world): nothing kept.</summary>
        public static void Forget() { _mine = null; _rose = false; }

        private static string Format(Point p) => string.Join("|", p.Dungeon, p.Resets.ToString(CultureInfo.InvariantCulture), MechSet.Vec(p.At),
                                                                p.Yaw.ToString("0.#", CultureInfo.InvariantCulture), p.Mech);

        private static bool Parse(string text, out Point p)
        {
            p = default;
            string[] f = (text ?? "").Split('|');
            if (f.Length < 5) return false;
            string[] v = f[2].Split(',');
            if (v.Length < 3) return false;
            p = new Point
            {
                Dungeon = f[0],
                Resets = TriggerState.I(f[1]),
                At = new Vector3((float)TriggerState.D(v[0]), (float)TriggerState.D(v[1]), (float)TriggerState.D(v[2])),
                Yaw = (float)TriggerState.D(f[3]),
                Mech = f[4],
            };
            return p.At.y > 3000f && !string.IsNullOrEmpty(p.Dungeon);
        }

        /// <summary>Is the checkpoint's dungeon still as it was? A known entrance with other resets: no; not known here (a client far from
        /// it): yes (Safety holds them until it's built, or takes them out if it never is).</summary>
        private static bool StillGood(Point p)
        {
            if (ZDOMan.instance == null) return false;
            var found = new List<ZDO>();
            foreach (string name in Entrance.PrefabNames)
            {
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(name, found, ref index)) { }
            }
            foreach (ZDO z in found)
                if (z.GetString(Keys.Id) == p.Dungeon) return z.GetInt(Keys.Resets) == p.Resets;
            return true;
        }

        // ---- the game's respawn

        private static readonly AccessTools.FieldRef<Game, bool> AfterDeath = AccessTools.FieldRefAccess<Game, bool>("m_respawnAfterDeath");
        private static readonly AccessTools.FieldRef<Game, float> Wait = AccessTools.FieldRefAccess<Game, float>("m_respawnWait");
        private static readonly AccessTools.FieldRef<Game, float> LoadDuration = AccessTools.FieldRefAccess<Game, float>("m_respawnLoadDuration");

        /// <summary>Game.FindSpawnPoint after a death: the checkpoint, if the player has one that still stands; false: the game's own way.
        /// As the game does for a logout point: the area round it loaded first, then the point (usedLogoutPoint, so it's not their home).</summary>
        public static bool SpawnPoint(Game game, float dt, out Vector3 point, out bool found)
        {
            point = Vector3.zero;
            found = false;
            if (!AfterDeath(game) || !(_mine is Point p)) return false;
            if (!StillGood(p))
            {
                Clear(Player.m_localPlayer, "its dungeon was reset or removed");
                return false;
            }
            Wait(game) += dt;
            ZNet.instance.SetReferencePosition(p.At);
            if (Wait(game) <= LoadDuration(game) || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(p.At)) return true;
            point = p.At;
            found = true;
            _rose = true;
            Plugin.Log.LogInfo($"Respawning at the dungeon checkpoint {p.Mech} of {p.Dungeon} ({p.At}), not at the bed");
            return true;
        }
    }

    // After a death inside a dungeon, back at the player's checkpoint (the game's own way otherwise: bed, then the start).
    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    internal static class Dungeons_Game_FindSpawnPoint
    {
        private static bool Prefix(Game __instance, float dt, ref Vector3 point, ref bool usedLogoutPoint, ref bool __result)
        {
            try
            {
                if (!Checkpoints.SpawnPoint(__instance, dt, out Vector3 at, out bool found)) return true;
                point = at;
                usedLogoutPoint = found;   // (so the game doesn't make it their home point)
                __result = found;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("The dungeon checkpoint's respawn failed (the game's own spawn is used): " + e);
                return true;
            }
        }
    }

    // Leaving the world: the checkpoint and anything on its way to a pedestal forgotten here (the character keeps its checkpoint).
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Shutdown))]
    internal static class Dungeons_ZNetScene_Shutdown_Checkpoints
    {
        private static void Prefix()
        {
            Checkpoints.Forget();
            MechSet.ForgetStatic();
        }
    }
}
