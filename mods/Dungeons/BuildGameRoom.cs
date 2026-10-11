using System;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // One of the game's own dungeon rooms, built the way DungeonGenerator.PlaceRoom does it: the room's RandomSpawn/RandomObject children
    // seeded from its position, then either its networked children made as saved objects (the owner, once) or the shell made with them
    // switched off (every player). Both change the room's asset for a moment, the same asset the game's own dungeons use, so everything they
    // change is put back in a finally, exactly as it was.
    internal static partial class Builder
    {
        /// <summary>A game room's shell under the dungeon's root: walls, floors, lights, props, without its networked parts. Null if the room
        /// couldn't be loaded.</summary>
        private static GameObject BuildGameRoom(Built built, PlacedRoom room, DungeonDB.RoomData data)
        {
            if (data.m_prefab.Load() != LoadResult.Succeeded)
            {
                data.m_prefab.Release();
                Plugin.Log.LogWarning($"Couldn't load the game's room {room.Def.GamePrefab}");
                return null;
            }
            Vector3 pos = WorldPos(built, room);
            Quaternion rot = WorldRot(room);
            GameObject shell = null;
            try
            {
                WithRoomAsset(data, pos, rot, RoomSeed(built.Layout, room), ThemeFor(built, data), built.Themes, (asset, views) =>
                {
                    // the networked children off, so the copy is only the shell (they are the owner's saved objects: SpawnNetworked)
                    foreach (ZNetView v in views) v.gameObject.SetActive(false);
                    // and nothing in it may become a saved object, whatever the room holds (the game relies on its rooms having no ZNetView
                    // on the root: we don't)
                    bool was = ZNetView.m_forceDisableInit;
                    ZNetView.m_forceDisableInit = true;
                    try { shell = SoftReferenceableAssets.Utils.Instantiate(data.m_prefab, pos, rot, built.Root.transform); }
                    finally { ZNetView.m_forceDisableInit = was; }
                    // nothing left in it may give items (BuildLoot.cs)
                    int stripped = shell != null ? StripLoot(shell) : 0;
                    if (stripped > 0) WarnOnce($"The game's room {data.m_prefab.Name}: {stripped} things in its shell could give items: taken off");
                });
            }
            finally { data.m_prefab.Release(); }
            if (shell != null) shell.name = $"{room.Index} {data.m_prefab.Name}";
            return shell;
        }

        /// <summary>The owner, once: a game room's networked children (spawners, pickables, roots, chests...) as saved objects, where
        /// PlaceRoom puts them, with the same RandomSpawn/RandomObject choices the shell got. started: anything may have been made.</summary>
        private static int SpawnRoomNetworked(Built built, PlacedRoom room, DungeonDB.RoomData data, CreatureFit fit, out bool started)
        {
            started = false;
            if (data.m_prefab.Load() != LoadResult.Succeeded)
            {
                data.m_prefab.Release();
                Plugin.Log.LogWarning($"Couldn't load the game's room {room.Def.GamePrefab} for its saved parts");
                return 0;
            }
            Vector3 pos = WorldPos(built, room);
            Quaternion rot = WorldRot(room);
            int made = 0;
            bool any = false;
            try
            {
                WithRoomAsset(data, pos, rot, RoomSeed(built.Layout, room), ThemeFor(built, data), built.Themes, (asset, views) =>
                {
                    Vector3 rootPos = asset.transform.position;
                    Quaternion unturn = Quaternion.Inverse(asset.transform.rotation);
                    any = true;
                    int ordinal = 0;
                    foreach (ZNetView v in views)
                    {
                        ordinal++;
                        if (!v.gameObject.activeSelf) continue;   // switched off by a RandomSpawn/RandomObject
                        Vector3 at = pos + rot * (unturn * (v.transform.position - rootPos));
                        Quaternion turn = rot * (unturn * v.transform.rotation);
                        // (its creatures fitted to the land: BuildCreatures.cs; null when it's left out)
                        GameObject go = MakeFitting(v.gameObject, at, turn, fit, room.Index, ordinal, LootTierOf(room), out bool replaced);
                        if (go == null) continue;
                        if (!replaced) go.HoldReferenceTo(data.m_prefab);   // its meshes are the room's: keep the room loaded while it exists (as the game does)
                        Settle(go);
                        made++;
                    }
                });
            }
            catch (Exception e) { Plugin.Log.LogError($"Making the saved parts of room {room.Index} ({room.Def.GamePrefab}): {e}"); }
            finally { data.m_prefab.Release(); }
            started = any;
            return made;
        }

        /// <summary>
        /// PlaceRoom's preparation of a room's asset, then use(asset, its enabled networked children), then the asset put back as it was:
        /// - its RandomSpawn and RandomObject children are run with UnityEngine.Random seeded with seed (each at its place in the world, as
        ///   PlaceRoom computes it), which switches some children off; the game's Random state is kept and put back;
        /// - afterwards the game's own Reset is called on each, and then every object in the asset gets back the active flag it had before
        ///   (Reset alone doesn't restore everything: a RandomSpawn's "off object", a networked child switched off). An exception anywhere
        ///   still puts it all back.
        /// The caller has loaded the asset (data.m_prefab.Load()) and releases it.
        /// </summary>
        private static void WithRoomAsset(DungeonDB.RoomData data, Vector3 pos, Quaternion rot, int seed, Room.Theme theme, DungeonGenerator dg,
                                          Action<GameObject, ZNetView[]> use)
        {
            GameObject asset = data.m_prefab.Asset;
            if (asset == null) throw new InvalidOperationException("the room's prefab isn't loaded: " + data.m_prefab.Name);

            // every object's active flag, before anything changes it
            Transform[] all = asset.GetComponentsInChildren<Transform>(true);
            bool[] active = new bool[all.Length];
            for (int i = 0; i < all.Length; i++) active[i] = all[i].gameObject.activeSelf;

            UnityEngine.Random.State state = UnityEngine.Random.state;
            RandomSpawn[] spawns = new RandomSpawn[0];
            RandomObject[] objects = new RandomObject[0];
            try
            {
                ZNetView[] views = global::Utils.GetEnabledComponentsInChildren<ZNetView>(asset);
                objects = global::Utils.GetEnabledComponentsInChildren<RandomObject>(asset);
                spawns = global::Utils.GetEnabledComponentsInChildren<RandomSpawn>(asset);
                foreach (RandomSpawn s in spawns) s.Prepare();
                if (dg != null) dg.m_themes = theme;

                Vector3 rootPos = asset.transform.position;
                Quaternion unturn = Quaternion.Inverse(asset.transform.rotation);
                UnityEngine.Random.InitState(seed);
                foreach (RandomSpawn s in spawns) s.Randomize(pos + rot * (unturn * (s.transform.position - rootPos)), null, dg);
                foreach (RandomObject o in objects) o.Randomize(pos + rot * (unturn * (o.transform.position - rootPos)), null, dg);

                use(asset, views);
            }
            finally
            {
                UnityEngine.Random.state = state;
                foreach (RandomSpawn s in spawns)
                {
                    try { if (s != null) s.Reset(); }
                    catch (Exception e) { Plugin.Log.LogWarning("Resetting a room's RandomSpawn: " + e.Message); }
                }
                foreach (RandomObject o in objects)
                {
                    try { if (o != null) o.Reset(); }
                    catch (Exception e) { Plugin.Log.LogWarning("Resetting a room's RandomObject: " + e.Message); }
                }
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.activeSelf != active[i]) all[i].gameObject.SetActive(active[i]);
                }
            }
        }

        private static Room.Theme ThemeFor(Built built, DungeonDB.RoomData data) => built.Theme != Room.Theme.None ? built.Theme : Lowest(data.m_theme);
    }
}
