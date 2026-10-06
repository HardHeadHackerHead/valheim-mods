"""
Exports the 3D shape of every building piece from Valheim's asset files, so previews show pieces as they really look (planks, stakes,
roof straw) instead of boxes. Writes one small file per piece into <blueprints>/_meshes/ (about 10 MB in all). Needs: pip install UnityPy

Each file holds the piece's triangles in its own coordinates (metres from its origin, like _pieces.json) and a colour per triangle, taken from
the average colour of the texture the game paints it with.
"""
import io, json, os, sys
import numpy as np
import UnityPy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from extract_pieces import BUNDLE, components, local_matrix, vec  # noqa: E402
from blueprint import blueprints_dir  # noqa: E402

OUT = os.path.join(os.environ.get("BLUEPRINTS_DIR") or blueprints_dir(), "_meshes")
_colour_cache = {}


def material_colour(mat):
    """The average colour of a material's main texture, times its colour tint (falls back to a mid grey)."""
    key = getattr(mat, "m_Name", "")
    if key in _colour_cache:
        return _colour_cache[key]
    colour = np.array([0.6, 0.6, 0.6])
    try:
        props = mat.m_SavedProperties
        for name, env in props.m_TexEnvs:
            if name == "_MainTex" and env.m_Texture and env.m_Texture.path_id:
                img = env.m_Texture.read().image.convert("RGBA").resize((16, 16))
                px = np.asarray(img, dtype=np.float32) / 255.0
                alpha = px[..., 3:4]
                if alpha.sum() > 0.1:
                    colour = (px[..., :3] * alpha).sum(axis=(0, 1)) / alpha.sum()
                break
        for name, col in props.m_Colors:
            if name == "_Color":
                colour = colour * np.array([col.r, col.g, col.b])
                break
    except Exception:
        pass
    _colour_cache[key] = colour
    return colour


def mesh_triangles(mesh):
    """Vertices and per-submesh face lists from UnityPy's OBJ export (which mirrors x; undone here)."""
    obj = mesh.export()
    verts, groups, current = [], [], None
    for line in obj.splitlines():
        if line.startswith("v "):
            verts.append([float(t) for t in line.split()[1:4]])
        elif line.startswith("g "):
            current = []
            groups.append(current)
        elif line.startswith("f ") and current is not None:
            current.append([int(t.split("/")[0]) - 1 for t in line.split()[1:4]])
    v = np.array(verts, dtype=np.float64)
    if len(v):
        # UnityPy's OBJ export always mirrors x (OBJ is right-handed); checked against the meshes' own bounds on 1500 meshes: never otherwise
        v[:, 0] = -v[:, 0]
    groups = [g for g in groups if g] or []
    # the first group line is the mesh name with no faces of its own; the rest are submeshes in material order
    return v, groups


def c_x(v):
    return getattr(v, "x", getattr(v, "X", 0.0))


def export_piece(root):
    transform = next((x for x in components(root) if x.__class__.__name__ == "Transform"), None)
    if transform is None:
        return None
    all_v, all_f, all_c = [], [], []

    def visit(t, parent_m, depth):
        go = t.m_GameObject.read()
        m = parent_m @ local_matrix(t) if depth > 0 else np.eye(4)
        if not go.m_IsActive or "snow" in go.m_Name.lower() or "destroy" in go.m_Name.lower():
            return
        children = [ch.read() for ch in t.m_Children]
        names = [ch.m_GameObject.read().m_Name.lower() for ch in children]
        has_high = any("high" in n for n in names)
        for cmp in components(go):
            if cmp.__class__.__name__ != "MeshFilter" or not getattr(cmp, "m_Mesh", None) or not cmp.m_Mesh.path_id:
                continue
            renderer = next((r for r in components(go) if r.__class__.__name__ == "MeshRenderer"), None)
            if renderer is None or not renderer.m_Enabled:
                continue
            try:
                mesh = cmp.m_Mesh.read()
                v, groups = mesh_triangles(mesh)
            except Exception:
                continue
            if not len(v) or not groups:
                continue
            world = (m @ np.c_[v, np.ones(len(v))].T).T[:, :3]
            mats = []
            for ptr in renderer.m_Materials:
                try:
                    mats.append(material_colour(ptr.read()) if ptr.path_id else np.array([0.6, 0.6, 0.6]))
                except Exception:
                    mats.append(np.array([0.6, 0.6, 0.6]))
            base = sum(len(x) for x in all_v)
            for gi, faces in enumerate(groups):
                colour = mats[min(gi, len(mats) - 1)] if mats else np.array([0.6, 0.6, 0.6])
                for f in faces:
                    all_f.append([base + f[0], base + f[1], base + f[2]])
                    all_c.append(colour)
            all_v.append(world)
        for child, name in zip(children, names):
            # a level-of-detail group: draw the detailed version only
            if has_high and "high" not in name and ("lod" in name or name in ("stone", "low", "new_low")):
                continue
            if "lod" in name and not has_high:
                continue
            visit(child, m, depth + 1)

    visit(transform, np.eye(4), 0)
    if not all_v:
        return None
    return np.vstack(all_v).astype(np.float32), np.array(all_f, dtype=np.int32), (np.array(all_c) * 255).clip(0, 255).astype(np.uint8)


def main():
    env = UnityPy.load(BUNDLE)
    os.makedirs(OUT, exist_ok=True)
    # Most pieces live under gameelements/pieces; a few (ships, carts, some stations, saplings) live elsewhere: take any prefab whose
    # name is a piece in _pieces.json too.
    wanted = set()
    try:
        with open(os.path.join(blueprints_dir(), "_pieces.json"), encoding="utf-8") as fh:
            wanted = {p["p"] for p in json.load(fh)["pieces"]}
    except (OSError, ValueError, KeyError):
        pass
    count = 0
    for path, asset in env.container.items():
        low = path.lower()
        if not low.endswith(".prefab") or "/effects/" in low:
            continue
        in_pieces = low.startswith("assets/gameelements/pieces/")
        if not in_pieces and os.path.splitext(os.path.basename(path))[0] not in wanted:
            continue
        try:
            root = asset.read()
            result = export_piece(root)
        except Exception:
            continue
        if result is None:
            continue
        v, f, c = result
        np.savez_compressed(os.path.join(OUT, root.m_Name + ".npz"), v=v, f=f, c=c)
        count += 1
    print(count, "piece shapes ->", OUT)


if __name__ == "__main__":
    main()
