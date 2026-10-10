---
name: valheim-compat
description: Make a Valheim mod work alongside other mods, find which popular mods change the same things, download and read another mod's code to see how it works, and integrate with it. Use before patching a game method, when a player reports a conflict, or when a mod must work with a specific other mod.
---

# Playing well with other mods

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (the command `help` lists them all). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

Players run dozens of mods, and they all patch the same game. A patch is a guest in someone else's method: other mods' patches run on it
too. Two mods patching one method is normal; a clash needs one of them to take the method away from the others.

## The rules

1. **Postfix by default.** Run after the game's code and add to what it did.
2. **Skip the game's method (prefix returning false) only for your own things**: check the instance first (`if (!IsMine(__instance)) return true;`).
   A prefix that always returns false takes the method away from every other mod.
3. **Never replace a list or result the game passes in.** Add to it or filter it in place (`recipes.RemoveAll(...)`, `__result += ...`).
4. **Transpilers insert code; they don't delete the game's.** Match loosely and log when the pattern isn't found.
5. **`m_customData` is shared** (on items and the player): only touch your own keys, prefixed with your mod (`"YourMod.key"`), and only
   create the dictionary when it is null.
6. **Undo your patches on unload** (`OnDestroy` → `_harmony?.UnpatchSelf()`).
7. **Don't answer the same question twice.** If another mod already adds chest items to what the player has, adding them too makes
   crafts pay half. Doing one job through different methods still clashes (`systems` lists the game's jobs, `who system <name>` who does each).
8. **A Container isn't always a chest.** Creatures, carts, tombstones and other mods' objects have one too: a mod that uses "every container
   nearby" (crafting from chests, auto-feeding, quick stacking) must keep to built pieces (`GetComponent<Piece>() != null`), or it empties
   a companion's bag or a grave.
9. **A prefix that does the job reads `__runOriginal`.** Every prefix runs even after another returned false, so one that pays, deposits,
   moves or gives must take `bool __runOriginal`, return at once when it's already false (someone did it), and usually run at
   `[HarmonyPriority(Priority.Last)]`.
10. **Never let a shortfall pass silently.** If you take payment from somewhere and can't find enough, refuse the action.
11. **Name everything you register after your mod**: prefabs, RPCs, ZDO and `m_customData` keys, commands, `$` keys, asset bundles, the
    Harmony id (your GUID). One namespace is shared by every mod: two `check` commands, and one hides the other.
12. **Ask the game's rules, don't copy them.** Where the game has a check (ward access `PrivateArea.CheckAccess`, a chest's lock,
    `Player.HaveRequirements`, `ZNetView.IsOwner`), call it rather than re-implementing it: other mods patch those checks, and your copy
    would ignore them.

## What the popular mods do (the 100 most downloaded, from their code)

- **Taking a method over is rare**: of their prefixes, 62% never skip the game's method, 34% skip it only for their own things, 3% always.
- **Order on purpose**: many use `[HarmonyPriority(Priority.First)]` (800) to look before everyone else and `Priority.Last` (0) to act
  after everyone else; `[HarmonyBefore]`/`[HarmonyAfter]` name a specific mod's Harmony id when one must come first.
- **Saying "incompatible" is normal**: about a quarter of them use `[BepInIncompatibility]` (most often against ValheimPlus, and the
  inventory and quick-slot mods against each other). An honest refusal beats a broken game. But **ScriptEngine ignores
  `[BepInIncompatibility]` and `[BepInDependency]`** for mods it loads from `BepInEx/scripts` (some players' mod managers install
  there; BepInEx itself and Claude Tools' dev loader honour them): there, only a check in your own code stands down.
- **Every prefix runs**: in the HarmonyX that BepInEx ships, a prefix returning false skips only the game's method; every other mod's
  prefixes and postfixes on it still run. Two mods that both "take over" a method both do their work.
- **Detect, don't depend**: a `Compatibility` folder with one small class per other mod; detect each **lazily** (the first time it matters,
  e.g. when the player spawns, not in your `Awake`: mods loaded after yours, and everything ScriptEngine loads, aren't in
  `Chainloader.PluginInfos` yet) and keep the answer; touch the other mod's types only inside methods marked `[MethodImpl(MethodImplOptions.NoInlining)]` (so a missing mod can't
  break yours with a TypeLoadException); a soft dependency just to load after it.
- **Offer an API**: a public static class other mods can call (or reach by reflection), with events; wrap callers' code in try/catch so a
  bug in their mod doesn't break yours.

## Finding who else changes the same thing

With the game running (request file or `claude <command>` in the console; the console needs Settings → Gameplay → "Enable console"):

| Command | Use |
|---|---|
| `who <Type.Method>` | Installed and popular mods that patch it, and how (can skip, rewrites, changes the result). |
| `who system <name>` | Every mod touching a game system (crafting payment, inventory size, portals...); `systems` lists them. |
| `clashes [mod]` | The installed mods that clash with each other or with popular mods, `likely` first. |
| `modcheck <mod>` | The pre-release check, including popular mods that patch the same methods. |

Without the game: `BepInEx/claude/library/patchmap.json` (every popular mod's patches by game method, shipped with Claude Tools, kept
fresh when the player switches `DownloadMods` on), `library/index.json` (the mods), `library/clashes.json` (the last clashes report).

Each patch entry: `kind` (prefix, postfix, transpiler, finalizer, hook), `skips` (`never`, `sometimes`, `always`), `changesResult`,
`changesArgs`, `runOriginal` (a prefix that reads `__runOriginal`), `args` (which overload, when the method has several: patches on
different overloads don't meet), `priority`, `method` (where it is in that mod).

A clash pair has a `level`:
- `likely`: one mod took the method over while the other changes it too, or both rewrite it, or two prefixes on a method that pays or
  hands out both skip it and the later one doesn't read `__runOriginal` (both do the job: paid twice, given twice).
- `check`: either can skip the method, rewrites it, or changes its result or arguments. Read both patches: most are fine.
- `stacks`: they only add to each other (postfixes, prefixes that never skip). Normal; shown only with `clashes all`.

`highStakes` marks pairs in a system where two mods doing one job costs players items (crafting payment, inventory, saving, death,
containers): read those first, even at `check`. `sameJob` means different methods doing one job (both count chest items for crafting).

## Reading another mod's code

1. Get its DLL: `library get Namespace-Name` (the Thunderstore name, e.g. `library get Azumatt-AzuCraftyBoxes`); it lands in
   `BepInEx/claude/library/mods/<Namespace-Name>/dll/`, code only, never loaded. `library drop <name>` removes it.
2. Decompile it into a scratch folder: `ilspycmd <that dll> -o <scratch>/AzuCraftyBoxes`.
3. Search for the `method` named in its patch entry, and read what it does before calling anything a clash.

**Licences:** read other mods to understand them and work with them. Don't copy their code into your mod or publish their DLLs or
decompiled code. Credit ideas you take. The library stays on the player's computer.

## Working with another mod

- **Detect it**: `Chainloader.PluginInfos.TryGetValue("their.guid", out var info)` (GUIDs are in `library/index.json` and each
  `scan.json`). Check the first time it matters (the player spawns, a chest opens), not in your `Awake` (mods loaded after yours, and every
  ScriptEngine mod, aren't in the list yet) and not every frame, and keep the answer.
- **Soft dependency** (`[BepInDependency("their.guid", BepInDependency.DependencyFlags.SoftDependency)]`): load after them if present.
  BepInEx honours it for mods in `BepInEx/plugins` (Thunderstore installs) and Claude Tools' dev loader for `scripts`; ScriptEngine
  ignores it. Either way, detect the other mod when it matters rather than relying on the order.
- **Use their public API by reflection** so your mod still loads without theirs. Read their code for the method to call.
- **Stand down** when you would do the same job twice: turn your part off when they're installed, and say so in the log and README.
- **Truly incompatible**: `[BepInIncompatibility("their.guid")]` stops yours from loading next to theirs from `BepInEx/plugins`, but not
  under ScriptEngine (`BepInEx/scripts`): also check in code and stand down, and say why in the README.
- **Order**: `[HarmonyPriority(Priority.Low)]`, `[HarmonyBefore("their.harmony.id")]`, `[HarmonyAfter(...)]` when the order matters.

## When there's a clash

Claude Tools only finds and explains clashes: it never turns a mod off or changes one. After each launch it writes
`library/clashes.json` and logs one line (`Mod clashes: 1 likely (GearSlots and shudnal-ExtraSlots), 193 to check`); `clashes`,
`who` and `modcheck` give the details. Deciding what to do is your job, in this order.

**What a clash looks like in the game.** Rarely an error. Usually a feature that silently does nothing (another mod's prefix skips the
method it relies on), something that happens twice (two mods doing one job: crafts paid half, two extra rows), or things lost (items
dropped at spawn when inventory sizes disagree, data wiped). Players report the symptom, not the cause: "my items fell on the ground".

1. **Confirm it.** The levels are pointers, not proof. `likely` means one mod takes a method over while the other changes it too, or
   both rewrite it; `check` means read both. Get both patches (`library get <Namespace-Name>`, decompile, find the `method` named in
   the patch entry; your own source for yours) and work out what happens when both run, in their order (priority, then load order).
   Most `check` items end here: each mod handles only its own things, or both only add. Say so and move on.
2. **Fix your own side** when it breaks a rule above. This is the real fix and usually small: return false only for your own objects,
   add in a postfix instead of taking the method over, filter in place instead of replacing the list, refuse a short payment.
   Example: a mod that adds inventory rows by taking `Player.SetInventorySize` over breaks every other row mod (ValheimPlus, quivers,
   slot mods) and drops their items at spawn; the fix is in **valheim-pitfalls** ("The player's inventory height is reset at every spawn").
3. **If both can't run as they are**, work with the other mod: detect it (`Chainloader.PluginInfos`) and switch off only your
   overlapping feature, with a log line and a README note; or call its API by reflection so the job is done once.
4. **If they truly can't coexist**, stand down in code when theirs is installed (ScriptEngine ignores `[BepInIncompatibility]`; add the
   attribute too for players who load mods from `BepInEx/plugins`). Say why in the README. Last resort: it takes the choice away from the player.
5. **If the bug is in their mod**, don't patch around it silently. Write the author a report (mod and game versions, the method you both
   patch, what each does, how to reproduce; their page is `page` in `library/index.json`) and tell the player which mod to update or
   switch off meanwhile.
6. **Verify the fix.** Rebuild, then run `modcheck <your mod>` and `clashes <your mod>` again (in the game after it reloads, or
   `modkit` with the game closed) and compare with before. Then run the game with **both** mods installed and do the thing that broke
   (spawn, craft, open the inventory...), and `errors`. The checks read code, so they can't see runtime conditions: after a fix of
   step 3 or 4 (standing down when the other mod is installed) the clash still shows, because the patch is still there. That's
   expected: the in-game test is the proof, and step 7 records why it's handled. Repeat from step 1 if anything is still wrong.
7. **Record it**: the README's "Known clashes" list (fixed, handled and how, or incompatible and why), and the changelog when you fix one.

Never disable or edit another mod for the player, and never turn something off automatically on a guess: tell the player what clashes,
what it does to their game, and their choices.

**When a player reports a conflict:** get their mod list (`mods`), run `clashes <your mod>` and `errors`, match their symptom to the
patterns above, then go through the steps.
