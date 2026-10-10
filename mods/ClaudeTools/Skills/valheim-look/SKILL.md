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

**2. Build from cloned game parts** (a window of your own that still looks native). Take templates by path under
`InventoryGui.instance`, and log a warning if one is missing (a game update can move them):

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
- **Cloned buttons:** destroy `gamepad_hint*` children and every `UIGamePad` component (it binds a gamepad button to the original, so the
  clone would fire on it); replace `onClick` with a new event; reuse the child named `Text`. A cloned tab: also destroy its
  `ButtonTextColor` (it stays wired to the original tab and errors every frame) and add a fresh `Button`.
- **Colours** that match the game's: text (0.94, 0.9, 0.82), dim (0.75, 0.72, 0.66), warm title (1, 0.86, 0.55), gold (1, 0.78, 0.3),
  warning (1, 0.55, 0.35), good (0.6, 0.9, 0.55); rich-text accent `#ffd27a`, notes `<size=70%><color=#bdb7a9>`. Title in Norse about
  34 pt with the braid line under it; a selected tab in gold.
- **Rebuild rather than patch** a window when what it shows changes (check at most once a second); close it on distance, death or
  `Menu.IsVisible()`.

**3. IMGUI (OnGUI) only as a fallback**, made to fit: scale with `GUI.matrix` by `max(0.8, Screen.height / 1080)`; draw an opaque dark
board (0.07, 0.06, 0.05, 0.98) with a brass edge (0.62, 0.47, 0.22) (a single translucent layer lets the world show through); the game's
fonts (`Resources.FindObjectsOfTypeAll<Font>()` named Averia or Norse, once); clamp on screen, draggable by the title; draw only the visible
rows of long lists; queue button actions and run them in `Update`, never inside `OnGUI`. Atlas sprites need
`GUI.DrawTextureWithTexCoords` with `sprite.textureRect`.

## While a window is open: input

These five patches, or the game keeps playing under your window:
- `PlayerController.TakeInput` and `Player.TakeInput`: postfix `__result = false` while open.
- `GameCamera.UpdateMouseCapture`: prefix `ZCursor.LockState = None; ZCursor.Show(); return false;` (skip the game's: otherwise it locks
  the cursor every frame and snaps it to the centre on Linux). IMGUI windows also set `Cursor.lockState`/`visible` every draw.
- `ZInput.GetMouseScrollWheel`: postfix `0`, so the wheel scrolls your list instead of zooming the camera.
- `Menu.Update`: prefix that closes your window on `Escape` or `ZInput.GetButtonDown("JoyMenu")` and returns false, so the pause menu
  doesn't open too.

Also: `InventoryGui.instance.Hide()` when opening (clicks must not reach the inventory below). Text fields: postfix `Minimap.InTextInput`
to true while typing (other mods then ignore their hotkeys); inside the inventory, `ZInput.ResetButtonStatus("Use")`/`("Inventory")` so E
and Tab type instead of closing it. Each prefix that returns false does so **only while your window is open**.

## Messages and the HUD

| Use | For |
|---|---|
| `MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, text)` | Small news, rules, warnings. |
| `MessageType.Center` | One important line (a countdown at `<size=150%>`, "bag full"). |
| `MessageHud.instance.ShowBiomeFoundMsg(text, false)` | Big moments, in the large Norse title. It queues: at most one every few seconds, send the rest to Center. |
| `player.Message(type, text)` | A message to a player; on another player's character it reaches them over the network. |
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

The game rebuilds `InventoryGui` and the HUD on every world change, and a hot reload leaves the old copy's objects behind. So: test
`go != null` with Unity's own null check before reusing UI; find and destroy leftovers **by name** (the old copy's types differ from the new
one's); tear down everything your mod made in `OnDestroy`; move RectTransforms only when what they depend on changed (touching them
re-runs layout); if UI code fails every frame, log once and hide the panel instead of filling the log.

## Checklist

1. Can a game window do it (StoreGui, a cloned container, the inventory grid)? Use it.
2. Otherwise clone game parts by path, copy all Image fields, fonts from live game texts, game colours, your own canvas with the
   inventory's scaler settings.
3. Strip `UIGamePad` and gamepad hints from cloned buttons.
4. The five input patches; hide the inventory on open; close on Escape, distance, death.
5. TopLeft for small news, Center for important, biome title (rate-limited) for big moments.
6. 3D from game prefabs and materials; hand-made models through the modelkit; no `Shader.Find` without a fallback.
7. Sounds through the game's mixer.
8. IMGUI windows set the game's fonts (Averia, Norse) in **every** GUIStyle: Unity's default font is the first thing that looks wrong.
9. Survives hot reload and a world change; a `shot` and a `render` of every new thing before calling it done.
