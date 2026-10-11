"""
Collision shapes for hollow prefabs (python prefab_shapes.py giant_ribs ...): a prefab whose one box (prefab_bounds.json) would wall off
what a player walks through (a giant's ribcage you walk inside) gets its collider as a list of boxes in prefab_shapes.json, in the
prefab's own frame: its MeshCollider's triangles (read from the game's files, as game_meshes.py reads meshes; kept on this PC only
as these numbers) marked into CELL cubes, the marked cubes merged into boxes. The walk and gap checks use these boxes in place of the
one box (each moved, turned and scaled as the part is, then made axis-aligned again).
"""
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
OUT = os.path.join(HERE, "prefab_shapes.json")
CELL = 0.4


def collider_triangles(name):
    """The prefab's MeshColliders' triangles in its own frame: (vertices, faces), or None."""
    import game_meshes as gm
    sys.path.insert(0, gm.BLUEPRINT_TOOLS)
    from extract_pieces import components, local_matrix
    from extract_meshes import mesh_triangles
    root = gm._main_bundle().get(name)
    if root is None:
        return None
    go = root.read()
    tr = next(x for x in components(go) if x.__class__.__name__ == "Transform")
    vs, fs = [], []

    def visit(t, parent, depth):
        g = t.m_GameObject.read()
        m = parent @ local_matrix(t) if depth else np.eye(4)
        for c in components(g):
            if c.__class__.__name__ == "MeshCollider" and c.m_Mesh.path_id and not c.m_IsTrigger:
                v, groups = mesh_triangles(c.m_Mesh.read())
                base = sum(len(x) for x in vs)
                vs.append((m @ np.c_[v, np.ones(len(v))].T).T[:, :3])
                fs.extend([[base + a, base + b, base + c_] for grp in groups for a, b, c_ in grp])
        for ch in t.m_Children:
            visit(ch.read(), m, depth + 1)
    visit(tr, np.eye(4), 0)
    return (np.vstack(vs), np.array(fs)) if vs else None


def voxels(v, f):
    """The CELL cubes any triangle passes through (by dense points on each triangle)."""
    lo = v.min(0)
    marked = set()
    for t in v[f]:
        e1, e2 = t[1] - t[0], t[2] - t[0]
        n = int(max(np.linalg.norm(e1), np.linalg.norm(e2)) / (CELL / 4)) + 2
        a, b = np.meshgrid(np.linspace(0, 1, n), np.linspace(0, 1, n))
        keep = a + b <= 1
        pts = t[0] + a[keep][:, None] * e1 + b[keep][:, None] * e2
        marked.update(map(tuple, np.floor((pts - lo) / CELL).astype(int)))
    return lo, marked


def merge(marked):
    """Marked cubes merged into boxes: runs along x, then equal runs stacked along z, then along y."""
    rows = {}
    for i, j, k in sorted(marked):
        rows.setdefault((j, k), []).append(i)
    runs = []                                                   # (i0, i1, j, k)
    for (j, k), xs in rows.items():
        start = prev = xs[0]
        for x in xs[1:] + [None]:
            if x is not None and x == prev + 1:
                prev = x
                continue
            runs.append((start, prev, j, k))
            if x is not None:
                start = prev = x
    # stack equal x-runs along z
    by = {}
    for i0, i1, j, k in runs:
        by.setdefault((i0, i1, j), []).append(k)
    slabs = []                                                  # (i0, i1, j, k0, k1)
    for (i0, i1, j), ks in by.items():
        ks.sort()
        start = prev = ks[0]
        for k in ks[1:] + [None]:
            if k is not None and k == prev + 1:
                prev = k
                continue
            slabs.append((i0, i1, j, start, prev))
            if k is not None:
                start = prev = k
    # stack equal slabs along y
    by = {}
    for i0, i1, j, k0, k1 in slabs:
        by.setdefault((i0, i1, k0, k1), []).append(j)
    boxes = []
    for (i0, i1, k0, k1), js in by.items():
        js.sort()
        start = prev = js[0]
        for j in js[1:] + [None]:
            if j is not None and j == prev + 1:
                prev = j
                continue
            boxes.append((i0, i1, start, prev, k0, k1))
            if j is not None:
                start = prev = j
    return boxes


def shape(name):
    got = collider_triangles(name)
    if got is None:
        return None
    lo, marked = voxels(*got)
    out = []
    for i0, i1, j0, j1, k0, k1 in merge(marked):
        a = lo + np.array([i0, j0, k0]) * CELL
        b = lo + np.array([i1 + 1, j1 + 1, k1 + 1]) * CELL
        out.append([[round(float(x), 3) for x in a], [round(float(x), 3) for x in b]])
    return out


def main():
    shapes = json.load(open(OUT)) if os.path.exists(OUT) else {}
    for name in sys.argv[1:]:
        s = shape(name)
        if s is None:
            print(f"{name}: no MeshCollider")
            continue
        shapes[name] = s
        print(f"{name}: {len(s)} boxes")
    json.dump(shapes, open(OUT, "w"), separators=(",", ":"))


if __name__ == "__main__":
    main()
