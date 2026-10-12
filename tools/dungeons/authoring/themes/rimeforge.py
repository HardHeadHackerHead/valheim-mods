"""
The rimeforge theme (pack "rimeforge", Rimeforge Depths): a frost-giants' smithy that a glacier swallowed. The forges never quite went
out: they still smoulder inside the ice. Tall, narrow forge-naves: doorways 4 x 6 m, 8 m a storey, rooms on a 4 m grid, mostly longer
than wide. How it is built (architecture, not a texture):
  - walls: a black masonry plinth (Ashlands fortress stone) to 3 m over the floor, and glacier ice grown over everything above it;
  - black iron (flametal) ribs up every wall, iron beams across under an ice ceiling, icicles hanging high between them;
  - doorways framed in black iron, gates of iron bars, levers of black iron with a glowing ember lantern for a knob;
  - light: molten orange (ember lanterns, hanging fire bowls, forge vents in the walls) against the ice's own pale blue;
  - dressing: giants' anvils, ingot stacks, coal, frozen slag and ice; remains: a giant's anvil, an ice boulder, a giant's frozen arm.
Its signature rooms (hand made below) are the Frozen Forges (thaw the ice wall by lighting three furnaces at once) and the Hammer Line
(trip-hammer presses on a beat, steam vents between them); its boss rooms are in bosses_rimeforge.py (the Bellows-Keeper, the
Rimesmith). Pieces measured with palette_tool.py --probe (their sizes, solidity, climb heights).
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, Room, slab, euler_matrix

from ..core import side_xz, TAKEN, OUT, FREE, WAY
from ..theme import Theme

ICE = "Ice_floor"                       # 4.7 x 2.1 x 4.7 faceted glacier ice: upper walls, ceilings, ice blocks
STONE = "Ashlands_Fortress_Floor"       # 2 x 1 x 2 black fortress stone: floors, the plinth, solid levels
IRON = "Piece_flametal_beam"            # 2 x 0.56 x 0.56 black iron beam (along x): lintels, beams, rails
RIB = "Piece_flametal_pillar"           # 0.5 x 2 x 0.5 black iron post: ribs, door posts, lever handles
BARS = "Morkhalla_GateDoor"             # 4.6 x 7.6 iron bars: gate leaves
GRATE = "iron_grate"
FLOORGRATE = "iron_floor_2x2"           # 2 x 0.12 x 2 iron floor grate: over the slag channels
STEP = "Ashland_Stair"                  # 4 x 2 x 4 stone stair: a 1 m step (its own small steps inside)
ANVIL = "forge_ext4"                    # the smith's anvil: a giant's at 4-6 times
EMBER = "piece_Lavalantern"             # a small lantern with an orange 12 m light: knobs, vents, slag channels
LAMP = "deepnorth_lantern_standing"     # a 3.25 m iron lantern pole, orange: can't be climbed
BOWL = "piece_brazierceiling01"         # a hanging fire bowl (its origin at its foot; hangs on a chain)
ICICLE = "caverock_ice_stalagtite"
ICESPIKE = "caverock_ice_stalagmite"
COAL = "Morkhalla_coal_pile_memorial"   # smouldering coal (dim red light)
CHEST = "TreasureChest_mountaincave"
VAULT_CHEST = "Morkhalla_ChestAncient"
CHAIN = gk.CHAIN

TYPE = "rimeforge"
VAULT_TYPE = "rimeforge_vault"          # the Great Forge to the vault: only those two have one
PLINTH = 3.0                            # the masonry's height over the ground floor
BACK = "blackmarble_floor_large"        # the thin black slab behind every wall and over every ceiling (closed against the sky; 8 x 2 x 8
                                        # natively, so it isn't stretched far)
BACK_T = 0.12
BACK_CELL = 24.0                        # (hidden: big slabs, few parts)
WALL_CELL = 7.0                         # wall panels (ice and plinth): fewer, bigger pieces than the 4.5 m panel
CEIL_CELL = 9.0
FLOOR_CELL = 8.0
RIB_GAP = 6.5                           # iron ribs up the walls and beams under the ceiling, this far apart
LAMPS_PER_ROOM = 3
BOWLS_PER_ROOM = 1


def post(holder, centre, w, h, d=None):
    """A black iron post `h` tall (w x d across) standing at `centre` (its middle): an iron beam stood on end. (Not the flametal
    pillar stretched: the gap check reads that prefab's footholds as a climb to its top, whatever its height.)"""
    return box(holder, IRON, centre, (h, w, d or w), tilt=(0, 90))


def hang_bowl(room, at):
    """A fire bowl hung on an iron chain from whatever is above `at` (the ceiling, a beam): its bowl's foot at `at`."""
    lo, hi = bounds(BOWL)
    at = np.array(at, float)
    top = at[1] + (hi[1] - lo[1])
    above = [b0[1] for b0, b1 in map(gk.part_box, room.parts)
             if b0[1] > top + 0.05 and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2]]
    if above:
        gk.hang_chain(room, (at[0], min(above), at[2]), min(above) - top, thin=True)
    room.add(BOWL, at - np.array([0, lo[1], 0]))


def iron(holder, a, b, size=0.5):
    """A black iron beam from point a to point b."""
    return gk.girder(holder, a, b, size, prefab=IRON)


def anvil(room, at, yaw=0.0, s=5.0):
    """A giant's anvil standing at `at` (s times the smith's own). Returns its footprint (x, z) before the yaw."""
    lo, hi = bounds(ANVIL)
    room.stand(ANVIL, at, yaw=yaw, scale=(s, s, s))
    return ((hi[0] - lo[0]) * s, (hi[2] - lo[2]) * s)


def slag_channel(room, a, b, y, width=1.4, depth=0.7):
    """A slag channel sunk in a floor whose top is `y`, from floor-plan point a to b (along x or z): a trench lined with black stone,
    smouldering coal and ember light in it, and iron grates over it flush with the floor (walked over, never fallen into). The floor
    slab under the strip must already be cut away by the caller (or the channel stands in a raised kerb)."""
    a, b = np.array(a, float), np.array(b, float)
    d = b - a
    length = float(math.hypot(d[0], d[2]))
    yaw = math.degrees(math.atan2(d[0], d[2]))
    mid = (a + b) / 2
    box(room, STONE, (mid[0], y - depth - 0.25, mid[2]), (width, 0.5, length), yaw=yaw)            # its bed
    n = max(1, int(length // 4.0))
    for i in range(n):
        p = a + d * ((i + 0.5) / n)
        box(room, COAL, (p[0], y - depth + 0.15, p[2]), (width * 0.8, 0.3, min(3.0, length / n * 0.8)), yaw=yaw + 30 * (i % 2))
        if i % 2 == 0:
            room.stand(EMBER, (p[0], y - depth, p[2]), scale=(0.8, 0.5, 0.8))
    for i in range(int(math.ceil(length / 2.0))):
        u = min(length, (i + 1) * 2.0) - 1.0
        p = a + d * (u / length)
        box(room, FLOORGRATE, (p[0], y - 0.06, p[2]), (width + 0.3, 0.12, min(2.0, length - i * 2.0) + 0.02), yaw=yaw)


class Rimeforge(Theme):
    name = "rimeforge"
    door_type = TYPE
    door_w, door_h = 4.0, 6.0
    wall_t = 1.2
    floor_t = 1.0
    storey = 8.0
    height = 10.0
    grid = 4.0
    step_rise, step_run = 1.0, 1.5
    way_width = 3.0
    panel = 4.5
    lever_base = (1.6, 1.6)
    music = "forestcrypt"
    structure_prefabs = (RIB, IRON, STONE, BACK)
    ground = None                       # a hand-made room with a deep floor sets its floor here while its walls are built

    palette = {
        "wall": [ICE], "floor": [STONE], "ceiling": [ICE], "trim": [STONE], "pillar": [IRON], "column": [IRON], "rail": [IRON],
        "grate": [GRATE], "door_leaf": [BARS], "step": [STEP], "beam": [IRON], "block": [ICE], "chain": [CHAIN],
        "light_stand": [LAMP], "light_hang": [BOWL], "chest": [CHEST],
    }
    # long naves and stepped halls: no round rooms (the giants' vault has those), L-rooms and split levels
    shells = {"rect": 2, "L": 2, "split": 3, "balcony": 2.5, "round": 1}
    sizes = {"nook": ((2, 3), (2, 3)), "small": ((3, 4), (4, 5)), "medium": ((4, 5), (5, 7)), "large": ((6, 7), (7, 9)),
             "corridor": ((2, 2), (4, 6)), "hall": ((6, 7), (8, 9)), "plate": ((4, 5), (6, 7))}
    pit_gaps = (12.0, 13.0, 14.0)        # (more than the best jump across: 10.9 m)
    dense = 1.3
    forbid = set()
    reshape = {"hub": {"shapes": ("rect", "L", "balcony")}, "gate": {"shapes": ("rect", "split")}, "nook": {"shapes": ("rect",)}, "ambush": {"shapes": ("rect",)},
               "guardian": {"shapes": ("rect",)}}

    # ---------------------------------------------------------------- the shell's pieces

    def wall_run(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=None, cell=None):
        """The masonry plinth up to PLINTH over the ground floor, glacier ice above it (each cut at the doorways)."""
        yp = (self.ground if self.ground is not None else y0 + self.floor_t) + PLINTH
        if prefab is not None or yp >= y1 - 0.5:
            return Theme.wall_run(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=prefab, cell=cell)
        Theme.wall_run(self, room, k, depth, lat_lo, lat_hi, y0, yp, openings, prefab=STONE, cell=cell or WALL_CELL)
        Theme.wall_run(self, room, k, depth, lat_lo, lat_hi, yp, y1, openings, prefab=ICE, cell=cell or WALL_CELL)

    def backing(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings):
        """A thin black slab on side k's outer face behind the wall (cut at the doorways): the faceted ice doesn't fill its box, and
        without this the sky shows through the gaps between the panels (in the game: white light above and behind the walls)."""
        saved = self.wall_t
        self.wall_t = BACK_T
        try:
            Theme.wall_run(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=BACK, cell=BACK_CELL)
        finally:
            self.wall_t = saved

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        slab(room, x0, x1, z0, z1, top, prefab or STONE, thickness=thickness or self.floor_t, cell=FLOOR_CELL)

    def block(self, room, x0, x1, z0, z1, y0, y1, prefab=None):
        slab(room, x0, x1, z0, z1, y1, prefab or STONE, thickness=y1 - y0, cell=FLOOR_CELL)

    def ceiling(self, room, x0, x1, z0, z1, top, thickness=None):
        """The ice ceiling, and a thin black slab over it (closed against the sky whatever the ice's facets leave open)."""
        slab(room, x0, x1, z0, z1, top - BACK_T, ICE, thickness=(thickness or self.floor_t) - BACK_T, cell=CEIL_CELL, ceiling=True)
        slab(room, x0, x1, z0, z1, top, BACK, thickness=BACK_T, cell=BACK_CELL, ceiling=True)

    def wall_panel(self, room, centre, length, height, yaw, prefab=None):
        box(room, prefab or ICE, centre, (length, self.wall_t, height), yaw=yaw, tilt=(-90, 0))

    def door_frame(self, room, k, lateral, thr, face):
        """Black iron posts, a double lintel and a short hanging chain on each side of the head."""
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (self.door_w / 2 + 0.35), face - 0.35)
            post(room, (x, thr + self.door_h / 2 + 0.3, z), 0.7, self.door_h + 0.6, 0.7)
        x, z = side_xz(k, lateral, face - 0.35)
        box(room, IRON, (x, thr + self.door_h + 0.35, z), (self.door_w + 1.6, 0.7, 0.7), yaw=90 * k)
        x2, z2 = side_xz(k, lateral, face - 0.3)
        box(room, STONE, (x2, thr + self.door_h + 1.0, z2), (self.door_w + 2.2, 0.6, 0.6), yaw=90 * k)

    def frame_depth(self):
        return 0.7

    def structure(self, sh):
        """Black iron ribs up every wall, out of the doorways and ways (each level's floor to the ceiling or the level above), beams
        across under the ceiling, and icicles hanging high between the beams in rooms of one floor."""
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        doors = list(sh.doors.values())
        # the backing: every side of the box closed behind its walls (a round or L room's too: its corners lie inside it)
        for k in range(4):
            depth = H[2] if k % 2 == 0 else H[0]
            across = H[0] if k % 2 == 0 else H[2]
            self.backing(r, k, depth, -across, across, sh.bottom, sh.top, [(d.lateral, d.y) for d in doors if d.k == k])
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
            n = max(1, int(round(2 * across / RIB_GAP)))
            for i in range(n + 1):
                lat = -across + 0.4 + i * (2 * across - 0.8) / n
                if any(d.k == k and abs(d.lateral - lat) < self.door_w / 2 + 1.2 for d in doors):
                    continue
                x, z = side_xz(k, lat, depth - 0.3)
                for lv in range(len(sh.levels)):
                    if not sh.on_level(x, z, lv):
                        continue
                    y0 = sh.levels[lv]
                    above = [sh.levels[m] - 1.0 for m in range(len(sh.levels)) if sh.levels[m] > y0 + 0.1 and sh.on_level(x, z, m)]
                    y1 = min(above) if above else top
                    if y1 - y0 < 2.0:
                        continue
                    post(r, (x, (y0 + y1) / 2, z), 0.6, y1 - y0, 0.6)
                    sh.plan.set_rect(lv, x - 0.4, x + 0.4, z - 0.4, z + 0.4, TAKEN)
        # beams across under the ceiling, over the room's own floor
        along_x = sh.size[0] <= sh.size[2]
        span = (H[2] if along_x else H[0]) - self.wall_t
        width = (H[0] if along_x else H[2]) - self.wall_t
        n = max(1, int(round(2 * span / RIB_GAP)))
        beams = []
        for i in range(1, n):
            u = -span + i * 2 * span / n
            ts = np.arange(-width, width + 0.01, 0.25)
            inside = [sh.in_shape(*((t, u) if along_x else (u, t)), margin=-0.2) for t in ts]
            runs, start = [], None
            for t, ok in zip(ts, inside):
                if ok and start is None:
                    start = t
                if not ok and start is not None:
                    runs.append((start, t - 0.25))
                    start = None
            if start is not None:
                runs.append((start, ts[-1]))
            for t0, t1 in runs:
                if t1 - t0 < 1.0:
                    continue
                m = (t0 + t1) / 2
                c = (m, top - 0.35, u) if along_x else (u, top - 0.35, m)
                box(r, IRON, c, (t1 - t0, 0.6, 0.5), yaw=0 if along_x else 90)
                beams.append(u)
        # icicles between the beams, high over a one-floor room (their tips 5 m or more over the floor: out of any jump's reach)
        if len(sh.levels) == 1 and top - sh.floor_y >= 8.5 and not getattr(sh, "no_climb", False):
            from ..core import rng_for
            rng = rng_for(sh.name, "icicles", *[round(v, 1) for v in sh.size])
            us = sorted(beams)
            gaps = [(a + b) / 2 for a, b in zip([-span] + us, us + [span])]
            for g in gaps:
                for _ in range(2):
                    t = rng.uniform(-width + 1.5, width - 1.5)
                    x, z = (t, g) if along_x else (g, t)
                    if not sh.in_shape(x, z, margin=1.0):
                        continue
                    s = rng.uniform(0.7, 1.0)
                    lo, hi = bounds(ICICLE)
                    length = (hi[1] - lo[1]) * s
                    if top - length < sh.floor_y + 5.0:
                        continue
                    r.add(ICICLE, (x, top - hi[1] * s, z), (0, rng.uniform(0, 360), 0), (s, s, s))

    # ---------------------------------------------------------------- stairs, rails, bars, pillars

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Black stone flights: a stair block per metre of rise (the stone stair's own little steps inside), solid underneath."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, i * h, step * (i + 0.5)), yaw)
            box(holder, STEP, (c[0], c[1] + h / 2, c[2]), (width, h, step), yaw=(yaw + 180) % 360)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                box(holder, STONE, (c[0], fill_below + hh / 2, c[2]), (width, hh, step), yaw=yaw)

    def rail(self, room, a, b, y, height=1.6):
        """An iron railing: a black iron top beam on short iron posts."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        mid = (a + b) / 2
        box(room, IRON, (mid[0], y + height - 0.2, mid[2]), (length, 0.4, 0.4), yaw=yaw)
        box(room, IRON, (mid[0], y + height * 0.45, mid[2]), (length, 0.3, 0.3), yaw=yaw)
        n = max(2, int(length / 2.5) + 1)
        for i in range(n):
            p = a + d * (i / (n - 1))
            post(room, (p[0], y + height / 2, p[2]), 0.35, height, 0.35)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        gk.grate_wall(holder, a, b, bottom, top, thickness)

    def pillar(self, room, at, height, width=None):
        """A black iron pillar on a stone foot, a stone collar at the top."""
        w = width or 1.4
        box(room, STONE, (at[0], at[1] + 0.4, at[2]), (w + 0.6, 0.8, w + 0.6))
        post(room, (at[0], at[1] + height / 2, at[2]), w, height, w)
        box(room, STONE, (at[0], at[1] + height - 0.4, at[2]), (w + 0.4, 0.8, w + 0.4))
        return (w + 0.6, w + 0.6)

    def column(self, room, at, height, width=1.0):
        post(room, (at[0], at[1] + height / 2, at[2]), width, height, width)
        return (width, width)

    # ---------------------------------------------------------------- mechanisms

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        """A smith's lever: a black stone block, two iron cheeks, and between them a black iron handle 2.4 m long with a glowing ember
        lantern for a knob, tipping forward and back (the mechanism). Pulled from -z turned by yaw."""
        at = np.array(at, float)
        box(room, STONE, at + (0, 0.45, 0), (1.6, 0.9, 1.6), yaw=yaw)
        pivot = at + (0, 1.4, 0)
        for side in (-0.4, 0.4):
            box(room, "iron_wall_2x2", pivot + yaw_turn((side, -0.3, 0), yaw), (0.12, 1.3, 1.0), yaw=yaw)
        m = room.mech(id, "lever", pivot, (0, yaw, 0))
        box(m, IRON, (0, 0, 0), (0.7, 0.45, 0.45), yaw=0)
        post(m, (0, 1.1, 0), 0.28, 2.4, 0.28)
        m.stand(EMBER, (0, 1.95, 0), scale=(1.1, 0.8, 1.1))         # (set on the handle's end: the handle runs up into it)
        m.pose(euler=(-32, 0, 0)).pose(euler=(32, 0, 0))
        m.seconds = 0.8
        for target, do in wires:
            m.wire(target, do)
        m.reached_from = reached_from
        return m

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """An iron portcullis in side k's doorway, rising into the wall above (or sliding sideways where the room has no height)."""
        x, z = side_xz(k, lateral, half - self.wall_t / 2)
        g = room.mech(id, kind, (x, thr, z), (0, (90 * k) % 360, 0))
        if kind == "gate":
            g.doorway = doorway
        g.fill(BARS, (0, self.door_h / 2, 0), (self.door_w + 0.3, self.door_h + 0.1, 0.3))
        box(g, IRON, (0, self.door_h + 0.05, 0), (self.door_w + 0.4, 0.4, 0.4))
        up = room.size[1] / 2 - 0.05 - (thr + self.door_h + 0.3)
        if up >= self.door_h - 0.5:
            g.pose().pose(at=(0, self.door_h, 0))
        else:
            across = room.size[0 if k % 2 == 0 else 2] / 2
            if abs(lateral + open_dir * (self.door_w + 0.6)) + (self.door_w + 0.3) / 2 <= across:
                g.pose().pose(at=(open_dir * (self.door_w + 0.6), 0, 0))
            else:
                g.pose().pose(at=(0, up, 0))
        g.start = start
        g.seconds = 2.0
        return g

    def plate(self, room, id, at, size=2.4):
        p = room.mech(id, "plate", at)
        p.fill(IRON, (0, 0.08, 0), (size, 0.16, size))
        p.fill(FLOORGRATE, (0, 0.17, 0), (size - 0.4, 0.04, size - 0.4))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        return p

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (2.2, 1.2)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at):
        """An iron lantern pole; at most LAMPS_PER_ROOM a room (lights are costly: a few strong ones, not many small ones)."""
        n = getattr(room, "_lamps", 0)
        if n < LAMPS_PER_ROOM:
            room._lamps = n + 1
            room.stand(LAMP, at)
        return (0.8, 0.8)

    def light_hang(self, room, at):
        """A fire bowl hung over the room's middle; at most BOWLS_PER_ROOM a room."""
        n = getattr(room, "_bowls", 0)
        if n < BOWLS_PER_ROOM:
            room._bowls = n + 1
            hang_bowl(room, at)

    def accent(self, room, at, yaw=0.0):
        room.stand("fire_pit_iron", at, yaw=yaw)
        return (1.6, 1.6)

    def chain(self, holder, top, length, yaw=0.0):
        gk.hang_chain(holder, top, length, yaw=yaw)

    # ---------------------------------------------------------------- machinery: forge vents in the walls

    def girder(self, holder, a, b, size=0.5):
        return iron(holder, a, b, size)

    def bar(self, holder, a, b, width=0.5):
        return iron(holder, a, b, width)

    def big_gear_wall(self, room, machine, k, lateral, y, depth, diameter, turn):
        """A forge vent high in side k's wall: a square mouth `diameter` across framed in black iron, its back black stone, an ember
        glow in it behind iron bars (the forge behind the wall is still alight); a short slag drip of black stone frozen under it."""
        d = min(diameter, 4.0)
        def at(lat, dy, dd):
            x, z = side_xz(k, lateral + lat, depth - dd)
            return np.array([x, y + dy, z])
        # the frame
        for s in (-1, 1):
            post(room, at(s * (d / 2 + 0.25), 0, 0.1), 0.5, d + 1.0, 0.5)
        for s in (-1, 1):
            c = at(0, s * (d / 2 + 0.25), 0.1)
            box(room, IRON, c, (d + 1.0, 0.5, 0.5), yaw=90 * k)
        # its back and the bars over its mouth
        box(room, STONE, at(0, 0, -0.15), np.abs(yaw_turn((d, d, 0.3), 90 * k)))
        for i in range(3):
            c = at((i - 1) * d / 3, 0, 0.15)
            post(room, c, 0.18, d, 0.18)
        # the glow: an ember lantern on the mouth's sill (part of the machine: it flickers on), coal under it
        machine.stand(EMBER, at(0, -d / 2 + 0.01, -0.0), scale=(1.4, 1.2, 1.4))
        box(room, COAL, at(0, -d / 2 + 0.1, 0.0), np.abs(yaw_turn((d * 0.7, 0.3, 0.4), 90 * k)))

    def machine(self, room, id, at=(0, 0, 0)):
        m = room.mech(id, "machine", at)
        m.pose()
        return m

    # ---------------------------------------------------------------- dressing

    def decor(self):
        def S(name, s=1.0, wall=None, sets=()):
            lo, hi = bounds(name)
            return {"prefab": name, "scale": s, "size": ((hi[0] - lo[0]) * s, (hi[2] - lo[2]) * s), "wall": wall, "sets": sets}
        return [
            S(ANVIL, 2.2, None, ("smithy", "armoury")),
            S("forge_ext3", 1.6, True, ("smithy",)),
            S("forge_ext2", 1.8, True, ("smithy", "store")),
            S("bar_iron_stack", 1.4, True, ("smithy", "store")),
            S("bar_blackmetal_stack", 1.4, True, ("store",)),
            S(COAL, 1.0, True, ("coal", "smithy")),
            S("Morkhalla_Stonepile", 0.9, True, ("coal", "frozen")),
            S("dvergrprops_crate_ashlands", 1.0, True, ("store", "coal")),
            S(ICESPIKE, 0.6, True, ("frozen",)),
            S(ICE, 0.3, None, ("frozen",)),
            S("Morkhalla_WeaponStand", 1.2, True, ("armoury",)),
            S("Morkhalla_Bench", 1.0, True, ("armoury", "smithy")),
            S("fire_pit_iron", 1.0, False, ("coal", "smithy")),
        ]

    def dressing_sets(self):
        return {"smithy": 1.0, "store": 0.8, "coal": 0.8, "frozen": 1.0, "armoury": 0.5}

    def remains(self):
        """The giants' leavings for a big room's floor: (prefab, size (x, y, z) of its box, sunk metres)."""
        lo, hi = bounds(ANVIL)
        return [(ANVIL, tuple((hi - lo) * 4.5), 0.0), ("rock3_ice", (3.6, 4.6, 2.4), 0.6), ("giant_arm", (2.6, 2.4, 10.5), 0.6),
                ("Ice_floor_fractured", (4.2, 1.6, 4.0), 0.3)]

    def nook_pieces(self):
        def stand(name, s):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))

        def shrine(room, at, yaw):
            room.stand(ICESPIKE, at, yaw=yaw, scale=(0.8, 0.7, 0.8))
            room.stand(EMBER, (at[0] + 0.9, at[1], at[2]), scale=(1.4, 1.4, 1.4))
        return [
            {"name": "anvil", "main": True, "size": (2.4, 2.8), "build": lambda room, at, yaw: anvil(room, at, yaw, 2.2)},
            {"name": "ingots", "main": True, "size": (1.6, 1.6), "build": stand("bar_iron_stack", 2.2)},
            {"name": "forge", "main": True, "size": (2.6, 2.2), "build": stand("smelter", 0.8)},
            {"name": "shrine", "main": True, "size": (2.0, 1.4), "build": shrine},
            {"name": "coal", "size": (2.0, 2.0), "build": stand(COAL, 1.0)},
            {"name": "ingots2", "size": (1.0, 1.0), "build": stand("bar_blackmetal_stack", 1.4)},
            {"name": "spike", "size": (1.0, 1.0), "build": stand(ICESPIKE, 0.5)},
            {"name": "lamp", "size": (0.8, 0.8), "build": stand(LAMP, 1.0)},
        ]

    # ---------------------------------------------------------------- signature rooms (hand made: below, and bosses_rimeforge.py)

    def signatures(self):
        import bosses_rimeforge as bosses
        import rimeforge_rooms as rooms
        return {"entrance": lambda: rooms.entrance(self), "frozenforges": lambda: rooms.frozen_forges(self),
                "hammerline": lambda: rooms.hammer_line(self), "vault": lambda: rooms.vault(self),
                "secret_hoard": lambda: rooms.secret_hoard(self), "slagbridge": lambda: rooms.slag_bridge(self),
                "quenchhall": lambda: rooms.quench_hall(self),
                "keeper": lambda: bosses.keeper_hall(self), "greatforge": lambda: bosses.great_forge(self),
                "cap_plain": self.cap_plain, "cap_ice": self.cap_ice, "vault_cap": self.vault_cap}

    def shell(self, name, size, doors, **kw):
        """A plain room of this theme with doorways [(id, side, lateral)] on its ground floor, for the hand-made rooms."""
        from ..shells import Shell
        from ..core import rng_for
        sh = Shell(self, name, "rect", size, rng_for(name), **kw)
        for id, k, lat in doors:
            sh.add_door(id, k, lat, 0)
        sh.build()
        return sh

    def cap_plain(self):
        """End cap: an iron portcullis rusted shut, chained."""
        r = Room("rime_cap", (6, 10, 0), end_cap=True, weight=0.3)
        r.doorway("in", (0, -4, 0), (0, 0, 1), type=TYPE)
        r.fill(BARS, (0, -4 + self.door_h / 2, 0.3), (self.door_w + 0.4, self.door_h + 0.2, 0.3))
        box(r, STONE, (0, -4 + self.door_h / 2, 0.6), (self.door_w + 0.4, self.door_h + 0.2, 0.2))
        box(r, CHAIN, (0, -4 + 3.4, 0.1), (self.door_w + 0.6, 2.4, 0.4))
        return r

    def cap_ice(self):
        """End cap: the glacier has filled the passage: a wall of ice blocks."""
        r = Room("rime_cap_ice", (6, 10, 0), end_cap=True, weight=0.3)
        r.doorway("in", (0, -4, 0), (0, 0, 1), type=TYPE)
        box(r, ICE, (0, -4 + self.door_h / 2, 0.5), (self.door_w + 0.6, 1.0, self.door_h + 0.2), tilt=(-90, 0))
        box(r, BACK, (0, -4 + self.door_h / 2, 1.1), (self.door_w + 0.6, BACK_T, self.door_h + 0.2), tilt=(-90, 0))   # (closed behind)
        box(r, ICE, (-1.0, -4 + 0.6, 0.0), (2.0, 1.2, 1.4), yaw=20)
        box(r, ICE, (1.2, -4 + 0.5, -0.1), (1.6, 1.0, 1.2), yaw=-35)
        return r

    def vault_cap(self):
        r = Room("rime_vaultcap", (6, 10, 0), end_cap=True, weight=0.01)
        r.doorway("in", (0, -4, 0), (0, 0, 1), type=VAULT_TYPE, door=False)
        r.fill(BARS, (0, -4 + self.door_h / 2, 0.3), (self.door_w + 0.4, self.door_h + 0.2, 0.3))
        box(r, STONE, (0, -4 + self.door_h / 2, 0.6), (self.door_w + 0.4, self.door_h + 0.2, 0.2))
        return r


THEME = Rimeforge()
