using System.Collections.Generic;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// A companion of yours that is not near you (its area not loaded on your game): what the world knows of it, from its save (its ZDO),
    /// which the game hosting the world keeps for everything in it. Where it is, what it was doing, what it wrote in its journal; and calling
    /// it to you (its save moved beside you: it appears there, coming along).
    /// </summary>
    internal static class Remote
    {
        private static readonly Dictionary<long, ZDO> Found = new Dictionary<long, ZDO>();
        private static float _nextScan;
        private static bool _scanned;

        /// <summary>Its save, if this game has it (always on the game hosting the world; else only when it is near). Looked up every 2 s.</summary>
        public static ZDO Find(long id)
        {
            if (ZDOMan.instance == null) return null;
            if (Time.unscaledTime >= _nextScan) { _nextScan = Time.unscaledTime + 2f; Scan(); }
            return Found.TryGetValue(id, out ZDO z) ? z : null;
        }

        /// <summary>This game hosts the world and has looked: one not found is not in the world at all.</summary>
        public static bool Sure => _scanned && ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>Look again now (the host, answering another player).</summary>
        public static void Rescan() { _nextScan = Time.unscaledTime + 2f; Scan(); }

        private static readonly Dictionary<long, KeyValuePair<bool, float>> Answers = new Dictionary<long, KeyValuePair<bool, float>>();
        private static readonly Dictionary<long, float> Asked = new Dictionary<long, float>();

        /// <summary>
        /// Whether a companion is anywhere in the world, as the game hosting it knows (it keeps everything): true or false, or null while
        /// that is not known yet (another player's game asks the host, and the answer comes back in a moment).
        /// </summary>
        public static bool? InWorld(long id)
        {
            if (Find(id) != null) return true;
            if (Sure) return false;
            if (Answers.TryGetValue(id, out var a) && Time.unscaledTime - a.Value < 20f) return a.Key;
            if (!Asked.TryGetValue(id, out float at) || Time.unscaledTime - at > 5f) { Asked[id] = Time.unscaledTime; Net.AskWhere(id); }
            return Answers.TryGetValue(id, out a) ? a.Key : (bool?)null;
        }

        public static void Heard(long id, bool found) => Answers[id] = new KeyValuePair<bool, float>(found, Time.unscaledTime);

        private static void Scan()
        {
            Found.Clear();
            var list = new List<ZDO>();
            int index = 0;
            for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefab.PrefabName, list, ref index); guard++) { }
            foreach (ZDO z in list)
            {
                if (z == null) continue;
                long id = z.GetLong(Keys.Id, 0L);
                Found[id != 0L ? id : z.m_uid.ID] = z;
            }
            _scanned = true;
        }

        public static string Status(ZDO z) => z.GetString(Keys.Status, "");
        public static Order OrderOf(ZDO z) => (Order)z.GetInt(Keys.Order, 0);
        public static Vector3 Position(ZDO z) => z.GetPosition();

        /// <summary>The middle of its home (as Work.Center), or null when it has none.</summary>
        public static Vector3? Home(ZDO z)
        {
            if (z.GetBool(Work.HomeSetKey, false)) return z.GetVec3(Work.HomeSpotKey, Vector3.zero);
            if (z.GetBool(Keys.HasBed, false)) return z.GetVec3(Keys.BedPos, Vector3.zero);
            return null;
        }

        public static bool AtHome(ZDO z)
        {
            Vector3? home = Home(z);
            if (home == null) return false;
            Vector3 d = Position(z) - home.Value;
            d.y = 0f;
            return d.magnitude <= z.GetInt(Keys.Radius, 30) + 10f;
        }

        public static List<string> Journal(ZDO z, int last)
        {
            var lines = new List<string>((z.GetString(AICompanion.Journal.EntriesKey, "") ?? "").Split('\n'));
            lines.RemoveAll(string.IsNullOrEmpty);
            return lines.GetRange(Mathf.Max(0, lines.Count - last), Mathf.Min(last, lines.Count));
        }

        /// <summary>"240 m north-east of you".</summary>
        public static string Where(Vector3 at, Vector3 from)
        {
            Vector3 d = at - from;
            d.y = 0f;
            if (d.magnitude < 20f) return "right here";
            string[] dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            float ang = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f + 22.5f) % 360f;
            return $"{d.magnitude:0} m {dirs[(int)(ang / 45f) % 8]} of you";
        }

        /// <summary>
        /// Call it to you: its save goes beside you, and it comes along (it appears there in a moment, as its area loads around you).
        /// </summary>
        public static bool Call(ZDO z, Player p)
        {
            if (z == null || p == null) return false;
            if (!z.IsOwner()) z.SetOwner(ZDOMan.GetSessionID());
            Vector3 spot = p.transform.position - p.transform.forward * 2f + Vector3.up * 0.3f;
            z.SetPosition(spot);
            z.Set(Keys.Order, (int)Order.Follow);
            Plugin.Instance?.Note($"{z.GetString(Keys.Name, "A companion")} was called to {p.GetPlayerName()} from afar");
            return true;
        }
    }
}
