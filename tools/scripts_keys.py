#!/usr/bin/env python3
"""Writes mods/Blockscript/translations/English.json from the code: every L("key", "English words") in the editor, the English() table in
EditorScreen.cs, and the label of every block (read from the block tests project, which links the definitions). Run it after changing any
words in mods/Scripts.Engine, then tools/check_translations.py."""
import json, os, re, subprocess, sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
engine = os.path.join(root, "mods", "Scripts.Engine")
out = os.path.join(root, "mods", "Blockscript", "translations", "English.json")
keys = {}

BACKSLASH = chr(92)
QUOTE = chr(34)
BS = re.escape(BACKSLASH)
STR = QUOTE + "((?:[^" + QUOTE + BS + "]|" + BS + ".)*)" + QUOTE   # a C# string literal's inside


def unescape(s):
    return s.replace(BACKSLASH + QUOTE, QUOTE).replace(BACKSLASH * 2, BACKSLASH).replace(BACKSLASH + "n", "\n")


call = re.compile(r"\bL\(\s*" + QUOTE + "([a-z0-9_]+)" + QUOTE + r"\s*,\s*" + STR)
table = re.compile(r"case\s+" + QUOTE + "(scripts_[a-z0-9_]+)" + QUOTE + r"\s*:\s*return\s*" + STR + r"\s*;")

for folder, _, files in os.walk(engine):
    for f in files:
        if not f.endswith(".cs") or f == "GameUi.cs" or os.sep + "obj" in folder or os.sep + "bin" in folder:
            continue
        text = open(os.path.join(folder, f), encoding="utf-8").read()
        for m in call.finditer(text):
            keys[m.group(1)] = unescape(m.group(2))
        for m in table.finditer(text):
            keys[m.group(1)] = unescape(m.group(2))

labels = subprocess.run(["dotnet", "run", "--project", os.path.join(root, "tests", "Scripts.Blocks.Tests"), "--", "labels"],
                        capture_output=True, text=True)
if labels.returncode != 0:
    print(labels.stdout + labels.stderr)
    sys.exit(1)
text = labels.stdout[labels.stdout.index("{"):]
for key, value in json.loads(text).items():
    # a block's words always come from its definition; a tab's or heading's name only when the code did not already give one
    if key.startswith("scripts_block_"):
        keys[key] = value
    else:
        keys.setdefault(key, value)

os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(dict(sorted(keys.items())), f, ensure_ascii=False, indent=2)
    f.write("\n")
print(str(len(keys)) + " keys -> " + os.path.relpath(out, root))
