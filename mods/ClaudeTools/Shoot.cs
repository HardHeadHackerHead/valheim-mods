using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeTools
{
    /// <summary>
    /// "shoot": setting up a screenshot of a mod (for its page, or to show a player something): find pieces, stand the player somewhere
    /// looking somewhere, open the inventory, the map, the build menu or a mod's window, close them all again, hide the HUD. Then `shot`.
    /// Moved here from Arena's own tools, so every mod can use it.
    ///
    /// Most of it only changes what is on the screen. Two parts act as the player would never be able to, so they follow the game's own
    /// cheat rule (single player, the host, or devcommands on), like give and grow: `tp` (moves the player) and `use` (presses E on something
    /// up to 12 m away).
    /// </summary>
    public partial class Plugin
    {
        private const string ShootUsage =
            "shoot find <name> [radius=120] | near <name> [distance] | pose <name> [zoom] [pitch] | tp x y z yaw [pitch] | look yaw [pitch] | inv | close | map on|off | build | unbuild | hud on|off | " +
            "use <name> | open qolrules|recycler: set up a screenshot (near, tp and use only where cheats are allowed; near stands the player beside something on real floor, tp refuses thin air)";

        /// <summary>What a player can stand on (and bump into): the ground, pieces, rocks, ships.</summary>
        private static readonly int FloorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

        private void RegisterShootCommands() => Builtin("shoot", ShootUsage, (a, output, error) => Shoot(a, output, error));

        private static IEnumerator Shoot(string[] a, Action<JObject> output, Action<string> error)
        {
            Player player = Player.m_localPlayer;
            string what = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            switch (what)
            {
                case "find":
                {
                    // pieces nearby by name: where each stands and which way it faces (for placing a camera or the player)
                    string filter = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    float radius = F(a, 3, 120f);
                    var list = new JArray();
                    foreach (Piece pc in UnityEngine.Object.FindObjectsOfType<Piece>())
                    {
                        if (pc == null || !pc.name.ToLowerInvariant().Contains(filter)) continue;
                        float dist = Vector3.Distance(pc.transform.position, player.transform.position);
                        if (dist > radius) continue;
                        Vector3 pp = pc.transform.position;
                        list.Add($"{pc.name.Replace("(Clone)", "")} at {pp.x:0.0} {pp.y:0.0} {pp.z:0.0} yaw {pc.transform.eulerAngles.y:0} ({dist:0} m)");
                        if (list.Count >= 40) break;
                    }
                    output(new JObject { ["found"] = list });
                    yield break;
                }
                case "tp":
                {
                    // shoot tp x y z yaw [pitch]: far away through the game's own teleport (it loads the area), a short step at once
                    if (!CheatsAllowed()) { error(CheatRefusal("shoot tp")); yield break; }
                    var to = new Vector3(F(a, 2, 0f), F(a, 3, 0f), F(a, 4, 0f));
                    Quaternion face = Quaternion.Euler(0f, F(a, 5, 0f), 0f);
                    // never into thin air: a guessed spot beside an upstairs piece dropped the player a floor (with fall damage)
                    if ((to - player.transform.position).magnitude <= 60f && !Physics.Raycast(to + Vector3.up * 0.5f, Vector3.down, 2.5f, FloorMask))
                    { error($"no floor under {to.x:0.0} {to.y:0.0} {to.z:0.0}: the player would fall. To stand beside something, use shoot near <name>"); yield break; }
                    if ((to - player.transform.position).magnitude > 60f) { player.TeleportTo(to, face, true); yield return new WaitForSeconds(8f); }
                    else
                    {
                        player.transform.SetPositionAndRotation(to, face);
                        Rigidbody body = player.GetComponent<Rigidbody>();
                        if (body != null) { body.position = to; body.linearVelocity = Vector3.zero; }
                        yield return new WaitForSeconds(0.4f);
                    }
                    if (Player.m_localPlayer == null) { error("the player is gone (teleport?)"); yield break; }
                    Player.m_localPlayer.SetLookDir(Quaternion.Euler(F(a, 6, 0f), F(a, 5, 0f), 0f) * Vector3.forward);
                    output(new JObject { ["at"] = Vec(Player.m_localPlayer.transform.position) });
                    yield break;
                }
                case "near":
                {
                    // shoot near <name> [distance=1.6]: stand beside the nearest thing of that name, facing it, on real floor at its height with
                    // room for the player (in front of it first, then round it); refuses rather than guess
                    if (!CheatsAllowed()) { error(CheatRefusal("shoot near")); yield break; }
                    string name = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    float gap = Mathf.Clamp(F(a, 3, 1.6f), 0.8f, 6f);
                    Transform target = null;
                    float bd = 60f;
                    foreach (ZNetView v in UnityEngine.Object.FindObjectsOfType<ZNetView>())
                    {
                        if (v == null || !v.name.ToLowerInvariant().Contains(name)) continue;
                        float d = Vector3.Distance(v.transform.position, player.transform.position);
                        if (d < bd) { bd = d; target = v.transform; }
                    }
                    if (target == null) { error($"no \"{name}\" within 60 m"); yield break; }
                    Vector3 at = target.position, ahead = target.forward;
                    Bounds body = new Bounds(target.position, Vector3.one * 0.5f);
                    foreach (Collider col in target.GetComponentsInChildren<Collider>()) if (col.enabled && !col.isTrigger) body.Encapsulate(col.bounds);
                    Vector3? spot = null;
                    foreach (float turn in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 180f, 150f, -150f, 120f, -120f })
                    {
                        Vector3 dir = Quaternion.Euler(0f, turn, 0f) * new Vector3(ahead.x, 0f, ahead.z).normalized;
                        Vector3 probe = at + dir * gap;
                        if (!Physics.Raycast(probe + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.6f, FloorMask)) continue;
                        if (hit.point.y < at.y - 0.6f || hit.point.y > at.y + 0.9f) continue;   // (the floor it stands on, not the one below)
                        if (Physics.CheckCapsule(hit.point + Vector3.up * 0.45f, hit.point + Vector3.up * 1.7f, 0.33f, FloorMask)) continue;   // (room to stand)
                        // and it can be seen from there: the first thing between the eyes and its middle is itself (not a fence)
                        Vector3 eyes = hit.point + Vector3.up * 1.6f;
                        if (Physics.Raycast(eyes, (body.center - eyes).normalized, out RaycastHit first, Vector3.Distance(eyes, body.center) + 1f, FloorMask)
                            && first.collider.GetComponentInParent<ZNetView>()?.transform != target) continue;
                        spot = hit.point + Vector3.up * 0.05f;
                        break;
                    }
                    if (spot == null) { error($"no floor with room to stand beside {target.name} (walk there yourself, or try another distance)"); yield break; }
                    Vector3 toward = at - spot.Value;
                    float yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                    player.transform.SetPositionAndRotation(spot.Value, Quaternion.Euler(0f, yaw, 0f));
                    Rigidbody rb = player.GetComponent<Rigidbody>();
                    if (rb != null) { rb.position = spot.Value; rb.linearVelocity = Vector3.zero; }
                    yield return new WaitForSeconds(0.4f);
                    if (Player.m_localPlayer == null) yield break;
                    // look at its middle (a tall portal, a low chest), so E (screen use) is on it
                    Vector3 eye = Player.m_localPlayer.transform.position + Vector3.up * 1.6f;
                    Player.m_localPlayer.SetLookDir((body.center - eye).normalized);
                    output(new JObject { ["beside"] = target.name.Replace("(Clone)", ""), ["at"] = Vec(Player.m_localPlayer.transform.position), ["distance"] = Math.Round(Vector3.Distance(Player.m_localPlayer.transform.position, at), 1) });
                    yield break;
                }
                case "pose":
                {
                    // shoot pose <name> [zoom] [pitch=18]: a picture of a placed piece for a mod's page. The camera in front of the piece, a
                    // little above and looking down; the player beside it, turned to face the camera; the zoom fitted to the piece's size
                    if (!CheatsAllowed()) { error(CheatRefusal("shoot pose")); yield break; }
                    string name = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    Transform target = null;
                    float bd = 60f;
                    foreach (ZNetView v in UnityEngine.Object.FindObjectsOfType<ZNetView>())
                    {
                        if (v == null || !v.name.ToLowerInvariant().Contains(name)) continue;
                        float d = Vector3.Distance(v.transform.position, player.transform.position);
                        if (d < bd) { bd = d; target = v.transform; }
                    }
                    if (target == null) { error($"no \"{name}\" within 60 m"); yield break; }
                    // how big it is: its renderers' bounds
                    Bounds size = new Bounds(target.position, Vector3.one);
                    foreach (Renderer rend in target.GetComponentsInChildren<Renderer>()) if (rend.enabled && !(rend is ParticleSystemRenderer)) size.Encapsulate(rend.bounds);
                    float extent = Mathf.Max(size.size.x, size.size.z, size.size.y);
                    // its front: the way it faces (many of the game's pieces face -z, so the other way is tried too)
                    Vector3? spot = null;
                    Vector3 front = Vector3.forward;
                    float side = Mathf.Max(size.extents.x, size.extents.z) + 0.7f;
                    foreach (float flip in new[] { 1f, -1f })
                    {
                        front = new Vector3(target.forward.x, 0f, target.forward.z).normalized * flip;
                        Vector3 right = Vector3.Cross(Vector3.up, front);
                        // beside it first (right, then left), closer in and further in front when a wall is in the way; last, straight in front
                        var probes = new List<Vector3>();
                        foreach (float sd in new[] { side, side * 0.75f, side * 0.5f })
                            foreach (float f in new[] { 0.5f, 1.2f, 1.9f })
                            { probes.Add(target.position + right * sd + front * f); probes.Add(target.position - right * sd + front * f); }
                        foreach (float f in new[] { 1.5f, 2.2f, 3f }) probes.Add(target.position + front * (size.extents.z + f));
                        foreach (Vector3 probe in probes)
                        {
                            if (!Physics.Raycast(probe + Vector3.up * 1.2f, Vector3.down, out RaycastHit hit, 2.6f, FloorMask)) continue;
                            if (hit.point.y < target.position.y - 0.6f || hit.point.y > target.position.y + 0.9f) continue;
                            if (Physics.CheckCapsule(hit.point + Vector3.up * 0.45f, hit.point + Vector3.up * 1.7f, 0.33f, FloorMask)) continue;
                            spot = hit.point + Vector3.up * 0.05f;
                            break;
                        }
                        if (spot != null) break;
                    }
                    if (spot == null) { error($"no floor with room to stand beside {target.name}: pose it by hand (shoot tp, shoot look)"); yield break; }
                    float faceYaw = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;   // facing out of the piece's front: towards the camera
                    player.transform.SetPositionAndRotation(spot.Value, Quaternion.Euler(0f, faceYaw, 0f));
                    Rigidbody rb = player.GetComponent<Rigidbody>();
                    if (rb != null) { rb.position = spot.Value; rb.linearVelocity = Vector3.zero; }
                    // the third-person camera sits behind where the player looks: look back at the piece and the player from in front
                    float pitch = Mathf.Clamp(F(a, 4, 18f), 0f, 60f);
                    float zoom = a.Length > 3 && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z) ? z
                               : Mathf.Clamp(extent * 1.0f + 1.5f, 3f, 12f);   // (close enough that the piece fills the picture; more in a big hall)
                    GameCamera cam = GameCamera.instance;
                    if (cam != null)
                    {
                        zoom = Mathf.Clamp(zoom, cam.m_minDistance, cam.m_maxDistance);
                        AccessTools.Field(typeof(GameCamera), "m_distance")?.SetValue(cam, zoom);
                    }
                    yield return new WaitForSeconds(0.3f);
                    if (Player.m_localPlayer == null) yield break;
                    Player.m_localPlayer.SetLookDir(Quaternion.Euler(pitch, faceYaw + 180f, 0f) * Vector3.forward);
                    Player.m_localPlayer.transform.rotation = Quaternion.Euler(0f, faceYaw, 0f);   // (setting where it looks can turn the body: face the camera again)
                    yield return new WaitForSeconds(0.6f);   // (the camera eases to its new distance)
                    output(new JObject { ["posed"] = target.name.Replace("(Clone)", ""), ["at"] = Vec(Player.m_localPlayer.transform.position), ["zoom"] = Math.Round(zoom, 1),
                                         ["pitch"] = pitch, ["size"] = Math.Round(extent, 1), ["note"] = "then shoot hud off, shot (full size), shoot hud on" });
                    yield break;
                }
                case "look":
                    player.SetLookDir(Quaternion.Euler(F(a, 3, 0f), F(a, 2, 0f), 0f) * Vector3.forward);
                    output(new JObject { ["look"] = new JArray(F(a, 2, 0f), F(a, 3, 0f)) });
                    yield break;
                case "inv":
                    InventoryGui.instance?.Show(null);
                    output(new JObject { ["inventory"] = "open" });
                    yield break;
                case "close":
                    CloseWindows();
                    output(new JObject { ["closed"] = true });
                    yield break;
                case "map":
                    Minimap.instance?.SetMapMode(a.Length > 2 && a[2] == "off" ? Minimap.MapMode.Small : Minimap.MapMode.Large);
                    output(new JObject { ["map"] = !(a.Length > 2 && a[2] == "off") });
                    yield break;
                case "build":
                {
                    // the hammer in hand and its build menu open
                    ItemDrop.ItemData hammer = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Hammer");
                    if (hammer == null) { error("no hammer in the bag"); yield break; }
                    if (!hammer.m_equipped) player.EquipItem(hammer);
                    yield return new WaitForSeconds(0.6f);
                    if (!Hud.IsPieceSelectionVisible()) Hud.instance.TogglePieceSelection();
                    output(new JObject { ["build"] = Hud.IsPieceSelectionVisible() });
                    yield break;
                }
                case "unbuild":
                    if (Hud.IsPieceSelectionVisible()) Hud.instance.TogglePieceSelection();
                    output(new JObject { ["build"] = false });
                    yield break;
                case "hud":
                    Hud.instance.m_userHidden = a.Length > 2 && a[2] == "off";
                    output(new JObject { ["hud"] = !Hud.instance.m_userHidden });
                    yield break;
                case "use":
                {
                    // press E on the nearest thing of that name, up to 12 m away (farther than a player reaches: a cheat on a server)
                    if (!CheatsAllowed()) { error(CheatRefusal("shoot use")); yield break; }
                    string name = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                    Interactable best = null;
                    float bd = 12f;
                    foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    {
                        if (!(mb is Interactable it) || !mb.transform.root.name.ToLowerInvariant().Contains(name)) continue;
                        float dist = Vector3.Distance(mb.transform.position, player.transform.position);
                        if (dist < bd) { bd = dist; best = it; }
                    }
                    bool done = best != null && best.Interact(player, false, false);
                    output(new JObject { ["used"] = best != null ? ((MonoBehaviour)best).transform.root.name + " (" + best.GetType().Name + ")" : "nothing near", ["result"] = done });
                    yield break;
                }
                case "open":
                    output(new JObject { ["window"] = OpenModWindow(player, a.Length > 2 ? a[2].ToLowerInvariant() : "") });
                    yield break;
                default:
                    error(ShootUsage);
                    yield break;
            }
        }

        /// <summary>The newest copy of a type: after a hot reload the old copy's assembly is still loaded, but nothing uses it any more.</summary>
        private static Type FindType(string name)
        {
            Type found = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())   // (in the order they were loaded)
            {
                Type t = asm.GetType(name, false);
                if (t != null) found = t;
            }
            return found;
        }

        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The game's windows, and our mods' windows through their own close (as Escape does).</summary>
        private static void CloseWindows()
        {
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (StoreGui.IsVisible()) StoreGui.instance.Hide();
            string[] closers =
            {
                "Arena.Window:Close", "Recycler.Window:Close", "BountyBoard.Window:Close", "LedgerChest.Window:Close",
                "SlotMachine.Window:Close", "AICompanion.Menu:Close", "QualityOfLife.Plugin:CloseFromEscape", "FeedFromChests.Plugin:CloseFromEscape", "BuildOrders.Plugin:Close", "PortalHub.Plugin:CloseFromEscape",
            };
            foreach (string c in closers)
            {
                string[] tm = c.Split(':');
                MethodInfo mi = FindType(tm[0])?.GetMethod(tm[1], AnyStatic, null, Type.EmptyTypes, null);
                try { mi?.Invoke(null, null); } catch (Exception) { }
            }
        }

        /// <summary>A mod window that opens with its own key or a hold, opened as that does: QualityOfLife's chest rules, the recycler.</summary>
        private static string OpenModWindow(Player player, string which)
        {
            const BindingFlags any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (which == "qolrules")
            {
                Container near = UnityEngine.Object.FindObjectsOfType<Container>().Where(c => c.name.StartsWith("piece_chest_wood"))
                    .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault();
                Type plugin = FindType("QualityOfLife.Plugin");
                object inst = plugin?.GetField("Instance", any)?.GetValue(null) ?? plugin?.GetProperty("Instance", any)?.GetValue(null);
                MethodInfo open = plugin?.GetMethod("OpenRules", any);
                if (near == null || inst == null || open == null) return "not found (QualityOfLife and a wooden chest nearby are needed)";
                open.Invoke(inst, new object[] { near, player });
                return "opened at " + near.transform.position;
            }
            if (which == "recycler")
            {
                Type station = FindType("Recycler.RecyclerStation");
                MethodInfo open = FindType("Recycler.Window")?.GetMethod("Open", any);
                Component st = station != null ? UnityEngine.Object.FindObjectsOfType(station).OfType<Component>()
                    .OrderBy(c => (c.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault() : null;
                if (st == null || open == null) return "not found (Recycler and a recycler nearby are needed)";
                open.Invoke(null, new object[] { st });
                return "opened";
            }
            return "say which: open qolrules or open recycler";
        }
    }
}
