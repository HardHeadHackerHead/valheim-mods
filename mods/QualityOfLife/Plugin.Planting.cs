using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace QualityOfLife
{
    public partial class Plugin
    {
        internal static ConfigEntry<bool> PlantNeedsRoom;

        private void BindPlantingConfig()
        {
            PlantNeedsRoom = Config.Bind("Planting", "NeedRoomToGrow", true,
                "A seed, sapling or crop can only be planted where it has the room to grow up: the same check the game makes later (a plant with another plant, a rock, " +
                "a tree or a building too close never grows), made before you plant, so the ghost turns red and you are told. Takes effect at once.");
        }
    }

    /// <summary>The game only finds out that a plant has no room to grow after it is planted (and then it just sits there). Ask first.</summary>
    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class Player_UpdatePlacementGhost_PlantRoom
    {
        private static readonly AccessTools.FieldRef<Player, GameObject> Ghost = AccessTools.FieldRefAccess<Player, GameObject>("m_placementGhost");
        private static readonly AccessTools.FieldRef<Player, Player.PlacementStatus> Status = AccessTools.FieldRefAccess<Player, Player.PlacementStatus>("m_placementStatus");
        private static int _mask;

        private static void Postfix(Player __instance)
        {
            if (Plugin.PlantNeedsRoom == null || !Plugin.PlantNeedsRoom.Value) return;
            GameObject ghost = Ghost(__instance);
            if (ghost == null || !ghost.activeInHierarchy || Status(__instance) != Player.PlacementStatus.Valid) return;
            Plant plant = ghost.GetComponent<Plant>();
            if (plant == null || !LacksRoom(plant, ghost.transform.position)) return;
            Status(__instance) = Player.PlacementStatus.MoreSpace;
            ghost.GetComponent<Piece>()?.SetInvalidPlacementHeightlight(true);
        }

        /// <summary>The game's own room check (Plant.HaveGrowSpace), made at the ghost's place: true when there is not enough room.</summary>
        private static bool LacksRoom(Plant plant, Vector3 at)
        {
            if (_mask == 0) _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            Collider[] found = Physics.OverlapSphere(at, plant.m_growRadius, _mask);
            foreach (Collider c in found)
            {
                Plant other = c.GetComponent<Plant>();
                if (other == null || (other != plant && other.GetStatus() == Plant.Status.Healthy)) return true;
            }
            if (plant.m_growRadiusVines > 0f)
                foreach (Collider c in Physics.OverlapSphere(at, plant.m_growRadiusVines, _mask))
                    if (c.GetComponentInParent<Vine>() != null) return true;
            return false;
        }
    }
}
