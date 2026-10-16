using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Webslinger
{
    /// <summary>
    /// Spider-sense: a moment before danger reaches you, a tingle. Wavy lines flicker round your head (as in the comics), the edge of the
    /// screen glows on the side it comes from (the bottom edge: from behind), and the game's perfect-dodge sound plays. Only you see and hear
    /// it.
    ///
    /// What sets it off, checked ten times a second:
    ///   - a creature hostile to you, within range (the server's setting), starting an attack while it hunts you (or close by);
    ///   - a projectile (arrow, spit, fireball, harpoon) flying toward you that would pass within 2 m in the next 1.2 s.
    /// With the From setting at OutOfSight (default), attacks in front of the camera don't tingle (you can see those); projectiles always do.
    /// Projectiles are found as the game sets them flying (Projectile.Setup), never by searching the scene.
    /// </summary>
    internal static class Sense
    {
        private const float Seconds = 0.7f;
        private static readonly List<Projectile> Flying = new List<Projectile>();
        private static readonly Dictionary<Character, bool> Attacking = new Dictionary<Character, bool>();
        private static float _next, _tingleAt = -10f;
        private static Vector3 _from;
        public static int Tingles;
        public static string Last = "";

        private static readonly AccessTools.FieldRef<Projectile, Vector3> Vel = AccessTools.FieldRefAccess<Projectile, Vector3>("m_vel");
        private static readonly AccessTools.FieldRef<Projectile, Character> Owner = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");

        // the look: lines round the head, glows on the screen's edges
        private static GameObject _root;
        private static readonly LineRenderer[] Lines = new LineRenderer[7];
        private static Image _left, _right, _top, _bottom;
        private static Material _lineMat;

        public static void Reset()
        {
            Flying.Clear();
            Attacking.Clear();
            _tingleAt = -10f;
            if (_root != null) Object.Destroy(_root);
            _root = null;
            for (int i = 0; i < Lines.Length; i++) Lines[i] = null;
            _left = _right = _top = _bottom = null;
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class Projectile_Setup
        {
            private static void Postfix(Projectile __instance)
            {
                if (!Plugin.SpiderSense.Value || Player.m_localPlayer == null) return;
                if (Owner(__instance) == Player.m_localPlayer) return;
                if (Flying.Count < 200) Flying.Add(__instance);
            }
        }

        public static void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.1f;
            Player p = Player.m_localPlayer;
            if (p == null || p.IsDead() || !Plugin.SpiderSense.Value) { Flying.Clear(); Attacking.Clear(); return; }
            float range = Plugin.SenseRange.Value;
            if (range <= 0f) return;
            Vector3 me = p.GetCenterPoint();

            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c == p || c.IsDead()) continue;
                bool attacking = c.InAttack();
                Attacking.TryGetValue(c, out bool was);
                Attacking[c] = attacking;
                if (!attacking || was) continue;
                float dist = Vector3.Distance(c.transform.position, p.transform.position);
                if (dist > range || !BaseAI.IsEnemy(c, p)) continue;
                Character target = (c.GetBaseAI() as MonsterAI)?.GetTargetCreature();
                if (target != p && dist > 6f) continue;
                if (Plugin.SenseFrom.Value == Plugin.SenseMode.OutOfSight && InSight(c.GetCenterPoint())) continue;
                Tingle(c.GetCenterPoint(), c.m_name);
            }
            // (creatures gone: forget them now and then)
            if (Attacking.Count > 300) Attacking.Clear();

            for (int i = Flying.Count - 1; i >= 0; i--)
            {
                Projectile pr = Flying[i];
                if (pr == null) { Flying.RemoveAt(i); continue; }
                Character owner = Owner(pr);
                if (owner != null && !BaseAI.IsEnemy(owner, p)) { Flying.RemoveAt(i); continue; }
                Vector3 v = Vel(pr), rel = me - pr.transform.position;
                if (rel.magnitude > range + 20f || v.sqrMagnitude < 1f) continue;
                float t = Vector3.Dot(rel, v) / v.sqrMagnitude;
                if (t < 0f) { Flying.RemoveAt(i); continue; }   // (past us, flying away)
                if (t > 1.2f) continue;
                if ((rel - v * t).magnitude > 2f) continue;
                Flying.RemoveAt(i);
                Tingle(pr.transform.position, pr.name);
            }
        }

        private static bool InSight(Vector3 at)
        {
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            if (cam == null) return false;
            Vector3 to = at - cam.position;
            return to.sqrMagnitude > 0.01f && Vector3.Dot(cam.forward, to.normalized) > 0.6f;
        }

        /// <summary>The tingle (also from the test command): once per 0.8 s at most, from that spot.</summary>
        public static void Tingle(Vector3 from, string what)
        {
            if (Time.time - _tingleAt < 0.8f) return;
            _tingleAt = Time.time;
            _from = from;
            Tingles++;
            Last = what ?? "";
            Player p = Player.m_localPlayer;
            if (p != null) Sounds.Play(Sfx.Tingle, p.GetHeadPoint());
        }

        // ---- every frame: the look ----

        public static void Late()
        {
            float age = Time.time - _tingleAt;
            bool on = age >= 0f && age < Seconds && Player.m_localPlayer != null && GameCamera.instance != null;
            if (!on) { if (_root != null && _root.activeSelf) _root.SetActive(false); return; }
            Build();
            if (_root == null) return;
            if (!_root.activeSelf) _root.SetActive(true);

            // bright at once, then fading, with a flicker
            float k = age / Seconds;
            float strength = (age < 0.06f ? age / 0.06f : 1f - (age - 0.06f) / (Seconds - 0.06f)) * (0.75f + 0.25f * Mathf.Sin(age * 45f));
            strength = Mathf.Clamp01(strength);

            Player p = Player.m_localPlayer;
            Transform cam = GameCamera.instance.transform;
            Vector3 head = p.GetHeadPoint() + Vector3.up * 0.1f;
            for (int i = 0; i < Lines.Length; i++)
            {
                LineRenderer line = Lines[i];
                if (line == null) continue;
                // round the head in the camera's plane (as a drawing would), above and beside it: none straight down
                float angle = (-30f + i * 40f) * Mathf.Deg2Rad;
                Vector3 dir = cam.right * Mathf.Cos(angle) + cam.up * Mathf.Sin(angle);
                Vector3 across = Vector3.Cross(dir, cam.forward).normalized;
                float inner = 0.3f + 0.1f * k, outer = inner + 0.18f + 0.22f * Mathf.Min(1f, age * 8f);
                for (int j = 0; j < line.positionCount; j++)
                {
                    float u = j / (float)(line.positionCount - 1);
                    float zig = (j % 2 == 0 ? 1f : -1f) * 0.035f;
                    line.SetPosition(j, head + dir * Mathf.Lerp(inner, outer, u) + across * zig);
                }
                line.widthMultiplier = 0.018f * strength;
            }

            // the screen's edges, by where it comes from (seen from the camera, flat)
            Vector3 rel = Quaternion.Inverse(cam.rotation) * (_from - cam.position);
            rel.y = 0f;
            rel = rel.sqrMagnitude > 0.01f ? rel.normalized : Vector3.back;
            SetAlpha(_left, Mathf.Max(0f, -rel.x) * strength);
            SetAlpha(_right, Mathf.Max(0f, rel.x) * strength);
            SetAlpha(_top, Mathf.Max(0f, rel.z) * strength * 0.6f);
            SetAlpha(_bottom, Mathf.Max(0f, -rel.z) * strength);
        }

        private static void SetAlpha(Image img, float a)
        {
            if (img == null) return;
            Color c = img.color;
            c.a = Mathf.Clamp01(a) * 0.85f;
            img.color = c;
        }

        private static void Build()
        {
            if (_root != null) return;
            _root = new GameObject("webslinger_sense");
            Object.DontDestroyOnLoad(_root);
            Shader shader = Shader.Find("Sprites/Default");
            _lineMat = _lineMat ?? (shader != null ? new Material(shader) { name = "webslinger_sense" } : null);
            for (int i = 0; i < Lines.Length; i++)
            {
                var go = new GameObject("line" + i);
                go.transform.SetParent(_root.transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 6;
                line.numCapVertices = 1;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                if (_lineMat != null) line.sharedMaterial = _lineMat;
                line.startColor = line.endColor = new Color(1f, 0.92f, 0.6f, 1f);
                Lines[i] = line;
            }

            var canvasGo = new GameObject("edges");
            canvasGo.transform.SetParent(_root.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -10;   // (under the game's HUD)
            Sprite glow = Glow();
            _left = Edge(canvasGo.transform, glow, new Vector2(0f, 0f), new Vector2(0.18f, 1f), -90f);
            _right = Edge(canvasGo.transform, glow, new Vector2(0.82f, 0f), new Vector2(1f, 1f), 90f);
            _bottom = Edge(canvasGo.transform, glow, new Vector2(0f, 0f), new Vector2(1f, 0.22f), 0f);
            _top = Edge(canvasGo.transform, glow, new Vector2(0f, 0.78f), new Vector2(1f, 1f), 180f);
        }

        /// <summary>A strip along one edge of the screen, glowing at the edge and clear toward the middle (the glow drawn bottom-up, turned).</summary>
        private static Image Edge(Transform parent, Sprite glow, Vector2 min, Vector2 max, float turn)
        {
            var go = new GameObject("edge");
            go.transform.SetParent(parent, false);
            var holder = go.AddComponent<RectTransform>();
            holder.anchorMin = min;
            holder.anchorMax = max;
            holder.offsetMin = holder.offsetMax = Vector2.zero;
            var inner = new GameObject("glow");
            inner.transform.SetParent(go.transform, false);
            var rt = inner.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.Euler(0f, 0f, turn);
            var img = inner.AddComponent<Image>();
            img.sprite = glow;
            img.raycastTarget = false;
            img.color = new Color(1f, 0.85f, 0.45f, 0f);
            // (a strip turned on its side: swap its width and height so it still fills its edge)
            bool side = Mathf.Abs(Mathf.Abs(turn) - 90f) < 1f;
            holder.gameObject.AddComponent<Sizer>().Set(rt, side);
            return img;
        }

        /// <summary>Keeps a turned glow the size of its edge strip (the screen can change size).</summary>
        private sealed class Sizer : MonoBehaviour
        {
            private RectTransform _inner;
            private bool _side;
            public void Set(RectTransform inner, bool side) { _inner = inner; _side = side; Fit(); }
            private void LateUpdate() => Fit();
            private void Fit()
            {
                if (_inner == null) return;
                Rect r = ((RectTransform)transform).rect;
                _inner.sizeDelta = _side ? new Vector2(r.height, r.width) : new Vector2(r.width, r.height);
            }
        }

        private static Sprite Glow()
        {
            const int W = 4, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "webslinger_glow" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float a = Mathf.Pow(1f - y / (float)(H - 1), 2.2f);
                for (int x = 0; x < W; x++) px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, W, H), new Vector2(0.5f, 0.5f));
        }
    }
}
