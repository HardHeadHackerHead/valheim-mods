---
name: valheim-game-code
description: Look up Valheim's real code (types, methods, fields, signatures, who calls what) before writing or fixing a mod, instead of guessing names. Use whenever mod code touches a game class (Player, Inventory, ItemDrop, ZDO, Piece, InventoryGui...) or a patch target is needed.
---

# Reading the game's code

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (`modkit help` lists them). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

Most broken AI-written mods call methods that don't exist, with the wrong arguments, or patch a method that does something else. The
game's code is on the player's disk: read it.

## Quick lookups (the game is running, Claude Tools installed)

Put these in a request file (`BepInEx/claude/requests/<name>.txt`; they also work from the main menu) or type `claude <command>` in the
game's console (F5):

| Command | Gives |
|---|---|
| `game find <text>` | Types, methods and fields whose name contains the text. |
| `game <Type>` | The type's fields and method signatures (e.g. `game Inventory`, `game ItemDrop.ItemData`). |
| `game <Type.Method>` | Every overload's signature, who calls it, what it calls, its game system, which popular mods patch it. |
| `game <Type.field>` | Which game methods change the field and which read it (`game Inventory.m_height`). |
| `who <Type.Method>` | Which installed and popular mods patch it, and how. |

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

## When the game updates

Names and code change. `gameupdate` lists the methods that are gone, changed signature or changed code since the version seen before,
and which mods patch them; ClaudeTools logs a summary on the first launch after an update. Read the new code of each changed method a
mod patches.
