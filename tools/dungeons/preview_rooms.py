"""
Pictures of a pack's own rooms (dungeon_view.py), with the game's rooms joined to some of their doorways to check that thresholds and
openings line up. Called by the generators with --preview DIR.
"""
import os
import numpy as np
from dungeon_view import Scene
from dungeon_parts import yaw_turn

CATALOGUE = r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\dungeon-rooms.json"
_catalogue = None


def game_room(name):
    global _catalogue
    if _catalogue is None:
        import json
        _catalogue = {r["name"]: r for r in json.load(open(CATALOGUE))}
    return _catalogue[name]


def attach(scene, room, door_id, game, their=0, origin=(0, 0, 0)):
    """The game's room `game` joined to our room's doorway by its doorway number `their` (turned so the two face each other)."""
    d = room.door(door_id)
    g = game_room(game)
    gd = g["doorways"][their]
    want = -np.array(d["facing"])
    for q in range(4):
        if np.allclose(yaw_turn(gd["facing"], 90 * q), want, atol=0.05):
            at = np.array(d["at"]) - yaw_turn(gd["at"], 90 * q) + np.array(origin)
            scene.add(game, at, (0, 90 * q, 0))
            return at
    raise ValueError(f"{game} doorway {their} can't face {door_id}")


def shifting_halls(pack, halls, out, solve):
    os.makedirs(out, exist_ok=True)
    rooms = {r.name: r for r in pack.rooms if hasattr(r, "parts")}
    # the hall: cut open, with game rooms on a few doorways
    for name, hall in halls.items():
        s = hall.scene(Scene())
        attach(s, hall, "s0s", "dvergr_room01", 0)
        attach(s, hall, "s1e", "dvergr_stair03", 0)
        attach(s, hall, "s2w", "dvergr_corridor01", 0)
        s.render(os.path.join(out, f"{name}_section.png"), views=((200, 22), (290, 30), (20, 35), (110, 25)), section=True, size=(900, 800),
                 title=f"{name}: cut open (the near half left out), stairs at their start")
        for storey, y in enumerate((-14, -6, 2, 10)):
            s = hall.scene(Scene())
            s.render(os.path.join(out, f"{name}_storey{storey}.png"), views=((0, 89),), cut_y=y + 3.5, size=(900, 900), ortho=True,
                     dist=40, title=f"{name}: storey {storey} from above (cut at {y + 3.5})")
        path, _ = solve(hall, "s0n")
        states = {m.id: m.start for m in hall.mechs}
        frames = [dict(states)]
        mech = {m.id: m for m in hall.mechs}
        for lv in path:
            for w in mech[lv].wires:
                t = w["target"]
                n = len(mech[t].poses)
                states[t] = (states[t] + 1) % n if w["do"] == "next" else int(w["do"].split(":")[1])
            frames.append(dict(states))
        s = Scene()
        cols = 5
        for i, st in enumerate(frames):
            hall.scene(s, st, origin=((i % cols) * 30.0, 0, -(i // cols) * 30.0), marks=False)
        s.render(os.path.join(out, f"{name}_solution.png"), views=((0, 89),), ortho=True, cut_y=15.0, size=(1500, 900),
                 title=f"{name}: the stairs after each use, left to right: start, " + ", ".join(path))
    # the rotunda: from above in two states, and at an angle
    r = rooms["rotunda"]
    s = Scene()
    for k in range(4):
        r.scene(s, {"drum": k}, origin=(k * 20.0, 0, 0))
    s.render(os.path.join(out, "rotunda_states.png"), views=((0, 89),), ortho=True, cut_y=2.5, size=(1600, 500),
             title="rotunda: drum states 0-3 (joins n-e, e-s, s-w, w-n)")
    s = r.scene(Scene())
    attach(s, r, "s", "dvergr_corridor01", 0)
    s.render(os.path.join(out, "rotunda.png"), views=((30, 35), (200, 50)), cut_y=3.0, size=(900, 750), title="rotunda (ceiling left out)")
    for name in ("leverroom", "gateroom", "vault"):
        room = rooms[name]
        s = Scene()
        room.scene(s, origin=(0, 0, 0))
        if room.mechs:
            room.scene(s, {m.id: 1 for m in room.mechs}, origin=(12, 0, 0))
        attach(s, room, "in", "dvergr_corridor01", 0)
        s.render(os.path.join(out, f"{name}.png"), views=((35, 30), (215, 30)), section=False, cut_y=3.4 if name != "gateroom" else 5.0,
                 size=(900, 750), title=f"{name} (ceiling left out){', and with its mechanisms in state 1 (right)' if room.mechs else ''}")


def giant(pack, halls, out, solve):
    """Every room of the Giants' clockwork vault, the halls cut open, storey by storey and step by step."""
    os.makedirs(out, exist_ok=True)
    rooms = [r for r in pack.rooms if hasattr(r, "parts")]
    for room in rooms:
        if room.name.startswith("hall_"):
            continue
        top = room.size[1] / 2 - 1.05
        if room.mechs:
            s2 = Scene()
            room.scene(s2, {m.id: 1 for m in room.mechs})
        views = ((150, 24), (330, 24)) if room.size[2] > 0 else ((180, 15), (0, 15))
        s = Scene()
        room.scene(s)
        s.render(os.path.join(out, f"{room.name}.png"), views=views, cut_y=top, section=room.size[2] > 0, size=(900, 760),
                 title=f"{room.name} ({room.size[0]:g} x {room.size[1]:g} x {room.size[2]:g} m, cut open, ceiling left out)")
        if room.mechs:
            s2.render(os.path.join(out, f"{room.name}_state1.png"), views=views[:1], cut_y=top, section=True, size=(900, 760),
                      title=f"{room.name}: its mechanisms in state 1")
    for name, hall in halls.items():
        s = hall.scene(Scene())
        s.render(os.path.join(out, f"{name}_section.png"), views=((200, 22), (290, 30), (20, 35), (110, 25)), section=True,
                 size=(900, 800), title=f"{name}: cut open (the near half left out), stairs at their start")
        s = Scene()
        for i, y in enumerate((-23, -11, 1, 13)):
            hall.scene(s, origin=(i * 40.0, -y, 0), marks=True)
        s.render(os.path.join(out, f"{name}_storeys.png"), views=((0, 89),), ortho=True, cut_y=4.0, size=(1900, 620), dist=80,
                 title=f"{name}: storeys 0-3 from above")
        path, _ = solve(hall, "s0n")
        mech = {m.id: m for m in hall.mechs}
        states = {m.id: m.start for m in hall.mechs}
        frames = [dict(states)]
        for lv in path:
            for w in mech[lv].wires:
                t = w["target"]
                states[t] = (states[t] + 1) % len(mech[t].poses) if w["do"] == "next" else int(w["do"].split(":")[1])
            frames.append(dict(states))
        s = Scene()
        for i, st in enumerate(frames):
            hall.scene(s, st, origin=((i % 6) * 40.0, 0, -(i // 6) * 40.0), marks=False)
        s.render(os.path.join(out, f"{name}_solution.png"), views=((0, 89),), ortho=True, cut_y=23.0, size=(1800, 700),
                 title=f"{name}: the stairs after each use: start, " + ", ".join(path))


def layout_scene(pack_json, describe, ceilings=False):
    """A whole laid-out dungeon (the harness's --describe output) as one scene: every room of the pack's own in place (without their
    ceilings, the parts turned upside down, unless asked)."""
    import json
    from dungeon_parts import Room
    rooms = {r["name"]: r for r in pack_json["rooms"] if "name" in r}
    s = Scene()
    from dungeon_view import frame
    for placed in describe["placed"]:
        r = rooms.get(placed["name"])
        if r is None:
            continue
        f = frame(placed["at"], (0, placed["yaw"], 0))
        for p in r.get("parts", []):
            if not ceilings and abs(p.get("euler", (0, 0, 0))[0] - 180) < 1e-3:
                continue
            s.add(p["prefab"], p["at"], p.get("euler", (0, 0, 0)), p.get("scale", (1, 1, 1)), parent=f,
                  tint=(1.0, 0.85, 0.3) if p.get("saved") else None)
        for m in r.get("mechs", []):
            mf = f @ frame(m["at"], m.get("euler", (0, 0, 0)))
            pose = m["poses"][m.get("start", 0)] if m.get("poses") else None
            if pose:
                mf = mf @ frame(pose["at"], pose["euler"])
            for p in m.get("parts", []):
                s.add(p["prefab"], p["at"], p.get("euler", (0, 0, 0)), p.get("scale", (1, 1, 1)), parent=mf)
    return s


def showcase(pack, halls, out, describe=None, pack_json=None):
    """The pictures for the player: rooms as they look (no markers or tints), from where a player would stand, and a whole dungeon."""
    os.makedirs(out, exist_ok=True)
    rooms = {r.name: r for r in pack.rooms if hasattr(r, "parts")}

    def shot(room, name, views, target, dist, fov=75, **kw):
        s = room.scene(Scene(), marks=False, tint=False)
        s.render(os.path.join(out, name), views=views, size=(1100, 800), target=target, dist=dist, fov=fov, **kw)

    hall = halls[sorted(halls)[1]]
    shot(hall, "1_hall_from_the_floor.png", ((0, -25), (135, -20)), (0, -10, 0), 14, fov=80)
    shot(hall, "2_hall_from_the_top_storey.png", ((200, 28), (300, 35)), (0, 0, 0), 14, fov=80)
    s = hall.scene(Scene(), marks=False, tint=False)
    s.render(os.path.join(out, "3_hall_cut_open.png"), views=((20, 30), (200, 22)), section=True, size=(1100, 900))
    s = rooms["rotunda"].scene(Scene(), {"drum": 2}, marks=False, tint=False)
    s.render(os.path.join(out, "4_rotunda_from_its_south_door.png"), views=((0, 6),), size=(1100, 800), target=(0, -1.5, 0), dist=10,
             fov=85)
    s = rooms["rotunda"].scene(Scene(), marks=False, tint=False)
    s.render(os.path.join(out, "4_rotunda_from_above.png"), views=((30, 40),), size=(1100, 800), cut_y=4.9)
    shot(rooms["greathall"], "5_great_hall.png", ((0, 8), (60, 12)), (0, -5, 0), 10, fov=85)
    shot(rooms["engine"], "6_engine_room.png", ((180, 5),), (0, -4, 0), 6, fov=85)
    shot(rooms["entrance"], "7_entrance.png", ((180, -5), (0, 20)), (0, -4, 2), 8, fov=80)
    shot(rooms["vault"], "8_vault.png", ((0, 12),), (0, -2, 2), 7, fov=80)
    if describe is not None:
        s = layout_scene(pack_json, describe)
        lo, hi = s.bounds()
        s.render(os.path.join(out, "0_a_whole_dungeon.png"), views=((30, 50), (210, 50)), size=(1300, 1000), fov=35,
                 dist=float(np.linalg.norm(hi - lo)) * 1.25)
