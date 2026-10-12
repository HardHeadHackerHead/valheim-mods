using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Guns
{
    /// <summary>
    /// Puts the guns into the game: builds each item and its round once, then registers them where the game looks for them, and can be
    /// asked again and again (ZNetScene and ObjectDB wake in either order, and the game swaps the item lists when a world loads: see
    /// docs/modding-pitfalls.md). The items go in through ItemReload.cs (so the mod reloads in a world); the rounds (networked prefabs, so
    /// other players see them fly) go into ZNetScene here.
    /// </summary>
    internal static class Things
    {
        private static GameObject _holder;
        private static readonly Dictionary<string, GameObject> Guns = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, GameObject> Bullets = new Dictionary<string, GameObject>();

        private static Dictionary<int, GameObject> Named(ZNetScene scene) =>
            (Dictionary<int, GameObject>)AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene);

        internal static void Register()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            if (scene == null) return;   // the guns are copied from the Arbalest, which the scene knows: ZNetScene.Awake calls this again

            if (_holder == null)
            {
                _holder = new GameObject("QuadsGunsPrefabs");
                _holder.SetActive(false);   // keeps the copies from waking up as real objects
                Object.DontDestroyOnLoad(_holder);
            }
            if (Guns.Count == 0)
            {
                try
                {
                    foreach (GunDef d in Arsenal.All) Bullets[d.Id] = Gun.BuildBullet(scene, _holder.transform, d);
                    Fx.Build(scene, _holder.transform, Gun.Glow);   // (the sounds and flashes the items' trigger effects play)
                    foreach (GameObject bullet in Bullets.Values)   // where a round lands is heard there, with its own sound
                    {
                        Projectile p = bullet != null ? bullet.GetComponent<Projectile>() : null;
                        if (p != null && Fx.Impact != null) p.m_hitEffects = Fx.Impact;
                    }
                    foreach (GunDef d in Arsenal.All)
                    {
                        GameObject item = Gun.BuildItem(scene, _holder.transform, d);
                        if (item != null) Guns[d.Id] = item;
                    }
                }
                catch (System.Exception e) { Plugin.Log.LogError("could not build the guns: " + e); return; }
                if (Guns.Count == 0) return;
            }

            foreach (GameObject bullet in Bullets.Values)
            {
                if (bullet == null) continue;
                if (!scene.m_prefabs.Contains(bullet))
                {
                    scene.m_prefabs.RemoveAll(p => p == null || p.name == bullet.name);
                    scene.m_prefabs.Add(bullet);
                }
                Named(scene)[bullet.name.GetStableHashCode()] = bullet;
            }

            ItemReload.Replace(scene, db, Guns.Values);   // by name, over an older copy's (its m_dropPrefab is set when it is built)
            if (db != null) RegisterRecipes(scene, db);
        }

        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();

        /// <summary>Each gun's recipe at the forge, put into ObjectDB by name every time (Shared/Content.cs); not craftable on a server without the mod.</summary>
        private static void RegisterRecipes(ZNetScene scene, ObjectDB db)
        {
            GameObject forge = scene.GetPrefab("forge");
            CraftingStation station = forge != null ? forge.GetComponent<CraftingStation>() : null;
            if (station == null) { Plugin.Log.LogWarning("the forge was not found, so the guns have no recipes"); return; }
            foreach (GunDef d in Arsenal.All)
            {
                if (!Guns.TryGetValue(d.Id, out GameObject gun) || gun == null) continue;
                Recipe r = DHack.Shared.Content.Recipe(gun, station, d.Station, 1, null, d.CCost.Value, Plugin.Log, db: db);
                if (r == null) continue;
                Plugin.Synced.NeedsServer(r);
                Recipes[d.Id] = r;
            }
        }

        /// <summary>The cost settings (the server's in multiplayer) on the recipes. One that can't be read keeps the last good cost.</summary>
        internal static void ApplyCosts()
        {
            foreach (GunDef d in Arsenal.All)
                if (Recipes.TryGetValue(d.Id, out Recipe r) && r != null && d.CCost != null) DHack.Shared.Costs.Apply(r, d.CCost.Value, Plugin.Log);
        }

        internal static void Unregister()
        {
            bool keep = ItemReload.KeepOnUnload;   // inside a world: the guns and their rounds stay registered and alive, the next copy replaces them
            ZNetScene scene = ZNetScene.instance;
            if (!keep && scene != null)
                foreach (GameObject bullet in Bullets.Values)
                {
                    if (bullet == null) continue;
                    scene.m_prefabs.Remove(bullet);
                    Dictionary<int, GameObject> named = Named(scene);
                    int hash = bullet.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == bullet) named.Remove(hash);   // never another copy's entry
                }
            DHack.Shared.Content.RemoveAll(keepItems: true);   // the recipes (only those still this copy's); the items are ItemReload's
            Recipes.Clear();
            if (!keep) RemoveItems();   // at the main menu: out of ObjectDB and ZNetScene, where still this copy's
            if (!keep && _holder != null) { Fx.Clear(); Object.Destroy(_holder); }
            Guns.Clear(); Bullets.Clear(); _holder = null;
        }

        /// <summary>
        /// Every gun in the local player's bag gets this copy's prefab and shared data, however it was made. ItemReload.Repoint only
        /// swaps shared data that was the old prefab's own object, and an item the game made by instantiating the prefab (the give command,
        /// a dropped one picked up) carries its own copy of it, so it would keep an old build's sound, stats and name until a restart.
        /// All our guns' shared data is the prefab's, nothing is per item, so taking the prefab's loses nothing.
        /// </summary>
        internal static int Refresh()
        {
            if (Guns.Count == 0 || Player.m_localPlayer == null) return 0;
            int n = 0;
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
            {
                GunDef d = Arsenal.Of(item);
                if (d == null || !Guns.TryGetValue(d.Id, out GameObject prefab)) continue;
                ItemDrop.ItemData.SharedData shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                if (item.m_dropPrefab == prefab && ReferenceEquals(item.m_shared, shared)) continue;
                item.m_dropPrefab = prefab;
                item.m_shared = shared;
                n++;
            }
            if (n > 0) Plugin.Log.LogInfo("refreshed " + n + " gun(s) in the bag to this version");
            return n;
        }

        private static void RemoveItems()
        {
            ZNetScene scene = ZNetScene.instance;
            ObjectDB db = ObjectDB.instance;
            foreach (GameObject gun in Guns.Values)
            {
                if (gun == null) continue;
                if (scene != null)
                {
                    scene.m_prefabs.Remove(gun);
                    Dictionary<int, GameObject> named = Named(scene);
                    int hash = gun.name.GetStableHashCode();
                    if (named.TryGetValue(hash, out GameObject current) && current == gun) named.Remove(hash);
                }
                db?.m_items.RemoveAll(i => i == null || i == gun);
            }
            if (db != null) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);
        }

        /// <summary>The gun by name, for ItemReload.Repoint: items made from an older copy get this copy's prefab.</summary>
        internal static GameObject Current(string name) => name != null && Guns.TryGetValue(name, out GameObject g) ? g : null;
    }
}
