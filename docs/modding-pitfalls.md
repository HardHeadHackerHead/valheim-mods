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
character's `invrows` key. That sets the inventory height and then calls `DropInvalidItems`, which drops everything below it. A mod that
makes the inventory taller has to take over `SetInventorySize` (GearSlots does, in `mods/GearSlots/Patches.cs`), or anything in its extra
rows lands on the ground at every spawn. As with the pieces, a hot reload never spawns you, so it looks fine while developing.

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
