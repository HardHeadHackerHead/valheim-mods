# Ideas: tools that would help build mods

Saved 2026-10-06 to come back to later. Nothing here is built yet. Each idea comes from something that slowed things down or forced
guessing while building BuildOrders, ClaudeTools and the rest.

Suggested order: start with **1** and **3** (cheap, and they speed up everything else).

## 1. Searchable game source

A one-time script that decompiles all of `assembly_valheim.dll` (with `ilspycmd`, already installed) into a folder, so the game's code can
be searched instantly instead of decompiling one class at a time.

Why: most bugs this session came from guessing how the game works (the hoe's "level ground" is a smoothing stroke capped at 1 m; how smoke
escapes; when a fire goes out; what a bed needs).

## 2. Inspect anything in the game (a ClaudeTools command)

`inspect <prefab>` or `inspect looking`: every component on a piece, item or creature and all its settings, read from the live game.

Why: the smoke spawner height, the fire's 8 m warmth radius and the hoe's real settings were dug out of the game files with throwaway
Python (UnityPy) scripts. The live game gives exact values faster.

## 3. Errors since the last check (ClaudeTools commands)

- `errors`: only the new exceptions since the last check, with stack traces.
- `waitfor <mod> <version>`: wait until a rebuilt mod has really hot-reloaded.

Why: after every rebuild the log was searched by hand, and a silent failure ("the game has no Level ground piece") went unnoticed until
the player reported it.

## 4. A fake second player

A loopback that delivers a mod's own network messages back to the local player as if another player had sent them (a general version of
BuildOrders' `selfshare`).

Why: Share, BountyBoard group contracts, MapShare and ghost syncing could not be tested without a friend online.

## 5. Test-setup commands (behind their own permission switch, off by default)

- Set the time of day or the weather (smoke at night, a fire going out in rain).
- Spawn a creature (to test a bounty).
- Give test materials (instead of spending the player's).
- Turn free building on or off.

These are cheaty, so they would need their own setting, like `AllowConfigChanges`.

## 6. Release checker

A script run before committing that:

- confirms only mods whose source changed have changed `dist/` files, and restores the rest from the last commit;
- warns when a changed mod's version was not bumped or its CHANGELOG not updated;
- runs the blueprint tool tests.

Why: all of this is done by hand today, and it is easy to slip.

## Smaller ones

- **Item and piece lookup**: `find wood` returns prefab names and `$item_wood` tokens.
- **Performance check**: frame time with and without a mod's features (for example 600 ghosts on screen).
- **New-mod scaffold**: create the `.csproj`, `Plugin.cs`, `DESCRIPTION.txt` and `CHANGELOG.txt` in this repo's conventions in one step.
