"""
The Starfall Orrery's hand-made rooms (pack "starfall", theme authoring/themes/orrery.py): the rooms the recipes can't make, because
they are the dungeon's identity or a puzzle of their own.

  entrance      the Crater Stair: a round hall under a tumbling armillary, the first shard of the fallen star in its middle
  vault         the Star-Heart: the fallen star's core glowing behind the watchers' great chest, the way out through a moon-door
  orrery_a/b    the Great Orrery: cranks turn three planets' rings; when the planets stand in a line towards the far door (a
                conjunction) the moon-bridge rises across the star-well
  chart_a/b/c   the Star-Chart Halls: walk a constellation laid in the floor, star by star in its order, under falling starlight
  cap_seal, cap_fallen, vault_cap, secret_lens, shortcut_near/far
The boss rooms are in bosses_starfall.py.
"""
import math

import numpy as np

import giant_kit as gk

from dungeon_parts import Room, box, bounds, yaw_turn, r3
from authoring.core import rng_for, side_xz, FREE, WAY, TAKEN, OUT
from authoring.shells import Shell
from authoring.themes.orrery import (THEME, ASHLAR, MARBLE, BAND, PARAPET, COLUMN, STEP, GRATE, LATTICE, CHEST, TORCH, STAR, SPARK,
                                     CRYSTAL, RING, DISC, TABLET, METEOR, SHARD, STANDSTONE, CHAIN, CLOTH, BAR, SUN, TYPE,
                                     ring, orbit, spoke, hang_star, crystal_plinth, meteor_heap, star_crank)

T = THEME
HEART = "starfall_heart"          # the Sun-Eater's hall to the vault: only those two have one


def shell(name, size, doors, shape="rect", **kw):
    """A plain room of the theme with doorways [(id, side, lateral, level y or None for the floor)], built (walls, floor, ceiling,
    frames). Its floor plan marks where things may go."""
    sh = Shell(T, name, shape, size, rng_for(name), **kw)
    for id, k, lat, y in doors:
        d = sh.add_door(id, k, lat, 0)
        if y is not None:
            d.y = y
    sh.build()
    return sh


def put(sh, size, fn, count=1, margin=0.4, wall=None, level=0, rng=None, gap=0.0):
    """Puts `count` things of footprint `size` on free floor (the plan's FREE cells), calling fn(x, z) for each; returns how many."""
    rng = rng or sh.rng
    n = 0
    for x, z in sh.plan.spots(level, size, rng, margin=margin, wall=wall, count=count * 4, gap=gap):
        if n >= count:
            break
        if sh.plan.take(level, (x, 0, z), size, margin=0.2):
            fn(x, z)
            n += 1
    return n


def keep_clear(sh, ids=None):
    """Reserves the ways between the doorways (3 m wide), before anything that only looks goes down."""
    return sh.connect(ids)


# ---------------------------------------------------------------- the way in and the way out

def entrance():
    """The Crater Stair, where players arrive: a round hall 24 m across, 18 m to its vault, under a tumbling armillary of three
    silver hoops round a glowing shard of the fallen star; pale columns between the four ways, the first star-crystals."""
    sh = shell("entrance", (24, 18, 24), [("in", 2, 0.0, None), ("n", 0, 0.0, None), ("e", 1, 0.0, None), ("w", 3, 0.0, None)],
               shape="round", tags=["entrance"])
    r = sh.room
    r.entrance = True
    r.doorways[0]["entrance"] = True
    keep_clear(sh)
    F = sh.floor_y
    # four pale columns between the ways, a hanging star from each... and torches at their feet
    for i in range(4):
        a = math.radians(45 + 90 * i)
        x, z = math.sin(a) * 7.0, math.cos(a) * 7.0
        sh.plan.take(0, (x, 0, z), (2.2, 2.2), force=True)
        T.pillar(r, (x, F, z), sh.top - T.floor_t - F, 1.4)
        tx, tz = math.sin(a) * 5.3, math.cos(a) * 5.3
        if sh.plan.take(0, (tx, 0, tz), (1.0, 1.0)):
            T.light_stand(r, (tx, F, tz))
    # the armillary: three hoops tumbling about their diameters round the star-shard, high over the crossing (out of reach)
    m = T.machine(r, "armillary")
    cy = F + 10.5
    ring(m, (0, cy, 0), 7.5, yaw=0, turn=9)
    ring(m, (0, cy, 0), 6.2, yaw=60, turn=-13)
    ring(m, (0, cy, 0), 5.0, yaw=120, turn=17)
    box(r, BAR, (0, (cy + sh.top - T.floor_t) / 2, 0), (0.3, sh.top - T.floor_t - cy, 0.3))      # its pivot, from the vault
    r.stand(SHARD, (0, cy - 0.6, 0), scale=(2.2, 2.2, 2.2))
    r.add(SPARK, (0, cy, 0))
    # crystals and a star tablet in the quadrants, by the wall
    put(sh, (1.4, 1.4), lambda x, z: crystal_plinth(r, (x, F, z)), count=3, wall=True, gap=6.0)
    put(sh, (1.6, 1.0), lambda x, z: r.stand(TABLET, (x, F, z), yaw=_face_in(x, z)), count=2, wall=True, gap=8.0)
    for x, z in ((-7.5, -7.5), (7.5, 7.5)):
        hang_star(r, (x, F + 8.0, z))
    return r


def _face_in(x, z):
    """A yaw that turns a prop's front (-z) towards the room's middle."""
    return math.degrees(math.atan2(x, z)) % 360


def vault(door_type=HEART):
    """The Star-Heart, the end: the core of the fallen star standing in a ring of crystals, glowing; before it the watchers' great
    chest on a black dais; behind it the way out, a moon-door in the far wall (the exit, as the entrance room's Leave)."""
    sh = shell("vault", (24, 16, 24), [("in", 2, 0.0, None)], tags=["end"], max=1)
    r = sh.room
    r.doorways[0]["type"] = door_type
    r.doorways[0]["door"] = False
    F = sh.floor_y
    # the dais and the chest
    for i, (w, d) in enumerate(((8.0, 6.0), (7.0, 5.0))):
        box(r, BAND, (0, F + 0.2 * (i + 1) / 2, 0.5), (w, 0.2 * (i + 1), d))
    r.stand("Morkhalla_ChestAncient", (0, F + 0.4, 0.8), yaw=180, saved=True)
    sh.plan.take(0, (0, 0, 0.5), (8.0, 6.0), force=True)
    # the core: a pillar of meteorite rock, sunk in the floor, in a ring of crystals, under hanging stars
    lo, hi = bounds(METEOR)
    box(r, METEOR, (-6.5, F + 3.2, 6.5), (2.4, 7.0, 2.6), yaw=25)
    box(r, METEOR, (6.8, F + 2.4, 6.0), (1.8, 5.4, 2.0), yaw=-40, tilt=(8, 6))
    for i in range(6):
        a = math.radians(60 * i)
        r.stand(CRYSTAL, (-6.5 + math.sin(a) * 2.2, F, 6.5 + math.cos(a) * 2.2), yaw=60 * i, scale=(1.4, 1.4, 1.4))
    sh.plan.take(0, (-6.5, 0, 6.5), (6.0, 6.0), force=True)
    sh.plan.take(0, (6.8, 0, 6.0), (4.0, 4.0), force=True)
    # the way out: a moon-door in the north wall (a dark stone disc in a standing silver ring), its approach kept clear
    ez = sh.half[2] - T.wall_t - 0.5
    out = r.mech("exit", "exit", (0, F, ez), (0, 180, 0))
    ring(out, (0, 3.2, 0.1), 5.6, axis="z", yaw=0)
    box(out, DISC, (0, 3.2, 0.3), (4.6, 4.6, 0.5))
    box(out, BAND, (0, 0.2, 0.0), (5.0, 0.4, 1.4))
    sh.plan.take(0, (0, 0, ez - 1.0), (5.0, 2.0), force=True)
    sh.plan.reserve(0, -1.5, 1.5, ez - 5.0, ez - 1.5)
    for x in (-4.5, 4.5):
        T.light_stand(r, (x, F, ez - 1.2))
        sh.plan.take(0, (x, 0, ez - 1.2), (1.0, 1.0), force=True)
    hang_star(r, (0, F + 9.0, 0.5), scale=0.8)
    for x, z in ((-6.5, -6.0), (6.5, -6.0)):
        hang_star(r, (x, F + 8.0, z))
    put(sh, (1.6, 1.0), lambda x, z: r.stand(TABLET, (x, F, z), yaw=_face_in(x, z)), count=2, wall=True, gap=6.0)
    put(sh, (2.4, 2.4), lambda x, z: meteor_heap(r, (x, F, z)), count=2, wall=True, gap=5.0)
    T.spawner(r, "melee", (-5.0, F, -3.0))          # the heart's keepers
    T.spawner(r, "ranged", (5.0, F, -3.0))
    return r


# ---------------------------------------------------------------- end caps, the secret, the shortcut

CAP_SIZE = (T.door_w + 2.0, 12.0, 0.0)
CAP_Y = -5.0


def cap_seal():
    """End cap: the doorway walled up with pale stone, a black marble disc in it with a star."""
    r = Room("cap_seal", CAP_SIZE, end_cap=True, weight=0.4)
    r.doorway("in", (0, CAP_Y, 0), (0, 0, 1), type=TYPE)
    box(r, ASHLAR, (0, CAP_Y + T.door_h / 2, 0.45), (T.door_w + 0.4, T.door_h + 0.2, 0.8))
    box(r, DISC, (0, CAP_Y + T.door_h * 0.55, 0.0), (2.4, 2.4, 0.3))
    r.stand(STAR, (0, CAP_Y + T.door_h * 0.55 - 0.3, -0.05), scale=(0.35, 0.35, 0.35))
    return r


def cap_fallen():
    """End cap: a passage the falling star brought down: a slab of the vault across it, stones and a glowing shard."""
    r = Room("cap_fallen", CAP_SIZE, end_cap=True, weight=0.25)
    r.doorway("in", (0, CAP_Y, 0), (0, 0, 1), type=TYPE)
    box(r, ASHLAR, (0, CAP_Y + T.door_h / 2, 0.6), (T.door_w + 0.4, T.door_h + 0.2, 1.0), tilt=(6, 4))
    r.stand("stone_pile", (-1.2, CAP_Y, -0.2), scale=(1.3, 1.3, 1.3))
    r.stand("stone_pile", (1.3, CAP_Y, 0.0), yaw=120, scale=(1.1, 1.5, 1.1))
    r.stand(SHARD, (0.2, CAP_Y, -0.3), yaw=40, scale=(1.3, 1.3, 1.3))
    return r


def vault_cap():
    """Closes the Star-Heart's doorway if a layout ever leaves it open."""
    r = Room("vaultcap", CAP_SIZE, end_cap=True, weight=0.01)
    r.doorway("in", (0, CAP_Y, 0), (0, 0, 1), type=HEART, door=False)
    box(r, ASHLAR, (0, CAP_Y + T.door_h / 2, 0.45), (T.door_w + 0.4, T.door_h + 0.2, 0.8))
    return r


def shortcut_door(name, deep):
    """One end of the shortcut (kind "shortcut", pair "sc1"): a moon-door, a dark stone disc in a silver ring; the near one chained
    shut from its side. The deep one (by the Astrolabe Keeper) opens both from its side; then either takes you to the other."""
    r = Room(name, CAP_SIZE, end_cap=True, weight=0.01, max=1)
    r.doorway("in", (0, CAP_Y, 0), (0, 0, 1), type=TYPE)
    box(r, ASHLAR, (0, CAP_Y + T.door_h / 2, 0.7), (T.door_w + 0.4, T.door_h + 0.2, 0.6))
    d = r.mech("shortcut", "shortcut", (0, CAP_Y, 0), (0, 180, 0))
    d.pose().pose()
    ring(d, (0, 3.1, -0.15), 4.4, axis="z")
    box(d, DISC, (0, 3.1, -0.3), (3.6, 3.6, 0.4))
    if not deep:
        lo, hi = bounds("Morkhalla_Chain")
        box(d, "Morkhalla_Chain", (0, 3.1, 0.2), (T.door_w + 0.4, 2.2, 0.4))
    d.extra.update({"pair": "sc1", "deep": deep})
    return r


def secret_lens():
    """A secret behind cracked stone: the watchers' lens-room, a great crystal lens standing in a silver ring over a chest of the
    land's better things (the room is off the map until the wall is broken)."""
    from authoring.sidejobs import cracked
    sh = shell("secret_lens", (12, 12, 12), [("in", 2, 0.0, None)], tags=["secret"], weight=1.0, max=1)
    r = sh.room
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    b = r.mech("crack", "breakable", (0, F, -(sh.half[2] - T.wall_t / 2)), (0, 0, 0))
    b.pose().pose()
    b.doorway = "in"
    b.extra["health"] = 300
    cracked(T, b, T.door_w + 0.2, T.door_h + 0.1)
    r.extra["hiddenBy"] = "crack"
    box(r, BAND, (0, F + 0.4, 3.0), (5.0, 0.8, 2.6))
    r.stand(CHEST, (0, F + 0.8, 3.0), yaw=180, saved=True)
    ring(r, (0, F + 5.2, 4.3), 5.0, axis="z")
    r.stand(CRYSTAL, (0, F + 4.6, 4.3), scale=(1.8, 1.8, 1.8))
    for x in (-3.8, 3.8):
        crystal_plinth(r, (x, F, -2.5))
    hang_star(r, (0, F + 7.5, -1.0))
    return r


# ---------------------------------------------------------------- the Watchers' Gate (plan step "gate1")

def gatehouse():
    """The Watchers' Gate: a round hall 24 m across, shut by a lattice gate on its way in that the key lever (the step before) opens.
    Beyond it, three ways on (north, east and west), under a small armillary turning over a star-crystal; pale columns between them."""
    sh = shell("gatehouse", (24.0, 16.0, 24.0), [("in", 2, 0.0, None), ("n", 0, 0.0, None), ("e", 1, 0.0, None), ("w", 3, 0.0, None)],
               shape="round", tags=["gate"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    g = T.gate(r, "gate", 2, sh.half[2], F, 0, "in")
    g.seconds = 3.0
    keep_clear(sh)
    for i in range(4):
        a = math.radians(45 + 90 * i)
        x, z = math.sin(a) * 7.2, math.cos(a) * 7.2
        sh.plan.take(0, (x, 0, z), (2.2, 2.2), force=True)
        T.pillar(r, (x, F, z), sh.top - T.floor_t - F, 1.3)
    m = T.machine(r, "armillary")
    cy = F + 10.0
    ring(m, (0, cy, 0), 5.0, yaw=30, turn=10)
    ring(m, (0, cy, 0), 3.8, yaw=110, turn=-14)
    gk.bar(r, (0, sh.top - T.floor_t, 0), (0, cy + 0.6, 0), 0.3)
    r.stand(CRYSTAL, (0, cy - 0.7, 0), scale=(1.4, 1.4, 1.4))
    r.add(SPARK, (0, cy, 0))
    for x, z in ((-5.0, -5.0), (5.0, 5.0)):
        T.light_stand(r, (x, F, z))
    put(sh, (1.6, 1.0), lambda x, z: r.stand(TABLET, (x, F, z), yaw=_face_in(x, z)), count=2, wall=True, gap=8.0)
    return r


# ---------------------------------------------------------------- small passages (the layout's turns and climbs)

def bend():
    """A turn in the way: a narrow pale passage 8 m square under a hung star, its two doorways on neighbouring sides. Low weight: the
    layout takes it where nothing else fits."""
    sh = shell("passage_bend", (8.0, 12.0, 8.0), [("a", 2, 0.0, None), ("b", 1, 0.0, None)], weight=0.25)
    r = sh.room
    F = sh.floor_y
    hang_star(r, (-1.2, F + 7.5, 1.2), scale=0.5)
    T.light_stand(r, (-2.6, F, 2.6))
    return r


def riser():
    """A climb in the way: a passage 8 m wide and 22 m long, a flight of pale steps a storey up from one end's doorway to the
    other's. Low weight, as the bend."""
    S = (8.0, 12.0 + T.storey, 22.0)
    F = -S[1] / 2 + T.floor_t
    top = F + T.storey
    sh = shell("passage_riser", S, [("a", 2, 0.0, None), ("b", 0, 0.0, top)], weight=0.25)
    r = sh.room
    run = T.stair_run(T.storey)                                          # 16 m
    z0 = -S[2] / 2 + T.wall_t + 2.0
    T.stairs(r, (0, F, z0), T.storey, run, S[0] - 2 * T.wall_t, yaw=0, fill_below=F)
    T.block(r, -S[0] / 2, S[0] / 2, z0 + run, S[2] / 2, F - T.floor_t, top)
    hang_star(r, (0, top + 6.0, z0 + run / 2), scale=0.5)
    return r


# ---------------------------------------------------------------- the Great Orrery

def lit(part, states):
    """A mechanism's part whose light burns only in these states (the toolkit's one-model rule)."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


def ledge(r, x0, x1, z0, z1, y0, y1):
    """A gallery: pale stone from y0 up, a black marble top 0.5 m thick at y1."""
    T.block(r, x0, x1, z0, z1, y0, y1 - 0.5, prefab=ASHLAR)
    T.floor(r, x0, x1, z0, z1, y1, thickness=0.5)


# the cranks of each orrery: (id, where, yaw, the planets it turns); the planets' start states; the hall's name
ORRERIES = {
    # crank A turns the moon and the violet world, B the violet world and the pale star, C the pale star alone; the world and the
    # star start in line, and A and B knock them out: 5 pulls (A twice, B once, C twice)
    "orrery_a": ([("crankA", (-6.0, -14.5), 180, ("p1", "p2")), ("crankB", (6.0, -14.5), 180, ("p2", "p3")),
                  ("crankC", (14.5, -5.0), 90, ("p3",))], (1, 0, 0)),
    # A turns the moon alone, B the moon and the violet world, C all three: 5 pulls (A once, B twice, C twice)
    "orrery_b": ([("crankA", (-14.5, -5.0), 270, ("p1",)), ("crankB", (-6.0, -14.5), 180, ("p1", "p2")),
                  ("crankC", (6.0, -14.5), 180, ("p1", "p2", "p3"))], (1, 2, 1)),
}
PLANET_STATES = 3                      # a third of a turn a pull of a crank (more made the solver's search too big)


def orrery(name):
    """The Great Orrery: a round hall 36 m across and 36 m high over a star-well 6.5 m deep. Galleries run round the south half of the wall (the way
    in); across the well, alone in the north wall, the bay of the way on. In the dome hangs the fallen star
    in a cage of tumbling hoops, blazing white, and round it on three silver arms turn three planets (a dark moon, a violet crystal
    world, a pale star), each over its orbit's ring. Three star-cranks on the galleries turn the planets a third of a turn, each crank
    one or two or three of them, geared together. When all three stand in line towards the far door (a conjunction: each planet's
    light kindles when it's in line), the moon-bridge set in the well's floor rises to the galleries and the way on is open. Fall into
    the well and a stair in its corner takes you back up to the galleries."""
    cranks, start = ORRERIES[name]
    S = (36.0, 36.0, 36.0)
    sh = Shell(T, name, "round", S, rng_for(name), tags=["orrery"], max=1, min_depth=4)
    YL = sh.floor_y + 6.5                                                       # the galleries' floor (the doorways' thresholds)
    for id, k in (("in", 2), ("out", 0)):
        d = sh.add_door(id, k, 0.0, 0)
        d.y = YL
    sh.build()
    r = sh.room
    r.extra["joinBy"] = ["in"]
    r.extra["drops"] = [{"a": "out", "b": "in"}]                    # (off the far bay into the well: its stair is on the near side)
    F, H = sh.floor_y, 18.0
    # the galleries round the south half, the far bay; the stair up out of the well (west side, climbing south to the gallery)
    ledge(r, -H, H, -H, -11.0, F, YL)                                         # (the far bay's corners are 12.6 m from the
    ledge(r, 11.0, H, -11.0, 2.0, F, YL)                                      # side galleries': more than the best jump)
    ledge(r, -H, -11.0, -11.0, 2.0, F, YL)
    ledge(r, -4.0, 4.0, 12.5, H, F, YL)
    T.stairs(r, (-9.5, F, 2.0), YL - F, 13.0, 3.0, yaw=180, fill_below=F)
    # the well's floor: the moon-road (a pale line from the near gallery to the far one), stars of crystal round it
    box(r, ASHLAR, (0, F + 0.01, 0.75), (0.6, 0.06, 23.5))
    for x, z in ((-6.0, 5.0), (5.5, -3.5), (6.5, 7.5), (-5.0, -6.5)):
        r.stand(CRYSTAL, (x, F, z), yaw=37 * x, scale=(1.6, 1.6, 1.6))
    # the moon-bridge: set in the well's floor; the conjunction raises it to the galleries (joining the way in to the way on)
    b = r.mech("bridge", "platform", (0, YL, 0.75))
    box(b, MARBLE, (0, -0.5, 0), (3.6, 1.0, 24.5))
    for s in (-1, 1):
        box(b, ASHLAR, (s * 1.95, -0.4, 0), (0.3, 1.2, 24.5))
    for i, z in enumerate((-8.0, -2.7, 2.7, 8.0)):
        box(b, DISC, (0, 0.01, z), (1.4, 1.4, 0.02), tilt=(90, 0))
    b.pose(at=(0, F + 0.5 - YL, 0)).pose()
    b.seconds = 6.0
    b.join("in", "out", state=1)
    # the cranks
    for id, (x, z), yaw, planets in cranks:
        star_crank(r, id, (x, YL, z), yaw, [(p, "next") for p in planets], reached_from="in")
        T.light_stand(r, (x + (2.2 if yaw == 180 else 0), YL, z + (2.2 if yaw != 180 else 0)))
    # the orrery: a silver shaft from the dome, the fallen star in its cage, three planets on their arms over their orbits' rings
    top = sh.top - T.floor_t
    sun_y = YL + 19.0
    gk.bar(r, (0, top, 0), (0, YL + 7.0, 0), 0.6)
    cage = T.machine(r, "suncage")                             # (great hoops tumbling round the sun, high over everything)
    ring(cage, (0, sun_y, 0), 13.0, yaw=0, turn=7, width=0.6)
    ring(cage, (0, sun_y, 0), 11.0, yaw=60, turn=-9, width=0.55)
    ring(cage, (0, sun_y, 0), 9.0, yaw=120, turn=12, width=0.5)
    r.stand(SHARD, (0, sun_y - 2.0, 0), scale=(5.0, 5.0, 5.0))
    r.add(SUN, (0.2, sun_y - 0.6, 0))
    r.add(SPARK, (0, sun_y, 0))
    looks = {"p1": (5.0, YL + 10.5, "moon"), "p2": (8.5, YL + 9.0, "crystal"), "p3": (12.0, YL + 7.5, "star")}
    for pid, s0 in zip(("p1", "p2", "p3"), start):
        rad, y, look = looks[pid]
        orbit(r, (0, y - 0.6, 0), rad, width=0.45, pale=True)                     # its orbit: a pale stone ring
        for a in (45, 225):
            r.add(SPARK, (math.sin(math.radians(a + 40 * rad)) * rad, y - 0.3, math.cos(math.radians(a + 40 * rad)) * rad))
        p = r.mech(pid, "rotator", (0, y, 0))
        gk.bar(p, (0, 0, 0.3), (0, 0, rad), 0.45)                                   # its arm
        gk.bar(p, (0, 0, rad), (0, -1.2, rad), 0.2)                                 # (what it hangs by)
        if look == "moon":
            box(p, DISC, (0, -2.9, rad), (3.4, 3.4, 1.0))
        elif look == "crystal":
            orbit(p, (0, -2.9, rad), 2.2, width=0.2, axis="z")
            p.stand(CRYSTAL, (0, -4.4, rad), scale=(3.2, 3.2, 3.2))
        else:
            p.stand(STAR, (0, -4.6, rad), scale=(2.0, 2.0, 2.0))
        lit(p.add(SPARK, (0, -2.9, rad)), [0])                                     # its light: kindled in line
        for k in range(PLANET_STATES):
            p.pose(euler=(0, 360.0 / PLANET_STATES * k, 0))
        p.start = s0
        p.seconds = 2.5
    # the conjunction: all three in line (state 0: north, towards the way on) raises the bridge, once
    line = [{"mech": p, "state": 0} for p in ("p1", "p2", "p3")]
    r.extra["triggers"] = [
        {"id": "hint", "on": {"event": "enter", "from": "in"}, "do": [{"message": "$dungeons_starfall_orrery_hint", "to": "room"}],
         "solver": "ignore"},
        {"id": "conjunction", "on": [{"event": "state", "mech": p, "state": 0} for p in ("p1", "p2", "p3")], "if": line,
         "do": [{"set": "bridge", "to": 1}, {"sound": "sfx_secretfound", "at": [0, YL, -12.0]},
                {"sound": "sfx_fader_bell", "at": [0, sun_y, 0]}, {"message": "$dungeons_starfall_conjunction", "to": "room"}]},
    ]
    # light: torches along the galleries, the far bay's pair, stars hung from the dome
    for x in (-9.0, 9.0):
        T.light_stand(r, (x, YL, -13.0))
    for x in (-3.0, 3.0):
        T.light_stand(r, (x, YL, 15.5))
    # the sky: stars hung all round the dome (a few with a spark of light)
    for i in range(14):
        a = math.radians(360.0 / 14 * i + 7)
        rr = 14.5 if i % 2 else 10.5
        hang_star(r, (math.sin(a) * rr, top - 2.5 - (i % 3) * 1.5, math.cos(a) * rr), scale=0.75, spark=i % 4 == 0)
    r.stand(TABLET, (-3.0, YL, -15.8), yaw=0)
    r.stand(TABLET, (3.0, YL, -15.8), yaw=0)
    return r


# ---------------------------------------------------------------- the Star-Chart Halls

# Each hall's constellation: its stars in order (x, z on the hall's floor; the first is where the star fell), the stars drawn but
# not in it (no plate), where the falling starlight strikes (beside the way between the stars, never on a star), and the way on's
# side and place along it, and a second way on beyond the walk (side, place, the way its gate slides): two ways out, so a layout
# always finds room for what comes next.
SHADES = True           # the shades' fight after the walk (started by the sequence trigger's "start")
CHARTS = {
    "chart_a": {"name": "the Hunter", "stars": [(-8.0, -9.0), (-3.0, -3.5), (4.5, -6.5), (2.0, 2.5), (8.0, 8.0)],
                "drawn": [(-9.5, 6.5), (10.5, -1.0)], "falls": [(1.0, -6.5), (-0.5, 0.5), (6.0, 3.5), (-6.5, -1.0)], "out": (0, 0.0), "side": (1, -10.0, 1)},
    "chart_b": {"name": "the Serpent", "stars": [(8.5, -9.5), (2.5, -6.0), (-5.0, -6.5), (-8.5, 0.0), (-2.5, 3.5), (5.0, 2.0),
                                                   (7.5, 8.5)],
                "drawn": [(-10.0, 9.0), (0.0, -11.0)], "falls": [(-1.0, -9.0), (-6.5, 4.5), (1.5, 0.0), (9.5, -2.5)], "out": (1, -8.0), "side": (0, 8.0, -1)},
    "chart_c": {"name": "the Wolf", "stars": [(0.0, -10.0), (-7.5, -5.0), (6.0, -2.0), (-4.0, 3.5), (7.5, 8.0), (-8.5, 9.0)],
                "drawn": [(10.5, -9.0), (-10.5, -10.5)], "falls": [(0.0, -4.5), (1.5, 6.5), (-6.0, -0.5), (8.5, 3.0)], "out": (3, 6.0), "side": (0, -8.0, 1)},
}


def star_plate(r, id, at):
    """A star in the floor that is a plate: a pale stone diamond set level with the black floor, a star-stone's points standing out of
    it; pressed, it sinks a hand's breadth."""
    p = r.mech(id, "plate", at)
    box(p, ASHLAR, (0, 0.06, 0), (1.9, 0.12, 1.9), yaw=45)
    box(p, BAND, (0, 0.13, 0), (0.9, 0.04, 0.9), yaw=45)
    lo, hi = bounds(STAR)
    p.stand(STAR, (0, 0.14 - (hi[1] - lo[1]) * 0.25 * 0.55, 0), scale=(0.25, 0.25, 0.25))
    p.pose().pose(at=(0, -0.1, 0))
    p.seconds = 0.3
    return p


def star_line(r, a, b, y):
    """A pale inlaid line on the floor from star a to star b (stopping short of both)."""
    a, b = np.array([a[0], y, a[1]]), np.array([b[0], y, b[1]])
    d = b - a
    length = float(np.linalg.norm(d)) - 3.0
    if length > 0.5:
        mid = (a + b) / 2
        box(r, ASHLAR, (mid[0], y + 0.01, mid[2]), (0.22, 0.06, length), yaw=math.degrees(math.atan2(d[0], d[2])))


def falling_star(r, id, at, top, every, offset):
    """A trap of falling starlight: a star high under the vault that drops onto a pale ring on the floor and climbs back, on a beat.
    Before it falls the star whistles and draws up (its wind-up); where it lands, cold fire and lightning."""
    F = at[1]
    orbit(r, (at[0], F, at[2]), 1.6, width=0.2, n=16, inlay=True)
    t = r.mech(id, "trap", at)
    t.stand(STAR, (0, 0.0, 0), scale=(0.7, 0.7, 0.7))
    t.add(SPARK, (0, 0.6, 0))
    t.pose(at=(0, top - F - 1.6, 0)).pose(at=(0, 0.1, 0))         # rest: high under the vault; fired: on the floor
    t.start = 1
    t.extra.update({"damage": "lightning", "scale": 1.0, "every": every, "for": 1.0, "offset": offset,
                    "area": [3.2, 2.4, 3.2], "areaAt": [0, 1.2, 0],
                    "fx": ["fx_fimbulvinter_meteor_hit", "sfx_fader_spawn_meteor_impact"],
                    "warn": 1.6, "warnFx": ["sfx_fader_spawn_meteor_arrival"], "warnLift": 1.0})
    return t


def chart(name):
    """A Star-Chart Hall: 30 x 36 m under a 14 m vault, its floor a map of the night sky in black marble. Pale lines join a
    constellation's stars; where it begins lies a shard of the fallen star. Each of its stars is a plate: step on it and its beacon
    (a pale flame standing at the star's tip) kindles. Walk the constellation star by star in its order, from the fallen star along
    the lines, and the watchers' shades rise from the floor; when they're down the far gate opens. Step out of order and the walk
    starts again. All the while, stars fall on a beat onto the pale rings round the way."""
    cfg = CHARTS[name]
    S = (30.0, 14.0, 36.0)
    ok, olat = cfg["out"]
    sk, slat, sdir = cfg["side"]
    sh = shell(name, S, [("in", 2, 0.0, None), ("out", ok, olat, None), ("side", sk, slat, None)], tags=["chart"], max=1, min_depth=2)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    top = sh.top - T.floor_t
    # the ways on are shut until the constellation has been walked and its shades are down
    T.gate(r, "gate", ok, sh.half[2] if ok % 2 == 0 else sh.half[0], F, 0, "out", lateral=olat, open_dir=-1 if olat > 0 else 1)
    T.gate(r, "sidegate", sk, sh.half[2] if sk % 2 == 0 else sh.half[0], F, 0, "side", lateral=slat, open_dir=sdir)
    stars = cfg["stars"]
    for a, b in zip(stars, stars[1:]):
        star_line(r, a, b, F)
    for i, (x, z) in enumerate(stars, start=1):
        star_plate(r, f"star{i}", (x, F, z))
        bz = z + 1.6 if z < 10.0 else z - 1.6
        bb = r.mech(f"beacon{i}", "brazier", (x + 1.6, F, bz))
        box(bb, BAND, (0, 0.2, 0), (0.6, 0.4, 0.6))
        bb.stand("piece_groundtorch_blue", (0, 0.4, 0), scale=(1.3, 1.3, 1.3))
        bb.pose()
        bb.seconds = 0.2
        bb.extra.update({"burn": 40, "lightable": False, "fx": ["sfx_fader_bell"]})
    for x, z in cfg["drawn"]:
        box(r, ASHLAR, (x, F + 0.01, z), (1.2, 0.06, 1.2), yaw=45)
        r.add(SPARK, (x, F + 0.8, z))
    # where the star fell: a shard of it beside the first star, crystals round it
    fx, fz = stars[0]
    sx = fx - 2.4 if fx > -10 else fx + 2.4
    meteor_heap(r, (sx, F, fz - 1.0), yaw=30)
    r.stand(CRYSTAL, (sx - 0.6, F, fz + 0.9), scale=(1.2, 1.2, 1.2))
    # falling starlight
    for i, (x, z) in enumerate(cfg["falls"]):
        falling_star(r, f"fall{i + 1}", (x, F, z), top, every=6.0 + (i % 2), offset=round(1.5 * i, 1))
    # the shades that rise when it's walked: two waves; then the gates open (with SHADES off: the gates open at once)
    walked = [{"start": "shades"}] if SHADES else [{"set": "gate", "to": 1}, {"set": "sidegate", "to": 1}]
    pts = [(-11.0, F, -4.0), (11.0, F, -6.0), (-11.0, F, 10.0), (10.5, F, 12.0), (0.0, F, 13.0), (-3.0, F, -13.0)]
    enc = r.mech("shades", "encounter", (0, F + 3.5, 0)) if SHADES else None
    for _ in ([] if enc is None else [0]):
        enc.pose()
        enc.wire("gate", "set:1")
        enc.wire("sidegate", "set:1")
        enc.reached_from = "in"
        enc.extra.update({"area": [28.0, 7.0, 34.0], "startOn": "wire", "lock": [],
                          "waves": [{"flying": 2, "melee": 1}, {"melee": 2, "ranged": 1}],
                          "level": 1, "eliteLevel": 3, "pause": 3.0, "timeout": 40.0, "points": [r3(p) for p in pts],
                          "music": "CombatEventL2"})
    on = [{"event": "press", "mech": f"star{i}"} for i in range(1, len(stars) + 1)]
    r.extra["triggers"] = [
        {"id": "hint", "on": {"event": "enter", "from": "in"}, "do": [{"message": "$dungeons_starfall_chart_hint", "to": "room"}],
         "solver": "ignore"},
        *[{"id": f"kindle{i}", "on": {"event": "press", "mech": f"star{i}"}, "do": [{"set": f"beacon{i}", "to": 1}], "repeat": True,
          "solver": "ignore"}
          for i in range(1, len(stars) + 1)],
        {"id": "walked", "on": on, "when": "sequence", "within": 30.0 * len(stars),
         "do": walked + [{"sound": "sfx_secretfound", "at": [0, F + 1.0, 0]},
                {"message": "$dungeons_starfall_chart_done", "to": "room"}]},
    ]
    # light: pale flames by the walls, stars hung from the vault, the watchers' tables of charts
    for x, z in ((-13.0, -15.5), (13.0, -15.5), (-13.0, 15.5), (13.0, 15.5)):
        T.light_stand(r, (x, F, z))
    for x, z in ((-7.0, -8.0), (7.0, 0.0), (-6.0, 9.0)):
        hang_star(r, (x, F + 8.5, z), scale=0.6)
    tx = 13.4 if ok == 3 else -13.4                                  # (the watchers' table by the wall the way on isn't in)
    r.stand("piece_table_runed", (tx, F, 2.0), yaw=90)
    r.stand("piece_bench_runed", (tx - 1.4 * (1 if tx > 0 else -1), F, 2.0), yaw=90)
    r.stand(TABLET, (-tx * 1.015, F, -6.0), yaw=270 if tx < 0 else 90)
    return r


def signature_halls():
    out = {"orrery_a": lambda: orrery("orrery_a"), "orrery_b": lambda: orrery("orrery_b"), "passage_bend": bend, "passage_riser": riser,
           "gatehouse": gatehouse}
    out.update({n: (lambda n=n: chart(n)) for n in CHARTS})
    return out


# ---------------------------------------------------------------- the entrance look (mods/Dungeons/packs/looks/starfall.json)

def entrance_look():
    """The way in, out in the world (python orrery_rooms.py --look writes it): a crater rim of pale standing stones leaning outwards,
    the fallen star (a pillar of meteorite rock, glowing) driven into the ground in its middle behind the door, and the door a
    moon-door: a dark stone disc in a silver ring, in a pale stone arch under a black lintel with a star. Things below y 0 are
    buried."""
    from dungeon_parts import euler_matrix
    parts = []

    def part(prefab, at, euler=(0, 0, 0), scale=(1, 1, 1)):
        o = {"prefab": prefab, "at": r3(at)}
        if any(abs(v) > 1e-6 for v in euler):
            o["euler"] = r3(euler)
        if any(abs(v - 1) > 1e-6 for v in scale):
            o["scale"] = r3(scale)
        parts.append(o)

    def fit(prefab, centre, size, euler=(0, 0, 0)):
        lo, hi = bounds(prefab)
        R = euler_matrix(*euler)
        need = np.abs(R.T) @ np.array(size, float)
        scale = need / np.maximum(hi - lo, 1e-3)
        part(prefab, np.array(centre, float) - R @ (scale * (lo + hi) / 2), euler, scale)

    def hoop(centre, diameter, euler, width=0.35):
        lo, hi = bounds(RING)
        s = diameter / (hi[0] - lo[0])
        scale = np.array([s, width / (hi[1] - lo[1]), s])
        R = euler_matrix(*euler)
        part(RING, np.array(centre, float) - R @ (scale * (lo + hi) / 2), euler, scale)

    # the moon-door: a dark disc in a standing silver ring, set in a pale arch (the door's middle 3.2 m up, its face at z 0)
    fit(DISC, (0, 3.1, -0.6), (5.2, 5.4, 0.6))
    hoop((0, 3.1, -0.2), 6.2, (90, 0, 0), width=0.5)
    for x in (-3.9, 3.9):
        fit(BAND, (x, 0.4, -0.4), (1.6, 0.8, 1.4))
        fit(COLUMN, (x, 0.8 + 3.3, -0.4), (1.2, 6.6, 1.1))
    fit(BAND, (0, 7.75, -0.4), (9.6, 1.1, 1.4))
    lo, hi = bounds(STAR)
    part(STAR, (0, 8.3 - lo[1] * 0.8, -0.1), (0, 180, 0), (0.8, 0.8, 0.8))
    # the threshold: pale slabs into the dark
    fit(ASHLAR, (0, -0.4, 2.2), (5.0, 0.8, 4.0))
    fit(MARBLE, (0, -0.45, 5.2), (3.4, 0.8, 2.4))
    for x in (-2.6, 2.6):
        part(TORCH, (x, 0.0, 2.6), scale=(1.7, 1.7, 1.7))
    # the crater rim: standing stones in a ring behind the door, leaning out from its middle
    cz = -10.0
    for i, (a, s, lean) in enumerate(((-150, 1.4, 16), (-115, 1.2, 22), (-80, 1.5, 12), (-45, 1.25, 20), (-10, 1.6, 10),
                                      (25, 1.3, 18), (60, 1.45, 14), (95, 1.2, 24), (130, 1.5, 15), (165, 1.35, 19))):
        ar = math.radians(a)
        x, z = math.sin(ar) * 11.5, cz + math.cos(ar) * 11.5
        if z > -1.5 and abs(x) < 7.0:
            continue                                    # (the door stands there)
        part(STANDSTONE, (x, -0.8, z), (lean, a, 0), (s, s, s))
    for x, z, yaw in ((-9.5, -1.0, 35), (9.0, -2.0, -40), (-4.0, -21.0, 170), (5.5, -20.5, 200)):
        part("widestone_2", (x, -0.3, z), (0, yaw, 0), (1.3, 1.1, 1.3))
    # the fallen star, driven in at a slant, glowing; shards of it thrown round
    part(METEOR, (0.8, 2.2, cz - 0.5), (24, 30, -8), (1.1, 1.1, 1.1))
    for x, z, s in ((-4.5, cz + 3.5, 1.8), (3.8, cz + 4.0, 1.4), (5.0, cz - 3.0, 2.0), (-3.5, cz - 4.5, 1.5), (-6.5, 1.8, 1.2),
                    (6.4, 1.5, 1.4)):
        part(SHARD, (x, 0.2, z), (0, 37 * x, 0), (s, s, s))
    for x, z in ((-5.2, 0.8), (5.0, 1.0), (-7.5, cz + 7.0), (7.2, cz + 6.0)):
        part(CRYSTAL, (x, 0.3, z), (0, 40 * z, 0), (1.6, 1.6, 1.6))
    look = {"name": "starfall",
            "about": "The Starfall Orrery: a crater rim of pale standing stones leaning outwards, the fallen star (a pillar of meteorite "
                     "rock, glowing) driven in at a slant in its middle behind the door, its shards and violet crystals thrown round; the "
                     "door a moon-door (a dark stone disc in a silver ring) in a pale stone arch under a black lintel with a star, pale "
                     "flames either side. Made by tools/dungeons/orrery_rooms.py --look. Things below y 0 are buried.",
            "front": 5,
            "parts": parts,
            "inside": []}
    # the way out seen from inside: the same moon-door filling the doorway (its frame: origin on the floor, +z into the room)
    inside = []
    parts = inside
    fit(DISC, (0, 3.4, -0.5), (4.4, 4.4, 0.5))
    hoop((0, 3.4, -0.2), 4.9, (90, 0, 0), width=0.45)
    fit(ASHLAR, (0, 3.5, -0.9), (T.door_w + 0.4, T.door_h + 0.2, 0.4))
    look["inside"] = inside
    return look


if __name__ == "__main__":
    import json, os, sys
    if "--look" in sys.argv:
        out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "mods", "Dungeons", "packs", "looks", "starfall.json")
        look = entrance_look()
        from dungeon_parts import _compact, prefabs, known_prefabs
        missing = sorted(prefabs(look) - known_prefabs())
        if missing:
            raise SystemExit("not in ZNetScene: " + ", ".join(missing))
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write(_compact(json.dumps(look, indent=2)) + "\n")
        print("wrote", os.path.abspath(out), len(look["parts"]), "parts")
