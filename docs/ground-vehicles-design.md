# Ground vehicles: Four Wheelers and Mountain Bikes

Two mods (`mods/FourWheelers`, `mods/MountainBikes`, started 2026-10-12) on one shared core, `mods/Shared/GroundVehicle.cs`
(compiled into each, private copy, own `GroundVehicle.Spec`). Chosen with the user: built with the hammer (a piece, like Jet Skis, so
reload level `world`), arcade drive, two mods plus a shared file.

## How it works

- **The piece**: a copy of the game's Raft with everything of a boat stripped (`VehicleKit.CloneBase`): what remains is `ZNetView`,
  `ZSyncTransform`, `Rigidbody`, `Piece`, `WearNTear`. Body, hit boxes, seats and wheels are added in code (`Model.cs`). Registered like
  Jet Skis (`Things.cs`, both Awake hooks, `PieceReload`, `NeedsServer`).
- **Driving**: no WheelColliders. Each wheel is a ray down from its mount with a spring and damper; the motor sets the speed along the
  ground and takes the sideways slide out (the Jet Ski's motor, on land). Turning sets the yaw rate. A self-righting torque keeps a bike
  up (strong, and stronger parked, so it stands on its own) and a four wheeler from flipping easily. In deep water it floats and stalls.
- **Controls**: the game's `IDoodadController` (what boats and saddles use). E on it asks the owner (`dh_*_Request`); the owner sets the
  user and **hands the ZDO to the rider** (`SetOwner(sender)`, as `Sadle` does), so physics runs on the rider's game. The rider's keys come
  in through `ApplyControlls` (move, Shift = run, block = brake). Jump or E gets off (the game's own rule for a doodad controller).
- **Every game** draws the wheels (spin from the movement, front wheels steer from a synced `dh_*_steer` float; bike leans into turns).
- **Bike**: one rider, pedalling costs stamina (`PedalStamina`, x3 sprinting; out of breath: walking pace), coasting/braking free.
  **Four wheeler**: surtling-core engine, free to run, passenger seat (`Chair`) and a rack (`Container`, 4x2).

## Not done / untested (this is the whole to-do; since 2026-10-12 done: dust, paint, flip recovery, lights, sounds, gauge, icon, pages, translations, ramming, hammer removal)

1. **Nothing driven in the game yet.** Build is clean and `modcheck` reports 0 problems; no placement, no ride, no restart test.
   Expect tuning: spring/damper feel, speeds, turn rate, grip, bike self-righting, wheel heights (rest hub y = tyre radius).
2. **Does the Raft clone place on land?** `CloneBase` sets `m_waterPiece=false`, `m_noInWater=true`; check the ghost and that the
   stripped `WearNTear`/`ZSyncTransform` behave (ownership hand-over, remote players see it move, no rubber-banding).
3. **Rider pose**: uses the plain `attach_chair` sitting animation (legs clip, hands off the bars). Port a pose pass like
   `JetSkis/Rider.cs` (hands on grips, feet on pegs/pedals; a pedalling cycle for the bike).
4. **Dismount safety**: `OnUseStop` detaches 0.5 m above the seat. Check fall damage when getting off at speed; the pitfall "an
   attached player lets go on any input" applies: jump gets off by design here, not while crossing anything deadly.
5. Engine sound (four wheeler), tyre dust/dirt, headlight, paint choices (Jet Skis has E-on-cowl paint), brake light.
6. Build-menu icon (currently the raft's: use `Shared/Icon.cs`), thunderstore.toml, README, CHANGELOG, `images/`, the 8 other
   translations, mod pages, `publish.ps1`/feed entry.
7. Multiplayer: two-player test of the hand-over, passenger seat, and a server without the mod (`NeedsServer`).
8. Crash damage (`ImpactEffect`) and wear while ridden; leaving one parked on a slope (it is held when unridden: check).
9. `Shared/README.md` rule: shared code is tested in one real mod before a second uses it. `GroundVehicle.cs` was written for both at
   once; test Four Wheelers first and fix the file before trusting it in Mountain Bikes.
