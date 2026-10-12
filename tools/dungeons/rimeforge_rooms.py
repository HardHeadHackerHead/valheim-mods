"""
Rimeforge Depths' hand-made rooms (the theme authoring/themes/rimeforge.py names them in its signatures(); the boss rooms are in
bosses_rimeforge.py):
  entrance       the crack in the glacier: a cleft hall, the first frozen furnace in its corner, a giant's anvil, ember lamps
  frozenforges   SIGNATURE: the Frozen Forges. Three giant furnaces sealed in ice along a tall nave, a wall of ice across the way on.
                 Each furnace has a bellows lever (or take a torch to it): all three burning at once melts the ice wall. They burn
                 for a short while only and stand far apart, with steam vents across the hall on a beat between them.
  hammerline     SIGNATURE: the Hammer Line. A long works hall where trip-hammer presses slam across the whole floor in turn, on a beat
                 you can read (each draws up, creaks and shakes before it falls). At the far end a lever stops them for good, beside
                 the smiths' chest.
  slagbridge     a works hall crossed by a slag channel (grated, glowing), a giant's crucible on its tripod over it; its keepers come
                 for whoever steps in
  quenchhall     a frozen quench pool with a giant's sword quenched in it for ever; the smiths' chest on the far bank wakes its guards
  vault          the giants' hoard: their great chest on a giant's anvil; a plate opens the way out behind it
  secret_hoard   a secret: a closet walled up with cracked ice, a chest of ingots
Coordinates: the room's frame (its box centred on 0); doorways on their floor (dungeon_parts' convention).
"""
import math

import numpy as np

from dungeon_parts import box, bounds, Room, yaw_turn, r3
from authoring.core import rng_for, side_xz
from authoring.shells import Shell
from authoring.themes.rimeforge import (ICE, ICICLE, STONE, IRON, RIB, BARS, post, EMBER, LAMP, COAL, ANVIL, ICESPIKE, CHEST, VAULT_CHEST,
                                        VAULT_TYPE, FLOORGRATE, anvil, hang_bowl, iron, slag_channel)
from shifting_halls import encounter                          # (the fight helper: kind "encounter", its area, waves and points)

FURNACE = "blastfurnace"                                     # 3.7 x 5.4 x 3.9: a frost-giant's furnace at 1.4-1.6 times
FIREPIT = "fire_pit_iron"                                    # a Fireplace: the fire that comes and goes in a forge's mouth


def deep_shell(t, name, size, doors, deep, **kw):
    """A rect room whose floor stands `deep` metres over its box's bottom (room under it for things that sink into the floor: an ice
    wall that melts away, presses' pits). doors: [(id, side, lateral)] on that floor."""
    sh = Shell(t, name, "rect", size, rng_for(name), **kw)
    sh.floor_y = sh.bottom + deep
    sh.levels = [sh.floor_y]
    sh.plan.levels = [sh.floor_y]
    for id, k, lat in doors:
        sh.add_door(id, k, lat, 0)
    t.ground = sh.floor_y                                     # (the masonry plinth stands on this floor, not the box's bottom)
    try:
        sh.build()
    finally:
        t.ground = None
    # the floor's own thickness under it, down to the box's bottom less what must sink there
    return sh


def lit(part, states):
    """A mechanism part whose fire burns only in these states (the one-model rule: only the flame, light and crackle come and go)."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


# ---------------------------------------------------------------- the entrance

def entrance(t):
    """The way in, through the crack in the glacier: a tall cleft hall. A frost-giant's furnace stands sealed in ice in its north-east
    corner, still smouldering; a giant's anvil lies by the west wall; ember lamps by the way in, fire bowls hung high."""
    sh = t.shell("entrance", (16, 14, 20), [("in", 2, 0.0), ("n", 0, 0.0), ("e", 1, 3.0), ("w", 3, -3.0)], tags=["entrance"])
    r = sh.room
    r.entrance = True
    r.doorways[0]["entrance"] = True
    F = sh.floor_y
    # the furnace in the corner, its feet in a heap of ice, coal glowing in its mouth
    box(r, FURNACE, (4.6, F + 3.4, 6.6), (4.4, 6.8, 4.6), yaw=225)
    for x, z, s, yaw in ((2.4, 7.4, (2.4, 1.6, 2.0), 20), (5.8, 4.0, (2.2, 1.3, 2.4), -30), (3.4, 4.6, (1.6, 1.0, 1.4), 60)):
        box(r, ICE, (x, F + s[1] / 2 - 0.2, z), s, yaw=yaw)
    r.stand(COAL, (3.2, F, 5.4), yaw=40)
    r.stand(EMBER, (3.6, F + 0.6, 5.0))
    # a giant's anvil by the west wall (out of the ways), ingots stacked by it
    anvil(r, (-5.4, F, 5.4), 0, 3.2)
    r.stand("bar_iron_stack", (-4.0, F, 8.4), yaw=15, scale=(1.4, 1.4, 1.4))
    r.stand("bar_iron_stack", (-6.0, F, 8.4), yaw=-10, scale=(1.4, 1.4, 1.4))
    # ember lamps by the way in, fire bowls high over the hall
    for x in (-4.2, 4.2):
        t.light_stand(r, (x, F, -6.5))
    for x, z in ((0.0, -2.0), (0.0, 5.0)):
        hang_bowl(r, (x, F + 6.0, z))
    # the glacier's cleft: great icicles hanging in a crown over the middle of the hall (out of reach: their tips 6 m up)
    lo, hi = bounds(ICICLE)
    for x, z, sc in ((-1.6, -3.0, 1.2), (1.4, -1.0, 1.0), (-0.6, 1.6, 1.3), (2.2, 2.8, 0.9), (-2.4, 3.6, 1.0)):
        r.add(ICICLE, (x, sh.top - t.floor_t - hi[1] * sc, z), (0, 40 * x, 0), (sc, sc, sc))
    return r


# ---------------------------------------------------------------- the Frozen Forges (signature)

FF_SIZE = (24, 22, 36)
FF_DEEP = 7.5                     # its floor 7.5 m over the box's bottom: the ice wall sinks into it


def frozen_forges(t):
    """The Frozen Forges: a 12 m high nave, three frost-giants' furnaces sealed in ice along it, and a wall of ice filling the way on.
    Light all three at once to melt it: each furnace's bellows lever (beside it) blows its fire alight, or a torch in hand does; each
    burns 14 s, and they stand 15 to 20 m apart, with steam vents across the middle of the hall on a beat. When the three burn
    together the ice wall slumps into the floor, for good."""
    sh = deep_shell(t, "frozenforges", FF_SIZE, [("in", 2, 0.0), ("out", 0, 0.0), ("side", 1, -12.0), ("w", 3, 0.0)], FF_DEEP,
                    tags=["thaw"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    H = np.array(FF_SIZE) / 2
    W = t.wall_t
    # the ice walls in the ways on (north, and a side way east near the far end): thick glacier ice filling each doorway, sinking into
    # the floor as it melts
    def icewall(id, k, lateral, doorway):
        depth = H[2] if k % 2 == 0 else H[0]
        x, z = side_xz(k, lateral, depth - W - 0.9)
        g = r.mech(id, "gate", (x, F, z), (0, (90 * k) % 360, 0))
        g.doorway = doorway
        box(g, ICE, (0, t.door_h / 2 + 0.2, 0), (t.door_w + 0.6, 1.6, t.door_h + 0.6), tilt=(-90, 0))
        box(g, ICE, (-1.4, 1.0, -1.1), (2.2, 2.0, 1.4), yaw=25)
        box(g, ICE, (1.6, 0.7, -1.0), (1.8, 1.4, 1.2), yaw=-40)
        g.pose().pose(at=(0, -(t.door_h + 1.0), 0))
        g.seconds = 5.0
        g.start = 0
    icewall("icewall", 0, 0.0, "out")
    icewall("icewall2", 1, -12.0, "side")
    icewall("icewall3", 3, 0.0, "w")
    # the three furnaces: west near, east middle, west far (a zigzag), each in a heap of ice with its fire pit before its mouth
    forges = [("forgeA", -1, -11.0), ("forgeB", 1, 0.0), ("forgeC", -1, 11.0)]
    for id, side, z in forges:
        x = side * (H[0] - W - 3.4)
        box(r, FURNACE, (x, F + 3.8, z), (5.2, 7.6, 5.4), yaw=90 if side < 0 else 270)
        # the ice it's sealed in: heaps at its back corners, a crust over its shoulders
        for dz, s, yaw in ((-2.6, (2.6, 2.4, 2.2), 15), (2.6, (2.4, 1.8, 2.6), -25)):
            box(r, ICE, (x + side * 1.6, F + s[1] / 2 - 0.3, z + dz), s, yaw=yaw)
        box(r, ICE, (x + side * 0.4, F + 7.2, z), (4.0, 1.4, 5.8), yaw=8 * side)
        # the brazier: the fire in the furnace's mouth (one model: only its fire comes and goes), 14 s a lighting
        b = r.mech(id, "brazier", (x - side * 3.6, F, z))
        b.stand(FIREPIT, (0, 0, 0), scale=(1.5, 1.5, 1.5))
        b.stand(EMBER, (side * 1.2, 0.0, 0.0), scale=(1.2, 1.2, 1.2))
        b.pose()
        b.seconds = 0.2
        b.extra.update({"fx": ["vfx_FireAddFuel", "sfx_FireAddFuel"]})
        # its bellows lever, pulled from the hall's middle (a lever with no wires: never one-shot; the trigger lights the forge)
        t.lever(r, id + "bellows", (x - side * 3.6, F, z + (3.6 if z < 5 else -3.6)), 270 if side < 0 else 90, [])
        # it burns 14 s, then gutters out (a timer the solver leaves out: to it the fire stays lit, which only makes it easier)
        r.extra.setdefault("triggers", []).extend([
            {"id": id + "blow", "on": {"event": "lever", "mech": id + "bellows"}, "do": [{"set": id, "to": 1}], "repeat": True},
            {"id": id + "burn", "on": {"event": "state", "mech": id, "state": 1}, "repeat": True, "solver": "ignore",
             "do": [{"timer": id + "t", "after": 14}]},
            {"id": id + "gutter", "on": {"event": "timer", "name": id + "t"}, "repeat": True, "solver": "ignore",
             "do": [{"set": id, "to": 0}, {"sound": "vfx_Smoked", "at": r3((x - side * 3.6, F + 1.5, z))}]}])
    # the thaw: all three burning together
    r.extra["triggers"].append(
        {"id": "thaw", "on": [{"event": "state", "mech": id, "state": 1} for id, _, _ in forges],
         "if": [{"mech": id, "state": 1} for id, _, _ in forges],
         "do": [{"set": "icewall", "to": 1}, {"set": "icewall2", "to": 1}, {"set": "icewall3", "to": 1}, {"sound": "sfx_ice_destroyed", "at": r3((0, F + 2.0, H[2] - 3.0))},
                {"sound": "vfx_ice_destroyed", "at": r3((0, F + 2.0, H[2] - 3.0))}, {"sound": "sfx_secretfound"},
                {"message": "$dungeons_rimeforge_thawed"}]})
    r.extra["triggers"].append(
        {"id": "hint", "on": {"event": "enter", "at": r3((0, F + 2.0, -H[2] + 6.0)), "size": [16, 6, 8]}, "solver": "ignore",
         "do": [{"message": "$dungeons_rimeforge_thaw_hint"}]})
    # steam vents across the hall's middle: they hiss and whiten, then scald (fire), every 5 s; one lane on each side of the middle
    lane = H[0] * 2 - 2 * W - 1.4
    for i, z in enumerate((-5.5, 5.5)):
        v = r.mech(f"vent{i + 1}", "trap", (0, F, z))
        box(v, FLOORGRATE, (0, 0.06, 0), (lane, 0.12, 1.6))
        v.pose().pose()
        v.start = 1
        v.extra.update({"damage": "fire", "scale": 0.8, "every": 5.0, "for": 1.2, "offset": 2.5 * i,
                        "area": [round(lane, 2), 2.4, 1.8], "areaAt": [0, 1.2, 0],
                        "fx": ["vfx_Smoked", "sfx_fader_firebreath_out"],
                        "warn": 1.2, "warnFx": ["vfx_Smoked", "sfx_fader_firebreath_in"]})
        # (its glow under the grate)
        r.stand(EMBER, (-3.0, F - 0.8, z), scale=(0.8, 0.6, 0.8))
        r.stand(EMBER, (3.0, F - 0.8, z), scale=(0.8, 0.6, 0.8))
    # light: fire bowls hung high in the nave, lamps by the way in
    for z in (-12.0, 0.0, 12.0):
        hang_bowl(r, (0, F + 7.0, z))
    for x in (-4.0, 4.0):
        t.light_stand(r, (x, F, -H[2] + W + 3.0))
    for role, x, z in (("melee", 4.5, -11.0), ("ranged", -4.0, 6.0)):
        t.spawner(r, role, (x, F, z))
    return r


# ---------------------------------------------------------------- the Hammer Line (signature)

HL_SIZE = (16, 14, 44)


def hammer_line(t):
    """The Hammer Line: a long works hall, four trip-hammer presses across its whole width, falling in turn on a beat (each draws up,
    creaks and shakes 0.9 s before it falls: blunt, the land's trap damage). Run them between blows. At the far end a lever stops them
    all for good (for the way back), beside the smiths' chest. An iron gantry runs along the walls over the presses."""
    sh = t.shell("hammerline", HL_SIZE, [("in", 2, 0.0), ("out", 0, 0.0), ("side", 1, -17.0)], tags=["hammers"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    H = np.array(HL_SIZE) / 2
    W = t.wall_t
    inner = H[0] * 2 - 2 * W - 1.8                       # (clear of the ribs)
    top = sh.top - t.floor_t
    presses = (-12.0, -4.0, 4.0, 12.0)
    beat = 5.0
    for i, z in enumerate(presses):
        p = r.mech(f"press{i + 1}", "trap", (0, F, z))
        head = 2.4
        box(p, STONE, (0, head / 2, 0), (inner, head, 3.0))                       # the press head: black stone shod in iron
        box(p, IRON, (0, 0.15, 0), (inner + 0.2, 0.3, 3.2))
        for x in (-inner / 3, inner / 3):
            post(p, (x, head + 2.0, 0), 0.6, 4.0, 0.6)                        # its rods up into the beam
        raised = top - 0.9 - head - 4.0 - F                                          # (up under the beam, room for its wind-up)
        p.pose(at=(0, raised, 0)).pose(at=(0, 0, 0))
        p.start = 1
        p.extra.update({"damage": "blunt", "scale": 1.0, "every": beat, "for": 1.0, "offset": round(i * beat / 4 + 0.6 * (i % 2), 2),
                        "area": [round(inner + 0.4, 2), 2.4, 3.4], "areaAt": [0, 1.2, 0],
                        "fx": ["sfx_rock_destroyed", "vfx_troll_groundslam"],
                        "warn": 0.9, "warnFx": ["sfx_frozenking_punchaoe_chain_start", "vfx_Smoked"], "warnLift": 0.4})
        # the beam it hangs from, and its anvil mark on the floor (black iron)
        box(r, IRON, (0, top - 0.4, z), (H[0] * 2 - 2 * W, 0.8, 1.2))
        box(r, IRON, (0, F + 0.02, z), (inner, 0.04, 3.0))
    # the gantry: iron walkways along both walls under the beams, 5 m up, out of reach (nothing to climb to them: show, not a way)
    for s in (-1, 1):
        x = s * (H[0] - W - 0.9)
        n = 10
        span = H[2] * 2 - 2 * W - 1.0
        for i in range(n):
            box(r, FLOORGRATE, (x, top - 2.4, -span / 2 + (i + 0.5) * span / n), (1.4, 0.12, span / n + 0.02))
        for i in range(n):                                                     # (its rail, in lengths like the grates)
            z0 = -span / 2 + i * span / n
            iron(r, (x - s * 0.7, top - 2.4 + 1.1, z0), (x - s * 0.7, top - 2.4 + 1.1, z0 + span / n), 0.25)
    # the stop lever and the chest at the far end
    t.lever(r, "stop", (-4.0, F, 16.0), 180, [(f"press{i + 1}", "set:0") for i in range(4)], reached_from="in")
    t.chest(r, (-6.0, F, H[2] - W - 2.0), yaw=90, saved=True)
    # light: lamps between the presses, against the walls
    for x, z in ((-H[0] + W + 1.4, -16.0), (H[0] - W - 1.4, 0.0), (-H[0] + W + 1.4, 8.0)):
        t.light_stand(r, (x, F, z))
    r.extra["triggers"] = [
        {"id": "hint", "on": {"event": "enter", "at": r3((0, F + 2.0, -H[2] + 5.0)), "size": [12, 6, 6]}, "solver": "ignore",
         "do": [{"message": "$dungeons_rimeforge_hammers_hint"}]},
        {"id": "stopped", "on": {"event": "lever", "mech": "stop"}, "solver": "ignore",
         "do": [{"sound": "sfx_gui_craftitem_forge_end"}, {"message": "$dungeons_rimeforge_hammers_stopped"}]},
    ]
    return r


# ---------------------------------------------------------------- the vault and the secret

def vault(t):
    """The giants' hoard: their great chest (32 slots) on a giant's anvil-plinth, ingot stacks and the smiths' anvils. A plate just
    inside its door raises the portcullis in the far wall, and behind it is the way out to the entrance."""
    sh = t.shell("vault", (16, 12, 20), [("in", 2, 0.0)], tags=["end"], max=1)
    r = sh.room
    r.doorways[0]["type"] = VAULT_TYPE
    r.doorways[0]["door"] = False
    F = sh.floor_y
    box(r, STONE, (0, F + 0.4, 0.0), (7.0, 0.8, 5.0))                           # the plinth
    box(r, IRON, (0, F + 0.85, 0.0), (7.2, 0.1, 5.2))
    r.stand(VAULT_CHEST, (0, F + 0.9, 0.4), yaw=180, saved=True)                # the giants' chest (saved, scale 1)
    for x in (-5.4, 5.4):
        anvil(r, (x, F, -2.5), 0, 2.4)
        r.stand("bar_iron_stack", (x, F, 3.0), scale=(1.6, 1.6, 1.6))
    # the far wall with the way out: an opening, its portcullis, the exit behind
    zf = 6.6
    for x0, x1 in ((-8.0, -2.4), (2.4, 8.0)):
        box(r, STONE, ((x0 + x1) / 2, F + 4.0, zf), (x1 - x0, 8.0, 1.0))
    box(r, STONE, (0, F + 6.5, zf), (4.8, 3.0, 1.0))
    g = r.mech("exitgate", "platform", (0, F, zf))
    g.fill(BARS, (0, 2.5, 0), (4.6, 5.0, 0.3))
    g.pose().pose(at=(0, 4.4, 0))
    g.seconds = 3.0
    p = t.plate(r, "plate", (0, F, -6.0))
    p.wire("exitgate", "set:1")
    p.reached_from = "in"
    out = r.mech("exit", "exit", (0, F, 8.3), (0, 180, 0))
    box(out, BARS, (0, 2.6, 0.7), (4.4, 5.2, 0.3))
    box(out, IRON, (0, 5.3, 0.7), (4.6, 0.4, 0.5))
    for x, z in ((-6.0, -7.0), (6.0, -7.0), (0.0, 4.6)):
        t.light_stand(r, (x, F, z))
    hang_bowl(r, (0, F + 5.5, 1.0))
    t.spawner(r, "melee", (-4.0, F, -4.0))
    t.spawner(r, "ranged", (4.0, F, -4.0))
    return r


def cracked_ice(room, id, k, half, thr, doorway, t):
    """A doorway walled up with cracked ice (a breakable: pickaxe or blunt): slabs knocked askew, dark behind the gaps, one corner
    fallen out. Broken, its parts go and the doorway opens."""
    x, z = side_xz(k, 0.0, half - 0.6)
    b = room.mech(id, "breakable", (x, thr, z), (0, (90 * k) % 360, 0))
    b.pose().pose()
    b.doorway = doorway
    b.extra["health"] = 300
    w, h = (t.door_w + 0.3) / 2, (t.door_h + 0.1) / 3
    box(b, STONE, (0, t.door_h / 2, -0.4), (t.door_w + 0.3, t.door_h + 0.1, 0.2))
    tilts = ((6, -7, 4), (-5, 6, -6), (8, 4, -3), (-6, -5, 7), (5, 9, 5), (-8, -6, -4))
    for i in range(2):
        for j in range(3):
            if (i, j) == (1, 2):
                continue
            tx, ty, tz = tilts[i * 3 + j]
            box(b, ICE, (-w / 2 + i * w + 0.06 * tx, h / 2 + j * h + 0.04 * ty, -0.05 * abs(tz) - 0.15), (w - 0.18, 0.6, h - 0.16),
                yaw=tz, tilt=(tx - 90, ty))
    return b


def secret_hoard(t):
    """A secret: a smiths' strongroom off a spare doorway, walled up with cracked ice (break it with a pickaxe or a heavy blow): a
    chest of ingots on an anvil, coal, an ember lamp. Never on the way."""
    sh = t.shell("secret_hoard", (12, 10, 12), [("in", 2, 0.0)], tags=["secret"], max=1)
    r = sh.room
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    cracked_ice(r, "crack", 2, 6.0, F, "in", t)
    r.extra["hiddenBy"] = "crack"
    box(r, STONE, (0, F + 0.5, 2.6), (4.0, 1.0, 2.0))
    t.chest(r, (0, F + 1.0, 2.6), yaw=180, saved=True)
    r.stand("bar_iron_stack", (3.0, F, 3.0), scale=(1.4, 1.4, 1.4))
    r.stand(COAL, (-3.0, F, 2.8))
    t.light_stand(r, (-3.2, F, -1.5))
    return r


# ---------------------------------------------------------------- set pieces between the steps (the theme's own, with a job each)

def slag_band(r, t, z, F, x0, x1, width=2.2):
    """A slag channel across a floor at z, from x0 to x1: low stone kerbs (0.25 m: creatures step over them too), smouldering coal and
    ember light between them, iron grates over it flush with the kerbs (walked over, never fallen into)."""
    for s in (-1, 1):
        box(r, STONE, ((x0 + x1) / 2, F + 0.125, z + s * (width / 2 + 0.2)), (x1 - x0, 0.25, 0.4))
    n = max(1, int((x1 - x0) // 4.0))
    for i in range(n):
        x = x0 + (i + 0.5) * (x1 - x0) / n
        box(r, COAL, (x, F + 0.06, z), (min(3.4, (x1 - x0) / n - 0.3), 0.12, width - 0.2), yaw=0)
        if i % 2 == 0:
            r.stand(EMBER, (x, F, z), scale=(0.6, 0.28, 0.6))
    for i in range(int(math.ceil((x1 - x0) / 2.0))):
        a = x0 + i * 2.0
        b = min(x1, a + 2.0)
        box(r, FLOORGRATE, ((a + b) / 2, F + 0.19, z), (b - a + 0.02, 0.12, width + 0.4))


def slag_bridge(t):
    """A works hall crossed by a slag channel under iron grates, glowing; a frost-giant's crucible stands on its tripod over the
    channel by the east wall. Its keepers (one wave) come for whoever steps in."""
    sh = t.shell("slagbridge", (20, 12, 24), [("in", 2, 0.0), ("out", 0, 0.0), ("w", 3, 4.0)], max=1, tags=[])
    r = sh.room
    F = sh.floor_y
    H = np.array((20, 12, 24)) / 2
    W = t.wall_t
    slag_band(r, t, -2.0, F, -H[0] + W + 0.8, H[0] - W - 0.8)
    box(r, "piece_cauldron", (5.6, F + 3.2, -2.0), (3.6, 6.4, 3.9))
    r.stand("bar_iron_stack", (7.6, F, 6.5), scale=(1.4, 1.4, 1.4))
    r.stand(COAL, (6.4, F, 8.4), yaw=30)
    anvil(r, (-6.0, F, -8.0), 90, 2.2)
    for x, z in ((-7.6, 8.0), (7.6, -8.6)):
        t.light_stand(r, (x, F, z))
    hang_bowl(r, (0, F + 6.0, 4.0))
    pts = [(6.5, F, 3.5), (-6.5, F, 8.0), (6.0, F, -7.5)]
    encounter(r, "keepers", (-H[0] + W, F, -H[2] + W + 3.0), (H[0] - W, F + 8.0, H[2] - W), "enter", [{"melee": 2, "ranged": 1}], pts,
              reached_from="in", music="CombatEventL2")
    return r


def quench_hall(t):
    """A frozen quench pool fills half the hall; a giant's sword stands in it, quenched there for ever. The smiths' chest waits on the
    far bank by the anvil: lifting its lid wakes its guards (two waves)."""
    sh = t.shell("quenchhall", (20, 12, 20), [("in", 2, -3.0), ("out", 0, 3.0), ("e", 1, 3.0)], max=1, tags=[])
    r = sh.room
    F = sh.floor_y
    H = np.array((20, 12, 20)) / 2
    W = t.wall_t
    box(r, "IcePond_rock", (-3.6, F - 0.35, 1.0), (9.0, 0.8, 9.0))                       # the pool, frozen over (walked on)
    lo, hi = bounds("giant_sword1")
    box(r, "giant_sword1", (-4.6, F + 3.4, 2.4), (hi - lo) * np.array([0.38, 0.38, 0.38]), yaw=30, tilt=(8, -6))
    for x, z, sz, yaw in ((-6.6, -1.6, (2.2, 1.0, 1.8), 20), (-1.4, 3.8, (1.8, 0.8, 2.0), -35)):
        box(r, ICE, (x, F + sz[1] / 2 - 0.2, z), sz, yaw=yaw)
    chest = np.array([-6.4, F, 6.4])
    anvil(r, (-3.6, F, 7.0), 90, 2.0)
    t.chest(r, chest, yaw=180, saved=True)
    t.light_stand(r, (-8.0, F, -6.8))
    t.light_stand(r, (7.6, F, 7.0))
    hang_bowl(r, (2.0, F + 6.0, -2.0))
    pts = [(7.0, F, -6.0), (6.5, F, 5.8), (-2.0, F, -6.0), (-0.5, F, 6.6)]
    encounter(r, "guards", (-H[0] + W, F, -H[2] + W), (H[0] - W, F + 8.0, H[2] - W), "open", [{"melee": 2}, {"melee": 1, "ranged": 1}], pts,
              reached_from="in", chest=chest, music="CombatEventL2")
    return r
