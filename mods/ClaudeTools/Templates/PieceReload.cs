using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace YourMod
{
    /// <summary>
    /// Lets a mod that adds build pieces be reloaded while you play (a mod maker's hot reload), with nothing deleted and nothing left
    /// running the old code. Two things go wrong otherwise:
    ///   1. Between the old copy unloading and the new one loading, the game doesn't know the piece: an area loading at that moment loses
    ///      its pieces (the host deletes what it can't build). So the old copy never unregisters its prefab (UnloadPiece only takes it out
    ///      of the build menu); the new copy's Register replaces it (ReplacePrefab).
    ///   2. Pieces already standing keep the old copy's components: they run its code, and nothing of it is alive any more (pressing E does
    ///      nothing). So the new copy swaps them for its own (Reattach). Their fields start at their defaults: read what a component needs
    ///      in its Awake, not from fields set on the prefab.
    /// Pieces only. Items aren't covered: an item in a bag or chest keeps the old copy's data until the game restarts.
    /// A template from Claude Tools (BepInEx/claude/templates): copy it into your mod and change the namespace. The Bounty Board uses it.
    /// In your mod: Register calls ReplacePrefab and Reattach; your unload calls UnloadPiece (not your own removal); and the mod says so:
    ///   [assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]   (modcheck then lets it reload in a world)
    /// </summary>
    internal static class PieceReload
    {
        private static readonly FieldInfo NamedField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly FieldInfo InstancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");

        /// <summary>Register a piece prefab, replacing any earlier copy of the same name (an old copy of this mod's) in the scene and the build menu.</summary>
        public static void ReplacePrefab(ZNetScene scene, GameObject prefab, PieceTable table)
        {
            scene.m_prefabs.RemoveAll(p => p != prefab && p != null && p.name == prefab.name);
            if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
            ((Dictionary<int, GameObject>)NamedField.GetValue(scene))[prefab.name.GetStableHashCode()] = prefab;   // lets it be placed and loaded from saves
            if (table != null)
            {
                table.m_pieces.RemoveAll(p => p != prefab && p != null && p.name == prefab.name);
                if (!table.m_pieces.Contains(prefab)) table.m_pieces.Add(prefab);
            }
        }

        /// <summary>
        /// When this copy unloads: take the piece out of the build menu, but leave it registered (so no area loading in the meantime
        /// loses its pieces). The next copy replaces it; without one (the mod switched off), pieces still load until the game restarts.
        /// </summary>
        public static void UnloadPiece(GameObject prefab, PieceTable table)
        {
            if (prefab != null && table != null) table.m_pieces.Remove(prefab);
        }

        /// <summary>
        /// Give the pieces already standing in the world this copy's components, in place of an older copy's (found by type name: each
        /// copy of a mod is its own assembly). Returns how many objects were changed.
        /// </summary>
        public static int Reattach(ZNetScene scene, GameObject prefab)
        {
            if (scene == null || prefab == null) return 0;
            int hash = prefab.name.GetStableHashCode(), changed = 0;
            Assembly mine = typeof(PieceReload).Assembly;
            var instances = (Dictionary<ZDO, ZNetView>)InstancesField.GetValue(scene);
            foreach (ZNetView view in instances.Values.ToList())
            {
                if (view == null || view.GetZDO() == null || view.GetZDO().GetPrefab() != hash) continue;
                bool any = false;
                foreach (MonoBehaviour old in view.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (old == null || old.GetType().Assembly == mine) continue;
                    System.Type current = mine.GetType(old.GetType().FullName, false);   // the same class in this copy
                    if (current == null || !typeof(MonoBehaviour).IsAssignableFrom(current)) continue;
                    GameObject on = old.gameObject;
                    Object.DestroyImmediate(old);   // (now: or the hover and E would still find the old one this frame)
                    on.AddComponent(current);
                    any = true;
                }
                if (any) changed++;
            }
            return changed;
        }
    }
}
