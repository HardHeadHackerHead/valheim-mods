# valhiem

Before writing or changing a mod, read the toolkit's `E:\valheim-claude-tools\docs\modding-pitfalls.md` (bugs that lost players' items
and buildings, and how to avoid them; the valheim-pitfalls skill has the same). It is the only copy: add new pitfalls there.

## Where things live (four repos side by side)

| Folder | Repo | What's in it |
|---|---|---|
| `E:\valhiem` (here) | [valheim-mods](https://github.com/HardHeadHackerHead/valheim-mods) | Our mods (`mods/<Mod>`), shared code compiled into them (`mods/Shared`), `dist/` (the F7 feed), the mod pages (`tools/modpages`), translations check, model and blueprint scripts. |
| `E:\valheim-claude-tools` | [valheim-claude-tools](https://github.com/HardHeadHackerHead/valheim-claude-tools) | The toolkit: the Claude Tools mod, the skills (`skills/`), templates (`templates/`, with `project/`), modkit, PatchMap, pitfalls. **Change Claude Tools, a skill or a template there**, then release it from there (its own `dist/` and `publish.ps1`). |
| `E:\valhiem-mod-manager` | [valheim-mod-manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) | The F7 manager (ModUpdater), `sources.json` (approved sources: Claude Tools is one), `template/` (kept the same as the toolkit's `templates/project` by its `tools/sync_project_template.py`). |
| `E:\valheim-ragnarok` | [valheim-ragnarok](https://github.com/HardHeadHackerHead/valheim-ragnarok) (**private**, shared with Bob) | Quad's Ragnarök (working title), the roguelite: the design ideas (`docs/design.md`), later the private server mod and the Well. **Never copy its contents into the public repos.** |

Releasing on the mod sites: `.\thunderstore.ps1 -Mods <Mod> -Publish` uploads the same checked zip to Thunderstore (tcli) and Hexium
(`tools/hexium_publish.py`, which maps categories to Hexium's own); `-Sites hexium` for one site. Tokens are user environment variables,
never files: `TCLI_AUTH_TOKEN` and `HEXIUM_AUTH_TOKEN` (both for the Quads_Lab team).

Sessions here can read and edit the other three without asking (`additionalDirectories` in `.claude/settings.local.json`). Claude Tools'
in-game guide (every command) is `mods/ClaudeTools/CLAUDE.md` in the toolkit, and `BepInEx/claude/CLAUDE.md` in the game.
