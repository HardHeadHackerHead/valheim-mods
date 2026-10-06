"""
The Church of the Transfiguration, Kizhi (Russia, 1714): a log church crowned by 22 shingled onion domes in five tiers, built without a
single nail. No recreation of it in Valheim had been published when this was made.

    python kizhi.py [preview.png] [close.png]

From the real church (Kizhi museum, sobory.ru): a log octagon with four two-step arms to the cardinal points (the east arm, with the
altar, ends in a pentagon); two smaller octagons stacked on top; barrel roofs ("bochkas", rounded with a pointed keel) everywhere, each
carrying a dome. Domes by tier: 4 on the arms' lower steps, 4 on their upper steps, 8 on the main octagon, 4 on the middle octagon, the
great dome on top, and one over the altar: 22. Lower domes are larger than the next tier up, the main octagon's are the largest of the
small ones, the middle octagon's the smallest, and the top dome about three times their size.

In Valheim: the walls are core-wood logs laid log-cabin style (corners interlock and stick out); the domes and barrels are rings of
Deep North "scale" panels (carved like shingles, as Kizhi's aspen domes are) tilted to follow onion and keel curves (see shapes.py).
The front (-z) is the west door.
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report
from shapes import Shapes

P = Pieces()
bp = Blueprint("Kizhi church", P, anchor="look", yaw=0)
sh = Shapes(bp)
Y0 = 0.5                                        # top of the stone plinth
LOG4, LOG2 = "wood_wall_log_4x0.5", "wood_wall_log"
PANEL, SMALL = "scale_halfwall_1x2", "scale_quarterwall_1x1"
ROW = 0.5                                       # logs are laid every half metre

# the three octagons: apothem (centre to the middle of a face), the log for each face, bottom and top
OCT1 = dict(a=4.83, log=LOG4, y0=Y0, y1=Y0 + 10.0)
OCT2 = dict(a=2.90, log=LOG2, y0=OCT1["y1"], y1=OCT1["y1"] + 8.0)
OCT3 = dict(a=2.00, log=LOG2, y0=OCT2["y1"], y1=OCT2["y1"] + 3.5)
ARM_W = 4.0                                     # the arms are as wide as a face of the main octagon
ARM_IN = (OCT1["a"], OCT1["a"] + 2.0, Y0 + 6.5)          # upper step: from, to (distance out from the centre), wall top
ARM_OUT = (OCT1["a"] + 2.0, OCT1["a"] + 6.0, Y0 + 4.5)   # lower step
R = dict(arm_out=1.10, arm_in=0.95, oct1=1.25, oct2=0.85, top=2.40, altar=0.90)   # dome bulge radii by tier

# a bochka's cross-section for a half-width of 1: (out from its middle, up from the wall top), eave to ridge
KEEL = [(1.0, 0.0), (1.12, 0.25), (1.07, 0.55), (0.80, 0.90), (0.42, 1.22), (0.0, 1.50)]


def unit(deg):
    r = math.radians(deg)
    return math.sin(r), math.cos(r)


def at(deg, radial, tangent=0.0):
    """A point `radial` out along the direction `deg` (0 = +z, 90 = +x) and `tangent` to its right."""
    n, t = unit(deg), unit(deg + 90.0)
    return n[0] * radial + t[0] * tangent, n[1] * radial + t[1] * tangent


# ---------------------------------------------------------------- logs

def log_line(p0, p1, y0, y1, offset=0.0, overhang=0.3):
    """Logs laid along the line p0 -> p1 from height y0 to y1, a row every half metre; long logs where they fit, a short one to finish.
    The line is extended by `overhang` at both ends, as log-cabin corners stick out past each other."""
    dx, dz = p1[0] - p0[0], p1[1] - p0[1]
    length = math.hypot(dx, dz)
    ux, uz = dx / length, dz / length
    yaw = math.degrees(math.atan2(-uz, ux)) % 360.0     # turns a log's long axis (its local x) onto the line
    span = length + 2 * overhang
    y = y0 + offset + 0.25
    while y < y1 - 0.05:
        s = -overhang
        while s < span - overhang - 0.3:
            left = span - overhang - s
            name, l = (LOG4, 4.66) if left > 3.0 else (LOG2, 2.44)
            mid = min(s + l / 2, length + overhang - l / 2) if left < l else s + l / 2
            bp.raw(name, p0[0] + ux * mid, y, p0[1] + uz * mid, yaw=round(yaw, 3))
            s += l
        y += ROW


def octagon(o, skip_low=(), low_top=0.0):
    """A log octagon: faces at 0, 45, ... degrees (0 faces +z). skip_low: faces left open below low_top (where an arm joins)."""
    a = o["a"]
    side = 2 * a * math.tan(math.radians(22.5))
    for k in range(8):
        deg = 45.0 * k
        p0, p1 = at(deg, a, -side / 2), at(deg, a, side / 2)
        bottom = max(o["y0"], low_top) if deg in skip_low else o["y0"]
        log_line(p0, p1, bottom, o["y1"], offset=0.25 if k % 2 else 0.0, overhang=0.15)


def ceiling(o):
    """Floor boards over an octagon (it carries the next octagon up)."""
    a = o["a"] - 0.2
    for xc in range(-int(a), int(a) + 1, 2):
        for zc in range(-int(a), int(a) + 1, 2):
            if all(abs(xc * unit(45.0 * k)[0] + zc * unit(45.0 * k)[1]) <= a - 0.9 for k in range(8)):
                bp.raw("wood_floor", xc, o["y1"], zc)


# ---------------------------------------------------------------- bochkas (keel-pointed barrel roofs)

def bochka(deg, r0, r1, half_w, y, keel_out=True, dome_r=None, dome_at=0.5, core_from=Y0):
    """A barrel roof along the direction deg from r0 to r1 (distance out from the centre), half_w wide each side, eaves at height y.
    Its outer end gets a keel-shaped gable; a dome stands on the ridge at the fraction dome_at of its length. Returns the ridge height."""
    outline = [(u * half_w, v * half_w) for u, v in KEEL]
    length = r1 - r0
    count = max(1, math.ceil(length / 2.0 - 0.15))
    step = (length - 2.0) / (count - 1) if count > 1 else 0.0
    mids = [r0 + 1.0 + k * step for k in range(count)] if count > 1 else [r0 + length / 2]
    for side in (-1, 1):
        t = unit(deg + 90.0)
        out = (t[0] * side, t[1] * side)
        pts = sh_points(outline)
        for (u0, v0), (u1, v1) in zip(pts, pts[1:]):
            tilt = math.degrees(math.atan2(u1 - u0, v1 - v0))
            for rm in mids:
                cx, cz = at(deg, rm, side * (u0 + u1) / 2)
                sh.panel(PANEL, (cx, y + (v0 + v1) / 2, cz), out, tilt)
    ridge = y + KEEL[-1][1] * half_w
    if keel_out:                                 # the gable: an upright part, then two steep wedges meeting at the keel's point
        n = unit(deg)
        if half_w >= 1.5:
            for s in (-1, 1):
                cx, cz = at(deg, r1 + 0.05, s * half_w / 2)
                sh.panel(PANEL, (cx, y + 0.5, cz), n, 0.0)
            for s in (-1, 1):
                cx, cz = at(deg, r1 + 0.05, s * half_w / 2)
                t = unit(deg + 90.0)
                bp.place_mid("scale_wall_roof_45", ("bottom", "inner bottom"), (cx, y + 1.0, cz),
                             yaw=bp.yaw_for("scale_wall_roof_45", -s * t[0], -s * t[1]))
        else:
            cx, cz = at(deg, r1 + 0.05, 0.0)
            sh.panel(PANEL, (cx, y + 0.5, cz), n, 0.0)
            sh.panel(SMALL, (cx, y + 1.3, cz), n, 0.0)
    if dome_r:
        cx, cz = at(deg, r0 + length * dome_at)
        sh.dome(cx, ridge - 0.25, cz, dome_r, SMALL, drum=0.5, core_from=core_from)
    return ridge


def sh_points(outline):
    from shapes import resample
    return resample(outline, 1.0)


def width_at(outline, v):
    """The half-width of a keel outline at height v."""
    for (u0, v0), (u1, v1) in zip(outline, outline[1:]):
        if v0 <= v <= v1:
            return u0 + (u1 - u0) * (v - v0) / (v1 - v0) if v1 > v0 else u0
    return 0.0


# ================================================================ the plinth
for xc in range(-10, 11, 4):
    for zc in range(-10, 11, 4):
        if abs(xc) + abs(zc) <= 14 or abs(xc) <= 2 or abs(zc) <= 2:
            bp.place("Piece_grausten_floor_4x4", xc, zc, y=0.0, ground=True)
bp.place("Piece_grausten_floor_4x4", 14, 0, y=0.0, ground=True)

# ================================================================ the main octagon and its arms
octagon(OCT1, skip_low=(0.0, 90.0, 180.0, 270.0), low_top=Y0 + 3.0)
ceiling(OCT1)
half = ARM_W / 2
for deg in (0.0, 90.0, 180.0, 270.0):
    east = deg == 90.0
    r_in0, r_in1, top_in = ARM_IN
    r_out0, r_out1, top_out = ARM_OUT
    for s in (-1, 1):                             # side walls: the whole length up to the lower step's height, the upper step above it
        log_line(at(deg, r_in0, s * half), at(deg, r_out1, s * half), Y0, top_out, offset=0.25)
        log_line(at(deg, r_in0, s * half), at(deg, r_in1, s * half), top_out, top_in, offset=0.25)
    if east:                                      # the altar end: two walls meeting in a point (a pentagon in plan)
        tip = at(deg, r_out1 + 1.6)
        for s in (-1, 1):
            log_line(at(deg, r_out1, s * half), tip, Y0, top_out, overhang=0.2)
    elif deg == 180.0:                            # the west door: logs either side of it and above
        for s in (-1, 1):
            log_line(at(deg, r_out1, s * half), at(deg, r_out1, s * 0.9), Y0, Y0 + 2.5, overhang=0.3)
        log_line(at(deg, r_out1, -half), at(deg, r_out1, half), Y0 + 2.5, top_out)
        cx, cz = at(deg, r_out1)
        bp.place("wood_door", cx, cz, y=Y0, yaw=deg)
    else:
        log_line(at(deg, r_out1, -half), at(deg, r_out1, half), Y0, top_out)
    log_line(at(deg, r_in1, -half), at(deg, r_in1, half), top_out, top_in)     # the upper step's end, above the lower roof
    bochka(deg, r_out0, r_out1 + 0.3, half, top_out, dome_r=R["arm_out"], dome_at=0.55)
    bochka(deg, r_in0 - 0.3, r_in1 + 0.3, half, top_in, dome_r=R["arm_in"])
    if east:                                      # the altar dome, low over the pentagon's point
        tx, tz = at(deg, r_out1 + 0.9)
        sh.dome(tx, top_out + 0.2, tz, R["altar"], SMALL, drum=0.4, core_from=Y0)

# eight bochkas round the top of the main octagon, each with a dome (the largest of the small ones)
side1 = 2 * OCT1["a"] * math.tan(math.radians(22.5))
for k in range(8):
    bochka(45.0 * k, OCT2["a"] - 0.2, OCT1["a"] + 0.35, side1 / 2, OCT1["y1"], dome_r=R["oct1"], dome_at=0.55)

# ================================================================ the middle octagon, the drum and the great dome
# the hidden iron frame (Kizhi itself has had one inside since its restoration): posts from the ground at the corners of the middle
# octagon and of the drum, carrying them and the floors they stand on
for o in (OCT2, OCT3):
    rc = o["a"] / math.cos(math.radians(22.5)) - 0.3
    for k in range(8):
        cx, cz = at(45.0 * k + 22.5, rc)
        sh.core(cx, cz, Y0, o["y1"])
octagon(OCT2)
ceiling(OCT2)
side2 = 2 * OCT2["a"] * math.tan(math.radians(22.5))
for k in range(8):
    deg = 45.0 * k
    if k % 2 == 0:                                # four bochkas with the smallest domes
        bochka(deg, OCT3["a"] - 0.2, OCT2["a"] + 0.3, side2 / 2, OCT2["y1"], dome_r=R["oct2"], dome_at=0.6)
    else:                                         # the other four faces: a plain sloping roof in to the drum
        cx, cz = at(deg, (OCT2["a"] + OCT3["a"]) / 2 + 0.15)
        sh.panel(PANEL, (cx, OCT2["y1"] + 0.45, cz), unit(deg), -62.0)
# a sloping skirt all round the top of the middle octagon and of the drum, closing the gaps between the barrels and round the great dome
sh.ring(PANEL, 0.0, OCT2["y1"] + 0.35, 0.0, (OCT2["a"] + OCT3["a"]) / 2 + 0.2, -60.0, sides=8, start=22.5)
octagon(OCT3)
sh.ring(SMALL, 0.0, OCT3["y1"] + 0.3, 0.0, OCT3["a"] - 0.25, -55.0, sides=8, start=0.0)
sh.ring(SMALL, 0.0, OCT3["y1"] + 0.3, 0.0, OCT3["a"] - 0.25, -55.0, sides=8, start=22.5)
sh.dome(0.0, OCT3["y1"] - 0.2, 0.0, R["top"], PANEL, drum=0.8, sides=12, start=15.0, core_from=Y0)

# ================================================================ the refectory and its porch (west, in front of the west door)
FX, FZ0, FZ1, FTOP = 5.0, -(ARM_OUT[1]), -(ARM_OUT[1]) - 8.0, Y0 + 4.5
for s in (-1, 1):
    log_line((s * FX, FZ0), (s * FX, FZ1), Y0, FTOP, offset=0.25)
for xa, xb in ((-FX, -ARM_W / 2), (ARM_W / 2, FX)):           # the east wall, either side of the arm it joins
    log_line((xa, FZ0), (xb, FZ0), Y0, FTOP)
for s in (-1, 1):                                              # the west wall with its door
    log_line((s * FX, FZ1), (s * 0.9, FZ1), Y0, Y0 + 2.5, overhang=0.3)
log_line((-FX, FZ1), (FX, FZ1), Y0 + 2.5, FTOP)
bp.place("wood_door", 0.0, FZ1, y=Y0)
# its roof: two slopes of scale panels rising to a ridge along the middle, and a third slope over the west end (a "three-slope" roof)
RISE_DEG = 32.0
slant = (FX + 0.4) / math.cos(math.radians(RISE_DEG))
rows = math.ceil(slant / 2.0)
for s in (-1, 1):
    for k in range(rows):
        d = min(1.0 + 2.0 * k, slant - 1.0)
        u = FX + 0.4 - d * math.cos(math.radians(RISE_DEG))
        v = FTOP + d * math.sin(math.radians(RISE_DEG))
        n_along = math.ceil((FZ0 - FZ1) / 2.0)
        for j in range(n_along):                               # panels along the full length, the last one flush with the west end
            zc = max(FZ0 - 1.0 - 2.0 * j, FZ1 + 1.0)
            sh.panel("scale_wall_2x2", (s * u, v, zc), (s, 0.0), -(90.0 - RISE_DEG))
ridge_h = (FX + 0.4) * math.tan(math.radians(RISE_DEG))
for k in range(rows):                                          # the west hip
    d = min(1.0 + 2.0 * k, slant - 1.0)
    w = FX + 0.4 - d * math.cos(math.radians(RISE_DEG))
    v = FTOP + d * math.sin(math.radians(RISE_DEG))
    for xc in ([-w / 2, w / 2] if w > 2.2 else [0.0]):
        sh.panel("scale_wall_2x2", (xc, v, FZ1 - 0.4 + d * math.cos(math.radians(RISE_DEG))), (0.0, -1.0), -(90.0 - RISE_DEG))
sh.core(0.0, (FZ0 + FZ1) / 2, Y0, FTOP + ridge_h - 0.3)       # a hidden post under the ridge
# the east gable, either side of the church's arm: logs stepping up under the roof slope
y = FTOP + 0.25
while y < FTOP + ridge_h - 0.6:
    reach = FX + 0.4 - (y + 0.25 - FTOP) / math.tan(math.radians(RISE_DEG))
    if reach - ARM_W / 2 > 0.6:
        for s in (-1, 1):
            mid = (ARM_W / 2 + reach) / 2
            bp.raw(LOG2 if reach - ARM_W / 2 < 3.2 else LOG4, s * mid, y, FZ0 - 0.15, yaw=0.0)
    y += ROW
# the porch: carved posts and a little gable roof over the west door
PZ = FZ1 - 2.6
for x in (-1.6, 1.6):
    for z in (FZ1 - 0.4, PZ):
        bp.place_snap("stave_pole_2m", "bottom", (x, Y0, z))
for s in (-1, 1):                                              # two slopes meeting at the ridge, 35 degrees
    for zc in (FZ1 - 0.75, PZ + 0.55):
        sh.panel("scale_wall_2x2", (s * 0.82, Y0 + 2.67, zc), (s, 0.0), -55.0)
bp.place("Piece_grausten_floor_4x4", 0.0, FZ1 - 2.0, y=0.0, ground=True)

# ================================================================ the bell tower (south-west, in front): a square log base, a log octagon,
# an open belfry on posts, a tall tent roof and a small dome
BX, BZ = -13.0, -16.0
B_TOP = Y0 + 8.0
for dx in (-2, 2):                                             # its own stone plinth
    for dz in (-2, 2):
        bp.place("Piece_grausten_floor_4x4", BX + dx, BZ + dz, y=0.0, ground=True)
for (xa, za), (xb, zb), off in [((-3, -3), (3, -3), 0.0), ((3, -3), (3, 3), 0.25), ((3, 3), (-3, 3), 0.0), ((-3, 3), (-3, -3), 0.25)]:
    log_line((BX + xa, BZ + za), (BX + xb, BZ + zb), Y0, B_TOP, offset=off)
BOCT = dict(a=2.4, log=LOG2, y0=B_TOP, y1=B_TOP + 3.5)
side_b = 2 * BOCT["a"] * math.tan(math.radians(22.5))
for k in range(8):
    deg = 45.0 * k
    n, t = unit(deg), unit(deg + 90.0)
    c = (BX + n[0] * BOCT["a"], BZ + n[1] * BOCT["a"])
    log_line((c[0] - t[0] * side_b / 2, c[1] - t[1] * side_b / 2), (c[0] + t[0] * side_b / 2, c[1] + t[1] * side_b / 2),
             BOCT["y0"], BOCT["y1"], offset=0.25 if k % 2 else 0.0, overhang=0.15)
BELL = BOCT["y1"]
rc = BOCT["a"] / math.cos(math.radians(22.5))
for k in range(8):                                             # the belfry: eight posts with a rail between them
    n = unit(45.0 * k + 22.5)
    bp.place_snap("stave_pole_2m", "bottom", (BX + n[0] * rc, BELL, BZ + n[1] * rc))
    m = unit(45.0 * k)
    sh.panel(SMALL, (BX + m[0] * BOCT["a"], BELL + 0.5, BZ + m[1] * BOCT["a"]), m, 0.0)
for k in range(8):                                             # a ring of logs on the posts
    deg = 45.0 * k
    n, t = unit(deg), unit(deg + 90.0)
    c = (BX + n[0] * BOCT["a"], BZ + n[1] * BOCT["a"])
    bp.raw(LOG2, c[0], BELL + 2.25, c[1], yaw=round((-deg) % 360.0, 3))
TENT = BELL + 2.5
tent_tilt = -22.0
for k in range(4):                                             # the tent roof: rings of panels leaning in, narrowing to a point
    a = 3.0 - (k + 0.5) * 2.0 * math.sin(math.radians(-tent_tilt))
    y = TENT + (k + 0.5) * 2.0 * math.cos(math.radians(tent_tilt))
    sh.ring("scale_wall_2x2", BX, y, BZ, a, tent_tilt, sides=8, start=0.0)
    if k == 0:
        sh.ring("scale_wall_2x2", BX, y, BZ, a, tent_tilt, sides=8, start=22.5)
tent_tip = TENT + 4 * 2.0 * math.cos(math.radians(tent_tilt))
sh.dome(BX, tent_tip - 0.6, BZ, 0.7, SMALL, drum=0.3, core_from=Y0)

# ================================================================ the enclosure: a low stone wall round both, with a gap at the front
EX0, EX1, EZ0, EZ1 = -20, 18, -26, 16
for xc in range(EX0 + 1, EX1, 2):
    for zl in (EZ0, EZ1):
        if zl == EZ0 and abs(xc) < 3:
            continue
        bp.place("stone_fence", xc, zl, y=0.0, ground=True)
for zc in range(EZ0 + 1, EZ1, 2):
    for xl in (EX0, EX1):
        bp.place("stone_fence", xl, zc, y=0.0, ground=True, yaw=90)

# ---------------------------------------------------------------- checks
ok = report(analyze(bp.items, P))
ok = bp.summary() and ok
out = os.environ.get("BLUEPRINT_OUT", os.path.join(blueprints_dir(), "kizhi_church.json"))
print(bp.save(out), "pieces ->", out)
pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0], size=(900, 900))
    print("preview ->", pngs[0])
    if len(pngs) > 1:
        render(bp.items, P, pngs[1], views=((150, 12),), size=(1500, 1300), target=(0, 12, 0), dist=52)
        print("preview ->", pngs[1])
raise SystemExit(0 if ok else 1)
