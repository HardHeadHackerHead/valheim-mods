# Authoring rooms with the generators (tools/dungeons)

> For a **new dungeon kind**, start with new-dungeon.md: specs, themes and recipes (`tools/dungeons/authoring/`) make most rooms
> now. This file is the library underneath them, the checks, and how Shifting Halls' hand-made rooms are written.

How the Shifting Halls rooms are made, and how to make a pack of your own the same way. Packs of our own rooms are written by Python
scripts, never by hand: a room is hundreds of parts, and the checks below read the same data.

## The files

- **`dungeon_parts.py`**, the library:
  - `Part`, `Parts` (`add`, `fill`, `stand`), `Mech` (`pose`, `wire`, `join`, `extra`), `Room` (`doorway`, `join`, `extra` for `joinBy`,
    `drops` and `music`; `scene()` for pictures), `Pack` (`room`, `step`, `write`).
  - Geometry: `side_point(k, lateral, y, depth)`, `wall`, `slab`, `box` (a prefab stretched to a box, with yaw and tilt), `cuts`.
  - The per-biome tables `CREATURES` (melee, ranged, flying, elite) and `LOOT` (common, rare, vault, boss).
  - `Pack.write()` refuses a prefab that isn't in the game's ZNetScene (it checks `game_meshes.znetscene_names()`).
- **`giant_kit.py`**, the Giants' clockwork vault style (its style sheet is the comment at its top: every prefab name with its size):
  - Constants: `STONE`, `DARK`, `PILLAR`, `COLUMN`, `GEAR`, `DOOR`, `TORCH`, `CHAIN`...
  - `Shell`: a room's box, walls with door openings, floor, ceiling, door frames.
  - Props: `torch`, `wisp` (a lantern that always hangs on a chain from whatever is above it), `brazier` (optionally on a chain), `crystal`,
    `hang_chain`.
  - Machinery: `gear` (turning or spinning), `machine` (kind `machine`: spinning parts), `axle`, `bar`, `girder`, `stairs` (solid or open),
    `giant_lever`, `grate_wall`, `part_box`.
- **`shifting_halls.py`**, the pack: one function per room; `build()` ends with the plan's steps. The halls are puzzles searched offline
  and kept in a `HALLS` table with their declared falls. `solve(room, entry, falls="declared" | "free" | None)` proves each hall with the
  declared drops, with free descent and with no falls. Helpers: `encounter`, `door_gate`, `cracked_panel`, `secret_room`,
  `shortcut_door`.
- **`testmechs.py`**: the small test pack, one room per mechanism, joined to the game's forest crypt rooms.
- **`palettes/*.json`** (`palette_tool.py`): each theme's chosen pieces by role, with their facts from the game's files: new-dungeon.md.
- **`prefab_bounds.json`**: each prefab's box in its own frame (filled by `bounds()` from the game's files the first time a prefab is used).
- **`prefab_shapes.json`** (`prefab_shapes.py`): a hollow prefab's collider as many boxes (`giant_ribs`: a ribcage you walk through). The
  walk and gap checks use it in place of the one box.
- Pictures: `game_meshes.py` (the game's meshes, kept on this PC only), `dungeon_view.py` (a painter's renderer), `preview_rooms.py` (room
  sheets).

## The checks (each exits 1 on a problem; run them all after every change)

- **`box_check.py`**: every part, in every pose, inside its room's box (a part poking out shows up in the next room).
- **`walk_check.py [pack] [width]`**: every pair of doorways the solver joins must be walkable: 0.5 m columns, 1.8 m headroom, steps up to
  0.6 m, jumps 1.5 m up. With a width (3), only ways that wide count. Rough on stairs: check those by eye.
- **`prop_check.py`**: anything standing (lanterns, torches, braziers, chests, bones, rubble, remains, spawners) needs a top within 0.3 m
  under it; hung things and trophies against a wall are exempt. Every encounter spawn point: on a floor, 0.7 m clear at body height, inside
  its area, 3 m from doorways and from the first 6 m of the way in, 2.5 m from the others.
- **`lever_check.py`**: every lever and exit needs a clear 2 x 2 m approach on its pull side and a clear line at eye height (1.6 m) from a
  standing place within 2.5 m to its hub or handle.
- **The harness** (`LayoutTest.exe --seeds 1000`, all packs): pack problems, solvability, faults, and the gap check (SKILL.md).
- **The map** (`tools/dungeonmap`): map.md.

## The workflow

1. Edit the generator.
2. `python shifting_halls.py`: writes the pack and prints each hall's uses.
3. The three checks, then the harness on 1000 seeds.
4. `dotnet build mods/Dungeons -c Release -p:DeployToGame=false` (a plain build copies the DLL into the game's `BepInEx/scripts`,
   `mods/Directory.Build.props`). **Never deploy while a player is in the game.**
5. Pictures (`--preview DIR`), then the game.

## Conventions

- A room's frame: its box centred on 0, x right, y up, z forward. Doorways sit at a wall's face, facing into the room.
- Shifting Halls doorways (type `giant`) are 5 x 8 m, thresholds usually 1 m above the box's bottom, storeys 12 m apart, on an 8 m grid. A
  thick floor is fine (the great hall's is 3.5 m) and keeps sunk scenery inside the box.
- **Every part stays inside its room's box**, and moving parts in every pose: a part poking out shows up inside the neighbouring room. A
  gate that would slide sideways in a narrow room rises instead.
- Prefix our ids and names: `quaddungeon_` (prefabs, ZDO keys) and `Dungeons_` (RPCs). Player-facing words are translation keys
  (`$dungeons_...`, in `translations/*.json`).
- List a stair's steps bottom to top, one after another, so the map finds the flight (map.md).

## Lessons from authoring (each was a real bug)

The system-wide lessons are in SKILL.md; these are about writing rooms.

1. **Scenery is solid.** A giant's ribs lying across the great hall's floor stuck the dungeon. Walk-check every room, not only puzzle rooms,
   with the real part boxes, and clear a 3 m way between all doorways and stair feet.
2. **Low things over walkways.** A stair's gear 2.9 m above its middle step blocked the top stair in every hall. Keep 2 m of headroom over
   every step of a flight, and remember a spinning part sweeps a wider circle than its box.
3. **Declared drops must exist in every state.** Declaring every downward pair let the solver count falls that only exist when a stair is
   underneath. Declare only falls the gap check finds in all states, and prove the puzzle still takes as many uses with free descent
   (`solve(..., falls="free")`).
4. **Levers behind rails.** Players stood with their backs to a rail, and the camera's hover ray hit the rail. A lever needs 2 m of open
   floor on its pull side and a clear line of sight (`lever_check.py`); the mod also gives every lever and exit an aim box.
5. **Spawn points**: on the floor, spread round the room, never in a doorway or on the way in (`prop_check.py`).
6. **Hollow prefabs** need their collider as boxes (`prefab_shapes.json`), or the checks see a solid block. Measure clearances on the
   collider's own triangles.
7. **A step's room needs a way on.** A room whose only exit type matches only the next step (the warden's doorway) needs an end cap of that
   type too.
8. **Don't trust a doubtful picture.** The painter's renderer can draw far bars over near things: check it against the boxes. Render a
   broken breakable without its parts, and turn the tint off for "as the player sees it".
