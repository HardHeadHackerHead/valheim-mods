"""
A quick walk test for a pack's own rooms (python walk_check.py [pack.json] [width]): in each room, with its mechanisms at their start, can a
player get from every doorway to every other one the solver takes as joined? (Its "inside" joins, or all its doorways when it has
neither "inside" nor mechanisms that join doorways; rooms whose mechanisms join doorways are left to the harness's gap check.)

Every part is its box (prefab_bounds.json, scaled, turned and moved, made axis-aligned again), or its boxes for a hollow prefab
(prefab_shapes.json: a ribcage you walk through). The floor plan is cut into 0.5 m
columns; a box's top is a place to stand where nothing else is in the column from just above it to 1.8 m up. From a place a player
can step to a neighbouring column's place at most 0.6 m up, jump to one at most 1.5 m up, or drop to one any height down. Mechanisms
that move by themselves (a cycle) are taken at their start. The harness's GapCheck is the full version
(jumps across gaps too); this one is for finding what blocks a way, fast. With a width (3: metres), only ways that wide count.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dungeon_parts import euler_matrix  # noqa: E402

CELL, HEAD, STEP, JUMP = 0.5, 1.8, 0.6, 1.5


SHAPES_FILE = os.path.join(HERE, "prefab_shapes.json")
SHAPES = json.load(open(SHAPES_FILE)) if os.path.exists(SHAPES_FILE) else {}   # hollow prefabs: their collider as boxes


def boxes_of(parts, frame, bounds):
    out = []
    for p in parts:
        b = bounds.get(p["prefab"])
        if b is None:
            continue
        sc = np.array(p.get("scale", [1, 1, 1]))
        R = euler_matrix(*p.get("euler", [0, 0, 0]))
        for lo, hi in SHAPES.get(p["prefab"], [b]):
            lo, hi = np.array(lo), np.array(hi)
            if np.allclose(lo, hi):
                continue
            pts = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]) * sc
            w = (R @ pts.T).T + np.array(p["at"])
            w = (frame[:3, :3] @ w.T).T + frame[:3, 3]
            out.append((w.min(0), w.max(0), p["prefab"]))
    return out


def frame(at, euler):
    m = np.eye(4)
    m[:3, :3] = euler_matrix(*euler)
    m[:3, 3] = at
    return m


def room_boxes(room, bounds, states=None, tag=False):
    """Every part's box in the room's frame, its mechanisms in these states (gates open, the rest at their start). tag: each box's
    mechanism id (None for the room's own) as a fourth item."""
    allb = [b + ((None,) if tag else ()) for b in boxes_of(room.get("parts", []), np.eye(4), bounds)]
    for m in room.get("mechs", []):
        st = (states or {}).get(m["id"], 1 if m.get("kind") in ("gate", "door", "breakable") else m.get("start", 0))   # gates open
        if m.get("kind") == "breakable" and st >= 1:
            continue                                            # (broken: its parts are gone)
        mf = frame(m["at"], m.get("euler", [0, 0, 0]))
        if m.get("poses"):
            pose = m["poses"][min(st, len(m["poses"]) - 1)]
            mf = mf @ frame(pose["at"], pose["euler"])
        allb += [b + ((m["id"],) if tag else ()) for b in boxes_of(m.get("parts", []), mf, bounds)]
    return allb


def standing_places(room, allb):
    """{(i, j): [heights]}: the tops a player can stand on in each 0.5 m column (nothing in the column from just above to 1.8 m up)."""
    size = np.array(room["size"])
    half = size / 2
    nx, nz = int(math.ceil(size[0] / CELL)), int(math.ceil(size[2] / CELL))
    cols = [[[] for _ in range(nz)] for _ in range(nx)]
    tops = [[[] for _ in range(nz)] for _ in range(nx)]
    for lo, hi, name in (b[:3] for b in allb):
        x0, x1 = max(0, int((lo[0] + half[0]) // CELL)), min(nx - 1, int((hi[0] + half[0]) / CELL - 1e-4))
        z0, z1 = max(0, int((lo[2] + half[2]) // CELL)), min(nz - 1, int((hi[2] + half[2]) / CELL - 1e-4))
        for i in range(x0, x1 + 1):
            for j in range(z0, z1 + 1):
                cols[i][j].append((lo[1], hi[1]))
                cx, cz = -half[0] + (i + 0.5) * CELL, -half[2] + (j + 0.5) * CELL
                if lo[0] <= cx <= hi[0] and lo[2] <= cz <= hi[2]:
                    tops[i][j].append((hi[1], name))
    spots = {}
    for i in range(nx):
        for j in range(nz):
            for y, name in set(tops[i][j]):
                if not any(a < y + HEAD and b > y + 0.05 for a, b in cols[i][j]):
                    spots.setdefault((i, j), []).append(y)
    return spots


def check_room(room, bounds, states=None, width=0.0):
    size = np.array(room["size"])
    half = size / 2
    allb = room_boxes(room, bounds, states)
    spots = standing_places(room, allb)
    if width > 0:
        # a way `width` wide: a place counts only if every column within width / 2 has a place to stand near its height (within
        # 1 m, and 0.75 m more a metre away: a stair's steps are 1 m up every 1.33 m)
        # (a column between two that have one counts too: where two steps meet, the higher step's edge leaves the column no place)
        r = int(round(width / 2 / CELL))
        wide = {}

        def near(c, y, d):
            return any(abs(y2 - y) <= 1.0 + 0.75 * CELL * d for y2 in spots.get(c, []))

        def standable(i, j, y, d):
            return near((i, j), y, d) or any(near((i - di, j - dj), y, d) and near((i + di, j + dj), y, d) for di, dj in ((1, 0), (0, 1)))
        for (i, j), ys in spots.items():
            keep = [y for y in ys if all(standable(i + a, j + b, y, math.hypot(a, b))
                                         for a in range(-r, r + 1) for b in range(-r, r + 1) if a * a + b * b <= r * r)]
            if keep:
                wide[(i, j)] = keep
        spots = wide

    def place(d):
        at, f = np.array(d["at"]), np.array(d["facing"])
        p = at + f * 1.5
        i, j = int((p[0] + half[0]) // CELL), int((p[2] + half[2]) // CELL)
        for di in range(0, 9):                           # (a wide way: the nearest wide place further in)
            ii, jj = int((p[0] + f[0] * di * CELL + half[0]) // CELL), int((p[2] + f[2] * di * CELL + half[2]) // CELL)
            cands = [y for y in spots.get((ii, jj), []) if y <= at[1] + (0.6 if not width or di == 0 else 2.1)]   # (a step or two up)
            if cands or not width:
                return (ii, jj, max(cands)) if cands else None
        return None

    def reach(start):
        seen, todo = {start}, [start]
        while todo:
            i, j, y = todo.pop()
            for di in (-2, -1, 0, 1, 2):                         # (2 columns: a hop over a step's edge)
                for dj in (-2, -1, 0, 1, 2):
                    if di == dj == 0:
                        continue
                    for y2 in spots.get((i + di, j + dj), []):
                        if y2 - y <= JUMP:
                            # nothing in the way at body height between (the higher end + 0.1 to + 1.7)
                            n = (i + di, j + dj, y2)
                            if n not in seen:
                                seen.add(n); todo.append(n)
        return seen

    doors = {d["id"]: d for d in room["doorways"]}
    joined = mech_joins = any(m.get("joins") for m in room.get("mechs", []))
    if mech_joins:
        return None
    pairs = [(j["a"], j["b"]) for j in room.get("inside", [])] or \
            [(a, b) for a in doors for b in doors if a < b]
    problems = []
    places = {k: place(d) for k, d in doors.items()}
    for k, p in places.items():
        if p is None:
            problems.append(f"nothing to stand on inside doorway {k}")
    reached = {k: reach(p) for k, p in places.items() if p is not None}
    for a, b in pairs:
        if a not in reached or b not in reached or a == b:
            continue
        if places[b] not in reached[a] or places[a] not in reached[b]:
            problems.append(f"{a} and {b}: no walk between them")
    return problems


def main():
    pack_file = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs", "shiftinghalls.json")
    width = float(sys.argv[2]) if len(sys.argv) > 2 else 0.0
    pack = json.load(open(pack_file))
    bounds = json.load(open(os.path.join(HERE, "prefab_bounds.json")))
    bad = 0
    for room in pack["rooms"]:
        if "parts" not in room or len(room.get("doorways", [])) < 1 or room["size"][2] == 0 or room["size"][0] == 0:
            continue
        res = check_room(room, bounds, width=width)
        if res is None:
            continue
        for p in res:
            print(f"{room['name']}: {p}")
            bad += 1
    print(f"walk check: {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
