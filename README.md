<div align="center">

# ⚒️ Valheim Mods

**Quality-of-life mods for Valheim, built to play nicely together.**
Craft from chests. Build from chests. Plan builds as ghosts with your friends. Recycle the gear you never use.

![Valheim](https://img.shields.io/badge/Valheim-BepInEx-3b6e8f?style=flat-square)
![C#](https://img.shields.io/badge/made%20with-C%23-68217a?style=flat-square)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Mod Manager](https://img.shields.io/badge/get%20them%20with-the%20mod%20manager-f2c75c?style=flat-square)](https://github.com/HardHeadHackerHead/valheim-mod-manager)

</div>

---

## 🚀 Get them in one minute

Everything here installs and updates through the in-game **[mod manager](https://github.com/HardHeadHackerHead/valheim-mod-manager)**.
It already knows about this repo, so press **F7** in game, open **Browse** and click **Install**. No downloading DLLs, no restarting the game for most updates.

New to modding? The manager repo has a one-step installer that sets up BepInEx for you.

## 🧰 The mods

<table>
<tr>
<td width="50%" valign="top">

### 📦 CraftFromChests
<img src="dist/CraftFromChests.cover.png" alt="CraftFromChests" width="100%">

Crafting stations (and hand-crafting) use materials from nearby chests. See lines to every chest in use and change the range from 5 to 30 m.

</td>
<td width="50%" valign="top">

### 🔨 BuildFromChests
<img src="dist/BuildFromChests.cover.png" alt="BuildFromChests" width="100%">

Building with the hammer uses materials from chests near you. The build menu shows how much of everything you own in total.

</td>
</tr>
<tr>
<td valign="top">

### 🔥 FeedFromChests
<img src="dist/FeedFromChests.cover.png" alt="FeedFromChests" width="100%">

Feed smelters, kilns, cooking racks, fires and fermenters straight from your chests. Press **E** or **F** at a station, or hit **Fill**.

</td>
<td valign="top">

### 🎒 QualityOfLife
<img src="dist/QualityOfLife.cover.png" alt="QualityOfLife" width="100%">

Quick gear sets on **Q**, hammer on **B**, **Stack to chests** with undo, chest assignment on **K**, and item locks on **L**.

</td>
</tr>
<tr>
<td valign="top">

### 👻 BuildOrders
<img src="dist/BuildOrders.cover.png" alt="BuildOrders" width="100%">

Plan pieces as shared ghost build orders. Your party sees them, your placement snaps onto them, and a panel totals the materials you still need.

</td>
<td valign="top">

### 🧑‍🤝‍🧑 PartyHud
<img src="dist/PartyHud.cover.png" alt="PartyHud" width="100%">

A party panel with every player's Steam picture, health, stamina and distance, live and at any range.

</td>
</tr>
<tr>
<td valign="top">

### ♻️ Recycler
<img src="dist/Recycler.cover.png" alt="Recycler" width="100%">

A buildable Recycler that turns old weapons, armor and tools back into a share of their materials. Build Presses nearby to raise the share.

</td>
<td valign="top">

### 🧪 Your mod here
Want your own mods in the manager? See [Make your own mod repo](#-make-your-own-mod-repo).

</td>
</tr>
</table>

## ⌨️ Keys at a glance

| Key | Mod | What it does |
|---|---|---|
| **F7** | Mod manager | Open the mod manager |
| **Q** | QualityOfLife | Build a quick set (inventory open) / swap to it and back |
| **B** | QualityOfLife | Jump into construction mode with your hammer, and back |
| **K** / **L** | QualityOfLife | Assign what a chest receives / lock an item |
| **E** / **F** | FeedFromChests | Add to a station / always open its menu |
| **Left Alt** + place | BuildOrders | Plan a ghost instead of building |
| **G** / **Delete** / **F9** | BuildOrders | Select a ghost's piece / remove a ghost / show or hide ghosts |
| **F8** | PartyHud | Show or hide the party panel |

Every key and setting is configurable in `BepInEx/config`.

## 🛡️ Playing fair

These mods are about convenience, not cheating. Materials always come out of your inventory or a chest, recycling returns only part of what gear cost, and nothing is created from nothing.

## 🧑‍💻 For developers

```powershell
# build one mod and copy it into BepInEx\scripts, then press F6 in game to hot reload
dotnet build mods/CraftFromChests -c Release

# build everything and write dist/ (mod DLLs, cover images and manifest.json)
.\publish.ps1
```

- Each mod is a folder under `mods/` with its own `.csproj`, `Plugin.cs`, `DESCRIPTION.txt` (first paragraph is the summary, the rest is shown under **Details**), `CHANGELOG.txt` (first paragraph is "what is new") and an optional `cover.png`.
- Bump the `Version` constant in `Plugin.cs` for every change people should get.
- A mod that cannot be reloaded in game gets a `RESTART_REQUIRED.txt` explaining why.
- Set your game path once in `mods/Directory.Build.props`.
- Release: `.\publish.ps1`, then `git add -A; git commit; git push`.

## 🧱 Make your own mod repo

This repo is a **mod repo**: mod folders plus a `dist/` folder that the manager reads. Anyone can have one.
Copy the `template/` folder from the [mod manager repo](https://github.com/HardHeadHackerHead/valheim-mod-manager/tree/main/template), and to be watched by everyone by default, open a pull request there that adds your repo to `sources.json`.

## 📄 License

[MIT](LICENSE). Mods run inside your game with full access to your PC, so only install code you trust.
