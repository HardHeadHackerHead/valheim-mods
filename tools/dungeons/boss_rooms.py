"""
Shifting Halls' two boss rooms, made with the boss toolkit (mods/Dungeons/BossModel.cs; skill/bosses.md): the guardian's hall (the Cog-Thrall,
a mini boss) and the Clockwork Warden's hall. shifting_halls.py takes both from here (`from boss_rooms import warden_hall, guardian_room`).

What a boss room needs (the lesson of the first Warden, stuck on its dais: Valheim's creature navmesh steps 0.3 m at most, and the dais
was two 0.6 m tiers):
  - every raised place a boss or elite starts on is stepped down to the floor in 0.2 m steps on every side (step_dais), and
    creature_check.py proves each spawn point can walk to the floor the fight is entered from;
  - a set piece the fight uses: the Thrall's hammer that players drop on it, the Warden's braziers that break its shield, its hammers
    and rising blocks;
  - an intro (the gates slam, the lights die and come back, the boss wakes with a banner and leaps down) and a death moment.
"""
import numpy as np

from dungeon_parts import slab, box
from giant_kit import Shell, STONE, DARK, PILLAR, COLUMN, BLOCK, TORCH, torch, crystal, wisp, brazier, gear, machine, bar, giant_lever

BRAZIER_BLUE = "MountainKit_brazier_blue"
RISE, TREAD = 0.2, 0.5          # a dais step: under the navmesh's 0.3 m for every creature, and easy for players


def step_dais(r, prefab, centre, size, height):
    """A dais of `height` on the floor under `centre` (x, floor y, z), `size` (x, z) across at its foot, stepped down on all four sides
    in RISE steps TREAD deep: a ziggurat every creature can walk down. Returns the top's middle."""
    x, y, z = centre
    n = int(round(height / RISE))
    sx, sz = size
    for k in range(n):
        w, d = sx - 2 * TREAD * k, sz - 2 * TREAD * k
        top = y + RISE * (k + 1)
        r.fill(prefab, (x, (y + top) / 2, z), (w, top - y, d))
    return np.array([x, y + RISE * n, z])


def lit(part, states):
    """A mechanism's part whose fire and light burn only in these states ("lit": dungeon-triggers' one-model rule: the model stays,
    only its flame, light and crackle come and go). dungeon_parts' Part writes no "lit", so this part's own json adds it."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


def lamps(r, id, groups):
    """The torches a fight puts out and lights again in turn: one mechanism with a state per group (0 all dark, 1 the first group lit,
    2 the first two...), each group's giant torches "lit" from its state on. A platform with identical poses (it never moves and joins
    nothing: the solver leaves it out), switching at once, lit to the full as the dungeon stands."""
    m = r.mech(id, "platform", (0, 0, 0))
    n = len(groups)
    for k, spots in enumerate(groups, start=1):
        for at in spots:
            lit(m.stand(TORCH, at, scale=(1.3, 1.3, 1.3)), list(range(k, n + 1)))
    for _ in range(n + 1):
        m.pose()
    m.seconds = 0.0
    m.start = n
    return m


def fire_bowl(r, id, at, lever_at, burn=15):
    """A brazier that breaks the Warden's shield: a grey block with a blue brazier on it (one model: its fire comes and goes with its
    state), burning `burn` s once lit. The room supplies the fire: a giant's lever beside it (pulled from -z) sends a jet of flame into
    it, as often as it's pulled (a trigger: the lever has no wires of its own, so it's never one-shot). A torch in hand (E) or a fire
    arrow lights it too, as a shortcut."""
    b = r.mech(id, "brazier", at)
    b.fill(BLOCK, (0, 0.5, 0), (1.4, 1.0, 1.4))
    b.stand(BRAZIER_BLUE, (0, 1.0, 0), scale=(1.4, 1.4, 1.4))
    b.pose()
    b.seconds = 0.2
    b.extra.update({"burn": burn, "fx": ["vfx_FireAddFuel", "sfx_FireAddFuel"]})
    lever = id + "lever"
    giant_lever(r, lever, lever_at, 0, [])
    r.extra.setdefault("triggers", []).append(
        {"id": id + "fire", "on": {"event": "lever", "mech": lever}, "do": [{"set": id, "to": 1}], "repeat": True})
    return b


def warden_hall():
    """The Clockwork Warden's hall, between the great hall and the vault: a vast floor, the Warden on a stepped dais before the vault's
    gate. The fight (the boss toolkit):
      intro    step in: the gate slams, every torch in the hall gutters out; they come back in pairs from the door to the dais; the
               Warden wakes on its dais in a burst with the banner (its name and title) and the music, and leaps down into the hall.
      phase 1  its own blows, and a ground slam (a red ring round it, 1.6 s).
      at 66%   "the machine answers": it leaps back to its dais and is shielded; the hammers over the side lanes start falling, the
               gears race, stone falls under each player, servants come to guard the braziers. Light both braziers together, twice
               (each one's lever stands in a hammer's lane: pull it between blows; a torch or a fire arrow lights it too; they burn
               15 s): the first time the shield flickers and the braziers go out, the second it breaks, it staggers, takes a blow
               (8% of its health) and comes down.
      at 30%   a second wind: it recovers to 50% in a surge of light, and is enraged (faster, harder blows, hot light); blocks rise
               out of the floor, and it charges down a marked lane.
      death    the hammers stop, the gears grind to a halt, the banner, then the vault's gate opens and the trophy drops.
    The hammers hurt whoever is under them when they land, the Warden too. Its health is set from its fight length ("fight": 200 s of
    one player of the land hitting it all the time), the toughest in the dungeon: the gate checks it's over twice the guardian's."""
    from shifting_halls import WD_SIZE, WD_FLOOR, VAULT, WARDEN, door_gate, encounter
    F = WD_FLOOR
    s = Shell("warden", WD_SIZE, [("in", 2, 0, F), ("out", 0, 0, F)], tags=["boss"], max=1, floor=False, door_type=VAULT)
    r = s.r
    r.doorways[1]["type"] = WARDEN
    r.extra["joinBy"] = ["in"]
    slab(r, -18, 18, -20, 20, F, STONE, thickness=F + WD_SIZE[1] / 2, cell=10.0)
    door_gate(r, "ingate", 2, 20.0, F, start=1, doorway="in")
    door_gate(r, "outgate", 0, 20.0, F, start=0, doorway="out")
    # the dais: a black ziggurat before the vault's gate, 1.2 m in 0.2 m steps on every side
    top = step_dais(r, DARK, (0, F, 10.5), (15.0, 9.0), 1.2)
    for x in (-3.0, 3.0):
        crystal(r, (x, top[1], top[2] + 1.6))
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
    # the lights the intro puts out and brings back in pairs, from the door to the dais (lit as the hall stands)
    lamps(r, "lamps", [[(x, F, z) for x, z in ((-8.0, -17.0), (8.0, -17.0), (-15.0, -17.5), (15.0, -17.5))],
                       [(x, F, -9.0) for x in (-14.0, 14.0)],
                       [(x, F, 12.0) for x in (-15.0, 15.0)] + [(x, top[1], top[2] + 0.8) for x in (-4.2, 4.2)]])
    # the braziers that break its shield, each with its lever at the edge of a hammer's lane: whoever pulls it stands where the hammer's
    # blast lands (its red ring), so it's pulled between blows. 20 m apart, burning 15 s: both lit together, twice (the shield's "times")
    fire_bowl(r, "brazierL", (-10.0, F, 5.4), (-10.0, F, 2.6))
    fire_bowl(r, "brazierR", (10.0, F, 5.4), (10.0, F, 2.6))
    # the machinery the fight wakes: gears (state 0 turning, 1 racing, 2 faster, 3 still)
    gears = machine(r, "gears")
    gears.extra["spinScale"] = [1, 2.5, 4.0, 0]
    for x, sign in ((-17.2, 1), (17.2, -1)):
        gear(gears, (x, F + 12.0, 0.0), 10.0, axis="z", yaw=90, thickness=0.8, turn=6 * sign)
        gear(gears, (x, F + 7.0, -8.5), 5.0, axis="z", yaw=90, thickness=0.6, turn=-12 * sign)
    gear(gears, (0, F + 13.5, 19.0), 8.0, axis="z", thickness=0.7, turn=9)
    # the hammers over the side lanes: once woken (state 1) they fall every few seconds, to 0.4 m over the floor
    for i, x in enumerate((-10.0, 10.0)):
        h = r.mech(f"crusher{i + 1}", "platform", (x, F, -2.0))
        box(h, BLOCK, (0, 0.4 + 1.5, 0), (6.0, 3.0, 5.0))
        for bx in (-2.2, 2.2):
            bar(h, (bx, 3.4, 0), (bx, 7.0, 0), 0.6)
        h.pose(at=(0, 8.5, 0)).pose()
        h.seconds = 1.2                                           # (a red ring shows where it lands while it falls)
        h.cycle = 3.5 + 0.7 * i
        h.extra["cycleIn"] = [1]
        for bx, bz in ((-3.6, -3.0), (3.6, -3.0), (-3.6, 3.0), (3.6, 3.0)):
            bar(r, (x + bx, F, -2.0 + bz), (x + bx, F + 14.0, -2.0 + bz), 0.5)
        r.fill(DARK, (x, F + 0.01, -2.0), (6.4, 0.02, 5.4))                                 # its mark on the floor
    # the blocks that rise at the last phase (cover from falling stone)
    for i, (x, z) in enumerate(((-12.0, -13.0), (12.0, -13.0), (-5.0, -7.0), (5.0, -7.0))):
        b = r.mech(f"riser{i + 1}", "platform", (x, F, z))
        b.fill(STONE, (0, -1.4, 0), (4.0, 3.6, 4.0))              # its top 0.4 m over the floor, 3.4 m when risen
        b.pose().pose(at=(0, 3.0, 0))
        b.seconds = 3.0
    for x, z in ((-6.0, -6.0), (6.0, -6.0), (0.0, 3.0)):
        brazier(r, (x, F + 8.0, z), chain=WD_SIZE[1] / 2 - 1.0 - (F + 8.0 + 1.6))
    wisp(r, (0, F + 9.0, -14.0))

    seat = [0.0, top[1], 11.0]
    alcoves = [(x * 15.6, F, z) for x in (-1, 1) for z in (-6.0, 6.0)]
    hammers = [{"target": "crusher1", "do": "set:1"}, {"target": "crusher2", "do": "set:1"}]
    guards = [(-13.0, F, 3.5), (13.0, F, 3.5), (-7.0, F, 3.5), (7.0, F, 3.5)]      # (by the braziers: servants that guard them)
    boss = {
        "name": "$dungeons_boss_warden", "title": "$dungeons_boss_warden_title", "role": "elite", "fight": 200, "perPlayer": 0.5,
        "scale": 1.7,
        "look": {"light": [0.45, 0.75, 1.0], "range": 7, "aura": "vfx_Burning_blue", "enraged": [1.0, 0.45, 0.2]},
        "intro": {"seconds": 8.4, "wake": 4.0, "leap": [0.0, F, 1.0], "leapAt": 5.2, "beats": [
            {"at": 0.0, "wires": [{"target": "lamps", "do": "set:0"}],
             "sound": "sfx_prespawnLastBossGate", "shake": 0.3, "message": "$dungeons_fight_start", "where": [0.0, F, -12.0]},
            {"at": 1.6, "wires": [{"target": "lamps", "do": "set:1"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -17.0]},
            {"at": 2.6, "wires": [{"target": "lamps", "do": "set:2"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -9.0]},
            {"at": 3.6, "wires": [{"target": "lamps", "do": "set:3"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, 12.0]},
            {"at": 4.0, "fx": "vfx_spawn_large", "sound": "sfx_gdking_alert", "shake": 0.5, "banner": True, "where": seat},
            {"at": 4.1, "fx": "fx_eikthyr_stomp", "where": seat}]},
        "abilities": [
            {"id": "slam", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 5, "scale": 2.5, "push": 40, "phases": [0, 2]},
            {"id": "debris", "kind": "rain", "every": 6.5, "first": 3, "warn": 1.8, "radius": 2.2, "scale": 1.5, "push": 10, "count": 2,
             "phases": [1, 2]},
            {"id": "servants", "kind": "summon", "every": 14, "first": 6, "warn": 2.0, "adds": {"melee": 1}, "points": guards,
             "phases": [1]},
            {"id": "charge", "kind": "charge", "every": 11, "first": 4, "warn": 1.3, "width": 3, "length": 20, "scale": 2.5, "push": 50,
             "phases": [2]},
        ],
        "weakTo": [{"mech": f"crusher{i}", "state": 1, "area": [6.0, 4.0, 7.2], "areaAt": [0.0, 1.0, 0.6], "share": 0.06,
                    "stagger": True, "breaks": False, "hurt": 2.0} for i in (1, 2)],
        "phases": [
            {"at": 0.66, "wires": hammers + [{"target": "gears", "do": "set:1"}], "adds": {"melee": 2}, "leap": seat,
             "sound": "sfx_gdking_scream", "shake": 0.6,
             "shield": {"take": 0, "until": [{"mech": "brazierL", "state": 1}, {"mech": "brazierR", "state": 1}], "seconds": 110,
                        "times": 2, "hold": True, "breakShare": 0.08, "hint": "$dungeons_boss_warden_shield",
                        "flicker": "$dungeons_boss_warden_flicker"}},
            # a second wind: driven down to 30%, it tears free, recovers to 50% and is enraged
            {"at": 0.3, "healTo": 0.5, "wires": [{"target": f"riser{i}", "do": "set:1"} for i in range(1, 5)] + [{"target": "gears", "do": "set:2"}],
             "adds": {"melee": 1, "ranged": 1}, "enrage": 1.25, "message": "$dungeons_boss_warden_p3", "sound": "sfx_gdking_scream",
             "shake": 0.8},
        ],
        "death": {"seconds": 4.0, "beats": [
            {"at": 0.0, "wires": [{"target": "crusher1", "do": "set:0"}, {"target": "crusher2", "do": "set:0"}],
             "sound": "sfx_gdking_death", "shake": 1.0, "banner": True},
            {"at": 1.2, "wires": [{"target": "gears", "do": "set:3"}], "sound": "sfx_rock_destroyed", "fx": "vfx_RockDestroyed_large",
             "where": [0.0, F, 0.0], "shake": 0.5}]},
    }
    encounter(r, "warden", (-17.0, F, -17.0), (17.0, F + 12.0, 19.0), "enter", [], [seat] + alcoves,
              lock=["ingate"], opens=["ingate", "outgate"], reached_from="in", music="boss_frozenking", boss=boss)
    return r


def guardian_room():
    """The guardian before the great machine (a mini boss): a long, high hall with room to fight. Step past the threshold and both
    gates shut; the Cog-Thrall wakes on its stepped dais with a banner, leaps down and comes for you. Its iron hide turns three blows
    in four. In the middle of the hall a giant's hammer hangs over a black plate (an amber ring round it in the fight), nothing round
    it to path about, with a lever 6.8 m out on each side: lure the Thrall onto the plate and pull, chains snap round it and hold it,
    and the hammer falls (slowly enough to roll out from under it): it staggers, takes a heavy blow, is thrown out from under it, and
    its hide is broken for 12 s. Pull again to raise the hammer. It slams the floor round itself now and then (a red ring); driven
    down to 30% it has a second wind, back to 55%, enraged. When it falls the way on opens (and the side door, where the shortcut
    back ends)."""
    from shifting_halls import door_gate, encounter
    s = Shell("guardian", (24, 16, 32), [("in", 2, 0, -7), ("out", 0, 0, -7), ("side", 1, 8.0, -7)], tags=["guardian"], weight=1.0,
              max=1, min_depth=3)
    r = s.r
    r.extra["joinBy"] = ["in"]
    door_gate(r, "ingate", 2, 16.0, -7, start=1, doorway="in")
    door_gate(r, "outgate", 0, 16.0, -7, start=0, doorway="out")
    # a side door, shut until the guardian falls: where the shortcut back to the entrance ends (Layout puts it there or nearby)
    door_gate(r, "sidegate", 1, 12.0, -7, start=0, doorway="side", open_dir=-1, lateral=8.0)
    top = step_dais(r, DARK, (0, -7, 10.5), (10.0, 6.0), 0.8)                         # the dais it waits on, stepped
    for z in (-10.0, -2.0, 6.0):
        for x in (-9.0, 9.0):
            r.stand(PILLAR, (x, -7, z), scale=(0.6, 1.6, 0.6))
            torch(r, (x * 0.8, -7, z + 2.0))
    box(r, "giant_helmet2", (-9.5, -5.6, 12.0), (3.0, 2.8, 3.2), yaw=200)
    r.stand("skull_pile", (9.0, -7, 12.5), scale=(1.6, 1.6, 1.6))
    r.stand("bone_stack", (-9.6, -7, -13.0), yaw=30, scale=(1.5, 1.5, 1.5))
    for x in (-5.0, 5.0):
        brazier(r, (x, 0.0, 0.0), chain=4.5)
    wisp(r, (0, 3.0, 10.0))
    # the hammer: a grausten block between four iron rails, over a dark mark in the middle of the hall; up 5.4 m, down 0.4 m over the floor
    hz = -1.0
    # the target: a black marble plate with a carved grausten rim on the grey floor, and in the fight an amber ring round it (the
    # toolkit's weakTo mark). Nothing stands within 4.5 m of its middle (creature_check: open on every side): the hammer's four iron
    # rails hang from the ceiling and end above it, and its levers stand 6.8 m out
    r.fill(STONE, (0, -7 + 0.03, hz), (5.6, 0.06, 5.6))
    r.fill(DARK, (0, -7 + 0.05, hz), (4.6, 0.06, 4.6))
    for bx, bz in ((-2.8, -2.8), (2.8, -2.8), (-2.8, 2.8), (2.8, 2.8)):
        bar(r, (bx, 1.0, hz + bz), (bx, 8.0, hz + bz), 0.45)
    h = r.mech("hammer", "platform", (0, -7, hz))
    box(h, BLOCK, (0, 0.4 + 1.25, 0), (4.4, 2.5, 4.4))
    gear(h, (0, 3.0, 0), 2.6, axis="y", thickness=0.4)
    h.pose(at=(0, 5.0, 0)).pose()
    h.seconds = 1.4                                                                     # a slow, heavy fall: time to roll out
    for side, yaw in ((-1, 270), (1, 90)):                                              # (pulled from the target's side)
        giant_lever(r, f"hammerlever{'w' if side < 0 else 'e'}", (side * 6.8, -7, hz), yaw, [("hammer", "next")])
    seat = [0.0, float(top[1]), 10.5]
    boss = {
        "name": "$dungeons_boss_thrall", "title": "$dungeons_boss_thrall_title", "role": "elite", "fight": 45, "perPlayer": 0.5,
        "scale": 1.4, "mini": True, "armour": 0.25, "armourOff": 12,
        "look": {"light": [1.0, 0.62, 0.3], "range": 5, "aura": "vfx_Smoked"},
        "intro": {"seconds": 5.6, "wake": 1.2, "leap": [0.0, -7.0, 4.5], "leapAt": 2.6, "beats": [
            {"at": 0.0, "message": "$dungeons_fight_start", "sound": "sfx_prespawn", "where": seat, "shake": 0.2},
            {"at": 1.2, "fx": "vfx_spawn_large", "sound": "sfx_skeleton_big_alerted", "shake": 0.4, "banner": True, "where": seat},
            {"at": 5.6, "message": "$dungeons_boss_thrall_hint"}]},
        "abilities": [
            {"id": "slam", "kind": "slam", "every": 12, "first": 6, "warn": 1.6, "radius": 4, "scale": 2, "push": 35}],
        "weakTo": [{"mech": "hammer", "state": 1, "area": [6.0, 5.0, 6.0], "areaAt": [0.0, 1.5, 0.0], "share": 0.12, "stagger": True,
                    "held": "$dungeons_boss_thrall_held",
                    "breaks": True, "hurt": 2.0}],
        # a second wind: driven down to 30%, it recovers to 55% and is enraged
        "phases": [{"at": 0.3, "healTo": 0.55, "enrage": 1.2, "adds": {"melee": 1}, "message": "$dungeons_boss_thrall_p2",
                    "sound": "sfx_gdking_scream", "shake": 0.5}],
        "death": {"seconds": 2.5, "beats": [{"at": 0.0, "sound": "sfx_skeleton_big_death", "shake": 0.6, "banner": True}]},
    }
    encounter(r, "guardian", (-11.0, -7, -11.0), (11.0, 3, 15.0), "enter", [], [seat, (-8.0, -7.0, -6.0), (8.0, -7.0, -6.0)],
              lock=["ingate", "outgate"], opens=["ingate", "outgate", "sidegate"], reached_from="in", elite_level=3,
              music="boss_gdking", boss=boss)
    return r

