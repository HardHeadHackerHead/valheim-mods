"""
Emergency hut: the least wood that gives two people a bed they can sleep in, and a fire that never smokes them out.

A medieval catslide cottage, 4 x 6 m:
  - The left side is a low 1 m wall under a steep 45-degree roof; the right side is a full wall (with the door) under a 26-degree roof. The
    two slopes meet at a ridge 3 m up, so the room is closed and dry, and the beds fit under the low side. Every wall piece costs the same wood
    per square metre (a 2 x 2 wall 2, a 2 x 1 half wall 1), so the saving comes from less wall: the steep side needs only a half wall.
  - The fire pit is outside, under a little shed roof on the wall. A fire warms everything within 8 m, walls or not, so both beds count as
    "near a fire", and no smoke ever gets inside. The shed roof keeps the rain off the fire and rises away from the wall, so the smoke slides
    up under it and out at its open top edge (smoke leaves only at the highest point of whatever is over it; see CLAUDE.md).
  - The workbench stands outside the front (it is 3 m wide). Build it first: everything else needs it within 20 m.
  - No floor: the fire pit cannot stand on wood, and the beds do not need one (the ground is levelled when it is placed).

Wood 67 and 5 stone, of which the workbench is 10 and the beds 16 (taking the hut down later gives every piece's materials back).

    python emergency_hut.py [preview.png]
"""
import os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
bp = Blueprint("Emergency hut", P, anchor="look", yaw=0, auto=False)
GABLE = "wood_wall_roof_a" if P.has("wood_wall_roof_a") else "wood_wall_roof"   # 26-degree gable wedge (2 x 1)
GABLE45 = "wood_wall_roof_45"                                                     # 45-degree gable wedge (2 x 2)

HX, HZ = 2.0, 3.0      # walls at x = +-2 (left low, right full) and z = +-3 (the gable ends)
LOW, HIGH = 1.0, 2.0   # eave heights: the low left side and the full right side; the ridge is at x = 0, 3 m up

# left (low) side: half walls, origin in their middle
for z in (-2.0, 0.0, 2.0):
    bp.raw("wood_wall_half", -HX, LOW / 2, z, yaw=90)

# right (full) side: the door at the front, walls behind it
bp.raw("wood_door", HX, 1.0, -2.0, yaw=90)
for z in (0.0, 2.0):
    bp.raw("woodwall", HX, 1.0, z, yaw=90)

# the roof: 45 degrees from the low side (2 m across, 2 m up) and 26 degrees from the high side (2 m across, 1 m up): both reach 3 m at x = 0
for z in (-2.0, 0.0, 2.0):
    bp.place_mid("wood_roof_45", ("bottom 1", "bottom 2"), (-HX, LOW, z), yaw=bp.yaw_for("wood_roof_45", 1, 0))
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (HX, HIGH, z), yaw=bp.yaw_for("wood_roof", -1, 0))

# the gable ends: under the steep slope a half wall and a 45-degree wedge, under the shallow one a full wall and a 26-degree wedge
for gz in (-HZ, HZ):
    bp.raw("wood_wall_half", -1.0, LOW / 2, gz)
    bp.place_mid(GABLE45, ("bottom", "inner bottom"), (-1.0, LOW, gz), yaw=bp.yaw_for(GABLE45, 1, 0))
    bp.raw("woodwall", 1.0, 1.0, gz)
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (1.0, HIGH, gz), yaw=bp.yaw_for(GABLE, -1, 0))

# the shelter over the fire: a shed roof whose low edge rests on the wall's top and which rises away from the wall, so its highest point is
# its open outer edge: smoke slides up under it and out (a roof sloping down from the wall would trap smoke in the corner against the wall)
bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (HX, HIGH, 2.0), yaw=bp.yaw_for("wood_roof", 1, 0))
bp.place("fire_pit", 3.0, 2.0, y=0.0, ground=True)

# inside: a bed along each side, at the back (the front is clear for the door)
bp.place("bed", -0.97, 1.2, y=0.0, ground=True)
bp.place("bed", 0.95, 1.2, y=0.0, ground=True)

# outside the front: the workbench
bp.place("piece_workbench", 0.0, -4.4, y=0.0, ground=True)

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
