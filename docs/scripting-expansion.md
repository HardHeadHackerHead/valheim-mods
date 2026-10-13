# Quad's Blockscript: the big expansion (contract, 2026-10-12)

> **Status (2026-10-12):** the contract for the big expansion of **Quad's Blockscript** (the language, the blocks, the new game features). It is built; where the built version differs (for example `wait`), `scripting-api.md` section 12 has the last word.

The user asked to "expand the API and scripting language as much as possible" and "fully maximize this". This file is the **contract** for
that work: every name below is final for this round, so the host (C#, `mods/Scripts.Engine/*.cs`), the block language
(`mods/Scripts.Engine/Blocks`) and the editor (`mods/Scripts.Engine/Editor`) can be built at the same time and meet in the middle.
Everything here is an **addition inside API version 1** (nothing existing changes meaning). When an item is built, `docs/scripting-api.md`
gets its full entry (signature, power, cap, failure). Limits are still data: caps are settings the world's owner can change.

Who builds what (a file has one owner):

| Part | Owner files | Reads this file for |
|---|---|---|
| **Host** | `Api.cs Events.cs Players.cs Handles.cs Proxies.cs Store.cs Lists.cs Host.cs Limits.cs HostBridge.cs`, new files next to them, `docs/scripting-api.md` | sections 1 to 6 |
| **Blocks** | `Blocks/*`, `tests/Scripts.Blocks.Tests/*`, `docs/scripting-design.md` palette tables | sections 1 to 8 |
| **Editor** | `Editor/*` (not `GameUi.cs`), `Words.cs` | section 7 and 8 |

Rules for everyone: build with `-p:DeployToGame=false` (never deploy or restart the game; the lead does that), keep the pitfalls in
`E:\valheim-claude-tools\docs\modding-pitfalls.md` (no ZDOID saved, items never lost, names start with the mod's), no library types in the
plugin shell (`mods/Blockscript/Plugin.cs`), and keep `tests/Scripts.Blocks.Tests` green.

## 1. The language: waiting, signals, time

Typed scripts and blocks get the same things.

| Name | What | Notes |
|---|---|---|
| `await sleep(seconds)` | Pause **this handler** (not the game) and carry on later | Handlers and timer functions may be `async`. `seconds` 0.05 to 3600. At most **50 pending sleeps** per script (more is a ScriptError). A sleep dies with the script (stop, reload, pause). The script's budgets apply to every resume exactly as to a handler. Not allowed at top level. Built on Jint's Promise support: `sleep` returns a Promise the host resolves from its own clock; after resolving, call `engine.RunAvailableContinuations()` inside the budget. If Jint 3.1.6 cannot do this reliably, say so and fall back to compiling `wait` into chained `after` calls (the blocks owner must be told). |
| `signal(name, data?)` | Send a message to this script's own listeners | `name` up to 30 characters; 100 a second; data is any value |
| `onSignal(name, fn)` | Listen for it | 20 names per script; handler gets `data`; handlers may be `async` |
| `clock()` | The player's real clock `{hour, minute, second}` | The only real-time read; no `Date` |
| `lookingAt(player, meters = 60)` | The place a player is looking at, or `null` | A raycast on the ground and pieces, 100 m at most |
| `ahead(actor, meters)` | A place `meters` in front of an actor | `meters` -50 to 50; negative is behind |
| `bearing(from, to)` | Degrees 0 to 360 from one place to another | places or handles |
| `add(a, b)`, `scale(a, k)` | Vector math on places | |

## 2. Actors: more to read, more to do

Reads (no power): `stamina`, `maxStamina`, `isSwimming`, `isSitting`, `isRunning`, `isBlocking`, `isInWater`, `faction` (`"players"`,
`"wild"`, `"boss"`, `"tamed"`), `facing` (a unit vector), `speed` (m/s), and on players: `skill(name)` (level 0 to 100; names as the game's
skills: `"Swords"`, `"Run"`, ...), `inventory()` (array of `{prefab, name, count, quality}`; needs `items` or `takeItems`),
`equipped()` (`{right, left, chest, legs, head}` prefab names), `deaths` (this session).

Actions:

| Call | Power | Notes |
|---|---|---|
| `actor.talk(text, seconds = 5)` | `messages` | A speech bubble above the actor (the game's NPC text, as the Proxy's `say`). 120 characters; 5 a second per script |
| `player.banner(text)` | `messages` | The big centre banner (the one that says "Meadows" when you find a biome), to one player. 1 every 2 seconds per player |
| `banner(text)` | `messages` | The same to everyone; 1 every 3 seconds |
| `player.setStamina(n)` | `combat` | 0 to max |
| `actor.applyEffect(name, seconds)` | `combat` | A status effect from the **effects list** (data). Players and creatures. 5 to 600 seconds. 20 a minute per script |
| `actor.removeEffect(name)` | `combat` | |
| `actor.launch(dir, force)` | `combat` | A shove: `dir` a vector (it is normalised), `force` 0 to 15 (metres a second). Not on bosses. 2 a second per script |
| `creature.tame()` | `spawn` | Only a creature this script spawned. Makes it tamed (the game's own taming), keeps its faction |
| `creature.remove()` | `spawn` | Only a creature this script spawned. Removes it without loot |
| `creature.setName(text)` | `messages` | Only own spawned; the hover name, 24 characters |

**Effects list** (`Lists.Effects`, data): names of status effects that are safe to hand out, each with a max length. Start with the ones the
game has by name and no cheat meaning: `Rested`, `Wet`, `Burning` (short), `Poison` (short), `Frost` (short), `Cold`, `Warm`, `Shelter`,
`Resting`, `Lightning` (short), `Slow`-like effects if the game has one by a name the host can find, plus the food-and-mead effects that
only help for a few minutes. The host checks every name against `ObjectDB` at use and refuses unknown ones (a refusal, not an error).
The host owner finds the real names with `prefabs`/`objects` in Claude Tools; never include forsaken powers or boss-progress effects.

## 3. The world: sights, sounds, events, pieces

| Call | Power | Notes |
|---|---|---|
| `effect(name, pos, opts?)` | `effects` (new, allowed by default, capped) | Plays a **purely visual or audio** game prefab at a place: lightning flash, fire burst, smoke, magic circle, thunder, horns... `name` is checked: it must be on `Lists.Effects` **and** its prefab may carry only harmless components (transform, particle systems, audio, lights, timed destroy, ZNetView/ZSFX/ZSyncTransform/EffectList parts). Anything with damage (Aoe, Projectile), a creature, an item or a piece is refused even if somebody lists it. `opts.scale` 0.5 to 3. 10 a second per script, within 100 m of a player. Returns true/false |
| `sound(name, pos)` | `effects` | The same for audio-only prefabs (`sfx_...`) |
| `lightning(pos)` | `effects` | A visual lightning strike (flash and thunder), no damage |
| `shake(pos, range, strength)` | `effects` | Camera shake near a place for players inside `range` (up to 60 m); strength 0 to 3 |
| `ping(pos, text?)` | `messages` | The map ping everyone sees (the game's own shout ping); 1 every 3 seconds |
| `world.setWind(degrees, strength, seconds)` | `weather` | The wind for a while (`EnvMan` debug wind, always put back), strength 0 to 1, seconds 10 to 600 |
| `world.startEvent(name)` | `spawn` | Starts one of the game's random events (a raid) at the first player's place, from the **events list** (data). Default list: the meadows and black forest ones only (`army_eikthyr`, `army_theelder`, `foresttrolls` if present; the owner can add). 1 every 2 minutes |
| `world.stopEvent()` | `spawn` | Ends the current event |
| `world.spawnPiece(prefab, pos, rot?)` | `world` | Replaces the "not built" stub: a **decoration** from `Lists.Pieces` (data: banners, braziers, lanterns, torches, small furniture that holds no items; no chests, workbenches, portals, beds, anything with a Container or a crafting station). Not inside a built piece. 20 a minute, 200 alive per script, within 100 m of a player. Persistent (it stays after the script stops unless the script removed it); marked with `dh_script` on its ZDO. Returns a Piece or null |
| `piece.remove()` | `world` | Only a piece this script placed |
| `nearbyPieces(pos, radius, opts?)` | none | Read: built pieces near a place (radius up to 50, 100 results, nearest first). `opts.prefab` filters. Handles with `id`, `prefab`, `pos`, `valid`, `spawnedBy`, `builtBy` |
| `nearbyPlayers(pos, radius)` | none | Read |
| `net.send(endpoint, text)` | `net` | Replaces the stub: `endpoint` is a *name* from the owner's config entry `Endpoints` (`name=https://host/path;name2=...`, https only, no redirects); the body is JSON `{"text": ..., "script": ..., "world": ...}`; the reply is not given to the script; 1 every 10 seconds; 2000 characters |

## 4. Events

All fire on the game that runs the scripts. Existing events are unchanged.

| Event | `e` has | Notes |
|---|---|---|
| `hit` | `attacker`, `target`, `damage` (writable), `pos`, `weapon` (prefab or `""`) | Before damage is applied (Character damage, on the target's owner). Handler budget 0.2 ms. Writes need `combat` and are clamped as in the API reference: target creature 0 to 3x, target player 0 to 1.5x and never to or above current health |
| `pieceBuilt` | `player`, `piece` | After a placement |
| `itemPickup` | `player`, `item` (`{prefab, name, count, quality}`) | |
| `eat` | `player`, `item` | A food eaten |
| `skillUp` | `player`, `skill`, `level` | |
| `raidStart` / `raidEnd` | `name` | The game's random events |
| `weatherChange` | `from`, `to` | Names as `weather()` |
| `playerRespawn` | `player` | After a death |
| `command` | `player`, `name`, `args` (array of words) | Fired for the chat commands a script registered with `command()`; a **hat** for it exists in blocks |
| `tamed` | `creature`, `player` | |

Also fix: the Proxy events (`proxyEvent`) are already listed; keep them.

## 5. Capabilities and limits

New capability **`effects`**: "play sights and sounds" - allowed without asking (like `messages`), but capped (above) and with its own line in the
Limits panel. `Manifest.Capabilities` gets `"effects"`; `Words.Power("effects")` gets its text. `Limits` gets settings for the new caps
that matter (effects per second, pieces per minute, pieces alive, applyEffect per minute) in the BepInEx config, and `Limits.Lines` describes
them in the editor. Every new refusal goes through `Host.Refuse` with a reason that names the cap and the setting.

## 6. Lists (data the owner can change)

`Lists.Effects` (status effects), `Lists.VisualEffects` (effect and sound prefabs, found with `prefabs fx_`/`vfx_`/`sfx_` in the running game
by the host owner while building), `Lists.Pieces` (decorations), `Lists.Events` (random events), and the existing `Allow` (items) is widened
to more food, resources and building materials of the Meadows, Black Forest and Swamp (amounts still capped, trophies, keys and boss items
still never). Blocks read these lists through `BlockLists` (names listed in section 8), so a block cannot offer what the host would refuse.

## 7. The block language

Blocks stay the primary editor and compile to the same JavaScript. Additions:

**Language blocks**
- **Wait**: `wait {n} seconds` (a stack block; becomes `await sleep(n)`). Every handler that contains a wait is compiled as `async`.
  Allowed inside events, timers, `after`, procedures and loops; refused (a problem on the block) outside a handler.
- **Signals**: `send signal {name}` (+ optional value), hat `when signal {name} arrives`, value `the signal's value`. Names are text typed by the player (validated: letters, digits, spaces).
- **My blocks (procedures)**: `define {name} {parameters}` (a hat-like definition with up to 4 named parameters), `do {name} {arguments}` (a stack block), and for value procedures `result of {name}`. Compiled to JS functions (async when they contain a wait; a call to an async procedure from a handler is awaited). Recursion is allowed up to the sandbox's depth; a procedure must be defined once per name.
- **Lists**: list variables (`make list {name}`), `add {x} to {list}`, `insert`, `remove item {n}`, `delete all`, `item {n} of {list}`, `length of {list}`, `{list} contains {x}`, `index of`, `for each item in {list}`, `list of {a, b, c}` (a literal), `random item of {list}`, `sort`, `reverse`, `join list with {sep}`. Cap 500 items per list.
- **Loops**: `repeat until {cond}` and `while {cond}` bounded to 1000 turns per run (a counter in the generated code; the block says so), `for {i} from {a} to {b}` (bounded to 1000), `break` and `continue`.
- **Math**: `min`, `max`, `abs`, `floor`, `ceiling`, `round to {places}`, `square root`, `power`, `sin`, `cos`, `atan2`, `clamp {x} between {a} and {b}`, `map {x} from..to`, `distance between {a} and {b}`, `direction from {a} to {b}`, `pi`, `random decimal`, `random whole number between`.
- **Text**: `length of text`, `letter {n} of {text}`, `text {a} contains {b}` (exists), `replace {a} with {b} in {text}`, `uppercase`, `lowercase`, `trim`, `split {text} by {sep}` (a list), `text to number`, `number to text with {n} decimals`, `join` (exists, variadic up to 4 via nesting), `text before/after`.
- **Comments**: a free-floating `note` block (text only, ignored by the compiler) for explaining a script.
- **Switch**: `if / else if / else` chain as one block (`if_chain`) so long ladders do not nest 8 deep.
- **Variables**: `variable of any kind` (numbers, text, yes/no, places, lists; no types to declare), `local variable` inside a handler (`let`).
- Raised limits (already done): 1500 blocks, depth 24, 60 variables, 120 KB of code. The 100 iteration "for each" bound becomes 1000.

**API blocks** (every item in sections 1 to 4 gets a block; names below are the block ids, labels are the blocks owner's wording)

- Events (hats): `on_hit`, `on_piece_built`, `on_pickup`, `on_eat`, `on_skill_up`, `on_raid_start`, `on_raid_end`, `on_weather_change`, `on_respawn`, `on_command` (with a text slot for the command name), `on_tamed`, `on_signal`, `on_leave` (exists), `on_stop` (exists).
- Event values: `ev_attacker`, `ev_target`, `ev_damage` (readable and a `set damage to` stack block that only works under `on_hit`), `ev_weapon`, `ev_piece`, `ev_item`, `ev_skill`, `ev_level`, `ev_event_name`, `ev_from`, `ev_to`, `ev_args` (list of words), `ev_arg` (word n), `ev_creature`.
- Players/creatures (values): `stamina_of`, `max_stamina_of`, `is_swimming`, `is_sitting`, `is_running`, `is_blocking`, `faction_of`, `facing_of`, `speed_of`, `skill_of`, `has_item` (exists), `deaths_of`, `equipped_in {slot}`, `inventory_of` (list of names), `looking_at`, `ahead_of`, `real_hour`, `real_minute`.
- Stack: `talk`, `banner_to`, `banner_all`, `set_stamina`, `apply_effect`, `remove_effect`, `launch`, `tame`, `remove_creature`, `name_creature`, `play_effect`, `play_sound`, `lightning`, `shake`, `ping`, `set_wind`, `start_event`, `stop_event`, `place_piece`, `remove_piece`, `send_web` (net), `spawn_star` (exists), `spawn` returning the creature into a variable (`spawn_into {var}`).
- Values: `nearby_creatures` (list), `nearby_players` (list), `nearby_pieces` (list), `spawned_creature`.

A block that needs a power declares it (`Needs`); danger flags: `place_piece`, `remove_piece`, `send_web`, `launch`, `tame`, `start_event` are marked `Danger` (red).

## 8. Lists the blocks use, categories, groups

`BlockLists.Get` gets: `effectsAll` (status effects), `visualEffects`, `sounds`, `pieces`, `events`, `slots` (`right left chest legs head`), `skills` (the game's skill names), `factions`, `signals` (names used in this program), `lists` (the program's list variables), `procedures` (defined names). The host `Lists.*` are the source (the blocks project links `Lists.cs`).

**Palette categories** (`BlockDef.Category`), in tab order: `events`, `control`, `values`, `text`, `lists`, `players`, `world`, `effects`, `actions`, `memory`, `functions`. `BlockDef.Group` is an optional heading inside a tab (for example, in `players`: "Health", "Items", "Movement"). The editor shows as many tabs as there are categories (two rows), headings for groups, a search box that filters every block by its words, and keeps working with a thousand blocks in a program.

Colours per category are the editor owner's: events gold, control orange, values green, text purple, lists red, players blue, world teal, effects magenta, actions violet, memory pink, functions dark blue.

## 9. What done looks like

1. `dotnet build -p:DeployToGame=false` of `mods/Scripts.Engine`, `mods/Scripts` and `mods/Terminal` succeeds with no new warnings.
2. `tests/Scripts.Blocks.Tests` passes, including a test per new block family that compiles a program and runs the generated code in Jint against stand-ins, and a fuzz pass over the whole palette.
3. `docs/scripting-api.md` has an entry for every host item (the host owner); `docs/scripting-design.md` lists the palette (the blocks owner); `tools/scripts_keys.py` regenerates `English.json`.
4. Nothing was deployed. The lead deploys, reloads, and tries it in the game with the user.

## 10. The logging pass (lead, after the three forks finish)

Core done and tested: `mods/Scripts.Engine/ScriptLog.cs` (structured entries, folding, filters, text form, a rotating file per script) with
`tests/Scripts.Log.Tests`. To hook up once the host and editor files are free:

1. **Host**: `Script.Log` (a `List<string>`) becomes a `ScriptLog`; `Script.Say` keeps working (info) and gains `Say(level, text, detail, evt, line, blockId)`;
   `Host.Refuse` -> `Limit`; `Audit.Add` -> `Did`; `Host.Error`/`Fail` -> `Error` with the handler name, the JS line and column from Jint's
   exception, and the stack as detail; a `LogFile` sink per script at `config/Blockscript/logs/NAME.log`.
2. **Script side**: `log(anything)` prints objects/arrays readably (JSON, 400 characters), plus `log.info/warn/error/debug`, `print`, and
   `assert(cond, text)`; 20 lines a minute for user lines stays (counted, folded). A per-script **trace** switch records each event received and
   each action called with its result at Debug level (off by default; a button in the editor; `blockscript trace NAME on|off`).
3. **Errors point at blocks**: the editor maps an error's JS line through `Compiled.LineBlock` to the block, marks it red, and the log line
   is clickable ("go to block").
4. **Viewer** (Editor): filter chips (All / Errors / Limits / Did / Mine), a search, follow/pause, wheel scroll through all 500 lines, a Copy button
   (to the clipboard, as text) and Clear, a stats line (calls, average ms, queue, dropped, refusals, errors), opening a line shows its detail.
5. **Claude Tools**: `blockscript log NAME [n] [level] [grep]`, `blockscript trace NAME on|off`, `blockscript stats`.
