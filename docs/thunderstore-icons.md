# Thunderstore icons: brief

Each of our mods is released on Thunderstore as its own package (team **Quads_Lab**), and each needs an icon. The icon is the first thing
players see in Thunderstore, r2modman and Thunderstore Mod Manager: a small square next to the mod's name in a long list of other mods.

Right now each mod has a placeholder `thunderstore/icon.png`, cut from the bottom of its cover (`mods/<Mod>/cover.png`, 640 x 360, which has the
mod's name written across the top). Please replace them with icons made for the purpose.

## What every icon must be

- **Exactly 256 x 256 pixels, PNG.** Thunderstore refuses anything else. Saved as `mods/<Mod>/thunderstore/icon.png` (overwrite the
  placeholder). **Never touch `mods/<Mod>/icon.png`**: in BirdTrap, BountyBoard, LedgerChest and SlotMachine that file is the build-menu
  icon built into the mod itself.
- **Readable at 64 x 64**, the size mod managers often show: one clear subject, big, centred, strong silhouette and contrast. No
  small details that turn to mush.
- **No text, or at most one short word.** The mod's name is always shown beside the icon; the covers' long titles don't fit a square.
- **One series.** All 23 sit side by side on our team page, every one named "Quads ...", so they should look like a set: the same painted Viking style as the covers
  (warm firelight and gold accents against cool blue mountains and forests, rich wood and leather), the same framing distance, and
  ideally the same subtle border or vignette. Use each mod's cover as the reference for its subject and colours.
- **Nothing copied** from Valheim's own art or other mods' icons.
- If they're AI-generated, say so when handing them back. (Thunderstore doesn't require it for icons, but we note it in the README.)

## The mods

| Mod folder | Shown on Thunderstore as | What it does | Icon idea (the cover's main subject, made to read at small size) |
|---|---|---|---|
| AICompanion | Quads Companion | A viking companion who comes on your adventures and lives its own life at home | Two vikings shoulder to shoulder, a shield and a bow: companionship |
| Arena | Quads Arena | A stone colosseum where you fight for the crowd, gold and glory | A warrior with an axe in a torch-lit ring, or crossed axes over an arena gate |
| BetterCraftingStations | Quads Crafting Filters | Filter chips above the crafting list | A crafting grid of item icons with a few glowing check marks or a filter funnel |
| BirdTrap | Quads Bird Trap | A buildable trap you bait with berries to catch gulls for feathers | A wicker trap with a gull and berries, a feather in front |
| BountyBoard | Quads Bounty Board | A notice board with contracts for the group | A wooden board with pinned parchment showing creature silhouettes |
| BuildFromChests | Quads Build From Chests | Building uses materials from nearby chests | A chest with glowing stones and logs streaming up toward a hammer |
| BuildOrders | Quads Build Orders | Plan pieces as shared ghost build orders | A glowing blue ghost frame of a building beside a real wooden one |
| CigarSmoking | Quads Cigars | Grow tobacco, cure it, roll cigars and smoke them | A lit cigar with a curl of smoke over tobacco leaves |
| ClaudeTools | Quads AI Mod Toolkit | Lets an AI assistant see the game and help make mods | A map table with a glowing blueprint of a longhouse (tools and plans) |
| CraftFromChests | Quads Craft From Chests | Crafting uses materials from nearby chests | Open chests with golden trails of materials flowing to a workbench |
| FeedFromChests | Quads Feed From Chests | Smelters, kilns and fires take items from nearby chests | Ore and wood flying from chests into a glowing furnace |
| GearSlots | Quads Gear Slots | Extra inventory slots for what you wear, eat and use | A carved panel of item slots: helmet, armour, food, weapon |
| LedgerChest | Quads Ledger Chest | A chest that shows and searches every chest around it | An open ledger on a chest with a magnifying glass rune |
| MapShare | Quads Map Share | Share the uncovered map with everyone, live | A parchment map with a golden line sweeping across it |
| PartyHud | Quads Party HUD | A party panel showing every player | Two or three viking portraits stacked in carved frames |
| PortalHub | Quads Portal Hub | Portals with a list of destinations | A glowing round portal with a distant snowy fortress inside |
| QualityOfLife | Quads Quality Of Life | Small controls that make the game nicer to play | A viking with a hammer and shield surrounded by small golden rune icons |
| Quiver | Quads Quiver | Pick shot arrows back up and carry them in a quiver | A leather quiver full of arrows, one arrow being pulled out |
| Rainbows | Quads Rainbows | A rainbow after the rain, and a blessing for looking at it | A bright rainbow arcing over a misty lake and mountains |
| Recycler | Quads Recycler | Turn old gear back into materials | An iron press crushing an axe, with ingots coming out |
| SkalTavern | Quads Skal Tavern | Ale and mead that get you tipsy, and a toast | Wooden tankards clashing in a toast, foam flying |
| SlotMachine | Quads Slot Machine | Odin's Fortune: a buildable slot machine | A carved slot machine with three rune reels and gold coins spilling out |
| Ziplines | Quads Ziplines | Build two posts, run a rope between them and ride it | A viking hanging from an axe on a rope over a forest valley |

## Checking them

`modkit package mods/<Mod>` (see the valheim-thunderstore skill) checks each icon is a 256 x 256 PNG. To see them all together,
put them side by side at 64 px: they should be told apart at a glance and look like one family.
