<!-- This page is made by tools/modpages/make_pages.py from the mod's DESCRIPTION.txt, CHANGELOG.txt, settings and the
     repo's README. Change those (or the HAND-WRITTEN part below), not the rest of this page. -->

# 🛡️ GearSlots

<img src="cover.png" alt="GearSlots" width="100%">

**Version 1.2.1**  ·  [all the mods](../../README.md)  ·  install it from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Gear_Slots/) (r2modman, Thunderstore Mod Manager) or the in-game [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) (**F7**)

A Gear panel next to your inventory: Head, Chest, Legs, Cape, Belt, Trinket, Ammo and Shield slots (drop gear in and you wear it), three Food slots, and five Quick slots with hotkeys that also show in a row under your hotbar. Your shield follows your one-handed weapon, and worn gear moves into its slot by itself. Auto-eat keeps you fed: the food slots are eaten when a meal is below 20% of its time left, so your food goes about 1.6 times as far.

<!-- HAND-WRITTEN: kept when this page is made again -->
## 📸 Screenshots

**The Gear panel beside your inventory: armour, cape, belt, trinket, ammo and shield slots**

<img src="images/1.jpg" alt="The Gear panel beside your inventory: armour, cape, belt, trinket, ammo and shield slots" width="100%">

## Known clashes

- **ValheimPlus** (extra inventory rows): works. You keep all of its rows and the gear rows sit below them. (Before 1.2.0 everything in
  its rows past the fourth was dropped at every spawn.)
- **ExtraSlots, Equipment and Quick Slots, AzuExtendedPlayerInventory, Extended Player Inventory, ComfyQuickSlots**: they keep their
  own slots in the same inventory cells, so the gear slots switch off while one of them is installed. Whatever was in your gear slots moves
  into your bag (if your bag is full, what didn't fit stays put and is tried again next time you log in), and you are told in chat.
- **BetterArchery**: its quiver (on by default) is a row in the same place, so the gear slots switch off as above. With its quiver turned
  off (Quiver, Enable Quiver = false) both work.
<!-- END HAND-WRITTEN -->

## 🔍 How it works

Extra inventory slots for what you wear, eat and use, in a Gear panel next to your inventory.

### What is in it

- Head, Chest, Legs, Cape, Belt, Trinket, Ammo and Shield slots: drop a matching piece in and you put it on. Armor and arrows no longer take room in your bag, and gear you are already wearing moves into its slot by itself.
- The shield follows your weapon: switch to a one-handed weapon and the shield from the Shield slot comes out with it (two-handed weapons and bows put it away).
- Three Food slots: keep your best meals in their own place (right-click to eat). Optional auto-eat is in the settings.
- Five Quick slots (Z, X, C, V by default; the fifth is unbound) for a weapon, tool, potion or anything else: press the key to equip or use it. With the inventory closed they show in a row under your hotbar, with their keys.
- Slots only accept the right kind of item, and Sort and Stack to Chests (QualityOfLife) leave them alone.
- Nothing is stored in a new place: the slots are extra rows of your real inventory, so saving, weight, crafting and dragging work as normal. Before removing the mod, move your gear-slot items into your bag: without it the game drops anything in those rows at your feet the next time you spawn.

Every key and option is in the settings (the manager's Mod settings tab).

## ⌨️ Keys

| Key | What it does |
|---|---|
| **Z** / **X** / **C** / **V** | Use what is in Quick slot 1 to 4 (equip a weapon or tool, drink a potion); the fifth slot has no key until you set one |

## ⚙️ Settings

In `BepInEx/config/com.quad.gearslots.cfg` (made the first time the game runs with the mod).

**General**

| Setting | Default | What it does |
|---|---|---|
| `ShowPanel` | `true` | Show the Gear panel next to your inventory. (Turn off and your gear slots hide; the items stay where they are.) |
| `ShowMessages` | `true` | Short messages when something happens (auto-eat, a quick slot is empty). |

**Food**

| Setting | Default | What it does |
|---|---|---|
| `AutoEat` | `false` | Eat from your Food slots by yourself: when a food you have in a slot is not active (and you have a free food slot), or its effect has dropped below half. Off by default. |
| `EatBelowPercent` | `20` | Auto-eat a food you are already under only once its time left drops below this percent (the game allows it from 50). Lower saves food: a meal then lasts 80% of its time instead of 50%, and its effect weakens a little towards the end. |

**General**

| Setting | Default | What it does |
|---|---|---|
| `ShieldFollowsWeapon` | `true` | When you switch to a one-handed weapon, put on the shield from your Shield slot too. (Two-handed weapons and bows take the shield off, as in the game.) |

**Quick slots**

| Setting | Default | What it does |
|---|---|---|
| `ShowUnderHotbar` | `true` | Show what is in your Quick slots, with their keys, in a row under the hotbar (the 1-8 on screen) while the inventory is closed. |

**General**

| Setting | Default | What it does |
|---|---|---|
| `AutoFill` | `true` | When you put on a piece of gear (or the game loads with it on), move it into its matching gear slot if that slot is empty. |

**Layout**

| Setting | Default | What it does |
|---|---|---|
| `Gap` | `6` | Space between the Gear panel and the inventory (UI pixels). |
| `OffsetX` | `0` | Move the Gear panel right (negative = left). |
| `OffsetY` | `0` | Move the Gear panel up (negative = down). |

## 👥 Playing together

See [who needs which mod](../../README.md#playing-together) on the front page.

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## 📜 Changes

- **1.2.1** A new id, com.quad.gearslots (it was com.dhack.gearslots): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- Fix: next to other mods that change the inventory size, items were dropped on the ground at every spawn: with ValheimPlus at 7 to 9 rows everything in its extra rows, and with BetterArchery's quiver your armour row. The game now sizes the inventory as it does without this mod (with every other mod's changes), and the gear rows move to just under whatever height that gives, so with ValheimPlus you keep all its rows and the gear slots sit below them. Where the gear rows are is saved with your character. With a mod that keeps its own slots in the same cells (ExtraSlots, Equipment and Quick Slots, AzuExtendedPlayerInventory, Extended Player Inventory, ComfyQuickSlots, or BetterArchery with its quiver on) the gear slots now switch off: what was in them moves into your bag, and you are told in chat. The Gear panel's title is now in the game's own title lettering, with the braided line under it.
- Auto-eat now waits until a food you are already under is below 20% of its time left (setting Food / EatBelowPercent, 1 to 50; the game allows eating again from 50%). A meal lasts 80% of its time instead of half, so your food goes about 1.6 times as far; its effect weakens a little towards the end.
- Fix: quick slots 2-4 (X, C, V) also did the game's sit, walk and auto-pickup toggle: pressing V for quick slot 4 turned auto-pickup off. The game's own key for those is now unbound (once, and you are told in chat), so only the quick slot happens; give them another key in the game's Settings, Controls if you want them. Auto-pickup is switched back on if it was left off.
- Fix: everything in the gear slots was dropped on the ground each time you spawned after starting the game (the game resets the inventory size on spawn since inventory rows can be bought). The gear rows now stay, and if you buy a row they move down under it.
- First release: a Gear panel with Head, Chest, Legs, Cape, Belt, Trinket, Ammo and Shield slots, three Food slots and five Quick slots (shown under the hotbar), a shield that follows your one-handed weapon, auto-fill of worn gear, and optional auto-eat.
