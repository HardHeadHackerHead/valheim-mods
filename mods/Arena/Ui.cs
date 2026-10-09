using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Arena
{
    /// <summary>
    /// Builds windows in Valheim's own look from the game's own interface: the wood panels, the tab buttons and plain buttons, the braided
    /// line under a title and the fonts (Norse for titles, Averia for the rest) are all taken from the inventory screen, so the Arena's menus
    /// look and sound like part of the game.
    /// </summary>
    internal static class Ui
    {
        internal static TMP_FontAsset Norse, Serif, Sans;
        private static Image _panel, _sunken, _braid;
        private static Button _button, _tab;
        private static Color _bright = new Color(1f, 0.86f, 0.55f), _text = new Color(0.94f, 0.9f, 0.82f), _dim = new Color(0.75f, 0.72f, 0.66f);

        internal static Color Gold => new Color(1f, 0.78f, 0.3f);
        internal static Color Warm => _bright;
        internal static Color Text => _text;
        internal static Color Dim => _dim;
        internal static Color Warn => new Color(1f, 0.55f, 0.35f);
        internal static Color Good => new Color(0.6f, 0.9f, 0.55f);

        private static Canvas _canvas;

        /// <summary>The Arena's own screen layer, made like the inventory's (same scaling, so the game's interface scale setting applies), drawn above it.</summary>
        internal static Transform Canvas
        {
            get
            {
                if (_canvas != null) { Match(); return _canvas.transform; }
                InventoryGui gui = InventoryGui.instance;
                if (gui == null) return null;
                Canvas like = gui.GetComponentInChildren<Canvas>(true) ?? gui.GetComponentInParent<Canvas>();
                var go = new GameObject("ArenaCanvas", typeof(RectTransform));
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
        }

        internal static bool Ready()
        {
            if (_button != null && _panel != null) return true;
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
            if (_button == null || _tab == null || _panel == null) Plugin.Log.LogWarning("The game's interface was not found where expected: the Arena's menus may look plain");
            return _button != null && _panel != null;
        }

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

        /// <summary>A window: the game's crafting panel wood, centred on the screen.</summary>
        internal static RectTransform Window(string name, Vector2 size)
        {
            RectTransform r = Rect(Canvas, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = r.gameObject.AddComponent<Image>();
            Copy(_panel, img);
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

        internal static void Braid(Transform parent, Vector2 pos, float width)
        {
            if (_braid == null) return;
            RectTransform r = Rect(parent, "Braid", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, _braid.rectTransform.rect.height > 1f ? _braid.rectTransform.rect.height : 16f));
            Copy(_braid, r.gameObject.AddComponent<Image>());
        }

        private static void Copy(Image from, Image to)
        {
            if (from == null) return;
            to.sprite = from.sprite; to.type = from.type; to.material = from.material; to.color = from.color; to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
            to.fillCenter = from.fillCenter;
        }

        internal static TMP_Text Label(Transform parent, string text, Vector2 pos, Vector2 size, float fontSize, Color color, TMP_FontAsset font = null,
                                       TextAlignmentOptions align = TextAlignmentOptions.TopLeft, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform r = Rect(parent, "Text", a, a, pos, size);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font ?? Sans;
            t.fontSize = fontSize; t.color = color; t.alignment = align; t.richText = true;
            t.enableWordWrapping = true; t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A plain button (the inventory's "Take all").</summary>
        internal static Button Button(Transform parent, string text, Vector2 pos, Vector2 size, Action click, bool enabled = true)
        {
            Button b = Clone(_button, parent, pos, size, text, 18f);
            b.interactable = enabled;
            b.onClick.AddListener(() => click());
            return b;
        }

        /// <summary>A tab-style button (the crafting screen's tabs): lit when chosen.</summary>
        internal static Button Tab(Transform parent, string text, Vector2 pos, Vector2 size, bool on, Action click, bool enabled = true, float fontSize = 16f)
        {
            Button b = Clone(_tab ?? _button, parent, pos, size, text, fontSize);
            Transform selected = b.transform.Find("Selected");
            if (selected != null) selected.gameObject.SetActive(false);
            b.interactable = enabled;
            // the chosen one: its wood a little warmer, its lettering gold, with a mark
            if (on)
            {
                ColorBlock colors = b.colors;
                colors.normalColor = new Color(1f, 0.92f, 0.78f); colors.highlightedColor = new Color(1f, 0.96f, 0.86f);
                b.colors = colors;
            }
            TMP_Text label = b.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Text");
            if (label != null)
            {
                label.color = on ? Gold : enabled ? _text : _dim;
                label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                if (on) label.text = "• " + label.text + " •";
            }
            b.onClick.AddListener(() => click());
            return b;
        }

        private static Button Clone(Button template, Transform parent, Vector2 pos, Vector2 size, string text, float fontSize)
        {
            GameObject go = Object.Instantiate(template.gameObject, parent, false);
            go.name = "Button";
            go.SetActive(true);
            foreach (Transform child in go.transform.Cast<Transform>().ToList())
                if (child.name.StartsWith("gamepad_hint")) Object.Destroy(child.gameObject);
            foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(true)) Object.Destroy(pad);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            Button b = go.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            TMP_Text label = go.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Text");
            if (label == null)
            {
                label = Label(go.transform, text, Vector2.zero, size, fontSize, _text, Serif, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f));
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
    }
}
