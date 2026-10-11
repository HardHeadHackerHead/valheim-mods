"""
Can a fight's creatures reach the players? (python creature_check.py [pack.json ...]; exits 1 on any island.)

Valheim's creatures walk only where the game's navmesh goes (Pathfinding: built at runtime round them, one per agent type), and its
step height is 0.3 m for humanoid agents (skeletons, draugr, greydwarfs, goblins, fenrings) and 0.6 m for troll-sized ones; they never
jump down a ledge or up a step higher than that. The first Clockwork Warden spawned on a dais of two 0.6 m tiers: the dais top was an
island in the navmesh, and the boss walked to its edge and stood there.

So, for every encounter in our rooms: from each of its spawn points (a boss's or an elite's first, then every wave's), a creature's walk
over the room's floors must reach the floor of the doorway the fight is entered from (its "reachedFrom", else the room's first doorway).
The walk: the room's parts and mechanisms as boxes (walk_check's, prefab_bounds.json), in 0.5 m columns; a place to stand is a top with
1.8 m of room above it; a creature steps to a neighbouring column's place at most CLIMB (0.3 m) up or down, and only where every
column next to it has a place within CLIMB too (its body, about 0.5 m round: not along a knife edge). No jumps, no drops.
Checked with the mechanisms at their start, with every state the fight sets (its boss's phases and intro: hammers down, blocks risen),
and with every gate, door and platform at its state 1 (a siege's screen held open). A boss's point must pass in each of them (it is
there from the start); a wave's point in one at least (its creatures come while the fight holds the way open).
And a boss's lure targets (weakTo that breaks its guard) are open floor round them and reachable from the way in (check_targets).
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from walk_check import room_boxes, standing_places, CELL  # noqa: E402

CLIMB = 0.3          # the navmesh's step height for humanoid agents (the smallest of the walking agents')


def fight_states(room, enc):
    """The states the fight sets (a boss's intro beats, phases, shields' breaks, abilities, death: every "set:n"), applied over the start."""
    states = {}
    boss = enc.get("boss") or {}
    wires = []
    for ph in boss.get("phases", []):
        wires += ph.get("wires", []) + (ph.get("shield") or {}).get("breakWires", [])
    for t in ("intro", "death"):
        for beat in (boss.get(t) or {}).get("beats", []):
            wires += beat.get("wires", [])
    for a in boss.get("abilities", []):
        wires += a.get("wires", [])
    for w in wires:
        d = w.get("do", "next")
        if d.startswith("set:"):
            states[w["target"]] = int(d[4:])
    return states


def check_fight(room, enc, bounds):
    size = np.array(room["size"])
    half = size / 2
    doors = {d["id"]: d for d in room["doorways"]}
    door = doors.get(enc.get("reachedFrom")) or (room["doorways"][0] if room["doorways"] else None)
    if door is None:
        return []
    problems = []
    configs = [("at the start", None)]
    fs = fight_states(room, enc)
    if fs:
        configs.append(("in the fight's states", fs))
    opened = {m["id"]: 1 for m in room.get("mechs", []) if m.get("kind") in ("gate", "door", "platform") and len(m.get("poses", [])) > 1}
    if opened:
        configs.append(("with its gates and platforms at state 1", opened))
    pts = [np.array(p, float) for p in enc.get("points", [])]
    failed = {k: [] for k in range(len(pts))}           # per point: what each configuration found
    for label, states in configs:
        allb = room_boxes(room, bounds, states)
        spots = standing_places(room, allb)

        def cell(p):
            return int((p[0] + half[0]) // CELL), int((p[2] + half[2]) // CELL)

        def body_ok(i, j, y):
            # every column next to it has a place within CLIMB of y: room for a creature's body (about 0.5 m round)
            return all(any(abs(y2 - y) <= CLIMB for y2 in spots.get((i + a, j + b), [])) for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1)))

        def start_of(p, below=0.35):
            i, j = cell(p)
            cands = [y for y in spots.get((i, j), []) if abs(y - p[1]) <= below]
            return (i, j, min(cands, key=lambda y: abs(y - p[1]))) if cands else None

        # the players' side: the floor 1 to 3 m in from the doorway
        goal = None
        at, f = np.array(door["at"]), np.array(door["facing"])
        for dist in (1.5, 1.0, 2.0, 2.5, 3.0):
            g = start_of(at + f * dist, below=0.6)
            if g:
                goal = g
                break
        if goal is None:
            problems.append(f"{enc['id']}: no floor inside doorway {door['id']} {label}")
            continue

        def walk(start):
            seen, todo, edge = {start}, [start], (99.0, None)   # (the lowest edge it couldn't take)
            while todo:
                i, j, y = todo.pop()
                for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                    for y2 in spots.get((i + di, j + dj), []):
                        n = (i + di, j + dj, y2)
                        if n in seen:
                            continue
                        dy = abs(y2 - y)
                        if dy <= CLIMB and body_ok(*n):
                            seen.add(n)
                            todo.append(n)
                # the lowest edge out of here it couldn't take (looked for two columns out: a body's width from its edge)
                for di in (-2, -1, 0, 1, 2):
                    for dj in (-2, -1, 0, 1, 2):
                        for y2 in spots.get((i + di, j + dj), []):
                            dy = abs(y2 - y)
                            if CLIMB < dy < edge[0]:
                                edge = (dy, (i + di, j + dj, min(y, y2)))
            return seen, edge

        if label == "at the start":
            problems += check_targets(room, enc, spots, half, goal, walk, start_of)

        for k, p in enumerate(pts):
            what = ("boss" if enc.get("boss") and k == 0 else "spawn") + f" point {np.round(p, 1).tolist()}"
            s = start_of(p)
            if s is None:
                failed[k].append(f"{enc['id']}: {what}: no floor under it {label}")
                continue
            reached, (dy, where) = walk(s)
            if not any((goal[0] - gi) ** 2 + (goal[1] - gj) ** 2 <= 4 and abs(gy - goal[2]) <= CLIMB for gi, gj, gy in reached):
                at_txt = ""
                if where is not None:
                    x, z = -half[0] + (where[0] + 0.5) * CELL, -half[2] + (where[1] + 0.5) * CELL
                    at_txt = f": an edge of {dy:.1f} m at ({x:.1f}, {where[2]:.1f}, {z:.1f})"
                failed[k].append(f"{enc['id']}: {what} is on an island {label} (a creature steps {CLIMB} m at most, and can't reach "
                                 f"the floor at doorway {door['id']}{at_txt}). Step it down in 0.2 m steps (boss_rooms.step_dais)")
    for k, found in failed.items():
        boss = bool(enc.get("boss")) and k == 0
        if found and (boss or len(found) == len(configs)):
            problems += found
    return problems


TARGET_CLEAR = 1.5   # metres round a lure target's area that must be open floor


def check_targets(room, enc, spots, half, goal, walk, start_of):
    """A boss's lure targets (its weakTo mechanisms that break its guard: the Thrall's hammer): the boss must be able to walk onto the
    target from the players' floor, and nothing may stand on it or within TARGET_CLEAR m round it (no props, rails or levers to
    path round: the user couldn't lure the Thrall in through four rails and two levers). The mechanism itself (up, at its start)
    doesn't count."""
    problems = []
    mechs = {m["id"]: m for m in room.get("mechs", [])}
    for w in (enc.get("boss") or {}).get("weakTo", []):
        if not w.get("breaks", True) or w.get("mech") not in mechs:
            continue
        m = mechs[w["mech"]]
        from walk_check import frame
        f = frame(m["at"], m.get("euler", [0, 0, 0]))
        area, area_at = np.array(w.get("area", [5, 4, 5]), float), np.array(w.get("areaAt", [0, 0, 0]), float)
        centre = (f @ np.r_[area_at[0], 0.0, area_at[2], 1.0])[:3]
        floor_y = centre[1]
        r = max(area[0], area[2]) / 2 + TARGET_CLEAR
        blocked = []
        n = int(math.ceil(r / CELL))
        ci, cj = int((centre[0] + half[0]) // CELL), int((centre[2] + half[2]) // CELL)
        for di in range(-n, n + 1):
            for dj in range(-n, n + 1):
                x, z = -half[0] + (ci + di + 0.5) * CELL, -half[2] + (cj + dj + 0.5) * CELL
                if math.hypot(x - centre[0], z - centre[2]) > r:
                    continue
                if not any(abs(y - floor_y) <= CLIMB for y in spots.get((ci + di, cj + dj), [])):
                    blocked.append((round(float(x), 1), round(float(z), 1)))
        what = f"{enc['id']}: its target {w['mech']} at {np.round(centre, 1).tolist()}"
        if blocked:
            problems.append(f"{what}: the boss can't be lured onto it from every side: {len(blocked)} spots within {r:.1f} m of it aren't "
                            f"open floor (e.g. {blocked[:3]}): clear round it")
        s = start_of(centre, below=0.4)
        if s is None or s not in walk(goal)[0]:
            problems.append(f"{what}: the boss can't walk onto it from the floor at the way in")
    return problems


def main():
    files = sys.argv[1:] or [os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs", n)
                             for n in sorted(os.listdir(os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs"))) if n.endswith(".json")]
    bounds = json.load(open(os.path.join(HERE, "prefab_bounds.json")))
    bad = 0
    for pf in files:
        pack = json.load(open(pf))
        for room in pack.get("rooms", []):
            if "parts" not in room or 0 in room.get("size", [0, 0, 0])[::2]:
                continue
            for enc in (m for m in room.get("mechs", []) if m.get("kind") == "encounter"):
                for p in check_fight(room, enc, bounds):
                    print(f"{pack.get('name', os.path.basename(pf))}: {room['name']}: {p}")
                    bad += 1
    print(f"creature check: {bad} problems (islands, blocked targets)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
