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
| `give <item prefab> [amount]` | Put an item in the player's bag (for trying out a mod's new item). |
| `use <item prefab>` | Use an item from the player's bag, as a double-click does (eat, drink, light). |
| `grow <seconds> [radius]` | Age the cultivated plants near the player by that many seconds, so they grow without the wait; `grow 0` makes them grow now and says what came of it. |
| `say <text>` | A message in the middle of the player's screen. |
| `pin <place\|x z> <text>` | A labelled pin on the player's map. |
| `wait <seconds>` | Pause, so things happen before the next picture. |
| `render <prefab> [yaw=25] [pitch=12] [views=1\|4] [focus=x,y,z] [dist=m] [fov=30] [size=WxH] [bg=sky\|dark\|clear]` | A picture of any piece, item or creature **on its own**, built out of sight with its real materials and its own light (nothing is placed). `yaw=0` is its front (-z); `views=4` gives front, three-quarter, side and back; `focus` (metres from its origin) and `dist` give a close-up of one part. Use it to check and improve mods' models. |
| `inspect <prefab> [depth=3]` | What an object is made of: its parts with positions, rotations and sizes, colliders, components, meshes and materials (with colours). |
| `colliders <prefab> [prefab...]` | Each piece's solid colliders in its own frame as the game's support check sees them (box centre, rotation, size), its centre of mass, material and whether it holds others up. |
| `support [radius=20] [place=look] [filter]` | Built pieces near a place with the support the game gives them now, their material's minimum and maximum, and health: weakest first. |
| `errors` | The new errors and exceptions in the log since you last asked, with their stack traces. Run it after every rebuild. |
| `waitfor <mod> [version] [seconds=30]` | Wait until a mod (that version) is loaded: after a rebuild, before looking at it. |

Pictures can only show what is loaded, which is the world near the player (about 60 m or more).

## Improving a mod's model

A hot reload rebuilds a mod's piece in the build menu, but pieces already standing in the world keep their old model until the game
restarts. So check models with `render`, which always builds a fresh copy: change the model, rebuild, then

```
waitfor BountyBoard 1.1.2
errors
render piece_bountyboard views=4
render piece_bountyboard yaw=0 pitch=0 focus=0,2.2,-0.2 dist=1.5 size=1600x600
```

Small details sitting less than a centimetre or so in front of a surface flicker or vanish at a distance (the camera cannot tell which is
in front): raise them clearly off it.

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
