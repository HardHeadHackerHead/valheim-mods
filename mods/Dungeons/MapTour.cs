using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Dungeons
{
    /// <summary>
    /// `dungeon tour [id] [rooms] [views] [bright]`: pictures of every room of a built dungeon, for whoever makes a pack (an AI among them) to see what
    /// it built, taken by the CAMERA ONLY. The player is never moved, held or teleported (moving them has killed them), so it works with
    /// them standing outside: the dungeon is built on this game while its entrance is loaded.
    ///
    /// Each picture is taken at the end of a frame, after the screen has been drawn: the game's own camera is put at the view, made to draw
    /// once into a picture of its own (so the game's post effects apply, and the HUD, drawn over the screen, is never in it), and put back
    /// exactly as it was before the next frame, so the screen never shows any of it. For that one drawing the dungeon is lit as its player
    /// would see it inside: its pack's environment (ambient light, fog, the sun's light as the game sets them for it) and the torches and
    /// braziers near the view switched on as the game's light LOD switches on lights near a player (it measures from the player, who may be
    /// far below). All of it is put back in the same instant. One picture a frame.
    ///
    /// Views per room: 1 from the doorway it's entered by (the one towards the entrance): on the room's centrepiece if it has one (a boss
    /// arena, a fight, a chest, a pedestal, the way out...: near enough to fill the picture, from a little above), else 1.5 m in, at head
    /// height and a little above, looking at the room's far middle; 2 across, from the far side looking back; 3 and on from its other doorways, and for a room two or
    /// more floors tall, from high up looking down. Pictures go to BepInEx/claude/shots/tour/&lt;pack&gt;-&lt;seed&gt;/&lt;room&gt;-&lt;n&gt;.png
    /// with tour.json beside them (each picture's room, camera and target). "bright" lights the pictures more than the game does (the ambient
    /// raised to a grey dusk, the fog thinned, a soft light at the camera), so shapes, gaps and floating props show in a dark room: its
    /// files end in "-bright" and tour.json says so. Left out, the pictures are as the game draws them.
    /// </summary>
    internal static class DungeonTour
    {
        private const float Inward = 1.5f, Eye = 2.6f, Fov = 72f;
        private static readonly FieldInfo LodLights = AccessTools.Field(typeof(LightLod), "m_lights");
        private static readonly FieldInfo LodRange = AccessTools.Field(typeof(LightLod), "m_baseRange");
        private static readonly FieldInfo LodShadow = AccessTools.Field(typeof(LightLod), "m_baseShadowStrength");
        private static readonly FieldInfo LodLight = AccessTools.Field(typeof(LightLod), "m_light");
        private static readonly int SunColor = Shader.PropertyToID("_SunColor"), AmbientColor = Shader.PropertyToID("_AmbientColor"),
                                    SunFogColor = Shader.PropertyToID("_SunFogColor");
        private static bool _running;

        /// <summary>A view: where the camera stands and what it looks at (world), and what it's called.</summary>
        private struct View
        {
            public Vector3 At, Target;
            public string What;
        }

        public static IEnumerator Run(string[] args, Action<JObject> output, Action<string> error)
        {
            if (_running) { error("a tour is already running"); yield break; }
            Player p = Player.m_localPlayer;
            Camera cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Utils.GetMainCamera();
            if (p == null || cam == null) { error("no player or camera: load a world first"); yield break; }

            // the arguments: [id] [rooms] [views], in that order, each may be left out
            List<string> rest = args.Skip(1).ToList();
            Site site = null;
            if (rest.Count > 0) { site = Find(rest[0]); if (site != null) rest.RemoveAt(0); }
            if (site == null)
                site = Dungeons.Containing(p.transform.position)
                       ?? Dungeons.Sites.Where(s => s.Ready && s.HasOrigin).OrderBy(s => Dungeons.FlatDistance(s.EntrancePos, p.transform.position)).FirstOrDefault();
            if (site == null) { error("no dungeon built near the player (stand by its entrance: it's built while the entrance is loaded)"); yield break; }
            if (!site.Ready || !site.HasOrigin || site.Layout == null) { error($"dungeon {site.Id} isn't built here yet: {site.Problem ?? "wait for it"}"); yield break; }
            MapLayout map = DungeonMap.MapOf(site.Layout);
            int views = 2;
            string roomsArg = "all";
            bool roomsGiven = false, bright = false;
            foreach (string a in rest)
            {
                if (a.Equals("bright", StringComparison.OrdinalIgnoreCase)) { bright = true; continue; }
                if (a.StartsWith("views=")) { int.TryParse(a.Substring(6), out views); continue; }
                if (a.StartsWith("rooms=")) { roomsArg = a.Substring(6); roomsGiven = true; continue; }
                if (roomsGiven && int.TryParse(a, out int n) && n > 0 && n <= 8) { views = n; continue; }
                roomsArg = a;
                roomsGiven = true;
            }
            views = Mathf.Clamp(views, 1, 6);
            List<int> rooms = Rooms(site.Layout, map, roomsArg, out string bad);
            if (bad != null) { error(bad); yield break; }

            string name = $"{site.PackName}-{site.Seed}";
            string dir = Path.Combine(Paths.BepInExRootPath, "claude", "shots", "tour", name);
            Directory.CreateDirectory(dir);
            var shots = new JArray();
            var problems = new JArray();
            var notLoaded = new JArray();
            _running = true;
            try
            {
                foreach (int room in rooms)
                {
                    PlacedRoom r = site.Layout.Rooms[room];
                    List<View> list = Views(site, map, room, views);
                    string missing = Unloaded(site, room);
                    if (missing != null) notLoaded.Add($"{room} {r.Def.Name}: {missing}");
                    for (int k = 0; k < list.Count; k++)
                    {
                        yield return new WaitForEndOfFrame();
                        if (site.Gone || !site.Ready) { error("the dungeon was taken down during the tour"); yield break; }
                        string file = Path.Combine(dir, $"{room:00}-{Clean(r.Def.Name)}-{k + 1}{(bright ? "-bright" : "")}.png");
                        string why = Shoot(site, cam, list[k], file, bright);
                        if (why != null) { problems.Add($"{room} {r.Def.Name} view {k + 1}: {why}"); continue; }
                        shots.Add(new JObject
                        {
                            ["file"] = Path.GetFileName(file), ["room"] = room, ["name"] = r.Def.Name, ["view"] = list[k].What,
                            ["camera"] = V(list[k].At - site.Origin), ["target"] = V(list[k].Target - site.Origin),
                            ["bright"] = bright,
                        });
                    }
                }
            }
            finally { _running = false; }
            var summary = new JObject
            {
                ["dungeon"] = $"{site.PackName} seed {site.Seed} ({site.Id})",
                ["folder"] = dir,
                ["rooms"] = rooms.Count,
                ["pictures"] = shots.Count,
                ["player"] = site.Contains(p.transform.position) ? "inside (not moved)" : "outside (not moved)",
                ["environment"] = EnvOf(site)?.m_name ?? "(the game's current one)",
                ["lighting"] = bright ? "bright: brighter than in the game (ambient raised, fog thinned, a light at the camera), to see shapes; not how it looks"
                                      : "as in the game",
            };
            if (problems.Count > 0) summary["problems"] = problems;
            if (notLoaded.Count > 0)
            {
                // so an empty dais isn't taken for a missing chest
                summary["notInPictures"] = notLoaded;
                summary["notInPicturesWhy"] = "saved parts (chests, doors...) exist only where their zone is loaded on this game, and only once the "
                                              + "dungeon's owner has made them: stand nearer, or go in, and tour again to see them";
            }
            try { File.WriteAllText(Path.Combine(dir, "tour.json"), new JObject { ["summary"] = summary.DeepClone(), ["shots"] = shots }.ToString(Formatting.Indented)); }
            catch (Exception) { }
            summary["shots"] = shots;
            output(summary);
        }

        private static readonly AccessTools.FieldRef<ZNetScene, Dictionary<ZDO, ZNetView>> Instances = AccessTools.FieldRefAccess<ZNetScene, Dictionary<ZDO, ZNetView>>("m_instances");
        private static readonly int PartOf = Keys.PartOf.GetStableHashCode();

        /// <summary>A room's saved parts that aren't in this game now (one of ours: its "saved" parts you'd see, not spawners), as
        /// "2 saved parts not loaded here: Morkhalla_ChestAncient, ..." (or "not made yet" when the owner hasn't made the room's parts).
        /// Null when all are here.</summary>
        private static string Unloaded(Site site, int room)
        {
            PlacedRoom r = site.Layout.Rooms[room];
            if (r.Def.GamePrefab != null || r.Def.Parts == null || ZNetScene.instance == null) return null;
            var wanted = r.Def.Parts.Where(pt => pt.Saved && Visible(pt.Prefab)).ToList();
            if (wanted.Count == 0) return null;
            if (!site.RoomDone(room)) return $"{wanted.Count} saved parts not made yet (the dungeon's owner makes them): {string.Join(", ", wanted.Select(pt => pt.Prefab).Distinct())}";
            // what of this dungeon is here, by prefab name and place
            var here = new List<KeyValuePair<string, Vector3>>();
            foreach (KeyValuePair<ZDO, ZNetView> kv in Instances(ZNetScene.instance))
                if (kv.Value != null && kv.Key.GetString(PartOf) == site.Id)
                    here.Add(new KeyValuePair<string, Vector3>(Utils.GetPrefabName(kv.Value.gameObject), kv.Value.transform.position));
            int q = MapLayout.Quarter(r.Yaw);
            var missing = new List<string>();
            foreach (PartDef pt in wanted)
            {
                Vector3 at = site.Origin + r.Pos + Layouts.Turn(pt.At, q);
                if (!here.Any(h => string.Equals(h.Key, pt.Prefab, StringComparison.OrdinalIgnoreCase) && (h.Value - at).sqrMagnitude < 2.5f * 2.5f)) missing.Add(pt.Prefab);
            }
            return missing.Count == 0 ? null : $"{missing.Count} saved part{(missing.Count == 1 ? "" : "s")} not loaded here: {string.Join(", ", missing.Distinct())}";
        }

        /// <summary>A saved part that shows in a picture (not a spawner, which is invisible).</summary>
        private static bool Visible(string prefab)
        {
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab ?? "") : null;
            if (go == null) return true;
            return go.GetComponent<CreatureSpawner>() == null && go.GetComponent<SpawnArea>() == null && go.GetComponent<Character>() == null;
        }

        // ---- which rooms, which views

        private static Site Find(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return null;
            return Dungeons.Sites.FirstOrDefault(s => s.Zdo != null && (s.Zdo.m_uid.ToString() == arg || (s.Id.Length > 0 && s.Id.StartsWith(arg, StringComparison.OrdinalIgnoreCase) && arg.Length >= 4)));
        }

        /// <summary>The rooms asked for: "all" (every room that isn't a wall), or a comma list of room indices and names.</summary>
        private static List<int> Rooms(Layout layout, MapLayout map, string arg, out string bad)
        {
            bad = null;
            var list = new List<int>();
            if (string.IsNullOrEmpty(arg) || arg == "all")
            {
                for (int i = 0; i < layout.Rooms.Count; i++) if (!map.IsWall(i)) list.Add(i);
                return list;
            }
            foreach (string part in arg.Split(','))
            {
                string t = part.Trim();
                if (t.Length == 0) continue;
                if (int.TryParse(t, out int i))
                {
                    if (i < 0 || i >= layout.Rooms.Count) { bad = $"no room {i} (it has 0 to {layout.Rooms.Count - 1}: dungeon map lists them)"; return null; }
                    if (!list.Contains(i)) list.Add(i);
                    continue;
                }
                var named = layout.Rooms.Where(r => string.Equals(r.Def.Name, t, StringComparison.OrdinalIgnoreCase)).Select(r => r.Index).ToList();
                if (named.Count == 0) { bad = $"no room named {t} in this layout"; return null; }
                foreach (int n in named) if (!list.Contains(n)) list.Add(n);
            }
            return list;
        }

        /// <summary>A room's views (world positions), as many as asked for and the room allows.</summary>
        private static List<View> Views(Site site, MapLayout map, int room, int count)
        {
            PlacedRoom r = site.Layout.Rooms[room];
            Bounds box = map.Boxes[room];
            Vector3 o = site.Origin;
            var views = new List<View>();
            // its doorways that lead somewhere, the one towards the entrance first
            var doors = Enumerable.Range(0, r.Def.Doorways.Count).Where(d => map.Way(room, d) && map.DoorFloor[room][d] == map.Base[room])
                .OrderBy(d => map.LinkRoom[room][d] >= 0 ? site.Layout.Rooms[map.LinkRoom[room][d]].Depth : -1).ToList();
            if (doors.Count == 0) doors = Enumerable.Range(0, r.Def.Doorways.Count).Where(d => map.Way(room, d)).ToList();
            if (doors.Count == 0 && r.Def.Doorways.Count > 0) doors.Add(0);
            float floorY = doors.Count > 0 ? r.DoorwayPos(doors[0]).y - 1f : box.min.y;
            Vector3 middle = new Vector3(box.center.x, floorY + 1.2f, box.center.z);

            Vector3 Inside(Vector3 p, float margin) => new Vector3(Mathf.Clamp(p.x, box.min.x + margin, box.max.x - margin), p.y, Mathf.Clamp(p.z, box.min.z + margin, box.max.z - margin));

            Vector3 first = middle;
            if (doors.Count > 0)
            {
                int d = doors[0];
                Vector3 into = r.DoorwayFacing(d); into.y = 0f; into = into.sqrMagnitude > 0.01f ? into.normalized : Vector3.forward;
                Vector3 at = r.DoorwayPos(d) + into * Inward;
                at.y = r.DoorwayPos(d).y - 1f + Eye;
                if (Focus(site.Layout, map, room, floorY, out Vector3 focus, out string what))
                {
                    // the room's centrepiece (a boss arena, a chest, a pedestal...): from the doorway's side, near enough to fill the picture
                    // with it, a little above, looking down at it
                    Vector3 way = focus - at; way.y = 0f;
                    float dist = way.magnitude;
                    if (dist > 0.5f)
                    {
                        float keep = Mathf.Clamp(dist * 0.55f, 5f, 10f);
                        at += way / dist * Mathf.Max(0f, dist - keep);
                    }
                    at = Inside(at, Inward);
                    at.y = floorY + Eye + 1.4f;
                    first = at;
                    views.Add(new View { At = o + at, Target = o + focus, What = $"from the doorway {r.Def.Doorways[d].Id}'s side, on the {what}" });
                }
                else
                {
                    first = at;
                    // looking at the far middle: past the room's centre, a little down
                    Vector3 far = Inside(middle + into * Mathf.Min(box.extents.x, box.extents.z) * 0.5f, 1f);
                    views.Add(new View { At = o + at, Target = o + far, What = "from the doorway " + r.Def.Doorways[d].Id });
                }
            }
            else views.Add(new View { At = o + Inside(box.center + Vector3.back * box.extents.z, Inward) + Vector3.up * (floorY + Eye - box.center.y), Target = o + middle, What = "from the south wall" });

            if (count >= 2)
            {
                // across: from the far side, looking back
                Vector3 flat = middle - new Vector3(first.x, middle.y, first.z);
                Vector3 back = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
                Vector3 at = Inside(middle + back * Mathf.Max(box.extents.x, box.extents.z), Inward);
                at.y = floorY + Eye + 0.6f;
                views.Add(new View { At = o + at, Target = o + new Vector3(first.x, floorY + 1.4f, first.z), What = "across, looking back" });
            }
            for (int k = 1; k < doors.Count && views.Count < count; k++)
            {
                int d = doors[k];
                Vector3 into = r.DoorwayFacing(d); into.y = 0f; into = into.sqrMagnitude > 0.01f ? into.normalized : Vector3.forward;
                Vector3 at = r.DoorwayPos(d) + into * Inward;
                at.y = r.DoorwayPos(d).y - 1f + Eye;
                views.Add(new View { At = o + at, Target = o + Inside(middle, 1f), What = "from the doorway " + r.Def.Doorways[d].Id });
            }
            if (views.Count < count && map.Top[room] > map.Base[room])
            {
                // a tall room: from high up in a corner, looking down across it
                Vector3 at = Inside(new Vector3(box.min.x, box.max.y - 3f, box.min.z), Inward + 1f);
                views.Add(new View { At = o + at, Target = o + middle, What = "from high up" });
            }
            while (views.Count < count)
            {
                // corners, round the room
                int k = views.Count;
                Vector3 corner = new Vector3((k & 1) == 0 ? box.min.x : box.max.x, floorY + Eye + 1f, (k & 2) == 0 ? box.min.z : box.max.z);
                views.Add(new View { At = o + Inside(corner, Inward), Target = o + middle, What = "from a corner" });
                if (k > 8) break;
            }
            return views.Take(count).ToList();
        }

        /// <summary>A room's centrepiece, if it has one (dungeon frame, a metre above its floor): a fight's arena (a boss's first), then a
        /// chest (a saved part named like one), a pedestal, the way out. What it is, for the view's name.</summary>
        private static bool Focus(Layout layout, MapLayout map, int room, float floorY, out Vector3 at, out string what)
        {
            at = default;
            what = null;
            PlacedRoom r = layout.Rooms[room];
            // (not levers, plates or braziers: on a wall or in a corner, framing one leaves the room out)
            int Rank(MechDef m) => m.Encounter?.Boss != null ? 0 : m.Kind == "encounter" ? 1 : m.Kind == "pedestal" ? 3 : m.Kind == "exit" ? 4 : 99;
            MechDef best = r.Def.Mechs.Where(m => Rank(m) < 99).OrderBy(Rank).FirstOrDefault();
            PartDef chest = r.Def.Parts?.FirstOrDefault(pt => pt.Saved && (pt.Prefab ?? "").IndexOf("chest", StringComparison.OrdinalIgnoreCase) >= 0);
            if (best != null && (Rank(best) <= 1 || chest == null))
            {
                at = map.MechAt(room, best);
                what = best.Encounter?.Boss != null ? "boss arena" : best.Kind == "encounter" ? "fight" : best.Kind;
            }
            else if (chest != null)
            {
                at = r.Pos + Layouts.Turn(chest.At, MapLayout.Quarter(r.Yaw));
                what = "chest";
            }
            else return false;
            at.y = floorY + 1f;
            return true;
        }

        // ---- one picture

        /// <summary>The game's camera at a view, drawn once into a picture with the dungeon lit as inside, then everything put back as it
        /// was. Null when it worked, else why not.</summary>
        private static string Shoot(Site site, Camera cam, View view, string file, bool bright)
        {
            Transform t = cam.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            float fov = cam.fieldOfView;
            RenderTexture target = cam.targetTexture;
            int width = Mathf.Clamp(Screen.width, 640, 1920), height = Mathf.RoundToInt(width * 9f / 16f);
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var lights = new List<LightWas>();
            var canvases = new List<Canvas>();
            Lighting was = default, was2 = default;
            bool lit = false, brightened = false;
            GameObject lamp = null;
            try
            {
                Vector3 look = view.Target - view.At;
                t.SetPositionAndRotation(view.At, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward, Vector3.up));
                cam.fieldOfView = Fov;
                lit = LightUp(site, out was);
                if (bright) { Brighten(out was2); brightened = true; lamp = Lamp(view.At); }
                Wake(view.At, lights);
                // interface drawn by this camera (none of the game's is: its HUD is drawn over the screen; another mod's might be)
                foreach (Canvas ui in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (ui != null && ui.enabled && ui.renderMode != RenderMode.ScreenSpaceOverlay && ui.worldCamera == cam) { ui.enabled = false; canvases.Add(ui); }
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                try
                {
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply(false);
                }
                finally { RenderTexture.active = active; }
                byte[] png = DungeonMap.EncodeToPng(tex);
                Object.Destroy(tex);
                if (png == null) return "the game's PNG encoder (ImageConversion) wasn't found";
                File.WriteAllBytes(file, png);
                return null;
            }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
            finally
            {
                // everything back as it was, before the next frame is drawn
                cam.targetTexture = target;
                cam.fieldOfView = fov;
                t.SetPositionAndRotation(pos, rot);
                foreach (LightWas l in lights) l.Restore();
                if (lamp != null) Object.DestroyImmediate(lamp);
                if (brightened) Unlight(was2);
                foreach (Canvas ui in canvases) if (ui != null) ui.enabled = true;
                if (lit) Unlight(was);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ---- the lights near the view: on, as the game's light LOD puts them on near a player

        private struct LightWas
        {
            public Light Light;
            public bool Enabled;
            public float Range, Strength;
            public LightShadows Shadows;

            public void Restore()
            {
                if (Light == null) return;
                Light.enabled = Enabled; Light.range = Range; Light.shadows = Shadows; Light.shadowStrength = Strength;
            }
        }

        private static void Wake(Vector3 at, List<LightWas> was)
        {
            if (!(LodLights?.GetValue(null) is IEnumerable all) || LodLight == null || LodRange == null) return;
            foreach (object o in all)
            {
                if (!(o is LightLod lod) || lod == null || !lod.isActiveAndEnabled) continue;
                if (!(LodLight.GetValue(lod) is Light light) || light == null) continue;
                float d = Vector3.Distance(at, lod.transform.position);
                bool on = !lod.m_lightLod || d < lod.m_lightDistance, shadow = lod.m_shadowLod && d < lod.m_shadowDistance;
                if (!on && !shadow) continue;
                was.Add(new LightWas { Light = light, Enabled = light.enabled, Range = light.range, Shadows = light.shadows, Strength = light.shadowStrength });
                if (on && lod.m_lightLod) { light.enabled = true; light.range = (float)LodRange.GetValue(lod); }
                if (shadow && LodShadow != null) { light.shadows = LightShadows.Soft; light.shadowStrength = (float)LodShadow.GetValue(lod); }
            }
        }

        // ---- the dungeon's own light: its pack's environment, as the game sets it for a player inside

        private struct Lighting
        {
            public Color Fog, Ambient, SunColor, SunFog, AmbientGlobal, DirColor;
            public float FogDensity, DirIntensity;
            public AmbientMode Mode;
            public LightRenderMode DirMode;
        }

        private static EnvSetup EnvOf(Site site)
        {
            string name = site.Built?.Environment != null ? site.Built.Environment.m_environment : site.Layout?.Pack?.Environment;
            if (string.IsNullOrEmpty(name) || EnvMan.instance == null) return null;
            return EnvMan.instance.m_environments.FirstOrDefault(e => e != null && e.m_name == name);
        }

        private static bool LightUp(Site site, out Lighting was)
        {
            was = default;
            EnvMan env = EnvMan.instance;
            EnvSetup e = EnvOf(site);
            Light sun = env != null ? env.m_dirLight : null;
            if (e == null || sun == null) return false;
            was = new Lighting
            {
                Fog = RenderSettings.fogColor, FogDensity = RenderSettings.fogDensity, Ambient = RenderSettings.ambientLight, Mode = RenderSettings.ambientMode,
                SunColor = Shader.GetGlobalColor(SunColor), SunFog = Shader.GetGlobalColor(SunFogColor), AmbientGlobal = Shader.GetGlobalColor(AmbientColor),
                DirColor = sun.color, DirIntensity = sun.intensity, DirMode = sun.renderMode,
            };
            // the time of day's weights, as EnvMan.FixedUpdate works them out
            float f = env.GetDayFraction();
            float night = Mathf.Pow(Mathf.Max(1f - Mathf.Clamp01(f / 0.25f), Mathf.Clamp01((f - 0.75f) / 0.25f)), 0.5f);
            float day = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(f - 0.5f) / 0.25f), 0.5f);
            float morning = Mathf.Min(Mathf.Clamp01(1f - (f - 0.26f) / -env.m_sunHorizonTransitionL), Mathf.Clamp01(1f - (f - 0.26f) / env.m_sunHorizonTransitionH));
            float evening = Mathf.Min(Mathf.Clamp01(1f - (f - 0.74f) / -env.m_sunHorizonTransitionH), Mathf.Clamp01(1f - (f - 0.74f) / env.m_sunHorizonTransitionL));
            float sum = night + day + morning + evening;
            if (sum > 0f) { night /= sum; day /= sum; morning /= sum; evening /= sum; }
            // what EnvMan.SetEnv sets, the light and the fog (not the sky, particles or sounds: none of them is in a room)
            sun.intensity = e.m_lightIntensityDay * day + e.m_lightIntensityNight * night;
            Color c = e.m_sunColorNight * night;
            if (day > 0f) c += e.m_sunColorDay * day + e.m_sunColorMorning * morning + e.m_sunColorEvening * evening;
            sun.color = c;
            sun.renderMode = LightRenderMode.ForceVertex;   // (as for a player inside: Character.InInterior)
            RenderSettings.fogColor = e.m_fogColorNight * night + e.m_fogColorDay * day + e.m_fogColorMorning * morning + e.m_fogColorEvening * evening;
            Color sunFog = e.m_fogColorSunNight * night;
            if (day > 0f) sunFog += e.m_fogColorSunDay * day + e.m_fogColorSunMorning * morning + e.m_fogColorSunEvening * evening;
            sunFog = Color.Lerp(RenderSettings.fogColor, sunFog, Mathf.Clamp01(Mathf.Max(night, day) * 3f));
            RenderSettings.fogDensity = e.m_fogDensityNight * night + e.m_fogDensityDay * day + e.m_fogDensityMorning * morning + e.m_fogDensityEvening * evening;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(e.m_ambColorNight, e.m_ambColorDay, day);
            Shader.SetGlobalColor(SunFogColor, sunFog);
            Shader.SetGlobalColor(SunColor, sun.color * sun.intensity);
            Shader.SetGlobalColor(AmbientColor, RenderSettings.ambientLight);
            return true;
        }

        /// <summary>"bright": the ambient light raised to at least a grey dusk, the fog thinned to a quarter, for one picture (put back with
        /// Unlight). Not how the game looks: to see the shapes in a dark room.</summary>
        private static void Brighten(out Lighting was)
        {
            Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
            was = new Lighting
            {
                Fog = RenderSettings.fogColor, FogDensity = RenderSettings.fogDensity, Ambient = RenderSettings.ambientLight, Mode = RenderSettings.ambientMode,
                SunColor = Shader.GetGlobalColor(SunColor), SunFog = Shader.GetGlobalColor(SunFogColor), AmbientGlobal = Shader.GetGlobalColor(AmbientColor),
                DirColor = sun != null ? sun.color : Color.black, DirIntensity = sun != null ? sun.intensity : 0f, DirMode = sun != null ? sun.renderMode : LightRenderMode.Auto,
            };
            Color floor = new Color(0.55f, 0.52f, 0.48f);
            Color amb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(Mathf.Max(amb.r, floor.r), Mathf.Max(amb.g, floor.g), Mathf.Max(amb.b, floor.b));
            RenderSettings.fogDensity = RenderSettings.fogDensity * 0.25f;
            Shader.SetGlobalColor(AmbientColor, RenderSettings.ambientLight);
        }

        /// <summary>"bright": a soft light at the camera (no shadows), for one picture.</summary>
        private static GameObject Lamp(Vector3 at)
        {
            var go = new GameObject("quaddungeon_tourlamp");
            go.transform.position = at;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 40f;
            l.intensity = 1.6f;
            l.color = new Color(1f, 0.95f, 0.88f);
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return go;
        }

        private static void Unlight(Lighting was)
        {
            Light sun = EnvMan.instance != null ? EnvMan.instance.m_dirLight : null;
            RenderSettings.fogColor = was.Fog; RenderSettings.fogDensity = was.FogDensity;
            RenderSettings.ambientMode = was.Mode; RenderSettings.ambientLight = was.Ambient;
            Shader.SetGlobalColor(SunColor, was.SunColor); Shader.SetGlobalColor(SunFogColor, was.SunFog); Shader.SetGlobalColor(AmbientColor, was.AmbientGlobal);
            if (sun != null) { sun.color = was.DirColor; sun.intensity = was.DirIntensity; sun.renderMode = was.DirMode; }
        }

        private static string Clean(string s) => new string((s ?? "room").Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 2), Math.Round(v.y, 2), Math.Round(v.z, 2));
    }
}
