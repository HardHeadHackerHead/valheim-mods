using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace AICompanion
{
    /// <summary>
    /// Taking from or putting into a container another game may be using (your chests, its chests, a tombstone, a companion's bag), as
    /// CraftFromChests does it. Container.IsInUse() is only right on the game that owns the container: on every other game it says "free"
    /// while someone has it open, and that game's copy of what is inside can be a second old. Saving that old copy (any change does) undoes
    /// the other player's change: their items are lost or doubled. So: read the flag the owner saves, take the container over, and load
    /// what is really in it before touching anything (docs/modding-pitfalls.md).
    /// </summary>
    internal static class Containers
    {
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        /// <summary>Someone has it open (on any game).</summary>
        public static bool InUse(Container c)
        {
            if (c == null) return false;
            if (c.IsInUse()) return true;
            ZNetView view = c.GetComponent<ZNetView>();
            return view != null && view.IsValid() && !view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>
        /// Before changing what is in it: false when it is gone or someone has it open; else this game runs it from now on, with what is
        /// really in it. Items read from it before this call may be stale copies afterwards: look them up again. "Orphan": the game that
        /// owned it has left, so the "open" it saved is stale (only a running owner clears it).
        /// </summary>
        public static bool Take(Container c, bool orphan = false)
        {
            if (c == null || (orphan ? c.IsInUse() : InUse(c))) return false;
            ZNetView view = c.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (view.IsOwner()) return true;
            view.ClaimOwnership();
            if (Companion.Is(c)) Container_Load_Companion.Fresh(c); // (a companion's bag: load it now even if this game ran it a moment ago)
            Load?.Invoke(c, null); // the game's own reload (does nothing when this copy is already the latest)
            return true;
        }

        /// <summary>The ones it could take (Take), for paying from several chests: counted after they are taken, never before.</summary>
        public static List<Container> TakeAll(IEnumerable<Container> chests) => chests.Where(c => Take(c)).ToList();
    }
}
