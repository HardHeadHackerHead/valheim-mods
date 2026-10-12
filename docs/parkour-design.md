# Quad's Parkour: the design (agreed with the player, 2026-10-11)

A new mod in the valheim-mods repo, `E:\valhiem\mods\Parkour` (folder, csproj and DLL all `Parkour`: publish.ps1 and the F7 feed need the
folder and DLL names equal), GUID `com.quad.parkour`, Name `Parkour`, Version `0.1.0`, page title "Quad's Parkour".

## The four moves

| Move | How the player does it | What happens |
|---|---|---|
| **Wall run** | Sprinting, jump at a wall at an angle (roughly 20°-70° between the run direction and the wall) | Runs along the wall at run speed, sinking slowly (gravity mostly cancelled, more as time goes on), for 1.2 s at skill 0 up to 2.5 s at skill 100. The body leans into the wall. Ends at the time limit, out of stamina, at the wall's end, on releasing sprint or turning away, or with a wall jump. |
| **Wall jump** | Jump while wall running or clinging to / right after touching a wall (a short grace window) | Kicks off: away from the wall and up. Chains between alternating walls (up a gap between two buildings); the same wall can't be kicked twice in a row before touching ground or another wall (no free ladder). |
| **Wall climb (run up)** | Sprinting straight at a wall (under ~20°), jump near it | Runs 2 m up it at skill 0, 4 m at skill 100, then grabs the top if in reach (into the ledge grab) or drops back off. |
| **Ledge grab and mantle** | In the air (a jump, a climb, a wall run), an edge in reach in front at chest-to-head height | Catches the edge (brief hang), then pulls up onto it (forward/jump), or lets go (crouch/back). Only where the capsule fits on top. |
| **Landing roll** (added 2026-10-11) | Press Crouch just before landing (0.3 s window at skill 0, 0.5 s at 100) | The game's own dodge roll on landing (synced to other players), keeping speed: no fall damage up to 8 m (10 m at skill 100), half above it. Dodge-like stamina, raises the skill. Mistimed: a normal landing. |

Works on **everything solid**: built pieces, rocks, cliffs, trees (the game's solid layers; not water, characters or triggers).

## Cost and the skill
- Each move costs **stamina**, like sprinting and jumping do, scaled by the armour's movement penalty the way the game scales sprint and
  jump (`GetEquipmentMovementModifier` etc.). No regen while on a wall (the game already pauses regen while `m_wallRunning`).
- A new **Parkour skill**: every move raises it; its factor lengthens wall runs, raises climbs and cuts stamina costs. A custom skill
  needs: our own `Skills.SkillType` value (a stable hash of "com.quad.parkour", as blaxxun's SkillManager does, so it never collides),
  a `SkillDef` added to the player's `Skills.m_skills` (with an icon), `Skills.IsSkillValid` accepting our value (it uses
  `Enum.IsDefined`, so without a patch the saved level is dropped on load), and the translation key `skill_<value>` (the game shows
  `$skill_` + the type's ToString().ToLower(), which is the number). Removing the mod later just drops the skill: no harm.

## Limits (as the game does)
No parkour while over-encumbered, dead, swimming, riding/attached, attacking, blocking, dodging, staggered or in a debug fly. **Wet**
shortens wall runs (about 30%). Not while the player is typing / in a menu (the game's `TakeInput`).

## Fall damage: fair
The game measures fall damage from `m_maxAirAltitude` (the highest point since ground). Touching a wall **during a move** counts like
ground for that (reset it then), so a wall run or climb doesn't hurt you from where your jump peaked; falling off a wall high up hurts
from where you left it. Never reset it on a mere brush against a wall outside a move.

## What the game already gives us (read in its code, Character.cs)
- `m_wallRunning` and `IsWallRunning()`: the game's own "wall run" is sprinting up ground steeper than the slide angle (`ApplySlide`).
  While it's set, stamina doesn't regenerate (Player.UpdateStats).
- **The lean is shared with every player**: `UpdateGroundTilt` tilts `m_visual` toward the surface while wall running and writes the
  local rotation to the ZDO (`ZDOVars.s_tiltrot`); other players' games (even without our mod: `CanWallRun()` is true for players) apply
  it. Use this for our wall run/climb lean so everyone sees it.
- Vertical walls are **not ground** to the game (`OnCollisionStay` only counts contacts with normal.y > 0.1): find walls with our own
  short casts (sides and front, the game's `s_groundRayMask` / solid layers).
- `Character.Jump` / `ForceJump(vel)` for kicks; `m_jumpStaminaUsage`, `m_jumpForce`, skills via `GetSkills()`.
- Decompiled code: `C:\Users\DHack\AppData\Local\Temp\claude\E--valhiem\067c02b0-b6c3-49ee-9484-8196a713a2fa\scratchpad\movement\`
  (Character.cs, Player.cs, Humanoid.cs, Skills.cs from this game version).

## Animations
The game has no wall-run or climbing animations: use its run animation with the lean for wall runs and climbs, its jump for wall jumps,
and a short pull-up (body moved up and over in ~0.4 s) for the mantle. Keep the animation part in its own file: custom animations come
later.

## Multiplayer and settings
Movement is each player's own game's job: the mod isn't needed on the server, and players without it simply see the others move (and
lean). Settings: each player's own (the moves on/off one by one, hints); the **strengths are the server's** (durations, climb heights,
stamina costs, skill gain) through `mods/Shared/ServerSettings.cs` (link it in the csproj as Rainbows does), "[Synced with Server]" in
their descriptions.

## Other mods (clash notes)
SmoothClimbing (HansenbansenTeam), Just_Vault (igentuman) and Climbing (Rabid_Wolf_Studios) also act at walls and ledges. A separate
research report will say what they patch and their GUIDs: `scratchpad\parkour\compat.md`. When one is installed, Parkour stands down
for that part (SmoothClimbing: our wall climb and ledge grab; Just_Vault: our mantle) and says so once in the log; the page's "Other
mods" part lists them.

## How our mods are built (follow it)
- Read `E:\valheim-claude-tools\docs\modding-pitfalls.md` and the skills (`C:\Users\DHack\.claude\plugins\cache\valheim-claude-tools\valheim-modding\1.2.8\skills\`:
  valheim-mod-start, valheim-pitfalls, valheim-compat, valheim-game-code).
- Translations from the start (mods/Shared/Translations.cs comes in by itself when `translations/English.json` exists): 9 languages
  (English, Russian, German, Chinese, Portuguese_Brazilian, Spanish, French, Polish, Turkish), keys `parkour_*`, the game's own words
  first (game words in every language: `D:\SteamLibrary\steamapps\common\Valheim\BepInEx\claude\game-words\`); check with
  `python E:\valhiem\tools\check_translations.py Parkour`.
- Reload level `world` (it only patches and adds a skill): reset every static in `OnDestroy`, `UnpatchSelf`.
- **Testable without a player at the keyboard**: read the player's input through one small class the mod owns, so a Claude Tools command
  (`parkour status`, and `parkour try <wallrun|walljump|climb|ledge> ...` driving that input: sprint/forward/jump for a few seconds)
  can perform each move for automated tests. Register commands into Claude Tools by reflection as other mods do (see
  `E:\valhiem\mods\Rainbows\Tools.cs`). Anything that moves the player follows the game's cheat rule (`mods/Shared/TestCommands.cs`).
- Files: DESCRIPTION.txt (what it does, for players), CHANGELOG.txt (`0.1.0: First release: ...`), RESTART_REQUIRED.txt only if the
  reload level isn't world.
- Build: `cd E:\valhiem\mods && dotnet build Parkour -c Release -p:DeployToGame=false` (never deploy: the game may be running).

## Skill progression and the parkour bar (added 2026-10-11)
- Wall run: 1.2 s at skill 0 to 2.5 s at 100, up to +15% speed (about 6 m to 14 m along the wall).
- Wall-jump chain: 2 kicks in a row at skill 0, +1 every 20 levels, up to 6.
- Auto-roll unlocks at Parkour 50: landing forward from a height that would hurt rolls by itself (Crouch still works; can be switched off).
- A blue parkour bar, cloned from the game's stamina bar, just above it: fills when a move starts, drains as it is used (wall-run time, wall-jump kicks left), shown only during a move.

## Added 2026-10-11 (after the first play)
- Wall runs 2-4 s; every move refills the blue bar and runs on it only (wall run, climb, hang 3-6 s, pull-up: no stamina; the sprint drain is paused during moves). Wall jumps and rolls keep the game's jump/dodge stamina.
- Pull-up needs a fresh forward press (or jump); thin wall tops are vaulted over; seams between stacked walls are not ledges.
- Feel: the game's own footstep/effect sounds on walls, kicks, pull-ups and rolls; camera roll toward the wall and an FOV kick (both can be switched off); wall jumps steer toward where you look.
- Ledge shimmy: left/right while hanging moves along the edge (the hang bar keeps draining).
- Slide: sprinting, press Crouch on the ground: a speed burst, longer downhill, 1-1.5 s on the blue bar; jump out of it keeps the speed.
- Later: custom animations; decide whether to cap endless climbing (every move refills the bar).
