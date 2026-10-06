"""
A stave church after Borgund (Norway, about 1180): tier upon tier of steep black roofs, carved dragons rising from the gables, a ridge
turret with a needle spire, and an open gallery of little carved columns wrapping the base. Built from the best of every biome: stave
timber (the strongest wood, so it can stand tall) for the posts, columns and arcades, Ashlands blackwood for the tarred-black walls,
darkwood shingles for every roof, grausten for the plinth.

    python stave_church.py [preview.png] [close.png]

Plan (from the real church's plan, on Valheim's 2 m grid; the front, -z, is the west door):
  plinth        grausten slabs, 0.5 m, under everything
  gallery       an arcade ring 2 m outside the walls, under a low ring roof (the svalgang)
  nave          black walls 6 m high on stave posts, a second ring roof above them
  central nave  10 free-standing columns with carved arches, a clerestory with openwork windows, a 67 degree main roof
  turret        straddling the ridge: walls, a skirt roof, an openwork belfry, a pyramid spire and a needle (about 30 m)
  chancel, apse to the east, with their own roofs and a little spire
  porches       west (main), north and south, each a gable with a dragon
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
bp = Blueprint("Stave church", P, anchor="look", yaw=0)
Y0 = 0.5                                    # floor level on top of the plinth

WALL, LATTICE, HALF = "ashwood_wall_2x2", "ashwood_decowall_2x2", "ashwood_halfwall_1x2"
ARCADE = "stave_deco_wall_2x2"
ROOF26, ROOF45, ROOF67 = "darkwood_roof", "darkwood_roof_45", "darkwood_roof_67"
TOP45, TOP67 = "darkwood_roof_top_45", "darkwood_roof_top_67"
OC26, OC45, OC67 = "darkwood_roof_ocorner", "darkwood_roof_ocorner_45", "darkwood_roof_ocorner_67"
W45, W67 = "ashwood_wall_roof_45", "ashwood_wall_roof_67_a"
RISE = {ROOF26: 1.0, ROOF45: 2.0, ROOF67: 4.0, OC26: 1.0, OC45: 2.0, OC67: 4.0}

# ---------------------------------------------------------------- helpers

def wall_x(piece, x, z, y_bottom):
    """A 2 m wall piece centred at x on the line z (runs along x)."""
    bp.place(piece, x, z, y=y_bottom)


def wall_z(piece, x, z, y_bottom):
    bp.place(piece, x, z, y=y_bottom, yaw=90)


def column(x, z, y_bottom, height):
    """Stave posts from y_bottom up `height` metres (4 m and 2 m pieces)."""
    y = y_bottom
    while height - (y - y_bottom) > 0.05:
        left = height - (y - y_bottom)
        name = "stave_pole_4m" if left >= 3.95 else "stave_pole_2m"
        bp.place_snap(name, "bottom", (x, y, z))
        y += 4.0 if name == "stave_pole_4m" else 2.0


def corner_yaw(sx, sz):
    """Yaw that turns an outside-corner roof piece so its outer corner points to (sx, sz) (its local outer corner is +x +z)."""
    return {(1, 1): 0.0, (1, -1): 90.0, (-1, -1): 180.0, (-1, 1): 270.0}[(sx, sz)]


def ring_roof(x0, x1, z0, z1, y, roof, corner, skip=()):
    """One row of roof all round a rectangle, rising inward 2 m from its edges, with outside-corner pieces at the corners.
    y is the height of the bottom (eave) edge. skip: cell centres (x, z) to leave out (porches, chancel)."""
    up = {"s": (0, 1), "n": (0, -1), "w": (1, 0), "e": (-1, 0)}
    for xc in range(x0 + 3, x1 - 2, 2):
        for side, zl in (("s", z0), ("n", z1)):
            cell = (xc, zl + (1 if side == "s" else -1))
            if cell not in skip:
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (xc, y, zl), yaw=bp.yaw_for(roof, *up[side]))
    for zc in range(z0 + 3, z1 - 2, 2):
        for side, xl in (("w", x0), ("e", x1)):
            cell = (xl + (1 if side == "w" else -1), zc)
            if cell not in skip:
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (xl, y, zc), yaw=bp.yaw_for(roof, *up[side]))
    for sx, xl in ((-1, x0), (1, x1)):
        for sz, zl in ((-1, z0), (1, z1)):
            if (xl - sx, zl - sz) not in skip:
                bp.place_snap(corner, "bottom center", (xl, y, zl), yaw=corner_yaw(sx, sz))


def gable_roof(x0, x1, z0, z1, y, roof, top, along="z"):
    """A gable roof over a rectangle whose ridge runs along z (or x): a row of `roof` up from each long side, and the ridge
    cap `top` when the two rows end 2 m apart (rows simply meet when the span is 4 m)."""
    rise = RISE[roof]
    if along == "z":
        span, cells = x1 - x0, range(z0 + 1, z1, 2)
        rows = (span // 2 - (1 if (span // 2) % 2 else 0)) // 2 if span > 4 else 1
        for zc in cells:
            for k in range(rows):
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (x0 + 2 * k, y + rise * k, zc), yaw=bp.yaw_for(roof, 1, 0))
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (x1 - 2 * k, y + rise * k, zc), yaw=bp.yaw_for(roof, -1, 0))
            if span - 4 * rows == 2 and top:
                bp.place_mid(top, ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), ((x0 + x1) / 2, y + rise * rows, zc), yaw=90)
    else:
        span, cells = z1 - z0, range(x0 + 1, x1, 2)
        rows = (span // 2 - (1 if (span // 2) % 2 else 0)) // 2 if span > 4 else 1
        for xc in cells:
            for k in range(rows):
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (xc, y + rise * k, z0 + 2 * k), yaw=bp.yaw_for(roof, 0, 1))
                bp.place_mid(roof, ("bottom 1", "bottom 2"), (xc, y + rise * k, z1 - 2 * k), yaw=bp.yaw_for(roof, 0, -1))
            if span - 4 * rows == 2 and top:
                bp.place_mid(top, ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), (xc, y + rise * rows, (z0 + z1) / 2), yaw=0)
    return rows


def gable_wall(line, lo, hi, y, wedge, wedge_rise, along="z", fill=WALL, cap=None):
    """Close the triangle under a gable roof on the line z=line (when the ridge runs along z) between lo and hi.
    Each 2 m step inward from the eaves rises wedge_rise: full walls up to the step, a wedge on top."""
    mid = (lo + hi) / 2
    for c in range(int(lo) + 1, int(hi), 2):
        steps = int((min(c - lo, hi - c) + 1) // 2)        # 1 for the outermost cell
        base = y + wedge_rise * (steps - 1)
        h = y
        while base - h > 0.05:                              # plain wall up to where this cell's slope starts
            piece = fill if base - h >= 1.95 else HALF
            if along == "z":
                bp.place(piece, c, line, y=h)
            else:
                bp.place(piece, line, c, y=h, yaw=90)
            h += 2.0 if piece == fill else 1.0
        rising = 1 if c < mid else -1
        if cap and abs(c - mid) < 1.01:
            continue
        if along == "z":
            bp.place_mid(wedge, ("bottom", "inner bottom"), (c, base, line), yaw=bp.yaw_for(wedge, rising, 0))
        else:
            bp.place_mid(wedge, ("bottom", "inner bottom"), (line, base, c), yaw=bp.yaw_for(wedge, 0, rising))


def dragon(x, y, z, out_x, out_z, tilt=-28.0, piece="wood_dragon1"):
    """A carved head at a gable peak, pointing out (out_x, out_z) and raised by tilt degrees."""
    yaw = math.degrees(math.atan2(out_x, out_z)) % 360.0
    bp.raw(piece, x, y, z, yaw=round(yaw, 3), rx=tilt)


# ================================================================ the plinth
for x0, x1, z0, z1 in [(-8, 8, -10, 10), (-4, 4, 10, 14), (-2, 2, 14, 18), (-2, 2, -14, -10), (8, 12, -6, -2), (-12, -8, -6, -2)]:
    for xc in range(x0 + 2, x1, 4):
        for zc in range(z0 + 2, z1, 4):
            bp.place("Piece_grausten_floor_4x4", xc, zc, y=0.0, ground=True)

# ================================================================ the nave: walls, posts and its ring roof
NX, NZ = 6, 8                                   # nave outer walls at x = +-6, z = +-8
WEST_DOOR, SIDE_DOORS = (-1, 1), (-5, -3)        # the west portal and the north and south portals (two leaves each)
for xc in range(-NX + 1, NX, 2):
    for zl in (-NZ, NZ):
        if zl == -NZ and xc in WEST_DOOR:
            continue
        if zl == NZ and xc in (-1, 1):
            continue                             # the choir opening into the chancel
        for k, piece in enumerate((WALL, WALL, LATTICE if xc in (-3, 3) else WALL)):
            wall_x(piece, xc, zl, Y0 + 2 * k)
for zc in range(-NZ + 1, NZ, 2):
    for xl in (-NX, NX):
        if zc in SIDE_DOORS:
            bp.place("ashwood_door", xl, zc, y=Y0, yaw=90)
            bp.place(HALF, xl, zc, y=Y0 + 3.0, yaw=90)
            wall_z(WALL, xl, zc, Y0 + 4.0)
            continue
        for k, piece in enumerate((WALL, WALL, LATTICE if zc in (1, 5) else WALL)):
            wall_z(piece, xl, zc, Y0 + 2 * k)
for xc in WEST_DOOR:                            # west portal: two black doors, 3 m tall, with wall above
    bp.place("ashwood_door", xc, -NZ, y=Y0)
    bp.place(HALF, xc, -NZ, y=Y0 + 3.0)
    wall_x(WALL, xc, -NZ, Y0 + 4.0)
for xc in (-1, 1):                              # over the choir opening
    wall_x(WALL, xc, NZ, Y0 + 4.0)
for x in (-6, -2, 2, 6):                        # staves: heavy posts every 4 m round the outside
    for z in (-NZ, NZ):
        column(x, z, Y0, 6.0)
for z in (-4, 0, 4):
    for x in (-NX, NX):
        column(x, z, Y0, 6.0)

# the aisle roof: from the top of the nave walls (y 6.5) up to the clerestory 2 m in
ring_roof(-NX, NX, -NZ, NZ, Y0 + 6.0, ROOF45, OC45)

# ================================================================ the central nave: columns, arches, clerestory, main roof
CX, CZ = 4, 6
COLS = [(x, z) for x in (-CX, CX) for z in (-6, -2, 2, 6)] + [(0, -CZ), (0, CZ)]
for x, z in COLS:
    column(x, z, Y0, 18.0 if x == 0 else 12.0)   # the middle columns at each end go on up the gable to carry its dragon
# carved arches between the columns (two quarter-arches meet in the middle of each 4 m span), with a beam above them
ARCH_Y = Y0 + 5.6
for xl in (-CX, CX):
    for za, zb in ((-6, -2), (-2, 2), (2, 6)):
        bp.raw("darkwood_arch", xl, ARCH_Y, za, yaw=bp.yaw_for("darkwood_arch", 0, 1))
        bp.raw("darkwood_arch", xl, ARCH_Y, zb, yaw=bp.yaw_for("darkwood_arch", 0, -1))
        bp.raw("stave_beam_4m", xl, Y0 + 8.0 - 0.36, (za + zb) / 2, yaw=90)
for zl in (-CZ, CZ):
    for xa, xb in ((-4, 0), (0, 4)):
        bp.raw("darkwood_arch", xa, ARCH_Y, zl, yaw=bp.yaw_for("darkwood_arch", 1, 0))
        bp.raw("darkwood_arch", xb, ARCH_Y, zl, yaw=bp.yaw_for("darkwood_arch", -1, 0))
        bp.raw("stave_beam_4m", (xa + xb) / 2, Y0 + 8.0 - 0.36, zl)
# the clerestory: two rows of wall above the aisle roof, the upper one with openwork windows here and there
for zc in range(-CZ + 1, CZ, 2):
    for xl in (-CX, CX):
        wall_z(WALL, xl, zc, Y0 + 8.0)
        wall_z(LATTICE if zc in (-3, 3) else WALL, xl, zc, Y0 + 10.0)
for xc in range(-CX + 1, CX, 2):
    for zl in (-CZ, CZ):
        wall_x(WALL, xc, zl, Y0 + 8.0)
        wall_x(LATTICE if xc in (-1, 1) else WALL, xc, zl, Y0 + 10.0)

# tie beams across the nave at the top of the columns: they carry the turret
for zl in (-2, 2):
    for xc in (-2, 2):
        bp.raw("stave_beam_4m", xc, Y0 + 12.0 - 0.36, zl)
for xl in (-2, 2):
    bp.raw("stave_beam_4m", xl, Y0 + 12.0 - 0.36, 0, yaw=90)

# the main roof: one steep pitch, two 67 degree rows from each side meeting at a sharp ridge 8 m above the eaves (as at Borgund);
# the turret stands where the ridge crosses the middle
MAIN_EAVE = Y0 + 12.0
RIDGE = MAIN_EAVE + 8.0
for zc in range(-CZ + 1, CZ, 2):
    for k in range(2):
        if k == 1 and abs(zc) < 2:
            continue                             # inside the turret
        bp.place_mid(ROOF67, ("bottom 1", "bottom 2"), (-CX + 2 * k, MAIN_EAVE + 4 * k, zc), yaw=bp.yaw_for(ROOF67, 1, 0))
        bp.place_mid(ROOF67, ("bottom 1", "bottom 2"), (CX - 2 * k, MAIN_EAVE + 4 * k, zc), yaw=bp.yaw_for(ROOF67, -1, 0))
for zl in (-CZ, CZ):                            # gables: a steep wedge at each side, walls and a steep wedge in the middle
    for xc, rising in ((-3, 1), (3, -1)):
        bp.place_mid(W67, ("bottom", "inner bottom"), (xc, MAIN_EAVE, zl), yaw=bp.yaw_for(W67, rising, 0))
    for xc, rising in ((-1, 1), (1, -1)):
        wall_x(WALL, xc, zl, MAIN_EAVE)
        wall_x(WALL, xc, zl, MAIN_EAVE + 2.0)
        bp.place_mid(W67, ("bottom", "inner bottom"), (xc, MAIN_EAVE + 4.0, zl), yaw=bp.yaw_for(W67, rising, 0))
    dragon(0, RIDGE - 1.3, zl + (-0.3 if zl < 0 else 0.3), 0, -1 if zl < 0 else 1)

# ================================================================ the ridge turret
TB = MAIN_EAVE                                  # it stands on the tie beams
for k in range(6):
    piece = LATTICE if k == 5 else WALL         # the belfry: openwork all round at the top
    for c in (-1, 1):
        for l in (-2, 2):
            wall_x(piece, c, l, TB + 2 * k)
            wall_z(piece, l, c, TB + 2 * k)
for x in (-2, 2):
    for z in (-2, 2):
        column(x, z, TB, 12.0)
SKIRT = TB + 8.0                                # level with the main ridge                                # a skirt roof round the turret, below the belfry
for c in (-1, 1):
    for side in (-1, 1):
        bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (c, SKIRT, side * 3.0), yaw=bp.yaw_for(ROOF45, 0, -side))
        bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (side * 3.0, SKIRT, c), yaw=bp.yaw_for(ROOF45, -side, 0))
for sx in (-1, 1):
    for sz in (-1, 1):
        bp.place_snap(OC45, "bottom center", (sx * 3.0, SKIRT, sz * 3.0), yaw=corner_yaw(sx, sz))
        dragon(sx * 2.25, SKIRT - 0.9, sz * 2.25, sx, sz, tilt=-15.0, piece="darkwood_raven")   # perched on the turret's posts
SPIRE = TB + 12.0                               # the spire: four steep corner pieces make a pyramid, then a needle
for sx in (-1, 1):
    for sz in (-1, 1):
        bp.place_snap(OC67, "bottom center", (sx * 2.0, SPIRE, sz * 2.0), yaw=corner_yaw(sx, sz))

# ================================================================ the gallery (svalgang)
GX, GZ = 8, 10
PORCH_CELLS = {(-1, -GZ + 1), (1, -GZ + 1), (GX - 1, -5), (GX - 1, -3), (-GX + 1, -5), (-GX + 1, -3)}
CHANCEL_CELLS = {(x, GZ - 1) for x in (-3, -1, 1, 3)}
for xc in range(-GX + 1, GX, 2):
    for zl in (-GZ, GZ):
        if (zl == -GZ and xc in (-1, 1)) or (zl == GZ and abs(xc) < 4):
            continue
        wall_x(ARCADE, xc, zl, Y0)
for zc in range(-GZ + 1, GZ, 2):
    for xl in (-GX, GX):
        if zc in (-5, -3):
            continue
        wall_z(ARCADE, xl, zc, Y0)
for x in range(-GX, GX + 1, 4):
    for z in (-GZ, GZ):
        if abs(x) > 2 or z == GZ and abs(x) > 4:
            column(x, z, Y0, 2.0)
for z in range(-GZ + 4, GZ, 4):
    for x in (-GX, GX):
        if z not in (-4,):
            column(x, z, Y0, 2.0)
ring_roof(-GX, GX, -GZ, GZ, Y0 + 2.0, ROOF45, OC45, skip=PORCH_CELLS | CHANCEL_CELLS)

# ================================================================ chancel and apse
KX, KZ0, KZ1 = 4, NZ, 14
for zc in range(KZ0 + 1, KZ1, 2):
    for xl in (-KX, KX):
        for k in range(3):
            wall_z(LATTICE if (k == 2 and zc == 11) else WALL, xl, zc, Y0 + 2 * k)
for xc in (-3, 3):
    for k in range(3):
        wall_x(WALL, xc, KZ1, Y0 + 2 * k)
for xc in (-1, 1):                              # opening into the apse, wall above it
    wall_x(WALL, xc, KZ1, Y0 + 4.0)
for x in (-KX, KX):
    for z in (KZ1,):
        column(x, z, Y0, 6.0)
CH_EAVE = Y0 + 6.0
gable_roof(-KX, KX, KZ0, KZ1, CH_EAVE, ROOF45, TOP45, along="z")
for zl in (KZ0, KZ1):
    gable_wall(zl, -KX, KX, CH_EAVE, W45, 2.0, along="z")
dragon(0, CH_EAVE + 3.6, KZ1 + 0.3, 0, 1)
# the apse: a little square tower with a pyramid spire
AX, AZ0, AZ1 = 2, KZ1, 18
for zc in (15, 17):
    for xl in (-AX, AX):
        for k in range(2):
            wall_z(WALL, xl, zc, Y0 + 2 * k)
for xc in (-1, 1):
    for k in range(2):
        wall_x(WALL, xc, AZ1, Y0 + 2 * k)
for sx in (-1, 1):
    bp.place_snap(OC67, "bottom center", (sx * AX, Y0 + 4.0, AZ0), yaw=corner_yaw(sx, -1))
    bp.place_snap(OC67, "bottom center", (sx * AX, Y0 + 4.0, AZ1), yaw=corner_yaw(sx, 1))
bp.place_snap("darkwood_pole", "bottom", (0, Y0 + 7.6, (AZ0 + AZ1) / 2))

# ================================================================ porches
def porch(cx, cz, out_x, out_z):
    """A gabled porch 4 m wide, its ridge pointing out (out_x, out_z) from the gallery, from the nave wall out 4 m past the gallery."""
    if out_z:
        x0, x1 = cx - 2, cx + 2
        z_in, z_out = (-NZ, -GZ - 4) if out_z < 0 else (NZ, GZ + 4)
        z0, z1 = min(z_in, z_out), max(z_in, z_out)
        for xl in (x0, x1):                      # openwork sides beyond the gallery line
            for zc in range(min(-GZ, z_out) + 1, max(-GZ, z_out), 2) if out_z < 0 else range(GZ + 1, z_out, 2):
                wall_z(ARCADE, xl, zc, Y0)
                column(xl, zc - out_z, Y0, 3.0)
        rows_eave = Y0 + 3.0
        for zc in range(z0 + 1, z1, 2):
            bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (x0, rows_eave, zc), yaw=bp.yaw_for(ROOF45, 1, 0))
            bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (x1, rows_eave, zc), yaw=bp.yaw_for(ROOF45, -1, 0))
        gable_wall(z_out, x0, x1, rows_eave, W45, 2.0, along="z")
        for xa, rising in ((x0, 1), (x1, -1)):
            bp.raw("darkwood_arch", xa, Y0, z_out, yaw=bp.yaw_for("darkwood_arch", rising, 0))
        dragon(cx, rows_eave + 2.1, z_out + 0.3 * out_z, 0, out_z)
    else:
        z0, z1 = cz - 2, cz + 2
        x_in, x_out = (NX, GX + 4) if out_x > 0 else (-NX, -GX - 4)
        x0, x1 = min(x_in, x_out), max(x_in, x_out)
        for zl in (z0, z1):
            for xc in (range(GX + 1, x_out, 2) if out_x > 0 else range(x_out + 1, -GX, 2)):
                wall_x(ARCADE, xc, zl, Y0)
                column(xc + out_x, zl, Y0, 3.0)
        rows_eave = Y0 + 3.0
        for xc in range(x0 + 1, x1, 2):
            bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (xc, rows_eave, z0), yaw=bp.yaw_for(ROOF45, 0, 1))
            bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (xc, rows_eave, z1), yaw=bp.yaw_for(ROOF45, 0, -1))
        gable_wall(x_out, z0, z1, rows_eave, W45, 2.0, along="x")
        for za, rising in ((z0, 1), (z1, -1)):
            bp.raw("darkwood_arch", x_out, Y0, za, yaw=bp.yaw_for("darkwood_arch", 0, rising))
        dragon(x_out + 0.3 * out_x, rows_eave + 2.1, cz, out_x, 0)


porch(0, -GZ, 0, -1)
porch(GX, -4, 1, 0)
porch(-GX, -4, -1, 0)

# ================================================================ inside
# the altar in the apse, candles, banners between the columns, benches in the aisles, lanterns hanging from the tie beams
bp.place("piece_blackmarble_table", 0, 16.0, y=Y0)
for x in (-0.8, 0.8):
    bp.place("Candle_resin", x, 16.0, y=Y0 + 0.82)
bp.place("piece_blackmarble_throne", 0, 12.6, y=Y0, yaw=180)
for z in (1.0, 5.0):                          # benches along the aisles, between the posts
    bp.place("piece_bench01", -5.0, z, y=Y0, yaw=90)
    bp.place("piece_bench01", 5.0, z, y=Y0, yaw=270)
for z in (-1.0, 3.0, 7.0):                     # banners hung on the aisle walls
    bp.raw("piece_banner05", -NX + 0.3, Y0 + 5.6, z)
    bp.raw("piece_banner05", NX - 0.3, Y0 + 5.6, z)
for z in (-6.0, 6.0):
    bp.place("piece_dvergr_lantern", 0, z + (1.0 if z < 0 else -1.0), y=Y0 + 7.0)

# ================================================================ the churchyard approach: a path and lantern poles
for z in (-16, -20, -24, -28):
    bp.place("Piece_grausten_floor_4x4", 0, z, y=0.0, ground=True)
for z in (-17.0, -23.0):
    for x in (-2.6, 2.6):
        bp.place("piece_dvergr_lantern_pole", x, z, y=0.0, ground=True, yaw=0 if x > 0 else 180)

# the churchyard: a low stone wall all round, and a roofed lych-gate where the path comes in, with its own dragon
YX0, YX1, YZ0, YZ1 = -18, 18, -28, 24
for xc in range(YX0 + 1, YX1, 2):
    for zl in (YZ0, YZ1):
        if zl == YZ0 and abs(xc) < 3:
            continue
        bp.place("stone_fence", xc, zl, y=0.0, ground=True)
for zc in range(YZ0 + 1, YZ1, 2):
    for xl in (YX0, YX1):
        bp.place("stone_fence", xl, zc, y=0.0, ground=True, yaw=90)
LG = 2.7                                        # the lych-gate's eaves: four posts, beams, a steep gable roof
for x in (-2, 2):
    for z in (YZ0 - 2, YZ0 + 2):
        column(x, z, 0.0, 2.0)
    bp.raw("stave_beam_4m", x, 2.0 + 0.35, YZ0, yaw=90)
for zc in (YZ0 - 1, YZ0 + 1):
    bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (-2, LG, zc), yaw=bp.yaw_for(ROOF45, 1, 0))
    bp.place_mid(ROOF45, ("bottom 1", "bottom 2"), (2, LG, zc), yaw=bp.yaw_for(ROOF45, -1, 0))
for zl in (YZ0 - 2, YZ0 + 2):
    for xc, rising in ((-1, 1), (1, -1)):
        bp.place_mid(W45, ("bottom", "inner bottom"), (xc, LG, zl), yaw=bp.yaw_for(W45, rising, 0))
dragon(0, LG + 1.4, YZ0 - 2.3, 0, -1)

# ---------------------------------------------------------------- checks
ok = report(analyze(bp.items, P))
ok = bp.summary() and ok
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), "stave_church.json"))
print(bp.save(out), "pieces ->", out)
pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0], size=(900, 900))
    print("preview ->", pngs[0])
    if len(pngs) > 1:
        render(bp.items, P, pngs[1], views=((25, 6),), size=(1200, 1400), target=(0, 12, -6), dist=62)
        print("preview ->", pngs[1])
raise SystemExit(0 if ok else 1)
