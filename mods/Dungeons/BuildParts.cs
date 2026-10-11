using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // One of our own rooms, from its parts: the game's build pieces and props, each made without joining the network
    // (ZNetView.m_forceDisableInit, as the game makes its placement ghosts), then everything that would save it, let it be broken,
    // deconstructed or used taken off. Lights and fire stay.
    internal static partial class Builder
    {
        /// <summary>Builds one of our rooms under the dungeon's root, over frames when it's big; made gets the room's object (also when only
        /// some parts could be made).</summary>
        private static IEnumerator BuildOwnRoom(Built built, PlacedRoom room, FrameClock frame, HashSet<string> missing, Action<GameObject> made)
        {
            var go = new GameObject($"{room.Index} {room.Def.Name}");
            go.transform.SetParent(built.Root.transform, false);
            go.transform.SetPositionAndRotation(WorldPos(built, room), WorldRot(room));
            made(go);
            foreach (PartDef part in room.Def.Parts)
            {
                if (part.Saved)
                {
                    // the owner makes it once, saved (SpawnNetworked); one that can't be saved (no ZNetView) is scenery like the rest
                    if (SavedPrefab(part) != null) continue;
                    if (ZNetScene.instance != null && ZNetScene.instance.GetPrefab(part.Prefab ?? "") != null)
                        WarnOnce($"Room {room.Def.Name}: part {part.Prefab} is marked saved but isn't networked: made as scenery");
                }
                try { MakePart(part, go.transform, missing); }
                catch (Exception e) { Plugin.Log.LogWarning($"Room {room.Index} ({room.Def.Name}), part {part.Prefab}: {e.Message}"); }
                if (frame.Over)
                {
                    frame.Pause();
                    yield return null;
                    frame.Resume();
                }
            }
        }

        /// <summary>
        /// One part as stripped scenery under parent (no ZDO, nothing to break, use or save; lights and fire kept), its local position and
        /// rotation set from the part in parent's frame, its scale the prefab's times the part's. Null if the game has no such prefab
        /// (logged once per name). For the mechanisms' moving parts (Runtime) as well as our rooms' shells.
        /// </summary>
        internal static GameObject MakePart(PartDef part, Transform parent)
        {
            var missing = new HashSet<string>();
            GameObject go = MakePart(part, parent, missing);
            foreach (string name in missing) WarnOnce($"A dungeon part names a prefab this game does not have: {name}");
            return go;
        }

        private static GameObject MakePart(PartDef part, Transform room, HashSet<string> missing)
        {
            if (part == null || room == null) return null;
            GameObject prefab = ZNetScene.instance != null && !string.IsNullOrEmpty(part.Prefab) ? ZNetScene.instance.GetPrefab(part.Prefab) : null;
            if (prefab == null) { missing.Add(part.Prefab ?? "(no prefab)"); return null; }

            // as Player.SetupPlacementGhost does: no ZDO, no terrain changes (a piece that levels the ground would dig at 5000 m), and the
            // prefab's TerrainModifier switched off while copying it, then back on
            TerrainModifier tm = prefab.GetComponentInChildren<TerrainModifier>();
            bool tmWas = tm != null && tm.enabled;
            bool initWas = ZNetView.m_forceDisableInit, opsWas = TerrainOp.m_forceDisableTerrainOps;
            GameObject go;
            try
            {
                if (tm != null) tm.enabled = false;
                ZNetView.m_forceDisableInit = true;
                TerrainOp.m_forceDisableTerrainOps = true;
                go = Object.Instantiate(prefab, room.TransformPoint(part.At), room.rotation * Quaternion.Euler(part.Euler), room);
            }
            finally
            {
                ZNetView.m_forceDisableInit = initWas;
                TerrainOp.m_forceDisableTerrainOps = opsWas;
                if (tm != null) tm.enabled = tmWas;
            }
            go.name = part.Prefab;
            // set whole, in the parent's frame: a copy can come out somewhere else (the game's dungeon door stood 50 m up in step 1)
            go.transform.localPosition = part.At;
            go.transform.localRotation = Quaternion.Euler(part.Euler);
            go.transform.localScale = Vector3.Scale(prefab.transform.localScale, part.Scale);
            Strip(go);
            if (part.Scale != Vector3.one) ScaleLook(go, part.Scale);   // (lights and fire grown with it: BuildScale.cs)
            return go;
        }

        /// <summary>
        /// Takes off what makes a game object part of the game's world rather than scenery: its network view, its building piece (can't be
        /// deconstructed or repaired, doesn't count for comfort), its health (can't be broken), physics, terrain changes, and whatever can be
        /// used or hovered (a chest that would open nothing, a torch to add fuel to), and whatever would give items (a pickable, a pot that
        /// drops something when broken). Fires are left burning. Seats and ladders stay usable.
        /// Each behaviour is switched off before it's destroyed (destroying waits for the end of the frame), so nothing of it runs meanwhile
        /// and BuildLook can tell what still moves.
        /// </summary>
        internal static void Strip(GameObject go)
        {
            foreach (Fireplace f in go.GetComponentsInChildren<Fireplace>(true))
            {
                // burning, with nothing to feed (as the arena's braziers)
                if (f.m_enabledObject != null) f.m_enabledObject.SetActive(true);
                if (f.m_enabledObjectHigh != null) f.m_enabledObjectHigh.SetActive(true);
                if (f.m_enabledObjectLow != null) f.m_enabledObjectLow.SetActive(false);
                if (f.m_fullObject != null) f.m_fullObject.SetActive(true);
                if (f.m_halfObject != null) f.m_halfObject.SetActive(false);
                if (f.m_emptyObject != null) f.m_emptyObject.SetActive(false);
                // as Fireplace.Start does for one not placed by a player (it never starts here: it goes before its first frame)
                if (f.m_playerBaseObject != null) f.m_playerBaseObject.SetActive(false);
            }
            // a piece's "player base" and "private property" areas don't belong in a dungeon: the first stops creatures spawning near it,
            // the dungeon's spawners included. A no-monsters area goes whole (it lists itself in Awake); heat and fire stay (warmth, burns)
            foreach (EffectArea area in go.GetComponentsInChildren<EffectArea>(true))
            {
                area.m_type &= ~(EffectArea.Type.PlayerBase | EffectArea.Type.PrivateProperty);
                if (area.m_type == EffectArea.Type.None || (area.m_type & EffectArea.Type.NoMonsters) != 0)
                {
                    area.enabled = false;
                    Object.Destroy(area);
                }
            }
            foreach (Joint j in go.GetComponentsInChildren<Joint>(true)) Object.Destroy(j);
            foreach (Rigidbody b in go.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; Object.Destroy(b); }
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is Chair || mb is Ladder) continue;
                bool game = mb is ZNetView || mb is Piece || mb is WearNTear || mb is ZSyncTransform || mb is ZSyncAnimation || mb is Container
                            || mb is Door || mb is Fireplace || mb is ItemDrop || mb is TerrainModifier || mb is GuidePoint || mb is Aoe
                            || mb is CraftingStation || mb is StationExtension || mb is PrivateArea || mb is CreatureSpawner || mb is SpawnArea
                            || mb is StaticPhysics || mb is Floating || mb is Interactable || mb is Hoverable || mb is IDestructible
                            || mb is DropOnDestroyed || mb is LootSpawner;
                if (!game) continue;
                mb.enabled = false;
                Object.Destroy(mb);
            }
        }
    }
}
