"""
The tidevault theme: the Tidecaller's Drowned Vault (pack "drowned"). A sea-temple cut into a sea cliff that sank long ago and drained:
walls and vaults of dark banded sea-rock (the Mistlands' layered rock, a stone no other pack builds with), floored with the weathered
decks of the fleet it sank onto, olive wreck timber rammed through it and banded along every wall where the tide once stood, carved
ship's knees for the vault's ribs (like an upturned hull), kelp hanging, stone bells on chains, portcullises of dark timber lattice,
and a cold sea-green light: green fire burning in rock fonts and in bell-lanterns. Middle scale between the giants and the Dvergr:
doorways 4 x 4.5 m, a tide step (storey) of 5.5 m, rooms on a 6 m grid, walls 1.5 m thick (a dark backing behind the rock, so no gap
between its lumps ever shows the void).

It likes split levels (tide terraces) and round cisterns; it has no machinery (no gears: the machineroom job is forbidden), so its
quiet rooms are pillar halls, terraces and wreck-strewn breathers.

Its signature rooms (hand-made, here): the Tide Locks (the puzzle: drowned hulls that float low or high in three basins, sluice levers
on the pier tops), the Anchor Gallery (swinging anchors), the entrance, the vault, the secret grotto, the shortcut doors and the caps.
The two boss rooms (the Bell Shaft and the Tide Throne) are in tools/dungeons/bosses_drowned.py.
"""
import itertools
import math

import numpy as np

from dungeon_parts import box, bounds, yaw_turn, euler_matrix, slab, Room, r3

from ..core import side_xz
from ..theme import Theme

STONE = "rock_mistlands1"                 # 4.4 x 3.6 x 7.4 dark banded sea-rock (strata along y): walls, vaults, fills
DARK = "blackmarble_floor_large"           # 8 x 2 x 8 black slab: the hidden backing behind the rock (seen only in its gaps)
TILE = "Morkhalla_WoodBoards"              # 5 x 0.28 x 5 weathered planking: floors, decks, plates
FILL = STONE                               # solid rock: under floors, levels, steps, cover, daises
CURB = "darkwood_beam"                     # 2 x 0.4 x 0.5 dark timber (along x): parapets, lintels, curbs, kerbs
COLUMN = "rock_mistlands2"                 # 1.9 x 3.9 x 1.9 dark standing rock: pillar drums, door posts, lever posts
FONT = "rock_mistlands2"                   # the same, squat: the fire fonts, plinths
RIB = "ashwood_arch_big"                   # 2.2 x 2.2 carved timber knee (post side at x 0, beam side on top): the vault's ribs
BOARDS = "shipwreck_karve_bottomboards"    # olive wreck planking (a lattice, gaps and all): the tide bands
BEAM = "darkwood_beam"                     # the timber of the portcullis lattices
FIRE = "piece_groundtorch_green"           # a green-flamed torch: only its flame shows (in a font, in a bell)
BELL = "Bell"                              # a stone bell
CHAIN = "Morkhalla_WallChain1"             # an iron chain 4.4 m
BAR = "Morkhalla_MetalBar"                 # studded iron: anchors' shanks and arms
KELP = "vines"                             # walk-through leaves: kelp hanging from the vaults
STEP = "ashwood_stair"                     # a timber flight, one per step (the map finds "stair")
CHEST = "shipwreck_karve_chest"            # a sea chest (saved)
HOARD = "Morkhalla_ChestAncient"           # the vault's hoard (saved, 32 slots)
SPAWNERS = {"melee": "Spawner_Draugr", "ranged": "Spawner_Draugr_Ranged", "flying": "Spawner_Ghost"}

TYPE = "tide"                              # our doorways' type


def orient(prefab, along, up):
    """The quarter-turn euler that lays a prefab's longest flat axis along `along` (a room-frame axis vector) and its second along `up`:
    for planks laid as a band on a wall. Returns the euler."""
    lo, hi = bounds(prefab)
    size = hi - lo
    order = np.argsort(-size)                      # its axes, longest first
    a, b = np.zeros(3), np.zeros(3)
    a[order[0]], b[order[1]] = 1, 1
    along, up = np.array(along, float), np.array(up, float)
    best = None
    for ex, ey, ez in itertools.product((0, 90, 180, 270), repeat=3):
        R = euler_matrix(ex, ey, ez)
        s = abs(np.dot(R @ a, along)) + abs(np.dot(R @ b, up))
        if best is None or s > best[0] + 1e-6:
            best = (s, (ex, ey, ez))
    return best[1]


def lateral_axis(k):
    """The room-frame direction along side k's wall (its lateral, as side_xz counts it)."""
    x, z = side_xz(k, 1.0, 0.0)
    return np.array([x, 0.0, z])


class Tidevault(Theme):
    name = "tidevault"
    door_type = TYPE
    door_w, door_h = 4.0, 4.5
    wall_t = 1.5
    floor_t = 1.0
    storey = 5.5                       # a tide step: the arcades' floor to the pier tops
    height = 10.0
    grid = 6.0
    step_rise, step_run = 0.5, 1.0
    way_width = 3.0
    panel = 6.0
    lever_base = (1.0, 1.0)
    music = "forestcrypt"
    pit_gaps = (11.5, 12.5, 13.5)      # (more than the best jump across: 10.9 m)
    dense = 1.3

    palette = {
        "wall": [STONE], "floor": [TILE], "ceiling": [TILE], "trim": [CURB], "pillar": [COLUMN], "column": [COLUMN],
        "rail": [CURB], "grate": [BEAM], "door_leaf": [BEAM], "step": [STEP], "beam": [BOARDS], "block": [FILL],
        "chain": [CHAIN], "light_stand": [FONT], "light_hang": [BELL], "chest": [CHEST],
    }
    structure_prefabs = (STONE, DARK, TILE, CURB, COLUMN, BOARDS, RIB, KELP)
    shells = {"rect": 2, "L": 1, "round": 1.5, "split": 3, "balcony": 1}
    sizes = {"nook": ((2, 2), (2, 2)), "small": ((2, 2), (2, 3)), "medium": ((2, 3), (3, 4)), "large": ((3, 4), (4, 5)),
             "corridor": ((1, 1), (3, 4)), "hall": ((4, 4), (5, 6)), "plate": ((3, 3), (4, 4))}
    forbid = {"machineroom"}
    reshape = {"gate": {"shapes": ("rect", "L", "split")}}

    # ---------------------------------------------------------------- shell pieces

    def wall_panel(self, room, centre, length, height, yaw, prefab=None):
        """Banded sea-rock (its strata along the wall), a thin dark backing at the wall's outer face behind it."""
        c = np.array(centre, float)
        box(room, prefab or STONE, c - yaw_turn((0, 0, 0.15), yaw), (length, height, self.wall_t - 0.3), yaw=yaw)
        box(room, DARK, c + yaw_turn((0, 0, self.wall_t / 2 - 0.15), yaw), (length, 0.3, height), yaw=yaw, tilt=(-90, 0))

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        """Weathered deck planking on top, rock under it."""
        thickness = thickness or self.floor_t
        tile = 0.3
        slab(room, x0, x1, z0, z1, top, prefab or TILE, thickness=min(tile, thickness), cell=5.0)
        if thickness > tile + 0.05:
            slab(room, x0, x1, z0, z1, top - tile, FILL, thickness=thickness - tile, cell=self.panel)

    def ceiling(self, room, x0, x1, z0, z1, top, thickness=None):
        """A vault of rock, a dark backing over it."""
        thickness = thickness or self.floor_t
        slab(room, x0, x1, z0, z1, top, DARK, thickness=0.3, cell=8.0)
        slab(room, x0, x1, z0, z1, top - 0.3, STONE, thickness=thickness - 0.3, cell=self.panel, ceiling=True)

    def block(self, room, x0, x1, z0, z1, y0, y1, prefab=None):
        """Solid rock from y0 to y1 (a raised level, a ledge), decked on top."""
        if y1 - y0 > 0.5:
            slab(room, x0, x1, z0, z1, y1 - 0.3, prefab or FILL, thickness=y1 - y0 - 0.3, cell=self.panel)
            slab(room, x0, x1, z0, z1, y1, TILE, thickness=0.3, cell=5.0)
        else:
            slab(room, x0, x1, z0, z1, y1, prefab or FILL, thickness=y1 - y0, cell=self.panel)

    def door_frame(self, room, k, lateral, thr, face):
        """Posts of standing rock either side, a dark timber lintel over them."""
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (self.door_w / 2 + 0.45), face - 0.45)
            room.fill(COLUMN, (x, thr + (self.door_h + 0.4) / 2, z), (0.9, self.door_h + 0.4, 0.9))
        x, z = side_xz(k, lateral, face - 0.45)
        box(room, CURB, (x, thr + self.door_h + 0.75, z), (self.door_w + 2.0, 0.7, 0.9), yaw=90 * k)

    def frame_depth(self):
        return 0.9

    def structure(self, sh):
        """The theme's framing: olive wreck-timber tide bands along every wall at the same heights over each floor (where the tide once
        stood), curved stone ribs across the vault like an upturned hull (springing over the doorways' height), and kelp hanging down
        the walls between the doorways. None of it reaches into the room more than 0.15 m below the ribs, so it gives no footholds."""
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        doors = list(sh.doors.values())
        if sh.shape != "round":
            for k in range(4):
                depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
                across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
                axis = lateral_axis(k)
                eul = orient(BOARDS, axis, (0, 1, 0))
                for lv, y0 in enumerate(sh.levels):
                    for band in (1.1, 3.3):
                        y = y0 + band
                        if y + 0.4 > top - 0.3:
                            continue
                        # where this level's floor runs along the wall, and no doorway on it is in the way
                        lats = np.arange(-across + 0.25, across - 0.24, 0.5)
                        ok = [sh.on_level(*side_xz(k, u, depth - 0.6), lv)
                              and not any(d.k == k and abs(d.lateral - u) < self.door_w / 2 + 1.2 and d.y <= y < d.y + self.door_h + 1.5
                                          for d in doors)
                              and not (lv == 0 and len(sh.levels) > 1 and y > sh.levels[1] - 0.2)
                              for u in lats]
                        for a, b in _runs(lats, ok, 0.5):
                            if b - a < 1.5:
                                continue
                            x, z = side_xz(k, (a + b) / 2, depth - 0.075)
                            size = (b - a, 0.4, 0.15) if k % 2 == 0 else (0.15, 0.4, b - a)
                            r.fill(BOARDS, (x, y + 0.2, z), size, euler=eul)
        # the ribs: a pair of carved ship's knees across the shorter span (from each wall in under the vault), every grid along the
        # longer, over the highest floor's doorways
        spring = max(sh.levels) + self.door_h + 1.3
        if top - sh.floor_y > 12.0:
            spring = max(spring, top - 5.0)                 # (a tall room: high, clear of any ledge a job raises in it)
        rise = top - spring
        if rise >= 1.4 and sh.shape in ("rect", "split", "balcony"):
            along_x = sh.size[0] >= sh.size[2]
            span = (H[2] if along_x else H[0]) - self.wall_t           # half the width the ribs cross
            length = (H[0] if along_x else H[2]) - self.wall_t
            n = max(1, int(round(2 * length / self.grid)))
            lo, hi = bounds(RIB)
            for i in range(1, n):
                u = -length + i * 2 * length / n
                for s in (-1, 1):
                    # a knee from the wall (its post side, x 0) in towards the middle (+x), its beam side under the vault
                    sx, sy, sz = span / (hi[0] - lo[0]), rise / (hi[1] - lo[1]), 0.6 / (hi[2] - lo[2])
                    if along_x:
                        at = (u, spring, s * span)
                        yaw = 90 if s > 0 else 270
                    else:
                        at = (s * span, spring, u)
                        yaw = 180 if s > 0 else 0
                    r.add(RIB, at, (0, yaw, 0), (sx, sy, sz))
        # kelp: strands down the walls from the vault, two lengths, between the doorways (walk-through)
        if sh.shape != "round":
            rng = sh.rng
            for k in range(4):
                depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
                across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
                for u in np.arange(-across + 1.5, across - 1.4, 4.5):
                    if any(d.k == k and abs(d.lateral - u) < self.door_w / 2 + 2.0 for d in doors) or rng.random() < 0.45:
                        continue
                    if not sh.on_level(*side_xz(k, u, depth - 0.6), len(sh.levels) - 1) and not sh.on_level(*side_xz(k, u, depth - 0.6), 0):
                        continue
                    x, z = side_xz(k, u, depth - 0.35)
                    n = 2 if top - max(sh.levels) > 7.5 else 1
                    for j in range(n):
                        kelp(r, (x, top - 2.3 * j, z), 90 * k + rng.uniform(-20, 20))

    # ---------------------------------------------------------------- stairs, rails, bars, pillars

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Timber steps, one flight piece per step (the map finds them by name), solid rock under them."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, i * h, step * (i + 0.5)), yaw)
            box(holder, STEP, (c[0], c[1] + h / 2, c[2]), (width, h, step), yaw=(yaw + 180) % 360)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                box(holder, FILL, (c[0], fill_below + hh / 2, c[2]), (width, hh, step), yaw=yaw)

    def rail(self, room, a, b, y, height=1.2):
        """A coping-stone parapet along a..b."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2]))
        mid = (a + b) / 2
        box(room, CURB, (mid[0], y + height / 2, mid[2]), (length, height, 0.6), yaw=yaw - 90)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        """A timber lattice floor to ceiling between a and b: upright bars 0.5 m apart (no rail across it to climb by)."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        lattice(holder, (a + b) / 2 + np.array([0, (bottom + top) / 2, 0]), length, top - bottom, thickness, yaw, rails=False)

    def pillar(self, room, at, height, width=None):
        """A pillar of dark standing rock, drums stacked floor to ceiling."""
        w = width or 1.6
        n = max(1, int(math.ceil(height / 5.0)))                    # (stacked drums: a twisted pillar stretched far smears)
        for i in range(n):
            h = height / n
            room.fill(COLUMN, (at[0], at[1] + h * (i + 0.5), at[2]), (w, h, w))
        return (w, w)

    # ---------------------------------------------------------------- mechanisms

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        """A sluice lever: a twisted stone post, a stone cap, an iron handle with a little bell for its knob."""
        from dungeon_parts import lever
        return lever(room, id, at, yaw, wires, reached_from=reached_from, handle="piece_dvergr_pole", knob=BELL,
                     post=COLUMN, cap=TILE, size=1.2)

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """A timber lattice in side k's doorway, rising into the wall above it (sliding sideways where the room is wide enough)."""
        from ..core import side_xz as sxz
        x, z = sxz(k, lateral, half - self.wall_t / 2)
        g = room.mech(id, kind, (x, thr, z), (0, (90 * k) % 360, 0))
        if kind == "gate":
            g.doorway = doorway
        lattice(g, (0, self.door_h / 2, 0), self.door_w + 0.3, self.door_h + 0.1, 0.25)
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
        p.fill(TILE, (0, 0.08, 0), (size, 0.16, size))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        return p

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (1.8, 1.0)

    def spawner(self, room, role, at):
        bounds(SPAWNERS[role])
        room.add(SPAWNERS[role], (at[0], at[1] + 0.5, at[2]), saved=True)

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at):
        sea_fire(room, at)
        return (1.4, 1.4)

    def light_hang(self, room, at):
        bell_lantern(room, at)

    def accent(self, room, at, yaw=0.0):
        sea_fire(room, at)
        return (1.4, 1.4)

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        """No gears in a sea-temple: a round stone turntable (a pillar base, flat) where a recipe wants a wheel."""
        lo, hi = bounds(FONT)
        th = thickness if thickness is not None else diameter * 0.15
        s = np.array([diameter / (hi[0] - lo[0]), th / (hi[1] - lo[1]), diameter / (hi[2] - lo[2])])
        euler = np.array([0.0, yaw + spin, 0.0]) if axis == "y" else np.array([90.0, yaw, 0.0])
        R = euler_matrix(*euler)
        p = holder.add(FONT, np.array(centre, float) - R @ (s * (lo + hi) / 2), euler, s)
        if turn:
            p.spin = np.array([0.0, float(turn), 0.0])
        return p

    def chain(self, holder, top, length, yaw=0.0):
        hang_chain(holder, top, length, yaw)

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, size=(1.0, 1.0), wall=None, sets=(): {"prefab": name, "scale": s, "size": size, "wall": wall, "sets": sets}
        return [
            S("barrell", 1.2, (1.1, 1.1), True, ("hold", "wreck")),
            S("CargoCrate", 1.3, (1.0, 1.0), True, ("hold", "wreck")),
            S("BarrelRings", 1.4, (1.3, 1.3), None, ("hold", "wreck")),
            S("rug_seal", 1.3, (2.6, 3.0), False, ("hall", "shrine")),
            S("skull_pile", 1.2, (1.4, 1.4), True, ("drowned", "shrine")),
            S("bone_stack", 1.3, (1.4, 1.4), True, ("drowned",)),
            S("Bell", 1.4, (1.5, 1.5), None, ("shrine", "hall", "drowned")),
            S("GlowingMushroom", 0.8, (1.3, 1.3), True, ("drowned", "shrine")),
            S("shipwreck_karve_chest", 1.0, (1.8, 0.9), True, ("hold",)),
        ]

    def dressing_sets(self):
        return {"hold": 1.0, "wreck": 1.0, "drowned": 1.0, "shrine": 0.7, "hall": 0.6}

    def remains(self):
        """Wreckage for a big room's floor, half sunk in the silt (low: never a step up to a ledge): (prefab, size (x, y, z) of its box,
        sunk metres)."""
        return [("shipwreck_karve_bow", (3.4, 1.8, 6.4), 0.5), ("shipwreck_karve_stern", (3.5, 1.7, 5.8), 0.5)]

    def nook_pieces(self):
        def stand(name, s):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))
        return [
            {"name": "idol", "main": True, "size": (2.2, 2.4), "build": stand("StatueEvil", 0.9)},
            {"name": "bell", "main": True, "size": (2.6, 2.6), "build": lambda room, at, yaw: fallen_bell(room, at, yaw, 2.2)},
            {"name": "prow", "main": True, "size": (2.4, 2.4), "build": lambda room, at, yaw: prow(room, at, yaw)},
            {"name": "shrine", "main": True, "size": (2.6, 1.2), "build": stand("fader_bellholder", 0.9)},
            {"name": "font", "main": True, "size": (1.6, 1.6), "build": lambda room, at, yaw: sea_fire(room, at)},
            {"name": "barrels", "size": (1.2, 1.2), "build": stand("barrell", 1.2)},
            {"name": "crate", "size": (1.1, 1.1), "build": stand("CargoCrate", 1.3)},
            {"name": "bones", "size": (1.5, 1.5), "build": stand("skull_pile", 1.2)},
        ]

    # ---------------------------------------------------------------- signature rooms

    def signatures(self):
        import bosses_drowned as bd
        from . import tidevault_rooms as tr
        return {"entrance": tr.entrance, "tidelocks": tr.tide_locks, "anchors": tr.anchor_gallery, "vault": tr.vault,
                "wreckhall": tr.wreck_hall, "kelpway": tr.kelp_way, "sunkenshrine": tr.sunken_shrine, "shipshold": tr.ships_hold, "cap_seal": tr.cap_seal, "cap_silt": tr.cap_silt, "lock_seal": tr.lock_seal, "grotto": tr.secret_grotto,
                "shortcut_near": lambda: tr.shortcut_door("shortcut_near", False),
                "shortcut_far": lambda: tr.shortcut_door("shortcut_far", True),
                "bellshaft": bd.bell_shaft, "throne": bd.tide_throne, "throne_cap": bd.throne_cap}


# ---------------------------------------------------------------- the theme's props (also used by the hand-made rooms)

def _runs(us, ok, step):
    """Runs of consecutive True in ok over us: [(a, b)] edges."""
    out, start = [], None
    for u, o in zip(us, ok):
        if o and start is None:
            start = u
        if not o and start is not None:
            out.append((start - step / 2, u - step / 2))
            start = None
    if start is not None:
        out.append((start - step / 2, us[-1] + step / 2))
    return out


def hang_chain(holder, top, length, yaw=0.0, thick=1.0):
    """An iron chain hanging `length` metres down from `top`."""
    lo, hi = bounds(CHAIN)
    s = length / (hi[1] - lo[1])
    t = min(s, 0.7) * thick
    holder.add(CHAIN, np.array(top, float) - yaw_turn((0, hi[1] * s, 0), yaw), (0, yaw, 0), (t, s, t))


def part_box(p):
    lo, hi = bounds(p.prefab)
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]) * p.scale
    w = (euler_matrix(*p.euler) @ corners.T).T + p.at
    return w.min(0), w.max(0)


def above(room, at, clear=0.05):
    """The underside of the lowest part right above a point (None: nothing)."""
    ys = [b0[1] for b0, b1 in map(part_box, room.parts)
          if b0[1] > at[1] + clear and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2] and b1[1] - b0[1] < 40]
    return min(ys) if ys else None


def sea_fire(room, at, scale=0.6):
    """A font of dark rock with green sea-fire burning in it: the flame of a green torch rising out of its top."""
    at = np.array(at, float)
    box(room, FONT, (at[0], at[1] + 0.5 * scale, at[2]), (2.2 * scale, 1.0 * scale, 2.2 * scale))
    lo, hi = bounds(FIRE)
    top = at[1] + 1.0 * scale
    room.add(FIRE, (at[0], top + 0.25 - hi[1], at[2]))


def bell_lantern(room, at, scale=1.0, chain_to=None):
    """A stone bell hung on a chain from whatever is above it, a green flame burning under its lip on its clapper. `at`: the bell's
    mouth. chain_to: the height its chain hangs from (default the part above it)."""
    at = np.array(at, float)
    lo, hi = bounds(BELL)
    crown = at[1] + hi[1] * scale
    top = chain_to if chain_to is not None else above(room, (at[0], crown, at[2]))
    if top is not None and top - crown > 0.1:
        hang_chain(room, (at[0], top, at[2]), top - crown + 0.1)
    room.add(BELL, (at[0], at[1] - lo[1] * scale, at[2]), scale=(scale, scale, scale))
    flo, fhi = bounds(FIRE)
    fy = at[1] + 0.35 * scale - fhi[1] * 0.6
    room.add(FIRE, (at[0], fy, at[2]), scale=(0.6, 0.6, 0.6))
    # the clapper's iron-bound weight at its foot: what the flame's stick stands in
    box(room, CURB, (at[0], fy + flo[1] * 0.6 - 0.06, at[2]), (0.32, 0.12, 0.32))


def kelp(room, top, yaw=0.0, n=1):
    """A strand of kelp hanging from `top` (walk-through leaves)."""
    lo, hi = bounds(KELP)
    for j in range(n):
        room.add(KELP, (top[0], top[1] - hi[1] - j * 2.3, top[2]), (0, yaw, 0), (1.0, 1.0, 1.0))


def lattice(holder, centre, width, height, thick=0.25, yaw=0.0, rails=True):
    """A portcullis of dark timber: upright bars 0.5 m apart (each under 0.3 m thick: no top to stand on) and, with rails, three cross
    bars behind them. Centred on `centre`, across yaw's x."""
    c = np.array(centre, float)
    n = max(2, int(round(width / 0.5)) + 1)
    for i in range(n):
        u = -width / 2 + 0.11 + i * (width - 0.22) / (n - 1)
        box(holder, BEAM, c + yaw_turn((u, 0, 0), yaw), (height, 0.22, thick), yaw=yaw, tilt=(0, 90))
    if rails:
        for v in (-0.35, 0.0, 0.35):
            box(holder, BEAM, c + yaw_turn((0, v * height, -thick * 0.6), yaw), (width, 0.24, 0.2), yaw=yaw)


def fallen_bell(room, at, yaw, size=2.0):
    """A great stone bell fallen and lying on its side, half sunk."""
    lo, hi = bounds(BELL)
    s = size / (hi[1] - lo[1])
    box(room, BELL, (at[0], at[1] + (hi[0] - lo[0]) * s / 2 - 0.15, at[2]), ((hi[0] - lo[0]) * s, (hi[1] - lo[1]) * s, (hi[2] - lo[2]) * s),
        yaw=yaw, tilt=(0, 80))


def prow(room, at, yaw):
    """A wreck's dragonhead prow standing in the silt."""
    lo, hi = bounds("shipwreck_karve_dragonhead")
    c = (lo + hi) / 2
    room.add("shipwreck_karve_dragonhead", np.array(at, float) - yaw_turn((c[0], lo[1] + 0.3, c[2]), yaw), (0, yaw, 0))


def anchor(holder, top, length, yaw=0.0, width=None):
    """A ship's anchor of studded iron hanging from `top` (its ring): a shank `length` long, a stock across near the top, two flukes
    curving up from the crown. Returns the crown's height."""
    from giant_kit import girder
    w = width or max(0.35, length * 0.09)
    t = np.array(top, float)
    crown = t - (0, length, 0)
    girder(holder, t, crown, w, prefab=BAR)                                           # the shank
    ax = yaw_turn((1, 0, 0), yaw)
    s0 = t - (0, length * 0.12, 0)
    girder(holder, s0 - ax * length * 0.32, s0 + ax * length * 0.32, w * 0.8, prefab=BAR)   # the stock
    arm = length * 0.42
    for s in (-1, 1):
        tip = crown + ax * s * arm * 0.85 + (0, arm * 0.55, 0)
        girder(holder, crown, tip, w * 1.1, prefab=BAR)                              # the flukes
        girder(holder, tip, tip + ax * s * 0.05 + (0, w * 2.2, 0), w * 1.6, prefab=BAR)   # their points
    return crown[1]


THEME = Tidevault()
