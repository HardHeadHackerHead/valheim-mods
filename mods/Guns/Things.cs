using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Puts the guns into the game: builds the item and its projectile once, then registers them where the game looks for them, and can be
    /// asked again and again (ZNetScene and ObjectDB wake in either order, and the game swaps the item lists when a world loads: see
    /// docs/modding-pitfalls.md). The item goes in through ItemReload.cs (so the mod reloads in a world); the projectile (a networked prefab, so other players see
    /// it fly) goes into ZNetScene here.
    /// </summary>
    internal static class Things
    {
        private static GameObject _holder;
        private static GameObject _gun, _bullet;

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null) return;   // the gun is copied from the Arbalest, which the scene knows: ZNetScene.Awake calls this again

            if (_holder == null)
            {
                _holder = new GameObject("QuadsGunsPrefabs");
                _holder.SetActive(false);   // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
            }
            if (_gun == null)
            {
                try
                {
                    _bullet = MachineGun.BuildBullet(scene, _holder.transform);
                    Fx.Build(scene, _holder.transform, MachineGun.Glow);   // (the sound and flash the item's trigger effect plays)
                    _gun = MachineGun.BuildItem(scene, _holder.transform);
                }
                catch (System.Exception e) { Plugin.Log.LogError("could not build the Machine Gun: " + e); return; }
                if (_gun == null) return;
            }

            if (_bullet != null && !scene.m_prefabs.Contains(_bullet))
            {
                scene.m_prefabs.RemoveAll(p => p == null || p.name == _bullet.name);
                scene.m_prefabs.Add(_bullet);
            }
            if (_bullet != null) Named(scene)[_bullet.name.GetStableHashCode()] = _bullet;

            ItemReload.Replace(scene, db, new[] { _gun });   // by name, over an older copy's (its m_dropPrefab is set when it is built)
            // No recipe while the gun is being tested (`give dh_machinegun`): a recipe in ObjectDB needs the main menu to reload. It comes back with
            // a cost (Costs.cs) before release.
        }

        internal static void Unregister()
        {
            bool keep = ItemReload.KeepOnUnload;   // inside a world: the gun and its round stay registered and alive, the next copy replaces them
            ZNetScene scene = ZNetScene.instance;
            if (!keep && scene != null && _bullet != null)
            {
                scene.m_prefabs.Remove(_bullet);
                Dictionary<int, GameObject> named = Named(scene);
                int hash = _bullet.name.GetStableHashCode();
                if (named.TryGetValue(hash, out GameObject current) && current == _bullet) named.Remove(hash);   // never another copy's entry
            }
            if (!keep) RemoveItem();   // at the main menu: out of ObjectDB and ZNetScene, where still this copy's
            if (!keep && _holder != null) { Fx.Clear(); Object.Destroy(_holder); }
            _gun = null; _bullet = null; _holder = null;
        }

        /// <summary>
        /// Every Machine Gun in the local player's bag gets this copy's prefab and shared data, however it was made. ItemReload.Repoint only
        /// swaps shared data that was the old prefab's own object, and an item the game made by instantiating the prefab (the give command,
        /// a dropped one picked up) carries its own copy of it, so it would keep an old build's sound, stats and name until a restart.
        /// All our guns' shared data is the prefab's, nothing is per item, so taking the prefab's loses nothing.
        /// </summary>
        internal static int Refresh()
        {
            if (_gun == null || Player.m_localPlayer == null) return 0;
            ItemDrop.ItemData.SharedData shared = _gun.GetComponent<ItemDrop>().m_itemData.m_shared;
            int n = 0;
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
            {
                if (!MachineGun.Is(item) || (item.m_dropPrefab == _gun && ReferenceEquals(item.m_shared, shared))) continue;
                item.m_dropPrefab = _gun;
                item.m_shared = shared;
                n++;
            }
            if (n > 0) Plugin.Log.LogInfo("refreshed " + n + " Machine Gun(s) in the bag to this version");
            return n;
        }

        private static void RemoveItem()
        {
            if (_gun == null) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene != null)
            {
                scene.m_prefabs.Remove(_gun);
                Dictionary<int, GameObject> named = Named(scene);
                int hash = _gun.name.GetStableHashCode();
                if (named.TryGetValue(hash, out GameObject current) && current == _gun) named.Remove(hash);
            }
            ObjectDB db = ObjectDB.instance;
            if (db != null)
            {
                db.m_items.RemoveAll(i => i == null || i == _gun);
                AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);
            }
        }

        /// <summary>The gun by name, for ItemReload.Repoint: items made from an older copy get this copy's prefab.</summary>
        internal static GameObject Current(string name) => name == MachineGun.Prefab ? _gun : null;
    }
}
