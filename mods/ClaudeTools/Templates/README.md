# Templates

Code to copy into a mod (change the namespace). Each one is the version our own mods run, after the bugs in the skills were fixed.

| File | What it does |
|---|---|
| `ServerSettings.cs` | Settings the server decides in multiplayer: mark gameplay settings with `Add`, and a player joining a server with the mod plays by the server's values (their config file keeps their own). No library to install; uses the game's `ZRoutedRpc`. For more (locking settings for admins, a version check), use ServerSync instead: https://github.com/blaxxun-boop/ServerSync |
| `ContainerAccess.cs` | Taking from or putting into chests the player didn't open, safely in multiplayer: only built chests (not graves, carts or creatures), not one someone has open, ward and lock respected, taken over and reloaded before touching it. |

The full mod template (project file, game references, `publish.ps1` for sharing through the mod manager) is `template/` in
https://github.com/HardHeadHackerHead/valheim-mod-manager.
