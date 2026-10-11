"""
The recipe lab (python recipe_lab.py [theme] [jobs] [n]): makes n rooms of each job from seeds 1..n, writes them into a lab pack
(scratch: %TEMP%/quaddungeon_lab_<theme>.json) and runs walk_check, prop_check and lever_check on it. For trying recipes out.
"""
import json, os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from dungeon_parts import Pack, CREATURES, LOOT  # noqa: E402
from authoring.recipes import make, JOBS  # noqa: E402
import authoring.jobs  # noqa: E402,F401
import authoring.jobs_levels  # noqa: E402,F401
import authoring.sidejobs  # noqa: E402,F401
from authoring import themes  # noqa: E402


def theme_by_name(name):
    import importlib
    return importlib.import_module(f"authoring.themes.{name}").THEME


def main():
    theme = theme_by_name(sys.argv[1] if len(sys.argv) > 1 else "giant")
    jobs = sys.argv[2].split(",") if len(sys.argv) > 2 and sys.argv[2] != "all" else list(JOBS)
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 6
    pack = Pack("lab", "lab", environment="Caves", creatures=CREATURES, loot=LOOT)
    infos = []
    for j in jobs:
        if j in theme.forbid:
            continue
        for seed in range(1, n + 1):
            try:
                room, info = make(j, theme, seed)
            except Exception as e:
                print(f"{j} seed {seed}: FAILED {e}")
                continue
            pack.room(room)
            infos.append(info)
    sig = theme.signatures()
    for name in ("entrance", "cap_gate", "cap_rubble", "cap_plain"):
        if name in sig:
            pack.room(sig[name]())
    pack.step("entrance", "start")
    if "lever" in jobs:
        pack.step("lever", "lever1")
        pack.step("gate", "gate1", opens_with="lever1", from_="start")
    out = os.path.join(tempfile.gettempdir(), f"quaddungeon_lab_{theme.name}.json")
    pack.write(out)
    print(f"{len(infos)} rooms -> {out}")
    bad = 0
    for tool in ("walk_check.py", "prop_check.py", "lever_check.py"):
        r = subprocess.run([sys.executable, os.path.join(HERE, tool), out], capture_output=True, text=True)
        print(r.stdout.strip().splitlines()[-1] if r.stdout.strip() else r.stderr[-400:])
        if r.returncode:
            print("\n".join(r.stdout.strip().splitlines()[:-1][:40]))
            bad += 1
    harness = os.path.join(HERE, "bin", "Release", "net48", "LayoutTest.exe")
    if os.path.exists(harness):
        r = subprocess.run([harness, out, "--seeds", "1"], capture_output=True, text=True)
        gaps = [l for l in r.stdout.splitlines() if "gap" in l or "pack problem" in l]
        print(chr(10).join(gaps[-40:]))
        if any("pack problem" in l for l in gaps) or (gaps and "0 joins blocked, 0 walkable or jumpable ways round the puzzle, 0 falls" not in gaps[-1]):
            bad += 1
    json.dump(infos, open(out.replace(".json", "_info.json"), "w"), indent=1)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
