"""
The Drowned Vault's two boss rooms (pack "drowned", theme tidevault), made with the boss toolkit (skill/bosses.md): the Bell Shaft (the
Bellkeeper, a mini boss) and the Tide Throne (Jarl Hafgrim the Drowned, the final boss). The theme's signatures() takes both from here.

THE BELLKEEPER (mini boss), in the Bell Shaft: a tower room 26 m tall, a stone bell 4.7 m across hanging on its yoke over a planked mark
in the middle, a bell-rope lever 6.8 m out on each side of the mark.
  intro    step in: the gates slam, the great bell tolls; the Bellkeeper wakes on its stepped dais with the banner and leaps down.
  phase 1  its blows and a "tolling slam" (a red ring round it, the bell's toll as its cue). The bell is a weapon for the players from
           the start: lure it onto the mark, pull a rope, the bell drops over it (1.2 s: roll out): a heavy blow and a stagger.
  at 60%   it rings the bell back up and the tide shields it: it can't be hurt but keeps fighting; only the bell breaks the shield
           (lure it under, drop the bell). A drowned thrall joins it.
  at 30%   a second wind to 55%, enraged; it rushes down marked lanes; two more come.
  death    the bell falls with a last toll, the banner, the gates open (and the side door, where the shortcut ends).

JARL HAFGRIM THE DROWNED, CALLER OF THE TIDE (final boss), in the Tide Throne: a drowned mead-hall 36 x 44 m. A nave between two rows of
twisted pillars, a side aisle beyond each, his whale-bone throne on a stepped dais at the north end, his longship's bow rammed through
the north wall over it, and from the bow's tip his anchor hangs over a planked mark in the middle of the nave, a windlass lever 6.8 m
out on each side. A sluice gate in each side wall, its lever in the aisle beside it.
  intro    step in: the gate slams, the sea-fire gutters out, the hull groans; the fire comes back in pairs from the door to the throne;
           he wakes on his throne in a burst and a splash, the banner, and leaps down into the nave.
  phase 1  his blows, a "brine slam" (a ring round him), and his anchor all fight long: lure him onto the mark and pull a windlass,
           the anchor plunges (1.4 s): 10% of his health and a stagger.
  at 66%   he calls the tide: he leaps to his throne and is shielded (he holds, channelling); the sluice gates open and the aisles
           flood (the undertow: a hiss, the floor glows green, then a surge that hurts whoever is in the aisle, frost, every 6 s);
           brine falls under each player, drowned thralls come out of the sluices. Shut both sluice gates together (each lever stands in
           its flooded aisle, 26 m apart; a shut gate creaks open again after 12 s), twice: the first time his shield flickers and the
           sluices burst open again, the second it breaks, the tide turns on him (a blow, 10%), the undertow stops and he comes down.
  at 30%   a second wind to 55%, enraged; the tide floods back for good, tide-stones heave up out of the nave (cover from the brine),
           and he rushes down marked lanes.
  death    the sluices slam shut, the undertow stops, the anchor drops, his ship's timbers break, the banner; the vault's way opens.
His fight is 230 s (a solo player of the land hitting him all the time), the Bellkeeper's 55 s: over four times.
"""
import math

import numpy as np

from dungeon_parts import Room, box, bounds, r3
from boss_rooms import step_dais, lit
from authoring.themes.tidevault import (THEME as T, STONE, TILE, FILL, CURB, COLUMN, FONT, BOARDS, lattice, FIRE, BELL, CHAIN, KELP,
                                        TYPE, orient, hang_chain, sea_fire, bell_lantern, kelp, fallen_bell, anchor)
from authoring.themes.tidevault_rooms import shell, band, ribs, emblem, THRONE


def encounter(room, id, area_lo, area_hi, start_on, spawns, lock=(), opens=(), reached_from=None, music=None, boss=None, elite_level=3):
    """A boss fight (Runtime's kind "encounter" with a boss; skill/bosses.md)."""
    lo, hi = np.array(area_lo, float), np.array(area_hi, float)
    m = room.mech(id, "encounter", (lo + hi) / 2)
    m.pose()
    for t in opens:
        m.wire(t, "set:1")
    m.reached_from = reached_from
    m.extra.update({"area": r3(hi - lo), "startOn": start_on, "lock": list(lock), "level": 1, "eliteLevel": elite_level,
                    "pause": 4.0, "timeout": 40.0, "points": [r3(np.array(x, float)) for x in spawns]})
    if music:
        m.extra["music"] = music
    m.extra["boss"] = boss
    return m


def lamps(r, id, groups, scale=0.7):
    """Sea-fire fonts a fight puts out and lights again in turn: the fonts are the room's, their flames one mechanism with a state per
    group (0 all out, 1 the first group lit, ...), lit to the full as the room stands."""
    m = r.mech(id, "platform", (0, 0, 0))
    n = len(groups)
    lo, hi = bounds(FIRE)
    for k, spots in enumerate(groups, start=1):
        for at in spots:
            box(r, FONT, (at[0], at[1] + 0.5 * scale, at[2]), (2.2 * scale, 1.0 * scale, 2.2 * scale))
            lit(m.add(FIRE, (at[0], at[1] + scale + 0.25 - hi[1], at[2])), list(range(k, n + 1)))
    for _ in range(n + 1):
        m.pose()
    m.seconds = 0.0
    m.start = n
    return m


def floor_mark(r, at, size):
    """A planked mark on the floor, a coping-stone border round it: where the bell (the anchor) comes down."""
    x, y, z = at
    r.fill(CURB, (x, y + 0.02, z), (size + 0.8, 0.04, size + 0.8))
    r.fill(BOARDS, (x, y + 0.05, z), (size, 0.04, size), euler=orient(BOARDS, (0, 0, 1), (1, 0, 0)))


# ---------------------------------------------------------------- the Bell Shaft (the Bellkeeper)

BS_SIZE = (24.0, 26.0, 30.0)


def bell_shaft():
    sh = shell("bellshaft", BS_SIZE, [("in", 2, 0.0, None), ("out", 3, 5.0, None), ("side", 1, -5.0, None)], tags=["bellshaft"], max=1,
               min_depth=3)
    r = sh.room
    F = sh.floor_y
    H = np.array(BS_SIZE) / 2
    r.extra["joinBy"] = ["in"]
    T.gate(r, "ingate", 2, H[2], F, 1, "in")
    T.gate(r, "outgate", 3, H[0], F, 0, "out", lateral=5.0, open_dir=-1)
    T.gate(r, "sidegate", 1, H[0], F, 0, "side", lateral=-5.0, open_dir=1)
    # the Bellkeeper's dais at the north end, stepped down in 0.2 m steps on every side; an old bell frame behind it
    top = step_dais(r, TILE, (0, F, 9.5), (9.0, 5.0), 1.0)
    lo, hi = bounds("fader_bellholder")
    r.stand("fader_bellholder", (0, F, 12.9), yaw=0, scale=(1.6, 1.6, 1.6))
    bell_lantern(r, (0, F + 1.4 * 1.6, 12.9), scale=0.9, chain_to=F + 3.0 * 1.6)
    # the great bell over its mark: a mechanism (0 up, 1 down over the mark), on a timber yoke; the hoist chains hang from the vault
    # to where the yoke rides when it's up (the bell drops free of them)
    zc = -1.0
    s = 4.5
    blo, bhi = bounds(BELL)
    up = 6.0                                               # its mouth's height over the floor when it's up
    floor_mark(r, (0, F, zc), 5.4)
    b = r.mech("bell", "platform", (0, F, zc))
    b.add(BELL, (0, up - blo[1] * s, 0), scale=(s, s, s))
    crown = up + (bhi[1] - blo[1]) * s
    box(b, "darkwood_beam4x4", (0, crown + 0.35, 0), (7.0, 0.7, 0.9))
    b.pose().pose(at=(0, -(up - 0.05), 0))
    b.seconds = 1.2
    b.extra["fx"] = ["sfx_fader_bell"]
    ceil = sh.top - T.floor_t
    for x in (-3.2, 3.2):
        hang_chain(r, (x, ceil, zc), ceil - (F + crown + 0.7), thick=1.3)
    # the bell-ropes: a lever 6.8 m out on each side of the mark, pulled from the mark's side
    for side, yaw in ((-1, 270), (1, 90)):
        T.lever(r, f"rope{'w' if side < 0 else 'e'}", (side * 6.8, F, zc), yaw, [("bell", "next")])
    # light: bell-lanterns on brackets high on the walls, sea-fire by the door and the dais; small bells hung high round the shaft
    for x, z in ((-10.2, -8.0), (10.2, -8.0), (-10.2, 8.0), (10.2, 8.0)):
        box(r, "darkwood_beam4x4", (x * 0.93, F + 9.0, z), (1.6, 0.4, 0.5))
        bell_lantern(r, (x * 0.88, F + 7.2, z), scale=1.1, chain_to=F + 8.8)
    for x in (-5.0, 5.0):
        sea_fire(r, (x, F, -11.0))
        sea_fire(r, (x * 1.5, F, 9.0))
    for x, z in ((-8.5, -12.0), (8.5, 12.0), (-8.5, 12.0)):
        bell_lantern(r, (x, F + 14.0, z), scale=0.8, chain_to=ceil)
    r.stand("skull_pile", (-9.5, F, 2.0), scale=(1.3, 1.3, 1.3))
    fallen_bell(r, (9.0, F, -11.0), 40, 1.8)

    seat = [0.0, float(top[1]), 9.5]
    adds = [(-7.5, F, -6.5), (7.5, F, -6.5), (-8.0, F, 5.0), (8.0, F, 5.0)]
    boss = {
        "name": "$dungeons_boss_drowned_bellkeeper", "title": "$dungeons_boss_drowned_bellkeeper_title", "role": "elite", "fight": 55, "perPlayer": 0.5,
        "scale": 1.5, "mini": True,
        "look": {"light": [0.45, 1.0, 0.62], "range": 5, "aura": "vfx_Frost"},
        "intro": {"seconds": 5.6, "wake": 1.6, "leap": [0.0, F, 4.5], "leapAt": 3.0, "beats": [
            {"at": 0.0, "message": "$dungeons_fight_start", "sound": "sfx_fader_bell", "where": [0.0, F + 6.0, zc], "shake": 0.3},
            {"at": 1.6, "fx": "vfx_spawn_large", "sound": "sfx_draugr_alerted", "shake": 0.4, "banner": True, "where": seat},
            {"at": 5.6, "message": "$dungeons_boss_drowned_bellkeeper_hint"}]},
        "abilities": [
            {"id": "toll", "kind": "slam", "every": 11, "first": 5, "warn": 1.6, "radius": 4.5, "scale": 2, "push": 35,
             "cue": "sfx_fader_bell"},
            {"id": "rush", "kind": "charge", "every": 12, "first": 4, "warn": 1.4, "width": 3, "length": 16, "scale": 2, "push": 40,
             "phases": [2]}],
        "weakTo": [{"mech": "bell", "state": 1, "area": [5.6, 5.0, 5.6], "areaAt": [0.0, 2.5, 0.0], "share": 0.12, "stagger": True,
                    "breaks": True, "hurt": 2.0, "held": "$dungeons_boss_drowned_bellkeeper_held"}],
        "phases": [
            {"at": 0.6, "wires": [{"target": "bell", "do": "set:0"}], "adds": {"melee": 1}, "message": "$dungeons_boss_drowned_bellkeeper_p1",
             "sound": "sfx_fader_bell", "shake": 0.4,
             "shield": {"take": 0, "seconds": 75, "hold": False, "breakShare": 0.05, "hint": "$dungeons_boss_drowned_bellkeeper_shield"}},
            {"at": 0.3, "healTo": 0.55, "enrage": 1.2, "adds": {"melee": 1, "ranged": 1}, "message": "$dungeons_boss_drowned_bellkeeper_p2",
             "sound": "sfx_gdking_scream", "shake": 0.5}],
        "death": {"seconds": 3.0, "beats": [
            {"at": 0.0, "sound": "sfx_draugr_death", "shake": 0.6, "banner": True},
            {"at": 0.8, "wires": [{"target": "bell", "do": "set:1"}], "sound": "sfx_fader_bell", "shake": 0.4, "where": [0.0, F, zc]}]},
    }
    encounter(r, "bellkeeper", (-H[0] + T.wall_t, F, -H[2] + T.wall_t + 3.5), (H[0] - T.wall_t, F + 10.0, H[2] - T.wall_t), "enter",
              [seat] + adds, lock=["ingate", "outgate"], opens=["ingate", "outgate", "sidegate"], reached_from="in",
              music="CombatEventL4", boss=boss)
    return r


# ---------------------------------------------------------------- the Tide Throne (Jarl Hafgrim)

TT_SIZE = (32.0, 22.0, 36.0)
TT_FLOOR = -TT_SIZE[1] / 2 + 4.0              # a floor 4 m thick: the tide-stones rise out of it


def tide_throne():
    W, Hy, Hz = TT_SIZE[0] / 2, TT_SIZE[1] / 2, TT_SIZE[2] / 2
    F = TT_FLOOR
    IN_X, IN_Z = W - T.wall_t, Hz - T.wall_t
    AISLE = (8.5 + 0.9 + IN_X) / 2                # the side aisles' middle (beyond the pillars)
    r = Room("throne", TT_SIZE, tags=["throne"], max=1, min_depth=4)
    r.doorway("in", (0, F, -Hz), (0, 0, 1), type=TYPE)
    r.doorway("out", (0, F, Hz), (0, 0, -1), type=THRONE, door=False)
    # (a second way in and a second way out: a hall this big fits a layout more easily with a choice; the one not taken is sealed)
    r.doorway("inE", (W, F, 3.5), (-1, 0, 0), type=TYPE)
    r.doorway("outW", (-W, F, 10.0), (1, 0, 0), type=THRONE, door=False)
    r.extra["joinBy"] = ["in", "inE"]
    # the shell: walls with the doorways and the sluices' openings, the thick floor, the vault
    sluice_z = -5.0
    T.wall_run(r, 2, Hz, -W, W, -Hy, Hy, [(0.0, F)])
    T.wall_run(r, 0, Hz, -W, W, -Hy, Hy, [(0.0, F)])
    T.wall_run(r, 1, W, -Hz, Hz, -Hy, Hy, [(-3.5, F)])
    T.wall_run(r, 3, W, -Hz, Hz, -Hy, Hy, [(10.0, F)])
    T.floor(r, -W, W, -Hz, Hz, F, thickness=F + Hy)
    T.ceiling(r, -W, W, -Hz, Hz, Hy)
    T.door_frame(r, 2, 0.0, F, IN_Z)
    T.door_frame(r, 0, 0.0, F, IN_Z)
    T.gate(r, "ingate", 2, Hz, F, 1, "in")
    T.gate(r, "outgate", 0, Hz, F, 0, "out")
    T.door_frame(r, 1, -3.5, F, IN_X)
    T.door_frame(r, 3, 10.0, F, IN_X)
    T.gate(r, "ingateE", 1, W, F, 1, "inE", lateral=-3.5, open_dir=-1)
    T.gate(r, "outgateW", 3, W, F, 0, "outW", lateral=10.0, open_dir=-1)
    ceil = Hy - T.floor_t
    for z in (-14.0, -4.0, 6.0, 14.0):
        ribs(r, z, F + 9.0, ceil, IN_X)
    for k in (1, 3):
        for y in (F + 1.1, F + 3.3):
            band(r, k, W, -IN_Z + 0.5, IN_Z - 0.5, y)
    # the pillars between the nave and the aisles
    for x in (-8.5, 8.5):
        for z in (-14.0, -8.0, 6.0, 12.0):
            T.pillar(r, (x, F, z), ceil - F, 1.8)
    # the throne's dais (stepped in 0.2 m on every side), the whale-bone throne, the hull rammed through the wall over it
    top = step_dais(r, TILE, (0, F, 9.0), (14.0, 8.0), 1.2)
    r.stand("piece_bone_throne", (0, top[1], 10.0), yaw=0, scale=(1.8, 1.8, 1.8))
    lo, hi = bounds("shipwreck_karve_bow")
    bs = 2.4
    box(r, "shipwreck_karve_bow", (0, F + 10.0 + (lo[1] + hi[1]) / 2 * bs, 14.9 + (lo[2] + hi[2]) / 2 * bs), (hi - lo) * bs, yaw=0,
        tilt=(-8, 0))
    tip = np.array([0.0, F + 9.6, 2.4])
    # the anchor, hung from the bow's tip over its mark in the nave: a mechanism (0 up, 1 dropped onto the mark)
    zc = 1.0
    floor_mark(r, (0, F, zc), 5.0)
    a = r.mech("anchor", "platform", (0, F, zc))
    ring = 9.2                                            # its ring's height over the floor when it's up
    hang_chain(a, (0, ring + 2.0, 0), 2.0, thick=1.4)
    anchor(a, (0, ring, 0), 4.8, yaw=0, width=0.5)
    drop = ring - 4.8 - 0.3
    a.pose().pose(at=(0, -drop, 0))
    a.seconds = 1.4
    a.extra["fx"] = ["fx_WaterImpact_Big", "sfx_ship_impact"]
    hang_chain(r, (0, F + 11.4, zc), 0.6, thick=1.4)       # (the chain's end on the bow, where the anchor's chain runs from)
    for side, yaw in ((-1, 270), (1, 90)):
        T.lever(r, f"windlass{'w' if side < 0 else 'e'}", (side * 6.8, F, zc), yaw, [("anchor", "next")])
    # the sluices: a timber lattice gate in each side wall (0 open: the tide comes in; 1 shut, creaking open again after 12 s), a sea-glow
    # behind it, its lever in the aisle beside it (a trigger: never one-shot)
    for side, name in ((-1, "W"), (1, "E")):
        x = side * (IN_X - 0.3)
        for s2 in (-1, 1):
            r.fill(COLUMN, (side * (IN_X - 0.45), F + 3.2, sluice_z + s2 * 3.6), (0.9, 6.4, 0.9))
        r.fill(CURB, (side * (IN_X - 0.45), F + 6.6, sluice_z), (0.9, 0.8, 8.2))
        sea_fire(r, (side * (IN_X - 1.2), F, sluice_z - 2.4), scale=0.5)
        g = r.mech(f"sluice{name}", "platform", (x, F, sluice_z))
        lattice(g, (0, 3.0, 0), 6.4, 6.0, 0.25, yaw=90)
        g.pose(at=(0, 3.6, 0)).pose()
        g.start = 0                                         # (open as the hall stands: the shield puts them back open as it comes up)
        g.seconds = 1.5
        g.cycle = 12.0
        g.extra["cycleIn"] = [1]
        g.extra["fx"] = ["sfx_morkhalla_gate_slide"]
        lever = f"sluicelever{name}"
        T.lever(r, lever, (side * AISLE, F, sluice_z - 7.5), 0, [])
        r.extra.setdefault("triggers", []).append(
            {"id": f"shut{name}", "on": {"event": "lever", "mech": lever}, "do": [{"set": f"sluice{name}", "to": 1}], "repeat": True,
             "solver": "ignore"})
    # the undertow in each aisle (a trap: off until the tide is called): green glow in the floor's joints, a hiss, then a surge
    for side, name in ((-1, "W"), (1, "E")):
        cx = side * AISLE
        t = r.mech(f"undertow{name}", "trap", (cx, F, 0.0))
        flo, fhi = bounds(FIRE)
        for z in np.arange(-16.0, 16.1, 4.0):
            for dx in (-1.6, 1.6):
                lit(t.add(FIRE, (dx, 0.05 - fhi[1], z)), [1])
        t.pose().pose(at=(0, 0.25, 0))
        t.start = 0
        t.extra.update({"damage": "frost", "scale": 1.0, "every": 6.0, "for": 1.5, "offset": 0.0 if side < 0 else 3.0,
                        "area": [IN_X - 9.4, 2.5, 2 * IN_Z], "areaAt": [0.0, 1.25, 0.0], "warn": 1.5,
                        "warnFx": ["fx_leviathan_reaction"], "fx": ["fx_WaterImpact_Big"], "warnLift": 0.0})
    # the tide-stones that heave up out of the nave at the last (cover from the brine): their tops 0.4 m over the floor, 3.4 m risen
    for i, (x, z) in enumerate(((-4.5, -13.0), (4.5, -13.0), (-4.5, -6.8), (4.5, -6.8))):
        b = r.mech(f"stone{i + 1}", "platform", (x, F, z))
        b.fill(FILL, (0, -1.4, 0), (3.6, 3.6, 3.6))
        b.fill(TILE, (0, 0.38, 0), (3.6, 0.05, 3.6))
        b.pose().pose(at=(0, 3.0, 0))
        b.seconds = 3.0
    # the sea-fire the intro puts out and brings back in pairs, from the door to the throne (lit as the hall stands)
    lamps(r, "lamps", [[(x, F, -15.5) for x in (-5.0, 5.0)], [(x, F, -10.0) for x in (-6.0, 6.0)],
                       [(x, F, 3.2) for x in (-6.0, 6.0)] + [(x, top[1], 9.4) for x in (-3.5, 3.5)]])
    for z in (-12.0, 0.0, 11.0):
        for x in (-AISLE, AISLE):
            bell_lantern(r, (x, F + 7.0, z), scale=1.1, chain_to=ceil)
    for z in (-12.0, -2.0, 10.0):
        for x in (-IN_X + 0.2, IN_X - 0.2):
            kelp(r, (x, ceil, z), 90 if x < 0 else 270, n=2)
    for x, z in ((-13.3, 14.0), (13.6, 8.0)):
        r.stand("barrell", (x, F, z), scale=(1.3, 1.3, 1.3))
    r.stand("StatueEvil", (-13.0, F, 14.8), yaw=160, scale=(1.0, 1.0, 1.0))
    fallen_bell(r, (12.6, F, 14.2), 20, 2.4)

    seat = [0.0, float(top[1]), 8.5]
    aisles = [(-AISLE, F, 4.0), (AISLE, F, 4.0), (-AISLE, F, -8.5), (AISLE, F, -8.5)]
    sluices = [(-AISLE, F, sluice_z + 3.0), (AISLE, F, sluice_z + 3.0)]
    undertow_on = [{"target": "undertowW", "do": "set:1"}, {"target": "undertowE", "do": "set:1"}]
    undertow_off = [{"target": "undertowW", "do": "set:0"}, {"target": "undertowE", "do": "set:0"}]
    bow = [0.0, F + 9.0, 4.0]
    boss = {
        "name": "$dungeons_boss_drowned_jarl", "title": "$dungeons_boss_drowned_jarl_title", "role": "elite", "fight": 230, "perPlayer": 0.5,
        "scale": 1.8,
        "look": {"light": [0.4, 1.0, 0.75], "range": 7, "aura": "vfx_Poison", "enraged": [0.75, 1.0, 0.3]},
        "intro": {"seconds": 8.2, "wake": 4.4, "leap": [0.0, F, 3.8], "leapAt": 5.6, "beats": [
            {"at": 0.0, "wires": [{"target": "lamps", "do": "set:0"}], "sound": "sfx_prespawnLastBossGate", "shake": 0.3,
             "message": "$dungeons_fight_start", "where": [0.0, F, -12.0]},
            {"at": 1.2, "sound": "sfx_ship_impact", "shake": 0.5, "where": bow},
            {"at": 2.2, "wires": [{"target": "lamps", "do": "set:1"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -17.5]},
            {"at": 3.0, "wires": [{"target": "lamps", "do": "set:2"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -6.0]},
            {"at": 3.8, "wires": [{"target": "lamps", "do": "set:3"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, 9.0]},
            {"at": 4.4, "fx": "vfx_spawn_large", "sound": "sfx_draugr_alerted", "shake": 0.6, "banner": True, "where": seat},
            {"at": 4.5, "fx": "fx_WaterImpact_Big", "sound": "fx_leviathan_reaction", "where": seat},
            {"at": 8.2, "message": "$dungeons_boss_drowned_jarl_hint"}]},
        "abilities": [
            {"id": "brineslam", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 5, "scale": 2.5, "push": 40,
             "fx": "fx_WaterImpact_Big", "phases": [0, 2]},
            {"id": "brine", "kind": "rain", "every": 7, "first": 3, "warn": 1.8, "radius": 2.2, "scale": 1.5, "push": 10, "count": 2,
             "fx": "fx_WaterImpact_Big", "phases": [1, 2]},
            {"id": "thralls", "kind": "summon", "every": 15, "first": 6, "warn": 2.0, "adds": {"melee": 1}, "points": sluices,
             "phases": [1]},
            {"id": "undertow", "kind": "charge", "every": 10, "first": 4, "warn": 1.3, "width": 3, "length": 22, "scale": 2.5,
             "push": 50, "fx": "fx_WaterImpact_Big", "phases": [2]},
        ],
        "weakTo": [{"mech": "anchor", "state": 1, "area": [5.4, 4.0, 5.4], "areaAt": [0.0, 2.0, 0.0], "share": 0.1, "stagger": True,
                    "breaks": False, "hurt": 2.0, "held": "$dungeons_boss_drowned_jarl_held"}],
        "phases": [
            {"at": 0.66, "wires": undertow_on, "adds": {"melee": 2}, "leap": seat, "message": "$dungeons_boss_drowned_jarl_p1",
             "sound": "fx_leviathan_reaction", "shake": 0.6,
             "shield": {"take": 0, "until": [{"mech": "sluiceW", "state": 1}, {"mech": "sluiceE", "state": 1}], "seconds": 120,
                        "times": 2, "hold": True, "breakShare": 0.1, "breakWires": undertow_off,
                        "hint": "$dungeons_boss_drowned_jarl_shield", "flicker": "$dungeons_boss_drowned_jarl_flicker"}},
            {"at": 0.3, "healTo": 0.55, "enrage": 1.3, "wires": undertow_on + [{"target": f"stone{i}", "do": "set:1"} for i in range(1, 5)],
             "adds": {"melee": 1, "ranged": 1}, "message": "$dungeons_boss_drowned_jarl_p2", "sound": "sfx_gdking_scream", "shake": 0.8},
        ],
        "death": {"seconds": 4.5, "beats": [
            {"at": 0.0, "wires": undertow_off + [{"target": "anchor", "do": "set:1"}],
             "sound": "sfx_Bonemass_death", "shake": 1.0, "banner": True},
            {"at": 1.5, "sound": "sfx_ship_destroyed", "fx": "vfx_Destroyed_VikingShip", "where": bow, "shake": 0.5}]},
    }
    encounter(r, "jarl", (-IN_X, F, -IN_Z + 3.5), (IN_X, F + 10.0, IN_Z), "enter", [seat] + aisles,
              lock=["ingate", "ingateE"], opens=["ingate", "ingateE", "outgate", "outgateW"], reached_from="in", music="boss_bonemass", boss=boss)
    return r


def throne_cap():
    """Closes the Tide Throne's way to the vault if a layout ever leaves it open (in a good one the vault always takes it)."""
    r = Room("throne_cap", (8, 10, 0), end_cap=True, weight=0.01)
    thr = -10 / 2 + T.floor_t
    r.doorway("in", (0, thr, 0), (0, 0, 1), type=THRONE, door=False)
    r.fill(STONE, (0, thr + T.door_h / 2, 0.45), (T.door_w + 0.4, T.door_h + 0.2, 0.8))
    return r
