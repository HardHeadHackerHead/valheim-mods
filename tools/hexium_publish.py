"""Publishes our Thunderstore zips on Hexium (https://hexium.gg), the second mod site (Gale installs from it).

The same zip goes to both sites: thunderstore.ps1 builds and checks it, then uploads it to Thunderstore with tcli and here with this
script. Hexium's API is Thunderstore's upload protocol (upload the zip in parts, then submit it), but its categories are its own
("Quality of Life", "Storage"...), so they are made from the mod's Thunderstore categories by the map below, plus EXTRA for the ones
Thunderstore has no word for. After the upload it sets where the mod is installed (client, server or both) from client-side/server-side.

    python tools/hexium_publish.py CraftFromChests                 show what would go up (nothing is sent)
    python tools/hexium_publish.py CraftFromChests --publish       upload mods/CraftFromChests/thunderstore-build/<package>-<version>.zip
    python tools/hexium_publish.py CraftFromChests --metadata      only set the categories and install location of what is there

Needs HEXIUM_AUTH_TOKEN: the Quads_Lab team's API token (hexium.gg > your team's settings > API token), kept in an environment
variable, never in a file. A version already on Hexium is skipped. A version can't be uploaded twice, like on Thunderstore.
"""
import argparse
import json
import os
import sys
import time
import tomllib
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlparse

SITE = "https://hexium.gg"
COMMUNITY = "valheim"
ROOT = Path(__file__).resolve().parent.parent

# Thunderstore category -> Hexium's (None: Hexium has no such filter). Hexium's list: GET /api/experimental/community/valheim/category/
CATEGORY_MAP = {
    "building": "Building", "crafting": "Crafting", "gear": "Equipment", "enemies": "Enemies", "npcs": "Npc",
    "world-generation": "World Generation", "transportation": "Transportation", "vehicles": "Vehicles", "pvp": "PvP",
    "audio": "Audio", "skins": "Cosmetics", "tweaks": "Quality of Life", "utility": "Quality of Life", "tools": "Tools",
    "language": "Language",
    "mods": None, "misc": None, "libraries": None, "ai-generated": None, "client-side": None, "server-side": None,
    "deep-north-update": None,
}
ALWAYS = ["Open Source"]  # every mod's source is on GitHub (websiteUrl)
# Hexium filters Thunderstore has no word for, per package name
EXTRA = {
    "Quads_Craft_From_Chests": ["Storage"],
    "Quads_Build_From_Chests": ["Storage"],
    "Quads_Feed_From_Chests": ["Storage"],
    "Quads_Ledger_Chest": ["Storage", "Pieces"],
    "Quads_Party_HUD": ["User Interface"],
    "Quads_Crafting_Filters": ["User Interface"],
    "Quads_Cigars": ["Consumables", "Pieces"],
    "Quads_Skal_Tavern": ["Consumables", "Pieces"],
    "Quads_Rainbows": ["Visuals"],
    "Quads_Parkour": ["Mechanics"],
    "Quads_Arena": ["Combat", "Progression"],
    "Quads_Companion": ["Combat"],
    "Quads_Bounty_Board": ["Pieces", "Progression", "Currency"],
    "Quads_Slot_Machine": ["Pieces", "Currency"],
    "Quads_Ziplines": ["Pieces"],
    "Quads_Bird_Trap": ["Pieces", "Farming"],
    "Quads_Portal_Hub": ["User Interface"],
    "Quads_Recycler": ["Pieces"],
    "Quads_Jet_Skis": ["Storage"],
    "Quads_Map_Share": ["Networking"],
    "Quads_Quiver": ["Weapons"],
    "Quads_Gear_Slots": ["User Interface", "Storage"],
}


def fail(msg):
    print("hexium: " + msg, file=sys.stderr)
    sys.exit(1)


def call(method, url, token=None, body=None, raw=None, headers=None):
    """One request. Returns (status, parsed JSON or text, response headers)."""
    h = dict(headers or {})
    data = raw
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        h["Content-Type"] = "application/json"
    if token and urlparse(url).netloc == urlparse(SITE).netloc:  # the token only ever goes to Hexium itself
        h["Authorization"] = "Bearer " + token
    req = urllib.request.Request(url, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            status, text, rh = r.status, r.read().decode("utf-8", "replace"), r.headers
    except urllib.error.HTTPError as e:
        status, text, rh = e.code, e.read().decode("utf-8", "replace"), e.headers
    try:
        return status, json.loads(text), rh
    except ValueError:
        return status, text, rh


def read_mod(mod):
    toml_path = ROOT / "mods" / mod / "thunderstore.toml"
    if not toml_path.exists():
        fail(f"no {toml_path} (run thunderstore.ps1 -Mods {mod} first)")
    t = tomllib.loads(toml_path.read_text(encoding="utf-8"))
    p = t["package"]
    ts_cats = t.get("publish", {}).get("categories", {}).get(COMMUNITY, [])
    unknown = [c for c in ts_cats if c not in CATEGORY_MAP]
    if unknown:
        fail(f"no Hexium category for {unknown}: add them to CATEGORY_MAP in {Path(__file__).name}")
    cats = []
    for c in [CATEGORY_MAP[c] for c in ts_cats] + EXTRA.get(p["name"], []) + ALWAYS:
        if c and c not in cats:
            cats.append(c)
    client, server = "client-side" in ts_cats, "server-side" in ts_cats
    where = "both" if client and server else "server" if server else "client"
    zip_path = ROOT / "mods" / mod / "thunderstore-build" / f"{p['namespace']}-{p['name']}-{p['versionNumber']}.zip"
    return {"namespace": p["namespace"], "name": p["name"], "version": p["versionNumber"], "categories": cats,
            "install_location": where, "zip": zip_path}


def on_hexium(m):
    s, _, _ = call("GET", f"{SITE}/api/experimental/package/{m['namespace']}/{m['name']}/{m['version']}/")
    return s == 200


def hexium_zip(path):
    """The zip as Hexium gets it: the same files, but the README's links to our mods' Thunderstore pages go to their Hexium pages
    (a player browsing Hexium stays there; the pages' "Goes well with" icons link to each mod). Nothing else in it changes."""
    import io, re, zipfile
    src = zipfile.ZipFile(path)
    out = io.BytesIO()
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as dst:
        for info in src.infolist():
            data = src.read(info.filename)
            if info.filename.lower() == "readme.md":
                text = data.decode("utf-8")
                text = re.sub(r"https://thunderstore\.io/c/valheim/p/Quads_Lab/([A-Za-z0-9_]+)/?", r"https://valheim.hexium.gg/mods/Quads_Lab/\1", text)
                data = text.encode("utf-8")
            dst.writestr(info, data)
    return out.getvalue()


def send_file(m, token):
    """Uploads the zip in the parts Hexium asks for (with its README's links made Hexium's: hexium_zip). Returns its upload id."""
    data = hexium_zip(m["zip"])
    s, r, _ = call("POST", f"{SITE}/api/experimental/usermedia/initiate-upload/", token,
                   {"filename": m["zip"].name, "file_size_bytes": len(data)})
    if s not in (200, 201):
        fail(f"initiate-upload: {s} {r}")
    uuid = r["user_media"]["uuid"]
    parts = []
    try:
        for part in r["upload_urls"]:
            chunk = data[part["offset"]:part["offset"] + part["length"]]
            s, body, rh = call("PUT", part["url"], token, raw=chunk)
            if s not in (200, 201, 204) or not rh.get("ETag"):
                fail(f"upload part {part['part_number']}: {s} {body}")
            parts.append({"ETag": rh["ETag"], "PartNumber": part["part_number"]})
        s, r, _ = call("POST", f"{SITE}/api/experimental/usermedia/{uuid}/finish-upload/", token, {"parts": parts})
        if s not in (200, 201):
            fail(f"finish-upload: {s} {r}")
    except SystemExit:
        call("POST", f"{SITE}/api/experimental/usermedia/{uuid}/abort-upload/", token, {})
        raise
    return uuid


# Hexium's upload limit (measured 2026-10-11): 10 accepted uploads per 10 minutes, then submit answers 400 "You are uploading too many
# mods. Please wait a few minutes." until the oldest of those 10 is 10 minutes old. Refused tries don't count toward it. A refused
# submit uses up the uploaded file ("Upload is not complete" if submitted again), so a retry sends the file again.
LIMIT, WINDOW = 10, 600
accepted = []  # times of this run's accepted uploads


def upload(m, token):
    recent = [t for t in accepted if time.time() - t < WINDOW]
    if len(recent) >= LIMIT:  # wait for the window rather than be refused
        wait = recent[-LIMIT] + WINDOW + 10 - time.time()
        print(f"   {LIMIT} uploads in {WINDOW // 60} minutes is Hexium's limit: waiting {int(wait)} s", flush=True)
        time.sleep(max(0, wait))
    for attempt in range(15):  # refused anyway: uploads from an earlier run still in the window; a minute each, 15 at most
        uuid = send_file(m, token)
        s, r, _ = call("POST", f"{SITE}/api/experimental/submission/submit/", token, {
            "upload_uuid": uuid, "author_name": m["namespace"], "communities": [COMMUNITY], "has_nsfw_content": False,
            "categories": [], "community_categories": {COMMUNITY: m["categories"]}})
        if s in (200, 201):
            accepted.append(time.time())
            return r
        if "too many" not in json.dumps(r):
            break
        print(f"   Hexium's upload limit (uploads from before this run): trying again in a minute ({time.strftime('%H:%M')})", flush=True)
        time.sleep(60)
    fail(f"submit: {s} {r}")


def set_metadata(m, token):
    s, r, _ = call("POST", f"{SITE}/api/mod/{m['namespace']}/{m['name']}/set-metadata", token,
                   {"install_location": m["install_location"], "categories": m["categories"]})
    if s != 200:
        fail(f"set-metadata: {s} {r}")
    return r


def main():
    ap = argparse.ArgumentParser(description="Publish our Thunderstore zips on Hexium too")
    ap.add_argument("mods", nargs="+", help="mod folder names under mods/ (CraftFromChests ...)")
    ap.add_argument("--publish", action="store_true", help="upload (otherwise only shows what would go up)")
    ap.add_argument("--metadata", action="store_true", help="only set categories and install location on Hexium")
    a = ap.parse_args()
    token = os.environ.get("HEXIUM_AUTH_TOKEN")
    if (a.publish or a.metadata) and not token:
        fail("set HEXIUM_AUTH_TOKEN to the Quads_Lab team's Hexium API token first")
    for mod in a.mods:
        m = read_mod(mod)
        full = f"{m['namespace']}-{m['name']}-{m['version']}"
        print(f"== {mod}: {full}  install: {m['install_location']}  categories: {', '.join(m['categories'])}")
        if a.metadata:
            print("   metadata set:", set_metadata(m, token))
            continue
        if on_hexium(m):
            print("   already on Hexium, skipped")
            continue
        if not m["zip"].exists():
            fail(f"no {m['zip']} (thunderstore.ps1 -Mods {mod} builds it)")
        if not a.publish:
            print(f"   would upload {m['zip'].relative_to(ROOT)} ({m['zip'].stat().st_size // 1024} KB)")
            continue
        r = upload(m, token)
        urls = [c.get("url") for c in r.get("available_communities", [])]
        print(f"   uploaded{' (hidden)' if r.get('hidden') else ''}: {', '.join(u for u in urls if u)}")
        print("   metadata set:", set_metadata(m, token))


if __name__ == "__main__":
    main()
