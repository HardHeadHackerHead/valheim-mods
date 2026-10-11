"""
Jobs with their own shells (their levels are what they are about): the timed crossing (a bridge over a pit) and the one-way drop.
"""
import math

import numpy as np

from dungeon_parts import box

from .core import FREE, WAY, OUT, Plan, JUMP_UP, JUMP_ACROSS

LEVER_CLIMB = 2.5          # how high a lever's top stands (a place to stand: the Dvergr knob is 2.2 m up)
from .recipes import Job, job, NoFit
from .shells import Shell
from .jobs import place_lever


# ---------------------------------------------------------------- a timed crossing: the bridge over a pit

class PitShell(Shell):
    """Two ledges a storey up at the ends of the room, a pit between them (its floor the low level), a doorway on each ledge and one
    out of the pit."""

    def __init__(self, theme, name, size, rng, gap, zc=0.0, **kw):
        self.gap, self.zc = gap, zc                             # zc: the pit's middle along z (one ledge deeper than the other)
        Shell.__init__(self, theme, name, "pit", size, rng, **kw)

    def _layout(self):
        t = self.theme
        self.levels = [self.floor_y, self.floor_y + t.storey]
        self.plan = Plan(self.size, self.levels)
        self.params["gap"] = self.gap
        self.params["pit_at"] = self.zc
        for lv in range(2):
            self.plan.set_mask(lv, lambda x, z, lv=lv: self.on_level(x, z, lv), FREE)

    def on_level(self, x, z, lv):
        if not self.in_shape(x, z):
            return False
        return abs(z - self.zc) < self.gap / 2 - 0.3 if lv == 0 else abs(z - self.zc) > self.gap / 2

    def build(self):
        Shell.build(self)
        t, H = self.theme, self.half
        for s in (-1, 1):
            z0, z1 = sorted((self.zc + s * self.gap / 2, s * H[2]))
            t.block(self.room, -H[0], H[0], z0, z1, self.floor_y - t.floor_t, self.levels[1])
        return self


@job
class Timing(Job):
    """A bridge over a pit that lines up with the ledges only now and then, by itself: a slab turning on a pillar (lined up every
    other quarter turn), or one that sinks into the pit and rises again. A fall lands in the pit, whose own way out leads elsewhere."""
    name = "timing"
    tags = ("timing",)
    weight = 0.6
    max = 1
    door_ids = ("n", "s", "pit")
    features = ("remains", "cover")
    ambient = (0, 2)

    def connect(self, c):
        return True                                             # (the ledges are joined by the bridge only; the pit by falling)

    def make_shell(self, theme, name, rng, vary):
        g = theme.grid
        W = vary.choice("width", (2, 3, 4)) * g
        gap = vary.choice("gap", getattr(theme, "pit_gaps", (1.5 * g, 1.75 * g, 2.0 * g)))   # (always more than the best jump across)
        assert gap > JUMP_ACROSS + 0.5, (theme.name, gap)
        L = gap + 2 * max(g, theme.door_w + 4.5)
        L = math.ceil(L / g) * g
        deeper = vary.choice("deeper ledge", (0, 1, -1))      # one ledge a grid deeper than the other, or both the same
        L += abs(deeper) * g
        sh = PitShell(theme, name, (W, 2 * theme.height, L), rng, gap, zc=deeper * g / 2, tags=self.tags, weight=self.weight,
                      max=self.max)
        sh.no_climb = True                                      # (nothing to climb out of the pit by)
        return sh

    def doors(self, c):
        sh, t, rng = c.shell, c.theme, c.rng
        sh.add_door("n", 0, c.vary.choice("n lateral", (-1.0, 0.0, 1.0)), 1)
        sh.add_door("s", 2, c.vary.choice("s lateral", (1.0, -1.0, 0.0)), 1)
        k = c.vary.choice("pit side", (1, 3))
        reach = sh.gap / 2 - t.door_w / 2 - 1.2
        if reach < 0:
            raise NoFit()
        zz = sh.zc + rng.uniform(-reach, reach)
        sh.add_door("pit", k, float(round(-zz if k == 1 else zz)), 0)
        c.note("doors", [[d.k, d.lateral, d.level] for d in sh.doors.values()])

    def build(self, c):
        t, sh, rng, r = c.theme, c.shell, c.rng, c.room
        r.extra["joinBy"] = ["n", "s"]
        r.extra["drops"] = [{"a": "n", "b": "pit"}, {"a": "s", "b": "pit"}]
        y1 = sh.levels[1]
        bw = c.vary.uniform("bridge width", 2.4, 3.2)
        variant = c.vary.choice("bridge", ("turn", "sink"))
        L = sh.gap - 0.4
        zc = sh.zc
        if variant == "turn" and (L > sh.size[0] - 2 * t.wall_t - 0.4 or sh.gap / 2 - bw / 2 < JUMP_ACROSS + 0.5):
            variant = "sink"                                    # (turned crosswise it must clear the side walls, and its ends must
            #                                                     stay more than a jump (6 m) from the ledges)
        c.note("bridge", [variant, round(bw, 1)])
        if variant == "turn":
            h = y1 - 1.0 - sh.floor_y
            t.pillar(r, (0, sh.floor_y, zc), h, min(2.0, bw * 0.7))
            c.plan.take(0, (0, 0, zc), (2.2, 2.2), force=True)
            b = r.mech("bridge", "rotator", (0, y1, zc))
            box(b, t.p("floor"), (0, -0.5, 0), (bw, 1.0, L))
            t.gear(b, (0, -1.3, 0), min(2.4, bw * 0.9), thickness=0.5, turn=60)
            for k in range(4):
                b.pose(euler=(0, 90 * k, 0))
            b.join("n", "s", state=0)
            b.join("n", "s", state=2)
        else:
            b = r.mech("bridge", "platform", (0, y1, zc))
            box(b, t.p("floor"), (0, -0.5, 0), (bw, 1.0, L))
            b.pose().pose(at=(0, -max(t.storey * 0.5, JUMP_UP + 0.6), 0))     # (sunk: no jump up from it to a ledge)
            b.join("n", "s", state=0)
            if hasattr(t, "chain"):
                for s in (-1, 1):
                    t.chain(r, (s * (bw / 2 + 0.4), sh.top - t.floor_t, zc), sh.top - t.floor_t - y1 - 1.0)
        b.cycle = c.vary.choice("cycle", (5.0, 6.0, 7.0))
        b.seconds = 2.0
        c.note("cycle", b.cycle)
        # the ledges' edges: open (a railing is something to stand on, a jump nearer the other side)
        if False:
            x0, x1 = sh.inner[0], sh.inner[1]
            for s in (-1, 1):
                z = zc + s * (sh.gap / 2 + 0.35)
                for p, q in ((x0 + 0.3, -bw / 2 - 0.4), (bw / 2 + 0.4, x1 - 0.3)):
                    if q - p > 1.0:
                        t.rail(r, (p, 0, z), (q, 0, z), y1)
                        c.plan.set_rect(1, p, q, z - 0.4, z + 0.4, OUT)
            c.note("rails", True)
        # the pit: bones, rubble or crates in it (the theme's), under the bridge's line kept clear
        c.plan.set_rect(0, -bw, bw, zc - sh.gap / 2, zc + sh.gap / 2, WAY, only=(FREE,))


# ---------------------------------------------------------------- a one-way drop

class DropShell(Shell):
    """A storey-high ledge along one end with the way in; the floor below with the way on; the stair back up along a side wall,
    shut at its top by a grate."""

    def __init__(self, theme, name, size, rng, ledge, side=None, **kw):
        self.ledge = ledge
        self.side_wanted = side
        Shell.__init__(self, theme, name, "drop", size, rng, **kw)

    def _layout(self):
        t, rng = self.theme, self.rng
        self.levels = [self.floor_y, self.floor_y + t.storey]
        self.plan = Plan(self.size, self.levels)
        self.zl = -self.half[2] + self.ledge
        self.rise = t.storey
        self.run = t.storey * (1.0 if t.storey >= 8 else 1.4)
        self.sw = max(t.way_width + 0.5, t.door_w * 0.8)
        self.side = self.side_wanted or rng.choice((-1, 1))
        self.sx = self.side * (self.half[0] - t.wall_t - self.sw / 2)
        self.foot_z = self.zl + self.run
        if self.foot_z > self.half[2] - t.wall_t - 0.5:
            raise NoFit()
        self.stair_geo = {"rect": (self.sx - self.sw / 2, self.sx + self.sw / 2, self.zl, self.foot_z)}
        self.params.update({"ledge": self.ledge, "stair": self.side})
        for lv in range(2):
            self.plan.set_mask(lv, lambda x, z, lv=lv: self.on_level(x, z, lv), FREE)

    def on_level(self, x, z, lv):
        if not self.in_shape(x, z):
            return False
        a, b, c, d = self.stair_geo["rect"]
        if lv == 0:
            return z > self.zl + 0.3 and not (a - 0.6 < x < b + 0.6 and z < d + 0.3)
        return z < self.zl

    def build(self):
        Shell.build(self)
        t, H = self.theme, self.half
        t.block(self.room, -H[0], H[0], -H[2], self.zl, self.floor_y - t.floor_t, self.levels[1])
        t.stairs(self.room, (self.sx, self.floor_y, self.foot_z), self.rise, self.run, self.sw, yaw=180, fill_below=self.floor_y)
        return self


@job
class Drop(Job):
    """A one-way drop: off the ledge, a storey down. The stair back up is shut at its top by a grate that a lever at the bottom
    opens."""
    name = "drop"
    tags = ("drop",)
    weight = 0.6
    max = 1
    door_ids = ("top", "bottom")
    features = ("remains", "cover")             # (no pillars: a step back up to the ledge)
    ambient = (1, 2)
    target_from = "bottom"

    def connect(self, c):
        sh = c.shell
        # below: from the way on to the stair's foot; the ledge has only its way in
        return sh.plan.way(0, sh.door_at("bottom", 2.0), np.array([sh.sx, sh.floor_y, sh.foot_z + 1.5]), c.theme.way_width)

    def make_shell(self, theme, name, rng, vary):
        g = theme.grid
        widths = [k for k in range(2, 6) if 2 * theme.door_w + 6.0 <= k * g <= 32.0] or [3]
        W = vary.choice("width", widths) * g
        ledge = max(theme.door_w + 5.0, vary.choice("ledge", (1.0, 1.25, 1.5)) * g) + vary.choice("ledge extra", (0.0, 2.0, 4.0))
        # the floor below: the stair's length, and as much again beyond it for some; where the ledge is low enough to jump to from the
        # lever, room for the lever beyond the best jump from it
        below = theme.storey * (1.0 if theme.storey >= 8 else 1.4) + 1.5
        if theme.storey < LEVER_CLIMB + JUMP_UP + 0.5:
            below = max(below, JUMP_ACROSS + 4.0)
        L = math.ceil((ledge + below) / g) * g + vary.choice("lower floor", (0, 1, 2)) * g
        sh = DropShell(theme, name, (W, 2 * theme.height, L), rng, ledge, tags=self.tags, weight=self.weight, max=self.max,
                       side=vary.choice("stair side", (-1, 1)))
        sh.no_climb = True                                      # (nothing to climb back up to the ledge by)
        return sh

    def doors(self, c):
        sh, t, rng = c.shell, c.theme, c.rng
        sh.add_door("top", 2, rng.choice((-1.0, 0.0, 1.0)) * -sh.side, 1)
        across = sh.half[0] - t.wall_t
        lat_max = across - t.door_w / 2 - 1.0
        cand = [float(l) for l in np.arange(-math.floor(lat_max), math.floor(lat_max) + 0.1, 1.0)
                if abs(l - sh.sx) > sh.sw / 2 + t.door_w / 2 + 1.0]
        if not cand:
            raise NoFit()
        sh.add_door("bottom", 0, rng.choice(cand), 0)
        c.note("doors", [[d.k, d.lateral, d.level] for d in sh.doors.values()])

    def build(self, c):
        t, sh, rng, r, plan = c.theme, c.shell, c.rng, c.room, c.plan
        r.extra["drops"] = [{"a": "top", "b": "bottom"}]
        y1 = sh.levels[1]
        g = r.mech("upgate", "platform", (sh.sx, y1 - 1.0, sh.zl + 0.4))
        gh = min(sh.top - t.floor_t - (y1 - 1.0), t.door_h + 1.0, (y1 - 1.0) - sh.bottom - 0.4)   # (too tall to jump over, short
        #                                                                                             enough to sink away in its box)
        g.fill(t.p("grate"), (0, gh / 2, 0), (sh.sw + 0.2, gh, 0.25))
        g.pose().pose(at=(0, -(gh + 0.2), 0))
        g.seconds = 2.5
        g.join("bottom", "top", state=1)
        bx = sh.sx - sh.side * (sh.sw / 2 + 0.15)
        reach = sh.run                                          # (the whole flight: from beside it, its top is a jump from the ledge)
        t.bars(r, (bx, 0, sh.zl), (bx, 0, sh.zl + reach), sh.floor_y, sh.top - t.floor_t)
        plan.set_rect(0, bx - 0.4, bx + 0.4, sh.zl, sh.zl + reach + 0.4, OUT)
        where = c.vary.choice("lever", ("foot", "far"))
        c.note("lever", where)
        # a lever is something to stand on: where the ledge is within a jump of its top, out of the jump's reach across
        keep = None
        if sh.rise < LEVER_CLIMB + JUMP_UP + 0.5:
            keep = lambda x, z: z > sh.zl + JUMP_ACROSS + 0.5
        if where == "foot":
            self.lever = place_lever(c, "lever", [("upgate", "set:1")], level=0, near=(sh.sx, 0, sh.foot_z), reached_from="bottom",
                                     keep=keep)
        else:
            self.lever = place_lever(c, "lever", [("upgate", "set:1")], level=0, far_from="bottom", reached_from="bottom", keep=keep)
        # the ledge's edge: open all along, or railed with a gap to drop through
        if c.vary.flag("rails", 0.5):
            x0, x1 = sh.inner[0], sh.inner[1]
            gw = t.door_w + 1.0                                 # the gap to drop through: a doorway's width and more
            lo, hi = (x0 + gw / 2 + 0.5, sh.sx - sh.sw / 2 - gw / 2 - 1.0) if sh.side > 0 else                 (sh.sx + sh.sw / 2 + gw / 2 + 1.0, x1 - gw / 2 - 0.5)
            gx = rng.uniform(lo, hi) if hi > lo else (lo + hi) / 2
            for p, q in ((x0 + 0.3, gx - gw / 2), (gx + gw / 2, x1 - 0.3)):
                if q - p > 1.0 and not (min(p, q) < sh.sx + sh.sw / 2 + 0.5 and max(p, q) > sh.sx - sh.sw / 2 - 0.5):
                    t.rail(r, (p, 0, sh.zl - 0.35), (q, 0, sh.zl - 0.35), y1)
            c.note("drop_gap", round(gx, 1))
        plan.reserve(0, sh.sx - sh.sw / 2, sh.sx + sh.sw / 2, sh.foot_z, sh.foot_z + 2.5)

    def targets(self, c):
        return [self.lever.approach]
