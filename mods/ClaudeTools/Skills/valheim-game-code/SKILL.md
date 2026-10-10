---
name: valheim-game-code
description: Look up Valheim's real code (types, methods, fields, signatures, who calls what) before writing or fixing a mod, instead of guessing names. Use whenever mod code touches a game class (Player, Inventory, ItemDrop, ZDO, Piece, InventoryGui...) or a patch target is needed.
---

# Reading the game's code

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (the command `help` lists them all). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

Most broken AI-written mods call methods that don't exist, with the wrong arguments, or patch a method that does something else. The
game's code is on the player's disk: read it.

## Quick lookups (the game is running, Claude Tools installed)

Put these in a request file (`BepInEx/claude/requests/<name>.txt`; they also work from the main menu) or type `claude <command>` in the
game's console (F5, once Settings → Gameplay → "Enable console" is on):

| Command | Gives |
|---|---|
| `game find <text>` | Types, methods and fields whose name contains the text. |
| `game <Type>` | The type's fields and method signatures (e.g. `game Inventory`, `game ItemDrop.ItemData`). |
| `game <Type.Method>` | Every overload's signature, who calls it, what it calls, its game system, which popular mods patch it. |
| `game <Type.field>` | Which game methods change the field and which read it (`game Inventory.m_height`). |
| `who <Type.Method>` | Which installed and popular mods patch it, and how. |
| `prefabs <text>` | The exact names of the game's (and mods') prefabs containing the text (in a world): `prefabs torch`, `prefabs chest`. |

## Reading the full code

```
ilspycmd -t Player "<Valheim>/valheim_Data/Managed/assembly_valheim.dll"
ilspycmd -t ItemDrop+ItemData "<Valheim>/valheim_Data/Managed/assembly_valheim.dll"   (nested types use +)
```

Keys and input are in `assembly_utils.dll` (`ZInput`), some UI in `assembly_guiutils.dll`. `game <Type>` prints the exact command.
Decompile into a scratch folder, not into the mod's project (it would be compiled in).

## How the game is built (what to look up first)

- **Saved state lives in ZDOs**, one per networked object (`ZNetView.GetZDO()`), as key/value pairs (`ZDOVars.s_*` are the game's keys).
  Only the **owner** (`ZNetView.IsOwner()`) should write; others ask the owner with an RPC (`ZNetView.InvokeRPC`, `ZRoutedRpc`).
- **Prefabs**: `ZNetScene` holds every networked prefab by name (placed pieces are saved by name), `ObjectDB` holds items, recipes and
  status effects. Mods register their own in both (see **valheim-pitfalls**).
- **Items**: `ItemDrop.ItemData` (one stack), its `m_shared` (the same for every copy: name, type, food, armour) and `m_customData`
  (a dictionary shared by every mod, saved with the item). `Inventory` is a grid of them.
- **The player**: `Player.m_localPlayer` (null at the main menu), `Humanoid` for equipment, `Character` for health and damage.
- **Crafting and building costs**: `Player.HaveRequirements`, `HaveRequirementItems`, `ConsumeResources`, `Recipe.m_resources`
  (whose entries flagged `m_upgraderResource` are only charged at an upgrader station).
- **UI**: `InventoryGui` (inventory, crafting, containers), `Hud`, `Minimap`, `MessageHud`.

## Where the game does things in an order you wouldn't guess

Read these before changing them; each has cost players items in a released mod:

| What | The order |
|---|---|
| Crafting | `InventoryGui.DoCrafting` gives the item (`AddItem`) **before** `Player.ConsumeResources` charges; "any one of these" recipes pay with `RemoveItem` instead. Refuse in a prefix on `DoCrafting`. |
| Building | `Player.TryPlacePiece` places the piece **before** `ConsumeResources`. |
| Spawning | `Player.OnSpawned` → `SetInventorySize` → `Humanoid.DropInvalidItems` drops everything outside the grid (**valheim-pitfalls**). |
| Recipe cost | `Recipe.m_resources` includes battle idols (`m_upgraderResource`), charged only at an upgrader; `Player.HaveRequirementItems` filters them. |
| Chests | `Container.CheckForChanges` (every second) calls `Load` whenever the ZDO's revision changes; `IsInUse()` is only right on the owner (the saved flag is `ZDOVars.s_inUse`, an int). |
| Objects | Only the owner may destroy one (`ZDOMan.DestroyZDO`); a host knows every object in the world (`ZDOMan.GetAllZDOsWithPrefabIterative`), a client only those near the players. |
| Selling | Any item with `m_shared.m_value > 0` sells at the trader. |
| Cheats | Allowed in single player and for the host (`ZNet.instance.IsServer()`), otherwise with devcommands (`Console.instance.IsCheatsEnabled()`). |

## When the game updates

Names and code change. `gameupdate` lists the methods that are gone, changed signature or changed code since the version seen before,
and which mods patch them; ClaudeTools logs a summary on the first launch after an update. Read the new code of each changed method a
mod patches.
