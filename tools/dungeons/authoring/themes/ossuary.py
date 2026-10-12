"""
The ossuary theme (pack "bonechoir", the Cathedral of the Bone Choir): a cathedral the dead raised inside the skeleton of something
vast, where the dead still sing. Tall and narrow: pointed doorways 4 x 7 m, rooms on a 4 m grid, 16 m to the vault in a one-storey
room, 8 m a storey; long naves rather than wide halls. Pale ruin stone for the walls and floors (ivory and ash grey), and bone used as
architecture: every room's vault is a ribcage (a spine along the ridge, ribs arching down to the walls), long bones bundled into
pillars and standing as pilasters where the ribs land, skulls for keystones. Warm candle gold is the only light: clusters of giant
candles, hanging fire bowls, standing torches. It doesn't do machinery. Its own rooms: the Nave of Ribs, the Bell Tower, the Reliquary
(the signature rooms), the Choir Loft and the Apse of the Colossus (the bosses: bosses_bonechoir.py), the entrance and the vault.

Pieces are the game's own (ZNetScene); the choices and their facts: palettes/ossuary.json (palette_tool.py).
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, Room, slab

from ..core import side_xz
from ..theme import Theme

STONE = "Ashlands_Ruins_Floor_6x6"     # 6 x 0.44 x 6 pale ruin slab: floors, ceilings, and stood up, walls
BLOCK = "Ashlands_Floor"               # 2 x 1 x 2 pale block: steps, plinths, cover, trim
COPING = "Ashlands_Ruins_TopStone"     # 1 x 1 x 3 coping stone: parapets
STEP = "piece_grausten_stonestair"     # 2 x 1 x 2 ash-grey stair block (climbs -z)
SPIKE = "Ashlands_Pillar4_tip"         # a carved stone spike: turned point down, the falling spikes
ARCH = "Ashlands_Arch1"                # a pointed arch's half (two make the arch over a doorway)
ROSE = "Ashlands_Ruins_Wall_Top_wHole"  # a pointed gable with a round window: rose windows
BASE = "Ashlands_PillarBase3_double"   # a stepped pillar base
RIBS = "lox_ribs"                      # half a ribcage (five ribs on a spine bar): scaled up into the vaults
BONE = "LargeBone"                     # a long bone: pillars, pilasters, door posts, lever handles
SKULL = "AsksvinCarrionSkull"          # a beast's skull: keystones, lever knobs (an item: the Builder makes it solid)
GRATE = "iron_grate"                   # wrought iron screens
LEAF = "iron_grate"
CANDLE = "Candle_resin"                # walk-through: giant candles at 3-5 times
TORCH = "piece_groundtorch_wood"       # a thin standing torch (range 10, warm; nothing to stand on)
FIREBOWL = "piece_brazierceiling01"    # a fire bowl on ropes (range 10, warm): hung from above
CHAIN = "Morkhalla_WallChain1"
CHEST = "TreasureChest_ashland_stone"  # a pale stone reliquary chest
SINGER = ("FrozenSkeleton_Pose1", "FrozenSkeleton_Pose2")   # the choir: white skeletons, singing


class Ossuary(Theme):
    name = "ossuary"
    door_type = "bonechoir"
    door_w, door_h = 4.0, 7.0
    wall_t = 1.0
    floor_t = 1.0
    storey = 7.0
    height = 14.0
    grid = 4.0
    step_rise, step_run = 1.0, 1.4
    way_width = 3.0
    panel = 6.0
    lever_base = (1.0, 1.0)
    music = "forestcrypt"

    palette = {
        "wall": [STONE], "floor": [STONE], "ceiling": [STONE], "trim": [BLOCK], "pillar": [BONE], "column": [BONE],
        "rail": [COPING], "grate": [GRATE], "door_leaf": [LEAF], "step": [STEP], "beam": [BONE], "block": [BLOCK],
        "chain": [CHAIN], "light_stand": [TORCH], "light_hang": [FIREBOWL], "chest": [CHEST],
    }
    structure_prefabs = (RIBS, BONE)       # (the vault's ribs and the pilasters stand against the walls: through_walls leaves them)
    # long naves and tall towers: rect most, round apses, galleries (balcony) and split levels; L rooms are transepts
    shells = {"rect": 4, "round": 1.5, "balcony": 2, "split": 1.5, "L": 1}
    sizes = {"nook": ((2, 2), (2, 3)), "small": ((3, 3), (4, 5)), "medium": ((3, 4), (5, 7)), "large": ((4, 5), (7, 8)),
             "corridor": ((2, 2), (4, 6)), "hall": ((5, 6), (8, 9)), "plate": ((4, 5), (6, 7))}
    pit_gaps = (11.5, 12.5, 13.5)          # (more than the best jump across: 10.9 m)
    dense = 1.4
    forbid = {"machineroom"}

    # ---------------------------------------------------------------- shell pieces

    def door_frame(self, room, k, lateral, thr, face):
        """A Gothic portal: long-bone posts either side, a stone tympanum over the opening from 4.6 m up, a pointed arch of carved
        ruin stone springing from the posts, and a beast's skull for its keystone."""
        w, spring = self.door_w, 4.6
        for s in (-1, 1):
            x, z = side_xz(k, lateral + s * (w / 2 + 0.4), face - 0.4)
            box(room, BONE, (x, thr + spring / 2, z), (0.8, spring, 0.8))
            x, z = side_xz(k, lateral + s * (w / 2 + 0.4), face - 0.35)
            box(room, BLOCK, (x, thr + 0.3, z), (1.1, 0.6, 0.9), yaw=90 * k)
        x, z = side_xz(k, lateral, face - 0.15)
        box(room, STONE, (x, thr + (spring + self.door_h - 0.05) / 2, z), (w + 0.4, self.door_h - 0.05 - spring, 0.3), yaw=90 * k,
            tilt=(-90, 0))
        lo, hi = bounds(ARCH)
        x, z = side_xz(k, lateral, face - 0.4)
        sx = (w / 2 + 0.8) / (hi[0] - lo[0])
        sy = (self.door_h - 0.15 - spring) / (hi[1] - lo[1])          # (its apex under the storey above: a gallery's floor is there)
        for yaw in (0, 180):
            room.add(ARCH, (x, thr + spring, z), (0, 90 * k + yaw, 0), (sx, sy, 0.6 / (hi[2] - lo[2])))
        x, z = side_xz(k, lateral, face - 0.45)
        box(room, SKULL, (x, thr + self.door_h - 0.9, z), (0.9, 0.55, 0.7), yaw=(180 + 90 * k) % 360)

    def frame_depth(self):
        return 0.9

    def structure(self, sh):
        """The vault: a ribcage under the ceiling, its spine along the ridge (the room's long axis) and its ribs arching down to the
        walls in bays 4 m apart, from 6 m over the highest floor; where the ribs land, a long bone stands up the wall from the floor
        (a pilaster). A round room's ribs radiate from its middle like a dome's."""
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        spring = max(max(sh.levels) + 7.5, top - 6.0)      # (clear of every floor, ledge and jump: the ribs' box is solid to the checks)
        drop = top - spring
        if drop < 3.0:
            return
        if sh.shape == "round":
            R = H[0] - self.wall_t - 0.6
            n = 8
            for i in range(n):
                a = 360.0 * (i + 0.5) / n
                c = yaw_turn((0, 0, R / 2), a)
                box(r, RIBS, (c[0], top - drop / 2, c[2]), (3.2, R - 0.4, drop), yaw=a + 180, tilt=(-90, 0))
            return
        if not sh.notch:
            # a cornice of coping stones round the walls where the vault springs (out of every reach)
            for k in range(4):
                depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
                across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
                x, z = side_xz(k, 0.0, depth - 0.3)
                box(r, COPING, (x, spring - 0.3, z), (0.6, 0.6, 2 * across), yaw=90 * k + 90)
        along_x = sh.size[0] > sh.size[2]
        length = (H[0] if along_x else H[2]) - self.wall_t
        span = (H[2] if along_x else H[0]) - self.wall_t
        n = max(1, int(round(2 * length / 4.0)))
        for i in range(n):
            u = -length + (i + 0.5) * 2 * length / n
            for s in (-1, 1):
                reach = span - 0.4
                c = (u, top - drop / 2, s * reach / 2) if along_x else (s * reach / 2, top - drop / 2, u)
                # its whole footprint inside the room (an L's notch has no vault over it)
                ok = all(sh.in_shape(*((u + du, s * t) if along_x else (s * t, u + du)), margin=-0.3)
                         for du in (-1.6, 1.6) for t in (0.3, reach - 0.3))
                if not ok:
                    continue
                yaw = (0 if s > 0 else 180) if along_x else (90 if s > 0 else 270)
                box(r, RIBS, c, (2 * length / n * 0.9, reach, drop), yaw=yaw + 180, tilt=(-90, 0))
                # the pilaster under the rib's foot, from the floor at the wall to the springing
                px, pz = (u, s * (span - 0.45)) if along_x else (s * (span - 0.45), u)
                lv = next((l for l in range(len(sh.levels)) if sh.on_level(px, pz, l)), None)
                # (only on free floor, and nowhere a higher level runs over it: a gallery, a stair's landing)
                if lv is not None and (any(sh.on_level(px, pz, l2) for l2 in range(lv + 1, len(sh.levels)))
                                       or not sh.plan.rect_is(lv, px - 0.5, px + 0.5, pz - 0.5, pz + 0.5)):
                    lv = None
                if lv is None or any(d.k == self._side_of(sh, px, pz) and abs(self._lat_of(sh, d.k, px, pz) - d.lateral) < self.door_w / 2 + 1.3
                                     for d in sh.doors.values()):
                    continue
                y0 = sh.levels[lv]
                box(r, BONE, (px, (y0 + spring + 0.5) / 2, pz), (0.9, spring + 0.5 - y0, 0.9))
                from ..core import TAKEN
                sh.plan.set_rect(lv, px - 0.5, px + 0.5, pz - 0.5, pz + 0.5, TAKEN)

    @staticmethod
    def _side_of(sh, x, z):
        dx, dz = sh.half[0] - abs(x), sh.half[2] - abs(z)
        if dz < dx:
            return 0 if z > 0 else 2
        return 1 if x > 0 else 3

    @staticmethod
    def _lat_of(sh, k, x, z):
        return [x, -z, -x, z][k]

    # ---------------------------------------------------------------- stairs, rails, bars, pillars

    def rail(self, room, a, b, y, height=1.5):
        """A parapet of coping stones (1.5 m: no jumping over it, nothing on its top)."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2]))
        mid = (a + b) / 2
        box(room, COPING, (mid[0], y + height / 2, mid[2]), (0.6, height, length), yaw=yaw)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        """A wrought iron screen floor to ceiling, on a stone sill, with long bones for its posts."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        mid = (a + b) / 2
        box(holder, BLOCK, (mid[0], bottom + 0.3, mid[2]), (length, 0.6, thickness + 0.3), yaw=yaw)
        n = max(1, int(math.ceil(length / 3.0)))
        for i in range(n):
            c = a + d * ((i + 0.5) / n)
            box(holder, GRATE, (c[0], (bottom + 0.6 + top) / 2, c[2]), (length / n + 0.02, top - bottom - 0.6, thickness), yaw=yaw)

    def trap_head(self, holder, centre, size):
        """The corridor's slamming trap here: falling bone spikes, a row of carved stone spikes points down under a stone yoke."""
        cx, cy, cz = centre
        sx, sy, sz = size
        box(holder, BLOCK, (cx, cy + sy / 2 - 0.15, cz), (sx, 0.3, sz))
        lo, hi = bounds(SPIKE)
        along_x = sx >= sz
        n = max(2, int(round((sx if along_x else sz) / 1.1)))
        for i in range(n):
            u = -0.5 + (i + 0.5) / n
            px, pz = (cx + u * sx, cz) if along_x else (cx, cz + u * sz)
            s = (0.9 / (hi[0] - lo[0]), (sy - 0.3) / (hi[1] - lo[1]), 0.9 / (hi[2] - lo[2]))
            holder.add(SPIKE, (px, cy + sy / 2 - 0.3, pz), (180, 41 * i, 0), s)

    def pillar(self, room, at, height, width=None):
        """Four long bones bound into a pillar on a stepped stone base, a skull-carved capital where they meet the vault."""
        w = width or 2.0
        x, y, z = at
        box(room, BASE, (x, y + 0.5, z), (w + 0.3, 1.0, w + 0.3))
        b = w * 0.27
        for dx, dz in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            box(room, BONE, (x + dx * b, y + 1.0 + (height - 1.0) / 2, z + dz * b), (w * 0.5, height - 1.0, w * 0.5))
        return (w + 0.3, w + 0.3)

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Ash-grey stone stair blocks (a metre a block, walkable steps cut in them), solid pale stone underneath."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, (i + 0.5) * h, step * (i + 0.5)), yaw)
            box(holder, STEP, c, (width, h, step), yaw=(180 + yaw) % 360)
            if fill_below is not None and foot[1] + i * h > fill_below + 0.05:
                hh = foot[1] + i * h - fill_below
                c2 = np.array(foot, float) + yaw_turn((0, 0, step * (i + 0.5)), yaw)
                box(holder, BLOCK, (c2[0], fill_below + hh / 2, c2[2]), (width, hh, step), yaw=yaw)

    # ---------------------------------------------------------------- mechanisms

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        """A bone lever: a pale stone post, a long bone for the handle and a beast's skull on its end (it tips forward and back)."""
        from dungeon_parts import lever
        return lever(room, id, at, yaw, wires, reached_from=reached_from, handle=BONE, knob=SKULL, post=BLOCK, cap=BASE, size=1.15)

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """A wrought iron screen in the doorway, framed in bone (it slides into the wall, or rises in a narrow room)."""
        g = Theme.gate(self, room, id, k, half, thr, start, doorway, lateral=lateral, kind=kind, open_dir=open_dir)
        for s in (-1, 1):
            box(g, BONE, (s * (self.door_w / 2 - 0.1), self.door_h / 2, 0.0), (0.35, self.door_h, 0.35))
        box(g, BONE, (0, self.door_h - 0.2, 0.0), (0.35, self.door_w, 0.35), tilt=(0, 90))
        return g

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (2.2, 1.0)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    def plate(self, room, id, at, size=2.4):
        """A grave slab in the floor that sinks under a foot."""
        p = room.mech(id, "plate", at)
        p.fill(BLOCK, (0, 0.08, 0), (size, 0.16, size))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        return p

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at, yaw=0.0):
        """A bank of giant candles round a thin torch: warm gold, and nothing in it to stand on (candles are walk-through)."""
        x, y, z = at
        room.stand(TORCH, (x, y, z), scale=(1.2, 1.2, 1.2))
        for i, (dx, dz, s) in enumerate(((0.45, 0.2, 5.0), (-0.4, 0.35, 4.0), (0.05, -0.45, 3.2))):
            d = yaw_turn((dx, 0, dz), yaw)
            room.stand(CANDLE, (x + d[0], y, z + d[2]), scale=(s, s, s))
        return (1.4, 1.4)

    def light_hang(self, room, at):
        """A fire bowl on its ropes, on a chain from whatever is above it."""
        lo, hi = bounds(FIREBOWL)
        at = np.array(at, float)
        above = [b0[1] for b0, b1 in map(gk.part_box, room.parts)
                 if b0[1] > at[1] + hi[1] + 0.05 and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2]]
        if above:
            self.chain(room, (at[0], min(above), at[2]), min(above) - (at[1] + hi[1]))
        room.add(FIREBOWL, at, scale=(1.3, 1.0, 1.3))

    def accent(self, room, at, yaw=0.0):
        """A singer of the choir: a white skeleton caught mid-hymn."""
        room.stand(SINGER[int(abs(at[0] * 7 + at[2] * 3)) % 2], at, yaw=yaw)
        return (2.2, 2.4)

    def chain(self, holder, top, length, yaw=0.0):
        gk.hang_chain(holder, top, length, yaw=yaw, thin=True)

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        """No gears here: a stone drum under a turning bridge (its spin turns it)."""
        t = thickness or 0.6
        p = box(holder, BASE, centre, (diameter, t, diameter))
        if turn:
            p.spin = np.array([0.0, float(turn), 0.0])
        return p

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, wall=None, sets=(): {"prefab": name, "scale": s, "size": (1.0, 1.0), "wall": wall, "sets": sets}
        return [
            S("skull_pile", 1.4, True, ("ossuary", "reliquary")),
            S("bone_stack", 1.5, True, ("ossuary",)),
            S("Skull1", 2.5, None, ("ossuary",)),
            S("FrozenSkeleton_Pose1", 1.0, True, ("choir",)),
            S("FrozenSkeleton_Pose2", 1.0, True, ("choir", "ossuary")),
            S("piece_throne02", 1.0, True, ("choir", "reliquary")),
            S("piece_bone_throne", 0.8, True, ("reliquary",)),
            S("MemorialStone_Medium", 0.8, True, ("reliquary", "ossuary")),
            S("TreasureChest_ashland_stone", 1.0, True, ("reliquary",)),
            S("Bell", 1.6, None, ("belfry",)),
            S("fader_bellholder", 0.8, True, ("belfry",)),
            S("Candle_resin", 4.0, None, ("choir", "reliquary", "belfry", "ossuary")),
            S("piece_cloth_hanging_door", 1.6, True, ("choir", "belfry")),
            S("AsksvinCarrionRibcage", 1.6, None, ("ossuary", "belfry")),
        ]

    def dressing_sets(self):
        return {"choir": 1.0, "ossuary": 1.0, "reliquary": 0.8, "belfry": 0.7}

    def remains(self):
        """Bones of the vast thing, for a big room's floor: (prefab, box size, sunk)."""
        return [("Skull2", (2.6, 2.5, 2.4), 0.3), ("giant_arm", (1.7, 1.6, 7.0), 0.2), ("lox_ribs", (5.0, 2.4, 3.6), 0.1),
                ("piece_asksvinskeleton", (5.0, 2.4, 2.5), 0.0)]

    def nook_pieces(self):
        def stand(name, s=1.0):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))

        def shrine(room, at, yaw):
            """A skull on a bone-legged altar block under candles."""
            box(room, BLOCK, (at[0], at[1] + 0.5, at[2]), (1.8, 1.0, 1.2), yaw=yaw)
            room.stand("Skull1", (at[0], at[1] + 1.0, at[2]), yaw=yaw, scale=(2.5, 2.5, 2.5))
            for dx in (-0.6, 0.6):
                d = yaw_turn((dx, 0, 0), yaw)
                room.stand(CANDLE, (at[0] + d[0], at[1] + 1.0, at[2] + d[2]), scale=(2.0, 2.0, 2.0))

        def choir(room, at, yaw):
            """A singer kneeling at a throne of stone."""
            room.stand("piece_throne02", at, yaw=yaw)
            d = yaw_turn((0, 0, -1.4), yaw)
            room.stand("FrozenSkeleton_Pose2", (at[0] + d[0], at[1], at[2] + d[2]), yaw=yaw)

        return [
            {"name": "bones", "main": True, "size": (2.0, 2.0), "build": stand("skull_pile", 1.6)},
            {"name": "throne", "main": True, "size": (1.8, 1.0), "build": stand("piece_bone_throne", 0.9)},
            {"name": "shrine", "main": True, "size": (2.0, 1.4), "build": shrine},
            {"name": "choir", "main": True, "size": (2.2, 3.0), "build": choir},
            {"name": "tablet", "main": True, "size": (2.0, 1.0), "build": stand("MemorialStone_Medium", 0.8)},
            {"name": "bell", "main": True, "size": (1.8, 1.8), "build": stand("Bell", 1.6)},
            {"name": "stack", "size": (1.6, 1.6), "build": stand("bone_stack", 1.4)},
            {"name": "candles", "size": (0.8, 0.8), "build": stand(CANDLE, 4.0)},
            {"name": "singer", "size": (2.2, 2.6), "build": stand("FrozenSkeleton_Pose1")},
            {"name": "ribcage", "size": (1.8, 1.6), "build": stand("AsksvinCarrionRibcage", 1.6)},
        ]

    # ---------------------------------------------------------------- signature rooms

    def signatures(self):
        import bonechoir_rooms as br
        return br.signatures(self)


THEME = Ossuary()
