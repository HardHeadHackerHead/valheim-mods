# Quad's Dungeons: the design (agreed 2026-10-11)

One dungeon system, and each kind of dungeon plugged into it as a **pack**. The first real pack is **Shifting Halls** (multi-level, puzzly,
staircases that move). A plain **Test Crypt** pack proves the system before that. Placement in the world comes last: until then a
command puts an entrance anywhere.

"Quad's Dungeons": folder and DLL `Dungeons`, GUID `com.quad.dungeons`. `modcheck` checks that no popular mod uses the same assembly
name; if one does, the DLL gets another name.

**Decided with the player:**
- Dungeons **reset after some in-game days**: enemies, loot and the puzzle come back. The number of days is a server setting.
- **The best look we can get:** the game's own dungeon rooms wherever they fit, with our own rooms (built from the game's pieces and
  props) only where the game has nothing (below).
- Tests happen with the player in the game.

## How the game does its own dungeons (read in its code)

- A location with an interior (`Location.m_hasInterior`) builds its rooms **5000 m above** the entrance
  (`Location.Awake`: `position.y + 5000`), and the entrance is a `Teleport` that moves the player up there.
- **Anything above 3000 m is "inside"** (`Character.InInterior`: `y > 3000`), so it gets no rain or wind and no building, and the game
  already treats it as a dungeon. We use the same height, so all of that comes for free.
- `EnvZone` gives an area its own environment (the crypts' darkness and fog).
- **Every dungeon room the game has is in `DungeonDB`**: one `RoomData` each, with a prefab loaded on demand (`m_prefab.Load()`), a theme
  (Crypt, SunkenCrypt, Cave, ForestCrypt, GoblinCamp, DvergerTown, AshlandRuins, FortressRuins, MorkHalla...), a size (`Room.m_size`)
  and doorways (`RoomConnection`: a position, a type such as a crypt door, and whether a door may go there).
- **The game builds a dungeon in two halves** (`DungeonGenerator.PlaceRoom`):
  - the room's **shell** (walls, floors, lights, props) is instantiated on every player's game, never saved;
  - its **networked parts** (doors, chests, spawners, pots: everything with a `ZNetView`) are instantiated once, by the game generating
    it, and saved.
  This is exactly our model, so we can use the game's own rooms as they are.
- `DungeonGenerator` only joins rooms by their doorways on one level, with no plan and no moving parts, so we write our own layout
  builder (below). It uses the game's rooms and ours together.
- `ZNetView.m_forceDisableInit` is how the game builds placement ghosts from networked prefabs without making saved objects. We use it to
  build rooms out of the game's pieces.
- `PlayerProfile.SaveLogoutPoint` saves where the player logged out (see Safety).

## The core idea: a dungeon is a seed and a pack

- **One saved object per dungeon**: the entrance, a networked piece of ours. Its ZDO holds the pack name, the seed and the dungeon's
  **state**: doors opened, levers pulled, stairs' positions, rooms whose enemies were spawned, chests looted.
- **The rooms themselves are never saved.** Every player's game builds the same dungeon from the pack and the seed, locally, when they
  come near it, and removes it when everyone has left. Saves stay tiny, nothing gets out of step between players, and nothing is left
  behind if the mod is removed (apart from the entrance, which then shows as a "Missing prefab" line in the log).
- Only the entrance's **owner** writes its state. Other players ask through an RPC, `Dungeons_Act`, which carries a lever's id and the
  sender (the pitfalls doc's ownership rules).

## The layout builder

- **Rooms join at doorways**, as the game's do. Each doorway has a position, a facing and a type (a crypt door, a cave tunnel...), and two
  rooms join where doorways of the same type meet face to face. Each room's box must not overlap any other's.
- **This works in 3D:** our own rooms add doorways that go up and down (stair landings, shafts, balconies), with the same types as the
  game's doorways, so a game crypt corridor can lead into our tall hall.
- A pack's rooms are **the game's rooms by name or theme** (their doorways read from the prefab) **plus its own rooms** (parts, below),
  each with how often it may appear and where (only at the end, only once...).
- **Building a layout from a seed:**
  1. **The plan first:** the pack's rules give a chain of what must happen, for example "entrance, then lever A, which moves stair 1,
     then the gallery, then key B, then the vault". This is how puzzle dungeons stay solvable: every lock comes after its key.
  2. **Then the space:** rooms are placed in the grid to fit the plan, growing from the entrance and joining open sockets. They go up and
     down floors, keep to a size limit, and fill dead ends with side rooms.
  3. **Then checking:** a path must exist from the entrance to the end, using only what the plan unlocks so far. If it doesn't, try the
     next seed.
- The same seed always gives the same dungeon: a fixed random generator of our own (never Unity's, which other code also uses).

## Building rooms from parts

- A room is a list of **parts**, each with a prefab, a position, a rotation and an optional scale:
  - **the game's build pieces**: stone walls and floors, stairs, pillars, iron gates, torches, banners;
  - **its props**: bones, pots, cobwebs, chains;
  - possibly **the meshes of the game's own dungeon rooms** (crypt walls, arches, stairs): to check whether we can reach them.
- Each part is built without a ZDO (`m_forceDisableInit`). Its `ZNetView`, `WearNTear` and `Piece` are removed, so it can't be broken,
  saved or deconstructed. Lights and fire effects stay. Static parts are batched together so a big dungeon stays smooth.
- **Most rooms are the game's own**, built the way the game does it: the shell locally, the networked parts once by the owner (doors,
  chests and spawners from the game's room, or the pack's own instead).
- **Our own rooms are made two ways:**
  - **in the game:** build the room with normal pieces in a test world, then `dungeon capture <pack> <room>` saves every piece inside
    the room's cells as a room file (no art tools needed);
  - **in a script**, like `tools/blueprints/tools/parkour_course.py`, for rooms made of many repeated parts.
- An `EnvZone` over the whole dungeon gives it the pack's environment (dark and foggy for the crypt).

## Moving parts and puzzles

- **Kinds:** doors and gates, levers, pressure plates, moving platforms, **turning staircases**, rotating rooms, and floors that fall
  away.
- **Same for everyone:** a moving part's position comes from its state in the ZDO (which way, since when) and the server's clock
  (`ZNet.GetTimeSeconds`). Every player computes the same position each frame, and only a change of state is ever sent.
- A player standing on something that moves is carried with it (moved by the same amount each physics step).
- **The pack decides what is wired to what:** "lever A turns stair 1 a quarter turn", "plate B holds gate C open while something stands
  on it".

## Enemies and loot

- A room's spawn points spawn the pack's creatures **once**, the first time someone enters it. Only the owner spawns them, and it marks
  the room as spawned in the ZDO, so nobody spawns them twice. Creatures are ordinary saved game creatures.
- Chests are filled once from the pack's loot table, by the owner, and marked as looted when emptied. The pitfalls doc's rules apply:
  never claim ownership and then hand out items yourself.

## Resets

After the server's setting in in-game days (counted from when the dungeon was first entered, by the game's day count), the owner clears
the state: the puzzle goes back to the start, rooms spawn again and chests fill again. Creatures still alive from the last time are left
alone, and items players dropped inside stay. A reset never happens while a player is inside.

## Safety (what must never happen)

- **Never fall 5000 m.**
  - When a player logs out inside a dungeon, their saved logout point is changed to the entrance outside (a patch on `SaveLogoutPoint`).
    They log back in at the entrance, and if the mod has been removed meanwhile they are still safe.
  - When a player is above a dungeon before it's built (after a teleport, or a lag spike), they are held in place until it is.
  - Fall damage counts from the floor they stand on (as Parkour does).
- **Never lose items:** a death inside leaves the gravestone inside, as in the game's crypts. A "Leave" stone in every dungeon's
  entrance room is always reachable.
- **Reload level:** the entrance is a registered prefab, so the mod reloads only at the main menu (`menu`). The server and every player
  need the same version (a version check).

## Packs

A pack is a folder: `pack.json` (name, environment, grid size limits, rules, creatures, loot), `rooms/*.json` and the plan's rules.
They are built into the DLL first, and later read from `BepInEx/config/Dungeons/packs` too, so other people can make packs.

### Test Crypt (to prove the system)
About 6 rooms on two floors: an entrance hall, corridors, a crossing, stairs down, a side room with a chest, and an end room with a few
skeletons. One lever opens one gate. Its only job is to show that every part of the core works.

### Shifting Halls (the first real pack)
- A tall central hall, three or four storeys, with balconies around it. Rooms open off each storey.
- **Turning staircases** in the hall link the balconies. A lever turns one a quarter turn, so it joins a different pair of landings.
  Some turn on their own every so often.
- **Rotating rooms:** a room that turns to face a different doorway.
- **The puzzle:** to reach the vault at the top, set the stairs in the right order, using keys and levers found in rooms on the way.
  The plan step makes sure it can always be solved.
- Enemies fit the biome the entrance is in (decided later, with placement). For testing, skeletons and ghosts.

## Commands (Claude Tools, for testing; they change the world, so they follow the cheat rule)

| Command | Does |
|---|---|
| `dungeon spawn <pack> [seed]` | Puts an entrance in front of the player. |
| `dungeon list` | The dungeons in the world: pack, seed, where, state. |
| `dungeon enter [id]` / `dungeon leave` | Goes in or out as the entrance would. |
| `dungeon map [id]` | The layout floor by floor, as text and as a picture: rooms, openings, the plan's steps. |
| `dungeon state [id]` / `dungeon set <id> <part> <value>` | Reads or changes levers, stairs and doors. |
| `dungeon reset [id]` | Back to new now: state cleared, enemies and chests again (as the timed reset does). |
| `dungeon rooms [theme]` | Every room the game has: name, theme, size, doorways and their types, what networked parts it holds. |
| `dungeon capture <pack> <room>` | Saves the pieces built around the player as a room file. |
| `dungeon seeds <pack> <count>` | Builds that many layouts without placing them, and reports failures and sizes (testing the layout builder). |

## Build order

1. **Core:** the entrance piece, `dungeon spawn` and enter/leave, the seeded layout on the grid, building rooms from parts, the
   environment, the safety rules, the state in the ZDO, and the Test Crypt pack.
2. **Moving parts:** doors, levers, plates, platforms, turning stairs and rotating rooms, the same for every player.
3. **Shifting Halls.**
4. **Placement in the world:** which biomes, how far apart, entrance looks per biome, and a command for worlds that already exist.

As with Parkour, agents build the parts side by side, and we test each step in ClaudeTest.

## Open questions

- Which game rooms fit together across themes (doorway types and sizes): `dungeon rooms` lists them all from `DungeonDB` in the game.
- How big can a dungeon be before building it causes a stutter? Build it over several frames if needed, measured in the game.

## Later: a dungeon builder (an idea, not to build yet)

People should one day be able to make their own dungeons for this system: an in-game editor, an outside application, or an AI agent,
in this mod or a companion mod. Nothing of it is built, but the design must keep it possible:
- **Everything a dungeon is lives in its pack** (rooms as parts, mechanisms and wiring, the plan, enemies and loot per biome, the
  entrance look), in the documented JSON format.
- **The checks that make a pack safe work without the game:** the pack reader's problems, and the layout harness that proves every seed
  can be solved.
- **Room capture** (`dungeon capture`) and **packs from a folder** (synced from the server, so every player has the same one) are the
  first steps toward it.
