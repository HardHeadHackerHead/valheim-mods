"""
Parkour course: a training ground for the Parkour mod's four moves (wall run, wall jump, wall climb, ledge grab and mantle), each on its
own and then all in one run. About 29 x 26 m on level ground; wood, core wood and stone only (workbench and stonecutter).

    python parkour_course.py [preview.png] [views.png]        writes parkour_course.json

The front is -z (the player stands at -z looking +z). The loop, clockwise from the start (front left):
  - START: a sign on a post at (-10.5, -12.5), between the two lane run-ins. Stand in front of it, facing +z.
  - WALL-RUN LANES (left side, along z): three straight walls 12 m long, A (x -14) and B (x -11) 4 m tall, C (x -6) 6 m tall. Lane 1
    (between A and B, 3 m apart) and lane 2 (between B and C, 5 m apart) each have a 1 m stone walkway with a gap in it beside the walls:
    6 m in lane 1 (more than a sprint jump, less than a skill-0 wall run), 8 m in lane 2 (a long wall run). Falling in costs nothing: the
    gap floor is the ground, 1 m down; walk out of the lane's end and try again.
  - WALL-JUMP CHIMNEY (back right): a freestanding wall 8 m tall (x 6.3) facing the 8 m front face of the step tower (x 10), 3.7 m apart
    and 4 m wide, open at both ends (go in from the back, z +10). Kick between them to the top and mantle onto the tower's top platform.
  - STEP TOWER and CLIMB WALLS (right side, along z): five 4 x 4 m bays whose front faces (x 10, facing the middle) are 2, 3, 4, 6 and
    8 m tall, each with a floor on top. The 2, 3 and 4 m faces are the climb walls (skill 0 runs 2 m up, skill 100 runs 4 m). The bays
    also step down from the chimney top 8 -> 6 -> 4 -> 3 -> 2 -> ground, no drop over about 2 m: the way down. The high edges nobody uses
    (the back and the outer end of the 6 and 8 m bays) have a 1 m rail.
  - LEDGES and FINISH (front strip, going -x): log blocks whose top edges are at chest (1.2 m), head (1.7 m) and just above head (2.2 m:
    the 4 x 4 m finish platform, with torches). Their +x faces meet the runner; their -x sides can be grabbed too.
A full run: lane 1 or 2 -> along the back -> up the chimney -> down the steps -> chest ledge -> head ledge -> finish.

Build a workbench and a stonecutter near the middle (about x -2, z 0, each under a roof): everything is within 20 m of there.
"""
import math, os, sys
from blueprint import Pieces, Blueprint, blueprints_dir, world_box
from stability import analyze, report

P = Pieces()
bp = Blueprint("Parkour course", P, anchor="look", yaw=0)

WALL, HALF, FLOOR = "woodwall", "wood_wall_half", "wood_floor"
LOG4, BLOCK, TORCH = "wood_wall_log_4x0.5", "stone_floor_2x2", "piece_groundtorch_wood"

# ---------------------------------------------------------------- the numbers to tune after trying it in game
LANE_WALLS = ((-14.0, 4), (-11.0, 4), (-6.0, 6))          # (x, height) of walls A, B, C
LANE_Z = (-7.0, 5.0)                                     # the lane walls run from z -7 to +5 (12 m)
LANES = [  # walkway centre x, run-in z range, landing z range (the gap is between them)
    dict(x=-12.5, run=(-10.0, -4.0), land=(2.0, 6.0)),    # lane 1: 6 m gap
    dict(x=-8.5, run=(-11.0, -5.0), land=(3.0, 7.0)),     # lane 2: 8 m gap
]
TOWER_X = (10.0, 14.0)                                   # front face (climb walls, chimney back wall) and back of the step tower
BAYS = [(-10.0, 2), (-6.0, 3), (-2.0, 4), (2.0, 6), (6.0, 8)]   # (z where the 4 m bay starts, height of its face and floor)
CHIMNEY_X = 6.3                                          # the freestanding chimney wall: 3.7 m in front of the tower face
LEDGE_Z = (-15.0, -11.0)                                 # the front strip
LEDGES = [(7.0, 2, 4), (2.0, 3, 2), (-5.0, 4, 4)]        # (x of the block's -x side, log rows (0.5 m each), depth along x)


def wall_line(a, b, fixed, along_z, height, base=0.0):
    """Wood walls on the line from a to b (every 2 m), `height` metres tall (2 m courses, then a half wall for an odd metre)."""
    for k in range(int(round((b - a) / 2.0))):
        c = a + 1.0 + 2.0 * k
        x, z = (fixed, c) if along_z else (c, fixed)
        y = base
        while height - (y - base) >= 2.0 - 1e-6:
            bp.raw(WALL, x, y + 1.0, z, yaw=90 if along_z else 0)
            y += 2.0
        if height - (y - base) >= 1.0 - 1e-6:
            bp.raw(HALF, x, y + 0.5, z, yaw=90 if along_z else 0)


def floors(x0, x1, z0, z1, top):
    """2 x 2 floors over a rectangle; `top` is the height their snap plane sits at (the top of the walls under them)."""
    for i in range(int(round((x1 - x0) / 2.0))):
        for j in range(int(round((z1 - z0) / 2.0))):
            bp.raw(FLOOR, x0 + 1.0 + 2.0 * i, top, z0 + 1.0 + 2.0 * j)


def torch(x, z, y=0.0):
    bp.place(TORCH, x, z, y=y)


# ---------------------------------------------------------------- wall-run lanes
for x, h in LANE_WALLS:
    wall_line(LANE_Z[0], LANE_Z[1], x, True, h)
for lane in LANES:
    for z0, z1 in (lane["run"], lane["land"]):
        for k in range(int(round((z1 - z0) / 2.0))):
            bp.place(BLOCK, lane["x"], z0 + 1.0 + 2.0 * k, y=0.0)

# ---------------------------------------------------------------- step tower: climb walls 2/3/4 m, then 6 and 8 m bays (chimney top)
fx, bx = TOWER_X
for n, (z0, h) in enumerate(BAYS):
    wall_line(z0, z0 + 4.0, fx, True, h)                 # front face: the climb wall (and, for the 8 m bay, the chimney's back wall)
    wall_line(z0, z0 + 4.0, bx, True, h)                 # back
    side = max(h, BAYS[n - 1][1]) if n else h            # a side shared with the bay before is built once, as tall as the taller one
    wall_line(fx, bx, z0, False, side)
    floors(fx, bx, z0, z0 + 4.0, h)
wall_line(fx, bx, BAYS[-1][0] + 4.0, False, BAYS[-1][1])  # the outer end of the 8 m bay
# rails (a half wall on the wall top) on the high edges that are not part of the route
RAILS = [(BAYS[3][0], fx), (BAYS[3][0], bx), (BAYS[4][0], bx)]  # (bay start z, x of the edge)
for z0, x in RAILS:
    h = dict(BAYS)[z0]
    for c in (z0 + 1.0, z0 + 3.0):
        bp.raw(HALF, x, h + 0.5, c, yaw=90)
for c in (fx + 1.0, fx + 3.0):
    bp.raw(HALF, c, BAYS[4][1] + 0.5, BAYS[4][0] + 4.0, yaw=0)

# ---------------------------------------------------------------- the chimney: a freestanding 8 m wall facing the 8 m bay
wall_line(BAYS[4][0], BAYS[4][0] + 4.0, CHIMNEY_X, True, BAYS[4][1])

# ---------------------------------------------------------------- ledges and the finish platform (log blocks, floors on top)
LOG_HALF = 0.275                                         # half a log's thickness: logs sit flush with the floor edges
for x0, rows, depth in LEDGES:
    xs = [x0 + LOG_HALF, x0 + depth - LOG_HALF] + ([x0 + depth / 2.0] if depth > 2 else [])
    for x in xs:
        for r in range(rows):
            bp.raw(LOG4, x, 0.25 + 0.5 * r, (LEDGE_Z[0] + LEDGE_Z[1]) / 2.0, yaw=90)
    floors(x0, x0 + depth, LEDGE_Z[0], LEDGE_Z[1], 0.5 * rows + 0.12)   # floor bottom on the logs: top edge at 0.5 * rows + 0.2

# ---------------------------------------------------------------- start sign, torches
bp.raw("wood_pole2", -10.5, 1.0, -12.5)
bp.raw("sign", -10.5, 1.6, -12.75)                       # on the post's front (-z) face; write "Start" on it
for x, z in ((-9.6, -12.5), (-10.5, 8.0), (5.6, 10.8), (9.4, -6.0), (9.4, 2.0)):
    torch(x, z)
torch(bx - 0.5, BAYS[4][0] + 3.3, BAYS[4][1] + 0.08)     # chimney top
torch(bx - 0.5, BAYS[2][0] + 3.5, BAYS[2][1] + 0.08)     # 4 m bay
for x0, rows, depth in LEDGES:
    top = 0.5 * rows + 0.2
    torch(x0 + 0.3, LEDGE_Z[0] + 0.3, top)
    if depth > 2:
        torch(x0 + 0.3, LEDGE_Z[1] - 0.3, top)


# ---------------------------------------------------------------- safety: how far you drop off each platform edge
def drops():
    """For every platform (floor or stone block) edge: the drop to whatever is below just past it, unless a wall or rail stands there.
    Returns [(drop, piece, side, top)] sorted, highest first."""
    cat = P.by_name
    boxes = [world_box(i, cat[i["p"]]) for i in bp.items]
    surf = [k for k, i in enumerate(bp.items) if i["p"] in (FLOOR, BLOCK)]
    solid = [k for k, i in enumerate(bp.items) if i["p"] not in (FLOOR, BLOCK, TORCH, "sign")]
    out = []
    for k in surf:
        (x0, _, z0), (x1, top, z1) = boxes[k]
        for side, (dx, dz) in (("-x", (-1, 0)), ("+x", (1, 0)), ("-z", (0, -1)), ("+z", (0, 1))):
            worst = 0.0
            for t in (0.2, 0.5, 0.8):
                ex = x0 + (x1 - x0) * t if dx == 0 else (x0 if dx < 0 else x1)
                ez = z0 + (z1 - z0) * t if dz == 0 else (z0 if dz < 0 else z1)
                blocked = any(boxes[s][0][0] <= ex + dx * o <= boxes[s][1][0] and boxes[s][0][2] <= ez + dz * o <= boxes[s][1][2]
                              and boxes[s][0][1] <= top + 0.5 <= boxes[s][1][1] for s in solid for o in (0.05, 0.15, 0.25, 0.35))
                if blocked:
                    continue
                px, pz = ex + dx * 0.4, ez + dz * 0.4
                below = [boxes[s][1][1] for s in surf if boxes[s][0][0] <= px <= boxes[s][1][0] and boxes[s][0][2] <= pz <= boxes[s][1][2]]
                worst = max(worst, top - max(below + [0.0]))
            if worst > 0.3:
                out.append((round(worst, 2), k, side, round(top, 2)))
    return sorted(out, reverse=True)


ok = report(analyze(bp.items, P))
ok = bp.summary() and ok
print("Platform tops:", ", ".join(f"{t:.2f}" for t in sorted({round(world_box(i, P.by_name[i['p']])[1][1], 2)
                                                                for i in bp.items if i["p"] in (FLOOR, BLOCK)})))
chimney_top = [k for k, i in enumerate(bp.items) if i["p"] == FLOOR and abs(i["y"] - BAYS[4][1]) < 0.01 and i["x"] < fx + 2.0]
print("Drops over 2.5 m off platform edges (fall damage starts above 4 m):")
for d, k, side, top in drops():
    if d <= 2.5:
        continue
    i = bp.items[k]
    why = "the chimney mouth: where you arrive from the shaft" if (k in chimney_top and side == "-x") else ""
    if d > 4.3 and not why:
        ok = False
        why = "UNSAFE"
    print(f"  {d:5.2f} m  #{k} {i['p']} at ({i['x']}, {i['z']}) top {top}, {side} edge  {why}")
out = os.environ.get("BLUEPRINT_OUT_DIR", blueprints_dir())
path = os.path.join(out, "parkour_course.json")
print(bp.save(path), "pieces ->", path)
xs = [b for i in bp.items for b in (world_box(i, P.by_name[i["p"]])[0][0], world_box(i, P.by_name[i["p"]])[1][0])]
zs = [b for i in bp.items for b in (world_box(i, P.by_name[i["p"]])[0][2], world_box(i, P.by_name[i["p"]])[1][2])]
print(f"Footprint: x {min(xs):.1f} .. {max(xs):.1f}, z {min(zs):.1f} .. {max(zs):.1f}  ({max(xs) - min(xs):.1f} x {max(zs) - min(zs):.1f} m)")

pngs = [a for a in sys.argv[1:] if a.endswith(".png")]
if pngs:
    from preview import render
    render(bp.items, P, pngs[0], views=((25, 30), (-35, 30), (0, 89), (0, 8)), size=(720, 720))
    print("preview ->", pngs[0])
    if len(pngs) > 1:
        render(bp.items, P, pngs[1], views=((-30, 28), (150, 28)), size=(1100, 800))
        print("preview ->", pngs[1])
raise SystemExit(0 if ok else 1)
