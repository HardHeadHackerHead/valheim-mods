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

## How the manager signs in to GitHub
- **Public repo:** no login at all (GitHub allows 60 anonymous requests per hour, plenty for update checks).
- **Private repo:** it uses **only** the read-only `Token` from its config (or one pasted into the window's "Connect to GitHub" panel), shown only if GitHub refuses the anonymous request.
- It will use the GitHub CLI (`gh`) login **only if explicitly allowed** (`AllowGitHubCli = true`, or the "Allow GitHub CLI login" button/toggle), because that login has much broader access than one read-only token. Otherwise `gh` is never run.

## Adding a new player
1. Public repo: nothing to prepare. (Private repo: create a fine-grained token, **only this repo**, **Contents: Read-only**, and send it privately.)
2. Send them the prompt in `installer/INSTALL.md`'s spirit: *"Follow the instructions in installer/INSTALL.md of HardHeadHackerHead/valheim-mods"* plus the token. Their agent does the rest.
   (Or just send them `installer/INSTALL.md` and `installer/install.ps1`.)

The manager lives in `BepInEx\scripts` like every other mod, so it hot-reloads and updates itself. If a bad
manager build ever breaks F7, re-run `installer/install.ps1` to recover.
On the machine that builds the mods, set `DeveloperMode = true` in `com.dhack.modupdater.cfg` so the manager
never overwrites your own builds.

## Sharing mods from more than one repo
The manager reads "feeds": a GitHub folder with `manifest.json` plus the DLL/PDB files that `publish.ps1` makes. Ours is the main feed; anyone can add more under **Mod sources** in the window (or `ExtraFeeds` in the config, `owner/repo;owner/repo@branch:folder`). Mods from extra feeds are never installed automatically, and our feed wins if two feeds ship the same mod GUID.
- To publish your own mods, copy the `template/` folder into a new repo (see `template/README.md`).
- A mod that can't be hot-reloaded gets a `RESTART_REQUIRED.txt` in its folder (the text says why). The manager then shows "Restart the game" instead of reloading it.
