---
name: valheim-prerelease
description: Check a Valheim mod before releasing or updating it: the automatic pre-release check, a real restart, multiplayer and dedicated server, clashes with popular mods, the README. Use before publishing a mod, raising its version, or telling a player a fix is ready.
---

# Before releasing a Valheim mod

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (the command `help` lists them all). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

Players lose items and buildings to mod bugs that only show after a restart, in multiplayer, or next to another mod. Go through this
list every release; say what you checked and what you couldn't.

## 1. The automatic check

```
modcheck YourMod                   (an installed mod, by name, GUID or DLL name)
modcheck C:/path/to/YourMod.dll    (any build)
```

(A request file in `BepInEx/claude/requests`, or `claude modcheck YourMod` in the game's console; it works from the main menu.)

It reads the DLL and lists **problems**, **warnings** and **notes**, each with where, why and how to fix:

| Rule | What it means |
|---|---|
| takes-over-method | A prefix always skips the game's method: every other mod's changes to it are lost (a problem when popular mods patch it too). |
| rewrites-shared-method | A transpiler on a method popular mods also change. |
| replaces-custom-data | A whole `m_customData` dictionary replaced: other mods' saved data (backpack contents...) wiped. |
| custom-data-key | A short, plain `m_customData` key that can collide with another mod's. |
| recipe-cost | Counts recipe costs without `m_upgraderResource`: battle idols counted where the game doesn't charge them. |
| saves-zdoid | A ZDOID saved in a ZDO: it points at another object after a restart. |
| prefab-registration | Prefabs added without a `ZNetScene.Awake` patch: placed pieces deleted on a fresh launch. |
| inventory-height | An inventory made taller without handling `Player.SetInventorySize`: items dropped at every spawn. |
| no-unpatch | Harmony patches never removed: hot reload runs them twice. |
| unpatch-all | `UnpatchAll()` with no id: removes every mod's patches when this one unloads. |
| watcher-not-disposed | A file watcher left running after a hot reload. |
| game-key | A default key the game already uses (both actions happen). |
| find-every-frame | `FindObjectsOfType` in Update: stutters. |
| shader-find | `Shader.Find` can return null (and a throw while registering loses the piece). |
| same-job | Popular mods do the same job through other methods: read how, and detect them. |
| pays-twice | A prefix that skips the game's method and pays, deposits or gives itself, without reading `__runOriginal`: next to another mod doing the same, it's done twice. |
| container-not-chest | Collects containers without checking they're built pieces: a companion's bag, a cart or a grave gets emptied. |
| sellable-item | Gives an item a trader price (`m_value`): cheap to make, sold for coins. |
| own-settings | Settings and multiplayer, but nothing makes the server decide them: each player sets their own rewards. |
| assembly-name | The DLL's .NET assembly name is a popular mod's too: with both installed, one can be handed the other's assembly. |
| monomod-hook | A MonoMod `On.` hook: it can skip the game's method like a prefix (read it as one). |

They are candidates: read the code at `where`, fix what is real, and say why the rest is fine. Run it again after fixing: zero
problems before a release, and every warning either fixed or explained.

## 2. Clashes with popular mods

`clashes YourMod`: every `likely` needs an answer (see **When there's a clash** in **valheim-compat**: confirm it, fix your patch, detect
the other mod and stand down, or mark it incompatible and say so in the README). Read both patches for each `check` on the methods your mod depends on (see **valheim-compat**).

## 3. A real restart

Quit the game completely, start it, load a world that already has your mod's things in it, then `errors` and `log 300 Missing prefab`.
Look at the things themselves (placed pieces still there, items still in chests and the inventory, settings kept): `objects <your prefab>`
counts them all, loaded or not.

## 3b. Measure what it takes and gives

For anything that moves items (crafting, building, paying, rewards, deaths, chests), measure instead of looking: `inventory` and
`chests 10` before, do it once, the same after, and compare with what the recipe or rule says. Do it at least for: crafting from the bag
only, from chests only, and from both; a death and respawn with the mod's items on; quitting in the middle. A difference of one item is
a bug (paid half, paid twice, an idol counted, an item given before payment).

## 4. Multiplayer and dedicated servers

- Saved changes only from the owner (`ZNetView.IsOwner()`); other players ask through an RPC.
- A dedicated server has no player (`Player.m_localPlayer` is null), no camera and nothing is drawn: guard UI and visual code, and
  never let a visual failure stop a piece or item being registered.
- If the mod adds pieces or items, the server needs it too, and every player needs the same version.

## 4b. Settings and versions

- Gameplay settings synced from the server (ServerSync or Jotunn), look-and-feel ones local; test a synced setting on a dedicated server
  with a second player.
- Decide the version check: required (pieces, items, anything both sides must agree on) or optional (a client-side helper).
- Every word in `$` keys with an English translation file; settings with ranges and clear descriptions.
- Everything you registered is named after your mod (prefabs, RPCs, keys, commands; **valheim-compat** rule 11), and no command name
  hides another mod's: `help` lists every command with the mod it comes from.
- **Economy**: for anything that pays coins or items, work out what a player gets on average and whether quitting, a crash, a second
  character or their own config changes it (see "Rewards must not be gamed" in **valheim-pitfalls**). Items that sell (`m_value`)?

## 5. After a game update

`gameupdate` lists the game methods that are gone or changed since the last version, and which mods patch them. Re-read each changed
method your mod patches; transpilers are the first to break.

## 6. Ship only what you mean to

- **Uncommitted code ships.** A build script builds from your working folder, so a half-done feature or a debug command you were trying
  goes out with the release. Commit or stash everything that isn't part of it, then build. To check a built DLL holds no leftover, search
  it for a word from that code (strings in a .NET DLL are UTF-16, a zero byte after each letter: `grep -c -a "w.o.r.d" YourMod.dll` finds "word",
  or decompile it with ilspycmd).
- **Debug commands** that give items, move the player or remove things either check the game's cheat rule (single player, the host, or
  devcommands on) or stay out of the release. On a server, anything else lets every player cheat.
- **Only rebuild what changed.** A rebuild changes the DLL's bytes even when the code didn't, and the mod manager offers an update to
  everyone for it. Release only the mods whose source changed, and check the manifest lists only their new versions.

## 7. The release itself

Raise `Version`; one line per change in `CHANGELOG.txt` (newest first); README: what it does, settings, keys, **known clashes**;
the reload level `modcheck` gives (and, for HardHeadHackerHead's mod manager, `RESTART_REQUIRED.txt` when it isn't `world`). Build, check the built DLL is the one being published (its version, and
`modcheck` on that file), then publish. Say what you tested in the game and what you couldn't (multiplayer, a dedicated server).

On Thunderstore: **valheim-thunderstore** (`modkit package`, `tcli build`, the zip tested on a clean profile, then `tcli publish`).
