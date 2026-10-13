using System.Globalization;
using UnityEngine;

namespace Arena
{
    /// <summary>
    /// For other mods, read-only, found by reflection (no reference to this assembly): see docs/arena-api.md. Any game with the mod can
    /// answer, not just the fighter's: another player's fight is known from what the fighter's game tells everyone (Net), so it reads the
    /// same on the host, the fighter's game and a game across the world.
    /// </summary>
    public static class Api
    {
        public const int Version = 1;

        /// <summary>A contest is on in the ring (yours, or another player's heard from in the last few seconds; not a duel). kind is
        /// "road", "champion", "endless" or "trial". Another player's fight is only seen if their Arena is 0.4.4 or newer (an older one
        /// does not say what kind of fight it is, and a duel tells everyone the same way).</summary>
        public static bool IsContestRunning(out string fighter, out string kind)
        {
            if (Contest.Active)
            {
                fighter = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";
                kind = Contest.KindWord(Contest.Kind);
                return true;
            }
            bool remote = Net.RemoteFight && Net.RemoteKind.Length > 0;
            fighter = remote ? Net.RemoteName : "";
            kind = remote ? Net.RemoteKind : "";
            return remote;
        }

        /// <summary>The middle of the fighting floor; false until this game has found the arena.</summary>
        public static bool ArenaAt(out Vector3 centre)
        {
            centre = Site.Known ? Site.Origin : Vector3.zero;
            return Site.Known;
        }

        /// <summary>Up to max seats in the stands, spread round the ring, in world positions (empty until the arena is found). Each seat
        /// faces the middle of the ring. The crowd's figures sit on seats too: stand beside one rather than on it.</summary>
        public static Vector3[] StandSpots(int max)
        {
            int count = Layout.Seats.Count;
            if (!Site.Known || count == 0 || max <= 0) return new Vector3[0];
            int n = Mathf.Min(max, count);
            var spots = new Vector3[n];
            for (int i = 0; i < n; i++) spots[i] = Site.World(Layout.Seats[i * count / n].Pos);
            return spots;
        }

        /// <summary>Goes up by one each time this game hears of a contest's end (its own or another player's): poll it and read LastResult
        /// when it changes.</summary>
        public static int ResultCount { get; private set; }

        /// <summary>The last contest to end since this game started. outcome is "win", "cashout", "yield", "died", "timeup", "leftring",
        /// "carried", "calledoff" or "aborted" (the game left mid-fight); won is true only for "win" (the whole contest); crowned: the run
        /// went into the Hall of Champions; coins: the purse paid (without the stake); when: the world's time in seconds (ZNet).</summary>
        public static bool LastResult(out string fighter, out string kind, out string outcome, out bool won, out bool crowned, out int coins, out double when)
        {
            fighter = _fighter; kind = _kind; outcome = _outcome; won = _outcome == "win"; crowned = _crowned; coins = _coins; when = _when;
            return ResultCount > 0;
        }

        private static string _fighter = "", _kind = "", _outcome = "";
        private static bool _crowned;
        private static int _coins;
        private static double _when;

        // the result as Net carries it: kind;outcome;crowned (0/1);coins
        internal static string Pack(string kind, string outcome, bool crowned, int coins) =>
            string.Join(";", kind, outcome, crowned ? "1" : "0", coins.ToString(CultureInfo.InvariantCulture));

        internal static void Heard(string fighter, string data)
        {
            string[] v = (data ?? "").Split(';');
            if (v.Length < 4 || !int.TryParse(v[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins)) return;
            _fighter = fighter ?? ""; _kind = v[0]; _outcome = v[1]; _crowned = v[2] == "1"; _coins = coins;
            _when = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
            ResultCount++;
        }
    }
}
