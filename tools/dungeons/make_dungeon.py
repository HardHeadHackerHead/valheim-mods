"""
Makes a dungeon pack from its spec (python make_dungeon.py specs/<name>.toml [--seed N] [--out pack.json] [--report report.json]
[--preview folder]):
the spec's theme builds the plan's job rooms and fill rooms through the recipes, adds the theme's signature rooms the spec names,
and writes the pack JSON (default tools/dungeons/bin/made/<name>.json; the gate's --install puts it in mods/Dungeons/packs) and its variety report (default tools/dungeons/reports/<name>.report.json).
Then run walk_check, prop_check, lever_check, box_check and the harness on it (see authoring/README in the docs).
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from authoring.spec import load, build  # noqa: E402

PACKS = os.path.join(HERE, "..", "..", "mods", "Dungeons", "packs")
REPORTS = os.path.join(HERE, "reports")            # (not next to the pack: everything in packs/ is built into the mod as a pack)
MADE = os.path.join(HERE, "bin", "made")            # where a pack is made; make_pack.py --install puts it in packs/ after the gate passes


def preview(pack, folder):
    from dungeon_view import Scene
    os.makedirs(folder, exist_ok=True)
    for room in pack.rooms:
        if not hasattr(room, "scene") or 0 in list(room.size)[::2]:
            continue
        s = Scene()
        room.scene(s, marks=True, tint=False)
        tags = ", ".join(room.tags) if room.tags else ""
        s.render(os.path.join(folder, f"{room.name}.png"), views=((150, 40), (330, 40)), cut_y=room.size[1] / 2 - 1.0, section=True,
                 size=(640, 560), title=f"{room.name} ({room.size[0]:g} x {room.size[1]:g} x {room.size[2]:g} m{'; ' + tags if tags else ''})")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("spec")
    ap.add_argument("--seed", type=int)
    ap.add_argument("--out")
    ap.add_argument("--report")
    ap.add_argument("--preview", help="a folder for a picture of every room (cut open, from above at an angle)")
    args = ap.parse_args()
    spec = load(args.spec)
    if args.seed is not None:
        spec["seed"] = args.seed
    pack, rep = build(spec)
    out = args.out or os.path.join(MADE, spec["name"] + ".json")
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    pack.write(out)
    rp = args.report or os.path.join(REPORTS, spec["name"] + ".report.json")
    with open(rp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(rep, f, indent=1)
    if args.preview:
        preview(pack, args.preview)
    v = rep["variety"]
    print(f"wrote {out}: {len(pack.rooms)} rooms ({v['recipeRooms']} from recipes: {v['jobs']} jobs in {v['shells']} shells; "
          f"{v['signatureRooms']} signature), report {rp}")
    print(f"  spread per job: {v['spread']}")
    if v["sameRooms"]:
        print(f"  nearly the same rooms: {v['sameRooms']}")
    if v["overlap"]:
        print(f"  overlap with other packs: {v['overlap']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
