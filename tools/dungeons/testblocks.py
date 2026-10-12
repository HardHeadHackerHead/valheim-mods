"""
Test Blocks (packs/testblocks.json): the smallest pack with every trigger feature and building block, for trying them in the game. The
game's forest crypt rooms, and five rooms of ours in crypt stone, each 12 x 5 x 12, entered from the south ("in"), left by the north
("out"), whose way out is shut until its puzzle is done:

  brazierroom (tag "braziers")  three braziers: lit with a torch in hand, west, middle, east in that order within 60 s (a sequence trigger),
                                they open the gate. A lever by the way in puts them out again (a "reset" of their group).
  keyroom     (tag "key")       walking in drops a small key (a pack key item) by the north wall; three kills in the room say so; the
                                music changes while someone is in it.
  lockroom    (tag "lock")      a lock (needs the small key, used up) across the way out; a checkpoint by the way in.
  pushroom    (tag "push")      a block to push round a pillar on a 3 x 3 grid; on the south-east cell it opens the gate. A lever puts
                                it back.
  traproom    (tag "traps")     spikes across the middle (every 3 s for 1 s), a crusher by the way out that falls once when someone comes
                                near, a one-way teleporter from the way in to the way out (round the spikes), a pedestal that wants a boar
                                trophy (says so, and drops coins), and a timer that drips while someone is in the room.
Plan: start (the crypt's entrance hall), braziers, key, lock, push, traps, end (a burial chamber).

    python testblocks.py
"""
import os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dungeon_parts import Room, Pack, CREATURES, LOOT, slab, lever  # noqa: E402
from testmechs import torch, _side_wall, GAME_ROOMS  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(REPO, "mods", "Dungeons", "packs", "testblocks.json")
STONE = "stone_floor_2x2"
FLOOR = -1.0                    # the floor's top (and the doorways' thresholds)


def shell(name, tags):
    """A 12 x 5 x 12 stone room, its way in south, its way out north (it's joined only by its way in: the way out leads on)."""
    r = Room(name, (12, 5, 12), tags=tags, max=1)
    r.doorway("in", (0, FLOOR, -6), (0, 0, 1), type="")
    r.doorway("out", (0, FLOOR, 6), (0, 0, -1), type="")
    r.extra["joinBy"] = ["in"]
    for k, doors in ((0, [(0.0, FLOOR)]), (2, [(0.0, FLOOR)]), (1, []), (3, [])):
        _side_wall(r, k, 6.0, 6.0, -2.5, 2.5, doors, thickness=0.5)
    slab(r, -6, 6, -6, 6, FLOOR, STONE, thickness=1.5, cell=4.0)
    slab(r, -6, 6, -6, 6, 2.5, STONE, thickness=0.5, cell=4.0)
    for k, lat in ((1, -3.0), (3, 3.0), (1, 3.0), (3, -3.0)):
        torch(r, k, lat, 0.8, 5.5)
    return r


def out_gate(r, id="gate", kind="gate"):
    """An iron gate across the way out, hinged on its west side, swinging open into the room."""
    g = r.mech(id, kind, (-1.2, FLOOR, 5.45))
    g.fill("dungeon_sunkencrypt_irongate", (1.2, 1.5, 0), (2.4, 3.0, 0.12))
    g.pose(euler=(0, 0, 0)).pose(euler=(0, 90, 0))
    g.doorway = "out"
    g.seconds = 2.0
    return g


def shown(part, states):
    """A mechanism's part shown only in these states (PartDef.Show)."""
    part.show = list(states)
    return part


def brazier(r, id, at, group=None):
    b = r.mech(id, "brazier", at)
    b.fill("blackmarble_column_1", (0, 0.4, 0), (0.6, 0.8, 0.6))
    # one model, lit or out: the mod lights only its fire (the game torch's own flame, light, warmth and crackle) while it's lit
    b.stand("CastleKit_groundtorch", (0, 0.8, 0))
    b.seconds = 0.2
    b.extra["fx"] = ["vfx_FireAddFuel", "sfx_FireAddFuel"]
    if group:
        b.extra["group"] = group
    return b


def brazier_room():
    r = shell("brazierroom", ["braziers"])
    out_gate(r)
    for id, x, z in (("west", -3.5, 1.0), ("middle", 0.0, 2.5), ("east", 3.5, 1.0)):
        brazier(r, id, (x, FLOOR, z), group="braziers")
    lever(r, "reset", (3.0, FLOOR, -4.6), 180, [], reached_from="in")
    r.extra["triggers"] = [
        {"id": "order", "on": [{"event": "lit", "mech": "west"}, {"event": "lit", "mech": "middle"}, {"event": "lit", "mech": "east"}],
         "when": "sequence", "within": 60, "group": "braziers",
         "do": [{"set": "gate", "to": 1}, {"sound": "sfx_secretfound"}, {"message": "The flames burn in order: the gate opens."}]},
        {"id": "putout", "on": {"event": "lever", "mech": "reset"}, "repeat": True,
         "do": [{"reset": "braziers"}, {"message": "The flames die down."}]},
    ]
    return r


def key_room():
    r = shell("keyroom", ["key"])
    r.fill("blackmarble_2x2x1", (3.5, FLOOR + 0.4, 4.6), (1.2, 0.8, 0.8))     # a plinth the key falls on (beside the way out)
    r.extra["triggers"] = [
        {"id": "key", "on": {"event": "enter", "at": [0, 0, -2], "size": [12, 5, 8]},
         "do": [{"item": "smallkey", "at": [3.5, 0.2, 4.6]}, {"sound": "sfx_secretfound", "at": [3.5, 0, 4.6]},
                {"message": "Something glints by the north wall."}]},
        {"id": "kills", "on": {"event": "kills", "count": 3}, "solver": "ignore",
         "do": [{"message": "Three have fallen here.", "to": "dungeon"}]},
        {"id": "music", "on": {"event": "enter"}, "repeat": True, "do": [{"music": "CombatEventL1"}]},
        {"id": "quiet", "on": {"event": "leave"}, "repeat": True, "do": [{"music": ""}]},
    ]
    return r


def lock_room():
    r = shell("lockroom", ["lock"])
    lk = out_gate(r, "lock", "lock")
    lk.extra["key"] = "smallkey"
    lk.extra["fx"] = ["sfx_gui_craftitem_workbench_end"]
    cp = r.mech("checkpoint", "checkpoint", (-4.0, FLOOR, -3.5), (0, 90, 0))
    cp.fill("blackmarble_column_1", (0, 0.5, 0), (0.7, 1.0, 0.7))
    shown(cp.stand("piece_groundtorch_blue", (0, 1.0, 0)), [1])
    cp.extra["fx"] = ["fx_guardstone_activate"]
    return r


PUSH_ROWS = ["     ",       # the ring: floor for players only (the block can't go there), so it can be pushed from every side
             " S.. ",
             " .#. ",
             " ... ",
             "     "]


def push_room():
    r = shell("pushroom", ["push"])
    out_gate(r)
    blk = r.mech("block", "pushblock", (0, FLOOR, 0))
    blk.fill("blackmarble_2x2x1", (0, 0.9, 0), (1.8, 1.8, 1.8))
    blk.extra.update({"cell": 2, "rows": PUSH_ROWS, "group": "push", "fx": ["sfx_build_hammer_stone"]})
    blk.seconds = 0.5
    # the grid's pillar ('#', the middle) stands in the room
    r.fill("stone_pillar", (0.0, FLOOR + 1.0, 0.0), (1.6, 2.0, 1.6))
    lever(r, "reset", (-4.5, FLOOR, -4.6), 180, [], reached_from="in")
    cell = 7                    # the south-east corner (column 3, row 3): states are the floor cells in reading order
    r.extra["triggers"] = [
        {"id": "home", "on": {"event": "state", "mech": "block", "state": cell},
         "do": [{"set": "gate", "to": 1}, {"sound": "sfx_secretfound"}, {"message": "The block settles: the gate opens."}]},
        {"id": "back", "on": {"event": "lever", "mech": "reset"}, "repeat": True, "do": [{"reset": "push"}]},
    ]
    return r


def trap_room():
    r = shell("traproom", ["traps"])
    spikes = r.mech("spikes", "trap", (0, FLOOR, 0))
    for x in (-4.5, -1.5, 1.5, 4.5):
        spikes.fill("piece_sharpstakes", (x, 0.6, 0), (2.6, 1.2, 1.6))
    spikes.pose(at=(0, -1.15, 0)).pose(at=(0, 0, 0))            # rest: sunk in the floor; fired: up
    spikes.start = 1            # on from the start (dungeon_parts always writes "start")
    spikes.extra.update({"damage": "pierce", "every": 3, "for": 1, "area": [12, 2, 2], "areaAt": [0, 1, 0],
                         "fx": ["fx_trap_trigger"], "warn": 0.8, "warnFx": ["fx_trap_arm"], "warnLift": 0.1})
    crusher = r.mech("crusher", "trap", (3.0, FLOOR, 4.0))
    crusher.fill("blackmarble_2x2x1", (0, 0.5, 0), (2.0, 1.0, 2.0))
    crusher.pose(at=(0, 2.6, 0)).pose(at=(0, 0, 0))             # rest: up under the ceiling; fired: down
    crusher.extra.update({"damage": "blunt", "for": 1.5, "area": [2.2, 2.0, 2.2], "areaAt": [0, 1, 0], "fx": ["sfx_rock_destroyed"],
                          "warn": 0.8, "warnFx": ["sfx_frozenking_punchaoe_chain_start"], "warnLift": 0.3})
    # a one-way teleporter round the spikes: from the way in to the way out
    for id, z, send in (("tp_in", -4.0, True), ("tp_out", 3.5, False)):
        tp = r.mech(id, "teleporter", (-4.5, FLOOR, z), (0, 180 if send else 0, 0))
        tp.fill("blackmarble_altar_crystal", (0, 0.8, 0), (1.0, 1.6, 1.0))
        tp.extra.update({"pair": "round", "send": send, "fx": ["sfx_OpenPortal"]})
        tp.start = 1            # on
    ped = r.mech("pedestal", "pedestal", (4.5, FLOOR, -3.0))
    ped.fill("blackmarble_column_1", (0, 0.5, 0), (0.7, 1.0, 0.7))
    ped.fill("blackmarble_2x2x1", (0, 1.05, 0), (0.9, 0.1, 0.9))
    ped.extra.update({"item": "TrophyBoar", "accepts": ["TrophyBoar", "TrophyDeer"], "itemAt": [0, 1.15, 0], "fx": ["sfx_secretfound"]})
    r.extra["triggers"] = [
        {"id": "crush", "on": {"event": "enter", "at": [3, 0, 4], "size": [2.2, 3, 2.2]}, "repeat": True, "do": [{"set": "crusher", "to": 1}]},
        {"id": "trophy", "on": {"event": "place", "mech": "pedestal", "item": "TrophyBoar"},
         "do": [{"message": "The boar is honoured."}, {"item": "Coins", "count": 3, "at": [4.5, 0.5, -1.8]}]},
        {"id": "wrong", "on": {"event": "state", "mech": "pedestal", "state": 2}, "repeat": True, "solver": "ignore",
         "do": [{"message": "Not that one."}]},
        {"id": "drip", "on": {"event": "enter"}, "repeat": True, "do": [{"timer": "drip", "every": 10}]},
        {"id": "dry", "on": {"event": "leave"}, "repeat": True, "do": [{"timer": "drip", "stop": True}]},
        {"id": "dripping", "on": {"event": "timer", "name": "drip"}, "repeat": True, "do": [{"sound": "sfx_rock_destroyed", "at": [-5, 2, 0]}]},
    ]
    return r


def build():
    pack = Pack("testblocks", "$dungeons_pack_testblocks", environment="Crypt", entranceLook="crypt", parkour=False, minRooms=7, maxRooms=12,
                maxSize=[120, 40, 120], doors={"": "dungeon_forestcrypt_door"}, doorChance=0.3, creatures=CREATURES, loot=LOOT,
                items={"smallkey": {"look": "CryptKey", "name": "$dungeons_key_small", "description": "$dungeons_key_small_desc"}})
    for g in GAME_ROOMS:
        pack.room(dict(g))
    for room in (brazier_room(), key_room(), lock_room(), push_room(), trap_room()):
        pack.room(room)
    for tag, id in (("entrance", "start"), ("braziers", "braziers"), ("key", "key"), ("lock", "lock"), ("push", "push"), ("traps", "traps"), ("end", "end")):
        pack.step(tag, id)
    return pack


def _with_show(pack):
    """dungeon_parts writes no "show": added here, after the JSON is made, from the parts marked by shown()."""
    data = pack.json()
    for room, rj in zip(pack.rooms, data["rooms"]):
        if not isinstance(room, Room):
            continue
        for m, mj in zip(room.mechs, rj.get("mechs", [])):
            for p, pj in zip(m.parts, mj["parts"]):
                if getattr(p, "show", None) is not None:
                    pj["show"] = p.show
        if "triggers" in rj:            # (the triggers last, after the parts: easier to read)
            rj["triggers"] = rj.pop("triggers")
    return data


def main():
    import json
    from dungeon_parts import prefabs, known_prefabs, _compact
    pack = build()
    data = _with_show(pack)
    missing = sorted(prefabs(data) - known_prefabs())
    if missing:
        raise SystemExit(f"these prefabs aren't in the game's ZNetScene: {', '.join(missing)}")
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(_compact(json.dumps(data, indent=1)) + "\n")
    print("wrote", OUT)
    for r in pack.rooms:
        if isinstance(r, Room):
            print(f"  {r.name}: {len(r.parts)} parts, {len(r.mechs)} mechanisms, {len(r.extra.get('triggers', []))} triggers")
    return 0


if __name__ == "__main__":
    sys.exit(main())
