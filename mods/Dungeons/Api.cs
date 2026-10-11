using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// What other mods may ask Quad's Dungeons (by reflection, so they load the same without it). Type "Dungeons.Api" in the assembly
    /// "Dungeons"; every method is public static and takes and returns only plain or UnityEngine types.
    /// </summary>
    public static class Api
    {
        /// <summary>The point is inside a dungeon built on this game whose pack has parkour off ("parkour": false): wall runs, wall jumps,
        /// climbs and ledge grabs would let a player skip its puzzle. Used by Quad's Parkour.</summary>
        public static bool BlocksParkour(Vector3 position)
        {
            if (position.y < 3000f) return false;
            Site site = Dungeons.Containing(position);
            return site != null && site.Ready && site.Layout?.Pack != null && !PackReader.Parkour(site.Layout.Pack);
        }
    }
}
