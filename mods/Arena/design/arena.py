"""
The Arena's layout, designed offline with the BuildOrders blueprint kit (its piece list and previews) and baked into the mod.

    python arena.py            # writes ../Layout.json and renders previews into ./renders/
    python arena.py --no-render

Coordinates: metres from the middle of the fighting floor, y up from the floor's edge (the levelled ground), z north. The main gate faces
south (-z) onto the forecourt, where you arrive, the Hall of Fame stands and the Arena Master takes your name. Inside the gate a hall: ahead,
the tunnel to the fighters' grate; left and right, stairs inside the outer gallery up to the stands.

Bearings: a = 0 is north (+z), 90 east (+x). A piece turned by yaw a faces outward from the middle at bearing a.
Every piece is a game piece, so it looks like Valheim; the mod builds them as plain scenery (not saved, cannot be broken).
"""
import json, math, os, sys

BLUEPRINTS = r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\blueprints"
sys.path.insert(0, os.path.join(BLUEPRINTS, "tools"))
from blueprint import Pieces, Blueprint  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
P = Pieces(os.path.join(BLUEPRINTS, "_pieces.json"))
bp = Blueprint("The Arena", P)
markers = {}
props = {}            # the cover put on the floor for a contest: a few sets, one picked each time


def rad(a):
    return math.radians(a)


def polar(r, a):
    return r * math.sin(rad(a)), r * math.cos(rad(a))


def ang(a, b):
    return abs((a - b + 180) % 360 - 180)


def put(name, r, a, y, yaw_add=0.0, ref="bottom", height=None, out=None):
    x, z = polar(r, a)
    return (out or bp).place(name, x, z, y=y, yaw=(a + yaw_add) % 360, ref=ref, height=height)


def mark(name, x, y, z, yaw):
    markers[name] = {"x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yaw": round(yaw % 360, 2)}


def ring_count(r, width):
    return max(8, int(round(2 * math.pi * r / width)))


# ---------------------------------------------------------------------------------------------------------------- sizes
FLOOR = 14.0            # the fighting floor: you must stay inside it
PODIUM_R = 15.2         # the inner face of the podium wall
WALL_H = 2.2            # a course of stone_wall_4x2
PODIUM_TOP = 2 * WALL_H
TIERS = 4
TIER_RISE = 0.95
TIER_DEPTH = 2.0
INNER_R = 24.9          # the gallery's inner wall (behind the last tier)
FACADE_R = 27.2         # the outer arcade
STOREY = 4.08           # two courses of grausten (2.04)
TOP = 2 * STOREY        # the promenade behind the last tier, over the gallery
ATTIC = TOP + STOREY    # the top of the attic: a third, closed storey over the arcades, as the Colosseum has

GATES = [45.0, 135.0, 225.0, 315.0]     # where the creatures come in
MAIN = 180.0                            # the fighters' grate, at the end of the tunnel from the main gate
MASTER = 0.0                            # the Arena Master's box, over the north side
openings = GATES + [MAIN]


# ---------------------------------------------------------------------------------------------------------------- the podium wall, with its gates
seg = ring_count(PODIUM_R + 0.67, 4.1)
step = 360.0 / seg
for i in range(seg):
    a = i * step
    near = min(openings, key=lambda g: ang(a, g))
    if ang(a, near) < step / 2:
        a = near
        r = PODIUM_R + 0.67
        x, z = polar(r, a)
        tx, tz = math.cos(rad(a)), -math.sin(rad(a))           # along the wall
        for side in (-1, 1):
            for course in range(2):
                bp.place("stone_pillar", x + tx * 1.62 * side, z + tz * 1.62 * side, y=course * 2.0, yaw=a)
        gx, gz = polar(PODIUM_R + 0.2, a)
        bp.place("iron_grate", gx, gz, y=0.0, yaw=a)
        grate = len(bp.items) - 1
        put("stone_wall_4x2", r, a, 3.0, height=WALL_H)
        main = a == MAIN
        markers.setdefault("gates", []).append({"x": round(gx, 3), "y": 0.0, "z": round(gz, 3), "yaw": round((a + 180) % 360, 2), "bearing": a, "main": main, "piece": grate})
        if not main:
            # a dark pen behind the grate, where the creature waits
            for d in (1.0, 3.0):
                for side in (-1, 1):
                    px, pz = polar(PODIUM_R + 1.4 + d, a)
                    bp.place("Piece_grausten_wall_2x2", px + tx * 1.4 * side, pz + tz * 1.4 * side, y=0.0, yaw=a + 90)
                    bp.place("Piece_grausten_wall_2x2", px + tx * 1.4 * side, pz + tz * 1.4 * side, y=2.04, yaw=a + 90)
            for side in (-1, 1):
                put("Piece_grausten_wall_2x2", PODIUM_R + 5.4, a + side * 2.8, 0.0)
                put("Piece_grausten_wall_2x2", PODIUM_R + 5.4, a + side * 2.8, 2.04)
            sx, sz = polar(PODIUM_R - 1.8, a)          # (where a creature appears: just inside the grate, as it rises)
            markers.setdefault("pens", []).append({"x": round(sx, 3), "y": 0.0, "z": round(sz, 3), "yaw": round((a + 180) % 360, 2), "gate": len(markers["gates"]) - 1})
            put("piece_walltorch", PODIUM_R + 5.15, a, 2.6, yaw_add=180)
        continue
    for c in range(2):
        put("stone_wall_4x2", PODIUM_R + 0.67, a, c * WALL_H, height=WALL_H)

# a low parapet along the top, banners and torches on the inner face
seg_p = ring_count(PODIUM_R + 0.6, 2.18)
for i in range(seg_p):
    a = i * 360.0 / seg_p
    if ang(a, MASTER) < 9:
        continue
    put("stone_wall_2x1", PODIUM_R + 0.6, a, PODIUM_TOP, height=1.0)
banners = ["piece_banner01", "piece_banner02", "piece_banner05", "piece_banner07", "piece_banner09", "piece_banner04"]
k = 0
for i in range(seg):
    a = i * step + step / 2
    if min(ang(a, g) for g in openings) < step:
        continue
    if i % 2 == 0:
        put(banners[k % len(banners)], PODIUM_R - 0.12, a, PODIUM_TOP - 0.1, yaw_add=90, ref="top")
        k += 1
    else:
        put("piece_walltorch", PODIUM_R - 0.05, a, 2.7, yaw_add=180)

# iron bars along the parapet, 3 m of them: the front row sits right over the fight, but nobody falls (or jumps, or climbs) into the ring
seg_b = ring_count(PODIUM_R + 0.6, 1.98)
for i in range(seg_b):
    a = (i + 0.5) * 360.0 / seg_b
    if ang(a, MASTER) < 8:
        continue          # (the Arena Master's box has its own, at its front)
    put("iron_grate", PODIUM_R + 0.6, a, PODIUM_TOP + 0.5)

# ---------------------------------------------------------------------------------------------------------------- the stands
seats = []
for t in range(TIERS):
    r = PODIUM_R + 1.35 + TIER_DEPTH * t + 1.0
    top = PODIUM_TOP + TIER_RISE * t
    n = ring_count(r, 2.0)
    for i in range(n):
        a = (i + 0.5 * (t % 2)) * 360.0 / n
        if ang(a, MASTER) < 7 and t < 3:
            continue          # the master's box stands here
        put("stone_floor_2x2", r, a, top - 1.0)
        if ang(a, MAIN) > 8 and ang(a, MASTER) > 10:
            seats.append({"r": round(r - 0.3, 2), "a": round(a, 2), "y": round(top, 2)})
markers["seats"] = seats

# the promenade behind the last tier and over the gallery, with a gap over each stair
last_r = PODIUM_R + 1.35 + TIER_DEPTH * TIERS
STAIR_SPAN = 35.5       # degrees of gallery each stair climbs through (8 stairs, 2 m each, end to end)
STAIR_ROWS = int(TOP)
STAIR_STEP = STAIR_SPAN / STAIR_ROWS
STAIR_HALF = math.degrees(1.0 / ((INNER_R + FACADE_R) / 2))     # half a stair's width, in degrees round the gallery
# The stairs start one arch round from the gate (the gate's towers stand before the first), and end on a seam between two of the
# promenade's floors over them, so the gap left for headroom (over the top three steps only) ends exactly where the stairs do: no hole.
_n_out = ring_count(last_r + 2.2, 2.0)
_tstep = 360.0 / _n_out
_seam = (math.ceil((19.5 + (STAIR_ROWS - 1) * STAIR_STEP + STAIR_HALF) / _tstep - 0.5) + 0.5) * _tstep
STAIR_START = _seam - (STAIR_ROWS - 1) * STAIR_STEP - STAIR_HALF
_gap_lo = STAIR_START + (STAIR_ROWS - 3) * STAIR_STEP - STAIR_HALF
for r in (last_r + 0.2, last_r + 2.2):
    n = ring_count(r, 2.0)
    for i in range(n):
        a = i * 360.0 / n
        off = (a - MAIN + 360) % 360
        side_off = min(off, 360 - off)
        if r > last_r + 1 and _gap_lo - _tstep / 2 < side_off < _seam:
            continue
        put("stone_floor_2x2", r, a, TOP - 1.0)
_sx, _sz = polar(last_r + 0.5, MAIN + STAIR_SPAN + 14)
mark("stands", _sx, TOP, _sz, 0.0)

# ---------------------------------------------------------------------------------------------------------------- the gallery's inner wall
n_in = ring_count(INNER_R, 4.1)
for i in range(n_in):
    a = i * 360.0 / n_in
    if ang(a, MAIN) < 6:
        continue
    for s in range(4):
        put("Piece_grausten_wall_4x2", INNER_R, a, s * 2.04, yaw_add=180)

# ---------------------------------------------------------------------------------------------------------------- the facade
# Two storeys of arches, each post fronted by a column (base, tapered shaft, capital), braziers hanging in the ground arches and banners in
# the upper ones; over them the attic, a closed storey with windows, pilasters and masts flying banners all round the top.
BAY = 5.04          # a 1 m post and a 4 m arch
n_bay = ring_count(FACADE_R, BAY)
bay_step = 360.0 / n_bay
main_bay = round(MAIN / bay_step)
colours = ["piece_banner01", "piece_banner02", "piece_banner05", "piece_banner07", "piece_banner09", "piece_banner04", "piece_banner11", "piece_banner03"]
for i in range(n_bay):
    a = i * bay_step
    post_a = a - bay_step / 2
    is_main = i == main_bay
    for st in range(2):
        y = st * STOREY
        # the post between bays, and its column standing before it
        put("Piece_grausten_wall_1x2", FACADE_R, post_a, y)
        put("Piece_grausten_wall_1x2", FACADE_R, post_a, y + 2.04)
        put("Piece_grausten_pillarbase_medium", FACADE_R + 0.6, post_a, y)
        put("Piece_grausten_pillarbase_tapered", FACADE_R + 0.6, post_a, y + 1.0)
        put("Piece_grausten_pillarbeam_medium", FACADE_R + 0.6, post_a, y + 3.0)
        if is_main:
            continue
        # the arch: two arch walls meeting in the middle, open below
        da = math.degrees(1.0 / FACADE_R)
        put("Piece_grausten_wall_arch", FACADE_R, a - da, y + 2.04)
        put("Piece_grausten_wall_arch", FACADE_R, a + da, y + 2.04, yaw_add=180)
        if st == 0 and i % 2 == 0 and abs(i - main_bay) > 1:
            put("piece_brazierceiling01", FACADE_R - 0.1, a, y + 3.3, ref="top")       # a fire hanging in the arch
        if st == 1 and abs(i - main_bay) > 1:
            put(colours[i % len(colours)], FACADE_R + 0.32, a, TOP - 0.2, yaw_add=90, ref="top")
    # the attic: a pilaster over each post, a window or a panel between
    put("Piece_grausten_wall_1x2", FACADE_R, post_a, TOP)
    put("Piece_grausten_wall_1x2", FACADE_R, post_a, TOP + 2.04)
    put("Piece_grausten_window_4x2" if i % 2 and not is_main else "Piece_grausten_wall_4x2", FACADE_R, a, TOP)
    put("Piece_grausten_wall_4x2", FACADE_R, a, TOP + 2.04)
    if not is_main:
        if i % 2 == 0:   # and, inside, banners hanging over the stands for the crowd to see across the ring
            put(colours[(i // 2) % len(colours)], FACADE_R - 0.35, a, ATTIC - 0.3, yaw_add=-90, ref="top")
    # masts round the top, every third post, each flying a banner
    if i % 3 == 0:
        put("stave_pole_4m", FACADE_R, post_a, ATTIC)
        put(colours[(i // 3 + 3) % len(colours)], FACADE_R + 0.45, post_a, ATTIC + 4.0, yaw_add=90, ref="top")
# the crown of the attic: merlons all round
n_cr = ring_count(FACADE_R, 2.06)
for i in range(n_cr):
    a = i * 360.0 / n_cr
    put("Piece_grausten_wall_1x2", FACADE_R, a, ATTIC if i % 2 == 0 else ATTIC - 1.0)
# braziers along the promenade, against the attic
for i in range(0, n_bay, 4):
    a = i * bay_step + bay_step / 2
    if ang(a, MAIN) < 12 or STAIR_START - 4 < ang(a, MAIN) < _seam + 4:
        continue    # (none by the gate, nor at the top of a stair, in the way)
    put("piece_brazierfloor01", FACADE_R - 0.9, a, TOP)

# ---------------------------------------------------------------------------------------------------------------- the main gate and its hall
gx, gz = polar(FACADE_R, MAIN)
# the main gate: a pair of stave doors, one each side, meeting in the middle (the mod opens them for a fighter and shuts them after)
# (a stave door hangs from its +x end: the east door as it comes, the west one turned round, so both hinge at the outside)
bp.place("stave_gate", gx + 1.03, gz, y=0.0, yaw=0)
_east = len(bp.items) - 1
bp.place("stave_gate", gx - 1.03, gz, y=0.0, yaw=180)
markers["maingate"] = {"x": round(gx, 3), "y": 0.0, "z": round(gz, 3), "yaw": 0.0, "piece": _east, "gate": len(bp.items) - 1}
for side in (-1, 1):
    for s in range(6):
        bp.place("blackmarble_column_1", gx + side * 1.9, gz - 0.7, y=float(s), yaw=0)
    bp.place("blackmarble_tip", gx + side * 1.9, gz - 0.7, y=6.0, yaw=0)
    bp.place("wood_dragon1", gx + side * 2.2, gz - 0.8, y=6.6, yaw=180 + side * 25)
    # a tower each side of the gate, as high as the attic, a fire on top and a banner down its face
    tx = gx + side * 4.4
    for s in range(6):
        bp.place("blackmarble_2x2x2", tx, gz - 1.3, y=2.0 * s, yaw=0)
    bp.place("piece_brazierfloor01", tx, gz - 1.3, y=12.0, yaw=0)
    bp.place("piece_banner10", tx, gz - 2.38, y=11.0, yaw=90, ref="top")
    bp.place("piece_brazierfloor01", gx + side * 6.6, gz - 1.8, y=0.0, yaw=0)
for x in (-1.0, 1.0):
    bp.place("Piece_grausten_wall_2x2", gx + x, gz, y=6.4, yaw=0)

# the tunnel from the gate hall to the fighters' grate, under the stands
r0, r1 = PODIUM_R + 1.3, INNER_R
k2 = 0
r = r0
while r < r1:
    x, z = polar(r + 2.0, MAIN)
    for side in (-1, 1):
        bp.place("Piece_grausten_wall_4x2", x + side * 2.05, z, y=0.0, yaw=90)
        bp.place("Piece_grausten_wall_4x2", x + side * 2.05, z, y=2.04, yaw=90)
        if k2 % 2 == 0:
            bp.place("piece_walltorch", x + side * 1.85, z, y=2.4, yaw=90 if side < 0 else 270)
    r += 4.1
    k2 += 1
markers["tunnel"] = {"x": 0.0, "y": 0.0, "z": round(-(r0 + 1.0), 3), "yaw": 0.0}

# the stairs up to the stands: inside the gallery, either way from the gate hall
for side in (-1, 1):
    rows = int(TOP)
    for k4 in range(rows):
        a = MAIN + side * (STAIR_START + k4 * STAIR_STEP)
        tang = (a + side * 90) % 360            # climbing away from the gate, round the gallery
        x, z = polar((INNER_R + FACADE_R) / 2, a)
        bp.place("stone_stair", x, z, y=float(k4), yaw=bp.yaw_for("stone_stair", math.sin(rad(tang)), math.cos(rad(tang))))
markers["upstairs"] = {"x": round(polar((INNER_R + FACADE_R) / 2, MAIN + STAIR_START)[0], 3), "y": 0.0, "z": round(polar((INNER_R + FACADE_R) / 2, MAIN + STAIR_START)[1], 3), "yaw": 90.0}
print("stairs from %.1f to %.1f degrees off the gate; the walk open over them from %.1f" % (STAIR_START, _seam, _gap_lo))

# ---------------------------------------------------------------------------------------------------------------- the Arena Master's box (north)
box_y = PODIUM_TOP + TIER_RISE * 2
for dx in (-2.0, 0.0, 2.0):
    for dz in (0.0, 2.0, 4.0, 6.0):            # (the last row meets the top tier behind the throne: no gap to fall through)
        bp.place("blackmarble_floor", dx, PODIUM_R + 1.4 + dz, y=box_y - 1.0, yaw=0)
    bp.place("iron_grate", dx, PODIUM_R + 0.45, y=box_y, yaw=0)     # bars along the box's front
for dx in (-3.0, 3.0):
    for s in range(3):
        bp.place("blackmarble_column_1", dx, PODIUM_R + 1.0, y=box_y + s, yaw=0)
    bp.place("blackmarble_tip", dx, PODIUM_R + 1.0, y=box_y + 3.0, yaw=0)
    bp.place("piece_banner06", dx * 0.6, PODIUM_R + 1.0, y=box_y + 0.0, yaw=0, ref="bottom") if False else None
bp.place("piece_blackmarble_throne", 0.0, PODIUM_R + 4.2, y=box_y, yaw=180)
for dx in (-3.0, 3.0):
    for s in range(3):
        bp.place("blackmarble_column_1", dx, PODIUM_R + 5.6, y=box_y + s, yaw=0)
for dx in (-2.0, 0.0, 2.0):
    for dz in (1.6, 3.6, 5.6):
        bp.place("blackmarble_floor", dx, PODIUM_R + dz, y=box_y + 3.0, yaw=0)    # the canopy
    bp.place("blackmarble_base_1", dx, PODIUM_R + 6.4, y=box_y, yaw=0)             # the backdrop
    bp.place("blackmarble_base_1", dx, PODIUM_R + 6.4, y=box_y + 2.0, yaw=0)
for dx in (-1.6, 1.6):
    bp.place("piece_banner06", dx, PODIUM_R + 5.85, y=box_y + 2.9, yaw=90, ref="top")
for dx in (-2.6, 2.6):
    bp.place("darkwood_raven", dx, PODIUM_R + 1.4, y=box_y + 4.0, yaw=90 if dx > 0 else 270)   # ravens on the canopy's corners, looking out to either side
for dx in (-1.8, 1.8):
    bp.place("piece_brazierfloor01", dx, PODIUM_R + 1.6, y=box_y, yaw=0)
bp.place("jute_carpet", 0.0, PODIUM_R + 3.0, y=box_y, yaw=0)
mark("master", 0.0, box_y, PODIUM_R + 3.6, 180.0)

# ---------------------------------------------------------------------------------------------------------------- the forecourt
court_top = -(FACADE_R + 1.0)
DEPTH, HALF = 26.0, 13.0
for x in range(-12, 13, 4):
    z = court_top - 2.0
    while z > court_top - DEPTH:
        bp.place("Piece_grausten_floor_4x4", float(x), z, y=-0.5, yaw=0)
        z -= 4.0
# the Hall of Fame: four tall plinths, two each side of the court, one for the champion of each contest (the mod puts their statue on
# it): the Long Road and the Champion Bout nearest the gate, the Endless Horde and Today's Trial beyond them; braziers between
hall = []
for k3, z in enumerate((court_top - 5.0, court_top - 12.0)):
    for row, x in enumerate((-8.0, 8.0)):
        for t in range(3):
            bp.place("blackmarble_column_2", x, z, y=float(t), yaw=22.5)
        hall.append({"x": x, "y": 3.0, "z": round(z, 3), "yaw": 90.0 if x < 0 else 270.0, "kind": ["road", "champion", "endless", "trial"][k3 * 2 + row]})
for xx in (-8.0, 8.0):
    for z in (court_top - 8.5, court_top - 15.5):
        bp.place("piece_brazierfloor01", xx, z, y=0.0, yaw=0)
markers["hall"] = hall
# the gods watch over the hall from beside the main gate (the mod puts up the game's own statues of Thor and Freya there)
markers["thor"] = {"x": -6.5, "y": 0.0, "z": round(court_top - 1.2, 3), "yaw": 160.0}
markers["freya"] = {"x": 6.5, "y": 0.0, "z": round(court_top - 1.2, 3), "yaw": 200.0}
# the Arena Master's desk, under banners
desk_z = court_top - 8.0
bp.place("piece_table_runed", 0.0, desk_z, y=0.0, yaw=0)
for side in (-1, 1):
    bp.place("darkwood_pole4", side * 2.6, desk_z + 0.6, y=0.0, yaw=0)
    bp.place("piece_banner08", side * 2.6, desk_z + 0.6, y=4.0, yaw=0, ref="top")
bp.place("darkwood_beam4x4", 0.0, desk_z + 0.6, y=3.6, yaw=0)
bp.place("darkwood_beam", 0.0, desk_z + 0.6, y=3.6, yaw=0) if False else None
mark("desk", 0.0, 0.0, desk_z, 180.0)
mark("masterspot", 0.0, 0.0, desk_z + 1.0, 180.0)
# a low stone wall along both sides of the forecourt, masts with banners, lanterns along the way in
for side in (-1, 1):
    z = court_top - 1.6
    while z > court_top - DEPTH + 1.0:
        bp.place("stone_wall_2x1", side * 14.0, z, y=0.0, yaw=90, height=1.0)
        z -= 2.2
    for k in range(3):
        mz = court_top - 3.0 - k * 9.0
        bp.place("darkwood_pole4", side * 12.6, mz, y=0.0, yaw=0)
        bp.place("darkwood_pole4", side * 12.6, mz, y=4.0, yaw=0)
        bp.place(["piece_banner01", "piece_banner02", "piece_banner05"][k], side * 12.6 + side * 0.3, mz, y=8.0, yaw=0, ref="top")
    for k in range(3):
        bp.place("piece_dvergr_lantern_pole", side * 4.2, court_top - 12.0 - k * 4.0, y=0.0, yaw=90 - side * 90)
# the gateway you come in by, behind the waystone
gate_z = court_top - DEPTH + 1.2
for side in (-1, 1):
    bp.place("Piece_grausten_pillarbase_medium", side * 4.0, gate_z, y=0.0, yaw=0)
    bp.place("Piece_grausten_pillarbase_tapered", side * 4.0, gate_z, y=1.0, yaw=0)
    bp.place("Piece_grausten_pillarbase_tapered", side * 4.0, gate_z, y=3.0, yaw=0)
    bp.place("Piece_grausten_pillarbeam_medium", side * 4.0, gate_z, y=5.0, yaw=0)
    bp.place("piece_brazierfloor01", side * 4.0, gate_z, y=6.0, yaw=0)
bp.place("darkwood_beam4x4", -2.0, gate_z, y=5.5, yaw=0)
bp.place("darkwood_beam4x4", 2.0, gate_z, y=5.5, yaw=0)
bp.place("piece_banner10", 0.0, gate_z - 0.3, y=5.5, yaw=90, ref="top")
# where you arrive (the waystone), at the far end of the forecourt
mark("arrival", 0.0, 0.0, court_top - DEPTH + 4.0, 0.0)
for side in (-1, 1):
    bp.place("piece_groundtorch", side * 3.0, court_top - DEPTH + 4.0, y=0.0, yaw=0)
    bp.place("piece_groundtorch", side * 12.0, court_top - 1.5, y=0.0, yaw=0)
    bp.place("piece_groundtorch", side * 12.0, court_top - DEPTH + 1.5, y=0.0, yaw=0)

# ---------------------------------------------------------------------------------------------------------------- cover for the floor: a set is put up for each contest
def prop_set(name, build):
    sub = Blueprint(name, P)
    build(sub)
    props[name] = sub.items

def ruins(b):
    for a, r in ((20, 7.0), (110, 8.5), (200, 6.5), (290, 9.0)):
        x, z = polar(r, a)
        b.place("stone_wall_4x2", x, z, y=0.0, yaw=a + 90, height=WALL_H)
        b.place("stone_wall_2x1", x + 0.8, z, y=WALL_H, yaw=a + 90, height=1.0)
    for a, r in ((65, 4.5), (245, 4.5), (155, 11.0), (335, 11.0)):
        x, z = polar(r, a)
        b.place("stone_pillar", x, z, y=0.0, yaw=a)
        b.place("stone_pillar", x, z, y=2.0, yaw=a)

def pillars(b):
    for k5 in range(6):
        a = k5 * 60 + 30
        x, z = polar(8.0, a)
        for s in range(4):
            b.place("blackmarble_column_1", x, z, y=float(s), yaw=0)
        b.place("piece_brazierfloor01", x, z, y=4.0, yaw=0)
    for k5 in range(3):
        x, z = polar(3.0, k5 * 120)
        b.place("blackmarble_column_2", x, z, y=0.0, yaw=0)

def stakes(b):
    for a in range(0, 360, 45):
        x, z = polar(9.0 if a % 90 else 5.5, a)
        b.place("piece_sharpstakes", x, z, y=0.0, yaw=a)
    for a in (0, 120, 240):
        x, z = polar(2.5, a)
        b.place("stake_wall", x, z, y=0.0, yaw=a + 90)

def platform(b):
    for dx in (-2.0, 0.0, 2.0):
        for dz in (-2.0, 0.0, 2.0):
            b.place("stone_floor_2x2", dx, dz, y=0.5, yaw=0)
    for a in (0, 90, 180, 270):
        x, z = polar(4.0, a)
        b.place("stone_stair", x, z, y=0.0, yaw=b.yaw_for("stone_stair", -math.sin(rad(a)), -math.cos(rad(a))))
    for a in (45, 135, 225, 315):
        x, z = polar(2.8, a)
        b.place("piece_brazierfloor01", x, z, y=1.5, yaw=0)

prop_set("Ruins", ruins)
prop_set("Pillars", pillars)
prop_set("Stakes", stakes)
prop_set("Platform", platform)

# the shape of the fighting floor: bumps added to the level ground (each: x, z, radius, height), flat within 2 m of the wall
bumps = [
    [5.5, -6.0, 3.6, 1.1], [-7.0, 3.5, 4.2, 1.3], [8.0, 6.5, 3.0, 0.8],
    [-3.5, -8.5, 3.2, -0.7], [2.0, 9.5, 3.4, -0.6], [-9.5, -3.0, 2.6, 0.7],
]

# ---------------------------------------------------------------------------------------------------------------- write it out
out = {"name": "The Arena", "floor": FLOOR, "podium": PODIUM_R, "facade": FACADE_R, "top": TOP, "footprint": FACADE_R + 3.0,
       "forecourt": {"x0": -14.0, "x1": 14.0, "z0": round(court_top - DEPTH - 1.0, 2), "z1": round(court_top + 1.0, 2)},
       "bumps": bumps, "pieces": bp.items, "props": props, "markers": markers}
with open(os.path.join(HERE, "..", "Layout.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, separators=(",", ":"))
counts = {}
for it in bp.items:
    counts[it["p"]] = counts.get(it["p"], 0) + 1
print(len(bp.items), "pieces;", len(seats), "seats;", sum(len(v) for v in props.values()), "prop pieces")
print(sorted(counts.items(), key=lambda kv: -kv[1])[:24])

if "--no-render" not in sys.argv:
    from preview import render
    os.makedirs(os.path.join(HERE, "renders"), exist_ok=True)
    render(bp.items + props["Ruins"], P, os.path.join(HERE, "renders", "overview.png"), views=((25, 35), (200, 30), (0, 89), (0, 10)), size=(900, 900))
