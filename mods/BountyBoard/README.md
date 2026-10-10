<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 📜 Quad's Bounty Board

<img src="cover.png" alt="Quad's Bounty Board" width="100%">

**A buildable notice board with contracts for the whole group, paid in coins and the materials of the stage you have reached.**

![version: 1.2.1](https://img.shields.io/badge/version-1.2.1-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Bounty_Board/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/BountyBoard)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Bounty%20Board%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Bounty_Board/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- Build it with the hammer at a workbench (Wood, Leather Scraps and Flint) and press E on it. Every board shows the same notices.
- Five notices are posted each in-game day: hunt a number of one creature, clear out a region's creatures, slay starred ones, or bring in loot.
- The whole server shares the contracts. The group can run three at a time, and a new one can only be taken when one is finished. Everyone's kills count towards them, and loot is handed in by whoever has it.
- When a contract is finished, every player collects their own reward at a board.
- The further you get, the harder it gets: each boss you beat opens the next region's creatures, and Hard and Brutal contracts (more to kill, better pay) show up more often.
- Rewards are materials you will actually use, matched to progress: resin and flint early, then copper, tin and bronze after Eikthyr, iron after the Elder, silver after Bonemass, black metal after Moder, and on up to Ashlands metals. Coins come too.
- Finished contracts raise the group's rank (Newcomers up to Legends), which adds a bonus to every reward.
- A switch in the board's menu shows the running contracts and their progress on your screen, so you can hunt with them in mind.
- It has its own hand-made look: a gabled roof, a horned skull, a hanging rune sign, pinned notices, a lantern and a bell.

The contracts are kept by the host's game, so BountyBoard has to be installed on the host too.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Bounty Board**

<img src="images/1.jpg" alt="The Bounty Board" width="100%">

**The day's notices: hunts, clear-outs and fetch jobs**

<img src="images/2.jpg" alt="The day's notices: hunts, clear-outs and fetch jobs" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **E** at a Bounty Board | Read the day's notices, take a contract, hand one in |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Bounty%20Board%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->
The host runs the group's contracts and keeps them in a file next to its settings; each player's progress is credited to whoever really played it. A kill counts when the game running that creature has the mod (in a group, usually the nearest player's).

**The host decides:** days between new notices, notices per board, most contracts at once and rewards. While you play on their world their values replace yours (your file isn't changed); a host without the mod leaves everyone their own.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.bountyboard.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**Contracts**

| Setting | Default | What it does |
|---|---|---|
| `MostAtOnce` | `3` | How many contracts the group can have going at once. A new one can only be taken when one is finished. In multiplayer the server's value applies. |
| `RewardPercent` | `100` | Pay as a percentage of the standard rate (50 = half, 200 = double). In multiplayer the server's value applies. |

**Notices**

| Setting | Default | What it does |
|---|---|---|
| `DaysBetweenNewNotices` | `1` | How many in-game days before the board posts new notices. In multiplayer the server's value applies. |
| `NoticesPerBoard` | `5` | How many notices are posted. In multiplayer the server's value applies. |

**Tracker**

| Setting | Default | What it does |
|---|---|---|
| `ShowOnScreen` | `false` | Show the group's contracts and their progress on your screen (also a switch in the board's menu). |
| `X` | `14` | Distance from the left edge of the screen (UI pixels). |
| `Y` | `330` | Distance from the top of the screen (UI pixels). |
| `Scale` | `1` | Size of the tracker. |

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

- **1.2.1** A new id, com.quad.bountyboard (it was com.dhack.bountyboard): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- **1.2.0** Fix: a finished contract's reward could be collected again and again with new characters. The host now goes by who is actually playing on each connection (not what the message says) and pays only the characters that worked on a contract (took it, made a kill for it or handed loot in) or were playing when it was finished. (Contracts finished before this update can still be collected as before.) In multiplayer the host's notice and contract settings now apply on everyone's screen too. The menu and tracker use the game's fonts, and a problem drawing the board's look can no longer stop it being registered.
- Fix: after a fresh game start the board was not registered until the mods were reloaded, so the game deleted any it found standing in the world. It is now registered as the world loads. Ones already lost can't be brought back; build them again. Fix: kills by any player now count in multiplayer (before, a kill only counted when the killer's own game was handling that creature). Fix: hosting a different world without restarting the game no longer carries the last world's contracts over and overwrites the new one's. Contracts are now kept by the world's id, so two worlds with the same name no longer share them (your existing contracts carry over).

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
