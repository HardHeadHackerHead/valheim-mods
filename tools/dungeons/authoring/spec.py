"""
A dungeon spec: the short file an AI (or anyone) writes to make a dungeon kind. It says what the dungeon IS, never how a room is built:

    name = "shiftinghalls"                       # the pack's name
    title = "$dungeons_pack_shiftinghalls"       # its translation key
    theme = "giant"                              # architecture: authoring/themes/<theme>.py
    seed = 1                                     # which rooms the recipes make (another seed: other rooms, the same dungeon)
    difficulty = "normal"                        # easy | normal | hard: the size of every fight's waves
    biome = "any"                                # where its entrances go: creatures and loot always follow the entrance's biome
    environment = "Caves"                        # the game's environment inside
    entrance_look = "giant"
    rooms = [16, 28]                             # a layout's room count (min, max)
    max_size = [320, 140, 320]
    min_uses = 8                                 # the puzzle's fewest uses
    music = "forestcrypt"                        # the default track inside (fights and bosses set their own)
    parkour = false

    # the way through, in order: a job (made by a recipe) or a signature room (made by hand, the theme's own)
    plan = [
      {id = "start", room = "entrance"},
      {id = "fight1", job = "fight"},
      {id = "lever1", job = "lever", from = "hub1"},
      {id = "gate1", job = "gate", opens_with = "lever1", from = "hub1"},
      {id = "hall", rooms = ["hall_a", "hall_b", "hall_c"]},
      ...]

    [fill]                                       # recipe rooms besides the plan's: job = how many different rooms
    breather = 8
    nook = 4

    signatures = ["greathall", "cap_gate"]       # more hand-made rooms of the theme's, used where they fit
    variants = 2                                 # rooms made per job step of the plan (the layout picks one)

    [shortcut]                                   # optional
    near = "shortcut_near"
    far = "shortcut_far"
    far_step = "guardian1"

    [creatures]                                  # optional: per biome role lists; default dungeon_parts.CREATURES
    [loot]                                       # optional: per biome lists; default dungeon_parts.LOOT

build(spec) -> (Pack, report): the pack JSON (the format Packs.cs reads) and the variety report (variety.py).
"""
import importlib
import tomllib

from dungeon_parts import Pack, CREATURES, LOOT

from . import recipes
from . import jobs as _jobs          # noqa: F401  (registers the jobs)
from . import jobs_levels as _jl     # noqa: F401
from . import sidejobs as _sj        # noqa: F401
from .recipes import JOBS, make
from .variety import distance

MIN_APART = 0.3                      # the least distance (variety.distance) between two recipe rooms of one job


def load(path):
    with open(path, "rb") as f:
        return tomllib.load(f)


def theme_named(name):
    return importlib.import_module(f".themes.{name}", __package__).THEME


STEP_TAG = {"fight": "fight", "hub": "hub", "lever": "lever", "gate": "gate", "timing": "timing", "drop": "drop", "plate": "plate",
            "guardian": "guardian"}


def build(spec):
    theme = theme_named(spec["theme"])
    seed = spec.get("seed", 1)
    recipes.DIFFICULTY = spec.get("difficulty", "normal")
    fields = dict(environment=spec.get("environment", "Caves"), minRooms=spec.get("rooms", [16, 28])[0],
                  maxRooms=spec.get("rooms", [16, 28])[1], maxSize=spec.get("max_size", [320, 140, 320]),
                  minUses=spec.get("min_uses", 0), entranceLook=spec.get("entrance_look", theme.name), doors={}, doorChance=0.0,
                  parkour=spec.get("parkour", False), creatures=spec.get("creatures", CREATURES), loot=spec.get("loot", LOOT))
    if spec.get("music", theme.music):
        fields["music"] = spec.get("music", theme.music)
    if "shortcut" in spec:
        sc = spec["shortcut"]
        fields["shortcut"] = {"near": sc["near"], "far": sc["far"], "farStep": sc["far_step"]}
    pack = Pack(spec["name"], spec.get("title", spec["name"]), **fields)
    sig = theme.signatures()
    infos, used_sig = [], []
    made = set()

    def add_sig(name):
        if name in made:
            return
        if name not in sig:
            raise SystemExit(f"{spec['name']}: theme {theme.name} has no signature room {name}")
        pack.room(sig[name]())
        made.add(name)
        used_sig.append(name)

    counts, tried = {}, {}

    def shells_for(jb):
        """The shells this job can have in this theme."""
        job = JOBS[jb]
        if hasattr(job, "make_shell"):
            return {"own"}
        shapes = theme.reshape.get(jb, {}).get("shapes") or job.shapes
        return {sh for sh in shapes if theme.shells.get(sh, 0) > 0}

    def add_recipe(jb, n, salt):
        got, s, last = 0, 0, None
        while got < n and s < n * 10 + 10:
            s += 1
            try:
                vi = tried[jb] = tried.get(jb, -1) + 1          # (one index per attempt: the rooms made follow one another)
                room, info = make(jb, theme, f"{seed}/{salt}/{s}", vi=vi)
            except RuntimeError as e:
                last = e
                continue
            # two rooms of one job must look different: a room too like one already made is passed over while there are tries left
            like = [distance(info, i) for i in infos if i["recipe"] == jb]
            if like and min(like) < MIN_APART and s < n * 10:
                last = RuntimeError(f"{jb}: too like a room already made ({min(like):.2f})")
                continue
            # and in another shell while the job has shells it hasn't used (one recipe in one shell twice reads as the same room)
            used = {i["shell"] for i in infos if i["recipe"] == jb}
            if info["shell"] in used and len(used) < len(shells_for(jb)) and s < n * 8:
                last = RuntimeError(f"{jb}: a {info['shell']} room again")
                continue
            room.name = f"{jb}_{info['shell']}_{len(infos) + 1}"
            info["name"] = room.name
            pack.room(room)
            infos.append(info)
            got += 1
            counts[jb] = counts.get(jb, 0) + 1
        if got < n:
            raise SystemExit(f"{spec['name']}: only {got} of {n} {jb} rooms could be made ({last})")

    variants = spec.get("variants", 1)
    for st in spec["plan"]:
        if "job" in st:
            jb = st["job"]
            if jb in theme.forbid:
                raise SystemExit(f"{spec['name']}: theme {theme.name} has no {jb} rooms")
            add_recipe(jb, st.get("variants", variants), st["id"])
            tag = JOBS[jb].tags[0] if JOBS[jb].tags else jb
        else:
            names = st.get("rooms") or [st["room"]]
            for n in names:
                add_sig(n)
            tag = st.get("tag") or _sig_tag(pack, names[0])
        pack.step(tag, st["id"], opens_with=st.get("opens_with"), from_=st.get("from"))
    for jb, n in spec.get("fill", {}).items():
        add_recipe(jb, n, f"fill/{jb}")
    for n in spec.get("signatures", []):
        add_sig(n)
    from .variety import report
    return pack, report(spec, theme, infos, used_sig)


def _sig_tag(pack, name):
    room = next(r for r in pack.rooms if getattr(r, "name", None) == name)
    return room.tags[0] if room.tags else name
