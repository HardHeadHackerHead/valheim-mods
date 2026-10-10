# Valheim modding pitfalls

Things that cost us lost items or lost buildings. Check new mods against them.

## Register custom pieces from the scene, and from both Awake hooks

A placed piece is saved by its prefab name. When its zone loads on the **host** and no prefab with that name is registered in
`ZNetScene`, the game logs `Missing prefab hash: N` and then `Destroyed invalid prefab ZDO`, and the piece is gone for good.

At world load `ZNetScene.Awake` can run before `ObjectDB.Awake`, so a register step that starts with
`if (ObjectDB.instance == null) return;` does nothing on a fresh launch. It only works after a hot reload, which hides the bug while
you are developing. So:

- Get the hammer and the cost items with `scene.GetPrefab("Hammer")` (items are in `ZNetScene` too), not `ObjectDB.instance`.
- Register from a `ZNetScene.Awake` postfix **and** an `ObjectDB.Awake` postfix (when `ZNetScene.instance` exists), and in the plugin's
  `Awake` for a hot reload while in a world.
- On unload, remove the `m_namedPrefabs` entry only if it still points at your own prefab, so unloading never takes out another
  copy's registration. (ScriptEngine's reload destroys the old copies first and adds the new ones a frame later, so the old
  `OnDestroy` runs before the new `Awake`.)

See `mods/SlotMachine/Plugin.cs` (`Register`, `Unregister` and the two patches at the bottom).

## The player's inventory height is reset on every spawn

Since the trader started selling inventory rows, `Player.OnSpawned` calls `Player.SetInventorySize(rows)` with the row count saved in the
character's `invrows` key. That sets the inventory height and then calls `Humanoid.DropInvalidItems`, which drops everything below it. As
with the pieces, a hot reload never spawns you, so it looks fine while developing.

Don't take `SetInventorySize` over (a prefix returning false), and don't set an exact height in `Player.Load`: GearSlots did both, and with
ValheimPlus' extra rows, BetterArchery's quiver or the slot mods (ExtraSlots, EquipmentAndQuickSlots, AzuExtendedPlayerInventory) items were
dropped at every spawn (review 2026-10). A postfix is too late: the items are already on the ground. What works (EquipmentAndQuickSlots'
pattern): a non-skipping prefix on `SetInventorySize`; a `Priority.First` prefix on `DropInvalidItems` that moves your rows' items below the
height the game and other mods just set and raises the height to fit; a `Priority.Last` postfix that raises it again if another mod lowered
it; your rows' position saved in `Player.m_customData`; `Max(height, ...)`, never an exact height. And stand down when a slot mod that uses
the same cells is installed.

## Never save a ZDOID

Every time a world loads, the game gives each object a new `ZDOID` (`ZDO.Load` sets it from a counter that starts again at 0). A
`ZDOID` is only good for the current session: one saved in a ZDO or config points at some unrelated object after a restart. To refer to an
object across restarts, store your own random id on it (PortalHub's `dh_pid` in `mods/PortalHub/Plugin.Net.cs`), or use the game's
connections (`ZDO.SetConnection`), which it re-links by hash when the world loads.

## Fill a tombstone with the game's own move

A tombstone's (or cargo crate's) container is only a few slots big until the game fills it: `Inventory.MoveInventoryToGrave(original)`
resizes the grave to the bag it empties and moves everything at once. Adding items one by one with `AddItem` fits only the small default
size, and the rest is lost (AICompanion 0.3.0 lost a fallen companion's armour this way). The move also skips items flagged `m_equipped`:
unequip and clear the flag first.

## A container saves only when items come and go

A `Container` writes its inventory to its ZDO when an item is added or removed (`Inventory.m_onChanged`). Changing an item in place (its
quality after an upgrade, its durability after a repair or wear) does not save it, and the change is lost the next time the object is made
from its ZDO (its area reloading, a hot reload). After changing items in place, invoke `inventory.m_onChanged` on the game that owns it.

## Check every default key against the game's

Valheim reads its own keys whatever a mod does: GearSlots' quick slot 4 on V also flipped the game's auto-pickup toggle (V), and the
player only saw it as "auto-pickup stopped working". The game's defaults are in `ZInput` (in `assembly_utils.dll`, `AddButton(...)`):
V auto-pickup, X sit, C walk, Q auto-run and previous tab, G radial menu, F forsaken power, R hide, T emotes, E use, Tab, M, digits
1-8. Mods that use one of these call `GameKeys.Free` (in GearSlots, QualityOfLife, BuildOrders), which unbinds the game's action once
(saved in the game's own settings, never the essential ones) and tells the player, so they can give it another key in Settings, Controls.

## Test with a real restart

These bugs only show on a fresh launch. Before releasing a mod that registers prefabs or changes the player, quit the game fully,
start it again, and check the log for `Missing prefab hash` and `invalid positioned items`.

## Count a recipe's cost as the game does

Since the battle idols arrived, a recipe's `m_resources` also lists items marked `m_upgraderResource` (the flint axe lists 1
`Upgrader1Weapon`). The game counts a requirement only when that flag matches the station's `m_upgrader` (see
`Player.HaveRequirementItems`, `ConsumeResources`): at a workbench or forge the idols are left out, at an upgrader station only they count.
Code that reads `m_resources` directly thinks every recipe needs an idol, so nothing can be made (AICompanion 0.5.0's companion never
crafted or upgraded). Filter them out as AICompanion's `Upgrades.Needs` does.

## A container reloads whenever anything in its ZDO changes

`Container.Load` (called from `CheckForChanges` every update) rebuilds the inventory from the saved `s_items` whenever the ZDO's
`DataRevision` differs from the last one it saw, and any `ZDO.Set` on that object bumps the revision, not only the items. An object that
keeps a Container and also writes other values to its ZDO often (AICompanion's companion: status, stamina, skills several times a
second) gets its inventory replaced by fresh copies of the last saved items over and over: changes made in place since the last save
(wear, repairs) are lost, and anything holding a reference to an item (what a Humanoid has equipped) points at an old copy. AICompanion
skips the reload on the game that owns the companion, after loading once when it takes ownership (`Container_Load_Companion`).

## Every prefix runs, and ScriptEngine ignores incompatibility

In the HarmonyX that BepInEx ships, a prefix returning false skips only the game's method: every other mod's prefixes still run. So a
prefix that pays, deposits or gives must take `bool __runOriginal`, return at once when it's false (another mod did the job), and usually
run at `Priority.Last`. Our chest crafting charged twice next to Adventure Backpacks, and FeedFromChests deposited smelter output twice
next to ValheimPlus, this way. ScriptEngine (how the mod manager loads our mods, from `BepInEx/scripts`) ignores `[BepInIncompatibility]`
and `[BepInDependency]`, and at `Awake` mods loaded later aren't in `Chainloader.PluginInfos` yet: detect other mods lazily, in code.

## Pay in full or not at all, and refuse early

The game gives the crafted item (`InventoryGui.DoCrafting` calls `AddItem` before `ConsumeResources`) and places the piece (`TryPlacePiece`
before `ConsumeResources`) before it charges, so a shortfall found in `ConsumeResources` is too late to refuse. Check and reserve in a
`DoCrafting` / `TryPlacePiece` prefix. "Any one of these" recipes (`m_requireOnlyOneIngredient`) pay through `DoCrafting`'s own
`RemoveItem`. Count the player's own items with `GetAllItems()` (other mods add their sources to `CountItems`). Never ignore what a
"take from chests" helper says it couldn't find.

## "Is this chest open?" only works on its owner's game

`Container.IsInUse()` reads a flag only the container's owner sets: on other games it says "free" while someone has it open, and their copy
of the contents can be a second old. Read the saved flag (`ZDOVars.s_inUse`), skip it if set, `ClaimOwnership()`, then reload
(`Container.Load`) before touching items, as `CraftFromChests/ChestScanner.cs` does. AICompanion and QualityOfLife missed this.

## A Container isn't always a chest

Creatures (AICompanion's companions), carts, ships, tombstones and Adventure Backpacks' backpack proxy have a `Container` too. Mods that use
"every container nearby" must keep to built pieces (a `Piece` in the parent chain, no `Character`), or they spend a companion's arrows, a
grave's contents, or the backpack a second time.

## Recovery data must live in the world

`Player.m_customData` is saved on logout and every 30 minutes, and can't change while its player is offline. A companion's kept gear stored
only there and on the companion itself (destroyed when it dies) was lost when it fell on another player's game. Keep what other games may
need on an object that survives (the tombstone).

## Never put back an old snapshot of shared things

BuildOrders restored the ground under a removed plan from its saved copy, checking only the plan's own pieces: other buildings on the pad
lost their ground and fell. A zone's terrain also loads before its pieces, so a restore can see "nothing built" under a finished building.
Restore only what is still exactly as you left it, skip anything another piece stands on, and wait for `ZNetScene.IsAreaReady`.

## Rewards must not be gamed

Quitting mid-contest must not pay better than losing (Arena paid a full round). Items from lands whose boss isn't beaten must not leave a
contest. `m_value > 0` makes an item sellable at the trader (SkalTavern's mead was a coin machine). Identify players from the RPC sender,
not the payload, and remember new characters can claim per-character rewards (BountyBoard). Gameplay settings (payouts, rewards, ranges,
timers) are the server's in multiplayer (`mods/Shared/ServerSettings.cs`), or every player sets their own.

## The chest-crafting clash

CraftFromChests and BuildFromChests add chest items in `Inventory.CountItems`; ValheimPlus (craft from chest, off by default) and
AzuCraftyBoxes add them in `Player.HaveRequirementItems`/`HaveRequirements`. Installed together, chests count twice and crafts pay half. Stand
down when one of them is installed.

## Name everything after your mod

Prefabs, RPCs, ZDO and `m_customData` keys, `$` keys, commands and the Harmony id share one space with every other mod. ClaudeTools' `check`
command hid BuildOrders' `check` until it was renamed `modcheck`. Start every name with the mod's, and pick prefab names a player can find
(`prefabs <text>` in ClaudeTools lists them: a "war stone" was `BobWarstone`).

## Ship only what you mean to

`publish.ps1` builds every mod from the working folder, so uncommitted code ships (Arena 0.2.3 nearly went out with the screenshot tool).
Stash or commit first, and check the built DLL (strings are UTF-16). Debug commands that give, move or remove follow the game's cheat rule
(`CheatsAllowed()` in ClaudeTools) or stay out. F6 reloads every mod in `scripts`: test mods with `RESTART_REQUIRED.txt` with a full restart.
