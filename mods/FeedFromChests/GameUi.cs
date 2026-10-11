using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FeedFromChests
{
    /// <summary>
    /// Windows in Valheim's own look, built from the game's own interface: the wood panel, the sunken list box, buttons, the braided line
    /// under a title, the inventory's item cells (icons, durability, quality, tooltips) and the fonts (Norse for titles, Averia for the rest),
    /// all cloned from the inventory screen. A template from Claude Tools (BepInEx/claude/templates): copy it into your mod and change the
    /// namespace and CanvasName. The same code runs the Recycler's window. Needs references to Unity.TextMeshPro, UnityEngine.UI and
    /// assembly_guiutils (the mod template has them).
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
    ///           if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || Menu.IsVisible() || useAgain || tooFarAway) { Close(); return; }
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
        private const string CanvasName = "FeedFromChestsCanvas";

        /// <summary>Whether one of your windows is open: the input patches below act only while it is.</summary>
        internal static Func<bool> IsOpen = () => false;

        internal static TMP_FontAsset Norse, Serif, Sans;
        private static Image _panel, _sunken, _braid;
        private static Button _button, _tab;
        private static GameObject _cell;
        private static float _cellSpace = 70f;

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
            if (_button == null || _panel == null || _cell == null) Debug.LogWarning($"{CanvasName}: the game's interface was not found where expected (a game update moved it?)");
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
            t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow;
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
            label.enableWordWrapping = false;
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
            label.enableWordWrapping = false;
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

        /// <summary>
        /// A compact item with an amount ("156" beside a coin): its icon and the number in readable text, with the game's item tooltip on
        /// the icon. For costs and rewards inside a line, where a whole inventory cell is too big. Returns the width it took.
        /// </summary>
        internal static float Chip(Transform parent, Vector2 pos, ItemDrop.ItemData item, string amount, float size = 28f, float fontSize = 17f)
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
            TMP_Text label = Label(parent, amount, new Vector2(pos.x + size + 3f, pos.y - (size - fontSize * 1.3f) / 2f), new Vector2(80f, size), fontSize, Text);
            label.enableWordWrapping = false;
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
                    t.enableWordWrapping = false;
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
    }

    // ---- while a window is open: the game must not play underneath it ---------------------------------------------------------------

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class GameUi_PlayerControllerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }   // no walking, attacking, building
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class GameUi_PlayerTakeInput
    {
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = false; }
    }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    internal static class GameUi_UpdateMouseCapture
    {
        // a free cursor (the game's would lock it every frame, and snap it to the centre on Linux)
        private static bool Prefix()
        {
            if (!GameUi.IsOpen()) return true;
            ZCursor.LockState = CursorLockMode.None;
            ZCursor.Show();
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class GameUi_ScrollWheel
    {
        private static void Postfix(ref float __result) { if (GameUi.IsOpen()) __result = 0f; }   // the wheel scrolls the window, not the camera
    }

    [HarmonyPatch(typeof(Menu), "Update")]
    internal static class GameUi_EscapeCloses
    {
        /// <summary>What Escape closes: set it to your window's Close.</summary>
        internal static Action Close = () => { };

        // Escape closes the window, and only that: the pause menu doesn't open too
        private static bool Prefix()
        {
            if (!GameUi.IsOpen()) return true;
            if (!(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyMenu"))) return true;
            Close();
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    internal static class GameUi_InventoryKey
    {
        // Tab would open the inventory under the window: instead it closes the window, as it closes a chest (what Escape closes), and
        // while typing in a text box it does nothing. Here, before the inventory looks, whichever runs first in the frame.
        private static void Prefix()
        {
            if (!GameUi.IsOpen() || !(ZInput.GetButtonDown("Inventory") || ZInput.GetButtonDown("JoyButtonY"))) return;
            UnityEngine.GameObject selected = UnityEngine.EventSystems.EventSystem.current != null ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            TMPro.TMP_InputField field = selected != null ? selected.GetComponent<TMPro.TMP_InputField>() : null;
            bool typing = field != null && field.isFocused;
            ZInput.ResetButtonStatus("Inventory");
            ZInput.ResetButtonStatus("JoyButtonY");
            if (!typing) GameUi_EscapeCloses.Close();
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput))]
    internal static class GameUi_InTextInput
    {
        // the game's chat (Enter) and map (M) keys, and other mods' hotkeys, stay quiet while the game says "typing in a box": so while a
        // window is open, it says so
        private static void Postfix(ref bool __result) { if (GameUi.IsOpen()) __result = true; }
    }
}
