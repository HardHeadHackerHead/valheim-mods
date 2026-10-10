<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🚬 Quad's Cigars

<img src="cover.png" alt="Quad's Cigars" width="100%">

**Grow tobacco, cure it, roll your own cigars and smoke them: a real part of the game, from the wild plant to the cigar in your mouth.**

![version: 0.3.2](https://img.shields.io/badge/version-0.3.2-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Cigars/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/CigarSmoking)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Cigars%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Cigars/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- Find wild tobacco growing in the Meadows, the Black Forest and the Plains. Each biome has its own strain, a little different in leaf and flower. Picking a plant gives leaves and seeds.
- Sow seeds with the cultivator in cultivated soil. A plant grows tall in about 25 minutes and gives leaves and more seeds. Every strain can be farmed in the Meadows, the Black Forest and the Plains.
- Build a Tobacco Drying Rack: hang fresh leaves on it and take them back dried. Then pack the dried leaves into a Tobacco Curing Barrel to age them.
- Build a Cigar Rolling Table (near a workbench) and roll cigars in the game's own crafting menu. Build a Humidor beside the table to roll the finer cigars.
- Light a cigar from your inventory or hotbar. You smoke for 5 minutes with a cigar in your hand or mouth, an ember and drifting smoke; with a weapon in your right hand it stays in your mouth. Other players with the mod see it too.

The cigars (one at a time; lighting another starts it fresh):
- Connecticut (Dried Meadow Leaf, Resin): Relaxed, stamina regenerates 15% faster.
- Maduro (Aged Forest Leaf, Honey, Resin; needs a Humidor): Smooth, health regenerates 40% faster.
- Corojo (Aged Forest Leaf, Thistle, Resin; needs a Humidor): Shadow, you make less noise and are harder to spot.
- Habano (Aged Plains Leaf, Cloudberries, Resin; needs a Humidor): Fired Up, attacks and sprinting cost less stamina.

Wild tobacco only appears in parts of the world you have not visited yet. It adds new items, pieces and plants. Restart the game after installing or updating it.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The Cigar Rolling Table**

<img src="images/1.jpg" alt="The Cigar Rolling Table" width="100%">

**The tobacco Drying Rack**

<img src="images/2.jpg" alt="The tobacco Drying Rack" width="100%">

**The Curing Barrel**

<img src="images/3.jpg" alt="The Curing Barrel" width="100%">

**A Maduro cigar**

<img src="images/4.jpg" alt="A Maduro cigar" width="100%">

**Out in the Meadows with a cigar**

<img src="images/5.jpg" alt="Out in the Meadows with a cigar" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.

Add-ons (such as a pipes mod) can hook their smoke into it so only one smoke runs at a time: see [docs/cigars-api.md](../../docs/cigars-api.md).
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Cigars%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->
A lit cigar is saved on the player, so friends with the mod see it and its smoke. The rack and barrel hand out each batch once, whoever takes it. Wild tobacco grows only in areas first visited after installing the mod.

**The host decides:** how long a cigar lasts, its strength, and growing, drying and curing times. While you play on their world their values replace yours (your file isn't changed); a host without the mod leaves everyone their own.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.cigarsmoking.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**Growing**

| Setting | Default | What it does |
|---|---|---|
| `TobaccoGrowMinutes` | `25` | How long a tobacco plant takes to grow (minutes). Applies to plants as they load. In multiplayer the server's value applies. |
| `DryingMinutes` | `2` | How long a batch of leaves takes to dry on the rack (minutes). Applies to batches put in after a change. In multiplayer the server's value applies. |
| `CuringMinutes` | `6` | How long a batch of leaves takes to age in the barrel (minutes). Applies to batches put in after a change. In multiplayer the server's value applies. |

**Look**

| Setting | Default | What it does |
|---|---|---|
| `DrawSmoke` | `true` | Draw the smoke curling up from smoking players. |
| `DrawGlow` | `true` | Give the ember a small flickering light (nice at night). |

**Smoking**

| Setting | Default | What it does |
|---|---|---|
| `Minutes` | `5` | How long one cigar lasts (minutes). In multiplayer the server's value applies. |
| `EffectStrength` | `100` | How strong the cigars' bonuses are (percent of the default). 0 for none. In multiplayer the server's value applies. |

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

- **0.3.2** A clearer page: what it does, who needs it in multiplayer, how it gets on with other mods, and where to report a bug. The game itself is unchanged.
- **0.3.1** A new id, com.quad.cigarsmoking (it was com.dhack.cigarsmoking): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- **0.3.0** The Drying Rack and Curing Barrel are safe with friends around: putting leaves in and taking them out now goes through the piece's owner, as the game's fermenter does, so two players taking a batch at once no longer both get it, and leaves put in at the same moment as someone else's come back to you instead of vanishing. They respect wards, can't be torn down while they hold leaves (the game says it can't be removed now), and drop what they held when broken. A tobacco plant you sowed and harvested is now removed like the game's own crops, instead of staying hidden in the world for ever (old ones are cleared as their area loads). On a server, the cigar length and strength and the growing, drying and curing times are the server's, and a batch's finish time is saved when it goes in. A model that fails to build no longer stops the pieces registering, and lighting cigars no longer makes a new material each time.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
