"""
The pieces every recipe stands on: a seeded random source, doorways, and the floor plan that keeps a room playable while it is
filled in.

The floor plan is a grid of 0.5 m cells per floor level (a room has one or more: a split level, a balcony storey). Every cell is
  OUT    not floor: a wall, a pit, outside a round or L-shaped room
  FREE   floor nothing has claimed
  WAY    floor that must stay clear: the ways between doorways (3 m wide), a lever's approach, the space in front of a chest
  TAKEN  floor something stands on
A recipe builds in this order: the shell (walls, floors, levels) marks OUT and FREE; the job puts its mechanisms down (TAKEN) and
reserves what players need (WAY); then the ways between all doorways are found on the grid and reserved (WAY); only then do the
features (cover, pillars, machinery) and the dressing (props, lights) go down, and only on FREE cells. So nothing a recipe adds for
looks can ever block a way, a lever or a door. walk_check, prop_check, lever_check and the harness's gap check are still the
authority: the plan makes passing them the normal case, the checks prove it.
"""
import hashlib
import math
import random
from collections import deque

import numpy as np

CELL = 0.5
OUT, FREE, WAY, TAKEN = 0, 1, 2, 3


def rng_for(*keys):
    """A random source that depends only on its keys (the same keys, the same room), never on anything global."""
    digest = hashlib.sha1("/".join(str(k) for k in keys).encode()).hexdigest()
    return random.Random(int(digest[:12], 16))


def pick(rng, weighted):
    """One key of {key: weight} (weights > 0), drawn from rng."""
    items = [(k, w) for k, w in weighted.items() if w > 0]
    total = sum(w for _, w in items)
    x = rng.random() * total
    for k, w in items:
        x -= w
        if x <= 0:
            return k
    return items[-1][0]


def side_vec(k):
    """Side k's outward direction on the floor plan (0 north +z, 1 east +x, 2 south -z, 3 west -x)."""
    return [(0.0, 1.0), (1.0, 0.0), (0.0, -1.0), (-1.0, 0.0)][k % 4]


def side_xz(k, lateral, depth):
    """(x, z) of a point on side k: `lateral` along it (seen from inside, to the right for north: +x), `depth` from the middle."""
    return [(lateral, depth), (depth, -lateral), (-lateral, -depth), (-depth, lateral)][k % 4]


class Door:
    """A doorway of a recipe room: side k, `lateral` along that side, on floor level `level` (its threshold is that level's height)."""

    def __init__(self, id, k, lateral, level=0, width=5.0, height=8.0):
        self.id, self.k, self.lateral, self.level, self.width, self.height = id, k, lateral, level, width, height

    def __repr__(self):
        return f"Door({self.id} side {self.k} at {self.lateral:+.1f}, level {self.level})"


class Plan:
    """The floor plan of a room: one grid per level (see the module's notes). x and z are room-frame metres."""

    def __init__(self, size, levels):
        self.size = np.array(size, float)
        self.half = self.size / 2
        self.levels = list(levels)                    # each level's floor height
        self.nx = int(round(self.size[0] / CELL))
        self.nz = int(round(self.size[2] / CELL))
        self.grid = [np.full((self.nx, self.nz), OUT, np.int8) for _ in self.levels]

    # ---- cells and coordinates

    def ij(self, x, z):
        return int(math.floor((x + self.half[0]) / CELL)), int(math.floor((z + self.half[2]) / CELL))

    def xz(self, i, j):
        return -self.half[0] + (i + 0.5) * CELL, -self.half[2] + (j + 0.5) * CELL

    def _span(self, x0, x1, z0, z1):
        i0, i1 = max(0, int(math.floor((min(x0, x1) + self.half[0]) / CELL + 1e-6))), \
            min(self.nx, int(math.ceil((max(x0, x1) + self.half[0]) / CELL - 1e-6)))
        j0, j1 = max(0, int(math.floor((min(z0, z1) + self.half[2]) / CELL + 1e-6))), \
            min(self.nz, int(math.ceil((max(z0, z1) + self.half[2]) / CELL - 1e-6)))
        return i0, i1, j0, j1

    def set_rect(self, level, x0, x1, z0, z1, value, only=None):
        i0, i1, j0, j1 = self._span(x0, x1, z0, z1)
        g = self.grid[level][i0:i1, j0:j1]
        if only is None:
            g[...] = value
        else:
            g[np.isin(g, only)] = value

    def set_mask(self, level, fn, value, only=None):
        """Every cell whose middle (x, z) passes fn gets value (only cells now in `only`, if given)."""
        g = self.grid[level]
        for i in range(self.nx):
            for j in range(self.nz):
                if fn(*self.xz(i, j)) and (only is None or g[i, j] in only):
                    g[i, j] = value

    def rect_is(self, level, x0, x1, z0, z1, allowed=(FREE,)):
        i0, i1, j0, j1 = self._span(x0, x1, z0, z1)
        if i0 >= i1 or j0 >= j1:
            return False
        if i0 == 0 and x0 < -self.half[0] - 1e-6 or j0 == 0 and z0 < -self.half[2] - 1e-6:
            return False
        if i1 == self.nx and x1 > self.half[0] + 1e-6 or j1 == self.nz and z1 > self.half[2] + 1e-6:
            return False
        return bool(np.isin(self.grid[level][i0:i1, j0:j1], allowed).all())

    @staticmethod
    def footprint(centre, size, yaw=0.0):
        """The axis-aligned rectangle (x0, x1, z0, z1) round a footprint of `size` (x, z) turned by yaw."""
        c, s = abs(math.cos(math.radians(yaw))), abs(math.sin(math.radians(yaw)))
        hx, hz = (size[0] * c + size[1] * s) / 2, (size[0] * s + size[1] * c) / 2
        return centre[0] - hx, centre[0] + hx, centre[2] - hz, centre[2] + hz

    def can_take(self, level, centre, size, yaw=0.0, margin=0.0, allowed=(FREE,)):
        x0, x1, z0, z1 = self.footprint(centre, size, yaw)
        return self.rect_is(level, x0 - margin, x1 + margin, z0 - margin, z1 + margin, allowed)

    def take(self, level, centre, size, yaw=0.0, margin=0.0, allowed=(FREE,), force=False):
        """Claims a footprint (TAKEN) if it and a margin round it are all `allowed`; True if it did. force: claim it anyway."""
        if not force and not self.can_take(level, centre, size, yaw, margin, allowed):
            return False
        x0, x1, z0, z1 = self.footprint(centre, size, yaw)
        self.set_rect(level, x0, x1, z0, z1, TAKEN, only=None if force else (FREE, WAY))
        return True

    def reserve(self, level, x0, x1, z0, z1):
        """Floor that must stay clear (WAY); walls and pits stay as they are."""
        self.set_rect(level, x0, x1, z0, z1, WAY, only=(FREE,))

    # ---- ways

    def way(self, level, a, b, width=3.0):
        """Finds a walk from point a to point b on one level (through FREE and WAY cells) and reserves it `width` wide. False if
        there is none."""
        g = self.grid[level]
        start, goal = self._nearest_open(level, a), self._nearest_open(level, b)
        if start is None or goal is None:
            return False
        prev = {start: None}
        q = deque([start])
        while q:
            c = q.popleft()
            if c == goal:
                break
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                n = (c[0] + di, c[1] + dj)
                if 0 <= n[0] < self.nx and 0 <= n[1] < self.nz and n not in prev and g[n] in (FREE, WAY):
                    if di and dj and not (g[c[0] + di, c[1]] in (FREE, WAY) and g[c[0], c[1] + dj] in (FREE, WAY)):
                        continue
                    prev[n] = c
                    q.append(n)
        if goal not in prev:
            return False
        r = int(math.ceil(width / 2 / CELL))
        c = goal
        while c is not None:
            i, j = c
            for di in range(-r, r + 1):
                for dj in range(-r, r + 1):
                    if di * di + dj * dj <= r * r + 1:
                        ii, jj = i + di, j + dj
                        if 0 <= ii < self.nx and 0 <= jj < self.nz and g[ii, jj] == FREE:
                            g[ii, jj] = WAY
            c = prev[c]
        return True

    def _nearest_open(self, level, p, reach=8):
        i, j = self.ij(p[0], p[2] if len(p) == 3 else p[1])
        g = self.grid[level]
        best = None
        for d in range(reach + 1):
            for di in range(-d, d + 1):
                for dj in range(-d, d + 1):
                    if max(abs(di), abs(dj)) != d:
                        continue
                    ii, jj = i + di, j + dj
                    if 0 <= ii < self.nx and 0 <= jj < self.nz and g[ii, jj] in (FREE, WAY):
                        if best is None or di * di + dj * dj < best[0]:
                            best = (di * di + dj * dj, (ii, jj))
            if best:
                return best[1]
        return None

    # ---- places for things

    def spots(self, level, size, rng, margin=0.0, wall=None, count=1, gap=0.0, yaw=0.0, avoid=(), allowed=(FREE,)):
        """Up to `count` centres (x, z) where a footprint of `size` and a margin round it are all FREE, in random order, at least
        `gap` apart from each other and from the points in `avoid`. wall: True for places against a wall (an OUT cell within 1 m
        of the footprint's edge), False for places away from walls (none within 1.5 m), None for either. allowed: the cells it may
        cover (spawn points may stand on a way: nothing stands there)."""
        g = self.grid[level]
        cand = []
        for i in range(self.nx):
            for j in range(self.nz):
                if g[i, j] not in allowed:
                    continue
                x, z = self.xz(i, j)
                if not self.can_take(level, (x, 0, z), size, yaw, margin, allowed):
                    continue
                if wall is not None:
                    x0, x1, z0, z1 = self.footprint((x, 0, z), size, yaw)
                    near = self._near_out(level, x0, x1, z0, z1, 1.0 if wall else 1.5)
                    if near != wall:
                        continue
                cand.append((x, z))
        rng.shuffle(cand)
        out = []
        for x, z in cand:
            if all(math.hypot(x - a, z - b) >= gap for a, b in out) and all(math.hypot(x - a[0], z - a[-1]) >= gap for a in avoid):
                out.append((x, z))
                if len(out) >= count:
                    break
        return out

    def _near_out(self, level, x0, x1, z0, z1, d):
        i0, i1, j0, j1 = self._span(x0 - d, x1 + d, z0 - d, z1 + d)
        if x0 - d < -self.half[0] or x1 + d > self.half[0] or z0 - d < -self.half[2] or z1 + d > self.half[2]:
            return True
        return bool((self.grid[level][i0:i1, j0:j1] == OUT).any())

    def free_share(self, level):
        g = self.grid[level]
        floor = np.isin(g, (FREE, WAY, TAKEN)).sum()
        return float((g == FREE).sum()) / max(1, floor)


class Vary:
    """The choices that make one room of a job different from the others of that job. With no instance index (vi None) every
    choice is the random source's; with one, each named choice steps through its options with the index (offset per name), so two
    rooms made one after the other differ in every choice that has options: shape, size, features, dressing, the job's variant..."""

    def __init__(self, rng, vi=None):
        self.rng, self.vi = rng, vi

    def _off(self, key):
        return int(hashlib.sha1(key.encode()).hexdigest()[:6], 16)

    def choice(self, key, options):
        options = list(options)
        if self.vi is None:
            return self.rng.choice(options)
        # every n instances the options come round again, each name with its own twist (so instances 0 and n, or 0 and 2 for a
        # choice of two, differ in about half their choices instead of none)
        n, off = len(options), self._off(key)
        return options[(off + self.vi + (self.vi // n) * (off // 7 % n)) % n]

    def uniform(self, key, a, b):
        if self.vi is None:
            return self.rng.uniform(a, b)
        u = self.choice(key, (0.0, 1.0, 0.5, 0.25, 0.75))
        return a + (b - a) * min(1.0, max(0.0, u + self.rng.uniform(-0.08, 0.08)))

    def integer(self, key, a, b):
        return self.choice(key, range(a, b + 1)) if self.vi is not None else self.rng.randint(a, b)

    def flag(self, key, p):
        """On with chance p; stepped (on in about p of the instances, never the same as the one before for p near a half)."""
        if self.vi is None:
            return self.rng.random() < p
        k = 4
        return (self.vi + self._off(key)) % k < round(p * k)


# What the best player can do (Jump 100 + Run 100; the user's rule: puzzles hold at any skill; layout2's gap check, worst case):
JUMP_UP = 3.14             # up onto a ledge (bars stop a jump only if they stand higher than this over the takeoff)
JUMP_ACROSS = 10.9         # across, landing level (edge to edge, the body's radius in: layout2's table)
JUMP_ACROSS_DOWN = 13.4    # across, landing 4 m lower
PROP_TOP = 1.7             # the tallest standable dressing (barrels, shelves 1.6: builder2's climb.top)
