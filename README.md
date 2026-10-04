# valheim-mods

Our Valheim mods (BepInEx + Harmony, C#).

| Folder | What |
|---|---|
| `mods/CraftFromChests` | Crafting stations use materials from nearby chests (range button, optional lines to chests). |
| `mods/ModUpdater` | The in-game mod manager (F7): installed vs. latest versions, other players' versions, one-click updates with hot reload. Updates itself. |
| `dist/` | **Built** mod DLLs (including the manager) and `manifest.json`. Produced by `publish.ps1`. |
| `installer/` | One-time setup for a new player: `INSTALL.md` (hand to their AI agent) and `install.ps1`. |

## Release flow (owner)
```powershell
.\publish.ps1                 # builds every mod, fills dist/ and writes dist/manifest.json (bump each mod's Version first)
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

The manager lives in `BepInEx\scripts` like every other mod, so it hot-reloads and updates itself. If a bad
manager build ever breaks F7, re-run `installer/install.ps1` to recover.
On the machine that builds the mods, set `DeveloperMode = true` in `com.dhack.modupdater.cfg` so the manager
never overwrites your own builds.
