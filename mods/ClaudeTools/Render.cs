using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// Looking at things on their own: "render" photographs any piece, item or creature (by its prefab name) built out of sight with its real
    /// materials, under its own camera and light, so a model can be checked and improved without placing it (and the build menu's copy is the
    /// one a hot reload updates). "inspect" lists what an object is made of. "errors" and "waitfor" help the build-and-look loop.
    /// </summary>
    public partial class Plugin
    {
        private static readonly Vector3 StageSpot = new Vector3(0f, 4300f, 0f);   // far above the middle of the world: nothing else there

        private static GameObject FindPrefab(string name)
        {
            GameObject p = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (p == null && ObjectDB.instance != null) p = ObjectDB.instance.GetItemPrefab(name);
            if (p == null && ObjectDB.instance != null)   // pieces only on a tool's build list (terrain tools and the like)
                foreach (GameObject item in ObjectDB.instance.m_items)
                {
                    PieceTable table = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces : null;
                    GameObject found = table?.m_pieces.FirstOrDefault(x => x != null && x.name == name);
                    if (found != null) return found;
                }
            return p;
        }

        private static Dictionary<string, string> Options(string[] a, int from)
        {
            var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string w in a.Skip(from)) { int eq = w.IndexOf('='); if (eq > 0) o[w.Substring(0, eq)] = w.Substring(eq + 1); }
            return o;
        }

        private static float Opt(Dictionary<string, string> o, string key, float fallback) =>
            o.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;

        private static bool OptVec(Dictionary<string, string> o, string key, out Vector3 v)
        {
            v = Vector3.zero;
            if (!o.TryGetValue(key, out string s)) return false;
            string[] p = s.Split(',');
            if (p.Length != 3) return false;
            float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v.x);
            float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v.y);
            float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v.z);
            return true;
        }

        private static int FreeLayer()
        {
            for (int l = 29; l > 20; l--) if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l;
            return 29;
        }

        private void RegisterRenderCommands()
        {
            Builtin("render", "render <prefab> [yaw=25] [pitch=12] [views=1|4] [focus=x,y,z] [dist=m] [fov=30] [size=1280x720] [bg=sky|dark|clear]: a picture of any piece, item or creature on its own, with its real materials (yaw 0 = from its front, -z); focus and dist for a close-up of one part",
                (a, output, error) => a.Length < 2 ? Fail(error, "render <prefab name>") : RenderCommand(a, output, error));

            Builtin("inspect", "inspect <prefab> [depth=3]: what an object is made of: its components, parts (position, size), colliders, meshes and materials", (a, output, error) =>
            {
                if (a.Length < 2) { error("inspect <prefab name>"); return null; }
                GameObject prefab = FindPrefab(a[1]);
                if (prefab == null) { error($"no prefab called '{a[1]}'"); return null; }
                int depth = Mathf.Clamp(I(a, 2, 3), 1, 8);
                if (a.Length > 2 && a[2].StartsWith("depth=")) int.TryParse(a[2].Substring(6), out depth);
                var parts = new JArray();
                void Walk(Transform t, string path, int level)
                {
                    if (parts.Count >= 250) return;
                    var info = new JObject { ["path"] = path, ["pos"] = Vec(t.localPosition), ["active"] = t.gameObject.activeSelf };
                    if (t.localEulerAngles != Vector3.zero) info["rot"] = Vec(t.localEulerAngles);
                    if (t.localScale != Vector3.one) info["scale"] = Vec(t.localScale);
                    var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
                    if (comps.Length > 0) info["components"] = new JArray(comps);
                    MeshFilter mf = t.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) info["mesh"] = mf.sharedMesh.name + " " + Vec(mf.sharedMesh.bounds.size);
                    var cols = new JArray();   // its colliders' shapes (sizes, for measuring: how wide a player is, what fits through a gap)
                    foreach (Collider col in t.GetComponents<Collider>())
                    {
                        if (col is CapsuleCollider cc) cols.Add($"capsule radius {cc.radius:0.###} height {cc.height:0.###} centre {Vec(cc.center)} axis {cc.direction}{(cc.isTrigger ? " (trigger)" : "")}");
                        else if (col is BoxCollider bc) cols.Add($"box {Vec(bc.size)} centre {Vec(bc.center)}{(bc.isTrigger ? " (trigger)" : "")}");
                        else if (col is SphereCollider sc) cols.Add($"sphere radius {sc.radius:0.###} centre {Vec(sc.center)}{(sc.isTrigger ? " (trigger)" : "")}");
                        else if (col is MeshCollider mc) cols.Add($"mesh {(mc.sharedMesh != null ? mc.sharedMesh.name + " " + Vec(mc.sharedMesh.bounds.size) : "?")}{(mc.isTrigger ? " (trigger)" : "")}");
                    }
                    if (cols.Count > 0) info["colliders"] = cols;
                    Renderer r = t.GetComponent<Renderer>();
                    if (r != null) info["materials"] = new JArray(r.sharedMaterials.Where(m => m != null).Select(m => m.name + " (" + (m.shader != null ? m.shader.name : "?") + ")" + (m.HasProperty("_Color") ? " " + ColorString(m.color) : "")));
                    parts.Add(info);
                    if (level < depth) foreach (Transform c in t) Walk(c, path + "/" + c.name, level + 1);
                }
                Walk(prefab.transform, prefab.name, 0);
                output(new JObject { ["prefab"] = prefab.name, ["parts"] = parts, ["note"] = parts.Count >= 250 ? "cut at 250 parts: use a smaller depth" : null });
                return null;
            });

            Builtin("colliders", "colliders <prefab> [prefab...]: each piece's solid colliders in its own frame (as the game's support check sees them: box centre, rotation, size; other shapes as bounds) and its support settings (centre of mass, material, whether it holds others up)", (a, output, error) =>
            {
                if (a.Length < 2) { error("colliders <prefab> [prefab...]"); return null; }
                var all = new JObject();
                foreach (string name in a.Skip(1))
                {
                    GameObject prefab = FindPrefab(name);
                    if (prefab == null) { all[name] = "no such prefab"; continue; }
                    Transform root = prefab.transform;
                    var list = new JArray();
                    foreach (Collider col in prefab.GetComponentsInChildren<Collider>(true))
                    {
                        if (col.isTrigger || col.attachedRigidbody != null) continue;
                        Transform t = col.transform;
                        Matrix4x4 toRoot = root.worldToLocalMatrix * t.localToWorldMatrix;
                        Quaternion rot = Quaternion.Inverse(root.rotation) * t.rotation;
                        Vector3 scale = new Vector3(toRoot.GetColumn(0).magnitude, toRoot.GetColumn(1).magnitude, toRoot.GetColumn(2).magnitude);
                        var c = new JObject { ["path"] = t.name, ["layer"] = LayerMask.LayerToName(t.gameObject.layer) };
                        if (col is BoxCollider bc)
                        {
                            c["type"] = "box";
                            c["centre"] = Vec(toRoot.MultiplyPoint3x4(bc.center));
                            c["rot"] = new JArray(rot.x, rot.y, rot.z, rot.w);
                            c["size"] = Vec(Vector3.Scale(scale, bc.size));
                        }
                        else
                        {
                            Bounds lb = col is MeshCollider mc && mc.sharedMesh != null ? mc.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
                            if (col is CapsuleCollider cc) lb = new Bounds(cc.center, cc.direction == 0 ? new Vector3(cc.height, cc.radius * 2, cc.radius * 2) : cc.direction == 1 ? new Vector3(cc.radius * 2, cc.height, cc.radius * 2) : new Vector3(cc.radius * 2, cc.radius * 2, cc.height));
                            if (col is SphereCollider sc) lb = new Bounds(sc.center, Vector3.one * sc.radius * 2);
                            c["type"] = col is MeshCollider m2 ? (m2.convex ? "mesh-convex" : "mesh") : col.GetType().Name;
                            c["centre"] = Vec(toRoot.MultiplyPoint3x4(lb.center));      // its own box, turned with its part (the game takes the world bounds of this)
                            c["rot"] = new JArray(rot.x, rot.y, rot.z, rot.w);
                            c["size"] = Vec(Vector3.Scale(scale, lb.size));
                        }
                        list.Add(c);
                    }
                    WearNTear w = prefab.GetComponent<WearNTear>();
                    var info = new JObject { ["colliders"] = list };
                    if (w != null)
                    {
                        info["com"] = Vec(w.m_comOffset);
                        info["material"] = w.m_materialType.ToString();
                        info["supports"] = w.m_supports;
                        info["noSupportWear"] = w.m_noSupportWear;
                    }
                    all[prefab.name] = info;
                }
                output(new JObject { ["colliders"] = all });
                return null;
            });

            Builtin("support", "support [radius=20] [place=look] [filter]: built pieces near a place with the support the game gives them now (and their material's minimum and maximum), weakest first", (a, output, error) =>
            {
                float radius = a.Length > 1 && float.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rr) ? rr : 20f;
                if (!Frame(a.Length > 2 ? a[2] : "look", out Vector3 at, out float _, out string err)) { error(err); return null; }
                string filter = a.Length > 3 ? a[3] : null;
                var getSupport = HarmonyLib.AccessTools.Method(typeof(WearNTear), "GetSupport");
                var getProps = HarmonyLib.AccessTools.Method(typeof(WearNTear), "GetMaterialProperties");
                var rows = new List<KeyValuePair<float, JObject>>();
                foreach (WearNTear w in UnityEngine.Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None))
                {
                    if (w == null || (w.transform.position - at).sqrMagnitude > radius * radius) continue;
                    string name = w.gameObject.name.Replace("(Clone)", "").Trim();
                    if (filter != null && !name.Contains(filter)) continue;
                    float s = (float)getSupport.Invoke(w, null);
                    var args = new object[] { 0f, 0f, 0f, 0f };
                    getProps.Invoke(w, args);
                    float max = (float)args[0], min = (float)args[1];
                    rows.Add(new KeyValuePair<float, JObject>(max > 0 ? (s - min) / max : 0f, new JObject { ["piece"] = name, ["support"] = Math.Round(s, 1), ["min"] = min, ["max"] = max,
                        ["position"] = Vec(w.transform.position), ["health"] = Math.Round(w.GetHealthPercentage() * 100f) }));
                }
                output(new JObject { ["pieces"] = new JArray(rows.OrderBy(r => r.Key).Take(60).Select(r => (JToken)r.Value)), ["count"] = rows.Count });
                return null;
            });

            Builtin("errors", "errors: the new errors and exceptions in the log since the last time you asked (with where they came from)", (a, output, error) =>
            {
                string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                var found = new JArray();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (_logSeen > fs.Length) _logSeen = 0;   // a new log (the game restarted)
                    fs.Seek(_logSeen, SeekOrigin.Begin);
                    var reader = new StreamReader(fs);
                    var lines = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                    _logSeen = fs.Length;
                    for (int i = 0; i < lines.Count && found.Count < 30; i++)
                    {
                        string l = lines[i];
                        if (!(l.StartsWith("[Error") || l.StartsWith("[Fatal") || l.Contains("Exception:") || l.StartsWith("[Warning") && l.Contains("Exception"))) continue;
                        var trace = new List<string>();
                        for (int k = i + 1; k < lines.Count && trace.Count < 8 && lines[k].Length > 0 && !lines[k].StartsWith("["); k++) trace.Add(lines[k].Trim());
                        found.Add(new JObject { ["error"] = l.Length > 300 ? l.Substring(0, 300) : l, ["where"] = new JArray(trace) });
                    }
                }
                output(new JObject { ["errors"] = found, ["note"] = found.Count == 0 ? "none since the last check" : null });
                return null;
            });

            Builtin("waitfor", "waitfor <mod> [version] [seconds=30]: wait until a mod (that version, if given) is loaded: after a rebuild, before looking at it", (a, output, error) =>
                a.Length < 2 ? Fail(error, "waitfor <mod> [version]") : WaitForMod(a[1], a.Length > 2 && !a[2].Contains("=") && char.IsDigit(a[2][0]) ? a[2] : null, Opt(Options(a, 2), "seconds", 30f), output, error));
        }

        private long _logSeen;

        private static IEnumerator Fail(Action<string> error, string message) { error(message); return null; }

        private static string ColorString(Color c) => $"#{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";

        private IEnumerator WaitForMod(string name, string version, float seconds, Action<JObject> output, Action<string> error)
        {
            float until = Time.unscaledTime + Mathf.Clamp(seconds, 1f, 120f);
            while (Time.unscaledTime < until)
            {
                BaseUnityPlugin mod = FindMod(name);
                string have = mod != null ? MetadataHelper.GetMetadata(mod)?.Version?.ToString() : null;
                if (mod != null && (version == null || have == version || have == version + ".0"))
                {
                    output(new JObject { ["loaded"] = name, ["version"] = have });
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            error($"{name}{(version != null ? " " + version : "")} did not load within {seconds:0} s");
        }

        private IEnumerator RenderCommand(string[] a, Action<JObject> output, Action<string> error)
        {
            GameObject prefab = FindPrefab(a[1]);
            if (prefab == null) { error($"no prefab called '{a[1]}'"); yield break; }
            var o = Options(a, 2);
            int w = 1280, h = 720;
            if (o.TryGetValue("size", out string size))
            {
                string[] wh = size.ToLowerInvariant().Split('x');
                if (wh.Length == 2) { int.TryParse(wh[0], out w); int.TryParse(wh[1], out h); }
            }
            w = Mathf.Clamp(w, 64, 2560); h = Mathf.Clamp(h, 64, 2560);
            float fov = Mathf.Clamp(Opt(o, "fov", 30f), 5f, 90f);
            float pitch = Opt(o, "pitch", 12f);
            int views = Mathf.Clamp((int)Opt(o, "views", 1f), 1, 4);
            float[] yaws = views == 1 ? new[] { Opt(o, "yaw", 25f) } : new[] { 0f, 35f, 90f, 180f };   // front, three-quarter, side, back
            string bg = o.TryGetValue("bg", out string b) ? b.ToLowerInvariant() : "sky";

            int layer = FreeLayer();
            var root = new GameObject("ClaudeRenderStage");
            root.transform.position = StageSpot;
            GameObject copy;
            ZNetView.m_forceDisableInit = true;   // a copy that is not a networked object (nothing is saved or shared)
            try { copy = Instantiate(prefab, StageSpot, Quaternion.identity, root.transform); }
            finally { ZNetView.m_forceDisableInit = false; }
            try
            {
                foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                foreach (AudioSource s in copy.GetComponentsInChildren<AudioSource>(true)) s.enabled = false;
                foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
                foreach (CircleProjector ring in copy.GetComponentsInChildren<CircleProjector>(true)) ring.gameObject.SetActive(false);
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                yield return null;   // let it settle for a frame (some models finish building in Start)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                Bounds box = default; bool any = false;
                foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r is LineRenderer || !r.enabled || r.bounds.extents.magnitude > 60f) continue;
                    if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds);
                }
                if (!any) { error("nothing to see on " + prefab.name + " (no visible parts)"); yield break; }
                bool focused = OptVec(o, "focus", out Vector3 focus);
                Vector3 target = focused ? StageSpot + focus : box.center;
                float radius = focused ? 0.5f : box.extents.magnitude;
                float dist = Opt(o, "dist", radius / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad) * 1.02f);

                yield return new WaitForEndOfFrame();
                var camGo = new GameObject("ClaudeRenderCamera");
                var lightGo = new GameObject("ClaudeRenderLight");
                var fillGo = new GameObject("ClaudeRenderFill");
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                bool fog = RenderSettings.fog;
                Color ambient = RenderSettings.ambientLight;
                var ambientMode = RenderSettings.ambientMode;
                try
                {
                    Light sun = lightGo.AddComponent<Light>();
                    sun.type = LightType.Directional; sun.color = new Color(1f, 0.96f, 0.88f); sun.intensity = 1.3f; sun.shadows = LightShadows.Soft; sun.cullingMask = 1 << layer;
                    Light fill = fillGo.AddComponent<Light>();
                    fill.type = LightType.Directional; fill.color = new Color(0.75f, 0.82f, 1f); fill.intensity = 0.45f; fill.cullingMask = 1 << layer;
                    Camera cam = camGo.AddComponent<Camera>();
                    cam.enabled = false; cam.cullingMask = 1 << layer; cam.fieldOfView = fov; cam.nearClipPlane = 0.02f; cam.farClipPlane = dist + radius * 4f + 20f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = bg == "dark" ? new Color(0.12f, 0.13f, 0.14f) : bg == "clear" ? new Color(0f, 0f, 0f, 0f) : new Color(0.62f, 0.72f, 0.78f);
                    cam.targetTexture = rt;
                    RenderSettings.fog = false;
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.6f, 0.61f, 0.64f);
                    for (int i = 0; i < yaws.Length; i++)
                    {
                        // yaw 0 looks at the front (the model's -z side, as the game's pieces face); the light comes from over the viewer's shoulder
                        Quaternion look = Quaternion.Euler(pitch, yaws[i], 0f);
                        cam.transform.SetPositionAndRotation(target - look * Vector3.forward * dist, look);
                        lightGo.transform.rotation = Quaternion.Euler(45f, yaws[i] + 30f, 0f);
                        fillGo.transform.rotation = Quaternion.Euler(20f, yaws[i] - 120f, 0f);
                        cam.Render();
                        var prev = RenderTexture.active;
                        RenderTexture.active = rt;
                        var tex = new Texture2D(w, h, bg == "clear" ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tex.Apply();
                        RenderTexture.active = prev;
                        string file = Path.Combine(ShotDir, $"{Stamp()}-render-{prefab.name}{(yaws.Length > 1 ? "-" + (int)yaws[i] : "")}.png");
                        Directory.CreateDirectory(ShotDir);
                        File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
                        Destroy(tex);
                        output(new JObject { ["render"] = file, ["yaw"] = yaws[i] });
                    }
                    output(new JObject { ["size"] = Vec(box.size), ["centre"] = Vec(box.center - StageSpot), ["note"] = "positions are metres from the prefab's origin" });
                }
                finally
                {
                    RenderSettings.fog = fog; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                    RenderTexture.ReleaseTemporary(rt);
                    Destroy(camGo); Destroy(lightGo); Destroy(fillGo);
                }
            }
            finally { Destroy(root); }
        }
    }
}
