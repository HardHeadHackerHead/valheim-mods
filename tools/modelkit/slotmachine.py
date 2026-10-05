"""Odin's Fortune: a carved-wood and gold slot machine with three reels, a lever, a coin slot and a payout tray."""
import math
from modelkit import Model

REEL_X = (-0.3, 0.0, 0.3)
REEL_Y, REEL_Z = 1.55, -0.12
APOTHEM = 0.19


def reel(m, name, x):
    m.begin_group(name, x, REEL_Y, REEL_Z)
    side = 2 * APOTHEM * math.tan(math.radians(22.5))
    m.cyl("core", 0, 0, 0, 0.34, 0.15, 0.34, "Coal", rz=90)
    for i in range(8):
        phi = i * 45
        a = math.radians(phi)
        m.quad("face%d" % i, 0, APOTHEM * math.sin(a), -APOTHEM * math.cos(a), 0.27, side * 1.01, "Sym%d" % i, rx=phi)
    for sx in (-1, 1):
        m.cyl("rim", sx * 0.145, 0, 0, 0.4, 0.014, 0.4, "Gold", rz=90)
        m.cyl("hub", sx * 0.16, 0, 0, 0.1, 0.012, 0.1, "Iron", rz=90)
    m.end_group()


def make():
    m = Model()
    m.scale = 0.82

    # --- plinth and the dark wood body
    m.box("plinth", 0, 0.07, 0, 1.22, 0.14, 0.96, "Stone")
    m.box("plinthEdge", 0, 0.15, 0, 1.14, 0.03, 0.88, "StoneDark")
    m.box("lower", 0, 0.58, 0, 1.0, 0.86, 0.8, "Dark")
    m.box("lowerFront", 0, 0.5, -0.405, 0.88, 0.5, 0.03, "Planks")
    for x in (-0.3, 0.0, 0.3):
        m.box("plankLine", x + 0.15, 0.5, -0.422, 0.012, 0.5, 0.004, "Dark")
    for y in (0.28, 0.72):
        m.box("lowerBand", 0, y, -0.41, 0.94, 0.05, 0.04, "Iron")
    for sx in (-1, 1):
        for y in (0.28, 0.72):
            m.sph("rivet", sx * 0.4, y, -0.435, 0.04, 0.04, 0.02, "Gold")

    # --- the payout tray: a dark opening with a brass lip
    m.box("trayOpening", 0, 0.36, -0.41, 0.62, 0.22, 0.03, "Coal")
    m.box("trayDoor", 0, 0.44, -0.425, 0.6, 0.06, 0.02, "Iron")
    m.box("trayFloor", 0, 0.255, -0.5, 0.66, 0.03, 0.26, "Gold", rx=-8)
    for sx in (-1, 1):
        m.box("trayLip", sx * 0.32, 0.29, -0.5, 0.03, 0.09, 0.26, "Gold", rx=-8)
    m.box("trayFront", 0, 0.27, -0.63, 0.66, 0.06, 0.03, "Gold", rx=-8)

    # --- the sloping control deck with buttons and the coin slot
    c, s = math.cos(math.radians(25)), math.sin(math.radians(25))
    cx, cy, cz = 0.0, 1.0, -0.47

    def deck(lx, ly, lz=0.0):
        return (cx + lx, cy + ly * c - lz * s, cz + ly * s + lz * c)

    m.box("deck", cx, cy, cz, 1.06, 0.46, 0.06, "Dark", rx=25)
    m.box("deckTrimLow", *deck(0, -0.235, -0.02), 1.08, 0.04, 0.07, "Gold", rx=25)
    m.box("deckTrimHigh", *deck(0, 0.235, -0.02), 1.08, 0.04, 0.07, "Gold", rx=25)
    m.box("deckPlate", *deck(0, 0.02, -0.035), 0.94, 0.36, 0.012, "Red", rx=25)
    for lx, mat in ((-0.3, "Red"), (0.0, "Gold"), (0.3, "Green")):
        m.cyl("button", *deck(lx, -0.1, -0.06), 0.13, 0.03, 0.13, mat, rx=-65)
        m.cyl("buttonRing", *deck(lx, -0.1, -0.045), 0.17, 0.012, 0.17, "Gold", rx=-65)
    m.box("slotBezel", *deck(0, 0.1, -0.045), 0.22, 0.09, 0.012, "Gold", rx=25)
    m.box("slotCut", *deck(0, 0.1, -0.055), 0.14, 0.022, 0.012, "Black", rx=25)
    m.box("slotGlow", *deck(0, 0.165, -0.045), 0.2, 0.014, 0.01, "Glow", rx=25)

    # --- the neck, then the reel cabinet with its window
    m.box("neck", 0, 1.26, -0.02, 1.0, 0.22, 0.7, "Dark")
    # the reel cabinet: solid above and below the window and at its sides, open in front of the reels
    m.box("upperTop", 0, 1.88, 0, 1.04, 0.3, 0.8, "Dark")
    m.box("upperSill", 0, 1.355, 0, 1.04, 0.1, 0.8, "Dark")
    for sx in (-1, 1):
        m.box("upperSide", sx * 0.49, 1.55, 0, 0.1, 0.3, 0.8, "Dark")
    m.box("upperBack", 0, 1.55, 0.17, 0.9, 0.34, 0.46, "Dark")
    m.box("windowBack", 0, REEL_Y, -0.055, 0.9, 0.34, 0.02, "Black")
    for i, x in enumerate(REEL_X):
        reel(m, "reel%d" % i, x)
    # frame around the window, and the line that marks the win
    m.box("frameTop", 0, 1.77, -0.405, 1.04, 0.12, 0.05, "Gold")
    m.box("frameBottom", 0, 1.33, -0.405, 1.04, 0.12, 0.05, "Gold")
    for sx in (-1, 1):
        m.box("frameSide", sx * 0.49, 1.55, -0.405, 0.1, 0.4, 0.05, "Gold")
    m.box("frameInner", 0, 1.77, -0.435, 0.88, 0.035, 0.02, "Dark")
    m.box("frameInnerB", 0, 1.33, -0.435, 0.88, 0.035, 0.02, "Dark")
    m.box("payline", 0, REEL_Y, -0.36, 0.9, 0.012, 0.006, "Red")
    for sx in (-1, 1):
        m.sph("lineMarker", sx * 0.45, REEL_Y, -0.43, 0.05, 0.05, 0.03, "Red")

    # --- the marquee: a sign with three emblems and a ring of lamps
    m.box("marquee", 0, 2.14, -0.04, 1.1, 0.42, 0.66, "Dark")
    m.box("signBoard", 0, 2.14, -0.372, 0.96, 0.32, 0.03, "Red")
    m.box("signTrimTop", 0, 2.31, -0.385, 0.98, 0.03, 0.03, "Gold")
    m.box("signTrimBottom", 0, 1.97, -0.385, 0.98, 0.03, 0.03, "Gold")
    m.quad("emblemL", -0.29, 2.14, -0.392, 0.24, 0.24, "Sym4")
    m.quad("emblemC", 0.0, 2.14, -0.392, 0.26, 0.26, "Sym5")
    m.quad("emblemR", 0.29, 2.14, -0.392, 0.24, 0.24, "Sym4")
    for sx in (-1, 1):
        m.box("signDivider", sx * 0.15, 2.14, -0.39, 0.012, 0.26, 0.01, "Gold")
        m.box("signDividerOuter", sx * 0.44, 2.14, -0.39, 0.012, 0.26, 0.01, "Gold")
    # lamps around the sign, alternating so they can chase each other
    lamps = [(-0.46 + k * 0.115, 2.355) for k in range(9)] + [(-0.46 + k * 0.115, 1.93) for k in range(9)] + [(-0.5, 2.05 + k * 0.1) for k in range(3)] + [(0.5, 2.05 + k * 0.1) for k in range(3)]
    for k, (x, y) in enumerate(lamps):
        m.sph("bulb%s" % ("A" if k % 2 == 0 else "B"), x, y, -0.4, 0.05, 0.05, 0.04, "Glow" if k % 2 == 0 else "GlowB")

    # --- the roof cap, with a raven keeping watch and a gold ball at each corner
    m.box("cap", 0, 2.38, -0.02, 1.2, 0.06, 0.74, "Gold")
    m.box("capTop", 0, 2.43, -0.02, 1.06, 0.05, 0.6, "Dark")
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.sph("capBall", sx * 0.56, 2.46, -0.02 + sz * 0.33, 0.1, 0.1, 0.1, "Gold")
    m.box("perch", 0, 2.48, -0.05, 0.3, 0.03, 0.14, "Gold")
    m.sph("ravenBody", 0, 2.6, -0.04, 0.2, 0.17, 0.3, "Black", rx=-18)
    m.sph("ravenHead", 0, 2.7, -0.17, 0.12, 0.12, 0.12, "Black")
    m.box("ravenBeak", 0, 2.69, -0.255, 0.035, 0.035, 0.09, "Gold", rx=12)
    m.sph("ravenEyeL", -0.04, 2.72, -0.225, 0.025, 0.025, 0.02, "Red")
    m.sph("ravenEyeR", 0.04, 2.72, -0.225, 0.025, 0.025, 0.02, "Red")
    m.box("ravenTail", 0, 2.55, 0.14, 0.1, 0.03, 0.2, "Black", rx=-25)
    for sx in (-1, 1):
        m.box("ravenWing", sx * 0.1, 2.61, -0.02, 0.05, 0.12, 0.26, "Coal", rz=sx * 10)

    # --- carved panels on the sides of the lower cabinet
    for sx in (-1, 1):
        m.box("sidePanel", sx * 0.505, 0.58, 0.0, 0.025, 0.56, 0.6, "Planks")
        m.box("sideTrimTop", sx * 0.515, 0.88, 0.0, 0.02, 0.025, 0.64, "Gold")
        m.box("sideTrimBottom", sx * 0.515, 0.28, 0.0, 0.02, 0.025, 0.64, "Gold")
        m.box("sideTrimFront", sx * 0.515, 0.58, -0.31, 0.02, 0.6, 0.025, "Gold")
        m.box("sideTrimBack", sx * 0.515, 0.58, 0.31, 0.02, 0.6, 0.025, "Gold")
        m.cyl("sideCoin", sx * 0.52, 0.58, 0.0, 0.3, 0.012, 0.3, "Gold", rz=90)
        m.cyl("sideCoinInner", sx * 0.53, 0.58, 0.0, 0.2, 0.012, 0.2, "Dark", rz=90)

    # --- gold corner strips and iron bands that tie the cabinet together
    for sx in (-1, 1):
        m.box("cornerStrip", sx * 0.515, 1.2, -0.405, 0.04, 2.1, 0.04, "Gold")
        m.box("sideBand", sx * 0.505, 1.0, 0.0, 0.03, 0.06, 0.82, "Iron")
        m.box("sideBandUpper", sx * 0.525, 1.66, 0.0, 0.03, 0.06, 0.82, "Iron")

    # --- the lever on the right side: a ball on an arm that swings towards you
    m.cyl("leverBoss", 0.56, 1.05, -0.08, 0.15, 0.04, 0.15, "Iron", rz=90)
    m.cyl("leverPlate", 0.53, 1.05, -0.08, 0.26, 0.012, 0.26, "Gold", rz=90)
    m.begin_group("lever", 0.62, 1.05, -0.08)
    m.cyl("armPivot", 0, 0, 0, 0.1, 0.04, 0.1, "Gold", rz=90)
    m.cyl("arm", 0, 0.26, 0, 0.045, 0.28, 0.045, "Iron")
    m.sph("knob", 0, 0.58, 0, 0.14, 0.14, 0.14, "Red")
    m.end_group()

    return m


if __name__ == "__main__":
    import sys, os
    from modelkit import contact_sheet
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    mod = make()
    contact_sheet(mod, os.path.join(out, "slot_sheet.png"), target=(0, 1.25, 0), dist=7.0, atlas="symbols.png")
    print(len(mod.parts), "parts")
