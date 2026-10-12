"""
The jobs with mechanisms: a fight, a key (a lever for a gate further on), the gate, a held gate (a plate, a siege, a solo lever up on a
ledge), a timed crossing (a bridge over a pit), a one-way drop, the guardian, and the treasure rooms (two levers, an ambush, a door that
opens by itself). Each keeps what the room does exactly as the solver and the runtime expect, and leaves everything about how it looks
and where things are to the shell, the seed and the theme.
"""
import math

import numpy as np

from dungeon_parts import yaw_turn, box

from .core import JUMP_UP, JUMP_ACROSS, PROP_TOP, side_xz, side_vec, FREE, WAY, TAKEN, OUT, CELL, Door
from .recipes import Job, job, pick_doors, NoFit
from .shells import Shell


# ---------------------------------------------------------------- shared pieces

def encounter(c, id, start_on, waves, points, lock=(), opens=(), reached_from=None, chest=None, music="CombatEventL3",
              level=1, elite_level=3, pause=4.0, timeout=40.0, siege=0, hold_by=None, boss=None, area=None):
    sh = c.shell
    from . import recipes
    if recipes.DIFFICULTY != "normal":
        d = -1 if recipes.DIFFICULTY == "easy" else 1
        waves = [dict(w, melee=max(0 if len(w) > 1 else 1, w.get("melee", 0) + d)) if "melee" in w or d > 0 else dict(w)
                 for w in waves]
    x0, x1, z0, z1 = sh.inner
    lo = np.array([x0, sh.floor_y, z0]) if area is None else np.array(area[0], float)
    hi = np.array([x1, sh.top - 1.0, z1]) if area is None else np.array(area[1], float)
    m = c.room.mech(id, "encounter", (lo + hi) / 2)
    m.pose()
    for t in opens:
        m.wire(t, "set:1")
    m.reached_from = reached_from
    from dungeon_parts import r3
    m.extra.update({"area": r3(hi - lo), "startOn": start_on, "lock": list(lock), "waves": waves, "level": level,
                    "eliteLevel": elite_level, "pause": pause, "timeout": timeout, "points": [r3(p) for p in points]})
    if siege:
        m.extra.update({"siege": siege, "holdBy": hold_by})
    if music:
        m.extra["music"] = music
    if chest is not None:
        m.extra["chest"] = r3(chest)
    if boss:
        m.extra["boss"] = boss
    return m


def front_of(yaw):
    """Where a lever pulled from -z turned by yaw is stood at: the direction from it."""
    v = yaw_turn((0, 0, -1), yaw)
    return np.array([v[0], 0.0, v[2]])


def yaw_facing(frm, to):
    """The yaw whose pull side (-z turned) points from `frm` towards `to` on the floor plan."""
    d = np.array(to, float) - np.array(frm, float)
    return math.degrees(math.atan2(-d[0], -d[2])) % 360


def place_lever(c, id, wires, level=0, far_from=None, near=None, reached_from=None, wall=True, toward=None, keep=None):
    """A lever on a free spot of a level with 2.5 m of clear floor in front of it, turned to face the room (or `toward`), as far as it
    can be from doorway `far_from` (or as near as it can be to point `near`). Returns the mechanism."""
    t, sh, plan, rng = c.theme, c.shell, c.plan, c.rng
    base = t.lever_base
    y = sh.levels[level]
    centre = toward if toward is not None else np.array([0.0, y, 0.0])
    groups = [plan.spots(level, base, rng, margin=0.4, wall=wall, count=400)]
    if wall:                                                          # (then out from the walls, if no wall has room in front)
        groups.append(plan.spots(level, base, rng, margin=0.4, wall=None, count=400))
    for spots in groups:
        if far_from:
            ref = sh.door_at(far_from, 0)
            spots.sort(key=lambda p: -math.hypot(p[0] - ref[0], p[1] - ref[2]))
        elif near is not None:
            spots.sort(key=lambda p: math.hypot(p[0] - near[0], p[1] - near[2]))
    for x, z in [p for spots in groups for p in spots if keep is None or keep(*p)]:
        p = np.array([x, y, z])
        tgt = centre if toward is not None else _room_middle(sh, level, p)
        yaw = yaw_facing(p, tgt)
        yaw = round(yaw / 90.0) * 90.0 % 360                         # (square to the room: the checks' boxes are square too)
        f = front_of(yaw)
        # the approach: 2 m wide, from the base's front to 2.6 m beyond it, must be floor nobody stands on
        a = p + f * (base[1] / 2 + 1.4)
        side = np.array([f[2], 0, -f[0]])
        ok = True
        for u in np.arange(-1.5, 1.55, 0.25):                         # (the lever check counts any box touching a cell)
            for v in np.arange(-1.1, 1.3, 0.45):
                q = a + side * u + f * v
                i, j = plan.ij(q[0], q[2])
                if not (0 <= i < plan.nx and 0 <= j < plan.nz) or plan.grid[level][i, j] not in (FREE, WAY):
                    ok = False
        if not ok or not plan.take(level, p, base, yaw=yaw, margin=0.2):
            continue
        for u in np.arange(-1.0, 1.05, 0.5):
            for v in np.arange(-1.2, 1.35, 0.5):
                q = a + side * u + f * v
                plan.set_rect(level, q[0] - 0.25, q[0] + 0.25, q[2] - 0.25, q[2] + 0.25, WAY, only=(FREE,))
        m = t.lever(c.room, id, p, yaw, wires, reached_from=reached_from)
        m.approach = (level, a)
        return m
    raise NoFit()


def _room_middle(sh, level, p):
    """The middle of a level's floor, to turn things towards."""
    g = sh.plan.grid[level]
    ii, jj = np.nonzero(np.isin(g, (FREE, WAY)))
    if len(ii) == 0:
        return np.array([0.0, 0.0, 0.0])
    x = -sh.half[0] + (ii.mean() + 0.5) * CELL
    z = -sh.half[2] + (jj.mean() + 0.5) * CELL
    return np.array([x, sh.levels[level], z])


def spread_points(c, n, level=0, way_in=None, gap=2.5, keep=None, on_ways=False):
    """n spawn points on free floor, spread round the room: never within 3 m of a doorway or of the first 6 m of the way in. keep:
    a test a point must pass (e.g. which side of a screen it's on)."""
    sh, plan, rng = c.shell, c.plan, c.rng
    doors = [sh.door_at(d, 0) for d in sh.doors]
    seg = None
    if way_in:
        seg = (sh.door_at(way_in, 1.5), sh.door_at(way_in, 6.0))
    out = []
    for x, z in plan.spots(level, (1.0, 1.0), rng, margin=0.8, count=600, gap=gap, allowed=(FREE, WAY) if on_ways else (FREE,)):
        if min(math.hypot(x - d[0], z - d[2]) for d in doors) < 3.2:
            continue
        if keep and not keep(x, z):
            continue
        if seg is not None:
            a, b = seg[0][[0, 2]], seg[1][[0, 2]]
            p = np.array([x, z])
            tt = np.clip(np.dot(p - a, b - a) / max(np.dot(b - a, b - a), 1e-9), 0, 1)
            if np.linalg.norm(p - (a + tt * (b - a))) < 3.2:
                continue
        if all(math.hypot(x - q[0], z - q[2]) >= gap for q in out):
            out.append(np.array([x, sh.levels[level], z]))
            if not on_ways:
                plan.take(level, (x, 0, z), (1.6, 1.6), force=True)  # (nothing may stand on a spawn point)
            else:
                plan.set_rect(level, x - 0.8, x + 0.8, z - 0.8, z + 0.8, WAY, only=(FREE,))
            if len(out) >= n:
                break
    return out


def add_spare(c, id, level=None):
    """A spare doorway on a side none of the room's doorways use yet (the floor's level first): a second way on for the layout when
    the first runs into rooms already placed; unused, it gets an end cap like any other. True if one fits."""
    sh = c.shell
    used = {d.k for d in sh.doors.values()}
    slots = [s for s in sh.slots() if s[0] not in used and (level is None or s[2] == level)]
    if not slots:
        return False
    slots.sort(key=lambda s: (s[2], abs(s[1])))
    k, lat, lv = slots[0] if c.vary.flag("spare middle", 0.5) else c.rng.choice(slots)
    sh.add_door(id, k, lat, lv)
    c.info["params"].setdefault("doors", []).append([k, lat, lv])
    return True


def gate_on(c, id, door, start, kind="gate"):
    sh, t = c.shell, c.theme
    d = sh.doors[door]
    depth = sh.half[2] if d.k % 2 == 0 else sh.half[0]
    across = sh.half[0] if d.k % 2 == 0 else sh.half[2]
    open_dir = 1 if d.lateral <= 0 else -1
    return t.gate(c.room, id, d.k, depth, d.y, start, door, lateral=d.lateral, kind=kind, open_dir=open_dir)


# ---------------------------------------------------------------- a fight

@job
class Fight(Job):
    """An arena: the lever at the far end starts it, both gates shut, three waves (the land's elite last), both open when it's won."""
    name = "fight"
    tags = ("fight",)
    size = "large"
    shapes = ("rect", "round", "L", "balcony")
    weight = 0.6
    max = 1
    door_ids = ("in", "out")
    features = ("pillars", "cover", "remains", "machinery")
    always = ("cover",)                  # (something to fight round, and a reason to move: pillars or the machine)
    one_of = ("pillars", "machinery")
    ambient = (0, 0)

    def doors(self, c):
        # in on the floor (the waves come for it); on a balcony the way out may be up on the gallery
        pick_doors(c, ("in", "out"), c.vary.choice("door pattern", ("through", "turn")), levels={"in": 0})
        # a spare way out on a third side, gated like the other (the layout uses it where the first runs into rooms already placed,
        # or caps it)
        self.spare = c.vary.flag("spare exit", 0.75) and add_spare(c, "out2")
        self.door_ids = ("in", "out", "out2") if self.spare else ("in", "out")

    def build(self, c):
        c.room.extra["joinBy"] = ["in"]
        gate_on(c, "ingate", "in", 1)
        gate_on(c, "outgate", "out", 0)
        if self.spare:
            gate_on(c, "outgate2", "out2", 0)
        self.lever = place_lever(c, "release", [("fight", "start")], far_from="in", reached_from="in")
        c.note("lever", "far")

    def targets(self, c):
        return [self.lever.approach]

    def after_ways(self, c):
        pts = spread_points(c, 6, way_in="in")
        if len(pts) < 4:
            raise NoFit()
        gates = ["ingate", "outgate"] + (["outgate2"] if self.spare else [])
        encounter(c, "fight", "wire", [{"melee": 2, "ranged": 1}, {"melee": 3, "flying": 1}, {"melee": 2, "ranged": 1, "elite": 1}],
                  pts, lock=gates, opens=gates, reached_from="in")
        c.note("points", len(pts))


# ---------------------------------------------------------------- the key and its gate

@job
class Key(Job):
    """The lever for a gate further on (plan step "lever"): in a dead-end alcove, a room on the way, or up on a gallery."""
    name = "lever"
    tags = ("lever",)
    size = "small"
    shapes = ("rect", "round", "L", "balcony")
    weight = 1.0
    max = 1
    ambient = (0, 1)

    def doors(self, c):
        variant = c.vary.choice("key variant", ("alcove", "room")) if c.shell.shape != "balcony" else "room"
        c.note("variant", variant)
        self.door_ids = ("in",) if variant == "alcove" else ("in", "out")
        pick_doors(c, self.door_ids, "any", levels={"in": 0})

    def build(self, c):
        level = 1 if c.shell.shape == "balcony" else 0
        c.note("lever_level", level)
        self.lever = place_lever(c, "lever", [("step:gate1", "set:1")], level=level, far_from="in")

    def targets(self, c):
        return [self.lever.approach]


@job
class Gate(Job):
    """The gate the key opens (plan step "gate", opensWith the lever): its way on is shut until then."""
    name = "gate"
    tags = ("gate",)
    size = "small"
    shapes = ("rect", "round", "L", "split")
    weight = 1.0
    max = 1
    door_ids = ("in", "out")

    def doors(self, c):
        pick_doors(c, self.door_ids, c.vary.choice("door pattern", ("through", "turn")), levels={"in": 0})

    def build(self, c):
        c.room.extra["joinBy"] = ["in"]
        g = gate_on(c, "gate", "in", 0)                         # (the lock is on the doorway the step is joined by)
        g.seconds = 3.0
        t, sh = c.theme, c.shell
        if hasattr(t, "big_gear_wall") and c.rng.random() < 0.8:
            d = sh.doors["in"]
            depth = (sh.half[2] if d.k % 2 == 0 else sh.half[0]) - t.wall_t - 0.4
            lat = d.lateral + (t.door_w + 2.0) * (1 if d.lateral <= 0 else -1)
            across = sh.half[0] if d.k % 2 == 0 else sh.half[2]
            if abs(lat) + 2.5 < across:
                m = t.machine(c.room, "winch")
                t.big_gear_wall(c.room, m, d.k, lat, d.y + t.door_h * 0.75, depth, min(5.0, t.door_h * 0.6), c.rng.choice((-1, 1)) * 18)
                c.note("winch", True)


# ---------------------------------------------------------------- a held gate (plate), a siege, the solo lever on a ledge

@job
class HeldGate(Job):
    """An iron screen across the room with a gate in it, held open while someone stands on the plate before it (and bringing a
    siege: waves while it's held, the screen open for good when they're down); alone, a lever up on a ledge opens it for good."""
    name = "plate"
    tags = ("plate",)
    size = "plate"
    shapes = ("rect",)
    weight = 0.6
    max = 1
    door_ids = ("in", "out", "side")
    features = ("remains", "cover")

    def storeys_in(self, theme):
        rise = max(PROP_TOP + JUMP_UP + 0.4, theme.storey / 2)
        return 1 if rise + theme.lever_base[1] + 3.0 < theme.height - theme.floor_t else 2                 # (only on free floor, never by the ledge; no pillars: a step over the screen)
    ambient = (0, 0)

    def doors(self, c):
        sh, t, rng = c.shell, c.theme, c.rng
        sh.no_climb = True                                      # (the ledge must stay out of reach: no framing to climb)
        # the screen across z at zs: "in" on the south side, "out" north, "side" on an east/west wall beyond the screen
        x0, x1, z0, z1 = sh.inner
        rise = max(PROP_TOP + JUMP_UP + 0.4, t.storey / 2)    # the ledge: out of a jump's reach from the floor or a prop on it
        c.prop_limit = rise - JUMP_UP - 0.6
        self.rise, self.run = rise, rise * 1.2
        self.ledge_len = max(5.5, t.lever_base[1] + 3.5)
        lo = z0 + 0.5 + self.run + self.ledge_len + 1.0
        hi = z1 - t.door_w - 2.6
        if lo > hi or (x1 - x0) < 2 * t.grid - 2 * t.wall_t:
            raise NoFit()
        self.zs = float(round(c.vary.uniform("screen", lo, hi)))
        sh.add_door("in", 2, c.vary.choice("in lateral", (-1.0, 0.0, 1.0)), 0)
        sh.add_door("out", 0, c.vary.choice("out lateral", (1.0, 0.0, -1.0)), 0)
        k = c.vary.choice("side door", (1, 3))
        beyond = (self.zs + t.door_w / 2 + 1.0, z1 - t.door_w / 2 - 1.6)
        if beyond[0] > beyond[1]:
            raise NoFit()
        zz = rng.uniform(*beyond)
        lat = -zz if k == 1 else zz
        sh.add_door("side", k, round(lat), 0)
        doors = [[2, sh.doors["in"].lateral, 0], [0, sh.doors["out"].lateral, 0], [k, lat, 0]]
        # a spare way on, beyond the screen in the other side wall (the layout uses it where the first two run into rooms already
        # placed, or caps it)
        self.spare = False
        if c.vary.flag("spare exit", 0.75):
            zz2 = rng.uniform(*beyond)
            k2 = 4 - k
            sh.add_door("side2", k2, round(-zz2 if k2 == 1 else zz2), 0)
            doors.append([k2, round(-zz2 if k2 == 1 else zz2), 0])
            self.spare = True
        self.door_ids = ("in", "out", "side", "side2") if self.spare else ("in", "out", "side")
        c.note("doors", doors)
        c.note("screen_z", self.zs)

    def build(self, c):
        t, sh, plan, rng, room = c.theme, c.shell, c.plan, c.rng, c.room
        x0, x1, z0, z1 = sh.inner
        zs = self.zs
        gx = c.vary.choice("gap side", (-1, 1)) * c.vary.uniform("gap offset", 0, (x1 - x0) / 2 - t.door_w - 1.0)
        gx = round(gx)
        c.note("gap", gx)
        gw = t.door_w + 0.4
        t.bars(room, (x0, 0, zs), (gx - gw / 2, 0, zs), sh.floor_y, sh.top - t.floor_t)
        t.bars(room, (gx + gw / 2, 0, zs), (x1, 0, zs), sh.floor_y, sh.top - t.floor_t)
        plan.set_rect(0, x0, x1, zs - 0.6, zs + 0.6, OUT)
        g = room.mech("screen", "platform", (gx, sh.floor_y, zs))
        g.fill(t.p("grate"), (0, (sh.top - t.floor_t - sh.floor_y) / 2, 0), (gw, sh.top - t.floor_t - sh.floor_y, 0.25))
        slide = gw + 0.2 if gx <= 0 else -(gw + 0.2)
        g.pose().pose(at=(slide, 0, 0.45))
        g.seconds = 1.5
        g.join("in", "out", state=1)
        g.join("in", "side", state=1)
        room.join("out", "side")
        if self.spare:
            g.join("in", "side2", state=1)
            room.join("out", "side2")
        # the plate before the gate
        pz = zs - rng.uniform(2.5, 4.0)
        p = t.plate(room, "plate", (gx + rng.uniform(-1.5, 1.5), sh.floor_y, pz))
        p.wire("screen", "hold")
        p.wire("siege", "start")
        p.reached_from = "in"
        plan.take(0, (p.at[0], 0, p.at[2]), (2.6, 2.6), force=True)
        plan.set_rect(0, x0, x1, zs - 0.6, zs + 0.6, OUT)
        # nothing to stand on within 3 m of the screen either side (a prop by the bars is a step towards their top)
        plan.set_rect(0, x0, x1, zs - 3.0, zs + 3.0, WAY, only=(FREE,))
        # the gap in the screen is floor (a way), the screen itself isn't
        plan.set_rect(0, gx - gw / 2 + 0.3, gx + gw / 2 - 0.3, zs - 0.6, zs + 0.6, WAY)
        self.plate_at = np.array([p.at[0], sh.floor_y, p.at[2]])
        # the solo way: a ledge on the near side, a storey's half up, a stair to it, the lever on it
        side = -1 if gx > 0 else 1
        lw = max(t.door_w * 0.8, 4.0)
        lx0, lx1 = (x0, x0 + lw) if side < 0 else (x1 - lw, x1)
        rise, run = self.rise, self.run
        lz1 = zs - 1.0
        lz0 = lz1 - self.ledge_len
        if lz0 - run < z0 + 0.5:
            raise NoFit()
        ly = sh.floor_y + rise
        t.block(room, lx0, lx1, lz0, lz1, sh.floor_y - t.floor_t, ly)
        sw = min(lw, max(t.way_width, 3.0))
        sx = (lx0 + sw / 2) if side < 0 else (lx1 - sw / 2)
        t.stairs(room, (sx, sh.floor_y, lz0 - run), rise, run, sw, yaw=0, fill_below=sh.floor_y)
        plan.set_rect(0, lx0 - 0.3, lx1 + 0.3, lz0 - run, lz1 + 0.3, OUT)
        lever_at = np.array([(lx0 + lx1) / 2, ly, lz1 - t.lever_base[1] / 2 - 0.4])
        yaw = 0.0                                                     # pulled from -z: the player stands on the ledge, south of it
        m = t.lever(room, "lever", lever_at, yaw, [("screen", "set:1")], reached_from="in")
        self.lever_at = lever_at
        c.note("ledge", side)
        # rail on the ledge's open side (towards the room), leaving the stair's head
        rail_x = lx1 - 0.3 if side < 0 else lx0 + 0.3                 # (on the ledge's edge, not beside it)
        t.rail(room, (rail_x, 0, lz0 + 0.5), (rail_x, 0, lz1), ly)
        # nothing to climb onto it from: no prop, light or cover within a jump (6 m) of the ledge (spawn points may still go there)
        plan.set_rect(0, lx0 - 6.5, lx1 + 6.5, lz0 - 6.5, lz1 + 6.5, WAY, only=(FREE,))

    def targets(self, c):
        return [(0, self.plate_at)]

    def after_ways(self, c):
        x0, x1, z0, z1 = c.shell.inner
        far = spread_points(c, 3, way_in="in", keep=lambda x, z: z > self.zs + 1.5)
        near = spread_points(c, 2, way_in="in", keep=lambda x, z: z < self.zs - 1.5)
        if len(far) + len(near) < 3:                     # (a small room: the floor round the ledge is kept clear of props, not of foes)
            near += spread_points(c, 3 - len(far) - len(near), way_in="in", keep=lambda x, z: z < self.zs - 1.5, on_ways=True)
        pts = far + near
        if len(pts) < 3:
            raise NoFit()
        encounter(c, "siege", "wire", [{"melee": 2, "ranged": 1}, {"melee": 2, "flying": 1}, {"melee": 2, "ranged": 1, "elite": 1}],
                  pts, opens=["screen"], reached_from="in", siege=12, hold_by="plate")


# ---------------------------------------------------------------- the guardian

@job
class Guardian(Job):
    """A long, high hall with room to fight. Step past the threshold and the gates shut; the land's elite at its strongest comes down
    from the dais; when it falls the way on (and the side door, where the shortcut ends) opens."""
    name = "guardian"
    tags = ("guardian",)
    size = "hall"
    shapes = ("rect", "round")
    weight = 1.0
    max = 1
    min_depth = 3
    door_ids = ("in", "out", "side")
    features = ("pillars", "remains")
    ambient = (0, 0)

    def doors(self, c):
        sh = c.shell
        sh.add_door("in", 2, 0.0, 0)
        sh.add_door("out", 0, 0.0, 0)
        slots = [s for s in sh.slots() if s[0] in (1, 3) and s[2] == 0]
        if not slots:
            raise NoFit()
        k, lat, lv = c.rng.choice(slots)
        sh.add_door("side", k, lat, lv)
        c.note("doors", [[2, 0, 0], [0, 0, 0], [k, lat, lv]])

    def build(self, c):
        t, sh, rng, plan = c.theme, c.shell, c.rng, c.plan
        c.room.extra["joinBy"] = ["in"]
        gate_on(c, "ingate", "in", 1)
        gate_on(c, "outgate", "out", 0)
        gate_on(c, "sidegate", "side", 0)
        x0, x1, z0, z1 = sh.inner
        dz = (z1 - t.frame_depth() - 3.5 - t.door_w) if sh.shape != "round" else (z1 * 0.45)
        dw = min(10.0, (x1 - x0) * 0.45)
        dd = min(5.0, (z1 - z0) * 0.18)
        r = c.room
        # the dais, stepped down on every side (0.2 m steps): the guardian must be able to walk off it (the creature check)
        rise, tread, n = 0.2, 0.5, 4
        dw, dd = max(dw, 2 * tread * (n - 1) + 3.0), max(dd, 2 * tread * (n - 1) + 3.0)    # (a top 3 m across at least)
        for i in range(n):
            w, d = dw - 2 * tread * i, dd - 2 * tread * i
            r.fill(t.p("trim"), (0, sh.floor_y + rise * (i + 1) / 2, dz), (w, rise * (i + 1), d))
        plan.take(0, (0, 0, dz), (dw, dd), force=True)
        self.dais = np.array([0.0, sh.floor_y + rise * n, dz])
        c.note("dais", [round(dz, 1), round(dw, 1)])

    def after_ways(self, c):
        sh = c.shell
        x0, x1, z0, z1 = sh.inner
        enter_z = z0 + 5.0
        encounter(c, "guardian", "enter", [{"elite": 1}], [self.dais], lock=["ingate", "outgate"], opens=["ingate", "outgate", "sidegate"],
                  reached_from="in", music="boss_gdking", area=((x0, sh.floor_y, enter_z), (x1, sh.top - 1.0, z1)))


# ---------------------------------------------------------------- treasure rooms (dead ends)

@job
class TwoLevers(Job):
    """A treasure behind a stone door worked by two levers: one raises it a notch at a time, the other drops it shut."""
    name = "twolevers"
    size = "small"
    shapes = ("rect",)
    end_cap = True
    weight = 0.6
    max = 1
    door_ids = ("in",)
    features = ()
    ambient = (0, 0)

    def doors(self, c):
        c.shell.add_door("in", 2, 0.0, 0)
        c.note("doors", [[2, 0, 0]])

    def build(self, c):
        t, sh, rng, plan, r = c.theme, c.shell, c.rng, c.plan, c.room
        r.extra["joinBy"] = ["in"]
        x0, x1, z0, z1 = sh.inner
        front_room = t.frame_depth() + 3.4 + t.lever_base[1] / 2 + 1.0      # the levers' approach clear of the doorway's frame
        wz = max(round(rng.uniform(0.0, 0.25) * (z1 - z0), 1), round(z0 + front_room, 1))
        if wz > z1 - 3.0:
            raise NoFit()
        dw = t.door_w + 0.2
        for a, b in ((x0 - t.wall_t, -dw / 2), (dw / 2, x1 + t.wall_t)):
            t.wall_panel(r, ((a + b) / 2, (sh.floor_y + sh.top) / 2, wz), b - a, sh.top - sh.floor_y, 0)
        top = sh.floor_y + t.door_h + 0.6
        t.wall_panel(r, (0, (top + sh.top) / 2, wz), dw, sh.top - top, 0)
        plan.set_rect(0, x0, x1, wz - t.wall_t / 2, z1, OUT)
        d = r.mech("stonedoor", "platform", (0, sh.floor_y, wz))
        d.fill(t.p("block"), (0, t.door_h * 0.75 / 2 + 0.0, 0), (dw, t.door_h * 0.75, t.wall_t * 0.9))
        steps = rng.choice((3, 4))
        for i in range(steps + 1):
            d.pose(at=(0, i * t.door_h * 0.6 / steps, 0))
        d.seconds = 1.5
        c.note("notches", steps)
        c.note("wall_z", wz)
        lx = (x1 - x0) / 2 - max(t.lever_base[0] / 2, 1.3) - 0.9           # (clear of anything framing the walls)
        first = 1 if rng.random() < 0.5 else -1
        self.fronts = []
        for i, (id, wires) in enumerate((("raise", [("stonedoor", "next")]), ("drop", [("stonedoor", "set:0")]))):
            s = first if i == 0 else -first
            pz = round((wz - t.lever_base[1] / 2 - 1.0 - 0.25) / 0.5) * 0.5 + 0.25     # (on a cell's middle, as place_lever does)
            p = np.array([round((s * lx - 0.25) / 0.5) * 0.5 + 0.25, sh.floor_y, pz])
            plan.take(0, p, t.lever_base, force=True)
            fz = p[2] - t.lever_base[1] / 2
            plan.reserve(0, p[0] - 1.2, p[0] + 1.2, fz - 3.0, fz)
            t.lever(r, id, p, 0.0, wires)
            self.fronts.append((0, np.array([p[0], sh.floor_y, fz - 1.5])))
        cz = (wz + z1) / 2 + 0.5
        t.chest(r, (0, sh.floor_y, cz), yaw=180, saved=True)
        if hasattr(t, "accent"):
            for x in (-1, 1):
                t.accent(r, (x * (x1 - x0) / 3, sh.floor_y, cz + 0.8))

    def targets(self, c):
        return self.fronts


@job
class Ambush(Job):
    """A treasure in plain sight at the far end: lifting the chest's lid shuts the gate behind and brings two waves out of the
    alcoves along the walls; the gate opens when they're down."""
    name = "ambush"
    size = "medium"
    shapes = ("rect", "round")
    end_cap = True
    weight = 0.6
    max = 1
    min_depth = 2
    door_ids = ("in",)
    features = ("pillars",)
    ambient = (0, 0)

    def doors(self, c):
        c.shell.add_door("in", 2, 0.0, 0)
        c.note("doors", [[2, 0, 0]])

    def build(self, c):
        t, sh, plan, r = c.theme, c.shell, c.plan, c.room
        r.extra["joinBy"] = ["in"]
        gate_on(c, "gate", "in", 1)
        x0, x1, z0, z1 = sh.inner
        cz = z1 - 3.5 if sh.shape != "round" else (z1 - 4.5)
        r.fill(t.p("trim"), (0, sh.floor_y + 0.4, cz), (6.0, 0.8, 4.0))
        plan.take(0, (0, 0, cz), (6.0, 4.0), force=True)
        self.chest = np.array([0.0, sh.floor_y + 0.8, cz + 0.4])
        t.chest(r, self.chest, yaw=180, saved=True)
        self.front = np.array([0.0, sh.floor_y, cz - 3.2])
        plan.reserve(0, -1.5, 1.5, cz - 4.5, cz - 2.0)

    def targets(self, c):
        return [(0, self.front)]

    def after_ways(self, c):
        pts = spread_points(c, 6, way_in="in")
        if len(pts) < 4:
            raise NoFit()
        encounter(c, "ambush", "open", [{"melee": 3, "ranged": 1}, {"melee": 2, "flying": 1, "elite": 1}], pts, lock=["gate"],
                  opens=["gate"], reached_from="in", chest=self.chest)


@job
class TimedDoor(Job):
    """A treasure behind a door that opens and shuts by itself every few seconds: get in, grab, get out."""
    name = "timeddoor"
    size = "small"
    shapes = ("rect",)
    end_cap = True
    weight = 0.8
    max = 2
    door_ids = ("in",)
    features = ("machinery",)
    ambient = (0, 0)

    def doors(self, c):
        c.shell.add_door("in", 2, 0.0, 0)
        c.note("doors", [[2, 0, 0]])

    def build(self, c):
        t, sh, plan, r, rng = c.theme, c.shell, c.plan, c.room, c.rng
        r.extra["joinBy"] = ["in"]
        g = gate_on(c, "timedoor", "in", 0, kind="platform")
        g.cycle = rng.choice((4.0, 5.0, 6.0))
        g.seconds = 1.2
        c.note("cycle", g.cycle)
        x0, x1, z0, z1 = sh.inner
        cz = z1 - 2.5
        r.fill(t.p("trim"), (0, sh.floor_y + 0.4, cz), (6.0, 0.8, 3.0))
        plan.take(0, (0, 0, cz), (6.0, 3.0), force=True)
        t.chest(r, (0, sh.floor_y + 0.8, cz), yaw=180, saved=True)
        self.front = np.array([0.0, sh.floor_y, cz - 2.8])

    def targets(self, c):
        return [(0, self.front)]
