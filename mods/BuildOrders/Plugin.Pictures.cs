using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildOrders
{
    /// <summary>
    /// Pictures for blueprints. A blueprint without one gets one made in the game: a solid copy of the whole build is put together far up in the
    /// sky where nobody sees it, photographed by its own camera with its own light (so it looks the same by day or night), saved as
    /// "&lt;blueprint&gt;.png" next to the file, and taken away again. A picture someone made themselves (a .png or .jpg with the same name) is kept.
    /// </summary>
    public partial class Plugin
    {
        private const int PicWidth = 320, PicHeight = 180;
        private static readonly Vector3 PhotoSpot = new Vector3(0f, 4000f, 0f);   // high above the middle of the world: nothing else is there
        private readonly Queue<string> _pictureQueue = new Queue<string>();
        private readonly Dictionary<string, float> _pictureTried = new Dictionary<string, float>();   // when each was last asked for (tried again a minute later if it is still missing)
        private bool _photographing;

        /// <summary>The picture file of a blueprint (its own .png, else .jpg), or null.</summary>
        private static string PictureFile(string blueprint)
        {
            foreach (string ext in new[] { ".png", ".jpg" })
            {
                string p = Path.ChangeExtension(blueprint, ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>Ask for a picture of a blueprint that has none (made a moment later, one at a time).</summary>
        private void WantPicture(string blueprint)
        {
            if (PictureFile(blueprint) != null) return;
            if (_pictureTried.TryGetValue(blueprint, out float at) && Time.unscaledTime - at < 60f) return;
            _pictureTried[blueprint] = Time.unscaledTime;
            _pictureQueue.Enqueue(blueprint);
            if (!_photographing) StartCoroutine(PhotographQueue());
        }

        private IEnumerator PhotographQueue()
        {
            _photographing = true;
            try
            {
                while (_pictureQueue.Count > 0)
                {
                    string file = _pictureQueue.Dequeue();
                    yield return null;
                    if (ZNetScene.instance == null || !File.Exists(file)) continue;
                    yield return Photograph(file);
                    _libraryAt = -99f; _inboxAt = -99f;   // show it
                }
            }
            finally { _photographing = false; }
        }

        private static int PhotoLayer()
        {
            for (int l = 30; l > 20; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 30;
        }

        private IEnumerator Photograph(string file)
        {
            List<Entry> entries;
            try { entries = EntriesFrom(JObject.Parse(File.ReadAllText(file))["pieces"] as JArray ?? new JArray()); }
            catch (Exception e) { Logger.LogWarning($"No picture for {Path.GetFileName(file)}: {e.Message}"); yield break; }
            if (entries.Count == 0) yield break;

            int layer = PhotoLayer();
            var root = new GameObject("BlueprintPhoto");
            root.transform.position = PhotoSpot;
            try
            {
                // the build, solid, a few dozen pieces a frame
                int n = 0;
                foreach (Entry e in entries)
                {
                    GameObject prefab = ZNetScene.instance.GetPrefab(e.Prefab);
                    if (prefab == null) continue;
                    ZNetView.m_forceDisableInit = true;
                    GameObject copy;
                    try { copy = Instantiate(prefab, PhotoSpot + e.Local, e.Rot, root.transform); }
                    finally { ZNetView.m_forceDisableInit = false; }
                    foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                    foreach (AudioSource a in copy.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
                    foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                    foreach (CircleProjector ring in copy.GetComponentsInChildren<CircleProjector>(true)) ring.gameObject.SetActive(false); // a workbench's area ring
                    if (++n % 40 == 0) yield return null;
                }
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                // frame it from the front-left and a little above (a blueprint's front is -z), using every piece's own measured corners (from the
                // piece list, not everything that draws: a workbench's 20 m area ring would shrink the build), centred on what is really there
                var shapes = Shapes();
                var points = new List<Vector3>();
                foreach (Entry e in entries)
                {
                    if (!shapes.TryGetValue(e.Prefab, out Shape s) || s.Max == s.Min) { points.Add(PhotoSpot + e.Local); continue; }
                    for (int c = 0; c < 8; c++)
                        points.Add(PhotoSpot + e.Local + e.Rot * new Vector3((c & 1) == 0 ? s.Min.x : s.Max.x, (c & 2) == 0 ? s.Min.y : s.Max.y, (c & 4) == 0 ? s.Min.z : s.Max.z));
                }
                if (points.Count == 0) yield break;
                Quaternion look = Quaternion.Euler(28f, 35f, 0f), inv = Quaternion.Inverse(look);
                float fov = 30f, aspect = PicWidth / (float)PicHeight;
                float tanV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tanH = tanV * aspect;
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                foreach (Vector3 pt in points) { Vector3 q = inv * pt; lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q); }
                Vector3 mid = (lo + hi) / 2f;
                float dist = 0f, depth = hi.z - lo.z;
                foreach (Vector3 pt in points)
                {
                    Vector3 q = inv * pt - mid;   // in the camera's frame, from the middle; a point nearer the camera (q.z < 0) needs more room
                    dist = Mathf.Max(dist, Mathf.Abs(q.x) / (tanH * 0.9f) - q.z, Mathf.Abs(q.y) / (tanV * 0.9f) - q.z);
                }
                Vector3 target = look * mid;
                float radius = depth + 10f;

                yield return new WaitForEndOfFrame();
                var camGo = new GameObject("BlueprintPhotoCamera");
                var lightGo = new GameObject("BlueprintPhotoLight");
                RenderTexture rt = RenderTexture.GetTemporary(PicWidth * 2, PicHeight * 2, 24, RenderTextureFormat.ARGB32);
                bool fog = RenderSettings.fog;
                Color ambient = RenderSettings.ambientLight;
                var ambientMode = RenderSettings.ambientMode;
                try
                {
                    Light sun = lightGo.AddComponent<Light>();
                    sun.type = LightType.Directional;
                    sun.color = new Color(1f, 0.96f, 0.88f);
                    sun.intensity = 1.35f;
                    sun.shadows = LightShadows.Soft;
                    sun.cullingMask = 1 << layer;
                    lightGo.transform.rotation = Quaternion.Euler(50f, 20f, 0f);

                    Camera cam = camGo.AddComponent<Camera>();
                    cam.enabled = false;
                    cam.cullingMask = 1 << layer;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
                    cam.fieldOfView = fov;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = dist + radius * 3f + 10f;
                    cam.transform.SetPositionAndRotation(target - look * Vector3.forward * dist, look);
                    cam.targetTexture = rt;

                    RenderSettings.fog = false;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.68f, 0.69f, 0.72f);
                    cam.Render();
                }
                finally
                {
                    RenderSettings.fog = fog;
                    RenderSettings.ambientMode = ambientMode;
                    RenderSettings.ambientLight = ambient;
                }
                try
                {
                    // read it back at half size (drawn at double size: smoother edges)
                    var big = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = prev;
                    var small = new Texture2D(PicWidth, PicHeight, TextureFormat.RGB24, false);
                    for (int y = 0; y < PicHeight; y++)
                        for (int x = 0; x < PicWidth; x++)
                        {
                            Color c = (big.GetPixel(x * 2, y * 2) + big.GetPixel(x * 2 + 1, y * 2) + big.GetPixel(x * 2, y * 2 + 1) + big.GetPixel(x * 2 + 1, y * 2 + 1)) / 4f;
                            small.SetPixel(x, y, c);
                        }
                    small.Apply();
                    File.WriteAllBytes(Path.ChangeExtension(file, ".png"), ImageConversion.EncodeToPNG(small));
                    Destroy(big); Destroy(small);
                    Logger.LogInfo($"Made a picture for {Path.GetFileName(file)}");
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(rt);
                    Destroy(camGo);
                    Destroy(lightGo);
                }
            }
            finally { Destroy(root); }
        }

        /// <summary>A blueprint's picture, small enough to send with Share (JPEG), or null.</summary>
        private byte[] PictureForSharing(string blueprint)
        {
            string path = PictureFile(blueprint);
            if (path == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (LoadImageMethod == null || !(bool)LoadImageMethod.Invoke(null, new object[] { tex, File.ReadAllBytes(path) })) return null;
                if (tex.width > 640 || tex.height > 640)
                {
                    // a big photo someone put there: send a smaller copy
                    float k = 640f / Mathf.Max(tex.width, tex.height);
                    int w = Mathf.Max(1, Mathf.RoundToInt(tex.width * k)), h = Mathf.Max(1, Mathf.RoundToInt(tex.height * k));
                    var small = new Texture2D(w, h, TextureFormat.RGB24, false);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            small.SetPixel(x, y, tex.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h));
                    small.Apply();
                    Destroy(tex);
                    tex = small;
                }
                return ImageConversion.EncodeToJPG(tex, 80);
            }
            catch (Exception) { return null; }
            finally { Destroy(tex); }
        }

        /// <summary>Keep a picture that came with a shared blueprint (only if it really is a small picture).</summary>
        private void SaveSharedPicture(string blueprint, string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                if (bytes.Length > 400 * 1024) return;
                var tex = new Texture2D(2, 2);
                try
                {
                    if (LoadImageMethod == null || !(bool)LoadImageMethod.Invoke(null, new object[] { tex, bytes })) return;
                    if (tex.width > 1024 || tex.height > 1024) return;
                }
                finally { Destroy(tex); }
                File.WriteAllBytes(Path.ChangeExtension(blueprint, ".jpg"), bytes);
            }
            catch (Exception) { }
        }
    }
}
