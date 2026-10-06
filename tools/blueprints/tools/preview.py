"""
Draw a blueprint from four sides to check a design by eye (needs numpy and Pillow).

Pieces are drawn with their real shapes when the shapes have been exported (extract_meshes.py writes <blueprints>/_meshes), coloured like
their textures; otherwise as the box each piece fills. The camera frames the whole design by itself.

    from preview import render
    render(bp.items, pieces, "design.png")                  # four views: two corners, top-down, front
    render(bp.items, pieces, "close.png", views=((30, 25),), size=(1280, 900))
"""
import math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)  # modelkit.py ships next to this file in the game's blueprints folder
for rel in (("..", "..", "modelkit"), ("..", "modelkit")):
    sys.path.insert(0, os.path.join(HERE, *rel))
from blueprint import item_rotation, world_box, blueprints_dir  # noqa: E402

MATERIAL_LOOK = {"Wood": "Planks", "HardWood": "Dark", "Stone": "Stone", "Iron": "Iron", "Marble": "Cream", "Ashstone": "StoneDark", "Ancient": "Gold", "Timberwood": "Dark"}
_mesh_cache = {}


def _mesh(name):
    if name not in _mesh_cache:
        path = os.path.join(blueprints_dir(), "_meshes", name + ".npz")
        _mesh_cache[name] = np.load(path) if os.path.exists(path) else None
    return _mesh_cache[name]


def render(items, catalog, path, views=((25, 30), (-35, 30), (0, 89), (0, 8)), size=(720, 720), dist=None, target=None, look=None,
           meshes=True, ground=True):
    from modelkit import Model, contact_sheet
    m = Model()
    lo = np.array([1e9] * 3); hi = -lo
    if meshes and not os.path.isdir(os.path.join(blueprints_dir(), "_meshes")):
        print("preview: no real piece shapes yet, so pieces are drawn as boxes. Export them once from the game's files:\n"
              "    pip install UnityPy\n    python tools/extract_meshes.py\n"
              "(about 15 seconds; writes _meshes/ in the blueprints folder. Run it again after a game update adds pieces.)")
    for n, item in enumerate(items):
        piece = catalog.by_name[item["p"]]
        r = np.array(item_rotation(item))
        origin = np.array([item["x"], item["y"], item["z"]])
        b0, b1 = world_box(item, piece)
        lo = np.minimum(lo, b0); hi = np.maximum(hi, b1)
        shape = _mesh(item["p"]) if meshes else None
        if shape is not None and len(shape["f"]):
            verts = (r @ shape["v"].astype(np.float64).T).T + origin
            m.mesh("p%d" % n, verts, shape["f"], shape["c"])
        else:
            mat = (look or {}).get(item["p"]) or MATERIAL_LOOK.get(piece.get("material", ""), "Leather")
            (x0, y0, z0), (x1, y1, z1) = piece["min"], piece["max"]
            centre = origin + r @ np.array([(x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2])
            m.box_rot("p%d" % n, centre, (x1 - x0, y1 - y0, z1 - z0), r, mat)
    if target is None:
        target = tuple((lo + hi) / 2)
    if dist is None:
        dist = float(np.linalg.norm(hi - lo)) * 1.25 + 4.0
    contact_sheet(m, path, target=target, dist=dist, size=size, views=views, ground=ground, floor=(74, 88, 56) if ground else None,
                  bg=((96, 122, 150), (170, 186, 196)))
    return path
