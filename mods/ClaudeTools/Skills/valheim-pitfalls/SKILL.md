---
name: valheim-pitfalls
description: The Valheim modding mistakes that have cost players items and buildings, and how to avoid each. Use when writing code that registers pieces or items, changes inventories, saves data, handles death, containers, keys, recipes or crafting costs.
---

# Valheim modding pitfalls

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (the command `help` lists them all). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

Each of these lost players' things in a released mod. Most only show after a real restart or next to other mods, so hot reload hides
them. `modcheck <mod>` finds many of them automatically (see **valheim-prerelease**).

## Register pieces and items from the scene, early

A placed piece is saved by its prefab name. If the **host** loads a zone before the prefab is in `ZNetScene`, the game logs
`Missing prefab hash` then `Destroyed invalid prefab ZDO`, and the piece is gone for good. `ZNetScene.Awake` can run before
`ObjectDB.Awake`, so: get the hammer and cost items from `ZNetScene` (`scene.GetPrefab("Hammer")`), and register from all of:
- a `ZNetScene.Awake` postfix (pieces, creatures and items into `ZNetScene`),
- an `ObjectDB.Awake` postfix (items, recipes, status effects into `ObjectDB`),
- an `ObjectDB.CopyOtherDB` postfix (the main menu's copy of `ObjectDB`, which the character preview uses),
- your plugin's `Awake` when `ZNetScene.instance` / `ObjectDB.instance` already exist (after a reload).
Registering twice must be harmless (check by name first). On unload, remove only entries that still point at your prefab.

## The player's inventory height is reset at every spawn

`Player.OnSpawned` calls `Player.SetInventorySize(rows)`, which sets the height and then `Humanoid.DropInvalidItems()` drops everything
below it. **A postfix is too late** (the items are already on the ground), and **taking the method over** (a prefix returning false)
breaks every other mod that changes the height (ValheimPlus' extra rows, quivers, slot mods) and drops their items instead. What works,
as the popular slot mods do it:
- a prefix on `SetInventorySize` that **doesn't skip** it, only notes where your rows are;
- a `[HarmonyPriority(Priority.First)]` prefix on `Humanoid.DropInvalidItems` (the game calls it only from there) that takes the height
  the game and other mods just set, moves your rows' items to just below it, and raises the height to fit them, so nothing is invalid;
- a `Priority.Last` postfix that raises the height again if another mod lowered it;
- your rows' position saved in `Player.m_customData` (e.g. `"YourMod.Base"`), so `Player.Load` can tell other mods' rows from yours, and
  `Math.Max(height, yourBase + yourRows)`, never an exact height.
And keep a list of the slot mods that use the same cells below the normal rows (ExtraSlots, EquipmentAndQuickSlots, AzuExtendedPlayerInventory,
ComfyQuickSlots, BetterArchery's quiver): when one is installed, stand down and move your rows' items into the bag.

## Never save a ZDOID

Every world load renumbers objects: a saved `ZDOID` points at an unrelated object after a restart. Store your own random id on the
object, or use `ZDO.SetConnection` (re-linked by the game).

## Fill a tombstone with the game's own move

A grave's container is small until filled: `Inventory.MoveInventoryToGrave(original)` resizes it and moves everything; `AddItem` one by
one loses what doesn't fit. Equipped items are skipped: unequip and clear `m_equipped` first.

## A container saves only when items come and go

Changing an item in place (quality, durability) doesn't save it: call `inventory.m_onChanged` on the game that owns it, or it is lost on
the next reload.

## A container reloads whenever anything in its ZDO changes

`Container.Load` rebuilds the inventory from the save whenever the ZDO's revision changes, and any `ZDO.Set` on that object changes it.
A container on an object that saves other things often (a creature) is rebuilt over and over: in-place changes lost, held items point at
old copies. Skip the reload on the owner after loading once.

## Count a recipe's cost as the game does

`recipe.m_resources` also lists battle idols, flagged `m_upgraderResource`: the game charges them only at an upgrader station
(`station.m_upgrader`). Ignore the flag and nothing can be crafted (every recipe "needs" an idol), or a recycler hands out free idols.
Filter as `Player.HaveRequirementItems` does.

## Pay in full or not at all

Mods that pay for crafting or building from chests must check they found everything. If a chest emptied in between (multiplayer) or
another mod already counted the chests, a silent shortfall gives the item for less: refuse the action instead. Three traps:
- **Refuse early.** The game hands over the crafted item (`InventoryGui.DoCrafting` calls `AddItem` before `ConsumeResources`) and places
  the piece (`TryPlacePiece` before `ConsumeResources`) **before** it charges. Check and reserve in a prefix on `DoCrafting` /
  `TryPlacePiece`; by `ConsumeResources` it's too late to say no.
- **Not every recipe pays through `ConsumeResources`.** "Any one of these" recipes (`m_requireOnlyOneIngredient`) pay with
  `DoCrafting`'s own `RemoveItem`.
- **Someone may already have paid.** Another mod's prefix (a backpack paying from itself) may have done it and returned false: a prefix that
  pays takes `bool __runOriginal`, returns at once when it's false, and runs at `Priority.Last`. Count the player's own items by walking
  `GetAllItems()`, not `CountItems` (other mods add their sources to it). And pay in the same context that counted: an affordability check
  inside one mod's craft context and a payment outside it charge less than was counted.

## Don't count the same thing twice

Another mod may already add chest items to what the player has (ValheimPlus, AzuCraftyBoxes): two such mods count the chests twice.
Detect them (`Chainloader.PluginInfos`) and stand down (see **valheim-compat**).

## Never put back an old snapshot of shared things

Writing a saved copy of shared world state back (terrain before levelling, a chest's old contents) undoes everything that happened since:
other players' buildings lose their ground and collapse, their items vanish. Only restore what is still exactly as you left it, skip
anything another piece now stands on or uses, and wait until the area is fully loaded (`ZNetScene.instance.IsAreaReady`: a zone's
terrain loads before its pieces). When in doubt, leave it.

## Rewards must not be gamed

- **Leaving must never pay better than losing**: logging out, quitting or a crash mid-contest gets at most what giving up gets.
- **What a mod hands out respects the world's progress**: items from lands whose boss isn't beaten (the world's global keys) don't leave a
  contest with the player, food, meads and arrows included.
- **An item's `m_value` is its price at the trader**: any item with `m_value > 0` sells for coins. A mod item that's cheap to make must
  have `m_value = 0`, or it's a coin machine.
- **Pay players for what they did**: identify a player from the RPC's sender (the peer), never from an id in the payload; rewards per
  character can be farmed with new characters.
- Work out the expected return of anything that pays coins (a slot machine, a contest) and keep it below 100% with the default settings,
  decided by the server.

## Item and player custom data is shared

`m_customData` holds every mod's saved data (backpack contents, enchantments). Never replace the dictionary; only your own prefixed keys.
Moving items through `Inventory.Save`/`Load` keeps it.

## Name everything after your mod

Prefab names, RPC names, ZDO keys, `m_customData` keys, `$` localization keys, console and Claude Tools commands, asset bundle names, the
Harmony id and the DLL's assembly name all share one space with every other mod. A plain name (`Lantern`, `check`, `"level"`) collides sooner or later: the later one
replaces the earlier, or the game throws. Start each with your mod's name (`yourmod_lantern`, `YourMod_RequestTake`, `"YourMod.level"`).

## Check default keys against the game's

The game reads its own keys whatever a mod does: V auto-pickup, X sit, C walk, Q auto-run, G radial menu, F forsaken power, R hide,
T emotes, E use, Tab, M, 1-8. A mod default on one of these does both actions. Pick a key the game doesn't use. Unbinding the game's action
(and telling the player) is only for a key your mod uses everywhere; for a key used in one situation (aiming at your object), pick a free one.

## Dedicated servers draw nothing

No player, no camera, nothing rendered: guard visual code, and wrap the visual part of registering a piece in try/catch so a material
problem never stops the piece being registered (the host would delete the placed ones).

## "Is this chest open?" only works on its owner's game

`Container.IsInUse()` reads a flag only the container's **owner** sets. On anyone else's game it says "free" while another player has
the chest open, and that game's copy of the contents can be a second old. Before taking from or putting into a container that isn't
yours: read the saved flag (`zdo.GetInt(ZDOVars.s_inUse) == 1`: an int, not a bool), skip it if set, then `ClaimOwnership()` and
**reload it** (`Container.Load`, private) before touching items. Without the reload, your change and the other player's overwrite each
other (whichever copy is newer wins): items duplicated or lost. `BepInEx/claude/templates/ContainerAccess.cs` does all of this (and keeps
to built chests: see "A Container isn't always a chest" in **valheim-compat**).

## Recovery data must live in the world

Data kept only on a player's character (`Player.m_customData`) is saved when they log out and every 30 minutes: while they're offline
it can't be updated, and a crash loses the last half hour. Something another player's game can need (a companion's gear when it falls at
a shared base) must be kept in the world: on the object itself if it survives, or on the tombstone.

## A piece that holds items needs what a chest has

A chest's `Container` brings a lot for free: ward checks (`PrivateArea.CheckAccess`), refusing to be torn down while it holds something,
dropping its contents when it's destroyed, and one player at a time. A piece that keeps items some other way (a drying rack, a display,
a machine) must do each of these itself, or players lose what's in it and anyone can take from a warded base. And the items must be
given out **by the piece's owner**, through an RPC (see **valheim-mod-start**), never by whoever clicked it.

## Undo temporary changes in a finalizer

A prefix that changes something for the game's method (a bigger inventory height, a cheaper stamina cost) and a postfix that puts it back
leaks when the method throws: the postfix never runs. Put it back in a `[HarmonyFinalizer]`, which always runs (as the popular inventory
mods do).

## Never UnpatchAll()

`harmony.UnpatchAll()` with no id removes **every mod's** patches, not only yours: when your mod unloads (a hot reload, an update), every
other mod stops working. Use `_harmony.UnpatchSelf()`. (One popular mod gets this wrong; `modcheck` finds it.)

## Keep what you can't load

The big inventory mods back up the player's items in `Player.m_customData` (with a version number), keep items whose prefab is missing as
saved data instead of deleting them (the mod that adds them may come back), and move them to the tombstone on death. Throw nothing
away you can't read.

## Clean up for hot reload

Everything a mod starts must stop in `OnDestroy`: patches (`UnpatchSelf`), file watchers (`Dispose`), coroutines and objects it made,
commands and RPCs it registered (registering an RPC name twice throws, so remove yours first). The game never unloads a mod's old copy, so
its **static fields live for the whole session**: set big static caches to null in `OnDestroy`, or every reload keeps another copy in
memory. Libraries that patch with their own Harmony id (blaxxun-boop's managers) can't be undone: mark such a mod as needing a restart.

A mod that registers pieces, items, creatures, recipes or status effects, or changes inventory sizes, can't be reloaded safely in a world:
its reload level is `menu` (`modcheck` shows it, and Claude Tools' `reload` refuses it in a world). Reload it at the main menu, or restart.
ScriptEngine's F6 reloads **every** mod in `BepInEx/scripts` at once with no such check, so one mod's problem hits a player who only
updated another.

## Test with a real restart

Before releasing a mod that registers prefabs or changes the player: quit fully, start again, check `log` for `Missing prefab hash` and
`invalid positioned items`, and look at the things themselves.
