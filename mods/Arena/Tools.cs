using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Arena
{
    /// <summary>An "arena" command for the Claude Tools mod (its request mailbox), so an AI assistant can test contests without walking the menu.</summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().Where(p => p != null && p.gameObject.scene.IsValid()).FirstOrDefault(p => MetadataHelper.GetMetadata(p)?.GUID == "com.dhack.claudetools");
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "arena", "arena status | roster | site here | ground | rebuild | tp court|master|gate|tunnel|ring|stands|box | gate <i> [s] | cover <set|none> | place (a waystone in front of you) | start gauntlet|champion|endless [tier] [fists] [nofood] [hard] [timed] [daily] [stake n] | stop | clear (kill the foes) | favour <n>",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: arena command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the arena command to Claude Tools: " + e.Message); }
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
            args = args.Skip(1).ToArray();   // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

            if (sub == "roster")
            {
                var tiers = new JArray();
                for (int t = 0; t < Roster.TierNames.Length; t++)
                    tiers.Add(new JObject { ["tier"] = Roster.TierNames[t], ["fighters"] = new JArray(Roster.Pool(t)), ["champions"] = new JArray(Roster.ChampionPool(t).Select(c => c + " (" + (Roster.Trophy(c) ?? "no trophy") + ")")), ["material"] = Roster.Material(t) });
                output(new JObject { ["stage"] = Roster.Stage(), ["tiers"] = tiers });
                yield break;
            }
            if (sub == "place")
            {
                if (Things.StandObject == null) { error("the standard is not registered"); yield break; }
                Vector3 at = player.transform.position + player.transform.forward * 3f;
                GameObject made = UnityEngine.Object.Instantiate(Things.StandObject, at, Quaternion.Euler(0f, player.transform.eulerAngles.y + 180f, 0f));
                output(new JObject { ["placed"] = made != null, ["at"] = at.ToString(), ["hasStand"] = made != null && made.GetComponent<Stand>() != null });
                yield break;
            }
            if (sub == "site" && args.Length > 1 && args[1] == "here")
            {
                Site.Set(player.transform.position + player.transform.forward * 40f, player.transform.eulerAngles.y + 180f);
                Scenery.Drop();
                output(new JObject { ["site"] = "moved: 40 m ahead, its gate facing you" });
                yield break;
            }
            if (sub == "probe")
            {
                Vector3 at = player.transform.position;
                WorldGenerator gen = WorldGenerator.instance;
                bool fits = Site.Fits(gen, at.x, at.z, player.transform.eulerAngles.y, ZoneSystem.instance.m_waterLevel, out float target, out float spread);
                output(new JObject { ["biome"] = gen.GetBiome(at.x, at.z).ToString(), ["genHeight"] = gen.GetHeight(at.x, at.z), ["ground"] = ZoneSystem.instance.GetGroundHeight(at),
                                     ["water"] = ZoneSystem.instance.m_waterLevel, ["fits"] = fits, ["target"] = target, ["spread"] = spread, ["fromCentre"] = new Vector2(at.x, at.z).magnitude });
                yield break;
            }
            if (sub == "kitlive")
            {
                // the real thing on the player: stow, arm for a land, feed, then give it all back; compare the bag before and after
                string Key(ItemDrop.ItemData i) => $"{i.m_dropPrefab?.name} q{i.m_quality}{(i.m_equipped ? " worn" : "")}";
                Inventory inv = player.GetInventory();
                var before = inv.GetAllItems().GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
                if (!Kit.Stow(player)) { error("already stowed"); yield break; }
                int land = args.Length > 1 && int.TryParse(args[1], out int l) ? l : 2;
                Kit.Arm(player, land, args.Length > 2 && int.TryParse(args[2], out int st) ? st : 0, false);
                Kit.Feed(player, Kit.Plate(land, Kit.DefaultRoles));
                var armed = inv.GetAllItems().Select(i => Key(i) + " x" + i.m_stack + (Kit.IsLoan(i) ? " (lent)" : "")).ToList();
                yield return new WaitForSeconds(3f);
                Kit.Return(player);
                var after = inv.GetAllItems().GroupBy(Key).ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
                var diff = before.Keys.Union(after.Keys).Where(k => (before.TryGetValue(k, out int a) ? a : 0) != (after.TryGetValue(k, out int b) ? b : 0))
                                 .Select(k => $"{k}: {(before.TryGetValue(k, out int a) ? a : 0)} -> {(after.TryGetValue(k, out int b) ? b : 0)}").ToList();
                output(new JObject { ["armed"] = new JArray(armed), ["before"] = before.Count, ["after"] = after.Count, ["differ"] = new JArray(diff), ["stowedLeft"] = Kit.Stowed(player) });
                yield break;
            }
            if (sub == "chest")
            {
                bool prize = args.Length > 1 && args[1] == "prize";
                if (args.Length > 1 && args[1] == "take") { Show.TakeChest(); output(new JObject { ["chest"] = "taken" }); yield break; }
                Container c = prize ? Show.PopChest(Show.Prize, new[] { ("Coins", 50, 1, (string)null) }) : Show.PopChest(Show.Armoury, Kit.Outfit(1, 0, false, true));
                output(new JObject { ["chest"] = c != null ? c.name : "none", ["items"] = c != null ? c.GetInventory().NrOfItems() : 0, ["at"] = c != null ? c.transform.position.ToString() : "" });
                yield break;
            }
            if (sub == "gift") { Favours.Throw(args.Length > 1 && int.TryParse(args[1], out int gl) ? gl : 2); output(new JObject { ["gift"] = "thrown" }); yield break; }
            if (sub == "armourer") { Show.Armourer(args.Length < 2 || args[1] != "off", args.Length > 1 && args[1] == "here" ? player.transform.position + player.transform.forward * 2f : (Vector3?)null); output(new JObject { ["armourer"] = args.Length < 2 || args[1] != "off" }); yield break; }
            if (sub == "store")
            {
                Armourer a = UnityEngine.Object.FindObjectOfType<Armourer>();
                bool shown = a != null && a.Interact(player, false, false);
                output(new JObject { ["store"] = shown, ["visible"] = StoreGui.IsVisible() });
                yield break;
            }
            if (sub == "upgrades")
            {
                var list = new JArray();
                foreach (Armoury.Slot s in Armoury.Slots(0, false))
                    list.Add($"{s}: {Armoury.Name(player, s, 0)} -> " + (Armoury.Next(player, s, 0, out int nt, out string np, out int nq) ? $"{np} q{nq} for {Armoury.Price(s, nt)}" : "none"));
                output(new JObject { ["upgrades"] = list, ["random"] = Armoury.Random(player, 0, false, "x")?.Prefab });
                yield break;
            }
            if (sub == "fireworks") { Show.Fireworks(12, 6f); output(new JObject { ["fireworks"] = 12 }); yield break; }
            if (sub == "kittest") { output(new JObject { ["kit"] = Kit.SelfTest(player), ["stowed"] = Kit.Stowed(player) }); yield break; }
            if (sub == "railtest")
            {
                // from every front-row seat towards the ring, at heights over the seat's floor: is something solid in the way within 3 m?
                int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
                var open = new JArray();
                int tried = 0;
                float front = Layout.Seats.Min(m => new Vector2(m.Pos.x, m.Pos.z).magnitude);
                foreach (Layout.Mark seat in Layout.Seats.Where(m => new Vector2(m.Pos.x, m.Pos.z).magnitude < front + 0.5f))
                    foreach (float h in new[] { 0.4f, 1.2f, 2.0f, 2.8f, 3.4f })
                    {
                        Vector3 from = Site.World(seat.Pos + Vector3.up * h);
                        Vector3 to = Site.World(new Vector3(0f, seat.Pos.y + h, 0f));
                        tried++;
                        if (!Physics.Raycast(from, (to - from).normalized, 3f, mask)) open.Add($"seat at {seat.Pos} height {h}");
                    }
                output(new JObject { ["tried"] = tried, ["open"] = open });
                yield break;
            }
            if (sub == "items")
            {
                // items by type (food, weapon, armour...): name, what it is, and its food values or damage
                string type = args.Length > 1 ? args[1] : "Consumable";
                var list = new JArray();
                foreach (GameObject go in ObjectDB.instance.m_items)
                {
                    ItemDrop.ItemData d = go != null ? go.GetComponent<ItemDrop>()?.m_itemData : null;
                    if (d == null || !d.m_shared.m_itemType.ToString().Equals(type, StringComparison.OrdinalIgnoreCase)) continue;
                    var s = d.m_shared;
                    list.Add($"{go.name} \"{Localization.instance.Localize(s.m_name)}\" " + (s.m_food + s.m_foodStamina + s.m_foodEitr > 0f
                        ? $"food {s.m_food}/{s.m_foodStamina}/{s.m_foodEitr} {s.m_foodBurnTime:0}s regen {s.m_foodRegen}"
                        : $"dmg {s.m_damages.GetTotalDamage():0} armor {s.m_armor:0} skill {s.m_skillType} q{s.m_maxQuality}"));
                }
                output(new JObject { ["items"] = list });
                yield break;
            }
            if (sub == "prefabs" || sub == "sounds")
            {
                string filter = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                var list = new JArray();
                foreach (GameObject go in ZNetScene.instance.m_prefabs)
                {
                    if (go == null || !go.name.ToLowerInvariant().Contains(filter)) continue;
                    if (sub == "sounds")
                    {
                        var clips = go.GetComponentsInChildren<ZSFX>(true).SelectMany(z => z.m_audioClips ?? new AudioClip[0]).Where(c => c != null).Select(c => c.name + " " + c.length.ToString("0.0") + "s").Distinct().Take(6);
                        if (!clips.Any()) continue;
                        list.Add(go.name + ": " + string.Join(", ", clips));
                    }
                    else list.Add(go.name);
                    if (list.Count >= 300) break;
                }
                output(new JObject { ["found"] = list });
                yield break;
            }
            if (sub == "ui")
            {
                var list = new JArray();
                InventoryGui gui = InventoryGui.instance;
                void Walk(Transform t, string path, int depth)
                {
                    if (depth > 9 || list.Count > 400) return;
                    var img = t.GetComponent<UnityEngine.UI.Image>();
                    var tmp = t.GetComponent<TMPro.TMP_Text>();
                    string what = (img != null && img.sprite != null ? " img:" + img.sprite.name + "/" + img.type : "") + (t.GetComponent<UnityEngine.UI.Button>() != null ? " BUTTON" : "")
                                + (tmp != null ? " text:" + tmp.font.name + "/" + tmp.fontSize + "'" + tmp.text.Substring(0, Mathf.Min(20, tmp.text.Length)) + "'" : "");
                    if (what.Length > 0 && !path.Contains("InventoryElement") && !path.Contains("GearSlots") && !path.Contains("RecipeElement")) list.Add(path + what);
                    foreach (Transform c in t) Walk(c, path + "/" + c.name, depth + 1);
                }
                string which = args.Length > 1 ? args[1] : "inventory";
                Transform root = which == "boss" ? EnemyHud.instance.m_baseHudBoss.transform : which == "message" ? MessageHud.instance.transform : gui.transform;
                Walk(root, root.name, 0);
                output(new JObject { ["ui"] = list });
                yield break;
            }
            if (sub == "figure")
            {
                string kind = args.Length > 1 ? args[1] : "master";
                var rng = new System.Random(Environment.TickCount);
                var created = new JArray();
                for (int i = 0; i < (kind == "crowd" ? 6 : 1); i++)
                {
                    Vector3 at = player.transform.position + new Vector3((i - 2.5f) * 1.2f, 0f, 4f);
                    at.y = ZoneSystem.instance.GetGroundHeight(at);
                    Figures.Look look = kind == "master" ? Figures.Master() : kind == "statue" ? Figures.Champion(rng.Next()) : Figures.Spectator(rng);
                    GameObject f = Figures.Make(null, at, Quaternion.LookRotation(Vector3.back), look, false);
                    if (f == null) { error("no Player prefab"); yield break; }
                    if (kind == "statue") Figures.Petrify(f, Figures.Stone(), "challenge");
                    else if (kind == "crowd") Figures.Emote(f, new[] { "cheer", "roar", "toast", "challenge", "flex", "point" }[i % 6]);
                    UnityEngine.Object.Destroy(f, 90f);
                    created.Add(f.name);
                }
                output(new JObject { ["made"] = created });
                yield break;
            }
            if (sub == "menu") { if (args.Length > 3 && int.TryParse(args[3], out int mk)) Window.ContestKind = mk; Window.Open(args.Length > 1 && args[1] == "stone" ? Window.Context.Arrival : Window.Context.Master, null, args.Length > 2 && int.TryParse(args[2], out int mt) ? mt : 0); output(new JObject { ["open"] = Window.IsOpen }); yield break; }
            if (sub == "uidebug")
            {
                Transform canvas = Ui.Canvas;
                var info = new JObject { ["canvas"] = canvas != null ? canvas.name : "none", ["canvasActive"] = canvas != null && canvas.gameObject.activeInHierarchy };
                GameObject win = GameObject.Find("ArenaWindow");
                if (win != null)
                {
                    var r = (RectTransform)win.transform;
                    Vector3[] corners = new Vector3[4]; r.GetWorldCorners(corners);
                    info["parent"] = r.parent != null ? r.parent.name : "none";
                    info["active"] = win.activeInHierarchy;
                    info["corners"] = corners[0] + " " + corners[2];
                    info["children"] = r.childCount;
                    Canvas c = win.GetComponentInParent<Canvas>();
                    info["canvasComp"] = c != null ? c.name + " " + c.renderMode + " order " + c.sortingOrder : "none";
                }
                Transform inv = InventoryGui.instance.transform;
                info["inventoryPath"] = inv.name + " <- " + (inv.parent != null ? inv.parent.name : "") + " <- " + (inv.parent != null && inv.parent.parent != null ? inv.parent.parent.name : "");
                output(info);
                yield break;
            }
            if (sub == "statues")
            {
                var list = new JArray();
                Transform hall = Scenery.Root != null ? Scenery.Root.Find("HallOfFame") : null;
                if (hall != null)
                    foreach (Transform plinth in hall)
                        foreach (Transform c in plinth)
                        {
                            var rends = c.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                            list.Add($"{plinth.name}/{c.name} at {Site.Local(c.position)} scale {c.lossyScale.x:0.00} skinned {rends.Length} active {c.gameObject.activeInHierarchy}");
                            foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true))
                                list.Add($"   {r.GetType().Name} {r.name} enabled {r.enabled}/{r.gameObject.activeInHierarchy} y {r.bounds.min.y:0.00}..{r.bounds.max.y:0.00}" + (r is SkinnedMeshRenderer sk ? $" root {(sk.rootBone != null ? sk.rootBone.name : "-")} bone0 {(sk.bones.Length > 0 && sk.bones[0] != null ? sk.bones[0].name + "@" + sk.bones[0].position.y.ToString("0.00") : "-")}" : ""));
                        }
                foreach (GameObject g in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (g.name == "TheArena" || g.name == "ArenaFigure" && Travel.Distance(g.transform.position) < 60f && g.transform.parent != null && g.transform.parent.name.StartsWith("Plinth") == false && !g.transform.IsChildOf(Scenery.Root ?? g.transform))
                        list.Add("loose: " + g.name + " at " + Site.Local(g.transform.position) + " parent " + (g.transform.parent != null ? g.transform.parent.name : "none"));
                int roots = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Count(g => g.name == "TheArena");
                list.Add("arena roots: " + roots);
                output(new JObject { ["statues"] = list, ["plinthTop"] = Layout.Hall.Count > 0 ? Site.World(Layout.Hall[0].Pos).y : 0f });
                yield break;
            }
            if (sub == "big")
            {
                var list = new JArray();
                foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.enabled || Travel.Distance(r.bounds.center) > 70f) continue;
                    Vector3 size = r.bounds.size;
                    if (size.y < 5f && size.x < 9f && size.z < 9f) continue;
                    Transform t = r.transform; string path = t.name;
                    for (int k = 0; k < 4 && t.parent != null; k++) { t = t.parent; path = t.name + "/" + path; }
                    list.Add($"{path} {r.GetType().Name} size {size} at {Site.Local(r.bounds.center)}");
                }
                output(new JObject { ["big"] = list });
                yield break;
            }
            if (sub == "hide" && args.Length > 1)
            {
                Transform t = Scenery.Root != null ? Scenery.Root.Find(args[1]) : null;
                if (t == null) { error("no " + args[1] + " in the arena"); yield break; }
                t.gameObject.SetActive(!t.gameObject.activeSelf);
                output(new JObject { [args[1]] = t.gameObject.activeSelf });
                yield break;
            }
            if (sub == "close") { Window.Close(); output(new JObject { ["open"] = Window.IsOpen }); yield break; }
            if (sub == "sound") { Crowd.Play(args.Length > 1 ? args[1] : "cheer"); output(new JObject { ["sounds"] = Sound.Ready }); yield break; }
            if (sub == "scan") { Site.Scan(); output(new JObject { ["scan"] = Site.Stats }); yield break; }
            if (sub == "ground") { Ground.Redo(); output(new JObject { ["ground"] = "will be shaped again when you are near" }); yield break; }
            if (sub == "rebuild") { Scenery.Drop(); output(new JObject { ["scenery"] = "dropped: it is put up again in a moment" }); yield break; }
            if (sub == "tp" && Site.Known)
            {
                string where = args.Length > 1 ? args[1].ToLowerInvariant() : "court";
                Layout.Mark m = where == "ring" ? new Layout.Mark { Pos = new Vector3(0f, 1f, -6f) } : where == "stands" ? Layout.Stands : where == "master" ? Layout.Desk
                              : where == "box" ? Layout.Master : where == "tunnel" ? Layout.Tunnel : where == "gate" ? Layout.MainGate : Layout.Arrival;
                Vector3 to = Site.Point(m, 0.5f) + (where == "master" ? Site.Turn * new Vector3(0f, 0f, -2f) : where == "court" ? Site.Turn * new Vector3(0f, 0f, 2.5f) : Vector3.zero);
                Quaternion face = Site.Turn;
                if (where == "hall") { to = Site.World(Layout.Hall[0].Pos + new Vector3(5.5f, -Layout.Hall[0].Pos.y + 0.3f, -1.5f)); face = Site.Turn * Quaternion.Euler(0f, 285f, 0f); }
                player.TeleportTo(to, face, false);
                output(new JObject { ["to"] = where, ["at"] = to.ToString() });
                yield break;
            }
            if (sub == "gate" && args.Length > 1 && int.TryParse(args[1], out int gi)) { Scenery.OpenGate(gi, args.Length > 2 && float.TryParse(args[2], out float gs) ? gs : 5f); output(new JObject { ["gate"] = gi }); yield break; }
            if (sub == "cover") { if (args.Length > 1 && args[1] != "none") Scenery.ShowProps(args[1]); else Scenery.ClearProps(); output(new JObject { ["cover"] = Scenery.PropSet }); yield break; }
            if (sub == "start")
            {
                string what = args.Length > 1 ? args[1].ToLowerInvariant() : "road";
                int tier = args.Length > 2 && int.TryParse(args[2], out int t) ? t : Roster.Stage();
                bool Flag(string f) => args.Any(a => a.ToLowerInvariant() == f);
                int stake = 0;
                for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "stake") int.TryParse(args[i + 1], out stake);
                Contest.KindOf kind = what.StartsWith("champ") ? Contest.KindOf.Champion : what.StartsWith("endless") ? Contest.KindOf.Endless : what.StartsWith("trial") ? Contest.KindOf.Trial : Contest.KindOf.Road;
                int style = -1;
                for (int i = 0; i < args.Length - 1; i++) if (args[i].ToLowerInvariant() == "style") int.TryParse(args[i + 1], out style);
                string why = Contest.Start(kind, tier, style, Flag("fists"), Flag("nofood"), Flag("hard"), Flag("timed"), stake, Flag("daily"));
                if (why != null) { error(why); yield break; }
            }
            else if (sub == "stop") Contest.Abort("Stopped by a command.");
            else if (sub == "clear")
            {
                foreach (Character c in Character.GetAllCharacters().ToList())
                    if (c != null && !c.IsPlayer() && c.GetComponent<ZNetView>() != null && c.GetComponent<ZNetView>().IsValid() && c.GetComponent<ZNetView>().GetZDO().GetBool("dh_arena", false)) c.SetHealth(0f);
            }
            else if (sub == "favour" && args.Length > 1 && float.TryParse(args[1], out float f)) Crowd.Gain(f - Crowd.Favour, false);

            output(new JObject
            {
                ["active"] = Contest.Active, ["phase"] = Contest.Phase.ToString(), ["title"] = Contest.Title, ["round"] = Contest.Round, ["rounds"] = Contest.Rounds,
                ["foesLeft"] = Contest.FoesLeft, ["purse"] = Contest.PurseText(), ["mood"] = Crowd.Mood, ["kills"] = Contest.Kills, ["favour"] = Crowd.Favour, ["rules"] = Rules.Text(), ["stake"] = Contest.Stake,
                ["site"] = Site.Known ? Site.Origin.ToString() : "none", ["siteYaw"] = Site.Yaw, ["ground"] = Ground.Done ? "done" : Ground.Status, ["built"] = Scenery.Built, ["cover"] = Scenery.PropSet, ["busy"] = Net.Busy, ["away"] = Travel.Distance(player.transform.position), ["duel"] = Duel.Active, ["title_you"] = Ladder.Title(), ["wins"] = Ladder.Wins, ["stage"] = Roster.Stage(),
            });
        }
    }
}
