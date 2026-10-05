# modelkit

Builds the hand-made 3D looks of our mods without opening the game.

A model is a list of simple shapes (boxes, cylinders, spheres, flat pictures) in a Python script (`bountyboard.py`, `slotmachine.py`).
`modelkit.py` can

- draw a preview picture of it from four sides (`python bountyboard.py <folder>`), so it can be reviewed and refined,
- write it out as C# (`ModelData.cs`) that the mod builds into game objects, using the game's own wood, stone and metal materials,
- render the picture used in the build menu and the cover image.

`build_bountyboard.py` and `build_slotmachine.py` regenerate everything for a mod (`symbols.py` draws the slot machine's reel pictures).
Needs Python with `numpy` and `Pillow`. Run the scripts from this folder.
