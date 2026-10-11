"""
Is a pack varied, and is it its own? (python variety.py pack.json layouts.jsonl [--others a.json b.json ...] [thresholds])
The layouts are the harness's --dump (one good layout per line: its rooms, the way from the entrance to the end, the solution).

A room's JOBS come from what's in it: its mechanisms (a lever, a gate, a turning stair, a rotating room, a plate, a fight, a boss, a secret
wall, the triggers' blocks...), its "fight" tag, a "signature" or "setpiece" tag (or the generator's report calling it a signature room),
its triggers, its drops. A room with none is walked through. A CONNECTOR is a room with no job that only links others, with two
doorways or more and its narrow side 8 m or less (corridors, galleries, stairwells), tagged "connector" or not. A room tagged "connector"
that's wider counts as a room all the same (a warning names it), so the tag can't hide a jobless room.
  - jobless: of the rooms on the way to the end that aren't connectors (the entrance left out), the share with no job; warned above
    --jobless. The jobless rooms are named, most often first, and the share with connectors is reported too;
  - connectors: their share of the rooms on the way, warned above --connectors.
    With --jobless-fail, the jobless, connector and wide-connector warnings are failures;
  - jobs on the way: the distinct jobs of the rooms on the way to the end, per layout (at least --min-jobs in --jobs-share of them);
  - repeats: the same room with a job twice within --window rooms on the way (in at most --repeat-share of the layouts);
  - shells: how many different rooms a layout uses (reported);
  - spread: two rooms of one recipe whose numeric parameters all differ by less than --spread (needs the generator's <pack>.recipes.json:
    {room name: {"recipe": name, "params": {...}}}; without it, rooms of ours with the same shape and jobs under different names are
    reported as near copies);
  - with the generator's report (--report, make_dungeon.py's: its rooms' recipe and shell, its signatures, and its variety numbers):
    the pack's jobs (at least --pack-jobs) and shells (--pack-shells), each job's spread (at least --job-spread), its near-copy pairs
    (at most --same-rooms), its overlap shares (at most --share), and from the layouts: the same recipe and shell twice within
    --recipe-window rooms on the way (rooms, not connectors; in at most --recipe-repeat-share of layouts), a signature room on the way
    (in --signature-share of layouts). Older notes, kept for packs without that report:
    the same recipe and shell (rooms with a job) within --recipe-window rooms on the way (at most --repeat-share of layouts), each recipe's spread from
    its instances' params and features (end caps left out; mean at least --recipe-spread, no pair under --spread), a signature room on the way (in --signature-share of
    layouts), and its overlap share with every other pack (at most --share);
  - overlap: against every other pack that isn't a test pack (its name starts with "test"), a structural likeness from the plan's steps,
    the rooms' shapes (size to 4 m, doorways, jobs) and the mix of mechanisms, prefabs left out. At --overlap or more it fails:
    "this is <pack> in another colour" when their prefabs differ, "this is <pack> again" when they don't.
Exit 1 on any failure; prints one line per measure.
"""
import argparse, collections, difflib, json, math, os, sys

JOB_OF_KIND = {"lever": "lever", "plate": "plate", "gate": "gate", "door": "gate", "lock": "lock", "stair": "stair", "rotator": "rotator",
               "platform": "platform", "floor": "floor", "encounter": "fight", "breakable": "secret", "shortcut": "shortcut",
               "pedestal": "pedestal", "brazier": "brazier", "pushblock": "push", "trap": "trap", "teleporter": "teleport"}


SIGNATURE = set()   # room names the generator's report calls signature rooms (set from --report)


def jobs_of(room):
    jobs = set()
    if room.get("name") in SIGNATURE or any(str(t).lower() in ("signature", "setpiece") for t in room.get("tags", [])):
        jobs.add("setpiece")
    for m in room.get("mechs", []):
        j = JOB_OF_KIND.get(m.get("kind"))
        if j == "fight" and m.get("boss"):
            j = "boss"
        if j == "rotator" and not m.get("joins"):
            j = None   # (a turning thing for the look)
        if j:
            jobs.add(j)
    if any(str(t).lower() == "fight" for t in room.get("tags", [])):
        jobs.add("fight")
    if room.get("triggers"):
        jobs.add("trigger")
    if room.get("drops"):
        jobs.add("drop")
    return jobs


CONNECTOR_SIDE = 8.0   # metres: a connector's narrow side at most this (a corridor, a gallery, a stairwell)


def tagged_connector(room):
    return any(str(t).lower() == "connector" for t in room.get("tags", []))


def connector(room, size_of):
    """A room with no job that only links others: two doorways or more and its narrow side CONNECTOR_SIDE or less. The "connector" tag
    doesn't make a wider room one (it counts as a room)."""
    if jobs_of(room):
        return False
    size, doors = size_of(room)
    return size is not None and doors >= 2 and min(size[0], size[2]) <= CONNECTOR_SIDE


def shape_of(room):
    size = room.get("size") or [0, 0, 0]
    return (tuple(int(round(v / 4.0)) for v in size), len(room.get("doorways", [])), tuple(sorted(jobs_of(room))))


def likeness(a, b):
    """Structural likeness of two packs (0..1): plan steps, room shapes, mechanism mix; and their prefab overlap (0..1)."""
    plan = difflib.SequenceMatcher(None, [s.get("tag") for s in a.get("plan", [])], [s.get("tag") for s in b.get("plan", [])]).ratio()
    sa = collections.Counter(shape_of(r) for r in a["rooms"] if not r.get("game"))
    sb = collections.Counter(shape_of(r) for r in b["rooms"] if not r.get("game"))
    shapes = sum((sa & sb).values()) / max(1, sum((sa | sb).values()))
    ma = collections.Counter(m.get("kind") for r in a["rooms"] for m in r.get("mechs", []))
    mb = collections.Counter(m.get("kind") for r in b["rooms"] for m in r.get("mechs", []))
    keys = set(ma) | set(mb)
    dot = sum(ma[k] * mb[k] for k in keys)
    mix = dot / (math.sqrt(sum(v * v for v in ma.values())) * math.sqrt(sum(v * v for v in mb.values())) or 1)
    pa = {p.get("prefab") for r in a["rooms"] for p in r.get("parts", [])}
    pb = {p.get("prefab") for r in b["rooms"] for p in r.get("parts", [])}
    prefabs = len(pa & pb) / max(1, len(pa | pb))
    return (plan + shapes + mix) / 3.0, plan, shapes, mix, prefabs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pack")
    ap.add_argument("layouts")
    ap.add_argument("--others", nargs="*", default=[])
    ap.add_argument("--recipes", help="the generator's <pack>.recipes.json (default: next to the pack)")
    ap.add_argument("--min-jobs", type=int, default=4)
    ap.add_argument("--jobs-share", type=float, default=0.95)
    ap.add_argument("--window", type=int, default=3)
    ap.add_argument("--repeat-share", type=float, default=0.10)
    ap.add_argument("--spread", type=float, default=0.15)
    ap.add_argument("--overlap", type=float, default=0.70)
    ap.add_argument("--report", help="the generator's variety report (make_dungeon.py --report)")
    ap.add_argument("--jobless", type=float, default=0.30)
    ap.add_argument("--connectors", type=float, default=0.25)
    ap.add_argument("--jobless-fail", action="store_true", help="fail (not only warn) on the jobless, connector and wide-connector measures")
    ap.add_argument("--catalogue", default=r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\dungeon-rooms.json")
    ap.add_argument("--recipe-window", type=int, default=4)
    ap.add_argument("--recipe-repeat-share", type=float, default=0.05)
    ap.add_argument("--signature-share", type=float, default=0.90)
    ap.add_argument("--share", type=float, default=0.20)
    ap.add_argument("--pack-jobs", type=int, default=8)
    ap.add_argument("--pack-shells", type=int, default=4)
    ap.add_argument("--job-spread", type=float, default=0.20)
    ap.add_argument("--same-rooms", type=int, default=3)
    a = ap.parse_args()

    pack = json.load(open(a.pack, encoding="utf-8"))
    rooms = {(r.get("name") or r.get("game")): r for r in pack["rooms"]}
    layouts = [json.loads(line) for line in open(a.layouts, encoding="utf-8") if line.strip()]
    fails, lines, warns = [], [], []
    if a.report and os.path.exists(a.report):
        rj = json.load(open(a.report, encoding="utf-8"))
        SIGNATURE.update(rj.get("signatures", []))
        SIGNATURE.update(r["name"] for r in rj.get("rooms", []) if r.get("signature"))
    catalogue = {}
    if os.path.exists(a.catalogue):
        catalogue = {r["name"]: r for r in json.load(open(a.catalogue, encoding="utf-8"))}

    def size_of(room):
        game = catalogue.get(room.get("game") or "")
        size = room.get("size") or (game or {}).get("size")
        doors = len(room.get("doorways") or (game or {}).get("doorways") or [])
        return size, doors
    if not layouts:
        print("variety: no good layouts to measure")
        return 1

    # jobs on the way, repeats, shells
    few, repeated, walked, shells = 0, 0, [], []
    first_repeat = None
    for lay in layouts:
        way = [lay["rooms"][i]["name"] for i in lay["path"]]
        jobs = set()
        for name in way:
            jobs |= jobs_of(rooms.get(name, {}))
        if len(jobs) < a.min_jobs:
            few += 1
        walked.append(sum(1 for n in way if not jobs_of(rooms.get(n, {}))) / max(1, len(way)))
        busy = [n for n in way if jobs_of(rooms.get(n, {}))]
        for i in range(len(way)):
            n = way[i]
            if n in busy and n in way[i + 1:i + a.window]:
                repeated += 1
                first_repeat = first_repeat or f"seed {lay['seed']}: {n} twice within {a.window} rooms on the way"
                break
        shells.append(len({r["name"] for r in lay["rooms"] if not r["cap"]}))
    n = len(layouts)
    ok_jobs = (n - few) / n
    lines.append(f"jobs on the way: {ok_jobs:.0%} of {n} layouts have {a.min_jobs} or more (needs {a.jobs_share:.0%})")
    if ok_jobs < a.jobs_share:
        fails.append(f"too few jobs: {few} of {n} layouts have under {a.min_jobs} different jobs on the way to the end")
    lines.append(f"repeats: {repeated / n:.0%} of layouts have a room with a job twice within {a.window} on the way (at most {a.repeat_share:.0%})"
                 + (f"; first: {first_repeat}" if first_repeat else ""))
    if repeated / n > a.repeat_share:
        fails.append(f"repeats: {repeated} of {n} layouts repeat a room with a job within {a.window} rooms ({first_repeat})")
    lines.append(f"shells: {min(shells)}-{max(shells)} different rooms per layout ({sum(shells) / n:.1f} on average)")

    # jobless rooms on the way (connectors apart)
    share, share_all, idle, links = [], [], collections.Counter(), collections.Counter()
    for lay in layouts:
        way = [lay["rooms"][i]["name"] for i in lay["path"][1:]]   # (the entrance left out)
        real = [x for x in way if not connector(rooms.get(x, {"name": x}), size_of)]
        jobless = [x for x in real if not jobs_of(rooms.get(x, {}))]
        share.append(len(jobless) / max(1, len(real)))
        share_all.append(sum(1 for x in way if not jobs_of(rooms.get(x, {}))) / max(1, len(way)))
        idle.update(jobless)
        links.update(x for x in way if x not in real)
    mean = sum(share) / n
    over = sum(1 for v in share if v > a.jobless) / n
    conn_share = sum(links.values()) / max(1, sum(len(lay["path"]) - 1 for lay in layouts))
    lines.append(f"jobless: {mean:.0%} of the rooms on the way have no job, connectors apart (at most {a.jobless:.0%}; {over:.0%} of layouts above it); "
                 f"{sum(share_all) / n:.0%} with connectors counted")
    lines.append("  jobless rooms (times on the way): " + ", ".join(f"{k} {v}" for k, v in idle.most_common(12)))
    lines.append("  connectors (times on the way): " + ", ".join(f"{k} {v}" for k, v in links.most_common(8)))
    lines.append(f"connectors: {conn_share:.0%} of the rooms on the way (at most {a.connectors:.0%})")
    flagged = []
    if mean > a.jobless:
        flagged.append(f"jobless rooms: {mean:.0%} of the rooms on the way have no job (at most {a.jobless:.0%}); most often: " + ", ".join(k for k, _ in idle.most_common(6)))
    if conn_share > a.connectors:
        flagged.append(f"connectors: {conn_share:.0%} of the rooms on the way (at most {a.connectors:.0%}); most often: " + ", ".join(k for k, _ in links.most_common(4)))
    for r in pack["rooms"]:
        if tagged_connector(r) and not jobs_of(r):
            size, _ = size_of(r)
            if size is not None and min(size[0], size[2]) > CONNECTOR_SIDE:
                flagged.append(f"{r.get('name') or r.get('game')} is tagged \"connector\" but its narrow side is {min(size[0], size[2]):g} m "
                               f"(over {CONNECTOR_SIDE:g}): counted as a room")
    (fails if a.jobless_fail else warns).extend(flagged)

    # the generator's report (make_dungeon.py): its pack-level numbers, spread per job, near-copies, overlap; recipe and shell repeats
    # on the way and signature rooms from the layouts
    if a.report and os.path.exists(a.report):
        rep = json.load(open(a.report, encoding="utf-8"))
        info = {r["name"]: r for r in rep.get("rooms", [])}
        var = rep.get("variety", {})
        jobs_n, shells_n = var.get("jobs", 0), var.get("shells", 0)
        lines.append(f"pack: {jobs_n} jobs (at least {a.pack_jobs}), {shells_n} shells (at least {a.pack_shells}), "
                     f"{var.get('recipeRooms', len(info))} recipe rooms, {var.get('signatureRooms', len(SIGNATURE))} signature rooms")
        if jobs_n < a.pack_jobs:
            fails.append(f"only {jobs_n} jobs in the pack (at least {a.pack_jobs})")
        if shells_n < a.pack_shells:
            fails.append(f"only {shells_n} room shells in the pack (at least {a.pack_shells})")
        # recipe + shell twice within --recipe-window rooms on the way (rooms, not connectors)
        rep_hits, first, sig_ok = 0, None, 0
        for lay in layouts:
            way = [lay["rooms"][i]["name"] for i in lay["path"]]
            keys = [(info[x].get("recipe"), info[x].get("shell")) if x in info and not connector(rooms.get(x, {"name": x}), size_of) else None
                    for x in way]
            hit = next((keys[i] for i in range(len(keys)) if keys[i] and keys[i] in keys[i + 1:i + a.recipe_window]), None)
            if hit:
                rep_hits += 1
                first = first or f"seed {lay['seed']}: {hit[0]}/{hit[1]} twice within {a.recipe_window} rooms on the way"
            if any(x in SIGNATURE for x in way):
                sig_ok += 1
        lines.append(f"recipe repeats: {rep_hits / n:.0%} of layouts have one recipe and shell twice within {a.recipe_window} rooms on the way "
                     f"(at most {a.recipe_repeat_share:.0%})" + (f"; first: {first}" if first else ""))
        if rep_hits / n > a.recipe_repeat_share:
            fails.append(f"recipe repeats: {rep_hits} of {n} layouts ({first})")
        lines.append(f"signature rooms: on the way in {sig_ok / n:.0%} of layouts (needs {a.signature_share:.0%})")
        if sig_ok / n < a.signature_share:
            fails.append(f"too few signature rooms on the way: {sig_ok} of {n} layouts pass one")
        # spread per job, and near-copies (the report's own numbers, by its distance)
        for job, spread in sorted((var.get("spread") or {}).items()):
            low = spread < a.job_spread
            lines.append(f"spread {job}: {spread:.2f}" + (f" (under {a.job_spread})" if low else ""))
            if low:
                fails.append(f"job {job}: its rooms are too alike (spread {spread:.2f}, at least {a.job_spread})")
        same = var.get("sameRooms") or []
        lines.append(f"near-copies: {len(same)} pairs closer than 0.15 (at most {a.same_rooms})"
                     + (": " + "; ".join(f"{x} and {y} ({d})" for x, y, d in same[:8]) if same else ""))
        if len(same) > a.same_rooms:
            fails.append(f"{len(same)} pairs of near-copy rooms (at most {a.same_rooms}): " + "; ".join(f"{x} and {y}" for x, y, _ in same[:6]))
        for other, share in (var.get("overlap") or {}).items():
            lines.append(f"overlap share with {other}: {share:.0%} (at most {a.share:.0%})")
            if share > a.share:
                fails.append(f"{share:.0%} of its recipe rooms are nearly in {other} (at most {a.share:.0%})")

    # spread between instances of a recipe (an older sidecar: {room: {recipe, params}})
    recipes_file = a.recipes or os.path.splitext(a.pack)[0] + ".recipes.json"
    if a.report and os.path.exists(a.report):
        pass
    elif os.path.exists(recipes_file):
        recipes = json.load(open(recipes_file, encoding="utf-8"))
        by = collections.defaultdict(list)
        for room, info in recipes.items():
            by[info.get("recipe")].append((room, info.get("params", {})))
        same = []
        for recipe, inst in by.items():
            for i in range(len(inst)):
                for j in range(i + 1, len(inst)):
                    pa, pb = inst[i][1], inst[j][1]
                    keys = [k for k in pa if k in pb and isinstance(pa[k], (int, float)) and isinstance(pb[k], (int, float))]
                    diff = max([abs(pa[k] - pb[k]) / max(abs(pa[k]), abs(pb[k]), 1e-9) for k in keys] + [0.0])
                    others = any(pa[k] != pb[k] for k in pa if k in pb and k not in keys)
                    if diff < a.spread and not others:
                        same.append(f"{inst[i][0]} and {inst[j][0]} ({recipe}: parameters within {diff:.0%})")
        lines.append(f"spread: {sum(len(v) for v in by.values())} rooms from {len(by)} recipes; {len(same)} pairs too alike")
        if same:
            fails.append("instances of one recipe too alike: " + "; ".join(same[:5]))
    else:
        own = [r for r in pack["rooms"] if not r.get("game") and not r.get("endCap") and jobs_of(r)]
        sig = collections.defaultdict(list)
        for r in own:
            sig[shape_of(r)].append(r["name"])
        near = [names for names in sig.values() if len(names) > 1]
        lines.append(f"spread: unchecked (no {os.path.basename(recipes_file)}); near copies by shape and jobs: "
                     + ("; ".join(", ".join(x) for x in near) if near else "none"))

    # overlap with other packs
    for other in a.others:
        o = json.load(open(other, encoding="utf-8"))
        if o.get("name") == pack.get("name") or str(o.get("name", "")).startswith("test"):
            continue
        like, plan, shapes, mix, prefabs = likeness(pack, o)
        lines.append(f"overlap with {o['name']}: {like:.2f} (plan {plan:.2f}, room shapes {shapes:.2f}, mechanism mix {mix:.2f}; prefabs {prefabs:.2f})")
        if like >= a.overlap:
            fails.append(f"this is {o['name']} " + ("in another colour" if prefabs < 0.5 else "again")
                         + f": likeness {like:.2f} (at most {a.overlap:.2f}); plan {plan:.2f}, room shapes {shapes:.2f}, mechanisms {mix:.2f}")

    for l in lines:
        print("  " + l)
    for w in warns:
        print("  WARN " + w)
    for f in fails:
        print("  FAIL " + f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
