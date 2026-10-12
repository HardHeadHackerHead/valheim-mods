using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PartyHud
{
    /// <summary>Embedded artwork, shared by every row; sliced corners stay the same size as rows grow.</summary>
    internal static class HudSkin
    {
        private static Texture2D _wood, _greenWood, _meter, _surroundWood;
        private static Sprite _gold, _green, _fill, _surround;
        internal static readonly Color Bronze = new Color(0.72f, 0.53f, 0.29f);

        private static void Load()
        {
            if (_gold != null) return;
            _wood = ReadTexture("PartyHud.nordic-panel.png");
            _surroundWood = ReadTexture("PartyHud.nordic-surround.png");
            _surround = Sprite.Create(_surroundWood, new Rect(9, 67, 2155, 595), Vector2.one * 0.5f,
                                     100, 0, SpriteMeshType.FullRect, new Vector4(900, 190, 190, 245));
            // Trim the transparent canvas without resampling the source artwork.
            var rect = new Rect(16, 85, 2140, 558);
            var border = new Vector4(180, 180, 180, 180);
            _gold = Sprite.Create(_wood, rect, Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, border);
            _greenWood = new Texture2D(_wood.width, _wood.height, TextureFormat.RGBA32, false);
            Color[] pixels = _wood.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                // Recolour the bright bronze only, keeping the charcoal wood neutral.
                float tint = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.12f, 0.32f, c.maxColorComponent));
                pixels[i] = Color.Lerp(c, new Color(c.r * 0.72f, c.r * 0.88f, c.r * 0.72f, c.a), tint);
            }
            _greenWood.SetPixels(pixels); _greenWood.Apply(false, true);
            _greenWood.wrapMode = TextureWrapMode.Clamp;
            _green = Sprite.Create(_greenWood, rect, Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect, border);

            // A small, deterministic texture adds a bevel and grain to all live bar fills.
            _meter = new Texture2D(128, 24, TextureFormat.RGBA32, false);
            var grain = new Color[128 * 24];
            for (int y = 0; y < 24; y++) for (int x = 0; x < 128; x++)
            {
                float v = 0.82f + ((x * 17 + y * 31) % 23) / 150f;
                if (y < 3) v *= 0.65f; else if (y > 20) v = 1f;
                grain[y * 128 + x] = new Color(v, v, v, 1);
            }
            _meter.SetPixels(grain); _meter.Apply(false, true);
            _meter.wrapMode = TextureWrapMode.Clamp;
            _fill = Sprite.Create(_meter, new Rect(0, 0, 128, 24), Vector2.one * 0.5f);
        }

        private static Texture2D ReadTexture(string resource)
        {
            using (Stream stream = typeof(HudSkin).Assembly.GetManifestResourceStream(resource))
            using (var bytes = new MemoryStream())
            {
                stream.CopyTo(bytes);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // Unity's conversion module targets netstandard 2.1 while BepInEx mods target net48.
                // Bind its public API at runtime, as the game's Mono already provides that module.
                var conversion = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", true);
                var load = conversion.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (!(bool)load.Invoke(null, new object[] { texture, bytes.ToArray(), false }))
                    throw new InvalidDataException("Could not decode the embedded party HUD texture.");
                texture.wrapMode = TextureWrapMode.Clamp;
                return texture;
            }
        }

        internal static void Surround(RectTransform rect, float opacity)
        {
            Load();
            // Back the shoulder with the same silhouette: bright scenery cannot wash out the title,
            // and the transparent area above the slope stays clear.
            Image backing = GameUi.Plain(rect, new Color(0, 0, 0, opacity));
            Image image = GameUi.Plain(GameUi.Stretch(rect, "MetalAndWood"), new Color(1, 1, 1, opacity));
            foreach (Image layer in new[] { backing, image })
            {
                layer.sprite = _surround;
                layer.type = Image.Type.Sliced;
                layer.pixelsPerUnitMultiplier = 2.8f;
            }
        }

        internal static Image Panel(RectTransform rect, bool companion, float opacity = 1f)
        {
            Load();
            Image image = GameUi.Plain(rect, new Color(1, 1, 1, opacity));
            image.sprite = companion ? _green : _gold;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 10f;
            return image;
        }

        internal static void TextureFill(Image image)
        {
            Load(); image.sprite = _fill;
        }

        internal static void Bevel(Transform rect, Color color)
        {
            GameUi.Frame(rect, 2.5f, 1.5f, new Color(0.08f, 0.06f, 0.04f));
            GameUi.Frame(rect, 1.5f, 1f, color);
            GameUi.Frame(rect, 0.5f, -0.7f, new Color(0.94f, 0.81f, 0.56f, 0.7f));
        }

        internal static void Destroy()
        {
            foreach (Object asset in new Object[] { _gold, _green, _fill, _surround, _wood, _greenWood, _meter, _surroundWood })
                if (asset != null) Object.Destroy(asset);
            _gold = _green = _fill = _surround = null;
            _wood = _greenWood = _meter = _surroundWood = null;
        }
    }
}
