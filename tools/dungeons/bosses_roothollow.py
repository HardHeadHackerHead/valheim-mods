"""
Rootdeep Hollow's boss rooms, made with the boss toolkit (mods/Dungeons/BossModel.cs; skill/bosses.md): the Sap-Warden's hollow (a
mini boss) and the Rotheart's chamber (the final boss). rootdeep_rooms.py has the other hand-made rooms; the theme
(authoring/themes/roothollow.py) names both files' rooms as its signatures.

The two fights ask different things of the players:
  - the Sap-Warden: a seed run. Driven down to 60% it goes back to the Mother Root and drinks (shielded, held still); a seed pod across
    the hollow bursts and drops a glowing seed; carry it, under spore rain and with its rootlings after you, to the root's mouth (a
    socket at the Mother Root's foot, beside the boss) and the root drinks the seed instead: the shield breaks;
  - the Rotheart: a burning, while the chamber closes in. At 70% the root walls slide in from the four diagonals and it withdraws to
    its heart behind a rot-shield; three rot-nodes round the rim must burn at once (each has a sap-fire bloom beside it: pull it and
    sap fire pours into the node; a torch or a fire arrow works too); at 30% it recovers, enraged, the walls close in again and it
    burrows through the floor at you.
Both: every boss stands on a dais stepped down in 0.2 m on every side (boss_rooms.step_dais: rule one), creature_check proves it
reaches the players in every state the fight sets.
"""
import math

import numpy as np

from dungeon_parts import box, side_point
from boss_rooms import step_dais
from shifting_halls import encounter

import rootdeep_rooms as rr


def _rh():
    import authoring.themes.roothollow as rh
    return rh


def lit(part, states):
    """A mechanism's part whose light and fire burn only in these states (its model stays)."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


def shown(part, states):
    """A mechanism's part shown only in these states (what a state adds: a pod that bursts is gone after)."""
    plain = part.json
    part.json = lambda: dict(plain(), show=list(states))
    return part


def lamps(r, id, groups, prefab, scale=1.0, start=None):
    """Lights a fight puts out and brings back in turn: one mechanism with a state per group (0 all dark, 1 the first group lit, 2 the
    first two...), each group's parts lit from its state on. A platform with identical poses (it never moves and joins nothing)."""
    m = r.mech(id, "platform", (0, 0, 0))
    n = len(groups)
    for k, spots in enumerate(groups, start=1):
        for at, yaw, s in spots:
            lit(m.stand(prefab, at, yaw=yaw, scale=(s * scale, s * scale, s * scale)), list(range(k, n + 1)))
    for _ in range(n + 1):
        m.pose()
    m.seconds = 0.0
    m.start = n if start is None else start
    return m


# ---------------------------------------------------------------- the Sap-Warden's hollow (mini boss)

def warden_hollow(t):
    """The Sap-Warden's hollow, a round trunk-hollow 28 m across. The Mother Root stands against the east wall from floor to roof, its
    sap veins glowing up it; the Sap-Warden waits bound to it on a stepped mound of roots. At the root's foot, north of the mound, its
    mouth: a socket of roots (a pedestal for a seed). Across the hollow, high on the west side, a great seed pod hangs.
      intro    step in: root walls grow shut over both ways, the fungus light dies; sap climbs the Mother Root in three pulses; the
               warden tears itself off the root in a burst (the banner) and leaps down.
      phase 1  its blows, and a root-stomp (a red ring round it).
      at 60%   it goes back to the Mother Root and drinks: shielded, held still. The seed pod bursts and drops a glowing seed under it;
               spores fall on the players (red rings), rootlings come. Carry the seed to the root's mouth: the root drinks the seed,
               the shield breaks, it staggers and takes a blow (10% of its health).
      at 30%   a second wind: it recovers to 55%, enraged (faster, harder blows), and lashes along a marked lane at you.
      death    it withers back into the root, the sap drains, the fungus light comes back, the root walls wither open."""
    rh = _rh()
    size = (28.0, 16.0, 28.0)
    F = -size[1] / 2 + t.floor_t
    sh = rr.shell(t, "sapwarden", size, [("in", 2, 0.0, F), ("out", 0, 0.0, F)], shape="round", tags=["warden"], max=1,
                  min_depth=3)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    t.gate(r, "ingate", 2, 14.0, F, 1, "in")
    t.gate(r, "outgate", 0, 14.0, F, 0, "out")
    # the Mother Root against the east wall: a great trunk floor to roof, roots flaring from it into the wall and the floor
    rh.stick(r, rh.OAK, (9.6, F - 0.5, 0.0), (9.0, size[1] / 2 - 0.5, 0.4), 4.2, spin=30)
    for yaw, y, z in ((80, F + 1.0, -3.6), (100, F + 1.0, 3.6), (90, F + 9.0, 0.0)):
        box(r, rh.ARC, (10.2, y, z), (5.0, 2.4, 1.6), yaw=yaw)
    # the mound it waits on: heartwood in 0.2 m steps on every side
    top = step_dais(r, rh.BACK, (5.0, F, 0.0), (5.0, 6.0), 0.8)          # (dark heartwood; a symmetric slab keeps its edges exact)
    seat = [5.0, float(top[1]), 0.0]
    # sap climbing the root: amber bulbs in three bands, lit as the intro brings the sap up (and dark again at its death)
    sap = lamps(r, "sap", [[((7.4, F + 2.4, -1.0), 0, 1.0), ((7.4, F + 3.0, 1.1), 0, 1.0)],
                           [((7.3, F + 5.0, 0.9), 0, 1.2), ((7.3, F + 5.8, -0.8), 0, 1.2)],
                           [((7.2, F + 8.4, 0.3), 0, 1.4), ((7.2, F + 9.6, -0.6), 0, 1.4)]], rh.SAP, start=0)
    # the fungus light round the walls: dies at the intro, comes back at its death
    ring = [((math.sin(math.radians(a)) * 10.6, F, math.cos(math.radians(a)) * 10.6), a + 180, 0.85) for a in (35, 145, 215, 325, 270)]
    lamps(r, "glow", [ring], rh.MUSH)
    # the root's mouth: a socket of roots at the Mother Root's foot, north of the mound; the seed goes in it
    mouth = r.mech("mouth", "pedestal", (7.4, F, 4.6))
    mouth.stand("root08", (-0.4, 0.0, 0.0), yaw=30, scale=(1.0, 1.4, 1.0))
    mouth.stand("root08", (0.4, 0.0, 0.2), yaw=210, scale=(1.0, 1.4, 1.0))
    mouth.fill(rh.HEART, (0, 0.45, 0), (0.9, 0.9, 0.9))
    mouth.stand(rh.CANDLE, (0.0, 0.9, -0.3), scale=(1.8, 1.8, 1.8))
    mouth.extra.update({"item": "heartseed", "accepts": ["heartseed"], "itemAt": [0, 1.0, 0], "fx": ["sfx_staff_elder_grow"]})
    # the seed pod high on the west side: it bursts (gone in state 1) and the seed falls to the floor under it
    pod = r.mech("pod", "platform", (-8.6, F, 0.0))
    shown(pod.add(rh.POD, (0, 7.2, 0), (0, 40, 0), (1.3, 1.3, 1.3)), [0])
    rh.stick(r, rh.SHOOT, (-8.6, F + 9.6, 0.0), (-8.6, size[1] / 2 - 1.0, 0.0), 0.3)
    pod.pose().pose()
    pod.seconds = 0.0
    seed_at = [-8.6, F + 0.4, 0.0]
    r.extra.setdefault("triggers", []).append(
        {"id": "seedfall", "on": {"event": "state", "mech": "pod", "state": 1}, "repeat": True, "solver": "ignore",
         "do": [{"item": "heartseed", "at": seed_at}, {"sound": "sfx_GuckSackDestroyed", "at": [-8.6, F + 7.2, 0.0]},
                {"message": "$dungeons_rootdeep_seed_fell", "to": "room"}]})
    r.extra["triggers"].append(
        {"id": "seeddrunk", "on": {"event": "place", "mech": "mouth", "item": "heartseed"}, "repeat": True, "solver": "ignore",
         "do": [{"set": "sap", "to": 0}, {"sound": "sfx_greydwarf_shaman_heal", "at": [7.4, F + 1.0, 4.6]}]})
    # the hollow's dressing: roots across the floor by the walls, mushrooms, vines (the shell's), a fallen trunk
    for x, z, yaw in ((-6.0, -8.5, 30), (-10.6, 7.6, 120), (2.0, 10.4, 200)):
        r.stand("root12", (x, F, z), yaw=yaw, scale=(1.4, 1.4, 1.4))
    box(r, rh.ARC, (-3.0, size[1] / 2 - 2.6, 0.0), (20.0, 2.6, 2.0), yaw=90)
    boss = {
        "name": "$dungeons_boss_rootdeep_sapwarden", "title": "$dungeons_boss_rootdeep_sapwarden_title", "role": "elite", "fight": 55, "perPlayer": 0.5,
        "scale": 1.5, "mini": True,
        "look": {"light": [1.0, 0.62, 0.18], "range": 6, "aura": "vfx_Smoked", "enraged": [0.75, 1.0, 0.3]},
        "intro": {"seconds": 6.6, "wake": 4.4, "leap": [1.0, F, 0.0], "leapAt": 5.4, "beats": [
            {"at": 0.0, "wires": [{"target": "glow", "do": "set:0"}, {"target": "sap", "do": "set:0"}, {"target": "pod", "do": "set:0"}],
             "sound": "sfx_staff_elder_grow", "shake": 0.25, "message": "$dungeons_fight_start", "where": [0.0, F, -11.0]},
            {"at": 1.4, "wires": [{"target": "sap", "do": "set:1"}], "sound": "sfx_greydwarf_shaman_heal", "where": [8.0, F + 1.0, 0.0]},
            {"at": 2.4, "wires": [{"target": "sap", "do": "set:2"}], "sound": "sfx_greydwarf_shaman_heal", "where": [8.0, F + 5.0, 0.0]},
            {"at": 3.4, "wires": [{"target": "sap", "do": "set:3"}], "sound": "sfx_greydwarf_shaman_heal", "where": [8.0, F + 9.0, 0.0]},
            {"at": 4.4, "fx": "fx_Abomination_arise", "sound": "sfx_abomination_arise_end", "shake": 0.5, "banner": True, "where": seat},
            {"at": 6.6, "message": "$dungeons_boss_rootdeep_sapwarden_hint"}]},
        "abilities": [
            {"id": "stomp", "kind": "slam", "every": 10, "first": 5, "warn": 1.6, "radius": 4.5, "scale": 2.2, "push": 30,
             "fx": "vfx_gdking_stomp", "sound": "sfx_gdking_stomp"},
            {"id": "spores", "kind": "rain", "every": 7, "first": 2, "warn": 1.8, "radius": 2.2, "scale": 1.2, "push": 5, "count": 2,
             "damage": "poison", "fx": "vfx_BombBlob_explode_poison", "sound": "sfx_GuckSackDestroyed", "cue": "sfx_GuckSackHit",
             "phases": [1, 2]},
            {"id": "rootlings", "kind": "summon", "every": 16, "first": 4, "warn": 2.0, "adds": {"melee": 1},
             "points": [[-6.0, F, -6.5], [-6.0, F, 6.5]], "phases": [1]},
            {"id": "lash", "kind": "charge", "every": 11, "first": 3, "warn": 1.4, "width": 2.6, "length": 16, "scale": 2.2, "push": 40,
             "cue": "sfx_staff_elder_root_swing", "sound": "sfx_staff_elder_root_hit", "phases": [2]},
        ],
        "phases": [
            {"at": 0.6, "leap": seat, "wires": [{"target": "pod", "do": "set:1"}], "adds": {"melee": 1}, "sound": "sfx_gdking_scream",
             "shake": 0.5, "message": "$dungeons_boss_rootdeep_sapwarden_p1",
             "shield": {"take": 0, "until": [{"mech": "mouth", "state": 1}], "seconds": 75, "hold": True, "breakShare": 0.1,
                        "hint": "$dungeons_boss_rootdeep_sapwarden_shield"}},
            {"at": 0.3, "healTo": 0.55, "enrage": 1.2, "adds": {"melee": 1}, "message": "$dungeons_boss_rootdeep_sapwarden_p2",
             "sound": "sfx_gdking_scream", "shake": 0.5},
        ],
        "death": {"seconds": 3.6, "beats": [
            {"at": 0.0, "sound": "sfx_tree_fall", "fx": "vfx_firetreecut_dead_abomination", "shake": 0.6, "banner": True},
            {"at": 1.2, "wires": [{"target": "sap", "do": "set:0"}], "sound": "sfx_greydwarf_shaman_heal", "where": [8.0, F + 4.0, 0.0]},
            {"at": 2.4, "wires": [{"target": "glow", "do": "set:1"}], "sound": "sfx_secretfound"}]},
    }
    spawns = [seat, [-7.0, F, -5.5], [-7.0, F, 5.5], [-2.0, F, 9.5]]
    encounter(r, "warden", (-12.0, F, -12.0), (12.0, F + 10.0, 12.0), "enter", [], spawns, lock=["ingate", "outgate"],
              opens=["ingate", "outgate"], reached_from="in", elite_level=3, music="boss_gdking", boss=boss)
    return r


# ---------------------------------------------------------------- the Rotheart's chamber (the final boss)

RH_SIZE = (40.0, 22.0, 40.0)
NODES = {"rotA": (-12.7, -4.6), "rotB": (12.7, -4.6), "rotC": (4.6, -12.7)}
BLOOMS = {"rotA": ((-10.0, -3.6), 270.0), "rotB": ((10.0, -3.6), 90.0), "rotC": ((3.6, -9.8), 180.0)}
WALLS = {"wallNE": 45.0, "wallSE": 135.0, "wallSW": 225.0, "wallNW": 315.0}
WALL_R = (16.0, 12.5, 10.0)          # how far a closing wall stands from the middle, in each state


def rot_node(t, r, id, at, lever_at, lever_yaw, burn=28):
    """A rot-node: a gnarled black knot over a heap of dead wood (a brazier: the heap's fire burns only while it's lit). A sap-fire
    bloom stands beside it (a lever with no wires of its own: a trigger sends fire into the node, as often as it's pulled; never
    one-shot); a torch in hand (E) or a fire arrow lights it too."""
    rh = _rh()
    b = r.mech(id, "brazier", at)
    b.stand("bonfire", (0, 0, 0), scale=(0.55, 0.55, 0.55))
    b.stand(rh.GNARL, (0, -0.2, 0), yaw=at[0] * 17, scale=(0.75, 0.32, 0.75))
    b.pose()
    b.seconds = 0.2
    b.extra.update({"burn": burn, "fx": ["vfx_FireAddFuel", "sfx_FireAddFuel"]})
    lever = id + "bloom"
    t.lever(r, lever, lever_at, lever_yaw, [])
    r.extra.setdefault("triggers", []).append(
        {"id": id + "fire", "on": {"event": "lever", "mech": lever}, "do": [{"set": id, "to": 1}], "repeat": True})
    return b


def heart_chamber(t):
    """The Rotheart's chamber: the heart of the tree, a round hollow 40 m across, rotten through. On the north side the heart itself, a
    black swollen knot of rot that great roots grow into from the walls; before it, a stepped mound where the Rotheart waits. Three
    rot-nodes stand round the rim (south of west, south of east, east of south), each with its sap-fire bloom. Four walls of root stand against
    the trunk at the diagonals, and over the floor hangs a canopy of roots. The way to the vault is the east door or the west one.
      intro    step in: the root wall grows shut behind, the fungus light dies; the heart beats three times (the floor shakes);
               the rot-nodes flare up sickly; the heart splits and the Rotheart comes out of it (the banner) and leaps down.
      phase 1  its blows, a stomp (red ring), spores falling on the players (red rings, poison).
      at 70%   the chamber tightens: the root walls slide in from the diagonals and the canopy sinks (the message says it first); it
               withdraws to the mound before its heart, shielded and held still; rootlings come. Burn all three rot-nodes at once
               (each burns 28 s): the shield breaks, it staggers and takes a blow (8% of its health).
      at 30%   a second wind: it recovers to 55%, enraged; the walls close in again; it burrows through the floor at you along a
               marked lane.
      death    the heart cracks, the walls draw back and the canopy lifts, the light floods back, the way to the vault opens."""
    rh = _rh()
    size = RH_SIZE
    F = -size[1] / 2 + t.floor_t
    sh = rr.shell(t, "rotheart", size, [("in", 2, 0.0, F), ("out", 1, 0.0, F), ("out2", 3, 0.0, F)], shape="round", tags=["boss"],
                  max=1)
    r = sh.room
    r.doorways[0]["type"] = rr.HEART_TYPE
    r.doorways[1]["type"] = rr.VAULT_TYPE
    r.doorways[2]["type"] = rr.VAULT_TYPE
    r.extra["joinBy"] = ["in"]
    t.gate(r, "ingate", 2, 20.0, F, 1, "in")
    t.gate(r, "outgate", 1, 20.0, F, 0, "out")
    t.gate(r, "outgate2", 3, 20.0, F, 0, "out2")
    # the heart: a swollen black knot of rot against the north wall, great roots growing into it from the walls and the roof
    box(r, rh.GNARL, (0.0, F + 4.5, 16.0), (7.0, 9.0, 4.4), yaw=180)
    r.stand(rh.TAR, (0.0, F - 0.2, 15.8), scale=(0.28, 0.06, 0.18))
    for yaw, x, z in ((150, -6.0, 14.0), (210, 6.0, 14.0), (180, 0.0, 15.5)):
        box(r, rh.ARC, (x, F + 11.0, z), (11.0, 3.2, 1.8), yaw=yaw)
    heart = r.mech("heart", "platform", (0.0, F, 13.8))
    lit(heart.add(rh.POD, (0, 3.0, 0), (0, 0, 0), (2.2, 2.0, 2.0)), [0])
    heart.pose().pose()
    heart.seconds = 0.0
    # the mound it waits on, stepped on every side
    top = step_dais(r, rh.BACK, (0.0, F, 9.0), (9.0, 6.0), 1.0)          # (dark heartwood; a symmetric slab keeps its edges exact)
    seat = [0.0, float(top[1]), 9.0]
    # the rot-nodes round the rim, each with its sap-fire bloom
    for id, (x, z) in NODES.items():
        (lx, lz), yaw = BLOOMS[id]
        rot_node(t, r, id, (x, F, z), (lx, F, lz), yaw)
    # the walls of root that close in: at the diagonals, three trunks and a skin of lattice each, sliding in along their radius
    for id, a in WALLS.items():
        ar = math.radians(a)
        out = np.array([math.sin(ar), 0.0, math.cos(ar)])
        m = r.mech(id, "platform", (out[0] * WALL_R[0], F, out[2] * WALL_R[0]), (0, (a + 180) % 360, 0))
        box(m, rh.LATTICE, (0, 4.2, -0.6), (6.2, 8.4, 0.5))
        box(m, rh.LATTICE, (0, 4.2, 0.0), (6.2, 8.4, 0.5), yaw=180)
        for i, roll in enumerate((55.0, -55.0)):                 # (two great roots grown across each other over the lattice)
            box(m, rh.ARC, (0, 4.2, 0.7 + 0.5 * i), (8.6, 2.4, 0.8), tilt=(0, roll))
        for d in WALL_R:
            m.pose(at=(0, 0, WALL_R[0] - d))
        m.seconds = 5.0
        m.start = 0
    # the canopy of roots over the floor: it sinks as the chamber tightens (never lower than 7 m over the floor)
    canopy = r.mech("canopy", "platform", (0.0, F, 0.0))
    for yaw, z in ((0, -4.0), (60, 2.0), (120, 0.0)):
        box(canopy, rh.ARC, (0.0, 0.0, z), (26.0, 2.8, 1.6), yaw=yaw)
    for y in (17.0, 13.5, 11.0):
        canopy.pose(at=(0, y, 0))
    canopy.seconds = 5.0
    # the fungus light round the walls: dies at the intro, floods back at the death
    ring = [((math.sin(math.radians(a)) * 16.6, F, math.cos(math.radians(a)) * 16.6), a + 180, 0.9) for a in (20, 160, 200, 340)]
    lamps(r, "glow", [ring], rh.MUSH)
    for x, z in ((-9.0, -15.5), (9.5, 9.5), (-15.0, 6.5)):
        r.stand("root08", (x, F, z), yaw=x * 20, scale=(1.5, 1.4, 1.5))
    boss = {
        "name": "$dungeons_boss_rootdeep_rotheart", "title": "$dungeons_boss_rootdeep_rotheart_title", "role": "elite", "fight": 200, "perPlayer": 0.5,
        "scale": 1.9,
        "look": {"light": [0.55, 0.95, 0.35], "range": 8, "aura": "vfx_Poison", "enraged": [1.0, 0.35, 0.15]},
        "intro": {"seconds": 8.0, "wake": 5.2, "leap": [0.0, F, 1.0], "leapAt": 6.4, "beats": [
            {"at": 0.0, "wires": [{"target": "glow", "do": "set:0"}, {"target": "heart", "do": "set:0"}], "sound": "sfx_prespawnLastBossGate",
             "shake": 0.3, "message": "$dungeons_fight_start", "where": [0.0, F, -16.0]},
            {"at": 1.2, "sound": "sfx_gdking_stomp", "shake": 0.35, "where": [0.0, F + 3.0, 14.0]},
            {"at": 2.2, "sound": "sfx_gdking_stomp", "shake": 0.45, "where": [0.0, F + 3.0, 14.0]},
            {"at": 3.2, "sound": "sfx_gdking_stomp", "shake": 0.55, "where": [0.0, F + 3.0, 14.0]},
            {"at": 4.0, "wires": [{"target": n, "do": "set:1"} for n in NODES], "sound": "sfx_FireAddFuel"},
            {"at": 5.2, "wires": [{"target": "heart", "do": "set:1"}], "fx": "fx_Abomination_arise", "sound": "sfx_gdking_alert",
             "shake": 0.7, "banner": True, "where": seat},
            {"at": 8.0, "message": "$dungeons_boss_rootdeep_rotheart_hint"}]},
        "abilities": [
            {"id": "stomp", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 5.5, "scale": 2.5, "push": 40,
             "fx": "vfx_gdking_stomp", "sound": "sfx_gdking_stomp", "phases": [0, 1, 2]},
            {"id": "spores", "kind": "rain", "every": 8, "first": 3, "warn": 1.8, "radius": 2.4, "scale": 1.4, "push": 5, "count": 3,
             "damage": "poison", "fx": "vfx_BombBlob_explode_poison", "sound": "sfx_GuckSackDestroyed", "cue": "sfx_GuckSackHit",
             "phases": [0, 1, 2]},
            {"id": "rootlings", "kind": "summon", "every": 15, "first": 5, "warn": 2.0, "adds": {"melee": 1},
             "points": [[-8.0, F, 2.0], [9.0, F, 3.0], [-4.0, F, -11.5]], "phases": [1]},
            {"id": "burrow", "kind": "charge", "every": 10, "first": 3, "warn": 1.4, "width": 3.0, "length": 22, "scale": 2.6, "push": 50,
             "cue": "sfx_staff_elder_root_swing", "sound": "sfx_staff_elder_root_hit", "fx": "fx_gdking_rootspawn", "phases": [2]},
        ],
        "phases": [
            {"at": 0.7, "leap": seat, "wires": [{"target": w, "do": "set:1"} for w in WALLS] + [{"target": "canopy", "do": "set:1"}],
             "adds": {"melee": 2}, "sound": "sfx_gdking_scream", "shake": 0.6, "message": "$dungeons_boss_rootdeep_rotheart_p1",
             "shield": {"take": 0, "until": [{"mech": n, "state": 1} for n in NODES], "seconds": 100, "hold": True,
                        "breakShare": 0.08, "hint": "$dungeons_boss_rootdeep_rotheart_shield"}},
            {"at": 0.3, "healTo": 0.55, "enrage": 1.3,
             "wires": [{"target": w, "do": "set:2"} for w in WALLS] + [{"target": "canopy", "do": "set:2"}],
             "adds": {"melee": 1, "ranged": 1}, "message": "$dungeons_boss_rootdeep_rotheart_p2", "sound": "sfx_gdking_scream", "shake": 0.8},
        ],
        "death": {"seconds": 5.0, "beats": [
            {"at": 0.0, "wires": [{"target": "heart", "do": "set:1"}], "sound": "sfx_gdking_death", "fx": "vfx_firetreecut_dead_abomination",
             "shake": 1.0, "banner": True},
            {"at": 1.4, "wires": [{"target": w, "do": "set:0"} for w in WALLS] + [{"target": "canopy", "do": "set:0"}],
             "sound": "sfx_tree_fall", "shake": 0.5, "where": [0.0, F + 3.0, 14.0]},
            {"at": 3.0, "wires": [{"target": "glow", "do": "set:1"}], "sound": "sfx_secretfound"}]},
    }
    spawns = [seat, [-8.0, F, 2.0], [9.0, F, 3.0], [-4.0, F, -11.5]]
    encounter(r, "rotheart", (-17.5, F, -17.5), (17.5, F + 12.0, 17.5), "enter", [], spawns, lock=["ingate"],
              opens=["ingate", "outgate", "outgate2"], reached_from="in", elite_level=3, music="boss_gdking", boss=boss)
    return r


def signatures(t):
    return {"sapwarden": lambda: warden_hollow(t), "rotheart": lambda: heart_chamber(t)}
