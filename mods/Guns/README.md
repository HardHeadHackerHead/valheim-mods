# Quad's Guns

Firearms for Valheim, starting with the **Machine Gun**. **Work in progress, not released**: it is made and tested in a world you can lose.

- Hold the attack button to fire (10 rounds a second); the spread grows the longer you hold, and settles when you let go.
- Right mouse (the block button; the left trigger on a controller) aims: the camera zooms and the spread tightens.
- The gun always points where you look; rounds fly to the spot under the crosshair, with a little lift for drop.
- Its own sound (made in code), muzzle flash, brass casings, recoil and tracers.
- Free to get while it is tested: `give dh_machinegun` (Claude Tools). No ammo yet; a recipe and ammo come with the balance pass.

Built on the Arbalest (the hold, the Crossbows skill), but fired by the mod, not the game's attack: see `Fire.cs`. Reloads in a world
(`ItemReload.cs`). Not yet tested in multiplayer: other players see the round fly, but not the flash or hear the shot, and the gun is
carried in the crossbow pose in their view.

Files: `Plugin.cs` (setup), `Things.cs` (registering), `MachineGun.cs` (item and round), `Model.cs` (the model), `GunIcon.cs` (inventory picture),
`Fire.cs` (firing, spread, aim at the crosshair), `Aim.cs` (aiming, pose), `Fx.cs` and `Sound.cs` (flash, sound, casings, recoil).
