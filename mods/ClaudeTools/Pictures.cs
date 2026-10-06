using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>Pictures and the lie of the land: your own view, a separate camera anywhere (your character does not move), and ground surveys.</summary>
    public partial class Plugin
    {
        /// <summary>What you see, saved as a PNG at the end of this frame (with a .json beside it saying where you were).</summary>
        internal IEnumerator Screenshot(string path, int maxWidth, Action<string> done)
        {
            yield return new WaitForEndOfFrame();
            string saved = null;
            try
            {
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (maxWidth > 0 && tex.width > maxWidth) tex = Shrink(tex, maxWidth);
                saved = SavePng(tex, path);
                Destroy(tex);
                WriteShotInfo(path);
            }
            catch (Exception e) { Logger.LogWarning("Screenshot failed: " + e.Message); }
            done?.Invoke(saved);
        }

        private static Texture2D Shrink(Texture2D tex, int width)
        {
            int height = Mathf.RoundToInt(tex.height * (width / (float)tex.width));
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(tex, rt);
            Texture2D small = ReadBack(rt);
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
            return small;
        }

        private static Texture2D ReadBack(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }

        private static string SavePng(Texture2D tex, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            File.Copy(path, Path.Combine(Path.GetDirectoryName(path), "latest.png"), true);
            return path;
        }

        private static void WriteShotInfo(string png)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            Transform cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
            var info = new JObject
            {
                ["time"] = DateTime.Now.ToString("s"),
                ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                ["player"] = Vec(player.transform.position),
                ["playerYaw"] = Math.Round(player.transform.eulerAngles.y, 1),
                ["camera"] = cam != null ? Vec(cam.position) : null,
                ["cameraYaw"] = cam != null ? Math.Round(cam.eulerAngles.y, 1) : 0,
                ["cameraPitch"] = cam != null ? Math.Round(cam.eulerAngles.x, 1) : 0,
            };
            if (LookPoint(out Vector3 hit)) info["lookingAt"] = Vec(hit);
            File.WriteAllText(Path.ChangeExtension(png, ".json"), info.ToString());
        }

        /// <summary>A picture from a separate camera at any spot (the character does not move). Only the world near the player is loaded.</summary>
        internal string RenderView(Vector3 position, Quaternion rotation, int width, int height, float fov, string path, bool orthographic = false, float orthoSize = 20f)
        {
            Camera main = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            var go = new GameObject("ClaudeViewCamera");
            RenderTexture rt = RenderTexture.GetTemporary(Mathf.Clamp(width, 64, 4096), Mathf.Clamp(height, 64, 4096), 24, RenderTextureFormat.ARGB32);
            try
            {
                Camera cam = go.AddComponent<Camera>();
                if (main != null) cam.CopyFrom(main);
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
                cam.orthographic = orthographic;
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 600f);
                cam.targetTexture = rt;
                cam.Render();
                Texture2D tex = ReadBack(rt);
                string saved = SavePng(tex, path);
                Destroy(tex);
                return saved;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Destroy(go);
            }
        }

        /// <summary>Ground heights on a grid around a point, the water level, the biome and the buildings there (BepInEx/claude/_survey.json).</summary>
        internal static string Survey(Vector3 centre, float radius, float step)
        {
            ZoneSystem zone = ZoneSystem.instance;
            float baseY = zone.GetGroundHeight(centre);
            int n = Mathf.Clamp(Mathf.RoundToInt(radius / step), 1, 128);
            var ground = new JArray();
            var solid = new JArray();
            float lowest = float.MaxValue, highest = float.MinValue;
            for (int iz = -n; iz <= n; iz++)
            {
                var gRow = new JArray();
                var sRow = new JArray();
                for (int ix = -n; ix <= n; ix++)
                {
                    var p = new Vector3(centre.x + ix * step, 0f, centre.z + iz * step);
                    float g = zone.GetGroundHeight(p) - baseY;
                    float s = zone.GetSolidHeight(p, out float sh, 1000) ? sh - baseY : g;
                    gRow.Add(Math.Round(g, 2));
                    sRow.Add(Math.Round(s, 2));
                    lowest = Mathf.Min(lowest, g); highest = Mathf.Max(highest, g);
                }
                ground.Add(gRow);
                solid.Add(sRow);
            }

            var pieces = new List<Piece>();
            Piece.GetAllPiecesInRadius(centre, radius, pieces);
            var existing = new JArray();
            foreach (Piece pc in pieces.Take(2000))
            {
                Vector3 local = pc.transform.position - new Vector3(centre.x, baseY, centre.z);
                existing.Add(new JObject { ["p"] = Utils.GetPrefabName(pc.gameObject), ["x"] = Math.Round(local.x, 2), ["y"] = Math.Round(local.y, 2), ["z"] = Math.Round(local.z, 2), ["ry"] = Math.Round(pc.transform.eulerAngles.y, 1) });
            }

            Player player = Player.m_localPlayer;
            var doc = new JObject
            {
                ["note"] = "Heights are metres above the ground at the centre. Rows run from -z (south) to +z, columns from -x (west) to +x, one every 'step' metres. 'solid' includes rocks, trees and buildings; 'ground' is the terrain only. Pieces are relative to the centre at its ground height.",
                ["time"] = DateTime.Now.ToString("s"),
                ["world"] = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                ["centre"] = Vec(new Vector3(centre.x, baseY, centre.z)),
                ["playerYaw"] = player != null ? Math.Round(player.transform.eulerAngles.y, 1) : 0,
                ["biome"] = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(centre).ToString() : "",
                ["radius"] = radius, ["step"] = step,
                ["waterAboveCentre"] = Math.Round(zone.m_waterLevel - baseY, 2),
                ["lowest"] = Math.Round(lowest, 2), ["highest"] = Math.Round(highest, 2),
                ["ground"] = ground, ["solid"] = solid, ["pieces"] = existing,
            };
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, "_survey.json");
            File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.None));
            return path;
        }

        private void RegisterPictureCommands()
        {
            Builtin("shot", "shot [width]: what the player sees", (a, output, error) => ShotCommand(a, output, error));

            Builtin("view", "view <place> <x> <y> <z> <yaw> <pitch> [fov] [width] [height]: a separate camera at a spot measured from a place (world, here, look, or a name)", (a, output, error) =>
            {
                if (!Frame(a.Length > 1 ? a[1] : "look", out Vector3 origin, out float yaw, out string err)) { error(err); return null; }
                Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
                Vector3 pos = origin + turn * new Vector3(F(a, 2, 0), F(a, 3, 10), F(a, 4, -20));
                Quaternion rot = turn * Quaternion.Euler(F(a, 6, 20), F(a, 5, 0), 0f);
                output(new JObject { ["view"] = RenderView(pos, rot, I(a, 8, 1280), I(a, 9, 720), F(a, 7, 55), Path.Combine(ShotDir, Stamp() + "-view.png")) });
                return null;
            });

            Builtin("orbit", "orbit <place> [radius] [pitch] [count] [width] [height]: pictures from all round a place", (a, output, error) =>
            {
                if (!Frame(a.Length > 1 ? a[1] : "look", out Vector3 origin, out float yaw, out string err)) { error(err); return null; }
                float radius = F(a, 2, 28), pitch = F(a, 3, 30);
                int count = Mathf.Clamp(I(a, 4, 4), 1, 12);
                Vector3 target = origin + Vector3.up * 2f;
                for (int k = 0; k < count; k++)
                {
                    float angle = yaw + 180f + 45f + k * 360f / count;
                    Quaternion look = Quaternion.Euler(pitch, angle, 0f);
                    output(new JObject { ["orbit"] = RenderView(target - look * Vector3.forward * radius, look, I(a, 5, 1280), I(a, 6, 720), 50f, Path.Combine(ShotDir, Stamp() + "-orbit" + k + ".png")) });
                }
                return null;
            });

            Builtin("top", "top <place> [size] [width]: straight down over a place", (a, output, error) =>
            {
                if (!Frame(a.Length > 1 ? a[1] : "look", out Vector3 origin, out float yaw, out string err)) { error(err); return null; }
                float size = F(a, 2, 30);
                output(new JObject { ["top"] = RenderView(origin + Vector3.up * 80f, Quaternion.Euler(90f, yaw, 0f), I(a, 3, 1024), I(a, 3, 1024), 50f, Path.Combine(ShotDir, Stamp() + "-top.png"), true, size / 2f) });
                return null;
            });

            Builtin("survey", "survey [radius] [step] [place]: ground heights, water, biome and buildings around a place (default: where the player looks) into _survey.json", (a, output, error) =>
            {
                if (!Frame(a.Length > 3 ? a[3] : "look", out Vector3 at, out float _, out string err)) { error(err); return null; }
                output(new JObject { ["survey"] = Survey(at, Mathf.Clamp(F(a, 1, _surveyRadius.Value), 2f, 128f), Mathf.Max(0.25f, F(a, 2, 1f))) });
                return null;
            });
        }

        private IEnumerator ShotCommand(string[] a, Action<JObject> output, Action<string> error)
        {
            string got = null;
            yield return Screenshot(Path.Combine(ShotDir, Stamp() + ".png"), I(a, 1, 0), p => got = p);
            if (got != null) output(new JObject { ["shot"] = got }); else error("the screenshot failed");
        }
    }
}
