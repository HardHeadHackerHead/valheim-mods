"""The Bounty Board: a Viking notice board with a roof, a horned skull, pinned notices, a lantern and a bell."""
import math
from modelkit import Model


def make():
    m = Model()

    # --- stone foundation and step
    m.box("plinth", 0, 0.07, 0, 2.4, 0.14, 0.8, "Stone")
    m.box("plinthTop", 0, 0.16, 0, 2.2, 0.04, 0.68, "Stone")
    m.box("step", 0, 0.03, -0.55, 1.3, 0.06, 0.3, "Stone")

    # seams between the stones
    for x in (-0.8, -0.3, 0.3, 0.8):
        m.box("seam", x, 0.07, -0.401, 0.02, 0.13, 0.004, "StoneDark")
    for x in (-0.55, 0.0, 0.55):
        m.box("seamTop", x, 0.181, -0.2, 0.02, 0.004, 0.5, "StoneDark")
    m.box("seamStep", 0, 0.061, -0.55, 1.3, 0.004, 0.015, "StoneDark")

    # --- two thick posts with iron bands
    for sx in (-1, 1):
        x = sx * 1.02
        m.box("postFoot", x, 0.3, 0, 0.36, 0.3, 0.36, "Stone")
        m.cyl("post", x, 1.3, 0, 0.24, 1.2, 0.24, "Dark")
        m.box("postBandLow", x, 0.62, 0, 0.3, 0.07, 0.3, "Iron")
        m.box("postBandHigh", x, 2.32, 0, 0.3, 0.07, 0.3, "Iron")

    # --- the board: vertical planks between the posts, framed by dark rails
    plank_w = 0.31
    for i in range(6):
        x = -0.78 + i * plank_w
        h = 1.34 + (0.03 if i % 2 else 0.0)
        m.box("plank%d" % i, x + plank_w / 2 - 0.015, 1.36 + h / 2 - 0.67, 0.0, plank_w - 0.02, h, 0.08, "Planks")
    m.box("railTop", 0, 2.04, -0.05, 1.98, 0.11, 0.07, "Dark")
    m.box("railBottom", 0, 0.7, -0.05, 1.98, 0.11, 0.07, "Dark")
    m.box("railMid", 0, 1.36, 0.06, 1.9, 0.07, 0.04, "Dark")
    for sx in (-1, 1):
        for y in (0.7, 2.04):
            m.box("bracket", sx * 0.93, y, -0.09, 0.12, 0.13, 0.03, "Iron")

    # --- a gabled roof: boards sloping both ways, a ridge, a planked triangle at the front with the skull in it
    pitch = math.radians(20)
    ridge_y, half_w = 2.92, 1.4
    cx, cy = (half_w / 2) * math.cos(pitch), ridge_y - (half_w / 2) * math.sin(pitch)
    for sx in (-1, 1):
        rz = -sx * 20
        ux, uy = -sx * math.cos(pitch), math.sin(pitch)   # up the slope, towards the ridge
        m.box("roofUnder", sx * cx, cy - 0.035, 0, half_w + 0.04, 0.04, 0.9, "Dark", rz=rz)
        for j, s_ in enumerate((-0.525, -0.175, 0.175, 0.525)):
            m.box("roofBoard", sx * cx + ux * s_, cy + uy * s_, 0, 0.33, 0.05, 0.98, "Planks", rz=rz)
        for z in (-0.5, 0.5):
            m.box("roofFascia", sx * cx, cy + 0.0, z, half_w + 0.06, 0.09, 0.035, "Dark", rz=rz)
    m.cyl("ridge", 0, ridge_y + 0.01, 0, 0.11, 0.53, 0.11, "Dark", rx=90)
    for z in (-0.54, 0.54):
        m.sph("ridgeEnd", 0, ridge_y + 0.01, z, 0.13, 0.13, 0.13, "Iron")
    eave_y = ridge_y - half_w * math.sin(pitch) * 2 / 2 * 1.0
    base_y = ridge_y - 2 * (half_w / 2) * math.sin(pitch) - 0.03
    full_w = 2 * half_w * math.cos(pitch)
    for k in range(-4, 5):
        x = k * 0.29
        h = max(0.05, (ridge_y - base_y) * (1 - abs(x) / (full_w / 2)) - 0.06)
        m.box("gable", x, base_y + h / 2, -0.3, 0.28, h, 0.05, "Planks")
    m.box("gableSill", 0, base_y - 0.02, -0.3, full_w - 0.1, 0.05, 0.07, "Dark")

    # --- a horned skull in the gable
    sy = base_y + 0.2
    m.sph("skull", 0, sy, -0.37, 0.27, 0.23, 0.2, "Bone")
    m.box("jaw", 0, sy - 0.15, -0.4, 0.15, 0.07, 0.1, "Bone")
    m.sph("eyeL", -0.065, sy - 0.01, -0.46, 0.07, 0.08, 0.04, "Black")
    m.sph("eyeR", 0.065, sy - 0.01, -0.46, 0.07, 0.08, 0.04, "Black")
    m.box("nose", 0, sy - 0.08, -0.475, 0.03, 0.05, 0.02, "Black")
    for sx in (-1, 1):
        m.cyl("hornBase", sx * 0.18, sy + 0.07, -0.37, 0.06, 0.09, 0.06, "Bone", rz=-sx * 62)
        m.cyl("hornTip", sx * 0.31, sy + 0.17, -0.37, 0.04, 0.09, 0.04, "Bone", rz=-sx * 28)

    # --- a hanging sign with carved runes, on chains from the roof
    sign_y = 2.22
    for sx in (-1, 1):
        m.box("signChain", sx * 0.42, sign_y + 0.14, -0.19, 0.018, 0.22, 0.018, "Iron")
        m.sph("signRing", sx * 0.42, sign_y + 0.25, -0.19, 0.04, 0.04, 0.04, "Iron")
    m.box("signBoard", 0, sign_y, -0.19, 0.96, 0.2, 0.05, "Dark")
    m.box("signTrim", 0, sign_y, -0.17, 1.0, 0.24, 0.02, "Planks")
    m.box("signFace", 0, sign_y, -0.218, 0.92, 0.17, 0.012, "Dark")
    # "BOUNTIES" in angular gold letters (a carved, runic look), raised well off the face so they never flicker into it
    letters = {   # strokes on a unit box: (x0, y0, x1, y1), x right, y up
        "B": [(0, 0, 0, 1), (0, 1, .65, 1), (.65, 1, 1, .78), (1, .78, .65, .5), (0, .5, .65, .5), (.65, .5, 1, .25), (1, .25, .65, 0), (.65, 0, 0, 0)],
        "O": [(.3, 1, .7, 1), (.7, 1, 1, .7), (1, .7, 1, .3), (1, .3, .7, 0), (.7, 0, .3, 0), (.3, 0, 0, .3), (0, .3, 0, .7), (0, .7, .3, 1)],
        "U": [(0, 1, 0, .28), (0, .28, .3, 0), (.3, 0, .7, 0), (.7, 0, 1, .28), (1, .28, 1, 1)],
        "N": [(0, 0, 0, 1), (0, 1, 1, 0), (1, 0, 1, 1)],
        "T": [(0, 1, 1, 1), (.5, 1, .5, 0)],
        "I": [(.5, 0, .5, 1), (.2, 1, .8, 1), (.2, 0, .8, 0)],
        "E": [(0, 0, 0, 1), (0, 1, 1, 1), (0, .5, .75, .5), (0, 0, 1, 0)],
        "S": [(1, 1, .3, 1), (.3, 1, 0, .76), (0, .76, .3, .5), (.3, .5, .7, .5), (.7, .5, 1, .24), (1, .24, .7, 0), (.7, 0, 0, 0)],
    }
    word, lw, lh, gap, stroke = "BOUNTIES", 0.078, 0.115, 0.034, 0.02
    left = -(len(word) * lw + (len(word) - 1) * gap) / 2
    for i, ch in enumerate(word):
        x0, y0 = left + i * (lw + gap), sign_y - lh / 2
        for (a, b, c, d) in letters[ch]:
            ax, ay, bx, by = x0 + a * lw, y0 + b * lh, x0 + c * lw, y0 + d * lh
            length = math.hypot(bx - ax, by - ay) + stroke
            m.box("letter", (ax + bx) / 2, (ay + by) / 2, -0.25, length, stroke, 0.02, "Gold", rz=math.degrees(math.atan2(by - ay, bx - ax)))

    # --- notices pinned to the board
    notes = [  # x, y, w, h, tilt, has red seal
        (-0.62, 1.62, 0.34, 0.44, 4, True), (-0.18, 1.4, 0.3, 0.38, -5, False),
        (0.56, 1.68, 0.34, 0.42, -3, True), (0.62, 1.2, 0.3, 0.34, 6, False), (-0.6, 1.08, 0.3, 0.36, -4, False),
    ]
    for i, (x, y, w, h, tilt, seal) in enumerate(notes):
        m.box("note%d" % i, x, y, -0.052 - i * 0.001, w, h, 0.012, "Parchment", rz=tilt)
        for line in range(4):
            lw = w * (0.7 - 0.12 * (line % 2))
            ly = y + h * 0.28 - line * h * 0.17
            m.box("noteText", x, ly, -0.06 - i * 0.001, lw, 0.016, 0.004, "Ink", rz=tilt)
        m.sph("nail", x, y + h / 2 - 0.035, -0.066 - i * 0.001, 0.035, 0.035, 0.03, "Iron")
        if seal:
            m.sph("seal", x + w * 0.28, y - h * 0.36, -0.062 - i * 0.001, 0.07, 0.07, 0.02, "Red")

    # the big WANTED poster in the middle
    m.box("posterBorder", 0.0, 1.62, -0.055, 0.5, 0.66, 0.012, "Dark", rz=1)
    m.box("poster", 0.0, 1.62, -0.062, 0.45, 0.61, 0.012, "Parchment", rz=1)
    m.sph("posterSkull", 0.0, 1.72, -0.075, 0.2, 0.18, 0.02, "Bone")
    m.sph("posterEyeL", -0.04, 1.73, -0.087, 0.045, 0.055, 0.01, "Black")
    m.sph("posterEyeR", 0.04, 1.73, -0.087, 0.045, 0.055, 0.01, "Black")
    m.box("posterJaw", 0.0, 1.62, -0.08, 0.09, 0.05, 0.01, "Bone")
    m.box("posterBar", 0.0, 1.45, -0.075, 0.34, 0.07, 0.01, "Red")
    for line in range(2):
        m.box("posterText", 0.0, 1.36 - line * 0.05, -0.075, 0.3 - line * 0.1, 0.017, 0.004, "Ink")
    for sx in (-1, 1):
        m.sph("posterNail", sx * 0.19, 1.9, -0.075, 0.04, 0.04, 0.03, "Iron")

    # --- lantern hanging from an arm on the left post
    m.box("armL", -1.02, 2.2, -0.28, 0.06, 0.06, 0.5, "Iron")
    m.box("armBraceL", -1.02, 2.0, -0.2, 0.05, 0.05, 0.38, "Iron", rx=-35)
    m.box("chain", -1.02, 1.98, -0.5, 0.02, 0.2, 0.02, "Iron")
    m.box("lanternBase", -1.02, 1.84, -0.5, 0.2, 0.04, 0.2, "Iron")
    m.box("lanternGlow", -1.02, 1.95, -0.5, 0.14, 0.18, 0.14, "Glow")
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.box("lanternBar", -1.02 + sx * 0.08, 1.95, -0.5 + sz * 0.08, 0.025, 0.2, 0.025, "Iron")
    m.box("lanternTop", -1.02, 2.07, -0.5, 0.22, 0.03, 0.22, "Iron")
    m.cyl("lanternCap", -1.02, 2.12, -0.5, 0.12, 0.03, 0.12, "Iron")

    # --- a bell on a bracket on the right post
    m.box("armR", 1.02, 2.2, -0.26, 0.06, 0.06, 0.46, "Iron")
    m.box("armBraceR", 1.02, 2.0, -0.18, 0.05, 0.05, 0.36, "Iron", rx=-35)
    m.sph("bell", 1.02, 1.97, -0.46, 0.22, 0.24, 0.22, "Gold")
    m.cyl("bellLip", 1.02, 1.85, -0.46, 0.26, 0.018, 0.26, "Gold")
    m.sph("clapper", 1.02, 1.82, -0.46, 0.05, 0.05, 0.05, "Dark")
    m.box("bellRope", 1.02, 2.1, -0.46, 0.015, 0.1, 0.015, "Dark")

    # --- a small ledge in front with a coin purse, a candle and a quill pot
    m.box("ledge", 0, 0.82, -0.2, 1.9, 0.05, 0.3, "Planks")
    for sx in (-1, 1):
        m.box("ledgeBracket", sx * 0.7, 0.72, -0.12, 0.05, 0.16, 0.14, "Dark")
    m.sph("purse", -0.62, 0.92, -0.22, 0.2, 0.18, 0.2, "Leather")
    m.cyl("purseNeck", -0.62, 1.03, -0.22, 0.08, 0.025, 0.08, "Leather")
    m.sph("purseTie", -0.62, 1.05, -0.22, 0.05, 0.03, 0.05, "Gold")
    for i, (cx, cz) in enumerate(((-0.4, -0.2), (-0.37, -0.15), (-0.43, -0.14))):
        m.cyl("coin", cx, 0.86 + 0.012 * i, cz, 0.08, 0.008, 0.08, "Gold")
    m.cyl("candle", 0.55, 0.93, -0.2, 0.07, 0.07, 0.07, "Cream")
    m.sph("flame", 0.55, 1.03, -0.2, 0.05, 0.08, 0.05, "Glow")
    m.cyl("inkpot", 0.2, 0.89, -0.2, 0.1, 0.035, 0.1, "Dark")
    m.cyl("quill", 0.22, 0.99, -0.2, 0.012, 0.1, 0.012, "Cream", rz=-18)

    return m


if __name__ == "__main__":
    import sys, os
    from modelkit import contact_sheet, icon
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    mod = make()
    contact_sheet(mod, os.path.join(out, "board_sheet.png"), target=(0, 1.5, 0), dist=7.6)
    print(len(mod.parts), "parts")
