"""
The game's shapes for the dungeon previews: its dungeon rooms (DungeonDB's prefabs, in the SoftRef bundles) and any prefab from the main
bundle (build pieces, props, chests, doors), read from the game's own files with UnityPy (no game needed). Each becomes a small .npz
(triangles in the prefab's own frame, a colour per triangle from its texture, and its bounds) in a cache folder on this PC.

The shapes are the game's assets: keep them on this PC, never commit or share them. The cache is DUNGEON_MESHES, or
%TEMP%/quaddungeon_meshes. Building pieces already exported by tools/blueprints/tools/extract_meshes.py (<blueprints>/_meshes) are used as
they are.

    python game_meshes.py dvergr_room01 dvergrprops_lantern ...     export these (rooms or prefabs), once each
    python game_meshes.py --list dvergr                              prefab and room names containing a text

    from game_meshes import shape
    v, f, c, lo, hi = shape("dvergr_room01")                         exports it the first time it's asked for
"""
import os, re, sys, tempfile
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
BLUEPRINT_TOOLS = os.path.join(HERE, "..", "blueprints", "tools")
sys.path.insert(0, BLUEPRINT_TOOLS)

GAME = os.environ.get("VALHEIM_DIR", r"D:\SteamLibrary\steamapps\common\Valheim")
SOFTREF = os.path.join(GAME, "valheim_Data", "StreamingAssets", "SoftRef")
CACHE = os.environ.get("DUNGEON_MESHES") or os.path.join(tempfile.gettempdir(), "quaddungeon_meshes")
_manifest = None
_main = None
_loaded = {}


def _read_manifest():
    """SoftRef/manifest: bundle -> its dependencies, and prefab name -> (bundle, path in bundle)."""
    global _manifest
    if _manifest is None:
        deps, assets, cur = {}, {}, None
        lines = open(os.path.join(SOFTREF, "manifest"), encoding="utf-8-sig").read().splitlines()
        for n, line in enumerate(lines):
            m = re.match(r"- bundle: (\w+)", line)
            if m:
                cur = m.group(1)
                deps[cur] = []
                continue
            m = re.match(r"  - (\w+)$", line)
            if m and cur:
                deps[cur].append(m.group(1))
                continue
            m = re.match(r"  path in bundle: (.*)", line)
            if m and n > 0:
                b = re.match(r"  bundle: (\w+)", lines[n - 1])
                if b and m.group(1).lower().endswith(".prefab"):
                    assets[os.path.basename(m.group(1))[:-7]] = (b.group(1), m.group(1))
        _manifest = deps, assets
    return _manifest


def _main_bundle():
    """The bundle that holds ZNetScene's prefabs (pieces, props, items): extract_pieces finds it."""
    global _main
    if _main is None:
        import UnityPy
        from extract_pieces import BUNDLE
        env = UnityPy.load(BUNDLE)
        _main = {os.path.basename(k)[:-7]: v for k, v in env.container.items() if k.lower().endswith(".prefab")}
    return _main


def _colour(mat, cache={}):
    key = getattr(mat, "m_Name", "")
    if key in cache:
        return cache[key]
    colour = np.array([0.6, 0.6, 0.6])
    try:
        props = mat.m_SavedProperties
        for name, env in props.m_TexEnvs:
            if name == "_MainTex" and env.m_Texture and env.m_Texture.path_id:
                img = env.m_Texture.read().image.convert("RGBA").resize((16, 16))
                px = np.asarray(img, dtype=np.float32) / 255.0
                a = px[..., 3:4]
                if a.sum() > 0.1:
                    colour = (px[..., :3] * a).sum(axis=(0, 1)) / a.sum()
                break
        for name, col in props.m_Colors:
            if name == "_Color":
                colour = colour * np.array([col.r, col.g, col.b])
                break
    except Exception:
        pass
    cache[key] = colour
    return colour


SKIP = ("collider", "destruction", "destroyed", "_frac", "particles", "mist", "watercube", "watervolume", "fx_", "vfx_", "sfx_",
        "spawner", "creep", "eggs", "snow", "_ghost", "placement")


def _export(root):
    """Every visible mesh under root (LOD 0 only), in root's frame."""
    from extract_meshes import mesh_triangles
    from extract_pieces import components, local_matrix
    tr = next((x for x in components(root) if x.__class__.__name__ == "Transform"), None)
    vs, fs, cs = [], [], []

    def visit(t, parent, depth):
        go = t.m_GameObject.read()
        name = go.m_Name.lower()
        if not go.m_IsActive or (depth > 0 and any(s in name for s in SKIP)):
            return
        if re.search(r"lod_?[1-9]", name):
            return
        m = parent @ local_matrix(t) if depth > 0 else np.eye(4)
        comps = list(components(go))
        for cmp in comps:
            if cmp.__class__.__name__ != "MeshFilter" or not getattr(cmp, "m_Mesh", None) or not cmp.m_Mesh.path_id:
                continue
            rend = next((r for r in comps if r.__class__.__name__ == "MeshRenderer"), None)
            if rend is None or not rend.m_Enabled:
                continue
            try:
                v, groups = mesh_triangles(cmp.m_Mesh.read())
            except Exception:
                continue
            if not len(v) or not groups:
                continue
            world = (m @ np.c_[v, np.ones(len(v))].T).T[:, :3]
            mats = []
            for ptr in rend.m_Materials:
                try:
                    mats.append(_colour(ptr.read()) if ptr.path_id else np.array([0.6, 0.6, 0.6]))
                except Exception:
                    mats.append(np.array([0.6, 0.6, 0.6]))
            base = sum(len(x) for x in vs)
            for gi, faces in enumerate(groups):
                col = mats[min(gi, len(mats) - 1)] if mats else np.array([0.6, 0.6, 0.6])
                for f in faces:
                    fs.append([base + f[0], base + f[1], base + f[2]])
                    cs.append(col)
            vs.append(world)
        for ch in t.m_Children:
            try:
                visit(ch.read(), m, depth + 1)
            except Exception:
                continue

    visit(tr, np.eye(4), 0)
    if not vs:
        return None
    return np.vstack(vs).astype(np.float32), np.array(fs, dtype=np.int32), (np.array(cs) * 255).clip(0, 255).astype(np.uint8)


def export(name):
    """Exports one room or prefab into the cache. Returns the file, or None if the game has no such thing."""
    os.makedirs(CACHE, exist_ok=True)
    out = os.path.join(CACHE, name + ".npz")
    deps, assets = _read_manifest()
    root = None
    if name in assets:
        import UnityPy
        bundle, path = assets[name]
        if bundle not in _loaded:
            _loaded[bundle] = UnityPy.load(*[os.path.join(SOFTREF, "Bundles", b) for b in [bundle] + deps.get(bundle, [])])
        env = _loaded[bundle]
        root = next((v for k, v in env.container.items() if k.lower() == path.lower()), None)
    elif name in _main_bundle():
        root = _main_bundle()[name]
    if root is None:
        return None
    result = _export(root.read())
    if result is None:
        v, f, c = np.zeros((0, 3), np.float32), np.zeros((0, 3), np.int32), np.zeros((0, 3), np.uint8)
    else:
        v, f, c = result
    np.savez_compressed(out, v=v, f=f, c=c)
    return out


def shape(name):
    """(vertices, faces, colours, min, max) of a room, piece or prefab in its own frame; exported the first time. None if unknown."""
    for path in (os.path.join(CACHE, name + ".npz"), os.path.join(BLUEPRINT_TOOLS, "..", "_meshes", name + ".npz")):
        if os.path.exists(path):
            break
    else:
        try:
            from blueprint import blueprints_dir
            path = os.path.join(blueprints_dir(), "_meshes", name + ".npz")
        except Exception:
            path = ""
        if not os.path.exists(path):
            path = export(name)
            if path is None:
                return None
    d = np.load(path)
    v = d["v"]
    lo, hi = (v.min(0), v.max(0)) if len(v) else (np.zeros(3), np.zeros(3))
    return v, d["f"], d["c"], lo, hi


if __name__ == "__main__":
    args = sys.argv[1:]
    if args and args[0] == "--list":
        text = args[1].lower() if len(args) > 1 else ""
        names = set(_read_manifest()[1]) | set(_main_bundle())
        print("\n".join(sorted(n for n in names if text in n.lower())))
    else:
        for name in args:
            s = shape(name)
            print(name, "unknown" if s is None else f"{len(s[1])} triangles, bounds {np.round(s[3], 2)} .. {np.round(s[4], 2)}")
