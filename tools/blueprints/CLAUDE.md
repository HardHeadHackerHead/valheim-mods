# Designing Valheim builds (blueprints) for the BuildOrders mod

You are helping a player design something to build in Valheim: a fort, a house, a bridge, a tower. You write a **blueprint** file; the
BuildOrders mod turns it into **ghost pieces** in the player's world; the player builds the ghosts with real materials (nothing is free).
You work from the game's own data, check every design with the tools here, and (when the player allows it) look at their world through
screenshots, camera views and surveys that the mod saves to files.

## Where things are

This folder is `BepInEx/blueprints` in the player's Valheim folder (in the mod repo the same files are in `tools/blueprints`).

| File | What it is |
|---|---|
| `_pieces.json` | Every buildable piece: name, size (`min`/`max`), named `snaps`, `ascend`, solid `boxes`, `material`, `cost`, the `station` it needs, placement `rules`, `health`; plus `stations` (build range, needs a roof...). **Read this; never guess names or sizes.** |
| `_meshes/` | The real 3D shape of each piece (for previews). Made by `tools/extract_meshes.py`. |
| `_survey.json` | Ground heights, water, biome and existing pieces around a spot (Ctrl+F11 in game, or a `survey` request). |
| `_imports.json` | Where each blueprint was placed (origin and turn), for views and removal. |
| `shots/` | Screenshots and camera views saved by the mod (`latest.png` is the newest; each has a `.json` with where it was taken). |
| `requests/` | Drop request files here to have the game take pictures, survey, place or remove blueprints (needs AllowRequests). |
| `tools/blueprint.py` | Place pieces (by box, snap point or origin), any rotation, `check()` and `summary()` (stations, overlaps, ground, fires, roofs, materials). |
| `tools/stability.py` | Will it stand? The game's own support rules. |
| `tools/preview.py` | `render(items, pieces, "out.png")`: four views with the real shapes. Look at it before handing a design over. |
| `tools/emergency_hut.py`, `tools/wood_fort.py`, `tools/fort.py`, `tools/selftest.py` | Worked examples (a two-bed cabin with a smoke louvre over the fire, wooden fort with towers and stairs, stone keep, small test hut). Copy one to start. |
| `tools/test_tools.py` | Tests for all of the above: run it if you change the tools. |
| `*.json` (no leading `_`) | Blueprints. `X.json.imported` beside one means it has been placed (delete the marker to place it again). |

Files in `tools/` are replaced when the mod updates: copy before editing. Python 3 with `numpy` and `Pillow` is needed for previews only.

## The blueprint file

```json
{ "name": "Stone keep", "anchor": "look", "yaw": 0, "offset": [0,0,0], "auto": false,
  "pieces": [ {"p": "stone_wall_4x2", "x": -8, "y": 1, "z": -10, "ry": 0, "g": true} ] }
```
- `anchor`: `"look"` (the player opens the Plans window with **F11**, presses Place, and puts a preview where they look, turning it as they
  like; it starts facing the way they face), `"bed"` (with `"auto": true` it
  places itself at their bed when they are in a world), `"player"`, or `"world"` with `"at": [x, z]`.
- **Auto-supports:** when placed, every bottom corner (lowest snap point) of a piece within 0.6 m of the blueprint's ground level (y 0) that
  ends up above the real ground gets posts down to it, in that piece's material (setting AutoSupports, up to SupportMaxHeight, 12 m). So a
  design for level ground works on a slope or raised: put its base at y 0 and leave the posts out. Pieces with `g: true` are not given posts.
  For a site you have surveyed you can still place your own posts (as `wood_fort.py --site` does); they then reach the ground and none are added.
- **Level ground:** the player can press L while placing to flatten the ground under the plan's bottom to its floor level as soon as it is
  placed (no hoe needed; at most 8 m of cut or fill). It sets the ground exactly to the floor height (3 m blend at the edges), then places the ghosts (posts only where still needed). Removing the plan restores the ground
  (saved in `_terrain/`) unless pieces of it are built. Request: `level <name|last>` levels a placed plan;
  `check <name|last>` reports post columns that do not reach the ground and how high doors and gates sit above it.
- Posts in a design that do not stand on another piece are lengthened down to the ground when placed. Keep designs for any ground
  (base at y 0); a design fitted to one surveyed hillside only fits there, so save it under its own name (`wood_fort.py --site` writes
  `wooden_fort_site.json`).
- Each piece: `p` prefab, `x y z` metres from the anchor (y above the ground at the anchor), `rx ry rz` degrees (Unity order z, x, y),
  `g: true` to measure `y` from the ground under that piece (follows slopes; for pieces touching the ground). Max 1500 pieces.

## Conventions

- Unity axes: **x right, y up, z forward**. The front of a blueprint is **-z** (the player stands at -z looking towards +z). Yaw 90 turns +z to +x.
- **Origins:** walls, floors, doors, poles, beams, stone walls have their origin in the **middle** (a wall's bottom snap is 1 m below its origin, a
  floor's snap plane is at its origin). **Stake walls, furniture and stations** have it at the **base**. Let the tools work it out.
- **Snap points** (`snaps`, e.g. `"top 1"`, `"bottom 2"`, `"corner 3"`, `"edge 1"`, `"inner bottom"`) are where pieces join in the game, on a 2 m grid.
  Use them: `bp.place_snap(name, snap, point, yaw)`, `bp.place_mid(name, ("top 1","top 2"), point, yaw)` (the middle of an edge),
  `bp.attach(name, snap, other_item, other_snap, yaw)`. `bp.raw(name, x, y, z, yaw)` places by origin; `bp.place(name, x, z, y, ref=..., height=...)` by box.
- **Stairs** rise 1 m over 2 m, 2 m wide; **roofs** (`wood_roof`, 26 degrees) the same; `_45`/`_67` roofs are steeper; `wood_stepladder` 2 over 2.
  Turn any climbing piece with `bp.yaw_for(name, dx, dz)`. Chain stairs top edge to bottom edge. A gable roof: a row of `wood_roof` from each
  long wall; if their tops are 2 m apart bridge them with `wood_roof_top`; close the ends with `wood_wall_roof` wedges (and `wood_wall_roof_top`).
- Doors and gates: the leaf is in the local x-y plane. Pass `height=2.0` to `place()` for rough-edged stone pieces stacked on the 2 m grid.

## Rules the game enforces (run `bp.summary()`)

- **Stations:** every piece names the station that must be within its build range (`station`; ranges in `stations`, 20 m for the workbench and
  stonecutter, more with upgrades). If the station is in the blueprint, tell the player to build it first. Workbenches, forges and other stations
  **need a roof over them** to be used.
- `rules`: `noClipping` pieces may not overlap others (stations, furniture), `notOnWood` (fires), `groundPiece`/`groundOnly` must stand on terrain,
  `noInWater`, `notOnTiltingSurface`, `onlyInBiome`, `mustConnectTo`, `spaceRequirement`. `check()` reports these.
- **Sleeping:** a bed needs a roof over it, at least 80% cover around it, and a fire whose warmth reaches it (a fire pit warms 8 m). A fire
  pit goes out in rain unless something is over it, smoke needs a way out (a raised ridge cap, as in `emergency_hut.py`), and it cannot
  stand on wood.
- Material availability follows progress: check `cost` (bronze nails, iron, black marble...) against what the player has unlocked; ask if unsure.

## Structural support (run `tools/stability.py` on every design)

| Material | Max | Min | Horizontal loss | Vertical loss |
|---|---|---|---|---|
| Wood | 100 | 10 | 0.2 | 0.125 |
| Core wood (HardWood) | 140 | 10 | 0.167 | 0.1 |
| Timberwood | 200 | 10 | 0.2 | 0.077 |
| Stone | 1000 | 100 | 1.0 | 0.125 |
| Iron | 1500 | 20 | 0.077 | 0.077 |
| Marble | 1500 | 100 | 0.5 | 0.125 |
| Ashstone | 2000 | 100 | 0.333 | 0.1 |
| Ancient | 5000 | 100 | 0.25 | 0.067 |

A piece on terrain gets the max. Others get `S - loss * d * S` from each touching piece (S its support, d centre to centre + 0.1 m; vertical loss
for a piece below, horizontal for one beside); two supports on opposite sides give their average. Below the minimum it collapses. Wood and
stone run out after about 8 stacked 2 m pieces; stone cannot cantilever; iron and timberwood go much higher. Fix anything reported as falling.

## Seeing the player's world

Ask the player before doing any of this in their world.
- **Screenshot key** (F12) saves what they see to `shots/` with a `.json` of where they stood and looked.
- **Survey key** (Ctrl+F11) writes `_survey.json` for the spot they look at: `ground` and `solid` height grids (rows -z to +z, columns -x to +x,
  `step` metres apart, heights relative to the centre), water level, biome, and the pieces already there. Use it to design for a real slope
  (posts and stairs reaching the ground, `g: true` for ground pieces) or to extend an existing base.
- **Request files** (only if `AllowRequests = true` in `BepInEx/config/com.dhack.buildorders.cfg`): write `requests/<name>.txt`, one command per
  line; the game moves it to `.taken`, carries it out within a second or two while the player is in a world, and writes `requests/<name>.done.json`
  listing the files made and any errors. Then read the images from `shots/`.
  - `shot [width]`: the player's own view.
  - `view <frame> <x> <y> <z> <yaw> <pitch> [fov] [w] [h]`: a separate camera at a point in a frame: `last` (the last placed blueprint, in its own
    coordinates), a blueprint's name, or `world` (world coordinates). The character does not move.
  - `orbit <frame> [radius] [pitch] [count] [w] [h]`: views from all round a placed blueprint. `top <frame> [size] [width]`: straight down.
  - `survey [radius] [step] [look|here|frame]`, `import <file.json> [look|here|x z yaw]`, `remove <name|last>`, `wait <seconds>`, `status`.
  - `ui <blueprints|plans|settings|close>` opens the Plans window on that tab; `ui place <file.json>` starts the player's placement preview
    (`ui height <m>`, `ui turn <deg>`, `ui level on|off` adjust it) and `ui cancel` ends it. Follow with `wait 1` and `shot` to see what the player sees. These show on the player's screen, so keep them short.
  - Example check after placing: `import my_fort.json look`, `wait 6`, `orbit last 30 30 4`, `top last 30`. Ghosts are only drawn near the player.
- `python tools/selftest.py --request` drops a request that places the test hut where the player looks, photographs it and removes it: a quick way
  to confirm everything works on a new PC.

## How to work with the player

1. Ask: what to build, how big, what style, what materials they can use yet, and where (flat ground? their bed? a survey of the site?).
2. Read `_pieces.json`; copy an example design script; build with snaps; run `stability` and `summary()`; render a preview and look at it.
3. Hand over: what it is, the materials list, the stations needed, and anything to check. Write the JSON into this folder.
4. They press **F11**, choose it and place the preview (or it places itself at their bed if `auto`). With requests on, place it and photograph it
   yourself. If it lands wrong they can Move or Remove it in the Plans window.
5. Adjust from screenshots and regenerate (delete the `.imported` marker first, or `remove` the old ghosts with a request).

## If you also work on the mods themselves

Source: the `valheim-mods` repo. `mods/BuildOrders/Plugin.Blueprints.cs` (importer, piece list), `Plugin.Eyes.cs` (screenshots, survey,
requests), `Stability.cs` (in-game support preview). `tools/blueprints/tools/extract_pieces.py` and `extract_meshes.py` (need `pip install UnityPy`)
rebuild `_pieces.json` and `_meshes` from the game's asset files without running the game. `tools/modelkit` draws previews and the
hand-made 3D models of other mods.
