"""
Test Mechanisms (packs/testmechs.json): the smallest pack with every kind of moving part once, for trying the mechanisms in the game.
The game's forest crypt rooms, and two rooms of ours in crypt stone:

  mechroom (tag "lever")  16 x 14 x 14 (deep enough for the sunk stair), its way in low (south), its way out on a ledge 4 m up (north). In it:
      lever   by the way in: opens the gate in the next room (wire "step:gate1", set:1);
      plate   a pressure plate on the floor: each time someone steps on it, the stair turns (next);
      stair   a turning stair, 2 states: 0 turned away to the south and sunk 3.4 m (joins nothing), 1 climbs to the ledge (joins
              in and out);
      lift    a moving platform in the east corner that rises to the ledge and back by itself every 8 s (cycle): joins in and out
              when it's up.
  gateroom (tag "gate")   a crypt corridor with an iron gate across its way in that swings open (gate, doorway "in").
Plan: start (the crypt's entrance hall), lever1 (mechroom), gate1 (gateroom, opened by lever1), end (a burial chamber).

    python testmechs.py [--preview DIR]
"""
import argparse, os, sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dungeon_parts import Room, Pack, CREATURES, LOOT, yaw_turn, slab, lever, side_point, cuts  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

OUT = os.path.join(REPO, "mods", "Dungeons", "packs", "testmechs.json")
STONE = "stone_floor_2x2"       # stone tiles: floors, ceilings and (stood up) walls
STAIR = "stone_stair"           # 2 x 1 x 2, climbs towards -z
TORCH = "piece_walltorch"
CRYPT_DOOR = (2.4, 3.0)         # the forest crypt's openings (width, height)


def torch(room, k, lateral, y, depth):
    """A wall torch on side k's wall (depth: the wall's inside face), facing into the room."""
    room.stand(TORCH, side_point(k, lateral, y, depth - 0.2), yaw=(180 + 90 * k) % 360)


def mech_room():
    r = Room("mechroom", (16, 14, 14), tags=["lever"], max=1)
    r.doorway("in", (0, -3.5, -7), (0, 0, 1), type="")
    r.doorway("out", (0, 0.5, 7), (0, 0, -1), type="")
    r.extra["drops"] = [{"a": "out", "b": "in"}]          # off the ledge to the floor: 4 m, no harm
    hx, hz = 8.0, 7.0
    # walls (the room isn't square: north and south walls are 16 wide at depth 7, east and west 14 wide at depth 8)
    for k, half, across, doors in ((0, hz, hx, [(0.0, 0.5)]), (2, hz, hx, [(0.0, -3.5)]), (1, hx, hz, []), (3, hx, hz, [])):
        _side_wall(r, k, half, across, -5.0, 5.0, doors)
    slab(r, -hx, hx, -hz, hz, -3.5, STONE, thickness=1.5, cell=4.0)
    slab(r, -hx, hx, -hz, hz, 5.0, STONE, thickness=0.5, cell=4.0)
    # the ledge: 4 m up along the north wall, on a sheer stone face (pillars under it were footholds up to it: the gap check)
    slab(r, -hx + 1, hx - 1, 3.0, hz - 0.5, 0.5, STONE, thickness=0.6, cell=4.0)
    r.fill(STONE, (0.0, -1.8, 3.3), (2 * (hx - 1), 3.4, 0.6), euler=(90, 0, 0))
    for x in (-6.5, -2.5, 1.5, 5.5):
        r.fill("wood_beam", (x + 1.0, 1.15, 3.1), (2.0, 0.2, 0.2))
    # the lever by the way in, the plate by the stair's foot
    lever(r, "lever", (3.0, -3.5, -5.6), 180, [("step:gate1", "set:1")], reached_from="in")
    plate = r.mech("plate", "plate", (-4.0, -3.5, -5.2))
    plate.fill(STONE, (0, 0.06, 0), (1.6, 0.12, 1.6))
    plate.pose(at=(0, 0, 0)).pose(at=(0, -0.08, 0))
    plate.seconds = 0.3
    plate.wire("stair", "next")
    plate.reached_from = "in"
    # the turning stair: 4 m up over 6 m, pivoting on its middle; state 1 climbs north to the ledge's edge
    st = r.mech("stair", "stair", (-4.0, -1.5, 0.0))
    for i in range(4):
        st.fill(STAIR, (0, -2.0 + i + 0.5, -3.0 + 1.5 * i + 0.75), (2.0, 1.0, 1.5), euler=(0, 180, 0))
    st.fill("wood_beam", (-1.15, -0.45, 0), (0.2, 0.25, 6.6))
    st.fill("wood_beam", (1.15, -0.45, 0), (0.2, 0.25, 6.6))
    st.fill("stone_pillar", (0, -1.6, 0), (0.9, 0.8, 0.9))
    # 0: turned away and sunk 3.4 m, its top more than a jump (3.1 m at Jump 100) below the ledge
    st.pose(at=(0, -3.4, 0), euler=(0, 180, 0)).pose(euler=(0, 0, 0))
    st.seconds = 2.5
    st.join("in", "out", state=1)
    # the lift: a 2 x 2 stone platform by the ledge's east end, up and down by itself
    lift = r.mech("lift", "platform", (6.0, -3.5, 2.0))
    lift.fill(STONE, (0, -0.25, 0), (2.0, 0.5, 2.0))
    for x in (-0.9, 0.9):
        lift.fill("wood_pole2", (x, 0.5, 0.9), (0.15, 1.0, 0.15))
    lift.pose(at=(0, 0, 0)).pose(at=(0, 4.0, 0))
    lift.seconds = 3.0
    lift.cycle = 8.0
    lift.join("in", "out", state=1)
    for k, lat, depth in ((2, 4.0, hz - 0.65), (2, -4.0, hz - 0.65), (0, 4.0, hz - 0.65), (1, -3.0, hx - 0.65), (3, 3.0, hx - 0.65)):
        torch(r, k, lat, -1.0 if k != 0 else 2.6, depth)
    return r


def _side_wall(room, k, depth, across, y0, y1, doors, thickness=0.7):
    """A stone wall on side k at `depth` from the middle, `across` metres either side, with crypt-sized openings [(lateral, threshold)]."""
    w, h = CRYPT_DOOR
    cols = cuts(-across, across, [e for l, _ in doors for e in (l - w / 2, l + w / 2)], 4.0)
    rows = cuts(y0, y1, [e for _, t in doors for e in (t, t + h)], 4.0)
    for a, b in cols:
        for lo, hi in rows:
            x, y = (a + b) / 2, (lo + hi) / 2
            if any(abs(x - l) < w / 2 and t < y < t + h for l, t in doors):
                continue
            room.fill(STONE, side_point(k, x, y, depth - thickness / 2), np.abs(yaw_turn((b - a, hi - lo, thickness), 90 * k)),
                      euler=(90, 90 * k, 0))


def gate_room():
    """A crypt corridor (4 x 5 x 8, as the game's forestcrypt_Corridor1) with an iron gate across its way in, swinging open into it."""
    r = Room("gateroom", (4, 5, 8), tags=["gate"], max=1)
    r.doorway("in", (0, -1, -4), (0, 0, 1), type="")
    r.doorway("out", (0, -1, 4), (0, 0, -1), type="")
    for k, depth, across, doors in ((0, 4.0, 2.0, [(0.0, -1.0)]), (2, 4.0, 2.0, [(0.0, -1.0)]), (1, 2.0, 4.0, []), (3, 2.0, 4.0, [])):
        _side_wall(r, k, depth, across, -2.5, 2.5, doors, thickness=0.5)
    slab(r, -2, 2, -4, 4, -1.0, STONE, thickness=1.5, cell=4.0)
    slab(r, -2, 2, -4, 4, 2.5, STONE, thickness=0.5, cell=4.0)
    # the gate: hinged on the west side of the way in, 2.4 wide; open, it lies along the west wall
    g = r.mech("gate", "gate", (-1.2, -1.0, -3.55))
    g.fill("dungeon_sunkencrypt_irongate", (1.2, 1.5, 0), (2.4, 3.0, 0.12))
    g.pose(euler=(0, 0, 0)).pose(euler=(0, -90, 0))
    g.doorway = "in"
    g.seconds = 2.0
    torch(r, 1, 1.5, 0.6, 1.5)
    return r


GAME_ROOMS = [
    {"game": "forestcrypt_entrance_large", "tags": ["entrance"]},
    {"game": "forestcrypt_Corridor1", "weight": 2},
    {"game": "forestcrypt_Corridor2", "weight": 2},
    {"game": "forestcrypt_Bend1"},
    {"game": "forestcrypt_room1"},
    {"game": "forestcrypt_new_BurialChamber01", "tags": ["end"]},
    {"game": "forestcrypt_new_BurialChamber02", "tags": ["end"]},
    {"game": "forestcrypt_EndCap", "weight": 0.5},
    {"game": "forestcrypt_EndCap2"},
]


def build():
    pack = Pack("testmechs", "$dungeons_pack_testmechs", environment="Crypt", entranceLook="crypt", parkour=False, minRooms=5, maxRooms=9, maxSize=[80, 40, 80],
                doors={"": "dungeon_forestcrypt_door"}, doorChance=0.3, creatures=CREATURES, loot=LOOT)
    for g in GAME_ROOMS:
        pack.room(dict(g))
    pack.room(mech_room())
    pack.room(gate_room())
    pack.step("entrance", "start")
    pack.step("lever", "lever1")
    pack.step("gate", "gate1", opens_with="lever1")
    pack.step("end", "end")
    return pack


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args()
    pack = build()
    pack.write(args.out)
    print("wrote", args.out)
    for r in pack.rooms:
        if isinstance(r, Room):
            print(f"  {r.name}: {len(r.parts)} parts, {len(r.mechs)} mechanisms, {len(r.doorways)} doorways")
    if args.preview:
        from dungeon_view import Scene
        import preview_rooms
        os.makedirs(args.preview, exist_ok=True)
        rooms = {r.name: r for r in pack.rooms if isinstance(r, Room)}
        for name, cut in (("mechroom", 4.5), ("gateroom", 2.0)):
            room = rooms[name]
            s = Scene()
            room.scene(s)
            room.scene(s, {m.id: 1 for m in room.mechs}, origin=(20.0, 0, 0))
            preview_rooms.attach(s, room, "in", "forestcrypt_Corridor1", 0)
            s.render(os.path.join(args.preview, f"testmechs_{name}.png"), views=((30, 40), (210, 40)), cut_y=cut, size=(1000, 800),
                     title=f"{name}: mechanisms at their start (left) and in state 1 (right), ceiling left out")
    return 0


if __name__ == "__main__":
    sys.exit(main())
