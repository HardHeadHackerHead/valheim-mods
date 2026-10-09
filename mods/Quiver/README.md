<!-- This page is made by tools/modpages/make_pages.py from the mod's DESCRIPTION.txt, CHANGELOG.txt, settings and the
     repo's README. Change those (or the HAND-WRITTEN part below), not the rest of this page. -->

# 🏹 Quiver

<img src="cover.png" alt="Quiver" width="100%">

**Version 0.1.0**  ·  [all the mods](../../README.md)  ·  installs and updates through the in-game [mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager) (**F7**)

Pick your arrows back up. Those that hit the ground, a tree or a wall are all kept as arrows you pick up; of those that hit a creature, three
in four drop with its loot when it dies. A quiver hangs on your back while you have arrows equipped, with the arrows you carry sticking out of it.
Only you need the mod.

<!-- HAND-WRITTEN: kept when this page is made again -->
## 📸 Screenshots

**The bow and quiver carried on your back**

<img src="images/1.jpg" alt="The bow and quiver carried on your back" width="100%">
<!-- END HAND-WRITTEN -->

## 🔍 How it works

Pick your shot arrows back up, and carry them in a quiver on your back.

### What is in it

- Arrows (and crossbow bolts) you shoot that hit the ground, a tree, a wall or a creature come back as arrows you pick up again. Arrows that hit the ground, a tree or a wall are all kept; of those that hit a creature, three in four are kept and drop with its loot when it dies. Fire arrows are always used up. All of it is in the settings.
- A quiver hangs on your back while you have arrows or bolts equipped, with the arrows you carry sticking out of it (more of them the more you carry), made from the real model of the arrow you have equipped.
- Where the quiver hangs (back, side, height, tilt) is in the settings, and changes at once.

It works on the game that shoots, so it does not need the other players to have it.

## ⚙️ Settings

In `BepInEx/config/com.dhack.quiver.cfg` (made the first time the game runs with the mod).

**Arrows**

| Setting | Default | What it does |
|---|---|---|
| `PickUpArrows` | `true` | Arrows you shoot that hit something land on the ground as arrows you can pick up again. |
| `ChanceOnGround` | `1` | The chance an arrow that hit the ground, a tree, a wall or the like is kept. 1 keeps every one. |
| `ChanceOnCreature` | `0.75` | The chance an arrow that hit a creature is kept: it drops with that creature's loot when it dies. 0.75 keeps three in four. |
| `IncludeBolts` | `true` | Crossbow bolts can be picked up too. |
| `FireArrowsBurnUp` | `true` | Fire arrows are always used up. |
| `LogHits` | `false` | Write what each arrow hit did (kept, broke, why not) to the BepInEx log. For finding out why an arrow did not come back. |

**Quiver**

| Setting | Default | What it does |
|---|---|---|
| `Show` | `true` | A quiver on your back while you have arrows (or bolts) equipped. |
| `Back` | `0.2` | How far behind your spine the quiver hangs (metres). |
| `Side` | `0.1` | How far to your right it hangs (negative: left). |
| `Height` | `-0.03` | How far up it hangs (negative: lower). |
| `TiltSide` | `22` | How far the top leans to your right (degrees). |
| `TiltBack` | `8` | How far the top leans backwards (degrees). |

## 👥 Playing together

See [who needs which mod](../../README.md#playing-together) on the front page.

## 📜 Changes

- New: arrows you shoot come back. Those that hit the ground, a tree or a wall are all kept as arrows you pick up; of those that hit a creature, three in four drop with its loot when it dies (only you need the mod). A quiver hangs on your back while you have arrows equipped, with the arrows you carry sticking out of it.
