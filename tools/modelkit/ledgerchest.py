"""
The Ledger Chest: an iron-bound strongbox with the storehouse ledger on a slanted reading stand on its lid. Open it and it shows not only
what is inside it but everything in the chests around it, with a search. The look says it: a quartermaster's chest with the book open,
a quill in its inkpot and a candle to read by.

The front faces -z. About 1.3 m wide, 0.75 m deep; the lid at 0.78 m, the book's top edge about 1.25 m.
"""
import math
from modelkit import Model

PAL = {"Wicker": (0.68, 0.52, 0.31), "Grey": (0.64, 0.66, 0.70)}

W, D = 1.30, 0.74          # the box
FOOT = 0.07                 # feet height
BODY = 0.58                 # box body height (above the feet)
LID = 0.13                  # lid thickness
TOP = FOOT + BODY + LID     # top of the lid


def make():
    m = Model()
    x0, x1, z0, z1 = -W / 2, W / 2, -D / 2, D / 2
    by = FOOT + BODY / 2

    # --- feet: four squat blocks with iron shoes
    for x in (x0 + 0.08, x1 - 0.08):
        for z in (z0 + 0.08, z1 - 0.08):
            m.box("foot", x, FOOT / 2, z, 0.14, FOOT, 0.14, "Dark")
            m.box("footShoe", x, 0.012, z, 0.155, 0.024, 0.155, "Iron")

    # --- the body: a dark frame with lighter plank panels set in, front and sides
    m.box("body", 0, by, 0, W, BODY, D, "Dark")
    for i in range(5):     # front planks, inset a little
        px = x0 + 0.1 + (i + 0.5) * (W - 0.2) / 5
        m.box("frontPlank", px, by, z0 - 0.006, (W - 0.2) / 5 - 0.012, BODY - 0.12, 0.02, "Planks")
    for i in range(5):     # back planks
        px = x0 + 0.1 + (i + 0.5) * (W - 0.2) / 5
        m.box("backPlank", px, by, z1 + 0.006, (W - 0.2) / 5 - 0.012, BODY - 0.12, 0.02, "Planks")
    for sx in (-1, 1):     # side planks
        for j in range(3):
            pz = z0 + 0.1 + (j + 0.5) * (D - 0.2) / 3
            m.box("sidePlank", sx * (W / 2 + 0.006), by, pz, 0.02, BODY - 0.12, (D - 0.2) / 3 - 0.012, "Planks")
    # iron: corner straps, bands round the body, and studs
    for x in (x0, x1):
        for z in (z0, z1):
            m.box("cornerStrap", x, by, z, 0.075, BODY + 0.01, 0.075, "Iron")
    for y in (FOOT + 0.07, FOOT + BODY - 0.07):
        m.box("bandFront", 0, y, z0 - 0.018, W + 0.02, 0.05, 0.02, "Iron")
        m.box("bandBack", 0, y, z1 + 0.018, W + 0.02, 0.05, 0.02, "Iron")
        for sx in (-1, 1):
            m.box("bandSide", sx * (W / 2 + 0.018), y, 0, 0.02, 0.05, D + 0.02, "Iron")
        for k in range(7):
            m.sph("stud", x0 + 0.12 + k * (W - 0.24) / 6, y, z0 - 0.03, 0.026, 0.026, 0.016, "Iron")

    # --- the lid: planks with iron edges, a little proud of the body
    ly = FOOT + BODY + LID / 2
    m.box("lid", 0, ly, 0, W + 0.04, LID, D + 0.04, "Dark")
    for i in range(6):
        pz = z0 - 0.02 + (i + 0.5) * (D + 0.04) / 6
        m.box("lidPlank", 0, TOP + 0.004, pz, W - 0.06, 0.012, (D + 0.04) / 6 - 0.012, "Planks")
    m.box("lidEdgeFront", 0, ly - LID / 2 + 0.02, z0 - 0.024, W + 0.06, 0.035, 0.02, "Iron")
    for k in range(9):
        m.sph("lidStud", x0 + 0.06 + k * (W - 0.12) / 8, ly - LID / 2 + 0.02, z0 - 0.036, 0.022, 0.022, 0.014, "Iron")
    for sx in (-1, 1):
        m.box("lidStrap", sx * 0.42, TOP + 0.012, 0, 0.07, 0.014, D + 0.06, "Iron")
        m.box("lidStrapFront", sx * 0.42, ly - 0.02, z0 - 0.035, 0.07, LID + 0.05, 0.014, "Iron")

    # --- the lock: a big hasp plate with a gold keyhole, and a rune plaque above the feet
    m.box("lockPlate", 0, FOOT + BODY - 0.04, z0 - 0.03, 0.17, 0.2, 0.022, "Iron")
    m.box("hasp", 0, FOOT + BODY + 0.03, z0 - 0.04, 0.07, 0.15, 0.02, "Iron")
    m.sph("keyhole", 0, FOOT + BODY - 0.06, z0 - 0.043, 0.035, 0.035, 0.01, "Gold")
    m.box("keySlot", 0, FOOT + BODY - 0.09, z0 - 0.043, 0.012, 0.04, 0.008, "Black")
    # a parchment label in a brass frame, with "lines of writing", on the front
    m.box("labelFrame", 0, FOOT + 0.2, z0 - 0.028, 0.34, 0.15, 0.012, "Gold")
    m.box("label", 0, FOOT + 0.2, z0 - 0.035, 0.3, 0.115, 0.008, "Parchment")
    for k, w in enumerate((0.22, 0.18, 0.2)):
        m.box("labelInk", -0.11 + w / 2, FOOT + 0.235 - k * 0.032, z0 - 0.04, w, 0.01, 0.004, "Ink")

    # --- iron ring handles on the sides
    for sx in (-1, 1):
        hx = sx * (W / 2 + 0.03)
        for hz in (-0.09, 0.09):
            m.box("handleMount", hx, FOOT + BODY - 0.15, hz, 0.02, 0.06, 0.04, "Iron")
            m.box("handleArm", hx + sx * 0.035, FOOT + BODY - 0.19, hz, 0.06, 0.016, 0.016, "Iron")
        m.cyl("handleGrip", hx + sx * 0.065, FOOT + BODY - 0.19, 0, 0.03, 0.1, 0.03, "Dark", rx=90)

    # --- the reading stand on the lid, and the open ledger on it
    sx0, sz0 = -0.12, 0.0       # where the stand sits
    tilt = 28                   # the book slopes up towards the back
    m.box("standBase", sx0, TOP + 0.02, sz0 + 0.04, 0.62, 0.04, 0.4, "Dark")
    m.box("standBack", sx0, TOP + 0.1, sz0 + 0.2, 0.62, 0.16, 0.04, "Dark")
    m.box("standBoard", sx0, TOP + 0.12, sz0 + 0.03, 0.6, 0.03, 0.42, "Planks", rx=-tilt)
    m.box("standLip", sx0, TOP + 0.05, sz0 - 0.16, 0.6, 0.04, 0.03, "Dark", rx=-tilt)
    # the book: leather covers splayed, two page blocks, writing, a ribbon
    by_ = TOP + 0.145
    for s in (-1, 1):
        m.box("cover", sx0 + s * 0.135, by_, sz0 + 0.03, 0.27, 0.014, 0.36, "Leather", rx=-tilt, rz=s * 4)
        m.box("pages", sx0 + s * 0.128, by_ + 0.02, sz0 + 0.02, 0.245, 0.03, 0.33, "Parchment", rx=-tilt, rz=s * 7)
        for k in range(7):
            zz = sz0 + 0.12 - k * 0.04
            yy = by_ + 0.038 + (0.12 - k * 0.04) * math.sin(math.radians(tilt)) * 0.0
            m.box("writing", sx0 + s * 0.13, by_ + 0.037 + (zz - sz0 - 0.02) * math.tan(math.radians(tilt)), zz, 0.17 - (k % 3) * 0.03, 0.004, 0.007, "Ink", rx=-tilt, rz=s * 7)
    m.box("spine", sx0, by_ + 0.008, sz0 + 0.03, 0.03, 0.03, 0.36, "Leather", rx=-tilt)
    m.box("ribbon", sx0 + 0.01, by_ - 0.06, sz0 - 0.17, 0.018, 0.12, 0.006, "Red", rx=-8)

    # --- the quill in its inkpot, and a candle to read by
    ix, iz = 0.36, -0.12
    m.cyl("inkpot", ix, TOP + 0.05, iz, 0.09, 0.05, 0.09, "Coal")
    m.cyl("inkpotRim", ix, TOP + 0.1, iz, 0.06, 0.008, 0.06, "Iron")
    # the quill: a shaft leaning back out of the pot, the feather along its upper part (placed along the shaft's own axis)
    import numpy as np
    from modelkit import euler_matrix
    rot = (-14, 0, -16)
    up = euler_matrix(*rot) @ np.array([0.0, 1.0, 0.0])
    base = np.array([ix, TOP + 0.08, iz])
    def along(t, side=0.0):
        p = base + up * t + (euler_matrix(*rot) @ np.array([side, 0.0, 0.0]))
        return float(p[0]), float(p[1]), float(p[2])
    m.box("quillShaft", *along(0.13), 0.008, 0.26, 0.008, "Bone", rx=rot[0], rz=rot[2])
    m.box("quillVane", *along(0.17, 0.014), 0.026, 0.15, 0.004, "Cream", rx=rot[0], rz=rot[2])
    m.box("quillVane", *along(0.25, 0.01), 0.018, 0.04, 0.004, "Cream", rx=rot[0], rz=rot[2])
    # rolled scrolls at the back corner, tied with cord
    for k, (sx_, sz_, ry_) in enumerate(((-0.55, 0.22, 8), (-0.52, 0.12, -6), (-0.555, 0.17, 2))):
        yy = TOP + 0.035 + (0.05 if k == 2 else 0)
        m.cyl("scroll", sx_, yy, sz_, 0.07, 0.15, 0.07, "Parchment", rx=90, ry=90 + ry_)
        m.cyl("scrollTie", sx_, yy, sz_, 0.074, 0.008, 0.074, "Red", rx=90, ry=90 + ry_)
    cx, cz = 0.4, 0.18
    m.cyl("candleDish", cx, TOP + 0.015, cz, 0.13, 0.012, 0.13, "Iron")
    m.cyl("candle", cx, TOP + 0.09, cz, 0.05, 0.07, 0.05, "Cream")
    m.sph("wax", cx + 0.02, TOP + 0.13, cz - 0.015, 0.025, 0.04, 0.02, "Cream")
    m.box("wick", cx, TOP + 0.172, cz, 0.006, 0.02, 0.006, "Black")
    m.sph("flame", cx, TOP + 0.2, cz, 0.03, 0.06, 0.03, "Glow")
    return m


if __name__ == "__main__":
    import sys, os
    from modelkit import contact_sheet
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    contact_sheet(make(), os.path.join(out, "ledgerchest.png"), size=(560, 560), target=(0, 0.55, 0), dist=3.6,
                  views=((28, 22), (-35, 30), (0, 10), (150, 35)), palette=PAL)
    print("ok")
