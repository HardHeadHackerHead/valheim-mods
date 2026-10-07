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
        private const string ClaudeToolsGuid = "com.dhack.claudetools";
        private BaseUnityPlugin _claudeTools;
        private float _nextClaudeCheck;

        private void UpdateClaudeLink()
        {
            if (Time.unscaledTime < _nextClaudeCheck) return;
            _nextClaudeCheck = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().Where(p => p != null && p.gameObject.scene.IsValid()).FirstOrDefault(p => MetadataHelper.GetMetadata(p)?.GUID == ClaudeToolsGuid);
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                if (register == null) return;
                register.Invoke(null, new object[] { Name, "companion",
                    "companion status | summon [name] | order <follow|stay|guard> | style <aggressive|balanced|defensive|passive> | jevtest | decide | menu [tab|close] | list | remove <id> | send-home: your companion (JSON)",
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
            if (sub == "jevtest")
            {
                string result = null;
                yield return Jev.Test(r => result = r);
                output(new JObject { ["jev"] = result, ["key"] = Mask(ApiKey.Value) });
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
            if (sub == "remove")
            {
                // A leftover companion (from before only one could be summoned): its gear goes into a tombstone, then it is gone for good.
                Humanoid extra = args.Length > 1 ? Companion.All().FirstOrDefault(h => Companion.IdOf(h).ToString() == args[1]) : null;
                if (extra == null) { error("remove <id> (see: companion list)"); yield break; }
                if (!Companion.IsMine(extra, p)) { error("that companion is not yours"); yield break; }
                ZNetView view = extra.GetComponent<ZNetView>();
                if (!view.IsOwner()) view.ClaimOwnership();
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
                    Decision got = null;
                    yield return Jev.Decide(st, Companion.Master(c), Brain.BuiltIn(st, Companion.Master(c)), d => got = d);
                    output(new JObject { ["decision"] = got?.Describe(st.Label), ["note"] = got?.Note, ["ms"] = Mathf.RoundToInt(Jev.LastMs) });
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
                ["style"] = Companion.StyleOf(c).ToString(),
                ["status"] = Companion.StatusOf(c),
                ["worn"] = new JArray(Companion.Worn(c).Select(i => Localization.instance.Localize(i.m_shared.m_name))),
                ["items"] = c.GetInventory().NrOfItems(),
                ["in_combat"] = st.InCombat,
                ["enemies"] = new JArray(st.Enemies.Select(e => st.Label(e))),
                ["history"] = new JArray(st.History),
                ["goal"] = st.Goal?.What,
                ["hazards_near"] = Steer.HazardsNear(c),
                ["bed"] = Companion.Zdo(c).GetBool(Keys.HasBed, false), ["chests"] = Home.Chests(c).Count,
                ["jev"] = new JObject { ["key"] = Mask(ApiKey.Value), ["decisions_today"] = Jev.Decisions, ["failures_today"] = Jev.Failures, ["last_ms"] = Mathf.RoundToInt(Jev.LastMs), ["last_error"] = Jev.LastError, ["cost_usd"] = Math.Round(Jev.Cost, 5) },
            };
        }
    }
}
