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
from dungeon_parts import Room, Pack, CREATURES, LOOT, yaw_turn, side_point, wall, slab, box, bounds  # noqa: E402
from giant_kit import (Shell, STONE, DARK, PILLAR, COLUMN, HEAD, GEAR, AXLE, GRATE, CHAINS, DOOR, RAIL, CRYSTAL, TYPE,  # noqa: E402
                       DOOR_W, DOOR_H, WALL, SIDES, facing, door_frame, torch, wisp, brazier, crystal, hang_chain, gear, axle, girder,
                       spawner, stairs, giant_lever, machine)

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "mods", "Dungeons", "packs", "shiftinghalls.json")
VAULT = "quaddungeon_vault"     # the vault's doorway type: only the hall's top storey has one


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
    box(r, AXLE, (-3.5, -0.5, 3.5), (0.9, 0.9, 2.0), yaw=45, tilt=(90, 0))
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
    s = Shell("crossing", (16, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5), ("e", 1, 0, -5), ("w", 3, 0, -5)], weight=1.2)
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
    torch(r, (2.0, -11, -14.0))
    torch(r, (-2.0, 1, 14.0))
    wisp(r, (0, 9.5, 14))
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
    torch(r, (-1.5, -11, -6))
    torch(r, (-5.5, -5, 5.5))
    torch(r, (5.5, 1, -1.5))
    wisp(r, (0, 9, 0))
    spawner(r, "flying", (0, -11, -5))
    return r


def chamber():
    s = Shell("chamber", (16, 12, 16), [("s", 2, 0, -5), ("n", 0, 0, -5), ("e", 1, 0, -5)], weight=1.5)
    r = s.r
    box(r, "giant_skull", (-3.6, -2.2, 2.0), (4.6, 5.4, 7.6), yaw=200)
    r.stand("skull_pile", (4.5, -5, 5))
    r.stand("bone_stack", (-5.5, -5, -5), yaw=40)
    r.stand("bone_stack", (5.0, -5, -4.5), yaw=200)
    brazier(r, (2.5, -5, -2.5))
    for x, z in ((5.6, -5.6), (-5.6, 5.6)):
        torch(r, (x, -5, z))
    crystal(r, (-6.0, -5, 6.0))
    wisp(r, (0, 3.5, 0))
    spawner(r, "melee", (3, -5, 3))
    spawner(r, "melee", (-2, -5, -4))
    return r


def great_hall():
    """Two storeys: a giant's ribs on the floor, a balcony along the north wall reached by stairs up the west wall."""
    s = Shell("greathall", (24, 24, 24), [("s", 2, 0, -11), ("e", 1, 0, -11), ("n", 0, 0, 1)], weight=0.8, max=1, min_depth=2)
    r = s.r
    slab(r, -11, 11, 6, 11, 1.0, STONE, thickness=1.0, cell=10.0)                       # the balcony
    for x in (-6.0, 0.0, 6.0):
        r.stand(PILLAR, (x, -11, 8.5), scale=(0.55, 1.5, 0.55))
    stairs(r, (-9, -11, -10), 12, 16, 4.0, fill_below=-11)                             # up the west wall to the balcony
    box(r, RAIL, (2.2, 1.0 + 0.65, 6.3), (0.6, 1.3, 17.6), yaw=90)             # (the stair comes up at its west end)
    box(r, "giant_ribs", (2.0, -8.0, -1.5), (12.6, 8.8, 14.2), yaw=15)
    box(r, "giant_skull", (6.5, -8.6, -8.5), (4.0, 4.8, 6.6), yaw=160)
    lo, hi = bounds("giant_sword1")
    box(r, "giant_sword1", (8.8, -4.0, -8.0), (hi - lo) * 0.7, yaw=30, tilt=(6, -8))
    for x, z in ((-9.5, -9.5), (9.5, -9.5), (9.5, 3.5)):
        torch(r, (x, -11, z))
    torch(r, (8.5, 1.0, 9.6))
    torch(r, (-3.0, 1.0, 9.6))
    brazier(r, (0, 4.0, -2.0), chain=6.0)
    wisp(r, (-9, -1, -1))
    wisp(r, (6, -2, -6))
    spawner(r, "melee", (-2, -11, -6))
    spawner(r, "melee", (5, -11, 3))
    spawner(r, "ranged", (3, 1, 9))
    spawner(r, "flying", (0, -11, 6))
    return r


def engine_room():
    """The machine's heart: two great wall gears turning all the time, smaller ones meshing above them, chains, girders."""
    s = Shell("engine", (16, 24, 16), [("s", 2, 0, -11), ("n", 0, 0, -11)], weight=0.8, max=1, min_depth=2)
    r = s.r
    m = machine(r, "engine")
    for x, sign in ((6.6, 1), (-6.6, -1)):
        gear(m, (x, -1.0, 0), 12.0, axis="z", yaw=90, thickness=0.9, turn=12 * sign)
        box(r, AXLE, (x, -1.0, 0), (2.4, 1.2, 1.2))
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


def cap_gate():
    """End cap: a sealed iron gate leaf in the doorway."""
    r = Room("cap_gate", (8, 12, 0), end_cap=True, weight=1.0)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=TYPE)
    r.fill(DOOR, (0, -5 + DOOR_H / 2, 0.45), (DOOR_W, DOOR_H, 0.8))
    lo, hi = bounds(CHAINS)
    box(r, CHAINS, (0, 0.8, 0.05), (DOOR_W + 0.8, 3.0, 0.5))
    return r


def cap_rubble():
    """End cap: a fallen-in passage."""
    r = Room("cap_rubble", (8, 12, 0), end_cap=True, weight=0.6)
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
        d.add("dverger_demister", yaw_turn((0, 6.8, R - 1.0), a))
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


def vault():
    s = Shell("vault", (16, 12, 16), [("in", 2, 0, -5)], tags=["end"], max=1, door_type=VAULT)
    r = s.r
    r.doorways[0]["door"] = False
    r.fill(DARK, (0, -4.5, 4.0), (8.0, 1.0, 6.0))                                   # the dais
    r.stand("Morkhalla_ChestAncient", (0, -4.0, 5.2), yaw=180)                     # the giants' chest: scenery
    r.stand("TreasureChest_trollcave", (0, -4.0, 2.4), yaw=180, saved=True)         # the treasure that opens
    for x in (-5.5, 5.5):
        r.stand(PILLAR, (x, -5, 5.5), scale=(0.4, 0.4, 0.4))
        crystal(r, (x, -5 + 3.2, 5.5))
    lo, hi = bounds("giant_sword2")
    for x, t in ((-3.5, 18), (3.5, -18)):
        box(r, "giant_sword2", (x, 0.0, 6.6), (hi - lo) * 0.62, yaw=0, tilt=(0, t))
    box(r, "giant_helmet1", (0, 1.8, 5.0), (3.4, 3.2, 3.9), yaw=180)
    for x, z in ((-6.0, -6.0), (6.0, -6.0)):
        torch(r, (x, -5, z))
    brazier(r, (0, 0.5, 0.0), chain=3.0)
    wisp(r, (0, 3.6, -4.0))
    spawner(r, "melee", (-3.5, -5, 0.0))           # the hoard's keepers
    spawner(r, "ranged", (3.5, -5, 0.0))
    return r


def vault_cap():
    """Closes the vault doorway if the layout ever leaves it open (in a good layout the vault always takes it)."""
    r = Room("vaultcap", (8, 12, 0), end_cap=True, weight=0.01)
    r.doorway("in", (0, -5, 0), (0, 0, 1), type=VAULT, door=False)
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
    for x in (a - 0.15, b + 0.15):
        girder(m, (x, -5.7, -FLIGHT_RUN + 0.3), (x, 5.2, FLIGHT_RUN - 0.3), 0.45)
    gear(m, (0, -1.8, 0), 6.0, thickness=0.6, spin=15 * s, turn=45)          # (spins while the stair turns)
    girder(m, (0, -1.8, 0), ((a + b) / 2, -6.5 + 2.0, -6.0), 0.4)
    girder(m, (0, -1.8, 0), ((a + b) / 2, 3.0, 6.0), 0.4)
    for k in range(4):
        m.pose(euler=(0, (180 + 90 * k) % 360, 0))
    m.start = start
    m.seconds = 4.0
    for k in range(4):
        m.join(door_id(s, k), door_id(s + 1, (k + 2) % 4), state=k)
    return m


def hall(name, corners, levers, start, weight=1.0):
    """One Shifting Hall. corners[s - 1][c]: on storey s, corner c (0 ne, 1 se, 2 sw, 3 nw) joins side c to side c + 1. levers: (storey,
    side, wires) with wires as (mechanism id, do), the gate's lever last. start: the three stairs' first states."""
    r = Room(name, (2 * HALF, TOP - BOTTOM, 2 * HALF), tags=["hall"], weight=weight, max=1)
    r.extra["joinBy"] = [door_id(0, k) for k in range(4)]     # always entered from the ground floor
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
            for l0, l1 in ((-VOID, -3.0), (3.0, VOID)):
                box(r, RAIL, side_point(k, (l0 + l1) / 2, y + 0.65, VOID + 0.35), (0.6, 1.3, l1 - l0), yaw=(90 + 90 * k) % 360)
            if not corners[s - 1][k]:
                box(r, RAIL, side_point(k, VOID - 0.35, y + 0.65, (VOID + WALL_IN) / 2), (0.6, 1.3, WALL_IN - VOID), yaw=(90 * k) % 360)
                box(r, RAIL, side_point((k + 1) % 4, -VOID + 0.35, y + 0.65, (VOID + WALL_IN) / 2), (0.6, 1.3, WALL_IN - VOID),
                    yaw=(90 * (k + 1)) % 360)
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
            box(r, AXLE, side_point(k, 0.0, y + 6.5, WALL_IN - 0.4), (1.0, 1.0, 1.2), yaw=90 * k, tilt=(90, 0))
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
        lat = -np.sign(door_lateral(s)) * (7.5 - 2.4 * n)
        gate = any(t == "vaultgate" for t, _ in wires)
        giant_lever(r, "vaultlever" if gate else f"lever{i + 1}", side_point(k, lat, STOREYS[s], 13.2), (90 * k) % 360, wires,
                    reached_from=door_id(s, k))
    # the vault's gate: a studded iron leaf in the top storey's north doorway, sliding sideways into the wall
    g = r.mech("vaultgate", "gate", side_point(0, door_lateral(3), STOREYS[3], HALF - 0.5))
    g.fill(DOOR, (0, DOOR_H / 2, 0), (DOOR_W + 0.2, DOOR_H + 0.1, 0.8))
    g.pose().pose(at=(5.6, 0, 0))
    g.doorway = door_id(3, 0)
    g.seconds = 4.0
    return r


def _floor(room, k, l0, l1, d0, d1, top, dy=0.0):
    """A floor on side k's frame: lateral l0..l1, depth d0..d1 from the middle, its top at `top`."""
    p0, p1 = side_point(k, l0, 0, d0), side_point(k, l1, 0, d1)
    lo, hi = np.minimum(p0, p1), np.maximum(p0, p1)
    slab(room, lo[0], hi[0], lo[2], hi[2], top + dy, STONE, thickness=1.0, cell=10.0)


# The three halls: corners per storey 1..3 (ne, se, sw, nw), levers (storey, side, wires), the stairs' start. Found by a search over
# random halls with solve() below: each needs 8 to 11 uses, on three or four storeys, no lever pulled more than three times running.
HALLS = {
    "hall_a": ([[1, 0, 0, 1], [1, 0, 0, 1], [0, 1, 1, 0]],
               [(0, 3, [("stair1", "next")]), (1, 1, [("stair2", "next")]), (2, 1, [("stair3", "next"), ("stair2", "next")]),
                (3, 3, [("vaultgate", "set:1")])], (1, 1, 3)),
    "hall_b": ([[1, 1, 0, 0], [0, 0, 0, 0], [0, 1, 1, 0]],
               [(0, 0, [("stair1", "next")]), (1, 0, [("stair2", "next")]), (2, 1, [("stair3", "next")]),
                (3, 2, [("vaultgate", "set:1")])], (3, 2, 0)),
    "hall_c": ([[0, 1, 0, 1], [1, 0, 0, 1], [0, 1, 0, 0]],
               [(0, 3, [("stair1", "next")]), (1, 2, [("stair2", "next")]), (2, 3, [("stair3", "next"), ("stair2", "next")]),
                (3, 1, [("vaultgate", "set:1")])], (2, 3, 3)),
}


def build():
    pack = Pack("shiftinghalls", "$dungeons_pack_shiftinghalls", environment="Caves", minRooms=12, maxRooms=20,
                maxSize=[240, 120, 240], minUses=8, entranceLook="giant", doors={}, doorChance=0.0,
                creatures=CREATURES, loot=LOOT)
    for f in (entrance, corridor, gallery, bend, tee, crossing, stair_room, gearshaft, chamber, great_hall, engine_room, dead_end,
              cap_gate, cap_rubble, lever_room, gate_room, rotunda):
        pack.room(f())
    halls = {}
    for name, (corners, levers, start) in HALLS.items():
        halls[name] = pack.room(hall(name, corners, levers, start))
    pack.room(vault())
    pack.room(vault_cap())
    pack.step("entrance", "start")
    pack.step("lever", "lever1")
    pack.step("gate", "gate1", opens_with="lever1")
    pack.step("rotator", "rotunda")
    pack.step("hall", "hall")
    pack.step("end", "vault")
    return pack, halls


# ---------------------------------------------------------------- the halls' puzzles, checked here

def solve(room, entry):
    """The fewest lever uses from `entry` (a doorway id) to the vault doorway with its gate open, with the room's own doorways as places
    (inside joins, and the stairs' joins in their states), as the layout's solver counts them: after a use the player stands where the
    lever is, and goes on from there (a stair that turned away may leave them stranded). Returns (uses as lever ids, states seen) or
    (None, seen)."""
    mechs = {m.id: m for m in room.mechs}
    moving = [m for m in room.mechs if m.kind in ("stair", "rotator")]
    order = [m.id for m in moving] + ["vaultgate"]
    levers = [m for m in room.mechs if m.kind == "lever"]

    def reach(state, here):
        adj = collections.defaultdict(set)
        for j in room.inside:
            adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for i, m in enumerate(moving):
            for j in m.joins:
                if j["state"] in (-1, state[i]):
                    adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
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
        if path is None:
            print(f"  {name}: NO SOLUTION ({seen} states)"); bad = True
        else:
            print(f"  {name}: {len(path)} uses ({seen} states): {' '.join(path)}")
    if args.preview:
        import preview_rooms
        preview_rooms.giant(pack, halls, args.preview, solve)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
