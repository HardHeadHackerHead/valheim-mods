using System.Reflection;
using UnityEngine;

namespace BirdTrap
{
    /// <summary>
    /// The picture shown in the build menu. The game itself draws the model into a small see-through image; if that comes out empty
    /// (or fails) the picture stored inside the mod is used, so the menu never shows the chest this piece was copied from.
    /// The trap is drawn with its bird perched on the door frame rather than inside.
    /// </summary>
    internal static class Icon
    {
        private const int Size = 128;

        internal static Sprite Make(GameObject prefab)
        {
            Sprite drawn = null;
            try { drawn = Render(prefab); }
            catch (System.Exception e) { Debug.LogWarning("[" + Plugin.Name + "] could not draw the build icon: " + e.Message); }
            return drawn != null ? drawn : FromResource();
        }

        private static Sprite Render(GameObject prefab)
        {
            Transform model = prefab.transform.Find("Model");
            if (model == null) return null;

            // a copy of the model, far below the world where nothing else is
            var stage = new GameObject(Plugin.Name + "IconStage");
            stage.transform.position = new Vector3(0f, -3000f, 0f);
            GameObject copy = Object.Instantiate(model.gameObject, stage.transform);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.SetActive(true);
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
            // the build-menu pose: the bird perched on the door frame's crossbar, looking down at the bait (as in icon.png)
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                if (t.name == "bird") { t.localPosition = TrapPiece.PerchPos; t.localRotation = Quaternion.Euler(0f, TrapPiece.PerchYaw, 0f); }

            Bounds bounds = default;
            bool any = false;
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any) { Object.DestroyImmediate(stage); return null; }

            var camGo = new GameObject("IconCamera");
            camGo.transform.SetParent(stage.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 60f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            float radius = bounds.extents.magnitude;
            Vector3 dir = new Vector3(0.55f, 0.42f, -1f).normalized;
            cam.transform.position = bounds.center + dir * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.02f;
            cam.transform.LookAt(bounds.center);

            var lightGo = new GameObject("IconLight");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(38f, 28f, 0f);
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.None;

            RenderTexture rt = RenderTexture.GetTemporary(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Sprite result = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                if (LooksFine(tex))
                    result = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                else Object.Destroy(tex);
            }
            finally
            {
                RenderTexture.active = previous;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(stage);
            }
            return result;
        }

        /// <summary>Something was actually drawn: a fair share of the picture is covered, and it is not just black.</summary>
        private static bool LooksFine(Texture2D tex)
        {
            Color32[] pixels = tex.GetPixels32();
            int covered = 0; long brightness = 0;
            for (int i = 0; i < pixels.Length; i += 7)
            {
                Color32 c = pixels[i];
                if (c.a > 40) { covered++; brightness += c.r + c.g + c.b; }
            }
            int sampled = pixels.Length / 7 + 1;
            return covered > sampled * 0.08f && brightness / System.Math.Max(1, covered) > 60;
        }

        // Texture2D.LoadImage lives in a module the build cannot reference directly, so it is called by name.
        private static readonly MethodInfo LoadImageMethod =
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        private static bool LoadImage(Texture2D tex, byte[] data) => LoadImageMethod != null && (bool)LoadImageMethod.Invoke(null, new object[] { tex, data });

        private static Sprite FromResource()
        {
            Assembly asm = typeof(Icon).Assembly;
            using (System.IO.Stream s = asm.GetManifestResourceStream("icon.png"))
            {
                if (s == null) return null;
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!LoadImage(tex, bytes)) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
        }
    }
}
