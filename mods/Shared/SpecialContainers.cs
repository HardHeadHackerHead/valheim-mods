namespace DHack.Shared
{
    /// <summary>
    /// Built pieces that have a chest's Container but are not for keeping things. A mod that picks, fills or empties chests by itself
    /// (crafting or building from chests, feeding stations, stacking, a companion's own chests) must leave them alone:
    ///   - the game's Obliterator (an Incinerator in the piece): what is in it burns when the lever is pulled;
    ///   - LedgerChest's Ledger Chest: what goes in is sent on to the chests around it;
    ///   - the Recycler and its Press (they have no Container today, but older builds did);
    ///   - Arena's chests in the ring (ZDO "dh_arena_chest"): lent gear and prizes;
    ///   - any piece whose ZDO says "dh_notstorage" (a mod's own special container marks itself this way).
    /// Shared source (mods/Shared/SpecialContainers.cs), linked into AICompanion, BuildFromChests, CraftFromChests, FeedFromChests,
    /// LedgerChest and QualityOfLife. Claude Tools' ContainerAccess template has the same rule.
    /// </summary>
    internal static class SpecialContainers
    {
        private static readonly string[] Prefabs = { "piece_ledgerchest", "piece_recycler" };

        public static bool NotStorage(Container c)
        {
            if (c == null) return false;
            Piece piece = c.GetComponentInParent<Piece>();
            string name = piece != null ? piece.gameObject.name : c.gameObject.name;
            foreach (string prefab in Prefabs)
                if (name.StartsWith(prefab, System.StringComparison.Ordinal) || c.gameObject.name.StartsWith(prefab, System.StringComparison.Ordinal)) return true;
            if ((piece != null ? piece.GetComponentInChildren<Incinerator>(true) : c.GetComponentInParent<Incinerator>()) != null) return true;
            ZNetView view = c.GetComponent<ZNetView>();
            ZDO zdo = view != null && view.IsValid() ? view.GetZDO() : null;
            return zdo != null && (zdo.GetInt("dh_arena_chest", 0) != 0 || zdo.GetBool("dh_notstorage", false));
        }
    }
}
