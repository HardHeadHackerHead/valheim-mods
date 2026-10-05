# valheim-mods

My Valheim mods (BepInEx + Harmony, C#). This is a **mod repo**: a folder per mod plus a built `dist/` that the in-game
[mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) reads. The manager already knows about this repo, so anyone running it can see and install these mods (press **F7**).

| Folder | What |
|---|---|
| `mods/CraftFromChests` | Crafting stations use materials from nearby chests. |
| `mods/BuildFromChests` | Building uses materials from nearby chests. |
| `mods/FeedFromChests` | Feed smelters, kilns, cooking racks, fires and fermenters from chests. |
| `mods/QualityOfLife` | Quick sets, hammer key, stack to chests, chest assignment, item locks. |
| `mods/BuildOrders` | Shared ghost build orders. |
| `mods/PartyHud` | Party panel with Steam avatars. |
| `mods/Recycler` | A buildable Recycler (and Press) that returns a share of the materials of old gear. |
| `dist/` | **Built** DLLs and `manifest.json`. Produced by `publish.ps1`. |
| `bridge/` | A prebuilt copy of the mod manager so older managers can update to the new one. Delete once nobody needs it. |

## Install
Get the mod manager and install from its repo: https://github.com/HardHeadHackerHead/valheim-mod-manager (it has a one-step installer for new players).

## Release flow
```powershell
.\publish.ps1                 # builds every mod, fills dist/ and writes dist/manifest.json (bump each mod's Version first)
git add -A; git commit -m "Update mods"; git push
```
Players press **F7** in-game and get the update with no restart. A mod that cannot be reloaded in-game gets a `RESTART_REQUIRED.txt` in its folder (the text says why).

## Develop
- `dotnet build -c Release` inside a mod folder builds it and copies it to `Valheim\BepInEx\scripts`; press **F6** in-game to reload.
- Game path is set once in `mods/Directory.Build.props`.

## Want your own mod repo?
Copy the `template/` folder from the mod manager repo. It is the same layout as this one. You can ask for it to be watched by everyone by default with a pull request that adds it to `sources.json` there.
