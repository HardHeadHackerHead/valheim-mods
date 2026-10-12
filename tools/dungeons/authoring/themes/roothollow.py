"""
The roothollow theme (pack "rootdeep", Rootdeep Hollow): inside the roots of a dying world-tree. Nothing in it is a straight wall or
a flat stone; it is all grown. How it is built (architecture, not a texture):
  - walls: dark layered rock (the earth the roots grew through), pale roots woven over it in patches, and in the rooms of one level
    great roots climbing it aslant; a round room is a hollow in that earth;
  - floors: packed earth (low mounds of old mud over a dark slab); raised levels are dark earth faced with root lattice;
  - ceilings: dark, under great arched roots that cross the room; hanging tendrils with glowing guck pods; vines down the walls;
  - doorways under a root arching over them, gates of root mass that withers up into the roof to open;
  - stairs of timber; railings of a fallen root on slim shoots; screens of pale root lattice;
  - no sawn timber anywhere in sight (the first version's walls of upright logs and floors of laid logs read as a log fort): logs only
    where a root really crosses (the bridges), and their ends buried;
  - light: one cold fungal glow (glowing mushrooms, green guck pods) and AMBER only on what you use (sap bulbs on lever handles,
    the sap valves and sockets): "amber = touch this";
  - dressing: fungus groves, rot (gnarled black stumps, tar), deadfall (mossy logs, roots, bushes), the bones of what died down here.
Scale: doorways 4 x 6 m, 9 m a storey, 13 m a one-storey room, rooms on a 4 m grid; it likes round trunk-hollows, galleries and split
levels. Its signature rooms (rootdeep_rooms.py): the Split Root (the entrance), the Sap Shaft (a three-storey hollow trunk whose root
bridges grow and wither by the sap valves), the Seed Garden (carry seeds to sockets to grow a bridge over the rot); its boss rooms
(bosses_roothollow.py): the Sap-Warden's hollow and the Rotheart's chamber. Pieces: palettes/roothollow.json (builder2's, measured).
"""
import hashlib
import math

import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, yaw_turn, euler_matrix, slab

from ..core import side_xz, TAKEN, FREE, OUT
from ..theme import Theme

OAK = "Oak_log"                       # 2.9 x 13.7 x 2.7 bark trunk, along y: pillars, door posts, bridges (ends buried)
ROCK = "cliff_mistlands1"             # 18.6 x 64.5 x 24.9 dark layered cliff: squashed thin, the rock face of every wall
EARTH = "mudpile_old"                 # 2.9 x 1.5 x 3.2 a heap of old mud: squashed into low mounds, the packed-earth floors
BARK = "SwampTree1_log"               # 1.4 x 1.35 x 6.7 ancient bark log, along z: railings, lintels, treads
MOSS = "FirTree_oldLog"               # 1.3 x 1.6 x 7 mossy log, along z: fallen trunks, curbs
SHOOT = "yggashoot_log_half"          # 0.55 x 4.8 x 0.55 slim shoot, along y: tendrils, rail posts, lever handles (no palette mount:
#                                       a tendril hangs from the roof, a post stands on the floor)
ARC = "HugeRoot1"                     # 29 x 10 x 12 arched root: arches over doorways and across ceilings
STUMP = "SwampTree1_Stub"             # 4.5 x 3.1 x 5.3 a stump with flaring roots: pillar feet, root flares at the walls
GNARL = "AshlandsTreeStump1"          # 2.6 x 7.1 x 2.9 black gnarled roots: rot
LATTICE = "HoleRock_rootWall1"        # 10 x 8.4 x 2.1 pale root lattice (solid): screens, gate faces
BACK = "blackmarble_floor_large"      # 8 x 2 x 8 dark slab: the solid behind the rock, under the earth, the ceiling
HEART = "wood_core_stack"             # 2 x 1.3 x 2.2 a stack of log ends: heartwood blocks, plinths, cover, steps' fill
STEP = "dvergrprops_wood_stair"       # 2.3 x 1.15 x 2.1 timber stair: steps (the map finds "stair")
MUSH = "GlowingMushroom"              # 1.5 x 1.5 x 1.4 glowing mushroom, cold blue-grey light (range 10)
POD = "GuckSack"                      # 2.4 x 3.9 x 2.8 green-glowing guck sack: seed pods
POD_S = "GuckSack_small"
SAP = "SeekerEgg"                     # 0.4 x 1.2 x 0.4 an egg with an orange glow: sap bulbs (what you use)
CANDLE = "Candle_resin"               # a resin candle, amber, walk-through: small amber marks
LAVA = "piece_Lavalantern"            # warm orange lantern (range 12): the sap-fire
VINE = "vines"                        # hanging vines (walk-through)
TAR = "tarlump1"                      # 28 x 8 x 24 lump of tar: rot pools (scaled right down)
CHEST = "TreasureChest_swamp"
VAULT_CHEST = "Morkhalla_ChestAncient"
TYPE = "roothollow"


def h01(*keys):
    """A number in [0, 1) that depends only on its keys: the jitter of grown things, the same on every build."""
    d = hashlib.sha1("/".join(f"{k:.2f}" if isinstance(k, float) else str(k) for k in keys).encode()).hexdigest()
    return int(d[:8], 16) / 2 ** 32


def stick(holder, prefab, a, b, dia, axis="y", spin=0.0, dia2=None):
    """A long piece (a log, a shoot) from point a to point b, `dia` thick: its own `axis` (y or z) laid along a->b. spin: degrees
    about its own length (so two logs side by side don't show the same face). dia2: its thickness the other way across (its own z
    for an axis-y piece, y for an axis-z one; for a log laid flat with spin 0, how tall it stands)."""
    dia2 = dia if dia2 is None else dia2
    a, b = np.array(a, float), np.array(b, float)
    d = b - a
    length = float(np.linalg.norm(d))
    flat = math.hypot(d[0], d[2])
    yaw = math.degrees(math.atan2(d[0], d[2])) if flat > 1e-6 else 0.0
    lo, hi = bounds(prefab)
    if axis == "y":
        tilt = math.degrees(math.atan2(flat, d[1]))                    # from straight up, towards yaw
        euler = np.array([tilt, yaw, 0.0])
        own = np.array([dia / (hi[0] - lo[0]), length / (hi[1] - lo[1]), dia2 / (hi[2] - lo[2])])
        # (a spin about its own y: turn the piece first, then lay it)
        pre = euler_matrix(0, spin, 0)
    else:
        pitch = -math.degrees(math.atan2(d[1], flat))
        euler = np.array([pitch, yaw, 0.0])
        own = np.array([dia / (hi[0] - lo[0]), dia2 / (hi[1] - lo[1]), length / (hi[2] - lo[2])])
        pre = euler_matrix(0, 0, spin)
    R = euler_matrix(*euler) @ pre
    # Unity euler for R: recover it (R is a proper rotation)
    e = _euler_of(R)
    at = (a + b) / 2 - euler_matrix(*e) @ (own * (lo + hi) / 2)
    return holder.add(prefab, at, e, own)


def _euler_of(R):
    """Unity's euler angles (x, y, z) of rotation matrix R (R = Ry Rx Rz)."""
    sx = -R[1, 2]
    sx = max(-1.0, min(1.0, sx))
    x = math.asin(sx)
    if abs(math.cos(x)) > 1e-6:
        y = math.atan2(R[0, 2], R[2, 2])
        z = math.atan2(R[1, 0], R[1, 1])
    else:
        y = math.atan2(-R[2, 0], R[0, 0])
        z = 0.0
    return np.array([math.degrees(x), math.degrees(y), math.degrees(z)])


class RootHollow(Theme):
    name = "roothollow"
    door_type = TYPE
    door_w, door_h = 4.0, 6.0
    wall_t = 1.6
    floor_t = 1.0
    storey = 9.0
    height = 13.0
    grid = 4.0
    step_rise, step_run = 1.0, 1.4      # (a timber stair block a step: its own small steps inside, as the Dvergr theme)
    way_width = 3.0
    panel = 8.0
    lever_base = (1.2, 1.2)
    music = "forestcrypt"
    pit_gaps = (12.0, 13.5, 15.0)        # (more than the best jump across: 10.9 m)

    palette = {
        "wall": [ROCK], "floor": [BARK], "ceiling": [BACK], "trim": [BACK], "pillar": [OAK], "column": [SHOOT], "rail": [BARK],
        "grate": [LATTICE], "door_leaf": [LATTICE], "step": [STEP], "beam": [BARK], "block": [HEART], "chain": [SHOOT],
        "light_stand": [MUSH], "light_hang": [POD_S], "chest": [CHEST],
    }
    structure_prefabs = (BACK, BARK, ARC, ROCK, LATTICE, EARTH)    # (the shell's own: may reach into the walls' band)
    shells = {"round": 3.0, "split": 1.5, "balcony": 1.5, "L": 1.0, "rect": 1.0}
    sizes = {"small": ((3, 4), (3, 4)), "medium": ((4, 5), (4, 6)), "large": ((6, 7), (6, 8)), "corridor": ((2, 2), (4, 6)),
             "hall": ((7, 8), (8, 9)), "nook": ((2, 3), (2, 3)), "plate": ((4, 5), (5, 6))}
    forbid = {"machineroom"}
    dense = 1.0

    # ---------------------------------------------------------------- walls: trunks side by side

    def wall_run(self, room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=None, cell=None):
        # the whole height in one run between the openings (each trunk stands floor to roof: no seam halfway up)
        super().wall_run(room, k, depth, lat_lo, lat_hi, y0, y1, openings, prefab=prefab, cell=60.0)

    def wall_panel(self, room, centre, length, height, yaw, prefab=None):
        """A stretch of wall: a dark solid at the outer face, the layered rock of the earth in front of it, and pale roots woven over
        the rock in a patch or two (never a row of anything). Its face is towards -z turned by yaw."""
        c = np.array(centre, float)
        t = self.wall_t
        out = yaw_turn((0, 0, 1), yaw)
        along = yaw_turn((1, 0, 0), yaw)
        box(room, BACK, c + out * (t / 2 - 0.15), (length + 0.1, 0.3, height), yaw=yaw, tilt=(-90, 0))
        if height < 1.5:
            return                                              # (under a threshold: the floor hides it)
        j = h01(c[0], c[2], c[1], length)
        box(room, ROCK, c + out * (t / 2 - 0.3 - 0.45), (length + 0.05, height, 0.9), yaw=(yaw + (180 if j < 0.5 else 0)) % 360)
        if height < 3.6 or length < 1.6:
            return
        # pale roots over the rock: one patch on a short stretch (every other one, by place), two on a long one, each its own size
        n = 0 if (length < 5.0 and j < 0.45) else (1 if length < 9.0 else 2)
        for i in range(n):
            k = h01(c[0], c[2], i, 9)
            w = min(length - 0.4, length * (0.45 + 0.35 * k) if n > 1 else length * (0.6 + 0.35 * k))
            hh = min(height - 0.4, max(3.0, height * (0.45 + 0.4 * h01(c[0], c[2], i, 10))))
            u = (-1 if i == 0 else 1) * (length - w) / 2 * (0.3 + 0.7 * k) if n > 1 else (length - w) / 2 * (2 * k - 1)
            v = (height - hh) / 2 * (2 * h01(c[2], c[0], i, 11) - 1)
            box(room, LATTICE, c + along * u + out * (-t / 2 + 0.3) + np.array([0, v, 0]), (w, hh, 0.45),
                yaw=(yaw + (180 if k < 0.5 else 0)) % 360)

    # ---------------------------------------------------------------- floors, ceilings, blocks

    def floor(self, room, x0, x1, z0, z1, top, thickness=None, prefab=None):
        """Packed earth: low mounds of old mud (their tops the walking line) side by side over a dark slab a little lower."""
        thickness = thickness or self.floor_t
        sink = 0.3
        slab(room, x0, x1, z0, z1, top - sink, prefab or BACK, thickness=max(0.3, thickness - sink), cell=self.panel)
        self.earth(room, x0, x1, z0, z1, top, sink)

    def earth(self, room, x0, x1, z0, z1, top, sink=0.3):
        """Mounds of old mud over a rectangle, about 6 m each, overlapping: the floor's face."""
        nx = max(1, int(math.ceil((x1 - x0) / 6.0)))
        nz = max(1, int(math.ceil((z1 - z0) / 6.0)))
        dx, dz = (x1 - x0) / nx, (z1 - z0) / nz
        for i in range(nx):
            for k in range(nz):
                ax, bx = max(x0, x0 + i * dx - 0.3), min(x1, x0 + (i + 1) * dx + 0.3)       # (overlapping, never past the rectangle)
                az, bz = max(z0, z0 + k * dz - 0.3), min(z1, z0 + (k + 1) * dz + 0.3)
                j = h01(ax, az, top)
                q = int(4 * j)
                size = (bx - ax, sink, bz - az)                          # (in the room's frame: fill turns it)
                room.fill(EARTH, ((ax + bx) / 2, top - sink / 2, (az + bz) / 2), size, euler=(0, 90 * q, 0))

    def block(self, room, x0, x1, z0, z1, y0, y1, prefab=None):
        """A raised level from y0 to y1: dark earth, packed earth on top, its faces woven over with pale root lattice (nothing to climb:
        never a pile of logs, whose ends are footholds all the way up)."""
        if y1 - y0 <= 0.5:
            slab(room, x0, x1, z0, z1, y1, BACK, thickness=y1 - y0, cell=self.panel)
            return
        slab(room, x0, x1, z0, z1, y1 - 0.3, BACK, thickness=y1 - 0.3 - y0, cell=self.panel)
        self.earth(room, x0, x1, z0, z1, y1)
        if y1 - y0 < 2.0:
            return
        h = y1 - 0.35 - (y0 + 0.1)
        for (ax, az), (bx, bz) in (((x0, z0), (x1, z0)), ((x1, z0), (x1, z1)), ((x1, z1), (x0, z1)), ((x0, z1), (x0, z0))):
            length = math.hypot(bx - ax, bz - az)
            ux, uz = (bx - ax) / length, (bz - az) / length
            nx, nz = uz, -ux                                    # (outwards, for a rectangle walked this way round: it stands in from it)
            n = max(1, int(math.ceil(length / 9.0)))
            for i in range(n):
                u = (i + 0.5) * length / n
                px, pz = ax + ux * u - nx * 0.3, az + uz * u - nz * 0.3
                yaw = math.degrees(math.atan2(ux, uz)) - 90.0
                box(room, LATTICE, (px, y0 + 0.1 + h / 2, pz), (length / n - 0.1, h, 0.45), yaw=yaw)

    # ---------------------------------------------------------------- doorways

    def door_frame(self, room, k, lateral, thr, face):
        """Two trunks either side of the opening, and a root arching over it into the wall."""
        for s in (-1, 1):
            a = side_xz(k, lateral + s * (self.door_w / 2 + 0.55), face - 0.5)
            stick(room, OAK, (a[0], thr - 0.6, a[1]), (a[0], thr + self.door_h + 1.2, a[1]), 1.0, spin=90 * s + 90 * k)
        x, z = side_xz(k, lateral, face - 0.6)
        box(room, ARC, (x, thr + self.door_h + 1.0, z), (self.door_w + 3.2, 2.0, 1.2), yaw=(90 * k) % 360)

    def frame_depth(self):
        return 1.1

    def structure(self, sh):
        """What grows over a shell: great roots arching across under the roof, root flares at the walls' feet, vines down the walls."""
        r, H = sh.room, sh.half
        top = sh.top - self.floor_t
        high = max(sh.levels)
        doors = list(sh.doors.values())
        # roots across the roof (over the heads: 5 m clear over the highest level), across the room's shorter way
        if top - high >= 8.0:
            along_x = sh.size[0] <= sh.size[2]
            width = (H[0] if along_x else H[2]) - self.wall_t
            span_len = (H[2] if along_x else H[0]) - self.wall_t
            n = 1 if span_len < 8 else (2 if span_len < 14 else 3)
            for i in range(n):
                u = -span_len + (i + 1) * 2 * span_len / (n + 1)
                w = width
                if sh.shape == "round":
                    R = H[0] - self.wall_t - 0.8
                    if abs(u) >= R - 1.0:
                        continue
                    w = math.sqrt(R * R - u * u)
                rise = min(3.2, top - high - 5.0)
                y = top - rise / 2 - 0.1
                j = h01(sh.name, i)
                c = (0.0, y, u) if along_x else (u, y, 0.0)
                yaw = (0 if along_x else 90) + (180 if j > 0.5 else 0)
                box(r, ARC, c, (2 * w + 0.6, rise, 1.6 + 0.8 * j), yaw=yaw)
        # vines down the walls, high up (walk-through), between the doorways
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t + 0.05
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
            for i in range(max(1, int(across / 5))):
                lat = -across + (i + 0.5) * 2 * across / max(1, int(across / 5))
                if any(d.k == k and abs(d.lateral - lat) < self.door_w / 2 + 1.5 for d in doors):
                    continue
                x, z = side_xz(k, lat, depth)
                if not sh.in_shape(*side_xz(k, lat, depth - 0.6), margin=-0.4):
                    continue
                y = top - 2.5 - 2.0 * h01(sh.name, k, i)
                if y < high + 4.0:
                    continue
                box(r, VINE, (x, y, z), (2.4, 3.0, 0.4), yaw=(90 * k + 180) % 360)     # (against the trunks: mounted)
        # root flares at the feet of the walls (not where the heights are the puzzle: a flare is a step)
        if getattr(sh, "no_climb", False) or len(sh.levels) > 1:
            return
        y0 = sh.levels[0]
        # roots grown slantwise across the walls' faces, in the walls' own depth (single-level rooms only: a slanting root's box would
        # be a ledge by a gallery), and one root crossing the roof aslant
        self.wall_roots(sh, y0, top)
        if top - y0 >= 8.0:
            along_x = sh.size[0] <= sh.size[2]
            w = (H[0] if along_x else H[2]) - self.wall_t
            if sh.shape == "round":
                w = (H[0] - self.wall_t - 0.8) * 0.8
            j = h01(sh.name, "aslant")
            yaw = (0 if along_x else 90) + (22 if j < 0.5 else -22)
            span = 1.5 * w
            ext = span / 2 * math.cos(math.radians(22)) + 1.0
            if ext < w + self.wall_t * 0.5:
                box(r, ARC, (0.0, top - 1.9, 0.0), (span, 2.6, 1.6), yaw=yaw)
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t
            for i, f in enumerate((-0.6, 0.6)):
                lat = across * f
                if any(d.k == k and abs(d.lateral - lat) < self.door_w / 2 + 3.0 for d in doors):
                    continue
                x, z = side_xz(k, lat, depth - 1.0)
                if not sh.in_shape(x, z, margin=0.2) or not sh.plan.can_take(0, (x, 0, z), (3.2, 3.2)):
                    continue
                sh.plan.take(0, (x, 0, z), (3.2, 3.2), force=True)
                r.stand(STUMP, (x, y0 - 0.3, z), yaw=(90 * k + 180 + 40 * (h01(sh.name, k, i) - 0.5)) % 360, scale=(0.55, 0.5, 0.5))

    def wall_roots(self, sh, y0, top):
        """Great roots climbing the walls aslant between the doorways (arched roots rolled 20-35 degrees in the wall's plane), in front
        of the rock but inside the walls' depth."""
        r, H = sh.room, sh.half
        doors = list(sh.doors.values())
        D = 1.0                                                  # (the root's depth: the wall's band, less the rock)
        if sh.shape == "round":
            Rc = H[0] - self.wall_t + D / 2 + 0.1
            for i in range(6):
                a = 30.0 + 60.0 * i + 12.0 * (h01(sh.name, "wr", i) - 0.5)
                if any(abs((a - 90.0 * dd.k + 180.0) % 360.0 - 180.0) < 32.0 for dd in doors):
                    continue
                L = min(2 * Rc * math.sin(math.radians(14)), 7.0)
                self._wall_root(r, (math.sin(math.radians(a)) * Rc, math.cos(math.radians(a)) * Rc), a + 180.0, L, y0, top,
                                h01(sh.name, "roll", i))
            return
        if sh.notch:
            return
        for k in range(4):
            depth = (H[2] if k % 2 == 0 else H[0]) - self.wall_t + D / 2 + 0.1
            across = (H[0] if k % 2 == 0 else H[2]) - self.wall_t - 0.4
            busy = [(dd.lateral - self.door_w / 2 - 1.5, dd.lateral + self.door_w / 2 + 1.5) for dd in doors if dd.k == k]
            for s in (-1, 1):
                j = h01(sh.name, "wr", k, s)
                L = min(9.0, across * (0.55 + 0.3 * j))
                lat = s * (across - L / 2 - 0.2)
                if L < 4.0 or any(lo < lat + L / 2 and lat - L / 2 < hi for lo, hi in busy):
                    continue
                x, z = side_xz(k, lat, depth)
                self._wall_root(r, (x, z), (90 * k + 180) % 360, L, y0, top, j)

    def _wall_root(self, r, at, face_yaw, L, y0, top, j):
        """One arched root against a wall at `at` (x, z), its face towards the room (face_yaw: the room's way), `L` along the wall,
        rolled 20-35 degrees one way or the other, its foot in the floor."""
        roll = (20.0 + 15.0 * j) * (1 if j < 0.5 else -1)
        H = 2.6
        span_y = L * math.sin(math.radians(abs(roll))) + H * math.cos(math.radians(roll))
        if y0 + span_y > top - 0.6:
            return
        box(r, ARC, (at[0], y0 - 0.3 + span_y / 2, at[1]), (L, H, 1.0), yaw=(face_yaw + 180) % 360, tilt=(0, roll))

    # ---------------------------------------------------------------- stairs, rails, screens, pillars

    def stairs(self, holder, foot, rise, run, width, yaw=0.0, fill_below=None):
        """Timber steps (each a stair block: the map finds the flight), heartwood under them, a root tread on each nose."""
        n = max(1, int(round(rise / self.step_rise)))
        step = run / n
        h = rise / n
        noses = []
        for i in range(n):
            c = np.array(foot, float) + yaw_turn((0, i * h, step * (i + 0.5)), yaw)
            box(holder, STEP, (c[0], c[1] + h / 2, c[2]), (width, h, step), yaw=(yaw + 180) % 360)
            noses.append(np.array(foot, float) + yaw_turn((0, (i + 1) * h - 0.15, step * i + 0.13), yaw))
        if fill_below is not None:
            for i in range(n):
                if foot[1] + i * h > fill_below + 0.05:
                    hh = foot[1] + i * h - fill_below
                    c2 = np.array(foot, float) + yaw_turn((0, 0, step * (i + 0.5)), yaw)
                    box(holder, BACK, (c2[0], fill_below + hh / 2, c2[2]), (width, hh, step), yaw=yaw)   # (smooth: no climbing its side)

    def rail(self, room, a, b, y, height=1.3):
        """A fallen root along the line on slim shoots: a railing."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        if length < 0.2:
            return
        stick(room, BARK, (a[0], y + height - 0.25, a[2]), (b[0], y + height - 0.25, b[2]), 0.5, axis="z", spin=length * 31)
        n = max(2, int(length / 2.5) + 1)
        for i in range(n):
            p = a + d * (i / (n - 1))
            stick(room, SHOOT, (p[0], y, p[2]), (p[0], y + height - 0.2, p[2]), 0.22)

    def bars(self, holder, a, b, bottom, top, thickness=0.25):
        """A screen of pale root lattice from floor to roof between trunks: nothing passes or climbs it."""
        a, b = np.array(a, float), np.array(b, float)
        d = b - a
        length = float(math.hypot(d[0], d[2]))
        yaw = math.degrees(math.atan2(d[0], d[2])) - 90.0
        n = max(1, int(math.ceil(length / 5.0)))
        for i in range(n):
            c = a + d * ((i + 0.5) / n)
            box(holder, LATTICE, (c[0], (bottom + top) / 2, c[2]), (length / n + 0.3, top - bottom, max(thickness, 0.35)), yaw=yaw)
        for i in (0, n):
            c = a + d * (i / n)
            stick(holder, OAK, (c[0], bottom, c[2]), (c[0], top, c[2]), 0.6)

    def pillar(self, room, at, height, width=None):
        """A trunk standing on its own flaring roots."""
        w = width or 1.6
        stick(room, OAK, (at[0], at[1] - 0.1, at[2]), (at[0], at[1] + height, at[2]), w, spin=at[0] * 13 + at[2] * 7)
        lo, hi = bounds(STUMP)
        s = (w + 0.3) / max(hi[0] - lo[0], hi[2] - lo[2])
        room.stand(STUMP, (at[0], at[1] - 0.25, at[2]), yaw=(at[0] * 37 + at[2] * 11) % 360, scale=(s, 0.35, s))
        return (w, w)

    def column(self, room, at, height, width=0.9):
        stick(room, SHOOT, at, (at[0], at[1] + height, at[2]), width)
        return (width, width)

    # ---------------------------------------------------------------- mechanisms

    lever_top = 2.05                     # (a lever's top is a place to stand: the Sap Shaft keeps its withered bridges out of a jump from it)

    def lever(self, room, id, at, yaw, wires, reached_from=None):
        """A sap valve: a stump with a root handle, an amber sap bulb on its end, that tips forward and back. Pulled from -z turned
        by yaw. 2.05 m to the top of its bulb."""
        at = np.array(at, float)
        lo, hi = bounds("Beech_Stub")
        s = 1.25 / (hi[0] - lo[0])
        room.stand("Beech_Stub", at, yaw=yaw + 30, scale=(s, 0.8 / (hi[1] - lo[1]), s))
        pivot = at + (0, 0.8, 0)
        m = room.mech(id, "lever", pivot, (0, yaw, 0))
        stick(m, SHOOT, (0, -0.1, 0), (0, 1.0, 0), 0.2)
        m.add(SAP, (0, 0.85, 0), (0, 0, 0), (1.0, 0.36, 1.0))
        m.pose(euler=(-32, 0, 0)).pose(euler=(32, 0, 0))
        m.seconds = 0.7
        for target, do in wires:
            m.wire(target, do)
        m.reached_from = reached_from
        return m

    def gate(self, room, id, k, half, thr, start, doorway, lateral=0.0, kind="gate", open_dir=1):
        """Root mass in a doorway (a gate closing that doorway in state 0): two skins of pale root lattice, a great root across them. It
        withers up into the roof to open (or aside, where there's room)."""
        x, z = side_xz(k, lateral, half - self.wall_t / 2)
        g = room.mech(id, kind, (x, thr, z), (0, (90 * k) % 360, 0))
        if kind == "gate":
            g.doorway = doorway
        w = self.door_w + 0.4
        box(g, LATTICE, (0, self.door_h / 2, 0.15), (w, self.door_h + 0.1, 0.4))
        box(g, LATTICE, (0, self.door_h / 2, -0.35), (w, self.door_h + 0.1, 0.4), yaw=180)
        box(g, ARC, (0, self.door_h / 2, 0.45), (self.door_h * 0.95, w * 0.55, 0.6), tilt=(0, 70 if h01(id) < 0.5 else -70))
        g.pose()
        rise = room.size[1] / 2 - 0.05 - (thr + self.door_h + 0.1)
        if rise >= self.door_h - 0.5:
            g.pose(at=(0, rise, 0))
        else:
            g.pose(at=(open_dir * (self.door_w + 0.6), 0, 0))
        g.start = start
        g.seconds = 2.5
        g.extra["fx"] = ["sfx_staff_elder_grow"]
        return g

    def plate(self, room, id, at, size=2.4):
        """A knot of roots in the floor that sinks under weight, an amber candle-ring round it."""
        p = room.mech(id, "plate", at)
        p.fill(HEART, (0, 0.08, 0), (size, 0.16, size))
        p.pose().pose(at=(0, -0.1, 0))
        p.seconds = 0.3
        for sx, sz in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
            room.stand(CANDLE, (at[0] + sx * (size / 2 + 0.25), at[1], at[2] + sz * (size / 2 + 0.25)))
        return p

    def chest(self, room, at, yaw=180.0, saved=True):
        room.stand(CHEST, at, yaw=yaw, saved=saved)
        return (1.8, 1.0)

    def spawner(self, room, role, at):
        gk.spawner(room, role, at)

    # ---------------------------------------------------------------- lights

    def light_stand(self, room, at):
        """A small glowing mushroom, or (every other one, by place) a root crawling out of the floor: few lights, and none of them
        big (a full-size glowing mushroom read as a cartoon, and three hundred lights cost the game a third of its frames)."""
        j = h01(at[0], at[2], 7)
        if j < 0.4:
            room.stand(MUSH, at, yaw=j * 720, scale=(0.75, 0.7, 0.75))
        else:
            room.stand("root12", at, yaw=90 * int(8 * j), scale=(0.95, 1.0, 0.48))   # (within the 1.6 m it takes, square: 1.3 m)
        return (1.6, 1.6)

    def light_hang(self, room, at):
        """A green guck pod on a tendril from whatever is above it."""
        lo, hi = bounds(POD_S)
        at = np.array(at, float)
        if h01(at[0], at[2], 8) < 0.5:
            return                                               # (half of them: fewer lights)
        above = [b0[1] for b0, b1 in map(gk.part_box, room.parts)
                 if b0[1] > at[1] + hi[1] + 0.05 and b0[0] <= at[0] <= b1[0] and b0[2] <= at[2] <= b1[2]]
        if above:
            stick(room, SHOOT, (at[0], at[1] + hi[1] - 0.1, at[2]), (at[0], min(above) + 0.1, at[2]), 0.14)
        room.add(POD_S, at, (0, h01(at[0], at[2]) * 360, 0))

    def accent(self, room, at, yaw=0.0):
        room.stand(MUSH, at, yaw=yaw, scale=(0.7, 0.7, 0.7))
        return (1.1, 1.1)

    def chain(self, holder, top, length, yaw=0.0):
        """A hanging root tendril, `length` down from `top`."""
        stick(holder, SHOOT, (top[0], top[1] - length, top[2]), top, 0.3)

    # ---------------------------------------------------------------- the hub's root tree and the bridges' knots

    def axle(self, holder, foot, height, width=1.2):
        """A root column in a hub, floor to roof."""
        self.pillar(holder, foot, height, max(width, 2.2))

    def machine(self, room, id, at=(0, 0, 0)):
        m = room.mech(id, "machine", at)
        m.pose()
        return m

    def gear(self, holder, centre, diameter, axis="y", yaw=0.0, thickness=None, spin=0.0, turn=None):
        """Where the other themes put a gear: a crown of roots spreading from a column (big), or a root knot (small). Never turns."""
        c = np.array(centre, float)
        if diameter >= 4.0:
            for i in range(3):
                y = 120.0 * i + spin
                box(holder, ARC, c + yaw_turn((diameter / 4, 0.3, 0), y), (diameter / 2 + 1.0, 2.2, 1.4), yaw=y)
            return None
        lo, hi = bounds(GNARL)
        s = diameter / max(hi[0] - lo[0], hi[2] - lo[2])
        return holder.add(GNARL, c - (0, hi[1] * s * 0.35, 0), (0, spin, 0), (s, s * 0.35, s))     # (its roots hang down from c)

    # ---------------------------------------------------------------- dressing

    def decor(self):
        S = lambda name, s=1.0, wall=None, sets=(): {"prefab": name, "scale": s, "size": (1.0, 1.0), "wall": wall, "sets": sets}
        return [
            S(MUSH, 0.45, True, ("fungus",)),
            S("root08", 1.0, True, ("fungus", "seedbed")),
            S("Bush01", 0.6, True, ("fungus", "deadfall")),
            S("root08", 1.2, None, ("deadfall", "bones", "rot")),
            S("root12", 1.3, None, ("deadfall", "fungus")),
            S("mudpile_old", 0.8, True, ("rot", "deadfall")),
            S(GNARL, 0.35, True, ("rot",)),
            S("skull_pile", 1.2, True, ("bones",)),
            S("bone_stack", 1.3, True, ("bones", "rot")),
            S("mudpile_old", 0.5, True, ("seedbed",)),
            S(CANDLE, 1.6, True, ("bones",)),
        ]

    def dressing_sets(self):
        return {"fungus": 1.0, "rot": 0.9, "deadfall": 0.9, "bones": 0.6, "seedbed": 0.6}

    def remains(self):
        """Big things on a big room's floor: (prefab, size (x, y, z) of its box, sunk metres)."""
        return [("SwampTree2_log", (5.0, 3.0, 12.0), 0.6), ("BigBranch", (9.0, 3.0, 4.0), 0.4), (ARC, (12.0, 3.4, 4.0), 0.6),
                (TAR, (6.0, 0.5, 5.0), 0.3), (GNARL, (2.6, 3.4, 2.8), 0.2)]

    def nook_pieces(self):
        def stand(name, s, sy=None):
            return lambda room, at, yaw: room.stand(name, at, yaw=yaw, scale=(s, sy or s, s))
        return [
            {"name": "seedpod", "main": True, "size": (2.6, 2.8), "build": stand(POD, 1.0)},
            {"name": "mushrooms", "main": True, "size": (2.4, 2.2), "build": stand(MUSH, 0.9)},
            {"name": "gnarl", "main": True, "size": (2.4, 2.4), "build": stand(GNARL, 0.8, 0.55)},
            {"name": "bones", "main": True, "size": (2.4, 2.4), "build": stand("skull_pile", 1.8)},
            {"name": "roots", "size": (2.6, 2.6), "build": stand("root08", 1.0)},
            {"name": "stack", "size": (1.4, 1.4), "build": stand("bone_stack", 1.2)},
            {"name": "mud", "size": (2.6, 2.6), "build": stand("mudpile_old", 0.6)},
            {"name": "rootlet", "size": (1.4, 1.4), "build": stand("root12", 0.9)},
        ]

    # ---------------------------------------------------------------- signature rooms (hand made: rootdeep_rooms.py, bosses_roothollow.py)

    def signatures(self):
        import rootdeep_rooms as rr
        import bosses_roothollow as br
        return rr.signatures(self) | br.signatures(self)


THEME = RootHollow()
