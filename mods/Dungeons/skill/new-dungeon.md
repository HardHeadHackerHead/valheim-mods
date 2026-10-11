# Make a new dungeon kind: the playbook

From an idea to a pack that passes the gate. A dungeon kind is a pack (pack-format.md): its own rooms, mechanics, enemies, loot and
entrance. This file says how to make one that's worth playing; the other files say what each field does.

> **Being written.** In: the piece palettes, the `make_pack` gate, `dungeon test` and `dungeon tour` (untested in the game). Still
> **(coming)**, from dungeon-content: the spec format, recipes, themes and `make_dungeon.py`. Until they land, Shifting Halls
> (`tools/dungeons/shifting_halls.py`) is the worked example of every step.

## What the player cares about

The player who plays these dungeons said what makes one good or bad (their words, from the first day of play-tests). Hold every new kind
to it:

- **A look that's in nothing in the game yet, but belongs in Valheim.** "I want this to look like it's not something in the game at
  all... but it belongs here." "I just want them to look as best as possible." New architecture made from the game's own pieces and props,
  not a copy of a crypt or a Dvergr town. (They chose the Giants' clockwork vault for Shifting Halls.)
- **Rooms with jobs, not walk-throughs.** "Most of them you're just kind of walking straight through." Their bar is the Ocarina of Time
  temples: rooms that are puzzles. Every room should ask something of the player: a fight, a lever to find, a way to work out, a secret,
  a reward. A corridor is there to join two rooms that have jobs.
- **Real fights.** "The lever doesn't activate until you kill all the enemies... Or maybe whenever you pull the lever the first time, the
  enemies spawn." A lever starts the waves, you kill them all, and the unlock sound tells you it's done (encounters: pack-format.md; the
  sound: `Mechs.cs`, `sfx_gui_craftitem_workbench_end` with `sfx_secretfound`).
- **Levers that read as levers, and do what they look like.** "The levers don't look like levers." "If a lever is only supposed to be
  pulled one time then make that only be pulled one time." "Maybe a unique sound too could help." (One-shot levers: SKILL.md lesson 9.)
- **No cheating the puzzle, and scenery that's solid.** "You can literally jump from one balcony to the next." The gears: "you could walk
  through them." (Lessons 2 and 4; the gap check.)
- **Set pieces you go through, not past.** On the giant ribcage: "It'd be cool if you have to walk through the ribcage."
- **Secrets and shortcuts.** "I do like the idea of secrets with breakable walls... they'll show up on the map." Shortcuts: "see what's
  feasible without like making the dungeon worse."
- **Music that follows the rooms.** "Tie the music into the different rooms too. Maybe during the fights and stuff." (`music` on the
  pack, a room, an encounter, or a trigger.)
- **Enemies and loot fitted to the land.** "There was stuff in that dungeon, I believe from a Mistlands dungeon, that we shouldn't have
  access to yet." (And Mistlands spawners killed the player and Bob: SKILL.md, "Enemies, loot and rewards".)
- **Never repetitive.** "More variety." "The same room with just a different texture is still the same room." "I just want to make sure
  that it's not going to feel repetitive or limiting."
- **A system, not one-offs.** "For now I think mostly dungeons are going to be coded by AI." "I still think building a system around it
  is better than from scratch every time." And later: "we need to make sure that players later can make packs." So a new kind is data and
  generator code on top of the system, never a change to the system for one dungeon.

## The variety rules

- **A recipe makes rooms.** Rooms come from recipes (a room's job, its shape, its mechanics), not from copying a room and changing it.
  **(coming: the recipe format, dungeon-content)**
- **A theme is architecture, not textures.** A theme decides shapes (how walls, floors, roofs, doorways, stairs and supports are built,
  how big things are), not just which stone they're made of. Swapping grausten for black marble makes the same dungeon.
  The palettes are in (below); **(coming: the theme format, dungeon-content)**
- **Every kind has its own signature rooms and mechanics.** Shifting Halls has its four-storey hall of turning stairs and the rotunda
  drum; a new kind needs its own, not the same puzzle in new stone.
- **The gate fails a re-colour.** A pack whose rooms are another pack's rooms with other pieces doesn't pass: "this is X in another
  colour". It also fails too few jobs on the way, the same room again and again, and recipe rooms that are near-copies (check 10,
  below).

## The steps

1. **The idea and the spec.** What the place is, who built it, what the player does there, its signature rooms and mechanics, its
   enemies' feel, its entrance. **(coming: the spec format, dungeon-content)**
2. **The theme.** Choose an existing theme or write one (below). **(coming)**
3. **The rooms.** Recipes for most rooms; a signature room by hand where a recipe can't make it (below). Our rooms are written by a
   generator script, never JSON by hand (authoring.md).
4. **The plan.** The steps a dungeon of this kind goes through, keys before locks (pack-format.md: "The plan").
5. **Enemies and loot** per biome (pack-format.md: "Enemies", "Loot").
6. **The entrance look** (pack-format.md: "Entrance looks").
7. **The checks, offline:** the generator's checks (walk, prop, lever: authoring.md), and the harness until seeds lay out and pass
   (SKILL.md). Then the map for a few seeds (`tools/dungeonmap`: floors, stairs, room sizes, mechanisms; cheap, no game: map.md).
   Then **the gate**, which runs all of it and the variety checks: `make_pack.py` (below). A kind is done when it passes.
8. **In the game:** `dungeon spawn`, stand by the entrance, then `dungeon tour` on a seed or two (below). Look at every room's views, fix,
   tour again. Then `dungeon mapshot all` on the built one: does the map match what the tour showed? The tour and the map are for
   looking; they prove nothing about solvability: walk it, or `dungeon solve`, and the gate. `dungeon test <pack>` runs the gate's
   per-layout checks inside the game, on this game's own rooms (below).
9. **With the player:** a real run, then what they say goes back into the pack.

## Choosing and writing a theme

**(Theme recipes: coming, dungeon-content.)** What a theme is built from is in: the piece palettes.

### The piece palettes (`tools/dungeons/palettes/`)

A palette is a theme's chosen list of the game's prefabs that are known to work as dungeon parts, with facts read from the game's files:
`giant.json`, `dvergr.json`, `crypt.json`, and `shared.json` (pieces any theme may use, and `trouble`: pieces that went wrong, and why).

- **Made by** `python tools/dungeons/palette_tool.py [theme ...] [--no-render]` (exits 1 on any problem). The choices (each piece's role,
  look, mount, scale range, look tier, notes) live in `CURATED`, `ROLES` and `HOW` at the top of that file: to add a piece or a theme,
  edit there and run it again. It also renders each piece from two sides and a `<theme>_sheet.png` of all of them, into the mesh cache
  (`game_meshes.CACHE`): the game's assets, never committed.
- **Each file**: `theme`, `about` (the look in words, grid and storey sizes), `fields` (what each field means), `pieces`, `roles`.
  - A piece: `prefab`, `role`, `look`, `mount` (`ground`, `hang`, `wall`, `span`, `structure`, `free`), `scale` [min, max], `tier`,
    `exists`, `size`, `min`, `max`, `origin` (where its origin sits on its box: foot, top, centre...), `hollow`, `kinds`, `colliders`,
    `solid`, `lights` (range, intensity, colour, lit, lod), `fire`, `particles`, `item`, `networked`, `gives`, `physics`, `notes`,
    `render`.
  - `tier` is the land the piece **looks** like: it decides nothing about enemies or loot (those follow the entrance's biome), so a Deep
    North-looking hall can stand in the Mountains.
  - `roles`: `{role: {pieces: [default, variants...], how}}`, in the recipes' words: `wall`, `wall_alt`, `floor`, `ceiling`, `trim`,
    `pillar`, `column`, `beam`, `rail`, `grate`, `door_leaf`, `stair`, `stair_spiral`, `light_stand`, `light_hang`, `light_wall`,
    `gear`, `machine`, `chain`, `chest`, `decor_small`, `decor_large`, `rubble`, `bones`. A role may name pieces from `shared.json`.
- **How a theme uses one**: a theme recipe picks each role's default and variants, and places them by the role's `how` (a floor slab stood
  up as a wall: euler (-90, side yaw, 0); a ceiling turned over: (180, 0, 0)), the piece's origin, box, mount and scale range. Then the
  pack writer refuses any prefab not in ZNetScene, `prop_check.py` checks that `ground` props stand on something, and the walk and gap
  checks use the boxes.

### Choosing pieces: new, but still Valheim

- **Make the newness from combination and scale, not invented assets.** Build from the game's own families: they share materials and
  light. The giants' vault is Ashlands grausten, Mistlands black marble, Mórk halla iron and statues and charred gears, at 2 to 17 times
  their size, under cold blue light. Nothing in the game looks like it, yet every piece is the game's.
- **One material family dominant** (walls, floors), **one accent family** (trim, doors), **one light colour**. Two or three families at
  most, or it reads as a junk pile.
- **Contrast a new theme with the others on scale, light and material.** Dvergr is tight and human-sized, black marble and Yggdrasil
  timber, warm orange lanterns against blue wisps; the giants' vault is vast and grey, iron and cold blue.

### The palette's rules

- **Every piece is in the game's ZNetScene**, checked offline from the game's files (the pack writer refuses others). `Morkhalla_MetalPillar`
  exists only inside the Mórk halla rooms.
- **Scale within each piece's [min, max].** Stairs and doors stay at 1 (step height; saved doors). Uneven scale is fine; never negative (a
  mirror). Light range, fade distance and fire grow with scale; brightness doesn't.
- **Structural roles must be solid** (the tool fails otherwise), by the game's own collision matrix. Characters pass through
  `TransparentFX`, `Ignore Raycast`, `UI`, `effect`, `item`, `ghost`, `piece_nonsolid`, `character_ghost`, `hitbox`, `skybox`,
  `pathblocker`, `viewblock` and `smoke`; of those the builder makes only `item` solid (items used as scenery, like the gears). Cloth
  banners and curtains stay walk-through on purpose.
- **As scenery, everything usable or loot-giving is stripped.** Chests, pickables and spawners only work as `saved` parts at scale 1.
- **Hanging things need something right above them, or a chain on their top** (the 35 floating lanterns).
- **Draw calls**: most piece meshes aren't readable, so each part is one: prefer one big scaled slab to many small pieces (a few hundred
  parts a room is fine). A few strong lights beat many small ones (players cap point lights; the game fades lights at about 40 m).
- **Thin pieces** (under 0.15 m) get a "may be one-sided" note: look at the render from both sides.

## When to write a signature room by hand

**(coming, dungeon-content.)** Until then: Shifting Halls' hall, rotunda and vault are written by hand in `shifting_halls.py`, because their
puzzle is their shape (each hall's corners, levers and stairs are searched offline and kept in its `HALLS` table, with their declared
falls).

## Looking at it: `dungeon tour` and the map

**`dungeon tour` is written but hasn't run in the game yet: untested until the lead confirms it.** `dungeon mapshot` and the offline map
renderer are checked. Code: `MapTour.cs` (the tour), `MapWindow.cs` (`DungeonMap.Shot`: the map shots), `tools/dungeonmap` (offline).

**`dungeon tour [id] [rooms] [views]`** (a Claude Tools request, or `claude dungeon tour ...` in the console): pictures of a built
dungeon's rooms, moving only the game's camera.
- `id`: the entrance's ZDO id or our own id (4+ letters); left out, the dungeon the player is in, else the nearest built one.
- `rooms`: `all` (default; flat end caps skipped) or a comma list of room indices and names (`3,hall_a,warden`; `dungeon map` lists the
  indices). `views`: per room, 2 by default, at most 6. Also `rooms=...`, `views=N`. Examples: `dungeon tour`, `dungeon tour hall_a,warden 4`,
  `dungeon tour all 1`.
- **Safe**: it never moves, holds or teleports the player; the camera moves for one drawing at the end of a frame and goes back exactly.
  Pictures are lit as from inside (the pack's environment, torches near the view on), with no HUD. The player can stand outside by the
  entrance (a dungeon is built while its entrance is loaded). It refuses if the dungeon isn't built yet.
- **The views**: 1, from the doorway in (1.5 m in, 2.6 m up, looking at the far middle); 2, from the far side looking back; 3 and on, the
  other doorways, then from high up for rooms two or more floors tall, then corners.
- **The pictures**: `BepInEx/claude/shots/tour/<pack>-<seed>/<room index>-<room name>-<n>.png`, 16:9 at the screen's width (640 to 1920),
  and beside them `tour.json`: the dungeon, the count, the environment, the player inside or outside, and per picture its room, view,
  camera and target in the dungeon's frame.

**`dungeon mapshot [floor] [all] [open]` / `mapshot mini`** and **`MapRender.exe`** offline: map.md.

**What to look for in the tour's pictures:**
- **Its look**: each room reads as this kind, not as a grey box. Walls, floors and ceilings meet with no holes or gaps (light leaking in,
  the sky showing). Props in reach and not floating. Light enough to play in, dark where the kind should be dark.
- **Repetition**: put the same room from different seeds side by side, and different rooms side by side. Rooms that differ only in size,
  the same row of props in every room, or a long run of the same corridor: the kind needs more rooms or random props.
- **Rooms with jobs**: from where a player comes in (view 1), is the lever, plate, gate, pedestal, brazier, chest or boss arena in sight?
  Can you tell what it does (a gate in its doorway, a lever by the door it opens)? Does a secret wall look like a crack, not like every
  other wall?
- **Doorways**: view 1 stands in one. A picture full of stone or black means a doorway or a room's size is off: fix the room's `size` or
  the doorway's `at`.

**What to look for in the map** (the offline renderer first, then `mapshot`): rooms on the floors you expect, with no thin extra floors (a
floor's doorways within 2.5 m of each other in height); each room's size matching its walls (a striped "upper part" where you didn't mean a
tall room: its box is too tall); stairs found, with arrows the right way (steps listed bottom to top, prefab names with stair, step or
ladder); gates and mechanisms where they belong; secret rooms hidden until broken; the vault and the way out marked; and with `--visit`,
fog that shows the way on (dashed doorways), not dead walls.

## The gate: `make_pack.py`

`python tools/dungeons/make_pack.py <spec> [--seeds 1000] [--keep-going] [--install] [--out DIR]`. **Exit 0 is a pass.**

- `<spec>`: a `.toml` spec (dungeon-content's `make_dungeon.py` builds it: **coming**), a generator `.py`, or a ready pack `.json`.
- Output in `DIR` (default `tools/dungeons/bin/gate/out/<pack>`, not committed): `report.txt`; `maps/seed<N>/dungeon-map-<floor>.png`
  with `legend.png`; `rooms/` (if the generator draws); `layouts.jsonl`; and for a `.toml`, `<name>.json` and `variety_report.json`.
- `--install` copies the built pack into `mods/Dungeons/packs` only after every check passes.
- It stops at the first FAIL (`--keep-going` runs them all). Shifting Halls takes about 80 s.
- Code: `make_pack.py`, `variety.py`, the harness (`Program.cs`, `GapCheck.cs`), and `mods/Dungeons/LayoutAudit.cs` (the per-layout
  checks and the replay, shared with `dungeon test`).

**The ten checks:**

| # | Check | What fails it, and what to do |
|---|---|---|
| 1 | generation | The generator's exit code; a refusal says why (an unknown prefab, a recipe that can't fit). Fix the spec or the kit. |
| 2 | pack | The pack reader's problems (unknown kinds, fields, doorways, targets; state counts; encounter, boss, shortcut, secret and look fields), each naming pack, room and mechanism. Fix the JSON or the generator. |
| 3 | walk | `walk_check.py`: a way the solver counts that can't be walked. |
| 4 | props | `prop_check.py`: a prop standing on nothing; a bad spawn point. |
| 5 | levers | `lever_check.py`: a lever or exit without room to stand or a clear aim. |
| 6 | gaps | The gap check, every room of ours in every mechanism state: "walkable" or "jumpable" (a way round the puzzle: close it), "only with a fall" (declare it in `drops` or close it), "blocked" (a join nobody can walk: the dungeon may be impossible), spawn points off the floor. `GAPDEBUG=room:a:b` traces one. |
| 7 | seeds | Every seed lays out (or why not); no faults (doorways meet, boxes, caps, gates on locks, wires, doors, secrets, shortcut); every solution replayed, triggers included. |
| 8 | maps | The in-game map of the first seed and of the one with the longest solution. Look at them (map.md). |
| 9 | look | The pack's `entranceLook` file exists and reads. |
| 10 | variety | `variety.py`, below. |

**Check 10, variety** (every threshold is a `variety.py` flag: `--min-jobs`, `--window`, `--recipe-window`, `--spread`,
`--recipe-spread`, `--overlap`, `--share`...):
- **Jobs.** A room's jobs come from its mechanisms (lever, plate, gate, lock, stair, rotator with joins, platform, floor, fight, boss, secret
  wall, shortcut, pedestal, brazier, push block, trap, teleporter), a `fight` tag, a `signature` or `setpiece` tag (or `signature: true`
  in the generator's report), triggers and drops. At least 4 different jobs on the way to the end in 95% of layouts.
- **Jobless rooms and connectors** (warnings for now; `--jobless-fail` makes all three fail). On the way from the entrance to the end (the
  entrance left out):
  - rooms with no job that aren't connectors may be at most 30% (`--jobless`);
  - connectors may be at most 25% of the rooms on the way (`--connectors`);
  - a **connector** is a jobless room with 2 or more doorways that is 8 m or less across its narrow side (corridors, galleries,
    stairwells). The `connector` tag doesn't make a wider room one: a wider room tagged `connector` counts as a room, with the warning
    "<room> is tagged "connector" but its narrow side is N m (over 8): counted as a room".

  The report lists the jobless rooms by how often they're on the way, and the share with connectors counted. **The fix is to give big
  jobless rooms a job** (a guarded chest, a small puzzle, a fight, a set piece), and to keep connectors narrow and few. Shifting Halls
  today: 34% (a warning), 45% with connectors. This is the player's "most of them you're just kind of walking straight through",
  measured, and it can't be passed by tagging. Shifting Halls today: 34% (a warning), 45% with
  connectors. This is the player's "most of them you're just kind of walking straight through", measured.
- **The same room.** The same room (by name) twice within 3 rooms on the way (counting rooms with a job) in at most 10% of layouts. With
  the generator's report, also the same recipe and shell within 6 rooms, at most 10%.
- **Spread.** Rooms of one recipe (end caps aside) must differ: a mean normalised distance of their parameters (features count as one more)
  of at least 0.25, and no pair under 0.15 ("near-copies").
- **The signature room** on the way in 90% of layouts (with the generator's report).
- **Re-colour.** A likeness of structure against every other pack not named `test*`: the mean of plan-step similarity, room-shape overlap
  (size to 4 m, doorway count, jobs; prefabs ignored) and the mix of mechanisms. At 0.70 or more it fails: "this is X in another colour"
  when their prefab sets overlap under 50%, "this is X again" otherwise. And the generator's own overlap share (rooms whose recipe, shell
  and features occur in another pack) at most 40%.

**Reading the report**: a pass is every line PASS and `RESULT: PASS`; a warning shows as "PASS ... passes, with a warning: ...", and
the variety numbers are always listed, also on a pass. A FAIL line gives the reason with the details indented under it:
fix that and run it again (the checks after it haven't run yet).

## In the game: `dungeon test <pack> [seeds]`

Needs a world; changes nothing, so no cheat rule. Default 200 seeds, at most 5000. It reads the pack (its problems), lays out the seeds
over several frames, and checks every layout with `LayoutAudit` (the same checks and replay as the gate's seeds step), on this game's own
rooms. The answer: pass or FAIL, the problems, good and failed seeds with reasons, faults by kind with the first example, solution
lengths, and milliseconds a layout. No geometry checks, maps or variety: those need the offline prefab boxes. Code: `PackTest.cs`.
