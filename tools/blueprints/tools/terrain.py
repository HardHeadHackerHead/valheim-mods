"""
The real ground under a placed blueprint, from the mod's survey (_survey.json), so a design can fit a slope: posts long enough to reach the
ground, platforms level, stairs that start on the ground.

    site = Site.for_blueprint("Wooden fort")     # uses where it was placed (_imports.json) and the survey around it
    site.ground(x, z)                            # ground height at blueprint coordinates, relative to the ground at the anchor
    site.highest(points), site.lowest(points)
"""
import json, math, os
from blueprint import blueprints_dir, rotation, _apply


class Site:
    def __init__(self, survey, origin_xz=None, yaw=0.0):
        self.s = survey
        cx, cy, cz = survey["centre"]
        self.cx, self.cz = cx, cz
        self.ox, self.oz = origin_xz if origin_xz else (cx, cz)
        self.yaw = yaw
        self.step = survey["step"]
        self.grid = survey["ground"]
        self.n = (len(self.grid) - 1) // 2
        # the survey's heights are relative to its centre; shift them so 0 is the ground at the blueprint's anchor
        self.base = self._world(self.ox, self.oz) if origin_xz else 0.0

    @classmethod
    def for_blueprint(cls, name, folder=None):
        folder = folder or blueprints_dir()
        imports = json.load(open(os.path.join(folder, "_imports.json"), encoding="utf-8"))
        rec = imports[name]
        survey_file = os.path.join(folder, "..", "claude", "_survey.json")   # Claude Tools writes it there
        if not os.path.exists(survey_file):
            survey_file = os.path.join(folder, "_survey.json")                # older BuildOrders wrote it here
        survey = json.load(open(survey_file, encoding="utf-8"))
        return cls(survey, (rec["origin"][0], rec["origin"][2]), rec["yaw"])

    def _world(self, wx, wz):
        """Ground at a world position, relative to the survey centre (bilinear between the grid points; flat beyond the edge)."""
        fx = (wx - self.cx) / self.step + self.n
        fz = (wz - self.cz) / self.step + self.n
        size = len(self.grid)
        fx = min(max(fx, 0.0), size - 1.0001); fz = min(max(fz, 0.0), size - 1.0001)
        ix, iz = int(fx), int(fz)
        tx, tz = fx - ix, fz - iz
        g = self.grid
        a = g[iz][ix] * (1 - tx) + g[iz][ix + 1] * tx
        b = g[iz + 1][ix] * (1 - tx) + g[iz + 1][ix + 1] * tx
        return a * (1 - tz) + b * tz

    def world_of(self, x, z):
        v = _apply(rotation(0, self.yaw, 0), [x, 0, z])
        return self.ox + v[0], self.oz + v[2]

    def ground(self, x, z):
        """Ground height at blueprint coordinates (x, z), relative to the ground at the anchor."""
        wx, wz = self.world_of(x, z)
        return self._world(wx, wz) - self.base

    def highest(self, points):
        return max(self.ground(x, z) for x, z in points)

    def lowest(self, points):
        return min(self.ground(x, z) for x, z in points)

    def covers(self, x, z):
        wx, wz = self.world_of(x, z)
        r = self.n * self.step
        return abs(wx - self.cx) <= r and abs(wz - self.cz) <= r


class Flat:
    """No survey: the ground is level everywhere."""
    def ground(self, x, z):
        return 0.0

    def highest(self, points):
        return 0.0

    def lowest(self, points):
        return 0.0
