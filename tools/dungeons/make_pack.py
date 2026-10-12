"""
The gate for a new dungeon pack (python tools/dungeons/make_pack.py <spec> [--seeds 1000] [--out DIR] [--keep-going]).

<spec> is one of:
  - a spec (.toml): built by tools/dungeons/make_dungeon.py (dungeon-content's generator) into a pack and a variety report, both in DIR;
    with --install the pack is copied into mods/Dungeons/packs once every check passes (never before);
  - a generator script (.py, e.g. shifting_halls.py): run, and the pack it writes in mods/Dungeons/packs is the one checked;
  - a pack (.json with "rooms"): checked as it is.
Then every check, in this order, stopping at the first failure with what and where (--keep-going runs the rest anyway):
   1. generation          the builder or generator (a refusal: a prefab it doesn't know, a recipe it can't fit... is its exit code)
   2. pack                PackReader's problems (unknown kinds, doorways, targets, states, fields, looks named...)
   3. walk                walk_check.py: every doorway the solver joins can be walked to in its room
   4. props               prop_check.py: lanterns, chests, spawners... stand on something
      boxes               box_check.py: every part, in every pose, inside its room's box (one poking out shows in the next room)
      creatures           creature_check.py (when it's there): a creature can walk from every encounter's spawn points to its fight
   5. levers              lever_check.py: every lever and exit can be reached and aimed at
   6. gaps                the harness's gap check: no jump or fall round a puzzle, no join blocked, spawn points on floors
   7. seeds               the harness over --seeds seeds: every seed lays out, every layout checked again, every solution replayed
   8. maps                the in-game map drawn offline (tools/dungeonmap) for a few seeds: pictures to look at
   9. look                the pack's entrance look exists and reads (LookCheck)
  10. variety             variety.py: jobs on the way, repeats, recipe spread, and likeness to the other packs
Writes DIR/report.txt (and prints it): one line per check, PASS or FAIL with the reason, and DIR/maps, DIR/rooms (room pictures, when the
generator can draw them). DIR defaults to tools/dungeons/bin/gate/out/<pack name> (ignored by git). Exit 0 when every check passes. It never deploys anything.
"""
import argparse, glob, json, os, shutil, subprocess, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
PACKS = os.path.join(REPO, "mods", "Dungeons", "packs")
GATE_BIN = os.path.join(HERE, "bin", "gate")             # the gate's builds' lock, and its default out folders
BUILDER = os.path.join(HERE, "make_dungeon.py")


def rel(path):
    """A path as the repo sees it, or whole when it's elsewhere (another drive)."""
    try:
        r = os.path.relpath(path, REPO)
        return path if r.startswith("..") else r
    except ValueError:
        return path


def run(cmd, timeout=1800):
    """A command's exit code and its output (stdout and stderr together)."""
    p = subprocess.run(cmd, cwd=REPO, capture_output=True, text=True, timeout=timeout, encoding="utf-8", errors="replace")
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def tail(text, pick=None, n=12):
    lines = [l.rstrip() for l in text.splitlines() if l.strip()]
    if pick:
        chosen = [l for l in lines if any(w in l for w in pick)]
        lines = chosen or lines
    return lines[-n:] if len(lines) > n else lines


class Gate:
    def __init__(self, out, keep_going):
        self.out, self.keep_going, self.results, self.stopped = out, keep_going, [], False

    def step(self, name, fn):
        if self.stopped:
            self.results.append((name, "not run", []))
            return None
        t = time.time()
        try:
            ok, summary, detail = fn()
        except Exception as e:   # (a check that breaks is a failure of the gate's own, said so)
            ok, summary, detail = False, f"the check itself broke: {type(e).__name__}: {e}", []
        status = "PASS" if ok else "FAIL"
        self.results.append((name, f"{status} ({time.time() - t:.0f} s) {summary}", detail))
        print(f"{status:4} {name:10} {summary}")
        for d in (detail if not ok else []):
            print(f"           {d}")
        if not ok and not self.keep_going:
            self.stopped = True
        return ok

    def report(self, pack_name):
        os.makedirs(self.out, exist_ok=True)
        lines = [f"make_pack: {pack_name}", ""]
        for name, line, detail in self.results:
            lines.append(f"{name:10} {line}")
            lines.extend("           " + d for d in detail if not line.startswith("PASS") or name in ("variety", "gaps"))   # (variety: its numbers always; gaps: the skill-0 line)
        passed = all(line.startswith("PASS") for _, line, _ in self.results)
        lines += ["", "RESULT: " + ("PASS" if passed else "FAIL"), f"pictures: {os.path.join(self.out, 'maps')}, {os.path.join(self.out, 'rooms')}"]
        open(os.path.join(self.out, "report.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
        print("\n" + "\n".join(lines[-3:]))
        return passed


def build_tools(harness_dir, map_dir):
    """The harness and the map renderer, built for this run into its own folders (several gates at once: none holds another's files
    open), one build at a time (they share the projects' obj folders): a lock file, waited for, taken over when it's 15 minutes old."""
    os.makedirs(GATE_BIN, exist_ok=True)
    lock = os.path.join(GATE_BIN, ".build.lock")
    start = time.time()
    while True:
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            break
        except FileExistsError:
            try:
                if time.time() - os.path.getmtime(lock) > 900:
                    os.remove(lock)
                    continue
            except OSError:
                continue
            if time.time() - start > 1800:
                raise RuntimeError("another gate's build has held the build lock for 30 minutes (" + lock + ")")
            time.sleep(3)
    try:
        for proj, out in ((os.path.join(HERE, "LayoutTest.csproj"), harness_dir), (os.path.join(REPO, "tools", "dungeonmap", "MapRender.csproj"), map_dir)):
            for attempt in range(5):   # (something else building the same project at once holds its files: wait and try again)
                code, text = run(["dotnet", "build", proj, "-c", "Release", "-o", out], timeout=600)
                if code == 0 or not any(k in text for k in ("MSB3021", "MSB3026", "MSB3027", "being used by another process")):
                    break
                time.sleep(15)
            if code != 0:
                raise RuntimeError(f"{os.path.basename(proj)} doesn't build: " + " | ".join(tail(text, ["error"], 3)))
    finally:
        try:
            os.remove(lock)
        except OSError:
            pass


def main():
    ap = argparse.ArgumentParser(description="the gate for a new dungeon pack")
    ap.add_argument("spec")
    ap.add_argument("--seeds", type=int, default=1000)
    ap.add_argument("--out")
    ap.add_argument("--keep-going", action="store_true", help="run every check even after one fails")
    ap.add_argument("--install", action="store_true", help="a built spec's pack: copy it into mods/Dungeons/packs when every check passes")
    a = ap.parse_args()
    spec = os.path.abspath(a.spec)
    state = {"pack": None, "name": os.path.splitext(os.path.basename(spec))[0], "dump": None, "rooms_dir": None, "report": None, "built": False}
    out = a.out or os.path.join(GATE_BIN, "out", state["name"])   # (under bin/: ignored by git)
    harness_dir, map_dir = os.path.join(out, "_tools", "harness"), os.path.join(out, "_tools", "map")   # (this run's own builds)
    harness = os.path.join(harness_dir, "LayoutTest.exe")
    gate = Gate(out, a.keep_going)

    def generation():
        build_tools(harness_dir, map_dir)
        os.makedirs(out, exist_ok=True)
        rooms_dir = os.path.join(out, "rooms")
        if spec.endswith(".json"):
            j = json.load(open(spec, encoding="utf-8"))
            if "rooms" in j:
                state["pack"] = spec
                name = j.get("name") or state["name"]
                reports = [os.path.splitext(spec)[0] + ".report.json", os.path.join(HERE, "reports", name + ".report.json")]
                state["report"] = next((r for r in reports if os.path.exists(r)), None)
                return True, f"a ready pack ({rel(spec)}): nothing generated", []
            return False, "a .json spec must be a pack (with \"rooms\"); a recipe spec is a .toml", []
        if spec.endswith(".toml"):
            if not os.path.exists(BUILDER):
                return False, "a spec, but there's no generator yet (tools/dungeons/make_dungeon.py, dungeon-content's)", []
            target = os.path.join(out, state["name"] + ".json")
            report = os.path.join(out, "variety_report.json")
            draws = "--preview" in open(BUILDER, encoding="utf-8").read()
            code, text = run([sys.executable, BUILDER, spec, "--out", target, "--report", report] + (["--preview", rooms_dir] if draws else []))
            state["rooms_dir"] = rooms_dir if draws else None
            if code != 0:
                return False, f"the generator refused (exit {code})", tail(text)
            if not os.path.exists(target):
                return False, f"the generator wrote no pack at {target}", tail(text)
            state["pack"], state["built"] = target, True
            state["report"] = report if os.path.exists(report) else None
            return True, f"built {rel(target)}" + ("" if state["report"] else " (no variety report)"), []
        if spec.endswith(".py"):
            before = {f: os.path.getmtime(f) for f in glob.glob(os.path.join(PACKS, "*.json"))}
            cmd = [sys.executable, spec] + (["--preview", rooms_dir] if "--preview" in open(spec, encoding="utf-8").read() else [])
            code, text = run(cmd)
            if code != 0:
                return False, f"the generator refused (exit {code})", tail(text)
            after = {f: os.path.getmtime(f) for f in glob.glob(os.path.join(PACKS, "*.json"))}
            written = [f for f in after if after[f] != before.get(f)]
            if len(written) != 1:
                return False, f"the generator wrote {len(written)} packs in mods/Dungeons/packs (one expected)", written
            state["pack"], state["rooms_dir"] = written[0], rooms_dir if "--preview" in cmd else None
            return True, f"generated {rel(written[0])}", []
        return False, "the spec must be a .json (recipe spec or pack) or a .py generator", []

    def pack_check():
        code, text = run([harness, state["pack"], "--stage", "read"])
        problems = [l.strip() for l in text.splitlines() if "pack problem" in l or "look problem" in l or "can't be read" in l]
        name = json.load(open(state["pack"], encoding="utf-8")).get("name")
        state["name"] = name or state["name"]
        return code == 0 and not problems, f"{len(problems)} problems" if problems else "reads clean", problems[:15]

    def script(name, pick):
        def check():
            code, text = run([sys.executable, os.path.join(HERE, name), state["pack"]])
            return code == 0, "clean" if code == 0 else f"exit {code}", tail(text, pick)
        return check

    def gaps():
        # (puzzles must hold at any skill: judged at the worst, Jump 100 and Run 100, the harness's default; a new character's numbers
        # alongside, as information)
        code, text = run([harness, state["pack"], "--stage", "gaps"])
        summary = next((l.strip() for l in text.splitlines() if "gap check" in l), "no rooms of ours to check")
        bad = [l.strip() for l in text.splitlines() if l.strip().startswith("gaps ") and "unchecked" not in l and not ("only with Parkour" in l and "(information)" in l)]
        _, plain = run([harness, state["pack"], "--stage", "gaps", "--jump-skill", "0", "--run-skill", "0"])
        plain_line = next((l.strip() for l in plain.splitlines() if "gap check" in l), "")
        plain_bad = sum(1 for l in plain.splitlines() if l.strip().startswith("gaps ") and "unchecked" not in l and "only with Parkour" not in l)
        info = [f"skill 0 (information): {plain_line.replace('gap check ', '') if plain_line else 'no rooms of ours to check'}"] if plain_line or plain_bad else []
        return code == 0 and not bad, summary.replace("gap check ", ""), info + bad[:15]

    def seeds():
        state["dump"] = os.path.join(out, "layouts.jsonl")
        code, text = run([harness, state["pack"], "--stage", "seeds", "--seeds", str(a.seeds), "--dump", state["dump"]])
        keep = [l.strip() for l in text.splitlines() if l.startswith(("Seeds ", "Checked again", "Solution length", "Time per layout"))]
        why = [l.rstrip() for l in text.splitlines() if l.startswith("    ") and l.strip()[:1].isdigit()][:10]
        # (judged on its own lines: every seed good, none differing, no faults; the pack's problems are the "pack" check's)
        seeds_line = next((k for k in keep if k.startswith("Seeds ")), "")
        ok = f"{a.seeds} good, 0 failed, 0 came out differently" in seeds_line and any(k.startswith("Checked again") and k.endswith(": 0 faults") for k in keep)
        return ok, " | ".join(k[:110] for k in keep[:2]), keep + why

    def maps():
        layouts = [json.loads(l) for l in open(state["dump"], encoding="utf-8")] if state["dump"] and os.path.exists(state["dump"]) else []
        if not layouts:
            return False, "no good layout to draw", []
        picks = sorted({layouts[0]["seed"], max(layouts, key=lambda x: len(x["solution"]))["seed"]})
        made = []
        for seed in picks:
            folder = os.path.join(out, "maps", f"seed{seed}")
            if os.path.isdir(folder):
                shutil.rmtree(folder)
            code, text = run([os.path.join(map_dir, "MapRender.exe"), state["pack"], "--seed", str(seed), "--visit", "1", "--out", folder])
            if code != 0:
                return False, f"the map for seed {seed} wasn't drawn (exit {code})", tail(text)
            made += glob.glob(os.path.join(folder, "*.png"))
        rooms = len(glob.glob(os.path.join(state["rooms_dir"], "**", "*.png"), recursive=True)) if state["rooms_dir"] else 0
        return bool(made), f"{len(made)} map pictures (seeds {', '.join(map(str, picks))})" + (f", {rooms} room pictures" if rooms else ", no room pictures (the generator draws none)"), []

    def look():
        name = json.load(open(state["pack"], encoding="utf-8")).get("entranceLook")
        if not name:
            return True, "no entranceLook: the default (crypt)", []
        f = os.path.join(PACKS, "looks", name + ".json")
        if not os.path.exists(f):
            return False, f"entranceLook {name}: no packs/looks/{name}.json", []
        code, text = run([harness, "--look", f])
        return code == 0, f"{name}: " + ("fine" if code == 0 else "problems"), tail(text, ["problem"])

    def variety():
        others = [f for f in glob.glob(os.path.join(PACKS, "*.json")) if os.path.abspath(f) != os.path.abspath(state["pack"])]
        cmd = [sys.executable, os.path.join(HERE, "variety.py"), state["pack"], state["dump"], "--others"] + others
        if state["report"]:
            cmd += ["--report", state["report"]]
        code, text = run(cmd)
        lines = [l.strip() for l in text.splitlines() if l.strip()]
        fails = [l for l in lines if l.startswith("FAIL")]
        warns = [l for l in lines if l.startswith("WARN")]
        summary = fails[0][5:] if fails else (f"passes, with a warning: {warns[0][5:]}" if warns else lines[0] if lines else "measured")
        return code == 0, summary, lines

    gate.step("generation", generation)
    gate.step("pack", pack_check)
    gate.step("walk", script("walk_check.py", ["can't", "FAIL", "blocked", "no way", "room"]))
    gate.step("props", script("prop_check.py", ["float", "FAIL", "spawn", "nothing"]))
    gate.step("boxes", script("box_check.py", ["out of", "FAIL"]))   # (a part poking out of its room's box shows in the next room)
    if os.path.exists(os.path.join(HERE, "creature_check.py")):   # (dungeon-boss's: creatures can walk from every spawn point to the fight)
        gate.step("creatures", script("creature_check.py", ["island", "FAIL", "can't", "stuck"]))
    gate.step("levers", script("lever_check.py", ["FAIL", "can't", "aim", "approach"]))
    gate.step("gaps", gaps)
    gate.step("seeds", seeds)
    gate.step("maps", maps)
    gate.step("look", look)
    gate.step("variety", variety)
    passed = gate.report(state["name"])
    if a.install and a.seeds < 1000:
        print(f"not installed: --install needs the full 1000 seeds (this run checked {a.seeds})")
    elif passed and a.install and state["built"]:
        target = os.path.join(PACKS, state["name"] + ".json")
        shutil.copyfile(state["pack"], target)
        print(f"installed: {rel(target)}")
    elif a.install and state["built"]:
        print("not installed: the gate didn't pass")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
