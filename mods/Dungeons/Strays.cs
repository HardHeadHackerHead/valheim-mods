using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Strays: saved objects above 3000 m that no dungeon covers and no game interior holds (no location in their zone).
    /// They're what a removed or remade dungeon left where it no longer reaches: creatures its spawners made (untagged), items, gravestones.
    /// Every game's Safety holds such things back un-made, for ever, and a teleport into their zone waited for them: a black screen (the
    /// shortcut in Quadbobheim, 0.2.0). The server knows every entrance, and moves them down:
    /// - items, gravestones (with their death spot, the owner named in the log) and tames go to the ground in front of the nearest
    ///   entrance within reach, else straight below;
    /// - a creature a dungeon could have spawned (DungeonCreature) is removed; any other (a companion, another mod's) is moved like a tame,
    ///   never removed;
    /// - anything else (a piece someone built up there) is only counted.
    /// Things tagged with a dungeon are left to Sweep (a live dungeon's old parts, or orphans).
    /// When it runs: every 10 min over everything (Sweep.Tick); around a point when a game holds something back with no dungeon over it;
    /// after a remove or a remake (around the entrance); and with `dungeon orphans`.
    /// </summary>
    internal static class Strays
    {
        public const string Rpc = "Dungeons_Strays";   // (point, reach, delay s): to the server, a pass around the point
        private const float Spread = 16f, Above = 100f;   // a laid-out dungeon's cover around its box (as Site.Covers)

        private static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> Objects = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
        private static ZRoutedRpc _for;
        private static float _nextAsk, _nextPass;

        public static void Register()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == _for) return;
            _for = ZRoutedRpc.instance;
            try
            {
                ZRoutedRpc.instance.Register<Vector3, float, float>(Rpc, (sender, p, reach, delay) =>
                {
                    if (ZNet.instance != null && ZNet.instance.IsServer()) Later(p, Mathf.Clamp(reach, 0f, Dungeons.UnknownReach), Mathf.Clamp(delay, 0f, 30f));
                });
            }
            catch (Exception e) { Plugin.Log.LogWarning("Registering Dungeons_Strays: " + e.Message); }
        }

        /// <summary>A stray pass around a point, by the server (asked of it from a client), after a delay (a remove's entrance gone first).</summary>
        public static void Around(Vector3 p, float reach, float delay)
        {
            if (ZNet.instance == null) return;
            if (ZNet.instance.IsServer()) Later(p, reach, delay);
            else ZRoutedRpc.instance?.InvokeRoutedRPC(Rpc, p, reach, delay);
        }

        /// <summary>A game holding back something with no dungeon over it (Safety.HoldBack): the server is asked to look there, every 15 s
        /// at most.</summary>
        public static void Held(Vector3 p)
        {
            if (Time.time < _nextAsk) return;
            _nextAsk = Time.time + 15f;
            Around(p, 64f, 0f);
        }

        private static void Later(Vector3 p, float reach, float delay)
        {
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Passes(p, reach, delay));
        }

        private static IEnumerator Passes(Vector3 p, float reach, float delay)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            for (int tries = 0; tries < 6; tries++)
            {
                if (delay <= 0f && Time.time < _nextPass) yield return new WaitForSecondsRealtime(_nextPass - Time.time);
                _nextPass = Time.time + 5f;
                bool unsure;
                try { unsure = Pass(Near(p, reach), true, out _); }
                catch (Exception e) { Plugin.Log.LogWarning("Moving stray dungeon objects: " + e.Message); yield break; }
                if (!unsure) yield break;
                yield return new WaitForSecondsRealtime(10f);   // (a dungeon near it still being laid out here: once it is)
            }
        }

        /// <summary>The server, every 10 minutes and for `dungeon orphans`: a pass over everything.</summary>
        public static JObject All(bool act)
        {
            Pass(ZDOMan.instance != null ? Objects(ZDOMan.instance).Values.ToList() : new List<ZDO>(), act, out JObject report);
            return report;
        }

        private static List<ZDO> Near(Vector3 p, float reach) =>
            ZDOMan.instance == null ? new List<ZDO>()
                : Objects(ZDOMan.instance).Values.Where(z => z != null && z.GetPosition().y >= 3000f && Dungeons.FlatDistance(z.GetPosition(), p) <= reach).ToList();

        private struct Cover
        {
            public string Id;
            public Vector3 Entrance;
            public bool Known;   // laid out: its box; else everything high up within UnknownReach of its entrance
            public Bounds Box;
        }

        /// <summary>
        /// The strays among these saved objects: moved down (act), and reported. Unsure: some lay within reach of a dungeon this game is
        /// still laying out (left this time: try again).
        /// </summary>
        private static bool Pass(List<ZDO> zdos, bool act, out JObject report)
        {
            report = new JObject();
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null) return false;
            bool unsure = false;
            var covers = new List<Cover>();
            var waiting = new List<Vector3>();
            foreach (ZDO e in Dungeons.EntranceZdos().ToList())
            {
                var c = new Cover { Id = e.GetString(Keys.Id), Entrance = e.GetPosition() };
                if (e.GetVec3(Keys.Origin, out Vector3 origin))
                {
                    Layout layout = Dungeons.LayoutSoon(e.GetString(Keys.Pack), e.GetInt(Keys.Seed), out _, out bool wait);
                    if (layout != null) { c.Known = true; c.Box = new Bounds(origin + layout.Box.center, layout.Box.size); }
                    else if (wait) waiting.Add(c.Entrance);
                }
                covers.Add(c);   // (not laid out, or can't be: covers all within reach, never touched)
            }
            var moved = new Dictionary<string, int>();
            var removed = new Dictionary<string, int>();
            var left = new Dictionary<string, int>();
            foreach (ZDO z in zdos)
            {
                if (z == null || !z.IsValid()) continue;
                Vector3 p = z.GetPosition();
                if (p.y < 3000f || Safety.Transient(z.GetPrefab()) || Sweep.DungeonOf(z).Length > 0 || Entrance.IsEntrance(z.GetPrefab())) continue;
                GameObject prefab = ZNetScene.instance.GetPrefab(z.GetPrefab());
                if (prefab == null || prefab.GetComponent<Player>() != null) continue;
                if (covers.Any(c => Covered(c, p)) || GameInterior(p)) continue;
                if (waiting.Any(w => Dungeons.FlatDistance(w, p) < Dungeons.UnknownReach)) { unsure = true; continue; }
                string name = prefab.name;
                bool creature = prefab.GetComponent<Character>() != null, grave = prefab.GetComponent<TombStone>() != null;
                if (creature && !z.GetBool(ZDOVars.s_tamed) && DungeonCreature(name))
                {
                    if (!act || Sweep.Destroy(z)) Count(removed, name);
                    continue;
                }
                if (!creature && !grave && prefab.GetComponent<ItemDrop>() == null) { Count(left, name); continue; }
                Count(moved, name);
                if (!act) continue;
                Vector3 to = Down(covers, p);
                Rebuild.Place(z, to, grave);
                if (grave) Plugin.Log.LogWarning($"{z.GetString(ZDOVars.s_ownerName)}'s gravestone was high up at {p} with no dungeon there: moved down to {to}");
                else if (creature) Plugin.Log.LogWarning($"{name} (not a dungeon's creature, or tame) was high up at {p} with no dungeon there: moved down to {to}");
            }
            report["moved"] = Named(moved);
            report["removed"] = Named(removed);
            report["left"] = Named(left);
            if (unsure) report["unsure"] = "some lie near a dungeon still being laid out here: looked at again in 10 s";
            int n = moved.Values.Sum() + removed.Values.Sum();
            if (act && n > 0)
                Plugin.Log.LogWarning($"Stray dungeon objects (high up, no dungeon there any more): {moved.Values.Sum()} moved down ({string.Join(", ", moved.Select(kv => kv.Key + " x" + kv.Value))}), " +
                                      $"{removed.Values.Sum()} creatures removed ({string.Join(", ", removed.Select(kv => kv.Key + " x" + kv.Value))})");
            return unsure;
        }

        private static bool Covered(Cover c, Vector3 p)
        {
            if (!c.Known) return Dungeons.FlatDistance(p, c.Entrance) < Dungeons.UnknownReach;
            Bounds b = c.Box;
            return p.x > b.min.x - Spread && p.x < b.max.x + Spread && p.z > b.min.z - Spread && p.z < b.max.z + Spread && p.y < b.max.y + Above;
        }

        /// <summary>One of the game's own interiors could be there: a location in its zone (the game builds an interior over its location's
        /// zone centre, 64 m across: Location.Awake). The same rule as Safety's hold. On the server, its placed locations too.</summary>
        public static bool GameInterior(Vector3 p)
        {
            if (Location.GetZoneLocation(p) != null) return true;
            return ZoneSystem.instance != null && ZoneSystem.instance.m_locationInstances.ContainsKey(ZoneSystem.GetZone(p));
        }

        /// <summary>Where a stray goes: the ground in front of the nearest entrance within reach (spread out), else the ground below it.</summary>
        private static Vector3 Down(List<Cover> covers, Vector3 p)
        {
            Cover nearest = covers.Where(c => !string.IsNullOrEmpty(c.Id) && Dungeons.FlatDistance(c.Entrance, p) < Dungeons.UnknownReach)
                                  .OrderBy(c => Dungeons.FlatDistance(c.Entrance, p)).FirstOrDefault();
            if (nearest.Id != null && Rebuild.OutsideOf(nearest.Id, out Vector3 outside, out Vector3 outward)) return Rebuild.NextSpot(nearest.Id, outside, outward);
            return Rebuild.Ground(new Vector3(p.x, -1000f, p.z), -1000f);
        }

        // ---- which creatures are a dungeon's

        private static HashSet<string> _dungeonCreatures;
        private static int _creaturesFor = -1;
        private static float _creaturesAgain;

        /// <summary>
        /// A creature a dungeon could have spawned, so one that may be removed: in a pack's creature lists (every biome and tier: encounters'
        /// roles and bosses' adds come from them too), or in the builder's table of the game's creatures (what our rooms' spawners are fitted
        /// to, and the game rooms' own: Draugr, Ghost...). Anything else (a companion, another mod's creature) is never removed by us. While
        /// a pack can't be read yet the list is short, so fewer are removed, never more.
        /// </summary>
        public static bool DungeonCreature(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            if (_dungeonCreatures == null || _creaturesFor != PackSync.Generation || (_creaturesAgain > 0f && Time.time >= _creaturesAgain)) MakeCreatures();
            return _dungeonCreatures.Contains(prefabName);
        }

        private static void MakeCreatures()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AccessTools.Field(typeof(Builder), "Creatures")?.GetValue(null) is IDictionary table)
                foreach (object key in table.Keys) if (key is string n) names.Add(n);
            bool all = true;
            foreach (string packName in Packs.Names.ToList())
            {
                Pack pack;
                try { pack = Packs.Get(packName); }
                catch (Exception) { pack = null; }
                if (pack == null) { all = false; continue; }
                foreach (string c in pack.Creatures) names.Add(EntryName(c));
                foreach (string biome in PackReader.Biomes)
                    foreach (string tier in PackReader.CreatureTiers(pack, biome))
                        foreach (string c in PackReader.Creatures(pack, biome, tier)) names.Add(EntryName(c));
            }
            names.Remove("");
            _dungeonCreatures = names;
            _creaturesFor = PackSync.Generation;
            _creaturesAgain = all ? 0f : Time.time + 30f;   // (a pack not readable yet: again in a while)
        }

        private static string EntryName(string entry) => (entry ?? "").Split(':')[0].Trim();

        private static void Count(Dictionary<string, int> d, string name) => d[name] = d.TryGetValue(name, out int n) ? n + 1 : 1;

        private static JObject Named(Dictionary<string, int> d) =>
            new JObject(d.OrderByDescending(kv => kv.Value).Select(kv => new JProperty(kv.Key, kv.Value)));
    }
}
