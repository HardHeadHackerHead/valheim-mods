using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Webslinger
{
    /// <summary>
    /// The extras that sell the speed, on the player's own screen and in the world:
    ///   Splats   where a web sticks, a small white web patch on the surface, fading after a few seconds (every player's webs).
    ///   Lines    speed lines streaming out from where you're heading, at swinging speed (the player's SpeedLines setting).
    ///   Camera   a slight roll into turns and toward the web, and a dip at the bottom of a swing (the player's CameraMotion setting).
    /// All made in code: the pictures are drawn into small textures when first needed.
    /// </summary>
    internal static class Fx
    {
        private static GameObject _root;

        public static void Reset()
        {
            foreach (Splat s in Splats) if (s.Go != null) Object.Destroy(s.Go);
            Splats.Clear();
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _lines = null;
            _roll = _dip = _dipVel = 0f;
            _lastFlat = Vector3.zero;
        }

        private static Transform Root()
        {
            if (_root == null) { _root = new GameObject("webslinger_fx"); Object.DontDestroyOnLoad(_root); }
            return _root.transform;
        }

        public static void Late(Player local)
        {
            Fade();
            Lines(local);
        }

        // ---- splats ----

        private sealed class Splat { public GameObject Go; public Material Mat; public float Born, Size; public bool Dark; }
        private static readonly List<Splat> Splats = new List<Splat>();
        private static Texture2D _splatTex;
        private const float SplatSeconds = 12f, SplatFade = 2f;

        /// <summary>A web patch where a web stuck: from the hand, the surface found along the web (its facing), else facing back along it.</summary>
        public static void SplatAt(Vector3 hand, Vector3 anchor, bool dark = false)
        {
            if (!Plugin.Splats.Value) return;
            Vector3 dir = anchor - hand;
            float dist = dir.magnitude;
            if (dist < 0.1f) return;
            dir /= dist;
            Vector3 normal = -dir, point = anchor;
            if (Physics.Raycast(hand, dir, out RaycastHit hit, dist + 0.6f, Anchors.Mask, QueryTriggerInteraction.Ignore) && Vector3.Distance(hit.point, anchor) < 1.5f)
            {
                normal = hit.normal;
                point = hit.point;
            }
            if (Splats.Count >= 30) { Object.Destroy(Splats[0].Go); Splats.RemoveAt(0); }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(go.GetComponent<Collider>());   // (never in the way of a cast or a body)
            go.name = "webslinger_splat";
            go.transform.SetParent(Root(), true);
            go.transform.position = point + normal * 0.03f;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.55f, 0.8f);
            go.transform.localScale = Vector3.one * size * 0.2f;
            Renderer r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Shader shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "webslinger_splat", mainTexture = SplatTexture() };
            mat.color = Tint(1f, dark);
            r.sharedMaterial = mat;
            Splats.Add(new Splat { Go = go, Mat = mat, Born = Time.time, Size = size, Dark = dark });
        }

        private static void Fade()
        {
            for (int i = Splats.Count - 1; i >= 0; i--)
            {
                Splat s = Splats[i];
                float age = Time.time - s.Born;
                if (s.Go == null || age > SplatSeconds + SplatFade)
                {
                    if (s.Go != null) Object.Destroy(s.Go);
                    if (s.Mat != null) Object.Destroy(s.Mat);
                    Splats.RemoveAt(i);
                    continue;
                }
                float grow = Mathf.Clamp01(age / 0.08f);   // (spreads out as it hits)
                s.Go.transform.localScale = Vector3.one * s.Size * Mathf.Lerp(0.2f, 1f, grow);
                s.Mat.color = Tint(1f - Mathf.Clamp01((age - SplatSeconds) / SplatFade), s.Dark);
            }
        }

        /// <summary>White, dimmed at night (the material isn't lit: it would glow in the dark otherwise).</summary>
        private static Color Tint(float alpha, bool dark = false)
        {
            if (dark) return new Color(0.05f, 0.05f, 0.07f, alpha * 0.92f);   // (the Venom suit's black webbing)
            Color amb = RenderSettings.ambientLight;
            float light = Mathf.Clamp(Mathf.Max(amb.r, Mathf.Max(amb.g, amb.b)) * 2.2f, 0.3f, 0.95f);
            return new Color(light, light, light * 0.98f, alpha * 0.9f);
        }

        /// <summary>A web patch: strands out from a sticky middle, with threads across them.</summary>
        private static Texture2D SplatTexture()
        {
            if (_splatTex != null) return _splatTex;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "webslinger_splat" };
            var px = new Color32[S * S];
            var rng = new System.Random(11);
            int strands = 9;
            var angles = new float[strands];
            var reach = new float[strands];
            for (int i = 0; i < strands; i++) { angles[i] = (i + (float)rng.NextDouble() * 0.6f) / strands * Mathf.PI * 2f; reach[i] = 0.7f + (float)rng.NextDouble() * 0.3f; }
            float mid = (S - 1) / 2f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - mid) / mid, dy = (y - mid) / mid;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float alpha = Mathf.Clamp01(1f - r / 0.22f) * 0.95f;   // the blob
                    for (int i = 0; i < strands; i++)
                    {
                        float d = Mathf.DeltaAngle(a * Mathf.Rad2Deg, angles[i] * Mathf.Rad2Deg) * Mathf.Deg2Rad * r * mid;   // (pixels from the strand)
                        if (r < reach[i]) alpha = Mathf.Max(alpha, Mathf.Clamp01(1.4f - Mathf.Abs(d)) * (1f - r / reach[i] * 0.6f));
                    }
                    // threads across, sagging between the strands
                    foreach (float ring in new[] { 0.38f, 0.6f })
                    {
                        float sag = ring - 0.04f * Mathf.Abs(Mathf.Sin(a * strands / 2f));
                        alpha = Mathf.Max(alpha, Mathf.Clamp01(1.2f - Mathf.Abs(r - sag) * mid) * 0.75f);
                    }
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return _splatTex = tex;
        }

        // ---- speed lines ----

        private sealed class Streak { public Image Img; public float Angle, Radius, Speed, Length; }
        private static Streak[] _lines;

        private static void Lines(Player p)
        {
            float speed = p != null && Web.State != Move.None && Web.State != Move.Crawl ? Web.Body(p).linearVelocity.magnitude : 0f;
            float strength = Plugin.SpeedLines.Value && !Hud.IsUserHidden() ? Mathf.Clamp01((speed - 15f) / 12f) : 0f;
            if (_lines == null)
            {
                if (strength <= 0f) return;
                BuildLines();
            }
            Camera cam = Utils.GetMainCamera();
            float w = Screen.width, h = Screen.height, half = Mathf.Sqrt(w * w + h * h) * 0.5f;
            Vector2 focus = new Vector2(w * 0.5f, h * 0.5f);
            if (cam != null && p != null && speed > 1f)
            {
                Vector3 ahead = cam.WorldToScreenPoint(cam.transform.position + Web.Body(p).linearVelocity.normalized * 20f);
                if (ahead.z > 0f) focus = Vector2.Lerp(focus, new Vector2(Mathf.Clamp(ahead.x, w * 0.2f, w * 0.8f), Mathf.Clamp(ahead.y, h * 0.2f, h * 0.8f)), 0.7f);
            }
            float dt = Time.deltaTime;
            foreach (Streak s in _lines)
            {
                s.Radius += s.Speed * dt * (0.6f + strength);
                if (s.Radius > 1.1f) Respawn(s, false);
                Vector2 dir = new Vector2(Mathf.Cos(s.Angle), Mathf.Sin(s.Angle));
                var rt = (RectTransform)s.Img.transform;
                rt.position = focus + dir * (s.Radius * half);
                rt.localRotation = Quaternion.Euler(0f, 0f, s.Angle * Mathf.Rad2Deg - 90f);
                rt.sizeDelta = new Vector2(Mathf.Max(1.5f, h / 540f), s.Length * h * (0.5f + strength));
                Color c = s.Img.color;
                c.a = strength * 0.28f * Mathf.Clamp01((s.Radius - 0.35f) / 0.2f);
                s.Img.color = c;
                s.Img.enabled = c.a > 0.003f;
            }
        }

        private static void BuildLines()
        {
            var canvasGo = new GameObject("speedlines");
            canvasGo.transform.SetParent(Root(), false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -20;   // (under the HUD and the web marks)
            Sprite streak = StreakSprite();
            _lines = new Streak[28];
            for (int i = 0; i < _lines.Length; i++)
            {
                var go = new GameObject("line");
                go.transform.SetParent(canvasGo.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                var img = go.AddComponent<Image>();
                img.sprite = streak;
                img.raycastTarget = false;
                img.color = new Color(1f, 1f, 1f, 0f);
                _lines[i] = new Streak { Img = img };
                Respawn(_lines[i], true);
            }
        }

        private static void Respawn(Streak s, bool anywhere)
        {
            s.Angle = Random.Range(0f, Mathf.PI * 2f);
            s.Radius = anywhere ? Random.Range(0.35f, 1.1f) : Random.Range(0.3f, 0.45f);
            s.Speed = Random.Range(0.9f, 1.6f);
            s.Length = Random.Range(0.06f, 0.16f);
        }

        private static Sprite StreakSprite()
        {
            const int W = 4, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "webslinger_streak" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float t = y / (float)(H - 1);
                float a = Mathf.Sin(Mathf.PI * t) * t;   // (thin at the inner end, brightest toward the outer)
                for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a * 1.6f) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, W, H), new Vector2(0.5f, 0.5f));
        }

        // ---- the camera ----

        private static float _roll, _dip, _dipVel;
        private static Vector3 _lastFlat;

        /// <summary>After the game has placed the camera this frame (it places it afresh each frame, so nothing adds up): the roll and the dip.</summary>
        [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
        private static class GameCamera_UpdateCamera
        {
            private static void Postfix(GameCamera __instance, float dt)
            {
                Player p = Player.m_localPlayer;
                if (p == null || dt <= 0f) return;
                bool on = Plugin.CameraMotion.Value && Plugin.Enabled.Value && !p.IsDead() && (Web.State == Move.Swing || Web.State == Move.Fly || Web.State == Move.Zip);
                float roll = 0f, dip = 0f;
                if (on)
                {
                    Vector3 v = Web.Body(p).linearVelocity, flat = new Vector3(v.x, 0f, v.z);
                    // into the turn: how fast the way of flight turns (degrees a second), a little of it
                    if (flat.magnitude > 3f && _lastFlat.magnitude > 3f)
                    {
                        float turn = Vector3.SignedAngle(_lastFlat, flat, Vector3.up) / dt;
                        roll = Mathf.Clamp(-turn * 0.05f, -8f, 8f);
                    }
                    if (Web.State == Move.Swing && Web.Stuck)
                    {
                        // toward the web's side, and down at the bottom of the arc where the rope pulls hardest
                        Vector3 toWeb = Web.Anchor - p.transform.position;
                        float sideways = Vector3.Dot(toWeb.normalized, __instance.transform.right);
                        roll += -sideways * 5f;
                        float pull = v.sqrMagnitude / Mathf.Max(3f, Web.Length);
                        float low = Mathf.Clamp01(1f - Mathf.Abs(v.y) / 6f);
                        dip = Mathf.Clamp(pull / 60f, 0f, 1f) * low * 0.35f;
                    }
                    _lastFlat = flat;
                }
                else _lastFlat = Vector3.zero;
                _roll = Mathf.Lerp(_roll, roll, 1f - Mathf.Exp(-4f * dt));
                _dip = Mathf.SmoothDamp(_dip, dip, ref _dipVel, 0.12f, 99f, dt);
                if (Mathf.Abs(_roll) < 0.01f && _dip < 0.001f) return;
                Transform t = __instance.transform;
                t.rotation = t.rotation * Quaternion.Euler(0f, 0f, _roll);
                t.position += Vector3.down * _dip;
            }
        }
    }
}
