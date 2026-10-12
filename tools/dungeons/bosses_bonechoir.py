"""
The Cathedral of the Bone Choir's two boss rooms (the boss toolkit: skill/bosses.md; the rooms' look: authoring/themes/ossuary.py):

  choir_loft   the Choirmaster (a mini boss). His choir sings from three stalls along the walls, one at a time, and while a stall
               sings he can't be hurt. He hunts you all the while (the shield doesn't hold him still): get to the lit stall and pull
               the lever beside it, and its iron shutter drops over the singers. Silenced, the stall's song breaks his shield: he
               staggers and loses a tenth of his health. Then the next stall takes up the song (at two thirds, at a third). Driven down
               to a quarter he recovers to half, enraged. When he falls, his skull (the skull relic) is left on his lectern.
  apse         the Bone Colossus (the final boss). A round apse under a dome of ribs: a mound of bones in the middle, four choir stalls
               round it, a great bell over each stall with its rope on the floor in front. At three quarters the choir sings it back
               onto its mound and it can't be hurt: the bells ring the hymn by themselves (each swings, tolls and glows in turn);
               ring the ropes in the same order and the choir falls silent: the shield breaks, it staggers, and comes down. At 45% a
               new verse (another order), and the great bells' toll sends a shockwave along two lanes. At a quarter it recovers to
               half, enraged, and charges. Its death opens the way to the vault.
"""
import numpy as np

from dungeon_parts import box, bounds, r3, yaw_turn
from boss_rooms import step_dais

import bonechoir_rooms as br
from bonechoir_rooms import shell, gate, encounter, trig, lit, swing_bell, hymn, rope_rings, lever_at, \
    BLOCK, BONE, CANDLE, TORCH, DARK, TOLL, BELL

T = None
GRATE = "iron_grate"


def _hidden(m, y=-0.6):
    """A mechanism that only holds a state (a marker): one small block inside the floor, out of sight."""
    box(m, BLOCK, (0, y, 0), (0.4, 0.4, 0.4))


# ---------------------------------------------------------------- the Choir Loft: the Choirmaster

LOFT = (24, 16, 36)


def choir_loft():
    br.T = T
    sh = shell("choir", LOFT, [("in", 2, 0.0, 0), ("out", 0, 0.0, 0), ("side", 1, -11.0, 0), ("west", 3, 0.0, 0)],
               tags=["choir", "fight"], max=1, min_depth=3)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    hx, hz = sh.half[0] - T.wall_t, sh.half[2] - T.wall_t
    gate(sh, "ingate", "in", 1)
    gate(sh, "outgate", "out", 0)
    gate(sh, "sidegate", "side", 0)
    gate(sh, "westgate", "west", 0)
    # his lectern: a stepped dais at the far end, a bone throne behind it
    top = step_dais(r, BLOCK, (0, F, 12.0), (7.0, 5.0), 0.8)
    r.stand("piece_bone_throne", (0, top[1], 12.6), yaw=180)
    seat = [0.0, float(top[1]), 11.3]
    # the three stalls: a low dais against the wall, three singers on it, candles; an iron shutter hung over its front that drops to
    # silence it (state 1), the lever beside it; the stall's fire burns only while it sings (its "glow": 1 singing)
    stalls = [(-1, -8.0, "west"), (1, 0.0, "east"), (-1, 8.5, "west2")]
    levers = [(-6.0, -3.0, 270), (6.0, 5.0, 90), (-6.0, 13.5, 270)]
    fronts = []
    for i, ((s, zc, _), (lx, lz, lyaw)) in enumerate(zip(stalls, levers), start=1):
        x_in = s * (hx - 3.0)                                   # the stall's front edge
        box(r, BLOCK, (s * (hx - 1.5), F + 0.2, zc), (3.0, 0.4, 7.0))
        for dz in (-2.2, 0.0, 2.2):
            r.stand("FrozenSkeleton_Pose1" if dz == 0 else "FrozenSkeleton_Pose2", (s * (hx - 1.4), F + 0.4, zc + dz),
                    yaw=90 if s < 0 else 270)
        glow = r.mech(f"glow{i}", "platform", (s * (hx - 0.9), F + 0.4, zc))
        for dz in (-3.0, 3.0):
            for k, (ddx, ddz, sc) in enumerate(((0, 0, 4.5), (0.4 * -s, 0.35, 3.6), (0.3 * -s, -0.4, 3.0))):
                lit(glow.stand(CANDLE, (ddx, 0, dz + ddz), scale=(sc, sc, sc)), [1])
        lit(glow.stand(TORCH, (0, 0, 0), scale=(1.4, 1.4, 1.4)), [1])
        glow.pose().pose()
        glow.seconds = 0.0
        glow.start = 0
        shutter = r.mech(f"shutter{i}", "platform", (s * (hx - 3.15), F, zc))
        box(shutter, GRATE, (0, 2.9, 0), (0.12, 5.4, 6.8), yaw=0)
        box(shutter, BONE, (0, 5.7, 0), (0.45, 7.2, 0.45), tilt=(90, 0))
        for dz in (-3.4, 3.4):
            box(shutter, BONE, (0, 2.9, dz), (0.4, 5.8, 0.4))
        shutter.pose(at=(0, 6.2, 0)).pose(at=(0, 0, 0))
        shutter.seconds = 0.8
        lever_at(r, f"silence{i}", (lx, F, lz), lyaw, reached_from="in")
        trig(r, id=f"drop{i}", on={"event": "lever", "mech": f"silence{i}"}, repeat=True, solver="ignore",
             do=[{"set": f"shutter{i}", "to": 1}, {"sound": "sfx_gdking_rock_destroyed", "at": r3((x_in, F + 3, zc))}])
        trig(r, id=f"hushed{i}", on={"event": "state", "mech": f"shutter{i}", "state": 1}, repeat=True, solver="ignore",
             do=[{"set": f"glow{i}", "to": 0}])
        fronts.append((x_in - s * 2.2, F, zc + (2.3 if i != 2 else -2.3)))
    # banners and the lights of the hall
    for z in (-14.0, 14.5):
        for s in (-1, 1):
            T.light_stand(r, (s * (hx - 1.2), F, z))
    for z in (-10.0, 0.0, 8.0):
        T.light_hang(r, (0, F + 9.0, z))
    pts = [seat] + [list(p) for p in fronts] + [[0.0, F, -2.0]]
    stall_light = lambda i: [{"target": f"glow{i}", "do": "set:1"}]
    boss = {
        "name": "$dungeons_bonechoir_choirmaster", "title": "$dungeons_bonechoir_choirmaster_title", "role": "elite", "fight": 50, "perPlayer": 0.5,
        "scale": 1.5, "mini": True,
        "look": {"light": [1.0, 0.78, 0.42], "range": 6, "aura": "vfx_Burning", "enraged": [1.0, 0.35, 0.15]},
        "intro": {"seconds": 6.5, "wake": 3.4, "leap": [0.0, F, 5.0], "leapAt": 4.6, "beats": [
            {"at": 0.0, "message": "$dungeons_fight_start", "sound": "sfx_prespawn", "where": seat, "shake": 0.2},
            {"at": 0.8, "wires": stall_light(1), "sound": TOLL, "where": r3(fronts[0])},
            {"at": 1.6, "wires": stall_light(2) + [{"target": "glow1", "do": "set:0"}], "sound": TOLL, "where": r3(fronts[1])},
            {"at": 2.4, "wires": stall_light(3) + [{"target": "glow2", "do": "set:0"}], "sound": TOLL, "where": r3(fronts[2])},
            {"at": 3.2, "wires": [{"target": "glow3", "do": "set:0"}]},
            {"at": 3.4, "fx": "vfx_spawn_large", "sound": "sfx_skeleton_big_alerted", "shake": 0.4, "banner": True, "where": seat}]},
        "abilities": [
            {"id": "voice", "kind": "slam", "every": 10, "first": 5, "warn": 1.5, "radius": 5, "scale": 2, "push": 30, "sound": TOLL},
            {"id": "notes", "kind": "rain", "every": 8, "first": 4, "warn": 1.8, "radius": 2.0, "scale": 1.2, "push": 8, "count": 1,
             "phases": [1, 2, 3, 4]},
            {"id": "chorus", "kind": "summon", "every": 16, "first": 5, "warn": 2.0, "adds": {"melee": 1}, "points": [list(p) for p in fronts],
             "phases": [1, 2, 3]},
        ],
        "phases": [
            {"at": 0.97, "wires": stall_light(1), "message": "$dungeons_bonechoir_choirmaster_p1", "sound": TOLL,
             "shield": {"take": 0, "until": [{"mech": "shutter1", "state": 1}], "seconds": 75, "hold": False, "breakShare": 0.1,
                        "hint": "$dungeons_bonechoir_choirmaster_shield"}},
            {"at": 0.66, "wires": stall_light(2), "message": "$dungeons_bonechoir_choirmaster_p2", "sound": TOLL, "adds": {"melee": 1},
             "shield": {"take": 0, "until": [{"mech": "shutter2", "state": 1}], "seconds": 75, "hold": False, "breakShare": 0.1,
                        "hint": "$dungeons_bonechoir_choirmaster_shield"}},
            {"at": 0.33, "wires": stall_light(3), "message": "$dungeons_bonechoir_choirmaster_p2", "sound": TOLL, "adds": {"melee": 1, "ranged": 1},
             "shield": {"take": 0, "until": [{"mech": "shutter3", "state": 1}], "seconds": 75, "hold": False, "breakShare": 0.1,
                        "hint": "$dungeons_bonechoir_choirmaster_shield"}},
            {"at": 0.25, "healTo": 0.5, "enrage": 1.25, "message": "$dungeons_bonechoir_choirmaster_p4", "sound": "sfx_gdking_scream", "shake": 0.5},
        ],
        "death": {"seconds": 3.0, "beats": [
            {"at": 0.0, "sound": "sfx_skeleton_big_death", "shake": 0.6, "banner": True,
             "wires": [{"target": f"glow{i}", "do": "set:0"} for i in (1, 2, 3)]},
            {"at": 1.2, "sound": TOLL, "where": seat}]},
    }
    encounter(r, "choirmaster", (-hx, F, -hz + 5.0), (hx, F + 10.0, hz), "enter", [], pts,
              lock=["ingate", "outgate", "sidegate", "westgate"], opens=["ingate", "outgate", "sidegate", "westgate"], reached_from="in",
              music="boss_eikthyr", boss=boss)
    trig(r, id="skull", on={"event": "cleared", "mech": "choirmaster"},
         do=[{"item": "relic_skull", "at": r3((0.0, top[1] + 0.6, 11.0))}, {"sound": "sfx_secretfound"},
             {"message": "$dungeons_bonechoir_relic_skull_found"}])
    return r


# ---------------------------------------------------------------- the Apse: the Bone Colossus

APSE = (32, 24, 32)
ORDERS = (["bellB", "bellD", "bellA", "bellC"], ["bellC", "bellA", "bellB", "bellD"])


def apse():
    br.T = T
    sh = shell("apse", APSE, [("in", 2, 0.0, 0), ("out", 0, 0.0, 0), ("e", 1, 0.0, 0), ("w", 3, 0.0, 0)], shape="round",
               tags=["apse", "fight"], max=1, min_depth=4)
    r = sh.room
    r.extra["joinBy"] = ["in", "e", "w"]          # (entered by any but the way on: the fight shuts every gate)
    F = sh.floor_y
    top = sh.top - T.floor_t
    gate(sh, "ingate", "in", 1)
    gate(sh, "outgate", "out", 0)
    gate(sh, "egate", "e", 1)
    gate(sh, "wgate", "w", 1)
    # the mound of bones in the middle, stepped so the Colossus can walk down from it
    mound = step_dais(r, BLOCK, (0, F, 0), (9.0, 9.0), 1.2)
    seat = [0.0, float(mound[1]), 0.0]
    for a in (20, 110, 200, 290):
        p = yaw_turn((0, 0, 5.4), a)
        r.stand("skull_pile", (p[0], F, p[2]), yaw=a, scale=(1.5, 1.4, 1.5))
    # the four stalls on the diagonals, a bell over each, its rope in front (pulled from the middle's side)
    names = ["bellA", "bellB", "bellC", "bellD"]
    where, fronts = {}, []
    choir = r.mech("choir", "platform", (0, F, 0))
    for b, a in zip(names, (315, 45, 135, 225)):
        st = yaw_turn((0, 0, 11.8), a)
        r.stand("piece_throne02", (st[0], F, st[2]), yaw=(a + 180) % 360)
        for side in (-1.6, 1.6):
            q = yaw_turn((side, 0, 10.4), a)
            r.stand("FrozenSkeleton_Pose1", (q[0], F, q[2]), yaw=(a + 180) % 360)
        for side in (-2.8, 2.8):
            q = yaw_turn((side, 0, 11.4), a)
            for k, sc in enumerate((4.5, 3.6, 3.0)):
                d = yaw_turn((0.4 * (k - 1), 0, 0.3 * (k % 2)), a)
                lit(choir.stand(CANDLE, (q[0] + d[0], 0, q[2] + d[2]), scale=(sc, sc, sc)), [1])
        bt = yaw_turn((0, 0, 10.0), a)
        swing_bell(r, b, (bt[0], top - 3.0, bt[2]), 2.6, top)
        where[b] = np.array([bt[0], top - 4.5, bt[2]])
        lv = yaw_turn((0, 0, 8.0), a)
        lever_at(r, "rope" + b[-1], (lv[0], F, lv[2]), a, reached_from="in")
        fr = yaw_turn((3.2, 0, 7.4), a)
        fronts.append([round(fr[0], 2), F, round(fr[2], 2)])
    choir.pose().pose()
    choir.seconds = 0.0
    choir.start = 0
    rope_rings(r, [("rope" + b[-1], b) for b in names], where)
    # the markers: which verse the choir sings (0 none, 1, 2), and whether it is hushed (1)
    verse = r.mech("verse", "platform", (0, F, 0))
    _hidden(verse)
    verse.pose().pose().pose()
    verse.seconds = 0.0
    hush = r.mech("hush", "platform", (0, F, 2.0))
    _hidden(hush)
    hush.pose().pose()
    hush.seconds = 0.0
    for n, (order, prefix) in enumerate(zip(ORDERS, ("h", "g")), start=1):
        cond = [{"mech": "verse", "state": n}, {"mech": "hush", "state": 0}]
        hymn(r, order, where, display_every=13.0, gap=1.6, cond=cond, prefix=prefix)
        trig(r, id=f"verse{n}", on={"event": "state", "mech": "verse", "state": n}, repeat=True, solver="ignore",
             do=[{"timer": f"{prefix}hymn", "every": 13.0}, {"timer": f"{prefix}0", "after": 2.5}, {"set": "choir", "to": 1}])
        trig(r, id=f"sing{n}", on=[{"event": "lever", "mech": "rope" + b[-1]} for b in order], when="sequence", within=30, repeat=True,
             solver="ignore", if_=cond,
             do=[{"set": "hush", "to": 1}, {"set": "choir", "to": 0}, {"sound": "sfx_secretfound"},
                 {"message": "$dungeons_bonechoir_colossus_hushed"}])
    # the great bells' toll: two shockwave lanes across the apse (each a bell that swings over its lane; off until the second verse)
    tolls = []
    for i, z in enumerate((6.4, -6.4)):
        crown = top - 4.3
        t = r.mech(f"toll{i + 1}", "trap", (0, crown, z))
        s = br.bell_scale(3.2)
        t.stand(BELL, (0, -3.2, 0), scale=(s, s, s))
        t.pose().pose(euler=(0, 0, 22))
        t.start = 0
        t.extra.update({"damage": "blunt", "scale": 1.2, "every": 8.0, "for": 0.6, "offset": 4.0 * i,
                        "area": [26.0, 2.6, 3.0], "areaAt": [0, round(F + 1.3 - crown, 2), 0], "fx": ["fx_eikthyr_stomp", "sfx_gdking_stomp"],
                        "warn": 1.6, "warnFx": [TOLL], "warnLift": 0.0})
        from giant_kit import hang_chain
        hang_chain(r, (0, top, z), top - crown)
        box(r, DARK, (0, F + 0.02, z), (24.0, 0.04, 2.6))
        tolls.append(t.id)
    # the organ round the far door, the lights
    for s in (-1, 1):
        for i in range(5):
            hgt = 15.0 - i * 2.0
            p = yaw_turn((0, 0, 14.4), s * (12 + i * 5.5))
            box(r, BONE, (p[0], F + hgt / 2, p[2]), (0.9, hgt, 0.9))
    for a in (90, 270):
        p = yaw_turn((0, 0, 13.0), a)
        T.light_stand(r, (p[0], F, p[2]))
    T.light_hang(r, (0, F + 13.0, 0))
    pts = [seat] + fronts
    lane = lambda on: [{"target": t, "do": f"set:{on}"} for t in tolls]
    rings = []
    for k, b in enumerate(names):
        rings += [{"at": 1.0 + 0.8 * k, "wires": [{"target": b, "do": "set:1"}], "sound": TOLL, "where": r3(where[b])},
                  {"at": 1.6 + 0.8 * k, "wires": [{"target": b, "do": "set:0"}]}]
    boss = {
        "name": "$dungeons_bonechoir_colossus", "title": "$dungeons_bonechoir_colossus_title", "role": "elite", "fight": 220, "perPlayer": 0.5,
        "scale": 2.0,
        "look": {"light": [1.0, 0.82, 0.5], "range": 8, "aura": "vfx_Burning", "enraged": [1.0, 0.35, 0.15]},
        "intro": {"seconds": 8.0, "wake": 5.0, "leap": [0.0, F, -6.5], "leapAt": 6.2, "beats": [
            {"at": 0.0, "message": "$dungeons_fight_start", "sound": "sfx_prespawnLastBossGate", "shake": 0.3, "where": [0.0, F, -10.0]},
            {"at": 0.9, "wires": [{"target": "choir", "do": "set:1"}]}] + rings + [
            {"at": 4.6, "fx": "vfx_bone_stack_destroyed", "sound": "sfx_rock_destroyed", "where": seat, "shake": 0.5},
            {"at": 5.0, "fx": "vfx_spawn_large", "sound": "sfx_skeleton_big_alerted", "shake": 0.7, "banner": True, "where": seat},
            {"at": 8.0, "message": "$dungeons_bonechoir_colossus_hint"}]},
        "abilities": [
            {"id": "toll", "kind": "slam", "every": 9, "first": 4, "warn": 1.6, "radius": 6, "scale": 2.5, "push": 40, "sound": TOLL,
             "fx": "fx_eikthyr_stomp", "phases": [0, 1, 2, 3]},
            {"id": "shards", "kind": "rain", "every": 6.5, "first": 3, "warn": 1.8, "radius": 2.2, "scale": 1.5, "push": 10, "count": 2,
             "phases": [1, 2, 3]},
            {"id": "singers", "kind": "summon", "every": 15, "first": 6, "warn": 2.0, "adds": {"melee": 1}, "points": fronts,
             "phases": [1, 2]},
            {"id": "charge", "kind": "charge", "every": 11, "first": 4, "warn": 1.3, "width": 3, "length": 20, "scale": 2.5, "push": 50,
             "phases": [3]},
        ],
        "phases": [
            {"at": 0.75, "wires": [{"target": "verse", "do": "set:1"}], "adds": {"melee": 2}, "leap": seat, "sound": "sfx_gdking_scream",
             "shake": 0.6, "message": "$dungeons_bonechoir_colossus_p1",
             "shield": {"take": 0, "until": [{"mech": "hush", "state": 1}], "seconds": 120, "hold": True, "breakShare": 0.08,
                        "hint": "$dungeons_bonechoir_colossus_shield"}},
            {"at": 0.45, "wires": [{"target": "verse", "do": "set:2"}] + lane(1), "adds": {"melee": 1, "ranged": 1}, "leap": seat,
             "sound": "sfx_gdking_scream", "shake": 0.7, "message": "$dungeons_bonechoir_colossus_p2",
             "shield": {"take": 0, "until": [{"mech": "hush", "state": 1}], "seconds": 120, "hold": True, "breakShare": 0.08,
                        "hint": "$dungeons_bonechoir_colossus_shield"}},
            {"at": 0.25, "healTo": 0.5, "enrage": 1.3, "wires": [{"target": "choir", "do": "set:1"}], "adds": {"melee": 2},
             "message": "$dungeons_bonechoir_colossus_p3", "sound": "sfx_gdking_scream", "shake": 0.8},
        ],
        "death": {"seconds": 5.0, "beats": [
            {"at": 0.0, "wires": lane(0) + [{"target": "verse", "do": "set:0"}], "sound": "sfx_skeleton_big_death", "shake": 1.0,
             "banner": True},
            {"at": 1.0, "wires": [{"target": "choir", "do": "set:0"}], "sound": TOLL, "where": r3(where["bellA"])},
            {"at": 2.2, "fx": "vfx_RockDestroyed_large", "sound": "sfx_rock_destroyed", "where": seat, "shake": 0.6}]},
    }
    R = sh.half[0] - T.wall_t
    encounter(r, "colossus", (-R, F, -R + 5.0), (R, F + 14.0, R), "enter", [], pts, lock=["ingate", "outgate", "egate", "wgate"], opens=["ingate", "outgate", "egate", "wgate"],
              reached_from="in", music="boss_bonemass", boss=boss)
    return r
