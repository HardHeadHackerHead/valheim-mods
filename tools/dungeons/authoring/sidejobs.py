"""
Side jobs: something to do in a room that is on the way through but isn't a step of the plan, so no room is just walked across.

  guard     a chest of the land's loot in the open; lifting its lid wakes a short ambush (two small waves, no doors shut)
  sentries  a handful of keepers who come for you as you step in (one wave, no doors shut)
  stash     a closet in a wall, walled up with cracked stone (a breakable): break it for the chest inside
  alcove    the same closet shut by an iron leaf; a lever somewhere else in the room raises it

Each needs only what a recipe already keeps: free floor (the ways stay clear), spawn points away from doorways, a lever's approach.
Rooms say which they can take (Job.side_jobs); add_side_job() picks one by the room's seed.
"""
import math

import numpy as np

from dungeon_parts import box

from .core import side_xz, FREE, WAY, TAKEN, OUT
from .recipes import pick, NoFit
from .jobs import encounter, spread_points, place_lever

WEIGHTS = {"guard": 0.8, "sentries": 0.5, "stash": 1.2, "alcove": 1.2}     # (most are quiet: the breathers between fights stay calm)
STASH = True          # a breakable closing no doorway: on once the pack reader takes it (dungeon-layout2)


def add_side_job(c, allowed):
    kinds = {k: WEIGHTS[k] for k in allowed if k in WEIGHTS and (STASH or k != "stash")}
    if c.shell.shape == "round":
        kinds.pop("stash", None)
        kinds.pop("alcove", None)
    first = c.vary.choice("side job kind", sorted(kinds)) if kinds else None
    for _ in range(4):
        if not kinds:
            return None
        kind = first if first in kinds else pick(c.rng, kinds)
        first = None
        try:
            ok = {"guard": guard, "sentries": sentries, "stash": stash, "alcove": alcove}[kind](c)
        except NoFit:
            ok = False
        if ok:
            c.note("side_job", kind)
            c.info["features"].append(kind)
            return kind
        kinds.pop(kind)
    return None


def _chest_spot(c, level=0):
    t, sh, plan, rng = c.theme, c.shell, c.plan, c.rng
    size = (2.2, 1.8)
    for x, z in plan.spots(level, size, rng, margin=0.3, wall=True, count=60):
        far = min(math.hypot(x - p[0], z - p[2]) for p in [sh.door_at(d, 0) for d in sh.doors])
        if far < 3.5:
            continue
        from .recipes import _face_room
        yaw = _face_room(sh, x, z)
        if plan.take(level, (x, 0, z), size, yaw=yaw, margin=0.3):
            front = np.array([x, sh.levels[level], z]) + np.array([math.sin(math.radians(yaw)), 0, math.cos(math.radians(yaw))]) * 0
            return np.array([x, sh.levels[level], z]), yaw
    return None


def _fight_door(c):
    """The doorway a side job's creatures come for, and its level: one on the lowest floor if there is one (creatures don't take the
    stairs in the creature check, so their spawn points go on the floor of the doorway they walk to)."""
    d = min(c.shell.doors.values(), key=lambda d: d.level)
    return d.id, d.level


def guard(c):
    """A chest in the open; opening it brings two small waves."""
    spot = _chest_spot(c)
    if spot is None:
        return False
    at, yaw = spot
    door, lv = _fight_door(c)
    pts = spread_points(c, 3, level=lv, way_in=door, gap=2.5, on_ways=True)
    if len(pts) < 2:
        return False
    c.theme.chest(c.room, at, yaw=yaw, saved=True)
    encounter(c, "guard", "open", [{"melee": 2}, {"melee": 1, "ranged": 1}], pts, chest=at, music="CombatEventL2", reached_from=door)
    return True


def sentries(c):
    """Keepers in the room who come for whoever steps in."""
    door, lv = _fight_door(c)
    pts = spread_points(c, 3, level=lv, way_in=door, gap=3.0, on_ways=True)
    if len(pts) < 2:
        return False
    sh = c.shell
    x0, x1, z0, z1 = sh.inner
    encounter(c, "sentries", "enter", [{"melee": 2, "ranged": 1}], pts, music="CombatEventL2", reached_from=door,
              area=((x0, sh.floor_y, z0), (x1, sh.floor_y + min(sh.top - sh.floor_y - 0.5, 8.0), z1)))
    return True


def _closet(c):
    """A closet against a wall, out of the ways: (side k, lateral, the wall's inner depth, width, depth, opening height). Its floor
    and the space in front of it (2 m) are claimed."""
    t, sh, plan, rng = c.theme, c.shell, c.plan, c.rng
    W = max(2.4, t.door_w * 0.6)
    D = max(2.0, t.door_w * 0.5)
    H = min(t.door_h * 0.75, (sh.top - t.floor_t - sh.floor_y) * 0.45)
    if H < 2.2:
        return None
    sides = list(range(4))
    rng.shuffle(sides)
    for k in sides:
        depth = (sh.half[2] if k % 2 == 0 else sh.half[0]) - t.wall_t
        across = (sh.half[0] if k % 2 == 0 else sh.half[2]) - t.wall_t
        lats = [l for l in np.arange(-across + W / 2 + t.wall_t + 0.5, across - W / 2 - t.wall_t - 0.5, 0.5)]
        rng.shuffle(lats)
        for lat in lats[:30]:
            a = side_xz(k, lat - W / 2 - t.wall_t, depth)
            b = side_xz(k, lat + W / 2 + t.wall_t, depth - D - t.wall_t - 2.0)
            x0, x1 = sorted((a[0], b[0]))
            z0, z1 = sorted((a[1], b[1]))
            if not plan.rect_is(0, x0, x1, z0, z1, (FREE,)):
                continue
            # (its walls go up to the ceiling: never under a gallery or a higher level)
            if any(not plan.rect_is(lv, x0 - 0.5, x1 + 0.5, z0 - 0.5, z1 + 0.5, (OUT,)) for lv in range(1, len(plan.levels))):
                continue
            if not all(sh.on_level(*side_xz(k, l, d), 0) for l in (lat - W / 2, lat + W / 2) for d in (depth - 0.3, depth - D - 2.0)):
                continue
            c0 = side_xz(k, lat - W / 2 - t.wall_t, depth)
            c1 = side_xz(k, lat + W / 2 + t.wall_t, depth - D - t.wall_t)
            plan.set_rect(0, min(c0[0], c1[0]), max(c0[0], c1[0]), min(c0[1], c1[1]), max(c0[1], c1[1]), OUT)
            f0 = side_xz(k, lat - W / 2, depth - D - t.wall_t)
            f1 = side_xz(k, lat + W / 2, depth - D - t.wall_t - 2.0)
            plan.set_rect(0, min(f0[0], f1[0]), max(f0[0], f1[0]), min(f0[1], f1[1]), max(f0[1], f1[1]), WAY)
            return k, lat, depth, W, D, H
    return None


def _closet_walls(c, k, lat, depth, W, D, H):
    """The closet's two side walls (floor to ceiling), its front above the opening, and its chest; returns the front's middle."""
    t, sh, r = c.theme, c.shell, c.room
    top = sh.top - t.floor_t
    for s in (-1, 1):
        x, z = side_xz(k, lat + s * (W / 2 + t.wall_t / 2), depth - (D + t.wall_t) / 2)
        t.wall_panel(r, (x, (sh.floor_y + top) / 2, z), D + t.wall_t, top - sh.floor_y, (90 * k + 90) % 360)
    fx, fz = side_xz(k, lat, depth - D - t.wall_t / 2)
    t.wall_panel(r, (fx, (sh.floor_y + H + top) / 2, fz), W + 2 * t.wall_t, top - sh.floor_y - H, 90 * k)
    cx, cz = side_xz(k, lat, depth - D / 2)
    t.chest(r, (cx, sh.floor_y, cz), yaw=(90 * k + 180) % 360, saved=True)
    return np.array([fx, sh.floor_y, fz])


def stash(c):
    """A closet walled up with cracked stone: a pickaxe or a heavy blow opens it."""
    got = _closet(c)
    if got is None:
        return False
    k, lat, depth, W, D, H = got
    t, r = c.theme, c.room
    front = _closet_walls(c, k, lat, depth, W, D, H)
    b = r.mech("stash", "breakable", front, (0, (90 * k) % 360, 0))
    b.pose().pose()
    b.extra["health"] = 300
    cracked(t, b, W, H)
    c.note("closet", [k, round(float(lat), 1)])
    return True


def cracked(t, holder, W, H):
    """Cracked stone filling an opening W x H (in the holder's frame, its face towards -z): the wall's own piece in slabs knocked
    askew, dark behind the gaps, a corner fallen out."""
    holder.fill(t.p("trim"), (0, H / 2, 0.3 * t.wall_t), (W + 0.1, H, 0.15))
    cols, rows = 2, max(2, int(round(H / 1.6)))
    w, h = W / cols, H / rows
    tilts = [(5, -6, 3), (-4, 5, -5), (6, 3, -2), (-5, -4, 5), (4, 7, 4), (-6, -5, -3), (3, -3, 2), (-3, 4, -4)]
    for i in range(cols):
        for j in range(rows):
            if (i, j) == (1, rows - 1):
                continue
            tx, ty, tz = tilts[(i * rows + j) % len(tilts)]
            c = (-W / 2 + (i + 0.5) * w + 0.03 * tx, (j + 0.5) * h + 0.02 * ty, -0.05 * abs(tz) - 0.05)
            box(holder, t.p("wall"), c, (w - 0.12, h - 0.1, max(0.3, t.wall_t * 0.6)), yaw=tz, tilt=(tx, ty))


def alcove(c):
    """A closet shut by an iron leaf that a lever elsewhere in the room raises (once)."""
    got = _closet(c)
    if got is None:
        return False
    k, lat, depth, W, D, H = got
    t, r = c.theme, c.room
    place_lever(c, "alcovelever", [("alcove", "set:1")], level=0)      # (first: if it doesn't fit, nothing is built)
    front = _closet_walls(c, k, lat, depth, W, D, H)
    g = r.mech("alcove", "platform", front, (0, (90 * k) % 360, 0))
    g.fill(t.p("grate"), (0, H / 2, 0), (W + 0.1, H, 0.2))
    g.pose().pose(at=(0, H - 0.05, 0))
    g.seconds = 2.0
    c.note("closet", [k, round(float(lat), 1)])
    return True
