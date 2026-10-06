# Blueprints

Blueprints let an AI assistant (or you) design a build and drop it into the game as **build-order ghosts** with the BuildOrders mod.
Nothing is built for free: the player still builds every ghost with real materials.

- **`CLAUDE.md`** is the design guide. Claude Code loads it automatically when started in this folder. BuildOrders writes the same file into
  `BepInEx/blueprints` the first time you play, so anyone who installs the mod can start an assistant there and ask for a fort.
- **`tools/`** are the helper scripts: `blueprint.py` (place pieces by snap point, box or origin, at any turn; `check()` for stations, overlaps,
  fires and roofs), `stability.py` (will it stand?), `preview.py` (four views with the game's real shapes), worked examples (`wood_fort.py`,
  `fort.py`, `selftest.py`), `test_tools.py` (tests), and `extract_pieces.py` / `extract_meshes.py` (read every piece's data and shape from
  the game files, no game needed; `pip install UnityPy`).
- With the Claude Tools mod, the assistant gets a request mailbox (`BepInEx/claude`): pictures, surveys and BuildOrders' blueprint commands, so it can see the site and
  check what it placed. See `CLAUDE.md`.

## Try it

1. Install BuildOrders 1.5 or later, start a world once (it writes `BepInEx/blueprints/_pieces.json`).
2. Start Claude Code in `BepInEx/blueprints` and ask for a build. It reads `CLAUDE.md`, designs it, checks the support, and writes a `.json` here.
3. In game, press **F11** to open the Plans window, press Place on the blueprint, and put the green preview where you want it (mouse wheel
   turns it, PgUp/PgDn raise it, click places it). The ghosts appear; build them as usual. Placed in the wrong spot? Placed plans > Move or Remove.

The repo also has a Claude Code skill (`.claude/skills/valheim-blueprints`) that points at the same guide.
