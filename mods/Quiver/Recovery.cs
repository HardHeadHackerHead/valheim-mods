using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Quiver
{
    /// <summary>
    /// When an arrow you shot hits something it may be kept (each has a chance to be broken). Done on the game that shot it (the projectile
    /// is its own). One that hit the ground, a tree or a wall is dropped where it hit, and the one stuck there is taken away so there are not
    /// two. One that hit a living creature is kept with that creature (Stuck): when it dies the arrows that hit it are part of its loot.
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "OnHit")]
    internal static class Projectile_OnHit_Recover
    {
        private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData> Ammo = AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_ammo");
        private static readonly AccessTools.FieldRef<Projectile, Character> Owner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile, ZNetView> View = AccessTools.FieldRefAccess<Projectile, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData> SpawnItem = AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_spawnItem");
        private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData> Weapon = AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_weapon");
        private static readonly HashSet<int> Done = new HashSet<int>();

        private static void Postfix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
        {
            if (Plugin.Debug.Value)
            {
                ZNetView nv = View(__instance);
                Plugin.Log.LogInfo($"OnHit {__instance.name}: owner {Owner(__instance)?.name ?? "none"} ({Owner(__instance)?.GetType().Name}), view {(nv == null ? "none" : nv.IsValid() ? (nv.IsOwner() ? "owned here" : "owned elsewhere") : "invalid")}, water {water}, on {collider?.name ?? "nothing"}");
            }
            if (!Plugin.Recover.Value || water) return;
            // (an arrow is not a network object: each player's game flies its own, so this one is handled where it was shot, by the player who shot it)
            if (!(Owner(__instance) is Player shooter) || shooter != Player.m_localPlayer) return;
            if (BetterArchery.ArrowsOn) return; // it gives arrows back itself
            ItemDrop.ItemData ammo = Ammo(__instance) ?? SpawnItem(__instance);   // (the arrow it was shot with)
            if (Plugin.Debug.Value)
                Plugin.Log.LogInfo($"Hit by {__instance.name}: ammo {Ammo(__instance)?.m_shared.m_name ?? "none"}, spawnItem {SpawnItem(__instance)?.m_shared.m_name ?? "none"}, weapon {Weapon(__instance)?.m_shared.m_name ?? "none"}, " +
                                   $"on {collider?.name ?? "nothing"} ({collider?.GetComponentInParent<Character>()?.name ?? "no creature"})");
            if (ammo == null || ammo.m_dropPrefab == null || !Recoverable(ammo)) { if (Plugin.Debug.Value) Plugin.Log.LogInfo("  not kept: " + (ammo == null ? "no ammo item on the projectile" : ammo.m_dropPrefab == null ? "ammo has no drop prefab" : "not an arrow or bolt it keeps")); return; }
            if (!Done.Add(__instance.GetInstanceID())) return;                // (a hit can be reported twice)
            if (Done.Count > 300) Done.Clear();

            Character target = collider != null ? collider.GetComponentInParent<Character>() : null;
            if (target != null && !target.IsPlayer() && !target.IsTamed() && !target.IsDead())
            {
                // Into the creature's loot: the arrow stays stuck in it (its own picture), and comes out with its drops if it dies.
                bool kept = Random.value <= Plugin.CreatureChance.Value;
                if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  creature {target.name}: arrow {ammo.m_dropPrefab.name} {(kept ? "kept for when it dies" : "broke")}");
                if (kept) Stuck.Add(target, ammo);
                return;
            }
            if (Random.value > Plugin.GroundChance.Value) { if (Plugin.Debug.Value) Plugin.Log.LogInfo("  broke on what it hit"); return; }

            if (Plugin.Debug.Value) Plugin.Log.LogInfo($"  {ammo.m_dropPrefab.name} dropped where it hit");
            ItemDrop.ItemData one = ammo.Clone();
            one.m_stack = 1;
            one.m_equipped = false; // (a copy of the arrows you have equipped: the one on the ground is not)
            ItemDrop.DropItem(one, 1, hitPoint + normal * 0.08f + Vector3.up * 0.05f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            try { __instance.SetStayTTL(0.05f); } catch (System.Exception) { }                                      // (the arrow stuck in the ground goes: it is the one on the ground now)
        }

        private static bool Recoverable(ItemDrop.ItemData ammo)
        {
            if (ammo.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo) return false;
            string kind = (ammo.m_shared.m_ammoType ?? "").ToLowerInvariant();
            if (kind.Contains("bolt") && !Plugin.Bolts.Value) return false;
            if (!kind.Contains("arrow") && !kind.Contains("bolt")) return false;   // (not a bow's or crossbow's)
            return !(Plugin.FireBurns.Value && ammo.m_dropPrefab.name.ToLowerInvariant().Contains("fire"));
        }
    }
}
