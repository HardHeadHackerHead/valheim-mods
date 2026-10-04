# valheim-mods

Our Valheim mods (BepInEx + Harmony, C#).

| Folder | What |
|---|---|
| `mods/CraftFromChests` | Crafting stations use materials from nearby chests (range button, optional lines to chests). |
| `mods/ModUpdater` | Press F7 in-game to pull the latest mod DLLs from this repo and hot-reload them. |
| `dist/` | **Built** mod DLLs that players download. Produced by `publish.ps1`. |
| `installer/` | One-time setup for a new player: `INSTALL.md` (hand to their AI agent), `install.ps1`, and `ModUpdater.dll`. |

## Release flow (owner)
```powershell
.\publish.ps1                 # builds every mod, fills dist/ and installer/ModUpdater.dll
git add -A; git commit -m "Update mods"; git push
```
Players press **F7** in-game and get the update with no restart.

## Develop
- `dotnet build -c Release` inside a mod folder builds and copies it to `Valheim\BepInEx\scripts`; press **F6** in-game to reload.
- Game path is set once in `mods/Directory.Build.props`.

## Adding a new player
1. Create a fine-grained GitHub token: **only this repo**, **Contents: Read-only**. Send it to them privately.
2. Send them the prompt in `installer/INSTALL.md`'s spirit: *"Follow the instructions in installer/INSTALL.md of HardHeadHackerHead/valheim-mods"* plus the token. Their agent does the rest.
   (Or just send them `installer/INSTALL.md` and `installer/install.ps1`.)

The ModUpdater is intentionally **not** in `dist/`: it's installed once and shouldn't overwrite itself.
