"""
The Giants' clockwork vault: the look of Shifting Halls, from the game's own parts (style sheet below). Ancient Jotunn machinery: huge grey
grausten blocks with black marble, bronze gears and iron chains, cold blue light. Everything is at giant scale: rooms on an 8 m grid, 12 m
a storey, doorways 5 m wide and 8 m tall.

Style sheet (every name checked in the game's files; sizes are each prefab's shape, see prefab_bounds.json):
  Stone       STONE    Ashlands_floor_large      10 x 2 x 10 grausten blocks: floors, ceilings, and stood up, the walls
              DARK     blackmarble_floor_large   8 x 2 x 8 black marble: lintels, plinths, inlays
              PILLAR   Ashlands_Boss_Pillar      carved grausten pillar 4.6 x 8 x 4.6 (origin at its foot)
              COLUMN   blackmarble_column_3      2 x 8 x 2 black marble column (door posts)
              HEAD     blackmarble_head_big01    2 m carved head (keystones)
              STEP     piece_grausten_stonestair 2 x 1 x 2 stair block (climbs -z)
  Metal       GEAR     CharredCogwheel           a bronze cogwheel (1.1 m, lying flat): scaled 5 to 17 times into the machine's gears
              AXLE     Morkhalla_MetalPillar     iron post 0.7 x 5 (origin at its foot): axles, struts
              IBEAM    Piece_flametal_beam       dark metal beam 2 x 0.56: girders, rails, gear spokes
              GRATE    iron_grate                iron grate 2 x 3: portcullis
              CHAIN    Morkhalla_WallChain1      iron chain hanging 4.4 m from a wall anchor
              CHAINS   Morkhalla_Chain           20 m chain span between two anchors
              DOOR     Ashlands_Fortress_Gate_Door  studded iron gate leaf 4 x 5.6: sealed doors (end caps), the vault gate
  Remains     giant_skull, giant_ribs, giant_sword1/2, giant_helmet1/2, giant_brain, ancient_skull (the Mistlands' giant bones and
              arms), Morkhalla_StatueSword_hanging, Morkhalla_StatuePieceFace_big (a Jotunn statue's face), skull_pile, bone_stack
  Light       TORCH    Morkhalla_giant_railing_torch   stone post with a blue flame (light 0.48/0.77/1.0, range 10)
              BRAZIER  MountainKit_brazier_blue        blue brazier (range 5)
              WISP     dverger_demister                blue wisp light (range 10)
              CRYSTAL  blackmarble_altar_crystal       cyan crystal on a stand (range 3), accents
  Rails       RAIL     Morkhalla_giant_railing         giant stone balustrade 4 m long
  Enemies     Spawner_Draugr (melee), Spawner_Draugr_Ranged (ranged), Spawner_Ghost (flying), saved: the Builder swaps each for the
              biome's creature of the same role.
"""
import math
import numpy as np
from dungeon_parts import Room, yaw_turn, side_point, wall, slab, box, lever, bounds, euler_matrix

STONE = "Ashlands_floor_large"
DARK = "blackmarble_floor_large"
PILLAR = "Ashlands_Boss_Pillar"
COLUMN = "blackmarble_column_3"
HEAD = "blackmarble_head_big01"
STEP = "piece_grausten_stonestair"
GEAR = "CharredCogwheel"
AXLE = "Morkhalla_MetalPillar"
IBEAM = "Piece_flametal_beam"
GRATE = "iron_grate"
CHAIN = "Morkhalla_WallChain1"
CHAINS = "Morkhalla_Chain"
DOOR = "Ashlands_Fortress_Gate_Door"
TORCH = "Morkhalla_giant_railing_torch"
BRAZIER = "MountainKit_brazier_blue"
WISP = "dverger_demister"
CRYSTAL = "blackmarble_altar_crystal"
RAIL = "Morkhalla_giant_railing"
SPAWNERS = {"melee": "Spawner_Draugr", "ranged": "Spawner_Draugr_Ranged", "flying": "Spawner_Ghost"}

TYPE = "giant"                  # our doorways' type: only our rooms join
DOOR_W, DOOR_H = 5.0, 8.0       # a doorway's opening
STOREY = 12.0                   # floor to floor
WALL = 1.0                      # wall thickness (inside the box)
SIDES = "nesw"


def facing(k):
    return yaw_turn((0, 0, -1), 90 * k)


class Shell:
    """A room of ours: its box, its doorways (id, side, lateral, threshold), walls with openings, floor, ceiling and door frames."""

    def __init__(self, name, size, doors, tags=(), weight=1.0, max=0, min_depth=0, entrance=None, floor=True, door_type=TYPE):
        self.r = Room(name, size, tags=tags, weight=weight, max=max, min_depth=min_depth)
        self.sx, self.sy, self.sz = size
        self.bottom, self.top = -self.sy / 2, self.sy / 2
        self.doors = doors
        for id, k, lat, thr in doors:
            self.r.doorway(id, side_point(k, lat, thr, self.half(k)), facing(k), type=door_type, entrance=(id == entrance))
        for k in range(4):
            openings = [(lat, thr) for id, kk, lat, thr in doors if kk == k]
            wall(self.r, k, self.half(k), self.bottom, self.top, openings, STONE, thickness=WALL, cell=10.0, size=(DOOR_W, DOOR_H),
                 across=self.across(k))
            for lat, thr in openings:
                door_frame(self.r, k, lat, thr, self.half(k) - WALL)
        if floor:
            slab(self.r, -self.sx / 2, self.sx / 2, -self.sz / 2, self.sz / 2, self.bottom + 1.0, STONE, thickness=1.0, cell=10.0)
        slab(self.r, -self.sx / 2, self.sx / 2, -self.sz / 2, self.sz / 2, self.top, STONE, thickness=1.0, cell=10.0, ceiling=True)

    def half(self, k):
        return self.sz / 2 if k % 2 == 0 else self.sx / 2

    def across(self, k):
        return self.sx / 2 if k % 2 == 0 else self.sz / 2

    def inside(self, k, lateral, y, inward=0.0):
        """A point by side k's wall: `inward` metres in from its inside face."""
        return side_point(k, lateral, y, self.half(k) - WALL - inward)


def door_frame(room, k, lateral, thr, face):
    """Black marble posts and a lintel with a carved head round a doorway, on the wall's inside face (`face`: its depth)."""
    for s in (-1, 1):
        room.fill(COLUMN, side_point(k, lateral + s * (DOOR_W / 2 + 0.6), thr + DOOR_H / 2, face - 0.5),
                  np.abs(yaw_turn((1.2, DOOR_H, 1.0), 90 * k)))
    room.fill(DARK, side_point(k, lateral, thr + DOOR_H + 0.6, face - 0.5), np.abs(yaw_turn((DOOR_W + 2.4, 1.2, 1.0), 90 * k)))
    box(room, HEAD, side_point(k, lateral, thr + DOOR_H + 0.4, face - 1.0), (1.3, 1.3, 1.3), yaw=(180 + 90 * k) % 360)


def torch(room, at):
    """A blue-flamed stone post standing at `at`."""
    room.stand(TORCH, at, scale=(1.3, 1.3, 1.3))


def wisp(room, at):
    room.add(WISP, at)


def brazier(room, at, chain=0.0):
    """A blue brazier; hung on a chain of that length from above when chain > 0."""
    room.stand(BRAZIER, at, scale=(1.6, 1.6, 1.6))
    if chain > 0:
        hang_chain(room, (at[0], at[1] + 1.6 + chain, at[2]), chain)


def crystal(room, at, yaw=0.0):
    room.stand(CRYSTAL, at, yaw=yaw)


def hang_chain(holder, top, length, yaw=0.0):
    """An iron chain hanging `length` metres down from `top` (its anchor block at the top)."""
    lo, hi = bounds(CHAIN)
    s = length / (hi[1] - lo[1])
    holder.add(CHAIN, np.array(top, float) - yaw_turn((0, hi[1] * s, 0), yaw), (0, yaw, 0), (s, s, s))


def gear(holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
    """A bronze cogwheel `diameter` across, turning about `axis` (y: lying flat, spin turning it about its own axis to set its teeth; z:
    standing, its face along z turned by yaw). turn: degrees a second it spins about its axle (a mechanism's part only)."""
    lo, hi = bounds(GEAR)
    s = diameter / (hi[0] - lo[0])
    t = (thickness if thickness is not None else diameter * 0.08) / (hi[1] - lo[1])
    scale = np.array([s, t, s])
    if axis == "y":
        euler = np.array([0.0, yaw + spin, 0.0])
    else:
        euler = np.array([90.0, yaw, 0.0])           # its flat face to z, then turned by yaw
    R = euler_matrix(*euler)
    at = np.array(centre, float) - R @ (scale * (lo + hi) / 2)
    p = holder.add(GEAR, at, euler, scale)
    if turn:
        p.spin = np.array([0.0, float(turn), 0.0])      # about its own y: its axle, however it stands
        # (turning about its own axis keeps its middle still only if the middle is its origin: the cogwheel's is, within 3 cm)
    return p


def machine(room, id, at=(0, 0, 0)):
    """Machinery that turns all the time (Runtime's kind "machine": one state, its parts' spin)."""
    m = room.mech(id, "machine", at)
    m.pose()
    return m


def axle(holder, foot, height, width=0.8):
    holder.fill(AXLE, (foot[0], foot[1] + height / 2, foot[2]), (width, height, width))


def girder(holder, a, b, size=0.5):
    """A dark metal beam from point a to point b."""
    a, b = np.array(a, float), np.array(b, float)
    d = b - a
    length = float(np.linalg.norm(d))
    yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0     # the beam lies along its own x
    pitch = math.degrees(math.atan2(d[1], math.hypot(d[0], d[2])))
    lo, hi = bounds(IBEAM)
    scale = np.array([length / (hi[0] - lo[0]), size / (hi[1] - lo[1]), size / (hi[2] - lo[2])])
    euler = np.array([0.0, yaw, pitch])
    R = euler_matrix(*euler)
    at = (a + b) / 2 - R @ (scale * (lo + hi) / 2)
    holder.add(IBEAM, at, euler, scale)


def spawner(room, role, at):
    room.add(SPAWNERS[role], (at[0], at[1] + 0.5, at[2]), saved=True)


def stairs(holder, foot, rise, run, width, yaw=0.0, fill_below=None, lateral=0.0):
    """A flight of grausten steps: its foot's middle at `foot`, climbing `rise` metres over `run` (1 m a step) towards +z turned by yaw.
    fill_below: a floor height to fill down to under each step (a solid stair), or None (open underneath)."""
    n = int(round(rise))
    step = run / n
    for i in range(n):
        c = np.array(foot, float) + yaw_turn((lateral, i + 0.5, step * (i + 0.5)), yaw)
        box(holder, STEP, c, (width, 1.0, step), yaw=(180 + yaw) % 360)
        if fill_below is not None and foot[1] + i > fill_below + 0.05:
            h = foot[1] + i - fill_below
            c2 = np.array(foot, float) + yaw_turn((lateral, 0, step * (i + 0.5)), yaw)
            box(holder, STONE, (c2[0], fill_below + h / 2, c2[2]), (width, h, step), yaw=yaw)


def giant_lever(room, id, at, yaw, wires, reached_from=None):
    """A giant's lever: a grausten plinth with a bronze gear on it and an iron handle twice a man's height."""
    m = lever(room, id, at, yaw, wires, reached_from, handle=AXLE, knob=HEAD, post=PILLAR, cap=DARK, size=1.7)
    gear(room, (at[0], at[1] + 1.7 * 1.1 + 0.3, at[2]), 1.8, thickness=0.25)
    return m
