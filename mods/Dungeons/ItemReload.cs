using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// Lets a mod that adds items be reloaded while you play (a mod maker's hot reload), with no item lost and none left on the old copy.
    /// Two things go wrong otherwise:
    ///   1. Between the old copy unloading and the new one loading, the game doesn't know the item: a dropped one in an area loading at
    ///      that moment is deleted by the host, and a chest or bag loading then drops it. So the old copy never unregisters or destroys its
    ///      item prefabs (KeepOnUnload); the new copy's Replace puts its own in their place by name, in ZNetScene and ObjectDB.
    ///   2. Items already in bags, chests and on the ground hold the old copy's prefab (m_dropPrefab) and its shared data. Nothing breaks
    ///      (the old prefab stays alive, and items are saved by name), but they show the old copy's name, weight and icon until reloaded.
    ///      Repoint gives the ones this game has loaded the new copy's.
    /// Claude Tools' template (templates/ItemReload.cs in the valheim-claude-tools repo), as it is there: keep the two the same.
    /// Here: BuildRewards.cs registers through Replace, HotReload.Settle calls Repoint, Builder.UnregisterItems leaves them on a reload.
    /// In a mod: register your items only through Replace (from ZNetScene.Awake, ObjectDB.Awake and ObjectDB.CopyOtherDB postfixes, and
    /// the plugin's Awake for a reload in a world); after registering in Awake, call Repoint; on unload, do nothing to your items when
    /// KeepOnUnload is true. Keep the item prefabs plain (the game's own components, no MonoBehaviour of yours on them). Then declare it:
    ///   [assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]
    /// modcheck gives it world only when every item it adds to ObjectDB goes through this class and it calls Repoint.
    /// </summary>
    internal static class ItemReload
    {
        private static readonly FieldInfo NamedField = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs");
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");

        /// <summary>The mod is unloading inside a world: leave its items registered and alive (the next copy replaces them).</summary>
        public static bool KeepOnUnload => ZNetScene.instance != null;

        /// <summary>Registers item prefabs in ZNetScene and ObjectDB (either may be null), replacing any earlier copy of the same name (an
        /// old copy of this mod's). Safe to call again and again.</summary>
        public static void Replace(ZNetScene scene, ObjectDB db, IEnumerable<GameObject> items)
        {
            bool changed = false;
            foreach (GameObject item in items)
            {
                if (item == null) continue;
                if (scene != null)
                {
                    scene.m_prefabs.RemoveAll(p => p != item && p != null && p.name == item.name);
                    if (!scene.m_prefabs.Contains(item)) scene.m_prefabs.Add(item);
                    ((Dictionary<int, GameObject>)NamedField.GetValue(scene))[item.name.GetStableHashCode()] = item;   // saved and dropped ones load by name
                }
                if (db != null && !db.m_items.Contains(item))
                {
                    db.m_items.RemoveAll(i => i == null || (i != item && i.name == item.name));
                    db.m_items.Add(item);
                    changed = true;
                }
            }
            if (changed) UpdateRegisters?.Invoke(db, null);   // (ObjectDB's lookups by name and by shared data)
        }

        /// <summary>
        /// Items made from an older copy's prefab point at this copy's (current: your item prefab by name, or null for names not yours):
        /// in the local player's bag, every loaded container, lying in the world, and any you pass (an item your mod holds in memory).
        /// Their shared data too, when it was the old prefab's own. Returns how many changed.
        /// </summary>
        public static int Repoint(Func<string, GameObject> current, params ItemDrop.ItemData[] more)
        {
            int n = 0;
            void One(ItemDrop.ItemData item)
            {
                GameObject was = item?.m_dropPrefab;
                if (was == null) return;
                GameObject now = current(was.name);
                if (now == null || now == was) return;
                ItemDrop.ItemData.SharedData oldShared = was.GetComponent<ItemDrop>()?.m_itemData.m_shared;
                item.m_dropPrefab = now;
                if (oldShared != null && ReferenceEquals(item.m_shared, oldShared)) item.m_shared = now.GetComponent<ItemDrop>().m_itemData.m_shared;
                n++;
            }
            if (Player.m_localPlayer != null) foreach (ItemDrop.ItemData i in Player.m_localPlayer.GetInventory().GetAllItems()) One(i);
            foreach (Container c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                if (c != null && c.GetInventory() != null) foreach (ItemDrop.ItemData i in c.GetInventory().GetAllItems()) One(i);
            foreach (ItemDrop d in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)) if (d != null) One(d.m_itemData);
            if (more != null) foreach (ItemDrop.ItemData i in more) One(i);
            return n;
        }
    }
}
