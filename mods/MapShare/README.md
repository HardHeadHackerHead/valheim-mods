<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🗺️ Quad's Map Share

<img src="cover.png" alt="Quad's Map Share" width="100%">

**Share the map you uncover with everyone in the world, live, as you run through the fog of war.**

![version: 1.1.1](https://img.shields.io/badge/version-1.1.1-3b6e8f?style=flat-square)  ![multiplayer: only who wants it](https://img.shields.io/badge/multiplayer-only%20who%20wants%20it-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Map_Share/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/MapShare)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Map%20Share%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Map_Share/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

- The cartography table only shares a map by hand: you write your map to the table, then others read it one visit at a time. This mod does it automatically instead.
- Every few seconds, the new parts of the map you uncovered are sent to the other players and appear on their maps.
- When someone joins, one of the players already there sends them the whole map, and the newcomer sends theirs back, so everyone ends up with the same explored area.
- Only the explored area is shared, not pins. Players without the mod do not take part.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**One map for everyone: what each player has explored, and their pins**

<img src="images/1.jpg" alt="One map for everyone: what each player has explored, and their pins" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Map%20Share%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Only the players who want it.

<!-- HAND-WRITTEN: multiplayer -->
Players without the mod simply don't take part. New areas anyone explores go to the others, and a player who joins gets the whole map from someone who has it. Pins aren't shared.

**The host decides** whether sharing is allowed (AllowSharing). A host without the mod leaves it on.
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.mapshare.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**General**

| Setting | Default | What it does |
|---|---|---|
| `ShareMyMap` | `true` | Send the parts of the map you uncover to the other players in the world. |
| `ReceiveSharedMap` | `true` | Show the parts of the map other players uncover on your own map. |
| `SendInterval` | `2` | Seconds between sending newly uncovered map cells. |
| `AllowSharing` | `true` | Players with this mod share the map they uncover with each other. A server that wants everyone to explore for themselves turns this off. In multiplayer the server's value applies. |

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

- **1.1.1** A new id, com.quad.mapshare (it was com.dhack.mapshare): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- New: General/AllowSharing, decided by the server in multiplayer: a server where everyone should explore for themselves can turn map sharing off for all. Fix: a map that arrived only partly (its sender left) was kept in memory for the rest of the session, and a shared map is now only unpacked up to the size of this world's map, so nobody can send something that unpacks to a huge size.
- Fix: a player joining could miss the map explored so far when someone online did not have MapShare or had sharing off. Everyone who shares now sends it.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
