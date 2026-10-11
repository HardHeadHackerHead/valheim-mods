using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // The saved objects the owner makes once (Builder.SpawnNetworked), besides the game rooms' own: the doors where two rooms join
    // (Layout.Doors, the pack's door prefabs), placed as the game's DungeonGenerator.PlaceDoors places them, and our own rooms' Saved parts
    // (a chest, a spawner): ordinary networked objects, not stripped.
    internal static partial class Builder
    {
        private static readonly HashSet<string> WarnedOnce = new HashSet<string>();

        private static void WarnOnce(string message)
        {
            if (WarnedOnce.Add(message)) Plugin.Log.LogWarning(message);
        }

        /// <summary>The layout's doors by the room each is made with: the link's Room (the room the other was joined to, as the game's door
        /// belongs to the connection it placed the next room on).</summary>
        private static Dictionary<int, List<Link>> DoorsByRoom(Layout layout)
        {
            var by = new Dictionary<int, List<Link>>();
            if (layout?.Doors == null) return by;
            foreach (Link link in layout.Doors)
            {
                if (!by.TryGetValue(link.Room, out List<Link> list)) by[link.Room] = list = new List<Link>();
                list.Add(link);
            }
            return by;
        }

        /// <summary>
        /// The doors at these links, as PlaceDoors makes them: the pack's door prefab for the doorway's type, at the joint (the link's Room's
        /// doorway), turned as that doorway (the game uses its RoomConnection's rotation, whose forward points into the room). Doorways in a
        /// floor or a ceiling get none. The copy's position and rotation are set whole (the game's prefabs can hold odd ones).
        /// </summary>
        private static int SpawnDoors(Built built, List<Link> links, CreatureFit fit)
        {
            Layout layout = built.Layout;
            int made = 0;
            foreach (Link link in links)
            {
                if (link.Room < 0 || link.Room >= layout.Rooms.Count) continue;
                PlacedRoom room = layout.Rooms[link.Room];
                if (room.Def == null || link.Doorway < 0 || link.Doorway >= room.Def.Doorways.Count) continue;
                string type = room.Def.Doorways[link.Doorway].Type ?? "";
                if (layout.Pack?.Doors == null || !layout.Pack.Doors.TryGetValue(type, out string name) || string.IsNullOrEmpty(name))
                {
                    WarnOnce($"Pack {layout.Pack?.Name}: no door for doorways of type \"{type}\" (its \"doors\")");
                    continue;
                }
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null || prefab.GetComponent<ZNetView>() == null)
                {
                    WarnOnce($"Pack {layout.Pack.Name}: its door {name} isn't one of the game's networked prefabs");
                    continue;
                }
                Vector3 facing = room.DoorwayFacing(link.Doorway);
                if (Mathf.Abs(facing.y) > 0.5f) continue;
                facing.y = 0f;
                Vector3 at = built.Origin + room.DoorwayPos(link.Doorway);
                Quaternion turn = Quaternion.LookRotation(facing.normalized, Vector3.up);
                try
                {
                    GameObject go = Object.Instantiate(prefab, at, turn);
                    go.transform.SetPositionAndRotation(at, turn);
                    // (a door that gives what it's made of when broken gives only what fits the land: BuildLoot.cs)
                    FitLoot(go, fit, "common", new System.Random(unchecked(fit.Seed * 31 + link.Room * 1009 + link.Doorway)), $"{link.Room}: {name}");
                    Settle(go);
                    made++;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Making the door {name} between rooms {link.Room} and {link.OtherRoom}: {e.Message}"); }
            }
            return made;
        }

        /// <summary>One of our rooms' Saved parts as saved objects (its other parts are the shell: BuildParts.cs).</summary>
        private static int SpawnSavedParts(Built built, PlacedRoom room, CreatureFit fit)
        {
            Vector3 pos = WorldPos(built, room);
            Quaternion rot = WorldRot(room);
            int made = 0, ordinal = 0;
            foreach (PartDef part in room.Def.Parts)
            {
                ordinal++;
                if (!part.Saved) continue;
                GameObject prefab = SavedPrefab(part);
                if (prefab == null) continue;   // (built as scenery instead, or missing: BuildOwnRoom said so)
                try
                {
                    Vector3 at = pos + rot * part.At;
                    Quaternion turn = rot * Quaternion.Euler(part.Euler);
                    // (its creatures fitted to the land, its spawners' levels capped: BuildCreatures.cs; null when it's left out)
                    GameObject go = MakeFitting(prefab, at, turn, fit, room.Index, ordinal, LootTierOf(room), out bool replaced);
                    if (go == null) continue;
                    if (part.Scale != Vector3.one && !replaced)
                    {
                        // saved with the object when its prefab keeps its scale (ZNetView.m_syncInitialScale); otherwise only here
                        Vector3 scale = Vector3.Scale(prefab.transform.localScale, part.Scale);
                        ZNetView view = go.GetComponent<ZNetView>();
                        if (view != null) view.SetLocalScale(scale); else go.transform.localScale = scale;
                    }
                    Settle(go);
                    made++;
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Room {room.Index} ({room.Def.Name}), saved part {part.Prefab}: {e.Message}"); }
            }
            return made;
        }

        /// <summary>A Saved part's prefab: one of the game's networked prefabs. Null when it's missing, or has no ZNetView (it can't be saved:
        /// BuildOwnRoom makes it as scenery).</summary>
        internal static GameObject SavedPrefab(PartDef part)
        {
            if (!part.Saved || ZNetScene.instance == null || string.IsNullOrEmpty(part.Prefab)) return null;
            GameObject prefab = ZNetScene.instance.GetPrefab(part.Prefab);
            return prefab != null && prefab.GetComponent<ZNetView>() != null ? prefab : null;
        }

        private static readonly int PartKey = "Dungeons_part".GetStableHashCode();

        /// <summary>Every saved object the builder makes, once made: as PlaceRoom does for the game rooms' parts, a building piece is marked
        /// as standing since before the snow (ZDOVars.s_preSnow: it starts with snow on it where it snows, rather than gathering it); and it's
        /// marked as a dungeon part ("Dungeons_part"), so it can never be taken down with the hammer (KeepPart, now and whenever it loads).</summary>
        private static void Settle(GameObject go)
        {
            ZDO zdo = go.GetComponent<ZNetView>()?.GetZDO();
            if (go.GetComponent<WearNTear>() != null) zdo?.Set(ZDOVars.s_preSnow, true);
            zdo?.Set(PartKey, true);
            KeepPart(go);
        }

        /// <summary>A dungeon's saved part can't be removed with the hammer: no free build refund from a vault chest, no taking a dungeon's
        /// doors, torches or furniture apart (Player.RemovePiece refuses a Piece with m_canBeRemoved off). A prefab field: set on the object
        /// each time it's made (the ZNetView.Awake patch in BuildLoot.cs, for objects marked Dungeons_part, or Runtime's quaddungeon_partof
        /// on parts made before this mark).</summary>
        internal static void KeepPart(GameObject go)
        {
            foreach (Piece piece in go.GetComponentsInChildren<Piece>(true)) piece.m_canBeRemoved = false;
        }

        /// <summary>Whether a saved object is one of a dungeon's parts (ours, or marked by Runtime as part of a dungeon).</summary>
        internal static bool IsDungeonPart(ZDO zdo) =>
            zdo != null && (zdo.GetBool(PartKey) || !string.IsNullOrEmpty(zdo.GetString(Keys.PartOf)));

        /// <summary>A dungeon's building pieces (WearNTear: walls, floors, stairs, gates, grates, doors, chests, torches, black marble) don't
        /// take damage: hits, wear, lost support, fire all go through WearNTear.ApplyDamage (the patches below). Nobody smashes a way round a
        /// puzzle, or a chest open, or black marble for Mistlands materials in a Meadows dungeon. The game's own breakables (Destructible: pots,
        /// bone piles, roots across a crypt corridor, creep, rubble, Mórk halla's gate doors) still break as in its dungeons, their drops kept
        /// to the land's tier; so do our mechanisms' breakable walls (Runtime's DungeonBreakable, not saved, not a WearNTear).</summary>
        internal static bool Unbreakable(WearNTear piece)
        {
            ZNetView view = piece != null ? piece.GetComponent<ZNetView>() : null;
            return view != null && IsDungeonPart(view.GetZDO());
        }
    }

    // A dungeon's building pieces don't take damage (Builder.Unbreakable): a hit shows "too hard" (as a tool too weak does) and does
    // nothing; wear, lost support and fire (all through ApplyDamage) do nothing either.
    [HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
    internal static class Dungeons_WearNTear_RPC_Damage
    {
        private static bool Prefix(WearNTear __instance, HitData hit)
        {
            if (!Builder.Unbreakable(__instance)) return true;
            if (hit != null && DamageText.instance != null) DamageText.instance.ShowText(DamageText.TextType.TooHard, hit.m_point, 0f);
            return false;
        }
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
    internal static class Dungeons_WearNTear_ApplyDamage
    {
        private static bool Prefix(WearNTear __instance, ref bool __result)
        {
            if (!Builder.Unbreakable(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
