---
name: valheim-fix-errors
description: Find out why Valheim or a mod is broken from the BepInEx log: read errors and stack traces, tell which mod and patch caused it, spot mod clashes and game-update breakage, and fix it or report it to the right author. Use when a player says something stopped working, the game errors, or after a game update.
---

# Fixing a broken mod setup

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (`modkit help` lists them). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

## 1. Get the errors

With the game running (request file in `BepInEx/claude/requests`, or `claude <command>` in the console; works from the main menu):

```
errors                 the new errors and exceptions since you last asked, with stack traces
log 300 <text>         the last lines of the log containing some text (e.g. log 300 Missing prefab)
mods                   the mods running, with versions
```

Without the game: `BepInEx/LogOutput.log` (this session) in the Valheim folder.

## 2. Read the stack trace

- The top frames name the code that failed. A mod's own frames start with its namespace (`AICompanion.Brain.Update`).
- `DMD<Player::Update>` or `(wrapper dynamic-method) Player.DMD<...>` means **the game's method as rewritten by Harmony**: the failure
  is inside a patched method, in one of the patches on it. `who Player.Update` lists them; the mods whose frames appear are the suspects.
- `MissingMethodException` / `MissingFieldException` / `TypeLoadException` naming a game type: the game changed under a mod (step 4).
- `NullReferenceException` in a patch: it ran where it didn't expect (main menu, dedicated server, another mod's object, a hot reload).

## 2b. Messages that point straight at the cause

| In the log or on screen | Means |
|---|---|
| `Missing prefab hash` / `Destroyed invalid prefab ZDO` | A mod's piece or item isn't registered (mod missing, or registered too late): placed ones are being deleted. Stop and fix before playing on. |
| A connection refused for a mod version / "incompatible version" | Server and player have different versions of a mod that checks (ServerSync, Jotunn): update both to the same one. |
| `Expected an embedded resource translations/English...` | A mod using LocalizationManager was built without its English file. |
| An AssetBundle "already loaded" | A mod loaded its bundle twice (usually after a hot reload): restart the game; the mod should reuse a loaded bundle. |
| Every mod stops working after one is updated or reloaded | Possibly `UnpatchAll()` with no id in that mod (`modcheck <mod>` finds it). |

## 3. Clashes between mods

`clashes` lists the installed mods that change the same methods or do the same job, most likely first; `library/clashes.json` has the
last report (ClaudeTools makes one after each launch and logs a one-line summary). If the error is in a method two mods patch, read both
patches (**valheim-compat**: `library get` and `ilspycmd`) before blaming either. Typical signs:

- Things happen twice, or crafts cost half: two mods doing one job (counting chests, adding rows, handling one key).
- A feature of one mod does nothing: another mod's prefix always skips that method (`skips: always`).
- Items vanish on spawn or death: an inventory-size or tombstone clash.

Then go through **When there's a clash** in **valheim-compat**: confirm it in the code, fix your side, work with the other mod, or report it.

## 4. After a game update

`gameupdate` lists the game methods gone, changed signature or changed code since the version before, and which installed and
popular mods patch them (`gone` breaks the patch outright; transpilers on `codeChanged` methods often break). ClaudeTools logs a summary on
the first launch after an update. Those mods need an update from their authors.

## 5. Fix it, or report it

- **Your own mod**: fix it, then `modcheck <mod>` and a real restart (see **valheim-prerelease**).
- **Someone else's**: don't patch their DLL. Tell the player which mod and why, and write the author a report: the mod versions,
  the game version, the error with its stack trace, the other mod involved and the method you both patch, and steps to reproduce. Mod
  pages are listed in `library/index.json` (`page` for each mod).
- **Two mods that can't work together**: the player can turn one off; the authors can detect each other (see **valheim-compat**).
