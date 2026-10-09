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
| `../claude/` | The **Claude Tools** mod's folder (if installed): `requests/` (the request mailbox), `shots/` (pictures), `_survey.json` (the ground around a spot), and its own `CLAUDE.md` listing every command. |
| `_imports.json` | Where each blueprint was placed (origin and turn), for views and removal. |
| `tools/blueprint.py` | Place pieces (by box, snap point or origin), any rotation, `check()` and `summary()` (stations, overlaps, ground, fires, roofs, materials). |
| `tools/stability.py` | Will it stand? The game's own support rules. |
| `tools/preview.py` | `render(items, pieces, "out.png")`: four views with the real shapes. Look at it before handing a design over. |
| `tools/emergency_hut.py`, `tools/wood_fort.py`, `tools/fort.py`, `tools/selftest.py` | Worked examples (a two-bed catslide cottage with the fire outside under a lean-to, wooden fort with towers and stairs, stone keep, small test hut). Copy one to start. |
| `tools/test_tools.py` | Tests for all of the above: run it if you change the tools. |
| `_plans/`, `_terrain/` | Per placed plan: its pieces as placed (to take down what was built of it) and the ground before levelling (to put it back). |
| `_inbox/` | Blueprints other players shared in game (Plans window, "Shared with you"); share codes start with `BO1:` (base64 of the compressed JSON). |
| `*.json` (no leading `_`) | Blueprints: each shows in the player's Plans window (F11). |
| `<blueprint>.png` / `.jpg` | Its picture in the Plans window. The mod makes one in the game (a solid copy photographed out of sight) when there is none; put your own image there to replace it. It goes along with in-game Share. |

Files in `tools/` are replaced when the mod updates: copy before editing. Python 3 with `numpy` and `Pillow` is needed for previews only.

### First-time setup: real piece shapes for previews

You can design and check a whole build without the game running: `_pieces.json` (written by the mod the first time the player plays
with it) has every piece's size and snap points, and `tools/preview.py` draws the design. To draw pieces as they really look instead of
plain boxes, export their shapes from the player's own game files once:

```
pip install numpy Pillow UnityPy
python tools/extract_meshes.py
```

It takes about 15 seconds and writes `_meshes/` here (about 10 MB). It finds the game from this folder's location (set `VALHEIM_DIR` if the
game is elsewhere), and finds the right asset file itself if a game update renamed it. Run it again after an update adds pieces. The
shapes are the game's own assets: keep them on the player's machine, never share or commit them. Pieces added by mods (and a few
stations) have no exported shape and are drawn as their box.

## The blueprint file

```json
{ "name": "Stone keep", "anchor": "look", "yaw": 0, "offset": [0,0,0],
  "pieces": [ {"p": "stone_wall_4x2", "x": -8, "y": 1, "z": -10, "ry": 0, "g": true} ] }
```
- **Placing:** the player opens the Plans window (**F11**), presses Place and puts a preview where they look, turning and raising it as they
  like (it starts facing the way they face). `anchor` is `"look"`; `"world"` with `"at": [x, z]` is for request files that give coordinates.
- **Every plan is placed on level ground:** the ground under the whole plan (every piece that touches the ground, plus 1 m) is set exactly to
  the plan's floor height (y 0, or higher if the player raised it), with a 3 m slope back to the natural ground, and painted as dirt; then the
  ghosts appear. The game moves ground at most 8 m from where it started; warded ground is left alone. Removing or moving the plan restores the
  ground (saved in `_terrain/`) unless pieces of it are built. So **design for flat ground with the base at y 0**; there is no need for posts
  down a slope. Requests: `level <name|last>` levels a placed plan again; `check <name|last>` reports post columns that do not reach the ground
  and how high doors and gates sit above it.
- **Keeping the land as it is:** `"level": false` in the blueprint file skips the levelling entirely: the ghosts go straight onto the ground
  (for builds that use the terrain: bridges over a creek, a keep on a crag, posts cut to the slope). Heights are then measured from the
  ground at the anchor (or from the ground under each piece marked `g`), so such a plan is placed by its world coordinates (`anchor: "world"`,
  `at`, the import request with `x z yaw`), where it was designed to fit.
- A design fitted to one surveyed hillside (its own posts cut to the slope, as `wood_fort.py --site` does) only fits there, so save it under its
  own name (`wooden_fort_site.json`); being levelled, it no longer needs those posts.
- Each piece: `p` prefab, `x y z` metres from the anchor (y above the ground at the anchor), `rx ry rz` degrees (Unity order z, x, y),
  `g: true` to measure `y` from the ground under that piece (follows slopes; for pieces touching the ground). Max 5000 pieces.

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
- Fires, smoke and sleeping: see "Game facts" below (checked in the game's code; do not guess these).
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

## Game facts (read from the game's code and assets; trust these over memory or forum lore)

**Sleeping in a bed** (`Bed.Interact`): the bed's spawn point must be **under a roof** (a ray straight up hits something) and have **cover of at
least 80%** (rays in all directions mostly hit something: a closed room passes, an open side usually does not); a **fire's warmth must reach
the bed** (`EffectArea` of type Heat); the player must not be **wet**, and no enemy may be close enough to sense them. A bed costs 8 wood.

**Fire warmth** reaches through walls: a fire pit's warmth (Heat + Fire, which also gives the Warm/Resting effects) is a sphere of **8 m**
around it, walls or not. So **a fire outside the wall still warms the beds inside**: the simplest way to never be smoked out.

**A fire pit goes out** when it is raining and nothing is straight above it (`underRoof`), or in strong wind (80%+) when its cover is under
70%. It also goes out when its **own smoke piles up** around it for 4 seconds (the check looks for smoke within 0.75 m of a point 0.5 m
above a fire pit, 0.9 m above a hearth: smoke trapped under a roof fills down until it smothers the fire). A roof close above is fine. It may not stand on wood (no wooden
floor under it) and burns 1 wood per 5000 s (10 wood fills it).

**Smoke** (`Smoke`, `SmokeSpawner`): a fire puts out a puff every half second (at most 100 in the world). Each puff is a small physics ball that
rises (strongly at first, weakly over its 10 s life), slides along whatever it touches, then fades. It **only leaves a building through an
opening at the top**: it collects in the highest pocket under a roof and fills downwards from there. A vent must be the highest point of the
space (an open ridge, or a gap at the top of a slope), not a side opening below a cap: a raised ridge cap traps smoke in its peak. Players
standing in smoke are "smoked" (damage over time). Easiest of all: keep the fire outside under its own small roof, sloping **up** away from the wall so its open top edge is the highest
point (a roof sloping down from a wall makes a pocket against the wall where smoke gathers). `check()` warns about fires in closed rooms.

**Costs per area** (wood): wall 2 x 2 = 2, half wall 2 x 1 = 1, quarter 1 x 1 = 1 (twice the price per metre), 26 and 45 degree roofs 2 each,
gable wedges 2, door 4, floor 2 x 2 = 2, pole 1 m = 1, 2 m = 2, stake wall 4. Walls cost the same per square metre, so a cheaper building has
**less wall area**: low walls under a steep roof (`emergency_hut.py`: a half wall under a 45 degree roof on one side). The workbench costs 10
and must be within 20 m of anything built; taking pieces down gives all their materials back.

## Seeing the player's world (with the Claude Tools mod)

The **Claude Tools** mod gives you a request mailbox in `BepInEx/claude` (next to this folder): you write commands into
`../claude/requests/<name>.txt`, the game carries them out while the player is in a world and writes `../claude/requests/<name>.done.json`;
pictures land in `../claude/shots/`. Read `../claude/CLAUDE.md` for how it works and every built-in command (pictures, `survey`, `status`,
`inventory`, `nearby`, `looking`, ...). It only runs when the player has switched `AllowRequests` on in Claude Tools' settings: ask first.
Without Claude Tools you can still design: the player places your blueprints from the Plans window, and can send you F12 screenshots.

BuildOrders adds these commands to it, and lets the camera commands use a placed blueprint's name as the place (`last` for the newest; `_` for spaces):

- `import <file.json> [look|here|x z yaw]`: place a blueprint (the ground is levelled first; the ghosts follow about a second later).
- `remove <name|last>` (ghosts only), `takedown <name>` (also takes down what was built, materials back to the player).
- `check <name|last>`: post columns that do not reach the ground (posts standing on the plan's own pieces, like rail posts, are left out), how high doors and gates sit, and whether it would stand (`wouldFall`, `weakestSupport`). `level <name|last>`: level again.
- `bridge <from> <to> [width=2|4|6] [material=wood|corewood|darkwood|stone] [sides=rails|halfwalls|none] [ends=sloped|steps] [roof=on|off] [supports=auto|2|4] [shape=straight|arched] [torches=on|off]`: plan a bridge between two spots (`here`, `look`, or `x,z`); unset options use the player's last choices. The player draws one with the hammer's Bridge piece and picks these in a panel.
- `build <count> <name>`: build that many ghosts as E does (spends the player's materials; the workbench first). `plans`: the placed plans.
- `ui <blueprints|plans|settings|close>` opens the Plans window on that tab; `ui place <file.json>` starts the player's placement preview
  (`ui height <m>`, `ui turn <deg>`, `ui confirm`, `ui cancel`). Follow with `wait 1` and `shot` to see what the player sees. These show on
  the player's screen, so keep them short.
- Example check after placing: `import my_fort.json look`, `wait 3`, `check last`, `orbit last 30 30 4`, `top last 30`. Ghosts are drawn only near the player.
- The **survey** (`survey 24 1 look`, or the player's Ctrl+F12) writes `../claude/_survey.json`: `ground` and `solid` height grids (rows -z
  to +z, columns -x to +x, `step` metres apart, heights relative to the centre), water level, biome, and the pieces already there. Plans are
  levelled anyway; use it to extend an existing base or to see what is in the way.
- `python tools/selftest.py --request` drops a request that places the test hut where the player looks, photographs it and removes it: a
  quick way to confirm everything works on a new PC.

## How to work with the player

1. Ask: what to build, how big, what style and what materials they can use yet.
2. Read `_pieces.json`; copy an example design script; build with snaps; run `stability` and `summary()`; render a preview and look at it.
3. Hand over: what it is, the materials list, the stations needed, and anything to check. Write the JSON into this folder.
4. They press **F11**, choose it and place the preview. With requests on, place it and photograph it
   yourself. If it lands wrong they can Move or Remove it in the Plans window.
5. Adjust from screenshots and regenerate (`remove` the old ghosts with a request, or the player removes them in the Plans window).

## If you also work on the mods themselves

Source: the `valheim-mods` repo. `mods/BuildOrders/Plugin.Blueprints.cs` (importer, piece list), `Plugin.Requests.cs` (its Claude Tools
commands), `mods/ClaudeTools` (the mailbox, pictures, surveys), `Stability.cs` (in-game support preview). `tools/blueprints/tools/extract_pieces.py` and `extract_meshes.py` (need `pip install UnityPy`)
rebuild `_pieces.json` and `_meshes` from the game's asset files without running the game. `tools/modelkit` draws previews and the
hand-made 3D models of other mods.
