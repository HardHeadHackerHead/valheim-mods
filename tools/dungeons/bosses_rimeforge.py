"""
Rimeforge Depths' two boss rooms, made with the boss toolkit (skill/bosses.md; the theme authoring/themes/rimeforge.py names them in
its signatures()):

  keeper       the Bellows-Keeper (a mini boss), in the bellows hall: two giant bellows on the side walls blast fire down a lane each
  greatforge   the Rimesmith (the final boss), in the Great Forge: drop-hammer presses, a hearth, a channel of glacier ice

Every raised place a boss starts on is stepped down in 0.2 m steps (boss_rooms.step_dais), and creature_check.py proves the bosses can
walk to the players and onto their lure targets.
"""
import numpy as np

from dungeon_parts import box, bounds, r3
from boss_rooms import step_dais
from authoring.themes.rimeforge import (ICE, STONE, IRON, RIB, BARS, post, EMBER, LAMP, COAL, ANVIL, ICESPIKE, FLOORGRATE, VAULT_TYPE,
                                        anvil, hang_bowl, iron)
from rimeforge_rooms import deep_shell, lit, FURNACE, FIREPIT
from shifting_halls import encounter


def lamps(r, id, groups, prefab=FIREPIT, scale=1.2):
    """Fires a fight puts out and lights again in turn: one mechanism with a state per group (0 all dark, 1 the first group lit, 2 the
    first two...), each group's fire pits "lit" from its state on (a platform with identical poses: it never moves and joins nothing).
    Lit to the full as the dungeon stands."""
    m = r.mech(id, "platform", (0, 0, 0))
    n = len(groups)
    for k, spots in enumerate(groups, start=1):
        for at in spots:
            lit(m.stand(prefab, at, scale=(scale, scale, scale)), list(range(k, n + 1)))
    for _ in range(n + 1):
        m.pose()
    m.seconds = 0.0
    m.start = n
    return m


def bellows(r, id, x_wall, z, side, F):
    """A giant's bellows on a side wall (side -1 west, +1 east), its nozzle pointing across the hall along x: two black stone boards
    bound in iron, the top one heaving down (state 1) to blast. A platform the triggers set to 1 and back to 0 after 2.5 s."""
    m = r.mech(id, "platform", (x_wall, F, z), (0, 90 if side < 0 else 270, 0))
    # (its frame: +z out from the wall into the hall)
    box(m, STONE, (0, 1.1, 1.6), (4.6, 0.4, 3.4))                                      # the top board
    box(m, IRON, (0, 1.35, 1.6), (4.8, 0.15, 0.4))
    box(m, IRON, (0, 1.35, 0.4), (4.8, 0.15, 0.4))
    m.pose(at=(0, 1.2, 0), euler=(-14, 0, 0)).pose(at=(0, 0.2, 0), euler=(0, 0, 0))
    m.seconds = 0.9
    # the fixed parts: the bottom board on a stone block, the leather sides (dark stone), the iron nozzle into the hall
    box(r, STONE, (x_wall - side * 1.6, F + 0.5, z), (3.4, 1.0, 4.6))
    box(r, STONE, (x_wall - side * 1.6, F + 1.4, z), (3.0, 0.8, 4.0))
    iron(r, (x_wall - side * 3.0, F + 1.3, z), (x_wall - side * 3.6, F + 1.1, z), 0.8)
    return m


# ---------------------------------------------------------------- the Bellows-Keeper (mini boss)

KH_SIZE = (24, 14, 36)


def keeper_hall(t):
    """The bellows hall, before the deep works: a long hall with a cold hearth at the north end, two giant bellows on the side walls,
    each blowing down a lane marked in black iron across the floor. Step in: both gates slam, the hearth's fires die; the
    Bellows-Keeper wakes on its stepped hearth with a banner and leaps down.
      phase 1  its blows and a ground slam (a red ring, 1.6 s); now and then it heaves at its chains and both bellows blast their lanes
               (an inhale first, the red ring round it: get out of the lanes). Its frost-crusted hide turns three blows in four.
               The room's answer: lure it into a lane and pull that bellows' lever (beside the lane): the blast holds it, cracks its
               crust (armour off for 12 s), staggers it and burns it (10%). Whoever stands in the lane is burnt too.
      at 30%   a second wind: back to 55%, enraged; the bellows run wild (they blast every few seconds); two more come.
      death    the bellows sigh out, the hearth's fires come back, both gates open."""
    sh = t.shell("keeper", KH_SIZE, [("in", 2, 0.0), ("out", 0, 0.0), ("side", 1, -12.0), ("w", 3, 12.0)], tags=["keeper"],
                 max=1, min_depth=3)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    H = np.array(KH_SIZE) / 2
    W = t.wall_t
    t.gate(r, "ingate", 2, H[2], F, 1, "in")
    t.gate(r, "outgate", 0, H[2], F, 0, "out")
    t.gate(r, "sidegate", 1, H[0], F, 0, "side", lateral=-12.0)
    t.gate(r, "westgate", 3, H[0], F, 0, "w", lateral=12.0)
    # the hearth at the north end: a stepped dais of black stone (0.8 m in 0.2 m steps), a cold forge either side of the way out
    top = step_dais(r, STONE, (0, F, 8.0), (10.0, 5.0), 0.8)
    anvil(r, (-2.6, top[1], 8.4), 90, 1.6)
    for x in (-5.4, 5.4):
        box(r, FURNACE, (x, F + 2.3, H[2] - W - 2.4), (3.0, 4.6, 3.2), yaw=180)
        r.stand(COAL, (x * 1.3, F, H[2] - W - 4.6))
    # the bellows and their lanes: west and east, the lanes across the floor's middle third
    zl = -3.0
    lane_len, lane_w = 5.0, 4.4
    pulls = []
    for side, id in ((-1, "bellowsW"), (1, "bellowsE")):
        xw = side * (H[0] - W - 0.4)
        bellows(r, id, xw, zl, side, F)
        lane_mid = side * (H[0] - W - 5.8 - lane_len / 2)                         # (open floor all round the lane: the lure rule)
        r.fill(IRON, (lane_mid, F + 0.02, zl), (lane_len + 2.0, 0.04, lane_w))   # its scorched iron mark
        for dz in (-lane_w / 2, lane_w / 2):
            r.fill(COAL, (lane_mid, F + 0.03, zl + dz), (lane_len + 2.0, 0.05, 0.3))
        lever = id + "lever"
        lx = side * (H[0] - W - 4.8)
        t.lever(r, lever, (lx, F, zl - 7.0), 0, [])                                # (pulled from the south, 7 m from the lane's middle)
        pulls.append((id, lever, lane_mid))
    # light: the hearth's fires (the intro puts them out, the death lights them), lamps by the way in, fire bowls high
    lamp = lamps(r, "hearth", [[(x, F, -H[2] + W + 2.6) for x in (-4.0, 4.0)],
                               [(x, top[1], 9.6) for x in (-3.8, 3.8)]])
    for z in (-10.0, 4.0):
        hang_bowl(r, (0, F + 7.0, z))
    for x in (-9.2, 9.2):
        t.light_stand(r, (x, F, 1.5))
    seat = [0.0, float(top[1]), 8.6]
    corners = [(x, F, z) for x in (-8.5, 8.5) for z in (-11.0, 5.0)]
    trig = []
    for id, lever, lane_mid in pulls:
        trig += [
            {"id": id + "pump", "on": {"event": "lever", "mech": lever}, "do": [{"set": id, "to": 1}], "repeat": True, "solver": "ignore"},
            {"id": id + "blast", "on": {"event": "state", "mech": id, "state": 1}, "repeat": True, "solver": "ignore",
             "do": [{"timer": id + "t", "after": 2.5}, {"sound": "sfx_fader_firebreath_out", "at": r3((lane_mid, F + 1.0, zl))}]
             + [{"sound": "fx_fenring_flames", "at": r3((lane_mid + d, F + 0.6, zl))} for d in (-2.0, 0.0, 2.0)]},
            {"id": id + "back", "on": {"event": "timer", "name": id + "t"}, "repeat": True, "solver": "ignore",
             "do": [{"set": id, "to": 0}]},
        ]
    r.extra["triggers"] = trig
    both = [{"target": "bellowsW", "do": "set:1"}, {"target": "bellowsE", "do": "set:1"}]
    boss = {
        "name": "$dungeons_boss_rimeforge_keeper", "title": "$dungeons_boss_rimeforge_keeper_title", "role": "elite", "fight": 60, "perPlayer": 0.5,
        "scale": 1.5, "mini": True, "armour": 0.25, "armourOff": 12,
        "look": {"light": [0.6, 0.85, 1.0], "range": 5, "aura": "vfx_Frost", "enraged": [1.0, 0.55, 0.2]},
        "intro": {"seconds": 6.0, "wake": 2.0, "leap": [0.0, F, 2.0], "leapAt": 3.2, "beats": [
            {"at": 0.0, "wires": [{"target": "hearth", "do": "set:0"}], "message": "$dungeons_fight_start", "sound": "sfx_prespawn",
             "where": seat, "shake": 0.2},
            {"at": 1.0, "sound": "sfx_fader_firebreath_in", "where": seat},
            {"at": 2.0, "fx": "vfx_spawn_large", "sound": "sfx_skeleton_big_alerted", "shake": 0.4, "banner": True, "where": seat},
            {"at": 6.0, "message": "$dungeons_boss_rimeforge_keeper_hint"}]},
        "abilities": [
            {"id": "slam", "kind": "slam", "every": 11, "first": 6, "warn": 1.6, "radius": 4, "scale": 2, "push": 35, "damage": "frost"},
            {"id": "bellows", "kind": "wires", "every": 16, "first": 9, "warn": 2.0, "wires": both, "cue": "sfx_fader_firebreath_in",
             "phases": [0]},
            {"id": "wild", "kind": "wires", "every": 7, "first": 3, "warn": 1.6, "wires": both, "cue": "sfx_fader_firebreath_in",
             "phases": [1]},
        ],
        "weakTo": [{"mech": id, "state": 1, "area": [lane_w, 4.0, lane_len], "areaAt": [0.0, 1.0, abs(lane_mid - side * (H[0] - W - 0.4))],
                    "share": 0.10, "stagger": True, "breaks": True, "hurt": 2.0, "held": "$dungeons_boss_rimeforge_keeper_held"}
                   for (id, _, lane_mid), side in zip(pulls, (-1, 1))],
        "phases": [{"at": 0.3, "healTo": 0.55, "enrage": 1.2, "adds": {"melee": 1, "ranged": 1}, "message": "$dungeons_boss_rimeforge_keeper_p2",
                    "sound": "sfx_gdking_scream", "shake": 0.5}],
        "death": {"seconds": 3.0, "beats": [
            {"at": 0.0, "wires": [{"target": "bellowsW", "do": "set:0"}, {"target": "bellowsE", "do": "set:0"}],
             "sound": "sfx_fader_firebreath_out", "shake": 0.6, "banner": True},
            {"at": 1.2, "wires": [{"target": "hearth", "do": "set:2"}], "sound": "sfx_FireAddFuel", "fx": "vfx_Smoked",
             "where": seat}]},
    }
    encounter(r, "keeper", (-H[0] + W, F, -H[2] + W + 4.0), (H[0] - W, F + 9.0, H[2] - W), "enter", [], [seat] + corners,
              lock=["ingate", "outgate", "sidegate", "westgate"], opens=["ingate", "outgate", "sidegate", "westgate"], reached_from="in",
              elite_level=3,
              music="boss_goblinking",
              boss=boss)
    return r


# ---------------------------------------------------------------- the Rimesmith (final boss)

GF_SIZE = (32, 20, 40)
GF_DEEP = 4.0                    # the floor 4 m over the box's bottom: the ice risers stand in it


ICE_ROWS = ["   ",              # the ice channel, seen from above (+z up): the block calves at S by the north wall and is pushed
            " S ",              # south down the channel to the hearth (state 3: the forge's mouth); a ring of player-only floor
            " . ",              # round it so it can be pushed from every side
            " . ",
            " . ",
            "   "]
ICE_CELL = 3.0
ICE_MOUTH = 3                   # the block's state in the forge's mouth (the floor cells in reading order: S is 0)


def great_forge(t):
    """The Great Forge: the frost-giants' smithy's heart, 36 x 44 m and 15 m to its ice roof. In the middle the great hearth on a stepped
    dais with the giant's anvil; a channel of glacier ice runs down to the hearth from the north wall; two drop-hammer presses stand over
    iron anvil-marks in the south half; frozen quench pools by the east and west walls; a giant furnace in the north-west corner.
      intro    step in: the gate slams and the hearth goes dark and cold; the furnace's fires catch from the door to the hearth; the
               Rimesmith wakes on the hearth in a burst with the banner, the presses start to fall, and it leaps down.
      COLD     (to 66%) rime armour: it takes a quarter of each blow. The forge breaks it: lure it under a press as it falls (each draws
               a red ring while it comes down; a press holds it as it lands): the press staggers it, takes 7% and breaks its armour
               for 12 s. It strikes the floor round itself (slam) and charges down a marked lane.
      HEAT     (at 66%) it stokes the forge: it leaps back to the hearth, white-hot and shielded (no blow hurts it), the steam vent across
               the ice channel wakes, burning slag rains under each player and fire-born servants come. Quench it: push the block of
               glacier ice down the channel into the forge's mouth, twice (the first time the shield flickers and a new block calves
               at the wall). Then it staggers and takes 8%, and comes down.
      QUENCH   (at 30%) a second wind: it plunges into a quench pool and rises tempered, back to 55%, enraged; the vents die, ice rises
               out of the floor as cover, icicles fall under each player, it charges; the presses still break its armour.
      death    the presses stop, the hearth gutters out, steam, the banner; the way to the vault opens and its trophy drops.
    The final boss, the dungeon's hardest fight: "fight" 220 s with armour (the gate checks it's over twice the Keeper's)."""
    sh = deep_shell(t, "greatforge", GF_SIZE, [("in", 2, 0.0), ("out", 0, 10.0), ("side", 1, -10.0), ("w", 3, -15.0)], GF_DEEP,
                    tags=["boss"], max=1)
    r = sh.room
    r.doorways[1]["type"] = VAULT_TYPE                                               # (the vault takes either the north way or the east)
    r.doorways[2]["type"] = VAULT_TYPE
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    H = np.array(GF_SIZE) / 2
    W = t.wall_t
    t.gate(r, "ingate", 2, H[2], F, 1, "in")
    t.gate(r, "outgate", 0, H[2], F, 0, "out", lateral=10.0)
    t.gate(r, "sidegate", 1, H[0], F, 0, "side", lateral=-10.0)
    t.gate(r, "westgate", 3, H[0], F, 0, "w", lateral=-15.0)
    # the hearth: a stepped dais of black stone 10 x 8 m, 0.8 m high, in the hall's middle; the giant's anvil on it
    top = step_dais(r, STONE, (0, F, -2.0), (10.0, 8.0), 0.8)
    anvil(r, (-3.0, top[1], -2.6), 90, 2.4)
    box(r, IRON, (0, top[1] + 0.03, 0.4), (4.0, 0.06, 1.6))                          # the hearth's grate, at the channel's end
    # the furnace in the north-west corner, its coal and a heap of ice round its feet
    box(r, FURNACE, (-10.8, F + 4.75, 14.2), (6.5, 9.5, 6.8), yaw=135)
    for x, z, s in ((-13.4, 9.2, (2.6, 2.0, 2.4)), (-5.6, 17.0, (2.4, 1.6, 2.6))):
        box(r, ICE, (x, F + s[1] / 2 - 0.3, z), s, yaw=30)
    r.stand(COAL, (-6.4, F, 9.6), yaw=20, scale=(1.4, 1.4, 1.4))
    # the ice channel: a black iron-edged trough's rims on the floor, the block of glacier ice (a push block)
    zc = 8.5                                                                          # the grid's middle (rows 1-4: z 13, 10, 7, 4)
    for x in (-1.6, 1.6):
        r.fill(IRON, (x, F + 0.03, zc), (0.3, 0.06, 4 * ICE_CELL))
    blk = r.mech("iceblock", "pushblock", (0, F, zc))
    box(blk, ICE, (0, 1.4, 0), (2.8, 2.8, 2.8))
    blk.extra.update({"cell": ICE_CELL, "rows": ICE_ROWS, "group": "ice", "fx": ["sfx_ice_hit"]})
    blk.seconds = 0.6
    # the glacier wall it calves from (behind S), and a lever there that calves a new block (resets the channel)
    box(r, ICE, (0, F + 3.0, H[2] - W - 0.6), (6.0, 6.0, 1.2), yaw=0)
    t.lever(r, "calve", (6.6, F, 14.0), 90, [])
    # the presses over their anvil-marks in the south half (state 1: falling on their own every 4 s; set on by the intro)
    presses = []
    for i, x in enumerate((-9.0, 9.0)):
        pz = -9.0
        p = r.mech(f"press{i + 1}", "platform", (x, F, pz))
        box(p, STONE, (0, 0.4 + 1.3, 0), (5.0, 2.6, 5.0))
        box(p, IRON, (0, 0.5, 0), (5.2, 0.4, 5.2))
        for bx in (-1.6, 1.6):
            post(p, (bx, 2.6 + 2.5, 0), 0.7, 5.0, 0.7)
        p.pose(at=(0, 7.5, 0)).pose()
        p.seconds = 1.2
        p.cycle = 4.0 + 0.8 * i
        p.extra["cycleIn"] = [1]
        r.fill(IRON, (x, F + 0.02, pz), (5.4, 0.04, 5.4))                             # its anvil-mark
        r.fill(STONE, (x, F + 0.03, pz), (4.2, 0.04, 4.2))
        box(r, IRON, (x, sh.top - t.floor_t - 0.5, pz), (7.0, 1.0, 1.4))             # the beam it hangs from
        presses.append(p.id)
    # the quench pools by the east and west walls (frozen over: walked on), where it plunges at its second wind
    pools = []
    for x in (-11.5, 11.5):
        box(r, "IcePond_rock", (x, F - 0.35, 3.0), (6.0, 0.8, 6.0))
        pools.append([x, F, 3.0])
    # the steam vent across the ice channel (woken in the heat): it hisses white, then scalds, every 4.5 s
    v = r.mech("vent", "trap", (0, F, 8.5))
    box(v, FLOORGRATE, (0, 0.06, 0), (9.0, 0.12, 1.4))
    v.pose().pose()
    v.start = 0
    v.extra.update({"damage": "fire", "scale": 0.8, "every": 4.5, "for": 1.2, "area": [9.0, 2.4, 1.6], "areaAt": [0, 1.2, 0],
                    "fx": ["vfx_Smoked", "sfx_fader_firebreath_out"], "warn": 1.2, "warnFx": ["vfx_Smoked", "sfx_fader_firebreath_in"]})
    # the ice that rises at the second wind: four flat-topped blocks as cover (sunk in the floor until then)
    for i, (x, z) in enumerate(((-7.0, -1.0), (7.0, -1.0), (-5.0, -14.0), (5.0, -14.0))):
        b = r.mech(f"riser{i + 1}", "platform", (x, F, z))
        box(b, ICE, (0, -1.75, 0), (3.2, 3.6, 3.2))
        b.pose().pose(at=(0, 3.2, 0))
        b.seconds = 3.0
    # the fires: by the door, mid-hall, and the hearth (the intro puts them out and lights them in turn; the heat flares the hearth)
    fires = lamps(r, "fires", [[(x, F, -H[2] + W + 2.8) for x in (-5.0, 5.0)],
                               [(x, F, -1.0) for x in (-15.0, 15.0)],
                               [(x, top[1], 0.6) for x in (-3.4, 3.4)] + [(-7.0, F, 11.0)]])
    for x, z in ((-4.8, -12.0), (4.8, 0.0), (-4.8, 12.0)):
        hang_bowl(r, (x, F + 9.0, z))
    seat = [1.2, float(top[1]), -3.0]
    alcoves = [(-13.0, F, -3.5), (13.0, F, -14.0), (13.0, F, 14.5), (-13.0, F, 6.5)]
    guards = [(-6.0, F, 6.5), (6.0, F, 10.0), (6.0, F, 4.5)]
    trig = [
        {"id": "calved", "on": {"event": "lever", "mech": "calve"}, "repeat": True, "do": [{"reset": "ice"}, {"sound": "sfx_ice_destroyed"}]},
        {"id": "quench", "on": {"event": "state", "mech": "iceblock", "state": ICE_MOUTH}, "repeat": True, "solver": "ignore",
         "do": [{"sound": "vfx_ice_destroyed", "at": r3((0, F + 1.0, 4.0))}, {"sound": "sfx_ice_destroyed", "at": r3((0, F + 1.0, 4.0))},
                {"sound": "vfx_Smoked", "at": r3((0, top[1] + 0.5, 0.4))}, {"sound": "sfx_fader_firebreath_out", "at": r3((0, F + 1.0, 2.6))}]},
    ]
    r.extra["triggers"] = trig
    press_on = [{"target": p, "do": "set:1"} for p in presses]
    boss = {
        "name": "$dungeons_boss_rimeforge_smith", "title": "$dungeons_boss_rimeforge_smith_title", "role": "elite", "fight": 220, "perPlayer": 0.5,
        "scale": 1.8, "armour": 0.25, "armourOff": 12,
        "look": {"light": [0.55, 0.85, 1.0], "range": 7, "aura": "vfx_Frost", "enraged": [1.0, 0.5, 0.15]},
        "intro": {"seconds": 8.6, "wake": 4.4, "leap": [0.0, F, -9.0], "leapAt": 5.8, "beats": [
            {"at": 0.0, "wires": [{"target": "fires", "do": "set:0"}], "sound": "sfx_prespawnLastBossGate", "shake": 0.3,
             "message": "$dungeons_fight_start", "where": [0.0, F, -14.0]},
            {"at": 1.4, "wires": [{"target": "fires", "do": "set:1"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -18.0]},
            {"at": 2.4, "wires": [{"target": "fires", "do": "set:2"}], "sound": "sfx_FireAddFuel", "where": [0.0, F, -1.0]},
            {"at": 3.4, "wires": [{"target": "fires", "do": "set:3"}], "sound": "sfx_fader_firewall_start", "where": seat},
            {"at": 4.4, "fx": "vfx_spawn_large", "sound": "sfx_gdking_alert", "shake": 0.6, "banner": True, "where": seat},
            {"at": 4.6, "wires": press_on, "fx": "fx_eikthyr_stomp", "sound": "sfx_gui_craftitem_forge", "where": seat},
            {"at": 8.6, "message": "$dungeons_boss_rimeforge_smith_hint"}]},
        "abilities": [
            {"id": "strike", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 5, "scale": 2.5, "push": 40,
             "sound": "sfx_gui_craftitem_forge", "phases": [0, 2]},
            {"id": "charge", "kind": "charge", "every": 12, "first": 7, "warn": 1.3, "width": 3, "length": 22, "scale": 2.5, "push": 50,
             "phases": [0, 2]},
            {"id": "slag", "kind": "rain", "every": 6.5, "first": 3, "warn": 1.8, "radius": 2.4, "scale": 1.5, "push": 10, "count": 2,
             "damage": "fire", "fx": "vfx_BombBlob_explode_lava", "sound": "sfx_blobLava_explosion", "phases": [1]},
            {"id": "embers", "kind": "summon", "every": 15, "first": 6, "warn": 2.0, "adds": {"ranged": 1}, "points": guards,
             "phases": [1]},
            {"id": "icefall", "kind": "rain", "every": 7.5, "first": 4, "warn": 1.8, "radius": 2.2, "scale": 1.5, "push": 10, "count": 1,
             "damage": "frost", "fx": "vfx_ice_destroyed", "sound": "sfx_ice_destroyed", "cue": "sfx_frozenking_spikerain_iceceiling",
             "phases": [2]},
        ],
        "weakTo": [{"mech": p, "state": 1, "area": [5.4, 4.0, 5.4], "areaAt": [0.0, 1.0, 0.0], "share": 0.07, "stagger": True,
                    "breaks": True, "hurt": 2.0} for p in presses],
        "phases": [
            {"at": 0.66, "wires": [{"target": "vent", "do": "set:1"}], "adds": {"melee": 2}, "leap": seat,
             "message": "$dungeons_boss_rimeforge_smith_heat", "sound": "sfx_fader_firewall_start", "shake": 0.6,
             "shield": {"take": 0, "until": [{"mech": "iceblock", "state": ICE_MOUTH}], "seconds": 100, "times": 2, "hold": True,
                        "breakShare": 0.08, "hint": "$dungeons_boss_rimeforge_smith_shield",
                        "flicker": "$dungeons_boss_rimeforge_smith_flicker",
                        "breakWires": [{"target": "vent", "do": "set:0"}]}},
            {"at": 0.3, "healTo": 0.55, "leap": pools[1],
             "wires": [{"target": f"riser{i}", "do": "set:1"} for i in range(1, 5)] + [{"target": "vent", "do": "set:0"},
                                                                                       {"target": "fires", "do": "set:2"}],
             "adds": {"melee": 1, "ranged": 1}, "enrage": 1.3, "message": "$dungeons_boss_rimeforge_smith_p3", "sound": "sfx_frozenking_voice_scream",
             "shake": 0.8},
        ],
        "death": {"seconds": 4.0, "beats": [
            {"at": 0.0, "wires": [{"target": p, "do": "set:0"} for p in presses], "sound": "sfx_gdking_death", "shake": 1.0, "banner": True},
            {"at": 1.4, "wires": [{"target": "fires", "do": "set:0"}], "sound": "sfx_fader_firebreath_out", "fx": "vfx_Smoked",
             "where": seat, "shake": 0.4}]},
    }
    encounter(r, "smith", (-H[0] + W, F, -H[2] + W + 4.0), (H[0] - W, F + 13.0, H[2] - W), "enter", [], [seat] + alcoves,
              lock=["ingate", "sidegate", "westgate"], opens=["ingate", "outgate", "sidegate", "westgate"], reached_from="in",
              music="boss_moder", boss=boss)
    return r
