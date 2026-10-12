"""
The tidevault theme's hand-made rooms (pack "drowned"): the Tide Locks (the puzzle), the Anchor Gallery (swinging anchors), the entrance,
the vault, the secret grotto, the shortcut's doors and the end caps. The boss rooms are in tools/dungeons/bosses_drowned.py.

    python -m authoring.themes.tidevault_rooms      (from tools/dungeons) solves the Tide Locks and prints its fewest pulls

THE TIDE LOCKS. A canal hall 73 m long: four stone piers across it and three basins between them. Each pier is a block with an arcade
running through it along the west wall (the low way, its mouths low arches 2.1 m high) and its top paved (the high way, 5.5 m up). In
each basin floats a drowned ship's hull (a platform): LOW, its deck lies level with the arcades' floor and is the low way across; HIGH,
its deck lies level with the pier tops (the high way) and its hull's sides close the arcades' mouths below it. Sluice levers stand on
the pier tops; each turns the tide in one or two basins (every pull: those hulls rise if they're low, sink if they're high). Wells in
the pier tops drop you into the arcade under them (one way); a stair in the first pier joins the two ways there. Which basins a lever
turns is shown by emblems beside it, the same as on the hulls' decks: the dragonhead, the bell, the anchor.

Its nodes (the solver walks between doorways): "in" (the arcade's south end, the way in), "e0" (the first pier's top), "w1"/"e1",
"w2"/"e2" (the middle piers' arcades and tops), "w3" (the last arcade) and "out" (the last pier's top, the way on). The inner ones are
doorways of their own type ("tidelock") closed by flat seal caps: places for the solver, sealed sluice outlets to the eye.

The puzzle (searched offline, search.py in the pack team's notes, re-proved here by solve()): levers e0 (dragonhead + anchor: basins 1
and 3), e1 (bell: basin 2), e2 (dragonhead + bell: basins 1 and 2); start low, low, high; out on the high way. Fewest pulls: 5, with
the way back down a well and up the stair again in the middle of it. Riding a rising hull is no way round it: every lever is up on a
pier top (a fall away from any low deck), and an arcade's low mouth closes to a rising deck within half a metre.
"""
import collections
import math

import numpy as np

from dungeon_parts import box, bounds, yaw_turn, slab, Room, r3
from giant_kit import girder

from ..core import side_xz, rng_for
from .tidevault import (THEME as T, STONE, TILE, FILL, CURB, COLUMN, FONT, RIB, BOARDS, lattice, FIRE, BELL, CHAIN, BAR, KELP, CHEST,
                        HOARD, TYPE, orient, lateral_axis, hang_chain, sea_fire, bell_lantern, kelp, fallen_bell, prow, anchor, above)

PLANKS = "Morkhalla_WoodBoards"     # weathered planking 5 x 0.3 x 5: the hulls' decks and sides
LOCK = "tidelock"                  # the Tide Locks' inner doorways: only their seal caps join them
THRONE = "tidethrone"              # the Tide Throne's way out to the vault: only the vault (and its cap) join it


def shell(name, size, doors, **kw):
    """A plain room of the theme (walls, floor, ceiling, frames, its framing) with doorways [(id, side, lateral, level y)]."""
    from ..shells import Shell
    sh = Shell(T, name, "rect", size, rng_for(name), **kw)
    for id, k, lat, y in doors:
        d = sh.add_door(id, k, lat, 0)
        if y is not None:
            d.y = y
    sh.build()
    return sh


def band(room, k, depth, a, b, y, height=0.4):
    """An olive tide band on side k's wall face (`depth` from the middle), lateral a..b, its bottom at y."""
    axis = lateral_axis(k)
    x, z = side_xz(k, (a + b) / 2, depth - 0.075)
    size = (b - a, height, 0.15) if k % 2 == 0 else (0.15, height, b - a)
    room.fill(BOARDS, (x, y + height / 2, z), size, euler=orient(BOARDS, axis, (0, 1, 0)))


# ---------------------------------------------------------------- the Tide Locks

LK_W = 18.0                                   # the hall's width (x), walls inside
LK_IN = LK_W / 2 - T.wall_t                   # the walls' inside faces (7.5)
LK_TOP, LK_BOT = 10.0, -10.0                  # the box (20 m tall)
PIT = -9.0                                    # the pits' floor
F = -3.2                                      # the arcades' floor (the low way)
HI = F + T.storey                             # the pier tops (the high way): 2.3
ARC_X = (-LK_IN, -2.5)                        # the arcades' lane along the west wall
ARC_H = 4.5                                   # an arcade's height inside
MOUTH_H = 2.1                                 # its mouths' clear height (a low arch: a rising deck closes it at once)
SKIRT = HI - 1.0 - F + 0.1                    # a hull's sides, under its deck: reach the arcade's floor when it floats high (4.6)
# along z: (kind, from, to): piers and basins, south to north
LK_PLAN = [("pier", -35.0, -25.0), ("basin", -25.0, -13.0), ("pier", -13.0, -5.0), ("basin", -5.0, 7.0), ("pier", 7.0, 15.0),
           ("basin", 15.0, 27.0), ("pier", 27.0, 35.0)]
LK_LEN = 70.0 + 2 * T.wall_t
# the puzzle: levers (pier, basins, emblems), stairs (piers), the hulls' first states (0 low, 1 high), the way out
LK_LEVERS = [(0, (1, 3)), (1, (2,)), (2, (1, 2))]
LK_STAIRS = (0,)
LK_START = (0, 0, 1)
EMBLEM = {1: "prow", 2: "bell", 3: "anchor"}


def lk_node(level, k):
    """The solver's name for pier k's low (arcade) or high (top) place."""
    if level == "w":
        return "in" if k == 0 else f"w{k}"
    return "out" if k == 3 else f"e{k}"


def emblem(holder, kind, at, yaw=0.0, size=1.0):
    """A basin's emblem, kept under 2 m tall (no foothold up to a pier top from a deck): a dragonhead prow, a fallen stone bell, an
    anchor lying on its side."""
    at = np.array(at, float)
    if kind == "prow":
        lo, hi = bounds("shipwreck_karve_dragonhead")
        s = 1.9 * size / (hi[1] - lo[1])
        c = (lo + hi) / 2
        holder.add("shipwreck_karve_dragonhead", at - yaw_turn((c[0] * s, lo[1] * s, c[2] * s), yaw), (0, yaw, 0), (s, s, s))
    elif kind == "bell":
        fallen_bell(holder, at, yaw, 1.5 * size)
    else:
        a = yaw_turn((1, 0, 0), yaw)
        L = 2.4 * size
        girder(holder, at + (0, 0.25, 0) - a * L / 2, at + (0, 0.25, 0) + a * L / 2, 0.3, prefab=BAR)
        for s in (-1, 1):
            girder(holder, at + (0, 0.25, 0) + a * L / 2, at + (0, 0.25, 0) + a * L / 2 + yaw_turn((0, 0, s * 0.9), yaw) - a * 0.5, 0.3,
                   prefab=BAR)
        girder(holder, at + (0, 0.25, 0) - a * L / 2 + yaw_turn((0, 0, -0.7), yaw), at + (0, 0.25, 0) - a * L / 2 + yaw_turn((0, 0, 0.7), yaw),
               0.25, prefab=BAR)


def tide_locks():
    W, H = LK_W / 2, LK_LEN / 2
    r = Room("tidelocks", (LK_W, LK_TOP - LK_BOT, LK_LEN), tags=["tidelocks"], max=1, min_depth=2)
    piers = [(a, b) for kind, a, b in LK_PLAN if kind == "pier"]
    basins = [(a, b) for kind, a, b in LK_PLAN if kind == "basin"]
    mid = [(a + b) / 2 for a, b in piers]
    arc_mid = (ARC_X[0] + ARC_X[1]) / 2
    # the doorways: the way in (the first arcade's south end), the way out (the last pier's top, north), and the inner nodes
    r.doorway("in", (arc_mid, F, -H), (0, 0, 1), type=TYPE)
    r.doorway("out", (3.0, HI, H), (0, 0, -1), type=TYPE)
    nodes = {}
    for k, z in enumerate(mid):
        if k > 0:
            nodes[f"w{k}"] = r.doorway(f"w{k}", (-W, F, z), (1, 0, 0), type=LOCK, door=False)
        if k < 3:
            nodes[f"e{k}"] = r.doorway(f"e{k}", (W, HI, z), (-1, 0, 0), type=LOCK, door=False)
    r.extra["joinBy"] = ["in"]
    # the walls (with their openings), the pits' floor, the ceiling
    T.wall_run(r, 2, H, -W, W, LK_BOT, LK_TOP, [(-arc_mid, F)])                 # south: "in" (side 2 laterals are -x)
    T.wall_run(r, 0, H, -W, W, LK_BOT, LK_TOP, [(3.0, HI)])                     # north: "out"
    T.wall_run(r, 3, W, -H, H, LK_BOT, LK_TOP, [(z, F) for z in mid[1:]])       # west: the arcades' nodes (side 3 laterals are +z)
    T.wall_run(r, 1, W, -H, H, LK_BOT, LK_TOP, [(-z, HI) for z in mid[:3]])     # east: the tops' nodes (side 1 laterals are -z)
    T.floor(r, -W, W, -H, H, PIT, thickness=PIT - LK_BOT)
    T.ceiling(r, -W, W, -H, H, LK_TOP)
    for k, (a, b) in enumerate(piers):
        pier(r, k, a, b)
    for k, (a, b) in enumerate(basins, start=1):
        hull(r, k, a, b)
    # the doorways' frames: the way in and out, and the inner nodes' (their caps seal them)
    T.door_frame(r, 2, -arc_mid, F, H - T.wall_t)
    T.door_frame(r, 0, 3.0, HI, H - T.wall_t)
    for k, z in enumerate(mid):
        if k > 0:
            T.door_frame(r, 3, z, F, W - T.wall_t)
        if k < 3:
            T.door_frame(r, 1, -z, HI, W - T.wall_t)
    # levers on the pier tops, their emblems beside them
    for i, (k, bs) in enumerate(LK_LEVERS):
        z = mid[k]
        at = (4.25, HI, z + 1.25 if k else -27.5)
        lv = T.lever(r, f"sluice{i + 1}", at, 90, [(f"tide{b}", "next") for b in bs], reached_from=lk_node("e", k))
        for j, b in enumerate(bs):
            emblem(r, EMBLEM[b], (6.4, HI, z - 1.6 + 2.6 * j), yaw=90, size=0.7)
    # the stair in the first pier joins its arcade and its top
    for k in LK_STAIRS:
        r.join(lk_node("w", k), lk_node("e", k))
    r.extra["drops"] = [{"a": lk_node("e", k), "b": lk_node("w", k)} for k in range(1, 4)]
    # light: a bell-lantern over every pier top (hung from the vault), sea-fire at the arcades' mouths and on the tops by the walls
    for k, (a, b) in enumerate(piers):
        z = mid[k]
        bell_lantern(r, (1.5, HI + 4.6, z), scale=1.3, chain_to=LK_TOP - T.floor_t)
        sea_fire(r, (6.3, HI, z - 2.9 if k else z + 3.5))
    # the vault's ribs, high over the tops (no foothold within a jump of a pier top), kelp from them over the basins
    for z in [m for m in mid] + [(a + b) / 2 for a, b in basins]:
        ribs(r, z, HI + 3.6, LK_TOP - T.floor_t, W - T.wall_t)
    for a, b in basins:
        for x in (-6.8, 6.8):
            for zz in (a + 3.0, b - 3.0):
                kelp(r, (x, LK_TOP - T.floor_t, zz), 90 if x < 0 else 270)
    # wreck timbers rammed through the walls, high up (out of any reach): the temple sank onto a fleet
    wreck = ((1, basins[1], "shipwreck_karve_stern", 25), (3, basins[2], "shipwreck_karve_bow", -20))
    for k, (a, b), name, tilt in wreck:
        lo, hi = bounds(name)
        x = (W - T.wall_t - 1.6) * (1 if k == 1 else -1)
        box(r, name, (x, HI + 5.0, (a + b) / 2), (hi - lo) * 0.8, yaw=90 if k == 1 else 270, tilt=(tilt, 0))
    return r


def ribs(r, z, spring, top, half):
    """A pair of carved ship's knees across the hall at z, from both walls at `spring` in under the vault."""
    lo, hi = bounds(RIB)
    rise = top - spring
    if rise < 1.0:
        return
    s = np.array([half / (hi[0] - lo[0]), rise / (hi[1] - lo[1]), 0.7 / (hi[2] - lo[2])])
    for side in (-1, 1):
        r.add(RIB, (side * half, spring, z), (0, 180 if side > 0 else 0, 0), s)     # (a knee: its post side on the wall, +x inwards)


def rock(r, x0, x1, z0, z1, y0, y1):
    """Solid sea-rock from y0 to y1: the piers (their tops bare rock, so the hulls' planked decks stand out against them)."""
    slab(r, x0, x1, z0, z1, y1, STONE, thickness=y1 - y0, cell=6.0)


def pier(r, k, z0, z1):
    """Pier k from z0 to z1: solid from the pits' floor to the high way, but the arcade along the west wall (its floor F, its mouths
    low arches), a well in its top over the arcade (k > 0), and in pier 0 the stair from the arcade up to its top."""
    W = LK_IN
    a0, a1 = ARC_X
    # under the arcade, the arcade's lintels and roof
    T.block(r, a0, a1, z0, z1, PIT, F)
    rock(r, a0, a1, z0, z1, F + ARC_H, HI) if k == 0 else None
    zm = (z0 + z1) / 2
    if k > 0:
        # the roof with the well's hole (2.4 m square over the arcade's middle)
        wx0, wx1, wz0, wz1 = (a0 + a1) / 2 - 1.2, (a0 + a1) / 2 + 1.2, zm - 1.2, zm + 1.2
        for x0, x1, zz0, zz1 in ((a0, wx0, z0, z1), (wx1, a1, z0, z1), (wx0, wx1, z0, wz0), (wx0, wx1, wz1, z1)):
            rock(r, x0, x1, zz0, zz1, F + ARC_H, HI)
        # the well's kerb (low: no step towards anything) and kelp down its throat
        for x0, x1, zz0, zz1 in ((wx0 - 0.4, wx0, wz0 - 0.4, wz1 + 0.4), (wx1, wx1 + 0.4, wz0 - 0.4, wz1 + 0.4),
                                 (wx0, wx1, wz0 - 0.4, wz0), (wx0, wx1, wz1, wz1 + 0.4)):
            r.fill(CURB, ((x0 + x1) / 2, HI + 0.15, (zz0 + zz1) / 2), (x1 - x0, 0.3, zz1 - zz0))
    # the mouths: lintels from the low arch up to the roof, 0.6 m deep at each basin face
    for face, inward in ((z0, 1), (z1, -1)):
        if (k == 0 and inward == 1) or (k == 3 and inward == -1):
            continue                                                # (the hall's end walls)
        za, zb = sorted((face, face + inward * 0.6))
        rock(r, a0, a1, za, zb, F + MOUTH_H, F + ARC_H)
    # the rest of the pier: solid to the top, but the stair's cut in pier 0
    if k == 0:
        sz0, sz1 = -33.5, -30.5                                     # the stair's passage across the pier
        run = 8.5                                                   # (steeper than the theme's: the pier is 10 m across)
        sx1 = a1 + run
        rock(r, a1, W, z0, sz0, PIT, HI)
        rock(r, a1, W, sz1, z1, PIT, HI)
        T.block(r, a1, W, sz0, sz1, PIT, F)
        rock(r, sx1, W, sz0, sz1, F, HI)
        T.stairs(r, (a1, F, (sz0 + sz1) / 2), T.storey, run, sz1 - sz0, yaw=90, fill_below=F)
        # a parapet round the stairwell's open sides on the top (it's a fall onto the stair, joined anyway: just to look like a stair)
        T.rail(r, (a1 + 1.5, 0, sz0 - 0.3), (sx1, 0, sz0 - 0.3), HI, height=0.6)
        T.rail(r, (a1 + 1.5, 0, sz1 + 0.3), (sx1, 0, sz1 + 0.3), HI, height=0.6)
    else:
        rock(r, a1, W, z0, z1, PIT, HI)
    # tide bands along the arcade's west wall, and on the top's walls
    band(r, 3, LK_W / 2, z0 + 0.5, z1 - 0.5, F + 1.0)
    for kk in (1, 3):
        band(r, kk, LK_W / 2, (-z1 + 0.5) if kk == 1 else (z0 + 0.5), (-z0 - 0.5) if kk == 1 else (z1 - 0.5), HI + 1.1)


def hull(r, k, z0, z1):
    """Basin k's drowned hull (mechanism "tide<k>", a platform): its deck (wreck planking) and its sides (the planking stood up under
    the deck's ends, closing the arcades' mouths when it floats high). State 0: low (its deck the arcades' floor), 1: high (the pier
    tops'). Low it joins the arcades either side, high the tops."""
    zc = (z0 + z1) / 2
    L = z1 - z0 - 0.2
    m = r.mech(f"tide{k}", "platform", (0, F, zc))
    slab(m, -LK_IN + 0.05, LK_IN - 0.05, -L / 2, L / 2, 0.0, PLANKS, thickness=0.3, cell=5.0)
    slab(m, -LK_IN + 0.05, LK_IN - 0.05, -L / 2, L / 2, -0.3, PLANKS, thickness=0.7, cell=5.0)       # (planks under planks: no rock through their gaps)
    for s in (-1, 1):
        box(m, PLANKS, (0, -1.0 - SKIRT / 2, s * (L / 2 - 0.25)), (2 * LK_IN - 0.1, 0.5, SKIRT), tilt=(-90, 0))
    # the keel and ribs under the deck (seen from the pier tops when it lies low, from the arcades when it rides high)
    for x in (-4.0, 0.0, 4.0):
        box(m, "darkwood_beam4x4", (x, -1.25, 0), (L - 1.0, 0.5, 0.6), yaw=90)
    emblem(m, EMBLEM[k], (5.0, 0.0, 0.0), yaw=0 if k != 2 else 90)
    m.pose().pose(at=(0, T.storey, 0))
    m.start = LK_START[k - 1]
    m.seconds = 5.0
    m.join(lk_node("w", k - 1), lk_node("w", k), state=0)
    m.join(lk_node("e", k - 1), lk_node("e", k), state=1)
    # (the pit under it: sea-fire glowing on its floor, seen when the hull rides high)
    sea_fire(r, (-3.0, PIT, zc), scale=0.8)
    return m


def solve_locks(room):
    """The fewest sluice pulls from "in" to "out" (as the layout's solver counts them: after a pull the player stands at the lever's
    place), with the room's joins, its hulls' joins in their states and its drops. Returns the pulls (lever ids) or None."""
    hulls = [m for m in room.mechs if m.kind == "platform" and m.id.startswith("tide")]
    levers = [m for m in room.mechs if m.kind == "lever"]

    def reach(state, here):
        adj = collections.defaultdict(set)
        for j in room.inside:
            adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for i, m in enumerate(hulls):
            for j in m.joins:
                if j["state"] in (-1, state[i]):
                    adj[j["a"]].add(j["b"]); adj[j["b"]].add(j["a"])
        for j in room.extra.get("drops", []):
            adj[j["a"]].add(j["b"])
        seen, todo = {here}, [here]
        while todo:
            x = todo.pop()
            for y in adj[x] - seen:
                seen.add(y); todo.append(y)
        return seen
    start = (tuple(m.start for m in hulls), "in")
    prev = {start: None}
    q = collections.deque([start])
    while q:
        st, at = q.popleft()
        here = reach(st, at)
        if "out" in here:
            path, x = [], (st, at)
            while prev[x]:
                path.append(prev[x][1]); x = prev[x][0]
            return list(reversed(path))
        for lv in levers:
            if lv.reached_from not in here:
                continue
            ns = list(st)
            for w in lv.wires:
                i = [m.id for m in hulls].index(w["target"])
                ns[i] ^= 1
            key = (tuple(ns), lv.reached_from)
            if key not in prev:
                prev[key] = ((st, at), lv.id)
                q.append(key)
    return None


# ---------------------------------------------------------------- the Anchor Gallery: swinging anchors

AG_SIZE = (18.0, 10.0, 30.0)


def anchor_gallery():
    """A long hall under four great anchors hung from timber yards across it. Each is held up to one side and, on its own beat, swings
    through the middle and up the other side, then back: blunt, the land's trap damage. Before each swing its chain creaks and it
    shudders (1 s). The anchors alternate sides and keep a rolling beat, so there's always a gap to run through; alcoves between them
    to wait in."""
    sh = shell("anchors", AG_SIZE, [("in", 2, 0.0, None), ("out", 0, 0.0, None), ("east", 1, 6.0, None), ("west", 3, -6.0, None)],
               tags=["anchors"], max=1, min_depth=2)
    r = sh.room
    fy = sh.floor_y
    top = sh.top - T.floor_t
    pivot = fy + 6.6
    L = pivot - fy - 0.9                                     # the anchor reaches to 0.9 m over the floor at the bottom of its swing
    tilt = 52.0
    for i, z in enumerate((-9.0, -3.0, 3.0, 9.0)):
        # the yard across the hall it hangs from, and its chain's block
        box(r, "darkwood_beam4x4", (0, pivot + 0.35, z), (2 * (AG_SIZE[0] / 2 - T.wall_t), 0.7, 0.8))
        s = 1 if i % 2 == 0 else -1
        m = r.mech(f"anchor{i + 1}", "trap", (0, pivot, z))
        hang_chain(m, (0, 0, 0), L * 0.35, thick=1.4)
        anchor(m, (0, -L * 0.35, 0), L * 0.65, yaw=0, width=0.45)
        m.pose(euler=(0, 0, s * tilt)).pose(euler=(0, 0, -s * tilt))
        m.start = 1
        m.seconds = 0.5
        m.extra.update({"damage": "blunt", "scale": 1.5, "every": 4.0, "for": 1.0, "offset": round(i * 1.0, 2),
                        "area": [5.0, 2.4, 2.4], "areaAt": [0.0, -(pivot - fy) + 1.2, 0.0],
                        "fx": ["sfx_frozenking_attackmelee_double_whoosh_lead"], "warn": 1.0,
                        "warnFx": ["sfx_frozenking_punchaoe_chain_start"], "warnLift": 0.0})
        # the mark it sweeps: a dark band of wreck planking across the floor
        r.fill(BOARDS, (0, fy + 0.02, z), (2 * (AG_SIZE[0] / 2 - T.wall_t) - 1.0, 0.04, 2.0), euler=orient(BOARDS, (1, 0, 0), (0, 0, 1)))
    # alcoves of light between them: sea-fire fonts against the walls, a bell-lantern at each end
    for z in (-12.5, -6.0, 0.0, 6.0, 12.5):
        for x in (-6.6, 6.6):
            sea_fire(r, (x, fy, z))
    for z in (-12.0, 12.0):
        bell_lantern(r, (0, fy + 5.4, z), scale=1.2)
    for z, x in ((-13.0, -5.0), (13.0, 5.0)):
        r.stand("barrell", (x, fy, z), scale=(1.2, 1.2, 1.2))
    return r


# ---------------------------------------------------------------- two hand-made rooms between the steps: the wreck hall, the kelp way

def wreck_hall():
    """A hall a longship broke its back in: its stern and its bow lie across the room wall to wall, the way on runs through the break
    between them (a set piece you go through, not past). Inside the stern's lee a drowned crew's sea chest: lifting its lid wakes
    them (two small waves)."""
    from shifting_halls import encounter
    sh = shell("wreckhall", (24.0, 10.0, 24.0), [("a", 2, 0.0, None), ("b", 0, 0.0, None)], tags=["setpiece"], max=1)
    r = sh.room
    fy = sh.floor_y
    for name, x, yaw in (("shipwreck_karve_stern", -3.0, 270), ("shipwreck_karve_bow", 3.0, 270)):
        lo, hi = bounds(name)
        r.add(name, (x, fy - lo[1] + 0.0 - 0.37, 0.0), (0, yaw, 0))
    chest = (-6.5, fy, 5.0)
    r.stand(CHEST, chest, yaw=180, saved=True)
    for x, z in ((-8.5, 5.5), (-4.6, 5.8)):
        r.stand("bone_stack", (x, fy, z), yaw=40 * x, scale=(1.2, 1.2, 1.2))
    pts = [(7.0, fy, -6.5), (-7.0, fy, -7.0), (7.5, fy, 6.5), (-2.5, fy, 8.0)]
    encounter(r, "crew", (-10.0, fy, -10.0), (10.0, fy + 6.0, 10.0), "open", [{"melee": 2}, {"melee": 1, "ranged": 1}], pts,
              chest=chest, music="CombatEventL2", reached_from="a")
    bell_lantern(r, (0.0, fy + 5.0, 0.0), scale=1.2)
    for x, z in ((-9.0, -9.0), (9.0, -9.0), (9.0, 9.0)):
        sea_fire(r, (x, fy, z))
    return r


def kelp_way():
    """A passage under two timber tide-gates that slam down across it in turn (traps: before each a chain rattles and the gate draws
    up and shudders, 1 s; blunt, the land's trap damage), kelp hanging from the vault, sea-fire along the walls."""
    sh = shell("kelpway", (12.0, 10.0, 24.0), [("a", 2, 0.0, None), ("b", 0, 0.0, None)], tags=["setpiece"], max=1)
    r = sh.room
    fy = sh.floor_y
    inner = 12.0 / 2 - T.wall_t
    for i, z in enumerate((-4.0, 4.0)):
        m = r.mech(f"tidegate{i + 1}", "trap", (0, fy, z))
        lattice(m, (0, 5.0 + 1.5, 0), 2 * inner - 0.1, 3.0, 0.25)
        m.fill(CURB, (0, 5.0 + 3.0 - 0.2, 0), (2 * inner - 0.1, 0.4, 0.6))
        m.pose().pose(at=(0, -5.0, 0))
        m.start = 1
        m.extra.update({"damage": "blunt", "scale": 1.2, "every": 5.0, "for": 1.0, "offset": round(i * 2.5, 2),
                        "area": [2 * inner, 2.4, 1.6], "areaAt": [0.0, 1.2, 0.0], "fx": ["sfx_rock_destroyed"],
                        "warn": 1.0, "warnFx": ["sfx_frozenking_punchaoe_chain_start"], "warnLift": 0.3})
        # the gate's grooves in the walls, and its mark on the floor
        for s2 in (-1, 1):
            r.fill(CURB, (s2 * (inner - 0.25), fy + 4.0, z), (0.5, 8.0, 0.9))
        r.fill(BOARDS, (0, fy + 0.02, z), (2 * inner - 1.0, 0.04, 1.6), euler=orient(BOARDS, (1, 0, 0), (0, 0, 1)))
    for z in (-9.0, 0.0, 9.0):
        for x in (-inner + 0.9, inner - 0.9):
            sea_fire(r, (x, fy, z), scale=0.5)
    bell_lantern(r, (0.0, fy + 5.6, -9.0), scale=1.0)
    bell_lantern(r, (0.0, fy + 5.6, 9.0), scale=1.0)
    return r


def sunken_shrine():
    """A shrine of the drowned: a pale idol on a stepped plinth in the middle, a ring of sea-fire round it, the bones of those who
    prayed to it, kelp hanging; three ways out. A set piece."""
    sh = shell("sunkenshrine", (18.0, 10.0, 18.0), [("a", 2, 0.0, None), ("b", 0, 0.0, None), ("c", 1, 0.0, None)], tags=["setpiece"],
               max=1)
    r = sh.room
    fy = sh.floor_y
    r.fill(FILL, (0, fy + 0.2, 0), (5.0, 0.4, 5.0))
    r.fill(TILE, (0, fy + 0.5, 0), (3.6, 0.2, 3.6))
    r.stand("StatueEvil", (0, fy + 0.6, 0.3), yaw=180, scale=(0.85, 0.85, 0.85))
    for i in range(6):
        a = math.radians(30 + 60 * i)
        sea_fire(r, (math.sin(a) * 4.2, fy, math.cos(a) * 4.2), scale=0.5)
    for x, z, y in ((-6.0, 5.8, 30), (6.0, -5.8, 200), (-6.2, -5.6, 120)):
        r.stand("skull_pile", (x, fy, z), yaw=y, scale=(1.2, 1.2, 1.2))
    r.stand("rug_seal", (0, fy, -4.6), scale=(1.2, 1.2, 1.2))
    bell_lantern(r, (0, fy + 5.6, 0), scale=1.1)
    return r


def ships_hold():
    """A drowned ship's hold: casks and crates stacked against the walls among wreck timber, a coil of anchor chain, a sea chest
    left open on the floor (a guarded chest: its crew wake when it's opened); three ways out."""
    from shifting_halls import encounter
    sh = shell("shipshold", (12.0, 10.0, 30.0), [("a", 2, 0.0, None), ("b", 0, 0.0, None), ("c", 3, 7.0, None)], max=1)
    r = sh.room
    fy = sh.floor_y
    inner = 6.0 - T.wall_t
    for z in (-11.0, -6.5, 1.0, 10.5):
        for x, s2 in ((-inner + 0.7, 1), (inner - 0.7, -1)):
            if x < 0 and abs(z - 7.0) < 4.0:
                continue
            r.stand("barrell", (x, fy, z), yaw=30 * z, scale=(1.2, 1.2, 1.2))
            r.stand("CargoCrate", (x + s2 * 1.3, fy, z + 1.4), yaw=15 * z, scale=(1.3, 1.3, 1.3))
    lo, hi = bounds("shipwreck_karve_sternpost")
    r.add("shipwreck_karve_sternpost", (inner - 0.8, fy - lo[1] - 0.2, -2.0 - (lo[2] + hi[2]) / 2), (0, 0, 0))
    chest = (2.5, fy, 5.5)
    r.stand(CHEST, chest, yaw=200, saved=True)
    pts = [(0.0, fy, -1.0), (0.5, fy, -6.0), (-2.5, fy, 10.0)]
    encounter(r, "hold", (-inner, fy, -13.0), (inner, fy + 6.0, 13.0), "open", [{"melee": 2}, {"melee": 1, "ranged": 1}], pts,
              chest=chest, music="CombatEventL2", reached_from="a")
    bell_lantern(r, (0, fy + 5.4, -4.0), scale=1.0)
    bell_lantern(r, (0, fy + 5.4, 8.0), scale=1.0)
    return r


# ---------------------------------------------------------------- the entrance, the vault, the grotto, shortcut doors, caps

def entrance():
    """The way in: the Drowned Gate, a long hall under hull ribs, a great bell fallen across its far end, the first sea-fire."""
    sh = shell("entrance", (18.0, 10.0, 24.0), [("in", 2, 0.0, None), ("n", 0, 0.0, None), ("e", 1, 3.0, None), ("w", 3, -3.0, None)],
               tags=["entrance"])
    r = sh.room
    r.entrance = True
    r.doorways[0]["entrance"] = True
    fy = sh.floor_y
    for x in (-4.5, 4.5):
        sea_fire(r, (x, fy, -8.0))
    fallen_bell(r, (5.0, fy, 7.0), 30, 3.2)
    r.stand("StatueEvil", (-5.6, fy, 7.5), yaw=150, scale=(0.9, 0.9, 0.9))
    bell_lantern(r, (0, fy + 5.2, 0), scale=1.4)
    r.stand("rug_seal", (0, fy, -3.0), scale=(1.3, 1.3, 1.3))
    return r


def vault():
    """The Tidecaller's hoard, behind the throne: his great chest on a stepped plinth among sea chests and fallen bells; a plate inside
    the door lifts the sea-gate in the far wall, and behind it the way out (an exit)."""
    sh = shell("vault", (18.0, 10.0, 24.0), [("in", 2, 0.0, None)], tags=["end"], max=1)
    r = sh.room
    r.doorways[0]["type"] = THRONE
    r.doorways[0]["door"] = False
    fy = sh.floor_y
    r.fill(FILL, (0, fy + 0.25, 1.5), (7.0, 0.5, 6.0))
    r.fill(TILE, (0, fy + 0.6, 1.5), (5.0, 0.2, 4.0))
    r.stand(HOARD, (0, fy + 0.7, 1.8), yaw=180, saved=True)
    for x in (-4.6, 4.6):
        r.stand(CHEST, (x, fy, 3.5), yaw=180, saved=True)
        sea_fire(r, (x, fy, -4.5))
    fallen_bell(r, (-5.4, fy, -1.0), 70, 2.0)
    emblem(r, "anchor", (5.5, fy, -1.0), yaw=90, size=1.0)
    # the far wall's sea-gate and the way out behind it
    for x0, x1 in ((-7.5, -2.2), (2.2, 7.5)):
        box(r, STONE, ((x0 + x1) / 2, fy + 3.5, 8.0), (x1 - x0, 7.0, 1.0))
    box(r, STONE, (0, fy + 5.25, 8.0), (4.4, 3.5, 1.0))
    g = r.mech("exitgate", "platform", (0, fy, 8.0))
    lattice(g, (0, 1.75, 0), 4.4, 3.5, 0.25)
    g.pose().pose(at=(0, 3.4, 0))
    g.seconds = 3.0
    p = T.plate(r, "plate", (0, fy, -7.0))
    p.wire("exitgate", "set:1")
    p.reached_from = "in"
    out = r.mech("exit", "exit", (0, fy, 10.0), (0, 180, 0))
    out.fill(STONE, (0, 1.8, 0.4), (4.0, 3.6, 0.4))
    out.pose()
    bell_lantern(r, (0, fy + 5.0, 0.0), scale=1.3)
    T.spawner(r, "melee", (-3.5, fy, -2.0))
    T.spawner(r, "ranged", (3.5, fy, -2.0))
    return r


def cracked_panel(room, id, half, thr, doorway):
    """A doorway walled up with cracked rock (a breakable), dark behind the gaps, a corner fallen out, kelp over it."""
    from ..sidejobs import cracked
    b = room.mech(id, "breakable", side_xz_point(2, 0.0, thr, half - 0.5), (0, 180, 0))
    b.pose().pose()
    b.doorway = doorway
    b.extra["health"] = 300
    cracked(T, b, T.door_w + 0.2, T.door_h + 0.1)
    return b


def side_xz_point(k, lateral, y, depth):
    x, z = side_xz(k, lateral, depth)
    return np.array([x, y, z])


def secret_grotto():
    """A secret: a sea-cave behind cracked stone off a spare doorway, its pool of glowing polyps, a drowned idol, a sea chest."""
    sh = shell("secret_grotto", (12.0, 10.0, 12.0), [("in", 2, 0.0, None)], tags=["secret"], max=1)
    r = sh.room
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    fy = sh.floor_y
    cracked_panel(r, "crack", 6.0, fy, "in")
    r.extra["hiddenBy"] = "crack"
    r.stand(CHEST, (0, fy, 3.0), yaw=180, saved=True)
    r.stand("StatueEvil", (-3.0, fy, 3.0), yaw=170, scale=(0.8, 0.8, 0.8))
    for x, z in ((3.0, 2.5), (2.6, -1.5), (-3.2, -1.0)):
        r.stand("GlowingMushroom", (x, fy, z), yaw=40 * x, scale=(0.9, 0.9, 0.9))
    r.stand("skull_pile", (3.4, fy, -3.4), scale=(1.2, 1.2, 1.2))
    return r


def shortcut_door(name, deep):
    """One end of the shortcut (kind "shortcut", pair "sc1"): a sealed sea-gate of rock with an anchor on its face; the near one
    chained shut from this side. Its +z faces the room players stand in."""
    r = Room(name, (8, 10, 0), end_cap=True, weight=0.01, max=1)
    thr = -10 / 2 + T.floor_t
    r.doorway("in", (0, thr, 0), (0, 0, 1), type=TYPE)
    d = r.mech("shortcut", "shortcut", (0, thr, 0), (0, 180, 0))
    d.pose().pose()
    d.fill(STONE, (0, T.door_h / 2, -0.45), (T.door_w + 0.2, T.door_h + 0.1, 0.8))
    emblem(d, "anchor", (0, T.door_h * 0.5, 0.15), yaw=0, size=0.7)
    if not deep:
        hang_chain(d, (-1.6, T.door_h - 0.2, 0.2), 3.0)
        hang_chain(d, (1.6, T.door_h - 0.2, 0.2), 3.0)
    d.extra.update({"pair": "sc1", "deep": deep})
    return r


def _cap(name, kind, type_=TYPE, weight=0.3):
    r = Room(name, (8, 10, 0), end_cap=True, weight=weight)
    thr = -10 / 2 + T.floor_t
    r.doorway("in", (0, thr, 0), (0, 0, 1), type=type_, door=False if type_ != TYPE else True)
    if kind == "seal":
        r.fill(STONE, (0, thr + T.door_h / 2, 0.45), (T.door_w + 0.4, T.door_h + 0.2, 0.8))
        band(r, 0, 0.0, -T.door_w / 2 - 0.2, T.door_w / 2 + 0.2, thr + 1.1)
    elif kind == "grille":
        r.fill(STONE, (0, thr + T.door_h / 2, 0.75), (T.door_w + 0.4, T.door_h + 0.2, 0.5))
        lattice(r, (0, thr + T.door_h / 2, 0.3), T.door_w + 0.2, T.door_h, 0.2)
    else:
        r.fill(STONE, (0, thr + T.door_h / 2, 0.7), (T.door_w + 0.4, T.door_h + 0.2, 0.6))
        lo, hi = bounds("shipwreck_karve_bottomboards")
        box(r, BOARDS, (0.4, thr + 1.6, 0.2), (T.door_w * 0.9, 0.2, 3.0), yaw=12, tilt=(-70, 0))
        r.stand("barrell", (-1.2, thr, -0.2), yaw=30, scale=(1.1, 1.1, 1.1))
    return r


def cap_seal():
    """End cap: the doorway walled up in rock, banded where the tide stood."""
    return _cap("cap_seal", "seal")


def cap_silt():
    """End cap: a passage choked with wreck timber."""
    return _cap("cap_silt", "silt", weight=0.2)


def lock_seal():
    """The Tide Locks' inner doorways' cap: a sealed sluice outlet, a dark timber lattice over rock."""
    return _cap("lock_seal", "grille", type_=LOCK, weight=1.0)


if __name__ == "__main__":
    room = tide_locks()
    p = solve_locks(room)
    print("Tide Locks: fewest pulls", len(p) if p else None, p)
