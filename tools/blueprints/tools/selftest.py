"""
A small test build that uses every kind of joint the tools know (floor, walls, a door, a pitched roof with gable ends, posts, a platform with a
rail, and stairs), plus the request file that checks it in the game.

    python selftest.py              writes selftest_hut.json into the blueprints folder (and a preview if you pass a png path)
    python selftest.py --request    also writes ../claude/requests/selftest.txt (Claude Tools): the next time the game is open with requests on, it surveys the
                                    spot you look at, places the hut there, photographs it from all sides and from above, then removes it.
"""
import os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
bp = Blueprint("Selftest hut", P, anchor="look", auto=False)
GABLE = "wood_wall_roof_a" if P.has("wood_wall_roof_a") else "wood_wall_roof"   # the buildable gable wedge

# a 4 x 4 m room with a door at the front (-z)
FLOOR = 0.1
for x in (-1, 1):
    for z in (-1, 1):
        bp.raw("wood_floor", x, FLOOR, z)
for x in (-1, 1):
    bp.raw("wood_door" if x == -1 else "woodwall", x, FLOOR + 1.0, -2.0)
    bp.raw("woodwall", x, FLOOR + 1.0, 2.0)
for z in (-1, 1):
    bp.raw("woodwall", -2.0, FLOOR + 1.0, z, yaw=90)
    bp.raw("woodwall", 2.0, FLOOR + 1.0, z, yaw=90)

# a pitched roof: one slope from each long side meeting at a ridge over the middle; the room is 4 m deep so the slopes meet without a gap
EAVE = FLOOR + 2.0
for x in (-1, 1):
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, EAVE, -2.0), yaw=bp.yaw_for("wood_roof", 0, 1))
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, EAVE, 2.0), yaw=bp.yaw_for("wood_roof", 0, -1))
for gx in (-2.0, 2.0):
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (gx, EAVE, -1.0), yaw=bp.yaw_for(GABLE, 0, 1))
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (gx, EAVE, 1.0), yaw=bp.yaw_for(GABLE, 0, -1))

# a little lookout beside it: four posts, a 2 x 2 platform 2 m up with a rail, and two stairs climbing to it from the front
LX = 5.0
for dx in (-1, 1):
    for dz in (-1, 1):
        bp.place_snap("wood_pole2", "bottom", (LX + dx, 0.0, dz), ground=True)
bp.raw("wood_floor", LX, 2.0, 0.0)
bp.place_snap("wood_wall_half", "bottom 1", (LX + 1, 2.0, 1.0))            # the back rail
for dx in (-1, 1):
    bp.place_snap("wood_wall_half", "bottom 1", (LX + dx, 2.0, 1.0), yaw=90)  # the side rails (the front stays open for the stairs)
yaw = bp.yaw_for("wood_stair", 0, 1)
for k in range(2):
    bp.place_mid("wood_stair", ("top 1", "top 2"), (LX, 1.0 + k, -1.0 - 2.0 * (1 - k)), yaw=yaw, ground=True)

ok = report(analyze(bp.items, P))
ok = bp.summary() and ok
out = os.path.join(blueprints_dir(), "selftest_hut.json")
print(bp.save(out), "pieces ->", out)

if "--request" in sys.argv:
    req_dir = os.path.join(blueprints_dir(), "..", "claude", "requests")   # the Claude Tools mailbox
    os.makedirs(req_dir, exist_ok=True)
    with open(os.path.join(req_dir, "selftest.txt"), "w", encoding="utf-8") as f:
        f.write("# check the blueprint tools in the game: survey, place the test hut, photograph it, take it away again\n"
                "status\nshot 1280\nsurvey 16 1 look\nimport selftest_hut.json look\nwait 6\norbit last 14 28 4 1280 720\ntop last 16 1024\n"
                "view last 0 1.6 -7 0 5 60 1280 720\nremove last\n")
    print("request written: the game will run it the next time Claude Tools has requests on and you are in a world")

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0])
    print("preview ->", pngs[0])
sys.exit(0 if ok else 1)
