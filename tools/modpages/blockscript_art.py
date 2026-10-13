#!/usr/bin/env python3
"""Draws Quad's Blockscript's package icon and cover with Pillow (no outside art).

    python tools/modpages/blockscript_art.py

Writes mods/Blockscript/thunderstore/icon.png (256 x 256), mods/Blockscript/cover.png (640 x 360) and dist/Blockscript.cover.png.
Everything is drawn at 4x and shrunk, so the edges are clean. The look follows the other mods' art: warm gold against cool blue-teal
darkness, carved stone, a gold border; the blocks use the editor's own palette colours.
"""
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
FONTS = "C:/Windows/Fonts/"
GOLD = (236, 190, 96)
GOLD_HI = (255, 232, 160)
GOLD_LO = (150, 98, 36)

# the editor's block colours
EVENT, CONTROL, ACTION, VALUE = (211, 148, 33), (214, 115, 43), (140, 92, 199), (58, 153, 97)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def vgrad(size, top, bottom):
    w, h = size
    im = Image.new("RGB", size)
    px = ImageDraw.Draw(im)
    for y in range(h):
        px.line([(0, y), (w, y)], fill=lerp(top, bottom, y / max(1, h - 1)))
    return im


def radial(size, centre, inner, outer, reach):
    w, h = size
    cx, cy = centre
    im = Image.new("RGB", size, outer)
    mask = Image.new("L", size, 0)
    md = ImageDraw.Draw(mask)
    steps = 90
    for i in range(steps, 0, -1):
        r = reach * i / steps
        v = int(255 * (1 - i / steps) ** 1.4)
        md.ellipse([cx - r, cy - r, cx + r, cy + r], fill=v)
    return Image.composite(Image.new("RGB", size, inner), im, mask)


def glow(layer, radius, strength=1.0):
    """The light of an RGBA layer, blurred, to lay under it."""
    g = layer.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        a = g.getchannel("A").point(lambda v: min(255, int(v * strength)))
        g.putalpha(a)
    return g


def rounded_mask(size, box, radius):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius, fill=255)
    return m


def block(size, box, base, radius, rune=None, rune_colour=GOLD, tilt=0.0):
    """One carved block as an RGBA layer: a body with a vertical gradient, a lit top edge, a dark underside, a glowing rune."""
    w, h = size
    x0, y0, x1, y1 = box
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    mask = rounded_mask(size, box, radius)
    body = vgrad(size, lerp(base, (255, 255, 255), 0.30), lerp(base, (0, 0, 0), 0.38))
    # a faint granite grain
    rnd = random.Random(int(x0 * 7 + y0 * 13))
    grain = Image.new("L", size, 128)
    gd = ImageDraw.Draw(grain)
    for _ in range(int((x1 - x0) * (y1 - y0) / 900)):
        gx, gy = rnd.randint(x0, x1), rnd.randint(y0, y1)
        r = rnd.randint(2, 7)
        gd.ellipse([gx - r, gy - r, gx + r, gy + r], fill=rnd.randint(95, 165))
    grain = grain.filter(ImageFilter.GaussianBlur(3))
    body = ImageChops.overlay(body, Image.merge("RGB", (grain, grain, grain)))
    layer.paste(body, (0, 0), mask)
    d = ImageDraw.Draw(layer)
    bw = max(6, (y1 - y0) // 14)
    # lit top-left rim and dark bottom-right rim, inside the outline
    rim = Image.new("RGBA", size, (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim)
    rd.rounded_rectangle(box, radius, outline=(255, 255, 255, 150), width=bw)
    shade = Image.new("RGBA", size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    sd.rounded_rectangle(box, radius, outline=(0, 0, 0, 190), width=bw)
    # keep only the lit half of the light rim and the dark half of the shade rim
    diag = Image.new("L", size, 0)
    dd = ImageDraw.Draw(diag)
    dd.polygon([(x0, y0), (x1, y0), (x0, y1)], fill=255)
    rim.putalpha(ImageChops.multiply(rim.getchannel("A"), diag))
    inv = ImageChops.invert(diag)
    shade.putalpha(ImageChops.multiply(shade.getchannel("A"), inv))
    layer = Image.alpha_composite(layer, shade)
    layer = Image.alpha_composite(layer, rim)
    # a thin dark outline all round, so every block reads on any background
    ol = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(ol).rounded_rectangle(box, radius, outline=(10, 14, 22, 255), width=max(3, bw // 2))
    layer = Image.alpha_composite(layer, ol)
    if rune:
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        s = (y1 - y0) * 0.30
        rl = Image.new("RGBA", size, (0, 0, 0, 0))
        rd = ImageDraw.Draw(rl)
        for (ax, ay), (bx, by) in rune:
            rd.line([(cx + ax * s, cy + ay * s), (cx + bx * s, cy + by * s)], fill=rune_colour + (255,), width=max(6, int(s * 0.28)), joint="curve")
            for px, py in ((ax, ay), (bx, by)):
                r = max(3, int(s * 0.14))
                rd.ellipse([cx + px * s - r, cy + py * s - r, cx + px * s + r, cy + py * s + r], fill=rune_colour + (255,))
        layer = Image.alpha_composite(layer, glow(rl, s * 0.5, 1.6))
        layer = Image.alpha_composite(layer, rl)
    return layer


# runes (strokes in -1..1 space), simple futhark-like shapes
RUNES = [
    [((-0.5, -1), (-0.5, 1)), ((-0.5, -1), (0.6, -0.25)), ((-0.5, -0.1), (0.6, 0.6))],       # fehu-ish
    [((0, -1), (0, 1)), ((0, -0.9), (0.7, -0.2)), ((0, -0.2), (-0.7, -0.9))],               # thurs-ish
    [((-0.5, -1), (-0.5, 1)), ((-0.5, -1), (0.55, -0.45)), ((0.55, -0.45), (-0.5, 0.1))],   # ansuz-ish
    [((-0.6, -1), (0.6, 1)), ((0.6, -1), (-0.6, 1))],                                       # gebo
    [((0, -1), (0.7, 0)), ((0.7, 0), (0, 1)), ((0, 1), (-0.7, 0)), ((-0.7, 0), (0, -1))],   # othala-ish
]


def bolt(size, p0, p1, width, colour=GOLD_HI):
    """A jagged lightning bolt from p0 to p1 as an RGBA layer."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    pts = [p0]
    n = 5
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    length = math.hypot(dx, dy)
    nx, ny = -dy / length, dx / length
    rnd = random.Random(5)
    for i in range(1, n):
        t = i / n
        off = (rnd.random() - 0.5) * length * 0.32
        pts.append((p0[0] + dx * t + nx * off, p0[1] + dy * t + ny * off))
    pts.append(p1)
    d.line(pts, fill=colour + (255,), width=width, joint="curve")
    return layer


def spark(size, centre, r, colour=GOLD_HI):
    """A four-point star flare."""
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = centre
    d.polygon([(cx, cy - r), (cx + r * 0.16, cy - r * 0.16), (cx + r, cy), (cx + r * 0.16, cy + r * 0.16),
               (cx, cy + r), (cx - r * 0.16, cy + r * 0.16), (cx - r, cy), (cx - r * 0.16, cy - r * 0.16)], fill=colour + (255,))
    d.ellipse([cx - r * 0.28, cy - r * 0.28, cx + r * 0.28, cy + r * 0.28], fill=(255, 255, 240, 255))
    return Image.alpha_composite(glow(layer, r * 0.45, 1.8), layer)


def vignette(im, strength=0.55):
    w, h = im.size
    m = Image.new("L", (w, h), 0)
    md = ImageDraw.Draw(m)
    md.rectangle([0, 0, w, h], fill=255)
    md.ellipse([-w * 0.18, -h * 0.25, w * 1.18, h * 1.25], fill=0)
    m = m.filter(ImageFilter.GaussianBlur(min(w, h) * 0.12)).point(lambda v: int(v * strength))
    return Image.composite(Image.new("RGB", im.size, (3, 6, 12)), im, m)


def border(im, width, inset=0):
    d = ImageDraw.Draw(im)
    w, h = im.size
    for i in range(width):
        t = i / max(1, width - 1)
        c = lerp(GOLD_HI, GOLD_LO, t)
        d.rectangle([inset + i, inset + i, w - 1 - inset - i, h - 1 - inset - i], outline=c)
    return im


def stars_and_motes(im, count, seed, size_range=(1, 3)):
    rnd = random.Random(seed)
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    w, h = im.size
    for _ in range(count):
        x, y = rnd.randint(0, w), rnd.randint(0, h)
        r = rnd.uniform(*size_range)
        a = rnd.randint(70, 200)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 220, 140, a))
    return Image.alpha_composite(im.convert("RGBA"), glow(layer, 2, 1.5)).convert("RGB")


# ================================================================ the icon

def make_icon():
    S = 1024
    size = (S, S)
    bg = radial(size, (S * 0.55, S * 0.45), (28, 74, 98), (6, 14, 28), S * 0.85)
    # a warm glow low on the left, as if from a hearth
    warm = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(warm).ellipse([-S * 0.25, S * 0.55, S * 0.55, S * 1.3], fill=(255, 140, 40, 120))
    bg = Image.alpha_composite(bg.convert("RGBA"), warm.filter(ImageFilter.GaussianBlur(S * 0.12))).convert("RGB")
    im = bg.convert("RGBA")

    bw, bh = 400, 300
    r = 58
    colours = [(120, 130, 150), (112, 124, 146), (126, 136, 156), (116, 126, 150)]
    # a staircase going up to the right; the top block hangs a little off, mid-snap
    placements = [(110, 650), (290, 495), (470, 340), (655, 150)]
    blocks = []
    for i, (x, y) in enumerate(placements):
        blocks.append(block(size, (x, y, x + bw, y + bh), colours[i], r, RUNES[i]))
    # shadows under each, offset down-right
    for i, (x, y) in enumerate(placements):
        sh = Image.new("RGBA", size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle((x + 22, y + 34, x + bw + 22, y + bh + 34), r, fill=(0, 0, 0, 175))
        im = Image.alpha_composite(im, sh.filter(ImageFilter.GaussianBlur(20)))
        im = Image.alpha_composite(im, blocks[i])
    # the connection between block 3 and 4: a bolt and a spark in the gap
    gap = (655 + 70, 150 + bh + 6)
    b = bolt(size, (gap[0] - 70, gap[1] - 4), (gap[0] + 120, gap[1] + 14), 26)
    im = Image.alpha_composite(im, glow(b, 26, 2.2))
    im = Image.alpha_composite(im, b)
    im = Image.alpha_composite(im, spark(size, (gap[0] + 150, gap[1] + 2), 120))
    im = stars_and_motes(im.convert("RGB"), 40, 3, (2, 5))
    im = vignette(im, 0.5)
    im = im.convert("RGB")
    border(im, 22, 0)
    out = im.resize((256, 256), Image.LANCZOS)
    path = os.path.join(ROOT, "mods", "Blockscript", "thunderstore", "icon.png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path)
    return out


# ================================================================ the cover

def title_text(size, text, font, centre, fill_top=GOLD_HI, fill_bottom=GOLD_LO):
    """The title as a gold, outlined, glowing layer."""
    w, h = size
    box = ImageDraw.Draw(Image.new("L", size)).textbbox((0, 0), text, font=font)
    tw, th = box[2] - box[0], box[3] - box[1]
    x, y = centre[0] - tw / 2 - box[0], centre[1] - th / 2 - box[1]
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).text((x, y), text, font=font, fill=255)
    # the gold fill, brighter on top
    grad = vgrad(size, fill_top, fill_bottom).convert("RGBA")
    face = Image.new("RGBA", size, (0, 0, 0, 0))
    face.paste(grad, (0, 0), mask)
    # the outline: the mask grown, dark
    grow = mask.filter(ImageFilter.MaxFilter(9))
    outline = Image.new("RGBA", size, (0, 0, 0, 0))
    outline.paste(Image.new("RGBA", size, (14, 10, 6, 255)), (0, 0), grow)
    halo = Image.new("RGBA", size, (0, 0, 0, 0))
    halo.paste(Image.new("RGBA", size, (255, 190, 80, 255)), (0, 0), mask)
    halo = glow(halo, 14, 0.9)
    shadow = Image.new("RGBA", size, (0, 0, 0, 0))
    shadow.paste(Image.new("RGBA", size, (0, 0, 0, 255)), (0, 0), grow.filter(ImageFilter.GaussianBlur(5)))
    shadow = ImageChops.offset(shadow, 4, 7)
    out = Image.alpha_composite(shadow, halo)
    out = Image.alpha_composite(out, outline)
    out = Image.alpha_composite(out, face)
    return out


def tree(d, x, base, h, w, colour):
    d.polygon([(x, base - h), (x - w / 2, base), (x + w / 2, base)], fill=colour)
    d.polygon([(x, base - h * 0.72), (x - w * 0.66, base - h * 0.28), (x + w * 0.66, base - h * 0.28)], fill=colour)
    d.polygon([(x, base - h * 0.9), (x - w * 0.42, base - h * 0.5), (x + w * 0.42, base - h * 0.5)], fill=colour)


def make_cover():
    K = 4
    W, H = 640 * K, 360 * K
    size = (W, H)
    # a stormy dusk: dark blue above, a warm bruise at the horizon
    im = vgrad(size, (10, 20, 38), (60, 52, 66)).convert("RGBA")
    haze = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(haze).ellipse([W * 0.05, H * 0.45, W * 0.95, H * 1.2], fill=(226, 130, 60, 110))
    im = Image.alpha_composite(im, haze.filter(ImageFilter.GaussianBlur(120)))
    # clouds
    rnd = random.Random(11)
    clouds = Image.new("RGBA", size, (0, 0, 0, 0))
    cd = ImageDraw.Draw(clouds)
    for _ in range(26):
        cx, cy = rnd.randint(0, W), rnd.randint(int(H * 0.05), int(H * 0.5))
        rx, ry = rnd.randint(220, 520), rnd.randint(50, 120)
        cd.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=(18, 26, 46, rnd.randint(70, 150)))
    im = Image.alpha_composite(im, clouds.filter(ImageFilter.GaussianBlur(50)))
    # a lightning flash, top right, lighting the scene a little
    flash_from, flash_to = (W * 0.84, H * 0.02), (W * 0.74, H * 0.58)
    fl = bolt(size, flash_from, flash_to, 20, (235, 242, 255))
    flare = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(flare).ellipse([W * 0.55, -H * 0.1, W * 1.05, H * 0.6], fill=(120, 170, 255, 70))
    im = Image.alpha_composite(im, flare.filter(ImageFilter.GaussianBlur(100)))
    im = Image.alpha_composite(im, glow(fl, 28, 2.4))
    im = Image.alpha_composite(im, fl)
    # mountains and forest silhouettes in layers
    def ridge(seed, base, amp, colour, peak=0.0):
        rr = random.Random(seed)
        layer = Image.new("RGBA", size, (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        pts = [(0, H)]
        x = 0
        y = base
        while x <= W:
            pts.append((x, y))
            x += rr.randint(60, 150)
            y = base - rr.random() * amp - (peak if rr.random() < 0.2 else 0)
        pts += [(W, base), (W, H)]
        ld.polygon(pts, fill=colour)
        return layer
    im = Image.alpha_composite(im, ridge(2, H * 0.58, 130, (34, 40, 62, 255), 70).filter(ImageFilter.GaussianBlur(3)))
    im = Image.alpha_composite(im, ridge(8, H * 0.68, 70, (22, 30, 44, 255)))
    trees = Image.new("RGBA", size, (0, 0, 0, 0))
    td = ImageDraw.Draw(trees)
    rr = random.Random(4)
    for x in list(range(-40, W, 70)):
        th = rr.randint(150, 300)
        tree(td, x + rr.randint(-20, 20), H * 0.8, th, th * 0.46, (10, 16, 24, 255))
    im = Image.alpha_composite(im, trees)
    # the ground
    ground = Image.new("RGBA", size, (0, 0, 0, 0))
    ImageDraw.Draw(ground).polygon([(0, H * 0.84), (W * 0.3, H * 0.8), (W * 0.7, H * 0.83), (W, H * 0.79), (W, H), (0, H)], fill=(8, 13, 18, 255))
    im = Image.alpha_composite(im, ground)

    # the stack of blocks across the lower half: coloured like the editor's blocks, connected, with a bolt between two of them
    def coloured_block(x, y, w, h, colour, text_w=None, notch=True):
        lay = Image.new("RGBA", size, (0, 0, 0, 0))
        d = ImageDraw.Draw(lay)
        radius = 26
        sh = Image.new("RGBA", size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle((x + 10, y + 20, x + w + 10, y + h + 20), radius, fill=(0, 0, 0, 180))
        lay = Image.alpha_composite(lay, sh.filter(ImageFilter.GaussianBlur(16)))
        mask = rounded_mask(size, (x, y, x + w, y + h), radius)
        body = vgrad(size, lerp(colour, (255, 255, 255), 0.28), lerp(colour, (0, 0, 0), 0.30))
        lay.paste(body, (0, 0), mask)
        hl = Image.new("RGBA", size, (0, 0, 0, 0))
        ImageDraw.Draw(hl).rounded_rectangle((x + 4, y + 4, x + w - 4, y + h - 4), radius - 4, outline=(255, 255, 255, 110), width=6)
        lay = Image.alpha_composite(lay, hl)
        ol = Image.new("RGBA", size, (0, 0, 0, 0))
        ImageDraw.Draw(ol).rounded_rectangle((x, y, x + w, y + h), radius, outline=(10, 12, 20, 255), width=7)
        lay = Image.alpha_composite(lay, ol)
        # the editor's look: white slots with a darker pick on the block's face
        sd = ImageDraw.Draw(lay)
        sw = text_w or w * 0.3
        sx = x + 42
        for k, frac in enumerate((0.32, 0.22)):
            ww = w * frac
            sd.rounded_rectangle((sx, y + h / 2 - 22, sx + ww, y + h / 2 + 22), 20, fill=(250, 250, 252, 235))
            sd.rounded_rectangle((sx + 10, y + h / 2 - 5, sx + ww - 10, y + h / 2 + 5), 5, fill=lerp(colour, (0, 0, 0), 0.4) + (255,))
            sx += ww + 22
        return lay

    bx = int(W * 0.12)
    bw = int(W * 0.62)
    bh = 150
    rows = [(bx + 60, int(H * 0.575), bw - 110, EVENT), (bx + 140, int(H * 0.575) + bh - 14, bw - 190, CONTROL), (bx + 220, int(H * 0.575) + 2 * (bh - 14), bw - 270, ACTION)]
    # a hat block over them: the editor's 'when' block shape with a stripe on top
    for (x, y, w, c) in rows:
        im = Image.alpha_composite(im, coloured_block(x, y, w, bh, c))
    # the snapping bolt between the first and second block, with a spark
    gx, gy = bx + 60 + (bw - 110) * 0.78, int(H * 0.575) + bh - 8
    b = bolt(size, (gx - 40, gy - 90), (gx + 20, gy + 70), 18)
    im = Image.alpha_composite(im, glow(b, 22, 2.0))
    im = Image.alpha_composite(im, b)
    im = Image.alpha_composite(im, spark(size, (gx - 10, gy), 90))
    # a green value block on the right, floating toward the stack
    im = Image.alpha_composite(im, coloured_block(int(W * 0.76), int(H * 0.60), int(W * 0.17), 120, VALUE))

    im = im.convert("RGB")
    # the title and the tagline: the biggest size that fits with a margin
    def fitted(name, text, limit):
        px = 400
        while px > 20:
            f = ImageFont.truetype(FONTS + name, px)
            box = ImageDraw.Draw(Image.new("L", (10, 10))).textbbox((0, 0), text, font=f)
            if box[2] - box[0] <= limit:
                return f
            px -= 6
        return ImageFont.truetype(FONTS + name, 20)
    f = fitted("georgiab.ttf", "Quad's Blockscript", W * 0.86)
    t = title_text(size, "Quad's Blockscript", f, (W / 2, H * 0.15))
    im = Image.alpha_composite(im.convert("RGBA"), t)
    tag = title_text(size, "Build your own game rules with blocks", fitted("georgiaz.ttf", "Build your own game rules with blocks", W * 0.62), (W / 2, H * 0.295), (255, 236, 190), (214, 170, 90))
    im = Image.alpha_composite(im, tag)
    im = stars_and_motes(im.convert("RGB"), 70, 9, (3, 8))
    im = vignette(im, 0.5)
    border(im, 14, 0)
    out = im.resize((640, 360), Image.LANCZOS)
    for rel in (("mods", "Blockscript", "cover.png"), ("dist", "Blockscript.cover.png")):
        path = os.path.join(ROOT, *rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        out.save(path)
    return out


if __name__ == "__main__":
    icon = make_icon()
    cover = make_cover()
    print("icon", icon.size, "cover", cover.size)
