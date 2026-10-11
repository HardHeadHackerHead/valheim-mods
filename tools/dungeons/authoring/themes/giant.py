"""
The giant theme: the Jotunn's clockwork vault (giant_kit.py's pieces). Huge and vast: doorways 5 x 8 m, 12 m a storey, rooms on an
8 m grid from 8 to 36 m, grausten walls with black marble trim, bronze gears turning, iron chains and bars, cold blue light. It likes
round halls and big plain floors, puts machinery everywhere, and has the giants' remains (skulls, ribs, swords, helmets) to dress
with. Its signature rooms are the Shifting Halls (the turning stairs), the ribcage hall, the Warden's hall and the vault.
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn

from ..core import side_xz
from ..theme import Theme


class Giant(Theme):
    name = "giant"
    door_type = gk.TYPE
    door_w, door_h = gk.DOOR_W, gk.DOOR_H
    wall_t = gk.WALL
    floor_t = 1.0
    storey = gk.STOREY
    height = 12.0
    grid = 8.0
    step_rise, step_run = 1.0, 1.5
    way_width = 3.0
    panel = 10.0
    lever_base = (1.8, 1.8)
    music = "forestcrypt"

    palette = {
        "wall": [gk.STONE], "floor": [gk.STONE], "ceiling": [gk.STONE], "trim": [gk.DARK], "pillar": [gk.PILLAR],
        "column": [gk.COLUMN], "rail": [gk.RAIL], "grate": [gk.GRATE], "door_leaf": [gk.DOOR], "step": [gk.STEP],
        "beam": [gk.IBEAM], "bar": [gk.BAR], "block": [gk.BLOCK], "chain": [gk.CHAIN], "gear": [gk.GEAR],
        "light_stand": [gk.TORCH], "light_hang": [gk.WISP], "chest": ["TreasureChest_trollcave"],
    }
    shells = {"rect": 4, "round": 2, "L": 1.5, "split": 1.5, "balcony": 1.5}
    sizes = {"small": ((2, 2), (2, 2)), "medium": ((2, 3), (2, 3)), "large": ((3, 4), (3, 4)), "corridor": ((1, 1), (2, 3)),
             "hall": ((3, 4), (4, 4)), "nook": ((1, 1), (1, 2)),
             "plate": ((2, 3), (3, 4))}
    reshape = {}
    pit_gaps = (12.0, 14.0, 16.0)

    # ---------------------------------------------------------------- pieces

    def door_frame(self, room, k, lateral, thr, face):
        gk.door_frame(room, k, lateral, thr, face)

    def frame_depth(self):
        return 1.7

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        gk.stairs(holder, foot, rise, run, width, yaw=yaw, fill_below=fill_below)

    def rail(self, room, a, b, y, height=1.7):
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2]))
        mid = (a + b) / 2
        box(room, gk.RAIL, (mid[0], y + height / 2, mid[2]), (0.6, height, length), yaw=yaw)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        gk.grate_wall(holder, a, b, bottom, top, thickness)

    def pillar(self, room, at, height, width=None):
        lo, hi = bounds(gk.PILLAR)
        s = (width or 2.6) / (hi[0] - lo[0])
        room.stand(gk.PILLAR, at, scale=(s, height / (hi[1] - lo[1]), s))
        return ((hi[0] - lo[0]) * s, (hi[2] - lo[2]) * s)

    def column(self, room, at, height, width=1.2):
        room.fill(gk.COLUMN, (at[0], at[1] + height / 2, at[2]), (width, height, width))
        return (width, width)

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        return gk.giant_lever(room, id, at, yaw, wires, reached_from=reached_from)

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand("TreasureChest_trollcave", at, yaw=yaw, saved=saved)
        return (2.0, 1.4)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    def light_stand(self, room, at):
        gk.torch(room, at)
        return (1.4, 1.4)

    def light_hang(self, room, at):
        gk.wisp(room, at)

    def brazier(self, room, at, chain=0.0):
        gk.brazier(room, at, chain=chain)

    def accent(self, room, at, yaw=0.0):
        gk.crystal(room, at, yaw=yaw)
        return (1.3, 1.3)

    def chain(self, holder, top, length, yaw=0.0):
        gk.hang_chain(holder, top, length, yaw=yaw)

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        return gk.gear(holder, centre, diameter, axis=axis, yaw=yaw, thickness=thickness, spin=spin, turn=turn)

    def girder(self, holder, a, b, size=0.5):
        return gk.girder(holder, a, b, size)

    def axle(self, holder, foot, height, width=0.8):
        gk.axle(holder, foot, height, width)

    def bar(self, holder, a, b, width=0.6):
        return gk.bar(holder, a, b, width)

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, size=(2.0, 2.0), wall=None, sets=(): {"prefab": name, "scale": s, "size": size, "wall": wall, "sets": sets}
        return [
            S("bone_stack", 1.5, (2.4, 2.4), True, ("ossuary", "ruin")),
            S("skull_pile", 1.5, (2.2, 2.2), True, ("ossuary",)),
            S("Morkhalla_Rubble1", 0.8, (3.0, 3.0), True, ("ruin",)),
            S("Morkhalla_Rubble2", 0.8, (3.0, 3.0), True, ("ruin",)),
            S("Morkhalla_Stonepile", 1.0, (3.0, 3.0), True, ("ruin", "forge")),
            S("grausten_pile", 1.2, (2.4, 2.4), True, ("ruin",)),
            S("bar_iron_stack", 1.6, (2.2, 1.6), True, ("forge", "engine")),
            S("Morkhalla_coal_pile_memorial", 1.4, (2.6, 2.6), True, ("forge",)),
            S("Morkhalla_firepit", 1.6, (3.2, 3.2), False, ("forge",)),
            S("Morkhalla_Bench", 1.0, (3.6, 1.4), True, ("archive",)),
            S("Morkhalla_Table", 1.0, (3.0, 2.0), False, ("archive",)),
            S("MemorialStone_Large", 1.4, (2.6, 1.6), True, ("archive", "ossuary")),
            S("Morkhalla_WeaponStand", 1.4, (2.4, 1.2), True, ("forge", "armoury")),
            S("blackmarble_altar_crystal", 1.0, (1.4, 1.4), None, ("archive", "ossuary", "engine", "armoury", "ruin", "forge")),
        ]

    def dressing_sets(self):
        return {"ossuary": 1.0, "ruin": 1.0, "forge": 0.6, "archive": 0.6, "engine": 0.8, "armoury": 0.5}

    def remains(self):
        """The giants' bones and arms, for a big room's floor: (prefab, size (x, y, z) of its box, sunk metres)."""
        return [("giant_skull", (4.0, 4.6, 6.0), 0.4), ("giant_helmet2", (3.0, 2.8, 3.2), 0.2),
                ("giant_helmet1", (2.8, 2.4, 3.0), 0.2), ("giant_brain", (2.6, 2.0, 3.0), 0.3)]

    def nook_pieces(self):
        """What a nook can hold: a centrepiece ("main") against the back wall, and smaller things beside it."""
        def stand(name, s):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, s, s))

        def boxed(name, size, sink=0.0):
            return lambda room, at, yaw: box(room, name, (at[0], at[1] + size[1] / 2 - sink, at[2]), size, yaw=yaw)
        return [
            {"name": "bones", "main": True, "size": (2.6, 2.6), "build": stand("skull_pile", 1.5)},
            {"name": "tablet", "main": True, "size": (2.6, 1.8), "build": stand("MemorialStone_Large", 1.5)},
            {"name": "skull", "main": True, "size": (2.6, 2.4), "build": boxed("Skull2", (2.2, 2.2, 2.0))},
            {"name": "helmet", "main": True, "size": (3.0, 3.0), "build": boxed("giant_helmet2", (2.8, 2.6, 3.0), 0.2)},
            {"name": "chest", "main": True, "size": (2.4, 1.8), "build": stand("Morkhalla_ChestAncient", 1.0)},
            {"name": "shrine", "main": True, "size": (1.6, 1.6), "build": lambda room, at, yaw: gk.crystal(room, at, yaw)},
            {"name": "stack", "size": (2.4, 2.4), "build": stand("bone_stack", 1.3)},
            {"name": "rubble", "size": (2.6, 2.6), "build": stand("Morkhalla_Rubble2", 0.6)},
            {"name": "stones", "size": (2.6, 2.6), "build": stand("Morkhalla_Stonepile", 0.8)},
            {"name": "crystal", "size": (1.4, 1.4), "build": lambda room, at, yaw: gk.crystal(room, at, yaw)},
        ]

    def big_gear_wall(self, room, machine, k, lateral, y, depth, diameter, turn):
        """A great gear standing against side k's wall, on an iron boss."""
        x, z = side_xz(k, lateral, depth)
        self.gear(machine, (x, y, z), diameter, axis="z", yaw=90 * k, thickness=0.6, turn=turn)
        x2, z2 = side_xz(k, lateral, depth + 0.5)
        self.bar(room, (x, y, z), (x2, y, z2), diameter * 0.12)

    # ---------------------------------------------------------------- signature rooms (hand made: shifting_halls.py)

    def signatures(self):
        import shifting_halls as sh
        out = {"entrance": sh.entrance, "greathall": sh.great_hall, "warden": sh.warden_hall, "vault": sh.vault,
               "vault_cap": sh.vault_cap, "warden_cap": sh.warden_cap, "rotunda": sh.rotunda,
               "secret_cache": sh.secret_cache, "secret_shrine": sh.secret_shrine,
               "shortcut_near": lambda: sh.shortcut_door("shortcut_near", False),
               "shortcut_far": lambda: sh.shortcut_door("shortcut_far", True),
               "cap_gate": sh.cap_gate, "cap_rubble": sh.cap_rubble}
        for name, (corners, levers, start, falls) in sh.HALLS.items():
            out[name] = (lambda c=corners, l=levers, s=start, f=falls, n=name: sh.hall(n, c, l, s, f))
        return out


THEME = Giant()
