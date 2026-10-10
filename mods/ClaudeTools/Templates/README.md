# Templates

Code to copy into a mod (change the namespace). Each one is the version our own mods run, after the bugs in the skills were fixed.

| File | What it does |
|---|---|
| `ServerSettings.cs` | Settings the server decides in multiplayer: mark gameplay settings with `Add`, and a player joining a server with the mod plays by the server's values (their config file keeps their own). No library to install; uses the game's `ZRoutedRpc`. For more (locking settings for admins, a version check), use ServerSync instead: https://github.com/blaxxun-boop/ServerSync |
| `GameUi.cs` | Windows in the game's own look, cloned from the inventory screen: wood panel, sunken boxes, buttons, the braided title line, the crafting screen's tabs, the inventory's own item cells (icons, durability, quality, tooltips, clicks), compact item chips with tooltips, progress bars, icons, a scrolling list with the game's scrollbar and scroll speed, a place on the HUD, the game's fonts and colours, a canvas that follows the player's interface scale, and the input patches every window needs (no walking or zooming underneath it, a free cursor, Escape closes only it). Its comment shows a whole window. The Recycler's and the Bounty Board's windows are built with it. |
| `PieceReload.cs` | Lets a mod with build pieces be reloaded while you play (`reload YourMod`) without losing pieces or leaving placed ones on the old code: the piece stays registered across the reload, and pieces standing in the world get the new copy's components. Pieces only (items in bags still need the main menu). |
| `ContainerAccess.cs` | Taking from or putting into chests the player didn't open, safely in multiplayer: only built chests (not graves, carts or creatures), not one someone has open, ward and lock respected, taken over and reloaded before touching it. |

The full mod template (project file, game references, `publish.ps1` for sharing through the mod manager) is `template/` in
https://github.com/HardHeadHackerHead/valheim-mod-manager.
