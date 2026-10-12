"""
A theme is architecture, not a texture: how big a place is and how it is built. It answers, for every recipe:
  - scale: doorway size, wall thickness, floor-to-floor height, room sizes, how steep a stair is, how big a lever is;
  - what walls, floors, ceilings, pillars, rails, bars, gates, levers, plates, lights, gears and props are made of;
  - which shells it likes (round halls, L-rooms, split levels...), which jobs it reshapes or forbids, and its own features;
  - the rooms only it has (signature rooms).
Recipes only ever build through a theme. The base class here builds everything generically from the theme's pieces (its "palette":
role -> prefab); a theme overrides what it does its own way (the giant theme's levers, the Dvergr's spiral stairs).
"""
import math

import numpy as np

from dungeon_parts import box, bounds, cuts, yaw_turn, euler_matrix, slab

from .core import side_xz


class Theme:
    name = "base"
    door_type = "base"
    door_w, door_h = 4.0, 5.0          # a doorway's opening
    wall_t = 1.0                       # wall thickness (inside the room's box)
    floor_t = 1.0                      # the floor slab under the lowest level
    storey = 6.0                       # floor to floor
    height = 8.0                       # a one-storey room's box height
    grid = 4.0                         # room sizes are multiples of this
    step_rise, step_run = 0.5, 0.75    # a stair's step
    way_width = 3.0                    # the clear width kept between doorways
    panel = 8.0                        # the largest wall, floor or ceiling piece (metres): pieces stretched further look smeared
    lever_base = (1.0, 1.0)            # a lever's footprint (x, z), pulled from its -z
    music = None                       # the pack's default track inside

    palette = {}                       # role -> [prefab, ...]: wall, floor, ceiling, trim, pillar, column, rail, grate, door_leaf,
    #                                    step, beam, chain, gear, light_stand, light_hang, chest, decor ...

    # recipes: which shells this theme likes, and size ranges per size class (x, z ranges, in grid units)
    shells = {"rect": 3, "L": 1, "round": 1, "split": 1}
    sizes = {"small": ((2, 2), (2, 3)), "medium": ((3, 4), (3, 5)), "large": ((5, 6), (5, 7)), "corridor": ((2, 2), (4, 6))}
    forbid = set()                     # jobs this theme doesn't have
    reshape = {}                       # job -> {param: value} overrides (e.g. {"timing": {"shells": ["rect"]}})

    def p(self, role, i=0):
        names = self.palette[role]
        return names[i % len(names)]

    # ---------------------------------------------------------------- shell pieces

    def wall_run(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=None, cell=None):
        """A straight wall on side k (its outer face `depth` from the middle, facing in), from lat_lo to lat_hi along the side, y0 to
        y1, with doorway openings [(lateral, threshold)] of the theme's doorway size. Slabs of the wall piece over a grid cut at the
        openings' edges."""
        prefab = prefab or self.p("wall")
        cell = cell or self.panel
        w, h = self.door_w, self.door_h
        cols = cuts(lat_lo, lat_hi, [e for l, _ in openings for e in (l - w / 2, l + w / 2)], cell / 2)
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
            x, z = side_xz(k, (a + b) / 2, depth - self.wall_t / 2)
            self.wall_panel(room, (x, (lo + hi) / 2, z), b - a, hi - lo, 90 * k, prefab)

    def wall_panel(self, room, centre, length, height, yaw, prefab=None):
        """One wall slab: `length` along its face, `height` tall, the theme's thickness, its face towards -z turned by yaw (yaw 0:
        a north wall, facing south into the room)."""
        box(room, prefab or self.p("wall"), centre, (length, self.wall_t, height), yaw=yaw, tilt=(-90, 0))

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        slab(room, x0, x1, z0, z1, top, prefab or self.p("floor"), thickness=thickness or self.floor_t, cell=self.panel)

    def ceiling(self, room, x0, x1, z0, z1, top, thickness=None):
        slab(room, x0, x1, z0, z1, top, self.p("ceiling"), thickness=thickness or self.floor_t, cell=self.panel, ceiling=True)

    def block(self, room, x0, x1, z0, z1, y0, y1, prefab=None):
        """Solid stone from y0 to y1 over a rectangle (a raised level, a platform)."""
        slab(room, x0, x1, z0, z1, y1, prefab or self.p("floor"), thickness=y1 - y0, cell=self.panel)

    def door_frame(self, room, k, lateral, thr, face):
        """Posts and a lintel round a doorway on the wall's inside face (`face`: its depth)."""
        post = self.p("column")
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (self.door_w / 2 + 0.3), face - 0.3)
            room.fill(post, (x, thr + self.door_h / 2, z), np.abs(yaw_turn((0.6, self.door_h, 0.6), 90 * k)))
        x, z = side_xz(k, lateral, face - 0.3)
        room.fill(self.p("trim"), (x, thr + self.door_h + 0.3, z), np.abs(yaw_turn((self.door_w + 1.2, 0.6, 0.6), 90 * k)))

    dense = 1.0                        # how much dressing a room gets (1: the base amount)

    def structure(self, shell):
        """The theme's own framing on a shell's walls (posts, beams, ribs), built after its walls and levels: none by default."""

    def frame_depth(self):
        """How far a door frame stands into the room from the wall's inside face."""
        return 0.6

    # ---------------------------------------------------------------- stairs, rails, bars, pillars

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """A flight from `foot` (its foot's middle) climbing `rise` over `run` towards +z turned by yaw; fill_below: solid under it
        down to that height."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, (i + 0.5) * h, step * (i + 0.5)), yaw)
            box(holder, self.p("step"), c, (width, h, step), yaw=yaw)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                c2 = np.array(foot, float) + yaw_turn((0, 0, step * (i + 0.5)), yaw)
                box(holder, self.p("floor"), (c2[0], fill_below + hh / 2, c2[2]), (width, hh, step), yaw=yaw)

    def stair_run(self, rise):
        """The run a flight of this rise needs."""
        return rise / self.step_rise * self.step_run

    def rail(self, room, a, b, y, height=None):
        """A balustrade along the floor-plan line a..b, standing on height y."""
        height = height or 1.3
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2]))
        mid = (a + b) / 2
        box(room, self.p("rail"), (mid[0], y + height / 2, mid[2]), (0.5, height, length), yaw=yaw)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        """Floor-to-ceiling bars between floor-plan points a and b: nothing passes or climbs them."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        mid = (a + b) / 2
        n = max(1, int(math.ceil(length / 3.0)))
        for i in range(n):
            c = a + d * ((i + 0.5) / n)
            box(holder, self.p("grate"), (c[0], (bottom + top) / 2, c[2]), (length / n + 0.02, top - bottom, thickness), yaw=yaw)

    def trap_head(self, holder, centre, size):
        """What a slamming trap brings down (the corridor's hammers): `size` (x, y, z) about `centre` in the trap's frame, its foot
        what strikes. Default: a block of the theme's stone. A theme may hang anything there (spikes, a bell...): the trap, its poses,
        area and warning stay the recipe's."""
        box(holder, self.p("block"), centre, size)

    def pillar(self, room, at, height, width=1.2):
        """A pillar standing at `at`, `height` tall; returns its footprint (x, z)."""
        room.fill(self.p("pillar"), (at[0], at[1] + height / 2, at[2]), (width, height, width))
        return (width, width)

    # ---------------------------------------------------------------- mechanisms

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        from dungeon_parts import lever
        return lever(room, id, at, yaw, wires, reached_from=reached_from)

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """A leaf in side k's doorway (a gate closing that doorway in state 0), sliding sideways into the wall to open, or rising if
        the room is too narrow for that."""
        from .core import side_xz as sxz
        x, z = sxz(k, lateral, half - self.wall_t / 2)
        g = room.mech(id, kind, (x, thr, z), (0, (90 * k) % 360, 0))
        if kind == "gate":
            g.doorway = doorway
        g.fill(self.p("door_leaf"), (0, self.door_h / 2, 0), (self.door_w + 0.3, self.door_h + 0.1, self.wall_t * 0.8))
        across = room.size[0 if k % 2 == 0 else 2] / 2
        if abs(lateral + open_dir * (self.door_w + 0.6)) + (self.door_w + 0.3) / 2 <= across:
            g.pose().pose(at=(open_dir * (self.door_w + 0.6), 0, 0))
        else:
            g.pose().pose(at=(0, room.size[1] / 2 - 0.05 - (thr + self.door_h + 0.1), 0))
        g.start = start
        g.seconds = 2.0
        return g

    def plate(self, room, id, at, size=2.4):
        p = room.mech(id, "plate", at)
        p.fill(self.p("trim"), (0, 0.08, 0), (size, 0.16, size))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        return p

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(self.p("chest"), at, yaw=yaw, saved=saved)
        return (1.6, 1.2)

    def spawner(self, room, role, at):
        names = {"melee": "Spawner_Draugr", "ranged": "Spawner_Draugr_Ranged", "flying": "Spawner_Ghost"}
        bounds(names[role])
        room.add(names[role], (at[0], at[1] + 0.5, at[2]), saved=True)

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at):
        room.stand(self.p("light_stand"), at)
        return (1.0, 1.0)

    def light_hang(self, room, at):
        from giant_kit import wisp
        wisp(room, at)

    # ---------------------------------------------------------------- machinery and dressing

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        from giant_kit import gear
        return gear(holder, centre, diameter, axis=axis, yaw=yaw, thickness=thickness, spin=spin, turn=turn)

    def machine(self, room, id, at=(0, 0, 0)):
        m = room.mech(id, "machine", at)
        m.pose()
        return m

    def decor(self):
        """The props a breather can be dressed with: [{"prefab", "scale", "size": (x, z), "wall": True/False/None, "set": name}]."""
        return []

    def dressing_sets(self):
        """Named sets of dressing (an archive, a forge...): {name: [decor prefab names]}; a room picks one set."""
        return {}

    def signatures(self):
        """{name: fn(rng) -> Room}: rooms only this theme has, built by hand."""
        return {}
