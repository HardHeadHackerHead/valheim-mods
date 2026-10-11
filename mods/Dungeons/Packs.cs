using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Dungeons
{
    /// <summary>
    /// The packs: built into the DLL (packs/*.json, embedded), the game's rooms they name filled in from DungeonDB when a world is loaded.
    ///
    /// A pack's JSON (packs/testcrypt.json is a whole one):
    ///   {
    ///     "name": "testcrypt",                 what commands and the entrance's ZDO call it (unique)
    ///     "title": "$dungeons_pack_testcrypt", what a player reads: a translation key (with $) or plain text
    ///     "environment": "Crypt",              the game's environment inside (EnvMan's name; the game's own crypt interiors use "Crypt")
    ///     "minRooms": 6, "maxRooms": 12,       rooms in one dungeon, not counting end caps
    ///     "maxSize": [96, 40, 96],             optional: the whole dungeon's box may not be bigger (metres, x y z)
    ///     "rooms": [                           what it's built from (each picked by weight among those that fit)
    ///       { "game": "forestcrypt_room1" },   one of the game's rooms, by prefab name (DungeonDB): its size, doorways, weight and
    ///                                          whether it's an entrance or an end cap come from the game
    ///       { "game": "...", "name": "...",    a second entry for the same game room needs its own name
    ///         "weight": 2, "max": 1, "minDepth": 3,   how often, at most how many, how many rooms from the entrance at least
    ///         "entrance": true, "endCap": false,      the game's flags, overridden
    ///         "tags": ["end"] },               what the plan can ask for. A room with a tag the plan uses only goes where the plan puts it
    ///       { "name": "hall", "size": [12, 16, 12],            one of our own rooms: its box, centred on its origin,
    ///         "doorways": [ { "id": "north", "type": "", "at": [0, -6.5, 6], "facing": [0, 0, -1],   facing points INTO the room
    ///                         "entrance": false, "door": true } ],
    ///         "parts": [ { "prefab": "stone_wall_4x2", "at": [0, 0, 6], "euler": [0, 90, 0], "scale": [1, 1, 1] } ] }
    ///     ],
    ///     "plan": [                            in order along the way from the entrance; each step's room is only reached through the
    ///       { "tag": "entrance", "id": "start" },          step before it. The first one may be the entrance room itself.
    ///       { "tag": "lever", "id": "lever1" },
    ///       { "tag": "gate", "id": "gate1", "opensWith": "lever1" },   the way into this step's room is locked until lever1
    ///       { "tag": "end", "id": "end" } ],
    ///     "creatures": ["Skeleton"], "loot": ["Coins:10-30"]   a plain list counts as every biome's "common"; both may be by biome:
    ///       {"Mountain": {"melee": [...], "ranged": [...], "flying": [...]}, ..., "default": {...}} (a biome's value may be a plain list:
    ///       its "common"). Loot tiers: "common", "rare", "vault"; entries "Item", "Item:5", "Item:10-30". PackReader.Creatures / Loot
    ///       look one up: the biome's own tiers, else "default"'s, else the plain list (for "common").
    ///   }
    /// Entrance looks: a pack's "entranceLook" names one of packs/looks/<name>.json (embedded as "looks/<name>.json", read by
    /// EntranceLooks.cs when the entrance prefabs are registered, before a world loads: no DungeonDB). A look's file:
    ///   {
    ///     "name": "giant",                 lower case letters, digits, _ (it names the saved prefab: quaddungeon_entrance_<name>, or
    ///                                      quaddungeon_entrance for "crypt"); the same as the file's name
    ///     "about": "...",                  for whoever edits it (not read)
    ///     "front": 5,                      where players come out: metres in front of the door
    ///     "parts": [                       in the entrance's frame: +z out (towards whoever comes to it), y 0 the ground
    ///       { "prefab": "Ashlands_Boss_Pillar", "at": [x, y, z], "euler": [...], "scale": [...] },   one of the game's prefabs, as scenery
    ///       { "location": "Crypt4", "keep": ["exterior", "Gateway/Cube"], "doorway": [0, 0, 6.96], "at": [0, 0, 0], "euler": [0, 0, 0] }
    ///                                      children of a game location, copied so the location's point "doorway" lands at "at"
    ///     ],
    ///     "inside": [ { "prefab": "...", "at": [...], "euler": [...], "scale": [...] } ]   the way out seen from inside: in the way-in
    ///                                      doorway's frame (origin on its floor, +z into the room), prefabs only; none: the stand-in door
    ///   }
    /// LookCheck.cs says what's wrong in one (logged when read); tools/dungeons checks them all, and that every pack's look exists.
    /// Rooms join where doorways of the same type meet face to face. Every doorway type needs an end cap room (a room marked endCap with
    /// one doorway of that type), or the open doorways of that type are left sealed.
    ///
    /// Moving parts and puzzles (step 2):
    ///   pack:  "theme": "DvergerTown"           the game's Room.Theme for random props (null: its game rooms')
    ///          "entranceLook": "crypt"           what its entrance looks like outside
    ///          "doors": { "": "dungeon_forestcrypt_door" }, "doorChance": 0.5   a door prefab per doorway type, and how often a joint
    ///                                            whose two doorways allow one gets it (never where a gate stands, never on an end cap)
    ///          "minUses": 4                      the puzzle must take at least this many uses of levers and plates
    ///          "parkour": false                  no parkour inside (wall runs, climbing: PackReader.Parkour; default true)
    ///          "entranceCap": "cap_gate"         the end cap the entrance room's way in takes (else any plain one: under 1 m deep, no
    ///                                            mechanisms, no chest). A doorway a gate closes takes only plain caps too.
    ///          "gates": { "": { "kind": "gate", "parts": [...], "poses": [closed, open], "seconds": 2 } }   the gate put in a locked
    ///                                            doorway of that type when the step's room has none; its frame is the doorway's
    ///                                            (origin at the doorway, +z into the room behind the gate, +y up)
    ///   plan:  { "tag": "lever", "id": "lever2", "from": "hall" }   grows from an earlier step's room (a hub) instead of the one before
    ///          { "tag": "vault", "id": "vault", "opensWith": "lever2" }   entered through a gate a lever in lever2's room opens
    ///          The step after a "from" step (with no "from" of its own) grows from that step's room. A step nothing grows on from (the
    ///          next step has "from", and no step is "from" it) may be a dead end (one doorway: a key in a side wing). Any number of
    ///          keys and locks; each lock's key is any earlier step.
    ///   tags:  a room tagged "fight" opens its gates when its fight is won: the solver takes them as open (the fight is the player's).
    ///   encounters: kind "encounter" (EncounterDef in Model.cs; Runtime's MechEncounter.cs): "startOn" "wire" (default) | "enter" |
    ///          "open", "area", "chest", "lock", "waves" [{"melee": 2, ...}], "points", "level", "eliteLevel", "pause", "timeout", "siege",
    ///          "holdBy", "lockLever", "music" (while it fights), "boss": {"name": "$...", "role": "elite", "health": 4,
    ///          "phases": [{"at": 0.5, "wires": [...], "adds": {"melee": 2}}]} (one creature instead of waves). Its wires apply when it's won:
    ///          the solver takes it as won where it's reached ("enter", "open"), or with the lever or plate whose wire does "start" on it
    ///          ("wire"); a boss's phase wires fire on the way.
    ///   breakable: {"kind": "breakable", "doorway": "in", "health": 300, "poses": [standing, broken]}: closes its doorway until broken.
    ///          A room tagged "secret" is entered through one: the layout puts it only on a side branch, joined by that doorway; the
    ///          solution is found with every breakable standing, so nothing may need a secret room.
    ///   shortcut: {"kind": "shortcut", "pair": "sc1", "deep": true} (the far door; "end": "far" too) in two end-cap rooms, and the pack's
    ///          "shortcut": {"near": "<room>", "far": "<room>", "farStep": "<step id>"}: the far room on (or one room from) that step's room,
    ///          the near room on a spare doorway of the entrance room. Usable once the far end is reached; never shortens the solution.
    ///   music: "music": "<the game's music name>" on the pack (inside), a room, an encounter.
    ///   "cycleIn": [1]: a cycle that runs only while the mechanism is set to one of those states (a crusher slams after "set:1").
    ///   "spinScale": [1, 2.5]: its parts' spin in each state; a machine with one may have that many states.
    ///   plates: "hold" (open only while pressed) counts for nothing alone. A layout must be solvable alone; one that only two players
    ///          (or a creature on the plate) could finish is refused, saying so.
    ///   gates: a gate with a "doorway" closes that doorway in state 0; one without blocks something inside its room that the solver
    ///          doesn't see (in front of an exit: keep the way to the end through doorways).
    ///   rooms: "parts": [ { ..., "saved": true } ]   made once by the owner as a saved object (a chest, a spawner)
    ///          a mechanism's part may have "spin": [x, y, z]   degrees a second it turns while its mechanism moves (always, with one state)
    ///          "mechs": [ { "id": "stair1", "kind": "stair", "at": [0, -8, 0], "euler": [0, 0, 0],
    ///                       "parts": [...], "poses": [ { "at": [...], "euler": [...] }, ... ], "start": 0, "seconds": 1.5, "cycle": 0,
    ///                       "joins": [ { "state": 0, "a": "landing0", "b": "landing1" } ] },   doorways of its room joined in a state
    ///                     { "id": "lever1", "kind": "lever", "reachedFrom": "landing1",   used from that doorway's side (else anywhere)
    ///                       "wires": [ { "target": "stair1", "do": "next" } ] },   "next", "set:2", "hold" (plates); a target in the same
    ///                                                                            room, "step:<id>" or "step:<id>.<mech id>"
    ///                     { "id": "gate1", "kind": "gate", "doorway": "north", ... } ]   closes that doorway in state 0
    ///                     a lever or plate with "once": true is used once only (its wires must all be "set:n"; so is any lever whose
    ///                     wires all are, at runtime)
    ///          "joinBy": ["s0n", "s0e"]          the only doorways the room is attached by when it's placed (its other doorways still
    ///                                            take rooms after it)
    ///          "drops": [ { "a": "s1n", "b": "s0n" } ]   one way only, from a to b (a jump off a balcony): the solver uses them
    ///          "inside": [ { "a": "landing0", "b": "south" } ]   doorways always joined through the room. With neither "inside" nor
    ///                                            mechanisms with "joins" a room joins all its doorways; with mechanism joins and no
    ///                                            "inside", only those.
    /// Kinds: lever, plate, gate, door, platform, stair, rotator, floor, machine (one state: its spinning parts just turn), exit (a way
    /// out, as the entrance room's Leave: Runtime's). LayoutSolver.cs
    /// says how the puzzle is checked; it leaves out mechanisms that can't open or close a way (no joins, not a gate), such as turning gears.
    /// </summary>
    internal static class Packs
    {
        private const string Prefix = "packs/";

        private static Dictionary<string, JObject> _json;                         // name (lower case) -> its JSON, from the DLL
        private static readonly Dictionary<string, Pack> Loaded = new Dictionary<string, Pack>();
        private static readonly Dictionary<string, List<string>> LoadProblems = new Dictionary<string, List<string>>();
        private static DungeonDB _world;                                          // the DungeonDB the loaded packs were read from
        private static readonly Dictionary<string, List<DungeonDB.RoomData>> Held = new Dictionary<string, List<DungeonDB.RoomData>>();
        private static readonly Dictionary<string, float> HeldSince = new Dictionary<string, float>();
        private const float ReadyWait = 10f;   // seconds Ready waits for the game's rooms before letting Get load the rest itself

        /// <summary>A pack by name (case ignored), or null. Its game rooms are read from DungeonDB the first time it's asked for in a world.</summary>
        public static Pack Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (DungeonDB.instance == null) return null;   // (no world: the game's rooms can't be read)
            if (Waiting) return null;                      // (a client waiting for its server's packs)
            SameWorld();
            string key = name.ToLowerInvariant();
            if (Loaded.TryGetValue(key, out Pack pack)) return pack;
            if (!Json().TryGetValue(key, out JObject json)) return null;
            var problems = new List<string>();
            try
            {
                pack = PackReader.Read(json, GameRoom, problems);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Pack {name} can't be read: {e.Message}");
                return null;
            }
            if (pack.EntranceLook != null && !EntranceLooks.Known(pack.EntranceLook))
                problems.Add($"{pack.Name}: its entranceLook {pack.EntranceLook} has no file in packs/looks (the default look is used)");
            foreach (string p in problems) Plugin.Log.LogWarning("Pack " + p);
            Loaded[key] = pack;
            LoadProblems[key] = problems;
            return pack;
        }

        /// <summary>
        /// Starts loading the game's rooms a pack names in the background and says whether they're all in, so Get can read them without
        /// stopping the game (Get on its own loads each one at once: 15-25 loads in one frame). Call it every frame before Get until it
        /// says true. After 10 s it says true anyway (Get then loads what's left itself). True for a pack already read, or unknown.
        /// The rooms stay loaded (held) until the world changes or the packs are cleared, so the builder's own loads of them are instant.
        /// </summary>
        public static bool Ready(string name)
        {
            if (string.IsNullOrEmpty(name) || DungeonDB.instance == null) return true;
            if (Waiting) return false;
            SameWorld();
            string key = name.ToLowerInvariant();
            if (Loaded.ContainsKey(key) || !Json().TryGetValue(key, out JObject json)) return true;
            if (!Held.TryGetValue(key, out List<DungeonDB.RoomData> rooms))
            {
                rooms = new List<DungeonDB.RoomData>();
                foreach (string game in (json["rooms"] as JArray ?? new JArray()).OfType<JObject>().Select(r => (string)r["game"]).Where(g => !string.IsNullOrEmpty(g)).Distinct())
                {
                    try
                    {
                        DungeonDB.RoomData data = DungeonDB.instance.GetRoom(game.GetStableHashCode());
                        if (data == null || !data.m_prefab.IsValid) continue;   // (Get says which are missing)
                        data.m_prefab.LoadAsync();                            // (holds a reference until Let go)
                        rooms.Add(data);
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"Pack {name}: the game's room {game} can't be loaded: {e.Message}"); }
                }
                Held[key] = rooms;
                HeldSince[key] = Time.realtimeSinceStartup;
            }
            if (Time.realtimeSinceStartup - HeldSince[key] > ReadyWait) return true;
            return rooms.All(r => r.m_prefab.IsLoaded);
        }

        /// <summary>A different world (or none) since the packs were read: forget them, and let go of the rooms held for them.</summary>
        private static void SameWorld()
        {
            if (_world == DungeonDB.instance) return;
            Loaded.Clear();
            LoadProblems.Clear();
            LetGo();
            _world = DungeonDB.instance;
        }

        private static void LetGo()
        {
            foreach (List<DungeonDB.RoomData> rooms in Held.Values)
                foreach (DungeonDB.RoomData r in rooms)
                    try { r.m_prefab.Release(); } catch (Exception) { }
            Held.Clear();
            HeldSince.Clear();
        }

        /// <summary>Every pack's name.</summary>
        public static IEnumerable<string> Names => Json().Values.Select(j => (string)j["name"]).OrderBy(n => n);

        /// <summary>What was wrong when a pack was read in this world (missing game rooms, plan tags no room has...): for `dungeon packs`.</summary>
        public static List<string> Problems(string name) =>
            name != null && LoadProblems.TryGetValue(name.ToLowerInvariant(), out List<string> list) ? list : new List<string>();

        /// <summary>Forget the packs (hot reload, or a different world: DungeonDB's rooms come from the world's game).</summary>
        public static void Clear()
        {
            _json = null;
            Loaded.Clear();
            LoadProblems.Clear();
            LetGo();
            _world = null;
        }

        /// <summary>The packs built into the DLL (embedded resources "packs/&lt;name&gt;.json").</summary>
        // ---- where packs come from (dungeon-sync's seam) ----
        // "builtin": packs/*.json in the DLL; "config": a folder of the player's or server's; "server": the set a server sent its players.
        // A pack in a later source replaces one of the same name in an earlier: builtin < config. A "server" set, when there is one (a
        // client of a server with Dungeons), is the whole set: the client lays out exactly what the server does.
        public static readonly string[] Sources = { "builtin", "config", "server" };
        private static readonly Dictionary<string, Dictionary<string, KeyValuePair<JObject, string>>> Extra =
            new Dictionary<string, Dictionary<string, KeyValuePair<JObject, string>>>();   // source -> name (lower) -> (json, its text)
        private static readonly Dictionary<string, string> SourceByName = new Dictionary<string, string>();
        private static Dictionary<string, string> _builtinText;

        /// <summary>True on a client whose server has Dungeons but whose pack set hasn't come yet (dungeon-sync sets it): Get gives nothing
        /// and Ready says no, so no dungeon is laid out from the client's own packs meanwhile.</summary>
        public static bool Waiting { get; set; }

        /// <summary>Goes up by one every time the set of packs changes (SetSource): a layout made from an older set is out of date.</summary>
        public static int Version { get; private set; }

        /// <summary>Fired after the set of packs changed (SetSource), on the caller's thread.</summary>
        public static event Action Changed;

        /// <summary>Sets the packs from a source ("config" or "server"; null or empty clears it), each a pack's JSON text. A text that can't
        /// be read, or has no "name", is left out with a warning. Read packs are forgotten, so the next Get reads the new set.</summary>
        public static void SetSource(string source, IEnumerable<string> texts)
        {
            if (source == null || source == "builtin" || !Sources.Contains(source)) throw new ArgumentException("source: \"config\" or \"server\"");
            var set = new Dictionary<string, KeyValuePair<JObject, string>>();
            foreach (string text in texts ?? Enumerable.Empty<string>())
            {
                try
                {
                    JObject json = JObject.Parse(text);
                    string name = (string)json["name"];
                    if (string.IsNullOrEmpty(name)) { Plugin.Log.LogWarning($"A {source} pack has no \"name\": left out"); continue; }
                    set[name.ToLowerInvariant()] = new KeyValuePair<JObject, string>(json, text);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"A {source} pack can't be read: {e.Message}"); }
            }
            if (set.Count == 0) Extra.Remove(source); else Extra[source] = set;
            _json = null;
            Loaded.Clear();
            LoadProblems.Clear();
            Version++;
            try { Changed?.Invoke(); } catch (Exception e) { Plugin.Log.LogWarning("Packs.Changed: " + e.Message); }
        }

        /// <summary>Where a pack (by name) comes from now: "builtin", "config" or "server"; null if there's none.</summary>
        public static string SourceOf(string name)
        {
            Json();
            return name != null && SourceByName.TryGetValue(name.ToLowerInvariant(), out string source) ? source : null;
        }

        /// <summary>Every pack in use, name -> its JSON text (what a server sends its players).</summary>
        public static Dictionary<string, string> Texts()
        {
            var texts = new Dictionary<string, string>();
            foreach (KeyValuePair<string, JObject> p in Json())
                texts[(string)p.Value["name"]] = SourceByName[p.Key] == "builtin" ? _builtinText[p.Key]
                    : Extra[SourceByName[p.Key]][p.Key].Value;
            return texts;
        }

        /// <summary>Every pack in use, by name (lower case): the built-in ones, each later source's over them.</summary>
        private static Dictionary<string, JObject> Json()
        {
            if (_json != null) return _json;
            _json = new Dictionary<string, JObject>();
            SourceByName.Clear();
            if (Extra.TryGetValue("server", out var server))
            {
                foreach (var p in server) { _json[p.Key] = p.Value.Key; SourceByName[p.Key] = "server"; }
                return _json;
            }
            Builtin();
            if (Extra.TryGetValue("config", out var config))
                foreach (var p in config) { _json[p.Key] = p.Value.Key; SourceByName[p.Key] = "config"; }
            return _json;
        }

        /// <summary>The packs built into the DLL (embedded resources "packs/&lt;name&gt;.json").</summary>
        private static void Builtin()
        {
            bool first = _builtinText == null;
            if (first) _builtinText = new Dictionary<string, string>();
            var asm = typeof(Packs).Assembly;
            foreach (string res in asm.GetManifestResourceNames().Where(r => r.StartsWith(Prefix) && r.EndsWith(".json")).OrderBy(r => r))
            {
                try
                {
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (var reader = new StreamReader(s))
                    {
                        string text = reader.ReadToEnd();
                        JObject json = JObject.Parse(text);
                        string name = (string)json["name"];
                        if (string.IsNullOrEmpty(name)) { if (first) Plugin.Log.LogWarning($"{res} has no \"name\": left out"); continue; }
                        if (_json.ContainsKey(name.ToLowerInvariant())) { if (first) Plugin.Log.LogWarning($"{res}: a pack called {name} is already built in: left out"); continue; }
                        _json[name.ToLowerInvariant()] = json;
                        SourceByName[name.ToLowerInvariant()] = "builtin";
                        _builtinText[name.ToLowerInvariant()] = text;
                    }
                }
                catch (Exception e) { if (first) Plugin.Log.LogWarning($"{res} can't be read: {e.Message}"); }
            }
        }

        /// <summary>The game's room by prefab name, from DungeonDB: its box, doorways (in its own frame, rounded to the centimetre, as the
        /// catalogue has them) and flags. Its prefab is loaded for this and let go again. Null if the game has no such room.</summary>
        private static RoomDef GameRoom(string prefab)
        {
            DungeonDB.RoomData data = DungeonDB.instance.GetRoom(prefab.GetStableHashCode());
            if (data == null) return null;
            data.m_prefab.Load();
            try
            {
                GameObject asset = data.m_prefab.Asset;
                Room room = asset != null ? asset.GetComponent<Room>() : null;
                if (room == null) return null;
                Transform root = room.transform;
                var def = new RoomDef
                {
                    Name = prefab,
                    GamePrefab = prefab,
                    Size = new Vector3(room.m_size.x, room.m_size.y, room.m_size.z),
                    Weight = room.m_weight,
                    Entrance = room.m_entrance,
                    EndCap = room.m_endCap,
                };
                // The connections the game itself uses (Room.GetConnections: the active ones), in the same order.
                RoomConnection[] connections = room.GetComponentsInChildren<RoomConnection>(false);
                if (connections.Length == 0) connections = room.GetComponentsInChildren<RoomConnection>(true);
                foreach (RoomConnection c in connections)
                    def.Doorways.Add(new Doorway
                    {
                        Id = def.Doorways.Count.ToString(),
                        Type = c.m_type ?? "",
                        At = PackReader.Round(root.InverseTransformPoint(c.transform.position)),
                        Facing = PackReader.Round(root.InverseTransformDirection(c.transform.forward)),
                        Entrance = c.m_entrance,
                        AllowDoor = c.m_allowDoor,
                    });
                if (!(data.m_enabled && room.m_enabled)) Plugin.Log.LogInfo($"Pack room {prefab} is switched off in the game's own dungeons (used anyway)");
                return def;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"The game's room {prefab} can't be read: {e.Message}");
                return null;
            }
            finally { data.m_prefab.Release(); }
        }
    }
}
