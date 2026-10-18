using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RocketRoadster
{
    /// <summary>
    /// Windows in Valheim's own look, built from the game's own interface: the wood panel, the sunken list box, buttons, the braided line
    /// under a title, the inventory's item cells (icons, durability, quality, tooltips) and the fonts (Norse for titles, Averia for the rest),
    /// all cloned from the inventory screen. A template from Claude Tools (BepInEx/claude/templates): copy it into your mod and change the
    /// namespace and CanvasName. Our AICompanion, Recycler, Bounty Board, BuildOrders, FeedFromChests, QualityOfLife, PortalHub, LedgerChest and PartyHud are built
    /// with it. Needs references to Unity.TextMeshPro, UnityEngine.UI and assembly_guiutils (the mod template has them).
    ///
    /// What's in it: Window, Box, Board (wood inside something else), Over (a window laid over a game panel, inside the inventory screen),
    /// Braid, Label, Line (one line, cut with ...), Width, Button, Tab, Toggle (On/Off), Stepper (-10 -1 [n] +1 +10), Slider (the split-stack one), InputField (a copy of the game's text box) with Typing and
    /// NoParse, Cell (the inventory's item cell), Chip (icon + amount), Icon, Picture (a texture: a Steam avatar), Bar, SetBar, LiveBar (a bar you
    /// update in place), Tag, Frame, ArrowSprite, Scroll (the game's scroll speed and scrollbar), ScreenSize, HudRoot, and the patches that keep the
    /// game quiet under a window (walking, the camera, the cursor, the wheel, Escape, Tab, chat and other mods' keys).
    /// A HUD-only mod (no window) deletes the patch section at the end: patches it never applies still make modcheck's reload level worse.
    ///
    /// A window with it (see valheim-look for why each line is there):
    ///
    ///   internal static class MyWindow
    ///   {
    ///       static RectTransform _window, _list; static string _shown = ""; static float _closedAt = -1f, _openedAt;
    ///       internal static bool IsOpen => _window != null;
    ///
    ///       internal static void Open()                 // from your piece's Interact (return false if Time.unscaledTime < _closedAt + 0.25f)
    ///       {
    ///           if (!GameUi.Ready()) return;
    ///           GameUi.IsOpen = () => IsOpen;           // turns on the input patches below
    ///           if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
    ///           _openedAt = Time.unscaledTime;
    ///           Build();
    ///       }
    ///       internal static void Close()                // public static: Claude Tools' "shoot close" calls it before a screenshot
    ///       {
    ///           if (_window != null) Object.Destroy(_window.gameObject);
    ///           _window = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip();
    ///       }
    ///       internal static void Tick()                 // from your plugin's Update
    ///       {
    ///           if (_window == null) return;
    ///           bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");   // E closes it, as it does a chest
    ///           if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || global::Menu.IsVisible() || useAgain || tooFarAway) { Close(); return; }
    ///           if (Signature() != _shown) Build();     // build again only when what it shows changed
    ///       }
    ///       static void Build()
    ///       {
    ///           float scrolled = _list != null ? _list.anchoredPosition.y : 0f;
    ///           if (_window != null) Object.Destroy(_window.gameObject);
    ///           _shown = Signature();
    ///           _window = GameUi.Window("MyWindow", new Vector2(860f, 580f));
    ///           GameUi.Label(_window, "Title", new Vector2(30f, -20f), new Vector2(300f, 44f), 34f, GameUi.Warm, GameUi.Norse);
    ///           GameUi.Braid(_window, new Vector2(0f, -76f), 800f);
    ///           RectTransform box = GameUi.Box(_window, new Vector2(24f, -124f), new Vector2(470f, 426f));
    ///           _list = GameUi.Scroll(box, 8f);
    ///           ... GameUi.Cell(_list, position, item, null, item == selected, () => Select(item)) for each item; then size _list ...
    ///           GameUi.Button(_window, "Do it", new Vector2(510f, -502f), new Vector2(320f, 48f), () => pending = true);
    ///           _list.anchoredPosition = new Vector2(0f, scrolled);   // keep the scroll position
    ///       }
    ///   }
    ///
    /// Never change the player's inventory from a button's click: set a flag and do it in Tick (the window is being drawn).
    /// In your plugin's OnDestroy: MyWindow.Close(); GameUi.Destroy(); (a hot reload must not leave the window or canvas behind).
    /// </summary>
    internal static class GameUi
    {
        /// <summary>Your canvas's name: change it to your mod's (a hot reload finds and removes the old copy's by this name).</summary>
        private const string CanvasName = "RocketRoadsterCanvas";

        /// <summary>Whether one of your windows is open: the input patches below act only while it is.</summary>
        internal static Func<bool> IsOpen = () => false;

        internal static TMP_FontAsset Norse, Serif, Sans;
        private static Image _panel, _sunken, _braid;
        private static Button _button, _tab;
        private static GameObject _cell;
        private static float _cellSpace = 70f;
        private static bool _warned;

        // the game's own colours (text, dim notes, warm titles, gold, warnings, good news); in rich text: accent #ffd27a, notes #bdb7a9
        internal static readonly Color Warm = new Color(1f, 0.86f, 0.55f), Text = new Color(0.94f, 0.9f, 0.82f), Dim = new Color(0.75f, 0.72f, 0.66f);
        internal static readonly Color Gold = new Color(1f, 0.78f, 0.3f), Warn = new Color(1f, 0.55f, 0.35f), Good = new Color(0.6f, 0.9f, 0.55f);

        private static Canvas _canvas;

        /// <summary>Your own screen layer, made like the inventory's (same scaling, so the player's interface scale applies), drawn above it.</summary>
        internal static Transform Canvas
        {
            get
            {
                if (_canvas != null) { Match(); return _canvas.transform; }
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return null;
                // a hot reload leaves the old copy's canvas behind (its types differ from ours): find it by name
                foreach (Transform old in gui.transform.parent.Cast<Transform>().Where(t => t.name == CanvasName).ToList()) Object.Destroy(old.gameObject);
                Canvas like = gui.GetComponentInChildren<Canvas>(true) ?? gui.GetComponentInParent<Canvas>();
                var go = new GameObject(CanvasName, typeof(RectTransform));
                go.transform.SetParent(gui.transform.parent, false);
                _canvas = go.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = (like != null ? like.sortingOrder : 0) + 5;
                go.AddComponent<CanvasScaler>();
                go.AddComponent<GraphicRaycaster>();
                Match();
                return _canvas.transform;
            }
        }

        /// <summary>Every CanvasScaler field from the inventory's canvas (the game changes scaleFactor when the player changes the GUI scale).</summary>
        private static void Match()
        {
            InventoryGui gui = InventoryGui.instance;
            CanvasScaler mine = _canvas != null ? _canvas.GetComponent<CanvasScaler>() : null;
            CanvasScaler theirs = gui != null ? gui.GetComponentInChildren<CanvasScaler>(true) ?? gui.GetComponentInParent<CanvasScaler>() : null;
            if (mine == null || theirs == null) return;
            mine.uiScaleMode = theirs.uiScaleMode; mine.referenceResolution = theirs.referenceResolution; mine.screenMatchMode = theirs.screenMatchMode;
            mine.matchWidthOrHeight = theirs.matchWidthOrHeight; mine.scaleFactor = theirs.scaleFactor; mine.referencePixelsPerUnit = theirs.referencePixelsPerUnit;
        }

        internal static void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null; _button = null; _cell = null;
            IsOpen = () => false;
        }

        /// <summary>
        /// Finds the game's interface parts, by path under InventoryGui. The game rebuilds them on every world change, so they are looked up
        /// again then (Unity's null check notices the old ones are gone). False, with a warning in the log, if a game update moved them.
        /// </summary>
        internal static bool Ready()
        {
            if (_button != null && _panel != null && _cell != null) return true;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return false;
            Transform root = gui.transform;
            T Find<T>(string path) where T : Component { Transform t = root.Find(path); return t != null ? t.GetComponent<T>() : null; }
            _panel = Find<Image>("root/Crafting/Bkg") ?? Find<Image>("root/Player/Bkg");
            _sunken = Find<Image>("root/Player/sunken");
            _braid = Find<Image>("root/Crafting/BraidLineHorisontalMedium");
            _button = Find<Button>("root/Player/Container/TakeAll");
            _tab = Find<Button>("root/Crafting/TabsButtons/Craft");
            Norse = Find<TMP_Text>("root/Crafting/topic")?.font;
            Serif = Find<TMP_Text>("root/Crafting/TabsButtons/Craft/Text")?.font;
            Sans = Find<TMP_Text>("root/Crafting/Decription/Description")?.font ?? Serif;
            InventoryGrid grid = gui.m_playerGrid;
            if (grid != null) { _cell = grid.m_elementPrefab; _cellSpace = grid.m_elementSpace > 1f ? grid.m_elementSpace : 70f; }
            if ((_button == null || _panel == null || _cell == null) && !_warned) { _warned = true; Debug.LogWarning($"{CanvasName}: the game's interface was not found where expected (a game update moved it?)"); }
            return _button != null && _panel != null && _cell != null;
        }

        /// <summary>The distance between two of the inventory's cells (a cell is about 64 wide): lay grids out by it.</summary>
        internal static float CellSpace => _cellSpace;

        // ---- building blocks ----------------------------------------------------------------------------------------------------

        internal static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        /// <summary>A window: the game's crafting panel wood, centred on the screen. Positions inside it are from its top-left corner (y down is negative).</summary>
        internal static RectTransform Window(string name, Vector2 size)
        {
            RectTransform r = Rect(Canvas, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Copy(_panel, r.gameObject.AddComponent<Image>());
            return r;
        }

        /// <summary>A sunken box (where the game shows a list), to set things apart.</summary>
        internal static RectTransform Box(Transform parent, Vector2 pos, Vector2 size)
        {
            RectTransform r = Rect(parent, "Box", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            var img = r.gameObject.AddComponent<Image>();
            if (_sunken != null) Copy(_sunken, img); else img.color = new Color(0f, 0f, 0f, 0.35f);
            return r;
        }

        /// <summary>The braided line the game draws under a title (pos is its centre, from the parent's top middle).</summary>
        internal static void Braid(Transform parent, Vector2 pos, float width)
        {
            if (_braid == null) return;
            RectTransform r = Rect(parent, "Braid", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, _braid.rectTransform.rect.height > 1f ? _braid.rectTransform.rect.height : 16f));
            Copy(_braid, r.gameObject.AddComponent<Image>());
        }

        /// <summary>All the fields that make a game image look right (a sliced sprite without its type or multiplier looks stretched).</summary>
        private static void Copy(Image from, Image to)
        {
            if (from == null) return;
            to.sprite = from.sprite; to.type = from.type; to.material = from.material; to.color = from.color; to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
            to.fillCenter = from.fillCenter;
        }

        /// <summary>
        /// Text in the game's fonts (Averia by default; Norse for titles, never for numbers: it draws 0 as a rune). Rich text works.
        /// For text that may wrap, size it to fit before placing what comes under it: label.GetPreferredValues(text, width, 0).y.
        /// </summary>
        internal static TMP_Text Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TMP_FontAsset font = null,
                                       TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform r = Rect(parent, "Text", a, a, pos, size);
            // (off while the text is made and given its font: added to an object that is on, TextMeshPro wakes at once with no font, looks for its
            // default one, which the game doesn't have, and says so in the log every time)
            bool tWasOn = r.gameObject.activeSelf;
            r.gameObject.SetActive(false);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font ?? Sans;
            r.gameObject.SetActive(tWasOn);
            t.fontSize = fontSize; t.color = color; t.alignment = align; t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A plain button: the inventory's "Take all", with its sounds and frame, and without its gamepad binding.</summary>
        internal static Button Button(Transform parent, string text, Vector2 pos, Vector2 size, Action click, bool enabled = true, float fontSize = 18f)
        {
            Button b = Clone(_button, parent, text, pos, size, fontSize);
            b.interactable = enabled;
            Label(b).color = enabled ? Text : Dim;
            b.onClick.AddListener(() => click());
            return b;
        }

        /// <summary>
        /// A tab (the crafting screen's). The game shows the chosen tab by making it not clickable, which draws it pressed in: so does this.
        /// Its label is gold when chosen.
        /// </summary>
        internal static Button Tab(Transform parent, string text, Vector2 pos, Vector2 size, bool chosen, Action click, float fontSize = 18f)
        {
            Button b = Clone(_tab ?? _button, parent, text, pos, size, fontSize);
            Transform selected = b.transform.Find("Selected");
            if (selected != null) selected.gameObject.SetActive(false);
            b.interactable = !chosen;
            Label(b).color = chosen ? Gold : Text;
            b.onClick.AddListener(() => click());
            return b;
        }

        /// <summary>A button's label (to change its text or colour after making it).</summary>
        internal static TMP_Text Label(Button b) => b.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.activeSelf);

        private static Button Clone(Button template, Transform parent, string text, Vector2 pos, Vector2 size, float fontSize)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = "Button";
            go.SetActive(true);
            // the original's gamepad hint and UIGamePad would make a gamepad button press this one too; ButtonTextColor sets the label's
            // colour every frame (so yours would never show)
            foreach (Transform child in go.transform.Cast<Transform>().ToList())
                if (child.name.StartsWith("gamepad_hint")) Object.Destroy(child.gameObject);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            foreach (ButtonTextColor tint in go.GetComponentsInChildren<ButtonTextColor>(true)) Object.Destroy(tint);
            foreach (Localize loc in go.GetComponentsInChildren<Localize>(true)) Object.Destroy(loc);   // (it would translate a "$word" in your label)
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            Button b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();   // (not the original's "take all")
            TMP_Text label = go.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Text");
            if (label == null)
            {
                label = Label(go.transform, text, Vector2.zero, size, fontSize, Text, Serif, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));
                ((RectTransform)label.transform).pivot = new Vector2(0.5f, 0.5f);
            }
            label.text = text;
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var lr = (RectTransform)label.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(4f, 0f); lr.offsetMax = new Vector2(-4f, 0f);
            label.alignment = TextAlignmentOptions.Center;
            foreach (TMP_Text other in go.GetComponentsInChildren<TMP_Text>(true)) if (other != label) other.gameObject.SetActive(false);
            return b;
        }

        /// <summary>A picture (an item's or a trophy's icon: ItemDrop.m_itemData.GetIcon()), kept in proportion.</summary>
        internal static Image Icon(Transform parent, Vector2 pos, Vector2 size, Sprite sprite)
        {
            var img = Rect(parent, "Icon", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        /// <summary>
        /// A compact item with an amount ("156" beside a coin): its icon and the number in readable text, with the game's item tooltip on
        /// the icon. For costs and rewards inside a line, where a whole inventory cell is too big. Returns the width it took.
        /// </summary>
        internal static float Chip(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, float size = 28f, float fontSize = 17f, Color? color = null)
        {
            Image icon = Icon(parent, pos, new Vector2(size, size), item.GetIcon());
            icon.raycastTarget = true;   // (the tooltip needs the pointer)
            UITooltip cellTip = _cell != null ? _cell.GetComponent<InventoryElement>()?.m_tooltip : null;
            if (cellTip != null)
            {
                UITooltip tip = icon.gameObject.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = cellTip.m_tooltipPrefab;   // (the inventory's tooltip window)
                tip.Set(item.m_shared.m_name, item.GetTooltip());
            }
            TMP_Text label = Label(parent, amount, new Vector2(pos.x + size + 3f, pos.y - (size - fontSize * 1.3f) / 2f), new Vector2(80f, size), fontSize, color ?? Text);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float w = label.GetPreferredValues(amount, 200f, 0f).x;
            ((RectTransform)label.transform).sizeDelta = new Vector2(w + 2f, size);
            return size + 3f + w + 12f;
        }

        /// <summary>
        /// A progress bar: a sunken groove filled to <paramref name="fraction"/> in a colour (the game's gold: Gold; done: Good), with
        /// text in the middle ("3/6") that reads on the fill and on the groove alike.
        /// </summary>
        internal static RectTransform Bar(Transform parent, Vector2 pos, Vector2 size, float fraction, Color fill, string text, float fontSize = 15f)
        {
            RectTransform groove = Box(parent, pos, size);
            RectTransform bar = Rect(groove, "Fill", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(0f, 0f));
            bar.anchorMax = new Vector2(0f, 1f);   // (full height, its width the fraction)
            bar.offsetMax = new Vector2(2f + (size.x - 4f) * Mathf.Clamp01(fraction), -2f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = new Color(fill.r * 0.8f, fill.g * 0.8f, fill.b * 0.8f, 0.9f);
            img.raycastTarget = false;
            if (!string.IsNullOrEmpty(text))
            {
                // a dark copy under it, a pixel down and right: readable on the gold and on the dark groove
                foreach (bool shadow in new[] { true, false })
                {
                    TMP_Text t = Label(groove, text, new Vector2(shadow ? 1f : 0f, shadow ? -1f : 0f), size, fontSize, shadow ? new Color(0f, 0f, 0f, 0.85f) : Color.white,
                                       Sans, TextAlignmentOptions.Center);
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                }
            }
            return groove;
        }

        /// <summary>
        /// Where HUD things go (a panel that stays on screen while playing): under the game's HUD, so it hides with it (the player's
        /// hide-HUD key, cutscenes, the loading screen) and scales with it. Null before a world is loaded. Position from the top-left:
        /// anchor (0, 1), anchoredPosition (x, -y).
        /// </summary>
        internal static Transform HudRoot => Hud.instance != null && Hud.instance.m_rootObject != null ? Hud.instance.m_rootObject.transform : null;

        /// <summary>
        /// One of the inventory's own item cells (InventoryGui.m_playerGrid.m_elementPrefab), showing an item as the inventory does: icon,
        /// durability bar, quality, the equipped mark, and the game's tooltip on hover. <paramref name="amount"/> replaces the stack text
        /// (a cost, a range like "2-3"); <paramref name="selected"/> shows the selection frame; click runs on a left click (null: none).
        /// pos is the cell's top-left corner; lay cells out CellSpace apart.
        /// </summary>
        internal static InventoryElement Cell(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, bool selected, Action click)
        {
            GameObject go = Object.Instantiate(_cell, parent, false);
            go.name = "Cell";
            go.SetActive(true);
            // only clicks: dragging belongs to the inventory's own grid (its handlers aren't wired here) and gamepad bindings to the original
            foreach (UIDragHandler drag in go.GetComponentsInChildren<UIDragHandler>(true)) Object.Destroy(drag);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;

            // what InventoryGrid.UpdateGui does for one cell
            InventoryElement e = go.GetComponent<InventoryElement>();
            e.Initialize(0, 0);
            Transform binding = go.transform.Find("binding");   // (the hotbar's 1-8)
            if (binding != null && binding.GetComponent<TMP_Text>() is TMP_Text b) b.enabled = false;
            e.m_icon.enabled = true;
            e.m_icon.sprite = item.GetIcon();
            e.m_icon.color = Color.white;
            bool worn = item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability();
            e.m_durability.gameObject.SetActive(worn);
            if (worn) e.m_durability.SetValue(item.GetDurabilityPercentage());
            e.m_equiped.enabled = item.m_equipped;
            e.m_queued.enabled = false;
            e.m_noteleport.enabled = false;
            e.m_food.enabled = false;
            e.m_quality.enabled = amount == null && item.m_shared.m_maxQuality > 1;
            if (e.m_quality.enabled) e.m_quality.text = item.m_quality.ToString();
            e.m_amount.enabled = amount != null || item.m_shared.m_maxStackSize > 1;
            e.m_amount.text = amount ?? $"{item.m_stack}/{item.m_shared.m_maxStackSize}";
            if (e.m_selected != null) e.m_selected.SetActive(selected);
            e.m_tooltip.Set(item.m_shared.m_name, item.GetTooltip());

            UIInputHandler input = go.GetComponentInChildren<UIInputHandler>(true);
            if (input != null && click != null) input.m_onLeftClick = _ => click();
            return e;
        }

        /// <summary>
        /// A scrolling area filling a box (inset from its edge): returns the content to put things in. Set the content's height to what
        /// you put in it (content.sizeDelta = new Vector2(content.sizeDelta.x, height)); the mouse wheel scrolls it (with the patch below).
        /// </summary>
        internal static RectTransform Scroll(RectTransform box, float inset)
        {
            // the crafting screen's recipe list: its scroll speed (the game's mouse wheel moves a ScrollRect only a little per notch, so a
            // guessed sensitivity scrolls far too slowly) and its scrollbar
            InventoryGui gui = InventoryGui.instance;
            ScrollRect like = gui != null ? gui.GetComponentsInChildren<ScrollRect>(true).FirstOrDefault(s => s.content == gui.m_recipeListRoot) : null;
            Scrollbar barTemplate = gui != null ? gui.m_recipeListScroll : null;
            float barW = barTemplate != null ? Mathf.Max(8f, ((RectTransform)barTemplate.transform).rect.width) : 0f;

            RectTransform view = Rect(box, "View", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(inset, -inset),
                                      box.sizeDelta - new Vector2(inset * 2f + (barW > 0f ? barW + 4f : 0f), inset * 2f));
            view.gameObject.AddComponent<RectMask2D>();
            var hit = view.gameObject.AddComponent<Image>();   // (clear: catches the mouse wheel over the gaps between things)
            hit.color = Color.clear;
            RectTransform content = Rect(view, "Content", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(view.sizeDelta.x, 0f));
            ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = view;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = like != null && like.scrollSensitivity > 0f ? like.scrollSensitivity : 300f;
            scroll.inertia = false;

            if (barTemplate != null)
            {
                GameObject bar = Object.Instantiate(barTemplate.gameObject, box, false);
                bar.name = "Scrollbar";
                bar.SetActive(true);
                foreach (UIGamePad pad in bar.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
                var br = (RectTransform)bar.transform;
                br.anchorMin = new Vector2(1f, 0f); br.anchorMax = new Vector2(1f, 1f); br.pivot = new Vector2(1f, 0.5f);
                br.sizeDelta = new Vector2(barW, -inset * 2f);
                br.anchoredPosition = new Vector2(-inset * 0.5f, 0f);
                Scrollbar sb = bar.GetComponent<Scrollbar>();
                sb.onValueChanged = new Scrollbar.ScrollEvent();   // (not the recipe list's)
                scroll.verticalScrollbar = sb;
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;   // only when there's more than fits
            }
            return content;
        }


        // ---- controls: on/off, numbers ---------------------------------------------------------------------------------------------

        /// <summary>
        /// An on/off setting (the inventory's one tick box, the PvP switch InventoryGui.m_pvp, is untried as a clone): a plain button that says its state ("Auto-feed: On"), its
        /// label gold when on. With an <paramref name="icon"/> (an item's), the icon sits at the button's left, greyed when off, and the
        /// label is left-aligned beside it: a list of items to allow, one button each. click runs from the click: change the game in Update.
        /// </summary>
        internal static Button Toggle(Transform parent, string text, bool on, Vector2 pos, Vector2 size, Action click, float fontSize = 17f, Sprite icon = null)
        {
            Button b = Button(parent, text + ": " + Localization.instance.Localize(on ? "$hud_on" : "$hud_off"), pos, size, click, true, fontSize);
            TMP_Text label = Label(b);
            label.color = on ? Gold : Text;
            if (icon != null)
            {
                float s = Mathf.Max(16f, size.y - 12f);
                Image img = Icon(b.transform, new Vector2(8f, -(size.y - s) / 2f), new Vector2(s, s), icon);
                img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                ((RectTransform)label.transform).offsetMin = new Vector2(s + 14f, 0f);
                label.alignment = TextAlignmentOptions.Left;
            }
            return b;
        }

        /// <summary>
        /// A number setting: the game's buttons around the value in a sunken box, "-10  -1  [ 25 ]  +1  +10" for steps {10, 1} (larger
        /// first). The value is in Averia (Norse draws 0 as a rune). change gets the signed step (-10, +1...) and runs from the click: change
        /// the game in Update. Returns the value's label, so it can be updated in place without building the window again; its width is
        /// StepperWidth.
        /// </summary>
        internal static TMP_Text Stepper(Transform parent, Vector2 pos, float height, string value, int[] steps, Action<int> change,
                                         float buttonW = 48f, float valueW = 76f, float fontSize = 16f)
        {
            float x = pos.x;
            foreach (int step in steps)
            {
                int d = -step;
                Button(parent, "-" + step, new Vector2(x, pos.y), new Vector2(buttonW, height), () => change(d), true, fontSize);
                x += buttonW + 4f;
            }
            RectTransform box = Box(parent, new Vector2(x, pos.y), new Vector2(valueW, height));
            TMP_Text label = Label(box, value, Vector2.zero, new Vector2(valueW, height), fontSize + 2f, Text, Sans, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            x += valueW + 4f;
            foreach (int step in steps.Reverse())
            {
                int d = step;
                Button(parent, "+" + step, new Vector2(x, pos.y), new Vector2(buttonW, height), () => change(d), true, fontSize);
                x += buttonW + 4f;
            }
            return label;
        }

        /// <summary>How wide a Stepper with this many steps is.</summary>
        internal static float StepperWidth(int steps, float buttonW = 48f, float valueW = 76f) => steps * 2f * (buttonW + 4f) + valueW;

        /// <summary>One line of text that never wraps: what doesn't fit ends in "..." (a name in a card, a line on a banner).</summary>
        internal static TMP_Text Line(Transform parent, string text, Vector2 pos, float width, float fontSize, Color color, TMP_FontAsset font = null,
                                      TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Vector2? anchor = null)
        {
            // The box is taller than the line: "..." (Ellipsis) hides a line taller than its box, and Chinese, Japanese and Korean come from
            // the game's fallback font, whose lines are taller than its own fonts'. A box grown for them keeps the text where it was: a centred
            // line stays centred, a bottom one at the bottom.
            float line = fontSize * 1.35f, box = fontSize * 1.8f;
            int v = (int)align & 0xFF00;
            if (v != 0x0100) pos.y += v == 0x0400 ? box - line : (box - line) / 2f;   // (0x0100 top, 0x0400 bottom, the rest centred)
            TMP_Text t = Label(parent, text, pos, new Vector2(width, box), fontSize, color, font, align, anchor);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>How wide a line of text is in the game's body font (to size a panel around it).</summary>
        internal static float Width(TMP_Text like, string text) => like.GetPreferredValues(text, 4000f, 0f).x;

        /// <summary>
        /// A picture from a texture (a blueprint's photo), fitted inside the box and kept in proportion, centred. The texture stays the
        /// caller's: it is not destroyed with the picture.
        /// </summary>
        internal static RawImage Picture(Transform parent, Vector2 pos, Vector2 size, Texture texture)
        {
            RectTransform box = Rect(parent, "Picture", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            Vector2 fit = size;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                float scale = Mathf.Min(size.x / texture.width, size.y / texture.height);
                fit = new Vector2(texture.width * scale, texture.height * scale);
            }
            RectTransform r = Rect(box, "Image", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, fit);
            var img = r.gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.raycastTarget = false;
            img.enabled = texture != null;
            return img;
        }

        /// <summary>
        /// A slider: the inventory's split-stack slider (InventoryGui.m_splitDialog.m_splitSlider), with its own look. Values snap to
        /// <paramref name="step"/>; <paramref name="changed"/> runs with each new value while it is dragged. If a game update moved the
        /// slider, "-" and "+" buttons at either end do the same instead (and null is returned).
        /// </summary>
        internal static Slider Slider(Transform parent, Vector2 pos, Vector2 size, float min, float max, float value, float step, Action<float> changed)
        {
            float Snap(float v) => Mathf.Clamp(step > 0f ? Mathf.Round(v / step) * step : v, min, max);
            Slider template = InventoryGui.instance != null && InventoryGui.instance.m_splitDialog != null ? InventoryGui.instance.m_splitDialog.m_splitSlider : null;
            if (template == null)
            {
                float current = Snap(value);
                Button(parent, "-", pos, new Vector2(size.y + 8f, size.y), () => { current = Snap(current - step); changed(current); });
                Button(parent, "+", new Vector2(pos.x + size.x - size.y - 8f, pos.y), new Vector2(size.y + 8f, size.y), () => { current = Snap(current + step); changed(current); });
                return null;
            }
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = "Slider";
            go.SetActive(true);
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("gamepad_hint")).ToList()) Object.Destroy(child.gameObject);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(pos.x, pos.y - size.y / 2f);
            r.sizeDelta = size;
            Slider s = go.GetComponent<Slider>();
            s.onValueChanged = new Slider.SliderEvent();   // (not the split dialog's)
            s.navigation = new Navigation { mode = Navigation.Mode.None };   // (arrow keys don't move it)
            s.wholeNumbers = false;
            s.minValue = min; s.maxValue = max;
            s.SetValueWithoutNotify(Snap(value));
            s.interactable = true;
            s.onValueChanged.AddListener(v =>
            {
                float snapped = Snap(v);
                if (!Mathf.Approximately(snapped, v)) s.SetValueWithoutNotify(snapped);
                changed(snapped);
            });
            return s;
        }

        /// <summary>
        /// The screen's size in the canvas's units (what the player's interface scale leaves for a window): size a tall window to fit it,
        /// Mathf.Min(wanted, ScreenSize.y - 40). Worked out from the scaler's settings, so it is right on the frame the canvas is made.
        /// </summary>
        internal static Vector2 ScreenSize
        {
            get
            {
                Transform t = Canvas;
                CanvasScaler s = t != null ? t.GetComponent<CanvasScaler>() : null;
                var screen = new Vector2(Screen.width, Screen.height);
                float k = 1f;
                if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) k = s.scaleFactor;
                else if (s != null && s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && s.referenceResolution.x > 0f && s.referenceResolution.y > 0f)
                {
                    float w = screen.x / s.referenceResolution.x, h = screen.y / s.referenceResolution.y;
                    k = s.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand ? Mathf.Min(w, h)
                      : s.screenMatchMode == CanvasScaler.ScreenMatchMode.Shrink ? Mathf.Max(w, h)
                      : Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w, 2f), Mathf.Log(h, 2f), s.matchWidthOrHeight));
                }
                else if (_canvas != null) k = _canvas.scaleFactor;
                return screen / Mathf.Max(0.01f, k);
            }
        }

        /// <summary>
        /// Changes a bar made by Bar in place (a value that changes all the time: health, a timer), without building the window again.
        /// Only what changed is touched (moving a RectTransform makes the canvas lay out again).
        /// </summary>
        internal static void SetBar(RectTransform bar, float fraction, string text = null, Color? fill = null)
        {
            if (bar == null) return;
            var f = bar.Find("Fill") as RectTransform;
            if (f != null)
            {
                var want = new Vector2(2f + (bar.sizeDelta.x - 4f) * Mathf.Clamp01(fraction), -2f);
                if (f.offsetMax != want) f.offsetMax = want;
                if (fill != null && f.GetComponent<Image>() is Image img)
                {
                    Color c = fill.Value, shade = new Color(c.r * 0.8f, c.g * 0.8f, c.b * 0.8f, 0.9f);
                    if (img.color != shade) img.color = shade;
                }
            }
            if (text != null)
                foreach (TMP_Text t in bar.GetComponentsInChildren<TMP_Text>(true))
                    if (t.text != text) t.text = text;
        }

        // ---- parts of the game's own screens ------------------------------------------------------------------------------------

        /// <summary>
        /// A board of the game's panel wood inside something else (Window makes one on its own in the middle of the screen): a part that
        /// hangs off a game window, say. Positions from the parent's top-left corner.
        /// </summary>
        internal static RectTransform Board(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            RectTransform r = Rect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size);
            Copy(_panel, r.gameObject.AddComponent<Image>());
            return r;
        }

        /// <summary>
        /// A window laid over one of the inventory screen's own panels (InventoryGui.m_crafting: the crafting panel), for a menu that belongs
        /// with the open inventory or chest: it is made inside the inventory's interface, just after that panel, so it moves, scales and hides
        /// with the inventory and covers the panel (its clicks don't reach it). It grows right and down to at least minSize. Don't hide the
        /// inventory for it, and close it when the inventory closes. A hot reload's leftover of the same name is removed.
        /// </summary>
        internal static RectTransform Over(RectTransform panel, string name, Vector2 minSize)
        {
            Transform parent = panel.parent;
            foreach (Transform old in parent.Cast<Transform>().Where(t => t.name == name).ToList()) Object.Destroy(old.gameObject);
            // the area the panel and its background take (its background can reach a little past it)
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            Vector2 min = parent.InverseTransformPoint(corners[0]), max = parent.InverseTransformPoint(corners[2]);
            Transform bkg = panel.Find("Bkg");
            if (bkg is RectTransform b)
            {
                b.GetWorldCorners(corners);
                min = Vector2.Min(min, parent.InverseTransformPoint(corners[0]));
                max = Vector2.Max(max, parent.InverseTransformPoint(corners[2]));
            }
            Vector2 size = Vector2.Max(max - min, minSize);
            Rect area = ((RectTransform)parent).rect;
            RectTransform r = Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(min.x, max.y) - area.center, size);
            r.SetSiblingIndex(panel.GetSiblingIndex() + 1);
            Copy(_panel, r.gameObject.AddComponent<Image>());
            return r;
        }

        /// <summary>Makes a rect fill its parent, inset by the given offsets (left/bottom, right/top as negative).</summary>
        internal static RectTransform Stretch(RectTransform r, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = offsetMin; r.offsetMax = offsetMax;
            return r;
        }

        /// <summary>
        /// A small coloured tag with a word on it (a marker on an inventory cell, a label in a row of tags), as wide as its text. anchor is
        /// also its pivot (default top-left). Nothing in it catches the mouse. Its width: tag.sizeDelta.x.
        /// </summary>
        internal static RectTransform Tag(Transform parent, string text, Vector2 pos, float height, float fontSize, Color fill, Color textColor, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform r = Rect(parent, "Tag", a, a, pos, new Vector2(height, height));
            var img = r.gameObject.AddComponent<Image>();
            img.color = fill;
            img.raycastTarget = false;
            TMP_Text t = Label(r, text, Vector2.zero, new Vector2(height, height), fontSize, textColor, Sans, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch((RectTransform)t.transform, Vector2.zero, Vector2.zero);
            float pad = Mathf.Max(3f, height * 0.3f);
            r.sizeDelta = new Vector2(Mathf.Max(height, t.GetPreferredValues(text, 1000f, 0f).x + pad * 2f), height);
            return r;
        }

        /// <summary>A rectangle placed from its parent's top-left corner, x right and y down (as IMGUI and a layout on paper count).</summary>
        internal static RectTransform At(Transform parent, string name, float x, float y, float w, float h) =>
            Rect(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));

        /// <summary>A rectangle stretched over its parent, its edges moved by the offsets (positive grows it: left, bottom, right, top).</summary>
        internal static RectTransform Stretch(Transform parent, string name, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            RectTransform r = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(-left, -bottom); r.offsetMax = new Vector2(right, top);
            return r;
        }

        /// <summary>A plain coloured rectangle (no sprite): a backing behind a translucent box, a line, a coloured square.</summary>
        internal static Image Plain(RectTransform r, Color color)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// A texture on screen (a Steam profile picture, a camera's picture): a RawImage, cropped to fill its square as IMGUI's ScaleAndCrop
        /// did. It does not own the texture: whoever made it destroys it. Change it later with <see cref="Show(RawImage, Texture, Color)"/>.
        /// </summary>
        internal static RawImage Picture(Transform parent, Vector2 pos, Vector2 size)
        {
            var img = Rect(parent, "Picture", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            img.enabled = false;
            return img;
        }

        /// <summary>Show a texture (or nothing, for null) in a <see cref="Picture"/>, cropped to fill it; only touches it when something changed.</summary>
        internal static void Show(RawImage img, Texture texture, Color tint)
        {
            if (img == null) return;
            if (img.color != tint) img.color = tint;
            if (img.texture == texture) return;
            img.texture = texture;
            img.enabled = texture != null;
            if (texture == null || texture.width <= 0 || texture.height <= 0) return;
            Vector2 size = img.rectTransform.rect.size;
            float have = texture.width / (float)texture.height, want = size.y > 0f ? size.x / size.y : 1f;
            img.uvRect = have > want ? new Rect(0.5f - want / have / 2f, 0f, want / have, 1f) : new Rect(0f, 0.5f - have / want / 2f, 1f, have / want);
        }

        /// <summary>Set a text only when it differs (TextMeshPro rebuilds its mesh on every assignment, even of the same string).</summary>
        internal static void SetText(TMP_Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        /// <summary>
        /// The minimap's arrow for the player (it points up when not turned): a direction arrow in the game's look. The game's fonts have no
        /// arrow signs. Turn its RectTransform with localRotation = Quaternion.Euler(0, 0, -degrees) (degrees clockwise from up). Null
        /// before the minimap exists.
        /// </summary>
        internal static Sprite ArrowSprite()
        {
            Minimap map = Minimap.instance;
            Image marker = map != null && map.m_smallMarker != null ? map.m_smallMarker.GetComponentInChildren<Image>(true) : null;
            return marker != null ? marker.sprite : null;
        }

        /// <summary>
        /// A thin frame around a rectangle (four plain lines), outside it by <paramref name="outset"/>: a coloured edge that says "this one is
        /// you" or pulses red when someone is in trouble. Colour it in place each frame with <see cref="Edge.SetColor"/> (alpha 0 hides it).
        /// </summary>
        internal static Edge Frame(Transform parent, float width, float outset, Color color)
        {
            var e = new Edge();
            float o = outset, w = width;
            RectTransform Side(string name, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                RectTransform r = Rect(parent, name, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                r.anchorMin = min; r.anchorMax = max; r.offsetMin = offMin; r.offsetMax = offMax;
                return r;
            }
            e.Sides = new[]
            {
                Plain(Side("EdgeTop", new Vector2(0f, 1f), Vector2.one, new Vector2(-o, o - w), new Vector2(o, o)), color),
                Plain(Side("EdgeBottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(-o, -o), new Vector2(o, w - o)), color),
                Plain(Side("EdgeLeft", Vector2.zero, new Vector2(0f, 1f), new Vector2(-o, w - o), new Vector2(w - o, o - w)), color),
                Plain(Side("EdgeRight", new Vector2(1f, 0f), Vector2.one, new Vector2(o - w, w - o), new Vector2(o, o - w)), color),
            };
            e.SetColor(color, true);
            return e;
        }

        internal sealed class Edge
        {
            internal Image[] Sides;
            private Color _color;

            internal void SetColor(Color color, bool force = false)
            {
                if (!force && color == _color) return;
                _color = color;
                foreach (Image side in Sides)
                {
                    side.color = color;
                    side.enabled = color.a > 0.01f;
                }
            }
        }

        /// <summary>
        /// A bar you update in place (health, stamina, a timer), for things that change every frame: GameUi.Bar builds a fixed one, and
        /// building again every frame makes garbage and redraws the whole canvas. The groove is the game's sunken box (a plain dark one when
        /// thinner than 10, where the box's sliced edges don't fit), the fill a plain colour; optionally a pale trail behind the fill (recent
        /// damage, as the game's own health bar shows it) and text in the middle with a dark copy under it. Then each frame:
        /// meter.Set(fraction, trail), meter.SetColor(c), meter.SetText("78 / 78"); each only touches Unity when the value changed.
        /// </summary>
        internal static Meter LiveBar(Transform parent, Vector2 pos, Vector2 size, Color fill, bool trail = false, float fontSize = 0f)
        {
            var m = new Meter();
            bool thick = size.y >= 10f && _sunken != null;
            if (thick) { m.Groove = Box(parent, pos, size); m.Groove.GetComponent<Image>().raycastTarget = false; }
            else { m.Groove = Rect(parent, "Groove", new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size); Plain(m.Groove, new Color(0f, 0f, 0f, 0.6f)); }
            float inset = thick ? 2f : size.y >= 4f ? 1f : 0f;   // (a line of 2 or 3: the fill takes its whole height)
            RectTransform inner = Stretch(m.Groove, "Inner", -inset, -inset, -inset, -inset);
            Image Fill(string name, Color color)
            {
                RectTransform r = Rect(inner, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(0f, 1f); r.offsetMin = r.offsetMax = Vector2.zero;
                return Plain(r, color);
            }
            if (trail) m.Trail = Fill("Trail", new Color(1f, 0.92f, 0.85f, 0.55f));
            m.Fill = Fill("Fill", fill);
            if (fontSize > 0f)
            {
                foreach (bool shadow in new[] { true, false })
                {
                    TMP_Text t = Label(m.Groove, "", Vector2.zero, size, fontSize, shadow ? new Color(0f, 0f, 0f, 0.85f) : Color.white, Sans, TextAlignmentOptions.Center);
                    var tr = (RectTransform)t.transform;
                    tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                    tr.offsetMin = shadow ? new Vector2(1f, -1f) : Vector2.zero; tr.offsetMax = shadow ? new Vector2(1f, -1f) : Vector2.zero;
                    t.textWrappingMode = TextWrappingModes.NoWrap;
                    t.overflowMode = TextOverflowModes.Overflow;
                    if (shadow) m.Shadow = t; else m.Text = t;
                }
            }
            m.Set(0f, 0f, true);
            return m;
        }

        internal sealed class Meter
        {
            internal RectTransform Groove;
            internal Image Fill, Trail;
            internal TMP_Text Text, Shadow;
            private float _fill = -1f, _trail = -1f;

            /// <summary>The fill (0 to 1) and, with a trail, where the trail reaches (it never shows below the fill).</summary>
            internal void Set(float fraction, float trail = -1f, bool force = false)
            {
                fraction = Mathf.Clamp01(fraction);
                if (force || Mathf.Abs(fraction - _fill) > 0.0005f)
                {
                    _fill = fraction;
                    Fill.rectTransform.anchorMax = new Vector2(fraction, 1f);
                    Fill.enabled = fraction > 0.002f;
                }
                if (Trail == null) return;
                trail = Mathf.Clamp01(Mathf.Max(trail, _fill));
                if (force || Mathf.Abs(trail - _trail) > 0.0005f)
                {
                    _trail = trail;
                    Trail.rectTransform.anchorMax = new Vector2(trail, 1f);
                }
                Trail.enabled = _trail > _fill + 0.002f;
            }

            internal void SetColor(Color color) { if (Fill.color != color) Fill.color = color; }

            internal void SetText(string text)
            {
                GameUi.SetText(Text, text ?? "");
                GameUi.SetText(Shadow, text ?? "");
            }

            internal void SetActive(bool on) { if (Groove.gameObject.activeSelf != on) Groove.gameObject.SetActive(on); }
        }

        // ---- text boxes --------------------------------------------------

        /// <summary>
        /// A text box: a copy of the game's own (the sign and portal name box, TextInput; else the map's pin-name box), so it has the
        /// game's look, caret, selection colour and Ctrl+Backspace; built from TMP parts in the game's font if neither is found.
        /// <paramref name="changed"/> runs on every edit, <paramref name="submit"/> on Enter (both while the window is being drawn: set
        /// flags, act in your Tick). While the player types in it, <see cref="Typing"/> is true: don't close on E then (the patches below
        /// already keep Tab and other mods' hotkeys quiet). The text is the player's: show it elsewhere with <see cref="NoParse"/>.
        /// </summary>
        internal static TMP_InputField InputField(Transform parent, Vector2 pos, Vector2 size, string text, string placeholder, int limit,
                                                  Action<string> changed, Action<string> submit = null, float fontSize = 18f)
        {
            TMP_InputField like = TextInput.instance != null ? TextInput.instance.GetComponentInChildren<TMP_InputField>(true) : null;
            if (like == null && Minimap.instance != null) like = Minimap.instance.GetComponentsInChildren<TMP_InputField>(true).FirstOrDefault();
            if (like == null) Debug.LogWarning($"{CanvasName}: the game's text box was not found (a game update moved it?): building one");

            // made inside a switched-off holder, so nothing on the copy wakes up before it is cleaned
            var holder = new GameObject(CanvasName + "Holder");
            holder.SetActive(false);
            TMP_InputField field;
            if (like != null)
            {
                GameObject copy = Object.Instantiate(like.gameObject, holder.transform, false);
                field = copy.GetComponent<TMP_InputField>();
                // the original's events call the game's dialog (TextInput.OnEnter, Hide...) through listeners saved with it: fresh ones
                // for the copy. (The game's GuiInputField adds its own in Start, which runs later, onto these.)
                for (Type t = field.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (typeof(UnityEventBase).IsAssignableFrom(f.FieldType) && !f.FieldType.IsAbstract)
                            try { f.SetValue(field, Activator.CreateInstance(f.FieldType)); } catch (Exception) { }
                foreach (UIGamePad pad in copy.GetComponentsInChildren<UIGamePad>(true)) Object.DestroyImmediate(pad);
                foreach (Transform child in copy.GetComponentsInChildren<Transform>(true).Where(c => c != copy.transform && c.name.StartsWith("gamepad_hint")).ToList())
                    if (child != null) Object.DestroyImmediate(child.gameObject);
                // the game's Localize (if any) would turn a "$word" the player typed into a translation on its first frame
                foreach (Localize loc in copy.GetComponentsInChildren<Localize>(true)) Object.DestroyImmediate(loc);
                // a caret the original made for itself when it was shown (the copy makes its own)
                foreach (TMP_SelectionCaret caret in copy.GetComponentsInChildren<TMP_SelectionCaret>(true)) Object.DestroyImmediate(caret.gameObject);
            }
            else field = BuildInputField(holder.transform);

            GameObject go = field.gameObject;
            go.name = "InputField";
            go.SetActive(true);
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            Object.Destroy(holder);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            r.localScale = Vector3.one;
            r.localRotation = Quaternion.identity;

            field.contentType = TMP_InputField.ContentType.Standard;
            field.lineType = TMP_InputField.LineType.SingleLine;   // (Enter submits; the sign box's copy could take several lines)
            field.characterLimit = limit;
            field.interactable = true;
            field.richText = false;   // (a typed "<b>" stays letters)
            foreach (TMP_Text t in new[] { field.textComponent, field.placeholder as TMP_Text })
            {
                if (t == null) continue;
                if (t.font == null) t.font = Sans;
                t.fontSize = fontSize;
                t.enableAutoSizing = false;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.alignment = TextAlignmentOptions.MidlineLeft;
            }
            if (field.placeholder is TMP_Text hint) hint.text = placeholder ?? "";
            else if (field.placeholder != null) field.placeholder.gameObject.SetActive(false);
            field.text = text ?? "";   // (before the listeners: setting it is no edit)
            if (changed != null) field.onValueChanged.AddListener(s => changed(s ?? ""));
            if (submit != null) field.onSubmit.AddListener(s => submit(s ?? ""));
            return field;
        }

        /// <summary>A text box from TMP parts in the game's font and sunken look, if the game's own can't be found.</summary>
        private static TMP_InputField BuildInputField(Transform holder)
        {
            RectTransform box = Rect(holder, "InputField", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(200f, 36f));
            var bg = box.gameObject.AddComponent<Image>();
            if (_sunken != null) Copy(_sunken, bg); else bg.color = new Color(0f, 0f, 0f, 0.45f);
            RectTransform area = Rect(box, "Text Area", Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            area.anchorMax = Vector2.one; area.offsetMin = new Vector2(10f, 2f); area.offsetMax = new Vector2(-10f, -2f);
            area.gameObject.AddComponent<RectMask2D>();
            TMP_Text Part(string name, Color color)
            {
                TMP_Text t = Label(area, "", Vector2.zero, Vector2.zero, 18f, color, Sans, TextAlignmentOptions.MidlineLeft, Vector2.zero);
                t.name = name;
                var tr = (RectTransform)t.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                return t;
            }
            TMP_Text placeholder = Part("Placeholder", Dim);
            placeholder.fontStyle = FontStyles.Italic;
            TMP_Text text = Part("Text", Text);
            TMP_InputField field = box.gameObject.AddComponent<TMP_InputField>();   // (switched off: it wakes up with its parts in place)
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.targetGraphic = bg;
            field.fontAsset = Sans;
            field.customCaretColor = true;
            field.caretColor = Text;
            field.selectionColor = new Color(1f, 0.78f, 0.3f, 0.35f);
            return field;
        }

        /// <summary>True while the player is typing in a text box (yours or the game's): E and other keys are letters then.</summary>
        internal static bool Typing
        {
            get
            {
                EventSystem events = EventSystem.current;
                GameObject selected = events != null ? events.currentSelectedGameObject : null;
                TMP_InputField field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        /// <summary>Text the player wrote (a name), shown as it is inside a rich-text label: a "&lt;b&gt;" in it stays letters.</summary>
        internal static string NoParse(string text) => "<noparse>" + (text ?? "").Replace("</noparse>", "") + "</noparse>";
    }
    // (the template's patches that stop the game playing under an open window are left out: these mods only draw the speed gauge, never a window)
}
