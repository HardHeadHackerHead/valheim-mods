# Quad's Terminal: a computer in Valheim (mods/Terminal, version 0.1.0, built and tested 2026-10-12)

A computer you build with the hammer. Sit at it and it boots to a desktop with programs. **Valheim** is one of them: start it and a
second, shared character, **the Proxy**, appears in the world, and the screen shows the world through the Proxy's eyes, for everyone
near. The other programs: a **Browser** (renders real web pages), and **Scripts** (look after scripts, see `docs/scripting-design.md`).

Decided with the user: one shared Proxy per terminal (not one per player); it works in multiplayer; Proxy starts with starting gear;
it can die; you have to boot the computer and launch Valheim to get the Proxy; the desktop is a small platform other programs can join.

## Parts

| Part | File(s) | What |
|---|---|---|
| **Terminal** (piece) | `Things.cs`, `Model.cs`, `TerminalPiece.cs` | Desk, monitor and stool built in code from the game's materials; a copy of the wooden chest's components (no storage), registered per `docs/modding-pitfalls.md` (both Awake hooks, `PieceReload`, `NeedsServer`). Its **whole state is in its saved data**, so every player near sees the same screen. |
| **State machine** | `TerminalPiece.cs` | Off → Booting (someone sits) → Desktop → Loading (an app is started) → App (in front) → Desktop (Escape; the app keeps running) → App (its taskbar button). Several apps can run; a crash (Valheim's Proxy died) leaves a message on the desktop. The owner's game moves timers on. |
| **Desktop** | `Desktop.cs` | One UGUI scene, drawn by its own camera (below the world, UI layer) into the terminal's RenderTexture: boot screen, icons, taskbar with running apps and the game's clock, loading screen, crash dialog, window layer, software cursor. 1280x720 reference space. |
| **Apps** | `Apps.cs` | A registry (`App`: id, title, icon, load time, `Draw` or a window with `BuildWindow`/`UpdateWindow`/`Click`/`Scroll`). Valheim, Browser and Scripts are three. |
| **Sitting and input** | `Drive.cs` | The player is attached to the stool (the game's sitting; the camera is held on the monitor with `AttachStart(..., cameraPoint)`, near clip lowered). A `Player.SetControls` prefix takes every key and zeroes it for the player. Desktop and cursor apps: mouse moves a cursor, attack clicks, wheel scrolls, E stands up. Valheim: keys steer the Proxy. Escape = back to the desktop (`Menu.Show` prefix). |
| **Proxy** | `Proxy.cs`, `ProxyBag.cs`, `ProxyControls.cs`, `ProxyAuto.cs` | A copy of the Player prefab with the player-only parts removed (the AICompanion pattern), a `Humanoid`, the game's `MonsterAI` with its thinking switched off (health bars need one), and `ProxyBody`. |
| **View** | `View.cs` | The Proxy's third-person camera (shoulder, pulled in by obstacles, wheel zoom) drawn into a texture; health, stamina and a crosshair are baked into the picture so onlookers see them. |
| **Browser** | `Browser.cs`, `Dom.cs`, `Css.cs`, `Layout.cs`, `PageView.cs` | Fetches https pages, parses HTML and CSS, lays them out and draws them (below). |
| **Scripts program** | `ScriptsApp.cs` | List, source view, powers with Allow buttons, log, Run/Stop/New/Paste/Delete. Talks to the Scripts mod through `Scripts.HostBridge` (found by name). |
| **Scripts bridge** | `ProxyAuto.cs` (`ScriptsBridge`) | The one class the Scripts mod calls to drive the Proxy. |

## The Proxy

- **Driving**: the driver's game takes ownership (`ClaimOwnership`); the terminal's saved data records who sits (beat once a second so a
  vanished driver frees it). Walk, run (costs stamina), jump, swing, block, crouch. 1-8 use or wear what it carries (food heals), Shift+1-8
  drops it; E uses a door, a plant, a lever or a hive in front of it, else stands you up; the wheel zooms the camera.
- **Bag**: 5x3, kept as base64 in its saved data (written only when it changes), re-read by whichever game owns it next. Picks up loot near
  it while someone drives. Starting gear (club, rag tunic and trousers) is tagged so a death leaves loot, not free gear.
- **Death**: ragdoll, a **tombstone** with the loot, the terminal records a 60 s recovery, the screen goes back to the desktop with
  "Valheim has stopped working"; launching again shows the loading screen for the recovery time, then a new Proxy with the starting kit.
- **Stamina** (60): running 12/s, jump 8, swing 10, block 4/s; regenerates after about a second of rest. Written to its saved data as a
  percent when it moves by 3 or more.
- **On its own** (for scripts): `GoTo` through `BaseAI.MoveTo` (the game's pathfinding), one `SwingAt`, `UseAt`, `Equip`, `Speak`
  (NPC chat bubble), `Deposit`/`Withdraw` against a chest within reach (the chest is claimed and re-read first: pitfall), `Nearest`.
  Orders stop when a player takes the keys; events `arrived`, `blocked`, `taken`, `released`, `died` wait in `ScriptsBridge`.
- **Reach**: it stays within the tether (default 100, at most 120 m) because the game only loads the world around real players; a
  Proxy past it is put back (`PlaceAt`, on its Rigidbody). Moving the game's loading point to the Proxy (`SetReferencePosition`, as Arena
  and Dungeons do) is the open version-2 idea.

## The Browser

Valheim can't carry a browser engine, so this is a small one: HTML parser, CSS (selectors with ids, classes, attributes, `:not()`,
first/last-child, combinators; `@media` for a laptop-sized window; custom properties and `var()`; `light-dark()`), a layout engine
(blocks with margin, padding, border, width; inline text with word wrap and alignment; **floats**, so an infobox sits beside the text;
lists; tables with columns sized to their content; pictures, loaded PNG and JPEG), drawn into the screen through pooled UI objects (only
what is in view), with a scroll bar. No JavaScript, forms, cookies, SVG or WebP. Tested on example.com, Wikipedia and DuckDuckGo.

Rules (the audience is the public): https only, normal port only, no local or private addresses, every redirect checked again, pages
over 1.5 MB or 12 s refused; stylesheets and pictures follow the same rules. What is shared is the address and the scroll position, in the
terminal's saved data; **another player's game fetches the same address only if its host is on that player's own list** (default
Wikipedia, DuckDuckGo, example.com) and otherwise shows a note, so nobody's game is made to contact a site by someone else. The server can
switch the browser off. Text for the address bar comes from the game's own text box; a bare word becomes a search.

## What is built and tested (single player, 2026-10-12)

Boot, desktop, launching, loading, the Proxy appearing, driving, fighting, eating, dropping, picking up, E on a door, stamina, death and the
tombstone, the recovery timer, reloading the mod with terminals and Proxies standing, the browser (real clicks, scrolling, search, links),
mouse feel, brightness (the desktop is dimmed against the game's bloom: setting `DesktopBrightness`).

## Not done / untested

1. **Multiplayer** has not been tried: two players, hand-over of the Proxy, the screen on the other game, a server without the mod.
2. **A real restart** with a terminal and Proxy placed (`Missing prefab hash` check).
3. The tether's pull-back at 120 m was only exercised through the same call used by the warp test.
4. Scripts program, Browser state and the desktop are only meaningful on the game that runs them; other players see the same desktop and
   cursor and Valheim's picture, but the Scripts window shows a note.
5. **Dodge roll** is part of the `Player` class and is not available to a `Humanoid`.
6. Look pass: sounds, a screen glow, a light at night, nicer icons, translations (8 languages), the mod pages, README, CHANGELOG,
   Thunderstore and Hexium packaging. Nothing is released.
7. Browser: pictures only PNG and JPEG; flex and grid are approximated (flex rows flow side by side, the rest stacks); no JavaScript.
8. Reach beyond the tether (version 2).
