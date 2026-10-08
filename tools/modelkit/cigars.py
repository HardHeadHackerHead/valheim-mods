"""
Quad's Cigars: everything the mod draws with the model kit.

Three strains of tobacco grow in three biomes (Meadows, Black Forest, Plains), each a little different, as real tobacco varieties are:

  meadow  pale, light green leaves, pink flowers          (Connecticut shade leaf)
  forest  deep green, narrow, upright leaves, cream flowers, taller (Corojo / Maduro leaf)
  plains  big, broad yellow-green leaves, red flowers     (Habano leaf)

A real tobacco plant (Nicotiana tabacum): a stout, erect stem 1 to 2.5 m tall; large ovate to lance-shaped leaves with a pointed tip and a
prominent pale midrib, attached straight to the stem (no stalk), alternately round it and largest low down; and a terminal cluster of
narrow trumpet-shaped flowers (white, pink or red) with five flared lobes.

  plant(strain, stage, healthy)   'seedling', 'growing' or 'mature'
  leaf_item(strain, kind)         a "hand" of leaves tied at the stem ends: 'fresh', 'dried' or 'aged'
  seeds(strain)                   a little cloth pouch with seeds spilling out
  rack(), barrel(), table(), humidor()   the Drying Rack, Curing Barrel, Cigar Rolling Table and Humidor
  cigar(kind)                     two cigars of one type crossed (for the item's picture)

Groups the game shows or hides (see mods/CigarSmoking/Curer.cs):
  rack:    fresh (green hands, while leaves are drying), dry (tan hands, when they are done)
  barrel:  lidOn (shown while empty and while curing), contents (dark leaves heaped in the top, when it is done)
The front of every piece faces -z.
"""
import math
import numpy as np
from modelkit import Model, euler_matrix

# ---- the cigar types: body colour, band colour (also in mods/CigarSmoking/Types.cs)
TYPES = {
    "connecticut": ((0.72, 0.55, 0.32), (0.85, 0.15, 0.12)),
    "maduro": ((0.15, 0.09, 0.05), (0.10, 0.10, 0.10)),
    "habano": ((0.55, 0.27, 0.12), (0.10, 0.55, 0.22)),
    "corojo": ((0.42, 0.28, 0.17), (0.15, 0.30, 0.72)),
}

# ---- the tobacco strains
STRAINS = {
    "meadow": dict(leaf=(0.34, 0.55, 0.20), leaf2=(0.45, 0.66, 0.27), rib=(0.68, 0.80, 0.46), flower=(0.90, 0.52, 0.62),
                   dry=(0.80, 0.64, 0.30), aged=(0.46, 0.31, 0.14), height=1.1, count=18, length=0.60, ratio=0.48, droop=34, step=2.2),
    "forest": dict(leaf=(0.15, 0.34, 0.15), leaf2=(0.21, 0.43, 0.19), rib=(0.50, 0.64, 0.36), flower=(0.95, 0.93, 0.84),
                   dry=(0.56, 0.38, 0.20), aged=(0.25, 0.15, 0.08), height=1.35, count=21, length=0.58, ratio=0.38, droop=16, step=1.2),
    "plains": dict(leaf=(0.46, 0.58, 0.17), leaf2=(0.57, 0.69, 0.23), rib=(0.84, 0.88, 0.50), flower=(0.80, 0.20, 0.25),
                   dry=(0.64, 0.41, 0.18), aged=(0.36, 0.17, 0.08), height=1.2, count=16, length=0.70, ratio=0.56, droop=30, step=2.0),
}

PAL = {
    "LeafGreen": (0.26, 0.46, 0.14), "LeafLight": (0.42, 0.62, 0.20), "LeafSick": (0.58, 0.55, 0.20), "RibSick": (0.80, 0.78, 0.45),
    "LeafDry": (0.66, 0.50, 0.26), "LeafAged": (0.30, 0.18, 0.09), "Stalk": (0.40, 0.52, 0.22), "StalkDark": (0.30, 0.40, 0.17),
    "Pink": (0.88, 0.48, 0.58), "Cedar": (0.62, 0.33, 0.20), "CedarDark": (0.44, 0.22, 0.13),
    "Brass": (0.80, 0.62, 0.22), "Seed": (0.22, 0.14, 0.08), "Twine": (0.78, 0.66, 0.42), "Felt": (0.24, 0.40, 0.28),
    "Ash": (0.55, 0.55, 0.52), "Thatch": (0.60, 0.50, 0.28), "Calyx": (0.35, 0.50, 0.20),
}
for _k, (_body, _band) in TYPES.items():
    PAL["Body_" + _k], PAL["Band_" + _k] = _body, _band
for _s, _p in STRAINS.items():
    lighter = lambda c, f: tuple(min(1.0, v * f + 0.04) for v in c)
    PAL["Leaf_" + _s], PAL["LeafB_" + _s], PAL["Rib_" + _s], PAL["Flower_" + _s] = _p["leaf"], _p["leaf2"], _p["rib"], _p["flower"]
    PAL["Dry_" + _s], PAL["DryRib_" + _s] = _p["dry"], lighter(_p["dry"], 1.18)
    PAL["Aged_" + _s], PAL["AgedRib_" + _s] = _p["aged"], lighter(_p["aged"], 1.35)
PAL["LeafGreenRib"], PAL["LeafDryRib"], PAL["LeafAgedRib"] = (0.60, 0.74, 0.42), (0.80, 0.66, 0.40), (0.44, 0.30, 0.16)


def _dir(rx, ry, rz=0):
    """Where a shape's long axis (+z) points after the game's rotation."""
    return euler_matrix(rx, ry, rz) @ np.array([0.0, 0.0, 1.0])


def leaf(m, name, x, y, z, yaw, length, width, droop, mat, rib, thick=1.0):
    """
    A tobacco leaf growing from (x, y, z) out along `yaw`, its tip drooping by `droop` degrees; with its pale midrib and veins.
    thick: how pronounced the midrib ridge and the ruffles are (the mesh's height is thick x the leaf's length x 0.1).
    """
    h = length * thick * 1.5
    m._add(name, "Leaf", (x, y, z), (width, h, length), (droop, yaw, 0), mat)
    m._add(name + "Rib", "Rib", (x, y, z), (width, h, length), (droop, yaw, 0), rib)


def flower(m, base, direction, mat, size=1.0):
    """A trumpet flower: a narrow tube with five flared lobes at the mouth."""
    d = np.array(direction, float)
    d /= np.linalg.norm(d)
    base = np.array(base, float)
    length = 0.075 * size
    mid = base + d * length / 2
    rx = math.degrees(math.atan2(math.hypot(d[0], d[2]), d[1]))     # tilt of the tube from straight up
    ry = math.degrees(math.atan2(d[0], d[2]))
    # a cylinder stands along y; tilt it to d
    m.cyl("tube", mid[0], mid[1], mid[2], 0.016 * size, length / 2, 0.016 * size, "Calyx", rx=rx, ry=ry)
    mouth = base + d * length
    # lobes: five little flattened spheres round the mouth
    up = np.array([0, 1.0, 0]) if abs(d[1]) < 0.9 else np.array([1.0, 0, 0])
    a = np.cross(d, up); a /= np.linalg.norm(a)
    b = np.cross(d, a)
    for k in range(5):
        ang = k * 72
        off = (a * math.cos(math.radians(ang)) + b * math.sin(math.radians(ang))) * 0.019 * size
        c = mouth + off + d * 0.004
        m.sph("lobe", c[0], c[1], c[2], 0.03 * size, 0.012 * size, 0.03 * size, mat, rx=rx, ry=ry)
    m.sph("throat", mouth[0], mouth[1], mouth[2], 0.016 * size, 0.01 * size, 0.016 * size, "Seed", rx=rx, ry=ry)


def plant(strain="meadow", stage="mature", healthy=True):
    """
    The plant's growth stages. 'seedling': a rosette of small leaves, 'growing': a knee-high plant, 'mature': a tall stalk with big leaves
    and a head of trumpet flowers. healthy=False turns the leaves yellow and sickly (no sun, or no room).
    """
    p = STRAINS[strain]
    m = Model()
    a, b, rib = ("Leaf_" + strain, "LeafB_" + strain, "Rib_" + strain) if healthy else ("LeafSick", "LeafSick", "RibSick")
    if stage == "seedling":
        m.cyl("stalk", 0, 0.03, 0, 0.012, 0.03, 0.012, "Stalk")
        for i in range(7):
            leaf(m, "leaf", 0, 0.02 + (i % 2) * 0.02, 0, i * 137.5, 0.14 + (i % 3) * 0.015, 0.075, 38, a if i % 2 else b, rib)
        return m
    if stage == "growing":
        h = 0.5
        m.cyl("stalk", 0, h / 2, 0, 0.024, h / 2, 0.024, "Stalk")
        n = 11
        for i in range(n):
            y = 0.05 + i * 0.037
            size = (0.36 - abs(i - 3) * 0.014) * p["length"] / 0.6
            leaf(m, "leaf", 0, y, 0, i * 137.5, size, size * p["ratio"], 34 - i * 2, a if i % 2 else b, rib)
        return m
    h = p["height"]
    m.cyl("stalk", 0, h / 2, 0, 0.034, h / 2, 0.034, "Stalk")
    m.cyl("stalkBase", 0, 0.12, 0, 0.05, 0.12, 0.05, "StalkDark")
    n = p["count"]
    for i in range(n):
        y = 0.06 + i * (h * 0.84) / n
        size = p["length"] * (1.0 - abs(i - n * 0.28) / (n * 1.1))     # largest low down, small at the top
        leaf(m, "leaf", 0, y, 0, i * 137.5, size, size * p["ratio"], p["droop"] - i * p["step"], a if i % 2 else b, rib)
    # the flower head: stems fanning out and up from the top, each carrying a few trumpet flowers
    top = np.array([0, h - 0.02, 0])
    for k in range(6):
        ang = k * 60 + 15
        d = _dir(-48, ang)
        tip = top + d * 0.2
        m.cyl("stem", (top[0] + tip[0]) / 2, (top[1] + tip[1]) / 2, (top[2] + tip[2]) / 2, 0.01, 0.1, 0.01, "Stalk", rx=-48, ry=ang)
        for j in range(3):
            q = top + d * (0.10 + j * 0.05)
            flower(m, q, d * 0.5 + np.array([0, 1, 0]) * 0.6, "Flower_" + strain)
    flower(m, top + np.array([0, 0.02, 0]), (0, 1, 0), "Flower_" + strain, size=1.1)
    return m


def _hand(m, x, y, z, kind, strain, hang=False, yaw=0, length=0.30, count=6, fan=95):
    """A 'hand' of leaves tied together at their stem ends with twine; lying flat on the ground (items) or hanging (on the rack)."""
    mat = {"fresh": "Leaf_" + strain, "dried": "Dry_" + strain, "aged": "Aged_" + strain}[kind] if strain else {"fresh": "LeafGreen", "dried": "LeafDry", "aged": "LeafAged"}[kind]
    ribm = {"fresh": "Rib_" + strain, "dried": "DryRib_" + strain, "aged": "AgedRib_" + strain}[kind] if strain else {"fresh": "LeafGreenRib", "dried": "LeafDryRib", "aged": "LeafAgedRib"}[kind]
    thick = {"fresh": 1.0, "dried": 1.25, "aged": 1.35}[kind]          # dried leaves curl and ruffle
    ratio = {"fresh": 0.46, "dried": 0.36, "aged": 0.34}[kind]       # and shrink and narrow
    for i in range(count):
        a = yaw + (i - (count - 1) / 2) * fan / max(1, count - 1)
        if hang:
            leaf(m, "hangLeaf", x, y, z, a, length, length * ratio, 80 + (i % 2) * 4, mat, ribm, thick)
        else:
            leaf(m, "leaf", x, y + 0.006 * (i % 3), z, a, length, length * ratio, -16 + (i % 3) * 4, mat, ribm, thick)
    # the twine binding the stem ends
    if hang:
        m.cyl("twine", x, y - 0.015, z, 0.045, 0.022, 0.045, "Twine")
        m.cyl("knot", x, y + 0.018, z, 0.012, 0.03, 0.012, "Twine")
    else:
        d = _dir(0, yaw)
        m.cyl("twine", x + d[0] * 0.025, y + 0.012, z + d[2] * 0.025, 0.05, 0.022, 0.05, "Twine", rx=90, ry=yaw)


def leaf_item(strain="meadow", kind="fresh"):
    m = Model()
    _hand(m, 0, 0.03, -0.14, kind, strain, yaw=0, length=0.32, count=6)
    return m


def seeds(strain="meadow"):
    """A cloth pouch tied with a ribbon in the strain's flower colour, seeds spilling out."""
    m = Model()
    rng = np.random.default_rng(7)
    m.sph("pouch", 0, 0.035, 0.0, 0.11, 0.085, 0.09, "Twine")
    m.sph("pouchTop", 0, 0.085, 0.0, 0.05, 0.035, 0.05, "Twine")
    m.cyl("ribbon", 0, 0.072, 0, 0.056, 0.008, 0.056, "Flower_" + strain)
    m.box("bow", 0.012, 0.09, 0, 0.03, 0.012, 0.012, "Flower_" + strain)
    for i in range(26):
        r = 0.02 + 0.05 * math.sqrt(rng.random())
        ang = rng.random() * 360
        m.sph("seed", 0.03 + r * math.cos(math.radians(ang)), 0.008 + rng.random() * 0.006, -0.05 + r * math.sin(math.radians(ang)) * 0.8,
              0.011, 0.007, 0.011, "Seed")
    return m


def rack():
    """An A-frame drying rack with a little thatched roof, 1.3 m wide, about 0.6 m deep and 1.5 m tall, with rails to hang leaves from."""
    m = Model()
    W, D, H = 1.3, 0.62, 1.5
    tilt = math.degrees(math.atan2(D / 2 - 0.03, H))
    for x in (-W / 2 + 0.04, W / 2 - 0.04):
        for s in (-1, 1):
            m.cyl("pole", x, H / 2, s * (D / 4 - 0.015), 0.07, math.hypot(H, D / 2 - 0.03) / 2, 0.07, "Planks", rx=-s * tilt)
        m.box("foot", x, 0.03, 0, 0.09, 0.06, D, "Dark")
        m.box("brace", x, 0.55, 0, 0.05, 0.04, D * 0.62, "Dark")
        m.cyl("peg", x, 1.12, 0, 0.05, 0.05, 0.05, "Dark", rz=90)     # the pegs the rails rest on
        m.cyl("peg", x, 0.78, 0, 0.05, 0.05, 0.05, "Dark", rz=90)
        m.box("lash", x, H - 0.05, 0, 0.09, 0.07, 0.09, "Leather")
        for y in (1.12, 0.78, 0.55):
            m.cyl("rope", x, y, 0, 0.07, 0.01, 0.07, "Twine", rz=90)
    m.cyl("ridge", 0, H - 0.05, 0, 0.06, W / 2 + 0.06, 0.06, "Dark", rz=90)
    m.cyl("rail", 0, 1.12, 0, 0.045, W / 2, 0.045, "Planks", rz=90)
    m.cyl("rail", 0, 0.78, 0, 0.045, W / 2, 0.045, "Planks", rz=90)
    # the roof: two sloping thatch boards over the ridge to keep the rain off the leaves
    for s in (-1, 1):
        m.box("roof", 0, H - 0.03, s * 0.2, W + 0.24, 0.035, 0.46, "Thatch", rx=s * 38)
        m.box("roofEdge", 0, H - 0.015 - s * 0.01, s * 0.38, W + 0.26, 0.03, 0.04, "Dark", rx=s * 38)
    # hanging hands: green while they dry, tan when done (the game shows one group or the other)
    for name, kind in (("fresh", "fresh"), ("dry", "dried")):
        m.begin_group(name)
        for k, x in enumerate(np.linspace(-0.5, 0.5, 5)):
            _hand(m, x, 1.10, 0, kind, None, hang=True, yaw=k * 17, length=0.26, count=4, fan=70)
            _hand(m, x + 0.1, 0.76, 0, kind, None, hang=True, yaw=k * 29, length=0.23, count=4, fan=70)
        m.end_group()
    return m


def barrel():
    """A curing barrel 0.75 m across and 0.9 m tall with a loose lid and a tap; dark leaves heaped in the top when it holds some."""
    m = Model()
    R, H = 0.37, 0.88
    m.cyl("body", 0, H / 2, 0, R * 2, H / 2, R * 2, "Cedar")
    m.cyl("belly", 0, H / 2, 0, R * 2.1, H * 0.3, R * 2.1, "Cedar")      # the barrel's bulge
    for y in (0.12, 0.34, H - 0.34, H - 0.12):
        mid = 0.3 < y < H - 0.3
        m.cyl("hoop", 0, y, 0, R * (2.16 if mid else 2.04), 0.022, R * (2.16 if mid else 2.04), "Iron")
    for k in range(12):
        a = k * 30
        m.box("stave", R * 1.01 * math.sin(math.radians(a)), H / 2, R * 1.01 * math.cos(math.radians(a)), 0.012, H * 0.9, 0.02, "CedarDark", ry=a)
    m.cyl("rim", 0, H + 0.01, 0, R * 2.0, 0.015, R * 2.0, "CedarDark")
    # a brass tap low at the front, and a rope handle each side
    m.cyl("tap", 0, 0.2, -R - 0.05, 0.05, 0.06, 0.05, "Brass", rx=90)
    m.cyl("tapHandle", 0, 0.2, -R - 0.11, 0.07, 0.012, 0.012, "Brass", rz=90)
    for s in (-1, 1):
        m.cyl("handle", s * (R + 0.015), H - 0.14, 0, 0.03, 0.06, 0.03, "Twine", rz=90 * s)
    m.begin_group("contents")
    for k in range(9):
        a = k * 40
        d = _dir(-42, a)
        c = np.array([0, H + 0.0, 0]) + d * 0.03
        leaf(m, "heapLeaf", c[0], c[1], c[2], a, 0.36, 0.15, -50, "LeafAged", "LeafAgedRib", 1.1)
    m.end_group()
    m.begin_group("lidOn")
    m.cyl("lid", 0.02, H + 0.05, 0, R * 1.9, 0.025, R * 1.9, "Cedar", rx=4)
    m.box("lidBatten", 0.02, H + 0.085, 0, 0.5, 0.025, 0.07, "CedarDark")
    m.sph("lidKnob", 0.02, H + 0.115, 0, 0.07, 0.05, 0.07, "Brass")
    m.end_group()
    return m


def table(cigars=True):
    """The rolling table: 1.3 m wide, 0.7 m deep, 0.9 m tall, with a drawer, a felt rolling mat, a cutter and cigars on it."""
    m = Model()
    W, D, H = 1.3, 0.7, 0.9
    for x in (-W / 2 + 0.07, W / 2 - 0.07):
        for z in (-D / 2 + 0.07, D / 2 - 0.07):
            m.box("leg", x, H / 2 - 0.03, z, 0.09, H - 0.06, 0.09, "CedarDark")
    for k in range(5):
        z = -D / 2 + (k + 0.5) * D / 5
        m.box("topBoard", 0, H - 0.025, z, W + 0.04, 0.05, D / 5 - 0.008, "Cedar")
    for z in (-D / 2 + 0.07, D / 2 - 0.07):
        m.box("apron", 0, H - 0.1, z, W - 0.12, 0.08, 0.04, "CedarDark")
    # a drawer at the front with a brass knob
    m.box("drawer", 0.25, H - 0.115, -D / 2 + 0.045, 0.5, 0.1, 0.02, "Cedar")
    m.sph("knob", 0.25, H - 0.115, -D / 2 + 0.025, 0.035, 0.035, 0.03, "Brass")
    m.box("shelf", 0, 0.22, 0, W - 0.1, 0.035, D - 0.1, "Planks")
    # on the shelf: a hand of aged leaves and a lidded jar
    _hand(m, -0.32, 0.26, -0.08, "aged", None, yaw=-10, length=0.30, count=5)
    m.cyl("jar", 0.3, 0.30, 0.0, 0.2, 0.065, 0.2, "Iron")
    m.cyl("jarLid", 0.3, 0.375, 0.0, 0.21, 0.012, 0.21, "Dark")
    top = H
    m.box("mat", -0.12, top + 0.008, -0.02, 0.50, 0.016, 0.36, "Felt")
    m.box("matEdge", -0.12, top + 0.018, -0.2, 0.50, 0.012, 0.02, "Twine")
    _hand(m, -0.30, top + 0.025, 0.0, "fresh", None, yaw=-8, length=0.28, count=4, fan=50)
    m.box("board", 0.36, top + 0.012, 0.0, 0.34, 0.024, 0.46, "Planks")           # cutting board
    m.box("blade", 0.36, top + 0.035, -0.12, 0.2, 0.012, 0.045, "Iron", ry=8)      # the cutter
    m.box("bladeHandle", 0.48, top + 0.04, -0.13, 0.08, 0.025, 0.035, "Leather", ry=8)
    if cigars:
        for k, y in enumerate((0.0, 0.02, 0.04)):
            m.cyl("cigar", -0.12 + 0.02 * k, top + 0.045 + y, 0.2, 0.026, 0.09, 0.026, "Body_connecticut", rz=90, ry=4 * k)
            m.cyl("cigarBand", -0.12 + 0.02 * k + 0.03, top + 0.045 + y, 0.2, 0.029, 0.012, 0.029, "Band_connecticut", rz=90, ry=4 * k)
    return m


def humidor():
    """The humidor: a cedar cabinet 0.55 m wide, 0.4 deep and 0.7 tall on short feet, with a brass latch, hinges and a hygrometer."""
    m = Model()
    W, D, H = 0.56, 0.42, 0.62
    for x in (-W / 2 + 0.04, W / 2 - 0.04):
        for z in (-D / 2 + 0.04, D / 2 - 0.04):
            m.box("foot", x, 0.05, z, 0.07, 0.1, 0.07, "CedarDark")
    m.box("body", 0, 0.1 + H / 2 - 0.03, 0, W, H - 0.06, D, "Cedar")
    m.box("plinth", 0, 0.12, 0, W + 0.03, 0.04, D + 0.03, "CedarDark")
    m.box("lid", 0, 0.1 + H - 0.01, 0, W + 0.04, 0.05, D + 0.04, "CedarDark")
    m.box("lidTop", 0, 0.1 + H + 0.025, 0, W - 0.06, 0.02, D - 0.06, "Cedar")
    # the door: panelled, with a brass latch and hygrometer
    m.box("door", 0, 0.1 + H / 2 - 0.03, -D / 2 - 0.006, W - 0.1, H - 0.14, 0.02, "CedarDark")
    m.box("panel", 0, 0.1 + H / 2 - 0.03, -D / 2 - 0.02, W - 0.2, H - 0.26, 0.012, "Cedar")
    m.sph("latch", 0.18, 0.1 + H / 2 - 0.03, -D / 2 - 0.035, 0.045, 0.045, 0.02, "Brass")
    m.cyl("hygrometer", -0.14, 0.1 + H - 0.2, -D / 2 - 0.035, 0.1, 0.012, 0.1, "Brass", rx=90)
    m.cyl("hygroFace", -0.14, 0.1 + H - 0.2, -D / 2 - 0.045, 0.08, 0.006, 0.08, "Cream", rx=90)
    for y in (0.1 + 0.12, 0.1 + H - 0.2):
        m.box("hinge", W / 2 - 0.04, y, -D / 2 - 0.02, 0.025, 0.07, 0.015, "Brass")
    # a cigar resting on the lid
    m.cyl("cigarOnLid", -0.1, 0.1 + H + 0.052, 0.02, 0.026, 0.08, 0.026, "Body_maduro", rz=90, ry=12)
    m.cyl("cigarBandOnLid", -0.07, 0.1 + H + 0.052, 0.017, 0.029, 0.011, 0.029, "Band_maduro", rz=90, ry=12)
    return m


def cigar(kind):
    """Two cigars of one type crossed over each other, for the item's picture (the game builds the real one in Cigar.cs)."""
    m = Model()
    for ang, y in ((-14, 0.0), (16, 0.022)):
        m.begin_group("c", 0, y, 0, 0, ang, 0)
        m.cyl("body", 0, 0, 0, 0.034, 0.1, 0.034, "Body_" + kind, rz=90)
        m.cyl("band", 0.05, 0, 0, 0.038, 0.011, 0.038, "Band_" + kind, rz=90)
        m.sph("tip", -0.1, 0, 0, 0.034, 0.034, 0.034, "Ash")
        m.end_group()
    return m


if __name__ == "__main__":
    import sys
    from modelkit import contact_sheet
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    jobs = [
        ("mature_meadow", plant("meadow"), dict(target=(0, 0.6, 0), dist=4.4)),
        ("mature_forest", plant("forest"), dict(target=(0, 0.68, 0), dist=4.8)),
        ("mature_plains", plant("plains"), dict(target=(0, 0.62, 0), dist=4.6)),
        ("growing_meadow", plant("meadow", "growing"), dict(target=(0, 0.25, 0), dist=2.4)),
        ("seedling_forest", plant("forest", "seedling"), dict(target=(0, 0.07, 0), dist=1.0)),
        ("rack", rack(), dict(target=(0, 0.75, 0), dist=5.0)),
        ("barrel", barrel(), dict(target=(0, 0.5, 0), dist=3.6)),
        ("table", table(), dict(target=(0, 0.5, 0), dist=5.0)),
        ("humidor", humidor(), dict(target=(0, 0.4, 0), dist=2.8)),
    ]
    for strain in STRAINS:
        for kind in ("fresh", "dried", "aged"):
            jobs.append(("leaf_%s_%s" % (strain, kind), leaf_item(strain, kind), dict(target=(0, 0.03, -0.1), dist=1.0)))
        jobs.append(("seeds_" + strain, seeds(strain), dict(target=(0, 0.02, -0.02), dist=0.6)))
    for name, mod, kw in jobs:
        contact_sheet(mod, "%s/%s.png" % (out, name), size=(520, 520), palette=PAL, **kw)
        print(name, len(mod.parts), "parts")
