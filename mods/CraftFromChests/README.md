<!-- This page is made by tools/modpages/make_pages.py from the mod's DESCRIPTION.txt, CHANGELOG.txt, settings and the
     repo's README. Change those (or the HAND-WRITTEN part below), not the rest of this page. -->

# 📦 CraftFromChests

<img src="cover.png" alt="CraftFromChests" width="100%">

**Version 1.4.1**  ·  [all the mods](../../README.md)  ·  install it from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Craft_From_Chests/) (r2modman, Thunderstore Mod Manager) or the in-game [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) (**F7**)

Crafting stations (and hand-crafting) use materials from nearby chests. See lines to every chest in use and change the range from 5 to 30 m.

<!-- HAND-WRITTEN: kept when this page is made again -->
## 📸 Screenshots

**The workbench: Chest lines and Range beside the tabs, and the materials counted from the chests**

<img src="images/1.jpg" alt="The workbench: Chest lines and Range beside the tabs, and the materials counted from the chests" width="100%">

**Close up: the buttons and the counts**

<img src="images/2.jpg" alt="Close up: the buttons and the counts" width="100%">
## ⚠️ Known clashes

- **Adventure Backpacks** ("Craft From Backpack", on by default): handled. The backpack pays its part first and this mod only takes what is
  still missing from the chests (before 1.4.0, crafting next to it could cost up to twice as much).
- **AzuCraftyBoxes**, **CraftFromContainers** (aedenthorn), and **ValheimPlus** with its `CraftFromChest` section on: they also craft from
  chests, and two such mods count every chest twice (crafts pay half). When one of them is installed (ValheimPlus: while that section is on)
  this mod stands down and leaves crafting from chests to it; the log says so once.
- **BuildFromChests** and **FeedFromChests** (ours): fine together. Each one only works in its own moment (crafting, building, feeding a
  station), and each pays only what the others left.
- Only built chests are used: graves, carts, ships, a companion's bag and chest, and a backpack you carry are left alone.
<!-- END HAND-WRITTEN -->

## 🔍 How it works

Crafting stations, and hand-crafting from your inventory, use materials from nearby chests.

### How it works

- Anything you can reach within the range counts toward a recipe. The game takes the materials from your inventory first, then from the chests.
- The crafting menu shows how many of each material you have in total.
- A Chest lines button beside the crafting tabs draws lines from the station to every chest in use; a Range button cycles the distance (5 to 30 m).

Settings: range (default 20 m), whether counts and lines show, and the position and size of the buttons.

## ⚙️ Settings

In `BepInEx/config/com.quad.craftfromchests.cfg` (made the first time the game runs with the mod).

**Display**

| Setting | Default | What it does |
|---|---|---|
| `ShowHaveCounts` | `true` | In the crafting window, show how many of each material you HAVE (inventory + chests) next to how many you need. |
| `ShowLines` | `false` | Draw lines from the crafting station to every chest it can use. |
| `ToggleOffsetX` | `0` | Move the 'Chest lines' button horizontally from its default spot beside the Upgrade tab (UI pixels). |
| `ToggleOffsetY` | `0` | Move the 'Chest lines' button vertically (UI pixels). Negative = down. |
| `ToggleScale` | `1` | Size multiplier for the 'Chest lines' button. |
| `ToggleWidth` | `2` | 'Chest lines' button width as a multiple of the Upgrade tab's width. |
| `RangeButtonWidth` | `1.4` | 'Range' button width as a multiple of the Upgrade tab's width. |

**General**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turn the mod on or off. |
| `Radius` | `20` | How far (in meters) from the crafting station a chest can be and still be used. In multiplayer the server's value applies. |

## 👥 Playing together

See [who needs which mod](../../README.md#playing-together) on the front page.

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## 📜 Changes

- **1.4.1** A new id, com.quad.craftfromchests (it was com.dhack.craftfromchests): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks. It also stands down next to Toxo's Craft From Chests, CraftFromChestsPlus, Teflon Ted's Craft From Chests, StoreAndCraft and SmartCraft-Storage, as it already did next to AzuCraftyBoxes and CraftFromContainers: two such mods count every chest twice.
- Fix: next to Adventure Backpacks, crafting could cost up to twice as much (the backpack and the chests both paid). Now the backpack pays its part and the chests only what is still missing. Fix: "any one of these" recipes (meads and the like) could be crafted for free from the chests, or failed when you carried none. Fix: a craft the chests can no longer pay for (someone took the materials since the window counted them) is now refused with a message, instead of being handed over for less. Graves, carts, ships, a companion's bag and chest and a backpack are no longer used as storage. With AzuCraftyBoxes, CraftFromContainers or ValheimPlus' craft-from-chest installed, this mod stands down (two would count every chest twice). In multiplayer the server's Radius applies (2 to 50 m).
- Fix: in multiplayer, crafting with materials from a chest another player had open could lose or duplicate items. Chests in use are now left alone.
- Smoother: the crafting-screen buttons are only looked after while the inventory is open and aren't repositioned every frame, and the chest checks are worked out once and reused (no more stutter when opening the inventory). Hand-crafting from the inventory uses nearby chests, and the crafting window shows how many of each material you have.
