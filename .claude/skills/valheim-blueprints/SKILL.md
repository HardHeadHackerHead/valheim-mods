---
name: valheim-blueprints
description: Design a Valheim build (a fort, house, tower, bridge, wall...) as a blueprint file for the BuildOrders mod. Use when the player asks you to build or design something in Valheim, or to check whether a design will stand.
---

Read `tools/blueprints/CLAUDE.md` in this repo (or `CLAUDE.md` in the player's `BepInEx/blueprints` folder) and follow it. It explains the
blueprint format, the piece list (`_pieces.json`), the coordinate conventions, the structural support rules and the helper scripts
(`tools/blueprint.py`, `tools/stability.py`, `tools/fort.py`). Always read the real piece list instead of guessing names or sizes, and always
run the stability check before handing a design over.
