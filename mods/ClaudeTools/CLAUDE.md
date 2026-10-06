# Claude Tools: seeing and helping with the player's Valheim game

You are working with a Valheim player who has the **Claude Tools** mod. It gives you a **request mailbox**: you write commands into a
text file, the game carries them out while the player is in a world, and writes back what happened. Pictures are saved as files you can
read. Nothing you do moves the player's character or presses keys: cameras are separate, and commands only do what is listed below.

This folder is `BepInEx/claude` in the player's Valheim folder.

| Path | What it is |
|---|---|
| `requests/` | Write `<name>.txt` here. The game renames it `<name>.taken`, runs it, and writes `<name>.done.json`. |
| `shots/` | Pictures: `shot`, `view`, `orbit`, `top`, and the player's F12 key. `latest.png` is the newest; screenshots have a `.json` beside them saying where the player stood and looked. |
| `_survey.json` | The ground around a spot (`survey`, or the player's Ctrl+F12 key). |

## Before you start

- **Ask the player** before looking at or doing things in their game, and tell them what you are about to do.
- Requests only run when `AllowRequests = true` in `BepInEx/config/com.dhack.claudetools.cfg` (the player switches it on) and the player
  is **in a world**. If a request stays as `.txt`, one of those is not true: ask the player.
- Requests run one at a time, in name order, within a second or two. Wait for `<name>.done.json`, then read it (and any pictures it names).
- Run `help` first in a new session: it lists every command, including those other mods add (BuildOrders adds blueprint commands).

## Writing a request

One command per line; `#` starts a comment. Example `requests/look_around.txt`:

```
status
shot 1280
nearby 30
orbit here 20 30 4
```

The answer, `requests/look_around.done.json`:

```json
{ "request": "look_around.txt", "outputs": [ {"world": "...", "position": [x, y, z], ...}, {"shot": ".../shots/....png"}, ... ],
  "errors": [], "ok": true }
```

Each command adds its result to `outputs`; anything that went wrong is in `errors` (the rest still runs). Keep requests short: things on
screen (like `say`) are seen by the player.

## Places

Camera and survey commands measure from a **place**: `world` (world coordinates), `here` (the player, facing where they face), `look` (the
spot the player is looking at, facing where they face), or a name another mod knows: with BuildOrders, a placed blueprint's name (or `last`
for the newest; use `_` for spaces). Positions in a place are metres: x right, y up, z forward (Unity), turned with the place.

## Built-in commands

| Command | What it does |
|---|---|
| `help` | Every command, with the mod that adds it. |
| `status` | World, player name, position, facing, health, stamina, biome, day, time of day, weather, who is online. |
| `shot [width]` | What the player sees, saved to `shots/`. |
| `view <place> <x> <y> <z> <yaw> <pitch> [fov] [w] [h]` | A separate camera at a spot measured from a place. |
| `orbit <place> [radius] [pitch] [count] [w] [h]` | Pictures from all round a place. |
| `top <place> [size] [width]` | Straight down over a place (an orthographic map of `size` metres). |
| `survey [radius] [step] [place]` | Ground and solid heights on a grid, water level, biome, buildings: `_survey.json`. |
| `inventory` | What the player carries: name, stack, quality, durability, equipped, totals, weight, free slots. |
| `nearby [radius]` | Creatures and players around (health, level, distance), buildings by kind, items on the ground. |
| `looking` | What the crosshair is on: name, kind, distance, condition. |
| `mods` | The mods running, with versions. |
| `config <mod> [section] [key]` | A mod's settings (value, default, description). `config set <mod> <section> <key> <value>` changes one, only if the player allows it (`AllowConfigChanges`). |
| `log [lines] [text]` | The last lines of the BepInEx log, optionally only those containing some text: for checking a mod's messages and errors. |
| `say <text>` | A message in the middle of the player's screen. |
| `pin <place\|x z> <text>` | A labelled pin on the player's map. |
| `wait <seconds>` | Pause, so things happen before the next picture. |

Pictures can only show what is loaded, which is the world near the player (about 60 m or more).

## Commands from other mods

Other mods add commands when they are installed; `help` shows them with the mod they come from. **BuildOrders** adds blueprint commands
(`import`, `remove`, `takedown`, `build`, `check`, `level`, `plans`, `ui`, `selfshare`) and lets places be blueprint names. Its design guide
is `BepInEx/blueprints/CLAUDE.md`: read it before designing builds.

Some commands change the world: `import` places ghosts (and levels the ground), `build` spends the player's materials, `takedown` removes
built pieces (giving the materials back). Ask first.

## For mod makers: adding commands

A mod adds commands without referencing Claude Tools (so it works without it), by finding the Claude Tools plugin
(`MetadataHelper.GetMetadata(plugin).GUID == "com.dhack.claudetools"`) and calling its public static methods by reflection:

```csharp
RegisterCommand(string owner, string name, string usage, Func<string[], Action<JObject>, Action<string>, IEnumerator> run)
RegisterFrame(string owner, Func<string, float[]> resolve)   // a named place: returns { x, y, z, yaw } or null
UnregisterAll(string owner)
```

`run` gets the words of the line (`args[0]` is the command), reports results with `output(JObject)` and problems with `error(string)`, and
returns `null` when done or an `IEnumerator` to take its time (it runs as a coroutine). Register again whenever a new Claude Tools appears
(it may be hot-reloaded); call `UnregisterAll` when your mod unloads. See `mods/BuildOrders/Plugin.Requests.cs` in the valheim-mods repo.
