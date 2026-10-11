<!-- This page is made by tools/modpages/make_pages.py from the mod's thunderstore.toml, DESCRIPTION.txt, CHANGELOG.txt,
     settings and the repo's README. Change those, or the HAND-WRITTEN parts, not the rest of this page. -->

# 🌊 Quad's JetSkis

<img src="cover.png" alt="Quad's JetSkis" width="100%">

**A jet ski for Valheim: a fast little boat for one (and a friend), pushed along by a surtling core's fire instead of the wind.**

![version: 0.2.2](https://img.shields.io/badge/version-0.2.2-3b6e8f?style=flat-square)  ![multiplayer: everyone + host](https://img.shields.io/badge/multiplayer-everyone%20%2B%20host-7a5c2e?style=flat-square)  ![made with: Claude Code](https://img.shields.io/badge/made%20with-Claude%20Code-c26a3d?style=flat-square)

[Source code](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/mods/JetSkis)  ·  [Report a bug](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20JetSkis%5D%20)  ·  [Discord](https://discord.gg/EwTjQxEZPy)

[![Join us on Discord](https://img.shields.io/badge/Join_us_on-Discord-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/EwTjQxEZPy)

**Install:** press **F7** in the game with our [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager), or take `JetSkis.dll` from [dist/](https://github.com/HardHeadHackerHead/valheim-mods/tree/main/dist). It isn't on Thunderstore.

## ✨ Features

### Riding it

- Build it with the hammer on the water, near a workbench, like the raft: 12 Fine Wood, 30 Bronze Nails, 2 Surtling Cores and 4 Deer Hide.
- Press E on its handlebars from the water or the shore and you climb onto the saddle and take them.
- Hold W to go, Shift+W for full speed, S to back up; let go and it coasts to a stop. The wind doesn't matter.
- A and D steer: the handlebars turn and come back to the middle by themselves. It turns slower when barely moving, and stays level in a hard turn.
- Jump to get off.
- You ride it like a jet ski: leaning over the bars, hands on the grips, feet on the pads, your head turning where you steer, bobbing over the waves. Everyone sees it.
- A gauge at the bottom right shows your speed and throttle, in place of the boat's wheel in the middle of the screen.
- Running into a friend at speed doesn't hurt them, and hopping waves barely scratches the hull.

### Room for two, and their things

- A passenger seat behind the rider (E on the back of the saddle): the passenger holds on round the rider's waist.
- Saddlebags on the back corners hold 8 stacks: E on them, or Tab while riding. If the jet ski breaks, what's in them floats away in a cargo crate, as from a Karve.

### The look and the sound

- A dark wooden hull with a band of red-stained wood and bronze rivets, a rub rail, a round shield on each side, a curled prow like a longship's, a leather saddle, a cowl up to iron handlebars, and a surtling core in an iron cage feeding the jet nozzle.
- Paint it: E on the cowl in front of the handlebars gives the next colour (red, blue, green, teal, purple, black, bare wood). Everyone sees it.
- An engine that burbles at idle and screams at full speed, spray thrown up behind it and off the side in hard turns, and a core that glows brighter (and lights the water further) as you open the throttle.
- It is the game's raft underneath, so it floats, wears, breaks, saves and changes hands in multiplayer as the game's boats do.

Everyone in the world needs it installed.

## 📸 Screenshots

<!-- HAND-WRITTEN: screenshots -->
**The jet ski: a curled prow, the red cowl and iron handlebars, the leather saddle and the caged surtling core**

<img src="images/1.png" alt="The jet ski" width="100%">

**Its side up close: the red band with bronze rivets, a round shield, the saddle and the core's cage**

<img src="images/2.png" alt="The jet ski's side up close" width="100%">
<!-- END HAND-WRITTEN: screenshots -->

## 🎮 How to use

| Key | What it does |
|---|---|
| **E** on a jet ski | Climb on (from the water or the shore) and take the handlebars |
| **W** / **Shift**+**W** / **S** | Go / full speed / back up (let go to coast); **A** / **D** steer, **Jump** gets off |
| **E** on a jet ski's cowl / saddlebags | Paint it the next colour / open the saddlebags (**Tab** while riding opens them too) |

<!-- HAND-WRITTEN: how-to-use -->

<!-- END HAND-WRITTEN: how-to-use -->

## 🧩 Other mods

<!-- HAND-WRITTEN: other-mods -->
**Known clashes:** none known.
<!-- END HAND-WRITTEN: other-mods -->

Found a clash with another mod? [Tell us](https://github.com/HardHeadHackerHead/valheim-mods/issues/new?template=bug.yml&title=%5BQuad%27s%20JetSkis%5D%20) which one and what happened.

## 👥 Multiplayer

**Who needs it:** Everyone, and **the host above all**: a game without the mod deletes the pieces when their area loads. Restart after installing or updating them.

<!-- HAND-WRITTEN: multiplayer -->

<!-- END HAND-WRITTEN: multiplayer -->

## ⚙️ Settings

In `BepInEx/config/com.quad.jetskis.cfg`, made the first time the game runs with the mod. r2modman and Thunderstore Mod Manager edit it under **Config editor**.<!-- GITHUB-ONLY --> With our mod manager: **F7**, then **Mod settings**.<!-- END GITHUB-ONLY -->

**1 - Driving**

| Setting | Default | What it does |
|---|---|---|
| `CruiseSpeed` | `11` | How fast it goes holding W (metres per second; a longship under full sail does about 10). [Synced with Server] |
| `FullSpeed` | `17` | How fast it goes holding Shift+W (metres per second). [Synced with Server] |
| `ReverseSpeed` | `4` | How fast it backs up holding S (metres per second). [Synced with Server] |
| `Acceleration` | `5` | How quickly it picks up speed (metres per second, each second). Braking is a little stronger. [Synced with Server] |
| `Coasting` | `2.5` | How quickly it slows down with nothing held (metres per second, each second). [Synced with Server] |
| `TurnRate` | `95` | How fast it turns at full lock (degrees per second). It turns slower when barely moving. [Synced with Server] |
| `Grip` | `3` | How much it holds its line in a turn instead of sliding sideways (higher grips more). [Synced with Server] |

**2 - Look**

| Setting | Default | What it does |
|---|---|---|
| `LeanIntoTurns` | `true` | The jet ski (and you on it) leans into turns and lifts its nose at speed, your head turns where you steer and you bob over the waves. |
| `ShowControls` | `true` | Show the controls the first time you get on in a session. |
| `EngineVolume` | `1` | How loud the engine is (0 is silent, 1 as made; the game's own volume settings apply on top). |

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

- **0.2.2** Spray you can see: a rooster tail of water arcing out of the nozzle that grows with your speed, a curl off each side of the bow, a sheet thrown off the outside of a hard turn, and a trail of foam left on the water behind you. It's made of the game's own splash, white mist and droplets (the old spray was flat patches of foam that didn't show in the air).
- **0.2.1** The passenger hugs the rider on every player's screen: each game now knows for sure who sits behind whom (it went by where their body was, which arrives late from another player's game), and puts other players exactly on their seats, so nobody trails behind a jet ski at speed. Both of you need 0.2.1.
- **0.2.0** You ride it like a jet ski now: leaning over the bars with your hands on the grips and feet on the pads, your head turning where you steer and bobbing over the waves (everyone sees it). A passenger seat behind, the passenger hugging the rider. Saddlebags (8 slots, a cargo crate if it breaks): E on them, or Tab while riding. Paint it with E on the cowl (red, blue, green, teal, purple, black, bare wood). An engine sound, spray off the nozzle and in hard turns, and a core that glows brighter with the throttle. A gauge at the bottom right (speed, throttle) in place of the boat's wheel. It sits right in the water (it rode too low), and stays level in hard turns. Shift+E is left to other mods (Wayfinder names boats with it). Restart the game to see the new parts on jet skis already built.

<!-- GITHUB-ONLY -->[Every change](CHANGELOG.txt)<!-- END GITHUB-ONLY --><!-- THUNDERSTORE-ONLY: Every change is on the Changelog tab. -->
