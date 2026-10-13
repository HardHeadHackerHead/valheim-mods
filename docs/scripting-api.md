# Quad's Blockscript: API reference, version 1

> **Status (2026-10-12):** the API reference for **Quad's Blockscript**, version 1. Everything here is built unless a section says otherwise (section 12.1 explains why `wait` is compiled to timers). Players: start with `mods/Blockscript/README.md`.

The full API a script sees. The design, the reasons and the safety rules are in `docs/scripting-design.md`; this file is the contract
that scripts, the block editor and the gallery are written against. Once released, names and behavior in an API version **never change**
(see Versioning).

Scripts are JavaScript (ECMAScript 2020 level, through Jint). Block programs compile to the same API, so everything here is also
what a block does.

## 1. Conventions

### Types

| Type | What |
|---|---|
| `number` | A JavaScript number. Counts and levels are whole numbers; the host rounds down and rejects `NaN`/`Infinity`. |
| `string` | Text. Lengths are capped per call (stated below). |
| `Vec3` | A plain object `{x, y, z}` in world meters. Make one with `vec(x, y, z)`. Copies, never live references. |
| `Actor` | A handle to a player or a creature (section 4). |
| `Player` | An `Actor` that is a player; has the extra player members. |
| `Creature` | An `Actor` that is a creature. |
| `Piece` | A handle to a built piece (section 4). |
| `Item` | A read-only snapshot `{prefab, name, count, quality}`. |
| `id` | A `string`. Stable for the life of the object. Players' ids are stable across sessions. |
| `seconds` | A `number`. Use `minutes(n)` / `hours(n)` to build one. |

### Handles

- A handle is a small wrapper around an id. It is not the game object. A script can never reach the game object behind it.
- `h.valid` is `true` while the object exists and is loaded. After it is gone (killed, despawned, player left, zone unloaded) every
  call on the handle does nothing and returns `null`, `0` or `false`, and `valid` is `false`. It never throws for that.
- Do not keep a handle between sessions. Keep `h.id` in `store` and look it up again with `findById(id)`.
- A handle cannot be put in `store` (a `ScriptError`); use its `id`.

### Results and errors

There are two kinds of "no":

| Kind | When | What the script sees | What the panel shows |
|---|---|---|---|
| **Refusal** (soft) | A cap, a blocklist, an unknown prefab, a power the player did not approve, a dead handle, a full bag | The call returns `false`, `0` or `null` (each call says which). The script keeps running. | A counter "refused: n" per reason, and a log line at most once a minute per reason. |
| **ScriptError** (hard) | A bug in the script: wrong argument type, a power not listed in `needs`, a player-only call on a creature, an action at top level, a handle put in `store` | The current handler stops. | A red line: script, line number, the line, a plain sentence. Five in one minute pauses the script. |

Return values always say what **actually** happened: `give` returns how many were given, not how many were asked for.

### When code runs

- The top level of a script runs once, when it starts. At top level you may only **register** things: `on`, `every`, `after`,
  `command`, `store.get/set`, `log`. Any action there is a `ScriptError` ("do that inside an event or a timer"), because players and
  zones may not be loaded yet. Put start-up work in `on("start", ...)`.
- Handlers and timers run **on the server, on the game thread**, one at a time, in the order they were queued. Scripts take turns
  round-robin; one script cannot starve the others.
- There is no waiting in typed code. No `sleep`, no `setTimeout`, no `await`, no loop that pauses. Use `every`/`after`, or keep a `state`
  variable and let a timer advance it (see Idioms). The `wait` block does exactly that for you: it compiles to `after` calls (section 12.1).
- Events queue per script (100 at most); when full, the oldest is dropped and counted in the panel.

### What the language has and has not

| Available | Removed |
|---|---|
| `let`/`const`, arrow functions, template strings, destructuring, spread, classes, `Map`/`Set`, `JSON`, `Math` (except `random`), `Array`/`String`/`Number`/`Object` methods | `eval`, `Function`, `import`, `require`, `Date`, `Math.random` (use `random()`), `Proxy`/`Reflect`, `WebAssembly`, `Intl`, `globalThis` writes, every `System`/`UnityEngine` name, timers from the browser (`setTimeout`, ...) |

### Capabilities

A script lists what it needs in its manifest (`needs:`). A call that needs a capability the script did not list is a **ScriptError**.
A capability the script listed but the player or admin did not approve makes that call a **refusal**.

| Capability | Plain words (shown to the player) | Default | Flag |
|---|---|---|---|
| *(none)* | read what is going on (who is online, where creatures are, the time) | allowed | |
| `messages` | show messages and play sounds | allowed | |
| `effects` | play visual and sound effects | allowed | |
| `store` | remember small things between sessions | allowed | |
| `items` | give you small amounts of items | ask | |
| `takeItems` | take items from you | ask | red |
| `spawn` | spawn creatures near players and drop items on the ground | ask | |
| `combat` | change damage in fights (it cannot kill a player) | ask | |
| `weather` | change the weather and the time of day | ask | |
| `world` | place decorative pieces (it can only remove its own) | ask | red |
| `teleport` | move players | ask | red |
| `proxy` | drive the Terminal's Proxy character | ask | |
| `net` | send short messages to a web address the admin set up | off | red |

## 2. Globals (no capability needed)

### Time

| Call | Returns | Notes |
|---|---|---|
| `minutes(n)` | `seconds` | `n * 60` |
| `hours(n)` | `seconds` | `n * 3600` |
| `time()` | `number` | Game seconds since the world began (the world clock; not the computer's clock). |
| `day()` | `number` | The current in-game day, from 1. |
| `timeOfDay()` | `number` | 0 to 1; 0 is midnight, 0.5 is noon. |
| `isNight()`, `isDay()` | `boolean` | Night is the game's own night. |

### Random (host-seeded, so a script cannot fix the odds)

| Call | Returns | Notes |
|---|---|---|
| `chance(p)` | `boolean` | `true` with probability `p` (0 to 1). |
| `random(a, b)` | `number` | A float from `a` up to (not including) `b`. |
| `randomInt(a, b)` | `number` | A whole number from `a` to `b`, both included. |
| `pick(list)` | any or `null` | A random element; `null` for an empty list. |

### Positions

| Call | Returns | Notes |
|---|---|---|
| `vec(x, y, z)` | `Vec3` | |
| `distance(a, b)` | `number` | Straight-line meters between two `Vec3`, or two `Actor`/`Piece` handles. |
| `offset(pos, dx, dy, dz)` | `Vec3` | `pos` moved by the amounts. |
| `around(pos, radius)` | `Vec3` | A random point on the flat circle around `pos` (same `y`). |
| `groundHeight(pos)` | `number` or `null` | The ground's `y` at that `x,z`, `null` if the zone is not loaded. |
| `biomeAt(pos)` | `string` | `"Meadows"`, `"BlackForest"`, `"Swamp"`, `"Mountain"`, `"Plains"`, `"Mistlands"`, `"AshLands"`, `"DeepNorth"`, `"Ocean"`. |

### Looking things up

| Call | Returns | Notes |
|---|---|---|
| `players()` | `Player[]` | Everyone online. |
| `findPlayer(name)` | `Player` or `null` | Case-insensitive, exact name. |
| `findById(id)` | `Actor`, `Piece` or `null` | For an id kept in `store`. |
| `nearbyCreatures(pos, radius, opts?)` | `Creature[]` | `radius` up to 50 m (clamped); at most 50 results, nearest first. `opts`: `{prefab: "Boar", hostile: true}`. Loaded zones only. |
| `weather()` | `string` | `"clear"`, `"rain"`, `"storm"`, `"mist"`, `"snow"`. |

### Other

| Call | Returns | Notes |
|---|---|---|
| `log(text)` | nothing | Into the panel's log. 200 characters, 20 lines a minute; the rest are dropped and counted. |
| `host.apiVersion` | `number` | The highest API version the host speaks. |

## 3. Events: `on(name, handler)`

`on(name, fn)` registers `fn(e)`. A script may register several handlers for one event; they run in registration order. Event objects
are **read-only** except where a field says "writable". Handles inside an event are valid only while the event is handled, unless the
object still exists afterwards (then they stay valid).

| Event | Fires | `e` fields |
|---|---|---|
| `start` | Once, after the script's top level, with the server running | none |
| `stop` | Once, when the script is stopped or removed (20 ms to finish) | none |
| `playerJoin` | A player has finished joining | `player` |
| `playerLeave` | A player is leaving (handle valid during the call only) | `player` |
| `playerDeath` | After a player dies | `player`, `pos`, `biome`, `cause` (`"creature"`, `"fall"`, `"fire"`, `"drown"`, `"cold"`, `"poison"`, `"other"`), `killer` (`Actor` or `null`) |
| `kill` | After any creature or player dies | `victim` (`Actor`), `killer` (`Actor` or `null`), `pos` |
| `hit` | **Before** damage is applied | `attacker` (`Actor` or `null`), `target` (`Actor`), `pos`, `damage` (**writable** number, see below) |
| `pieceBuilt` | A player has placed a piece | `player`, `piece` |
| `itemPickup` | A player has picked an item up | `player`, `item` |
| `bossSpawn` | A boss has appeared | `boss` (`Creature`), `biome`, `pos` |
| `bossDeath` | A boss has died | `boss`, `biome`, `pos`, `killer` (`Actor` or `null`) |
| `chat` | A player has sent a chat line | `player`, `text`, `channel` (`"normal"`, `"shout"`, `"whisper"`); read-only, cannot be hidden or edited |
| `dayStart` | Morning | `day` |
| `nightStart` | Dusk | `day` |
| `proxyEvent` | The Proxy did something (needs the Terminal) | `kind`, plus the fields below |

`proxyEvent` kinds: `spawned`, `died` (`killer`), `respawned`, `arrived` (a `walkTo` reached its goal), `blocked` (a `walkTo` cannot
get there), `taken` (a player sat down at the Terminal: the driver always wins), `released` (the driver stood up), `seen`
(`player` came within 5 m of the Proxy).

### The `hit` event is special

- It runs on the hot path, so its handlers get a **tighter budget** (0.2 ms, a setting) than other handlers.
- `e.damage` is a number. A script with `combat` may **write** it; the host then clamps the result:
  - to `0 ... 3 x` the original for a creature target;
  - to `0 ... 1.5 x` the original for a player target, and **never to or above the player's current health** (a script cannot kill a player).
- Without `combat` (or without approval) the write is ignored and counted as a refusal.

### Other commands

| Call | Notes |
|---|---|
| `command(name, handler)` | Register a chat command `/name`. `handler(player, args)` where `args` is an array of strings. 5 per script. A name that the game or a known chat mod already uses is a `ScriptError`. Runs on the server; the reply is whatever the handler `say`s. |
| `off(name?)` | Remove this script's handlers (all, or one event). |

## 4. Handles

### Actor (a player or a creature): read members

| Member | Type | Notes |
|---|---|---|
| `id` | `string` | |
| `prefab` | `string` | `"Player"` for players |
| `name` | `string` | Display name; the player's name for players |
| `pos` | `Vec3` | A copy at the time of the read |
| `hp`, `maxHp` | `number` | |
| `level` | `number` | Creatures: 1 to 3 (stars + 1). Players: 1. |
| `isPlayer`, `isBoss`, `isTamed`, `isDead` | `boolean` | |
| `valid` | `boolean` | |
| `distanceTo(other)` | `number` | `other` is an `Actor`, `Piece` or `Vec3` |

### Actor: actions

| Call | Needs | Returns | Notes |
|---|---|---|---|
| `heal(n)` | `combat` | `number` healed | Up to `maxHp`. 500 a call. |
| `damage(n)` | `combat` | `number` dealt | On a **player**: at most `hp - 1` (cannot kill), 50 a second. On a **creature**: up to 500 a call; it can kill, and `kill` then fires with `killer = null`. Never on bosses (refused). |

### Player: extra members and actions

| Call | Needs | Returns | Notes |
|---|---|---|---|
| `isDodging` | | `boolean` | Read. |
| `isAdmin` | | `boolean` | Read. |
| `biome` | | `string` | Read; as `biomeAt(pos)`. |
| `say(text, opts?)` | `messages` | nothing | A message on that player's screen. `opts.style`: `"center"` (default) or `"corner"`. 120 characters; 5 a second per script. |
| `give(prefab, n = 1)` | `items` | `number` given | See "Items". Whole numbers, 1 to 10 a call. Gives only what **fits in the bag** (returns the smaller number; never drops or loses the rest). 50 per hour per player per script. |
| `take(prefab, n = 1)` | `takeItems` | `number` taken | Only from the main bag, never equipped or hotbar-pinned items, never more than the player has. 20 per hour per player per script. |
| `count(prefab)` | `items` or `takeItems` | `number` | Read. |
| `has(prefab, n = 1)` | `items` or `takeItems` | `boolean` | Read. |
| `teleport(pos)` | `teleport` | `boolean` | See "Teleport". 1 per 10 seconds per player. |

A player-only call on a creature is a `ScriptError`; check `isPlayer` first.

### Creature: extra members

| Member | Type | Notes |
|---|---|---|
| `spawnedBy` | `string` or `null` | The name of the script that spawned it, or `null`. |

### Piece

| Member | Type | Notes |
|---|---|---|
| `id`, `prefab`, `valid` | | |
| `pos`, `rot` | `Vec3` | `rot` is euler degrees |
| `builtBy` | `string` or `null` | Player id, if known |
| `spawnedBy` | `string` or `null` | Script name if a script placed it |
| `remove()` | `boolean` | Needs `world`. Only a piece **this** script placed. Anything else is refused. |

## 5. Actions on the world

| Call | Needs | Returns | Cap and rules |
|---|---|---|---|
| `broadcast(text, opts?)` | `messages` | nothing | To every player. 120 characters; 1 a second per script. `opts.style` as `say`. |
| `playSound(name, pos, opts?)` | `messages` | `boolean` | `name` from the sound allow-list (data). `opts.range` up to 80 m. 5 a second. |
| `spawn(prefab, pos, opts?)` | `spawn` | `Creature` or `null` | `opts.level` 1 to 3. The prefab must be on the creature allow-list for the **current tier** (below); no bosses. `pos` must be within 60 m of a player and in a loaded zone. 5 a minute per script, 20 alive at once per script. Spawned creatures are tagged with the script's name and are removed when the script is removed. |
| `dropItem(prefab, n, pos)` | `spawn` | `number` dropped | As `give` but on the ground. Same lists and caps; counts toward the same hourly cap. |
| `world.setWeather(name, seconds)` | `weather` | `boolean` | `name` is `"clear"`, `"rain"`, `"storm"`, `"mist"`, `"snow"`; the host picks the matching environment for the biome. `seconds` 30 to 1800. 1 a minute. |
| `world.setTime(t)` | `weather` | `boolean` | `t` is 0 to 1 like `timeOfDay()`. 3 at once, then 1 every 20 seconds. |
| `world.spawnPiece(prefab, pos, rot?)` | `world` | `Piece` or `null` | Prefab from the piece allow-list (decorations: banners, torches, markers, small furniture; **no chests, no workbenches, no portals**). Not overlapping a built piece, not in a no-build area. 20 a minute, 200 alive per script. Persistent: stays after the script stops unless the script removed it. |
| `net.send(endpoint, text)` | `net` | `boolean` | `endpoint` is a *name* from the admin's config (`"discord"`), never a URL. `text` up to 2000 characters. 1 per 10 seconds. The reply is not given to the script. |

### Teleport

`player.teleport(pos)` ignores the `y` of `pos`. The host reads the ground height at `x,z` and puts the player there (+1 m). If the
zone is not loaded (no ground to read) it refuses and returns `false`. This is deliberate: a teleport that lands a few meters above or
below the ground kills the player by fall damage (see the teleport memory); the script cannot choose a height.

### Items (the lists are data)

`give`, `take`, `count`, `has` and `dropItem` take a **prefab name**. The names come from three lists in the host's data, each
editable by the server admin:

- **`items.allow`**: what can be given or dropped, each with a per-call maximum. Starting list (the names are to be checked against the
  game's real prefab list in the spike): basic resources (`Wood`, `Stone`, `Flint`, `Resin`, `Coal`, `Feathers`, `LeatherScraps`),
  gathered food (`Honey`, `Blueberries`, `Raspberry`, `Mushroom`, `Dandelion`, `Carrot`, `Turnip`), simple cooked food, and `Coins`.
  Everything else is refused until the admin adds it.
- **`items.neverGive`**: always refused, even if an admin lists it in `allow`: trophies, boss items, keys, the items that end a
  progression gate (`Wishbone`, `DragonEgg`, ...), `Megingjord`-class gear, and anything else in the blocklist.
- **`items.neverTake`**: items `take` refuses (so a script cannot strip a quest item): keys, and the Hearth/Hammer/Cultivator class
  of tools.

An unknown or unlisted name is a **refusal**, not an error, so a typo does not stop the script.

### Creature tiers for `spawn`

The creature allow-list has a **tier** setting per world (default: up to Black Forest strength: Boar, Neck, Greyling, Deer, Greydwarf,
Skeleton). A higher tier is an admin choice in the panel, with a warning (a script that spawns the wrong creatures near a new
character can kill them). The permission screen states the tier in words: "spawn creatures up to Black Forest strength".

## 6. Store (needs `store`)

Small persistent memory, saved with the world, one area per script.

| Call | Returns | Notes |
|---|---|---|
| `store.get(scope, key, fallback?)` | the value, or `fallback` (default `undefined`) | `scope` is any string: a `player.id` for per-player data, `"world"` for global data. |
| `store.set(scope, key, value)` | `boolean` | `value` is a number, string, boolean, `null`, or an array/object of those. `false` if the store is full. |
| `store.remove(scope, key)` | nothing | |
| `store.keys(scope)` | `string[]` | |

Limits: 64 KB per script, 256 keys per scope, 4 KB per value. Removing a script keeps its store until the player chooses "forget this
script's data" in the panel (so reinstalling does not lose state). Handles cannot be stored; keep ids.

## 7. The Proxy (needs the Terminal and `proxy`)

A global `proxy` stands for the Terminal's one shared Proxy character. Calls do nothing (return `false`/`null`) when there is no
Terminal or the Proxy is dead. Movement uses the game's own pathing and is queued by the host (a script cannot lock the server's
tick). The Proxy is held inside the Terminal's **tether** (default 100 m).

### Read

| Member | Type | Notes |
|---|---|---|
| `proxy.pos` | `Vec3` | |
| `proxy.hp`, `proxy.maxHp` | `number` | |
| `proxy.isDead` | `boolean` | It comes back at the Terminal after a cooldown. |
| `proxy.driver` | `Player` or `null` | Who is sitting at the Terminal. |
| `proxy.busy` | `boolean` | `true` while a **player** drives it. While busy every action below returns `false`. |
| `proxy.arrived()` | `boolean` | `true` when the last `walkTo` reached its goal. |
| `proxy.nearest(kind, radius)` | handle or `null` | `kind`: `"tree"`, `"rock"`, `"pickup"`, `"chest"`, `"creature"`, `"player"`. `radius` up to 60. Tree/rock/chest handles are `Piece`-like (`id`, `pos`, `valid`). |
| `proxy.has(prefab, n = 1)` | `boolean` | In its own bag. |
| `proxy.inventory()` | `Item[]` | Its bag. |

### Act

| Call | Returns | Notes |
|---|---|---|
| `proxy.walkTo(pos, opts?)` | `boolean` | `false` if beyond the tether or no route. `opts.run`. Replaces the last order. |
| `proxy.stop()` | nothing | |
| `proxy.look(target)` | nothing | A `Vec3`, handle or direction. |
| `proxy.use(target)` | `boolean` | Interact: open a door or chest, pick up an item. Must be within reach. |
| `proxy.attack(target)` | `boolean` | One swing of what it holds, at a creature, tree or rock in reach. Call it again on the next tick for the next swing. |
| `proxy.equip(slot, prefab)` | `boolean` | `slot` is `"hand"`, `"chest"`, `"legs"`, `"head"`. Only from its own bag. |
| `proxy.say(text)` | nothing | A chat bubble above it. |
| `proxy.deposit(chest, opts?)` | `number` moved | Move items from its bag into a chest within reach. `opts.only`: a list of prefab names. |
| `proxy.withdraw(chest, prefab, n)` | `number` moved | The other way, up to what fits. |
| `proxy.home()` | nothing | Walk back to the Terminal. |

The Proxy's bag is a persistent container (the Terminal design's death rule applies: death drops a tombstone and it respawns with the
starting kit). A script cannot give the Proxy items from nowhere; it can only move items that exist.

## 8. Limits (settings in the host, per script unless noted)

| Limit | Default |
|---|---|
| Time a script may use in one frame (all of its handlers together) | 1 ms |
| Longest single call | 50 ms (`TimeoutInterval`) |
| `hit` handler time | 0.2 ms |
| `stop` handler time | 20 ms |
| Statements per call | 100,000 |
| Memory per script | 8 MB |
| Recursion depth | 64 |
| Timers per script (`every` and `after` pending at once, a `wait` is one) | 120 (`every` minimum interval 1 s) |
| Commands per script | 5 |
| Queued events | 100 |
| Frame budget for all scripts together | 2 ms |
| Over budget three frames in a row | script paused, named in the panel |
| ScriptErrors | five in one minute pauses the script |

Action caps (spawns, items, messages) are in sections 4 and 5.

## 9. Versioning

- The manifest says `api: 1`. The host runs a script against **exactly** the behavior of that version. Event names, call names,
  argument orders, return meanings and caps' *meaning* never change inside a version.
- Compatible additions (a new event, a new function, a new optional field, a new item in a list) may arrive in the same version; a
  script that does not use them is not affected. A script that calls something newer than it declares gets a `ScriptError` naming the
  version it was added in.
- A change that could break an existing script (a renamed call, a different return) ships as `api: 2`, side by side. The host keeps
  running version 1 scripts for as long as the gallery holds any (and at least a year). The panel says when a script uses an old version.
- Blocks and the gallery pin the version they were made for.
- Caps and allow-lists are data, not API: they may be tightened for safety at any time, because a refusal is a normal answer.

## 10. Idioms (since nothing can wait)

**A cooldown per player**

```js
on("chat", e => {
  if (e.text !== "!daily") return;
  const last = store.get(e.player.id, "last", -1);
  if (last === day()) { e.player.say("Come back tomorrow."); return; }
  store.set(e.player.id, "last", day());
  e.player.give("Coins", 5);
});
```

**A sequence (a state variable and a timer)**

```js
let step = 0;
every(2, () => {
  if (step === 0) broadcast("Three...");
  else if (step === 1) broadcast("Two...");
  else if (step === 2) broadcast("One...");
  else { broadcast("Go!"); step = -1; }
  step++;
});
```

**Keeping a target between ticks (by id)**

```js
let targetId = null;
every(3, () => {
  const t = targetId && findById(targetId);
  if (!t || !t.valid) { targetId = pick(players()).id; return; }
  t.say("Someone is watching you.");
});
```

**Reacting to a handle that disappeared**

Check `h.valid` and treat `false`/`null` results as normal. Do not wrap everything in `try`; a refusal is not an exception.

## 11. What a script can never do (so it is not in this file)

Read or write files; open the network except `net.send`; start a process; read a clock other than the game's; see another script's
variables or store; call another mod; make a new prefab; remove a piece it did not place; change terrain; edit a save or a character
file; hear or change a player's chat except by sending its own messages; grant a blocked item; kill a player.

## 12. The expansion (built 2026-10-12; contract in `docs/scripting-expansion.md`)

Additions inside API version 1; nothing above changed meaning. "Power" is the capability the manifest must list; a refusal is a soft
`false`/`null` and names the cap and the setting. All caps below are settings or data the world's owner can change (BepInEx config of
the Scripts mod, section `Limits` and `Allowed`).

### 12.1 Waiting (what is and is not built)

`await sleep(seconds)` and `async` handlers are **not built**. Jint 3.1.6 cannot suspend a function for a host-resolved promise: an `await`
on a promise that nobody has settled blocks the whole game thread (measured: `Invoke` never returned), and its experimental generators
return wrong values and cannot be thrown into. A script waits with `after(seconds, fn)` and `every(seconds, fn)`; the number of timers
a script may have pending at once is **60** (it was 20). Block programs compile a `wait` into a chain of `after` calls (continuations).

### 12.2 Signals, time, places

| Call | Power | Notes |
|---|---|---|
| `onSignal(name, fn)` | | `fn(data)`. 20 names per script, 20 handlers per name. A ScriptError over that |
| `signal(name, data?)` | | Queues `fn(data)` for every listener of this script, like an event (next frame, in the script's budget). 100 a second. Not at top level |
| `clock()` | | `{hour, minute, second}` of the player's real clock |
| `add(a, b)`, `scale(a, k)` | | Vector maths on places (or handles) |
| `bearing(from, to)` | | Degrees 0 to 360, 0 is +z |
| `ahead(actor, meters)` | | A place in front of an actor, on the flat; -50 to 50; `null` if the handle is gone |
| `lookingAt(player, meters = 60)` | | The place a player looks at (a raycast over ground, pieces and vehicles, 1 to 100 m), or `null` |
| `nearbyPlayers(pos, radius)` | | Up to 50, nearest first; radius up to 200 |
| `nearbyPieces(pos, radius, opts?)` | | Built pieces (`Piece` handles, 12.5), radius up to 50, 100 results, nearest first; `opts.prefab` filters. 2 a second |

### 12.3 Actors

Reads (any actor; on a handle that is gone they are `0`/`false`): `stamina`, `maxStamina` (players), `isSwimming`, `isSitting`, `isRunning`,
`isBlocking`, `isInWater`, `faction` (`"players"`, `"wild"`, `"boss"`, `"tamed"`), `facing` (a unit vector; a player's is where they look),
`speed` (m/s). Players also: `deaths` (this session), `skill(name)` (the game's skill names, any case; `null` if unknown), `inventory()`
(needs `items` or `takeItems`; up to 100 `{prefab, name, count, quality}`; only the game's own player for now), `equipped()` (`{right, left,
chest, legs, head}` prefab names).

| Call | Power | Notes |
|---|---|---|
| `actor.talk(text)` | `messages` | A speech bubble (the game's NPC text) above the actor, 120 characters, 5 a second. Seen on the host's own screen; the bubble's length is the game's |
| `player.banner(text)` | `messages` | The big centre banner (the new-biome one) on that player's screen; another player's game gets the centre message instead. 1 every 3 seconds per player |
| `player.setStamina(n)` | `combat` | 0 to max; only for a player this game owns |
| `actor.applyEffect(name, seconds)` | `combat` | A status effect from `Lists.StatusEffects` (`Rested Resting Wet Cold Freezing Frost Burning Poison Lightning Shelter Warm Smoked Tared Spirit`, each with its own longest time), 5 s up to that. The name must also exist in this game (else a refusal). Only on what this game owns. 20 a minute per script (setting) |
| `actor.removeEffect(name)` | `combat` | |
| `actor.launch(dir, force)` | `combat` | A shove: `dir` a vector, `force` 0 to 15 m/s (adds to the body's velocity). Not on bosses; only on what this game owns; 2 a second |
| `creature.tame()` | `spawn` | Only a creature this script spawned |
| `creature.remove()` | `spawn` | Only own spawned; no loot |
| `creature.setName(text)` | `messages` | Only own spawned; 24 characters; the hover name on this game |

### 12.4 Sights, sounds, banners, pings

| Call | Power | Notes |
|---|---|---|
| `effect(name, pos, opts?)` | `effects` | `name` on `Lists.VisualEffects` (lightning, fireworks, summon rings, spawn puffs, mead splashes...). The prefab is **also checked itself**: only particles, lights, audio, camera-shake parts, timed destroy, the network view and its sync parts; anything else is refused by name (the log says which part). `opts.scale` 0.5 to 3. Within 100 m of a player. 10 a second per script (setting) |
| `sound(name, pos)` | `effects` | Same for `Lists.Sounds` (`sfx_...`) |
| `lightning(pos)` | `effects` | `fx_Lightning` (flash and thunder), no damage |
| `shake(pos, range, strength)` | `effects` | Camera shake on this game for players within `range` (1 to 60 m), strength 0 to 3; 2 a second |
| `ping(pos, text?)` | `messages` | The map ping everyone sees; 1 every 3 seconds |
| `banner(text)` | `messages` | The centre banner for everyone; 1 every 3 seconds |

Sights that come from a prefab with a network view (most do) reach other players' games; the camera shake and the speech bubble show on the
host's own game.

### 12.5 The world

| Call | Power | Notes |
|---|---|---|
| `world.setTime(t)` | `weather` | **Now built.** `t` 0 to 1 (0 midnight, 0.5 noon). Always moves forward to the next such time. Only the game that runs the world. Once a minute |
| `world.setWind(degrees, strength, seconds)` | `weather` | `strength` 0 to 1, `seconds` 10 to 600, then the game's own wind returns. Once in 10 seconds |
| `world.startEvent(name)` | `spawn` | One of the game's random events (raids), from the setting `Allowed / Random events a script may start` (default `army_eikthyr`, `army_theelder`, `foresttrolls`), at the first player's place. Once in 2 minutes. The log lists this game's event names the first time |
| `world.stopEvent()` | `spawn` | Ends the current event |
| `world.spawnPiece(prefab, pos, rot?)` | `world` | **Now built.** A decoration from `Lists.Pieces` (banners, ground torches, maypole, jack-o-turnip, christmas pieces, chairs, benches, tables). The prefab is checked: nothing that holds items, crafts, sleeps, teleports, opens, burns or hurts (`Aoe`, `Container`, `CraftingStation`, `Bed`, `Door`, `PrivateArea`...). `rot` is euler degrees `{x,y,z}`. Within 100 m of a player, not inside a ward, not on another piece; the height snaps to the ground if it is within 3 m. 20 a minute and 200 alive per script (settings). Persistent. Returns a Piece |
| `net.send(endpoint, text)` | `net` | **Now built.** `endpoint` is a name from the setting `Allowed / Web addresses scripts may send to` (`name=https://...;name2=...`, https only, no redirects). The body is JSON `{"text", "script", "world"}`; nothing is read back; 10 s timeout; 2000 characters; once in 10 seconds |

### 12.6 Piece handles

`id` (this session only), `prefab`, `valid`, `pos`, `rot`, `builtBy` (`"p<id>"` or `null`), `spawnedBy` (the script's name or `null`),
`distanceTo(other)`, `remove()` (`world`; only a piece this script placed). `findById(id)` now finds pieces too.

### 12.7 Events added

Listed in `docs/scripting-expansion.md` section 4. Each is hooked separately; one that cannot be hooked in a given game version says so
at `on(...)` and in the log, and the others work.

| Event | `e` | Notes |
|---|---|---|
| `hit` | `attacker`, `target`, `damage` (writable), `pos`, `weapon` | Before damage, only on the game that owns the target (so a player hit on another player's own game is not seen by a host's scripts). The handler runs inside the game's own damage call with a 0.2 ms budget. A write needs `combat` approved and is clamped: creature 0 to 3x, player 0 to 1.5x and never to or above current health. `weapon` is what the attacker holds |
| `pieceBuilt` | `player`, `piece` | After a placement (the game sets the creator) |
| `itemPickup` | `player`, `item` | A pickup from the ground |
| `eat` | `player`, `item` | |
| `skillUp` | `player`, `skill`, `level` | |
| `raidStart` / `raidEnd` | `name` | |
| `weatherChange` | `from`, `to` | Watched once a second |
| `playerRespawn` | `player` | |
| `command` | `player`, `name`, `args` | Also fired, to the script that registered `command(name, fn)`, when the player types `/name args` (in addition to calling `fn`) |
| `tamed` | `creature`, `player` | `player` is the nearest one within 30 m |

### 12.8 Settings and lists (data, not code)

Settings (BepInEx `com.quad.blockscript.cfg`): `Limits`: `Spawns per minute per script` (5), `Creatures alive per script` (20), `Effects and sounds per
second per script` (10), `Pieces placed per minute per script` (20), `Pieces alive per script` (200), `Status effects per minute per script`
(20); `Allowed`: `Random events a script may start`, `Web addresses scripts may send to`. Lists in `Lists.cs`: `Allow` (items, widened to the
Meadows, Black Forest and Swamp food and materials), `StatusEffects`, `VisualEffects`, `Sounds`, `Pieces`. The editor's Limits panel
(`Limits.Lines`) describes each of these for the powers a script uses.

## 13. Multiplayer (built 2026-10-12; design in `docs/scripting-multiplayer.md`)

Scripts run on the game that runs the world (single player, the host of a listen server, or a dedicated server). A player on **another**
game is reached by asking their game, over the direct connection, to do it. That needs **Quad's Blockscript on that player's game**; without it
the action is refused with a reason that names the player ("<name>'s game does not have Quad's Blockscript, so this cannot reach them"), while
everything vanilla (creatures, time, the plain messages) still reaches them. Single player and the host's own character work as before.
**Status: built and unit-tested without the game (frames, limits, the player's checks, the server's bookkeeping, a loopback between a fake
server and a fake player); not yet run between two real games.**

What a player's game checks again before it obeys (so a server that is not yours cannot hand a player more than the mod would): the item
and effect lists, the amounts, "never the last point of health", the ground for a teleport, a shove's force. And the player's own settings
(BepInEx config, section "My character on other servers"): `Servers may give me items` (on), `heal and hurt me` (on), `give me
status effects` (on), `change what I see` (on), `take items from me` (**off**), `teleport me` (**off**), `shove me` (**off**). An action the
player turned off is refused with "the player does not allow it", in the script's log.

**Actions on a player on another game** (what the script gets back at once; the real result arrives in a moment and is logged as a `did:`
line or a refusal):

| Call | Returns at once | Notes |
|---|---|---|
| `player.give(item, n)` | the amount asked after the caps | The player's own bag decides how many fit; the hourly count is reserved at once and settled from the answer (what did not go in is given back). The log says what really went in. |
| `player.take(item, n)` | the amount asked, cut to what their last report says they have | Counted only once their game says what it took. Off unless the player allows it. |
| `heal`, `damage` | the amount asked after the caps | `damage` never takes the last point and is never over 50. |
| `setStamina`, `applyEffect`, `removeEffect` | `true` when sent | |
| `launch(dir, force)` | `true` when sent | Off unless the player allows it. |
| `teleport(pos)` | `true` when sent | Their game lands them on the ground at that place and never uses the height given. Off unless the player allows it. A place their game has not loaded is a refusal. |
| `player.banner(text)` | `true` | Their game shows the big banner; a game without the mod gets the plain centre message. |
| `say`, `broadcast` | as before | The game's own message, which reaches everyone. |

**Reads of a player on another game** come from a report their game sends every two seconds when something changed (`inventory()`,
`equipped()`, `skill()`, `count()`, `has()`, `stamina`, `maxStamina`, `isSwimming`, `isSitting`, `isRunning`, `isBlocking`, `deaths`,
`lookingAt`): up to a few seconds old. Before the first report arrives, the bag reads as empty (one note in the game's log). Health, place,
name, level and facing are read live from the world as for anyone. A player without the mod never reports: those reads are empty.

**Creatures** another game owns (most of the creatures near a player belong to that player's game): `heal`, `damage`, `applyEffect`,
`removeEffect` and `launch` first take the creature over and act on the next frame; the call returns the amount asked, and if the creature
does not become this game's within a second it is refused as "busy". `tame`, `remove` and `setName` work on a creature this script spawned.

**World things everyone sees**: weather (`world.setWeather`), wind, `effect`, `sound`, `lightning`, `shake`, `banner`, `talk` and
`setName` are also sent to every game that has the mod (each applies it if its player allows, an effect only after the same check of what
the prefab carries). `ping` goes as the game's own map ping to everyone. A game without the mod sees the creatures, the time and the plain
messages and not the effects, banners, bubbles, forced weather or camera shake. `world.setTime`, `startEvent`, `stopEvent`, `spawn`,
`dropItem`, `spawnPiece` and `net.send` need nothing from the players' games.

**Events**. The server sees these itself on every game: `playerJoin`, `playerLeave`, `chat` (and a `/command` typed by any player),
`bossSpawn`, `dayStart`, `nightStart`, `weatherChange`, `raidStart`, `raidEnd`. A player's game tells the server about what only it can see,
and only when a running script listens: `kill` and `bossDeath` (of what that game owns, which is most creatures near the player),
`playerDeath`, `playerRespawn`, `itemPickup`, `eat`, `skillUp`, `pieceBuilt`, `tamed` and `hit`. The server checks each one: the thing it is
about must belong to that connection, no more than 50 a second, and the same kill told twice is one event. `hit` forwarded from a player's
game is **read-only** (the damage was settled on the game that owns the target, so a script's change to `e.damage` is ignored); a hit on
something the server owns is writable as before. A kill that the server's own game and a player's game both see is fired once.

**Limits and lists** are the server's: a player's game gets the server's numbers and lists when it joins (and again when they change),
the editor's Limits panel can show them (`Limits.FromServer` says so), and the player's own checks use them, with one rule: a server can only
*add* to what must never be given or taken. Leaving the server puts the player's own lists and settings back.

**On a dedicated server** there is no screen and no player of its own: scripts run, events (including chat) are watched, effects
reach the players' games; nothing is shown on the server itself.
