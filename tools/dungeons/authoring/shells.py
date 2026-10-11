"""
Shells: the empty room a job is built in. A shell is a shape (rect, L, round, split level, balcony storey), a size, its floor levels and
its doorways, built from the theme's pieces, with its floor plan (core.Plan) marked: floor FREE, everything else OUT, the space in
front of each doorway reserved (WAY), the door frames TAKEN.

The shapes:
  rect     four walls, one floor
  L        a rectangle with one corner cut out (walls round the notch); doorways only on the arms
  round    a circle of wall panels in a square box; doorways where it touches the box (the sides' middles)
  split    two floor levels: the low half and, behind a rise of half a storey (a full one in a small-scale theme), the high half;
           a stair along a side wall joins them, a rail guards the high edge
  balcony  two storeys: a gallery along one wall a storey up, reached by a stair along the next wall; doorways on both floors
Doorway slots (where doorways may go) come from the shape; a job picks from them (doorways(...)).
"""
import math

import numpy as np

from dungeon_parts import Room

from .core import Door, Plan, OUT, FREE, WAY, TAKEN, side_xz, side_vec, CELL


SHAPES = ("rect", "L", "round", "split", "balcony")


class Shell:
    def __init__(self, theme, name, shape, size, rng, tags=(), weight=1.0, max=0, min_depth=0, end_cap=False, rise=None):
        self.theme, self.name, self.shape, self.rng = theme, name, shape, rng
        self.size = np.array(size, float)
        self.half = self.size / 2
        self.bottom, self.top = -self.size[1] / 2, self.size[1] / 2
        self.floor_y = self.bottom + theme.floor_t
        self.params = {"shape": shape, "size": [float(v) for v in size]}
        self.room = Room(name, size, tags=tags, weight=weight, max=max, min_depth=min_depth, end_cap=end_cap)
        t = theme
        self.inner = (-self.half[0] + t.wall_t, self.half[0] - t.wall_t, -self.half[2] + t.wall_t, self.half[2] - t.wall_t)
        self.levels = [self.floor_y]
        self.notch = None
        self.split_z = None
        self.balcony = None
        if shape == "split":
            self.rise = rise or t.storey                       # (a whole storey: doorways stay on the dungeon's storeys)
            self.levels.append(self.floor_y + self.rise)
        elif shape == "balcony":
            self.rise = t.storey
            self.levels.append(self.floor_y + t.storey)
        self.plan = Plan(size, self.levels)
        self.doors = {}
        self._layout()

    # ---------------------------------------------------------------- the shape's own layout (before doorways)

    def _layout(self):
        t, rng = self.theme, self.rng
        x0, x1, z0, z1 = self.inner
        if self.shape == "L":
            c = rng.randrange(4)                                      # the cut corner: 0 ne, 1 se, 2 sw, 3 nw
            fx, fz = rng.choice((0.4, 0.5)), rng.choice((0.4, 0.5))
            nx, nz = (self.size[0] * fx), (self.size[2] * fz)
            sx = 1 if c in (0, 1) else -1
            sz = 1 if c in (0, 3) else -1
            # the notch: from the corner inwards nx by nz (box coordinates)
            ax = self.half[0] * sx
            az = self.half[2] * sz
            self.notch = (min(ax, ax - sx * nx), max(ax, ax - sx * nx), min(az, az - sz * nz), max(az, az - sz * nz), c)
            self.params["notch"] = [c, fx, fz]
        elif self.shape == "split":
            # the low half must hold the stair and a landing at its foot, the high half a doorway's approach
            run = t.stair_run(self.rise)
            width = max(t.way_width, t.door_w * 0.6)
            lo_min = z0 + run + 3.0
            hi_max = z1 - max(5.0, t.door_w)
            if lo_min > hi_max:
                from .recipes import NoFit
                raise NoFit()
            self.split_z = float(round(rng.uniform(lo_min, hi_max)))
            side = rng.choice((-1, 1))
            sx = side * (self.half[0] - t.wall_t - width / 2)
            self.stair_geo = {"foot": np.array([sx, self.floor_y, self.split_z - run]), "rise": self.rise, "run": run,
                              "width": width, "yaw": 0.0,
                              "rect": (sx - width / 2, sx + width / 2, self.split_z - run, self.split_z)}
            self.params["rise"] = self.rise
            self.params["split"] = self.split_z
            self.params["stair"] = [side]
        elif self.shape == "balcony":
            k = rng.randrange(4)
            across = self.half[0] if k % 2 == 0 else self.half[2]
            depth = self.half[2] if k % 2 == 0 else self.half[0]
            d = min(max(t.door_w + 1.0, 2 * depth * rng.uniform(0.25, 0.32)), depth)
            self.balcony = (k, d)
            self.params["balcony"] = [k, round(d, 1)]
            # the stair: along the side wall at one end of the gallery, climbing towards side k
            run = t.stair_run(t.storey)
            width = max(t.way_width, t.door_w * 0.6)
            side = rng.choice((-1, 1))
            lat = side * (across - t.wall_t - width / 2)
            top_depth = depth - t.wall_t - d
            foot_depth = top_depth - run
            if foot_depth < -depth + t.wall_t + 3.0:
                from .recipes import NoFit
                raise NoFit()
            a = side_xz(k, lat - width / 2, foot_depth)
            b = side_xz(k, lat + width / 2, top_depth)
            fx, fz = side_xz(k, lat, foot_depth)
            self.stair_geo = {"foot": np.array([fx, self.floor_y, fz]), "rise": t.storey, "run": run, "width": width,
                              "yaw": 90.0 * k, "rect": (min(a[0], b[0]), max(a[0], b[0]), min(a[1], b[1]), max(a[1], b[1])),
                              "lat": lat, "top_depth": top_depth, "foot_depth": foot_depth}
            self.params["stair"] = [side]
        elif self.shape == "round":
            if abs(self.size[0] - self.size[2]) > 1e-6:
                raise ValueError("a round shell needs a square box")
        # the floor plan: the shape's floor FREE
        R = self.half[0] - t.wall_t
        for lv in range(len(self.levels)):
            self.plan.set_mask(lv, lambda x, z, lv=lv: self.on_level(x, z, lv), FREE)

    def in_shape(self, x, z, margin=0.0):
        """Whether floor-plan point (x, z) is inside the room's walls (margin metres clear of them)."""
        x0, x1, z0, z1 = self.inner
        if not (x0 + margin < x < x1 - margin and z0 + margin < z < z1 - margin):
            return False
        if self.shape == "round":
            return math.hypot(x, z) < self.half[0] - self.theme.wall_t - margin - 0.8
        if self.notch:
            a, b, c, d, _ = self.notch
            if a - self.theme.wall_t - margin < x < b + self.theme.wall_t + margin and \
                    c - self.theme.wall_t - margin < z < d + self.theme.wall_t + margin:
                return False
        return True

    def on_level(self, x, z, lv):
        if not self.in_shape(x, z):
            return False
        if lv == 0 and getattr(self, "stair_geo", None):
            a, b, c, d = self.stair_geo["rect"]
            if a - 0.3 < x < b + 0.3 and c - 0.3 < z < d:
                return False
        if self.shape == "split":
            return (z < self.split_z) if lv == 0 else (z > self.split_z)
        if self.shape == "balcony":
            if lv == 0:
                return True
            k, d = self.balcony
            depth = {0: z, 1: x, 2: -z, 3: -x}[k]
            half = self.half[2] if k % 2 == 0 else self.half[0]
            return depth > half - self.theme.wall_t - d
        return lv == 0

    # ---------------------------------------------------------------- doorway slots

    def slots(self):
        """Where doorways may go: [(k, lateral, level)], every 1 m along each side where a doorway and its frame fit."""
        t = self.theme
        out = []
        margin = t.door_w / 2 + 1.0 + t.wall_t + (0.6 if t.frame_depth() > 1 else 0.3)
        for k in range(4):
            across = self.half[0] if k % 2 == 0 else self.half[2]
            depth = self.half[2] if k % 2 == 0 else self.half[0]
            if self.shape == "round":
                lats = [0.0]
            else:
                n = int(math.floor(across - margin))
                lats = [float(v) for v in range(-n, n + 1)] if n >= 0 else []
                if not lats and across >= t.door_w / 2 + t.wall_t + 0.4:
                    lats = [0.0]                                  # a narrow room: its doorway fills the side
            for lat in lats:
                for lv in range(len(self.levels)):
                    # the doorway's whole width must open onto this level's floor
                    ok = True
                    for s in (-1, 0, 1):
                        x, z = side_xz(k, lat + s * (t.door_w / 2 + 0.3), depth - t.wall_t - 0.6 - self._sag(t.door_w / 2 + 0.3))
                        if not self.on_level(x, z, lv):
                            ok = False
                    if ok and self._door_clear(k, lat, lv):
                        out.append((k, lat, lv))
        return out

    def _sag(self, w):
        """How far a round room's floor edge falls back from the box's face at w metres either side of a side's middle."""
        if self.shape != "round":
            return 0.0
        r = self.half[0] - self.theme.wall_t - 0.8
        return (r - math.sqrt(max(0.0, r * r - w * w))) + 0.3          # (+ the 0.2 the floor stops short of the probe line)

    def _door_clear(self, k, lat, lv):
        """The doorway's approach (3 m in) must be this level's floor too (not a stair, not the other level)."""
        t = self.theme
        depth = self.half[2] if k % 2 == 0 else self.half[0]
        for d in (1.0, 2.5, 3.5):
            for s in (-1, 1):
                x, z = side_xz(k, lat + s * (t.door_w / 2), depth - t.wall_t - d - self._sag(t.door_w / 2))
                if not self.on_level(x, z, lv):
                    return False
        return True

    # ---------------------------------------------------------------- building

    def add_door(self, id, k, lateral, level=0):
        t = self.theme
        d = Door(id, k, lateral, level, t.door_w, t.door_h)
        d.y = self.levels[level]
        self.doors[id] = d
        return d

    def door_at(self, id, inward=0.0, y=None):
        """A point on doorway id's middle line, `inward` metres in from the box's face."""
        d = self.doors[id]
        depth = self.half[2] if d.k % 2 == 0 else self.half[0]
        x, z = side_xz(d.k, d.lateral, depth - inward)
        return np.array([x, d.y if y is None else y, z])

    def build(self):
        """Walls with their doorways, floors, levels, the ceiling, door frames; the floor plan's doorway approaches."""
        t, room = self.theme, self.room
        x0, x1, z0, z1 = self.inner
        H, W = self.half, t.wall_t
        for id, d in self.doors.items():
            room.doorway(id, self.door_at(id), [-v for v in (side_vec(d.k)[0], 0, side_vec(d.k)[1])], type=t.door_type)
        # floor (the whole box: under a notch or round corners nobody sees it) and the ceiling
        for x0, x1, z0, z1 in self._cover():
            t.floor(room, x0, x1, z0, z1, self.floor_y, thickness=t.floor_t)
            t.ceiling(room, x0, x1, z0, z1, self.top)
        if self.shape == "round":
            self._round_walls()
        else:
            for k in range(4):
                depth = H[2] if k % 2 == 0 else H[0]
                across = H[0] if k % 2 == 0 else H[2]
                lo, hi = -across, across
                if self.notch:
                    lo, hi = self._side_span(k, lo, hi)
                    if lo is None:
                        continue
                openings = [(d.lateral, d.y) for d in self.doors.values() if d.k == k]
                t.wall_run(room, k, depth, lo, hi, self.bottom, self.top, openings)
            if self.notch:
                self._notch_walls()
        for d in self.doors.values():
            depth = H[2] if d.k % 2 == 0 else H[0]
            t.door_frame(room, d.k, d.lateral, d.y, depth - W)
            if getattr(self, "no_climb", False) and d.y < max(self.levels) - 0.1:
                # where the heights are the puzzle, a lower doorway's lintel is no ledge: the wall stands out over it as far as the
                # frame, up to the ceiling (a buttress over the doorway: no top to stand on)
                fd = t.frame_depth() + 0.1
                w = t.door_w / 2 + 1.6
                a0, a1 = side_xz(d.k, d.lateral - w, depth - W), side_xz(d.k, d.lateral + w, depth - W - fd)
                y0 = d.y + t.door_h
                t.block(room, min(a0[0], a1[0]), max(a0[0], a1[0]), min(a0[1], a1[1]), max(a0[1], a1[1]), y0, self.top - t.floor_t)
        if self.shape == "split":
            self._split_level()
        if self.shape == "balcony":
            self._balcony()
        self.theme.structure(self)                         # the theme's framing: posts, beams... (on the walls, out of the ways)
        # the doorways' approaches: the frame is taken, the space in front reserved (3 m in, the doorway's width)
        for id, d in self.doors.items():
            depth = H[2] if d.k % 2 == 0 else H[0]
            fd = t.frame_depth()
            for s in (-1, 1):
                x, z = side_xz(d.k, d.lateral + s * (t.door_w / 2 + 0.7), depth - W - fd / 2)
                self._rect(d.level, x, z, 1.6 if d.k % 2 == 0 else fd, fd if d.k % 2 == 0 else 1.6, TAKEN)
            for i in np.arange(0.25, fd + 3.25, 0.5):
                x, z = side_xz(d.k, d.lateral, depth - W - i)
                w = t.door_w
                self._rect(d.level, x, z, w if d.k % 2 == 0 else 0.5, 0.5 if d.k % 2 == 0 else w, WAY, only=(FREE,))
        return self

    def _cover(self):
        """The rectangles a floor and ceiling cover: the whole box, but for an L's notch (a round room's corners are hidden)."""
        H = self.half
        if not self.notch:
            return [(-H[0], H[0], -H[2], H[2])]
        a, b, c, d, corner = self.notch
        # the box minus the notch: a full-depth strip beside it and the rest of its side
        if corner in (0, 1):            # notch on the east (+x)
            rects = [(-H[0], a, -H[2], H[2])]
        else:
            rects = [(b, H[0], -H[2], H[2])]
        x0, x1 = (a, H[0]) if corner in (0, 1) else (-H[0], b)
        if corner in (0, 3):            # notch on the north (+z): the strip south of it
            rects.append((x0, x1, -H[2], c))
        else:
            rects.append((x0, x1, d, H[2]))
        return [r for r in rects if r[1] - r[0] > 0.01 and r[3] - r[2] > 0.01]

    def _rect(self, lv, x, z, sx, sz, value, only=None):
        self.plan.set_rect(lv, x - sx / 2, x + sx / 2, z - sz / 2, z + sz / 2, value, only=only)

    def _side_span(self, k, lo, hi):
        """The part of side k's wall that isn't along the notch (L shells)."""
        a, b, c, d, corner = self.notch
        depth = self.half[2] if k % 2 == 0 else self.half[0]
        # the notch touches side k if its rectangle reaches that side
        touches = {0: d >= self.half[2] - 1e-6, 2: c <= -self.half[2] + 1e-6, 1: b >= self.half[0] - 1e-6, 3: a <= -self.half[0] + 1e-6}[k]
        if not touches:
            return lo, hi
        # the notch's span along side k, in lateral coordinates
        if k == 0:
            n0, n1 = a, b
        elif k == 2:
            n0, n1 = -b, -a
        elif k == 1:
            n0, n1 = -d, -c
        else:
            n0, n1 = c, d
        if n0 <= lo + 1e-6:
            return n1, hi
        return lo, n0

    def _notch_walls(self):
        t, room = self.theme, self.room
        a, b, c, d, corner = self.notch
        H = self.half
        # two inner walls, each facing into the room: along x (at z = the notch's inner z) and along z (at x = its inner x)
        zi = c if corner in (0, 3) else d                      # the notch's z edge towards the room
        xi = a if corner in (0, 1) else b
        # the wall at z = zi, from the notch's x span; its room side faces away from the notch
        if corner in (0, 3):     # notch is north: a north-type wall (side 0) whose outer face is at z = zi + W
            t.wall_run(room, 0, zi + t.wall_t, a - t.wall_t if corner == 0 else a, b if corner == 0 else b + t.wall_t, self.bottom, self.top, [])
        else:                    # notch is south: a side-2 wall, laterals are -x
            t.wall_run(room, 2, -(zi - t.wall_t), -b if corner == 1 else -b - t.wall_t, -(a - t.wall_t) if corner == 1 else -a,
                       self.bottom, self.top, [])
        if corner in (0, 1):     # notch is east: a side-1 wall at x = xi, laterals are -z
            t.wall_run(room, 1, xi + t.wall_t, -d, -c, self.bottom, self.top, [])
        else:                    # notch is west: a side-3 wall, laterals are +z
            t.wall_run(room, 3, -(xi - t.wall_t), c, d, self.bottom, self.top, [])

    def _round_walls(self):
        t, room = self.theme, self.room
        R = self.half[0]
        circumference = 2 * math.pi * R
        n = max(16, int(math.ceil(circumference / (t.door_w / 2))))
        seg = 2 * R * math.sin(math.pi / n) + 0.2
        doors = [(d.k, d.y) for d in self.doors.values()]
        for i in range(n):
            a = (i + 0.5) * 360.0 / n
            ar = math.radians(a)
            x, z = math.sin(ar) * (R - t.wall_t / 2), math.cos(ar) * (R - t.wall_t / 2)
            yaw = a                                            # its face towards the middle
            gap = None
            for k, thr in doors:
                kv = side_vec(k)
                along = abs(x * kv[1] - z * kv[0])                 # the panel's distance from the doorway's axis
                if x * kv[0] + z * kv[1] > 0 and along < t.door_w / 2 + seg / 2 - 0.05:
                    gap = thr
            if gap is None:
                t.wall_panel(room, (x, 0, z), seg, self.size[1], yaw)
            else:
                lo = gap + t.door_h
                t.wall_panel(room, (x, (lo + self.top) / 2, z), seg, self.top - lo, yaw)
        # posts either side of each doorway, closing the wall between the panels and the opening
        for d in self.doors.values():
            for s in (-1, 1):
                x, z = side_xz(d.k, d.lateral + s * (t.door_w / 2 + 0.3), R - t.wall_t / 2 - 0.4)
                room.fill(t.p("trim"), (x, (self.bottom + self.top) / 2, z), (0.6 + 0.8 * (d.k % 2), self.size[1], 0.6 + 0.8 * (1 - d.k % 2)))

    def _split_level(self):
        """The high half: solid up to the rise; the stair along a side wall from the low half; a rail on the high edge."""
        t, room = self.theme, self.room
        x0, x1, z0, z1 = self.inner
        H = self.half
        zs, rise, g = self.split_z, self.rise, self.stair_geo
        t.block(room, -H[0], H[0], zs, H[2], self.floor_y - t.floor_t, self.floor_y + rise)
        t.stairs(room, g["foot"], rise, g["run"], g["width"], yaw=0, fill_below=self.floor_y)
        sx, width = g["foot"][0], g["width"]
        self.plan.set_rect(0, sx - width / 2 - 0.5, sx + width / 2 + 0.5, zs - g["run"] - 2.5, zs - g["run"], WAY, only=(FREE,))
        self.plan.set_rect(1, sx - width / 2, sx + width / 2, zs, zs + 2.5, WAY, only=(FREE,))
        rail_y = self.floor_y + rise
        a0, a1 = x0 + 0.3, x1 - 0.3
        g0, g1 = sx - width / 2 - 0.2, sx + width / 2 + 0.2
        for p, q in ((a0, g0), (g1, a1)):
            if q - p > 1.0:
                t.rail(room, (p, 0, zs + 0.35), (q, 0, zs + 0.35), rail_y)
                self.plan.set_rect(1, p, q, zs, zs + 0.8, TAKEN)
        self.stair_link = (0, g["foot"] + np.array([0, 0, -1.5]), 1, np.array([sx, rail_y, zs + 1.5]))

    def _balcony(self):
        """A gallery a storey up along side k, and the stair up the side wall to it."""
        t, room = self.theme, self.room
        k, d = self.balcony
        g = self.stair_geo
        H = self.half
        y1 = self.levels[1]
        across = H[0] if k % 2 == 0 else H[2]
        depth = H[2] if k % 2 == 0 else H[0]
        inner_lo, inner_hi = -across + t.wall_t, across - t.wall_t
        p0 = side_xz(k, inner_lo, depth - t.wall_t)
        p1 = side_xz(k, inner_hi, depth - t.wall_t - d)
        t.floor(room, min(p0[0], p1[0]), max(p0[0], p1[0]), min(p0[1], p1[1]), max(p0[1], p1[1]), y1, thickness=1.0)
        t.stairs(room, g["foot"], g["rise"], g["run"], g["width"], yaw=g["yaw"], fill_below=self.floor_y)
        lat, width, top_depth, foot_depth = g["lat"], g["width"], g["top_depth"], g["foot_depth"]
        f0 = side_xz(k, lat - width / 2 - 0.5, foot_depth - 2.5)
        f1 = side_xz(k, lat + width / 2 + 0.5, foot_depth)
        self.plan.set_rect(0, min(f0[0], f1[0]), max(f0[0], f1[0]), min(f0[1], f1[1]), max(f0[1], f1[1]), WAY, only=(FREE,))
        e0, e1 = inner_lo + 0.3, inner_hi - 0.3
        g0, g1 = lat - width / 2 - 0.2, lat + width / 2 + 0.2
        for p, q in ((e0, g0), (g1, e1)):
            if q - p > 1.0:
                a = side_xz(k, p, top_depth + 0.35)
                b = side_xz(k, q, top_depth + 0.35)
                t.rail(room, (a[0], 0, a[1]), (b[0], 0, b[1]), y1)
                r0 = side_xz(k, p, top_depth)
                r1 = side_xz(k, q, top_depth + 0.8)
                self.plan.set_rect(1, min(r0[0], r1[0]), max(r0[0], r1[0]), min(r0[1], r1[1]), max(r0[1], r1[1]), TAKEN)
        hx, hz = side_xz(k, lat, top_depth + 1.5)
        fxx, fzz = side_xz(k, lat, foot_depth - 1.5)
        self.stair_link = (0, np.array([fxx, self.floor_y, fzz]), 1, np.array([hx, y1, hz]))
        h0 = side_xz(k, lat - width / 2, top_depth)
        h1 = side_xz(k, lat + width / 2, top_depth + 2.5)
        self.plan.set_rect(1, min(h0[0], h1[0]), max(h0[0], h1[0]), min(h0[1], h1[1]), max(h0[1], h1[1]), WAY, only=(FREE,))

    # ---------------------------------------------------------------- ways

    def connect(self, ids=None, width=None):
        """Reserves a way between every pair of doorways (and through the stair between levels). False if one can't be found."""
        width = width or self.theme.way_width
        ids = list(ids or self.doors)
        ok = True
        for i, a in enumerate(ids):
            for b in ids[i + 1:]:
                ok &= self.way_between(self.door_at(a, 2.0), self.doors[a].level, self.door_at(b, 2.0), self.doors[b].level, width)
        return ok

    def way_between(self, p, lp, q, lq, width=None):
        width = width or self.theme.way_width
        if lp == lq:
            return self.plan.way(lp, p, q, width)
        if not hasattr(self, "stair_link"):
            return False
        l0, s0, l1, s1 = self.stair_link
        if lp == l0:
            return self.plan.way(l0, p, s0, width) and self.plan.way(l1, s1, q, width)
        return self.plan.way(l1, p, s1, width) and self.plan.way(l0, s0, q, width)
