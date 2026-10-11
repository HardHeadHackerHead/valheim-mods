# The dungeon map: what a pack author must get right

Inside a dungeon, **M** opens the dungeon's own map, and the corner minimap shows it too. The code: `MapLayout.cs` (floors and rooms from a
layout, plain arithmetic), `MapDraw.cs` (the drawing), `MapWindow.cs` (the window and the fog of war), `MapMini.cs` (the minimap). The map
reads only the layout and the pack, so a room built carelessly draws wrongly: check your pack's maps before trying it in the game.

## Floors come from doorway heights

- Rooms only join at doorways, so the heights doorways are at are the dungeon's floors: doorways within 2.5 m of each other in height
  are one floor, numbered from the lowest (1). **Keep a floor's doorways at one height**: a doorway a few metres off makes a floor of its own.
- A point (a player, a lever, a chest) is on the highest floor whose doorways are at most 2 m above it.
- **Our rooms**: `size` is the room, so make it match the walls. A room starts on the floor 2.5 m up from the bottom of its box (a pit or a
  footing under the floor is ignored), or on its lowest doorway's floor if that is lower. It reaches every floor whose doorways are at
  least 2.5 m below its ceiling. On the floors above its own it's drawn striped: "the upper part of a taller room" (a balcony level, a
  shaft). So the 48 m hall of Shifting Halls is on all four storeys.
- **The game's rooms** run from their lowest to their highest doorway (their boxes are rough).
- The map only offers floors where the player has seen something.

## What is drawn, and what stays hidden

- **Fog of war**: only rooms this character has stood in. Kept with the character (`Player.m_customData["Dungeons.map.<dungeon id>"]`,
  with the pack, seed and room count), never shared. A new version of the pack that changes the layout starts it over.
- **Doorways**: a gap in the wall where it joins another room or is the way in; one into a room not visited yet trails off in dashes;
  sealed doorways and those joined only to an end cap are plain wall.
- **End caps** with no floor area (x or z under 1 m) are walls, never rooms. End caps with floor (nooks, treasuries) are rooms.
- **Mechanisms show their state now, never a hint or the solution**: gates and doors open or shut at their `doorway`; levers lean by
  state; plates pressed or not (not on the minimap); a mechanism's joins that are active now as a dashed path through the room, or a
  ladder between floors for a `stair`; a rotator with no join now, which way it points. A `machine` is never drawn.
- **Chests and doors** are found among the dungeon's saved parts in the world, and shown only near a visited room.
- **The vault** (the plan's last step, or a room tagged `end`) gets a gilded outline and a diamond. **The way out**: the entrance room's way
  in, and any `exit`.

## Stairs and other ways between floors

- **Fixed stairs are found by name**: parts whose prefab name contains `stair`, `step` or `ladder`. Steps make one flight while each comes
  right after the last in the room's part list, has the same `euler` and `scale`, is at most 1.6 m higher or lower and 3.5 m away, and keeps
  climbing the same way. **List a flight's steps in order, bottom to top.** Its width is 2 m times `scale.x`. A lone stair piece climbs
  against its forward direction (as the Grausten stair does). A ramp, or a stair prefab without those words in its name, isn't found.
- A room whose doorways that lead somewhere are on more than one floor gets an up or down triangle beside each one (not rooms whose ways
  between floors are their mechanisms' joins).
- Declared `drops` show as one dashed arrow per doorway dropped from.

## Secrets, shortcuts, the boss

- **Breakable**: the wall over its `doorway`. While it stands, that doorway is plain wall from both sides, and the secret room behind it is
  hidden even if visited (the room tagged `secret`; failing that, the one farther from the entrance). Broken: shown.
- **Shortcut**: before it's open, only the deep (far) end shows, barred; once either end has been used, both show. Ends in flat end caps are
  drawn against the wall, in the room they close.
- **Boss**: an encounter with a `boss`, or any encounter in a room tagged `boss`. A skull once its room is visited, struck through once it's
  cleared.

## Checking a pack's map without the game

```
dotnet build tools/dungeonmap -c Release
tools/dungeonmap/bin/Release/net48/MapRender.exe <pack.json> --seed N      every floor, into tools/dungeonmap/out
    --visit 0.4            fog of war: the rooms nearest the entrance first
    --states 7             random mechanism states
    --zoom 3 --centre <room index>
    --mini                 the minimap's view
    --fake real,broken,open,won   walls broken, shortcuts open, fights won
```

`MAPDEBUG=1` lists each room's floors, the breakables and the chests. In the game: `dungeon mapshot [floor] [all] [open]` and
`dungeon mapshot mini` save pictures to `BepInEx/claude/shots`.
