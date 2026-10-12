# Shared code

Code more than one mod here needs. It is **compiled into each mod's own DLL** (players install only the mod: nothing extra), and each
mod gets its own private copy (every class is `internal`), so two of our mods never clash over it. One file to fix, but a fix reaches
players only when each mod using it is rebuilt and released.

Two ways in:
- **Every mod**: listed in `mods/Directory.Build.props` (a `Compile Include ... Link` line).
- **Some mods**: the mod's csproj links it: `<Compile Include="../Shared/PieceReload.cs" Link="Shared/PieceReload.cs" />`.

| File | What it does | In |
|---|---|---|
| `AiDisclosure.cs` | Says in the DLL that AI helped make the mod (Thunderstore asks for it). | every mod |
| `Migration.cs` | Moves a mod's settings from its old id (`com.dhack.*`) to its new one, and stands down if an old copy is still installed. | every mod |
| `ServerSettings.cs` | Settings the host decides in multiplayer, with no library: edits to the .cfg apply live, admins change the server's values from Configuration Manager, and `Server`/`NeedsServer` keep pieces and items out of the menus on a server without the mod. Also a Claude Tools template. | most mods (their csproj) |
| `Costs.cs` | Build and craft costs and stations as settings ("FineWood:12, BronzeNails:30"); one with a mistake keeps the last good cost. Also a Claude Tools template. | JetSkis |
| `Content.cs` | Items, recipes, smelter/oven/fermenter conversions, creature drops and trader goods into the game, by name and again on every register: `m_dropPrefab` set, the items' status effects registered, one `Recipe_<prefab>[_<way>]` per way to make it (cost through `Costs.cs`), the game's prefab lists cleared of our earlier entries before adding, and on unload only what still points at this copy taken out. Needs `Costs.cs`. Also a Claude Tools template. | CigarSmoking, SkalTavern |
| `TestCommands.cs` | The game's cheat rule for test commands that change the game. | Arena, Rainbows, SkalTavern, Ziplines |
| `SpecialContainers.cs` | Built pieces with a chest's Container that aren't storage (the Obliterator, a Ledger Chest, the Recycler, Arena's chests, anything marked `dh_notstorage`): mods that pick or fill chests leave them alone. | AICompanion, BuildFromChests, CraftFromChests, FeedFromChests, LedgerChest, QualityOfLife |
| `Translations.cs` | A mod's words in the player's language through the game's own translations: `translations/*.json` built into the DLL, `T("key", args)` in code, the game's Language setting, players' own corrections in `BepInEx/config/translations/<GUID>/`. Also a Claude Tools template. `tools/check_translations.py` checks the files. | every mod with `translations/English.json` (`Directory.Build.props`) |
| `PieceReload.cs` | Build pieces that reload while you play, with nothing deleted. Also a Claude Tools template. | BountyBoard, LedgerChest, Recycler |
| `Skill.cs` | A custom skill (number = hash of a fixed id; never change it) that the game keeps: its def, `IsSkillValid`, a backup of the level in the character's `m_customData` put back after a load without the mod, its name where the game shows `$skill_<number>`, `raiseskill`/`resetskill` with tab completion, a live `Gain`. Also a Claude Tools template. | Parkour |
| `Icon.cs` | Build-menu and item pictures drawn from the mod's own model when the game starts (`Icon.Make(piece)`, `Icon.MakeItem(item, setIcon: true)`), in a layer of its own with networking switched off for the copy; an embedded PNG when drawing fails. Also a Claude Tools template. | BirdTrap, BountyBoard, LedgerChest, Recycler, SlotMachine |
| `StatusEffects.cs` | A mod's own status effects made in code: `Create`, `Register` (on every ObjectDB load), `Apply`/`Has`/`Remaining`/`Remove`, and `Unload`, which takes them off every character first; after a hot reload they go back on with the time left. Also a Claude Tools template. | Rainbows |
| `SnapPoints.cs` | A build piece's snap points fitted to its own shape: `Clear` takes off those of the prefab it was copied from (a pole's float a copy 1 m up or sink it), `Add` and `Box` put them where the game puts its own (switched-off `$hud_snappoint_...` children tagged `snappoint`), on the corners of a box-shaped piece's base. Also a Claude Tools template. | Arena, BountyBoard, LedgerChest, Recycler, SlotMachine, Ziplines |
| `GroundVehicle.cs` | A land vehicle: ray-and-spring wheels, an arcade motor, the rider taking the controls the way boats and saddles hand them over (`IDoodadController`, the ZDO handed to the rider), the piece cloned from the Raft with boat parts stripped, and plain-shape helpers and colours for code-built models. **Written for both mods at once, not yet driven in the game.** | FourWheelers, MountainBikes |
| `VehicleMesh.cs` | Shapes for code-built vehicles that cubes cannot make: a rounded box, a lofted body skin through cross-sections (smoothed between them), a curved fender arch, a tapered tube and a lathe-turned tyre. | FourWheelers, MountainBikes |

Before adding a file: is it needed by more than one mod, small, and tested in a real mod? Say at its top why it exists, which mods use
it and how it gets in. Code other mod makers would want goes to Claude Tools' `templates/` too (with namespace `YourMod`), in
https://github.com/HardHeadHackerHead/valheim-claude-tools.
