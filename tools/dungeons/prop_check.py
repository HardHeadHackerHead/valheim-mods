"""
Props that stand on something must stand on something (python prop_check.py [pack.json ...]): a lantern, torch, brazier, crystal,
chest, bones, rubble, remains or spawner in one of our rooms needs a floor, step or ledge top under it, from the parts' boxes
(prefab_bounds.json, scaled, turned and moved as the part is). Under it means: a box whose top is no more than 0.3 m below the prop's
bottom and no higher than the prop's middle (a prop sunk into the floor is fine), under the middle of the prop. A spawner (a point)
counts from 0.5 m above the floor (where the kit puts it). Hanging things are left out: chains, a prop with a chain's foot on its top
(a hung lantern or brazier), and a trophy against a wall (a carved face, a helmet, a sword). End caps are left out (their props stand
in the room they close). A mechanism's parts are checked with the mechanism at its start, against the room and the mechanism.
Also each encounter's spawn points (check_spawns). Exits 1 when any prop floats or a spawn point is badly placed.
"""
import json, math, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from walk_check import boxes_of, frame, SHAPES  # noqa: E402
from dungeon_parts import euler_matrix  # noqa: E402

STANDING = ("Morkhalla_giant_railing_torch", "MountainKit_brazier_blue", "blackmarble_altar_crystal", "dverger_demister",
            "TreasureChest_trollcave", "Morkhalla_ChestAncient", "bone_stack", "skull_pile", "Skull2", "Morkhalla_Bench",
            "Morkhalla_Table", "Morkhalla_WeaponStand", "Morkhalla_firepit", "Morkhalla_coal_pile_memorial", "bar_iron_stack",
            "grausten_pile", "Morkhalla_Stonepile", "Morkhalla_Rubble", "MemorialStone_Large", "giant_", "Morkhalla_Rug_middle",
            "Morkhalla_StatuePieceFace_big", "piece_walltorch_standing", "Spawner_", "crypt_skeleton_chest", "piece_brazier")
HANGING = ("Morkhalla_WallChain1", "Morkhalla_Chain")
MOUNTED = ("Morkhalla_StatuePieceFace_big", "giant_helmet", "giant_sword")   # trophies that may hang on a wall instead
DROP, SINK = 0.3, 0.5          # a support's top may be this far below the prop's bottom; a spawner stands this high above its floor


def _palette_mounts():
    """Every palette piece's "mount" (tools/dungeons/palettes/*.json, builder2's): ground stands on a floor, hang hangs from what's
    above, wall is mounted on a wall. The lists above cover what no palette has."""
    import glob
    out = {}
    for path in glob.glob(os.path.join(HERE, "palettes", "*.json")):
        try:
            for e in json.load(open(path, encoding="utf-8")).get("pieces", []):
                if e.get("prefab") and e.get("mount"):
                    out.setdefault(e["prefab"], e["mount"])
        except Exception:
            continue
    return out


MOUNTS = _palette_mounts()


def standing(prefab):
    return any(prefab.startswith(s) for s in STANDING) or MOUNTS.get(prefab) in ("ground", "hang", "wall")


def hanging(prefab):
    return prefab in HANGING or MOUNTS.get(prefab) == "hang"


def wall_mounted(prefab):
    return prefab.startswith(MOUNTED) or MOUNTS.get(prefab) == "wall"


def _plain_box(p, f, bounds):
    """A part's own box (prefab_bounds), scaled, turned and moved, whatever prefab_shapes says about walking through it."""
    b = bounds.get(p["prefab"])
    if b is None:
        return None
    lo, hi = np.array(b[0]), np.array(b[1])
    if np.allclose(lo, hi):
        return None                                     # (a true point: a spawner)
    sc = np.array(p.get("scale", [1, 1, 1]))
    R = euler_matrix(*p.get("euler", [0, 0, 0]))
    pts = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]) * sc
    w = (R @ pts.T).T + np.array(p["at"])
    w = (f[:3, :3] @ w.T).T + f[:3, 3]
    return w.min(0), w.max(0)


# walk-through pieces (prefab_shapes.json gives them no solid boxes: vines, rugs, webs, candles, cloth): checked by their own box like
# any prop, but nothing stands on or hangs from them
SOFT = {k for k, v in SHAPES.items() if not v}


def placed(parts, f, bounds):
    """(lo, hi, prefab, at) for each part, in the room's frame; a walk-through piece by its plain box; a part with no box at all (a
    spawner) is a point at its place."""
    out = []
    for p in parts:
        at = (f[:3, :3] @ np.array(p["at"])) + f[:3, 3]
        b = boxes_of([p], f, bounds)                    # (a hollow prefab's boxes taken together)
        if b:
            out.append((np.min([x[0] for x in b], 0), np.max([x[1] for x in b], 0), p["prefab"], at))
            continue
        pb = _plain_box(p, f, bounds)
        out.append((pb[0], pb[1], p["prefab"], at) if pb is not None else (at, at, p["prefab"], at))
    return out


def check_room(room, bounds):
    everything, groups = [], []
    everything += [(x, None) for x in placed(room.get("parts", []), np.eye(4), bounds)]
    for m in room.get("mechs", []):
        f = frame(m["at"], m.get("euler", [0, 0, 0]))
        if m.get("poses"):
            pose = m["poses"][min(m.get("start", 0), len(m["poses"]) - 1)]
            f = f @ frame(pose["at"], pose["euler"])
        everything += [(x, m["id"]) for x in placed(m.get("parts", []), f, bounds)]
    chains = [x for x, _ in everything if hanging(x[2])]
    # (a mechanism that moves, a platform, trap or lift with more than one pose, holds its own parts up: a hammer in the air is no
    # floating prop)
    moving = {m["id"] for m in room.get("mechs", []) if m.get("kind") in ("platform", "trap", "rotator", "stair") and len(m.get("poses", [])) > 1}
    problems = []
    for (lo, hi, name, at), mech in everything:
        if not standing(name) or mech in moving:
            continue
        mid = (lo + hi) / 2
        if name.startswith("Spawner_"):
            bottom, middle = at[1] - SINK, at[1]
        else:
            bottom, middle = lo[1], mid[1]
        hung = any(c[0][0] <= mid[0] <= c[1][0] and c[0][2] <= mid[2] <= c[1][2] and abs(c[0][1] - hi[1]) <= 0.6 for c in chains)
        if (hanging(name) or MOUNTS.get(name) == "wall") and any((hi[1] - 0.6 <= b[0][1] <= hi[1] + 0.6 or b[0][1] <= hi[1] <= b[1][1]) and b[0] is not lo
                                 and (b[2] not in SOFT or b[2] == name)        # (a strand may go on from a strand)
                                 and any(b[0][0] - 0.3 <= q[0] <= b[1][0] + 0.3 and b[0][2] - 0.3 <= q[2] <= b[1][2] + 0.3
                                         for q in (mid, at))       # (its middle, or its origin: where a lamp's ring is)
                                 for (b, _) in everything):
            continue                                   # (a hung piece with something right above it)
        mounted = wall_mounted(name) and any(
            b[1][1] - b[0][1] > (0.5 if MOUNTS.get(name) == "wall" else 2.0) and b[0][1] <= mid[1] <= b[1][1] and b[0] is not lo
            and b[0][0] - 0.3 <= hi[0] and lo[0] <= b[1][0] + 0.3 and b[0][2] - 0.3 <= hi[2] and lo[2] <= b[1][2] + 0.3
            for (b, _) in everything if b[2] not in SOFT)
        if hung or mounted:
            continue
        under = [b for (b, _) in everything if b is not None and not hanging(b[2]) and b[2] not in SOFT and (b[0] is not lo)
                 and b[0][0] - 0.05 <= mid[0] <= b[1][0] + 0.05 and b[0][2] - 0.05 <= mid[2] <= b[1][2] + 0.05
                 and ((bottom - DROP <= b[1][1] <= max(middle, bottom + 0.05)) or b[0][1] <= bottom <= b[1][1])   # (on it, or set in it)
                 and b[1][1] > b[0][1]]
        if not under:
            below = [b[1][1] for (b, _) in everything if b[0] is not lo and b[1][1] < bottom and not hanging(b[2])
                     and b[0][0] <= mid[0] <= b[1][0] and b[0][2] <= mid[2] <= b[1][2]]
            gap = f"{bottom - max(below):.1f} m above the nearest top under it" if below else "nothing under it at all"
            problems.append(f"{name}{' (' + mech + ')' if mech else ''} at {np.round(at, 1).tolist()} floats: {gap}")
    return problems


def check_spawns(room, bounds):
    """An encounter's spawn points: each on a floor top (within 0.3 m), nothing at body height within 0.7 m of it, inside its area,
    at least 3 m from every doorway and from the first 6 m of the way in (from the doorway it's reached from), and 2.5 m apart."""
    problems = []
    fights = [m for m in room.get("mechs", []) if m.get("kind") == "encounter"]
    if not fights:
        return problems
    boxes = [b[:2] for b in boxes_of(room.get("parts", []), np.eye(4), bounds)]
    for m in room.get("mechs", []):
        if m.get("kind") in ("gate", "door"):
            continue                                            # (gates: open or shut, they stand in the doorway)
        f = frame(m["at"], m.get("euler", [0, 0, 0]))
        if m.get("poses"):
            pose = m["poses"][min(m.get("start", 0), len(m["poses"]) - 1)]
            f = f @ frame(pose["at"], pose["euler"])
        boxes += [b[:2] for b in boxes_of(m.get("parts", []), f, bounds)]
    doors = {d["id"]: d for d in room["doorways"]}
    for m in fights:
        at, area = np.array(m["at"]), np.array(m.get("area", [0, 0, 0]))
        pts = [np.array(p) for p in m.get("points", [])]
        if not pts:
            problems.append(f"{m['id']}: no spawn points")
        way = None
        if m.get("reachedFrom") in doors:
            d = doors[m["reachedFrom"]]
            a, fc = np.array(d["at"]), np.array(d["facing"])
            way = (a + fc * 1.5, a + fc * 6.0)
        for i, p in enumerate(pts):
            where = f"{m['id']}: spawn {np.round(p, 1).tolist()}"
            if np.any(np.abs(p - at) > area / 2 + 0.01):
                problems.append(f"{where} is outside its area")
            under = [hi[1] for lo, hi in boxes if lo[0] <= p[0] <= hi[0] and lo[2] <= p[2] <= hi[2] and p[1] - 0.3 <= hi[1] <= p[1] + 0.05]
            if not under:
                problems.append(f"{where} has no floor top within 0.3 m under it")
            near = [1 for lo, hi in boxes if lo[0] - 0.7 <= p[0] <= hi[0] + 0.7 and lo[2] - 0.7 <= p[2] <= hi[2] + 0.7
                    and lo[1] < p[1] + 1.8 and hi[1] > p[1] + 0.3]
            if near:
                problems.append(f"{where} has something within 0.7 m at body height")
            for d in doors.values():
                if math.hypot(*(p - np.array(d["at"]))[::2]) < 3.0:
                    problems.append(f"{where} is within 3 m of doorway {d['id']}")
            if way is not None:
                a, b = way
                t = np.clip(np.dot((p - a)[::2], (b - a)[::2]) / max(np.dot((b - a)[::2], (b - a)[::2]), 1e-9), 0, 1)
                if np.linalg.norm((p - (a + t * (b - a)))[::2]) < 3.0:
                    problems.append(f"{where} is on the way in from {m['reachedFrom']}")
            for q in pts[i + 1:]:
                if math.hypot(*(p - q)[::2]) < 2.5:
                    problems.append(f"{where} is within 2.5 m of another")
    return problems


def main():
    files = sys.argv[1:] or [os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs", n) for n in ("shiftinghalls.json", "testmechs.json")]
    bounds = json.load(open(os.path.join(HERE, "prefab_bounds.json")))
    bad = 0
    for pf in files:
        pack = json.load(open(pf))
        for room in pack["rooms"]:
            if "parts" not in room or 0 in room["size"][::2]:      # (an end cap's props stand in the room it closes)
                continue
            for p in check_room(room, bounds) + check_spawns(room, bounds):
                print(f"{pack.get('name', os.path.basename(pf))}: {room['name']}: {p}")
                bad += 1
    print(f"prop check: {bad} floating")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
