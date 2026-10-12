"""
Writes each mod's own page, mods/<Mod>/README.md, in one layout for every mod (the same page goes to Thunderstore through modkit
package, which makes its links absolute):

    title, cover, one-line pitch, badges, links
    Features (DESCRIPTION.txt)        Screenshots (hand-written)       How to use (keys from the repo's README, hand-written notes)
    Goes well with (related.json: our related mods' icons left to right, each one's name under it)
    Other mods (hand-written: known clashes, "none known" until there are some)
    Multiplayer (who needs it, from the repo's README; hand-written notes)            Settings (the mod's config file, or its code)
    Made with AI      Join us on Discord (community.md)      About Quads Lab (tools/modpages/about.md, the same on every page)
    Support my mods (support.md, the Buy Me a Coffee banner)      Recent changes (CHANGELOG.txt, the newest 3)

Hand-written parts survive: each sits between <!-- HAND-WRITTEN: <part> --> and <!-- END HAND-WRITTEN: <part> --> and is kept as it is
when the page is written again (screenshots, how-to-use, other-mods, multiplayer). The first time, the screenshots part holds
placeholder cards (images/<n>.png, with what each picture should show, from shots.json) for you to replace with real pictures of the
same name; a real picture is never overwritten.

Some lines are for one site only: <!-- GITHUB-ONLY -->...<!-- END GITHUB-ONLY --> shows on GitHub and is left out on Thunderstore, and
<!-- THUNDERSTORE-ONLY: ... --> is hidden on GitHub and shown on Thunderstore (modkit package does both).

    python tools/modpages/make_pages.py              # every mod
    python tools/modpages/make_pages.py Arena Rainbows
    python tools/modpages/make_pages.py --committed  # read each mod as committed (not the working copy)
"""
import html, io, json, os, re, subprocess, sys
from urllib.parse import quote

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODS = os.path.join(ROOT, "mods")
GAME_CONFIG = os.environ.get("VALHEIM_CONFIG", r"D:\SteamLibrary\steamapps\common\Valheim\BepInEx\config")
SHOTS = json.load(io.open(os.path.join(os.path.dirname(__file__), "shots.json"), encoding="utf-8"))
ABOUT = os.path.join(os.path.dirname(__file__), "about.md")
COMMUNITY = os.path.join(os.path.dirname(__file__), "community.md")   # the Discord section's words
DISCORD = "https://discord.gg/EwTjQxEZPy"
# the banner over the Discord section: loaded from GitHub on every page (Thunderstore too), so a new picture here shows everywhere
BANNER = "https://raw.githubusercontent.com/HardHeadHackerHead/valheim-mods/main/tools/modpages/discord-banner.jpg"
BANNER_FILE = os.path.join(os.path.dirname(__file__), "discord-banner.jpg")
# Buy Me a Coffee: a badge by the Discord one at the top, and a section with its banner (loaded from GitHub like the Discord one) after
# About Quads Lab. Its words are support.md; the banner is support-banner.jpg (made from support-banner-source.png, 1600 px wide).
SUPPORT = "https://buymeacoffee.com/quadh"
SUPPORT_WORDS = os.path.join(os.path.dirname(__file__), "support.md")
SUPPORT_BANNER = "https://raw.githubusercontent.com/HardHeadHackerHead/valheim-mods/main/tools/modpages/support-banner.jpg"
SUPPORT_BANNER_FILE = os.path.join(os.path.dirname(__file__), "support-banner.jpg")
# Our mods that go with each one (related.json, written by hand): icons in a row, linking to their Thunderstore pages (Hexium's upload
# rewrites those links to Hexium's pages, tools/hexium_publish.py), the icons loaded from GitHub like the banners
RELATED = json.load(io.open(os.path.join(os.path.dirname(__file__), "related.json"), encoding="utf-8"))
ICON_URL = "https://raw.githubusercontent.com/HardHeadHackerHead/valheim-mods/main/mods/%s/thunderstore/icon.png"
THUNDERSTORE_TEAM = "Quads_Lab"  # our team on Thunderstore: each mod is its own package there
REPO = "https://github.com/HardHeadHackerHead/valheim-mods"
MANAGER = "https://github.com/HardHeadHackerHead/valheim-mod-manager"
NOT_ON_THUNDERSTORE = set()  # mods that are GitHub and F7 only (Claude Tools was, before it moved to its own repo, valheim-claude-tools; Jet Skis until 0.3.0)
PARTS = ["screenshots", "how-to-use", "other-mods", "multiplayer"]
# the languages our mods ship (the game's names: every language with 2%+ of Valheim's players), as a player reads their own
LANGUAGES = {"English": "English", "Russian": "Русский (Russian)", "German": "Deutsch (German)", "Chinese": "简体中文 (Chinese)",
             "Portuguese_Brazilian": "Português do Brasil (Portuguese)", "Spanish": "Español (Spanish)", "French": "Français (French)",
             "Polish": "Polski (Polish)", "Turkish": "Türkçe (Turkish)"}
GH_ONLY, GH_END = "<!-- GITHUB-ONLY -->", "<!-- END GITHUB-ONLY -->"
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


def github_only(text):
    return GH_ONLY + text + GH_END


def thunderstore_only(text):
    return "<!-- THUNDERSTORE-ONLY: %s -->" % text


# ---- the repo's README: each mod's emoji (by its cover), the keys table, who needs which mod ----------------------------------

def readme_parts():
    text = io.open(os.path.join(ROOT, "README.md"), encoding="utf-8").read().replace("\r\n", "\n")
    cards = {}
    for m in re.finditer(r"### ([^\n]+)\n<img src=\"dist/([A-Za-z0-9]+)\.cover\.(?:png|jpg)\"", text):
        cards[m.group(2)] = m.group(1).strip()
    keys = [l for l in text.split("\n") if l.startswith("| ") and l.count("|") >= 4]
    # "Playing together": | **ModA, ModB** (why) | who needs it |
    who, default = [], None
    m = re.search(r"### Playing together\n(.*?)\n(?:##|$)", text, re.S)
    for row in (m.group(1) if m else "").split("\n"):
        cells = [c.strip() for c in row.strip().strip("|").split("|")] if row.startswith("| ") else []
        if len(cells) < 2 or cells[0] in ("Mod", "---"):
            continue
        if cells[0].lower().startswith("everything else"):
            default = cells[1]
            continue
        names = [n.strip() for b in re.findall(r"\*\*([^*]+)\*\*", cells[0]) for n in b.split(",")]
        why = re.sub(r"\*\*[^*]+\*\*", "", cells[0]).strip(" ()")
        who.append((names, why, cells[1]))
    return cards, keys, who, default


def name_of(src):
    # the plugin's own (public) Name: a mod may have other private "Name" constants (Parkour's bar)
    m = re.search(r'public const string Name\s*=\s*"([^"]+)"', src) or re.search(r'const string Name\s*=\s*"([^"]+)"', src)
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
    """DESCRIPTION.txt as Markdown: short lines over a list become headings, other lines their own paragraphs."""
    lines = text.replace("\r\n", "\n").strip().split("\n")
    out = []
    for i, line in enumerate(lines):
        s = line.strip()
        nxt = lines[i + 1].strip() if i + 1 < len(lines) else ""
        if s and not s.startswith("- ") and len(s) < 48 and not s.endswith(".") and nxt.startswith("- "):
            out += ["", "### " + s, ""]
        elif s and not s.startswith("- "):
            out += ["", s]      # (a line of its own: its own paragraph)
        else:
            out.append(s)
    md = re.sub(r"\n{3,}", "\n\n", "\n".join(out)).strip()
    return re.sub(r"^### How it works\n\n", "", md)     # (the section is the features: a heading saying so again is noise)


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


def hand_parts(old):
    """The hand-written parts of a page, by name. A page from before the parts had names has one block: split it by its headings."""
    parts = {k: v.strip("\n") for k, v in re.findall(r"<!-- HAND-WRITTEN: ([a-z-]+) -->\n?(.*?)\n?<!-- END HAND-WRITTEN: \1 -->", old, re.S)}
    if parts:
        return parts
    m = re.search(r"<!-- HAND-WRITTEN: kept when this page is made again -->(.*?)<!-- END HAND-WRITTEN -->", old, re.S)
    if not m:
        return {}
    parts = {}
    for sec in re.split(r"(?m)^(?=## )", m.group(1).strip("\n")):
        head, _, body = sec.partition("\n")
        head, body = head.lower(), body.strip("\n")
        if not head.startswith("## ") or not body:
            continue
        part = ("screenshots" if "screenshot" in head else "other-mods" if "clash" in head else
                "how-to-use" if "good to know" in head else "multiplayer")
        if part == "multiplayer" and "who needs it" not in head:
            body = "**%s**\n\n%s" % (re.sub(r"^##\s*\W*\s*", "", sec.partition("\n")[0]).strip(), body)
        elif part == "multiplayer":
            continue        # (who needs it is written from the repo's README now)
        parts[part] = (parts[part] + "\n\n" + body) if part in parts else body
    return parts


def block(name, body):
    return ["<!-- HAND-WRITTEN: %s -->" % name, body, "<!-- END HAND-WRITTEN: %s -->" % name, ""]


def package_of(folder):
    """A mod's Thunderstore package name and its title (Quad's <Name>), from its thunderstore.toml."""
    toml = read(os.path.join(MODS, folder, "thunderstore.toml")) or ""
    named = re.search(r'(?m)^name\s*=\s*"([^"]+)"', toml)
    package = named.group(1) if named else folder
    return package, "Quad's " + re.sub(r"^Quads_", "", package).replace("_", " ").replace(" Of ", " of ")


def related_section(folder):
    """The 'Goes well with' row: the related mods' icons left to right (a table's top row), each one's short name under it; both link to it."""
    mods = [m for m in RELATED.get("related", {}).get(folder, []) if os.path.exists(os.path.join(MODS, m, "thunderstore", "icon.png"))]
    if not mods:
        return []
    icons, names = [], []
    for m in mods:
        package, title = package_of(m)
        link = ("%s/tree/main/mods/%s" % (REPO, m)) if m in RELATED.get("notOnThunderstore", [])             else "https://thunderstore.io/c/valheim/p/%s/%s/" % (THUNDERSTORE_TEAM, package)
        icons.append('<a href="%s"><img src="%s" alt="%s" width="64"></a>' % (link, ICON_URL % m, html.escape(title)))
        names.append("[%s](%s)" % (re.sub(r"^Quad's ", "", title), link))
    return ["## 🤝 Goes well with", "", "| " + " | ".join(icons) + " |", "|" + ":-:|" * len(mods), "| " + " | ".join(names) + " |", ""]


def badge(label, value, colour):
    esc = lambda s: quote(s.replace("-", "--").replace("_", "__"), safe="")
    return "![%s: %s](https://img.shields.io/badge/%s-%s-%s?style=flat-square)" % (label, value, esc(label), esc(value), colour)


def page(folder, cards, keys, who, default_who, about):
    d = os.path.join(MODS, folder)
    src = "\n".join(read(os.path.join(d, f)) or "" for f in sorted(os.listdir(d)) if f.endswith(".cs"))
    name = name_of(src) or folder
    guid = (re.search(r'const string Guid\s*=\s*"([^"]+)"', src) or [None, None])[1]
    version = (re.search(r'const string Version\s*=\s*"([^"]+)"', src) or [None, "?"])[1]
    card = cards.get(folder, name)
    emoji = card.split()[0] if not card[0].isalnum() else ""
    plain = re.sub(r"^\W+\s*", "", card).split(":")[0].strip()
    desc = read(os.path.join(d, "DESCRIPTION.txt")) or ""
    log = read(os.path.join(d, "CHANGELOG.txt")) or ""
    restart = (read(os.path.join(d, "RESTART_REQUIRED.txt")) or "").strip()
    cover = next((f for f in ("cover.png", "cover.jpg") if os.path.exists(os.path.join(d, f))), None)

    # its Thunderstore package (from thunderstore.toml, made by modkit package) and its name there, as Quad's <Name>
    toml = read(os.path.join(d, "thunderstore.toml")) or ""
    named = re.search(r'(?m)^name\s*=\s*"([^"]+)"', toml)
    package = named.group(1) if named else (re.sub(r"[^A-Za-z0-9_]", "", re.sub(r"[\s\-.]+", "_", name)) or folder)
    title = "Quad's " + re.sub(r"^Quads_", "", package).replace("_", " ").replace(" Of ", " of ")
    pitch = (re.search(r'(?m)^description\s*=\s*"((?:[^"\\]|\\.)*)"', toml) or [None, None])[1] or (paragraphs(desc) or [""])[0]
    store = "https://thunderstore.io/c/valheim/p/%s/%s/" % (THUNDERSTORE_TEAM, package)
    on_store = folder not in NOT_ON_THUNDERSTORE
    bug = "%s/issues/new?template=bug.yml&title=%s" % (REPO, quote("[%s] " % title))
    old = read(os.path.join(d, "README.md")) or ""
    hand = hand_parts(old)

    # who needs it in multiplayer: the repo README's "Playing together" table
    rows = [(why, need) for names, why, need in who if folder in names or plain in names or title.replace("Quad's ", "") in names]
    needs = " ".join(need for _, need in rows) if rows else (default_who or "Only the players who want it.")
    everyone = any("everyone" in n.lower() for _, n in rows)
    host = any("host" in n.lower() for _, n in rows)

    out = ["<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,",
           "     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->", "",
           "# %s%s" % (emoji + " " if emoji else "", title), ""]
    if cover:
        out += ['<img src="%s" alt="%s" width="100%%">' % (cover, html.escape(title, quote=False)), ""]
    out += ["**%s**" % pitch, ""]
    out += ["  ".join([badge("version", version, "3b6e8f"),
                       badge("multiplayer", "everyone + host" if everyone and host else "host" if host else "everyone" if everyone else "only who wants it", "7a5c2e"),
                       badge("made with", "Claude Code", "c26a3d")]), ""]
    links = "  ·  ".join(["[Source code](%s/tree/main/mods/%s)" % (REPO, folder), "[Report a bug](%s)" % bug, "[Discord](%s)" % DISCORD])
    out += [(github_only("[Thunderstore](%s)  ·  " % store) if on_store else "") + links, ""]
    out += ["[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](%s)  "
            "[![Buy me a coffee](https://img.shields.io/badge/Buy_me_a-coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](%s)" % (DISCORD, SUPPORT), ""]
    if on_store:
        out += [github_only("**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from "
                            "[Thunderstore](%s), or press **F7** in the game with our [mod manager](%s)." % (store, MANAGER)), ""]
    else:
        out += ["**Install:** press **F7** in the game with our [mod manager](%s), or take `%s.dll` from [dist/](%s/tree/main/dist). "
                "It isn't on Thunderstore." % (MANAGER, folder, REPO), ""]

    if desc:
        rest = desc.replace("\r\n", "\n").strip()
        first = paragraphs(rest)[0]
        if first.strip() == pitch.strip():
            rest = rest[len(first):].strip()
        rest = "\n".join(l for l in rest.split("\n") if not l.startswith("Settings:"))   # (the Settings section lists them all)
        out += ["## ✨ Features", "", description_md(rest), ""]

    # screenshots: hand-written (the first time, placeholder cards with what each picture should show)
    shots = hand.get("screenshots")
    if shots is None:
        rows_ = []
        for k, caption in enumerate(SHOTS.get(folder, ["%s in the game" % plain]), 1):
            file = "images/%d.png" % k
            placeholder(os.path.join(d, file), caption, folder)
            rows_ += ["**%s**" % caption, "", '<img src="%s" alt="%s" width="100%%">' % (file, html.escape(caption)), ""]
        shots = "\n".join(rows_).strip("\n")
    out += ["## 📸 Screenshots", ""] + block("screenshots", shots)

    mine = [k for k in keys if re.match(r"\|\s*[^|]+\|\s*(%s|%s)\s*\|" % (re.escape(folder), re.escape(plain)), k)]
    use = hand.get("how-to-use", "")
    if mine or use:
        out += ["## 🎮 How to use", ""]
        if mine:
            out += ["| Key | What it does |", "|---|---|"]
            for k in mine:
                cells = [c.strip() for c in k.strip("|").split("|")]
                out.append("| %s | %s |" % (cells[0], " | ".join(cells[2:])))
            out += [""]
    out += block("how-to-use", use)

    out += related_section(folder)
    out += ["## 🧩 Other mods", ""] + block("other-mods", hand.get("other-mods") or "**Known clashes:** none known.")
    out += ["Found a clash with another mod? [Tell us](%s) which one and what happened." % bug, ""]

    out += ["## 👥 Multiplayer", "", "**Who needs it:** %s" % needs, ""]
    if restart and "restart" not in needs.lower():
        out += [restart, ""]
    out += block("multiplayer", hand.get("multiplayer", ""))

    # the languages it speaks: its translations/*.json (Shared/Translations.cs), in the order of how many players use them
    tr = os.path.join(d, "translations")
    langs = [l for l in LANGUAGES if os.path.exists(os.path.join(tr, l + ".json"))] if os.path.isdir(tr) else []
    if len(langs) > 1:
        english = "%s/blob/main/mods/%s/translations/English.json" % (REPO, folder)
        fix = "%s/issues/new?template=bug.yml&title=%s" % (REPO, quote("[%s] Translation: " % title))
        out += ["## 🌍 Languages", "",
                "**%s.** It follows the game's own language setting." % ", ".join(LANGUAGES[l] for l in langs), "",
                "The translations were made with AI, so a few words may be off. To correct one, or to add your language, put a file in "
                "`BepInEx/config/translations/%s/` named after the game's name for the language (`German.json`, `Italian.json`, "
                "`Japanese.json`...) with only the lines you change, copied from [English.json](%s) " % (guid or folder, english) +
                "(it wins over the built-in one). Or [tell us](%s) the better words and we'll put them in for everyone." % fix, ""]

    sets = None
    if guid and os.path.isdir(GAME_CONFIG):
        # the mod's config files (some keep more in a second one, "<guid>.more.cfg")
        files = sorted(f[:-4] for f in os.listdir(GAME_CONFIG) if f.endswith(".cfg") and (f == guid + ".cfg" or f.startswith(guid + ".")))
        sets = [x for f in files for x in (settings_from_config(f) or [])] or None
    sets = sets or settings_from_code(src)
    if sets:
        out += ["## ⚙️ Settings", "", "In `BepInEx/config/%s.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod "
                "Manager edit it under **Config editor**.%s" % (guid or folder, github_only(" With our mod manager: **F7**, then **Mod settings**."))]
        section = None
        for sec, key, default, about_ in sets:
            if sec != section:
                out += ["", "**%s**" % sec, "", "| Setting | Default | What it does |", "|---|---|---|"]
                section = sec
            out.append("| `%s` | `%s` | %s |" % (key, default.replace("|", "\\|"), about_.replace("|", "\\|")))
        out += [""]

    # Thunderstore asks mods made with AI to say so (and so do we)
    out += ["## 🤖 Made with AI", "", "Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.", ""]

    out += ["## 💬 Join us on Discord", ""]
    if os.path.exists(BANNER_FILE):
        out += ['<a href="%s"><img src="%s" alt="Join the Quads Lab Discord" width="100%%"></a>' % (DISCORD, BANNER), ""]
    out += [(read(COMMUNITY) or "").strip(), "", "**[Join the Quads Lab Discord →](%s)**" % DISCORD, ""]
    out += ["## 🧪 About Quads Lab", "", about.strip(), ""]

    out += ["## ☕ Support my mods", ""]
    if os.path.exists(SUPPORT_BANNER_FILE):
        out += ['<a href="%s"><img src="%s" alt="Support my mods: buy me a coffee" width="100%%"></a>' % (SUPPORT, SUPPORT_BANNER), ""]
    out += [(read(SUPPORT_WORDS) or "").strip(), "", "**[☕ Buy me a coffee →](%s)**" % SUPPORT, ""]

    if log.strip():
        out += ["## 📜 Recent changes", ""]
        for p in paragraphs(log)[:3]:
            p = re.sub(r"\s*\n\s*", " ", p)
            mv = re.match(r"^(\d+\.\d+(?:\.\d+)?):\s*(.*)", p, re.S)
            out.append("- **%s** %s" % (mv.group(1), mv.group(2)) if mv else "- " + p)
        out += ["", github_only("[Every change](CHANGELOG.txt)") + thunderstore_only("Every change is on the Changelog tab."), ""]

    text = "\n".join(out).rstrip() + "\n"
    io.open(os.path.join(d, "README.md"), "w", encoding="utf-8", newline="\n").write(text)
    return name


def main():
    cards, keys, who, default_who = readme_parts()
    about = read(ABOUT) or ""
    want = [a for a in sys.argv[1:] if not a.startswith("--")]
    done = []
    for folder in sorted(os.listdir(MODS)):
        if want and folder not in want:
            continue
        if not os.path.exists(os.path.join(MODS, folder, folder + ".csproj")):
            continue
        done.append(page(folder, cards, keys, who, default_who, about))
    print("pages written:", ", ".join(done))


if __name__ == "__main__":
    main()
