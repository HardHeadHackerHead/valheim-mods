# The pack format: every field

A pack is one JSON file: built in (`mods/Dungeons/packs/<name>.json`, embedded in the DLL, so a change needs a build), or in the game's
`BepInEx/config/Dungeons/packs/` (read as a world starts; the server's set is sent to its players: SKILL.md). Entrance looks are files of
their own: `packs/looks/<name>.json`, in either place.

The code that reads packs: `PackReader.cs`, which reads every field below, and `Packs.cs`, whose top comment is the format as the code's
authors keep it. If this file and `Packs.cs` disagree, `Packs.cs` is right: fix this file. The data classes are in `Model.cs`, and pack
JSON uses their field names in camelCase.

Don't write pack JSON by hand for rooms of your own: write a generator script (`tools/dungeons/shifting_halls.py` is the model, with
`dungeon_parts.py`). Hand-writing works for a pack made only of the game's rooms (`packs/testcrypt.json`).

## The pack

| Field | Type | What it does |
|---|---|---|
| `name` | string | Unique: letters, digits, `_` and `-`, up to 48, matched without case. Commands and the entrance's ZDO call the pack by it. Changing it orphans every entrance of the old name; a config pack with a built-in pack's name replaces it. |
| `title` | string | What a player reads: a translation key (`$dungeons_pack_shiftinghalls`, in `translations/*.json`) or plain text. |
| `environment` | string | The game's environment inside (EnvMan's name): `Crypt` (the game's crypts), `Caves`... |
| `minRooms`, `maxRooms` | int | Rooms in one dungeon, not counting end caps. |
| `maxSize` | [x, y, z] | Optional: the whole dungeon's box may not be bigger (metres). |
| `minUses` | int | The puzzle must take at least this many lever and plate uses (the solver's shortest solution). Otherwise the seed fails. |
| `entranceLook` | string | The entrance in the world: a look in `packs/looks/` (below). None or unknown: `crypt`. |
| `entranceCap` | string | The end cap the entrance room's way in takes. Without it: any **plain** cap (below). |
| `theme` | string | The game's `Room.Theme` (a name or number) deciding which of the game rooms' random props appear (null: the one its game rooms share). |
| `doors` | {type: prefab} | A door prefab per doorway type (`{"": "dungeon_forestcrypt_door"}`). It goes where two joined doorways both allow a door. |
| `doorChance` | 0..1 | How often such a joint gets a door (seeded). A door never goes where a gate stands or on an end cap. |
| `gates` | {type: mech} | The gate put in a locked doorway of that type when the locked step's room has none: `{"kind": "gate", "parts": [...], "poses": [closed, open], "seconds": 2}`. Its frame is the doorway's: origin at the doorway, +z into the room behind the gate, +y up. |
| `parkour` | bool | Default true. `false`: Quad's Parkour is off inside (wall runs and climbs would skip the puzzle). Read through `Api.ParkourOff`. |
| `music` | string | The game's music track inside where a room names none. `dungeon music` lists the names; known good: `forestcrypt`, `combat`, `CombatEventL1`-`4`, `boss_eikthyr`, `boss_gdking`, `boss_bonemass`, `boss_moder`, `boss_goblinking`, `boss_frozenking`, and the biome names. |
| `shortcut` | object | `{"near": "<room>", "far": "<room>", "farStep": "<step id>"}`: see shortcuts below. |
| `items` | object | Key items: triggers.md. |
| `rooms` | list | Below. |
| `plan` | list | Below. |
| `creatures` | list or object | Below. |
| `loot` | list or object | Below. |

## Rooms

Every room is picked by weight among those that fit at an open doorway.

**One of the game's rooms** (from DungeonDB; its size, doorways and flags come from the game, and `dungeon rooms` lists them all):

```json
{ "game": "forestcrypt_room1", "name": "room1b", "weight": 2, "max": 1, "minDepth": 3,
  "entrance": true, "endCap": false, "tags": ["end"] }
```

`name` is only needed for a second entry of the same game room. `entrance` and `endCap` override the game's own flags.

**One of our own rooms**:

```json
{ "name": "hall", "size": [12, 16, 12], "tags": ["lever"], "weight": 1, "max": 1, "minDepth": 2, "music": "...",
  "doorways": [ { "id": "north", "type": "giant", "at": [0, -6.5, 6], "facing": [0, 0, -1], "entrance": false, "door": true } ],
  "parts":    [ { "prefab": "stone_wall_4x2", "at": [0, 0, 6], "euler": [0, 90, 0], "scale": [1, 1, 1], "saved": false } ],
  "mechs":    [ ... ],
  "inside":   [ { "a": "landing0", "b": "south" } ],
  "drops":    [ { "a": "s1n", "b": "s0n" } ],
  "joinBy":   ["s0n", "s0e"] }
```

| Field | What it does |
|---|---|
| `size` | The room's box, centred on its origin (as the game's `Room.m_size`). Boxes may not overlap (by more than 5 cm). |
| `doorways[].at` | In the room's frame: **the middle of the threshold, on its floor** (our convention). Two rooms join only where their points are equal, so a room of ours joining a game room's type must match that game room's height over its floor; measure it in the game (SKILL.md lesson 11). |
| `doorways[].facing` | Points **into** the room, along an axis. Two rooms join where doorways of the same `type` meet: same place, facing opposite ways. |
| `doorways[].type` | `""` is the crypts' type; the game also has `dvergr`, `stair` and others. Ours can be our own (`giant`). |
| `doorways[].entrance` | The room's way in (the entrance room's arrival doorway). |
| `doorways[].door` | A door or gate may stand here. Doors are never put in floor or ceiling doorways. |
| `parts[]` | A prefab from **ZNetScene**, as stripped scenery (no ZDO, nothing to break, use, deconstruct or save; lights and fire stay). `scale` multiplies the prefab's own scale. |
| `parts[].saved` | Made once, by the owner, as a real saved object: a chest, a spawner, a pickable. Must be a networked prefab (one that isn't is built as scenery, with a warning). **Keep saved parts at scale 1**: the size is only saved if the prefab syncs its scale. |
| `tags` | What the plan can ask for. **A room with a tag the plan uses only goes where the plan puts it.** Tags with a meaning of their own: `fight` (its gates open when its fight is won), `secret` (behind a breakable wall, on a side branch only), `vault`/`end` (its chests are vault chests), `boss` (boss chest). |
| `max`, `minDepth`, `weight` | At most this many (0: no limit); at least this many rooms from the entrance; how often. |
| `entrance` | Can start a dungeon (the entrance room is placed at the origin, its way in towards -z). |
| `endCap` | A room with one doorway that closes an open doorway of its type. **Every doorway type needs an end cap**, or open doorways of that type are left sealed (a problem). |
| `inside` | Doorways always joined through the room. With neither `inside` nor mechanisms with `joins`, a room joins all its doorways. With mechanism joins and no `inside`, only those. |
| `drops` | One way only, from `a` to `b`: a jump down off a balcony. The solver uses them. |
| `joinBy` | The only doorways the room is attached by when it's placed (its other doorways still take rooms after it). For a hall that must be entered from its ground floor. |
| `music` | The game's music while a player is in this room. |
| `triggers` | Events and actions: triggers.md. |

**Parts.** `at`, `euler` and `scale` are used exactly. Any scale works, uneven too: meshes, colliders and LOD scale with it, and a light's
range, its fade distance and fire and smoke grow by the mean of the three factors (brightness doesn't: use more lights, not bigger ones).
Avoid negative scale (a mirror) on parts people walk on; `euler` x 180 for a ceiling is fine. A part made of one of the game's items
(a cogwheel as a gear) is solid and can't be picked up. Our reward items belong in loot only.

**Plain caps.** The entrance room's way in (behind the way out) and a doorway a gate closes take only a plain end cap: under 1 m deep,
no mechanisms, no chest. A nook or a treasury there would put a door opening and shutting behind the Leave door, or a room behind a gate.
If none fits, the seed fails with "no plain end cap fits the way in". Give each doorway type a flat seal cap (Shifting Halls: `cap_gate`).

## The plan

In order along the way from the entrance. Each step's room is only reached through the step before it.

```json
"plan": [
  { "tag": "entrance", "id": "start" },
  { "tag": "hub",      "id": "hub1" },
  { "tag": "lever",    "id": "lever1", "from": "hub1" },
  { "tag": "gate",     "id": "gate1",  "opensWith": "lever1", "from": "hub1" },
  { "tag": "end",      "id": "vault" } ]
```

- `tag`: a room with this tag fills the step. `id`: the step's name, for keys, locks and wires (`step:<id>`).
- `opensWith`: the step's room is entered through a gate that the named earlier step opens. The gate is the room's own (a gate or door
  mechanism with a `doorway`), else the pack's `gates` pattern for that doorway type. The opening step's room must hold a lever or plate;
  if none of its wires reaches the gate, its first lever gets one (`step:<locked step>`, `set:1`).
- `from`: grows from an earlier step's room (a hub) instead of the step before. The step after a `from` step, with no `from` of its own,
  grows from that step's room. A step nothing grows on from may be a dead end (a key in a side wing).
- Any number of keys and locks; each lock's key is any earlier step.
- The last step's room is the end. Branches never grow past it.

## Mechanisms

`"mechs": [...]` in a room. A mechanism's state is saved on the entrance; its motion comes from the state and the server's clock.

```json
{ "id": "stair1", "kind": "stair", "at": [0, -8, 0], "euler": [0, 0, 0],
  "parts": [ { "prefab": "...", "at": [...], "euler": [...], "scale": [...], "spin": [0, 30, 0] } ],
  "poses": [ { "at": [0, 0, 0], "euler": [0, 0, 0] }, { "at": [0, 0, 0], "euler": [0, 90, 0] } ],
  "start": 0, "seconds": 1.5, "cycle": 0,
  "joins": [ { "state": 0, "a": "landing0", "b": "landing1" }, { "state": 1, "a": "landing1", "b": "landing2" } ] }
```

| Field | What it does |
|---|---|
| `id` | Unique in its room. In a layout it becomes `<room index>.<id>` (the key commands use). |
| `kind` | See the table below. |
| `at`, `euler` | Its frame in the room. Parts and poses are in this frame. |
| `parts` | What moves: its look and colliders. Always scenery; `saved` on a mechanism part is ignored (and warned). |
| `parts[].spin` | Degrees a second about its own x, y, z while the mechanism moves (backwards when it goes back a state), or all the time in a `machine`. Gears. Only in a mechanism's parts: a room's parts are batched scenery and never turn. |
| `spinScale` | `[1, 2.5]`: the parts' spin times this in each state (a machine with more than one state needs one per state). |
| `poses` | One per state: where the parts stand. |
| `start` | Its state when the dungeon is new. |
| `seconds` | Time to move from one state to the next (default 1.5). Turns take the shortest way, positions ease in and out. |
| `cycle` | > 0: goes to its next state by itself every `cycle` seconds of the server's clock. |
| `cycleIn` | `[1]`: the cycle runs only while it's set to one of these states (a crusher that wakes on a wire). Needs a `cycle`. |
| `doorway` | A gate or door: the doorway it closes in state 0 (open in state 1). |
| `joins` | Doorways of its room joined in a state (`state: -1`: every state). The solver walks these. |
| `wires` | A lever or plate: `[{"target": "stair1", "do": "next"}]`. |
| `reachedFrom` | A lever or plate: a doorway id on the side of the room it's used from (null: anywhere in the room). |
| `once` | A lever stays down, a plate stays pressed, until a reset. Its wires must all be `set:n` (or `start`). |

**Wires.** `target`: a mechanism in the same room, `step:<id>` (the gate on that step's lock if it's locked, else that room's first
mechanism that isn't a lever or plate), or `step:<id>.<mech id>`. `do`: `next` (one state on, wrapping), `set:<n>`, `hold` (a plate:
state 1 while pressed, 0 after), `start` (starts an encounter).

**Kinds.**

| Kind | What it is |
|---|---|
| `lever` | E pulls it ("Pull lever", the Obliterator's words and sound). The owner checks the puller stands near it. **A lever whose wires all `set:` or `start` is one-shot** (stays down until a reset), as is one with `once`. |
| `plate` | Pressed while a character stands on it (the owner looks every 0.1 s; creatures count). `hold` wires follow it. A `hold` plate counts for nothing alone in the solver. |
| `gate`, `door` | Close their `doorway` in state 0. A gate with no `doorway` blocks something inside its room the solver doesn't see. With no solid part, one box over its parts is added, so it always blocks. |
| `platform` | A lift or moving floor (`testmechs`: a lift with `cycle` 8). Platforms, stairs and rotators carry whatever stands on them, up, down and round. |
| `stair` | A turning stair: its `joins` say which landings it links in each state. |
| `rotator` | A rotating room or drum (the rotunda: a `cycle` of 12 s). |
| `floor` | A floor that falls away. |
| `machine` | Its spinning parts just turn (scenery that moves). No joins, no doorway; more than one state only with a `spinScale` for each. To move between states, make it a rotator or stair. |
| `exit` | A way out, as the entrance room's Leave (E from within 3 m; a gate in front of it keeps it shut). The vault's way out. |
| `encounter` | A fight: below. |
| `breakable` | A wall that breaks: below. |
| `shortcut` | One door of a shortcut pair: below. |
| `pedestal`, `brazier`, `pushblock`, `trap`, `teleporter`, `lock`, `checkpoint` | The building blocks: triggers.md. |

The solver leaves out mechanisms that can't open or close a way (no joins, not a gate): gears and machines.

Levers and exits get an invisible aim box 0.3 m bigger than their parts, and E falls back to the nearest lever, exit or shortcut within
3 m in front of the player when the camera's ray hits stone first. Still give each 2 m of clear floor in front (`lever_check.py`).

**Encounters** (`kind: "encounter"`, `EncounterDef` in `Model.cs`, run by `MechEncounter.cs`):

```json
{ "id": "fight", "kind": "encounter", "at": [...],
  "area": [16, 8, 16], "startOn": "enter", "chest": [x, y, z],
  "lock": ["gate_in", "gate_out"], "waves": [ {"melee": 2}, {"melee": 2, "ranged": 1} ],
  "points": [[x, y, z], ...], "level": 1, "eliteLevel": 3, "pause": 4, "timeout": 40,
  "siege": 0, "holdBy": "plate1", "lockLever": "lever1", "music": "CombatEventL3",
  "boss": { "name": "$...", "role": "elite", "health": 4,
            "phases": [ { "at": 0.5, "wires": [...], "adds": {"melee": 2} } ] },
  "wires": [ ... ] }
```

- `startOn`: `wire` (default: a lever's or plate's wire `do: "start"`), `enter` (a living player steps into `area`), `open` (the chest at
  `chest` is opened).
- When it starts, its `lock` mechanisms (gates, doors or platforms of the room) go to state 0. Waves come at `points` (room frame, on
  the floor, inside `area`; used in turn): roles `melee`, `ranged`, `flying`, `elite`, from the pack's `creatures` for the biome, else
  the table. One more melee a wave for each extra player inside. The next wave when the last is dead (after `pause`), or after `timeout`
  anyway; the last wave must die.
- Cleared when the last wave is dead: its `wires` fire (or, with none, its locks go back as they were).
- No `waves`: guard mode, cleared when no creature is alive in its area.
- `siege` > 0: waves come only while the `holdBy` plate is pressed, until `siege` creatures are killed.
- `lockLever`: a lever in the room that can't be pulled until it's cleared ("Locked: clear the room").
- `music`: its fight track (default `CombatEventL3`; a boss: `boss_frozenking`).
- A wipe (no living player in the area for 60 s) puts it back to idle: its creatures go, its locks go back.
- `boss`: one creature instead of waves (not both), at `points[0]`: the land's elite (or `role`) at the level cap, with the game's boss
  bar and `name`, taking 1/`health` of each hit. Each phase fires its wires and brings `adds` once, as its health passes `at` (a
  fraction between 0 and 1). On its death: the clear wires, the warden's trophy dropped where it fell, the vault moment and every
  player's vault reward. A room tagged `boss` also gets a boss chest with a trophy: use one or the other in a dungeon, or there are two.
- `level`, `eliteLevel`: the game's levels (1 = no star), capped to the land.
- The state is saved on the entrance (it survives a relog); its creatures are counted by a tag, never by ZDOID.
- `dungeon set <key> 0|1|2`: idle, start, cleared.
- The solver takes an encounter as won: where it's reached (`enter`, `open`), or with the lever or plate that starts it (`wire`).

**Breakables**: `{"id": "wall", "kind": "breakable", "doorway": "in", "health": 300, "poses": [standing, broken]}` closes its doorway until
broken. Only pickaxe and blunt damage count. A room tagged `secret` is entered through one, and the layout puts it only on a side branch.
The solution is found with every breakable standing, so nothing may need a secret room.

**Shortcuts**: `{"id": "door", "kind": "shortcut", "pair": "sc1", "deep": true}` in two end-cap rooms (the deep one is the far door, used
from its front, +z), and the pack's `"shortcut": {"near": "<room>", "far": "<room>", "farStep": "<step id>"}`. The far room goes on (or
one room from) that step's room; the near room on a spare doorway of the entrance room. Both doors stay sealed until a player uses the
far one from its +z side; after that, E on either takes them to 1.5 m in front of the other. Face +z towards where players stand. It never shortens the solution (the harness checks).

**What `dungeon packs` (and building) refuse**, from `PackReader.cs`: a wire with no target, or to a mechanism not in its room (other
than `step:`); an unknown `do`; `hold` on anything but a plate; `start` on anything but an encounter; `set:n` past the target's states;
`once` with a `next` or `hold` wire; `cycleIn` without `cycle` or naming a state it hasn't; an empty `spinScale`; a machine that joins or
closes a doorway; an encounter wave role other than melee, ranged, flying or elite; waves or a boss with no `points`; a point outside its
area or its room; a boss together with waves; a boss phase outside 0..1; a lock that isn't a gate, door or platform; `holdBy` that isn't a
plate, `lockLever` that isn't a lever in the room.

## Enemies

```json
"creatures": ["Skeleton", "Ghost"]
"creatures": { "Mountain": { "melee": ["Ulv", "Fenring"], "ranged": ["Fenring_Cultist"], "flying": ["Bat"] },
               "default":  { "melee": [...] } }
```

A plain list counts as every biome's `common`. Biome names are the game's `Heightmap.Biome` names: `Meadows`, `BlackForest`, `Swamp`,
`Mountain`, `Plains`, `Mistlands`, `AshLands`, `DeepNorth`, `Ocean`. A biome's value may also be a plain list. Roles: `melee`, `ranged`,
`flying`, and `elite` (encounters' elites and bosses; with no `elite` list, a melee pick at an elite's level). Lookup: the biome's own
role, then its `common`, else `default`'s, else the plain list, else the builder's table. When the pack names creatures for a biome and
role, a spawner of that role uses the pack's even if its own creature would fit. Whatever the pack says, a creature above the land's
tier, or a boss, is never used (SKILL.md).

## Loot

```json
"loot": ["Coins:10-30"]
"loot": { "Mountain": { "common": ["Coins:20-40", "SilverOre:2-4"], "rare": [...], "vault": [...], "boss": [...] },
          "default":  { "common": [...] } }
```

Entries: `"Item"`, `"Item:5"`, `"Item:10-30"` (prefab names from ObjectDB). Lists: `common` (the game rooms' chests), `rare` (our rooms'
chests), `vault` (chests in a room tagged `vault` or `end`), `boss` (chests in a room tagged `boss`). Fallbacks: `boss` → `vault` →
`common`; the others → `common`; then the builder's table. A chest gets as many stacks as its own default list would give (2 to 6), 2
more in a vault or boss chest. An item the table knows to be above the land's tier is left out even if the pack lists it; an item the
table doesn't know (another mod's) is the pack's word and stays (but an unknown item anywhere else, a game room's pickable or drop,
counts as above every tier).

- **Automatic:** 1-3 `quaddungeon_giantcog` in every vault and boss chest, and a `quaddungeon_trophy_warden` in every boss chest. Don't
  list them too unless you want more. Both may be named in any list like any item.
- **The vault reward:** when the vault opens (a gate in the end room or a room tagged `vault`, or the boss dies), each player inside gets
  3 rolls from the `vault` list for the land (else `rare`), into their inventory (what doesn't fit at their feet), once per character per
  time the dungeon is new.

## Entrance looks: packs/looks/<name>.json

Read before a world loads, when the entrance prefabs are registered (`EntranceLooks.cs`; `LookCheck.cs` says what's wrong in one). Each
look is its own saved prefab: `quaddungeon_entrance_<name>`, or `quaddungeon_entrance` for `crypt`.

```json
{ "name": "giant",
  "about": "for whoever edits it (not read)",
  "front": 5,
  "parts": [ { "prefab": "Ashlands_Boss_Pillar", "at": [x, y, z], "euler": [...], "scale": [...] },
             { "location": "Crypt4", "keep": ["exterior", "Gateway/Cube"], "doorway": [0, 0, 6.96], "at": [0, 0, 0], "euler": [0, 0, 0] } ],
  "inside": [ { "prefab": "...", "at": [...], "euler": [...], "scale": [...] } ] }
```

- `name`: lower case letters, digits and `_`. **A built-in look is a saved prefab of its own**: adding one to the DLL is fine, renaming or
  removing one leaves its entrances as "Missing prefab". A look from the config folder or the server is only a name in the entrance's
  ZDO on the default prefab: removing its file shows the stand-in door until it's back.
- `front`: where players come out, metres in front of the door.
- `parts`: in the entrance's frame (+z out, towards whoever comes to it; y 0 the ground, anything below it buried). A prefab part is scenery; a location part copies
  the named children of one of the game's locations so the location's `doorway` point lands at `at`.
- The stand-in (the crypt door) shows until the look is built, and stays if a location part, or more than half of the prefab parts, is
  missing.
- `inside`: the way out as seen from inside, in the way-in doorway's frame (origin on its floor, +z into the room), prefabs only. None:
  the stand-in door, scaled to the doorway.
- The looks today: `crypt` (the Black Forest burial chamber, location `Crypt4`), `dvergr` (a black marble Dvergr gate), `giant` (Shifting
  Halls: a 7 x 10 m iron gate between grausten pillars in a hill, a cogwheel, a giant's skull and sword, blue flames).
