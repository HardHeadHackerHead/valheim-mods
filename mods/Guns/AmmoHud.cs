using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// What a gun shows on screen: the rounds left in the magazine, a bar while it reloads, and (aiming a scoped gun) the scope picture.
    /// Made like the game's own windows: a screen layer scaled like the inventory's, with its font.
    /// </summary>
    internal static class AmmoHud
    {
        private static Canvas _canvas;
        private static TMP_Text _count, _note;
        private static Image _barBack, _bar;
        private static RawImage _scope;
        private static Texture2D _scopeTexture;
        private static float _scopeAspect;
        private static bool _wantScope;

        private static readonly Color Warm = new Color(1f, 0.86f, 0.55f), Low = new Color(1f, 0.55f, 0.25f), Empty = new Color(1f, 0.30f, 0.25f);

        internal static void Scope(bool on)
        {
            _wantScope = on;
            if (_scope != null) _scope.gameObject.SetActive(on);
            if (on && _scope != null) FitScope();
        }

        /// <summary>Every frame from the plugin.</summary>
        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            GunDef d = Arsenal.Held(p, out ItemDrop.ItemData item);
            if (d == null || p.IsDead()) { Show(false); return; }
            if (!Ensure()) return;
            Show(!global::Hud.IsUserHidden());
            int rounds = Ammo.Rounds(item, d);
            _count.text = $"{rounds}<size=55%> / {d.MagNow}";
            _count.color = rounds == 0 ? Empty : rounds * 4 <= d.MagNow ? Low : Warm;
            bool reloading = Ammo.Reloading;
            _barBack.gameObject.SetActive(reloading);
            if (reloading)
            {
                _bar.rectTransform.anchorMax = new Vector2(Ammo.Progress, 1f);
                _note.text = Localization.instance.Localize("$guns_reloading");
                _note.color = Warm;
            }
            else
            {
                _note.text = rounds == 0 ? Localization.instance.Localize("$guns_empty") : "";
                _note.color = Empty;
            }
        }

        private static void Show(bool on)
        {
            if (_canvas != null && _canvas.gameObject.activeSelf != on) _canvas.gameObject.SetActive(on);
        }

        internal static void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            if (_scopeTexture != null) Object.Destroy(_scopeTexture);
            _canvas = null; _count = _note = null; _barBack = _bar = null; _scope = null; _scopeTexture = null;
        }

        private static bool Ensure()
        {
            if (_canvas != null) return true;
            InventoryGui gui = InventoryGui.instance;
            if (gui == null) return false;
            TMP_FontAsset font = gui.transform.Find("root/Crafting/Decription/Description")?.GetComponent<TMP_Text>()?.font ??
                                 gui.transform.Find("root/Crafting/TabsButtons/Craft/Text")?.GetComponent<TMP_Text>()?.font;
            if (font == null) return false;

            var go = new GameObject("QuadsGunsHud", typeof(RectTransform));
            go.transform.SetParent(gui.transform.parent, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = -10;   // (under every window and the game's own interface)
            CanvasScaler mine = go.AddComponent<CanvasScaler>();
            CanvasScaler theirs = gui.GetComponentInChildren<CanvasScaler>(true) ?? gui.GetComponentInParent<CanvasScaler>();
            if (theirs != null)
            {
                mine.uiScaleMode = theirs.uiScaleMode; mine.referenceResolution = theirs.referenceResolution; mine.screenMatchMode = theirs.screenMatchMode;
                mine.matchWidthOrHeight = theirs.matchWidthOrHeight; mine.scaleFactor = theirs.scaleFactor; mine.referencePixelsPerUnit = theirs.referencePixelsPerUnit;
            }

            // the scope picture, over everything of the game's interface that is not a window
            _scope = new GameObject("Scope", typeof(RectTransform)).AddComponent<RawImage>();
            _scope.transform.SetParent(go.transform, false);
            var sr = (RectTransform)_scope.transform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one; sr.offsetMin = sr.offsetMax = Vector2.zero;
            _scope.raycastTarget = false;
            _scope.gameObject.SetActive(_wantScope);
            if (_wantScope) FitScope();

            _count = Text(go.transform, "Count", new Vector2(-48f, 132f), new Vector2(300f, 56f), 44f, font, TextAlignmentOptions.BottomRight);
            _note = Text(go.transform, "Note", new Vector2(-48f, 112f), new Vector2(300f, 24f), 20f, font, TextAlignmentOptions.BottomRight);
            _barBack = Bar(go.transform, "BarBack", new Vector2(-48f, 106f), new Vector2(220f, 6f), new Color(0f, 0f, 0f, 0.55f));
            _bar = Bar(_barBack.transform, "Bar", Vector2.zero, Vector2.zero, Warm);
            var br = _bar.rectTransform;
            br.anchorMin = Vector2.zero; br.anchorMax = new Vector2(0f, 1f); br.pivot = new Vector2(0f, 0.5f); br.offsetMin = br.offsetMax = Vector2.zero;
            _barBack.gameObject.SetActive(false);
            return true;
        }

        private static Image Bar(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static TMP_Text Text(Transform parent, string name, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            go.SetActive(false);   // (off while it gets its font: added to an object that is on, TextMeshPro looks for a default font the game doesn't have)
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font;
            go.SetActive(true);
            t.fontSize = fontSize; t.alignment = align; t.richText = true; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            t.fontStyle = FontStyles.Bold;
            t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 200);
            return t;
        }

        // ---- the scope ----------------------------------------------------------------------------------------------------

        /// <summary>A black screen with a round window and thin crosshair lines, drawn for the screen's shape (made again when it changes).</summary>
        private static void FitScope()
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            if (_scopeTexture != null && Mathf.Abs(aspect - _scopeAspect) < 0.01f) { _scope.texture = _scopeTexture; return; }
            if (_scopeTexture != null) Object.Destroy(_scopeTexture);
            _scopeAspect = aspect;
            int h = 360, w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 200, 1200);
            var px = new Color32[w * h];
            const float radius = 0.90f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float nx = ((x + 0.5f) / w * 2f - 1f) * aspect, ny = (y + 0.5f) / h * 2f - 1f;   // (half the height = 1)
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - 0.012f, radius + 0.012f, r));
                    if (r < radius && r > 0.025f && (Mathf.Abs(nx) < 0.0035f || Mathf.Abs(ny) < 0.0035f)) a = Mathf.Max(a, 0.85f);   // the crosshair
                    px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            _scopeTexture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _scopeTexture.SetPixels32(px);
            _scopeTexture.Apply();
            _scope.texture = _scopeTexture;
        }
    }
}
