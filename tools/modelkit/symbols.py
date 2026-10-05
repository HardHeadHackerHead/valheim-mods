"""The slot machine's reel pictures: eight cells in one strip (six symbols, two of them twice)."""
import math
from PIL import Image, ImageDraw, ImageFilter

CELL = 256
SS = 4  # draw at 4x and shrink, for smooth edges
# which symbol each of the 8 faces of a reel shows
FACES = [0, 1, 2, 3, 4, 5, 0, 1]
NAMES = ["Coin", "Boar", "Horn", "Axe", "Raven", "Valknut"]

CREAM = (240, 230, 205)
EDGE = (110, 84, 44)
GOLD = (226, 170, 40)
GOLD_D = (150, 100, 20)
INK = (36, 28, 24)
RED = (176, 34, 28)
BONE = (250, 246, 232)
BROWN = (110, 66, 34)
STEEL = (150, 156, 168)


def px(v):
    return int(v * SS)


def pts(points, ox, oy, s=1.0):
    return [(px(ox + x * s), px(oy + y * s)) for x, y in points]


def coin(d, ox, oy):
    c = (ox + 128, oy + 128)
    for r, col in ((98, GOLD_D), (92, GOLD), (70, GOLD_D), (65, (244, 200, 80))):
        d.ellipse([px(c[0] - r), px(c[1] - r), px(c[0] + r), px(c[1] + r)], fill=col)
    # a rune (fehu) cut into the face
    w = 9
    d.line([(px(c[0] - 18), px(c[1] + 44)), (px(c[0] - 18), px(c[1] - 44))], fill=GOLD_D, width=px(w))
    d.line([(px(c[0] - 18), px(c[1] - 22)), (px(c[0] + 30), px(c[1] - 48))], fill=GOLD_D, width=px(w))
    d.line([(px(c[0] - 18), px(c[1] + 4)), (px(c[0] + 30), px(c[1] - 22))], fill=GOLD_D, width=px(w))


def boar(d, ox, oy):
    # head, ears, snout, tusks, eye
    d.polygon(pts([(48, 150), (74, 78), (118, 56), (176, 62), (212, 100), (216, 160), (182, 206), (96, 210)], ox, oy), fill=BROWN)
    d.polygon(pts([(76, 84), (60, 34), (112, 62)], ox, oy), fill=(86, 50, 26))      # ear
    d.polygon(pts([(172, 66), (206, 30), (214, 86)], ox, oy), fill=(86, 50, 26))    # ear
    d.ellipse([px(ox + 70), px(oy + 130), px(ox + 176), px(oy + 214)], fill=(150, 98, 70))  # snout
    for x in (96, 148):
        d.ellipse([px(ox + x - 7), px(oy + 168), px(ox + x + 7), px(oy + 188)], fill=INK)   # nostrils
    d.polygon(pts([(76, 196), (44, 150), (70, 160), (96, 190)], ox, oy), fill=BONE)           # tusk
    d.polygon(pts([(172, 196), (204, 150), (178, 160), (152, 190)], ox, oy), fill=BONE)       # tusk
    for x in (98, 168):
        d.ellipse([px(ox + x - 9), px(oy + 104), px(ox + x + 9), px(oy + 122)], fill=INK)
        d.ellipse([px(ox + x - 3), px(oy + 107), px(ox + x + 3), px(oy + 113)], fill=BONE)


def horn(d, ox, oy):
    # a drinking horn: a thick curve from a wide mouth (top right) down to a pointed tip (bottom left)
    path = []
    for i in range(61):
        t = i / 60
        ang = math.radians(-18 + t * 118)
        cx, cy, R = 62, 70, 150
        x = cx + R * math.cos(ang) * (1 - 0.12 * t)
        y = cy + R * math.sin(ang) * (1 - 0.12 * t)
        path.append((x, y, 44 * (1 - t) ** 0.8 + 7))
    for (x, y, r) in path:
        d.ellipse([px(ox + x - r), px(oy + y - r), px(ox + x + r), px(oy + y + r)], fill=(196, 168, 112))
    for (x, y, r) in path:
        d.ellipse([px(ox + x - r * 0.62 - 3), px(oy + y - r * 0.62 - 5), px(ox + x + r * 0.62 - 3), px(oy + y + r * 0.62 - 5)], fill=(222, 196, 142))
    for (x, y, r) in path[::3]:
        d.ellipse([px(ox + x - r * 0.28 - 8), px(oy + y - r * 0.28 - 10), px(ox + x + r * 0.28 - 8), px(oy + y + r * 0.28 - 10)], fill=(244, 226, 184))
    x, y, r = path[14]
    d.ellipse([px(ox + x - r - 2), px(oy + y - r - 2), px(ox + x + r + 2), px(oy + y + r + 2)], outline=GOLD_D, width=px(9))
    x, y, r = path[0]
    d.ellipse([px(ox + x - r), px(oy + y - r * 0.8), px(ox + x + r), px(oy + y + r * 0.8)], fill=(214, 186, 128))
    d.ellipse([px(ox + x - r * 0.78), px(oy + y - r * 0.55), px(ox + x + r * 0.78), px(oy + y + r * 0.55)], fill=(72, 42, 22))
    for fx, fy in ((-22, -22), (2, -30), (26, -20), (-6, -12)):   # foam
        d.ellipse([px(ox + x + fx - 17), px(oy + y + fy - 14), px(ox + x + fx + 17), px(oy + y + fy + 14)], fill=BONE)
    x, y, r = path[-1]
    d.ellipse([px(ox + x - r - 3), px(oy + y - r - 3), px(ox + x + r + 3), px(oy + y + r + 3)], fill=GOLD_D)


def axe(d, ox, oy):
    d.line([(px(ox + 70), px(oy + 224)), (px(ox + 186), px(oy + 40))], fill=BROWN, width=px(18))
    d.polygon(pts([(150, 56), (232, 40), (240, 128), (196, 156), (172, 110)], ox, oy), fill=STEEL)
    d.polygon(pts([(172, 76), (222, 62), (224, 118), (196, 134)], ox, oy), fill=(196, 202, 214))
    d.polygon(pts([(160, 46), (140, 70), (186, 96), (192, 60)], ox, oy), fill=(110, 116, 128))
    d.line([(px(ox + 82), px(oy + 204)), (px(ox + 98), px(oy + 178))], fill=GOLD_D, width=px(20))


def raven(d, ox, oy):
    d.polygon(pts([(40, 190), (96, 152), (118, 190), (60, 224)], ox, oy), fill=INK)          # tail
    d.ellipse([px(ox + 66), px(oy + 96), px(ox + 188), px(oy + 196)], fill=INK)               # body
    d.polygon(pts([(110, 118), (206, 40), (176, 134)], ox, oy), fill=(54, 46, 60))            # wing
    d.ellipse([px(ox + 150), px(oy + 70), px(ox + 214), px(oy + 134)], fill=INK)              # head
    d.polygon(pts([(208, 92), (246, 106), (210, 118)], ox, oy), fill=GOLD_D)                  # beak
    d.ellipse([px(ox + 176), px(oy + 88), px(ox + 192), px(oy + 104)], fill=RED)              # eye
    for x in (110, 134):
        d.line([(px(ox + x), px(oy + 190)), (px(ox + x), px(oy + 226))], fill=GOLD_D, width=px(7))


def valknut(d, ox, oy):
    # three interlocked triangles
    cx, cy = ox + 128, oy + 140
    size = 66
    offsets = [(0, -44), (-46, 30), (46, 30)]
    shades = [GOLD, (200, 140, 30), (246, 196, 70)]
    for (dx, dy), col in zip(offsets, shades):
        tri = [(cx + dx + size * math.cos(math.radians(-90 + j * 120)), cy + dy + size * math.sin(math.radians(-90 + j * 120))) for j in range(3)]
        d.line([(px(x), px(y)) for x, y in tri + [tri[0], tri[1]]], fill=GOLD_D, width=px(24), joint="curve")
    for (dx, dy), col in zip(offsets, shades):
        tri = [(cx + dx + size * math.cos(math.radians(-90 + j * 120)), cy + dy + size * math.sin(math.radians(-90 + j * 120))) for j in range(3)]
        d.line([(px(x), px(y)) for x, y in tri + [tri[0], tri[1]]], fill=col, width=px(14), joint="curve")
    d.ellipse([px(cx - 12), px(cy - 6), px(cx + 12), px(cy + 18)], fill=RED)


DRAW = [coin, boar, horn, axe, raven, valknut]


def atlas(path="symbols.png"):
    img = Image.new("RGB", (CELL * 8 * SS, CELL * SS), CREAM)
    d = ImageDraw.Draw(img)
    for i, sym in enumerate(FACES):
        ox = i * CELL
        # a faint inner panel so each face looks like a printed card
        d.rounded_rectangle([px(ox + 6), px(6), px(ox + CELL - 6), px(CELL - 6)], radius=px(18), outline=EDGE, width=px(5), fill=CREAM)
        DRAW[sym](d, ox, 0)
    img = img.resize((CELL * 8, CELL), Image.LANCZOS)
    img.save(path)
    return img


if __name__ == "__main__":
    atlas("symbols.png")
