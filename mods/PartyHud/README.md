<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🧑‍🤝‍🧑 Quad's Party HUD

<img src="cover.png" alt="Quad's Party HUD" width="100%">

**A party HUD: health bars, stamina and distance for every player in your group, live and at any range, on the right of the screen.**

![version: 1.10.0](https://img.shields.io/badge/version-1.10.0-3b6e8f?style=flat-square)  ![multiplayer: only who wants it](https://img.shields.io/badge/multiplayer-only%20who%20wants%20it-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

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

## 🌍 Languages

**English, Русский (Russian), Deutsch (German), 简体中文 (Chinese), Português do Brasil (Portuguese), Español (Spanish), Français (French), Polski (Polish), Türkçe (Turkish).** It follows the game's own language setting.

The translations were made with AI, so a few words may be off. To correct one, or to add your language, put a file in `BepInEx/config/translations/com.quad.partyhud/` named after the game's name for the language (`German.json`, `Italian.json`, `Japanese.json`...) with only the lines you change, copied from [English.json](https://github.com/HardHeadHackerHead/valheim-mods/blob/main/mods/PartyHud/translations/English.json) (it wins over the built-in one). Or [tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20Party%20HUD%5D%20Translation%3A%20) the better words and we'll put them in for everyone.

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

- **1.10.0** Now in your language: English, Russian, German, Chinese (Simplified), Portuguese (Brazil), Spanish, French, Polish and Turkish, following the game's own Language setting. The game's own names are used for its things (items, creatures, lands, stations). The translations were made with AI: to correct a line, or add a language, put a file in BepInEx/config/translations/com.quad.partyhud/ named after the game's name for the language (German.json...) with only the lines you change, copied from translations/English.json on GitHub. Fixed: names and status lines no longer vanish in Chinese.
- **1.9.0** The party panel is now part of the game's own HUD, made from its own pieces: the inventory's sunken boxes and bars, the game's lettering, and the minimap's arrow for which way each player is. It hides with the HUD (and in cutscenes), follows the interface scale you set in the game's settings, and is lighter to draw: rows are only built again when someone joins or leaves or you change a setting. Every setting still works.
- **1.8.2** A clearer page: what it does, who needs it in multiplayer, how it gets on with other mods, and where to report a bug. The game itself is unchanged.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
