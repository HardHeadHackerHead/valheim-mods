"""Checks the pictures on each mod's page (mods/<Mod>/README.md): every one exists, and none is still the "Screenshot to come" card
that tools/modpages/make_pages.py draws where a picture is to go (a dark 1280x720 card). thunderstore.ps1 refuses to package a mod whose
page has one: Thunderstore shows the page as it was uploaded, until the next version.

    python tools/check_pages.py [Mod ...]      (every mod with a page when none is named; exit 1 if any picture is missing or a card)
"""
import io, os, re, sys

MODS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'mods')
CARD = (38, 30, 24)   # the placeholder card's background (make_pages.placeholder)


def is_card(path):
    from PIL import Image
    im = Image.open(path).convert('RGB')
    if im.size != (1280, 720): return False
    near = lambda p: all(abs(a - b) < 6 for a, b in zip(im.getpixel(p), CARD))
    return near((640, 60)) and near((100, 650)) and near((1180, 650))


def check(mod):
    page = os.path.join(MODS, mod, 'README.md')
    if not os.path.exists(page): return []
    problems = []
    for src in re.findall(r'<img src="(images/[^"]+)"', io.open(page, encoding='utf-8').read()):
        path = os.path.join(MODS, mod, src)
        if not os.path.exists(path): problems.append(f'{src} is missing')
        elif is_card(path): problems.append(f'{src} is still the "Screenshot to come" card: take the picture (see the valheim-look skill)')
    return problems


mods = sys.argv[1:] or sorted(m for m in os.listdir(MODS) if os.path.exists(os.path.join(MODS, m, 'README.md')))
bad = 0
for mod in mods:
    p = check(mod)
    bad += len(p)
    if p or len(mods) > 1: print(f'{mod}: ' + ('ok' if not p else f'{len(p)} problem(s)'))
    for line in p: print('   ' + line)
sys.exit(1 if bad else 0)
