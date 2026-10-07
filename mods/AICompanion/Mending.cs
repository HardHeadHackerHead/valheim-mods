using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// Repairing your base, a job at home: walls, floors, roofs and the rest that something damaged (a raid, the weather), within its home,
    /// repaired as you would with a hammer, by the game's own rule (a station of the piece's kind in range, as for you: WearNTear.Repair,
    /// which costs nothing). Without a hammer it makes one when it has the wood and stone, else it asks you for one. Also while catching up.
    /// </summary>
    internal static class Mending
    {
        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> Anim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly int Pieces = LayerMask.GetMask("piece", "piece_nonsolid");

        public static ItemDrop.ItemData Hammer(Humanoid h) =>
            h.GetInventory().GetAllItems().FirstOrDefault(i => IsHammer(i) && (!i.m_shared.m_useDurability || i.m_durability > 0f));

        /// <summary>The building hammer (its own build menu), not the hoe or the cultivator.</summary>
        public static bool IsHammer(ItemDrop.ItemData i) => i.m_shared.m_buildPieces != null && i.m_shared.m_buildPieces.name.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A damaged piece you built within range of the spot that the game lets it repair (a station of its kind near it), the nearest.</summary>
        public static WearNTear Damaged(Humanoid me, Vector3 at, float range, Func<Component, bool> allowed)
        {
            return Physics.OverlapSphere(at, range, Pieces).Select(c => c.GetComponentInParent<WearNTear>()).Where(w => w != null).Distinct()
                .Where(w => w.GetHealthPercentage() < 0.9f && allowed(w) && Repairable(w))
                .OrderBy(w => Vector3.Distance(w.transform.position, me.transform.position)).FirstOrDefault();
        }

        private static bool Repairable(WearNTear w)
        {
            Piece p = w.GetComponent<Piece>();
            if (p == null || !p.IsPlacedByPlayer()) return false;
            return p.m_craftingStation == null || CraftingStation.HaveBuildStationInRange(p.m_craftingStation.m_name, w.transform.position) != null;
        }

        /// <summary>One piece back to full health (the hammer swing, the game's repair and its sound). False if it could not.</summary>
        public static bool Fix(BrainState st, WearNTear w)
        {
            Humanoid me = st.Body;
            ItemDrop.ItemData hammer = Hammer(me);
            if (w == null || hammer == null || !Repairable(w)) return false;
            if (!w.Repair()) return false;
            Piece p = w.GetComponent<Piece>();
            if (hammer.m_shared.m_attack != null && !string.IsNullOrEmpty(hammer.m_shared.m_attack.m_attackAnimation)) Anim(me)?.SetTrigger(hammer.m_shared.m_attack.m_attackAnimation);
            p?.m_placeEffect.Create(w.transform.position, w.transform.rotation);
            int n = Journal.Count(me, "mended");
            if (n % 10 == 1) st.Remember("repaired your base");
            return true;
        }

        /// <summary>Catching up: every damaged piece in its home repaired at once. How many.</summary>
        public static int FixAll(BrainState st, Vector3 center, float radius)
        {
            int n = 0;
            for (int i = 0; i < 200; i++)
            {
                WearNTear w = Damaged(st.Body, center, radius, c => true);
                if (w == null || !Fix(st, w)) break;
                n++;
            }
            return n;
        }

        /// <summary>A hammer of its own (wood and stone, no workbench needed), from its bag and its chests near it.</summary>
        public static void MakeHammer(BrainState st)
        {
            GameObject prefab = ObjectDB.instance?.GetItemPrefab("Hammer");
            Recipe r = prefab != null ? ObjectDB.instance.GetRecipe(prefab.GetComponent<ItemDrop>().m_itemData) : null;
            if (r != null) Upgrades.Craft(st, r, null, true);
        }
    }
}
