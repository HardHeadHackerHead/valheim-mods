<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🎰 Quad's Slot Machine

<img src="cover.png" alt="Quad's Slot Machine" width="100%">

**Odin's Fortune: a buildable slot machine. Put coins in, pull the lever, and the winnings come out of the tray.**

![version: 1.1.1](https://img.shields.io/badge/version-1.1.1-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Slot_Machine/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/SlotMachine)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Slot%20Machine%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Slot_Machine/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- Build it with the hammer at a workbench: Fine Wood, Bronze and Stone.
- Press E to pull the lever. Your bet goes in, three reels spin and stop one after another, and a win is spat out of the tray as coins.
- Press the alternate-use key with E to change the bet (5, 10, 25, 50 or 100 coins).
- Three alike pay the most, from three Coins to three Valknuts (300 times your bet). Pairs of Coins, Ravens and Valknuts pay a little.
- Everyone nearby sees the same spin. The lamps chase each other while the reels turn and flash after a win.
- Over a long time it gives back about 93% of what goes in: a fun way to lose a few coins, not a way to make them.

It has its own hand-made look: carved dark wood and gold, a raven on top, a rune-marked marquee, a lever, buttons and a brass payout tray.

Settings let you change the bet and scale the prizes.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Slot Machine**

<img src="images/1.jpg" alt="The Slot Machine" width="100%">

**The machine at home, with the winnings piled up beside it**

<img src="images/2.jpg" alt="The machine at home, with the winnings piled up beside it" width="100%">

**Its reels, its lamps and the coin tray**

<img src="images/3.jpg" alt="Its reels, its lamps and the coin tray" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **E** / alternate-use + **E** at a slot machine | Pull the lever / change the bet |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Slot%20Machine%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->
Each spin is saved on the machine, so everyone nearby watches the same spin, and whoever pulled gets the coins.

**The host decides:** the payout percentage. While you play on their world their values replace yours (your file isn't changed); a host without the mod leaves everyone their own.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.slotmachine.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**Play**

| Setting | Default | What it does |
|---|---|---|
| `Bet` | `10` | Coins per pull (change it at the machine with the alternate-use key + use). |
| `PayoutPercent` | `100` | Scales every prize (100 = standard, which returns about 93% over time; 50 = half the prizes; above 107 the machine pays out more than goes in). In multiplayer the server's value applies. |

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

- **1.1.1** A new id, com.quad.slotmachine (it was com.dhack.slotmachine): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- The machine costs what it should again: 15 Fine Wood, 8 Bronze and 10 Stone (at a workbench), no longer the single Fine Wood it cost while it was being tested. In multiplayer the server decides the payout (PayoutPercent), so nobody can set their own machine to pay out more than goes in.
- Fix: after a fresh game start the slot machine was not registered until the mods were reloaded, so the game deleted any it found standing in the world. It is now registered as the world loads. Ones already lost can't be brought back; build them again. Fix: logging out, or walking away so the machine unloads, before the reels stop no longer loses your bet's winnings: they go into your inventory.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
