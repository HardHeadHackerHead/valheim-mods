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

## Test with a real restart

These bugs only show on a fresh launch. Before releasing a mod that registers prefabs or changes the player, quit the game fully,
start it again, and check the log for `Missing prefab hash` and `invalid positioned items`.
