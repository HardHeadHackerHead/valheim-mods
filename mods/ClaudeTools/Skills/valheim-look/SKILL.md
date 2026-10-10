---
name: valheim-look
description: Make a Valheim mod look and sound like part of the game - windows, buttons, fonts, HUD messages, 3D models, materials, effects and sounds - instead of a debug overlay or untextured blocks. Use when a mod adds any UI, notification, piece, item model, scenery, effect or sound.
---

# Making a mod look like it belongs in Valheim

> **Game closed?** 3D models can be designed and checked offline with the kit in `BepInEx/claude/modelkit` (see its README). UI and the
> in-game checks (`render`, `inspect`, `shot`) need the game running with Claude Tools.

Players notice at once when a mod looks bolted on: Unity's default grey buttons and Arial text, untextured boxes, sounds that ignore the
volume slider. The game already has everything you need. **Take it from the game instead of making it**, in this order of preference.

## Windows and menus: three levels

**1. Use a game window as it is** (best: tooltips, dragging, gamepad hints and sounds come free).
- A shop: the game's `StoreGui` with a `Trader` on a child object kept `enabled = false` (no chatter or head turning); fill
  `Trader.m_items` (each `TradeItem` can carry `m_tooltip`), then `StoreGui.instance.Show(trader)`.
- A bag or chest view: `Instantiate(InventoryGui.instance.m_container)`, destroy the copied grid cells (the grid makes its own), hide
  buttons you don't need, find texts in the copy by path. The game wires the grid's events in code, so re-wire them: create delegates to
  InventoryGui's private `OnSelectedItem`, `OnReleasedItem`, `OnEnterElement` and `CanDropDragOntoItem` (without the last, the grid throws
  for every item).
- Item slots: reuse the game's `InventoryElement` cells, so equip markers, tooltips and drag behave as in the game.

**2. Build from cloned game parts** (a window of your own that still looks native). **Start from `templates/GameUi.cs`** (in
`BepInEx/claude/templates`): it does everything below (panel, boxes, buttons, tabs, braid, fonts, item cells, compact item chips,
progress bars, icons, a scrolling list with the game's scrollbar, a place on the HUD, the canvas, the input patches), and its comment
shows a whole window. The Recycler's and the Bounty Board's windows are built with it. The parts, by path under
`InventoryGui.instance` (log a warning if one is missing: a game update can move them):

| Part | Path under InventoryGui |
|---|---|
| Wood panel background | `root/Crafting/Bkg` |
| Sunken list box | `root/Player/sunken` |
| Title underline (braid) | `root/Crafting/BraidLineHorisontalMedium` |
| Plain button | `root/Player/Container/TakeAll` |
| Tab button | `root/Crafting/TabsButtons/Craft` |
| Title font (Norse) | `root/Crafting/topic` |
| Body font | `root/Crafting/Decription/Description` |

- **Canvas:** your own `ScreenSpaceOverlay` canvas, parented next to the inventory's (`InventoryGui.instance.transform.parent`), with
  `sortingOrder` above it, and **every CanvasScaler field copied from the inventory's canvas** so the player's GUI scale applies (the game's
  `GuiScaler` changes `scaleFactor` at runtime: copy it again when the window opens).
- **Images:** copy all of `sprite`, `type` (sliced), `material`, `color`, `pixelsPerUnitMultiplier`, `fillCenter`, or the panel looks wrong.
- **Fonts:** take `font` and `fontSharedMaterial` from a live game TMP text; never hard-code font names, never use Unity's default font.
  Norse for titles, Averia for body text and **numbers** (Norse draws 0 as a rune).
- **Cloned buttons and tabs:** destroy `gamepad_hint*` children and every `UIGamePad` component (it binds a gamepad button to the
  original, so the clone would fire on it); replace `onClick` with a new event; reuse the child named `Text`. Destroy `ButtonTextColor`
  too: it sets the label's colour every frame, so a colour you give it (gold for a chosen tab, orange for "are you sure?") never shows;
  then colour the label yourself (dim when the button is disabled).
- **Tabs:** the game shows the chosen tab by making it **not clickable** (`interactable = false`), which draws it pressed in; its label in
  gold. A tab with news (rewards waiting) can have a green label.
- **An on/off setting:** a button that says its state ("Auto-feed: On"), its label gold when on (`GameUi.Toggle`; with an item icon for
  a list of items to allow). (`InventoryGui.m_pvp` is a real game Toggle, untried as a clone.)
- **Numbers:** open-ended values (a reserve, a target), `GameUi.Stepper`: "-10 -1 [value] +1 +10" of cloned buttons around a sunken box.
  A fixed range (a radius, an opacity), `GameUi.Slider`, a clone of the split-stack slider (`InventoryGui.m_splitDialog.m_splitSlider`,
  whose event is empty: the game wires it in code). Never rebuild a window because a slider moved (that destroys it mid-drag): leave
  slider values out of the signature, update their labels from the slider's callback, and copy outside changes back only while the mouse
  is up (`SetValueWithoutNotify`).
- **Text boxes:** `GameUi.InputField`, a copy of the game's own (`TextInput.instance`'s `TMP_InputField`, the sign and portal name box;
  else the map's pin-name box). It is a `GuiInputField` from gui_framework.dll, which mods don't reference: take it as `TMP_InputField`.
  The copy keeps the sign dialog's saved listeners (`TextInput.OnEnter`, `GuiInputField.OnInputSubmit`): replace every UnityEvent field
  by reflection right after Instantiate (inside a switched-off holder, before its Start), and destroy its `UIGamePad`, `gamepad_hint*`,
  `Localize` (it would translate a typed "$word") and `TMP_SelectionCaret`. Then SingleLine, `characterLimit`, `richText = false`, and set
  `.text` before adding listeners. Built from parts instead: build it inactive and switch it on after `textComponent`/`textViewport` are
  set (the caret is made in OnEnable). Make text boxes **once** and rebuild the rest of the window around them, or typing loses focus;
  while `GameUi.Typing`, E, Tab and your mod's keys are letters, not commands. Show what a player typed in a rich-text label through
  `GameUi.NoParse` (a "<b>" in a name stays letters).
- **Symbols:** the game's fonts lack many (★, ✓, arrows, "−", "…"): they show as empty boxes. Use words ("Hard", "Brutal"), "-" and "...",
  or the game's icons (the minimap's player marker, `Minimap.instance.m_smallMarker`, is an arrow that points up: turn it with
  `localRotation = Quaternion.Euler(0, 0, -degrees)`). Show keys the game's way, `[<color=yellow><b>$KEY_Use</b></color>]` through
  `Localization.instance.Localize`, not drawn key caps.
- **Boxes taller than the letters:** with `overflowMode` Ellipsis (or Truncate), TMP hides a whole line that is even a little taller
  than its box, and Averia's lines are about 1.3 times the font size. Make a one-line label at least 1.4 times its font size tall
  (PartyHud's names never showed in 18-high boxes at 16 pt). `GameUi.Line` does it.
- **Colours** that match the game's: text (0.94, 0.9, 0.82), dim (0.75, 0.72, 0.66), warm title (1, 0.86, 0.55), gold (1, 0.78, 0.3),
  warning (1, 0.55, 0.35), good (0.6, 0.9, 0.55); rich-text accent `#ffd27a`, notes `<size=70%><color=#bdb7a9>`. Title in Norse about
  34 pt with the braid line under it; a selected tab in gold.
- **Rebuild for layout, update numbers in place.** Keep a signature string of what decides the window's layout (which rows, which tab,
  which buttons, their states) and build again only when it differs (checking a few times a second is cheap); keep the scroll position
  across a rebuild. Numbers that change all the time (health, counts, timers, cooking %, distance) are **not** in the signature: keep the
  label or bar and set it in place several times a second (`GameUi.SetText` assigns only when the string differs: TMP rebuilds its mesh
  on every assignment; `GameUi.SetBar` moves a bar's fill). Rebuilding for every number swallows clicks mid-press. Never rebuild while
  the player types, drags a slider or holds the mouse button. A window with many live values can run its own drawing code in a
  "signature only" pass and build only when that differs. Leave item identity out of a signature for a creature's or another game's
  container (its items are new copies whenever its save data changes). If a live text that wraps no longer fits its height, rebuild then.
- **Closing:** on distance, death, `global::Menu.IsVisible()`, Escape, Tab (as a chest closes), and E again (ignore E for 0.2 s after
  opening). The E that closes it mustn't open it again: your piece's `Interact` ignores E for 0.25 s after closing, and a window opened
  from a `Player.Interact` prefix (a station, a portal) returns false (skips the game) in that time, or the closing E also runs the game's
  interact (puts an item in the station). Not while typing.
- **Long lists** (hundreds of portals or items): build only the rows in view. Fixed row height, content sized `count * rowHeight - gap`,
  rows for the visible range plus a few, filled again from the `ScrollRect`'s `onValueChanged` and in Update.
- **Fit the screen:** `GameUi.ScreenSize` is the canvas in UI units; a tall window takes the smaller of its height and that.
- **Never change the inventory from a click handler**: set a flag and act on it in `Update` (the window is mid-draw).
- **Text that may wrap** (an item's name, a long line): size it before placing what comes under it,
  `label.GetPreferredValues(text, width, 0).y`, or the next line runs into it.
- **Item slots**: the inventory's own cell, `InventoryGui.instance.m_playerGrid.m_elementPrefab` (spaced `m_elementSpace` apart). On a
  copy: `Initialize(0, 0)`; fill `m_icon`, `m_durability` (a `GuiBar`: `SetValue`), `m_quality`, `m_amount` (any text: a cost, "2-3"),
  `m_equiped`; switch off `m_queued`, `m_noteleport`, `m_food` and the `binding` text; `m_selected` is the selection frame;
  `m_tooltip.Set(item.m_shared.m_name, item.GetTooltip())` gives the game's tooltip. Clicks: its `UIInputHandler.m_onLeftClick`. Destroy
  its `UIDragHandler` (its drag handlers are wired only by the inventory's grid) and `UIGamePad`.
- **Compact items** (a cost or reward inside a line): a whole cell is 64 wide, and a cell scaled down makes its amount unreadable. Use
  the item's icon (about 28) with the amount beside it in normal text, and give the icon a `UITooltip` whose `m_tooltipPrefab` is the
  cell's (`m_elementPrefab`'s `InventoryElement.m_tooltip.m_tooltipPrefab`): the game's tooltip on hover. Keep cards short: put rewards
  on a line that has room (beside "Hard · Mountain") rather than on a row of their own.
- **Scrolling lists**: a viewport with `RectMask2D` and a clear `Image` (so the wheel works between items), a content child sized to
  what's in it, and a `ScrollRect` (vertical, Clamped). **Copy the scroll speed** from the crafting screen's recipe list (the `ScrollRect`
  whose `content` is `InventoryGui.m_recipeListRoot`): the game's wheel moves a ScrollRect only a little per notch, so a guessed
  `scrollSensitivity` (40) takes a hundred notches. **Clone its scrollbar** (`InventoryGui.m_recipeListScroll`) beside the list and set
  `verticalScrollbarVisibility = AutoHide`. Size the content **exactly** (no gap after the last item, notes measured): a few pixels too
  tall and a scrollbar appears for nothing. The `GetMouseScrollWheel` patch below keeps the wheel from zooming the camera too.
- **Progress:** a sunken groove (`root/Player/sunken`) with a fill `Image` its width the fraction (gold; green when done) and the count
  in the middle, with a dark copy one pixel down-right under it so it reads on the fill and on the groove.
- **A window that waits for the host** (the host keeps the data): say "Reading..." at first and, after a few seconds without an answer,
  why (the host needs the mod too), instead of an empty window.
- **A public static `Close()`** on your window class (and `IsOpen`), so other mods and tools can close it as Escape does: Claude Tools'
  `shoot close` calls it by reflection before a screenshot. A class of yours named `Menu` hides the game's `Menu` in your namespace: write
  `global::Menu` (the template does).
- **Drive windows from the mod's own open flags** (open, build, close in Update), so commands and other code that set the flags keep working.

**Adding to the game's own screens** (level 1 with parts of yours): a part that belongs with a game window (a search box on a chest, a
button under the inventory, a tag on an item cell) is a child of that window's own objects (`InventoryGui.m_player`, `m_container`,
`m_crafting`, the cells): it moves, scales and hides with it. Give it `LayoutElement.ignoreLayout` (the panel may have a layout group);
update it in `LateUpdate` (cells are recreated in `InventoryGrid.UpdateGui`); after a hot reload, remove the old copy's parts by name
(buttons especially: their listeners point at the old copy). `GameUi.Board` is the game's wood inside such a parent; `GameUi.Over` lays a
window over a game panel (QualityOfLife's assign menu covers the crafting panel while a chest stays open). Then Escape closes the
inventory too: `InventoryGui.Update` reads `ZInput.GetKeyDown(Escape)`, which can't be reset. Saving to the ZDO of a chest the player has
open is safe for its items (`Container.Load` skips a chest in use), but code that refuses "in use" chests must except the one you have open.

**On the big map:** draw under `Minimap.instance.m_pinRootLarge`, on a stretched layer that is its first child (the map adds pins after it,
so they draw on top). Position from `LateUpdate` (after `Minimap.UpdatePins`) by `layer.InverseTransformPoint(pin.m_uiElement.position)`;
`raycastTarget = false`; skip a pin whose `m_uiElement` is null or not under the large root (markers out of view are destroyed).

**3. IMGUI (OnGUI) only as a fallback**, made to fit: scale with `GUI.matrix` by `max(0.8, Screen.height / 1080)`; draw an opaque dark
board (0.07, 0.06, 0.05, 0.98) with a brass edge (0.62, 0.47, 0.22) (a single translucent layer lets the world show through); the game's
fonts (`Resources.FindObjectsOfTypeAll<Font>()` named Averia or Norse, once); clamp on screen, draggable by the title; draw only the visible
rows of long lists; queue button actions and run them in `Update`, never inside `OnGUI`. Atlas sprites need
`GUI.DrawTextureWithTexCoords` with `sprite.textureRect`.

## While a window is open: input

These seven patches (all in `templates/GameUi.cs`), or the game keeps playing under your window:
- `PlayerController.TakeInput` and `Player.TakeInput`: postfix `__result = false` while open.
- `GameCamera.UpdateMouseCapture`: prefix `ZCursor.LockState = None; ZCursor.Show(); return false;` (skip the game's: otherwise it locks
  the cursor every frame and snaps it to the centre on Linux). IMGUI windows also set `Cursor.lockState`/`visible` every draw.
- `ZInput.GetMouseScrollWheel`: postfix `0`, so the wheel scrolls your list instead of zooming the camera.
- `Menu.Update`: prefix that closes your window on `Escape` or `ZInput.GetButtonDown("JoyMenu")` and returns false, so the pause menu
  doesn't open too. A mod that uses Escape outside its windows too (cancelling a placement) keeps its own prefix beside it, with an "Escape
  used this frame" guard.
- `InventoryGui.Update`: prefix that, on `Inventory` or `JoyButtonY`, resets them (`ZInput.ResetButtonStatus`) and closes your window
  (unless typing): without it **Tab opens the inventory under every window** (it did under our Recycler's and Bounty Board's).
- `Minimap.InTextInput`: postfix true while your window is open: the chat (Enter), the map key (M) and other mods' hotkeys stay quiet.

Also: `InventoryGui.instance.Hide()` when opening (clicks must not reach the inventory below). Each prefix that returns false does so
**only while your window is open**. A HUD-only mod (no window) deletes the template's patch section: patches it never applies still count
in `modcheck` (a "never unpatches" warning, and a worse reload level).

## Messages and the HUD

| Use | For |
|---|---|
| `MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text)` | Small news, rules, warnings. |
| `MessageType.Center` | One important line (a countdown at `<size=150%>`, "bag full"). |
| `MessageHud.instance.ShowBiomeFoundMsg(text, false)` | Big moments, in the large Norse title. It queues: at most one every few seconds, send the rest to Center. |
| `player.Message(type, text)` | A message to a player; on another player's character it reaches them over the network. |
| A panel on the HUD | Something to keep an eye on while playing (the group's contracts, the party): build it under `Hud.instance.m_rootObject` (it hides with the HUD and scales with it), from the same parts as a window (a sunken box, labels, bars). Hide it while the inventory, the menu, the map, a store or your own window is open. Build its rows only when the set of rows or a setting changes, and update values in place each frame (`GameUi.LiveBar`, `SetText`, an arrow's `localRotation`) from `LateUpdate`, so arrows follow this frame's camera; give the panel's root its own `Canvas` so per-frame changes don't rebuild the whole HUD. The HUD hides by moving `m_rootObject` off screen, not by switching it off (`activeInHierarchy` stays true). Remove the old copy's by name after a hot reload. |
| A cloned boss bar | Progress: `Instantiate(EnemyHud.instance.m_baseHudBoss, EnemyHud.instance.m_hudRoot.transform)`, hide the parts you don't need, drive its `GuiBar`s (`SetValue`, `SetColor`). Under the HUD root it hides with the HUD. |

Hover prompts in the game's own format, localized: `"[<color=yellow><b>$KEY_Use</b></color>] Open"` through
`Localization.instance.Localize`. Tooltips: add to `UITooltip.m_text` after the game writes it each frame. Write words with
`$` keys and translations where the game does (see **valheim-mod-start**).

## 3D: pieces, items, scenery

- **Clone the closest game prefab** and change it, rather than building from nothing: it brings the right layer, collider setup, sounds,
  wear, placement rules and LOD. Register it as in **valheim-pitfalls**.
- **Materials come from the game's prefabs**, never `Shader.Find` (the game strips unused shaders; a dedicated server draws nothing):
  wood, stone and iron by name from pieces (`Look.cs` in the modelkit does this), plain colours as copies of a game material with its
  texture removed, fire from `piece_groundtorch_wood`'s particles. Destroy materials you create when you're done with them.
- **Hand-made models**: the modelkit (`BepInEx/claude/modelkit`): describe the model as boxes, cylinders and spheres in Python, look at
  it from four sides, write it as C#, build it with `ModelBuilder.cs` in the game's materials. It also draws the **game's real pieces**
  offline (`extract_meshes.py`, `preview.py`), so you can match their size and style. Read its README.
- **Scenery that isn't saved or networked** (the same for everyone, built by each client): instantiate real game prefabs with
  `ZNetView.m_forceDisableInit = true` (and `TerrainOp.m_forceDisableTerrainOps = true`) inside try/finally, then remove `Piece`,
  `WearNTear`, `ZNetView`, `Rigidbody` and the like.
- **Characters**: the game's `Player` prefab dressed with `VisEquipment` looks right; a stone look on a moving body breaks up (use a plain
  Standard-based stone).
- **World text**: `TextMeshPro` with the Norse font (fall back to the `sign` prefab's font).
- **Details flush with a surface flicker or vanish** at a distance: raise decals, text and marks clearly off it (about a centimetre or more).
- **Check it in the game**: `render <prefab> views=4` (a fresh copy, real materials, four sides), close-ups with `focus=` and `dist=`,
  `inspect <prefab>` for parts, sizes and materials. Placed pieces keep their old look until a restart; `render` always builds a new one.

## Effects and sound

- Effects: instantiate the game's `vfx_*` and `sfx_*` prefabs (`vfx_spawn`, `sfx_chest_open`, fireworks...), or a piece's own
  `EffectList` (`Door.m_openEffects.Create(position, rotation)`).
- Your own sounds: put the `AudioSource` on the game's SFX mixer group (take `outputAudioMixerGroup` from a `ZSFX` prefab's source), or it
  ignores the player's volume slider. Credit sound files' licences in the README.

## Hot reload and world changes

The game rebuilds `InventoryGui` and the HUD on every world change, and a hot reload leaves the old copy's objects behind. Pieces
already standing in the world keep the **old copy's components** after a reload: their `Interact` opens the old copy's window, which
nothing draws any more. `templates/PieceReload.cs` fixes that (see **valheim-mod-start**, "Pieces that reload while you play"). So: test
`go != null` with Unity's own null check before reusing UI; find and destroy leftovers **by name** (the old copy's types differ from the new
one's); tear down everything your mod made in `OnDestroy`; move RectTransforms only when what they depend on changed (touching them
re-runs layout); if UI code fails every frame, log once and hide the panel instead of filling the log.

## Checklist

1. Can a game window do it (StoreGui, a cloned container, the inventory grid)? Use it.
2. Otherwise start from `templates/GameUi.cs`: game parts cloned by path, all Image fields, fonts from live game texts, game colours,
   your own canvas with the inventory's scaler settings, the inventory's own cells for items.
3. Strip `UIGamePad` and gamepad hints from cloned buttons.
4. The five input patches; hide the inventory on open; close on Escape, distance, death.
5. TopLeft for small news, Center for important, biome title (rate-limited) for big moments.
6. 3D from game prefabs and materials; hand-made models through the modelkit; no `Shader.Find` without a fallback.
7. Sounds through the game's mixer.
8. IMGUI windows set the game's fonts (Averia, Norse) in **every** GUIStyle: Unity's default font is the first thing that looks wrong.
9. Survives hot reload and a world change; a `shot` and a `render` of every new thing before calling it done.

## Pictures for checking and for the mod's page

`render <prefab> views=4` for a model on its own. For a window or a piece in place, set the scene with `shoot` (a request file, or
`claude shoot ...` in the console), then `shot`:

```
shoot pose mything            a page picture of a placed piece: player beside it facing the camera, camera in front looking down
shoot near mything            stand beside it, on real floor (to press E on it: shoot use mything)
shoot find mything            where your pieces stand and which way they face
shoot tp x y z yaw pitch      stand the player there, looking that way (cheat: single player or the host)
shoot inv / shoot build       open the inventory / the build menu with the hammer
shoot hud off                 hide the HUD for a clean picture
shot 1920
shoot close                   close every window again (yours too, through its Close())
shoot hud on
```

**A piece in the world, for the mod's page:** `shoot pose <piece> [zoom] [pitch]` stands the player beside it on real floor, **turned to
face the camera**, with the camera in front of the piece, a little above and **looking down about 18°**, at a zoom fitted to its size.
Then `shoot hud off`, `shot`, `shoot hud on`. Look at the picture: in a cramped room give a smaller zoom (`shoot pose piece_x 4`) or pick
a roomier piece of the same kind; the piece should fill most of the picture, the player beside it as scale. Never move the player to a
guessed spot (`shoot tp` refuses thin air now; `shoot near <name>` stands beside something safely): a guess beside an upstairs piece
dropped the player a floor.

**A window, for the mod's page:** open it in a state worth showing (an item chosen, a list with a few rows), take the full-size shot and
crop it to the window with a little margin.

For the mod's page, take `shot` with **no width** (the full screen, sharpest), crop to what matters, and save it as PNG (or JPG at
quality 92 or more); a width shrinks it, which is fine for checking but loses detail. Look at every picture yourself before calling the look done: text cut off, Arial anywhere, a panel that lets the world show through, a
button without its frame.
