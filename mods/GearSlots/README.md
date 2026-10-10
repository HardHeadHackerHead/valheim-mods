<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🛡️ Quad's Gear Slots

<img src="cover.png" alt="Quad's Gear Slots" width="100%">

**Extra inventory slots for what you wear, eat and use, in a Gear panel next to your inventory.**

![version: 1.2.2](https://img.shields.io/badge/version-1.2.2-3b6e8f?style=flat-square)  ![multiplayer: only who wants it](https://img.shields.io/badge/multiplayer-only%20who%20wants%20it-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Gear_Slots/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/GearSlots)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Gear%20Slots%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Gear_Slots/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

### What is in it

- Head, Chest, Legs, Cape, Belt, Trinket, Ammo and Shield slots: drop a matching piece in and you put it on. Armor and arrows no longer take room in your bag, and gear you are already wearing moves into its slot by itself.
- The shield follows your weapon: switch to a one-handed weapon and the shield from the Shield slot comes out with it (two-handed weapons and bows put it away).
- Three Food slots: keep your best meals in their own place (right-click to eat). Optional auto-eat is in the settings.
- Five Quick slots (Z, X, C, V by default; the fifth is unbound) for a weapon, tool, potion or anything else: press the key to equip or use it. With the inventory closed they show in a row under your hotbar, with their keys.
- Slots only accept the right kind of item, and Sort and Stack to Chests (QualityOfLife) leave them alone.
- Nothing is stored in a new place: the slots are extra rows of your real inventory, so saving, weight, crafting and dragging work as normal. Before removing the mod, move your gear-slot items into your bag: without it the game drops anything in those rows at your feet the next time you spawn.

Every key and option is in the settings (the manager's Mod settings tab).

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Gear panel beside your inventory: armour, cape, belt, trinket, ammo and shield slots**

<img src="images/1.jpg" alt="The Gear panel beside your inventory: armour, cape, belt, trinket, ammo and shield slots" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **Z** / **X** / **C** / **V** | Use what is in Quick slot 1 to 4 (equip a weapon or tool, drink a potion); the fifth slot has no key until you set one |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes**

- **ValheimPlus** (extra inventory rows): works. You keep all of its rows and the gear rows sit below them.
- **ExtraSlots, Equipment and Quick Slots, AzuExtendedPlayerInventory, Extended Player Inventory, ComfyQuickSlots**: they keep their own slots in the same inventory cells, so the gear slots switch off while one is installed. What was in them moves into your bag, and you're told in chat.
- **BetterArchery**: the gear slots switch off while its quiver is on.

**Works with our mods**

- [Quad's Quality of Life](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Quality_Of_Life/): Sort and Stack to chests leave the gear slots alone.
- [Quad's Ledger Chest](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Ledger_Chest/): items you take go into your ordinary rows.
- [Quad's Companion](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Companion/): what's in your gear slots isn't offered to your companion.

**Removing it:** take everything out of the gear slots first, or the game drops it at your feet the next time you spawn.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Gear%20Slots%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Only the players who want it.

<!-- HAND-WRITTEN: multiplayer -->
Nothing is shared: the slots are rows of your own inventory.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.gearslots.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**Food**

| Setting | Default | What it does |
|---|---|---|
| `AutoEat` | `false` | Eat from your Food slots by yourself: when a food you have in a slot is not active (and you have a free food slot), or its effect has dropped below half. Off by default. |
| `EatBelowPercent` | `20` | Auto-eat a food you are already under only once its time left drops below this percent (the game allows it from 50). Lower saves food: a meal then lasts 80% of its time instead of 50%, and its effect weakens a little towards the end. |

**General**

| Setting | Default | What it does |
|---|---|---|
| `ShowPanel` | `true` | Show the Gear panel next to your inventory. (Turn off and your gear slots hide; the items stay where they are.) |
| `ShowMessages` | `true` | Short messages when something happens (auto-eat, a quick slot is empty). |
| `ShieldFollowsWeapon` | `true` | When you switch to a one-handed weapon, put on the shield from your Shield slot too. (Two-handed weapons and bows take the shield off, as in the game.) |
| `AutoFill` | `true` | When you put on a piece of gear (or the game loads with it on), move it into its matching gear slot if that slot is empty. |

**Layout**

| Setting | Default | What it does |
|---|---|---|
| `Gap` | `6` | Space between the Gear panel and the inventory (UI pixels). |
| `OffsetX` | `0` | Move the Gear panel right (negative = left). |
| `OffsetY` | `0` | Move the Gear panel up (negative = down). |

**Quick slots**

| Setting | Default | What it does |
|---|---|---|
| `ShowUnderHotbar` | `true` | Show what is in your Quick slots, with their keys, in a row under the hotbar (the 1-8 on screen) while the inventory is closed. |
| `Key1` | `Z` | Press to use what is in quick slot 1: equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls. |
| `Key2` | `X` | Press to use what is in quick slot 2: equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls. |
| `Key3` | `C` | Press to use what is in quick slot 3: equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls. |
| `Key4` | `V` | Press to use what is in quick slot 4: equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls. |
| `Key5` | `None` | Press to use what is in quick slot 5: equips or unequips a weapon, tool or shield, or drinks a potion. Set to None to turn it off. If the game uses the same key for something (V: auto-pickup, X: sit, C: walk), the game's key is unbound once, so only this one works; bind it again in the game's Settings, Controls. |

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## 🧪 About Quads Lab

Quads Lab is where **Quad H** ([HardHeadHackerHead](https://github.com/HardHeadHackerHead)) builds software with AI, of every kind:
these Valheim mods are one of its projects. Each mod is designed by a player, written with [Claude Code](https://claude.com/claude-code)
and tested in our own world, with **bobisme** as our tester.

- 🌐 [quadslab.io](https://quadslab.io)
- 🧰 [All Quads Lab mods on Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/)
- 💻 [Source code on GitHub](https://github.com/HardHeadHackerHead/valheim-mods)
- 💬 [Discord](https://discord.gg/EwTjQxEZPy)
- 🐞 Found a bug? [Open an issue on GitHub](https://github.com/HardHeadHackerHead/valheim-mods/issues/new/choose)

## 📜 Recent changes

- **1.2.2** A clearer page: what it does, who needs it in multiplayer, how it gets on with other mods, and where to report a bug. The game itself is unchanged.
- **1.2.1** A new id, com.quad.gearslots (it was com.dhack.gearslots): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- Fix: next to other mods that change the inventory size, items were dropped on the ground at every spawn: with ValheimPlus at 7 to 9 rows everything in its extra rows, and with BetterArchery's quiver your armour row. The game now sizes the inventory as it does without this mod (with every other mod's changes), and the gear rows move to just under whatever height that gives, so with ValheimPlus you keep all its rows and the gear slots sit below them. Where the gear rows are is saved with your character. With a mod that keeps its own slots in the same cells (ExtraSlots, Equipment and Quick Slots, AzuExtendedPlayerInventory, Extended Player Inventory, ComfyQuickSlots, or BetterArchery with its quiver on) the gear slots now switch off: what was in them moves into your bag, and you are told in chat. The Gear panel's title is now in the game's own title lettering, with the braided line under it.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
