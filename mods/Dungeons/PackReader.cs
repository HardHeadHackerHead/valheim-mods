using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// Reads a pack from its JSON (the format is at the top of Packs.cs), with the game's rooms filled in by whoever calls it: from DungeonDB
    /// in the game (Packs.cs), from the catalogue file outside it (tools/dungeons). No game types here, so the offline harness compiles it.
    /// </summary>
    internal static class PackReader
    {
        // What a pack has that Model.cs doesn't (the layout's own business), kept beside the pack.
        private sealed class Extra
        {
            public Vector3 MaxSize;
            public int MinUses;
            public bool Parkour = true;
            public bool ParkourSaid;   // the pack says "parkour": true itself (not left to the default)
            public string EntranceCap;
            public ShortcutSpec Shortcut;
            public readonly Dictionary<string, MechDef> Gates = new Dictionary<string, MechDef>();
            public readonly Dictionary<PlanStep, string> From = new Dictionary<PlanStep, string>();
            public readonly Dictionary<string, List<Join>> Drops = new Dictionary<string, List<Join>>();   // room name -> its drops
            public readonly Dictionary<string, HashSet<string>> JoinBy = new Dictionary<string, HashSet<string>>();   // room name -> doorway ids
            // "creatures" and "loot" by biome: biome (lower case; "default" for the rest) -> tier ("melee", "common", "rare"...) -> names
            public readonly Dictionary<string, Dictionary<string, List<string>>> Creatures = new Dictionary<string, Dictionary<string, List<string>>>();
            public readonly Dictionary<string, Dictionary<string, List<string>>> Loot = new Dictionary<string, Dictionary<string, List<string>>>();
        }
        private static readonly ConditionalWeakTable<Pack, Extra> Extras = new ConditionalWeakTable<Pack, Extra>();

        /// <summary>The kinds of mechanism the mod knows (MechDef.Kind).</summary>
        public static readonly string[] Kinds = { "lever", "plate", "gate", "door", "platform", "stair", "rotator", "floor", "machine", "exit", "encounter", "breakable", "shortcut",
            "pedestal", "brazier", "pushblock", "trap", "teleporter", "lock", "checkpoint" };   // (the last seven: dungeon-triggers' blocks)

        /// <summary>The pack's size limit for a whole dungeon (x, y, z in metres), or zero for none ("maxSize" in its JSON).</summary>
        public static Vector3 MaxSize(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) ? e.MaxSize : Vector3.zero;

        /// <summary>The fewest uses of levers and plates a layout's puzzle may be solved in ("minUses"; 0: any, even none).</summary>
        public static int MinUses(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) ? e.MinUses : 0;

        /// <summary>The gate the layout puts in a locked doorway of this type ("gates": {type: mechanism}, its parts and poses in the
        /// doorway's frame), or null if the pack has none for it. A pattern: the layout copies it.</summary>
        public static MechDef Gate(Pack pack, string type) =>
            pack != null && Extras.TryGetValue(pack, out Extra e) && e.Gates.TryGetValue(type ?? "", out MechDef m) ? m : null;

        /// <summary>The earlier plan step a step's room grows from ("from": a hub, several steps off one hall), or null: the step before it.</summary>
        public static string From(Pack pack, PlanStep step) =>
            pack != null && step != null && Extras.TryGetValue(pack, out Extra e) && e.From.TryGetValue(step, out string from) ? from : null;

        /// <summary>A room's one-way ways through it ("drops": a jump off a balcony to the floor below): from A to B, never back. By the
        /// room's name, so a placed room's own copy of its RoomDef finds them too. Empty if none.</summary>
        public static List<Join> Drops(Pack pack, RoomDef room) =>
            pack != null && room != null && Extras.TryGetValue(pack, out Extra e) && e.Drops.TryGetValue(room.Name ?? "", out List<Join> d) ? d : NoJoins;
        private static readonly List<Join> NoJoins = new List<Join>();

        /// <summary>Whether a room may be placed joined by this doorway of its own ("joinBy": the only doorway ids it's attached by, e.g. a
        /// hall entered on its ground floor; other doorways still take rooms placed after it). By the room's name.</summary>
        public static bool JoinBy(Pack pack, RoomDef room, Doorway doorway) =>
            pack == null || room == null || !Extras.TryGetValue(pack, out Extra e) || !e.JoinBy.TryGetValue(room.Name ?? "", out HashSet<string> ids)
            || ids.Contains(doorway.Id);

        /// <summary>The game's biomes (Heightmap.Biome's names) "creatures" may be keyed by, and "default" for any other.</summary>
        public static readonly string[] Biomes = { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Ocean", "Mistlands", "AshLands", "DeepNorth", "default" };

        /// <summary>The creatures a pack's dungeon in this biome spawns, of one tier ("common", "strong"...): the biome's own list, else
        /// "default"'s, else the plain list (a plain "creatures" list counts as every biome's "common"). Empty if it has none.</summary>
        public static List<string> Creatures(Pack pack, string biome, string tier = "common") =>
            pack != null && Extras.TryGetValue(pack, out Extra e) ? Lookup(e.Creatures, pack.Creatures, biome, tier) : new List<string>();

        /// <summary>The loot a pack's chests in this biome hold, of one tier ("common", "rare", "vault"...), the same way as Creatures:
        /// the biome's own list, else "default"'s, else the plain list (as "common"). Each entry as written ("Coins", "Coins:10-30",
        /// "Coins:5"): the builder reads the stack. Empty if none.</summary>
        public static List<string> Loot(Pack pack, string biome, string tier = "common") =>
            pack != null && Extras.TryGetValue(pack, out Extra e) ? Lookup(e.Loot, pack.Loot, biome, tier) : new List<string>();

        private static List<string> Lookup(Dictionary<string, Dictionary<string, List<string>>> table, List<string> plain, string biome, string tier)
        {
            foreach (string key in new[] { (biome ?? "").ToLowerInvariant(), "default" })
                if (table.TryGetValue(key, out Dictionary<string, List<string>> tiers))
                    return tiers.TryGetValue((tier ?? "common").ToLowerInvariant(), out List<string> list) ? list : new List<string>();
            return string.Equals(tier ?? "common", "common", StringComparison.OrdinalIgnoreCase) ? plain : new List<string>();
        }

        /// <summary>The tiers a pack names for a biome (or "default"), e.g. "common", "strong".</summary>
        public static IEnumerable<string> CreatureTiers(Pack pack, string biome)
        {
            if (pack == null || !Extras.TryGetValue(pack, out Extra e)) return Enumerable.Empty<string>();
            foreach (string key in new[] { (biome ?? "").ToLowerInvariant(), "default" })
                if (e.Creatures.TryGetValue(key, out Dictionary<string, List<string>> tiers)) return tiers.Keys.ToList();
            return pack.Creatures.Count > 0 ? new[] { "common" } : Enumerable.Empty<string>();
        }

        /// <summary>Whether parkour (wall runs, climbing, ledge hangs: Quad's Parkour) is allowed inside ("parkour": false turns it off, so
        /// a puzzle can't be climbed round). True when the pack doesn't say.</summary>
        public static bool Parkour(Pack pack) => pack == null || !Extras.TryGetValue(pack, out Extra e) || e.Parkour;

        /// <summary>Whether the pack itself says "parkour": true (a puzzle in it must then hold with Quad's Parkour too).</summary>
        public static bool ParkourSaid(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) && e.ParkourSaid;

        /// <summary>The end cap the entrance room's way in takes ("entranceCap": a room's name), or null: any plain one (flat, nothing in it).</summary>
        public static string EntranceCap(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) ? e.EntranceCap : null;

        /// <summary>How an encounter (a fight room's waves: MechEncounter.cs) starts: "enter" (a player walks into its area), "wire" (a
        /// lever's or plate's "start" wire; the default) or "open" (its chest opened). Null if the mechanism isn't an encounter.</summary>
        public static string StartOn(Pack pack, RoomDef room, MechDef m) =>
            m == null || m.Kind != "encounter" ? null : m.Encounter?.StartOn ?? "wire";

        /// <summary>A pack's shortcut ("shortcut"): its two end-cap rooms and the plan step the far end goes by.</summary>
        internal sealed class ShortcutSpec { public string Near, Far, FarStep; }

        /// <summary>The pack's shortcut, or null.</summary>
        public static ShortcutSpec Shortcut(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) ? e.Shortcut : null;

        /// <summary>A shortcut mechanism's end ("near", or "far": MechDef.Deep) and its pair (MechDef.Pair), or null if it isn't one.</summary>
        public static string ShortcutEnd(Pack pack, RoomDef room, MechDef m, out string pair)
        {
            pair = m != null && m.Kind == "shortcut" ? m.Pair : null;
            return pair == null ? null : m.Deep ? "far" : "near";
        }

        /// <summary>A room only reached by breaking a wall (tag "secret"): a side branch, never on the way, never needed.</summary>
        public static bool Secret(RoomDef room) => room != null && room.Tags.Any(t => string.Equals(t, "secret", StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether a room holds an end of a shortcut (it's placed only as the shortcut, never as an ordinary cap or room).</summary>
        public static bool HasShortcut(RoomDef room) => room != null && room.Mechs.Any(m => m.Kind == "shortcut");

        /// <summary>Whether the pack has a lock gate for any doorway type.</summary>
        public static bool HasGates(Pack pack) => pack != null && Extras.TryGetValue(pack, out Extra e) && e.Gates.Count > 0;

        /// <summary>A pack from its JSON. gameRoom gives the game's room by prefab name (its size, doorways and flags), or null if the game
        /// has none by that name. Whatever is wrong goes into problems; a room that can't be read is left out, the rest still works.</summary>
        public static Pack Read(JObject json, Func<string, RoomDef> gameRoom, List<string> problems)
        {
            var pack = new Pack
            {
                Name = (string)json["name"],
                Title = (string)json["title"] ?? (string)json["name"],
                Environment = (string)json["environment"] ?? "Crypt",
                MinRooms = (int?)json["minRooms"] ?? 6,
                MaxRooms = (int?)json["maxRooms"] ?? 12,
                Theme = (string)json["theme"],
                EntranceLook = (string)json["entranceLook"],
                DoorChance = (float?)json["doorChance"] ?? 0.5f,
            };
            if (string.IsNullOrEmpty(pack.Name)) { problems.Add("a pack has no name"); pack.Name = "unnamed"; }
            if (pack.MaxRooms < pack.MinRooms) { problems.Add($"{pack.Name}: maxRooms is less than minRooms"); pack.MaxRooms = pack.MinRooms; }
            var extra = new Extra
            {
                MaxSize = json["maxSize"] is JArray max ? Vec(max) : Vector3.zero,
                MinUses = (int?)json["minUses"] ?? 0,
                Parkour = (bool?)json["parkour"] ?? true,
                ParkourSaid = (bool?)json["parkour"] == true,
                EntranceCap = (string)json["entranceCap"],
            };
            pack.Music = (string)json["music"];
            if (json["shortcut"] is JObject sc)
                extra.Shortcut = new ShortcutSpec { Near = (string)sc["near"], Far = (string)sc["far"], FarStep = (string)sc["farStep"] };
            Extras.Add(pack, extra);
            if (json["doors"] is JObject doors)
                foreach (JProperty d in doors.Properties())
                    if (!string.IsNullOrEmpty((string)d.Value)) pack.Doors[d.Name] = (string)d.Value;
            if (json["gates"] is JObject gates)
                foreach (JProperty g in gates.Properties())
                {
                    if (!(g.Value is JObject gj)) continue;
                    string where = $"{pack.Name}: the gate for doorways of type \"{g.Name}\"";
                    MechDef gate = ReadMech(gj, problems, where);
                    gate.Id = "lockgate";
                    if (string.IsNullOrEmpty(gate.Kind)) gate.Kind = "gate";
                    if (!Layouts.IsGate(gate)) problems.Add($"{where} is a {gate.Kind} (only a gate or a door closes a doorway)");
                    if (gate.Poses.Count < 2) problems.Add($"{where} needs two poses or more (closed, open)");
                    extra.Gates[g.Name] = gate;
                }

            foreach (JObject r in (json["rooms"] as JArray ?? new JArray()).OfType<JObject>())
            {
                RoomDef def = ReadRoom(r, gameRoom, problems, pack.Name);
                if (def == null) continue;
                if (pack.Rooms.Any(o => o.Name == def.Name)) { problems.Add($"{pack.Name}: two rooms are called {def.Name} (give one a \"name\")"); continue; }
                pack.Rooms.Add(def);
                var drops = Joins(r["drops"]).ToList();
                foreach (Join j in drops)
                    if (!def.Doorways.Any(d => d.Id == j.A) || !def.Doorways.Any(d => d.Id == j.B))
                        problems.Add($"{pack.Name}: {def.Name}: a drop from {j.A} to {j.B}, but the room has no doorway {(def.Doorways.Any(d => d.Id == j.A) ? j.B : j.A)}");
                if (drops.Count > 0) extra.Drops[def.Name] = drops;
                var joinBy = new HashSet<string>(Strings(r["joinBy"]));
                foreach (string id in joinBy.Where(id => !def.Doorways.Any(d => d.Id == id)))
                    problems.Add($"{pack.Name}: {def.Name}: \"joinBy\" names doorway {id}, which the room doesn't have");
                if (joinBy.Count > 0) extra.JoinBy[def.Name] = joinBy;
            }
            BossReader.CheckPack(pack, problems);   // (dungeon-boss: the final bosses the hardest fights in the pack, PackReaderBoss.cs)
            foreach (JObject s in (json["plan"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var step = new PlanStep { Tag = (string)s["tag"], Id = (string)s["id"], OpensWith = (string)s["opensWith"] };
                pack.Plan.Add(step);
                if (!string.IsNullOrEmpty((string)s["from"])) extra.From[step] = (string)s["from"];
            }
            ReadByBiome(json["creatures"], "creatures", pack, pack.Creatures, extra.Creatures, problems);
            ReadByBiome(json["loot"], "loot", pack, pack.Loot, extra.Loot, problems);
            Check(pack, problems);
            TriggerReader.Pack(json, pack, problems);   // (dungeon-triggers: key items, and what the solver can't model)
            return pack;
        }

        private static RoomDef ReadRoom(JObject r, Func<string, RoomDef> gameRoom, List<string> problems, string packName)
        {
            string game = (string)r["game"];
            RoomDef def;
            if (!string.IsNullOrEmpty(game))
            {
                RoomDef found = gameRoom(game);
                if (found == null) { problems.Add($"{packName}: the game has no room {game} (renamed in an update?): left out"); return null; }
                def = found;
                def.GamePrefab = game;
                def.Name = (string)r["name"] ?? game;
            }
            else
            {
                def = new RoomDef { Name = (string)r["name"] };
                if (string.IsNullOrEmpty(def.Name)) { problems.Add($"{packName}: a room has neither \"game\" nor \"name\": left out"); return null; }
                def.Size = r["size"] is JArray size ? Vec(size) : Vector3.zero;
                foreach (JObject d in (r["doorways"] as JArray ?? new JArray()).OfType<JObject>())
                    def.Doorways.Add(new Doorway
                    {
                        Id = (string)d["id"] ?? def.Doorways.Count.ToString(),
                        Type = (string)d["type"] ?? "",
                        At = d["at"] is JArray at ? Vec(at) : Vector3.zero,
                        Facing = d["facing"] is JArray f ? Vec(f) : Vector3.forward,
                        Entrance = (bool?)d["entrance"] ?? false,
                        AllowDoor = (bool?)d["door"] ?? true,
                    });
                def.Parts.AddRange(Parts(r["parts"]));
            }
            // Mechanisms, and which doorways are joined through the room (the game's rooms may have them too).
            foreach (JObject m in (r["mechs"] as JArray ?? new JArray()).OfType<JObject>())
                def.Mechs.Add(ReadMech(m, problems, $"{packName}: {def.Name}"));
            def.Inside.AddRange(Joins(r["inside"]));
            def.Music = (string)r["music"];
            // The pack's own say over the game's (or the defaults).
            if (r["weight"] != null) def.Weight = (float)r["weight"];
            if (r["max"] != null) def.Max = (int)r["max"];
            if (r["minDepth"] != null) def.MinDepth = (int)r["minDepth"];
            if (r["entrance"] != null) def.Entrance = (bool)r["entrance"];
            if (r["endCap"] != null) def.EndCap = (bool)r["endCap"];
            def.Tags.AddRange(Strings(r["tags"]).Where(t => !def.Tags.Contains(t)));

            for (int i = 0; i < def.Doorways.Count; i++)
                if (Layouts.Quadrant(def.Doorways[i].Facing) < 0)
                    problems.Add($"{packName}: {def.Name} doorway {i} faces {def.Doorways[i].Facing} (not along an axis): it never joins");
            if (def.Doorways.Count == 0) problems.Add($"{packName}: {def.Name} has no doorways: it never joins");
            TriggerReader.Room(r, def, problems, packName);   // (dungeon-triggers: the room's "triggers", parts' "show")
            CheckMechs(def, problems, packName);
            return def;
        }

        private static IEnumerable<PartDef> Parts(JToken t) => (t as JArray ?? new JArray()).OfType<JObject>().Select(p => new PartDef
        {
            Prefab = (string)p["prefab"],
            At = p["at"] is JArray at ? Vec(at) : Vector3.zero,
            Euler = p["euler"] is JArray e ? Vec(e) : Vector3.zero,
            Scale = p["scale"] is JArray s ? Vec(s) : Vector3.one,
            Saved = (bool?)p["saved"] ?? false,
            Spin = p["spin"] is JArray spin ? Vec(spin) : Vector3.zero,   // (a mechanism's part: degrees a second it turns while moving)
        }).ToList();

        private static IEnumerable<Join> Joins(JToken t) => (t as JArray ?? new JArray()).OfType<JObject>().Select(j => new Join
        {
            State = (int?)j["state"] ?? -1,
            A = (string)j["a"],
            B = (string)j["b"],
        }).ToList();

        /// <summary>One mechanism (the JSON names are MechDef's, in camelCase).</summary>
        private static MechDef ReadMech(JObject m, List<string> problems, string where)
        {
            var mech = new MechDef
            {
                Id = (string)m["id"],
                Kind = ((string)m["kind"])?.ToLowerInvariant(),
                At = m["at"] is JArray at ? Vec(at) : Vector3.zero,
                Euler = m["euler"] is JArray e ? Vec(e) : Vector3.zero,
                Start = (int?)m["start"] ?? 0,
                Seconds = (float?)m["seconds"] ?? 1.5f,
                Cycle = (float?)m["cycle"] ?? 0f,
                Doorway = (string)m["doorway"],
                ReachedFrom = (string)m["reachedFrom"],
                Once = (bool?)m["once"] ?? false,
                Health = (float?)m["health"] ?? 0f,
                Pair = (string)m["pair"],
                Deep = (bool?)m["deep"] ?? (string)m["end"] == "far",   // ("end": "far" is the same)
            };
            if (m["cycleIn"] is JArray cycleIn) mech.CycleIn = cycleIn.Where(t => t.Type == JTokenType.Integer).Select(t => (int)t).ToList();
            if (m["spinScale"] is JArray spinScale) mech.SpinScale = spinScale.Where(t => t.Type == JTokenType.Integer || t.Type == JTokenType.Float).Select(t => (float)t).ToList();
            mech.Parts.AddRange(Parts(m["parts"]));
            foreach (JObject p in (m["poses"] as JArray ?? new JArray()).OfType<JObject>())
                mech.Poses.Add(new Pose { At = p["at"] is JArray pa ? Vec(pa) : Vector3.zero, Euler = p["euler"] is JArray pe ? Vec(pe) : Vector3.zero });
            foreach (JObject w in (m["wires"] as JArray ?? new JArray()).OfType<JObject>())
                mech.Wires.Add(new Wire { Target = (string)w["target"], Do = (string)w["do"] ?? "next" });
            mech.Joins.AddRange(Joins(m["joins"]));
            if (mech.Kind == "encounter")
            {
                var enc = new EncounterDef
                {
                    StartOn = (string)m["startOn"] ?? "wire",
                    Chest = m["chest"] is JArray chest ? Vec(chest) : Vector3.zero,
                    Level = (int?)m["level"] ?? 1,
                    EliteLevel = (int?)m["eliteLevel"] ?? 3,
                    Pause = (float?)m["pause"] ?? 4f,
                    Timeout = (float?)m["timeout"] ?? 40f,
                    Siege = (int?)m["siege"] ?? 0,
                    HoldBy = (string)m["holdBy"],
                    LockLever = (string)m["lockLever"],
                    Music = (string)m["music"],
                };
                if (m["boss"] is JObject b) enc.Boss = BossReader.Read(b, m);   // (the boss toolkit's fields: PackReaderBoss.cs)
                if (m["area"] is JArray area) enc.Area = Vec(area);
                enc.Lock.AddRange(Strings(m["lock"]));
                foreach (JObject w in (m["waves"] as JArray ?? new JArray()).OfType<JObject>())
                    enc.Waves.Add(w.Properties().Where(p => p.Value.Type == JTokenType.Integer).ToDictionary(p => p.Name, p => (int)p.Value));
                foreach (JArray p in (m["points"] as JArray ?? new JArray()).OfType<JArray>()) enc.Points.Add(Vec(p));
                mech.Encounter = enc;
            }
            if (mech.Cycle < 0f) { problems.Add($"{where} mechanism {mech.Id}: a cycle below 0 (taken as none)"); mech.Cycle = 0f; }
            TriggerReader.Block(m, mech, problems, where);   // (dungeon-triggers' blocks: before the states are counted)
            return mech;
        }

        /// <summary>What can't work in a room's mechanisms: unknown kinds, doorways and targets, states out of range.</summary>
        private static void CheckMechs(RoomDef def, List<string> problems, string packName)
        {
            string where = $"{packName}: {def.Name}";
            bool Door(string id) => def.Doorways.Any(d => d.Id == id);
            foreach (PartDef p in def.Parts.Where(p => p.Spin != Vector3.zero))
                problems.Add($"{where}: part {p.Prefab} has \"spin\", but spin only turns in a mechanism's parts (a room's parts are fixed scenery)");
            foreach (Join j in def.Inside)
                if (!Door(j.A) || !Door(j.B)) problems.Add($"{where}: \"inside\" joins {j.A} and {j.B}, but the room has no doorway {(!Door(j.A) ? j.A : j.B)}");
            var ids = new HashSet<string>();
            foreach (MechDef m in def.Mechs)
            {
                if (string.IsNullOrEmpty(m.Id)) { problems.Add($"{where}: a mechanism has no \"id\""); continue; }
                string me = $"{where} mechanism {m.Id}";
                if (m.Id.Contains(".") || m.Id.Contains(":")) problems.Add($"{me}: an id can't hold '.' or ':' (keys are \"<room>.<id>\")");
                if (!ids.Add(m.Id)) problems.Add($"{where}: two mechanisms are called {m.Id}");
                if (!Kinds.Contains(m.Kind)) problems.Add($"{me}: unknown kind {m.Kind ?? "(none)"} (one of {string.Join(", ", Kinds)})");
                int states = Layouts.States(m);
                if (Layouts.IsGate(m))
                {
                    if (!string.IsNullOrEmpty(m.Doorway) && !Door(m.Doorway)) problems.Add($"{me}: closes doorway {m.Doorway}, which the room doesn't have");
                }
                else if (!string.IsNullOrEmpty(m.Doorway) && !Door(m.Doorway)) problems.Add($"{me}: names doorway {m.Doorway}, which the room doesn't have");
                if (!string.IsNullOrEmpty(m.ReachedFrom) && !Door(m.ReachedFrom)) problems.Add($"{me}: is reached from doorway {m.ReachedFrom}, which the room doesn't have");
                foreach (Join j in m.Joins)
                    if (!Door(j.A) || !Door(j.B)) problems.Add($"{me}: joins {j.A} and {j.B} in state {j.State}, but the room has no doorway {(!Door(j.A) ? j.A : j.B)}");
                if (m.Start < 0 || m.Start >= states) problems.Add($"{me}: starts in state {m.Start}, but it has {states} (0 to {states - 1})");
                if (m.Poses.Count > 0 && m.Poses.Count < states && !(m.SpinScale != null && m.Poses.Count == 1))   // (one pose: it only spins, by state)
                    problems.Add($"{me}: has {m.Poses.Count} poses for {states} states");
                if (m.Wires.Count > 0 && !Layouts.IsUsable(m) && m.Kind != "encounter") problems.Add($"{me}: has wires, but only levers, plates and encounters act");
                if (m.Once && !Layouts.IsUsable(m)) problems.Add($"{me}: \"once\" is for levers and plates");
                if (m.Kind == "shortcut" && string.IsNullOrEmpty(m.Pair)) problems.Add($"{me}: a shortcut needs its \"pair\"");
                if (m.Kind == "breakable")
                {
                    // (a "doorway" is optional: a wall in front of a stash closes none; a secret room's way in is checked by Secret)
                    if (m.Health <= 0f) problems.Add($"{me}: a breakable needs its \"health\" (the damage it takes to break)");
                    if (m.Poses.Count > 0 && m.Poses.Count != 2) problems.Add($"{me}: a breakable has two poses (standing, broken), not {m.Poses.Count}");
                }
                if (m.Encounter != null)
                {
                    EncounterDef e = m.Encounter;
                    if (e.StartOn != "enter" && e.StartOn != "wire" && e.StartOn != "open") problems.Add($"{me}: startOn {e.StartOn} (\"enter\", \"wire\" or \"open\")");
                    if (e.StartOn == "open" && e.Chest == Vector3.zero) problems.Add($"{me}: starts when its chest is opened, but has no \"chest\"");
                    if (e.StartOn == "wire" && !def.Mechs.Any(o => o.Wires.Any(w => w.Do == "start" && w.Target == m.Id)))
                        problems.Add($"{me}: starts on a wire, but nothing in its room is wired to \"start\" it");
                    foreach (string id in e.Lock.Where(id => !def.Mechs.Any(o => o.Id == id))) problems.Add($"{me}: locks {id}, which isn't a mechanism in its room");
                    if (e.HoldBy != null && !def.Mechs.Any(o => o.Id == e.HoldBy && o.Kind == "plate")) problems.Add($"{me}: is held by {e.HoldBy}, which isn't a plate in its room");
                    if (e.LockLever != null && !def.Mechs.Any(o => o.Id == e.LockLever && o.Kind == "lever")) problems.Add($"{me}: lockLever {e.LockLever} isn't a lever in its room");
                    foreach (string role in e.Waves.SelectMany(w => w.Keys).Distinct().Where(r => r != "melee" && r != "ranged" && r != "flying" && r != "elite"))
                        problems.Add($"{me}: a wave has role {role} (melee, ranged, flying or elite)");
                    if (e.Waves.Count > 0 && e.Points.Count == 0) problems.Add($"{me}: has waves but no \"points\" for them to come at");
                    if (e.Boss != null) BossReader.Check(def, m, e, problems, me);   // (PackReaderBoss.cs)
                    foreach (string id in e.Lock)
                    {
                        MechDef l = def.Mechs.FirstOrDefault(o => o.Id == id);
                        if (l != null && !Layouts.IsGate(l) && l.Kind != "platform") problems.Add($"{me}: locks {id}, a {l.Kind} (a lock is a gate, door or platform)");
                    }
                    // Its points: inside its area (a box round its At, turned with it) and inside the room's box (GapCheck: on a floor).
                    Vector3 half = e.Area * 0.5f, room = def.Size * 0.5f;
                    foreach (Vector3 p in e.Points)
                    {
                        Vector3 local = PlacedRoom.Turn(p - m.At, -m.Euler.y);
                        if (Math.Abs(local.x) > half.x + 0.5f || Math.Abs(local.y) > half.y + 0.5f || Math.Abs(local.z) > half.z + 0.5f)
                            problems.Add($"{me}: point {p} is outside its area");
                        if (def.Size != Vector3.zero && (Math.Abs(p.x) > room.x || Math.Abs(p.y) > room.y || Math.Abs(p.z) > room.z))
                            problems.Add($"{me}: point {p} is outside the room");
                    }
                }
                if (m.Once && m.Wires.Any(w => w.Do == "next" || w.Do == "hold"))
                    problems.Add($"{me}: is used \"once\", but a wire does \"{m.Wires.First(w => w.Do == "next" || w.Do == "hold").Do}\" (a one-shot lever sets: \"set:n\")");
                if (m.CycleIn != null && m.Cycle <= 0f) problems.Add($"{me}: has \"cycleIn\" but no \"cycle\"");
                if (m.CycleIn != null && m.CycleIn.Any(c => c < 0 || c >= states)) problems.Add($"{me}: cycleIn names a state it hasn't (0 to {states - 1})");
                if (m.SpinScale != null && m.SpinScale.Count == 0) problems.Add($"{me}: an empty \"spinScale\"");
                if (m.Kind == "machine" && (m.Joins.Count > 0 || !string.IsNullOrEmpty(m.Doorway) || (states > 1 && (m.SpinScale == null || m.SpinScale.Count < states))))
                    problems.Add($"{me}: a machine only turns (no joins, no doorway; more than one state only with a \"spinScale\" for each): make it a rotator or stair to move between states");
                foreach (Wire w in m.Wires)
                {
                    if (string.IsNullOrEmpty(w.Target)) { problems.Add($"{me}: a wire has no \"target\""); continue; }
                    MechDef target = def.Mechs.FirstOrDefault(o => o.Id == w.Target);
                    if (!w.Target.StartsWith("step:") && target == null)
                        problems.Add($"{me}: is wired to {w.Target}, which isn't a mechanism in its room (or \"step:<plan step id>\")");
                    int to = -1;
                    bool set = w.Do != null && w.Do.StartsWith("set:") && int.TryParse(w.Do.Substring(4), out to) && to >= 0;
                    if (w.Do == "hold") { if (m.Kind != "plate") problems.Add($"{me}: \"hold\" is for plates"); }
                    else if (w.Do == "start") { if (target != null && target.Kind != "encounter") problems.Add($"{me}: \"start\" is for encounters, and {w.Target} is a {target.Kind}"); }
                    else if (w.Do != "next" && !set) problems.Add($"{me}: unknown wire \"do\": {w.Do} (\"next\", \"set:<n>\", \"hold\" or \"start\")");
                    if (target != null && set && to >= Layouts.States(target))
                        problems.Add($"{me}: sets {w.Target} to state {to}, but it has {Layouts.States(target)}");
                }
            }
        }

        /// <summary>"creatures" or "loot": a list (every biome's "common"), or by biome: {"Meadows": {"melee": [...], "ranged": [...]},
        /// ..., "default": {...}}, where a biome's value may also be a plain list (its "common"). all gets every entry it names.</summary>
        private static void ReadByBiome(JToken t, string field, Pack pack, List<string> all, Dictionary<string, Dictionary<string, List<string>>> table,
            List<string> problems)
        {
            if (t == null || t.Type == JTokenType.Null) return;
            if (t is JArray list) { all.AddRange(Strings(list)); return; }
            if (!(t is JObject byBiome)) { problems.Add($"{pack.Name}: \"{field}\" is neither a list nor by biome"); return; }
            foreach (JProperty b in byBiome.Properties())
            {
                string biome = Biomes.FirstOrDefault(x => string.Equals(x, b.Name, StringComparison.OrdinalIgnoreCase));
                if (biome == null) { problems.Add($"{pack.Name}: \"{field}\" has {b.Name}, which isn't a biome ({string.Join(", ", Biomes)})"); continue; }
                var tiers = new Dictionary<string, List<string>>();
                if (b.Value is JArray plain) tiers["common"] = Strings(plain).ToList();
                else if (b.Value is JObject named)
                    foreach (JProperty tier in named.Properties()) tiers[tier.Name.ToLowerInvariant()] = Strings(tier.Value).ToList();
                else { problems.Add($"{pack.Name}: \"{field}\" {b.Name} is neither a list nor tiers"); continue; }
                table[biome.ToLowerInvariant()] = tiers;
                foreach (string name in tiers.Values.SelectMany(x => x))
                    if (!all.Contains(name)) all.Add(name);
            }
        }

        /// <summary>What can't work in a pack: the plan's tags and keys, entrances, end caps for every doorway type.</summary>
        private static void Check(Pack pack, List<string> problems)
        {
            if (!pack.Rooms.Any(r => r.Entrance && r.Doorways.Any(d => d.Entrance)))
                problems.Add($"{pack.Name}: no entrance room (a room marked entrance with a doorway marked entrance)");
            var ids = new List<string>();
            foreach (PlanStep step in pack.Plan)
            {
                string name = Layouts.StepName(step);
                if (string.IsNullOrEmpty(name)) problems.Add($"{pack.Name}: a plan step has no tag");
                else if (!pack.Rooms.Any(r => r.Tags.Any(t => string.Equals(t, step.Tag, StringComparison.OrdinalIgnoreCase))))
                    problems.Add($"{pack.Name}: no room has the tag {step.Tag} (plan step {name})");
                string from = From(pack, step);
                if (from != null && !ids.Contains(from)) problems.Add($"{pack.Name}: plan step {name} grows from {from}, which isn't an earlier step");
                if (!string.IsNullOrEmpty(step.OpensWith) && !ids.Contains(step.OpensWith))
                    problems.Add($"{pack.Name}: plan step {name} opens with {step.OpensWith}, which isn't an earlier step");
                if (ids.Contains(name)) problems.Add($"{pack.Name}: two plan steps are called {name} (give one an \"id\")");
                ids.Add(name);
                // A lock needs a gate (in the step's room, or the pack's for its doorway type), and its key step a lever or plate.
                if (!string.IsNullOrEmpty(step.OpensWith) && !HasGates(pack) && !pack.Rooms.Any(r => HasTag(r, step.Tag) && r.Mechs.Any(m => Layouts.IsGate(m) && !string.IsNullOrEmpty(m.Doorway))))
                    problems.Add($"{pack.Name}: plan step {name} is locked, but no {step.Tag} room has a gate and the pack has no \"gates\"");
                PlanStep key = pack.Plan.FirstOrDefault(p => Layouts.StepName(p) == step.OpensWith);
                if (key != null && !pack.Rooms.Any(r => HasTag(r, key.Tag) && r.Mechs.Any(Layouts.IsUsable)))
                    problems.Add($"{pack.Name}: plan step {name} opens with {step.OpensWith}, but no {key.Tag} room has a lever or plate");
            }
            // A wire to a plan step: the step must be in the plan.
            foreach (RoomDef r in pack.Rooms)
                foreach (MechDef m in r.Mechs)
                    foreach (Wire w in m.Wires.Where(w => w.Target != null && w.Target.StartsWith("step:")))
                    {
                        string step = w.Target.Substring(5).Split('.')[0];
                        if (!ids.Contains(step)) problems.Add($"{pack.Name}: {r.Name} mechanism {m.Id} is wired to {w.Target}, but the plan has no step {step}");
                    }
            foreach (string type in pack.Doors.Keys)
                if (!pack.Rooms.Any(r => r.Doorways.Any(d => (d.Type ?? "") == type)))
                    problems.Add($"{pack.Name}: \"doors\" has a door for doorway type \"{type}\", which no room has");
            // The shortcut: two rooms of the pack, each with one end of the same pair, and a plan step for the far end.
            ShortcutSpec spec = Shortcut(pack);
            if (spec != null)
            {
                string pairOf(string room, string end)
                {
                    RoomDef r = pack.Rooms.FirstOrDefault(x => x.Name == room);
                    if (r == null) { problems.Add($"{pack.Name}: the shortcut's {end} room {room ?? "(none)"} isn't one of its rooms"); return null; }
                    foreach (MechDef m in r.Mechs)
                        if (ShortcutEnd(pack, r, m, out string pair) == end) return pair;
                    problems.Add($"{pack.Name}: the shortcut's {end} room {room} has no shortcut mechanism with \"end\": \"{end}\"");
                    return null;
                }
                string a = pairOf(spec.Near, "near"), b = pairOf(spec.Far, "far");
                if (a != null && b != null && a != b) problems.Add($"{pack.Name}: the shortcut's ends are of different pairs ({a}, {b})");
                if (!pack.Plan.Any(p => Layouts.StepName(p) == spec.FarStep)) problems.Add($"{pack.Name}: the shortcut's farStep {spec.FarStep ?? "(none)"} isn't a plan step");
            }
            // Secret rooms: entered through a breakable wall, never a plan step.
            foreach (RoomDef r in pack.Rooms.Where(Secret))
                if (!r.Mechs.Any(m => m.Kind == "breakable" && !string.IsNullOrEmpty(m.Doorway)))
                    problems.Add($"{pack.Name}: {r.Name} is secret, but has no breakable with the \"doorway\" it's entered by");
            foreach (PlanStep step in pack.Plan.Where(s => string.Equals(s.Tag, "secret", StringComparison.OrdinalIgnoreCase)))
                problems.Add($"{pack.Name}: plan step {Layouts.StepName(step)} asks for a secret room (nothing may need one)");
            string cap = EntranceCap(pack);
            if (cap != null && !pack.Rooms.Any(r => r.Name == cap && r.EndCap))
                problems.Add($"{pack.Name}: entranceCap {cap} isn't one of its end caps");
            if (pack.DoorChance < 0f || pack.DoorChance > 1f) problems.Add($"{pack.Name}: doorChance {pack.DoorChance} isn't between 0 and 1");
            // A cap joins face to face: a level doorway takes any level cap, one in a floor (facing up) a cap facing down, and so on.
            foreach (var kind in pack.Rooms.Where(r => !r.EndCap).SelectMany(r => r.Doorways).Select(d => new { Type = d.Type ?? "", Way = Way(d) }).Distinct())
                if (!pack.Rooms.Any(r => r.EndCap && r.Doorways.Any(d => (d.Type ?? "") == kind.Type && Way(d) == (kind.Way == 0 ? 0 : -kind.Way))))
                    problems.Add($"{pack.Name}: no end cap for {(kind.Way == 0 ? "" : kind.Way > 0 ? "upward " : "downward ")}doorways of type \"{kind.Type}\" (open ones will be left sealed)");
        }

        private static bool HasTag(RoomDef r, string tag) => r.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));

        private static int Way(Doorway d) { int q = Layouts.Quadrant(d.Facing); return q == 4 ? 1 : q == 5 ? -1 : 0; }

        /// <summary>One room from the catalogue file `dungeon rooms` writes (BepInEx/claude/dungeon-rooms.json): what DungeonDB gives in the
        /// game, for testing outside it.</summary>
        public static RoomDef FromCatalogue(JObject room)
        {
            var def = new RoomDef
            {
                Name = (string)room["name"],
                GamePrefab = (string)room["name"],
                Size = Vec((JArray)room["size"]),
                Weight = (float?)room["weight"] ?? 1f,
                Entrance = (bool?)room["entrance"] ?? false,
                EndCap = (bool?)room["endCap"] ?? false,
            };
            int i = 0;
            foreach (JObject d in ((JArray)room["doorways"]).OfType<JObject>())
                def.Doorways.Add(new Doorway
                {
                    Id = (i++).ToString(),
                    Type = (string)d["type"] ?? "",
                    At = Vec((JArray)d["at"]),
                    Facing = Vec((JArray)d["facing"]),
                    Entrance = (bool?)d["entrance"] ?? false,
                    AllowDoor = (bool?)d["door"] ?? true,
                });
            return def;
        }

        /// <summary>To the centimetre, as the catalogue writes them: doorways read from a prefab's transforms carry float noise.</summary>
        public static Vector3 Round(Vector3 v) => new Vector3(R(v.x), R(v.y), R(v.z));
        private static float R(float f) => (float)(Math.Round(f * 100.0, MidpointRounding.AwayFromZero) / 100.0);

        private static Vector3 Vec(JArray a) => new Vector3(a.Count > 0 ? (float)a[0] : 0f, a.Count > 1 ? (float)a[1] : 0f, a.Count > 2 ? (float)a[2] : 0f);
        private static IEnumerable<string> Strings(JToken t) => (t as JArray ?? new JArray()).Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x));
    }
}
