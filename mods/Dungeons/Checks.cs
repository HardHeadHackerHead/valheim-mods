using System.Collections.Generic;
using System.Linq;

namespace Dungeons
{
    /// <summary>
    /// Which of the game's things a pack names that this game doesn't have (a game update renamed one, or a pack was written with a wrong
    /// name: a lever built from "Morkhalla_MetalPillar", which isn't a networked prefab, showed as a floating knob). Prefabs are looked up
    /// in ZNetScene (where Builder.MakePart finds them), the game's rooms in DungeonDB, locations in ZoneSystem. For `dungeon check` and
    /// `dungeon state`; building logs each missing name once as well.
    /// </summary>
    internal static class Checks
    {
        /// <summary>A name the pack or look uses, of one kind (prefab, room, location), with where it's used.</summary>
        internal class Use
        {
            public string Name, Kind;
            public readonly List<string> Where = new List<string>();
        }

        /// <summary>Every name a pack uses (its rooms, their parts, its mechanisms' parts, its doors, creatures and loot, and its entrance
        /// look), the missing ones only when missingOnly.</summary>
        public static List<Use> Pack(Pack pack, bool missingOnly)
        {
            var uses = new Dictionary<string, Use>();
            void Add(string name, string kind, string where)
            {
                if (string.IsNullOrEmpty(name)) return;
                string key = kind + "|" + name;
                if (!uses.TryGetValue(key, out Use u)) uses[key] = u = new Use { Name = name, Kind = kind };
                if (u.Where.Count < 8 && !u.Where.Contains(where)) u.Where.Add(where);
            }
            foreach (RoomDef room in pack.Rooms)
            {
                if (!string.IsNullOrEmpty(room.GamePrefab)) Add(room.GamePrefab, "room", $"{pack.Name}: room {room.Name}");
                foreach (PartDef part in room.Parts) Add(part.Prefab, "prefab", $"{pack.Name}: room {room.Name}");
                foreach (MechDef mech in room.Mechs)
                    foreach (PartDef part in mech.Parts) Add(part.Prefab, "prefab", $"{pack.Name}: room {room.Name}, mechanism {mech.Id} ({mech.Kind})");
            }
            foreach (KeyValuePair<string, string> door in pack.Doors) Add(door.Value, "prefab", $"{pack.Name}: door for \"{door.Key}\" doorways");
            // creatures and loot: the plain lists and every biome's tiers; an entry is "Name", "Name:n" or "Name:min-max" (BuildLoot reads
            // it so): the name is what's checked, loot as an item (ObjectDB)
            foreach (string c in pack.Creatures) Add(EntryName(c), "prefab", $"{pack.Name}: creatures");
            foreach (string biome in PackReader.Biomes)
                foreach (string tier in PackReader.CreatureTiers(pack, biome))
                    foreach (string c in PackReader.Creatures(pack, biome, tier)) Add(EntryName(c), "prefab", $"{pack.Name}: creatures ({biome}, {tier})");
            foreach (string l in pack.Loot) Add(EntryName(l), "item", $"{pack.Name}: loot");
            foreach (string biome in PackReader.Biomes)
                foreach (string tier in LootTiers)
                    foreach (string l in PackReader.Loot(pack, biome, tier)) Add(EntryName(l), "item", $"{pack.Name}: loot ({biome}, {tier})");
            foreach (Use u in Look(EntranceLooks.Get(pack.EntranceLook), false)) foreach (string w in u.Where) Add(u.Name, u.Kind, w);
            foreach (string sound in MechSet.SoundPrefabs) Add(sound, "prefab", "the mechanisms' sounds");
            // the game's rooms the pack names but the game doesn't have are left out of the pack when it's read: its problems say so
            foreach (string problem in Packs.Problems(pack.Name).Where(p => p.Contains("the game has no room")))
            {
                string name = problem.Substring(problem.IndexOf("the game has no room") + "the game has no room".Length).Trim().Split(' ')[0];
                Add(name, "room", $"{pack.Name}: rooms");
            }
            return uses.Values.Where(u => !missingOnly || !Has(u)).OrderBy(u => u.Kind).ThenBy(u => u.Name).ToList();
        }

        /// <summary>The chests' lists BuildLoot fills from.</summary>
        private static readonly string[] LootTiers = { "common", "rare", "vault" };

        /// <summary>A creature or loot entry's prefab name: what's before ":" ("Coins:30-50" is Coins).</summary>
        private static string EntryName(string entry) => (entry ?? "").Split(':')[0].Trim();

        /// <summary>Every name an entrance look uses.</summary>
        public static List<Use> Look(EntranceLooks.Look look, bool missingOnly)
        {
            var list = new List<Use>();
            foreach (EntranceLooks.LocationPart lp in look.Locations)
                list.Add(new Use { Name = lp.Location, Kind = "location", Where = { $"entrance look {look.Name}" } });
            foreach (string prefab in look.Parts.Select(p => p.Prefab).Where(p => !string.IsNullOrEmpty(p)).Distinct())
                list.Add(new Use { Name = prefab, Kind = "prefab", Where = { $"entrance look {look.Name}" } });
            return list.Where(u => !missingOnly || !Has(u)).ToList();
        }

        /// <summary>Does this game have it?</summary>
        public static bool Has(Use u)
        {
            switch (u.Kind)
            {
                case "room": return DungeonDB.instance != null && DungeonDB.instance.GetRoom(u.Name.GetStableHashCode()) != null;
                case "item": return ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(u.Name) != null;
                case "location": return ZoneSystem.instance?.m_locations != null && ZoneSystem.instance.m_locations.Any(l => l != null && l.m_prefabName == u.Name && l.m_prefab.IsValid);
                default: return ZNetScene.instance != null && ZNetScene.instance.GetPrefab(u.Name) != null;
            }
        }
    }
}
