# Quad's Blockscript: write scripts in the game, on the Terminal (working title, designed 2026-10-12; host core and Terminal program built, see Built so far)

> **Status (2026-10-12):** design document for **Quad's Blockscript** (`mods/Blockscript`, engine in `mods/Scripts.Engine`). The block editor, the host, the Terminal program, multiplayer and live co-editing are built; the gallery, a world snapshot and safe mode are not. Players: start with `mods/Blockscript/README.md`; this file is for people changing the mod.

A small scripting language inside Valheim. Players write short scripts that react to the game ("when a boar dies, maybe drop honey",
"every 20 minutes change the weather") and run them without building a mod, without a restart. The editor is a **program on the
Terminal** (`docs/terminal-design.md`): sit at the computer, open Scripts, snap blocks together (Scratch-style) or type code, press
Run. The same scripts can also drive the
**Proxy**, the character you play from the Terminal, so a script is a bot you wrote yourself.

Audience: **the public**. People who installed a mod and know nothing about modding. That makes the sandbox and the safety rules a
part of version 1, not a later step. Anything a script can do must be safe to hand to a stranger.

Status: design, with the **host core built** (`mods/Scripts` 0.1.0) and the **Scripts program** on the Terminal (`mods/Terminal/ScriptsApp.cs`); see "Built so far". Every name (events, actions, permissions) is still a proposal until the API review. The full contract, with every signature, is in
**`docs/scripting-api.md`**; this file is the design and the reasons. Review both before code.

## Decided and proposed

| | |
|---|---|
| Editors | **Blocks first** (Scratch-style, built for this mod), **JavaScript text as the advanced mode**. Blocks compile to the same JavaScript, so there is one sandbox and one set of rules. Text never converts back to blocks. |
| Language | **JavaScript** through **Jint** (a pure C# interpreter, works on Unity's Mono). Timeout, statement cap and memory cap are built in; no .NET access unless we hand it over. |
| Where scripts run | **On the server** (the host in a listen server, the dedicated server otherwise). Clients show UI and effects only. |
| Where scripts are written | The **Scripts program on the Terminal**, in game (and a plain in-game panel that needs no computer). Local files in `BepInEx/config/Blockscript/` are the second way (off by default, see Sources). |
| Where scripts come from | A **curated gallery** by default; local files behind an "advanced" switch with a warning. |
| Safety | Budgets, a capability list shown in plain words, a world snapshot before a script's first run, a kill switch, safe mode after a crash. |
| Cut from version 1 | Calling other mods' APIs, client-side scripts that change what others see, terrain edits, anything that touches saves or character files. |

**To decide with the user** is at the end.

## Why this is safe enough to ship (the rules)

These are the rules every part of the design has to keep. They are why the API looks the way it does.

1. **A script never holds a game object.** It gets opaque handles (`player`, `creature`, `piece`: small objects wrapping an id) and
   calls the host through a fixed API. No Unity types, no reflection, no `Type`, no `System`. The engine is created with the CLR
   closed (`AllowClr` off), `eval` and `Function` constructors removed, no `import`, no `require`.
2. **Every script has budgets.** Time per frame (default 1 ms; a script that goes over is skipped for the rest of that frame), a
   statement cap per call, a memory cap, and rates on the actions (spawns, damage, item grants, messages). Over budget: the script is
   paused, the panel says why, the game keeps running.
3. **Every power is a capability, off until approved.** The player sees them in plain words, not as names (see Permissions).
4. **A script cannot lose items or buildings.** It can only lower or raise things inside caps the host sets, it cannot remove pieces,
   cannot register new persistent prefabs (a removed script would leave pieces whose prefab is gone), and every item grant goes through
   the same code path as a normal mod grant (`docs/modding-pitfalls.md`: read it before writing the host).
5. **Failure is quiet.** An error disables the script and shows it in the panel. The host never throws into the game loop, and the
   Harmony patches the host uses are wrapped so a script fault cannot take the patch down.
6. **Server decides.** A client never approves anything for a server, and anything a client asks (through the Terminal) goes through an
   RPC the host validates.

## Built so far (2026-10-12)

- **Spike (risks 1 and 2)**: Jint 3.1.6 and its parser Esprima run on Valheim's Mono, carried inside our DLL with `System.Buffers`
  (`Deps.cs`; the game already has `System.Memory` and `Unsafe`). **Limits measured in the game**: an infinite loop is stopped in about
  16 ms by the statement cap, runaway recursion in 1.4 ms (Jint), and **Jint's own `LimitMemory` does nothing on this runtime** (32 MB went
  past an 8 MB limit). So memory and per-call time are ours: `Budget` (a Jint `Constraint`, `Sandbox.cs`) checks the call's clock and the
  managed heap's growth on **every statement** (a string can double in one). Measured with it on: string doubling stopped in under 2 ms,
  array growth in 16 ms, object growth in 26 ms, all at 8 MB. `String.prototype.repeat/padStart/padEnd` are capped, `Date`, `Reflect`,
  `Proxy`, `Intl`, `WebAssembly` and `Math.random` are removed, string compilation (`eval`, `new Function`) is off, arrays are capped at
  100,000. **Verified after a real restart (2026-10-12)**: the ES2020 checks pass (`let`, arrows, classes, template strings, spread, `Map`,
`JSON`); `eval` and `new Function` are blocked; `repeat` is capped; the hostile suite through the **host** (a script with nine attacks) was
stopped every time with a plain message: an infinite loop, string doubling, array growth, deep recursion, `eval`, and a 1000-message
flood (one went through), and the script was **paused after five errors in a minute** with the reason shown. Packaging lesson: the plugin
is a thin shell (`mods/Scripts`) that loads the real code (`mods/Scripts.Engine`, embedded) after Jint can be found, because the dev loader
scans every type before `Awake` (see the pitfall in `docs/modding-pitfalls.md`).
- **Tested through the game**: `blockscript spike`, a script that calls most of the API (time, position, biome, ground, nearby creatures,
  `give` with caps and each refusal, store round-trip, `spawn` with the tier list and the audit entry, `setWeather` and its rate limit, a
  timer), the `kill` event (with a snapshot of what died), the Proxy from a script (reads, `nearest`, `walkTo`, `arrived`, `home`, `proxyEvent`),
  and the Scripts program with the cursor (select, Run, Allow).
- **Host** (`Host.cs`): scripts are files in `BepInEx/config/Blockscript/`; one engine each; a manifest parser (`Manifest.cs`);
  timers (`every` at least 1 s, 20 a script) and events in a queue (100, oldest dropped); round-robin with **1 ms a script and 2 ms all
  scripts a frame**, a call limit of 25 ms and 100,000 statements; paused after three over-budget frames, five errors in a minute pause it.
- **API built** (`Api.cs`, `Handles.cs`, `Players.cs`, `Store.cs`, `Events.cs`): time, random, positions, `players`, `findPlayer`,
  `findById`, `nearbyCreatures`, `weather`, `log`, `on/off/every/after/cancel/command`; `say`, `broadcast`, `give/take/count/has` (the
  item lists and caps: `Lists.cs`), `spawn` (creature list, 5 a minute, 20 alive, removed with the script), `dropItem`, `heal`, `damage`
  (cannot kill a player, never a boss), `teleport` (ignores the height, lands on the ground), `world.setWeather` (restored when its time is
  up), `store.*` (64 KB, 256 keys, 4 KB; handles refused), `proxy.*` through the Terminal's bridge. Events: `start`, `stop`, `playerJoin`,
  `playerLeave`, `playerDeath`, `kill`, `chat`, `dayStart`, `nightStart`, `bossSpawn`, `bossDeath`, `proxyEvent`; a chat `/command` of a
  script is taken before the game sees it. **Calls that exist in the API doc but are not built** (`hit`, `pieceBuilt`, `itemPickup`,
  `world.setTime`, `world.spawnPiece`, `net.send`, `playSound`) fail with a plain "not built yet" ScriptError, never silently.
  An audit log records every grant, take, spawn, drop, teleport and damage (`Audit.cs`, also in the BepInEx log).
- **For now scripts act on the player whose game runs them** (single player and the host); items, teleports and healing of *other*
  players need that player's own game: a later step (multiplayer).
- **Console** (`blockscript list|run|stop|allow|deny|log|audit|chat|scan|spike`), six **example scripts** written to the folder on first run
  (Lucky Boars, Welcome Wagon, Boss Announcer, Kill Streaks, Weather Event, Hello), approvals saved in `approvals.txt`.
- **Scripts program** on the Terminal: list, source with line numbers and pages, powers with an **Allow** click, log, Run, Stop, New (a
  template), **Paste** (the clipboard becomes the script: the practical way to get text in until the editor exists), Delete (click twice).
- **Not built**: the block editor, the in-game text editor, the plain panel with no computer, the gallery, the world snapshot before first
  run, safe mode after a crash, the remote kill switch, multiplayer (scripts for remote players, the join list), the permission screen as
  a separate confirmation with red items, `hit` with writable damage, dedicated server testing.

## Parts

| Part | What |
|---|---|
| **Host** (`mods/Scripts`) | The engine (Jint), the event bridge, the API, the budgets, the store, the permission list, the panel. A BepInEx plugin that needs the server (`NeedsServer`, like the other server-side mods). |
| **Block editor** (in `mods/Scripts`) | The drag-and-drop editor: palette, workspace, snapping. Stores a program as JSON and compiles it to JavaScript. Shown in a plain UI panel, and on the Terminal screen by the program below. |
| **Scripts program** (in `mods/Terminal`) | A program on the Terminal screen: file list, the block editor (and the text editor in advanced mode), Run/Stop, a log. Talks to the host through RPCs. The Terminal depends on the host, not the other way around, so the host also works with no computer (console command + local files). |
| **Gallery** | A JSON feed of reviewed scripts (name, author, version, hash, permissions, text), like the F7 `sources.json` idea: reviewed, hashed, pinned, removable remotely. Browsed from the Scripts program. |
| **Proxy API** | The part of the API that drives the Terminal's Proxy character. Present only when the Terminal mod is installed. |

## Script file format

One `.js` file. The first lines are a comment block the host reads before running anything (the **manifest**). A script that asks for
more than its manifest says is refused at the call, not at load.

```js
// ==Script==
// name:    Lucky Boars
// version: 1.0
// author:  Quad
// about:   Boars sometimes drop an extra snack.
// needs:   items, messages
// api:     1
// ==/Script==

on("kill", e => {
  if (e.victim.prefab !== "Boar") return;
  if (chance(0.10)) e.killer.give("Honey", 2);
});
```

- `api` is the API version the script was written for. The host keeps old versions working; event and action names are **frozen** once
  released (renaming `kill` would break strangers' scripts). New things get new names.
- `needs` is the capability list. The host shows it, the player approves it.
- A **block program** is a JSON file (`.blocks.json`), not text. Each block type names the capability it needs, so the editor writes
  `needs` by itself and it can never be wrong. The host compiles the JSON to JavaScript with a fixed generator (the same output for the
  same blocks) and runs that in the same sandbox. "Show code" displays the generated JavaScript, read-only.

## Permissions and the API

The capability list, with the plain-words text the player sees and the default (allowed, ask, off; red = a second confirmation), is
in `docs/scripting-api.md`, section 1. The short version:

- Allowed with no question: read the game's state, show messages, remember small things.
- Ask: give items, spawn creatures, change damage, change weather or time, drive the Proxy.
- Ask, **red**: take items, place pieces, move players. Off by default: `net`.
- On a server only the **admin** approves. A client sees what the server allowed and cannot change it.
- Items are limited by three data lists (what can be given, what can never be given, what can never be taken) and per-call and
  hourly caps. Spawns are limited by a creature tier (default up to Black Forest), because a stray Mistlands spawn near a new character
  kills them (it killed the user and Bob in a dungeon test). Teleports ignore the script's height and land on the ground, because a
  height off by 3 m kills (the teleport memory).
- For a block program the permission screen is **generated** from the blocks used.

## API (version 1, proposed)

The reference lives in **`docs/scripting-api.md`** and is the only copy: types, handle rules, results and errors, every global, every
event, every action with its capability and cap, the store, the Proxy, the limits, versioning, idioms. Do not repeat it here.

How it is organized, for orientation:

| Group | Examples |
|---|---|
| Time, random, positions | `minutes`, `isNight`, `chance`, `pick`, `vec`, `distance`, `groundHeight` |
| Looking things up | `players`, `findPlayer`, `findById`, `nearbyCreatures`, `weather` |
| Events | `start`, `stop`, `playerJoin`, `playerDeath`, `kill`, `hit`, `pieceBuilt`, `bossSpawn`, `chat`, `dayStart`, `proxyEvent` |
| Timers | `every`, `after`, `cancel` (a script cannot wait) |
| Actions | `say`, `broadcast`, `give`, `take`, `spawn`, `setWeather`, `teleport`, `spawnPiece`, `net.send` |
| Store | `store.get/set/remove/keys` |
| Proxy | `proxy.walkTo`, `attack`, `use`, `equip`, `deposit`, `home`, `nearest` |

Not available on purpose: `eval`, `Function`, `import`, files, the real clock, the network (except `net.send`), other mods,
reflection, new persistent prefabs, removing pieces a script did not place, terrain edits, another script's store.

## Sandbox and budgets

- One Jint engine **per script**, so one script cannot see another's globals.
- `TimeoutInterval` for the longest single call (default 50 ms, a setting), `MaxStatements` per call, `LimitMemory` per engine, a
  recursion depth cap.
- A **frame budget** across all scripts (default 2 ms). Scripts are served round-robin so one cannot starve the others. A script
  that is over budget three times in a row is paused and the panel names it.
- Action rate limits are token buckets per script and per capability.
- Output is capped: `say`/`broadcast`/`log` have length and rate limits so a script cannot flood the chat.
- A script runs against **handles**, not objects. If a handle's object is gone, the call returns `null`; it never throws into game code.

## World safety

- **Snapshot before first run.** The first time a script with a risky capability (`items`, `spawn`, `world`, `teleport`) runs in a
  world, the host copies the world save (`.fwl`/`.db`) to `worlds_local/backups-scripts/`. A "Restore" button in the panel puts it back
  after a full exit. Cheap, and it is the answer to "a script broke my world".
- **Audit log.** Every item grant, spawn, piece, teleport and damage a script did, with the script's name and time, in a ring buffer
  the admin can read in the panel and write to `activity.log` (like the AICompanion's). A player who lost something can see who did it.
- **Safe mode.** If the last session ended without a clean shutdown (a crash), the host starts with every script off and says so.
- **Kill switch.** One button: all scripts off. Also a console command, a config setting, and the admin chat command.
- **Remote disable.** The gallery feed can mark a script version as bad; the host disables it on the next check and says why.

## The Scripts program on the Terminal

The Terminal is a computer, so this is an app on it, in the screen's own look.

- **Screen layout**: a file list on the left (My scripts, Gallery, Examples), the editor in the middle, the log and the "running"
  list on the right. Drawn with the game's own UI style (the look skill), not a debug overlay, and it is a real screen the other
  players can see over your shoulder.
- **Run / Stop**: the editor sends the program to the host (RPC), the host shows the permission screen, then runs it.
- **Help**: a "Learn" tab with a short course (events, timers, no waiting, how to read an error) and examples to copy and edit. The
  first-run script is "Hello": one block, `say`.
- **Errors are readable**: the block that failed is highlighted red with a plain sentence ("there is no item called `Hony`. Did you
  mean `Honey`?"); text mode shows the line number and the line, not a stack trace.
- **Gallery tab**: browse, see permissions in plain words, install (a copy lands in My scripts, edit freely, the original stays).
- **Multiplayer**: the screen is visible to everyone; only the driver edits. Who may run scripts on a server is a setting (admins
  only, anyone at a Terminal, or nobody).
- **Without the Terminal**: the same editor opens as a plain UI panel (a hotkey or the console command `blockscript` (or `blockscript panel` in Claude Tools)), and
  `blockscript list|run|stop` console commands plus local files still work.

## The block editor

The primary editor, and the one the public sees. It is built for this mod with Unity UI (Google's Blockly is a web library and cannot
be dropped into the game).

### Why blocks first

- A hat block ("when a Boar is killed") is the same thing as `on("kill", ...)`: the event model is already block-shaped.
- "A script cannot wait" stops being a trap: there is no wait block. Loops are only bounded ("repeat 5 times", "for each player"),
  so there is no infinite loop to write.
- An item or creature is a **dropdown from the allow-lists**, so a typo or a blocked item cannot be typed.
- The permission screen is generated from the blocks.
- Block labels go in the translation files like the rest of the mod; code cannot be translated.
- A text editor drawn on a `RenderTexture` screen (cursor, selection, scrolling, key repeat) is the hardest UI in this plan. Blocks
  need a mouse and a drag, which is simpler.

### What it looks like

```
 [when a  Boar  is killed]               hat (event); the creature is a dropdown
   [if  chance  10 %]                    condition
     [give  killer   2  Honey]           action; item and amount from lists
     [say   killer   "Lucky boar!"]
```

### Block palette (generated: `tests/Scripts.Blocks.Tests palette`)

Eleven tabs, 215-odd blocks. Each line is a heading in the tab, then its blocks (`[x]` is a slot, `[power]` the power it needs, `(red)` a dangerous one). The tab order is `BlockDef.Category` order in `Defs.Categories`; the headings are `BlockDef.Group`, and their translation key is `scripts_group_<heading in lower case, other characters as _>`.


**Events (the blocks that start a stack)** (24 blocks)

- *Start and stop*: when this script starts; when this script stops
- *Players*: when a player joins; when a player leaves; when a player dies
- *Fights*: when [creature] is killed; when a boss appears; when a boss falls; when something is hit; when a creature is tamed
- *Talk and time*: when someone says [text]; when morning begins; when night falls; every [n] [unit]
- *Signals*: when signal [name] arrives
- *Building and items*: when a player builds something; when a player picks something up; when a player eats; when a skill goes up
- *The world*: when a raid begins; when a raid ends; when the weather changes; when a player comes back after dying
- *Commands*: when someone types /[name]

**Control** (16 blocks)

- *Choices*: if [cond]; if [cond]otherwise; if [c1]else if [c2]else if [c3]otherwise
- *Loops*: repeat [n] times; for each player; for each creature within [r] of [pos]; repeat until [cond]; repeat while [cond]; for [v] from [a] to [b]; for each item in [list]; break out of the loop; skip to the next turn
- *Waiting*: after [n] [unit]; wait [n] seconds
- *Stopping*: stop here
- *Notes*: note: [text]

**Values (numbers, logic, what the event tells)** (44 blocks)

- *This event*: the player; the killer; the one killed; the boss; what was said; where it happened; the biome; the cause of death; the day; the attacker; the one hit; the damage; the weapon used; the piece built; the item; the skill; the skill's new level; the raid's name; the weather before; the weather now; the words typed after the command; word [n] typed after the command; the creature tamed; [field] of [item]; kind of [p]
- *This loop*: that player; that creature
- *Numbers*: a random number from [a] to [b]; [a] [op] [b]; round [a]; [p] % chance; [fn] of [a]; the [fn] of [a] and [b]; round [a] to [d] places; keep [x] between [a] and [b]; turn [x] from [a]..[b] into [c]..[d]; pi; random decimal (0 to 1); distance between [a] and [b]; direction from [a] to [b]
- *Logic*: [a] [op] [b]; [a] and [b]; [a] or [b]; not [a]

**Text** (12 blocks)

- *Words*: [a] [op] [b]; join [a] [b]; a random one of [list]; join [a] [b] [c] [d]; length of [t]; letter [n] of [t]; replace [a] with [b] in [t]; [fn] [t]; the words before [s] in [t]; the words after [s] in [t]; [t] as a number; [n] as words with [d] decimals

**Lists** (18 blocks)

- *Make a list*: list of [items]; split [t] by [sep]; all players
- *Change a list*: add [x] to [l]; add the number [x] to [l]; insert [x] at place [n] in [l]; remove item [n] of [l]; delete all items of [l]; sort [l]; reverse [l]
- *Look in a list*: item [n] of [l]; length of [l]; [l] contains [x]; place of [x] in [l]; random item of [l]; sum of [l]; join [l] with [sep]; that item

**Players and creatures** (40 blocks)

- *Who*: the player named [name]; [who] is a player; [who] is a [kind]; name of [who]; kind of [who]; health of [who]; position of [who]
- *Items*: how many [item] [who] has [items]; [who] has at least [n] [item] [items]; give [who] [n] [item] [items]; take [n] [item] from [who] (red) [takeItems]
- *Messages*: say [text] to [who] [messages]; [who] says [text] in a speech bubble [messages]; call [who] [text] [messages]
- *Health*: heal [who] by [n] [combat]; hurt [who] by [n] [combat]
- *Movement*: teleport [who] to [pos] (red) [teleport]; launch [who] [dir] with force [n] (red) [combat]; launch [who] away from [from] with force [n] (red) [combat]
- *Stats*: stamina of [who]; most stamina of [who]; speed of [who] (metres a second); times [who] has died; [skill] skill of [who]; side of [who] (players, wild, boss, tamed)
- *What they are doing*: [who] is swimming; [who] is in water; [who] is sitting; [who] is running; [who] is blocking; the way [who] faces; the place [who] is looking at; the place [m] metres in front of [who]
- *What they carry*: what [who] holds in [slot]; everything [who] carries (a list) [items]
- *Health and effects*: set the stamina of [who] to [n] [combat]; give [who] the effect [effect] for [n] seconds [combat]; take the effect [effect] off [who] [combat]
- *Creatures*: tame [who] (red) [spawn]; remove [who] [spawn]

**World** (28 blocks)

- *Time and weather*: the day number; time of day (0 to 1); players online; it is night; it is day; the weather is [kind]; the weather; set the weather to [kind] for [n] minutes [weather]; turn the weather to a random one for [n] minutes [weather]; set the time to [t] [weather]; set the wind to [deg] degrees, strength [s], for [n] seconds [weather]
- *Places*: [pos] moved by [dx] [dy] [dz]; a random spot within [r] of [pos]
- *Creatures and items*: drop [n] [item] at [pos] [spawn]; spawn a [creature] near [pos] [spawn]; spawn a [stars]-star [creature] near [pos] [spawn]; spawn a [stars]-star [creature] near [pos] and keep it in [v] [spawn]
- *Time*: the real hour (on the clock); the real minute (on the clock)
- *Nearby*: creatures within [r] of [pos]; players within [r] of [pos]; built pieces within [r] of [pos]; the creature just spawned
- *Events*: start the event [name] (red) [spawn]; stop the current event [spawn]
- *Building*: place [piece] at [pos] (red) [world]; place [piece] at [pos] and keep it in [v] (red) [world]; remove [p] (only one this script placed) (red) [world]

**Effects (sights, sounds, banners)** (7 blocks)

- *Messages*: show [who] the banner [text] [messages]; show everyone the banner [text] [messages]; ping the map at [pos] saying [text] [messages]
- *Sights and sounds*: play the effect [effect] at [pos] [effects]; play the sound [sound] at [pos] [effects]; strike lightning (no damage) at [pos] [effects]; shake the screen near [pos] within [r] metres, strength [s] [effects]

**Actions** (5 blocks)

- *Messages*: announce [text] [messages]
- *Debug*: write [text] in the log
- *Fights*: set the damage to [n] [combat]; multiply the damage by [n] [combat]
- *Outside the game*: send [text] to the web address named [endpoint] (red) [net]

**Memory (variables, saved in the world)** (14 blocks)

- *Variables*: set [v] to [val]; set [v] to the words [val]; change [v] by [n]; [v]; set [v] to [val]; make [v] an empty list
- *Saved in the world*: the world; remember [val] for [who] as [name] [store]; remember the words [val] for [who] as [name] [store]; recall [name] of [who] [store]; recall the words [name] of [who] [store]
- *Local variables*: make local [name] = [val]; set local [name] to [val]; local [name]

**My blocks and signals** (7 blocks)

- *My blocks*: define [name] with inputs [p1] [p2] [p3] [p4]; do [name] [a1] [a2] [a3] [a4]; result of [name] [a1] [a2] [a3] [a4]; give back [v]; input [p]
- *Signals*: send signal [name] with [val]; the signal's value

**New in this round** (all compile to the same sandboxed JavaScript): `wait` (the code after it is carried by the host's own timer, so there is no async or generator in the engine: Jint 3.1.6's `await` blocks the thread and generators are off by default), loops that wait, my blocks with up to four inputs (one that waits is called with a "what next" function and so can only be used as a stack block), signals, lists (500 items at most), `break` and `next turn`, local variables (a `let` in the part they were made in), notes, and one block for every item of docs/scripting-expansion.md sections 1 to 4.

### How it is stored and run

```json
{ "format": 1, "api": 1, "name": "Lucky Boars", "about": "Boars sometimes drop an extra snack.", "vars": [],
  "tops": [ { "x": 30, "y": 30, "blocks": [
    { "type": "on_kill", "f": { "creature": "Boar" }, "do": [
      { "type": "if", "in": { "cond": { "type": "chance", "f": { "p": "10" } } }, "do": [
        { "type": "give", "f": { "n": "2", "item": "Honey" }, "in": { "who": { "type": "ev_killer" } } },
        { "type": "say",  "f": { "text": "Lucky boar!" },     "in": { "who": { "type": "ev_killer" } } } ] } ] } ] } ] }
```

- A program is a list of **stacks** (`tops`) on the workspace, each with a place (`x`, `y`) and its blocks. A hat block holds the
  blocks under it in `do`. `f` are the values typed or chosen in a block's slots, `in` the blocks plugged into its sockets, `do` and
  `else` the blocks inside a C-shaped block. Loose blocks (a stack with no hat) are kept but never run.
- Two files with one name: `NAME.blocks.json` (this, the truth) and `NAME.js` (the code made from it, which the host runs). The
  `needs` line of the manifest in `NAME.js` is written by the compiler from the blocks used.
- The host reads the JSON, rejects unknown block types and values outside the lists (a hand-edited file cannot add a power), computes
  `needs`, **generates JavaScript**, and runs it in the same sandbox as a typed script. Budgets, caps and refusals therefore apply the
  same way, with no second code path.
- "Code" is the way to learn: the generated JavaScript, read-only. (Converting to editable text is not built.)
- The editor highlights the block that caused a ScriptError or a refusal, using a map from generated lines to blocks.

### Editor behavior

- **Palette** on the left by category colour; **workspace** in the middle with scroll and zoom; **trash** area; undo/redo; copy and
  paste blocks; right-click for duplicate and delete; blocks snap by shape (a hat block takes a stack; a value fits only a value slot).
- Dropdowns: items, creatures, sounds and weather names come from the host's lists, so a server admin's changes show up.
- Cursor: Valheim holds the mouse for the camera, so the editor takes the cursor the way the inventory does and gives it back cleanly,
  without fighting the chair's input grab.
- Mouse on the Terminal screen: the driver's cursor on the screen is already a point in the screen's own 1280 x 720 space, so the
  editor gets plain positions plus the button going down and up; no raycast is needed. The same editor also opens in a window of its
  own (Home, or the console command `blockscript`) for people with no computer built.

### The text editor (advanced mode)

For people who prefer typing: line numbers, syntax colours, copy and paste, the manifest block. It can be the same UI panel (not on the
screen) in version 1, and move onto the screen later. It is not on the critical path.

## Sources (where a script can come from)

| Source | Default | Notes |
|---|---|---|
| Examples shipped with the mod | on | written and tested by us |
| Curated gallery | on | reviewed, hashed, pinned, remotely removable |
| Written in the Terminal | on | the player's own code; runs with the same sandbox |
| Local files in `BepInEx/config/Blockscript/` | **off** | "advanced", with a warning: "scripts here have not been reviewed" |
| Pasted from a web page or chat | off | the editor warns on paste of long text it did not write |

**Store rules to check before publishing the mod** (do this first, it can change the whole plan): whether Thunderstore and Hexium allow
a mod that runs player-supplied scripts and a mod that downloads a gallery feed. A fixed-purpose interpreter is not a loader for DLLs,
but the sites' rules decide, so read them and ask before building the gallery.

## Multiplayer

- Scripts run on the server. The Terminal's editor sends text to the server; the server stores scripts in the world's folder.
- Clients are told what is running (name, version, hash) at join, so everyone can see the same rules in the panel.
- A client that has the host but a server that does not: scripts are not available (`NeedsServer`).
- Everything a client can ask (run, stop, install) is an RPC the server checks against who may do it.
- Effects that need to be seen everywhere (a message, a sound, a spawn) use the game's own ZDO/RPC paths, as the other mods do.

## Remote editing (a server's scripts, from another game)

Contract: `docs/scripting-multiplayer.md` sections 7 and 8. Built 2026-10-12; tried by two players on 2026-10-13 (an admin editing the host's scripts from their own game, and editing one block script together).

**The idea.** The scripts live on the server. A player's editor never touches the host or the disk itself: it asks a **backend**
(`Mp/Backend.cs`). On the game that runs the scripts that is `LocalBackend`, which does what the editor always did. On a game connected to a
server that has the mod it is `RemoteBackend`; on a game connected to a server that does not, `DisconnectedBackend`, which says so
("The server does not have Quad's Blockscript: scripts only run in single player or on a server that has it").

| Who | What the editor shows | What it may do |
|---|---|---|
| The game that runs the scripts (single player, host, dedicated server's owner) | everything, as before | everything |
| An **admin** (the server's admin list, checked on the connection itself) connected to a server with the mod | the server's scripts, code, blocks, logs and limits, title "Server: NAME (version)" | everything the owner can: edit blocks, run, stop, allow powers, make, delete, trace. A save is one request (`writeBlocks`: blocks and code together); the server makes the code again itself and keeps its own |
| **Any other player** with the mod on that server | the list of what the server runs (name, what it says it does, running or not, its powers, whether it is blocks) and the server's limits, **read only** | nothing: the palette is dimmed, no Run, Stop, New or Delete, the powers cannot be toggled, no code or logs ("Only admins can change scripts on a server. You can see what it runs.") |

**How it talks.** One request name, `req {op, a}`, and one answer, `res {ok, why, d}`, tied by an id, over the direct connection
(`Wire`, owned by the multiplayer core). Ops: `list`, `settings` (anyone), and for admins `source`, `log`, `logrows`, `logtext`,
`logcounts`, `stats`, `refused`, `trace`, `status`, `start`, `stop`, `allow`, `write`, `writeBlocks`, `new`, `delete`, `blocks`, `logclear`, `logpath`.
The server (`Mp/AdminServer.cs`) answers `list` and `settings` for everyone and everything else only after the admin check; a
request is limited to 20 a second for each connection (the rest are dropped without an answer), at most 512 KB, JSON nested at most 16
deep, code at most 120 KB and blocks at most 400 KB, a script's name must be exactly a script's name (letters, digits, `-`, `_`, at most 40,
`.js`), so nothing can leave the scripts folder. Every change an admin makes goes to the audit log with their name.

**How it stays fast.** The client keeps the last answer to each question the editor keeps asking (the list, a script's status, its log
rows, its code) and asks again in the background about twice a second while the window is open; the editor reads the cache, so a frame never
waits for the network. Things somebody pressed (a write, a start, a delete) are one request each whose answer comes to a callback; a
request nobody answers ends after 5 seconds as "no answer from the server", shown in the editor's status line with a dot (green: answering,
orange: asking, red: no answer). Joining or leaving a server starts the editor over.

**Limits and lists.** The Limits panel shows the server's numbers while connected ("This server's limits"): the server sends its own
limit lines in `settings`, one list per power, in its own words.

**Where it lives.** `Mp/AdminCore.cs` (plain C#, no game: the frames, the admin gate, the rate limit, name cleaning, size caps, the
client's cache and request book, the server's dispatcher), `Mp/Backend.cs`, `Mp/RemoteBackend.cs`, `Mp/AdminServer.cs`;
`tests/Scripts.Admin.Tests` runs a client against a server in memory (103 checks: who may ask what, the limits, timeouts, the cache,
a non-admin that tries to write anyway). `blockscript server` in Claude Tools prints what a game thinks of its server.

**Not yet verified.** Everything above the transport is tested; the transport itself, the admin check on a real connection, and the
editor on a joined game need a dedicated server (or a listen server with a second player) to be tried.

## Risks to settle first (a spike before any feature)

1. **Jint on the game's runtime.** Does the Jint build we can ship run on Valheim's Mono (no JIT surprises, no missing assemblies)?
   Spike: a plugin that evaluates `1+1` and an infinite loop, logs both. If Jint will not load, fall back to MoonSharp (Lua), which is
   pure C# and smaller, and replace the budgets with MoonSharp's auto-yield counter and a hook-based statement count. (The block
   compiler would then emit Lua; nothing else changes.)
2. **Real limits.** Do `TimeoutInterval`, `MaxStatements` and `LimitMemory` actually stop a hostile script in under a frame? Spike with
   a loop, a huge string, and recursion, on the game's thread.
3. **The block editor UI.** Drag and drop, snapping, nested stacks, scroll and zoom, copy/paste, undo, in Unity UI, in the game's style.
   Spike: three block types, drag them, nest them, run the result. This is the largest piece of work in the plan.
4. **Mouse on the Terminal screen.** A raycast onto the screen quad to UI coordinates, and the cursor handoff with the camera and the
   chair. Fallback: the editor as a full-screen UI panel while seated (the monitor mirrors it).
5. **Blocks must be as safe as typed code.** The generator's output is run in the same sandbox; test it with a fuzzer (random block
   trees) and with hand-edited JSON that names blocks or values that do not exist.
6. **Typing without moving** (text mode only): reuse the Terminal's input grab; test that no key reaches the character.
7. **Item and piece safety.** Walk the grant path against `docs/modding-pitfalls.md`; the lists and caps are data and need a test with
   a script that tries every refused case. Check that a full bag never loses the rest of a `give`.
8. **API drift.** The event names and shapes are a promise to strangers. Settle them in the review (build step 0) before any release.
9. **The "bot" case.** A script that drives the Proxy can loop on `walkTo`; check that the Proxy's pathing cannot lock a server's tick
   (budget the pathing, queue the requests).
10. **Store rules** of Thunderstore and Hexium (see Sources). Check before building the gallery.

## Build order

0. **API review** with the user: read `docs/scripting-api.md` and settle the names, the caps and the lists. Names are a promise.
1. **Spikes**: (a) Jint inside a plugin, `1+1`, the three hostile scripts, the numbers (risks 1 and 2); (b) three draggable blocks in
   a UI panel and a mouse raycast onto a quad (risks 3 and 4).
2. **Host core**: engine per script, manifest parser, capability list, budgets, `log`, `say`, `every`; console commands to run a
   local file. Test with hostile scripts first.
3. **Events and the safe actions**: `kill`, `chat`, timers, `store`, `give` with caps and the lists; the audit log.
4. **World safety and the plain panel**: the snapshot, safe mode, the kill switch, the list of scripts with on/off, the log. No
   Terminal needed.
5. **The block compiler and the block editor in the plain panel**: blocks to JSON to JavaScript, the permission screen generated from
   the blocks, "Show code". This is the first editor players use.
6. **The Terminal program**: the same editor on the screen (or the full-screen fallback), file list, run, log. Single player first.
7. **Gallery and examples**: the feed, hashes, remote disable; the 10 examples as blocks plus generated code.
8. **The text editor (advanced mode)** and "convert to text".
9. **Proxy API and the Proxy blocks.**
10. **Multiplayer**: the editor from a client, the join list, a server without the mod, the admin settings. Then a dedicated server.
11. Look pass, icon, translations (block labels too), pages (like the other mods), then a real restart test and a test with Bob.

## Example scripts (the first gallery)

All ten ship as block programs (with the generated JavaScript beside them). The first is shown here in both forms.

1. **Lucky Boars**: a kill drops a small reward. `items`, `messages`.
2. **Weather Event**: `every(minutes(20))` sets weather and announces it. `weather`, `messages`.
3. **Hardcore Corner**: a death costs half your coins. `takeItems`. **Marked risky** (it takes).
4. **Dodge Roll Bonus**: `hit` with `isDodging` sets damage to 0. `combat`.
5. **Kill Streaks**: counts in `store`, announces every 5. `store`, `messages`.
6. **Boss Announcer**: `bossSpawn`/`bossDeath` with a bell. `messages`.
7. **Welcome Wagon**: `playerJoin` greets by name and tells the new player the day and the weather. `messages`.
8. **Daily Reward**: a chat command or a join gives a few coins once per in-game day via `store`. `items`, `store`.
9. **Proxy Woodcutter**: the Proxy walks to the nearest tree, chops it, collects the logs, puts them in a chest, repeats, goes home at
   night. `proxy`. The showpiece of the Terminal link.
10. **Proxy Greeter**: the Proxy stands by the door and says hello to anyone who walks up. `proxy`, `messages`.

Lucky Boars as blocks and as the generated code:

```
[when a Boar is killed]
  [if chance 10 %]
    [give killer 2 Honey]
    [say killer "Lucky boar!"]
```

```js
on("kill", e => {
  if (e.victim.prefab !== "Boar") return;
  if (chance(0.10)) {
    e.killer.give("Honey", 2);
    e.killer.say("Lucky boar!");
  }
});
```

Proxy Woodcutter (JavaScript). A state machine on a timer; nothing waits. It needs an axe in the Proxy's own bag (the Proxy starts
with a club and rags, so the player has to put one in first).

```js
// ==Script==
// name:  Proxy Woodcutter
// needs: proxy, messages
// api:   1
// ==/Script==

let state = "idle";
let treeId = null;

function tree() { return treeId ? findById(treeId) : null; }

every(3, () => {
  if (proxy.busy) return;                       // a player is driving it
  if (isNight()) { proxy.home(); state = "idle"; return; }

  if (state === "idle") {
    if (!proxy.has("AxeStone") && !proxy.has("AxeBronze")) { proxy.say("I need an axe."); return; }
    proxy.equip("hand", proxy.has("AxeBronze") ? "AxeBronze" : "AxeStone");
    const t = proxy.nearest("tree", 40);
    if (t && proxy.walkTo(t.pos)) { treeId = t.id; state = "toTree"; }

  } else if (state === "toTree") {
    if (proxy.arrived()) state = "chopping";

  } else if (state === "chopping") {
    const t = tree();
    if (t && t.valid) proxy.attack(t);          // one swing per tick
    else state = "collect";                     // the tree is down

  } else if (state === "collect") {
    const log = proxy.nearest("pickup", 12);
    if (log) proxy.use(log);                    // one pickup per tick
    else state = "toChest";

  } else if (state === "toChest") {
    const chest = proxy.nearest("chest", 60);
    if (chest && proxy.walkTo(chest.pos)) state = "atChest";
    else state = "idle";

  } else if (state === "atChest") {
    if (proxy.arrived()) {
      proxy.deposit(proxy.nearest("chest", 6), { only: ["Wood"] });
      state = "idle";
    }
  }
});

on("proxyEvent", e => {
  if (e.kind === "taken" || e.kind === "blocked") state = "idle";
});
```

Prefab names (`AxeStone`, `AxeBronze`, `Wood`) are to be checked against the game's real list in the spike.

## Tests

- A **hostile suite** the host must pass before a release: infinite loop, deep recursion, a huge string, a huge array, `eval`,
  `Function`, `import`, a forged handle, a negative or huge quantity, a blocked prefab, a spawn flood, a message flood, a store
  that fills up. Each ends in a paused script and a readable line in the panel, and the game keeps its frame rate.
- **Item/world tests**: run the Examples, then check the audit log against the player's inventory; restore a snapshot and compare.
- **Block tests**: a fuzzer that builds random block trees and checks the generated code stays inside the sandbox; hand-edited block
  JSON with unknown block types, items outside the lists and oversized trees must all be rejected on load; the generated code for
  each example must match a stored copy (the generator is deterministic).
- **API compatibility test**: every released example still runs after each host change (the frozen-names rule).
- A **real restart test** and a **multiplayer test** (a client, then a dedicated server) before any release, like the others.

## Decided with the user (2026-10-12)

- The scripting host is combined with the Terminal: a **Scripts program** on the computer, and scripts can drive the **Proxy**.
- Audience is the **public**, so the sandbox and safety rules are part of version 1.
- A **block (Scratch-style) editor** is part of the design and the primary editor; text is the advanced mode.
- The scripting program also opens as a **screen of its own** (a window, no computer needed), with the same features as the Terminal's.

## Built (2026-10-12)

- `mods/Scripts.Engine/Blocks`: the block language (model, definitions, compiler to JavaScript, example programs). No Unity in it, so
  `tests/Scripts.Blocks.Tests` runs it for real: 4000+ checks including a fuzzer (random programs: what compiles runs, what does not
  gives no code) and the examples run in Jint against stand-ins for the host.
- `mods/Scripts.Engine/Editor`: the shared editor (a painter with pooled UI objects, the block view with palette, workspace, drag and
  drop, undo, menus; the screen with the list, blocks, code, powers, log and the "run?" box) and the window (Home).
- `mods/Terminal/ScriptsApp.cs`: the program on the computer hosts the same editor with the driver's pointer.
- Not tried in the game yet when this was written: the editor's look and handling, dragging with the Terminal's cursor.
- Not built: gallery, world snapshot, safe mode, the text editor, `hit`/`pieceBuilt`/`itemPickup`/proxy blocks, multiplayer sharing of
  what the driver is editing (other players' screens show their own copy of the list, not the editor in use).

## Decisions for the user

1. **Gallery only, or local files too** (behind a warning)? The design says both, with local files off by default.
2. **Plain in-game panel as well as the Terminal?** The build order needs one anyway (steps 4 and 5), and it lets people use scripts
   without building a computer. Recommended: yes.
3. **Server support in 1.0** or listen-server first? Dedicated servers roughly double the testing, and multiplayer with Bob is still
   untested for other mods.
4. **Who can run scripts on a server** by default: admins only (recommended), anyone at a Terminal, or nobody?
5. **Text editor in 1.0 or 1.1?** Blocks first is decided; the text editor is advanced mode and can follow.
6. **Gallery feed**: reuse the F7 manager's approved-sources format, or its own feed?
7. **The creature tier** for `spawn` (default up to Black Forest) and the starting **item allow-list**: review in build step 0.
8. **Name**: Quad's Blockscript is a working title.

## Not now

- Calling other mods' APIs. Possible later as an **opt-in** from the other mod (it exposes an API on purpose); never by reflection.
- Terrain edits, removing pieces, character-file access.
- Client-side scripts and custom UI from scripts.
- Lists, functions and custom blocks in the block editor; text-to-blocks conversion; a visual debugger; a script marketplace with ratings.

## Working together

Several admins (and the server's owner) can open the same block script and change it at the same time.

- **Who.** The owner's own game and any admin of the server. Everyone else can see the list of scripts, read-only.
- **How it looks.** The script list says "Bob editing now" on a script someone has open. Inside, "Editing now" shows a coloured dot and name
  for each person. The block someone has selected has an outline in their colour with their name; their pointer is a small arrow with their name.
- **Dragging.** While someone drags a block, it is dimmed for the others ("Bob is moving these"), and nobody else can change it or pick it up:
  they see "Bob is moving that". When Bob lets go, it is free again.
- **Conflicts.** Different stacks merge without a fuss. If two people change the same slot at once, the later one wins for everybody. If someone
  deletes a block you were changing, your change is dropped and the message says why.
- **Undo.** Undo and redo only take back your own changes, never a friend's.
- **Saving.** The server saves a second after the last change, so nothing is lost if somebody leaves. A script running when it is saved restarts with the new blocks.
- **Plain code.** A script written in code (not blocks) is replaced as a whole. If it changed since you looked, you are told who changed it and
  how long ago, and **Reload** takes you to the new version.
