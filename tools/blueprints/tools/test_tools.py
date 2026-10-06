"""
Checks for the blueprint tools: run  python test_tools.py  (needs _pieces.json; the preview and mesh checks also need numpy, Pillow and _meshes).
Each check prints PASS or FAIL; the script exits non-zero if any failed.
"""
import json, math, os, random, sys, tempfile, traceback

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from blueprint import Pieces, Blueprint, rotation, world_box, _apply, blueprints_dir  # noqa: E402
from stability import analyze  # noqa: E402

P = Pieces()
results = []


def check(name):
    def wrap(fn):
        try:
            fn()
            results.append((name, True, ""))
        except Exception as e:
            results.append((name, False, f"{e.__class__.__name__}: {e}\n{traceback.format_exc(limit=2)}"))
        return fn
    return wrap


def close(a, b, tol=1e-3):
    return all(abs(x - y) <= tol for x, y in zip(a, b))


@check("key pieces exist with named snap points")
def _():
    for name in ["woodwall", "wood_floor", "wood_stair", "wood_roof", "wood_roof_top", "stake_wall", "stone_wall_4x2", "wood_pole2", "wood_beam"]:
        assert P.has(name), name
        assert P.snaps(name), f"{name} has no snaps"
    assert close(P.by_name["wood_stair"]["ascend"], [0, 1, -2])


@check("yaw_for turns climbing pieces towards any direction")
def _():
    bp = Blueprint("t", P)
    for name in ["wood_stair", "wood_roof", "wood_stepladder", "wood_beam_26"]:
        ax, az, _ = P.ascend(name)
        for d in [(0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-2, 1)]:
            yaw = bp.yaw_for(name, *d)
            v = _apply(rotation(0, yaw, 0), [ax, 0, az])
            n1, n2 = math.hypot(v[0], v[2]), math.hypot(*d)
            assert abs(v[0] / n1 - d[0] / n2) < 1e-6 and abs(v[2] / n1 - d[1] / n2) < 1e-6, (name, d, yaw, v)


@check("Unity rotation convention: yaw 90 turns +z to +x")
def _():
    assert close(_apply(rotation(0, 90, 0), [0, 0, 1]), [1, 0, 0])
    assert close(_apply(rotation(90, 0, 0), [0, 1, 0]), [0, 0, 1])   # pitch 90: up turns to forward, as Quaternion.Euler(90,0,0) does


@check("place_snap puts the snap point exactly on the target, at any turn")
def _():
    bp = Blueprint("t", P)
    rnd = random.Random(3)
    for _ in range(200):
        name = rnd.choice(["wood_stair", "wood_roof", "woodwall", "wood_beam_45", "stone_wall_4x2"])
        snap = rnd.choice(list(P.snaps(name)))
        point = (rnd.uniform(-20, 20), rnd.uniform(0, 10), rnd.uniform(-20, 20))
        item = bp.place_snap(name, snap, point, yaw=rnd.uniform(0, 360), rx=rnd.choice([0, 0, 30, -45]), rz=rnd.choice([0, 0, 15]))
        assert close(bp.snap_world(item, snap), point, 2e-3), (name, snap)


@check("a chain of stairs is continuous and reaches the platform")
def _():
    bp = Blueprint("t", P)
    yaw = bp.yaw_for("wood_stair", 0, 1)
    stairs = [bp.place_mid("wood_stair", ("top 1", "top 2"), (0.0, 1.0 + k, 2.0 * k + 2.0), yaw=yaw) for k in range(3)]
    for lower, upper in zip(stairs, stairs[1:]):
        top = [(a + b) / 2 for a, b in zip(bp.snap_world(lower, "top 1"), bp.snap_world(lower, "top 2"))]
        bottom = [(a + b) / 2 for a, b in zip(bp.snap_world(upper, "bottom 1"), bp.snap_world(upper, "bottom 2"))]
        assert close(top, bottom), (top, bottom)
    first_bottom = [(a + b) / 2 for a, b in zip(bp.snap_world(stairs[0], "bottom 1"), bp.snap_world(stairs[0], "bottom 2"))]
    assert abs(first_bottom[1]) < 1e-6, "the first stair starts on the ground"
    last_top = [(a + b) / 2 for a, b in zip(bp.snap_world(stairs[-1], "top 1"), bp.snap_world(stairs[-1], "top 2"))]
    assert close(last_top, (0.0, 3.0, 6.0)), last_top


@check("hall roof meets the walls and the ridge cap meets both slopes")
def _():
    bp = Blueprint("t", P)
    wall = bp.raw("woodwall", 0, 1.1, 2.5)
    roof = bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (0, 2.1, 2.5), yaw=bp.yaw_for("wood_roof", 0, 1))
    other = bp.place_mid("wood_roof", ("bottom 1", "bottom 2"), (0, 2.1, 8.5), yaw=bp.yaw_for("wood_roof", 0, -1))
    ridge = bp.place_mid("wood_roof_top", ("bottom 1", "bottom 2", "bottom 3", "bottom 4"), (0, 3.1, 5.5))
    wall_tops = sorted([bp.snap_world(wall, "top 1"), bp.snap_world(wall, "top 2")])
    roof_bottoms = sorted([bp.snap_world(roof, "bottom 1"), bp.snap_world(roof, "bottom 2")])
    assert all(close(a, b) for a, b in zip(wall_tops, roof_bottoms)), (wall_tops, roof_bottoms)
    tops = sorted([bp.snap_world(roof, "top 1"), bp.snap_world(roof, "top 2"), bp.snap_world(other, "top 1"), bp.snap_world(other, "top 2")])
    ridge_b = sorted([bp.snap_world(ridge, k) for k in ("bottom 1", "bottom 2", "bottom 3", "bottom 4")])
    assert all(close(a, b) for a, b in zip(tops, ridge_b)), (tops, ridge_b)


@check("world_box swaps width and depth when a piece is turned 90 degrees")
def _():
    item = {"p": "stone_wall_4x2", "x": 0, "y": 0, "z": 0, "ry": 90}
    lo, hi = world_box(item, P.by_name["stone_wall_4x2"])
    assert hi[2] - lo[2] > 4.0 and hi[0] - lo[0] < 1.5, (lo, hi)


@check("stability: wood and stone towers fall above about 16 m, iron stands, a bridged beam beats a cantilever")
def _():
    def tower(piece, n):
        bp = Blueprint("t", P)
        for k in range(n):
            bp.place(piece, 0, 0, y=2.0 * k, height=2.0)
        return analyze(bp.items, P)
    wood = tower("wood_pole2", 14)
    first_fall = min((r["index"] for r in wood if not r["ok"]), default=None)
    assert first_fall is not None and 6 <= first_fall <= 10, first_fall
    assert all(r["ok"] for r in tower("iron_wall_2x2", 14))
    # a beam held at one end vs. a beam held at both ends
    one = Blueprint("t", P); one.place("wood_pole2", -1.2, 0, height=2.0); one.raw("wood_beam", 0, 2.2, 0)
    two = Blueprint("t", P); two.place("wood_pole2", -1.2, 0, height=2.0); two.place("wood_pole2", 1.2, 0, height=2.0); two.raw("wood_beam", 0, 2.2, 0)
    s1 = [r for r in analyze(one.items, P) if r["p"] == "wood_beam"][0]["support"]
    s2 = [r for r in analyze(two.items, P) if r["p"] == "wood_beam"][0]["support"]
    assert s2 > s1, (s1, s2)


def run_design(script, expect_file):
    env = dict(os.environ, BLUEPRINT_OUT=os.path.join(tempfile.gettempdir(), expect_file))
    import subprocess
    out = subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), script)], capture_output=True, text=True, env=env)
    assert out.returncode == 0, out.stdout[-2000:] + out.stderr[-2000:]
    return json.load(open(env["BLUEPRINT_OUT"], encoding="utf-8")), out.stdout


@check("the wooden fort builds, stands and passes the checks")
def _():
    doc, out = run_design("wood_fort.py", "test_wood_fort.json")
    assert "0 would fall" in out, out[-800:]
    bp = Blueprint("t", P); bp.items = doc["pieces"]
    c = bp.check()
    assert not c["problems"], c["problems"][:5]


@check("the stone keep builds, stands and passes the checks")
def _():
    doc, out = run_design("fort.py", "test_stone_keep.json")
    assert "0 would fall" in out, out[-800:]
    bp = Blueprint("t", P); bp.items = doc["pieces"]
    c = bp.check()
    assert not c["problems"], c["problems"][:5]


@check("importer maths: a blueprint turned to face the player keeps its gate towards them")
def _():
    # BuildOrders places local (x, z) at anchor + Quaternion.Euler(0, yaw, 0) * (x, 0, z); the player faces +z at yaw 0
    for yaw in (0, 90, 180, 270, 37):
        gate = _apply(rotation(0, yaw, 0), [0, 0, -10])                  # the gate is at local -z
        facing = _apply(rotation(0, yaw, 0), [0, 0, 1])                  # the way the player looks
        assert gate[0] * facing[0] + gate[2] * facing[2] < -9.99         # the gate is on the player's side


@check("check() catches a fire on a wooden floor and a piece that must touch the ground")
def _():
    bp = Blueprint("t", P)
    bp.raw("wood_floor", 0, 0.5, 0)
    bp.place("fire_pit", 0, 0, y=0.58)
    c = bp.check()
    assert any("cannot stand on wood" in p for p in c["problems"]), c["problems"]
    bp2 = Blueprint("t", P)
    bp2.place("piece_sharpstakes", 0, 0, y=1.0)
    ground_only = [n for n, p in P.by_name.items() if p.get("groundOnly")]
    if ground_only:
        bp3 = Blueprint("t", P); bp3.place(ground_only[0], 0, 0, y=2.0)
        assert any("must stand on the ground" in p for p in bp3.check()["problems"])


@check("shapes exported from the game match the measured sizes of the main pieces")
def _():
    import numpy as np
    folder = os.path.join(blueprints_dir(), "_meshes")
    if not os.path.isdir(folder):
        raise AssertionError("no _meshes folder (run extract_meshes.py)")
    for name in ["woodwall", "wood_floor", "wood_roof", "stone_wall_4x2", "stake_wall", "wood_stair", "wood_gate"]:
        z = np.load(os.path.join(folder, name + ".npz"))
        err = abs(z["v"].min(0) - np.array(P.by_name[name]["min"])).max() + abs(z["v"].max(0) - np.array(P.by_name[name]["max"])).max()
        assert err < 0.05, (name, err)


@check("preview draws a small design without errors")
def _():
    from preview import render
    bp = Blueprint("t", P)
    bp.raw("woodwall", 0, 1, 0)
    bp.place_mid("wood_stair", ("bottom 1", "bottom 2"), (0, 0, -2), yaw=bp.yaw_for("wood_stair", 0, -1))
    path = os.path.join(tempfile.gettempdir(), "test_preview.png")
    render(bp.items, P, path, views=((30, 25),), size=(160, 120))
    assert os.path.getsize(path) > 1000


if __name__ == "__main__":
    failed = 0
    for name, ok, info in results:
        print(("PASS  " if ok else "FAIL  ") + name)
        if not ok:
            failed += 1
            print("      " + info.replace("\n", "\n      "))
    print(f"\n{len(results) - failed} passed, {failed} failed")
    sys.exit(1 if failed else 0)
