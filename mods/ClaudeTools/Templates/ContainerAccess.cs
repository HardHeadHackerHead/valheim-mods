using HarmonyLib;
using UnityEngine;

namespace YourMod
{
    /// <summary>
    /// Taking from or putting into chests the player didn't open (crafting from chests, auto-feeding, quick stacking), safely in multiplayer.
    /// A template from Claude Tools (BepInEx/claude/templates): copy it into your mod and change the namespace. The same code runs in
    /// CraftFromChests, BuildFromChests, FeedFromChests and QualityOfLife.
    ///
    ///   foreach (Container c in chestsNearby)
    ///   {
    ///       if (!ContainerAccess.IsStorage(c) || ContainerAccess.InUse(c) || !ContainerAccess.MayUse(c)) continue;
    ///       ContainerAccess.TakeOver(c);           // before touching its items
    ///       ... c.GetInventory().RemoveItem(...) ...
    ///   }
    ///
    /// Find the chests from a list you keep (a Container.Awake postfix adds, skip nulls), never FindObjectsOfType every frame.
    /// </summary>
    internal static class ContainerAccess
    {
        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo NView = AccessTools.Field(typeof(Container), "m_nview");
        private static readonly System.Reflection.MethodInfo Load = AccessTools.Method(typeof(Container), "Load");

        private static ZNetView View(Container c) => c != null ? NView.GetValue(c) as ZNetView : null;

        /// <summary>
        /// A chest someone built. Graves, carts, ships, creatures (a tamed animal's or a companion's bag) and other mods' objects have a
        /// Container too: taking from those empties a grave or a companion. Add your own exclusions (another mod's machine) here.
        /// </summary>
        public static bool IsStorage(Container c)
        {
            if (c == null || c.GetInventory() == null) return false;
            if (c.GetComponentInParent<Piece>() == null || c.GetComponent<TombStone>() != null) return false;
            if (c.GetComponentInParent<Character>() != null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) return false;
            ZNetView view = View(c);
            return view != null && view.IsValid() && !NotStorage(c, view.GetZDO());
        }

        /// <summary>Other mods' special pieces, by prefab name (add yours and those you know of).</summary>
        private static readonly string[] SpecialPrefabs = { "piece_ledgerchest", "piece_recycler" };

        /// <summary>
        /// Built, with a Container, but not for keeping things: the game's Obliterator (what is in it burns when the lever is pulled), other
        /// mods' machines (SpecialPrefabs), Arena's prize chests ("dh_arena_chest"), and any piece that marks itself with a ZDO bool
        /// "dh_notstorage" (do that on a piece of yours that keeps a Container but isn't storage).
        /// </summary>
        public static bool NotStorage(Container c, ZDO zdo)
        {
            Piece piece = c.GetComponentInParent<Piece>();
            string name = piece != null ? piece.gameObject.name : c.gameObject.name;
            foreach (string prefab in SpecialPrefabs) if (name.StartsWith(prefab, System.StringComparison.Ordinal)) return true;
            if ((piece != null ? piece.GetComponentInChildren<Incinerator>(true) : c.GetComponentInParent<Incinerator>()) != null) return true;
            return zdo != null && (zdo.GetInt("dh_arena_chest", 0) != 0 || zdo.GetBool("dh_notstorage", false));
        }

        /// <summary>
        /// Someone has it open. Container.IsInUse() is only right on the chest's owner's game; everyone else reads the flag the owner keeps
        /// in the chest's save data (an int, 1 = open).
        /// </summary>
        public static bool InUse(Container c)
        {
            if (c.IsInUse()) return true;
            ZNetView view = View(c);
            return view != null && view.IsValid() && !view.IsOwner() && view.GetZDO().GetInt(ZDOVars.s_inUse) == 1;
        }

        /// <summary>The player may open it: the ward (PrivateArea) and the chest's own lock (private chests), as the game checks on E.</summary>
        public static bool MayUse(Container c)
        {
            if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) return false;
            long playerId = Game.instance.GetPlayerProfile().GetPlayerID();
            return (bool)CheckAccess.Invoke(c, new object[] { playerId });
        }

        /// <summary>
        /// Only the owner's changes are saved, so take the chest over first. Then reload its contents from the save data: until now this
        /// game had a copy up to a second old, and saving that would undo another player's changes (items duplicated or lost).
        /// </summary>
        public static void TakeOver(Container c)
        {
            ZNetView view = View(c);
            if (view == null || !view.IsValid() || view.IsOwner()) return;
            view.ClaimOwnership();
            Load?.Invoke(c, null); // the game's own reload: does nothing when this copy is already the latest
        }
    }
}
