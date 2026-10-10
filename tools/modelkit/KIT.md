# modelkit: making and checking 3D models without the game

This folder (`BepInEx/claude/modelkit`, written by Claude Tools) lets you design the look of a mod's piece or item offline, see it from
every side, and turn it into C# the mod builds in the game, dressed in the game's own wood, stone and iron. It also draws the game's real
building pieces offline, so you can see what you're matching. Needs Python 3 with `pip install numpy Pillow` (and `UnityPy` for the
game's real shapes).

| File | What it is |
|---|---|
| `modelkit.py` | A model as simple shapes (`box`, `cyl`, `sph`, `quad`, groups that move together, real triangles with `mesh`); `contact_sheet` draws it from four sides, `icon` draws its build-menu picture, `Model.to_csharp` writes it as C#. |
| `bountyboard.py` | A worked example (a notice board with a roof, skull, notices, lantern and bell). `python bountyboard.py out` draws `out/board_sheet.png`. Copy it to start. |
| `ModelBuilder.cs`, `Look.cs` | Copy these into your mod (change the namespace): they build the C# model in the game and give its parts the game's materials. |
| `extract_pieces.py`, `extract_meshes.py` | Read every building piece's size, snap points and real 3D shape from the game's own files (no game running): `BepInEx/blueprints/_pieces.json` and `_meshes/`. Needs `UnityPy`. |
| `preview.py`, `blueprint.py` | Draw game pieces (and whole builds) with those real shapes. |

## Making a model

1. **Look at the game first.** Your piece sits next to the game's: match their size and style. Draw a game piece offline:
   ```
   python extract_pieces.py          (once: writes BepInEx/blueprints/_pieces.json; BuildOrders writes it too)
   python extract_meshes.py          (once: the real shapes, about 15 seconds, 10 MB)
   python -c "from blueprint import Pieces; from preview import render; P=Pieces(); render([{'p':'piece_chest_wood','x':0,'y':0,'z':0}], P, 'chest.png')"
   ```
   With the game running, Claude Tools' `render <prefab>` and `inspect <prefab>` show any piece, item or creature, and its parts and sizes.
2. **Describe yours as shapes** in a copy of `bountyboard.py`. Units are metres, y up, **the front faces -z**; a 2 m wall is 2 m. Cylinders
   take a half height (`cyl(..., 0.05, 0.5, 0.05)` is a pole 1 m tall), rotations are Unity's (z, then x, then y). Materials by name:
   `Stone`, `Planks`, `Dark`, `Iron` are the game's own; `Gold`, `Glow`, `Parchment`, `Leather`, `Bone`, `Red`, ... are plain colours.
3. **Look at it** (`contact_sheet`) from all four sides, fix, repeat. Small parts flat on a surface flicker in the game: raise details at
   least a centimetre off what they sit on.
4. **Write the C#**: `open("ModelData.cs","w").write(make().to_csharp("YourMod"))`, and the build-menu icon with `icon(make(), "icon.png")`.
5. **Build it in the game** with `ModelBuilder.Build(prefab.transform, ModelData.Parts, prefab.layer)` after `Look.Harvest(ZNetScene.instance, ...)`,
   on a prefab cloned from a similar game piece (it brings the right layer, sounds, wear and placement rules). Give the piece its own
   collider (the builder removes the shapes' ones).
6. **Check it in the game**: `render <your prefab> views=4` (a fresh copy with its real materials, from four sides) and close-ups with
   `focus=` and `dist=`. Placed pieces keep their old look until the game restarts; `render` always builds a new one.

## Rules

- **The game's shapes and textures stay on this computer.** `_meshes/` and anything drawn from it are the game's assets: never share or
  commit them. Your own models (the Python script and the C# it writes) are yours.
- Prefer the game's own prefabs and materials over new ones: they look right, cost nothing, and work on a dedicated server (which draws
  nothing). Start every material from one the game already has; `Shader.Find` can return null.
- Files here are replaced when Claude Tools updates: copy them into your own project before editing.
