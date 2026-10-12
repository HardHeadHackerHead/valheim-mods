"""
Room recipes: functions that MAKE rooms. A recipe is a job (what the room is for: a fight, a key, a held gate...) built in a shell
(rect, L, round, split level, balcony storey) of a size, with features (pillars, cover, machinery, remains) and dressing (props,
lights) on top, all through a theme. Every choice comes from a seeded random source, so each seed is a different room, and the same
job in another shell with other features is a genuinely different room.

    room, info = make("fight", theme, seed=3)          # info: what was chosen (for the variety report)

Order (see core.py): shell -> doorways -> walls/levels -> the job's mechanisms and what they need clear -> the ways between doorways
-> features -> dressing -> lights -> ambient enemies. Things that only look go on FREE cells, so they never block a way.
"""
import math
import os

import numpy as np

from dungeon_parts import box

from .core import JUMP_UP, Vary, rng_for, pick, side_xz, side_vec, FREE, WAY, TAKEN, OUT, CELL
from .shells import Shell

JOBS = {}
DIFFICULTY = "normal"           # easy | normal | hard: the dungeon spec's; fights' waves follow it


class Job:
    """A room's purpose. Subclasses set the class fields and override doors() and build()."""
    name = "job"
    tags = ()
    size = "medium"                    # size class (theme.sizes)
    shapes = ("rect", "L", "round", "split", "balcony")
    storeys = 1                        # box height in storeys (a split or balcony shell adds what it needs)

    def storeys_in(self, theme):
        return self.storeys
    weight = 1.0
    max = 0
    min_depth = 0
    end_cap = False
    features = ("pillars", "machinery", "remains", "cover")
    dressing = True
    lights = True
    ambient = (0, 2)                   # ambient enemies (spawners): min, max
    door_ids = ("a", "b")
    pattern = "any"                    # how its doorways sit: through (opposite sides), turn (adjacent), any (different sides)
    side_jobs = ()                     # side jobs it may take (sidejobs.py: guard, sentries, stash, alcove), and how often
    side_chance = 0.0

    def doors(self, c):
        pick_doors(c, self.door_ids, self.pattern)

    def build(self, c):
        pass

    def targets(self, c):
        """Points (with their level) that must be reachable from doorway `target_from` (default the first) besides the other
        doorways: [(level, point)]."""
        return []

    target_from = None

    def connect(self, c):
        """Reserves the ways the room needs between its doorways (default: all of them with each other)."""
        return c.shell.connect()


def job(cls):
    JOBS[cls.name] = cls()
    return cls


class Ctx:
    """One room being made: the theme, the random source, the shell, what was chosen."""

    def __init__(self, theme, rng, shell, job):
        self.theme, self.rng, self.shell, self.job = theme, rng, shell, job
        self.room, self.plan = shell.room, shell.plan
        self.info = {"recipe": job.name, "shell": shell.shape, "features": [], "params": dict(shell.params)}
        self.points = []                                   # ambient spawn points placed (kept apart)
        self.vary = Vary(rng)                              # (make() puts the instance's own here)

    def note(self, key, value):
        self.info["params"][key] = value


def make(job_name, theme, seed, name=None, vi=None, **over):
    """A room of this job in this theme from this seed: (Room, info). vi: the instance's index among the rooms of this job in a
    pack (core.Vary: consecutive instances differ in every choice)."""
    jb = JOBS[job_name]
    rng = rng_for(theme.name, job_name, seed)
    vary = Vary(rng, vi)
    shapes = over.get("shapes") or theme.reshape.get(job_name, {}).get("shapes") or jb.shapes
    weights = {s: theme.shells.get(s, 0) for s in shapes}
    if not any(weights.values()):
        weights = {shapes[0]: 1}
    why = {}

    def failed(stage):
        why[stage] = why.get(stage, 0) + 1

    order = sorted(s for s, w in weights.items() if w > 0)
    for attempt in range(40):
        # an instance keeps its own shape and choices for a few tries (the random placements differ each time), then the next shape;
        # only then other choices (a refusal is mostly where things landed, not what was chosen)
        tries = 6
        if vi is not None and attempt < tries * len(order):
            shape = order[(vi + vary._off(job_name) + attempt // tries) % len(order)]
            vary = Vary(rng, vi)
        else:
            shape = pick(rng, weights)
            vary = Vary(rng, None if vi is None else vi + 7 * (attempt // tries))
        size = choose_size(theme, jb, shape, rng, vary)
        nm = name or f"{job_name}_{shape}_{seed}"
        try:
            if hasattr(jb, "make_shell"):
                nm = name or f"{job_name}_{seed}"
                shell = jb.make_shell(theme, nm, rng, vary)
            else:
                shell = Shell(theme, nm, shape, size, rng, tags=jb.tags, weight=jb.weight, max=jb.max, min_depth=jb.min_depth,
                              end_cap=jb.end_cap)
            c = Ctx(theme, rng, shell, jb)
            c.vary = vary
            jb.doors(c)
        except NoFit:
            failed("shell or doorways")
            continue
        shell.build()
        try:
            jb.build(c)
        except NoFit:
            failed("job")
            continue
        if not jb.connect(c):
            failed("ways between doorways")
            continue
        frm = jb.target_from or jb.door_ids[0]
        if not all(shell.way_between(shell.door_at(frm, 2.0), shell.doors[frm].level, p, lv) for lv, p in jb.targets(c)):
            failed("ways to the job's targets")
            continue
        try:
            if hasattr(jb, "after_ways"):
                jb.after_ways(c)
        except NoFit:
            failed("after the ways (spawn points...)")
            continue
        finish(c)
        stray = through_walls(c)
        if stray:
            failed("a part through a wall (" + stray[0] + ")")
            continue
        c.info["name"] = nm
        c.info["seed"] = seed
        return shell.room, c.info
    raise RuntimeError(f"{job_name}: no {theme.name} room fitted in 40 tries (seed {seed}): {why}")


class NoFit(Exception):
    pass


def choose_size(theme, jb, shape, rng, vary=None):
    (ax, bx), (az, bz) = theme.sizes[jb.size]
    g = theme.grid
    vary = vary or Vary(rng)
    sx = vary.integer("size x", ax, bx) * g
    sz = vary.integer("size z", az, bz) * g
    if shape == "round":
        sx = sz = max(sx, sz, 2 * g)
    if shape in ("L",):
        sx, sz = max(sx, 2 * g), max(sz, 2 * g)
    storeys = jb.storeys_in(theme) + (1 if shape == "balcony" else 0)
    sy = theme.height * storeys
    if shape == "balcony":                                # (room for the gallery's stair, with floor at its foot, whichever side)
        need = math.ceil((theme.stair_run(theme.storey) + theme.door_w + 1.0 + 3.0 + 2 * theme.wall_t + 1.0) / g) * g
        sx, sz = max(sx, need), max(sz, need)
    if shape == "split":
        sy = theme.height + theme.storey
        sz = max(sz, math.ceil((theme.stair_run(theme.storey) + 3.0 + max(5.0, theme.door_w) + 2 * theme.wall_t + 1.0) / theme.grid)
                 * theme.grid)
    return (sx, sy, sz)


def pick_doors(c, ids, pattern="any", levels=None):
    """Puts doorways `ids` on the shell's slots: `through` = first two on opposite sides, `turn` = adjacent sides, `any` = different
    sides where it can. levels: {id: level} to keep one on a given floor level."""
    slots = c.shell.slots()
    rng = c.rng
    levels = levels or {}
    if not slots:
        raise NoFit()
    for _ in range(60):
        chosen = []
        for i, id in enumerate(ids):
            cand = [s for s in slots if all(s[0] != o[0] for o in chosen)] or [s for s in slots if all(s[:2] != o[:2] for o in chosen)]
            if id in levels:
                cand = [s for s in cand if s[2] == levels[id]]
            if i == 1 and pattern == "through":
                cand = [s for s in cand if s[0] == (chosen[0][0] + 2) % 4]
            if i == 1 and pattern == "turn":
                cand = [s for s in cand if (s[0] - chosen[0][0]) % 2 == 1]
            if not cand:
                break
            # prefer the middle of a side a little, and a level spread
            chosen.append(rng.choice(cand))
        if len(chosen) == len(ids):
            for id, (k, lat, lv) in zip(ids, chosen):
                c.shell.add_door(id, k, lat, lv)
            c.note("doors", [[k, lat, lv] for k, lat, lv in chosen])
            return
    raise NoFit()


# ---------------------------------------------------------------- features, dressing, lights

def spawn_ways(c, width=1.6):
    """Keeps a way (1.6 m wide) from every spawn point to a doorway, before anything is put down: what comes out of a spawn point
    must be able to walk to the player (props and cover round it would make it an island)."""
    sh = c.shell
    for m in c.room.mechs:
        if m.kind != "encounter":
            continue
        for p in m.extra.get("points", []):
            p = np.array(p, float)
            lv = int(np.argmin([abs(y - p[1]) for y in sh.levels]))
            doors = sorted(sh.doors.values(), key=lambda d: (d.id != m.reached_from, d.level != lv))
            if doors:
                d = doors[0]
                sh.way_between(p, lv, sh.door_at(d.id, 2.0), d.level, width=width)


def through_walls(c):
    """The parts that leave the room or pass through its walls: every part but the shell's floors, walls, ceilings and solid blocks
    (an L's notch and a round room's corners are walled off, and the floor and ceiling run under them), sampled along its box every
    0.5 m; a sample beyond the walls' outer face (the room's shape, the wall's thickness and 0.3 m on) is out. [prefab names]"""
    import json as _json
    from dungeon_parts import euler_matrix
    global _BOUNDS
    try:
        _BOUNDS
    except NameError:
        _BOUNDS = _json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "prefab_bounds.json")))
    t, sh = c.theme, c.shell
    skip = {t.p(role) for role in ("wall", "floor", "ceiling") if t.p(role)} | set(getattr(t, "structure_prefabs", ()))
    out = []
    H = np.array(sh.size) / 2
    for p in c.room.parts:
        if p.prefab in skip or p.prefab not in _BOUNDS:
            continue
        lo, hi = (np.array(v) for v in _BOUNDS[p.prefab])
        R = euler_matrix(*p.euler)
        sc = np.array(p.scale, float)
        n = [max(1, int(np.ceil((hi[i] - lo[i]) * abs(sc[i]) / 0.5))) for i in range(3)]
        for u in np.linspace(lo[0], hi[0], n[0] + 1):
            for w in np.linspace(lo[2], hi[2], n[2] + 1):
                q = np.array(p.at, float) + R @ (sc * np.array([u, (lo[1] + hi[1]) / 2, w]))
                # (a round room's corners are behind its wall and inside its box: hidden from both sides, only the box counts there)
                if (np.abs(q[[0, 2]]) > H[[0, 2]] + 0.3).any() or                         (sh.shape != "round" and not sh.in_shape(q[0], q[2], margin=-(t.wall_t + 0.3))):
                    out.append(p.prefab)
                    break
            else:
                continue
            break
    return out


def finish(c):
    jb, t = c.job, c.theme
    if jb.side_jobs and c.vary.flag("side job", jb.side_chance):
        from .sidejobs import add_side_job
        add_side_job(c, jb.side_jobs)
    spawn_ways(c, 1.0)                 # (a narrow way first, that the big features keep clear of...)
    if jb.features:
        add_features(c, jb.features)
    spawn_ways(c)                      # (...then the full width round them, before the many small props)
    if jb.dressing:
        dress(c)
    if jb.lights:
        light(c)
    lo, hi = jb.ambient
    if hi:
        ambient(c, c.rng.randint(lo, hi))


def add_features(c, allowed):
    rng, t, sh = c.rng, c.theme, c.shell
    area = (sh.inner[1] - sh.inner[0]) * (sh.inner[3] - sh.inner[2])
    chosen = [f for f in allowed if f in getattr(c.job, "always", ()) or c.vary.flag(f, 0.5)]
    one_of = [f for f in getattr(c.job, "one_of", ()) if f in allowed]
    if one_of and not any(f in chosen for f in one_of):      # (a job that needs one of these always gets one)
        chosen.append(c.vary.choice("needed feature", one_of))
    _place_features(c, chosen, area)
    # what the job needs and didn't get (no room for it where it was tried): the others of its kind
    if one_of and not any(f in c.info["features"] for f in one_of):
        _place_features(c, [f for f in one_of if f not in chosen], area)


def _place_features(c, chosen, area):
    rng, t, sh = c.rng, c.theme, c.shell
    for f in chosen:
        if f == "pillars":
            n = pillars(c, area)
            if n:
                c.info["features"].append("pillars")
                c.note("pillars", n)
        elif f == "machinery" and hasattr(t, "big_gear_wall"):
            n = machinery(c)
            if n:
                c.info["features"].append("machinery")
                c.note("gears", n)
        elif f == "remains" and hasattr(t, "remains") and area > 300:
            n = remains(c)
            if n:
                c.info["features"].append("remains")
        elif f == "cover":
            n = cover(c)
            if n:
                c.info["features"].append("cover")
                c.note("cover", n)


def pillars(c, area):
    """Pillars standing floor to ceiling, in a pattern: pairs in rows, or a ring in a round room, or scattered."""
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    h = sh.top - t.floor_t - sh.floor_y if sh.shape not in ("split", "balcony") else sh.levels[1] - sh.floor_y
    w = t.door_w * 0.5
    placed = 0
    pattern = "ring" if sh.shape == "round" else c.vary.choice("pillar pattern", ("rows", "scatter", "rows"))
    c.note("pillar_pattern", pattern)
    if pattern == "ring":
        n = rng.choice((4, 6, 8))
        off = rng.uniform(0, 360 / n)
        # (a ring that finds the ways in its path tries another radius)
        for f in (rng.uniform(0.45, 0.62), 0.7, 0.35):
            if placed >= 3:
                break
            R = (sh.half[0] - t.wall_t) * f
            for i in range(n):
                a = math.radians(off + 360.0 * i / n)
                p = (math.sin(a) * R, sh.floor_y, math.cos(a) * R)
                if plan.take(0, p, (w, w), margin=0.3):
                    t.pillar(c.room, p, h, w)
                    placed += 1
    elif pattern == "rows":
        x0, x1, z0, z1 = sh.inner
        along_x = (x1 - x0) >= (z1 - z0)
        n = max(2, int(((x1 - x0) if along_x else (z1 - z0)) / (t.grid * rng.choice((1.0, 1.5)))))
        inset = rng.uniform(0.22, 0.32)
        for i in range(n):
            u = (i + 0.5) / n
            for s in (-1, 1):
                if along_x:
                    p = (x0 + u * (x1 - x0), sh.floor_y, s * (z1 - z0) / 2 * (1 - 2 * inset))
                else:
                    p = (s * (x1 - x0) / 2 * (1 - 2 * inset), sh.floor_y, z0 + u * (z1 - z0))
                if sh.on_level(p[0], p[2], 0) and plan.take(0, p, (w, w), margin=0.3):
                    t.pillar(c.room, p, h, w)
                    placed += 1
    if pattern == "scatter" or placed == 0:            # (and where a ring or rows found no room: wherever there is some)
        for x, z in plan.spots(0, (w, w), rng, margin=0.8, wall=False, count=rng.randint(2, 4), gap=t.grid * 0.75):
            p = (x, sh.floor_y, z)
            if plan.take(0, p, (w, w), margin=0.3):
                t.pillar(c.room, p, h, w)
                placed += 1
    return placed


def machinery(c):
    """Great gears against the walls, turning (a machine), or one lying on an axle in the middle."""
    t, sh, rng = c.theme, c.shell, c.rng
    if sh.shape == "round":
        return 0
    m = None
    n = 0
    for k in rng.sample(range(4), 4):
        if n >= 2:
            break
        depth = sh.half[2] if k % 2 == 0 else sh.half[0]
        across = sh.half[0] if k % 2 == 0 else sh.half[2]
        lat = rng.uniform(-across * 0.3, across * 0.3)
        if any(d.k == k and abs(d.lateral - lat) < t.door_w + 4 for d in sh.doors.values()):
            continue
        if sh.notch and not sh.in_shape(*side_xz(k, lat, depth - t.wall_t - 1.0)):
            continue
        h = sh.top - t.floor_t - sh.floor_y
        dia = min(rng.uniform(0.35, 0.55) * h, h - 2.4 - 0.6)       # (over the heads, under the ceiling: a low room has a small one)
        y = sh.floor_y + 2.4 + dia / 2
        if dia < 1.2:
            continue
        m = m or t.machine(c.room, "gears")
        t.big_gear_wall(c.room, m, k, lat, y, depth - t.wall_t - 0.4, dia, rng.choice((-1, 1)) * rng.uniform(8, 18))
        n += 1
    return n


def remains(c):
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    n = 0
    for prefab, size, sink in rng.sample(t.remains(), 1):
        yaw = rng.choice((0, 90, 180, 270)) + rng.uniform(-20, 20)
        spots = plan.spots(0, (size[0], size[2]), rng, margin=0.5, wall=True, count=1, yaw=yaw)
        for x, z in spots:
            from dungeon_parts import box
            if plan.take(0, (x, 0, z), (size[0], size[2]), yaw=yaw, margin=0.5):
                box(c.room, prefab, (x, sh.floor_y + size[1] / 2 - sink, z), size, yaw=yaw)
                n += 1
    return n


def cover(c):
    """Low blocks to fight round (waist high): never near a balcony or a high level's edge."""
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    if len(sh.levels) > 1:
        return 0
    n = 0
    size = (t.door_w * 0.5, t.door_w * 0.25)
    spots = plan.spots(0, size, rng, margin=1.0, wall=False, count=rng.randint(2, 3), gap=t.grid) or         plan.spots(0, size, rng, margin=0.5, wall=False, count=rng.randint(2, 3), gap=t.grid * 0.6)
    for x, z in spots:
        yaw = rng.choice((0, 90))
        if plan.take(0, (x, 0, z), size, yaw=yaw, margin=0.5):
            from dungeon_parts import box
            box(c.room, t.p("block"), (x, sh.floor_y + 0.6, z), (size[0], 1.2, size[1]), yaw=yaw)
            n += 1
    return n


def dress(c):
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    sets = t.dressing_sets()
    if not sets:
        return
    name = c.vary.choice("dressing", sorted(sets))
    c.note("dressing", name)
    items = [d for d in t.decor() if name in d["sets"]]
    # in a room with a higher level, nothing tall enough to climb onto it from (a prop's top is a place to stand; 1.5 m a jump)
    climb = max_prop_height(c)
    items = [d for d in items if prop_height(d["prefab"], d["scale"]) <= climb]
    area = (sh.inner[1] - sh.inner[0]) * (sh.inner[3] - sh.inner[2])
    n = int(min(len(items) * 2, max(2, int(area / (t.grid * t.grid * 0.5)))) * getattr(t, "dense", 1.0))
    placed = 0
    for i in range(n * 4):
        if placed >= n or not items:
            break
        d = rng.choice(items)
        lv = rng.randrange(len(sh.levels))
        size = real_size(d["prefab"], d["scale"])
        spots = plan.spots(lv, (max(size), max(size)), rng, margin=0.3, wall=d["wall"], count=1)
        if not spots:
            continue
        x, z = spots[0]
        yaw = _face_room(sh, x, z) if d["wall"] else rng.choice((0, 90, 180, 270)) + rng.uniform(-10, 10)
        if plan.take(lv, (x, 0, z), size, yaw=yaw, margin=0.3):
            s = d["scale"]
            c.room.stand(d["prefab"], (x, sh.levels[lv], z), yaw=yaw, scale=(s, s, s))
            placed += 1
    c.note("props", placed)


def prop_height(prefab, scale=1.0):
    from dungeon_parts import bounds
    lo, hi = bounds(prefab)
    return (hi[1] - lo[1]) * scale


def max_prop_height(c):
    """How tall a prop may be in this room: below the lowest rise to a higher level less a jump and a margin (no limit in a room of
    one level)."""
    if getattr(c, "prop_limit", None) is not None:
        return c.prop_limit
    lv = sorted(set(c.shell.levels))
    if len(lv) < 2:
        return 99.0
    rise = min(b - a for a, b in zip(lv, lv[1:]))
    return rise - JUMP_UP - 0.6


def real_size(prefab, scale=1.0):
    """A prefab's footprint (x, z) at a scale, from its box."""
    from dungeon_parts import bounds
    lo, hi = bounds(prefab)
    return ((hi[0] - lo[0]) * scale, (hi[2] - lo[2]) * scale)


def _face_room(sh, x, z):
    """A yaw that turns a prop's front (its +z... the game's props face -z) away from the nearest wall."""
    dists = {0: sh.inner[3] - z, 1: sh.inner[1] - x, 2: z - sh.inner[2], 3: x - sh.inner[0]}
    k = min(dists, key=dists.get)
    return (90 * k + 180) % 360


def light(c):
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    area = (sh.inner[1] - sh.inner[0]) * (sh.inner[3] - sh.inner[2])
    stands = max(2, min(6, int(area / (t.grid * t.grid * 1.2) * getattr(t, "dense", 1.0))))
    placed = 0
    for lv in range(len(sh.levels)):
        for x, z in plan.spots(lv, (1.4, 1.4), rng, margin=0.2, wall=True, count=stands if lv == 0 else 2, gap=t.grid * 0.9):
            if plan.take(lv, (x, 0, z), (1.4, 1.4)):
                t.light_stand(c.room, (x, sh.levels[lv], z))
                placed += 1
    hang = max(1, int(area / (t.grid * t.grid * 2.5) * getattr(t, "dense", 1.0)))
    if getattr(sh, "no_climb", False):
        hang = 0                                           # (a lamp's chain is a climb to the ceiling)
    hung = 0
    # (hung over the floor, so over the ways too, but for rooms whose heights are the puzzle: nothing up there to step onto)
    over = (FREE,) if getattr(sh, "no_climb", False) else (FREE, WAY)
    for x, z in (plan.spots(0, (1.0, 1.0), rng, margin=0.0, wall=False, count=hang, gap=t.grid, allowed=over) if hang else []):
        y = sh.levels[-1] + max(3.2, min(3.5, (sh.top - sh.levels[-1]) * 0.45)) if len(sh.levels) == 1 else sh.levels[1] + 3.2
        if sh.top - 1.5 > y:
            t.light_hang(c.room, (x, y, z))
            hung += 1
    c.note("lights", [placed, hung])


def ambient(c, n):
    t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
    roles = ["melee", "melee", "ranged", "flying"]
    for x, z in plan.spots(0, (1.0, 1.0), rng, margin=0.5, count=n, gap=4.0,
                           avoid=[sh.door_at(d, 0)[[0, 2]] for d in sh.doors]):
        if min(math.hypot(x - p[0], z - p[2]) for p in [sh.door_at(d, 0) for d in sh.doors]) < 4.0:
            continue
        t.spawner(c.room, rng.choice(roles), (x, sh.floor_y, z))


# ---------------------------------------------------------------- plain rooms

@job
class Breather(Job):
    """A room to walk through: two or three doorways, a shell, features and a dressing set (an ossuary, a forge, an archive...)."""
    name = "breather"
    size = "medium"
    weight = 1.5
    max = 1                            # (each room once in a dungeon: the next one along is another room)
    door_ids = ("a", "b")
    side_jobs = ("guard", "sentries", "stash", "alcove")
    side_chance = 0.95

    def doors(self, c):
        ids = ("a", "b", "c") if c.vary.flag("three doorways", 0.4) and c.shell.shape != "split" else ("a", "b")
        self.door_ids = ids
        pick_doors(c, ids, c.vary.choice("door pattern", ("through", "turn", "any")))


@job
class PillarHall(Breather):
    """A walk-through hall built round its pillars or columns (rows, a ring, a scatter), dressed; a side job."""
    name = "pillarhall"
    shapes = ("rect", "round", "L")
    features = ("pillars", "remains")
    always = ("pillars",)
    weight = 1.5
    max = 1


@job
class MachineRoom(Breather):
    """A walk-through room given over to the machine: great gears on the walls, cover, dressing; a side job."""
    name = "machineroom"
    shapes = ("rect", "L", "split")
    features = ("machinery", "cover")
    always = ("machinery",)
    weight = 1.5
    max = 1


@job
class Corridor(Job):
    """A passage: plain, under arches, or under the machine's hammers (stone blocks that rise and fall on their own, stopping well
    above a head)."""
    name = "corridor"
    size = "corridor"
    shapes = ("rect",)
    weight = 1.5
    max = 1                            # (a pack's corridors differ; the same one twice close together reads as a loop)
    features = ("pillars", "machinery")
    pattern = "through"
    ambient = (0, 1)

    def build(self, c):
        t, sh, rng, r = c.theme, c.shell, c.rng, c.room
        variant = c.vary.choice("corridor", ("plain", "arches", "hammers"))
        c.note("variant", variant)
        x0, x1, z0, z1 = sh.inner
        along_z = (z1 - z0) >= (x1 - x0)
        length = (z1 - z0) if along_z else (x1 - x0)
        width = (x1 - x0) if along_z else (z1 - z0)
        n = max(1, int(length // (2 * t.grid)))
        for i in range(n):
            u = -length / 2 + (i + 0.5) * length / n
            if variant == "arches":
                y = sh.floor_y + t.door_h + 0.8
                if y + 0.6 < sh.top - t.floor_t:
                    size = (width, 0.6, 0.8) if along_z else (0.8, 0.6, width)
                    r.fill(t.p("trim"), (0 if along_z else u, y, u if along_z else 0), size)
            elif variant == "hammers":
                head = 2.4                                       # the hammers' foot never comes lower than this over the floor
                hh = min(t.door_h * 0.35, sh.top - t.floor_t - sh.floor_y - head - 1.0)
                if hh < 0.8:
                    continue
                at = (0 if along_z else u, sh.top - t.floor_t - hh / 2 - 0.1, u if along_z else 0)
                h = r.mech(f"hammer{i + 1}", "platform", at)
                box(h, t.p("block"), (0, 0, 0), (width * 0.7, hh, min(t.grid * 0.4, 3.0)) if along_z else
                    (min(t.grid * 0.4, 3.0), hh, width * 0.7))
                drop = (sh.top - t.floor_t - 0.1 - hh) - (sh.floor_y + head)
                h.pose().pose(at=(0, -drop, 0))
                h.cycle = rng.choice((4.0, 5.0, 6.0))
                h.seconds = 0.9
        c.note("bays", n)

    def doors(self, c):
        # a long room: doorways in its short ends
        sh = c.shell
        k = 0 if sh.size[2] >= sh.size[0] else 1
        sh.add_door("a", k + 2, 0.0, 0)
        sh.add_door("b", k, 0.0, 0)
        c.note("doors", [[k + 2, 0, 0], [k, 0, 0]])


@job
class Hub(Job):
    """Four ways meet: a crossing, round or square, machinery in the middle."""
    name = "hub"
    tags = ("hub",)
    shapes = ("rect", "round", "balcony")
    weight = 1.2
    door_ids = ("n", "e", "s", "w")
    features = ("machinery", "pillars")
    side_jobs = ("sentries", "guard", "stash", "alcove")
    side_chance = 0.95

    def doors(self, c):
        sh = c.shell
        want = {k: c.vary.choice(f"lateral {k}", (0.0, -1.0, 1.0)) * (c.theme.grid / 2 if sh.shape == "rect" else 0) for k in range(4)}
        slots = sh.slots()
        lats = {}
        for k in range(4):                    # the doorway's slot on side k nearest the one wanted (on the floor, clear of a stair)
            ok = [lat for kk, lat, lv in slots if kk == k and lv == 0]
            if not ok:
                raise NoFit()
            lats[k] = min(ok, key=lambda lat: abs(lat - want[k]))
        for k, id in enumerate(self.door_ids):
            sh.add_door(id, k, lats[k], 0)
        c.note("doors", [[k, lats[k], 0] for k in range(4)])

    def build(self, c):
        t, sh, rng = c.theme, c.shell, c.rng
        centre = c.vary.choice("centre", ("axle", "open")) if hasattr(t, "axle") else "open"
        if centre == "axle":
            m = t.machine(c.room, "hubgears")
            h = sh.top - t.floor_t - sh.floor_y
            t.axle(c.room, (0, sh.floor_y, 0), h, 1.2)
            dia = rng.uniform(0.45, 0.6) * min(sh.size[0], sh.size[2])
            t.gear(m, (0, sh.floor_y + h * rng.uniform(0.65, 0.8), 0), dia, thickness=0.6, spin=rng.uniform(0, 30), turn=rng.uniform(8, 16))
            c.plan.take(0, (0, 0, 0), (1.6, 1.6), force=True)
            c.note("centre", "axle")


@job
class StairHall(Job):
    """A floor up (or down): two doorways on different levels of a split or balcony shell."""
    name = "stairhall"
    shapes = ("split", "balcony")
    weight = 1.5
    max = 1                            # (a second one in a layout is the same climb again)
    size = "medium"
    side_jobs = ("guard", "stash", "alcove", "sentries")
    side_chance = 0.95

    def doors(self, c):
        pick_doors(c, ("a", "b"), "any", levels={"a": 0, "b": 1})


@job
class Nook(Job):
    """A small dead end that closes an open doorway: something to look at in it."""
    name = "nook"
    size = "nook"
    shapes = ("rect", "round")
    end_cap = True
    weight = 3.0
    features = ("remains",)
    ambient = (0, 0)
    door_ids = ("in",)
    dressing = True
    max = 1

    def doors(self, c):
        pick_doors(c, ("in",))

    def build(self, c):
        """A centrepiece against the back wall (the theme's nook pieces), and one or two smaller things beside it."""
        t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
        pieces = t.nook_pieces() if hasattr(t, "nook_pieces") else []
        if not pieces:
            return
        d = sh.doors["in"]
        back = (d.k + 2) % 4
        depth = (sh.half[2] if back % 2 == 0 else sh.half[0]) - t.wall_t
        main = c.vary.choice("centrepiece", [p for p in pieces if p.get("main")])
        size = main["size"]
        yaw = (90 * back + 180) % 360
        dp = sh.door_at("in", 0)
        lat0 = c.vary.choice("centre lateral", (0.0, -1.0, 1.0)) * max(0.0, (sh.half[0] if back % 2 == 0 else sh.half[2]) - size[0] - 1.0)
        # against the back wall, or a little out from it where the theme's framing stands on the wall
        for inset, lat in ((0.4, lat0), (0.9, lat0), (1.4, lat0), (0.4, 0.0), (0.9, 0.0), (1.4, 0.0)):
            x, z = side_xz(back, lat, depth - size[1] / 2 - inset)
            far_enough = math.hypot(x - dp[0], z - dp[2]) - max(size) / 2 > t.frame_depth() + 2.0
            if far_enough and plan.take(0, (x, 0, z), size, yaw=yaw, margin=0.2, allowed=(FREE, WAY)):
                main["build"](c.room, (x, sh.floor_y, z), yaw)
                c.note("centrepiece", main["name"])
                break
        extra = [p for p in pieces if not p.get("main")]
        n = 0
        for p in rng.sample(extra, min(len(extra), rng.randint(1, 2))):
            for sx, sz in plan.spots(0, p["size"], rng, margin=0.2, wall=True, count=1):
                if plan.take(0, (sx, 0, sz), p["size"], margin=0.2):
                    p["build"](c.room, (sx, sh.floor_y, sz), _face_room(sh, sx, sz))
                    n += 1
        c.note("pieces", n)
