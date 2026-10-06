"""
Emergency hut: a one-room medieval cabin for two, quick to put up in the woods with nothing but wood (and 5 stone for the fire).

  - 6 x 4 m, timber-framed (a post at each corner), wooden walls, a door at the front.
  - A thatched roof with a raised smoke louvre along the ridge: the cap stands half a metre above the two slopes on short posts, so the
    smoke from the fire below escapes through the gaps while the cap keeps the rain off the fire (a fire pit goes out in the rain).
  - Two beds along the side walls, the fire pit in the middle (its warmth reaches 8 m, so both beds count as "near a fire"; the room is
    closed and roofed, so both beds are covered enough to sleep).
  - The workbench stands outside by the door (it is 3 m wide): build it first; the hut needs it within 20 m.
  - No floor: the fire pit cannot stand on wood. Press L when placing it to level the ground under it.

    python emergency_hut.py [preview.png]
"""
import os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
bp = Blueprint("Emergency hut", P, anchor="look", yaw=0, auto=False)
GABLE = "wood_wall_roof_a" if P.has("wood_wall_roof_a") else "wood_wall_roof"

HX, HZ = 3.0, 2.0          # half width (x) and half depth (z): walls at x = +-3 and z = +-2
WALL = 1.0                 # a wall's origin is its middle: 1 m above the ground
EAVE = 2.0                 # top of the walls
LOUVRE = 0.5               # how far the ridge cap stands above the slopes

# timber frame: a post at each corner
for x in (-HX, HX):
    for z in (-HZ, HZ):
        bp.place_snap("wood_pole2", "bottom", (x, 0.0, z))

# walls: front and back run along x (three 2 m panels, the door in the middle of the front), the sides along z (two panels)
for x in (-2.0, 0.0, 2.0):
    bp.raw("wood_door" if x == 0.0 else "woodwall", x, WALL, -HZ)
    bp.raw("woodwall", x, WALL, HZ)
for z in (-1.0, 1.0):
    bp.raw("woodwall", -HX, WALL, z, yaw=90)
    bp.raw("woodwall", HX, WALL, z, yaw=90)

# roof: a slope rising from each side wall towards the middle; each is 2 m across, so they stop 1 m short of the ridge on either side
for z in (-1.0, 1.0):
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (-HX, EAVE, z), yaw=bp.yaw_for("wood_roof", 1, 0))
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (HX, EAVE, z), yaw=bp.yaw_for("wood_roof", -1, 0))

# gable ends (front and back): a wedge under each slope and a block in the middle
for gz in (-HZ, HZ):
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (-2.0, EAVE, gz), yaw=bp.yaw_for(GABLE, 1, 0))
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (2.0, EAVE, gz), yaw=bp.yaw_for(GABLE, -1, 0))
    bp.place_mid("wood_wall_roof_top", ("bottom 1", "bottom 2"), (0.0, EAVE, gz), yaw=0)
    # a short post on each gable carries the raised cap
    bp.place_snap("wood_pole", "top", (0.0, EAVE + 1.0 + LOUVRE, gz))   # its top is where the cap sits (the rest hides in the gable)

# the smoke louvre: the ridge cap, raised above the tops of the slopes
for z in (-1.0, 1.0):
    bp.place_mid("wood_roof_top", ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), (0.0, EAVE + 1.0 + LOUVRE, z), yaw=90)

# inside: the fire in the middle, a bed along each side wall
bp.place("fire_pit", 0.0, 0.3, y=0.0, ground=True)
bp.place("bed", -1.9, 0.05, y=0.0, ground=True)
bp.place("bed", 1.9, 0.05, y=0.0, ground=True)

# outside, by the door: the workbench (build it first)
bp.place("piece_workbench", 3.2, -3.2, y=0.0, ground=True)

ok = report(analyze(bp.items, P))
ok = bp.summary() and ok
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), "emergency_hut.json"))
print(bp.save(out), "pieces ->", out)

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0])
    print("preview ->", pngs[0])
sys.exit(0 if ok else 1)
