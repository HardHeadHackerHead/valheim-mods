using System.Collections.Generic;
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

        /// <summary>As PlaceRoom does for the game rooms' parts: a building piece is marked as standing since before the snow
        /// (ZDOVars.s_preSnow: it starts with snow on it where it snows, rather than gathering it).</summary>
        private static void Settle(GameObject go)
        {
            if (go.GetComponent<WearNTear>() != null) go.GetComponent<ZNetView>()?.GetZDO()?.Set(ZDOVars.s_preSnow, true);
        }
    }
}
