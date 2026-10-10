using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
            "shoot find <name> [radius=120] | tp x y z yaw [pitch] | look yaw [pitch] | inv | close | map on|off | build | unbuild | hud on|off | " +
            "use <name> | open qolrules|recycler: set up a screenshot (tp and use only where cheats are allowed)";

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

        private static Type FindType(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The game's windows, and our mods' windows through their own close (as Escape does).</summary>
        private static void CloseWindows()
        {
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            if (StoreGui.IsVisible()) StoreGui.instance.Hide();
            string[] closers =
            {
                "Arena.Window:Close", "PortalHub.Plugin:CloseFromEscape", "Recycler.Window:Close", "BountyBoard.Window:Close", "LedgerChest.Window:Close",
                "SlotMachine.Window:Close", "AICompanion.Menu:Close", "QualityOfLife.Plugin:CloseFromEscape",
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
