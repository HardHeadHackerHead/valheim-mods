using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Terminal
{
    /// <summary>
    /// A "terminal" command for the Claude Tools mod (its request mailbox), so the mod can be tried without keys: put a terminal in front of
    /// the player, sit down, feed the Proxy stand-in keys, stand up. Found and registered while the game runs, like Ziplines' "zip".
    /// </summary>
    internal static class Tools
    {
        private const string Help = "terminal status | hurt <n> | measure <text> | dump [n] | browse <address or words> | page <n> | warp <x> <z> | front <prefab> | bag | give <item> [n] | use <1-8> [drop] | reloadbag | interact | eyeshot | place | sit | stand | launch <app> | back | click <x> <y> | bring | input <forward> <right> <seconds> [run] [jump] [attack] [yaw=<deg>] [pitch=<deg up>] | kill | clear: put a terminal in front of the player, sit at it, press stand-in keys for the Proxy";
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

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
                register?.Invoke(null, new object[] { Plugin.Name, "terminal", Help, (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: terminal command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the terminal command to Claude Tools: " + e.Message); }
        }

        public static void Unregister()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); }
            catch (Exception) { }
        }

        private static JArray At(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));

        private static IEnumerator Command(string[] args, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray();
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            if (Array.IndexOf(new[] { "status", "stand" }, sub) < 0 && !DHack.Shared.TestCommands.CheatsAllowed())
            { error(DHack.Shared.TestCommands.Refusal("terminal " + sub)); yield break; }

            if (sub == "status")
            {
                var terminals = new JArray();
                foreach (TerminalPiece t in TerminalPiece.All)
                {
                    ProxyBody p = t.FindProxy();
                    terminals.Add(new JObject { ["at"] = At(t.transform.position), ["id"] = t.Id, ["state"] = t.State.ToString(), ["app"] = t.RunningApp, ["progress"] = Math.Round(t.Progress, 2), ["crashed"] = t.CrashedApp, ["screen"] = t.ScreenInfo(), ["recoveringSeconds"] = t.Recovering(out int left) ? left : 0, ["away"] = Math.Round(Vector3.Distance(t.transform.position, player.transform.position), 1),
                        ["proxy"] = p == null ? null : new JObject { ["at"] = At(p.transform.position), ["driver"] = p.Driver, ["driven"] = p.Driven, ["owner"] = p.IsOwner,
                            ["health"] = Math.Round(p.Body.GetHealth(), 1), ["stamina"] = p.StaminaPercent, ["dead"] = p.Body.IsDead(), ["gear"] = p.Body.GetInventory().GetAllItems().Count } });
                }
                output(new JObject { ["eyes"] = new JObject { ["renders"] = View.Renders, ["at"] = At(View.LastAt), ["dir"] = At(View.LastDir), ["distance"] = Math.Round(View.LastDistance, 2) }, ["terminals"] = terminals, ["driving"] = Drive.Active, ["inBuildMenu"] = Things.InMenu(), ["cost"] = Plugin.Cost.Value, ["proxies"] = ProxyBody.All.Count, ["attached"] = player.IsAttached() });
                yield break;
            }
            if (sub == "place")
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(Things.Prefab);
                if (prefab == null) { error("the terminal prefab isn't registered"); yield break; }
                Vector3 at = player.transform.position + player.transform.forward * 3f;
                if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
                GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(player.transform.forward));   // (its stool towards the player: the monitor looks the way the player does)
                output(new JObject { ["placed"] = At(go.transform.position), ["yaw"] = Math.Round(go.transform.eulerAngles.y, 1) });
                yield break;
            }
            if (sub == "sit")
            {
                TerminalPiece t = TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
                if (t == null) { error("no terminal loaded"); yield break; }
                output(new JObject { ["sat"] = Drive.Begin(player, t) });
                yield break;
            }
            if (sub == "eyeshot")
            {
                TerminalPiece t = TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
                string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", "terminal-screen.png");
                output(new JObject { ["saved"] = t?.Snapshot(path) });
                yield break;
            }
            if (sub == "colliders")
            {
                float F(int i) => args.Length > i ? float.Parse(args[i], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                Collider[] found = Physics.OverlapSphere(new Vector3(F(1), F(2), F(3)), Mathf.Max(0.3f, F(4)), ~0, QueryTriggerInteraction.Collide);
                output(new JObject { ["colliders"] = new JArray(found.Take(25).Select(c => (object)(c.name + "/" + LayerMask.LayerToName(c.gameObject.layer) + (c.isTrigger ? "/trigger" : "") + " <- " + c.transform.root.name + " at " + c.bounds.center.ToString("0.0") + " size " + c.bounds.size.ToString("0.0"))).ToArray()) });
                yield break;
            }
            if (sub == "warp" || sub == "front")
            {
                ProxyBody pb = TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault()?.FindProxy();
                if (pb == null || !pb.IsOwner) { error("no Proxy of this game near the nearest terminal"); yield break; }
                if (sub == "warp")
                {
                    float wx = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 0f, wz = args.Length > 2 ? float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                    var to = new Vector3(wx, 0f, wz);
                    if (!ZoneSystem.instance.GetSolidHeight(to, out float wy)) { error("no ground there"); yield break; }
                    to.y = wy + 0.3f;
                    pb.PlaceAt(to);
                    output(new JObject { ["warped"] = At(to) });
                    yield break;
                }
                GameObject thing = ZNetScene.instance.GetPrefab(args.Length > 1 ? args[1] : "Wood");
                if (thing == null) { error("no such prefab"); yield break; }
                pb.Aim(out float ay, out float ap);
                Terminal.View.Pose(pb, out Vector3 camAt, out Vector3 camDir, out float camAway);
                Vector3 at = camAt + camDir * (camAway + 2.0f);   // (on the crosshair's ray, 2 m past the Proxy)
                if (ZoneSystem.instance.GetSolidHeight(at, out float fy)) at.y = fy + 0.1f;
                UnityEngine.Object.Instantiate(thing, at, Quaternion.Euler(0f, ay + 180f, 0f));
                output(new JObject { ["placed"] = At(at) });
                yield break;
            }
            if (sub == "bag" || sub == "give" || sub == "use" || sub == "reloadbag" || sub == "interact")
            {
                ProxyBody proxy = TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault()?.FindProxy();
                if (proxy == null) { error("no Proxy loaded near the nearest terminal"); yield break; }
                if (sub == "give") { output(new JObject { ["gave"] = proxy.Give(args.Length > 1 ? args[1] : "Wood", args.Length > 2 && int.TryParse(args[2], out int n) ? n : 1) }); yield break; }
                if (sub == "use") { proxy.UseSlot(args.Length > 1 && int.TryParse(args[1], out int k) ? k : 1, args.Contains("drop")); output(new JObject { ["used"] = true }); yield break; }
                if (sub == "reloadbag") { proxy.ForceReload(); output(new JObject { ["reloading"] = true }); yield break; }
                if (sub == "interact") { string hover; Interactable it = proxy.FindInteractable(out hover); output(new JObject { ["found"] = it != null ? it.GetType().Name : null, ["hover"] = hover, ["ray"] = proxy.DescribeRay(), ["used"] = proxy.TryInteract() }); yield break; }
                var items = new JArray();
                foreach (ItemDrop.ItemData it in proxy.Body.GetInventory().GetAllItemsInGridOrder())
                    items.Add(new JObject { ["name"] = it.m_shared.m_name, ["stack"] = it.m_stack, ["equipped"] = it.m_equipped, ["start"] = it.m_customData.ContainsKey("dh_tm_start") });
                output(new JObject { ["items"] = items, ["savedChars"] = proxy.BagBytes, ["hp"] = Math.Round(proxy.Body.GetHealth(), 1) });
                yield break;
            }
            if (sub == "hurt")
            {
                ProxyBody victim = TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault()?.FindProxy();
                if (victim == null || !victim.IsOwner) { error("no Proxy of this game near the nearest terminal"); yield break; }
                float amount = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hv) ? hv : 20f;
                victim.Body.ApplyDamage(new HitData { m_damage = { m_damage = amount } }, false, true);
                output(new JObject { ["hp"] = Math.Round(victim.Body.GetHealth(), 1), ["stamina"] = victim.StaminaPercent });
                yield break;
            }
            if (sub == "measure")
            {
                string sample = string.Join(" ", args.Skip(1).ToArray());
                var holder = new GameObject("TerminalMeasure", typeof(RectTransform));
                var label = holder.AddComponent<TMPro.TextMeshProUGUI>();
                label.font = Desktop.Font; label.fontSize = 100f; label.enableWordWrapping = false; label.richText = false;
                Vector2 pref = label.GetPreferredValues(sample);
                UnityEngine.Object.Destroy(holder);
                output(new JObject { ["text"] = sample, ["tmpWidthAt100"] = Math.Round(pref.x, 1), ["myWidthAt100"] = Math.Round(Fonts.Width(sample, 100f, false), 1) });
                yield break;
            }
            if (sub == "dump")
            {
                TerminalPiece dt = Drive.Current ?? TerminalPiece.All.OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
                output(new JObject { ["page"] = dt == null ? "no terminal" : BrowserApp.Describe(dt, args.Length > 1 && int.TryParse(args[1], out int cnt) ? cnt : 12) });
                yield break;
            }
            if (sub == "browse" || sub == "page")
            {
                if (!Drive.Active) { error("not sitting at a terminal: terminal sit first"); yield break; }
                if (sub == "page") { Drive.Current.SetBrowser(Drive.Current.BrowserUrl, args.Length > 1 && int.TryParse(args[1], out int pg) ? pg : 0); output(new JObject { ["page"] = Drive.Current.BrowserPage }); yield break; }
                string address = Net.Normalize(string.Join(" ", args.Skip(1).ToArray()));
                if (address == null) { error("terminal browse <address or words>"); yield break; }
                Drive.Current.SetBrowser(address, 0);
                output(new JObject { ["address"] = address });
                yield break;
            }
            if (sub == "launch")
            {
                if (!Drive.Active) { error("not sitting at a terminal: terminal sit first"); yield break; }
                App app = Apps.Get(args.Length > 1 ? args[1].ToLowerInvariant() : ValheimApp.AppId);
                if (app == null) { error("no such app: " + string.Join(", ", Apps.All.Select(a => a.Id).ToArray())); yield break; }
                Drive.Current.Launch(app);
                output(new JObject { ["state"] = Drive.Current.State.ToString() });
                yield break;
            }
            if (sub == "back") { Drive.Escape(); output(new JObject { ["state"] = Drive.Current?.State.ToString() }); yield break; }
            if (sub == "bring") { if (Drive.Active) Drive.Current.Bring(Drive.Current.RunningApp); output(new JObject { ["state"] = Drive.Current?.State.ToString() }); yield break; }
            if (sub == "click")
            {
                if (!Drive.Active) { error("not sitting at a terminal: terminal sit first"); yield break; }
                float X(int i) => args.Length > i && float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0.5f;
                Desktop.Click(Drive.Current, new Vector2(X(1), X(2)));
                output(new JObject { ["clicked"] = true });
                yield break;
            }
            if (sub == "stand") { Drive.End("command"); output(new JObject { ["stood"] = true }); yield break; }
            if (sub == "input")
            {
                float Num(int i, float d) => args.Length > i && float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d;
                if (!Drive.Active) { error("not sitting at a terminal: terminal sit first"); yield break; }
                Drive.TestMove = new Vector3(Num(2, 0f), 0f, Num(1, 0f));
                Drive.TestUntil = Time.time + Num(3, 1f);
                Drive.TestRun = args.Contains("run");
                Drive.TestJump = args.Contains("jump");
                Drive.TestAttack = args.Contains("attack");
                string yaw = args.FirstOrDefault(a => a.StartsWith("yaw="));
                string pitch = args.FirstOrDefault(a => a.StartsWith("pitch="));
                Drive.TestPitch = pitch != null && float.TryParse(pitch.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pv) ? pv : 0f;
                Drive.TestYaw = yaw != null && float.TryParse(yaw.Substring(4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ? y : float.NaN;
                output(new JObject { ["input"] = "ok", ["seconds"] = Drive.TestUntil - Time.time });
                yield break;
            }
            if (sub == "kill")
            {
                foreach (ProxyBody p in ProxyBody.All.ToList()) if (p.IsOwner) p.Body.ApplyDamage(new HitData { m_damage = { m_damage = 99999f } }, false, true);
                output(new JObject { ["killed"] = ProxyBody.All.Count });
                yield break;
            }
            if (sub == "clear")
            {
                Drive.End("clear");
                int n = 0;
                foreach (ProxyBody p in ProxyBody.All.ToList()) { if (p.View != null && p.View.IsValid()) { p.View.ClaimOwnership(); ZNetScene.instance.Destroy(p.gameObject); n++; } }
                foreach (TerminalPiece t in TerminalPiece.All.ToList()) { t.Nview.ClaimOwnership(); ZNetScene.instance.Destroy(t.gameObject); n++; }
                output(new JObject { ["removed"] = n });
                yield break;
            }
            error(Help);
        }
    }
}
