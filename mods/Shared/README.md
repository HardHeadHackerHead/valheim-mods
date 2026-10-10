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
| `ServerSettings.cs` | Settings the host decides in multiplayer, with no library. Also a Claude Tools template. | most mods (their csproj) |
| `TestCommands.cs` | The game's cheat rule for test commands that change the game. | Arena, Rainbows, SkalTavern, Ziplines |
| `SpecialContainers.cs` | Built pieces with a chest's Container that aren't storage (the Obliterator, a Ledger Chest, the Recycler, Arena's chests, anything marked `dh_notstorage`): mods that pick or fill chests leave them alone. | AICompanion, BuildFromChests, CraftFromChests, FeedFromChests, LedgerChest, QualityOfLife |
| `PieceReload.cs` | Build pieces that reload while you play, with nothing deleted. Also a Claude Tools template. | BountyBoard, Recycler |

Before adding a file: is it needed by more than one mod, small, and tested in a real mod? Say at its top why it exists, which mods use
it and how it gets in. Code other mod makers would want goes to Claude Tools' `Templates/` too (with namespace `YourMod`).
