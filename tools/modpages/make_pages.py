"""
Writes each mod's own page, mods/<Mod>/README.md, from what the mod already has: its pitch and keys from the repo's README, its
DESCRIPTION.txt (how it works), its settings (from the game's BepInEx/config file for it, which has every setting with its description
and default; or, without the game, from the Config.Bind calls in its code), RESTART_REQUIRED.txt, CHANGELOG.txt and its cover.

Hand-written parts survive: whatever is between the HAND-WRITTEN markers in a page is kept as it is when the page is written again. The
first time, that part holds the screenshot slots: images/<n>.png under the mod, made as placeholder cards here (with what each picture
should show, from shots.json) for you to replace with real pictures of the same name. A real picture is never overwritten.

    python tools/modpages/make_pages.py              # every mod
    python tools/modpages/make_pages.py Arena Rainbows
    python tools/modpages/make_pages.py --committed  # read each mod as committed (not the working copy)
"""
import html, io, json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODS = os.path.join(ROOT, "mods")
GAME_CONFIG = os.environ.get("VALHEIM_CONFIG", r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\config")
SHOTS = json.load(io.open(os.path.join(os.path.dirname(__file__), "shots.json"), encoding="utf-8"))
BEGIN, END = "<!-- HAND-WRITTEN: kept when this page is made again -->", "<!-- END HAND-WRITTEN -->"
COMMITTED = "--committed" in sys.argv


def read(path):
    """A file's text (as committed, with --committed), or None."""
    if COMMITTED:
        rel = os.path.relpath(path, ROOT).replace("\\", "/")
        try:
            return subprocess.run(["git", "show", "HEAD:" + rel], cwd=ROOT, capture_output=True, check=True).stdout.decode("utf-8-sig")
        except subprocess.CalledProcessError:
            return None
    if not os.path.exists(path):
        return None
    return io.open(path, encoding="utf-8-sig").read()


def paragraphs(text):
    return [p.strip() for p in re.split(r"\n\s*\n", text.replace("\r\n", "\n")) if p.strip()]


# ---- the repo's README: each mod's emoji, title and pitch (by its cover), the keys table, who needs it ----------------------------

def readme_parts():
    text = io.open(os.path.join(ROOT, "README.md"), encoding="utf-8").read().replace("\r\n", "\n")
    cards = {}
    for m in re.finditer(r"### (.+?)\n<img src=\"dist/([A-Za-z0-9]+)\.cover\.(?:png|jpg)\"[^>]*>\n\n(.*?)(?=\n</td>|\n### |\n<table>|\n## )", text, re.S):
        title, folder, body = m.group(1).strip(), m.group(2), m.group(3).strip()
        body = re.sub(r"\n*\*\*\[More: how it works.*?\)\*\*", "", body).strip()   # (the front page's link to this very page)
        cards[folder] = (title, body)
    keys = [l for l in text.split("\n") if l.startswith("| ") and l.count("|") >= 4]
    return cards, keys, text


def name_of(src):
    m = re.search(r'const string Name\s*=\s*"([^"]+)"', src)
    return m.group(1) if m else None


# ---- settings -----------------------------------------------------------------------------------------------------------------

def settings_from_config(guid):
    """Every setting in the game's config file for the mod: (section, key, default, description)."""
    path = os.path.join(GAME_CONFIG, guid + ".cfg")
    if not os.path.exists(path):
        return None
    out, section, desc, default = [], "", [], None
    for line in io.open(path, encoding="utf-8-sig").read().splitlines():
        line = line.strip()
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1]; desc = []; default = None
        elif line.startswith("## ") and not line.startswith("## Settings file") and not line.startswith("## Plugin GUID"):
            desc.append(line[3:])
        elif line.startswith("# Default value:"):
            default = line.split(":", 1)[1].strip()
        elif "=" in line and not line.startswith("#"):
            key = line.split("=", 1)[0].strip()
            if desc and default is not None:      # (an entry without a description is one the mod no longer has)
                out.append((section, key, default, " ".join(desc)))
            desc = []; default = None
    return out


def settings_from_code(src):
    out = []
    for m in re.finditer(r'Bind\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*(.+?)\s*,\s*(?:new ConfigDescription\(\s*)?"((?:[^"\\]|\\.)*)"', src, re.S):
        default = re.sub(r"\s+", " ", m.group(3)).replace("new KeyboardShortcut(KeyCode.", "").rstrip(")").rstrip("f")
        out.append((m.group(1), m.group(2), default, m.group(4).replace('\\"', '"')))
    return out


# ---- the page -------------------------------------------------------------------------------------------------------------------

def description_md(text):
    """DESCRIPTION.txt as Markdown: its first paragraph is the summary; short lines over a list become headings."""
    lines = text.replace("\r\n", "\n").strip().split("\n")
    out = []
    for i, line in enumerate(lines):
        s = line.strip()
        nxt = lines[i + 1].strip() if i + 1 < len(lines) else ""
        if i > 0 and s and not s.startswith("- ") and len(s) < 48 and not s.endswith(".") and nxt.startswith("- "):
            out.append("")
            out.append("### " + s)
            out.append("")
        elif s and not s.startswith("- ") and i > 0:
            out.append("")      # (a line of its own: its own paragraph)
            out.append(s)
        else:
            out.append(s)
    md = "\n".join(out)
    return re.sub(r"\n{3,}", "\n\n", md).strip()


def placeholder(path, caption, mod):
    """A card where a picture is to go (never over a real one)."""
    if os.path.exists(path):
        return
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        return
    w, h = 1280, 720
    img = Image.new("RGB", (w, h), (38, 30, 24))
    d = ImageDraw.Draw(img)
    for k in range(6):
        d.rectangle([18 + k, 18 + k, w - 19 - k, h - 19 - k], outline=(120 - k * 12, 92 - k * 9, 50 - k * 5))
    def font(size, bold=False):
        for f in (("georgiab.ttf" if bold else "georgia.ttf"), "DejaVuSerif.ttf", "arial.ttf"):
            try:
                return ImageFont.truetype(f, size)
            except OSError:
                continue
        return ImageFont.load_default()
    def centred(text, y, f, colour):
        box = d.textbbox((0, 0), text, font=f)
        d.text(((w - (box[2] - box[0])) / 2, y), text, font=f, fill=colour)
    centred(mod, 170, font(40, True), (230, 190, 110))
    centred("Screenshot to come", 250, font(64, True), (240, 228, 205))
    words, line, rows = caption.split(), "", []
    f = font(36)
    for word in words:
        trial = (line + " " + word).strip()
        if d.textbbox((0, 0), trial, font=f)[2] > w - 220 and line:
            rows.append(line); line = word
        else:
            line = trial
    rows.append(line)
    for k, row in enumerate(rows):
        centred(row, 360 + k * 48, f, (205, 192, 170))
    centred("mods/%s/images/%s" % (mod, os.path.basename(path)), h - 110, font(24), (150, 132, 110))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.quantize(colors=48).save(path, optimize=True)


def page(folder, cards, keys):
    d = os.path.join(MODS, folder)
    src = "\n".join(read(os.path.join(d, f)) or "" for f in sorted(os.listdir(d)) if f.endswith(".cs"))
    name = name_of(src) or folder
    guid = (re.search(r'const string Guid\s*=\s*"([^"]+)"', src) or [None, None])[1]
    version = (re.search(r'const string Version\s*=\s*"([^"]+)"', src) or [None, "?"])[1]
    title, pitch = cards.get(folder, (name, ""))
    desc = read(os.path.join(d, "DESCRIPTION.txt")) or ""
    log = read(os.path.join(d, "CHANGELOG.txt")) or ""
    restart = (read(os.path.join(d, "RESTART_REQUIRED.txt")) or "").strip()
    cover = next((f for f in ("cover.png", "cover.jpg") if os.path.exists(os.path.join(d, f))), None)
    short = re.sub(r"^\S+\s+", "", title) if not title[0].isalnum() else title
    plain = short.split(":")[0].strip()

    out = ["<!-- This page is made by tools/modpages/make_pages.py from the mod's DESCRIPTION.txt, CHANGELOG.txt, settings and the",
           "     repo's README. Change those (or the HAND-WRITTEN part below), not the rest of this page. -->", "",
           "# " + title, ""]
    if cover:
        out += ['<img src="%s" alt="%s" width="100%%">' % (cover, html.escape(plain)), ""]
    out += ["**Version %s**  ·  [all the mods](../../README.md)  ·  installs and updates through the in-game [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) (**F7**)" % version, ""]
    if pitch:
        out += [pitch, ""]

    # the hand-written part: kept as it is, or the screenshot slots the first time
    old = read(os.path.join(d, "README.md")) or ""
    m = re.search(re.escape(BEGIN) + r"(.*?)" + re.escape(END), old, re.S)
    if m:
        hand = m.group(1).strip("\n")
    else:
        shots = SHOTS.get(folder, ["%s in the game" % plain])
        rows = ["## 📸 Screenshots", ""]
        for k, caption in enumerate(shots, 1):
            file = "images/%d.png" % k
            placeholder(os.path.join(d, file), caption, folder)
            rows += ["**%s**" % caption, "", '<img src="%s" alt="%s" width="100%%">' % (file, html.escape(caption)), ""]
        hand = "\n".join(rows).strip("\n")
    out += [BEGIN, hand, END, ""]

    if desc:
        out += ["## 🔍 How it works", "", description_md(desc), ""]

    mine = [k for k in keys if re.match(r"\|\s*[^|]+\|\s*(%s|%s)\s*\|" % (re.escape(folder), re.escape(plain)), k)]
    if mine:
        out += ["## ⌨️ Keys", "", "| Key | What it does |", "|---|---|"]
        for k in mine:
            cells = [c.strip() for c in k.strip("|").split("|")]
            out.append("| %s | %s |" % (cells[0], " | ".join(cells[2:])))
        out += [""]

    sets = None
    if guid and os.path.isdir(GAME_CONFIG):
        # the mod's config files (some keep more in a second one, "<guid>.more.cfg")
        files = sorted(f[:-4] for f in os.listdir(GAME_CONFIG) if f.endswith(".cfg") and (f == guid + ".cfg" or f.startswith(guid + ".")))
        sets = [x for f in files for x in (settings_from_config(f) or [])] or None
    sets = sets or settings_from_code(src)
    if sets:
        out += ["## ⚙️ Settings", "", "In `BepInEx/config/%s.cfg` (made the first time the game runs with the mod)." % (guid or folder)]
        section = None
        for sec, key, default, about in sets:
            if sec != section:
                out += ["", "**%s**" % sec, "", "| Setting | Default | What it does |", "|---|---|---|"]
                section = sec
            out.append("| `%s` | `%s` | %s |" % (key, default.replace("|", "\\|"), about.replace("|", "\\|")))
        out += [""]

    out += ["## 👥 Playing together", ""]
    if restart:
        out += ["Everyone in the world needs it, **the host above all**. %s" % restart, ""]
    else:
        out += ["See [who needs which mod](../../README.md#playing-together) on the front page.", ""]

    if log.strip():
        out += ["## 📜 Changes", ""]
        for p in paragraphs(log):
            p = re.sub(r"\s*\n\s*", " ", p)
            mv = re.match(r"^(\d+\.\d+(?:\.\d+)?):\s*(.*)", p, re.S)
            out.append("- **%s** %s" % (mv.group(1), mv.group(2)) if mv else "- " + p)
        out += [""]

    text = "\n".join(out).rstrip() + "\n"
    io.open(os.path.join(d, "README.md"), "w", encoding="utf-8", newline="\n").write(text)
    return name


def main():
    cards, keys, _ = readme_parts()
    want = [a for a in sys.argv[1:] if not a.startswith("--")]
    done = []
    for folder in sorted(os.listdir(MODS)):
        if want and folder not in want:
            continue
        if not os.path.exists(os.path.join(MODS, folder, folder + ".csproj")):
            continue
        done.append(page(folder, cards, keys))
    print("pages written:", ", ".join(done))


if __name__ == "__main__":
    main()
