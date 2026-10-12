using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Guns
{
    /// <summary>
    /// The inventory picture of a gun: its model drawn from the side, barrel up and to the right (as the game's own weapon icons lie),
    /// with a light from the top left and a see-through background. The model is copied far below the world into a layer of its own
    /// with nothing else in it, a camera that sees only that layer takes the picture, and the copy goes again (in finally). Unlike the
    /// shared Icon.cs, which picks its view from the item's shape, this knows the model: barrel along +Z, up +Y. Logs why when it fails.
    /// </summary>
    internal static class GunIcon
    {
        private const int Size = 256;

        internal static Sprite Make(Transform model, float roll = 24f)
        {
            if (model == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null;
            int layer = FreeLayer();
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            var stage = new GameObject("QuadsGunsIconStage");
            stage.SetActive(false);
            stage.transform.position = new Vector3(0f, -3000f, 0f);
            try
            {
                GameObject copy = Object.Instantiate(model.gameObject, stage.transform);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.SetActive(true);
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                stage.SetActive(true);

                // looking along -X (barrel +Z to the right of the picture, up +Y up), then rolled so the barrel climbs to the right
                Quaternion rot = Quaternion.LookRotation(Vector3.left, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
                Renderer[] rs = copy.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { Plugin.Log.LogWarning("the icon: the model has no renderers"); return null; }
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);

                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = Quaternion.Inverse(rot) * (corner - b.center);
                    min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
                Vector3 mid = (min + max) * 0.5f;

                var camGo = new GameObject("IconCamera");
                camGo.transform.SetParent(stage.transform, false);
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.orthographic = true;
                cam.cullingMask = 1 << layer;
                cam.transform.rotation = rot;
                cam.orthographicSize = Mathf.Max(0.01f, Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f * 1.1f);
                cam.transform.position = b.center + rot * new Vector3(mid.x, mid.y, min.z - 1f);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = (max.z - min.z) + 2f;

                var lightGo = new GameObject("IconLight");
                lightGo.transform.SetParent(stage.transform, false);
                lightGo.transform.rotation = Quaternion.LookRotation(rot * new Vector3(0.5f, -0.6f, 1f), rot * Vector3.up);
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << layer;

                rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                // The shader may write any alpha it likes, so the see-through background is worked out from two pictures of the same model, on
                // black and on white: where they differ the model is not covering (alpha = 1 - difference), and colour = the black one / alpha.
                Color32[] onBlack = Take(cam, rt, Color.black);
                Color32[] onWhite = Take(cam, rt, Color.white);
                var px = new Color32[onBlack.Length];
                int covered = 0; long bright = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float diff = ((onWhite[i].r - onBlack[i].r) + (onWhite[i].g - onBlack[i].g) + (onWhite[i].b - onBlack[i].b)) / (3f * 255f);
                    float alpha = Mathf.Clamp01(1f - diff);
                    if (alpha < 0.02f) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    px[i] = new Color32((byte)Mathf.Min(255f, onBlack[i].r / alpha), (byte)Mathf.Min(255f, onBlack[i].g / alpha), (byte)Mathf.Min(255f, onBlack[i].b / alpha), (byte)(alpha * 255f));
                    if (alpha > 0.15f) { covered++; bright += px[i].r + px[i].g + px[i].b; }
                }
                Plugin.Log.LogInfo($"the icon: {covered * 100 / px.Length}% covered, average brightness {(covered > 0 ? bright / covered / 3 : 0)} of 255, {rs.Length} renderers");
                if (covered < px.Length / 50) return null;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.SetPixels32(px);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Log.LogWarning("the icon could not be drawn: " + e); return null; }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(stage);
            }
        }

        private static Color32[] Take(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            Color32[] pixels = tex.GetPixels32();
            Object.Destroy(tex);
            return pixels;
        }

        /// <summary>A layer no one has named, for the copy, the camera and the light alone.</summary>
        private static int FreeLayer()
        {
            for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 31;
        }
    }
}
