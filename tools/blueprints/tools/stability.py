"""
Estimate the structural support of a blueprint before anyone builds it, with the same rules the game uses.

    python stability.py my_build.json            # prints a summary and the weakest pieces
    from stability import analyze                # or use it from a design script: analyze(blueprint.items, pieces)

How the game works (WearNTear in the game's code):
  * A piece touching the ground has its material's MAX support. Everything else gets support from the pieces it touches.
  * From a touching piece with support S at distance d (centre to centre, plus 0.1 m) a piece can get  S - loss * d * S.
    "loss" is the material's horizontal loss for a sideways neighbour, the vertical loss for one directly below, and in between for a diagonal.
  * Two supports on opposite sides (more than 100 degrees apart) give the average of the two, so a beam between two pillars is stronger.
  * A piece collapses when its support falls below the material's MIN support. The game colours support from red (the minimum) to green
    (half of the maximum); at the maximum it is blue.
This script works from each piece's bounding box, so it is an estimate: good for finding a design that will certainly fall, not a promise
that a marginal one will stand. The ground is a flat plane at y = 0 (blueprint heights are above the ground at the anchor).
"""
import json, math, sys

# max support, min support, horizontal loss, vertical loss (the game's numbers)
MATERIALS = {
    "Wood": (100, 10, 0.2, 0.125), "HardWood": (140, 10, 1 / 6, 0.1), "Stone": (1000, 100, 1.0, 0.125), "Iron": (1500, 20, 1 / 13, 1 / 13),
    "Marble": (1500, 100, 0.5, 0.125), "Ashstone": (2000, 100, 1 / 3, 0.1), "Ancient": (5000, 100, 0.25, 1 / 15),
    "Ice": (1000, 100, 1 / 3, 0.125), "Timberwood": (200, 10, 0.2, 1 / 13),
}


def _world_box(item, piece):
    from blueprint import world_box
    return world_box(item, piece)


def _closest(box, p):
    lo, hi = box
    return [min(max(p[i], lo[i]), hi[i]) for i in range(3)]


def _dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def analyze(items, catalog, ground_tolerance=0.2, touch=0.12, ground=None):
    """Returns a list of dicts: piece, index, support, max, min, ok. `catalog` is a Pieces (blueprint.py) or a dict name -> piece.
    `ground(x, z)` gives the ground height for a sloped site (terrain.Site.ground); without it the ground is flat at y = 0.
    Pieces marked "g" are measured from the ground, so their heights are made absolute first."""
    by_name = getattr(catalog, "by_name", catalog)
    nodes = []
    for i, item in enumerate(items):
        p = by_name.get(item["p"])
        if not p or not p.get("material") or p["material"] not in MATERIALS or p.get("supports") is False:
            continue
        mx, mn, hl, vl = MATERIALS[p["material"]]
        if ground is not None and item.get("g"):
            item = dict(item, y=item["y"] + ground(item["x"], item["z"]))
        box = _world_box(item, p)
        com = [(box[0][k] + box[1][k]) / 2 for k in range(3)]
        off = p.get("comOffset")
        if off:
            com = [com[k] + off[k] for k in range(3)]
        nodes.append({"i": i, "p": item["p"], "box": box, "com": com, "max": mx, "min": mn, "hl": hl, "vl": vl,
                      "grounded": box[0][1] <= (ground(com[0], com[2]) if ground else 0.0) + ground_tolerance, "support": 0.0, "near": []})
    for n in nodes:
        n["support"] = float(n["max"]) if n["grounded"] else 0.0

    for a in range(len(nodes)):
        for b in range(a + 1, len(nodes)):
            A, B = nodes[a]["box"], nodes[b]["box"]
            if all(A[0][k] - touch <= B[1][k] and B[0][k] - touch <= A[1][k] for k in range(3)):
                nodes[a]["near"].append(nodes[b])
                nodes[b]["near"].append(nodes[a])

    for _ in range(40):
        changed = False
        for n in nodes:
            if n["grounded"]:
                continue
            best, points, values = 0.0, [], []
            for o in n["near"]:
                s = o["support"]
                if s <= 0:
                    continue
                d = _dist(n["com"], o["com"]) + 0.1
                best = max(best, s - n["hl"] * d * s)
                pt = _closest(o["box"], n["com"])
                if pt[1] < n["com"][1] + 0.05:
                    v = [pt[k] - n["com"][k] for k in range(3)]
                    length = math.sqrt(sum(c * c for c in v)) or 1e-9
                    if v[1] / length < 0:
                        t = math.acos(max(-1.0, min(1.0, 1 - abs(v[1] / length)))) / (math.pi / 2)
                        loss = n["hl"] + (n["vl"] - n["hl"]) * t
                        best = max(best, s - loss * d * s)
                    points.append(v)
                    values.append(s - n["vl"] * d * s)
            for l in range(len(points) - 1):
                for m in range(l + 1, len(points)):
                    avg = (values[l] + values[m]) / 2
                    if avg <= best:
                        continue
                    f, t_ = (points[l][0], points[l][2]), (points[m][0], points[m][2])
                    lf, lt = math.hypot(*f), math.hypot(*t_)
                    if lf > 1e-6 and lt > 1e-6:
                        ang = math.degrees(math.acos(max(-1.0, min(1.0, (f[0] * t_[0] + f[1] * t_[1]) / (lf * lt)))))
                        if ang >= 100:
                            best = avg
            best = min(best, n["max"])
            if best > n["support"] + 0.01:
                n["support"] = best
                changed = True
        if not changed:
            break

    return [{"index": n["i"], "p": n["p"], "support": round(n["support"], 1), "max": n["max"], "min": n["min"],
             "ok": n["support"] >= n["min"], "fraction": n["support"] / n["max"]} for n in nodes]


def report(results, top=12):
    bad = [r for r in results if not r["ok"]]
    print(f"{len(results)} structural pieces: {len(results) - len(bad)} stand, {len(bad)} would fall")
    weakest = (bad + sorted(results, key=lambda r: (r["support"] - r["min"]) / max(1.0, r["max"] * 0.5 - r["min"])))[:max(top, len(bad))]
    seen_idx = set()
    weakest = [r for r in weakest if not (r["index"] in seen_idx or seen_idx.add(r["index"]))][:max(top, len(bad))]
    for r in weakest:
        flag = "FALLS" if not r["ok"] else "ok   "
        print(f"  {flag} {r['p']:22} support {r['support']:7.1f}  (min {r['min']}, max {r['max']})  piece #{r['index']}")
    return not bad


if __name__ == "__main__":
    from blueprint import Pieces
    doc = json.load(open(sys.argv[1], encoding="utf-8"))
    ok = report(analyze(doc["pieces"], Pieces()))
    sys.exit(0 if ok else 1)
