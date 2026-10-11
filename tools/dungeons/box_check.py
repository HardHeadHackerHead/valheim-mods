"""
Every part inside its room's box (python box_check.py [pack.json ...]): a part poking out of the box shows up inside the next room
(rooms are packed box against box). Each part's box (prefab_bounds.json; hollow prefabs by their shapes), in every pose of its
mechanism, must stay within the room's box (0.3 m slack: boxes of turned parts are rough). End caps (no depth) are left out.
"""
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from walk_check import boxes_of, frame  # noqa: E402

SLACK = 0.3


def check_room(room, bounds):
    h = np.array(room["size"]) / 2
    out = []
    for lo, hi, name in boxes_of(room.get("parts", []), np.eye(4), bounds):
        if (lo < -h - SLACK).any() or (hi > h + SLACK).any():
            out.append(f"{name} reaches {np.round(np.maximum(-h - lo, hi - h).max(), 1)} m out of the box")
    for m in room.get("mechs", []):
        for st, pose in enumerate(m.get("poses", []) or [{"at": [0, 0, 0], "euler": [0, 0, 0]}]):
            f = frame(m["at"], m.get("euler", [0, 0, 0])) @ frame(pose["at"], pose["euler"])
            for lo, hi, name in boxes_of(m.get("parts", []), f, bounds):
                if (lo < -h - SLACK).any() or (hi > h + SLACK).any():
                    out.append(f"{m['id']} ({name}, state {st}) reaches {np.round(np.maximum(-h - lo, hi - h).max(), 1)} m out of the box")
                    break
    return out


def main():
    files = sys.argv[1:] or [os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs", n) for n in ("shiftinghalls.json", "testmechs.json")]
    bounds = json.load(open(os.path.join(HERE, "prefab_bounds.json")))
    bad = 0
    for pf in files:
        pack = json.load(open(pf))
        for room in pack["rooms"]:
            if "parts" not in room or 0 in room["size"][::2]:
                continue
            for p in check_room(room, bounds):
                print(f"{pack.get('name', os.path.basename(pf))}: {room['name']}: {p}")
                bad += 1
    print(f"box check: {bad} parts out of their box")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
