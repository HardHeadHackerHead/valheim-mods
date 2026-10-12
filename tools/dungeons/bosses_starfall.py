"""
The Starfall Orrery's boss rooms (pack "starfall"), made with the boss toolkit (skill/bosses.md):

  astrolabe   the Astrolabe Keeper's chamber (a mini boss): a round hall under the watchers' great astrolabe, its hoops tumbling over a
              stepped dais. In the middle of the floor a sighting-lens hangs over a pale ring: once the fight is on it drops a lance of
              starlight onto the ring every 12 s (a red ring shows where while it falls). Lure the Keeper onto the ring as it strikes:
              it's held, staggered, takes a heavy blow and its star-iron hide breaks for a while. Players under it are hurt too.
  eclipse     the Sun-Eater's dome (the final boss), before the Star-Heart: a captured sun blazes in the dome, and a black moon hangs
              ready beside it. The fight darkens and brightens the hall in stages; at two thirds of its health the moon covers the sun
              (totality): every lamp dies, and the Sun-Eater, shielded, channels on its dais. Three sun-mirrors stand round the hall,
              each turned a quarter by its crank: when all three throw their light onto the dais at once, the shield breaks (twice: the
              first time it shrugs and knocks the mirrors askew), the light comes back and it takes a heavy blow. At 30% it recovers
              half its strength in a surge, enraged, and the moon passes over again (half light).

What a boss room needs (boss_rooms.py's lesson): every raised place a boss starts on is stepped down to the floor in 0.2 m steps
(step_dais), and creature_check.py proves each spawn point can walk to the floor the fight is entered from; a weakTo that breaks
needs open floor round its mark.
"""
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, r3
from boss_rooms import step_dais
from shifting_halls import encounter
from authoring.core import rng_for
from authoring.shells import Shell
from authoring.themes.orrery import (THEME as T, ASHLAR, MARBLE, BAND, STAR, SPARK, CRYSTAL, DISC, SHARD, TORCH, SUN, METEOR, TABLET,
                                     ring, orbit, spoke, hang_star, crystal_plinth, star_crank, post)
from orrery_rooms import lit, HEART



def shown(part, states):
    """A mechanism's part that appears only in these states (what a state adds: a beam of light)."""
    plain = part.json
    part.json = lambda: dict(plain(), show=list(states))
    return part


def lamps(r, id, groups):
    """The hall's lamps, one mechanism with a state per group: 0 all dark, 1 the first group lit, 2 the first two... (pale flames on
    black marble feet; only the flame and its light come and go). Lit to the full as the hall stands."""
    m = r.mech(id, "platform", (0, 0, 0))
    n = len(groups)
    for k, spots in enumerate(groups, start=1):
        for x, y, z in spots:
            box(r, BAND, (x, y + 0.15, z), (0.8, 0.3, 0.8))
            lit(m.stand(TORCH, (x, y + 0.3, z), scale=(1.7, 1.7, 1.7)), list(range(k, n + 1)))
    for _ in range(n + 1):
        m.pose()
    m.seconds = 0.0
    m.start = n
    return m


def round_hall(name, size, doors, **kw):
    sh = Shell(T, name, "round", size, rng_for(name), **kw)
    for id, k in doors:
        sh.add_door(id, k, 0.0, 0)
    sh.build()
    return sh


# ---------------------------------------------------------------- the Astrolabe Keeper (mini boss)

def astrolabe():
    """The Astrolabe Keeper's chamber: a round hall 32 m across. Step past the threshold and the gates shut; the astrolabe over the dais
    races, a star falls on the dais and the Keeper wakes there with a banner, leaps down and comes for you. Its star-iron hide turns
    three blows in four. The sighting-lens over the pale ring in the middle strikes every 12 s: lure the Keeper onto the ring as the
    lance falls (a red ring shows where), and it's held, staggered, takes a heavy blow and its hide breaks for 12 s. It slams the floor
    round itself (a red ring); driven down to 30% it has a second wind, back to 55%, enraged, and stars fall under you. When it falls
    the way on opens, and the side door where the shortcut back ends."""
    S = (32.0, 18.0, 32.0)
    sh = round_hall("astrolabe", S, [("in", 2), ("out", 0), ("side", 1)], tags=["astrolabe"], max=1, min_depth=3)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F, H = sh.floor_y, S[0] / 2
    top = sh.top - T.floor_t
    T.gate(r, "ingate", 2, H, F, 1, "in", kind="platform")     # (shut only during the fight: a platform, which the solver needn't follow)
    T.gate(r, "outgate", 0, H, F, 0, "out")
    T.gate(r, "sidegate", 1, H, F, 0, "side", open_dir=-1)
    # the dais the Keeper waits on (stepped), under the astrolabe: three hoops tumbling round a standing rim and its turning rete
    dais = step_dais(r, BAND, (0, F, 8.5), (9.0, 6.0), 0.8)
    seat = [0.0, float(dais[1]), 8.5]
    rings = r.mech("astrolabe", "machine", (0, 0, 0))
    rings.extra["spinScale"] = [1, 2.6, 4.0, 0]
    cy = F + 9.0
    ring(rings, (0, cy, 8.5), 7.0, yaw=0, turn=8)
    ring(rings, (0, cy, 8.5), 5.6, yaw=60, turn=-11)
    ring(rings, (0, cy, 8.5), 4.4, yaw=120, turn=15)
    orbit(r, (0, cy, 12.6), 3.6, width=0.3, axis="z")
    spoke(rings, (0, cy, 12.5), 6.8, axis="z", turn=12)
    box(r, DISC, (0, cy, 12.8), (1.6, 1.6, 0.4))
    gk.bar(r, (0, top, 8.5), (0, cy + 1.0, 8.5), 0.4)
    r.stand(SHARD, (0, cy - 0.7, 8.5), scale=(1.8, 1.8, 1.8))
    r.add(SPARK, (0, cy, 8.5))
    # the sighting-lens: a crystal lance on a silver shaft high in the dome; once woken, it drops onto its ring every 12 s
    lz = -2.0
    orbit(r, (0, F, lz), 2.6, width=0.22, n=20, inlay=True)
    box(r, BAND, (0, F + 0.01, lz), (2.4, 0.06, 2.4), yaw=45)
    lens = r.mech("lens", "platform", (0, F, lz))
    lens.stand(STAR, (0, 0.2, 0), scale=(0.9, 0.9, 0.9))
    lens.stand(CRYSTAL, (0, 0.9, 0), scale=(2.0, 2.0, 2.0))
    lens.add(SPARK, (0, 1.2, 0))
    gk.bar(lens, (0, 2.2, 0), (0, 5.5, 0), 0.25)
    up = top - F - 6.3
    lens.pose(at=(0, up, 0)).pose(at=(0, up, 0)).pose(at=(0, 0.1, 0)).pose(at=(0, up, 0))   # idle, up, STRIKE, up
    lens.seconds = 1.6                                          # (it falls slowly enough to roll out from under it)
    lens.cycle = 4.0
    lens.extra["cycleIn"] = [1, 2, 3]
    for x, z in ((-2.6, lz - 2.6), (2.6, lz - 2.6), (-2.6, lz + 2.6), (2.6, lz + 2.6)):
        gk.bar(r, (x, top, z), (x, top - 3.5, z), 0.3)             # (the lens's guides: from the dome, ending high above the ring)
    # the hall: pale flames round the wall, star tablets of the watchers, hung stars
    for i in range(6):
        a = math.radians(30 + 60 * i)
        x, z = math.sin(a) * 12.5, math.cos(a) * 12.5
        if z > 4.0:
            continue
        T.light_stand(r, (x, F, z))
    for x in (-6.5, 6.5):
        T.light_stand(r, (x, F, 11.0))
        r.stand(TABLET, (x * 1.45, F, 7.5), yaw=90 if x < 0 else 270)
    for x, z in ((-8.0, -6.0), (8.0, -6.0)):
        hang_star(r, (x, F + 10.0, z), scale=0.65)

    boss = {
        "name": "$dungeons_boss_starfall_keeper", "title": "$dungeons_boss_starfall_keeper_title", "role": "elite", "fight": 50, "perPlayer": 0.5,
        "scale": 1.5, "mini": True, "armour": 0.25, "armourOff": 12,
        "look": {"light": [0.72, 0.82, 1.0], "range": 5, "aura": "vfx_Frost", "enraged": [0.75, 0.45, 1.0]},
        "intro": {"seconds": 6.0, "wake": 2.2, "leap": [0.0, F, 3.5], "leapAt": 3.4, "beats": [
            {"at": 0.0, "message": "$dungeons_fight_start", "sound": "sfx_prespawn", "where": seat, "shake": 0.2},
            {"at": 0.8, "wires": [{"target": "astrolabe", "do": "set:1"}], "sound": "sfx_fader_bell", "where": [0.0, cy, 8.5]},
            {"at": 2.0, "fx": "fx_fimbulvinter_meteor_hit", "sound": "sfx_fader_spawn_meteor_impact", "where": seat, "shake": 0.4},
            {"at": 2.2, "fx": "vfx_spawn_large", "sound": "sfx_skeleton_big_alerted", "banner": True, "where": seat},
            {"at": 5.0, "wires": [{"target": "lens", "do": "set:1"}]},
            {"at": 6.0, "message": "$dungeons_boss_starfall_keeper_hint"}]},
        "abilities": [
            {"id": "slam", "kind": "slam", "every": 11, "first": 6, "warn": 1.6, "radius": 4, "scale": 2, "push": 35},
            {"id": "starfall", "kind": "rain", "every": 12, "first": 4, "warn": 1.8, "radius": 2.2, "scale": 1.4, "push": 10, "count": 1,
             "damage": "lightning", "cue": "sfx_fader_spawn_meteor_arrival", "sound": "sfx_fader_spawn_meteor_impact",
             "fx": "fx_fimbulvinter_meteor_hit", "phases": [1]}],
        "weakTo": [{"mech": "lens", "state": 2, "area": [5.0, 4.0, 5.0], "areaAt": [0.0, 1.5, 0.0], "share": 0.12, "stagger": True,
                    "breaks": True, "hurt": 2.0}],
        "phases": [{"at": 0.3, "healTo": 0.55, "enrage": 1.2, "adds": {"melee": 1}, "message": "$dungeons_boss_starfall_keeper_p2",
                    "wires": [{"target": "astrolabe", "do": "set:2"}], "sound": "sfx_gdking_scream", "shake": 0.5}],
        "death": {"seconds": 3.0, "beats": [
            {"at": 0.0, "wires": [{"target": "lens", "do": "set:0"}, {"target": "astrolabe", "do": "set:3"}],
             "sound": "sfx_frozenking_death_crystal_break", "shake": 0.6, "banner": True}]},
    }
    encounter(r, "keeper", (-13.0, F, -11.0), (13.0, F + 10.0, 13.0), "enter", [], [seat, (-8.0, F, 2.0), (8.0, F, 2.0)],
              lock=["ingate"], opens=["ingate", "outgate", "sidegate"], reached_from="in", elite_level=3, music="boss_eikthyr",
              boss=boss)
    return r


# ---------------------------------------------------------------- the Sun-Eater (final boss)

# the mirrors: (id, where (x, z), the state that throws its light on the dais)
MIRRORS = (("mirrorW", (-11.0, -3.0), 1), ("mirrorE", (11.0, -3.0), 3), ("mirrorS", (-5.0, -10.0), 2))


def sun_mirror(r, id, at, aligned, target):
    """A sun-mirror: a pale crystal in a standing silver ring on a black turntable, turned a quarter at a time by its crank. In its
    aligned state it faces the dais: its crystal kindles and a beam of light runs from it to the dais (shown only then)."""
    x, y, z = at
    d = np.array(target, float) - np.array(at, float)
    face = math.degrees(math.atan2(d[0], d[2]))                 # the yaw that turns +z towards the dais
    m = r.mech(id, "rotator", at, (0, (face - 90 * aligned) % 360, 0))
    box(m, BAND, (0, 0.4, 0), (2.8, 0.8, 2.8))
    post(m, (0, 1.4, 0), (0.7, 1.2, 0.7))
    orbit(m, (0, 3.6, 0), 2.3, width=0.3, axis="z", n=20, pale=True)
    m.stand(CRYSTAL, (0, 2.0, 0.1), scale=(2.6, 2.6, 2.6))
    lit(m.add(SPARK, (0, 3.6, 0.3)), [aligned])
    # the beam: a line of warm light from the crystal to the dais while the mirror is aimed (the toolkit's "beams"), with sparks
    # drifting at the crystal
    m.extra["beams"] = [{"from": [0.0, 3.6, 0.3], "fromMech": id, "to": [round(float(target[0]), 2), round(float(target[1]) + 1.5, 2),
                                                                       round(float(target[2]), 2)],
                         "states": [aligned], "colour": [1.0, 0.88, 0.55], "width": 0.35, "light": 4}]
    for k in range(4):
        m.pose(euler=(0, 90 * k, 0))
    m.seconds = 1.0
    return m


def eclipse():
    """The Sun-Eater's dome, 44 m across, before the Star-Heart. Step in and the gate shuts behind; every lamp dies, then comes back
    ring by ring from the door to the dais, the captured sun blazes, and the Sun-Eater wakes on its dais with the banner and leaps
    down. It slams and charges down marked lanes. At two thirds of its health the black moon slides over the sun: totality. The
    lamps die, it leaps back to its dais and channels there, shielded (nothing hurts it), shades come for you and stars fall under
    you. Turn the three sun-mirrors round the hall (a crank by each, a quarter turn a pull) until all three throw their light onto
    the dais at once: the first time it shrugs it off and knocks them askew, the second the shield breaks, it staggers and takes a
    heavy blow, and the light comes back. At 30% it recovers to half its strength in a surge, enraged, and the moon passes over
    again (half light): it charges and stars rain. When it falls, the moon slides away, the sun blazes, and the way to the
    Star-Heart opens."""
    S = (44.0, 26.0, 44.0)
    sh = round_hall("eclipse", S, [("in", 2), ("out", 0)], tags=["boss"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    r.doorways[1]["type"] = HEART
    r.doorways[1]["door"] = False
    F, H = sh.floor_y, S[0] / 2
    top = sh.top - T.floor_t
    T.gate(r, "ingate", 2, H, F, 1, "in", kind="platform")     # (shut only during the fight: a platform, which the solver needn't follow)
    T.gate(r, "outgate", 0, H, F, 0, "out")
    # the dais (stepped), before the way out
    dais = step_dais(r, BAND, (0, F, 11.5), (12.0, 8.0), 1.2)
    seat = [0.0, float(dais[1]), 11.5]
    for x in (-4.0, 4.0):
        crystal_plinth(r, (x, float(dais[1]), 13.5))
    # the captured sun in the dome: a shard of the fallen star in tumbling hoops, its light (state 1) dies in totality (state 0)
    sun_at = (0.0, F + 11.5, 3.0)
    gk.bar(r, (0, top, 3.0), (0, sun_at[1] + 2.5, 3.0), 0.6)
    cage = T.machine(r, "suncage")                             # (great hoops tumbling round it: from F + 6.5 up, out of reach)
    for d, yaw, turn, w in ((10.0, 0, 7, 0.55), (8.5, 60, -10, 0.5), (7.0, 120, 13, 0.45)):
        ring(cage, sun_at, d, yaw=yaw, turn=turn, width=w)
    r.stand(SHARD, (sun_at[0], sun_at[1] - 2.2, sun_at[2]), scale=(5.5, 5.5, 5.5))
    sun = r.mech("sun", "platform", (0, 0, 0))
    lit(sun.add(SUN, (0.3, sun_at[1] - 0.6, sun_at[2])), [1])
    lit(sun.add(SPARK, sun_at), [1])
    sun.pose().pose()
    sun.seconds = 0.0
    sun.start = 1
    # the black moon: a dark disc hanging by the wall (state 0), slid under the sun in an eclipse (state 1)
    moon = r.mech("moon", "platform", (0, 0, 0))
    box(moon, DISC, (sun_at[0], sun_at[1] - 5.8, sun_at[2]), (8.0, 8.0, 0.8), tilt=(90, 0))
    orbit(moon, (sun_at[0], sun_at[1] - 5.4, sun_at[2]), 4.2, width=0.35, n=24, pale=True)
    moon.pose(at=(-12.5, 3.0, 0)).pose()
    moon.seconds = 4.0
    # the lamps: three rings of pale flames, from the door to the dais
    lamps(r, "lamps", [[(x, F, -15.0) for x in (-8.0, 8.0)] + [(-14.5, F, -9.5), (14.5, F, -9.5)],
                       [(x, F, 2.0) for x in (-17.5, 17.5)] + [(-12.5, F, 9.0), (12.5, F, 9.0)],
                       [(x, float(dais[1]), 14.5) for x in (-2.0, 2.0)] + [(-9.0, F, 15.5), (9.0, F, 15.5)]])
    # the sun-mirrors and their cranks (each crank in front of its mirror, the player facing the mirror)
    aim = (0.0, float(dais[1]), 11.5)
    until = []
    for id, (x, z), a in MIRRORS:
        sun_mirror(r, id, (x, F, z), a, aim)
        d = np.array([x - aim[0], z - aim[2]])
        d /= np.linalg.norm(d)
        cx, cz = x - d[0] * 3.5, z - d[1] * 3.5                    # (between the mirror and the dais, pulled from the dais's side)
        yaw = round(math.degrees(math.atan2(d[0], d[1])) / 90.0) * 90.0 % 360
        star_crank(r, id + "crank", (round(cx * 2) / 2 + 0.25, F, round(cz * 2) / 2 + 0.25), yaw, [(id, "next")], reached_from="in")
        until.append({"mech": id, "state": a})
    # the observatory round it: the great orbit ring at F + 8 with its planets (the moon's way across the sky), a star-map in the
    # floor (rings and the eight ways of the sky, flush), stars hung under the dome
    orbit(r, (0, F + 8.0, 0), 16.5, width=0.5, pale=True)
    for i, (look, a) in enumerate((("disc", 30), ("star", 110), ("crystal", 200), ("disc", 290))):
        x, z = math.sin(math.radians(a)) * 16.5, math.cos(math.radians(a)) * 16.5
        if look == "disc":
            box(r, DISC, (x, F + 8.0 - 1.6, z), (2.4, 2.4, 0.8), yaw=a)
        elif look == "star":
            r.stand(STAR, (x, F + 8.0 - 2.6, z), scale=(1.4, 1.4, 1.4))
        else:
            box(r, DISC, (x, F + 8.0 - 1.2, z), (1.6, 1.6, 0.6), yaw=a + 90)
        gk.bar(r, (x, F + 8.0, z), (x, F + 8.0 - 0.6, z), 0.2)
    for rr in (7.0, 13.5):
        orbit(r, (0, F, 0), rr, width=0.3, inlay=True)
    for i in range(8):
        a = math.radians(45 * i + 22.5)
        a0, a1 = 7.4, 13.1
        mx, mz = math.sin(a) * (a0 + a1) / 2, math.cos(a) * (a0 + a1) / 2
        box(r, ASHLAR, (mx, F + 0.01, mz), (0.25, 0.06, a1 - a0), yaw=math.degrees(a))
    for i in range(12):
        a = math.radians(30 * i + 15)
        rr = 18.0 if i % 2 else 12.0
        hang_star(r, (math.sin(a) * rr, top - 2.5 - (i % 3) * 1.5, math.cos(a) * rr), scale=0.8, spark=i % 4 == 0)
    for x in (-17.0, 17.0):
        r.stand(TABLET, (x, F, -2.0), yaw=90 if x < 0 else 270)

    alcoves = [(-15.0, F, 4.0), (15.0, F, 4.0), (-8.0, F, -10.0), (8.0, F, -10.0)]
    dark = [{"target": "moon", "do": "set:1"}, {"target": "sun", "do": "set:0"}, {"target": "lamps", "do": "set:0"}]
    boss = {
        "name": "$dungeons_boss_starfall_suneater", "title": "$dungeons_boss_starfall_suneater_title", "role": "elite", "fight": 240, "perPlayer": 0.5,
        "trophy": "",                                           # (no Warden's trophy: the Star-Heart beyond holds the reward)
        "scale": 1.75,
        "look": {"light": [0.62, 0.45, 1.0], "range": 7, "aura": "vfx_Smoked", "enraged": [1.0, 0.72, 0.3]},
        "intro": {"seconds": 8.0, "wake": 4.2, "leap": [0.0, F, 3.0], "leapAt": 5.4, "beats": [
            {"at": 0.0, "wires": [{"target": "lamps", "do": "set:0"}, {"target": "sun", "do": "set:0"}],
             "sound": "sfx_prespawnLastBossGate", "shake": 0.3, "message": "$dungeons_fight_start", "where": [0.0, F, -12.0]},
            {"at": 1.4, "wires": [{"target": "lamps", "do": "set:1"}], "sound": "sfx_fader_bell", "where": [0.0, F, -14.0]},
            {"at": 2.4, "wires": [{"target": "lamps", "do": "set:2"}], "sound": "sfx_fader_bell", "where": [0.0, F, 2.0]},
            {"at": 3.4, "wires": [{"target": "lamps", "do": "set:3"}, {"target": "sun", "do": "set:1"}], "sound": "sfx_StaffLightning_charge",
             "where": list(sun_at)},
            {"at": 4.2, "fx": "vfx_spawn_large", "sound": "sfx_dragon_scream", "shake": 0.5, "banner": True, "where": seat},
            {"at": 4.3, "fx": "fx_fimbulvinter_meteor_hit", "where": seat}]},
        "abilities": [
            {"id": "slam", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 5, "scale": 2.5, "push": 40, "phases": [0, 2]},
            {"id": "charge", "kind": "charge", "every": 11, "first": 6, "warn": 1.3, "width": 3, "length": 20, "scale": 2.5, "push": 50,
             "phases": [0, 2]},
            {"id": "starfall", "kind": "rain", "every": 7, "first": 3, "warn": 1.8, "radius": 2.4, "scale": 1.5, "push": 10, "count": 2,
             "damage": "lightning", "cue": "sfx_fader_spawn_meteor_arrival", "sound": "sfx_fader_spawn_meteor_impact",
             "fx": "fx_fimbulvinter_meteor_hit", "phases": [1, 2]},
            {"id": "shades", "kind": "summon", "every": 16, "first": 5, "warn": 2.0, "adds": {"flying": 1}, "points": alcoves,
             "phases": [1]},
        ],
        "phases": [
            {"at": 0.66, "wires": dark, "adds": {"melee": 2}, "leap": seat, "sound": "sfx_dragon_scream", "shake": 0.6,
             "message": "$dungeons_boss_starfall_suneater_totality",
             "shield": {"take": 0, "until": until, "seconds": 120, "times": 2, "hold": True, "breakShare": 0.08,
                        "breakWires": [{"target": "moon", "do": "set:0"}, {"target": "sun", "do": "set:1"},
                                       {"target": "lamps", "do": "set:3"}],
                        "hint": "$dungeons_boss_starfall_suneater_shield", "flicker": "$dungeons_boss_starfall_suneater_flicker"}},
            # a second wind: driven down to 30%, it tears the light away again, recovers to 50% and is enraged
            {"at": 0.3, "healTo": 0.5, "wires": [{"target": "moon", "do": "set:1"}, {"target": "lamps", "do": "set:1"}],
             "adds": {"melee": 1, "ranged": 1}, "enrage": 1.25, "message": "$dungeons_boss_starfall_suneater_p3", "sound": "sfx_dragon_scream",
             "shake": 0.8},
        ],
        "death": {"seconds": 5.0, "beats": [
            {"at": 0.0, "wires": [{"target": "moon", "do": "set:0"}], "sound": "sfx_dragon_death", "shake": 1.0, "banner": True},
            {"at": 1.6, "wires": [{"target": "sun", "do": "set:1"}, {"target": "lamps", "do": "set:3"}], "sound": "sfx_fader_bell",
             "fx": "fx_fimbulvinter_meteor_hit", "where": list(seat), "shake": 0.4}]},
    }
    encounter(r, "suneater", (-19.0, F, -18.0), (19.0, F + 14.0, 19.0), "enter", [], [seat] + alcoves,
              lock=["ingate"], opens=["ingate", "outgate"], reached_from="in", music="boss_moder", boss=boss)
    return r


def boss_rooms():
    return {"astrolabe": astrolabe, "eclipse": eclipse}
