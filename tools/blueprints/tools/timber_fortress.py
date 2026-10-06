"""
A medieval timber fortress for the bronze age: everything is wood, core wood and fine wood, plus bronze nails for the windows, copper for
the forge and wall torches, stone for the fire pits. No iron, no tar, no stonecutter.

    python timber_fortress.py [preview.png] [close.png]

The keep (20 x 12 m, three 4 m storeys under a steep timber roof) stands at the back of a walled courtyard:
  ground floor   great hall (long table, benches, high seat, banners, timber columns), storeroom, workshop, stair hall
  first floor    master bedroom, two bedrooms, guest room, corridors, a closet off the stair landing
  top floor      war room, armory, and the tower rooms; the four corner towers climb on (ladders) to battlements above the roof
The courtyard has a gatehouse (gate, two guard rooms, a room over the gate, battlements), a cookhouse and a smithy under lean-to roofs, a
smelter and charcoal kiln, wood stores, and a palisade with a watch platform at each corner.

Fires follow the game's rules (CLAUDE.md, "Game facts"): the keep has no fire inside (smoke has nowhere to go under three floors); its
warmth comes from fire pits in sheltered nooks against the outside walls, whose 8 m warmth reaches the beds through the walls. The cookhouse
and smithy are lean-tos rising to an open top edge, so their smoke slides out.

The anchor is the middle of the courtyard's front; the gate faces you when you place it (the front is -z).
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
bp = Blueprint("Timber fortress", P, anchor="look", yaw=0)
GABLE = "wood_wall_roof_a" if P.has("wood_wall_roof_a") else "wood_wall_roof"
STOREY = 4.0                               # each floor is two walls high

# ---------------------------------------------------------------- layout (metres; cells are 2 m, walls on even lines)
KEEP = (-10, 10, 6, 18)                   # x0, x1, z0, z1
TOWERS = [(8, 4), (8, 16), (-12, 4), (-12, 16)]   # corner towers: x0, z0 of a 4 x 4 m footprint centred on each keep corner
TOWER_TOP = 16.0                           # the battlement platform (the keep's ridge is at about 15)
PAL = (-22, 22, -20, 22)                   # palisade line
GATE = (-6, 6, -22, -18)                   # gatehouse block


def in_rect(x, z, r, strict=True):
    x0, x1, z0, z1 = r
    return (x0 < x < x1 and z0 < z < z1) if strict else (x0 <= x <= x1 and z0 <= z <= z1)


def tower_rect(t):
    return (t[0], t[0] + 4, t[1], t[1] + 4)


AX = {0: (1.0, 0.0), 90: (0.0, -1.0)}       # where a piece's local +x points for the two wall directions


# ---------------------------------------------------------------- building blocks

def seg_pos(o, i, j):
    """A wall segment: o 'x' runs along x (centre x=i on the line z=j), 'z' runs along z (on the line x=i, centre z=j)."""
    return (i, j, 0) if o == "x" else (i, j, 90)


def wall_segment(o, i, j, y, kind):
    """One 2 m wide, 4 m tall storey of wall: kind wall | door | window | rail | low (2 m) | none."""
    x, z, yaw = seg_pos(o, i, j)
    ax, az = AX[yaw]
    if kind == "none":
        return
    if kind == "rail":
        bp.raw("wood_wall_half", x, y + 0.5, z, yaw=yaw)
        return
    bp.raw("wood_door" if kind == "door" else "woodwall", x, y + 1.0, z, yaw=yaw)
    if kind == "low":
        return
    if kind == "window":
        # a half wall, then a window beside a quarter wall; neighbours mirror each other so windows come in pairs
        flip = 1 if ((i if o == "x" else j) // 2) % 2 == 0 else -1
        bp.raw("wood_wall_half", x, y + 2.5, z, yaw=yaw)
        bp.raw("wood_wall_quarter", x - flip * 0.5 * ax, y + 3.0, z - flip * 0.5 * az, yaw=yaw)
        bp.place("wood_window", x + flip * 0.5 * ax, z + flip * 0.5 * az, y=y + 3.0, yaw=yaw)
    else:
        bp.raw("woodwall", x, y + 3.0, z, yaw=yaw)


def floor_cells(cells, y):
    for (cx, cz) in cells:
        bp.raw("wood_floor", cx, y, cz)


def stair_run(x, z_from, y_from, dz, steps):
    """Stairs climbing 1 m per 2 m from (x, y_from) at the edge z_from, going towards dz (+1/-1)."""
    yaw = bp.yaw_for("wood_stair", 0, dz)
    for k in range(steps):
        bp.place_mid("wood_stair", ("bottom 1", "bottom 2"), (x, y_from + k, z_from + dz * 2 * k), yaw=yaw)


def ladder_run(x, z_from, y_from, dz, count=2):
    yaw = bp.yaw_for("wood_stepladder", 0, dz)
    for k in range(count):
        bp.place_mid("wood_stepladder", ("bottom 1", "bottom 2"), (x, y_from + 2 * k, z_from + dz * 2 * k), yaw=yaw)


def battlements(segments, y):
    """A parapet of half walls with merlons (quarter walls) on every other metre."""
    for o, i, j in segments:
        x, z, yaw = seg_pos(o, i, j)
        ax, az = AX[yaw]
        bp.raw("wood_wall_half", x, y + 0.5, z, yaw=yaw)
        bp.raw("wood_wall_quarter", x - 0.5 * ax, y + 1.0, z - 0.5 * az, yaw=yaw)


def perimeter(r):
    x0, x1, z0, z1 = r
    segs = [("x", x, z0) for x in range(x0 + 1, x1, 2)] + [("x", x, z1) for x in range(x0 + 1, x1, 2)]
    segs += [("z", x0, z) for z in range(z0 + 1, z1, 2)] + [("z", x1, z) for z in range(z0 + 1, z1, 2)]
    return segs


def cells_of(r):
    x0, x1, z0, z1 = r
    return [(x, z) for x in range(x0 + 1, x1, 2) for z in range(z0 + 1, z1, 2)]


def post(x, z, top, bottom=0.0):
    y = top
    while y - bottom > 0.05:
        if y - bottom > 1.0:
            bp.place_snap("wood_pole2", "top", (x, y, z)); y -= 2.0
        else:
            bp.place_snap("wood_pole", "top", (x, y, z)); y -= 1.0


def lean_to(x_back, x_front, z0, z1, wall_h=2.0, posts=True):
    """A shed: a back wall on the line x_back, low side walls, and a roof rising from the back wall to an open top edge at x_front."""
    dx = 1 if x_front > x_back else -1
    for zc in range(z0 + 1, z1, 2):
        bp.raw("woodwall", x_back, wall_h / 2, zc, yaw=90)
    for xc in range(min(x_back, x_front) + 1, max(x_back, x_front), 2):
        for zl in (z0, z1):
            bp.raw("woodwall", xc, wall_h / 2, zl)
    yaw = bp.yaw_for("wood_roof", dx, 0)
    n = abs(x_front - x_back) // 2
    for k in range(n):
        for zc in range(z0 + 1, z1, 2):
            bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x_back + dx * 2 * k, wall_h + k, zc), yaw=yaw)
    if posts:
        for zl in range(z0, z1 + 1, 2):
            post(x_front, zl, wall_h + n)
    return wall_h + n


# ================================================================ the keep
kx0, kx1, kz0, kz1 = KEEP
keep_cells = cells_of(KEEP)
tower_cells = [c for t in TOWERS for c in cells_of(tower_rect(t))]
all_cells = sorted(set(keep_cells) | set(tower_cells))

STAIR_A = [(7, z) for z in (9, 11, 13, 15)]     # first flight: up from the ground (x 6..8, z 8 -> 16)
STAIR_B = [(9, z) for z in (9, 11, 13, 15)]     # second flight: up from the first floor (x 8..10, z 16 -> 8)

HALL = (-4, 6, 6, 18)
floor_cells(cells_of(HALL), 0.0)               # boards in the great hall; the other ground rooms keep the levelled earth
floor_cells([c for c in all_cells if c not in STAIR_A], STOREY)
floor_cells([c for c in all_cells if c not in STAIR_B], 2 * STOREY)
stair_run(7, 8, 0.0, +1, 4)
stair_run(9, 16, STOREY, -1, 4)

# walls: an edge map per storey, (o, i, j) -> kind
storeys = [{}, {}, {}]
# simpler and exact: an outer keep segment is dropped when its centre lies inside a tower footprint
outer = [s for s in perimeter(KEEP) if not any(in_rect(seg_pos(*s)[0], seg_pos(*s)[1], tower_rect(t), strict=False) for t in TOWERS)]
tower_outer = [s for t in TOWERS for s in perimeter(tower_rect(t)) if not in_rect(seg_pos(*s)[0], seg_pos(*s)[1], KEEP, strict=False)
               or (seg_pos(*s)[0] in (kx0, kx1) and not kz0 < seg_pos(*s)[1] < kz1)
               or (seg_pos(*s)[1] in (kz0, kz1) and not kx0 < seg_pos(*s)[0] < kx1)]


def tower_window(seg):
    """One window slit per outer face of a tower: the segment nearest the tower's outer corner."""
    x, z, _ = seg_pos(*seg)
    for t in TOWERS:
        tx0, tx1, tz0, tz1 = tower_rect(t)
        if tx0 <= x <= tx1 and tz0 <= z <= tz1:
            cx = tx1 if tx0 > 0 else tx0
            cz = tz1 if tz0 > 6 else tz0
            return abs(x - cx) < 1.5 and abs(z - cz) < 1.5
    return False


for L in range(3):
    for s in outer:
        alt = ((s[1] if s[0] == "x" else s[2]) // 2) % 2 == 0
        storeys[L][s] = "window" if (L > 0 and alt) else "wall"
    for s in tower_outer:
        storeys[L][s] = "window" if (L > 0 and tower_window(s)) else "wall"
storeys[0][("x", -3, kz0)] = storeys[0][("x", 5, kz0)] = "window"   # light for the great hall

g = storeys[0]
g[("x", 1, kz0)] = "door"                      # the main door
g[("x", 3, kz1)] = "door"                      # back door, to the fire nooks behind
for z in range(kz0 + 1, kz1, 2):
    g[("z", -4, z)] = "door" if z in (9, 15) else "wall"     # great hall | storeroom and workshop
    g[("z", 6, z)] = "door" if z == 7 else "wall"            # great hall | stair hall
for x in (-9, -7, -5):
    g[("x", x, 12)] = "door" if x == -7 else "wall"          # storeroom | workshop

f1 = storeys[1]
for z in range(kz0 + 1, kz1, 2):
    f1[("z", 6, z)] = "door" if z in (7, 17) else "wall"     # corridor | closet, stair void, landing
for z in (7, 9, 13, 15, 17):
    f1[("z", -4, z)] = "wall"                                 # west bedrooms | guest room, master bedroom
    f1[("z", 4, z)] = "door" if z == 15 else "wall"          # guest room, master bedroom | corridor
for x in range(kx0 + 1, 4, 2):
    f1[("x", x, 10)] = "door" if x in (-7, 1) else "wall"    # cross corridor
    f1[("x", x, 12)] = "door" if x == -7 else "wall"
for x in (7, 9):
    f1[("x", x, 8)] = "wall"                                  # closet | stair void

f2 = storeys[2]
for z in range(kz0 + 3, kz1 - 1, 2):
    f2[("z", 8, z)] = "rail"                                  # rail along the stair opening
for z in range(kz0 + 1, kz1, 2):
    f2[("z", -4, z)] = "door" if z == 11 else "wall"         # armory | war room

for L, edges in enumerate(storeys):
    for (o, i, j), kind in edges.items():
        if L == 0 and kind == "window" and (o, i, j) in ():
            continue
        wall_segment(o, i, j, L * STOREY, kind)

# core-wood posts under the ridge, from the ground to the roof every 4 m: they carry the ridge (a 20 m roof cannot hang from its
# gables), stand as columns in the great hall and the war room, and sit in the wall lines of the rooms between
RIDGE_POSTS = [(-8, 12), (-4, 12), (0, 12), (4, 12), (6, 12)]
for x, z in RIDGE_POSTS:
    for k in range(3):
        bp.place_snap("wood_pole_log_4", "bottom", (x, 4.0 * k, z))
    bp.place_snap("wood_pole_log", "bottom", (x, 12.35, z))

# the roof: 26 degree slopes from 1 m outside each long wall to a ridge cap; gable ends filled under the slope
EAVE = 3 * STOREY
south, north = bp.yaw_for("wood_roof", 0, 1), bp.yaw_for("wood_roof", 0, -1)
for x in range(kx0 + 1, kx1, 2):
    for k in range(3):
        for zb, yaw, sgn in ((kz0 - 1 + 2 * k, south, 1), (kz1 + 1 - 2 * k, north, -1)):
            zc = zb + sgn * 1.0
            if any(in_rect(x, zc, tower_rect(t)) for t in TOWERS):
                continue
            bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, EAVE - 0.5 + k, zb), yaw=yaw)
    bp.place_mid("wood_roof_top", ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), (x, EAVE + 2.5, (kz0 + kz1) / 2), yaw=0)
for gx in (kx0, kx1):
    for zc, rise_to in ((9, 1), (11, 1), (13, -1), (15, -1)):
        h0 = EAVE + (1.0 if zc in (9, 15) else 2.0)                     # where the slope starts over this 2 m
        bp.raw("wood_wall_half" if zc in (9, 15) else "woodwall", gx, EAVE + (0.5 if zc in (9, 15) else 1.0), zc, yaw=90)
        bp.place_mid(GABLE, ("bottom", "inner bottom"), (gx, h0, zc), yaw=bp.yaw_for(GABLE, 0, rise_to))

# core-wood posts up every outer corner of each tower: wood alone does not stand 16 m tall
for t in TOWERS:
    tx0, tx1, tz0, tz1 = tower_rect(t)
    for x, z in [(tx0, tz0), (tx0, tz1), (tx1, tz0), (tx1, tz1)]:
        if in_rect(x, z, KEEP, strict=False):
            continue
        for k in range(4):
            bp.place_snap("wood_pole_log_4", "bottom", (x, 4.0 * k, z))

# the corner towers above the roof: walls all round, a floor, battlements, ladders up from the top floor
for t in TOWERS:
    r = tower_rect(t)
    tx0, tx1, tz0, tz1 = r
    for y in (EAVE, EAVE + 2):
        for o, i, j in perimeter(r):
            x, z, yaw = seg_pos(o, i, j)
            bp.raw("woodwall", x, y + 1.0, z, yaw=yaw)
    outer_x = tx1 - 0.5 if tx0 > 0 else tx0 + 0.5          # the ladder lanes, 1 m wide, against the outer side wall
    inner_x = tx1 - 1.5 if tx0 > 0 else tx0 + 1.5
    mid_x = tx0 + 1 if tx0 > 0 else tx1 - 1
    for y, gap in ((EAVE, outer_x), (TOWER_TOP, inner_x)):
        for zc in (tz0 + 1, tz0 + 3):
            bp.raw("wood_floor", mid_x, y, zc)
        keep_lane = inner_x if gap == outer_x else outer_x
        for zc in (tz0 + 0.5, tz0 + 1.5, tz0 + 2.5, tz0 + 3.5):
            bp.raw("wood_floor_1x1", keep_lane, y, zc)
    ladder_run(outer_x, tz0, 2 * STOREY, +1)
    ladder_run(inner_x, tz1, EAVE, -1)
    battlements(perimeter(r), TOWER_TOP)

# fire nooks against the keep's outside walls: a fire pit under a little roof rising away from the wall
NOOKS = [(-6, kz0, -1), (4, kz0, -1), (-5, kz1, 1), (1, kz1, 1)]
for x, zw, out in NOOKS:
    bp.place("fire_pit", x, zw + out * 1.15, y=0.0, ground=True)
    bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (x, 2.6, zw), yaw=bp.yaw_for("wood_roof", 0, out))

# the entrance: a little roof over the main door, banners either side, torches
bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (1, 2.6, kz0), yaw=bp.yaw_for("wood_roof", 0, -1))
for x in (-0.6, 2.6):
    bp.raw("piece_banner03", x, 3.9, kz0 - 0.32, yaw=90)   # a banner hangs in its local z-y plane: turn it to face along z
    bp.place("piece_walltorch", x + (-0.9 if x < 1 else 0.9), kz0 - 0.4, y=1.9, yaw=180)

# ---------------------------------------------------------------- furnishing the keep
def put(name, x, z, y=0.0, yaw=0.0):
    return bp.place(name, x, z, y=y, yaw=yaw)

# great hall: a long table with benches, the lord's high seat at the head, banners on the walls
for zc in (8.9, 12.0, 15.1):                  # the long table runs between the two hall columns
    put("piece_table", 2.0, zc, yaw=90)
    put("piece_bench01", 0.8, zc, yaw=90)
    put("piece_bench01", 3.2, zc, yaw=270)
put("piece_chair02", 2.0, 17.2, yaw=180)
for x in (-1, 3):
    bp.raw("piece_banner01", x, 3.9, kz1 - 0.3, yaw=90)
for z in (9, 15):
    bp.raw("piece_banner02", 5.7, 3.9, z)
for x, z, yaw in [(-3.6, 8, 90), (-3.6, 16, 90), (5.6, 10, 270), (5.6, 14, 270)]:
    bp.place("piece_walltorch", x, z, y=2.2, yaw=yaw)

# storeroom: chests along the walls; workshop: the workbench (build it first: it reaches 20 m) and chests
for x in (-9.0, -7.2, -5.4):
    put("piece_chest_wood", x, 6.6)
for z in (8.4, 10.2):
    put("piece_chest_wood", -9.6, z, yaw=90)
put("piece_chest_wood", -11.3, 5.2, yaw=90)
put("piece_workbench", -7.0, 17.2, yaw=180)
put("piece_workbench", 10.0, 18.6, yaw=180)       # in the north-east tower: reaches the north-east corner of the palisade
put("piece_chest_wood", -9.6, 14.0, yaw=90)
put("piece_chest_wood", -4.6, 13.3, yaw=90)

# first floor: beds near the outer walls (each within 8 m of a nook fire), chests, rugs
F1 = STOREY
put("bed", -6.0, 7.9, y=F1)                     # south-west bedroom
put("piece_chest_wood", -8.6, 9.4, y=F1)
put("bed", 2.6, 7.9, y=F1)                      # guest room
put("bed", -0.5, 7.9, y=F1)
put("bed", -6.0, 16.1, y=F1)                    # north-west bedroom
put("piece_chest_wood", -8.6, 13.0, y=F1)
put("bed", 0.6, 16.1, y=F1)                     # master bedroom
put("rug_deer", 0.6, 14.4, y=F1)
put("piece_chest_wood", -2.8, 17.2, y=F1, yaw=180)
put("piece_chest_wood", 3.0, 17.2, y=F1, yaw=180)
put("piece_table", 0.6, 12.9, y=F1)
put("piece_chair02", -1.2, 12.9, y=F1, yaw=90)

# top floor: the war room's table, the armory's chests and banners
F2 = 2 * STOREY
put("piece_table", 1.0, 12.0, y=F2, yaw=90)
for zc, yaw in ((9.8, 0), (14.2, 180)):
    put("piece_chair02", 1.0, zc, y=F2, yaw=yaw)
for z in (8.2, 10.0, 14.0, 15.8):
    put("piece_chest_wood", -9.4, z, y=F2, yaw=90)
bp.raw("piece_banner01", 1.0, F2 + 3.9, kz1 - 0.3, yaw=90)

# ================================================================ the gatehouse
gx0, gx1, gz0, gz1 = GATE
gh = [{}, {}]
for s in perimeter(GATE):
    gh[0][s] = "wall"
    gh[1][s] = "window"
for i in (-1, 1):
    gh[0][("x", i, gz0)] = "none"               # the gate leaves go here
    gh[0][("x", i, gz1)] = "none"               # open arch on the courtyard side
for z in (gz0 + 1, gz0 + 3):
    gh[0][("z", -2, z)] = "door" if z == gz0 + 3 else "wall"   # guard rooms either side of the passage
    gh[0][("z", 2, z)] = "door" if z == gz0 + 3 else "wall"
for L, edges in enumerate(gh):
    for (o, i, j), kind in edges.items():
        wall_segment(o, i, j, L * STOREY, kind)
for i in (-1, 1):
    bp.place("wood_gate", i, gz0, y=0.0)
    for zl in (gz0, gz1):
        bp.raw("wood_wall_half", i, 3.5, zl)    # lintel over the gate and the arch
up_lane, down_lane = gx0 + 0.5, gx0 + 1.5        # ladders in the west guard room
for y, gap in ((STOREY, up_lane), (2 * STOREY, down_lane)):
    floor_cells([c for c in cells_of(GATE) if c[0] > gx0 + 2], y)
    keep_lane = down_lane if gap == up_lane else up_lane
    for zc in (gz0 + 0.5, gz0 + 1.5, gz0 + 2.5, gz0 + 3.5):
        bp.raw("wood_floor_1x1", keep_lane, y, zc)
ladder_run(up_lane, gz1, 0.0, -1)
ladder_run(down_lane, gz0, STOREY, +1)
battlements(perimeter(GATE), 2 * STOREY)
for x in (-4, 4):
    bp.raw("piece_banner01", x, 7.4, gz0 - 0.32, yaw=90)   # banners over the gate, on the outside
put("piece_workbench", -4.0, gz0 + 1.0)          # the gatehouse's own workbench: the front of the fortress is beyond the keep's reach
put("piece_chest_wood", 4.0, gz0 + 0.6)

# ================================================================ the palisade and its corner platforms
px0, px1, pz0, pz1 = PAL
for c in range(px0 + 1, px1, 2):
    for x, z, yaw in [(c, pz0, 0), (c, pz1, 0)]:
        if z == pz0 and gx0 < c < gx1:
            continue
        bp.raw("stake_wall", x, -0.3, z, yaw=yaw, ground=True)
for c in range(pz0 + 1, pz1, 2):
    for x in (px0, px1):
        bp.raw("stake_wall", x, -0.3, c, yaw=90, ground=True)

PLAT = 3.0
for cx in (px0, px1):
    for cz in (pz0, pz1):
        sx, sz = (1 if cx > 0 else -1), (1 if cz > 0 else -1)
        for dx in (-2, 2):
            for dz in (-2, 2):
                bp.place_snap("wood_pole_log_4", "bottom", (cx + dx, 0.0, cz + dz))
        for dx in (-1, 1):
            for dz in (-1, 1):
                bp.raw("wood_floor", cx + dx, PLAT, cz + dz)
        for dx in (-1, 1):
            for dz in (-2, 2):
                if dz == -sz * 2 and dx == -sx:
                    continue
                bp.raw("wood_wall_half", cx + dx, PLAT + 0.5, cz + dz)
        for dz in (-1, 1):
            for dx in (-2, 2):
                bp.raw("wood_wall_half", cx + dx, PLAT + 0.5, cz + dz, yaw=90)
        stair_run(cx - sx * 1.0, cz - sz * 2.0 - sz * 6.0, 0.0, sz, 3)

# ================================================================ the courtyard
# cookhouse (west) and smithy (east): lean-tos whose roofs rise to an open top edge, so smoke slides out
lean_to(-20, -12, -8, -2)
for z in (-6.4, -3.6):
    bp.place("fire_pit", -16.5, z, y=0.0, ground=True)
    bp.place("piece_cookingstation", -16.5, z, y=0.0, ground=True, yaw=90)
bp.place("fire_pit", -14.2, -5.0, y=0.0, ground=True)
bp.place("piece_cookingstation", -14.2, -5.0, y=0.0, ground=True, yaw=90)
for z in (-6.8, -5.0, -3.2):
    put("piece_chest_wood", -19.3, z, yaw=90)
put("piece_workbench", -16.0, -10.6)             # reaches the west palisade
put("piece_table", -12.5, -10.6)

lean_to(20, 12, -8, -2)
put("forge", 17.6, -5.0, yaw=270)
put("piece_chest_wood", 19.3, -7.0, yaw=270)
put("piece_chest_wood", 19.3, -3.0, yaw=270)
put("piece_workbench", 15.5, -7.2)               # the courtyard's workbench, for the palisade's far corners
bp.place("smelter", 17.0, -12.5, y=0.0, ground=True)
bp.place("charcoal_kiln", 12.0, -14.0, y=0.0, ground=True)
for x in (-18.5, -15.5):
    bp.place("wood_stack", x, -14.0, y=0.0, ground=True)
bp.place("wood_core_stack", -12.5, -14.0, y=0.0, ground=True)

# torches along the path from the gate to the keep door
for z in (-14, -8, -2, 3):
    for sx in (-1, 1):
        bp.place("piece_groundtorch_wood", 1.0 + sx * 2.6, z, y=0.0, ground=True)

# ---------------------------------------------------------------- checks
benches = [i for i in bp.items if i["p"] == "piece_workbench"]
far = [i for i in bp.items if P.by_name[i["p"]].get("station") == "piece_workbench"
       and min(math.hypot(i["x"] - b["x"], i["z"] - b["z"]) for b in benches) >= 19.5]
if far:
    print(f"  PROBLEM: {len(far)} pieces are 20 m or more from every workbench, e.g. {far[0]['p']} at ({far[0]['x']}, {far[0]['z']})")
ok = report(analyze(bp.items, P)) and not far
ok = bp.summary() and ok
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), "timber_fortress.json"))
print(bp.save(out), "pieces ->", out)

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0], size=(900, 900))
    print("preview ->", pngs[0])
    if len(pngs) > 1:
        render(bp.items, P, pngs[1], views=((15, 12), (200, 18)), size=(1400, 900), target=(1, 5, 4), dist=26)
        print("preview ->", pngs[1])
raise SystemExit(0 if ok else 1)
