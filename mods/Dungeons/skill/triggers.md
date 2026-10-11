# Triggers, actions, building blocks, key items and checkpoints

A second layer on top of mechanisms and wires: a room's **triggers** watch for **events** and run **actions**, and seven **building blocks**
(pedestals, braziers, push blocks, traps, teleporters, locks, checkpoints) add new kinds of mechanism. The code: `PackReaderTriggers.cs`
(the format, in its top comment: the truth if this file disagrees), `TriggerModel.cs`, `TriggerSet.cs`, `TriggerState.cs`,
`TriggerSolve.cs` (what the solver can check), `BlockSet.cs`, `BlockUse.cs`, `BlockKeys.cs`, `Checkpoints.cs`, `TriggerTools.cs` (the
commands). A worked pack: `packs/testblocks.json`, made by `tools/dungeons/testblocks.py`.

**No theme is built in**: every block's look is the pack's `parts`, every sound a game prefab the pack names.

## Triggers

On a room: `"triggers": [ { "id", "on", "when", "within", "if", "do", "repeat", "group", "solver" } ]`.

| Field | What it does |
|---|---|
| `id` | Unique in the room (default `t0`, `t1`...). |
| `on` | One event or a list. Any one fires it, unless `when` is `sequence`. |
| `when` | `all` (default: every `if` holds), `any` (one `if` holds), `sequence` (the `on` events, all of them, in this order). |
| `within` | A sequence: seconds from its first event to its last (0: no limit). An event out of order starts it again (the sequence's first event out of order starts it at 1). |
| `if` | Conditions: `{"mech": "gate1", "state": 1}`, `{"mech": "x", "not": 0}`, `{"counter": "c", "atLeast": 3, "atMost": 9, "equals": 4}`. |
| `do` | The actions. |
| `repeat` | `true`: fires every time. Default: once, until the dungeon resets or its group is reset. |
| `group` | `{"reset": "g"}` puts it back (fired flag, sequence progress, kill counts). Any mechanism can have a `group` too. |
| `solver` | `"ignore"`: the solver leaves it out, and the layout must be solvable without it. For decoration, optional rewards, anything the solver can't model. |

**Events** (`{"event": "<kind>", ...}`):

| Event | Fires when |
|---|---|
| `enter` / `leave` `{"at", "size", "from"}` | A box in the room's frame (none: the whole room) becomes occupied / empty by living players. `from`: entered from that doorway. |
| `state` `{"mech", "state"}` | A mechanism goes to that state (any change without `state`). |
| `lever` `{"mech"}` | A lever is pulled (wires or not). |
| `press` / `release` `{"mech"}` | A plate. |
| `kills` `{"at", "size", "count"}` | That many creatures died in the area. |
| `place` / `take` `{"mech", "item"}` | An item put on / taken off a pedestal (`item` optional). |
| `timer` `{"name"}` or `{"every": 30}` | A named timer rang, or every 30 s of the server's clock. |
| `lit` / `out` `{"mech"}` | A brazier. |
| `broken` `{"mech"}` | A breakable broke. |
| `cleared` `{"mech"}` | An encounter was won. |
| `signal` `{"name"}` | Another trigger's signal. |
| `counter` `{"name"}` | A counter changed. |

**Actions** (each an object with one verb key):

| Action | Does |
|---|---|
| `{"set": "gate1", "to": 1}` / `{"next": "stair1"}` | A mechanism to a state / one state on. |
| `{"start": "enc1"}` | Starts an encounter. |
| `{"spawn": "melee", "count": 2, "at": [[x,y,z], ...], "level": 1}` | The land's creatures of that role (melee, ranged, flying, elite), fitted by the builder, level capped to the land. |
| `{"sound": "<game sfx/vfx prefab>", "at": [x,y,z]}` | Every player near hears it. |
| `{"message": "$key or text", "to": "room" \| "dungeon"}` | To the players in the room, or everyone inside. |
| `{"music": "<game music>"}` | `""` gives it back. A fight's music still wins. `dungeon music` lists the names. |
| `{"counter": "c", "set": 0}` / `{"counter": "c", "add": 1}` | Counters. |
| `{"reset": "g"}` | The group's triggers, and the mechanisms with that `group`, back to their start; a pedestal in it drops its item. |
| `{"timer": "t", "after": 30}` / `{"timer": "t", "every": 5}` / `{"timer": "t", "stop": true}` | Once, repeating, stopped. |
| `{"signal": "s"}` | Fires `signal` events. |
| `{"item": "<key item or game item>", "count": 1, "at": [x,y,z]}` | Dropped there. A key item is bound to the dungeon and a reset takes it away. |

**Names.** Mechanisms are named as wires name them (an id in the same room, or `step:<step id>.<mech id>`). Counters, timers, signals and
groups belong to the room; `@name` makes one the whole dungeon's.

**How it runs.** On the entrance's owner only. Saved in the entrance's ZDO (`quaddungeon_trig`: fired flags, sequences, counters, timers,
kills, pedestal contents, music), so it's the same for every player and survives relogs and a change of owner. Cleared by a reset. Actions
that change mechanisms are applied together, after the trigger's other actions; the signals and counter events it causes fire after it
(at most 8 deep).

## Building blocks (mechanism kinds)

Any mechanism's part may have `"show": [states]` (it appears only in those states), and any block `"fx": [game prefabs]` played when it acts.

| Kind | Fields | What it is |
|---|---|---|
| `pedestal` | `item` (a key item or a game item), `accepts` [...], `itemAt` | States: 0 empty, 1 its item, 2 another it accepts. E puts its item on (or a hotbar key it accepts); **hold E** takes it off (dropped at its foot, +z). Only an item as found (quality 1, no wear, no custom data but a key's binding): nothing of value can be lost. The player's game holds the item until the owner confirms, and gives it back if there's no answer in 6 s. A reset drops what's on it at its foot; a dungeon removed for good drops it outside the entrance. |
| `brazier` | `burn` (s), `lightable` | States: 0 out, 1 lit. Lit by E with a torch (or any fire-damage item) in hand, a fire hit, or an action; goes out `burn` s after it was lit (0: stays lit). Typical look: an unlit torch part with `"show": [0]`, a lit one with `"show": [1]`. |
| `pushblock` | `cell` (m), `rows` | The grid seen from above (+z up, the mechanism's `at` its middle): `.` floor, `#` wall, `S` the start, ` ` (a space) floor for players only (the block can't go there). Off the grid counts as wall. States: the floor cells in reading order (row 0 first). A player walking into a face for a moment pushes it one cell, if the cell ahead is floor and free of other push blocks, and only from a `.` or ` ` cell behind it: give the grid a ring of ` ` so players can walk round it (`testblocks.json`'s pushroom: `["     ", " S.. ", " .#. ", " ... ", "     "]`). Give it a `group` and a trigger that resets the group (a lever), or the reader says it can get stuck. |
| `trap` | `damage` (pierce, blunt, slash, fire, frost, lightning, poison, spirit), `scale`, `every`, `for`, `offset`, `area`, `areaAt`, `poses` [rest, fired] | States: 0 off, 1 on. On, it fires `for` s every `every` s of the server's clock (default on); `every` 0: once each time an action sets it to 1 (default off). The mechanism stands at its rest pose (the first) in both states; its parts move from there to the fired pose on its own clock. Damage by the land: Meadows 8, Black Forest 15, Swamp 25, Mountain 35, Plains 50, Mistlands 65, Ashlands 80, Deep North 95, times `scale`, and never more than 34% of the victim's health up to the Black Forest, 50% to the Mountain, 70% after (never a one-shot kill at low tiers). Each player is hurt once per firing; creatures too. No `area`: its parts' box plus 1.2 m above. |
| `teleporter` | `pair`, `send`, `step` | States: 0 off, 1 on (default on; `"start": 0` to switch it on with an action). E (or stepping on it with `"step": true`) takes the player to 1.5 m in front (+z) of the pair's other end, while this end is on. `"send": false`: arrival only (a one-way pair). Two ends per pair. |
| `lock` | `doorway`, `key`, `parts`, `poses` [shut, open] | A gate a key opens (closes its doorway in state 0). E with one of the dungeon's keys asks the owner; the key is taken only once it's open. |
| `checkpoint` | none | E makes it the player's own: dying inside, they come back 1.5 m in front of it instead of at their bed (held until the dungeon is built there). Per player (their character's data), cleared when they leave by the way out or the dungeon resets. The gravestone stays where they died. A part with `"show": [1]` shows only on the game of the player whose checkpoint it is. |

## Key items

On the pack: `"items": {"smallkey": {"look": "CryptKey", "name": "$dungeons_key_small", "description": "$dungeons_key_small_desc"}}`
makes the prefab `quaddungeon_key_<pack>_<name>`, a copy of the game item `look`, weightless and worth nothing. Built-in words:
`$dungeons_key_small(_desc)`, `$dungeons_key_boss(_desc)`.

A key is bound to its dungeon as it is now: taken from a player as soon as they're outside it, worthless after a reset, and a lock only takes
its own dungeon's. Give keys with the action `{"item": "smallkey"}`: **the solver only counts keys given that way**, not chest loot.

## What the solver can check (TriggerSolve.cs)

A pack it can't model gets a pack problem, so the harness fails, unless the trigger has `"solver": "ignore"`.

- A lever, a plate, a pedestal's place or take, a brazier lit by a player: its actions happen with that use.
- `enter`, `leave`, `kills`: free once the room (or the `from` doorway) is reached.
- A `state`, an `out`, a lit-by-action, an every-N timer: applied whenever its state holds, so only `set` there (a `next` in such a trigger
  is reported).
- A `sequence`: one use, only when all its events are things a player does in its room; `within` must give at least 3 s per event.
- `any`: one rule per condition. Signals and named timers are followed (their actions as if at once).
- Locks need a key given by a trigger before them; push blocks count as in any cell they can reach; teleporters as one-way links while
  their sending end is on; braziers that burn out as relightable.

**Reported** (it can't model them): counter conditions or counter events; a `state` event without `state`; `next` in a trigger that follows
a state or a timer; a sequence with events outside its room or not done by players; dungeon-wide (`@`) signals; a signal or timer going to
a trigger with conditions; a push block that can get stuck with no reset; a place or take event naming an item that isn't the pedestal's
own.

## Commands

| Command | Does |
|---|---|
| `dungeon triggers [id]` | Each trigger: events, conditions and whether they hold now, actions, fired, sequence progress; counters, timers, music. |
| `dungeon fire <trigger key> [id]` | Runs its actions now (a cheat). |
| `dungeon fire <event> <what> [id]` | Fakes an event: `lever`, `press`, `release`, `lit`, `out`, `broken`, `cleared` `<mech key>`; `state <key>=<n>`; `place` / `take <key>[=<item>]`; `signal`, `timer`, `counter <room>.<name>` or `@name`; `kills <count>` at the player. |
| `dungeon blocks [id]` | Pedestals' items, braziers, push block cells, traps on or firing, teleporters, locks, checkpoints, key items. |
| `dungeon checkpoint [clear]` | This player's checkpoint, or clears it. |

## Pitfalls

- A room with a gate on its way out should have `"joinBy": ["in"]`, so it's always entered from the other side.
- A sequence of braziers needs a way to put them out (a reset lever), or a wrong order is stuck for good.
- A trigger without `repeat` fires once per reset.
- `next` from a trigger that follows a state can loop.
- Pedestals only take items as found.
- A key dropped outside its dungeon vanishes.
- Trap damage is capped per hit, but repeated hits still kill.
- Music from a trigger lasts until a trigger gives it back with `""`.
- Teleporter pairs are matched by `pair` across the whole dungeon (the first two ends): a room with one end goes in the layout at most once
  (`"max": 1`), or put both ends in one room.
- `dungeon_parts.py` always writes `start`: set `start` 1 on traps with `every`, and on teleporters that should be on.

The harness checks packs with triggers too (each solution replayed with them): all four packs, `testblocks` included, pass 1000 seeds.
