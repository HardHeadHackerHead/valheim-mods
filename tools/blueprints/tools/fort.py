"""
A walled keep: a 20 m stone wall with battlements and corner pillars, a gatehouse facing you, a wooden hall with a walkable roof
inside, and a fire pit and torches in the courtyard. Run from this folder:  python fort.py [preview.png]

The anchor is the middle of the keep and the gate faces the way you face when you place it.
Piece sizes come from _pieces.json (the game's own data), so everything lines up on the real grid.
"""
import math, os, sys
from blueprint import Pieces, Blueprint

pieces = Pieces()
bp = Blueprint("Stone keep", pieces, anchor="look", yaw=0, auto=False)

WALL, PILLAR, GATE = "stone_wall_4x2", "stone_pillar", "wood_gate"
HALF = 10.0                                  # the wall is 10 m from the middle each way (a 20 m square)
SEGMENTS = [-8, -4, 0, 4, 8]                 # five 4 m wall pieces per side

# ---- the perimeter: two courses, with battlements on every other piece ----
for i, c in enumerate(SEGMENTS):
    crenel = i % 2 == 0
    sides = [(c, -HALF, 0), (c, HALF, 0), (-HALF, c, 90), (HALF, c, 90)]
    for x, z, yaw in sides:
        if (x, z) == (0, -HALF):             # the gate gap in the south wall
            continue
        for course in range(2 + (1 if crenel else 0)):
            bp.place(WALL, x, z, y=2.0 * course, yaw=yaw, ground=True, height=2.0)

# ---- corner pillars, three pieces high, so the corners stand taller than the wall ----
for sx in (-1, 1):
    for sz in (-1, 1):
        for k in range(3):
            bp.place(PILLAR, sx * HALF, sz * HALF, y=2.0 * k, ground=True, height=2.0)

# ---- the gatehouse: pillars either side, two gate leaves, and a lintel over them ----
for sx in (-1, 1):
    for k in range(3):
        bp.place(PILLAR, sx * 2.3, -HALF, y=2.0 * k, ground=True, height=2.0)
for x in (-1.0, 1.0):
    bp.place(GATE, x, -HALF, y=0.0, ground=True)          # the gate is placed by its bottom edge
bp.place(WALL, 0.0, -HALF, y=3.1, ground=True, height=2.0)  # the lintel, just above the gate

# ---- the hall: 10 m wide, 6 m deep, against the north wall; flat on the middle's ground level ----
FLOOR_TOP = 0.15
HALL_X = [-4, -2, 0, 2, 4]
HALL_Z = [3.5, 5.5, 7.5]
for x in HALL_X:
    for z in HALL_Z:
        bp.place("wood_floor", x, z, y=FLOOR_TOP, ref="top")
        bp.place("wood_floor", x, z, y=FLOOR_TOP + 2.0 + 0.22, ref="top")        # the roof deck, resting on the walls
for x in HALL_X:
    if x == 0:
        bp.place("wood_door", x, 2.5, y=FLOOR_TOP, height=2.0)                  # the door faces the courtyard
    else:
        bp.place("woodwall", x, 2.5, y=FLOOR_TOP, height=2.0)
    bp.place("woodwall", x, 8.5, y=FLOOR_TOP, height=2.0)
for z in HALL_Z:
    bp.place("woodwall", -5.0, z, y=FLOOR_TOP, yaw=90, height=2.0)
    bp.place("woodwall", 5.0, z, y=FLOOR_TOP, yaw=90, height=2.0)

# inside the hall
bp.place("piece_workbench", -3.0, 7.4, y=FLOOR_TOP)
bp.place("bed", 3.5, 6.5, y=FLOOR_TOP)
bp.place("bed", 1.7, 6.5, y=FLOOR_TOP)
bp.place("piece_chest_wood", -4.1, 3.6, y=FLOOR_TOP, yaw=90)
bp.place("piece_chest_wood", -4.1, 5.5, y=FLOOR_TOP, yaw=90)

# the fire: outside the hall's front wall, within 8 m of the beds (its warmth reaches through the wall), under a little shed roof that keeps
# the rain off and rises away from the wall so the smoke slides out at its top edge; no smoke ever gets into the hall
bp.place("fire_pit", 3.0, 1.2, y=0.0, ground=True)
bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (3.0, FLOOR_TOP + 2.0, 2.5), yaw=bp.yaw_for("wood_roof", 0, -1))

# torches by the gate
for sx in (-1, 1):
    bp.place("piece_groundtorch_wood", sx * 3.4, -8.6, y=0.0, ground=True)

missing = sorted({i["p"] for i in bp.items if not pieces.has(i["p"])})
if missing:
    raise SystemExit("Pieces not in the list: " + ", ".join(missing))

from blueprint import blueprints_dir
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), "stone_keep.json"))
print(bp.save(out), "pieces ->", out)

# does it stand? (an estimate from the game's own support rules)
from stability import analyze, report
report(analyze(bp.items, pieces))

# ---- a quick picture of it (each piece drawn as the box it occupies), to check it by eye ----
if len(sys.argv) > 1:
    sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "modelkit"))
    from modelkit import Model, contact_sheet
    m = Model()
    for n, item in enumerate(bp.items):
        (x0, y0, z0), (x1, y1, z1) = bp.box(item["p"])
        cx, cy, cz = (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2
        a = math.radians(item["ry"])
        wx = item["x"] + cx * math.cos(a) + cz * math.sin(a)
        wz = item["z"] - cx * math.sin(a) + cz * math.cos(a)
        base = 0.0 if item.get("g") or True else 0.0
        mat = "Stone" if "stone" in item["p"] or "pillar" in item["p"] else "Dark" if "gate" in item["p"] else "Planks"
        if item["p"] in ("piece_workbench", "bed", "piece_chest_wood", "fire_pit", "piece_groundtorch_wood"):
            mat = "Red" if item["p"] == "bed" else "Gold" if item["p"] == "fire_pit" else "Leather"
        m.box("p%d" % n, wx, item["y"] + cy + base, wz, x1 - x0, y1 - y0, z1 - z0, mat, ry=item["ry"])
    contact_sheet(m, sys.argv[1], target=(0, 2.0, 0), dist=62, size=(720, 720), views=((20, 32), (-40, 32), (0, 89), (0, 6)))
    print("preview ->", sys.argv[1])
