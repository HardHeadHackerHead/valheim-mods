using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SoftReferenceableAssets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    // How a built dungeon looks: the pack's environment over all of it (an EnvZone, as the game's interiors have), and its still meshes
    // batched together.
    internal static partial class Builder
    {
        // ---- the environment

        /// <summary>What the game's interior box is made of (Location.m_interiorPrefab: a trigger box with an EnvZone, scaled to 64 x 500 x 64
        /// over the location's zone): read once from the game's own prefab, so ours is on the same layer and behaves the same.</summary>
        private class Interior
        {
            public int Layer;
            public Vector3 Size = Vector3.one, Center;   // its BoxCollider at scale 1
            public bool Force = true;                    // EnvZone.m_force
            public bool Body;                            // it has a (kinematic) Rigidbody
            public bool FromGame;
        }
        private static Interior _interior;

        private const float EnvMargin = 2f;      // the box reaches this far past the rooms on every side

        private static readonly FieldInfo AllLocations = AccessTools.Field(typeof(Location), "s_allLocations");

        /// <summary>Finds the game's interior prefab, cheaply: from a location standing in the world (Location's own list), or one whose prefab
        /// is already loaded. Otherwise what step 1 read from the game (InteriorEnvironmentZoneForce: a unit box on the character_trigger layer,
        /// forced), without loading a location for it. Step 1 searched every object in memory (Resources.FindObjectsOfTypeAll) and could load
        /// and let go a whole location: both stall a frame. Once a session; kept an IEnumerator so the build can wait on it.</summary>
        private static IEnumerator FindInterior()
        {
            if (_interior != null) yield break;
            try
            {
                if (AllLocations?.GetValue(null) is List<Location> standing)
                    foreach (Location loc in standing)
                    {
                        if (loc == null || !loc.m_hasInterior || loc.m_interiorPrefab == null) continue;
                        _interior = ReadInterior(loc.m_interiorPrefab);
                        if (_interior != null) yield break;
                    }
                if (ZoneSystem.instance != null)
                    foreach (ZoneSystem.ZoneLocation zl in ZoneSystem.instance.m_locations)
                    {
                        if (zl == null || !zl.m_prefab.IsValid || !zl.m_prefab.IsLoaded || zl.m_prefab.Asset == null) continue;
                        Location loc = zl.m_prefab.Asset.GetComponent<Location>();
                        if (loc == null || !loc.m_hasInterior || loc.m_interiorPrefab == null) continue;
                        _interior = ReadInterior(loc.m_interiorPrefab);
                        if (_interior != null) yield break;
                    }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Looking for the game's interior box: " + e.Message); }

            int layer = LayerMask.NameToLayer("character_trigger");
            _interior = new Interior { Layer = layer >= 0 ? layer : 0, Force = true };
            Plugin.Log.LogInfo("The game's interior box isn't loaded: the dungeon's environment uses the one step 1 read (character_trigger, forced)");
        }

        private static Interior ReadInterior(GameObject prefab)
        {
            if (prefab.GetComponent<EnvZone>() == null) return null;
            BoxCollider box = prefab.GetComponent<BoxCollider>();
            var found = new Interior
            {
                Layer = prefab.layer,
                Force = prefab.GetComponent<EnvZone>().m_force,
                Body = prefab.GetComponent<Rigidbody>() != null,
                FromGame = true,
            };
            if (box != null) { found.Size = box.size; found.Center = box.center; }
            Plugin.Log.LogInfo($"The game's interior box: {prefab.name}, layer {LayerMask.LayerToName(prefab.layer)}, collider {(box != null ? box.size.ToString() : "none")}, force {found.Force}");
            return found;
        }

        /// <summary>An EnvZone box over the layout's box (plus a margin), with the pack's environment.</summary>
        private static EnvZone MakeEnvironment(Built built)
        {
            Interior recipe = _interior ?? new Interior();
            Bounds box = BoxOf(built.Layout);
            string env = string.IsNullOrEmpty(built.Layout.Pack?.Environment) ? "Crypt" : built.Layout.Pack.Environment;
            if (EnvMan.instance != null && !EnvMan.instance.m_environments.Any(e => e.m_name == env))
                Plugin.Log.LogWarning($"The pack's environment \"{env}\" isn't one of the game's");

            var go = new GameObject("quaddungeon_environment") { layer = recipe.Layer };
            go.transform.SetParent(built.Root.transform, false);
            go.transform.position = built.Origin + box.center;
            // the game scales a unit box (64 x 500 x 64); ours is sized with the collider instead, scale 1
            BoxCollider c = go.AddComponent<BoxCollider>();
            c.isTrigger = true;
            c.size = box.size + Vector3.one * (2f * EnvMargin);
            if (recipe.Body) { Rigidbody b = go.AddComponent<Rigidbody>(); b.isKinematic = true; b.useGravity = false; }
            EnvZone zone = go.AddComponent<EnvZone>();
            zone.m_environment = env;
            zone.m_force = recipe.Force;
            return zone;
        }

        /// <summary>The layout's box, or one around its rooms if the layout didn't set it.</summary>
        internal static Bounds BoxOf(Layout layout)
        {
            if (layout.Box.size != Vector3.zero) return layout.Box;
            Bounds? all = null;
            foreach (PlacedRoom r in layout.Rooms)
            {
                if (r.Def == null) continue;
                Vector3 s = r.Def.Size;
                bool sideways = Mathf.Abs(Mathf.DeltaAngle(r.Yaw, 90f)) < 1f || Mathf.Abs(Mathf.DeltaAngle(r.Yaw, -90f)) < 1f;
                var b = new Bounds(r.Pos, sideways ? new Vector3(s.z, s.y, s.x) : s);
                if (all == null) all = b; else { Bounds a = all.Value; a.Encapsulate(b); all = a; }
            }
            return all ?? new Bounds(Vector3.zero, new Vector3(16f, 8f, 16f));
        }

        private static readonly FieldInfo EnvZoneTriggered = AccessTools.Field(typeof(EnvZone), "s_triggered");

        /// <summary>Destroying an EnvZone with the player in it never tells it they left (no OnTriggerExit): the game would keep the dungeon's
        /// environment forced outside. Do what OnTriggerExit does.</summary>
        private static void LeaveEnvironment(Built built)
        {
            EnvZone zone = built.Environment;
            built.Environment = null;
            if (zone == null || EnvZoneTriggered == null) return;
            if (!ReferenceEquals(EnvZoneTriggered.GetValue(null), zone)) return;
            EnvZoneTriggered.SetValue(null, null);
            if (zone.m_force && EnvMan.instance != null) EnvMan.instance.SetForceEnvironment("");
        }

        // ---- static batching

        // behaviours that never move what's under them (the game's rooms have them on the root and around their props)
        private static readonly HashSet<Type> Still = new HashSet<Type>
        {
            typeof(Room), typeof(RoomConnection), typeof(RandomSpawn), typeof(RandomObject), typeof(LightLod), typeof(LightFlicker), typeof(ReferenceHolder),
        };

        /// <summary>How many objects go into one StaticBatchingUtility.Combine: a hall of a few hundred parts is combined in several, over
        /// frames.</summary>
        private const int BatchChunk = 48;

        /// <summary>Puts a room's still meshes together (StaticBatchingUtility, with the room as the root, so the room can still be moved
        /// whole). Only meshes Unity can read (it skips the others: many of the game's), only switched-on renderers, and nothing with an
        /// animation, a moving body or any running behaviour other than the still ones above it. In chunks, yielding when the frame's budget
        /// is spent (the caller stops if the dungeon is removed meanwhile). The combined meshes are ours to destroy. count gets each chunk's
        /// number of objects.</summary>
        private static IEnumerator Batch(Built built, GameObject room, FrameClock frame, Action<int> count)
        {
            var gos = new List<GameObject>();
            var before = new HashSet<Mesh>();
            foreach (MeshRenderer r in room.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
                if (Moves(r.transform, room.transform)) continue;
                gos.Add(r.gameObject);
                before.Add(f.sharedMesh);
            }
            if (gos.Count < 2) yield break;
            for (int i = 0; i < gos.Count; i += BatchChunk)
            {
                if (i > 0 && frame.Over)
                {
                    frame.Pause();
                    yield return null;
                    frame.Resume();
                }
                GameObject[] chunk = gos.Skip(i).Take(BatchChunk).Where(g => g != null).ToArray();
                if (chunk.Length < 2) continue;
                StaticBatchingUtility.Combine(chunk, room);
                foreach (GameObject g in chunk)
                {
                    Mesh m = g.GetComponent<MeshFilter>().sharedMesh;
                    if (m != null && !before.Contains(m) && !built.Owned.Contains(m)) built.Owned.Add(m);
                }
                count(chunk.Length);
            }
        }

        private static bool Moves(Transform t, Transform room)
        {
            for (; t != null; t = t.parent)
            {
                Rigidbody body = t.GetComponent<Rigidbody>();
                if (body != null && !body.isKinematic) return true;
                foreach (Behaviour b in t.GetComponents<Behaviour>())
                {
                    if (b == null || !b.enabled) continue;
                    // (Animator and Animation by name: their module isn't one this mod references)
                    if (b is MonoBehaviour ? !Still.Contains(b.GetType()) : b.GetType().Name == "Animator" || b.GetType().Name == "Animation") return true;
                }
                if (t == room) break;
            }
            return false;
        }
    }
}
