using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Saved objects of a dungeon wherever they are, loaded or not. A dungeon is bigger than what's loaded round anyone (up to 320 m), so
    /// removing only the loaded parts on a reset or a remove left the rest standing where no room is any more: spawners in the air whose
    /// skeletons fell. The server (or host) knows every ZDO: it goes through all of them (ZDOMan's list), finds this dungeon's by their
    /// tags (Keys.PartOf, and for a remove the encounter creatures' MechSet.EncounterTag), and destroys them through ZDOMan (taking them
    /// over first). A client asks the server (Dungeons_Sweep). A loaded one is removed as RemoveParts does (a chest's contents dropped
    /// first); an unloaded chest that still holds something is left alone and counted (nobody loses what's in it).
    ///
    /// Orphans: saved objects tagged with a dungeon whose entrance no longer exists. `dungeon orphans [remove]` lists or removes them, and
    /// the host removes them by itself every 10 minutes while a dungeon is known.
    /// </summary>
    internal static class Sweep
    {
        public const string Rpc = "Dungeons_Sweep";   // (dungeon id, keep generation or -1 for everything, with creatures 1/0): to the server
        private static readonly int PartOfHash = Keys.PartOf.GetStableHashCode();
        private static readonly int PartGenHash = Keys.PartGen.GetStableHashCode();
        private static readonly int EncHash = MechSet.EncounterTag.GetStableHashCode();
        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> Objects = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
        private static float _nextOrphans = 600f;

        /// <summary>The dungeon id a saved object belongs to (a part, or an encounter's creature), or "".</summary>
        public static string DungeonOf(ZDO z)
        {
            string part = z.GetString(PartOfHash);
            if (!string.IsNullOrEmpty(part)) return part;
            string enc = z.GetString(EncHash);
            if (string.IsNullOrEmpty(enc)) return "";
            int bar = enc.IndexOf('|');
            return bar > 0 ? enc.Substring(0, bar) : enc;
        }

        private static List<ZDO> All() => ZDOMan.instance != null ? Objects(ZDOMan.instance).Values.ToList() : new List<ZDO>();

        /// <summary>A dungeon's saved objects everywhere: on the server now, else asked of it. keepGen >= 0: parts of that generation stay
        /// (a reset or a remake: the new ones). creatures: its encounters' creatures too (a remove).</summary>
        public static void Everywhere(string id, int keepGen, bool creatures)
        {
            if (string.IsNullOrEmpty(id) || ZNet.instance == null) return;
            if (ZNet.instance.IsServer()) { Run(id, keepGen, creatures); return; }
            ZRoutedRpc.instance?.InvokeRoutedRPC(Rpc, id, keepGen, creatures);
        }

        private static ZRoutedRpc _for;

        public static void Register()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _for) return;
            _for = ZRoutedRpc.instance;
            try { ZRoutedRpc.instance.Register<string, int, bool>(Rpc, (sender, id, keep, creatures) => { if (ZNet.instance != null && ZNet.instance.IsServer()) Run(id, keep, creatures); }); }
            catch (Exception e) { Plugin.Log.LogWarning("Registering Dungeons_Sweep: " + e.Message); }
        }

        private static void Run(string id, int keepGen, bool creatures)
        {
            int removed = 0, kept = 0;
            foreach (ZDO z in All())
            {
                if (z == null || !z.IsValid()) continue;
                bool part = z.GetString(PartOfHash) == id;
                bool creature = creatures && DungeonOf(z) == id && !part;
                if (!part && !creature) continue;
                if (part && keepGen >= 0 && z.GetInt(PartGenHash, 0) == keepGen) continue;
                if (Destroy(z)) removed++; else kept++;
            }
            if (removed > 0 || kept > 0)
                Plugin.Log.LogInfo($"Dungeon {id}: {removed} of its saved objects removed wherever they were{(kept > 0 ? $", {kept} left (chests still holding something, or in use)" : "")}");
        }

        /// <summary>Removes one saved object (the server): loaded, through ZNetScene; not loaded, through ZDOMan. A chest holding something
        /// is emptied out to its dungeon's entrance first (Rebuild.EmptyOut); one in use, or whose contents can't be read, is left. False
        /// when it's left.</summary>
        public static bool Destroy(ZDO z)
        {
            ZNetView view = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(z) : null;
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(z.GetPrefab()) : null;
            if (view != null)
            {
                Container chest = view.GetComponent<Container>();
                if (chest != null)
                {
                    if (z.GetInt(ZDOVars.s_inUse) == 1) return false;
                    view.ClaimOwnership();
                    if (chest.GetInventory().NrOfItems() > 0 && !Rebuild.EmptyOut(DungeonOf(z), z, chest.GetInventory())) return false;
                }
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
                return true;
            }
            Container template = prefab != null ? prefab.GetComponent<Container>() : null;
            if (template != null)
            {
                Inventory saved = Saved(z, template);
                if (saved == null) return false;   // (can't read it: kept)
                if (saved.NrOfItems() > 0 && (z.GetInt(ZDOVars.s_inUse) == 1 || !Rebuild.EmptyOut(DungeonOf(z), z, saved))) return false;
            }
            z.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(z);
            return true;
        }

        /// <summary>An unloaded chest's saved contents (empty when it has none); null when they can't be read.</summary>
        private static Inventory Saved(ZDO z, Container template)
        {
            var inv = new Inventory("quaddungeon_check", null, Math.Max(1, template.m_width), Math.Max(1, template.m_height));
            byte[] saved = z.GetByteArray(ZDOVars.s_items);   // (the game keeps it as bytes, not a string)
            if (saved == null || saved.Length == 0) return inv;
            try { inv.Load(new ZPackage(saved)); return inv; }
            catch (Exception) { return null; }
        }

        // ---- orphans

        /// <summary>The server: saved objects tagged with a dungeon whose entrance isn't in the world any more, by dungeon (and removed when
        /// remove is set).</summary>
        public static JObject Orphans(bool remove)
        {
            var live = new HashSet<string>();
            foreach (ZDO z in All())
                if (z != null && Entrance.IsEntrance(z.GetPrefab())) { string id = z.GetString(Keys.Id); if (!string.IsNullOrEmpty(id)) live.Add(id); }
            var by = new Dictionary<string, Dictionary<string, int>>();
            int removed = 0, kept = 0, found = 0;
            foreach (ZDO z in All())
            {
                if (z == null || !z.IsValid()) continue;
                string id = DungeonOf(z);
                if (id.Length == 0 || live.Contains(id)) continue;
                found++;
                string name = ZNetScene.instance?.GetPrefab(z.GetPrefab())?.name ?? z.GetPrefab().ToString();
                if (!by.TryGetValue(id, out Dictionary<string, int> names)) by[id] = names = new Dictionary<string, int>();
                names[name] = names.TryGetValue(name, out int n) ? n + 1 : 1;
                if (!remove) continue;
                if (Destroy(z)) removed++; else kept++;
            }
            JObject strays = Strays.All(remove);   // (untagged things high up where no dungeon is: Strays.cs)
            if (remove && found > 0) Plugin.Log.LogInfo($"Orphaned dungeon objects (their entrance is gone): {removed} removed{(kept > 0 ? $", {kept} chests still holding something left" : "")}");
            return new JObject
            {
                ["dungeonsInWorld"] = live.Count,
                ["strays"] = strays,
                ["orphans"] = found,
                ["byDungeon"] = new JObject(by.Select(kv => new JProperty(kv.Key, new JObject(kv.Value.OrderByDescending(x => x.Value).Select(x => new JProperty(x.Key, x.Value)))))),
                ["removed"] = remove ? removed : (JToken)null,
                ["left"] = remove && kept > 0 ? kept : (JToken)null,
            };
        }

        /// <summary>The host, every 10 minutes while a dungeon is known here: orphans removed.</summary>
        public static void Tick()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Time.time < _nextOrphans) return;
            _nextOrphans = Time.time + 600f;
            if (Dungeons.Sites.Count == 0 && Dungeons.EntranceZdos().Count == 0) return;   // (a dedicated server has no sites: any entrance in the world)
            try { Orphans(true); }
            catch (Exception e) { Plugin.Log.LogWarning("Removing orphaned dungeon objects: " + e.Message); }
        }
    }
}
