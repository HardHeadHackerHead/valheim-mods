"""
The orrery theme (pack "starfall", the Starfall Orrery): a sky-watchers' observatory built into the crater where a star fell. A
forgotten people charted the gods' skies here; their instruments still turn. Tall and round: doorways 4.5 x 7 m (high arches), 9 m a
storey, 12 m rooms, a 6 m grid, round halls and observation galleries before anything else. How it is built (architecture, not a
texture):
  - walls of pale ashlar (the game's stone floor stood up), banded at the foot and under the vault with night-blue black marble;
  - floors and ceilings of black marble: every room's floor and roof is the night sky, the ceiling hung with stars (a pale star-stone
    on a chain, a spark of starlight in it);
  - pale square columns on marble plinths; doorways framed in pale posts under a black lintel with a star for a keystone;
  - silver: armillary rings (iron hoops scaled to 4-18 m) tumbling about a diameter, lattice gates, star-cranks for levers;
  - light: cold white starlight (pale-flamed iron torches), violet star-crystals, sparks; the "sun" a warm white lamp for the eclipse;
  - dressing: the watchers' charts and benches, bells and sighting frames, star tablets, their bones; meteorite shards and fallen
    standing stones from the crater.
No gears and no giants' bones (the Shifting Halls'), no ice (Rimeforge's). Its signature rooms (hand made below) are the Great Orrery
(cranks that turn three planets' rings; a conjunction raises the moon-bridge) and the Star-Chart Halls (walk a constellation on the
floor in its order, under falling starlight); its boss rooms are in bosses_starfall.py (the Astrolabe Keeper, the Sun-Eater). Pieces
measured with palette_tool.py --probe (sizes, solidity, climb heights).
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, Room, slab, euler_matrix

from ..core import side_xz, TAKEN, OUT, FREE, WAY
from ..theme import Theme

ASHLAR = "stone_floor"                  # 4 x 1 x 4 pale stone: walls (stood up), raised levels, blocks
MARBLE = "blackmarble_floor_large"      # 8 x 2 x 8 black marble: floors and ceilings (the night sky)
BAND = "blackmarble_2x2x1"              # 2 x 2 x 1 black marble block: bands, plinths, lintels, dais steps
PARAPET = "stone_wall_2x1"              # 2.2 x 1.2 x 1.2 pale stone: parapets (rails)
COLUMN = "stone_pillar"                 # 1.1 x 2 x 1.1 pale square column: pillars, door posts
STEP = "stone_stair"                    # 2 x 1 x 2 pale stone stair block
GRATE = "iron_grate"
LATTICE = "piece_dvergr_metal_wall_2x2"  # 2 x 2 x 0.08 metal lattice: gate leaves (thickened), screens
CHEST = "piece_chest_grausten"          # a pale stone chest (40 slots; saved)
TORCH = "CastleKit_groundtorch_blue"    # an iron torch with a pale white-blue flame (range 6, more scaled)
STAR = "StatueSeed"                     # a pale spiky star-stone (1.6 x 1.9 x 2)
SPARK = "FireFlies"                     # drifting sparks of light (#8cb8ff, range 4): no mesh
CRYSTAL = "Pickable_MountainCaveCrystal"  # violet crystals with cyan light (walk-through)
RING = "BarrelRings"                    # an iron hoop (0.9 m, flat in xz; its own z axis is a diameter): armillary rings
DISC = "MemorialStone_Medium"           # a round dark stone 2.3 m: planets, moons
TABLET = "MemorialStone_Small"          # a standing stone tablet 3.3 m: star tablets
METEOR = "MineRock_Meteorite"           # a 13 m pillar of meteorite rock, glowing (#ffae8a)
SHARD = "Pickable_Meteorite"            # a glowing meteorite chunk (0.7 m)
STANDSTONE = "highstone_2"              # a pale standing stone 4.4 x 6.6 x 2.3
CHAIN = "Morkhalla_WallChain1"
CLOTH = "piece_cloth_hanging_door_blue"  # night-blue hangings (walk-through)
BAR = "Morkhalla_MetalBar"              # 20 x 1 x 1 studded bar, centred along x: lever handles, spokes
SUN = "piece_hoodedlantern"             # a warm white lamp, range 30: the sun
TYPE = "starfall"                       # our doorways' type


class Orrery(Theme):
    name = "orrery"
    door_type = TYPE
    door_w, door_h = 4.5, 7.0
    wall_t = 1.0
    floor_t = 1.0
    storey = 8.0
    height = 12.0
    grid = 6.0
    step_rise, step_run = 0.5, 1.0         # (a step 1 m deep: the checks find a place on every one)
    way_width = 3.0
    panel = 6.0
    lever_base = (1.4, 1.4)
    music = "forestcrypt"

    palette = {
        "wall": [ASHLAR], "floor": [MARBLE], "ceiling": [MARBLE], "trim": [BAND], "pillar": [ASHLAR], "column": [ASHLAR],
        "rail": [PARAPET], "grate": [GRATE], "door_leaf": [LATTICE], "step": [STEP], "beam": [BAR], "block": [ASHLAR],
        "chain": [CHAIN], "light_stand": [TORCH], "light_hang": [STAR], "chest": [CHEST],
    }
    structure_prefabs = (BAND, STAR)
    shells = {"round": 3, "rect": 2, "balcony": 2, "split": 1, "L": 0.6}
    sizes = {"nook": ((2, 2), (2, 2)), "small": ((2, 3), (2, 3)), "medium": ((3, 4), (3, 4)), "large": ((4, 5), (4, 5)),
             "corridor": ((1, 1), (3, 4)), "hall": ((4, 5), (5, 6)), "plate": ((4, 4), (5, 6))}
    pit_gaps = (12.0, 13.5, 15.0)        # (more than the best jump across: 10.9 m)
    forbid = set()

    # ---------------------------------------------------------------- shell pieces

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        slab(room, x0, x1, z0, z1, top, prefab or MARBLE, thickness=thickness or self.floor_t, cell=8.0)

    def ceiling(self, room, x0, x1, z0, z1, top, thickness=None):
        slab(room, x0, x1, z0, z1, top, MARBLE, thickness=thickness or self.floor_t, cell=8.0, ceiling=True)

    def door_frame(self, room, k, lateral, thr, face):
        """Pale posts on black marble plinths, a black lintel, a star for its keystone."""
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (self.door_w / 2 + 0.45), face - 0.35)
            box(room, BAND, (x, thr + 0.4, z), (1.0, 0.8, 0.8), yaw=90 * k)
            post(room, (x, thr + 0.8 + (self.door_h - 0.8) / 2, z), (0.8, self.door_h - 0.8, 0.7), yaw=90 * k)
        x, z = side_xz(k, lateral, face - 0.35)
        box(room, BAND, (x, thr + self.door_h + 0.35, z), (self.door_w + 1.8, 0.7, 0.7), yaw=90 * k)
        kx, kz = side_xz(k, lateral, face - 0.55)
        room.stand(STAR, (kx, thr + self.door_h + 0.75, kz), yaw=90 * k, scale=(0.45, 0.45, 0.45))
        # dark stone behind the frame, in the wall's own thickness: a round room's curved wall leaves the sky showing either side of
        # a doorway (and in a straight wall it's hidden inside the wall)
        w2 = self.door_w / 2 + 0.05
        across = room.size[0 if k % 2 == 0 else 2] / 2 - 0.05          # (never past the room's box: a narrow room's walls are there)
        lo, hi = max(-across, lateral - w2 - 2.6), min(across, lateral + w2 + 2.6)
        for a, b in ((lo, lateral - w2), (lateral + w2, hi)):
            if b - a > 0.2:
                x, z = side_xz(k, (a + b) / 2, face + 0.5)
                box(room, BAND, (x, thr + (self.door_h + 1.4) / 2, z), (b - a, self.door_h + 1.4, 0.9), yaw=90 * k)
        x, z = side_xz(k, (lo + hi) / 2, face + 0.5)
        box(room, BAND, (x, thr + self.door_h + 1.0, z), (hi - lo, 2.0, 0.9), yaw=90 * k)

    def frame_depth(self):
        return 0.7

    def structure(self, sh):
        """The walls banded in black marble: a plinth at the foot (0.6 m high, 0.4 m out), a band above the doorways and one under
        the vault, and between them pale star-stones set in the wall every few metres; all of it broken round the doorways. A round
        room gets its bands as rings. Where heights are the puzzle (a pit, a ledge), no plinth and no stones (nothing to climb by)."""
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        quiet = getattr(sh, "no_climb", False)
        y0 = sh.levels[0]
        doors = list(sh.doors.values())
        mid = y0 + self.door_h + 1.6                                   # (over every floor-level lintel and its dark backing)
        bands = [(y0 + 0.3, 0.6, 0.4)] if not quiet else []
        if mid + 1.0 < top and all(d.y < y0 + 0.1 or d.y > mid + 0.6 for d in doors):
            bands.append((mid, 0.5, 0.3))
        if top - 0.5 > mid + 2.0:
            bands.append((top - 0.5, 0.6, 0.4))
        stones = y0 + self.door_h * 0.55 if not quiet else None
        if sh.shape == "round":
            R = H[0] - self.wall_t                                     # (the panels' inner faces, at their middles)
            n = max(24, int(2 * math.pi * R / 2.2))
            for i in range(n):
                a = 2 * math.pi * (i + 0.5) / n
                x, z = math.sin(a), math.cos(a)
                if any(abs(x * side_xz(d.k, 0, 1)[1] - z * side_xz(d.k, 0, 1)[0]) * R < self.door_w / 2 + 2.9
                       and (x * side_xz(d.k, 0, 1)[0] + z * side_xz(d.k, 0, 1)[1]) > 0 for d in doors):
                    continue                                           # (a doorway's frame and backing there)
                seg = 2 * R * math.sin(math.pi / n) + 0.1
                for y, h, t in bands:
                    if y > y0 + 1.0 and any(abs(x * side_xz(d.k, 0, 1)[1] - z * side_xz(d.k, 0, 1)[0]) * R < self.door_w / 2 + 2.9
                                            for d in doors if d.y > y0 + 0.1):
                        continue
                    box(r, BAND, (x * (R - t / 2), y, z * (R - t / 2)), (seg, h, t), yaw=math.degrees(a))
                if stones is not None and i % 3 == 1:
                    box(r, STAR, (x * (R - 0.25), stones, z * (R - 0.25)), (0.9, 0.9, 0.5), yaw=math.degrees(a) + 180)
            return
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
            for y, h, t in bands:
                # this wall's band at height y, broken round every doorway in it at that height, only where the floor (or for the
                # upper bands, the room) runs along the wall
                cut = [(d.lateral - self.door_w / 2 - 2.9, d.lateral + self.door_w / 2 + 2.9) for d in doors
                       if d.k == k and d.y - 0.5 < y < d.y + self.door_h + 2.5]
                u = -across
                spans = []
                for a, b in sorted(cut) + [(across, across)]:
                    if a > u + 0.5:
                        spans.append((u, min(a, across)))
                    u = max(u, b)
                for a, b in spans:
                    pts = np.arange(a, b + 0.01, 0.5)
                    ok = [sh.in_shape(*side_xz(k, v, depth - 0.6), margin=-0.4) and (y > y0 + 1.0 or sh.on_level(*side_xz(k, v, depth - 0.6), 0))
                          for v in pts]
                    run = None
                    for v, good in list(zip(pts, ok)) + [(b + 1, False)]:
                        if good and run is None:
                            run = v
                        if not good and run is not None:
                            if v - 0.5 - run > 1.0:
                                c0, c1 = side_xz(k, run, depth - t / 2), side_xz(k, v - 0.5, depth - t / 2)
                                box(r, BAND, ((c0[0] + c1[0]) / 2, y, (c0[1] + c1[1]) / 2), (v - 0.5 - run, h, t), yaw=90 * k)
                            run = None
            if stones is not None:
                for v in np.arange(-across + 3.0, across - 2.9, 6.0):
                    if any(d.k == k and abs(d.lateral - v) < self.door_w / 2 + 3.4 for d in doors):
                        continue
                    if not sh.on_level(*side_xz(k, v, depth - 0.6), 0) or any(
                            sh.on_level(*side_xz(k, v, depth - 0.6), lv) for lv in range(1, len(sh.levels))):
                        continue
                    x, z = side_xz(k, v, depth - 0.25)
                    box(r, STAR, (x, stones, z), (0.9, 0.9, 0.5), yaw=(90 * k + 180) % 360)

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Pale stone flights: a stair block per step, solid underneath."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, i * h, step * (i + 0.5)), yaw)
            box(holder, STEP, (c[0], c[1] + h / 2, c[2]), (width, h, step), yaw=(yaw + 180) % 360)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                box(holder, ASHLAR, (c[0], fill_below + hh / 2, c[2]), (width, hh, step), yaw=yaw)

    def rail(self, room, a, b, y, height=1.3):
        """A pale parapet along a..b: a low wall of cut stone with a black marble coping."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2]))
        mid = (a + b) / 2
        box(room, PARAPET, (mid[0], y + (height - 0.2) / 2, mid[2]), (0.55, height - 0.2, length), yaw=yaw)
        box(room, BAND, (mid[0], y + height - 0.1, mid[2]), (0.7, 0.2, length), yaw=yaw)

    def pillar(self, room, at, height, width=None):
        """A pale square column on a black marble plinth, a black capital under the vault."""
        w = width or 1.4
        box(room, BAND, (at[0], at[1] + 0.35, at[2]), (w + 0.5, 0.7, w + 0.5))
        post(room, (at[0], at[1] + 0.7 + (height - 1.3) / 2, at[2]), (w, height - 1.3, w))
        box(room, BAND, (at[0], at[1] + height - 0.3, at[2]), (w + 0.4, 0.6, w + 0.4))
        return (w + 0.5, w + 0.5)

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """A silver lattice leaf in the doorway, framed in black marble: slides into the wall, or rises where the room is narrow."""
        from ..core import side_xz as sxz
        x, z = sxz(k, lateral, half - self.wall_t / 2)
        g = room.mech(id, kind, (x, thr, z), (0, (90 * k) % 360, 0))
        if kind == "gate":
            g.doorway = doorway
        W, Hh = self.door_w + 0.3, self.door_h + 0.1
        g.fill(LATTICE, (0, Hh / 2, 0), (W, Hh, 0.3))
        g.fill(BAND, (0, Hh - 0.15, 0), (W, 0.3, 0.5))
        g.fill(BAND, (0, 0.15, 0), (W, 0.3, 0.5))
        across = room.size[0 if k % 2 == 0 else 2] / 2
        if abs(lateral + open_dir * (self.door_w + 0.6)) + W / 2 <= across:
            g.pose().pose(at=(open_dir * (self.door_w + 0.6), 0, 0))
        else:
            g.pose().pose(at=(0, room.size[1] / 2 - 0.05 - (thr + Hh), 0))
        g.start = start
        g.seconds = 2.0
        return g

    def plate(self, room, id, at, size=2.4):
        """A star-plate: a pale stone disc set in the floor with a silver ring round it."""
        p = room.mech(id, "plate", at)
        p.fill(ASHLAR, (0, 0.08, 0), (size, 0.16, size))
        p.fill(BAND, (0, 0.17, 0), (size * 0.5, 0.04, size * 0.5))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        return p

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        return star_crank(room, id, at, yaw, wires, reached_from=reached_from)

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (1.8, 1.3)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at):
        """A pale-flamed iron torch on a black marble foot."""
        box(room, BAND, (at[0], at[1] + 0.15, at[2]), (0.8, 0.3, 0.8))
        room.stand(TORCH, (at[0], at[1] + 0.3, at[2]), scale=(1.6, 1.6, 1.6))
        return (1.0, 1.0)

    def light_hang(self, room, at):
        hang_star(room, at)

    def accent(self, room, at, yaw=0.0):
        crystal_plinth(room, at, yaw)
        return (1.2, 1.2)

    def chain(self, holder, top, length, yaw=0.0):
        gk.hang_chain(holder, top, length, yaw=yaw, thin=True)

    # ---------------------------------------------------------------- the instruments (in place of the giants' gears)

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        """An armillary ring in place of a gear: a silver hoop `diameter` across (lying flat for axis y, standing for z), and when
        it turns, a bar across it turning with it (a hoop turning in its own plane looks still)."""
        p = ring(holder, centre, diameter, axis=axis, yaw=yaw + spin, width=thickness)
        if turn:
            spoke(holder, centre, diameter * 0.96, axis=axis, yaw=yaw, turn=turn)
        return p

    def machine(self, room, id, at=(0, 0, 0)):
        m = room.mech(id, "machine", at)
        m.pose()
        return m

    def big_gear_wall(self, room, machine, k, lateral, y, depth, diameter, turn):
        """An astrolabe dial on the wall: a standing silver ring, a planet disc at its middle, a bar turning across it."""
        x, z = side_xz(k, lateral, depth)
        ring(room, (x, y, z), diameter, axis="z", yaw=90 * k)
        spoke(machine, (x, y, z), diameter * 0.94, axis="z", yaw=90 * k, turn=turn * 0.6)
        x2, z2 = side_xz(k, lateral, depth + 0.15)
        box(room, DISC, (x2, y, z2), (diameter * 0.22, diameter * 0.22, 0.4), yaw=90 * k)

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, wall=None, sets=(): {"prefab": name, "scale": s, "size": (1.0, 1.0), "wall": wall, "sets": sets}
        return [
            S("piece_table_runed", 1.0, True, ("charts", "watch")),
            S("piece_bench_runed", 1.0, True, ("charts", "watch")),
            S("Bell", 1.2, None, ("watch", "shrine")),
            S("fader_bellholder", 0.8, True, ("watch",)),
            S(TABLET, 0.8, True, ("shrine", "charts")),
            S(CRYSTAL, 1.3, None, ("shrine", "crater")),
            S(SHARD, 1.4, None, ("crater",)),
            S("stone_pile", 1.2, True, ("crater",)),
            S("bone_stack", 1.2, True, ("ossuary", "crater")),
            S("skull_pile", 1.2, True, ("ossuary",)),
            S(CLOTH, 1.0, True, ("shrine", "ossuary", "watch")),
        ]

    def dressing_sets(self):
        return {"charts": 1.0, "watch": 1.0, "shrine": 0.8, "crater": 0.9, "ossuary": 0.6}

    def remains(self):
        """Big things on a hall's floor: a shard of the fallen star, a fallen standing stone, a dead planet."""
        return [(METEOR, (1.6, 5.0, 1.8), 0.6), (STANDSTONE, (3.0, 2.0, 4.6), 0.3), (DISC, (3.4, 3.2, 1.2), 0.4),
                ("widestone_2", (4.6, 1.6, 3.0), 0.3)]

    def nook_pieces(self):
        def stand(name, s=1.0):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))
        return [
            {"name": "tablet", "main": True, "size": (1.8, 1.2), "build": stand(TABLET, 1.0)},
            {"name": "shrine", "main": True, "size": (1.6, 1.6), "build": lambda room, at, yaw: crystal_plinth(room, at, yaw)},
            {"name": "meteor", "main": True, "size": (2.4, 2.4), "build": lambda room, at, yaw: meteor_heap(room, at, yaw)},
            {"name": "sight", "main": True, "size": (2.8, 1.2), "build": stand("fader_bellholder", 0.9)},
            {"name": "bones", "main": True, "size": (1.6, 1.6), "build": stand("skull_pile", 1.4)},
            {"name": "bell", "size": (1.4, 1.4), "build": stand("Bell", 1.1)},
            {"name": "bench", "size": (2.4, 1.0), "build": stand("piece_bench_runed")},
            {"name": "crystal", "size": (1.2, 1.2), "build": stand(CRYSTAL, 1.2)},
            {"name": "stones", "size": (1.8, 1.8), "build": stand("stone_pile", 1.0)},
        ]

    # ---------------------------------------------------------------- signature rooms

    def signatures(self):
        import orrery_rooms as orr
        import bosses_starfall as bs
        return {"entrance": orr.entrance, "vault": orr.vault, "cap_seal": orr.cap_seal, "cap_fallen": orr.cap_fallen,
                "vault_cap": orr.vault_cap, "secret_lens": orr.secret_lens,
                "shortcut_near": lambda: orr.shortcut_door("shortcut_near", False),
                "shortcut_far": lambda: orr.shortcut_door("shortcut_far", True),
                **orr.signature_halls(), **bs.boss_rooms()}


# ---------------------------------------------------------------- the theme's own pieces (used by orrery_rooms and the bosses too)

def ring(holder, centre, diameter, axis="y", yaw=0.0, tilt=0.0, width=None, turn=None):
    """A silver armillary hoop `diameter` across, its middle at `centre`: lying flat (axis y; tilted `tilt` degrees about its own z,
    a diameter) or standing (axis z: its face along z, turned by yaw). turn: degrees a second it tumbles about that diameter (its
    own z axis: the hoop's own origin lies on it, so it tumbles in place). The hoop is `width` thick (default 3% of its diameter)."""
    lo, hi = bounds(RING)
    s = diameter / (hi[0] - lo[0])
    t = (width if width is not None else max(0.15, diameter * 0.03)) / (hi[1] - lo[1])
    scale = np.array([s, t, s])
    euler = np.array([0.0, yaw, tilt]) if axis == "y" else np.array([90.0, yaw, 0.0])
    R = euler_matrix(*euler)
    mid = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2])
    at = np.array(centre, float) - R @ (scale * mid)
    p = holder.add(RING, at, euler, scale)
    if turn:
        p.spin = np.array([0.0, 0.0, float(turn)])
    return p


def orbit(holder, centre, radius, width=0.25, n=None, axis="y", yaw=0.0, inlay=False, pale=False):
    """A thin silver hoop of `radius` drawn as a polygon of short bars (lying flat, or standing for axis z, its face along z turned by
    yaw): an orbit's ring, an astrolabe's rim. (The game's hoop prefab is three hoops together: kept for the tumbling armillaries.)"""
    n = n or max(16, int(radius * 2.2))
    c = np.array(centre, float)
    pts = []
    for i in range(n + 1):
        a = 2 * math.pi * i / n
        v = (math.sin(a) * radius, 0.0, math.cos(a) * radius) if axis == "y" else (math.sin(a) * radius, math.cos(a) * radius, 0.0)
        pts.append(c + yaw_turn(v, yaw))
    for a, b in zip(pts, pts[1:]):
        d = b - a
        if inlay:
            m = (a + b) / 2
            box(holder, ASHLAR, (m[0], c[1] + 0.01, m[2]), (width, 0.06, float(np.linalg.norm(d)) * 1.04),
                yaw=math.degrees(math.atan2(d[0], d[2])))
        else:
            gk.girder(holder, a - d * 0.04, b + d * 0.04, width, prefab=ASHLAR if pale else BAR)


def spoke(holder, centre, length, axis="y", yaw=0.0, turn=None, width=0.25):
    """A studded bar `length` long across a ring's middle (the bar's origin is its middle): turning `turn` degrees a second in the
    ring's plane."""
    lo, hi = bounds(BAR)
    scale = np.array([length / (hi[0] - lo[0]), width / (hi[1] - lo[1]), width / (hi[2] - lo[2])])
    euler = np.array([0.0, yaw, 0.0]) if axis == "y" else np.array([90.0, yaw, 0.0])
    p = holder.add(BAR, np.array(centre, float), euler, scale)
    if turn:
        p.spin = np.array([0.0, float(turn), 0.0])
    return p


def post(holder, centre, size, yaw=0.0):
    """A pale stone post or column: the ashlar stood on end (its 4 m face upright), `size` (x, height, z) round `centre`. (The game's
    stone pillar has ledges a player climbs: never a column here.)"""
    return box(holder, ASHLAR, centre, (size[0], size[2], size[1]), yaw=yaw, tilt=(-90, 0))


def hang_star(room, at, scale=0.55, chain=True, spark=True):
    """A star hung from whatever is above it: a pale star-stone on a thin iron chain, a spark of light drifting in it."""
    lo, hi = bounds(STAR)
    at = np.array(at, float)
    top = at[1] + hi[1] * scale
    above = [b0[1] for b0, b1 in map(gk.part_box, room.parts)
             if b0[1] > top + 0.05 and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2]]
    if above and chain:
        gk.hang_chain(room, (at[0], min(above), at[2]), min(above) - top, thin=True)
    room.stand(STAR, (at[0], at[1] + lo[1] * scale, at[2]), scale=(scale, scale, scale))
    if spark:                                                      # (sparks are lights: a sky of stars has only a few)
        room.add(SPARK, (at[0], at[1] + 0.5 * scale, at[2]))


def crystal_plinth(room, at, yaw=0.0):
    """Violet star-crystals on a black marble plinth: a small cold light."""
    box(room, BAND, (at[0], at[1] + 0.35, at[2]), (1.0, 0.7, 1.0), yaw=yaw)
    room.stand(CRYSTAL, (at[0], at[1] + 0.7, at[2]), yaw=yaw, scale=(1.1, 1.1, 1.1))


def meteor_heap(room, at, yaw=0.0):
    """Glowing shards of the fallen star, heaped on the floor."""
    for i, (dx, dz, s) in enumerate(((0.0, 0.0, 1.8), (0.7, 0.4, 1.2), (-0.6, 0.5, 1.0))):
        p = yaw_turn((dx, 0, dz), yaw)
        room.stand(SHARD, (at[0] + p[0], at[1], at[2] + p[2]), yaw=yaw + 70 * i, scale=(s, s, s))


def star_crank(room, id, at, yaw, wires, reached_from=None, size=1.25):
    """The watchers' lever, a star-crank: a pale stone pedestal with a black marble cap, and on it a long silver handle with a star
    for its knob, tipping forward and back (the mechanism). Pulled from -z turned by yaw."""
    at = np.array(at, float)
    h = 1.0 * size
    post(room, (at[0], at[1] + h / 2, at[2]), (0.75 * size, h, 0.75 * size), yaw=yaw)
    box(room, BAND, (at[0], at[1] + h + 0.08, at[2]), (1.0 * size, 0.16, 1.0 * size), yaw=yaw)
    m = room.mech(id, "lever", (at[0], at[1] + h + 0.16, at[2]), (0, yaw, 0))
    lo, hi = bounds(BAR)
    L = 1.3 * size
    m.add(BAR, (0, L / 2, 0), (0, 0, 90), (L / (hi[0] - lo[0]), 0.12 / (hi[1] - lo[1]), 0.12 / (hi[2] - lo[2])))
    slo, shi = bounds(STAR)
    s = 0.22 * size
    m.add(STAR, (0, L - slo[1] * s, 0), (0, 0, 0), (s, s, s))
    m.pose(euler=(-35, 0, 0)).pose(euler=(35, 0, 0))
    m.seconds = 0.6
    for target, do in wires:
        m.wire(target, do)
    m.reached_from = reached_from
    return m


THEME = Orrery()
