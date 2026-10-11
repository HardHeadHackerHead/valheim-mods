"""
Variety numbers for a generated pack (the report make_dungeon.py writes next to it, read by dungeon-layout2's gate):
  rooms          every recipe room: its recipe (job), shell, features and the parameters chosen
  jobs, shells   how many different jobs and shells the recipe rooms use
  spread         per job with two rooms or more: the mean distance between its rooms' parameters (0 = the same room twice,
                 1 = nothing in common): shell, size, storeys/levels, doorway pattern, features, dressing, pillar pattern, ...
  sameRooms      pairs of recipe rooms of one job whose parameters are nearly the same (spread < 0.15): candidates for repetition
  overlap        per other pack that has a report (tools/dungeons/reports): the share of this pack's recipe rooms that pack already
                 has: a room of the same theme and job whose parameters are nearly the same (spread < 0.15); a pack in another theme
                 shares none (its rooms are built of other things at another scale)
"repeats within N rooms" needs real layouts: the gate counts it from the rooms each seed uses, by the recipe and shell given here.
"""
import glob
import itertools
import json
import os


def _vec(info):
    p = info["params"]
    size = p.get("size", [0, 0, 0])
    doors = p.get("doors", [])
    return {
        "shell": info["shell"],
        "sx": size[0], "sy": size[1], "sz": size[2],
        "doors": len(doors),
        "pattern": _pattern(doors),
        "levels": len({d[2] for d in doors}) if doors else 1,
        "sides": tuple(sorted((d[0] - doors[0][0]) % 4 for d in doors)) if doors else (),
        "storeys": round(size[1] / 4.0),
        "features": tuple(sorted(info.get("features", []))),
        "dressing": p.get("dressing"),
        "pillars": p.get("pillar_pattern"),
        "variant": str(p.get("variant") or p.get("bridge") or p.get("notches") or ""),
    }


def _pattern(doors):
    if len(doors) < 2:
        return "one"
    ks = sorted(d[0] for d in doors[:2])
    return "through" if (ks[1] - ks[0]) % 4 == 2 else ("turn" if ks[0] != ks[1] else "same")


def _num(x):
    return isinstance(x, (int, float)) and not isinstance(x, bool)


def _param_distance(x, y):
    """0..1 between two parameter values: numbers by relative difference, lists element by element, anything else equal or not."""
    if _num(x) and _num(y):
        return min(1.0, abs(x - y) / max(abs(x), abs(y), 1.0))
    if isinstance(x, list) and isinstance(y, list):
        if len(x) != len(y) or not x:
            return 1.0 if x != y else 0.0
        return sum(_param_distance(p, q) for p, q in zip(x, y)) / len(x)
    return 0.0 if x == y else 1.0


# parameters the eye doesn't see from the doorway: how many props or lights (the dressing set is what shows), the doorways' exact
# offsets (their sides and levels are in the structural part), and the shape and size (structural too)
UNSEEN = {"props", "lights", "doors", "shape", "size", "dressing", "pillar_pattern", "pillars", "pieces"}


def distance(a, b):
    """0 (the same room) .. 1 (nothing in common), as seen walking in: the shell, its footprint and storeys, the doorways' sides
    and levels, features, dressing and pillars (structural), and the job's own visible choices (where the bridge, pit, ledge,
    lever, screen... are and which variant)."""
    va, vb = _vec(a), _vec(b)
    keys = (set(a["params"]) | set(b["params"])) - UNSEEN
    pd = [(_param_distance(a["params"][k], b["params"][k]) if k in a["params"] and k in b["params"] else 1.0) for k in keys]
    structural = _structural(va, vb)
    if not pd:
        return round(structural, 3)
    return round(0.6 * structural + 0.4 * (sum(pd) / len(pd)), 3)


def _structural(va, vb):
    parts = []
    for key in ("shell", "pattern", "sides", "dressing", "pillars", "variant", "features"):
        parts.append(0.0 if va[key] == vb[key] else 1.0)
    parts.append(0.0 if va["storeys"] == vb["storeys"] else 1.0)
    for key, scale in (("sx", 16.0), ("sz", 16.0), ("sy", 12.0)):
        parts.append(min(1.0, abs(va[key] - vb[key]) / scale))
    parts.append(min(1.0, abs(va["doors"] - vb["doors"]) / 2.0))
    parts.append(0.0 if va["levels"] == vb["levels"] else 1.0)
    return sum(parts) / len(parts)


def report(spec, theme, infos, signatures, others_dir=None):
    by_job = {}
    for i in infos:
        by_job.setdefault(i["recipe"], []).append(i)
    spread, same = {}, []
    for jb, items in by_job.items():
        if len(items) < 2:
            continue
        ds = []
        for a, b in itertools.combinations(items, 2):
            d = distance(a, b)
            ds.append(d)
            if d < 0.15:
                same.append([a["name"], b["name"], round(float(d), 2)])
        spread[jb] = round(float(sum(ds) / len(ds)), 2)
    # overlap: rooms another pack already has: one of the same theme and job whose parameters are nearly the same (a room in another
    # theme is another room, whatever its job and shell)
    overlap = {}
    others_dir = others_dir or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "reports")
    for path in glob.glob(os.path.join(others_dir, "*.report.json")):
        try:
            other = json.load(open(path))
        except Exception:
            continue
        if other.get("pack") == spec["name"]:
            continue
        if other.get("theme") != theme.name:
            overlap[other["pack"]] = 0.0
            continue
        theirs = [dict(r, name=r.get("name", "")) for r in other.get("rooms", [])]
        shared = [i for i in infos if any(r["recipe"] == i["recipe"] and distance(i, r) < 0.15 for r in theirs)]
        overlap[other["pack"]] = round(len(shared) / max(1, len(infos)), 2)
    return {
        "pack": spec["name"], "theme": theme.name, "seed": spec.get("seed", 1),
        "rooms": [{"name": i["name"], "recipe": i["recipe"], "shell": i["shell"], "features": i.get("features", []),
                   "params": i["params"]} for i in infos],
        "signatures": signatures,
        "variety": {
            "recipeRooms": len(infos), "signatureRooms": len(signatures),
            "jobs": len(by_job), "shells": len({i["shell"] for i in infos}),
            "spread": spread, "sameRooms": same, "overlap": overlap,
        },
    }
