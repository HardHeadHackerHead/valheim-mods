"""
A campfire kitchen: the most cooking from the least, checked against the game's own rules.

    python campfire_kitchen.py [preview.png]        writes campfire_kitchen.json and campfire_kitchen_double.json

One fire pit carries two cooking spits side by side (four pieces of meat at once): a spit cooks while its fire check point (in its middle,
0.2 m above its base) is within the fire's burning area (a 0.45 m sphere 0.34 m above the pit, plus the game's 0.25 m allowance), so a
spit 0.42 m either side of the fire's middle is lit. Spits may not sink more than 0.2 m into each other (their 1 m blocking boxes overlap
0.16 m here), and a third spit would be too far from the fire. A crossed pair is refused by the game.

A small shed roof keeps the rain off (rain puts out a fire with nothing above it); it rises towards the back and is open on every side,
so the smoke slides up it and out (a fire smothered by its own smoke goes out). A chest at the side holds the raw meat and wood: with
FeedFromChests, pressing E on a spit or the fire with empty hands takes what it needs from the chest. The cooked meat comes off the spit
straight into your inventory with E.

The double kitchen is two of these side by side under one roof: eight pieces at once.
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir
from stability import analyze, report

P = Pieces()
SPIT_OFF = 0.42          # each spit's middle from the fire's middle (front and back)


def post(bp, x, z, top):
    y = top
    while y > 0.05:
        name = "wood_pole2" if y > 1.0 else "wood_pole"
        bp.place_snap(name, "top", (x, y, z))
        y -= 2.0 if name == "wood_pole2" else 1.0


def kitchen(fires):
    name = "Campfire kitchen" + (" (double)" if fires > 1 else "")
    bp = Blueprint(name, P, anchor="look", yaw=0)
    xs = [(-1.3 + 2.6 * k) if fires > 1 else 0.0 for k in range(fires)]
    for x in xs:
        bp.place("fire_pit", x, 0.0, y=0.0, ground=True)
        for dz in (-SPIT_OFF, SPIT_OFF):
            bp.place("piece_cookingstation", x, dz, y=0.0, ground=True)   # long side along x, facing you
    half = 1.0 + (1.3 if fires > 1 else 0.0) + 1.0                         # the roof's half width: a 2 m roof piece per metre pair
    edge_x = math.ceil(half / 2.0) * 2.0                                   # roof covers whole 2 m pieces
    LOW, ROWS = 2.3, 1
    xc_list = [x + 1.0 for x in range(int(-edge_x), int(edge_x), 2)]
    # the roof: one row of 26 degree pieces from the front (low edge, 2.3 m) rising 1 m towards the back
    for xc in xc_list:
        bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (xc, LOW, -1.1), yaw=bp.yaw_for("wood_roof", 0, 1))
    for x in (-edge_x, edge_x):
        post(bp, x, -1.1, LOW)
        post(bp, x, 0.9, LOW + 1.0)
    # the chest at the side, in easy reach of both spits and the fire
    bp.place("piece_chest_wood", -edge_x - 1.0, 0.0, y=0.0, ground=True, yaw=90)
    if fires > 1:
        bp.place("piece_chest_wood", edge_x + 1.0, 0.0, y=0.0, ground=True, yaw=90)
    return bp


ok = True
outs = []
for fires, file in ((1, "campfire_kitchen.json"), (2, "campfire_kitchen_double.json")):
    bp = kitchen(fires)
    print("==", bp.meta["name"])
    ok = report(analyze(bp.items, P)) and ok
    ok = bp.summary() and ok
    # the game's fire rule, measured: every spit's check point must be in a fire's burning area
    burns = [(i["x"] + 0.02, i["y"] + 0.34, i["z"]) for i in bp.items if i["p"] == "fire_pit"]   # FireBurn: 0.45 m sphere
    for i in bp.items:
        if i["p"] == "piece_cookingstation":
            check = (i["x"], i["y"] + 0.2, i["z"])                                                  # its FireCheckPoint
            d = min(math.dist(check, b) for b in burns)
            lit = d <= 0.45 + 0.25
            print(f"  spit at ({i['x']:.2f}, {i['z']:.2f}): {d:.2f} m from the fire's middle -> {'lit' if lit else 'NOT LIT'}")
            ok = ok and lit
    out = os.environ.get("BLUEPRINT_OUT_DIR", blueprints_dir())
    path = os.path.join(out, file)
    print(bp.save(path), "pieces ->", path)
    outs.append(bp)

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    for bp, png in zip(outs, pngs):
        render(bp.items, P, png, views=((25, 30), (-35, 30), (0, 89), (0, 10)), size=(700, 700))
        print("preview ->", png)
raise SystemExit(0 if ok else 1)
