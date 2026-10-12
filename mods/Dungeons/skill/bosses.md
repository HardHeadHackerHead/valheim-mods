# Bosses: the boss toolkit

> Played in the game: the Cog-Thrall's fight with its hammer clamp, the Warden's fight, its shield phase and the trophy.

A boss fight is an encounter with a `boss` (pack-format.md, "Encounters"). The toolkit makes it a set piece rather than a creature
with more health: an intro, telegraphed special attacks, phases that change the room, shields the players break by using the room,
mechanisms that hurt the boss, enrage, a look of its own, and a death moment. It's all pack data, and any pack can use it.
The code: `BossModel.cs` (the data), `PackReaderBoss.cs` (reading it and its problems; its top comment is the format), `BossFight.cs`
(the owner decides), `BossShow.cs` (what every game sees and hears), `BossBody.cs` (the boss creature: its look, its guard, being held
still, leaps), `BossMarks.cs` (the telegraphs). Worked examples: Shifting Halls' Warden and guardian (`tools/dungeons/boss_rooms.py`).

## What the players asked for

After Shifting Halls (Meadows), the user said: "The boss was really lame. First off the boss was spawned on this pedestal area and it looked like
he couldn't actually get down... it needs to be just so much more epic and like an epic battle. Most of our dungeons should be epic
boss battles." The mini boss "also felt a little lame." So every boss needs an intro, mechanics that use the room, telegraphed
attacks and a death moment, and it must feel big even in the Meadows (stats still fit the land).

## Rule one: the boss must be able to reach the players

Valheim's creatures walk only where the game's navmesh goes. The game builds it at runtime round them (`Pathfinding`, 32 m tiles, one
per agent type). Its **step height is 0.3 m** for humanoid agents (skeletons, draugr, greydwarfs, goblins, fenrings) and 0.6 m for
troll-sized ones. They never jump down a ledge. The first Warden spawned on a dais of two 0.6 m tiers: the dais top was an island, and
the boss walked to its edge and stood there.

- **Step every raised place a boss or elite starts on** down to the floor in 0.2 m steps (`boss_rooms.step_dais`: a ziggurat, 0.2 m
  rise, 0.5 m tread, on all four sides).
- **`creature_check.py`** (a gate step) walks a creature model (0.3 m steps, no jumps, a body 0.5 m round) from every encounter
  spawn point to the floor of the doorway the fight is entered from. It checks the start state, the states the fight sets, and every
  gate and platform open. A boss point must pass in all of them; a wave's point in one. "is on an island ... an edge of 0.6 m at (x, y, z)" names the
  edge to fix.
- At runtime the boss also **leaps** to the players when it hasn't moved 1 m in `unstick` seconds while away from all of them
  (default 5; 0 turns it off).

## The fields

All under the encounter's `"boss"`. Today's fields (`name`, `role`, `health`, `phases` with `at`/`wires`/`adds`) work as before.

| Field | Default | What it does |
|---|---|---|
| `name`, `title` | | Its name (the game's boss bar and the banner) and the line under it on the banner ($ keys). |
| `role` | `elite` | The creature: the pack's `elite` list for the land (else a melee pick), never above the land's tier, at the land's level cap. |
| `health` | 4 | It takes 1/this of each hit (so this many times its health). Ignored when `fight` is set. |
| `fight` | | Seconds (10 to 900) one player of the land's tier, hitting it all the time, needs to kill it: sets its health. Expected damage a second by land: Meadows 9, Black Forest 14, Swamp 20, Mountain 28, Plains 38, Mistlands 50, Ashlands 65, Deep North 80. Shifting Halls: the Warden 200, the Thrall 45 (with `armour` 0.25). |
| `perPlayer` | 0.5 | More health per extra player inside when it wakes (+50% each). |
| `scale` | 1 | Its size on every game (0.5 to 3). 1.4-1.8 reads as a boss without breaking its attacks' reach. |
| `mini` | false | A mini boss: the banner without the game's stinger. |
| `armour` | 1 | The share of each hit it takes when no shield is up (0.25: armoured). Needs a `weakTo` that `breaks` it. |
| `armourOff` | 10 | Seconds its armour stays off after a weak hit. |
| `unstick` | 5 | See rule one. |
| `trophy` | the Warden's | The item it drops where it falls: default `quaddungeon_trophy_warden`; a mini boss drops none; `""` none; any registered item name works. A room tagged `boss` also gets a boss chest with a trophy: pick one or the other. |
| `look` | | `{"light": [r,g,b], "range": 6, "aura": "vfx_Burning_blue", "enraged": [r,g,b]}`: a point light on it (and its colour once enraged) and an aura (a game effect prefab: only its particles, lights and sound are kept, looping). Good auras: `vfx_Burning_blue`, `vfx_Burning`, `vfx_Smoked`, `vfx_Frost`, `vfx_Poison`. |
| `intro` | none | `{"seconds", "wake", "leap": [x,y,z], "leapAt", "beats": [...]}`: the fight starts when it always did (the locks shut), the beats play, the boss appears at `wake`, leaps to `leap` (room frame, a floor) at `leapAt`, and can't be hurt and doesn't move until `seconds` (30 at most). The music starts at `wake`. No intro: it comes at once, as before. |
| `death` | none | `{"seconds", "beats": [...]}`: beats from its fall; after `seconds` the trophy drops and the fight is cleared (its wires: the gates open; the vault moment and each player's vault roll). |
| `abilities` | none | Telegraphed special attacks (below). |
| `weakTo` | none | Mechanisms that hurt it (below). |
| `phases` | none | `{"at", "wires", "adds", "use", "leap", "enrage", "banner", "message", "sound", "shake", "shield"}` (below). |

**A beat**: `{"at": 1.5, "wires": [...], "sound": "<game sfx/vfx>", "fx": "<another>", "where": [x,y,z], "shake": 0.4, "banner": true,
"message": "$key"}`. The owner fires its wires (`next` or `set:n` on the room's mechanisms) when its time comes. Every game near plays
the sound and effect at `where` (room frame; none: at the boss, or its point before it wakes) and shakes the camera (0 to 1).
`banner` shows the game's big land-found banner with the boss's name and title (intro) or "{name} has fallen" (death). `message` is a
centre message (`{0}` is the boss's name). The banner and messages go to the players in the fight's area (the death's to everyone inside).

**Lights that die and come back**: one mechanism (a `platform` with identical poses, one per state, `seconds` 0, `start` its last
state), its torches as parts with `"lit": [states]` (triggers.md: one model, only the flame, light and crackle come and go). The
Warden's: state 0 dark, the door's torches `lit [1,2,3]`, the middle's `[2,3]`, the dais's `[3]`; beats `set:0`, then `set:1`, `set:2`,
`set:3`. Never an `_unlit` prefab swapped for a lit one with `show` (the reader refuses it).

**Abilities**: `{"id", "kind", "every", "first", "warn", "phases": [0, 2], ...}`, one at a time, on a cooldown (`every` seconds, a
little more or less each time; the first `first` seconds after it becomes usable). A red ring from the game's ward circle shows on the
floor (a lane for a charge) and a second ring fills it during `warn` seconds; then it lands. Damage is fitted to the land like traps':
Meadows 8, Black Forest 15, Swamp 25, Mountain 35, Plains 50, Mistlands 65, Ashlands 80, Deep North 95, times `scale`, and never more
than a share of the player's health (a third up to the Black Forest, half to the Mountains, 70% after). `damage` sets the type (blunt,
slash, pierce, fire, frost, lightning, poison, spirit) and `push` the knock-back. A dodge-roll avoids it, a block takes it, and a parry
staggers the boss. `sound` and `fx` replace the defaults played as it lands. **Every telegraph has a sound** as its marks appear:
`cue` (default: slam and charge a roar, `sfx_gdking_alert`; rain `sfx_rock_hit` at each ring; summon `sfx_prespawn`; wires none:
give it one, or let the mechanisms' own sounds do).

| Kind | Fields | What it does |
|---|---|---|
| `slam` | `radius` | A ring round the boss; it stands still winding up; then a stomp (`fx_eikthyr_stomp`). Only cast when a player is near. |
| `rain` | `radius`, `count` | A ring under each player in the fight (and `count` more round the boss); then falling stone. |
| `charge` | `width`, `length` | A lane from the boss towards a player (stopping short of walls); then it dashes down it, hurting whoever is in its way. |
| `summon` | `adds`, `points` | A pre-spawn glow at the points for `warn` seconds; then the adds come (by role, fitted to the land). No points: the fight's points but its first. |
| `wires` | `wires` | Fires the room's mechanisms as it lands (hammers, traps). |

`phases`: the phases it's used in (0 is before the first phase, 1 after the first, ...). Without it, the phases' `use` lists decide
(the latest phase with a `use` up to now), else every phase. Slams and charges wait while a shield holds it still.

**Phases**, as its health falls past `at` (in falling order): `wires` and `adds` as before (adds come at the fight's points but its
first, not on its dais); `use` switches abilities; `leap` sends it to a point (a dais to channel on); `enrage` (1 to 2) makes it
faster and hit harder from now on (its light turns `enraged`); `banner`, `message` (`{0}`: its name), `sound`, `shake` for the moment;
`healTo` (above `at`, at most 1): as the phase starts the boss recovers to that share of its health, and everyone sees a surge (its light
flares, the game's health-upgrade effect, a guard stone's activation, a heal sound, "{name} recovers its strength!").

**A shield** (on a phase): `{"take": 0, "until": [{"mech": "brazierL", "state": 1}, ...], "seconds": 60, "hold": true,
"breakShare": 0.08, "breakWires": [...], "hint": "$key"}`. While it's up the boss takes `take` of each hit (0: none; hits clang off,
"Immune", with the protection staff's bubble on it). With `hold` it stands still, channelling. The mechanisms in `until` are put back to their **start**
state as it comes up and on each flicker (a push block to its `S` cell, a brazier out; the reader refuses an `until` state equal to the
mechanism's start), and it breaks when every condition holds at once, when a `weakTo` that `breaks` lands, or after
`seconds` (never stuck). With `"times": n` (1 to 5) its conditions must hold that many times: each time but the last the shield
flickers (a sound, and the message `flicker`: a $ key, `{0}` the boss's name; default "The shield of {0} still holds: do it again!";
the Warden's is `$dungeons_boss_warden_flicker`) and the mechanisms in `until` are put back. Then it staggers, takes `breakShare` of its health, and
`breakWires` fire. `hint` is the message the
players in the fight get as it comes up (default: "{0} is shielded: find what in the room breaks it!").

**weakTo**: `{"mech": "hammer", "state": 1, "area": [6, 4, 6], "areaAt": [0, 1, 0], "share": 0.12, "stagger": true, "breaks": true,
"hurt": 2}`. When that mechanism arrives at `state` (a hammer coming down) with the boss inside `area` (the mechanism's own frame, round
`areaAt`), the boss takes `share` of its whole health (through its shield and armour), staggers, and with `breaks` its shield
breaks and its armour comes off for `armourOff` seconds. Players inside take `hurt` times the land's trap damage. A lever with a `next`
wire drops it and raises it again. While it falls, every game draws a red ring where it lands (with the mechanism's own sound).
Give it `seconds` of 1 or more so players can roll out. A platform with `cycle` and `cycleIn` keeps cycling while it's set into
`cycleIn`: for one blast, set it, then set it back with a named timer (triggers.md).

**The clamp**: when the mechanism starts down with the boss inside its area, the boss is held there until it lands (chains snap round
it, with an effect, a chain sound and the weakTo's `held` message: a $ key, `{0}` the boss's name; default "{0} is held in place: it's
coming down on it!"; the Thrall's is `$dungeons_boss_thrall_held`). The hit is judged at the landing, so a
well-timed pull always lands; a pull with the boss off the target drops on empty floor. While the mechanism moves, or is down with the
boss under it, the boss passes through its colliders (it can't be caught in it), and after a hit it's hopped out.

**Lure targets must be open**: a `weakTo` that `breaks` needs open floor within 1.5 m of its area all round (no rails, levers or props
for the boss to path about: the user "couldn't get him to actually find a path inside of it"), and the boss must be able to walk onto it
from the way in. `creature_check.py` checks it ("...the boss can't be lured onto it from every side"). The Thrall's rails now hang from
the ceiling, and its levers stand 6.8 m out.

## Designing a fight

- **An intro of 3 to 7 seconds**: the gates slam (the encounter's `lock`), something changes in the room (lights out, gears start), the
  boss appears with a burst (`vfx_spawn_large`) and a roar, the banner, then it comes down to the players (a leap). Keep the boss's
  point on its stepped dais anyway: the leap is for show.
- **One mechanic the room teaches**: the players find out how to hurt it by using the room (braziers, a hammer on a lever). Put a
  `hint` or an intro `message` on it, and mark where it happens (a dark inlay under the hammer).
- **The room supplies the means.** Never make a fight depend on what players carry: the Warden's braziers each have a lever that
  sends fire into them (a room trigger: `{"on": {"event": "lever", "mech": "brazierLlever"}, "do": [{"set": "brazierL", "to": 1}],
  "repeat": true}`; a lever with no wires of its own is never one-shot); a torch or a fire arrow is only a shortcut.
- **Phases change the room**: hammers wake, blocks rise for cover, the boss goes back to its dais and shields.
- **Telegraph everything that hurts**, 1.3 to 2 s, with a dodge-roll as the answer.
- **Small lands still get the show.** In the Meadows the Warden is a skeleton at 1.7 times its size, glowing blue, with six times its
  health (+50% per extra player), slams, falling stone, a shield and a charge. The damage stays fitted to the land.
- **The room's mechanic is a challenge, not a chore.** The user's verdict on the first version: "pull the levers... that just felt way
  too easy". Do it under a hazard's timing, guarded, or more than once (a shield's `times`), and keep its reward modest (a stagger and
  about 8%).
- **The second wind** (recommended). The user: "get him down to 30% health first, and then whenever he goes to the next phase, then he
  goes back up to 50 or 60%". A phase with a low `at` (0.3) and `healTo` (0.5-0.6), paired with `enrage` and a room change (blocks
  rising). Shifting Halls: the Thrall at 0.3 → 0.55; the Warden's last phase at 0.3 → 0.5.
- **Say the effect plainly first, flavour after**, in every phase, enrage and shield `message`, `banner` and `hint`. The user asked "What
  does it mean when it says it grinds faster?". Now: "The Cog-Thrall recovers and is enraged: faster and harder-hitting! (its gears grind
  faster)", and the shield hint "is shielded and can't be hurt: light both braziers together, twice!...".
- **The final boss is clearly the hardest fight in the dungeon.** The user: "he had almost the same if not less health than the mini
  boss". A pack rule enforces it (`BossReader.CheckPack`): every final boss (not `mini`) must take at least twice as long as every mini
  boss, or the pack has a problem and the harness and the gate fail. A boss's length is its `fight`, else `health` x 30 s; armour counts
  as x (1 + (1 - armour) x 1.4). Set `fight` for every boss.
- **Solo and together**: everything must be doable alone. The Thrall's hammer has a lever at each edge of its mark, pulled from on
  the mark (lure, pull, roll out during its 1.4 s fall). The Warden's braziers are 20 m apart and burn 30 s: a few seconds' run
  between their levers.

## Multiplayer and reloads

The entrance's owner decides everything and saves it in the entrance's ZDO (`quaddungeon_bossfight`): the intro's clock, the phase,
the shield, enrage, the cast in progress with its time and targets, a leap. Every game draws the telegraphs and plays the beats from
that record on the server's clock. Each game hurts only its own player when a blow lands (the trap rule). The game that owns the boss
creature carries out its leaps and dashes and holds it still. Players' part goes through the usual acts (levers, braziers). Nothing
lives only in memory, so a reload hands nothing over: the boss's look is found again by name. The solver counts the fight as won, and the
toolkit's wires (beats, shields, abilities) are in what it checks (`BossDef.AllWires`).

Each fight stamps its boss creature with the fight's start time. A boss left alive by a reset, a remake or an earlier fight is never
taken as a new fight's boss: the owner removes it when the fight starts, and a reset or a remake also clears the boss records. (Found
in a test: after `dungeon respawn same`, the old Thrall at 47% health became the new fight's boss.)

## A lesson from the code: scaling a hit

The game's `HitData.ApplyModifier` scales every damage type **except untyped damage** (`m_damage`). Scale a whole hit with
`hit.m_damage.Modify(x)` as well. The toolkit's own blows are untyped, so until the play-test found it, the shield break did 8% times
the boss's toughness: 48% of its health in one go.

## Checking a boss

1. `python tools/dungeons/creature_check.py packs/<pack>.json`: no islands.
2. The harness and `dungeon packs`: the reader's problems (unknown ability kinds, wires to mechanisms not in the room, a shield nothing
   breaks, an intro over 30 s, an armoured boss nothing breaks...).
3. In the game: `dungeon spawn`, walk to the boss room, step in. `dungeon set <room>.<encounter> 1` starts it, `0` puts it back,
   `2` clears it. `dungeon mechs` shows the encounter's state. `dungeon boss [id]` reports every boss fight (state, phase, shield, the
   ability being cast, each ability's next use, the boss's health, `effectiveHealth`, `soloSeconds` and owner); with the cheat rule, `dungeon boss go <room>` puts the
   player 2.5 m outside that room's way in, facing in (on a floor found by a ray), `dungeon boss hurt <percent> [room]` takes that
   share of the boss's whole health through its shield, and `dungeon boss cast <ability> [room]` makes that ability's next use now. The log says each step ("boss 12.warden: the intro begins", "it wakes", "casts slam",
   "its shield breaks", "it falls").
