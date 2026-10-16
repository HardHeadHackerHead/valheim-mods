using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Webslinger
{
    /// <summary>
    /// For making the suit (web suit probe): what the player's body is painted with, saved as pictures in BepInEx/claude/shots/suit:
    /// the body material's textures, an armour's chest and legs textures, and the body mesh's texture layout coloured by body part.
    /// </summary>
    internal static class SuitProbe
    {
        private static readonly MethodInfo Encode =
            Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });

        public static JObject Run(Player p, string armour)
        {
            var o = new JObject();
            string dir = Path.Combine(Paths.BepInExRootPath, "claude", "shots", "suit");
            Directory.CreateDirectory(dir);
            VisEquipment vis = p.GetComponent<VisEquipment>();
            SkinnedMeshRenderer body = vis != null ? vis.m_bodyModel : null;
            if (body == null) { o["error"] = "no body model"; return o; }
            Material mat = body.material;
            o["shader"] = mat.shader.name;
            o["materials"] = body.materials.Length;
            var props = new JArray();
            for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
            {
                string n = mat.shader.GetPropertyName(i);
                var t = mat.shader.GetPropertyType(i);
                string val = t == UnityEngine.Rendering.ShaderPropertyType.Texture ? (mat.GetTexture(n) != null ? mat.GetTexture(n).name + " " + mat.GetTexture(n).width + "x" + mat.GetTexture(n).height : "null")
                    : t == UnityEngine.Rendering.ShaderPropertyType.Color ? mat.GetColor(n).ToString()
                    : t == UnityEngine.Rendering.ShaderPropertyType.Vector ? mat.GetVector(n).ToString() : mat.GetFloat(n).ToString("0.###");
                props.Add(n + " (" + t + ") = " + val);
                if (t == UnityEngine.Rendering.ShaderPropertyType.Texture && mat.GetTexture(n) != null) Save(mat.GetTexture(n), Path.Combine(dir, "body" + n + ".png"));
            }
            o["properties"] = props;
            Material m1 = body.materials.Length > 1 ? body.materials[1] : null;
            if (m1 != null) o["material1"] = m1.shader.name + " " + (m1.mainTexture != null ? m1.mainTexture.name : "");

            Mesh mesh = body.sharedMesh;
            o["mesh"] = mesh.name + " readable=" + mesh.isReadable + " verts=" + mesh.vertexCount + " submeshes=" + mesh.subMeshCount;
            if (mesh.isReadable)
            {
                Vector3[] v = mesh.vertices;
                Vector2[] uv = mesh.uv;
                BoneWeight[] w = mesh.boneWeights;
                Transform[] bones = body.bones;
                o["bounds"] = mesh.bounds.ToString();
                const int size = 1024;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                Texture2D skin = Read(mat.GetTexture("_MainTex"), size);
                Color32[] under = skin != null ? skin.GetPixels32() : null;
                for (int i = 0; i < px.Length; i++) px[i] = under != null ? Color32.Lerp(new Color32(0, 0, 0, 255), under[i], 0.6f) : new Color32(0, 0, 0, 255);
                var region = new Color32[size * size];
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        Color32 col = RegionColour(bones, w[a]);
                        Fill(region, size, uv[a], uv[b], uv[c], col);
                    }
                }
                for (int i = 0; i < px.Length; i++) if (region[i].a > 0) px[i] = Color32.Lerp(px[i], region[i], 0.55f);
                o["uv2"] = mesh.uv2 != null ? mesh.uv2.Length : 0;
                Vector2 mn = Vector2.one * 9, mx = -Vector2.one * 9; foreach (Vector2 q in uv) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                o["uvRange"] = mn + " .. " + mx;
                tex.SetPixels32(px);
                tex.Apply();
                Save(tex, Path.Combine(dir, "body-uv-regions.png"));
                // the same layout coloured by which way each triangle faces (normal x, y, z as red, green, blue) and its height (brightness)
                Vector3[] nrm = mesh.normals;
                var facing = new Color32[size * size];
                Bounds bb = mesh.bounds;
                int tris0 = 0;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] tris = mesh.GetTriangles(sub);
                    if (sub == 0) tris0 = tris.Length / 3;
                    for (int t = 0; t < tris.Length; t += 3)
                    {
                        Vector3 n = (nrm[tris[t]] + nrm[tris[t + 1]] + nrm[tris[t + 2]]).normalized;
                        Fill(facing, size, uv[tris[t]], uv[tris[t + 1]], uv[tris[t + 2]], new Color32((byte)(127 + 127 * n.x), (byte)(127 + 127 * n.y), (byte)(127 + 127 * n.z), 255));
                    }
                }
                tex.SetPixels32(facing); tex.Apply();
                Save(tex, Path.Combine(dir, "body-uv-facing.png"));
                o["triangles"] = tris0 + " in submesh 0, " + (mesh.subMeshCount > 1 ? mesh.GetTriangles(1).Length / 3 : 0) + " in submesh 1";
                Vector3 mnv = Vector3.one * 9, mxv = -Vector3.one * 9; foreach (Vector3 q in v) { mnv = Vector3.Min(mnv, q); mxv = Vector3.Max(mxv, q); }
                o["vertRange"] = mnv.ToString("F4") + " .. " + mxv.ToString("F4");
                var names = new JArray();
                foreach (Transform bt in bones) names.Add(bt != null ? bt.name : "null");
                o["bones"] = names;
            }

            if (!string.IsNullOrEmpty(armour))
            {
                GameObject item = ObjectDB.instance.GetItemPrefab(armour);
                Material am = item != null ? item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_armorMaterial : null;
                if (am != null)
                {
                    foreach (string n in new[] { "_ChestTex", "_LegsTex", "_ChestBumpMap", "_ChestMetal" })
                        if (am.HasProperty(n) && am.GetTexture(n) != null) Save(am.GetTexture(n), Path.Combine(dir, armour + n + ".png"));
                    o["armour"] = armour + ": " + am.name;
                }
                else o["armour"] = armour + ": no armour material";
                if (item != null)
                {
                    var kids = new JArray();
                    foreach (Transform k in item.transform) kids.Add(k.name);
                    o["armourChildren"] = kids;
                }
            }
            o["folder"] = dir;
            return o;
        }

        private static Color32 RegionColour(Transform[] bones, BoneWeight w)
        {
            string n = w.boneIndex0 < bones.Length && bones[w.boneIndex0] != null ? bones[w.boneIndex0].name : "";
            if (n.Contains("Head") || n.Contains("Neck") || n.Contains("Jaw") || n.Contains("Eye")) return new Color32(255, 255, 255, 255);
            if (n.Contains("Hand") || n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky")) return new Color32(255, 220, 0, 255);
            if (n.Contains("ForeArm")) return new Color32(255, 120, 0, 255);
            if (n.Contains("Arm") || n.Contains("Shoulder")) return new Color32(255, 0, 0, 255);
            if (n.Contains("UpLeg")) return new Color32(0, 90, 255, 255);
            if (n.Contains("Foot") || n.Contains("Toe")) return new Color32(0, 255, 120, 255);
            if (n.Contains("Leg")) return new Color32(0, 200, 255, 255);
            if (n.Contains("Hips")) return new Color32(160, 0, 255, 255);
            if (n.Contains("Spine")) return new Color32(255, 0, 200, 255);
            return new Color32(90, 90, 90, 255);
        }

        internal static void Fill(Color32[] px, int size, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            Vector2 A = a * size, B = b * size, C = c * size;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x))), 0, size - 1), x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y))), 0, size - 1), y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))), 0, size - 1);
            float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 P = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = ((B.x - P.x) * (C.y - P.y) - (C.x - P.x) * (B.y - P.y)) / area;
                    float w1 = ((C.x - P.x) * (A.y - P.y) - (A.x - P.x) * (C.y - P.y)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                    px[y * size + x] = col;
                }
        }

        internal static Texture2D Read(Texture src, int size)
        {
            if (src == null) return null;
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return t;
        }

        internal static void Save(Texture src, string path)
        {
            int w = Mathf.Min(src.width, 1024), h = Mathf.Min(src.height, 1024);
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            if (Encode != null) File.WriteAllBytes(path, (byte[])Encode.Invoke(null, new object[] { t }));
            UnityEngine.Object.Destroy(t);
        }
    }
}
