"""
Can a player reach and aim at every lever and exit? (python lever_check.py [pack.json ...]). For each lever (and each way out) in our
rooms, with every part a box (prefab_bounds.json, as walk_check.py makes them; the room's mechanisms at their start, gates open):
  - its approach: a 2 x 2 m patch of floor in front of it (on the side it's pulled from: -z turned by its yaw; a lever's base is
    taken as 0.9 m deep) where a player can stand everywhere, nothing within 1.8 m above the floor (no bars, rails or blocks);
  - a clear aim: from some place a player stands within 2.5 m of it (on that side), a straight line from the eye (1.6 m up) to its hub
    or to the middle of its handle that passes through no part's box but its own (the hover ray takes the first thing it meets).
An exit has no side: any place round it will do, and it needs no approach patch. Exits 1 when any fails.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from walk_check import boxes_of, frame, room_boxes, standing_places, CELL  # noqa: E402

EYE, REACH, APPROACH, BASE = 1.6, 2.5, 2.0, 0.9


def hits(p, q, lo, hi):
    """Whether the segment p..q passes through the box lo..hi (slab test, the ends a hair inside excluded)."""
    d = q - p
    t0, t1 = 0.0, 1.0
    for a in range(3):
        if abs(d[a]) < 1e-9:
            if p[a] <= lo[a] or p[a] >= hi[a]:
                return False
            continue
        u, v = (lo[a] - p[a]) / d[a], (hi[a] - p[a]) / d[a]
        if u > v:
            u, v = v, u
        t0, t1 = max(t0, u), min(t1, v)
        if t0 >= t1:
            return False
    return t1 > 0.02 and t0 < 0.98


def check_room(room, bounds):
    problems = []
    targets = [m for m in room.get("mechs", []) if m.get("kind") in ("lever", "exit")]
    if not targets:
        return problems
    allb = room_boxes(room, bounds, tag=True)
    half = np.array(room["size"]) / 2
    for m in targets:
        f = frame(m["at"], m.get("euler", [0, 0, 0]))
        pivot = f[:3, 3]
        lever = m["kind"] == "lever"
        front = f[:3, :3] @ np.array([0.0, 0.0, -1.0])
        front[1] = 0
        front /= max(np.linalg.norm(front), 1e-9)
        own = [b for b in allb if b[3] == m["id"]]
        # where players stand, leaving its own handle out (its box is coarse: a tipped handle's box covers the floor in front)
        spots = standing_places(room, [b for b in allb if b[3] != m["id"]])
        lo = np.min([b[0] for b in own], 0) if own else pivot - 0.2
        hi = np.max([b[1] for b in own], 0) if own else pivot + 0.2
        aims = [pivot, (lo + hi) / 2]
        # the floor a player stands on to use it: the highest place below the pivot just in front of it (or, for an exit, round it)
        floor = None
        for a in ((BASE + 0.5,) if lever else (1.0, 1.5)):
            for t in ((front,) if lever else [np.array([math.cos(u), 0, math.sin(u)]) for u in np.arange(0, 6.28, 0.785)]):
                p = pivot + t * a
                ys = [y for y in spots.get((int((p[0] + half[0]) // CELL), int((p[2] + half[2]) // CELL)), []) if y <= pivot[1] + 0.3]
                if ys and (floor is None or max(ys) > floor):
                    floor = max(ys)
        if floor is None:
            problems.append(f"{m['id']}: no floor in front of it")
            continue
        others = [b for b in allb if b[3] != m["id"]]
        # the approach patch
        if lever:
            side = np.array([front[2], 0, -front[0]])
            bad, what = 0, set()
            for a in np.arange(BASE + 0.5, BASE + 0.5 + APPROACH, 0.5):     # (a column touching the base is taken by it)
                for b in np.arange(-0.75, 1.0, 0.5):
                    p = pivot + front * a + side * b
                    i, j = int((p[0] + half[0]) // CELL), int((p[2] + half[2]) // CELL)
                    if not any(abs(y - floor) <= 0.6 for y in spots.get((i, j), [])):
                        bad += 1
                        cx, cz = -half[0] + (i + 0.5) * CELL, -half[2] + (j + 0.5) * CELL
                        what |= {x[2] for x in others if x[0][0] <= cx + 0.25 and cx - 0.25 <= x[1][0] and x[0][2] <= cz + 0.25
                                 and cz - 0.25 <= x[1][2] and x[0][1] < floor + 1.8 and x[1][1] > floor + 0.05}
            if bad:
                problems.append(f"{m['id']}: {bad} of 16 places in the 2 m in front of it can't be stood on "
                                f"({', '.join(sorted(what)) or 'no floor'})")
        # a clear aim
        seen = tried = 0
        for (i, j), ys in spots.items():
            c = np.array([-half[0] + (i + 0.5) * CELL, 0, -half[2] + (j + 0.5) * CELL])
            off = (c - pivot)
            dist = math.hypot(off[0], off[2])
            if dist > REACH or (lever and off[0] * front[0] + off[2] * front[2] < BASE):
                continue
            for y in ys:
                if not (floor - 1.0 <= y <= floor + 1.0):
                    continue
                tried += 1
                eye = np.array([c[0], y + EYE, c[2]])
                if any(not any(hits(eye, t, b[0], b[1]) for b in others) for t in aims):
                    seen += 1
        if not seen:
            problems.append(f"{m['id']}: nothing in sight of it from the {tried} places a player stands within {REACH} m"
                            f"{' in front of it' if lever else ''}")
    return problems


def main():
    files = sys.argv[1:] or [os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs", n) for n in ("shiftinghalls.json", "testmechs.json")]
    bounds = json.load(open(os.path.join(HERE, "prefab_bounds.json")))
    bad = 0
    for pf in files:
        pack = json.load(open(pf))
        for room in pack["rooms"]:
            if "parts" not in room:
                continue
            for p in check_room(room, bounds):
                print(f"{pack.get('name', os.path.basename(pf))}: {room['name']}: {p}")
                bad += 1
    print(f"lever check: {bad} problems")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
