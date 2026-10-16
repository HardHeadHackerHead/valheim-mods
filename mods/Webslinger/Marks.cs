using UnityEngine;
using UnityEngine.UI;

namespace Webslinger
{
    /// <summary>
    /// The web marks (the player's own WebMarks setting): in the air, a small web drawn over the spot a swing web would stick to, so a player
    /// knows a press of Jump will swing (no mark: nothing in reach, and the jump stays the game's); and a ring over the spot under the
    /// crosshair when a zip can reach it. Only on the player's own screen, under the game's HUD; hidden when the HUD is.
    /// The spots are looked for ten times a second (the same search a press makes), the marks follow them every frame.
    /// </summary>
    internal static class Marks
    {
        private static GameObject _root;
        private static Image _swing, _zip;
        private static float _nextLook, _swingAlpha, _zipAlpha;
        private static bool _hasSwing, _hasZip;
        private static Vector3 _swingAt, _zipAt;

        public static void Reset()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _swing = _zip = null;
            _hasSwing = _hasZip = false;
            _swingAlpha = _zipAlpha = 0f;
        }

        public static void Update()
        {
            Player p = Player.m_localPlayer;
            Camera cam = Utils.GetMainCamera();
            bool on = Plugin.ShowMarks.Value && Plugin.Enabled.Value && p != null && cam != null && !Hud.IsUserHidden()
                && !InventoryGui.IsVisible() && !Menu.IsVisible() && !Minimap.IsOpen() && Web.CanAct(p, out _);
            if (!on)
            {
                _hasSwing = _hasZip = false;
                if (_root != null) { Show(_swing, 0f); Show(_zip, 0f); _swingAlpha = _zipAlpha = 0f; }
                return;
            }

            if (Time.time >= _nextLook)
            {
                _nextLook = Time.time + 0.1f;
                Look(p);
            }
            if (_root == null) Build();

            float dt = Time.deltaTime, scale = Screen.height / 1080f;
            _swingAlpha = Mathf.MoveTowards(_swingAlpha, _hasSwing ? 1f : 0f, dt * 8f);
            _zipAlpha = Mathf.MoveTowards(_zipAlpha, _hasZip ? 1f : 0f, dt * 8f);
            Place(cam, _swing, _swingAt, _swingAlpha, 30f * scale * (1f + 0.08f * Mathf.Sin(Time.time * 6f)));
            Place(cam, _zip, _zipAt, _zipAlpha, 34f * scale);
        }

        /// <summary>What a press would do now: a swing web (in the air, out of a web), a zip (in the air or on a web or a wall).</summary>
        private static void Look(Player p)
        {
            bool air = !p.IsOnGround();
            bool free = Web.State == Move.None || Web.State == Move.Fly;
            bool busy = Compat.Busy(p);
            float f = Skill.Factor(p);
            _hasSwing = false;
            if (air && free && !busy && Plugin.Swing.Value
                && Anchors.SwingAnchor(p, Mathf.Lerp(Plugin.WebReach.Value, Plugin.WebReachAt100.Value, f), out Anchors.Spot spot))
            {
                _hasSwing = true;
                _swingAt = spot.Point;
            }
            _hasZip = false;
            if ((air || Web.State == Move.Crawl) && Web.State != Move.Zip && !busy && Plugin.Zip.Value
                && Anchors.ZipTarget(p, Mathf.Lerp(Plugin.ZipReach.Value, Plugin.ZipReachAt100.Value, f), out RaycastHit hit, out _))
            {
                _hasZip = true;
                _zipAt = hit.point;
            }
        }

        private static void Place(Camera cam, Image img, Vector3 world, float alpha, float size)
        {
            if (img == null) return;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (alpha <= 0.001f || screen.z <= 0f) { Show(img, 0f); return; }
            var rt = (RectTransform)img.transform;
            rt.position = new Vector3(screen.x, screen.y, 0f);
            rt.sizeDelta = new Vector2(size, size);
            Show(img, alpha);
        }

        private static void Show(Image img, float alpha)
        {
            if (img == null) return;
            Color c = img.color;
            c.a = alpha * 0.9f;
            img.color = c;
            if (img.enabled != alpha > 0.001f) img.enabled = alpha > 0.001f;
        }

        private static void Build()
        {
            _root = new GameObject("webslinger_marks");
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;   // (under the game's HUD)
            _swing = Mark("swing", WebSprite(), new Color(1f, 1f, 1f, 0f));
            _zip = Mark("zip", RingSprite(), new Color(0.75f, 0.9f, 1f, 0f));
        }

        private static Image Mark(string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.color = color;
            img.enabled = false;
            return img;
        }

        // ---- the pictures, drawn in code: white lines with a soft dark edge, so they show on snow and on night sky ----

        private const int Size = 64;

        /// <summary>A small spider web: eight spokes and two rings.</summary>
        private static Sprite WebSprite() => Draw("webslinger_mark_web", (r, angle) =>
        {
            float R = Size * 0.46f;
            if (r > R + 1f) return 99f;
            float m = Mathf.Repeat(angle, Mathf.PI / 4f);
            float spoke = r * Mathf.Sin(Mathf.Min(m, Mathf.PI / 4f - m));   // (distance to the nearest of eight spokes, in pixels)
            float ring1 = Mathf.Abs(r - R * 0.45f), ring2 = Mathf.Abs(r - R * 0.9f);
            return Mathf.Min(spoke, Mathf.Min(ring1, ring2));
        });

        /// <summary>A ring with four ticks pointing in.</summary>
        private static Sprite RingSprite() => Draw("webslinger_mark_ring", (r, angle) =>
        {
            float R = Size * 0.42f;
            float ring = Mathf.Abs(r - R) - 0.8f;
            float a = Mathf.Repeat(angle, Mathf.PI / 2f) - Mathf.PI / 4f;   // (the ticks sit at 45 degrees)
            float tick = r > R - 9f && r < R ? r * Mathf.Abs(Mathf.Sin(a)) : 99f;
            return Mathf.Min(ring, tick);
        });

        /// <summary>A sprite from a distance (in pixels, from the middle's radius and angle) to its nearest line.</summary>
        private static Sprite Draw(string name, System.Func<float, float, float> distance)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[Size * Size];
            float mid = (Size - 1) / 2f;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - mid, dy = y - mid;
                    float d = distance(Mathf.Sqrt(dx * dx + dy * dy), Mathf.Atan2(dy, dx));
                    float white = Mathf.Clamp01(1.6f - d), dark = Mathf.Clamp01(3.2f - d) * 0.55f;
                    float a = white + dark * (1f - white);
                    byte c = (byte)(a > 0f ? 255f * white / a : 0f);
                    px[y * Size + x] = new Color32(c, c, c, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f));
        }
    }
}
