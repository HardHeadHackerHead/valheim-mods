"""
The Cathedral of the Bone Choir's hand-made rooms (theme "ossuary", authoring/themes/ossuary.py; pack spec specs/bonechoir.toml):
the entrance (the narthex), the end caps, the vault, the secret chapel, the shortcut's doors, and the signature rooms:

  nave       the Nave of Ribs: a long nave under a ribcage vault. Ring the bell at the organ and the procession of the dead begins:
             the doors shut, four waves come down the aisles, and bone spikes fall from the vault in three lanes (a creak and a
             shudder first). When the last falls, the doors open.
  tower_a/b  the Bell Tower: four bells high in a shaft, ringing a hymn by themselves (each swings, tolls and glows in turn). Pull
             their ropes in the same order and the bells hold the counterweight for one turn: the lift rises to the way on. The
             first time, the bell relic falls.
  charnel    the Charnel: an ossuary where a cart of bones must be pushed round the bone piles onto the open grave; then the bone
             relic rises by the altar. A lever by the door puts the cart back.
  reliquary  the Reliquary: three pedestals, each carved with a relic's likeness (a bell, a skull, a bone). With all three relics
             on their own pedestals the reliquary's doors open (the way to the Colossus). A checkpoint.

The two boss rooms (the Choir Loft, the Apse of the Colossus) are in bosses_bonechoir.py. Every room stands on the theme's own shell
(authoring/shells.py: walls, floor, the vault of ribs, Gothic doorways), so the hand-made rooms and the recipe rooms are one architecture.
"""
import numpy as np

import giant_kit as gk
from dungeon_parts import box, bounds, Room, r3, yaw_turn

from authoring.core import rng_for
from authoring.shells import Shell

T = None                     # the theme (set by signatures())

STONE = "Ashlands_Ruins_Floor_6x6"
BLOCK = "Ashlands_Floor"
BONE = "LargeBone"
SKULL = "AsksvinCarrionSkull"
CANDLE = "Candle_resin"
TORCH = "piece_groundtorch_wood"
FIREBOWL = "piece_brazierceiling01"
ROSE = "Ashlands_Ruins_Wall_Top_wHole"
SPIKE = "Ashlands_Pillar4_tip"
BELL = "Bell"
DARK = "blackmarble_floor_large"
CHAIN = "Morkhalla_WallChain1"
TOLL = "sfx_fader_bell"

RELICS = {"relic_bell": {"look": "Bell", "name": "$dungeons_bonechoir_relic_bell", "description": "$dungeons_bonechoir_relic_bell_desc"},
          "relic_skull": {"look": "TrophySkeleton", "name": "$dungeons_bonechoir_relic_skull",
                          "description": "$dungeons_bonechoir_relic_skull_desc"},
          "relic_bone": {"look": "WitheredBone", "name": "$dungeons_bonechoir_relic_bone", "description": "$dungeons_bonechoir_relic_bone_desc"}}


# ---------------------------------------------------------------- helpers

def shell(name, size, doors, shape="rect", levels=None, **kw):
    """A room of the theme with doorways [(id, side, lateral, level)]; `levels`: more floor heights over the floor (metres up), for
    doorways on upper storeys."""
    sh = Shell(T, name, shape, size, rng_for(name), **kw)
    if levels:
        from authoring.core import Plan, FREE
        sh.levels = [sh.floor_y] + [sh.floor_y + h for h in levels]
        sh.plan = Plan(sh.size, sh.levels)                 # (the upper levels are the room's own floors, not the shell's)
        sh.plan.set_mask(0, lambda x, z: sh.on_level(x, z, 0), FREE)
    for id, k, lat, lv in doors:
        sh.add_door(id, k, lat, lv)
    sh.build()
    return sh


def lit(part, states):
    """A mechanism's part whose fire and light burn only in these states (dungeon_parts' Part writes no "lit")."""
    plain = part.json
    part.json = lambda: dict(plain(), lit=list(states))
    return part


def shown(part, states):
    """A mechanism's part shown only in these states (dungeon_parts' Part writes no "show")."""
    plain = part.json
    part.json = lambda: dict(plain(), show=list(states))
    return part


def trig(r, **t):
    if "if_" in t:
        t["if"] = t.pop("if_")
    r.extra.setdefault("triggers", []).append(t)
    return t


def candles(r, at, yaw=0.0, n=3, big=4.5):
    """A cluster of giant candles (walk-through)."""
    for i in range(n):
        d = yaw_turn((0, 0, 0.45 if n > 1 else 0.0), 360.0 * i / n + yaw)
        s = big * (1.0 - 0.18 * i)
        r.stand(CANDLE, (at[0] + d[0], at[1], at[2] + d[2]), scale=(s, s, s))


def bell_scale(size):
    lo, hi = bounds(BELL)
    return size / (hi[1] - lo[1])


def hang_bell(r, at, size=2.6, chain_to=None, yaw=0.0):
    """A great bell (the game's bell, scaled) with its mouth at `at`; on a chain from `chain_to` (a height above it)."""
    s = bell_scale(size)
    p = r.stand(BELL, at, yaw=yaw, scale=(s, s, s))
    if chain_to is not None:
        gk.hang_chain(r, (at[0], chain_to, at[2]), chain_to - (at[1] + size))
    return p


def swing_bell(r, id, top, size, chain_to, glow=True):
    """A bell that swings when it tolls: a platform pivoting at the bell's crown (`top`), state 0 at rest and 1 swung, with a fire
    bowl inside it that burns only while it swings (the bell glows as it rings); on a chain from `chain_to`."""
    gk.hang_chain(r, (top[0], chain_to, top[2]), chain_to - top[1])
    m = r.mech(id, "platform", top)
    s = bell_scale(size)
    m.stand(BELL, (0, -size, 0), scale=(s, s, s))
    if glow:
        lit(m.stand(TORCH, (0, -size + 0.3, 0), scale=(0.9, 0.9, 0.9)), [1])
    m.pose().pose(euler=(0, 0, 24))
    m.seconds = 0.45
    return m


def lever_at(r, id, at, yaw, wires=(), reached_from=None):
    return T.lever(r, id, at, yaw, list(wires), reached_from=reached_from)


def encounter(r, id, lo, hi, start_on, waves, points, lock=(), opens=(), reached_from=None, music="CombatEventL3", boss=None,
              level=1, elite_level=3, pause=4.0, timeout=40.0, wires=()):
    lo, hi = np.array(lo, float), np.array(hi, float)
    m = r.mech(id, "encounter", (lo + hi) / 2)
    m.pose()
    for t in opens:
        m.wire(t, "set:1")
    for t, do in wires:
        m.wire(t, do)
    m.reached_from = reached_from
    m.extra.update({"area": r3(hi - lo), "startOn": start_on, "lock": list(lock), "level": level, "eliteLevel": elite_level,
                    "pause": pause, "timeout": timeout, "points": [r3(p) for p in points]})
    if waves:
        m.extra["waves"] = waves
    if music:
        m.extra["music"] = music
    if boss:
        m.extra["boss"] = boss
    return m


def gate(sh, id, door, start):
    """The theme's gate (a wrought iron screen framed in bone) in a doorway of shell sh."""
    d = sh.doors[door]
    depth = sh.half[2] if d.k % 2 == 0 else sh.half[0]
    return T.gate(sh.room, id, d.k, depth, d.y, start, door, lateral=d.lateral, open_dir=1 if d.lateral <= 0 else -1)


def hymn(r, order, where, display_every=16.0, gap=1.7, cond=None, prefix="h", start_on=None):
    """The hymn the bells ring by themselves: every `display_every` s (while the timer `<prefix>hymn` runs) the bells in `order` swing,
    toll and glow one after another, `gap` s apart. Display only (the solver leaves it out). `cond`: conditions each step needs (a
    verse); started by `start_on` (an event) or by the caller's own trigger."""
    ifs = {"if": cond} if cond else {}
    if start_on is not None:
        trig(r, id=f"{prefix}start", on=start_on, repeat=True, solver="ignore",
             do=[{"timer": f"{prefix}hymn", "every": display_every}, {"timer": f"{prefix}0", "after": 2.0}])
    trig(r, id=f"{prefix}go", on={"event": "timer", "name": f"{prefix}hymn"}, repeat=True, solver="ignore",
         do=[{"timer": f"{prefix}0", "after": 0.5}], **ifs)
    for i, b in enumerate(order):
        nxt = [{"timer": f"{prefix}{i + 1}", "after": gap}] if i + 1 < len(order) else []
        trig(r, id=f"{prefix}{i}", on={"event": "timer", "name": f"{prefix}{i}"}, repeat=True, solver="ignore",
             do=[{"set": b, "to": 1}, {"sound": TOLL, "at": r3(where[b])}, {"timer": f"{prefix}{i}x", "after": 1.1}] + nxt, **ifs)
        trig(r, id=f"{prefix}{i}x", on={"event": "timer", "name": f"{prefix}{i}x"}, repeat=True, solver="ignore", do=[{"set": b, "to": 0}])


def rope_rings(r, ropes, where):
    """Each bell rope (a lever with no wires: pulled as often as you like) swings and tolls its bell."""
    for rope, b in ropes:
        trig(r, id=f"ring_{rope}", on={"event": "lever", "mech": rope}, repeat=True, solver="ignore",
             do=[{"set": b, "to": 1}, {"sound": TOLL, "at": r3(where[b])}, {"timer": f"back_{rope}", "after": 1.1}])
        trig(r, id=f"back_{rope}", on={"event": "timer", "name": f"back_{rope}"}, repeat=True, solver="ignore", do=[{"set": b, "to": 0}])


def spikes(r, id, at, width, rest, every, offset, warn=1.3, n=None):
    """Falling bone spikes (a trap): a row of carved stone spikes, points down, hung under the ribs `rest` m over the floor; on its
    beat it creaks and shudders up (warn), then drops to the floor across `width` m and draws back up. Off until something sets it on.
    A dark inlay on the floor marks where they land."""
    m = r.mech(id, "trap", at)
    n = n or max(2, int(round(width / 2.6)))
    lo, hi = bounds(SPIKE)
    s = (1.0 / (hi[0] - lo[0]), 4.2 / (hi[1] - lo[1]), 1.0 / (hi[2] - lo[2]))
    for i in range(n):
        x = -width / 2 + (i + 0.5) * width / n
        m.add(SPIKE, (x, 4.2, 0.35 * ((i % 2) * 2 - 1)), (180, 37 * i, 0), s)
    m.pose(at=(0, rest, 0)).pose(at=(0, 0, 0))
    m.start = 0
    m.seconds = 0.35
    m.extra.update({"damage": "pierce", "scale": 1.0, "every": every, "for": 1.0, "offset": offset,
                    "area": [round(width, 2), 2.4, 2.6], "areaAt": [0, 1.2, 0],
                    "fx": ["sfx_rock_destroyed", "vfx_RockDestroyed"], "warn": warn,
                    "warnFx": ["sfx_frozenking_spikerain_chain"], "warnLift": 0.5})
    box(r, DARK, (at[0], at[1] + 0.02, at[2]), (width, 0.04, 2.4))
    return m


def signatures(theme):
    global T
    T = theme
    return {"entrance": entrance, "cap_plain": cap_plain, "vault": vault, "nave": nave, "tower_a": tower_a, "tower_b": tower_b,
            "charnel": charnel, "reliquary": reliquary, "secret_chapel": secret_chapel,
            "ossuary_walk": ossuary_walk, "choir_crossing": choir_crossing, "bell_chapel": bell_chapel,
            "shortcut_near": lambda: shortcut_door("shortcut_near", False), "shortcut_far": lambda: shortcut_door("shortcut_far", True),
            "choir": _boss("choir_loft"), "apse": _boss("apse")}


def _boss(name):
    def make():
        import bosses_bonechoir as bb
        bb.T = T
        return getattr(bb, name)()
    return make


# ---------------------------------------------------------------- the entrance, caps, the vault, secrets, shortcuts

def entrance():
    """The narthex: a tall porch inside the bone archway. A great cracked bell hangs over the middle (its broken lip on the floor);
    banks of candles and two singers of the choir either side; the first rose window over the way on."""
    sh = shell("entrance", (16, 14, 20), [("in", 2, 0.0, 0), ("n", 0, 0.0, 0), ("e", 1, 2.0, 0)], tags=["entrance"])
    r = sh.room
    r.entrance = True
    r.door("in")["entrance"] = True
    F = sh.floor_y
    top = sh.top - T.floor_t
    for x in (-5.0, 5.0):
        T.light_stand(r, (x, F, -6.5))
        T.accent(r, (x * 1.15, F, 3.5), yaw=90 if x < 0 else 270)
        candles(r, (x * 1.2, F, -1.5))
    hang_bell(r, (0, F + 5.6, 0.5), 3.0, chain_to=top, yaw=20)
    r.stand(BELL, (-3.0, F, 1.8), yaw=70, scale=(1.2, 1.2, 1.2))
    r.add(ROSE, (0, F + 8.0, sh.half[2] - T.wall_t - 0.55), (0, 180, 0), (1.0, 1.0, 0.5))
    return r


def cap_plain():
    """End cap: the doorway walled up with pale stone, a skull set in it."""
    r = Room("cap_plain", (6, 14, 0), end_cap=True, weight=0.3)
    F = -7.0 + T.floor_t
    r.doorway("in", (0, F, 0), (0, 0, 1), type=T.door_type)
    box(r, BLOCK, (0, F + (T.door_h + 0.2) / 2, 0.35), (T.door_w + 0.4, T.door_h + 0.2, 0.6))
    box(r, SKULL, (0, F + 4.0, 0.0), (0.9, 0.55, 0.7), yaw=180)
    return r


def vault():
    """The treasury of the choir: the great chest on a stepped altar under a rose window, candles, skulls; in the back corner the
    way out (the vault's exit: back to the entrance)."""
    sh = shell("vault", (16, 14, 20), [("in", 2, 0.0, 0)], tags=["end"], max=1)
    r = sh.room
    r.doorways[0]["door"] = False
    F = sh.floor_y
    hz = sh.half[2] - T.wall_t
    box(r, BLOCK, (0, F + 0.3, 3.5), (8.0, 0.6, 5.0))
    box(r, BLOCK, (0, F + 0.8, 3.9), (6.0, 0.4, 3.6))
    r.stand("Morkhalla_ChestAncient", (0, F + 1.0, 4.0), yaw=180, saved=True)
    for x in (-5.5, 5.5):
        T.light_stand(r, (x, F, -5.5))
        candles(r, (x * 0.62, F + 0.6, 2.4))
    r.stand("skull_pile", (5.8, F, 7.6), yaw=180, scale=(1.4, 1.4, 1.4))
    r.add(ROSE, (0, F + 7.5, hz - 0.5), (0, 180, 0), (1.0, 1.0, 0.5))
    out = r.mech("exit", "exit", (-4.5, F, hz - 0.4), (0, 180, 0))
    box(out, DARK, (0, 1.8, 0.1), (2.4, 3.6, 0.2))
    box(out, BLOCK, (0, 3.8, 0.0), (3.0, 0.4, 0.6))
    for x in (-1.4, 1.4):
        box(out, BONE, (x, 1.9, 0.0), (0.45, 3.8, 0.45))
    return r


def secret_chapel():
    """A secret: a forgotten side chapel walled up with cracked pale stone (break it: pickaxe or blunt), a reliquary chest on a stone
    bier between two kneeling singers."""
    sh = shell("secret_chapel", (12, 14, 12), [("in", 2, 0.0, 0)], tags=["secret"], weight=1.0, max=1)
    r = sh.room
    r.end_cap = True
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    b = r.mech("crack", "breakable", (0, F, -sh.half[2] + T.wall_t * 0.5), (0, 180, 0))
    b.pose().pose()
    b.doorway = "in"
    b.extra["health"] = 300
    b.fill(DARK, (0, T.door_h / 2, -0.35), (T.door_w + 0.2, T.door_h + 0.1, 0.2))
    w, h = (T.door_w + 0.2) / 2, (T.door_h + 0.1) / 3
    tilts = ((6, -7, 4), (-5, 6, -6), (8, 4, -3), (-6, -5, 7), (5, 9, 5), (-8, -6, -4))
    for i in range(2):
        for j in range(3):
            if (i, j) == (0, 2):
                continue
            tx, ty, tz = tilts[i * 3 + j]
            box(b, BLOCK, (-w / 2 + i * w + 0.05 * tx, h / 2 + j * h + 0.04 * ty, -0.05 * abs(tz) - 0.1), (w - 0.16, h - 0.14, 0.5),
                yaw=tz, tilt=(tx, ty))
    box(b, SKULL, (w / 2, 2.2 * h, 0.05), (0.8, 0.5, 0.6), yaw=180)
    r.extra["hiddenBy"] = "crack"
    box(r, BLOCK, (0, F + 0.45, 2.5), (3.2, 0.9, 1.6))
    r.stand("TreasureChest_ashland_stone", (0, F + 0.9, 2.5), yaw=180, saved=True)
    for x in (-3.0, 3.0):
        r.stand("FrozenSkeleton_Pose2", (x, F, 1.0), yaw=90 if x < 0 else 270)
        candles(r, (x * 1.3, F, 3.5), n=3, big=4.0)
    return r


def shortcut_door(name, deep):
    """One end of the shortcut (pair "sc1"): a pale stone door with a skull on it; barred with a bone on the near side. The deep one
    opens both from its side."""
    r = Room(name, (6, 14, 0), end_cap=True, weight=0.01, max=1)
    F = -7.0 + T.floor_t
    r.doorway("in", (0, F, 0), (0, 0, 1), type=T.door_type)
    d = r.mech("shortcut", "shortcut", (0, F, 0), (0, 180, 0))
    d.pose().pose()
    d.fill(BLOCK, (0, T.door_h / 2, -0.4), (T.door_w, T.door_h, 0.6))
    box(d, SKULL, (0, T.door_h * 0.6, 0.0), (0.9, 0.55, 0.7))
    if not deep:
        box(d, BONE, (0, 3.0, 0.15), (0.5, T.door_w + 0.4, 0.5), tilt=(0, 90))
    d.extra.update({"pair": "sc1", "deep": deep})
    return r


# ---------------------------------------------------------------- hand-made rooms between the steps

def ossuary_walk():
    """A narrow walk between walls of the dead: skull piles in niches down both sides, candles at their feet, a fire bowl overhead."""
    sh = shell("ossuary_walk", (8, 14, 20), [("a", 2, 0.0, 0), ("b", 0, 0.0, 0)], weight=1.0, max=1)
    r = sh.room
    F = sh.floor_y
    hx = sh.half[0] - T.wall_t
    for z in (-5.5, -1.5, 2.5, 6.5):
        for s in (-1, 1):
            box(r, BLOCK, (s * (hx - 0.35), F + 1.2, z), (0.7, 0.3, 2.2))
            r.stand("skull_pile", (s * (hx - 0.4), F + 1.35, z), yaw=90 if s < 0 else 270, scale=(1.0, 0.9, 1.0))
            r.stand("bone_stack", (s * (hx - 0.4), F, z), yaw=90 if s < 0 else 270, scale=(1.1, 1.1, 1.1))
    for z in (-3.5, 4.5):
        candles(r, (-hx + 0.6, F, z), n=2, big=3.0)
        candles(r, (hx - 0.6, F, z + 1.0), n=2, big=3.0)
    T.light_hang(r, (0, F + 6.0, 0.5))
    return r


def choir_crossing():
    """A crossing of four ways round a ring of the choir: white singers on stone thrones facing a shrine of candles in the middle.
    Step in and the singers' keepers wake (one wave)."""
    sh = shell("choir_crossing", (20, 14, 20), [("n", 0, 0.0, 0), ("e", 1, 0.0, 0), ("s", 2, 0.0, 0), ("w", 3, 0.0, 0)],
               weight=1.0, max=1)
    r = sh.room
    F = sh.floor_y
    box(r, BLOCK, (0, F + 0.3, 0), (2.4, 0.6, 2.4))
    candles(r, (0, F + 0.6, 0), n=3, big=5.0)
    for a in (45, 135, 225, 315):
        p = yaw_turn((0, 0, 6.8), a)
        r.stand("piece_throne02", (p[0], F, p[2]), yaw=(a + 180) % 360)
        q = yaw_turn((0, 0, 5.3), a)
        T.accent(r, (q[0], F, q[2]), yaw=(a + 180) % 360)
    for a in (0, 90, 180, 270):
        p = yaw_turn((3.2, 0, 7.6), a)
        T.light_stand(r, (p[0], F, p[2]))
    T.light_hang(r, (0, F + 7.0, 0))
    pts = [(-3.3, F, 0.4), (3.3, F, -0.4), (0.4, F, 3.3)]
    encounter(r, "keepers", (-8.5, F, -8.5), (8.5, F + 8, 8.5), "enter", [{"melee": 2, "ranged": 1}], pts, music="CombatEventL2",
              reached_from="s")
    return r


def bell_chapel():
    """A chapel where a great bell fell through the vault and lies cracked on the floor among its broken ribs; a reliquary chest by its
    lip. Lift the lid and the dead come for it (two waves)."""
    sh = shell("bell_chapel", (16, 14, 20), [("a", 2, 0.0, 0), ("b", 1, -3.0, 0)], weight=1.0, max=1)
    r = sh.room
    F = sh.floor_y
    hz = sh.half[2] - T.wall_t
    r.add(BELL, (-2.0, F + 1.5, 1.2), (90, 20, 0), (3.0, 3.0, 3.0))
    box(r, "lox_ribs", (3.5, F + 1.1, 5.5), (4.0, 2.2, 3.0), yaw=200)
    chest = (2.5, F, 1.0)
    r.stand("TreasureChest_ashland_stone", chest, yaw=200, saved=True)
    for x in (-5.5, 5.5):
        T.light_stand(r, (x, F, -6.5))
    candles(r, (5.2, F, -1.0), n=3, big=4.0)
    T.light_hang(r, (0, F + 7.0, -2.0))
    pts = [(-4.0, F, -2.0), (-4.5, F, -5.5), (-5.0, F, 7.0), (0.5, F, -4.0)]
    encounter(r, "wake", (-6.5, F, -8.5), (6.5, F + 8, 8.5), "open", [{"melee": 2, "ranged": 1}, {"melee": 2, "flying": 1}], pts,
              music="CombatEventL2").extra["chest"] = r3((chest[0], F + 0.3, chest[2]))
    return r


# ---------------------------------------------------------------- the Nave of Ribs

NAVE = (20, 20, 44)


def nave():
    """The Nave of Ribs (signature): 44 m long and 20 high under a ribcage vault, bundled-bone pillars down both aisles, stone thrones
    and white singers along the walls, the bone organ and a rose window over the far door. Ring the bell by the organ (a lever) and the
    procession of the dead begins: the doors shut, four waves come down the aisles, and bone spikes drop from the vault in three lanes
    across the nave (each creaks and shudders up first; they hurt the dead too). When the last of them falls the doors open."""
    sh = shell("nave", NAVE, [("in", 2, 0.0, 0), ("out", 0, 0.0, 0), ("side", 1, -14.0, 0)], tags=["nave", "fight"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    hx, hz = sh.half[0] - T.wall_t, sh.half[2] - T.wall_t
    top = sh.top - T.floor_t
    spring = max(F + 7.0, top - 6.0)
    gate(sh, "ingate", "in", 1)
    gate(sh, "outgate", "out", 0)
    gate(sh, "sidegate", "side", 0)
    for z in (-14.0, -6.0, 2.0, 10.0):
        for x in (-5.0, 5.0):
            T.pillar(r, (x, F, z), spring - F, 1.8)
    traps = []
    for i, z in enumerate((-10.0, -2.0, 6.0)):
        traps.append(spikes(r, f"spikes{i + 1}", (0, F, z), 2 * hx - 0.4, spring - F - 4.6, 5.0, round(i * 5.0 / 3, 2)))
    # the organ: long bones for pipes either side of the far door, rising to the rose window
    for s in (-1, 1):
        for i in range(6):
            hgt = 13.0 - i * 1.4
            box(r, BONE, (s * (3.4 + i * 0.95), F + hgt / 2, hz - 0.6), (0.85, hgt, 0.85))
    r.add(ROSE, (0, F + 11.0, hz - 0.5), (0, 180, 0), (1.3, 1.3, 0.5))
    r.stand("piece_bone_throne", (-7.2, F, hz - 3.2), yaw=180)
    # the choir along the walls: stone thrones with white singers before them, banks of candles between
    for z in (-16.8, -12.6, -4.2, 3.6, 8.4, 16.8):
        for s in (-1, 1):
            if s > 0 and z > 10.0:
                continue                                              # (the side door)
            yaw = 90 if s < 0 else 270
            r.stand("piece_throne02", (s * (hx - 0.6), F, z), yaw=yaw)
            T.accent(r, (s * (hx - 1.3), F, z + 2.1), yaw=yaw)
    for z in (-19.0, 0.0, 12.6):
        for s in (-1, 1):
            if s > 0 and z > 10.0:
                continue
            T.light_stand(r, (s * (hx - 1.2), F, z))
    for z in (-14.0, -6.0, 2.0, 10.0):
        T.light_hang(r, (0, spring - 3.5, z))
    # the bell that starts it: a lever by the organ (pulled once), the little bell over it
    lever_at(r, "bell", (-3.25, F, hz - 6.25), 0, [("procession", "start")], reached_from="in")
    # the spikes fall while the procession is on: from the bell to the last of the dead (or till nobody is left in the nave)
    trig(r, id="spikes_on", on={"event": "lever", "mech": "bell"}, repeat=True, solver="ignore", do=[{"set": t.id, "to": 1} for t in traps])
    trig(r, id="spikes_off", on={"event": "leave"}, repeat=True, solver="ignore", do=[{"set": t.id, "to": 0} for t in traps])
    hang_bell(r, (-3.25, F + 4.6, hz - 5.0), 1.6, chain_to=top)
    pts = [(-5.2, F, -12.0), (5.2, F, -12.0), (-5.2, F, -4.0), (5.2, F, 4.0), (-5.2, F, 8.0), (5.2, F, -4.0), (2.5, F, 13.5)]
    encounter(r, "procession", (-hx, F, -hz), (hx, top - 1.0, hz), "wire",
              [{"melee": 2, "ranged": 1}, {"melee": 3, "flying": 1}, {"melee": 2, "ranged": 2}, {"melee": 2, "ranged": 1, "elite": 1}],
              pts, lock=["ingate", "outgate", "sidegate"], opens=["ingate", "outgate", "sidegate"], reached_from="in",
              music="CombatEventL4", wires=[(t.id, "set:0") for t in traps])
    trig(r, id="hint", on={"event": "enter", "at": [0, F + 2, -hz + 6], "size": [2 * hx, 4, 6]}, solver="ignore",
         do=[{"message": "$dungeons_bonechoir_nave_hint"}])
    return r


# ---------------------------------------------------------------- the Bell Tower

TOWER = (16, 30, 16)


def _tower(name, order, mirror):
    """The Bell Tower (signature): a shaft 30 m high, four bells near its top, a stone lift in the middle chained down, the way on a
    storey up. The bells ring a hymn by themselves (each swings, tolls and its fire glows in turn); pull their ropes in the same order
    (within 30 s) and they hold the counterweight for one turn: the lift rises to the upper landing (9 s after the hymn), waits 9 s,
    and comes down again. The first time, the bell relic falls by the door. The two towers differ in their hymn and their hand."""
    S = T.storey
    m = -1 if mirror else 1                                  # the hand: the west landing and east ropes, or mirrored
    sh = shell(name, TOWER, [("in", 2, 0.0, 0), ("out", 0, 0.0, 1), ("w", 3 if m > 0 else 1, -2.0 * m, 1)], levels=[S],
               tags=["tower"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    U = F + S
    h = sh.half[0] - T.wall_t                               # 7: the inner walls
    top = sh.top - T.floor_t
    # the landings a storey up: along the north wall, and along the west wall (east, mirrored)
    T.floor(r, -h, h, 3.0, h, U, thickness=1.0)
    x0, x1 = sorted((m * -h, m * -3.0))
    T.floor(r, x0, x1, -h, 3.0, U, thickness=1.0)
    # parapets on their open edges, but where the lift docks at the inner corner
    T.rail(r, (m * 3.0, 0, 3.3), (m * h, 0, 3.3), U)
    T.rail(r, (m * -3.3, 0, -h), (m * -3.3, 0, -3.0), U)
    # the lift: a stone deck 6 x 6 in the middle, parapets on its two outer sides; chained down (state 0) until the hymn is rung,
    # then one round: down (1), up (2), down (3), and chained again (0)
    lift = r.mech("lift", "platform", (0, F, 0))
    box(lift, BLOCK, (0, -0.15, 0), (6.0, 0.6, 6.0))
    T.rail(lift, (m * -2.7, 0, -2.75), (m * 3.0, 0, -2.75), 0.15, 1.3)
    T.rail(lift, (m * 2.6, 0, -2.4), (m * 2.6, 0, 3.0), 0.15, 1.3)
    for cx, cz in ((m * 2.6, -2.75), (m * -2.75, -2.75), (m * 2.6, 2.75)):
        box(lift, BONE, (cx, 0.9, cz), (0.4, 1.8, 0.4))
    lift.pose().pose().pose(at=(0, S, 0)).pose()
    lift.seconds = 4.0
    lift.cycle = 9.0
    lift.extra["cycleIn"] = [1, 2, 3]
    lift.join("in", "out", state=2)
    r.join("out", "w")
    r.extra["drops"] = [{"a": "out", "b": "in"}, {"a": "w", "b": "in"}]       # (off the landings, 7 m down the shaft)
    # its counterweight: stone and bones on chains over the landing's far corner, going down as the lift goes up (always over heads)
    weight = r.mech("weight", "platform", (m * -5.2, U + 11.0, 5.2))
    box(weight, BLOCK, (0, 0.8, 0), (2.0, 1.6, 2.0))
    weight.stand("skull_pile", (0, 1.6, 0), scale=(1.3, 1.3, 1.3))
    for dx in (-0.7, 0.7):
        box(weight, CHAIN, (dx, 4.4, 0), (0.35, 5.6, 0.35))
    weight.pose().pose().pose(at=(0, -S, 0)).pose()
    weight.seconds = 4.0
    weight.cycle = 9.0
    weight.extra["cycleIn"] = [1, 2, 3]
    # the bells, high up over their ropes (levers with no wires: ring them as often as you like); ropes along the open south-east
    ropes = {"bellA": (m * 5.0, -6.2, 180), "bellB": (m * 6.5, -2.6, 90 * m % 360), "bellC": (m * 6.5, -0.2, 90 * m % 360),
             "bellD": (m * 6.5, 2.4, 90 * m % 360)}
    where = {}
    for b, (x, z, yaw) in ropes.items():
        bx, bz = (x, z + 0.8) if yaw == 180 else (x - 0.9 * m, z)
        crown = max(U + 7.5, top - 6.0) - 0.5                 # (under the ribs' springing)
        swing_bell(r, b, (bx, crown, bz), 2.4, top)
        where[b] = np.array([bx, crown - 1.5, bz])
        lever_at(r, "rope" + b[-1], (x, F, z), yaw, reached_from="in")
        cy0, cy1 = F + 2.7, crown - 2.6
        box(r, CHAIN, (bx, (cy0 + cy1) / 2, bz), (0.16, cy1 - cy0, 0.16))
    rope_rings(r, [("rope" + b[-1], b) for b in ropes], where)
    hymn(r, order, where, display_every=16.0, start_on={"event": "enter"})
    trig(r, id="counterweight", on={"event": "state", "mech": "lift", "state": 1}, repeat=True, solver="ignore",
         do=[{"set": "weight", "to": 1}])
    trig(r, id="quiet", on={"event": "leave"}, repeat=True, solver="ignore", do=[{"timer": "hhymn", "stop": True}])
    trig(r, id="hymnrung", on=[{"event": "lever", "mech": "rope" + b[-1]} for b in order], when="sequence", within=30, repeat=True,
         do=[{"set": "lift", "to": 1}, {"sound": "sfx_secretfound"},
             {"message": "$dungeons_bonechoir_tower_open"}])
    relic_at = (m * -5.8, F + 0.95, -1.0)
    box(r, BLOCK, (relic_at[0], F + 0.45, relic_at[2]), (1.2, 0.9, 1.2))
    trig(r, id="relic", on=[{"event": "lever", "mech": "rope" + b[-1]} for b in order], when="sequence", within=30,
         do=[{"item": "relic_bell", "at": r3(relic_at)}, {"sound": TOLL, "at": r3(relic_at)}, {"message": "$dungeons_bonechoir_relic_bell_found"}])
    trig(r, id="hint", on={"event": "enter", "at": [0, F + 2, -h + 2.5], "size": [8, 4, 4]}, solver="ignore",
         do=[{"message": "$dungeons_bonechoir_tower_hint"}])
    for x, z, y in ((m * -5.6, -5.8, F), (m * 1.0, 5.8, F), (m * 5.0, 5.6, U), (m * -5.6, -5.6, U)):
        T.light_stand(r, (x, y, z))
    return r


def tower_a():
    return _tower("tower_a", ["bellB", "bellD", "bellA", "bellC"], False)


def tower_b():
    return _tower("tower_b", ["bellC", "bellA", "bellD", "bellB"], True)


# ---------------------------------------------------------------- the Charnel

CHARNEL_ROWS = ["       ",
                " S.#.. ",
                " ..... ",
                " #.#.# ",
                "  ...  ",
                "       "]
CHARNEL_GOAL = (4, 3)
CELL = 2.4


def charnel():
    """The Charnel (signature, a side wing): an ossuary of stacked skulls. A stone cart heaped with bones stands in the corner; push it
    (walk into it) round the bone piles onto the open grave at the far end, and the bone relic rises by the altar. A lever by the door
    puts the cart back where it started."""
    sh = shell("charnel", (20, 14, 22), [("in", 2, 0.0, 0)], tags=["charnel"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    hx, hz = sh.half[0] - T.wall_t, sh.half[2] - T.wall_t
    rows = CHARNEL_ROWS
    R, C = len(rows), len(rows[0])
    zc = 1.8

    def at(row, col):
        return ((col - (C - 1) / 2) * CELL, F, zc + ((R - 1) / 2 - row) * CELL)

    cart = r.mech("cart", "pushblock", (0, F, zc))
    box(cart, BLOCK, (0, 0.4, 0), (2.1, 0.8, 2.1))
    cart.stand("skull_pile", (0.3, 0.8, 0.2), scale=(1.3, 1.2, 1.3))
    cart.stand("bone_stack", (-0.5, 0.8, -0.4), yaw=40, scale=(1.2, 1.2, 1.2))
    cart.extra.update({"cell": CELL, "rows": rows, "group": "cart", "fx": ["sfx_build_hammer_stone"]})
    cart.seconds = 0.5
    for i, row in enumerate(rows):
        for j, ch in enumerate(row):
            if ch == "#":
                x, y, z = at(i, j)
                box(r, BLOCK, (x, F + 0.6, z), (2.2, 1.2, 2.2))
                r.stand("skull_pile", (x, F + 1.2, z), yaw=30 * j, scale=(1.6, 1.4, 1.6))
    gx, _, gz = at(*CHARNEL_GOAL)
    box(r, DARK, (gx, F + 0.02, gz), (2.3, 0.04, 2.3))
    goal = sum(1 for i, row in enumerate(rows) for j, ch in enumerate(row) if ch in ".S" and (i, j) < CHARNEL_GOAL)
    lever_at(r, "reset", (-6.75, F, -7.75), 180, reached_from="in")
    altar = (6.5, F, -7.6)
    box(r, BLOCK, (altar[0], F + 0.5, altar[2]), (1.6, 1.0, 1.4))
    trig(r, id="settle", on={"event": "state", "mech": "cart", "state": goal},
         do=[{"item": "relic_bone", "at": [altar[0], F + 1.1, altar[2]]}, {"sound": "sfx_secretfound"},
             {"message": "$dungeons_bonechoir_relic_bone_found"}])
    trig(r, id="back", on={"event": "lever", "mech": "reset"}, repeat=True, do=[{"reset": "cart"}])
    trig(r, id="hint", on={"event": "enter"}, solver="ignore", do=[{"message": "$dungeons_bonechoir_charnel_hint"}])
    candles(r, (altar[0] + 1.6, F, altar[2] + 0.3), n=2, big=3.5)
    for z in (-3.0, 3.0):
        for s in (-1, 1):
            r.stand("skull_pile", (s * (hx - 0.6), F, z), yaw=90 if s < 0 else 270, scale=(1.5, 1.5, 1.5))
    for x in (-6.0, 0.0, 6.0):
        r.stand("bone_stack", (x, F, hz - 0.7), yaw=180, scale=(1.6, 1.6, 1.6))
    T.light_stand(r, (-hx + 1.0, F, hz - 1.0))
    T.light_stand(r, (hx - 1.0, F, hz - 1.0))
    T.light_hang(r, (0, F + 6.5, 2.0))
    return r


# ---------------------------------------------------------------- the Reliquary

def reliquary():
    """The Reliquary (signature): before the reliquary's doors (the way to the Colossus) three pedestals stand in a row, each carved
    with a relic's likeness: a bell, a skull, a bone. Lay the bell relic, the skull relic and the bone relic each on its own pedestal
    (E; hold E takes one back) and the doors open. A checkpoint by the way in (E): die beyond, and you come back here."""
    sh = shell("reliquary", (16, 14, 20), [("in", 2, 0.0, 0), ("out", 0, 0.0, 0), ("e", 1, -6.0, 0)], tags=["reliquary"], max=1)
    r = sh.room
    r.extra["joinBy"] = ["in"]
    F = sh.floor_y
    hx, hz = sh.half[0] - T.wall_t, sh.half[2] - T.wall_t
    gate(sh, "doors", "out", 0)
    gate(sh, "doors2", "e", 0)
    peds = []
    for id, x, mark in (("ped_bell", -4.5, "bell"), ("ped_skull", 0.0, "skull"), ("ped_bone", 4.5, "bone")):
        p = r.mech(id, "pedestal", (x, F, 1.5))
        p.stand("Ashlands_Ruins_twist_PillarBaseSmall", (0, 0, 0), scale=(1.0, 0.6, 1.0))
        box(p, BLOCK, (0, 1.25, 0), (1.1, 0.1, 1.1))
        if mark == "bell":                                     # the carving on its front: the relic it wants
            p.stand(BELL, (0, 0.0, -0.8), scale=(0.45, 0.45, 0.45))
        elif mark == "skull":
            p.stand("Skull1", (0, 0.0, -0.75), yaw=180, scale=(2.2, 2.2, 2.2))
        else:
            box(p, BONE, (0, 0.09, -0.8), (0.18, 0.75, 0.18), tilt=(0, 90))
        p.pose().pose()                                        # (0 empty, 1 its relic: it takes only its own)
        p.extra.update({"item": "relic_" + mark, "itemAt": [0, 1.35, 0], "fx": [TOLL]})
        p.reached_from = "in"
        peds.append(p)
        candles(r, (x - 1.5, F, 2.6), n=2, big=3.0)
    trig(r, id="open", on=[{"event": "state", "mech": p.id, "state": 1} for p in peds], if_=[{"mech": p.id, "state": 1} for p in peds],
         do=[{"set": "doors", "to": 1}, {"set": "doors2", "to": 1}, {"sound": "sfx_secretfound"}, {"sound": TOLL, "at": [0, F + 4, hz - 2]},
             {"message": "$dungeons_bonechoir_reliquary_open"}])
    trig(r, id="hint", on={"event": "enter"}, solver="ignore", do=[{"message": "$dungeons_bonechoir_reliquary_hint"}])
    cp = r.mech("checkpoint", "checkpoint", (-6.5, F, -7.5), (0, 90, 0))
    cp.fill(BLOCK, (0, 0.5, 0), (0.9, 1.0, 0.9))
    shown(cp.stand(TORCH, (0, 1.0, 0)), [1])
    cp.extra["fx"] = ["fx_guardstone_activate"]
    r.stand("piece_bone_throne", (-4.6, F, hz - 0.8), yaw=180)
    r.stand("skull_pile", (-(hx - 0.8), F, hz - 0.9), yaw=180, scale=(1.5, 1.5, 1.5))
    for s in (-1, 1):
        T.light_stand(r, (s * (hx - 1.0), F, -2.0))
    r.add(ROSE, (0, F + 6.0, hz - 0.5), (0, 180, 0), (1.1, 1.1, 0.5))
    T.light_hang(r, (0, F + 7.0, -1.0))
    return r
