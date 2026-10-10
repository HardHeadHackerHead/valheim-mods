<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🪤 Quad's Bird Trap

<img src="cover.png" alt="Quad's Bird Trap" width="100%">

**A buildable bird trap: bait it with berries or seeds, leave it under the open sky, and come back to a gull to pluck for feathers.**

![version: 1.1.2](https://img.shields.io/badge/version-1.1.2-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Bird_Trap/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/BirdTrap)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Bird%20Trap%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Bird_Trap/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- Build it with the hammer at a workbench (8 Wood, 2 Leather Scraps). It is a slatted box with a drop-door propped up by a stick tied to the bait pedal.
- Use it to put bait in: raspberries, blueberries, cloudberries, barley, or beech, birch, carrot, turnip or onion seeds. Hold the alternate-place key (Shift) and use it to fill it at once. It holds five.
- Baited and with open sky above it, a bird comes in 8 to 14 minutes (game time, so it keeps going while you are away). It eats one bait, the prop falls and the door drops.
- Birds roost at night, and come at first light: sleep through the night and every trap you baited the day before has its bird in the morning. Under a roof none come, and the trap tells you why.
- Use it again to pluck the gull (2 or 3 feathers) and let it go. The door goes back up, and with bait left the next bird is on its way.
- Look at it to see how much bait is left and about how long until a bird. Build a few for a steady supply of feathers for arrows.
- It has its own hand-made look: wicker slats, a drop-door on a forked prop stick, berries on a leaf, and a grey-and-white gull that hops about and pecks once it's caught.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Bird Trap**

<img src="images/1.jpg" alt="The Bird Trap" width="100%">

**A row of traps along the fence**

<img src="images/2.jpg" alt="A row of traps along the fence" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **E** / Shift + **E** at a bird trap | Add bait (or pluck the bird) / fill it with bait |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Bird%20Trap%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->
A trap's bait, bird and timer are saved on the trap, so everyone sees the same trap, and two players can't both take one bird.

**The host decides:** catch times, bait, open sky, roosting at night and feathers per bird. While you play on their world their values replace yours (your file isn't changed); a host without the mod leaves everyone their own.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.birdtrap.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**Catching**

| Setting | Default | What it does |
|---|---|---|
| `MinutesMin` | `8` | Shortest wait (real minutes, in game time) from baiting to a bird. In multiplayer the server's value applies. |
| `MinutesMax` | `14` | Longest wait (real minutes, in game time) from baiting to a bird. In multiplayer the server's value applies. |
| `BaitHeld` | `5` | How many pieces of bait a trap holds (each bird eats one). In multiplayer the server's value applies. |
| `NeedsOpenSky` | `true` | Birds only come to a trap with open sky above it (not under a roof). In multiplayer the server's value applies. |
| `BirdsRoostAtNight` | `true` | No birds come at night: one due then comes in the morning. In multiplayer the server's value applies. |

**Plucking**

| Setting | Default | What it does |
|---|---|---|
| `FeathersMin` | `2` | Fewest feathers a bird gives. In multiplayer the server's value applies. |
| `FeathersMax` | `3` | Most feathers a bird gives. In multiplayer the server's value applies. |

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

- **1.1.2** A clearer page: what it does, who needs it in multiplayer, how it gets on with other mods, and where to report a bug. The game itself is unchanged.
- **1.1.1** A new id, com.quad.birdtrap (it was com.dhack.birdtrap): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- **1.1.0** Fix: two players plucking the same bird could both get its feathers, and bait could be lost. Whoever presses E now asks the trap's owner, which plucks the bird or takes the bait and answers: the feathers come only on its yes ("The bird is already gone" otherwise), and bait that doesn't fit comes back to you. In multiplayer the server decides the catching and plucking settings. A problem with the trap's look can no longer stop it being registered.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
