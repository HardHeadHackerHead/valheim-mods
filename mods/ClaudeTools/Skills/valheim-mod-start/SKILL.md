---
name: valheim-mod-start
description: Start a new Valheim mod (BepInEx plugin) from nothing, build it, load it into the running game and see it work. Use when someone wants to make a Valheim mod, add a feature to the game, or asks how modding Valheim works.
---

# Starting a Valheim mod

> **Game closed?** The mod-maker commands (`modcheck`, `who`, `clashes`, `patches`, `game`, `gameupdate`, `library`, `systems`) also run as a program: `BepInEx/claude/modkit/modkit.exe <command>` on Windows, `dotnet BepInEx/claude/modkit/modkit.dll <command>` on Linux and Mac (`modkit help` lists them). The others (`errors`, `log`, `waitfor`, pictures...) need the game running; its log is `BepInEx/LogOutput.log`.

A Valheim mod is a C# class library (.NET Framework 4.8) that BepInEx loads into the game. It changes the game by **patching** the game's
own methods with Harmony (run your code before or after them), and by adding things (items, pieces) to the game's lists. Work in this
order, and check each step in the game before the next.

## 1. What you need

- **Valheim with BepInEx** (the BepInExPack for Valheim), plus **ScriptEngine** for hot reload: mods in `BepInEx/scripts` load at start and
  reload when rebuilt (F6), so you don't restart the game for every change. The player's mod manager installer sets both up.
- The **.NET SDK** (8 or later: it builds .NET Framework 4.8 libraries with the reference assemblies package) and **ilspycmd** to read the
  game's code: `dotnet tool install -g ilspycmd`.
- **Claude Tools** (this mod) with `AllowRequests = true` in `BepInEx/config/com.dhack.claudetools.cfg`, so you can check things in the game
  (see `CLAUDE.md` next to this folder).

## 2. Make the project

Copy the template from https://github.com/HardHeadHackerHead/valheim-mod-manager (`template/`): `mods/Directory.Build.props` (game paths and
references: set `VALHEIM_DIR` or edit the path), `mods/ExampleMod/` (rename folder, csproj, namespace) and `publish.ps1`. The plugin:

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

Then, with the game running (a request file in `BepInEx/claude/requests`, see `CLAUDE.md`):

```
waitfor YourMod 1.0.0
errors
modcheck YourMod
```

`errors` shows exceptions since the last check (with the mod they came from), `modcheck` runs the pre-release check (see
**valheim-prerelease**). Press F6 in the game to reload after each rebuild.

## 5. Things that only show after a real restart

Hot reload hides several bugs (see **valheim-pitfalls**): pieces registered too late, inventory reset on spawn. Before calling anything
done, quit the game completely, start it again, load the world, and run `errors` and `log 200 Missing prefab`.

## 6. Play well with other mods from the start

Players run dozens of mods. Read **valheim-compat** before your first patch: postfix by default, skip the game's method only for your
own objects, never replace lists or results the game passes in, prefix your saved keys, and run `who <method>` before patching.

## 7. Build it the way the best mods are built

From reading the code of the 100 most-downloaded Valheim mods:

**Settings**
- `Config.Bind` with `AcceptableValueRange` for numbers, sections numbered so they sort ("1 - General", "2 - Keys"), and
  `ConfigurationManagerAttributes` (`Order`, `IsAdvanced`) for players who use ConfigurationManager.
- Bind many settings in one go: `Config.SaveOnConfigSet = false`, bind them all, then `Config.SaveOnConfigSet = true; Config.Save();`.
- Apply edits to the .cfg live: a `FileSystemWatcher` on it with `SynchronizingObject = ThreadingHelper.SynchronizingObject` (events
  arrive on the game's thread), a one-second debounce, `Config.Reload()` in try/catch, and **dispose it in `OnDestroy`** (hot reload).
- **Multiplayer settings come from the server**: most popular mods (61 of 95) use **ServerSync** (blaxxun-boop on GitHub, included as
  source) or Jotunn's sync (our mods use a small shared helper, `mods/Shared/ServerSettings.cs` in the valheim-mods repo): gameplay values are the server's, with a "Lock Configuration" setting for admins, while look-and-feel settings
  stay each player's own. Say which ones are synced in their descriptions ("[Synced with Server]").

**Versions between players**: ServerSync's `MinimumRequiredVersion` (or Jotunn's `[NetworkCompatibility]`) turns away a player whose version
differs, which matters when the mod adds pieces or items (a client can spawn something the server doesn't know). For a mod only some players
need, keep it optional (`ModRequired = false`): a strict check kicks everyone who doesn't have it.

**Words**: `$` keys and translations, never hard-coded English in the UI. Popular mods use **LocalizationManager** (blaxxun-boop: an
embedded `translations/English.yml` is required, other languages beside it, players can add their own files).

**Items, pieces, creatures, skills, status effects**: the game's own assets are in AssetBundles. Embed yours as a resource and load it once,
reusing it if it's already loaded (a hot reload would otherwise fail with "already loaded"):
`Resources.FindObjectsOfTypeAll<AssetBundle>().FirstOrDefault(b => b.name == name) ?? AssetBundle.LoadFromStream(stream)`.
Then register as in **valheim-pitfalls** (from `ZNetScene.Awake`, `ObjectDB.Awake` and `ObjectDB.CopyOtherDB`, which fills the main menu's
copy). Two ways most popular mods do the registering:
- **blaxxun-boop's managers** (ItemManager, PieceManager, CreatureManager, SkillManager, StatusEffectManager, LocationManager), included as
  source: `new Item(bundle, "MyAxe")`, `new BuildPiece(bundle, "MyWall")` with costs and crafting stations. Nothing for players to install.
  They patch with their own Harmony ids, which your `UnpatchSelf` doesn't undo: mark the mod as needing a restart (`RESTART_REQUIRED.txt`).
- **Jotunn** (a mod players must also install; 21 of the top mods require it): `PrefabManager.OnVanillaPrefabsAvailable`,
  `PieceManager.Instance.AddPiece`, `ItemManager.Instance.AddItem`.
A piece with no cost is free to build: refuse to register it.

**Multiplayer actions**: register RPCs with names starting with your mod's (`YourMod_RequestTake`); send the request to the object's owner;
in the handler check `ZNetView.IsOwner()` again and send back a "no" (so nothing is lost) when it isn't; identify the player from the
RPC's sender, never from an id in the payload; take ownership only after the access
check (`ZNetView.ClaimOwnership` / `ZDO.SetOwner`). **Never claim ownership and then hand out items yourself**: two players doing it at once
both get them (duplication). The owner gives, as the game's own Fermenter does (`RPC_Tap`); wrap every handler in try/catch and log; compress big payloads. `ZNet.instance.IsServer()`,
`IsDedicated()`; before `ZNet` exists, `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null` means a dedicated server.

**Performance**: use the game's own lists (`Player.GetAllPlayers()`, `Character.GetAllCharacters()`), keep lookups in dictionaries by
prefab name, and never search the scene (`FindObjectsOfType`) every frame.

## 8. Share it

`publish.ps1` builds every mod into `dist/` with a `manifest.json`; push it to GitHub and anyone running the mod manager (F7) can add your
repo as a source. Raise `Version` for every change. Add `RESTART_REQUIRED.txt` if the mod registers any networked prefab (pieces, items, **creatures**): during a hot reload there is a
moment with the prefab unregistered, and a host that loads an area then deletes those objects with everything they carry.
