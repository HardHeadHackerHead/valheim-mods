---
name: valheim-dungeons
description: "Build dungeons with Quad's Dungeons: a dungeon is a seed and a pack, laid out by our own builder 5000 m above an entrance. How the system works, the pack format (rooms, doorways, end caps, the plan, keys and locks, mechanisms, encounters, bosses, breakables, shortcuts, music), enemies and loot fitted to the land, the generators and checks in tools/dungeons, the in-game dungeon commands, and the mistakes that already cost players items or broke puzzles. Use before making or changing a pack, a room, a mechanism or an entrance look, or when a dungeon misbehaves in the game."
---

# Building dungeons with Quad's Dungeons

The mod is `mods/Dungeons` in the valheim-mods repo (`E:\valhiem`); its design is `docs/dungeons-design.md`. This skill is what we
know about making dungeons with it. **The code is the truth**: every file starts with a comment saying what it does and why, and the pack
format is kept at the top of `Packs.cs`. If this skill disagrees with the code, the code wins: fix the skill.

- **[new-dungeon.md](new-dungeon.md)**: the playbook for a new dungeon kind, from idea to passing the gate, and what the player cares
  about. Start here for a new kind.
- **[pack-format.md](pack-format.md)**: every field of a pack, its rooms, plan, mechanisms, encounters, enemies, loot and entrance looks.
- **[authoring.md](authoring.md)**: writing our own rooms with the generators (`dungeon_parts.py`, `giant_kit.py`), the checks in
  detail, the conventions, and the lessons from authoring rooms.
- **[bosses.md](bosses.md)**: the boss toolkit (intro, abilities and telegraphs, phases, shields, weak spots, death), rule one (a boss
  must reach the players), designing a fight, and checking it.
- **[triggers.md](triggers.md)**: triggers and actions, the building blocks (pedestal, brazier, push block, trap, teleporter, lock,
  checkpoint), key items, what the solver can check, and their commands.
- **[map.md](map.md)**: how the in-game map reads a pack (floors from doorway heights, stairs by name), and how to check it offline.
- This file: how the system works, the workflow, the tools, enemies and loot, and the lessons.

Read `docs/modding-pitfalls.md` too before changing the mod's code: it has the rules for saved objects, ownership and items.

## How it works

- **A dungeon is a seed and a pack.** The pack (`packs/<name>.json` built into the DLL, or a file in `BepInEx/config/Dungeons/packs`) says what a dungeon of that kind is made of; the
  seed picks one layout. The same pack and seed give the same dungeon on every machine: the layout code uses its own random generator
  (never `UnityEngine.Random`), quarter turns only, positions snapped to 1 cm, and no engine maths, so `tools/dungeons` runs the same code
  outside the game.
- **One saved object per dungeon: the entrance** (`Entrance.cs`), a networked prefab of ours, one per look. Its ZDO holds the pack name, the
  seed, our own id (`quaddungeon_id`), where the layout stands, which rooms' saved parts were made, the day it was first entered, the
  mechanisms' and encounters' states, the parts' generation and the layout's signature (`Keys` in `Entrance.cs`).
- **Built 5000 m above the entrance**, as the game's own interiors are (`Location.Awake`). Anything above 3000 m is "inside" to the game
  (`Character.InInterior`): no rain, no wind, no building. A dungeon that would overlap another goes up in 50 m steps.
- **The rooms are never saved.** Every player's game builds the shell (walls, floors, props, lights) locally, from the pack and the seed,
  as soon as the entrance exists on that game (its zone is loaded), and removes it when the entrance goes. A dungeon a player is inside
  stays built (Detached) until they leave, even if its entrance's zone unloads.
- **Saved parts** (chests, spawners, doors, and the game rooms' networked children) are made **once, by the entrance's owner**, tagged with
  the dungeon's id (`quaddungeon_partof`), the layout's signature and the generation. A part of another layout (the pack changed in an
  update) is removed when it loads.
- **Only the entrance's owner changes the state.** Players send `Dungeons_Act` (`use:<key>`, `entered`, `hit:<key>:<amount>`...) to the
  owner, who checks the sender (a lever's puller must stand near it). The sender is the RPC's, never something in the message.
- **Moving parts** take their position from their state and the server's clock (`ZNet.GetTimeSeconds`), so every player computes the same
  pose each frame; only changes of state are sent. Whoever stands on a moving part is carried with it (`Mechs.cs`: the game's own
  ground force needs a Rigidbody and doesn't lift, so we move the character ourselves, up, down and round).
- **Resets**: `ResetDays` in-game days (server setting, default 3; 0 never) after the dungeon was first entered, once nobody is inside,
  the owner clears the state: saved parts removed and made again, mechanisms back at their start, encounters idle. Creatures still
  alive and dropped items stay. **Chest contents** (`Rebuild.EmptyOut`): on a reset, a remake, a remove or a stale part, anything a
  player left in a dungeon chest goes on the ground in front of the entrance, outside, spread out; never where the chest stood. Loot the
  builder put there that nobody touched (the chest's `quaddungeon_lootsig` still matches its items) just goes: the dungeon makes new. With
  the entrance gone (a remove, an orphan), the contents go on the ground under the chest. A chest in use, or whose contents can't be read,
  is kept.
- **Safety** (`Safety.cs`): log out inside and the logout point is the entrance (also if the mod is removed meanwhile). A player above a
  dungeon not built yet is held still until it is, or taken out to its entrance. Saved objects above 3000 m aren't made on a game until a
  dungeon is built under them there. What falls out anyway goes back to its floor; a gravestone to the spot it was made. Our teleports
  count no fall.
- **Every player and the server need it**, at the same version (see "Packs from a folder" below).
- **Reload level `world`** (`HotReload.cs`; tested in the game): `reload Dungeons`, or the mod manager's auto-reload, works
  inside a world.
  - The entrance prefabs and items stay registered over the reload, and the new copy replaces them by name. Standing entrances get the new
    copy's component and look. Items in this game's bags, chests, on the ground and held by a pedestal are pointed at the new prefabs.
  - Each built dungeon is handed over as it stands (a "husk": the old copy's components off, sounds muted, floors kept), so nothing inside
    falls. The new copy lays it out and builds it again from the entrance's ZDO (state, encounters, triggers and map fog are saved, so
    nothing resets and nothing spawns twice), holds a player inside still ("hold on") until its build and mechanisms are complete, then
    removes the husk.
  - Carried over: the packs in use (a client's server set), the version check, Safety's hold and teleport grace, and bodies frozen for
    unbuilt dungeons. Until it settles (at least 10 s), keys aren't taken from players.
  - **Not kept:** a forced remake still waiting for players to leave (`reload force` or `respawn force` mid-wait: run it again); work a
    coroutine was doing (a pack watcher pass, a stray pass); a kill or act sent in the one frame between the copies.
  - A build whose packs changed: dungeons of a changed pack are laid out otherwise, so a player inside is taken out to the entrance first
    (as a remake), the old build goes, and the owner remakes the dungeon (`LayoutMatches`). A host whose own pack set changed with the
    build sends it again to its players.
  - The DLL declares `ClaudeTools.Reload` = `world`. Its items are registered through `ItemReload.cs` (a copy of Claude Tools' new
    `templates/ItemReload.cs`); Claude Tools' changed `modcheck` (not yet released) gives `world` only when every ObjectDB item add goes
    through ItemReload and the mod calls `ItemReload.Repoint`. The `modcheck` released so far still says `menu`: until Claude Tools is
    released, use `reload Dungeons force` in a world.
  - A client reloading in a world asks the server for its settings (`ResetDays`) again at once (in `HotReload.cs`).
  - If the hand-over fails anywhere, each part fails on its own: the error is logged ("Reload in a world: ... failed"), every built
    dungeon stays standing, the player stays held, and the new copy builds again from the entrances' ZDOs.
  - **When you change the mod's code:** anything kept only in memory that matters across a reload (an item taken from a player, a pending
    act) needs a Carry/Take in `HotReload.cs`; a component of ours on a **saved** object needs `HotReload.Reattach` (only
    `DungeonEntrance` has one). Local built objects are fine: they're built again. **Items** (rewards, packs' key items) are added only
    through `Builder.DefineItem` / `RegisterItems` → `ItemReload.Replace`: never add to ObjectDB any other way.
  - Why it's careful: lesson 29.

## Packs from a folder, the server's packs, and versions (SyncPacks.cs, SyncNet.cs)

- **Where:** `BepInEx/config/Dungeons/packs/<name>.json`, and looks in `BepInEx/config/Dungeons/packs/looks/<name>.json` (the mod makes the
  folders and a README.txt on first run). The same formats as the built-in ones. The file's name doesn't matter, the `name` inside does: a
  pack's is letters, digits, `_` and `-` (up to 48, matched without case); a look's lower case letters, digits and `_`. **A file whose
  `name` is a built-in one's replaces it** (a warning in the log): existing dungeons of that pack change (same seed, other rooms).
- **When:** read as a world starts (a dedicated server: only when it starts, so restart it to change its packs). A client can't reload:
  it uses the server's set.
- **The pack maker's loop** (single player or the host; cheats, so the cheat rule applies; `SyncReload.cs`, `Rebuild.cs`):
  1. Edit `BepInEx/config/Dungeons/packs/<name>.json` (looks in `packs/looks/`). Check it offline first if you can:
     `LayoutTest.exe <pack.json>` runs 1000 seeds with every check.
  2. `dungeon reload`: reads the folder again at once. Per pack and look: added, changed, unchanged, removed or left out (with why), with
     hashes; for each added or changed pack, its problems with this game's rooms and a 50-seed run (how many lay out, the main failure
     reasons with seeds, crashes, solution lengths, room counts). Connected players get the new set at once.
  3. Every dungeon of a changed pack is remade from the new pack: state back to new, the same seed, built again on every game (one not
     loaded is remade when it next loads). **Never under a player:** a dungeon someone is inside isn't remade (the answer names who and
     where); `dungeon reload force` takes them out to its entrance first, waits up to 15 s, then remakes it (also when the file hasn't
     changed).
  4. `dungeon respawn [same|new] [force] [id]`: remakes the nearest dungeon from scratch, with the same seed or (`new`) a new one that lays
     out. For retesting one layout quickly.
  5. Walk it again. A look change shows on entrances once their area loads again.
  - Optional: the setting `2 - Pack making / ReloadPacksOnSave` (off by default; single player or the host; not synced) reloads a second
    after a file in the folder is saved, never with force, and tells the host how many packs changed, how many dungeons were remade, and
    who blocked one.
- **What a remake does to things inside:** dropped items and gravestones go to the ground in front of the entrance, spread out (a
  gravestone's death spot moves with it); what's on pedestals is handed back outside; tames that followed someone in go outside; the
  dungeon's creatures and key items are removed and made again; its chests and other saved parts are removed (what players left in a chest
  goes outside the entrance first) and made again. Nothing is lost and nothing falls.
- **Multiplayer: only the server's (or host's) set counts.** When a player joins, the server sends its whole set (built in plus its config
  folder, gzipped, in 64 KB parts) unless the player's set is already the same (same hash), and the player uses exactly that set while
  connected. Players install no pack files; their own are ignored on that server and come back when they play alone or host. Until the
  server's set is in, a client has no packs at all, so nothing is ever laid out from its own. If it hasn't come 30 s after the server's
  version did (or none of it can be used), the player is told to reconnect and no dungeon can be built until they do, unless the
  server's set hash is the player's own (then their own set is used: it's the same one). Single player and the host never wait; a
  reconnect or leaving the world starts it all again.
- **A server without the mod (or with 0.1.0): dungeons are off for that player.** They can join, but no dungeon is laid out, spawned or
  entered there; a top-left message says why, and `dungeon sync` reports it. Why: such a server can't check anyone's version, its other
  players may have other packs or no mod, and it doesn't know our prefabs (the entrance, the items), so it deletes them. Only a server
  with the same Dungeons version decides the packs.
- **Checked on the server as it reads them and again on each player as they arrive.** Left out, with the reason in the log: not JSON, no
  valid `name`, a name already used, over 8 MB, or the pack reader throws on it. Anything else (missing game rooms, plan tags no room
  has) is kept and listed by `dungeon packs`; a pack that can't be laid out says why on its entrance's hover text. A look's problems
  (`LookCheck.cs`) are listed, never blocking.
- **Looks from a folder have no prefab of their own.** Only the built-in looks (`crypt`, `dvergr`, `giant`) are saved prefabs. A look from
  the config folder or the server is a name in the entrance's ZDO (`quaddungeon_look`) on the default entrance prefab, so removing the
  file never deletes entrances: they show the stand-in door until the look is back.
- **Versions:** the server and every player need the same Quad's Dungeons version. A player with another version, or without the mod (or
  with 0.1.0, which had no check), is refused when joining with the game's "incompatible version" message and a line naming both versions:
  on their game a dungeon's creatures, gravestones and dropped items would stand 5000 m up with no floor and fall out. A player with the
  mod on a server without it may join, with dungeons off (above).
- **`dungeon packs`** shows each pack and look with its source ("built in", "config (file.json)", "server (built in there)", "server
  (config there)") and hash, the whole set's hash, and what was left out. **`dungeon sync`** shows who decides here (own, the server's,
  waiting, refused), the hashes, the server's version, and on a server each player's version, what they were sent and recent refusals.

## Building: what the builder does with a pack (Build*.cs)

- Every player's game builds the rooms locally over several frames, about 3 ms of work a frame. A room is never split (a big game room can
  take ~5 ms). Our rooms' parts are spread over frames, and readable meshes are batched in chunks of 48. Hundreds of parts a room are
  fine, but **prefer one big scaled piece to many small ones**: most of the game's piece meshes aren't readable, so each part is a draw call.
- **The game's rooms** are built as `DungeonGenerator.PlaceRoom` builds them: the shell only, its random props (`RandomSpawn`,
  `RandomObject`) seeded from the room's place and the dungeon's seed, so every player sees the same. The owner makes their networked parts
  once, a room at a time, and records each room as done.
- **Our rooms**: each part becomes stripped scenery: no ZNetView, Piece, WearNTear, Rigidbody or joints (chains hang rigid), nothing usable
  or hoverable but chairs and ladders, nothing that gives items, no terrain changes, no "player base" or "private property" areas (so a
  fire doesn't stop spawners). Lights, fire, smoke and sound stay. `saved` parts are made once, by the owner, as ordinary saved objects.
- **Doors** go where the layout says, from the pack's `doors`, placed as the game's `PlaceDoors` does: at the joint, turned like the first
  room's doorway. Never in floor or ceiling doorways.
- **Lights**: the game fades lights beyond about 40 m (more when scaled), and players cap how many point lights show: a few strong lights
  beat dozens of small ones. Scaling a light grows its reach, not its brightness.
- **Solid colliders** (`BuildSolid.cs`): a scenery part's solid colliders on layers characters pass through (`item`, `effect`, `smoke`,
  `Ignore Raycast`) move to `static_solid`; an item prefab used as scenery gets a collider of its own shape (lesson 2). These colliders are
  the part's children, so they move with a mechanism and carrying finds them.

## Making or changing a dungeon: the workflow

1. **Look at what the game has.** In the game, `dungeon rooms [theme]` writes every DungeonDB room (size, doorways, types, networked
   parts) to `BepInEx/claude/dungeon-rooms.json`; the harness reads that file as its catalogue. Check every prefab name you mean to use
   exists **in ZNetScene** (`prefabs <text>` from Claude Tools, or `dungeon check` after building).
2. **Write a spec, not JSON**: for a new kind, follow new-dungeon.md (a spec, a theme, recipes, the gate). Shifting Halls is recipe-built: `specs/shiftinghalls.toml` → `make_dungeon.py`, installed by the gate (`make_pack.py specs/shiftinghalls.toml --install`). About 36 of its rooms come from recipes; its signature rooms are hand-made (`shifting_halls.py`: the entrance, rotunda, great hall, the three turning-stair halls, the vault and its caps, the secret rooms, the shortcut's doors, the end caps), and its boss rooms (the Warden's hall, the Cog-Thrall's guardian room) come from `boss_rooms.py`.
   Small test packs are still written by a script of their own (`testmechs.py`, `testblocks.py`).
3. **Check offline** (no game needed once `prefab_bounds.json` has your prefabs):
   - `python walk_check.py packs/<pack>.json [width]`: every pair of doorways a room says are joined can be walked.
   - `python prop_check.py packs/<pack>.json`: nothing floats; encounters' spawn points stand on floors.
   - `python lever_check.py packs/<pack>.json`: every lever and exit has room to stand and a clear aim.
   - The harness: `dotnet build tools/dungeons -c Release`, then `tools/dungeons/bin/Release/net48/LayoutTest.exe [pack.json] --seeds 1000`.
     Lays out many seeds with the mod's own code: failures and why, room counts, sizes, solution lengths, seeds that come out differently
     twice, a replay of each solution, and the gap check (`GapCheck.cs`) on every room whose mechanisms join doorways. `--map <seed>` prints a
     map with mechanisms and the solution (`--try K` for one try of it); `--describe <seed>` the layout; `--profile <seed>` timings;
     `--look file` checks one entrance look; `--no-shapes` uses one box per prefab. `GAPDEBUG=room:a:b` traces a route or a blocked join
     hop by hop, with the part under each spot. Exit code 1 on any failure, fault or finding. Per layout it checks: doorways meet,
     boxes clear, max and minDepth, nothing sealed, the way in a plain cap, gates on locks, wires resolve, doors, secrets and shortcuts
     placed right, and each solution replayed by independent code.
   - Pictures: `python shifting_halls.py --preview DIR` draws every room (and each hall's solution) with the game's real shapes
     (`dungeon_view.py`, `preview_rooms.py`, meshes from `game_meshes.py`, kept on this PC only: they're the game's assets).
   - The map: `tools/dungeonmap` draws every floor of a seed as the in-game map does (map.md).
4. **Build the mod.** The packs and looks are embedded, so a pack change needs a build. A plain `dotnet build mods/Dungeons -c Release`
   also copies the DLL into the game's `BepInEx/scripts` (`mods/Directory.Build.props`); `-p:DeployToGame=false` builds without
   touching the game. **Never deploy while a player is in the game** unless that's the test. The mod manager reloads a copied DLL by
   itself; this mod reloads in a world (reload level `world`, above), but until Claude Tools is released with the ItemReload rule,
   reload it with `reload Dungeons force`.
5. **Check in the game**, in a test world:
   - `dungeon check [pack]`: every prefab, game room and location the packs and looks name that this game doesn't have.
   - `dungeon packs`: each pack's problems as read in this world (missing rooms, plan tags no room has).
   - `dungeon seeds <pack> <count>`: lays out seeds without building them.
   - `dungeon spawn <pack> [seed]`, then walk it. `dungeon solve`, `dungeon mechs`, `dungeon use <key>`, `dungeon set <key> <state>` to
     drive the puzzle; `dungeon state` for everything (look, biome and enemy tier, objects held back, mechanisms).
   - `dungeon mapshot [floor] [all] [open]` and `dungeon mapshot mini`: the in-game map (M inside a dungeon) as pictures.
   - `dungeon aim`: what the hover ray hits now, when E doesn't reach something.
   - `dungeon reset`, `dungeon remove`, `dungeon orphans [remove]`, `dungeon bring <player>` (a rescue).
6. **Look at it and test with the player**: screenshots from the right places, then a real run: enemies, loot, every lever, a death, a
   logout inside.

## The commands (`dungeon ...`, through Claude Tools)

Request files in `BepInEx/claude/requests`, or `claude dungeon ...` in the game's console (F5). An id is the entrance's ZDO id as the
answers show it (good until the world loads again) or our own id (or its start); left out: the dungeon the player is in, else the
nearest. Commands that change the world follow the game's cheat rule (single player, the host, or devcommands): spawn, reset, remove, a
far enter, use, set, bring, `mapshot all`, `orphans remove`.

| Command | Does |
|---|---|
| `dungeon packs` | The packs and looks: title, rooms, plan, environment, problems, where each came from, hashes, what was left out. |
| `dungeon reload [force]` / `respawn [same\|new] [force] [id]` | Reads the packs folder again and remakes changed dungeons / remakes one dungeon (above). |
| `dungeon sync` | Whose packs are in use here, the hashes and versions; on a server, each player's version and what they were sent. |
| `dungeon spawn <pack> [seed]` | An entrance 4 m in front of the player, facing them (no seed: a random one that lays out). |
| `dungeon list` | The dungeons in the world (all on the host, the ones this game knows on a client). |
| `dungeon enter [id]` / `leave` | In or out as the entrance and the way out do. |
| `dungeon map [id \| <pack> <seed>]` | The layout floor by floor as text (also `BepInEx/claude/dungeon-map.txt`). |
| `dungeon boss [id]` / `boss go <room>` / `boss hurt <percent> [room]` / `boss cast <ability> [room]` | Every boss fight's state / to a boss room's way in / hurt the boss / cast an ability now (the last three: cheats): bosses.md. |
| `dungeon test <pack> [seeds]` | The gate's per-layout checks over many seeds, in the game (changes nothing): new-dungeon.md. |
| `dungeon tour [id] [rooms] [views] [bright]` | Camera-only pictures of a built dungeon's rooms (`bright`: lit up to show shapes and gaps): new-dungeon.md. |
| `dungeon mapshot [floor] [all] [open]` / `mapshot mini` | The in-game map as this character sees it, as a picture in `BepInEx/claude/shots/`. |
| `dungeon seeds <pack> <count>` | Lays out seeds 1..count: failures and why, room counts, sizes, time. |
| `dungeon state [id]` | Everything about one dungeon. |
| `dungeon mechs [id]` | Every mechanism: key, kind, state, moving, where, wires. |
| `dungeon use <key> [id]` / `set <key> <state> [id]` | Use one as a player would / put one in a state. |
| `dungeon solve [id]` | The layout's solution, with each mechanism's state now. |
| `dungeon reset [id]` | Back to new now (nobody may be inside). |
| `dungeon remove [id]` | Gone for good: its saved parts everywhere, then its entrance. |
| `dungeon orphans [remove]` | Saved objects of dungeons whose entrance is gone. |
| `dungeon check [pack]` | Names the packs and looks use that this game lacks. |
| `dungeon aim` | What the hover ray hits, the game's hover, and our nearest lever or way out in reach. |
| `dungeon music` | The game's music tracks (names for `music`), and what plays now. |
| `dungeon bring <player>` | That player to 2 m beside you. |
| `dungeon triggers` / `fire` / `blocks` / `checkpoint` | Triggers, faked events, building blocks, checkpoints: triggers.md. |
| `dungeon rooms [theme]` | Every room the game has, to `BepInEx/claude/dungeon-rooms.json`. |

## The layout builder (Layouts.cs, LayoutSolver.cs)

1. The entrance room at the origin, its way in towards -z.
2. The spine: rooms one after another, with the plan's steps on it in order (a few rooms between steps).
3. Branches on open doorways until the room count is reached, never past the last step.
4. Closing: two open doorways that touch are joined (a loop); every other gets an end cap of its type; one no cap fits is "sealed".
5. Checking: each step only through the step before it, each lock only through its gate, then the solver: from the entrance, using
   levers and plates, the end is reached in at least `minUses` uses, and every plan step's room is reached. A breadth-first search over
   the mechanisms' states and where the player stands (after a lever, at the lever); cycling mechanisms are taken as "wait for any
   state" (with `cycleIn`, only while set into those states), fights as won, breakables as breakable (and again all standing, to prove no
   secret room is needed), `hold` plates as nothing alone (a layout only two players could finish is refused), a shortcut as open only
   after its far end is reached (and never shortening the solution). The rules are at the top of `LayoutSolver.cs`.
6. Doors where both doorways allow one, by `doorChance`.

A seed that fails retries with seeds made from it (12 tries), then gives up with its problems.

## Enemies, loot and rewards: fitted to the land

**Why:** the game's Dvergr rooms brought their own Mistlands ticks and seekers into a Mountain dungeon, and they killed two players in
Mountain gear. Every saved object the builder makes is now looked at before it's made (`BuildCreatures.cs`, `BuildLoot.cs`).

- **The land's tier**: the biome under the entrance. Meadows 1, Black Forest 2 (the sea counts as 2), Swamp 3, Mountain 4, Plains 5,
  Mistlands 6, Ashlands 7, Deep North 8.
- **Creatures fit** when they're of the land's tier or the one below, and not a boss. One that doesn't fit is swapped for a seeded pick of
  the same role (melee, ranged, flying) from the pack's list for the biome, then the table's. A spawner keeps its object and gets a plan
  in its ZDO (`Dungeons_fit`) that the patches apply whenever it spawns (without the mod, the game's own creatures come back). Eggs, pots
  and props that make creatures some other way are left out when what they make is too strong. Levels are capped: up to 1 star to the
  Black Forest, 2 after.
- **Loot fits**: chests are emptied and filled again from the pack's `loot` for the biome (`common`, `rare`, `vault`, `boss`), else the
  builder's table (items of the land's tier weigh most, coins always, more deeper in). Nothing above the land's tier, even if the pack
  lists it (an item the table doesn't know is the pack's word). Pickables above the tier are swapped or left out; a random pickable gets
  one of its items that fits; items lying on the floor above the tier are left out; anything that gives items when broken or taken down
  (pots, rocks, the game rooms' black marble and Yggdrasil wood pieces, doors) keeps only the drops that fit. Shells give nothing.
- **Rewards** (`BuildRewards.cs`): two items of ours, worth nothing at the trader: `quaddungeon_giantcog` (a giant's cog, a crafting
  material for later: 1-3 in every vault and boss chest) and `quaddungeon_trophy_warden` (in every boss chest, or dropped by an
  encounter's boss where it falls: use one or the other, or there are two). When the vault opens, each player inside also gets 3 rolls
  from the pack's `vault` loot for the land (else `rare`), once per character per time the dungeon is new.
- **In the log**: "enemies fitted to <Biome> (tier N...)" and "loot fitted to <Biome>..." say what was changed, room by room.
- **Surprises for pack authors**: a weaker game creature in a higher land is swapped up; loot listed above the land's tier is dropped;
  a dungeon made before a change that leaves its layout as it was (new loot lists, a new creature table) keeps its old saved parts until
  `dungeon reset`, and creatures still alive from before stay. (A change of layout removes them by itself: lesson 14.)

## Lessons learned the hard way (with why)

Each of these happened in a test. Don't undo the fix; check new work against it.

1. **Prefabs must exist in ZNetScene.** `Builder.MakePart` finds parts there, and main-bundle props without a ZNetView aren't in it. A
   lever built from `Morkhalla_MetalPillar` lost its post and showed as a floating knob. Sounds too: `sfx_queendoor_open` and
   `sfx_forgeofpotential_success` aren't there. Now the generator refuses to write a pack that names a prefab the game's ZNetScene
   doesn't have (`dungeon_parts.py`: `Pack.write`), `dungeon check` lists what's missing in the game (sounds as well), and building logs
   each missing name once.
2. **Item-layer colliders are walk-through.** An item's collider is on the `item` layer, which characters pass, and is a small box for
   lying on the ground. Players walked through the giant `CharredCogwheel` gears. `BuildSolid.cs` now moves scenery colliders on
   pass-through layers to `static_solid` and gives items a collider of their own shape (mesh, a disc for a gear, or a box). Expect any item
   prefab used as scenery to need this; `piece_nonsolid` is left walk-through on purpose (crypt cloth, webs, candles, rugs). Banners and curtains are solid (the piece layer),
   though nothing to climb in the plain game; what players can climb is in new-dungeon.md ("Footholds").
3. **Floating props.** The first giant Shifting Halls had 35 lanterns hanging in mid-air (the player saw one floating up a staircase): the
   generator put them at a height with nothing above them. A script places props by numbers, and nothing tells it the floor or ceiling
   isn't there. Now hanging lights hang on chains from a ceiling or a beam, and `prop_check.py` fails any prop with nothing under it or
   above it within reach, from the parts' real boxes, and any encounter spawn point that isn't on a floor.
4. **Jumpable gaps bypass puzzles.** In 0.1.0 (seed 882425380) the player jumped from one balcony to the next and skipped the stair
   puzzle entirely. Now bars run along the balcony edges, and the harness's gap check (a walk and jump model of the player: 6 m across,
   1.5 m up, any height down) tests our rooms in every combination of their mechanisms' states. "walkable" or "jumpable": a way round the
   puzzle, close it; "only with a fall": declare it in `drops` or close it; "blocked": a join the solver counts that nobody can walk (the
   dungeon may be impossible). The same test found the game's giant ribcage blocking the great hall with no way round: it now has a tunnel
   through the ribs, and `prefab_shapes.json` gives the checks the real shape of hollow prefabs. So:
   - Rails, bars and stacked stairs make new jumps; a 1.7 m rail can't be jumped, so it also blocks a drop you declared.
   - Solid colliders block walks anywhere: the gap check looks at every room of ours, not only puzzle rooms.
   - Puzzle packs set `"parkour": false` (lesson 13).
5. **Aim boxes and the first-hit hover ray.** The game uses whatever its hover ray hits first from the camera; from below a ledge, the
   ledge's lip or the lever's own post took the ray before the lever. Levers and exits now get an aim box (`piece_nonsolid`, reaching
   0.3 m past their parts), and E with nothing usable under the crosshair uses our nearest lever or way out within 3 m and 70 degrees
   (`MechReach.cs`). `lever_check.py` checks a 2 x 2 m approach and a clear line from the eye; `dungeon aim` shows what the ray hits.
6. **Remove and reset must reach unloaded parts.** A dungeon is bigger than what's loaded round anyone (up to 320 m), so removing only the
   loaded parts left spawners standing in the air where no room was any more, and their skeletons fell. `Sweep.cs`: the server goes
   through every ZDO by its dungeon tag; a client asks it. Orphans (parts of a dungeon whose entrance is gone) are listed and removed by
   `dungeon orphans`, and by the host every 10 minutes. A chest that still holds something a player left is emptied out to the entrance first, loaded or not; one in use is left alone.
7. **Big layouts reach beyond the loaded zones.** Two gravestones fell 5000 m in the first Shifting Halls test: the player came back from
   the side, the far end's zone loaded before the entrance's, and nothing knew a dungeon was there. Now anything saved above 3000 m waits
   until a dungeon is built under it on that game (within 480 m of an entrance not yet laid out), a dungeon stays built while its player is
   inside, and whatever falls out is put back.
8. **The way in takes a plain cap.** The entrance room's way in is behind the Leave door; a nook or treasury there put a door opening and
   shutting behind it. The way in, and any doorway a gate closes, take only plain caps (under 1 m deep, no mechanisms, no chest), or the
   pack's `entranceCap`.
9. **One-shot levers.** The player pulled a lever on floor 2 meant to turn the stair between floors 2 and 3: "it's not moving". Pulling
   it again turned the stair back (each pull moved it one state on), so a lever meant to be pulled once looked broken. The player's rule:
   "if a lever is only supposed to be pulled one time then make that only be pulled one time". Now a lever with `once`, or whose wires
   all `set:` a state (or `start` a fight), locks after one pull until a reset (its record is saved, like a broken wall's), with a
   distinct unlock sound (`sfx_gui_craftitem_workbench_end` and `sfx_secretfound`) so the player knows it worked. Use `next` only for a
   lever meant to be pulled again and again. The rule for authors: a lever that opens something for good uses `set:1` (pulling it twice
   changes nothing); `next` toggles, so a second pull closes what the first opened, and belongs only where cycling through states is the
   puzzle (a turning stair). The opener wire the layout adds to a key's lever is `set:1` for this reason (it was `next`).
10. **Biome-fitted enemies and loot.** See above: the game's rooms carry their own land's creatures and treasure.
11. **Doorway heights must match across a join; never read a floor from a box.** (To confirm in the game once: the game's doorway
    height over its floor, measured in testmechs, where forest crypt rooms join ours. It is on the release checklist.)
    Two rooms join where their doorway points are equal (the new room is placed so its `at` lands exactly on the other's), so the floors line up only if both rooms' `at` sit at the same height
    over their own floor. Ours: `at` is on the floor, in the middle of the threshold (`dungeon_parts.py`). The game's: `at` is the
    `RoomConnection`'s position as the prefab has it, not tied to `Room.m_size` (a forest crypt corridor's box is 5 m tall with its doorways
    at y -1, its end cap 4 m at -1.5, a sunken crypt room 10 m at -2.5, a Dvergr room 8 m at -2). The game stands its doors on that point
    (`DungeonGenerator.PlaceDoors`), so it is very likely the threshold's floor, but the catalogue can't prove where the walkable floor is,
    and the way-out door once stood at the wrong height. So:
    - Anything placed at a doorway finds the floor with a ray down from the point, as `PlaceExit` does. Never derive a floor from a box's
      bottom (the game's boxes reach below their floors).
    - Our rooms joined to our rooms: fine, as long as all use the same convention (on the floor).
    - A room of ours that joins a game room by its doorway type: measure the game's height once in the game (stand in the game room's
      doorway and compare your feet's y with the doorway's world point from `dungeon map` or the layout) and use the same height over your
      floor. `testmechs` mixes forest crypt rooms with ours on type `""`: check there first. A mismatch shows as a step up or down at
      that doorway.
    - Doors at game-to-game joints are placed exactly as the game places them, so they're right whatever the height.
12. **Never save ZDOIDs.** A ZDOID changes every time the world loads. The entrance has its own id (`quaddungeon_id`), parts carry it as a
    tag, and an encounter's creatures are tagged and counted among the loaded objects instead of being remembered by ZDOID, so a fight goes
    on after a relog or a restart.
13. **Parkour off in puzzle dungeons.** Wall runs, wall jumps, climbs and ledge grabs skip a puzzle. A pack with `"parkour": false` turns
    Quad's Parkour off inside (`Api.ParkourOff`, read by Parkour).
14. **A changed pack changes existing dungeons.** The same seed with a new version of a pack is a different layout. Saved parts made for the
    old layout are removed when they load (the layout signature), the map's fog of war starts again, and the dungeon is made anew. Rename
    or remove a pack or a look only knowing its entrances will be broken.

15. **Saved parts need a networked prefab at scale 1.** A `saved` part that isn't networked is built as scenery (with a warning); a
    saved part's scale is only kept if the prefab syncs it, so it comes back the wrong size after a reload.
16. **The game takes an object's ZDO off before destroying it.** Anything needed when an entrance or part goes (its origin, its id) must
    be cached before, not read in `OnDestroy`.
17. **Sounds played on every game must be made without a ZDO.** Some sfx prefabs are networked: instantiated on every player's game, each
    would make a saved object.
18. **Never assume the entrance, a part or a player is loaded.** Acts go by a global RPC to whoever owns the entrance's ZDO; each player's
    game tells the owner who stands on a plate or in a fight's area; reset and remove go through the server's ZDOs. The safety net puts
    things that fell back on the floor of the room above, gravestones at their death spot, removes creatures with no room over them, leaves
    effects and projectiles alone, and never moves anything between dungeons.
19. **The solver tracks where the player stands.** After pulling a lever the player is at the lever, and a turning stair may have taken
    their way back. A search that let them walk back to the entrance for free passed puzzles that strand them. Falls count only when
    declared in `drops`.
20. **Decorative motion multiplies the search.** The solver gives up past 200,000 search states or 10 million combinations of mechanism
    states, and every mechanism with joins or a gate doorway multiplies them. A gear or a drum there only for the look should be a
    `machine` (with `spinScale` if it changes speed), which costs nothing. A cycling rotator's joins all count as open (players wait), so a
    cycling room never makes a puzzle harder: puzzles come from states levers set.
21. **Give steps, hubs and late rooms spare doorways.** Every step but a dead-end key needs two doorways. A step with a doorway type of
    its own (the vault, the warden) is attached straight to the room before it, with no fillers between. Shortcuts and secret rooms need a
    free doorway to hang on.
22. **`joinBy` decides how a room is entered.** Without it, a third of the layouts entered the Shifting Hall from a balcony instead of its
    ground floor.
23. **Don't rely on loops.** Rooms joined into a loop could go round a gate or a stair; the solver refuses such layouts, so a loop can't
    be part of a design.
24. **Run the harness on every pack change.** It takes 30-60 s with the gap check (the giant ribcage alone is about 2000 boxes) and exits
    1 on any pack problem, failed seed, fault or gap finding.
25. **Chest contents never drop where the chest stood.** A reset or a remake changes what's under a chest: its contents dropped there
    fell, and the safety net put them back inside the dungeon. Now what a player left goes on the ground in front of the entrance, and
    untouched builder loot is dropped silently (`Keys.LootSig`).
26. **A chest's saved contents are bytes.** `ZDOVars.s_items` is a byte array (`Container.Save` writes bytes). Read with `GetString` it
    is always `""`, so "is this unloaded chest empty?" always said yes: from when `Sweep.cs` went in until the fix, it destroyed unloaded
    dungeon chests with whatever players had left in them, and the first `LootSig` check would have deleted touched loot on every reset
    without a word. Read `s_items` with `GetByteArray` (or load the chest's `Inventory`), and test every "is it empty / untouched" check
    with a chest that holds something.
27. **The entrance can lose its owner while everyone is inside.** Everything a player does in a dungeon (`Site.Act`) is handled by the
    entrance's owner. The game's server releases an object's ownership when its owner's active area no longer reaches it
    (`ZDOMan.ReleaseNearbyZDOS`, every 2 s): about 140 m into a big dungeon. In Quadbobheim (0.2.2) everyone was deep inside, nobody owned
    the entrance, and lever pulls were dropped without a word. The fix (`Acts.cs`): the server keeps the entrance with its owner while
    they're above 3000 m; a game whose player is inside, or the server, claims an unowned entrance; the owner answers every one-off act
    (`Dungeons_Ack`), and a sender with no answer in 1.5 s takes the entrance over or sends again. **Rules for new code:** every new
    player action goes through `Site.Act`: never call the owner's handler directly, and never require `IsOwner` on the acting game. An
    act that's sent once a second anyway gets its prefix in `Acts.Repeated`. (The release check-list has the test: two players far
    inside, the entrance unloaded on both, pull a lever.)
28. **A boss must be able to walk off its dais.** The first Warden spawned on a dais of two 0.6 m tiers. Valheim's creature navmesh
    (`Pathfinding`, built at runtime) steps 0.3 m for humanoid agents and 0.6 m for troll-sized ones, and creatures never jump down, so the
    dais top was an island and the boss stood at its edge ("it looked like he couldn't actually get down"). The fix: daises in 0.2 m steps
    on every side (`boss_rooms.step_dais`), a runtime leap when a boss is stuck (`unstick`, 5 s), and the gate's `creatures` step
    (`creature_check.py`). The game's navmesh also reaches y 5500 at most: a boss above that can't walk (logged when the fight starts).
    The boss code: `BossModel.cs`, `PackReaderBoss.cs`, `BossRecord.cs`, `BossFight.cs`, `BossShow.cs`, `BossBody.cs`, `BossMarks.cs`,
    saved in the entrance's ZDO (`quaddungeon_bossfight`); the tools `boss_rooms.py` (the Warden's and guardian's rooms, used by
    `shifting_halls.py`) and `creature_check.py`.
29. **A reload in a world is a frame with no mod.** With no hold-back patch, saved objects up there get made and fall, spawners spawn
    unfitted creatures, and a landing after our teleport counts a 5000 m fall. So the old copy hands each built dungeon over as it stands
    (the "husk"), installs a small gap guard under its own Harmony id, and the new copy removes it once it has taken over (`HotReload.cs`).
30. **Dungeon parts can't be hammered or smashed.** Every saved part the builder makes is marked a dungeon part (`Dungeons_part`): the
    hammer can't take it down (no free build refund from a vault chest, no taking a dungeon's doors, torches or furniture apart), and its
    building pieces (walls, floors, stairs, gates, grates, doors, chests, torches, black marble) take no damage: a hit shows "too hard"
    (`BuildSaved.cs`, `Builder.Unbreakable`). Nobody smashes a way round a puzzle, a chest open, or black marble for Mistlands materials in a
    Meadows dungeon. The game's own breakables (pots, bone piles, roots, rubble, Mórk halla's gate doors) still break, their drops kept to
    the land's tier, and so do our breakable walls.
31. **Puzzles must hold against the best jumper.** The gap check models the game's own jump at its worst case for us, Jump 100 and Run 100
    (the user's choice): up to 3.1 m up, and 10.9 m across on the level for a sprinter (`GapCheck.cs`). A puzzle that a skilled player can
    jump round fails.

## Not built yet

- Placement in the world (biomes, spacing, entrance looks per biome, a command for existing worlds): entrances only come from
  `dungeon spawn` for now.
- `dungeon capture <pack> <room>` (save pieces built in the game as a room) and `autoplay`.

## Keeping this skill up to date

Every agent working on the dungeon system sends what it learns to the docs agent, who keeps this skill right. When you change the pack
format, a command, a rule or a check: change `Packs.cs`'s comment and this skill (and pack-format.md) together. A lesson goes in with
what happened and why the fix is what it is.
