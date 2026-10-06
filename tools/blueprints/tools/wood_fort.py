"""
A wooden fort: a 20 m palisade of pointed stakes, four corner watchtowers reached by stairs, a gate between two tall posts, and a hall with a
real pitched roof, gable ends, a door and furniture. Everything is built from wood (the fire pit needs a little stone, the torches resin).

    python wood_fort.py [preview.png]                    for flat ground, placed with F11 where you look
    python wood_fort.py --site "Wooden fort" [preview]   fitted to the real ground where "Wooden fort" was placed (needs a survey there),
                                                         written to wooden_fort_site.json:
                                                         level platforms on posts cut to length, stairs that start on the ground, the hall
                                                         raised on posts where the hill falls away. It is then placed at exactly that spot.

The anchor is the middle of the fort, with the gate facing the way you face when you place it.
Pieces are joined by their snap points (read from the game's data), so stairs climb the right way and roofs meet their walls.
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report
from terrain import Site, Flat

P = Pieces()
site_name = sys.argv[sys.argv.index("--site") + 1] if "--site" in sys.argv else None
site = Site.for_blueprint(site_name) if site_name else Flat()
if site_name:
    bp = Blueprint("Wooden fort", P, anchor="world", yaw=round(site.yaw, 2), auto=False, at=(round(site.ox, 2), round(site.oz, 2)))
else:
    bp = Blueprint("Wooden fort", P, anchor="look", yaw=0, auto=False)
gh = site.ground

HALF = 10.0   # the palisade is 10 m from the middle each way (a 20 m square)
GABLE = "wood_wall_roof_a" if P.has("wood_wall_roof_a") else "wood_wall_roof"   # the buildable gable wedge


def post(x, z, top):
    """A post from `top` down to the ground: 2 m poles stacked from the top, ending with a 1 m pole when that is enough."""
    y = top
    ground = gh(x, z)
    while y - ground > 0.05:
        if y - ground > 1.0:
            bp.place_snap("wood_pole2", "top", (x, y, z))
            y -= 2.0
        else:
            bp.place_snap("wood_pole", "top", (x, y, z))
            y -= 1.0


# ---- the palisade: pointed stakes standing on the ground (their origin is their base), 2 m apiece; the middle of the south side is the gate ----
for c in range(-9, 10, 2):
    for x, z, yaw in [(c, -HALF, 0), (c, HALF, 0), (-HALF, c, 90), (HALF, c, 90)]:
        if z == -HALF and abs(c) < 2:
            continue
        bp.raw("stake_wall", x, -0.3, z, yaw=yaw, ground=True)   # sunk 30 cm so slopes leave no gap underneath

# ---- the gate: two leaves, a post either side taller than the wall, and a lintel of beams ----
gate_floor = site.highest([(-2.0, -HALF), (0.0, -HALF), (2.0, -HALF)])
for x in (-1.0, 1.0):
    bp.place("wood_gate", x, -HALF, y=gate_floor)
for sx in (-1, 1):
    post(sx * 2.2, -HALF, gate_floor + 4.0)
for x in (-1.1, 1.1):
    bp.raw("wood_beam", x, gate_floor + 4.2, -HALF)   # lies on top of the posts

# ---- four watchtowers: a level 4 m platform on posts, a rail round it, and stairs up from the courtyard ----
PLATFORM_ABOVE = 3.0   # the platform floor stands this far above the highest ground under it
for sx in (-1, 1):
    for sz in (-1, 1):
        cx, cz = sx * HALF, sz * HALF
        corners = [(cx + dx, cz + dz) for dx in (-2, 2) for dz in (-2, 2)]
        top = site.highest(corners) + PLATFORM_ABOVE
        for x, z in corners:
            post(x, z, top)
        for dx in (-1, 1):
            for dz in (-1, 1):
                bp.raw("wood_floor", cx + dx, top, cz + dz)
        # the rail: half walls on every edge except the gap where the stairs arrive (on the inner side, nearest the middle of the fort)
        for dx in (-1, 1):
            for dz in (-2, 2):
                if dz == -sz * 2 and dx == -sx:
                    continue
                bp.place_snap("wood_wall_half", "bottom 1", (cx + dx + 1, top, cz + dz))
        for dz in (-1, 1):
            for dx in (-2, 2):
                bp.place_snap("wood_wall_half", "bottom 1", (cx + dx, top, cz + dz + 1), yaw=90)
        # stairs along the side wall, climbing 1 m per 2 m towards the tower, from the platform edge down until they reach the ground;
        # a tower standing high on a slope gets a steeper ladder-stair instead (2 m per 2 m) so it does not run into the next tower's stairs
        x_stair = sx * 8.9

        def run(piece, rise, place):
            edge_z, edge_y, n = sz * 8.0, top, 0
            while n < 12:
                if place:
                    bp.place_mid(piece, ("top 1", "top 2"), (x_stair, edge_y, edge_z), yaw=bp.yaw_for(piece, 0, sz))
                n += 1
                edge_z -= sz * 2.0
                edge_y -= rise
                if edge_y <= gh(x_stair, edge_z) + 0.05:
                    break
            return n

        if run("wood_stair", 1.0, False) <= 5:
            run("wood_stair", 1.0, True)
        else:
            run("wood_stepladder", 2.0, True)

# ---- the hall: 10 m wide, 6 m deep, against the north wall, level, on posts where the ground falls away ----
HALL_X, HALL_Z = [-4, -2, 0, 2, 4], [3.5, 5.5, 7.5]
footprint = [(x + dx, z + dz) for x in HALL_X for z in HALL_Z for dx in (-1, 1) for dz in (-1, 1)]
FLOOR = site.highest(footprint) + 0.1
for x in HALL_X:
    for z in HALL_Z:
        bp.raw("wood_floor", x, FLOOR, z)
for x in [-5, -3, -1, 1, 3, 5]:
    for z in [2.5, 4.5, 6.5, 8.5]:
        if FLOOR - gh(x, z) > 0.4:          # a post under the floor wherever it would hang in the air
            post(x, z, FLOOR - 0.15)
WALL_Y = FLOOR + 1.0           # a wall's origin is its middle, 1 m above its base
for x in HALL_X:
    bp.raw("wood_door" if x == 0 else "woodwall", x, WALL_Y, 2.5)
    bp.raw("woodwall", x, WALL_Y, 8.5)
for z in HALL_Z:
    bp.raw("woodwall", -5.0, WALL_Y, z, yaw=90)
    bp.raw("woodwall", 5.0, WALL_Y, z, yaw=90)
if FLOOR - gh(0, 1.5) > 0.6:   # steps up to the door when the floor is well above the ground in front of it
    step_yaw = bp.yaw_for("wood_stair", 0, 1)
    edge_z, edge_y = 2.25, FLOOR
    for k in range(4):
        bp.place_mid("wood_stair", ("top 1", "top 2"), (0.0, edge_y, edge_z), yaw=step_yaw)
        edge_z -= 2.0; edge_y -= 1.0
        if edge_y <= gh(0.0, edge_z) + 0.05:
            break

# the roof: slopes rising from each long wall to a ridge, with a ridge cap along the top
EAVE = FLOOR + 2.0
south = bp.yaw_for("wood_roof", 0, 1)
north = bp.yaw_for("wood_roof", 0, -1)
for x in HALL_X:
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, EAVE, 2.5), yaw=south)
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, EAVE, 8.5), yaw=north)
    bp.place_mid("wood_roof_top", ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), (x, EAVE + 1.0, 5.5), yaw=0)
# the gable ends: a wedge each side of the middle and a block in the middle, to close the triangle under the roof
for gx in (-5.0, 5.0):
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (gx, EAVE, 3.5), yaw=bp.yaw_for(GABLE, 0, 1))
    bp.place_mid(GABLE, ("bottom", "inner bottom"), (gx, EAVE, 7.5), yaw=bp.yaw_for(GABLE, 0, -1))
    bp.place_mid("wood_wall_roof_top", ("bottom 1", "bottom 2"), (gx, EAVE, 5.5), yaw=90)

# inside the hall and the courtyard
bp.place("piece_workbench", -3.0, 7.4, y=FLOOR)
bp.place("bed", 3.5, 6.5, y=FLOOR)
bp.place("bed", 1.7, 6.5, y=FLOOR)
bp.place("piece_chest_wood", -4.1, 3.6, y=FLOOR, yaw=90)
bp.place("piece_chest_wood", -4.1, 5.5, y=FLOOR, yaw=90)
bp.place("fire_pit", 0.0, -3.0, y=0.0, ground=True)
for sx in (-1, 1):
    bp.place("piece_groundtorch_wood", sx * 3.4, -8.6, y=0.0, ground=True)

# ---- checks, then write it out ----
ok = report(analyze(bp.items, P, ground=gh if site_name else None))
ok = bp.summary() and ok
# a fort fitted to one hillside only fits there: it gets its own file, so the library's "Wooden fort" stays the one for any ground
default = "wooden_fort_site.json" if site_name else "wooden_fort.json"
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), default))
print(bp.save(out), "pieces ->", out)
if site_name:
    import json
    doc = json.load(open(out, encoding="utf-8"))
    doc["name"] = "Wooden fort (fitted to its hillside)"
    doc["note"] = "Post lengths and heights are cut to the ground where it was placed; it is placed back at that spot (anchor world)."
    json.dump(doc, open(out, "w", encoding="utf-8"), indent=1)

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    shown = [dict(i, y=i["y"] + gh(i["x"], i["z"])) if i.get("g") else i for i in bp.items]
    render(shown, P, pngs[0])
    print("preview ->", pngs[0])
raise SystemExit(0 if ok else 1)
