"""
Helpers for designing blueprints (files the BuildOrders mod turns into build-order ghosts).

A blueprint is JSON: {"name", "anchor": "bed" | "player" | "look" | "world", "at": [x, z], "offset": [x, y, z], "yaw", "auto", "pieces": [...]}
where each piece is {"p": prefab, "x", "y", "z", "rx", "ry", "rz", "g"}. The numbers are where the piece's ORIGIN goes, in metres from the
anchor, and its turn in degrees (Unity order: z, then x, then y). Pieces keep their origin in different places, so this module reads the
game's own piece list (_pieces.json) and lets you place a piece by its box (`place`), by a snap point (`place_snap`, `place_mid`, `attach`)
or by its origin (`raw`). `check()` lists what the design needs and anything that would stop it being built. No packages needed.
"""
import json, math, os

HERE = os.path.dirname(os.path.abspath(__file__))
GAME_BLUEPRINTS = r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\blueprints"


def blueprints_dir():
    """The folder the game reads blueprints from: BLUEPRINTS_DIR, else the folder above this script when it lives inside it, else this PC's game folder."""
    if os.environ.get("BLUEPRINTS_DIR"):
        return os.environ["BLUEPRINTS_DIR"]
    parent = os.path.dirname(HERE)
    if os.path.exists(os.path.join(parent, "_pieces.json")):
        return parent
    return GAME_BLUEPRINTS


DEFAULT_PIECES = os.path.join(blueprints_dir(), "_pieces.json")


# ---------------------------------------------------------------- rotations (Unity: z, then x, then y)

def rotation(rx=0.0, ry=0.0, rz=0.0):
    """3x3 rotation matrix (rows) for Unity euler angles in degrees."""
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx, cy, sy, cz, sz = math.cos(ax), math.sin(ax), math.cos(ay), math.sin(ay), math.cos(az), math.sin(az)
    Rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    Ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    Rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]
    return _mul(_mul(Ry, Rx), Rz)


def _mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def _apply(m, v):
    return [m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2], m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2], m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]]


def item_rotation(item):
    return rotation(item.get("rx", 0.0), item.get("ry", 0.0), item.get("rz", 0.0))


# ---------------------------------------------------------------- the piece list

class Pieces:
    def __init__(self, path=DEFAULT_PIECES):
        if not os.path.exists(path):
            raise SystemExit("No _pieces.json yet. Run extract_pieces.py (reads the game files), or play once with BuildOrders 1.5 or later.")
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        self.path = path
        self.by_name = {p["p"]: p for p in data["pieces"]}
        self.stations = data.get("stations", {})

    def has(self, name):
        return name in self.by_name

    def snaps(self, name):
        raw = self.by_name[name].get("snaps", {})
        if isinstance(raw, list):  # an older in-game list: points without names
            return {"snap %d" % (i + 1): v for i, v in enumerate(raw)}
        return {k.replace("$hud_snappoint_", ""): v for k, v in raw.items()}

    def snap(self, name, snap):
        """The local position of a piece's named snap point (e.g. "bottom 1", "top", "corner 3"), where pieces join."""
        snaps = self.snaps(name)
        if snap in snaps:
            return snaps[snap]
        matches = [v for k, v in snaps.items() if k.startswith(snap)]
        if len(matches) == 1:
            return matches[0]
        raise KeyError(f"{name} has no snap point '{snap}'; it has: {sorted(snaps)}")

    def ascend(self, name):
        """The way a stair, ramp or roof climbs, as (dx, dz, rise) in the piece's own turn."""
        a = self.by_name[name].get("ascend")
        return (a[0], a[2], a[1]) if a else None

    def size(self, name):
        p = self.by_name[name]
        (x0, y0, z0), (x1, y1, z1) = p["min"], p["max"]
        return (x1 - x0, y1 - y0, z1 - z0)

    def names(self, contains=""):
        return sorted(n for n in self.by_name if contains in n)

    def find(self, text="", material=None, category=None):
        """Pieces whose name contains text (and optionally of a material / category): for looking things up."""
        return [p for p in self.by_name.values() if text in p["p"] and (material is None or p.get("material") == material)
                and (category is None or p.get("category") == category)]


def world_box(item, piece):
    """The axis-aligned box a placed piece fills, in blueprint coordinates: ([x0, y0, z0], [x1, y1, z1])."""
    (x0, y0, z0), (x1, y1, z1) = piece["min"], piece["max"]
    r = item_rotation(item)
    pts = [_apply(r, [x, y, z]) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]
    o = (item["x"], item["y"], item["z"])
    return [min(p[i] for p in pts) + o[i] for i in range(3)], [max(p[i] for p in pts) + o[i] for i in range(3)]


def _quat(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def solids(item, piece, shrink=0.1):
    """The solid parts of a placed piece as turned boxes (centre, three axes, half sizes): its colliders when they fill most of it, else its box."""
    r = item_rotation(item)
    o = [item["x"], item["y"], item["z"]]
    out = []
    boxes = piece.get("boxes") or []
    (x0, y0, z0), (x1, y1, z1) = piece["min"], piece["max"]
    volume = max(1e-6, (x1 - x0) * (y1 - y0) * (z1 - z0))
    if boxes and sum(b["s"][0] * b["s"][1] * b["s"][2] for b in boxes) >= 0.45 * volume:
        for b in boxes:
            m = _mul(r, _quat(b["r"]))
            c = _apply(r, b["c"])
            axes = [[m[0][k], m[1][k], m[2][k]] for k in range(3)]
            out.append(([c[i] + o[i] for i in range(3)], axes, [max(0.0, abs(s) / 2 - shrink) for s in b["s"]]))
    else:
        c = _apply(r, [(x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2])
        axes = [[r[0][k], r[1][k], r[2][k]] for k in range(3)]
        out.append(([c[i] + o[i] for i in range(3)], axes, [max(0.0, (x1 - x0) / 2 - shrink), max(0.0, (y1 - y0) / 2 - shrink), max(0.0, (z1 - z0) / 2 - shrink)]))
    return out


def _obb_hit(a, b):
    """Do two turned boxes overlap? (separating-axis test)"""
    (ca, A, ea), (cb, B, eb) = a, b
    d = [cb[i] - ca[i] for i in range(3)]
    axes = list(A) + list(B)
    for u in A:
        for v in B:
            c = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            if sum(x * x for x in c) > 1e-9:
                axes.append(c)
    for ax in axes:
        dist = abs(sum(d[i] * ax[i] for i in range(3)))
        ra = sum(ea[k] * abs(sum(A[k][i] * ax[i] for i in range(3))) for k in range(3))
        rb = sum(eb[k] * abs(sum(B[k][i] * ax[i] for i in range(3))) for k in range(3))
        if dist > ra + rb:
            return False
    return True


def pieces_overlap(item_a, piece_a, item_b, piece_b):
    return any(_obb_hit(x, y) for x in solids(item_a, piece_a) for y in solids(item_b, piece_b))


# ---------------------------------------------------------------- the blueprint

class Blueprint:
    def __init__(self, name, pieces, anchor="bed", offset=(0, 0, 0), yaw=0, auto=True, at=None):
        self.meta = {"name": name, "anchor": anchor, "offset": list(offset), "yaw": yaw, "auto": auto}
        if at:
            self.meta["at"] = list(at)
        self.pieces = pieces
        self.catalog = pieces
        self.items = []

    def box(self, name):
        p = self.catalog.by_name[name]
        return p["min"], p["max"]

    def _item(self, name, x, y, z, rx, ry, rz, ground):
        if not self.catalog.has(name):
            raise KeyError(f"'{name}' is not a buildable piece (look it up in _pieces.json)")
        item = {"p": name, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "ry": round(ry, 3)}
        if rx:
            item["rx"] = round(rx, 3)
        if rz:
            item["rz"] = round(rz, 3)
        if ground:
            item["g"] = True
        self.items.append(item)
        return item

    def place(self, name, x, z, y=0.0, yaw=0.0, ground=False, height=None, ref="bottom"):
        """
        Put a piece so a chosen part of it sits at (x, y, z) (y is metres above the ground when ground=True).
          ref="bottom": the bottom of the piece is at y.   ref="top": the top is at y.   ref="center": the middle is at y.
        Pass height= for pieces whose real model is a little rough around the edges (stone walls): the piece is then treated as exactly
        that tall and centred on its origin, which is how the game builds them. yaw turns it about its own middle.
        """
        (x0, y0, z0), (x1, y1, z1) = self.box(name)
        cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
        lo, hi = (-height / 2, height / 2) if height is not None else (y0, y1)
        origin_y = {"bottom": y - lo, "top": y - hi, "center": y - (lo + hi) / 2}[ref]
        tx, _, tz = _apply(rotation(0, yaw, 0), [cx, 0, cz])   # so that (x, z) is the piece's middle, not its origin
        return self._item(name, x - tx, origin_y, z - tz, 0, yaw, 0, ground)

    def raw(self, name, x, y, z, yaw=0.0, ground=False, rx=0.0, rz=0.0):
        """Place a piece by its origin exactly (use when the origin is the point you want, e.g. a stake wall's base)."""
        return self._item(name, x, y, z, rx, yaw, rz, ground)

    # ---- snapping: pieces join where their snap points meet ----

    def snap_world(self, item, snap):
        """Where a placed piece's snap point is, in blueprint coordinates."""
        s = _apply(item_rotation(item), self.catalog.snap(item["p"], snap))
        return item["x"] + s[0], item["y"] + s[1], item["z"] + s[2]

    def place_snap(self, name, snap, point, yaw=0.0, ground=False, rx=0.0, rz=0.0):
        """Put a piece so that its snap point sits exactly at `point` (x, y, z)."""
        s = _apply(rotation(rx, yaw, rz), self.catalog.snap(name, snap))
        return self._item(name, point[0] - s[0], point[1] - s[1], point[2] - s[2], rx, yaw, rz, ground)

    def place_mid(self, name, snaps, point, yaw=0.0, ground=False, rx=0.0, rz=0.0):
        """Put a piece so the middle of two or more of its snap points (an edge) sits at `point`: for stairs, roofs and rails that span a gap."""
        pts = [self.catalog.snap(name, k) for k in snaps]
        mid = [sum(p[i] for p in pts) / len(pts) for i in range(3)]
        s = _apply(rotation(rx, yaw, rz), mid)
        return self._item(name, point[0] - s[0], point[1] - s[1], point[2] - s[2], rx, yaw, rz, ground)

    def attach(self, name, snap, to_item, to_snap, yaw=0.0, ground=False, rx=0.0, rz=0.0):
        """Put a new piece so its snap point meets a snap point of a piece already placed."""
        return self.place_snap(name, snap, self.snap_world(to_item, to_snap), yaw, ground, rx, rz)

    def yaw_for(self, name, dx, dz):
        """The yaw (degrees) that makes a stair, ramp, roof or slanted beam climb towards the direction (dx, dz) on the ground."""
        ax, az, _ = self.catalog.ascend(name)
        yaw = math.degrees(math.atan2(dx, dz) - math.atan2(ax, az)) % 360.0
        nearest = round(yaw / 90.0) * 90.0
        return float(nearest % 360.0) if abs(nearest - yaw) < 0.01 else round(yaw, 3)

    # ---- what it costs and whether it can be built ----

    def materials(self):
        total = {}
        for item in self.items:
            for name, n in self.catalog.by_name[item["p"]].get("cost", []):
                total[name] = total.get(name, 0) + n
        return dict(sorted(total.items(), key=lambda kv: -kv[1]))

    def check(self):
        """Problems and needs: stations to build near, pieces that need terrain, overlaps of pieces that may not overlap, fires on wood, crafting stations without a roof."""
        cat = self.catalog.by_name
        notes, problems = [], []
        stations = {}
        for item in self.items:
            st = cat[item["p"]].get("station")
            if st:
                stations[st] = stations.get(st, 0) + 1
        boxes = [world_box(i, cat[i["p"]]) for i in self.items]
        for st, n in stations.items():
            rng = self.catalog.stations.get(st, {}).get("range", 20)
            inside = [i for i in self.items if i["p"] == st]
            where = "it is in this blueprint: build it first" if inside else "put one within reach"
            notes.append(f"{n} pieces need a {st} within {rng:g} m ({where})")

        def overlap(a, b, shrink=0.15):
            return all(a[0][k] + shrink < b[1][k] and b[0][k] + shrink < a[1][k] for k in range(3))

        for i, item in enumerate(self.items):
            p = cat[item["p"]]
            rules = p.get("rules", {})
            if (p.get("groundOnly") or rules.get("groundPiece")) and not (item.get("g") and boxes[i][0][1] < 0.3):
                problems.append(f"#{i} {item['p']} must stand on the ground (give it g: true at y near 0)")
            if rules.get("noClipping"):
                for j, other in enumerate(self.items):
                    if j != i and overlap(boxes[i], boxes[j], 0.0) and pieces_overlap(item, p, other, cat[other["p"]]):
                        problems.append(f"#{i} {item['p']} may not overlap other pieces but overlaps #{j} {other['p']}")
                        break
            if rules.get("notOnWood"):
                below = [j for j, b in enumerate(boxes) if j != i and cat[self.items[j]["p"]].get("material") in ("Wood", "HardWood", "Timberwood")
                         and b[1][1] <= boxes[i][0][1] + 0.15 and b[1][1] >= boxes[i][0][1] - 0.3
                         and b[0][0] < boxes[i][1][0] and boxes[i][0][0] < b[1][0] and b[0][2] < boxes[i][1][2] and boxes[i][0][2] < b[1][2]]
                if below:
                    problems.append(f"#{i} {item['p']} cannot stand on wood but sits on #{below[0]} {self.items[below[0]]['p']}")
            station = p.get("isStation")
            if station and station.get("needsRoof"):
                roofed = any(j != i and b[0][1] > boxes[i][1][1] - 0.2 and b[0][1] < boxes[i][1][1] + 6
                             and b[0][0] < boxes[i][1][0] and boxes[i][0][0] < b[1][0] and b[0][2] < boxes[i][1][2] and boxes[i][0][2] < b[1][2]
                             for j, b in enumerate(boxes))
                if not roofed:
                    notes.append(f"#{i} {item['p']} needs a roof over it before you can craft at it")
        return {"pieces": len(self.items), "materials": self.materials(), "notes": notes, "problems": problems}

    def summary(self):
        """Print the check in a form a player can read; returns True when there are no problems."""
        c = self.check()
        print(f"{c['pieces']} pieces. Materials: " + ", ".join(f"{k} {v}" for k, v in c["materials"].items()))
        for n in c["notes"]:
            print("  note:", n)
        for p in c["problems"][:20]:
            print("  PROBLEM:", p)
        if len(c["problems"]) > 20:
            print(f"  ... and {len(c['problems']) - 20} more problems")
        return not c["problems"]

    def save(self, path):
        doc = dict(self.meta)
        doc["pieces"] = self.items
        with open(path, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=1)
        return len(self.items)
