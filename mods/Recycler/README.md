<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# ♻️ Quad's Recycler

<img src="cover.png" alt="Quad's Recycler" width="100%">

**Build a Recycler and turn old weapons, armor and tools back into a share of their materials: recycle, salvage and reclaim the gear you've outgrown.**

![version: 1.3.0](https://img.shields.io/badge/version-1.3.0-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Recycler/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/Recycler)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Recycler%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Recycler/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- Look at the Recycler and press E: pick a weapon, armor piece or tool from your inventory, see what it would return, and recycle it.
- You get about half of the materials back (50% by default; leftovers are a chance of one more). Recycler Presses built within 8 m raise this to 60% and then 70%.
- The gear must be one you could craft, and the crafting station it was made at (and its level) must be nearby. Equipped or upgraded gear asks for a second click. Items locked with L are skipped.
- The Recycler needs only bronze-tier materials (fine wood, bronze and stone), so you can build it as soon as you have bronze. Each Press costs iron, bronze and stone, so the better refunds come with iron. Restart the game after installing or updating it.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Recycler**

<img src="images/1.jpg" alt="The Recycler" width="100%">

**Its window: your gear on the left, what the chosen piece gives back on the right**

<img src="images/2.jpg" alt="The Recycler's window: your gear, and what the hammer gives back" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes**

- **Adventure Backpacks** (or any mod that refuses to let an item go): a backpack that still holds things gives nothing back, because it never leaves your inventory.
- **Epic Loot** and other mods that keep data on items: such gear (enchantments, a bag's contents) asks for a second click, because recycling destroys it.

**Works with our mods**

- [Quad's Quality of Life](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Quality_Of_Life/): items locked with L can't be recycled.
- Our chest mods never treat the Recycler as a storage chest.

Battle idols are never handed back, and levels gained at the upgrader station return nothing.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Recycler%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->
**The host decides:** the share returned, Press bonuses, chance rounding, the crafting-station rule and which gear can be recycled. While you play on their world their values replace yours (your file isn't changed); a host without the mod leaves everyone their own.
<!-- END HAND-WRITTEN: multiplayer -->

## 🌍 Languages

**English, Русский (Russian), Deutsch (German), 简体中文 (Chinese), Português do Brasil (Portuguese), Español (Spanish), Français (French), Polski (Polish), Türkçe (Turkish).** It follows the game's own language setting.

The translations were made with AI, so a few words may be off. To correct one, or to add your language, put a file in `BepInEx/config/translations/com.quad.recycler/` named after the game's name for the language (`German.json`, `Italian.json`, `Japanese.json`...) with only the lines you change, copied from [English.json](https://github.com/HardHeadHackerHead/valheim-mods/blob/main/mods/Recycler/translations/English.json) (it wins over the built-in one). Or [tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Recycler%5D%20Translation%3A%20) the better words and we'll put them in for everyone.

## ⚙️ Settings

In `BepInEx/config/com.quad.recycler.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**General**

| Setting | Default | What it does |
|---|---|---|
| `ShowMessages` | `true` | Show a short message in the top-left when something is recycled. |

**Refund**

| Setting | Default | What it does |
|---|---|---|
| `BasePercent` | `50` | Share of the materials you get back with no Press nearby (0-100). In multiplayer the server's value applies. |
| `OnePressBonus` | `10` | Extra percentage points with one Press within 8 m. In multiplayer the server's value applies. |
| `TwoPressBonus` | `20` | Extra percentage points with two Presses within 8 m. In multiplayer the server's value applies. |
| `ChanceRounding` | `true` | Instead of always rounding down, a leftover fraction becomes a matching chance of one more (so 50% averages out to 50%). In multiplayer the server's value applies. |

**Rules**

| Setting | Default | What it does |
|---|---|---|
| `RequireCraftingStation` | `true` | Gear can only be recycled with the crafting station (and level) it was made at nearby. In multiplayer the server's value applies. |
| `ConfirmValuable` | `true` | Ask for a second click before recycling equipped or upgraded gear, or gear carrying other mods' data (enchantments, a bag's contents). |
| `AllowWeapons` | `true` | Weapons, bows and ammo launchers. In multiplayer the server's value applies. |
| `AllowArmor` | `true` | Armor, capes and shields. In multiplayer the server's value applies. |
| `AllowTools` | `true` | Tools and torches. In multiplayer the server's value applies. |

## 🤖 Made with AI

Made with the help of Claude (Anthropic), with Claude Code: designed, written and checked together, and tried in the game.

## 💬 Join us on Discord

<a href="https://discord.gg/EwTjQxEZPy"><img src="https://raw.githubusercontent.com/HardHeadHackerHead/valheim-mods/main/tools/modpages/discord-banner.jpg" alt="Join the Quads Lab Discord" width="100%"></a>

Come hang out with the people who make and play these mods. In the **Quads Lab Discord** you can:

- 🛠️ **Get help** and report bugs straight to us
- 👀 **See new mods first**, and help test them before they're released
- 🗳️ **Suggest ideas** and vote on what we make next
- 🏰 **Share your builds**, bases and arena runs
- 🤖 **Talk AI coding and vibe coding** with people building things with AI, the way these mods are made

**[Join the Quads Lab Discord →](https://discord.gg/EwTjQxEZPy)**

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

- **1.3.0** Now in your language: English, Russian, German, Chinese (Simplified), Portuguese (Brazil), Spanish, French, Polish and Turkish, following the game's own Language setting. The game's own names are used for its things (items, creatures, lands, stations). The translations were made with AI: to correct a line, or add a language, put a file in BepInEx/config/translations/com.quad.recycler/ named after the game's name for the language (German.json...) with only the lines you change, copied from translations/English.json on GitHub. The Recycler and the Press have translated names and hover texts too.
- **1.2.1** Tab no longer opens your inventory under the Recycler's window (it closes the window, as it closes a chest), and the chat and map keys stay quiet while it's open.
- **1.2.0** The Recycler's window is now part of the game's look: the crafting screen's wood panel and title, your gear in the inventory's own cells (icons, durability, quality, what you wear, the game's tooltips), the materials you get back in cells with their amounts, and the game's own button. E closes it again, as it does a chest.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
