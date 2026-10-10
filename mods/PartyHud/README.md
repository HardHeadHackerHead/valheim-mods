<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🧑‍🤝‍🧑 Quad's Party HUD

<img src="cover.png" alt="Quad's Party HUD" width="100%">

**A party panel on the right of the screen showing every player in your world.**

![version: 1.8.1](https://img.shields.io/badge/version-1.8.1-3b6e8f?style=flat-square)  ![multiplayer: only who wants it](https://img.shields.io/badge/multiplayer-only%20who%20wants%20it-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

<!-- GITHUB-ONLY -->[Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Party_HUD/)  ·  <!-- END GITHUB-ONLY -->[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/PartyHud)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Party%20HUD%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

<!-- GITHUB-ONLY -->**Install:** with [r2modman](https://thunderstore.io/c/valheim/p/ebkr/r2modman/) or Thunderstore Mod Manager from [Thunderstore](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Party_HUD/), or press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager).<!-- END GITHUB-ONLY -->

## ✨ Features

### What it shows

- Each player's Steam picture, name, health, stamina and distance, live, at any range.
- Each picture has a ring that is green, amber or red by how healthy that player is, and an arrow next to their distance points the way they are, relative to where you are looking. F8 hides or shows the panel (handy for screenshots). It hides itself while menus are open.

- With the AICompanion mod: each player's companion shows under them (health, distance, what it is doing).

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The party panel on the right of the screen**

<img src="images/1.jpg" alt="The party panel on the right of the screen" width="100%">

**Close up: health, stamina, distance and the arrow to each player**

<img src="images/2.jpg" alt="Close up: health, stamina, distance and the arrow to each player" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **F8** | Show or hide the party panel |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.

**Works with our mods**

- [Quad's Companion](https://thunderstore.io/c/valheim/p/Quads_Lab/Quads_Companion/): each player's companion shows under them (health, distance, what it's doing).
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Party%20HUD%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Only the players who want it.

<!-- HAND-WRITTEN: multiplayer -->
Players with the mod share their exact health, stamina, Eitr, food and buffs at any distance (ShareMyStats is each player's own choice); players without it show what the game shares nearby. **The host decides** whether stats are shared at all (AllowSharing, for PvP servers).
<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.partyhud.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**General**

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Show the party panel. |
| `ShowSelf` | `true` | Include yourself in the panel. |
| `ShowPortraits` | `true` | Show each player's Steam profile picture. Off (or no picture available) = a coloured initial. |
| `ShowDistance` | `true` | Show how far away each player is. |
| `HideInMenus` | `true` | Hide the panel while the inventory, crafting window, build menu, trader or big map is open, so it doesn't overlap them. |
| `MaxPlayers` | `8` | Most players to show (you first, then the rest alphabetically). |
| `ToggleKey` | `F8` | Press to hide or show the panel (handy for screenshots). |
| `ShowDirectionArrow` | `true` | Next to each player's distance, an arrow pointing the way they are, relative to where you are looking. |
| `ShowFood` | `true` | Show three food slots under each player's picture: what they are eating and how long it has left. Only for players who also have this mod. |
| `ShowStatusEffects` | `true` | Show each player's buffs and debuffs (food, rested, wet, poison...) as small icons under their bars. Only for players who also have this mod. |
| `ShowCompanions` | `true` | With the AICompanion mod: show each player's companion under them (health, distance, what it is doing). |

**Layout**

| Setting | Default | What it does |
|---|---|---|
| `OffsetX` | `10` | Gap from the right edge of the screen (UI pixels). |
| `OffsetY` | `300` | Gap from the top of the screen (UI pixels). Raise it if it overlaps your minimap. |
| `Scale` | `1` | Size of the panel (1 = normal, 1.25 = bigger). |
| `AvoidShipHud` | `true` | While you are steering a ship, move the panel down so it does not cover the wind indicator and the rest of the ship display. |
| `Compact` | `false` | A smaller, tighter panel: a small picture and thinner bars, for when you want it out of the way. |
| `OnLeft` | `false` | Put the panel on the left edge of the screen instead of the right. |
| `Opacity` | `0.85` | How solid the panel background is (0.2 to 1). |

**Sharing**

| Setting | Default | What it does |
|---|---|---|
| `ShareMyStats` | `true` | Send your health, stamina, Eitr, food and buffs to the other players who have this mod (a few times a second, at any distance). Off: they only see what the game itself shows of you when you are near. |
| `AllowSharing` | `true` | Players with this mod share their exact health, stamina, Eitr, food and buffs with each other at any distance. A PvP server may want this off (each player then sees only what the game shows of players near them). In multiplayer the server's value applies. |

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

- **1.8.1** A new id, com.quad.partyhud (it was com.dhack.partyhud): your settings move over by themselves the first time it starts, and the old settings file is kept as a backup. Restart the game after this update. If an old copy is still installed beside it, this one stands down and says which file to delete, so the two never both run. The DLL now says it was made with AI, as Thunderstore asks.
- New: Sharing/ShareMyStats (on by default): turn it off and your game stops sending your health, stamina, Eitr, food and buffs to the other players; they then see only what the game itself shows of you nearby. Sharing/AllowSharing is decided by the server in multiplayer, so a PvP server can switch the sharing off for everyone. The panel's names and numbers are now in the game's own lettering.
- A fallen companion's row says its things are in a tombstone (they keep the gear they wore since AICompanion 0.8), not "gear in a crate".

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
