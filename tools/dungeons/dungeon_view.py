"""
Pictures of dungeon rooms and layouts with the game's real shapes (game_meshes.py), for checking a pack's own rooms by eye before trying
them in the game. A painter's renderer (far triangles first), fast enough for a whole hall of pieces and the game's rooms around it.

    from dungeon_view import Scene
    s = Scene()
    s.add("blackmarble_floor", at=(0, 0, 0), euler=(0, 90, 0))      a piece, a prop or a game room, by prefab name
    s.render("out.png", views=((30, 25), (-40, 20)))                 (yaw, pitch) in degrees: yaw 0 looks from -z towards +z
    s.render("cut.png", cut_y=6)                                      leave out everything above y 6 (to look into a storey)
    s.render("section.png", section=True)                             leave out the half of the scene nearer the camera
"""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from game_meshes import shape  # noqa: E402


def euler_matrix(rx, ry, rz):
    """Unity's Quaternion.Euler as a 3x3 matrix (z, then x, then y)."""
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx, cy, sy, cz, sz = math.cos(ax), math.sin(ax), math.cos(ay), math.sin(ay), math.cos(az), math.sin(az)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


def frame(at=(0, 0, 0), euler=(0, 0, 0), scale=(1, 1, 1)):
    m = np.eye(4)
    m[:3, :3] = euler_matrix(*euler) @ np.diag(scale)
    m[:3, 3] = at
    return m


class Scene:
    def __init__(self):
        self.items = []         # (name, 4x4 matrix, tint or None)
        self.missing = set()
        self.marks = []         # (point, colour, label): doorways, levers...

    def add(self, name, at=(0, 0, 0), euler=(0, 0, 0), scale=(1, 1, 1), parent=None, tint=None):
        m = frame(at, euler, scale)
        if parent is not None:
            m = parent @ m
        self.items.append((name, m, tint))
        return m

    def mark(self, point, colour=(255, 220, 60), label=""):
        self.marks.append((np.array(point, float), colour, label))

    def triangles(self, cut_y=None, keep=None):
        tris, cols = [], []
        for name, m, tint in self.items:
            s = shape(name)
            if s is None or not len(s[1]):
                if s is None:
                    self.missing.add(name)
                continue
            v, f, c = s[0], s[1], s[2]
            w = (m[:3, :3] @ v.T).T + m[:3, 3]
            t = w[f]
            col = c.astype(np.float32) / 255.0
            if tint is not None:
                col = col * 0.35 + np.array(tint, np.float32) * 0.65
            if cut_y is not None:
                sel = t[:, :, 1].min(axis=1) < cut_y
                t, col = t[sel], col[sel]
            if keep is not None:
                sel = keep(t.mean(axis=1))
                t, col = t[sel], col[sel]
            tris.append(t)
            cols.append(col)
        if not tris:
            return np.zeros((0, 3, 3)), np.zeros((0, 3))
        return np.concatenate(tris), np.concatenate(cols)

    def bounds(self):
        lo, hi = np.array([1e9] * 3), np.array([-1e9] * 3)
        for name, m, _ in self.items:
            s = shape(name)
            if s is None or not len(s[1]):
                continue
            pts = np.array([[x, y, z] for x in (s[3][0], s[4][0]) for y in (s[3][1], s[4][1]) for z in (s[3][2], s[4][2])])
            w = (m[:3, :3] @ pts.T).T + m[:3, 3]
            lo, hi = np.minimum(lo, w.min(0)), np.maximum(hi, w.max(0))
        return lo, hi

    def render(self, path, views=((30, 25),), size=(900, 700), target=None, dist=None, fov=50, cut_y=None, section=False, ortho=False,
               title=None, cols=2, bg=(18, 20, 26)):
        lo, hi = self.bounds()
        if target is None:
            target = (lo + hi) / 2
        target = np.array(target, float)
        if dist is None:
            dist = float(np.linalg.norm(hi - lo)) * 0.9 + 4
        images = []
        for yaw, pitch in views:
            yr, pr = math.radians(yaw), math.radians(pitch)
            back = np.array([math.sin(yr) * math.cos(pr), math.sin(pr), -math.cos(yr) * math.cos(pr)])
            keep = None
            if section:
                horiz = np.array([back[0], 0, back[2]])
                horiz /= np.linalg.norm(horiz) + 1e-9
                keep = lambda c, h=horiz: (c - target) @ h < 0.3
            tris, colours = self.triangles(cut_y, keep)
            marks = [m for m in self.marks if (keep is None or keep(m[0][None, :])[0]) and (cut_y is None or m[0][1] < cut_y)]
            images.append(self._draw(tris, colours, marks, target, back, dist, size, fov, ortho, bg, f"yaw {yaw} pitch {pitch}"))
        n = len(images)
        c = 1 if n == 1 else cols
        r = (n + c - 1) // c
        sheet = Image.new("RGB", (size[0] * c, size[1] * r + (40 if title else 0)), bg)
        for i, im in enumerate(images):
            sheet.paste(im, ((i % c) * size[0], (i // c) * size[1] + (40 if title else 0)))
        if title:
            ImageDraw.Draw(sheet).text((12, 10), title, fill=(230, 220, 200), font=_font(22))
        sheet.save(path)
        if self.missing:
            print("no shape for:", ", ".join(sorted(self.missing)))
        return path

    def _draw(self, tris, colours, marks, target, back, dist, size, fov, ortho, bg, label):
        W, H = size[0] * 2, size[1] * 2
        eye = target + back * dist
        fwd = -back
        right = np.cross([0, 1, 0], fwd)
        if np.linalg.norm(right) < 1e-6:
            right = np.array([1.0, 0, 0])
        right /= np.linalg.norm(right)
        up = np.cross(fwd, right)
        img = Image.new("RGB", (W, H), bg)
        draw = ImageDraw.Draw(img)
        if len(tris):
            d = tris - eye
            x, y, z = d @ right, d @ up, d @ fwd
            f = 1.0 / math.tan(math.radians(fov) / 2)
            ok = z.min(axis=1) > 0.2
            x, y, z, col, t = x[ok], y[ok], z[ok], colours[ok], tris[ok]
            if ortho:
                scale = (H / 2) / (dist * math.tan(math.radians(fov) / 2))
                sx, sy = x * scale + W / 2, H / 2 - y * scale
            else:
                sx, sy = x * f / z * (H / 2) + W / 2, H / 2 - y * f / z * (H / 2)
            n = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0])
            n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-9
            n[(n @ fwd) > 0] *= -1          # both sides lit alike
            light = np.array([-0.4, 0.85, -0.35]); light /= np.linalg.norm(light)
            fill = np.array([0.5, 0.2, 0.6]); fill /= np.linalg.norm(fill)
            lit = 0.38 + 0.62 * np.clip(n @ light, 0, 1) + 0.18 * np.clip(n @ fill, 0, 1)
            rgb = np.clip(col * lit[:, None] * 1.7, 0, 1) ** 0.8
            order = np.argsort(-z.mean(axis=1))
            for k in order:
                draw.polygon([(sx[k, 0], sy[k, 0]), (sx[k, 1], sy[k, 1]), (sx[k, 2], sy[k, 2])],
                             fill=tuple(int(c * 255) for c in rgb[k]))
        for p, colour, text in marks:
            dd = p - eye
            zz = dd @ fwd
            if zz <= 0.2:
                continue
            if ortho:
                scale = (H / 2) / (dist * math.tan(math.radians(fov) / 2))
                px, py = (dd @ right) * scale + W / 2, H / 2 - (dd @ up) * scale
            else:
                f = 1.0 / math.tan(math.radians(fov) / 2)
                px, py = (dd @ right) * f / zz * (H / 2) + W / 2, H / 2 - (dd @ up) * f / zz * (H / 2)
            draw.ellipse([px - 7, py - 7, px + 7, py + 7], fill=colour, outline=(0, 0, 0))
            if text:
                draw.text((px + 10, py - 12), text, fill=colour, font=_font(24))
        draw.text((10, H - 34), label, fill=(150, 150, 150), font=_font(22))
        return img.resize(size, Image.LANCZOS)


def _font(px):
    for name in ("arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, px)
        except OSError:
            continue
    return ImageFont.load_default()
