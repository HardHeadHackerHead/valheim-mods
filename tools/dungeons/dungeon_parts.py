"""
Building blocks for the dungeon pack generators (shifting_halls.py, testmechs.py): a pack's own rooms as lists of the game's pieces and props,
their doorways, and their mechanisms (levers, gates, turning stairs...), written as the pack JSON the mod reads (mods/Dungeons/Packs.cs has
the format; the mechanism fields follow Model.cs in camelCase).

Coordinates are a room's own frame: x right, y up, z forward, the origin at the centre of the room's box (Room.m_size, as the game's rooms).
A doorway's "at" is the middle of its threshold (the floor of the opening), its facing points into the room. A mechanism's parts are in its
own frame (its "at" and "euler" in the room), and each pose moves them within it.

Prefab sizes come from prefab_bounds.json (the bounds of each prefab's visible shape, metres, in its own frame), filled from the game's files
by game_meshes.py the first time a prefab is used, so the generators also run on a PC without the game once it's filled.
"""
import json, math, os
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BOUNDS_FILE = os.path.join(HERE, "prefab_bounds.json")
_bounds = None


def bounds(prefab):
    """(min, max) of a prefab's shape in its own frame."""
    global _bounds
    if _bounds is None:
        _bounds = json.load(open(BOUNDS_FILE)) if os.path.exists(BOUNDS_FILE) else {}
    if prefab not in _bounds:
        from game_meshes import shape
        s = shape(prefab)
        if s is None:
            raise KeyError(f"the game has no prefab {prefab}")
        _bounds[prefab] = [[round(float(x), 3) for x in s[3]], [round(float(x), 3) for x in s[4]]]
        with open(BOUNDS_FILE, "w") as f:
            json.dump(dict(sorted(_bounds.items())), f, indent=0)
    lo, hi = _bounds[prefab]
    return np.array(lo), np.array(hi)


def euler_matrix(rx, ry, rz):
    """Unity's Quaternion.Euler (z, then x, then y) as a 3x3 matrix."""
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx, cy, sy, cz, sz = math.cos(ax), math.sin(ax), math.cos(ay), math.sin(ay), math.cos(az), math.sin(az)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def yaw_turn(v, yaw):
    """v turned about the up axis by yaw degrees (Unity: yaw 90 turns +z to +x)."""
    return euler_matrix(0, yaw, 0) @ np.array(v, float)


def r3(v):
    return [round(float(x), 3) + 0.0 for x in v]


class Part:
    def __init__(self, prefab, at, euler=(0, 0, 0), scale=(1, 1, 1), saved=False):
        self.prefab, self.at, self.euler, self.scale, self.saved = prefab, np.array(at, float), np.array(euler, float), np.array(scale, float), saved
        self.spin = None                # a mechanism's part only: degrees a second about its own axes (Runtime turns it)

    def json(self):
        o = {"prefab": self.prefab, "at": r3(self.at)}
        if np.any(np.abs(self.euler) > 1e-6):
            o["euler"] = r3(self.euler)
        if np.any(np.abs(self.scale - 1) > 1e-6):
            o["scale"] = r3(self.scale)
        if self.saved:
            o["saved"] = True
        if self.spin is not None:
            o["spin"] = [int(round(v)) for v in self.spin]
        return o


class Parts:
    """Something that holds parts (a room, or a mechanism's moving parts)."""

    def __init__(self):
        self.parts = []

    def add(self, prefab, at, euler=(0, 0, 0), scale=(1, 1, 1), saved=False):
        p = Part(prefab, at, euler, scale, saved)
        self.parts.append(p)
        return p

    def fill(self, prefab, centre, size, euler=(0, 0, 0), saved=False):
        """The prefab stretched to fill a box: centre and size in this frame, turned by euler (quarter turns: the box stays axis-aligned)."""
        lo, hi = bounds(prefab)
        R = euler_matrix(*euler)
        need = np.abs(R.T) @ np.array(size, float)
        own = np.maximum(hi - lo, 1e-3)
        scale = need / own
        at = np.array(centre, float) - R @ (scale * (lo + hi) / 2)
        return self.add(prefab, at, euler, scale, saved)

    def stand(self, prefab, at, yaw=0.0, scale=(1, 1, 1), saved=False):
        """The prefab with the bottom middle of its shape at `at`, turned by yaw."""
        lo, hi = bounds(prefab)
        s = np.array(scale, float)
        base = np.array([(lo[0] + hi[0]) / 2, lo[1], (lo[2] + hi[2]) / 2]) * s
        return self.add(prefab, np.array(at, float) - yaw_turn(base, yaw), (0, yaw, 0), scale, saved)


class Mech(Parts):
    def __init__(self, id, kind, at, euler=(0, 0, 0)):
        super().__init__()
        self.id, self.kind, self.at, self.euler = id, kind, np.array(at, float), np.array(euler, float)
        self.poses, self.wires, self.joins = [], [], []
        self.start, self.seconds, self.cycle, self.doorway, self.reached_from = 0, 1.5, 0.0, None, None

    def pose(self, at=(0, 0, 0), euler=(0, 0, 0)):
        self.poses.append((np.array(at, float), np.array(euler, float)))
        return self

    def wire(self, target, do="next"):
        self.wires.append({"target": target, "do": do})
        return self

    def join(self, a, b, state=-1):
        self.joins.append({"state": state, "a": a, "b": b})
        return self

    def json(self):
        o = {"id": self.id, "kind": self.kind, "at": r3(self.at)}
        if np.any(np.abs(self.euler) > 1e-6):
            o["euler"] = r3(self.euler)
        o["parts"] = [p.json() for p in self.parts]
        o["poses"] = [{"at": r3(a), "euler": r3(e)} for a, e in self.poses]
        o["start"] = self.start
        o["seconds"] = self.seconds
        if self.cycle:
            o["cycle"] = self.cycle
        if self.doorway:
            o["doorway"] = self.doorway
        if self.wires:
            o["wires"] = self.wires
        if self.joins:
            o["joins"] = self.joins
        if self.reached_from:
            o["reachedFrom"] = self.reached_from
        return o


class Room(Parts):
    def __init__(self, name, size, tags=(), weight=1.0, max=0, min_depth=0, entrance=False, end_cap=False):
        super().__init__()
        self.name, self.size, self.tags = name, np.array(size, float), list(tags)
        self.weight, self.max, self.min_depth, self.entrance, self.end_cap = weight, max, min_depth, entrance, end_cap
        self.doorways, self.mechs, self.inside = [], [], []
        self.extra = {}                 # more room fields, written before the doorways (e.g. "joinBy")

    def doorway(self, id, at, facing, type="dvergr", door=True, entrance=False):
        self.doorways.append({"id": id, "type": type, "at": r3(at), "facing": r3(facing), "door": door, "entrance": entrance})
        return self.doorways[-1]

    def door(self, id):
        return next(d for d in self.doorways if d["id"] == id)

    def mech(self, id, kind, at, euler=(0, 0, 0)):
        m = Mech(id, kind, at, euler)
        self.mechs.append(m)
        return m

    def join(self, a, b, state=-1):
        self.inside.append({"state": state, "a": a, "b": b})

    def json(self):
        o = {"name": self.name, "size": r3(self.size)}
        if self.tags:
            o["tags"] = self.tags
        if self.weight != 1.0:
            o["weight"] = self.weight
        for key, value in (("max", self.max), ("minDepth", self.min_depth)):
            if value:
                o[key] = value
        if self.entrance:
            o["entrance"] = True
        if self.end_cap:
            o["endCap"] = True
        o.update(self.extra)
        o["doorways"] = [dict((k, v) for k, v in d.items() if not (k == "entrance" and not v)) for d in self.doorways]
        if self.inside:
            o["inside"] = self.inside
        if self.mechs:
            o["mechs"] = [m.json() for m in self.mechs]
        o["parts"] = [p.json() for p in self.parts]
        return o

    # ---- for previews and checks

    def scene(self, scene, states=None, origin=(0, 0, 0), yaw=0.0, marks=True, tint=True):
        """Adds this room (its mechanisms in the given states: {id: state}, else their start) to a dungeon_view.Scene. tint: moving parts
        in blue and saved ones in gold, to tell them apart (off: as they look)."""
        from dungeon_view import frame
        room = frame(origin, (0, yaw, 0))
        for p in self.parts:
            scene.add(p.prefab, p.at, p.euler, p.scale, parent=room, tint=(1.0, 0.85, 0.3) if p.saved and tint else None)
        for m in self.mechs:
            state = (states or {}).get(m.id, m.start)
            mf = room @ frame(m.at, m.euler)
            if m.poses:
                a, e = m.poses[min(state, len(m.poses) - 1)]
                mf = mf @ frame(a, e)
            for p in m.parts:
                scene.add(p.prefab, p.at, p.euler, p.scale, parent=mf, tint=(0.45, 0.75, 1.0) if tint else None)
            if marks:
                scene.mark((room @ np.r_[m.at, 1])[:3], (120, 200, 255), f"{m.id} [{state}]")
        if marks:
            for d in self.doorways:
                scene.mark((room @ np.r_[d["at"], 1])[:3] + [0, 0.3, 0], (255, 210, 80), d["id"])
        return scene


# What a dungeon's spawners spawn, by the biome its entrance is in (pack "creatures"). The Builder swaps each spawner's creature for a seeded
# pick of the same role (melee, ranged, flying); a role or biome left out falls back to its own table. Every name is one of the game's prefabs.
_MEADOWS = {"melee": ["Greyling", "Skeleton_Meadows_noarcher"], "ranged": ["Skeleton_Meadows"]}
_FOREST = {"melee": ["Skeleton_NoArcher", "Greydwarf", "Greydwarf_Elite"], "ranged": ["Skeleton", "Greydwarf_Shaman"], "flying": ["Ghost"]}
CREATURES = {
    "Meadows": _MEADOWS,
    "BlackForest": _FOREST,
    "Swamp": {"melee": ["Draugr", "Draugr_Elite", "Skeleton_Swamps_noarcher", "Blob"], "ranged": ["Draugr_Ranged", "Skeleton_Swamps"],
              "flying": ["Wraith", "Bat_Swamp"]},
    "Mountain": {"melee": ["Ulv", "Fenring", "Skeleton_Mountains_noarcher"], "ranged": ["Fenring_Cultist", "Skeleton_Mountains"],
                 "flying": ["Bat", "Hatchling"]},
    "Plains": {"melee": ["Goblin", "GoblinBrute"], "ranged": ["GoblinArcher", "GoblinShaman"], "flying": ["Deathsquito"]},
    "Mistlands": {"melee": ["Seeker", "Tick", "SeekerBrute"], "flying": ["Seeker"]},
    "AshLands": {"melee": ["Charred_Melee", "Charred_Twitcher"], "ranged": ["Charred_Archer", "Charred_Mage"], "flying": ["Volture"]},
    "DeepNorth": {"melee": ["Skeleton_DeepNorth", "GoblinDeepNorth", "Greydwarf_Frozen"], "ranged": ["Greydwarf_Shaman_Frozen"], "flying": ["Bat"]},
    "Ocean": _MEADOWS,
    "default": _FOREST,
}


# What a dungeon's chests hold, by the biome its entrance is in (pack "loot"): "common" for ordinary chests, "rare" for our rooms' other
# saved chests, "vault" for the chest in the end room (the prize: worth the puzzle, fair for the land). Entries "Item", "Item:n" or
# "Item:min-max", all the game's own items of that land's tier or below (the Builder also drops anything above the tier).
_MEADOWS_LOOT = {"common": ["Coins:5-15", "Flint:2-5", "LeatherScraps:2-6", "Feathers:2-6", "Amber"],
                 "rare": ["Coins:15-30", "Amber:1-2", "DeerHide:2-4", "Flint:4-8"],
                 "vault": ["Coins:40-70", "Amber:2-3", "AmberPearl:1", "Ruby:1"]}
_FOREST_LOOT = {"common": ["Coins:10-20", "Amber", "Resin:3-6", "BoneFragments:3-6", "GreydwarfEye:2-4"],
                "rare": ["Coins:20-40", "AmberPearl", "Ruby", "SurtlingCore", "Copper:2-3", "Tin:2-3"],
                "vault": ["Coins:60-110", "Ruby:2-3", "AmberPearl:2-3", "SurtlingCore:2-3", "Bronze:3-5"]}
LOOT = {
    "Meadows": _MEADOWS_LOOT,
    "BlackForest": _FOREST_LOOT,
    "Swamp": {"common": ["Coins:15-30", "Amber", "AmberPearl", "WitheredBone", "Guck:2-4", "Entrails:2-4"],
              "rare": ["Coins:30-60", "IronScrap:3-6", "Chain", "Ruby", "AmberPearl:1-2"],
              "vault": ["Coins:90-150", "Iron:3-5", "IronScrap:6-10", "Chain:1-2", "Ruby:2-4", "AmberPearl:2-3"]},
    "Mountain": {"common": ["Coins:20-40", "Obsidian:3-6", "WolfFang:2-4", "Crystal:1-2", "Ruby"],
                 "rare": ["Coins:40-80", "SilverOre:3-6", "Crystal:2-4", "FreezeGland:2-4", "AmberPearl:1-2"],
                 "vault": ["Coins:120-200", "Silver:4-6", "Crystal:3-6", "Ruby:3-4", "AmberPearl:2-4", "SilverNecklace"]},
    "Plains": {"common": ["Coins:30-50", "BlackMetalScrap:2-4", "Barley:4-8", "Flax:4-8", "Needle:3-6"],
               "rare": ["Coins:60-100", "BlackMetalScrap:5-8", "LoxPelt:2-3", "SilverNecklace", "Ruby:1-2"],
               "vault": ["Coins:150-250", "BlackMetal:4-6", "BlackMetalScrap:6-10", "SilverNecklace:1-2", "Ruby:3-5", "AmberPearl:3-4"]},
    "Mistlands": {"common": ["Coins:40-70", "Softtissue:2-4", "Sap:3-6", "BlackMarble:4-8"],
                  "rare": ["Coins:80-120", "DvergrNeedle:2-4", "Softtissue:4-6", "SilverNecklace", "Ruby:2-3"],
                  "vault": ["Coins:200-300", "BlackCore:1-2", "DvergrNeedle:3-5", "Softtissue:6-10", "SilverNecklace:2", "Ruby:3-5"]},
    "AshLands": {"common": ["Coins:50-80", "Grausten:4-8", "CharredBone:3-6", "CharcoalResin:2-4"],
                 "rare": ["Coins:100-150", "FlametalOreNew:3-5", "AskHide:2-3", "ProustitePowder:2-3", "Ruby:2-3"],
                 "vault": ["Coins:250-400", "FlametalNew:3-5", "MoltenCore:1-2", "CelestialFeather:2-3", "Ruby:4-6", "SilverNecklace:2"]},
    "DeepNorth": {"common": ["Coins:50-80", "Grausten:4-8", "Obsidian:4-8", "Crystal:2-4"],
                  "rare": ["Coins:100-150", "Crystal:4-6", "Ruby:2-3", "SilverNecklace"],
                  "vault": ["Coins:250-400", "Ruby:4-6", "SilverNecklace:2", "AmberPearl:4-6", "Crystal:6-10"]},
    "Ocean": _MEADOWS_LOOT,
    "default": _FOREST_LOOT,
}

# ---------------------------------------------------------------- geometry shared by the generators

def side_point(k, lateral, y, depth):
    """A point on side k of a room (0 north +z, 1 east +x, 2 south, 3 west): `lateral` along the side (seen from inside, to the right for
    north: +x), `depth` from the middle. Side k is the north side turned k quarter turns."""
    return yaw_turn((lateral, y, depth), 90 * k)


def cuts(lo, hi, edges, cell):
    """lo..hi cut at every edge, and every piece longer than cell cut again: [(a, b), ...]."""
    xs = sorted({lo, hi} | {e for e in edges if lo < e < hi})
    out = [xs[0]]
    for x in xs[1:]:
        n = int(np.ceil((x - out[-1]) / cell - 1e-6))
        step = (x - out[-1]) / n
        out += [out[-1] + step * (i + 1) for i in range(n)]
    return list(zip(out[:-1], out[1:]))


def wall(room, k, half, y0, y1, openings, prefab, thickness=1.0, cell=8.0, size=(4.0, 4.0), across=None):
    """Side k's wall, its outer face on the box (`half` from the middle), from y0 to y1, `across` either side of the middle (default
    half), with openings [(lateral, threshold)] of size (width, height). Slabs of `prefab` (a floor tile stood up) over a grid cut at the
    openings' edges, cells merged into slabs up to `cell` metres."""
    w, h = size
    across = half if across is None else across
    cols = cuts(-across, across, [e for l, _ in openings for e in (l - w / 2, l + w / 2)], cell / 2)
    rows = cuts(y0, y1, [e for _, t in openings for e in (t, t + h)], cell / 2)

    def open_cell(c, r):
        x, y = (cols[c][0] + cols[c][1]) / 2, (rows[r][0] + rows[r][1]) / 2
        return any(abs(x - l) < w / 2 and t < y < t + h for l, t in openings)

    solid = {(c, r) for c in range(len(cols)) for r in range(len(rows)) if not open_cell(c, r)}
    while solid:
        c, r = min(solid, key=lambda cr: (cr[1], cr[0]))
        r1 = r
        while (c, r1 + 1) in solid and rows[r1 + 1][1] - rows[r][0] <= cell + 1e-6:
            r1 += 1
        c1 = c
        while c1 + 1 < len(cols) and all((c1 + 1, rr) in solid for rr in range(r, r1 + 1)) and cols[c1 + 1][1] - cols[c][0] <= cell + 1e-6:
            c1 += 1
        for cc in range(c, c1 + 1):
            for rr in range(r, r1 + 1):
                solid.discard((cc, rr))
        a, b, lo, hi = cols[c][0], cols[c1][1], rows[r][0], rows[r1][1]
        centre = side_point(k, (a + b) / 2, (lo + hi) / 2, half - thickness / 2)
        room.fill(prefab, centre, np.abs(yaw_turn((b - a, hi - lo, thickness), 90 * k)), euler=(-90, 90 * k, 0))   # its top inwards


def slab(room, x0, x1, z0, z1, top, prefab, thickness=1.0, cell=8.0, ceiling=False):
    """A floor over a rectangle (room frame), its top at `top`, in tiles of at most cell x cell metres. A ceiling is turned over, so the
    tiles' face looks down into the room."""
    for a, b in cuts(x0, x1, [], cell):
        for c, d in cuts(z0, z1, [], cell):
            room.fill(prefab, ((a + b) / 2, top - thickness / 2, (c + d) / 2), (b - a, thickness, d - c), euler=(180, 0, 0) if ceiling else (0, 0, 0))


def box(holder, prefab, centre, size, yaw=0.0, tilt=(0.0, 0.0)):
    """The prefab stretched to a box of `size` (x, y, z in the box's own frame), turned by yaw about the up axis (any angle), and tipped
    by tilt (x, z degrees) first. A flat prefab (a floor tile) keeps its thin axis as the box's y."""
    lo, hi = bounds(prefab)
    own = np.maximum(hi - lo, 1e-3)
    scale = np.array(size, float) / own
    euler = np.array([tilt[0], yaw, tilt[1]])
    R = euler_matrix(*euler)
    at = np.array(centre, float) - R @ (scale * (lo + hi) / 2)
    return holder.add(prefab, at, euler, scale)


def lever(room, id, at, yaw, wires, reached_from=None, handle="piece_dvergr_pole", knob="blackmarble_head01",
          post="blackmarble_column_1", cap="blackmarble_2x2x1", size=1.0):
    """A lever: a post (the room's) with a handle that tips forward or back (the mechanism's part), `size` times the human scale. The side
    a player pulls it from is -z turned by yaw."""
    h = 1.1 * size
    room.fill(post, (at[0], at[1] + h / 2, at[2]), (0.6 * size, h, 0.6 * size))
    room.fill(cap, (at[0], at[1] + h + 0.05 * size, at[2]), (0.8 * size, 0.1 * size, 0.8 * size))
    m = room.mech(id, "lever", (at[0], at[1] + h + 0.05 * size, at[2]), (0, yaw, 0))
    m.fill(handle, (0, 0.5 * size, 0), (0.14 * size, 1.0 * size, 0.14 * size))
    m.fill(knob, (0, 1.05 * size, 0), (0.26 * size, 0.26 * size, 0.26 * size))
    m.pose(euler=(-35, 0, 0)).pose(euler=(35, 0, 0))
    m.seconds = 0.6
    for target, do in wires:
        m.wire(target, do)
    m.reached_from = reached_from
    return m


class Pack:
    def __init__(self, name, title, **fields):
        self.name, self.title, self.fields, self.rooms, self.plan = name, title, fields, [], []

    def room(self, room):
        self.rooms.append(room)
        return room

    def step(self, tag, id=None, opens_with=None):
        s = {"tag": tag}
        if id:
            s["id"] = id
        if opens_with:
            s["opensWith"] = opens_with
        self.plan.append(s)

    def json(self):
        o = {"name": self.name, "title": self.title}
        o.update({k: v for k, v in self.fields.items() if k not in ("creatures", "loot")})
        o["rooms"] = [r.json() if isinstance(r, Room) else r for r in self.rooms]
        o["plan"] = self.plan
        o["creatures"] = self.fields.get("creatures", [])
        o["loot"] = self.fields.get("loot", [])
        return o

    def write(self, path):
        """The pack's JSON: one room or part per line where it fits, so diffs stay readable."""
        text = json.dumps(self.json(), indent=1)
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(_compact(text) + "\n")
        return path


def _compact(text):
    """json.dumps(indent=1) with every block that fits in 150 characters folded onto one line (parts, poses, doorways, vectors)."""
    lines, result, i = text.split("\n"), [], 0
    while i < len(lines):
        line = lines[i]
        if line.rstrip().endswith(("{", "[")):
            j, depth, block = i, 0, []
            while j < len(lines):
                t = lines[j].strip()
                block.append(t)
                depth += t.count("{") + t.count("[") - t.count("}") - t.count("]")
                if depth == 0:
                    break
                j += 1
            one = " ".join(block).replace("[ ", "[").replace(" ]", "]").replace("{ ", "{").replace(" }", "}")
            indent = line[: len(line) - len(line.lstrip())]
            if len(indent) + len(one) <= 150:
                result.append(indent + one)
                i = j + 1
                continue
        result.append(line)
        i += 1
    return "\n".join(result)
