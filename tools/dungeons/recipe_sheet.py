"""
Contact sheets of recipe rooms (python recipe_sheet.py <theme> <jobs|all> <seeds> <out folder>): each room cut open from above at an
angle, a few to a sheet, titled with what was chosen. For judging variety by eye and comparing themes side by side.

    python recipe_sheet.py giant fight 1,2,3 out/           one sheet: fight rooms from seeds 1-3
    python recipe_sheet.py giant,dvergr fight 1,2 out/      themes side by side: the same job and seeds in each
"""
import importlib
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dungeon_view import Scene  # noqa: E402
from authoring.recipes import make, JOBS  # noqa: E402
import authoring.jobs  # noqa: E402,F401
import authoring.jobs_levels  # noqa: E402,F401


def render(room, title, size=(640, 560)):
    s = Scene()
    room.scene(s, marks=False, tint=False)
    path = os.path.join(os.environ.get("TEMP", "."), "_quaddungeon_sheet_tmp.png")
    s.render(path, views=((150, 42),), cut_y=room.size[1] / 2 - 1.05, section=True, size=size, title=title)
    return Image.open(path).copy()


def main():
    themes = [importlib.import_module(f"authoring.themes.{t}").THEME for t in sys.argv[1].split(",")]
    jobs = list(JOBS) if sys.argv[2] == "all" else sys.argv[2].split(",")
    seeds = [int(x) for x in sys.argv[3].split(",")]
    out = sys.argv[4]
    os.makedirs(out, exist_ok=True)
    for j in jobs:
        tiles = []
        for seed in seeds:
            row = []
            for t in themes:
                if j in t.forbid:
                    continue
                try:
                    room, info = make(j, t, seed, vi=seed)          # (as a pack makes them: one instance after another)
                except Exception as e:
                    print(f"{t.name} {j} {seed}: {e}")
                    continue
                p = info["params"]
                title = f"{t.name} {j} #{seed}: {info['shell']} {int(p['size'][0])}x{int(p['size'][2])}" + \
                        (f", {'+'.join(info['features'])}" if info["features"] else "") + (f", {p.get('dressing')}" if p.get("dressing") else "")
                row.append(render(room, title))
            tiles.append(row)
        if not tiles or not tiles[0]:
            continue
        w, h = tiles[0][0].size
        cols = max(len(r) for r in tiles)
        sheet = Image.new("RGB", (w * cols, h * len(tiles)), (18, 20, 26))
        for i, row in enumerate(tiles):
            for k, im in enumerate(row):
                sheet.paste(im, (k * w, i * h))
        path = os.path.join(out, f"{'_'.join(t.name for t in themes)}_{j}.png")
        sheet.save(path)
        print(path)


if __name__ == "__main__":
    main()
