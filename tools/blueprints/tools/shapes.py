"""
Curved shapes from flat pieces: a blueprint may turn any piece to any angle, so domes, barrel roofs and tent roofs can be built from rings
of tilted wall panels following a profile curve. Used by kizhi.py; usable by any design.

    from shapes import Shapes
    sh = Shapes(bp)
    sh.dome(0, 10, 0, radius=1.2, piece="scale_quarterwall_1x1")      # an onion dome standing at (0, 10, 0)
    sh.panel("scale_wall_2x2", centre, out_dir, tilt)                   # one tilted panel

Panels are placed by the middle of their face; `tilt` leans the top outward (positive) or inward (negative), in degrees from upright.
"""
import math
from blueprint import rotation, _apply

# An onion dome's outline for a bulge of radius 1: (distance out from the axis, height above the base), bottom to top.
ONION = [(0.55, 0.0), (0.80, 0.18), (0.97, 0.42), (1.00, 0.62), (0.93, 0.88), (0.76, 1.15), (0.52, 1.42), (0.27, 1.68), (0.07, 1.95)]


def resample(points, step):
    """Points along a polyline, `step` apart (measured along it), starting at its first point; the last point is included."""
    out = [points[0]]
    carry = 0.0
    for (a0, h0), (a1, h1) in zip(points, points[1:]):
        seg = math.hypot(a1 - a0, h1 - h0)
        t = step - carry
        while t <= seg + 1e-9:
            out.append((a0 + (a1 - a0) * t / seg, h0 + (h1 - h0) * t / seg))
            t += step
        carry = seg - (t - step)
    if math.hypot(out[-1][0] - points[-1][0], out[-1][1] - points[-1][1]) > step * 0.35:
        out.append(points[-1])
    return out


class Shapes:
    def __init__(self, bp):
        self.bp = bp
        self.cat = bp.catalog
        # which way a positive x-turn leans a panel's top: towards local -z or +z
        up = _apply(rotation(10.0, 0.0, 0.0), [0.0, 1.0, 0.0])
        self.rx_sign = 1.0 if up[2] > 0 else -1.0       # rx = rx_sign * tilt leans the top towards +z (outward) for tilt > 0

    def size(self, piece):
        p = self.cat.by_name[piece]
        return [p["max"][i] - p["min"][i] for i in range(3)], [(p["max"][i] + p["min"][i]) / 2 for i in range(3)]

    def panel(self, piece, centre, out_dir, tilt=0.0, roll=0.0):
        """Put a flat piece so its face's middle is at `centre`, its front facing out_dir (x, z) and its top leaning out by `tilt` degrees.
        `roll` turns it in its own plane (for laying panels sideways)."""
        yaw = math.degrees(math.atan2(out_dir[0], out_dir[1])) % 360.0
        rx = self.rx_sign * tilt
        rot = rotation(rx, yaw, roll)
        _, c = self.size(piece)
        off = _apply(rot, c)
        return self.bp._item(piece, centre[0] - off[0], centre[1] - off[1], centre[2] - off[2], rx, yaw, roll, False)

    def ring(self, piece, cx, cy, cz, apothem, tilt, sides=8, start=0.0):
        """One ring of panels round a vertical axis: faces `apothem` out, their middles at height cy."""
        for k in range(sides):
            ang = math.radians(start + 360.0 * k / sides)
            d = (math.sin(ang), math.cos(ang))
            self.panel(piece, (cx + d[0] * apothem, cy, cz + d[1] * apothem), d, tilt)

    def profile_rings(self, piece, cx, base, cz, outline, sides=8, start=0.0, from_top=False):
        """Rings of panels following an outline [(apothem, height), ...] given at the panel's own height steps. from_top lays the
        rows down from the outline's end (so a dome closes at its tip; any shortfall is left at the bottom)."""
        (w, h, _), _ = self.size(piece)
        pts = list(reversed(resample(list(reversed(outline)), h))) if from_top else resample(outline, h)
        for (a0, h0), (a1, h1) in zip(pts, pts[1:]):
            tilt = math.degrees(math.atan2(a1 - a0, h1 - h0))
            self.ring(piece, cx, base + (h0 + h1) / 2, cz, (a0 + a1) / 2, tilt, sides, start)
        return pts

    def core(self, cx, cz, y_bottom, y_top, piece="woodiron_pole"):
        """A hidden post of iron-cored poles from y_bottom up to y_top: the strongest support there is, for domes and towers."""
        y = y_bottom
        while y < y_top - 0.3:
            y = min(y, y_top - 2.0) if y_top - y_bottom >= 2.0 else y    # the last pole ends exactly at y_top (overlapping the one below)
            self.bp.place_snap(piece, "bottom", (cx, y, cz))
            y += 2.0

    def dome(self, cx, base, cz, radius, piece="scale_quarterwall_1x1", drum=0.0, sides=8, cross=True, start=22.5, core_from=None):
        """An onion dome of bulge `radius` on a short drum, with a cross on top. Returns the height of its tip.
        core_from: run a hidden iron post from that height up through the dome to its tip (a dome's thin panels cannot hold
        themselves up; the post carries them from the top down)."""
        outline = [(a * radius, h * radius + drum) for a, h in ONION]
        if drum > 0:
            outline = [(ONION[0][0] * radius, 0.0)] + outline
        self.profile_rings(piece, cx, base, cz, outline, sides, start, from_top=True)
        tip = base + drum + ONION[-1][1] * radius
        if core_from is not None:
            self.core(cx, cz, core_from, tip - 0.2)
        if cross:                                   # a cross: a pole two metres tall (three on a big dome) with a bar two-thirds up
            poles = 3 if radius > 1.8 else 2
            for k in range(poles):
                self.bp.place_snap("wood_pole", "bottom", (cx, tip - 0.2 + k, cz))
            self.bp.raw("wood_beam_1", cx, tip - 0.2 + poles * 0.68, cz, yaw=start)
        return tip
