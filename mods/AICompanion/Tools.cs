using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AICompanion
{
    /// <summary>
    /// "companion" commands for the Claude Tools mod (its request mailbox), so an AI assistant can check on the companion while testing.
    /// Found and registered while the game runs (no reference between the mods), again whenever Claude Tools is reloaded.
    /// </summary>
    public partial class Plugin
    {
        private const string ClaudeToolsGuid = "com.quad.claudetools";
        private BaseUnityPlugin _claudeTools;
        private float _nextClaudeCheck;

        private void UpdateClaudeLink()
        {
            if (Time.unscaledTime < _nextClaudeCheck) return;
            _nextClaudeCheck = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid, out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                if (register == null) return;
                register.Invoke(null, new object[] { Name, "companion",
                    "companion status | summon [name] | order <follow|stay|guard> | style <aggressive|balanced|defensive|passive> | decide | menu [tab|close] | list | remove <id> | send-home: your companion (JSON)",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)CmdCompanion });
                Logger.LogInfo("Claude Tools found: companion command added");
            }
            catch (Exception e) { Logger.LogWarning("Could not add the companion command to Claude Tools: " + e.Message); }
        }

        private void UnregisterClaudeCommands()
        {
            try { _claudeTools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Name }); }
            catch (Exception) { }
        }

        private IEnumerator CmdCompanion(string[] args, Action<JObject> output, Action<string> error)
        {
            Player p = Player.m_localPlayer;
            if (p == null) { error("no player in the world"); yield break; }
            args = args.Skip(1).ToArray(); // (the first word is the command's own name)
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            Humanoid c = Companion.MineNear(p, 200f);

            if (sub == "summon")
            {
                if (c != null) { error($"{Companion.NameOf(c)} is already here"); yield break; }
                c = Companion.Summon(p, args.Length > 1 ? string.Join(" ", args.Skip(1)) : null);
                yield return new WaitForSeconds(1f);
                output(Describe(c));
                yield break;
            }
            if (sub == "list")
            {
                var all = new JArray();
                foreach (Humanoid h in Companion.All())
                    all.Add(new JObject
                    {
                        ["id"] = Companion.IdOf(h).ToString(), ["name"] = Companion.NameOf(h), ["mine"] = Companion.IsMine(h, p),
                        ["order"] = Companion.OrderOf(h).ToString(), ["status"] = Companion.StatusOf(h), ["items"] = h.GetInventory().NrOfItems(),
                        ["distance"] = Math.Round(Vector3.Distance(h.transform.position, p.transform.position), 1),
                        ["position"] = new JArray(Math.Round(h.transform.position.x), Math.Round(h.transform.position.y), Math.Round(h.transform.position.z)),
                    });
                output(new JObject { ["companions"] = all, ["profiles"] = new JArray(Profile.Here(p).Select(x => $"{x.Id} {x.Name}{(x.Dead ? " (fallen)" : "")}")) });
                yield break;
            }
            if (sub == "inspect")
            {
                // One companion by its id (companion list): who it is, everything it has, its skills.
                Humanoid who = args.Length > 1 ? Companion.All().FirstOrDefault(h => Companion.IdOf(h).ToString() == args[1]) : null;
                if (who == null) { error("inspect <id> (see: companion list)"); yield break; }
                JObject d = Describe(who);
                d["id"] = args[1];
                d["bag"] = new JArray(who.GetInventory().GetAllItems().OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x)
                    .Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)} x{i.m_stack} q{i.m_quality} at {i.m_gridPos.x},{i.m_gridPos.y}"));
                d["skills"] = Companion.Zdo(who).GetString(Skill.Key, "");
                d["journal_entries"] = (Companion.Zdo(who).GetString(Journal.EntriesKey, "") ?? "").Split('\n').Length;
                output(d);
                yield break;
            }
            if (sub == "retire")
            {
                // A companion sent away for good, its things kept: everything it has (bag and gear slots) goes into your chests near it (by
                // your chest rules with QualityOfLife, then any chest with room); what fits nowhere goes into a tombstone where it stands.
                Humanoid extra = args.Length > 1 ? Companion.All().FirstOrDefault(h => Companion.IdOf(h).ToString() == args[1]) : null;
                if (extra == null) { error("retire <id> (see: companion list)"); yield break; }
                if (!Companion.IsMine(extra, p)) { error("that companion is not yours"); yield break; }
                if (!Containers.Take(extra.GetComponent<Container>())) { error("someone has its gear open"); yield break; } // (what is really in its bag)
                Inventory inv = extra.GetInventory();
                var before = inv.GetAllItems().Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)} x{i.m_stack}").ToList();
                foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
                {
                    if (extra.IsItemEquiped(item)) extra.UnequipItem(item, false);
                    item.m_equipped = false;
                }
                int sorted = 0;
                if (AppDomain.CurrentDomain.GetData("DHack.QoL.StackInventory") is Func<Inventory, Vector3, float, Func<ItemDrop.ItemData, bool>, int> stack)
                    sorted = stack(inv, extra.transform.position, 40f, null);
                var put = new System.Collections.Generic.List<string>();
                foreach (ItemDrop.ItemData item in inv.GetAllItems().ToList())
                {
                    foreach (Container chest in Work.YourChests(extra, extra.transform.position, 40f))
                    {
                        if (!chest.GetInventory().CanAddItem(item) || !Containers.Take(chest) || !chest.GetInventory().CanAddItem(item)) continue; // (loaded fresh: room still?)
                        string what = $"{Localization.instance.Localize(item.m_shared.m_name)} x{item.m_stack}";
                        chest.GetInventory().MoveItemToThis(inv, item);
                        put.Add($"{what} -> chest at {chest.transform.position:F0}");
                        break;
                    }
                }
                int left = inv.NrOfItems();
                if (left > 0) Companion.DropGear(extra);
                Profile.Forget(p, Companion.IdOf(extra));
                Logger.LogInfo($"Retired the companion {Companion.NameOf(extra)} ({args[1]}) at {extra.transform.position:F0}: {sorted} sorted into your chests, {put.Count} more stacks into chests with room, {left} stacks to a tombstone");
                ZNetScene.instance.Destroy(extra.gameObject);
                output(new JObject { ["retired"] = args[1], ["had"] = new JArray(before), ["sorted_by_rules"] = sorted, ["into_chests"] = new JArray(put), ["to_tombstone"] = left });
                yield break;
            }
            if (sub == "world")
            {
                // Every companion anywhere in the world (this game's copy of the world: all of it on the game hosting it).
                Remote.Rescan();
                var list = new System.Collections.Generic.List<ZDO>();
                int index = 0;
                for (int guard = 0; guard < 1000 && !ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefab.PrefabName, list, ref index); guard++) { }
                var arr = new JArray();
                foreach (ZDO z in list)
                    arr.Add(new JObject { ["id"] = z.GetLong(Keys.Id, 0L).ToString(), ["name"] = z.GetString(Keys.Name, "?"), ["master"] = z.GetString(Keys.MasterName, ""),
                        ["position"] = z.GetPosition().ToString("F0"), ["health"] = z.GetFloat(ZDOVars.s_health, -1f), ["status"] = z.GetString(Keys.Status, "") });
                output(new JObject { ["host"] = ZNet.instance != null && ZNet.instance.IsServer(), ["companions_in_world"] = arr });
                yield break;
            }
            if (sub == "remove")
            {
                // A leftover companion (from before only one could be summoned): its gear goes into a tombstone, then it is gone for good.
                Humanoid extra = args.Length > 1 ? Companion.All().FirstOrDefault(h => Companion.IdOf(h).ToString() == args[1]) : null;
                if (extra == null) { error("remove <id> (see: companion list)"); yield break; }
                if (!Companion.IsMine(extra, p)) { error("that companion is not yours"); yield break; }
                if (!Containers.Take(extra.GetComponent<Container>())) { error("someone has its gear open"); yield break; } // (what is really in its bag)
                int items = extra.GetInventory().NrOfItems();
                if (items > 0) Companion.DropGear(extra);
                Profile.Forget(p, Companion.IdOf(extra));
                Logger.LogInfo($"Removed the companion {Companion.NameOf(extra)} ({Companion.IdOf(extra)}) at {extra.transform.position:F0}; {items} item stacks went to a tombstone");
                ZNetScene.instance.Destroy(extra.gameObject);
                output(new JObject { ["removed"] = args[1], ["items_to_tombstone"] = items });
                yield break;
            }
            if (c == null) { error("you have no companion within 200 m (try: companion summon)"); yield break; }

            switch (sub)
            {
                case "debug":
                    output(Debug(c, args.Length > 1 && int.TryParse(args[1], out int lines) ? lines : 60));
                    yield break;
                case "archery":
                    var arrows = new JArray();
                    foreach (string name in new[] { "AxeStone", "AxeFlint", "AxeBronze", "PickaxeAntler", "Club" })
                    {
                        Attack m = ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack;
                        if (m != null) arrows.Add($"{name}: range {m.m_attackRange:0.##}, height {m.m_attackHeight:0.##}, ray width {m.m_attackRayWidth:0.##}, angle {m.m_attackAngle:0}, offset {m.m_attackOffset:0.##}, type {m.m_attackType}, origin '{m.m_attackOriginJoint}'");
                    }
                    foreach (string name in new[] { "Bow", "BowFineWood", "ArrowWood", "ArrowFlint", "ArrowFire", "ArrowBronze" })
                    {
                        GameObject go = ObjectDB.instance.GetItemPrefab(name);
                        Attack at = go?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack;
                        Projectile pr = at?.m_attackProjectile != null ? at.m_attackProjectile.GetComponent<Projectile>() : null;
                        arrows.Add($"{name}: vel {at?.m_projectileVel:0.#} (min {at?.m_projectileVelMin:0.#}), accuracy {at?.m_projectileAccuracy:0.#} (min {at?.m_projectileAccuracyMin:0.#}), launch angle {at?.m_launchAngle:0.#}, projectile {(at?.m_attackProjectile != null ? at.m_attackProjectile.name : "-")}, gravity {pr?.m_gravity:0.##}, drag {pr?.m_drag:0.###}");
                    }
                    output(new JObject { ["archery"] = arrows });
                    yield break;
                case "sort":
                    var bagBefore = c.GetInventory().GetAllItems().Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)} x{i.m_stack}").ToList();
                    int sorted = Work.SortHome(Brain.Get(c));
                    output(new JObject { ["qol_installed"] = AppDomain.CurrentDomain.GetData("DHack.QoL.StackInventory") != null, ["moved"] = sorted, ["bag_before"] = new JArray(bagBefore),
                                         ["bag_after"] = new JArray(c.GetInventory().GetAllItems().Select(i => $"{Localization.instance.Localize(i.m_shared.m_name)} x{i.m_stack}")) });
                    yield break;
                case "catchup":
                    float secs = args.Length > 1 && float.TryParse(args[1], out float sv) ? sv : 600f;
                    string failed = CatchUp.Test(Brain.Get(c), secs);
                    output(new JObject { ["catchup_seconds"] = secs, ["error"] = failed, ["history"] = new JArray(Brain.Get(c).History.Take(2)) });
                    yield break;
                case "packtest":
                    var wornNow = Companion.Worn(c).ToList();
                    string packed = Companion.Pack(wornNow);
                    var back = new Inventory("test", null, 8, 4);
                    if (!string.IsNullOrEmpty(packed)) back.Load(new ZPackage(System.Convert.FromBase64String(packed)));
                    string Sig(ItemDrop.ItemData i) => $"{i.m_dropPrefab?.name ?? i.m_shared.m_name} q{i.m_quality} d{i.m_durability:0.#} x{i.m_stack}";
                    var before = wornNow.Select(Sig).OrderBy(x => x).ToList();
                    var after = back.GetAllItems().Select(Sig).OrderBy(x => x).ToList();
                    output(new JObject { ["worn"] = new JArray(before), ["restored"] = new JArray(after), ["same"] = before.SequenceEqual(after), ["packed_chars"] = packed.Length });
                    yield break;
                case "hazards": output(new JObject { ["around_you"] = Steer.Around(p.transform.position, args.Length > 1 && float.TryParse(args[1], out float rr) ? rr : 40f) }); yield break;
                case "home": Home.GoHome(c); break;
                case "follow": Home.Follow(c); break;
                case "toggle": Home.ToggleAll(p); break;
                case "goal":
                    BrainState gs = Brain.Get(c);
                    gs.Goal = Goals.Pick(c, Work.Center(c), Work.RadiusOf(c));
                    Goal g = gs.Goal;
                    output(g == null ? new JObject { ["goal"] = null } : new JObject
                    {
                        ["goal"] = g.What, ["station"] = g.Station != null ? Localization.instance.Localize(g.Station.m_name) : null, ["gather"] = g.RawText(),
                        ["steps"] = new JArray(g.Steps.Select(s => s.Key.m_item.name)), ["smelt"] = new JArray(g.Smelt), ["ask"] = new JArray(g.Ask), ["jobs"] = Goals.JobsFor(g, out _).ToString(),
                        ["recipe"] = g.Recipe != null ? g.Recipe.name + ": " + string.Join(", ", g.Recipe.m_resources.Where(q => q.m_resItem != null).Select(q => $"{q.GetAmount(1)} {q.m_resItem.name}")) : null,
                        ["recipes_for_item"] = g.Recipe != null ? new JArray(ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item == g.Recipe.m_item).Select(r => $"{r.name} enabled={r.m_enabled}: " + string.Join(", ", r.m_resources.Where(q => q.m_resItem != null).Select(q => $"{q.GetAmount(1)} {q.m_resItem.name}")))) : null,
                    });
                    yield break;
                case "order":
                    if (args.Length < 2 || !Enum.TryParse(args[1], true, out Order order)) { error("order <follow|stay|guard>"); yield break; }
                    Companion.Write(c, z => { z.Set(Keys.Order, (int)order); if (order == Order.Guard) z.Set(Keys.Post, c.transform.position); });
                    break;
                case "style":
                    if (args.Length < 2 || !Enum.TryParse(args[1], true, out Style style)) { error("style <aggressive|balanced|defensive|passive>"); yield break; }
                    Companion.Write(c, z => z.Set(Keys.Style, (int)style));
                    break;
                case "decide":
                    BrainState st = Brain.Get(c);
                    if (st.Enemies.Count == 0) { error("no enemies near it, so there is nothing to decide"); yield break; }
                    Decision got = Brain.BuiltIn(st, Companion.Master(c));
                    output(new JObject { ["decision"] = got.Describe(st.Label), ["note"] = got.Note });
                    yield break;
                case "map":
                {
                    Bed mb = Home.BedOf(c);
                    int r = args.Length > 1 && int.TryParse(args[1], out int rr2) ? Mathf.Clamp(rr2, 5, 40) : 20;
                    Vector3 to = args.Length > 2 && args[2] == "me" ? p.transform.position : mb != null ? mb.GetSpawnPoint() : Work.Center(c);
                    output(new JObject { ["map"] = new JArray(Wayfinding.Map(c, to, p.transform.position, r)) });
                    yield break;
                }
                case "path":
                {
                    Bed pb = Home.BedOf(c);
                    Vector3 to = args.Length > 1 && args[1] == "me" ? p.transform.position : args.Length > 1 && args[1] == "home" ? Work.Center(c) : pb != null ? pb.GetSpawnPoint() : Work.Center(c);
                    output(new JObject { ["path"] = new JArray(Brain.PathReport(c, to)) });
                    yield break;
                }
                case "emote":
                    if (args.Length < 2) { error("companion emote <wave|cheer|thumbsup|sit|stand|...>"); yield break; }
                    {
                        BrainState es = Brain.Get(c);
                        string em = args[1].ToLowerInvariant();
                        bool ok = true;
                        if (em == "sit") { Idle.SitDown(es); es.SitKeep = Time.time + 10f; } // (10 s, then the brain takes over again)
                        else if (em == "stand") Idle.Stand(es);
                        else ok = Idle.Emote(es, em);
                        output(new JObject { ["emote"] = em, ["played"] = ok, ["sitting"] = es.Sitting, ["plan"] = es.IdlePlan.ToString() });
                    }
                    yield break;
                case "menu":
                    if (args.Length > 1 && args[1].ToLowerInvariant() == "close") { CloseMenu(); output(new JObject { ["menu"] = "closed" }); yield break; }
                    OpenMenuFor(p, c);
                    if (args.Length > 1 && Enum.TryParse(args[1], true, out Tab tab)) _tab = tab;
                    output(new JObject { ["menu"] = _tab.ToString() });
                    yield break;
                case "send-home":
                    if (!Companion.Dismiss(c, out string why)) { error(why); yield break; }
                    output(new JObject { ["sent_home"] = true });
                    yield break;
            }
            yield return null;
            output(Describe(c));
        }

        private static JObject Describe(Humanoid c)
        {
            if (c == null) return new JObject { ["companion"] = null };
            BrainState st = Brain.Get(c);
            Player p = Player.m_localPlayer;
            return new JObject
            {
                ["name"] = Companion.NameOf(c),
                ["position"] = new JArray(Math.Round(c.transform.position.x, 1), Math.Round(c.transform.position.y, 1), Math.Round(c.transform.position.z, 1)),
                ["distance"] = p != null ? Math.Round(Vector3.Distance(p.transform.position, c.transform.position), 1) : -1,
                ["health"] = $"{c.GetHealth():0}/{c.GetMaxHealth():0}",
                ["armor"] = Math.Round(Companion.Armor(c), 1),
                ["owner_here"] = c.GetComponent<ZNetView>().IsOwner(),
                ["order"] = Companion.OrderOf(c).ToString(),
                ["style"] = $"{Companion.Chosen(c)} (fighting as {Companion.StyleOf(c)})",
                ["status"] = Companion.StatusOf(c),
                ["worn"] = new JArray(Companion.Worn(c).Select(i => Localization.instance.Localize(i.m_shared.m_name))),
                ["items"] = c.GetInventory().NrOfItems(),
                ["in_combat"] = st.InCombat,
                ["enemies"] = new JArray(st.Enemies.Select(e => st.Label(e))),
                ["history"] = new JArray(st.History),
                ["goal"] = st.Goal?.What,
                ["hazards_near"] = Steer.HazardsNear(c),
                ["bed"] = Companion.Zdo(c).GetBool(Keys.HasBed, false), ["chests"] = Home.Chests(c).Count,
            };
        }
    
        private static readonly System.Func<BaseAI, Vector3, bool> HavePath = HarmonyLib.AccessTools.MethodDelegate<System.Func<BaseAI, Vector3, bool>>(HarmonyLib.AccessTools.Method(typeof(BaseAI), "HavePath"));

        /// <summary>Its legs: where it means to go, how fast it goes, on the ground, a path to its task, what is in front of it.</summary>
        private static string Move(Humanoid c, Component target)
        {
            Rigidbody body = c.GetComponent<Rigidbody>();
            Vector3 dir = c.GetMoveDir();
            string path = target != null ? (HavePath(c.GetComponent<BaseAI>(), target.transform.position) ? "path yes" : "path NO") : "no target";
            string ahead = "";
            if (dir.sqrMagnitude > 0.01f && Physics.Raycast(c.transform.position + Vector3.up * 0.8f, dir.normalized, out RaycastHit hit, 2f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle")))
                ahead = $", blocked {hit.distance:0.0} m ahead by {Utils.GetPrefabName(hit.collider.transform.root.gameObject)}";
            return $"dir {dir.x:0.00},{dir.z:0.00} (|{dir.magnitude:0.00}|), speed {(body != null ? body.linearVelocity.magnitude : 0f):0.00}, ground {c.IsOnGround()}, run {c.IsRunning()}, {path}{ahead}, stamina {Stamina.Get(c):0} (last used {Stamina.UsedBy(c)}), steer: {Steer.Debug(c)}";
        }

        /// <summary>Everything about a companion, for finding out why it does what it does (Claude Tools: companion debug [lines]).</summary>
        private static JObject Debug(Humanoid c, int lines)
        {
            BrainState st = Brain.Get(c);
            ZDO z = Companion.Zdo(c);
            Vector3 center = Work.Center(c);
            string L(string s) => Localization.instance.Localize(s ?? "");
            Work.Task t = st.Task;
            var o = new JObject
            {
                ["vitals"] = Activity.Vitals(c, st),
                ["runs_here"] = c.GetComponent<ZNetView>().IsOwner(),
                ["status"] = st.Status,
                ["position"] = $"{c.transform.position:F0}",
                ["home"] = $"{center:F0}, {Vector3.Distance(center, c.transform.position):0} m away, radius {Work.RadiusOf(c)}",
                ["bed_spot"] = Home.BedOf(c) is Bed bd ? $"{bd.GetSpawnPoint():F1}, {Vector3.Distance(bd.GetSpawnPoint(), c.transform.position):0.0} m away, safe {Steer.Safe(bd.GetSpawnPoint(), c)}, path {Brain.CanReach(c, bd.GetSpawnPoint())}" : "none",
                ["bag_room"] = $"{c.GetInventory().GetEmptySlots()} free slots, {Carry.Weight(c):0}/{Carry.Max(c):0} kg, full {Work.BagFull(c)}, stock: {string.Join(", ", Work.StockCaps(Brain.Get(c)).Select(kv => $"{L(kv.Key)} {kv.Value}"))}",
                ["between_jobs"] = $"{Brain.Get(c).IdlePlan}, sitting {Brain.Get(c).Sitting}, chair {(Brain.Get(c).SitChair != null)}, asleep {Brain.Get(c).Asleep}",
                ["task"] = t == null ? null : $"{t.Kind} {(t.Target != null ? Utils.GetPrefabName(t.Target.gameObject) : "-")} at {(t.Target != null ? Vector3.Distance(t.Target.transform.position, c.transform.position) : 0f):0.0} m for {Time.time - t.Started:0} s{(t.ForGoal ? " (for goal)" : "")}",
                ["work_note"] = st.WorkNote,
                ["duty"] = Duties.Configured(c) ? (st.Duty != null ? $"{st.Duty.Duty}: {Duties.Have(c, st.Duty.Duty)} of {st.Duty.Target}" : "all stocked: its own life") : "none set",
                ["jobs"] = Work.JobsOf(c) == Job.None ? "auto: " + Work.AutoJobs(c) : Work.JobsOf(c).ToString(),
                ["skipped"] = st.Skipped.Count(kv => kv.Value > Time.time),
                ["in_combat"] = st.InCombat,
                ["enemies"] = new JArray(st.Enemies.Where(e => e != null).Select(e => $"{st.Label(e)} {Vector3.Distance(e.transform.position, c.transform.position):0.0} m, {e.transform.position.y - c.transform.position.y:+0.0;-0.0} m up, radius {e.GetRadius():0.0}, seen {c.GetComponent<BaseAI>().CanSeeTarget(e)}, hp {e.GetHealth():0}/{e.GetMaxHealth():0}, targets {(Brain.TargetOf(e) != null ? L(Brain.TargetOf(e).m_name) : "-")}")),
                ["decision"] = st.Current.Describe(st.Label) + (string.IsNullOrEmpty(st.Current.Note) ? "" : " - " + st.Current.Note),
                ["rested"] = Rest.IsRested(c),
                ["effects"] = new JArray(c.GetSEMan().GetStatusEffects().Where(se => se != null).Select(se => L(se.m_name))),
                ["hazards_near"] = Steer.HazardsNear(c),
                ["move"] = Move(c, t?.Target),
                ["you_walk_through"] = Player.m_localPlayer != null && c.GetComponent<CapsuleCollider>() is CapsuleCollider cc && Player.m_localPlayer.GetComponent<CapsuleCollider>() is CapsuleCollider pc ? Physics.GetIgnoreCollision(cc, pc) : (bool?)null,
                ["slots"] = new JArray(Companion.Worn(c).Select(i => $"{L(i.m_shared.m_name)} (in bag: {c.GetInventory().ContainsItem(i)}, flag {i.m_equipped}, game says equipped: {c.IsItemEquiped(i)})")),
                ["bag"] = new JArray(c.GetInventory().GetAllItems().Select(i => $"{L(i.m_shared.m_name)} x{i.m_stack}{(Companion.Worn(c).Contains(i) ? " (worn)" : "")}{(i.m_shared.m_useDurability && i.GetMaxDurability() > 0f ? $" {i.m_durability / i.GetMaxDurability() * 100f:0}%" : "")}")),
                ["chests"] = new JArray(Home.Chests(c).Select(ch => $"{Vector3.Distance(ch.transform.position, center):0} m from home: {ch.GetInventory().NrOfItems()} stacks: " + string.Join(", ", ch.GetInventory().GetAllItems().Take(12).Select(i => $"{L(i.m_shared.m_name)} x{i.m_stack}")))),
                ["cooking_near_home"] = new JArray(Kitchen.Describe(c, center, Work.RadiusOf(c))),
                ["stations_near_home"] = new JArray(Upgrades.StationsNear(center, Work.RadiusOf(c) + 10f).Select(s => $"{L(s.m_name)} level {s.GetLevel()}, {Vector3.Distance(s.transform.position, center):0} m from home")),
                ["goal"] = st.Goal == null ? null : $"{st.Goal.What}: gather {st.Goal.RawText()}; ask {string.Join(", ", st.Goal.Ask)}; steps {string.Join(", ", st.Goal.Steps.Select(s => s.Key.m_item.name))}",
                ["last_hurt"] = string.IsNullOrEmpty(st.LastHurtBy) ? null : $"{st.LastHurtBy}, {Time.time - st.LastHurtAt:0} s ago",
                ["activity"] = new JArray(Activity.Recent(c, lines)),
            };
            return o;
        }
}
}
