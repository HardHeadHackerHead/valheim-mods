"""
Reads the size, snap points, solid parts and settings of every building piece straight from Valheim's asset files (no game running needed)
and writes BepInEx/blueprints/_pieces.json, the same list BuildOrders writes from inside the game. Needs: pip install UnityPy

Per piece: p (prefab name), title, category, material, supports, groundOnly, cost, min/max (the visible box, metres from the piece's origin),
snaps (named connection points, e.g. "top 1", "bottom 2", "corner 3": pieces join where these meet), ascend (for stairs, roofs and ladders: the
direction from the low snaps to the high ones), and boxes (the solid parts: centre, size and turn of each, for drawing the real shape).
The game's own list, written the first time you play with BuildOrders 1.5+, replaces this one and is the final word.
"""
import json, math, os, sys
import numpy as np
import UnityPy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from blueprint import blueprints_dir  # noqa: E402


def _game_data():
    """valheim_Data: VALHEIM_DIR if set, else the game folder this blueprints folder sits in (<Valheim>/BepInEx/blueprints)."""
    root = os.environ.get("VALHEIM_DIR") or os.path.dirname(os.path.dirname(os.path.abspath(blueprints_dir())))
    return os.path.join(root, "valheim_Data")


def _find_bundle():
    """The asset bundle that holds the building pieces. Its file name is a hash that a game update can change: look for it if needed."""
    folder = os.path.join(GAME, "StreamingAssets", "SoftRef", "Bundles")
    known = os.path.join(folder, "c4210710")
    if os.environ.get("VALHEIM_BUNDLE"):
        return os.environ["VALHEIM_BUNDLE"]
    if os.path.exists(known) or not os.path.isdir(folder):
        return known
    print("Looking for the building pieces among the game's asset bundles (once, may take a few minutes)...")
    for name in sorted(os.listdir(folder), key=lambda n: -os.path.getsize(os.path.join(folder, n))):
        try:
            if any(p.lower().startswith("assets/gameelements/pieces/") for p in UnityPy.load(os.path.join(folder, name)).container):
                print("Found it:", name, "(set VALHEIM_BUNDLE to skip this search)")
                return os.path.join(folder, name)
        except Exception:
            continue
    return known


GAME = _game_data()
BUNDLE = _find_bundle()
OUT = os.environ.get("PIECES_OUT", os.path.join(blueprints_dir(), "_pieces.json"))
MATERIALS = ["Wood", "Stone", "Iron", "HardWood", "Marble", "Ashstone", "Ancient", "Ice", "Timberwood"]
CATEGORIES = ["Misc", "Crafting", "BuildingWorkbench", "BuildingStonecutter", "Furniture", "DeepNorth", "Feasts", "Food", "Meads"]
SNAP_PREFIX = "$hud_snappoint_"


def c(o, name):
    """A component of a vector or quaternion, whatever case this version of UnityPy spells it in."""
    return getattr(o, name.lower(), getattr(o, name.upper(), None))


def vec(v):
    return [c(v, "x"), c(v, "y"), c(v, "z")]


def quat_matrix(q):
    x, y, z, w = c(q, "x"), c(q, "y"), c(q, "z"), c(q, "w")
    return np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])


def local_matrix(t):
    m = np.eye(4)
    m[:3, :3] = quat_matrix(t.m_LocalRotation) @ np.diag(vec(t.m_LocalScale))
    m[:3, 3] = vec(t.m_LocalPosition)
    return m


def matrix_to_quat(r):
    """Rotation matrix -> (x, y, z, w)."""
    t = r[0, 0] + r[1, 1] + r[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        return [(r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s, (r[1, 0] - r[0, 1]) / s, 0.25 * s]
    if r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = math.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2
        return [0.25 * s, (r[0, 1] + r[1, 0]) / s, (r[0, 2] + r[2, 0]) / s, (r[2, 1] - r[1, 2]) / s]
    if r[1, 1] > r[2, 2]:
        s = math.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2
        return [(r[0, 1] + r[1, 0]) / s, 0.25 * s, (r[1, 2] + r[2, 1]) / s, (r[0, 2] - r[2, 0]) / s]
    s = math.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2
    return [(r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s, 0.25 * s, (r[1, 0] - r[0, 1]) / s]


def main():
    env = UnityPy.load(BUNDLE)
    out = []
    for path, asset in env.container.items():
        low = path.lower()
        if not low.startswith("assets/gameelements/pieces/") or not low.endswith(".prefab") or "/effects/" in low:
            continue
        try:
            root = asset.read()
        except Exception:
            continue
        if not hasattr(root, "m_Component"):
            continue
        entry = describe(root)
        if entry:
            out.append(entry)

    out.sort(key=lambda e: e["p"])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        stations = {e["p"]: e["isStation"] for e in out if e.get("isStation")}
        json.dump({"dump": "offline", "note": "Read from the game's asset files. Sizes are in metres from each piece's own origin.",
                   "stations": stations, "pieces": out}, f, indent=1)
    print(len(out), "pieces ->", OUT)


def costs(piece):
    """What the piece costs, as [item, amount] pairs, when the item prefabs can be read."""
    out = []
    try:
        for req in piece.m_resources:
            item = req.m_resItem.read()
            name = item.m_GameObject.read().m_Name if hasattr(item, "m_GameObject") else getattr(item, "m_Name", "")
            out.append([name, int(req.m_amount)])
    except Exception:
        return out
    return out


BIOMES = {1: "Meadows", 2: "Swamp", 4: "Mountain", 8: "BlackForest", 16: "Plains", 32: "AshLands", 64: "DeepNorth", 256: "Ocean", 512: "Mistlands"}
RULE_FLAGS = ["m_groundPiece", "m_waterPiece", "m_noInWater", "m_notOnWood", "m_notOnTiltingSurface", "m_inCeilingOnly", "m_notOnFloor",
              "m_onlyInTeleportArea", "m_allowedInDungeons", "m_clipEverything", "m_clipGround", "m_noClipping", "m_mustBeAboveConnected",
              "m_vegetationGroundOnly", "m_cultivatedGroundOnly", "m_isUpgrade"]


def station_name(piece):
    """The crafting station that must be near for this piece to be built (its prefab name), or ""."""
    try:
        st = piece.m_craftingStation
        if not st or not st.path_id:
            return ""
        return st.read().m_GameObject.read().m_Name
    except Exception:
        return ""


def rules(piece):
    """The placement rules that are switched on for a piece (only the true ones, to keep the file small)."""
    out = {}
    for flag in RULE_FLAGS:
        if bool(getattr(piece, flag, False)):
            out[flag[2].lower() + flag[3:]] = True
    biome = int(getattr(piece, "m_onlyInBiome", 0) or 0)
    if biome:
        out["onlyInBiome"] = [name for bit, name in BIOMES.items() if biome & bit] or [biome]
    space = float(getattr(piece, "m_spaceRequirement", 0) or 0)
    if space > 0:
        out["spaceRequirement"] = space
    comfort = int(getattr(piece, "m_comfort", 0) or 0)
    if comfort:
        out["comfort"] = comfort
    try:
        if piece.m_mustConnectTo and piece.m_mustConnectTo.path_id:
            out["mustConnectTo"] = piece.m_mustConnectTo.read().m_GameObject.read().m_Name
    except Exception:
        pass
    return out


def components(go):
    for cmp in go.m_Component:
        ptr = cmp.component if hasattr(cmp, "component") else cmp
        try:
            yield ptr.read()
        except Exception:
            continue


def describe(root):
    transform = next((x for x in components(root) if x.__class__.__name__ == "Transform"), None)
    if transform is None:
        return None
    lo = np.array([1e9] * 3)
    hi = np.array([-1e9] * 3)
    snaps = {}
    boxes = []
    info = {"material": "", "supports": None, "category": "", "title": "", "groundOnly": False, "cost": [], "com": None,
            "station": "", "rules": {}, "health": None, "stationInfo": None}
    seen = False

    def visit(t, parent_m, depth):
        nonlocal lo, hi, seen
        go = t.m_GameObject.read()
        m = parent_m @ local_matrix(t) if depth > 0 else np.eye(4)

        # snap points are switched-off empty objects, so they are read before the active check
        if go.m_Name.startswith(SNAP_PREFIX):
            snaps[go.m_Name[len(SNAP_PREFIX):]] = [round(float(v), 3) for v in m[:3, 3]]
        if not go.m_IsActive:
            return

        for cmp in components(go):
            kind = cmp.__class__.__name__
            if kind == "MeshFilter" and getattr(cmp, "m_Mesh", None) and cmp.m_Mesh.path_id:
                renderer = next((r for r in components(go) if r.__class__.__name__ == "MeshRenderer"), None)
                if renderer is None or not renderer.m_Enabled or "destroy" in go.m_Name.lower():
                    continue
                try:
                    mesh = cmp.m_Mesh.read()
                except Exception:
                    continue  # a built-in Unity shape stored outside this bundle
                box = mesh.m_LocalAABB
                ctr = np.array(vec(box.m_Center))
                ext = np.array(vec(box.m_Extent))
                for sx in (-1, 1):
                    for sy in (-1, 1):
                        for sz in (-1, 1):
                            p = m @ np.append(ctr + ext * np.array([sx, sy, sz]), 1.0)
                            lo = np.minimum(lo, p[:3]); hi = np.maximum(hi, p[:3]); seen = True
            elif kind == "BoxCollider" and depth > 0 and not getattr(cmp, "m_IsTrigger", False):
                # the solid parts: a box with its own turn (stairs are made of tilted boxes)
                size = np.array(vec(cmp.m_Size)); centre = np.array(vec(cmp.m_Center))
                lin = m[:3, :3]
                scale = np.linalg.norm(lin, axis=0)
                rot = lin / np.where(scale == 0, 1, scale)
                p = m @ np.append(centre, 1.0)
                boxes.append({"c": [round(float(v), 3) for v in p[:3]], "s": [round(float(v), 3) for v in size * scale],
                              "r": [round(float(v), 4) for v in matrix_to_quat(rot)]})
            elif kind == "MonoBehaviour" and depth == 0:
                if hasattr(cmp, "m_materialType"):
                    mt = int(cmp.m_materialType)
                    info["material"] = MATERIALS[mt] if 0 <= mt < len(MATERIALS) else str(mt)
                    info["supports"] = bool(cmp.m_supports)
                    info["com"] = vec(cmp.m_comOffset) if hasattr(cmp, "m_comOffset") and cmp.m_comOffset is not None else None
                if hasattr(cmp, "m_category") and hasattr(cmp, "m_canBeRemoved"):
                    cat = int(cmp.m_category)
                    info["category"] = CATEGORIES[cat] if 0 <= cat < len(CATEGORIES) else str(cat)
                    info["title"] = str(getattr(cmp, "m_name", ""))
                    info["groundOnly"] = bool(getattr(cmp, "m_groundOnly", False) or getattr(cmp, "m_groundPiece", False))
                    info["cost"] = costs(cmp)
                    info["station"] = station_name(cmp)
                    info["rules"] = rules(cmp)
                if hasattr(cmp, "m_rangeBuild"):
                    info["stationInfo"] = {"range": float(cmp.m_rangeBuild), "extraRangePerLevel": float(getattr(cmp, "m_extraRangePerLevel", 0)),
                                           "needsRoof": bool(getattr(cmp, "m_craftRequireRoof", False)), "needsFire": bool(getattr(cmp, "m_craftRequireFire", False))}
                if hasattr(cmp, "m_health") and hasattr(cmp, "m_materialType"):
                    info["health"] = float(cmp.m_health)

        for child in t.m_Children:
            visit(child.read(), m, depth + 1)

    visit(transform, np.eye(4), 0)
    if not seen:
        return None
    r = lambda v: [round(float(x), 3) for x in v]
    entry = {"p": root.m_Name, "title": info["title"], "category": info["category"], "material": info["material"], "supports": info["supports"],
             "groundOnly": info["groundOnly"], "min": r(lo), "max": r(hi), "snaps": snaps}
    if boxes:
        entry["boxes"] = boxes[:24]
    # the way a stair, ramp or roof climbs: from the middle of its "bottom" snaps to the middle of its "top" snaps
    tops = [v for k, v in snaps.items() if k.startswith("top")]
    bottoms = [v for k, v in snaps.items() if k.startswith("bottom")]
    if tops and bottoms:
        entry["ascend"] = r(np.mean(tops, axis=0) - np.mean(bottoms, axis=0))
    if info["com"] and any(abs(v) > 1e-6 for v in info["com"]):
        entry["comOffset"] = r(info["com"])
    if info["cost"]:
        entry["cost"] = info["cost"]
    if info["station"]:
        entry["station"] = info["station"]
    if info["rules"]:
        entry["rules"] = info["rules"]
    if info["health"] is not None:
        entry["health"] = info["health"]
    if info["stationInfo"]:
        entry["isStation"] = info["stationInfo"]
    return entry


if __name__ == "__main__":
    main()
