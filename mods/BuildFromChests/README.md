<!-- This page is made by tools/modpages/make_pages.py from the mod's DESCRIPTION.txt, CHANGELOG.txt, settings and the
     repo's README. Change those (or the HAND-WRITTEN part below), not the rest of this page. -->

# 🔨 BuildFromChests

<img src="cover.png" alt="BuildFromChests" width="100%">

**Version 1.4.1**  ·  [all the mods](../../README.md)  ·  install it from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Build_From_Chests/) (r2modman, Thunderstore Mod Manager) or the in-game [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) (**F7**)

Building with the hammer uses materials from chests near you. The build menu shows how much of everything you own in total.

<!-- HAND-WRITTEN: kept when this page is made again -->
## 📸 Screenshots

**Building with the hammer: what each piece needs counts what is in the chests around you**

<img src="images/1.jpg" alt="Building with the hammer: what each piece needs counts what is in the chests around you" width="100%">
## ⚠️ Known clashes

- **Adventure Backpacks** ("Craft From Backpack", on by default): handled. The backpack pays its part first and this mod only takes what is
  still missing from the chests (before 1.4.0, building next to it could cost up to twice as much).
- **AzuCraftyBoxes**, **CraftFromContainers** (aedenthorn), and **ValheimPlus** with its `CraftFromChest` section on: they also build from
  chests, and two such mods count every chest twice (pieces pay half). When one of them is installed (ValheimPlus: while that section is on)
  this mod stands down; the log says so once.
- **CraftFromChests** and **FeedFromChests** (ours): fine together, each only works in its own moment and pays only what the others left.
- Only built chests are used: graves, carts, ships, a companion's bag and chest, and a backpack you carry are left alone.
<!-- END HAND-WRITTEN -->

## 🔍 How it works

Building with the hammer uses materials from chests near you, not just your inventory.

### How it works

- Pieces you place take materials from your inventory first, then from chests within range (default 20 m).
- The build menu shows how many of each material you have in total, so you can see what is possible before you place anything.
- Works together with CraftFromChests; each can be turned off on its own.

Settings: range and whether the counts show.

## ⚙️ Settings

In `BepInEx/config/com.quad.buildfromchests.cfg` (made the first time the game runs with the mod).

**Display**

| Setting | Default | What it does |
|---|---|---|
| `ShowHaveCounts` | `true` | In the build menu, show how many of each material you HAVE (inventory + chests) next to how many you need. |

**General**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Turn the mod on or off. |
| `Radius` | `20` | How far (in meters) from YOU a chest can be and still be used while building (also for BuildOrders: building its ghosts, hold E to build all, and its fetch key). Raise it to build far from your storehouse: 60 reaches across a big base. Chests only count while their area is loaded around you (about 100 m and more). Takes effect at once. In multiplayer the server's value applies. |

## 👥 Playing together

See [who needs which mod](../../README.md#playing-together) on the front page.

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## 📜 Changes

- **1.4.1** A new id, com.quad.buildfromchests (it was com.dhack.buildfromchests): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks. It also stands down next to Toxo's Craft From Chests, CraftFromChestsPlus, Teflon Ted's Craft From Chests, StoreAndCraft and SmartCraft-Storage, as it already did next to AzuCraftyBoxes and CraftFromContainers: two such mods count every chest twice.
- Fix: next to Adventure Backpacks, building could cost up to twice as much (the backpack and the chests both paid). Now the backpack pays its part and the chests only what is still missing. Fix: a piece the chests can no longer pay for (someone took the materials since the menu counted them) is no longer placed for less: you get a message instead. Graves, carts, ships, a companion's bag and chest and a backpack are no longer used as storage. With AzuCraftyBoxes, CraftFromContainers or ValheimPlus' craft-from-chest installed, this mod stands down (two would count every chest twice). In multiplayer the server's Radius applies.
- Radius can now be set anywhere from 2 to 150 m (a slider in the configuration manager), and takes effect at once: raise it to build far from your storehouse, for instance a big BuildOrders blueprint at the edge of your base (BuildOrders' ghosts, hold-E build-all and fetch key use the same range).
- Fix: in multiplayer, building with materials from a chest another player had open could lose or duplicate items. Chests in use are now left alone.
- Lets BuildOrders fetch materials from the chests around you into your inventory (a small shared function it leaves for it).
