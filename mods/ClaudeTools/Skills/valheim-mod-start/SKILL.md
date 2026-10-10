---
name: valheim-mod-start
description: Start a new Valheim mod (BepInEx plugin) from nothing, build it, load it into the running game and see it work. Use when someone wants to make a Valheim mod, add a feature to the game, or asks how modding Valheim works.
---

# Starting a Valheim mod

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (the command `help` lists them all). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

A Valheim mod is a C# class library (.NET Framework 4.8) that BepInEx loads into the game. It changes the game by **patching** the game's
own methods with Harmony (run your code before or after them), and by adding things (items, pieces) to the game's lists. Work in this
order, and check each step in the game before the next.

## 1. What you need

- **Valheim with BepInEx** (the `denikson-BepInExPack_Valheim` package): installed by a mod manager (r2modman, Thunderstore Mod Manager)
  or by hand into the game folder.
- **Claude Tools**, which also brings the **dev loader**: the mod you are making goes in `BepInEx/scripts`, loads when the game starts,
  and reloads on its own with `reload YourMod`, without restarting the game (step 4). Nothing else to install for that. Set
  `AllowRequests = true` in `BepInEx/config/com.quad.claudetools.cfg` so Claude can send it commands.
- **Your BepInEx folder.** Installed by hand, it is `<Valheim>/BepInEx` (Steam → Valheim → Manage → Browse local files). With
  r2modman or Thunderstore Mod Manager each **profile** has its own: Settings → "Browse profile folder", then `BepInEx` inside it. Every
  `BepInEx/...` path in these skills means that folder (`BepInEx/claude` is where Claude Tools writes its guide, skills and tools).
- **The game's console**, for `claude <command>` and the game's own cheats: Settings → Gameplay → "Enable console" (or the `-console`
  launch option in Steam), then F5 opens it. Commands that cheat (`give`, `grow`, `objects ... remove`, `shoot tp`) work in single
  player and for the host; on someone else's server they need `devcommands`, which only the server's admins can use.
- The **.NET SDK** (8 or later: it builds .NET Framework 4.8 libraries with the reference assemblies package) and **ilspycmd** to read the
  game's code: `dotnet tool install -g ilspycmd`.
- Read `BepInEx/claude/CLAUDE.md`: every command Claude Tools has, and how requests work.

## 2. Make the project

Make the mod in its own folder (anywhere, not inside the game's), start Claude Code there, and copy the skills in: `BepInEx/claude/.claude/skills`
to the project's `.claude/skills`. Start from the template in https://github.com/HardHeadHackerHead/valheim-mod-manager (`template/`):
`mods/Directory.Build.props` (game paths and references), `mods/ExampleMod/` (rename folder, csproj, namespace) and `publish.ps1`. Set
`VALHEIM_DIR` to the game folder and, when a mod manager keeps BepInEx in a profile, `BEPINEX_DIR` to that profile's `BepInEx` folder. The
plugin:

```csharp
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "com.yourname.yourmod";   // unique forever; never change it
    public const string Name = "YourMod";
    public const string Version = "1.0.0";
    private Harmony _harmony;

    private void Awake()
    {
        _harmony = Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, Guid);
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy() => _harmony?.UnpatchSelf();  // hot reload: never leave old patches behind
}

[HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
internal static class Player_ConsumeResources
{
    private static void Postfix(Player __instance) { /* after the game's own code */ }
}
```

## 3. Before writing game code: look it up

Never guess the game's names. Use the **valheim-game-code** skill: `game Player.ConsumeResources` (in a request, or `claude game ...` in
the game's console) gives the real signature, who calls it and which popular mods patch it already. Read the game's code for anything you
change.

## 4. Build, load, check

```
dotnet build mods/YourMod -c Release        # copies YourMod.dll and .pdb into BepInEx/scripts
```

Building only copies the file: the game keeps running the old copy until it is reloaded. With the game running, send (a request file in
`BepInEx/claude/requests`, or `claude <command>` in the console):

```
reload YourMod
errors
modcheck YourMod
```

`reload` unloads your mod and loads the new build (its answer names the version it loaded: check it's the new one), `errors` shows
exceptions since the last check (with the mod they came from), `modcheck` runs the pre-release check (see **valheim-prerelease**). The
first time, put the DLL in `scripts` before starting the game (it loads at start), or `reload` loads it.

**When a mod may be reloaded.** A reload unloads the old copy (its `OnDestroy` runs) and starts the new one a frame later. Whether that is
safe depends on what the mod does: `modcheck` gives its **reload level**, `reload` keeps to it, and `devmods` lists every mod's.

| Level | The mod | Reload |
|---|---|---|
| `world` | only patches methods, adds UI, commands, settings; keeps its saved state in ZDOs or `m_customData` | anywhere, in a world too |
| `menu` | registers pieces, items, creatures, recipes or status effects, or changes an inventory's size | at the main menu only: for a moment its prefabs are missing (a host loading an area then deletes those objects), and items in bags point at the old copy |
| `restart` | uses Jotunn or blaxxun-boop's managers (they patch with their own Harmony ids) | only by restarting the game |

A mod can declare a stricter level than the one found: `[assembly: AssemblyMetadata("ClaudeTools.Reload", "menu")]`. To try a `menu` mod,
quit to the main menu, `reload YourMod`, then load the world. `reload YourMod force` ignores the level: only in a test world. The old
copy's code and static fields stay in memory (.NET never unloads it), so a mod that keeps state in statics must reset it in `OnDestroy`.
If ScriptEngine is installed too, its F6 reloads **every** mod in `scripts` at once with no such check: use `reload` instead.

**Test in a world you can lose.** Make a test world (and character) for your mod, and back up `BepInEx/scripts` before copying in a build
someone else made. Never try a build that saves things (pieces, chest contents, player data) on your main world first.

**Try it like a player would**, with commands instead of an afternoon of play (a request file, or `claude <command>` in the console):

```
prefabs mymod                 the exact names of your prefabs (as objects, give and render need them)
give MyItem 5                 put your item in the bag (cheat)
inventory                     what the player carries, before and after
chests 10                     what the chests around hold, before and after
nearby 20                     creatures, buildings and dropped items around
objects piece_mything         every one of your pieces in the world, loaded or not
render piece_mything views=4  your model on its own, from four sides
shoot find mything            then shoot tp / look / shot: set up a screenshot
```

Measure, don't eyeball: take `inventory` and `chests` before the thing you test (crafting, building, a reward) and after, and check the
difference is exactly what the recipe or the rule says.

## 5. Things that only show after a real restart

Hot reload hides several bugs (see **valheim-pitfalls**): pieces registered too late, inventory reset on spawn. Before calling anything
done, quit the game completely, start it again, load the world, and run `errors` and `log 200 Missing prefab`.

## 6. Play well with other mods from the start

Players run dozens of mods. Read **valheim-compat** before your first patch: postfix by default, skip the game's method only for your
own objects, never replace lists or results the game passes in, prefix your saved keys, and run `who <method>` before patching.

**Private game methods.** Many methods worth patching are private (`InventoryGui.DoCrafting`, `Container.Load`,
`Player.HaveRequirementItems`): `nameof` can't see them, and calling them doesn't compile. Patch them by name,
`[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]`, and call them through Harmony's `AccessTools` (`AccessTools.Method(typeof(Container),
"Load")`, kept in a static field). `game <Type.Method>` says whether a method is public.

## 7. Build it the way the best mods are built

From reading the code of the 100 most-downloaded Valheim mods:

**Settings**
- `Config.Bind` with `AcceptableValueRange` for numbers, sections numbered so they sort ("1 - General", "2 - Keys"), and
  `ConfigurationManagerAttributes` (`Order`, `IsAdvanced`) for players who use ConfigurationManager: copy
  `ConfigurationManagerAttributes.cs` from https://github.com/BepInEx/BepInEx.ConfigurationManager into your project (it's found by name).
- Bind many settings in one go: `Config.SaveOnConfigSet = false`, bind them all, then `Config.SaveOnConfigSet = true; Config.Save();`.
- Apply edits to the .cfg live: a `FileSystemWatcher` on it with `SynchronizingObject = ThreadingHelper.SynchronizingObject` (events
  arrive on the game's thread), a one-second debounce, `Config.Reload()` in try/catch, and **dispose it in `OnDestroy`** (hot reload).
- **Multiplayer settings come from the server**: most popular mods (61 of 95) use **ServerSync**
  (https://github.com/blaxxun-boop/ServerSync: a DLL merged into yours with ILRepack, as its README shows) or Jotunn's sync; for something small, copy
  `BepInEx/claude/templates/ServerSettings.cs` (no library: mark a setting with `Synced.Add(Config.Bind(...))`). Gameplay values
  (rewards, costs, ranges, timers, strengths: anything a player could set to gain an edge) are the server's, with a "Lock Configuration"
  setting for admins in ServerSync; look-and-feel settings stay each player's own. Say which ones are synced in their descriptions
  ("[Synced with Server]").

**Versions between players**: ServerSync's `MinimumRequiredVersion` (or Jotunn's `[NetworkCompatibility]`) turns away a player whose version
differs, which matters when the mod adds pieces or items (a client can spawn something the server doesn't know). For a mod only some players
need, keep it optional (`ModRequired = false`): a strict check kicks everyone who doesn't have it.

**Words**: `$` keys and translations, never hard-coded English in the UI. Popular mods use **LocalizationManager** (blaxxun-boop: an
embedded `translations/English.yml` is required, other languages beside it, players can add their own files).

**Items, pieces, creatures, skills, status effects**: the game's own assets are in AssetBundles. Embed yours as a resource and load it once,
reusing it if it's already loaded (a hot reload would otherwise fail with "already loaded"):
`Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(b => b.name == name) ?? AssetBundle.LoadFromStream(stream)`.
Then register as in **valheim-pitfalls** ("Register pieces and items from the scene, early"). Two ways most popular mods do the
registering:
- **blaxxun-boop's managers** (ItemManager, PieceManager, CreatureManager, SkillManager, StatusEffectManager, LocationManager, on
  https://github.com/blaxxun-boop, merged into your DLL with ILRepack as their READMEs show): `new Item(bundle, "MyAxe")`,
  `new BuildPiece(bundle, "MyWall")` with costs and crafting stations. Nothing for players to install. They patch with their own Harmony
  ids, which your `UnpatchSelf` doesn't undo: the mod's reload level is `restart`.
- **Jotunn** (a mod players must also install; 21 of the top mods require it): `PrefabManager.OnVanillaPrefabsAvailable`,
  `PieceManager.Instance.AddPiece`, `ItemManager.Instance.AddItem`.
A piece with no cost is free to build: refuse to register it.

**Multiplayer actions**: register RPCs with names starting with your mod's (`YourMod_RequestTake`); send the request to the object's owner;
in the handler check `ZNetView.IsOwner()` again and send back a "no" (so nothing is lost) when it isn't; identify the player from the
RPC's sender, never from an id in the payload; take ownership only after the access
check (`ZNetView.ClaimOwnership` / `ZDO.SetOwner`). **Never claim ownership and then hand out items yourself**: two players doing it at once
both get them (duplication). The owner gives, as the game's own Fermenter does (`RPC_Tap`); wrap every handler in try/catch and log; compress big payloads. `ZNet.instance.IsServer()`,
`IsDedicated()`; before `ZNet` exists, `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null` means a dedicated server.

**Names that are yours.** Everything a mod registers shares one space with every other mod, and two of the same name break one or both:
prefab names (`yourmod_lantern`, not `Lantern`), RPC names, ZDO and `m_customData` keys, console and Claude Tools command names, the
Harmony id (your GUID), `$` localization keys (`$yourmod_lantern`), asset bundle names, config file name (your GUID), and the DLL's
assembly name (`<AssemblyName>YourName.ModName</AssemblyName>`: `modcheck` warns when a popular mod has the same one). Choose the
GUID once (`com.yourname.modname`): BepInEx names the settings file after it, other mods find yours by it, and changing it later means
moving settings over and telling mod managers the old copy is the same mod. Start each with your
mod's name. (A real case: two of our mods both added a `check` command, and one hid the other.) Pick prefab names a player can find,
too: "war stone" turned out to be `BobWarstone`, so `prefabs` was needed to find it.

**Performance**: use the game's own lists (`Player.GetAllPlayers()`, `Character.GetAllCharacters()`), keep lookups in dictionaries by
prefab name, and never search the scene (`FindObjectsOfType`) every frame.

**Chests**: a mod that takes from or puts into chests the player didn't open (crafting from chests, feeding, sorting) copies
`BepInEx/claude/templates/ContainerAccess.cs`: only built chests (not graves, carts or a creature's bag), not one someone has open, ward
and lock respected, taken over and reloaded before touching it.

**Commands that cheat**: a debug command that gives items, moves the player or removes things must follow the game's own rule, or be
left out of the release build: allowed in single player and for the host (`ZNet.instance.IsServer()`), otherwise only with devcommands on
(`Console.instance.IsCheatsEnabled()`). Without that check, any player on a server can cheat with it.

## 8. Share it

Raise `Version` for every change, then go through **valheim-prerelease**. Most players install mods from **Thunderstore**
(https://thunderstore.io/c/valheim/) with r2modman or Thunderstore Mod Manager, which put them in `BepInEx/plugins`: loaded once at start
(players restart after an update), with `[BepInDependency]` and `[BepInIncompatibility]` honoured. **valheim-thunderstore** walks
through releasing there: the team and token, getting the Thunderstore CLI (`tcli`), `modkit package` (writes the package's
`thunderstore.toml` and checks everything an upload needs), testing the zip on a clean profile, and publishing. Thunderstore asks mods
made with AI to say so; doing it from the start is easiest: in any `.cs` file,
`[assembly: System.Reflection.AssemblyMetadata("AI_Assisted_Creation", "This assembly was partially or fully created with the assistance of Generative AI.")]`
and `[assembly: System.Reflection.AssemblyMetadata("AI_Model_Vendor", "Anthropic")]`, and a line in the README.

Builds come from your working folder, so unfinished code you haven't committed goes out too: commit or stash it first (see
**valheim-prerelease**). The template's `publish.ps1` also builds a feed for HardHeadHackerHead's mod manager (F7), which installs into
`BepInEx/scripts` and reloads mods in place; there, a mod whose reload level isn't `world` needs a `RESTART_REQUIRED.txt` beside its
csproj, holding the reason, and the manager asks players to restart instead of reloading it.
