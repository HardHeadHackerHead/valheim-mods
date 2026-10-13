using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// The eyes: one extra camera, drawn by hand into whichever terminal's RenderTexture is asked for (never more than one picture a
    /// frame, whatever number of terminals). It floats behind the Proxy's shoulder like the game's own third-person view, pulled in when
    /// something is in the way. The first spike (2026-10-12) showed grass, trees, sky and light come out right with the main camera's
    /// settings copied.
    /// </summary>
    internal static class View
    {
        private static GameObject _go;
        private static Camera _camera;
        private static int _lastFrame = -1;
        private static int _blockers = -1;
        private static float _zoom;
        internal static void Zoom(float delta) { _zoom = Mathf.Clamp(_zoom + delta, -3f, 5f); }
        internal static int Renders;            // (the "terminal status" command shows these)
        internal static Vector3 LastAt, LastDir;
        internal static float LastDistance;

        /// <summary>
        /// Where the camera floats and which way it looks for this Proxy: behind its shoulder, pulled in when something is in the way.
        /// What the driver's crosshair (the middle of the picture) points at is the ray from here along the direction.
        /// </summary>
        internal static void Pose(ProxyBody proxy, out Vector3 at, out Vector3 dir, out float distance)
        {
            proxy.Aim(out float yaw, out float pitch);
            Vector3 pivot = proxy.transform.position + Vector3.up * 1.7f + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * 0.4f;
            dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
            distance = Mathf.Clamp(Plugin.CameraDistance.Value + _zoom, 1.5f, 8f);
            if (_blockers < 0) _blockers = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            if (Physics.SphereCast(pivot, 0.2f, -dir, out RaycastHit hit, distance, _blockers, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0.5f, hit.distance - 0.05f);
            at = pivot - dir * distance;
        }

        /// <summary>Draw the Proxy's view into the texture. False when there is no picture this frame (try again later).</summary>
        public static bool Render(ProxyBody proxy, RenderTexture target)
        {
            if (_lastFrame == Time.frameCount || proxy == null || target == null) return false;
            if (!Ready()) return false;
            Pose(proxy, out Vector3 at, out Vector3 dir, out float distance);
            _go.transform.position = at;
            _go.transform.rotation = Quaternion.LookRotation(dir);
            _camera.targetTexture = target;
            _camera.Render();
            _camera.targetTexture = null;
            Hud(proxy, target);
            _lastFrame = Time.frameCount;
            Renders++; LastAt = at; LastDir = dir; LastDistance = distance;
            return true;
        }

        /// <summary>
        /// The picture's own HUD, drawn into it after the camera has: a health bar, a stamina bar and a crosshair, so everybody watching the
        /// screen sees the same (the stamina comes through the Proxy's saved data).
        /// </summary>
        private static void Hud(ProxyBody proxy, RenderTexture target)
        {
            if (proxy.Body == null) return;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
            float s = target.width / 1024f;
            float health = Mathf.Clamp01(proxy.Body.GetHealth() / Mathf.Max(1f, proxy.Body.GetMaxHealth()));
            float stamina = Mathf.Clamp01(proxy.StaminaPercent / 100f);
            float x = 22f * s, w = 240f * s, y = target.height - 52f * s;
            Quad(x - 3f * s, y - 3f * s, w + 6f * s, 18f * s, new Color(0f, 0f, 0f, 0.55f));
            Quad(x, y, w * health, 12f * s, new Color(0.8f, 0.12f, 0.1f, 0.95f));
            Quad(x - 3f * s, y + 18f * s, w * 0.7f + 6f * s, 14f * s, new Color(0f, 0f, 0f, 0.55f));
            Quad(x, y + 21f * s, w * 0.7f * stamina, 8f * s, new Color(0.95f, 0.8f, 0.2f, 0.95f));
            float cx = target.width / 2f, cy = target.height / 2f, a = 9f * s, t = 2f * s;
            Color dark = new Color(0f, 0f, 0f, 0.7f), light = new Color(1f, 1f, 1f, 0.9f);
            Quad(cx - a - t, cy - t * 1.5f, a * 2f + t * 2f, t * 3f, dark); Quad(cx - t * 1.5f, cy - a - t, t * 3f, a * 2f + t * 2f, dark);
            Quad(cx - a, cy - t / 2f, a * 2f, t, light); Quad(cx - t / 2f, cy - a, t, a * 2f, light);
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        private static void Quad(float x, float y, float w, float h, Color color)
        {
            Graphics.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0, color);
        }

        private static bool Ready()
        {
            if (_camera != null) return true;
            Camera main = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            if (main == null) return false;
            _go = new GameObject("TerminalEyes");
            Object.DontDestroyOnLoad(_go);
            _camera = _go.AddComponent<Camera>();
            _camera.CopyFrom(main);   // sky, fog, clip planes and culling as the real view
            _camera.enabled = false;  // drawn by hand
            _camera.depth = main.depth - 1;
            return true;
        }

        public static void Clear()
        {
            if (_go != null) Object.Destroy(_go);
            _go = null; _camera = null; _lastFrame = -1;
        }
    }
}
