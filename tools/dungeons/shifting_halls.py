"""
Shifting Halls (packs/shiftinghalls.json): a Giants' clockwork vault. Every room is ours, built from the game's parts in one style
(giant_kit.py has the style sheet): ancient Jotunn machinery of grausten, black marble, bronze gears and iron chains in cold blue light.

    python shifting_halls.py                   writes mods/Dungeons/packs/shiftinghalls.json and checks every hall's puzzle
    python shifting_halls.py --preview DIR     also draws every room (and each hall's solution, step by step) into DIR

Rooms join by their own doorway type ("giant": 5 m wide, 8 m tall, thresholds 1 m above a room's box bottom, 12 m a storey):
  entrance   the gate hall where players arrive, under a Jotunn's carved face (tag entrance)
  corridor, gallery, bend, tee, crossing, stair (a storey up or down), gearshaft (switchback stairs round an axle), chamber, great hall
  (two storeys, a giant's ribs, a balcony), engine room (wall gears that turn by themselves), dead end; end caps: a sealed iron gate,
  a collapsed passage.
The plan, in order (each step only through the one before it):
  start      the entrance;
  lever1     the lever room: a giant's lever that opens the gate further on;
  gate1      the gatehouse: an iron portcullis on its way in, locked until lever1 is pulled;
  rotunda    the turning gear: a drum under a 17 m bronze cogwheel, with an L-shaped passage that turns a quarter every 12 s by itself,
             joining a different pair of the room's four doorways each time (ride it, or wait for it);
  hall       the Shifting Hall: 32 x 48 x 32 m, four storeys 12 m apart, balconies round a 20 m void with an iron axle in the middle. Three
             turning stairs (one between each pair of storeys) hang on the axle by bronze gears: each joins the side its foot faces on the
             storey below to the opposite side on the storey above, and a giant's lever turns it a quarter. Some balcony corners are
             fallen away, so a storey can be in pieces; the levers stand on different storeys, and one also turns a second stair. The
             fourth lever opens the vault's iron gate. Three halls, each with its own corners, levers and start (the layout picks one);
  vault      at the top: the giants' hoard, only joined to the hall's top storey (its own doorway type).
"""
import argparse, collections, math, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dungeon_parts import Room, Pack, CREATURES, LOOT, yaw_turn, side_point, wall, slab, box, bounds, r3  # noqa: E402
from giant_kit import (Shell, STONE, DARK, PILLAR, COLUMN, HEAD, GEAR, BAR, BLOCK, GRATE, CHAINS, DOOR, RAIL, CRYSTAL, TYPE,  # noqa: E402
                       DOOR_W, DOOR_H, WALL, SIDES, facing, door_frame, torch, wisp, brazier, crystal, hang_chain, gear, axle, girder,
                       spawner, stairs, giant_lever, machine, bar, grate_wall)

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "mods", "Dungeons", "packs", "shiftinghalls.json")
VAULT = "quaddungeon_vault"     # the hall's top storey to the Warden's hall: only those two have one
WARDEN = "quaddungeon_warden"   # the Warden's hall to the vault


# ---------------------------------------------------------------- the small rooms: 12 m tall, thresholds at -5, ceilings at 5

def corridor():
    s = Shell("corridor", (8, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5)], weight=3)
    r = s.r
    for z in (-4.0, 4.0):
        for x in (-3.2, 3.2):
            r.fill(COLUMN, (x, 0.0, z), (1.2, 10.0, 1.2))
        r.fill(DARK, (0, 4.4, z), (7.0, 0.8, 1.0))
    for x, z in ((1.8, -5.4), (-1.8, 5.4)):
        torch(r, (x, -5, z))
    hang_chain(r, (0, 5.0, 0), 4.5)
    return r


def gallery():
    """A long corridor with a giant's sword leaning on the wall and iron girders overhead."""
    s = Shell("gallery", (8, 12, 24), [("s", 2, 0, -5), ("n", 0, 0, -5)], weight=1.5, min_depth=1)
    r = s.r
    for z in (-8.0, 0.0, 8.0):
        for x in (-3.2, 3.2):
            r.fill(COLUMN, (x, 0.0, z), (1.2, 10.0, 1.2))
        girder(r, (-3, 4.3, z), (3, 4.3, z), 0.6)
    lo, hi = bounds("giant_sword2")
    s2 = 0.72
    box(r, "giant_sword2", (2.2, -5 + (hi[1] - lo[1]) * s2 / 2 - 0.6, -3.5), np.array(hi - lo) * s2, yaw=90, tilt=(0, -14))
    torch(r, (-2.4, -5, -10))
    torch(r, (2.4, -5, 10))
    wisp(r, (0, 3.8, 4))
    spawner(r, "melee", (0, -5, 6))
    return r


def bend():
    s = Shell("bend", (16, 12, 16), [("s", 2, 0, -5), ("e", 1, 0, -5)], weight=1.5)
    r = s.r
    # the inner corner (north-west): a standing gear set in a slanted wall of stone, an axle through it
    box(r, STONE, (-4.5, 0, 4.5), (9.0, 10.0, 1.2), yaw=45)
    gear(machine(r, "gears"), (-3.9, -0.5, 3.9), 7.5, axis="z", yaw=45, thickness=0.6, turn=15)
    bar(r, (-4.2, -0.5, 4.2), (-2.8, -0.5, 2.8), 0.9)
    torch(r, (3.5, -5, -5.5))
    torch(r, (5.5, -5, 3.5))
    crystal(r, (-1.2, -5, 1.2))
    wisp(r, (2.5, 3.5, 2.5))
    spawner(r, "melee", (2, -5, 2))
    return r


def tee():
    s = Shell("tee", (16, 12, 16), [("s", 2, 0, -5), ("e", 1, 0, -5), ("w", 3, 0, -5)], weight=1.5)
    r = s.r
    r.stand(PILLAR, (0, -5, 3.5), scale=(0.75, 1.25, 0.75))
    gear(machine(r, "gears"), (0, 2.0, 3.5), 6.0, thickness=0.5, turn=20)
    for x in (-5.5, 5.5):
        torch(r, (x, -5, 5.5))
    wisp(r, (0, 3.6, -3))
    spawner(r, "ranged", (0, -5, 5.8))
    return r


def crossing():
    s = Shell("crossing", (16, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5), ("e", 1, 0, -5), ("w", 3, 0, -5)], tags=["hub"], weight=1.2)
    r = s.r
    axle(r, (0, -5, 0), 10.0, 1.2)
    m = machine(r, "gears")
    gear(m, (0, 3.6, 0), 9.0, thickness=0.6, spin=10, turn=12)
    gear(m, (0, -4.2, 0), 4.5, thickness=0.9, turn=-24)
    for x, z in ((5.6, 5.6), (-5.6, -5.6), (5.6, -5.6), (-5.6, 5.6)):
        torch(r, (x, -5, z))
        hang_chain(r, (x * 0.6, 5.0, z * 0.6), 3.0)
    spawner(r, "melee", (3, -5, 0))
    spawner(r, "flying", (-3, -5, 0))
    return r


def stair_room():
    """A storey up (or down): 24 m of solid grausten steps between two landings, the walls lined with black columns."""
    s = Shell("stair", (8, 24, 32), [("s", 2, 0, -11), ("n", 0, 0, 1)], weight=1.5, max=3)
    r = s.r
    stairs(r, (0, -11, -12), 12, 24, 6.0, fill_below=-11)
    slab(r, -4, 4, 12, 16, 1.0, STONE, thickness=12.0, cell=10.0)
    for z in (-12, -4, 4, 12):
        y = -11 + (z + 12) / 2
        for x in (-3.3, 3.3):
            r.fill(COLUMN, (x, y + 6.5, z), (1.0, 13.0, 1.0))
    wisp(r, (0, 9.5, 14))                                        # (lanterns over the landings: torches would narrow them)
    wisp(r, (0, 3.0, 0))
    wisp(r, (0, -3.0, -14))
    return r


def gearshaft():
    """Two storeys joined by switchback stairs round an open shaft, an iron axle with gears turning up its middle."""
    s = Shell("gearshaft", (16, 24, 16), [("s", 2, 4, -11), ("e", 1, 5, 1)], weight=1.0, max=2, min_depth=1)
    r = s.r
    # flight A up the west side (south to north), the north landing, flight B up the east side (north to south), the upper landing
    stairs(r, (-5, -11, -5), 6, 9, 4.0, fill_below=-11)
    slab(r, -7, 7, 4, 7, -5, STONE, thickness=6.0, cell=10.0)
    stairs(r, (5, -5, 4), 6, 8.5, 4.0, yaw=180)
    slab(r, 3, 7, -7, -4.5, 1.0, STONE, thickness=1.0, cell=10.0)
    girder(r, (2.8, -4.0, 4.0), (2.8, 2.0, -4.5), 0.35)          # a rail along flight B's open side
    girder(r, (-2.8, -10.0, -5.0), (-2.8, -4.0, 4.0), 0.35)      # and flight A's
    axle(r, (0, -11, 0), 23.0, 1.0)
    m = machine(r, "gears")
    for y, d, t in ((-8.0, 4.5, 30), (-2.0, 3.0, -45), (5.0, 5.0, 27)):
        gear(m, (0, y, 0), d, thickness=0.45, spin=y * 7, turn=t)
    torch(r, (1.5, -11, -6.5))
    torch(r, (-5.5, -5, 5.5))
    wisp(r, (5.0, 5.0, -5.8))                                    # over the upper landing (a torch there would narrow it)
    wisp(r, (0, 9, 0))
    spawner(r, "flying", (0, -11, -5))
    return r


def chamber():
    s = Shell("chamber", (16, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5), ("e", 1, 0, -5)], weight=1.5)
    r = s.r
    # a cross of clear floor 5 m wide from each doorway to the middle; the skull along the west wall, the rest in the corners
    box(r, "giant_skull", (-4.6, -2.2, 0.0), (4.6, 5.4, 7.6), yaw=180)
    r.stand("skull_pile", (4.6, -5, 5))
    r.stand("bone_stack", (-5.5, -5, -5), yaw=40)
    brazier(r, (0, -1.0, 0), chain=4.4)
    for x, z in ((5.6, -5.6), (-5.6, 5.6)):
        torch(r, (x, -5, z))
    crystal(r, (-6.0, -5, 6.0))
    wisp(r, (4.5, 3.5, -4.5))
    spawner(r, "melee", (3, -5, 3))
    spawner(r, "melee", (-2, -5, -4))
    return r


GH_SIZE = (24, 26.5, 24)
GH_FLOOR = -GH_SIZE[1] / 2 + 3.5        # a floor 3.5 m thick: the ribcage sunk into it stays inside the room's box
GH_BALCONY = GH_FLOOR + 12.0
# The great hall's ribcage, lying along x with its open chest at the east doorway and its neck 2.4 m short of the stair: the game's
# giant_ribs at 0.75, sunk until the floor meets it 1.4 m above its own bottom (above the curled rib ends that lie across its middle,
# under its narrowest arch), so a way at least 3.15 m wide and 2.1 m high runs all through it, measured on its collider's triangles
# (prefab_shapes.json holds its collider as boxes for the walk and gap checks; the boxes are 0.3 m cubes, so there it looks narrower).
RIB_SCALE, RIB_FLOOR = 0.75, 1.4
_rlo, _rhi = bounds("giant_ribs")
RIBCAGE = ((9.9 - (_rhi[2] - _rlo[2]) * RIB_SCALE / 2, GH_FLOOR + ((_rlo[1] + _rhi[1]) / 2 - RIB_FLOOR) * RIB_SCALE, 0.0),
           (_rhi - _rlo) * RIB_SCALE, 90)


def great_hall():
    """Two storeys: a giant's ribcage lying on the floor, its open chest towards the east doorway and a way through it to its neck, a
    balcony along the north wall reached by stairs up the west wall."""
    F, B = GH_FLOOR, GH_BALCONY
    s = Shell("greathall", GH_SIZE, [("s", 2, 0, F), ("e", 1, 0, F), ("n", 0, 0, B)], weight=0.8, max=1, min_depth=2, floor=False)
    r = s.r
    slab(r, -12, 12, -12, 12, F, STONE, thickness=F + GH_SIZE[1] / 2, cell=10.0)
    slab(r, -11, 11, 5, 11, B, STONE, thickness=1.0, cell=10.0)                         # the balcony
    for x in (-6.0, 0.0, 4.5):
        r.stand(PILLAR, (x, F, 8.5), scale=(0.55, 1.5, 0.55))
    stairs(r, (-9, F, -10), 12, 16, 4.0, fill_below=F)                                 # up the west wall to the balcony
    box(r, "giant_ribs", *RIBCAGE)
    # the skull in the south-east corner, the sword that felled the giant stuck in the floor under the balcony
    box(r, "giant_skull", (8.3, F + 2.1, -8.9), (3.4, 4.1, 5.1), yaw=90)
    lo, hi = bounds("giant_sword1")
    size = (hi - lo) * 0.55
    box(r, "giant_sword1", (-3.0, F + size[1] / 2 - 0.4, 9.0), size, yaw=90, tilt=(0, 4))
    box(r, RAIL, (2.2, B + 0.65, 5.3), (0.6, 1.3, 17.6), yaw=90)               # (the stair comes up at its west end)
    torch(r, (4.5, F, -10.5))
    for z in (-4.6, 4.6):                                                              # either side of the ribcage's mouth
        torch(r, (10.45, F, z))
    torch(r, (8.5, B, 10.2))
    torch(r, (-3.0, B, 10.2))
    brazier(r, (0, B + 3.0, -2.0), chain=6.0)
    wisp(r, (-9, B - 0.5, -1))                                   # (over the stair, high enough to pass under)
    wisp(r, (2, F + 10, -8.8))
    spawner(r, "melee", (3, F, 0))                               # in the ribcage
    spawner(r, "melee", (-2, F, -9))
    spawner(r, "ranged", (3, B, 9))
    spawner(r, "flying", (6, F, 0))
    return r


def engine_room():
    """The machine's heart: two great wall gears turning all the time, smaller ones meshing above them, chains, girders."""
    s = Shell("engine", (16, 24, 16), [("s", 2, 0, -11), ("n", 0, 0, -11)], weight=0.8, max=1, min_depth=2)
    r = s.r
    m = machine(r, "engine")
    for x, sign in ((6.6, 1), (-6.6, -1)):
        gear(m, (x, -1.0, 0), 12.0, axis="z", yaw=90, thickness=0.9, turn=12 * sign)
        bar(r, (x - 1.2, -1.0, 0), (x + 1.2, -1.0, 0), 1.2)
        gear(m, (x, 7.5, -4.5), 5.0, axis="z", yaw=90, thickness=0.6, turn=-29 * sign)     # meshing: faster, the other way
    for z in (-4.0, 0.0, 4.0):
        girder(r, (-7, 8.5, z), (7, 8.5, z), 0.6)
        hang_chain(r, (0, 12.0 - 1, z), 8.0)
    for x, z in ((3.5, -6.0), (-3.5, 6.0)):
        torch(r, (x, -11, z))
    wisp(r, (0, 2, 0))
    crystal(r, (0, -11, 0))
    spawner(r, "melee", (2, -11, 3))
    spawner(r, "ranged", (-2, -11, -3))
    return r


def dead_end():
    s = Shell("deadend", (8, 12, 8), [("s", 2, 0, -5)], weight=1.0)
    r = s.r
    box(r, "giant_helmet2", (0, -2.6, 1.8), (5.8, 5.4, 3.2), yaw=180)
    torch(r, (2.6, -5, -2.4))
    crystal(r, (-2.5, -5, 2.5))
    spawner(r, "melee", (0, -5, -1))
    return r


def chasm():
    """A chasm crossed by a stone bridge on a giant's pillar: its ways in are on the ledges at both ends, a storey up; a long stair goes
    down the west side to the floor of the pit, where bones lie, and a third way leads on from there."""
    s = Shell("chasm", (16, 24, 32), [("n", 0, 0, 1), ("s", 2, 0, 1), ("e", 1, 0, -11)], weight=1.0, max=1, min_depth=1)
    r = s.r
    slab(r, -8, 8, 10, 16, 1.0, STONE, thickness=12.0, cell=10.0)                      # the ledges
    slab(r, -8, 8, -16, -10, 1.0, STONE, thickness=12.0, cell=10.0)
    slab(r, -2.5, 2.5, -10, 10, 1.0, STONE, thickness=1.2, cell=10.0)                   # the bridge
    r.stand(PILLAR, (0, -11, 0), scale=(0.55, 1.4, 0.55))
    for x in (-2.8, 2.8):
        box(r, RAIL, (x, 1.65, 0), (0.5, 1.3, 20.0))
    stairs(r, (-5, -11, 8), 12, 18, 4.0, yaw=180, fill_below=-11)                      # down to the pit
    box(r, "giant_skull", (4.0, -9.2, 5.0), (4.0, 4.6, 6.0), yaw=230)
    r.stand("bone_stack", (3.5, -11, -5.0), scale=(1.8, 1.8, 1.8))
    r.stand("skull_pile", (5.5, -11, -3.0), scale=(1.6, 1.6, 1.6))
    r.stand("Morkhalla_Rubble3", (-0.5, -11, -7.0), yaw=40)
    for z in (-6.0, 6.0):
        hang_chain(r, (0, 0.0, z), 6.0)
    for x, z in ((-5.5, 13.0), (5.5, -13.0)):
        torch(r, (x, 1.0, z))
    torch(r, (6.0, -11, 3.0))
    crystal(r, (-6.5, -11, -8.0))
    crystal(r, (6.5, -11, 7.0))
    wisp(r, (0, -4.0, 0))
    spawner(r, "melee", (3.0, -11, 0))
    spawner(r, "melee", (-1.5, -11, 5.0))
    return r


def archive():
    """The giants' archive: tablets twice a man's height along the walls, a giant's reading table, a chest of what they kept."""
    s = Shell("archive", (16, 12, 24), [("s", 2, 0, -5), ("n", 0, 0, -5)], weight=0.8, max=1, min_depth=1)
    r = s.r
    lo, hi = bounds("MemorialStone_Large")
    for x, yaw in ((-6.4, 90), (6.4, 270)):
        for z in (-7.5, 0.0, 7.5):
            box(r, "MemorialStone_Large", (x, -5 + (hi[1] - lo[1]) * 1.6 / 2 - 0.4, z), (hi - lo) * 1.6, yaw=yaw)
    box(r, "Morkhalla_Table", (0, -3.7, 0), (3.4, 2.6, 3.0))                       # (3.5 m of floor either side of it)
    r.stand("skull_pile", (-1.2, -2.4, 0.2))
    crystal(r, (1.4, -2.4, 0.0))
    for z in (-4.0, 4.0):
        box(r, "Morkhalla_Bench", (0, -4.2, z), (3.4, 1.6, 1.2), yaw=0 if z < 0 else 180)
    r.stand("TreasureChest_trollcave", (-4.0, -5, 10.3), yaw=90, saved=True)       # (beside the way in from the north)
    for x, z in ((-3.4, -10.6), (4.5, 10.6)):
        torch(r, (x, -5, z))
    wisp(r, (0, 3.5, -5.0))
    wisp(r, (0, 3.5, 5.0))
    spawner(r, "ranged", (0, -5, -8.0))
    return r


def forge():
    """A giants' forge: a fire pit, an anvil the size of a cart, iron in stacks, coal, a weapon rack, bellows gears turning."""
    s = Shell("forge", (16, 12, 16), [("s", 2, 0, -5), ("e", 1, 0, -5)], weight=0.8, max=1, min_depth=1)
    r = s.r
    r.stand("Morkhalla_firepit", (-3.5, -5, 3.5), scale=(1.8, 1.8, 1.8))
    box(r, BLOCK, (2.5, -4.3, 3.0), (2.6, 1.4, 1.6))                                   # the anvil
    lo, hi = bounds("giant_sword2")
    size = (hi - lo) * 0.3
    box(r, "giant_sword2", (4.2, -5 + size[1] / 2 - 0.3, 3.6), size, yaw=0, tilt=(4, 5))   # stuck in the floor by the anvil
    for x, z in ((5.2, 5.8), (6.3, 4.6)):                                                # (in the corner, clear of the ways in)
        r.stand("bar_iron_stack", (x, -5, z), scale=(1.6, 1.6, 1.6))
    r.stand("Morkhalla_coal_pile_memorial", (-6.0, -5, -2.0), scale=(1.6, 1.6, 1.6))
    r.stand("Morkhalla_coal_pile_memorial", (-5.5, -5, 6.0), scale=(1.3, 1.3, 1.3))
    r.stand("Morkhalla_WeaponStand", (0.0, -5, 6.7), yaw=180, scale=(1.5, 1.5, 1.5))
    m = machine(r, "bellows")
    gear(m, (-6.8, 0.5, 3.5), 5.0, axis="z", yaw=90, thickness=0.6, turn=20)
    gear(m, (-6.8, 3.0, -0.5), 3.0, axis="z", yaw=90, thickness=0.5, turn=-33)
    torch(r, (5.5, -5, -5.5))
    crystal(r, (-5.5, -5, -5.5))
    spawner(r, "melee", (0, -5, -2.0))
    spawner(r, "melee", (3.0, -5, -4.0))
    return r


def collapsed():
    """A hall that fell in: rubble heaped along the walls, a fallen pillar across the floor, broken paving."""
    s = Shell("collapsed", (16, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5)], weight=1.0, max=2)
    r = s.r
    # heaped along the walls, a fallen pillar along the west side: a clear way down the middle from door to door
    for x, z, n, yaw in ((5.0, -4.0, 2, 0), (5.2, 3.5, 4, 180), (-5.4, 5.8, 1, 90)):
        r.stand(f"Morkhalla_Rubble{n}", (x, -5, z), yaw=yaw)
    lo, hi = bounds(PILLAR)
    box(r, PILLAR, (-5.0, -3.9, -0.5), (hi - lo) * np.array([0.6, 1.3, 0.6]), yaw=90, tilt=(0, 86))
    for x, z, yaw in ((-4.6, -4.6, 10), (4.4, -0.2, 100)):
        box(r, "Morkhalla_Floor_4x4_broken01", (x, -4.85, z), (4.0, 0.4, 2.7), yaw=yaw, tilt=(4, -3))
    r.stand("grausten_pile", (4.6, -5, 6.4), scale=(1.2, 1.2, 1.2))
    hang_chain(r, (3.0, 5.0, -3.0), 5.0)
    torch(r, (-5.8, -5, -6.0))
    torch(r, (5.8, -5, 6.0))
    crystal(r, (-2.5, -5, 2.5))
    spawner(r, "flying", (0, -5, -3.0))
    return r


def crusher():
    """A passage under the machine's hammers: two stone blocks rise and slam down on their own (they stop well above a man's head)."""
    s = Shell("crusher", (8, 12, 24), [("s", 2, 0, -5), ("n", 0, 0, -5)], weight=0.8, max=1, min_depth=2)
    r = s.r
    for i, (z, cycle) in enumerate(((-5.0, 5.0), (5.0, 4.0))):
        h = r.mech(f"hammer{i + 1}", "platform", (0, 1.4, z))
        box(h, BLOCK, (0, 0, 0), (5.6, 2.6, 3.0))
        bar(h, (-1.8, 1.3, 0), (-1.8, 4.0, 0), 0.5)
        bar(h, (1.8, 1.3, 0), (1.8, 4.0, 0), 0.5)
        h.pose().pose(at=(0, -2.6, 0))
        h.cycle = cycle
        h.seconds = 0.9
        for x in (-3.4, 3.4):
            bar(r, (x, -5.0, z - 1.8), (x, 5.0, z - 1.8), 0.5)
            bar(r, (x, -5.0, z + 1.8), (x, 5.0, z + 1.8), 0.5)
    m = machine(r, "gears")
    gear(m, (-3.2, 2.5, 0.0), 4.0, axis="z", yaw=90, thickness=0.5, turn=30)
    gear(m, (3.2, 2.5, 0.0), 4.0, axis="z", yaw=90, thickness=0.5, turn=-30)
    for z in (-10.0, 0.0, 10.0):
        torch(r, (2.0 if z else -2.0, -5, z))
    wisp(r, (0, 0.0, -10.0))
    return r


def nook(name, weight, max=0):
    """A small room that closes an open doorway (an end cap with something in it)."""
    s = Shell(name, (8, 12, 8), [("in", 2, 0, -5)], weight=weight, max=max)
    s.r.end_cap = True
    return s.r


def nook_bones():
    r = nook("nook_bones", 3.0)
    r.stand("skull_pile", (-1.8, -5, 1.8), scale=(1.4, 1.4, 1.4))
    r.stand("bone_stack", (1.8, -5, 2.0), yaw=60, scale=(1.5, 1.5, 1.5))
    box(r, "Skull2", (0.5, -4.0, 2.4), (2.2, 2.2, 2.0), yaw=200)
    hang_chain(r, (-2.5, 5.0, 2.5), 4.5)
    crystal(r, (2.6, -5, -1.0))
    return r


def nook_shrine():
    r = nook("nook_shrine", 2.0)
    box(r, "Morkhalla_Eye5_gemstone", (0, 0.0, 2.85), (2.6, 2.0, 0.6))
    box(r, "Morkhalla_Rug_middle", (0, -4.97, 0.5), (3.0, 0.05, 3.2))
    brazier(r, (0, -5, 1.6))
    for x in (-2.5, 2.5):
        crystal(r, (x, -5, 2.4))
    return r


def nook_tablet():
    r = nook("nook_tablet", 2.0)
    lo, hi = bounds("MemorialStone_Large")
    box(r, "MemorialStone_Large", (0, -5 + (hi[1] - lo[1]) * 1.4 / 2 - 0.3, 2.6), (hi - lo) * 1.4, yaw=180)
    r.stand("bone_stack", (-2.2, -5, 1.0), yaw=20)
    crystal(r, (2.4, -5, 1.6))
    return r


def nook_chest():
    r = nook("nook_chest", 1.0, max=2)
    r.stand("TreasureChest_trollcave", (0, -5, 2.0), yaw=180, saved=True)
    r.stand("Morkhalla_Stonepile", (-2.3, -5, 1.5), yaw=30)
    torch(r, (2.5, -5, 2.5))
    return r


def nook_rubble():
    r = nook("nook_rubble", 1.0)
    r.stand("Morkhalla_Rubble3", (0, -5, 1.0), yaw=180, scale=(1.2, 1.3, 1.2))
    r.stand("Morkhalla_Rubble2", (-2.0, -5, -1.5), yaw=70)
    crystal(r, (2.6, -5, -2.0))
    return r


# ---------------------------------------------------------------- rooms with a job: a fight, a timing crossing, a drop, a held gate, a
# hidden lever, a treasure behind a timing door

def encounter(room, id, area_lo, area_hi, start_on, waves, spawns, lock=(), opens=(), reached_from=None, chest=None, level=1,
              elite_level=3, pause=4.0, timeout=40.0, siege=0, hold_by=None, music="CombatEventL3", boss=None):
    """A fight (Runtime's kind "encounter"): it starts when a player steps into the area (start_on "enter"), when a lever or plate
    wires {"do": "start"} to it ("wire"), or when the saved chest standing at `chest` is opened ("open"). Then the mechanisms in
    `lock` shut (state 0), its waves come one after another (counts by role, from the pack's creatures for the entrance's biome; the
    "elite" role at elite_level), each at the spawn points (floor places round the room), and when the last is dead the ones in
    `opens` open (set:1), as its wires. A siege (siege > 0, hold_by: a plate) is cleared after that many kills, its waves coming only
    while the plate is held. A wipe sets it back (Runtime). timeout: seconds before the next wave comes anyway."""
    lo, hi = np.array(area_lo, float), np.array(area_hi, float)
    m = room.mech(id, "encounter", (lo + hi) / 2)
    m.pose()
    for t in opens:
        m.wire(t, "set:1")
    m.reached_from = reached_from
    m.extra.update({"area": r3(hi - lo), "startOn": start_on, "lock": list(lock), "waves": waves, "level": level,
                    "eliteLevel": elite_level, "pause": pause, "timeout": timeout,
                    "points": [r3(np.array(x, float)) for x in spawns]})
    if siege:
        m.extra.update({"siege": siege, "holdBy": hold_by})
    if music:
        m.extra["music"] = music
    if boss:
        m.extra["boss"] = boss
        if not waves:
            del m.extra["waves"]
    if chest is not None:
        m.extra["chest"] = r3(np.array(chest, float))
    return m


def door_gate(room, id, k, half, thr, start, doorway, open_dir=1, kind="gate", lateral=0.0):
    """An iron gate leaf in side k's doorway (kind gate, closing that doorway in state 0), sliding sideways into the wall to open; in a
    room too narrow for that (the leaf would leave the room's box), rising into the wall above as far as the room's top allows."""
    g = room.mech(id, kind, side_point(k, lateral, thr, half - 0.5), (0, (90 * k) % 360, 0))
    if kind == "gate":
        g.doorway = doorway
    g.fill(DOOR, (0, DOOR_H / 2, 0), (DOOR_W + 0.3, DOOR_H + 0.1, 0.8))
    across = room.size[0 if k % 2 == 0 else 2] / 2
    if abs(lateral + open_dir * (DOOR_W + 0.6)) + (DOOR_W + 0.3) / 2 <= across:
        g.pose().pose(at=(open_dir * (DOOR_W + 0.6), 0, 0))
    else:
        g.pose().pose(at=(0, room.size[1] / 2 - 0.05 - (thr + DOOR_H + 0.1), 0))
    g.start = start
    g.seconds = 2.0
    return g


def arena():
    """A fight: the lever across the arena starts it. Both gates shut, three waves come out of the corners (the last with the land's
    elite), and when the last falls both gates open."""
    s = Shell("arena", (24, 12, 24), [("in", 2, 0, -5), ("out", 0, 0, -5)], tags=["fight"], weight=0.6, max=1)
    r = s.r
    r.extra["joinBy"] = ["in"]
    door_gate(r, "ingate", 2, 12.0, -5, start=1, doorway="in")
    door_gate(r, "outgate", 0, 12.0, -5, start=0, doorway="out")
    giant_lever(r, "release", (7.5, -5, 8.0), 90, [("fight", "start")], reached_from="in")
    for x, z in ((-6.0, -2.0), (6.0, -2.0), (-6.0, 4.0), (6.0, 4.0)):
        r.stand(PILLAR, (x, -5, z), scale=(0.55, 1.25, 0.55))
    box(r, "giant_skull", (-7.5, -3.0, 8.5), (4.4, 5.0, 7.0), yaw=150)
    r.stand("bone_stack", (3.0, -5, 0.5), scale=(1.6, 1.6, 1.6))
    r.stand("skull_pile", (-2.5, -5, 6.0), scale=(1.5, 1.5, 1.5))
    for x, z in ((-9.5, -9.5), (9.5, -9.5), (-9.5, 9.5), (9.5, 9.5)):
        torch(r, (x, -5, z))
    brazier(r, (0, 0.0, 0.0), chain=3.4)
    # the keepers come from the four sides of the pit, away from the way in (the in doorway to the lever, up the east side)
    encounter(r, "fight", (-10.5, -5, -10.5), (10.5, 3, 10.5), "wire",
              [{"melee": 2, "ranged": 1}, {"melee": 3, "flying": 1}, {"melee": 2, "ranged": 1, "elite": 1}],
              [(-8.5, -5, -5.5), (-8.5, -5, 1.0), (2.5, -5, 8.0), (4.0, -5, -1.5), (-3.0, -5, -3.0), (8.5, -5, -2.0)],
              lock=["ingate", "outgate"], opens=["ingate", "outgate"], reached_from="in")
    return r


def spin_bridge():
    """A timing crossing: the middle of the bridge over a pit is a 15 m slab on a pivot that turns a quarter every 6 s by itself; it
    lines up with the ledges every other turn. A fall lands in the pit, whose own way out leads elsewhere."""
    s = Shell("spinbridge", (20, 24, 32), [("n", 0, 0, 1), ("s", 2, 0, 1), ("pit", 1, 0, -11)], tags=["timing"], weight=0.6, max=1)
    r = s.r
    r.extra["joinBy"] = ["n", "s"]
    r.extra["drops"] = [{"a": "n", "b": "pit"}, {"a": "s", "b": "pit"}]
    slab(r, -10, 10, 7.6, 16, 1.0, STONE, thickness=12.0, cell=10.0)
    slab(r, -10, 10, -16, -7.6, 1.0, STONE, thickness=12.0, cell=10.0)
    r.stand(PILLAR, (0, -11, 0), scale=(0.4, 1.24, 0.4))                             # the pivot's column (its top under the slab)
    b = r.mech("bridge", "rotator", (0, 1.0, 0))
    box(b, STONE, (0, -0.5, 0), (2.6, 1.0, 14.8))
    gear(b, (0, -1.3, 0), 2.4, thickness=0.5, turn=60)                                 # (narrower than the slab: no stepping stone)
    for k in range(4):
        b.pose(euler=(0, 90 * k, 0))
    b.join("n", "s", state=0)
    b.join("n", "s", state=2)
    b.cycle = 6.0
    b.seconds = 2.0
    box(r, "giant_skull", (5.0, -9.2, 4.0), (4.0, 4.6, 6.0), yaw=230)
    r.stand("bone_stack", (-5.0, -11, -3.0), scale=(1.8, 1.8, 1.8))
    r.stand("Morkhalla_Rubble3", (4.5, -11, -5.0), yaw=40)
    for x, z in ((-8.0, 12.0), (8.0, -12.0)):
        torch(r, (x, 1.0, z))
    torch(r, (8.0, -11, 4.0))
    crystal(r, (-8.0, -11, 6.0))
    wisp(r, (0, 6.0, 0))
    wisp(r, (0, -6.0, 0))
    spawner(r, "melee", (-3.0, -11, 2.0))
    return r


def lever_alcove():
    """A dead end with a lever in it (a side wing the plan sends you down for the key to a gate further on)."""
    s = Shell("leveralcove", (8, 12, 16), [("in", 2, 0, -5)], tags=["lever"], max=1)
    r = s.r
    r.fill(DARK, (0, -4.5, 4.0), (6.0, 1.0, 5.0))
    giant_lever(r, "lever", (0, -4.0, 4.5), 0, [("step:gate1", "set:1")])
    m = machine(r, "gears")
    gear(m, (0, 2.0, 6.6), 5.0, axis="z", thickness=0.5, turn=-15)
    for x in (-2.6, 2.6):
        hang_chain(r, (x, 5.0, 5.0), 5.5)
        crystal(r, (x, -5, -4.0))
    torch(r, (2.4, -5, 1.0))
    spawner(r, "melee", (0, -5, -3.0))
    return r


def plate_room():
    """A gate in an iron screen across the hall, held open while someone stands on the plate by it; for one player alone, a lever up a
    stair on the near side opens it for good."""
    s = Shell("plateroom", (16, 12, 24), [("in", 2, 0, -5), ("out", 0, 0, -5), ("side", 1, -6.5, -5)], tags=["plate"], weight=0.6,
              max=1)
    r = s.r
    grate_wall(r, (-7.0, 0, 3.0), (-2.6, 0, 3.0), -5.0, 5.0)
    grate_wall(r, (2.6, 0, 3.0), (7.0, 0, 3.0), -5.0, 5.0)
    g = r.mech("screen", "platform", (0, -5, 3.0))
    g.fill(GRATE, (0, 5.0, 0), (5.4, 10.0, 0.25))
    g.pose().pose(at=(4.6, 0, 0.45))
    g.seconds = 1.5
    g.join("in", "out", state=1)
    g.join("in", "side", state=1)
    r.join("out", "side")                      # (both beyond the screen)
    p = r.mech("plate", "plate", (-1.0, -5, -0.5))
    p.fill(DARK, (0, 0.08, 0), (2.4, 0.16, 2.4))
    p.pose().pose(at=(0, -0.1, 0))
    p.seconds = 0.3
    p.wire("screen", "hold")
    p.wire("siege", "start")
    p.reached_from = "in"
    # the siege: whoever holds the plate opens the screen to what waits beyond it; they come through the opening and from the
    # near corners, wave on wave, and when the last falls the screen stays open for good (two players: one holds, one fights)
    encounter(r, "siege", (-7.0, -5, -11.0), (7.0, 3, 11.0), "wire",
              [{"melee": 2, "ranged": 1}, {"melee": 2, "flying": 1}, {"melee": 2, "ranged": 1, "elite": 1}],
              [(-4.0, -5, 8.0), (4.0, -5, 8.5), (0.0, -5, 6.5), (5.0, -5, -3.0), (4.5, -5, -7.0)],
              opens=["screen"], reached_from="in", siege=12, hold_by="plate")
    # the solo way: a stair up the west wall to a ledge with a lever on it
    stairs(r, (-5.0, -5, -10.0), 6, 7.0, 3.0, fill_below=-5)
    slab(r, -7, -3, -3.0, 2.5, 1.0, STONE, thickness=6.0, cell=10.0)
    giant_lever(r, "lever", (-5.0, 1.0, 0.5), 0, [("screen", "set:1")], reached_from="in")
    for x, z in ((5.5, -9.0), (-5.5, 9.0), (5.5, 9.0)):
        torch(r, (x, -5, z))
    wisp(r, (0, 3.0, -4.0))
    wisp(r, (0, 3.0, 8.0))
    spawner(r, "ranged", (3.0, -5, 8.0))
    return r


def drop_shaft():
    """A one-way drop: off the upper ledge onto a heap of rubble 6 m down, then 6 m more to the floor. The stair back up is shut by an
    iron gate at its top, which a lever at the bottom opens."""
    s = Shell("dropshaft", (16, 24, 24), [("top", 2, 0, 1), ("bottom", 0, 0, -11)], tags=["drop"], weight=0.6, max=1)
    r = s.r
    r.extra["drops"] = [{"a": "top", "b": "bottom"}]
    slab(r, -8, 8, -12, -2, 1.0, STONE, thickness=12.0, cell=10.0)                      # the upper ledge
    slab(r, -7, 1, -2, 2, -5.0, STONE, thickness=6.0, cell=10.0)                        # the rubble shelf halfway down
    r.stand("Morkhalla_Rubble3", (-3.0, -5, 0.0), yaw=30)
    stairs(r, (5.0, -11, 10.0), 12, 12.0, 4.0, yaw=180, fill_below=-11)               # the way back up, along the east wall
    grate_wall(r, (2.5, 0, -2.0), (2.5, 0, 4.0), -11.0, 11.0)                          # (keeps the stair apart from the shelf)
    g = r.mech("upgate", "platform", (5.0, -1.0, -1.6))
    g.fill(GRATE, (0, 6.0, 0), (5.2, 12.0, 0.25))
    g.pose().pose(at=(0, -11.0, 0))                                                    # (open: sunk into the solid stair, inside the box)
    g.seconds = 2.5
    g.join("bottom", "top", state=1)
    giant_lever(r, "lever", (-3.0, -11, 9.0), 270, [("upgate", "set:1")], reached_from="bottom")
    for x, z in ((-5.5, -9.0), (5.5, 10.5), (-6.0, 6.0)):
        torch(r, (x, 1.0 if z < -2 else -11, z))
    wisp(r, (0, 4.0, 4.0))
    crystal(r, (-6.0, -5, 1.5))
    spawner(r, "melee", (0, -11, 6.0))
    spawner(r, "flying", (-4.0, -11, 2.0))
    return r


def treasury():
    """A treasure behind a door that opens and shuts by itself every few seconds: get in, grab, get out."""
    s = Shell("treasury", (16, 12, 12), [("in", 2, 0, -5)], weight=0.8, max=2)
    r = s.r
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    g = door_gate(r, "timedoor", 2, 6.0, -5, start=0, doorway="in", kind="platform")   # (a dead end: the solver needn't wait for it)
    g.cycle = 5.0
    g.seconds = 1.2
    r.fill(DARK, (0, -4.6, 2.5), (6.0, 0.8, 3.0))
    r.stand("TreasureChest_trollcave", (0, -4.2, 2.6), yaw=180, saved=True)
    for x in (-5.0, 5.0):
        crystal(r, (x, -5, 3.5))
    r.stand("Morkhalla_Stonepile", (-5.0, -5, -2.0), yaw=40)
    m = machine(r, "gears")
    gear(m, (-5.5, 1.5, 4.8), 4.0, axis="z", thickness=0.5, turn=24)
    gear(m, (-2.6, 1.0, 4.8), 2.4, axis="z", thickness=0.4, turn=-40)
    torch(r, (5.5, -5, -2.5))
    return r


def treasury_ambush():
    """A treasure in plain sight at the end of a long room: lifting the chest's lid wakes the room. The gate behind slams, the dead
    climb out of the alcoves along the walls, two waves, and the gate opens again when they're down."""
    s = Shell("treasury_ambush", (16, 12, 24), [("in", 2, 0, -5)], weight=0.6, max=1, min_depth=2)
    r = s.r
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    door_gate(r, "gate", 2, 12.0, -5, start=1, doorway="in")
    r.fill(DARK, (0, -4.6, 8.0), (6.0, 0.8, 4.0))                                        # the dais
    r.stand("TreasureChest_trollcave", (0, -4.2, 8.4), yaw=180, saved=True)
    for x in (-2.6, 2.6):
        crystal(r, (x, -4.2, 9.4))
    # alcoves: black columns in pairs along both walls, the waves come out from between them
    for x in (-6.4, 6.4):
        for z in (-7.0, -2.0, 3.0, 8.0):
            r.fill(COLUMN, (x, 0.0, z), (1.2, 10.0, 1.2))
        for z in (-4.5, 0.5, 5.5):
            r.stand("bone_stack", (x * 1.06, -5, z + 0.9), yaw=90 if x < 0 else 270, scale=(1.2, 1.2, 1.2))
    for x, z in ((-5.5, -10.0), (5.5, -10.0)):
        torch(r, (x, -5, z))
    wisp(r, (0, 3.5, 2.0))
    wisp(r, (0, 3.5, -6.0))
    encounter(r, "ambush", (-7.0, -5, -11.0), (7.0, 3, 11.0), "open",
              [{"melee": 3, "ranged": 1}, {"melee": 2, "flying": 1, "elite": 1}],
              [(-5.0, -5, -4.5), (5.0, -5, -4.5), (-5.0, -5, 0.5), (5.0, -5, 0.5), (-5.0, -5, 5.0), (5.0, -5, 5.0)],
              lock=["gate"], opens=["gate"], reached_from="in", chest=(0, -4.2, 8.4))
    return r


def guardian_room():
    """The guardian before the great machine: a long, high hall with room to fight. Step past the threshold and both gates shut;
    the land's elite at its strongest comes down from the dais; when it falls the way on opens."""
    s = Shell("guardian", (24, 16, 32), [("in", 2, 0, -7), ("out", 0, 0, -7), ("side", 1, 8.0, -7)], tags=["guardian"], weight=1.0,
              max=1, min_depth=3)
    r = s.r
    r.extra["joinBy"] = ["in"]
    door_gate(r, "ingate", 2, 16.0, -7, start=1, doorway="in")
    door_gate(r, "outgate", 0, 16.0, -7, start=0, doorway="out")
    # a side door, shut until the guardian falls: where the shortcut back to the entrance ends (Layout puts it there or nearby)
    door_gate(r, "sidegate", 1, 12.0, -7, start=0, doorway="side", open_dir=-1, lateral=8.0)
    r.fill(DARK, (0, -6.6, 9.5), (10.0, 0.8, 5.0))                                       # the dais it waits on
    for z in (-10.0, -2.0, 6.0):
        for x in (-9.0, 9.0):
            r.stand(PILLAR, (x, -7, z), scale=(0.6, 1.6, 0.6))
            torch(r, (x * 0.8, -7, z + 2.0))
    box(r, "giant_helmet2", (-9.5, -5.6, 12.0), (3.0, 2.8, 3.2), yaw=200)
    r.stand("skull_pile", (9.0, -7, 12.5), scale=(1.6, 1.6, 1.6))
    r.stand("bone_stack", (-9.6, -7, -13.0), yaw=30, scale=(1.5, 1.5, 1.5))
    for x in (-5.0, 5.0):
        brazier(r, (x, 0.0, 0.0), chain=4.5)
    hang_chain(r, (0, 7.0, -6.0), 5.0)
    wisp(r, (0, 3.0, 10.0))
    encounter(r, "guardian", (-11.0, -7, -11.0), (11.0, 3, 15.0), "enter",
              [{"elite": 1}],
              [(0.0, -6.2, 10.0)],
              lock=["ingate", "outgate"], opens=["ingate", "outgate", "sidegate"], reached_from="in", elite_level=3,
              music="boss_gdking")
    return r


def ledge_gallery():
    """A gallery whose far gate is shut: its lever is up on a ledge along the wall, reached by a stair at the near end."""
    s = Shell("ledgegallery", (12, 12, 24), [("in", 2, 0, -5), ("out", 0, 0, -5)], weight=1.0, max=2)
    r = s.r
    r.extra["joinBy"] = ["in"]
    door_gate(r, "outgate", 0, 12.0, -5, start=0, doorway="out")
    stairs(r, (-3.5, -5, -10.0), 5, 7.5, 3.0, fill_below=-5)                          # up the west wall
    slab(r, -6, -2, -2.5, 9.0, 0.0, STONE, thickness=5.0, cell=10.0)                    # the ledge, 5 m up
    box(r, RAIL, (-1.8, 0.85, 3.25), (0.5, 1.7, 11.5))
    giant_lever(r, "lever", (-3.6, 0.0, 7.5), 0, [("outgate", "set:1")], reached_from="in")
    for z in (-6.0, 2.0):
        r.fill(COLUMN, (4.8, 0.0, z), (1.2, 10.0, 1.2))
    lo, hi = bounds("giant_sword2")
    box(r, "giant_sword2", (4.6, -1.0, 7.5), (hi - lo) * 0.6, yaw=90, tilt=(0, -12))
    torch(r, (3.5, -5, -9.5))
    torch(r, (-4.0, 0.0, 3.0))
    wisp(r, (1.0, 3.5, 6.0))
    spawner(r, "ranged", (-4.0, 0.0, 0.0))
    spawner(r, "melee", (2.0, -5, 4.0))
    return r


def lever_vault():
    """A treasure behind a stone door worked by two levers: one opens it a notch at a time, the other drops it shut again. Which is
    which is for the player to find."""
    s = Shell("levervault", (16, 12, 16), [("in", 2, 0, -5)], weight=0.6, max=1)
    r = s.r
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    for x0, x1 in ((-8.0, -2.6), (2.6, 8.0)):                                          # the inner wall, a door in its middle
        box(r, STONE, ((x0 + x1) / 2, 0.0, 2.0), (x1 - x0, 10.0, 1.0))
    box(r, STONE, (0, 3.5, 2.0), (5.2, 3.0, 1.0))
    d = r.mech("stonedoor", "platform", (0, -5, 2.0))
    d.fill(BLOCK, (0, 3.0, 0), (5.2, 6.0, 0.9))
    d.pose().pose(at=(0, 1.6, 0)).pose(at=(0, 3.2, 0)).pose(at=(0, 4.8, 0))
    d.seconds = 1.5
    giant_lever(r, "raise", (-5.0, -5, -3.0), 0, [("stonedoor", "next")])
    giant_lever(r, "drop", (5.0, -5, -3.0), 0, [("stonedoor", "set:0")])
    r.stand("TreasureChest_trollcave", (0, -5, 5.5), yaw=180, saved=True)
    for x in (-5.0, 5.0):
        crystal(r, (x, -5, 5.5))
    m = machine(r, "gears")
    gear(m, (0, 3.6, 1.4), 4.0, axis="z", thickness=0.4, turn=20)
    torch(r, (-6.3, -5, -7.3))
    return r


def cap_gate():
    """End cap: a sealed iron gate leaf in the doorway."""
    r = Room("cap_gate", (8, 12, 0), end_cap=True, weight=0.3)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=TYPE)
    r.fill(DOOR, (0, -5 + DOOR_H / 2, 0.45), (DOOR_W, DOOR_H, 0.8))
    lo, hi = bounds(CHAINS)
    box(r, CHAINS, (0, 0.8, 0.05), (DOOR_W + 0.8, 3.0, 0.5))
    return r


def shortcut_door(name, deep):
    """One end of the shortcut (Runtime's kind "shortcut", pair "sc1"): a sealed iron door in a doorway, a bronze wheel on its face
    and chains across it. The deep one (near the guardian) opens both from its side; then either takes you to the other.
    The door's +z faces the room players stand in."""
    r = Room(name, (8, 12, 0), end_cap=True, weight=0.01, max=1)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=TYPE)
    d = r.mech("shortcut", "shortcut", (0, -5, 0), (0, 180, 0))
    d.pose().pose()
    d.fill(DOOR, (0, DOOR_H / 2, -0.45), (DOOR_W, DOOR_H, 0.8))
    gear(d, (0, DOOR_H * 0.55, 0.05), 2.6, axis="z", thickness=0.3)
    box(d, HEAD, (0, DOOR_H * 0.55, 0.25), (0.7, 0.7, 0.4))
    if not deep:
        box(d, CHAINS, (0, 2.8, 0.1), (DOOR_W + 0.6, 2.4, 0.4))       # sealed from this side: chained
    d.extra.update({"pair": "sc1", "deep": deep})
    return r


def cap_rubble():
    """End cap: a fallen-in passage."""
    r = Room("cap_rubble", (8, 12, 0), end_cap=True, weight=0.2)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=TYPE)
    r.fill(STONE, (0, -1.0, 0.7), (DOOR_W + 0.4, DOOR_H + 0.2, 1.0), euler=(90, 0, 0))
    r.stand("grausten_pile", (-1.2, -5, -0.3), scale=(1.4, 1.4, 1.4))
    r.stand("grausten_pile", (1.4, -5, 0.0), yaw=120, scale=(1.2, 1.6, 1.2))
    return r


def entrance():
    """Where players come in: a two-storey gate hall under a Jotunn's carved face, pillars, the first blue fires."""
    s = Shell("entrance", (16, 24, 24), [("in", 2, 0, -11), ("n", 0, 0, -11), ("e", 1, 4, -11), ("w", 3, -4, -11)],
              tags=["entrance"], entrance="in")
    r = s.r
    r.entrance = True
    for z in (-6.0, 2.0):
        for x in (-5.5, 5.5):
            for y in (-11.0, -3.0, 5.0):
                r.stand(PILLAR, (x, y, z), scale=(0.6, 0.75 if y == 5.0 else 1.0, 0.6))
            torch(r, (x * 0.7, -11, z - 2.5))
    lo, hi = bounds("Morkhalla_StatuePieceFace_big")
    box(r, "Morkhalla_StatuePieceFace_big", (0, 4.5, 8.4), (hi - lo) * 1.7, yaw=180)
    for x in (-3.0, 3.0):
        brazier(r, (x, 2.5, 4.0), chain=6.9)
    gear(machine(r, "gears"), (0, 9.5, -2.0), 9.0, thickness=0.5, turn=8)
    crystal(r, (-6.5, -11, -10))
    crystal(r, (6.5, -11, -10))
    return r


# ---------------------------------------------------------------- plan rooms: lever, gate, rotunda, vault

def lever_room():
    s = Shell("leverroom", (16, 12, 16), [("in", 2, 0, -5), ("out", 0, 0, -5)], tags=["lever"], max=1)
    r = s.r
    giant_lever(r, "lever", (4.0, -5, 0.0), 90, [("step:gate1", "set:1")])
    # chains run from the lever's gear into the wall: it drives the gate somewhere further on
    girder(r, (4.0, -2.5, 0.0), (7.0, 1.5, 0.0), 0.3)
    gear(machine(r, "gears"), (7.1, 1.5, 0.0), 4.0, axis="z", yaw=90, thickness=0.4, turn=25)
    for x, z in ((-5.5, -5.5), (-5.5, 5.5)):
        torch(r, (x, -5, z))
    crystal(r, (5.5, -5, 5.5))
    wisp(r, (-2.0, 3.5, 0))
    spawner(r, "ranged", (-4, -5, 0))
    return r


def gate_room():
    """The gatehouse: 24 m tall, so its portcullis can rise; a winch gear and chains above."""
    s = Shell("gateroom", (8, 24, 16), [("in", 2, 0, -11), ("out", 0, 0, -11)], tags=["gate"], max=1)
    r = s.r
    g = r.mech("gate", "gate", (0, -11, -5.7))
    for x in (-1.25, 1.25):
        g.fill(GRATE, (x, DOOR_H / 2, 0), (2.5, DOOR_H, 0.14))
    girder(g, (-2.9, DOOR_H + 0.2, 0), (2.9, DOOR_H + 0.2, 0), 0.5)
    g.pose().pose(at=(0, 8.6, 0))
    g.doorway = "in"
    g.seconds = 3.0
    gear(machine(r, "gears"), (-3.0, 6.0, -4.0), 5.0, axis="z", yaw=90, thickness=0.5, turn=18)
    for x in (-2.0, 2.0):
        hang_chain(r, (x, 11.0, -5.7), 3.0)
    torch(r, (2.3, -11, 3.0))
    torch(r, (-2.3, -11, 6.0))
    wisp(r, (0, 0, 0))
    spawner(r, "melee", (0, -11, 4))
    return r


def rotunda():
    """24 x 12 x 24: four doorways, and a drum under a 17 m bronze cogwheel that turns a quarter every 12 s by itself. Its L-shaped
    passage joins doorway k and k + 1 in state k; walls from the drum to the corners keep the doorways apart."""
    s = Shell("rotunda", (24, 12, 24), [(SIDES[k], k, 0, -5) for k in range(4)], tags=["rotator"], max=1)
    r = s.r
    for a in (45, 135, 225, 315):
        box(r, STONE, yaw_turn((0, 0.0, 12.7), a), (1.4, 10.0, 7.4), yaw=a)       # from r 9 (clear of the gear) to the corner
    d = r.mech("drum", "rotator", (0, -5, 0))
    n = 20
    R = 8.0
    chord = 2 * R * math.sin(math.pi / n) + 0.15
    for j in range(n):
        a = (360.0 / n) * (j + 0.5)
        if min(abs(a - 0), abs(a - 360)) < 19 or abs(a - 90) < 19:
            continue
        box(d, STONE, yaw_turn((0, 4.0, R), a), (chord, 8.0, 0.7), yaw=a)
    gear(d, (0, 8.35, 0), 17.6, thickness=0.7)                       # the lid: the drum is the hub of a giant gear
    for a in (45, 225):
        wisp(d, yaw_turn((0, 6.8, R - 1.0), a))                     # (hung from the lid)
    for a in (135, 315):
        torch(d, yaw_turn((0, 0, R - 1.2), a))                       # blue fires that ride round with the drum
    for a in (20, 110, 200, 290):
        hang_chain(d, yaw_turn((0, 8.0, 5.5), a), 3.5, yaw=a)
    for k in range(4):
        d.pose(euler=(0, 90 * k, 0))
        d.join(SIDES[k], SIDES[(k + 1) % 4], state=k)
    d.cycle = 12.0
    d.seconds = 4.0
    axle(r, (0, -5, 0), 10.0, 1.4)
    for a in (0, 90, 180, 270):
        crystal(r, yaw_turn((0, -5, 1.6), a), yaw=a)
    for k in range(4):
        torch(r, s.inside(k, 4.2, -5, 0.6))
    return r


WD_SIZE = (36, 24, 40)
WD_FLOOR = -WD_SIZE[1] / 2 + 4.0       # a floor 4 m thick: the rising blocks stand in it


def cracked_panel(room, id, half, thr, doorway):
    """A doorway walled up with cracked stone (Runtime's kind "breakable"): the same grey stone as the walls, but in six slabs knocked
    askew, gaps between them dark, one corner fallen out: what a sharp eye notices. Broken, its parts go and the doorway opens
    (chips of it lie inside)."""
    b = room.mech(id, "breakable", side_point(2, 0.0, thr, half - 0.5), (0, 180, 0))
    b.pose().pose()
    b.doorway = doorway
    b.extra["health"] = 300
    b.fill(DARK, (0, DOOR_H / 2, -0.4), (DOOR_W + 0.2, DOOR_H + 0.1, 0.2))          # the dark behind the gaps (its -z: inside)
    w, h = (DOOR_W + 0.2) / 2, (DOOR_H + 0.1) / 3
    tilts = ((6, -7, 4), (-5, 6, -6), (8, 4, -3), (-6, -5, 7), (5, 9, 5), (-8, -6, -4))
    for i in range(2):
        for j in range(3):
            if (i, j) == (1, 2):
                continue                                                              # the fallen corner
            tx, ty, tz = tilts[i * 3 + j]
            x = -w / 2 + i * w + 0.06 * tx
            y = h / 2 + j * h + 0.04 * ty
            box(b, STONE, (x, y, -0.05 * abs(tz) - 0.15), (w - 0.18, h - 0.16, 0.6), yaw=tz, tilt=(tx, ty))   # (askew inwards)
    box(b, STONE, (w / 2 + 0.3, 2.5 * h - 0.6, -0.25), (w - 0.9, h - 0.9, 0.5), yaw=12, tilt=(8, -14))   # half of it, slumped
    for x, z, n, sc in ((-2.4, 1.2, 2, 0.3), (2.3, 1.6, 4, 0.28), (-1.2, 2.6, 1, 0.22)):
        room.stand(f"Morkhalla_Rubble{n}", side_point(2, x, thr, half - 0.5 - z), yaw=40 * n, scale=(sc, sc, sc))
    return b


def secret_room(name, size, loot_at, weight):
    """A secret: a small room off a spare doorway, walled up with cracked stone, never on the way (an end cap the layout keeps off
    the solution: tag "secret")."""
    sx, sy, sz = size
    s = Shell(name, size, [("in", 2, 0, -sy / 2 + 1.0)], tags=["secret"], weight=weight, max=1)
    r = s.r
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    cracked_panel(r, "crack", sz / 2, -sy / 2 + 1.0, "in")
    return r


def secret_cache():
    """A forgotten store: a chest of the land's better things on a stone shelf, giant cogs heaped, a lantern."""
    r = secret_room("secret_cache", (12, 12, 12), None, 1.0)
    r.fill(DARK, (0, -4.5, 3.5), (6.0, 1.0, 3.0))
    r.stand("TreasureChest_trollcave", (0, -4.0, 3.6), yaw=180, saved=True)
    r.stand("Morkhalla_ChestAncient", (-3.8, -5, 3.8), yaw=150)
    r.stand("bar_iron_stack", (3.6, -5, 3.0), scale=(1.4, 1.4, 1.4))
    r.stand("Morkhalla_Stonepile", (3.5, -5, -2.5), yaw=80)
    for x in (-4.0, 4.0):
        crystal(r, (x, -5, -3.6))
    wisp(r, (0, 2.0, 1.0))
    return r


def secret_shrine():
    """A giant's shrine: a carved face in the far wall, an altar with an offering chest, bones of the faithful, cold light."""
    r = secret_room("secret_shrine", (16, 12, 12), None, 1.0)
    lo, hi = bounds("Morkhalla_StatuePieceFace_big")
    box(r, "Morkhalla_StatuePieceFace_big", (0, 1.5, 4.6), (hi - lo) * 1.2, yaw=180)
    r.fill(DARK, (0, -4.4, 2.6), (5.0, 1.2, 2.4))
    r.stand("TreasureChest_trollcave", (0, -3.8, 2.6), yaw=180, saved=True)
    r.stand("Morkhalla_Eye5_gemstone", (2.0, -3.8, 2.4), yaw=30)
    for x in (-5.8, 5.8):
        r.stand("bone_stack", (x, -5, 2.5), yaw=90 if x < 0 else 270, scale=(1.2, 1.2, 1.2))
        torch(r, (x, -5, -3.8))
    for x in (-3.0, 3.0):
        crystal(r, (x, -5, 4.2))
    wisp(r, (0, 2.5, -1.0))
    return r


def warden_hall():
    """The Clockwork Warden's hall, between the great hall and the vault: a vast floor, the Warden on a dais before the vault's
    gate. Step in and the gate behind shuts. At two thirds of its health the hammers over the side lanes start to fall and the wall
    gears race; at one third four blocks rise out of the floor and its servants come out of the alcoves. When it falls the vault's
    gate opens (the vault's way out is beyond, untouched). Its trophy is the Warden's own drop (Runtime), so the room has no chest."""
    F = WD_FLOOR
    s = Shell("warden", WD_SIZE, [("in", 2, 0, F), ("out", 0, 0, F)], tags=["boss"], max=1, floor=False, door_type=VAULT)
    r = s.r
    r.doorways[1]["type"] = WARDEN
    r.extra["joinBy"] = ["in"]
    slab(r, -18, 18, -20, 20, F, STONE, thickness=F + WD_SIZE[1] / 2, cell=10.0)
    door_gate(r, "ingate", 2, 20.0, F, start=1, doorway="in")
    door_gate(r, "outgate", 0, 20.0, F, start=0, doorway="out")
    # the dais: two black tiers before the vault's gate (around it on both sides to the gate)
    r.fill(DARK, (0, F + 0.3, 10.5), (15.0, 0.6, 9.0))
    r.fill(DARK, (0, F + 0.9, 11.0), (11.0, 0.6, 6.0))
    for x in (-4.5, 4.5):
        brazier(r, (x, F + 1.2, 13.0))
    # the walls: black columns in pairs, alcoves between them on the east and west walls (where the servants come from)
    for x in (-16.4, 16.4):
        for z in (-15.0, -9.0, -3.0, 3.0, 9.0, 15.0):
            r.fill(COLUMN, (x, F + 7.0, z), (1.4, 14.0, 1.4))
    for z in (-18.4, 18.4):
        for x in (-12.0, -6.0, 6.0, 12.0):
            r.fill(COLUMN, (x, F + 7.0, z), (1.4, 14.0, 1.4))
    for x in (-16.0, 16.0):
        for z in (-6.0, 6.0):
            r.stand("bone_stack", (x * 1.02, F, z + 1.6), yaw=90 if x < 0 else 270, scale=(1.3, 1.3, 1.3))
    # the machinery the fight wakes
    gears = machine(r, "gears")
    gears.extra["spinScale"] = [1, 2.5, 4.0]                       # racing faster at each phase
    gears.pose().pose()                                            # (a pose per state: they only turn faster)
    for x, sign in ((-17.2, 1), (17.2, -1)):
        gear(gears, (x, F + 12.0, 0.0), 10.0, axis="z", yaw=90, thickness=0.8, turn=6 * sign)
        gear(gears, (x, F + 7.0, -8.5), 5.0, axis="z", yaw=90, thickness=0.6, turn=-12 * sign)
    gear(gears, (0, F + 13.5, 19.0), 8.0, axis="z", thickness=0.7, turn=9)
    for i, x in enumerate((-10.0, 10.0)):
        h = r.mech(f"crusher{i + 1}", "platform", (x, F + 10.0, -2.0))
        box(h, BLOCK, (0, 0, 0), (6.0, 3.0, 5.0))
        for bx in (-2.2, 2.2):
            bar(h, (bx, 1.5, 0), (bx, 5.0, 0), 0.6)
        h.pose().pose(at=(0, -5.4, 0))                            # (down: its foot 3.1 m over the floor)
        h.seconds = 0.8
        h.cycle = 3.0 + 0.7 * i                                   # slamming over and over, once woken (state 1)
        h.extra["cycleIn"] = [1]
        for bx, bz in ((-3.6, -3.0), (3.6, -3.0), (-3.6, 3.0), (3.6, 3.0)):
            bar(r, (x + bx, F, -2.0 + bz), (x + bx, F + 14.0, -2.0 + bz), 0.5)
    for i, (x, z) in enumerate(((-12.0, -13.0), (12.0, -13.0), (-12.0, 4.5), (12.0, 4.5))):
        b = r.mech(f"riser{i + 1}", "platform", (x, F, z))
        b.fill(STONE, (0, -1.4, 0), (4.0, 3.6, 4.0))              # its top 0.4 m over the floor, 3.4 m when risen
        b.pose().pose(at=(0, 3.0, 0))
        b.seconds = 3.0
    for x, z in ((-8.0, -17.0), (8.0, -17.0), (-15.0, 12.0), (15.0, 12.0), (-15.0, -17.5), (15.0, -17.5)):
        torch(r, (x, F, z))
    for x, z in ((-6.0, -6.0), (6.0, -6.0), (0.0, 3.0)):
        brazier(r, (x, F + 8.0, z), chain=WD_SIZE[1] / 2 - 1.0 - (F + 8.0 + 1.6))
    wisp(r, (0, F + 9.0, -14.0))
    # the Warden: the land's elite at its cap, four times its health, on the dais; at two thirds the hammers wake and two of its
    # servants come out of the alcoves, at one third the blocks rise and two more come (the trophy is Runtime's, on its death)
    alcoves = [(x * 15.6, F, z) for x in (-1, 1) for z in (-6.0, 6.0)]
    encounter(r, "warden", (-17.0, F, -17.0), (17.0, F + 12.0, 19.0), "enter", [], [(0.0, F + 1.2, 12.0)] + alcoves,
              lock=["ingate"], opens=["ingate", "outgate"], reached_from="in", music="boss_frozenking",
              boss={"name": "$dungeons_boss_warden", "role": "elite", "health": 4,
                    "phases": [{"at": 0.66, "wires": [{"target": "crusher1", "do": "set:1"}, {"target": "crusher2", "do": "set:1"},
                                                      {"target": "gears", "do": "set:1"}],
                                "adds": {"melee": 2}},
                               {"at": 0.33, "wires": [{"target": f"riser{i}", "do": "set:1"} for i in range(1, 5)]
                                + [{"target": "gears", "do": "set:2"}],
                                "adds": {"melee": 1, "ranged": 2}}]})
    return r


def vault():
    """The giants' hoard, 24 m deep. A plate just inside its door opens an iron gate in the far wall, and behind it is the way out
    (Runtime's "exit": back to the entrance, as the entrance room's Leave), so nobody walks the whole way back."""
    s = Shell("vault", (16, 12, 24), [("in", 2, 0, -5)], tags=["end"], max=1, door_type=WARDEN)
    r = s.r
    r.doorways[0]["door"] = False
    r.fill(DARK, (0, -4.5, 0.5), (8.0, 1.0, 7.0))                                   # the dais
    r.stand("Morkhalla_ChestAncient", (0, -4.0, 2.4), yaw=180)                     # the giants' chest: scenery
    r.stand("TreasureChest_trollcave", (0, -4.0, -0.6), yaw=180, saved=True)        # the treasure that opens
    for x in (-5.5, 5.5):
        r.stand(PILLAR, (x, -5, 2.0), scale=(0.4, 0.4, 0.4))
        crystal(r, (x, -5 + 3.2, 2.0))
    # the far wall with the way out: an opening 4 x 6 m, its gate, and the exit behind
    for x0, x1 in ((-8.0, -2.0), (2.0, 8.0)):
        box(r, STONE, ((x0 + x1) / 2, 0.0, 8.5), (x1 - x0, 11.0, 1.0))
    box(r, STONE, (0, 3.5, 8.5), (4.0, 4.0, 1.0))
    lo, hi = bounds("giant_sword2")
    for x, t in ((-4.5, 14), (4.5, -14)):
        box(r, "giant_sword2", (x, -1.2, 7.6), (hi - lo) * 0.6, yaw=0, tilt=(0, t))         # (tips in the floor)
    box(r, "giant_helmet1", (0, 3.4, 7.4), (2.6, 2.4, 2.8), yaw=180)
    g = r.mech("exitgate", "platform", (0, -5, 8.5))
    g.fill(DOOR, (0, 3.0, 0), (4.2, 6.2, 0.6))
    g.pose().pose(at=(4.4, 0, 0))
    g.seconds = 3.0
    p = r.mech("plate", "plate", (0, -5, -8.0))
    p.fill(DARK, (0, 0.08, 0), (2.4, 0.16, 2.4))
    p.pose().pose(at=(0, -0.1, 0))
    p.seconds = 0.4
    p.wire("exitgate", "set:1")
    p.reached_from = "in"
    out = r.mech("exit", "exit", (0, -5, 10.3), (0, 180, 0))
    lo, hi = bounds("Ashlands_Fortress_Gate")
    box(out, "Ashlands_Fortress_Gate", (0, 2.6, 0.9), (5.4, 5.4, 0.8))
    crystal(out, (0, 0, -0.2))
    wisp(r, (0, 2.5, 10.0))
    for i, (x, z, d) in enumerate(((-5.4, 3.4, 2.4), (-5.1, 3.0, 2.0), (-5.6, 3.6, 1.6), (-4.6, 4.4, 1.8), (-5.2, 3.3, 1.2))):
        gear(r, (x, -5 + 0.16 + 0.26 * i, z), d, thickness=0.25, spin=37 * i)        # a heap of giant cogs (scenery: the hint)
    for x, z in ((-6.0, -9.0), (6.0, -9.0), (-6.0, 6.0), (6.0, 6.0)):
        torch(r, (x, -5, z))
    brazier(r, (0, 0.5, -4.0), chain=3.0)
    wisp(r, (0, 3.6, -6.0))
    spawner(r, "melee", (-4.0, -5, -3.0))          # the hoard's keepers
    spawner(r, "ranged", (4.0, -5, -3.0))
    return r


def vault_cap():
    """Closes the vault doorway if the layout ever leaves it open (in a good layout the vault always takes it)."""
    r = Room("vaultcap", (8, 12, 0), end_cap=True, weight=0.01)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=VAULT, door=False)
    r.fill(DOOR, (0, -5 + DOOR_H / 2, 0.45), (DOOR_W, DOOR_H, 0.8))
    return r


def warden_cap():
    """Closes the Warden's hall's far doorway if the layout ever leaves it open (in a good layout the vault always takes it)."""
    r = Room("wardencap", (8, 12, 0), end_cap=True, weight=0.01)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=WARDEN, door=False)
    r.fill(DOOR, (0, -5 + DOOR_H / 2, 0.45), (DOOR_W, DOOR_H, 0.8))
    return r


# ---------------------------------------------------------------- the Shifting Hall

HALF, TOP, BOTTOM = 16.0, 24.0, -24.0
STOREYS = [-23.0, -11.0, 1.0, 13.0]     # threshold (floor) heights
VOID = 10.0                             # balconies from here to the walls
WALL_IN = HALF - WALL                   # the walls' inside face
R_IN, R_OUT = 9.94, 9.96                # nothing on a turning stair reaches past R_IN; nothing still comes inside R_OUT
FLIGHT_LAT = (-2.75, -0.5)              # a stair's width, beside the middle (all on the same hand, so two never meet at a storey)
FLIGHT_RUN = 9.3                        # the steps run from -9.3 to 9.3; thin plates take its ends on to R_IN
BARRIER_FROM = 4.0                      # a fallen corner's bars start this far from the middle along the void's edge


def door_lateral(s):
    """Doorways alternate sides of the middle from storey to storey, so no room with two doorways above each other on one wall can join
    two storeys behind the stairs' backs."""
    return -6.0 if s % 2 == 0 else 6.0


def segments(corners):
    """{storey: {side: segment}}: storey 0 is one floor; above it a balcony side joins the next one where their corner is there."""
    segs = {0: {k: 0 for k in range(4)}}
    for s in (1, 2, 3):
        parent = list(range(4))

        def find(a):
            while parent[a] != a:
                a = parent[a]
            return a
        for c in range(4):
            if corners[s - 1][c]:
                parent[find(c)] = find((c + 1) % 4)
        segs[s] = {k: find(k) for k in range(4)}
    return segs


def door_id(s, k):
    return f"s{s}{SIDES[k]}"


def strips(lo, hi, width=0.5):
    out, x = [], lo
    while x < hi - 1e-6:
        out.append((x, min(hi, x + width)))
        x += width
    return out


def flight(room, s, start):
    """The turning stair between storey s and s + 1, hanging on the axle by a bronze gear: state k puts its foot at side k and its head
    at the opposite side a storey up. Its ends are cut to an arc (R_IN) so it turns clear of the balconies, which reach out to meet it."""
    m = room.mech(f"stair{s + 1}", "stair", (0, STOREYS[s] + 6.0, 0))
    a, b = FLIGHT_LAT
    stairs(m, (0, -6.0, -FLIGHT_RUN), 12, 2 * FLIGHT_RUN, b - a, lateral=(a + b) / 2)
    for l0, l1 in strips(a, b, 0.75):
        far = max(abs(l0), abs(l1))
        z = math.sqrt(R_IN ** 2 - far ** 2)
        if z > FLIGHT_RUN + 0.02:
            m.fill(STONE, ((l0 + l1) / 2, 5.75, (FLIGHT_RUN + z) / 2), (l1 - l0, 0.5, z - FLIGHT_RUN))
            m.fill(STONE, ((l0 + l1) / 2, -5.95, -(FLIGHT_RUN + z) / 2), (l1 - l0, 0.1, z - FLIGHT_RUN))
    # on the axle above the stair (spins while it turns): 3 m across and high enough that a man walking up under it keeps his head
    # (its sweep reaches the steps 2 m up), low enough that its top is no stepping stone to the next stair up
    gear(m, (0, 4.3, 0), 3.0, thickness=0.5, turn=45)
    for k in range(4):
        m.pose(euler=(0, (180 + 90 * k) % 360, 0))
    m.start = start
    m.seconds = 4.0
    for k in range(4):
        m.join(door_id(s, k), door_id(s + 1, (k + 2) % 4), state=k)
    return m


def hall(name, corners, levers, start, falls, weight=1.0):
    """One Shifting Hall. corners[s - 1][c]: on storey s, corner c (0 ne, 1 se, 2 sw, 3 nw) joins side c to side c + 1. levers: (storey,
    side, wires) with wires as (mechanism id, do), the gate's lever last. start: the three stairs' first states. falls: the drops, as
    hall_drops() reads them."""
    r = Room(name, (2 * HALF, TOP - BOTTOM, 2 * HALF), tags=["hall"], weight=weight, max=1)
    r.extra["joinBy"] = [door_id(0, k) for k in range(4)]     # always entered from the ground floor
    r.extra["drops"] = hall_drops(falls)
    segs = segments(corners)
    for s, y in enumerate(STOREYS):
        for k in range(4):
            vault_door = s == 3 and k == 0
            r.doorway(door_id(s, k), side_point(k, door_lateral(s), y, HALF), facing(k), type=VAULT if vault_door else TYPE,
                      door=not vault_door)
    for s in range(4):
        groups = collections.defaultdict(list)
        for k in range(4):
            groups[segs[s][k]].append(k)
        for ks in groups.values():
            for k0, k1 in zip(ks[:-1], ks[1:]):
                r.join(door_id(s, k0), door_id(s, k1))

    # walls with their doorways, the ground, the ceiling, the balconies
    for k in range(4):
        openings = [(door_lateral(s), y) for s, y in enumerate(STOREYS)]
        wall(r, k, HALF, BOTTOM + 1.0, TOP, openings, STONE, thickness=WALL, cell=10.0, size=(DOOR_W, DOOR_H))
        for s, y in enumerate(STOREYS):
            door_frame(r, k, door_lateral(s), y, WALL_IN)
    slab(r, -HALF, HALF, -HALF, HALF, STOREYS[0], STONE, thickness=1.0, cell=10.0)
    slab(r, -HALF, HALF, -HALF, HALF, TOP, STONE, thickness=1.0, cell=10.0, ceiling=True)
    for s in (1, 2, 3):
        y = STOREYS[s]
        for k in range(4):
            _floor(r, k, -VOID, VOID, VOID, HALF, y)
            if corners[s - 1][k]:
                _floor(r, k, VOID, HALF, VOID, HALF, y)
            # the edge reaches out to the turning stairs' arc where they land
            for l0, l1 in strips(-2.75, -0.5, 0.75) + strips(0.5, 2.75, 0.75):
                near = min(abs(l0), abs(l1)) if l0 * l1 > 0 else 0.0
                z = math.sqrt(R_OUT ** 2 - near ** 2)
                if VOID - z > 0.05:
                    _floor(r, k, l0, l1, z, VOID, y, dy=-0.004)
            for l0, l1 in ((-(BARRIER_FROM if not corners[s - 1][(k + 3) % 4] else VOID), -3.0),
                           (3.0, BARRIER_FROM if not corners[s - 1][k] else VOID)):
                box(r, RAIL, side_point(k, (l0 + l1) / 2, y + 0.85, VOID + 0.35), (0.6, 1.7, l1 - l0), yaw=(90 + 90 * k) % 360)
            if not corners[s - 1][k]:
                # a fallen corner: iron bars from the floor to the balcony above, along both balconies' ends and 6 m on along the void's
                # edge, so the two sides are 8.5 m apart through the air (no jump crosses that) and the hole below can't be reached
                top = (STOREYS[s + 1] - 1.0) if s < 3 else TOP - 1.0
                grate_wall(r, side_point(k, VOID, 0, BARRIER_FROM), side_point(k, VOID, 0, WALL_IN), y, top)
                grate_wall(r, side_point((k + 1) % 4, -VOID, 0, BARRIER_FROM), side_point((k + 1) % 4, -VOID, 0, WALL_IN), y, top)
    # the machine: pillars at the void's corners, the axle, wall gears between the doorways, chains, blue light
    for cx, cz in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
        for y in np.arange(BOTTOM, TOP - 1, 8.0):
            r.stand(PILLAR, (cx * (VOID + 1.4), y + (1.0 if y == BOTTOM else 0.0), cz * (VOID + 1.4)), scale=(0.6, 1.0, 0.6))
        hang_chain(r, (cx * 7.0, TOP - 1.0, cz * 7.0), 7.0)
    axle(r, (0, STOREYS[0], 0), TOP - 1.0 - STOREYS[0], 0.8)
    wheels = machine(r, "wallgears")
    for s, y in enumerate(STOREYS[:3]):
        for k in range(4):
            gear(wheels, side_point(k, 0.0, y + 6.5, WALL_IN - 0.3), 6.5, axis="z", yaw=90 * k, thickness=0.6,
                 turn=14 if (s + k) % 2 else -14)
            bar(r, side_point(k, 0.0, y + 6.5, WALL_IN + 0.2), side_point(k, 0.0, y + 6.5, WALL_IN - 1.0), 1.0)
    for s, y in enumerate(STOREYS):
        for k in range(4):
            torch(r, side_point(k, 3.6 * (1 if (s + k) % 2 else -1), y, VOID + 0.9))
    for x, z in ((5.5, 5.5), (-5.5, -5.5), (5.5, -5.5), (-5.5, 5.5)):
        brazier(r, (x, TOP - 1.0 - 2.5 - 1.6, z), chain=2.5)
    for k in range(4):
        crystal(r, side_point(k, 9.0, STOREYS[0], 13.5))
    spawner(r, "melee", side_point(1, 0.0, STOREYS[0], 12.5))
    spawner(r, "melee", side_point(3, 0.0, STOREYS[0], 12.5))
    for s, y in enumerate(STOREYS[1:], 1):
        for k in range(4):
            if corners[s - 1][k]:
                wisp(r, side_point(k, 12.5, y + 4.0, 12.5))

    for s in range(3):
        flight(r, s, start[s])
    taken = collections.Counter()
    for i, (s, k, wires) in enumerate(levers):
        n = taken[(s, k)]
        taken[(s, k)] += 1
        # on the far side of the middle from the storey's doorway, pulled along the balcony (not towards the void: a player stands
        # with the open floor of the balcony round them, never with their back to a rail), 2 m from a fallen corner's bars; a second
        # one faces the first across a shared 3.2 m of floor (lever_check.py: 2 m clear in front, in sight from where a player stands)
        out = -np.sign(door_lateral(s))
        lat = out * (7.0 if n == 0 else 2.0)
        pull = (side_point(k, -out if n == 0 else out, 0, 0) - side_point(k, 0, 0, 0))     # where the player stands, from it
        yaw = math.degrees(math.atan2(-pull[0], -pull[2])) % 360
        gate = any(t == "vaultgate" for t, _ in wires)
        giant_lever(r, "vaultlever" if gate else f"lever{i + 1}", side_point(k, lat, STOREYS[s], 13.0), yaw, wires,
                    reached_from=door_id(s, k))
    # the vault's gate: a studded iron leaf in the top storey's north doorway, sliding sideways into the wall
    g = r.mech("vaultgate", "gate", side_point(0, door_lateral(3), STOREYS[3], HALF - 0.5))
    g.fill(DOOR, (0, DOOR_H / 2, 0), (DOOR_W + 0.2, DOOR_H + 0.1, 0.8))
    g.pose().pose(at=(5.6, 0, 0))
    g.doorway = door_id(3, 0)
    g.seconds = 4.0
    return r


def hall_drops(falls):
    """The hall's "drops": into the void down to the ground floor from anywhere, and the falls onto a lower balcony that the harness's
    gap check finds in every state of the stairs ("s2e>s1e,s1n": from s2e to each). A fall that only some states of the stairs allow
    is left out: the solver must never count on a way down that may not be there. (The player can still take it; the puzzles are
    searched to need as many uses even if every fall down were open, so it skips nothing.)"""
    out = [{"a": door_id(s, k), "b": door_id(0, j)} for s in range(1, 4) for k in range(4) for j in range(4)]
    for item in falls.split():
        a, bs = item.split(">")
        out += [{"a": a, "b": b} for b in bs.split(",")]
    return out


def _floor(room, k, l0, l1, d0, d1, top, dy=0.0):
    """A floor on side k's frame: lateral l0..l1, depth d0..d1 from the middle, its top at `top`."""
    p0, p1 = side_point(k, l0, 0, d0), side_point(k, l1, 0, d1)
    lo, hi = np.minimum(p0, p1), np.maximum(p0, p1)
    slab(room, lo[0], hi[0], lo[2], hi[2], top + dy, STONE, thickness=1.0, cell=10.0)


# The three halls: corners per storey 1..3 (ne, se, sw, nw), levers (storey, side, wires), the stairs' start, the falls that hold in
# every state (hall_drops; from the gap check: rerun it when the hall's shape changes). Found by a search over random halls with
# solve() below: each needs 11 or 12 uses even if a player could drop from anywhere to anywhere lower, on three or four storeys, no
# lever pulled more than three times running.
HALLS = {
    "hall_a": ([[0, 0, 0, 1], [0, 1, 0, 0], [0, 1, 0, 0]],
               [(0, 2, [("stair1", "next")]), (1, 2, [("stair2", "next")]), (2, 3, [("stair3", "next")]),
                (2, 3, [("vaultgate", "set:1")])], (1, 0, 3),
               "s2e>s1e,s1n,s1s,s1w s2n>s1n,s1w s2s>s1e,s1n,s1s,s1w s2w>s1n,s1w s3e>s1e,s1n,s1s,s1w,s2e,s2s s3n>s1n,s1w,s2n "
               "s3s>s1e,s1n,s1s,s1w,s2e,s2s s3w>s1n,s1w,s2w"),
    "hall_b": ([[0, 1, 1, 0], [1, 0, 0, 0], [0, 1, 1, 0]],
               [(0, 2, [("stair1", "next")]), (1, 1, [("stair2", "next")]), (2, 3, [("stair3", "next")]),
                (3, 2, [("vaultgate", "set:1")])], (3, 3, 3),
               "s2e>s1e,s1n,s1s,s1w s2n>s1e,s1n,s1s,s1w s2s>s1e,s1s,s1w s2w>s1e,s1s,s1w s3e>s1e,s1n,s1s,s1w,s2e,s2n,s2s,s2w "
               "s3n>s1e,s1n,s1s,s1w,s2e,s2n s3s>s1e,s1n,s1s,s1w,s2e,s2n,s2s,s2w s3w>s1e,s1n,s1s,s1w,s2e,s2n,s2s,s2w"),
    "hall_c": ([[0, 1, 0, 1], [0, 0, 0, 0], [0, 0, 0, 0]],
               [(0, 2, [("stair1", "next")]), (1, 3, [("stair2", "next")]), (2, 0, [("stair3", "next")]),
                (3, 2, [("vaultgate", "set:1")])], (0, 0, 3),
               "s2e>s1e,s1s s2n>s1n,s1w s2s>s1e,s1s s2w>s1n,s1w s3e>s1e,s1s,s2e s3n>s1n,s1w,s2n s3s>s1e,s1s,s2s s3w>s1n,s1w,s2w"),
}


def build():
    pack = Pack("shiftinghalls", "$dungeons_pack_shiftinghalls", environment="Caves", minRooms=16, maxRooms=28,
                maxSize=[320, 140, 320], minUses=8, entranceLook="giant", doors={}, doorChance=0.0, parkour=False,
                creatures=CREATURES, loot=LOOT, music="forestcrypt",
                shortcut={"near": "shortcut_near", "far": "shortcut_far", "farStep": "guardian1"})
    for f in (entrance, corridor, gallery, bend, tee, crossing, stair_room, gearshaft, chamber, great_hall, engine_room, dead_end,
              chasm, archive, forge, collapsed, crusher, arena, spin_bridge, lever_alcove, plate_room, drop_shaft, treasury,
              ledge_gallery, lever_vault, treasury_ambush, guardian_room,
              nook_bones, nook_shrine, nook_tablet, nook_chest, nook_rubble, cap_gate, cap_rubble,
              lever_room, gate_room, rotunda):
        pack.room(f())
    halls = {}
    for name, (corners, levers, start, falls) in HALLS.items():
        halls[name] = pack.room(hall(name, corners, levers, start, falls))
    pack.room(warden_hall())
    pack.room(secret_cache())
    pack.room(secret_shrine())
    pack.room(shortcut_door("shortcut_near", False))
    pack.room(shortcut_door("shortcut_far", True))
    pack.room(vault())
    pack.room(vault_cap())
    pack.room(warden_cap())
    pack.step("entrance", "start")
    pack.step("fight", "fight1")
    pack.step("hub", "hub1")
    pack.step("lever", "lever1", from_="hub1")                  # a side wing: the key to the gate
    pack.step("gate", "gate1", opens_with="lever1", from_="hub1")
    pack.step("timing", "timing1")
    pack.step("drop", "drop1")
    pack.step("plate", "plate1")
    pack.step("rotator", "rotunda")            # (four ways out: the big hall finds room off it more easily)
    pack.step("guardian", "guardian1")        # the fight before the hall (a plain room or two between the encounters)
    pack.step("hall", "hall")
    pack.step("boss", "warden")
    pack.step("end", "vault")
    return pack, halls


# ---------------------------------------------------------------- the halls' puzzles, checked here

def solve(room, entry, falls="declared"):
    """The fewest lever uses from `entry` (a doorway id) to the vault doorway with its gate open, with the room's own doorways as places
    (inside joins, and the stairs' joins in their states), as the layout's solver counts them: after a use the player stands where the
    lever is, and goes on from there (a stair that turned away may leave them stranded). Returns (uses as lever ids, states seen) or
    (None, seen)."""
    mechs = {m.id: m for m in room.mechs}
    moving = [m for m in room.mechs if m.kind in ("stair", "rotator")]
    order = [m.id for m in moving] + ["vaultgate"]
    levers = [m for m in room.mechs if m.kind == "lever"]

    stairs_ = [m for m in moving if m.kind == "stair"]

    def reach(state, here):
        """Where the player can get from `here` (doorways). falls "declared": the room's drops too; "free": any doorway lower down."""
        adj = collections.defaultdict(set)
        for j in room.inside:
            adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for i, m in enumerate(moving):
            for j in m.joins:
                if j["state"] in (-1, state[i]):
                    adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for j in room.extra.get("drops", []) if falls == "declared" else []:
            adj[j["a"]].add(j["b"])
        if falls == "free":
            for d in room.doorways:
                for e in room.doorways:
                    if int(e["id"][1]) < int(d["id"][1]):
                        adj[d["id"]].add(e["id"])
        seen, todo = {here}, [here]
        while todo:
            x = todo.pop()
            for y in adj[x] - seen:
                seen.add(y); todo.append(y)
        return seen
    start = (tuple(m.start for m in moving) + (0,), entry)
    prev = {start: None}
    q = collections.deque([start])
    vault_door = mechs["vaultgate"].doorway
    while q:
        st, at = q.popleft()
        here = reach(st, at)
        if st[-1] == 1 and vault_door in here:
            path, x = [], (st, at)
            while prev[x]:
                path.append(prev[x][1]); x = prev[x][0]
            return list(reversed(path)), len(prev)
        for lv in levers:
            if lv.reached_from not in here:
                continue
            ns = list(st)
            for w in lv.wires:
                i = order.index(w["target"])
                n = len(mechs[w["target"]].poses)
                ns[i] = (ns[i] + 1) % n if w["do"] == "next" else int(w["do"].split(":")[1])
            key = (tuple(ns), lv.reached_from)
            if key not in prev:
                prev[key] = ((st, at), lv.id)
                q.append(key)
    return None, len(prev)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", help="folder for pictures of every room")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args()
    pack, halls = build()
    pack.write(args.out)
    print("wrote", args.out)
    for r in pack.rooms:
        if isinstance(r, Room):
            print(f"  {r.name}: {len(r.parts)} parts, {len(r.mechs)} mechanisms ({sum(len(m.parts) for m in r.mechs)} moving parts), "
                  f"{len(r.doorways)} doorways")
    bad = False
    for name, r in halls.items():
        path, seen = solve(r, door_id(0, 0))
        free, _ = solve(r, door_id(0, 0), falls="free")
        plain, _ = solve(r, door_id(0, 0), falls=None)
        if path is None or plain is None:
            print(f"  {name}: NO SOLUTION{'' if path is None else ' WITHOUT FALLS'} ({seen} states)"); bad = True
        else:
            print(f"  {name}: {len(path)} uses with the declared falls ({len(free)} if every fall down were open, {len(plain)} with "
                  f"none): {' '.join(path)}")
            if len(free) < 8:
                print(f"  {name}: FEWER THAN 8 USES"); bad = True
    if args.preview:
        import preview_rooms
        preview_rooms.giant(pack, halls, args.preview, solve)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
