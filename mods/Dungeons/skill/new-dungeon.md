# Make a new dungeon kind: the playbook

From an idea to a pack that passes the gate. A dungeon kind is a pack (pack-format.md): its own rooms, mechanics, enemies, loot and
entrance. This file says how to make one that's worth playing; the other files say what each field does.

> Everything here is in the code (`tools/dungeons` and `mods/Dungeons`). The brazier-order and push-block jobs wait for their mechanisms
> in the recipes.

## What the player cares about

The player who plays these dungeons said what makes one good or bad (their words, from the first day of play-tests). Hold every new kind
to it:

- **A look that's in nothing in the game yet, but belongs in Valheim.** "I want this to look like it's not something in the game at
  all... but it belongs here." "I just want them to look as best as possible." New architecture made from the game's own pieces and props,
  not a copy of a crypt or a Dvergr town. (They chose the Giants' clockwork vault for Shifting Halls.)
- **Rooms with jobs, not walk-throughs.** "Most of them you're just kind of walking straight through." Their bar is the Ocarina of Time
  temples: rooms that are puzzles. Every room should ask something of the player: a fight, a lever to find, a way to work out, a secret,
  a reward. A corridor is there to join two rooms that have jobs.
- **Real fights.** (Relayed, close to their words: "Fight rooms must be real fights. A room with one skeleton and a lever is the same as
  a walk-through.") "The lever doesn't activate until you kill all the enemies... Or maybe whenever you pull the lever the first time, the
  enemies spawn." A lever starts the waves, you kill them all, and the unlock sound tells you it's done (encounters: pack-format.md; the
  sound: `Mechs.cs`, `sfx_gui_craftitem_workbench_end` with `sfx_secretfound`).
- **An epic boss battle at the end.** After beating Shifting Halls: "The boss was really lame... it needs to be just so much more epic
  and like an epic battle. Most of our dungeons should be epic boss battles." The mini boss "also felt a little lame", and the Warden
  "spawned on this pedestal area and it looked like he couldn't actually get down". So **every dungeon kind ends in an epic boss battle**:
  an intro, telegraphed attacks, arena phases, mechanics the players use against it, a look of its own, and a death moment. **Mini bosses
  get a smaller set piece.** And **a boss must be able to reach the players**: the gate has to prove it. How: bosses.md.
- **Words that say what happens.** "What does it mean when it says it grinds faster?" Every `message`, `banner` and `hint` says the
  effect plainly first, flavour after: "faster and harder-hitting! (its gears grind faster)". And a boss's second wind: "get him down to
  30% health first, and then whenever he goes to the next phase, then he goes back up to 50 or 60%" (bosses.md, `healTo`).
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
- **Easy for an AI to author, without feeling repetitive** (relayed). That's what specs, themes and recipes are for.
- **A system, not one-offs.** "For now I think mostly dungeons are going to be coded by AI." "I still think building a system around it
  is better than from scratch every time." And later: "we need to make sure that players later can make packs." So a new kind is data and
  generator code on top of the system, never a change to the system for one dungeon.

## The variety rules

- **A recipe makes rooms.** Rooms come from recipes (a room's job, its shell, its features), not from copying a room and changing it.
  The same job made again with another seed gives another room (below).
- **A theme is architecture, not textures.** A theme decides shapes (how walls, floors, roofs, doorways, stairs and supports are built,
  how big things are), not just which stone they're made of. Swapping grausten for black marble makes the same dungeon.
- **The vault's reward is a real chest worth the climb.** Shifting Halls' vault holds the game's biggest chest, the Jotunn's
  `Morkhalla_ChestAncient` (32 slots, saved at scale 1), emptied when it's made and filled by the builder from the pack's `vault` loot
  (and the giant's cogs). The lever vault and the secret cache use it too. A theme's vault chest is its own choice (the Dvergr test
  theme: `TreasureChest_dvergr_loose_stone`).
- **Every kind has its own signature rooms and mechanics.** Shifting Halls has its four-storey hall of turning stairs and the rotunda
  drum; a new kind needs its own, not the same puzzle in new stone.
- **The gate fails a re-colour.** A pack whose rooms are another pack's rooms with other pieces doesn't pass: "this is X in another
  colour". It also fails too few jobs on the way, the same room again and again, and recipe rooms that are near-copies (check 10,
  below).

## The steps

1. **The idea.** What the place is, who built it, what the player does there, its signature rooms and mechanics, its enemies' feel, its
   entrance. Hold it against "What the player cares about" above.
2. **The theme.** Use one that exists (`giant`, `dvergr`), or write one (below): a new kind usually wants its own architecture.
3. **The signature rooms**, written by hand in the theme where a recipe can't make them (below).
4. **The boss battle** (and the mini bosses): every kind ends in one. Its arena, intro, telegraphed attacks, phases, the mechanics players
   use against it, its look and its death moment; mini bosses get a smaller set piece. The gate proves the boss can reach the players.
   The toolkit, rule one (the boss reaches the players: stepped daises, `creature_check.py`) and how to design the fight: bosses.md.
5. **The spec**, `tools/dungeons/specs/<name>.toml` (below): the plan as jobs and signature rooms, the fill, the size, the music, the
   shortcut, creatures and loot per biome.
6. **Make it:** `python tools/dungeons/make_dungeon.py specs/<name>.toml [--seed N] [--out pack.json] [--report report.json]
   [--preview DIR]`. It writes the pack (default `mods/Dungeons/packs/<name>.json`), its variety report (default
   `tools/dungeons/reports/<name>.report.json`) and, with `--preview`, a picture of every room (cut open, from above at an angle).
7. **The entrance look** (pack-format.md: "Entrance looks").
8. **The checks, offline:** `walk_check.py`, `prop_check.py`, `lever_check.py` and `box_check.py` (every part, in every pose, inside its
   room's box) on the pack, then the harness until seeds lay out and pass (SKILL.md), then the map for a few seeds (`tools/dungeonmap`:
   map.md). Then **the gate**, which runs all of it and the variety checks: `python tools/dungeons/make_pack.py specs/<name>.toml`
   (below). A kind is done when it passes.
9. **In the game:** `dungeon spawn`, stand by the entrance, then `dungeon tour` on a seed or two (below). Look at every room's views, fix,
   tour again. Then `dungeon mapshot all` on the built one: does the map match what the tour showed? The tour and the map are for
   looking; they prove nothing about solvability: walk it, or `dungeon solve`, and the gate. `dungeon test <pack>` runs the gate's
   per-layout checks inside the game, on this game's own rooms (below).
10. **With the player:** a real run, then what they say goes back into the spec, the theme or the recipes.

## The spec (`tools/dungeons/specs/<name>.toml`)

The short file that says what a dungeon kind **is**, never how a room is built. The format is the docstring of
`tools/dungeons/authoring/spec.py`; `specs/shiftinghalls.toml` and `specs/testtheme.toml` are examples.

```toml
name = "shiftinghalls"                  # the pack's name
title = "$dungeons_pack_shiftinghalls"  # its translation key
theme = "giant"                         # the architecture: authoring/themes/<theme>.py
seed = 1                                # which rooms the recipes make (another seed: other rooms, the same kind)
difficulty = "normal"                   # easy | normal | hard: every fight's waves one melee fewer or more
biome = "any"                           # where its entrances go; creatures and loot always follow the entrance's biome
environment = "Caves"
entrance_look = "giant"
rooms = [16, 28]                        # a layout's room count (min, max)
max_size = [320, 140, 320]
min_uses = 8                            # the puzzle's fewest uses
music = "forestcrypt"                   # the track inside (fights and bosses set their own)
parkour = false

plan = [                                # the way through, in order: a job (a recipe) or a room (the theme's signature room)
  {id = "start", room = "entrance"},
  {id = "fight1", job = "fight"},
  {id = "lever1", job = "lever", from = "hub1"},
  {id = "gate1", job = "gate", opens_with = "lever1", from = "hub1"},
  {id = "hall", rooms = ["hall_a", "hall_b", "hall_c"]},   # a choice: the layout picks one
]                                       # a step may also have variants = n, and tag = "..." (a signature without tags)

[fill]                                  # recipe rooms besides the plan's: job = how many different rooms
breather = 8
nook = 4

signatures = ["greathall", "cap_gate"]  # more of the theme's hand-made rooms, used where they fit
variants = 2                            # rooms made per job step of the plan (the layout picks one)

[shortcut]                              # optional
near = "shortcut_near"
far = "shortcut_far"
far_step = "guardian1"

[creatures]                             # optional: per-biome role lists (default: dungeon_parts.CREATURES)
[loot]                                  # optional: per-biome lists (default: dungeon_parts.LOOT)
```

## Choosing and writing a theme

### Themes (`tools/dungeons/authoring/themes/<theme>.py`)

A theme is a class on `authoring/theme.py`'s base: **architecture, not textures**. Two exist: `giant` (the Shifting Halls: vast, 12 m
storeys) and `dvergr` (tight and many-levelled). A theme sets:
- **its scale**: `door_w`, `door_h`, `wall_t`, `floor_t`, `storey`, `height`, `grid`, `step_rise`, `step_run`, `way_width` (the clear way
  kept between doorways, 3 m by default), `lever_base`, `panel` (the largest wall, floor or ceiling piece before textures smear: Dvergr
  marble 4 m, giant grausten 10 m), and optionally `pit_gaps` (a pit's widths, always more than the 6 m jump);
- **its palette**, by role (below);
- **its builders**: walls, floors, door frames, stairs, rails, bars, pillars, levers, gates, plates, chests, lights, gears, decor, dressing
  sets, nook pieces, remains;
- **its shells** (with weights), sizes per size class, what it forbids or reshapes, and `signatures()`: its hand-made rooms.

It also decides **shape preferences**: the `shells` weights (giant likes round halls; dvergr has none and favours split and balcony
levels), `sizes` per size class, and `forbid` / `reshape` for jobs. And **how things are built**: `wall_run` / `wall_panel` (a floor slab
stood up, or an upright piece as it is), `floor`, `ceiling`, `block`, `door_frame`, `stairs`, `rail`, `bars`, `pillar`; `lever`, `gate`,
`plate`, `chest`, `spawner`; `light_stand`, `light_hang` (always hung on a chain from what's above); `gear`, `machine`, `big_gear_wall`;
`decor()`, `dressing_sets()`, `nook_pieces()`, `remains()`; `signatures()`. So the same job becomes a genuinely different room.

**Choosing:** if the dungeon's scale and materials match a theme, use it and change only the spec. A new kind with a new look needs a new
theme: new builders and shells make new shapes. Changing only the palette's pieces is the re-colour the gate fails.

**Writing one:**
1. Copy `authoring/themes/dvergr.py`.
2. Set the scale.
3. Map roles to pieces from `palettes/<theme>.json` (below).
4. Override the builders that differ (upright wall pieces aren't tilted; steps can be a stair block).
5. Add 1-3 signature rooms.
6. `python recipe_lab.py <theme> all 10` until every check is 0, then `recipe_sheet.py` to look.

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
    `render`, `height` (metres at scale 1), `upright` (`flat`: thin along y, a floor tile to stand up as a wall; `upright`: a wall piece
    used as it is; `block`), `safe` (fine as stripped scenery), `saved_only` (chests, pickables, spawners: only as saved parts),
    `climb` (`ledges` and `top`: below).
  - `tier` is the land the piece **looks** like: it decides nothing about enemies or loot (those follow the entrance's biome), so a Deep
    North-looking hall can stand in the Mountains.
  - `roles`: `{role: {pieces: [default, variants...], how}}`, in the recipes' words: `wall`, `wall_alt`, `floor`, `ceiling`, `trim`,
    `pillar`, `column`, `beam`, `rail`, `grate`, `door_leaf`, `stair`, `stair_spiral`, `light_stand`, `light_hang`, `light_wall`,
    `gear`, `machine`, `chain`, `chest`, `decor_small`, `decor_large`, `rubble`, `bones`. A role may name pieces from `shared.json`.
    `by_role` is the same as a flat `{role: [default, variants...]}`.
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
  `pathblocker`, `viewblock` and `smoke`; of those the builder makes only `item` solid (items used as scenery, like the gears).
  **Banners and curtains are solid** (the piece layer: `Morkhalla_Banner1`, `dvergrprops_banner`, `dvergrprops_curtain`,
  `piece_banner01`); only crypt cloth (`cloth_hanging_long`), webs, candles, rugs and `Morkhalla_Stonepile` are walk-through.
- **Footholds, not height, decide what players climb** (numbers from the game's own files). A jump lifts the feet 1.6 m at Jump skill 0
  and 3.1 m at Jump 100 (gravity -20, jump force 8 x (1 + 0.4 x Jump skill)). A foothold is a face no steeper than 38 degrees (players
  slide off steeper ones) with room for the feet. Each palette piece has `climb`: `ledges`, `top` (the highest a player can stand on it
  at Jump 0; 0: they can't get on it) and `top_skilled` (at Jump 100). Standing on a top, a player reaches `top` + 1.6 m (or
  `top_skilled` + 3.1 m). Examples: banners, poles and grates 0; railings only with high Jump skill (2.6); crates 1.2, barrels and
  shelves 1.6; giant skulls and helmets are climbable mounds; the carved `Ashlands_Boss_Pillar` is **not** climbable (its twist is too
  steep). Stairs, floors and walls are walkable geometry, not props, and aren't counted as stepping stones. **In a room with levels, keep
  out props whose `top` + 1.6 m (or `top_skilled` + 3.1 m) reaches a level players shouldn't get to.**
- **Parkour is off inside a pack with `"parkour": false`** (Shifting Halls has it), so the plain game's rule above is the one that counts
  there. **A pack that allows Parkour must also be checked for wall-climb routes:** with Quad's Parkour any solid face climbs to about
  6 m (a wall climb of 2-4 m by skill, then a ledge grab), dungeon walls included, and only height protects (the giant storeys are 12 m).
  The recipes' "rise less 2.1 m" limit (below) comes from Parkour. The gap check fails layouts on the plain game's rule; reporting
  Parkour routes separately is proposed to dungeon-layout2, not built.
- **As scenery, everything usable or loot-giving is stripped.** Chests, pickables and spawners only work as `saved` parts at scale 1.
- **Hanging things need something right above them, or a chain on their top** (the 35 floating lanterns).
- **Draw calls**: most piece meshes aren't readable, so each part is one: prefer one big scaled slab to many small pieces (a few hundred
  parts a room is fine). A few strong lights beat many small ones (players cap point lights; the game fades lights at about 40 m).
- **Thin pieces** (under 0.15 m) get a "may be one-sided" note: look at the render from both sides.

## When to write a signature room by hand

A theme's `signatures()` are its hand-made rooms; the spec puts them in the plan (`room = "..."`, or `rooms = [...]` for a choice) and in
`signatures` for more. Write a room by hand when **the room is the dungeon's identity, or a puzzle recipes can't express**: a recipe makes a room
around one job in a shell. Shifting Halls' hand-made rooms (`shifting_halls.py`, the old generator, kept as their source):
- the Shifting Hall: lever puzzles searched offline, three versions in its `HALLS` table with their declared falls;
- the ribcage tunnel: a hollow prefab, measured on its collider;
- the Warden's arena: boss phases tied to machinery;
- the rotunda (a turning drum) and the crane shaft;
- the entrance and the vault: the start and the end.

Shifting Halls is **recipe-built**: `specs/shiftinghalls.toml` → `make_dungeon.py`, installed by the gate (`make_pack.py specs/shiftinghalls.toml --install`). About 36 of its rooms come from recipes; its signature rooms are hand-made (`shifting_halls.py`: the entrance, rotunda, great hall, the three turning-stair halls, the vault and its caps, the secret rooms, the shortcut's doors, the end caps), and its boss rooms (the Warden's hall, the Cog-Thrall's guardian room) come from `boss_rooms.py`.

Recipes are the backbone; every kind still needs 1-3 signature rooms of its own (the gate checks one is on the way in 90% of layouts).

## Recipes and jobs (`tools/dungeons/authoring/`)

`recipes.py` (the engine, shells, features, dressing), `jobs.py` and `jobs_levels.py` (the jobs), `core.py` (the floor plan), `shells.py`,
`variety.py` (the generator's report).

**A recipe** is a `Job` subclass registered with `@job` (`recipes.py`). Its fields: `name`, `tags` (the plan step tag it serves),
`size` (a size class the theme defines), `shapes` (allowed shells), `storeys`, `weight`, `max`, `min_depth`, `end_cap`, `door_ids` and
`pattern` (`through`, `turn` or `any`), the `features` it allows, `dressing`, `lights`, `ambient` (min and max spawners), `side_jobs` and
`side_chance`. Its methods: `doors(c)` (doorways on the shell's slots), `build(c)` (the mechanisms, and reserving what they need),
`connect(c)` (which ways to keep; default all doorways), `targets(c)` (points reachable from `target_from`), `after_ways(c)` (what needs
the ways known, like spawn points), `make_shell(...)` (a shell of its own: the pit, the drop). `make(job, theme, seed)` builds one room,
trying up to 40 shapes and sizes.

**A recipe makes a room in this order:**
1. the shell (rect, L, round, split, balcony; the pit and drop jobs have their own);
2. doorways, from the shell's slots;
3. walls and levels;
4. the job's mechanisms, and what they need kept clear;
5. the ways between doorways, `way_width` wide (3 m);
6. a side job (below);
7. features (pillars, machinery, remains, cover);
8. dressing (a theme's set);
9. lights;
10. ambient enemies.

**The floor plan** (`core.py`) marks each cell OUT, FREE, WAY or TAKEN, and things that only look go only on FREE cells: that is why a
recipe can't block a way.

**The jobs** (`JOBS` in `recipes.py`): `breather`, `corridor` (plain, arches or hammers), `hub`, `stairhall`, `nook`, `fight`, `lever`
(a key), `gate`, `plate` (a held gate, a siege, a lone ledge lever), `guardian`, `timing` (a turning or sinking bridge over a pit), `drop`,
`twolevers`, `ambush`, `timeddoor`, and two quieter kinds that still aren't plain walk-throughs: `pillarhall` and `machineroom`. Brazier order and push block wait for their mechanisms in the recipes.

**Side jobs** (`sidejobs.py`): something to do in a room that's on the way but isn't a plan step, so no room is just walked across. A room
says which it can take (`side_jobs`), and its seed picks one:
- `guard`: a chest of the land's loot in the open; lifting its lid wakes a short ambush (two small waves, no doors shut);
- `sentries`: a few keepers who come for you as you step in (one wave);
- `alcove`: a closet shut by an iron leaf, raised by a lever elsewhere in the room;
- `stash`: a closet walled up with cracked stone, broken open for its chest.

A side job's creatures always have a path to the way in. Fights always have cover and pillars or machinery, and two rooms of the same
job differ clearly (shape, size, galleries, features, dressing).

**When `make()` fails**, the message names the stage that failed.

### The lab

- `python tools/dungeons/recipe_lab.py <theme> <jobs|all> <n>`: for one job's rooms, or all of them: n rooms per job through every check and the harness's gap check.
- `python tools/dungeons/recipe_sheet.py <theme[,theme]> <jobs|all> <seeds> <out>`: contact sheets, themes side by side (the best way to
  see whether two themes really differ, or a job's rooms repeat).

### Lessons from building rooms by recipe (each was a real failure)

1. **A prop's top is a place to stand.** A 3.5 m lantern pole and a 4.3 m banner let players up to a 5 m gallery (with Parkour). The
   recipes keep props lower than a level's rise less 2.1 m, Parkour's reach; in the plain game what counts is footholds (the `climb`
   rule under "The palette's rules" above).
2. **Levers face square to the room.** Diagonal levers made the checks' boxes cover their own front.
3. **Snap levers to the middle of a 0.5 m cell**: a base edge exactly on a cell line looks like a blocked approach.
4. **Keep a 3 m strip beside a lever's approach clear** (the check counts any box touching a cell).
5. **Reserve spawn points before features go down**, or pillars land on them.
6. **Size props from their real boxes at their real yaw**, not a guessed footprint.
7. **A sinking gate must sink inside its room's box** (`box_check.py`).
8. **A turning bridge's ends must stay more than 6 m from the ledges when it's crosswise**; otherwise it must sink instead (6 m is the gap
   check's jump).
9. **Split levels rise a whole storey**, or doorways land on half-storeys and the map gets extra floors.
10. **Upright wall pieces aren't tilted; floor tiles are stood up** (the palette's `how`).
11. **A theme's `panel` caps how far a piece is stretched** (Dvergr marble 4 m, giant grausten 10 m).
12. **Variety reports live in `tools/dungeons/reports`, never in `packs/`**: everything in `packs/` is embedded as a pack.

## Looking at it: `dungeon tour` and the map

`dungeon tour`, `dungeon mapshot` and the offline map renderer are all checked in the game. Code: `MapTour.cs` (the tour), `MapWindow.cs` (`DungeonMap.Shot`: the map shots), `tools/dungeonmap` (offline).

**`dungeon tour [id] [rooms] [views] [bright]`** (a Claude Tools request, or `claude dungeon tour ...` in the console): pictures of a built
dungeon's rooms, moving only the game's camera.
- `id`: the entrance's ZDO id or our own id (4+ letters); left out, the dungeon the player is in, else the nearest built one.
- `rooms`: `all` (default; flat end caps skipped) or a comma list of room indices and names (`3,hall_a,warden`; `dungeon map` lists the
  indices). `views`: per room, 2 by default, at most 6. `bright` lights the pictures more than the game does (the ambient raised to a
  grey dusk, the fog thinned, a soft light at the camera) so shapes, gaps and floating props show in a dark room; its files end in
  `-bright`. Left out, the pictures are as the game draws them. Also `rooms=...`, `views=N`. Examples: `dungeon tour`, `dungeon tour hall_a,warden 4`,
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

- `<spec>`: a `.toml` spec (built with `make_dungeon.py`), a generator `.py`, or a ready pack `.json`.
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
| (4b) | creatures | `creature_check.py` (when it's there): a creature can walk from every encounter's spawn points to the fight's doorway (0.3 m steps, no jumps); "is on an island ... an edge of 0.6 m at (x, y, z)" names the edge to fix. bosses.md. |
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
  measured, and it can't be passed by tagging.
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
