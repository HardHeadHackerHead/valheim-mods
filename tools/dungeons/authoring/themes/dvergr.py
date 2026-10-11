"""
The Dvergr theme (for the test pack "testtheme"): a Dvergr workings sunk in black marble. Tight, mechanical and many-levelled: human
doorways (3 x 4 m), 5 m a storey, rooms on a 4 m grid from 4 to 24 m, split levels and galleries everywhere, steep black marble stairs,
dark wood beams and posts, iron grates, lanterns on hooks and poles, crates, barrels, tables and shelves, small gears. It doesn't do
round halls; its rooms are narrow and stacked. Its signature room is the crane shaft: a three-storey well with galleries round it and
the Dvergr's great crane in the middle.

Pieces are the game's own (ZNetScene); builder2's curated palette (palettes/dvergr.json) replaces this list when it lands.
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, Room

from ..core import side_xz
from ..theme import Theme

MARBLE = "blackmarble_tile_wall_2x4"  # 2 x 4 black marble tiles, upright: the walls
FLOOR = "blackmarble_tile_floor_2x2"  # 2 x 2 black marble tiles: floors and solid levels
PLANKS = "dvergrprops_wood_floor"    # 2 x 0.25 x 2 planks: ceilings, galleries
TRIM = "blackmarble_2x1x1"
POLE = "dvergrtown_wood_pole"        # 1.4 x 4 x 1.3 wooden post
BEAM = "dvergrtown_wood_beam"        # 6.1 x 0.9 x 0.9 beam along x
STEP = "blackmarble_stair"           # 2 x 1 x 2 stair block (climbs -z? see stairs)
LEAF = "piece_dvergr_metal_wall_2x2"
LAMP_POLE = "piece_dvergr_lantern_pole"
LAMP = "dvergrprops_lantern"
HOOK = "dvergrprops_hooknchain"
CHEST = "TreasureChest_dvergrtown"


class Dvergr(Theme):
    name = "dvergr"
    door_type = "dvergr_test"
    door_w, door_h = 3.0, 4.0
    wall_t = 0.6
    floor_t = 0.5
    storey = 5.0
    height = 6.0
    grid = 4.0
    step_rise, step_run = 1.0, 1.4
    way_width = 2.2
    panel = 4.0
    lever_base = (0.8, 0.8)
    music = "forestcrypt"

    palette = {
        "wall": [MARBLE], "floor": [FLOOR], "ceiling": [PLANKS], "trim": [TRIM], "pillar": [POLE], "column": ["blackmarble_column_1"],
        "rail": [BEAM], "grate": ["iron_grate"], "door_leaf": [LEAF], "step": [STEP], "beam": [BEAM], "block": ["blackmarble_2x2x2"],
        "chain": [HOOK], "gear": [gk.GEAR], "light_stand": [LAMP_POLE], "light_hang": [LAMP], "chest": [CHEST],
    }
    shells = {"rect": 2, "L": 2, "split": 3, "balcony": 3}
    sizes = {"nook": ((2, 2), (2, 3)), "small": ((2, 3), (2, 3)), "medium": ((3, 4), (3, 4)), "large": ((4, 5), (4, 5)),
             "corridor": ((1, 1), (3, 4)), "hall": ((5, 5), (6, 7)), "plate": ((3, 4), (5, 6))}
    pit_gaps = (11.0, 12.0, 13.0)        # (more than the best jump across: 10.4 m)
    dense = 2.2
    forbid = set()

    # ---------------------------------------------------------------- pieces

    def wall_panel(self, room, centre, length, height, yaw, prefab=None):
        # marble blocks stand upright already (thin along z): no tilt
        box(room, prefab or MARBLE, centre, (length, height, self.wall_t), yaw=yaw)

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        """Marble tiles at their own thickness on top, a solid block under them for the rest (a tile stretched thick shows its
        bevels as slanted faces)."""
        from dungeon_parts import slab
        thickness = thickness or self.floor_t
        tile = 0.2
        slab(room, x0, x1, z0, z1, top, prefab or FLOOR, thickness=tile, cell=self.panel)
        if thickness > tile + 0.05:
            slab(room, x0, x1, z0, z1, top - tile, "blackmarble_2x2x2", thickness=thickness - tile, cell=self.panel)

    def door_frame(self, room, k, lateral, thr, face):
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (self.door_w / 2 + 0.3), face - 0.3)
            box(room, POLE, (x, thr + self.door_h / 2, z), (0.6, self.door_h, 0.6))
        x, z = side_xz(k, lateral, face - 0.3)
        box(room, BEAM, (x, thr + self.door_h + 0.3, z), (self.door_w + 1.2, 0.6, 0.6), yaw=90 * k)

    def frame_depth(self):
        return 0.6

    def structure(self, sh):
        """A Dvergr room is framed in wood: posts up every wall (each floor level to the floor or ceiling above it), a beam along the
        walls above the doorways, beams across under the ceiling. Posts stand out 0.5 m from the walls and take that floor."""
        from ..core import TAKEN, side_xz as sxz
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        doors = list(sh.doors.values())
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
            n = max(1, int(round(2 * across / 3.5)))
            for i in range(n + 1):
                lat = -across + 0.3 + i * (2 * across - 0.6) / n
                if any(d.k == k and abs(d.lateral - lat) < self.door_w / 2 + 1.0 for d in doors):
                    continue
                x, z = sxz(k, lat, depth - 0.25)
                for lv in range(len(sh.levels)):
                    if not sh.on_level(x, z, lv):
                        continue
                    y0 = sh.levels[lv]
                    above = [sh.levels[m] - 0.5 for m in range(len(sh.levels)) if sh.levels[m] > y0 + 0.1 and sh.on_level(x, z, m)]
                    y1 = min(above) if above else top
                    box(r, POLE, (x, (y0 + y1) / 2, z), (0.5, y1 - y0, 0.5))
                    sh.plan.set_rect(lv, x - 0.3, x + 0.3, z - 0.3, z + 0.3, TAKEN)
                    # every other post a lantern on a bracket, out of reach (over the heads, under the wall beam)
                    ly = y0 + 3.0                     # (none where heights are the puzzle: a lamp by a ledge is a step to it)
                    if i % 2 == 1 and ly + 1.2 < y1 and not getattr(sh, "no_climb", False):
                        lx, lz = sxz(k, lat, depth - 0.95)
                        box(r, BEAM, ((x + lx) / 2, ly + 0.9, (z + lz) / 2), (0.75, 0.14, 0.14), yaw=90 * k + 90)
                        lo, hi = bounds(LAMP)
                        r.add(LAMP, (lx, ly + 0.9 - hi[1] - 0.07, lz))
            # between the posts: a rail at waist height and a brace, bay by bay (not across a doorway)
            posts = [-across + 0.3 + i * (2 * across - 0.6) / n for i in range(n + 1)]
            for i, (pa, pb) in enumerate(zip(posts, posts[1:])):
                mid = (pa + pb) / 2
                if any(d.k == k and abs(d.lateral - mid) < self.door_w / 2 + (pb - pa) / 2 + 0.5 for d in doors):
                    continue
                x, z = sxz(k, mid, depth - 0.15)
                if not sh.on_level(x, z, 0):
                    continue
                y0 = sh.levels[0]
                hb = min(self.door_h, top - y0 - 0.5)
                if getattr(sh, "no_climb", False):
                    # a room whose heights are the puzzle (a ledge, a pit, a drop): nothing within a jump of the floor, only knee
                    # braces up under the beam (from 3 m)
                    for s in (-1, 1):
                        kx, kz = sxz(k, (pa if s < 0 else pb) - s * 0.45, depth - 0.15)
                        box(r, BEAM, (kx, y0 + hb - 0.35, kz), (1.2, 0.16, 0.16), yaw=90 * k, tilt=(0, 45 * s))
                    continue
                box(r, BEAM, (x, y0 + 1.1, z), (pb - pa, 0.22, 0.22), yaw=90 * k)
                ang = math.degrees(math.atan2(hb - 1.3, pb - pa)) * (1 if i % 2 else -1)
                length = math.hypot(pb - pa, hb - 1.3)
                box(r, BEAM, (x, y0 + 1.25 + (hb - 1.3) / 2, z), (length, 0.18, 0.18), yaw=90 * k, tilt=(0, ang))
            # the beam along the wall above the doorways, on every level that runs along this wall
            for lv in range(len(sh.levels)):
                y = sh.levels[lv] + self.door_h + 0.5
                if y + 0.4 > top:
                    continue
                pa, pb = sxz(k, -across, depth - 0.2), sxz(k, across, depth - 0.2)
                # only where this level runs the wall's whole length: a beam over a pit or a drop is a way across it
                if all(sh.on_level(*sxz(k, u, depth - 1.0), lv) for u in np.arange(-across + 0.5, across - 0.4, 1.0)):
                    box(r, BEAM, ((pa[0] + pb[0]) / 2, y, (pa[1] + pb[1]) / 2), (2 * across, 0.4, 0.35), yaw=90 * k)
        # beams across under the ceiling
        along_x = sh.size[0] <= sh.size[2]
        span = (H[2] if along_x else H[0]) - self.wall_t
        width = (H[0] if along_x else H[2]) - self.wall_t
        n = max(1, int(round(2 * span / 4.0)))
        for i in range(1, n):
            u = -span + i * 2 * span / n
            c = (0, top - 0.3, u) if along_x else (u, top - 0.3, 0)
            box(r, BEAM, c, (2 * width, 0.5, 0.45), yaw=0 if along_x else 90)

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Steep black marble flights: a stair block per metre of rise, solid underneath."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, i * h, step * (i + 0.5)), yaw)
            box(holder, STEP, (c[0], c[1] + h / 2, c[2]), (width, h, step), yaw=(yaw + 180) % 360)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                box(holder, FLOOR, (c[0], fill_below + hh / 2, c[2]), (width, hh, step), yaw=yaw)

    def rail(self, room, a, b, y, height=1.1):
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        mid = (a + b) / 2
        box(room, BEAM, (mid[0], y + height - 0.15, mid[2]), (length, 0.3, 0.3), yaw=yaw)
        n = max(2, int(length / 2.0) + 1)
        for i in range(n):
            p = a + d * (i / (n - 1))
            box(room, POLE, (p[0], y + height / 2, p[2]), (0.25, height, 0.25))

    def pillar(self, room, at, height, width=None):
        w = width or 0.9
        box(room, POLE, (at[0], at[1] + height / 2, at[2]), (w, height, w))
        return (w, w)

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        from dungeon_parts import lever
        return lever(room, id, at, yaw, wires, reached_from=reached_from, handle="piece_dvergr_pole", knob="blackmarble_head01",
                     post="blackmarble_column_1", cap="blackmarble_2x2x1", size=1.0)

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (1.4, 1.0)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    def light_stand(self, room, at):
        # a lantern on a short stand: never a stepping stone (a 3.5 m lantern pole is one, up to a 5 m gallery)
        room.stand("dvergrprops_lantern_standing", at, scale=(1.6, 1.6, 1.6))
        return (0.8, 0.8)

    def light_hang(self, room, at):
        """A lantern on a hook and chain from whatever is above it."""
        lo, hi = bounds(LAMP)
        at = np.array(at, float)
        above = [b0[1] for b0, b1 in map(gk.part_box, room.parts)
                 if b0[1] > at[1] + hi[1] + 0.05 and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2]]
        if above:
            top = min(above)
            clo, chi = bounds(HOOK)
            length = top - (at[1] + hi[1])
            s = max(0.2, length / (chi[1] - clo[1]))
            room.add(HOOK, (at[0], top - chi[1] * s, at[2]), (0, 0, 0), (1, s, 1))
        room.add(LAMP, at)

    def accent(self, room, at, yaw=0.0):
        room.stand("dvergrprops_lantern_standing", at, yaw=yaw, scale=(1.6, 1.6, 1.6))
        return (0.8, 0.8)

    def chain(self, holder, top, length, yaw=0.0):
        clo, chi = bounds(HOOK)
        s = max(0.2, length / (chi[1] - clo[1]))
        holder.add(HOOK, (top[0], top[1] - chi[1] * s, top[2]), (0, yaw, 0), (1.4, s, 1.4))

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        return gk.gear(holder, centre, diameter, axis=axis, yaw=yaw, thickness=thickness, spin=spin, turn=turn)

    def girder(self, holder, a, b, size=0.4):
        return gk.girder(holder, a, b, size, prefab=BEAM)

    def axle(self, holder, foot, height, width=0.5):
        box(holder, POLE, (foot[0], foot[1] + height / 2, foot[2]), (width, height, width))

    def bar(self, holder, a, b, width=0.3):
        return gk.girder(holder, a, b, width, prefab=BEAM)

    def big_gear_wall(self, room, machine, k, lateral, y, depth, diameter, turn):
        x, z = side_xz(k, lateral, depth)
        self.gear(machine, (x, y, z), min(diameter, 3.2), axis="z", yaw=90 * k, thickness=0.25, turn=turn * 1.8)

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, wall=None, sets=(): {"prefab": name, "scale": s, "size": (1.0, 1.0), "wall": wall, "sets": sets}
        return [
            S("dvergrprops_crate", 1.0, True, ("store", "workshop", "barracks")),
            S("dvergrprops_crate_long", 1.0, True, ("store", "workshop")),
            S("dvergrprops_barrel", 1.0, True, ("store", "tavern")),
            S("dvergrprops_table", 1.0, False, ("tavern", "workshop", "barracks")),
            S("dvergrprops_chair", 1.0, None, ("tavern", "barracks")),
            S("dvergrprops_stool", 1.0, None, ("tavern", "workshop")),
            S("dvergrprops_shelf", 1.0, True, ("store", "workshop")),
            S("dvergrprops_bed", 1.0, True, ("barracks",)),
            S("dvergrprops_banner", 1.0, True, ("barracks", "tavern")),
            S("blackmarble_pile", 1.0, True, ("workshop", "store")),
        ]

    def dressing_sets(self):
        return {"store": 1.0, "workshop": 1.0, "tavern": 0.7, "barracks": 0.6}

    def remains(self):
        return [("dvergrprops_crate", (1.0, 1.0, 1.0), 0.0), ("dvergrprops_barrel", (1.5, 1.6, 1.8), 0.0)]

    def nook_pieces(self):
        def stand(name, s=1.0):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))
        return [
            {"name": "shelf", "main": True, "size": (2.0, 0.8), "build": stand("dvergrprops_shelf")},
            {"name": "bed", "main": True, "size": (1.4, 2.2), "build": stand("dvergrprops_bed")},
            {"name": "barrels", "main": True, "size": (1.8, 1.9), "build": stand("dvergrprops_barrel")},
            {"name": "table", "main": True, "size": (2.0, 1.5), "build": stand("dvergrprops_table")},
            {"name": "crate", "size": (1.1, 1.1), "build": stand("dvergrprops_crate")},
            {"name": "stool", "size": (0.8, 0.8), "build": stand("dvergrprops_stool")},
            {"name": "lantern", "size": (0.8, 0.8), "build": stand("dvergrprops_lantern_standing", 1.6)},
        ]

    # ---------------------------------------------------------------- signature rooms

    def signatures(self):
        return {"crane_shaft": self.crane_shaft, "entrance": self.entrance, "cap_plain": self.cap_plain, "vault": self.vault}

    def _shell(self, name, size, doors, **kw):
        """A plain room of this theme with doorways [(id, side, lateral, level y)], for the hand-made rooms."""
        from ..shells import Shell
        from ..core import rng_for
        sh = Shell(self, name, "rect", size, rng_for(name), **kw)
        for id, k, lat, y in doors:
            d = sh.add_door(id, k, lat, 0)
            d.y = y
        sh.build()
        return sh

    def entrance(self):
        """The way in: a black marble hall with lantern poles and a banner either side."""
        sh = self._shell("entrance", (12, 6, 16), [("in", 2, 0.0, -2.5), ("n", 0, 0.0, -2.5), ("e", 1, 2.0, -2.5)], tags=["entrance"])
        r = sh.room
        r.entrance = True
        r.doorways[0]["entrance"] = True
        for x in (-4.4, 4.4):
            self.light_stand(r, (x, -2.5, -4.0))
            r.stand("dvergrprops_banner", (x * 1.1, -2.5, 3.0), yaw=90 if x < 0 else 270)
        return r

    def cap_plain(self):
        """End cap: the doorway walled up with marble."""
        r = Room("dvergr_cap", (4, 6, 0), end_cap=True, weight=0.3)
        r.doorway("in", (0, -2.5, 0), (0, 0, 1), type=self.door_type)
        box(r, MARBLE, (0, -0.5, 0.3), (self.door_w + 0.4, self.door_h + 0.2, 0.6))
        return r

    def vault(self):
        """The end: a small strongroom with the chest on a marble plinth, crates of what the Dvergr kept."""
        sh = self._shell("vault", (12, 6, 12), [("in", 2, 0.0, -2.5)], tags=["end"], max=1)
        r = sh.room
        box(r, TRIM, (0, -2.25, 3.0), (3.0, 0.5, 2.0))
        r.stand(CHEST, (0, -2.0, 3.2), yaw=180, saved=True)
        for x in (-4.0, 4.0):
            r.stand("dvergrprops_crate", (x, -2.5, 4.0))
            self.light_stand(r, (x, -2.5, -3.5))
        return r

    def crane_shaft(self):
        """The crane shaft (signature): a three-storey square well, 16 m across, the Dvergr's great crane standing in its open middle
        from the floor to the roof, its arm out over the south side with a hook on a chain. A gallery runs along the west and north
        walls a storey up, another along the east wall two storeys up; steep flights climb along the south wall (floor to the west
        gallery) and along the north gallery (to the east one). Doorways on all three floors."""
        S = self.storey
        size = (16.0, 3 * S + 1.5, 16.0)
        bottom = -size[1] / 2
        y0 = bottom + self.floor_t
        ys = [y0, y0 + S, y0 + 2 * S]
        H = 8.0 - self.wall_t                                    # the inner walls' faces
        g = 3.0                                                   # gallery depth
        w = 2.2                                                   # flight width
        sh = self._shell("crane_shaft", size, [("a", 0, 0.0, ys[0]), ("b", 3, 0.0, ys[1]), ("c", 1, 3.0, ys[2])], max=1,
                         tags=["shaft"])
        r = sh.room
        run = self.stair_run(S)
        # the galleries: west and north a storey up, east two storeys up
        self.floor(r, -H, -H + g, -H, H, ys[1], thickness=0.4, prefab=PLANKS)
        self.floor(r, -H + g, H, H - g, H, ys[1], thickness=0.4, prefab=PLANKS)
        self.floor(r, H - g, H, -H, H, ys[2], thickness=0.4, prefab=PLANKS)
        # flight 1: along the south wall, from the floor (east) up to the west gallery's south end
        self.stairs(r, (-H + g + run, ys[0], -H + w / 2), S, run, w, yaw=270, fill_below=ys[0])
        # flight 2: along the north gallery, from its west part up to the east gallery's north end
        self.stairs(r, (H - g - run, ys[1], H - w / 2), S, run, w, yaw=90, fill_below=None)
        # rails on the galleries' open edges (leaving the flights' heads)
        self.rail(r, (-H + g + 0.2, 0, -H + w + 0.3), (-H + g + 0.2, 0, H - g - 0.2), ys[1])         # west gallery's edge
        self.rail(r, (-H + g + 0.2, 0, H - g - 0.2), (H - g - run - 0.4, 0, H - g - 0.2), ys[1])     # north gallery's edge
        self.rail(r, (H - g - 0.2, 0, -H + 0.3), (H - g - 0.2, 0, H - w - 0.3), ys[2])               # east gallery's edge
        # the crane (its arm out over the south), the hook, the lights
        box(r, "dvergrtown_wood_crane", (0, y0 + 6.2, -2.0), (6.23, 12.4, 1.34), yaw=90)
        self.chain(r, (0.3, y0 + 10.5, -6.0), 3.5)
        for x, z, y in ((-5.5, 4.5, ys[1] + 3.5), (5.5, -4.5, ys[2] + 3.0), (0.0, 4.5, ys[1] + 3.5)):
            self.light_hang(r, (x, y, z))
        for x, z in ((5.6, 5.0), (-3.0, -4.0)):
            self.light_stand(r, (x, ys[0], z))
        for x, z in ((5.5, 1.5), (-5.0, 2.5)):
            r.stand("dvergrprops_crate", (x, ys[0], z))
        r.stand("dvergrprops_barrel", (4.5, ys[0], -1.5))
        return r


THEME = Dvergr()
