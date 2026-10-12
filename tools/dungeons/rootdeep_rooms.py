"""
Rootdeep Hollow's hand-made rooms (the roothollow theme's signature rooms: authoring/themes/roothollow.py; its boss rooms are in
bosses_roothollow.py):
  entrance     the Split Root: down through the split in the ancient root, into a hollow of trunks lit by fungus
  sapshaft_*   the Sap Shaft: a hollow trunk three storeys tall, root ledges in its four corners, root stairs up the south wall, and
               root bridges along the other walls that grow (rise out of the dark) and wither (sink) as the sap valves are turned:
               each valve sends sap to one root and takes it from another. Work out the order to climb to the heart's door.
  seedgarden   the Seed Garden: a rot pit splits the hollow; seed pods burst as you come near and drop glowing seeds, and the two
               sockets on the edge drink them and grow the bridge across
  vault        the Heartwood Vault: the old chest the tree grew round, on a mound of roots
  caps         a doorway grown shut with roots (the plain cap), and the caps for the heart's and the vault's own doorway types
The puzzles are searched and proved here (solve(), as shifting_halls.py does for its halls): python rootdeep_rooms.py.
"""
import collections
import itertools
import math
import random
import sys

import numpy as np

from dungeon_parts import Room, box, bounds, side_point, slab, yaw_turn

HEART_TYPE = "roothollow"      # the Sap Shaft's top doors and the Rotheart's way in: the theme's own type (rooms may come between:
VAULT_TYPE = "roothollow"      # a type of their own left the Rotheart nowhere to go in a crowded layout), and so the vault's


def T():
    from authoring.themes.roothollow import THEME
    return THEME


def shell(t, name, size, doors, shape="rect", no_climb=False, **kw):
    """A plain room of the theme with doorways [(id, side, lateral, level y)], built (walls, floor, roof, frames, structure). no_climb:
    a room whose heights are the puzzle (built here, so the shell sees one level): nothing the theme grows on it is a step."""
    from authoring.shells import Shell
    from authoring.core import rng_for
    sh = Shell(t, name, shape, size, rng_for(name), **kw)
    sh.no_climb = no_climb
    for id, k, lat, y in doors:
        d = sh.add_door(id, k, lat, 0)
        d.y = y
    sh.build()
    return sh


# ---------------------------------------------------------------- the way in

def entrance(t):
    """The Split Root, inside: a trunk-hollow 20 m round. The way in (south) comes down out of the split; a great root lies across the
    back of the hollow, glowing mushrooms grow round its foot, and seed pods hang from tendrils under the roof. Ways on east and
    north."""
    import authoring.themes.roothollow as rh
    size = (20.0, 13.0, 20.0)
    y = -size[1] / 2 + t.floor_t
    sh = shell(t, "entrance", size, [("in", 2, 0.0, y), ("n", 0, 0.0, y), ("e", 1, 0.0, y)], shape="round", tags=["entrance"])
    r = sh.room
    r.entrance = True
    r.doorways[0]["entrance"] = True
    r.max = 1
    # a fallen root across the north-west, clear of the ways (its box low: a curb, not a ledge)
    rh.stick(r, rh.MOSS, (-7.2, y + 0.45, -2.0), (-3.0, y + 0.45, 6.6), 1.1, axis="z", spin=40)
    for x, z, s in ((-5.6, 1.0, 0.8), (5.8, 5.0, 0.7)):
        r.stand(rh.MUSH, (x, y, z), yaw=x * 40, scale=(s, s, s))
    for x, z in ((4.0, -4.0), (-3.5, 3.0), (0.0, 0.0)):
        t.light_hang(r, (x, size[1] / 2 - 5.0, z))
    for x, z in ((6.5, -5.0), (-6.0, -5.5)):
        r.stand("root08", (x, y, z), yaw=x * 23, scale=(1.3, 1.3, 1.3))
    return r


# ---------------------------------------------------------------- caps

def cap(t, name, type_, weight=0.3):
    """A doorway grown shut: two skins of pale root lattice with a great root across them (a plain cap: no depth of its own; it
    stands in the doorway's wall)."""
    import authoring.themes.roothollow as rh
    r = Room(name, (t.door_w + 2.4, 13.0, 0), end_cap=True, weight=weight)
    y = -3.0
    r.doorway("in", (0, y, 0), (0, 0, 1), type=type_, door=False)
    w = t.door_w + 0.6
    box(r, rh.LATTICE, (0, y + t.door_h / 2 + 0.1, 0.25), (w, t.door_h + 0.6, 0.4))
    box(r, rh.LATTICE, (0, y + t.door_h / 2 + 0.1, 0.75), (w, t.door_h + 0.6, 0.4), yaw=180)
    box(r, rh.ARC, (0, y + t.door_h / 2, 0.5), (t.door_h * 0.95, w * 0.5, 0.6), tilt=(0, 65 if rh.h01(name) < 0.5 else -65))
    return r


# ---------------------------------------------------------------- the end

def vault(t):
    """The Heartwood Vault: a small hollow at the very heart, the old chest the tree grew round on a stepped mound of roots, seed pods
    and amber light round it. Tagged end: its chest is the vault's."""
    import authoring.themes.roothollow as rh
    size = (16.0, 13.0, 16.0)
    y = -size[1] / 2 + t.floor_t
    sh = shell(t, "vault", size, [("in", 2, 0.0, y)], shape="round", tags=["end"])
    sh.room.doorways[0]["type"] = VAULT_TYPE
    r = sh.room
    r.max = 1
    # the mound: heartwood in 0.25 m steps, the chest on top, roots grown over its back
    for i, (w, d) in enumerate(((7.0, 6.0), (5.6, 4.8), (4.2, 3.6))):
        r.fill(rh.HEART, (0, y + 0.125 * (i + 1), 3.0), (w, 0.25 * (i + 1), d))
    top = y + 0.75
    r.stand(rh.VAULT_CHEST, (0, top, 3.2), yaw=180, saved=True)
    box(r, rh.ARC, (0, top + 2.6, 4.6), (6.0, 2.6, 1.6), yaw=180)
    for x in (-3.0, 3.0):
        r.stand(rh.LAVA, (x, y, 0.4), scale=(1.4, 1.4, 1.4))
        if x < 0:
            r.stand(rh.POD_S, (x * 1.6, y, 3.5), yaw=x * 30)
    for x, z in ((-4.5, -2.0), (4.5, -2.5)):
        r.stand(rh.MUSH, (x, y, z), yaw=x * 50, scale=(0.75, 0.75, 0.75))
    return r


# ---------------------------------------------------------------- the Sap Shaft

SH_HALF = 20.0                       # the shaft is 40 x 32 x 40 m
SH_Y = [-15.0, -5.0, 5.0]            # the ground's and the two ledge storeys' floors (10 m a storey)
LEDGE = (10.4, 18.4)                 # a corner ledge: from here to the walls' inside face, both ways (8 x 8 m)
LANE = (15.2, 18.2)                  # a bridge's lane along a wall: its depth from the middle (3 m wide, off the trunks)
SINK = 4.2                           # a withered bridge hangs this far under its storey: out of a jump's reach from above (4.2 m)
#                                      and from a lever's top on the storey below (10 - 4.2 - 2.05 = 3.75 m: more than the 3.14 m jump)
CORNERS = ("ne", "se", "sw", "nw")   # corner c lies between side c and side c + 1; its doorway is on side c, 14.4 m along
BRIDGE_SIDE = {"n": 0, "e": 1, "w": 3}   # the bridges' walls (the south wall carries the stairs): wall k joins corners k - 1 and k
LEDGE_TYPE = "rh_ledge"              # a ledge's doorway: only its caps (a sap-nook with a chest, or grown shut) close it


def corner_xz(c, x, z):
    """A point given in the north-east corner's frame, turned to corner c (a quarter turn each)."""
    p = yaw_turn((x, 0, z), 90 * c)
    return float(p[0]), float(p[2])


def bridge_ends(k):
    """The two corners a bridge on wall k joins."""
    return CORNERS[(k - 1) % 4], CORNERS[k]


# The two shafts: levers (node, bridges it turns), the bridges' first states, found by a search over random shafts (hill climbing
# on a model of this solver) and proved by main() below: the fewest uses with only the declared falls, and with every fall down open.
SHAFTS = {
    "sapshaft_a": ([("nw2", ("e1", "w2")), ("nw1", ("n1", "w2")), ("sw1", ("e1", "n2")), ("ne1", ("e2", "w1")), ("sw1", ("n1", "w1"))],
                   {"e1": 0, "n1": 1, "w1": 0, "e2": 1, "n2": 1, "w2": 1}),
    "sapshaft_b": ([("sw2", ("e1", "n1")), ("sw1", ("w1", "w2")), ("nw1", ("e1", "n2")), ("nw2", ("e2", "w1")), ("in", ("n1", "w1"))],
                   {"e1": 0, "n1": 1, "w1": 0, "e2": 1, "n2": 0, "w2": 1}),
}
SHAFT_EXITS = ("ne2", "se2")         # the heart's doors: on the top storey's north-east ledge (north wall) and south-east one (east)


def lit(part, states):
    """A mechanism's part whose light burns only in these states (its model stays)."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


def sapshaft(t, name, levers, start):
    """The Sap Shaft: a hollow trunk 40 m across and 32 m tall, three storeys 10 m apart. Ledges stand in its four corners on two storeys over the
    ground; the south wall carries root stairs (the ground up to the south-west ledge, and the south-east ledge up to the south-west
    one a storey higher); along the other three walls, root bridges join the corner ledges of a storey: grown (risen level with the
    ledges, amber sap glowing under them) or withered (sunk 4.6 m into the dark, out of reach). Each sap valve (a lever on a ledge, or
    on the ground) sends sap into two roots: the one that was withered grows, the one that was grown withers. The heart's door is on
    the top storey's north-east ledge and its south-east one (either takes the Rotheart's chamber). Under each lower ledge hangs a curtain of root lattice (so nobody slips from a high bridge
    in under a ledge), and the ledges' doorways lead only to sap-nooks."""
    import authoring.themes.roothollow as rh
    H = SH_HALF
    size = (2 * H, 32.0, 2 * H)
    y0, y1, y2 = SH_Y
    doors = [("in", 2, -8.5, y0)]
    for lv, y in ((1, y1), (2, y2)):
        for c in range(4):
            doors.append((f"{CORNERS[c]}{lv}", c, 14.4, y))
    sh = shell(t, name, size, doors, tags=["shaft"], max=1, no_climb=True)
    r = sh.room
    for d in r.doorways:
        if d["id"] != "in":
            d["type"] = HEART_TYPE if d["id"] in SHAFT_EXITS else LEDGE_TYPE
            d["door"] = False
    r.extra["joinBy"] = ["in"]
    r.join("in", "sw1")                      # stair A: the ground to the south-west ledge
    r.join("se1", "sw2")                     # stair B: the south-east ledge to the south-west ledge a storey up
    drops = [{"a": d["id"], "b": "in"} for d in r.doorways if d["id"] != "in"]
    drops.append({"a": "se2", "b": "se1"})   # (off the south-east ledge's west edge onto stair B, whatever the bridges do)
    drops += [{"a": f"{c}2", "b": f"{c}1"} for c in CORNERS if c != "se"]    # (off a high ledge's edge onto the ledge below it)
    r.extra["drops"] = drops
    a, b = LEDGE
    # the ledges: root-ribbed floors in the corners; under each lower one a trunk to the ground, and the root curtain on its inner edges
    for c in range(4):
        x0, z0 = corner_xz(c, a, a)
        x1, z1 = corner_xz(c, b, b)
        for y in (y1, y2):
            t.floor(r, min(x0, x1), max(x0, x1), min(z0, z1), max(z0, z1), y, thickness=1.0)
        px, pz = corner_xz(c, 16.6, 16.6)
        t.pillar(r, (px, y0, pz), y1 - 1.0 - y0, 2.2)
        for p, q in (((a + 0.2, a + 0.2), (a + 0.2, 15.0)), ((a + 0.2, a + 0.2), (15.0, a + 0.2))):
            pa, pb = corner_xz(c, *p), corner_xz(c, *q)
            t.bars(r, (pa[0], 0, pa[1]), (pb[0], 0, pb[1]), y1, y2 - 1.0)
        # over the bridges' mouths into the lower ledge, root lattice from over the heads up to the ledge above: nobody comes off a
        # withered bridge of the storey above (it hangs in the same lane) in under the high ledge
        for p, q in (((a + 0.25, 15.0), (a + 0.25, b)), ((15.0, a + 0.25), (b, a + 0.25))):
            pa, pb = corner_xz(c, *p), corner_xz(c, *q)
            if (c == 1 and q[0] > p[0]) or (c == 2 and q[1] > p[1]):
                continue                                  # (the south wall's mouths: the stairs, with no bridge above them)
            ln = math.hypot(pb[0] - pa[0], pb[1] - pa[1])
            box(r, rh.LATTICE, ((pa[0] + pb[0]) / 2, (y1 + 3.0 + y2 - 1.0) / 2, (pa[1] + pb[1]) / 2), (ln, y2 - 1.0 - y1 - 3.0, 0.35),
                yaw=math.degrees(math.atan2(pb[0] - pa[0], pb[1] - pa[1])) - 90.0)
    # stair A along the south wall, the ground up to the south-west ledge; stair B, the south-east ledge up to its landing a storey
    # higher, and the landing on to the south-west ledge
    lane = -(LANE[0] + LANE[1]) / 2
    width = LANE[1] - LANE[0]
    run = t.stair_run(y1 - y0)
    t.stairs(r, (-a + run, y0, lane), y1 - y0, run, width, yaw=270, fill_below=y0)
    t.stairs(r, (a, y1, lane), y2 - y1, run, width, yaw=270, fill_below=None)
    t.floor(r, -a, a - run, -LANE[1], -LANE[0], y2, thickness=1.0)
    for x in (a - 3.0, a - 7.5, a - run - 1.5):           # (roots under stair B and its landing, into the wall)
        yy = y1 + min(1.0, (a - x) / run) * (y2 - y1) - 2.6
        box(r, rh.ARC, (x, yy, -LANE[1] + 0.3), (3.4, 2.6, 1.0), yaw=90)
    # the bridges: three logs side by side along the wall, sap bulbs hanging under them that glow while they're grown
    for nm, k in BRIDGE_SIDE.items():
        for lv, y in ((1, y1), (2, y2)):
            m = r.mech(f"{nm}{lv}", "platform", side_point(k, 0.0, y, (LANE[0] + LANE[1]) / 2), (0, (90 * k) % 360, 0))
            for i, dz in enumerate((-1.05, 0.0, 1.05)):
                rh.stick(m, rh.OAK, (-a + 0.05, -0.4, dz), (a - 0.05, -0.4, dz), 1.05, spin=180 * (i % 2), dia2=0.8)
            box(m, rh.ARC, (0, -1.5, 0), (2 * a - 2.0, 1.2, 1.6))
            for x in (-6.0, 0.0, 6.0):
                lit(m.add(rh.SAP, (x, -1.3, 0.0), (180, 0, 0), (1.3, 0.6, 1.3)), [1])
            m.pose(at=(0, -SINK, 0)).pose()
            m.start = start[f"{nm}{lv}"]
            m.seconds = 3.0
            e1, e2 = bridge_ends(k)
            m.join(f"{e1}{lv}", f"{e2}{lv}", state=1)
    # the sap valves: on a ledge in its inner corner (two face each other there), or on the ground
    taken = collections.Counter()
    for i, (node, wires) in enumerate(levers):
        n = taken[node]
        taken[node] += 1
        if node == "in":
            at, yaw = (-4.0 + 8.0 * n, y0, 3.0), 180.0
        else:
            c = CORNERS.index(node[:2])
            lx, lz, yaw0 = ((11.6, 14.6, 270.0), (14.6, 11.6, 180.0))[n]
            x, z = corner_xz(c, lx, lz)
            at, yaw = (x, SH_Y[int(node[2])], z), (yaw0 + 90 * c) % 360
        t.lever(r, f"valve{i + 1}", at, yaw, [(w, "next") for w in wires], reached_from=node)
    # light: fungus on the ground round the void, guck pods hanging high over the middle, roots across the roof (the shell's)
    for x, z, s in ((-6.0, 8.0, 0.9), (9.5, -4.0, 0.8), (-2.0, 1.0, 0.7)):
        r.stand(rh.MUSH, (x, y0, z), yaw=x * 31, scale=(s, s, s))
    for x, z, y in ((0.0, 0.0, 11.0), (2.4, -1.8, 10.6), (-2.0, 2.2, 11.4)):
        t.light_hang(r, (x, y, z))
    return r


def solve(room, entry, goal, falls="declared"):
    """The fewest lever uses from doorway `entry` to doorway `goal`, as the layout's solver counts them (after a use the player stands
    at the lever): the room's inside joins, its mechanisms' joins in their states, and its declared drops ("free": any fall to a
    lower doorway too; None: no falls). Returns [lever ids] or None."""
    mechs = [m for m in room.mechs if m.joins]
    states_n = {m.id: len(m.poses) for m in mechs}
    levers = [m for m in room.mechs if m.kind == "lever"]
    ys = {d["id"]: d["at"][1] for d in room.doorways}
    order = [m.id for m in mechs]

    def reach(st, here):
        adj = collections.defaultdict(set)
        for j in room.inside:
            adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for m, s in zip(mechs, st):
            for j in m.joins:
                if j["state"] in (-1, s):
                    adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for j in room.extra.get("drops", []) if falls == "declared" else []:
            adj[j["a"]].add(j["b"])
        if falls == "free":
            for d in ys:
                for e in ys:
                    if ys[e] < ys[d] - 1:
                        adj[d].add(e)
        seen, todo = {here}, [here]
        while todo:
            x = todo.pop()
            for y in adj[x] - seen:
                seen.add(y); todo.append(y)
        return seen
    st0 = (tuple(m.start for m in mechs), entry)
    prev = {st0: None}
    q = collections.deque([st0])
    while q:
        st, at = q.popleft()
        if goal in reach(st, at):
            path, x = [], (st, at)
            while prev[x]:
                path.append(prev[x][1]); x = prev[x][0]
            return list(reversed(path))
        here = reach(st, at)
        for lv in levers:
            if lv.reached_from not in here:
                continue
            ns = list(st)
            for w in lv.wires:
                i = order.index(w["target"])
                ns[i] = (ns[i] + 1) % states_n[w["target"]] if w["do"] == "next" else int(w["do"].split(":")[1])
            key = (tuple(ns), lv.reached_from)
            if key not in prev:
                prev[key] = ((st, at), lv.id)
                q.append(key)
    return None


# ---------------------------------------------------------------- the Seed Garden

def seedgarden(t):
    """The Seed Garden: a hollow 32 m square, split by a rot pit 13 m wide and 8 deep. Glowing seed pods hang over the near
    ledge; the one by the way in is empty and bursts in a cloud as you pass under it. The other ripens when you turn the sap valve
    beside it and drops a glowing seed. On the pit's edge a socket of roots waits: put the seed in it and a root bridge grows up out
    of the rot to the far ledge. (One socket, not two: the solver tracks every pedestal's state through the rest of the dungeon,
    and a second socket here multiplied the Sap Shaft's states past its limit.) A spore pod by the way puffs poison on a beat (it swells and hisses first). Fall in and the rot's creatures are waiting;
    a root stair climbs back up to the near ledge."""
    import authoring.themes.roothollow as rh
    size = (32.0, 24.0, 32.0)
    y0 = -size[1] / 2 + t.floor_t                  # the pit's floor
    y1 = y0 + 8.0                                  # the ledges
    gap = 6.5                                      # the pit runs from -6.5 to 6.5 (z): 13 m, more than the best jump
    sh = shell(t, "seedgarden", size, [("in", 2, 0.0, y1), ("out", 0, 0.0, y1), ("east", 1, -10.0, y1), ("west", 3, 10.0, y1)],
               tags=["garden"], max=1, no_climb=True)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    r.join("out", "east")                          # (the far ledge's three ways)
    r.join("out", "west")
    r.join("east", "west")
    r.extra["drops"] = [{"a": d, "b": "in"} for d in ("out", "east", "west")]   # (into the pit, and up the stair to the near ledge)
    H = size[0] / 2
    run = t.stair_run(y1 - y0)
    sx = 2.6                                       # the stair's head on the near ledge
    t.block(r, -H, H, gap, H, y0 - t.floor_t, y1)                       # the far ledge
    t.block(r, -H, H, -H, -9.6, y0 - t.floor_t, y1)                     # the near ledge, behind the stair
    t.block(r, sx, H, -9.6, -gap, y0 - t.floor_t, y1)                   # and beside the stair's head, to the pit's edge
    t.stairs(r, (sx - run, y0, -8.05), y1 - y0, run, 3.0, yaw=90, fill_below=y0)
    # the bridge: three logs and the sap under them, sunk 4 m in the pit until the socket drinks a seed
    m = r.mech("bridge", "platform", (8.0, y1, 0.0))
    for i, dx in enumerate((-1.05, 0.0, 1.05)):
        rh.stick(m, rh.OAK, (dx, -0.4, -gap + 0.05), (dx, -0.4, gap - 0.05), 1.05, spin=180 * (i % 2), dia2=0.8)
    box(m, rh.ARC, (0, -1.5, 0), (2 * gap - 2.0, 1.2, 1.6), yaw=90)
    for z in (-3.5, 0.0, 3.5):
        lit(m.add(rh.SAP, (0.0, -1.3, z), (180, 0, 0), (1.3, 0.6, 1.3)), [1])
    m.pose(at=(0, -4.0, 0)).pose()
    m.seconds = 4.0
    m.join("in", "out", state=1)
    # the socket on the pit's edge beside the bridge's foot
    for id, x in (("socket", 5.0),):
        p = r.mech(id, "pedestal", (x, y1, -8.1))
        p.stand("root08", (-0.4, 0.0, 0.0), yaw=30, scale=(1.0, 1.4, 1.0))
        p.stand("root08", (0.4, 0.0, 0.2), yaw=210, scale=(1.0, 1.4, 1.0))
        p.fill(rh.HEART, (0, 0.45, 0), (0.9, 0.9, 0.9))
        p.stand(rh.CANDLE, (0.0, 0.9, -0.3), scale=(1.8, 1.8, 1.8))
        p.extra.update({"item": "seed", "accepts": ["seed"], "itemAt": [0, 1.0, 0], "fx": ["sfx_staff_elder_grow"]})
    # the seed pods: the empty one bursts as you come under it; the ripe one drops its seed when the sap valve beside it is turned
    pods = {}
    for id, x, z in (("pod1", 5.0, -12.4), ("pod2", -5.6, -12.4)):
        pm = r.mech(id, "platform", (x, y1, z))
        shown(pm.add(rh.POD, (0, 6.5, 0), (0, x * 20, 0), (1.1, 1.1, 1.1)), [0])
        rh.stick(r, rh.SHOOT, (x, y1 + 8.6, z), (x, size[1] / 2 - 1.0, z), 0.3)
        pm.pose().pose()
        pm.seconds = 0.0
        pods[id] = (x, z)
    t.lever(r, "valve", (-8.0, y1, -11.0), 270.0, [("pod2", "set:1")], reached_from="in")    # (one pull: the pod ripens once)
    x1, z1 = pods["pod1"]
    x2, z2 = pods["pod2"]
    r.extra["triggers"] = [
        {"id": "burst1", "on": {"event": "enter", "at": [x1, y1 + 1.0, z1], "size": [3.6, 3.0, 3.6]}, "solver": "ignore",
         "do": [{"set": "pod1", "to": 1}, {"sound": "sfx_GuckSackDestroyed", "at": [x1, y1 + 6.5, z1]}]},
        {"id": "burst2", "on": {"event": "lever", "mech": "valve"},
         "do": [{"item": "seed", "at": [x2, y1 + 0.4, z2 + 0.4]},
                {"sound": "sfx_GuckSackDestroyed", "at": [x2, y1 + 6.5, z2]}, {"message": "$dungeons_rootdeep_garden_seed", "to": "room"}]},
    ]
    # (on the socket reaching state 1: it's only 1 once a seed is really in it. The place event works the same now; an older solver
    # applied a place event's actions without the use's need of a seed, and this pack was gated with the state event)
    r.extra["triggers"].append(
        {"id": "grow", "on": {"event": "state", "mech": "socket", "state": 1},
         "do": [{"set": "bridge", "to": 1}, {"sound": "sfx_staff_elder_grow", "at": [8.0, y1, 0.0]},
                {"message": "$dungeons_rootdeep_garden_grown", "to": "room"}]})
    # a spore pod by the way: it swells and hisses, then puffs poison over the floor round it, every 6 s
    trap = r.mech("spores", "trap", (-2.6, y1, -10.2))
    trap.stand(rh.POD_S, (0, 0, 0), yaw=20, scale=(1.0, 0.8, 1.0))
    trap.pose().pose(at=(0, 0.12, 0))
    trap.start = 1
    trap.extra.update({"damage": "poison", "scale": 0.8, "every": 6.0, "for": 1.0, "area": [3.6, 2.4, 3.6], "areaAt": [0, 1.2, 0],
                       "fx": ["vfx_BombBlob_explode_poison", "sfx_GuckSackDestroyed"], "warn": 1.2, "warnFx": ["sfx_GuckSackHit"],
                       "warnLift": 0.1})
    # the garden: pods and fungus round the near ledge, rot and roots in the pit, the rot's creatures waiting down there
    for x, z, s in ((-10.0, -11.5, 0.8), (11.0, 9.5, 0.75)):
        if s:
            r.stand(rh.MUSH, (x, y1, z), yaw=x * 33, scale=(s, s, s))
    r.stand(rh.POD_S, (-8.5, y1, -12.8), yaw=20)
    r.stand(rh.TAR, (-4.0, y0 - 0.25, 0.0), scale=(0.3, 0.06, 0.2))
    r.stand(rh.GNARL, (-11.0, y0, 2.0), yaw=40, scale=(0.6, 0.4, 0.6))
    r.stand("bone_stack", (3.0, y0, 3.5), yaw=10, scale=(1.3, 1.3, 1.3))
    for x, z in ((-10.0, 3.0), (2.0, -1.0)):
        t.spawner(r, "melee", (x, y0, z))
    for x, z in ((0.0, 0.0), (-8.0, -1.5)):
        t.light_hang(r, (x, y1 + 6.0, z))
    return r


def shown(part, states):
    """A mechanism's part shown only in these states (a pod that bursts is gone after)."""
    plain = part.json
    part.json = lambda: dict(plain(), show=list(states))
    return part


def ledge_nook(t):
    """A sap-nook off a ledge (an end cap with floor): a small hollow in the trunk's wall, a chest of the land's loot among seed pods."""
    import authoring.themes.roothollow as rh
    size = (8.0, 10.0, 6.0)
    y = -size[1] / 2 + t.floor_t
    sh = shell(t, "ledge_nook", size, [("in", 2, 0.0, y)], end_cap=True, weight=1.0)
    r = sh.room
    r.doorways[0]["type"] = LEDGE_TYPE
    r.doorways[0]["door"] = False
    t.chest(r, (0.0, y, 1.4), yaw=180)
    r.stand(rh.POD_S, (-2.4, y, 1.2), yaw=40)
    r.stand("root12", (2.4, y, 1.1), yaw=200)
    return r


# ---------------------------------------------------------------- the signature rooms, by name

def signatures(t):
    return {
        "entrance": lambda: entrance(t),
        "cap_root": lambda: cap(t, "cap_root", t.door_type),
        "cap_ledge": lambda: cap(t, "cap_ledge", LEDGE_TYPE, 1.0),
        "ledge_nook": lambda: ledge_nook(t),
        "seedgarden": lambda: seedgarden(t),
        "vault": lambda: vault(t),
        **{name: (lambda n=name, lv=lv, st=st: sapshaft(t, n, lv, st)) for name, (lv, st) in SHAFTS.items()},
    }


def main():
    t = T()
    bad = 0
    for name, (lv, st) in SHAFTS.items():
        r = sapshaft(t, name, lv, st)
        for goal in SHAFT_EXITS:
            p = solve(r, "in", goal)
            free = solve(r, "in", goal, falls="free")
            print(f"{name} to {goal}: {len(p) if p else None} uses with the declared falls ({len(free) if free else None} with every fall "
                  f"down open): {' '.join(p or [])}")
            if not p or not free or len(free) < 8:
                bad += 1
    if "--look" in sys.argv:
        print(write_look())
    return 1 if bad else 0


# ---------------------------------------------------------------- the entrance look (packs/looks/roothollow.json)

LOOK = "roothollow"


def look():
    """The Split Root: a great root of the world-tree arching up out of the ground, split open along its foot where the way in goes
    down into the dark; glowing mushrooms round its foot, guck pods hanging from it, old roots crawling out over the ground, two amber
    sap-lights either side of the split. In the entrance's frame: +z out towards whoever comes, y 0 the ground, the way in at 0."""
    import authoring.themes.roothollow as rh
    parts = []

    def add(prefab, at, euler=(0, 0, 0), scale=(1, 1, 1)):
        o = {"prefab": prefab, "at": [round(float(v), 3) for v in at]}
        if any(abs(v) > 1e-6 for v in euler):
            o["euler"] = [round(float(v), 2) for v in euler]
        if any(abs(v - 1) > 1e-6 for v in scale):
            o["scale"] = [round(float(v), 3) for v in scale]
        parts.append(o)

    class Into:                                    # (stick() writes into a holder's parts: collect them as look parts)
        def add(self, prefab, at, euler=(0, 0, 0), scale=(1, 1, 1), saved=False):
            add(prefab, at, euler, scale)

    into = Into()
    # the great root arching over, its ends plunging into the ground either side
    add("YggdrasilRoot", (-0.5, 7.1, -1.5), (0, 0, 0), (0.45, 0.8, 0.55))
    # the split: two halves of bark leaning together over the way in, a dark throat behind
    for s in (-1, 1):
        rh.stick(into, rh.OAK, (s * 3.7, -1.0, -1.0), (s * 1.5, 7.6, -1.8), 2.6, spin=60 * s)
        rh.stick(into, rh.OAK, (s * 5.8, -1.0, -2.2), (s * 2.8, 7.6, -2.6), 3.0, spin=-30 * s)
        rh.stick(into, rh.BARK, (s * 3.3, 0.1, 0.4), (s * 8.5, -0.6, 4.5), 1.4, axis="z", spin=20)          # roots out over the ground
        rh.stick(into, rh.MOSS, (s * 5.0, 0.2, -1.0), (s * 11.0, -0.5, 1.5), 1.5, axis="z", spin=50)
    add(rh.BACK, (0, 3.2, -3.6), (90, 0, 0), (0.75, 0.3, 1.0))            # the dark down the throat
    add("HugeRoot1", (-6.5, 1.2, 3.5), (0, 200, 0), (0.25, 0.35, 0.3))
    add("HugeRoot1", (7.0, 1.0, 2.5), (0, -20, 0), (0.22, 0.3, 0.28))
    for x, z, s in ((-3.6, 1.6, 1.8), (-5.4, 0.4, 1.2), (3.9, 1.8, 2.2), (6.2, 0.2, 1.3), (-8.0, 3.6, 1.0), (8.4, 4.4, 1.5)):
        add(rh.MUSH, (x, -0.1, z), (0, x * 37, 0), (s, s, s))
    for x, y, z, s in ((-4.6, 7.4, -0.6, 1.0), (5.2, 6.4, -0.4, 0.8), (0.0, 9.6, -1.4, 0.9)):
        add(rh.POD, (x, y, z), (0, x * 50, 0), (s, s, s))
    for s in (-1, 1):
        add(rh.LAVA, (s * 2.5, 0.0, 1.2), (0, 0, 0), (1.5, 1.5, 1.5))
        add(rh.VINE, (s * 2.0, 5.6, -0.6), (0, 180, 0), (1.4, 1.8, 1.0))
    add("root08", (-2.6, 0.0, 3.4), (0, 30, 0), (1.6, 1.6, 1.6))
    add("root12", (2.4, 0.0, 3.0), (0, -40, 0), (1.6, 1.6, 1.6))
    add("Bush01", (-9.5, 0.0, -1.0), (0, 0, 0), (1.2, 1.2, 1.2))
    add("Bush01", (10.0, 0.0, -2.0), (0, 90, 0), (1.4, 1.4, 1.4))
    inside = []
    # the way out, from inside: the split's two halves of bark and its dark throat filled with the day's light beyond
    for s in (-1, 1):
        o = Into()
        before = len(parts)
        rh.stick(o, rh.OAK, (s * 2.6, -0.2, -0.4), (s * 1.0, 6.6, -0.6), 1.4, spin=40 * s)
        inside.append(parts.pop())
    return {"name": LOOK,
            "about": "The Split Root (Rootdeep Hollow): a great root of the world-tree arching up out of the ground, split open along "
                     "its foot where the way in goes down into the dark; two halves of bark leaning together over the opening, glowing "
                     "mushrooms round its foot, guck pods hanging from it, old roots crawling out over the ground, amber sap-lights "
                     "either side of the split. Made by tools/dungeons/rootdeep_rooms.py (python rootdeep_rooms.py --look).",
            "front": 5, "parts": parts, "inside": inside}


def write_look(path=None, render=None):
    import json, os
    here = os.path.dirname(os.path.abspath(__file__))
    path = path or os.path.join(here, "..", "..", "mods", "Dungeons", "packs", "looks", LOOK + ".json")
    data = look()
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write(f'  "name": {json.dumps(data["name"])},\n  "about": {json.dumps(data["about"])},\n  "front": {data["front"]},\n')
        for key in ("parts", "inside"):
            f.write(f'  "{key}": [\n' + ",\n".join("    " + json.dumps(p) for p in data[key]) + f"\n  ]{',' if key == 'parts' else ''}\n")
        f.write("}\n")
    if render:
        from dungeon_view import Scene
        s = Scene()
        for p in data["parts"]:
            s.add(p["prefab"], p["at"], p.get("euler", (0, 0, 0)), p.get("scale", (1, 1, 1)))
        s.add("blackmarble_floor_large", (0, -1.0, 0), (0, 0, 0), (4, 0.5, 4))       # (the ground, for the picture)
        s.mark((0, 0.2, 0), (255, 210, 80), "way in")
        s.render(render, views=((180, 12), (215, 20), (145, 30), (180, 60)), size=(800, 600), title="roothollow entrance look",
                 target=(0, 4, -1), dist=34)
    return path


if __name__ == "__main__":
    sys.exit(main())
