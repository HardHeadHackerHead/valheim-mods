using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ziplines
{
    /// <summary>
    /// A "zip" command for the Claude Tools mod (its request mailbox), so an AI assistant can try the ziplines while testing: put two posts
    /// with a line between them in front of the player, ride it, and clear them again. Found and registered while the game runs.
    /// </summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;
        private static readonly List<GameObject> Spawned = new List<GameObject>();

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.quad.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "zip", "zip status | posts | link | press | hover | summit | dedupe [radius] | mountain | tp <x> <y> <z> | spawn [length=40] | ride | clear: put two linked posts in front of the player, ride the line, take them away again",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: zip command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the zip command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
        }

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

            if (sub == "status")
            {
                output(new JObject { ["posts"] = Spawned.Count(g => g != null), ["riding"] = Ride.Active, ["speed"] = Ride.Speed, ["attached"] = player.IsAttached(), ["position"] = new JArray(player.transform.position.x, player.transform.position.y, player.transform.position.z) });
                yield break;
            }
            if (sub == "posts")
            {
                var list = new JArray();
                foreach (Post p in Post.Loaded().OrderBy(p => Vector3.Distance(p.transform.position, player.transform.position)))
                    list.Add(new JObject { ["at"] = new JArray(Math.Round(p.transform.position.x, 1), Math.Round(p.transform.position.y, 1), Math.Round(p.transform.position.z, 1)),
                                           ["away"] = Math.Round(Vector3.Distance(p.transform.position, player.transform.position)), ["id"] = p.Id, ["to"] = p.ToId, ["partnerLoaded"] = p.Partner != null });
                output(new JObject { ["posts"] = list, ["maxLength"] = Plugin.MaxLength.Value });
                yield break;
            }
            if (sub == "link")
            {
                // the two nearest posts without a line, whatever the distance between them
                var free = Post.Loaded().Where(p => p.ToId == 0L).OrderBy(p => Vector3.Distance(p.transform.position, player.transform.position)).Take(2).ToList();
                if (free.Count < 2) { error("fewer than two unlinked posts are loaded near you"); yield break; }
                Post.Join(player, free[0], free[1]);
                yield return new WaitForSeconds(0.5f);
                output(new JObject { ["linked"] = free[0].ToId != 0L, ["length"] = Vector3.Distance(Line.Anchor(free[0]), Line.Anchor(free[1])) });
                yield break;
            }
            if (sub == "press")
            {
                // E on what the player is looking at, as the game does it
                GameObject hover = player.GetHoverObject();
                if (hover == null) { error("the player is not looking at anything"); yield break; }
                var interact = HarmonyLib.AccessTools.Method(typeof(Player), "Interact");
                var result = new JObject { ["hover"] = hover.name, ["hoverText"] = hover.GetComponentInParent<Hoverable>()?.GetHoverText() ?? "(no hover text)" };
                if (interact == null) { error("Player.Interact not found"); yield break; }
                interact.Invoke(player, new object[] { hover, false, false });
                yield return new WaitForSeconds(0.6f);
                result["riding"] = Ride.Active; result["attached"] = player.IsAttached();
                output(result);
                yield break;
            }
            if (sub == "hover")
            {
                Post near = Post.Loaded().OrderBy(p => Vector3.Distance(p.transform.position, player.transform.position)).FirstOrDefault();
                if (near == null) { error("no post loaded"); yield break; }
                output(new JObject { ["name"] = near.GetHoverName(), ["text"] = near.GetHoverText(), ["hasLine"] = near.HasLine, ["away"] = Vector3.Distance(near.transform.position, player.transform.position),
                                     ["layer"] = LayerMask.LayerToName(near.gameObject.layer), ["colliders"] = new JArray(near.GetComponentsInChildren<Collider>().Select(c => c.name + " (" + LayerMask.LayerToName(c.gameObject.layer) + ", " + (c.enabled ? "on" : "off") + ")")) });
                yield break;
            }
            if (sub == "tp")
            {
                if (args.Length < 4) { error("zip tp <x> <y> <z>"); yield break; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Vector3 to = new Vector3(float.Parse(args[1], ci), float.Parse(args[2], ci), float.Parse(args[3], ci));
                player.TeleportTo(to, player.transform.rotation, true);
                output(new JObject { ["teleporting"] = new JArray(to.x, to.y, to.z) });
                yield break;
            }
            if (sub == "mountain")
            {
                // The lower of the two posts goes; a post is put on the highest ground north of the player with a line to the other one.
                Plugin.Log.LogInfo("mountain: start");
                while (Ride.Active) yield return null;
                var posts = Post.Loaded().ToList();
                if (posts.Count == 0) { error("no post loaded"); yield break; }
                Post upper = posts.OrderByDescending(p => p.transform.position.y).First(), lower = posts.OrderBy(p => p.transform.position.y).First();
                Vector3 upperAnchor = Line.Anchor(upper);
                if (lower != upper) ZNetScene.instance.Destroy(lower.gameObject);   // (the lower of two goes; with one left, it is the one to run the line to)
                upper.ClearLine();
                yield return new WaitForSeconds(0.4f);

                // the high ground north (north is +z), highest first, each at least 150 m from the last
                Plugin.Log.LogInfo($"mountain: upper post {upper.transform.position}, scanning from {player.transform.position}");
                Vector3 from = player.transform.position;
                var spots = new List<Vector3>();
                for (float z = from.z + 80f; z <= from.z + 1800f; z += 30f)
                    for (float x = from.x - 900f; x <= from.x + 900f; x += 30f)
                        spots.Add(new Vector3(x, WorldGenerator.instance.GetHeight(x, z), z));
                var peaks = new List<Vector3>();
                foreach (Vector3 s in spots.OrderByDescending(s => s.y)) { if (peaks.All(p => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z)) > 150f)) peaks.Add(s); if (peaks.Count >= 12) break; }

                Plugin.Log.LogInfo($"mountain: {spots.Count} samples, {peaks.Count} peaks, highest {(peaks.Count > 0 ? peaks[0].ToString() : "-")}");
                Vector3 chosen = Vector3.zero; string chosenWhy = null; var tried = new JArray();
                foreach (Vector3 peak in peaks)
                {
                    Vector3 anchor = peak + Vector3.up * Things.Height;
                    float length = Vector3.Distance(anchor, upperAnchor);
                    string why = length > Plugin.MaxLength.Value ? "too long" : anchor.y - upperAnchor.y < Mathf.Max(2f, length * Plugin.MinSlope.Value) ? "not high enough" : Line.Clearance(anchor, upperAnchor, null, upper);
                    Plugin.Log.LogInfo($"mountain: peak {peak} -> {why ?? "clear"}");
                    tried.Add(new JObject { ["peak"] = new JArray(Math.Round(peak.x), Math.Round(peak.y), Math.Round(peak.z)), ["length"] = Math.Round(length), ["result"] = why ?? "clear" });
                    if (why == null) { chosen = peak; break; }
                }
                if (chosen == Vector3.zero) { output(new JObject { ["placed"] = false, ["tried"] = tried }); yield break; }

                Plugin.Log.LogInfo($"mountain: placing at {chosen}");
                // far from the player the game takes the object away again at once, so its saved data is finished in the same moment: that stays,
                // and the post stands there whenever the land around it is loaded
                GameObject go = UnityEngine.Object.Instantiate(Things.PostObject, chosen, Quaternion.identity);
                Spawned.Add(go);
                ZDO z0 = go.GetComponent<ZNetView>().GetZDO();
                long id = Post.EnsureId(z0);
                bool linked = Post.Join(player, z0, id, chosen + Vector3.up * Things.Height, upper);
                output(new JObject { ["placed"] = true, ["post"] = new JArray(chosen.x, chosen.y, chosen.z), ["linked"] = linked, ["length"] = Vector3.Distance(chosen + Vector3.up * Things.Height, upperAnchor),
                                     ["drop"] = chosen.y + Things.Height - upperAnchor.y, ["tried"] = tried });
                yield break;
            }
            if (sub == "dedupe")
            {
                // posts with no line near the player (left over from tests): taken away, the ones with a line kept
                float radius = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) ? r : 40f;
                var drop = Post.Loaded().Where(p => p.ToId == 0L && Vector3.Distance(p.transform.position, player.transform.position) <= radius).ToList();
                foreach (Post p in drop) ZNetScene.instance.Destroy(p.gameObject);
                output(new JObject { ["removed"] = drop.Count, ["kept"] = Post.Loaded().Count(p => p != null && !drop.Contains(p)) });
                yield break;
            }
            if (sub == "summit")
            {
                // The line's top post moves to the highest ground right here (the best spot within 45 m whose rope clears the land), still
                // tied to the same bottom post. Any other posts near the player are taken away.
                Vector3 me = player.transform.position;
                var near = Post.Loaded().Where(p => Vector3.Distance(p.transform.position, me) <= 90f).ToList();
                Post old = near.FirstOrDefault(p => p.HasLine);
                if (old == null) { error("no post with a line near the player"); yield break; }
                long bottomId = old.ToId; Vector3 bottomAnchor = old.PartnerAnchor;
                ZDO bottomZdo = Post.FarPosts(bottomId).FirstOrDefault();
                if (bottomZdo == null) { error("the bottom post is not known to the game"); yield break; }

                var spots = new List<Vector3>();
                for (float dz = -45f; dz <= 45f; dz += 3f)
                    for (float dx = -45f; dx <= 45f; dx += 3f)
                    {
                        float x = me.x + dx, z = me.z + dz;
                        if (dx * dx + dz * dz <= 45f * 45f) spots.Add(new Vector3(x, WorldGenerator.instance.GetHeight(x, z), z));
                    }
                var tried = new JArray();
                Vector3 chosen = Vector3.zero;
                foreach (Vector3 spot in spots.OrderByDescending(s => s.y).Take(60))
                {
                    Vector3 anchor = spot + Vector3.up * Things.Height;
                    string why = Line.Clearance(anchor, bottomAnchor, null, null);
                    if (tried.Count < 6) tried.Add(new JObject { ["spot"] = new JArray(Math.Round(spot.x, 1), Math.Round(spot.y, 1), Math.Round(spot.z, 1)), ["result"] = why ?? "clear" });
                    if (why == null) { chosen = spot; break; }
                }
                if (chosen == Vector3.zero) { output(new JObject { ["placed"] = false, ["tried"] = tried, ["note"] = "no spot near here has a clear line" }); yield break; }

                foreach (Post p in near) ZNetScene.instance.Destroy(p.gameObject);
                yield return new WaitForSeconds(0.3f);
                GameObject go = UnityEngine.Object.Instantiate(Things.PostObject, chosen, Quaternion.identity);
                Spawned.Add(go);
                ZDO z0 = go.GetComponent<ZNetView>().GetZDO();
                long id = Post.EnsureId(z0);
                Post fresh = go.GetComponent<Post>();
                bool linked = Post.Join(player, bottomZdo, bottomId, bottomAnchor, fresh);
                output(new JObject { ["placed"] = true, ["post"] = new JArray(chosen.x, chosen.y, chosen.z), ["linked"] = linked, ["length"] = Vector3.Distance(chosen + Vector3.up * Things.Height, bottomAnchor),
                                     ["drop"] = chosen.y + Things.Height - bottomAnchor.y, ["removed"] = near.Count, ["tried"] = tried });
                yield break;
            }
            if (sub == "bones")
            {
                // the player's skeleton, as "Hips/Spine/..." paths with each bone's height above the feet (for posing)
                var lines = new JArray();
                void Walk(Transform t, string path, int depth)
                {
                    if (depth > 9) return;
                    lines.Add(path + t.name + $"  ({t.position.y - player.transform.position.y:0.00})");
                    foreach (Transform child in t) if (child.GetComponent<Renderer>() == null || child.name.Length < 24) Walk(child, path + t.name + "/", depth + 1);
                }
                Transform root = player.GetComponentInChildren<Animator>()?.transform ?? player.transform;
                if (args.Length > 1)
                {
                    Transform found = null;
                    foreach (Transform t in player.transform.Find("Visual/Armature").GetComponentsInChildren<Transform>()) if (t.name == args[1]) { found = t; break; }
                    if (found != null) root = found;
                }
                Walk(root, "", 0);
                output(new JObject { ["root"] = root.name, ["bones"] = lines });
                yield break;
            }
            if (sub == "bonecheck")
            {
                var result = new JObject();
                Transform armature = player.transform.Find("Visual/Armature");
                foreach (string n in new[] { "Hips", "Spine2", "LeftArm", "LeftForeArm", "LeftHand", "RightHand", "LeftHandIndex1", "LeftHandIndex3", "LeftUpLeg", "LeftFoot", "Head" })
                {
                    Transform t = armature != null ? armature.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name == n) : null;
                    result[n] = t == null ? "missing" : $"{t.position.x:0.00},{t.position.y:0.00},{t.position.z:0.00} scale {t.lossyScale.x:0.00}";
                }
                Transform vis = player.transform.Find("Visual");
                result["visualScale"] = vis != null ? vis.lossyScale.ToString() : "none";
                result["player"] = player.transform.position.ToString();
                result["riding"] = Ride.Active;
                output(result);
                yield break;
            }
            if (sub == "clear")
            {
                foreach (GameObject g in Spawned.ToList()) if (g != null) ZNetScene.instance.Destroy(g);
                int n = Spawned.Count; Spawned.Clear();
                output(new JObject { ["removed"] = n });
                yield break;
            }
            if (sub == "spawn")
            {
                if (Things.PostObject == null) { error("the post is not registered"); yield break; }
                float length = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float l) ? l : 40f;
                Vector3 forward = player.transform.forward; forward.y = 0f; forward.Normalize();
                Vector3 first = Ground(player.transform.position + forward * 4f), second = Ground(player.transform.position + forward * (4f + length));
                foreach (Vector3 at in new[] { first, second })
                {
                    GameObject go = UnityEngine.Object.Instantiate(Things.PostObject, at, Quaternion.identity);
                    Spawned.Add(go);
                }
                yield return new WaitForSeconds(1.2f);   // (each post gives itself its id)
                Post a = Spawned[Spawned.Count - 2].GetComponent<Post>(), b = Spawned[Spawned.Count - 1].GetComponent<Post>();
                Post.Join(player, a, b);
                yield return new WaitForSeconds(0.5f);
                output(new JObject { ["first"] = new JArray(first.x, first.y, first.z), ["second"] = new JArray(second.x, second.y, second.z), ["linked"] = a.Partner == b, ["length"] = Vector3.Distance(Line.Anchor(a), Line.Anchor(b)),
                                     ["drop"] = Line.Anchor(a).y - Line.Anchor(b).y });
                yield break;
            }
            if (sub == "ride")
            {
                Post a = Spawned.Where(g => g != null).Select(g => g.GetComponent<Post>()).FirstOrDefault(p => p != null && p.HasLine) ?? Post.Loaded().Where(p => p.HasLine && Line.Anchor(p).y > p.PartnerAnchor.y + 0.5f).OrderBy(p => Vector3.Distance(p.transform.position, player.transform.position)).FirstOrDefault();
                if (a == null) { error("no linked posts: zip spawn first"); yield break; }
                Ride.Start(player, a, a.PartnerAnchor, a.Partner);
                yield return new WaitForSeconds(0.3f);
                output(new JObject { ["riding"] = Ride.Active, ["attached"] = player.IsAttached() });
                yield break;
            }
            error("unknown subcommand " + sub);
        }

        private static Vector3 Ground(Vector3 at)
        {
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
            return at;
        }
    }
}
