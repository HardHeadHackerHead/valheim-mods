using System.Collections.Generic;
using System.Linq;

namespace BetterCraftingStations
{
    /// <summary>
    /// What is chosen on the chips for each crafting station (a workbench keeps its own, the forge its own), kept while the game runs, and the
    /// rule that picks the recipes that show.
    /// </summary>
    internal static class Filters
    {
        internal class State
        {
            public string Type;          // null: every type
            public int Tier = -1;        // -1: every tier
            public bool CanCraft;        // only what it can make now
        }

        private static readonly Dictionary<string, State> ByStation = new Dictionary<string, State>();

        public static string StationKey()
        {
            CraftingStation station = Player.m_localPlayer?.GetCurrentCraftingStation();
            return station != null ? station.m_name : "";
        }

        public static State Of(string station)
        {
            if (!ByStation.TryGetValue(station, out State s)) ByStation[station] = s = new State();
            return s;
        }

        public static bool Active(State s) => s.Type != null || s.Tier >= 0 || s.CanCraft;

        /// <summary>The recipes that pass what is chosen. What cannot be classified is never hidden by a type or tier it has no part in.</summary>
        public static List<Recipe> Apply(List<Recipe> all, State s)
        {
            if (!Active(s)) return all;
            Player player = Player.m_localPlayer;
            return all.Where(r => r != null && (s.Type == null || Classify.TypeOf(r) == s.Type)
                                          && (s.Tier < 0 || Classify.TierOf(r) == s.Tier)
                                          && (!s.CanCraft || player != null && player.HaveRequirements(r, false, 1))).ToList();
        }
    }
}
