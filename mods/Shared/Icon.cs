using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DHack.Shared
{
    /// <summary>
    /// Pictures for the build menu and the inventory, drawn from the mod's own model when the game starts, so a piece copied from a chest
    /// never shows the chest, and an item without a hand-made icon still has one. The model is copied far below the world into a layer
    /// of its own, a camera and a light that see only that layer take a see-through picture, and the copy is removed again (in finally).
    /// If nothing worth showing was drawn (an empty or black picture, a dedicated server with no graphics, an error), a PNG built into
    /// the mod is used instead.
    ///
    ///   piece.m_icon = Icon.Make(piecePrefab);           // its "Model" child if it has one, else every renderer it shows; else icon.png
    ///   Icon.Make(prefab, new Vector3(0.5f, 0.45f, -1f)); // looking from further up and to the right (the default is 0.42, 0.28, -1)
    ///   Icon.Make(prefab, pose: copy => ...);             // move parts of the copy first (a bird on its perch), never the prefab
    ///   Icon.MakeItem(itemPrefab, setIcon: true);         // an item from above, from its "attach" child; written to m_shared.m_icons
    ///   Icon.LoadEmbedded("symbols.png");                 // any PNG built into the mod, as a texture (LoadSprite: as a sprite)
    ///
    /// The fallback is the mod's embedded resource "icon.png" for pieces (csproj: EmbeddedResource Include="icon.png" LogicalName="icon.png"),
    /// and the one named for items. Call it once the prefab is built (it reads the renderers as they are then); it costs a few milliseconds.
    /// Copies are made with ZNetView.m_forceDisableInit and TerrainOp.m_forceDisableTerrainOps on, and a whole-prefab copy loses its
    /// scripts, colliders and physics before it ever wakes up, so drawing never makes a networked object or digs into the ground.
    /// Shared source (mods/Shared/Icon.cs), linked into the mods that use it: BirdTrap, BountyBoard, LedgerChest, Recycler, SlotMachine.
    /// Claude Tools ships it as a template.
    /// </summary>
    internal static class Icon
    {
        private const int Size = 128;                                                  // the picture is drawn at twice this
        private static readonly Vector3 DefaultView = new Vector3(0.42f, 0.28f, -1f);  // from the model towards the camera

        /// <summary>
        /// A build-menu picture of a piece: its "Model" child if it has one (all its renderers switched on), otherwise every renderer the
        /// prefab shows (switched-off ones stay off). view: the direction from the model to the camera. pose: called with the copy before
        /// it is framed. fallback: the embedded PNG used when drawing fails (null: none, and the result may be null).
        /// </summary>
        internal static Sprite Make(GameObject prefab, Vector3? view = null, Action<GameObject> pose = null, string fallback = "icon.png")
        {
            Sprite drawn = null;
            try { drawn = DrawPiece(prefab, view ?? DefaultView, pose); }
            catch (Exception e) { Debug.LogWarning("[" + ModName + "] could not draw the build icon: " + e.Message); }
            return drawn != null ? drawn : LoadSprite(fallback);
        }

        /// <summary>
        /// An inventory picture of an item, seen from above (or along its thinnest side) and turned so a long item lies corner to corner,
        /// drawn from its "attach" child (what the hand holds) or else the whole prefab. setIcon: also make it the item's icon
        /// (m_itemData.m_shared.m_icons[0], other variants' icons kept). look: the camera's rotation instead of the automatic one.
        /// fallback: an embedded PNG used when drawing fails.
        /// </summary>
        internal static Sprite MakeItem(GameObject itemPrefab, bool setIcon = false, string fallback = null, Quaternion? look = null)
        {
            Sprite drawn = null;
            try { drawn = DrawItem(itemPrefab, look); }
            catch (Exception e) { Debug.LogWarning("[" + ModName + "] could not draw the icon of " + (itemPrefab != null ? itemPrefab.name : "an item") + ": " + e.Message); }
            Sprite sprite = drawn != null ? drawn : LoadSprite(fallback);
            if (setIcon && sprite != null && itemPrefab != null)
            {
                ItemDrop drop = itemPrefab.GetComponent<ItemDrop>();
                if (drop != null)
                {
                    ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
                    // a new array (never write into one that may be shared with the item it was copied from), as long as before
                    Sprite[] icons = shared.m_icons != null && shared.m_icons.Length > 1 ? (Sprite[])shared.m_icons.Clone() : new Sprite[1];
                    icons[0] = sprite;
                    shared.m_icons = icons;
                }
            }
            return sprite;
        }

        private static Sprite DrawPiece(GameObject prefab, Vector3 view, Action<GameObject> pose)
        {
            if (prefab == null || !CanDraw) return null;
            Transform model = prefab.transform.Find("Model");
            return Draw(model != null ? model.gameObject : prefab, model == null, Quaternion.identity, pose, (stage, bounds) =>
            {
                Camera cam = AddCamera(stage);
                cam.fieldOfView = 24f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 60f;
                float radius = bounds.extents.magnitude;
                cam.transform.position = bounds.center + view.normalized * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.02f;
                cam.transform.LookAt(bounds.center);
                AddLight(stage, Quaternion.Euler(38f, 28f, 0f), 1.25f);
                return cam;
            }, 0.08f);
        }

        private static Sprite DrawItem(GameObject itemPrefab, Quaternion? look)
        {
            if (itemPrefab == null || !CanDraw) return null;
            Transform attach = itemPrefab.transform.Find("attach");
            // the attach child keeps its turn in the item (how the item lies), the whole prefab stands as it is
            return Draw(attach != null ? attach.gameObject : itemPrefab, true, attach != null ? attach.localRotation : Quaternion.identity, null, (stage, bounds) =>
            {
                Camera cam = AddCamera(stage);
                cam.orthographic = true;
                Vector3 s = bounds.size;
                Quaternion rot;
                if (look.HasValue) rot = look.Value;
                else if (s.y <= s.x * 1.15f && s.y <= s.z * 1.15f) rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);   // lying down: from above
                else if (s.x <= s.z) rot = Quaternion.LookRotation(Vector3.right, Vector3.up);                                   // standing: from its thin side
                else rot = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                if (!look.HasValue) rot *= Quaternion.Euler(0f, 0f, 45f);   // corner to corner, as the game's own icons are
                cam.transform.rotation = rot;

                // fit the box around the model as the camera sees it, with a little room
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = Quaternion.Inverse(rot) * (corner - bounds.center);
                    min = Vector3.Min(min, local); max = Vector3.Max(max, local);
                }
                Vector3 mid = (min + max) * 0.5f;
                cam.orthographicSize = Mathf.Max(0.01f, Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f * 1.08f);
                float depth = max.z - min.z;
                cam.transform.position = bounds.center + rot * new Vector3(mid.x, mid.y, min.z - 1f);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = depth + 2f;

                // light from the top left of the picture
                Vector3 dir = rot * new Vector3(0.4f, -0.5f, 1f);
                AddLight(stage, Quaternion.LookRotation(dir, rot * Vector3.up), 1.3f);
                return cam;
            }, 0.02f);
        }

        /// <summary>
        /// Copies source onto a hidden stage in a layer of its own (turned by turn), frames it with the camera frame() makes, and returns
        /// the picture if it looks fine. bare: take the copy's scripts, colliders and physics away before it wakes, and leave switched-off
        /// renderers off (a whole prefab); otherwise switch every renderer on (a "Model" child, as the build-menu pictures always were).
        /// </summary>
        private static Sprite Draw(GameObject source, bool bare, Quaternion turn, Action<GameObject> pose, Func<GameObject, Bounds, Camera> frame, float minCover)
        {
            int layer = Layer;
            bool oldInit = ZNetView.m_forceDisableInit, oldTerrain = TerrainOp.m_forceDisableTerrainOps;
            RenderTexture previous = RenderTexture.active;
            RenderTexture rt = null;
            Camera cam = null;
            // far below the world where nothing else is; switched off until the copy is ready, so nothing on it wakes before that
            var stage = new GameObject(ModName + "IconStage");
            stage.SetActive(false);
            stage.transform.position = new Vector3(0f, -3000f, 0f);
            try
            {
                ZNetView.m_forceDisableInit = true;
                TerrainOp.m_forceDisableTerrainOps = true;
                GameObject copy = Object.Instantiate(source, stage.transform);
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = turn;
                copy.SetActive(true);
                if (bare) Strip(copy);
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                if (!bare) foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
                stage.SetActive(true);
                pose?.Invoke(copy);

                Bounds bounds = default;
                bool any = false;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || IsParticles(r)) continue;   // particles would throw the framing off
                    if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                }
                if (!any) return null;

                cam = frame(stage, bounds);
                cam.cullingMask = 1 << layer;
                rt = RenderTexture.GetTemporary(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                if (LooksFine(tex, minCover))
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                Object.Destroy(tex);
                return null;
            }
            finally
            {
                ZNetView.m_forceDisableInit = oldInit;
                TerrainOp.m_forceDisableTerrainOps = oldTerrain;
                RenderTexture.active = previous;
                if (cam != null) cam.targetTexture = null;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(stage);
            }
        }

        // ParticleSystemRenderer lives in a module the build doesn't reference, so it is known by name
        private static bool IsParticles(Renderer r) => r is TrailRenderer || r is LineRenderer || r.GetType().Name == "ParticleSystemRenderer";

        private static Camera AddCamera(GameObject stage)
        {
            var camGo = new GameObject("IconCamera");
            camGo.transform.SetParent(stage.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.cullingMask = 1 << Layer;
            return cam;
        }

        private static void AddLight(GameObject stage, Quaternion rotation, float intensity)
        {
            var lightGo = new GameObject("IconLight");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = rotation;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
        }

        /// <summary>
        /// A whole-prefab copy, still asleep: its scripts go (ZNetView, ItemDrop, Piece, WearNTear... none of them ever wakes), a script
        /// another one needs after the one needing it; colliders and physics are switched off.
        /// </summary>
        private static void Strip(GameObject copy)
        {
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            {
                var scripts = new List<MonoBehaviour>(t.GetComponents<MonoBehaviour>());
                for (int pass = 0; pass < 4 && scripts.Count > 0; pass++)
                    for (int i = scripts.Count - 1; i >= 0; i--)
                    {
                        if (scripts[i] == null) { scripts.RemoveAt(i); continue; }
                        if (Needed(scripts[i], scripts)) continue;
                        Object.DestroyImmediate(scripts[i]);
                        scripts.RemoveAt(i);
                    }
            }
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Rigidbody b in copy.GetComponentsInChildren<Rigidbody>(true)) { b.isKinematic = true; b.detectCollisions = false; }
        }

        private static bool Needed(MonoBehaviour script, List<MonoBehaviour> others)
        {
            Type type = script.GetType();
            foreach (MonoBehaviour other in others)
            {
                if (other == null || other == script) continue;
                foreach (RequireComponent r in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    if (Is(r.m_Type0, type) || Is(r.m_Type1, type) || Is(r.m_Type2, type)) return true;
            }
            return false;
        }

        private static bool Is(Type required, Type type) => required != null && required.IsAssignableFrom(type);

        /// <summary>Something was actually drawn: a fair share of the picture is covered, and it is not just black.</summary>
        private static bool LooksFine(Texture2D tex, float minCover)
        {
            Color32[] pixels = tex.GetPixels32();
            int covered = 0; long brightness = 0;
            for (int i = 0; i < pixels.Length; i += 7)
            {
                Color32 c = pixels[i];
                if (c.a > 40) { covered++; brightness += c.r + c.g + c.b; }
            }
            int sampled = pixels.Length / 7 + 1;
            return covered > sampled * minCover && brightness / Math.Max(1, covered) > 60;
        }

        /// <summary>A layer no one has named, for the copy, the camera and the light alone (the highest free one; 31 if all are named).</summary>
        private static int Layer
        {
            get
            {
                if (_layer < 0)
                {
                    _layer = 31;
                    for (int i = 31; i >= 8; i--)
                        if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _layer = i; break; }
                }
                return _layer;
            }
        }
        private static int _layer = -1;

        /// <summary>No drawing on a dedicated server started without graphics: the fallback picture is used.</summary>
        private static bool CanDraw => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        private static string ModName => typeof(Icon).Assembly.GetName().Name;

        // Texture2D.LoadImage lives in a module the build cannot reference directly, so it is called by name.
        private static readonly MethodInfo LoadImageMethod =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });

        private static bool LoadImage(Texture2D tex, byte[] data) => LoadImageMethod != null && (bool)LoadImageMethod.Invoke(null, new object[] { tex, data });

        /// <summary>A PNG built into the mod (its LogicalName), as a texture; null if it isn't there.</summary>
        internal static Texture2D LoadEmbedded(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            using (System.IO.Stream stream = typeof(Icon).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return LoadImage(tex, bytes) ? tex : null;
            }
        }

        /// <summary>A PNG built into the mod, as a sprite; null if it isn't there.</summary>
        internal static Sprite LoadSprite(string name)
        {
            Texture2D tex = LoadEmbedded(name);
            return tex == null ? null : Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
