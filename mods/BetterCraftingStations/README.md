<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🔧 Quad's Crafting Filters

<img src="cover.png" alt="Quad's Crafting Filters" width="100%">

**Recipe filter chips above the crafting list: pick a type, a tier or what you can make now, at every crafting station.**

![version: 0.1.3](https://img.shields.io/badge/version-0.1.3-3b6e8f?style=flat-square)  ![multiplayer: only who wants it](https://img.shields.io/badge/multiplayer-only%20who%20wants%20it-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Crafting_Filters/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/BetterCraftingStations)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Crafting%20Filters%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Crafting_Filters/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

### What is in it

- At the workbench, forge, cauldron and the other stations, two rows of chips sit above the recipe list. The first row is the type of thing (weapons, armor, tools, ammo, food, potions), with All and Can craft; the second is how far into the game it is (wood and flint, bronze, iron, silver, black metal, Mistlands, Ashlands).
- Each chip shows how many recipes it has, given what the other row has chosen. Click a chip to pick it, click it again to clear it. All clears everything.
- Only what that station makes shows, and each station remembers its own choice while the game runs.
- The list below is the game's own, narrowed: crafting, upgrading and the Upgrade tab are unchanged.

The only setting is Enabled.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**A crafting station with its upgrades round it**

<img src="images/1.png" alt="A crafting station with its upgrades round it" width="100%">

**The station's menu**

<img src="images/2.png" alt="The station&#x27;s menu" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.

It narrows the game's own recipe list in place (it never swaps the list for another), so other mods that change the list see the same one.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Crafting%20Filters%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Only the players who want it.

<!-- HAND-WRITTEN: multiplayer -->
Nothing is shared: it only changes your own screen.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.bettercraftingstations.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**General**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Show the filter chips above the crafting list. Off, the list is the game's own. |

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

- **0.1.3** A clearer page: what it does, who needs it in multiplayer, how it gets on with other mods, and where to report a bug. The game itself is unchanged.
- **0.1.2** A new id, com.quad.bettercraftingstations (it was com.dhack.bettercraftingstations): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- Fix: the chips no longer swap the game's recipe list for a new one (other mods that change the list now see what is shown), the chips can't be pressed by the gamepad buttons of the real tabs any more and no longer cause errors every frame, and clicking a chip no longer writes a line to the log.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
