using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SkalTavern
{
    /// <summary>
    /// A "skal" command for the Claude Tools mod (its request mailbox), so an AI assistant can try the drinks while testing: how drunk you are,
    /// drinking one, and dropping one the way the game does it. Found and registered while the game runs (no reference between the mods).
    /// </summary>
    internal static class Tools
    {
        private static BaseUnityPlugin _claudeTools;
        private static float _next;

        public static void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5f;
            BaseUnityPlugin found = Chainloader.PluginInfos.TryGetValue("com.dhack.claudetools", out PluginInfo info) ? info.Instance : null;
            if (found == _claudeTools) return;
            _claudeTools = found;
            if (found == null) return;
            try
            {
                MethodInfo register = found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static);
                register?.Invoke(null, new object[] { Plugin.Name, "skal", "skal status | level <n> | puke | find <text> | drop [drink] | drink [drink] | sober: how drunk the player is; drop a drink from the bag as the game does and say what came of it; drink one; sober up",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Command });
                Plugin.Log.LogInfo("Claude Tools found: skal command added");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Could not add the skal command to Claude Tools: " + e.Message); }
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
            string want = args.Length > 1 ? args[1] : null;

            if (sub == "sober") { Tipsy.Clear(); output(new JObject { ["level"] = 0 }); yield break; }
            if (sub == "level")
            {
                float n = args.Length > 1 && float.TryParse(args[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
                Tipsy.Level = Mathf.Clamp(n, 0f, 130f);
                Tipsy.Drink(0f);   // (makes sure the Tipsy effect is on)
                output(new JObject { ["level"] = Tipsy.Level, ["stage"] = Tipsy.Stage(Tipsy.Level) });
                yield break;
            }
            if (sub == "puke") { Tipsy.Puke(player); output(new JObject { ["level"] = Tipsy.Level }); yield break; }
            if (sub == "se")
            {
                StatusEffect effect = ObjectDB.instance.m_StatusEffects.FirstOrDefault(e => e != null && string.Equals(e.name, args.Length > 1 ? args[1] : "", StringComparison.OrdinalIgnoreCase));
                if (effect == null) { error("no status effect of that name"); yield break; }
                var info = new JObject { ["type"] = effect.GetType().Name, ["name"] = effect.m_name, ["ttl"] = effect.m_ttl, ["tooltip"] = effect.m_tooltip,
                                         ["startEffects"] = new JArray(effect.m_startEffects.m_effectPrefabs.Select(p => p.m_prefab != null ? p.m_prefab.name : "-")),
                                         ["stopEffects"] = new JArray(effect.m_stopEffects.m_effectPrefabs.Select(p => p.m_prefab != null ? p.m_prefab.name : "-")) };
                if (effect is SE_Stats stats) { info["staminaRegen"] = stats.m_staminaRegenMultiplier; info["healthRegen"] = stats.m_healthRegenMultiplier; info["staminaOverTime"] = stats.m_staminaOverTime; info["healthOverTime"] = stats.m_healthOverTime; info["runDrain"] = stats.m_runStaminaDrainModifier; }
                output(info);
                yield break;
            }
            if (sub == "find")
            {
                // what the game has under a name: its objects, effects and the player's animations (for finding a vomit effect and the like)
                string text = (args.Length > 1 ? args[1] : "").ToLowerInvariant();
                var prefabs = ZNetScene.instance.m_prefabs.Where(g => g != null && g.name.ToLowerInvariant().Contains(text)).Select(g => g.name).Take(60).ToList();
                var effects = ObjectDB.instance.m_StatusEffects.Where(e => e != null && e.name.ToLowerInvariant().Contains(text)).Select(e => e.name).Take(40).ToList();
                var animator = player.GetComponentInChildren<Animator>();
                var anims = animator != null ? animator.parameters.Where(p => p.name.ToLowerInvariant().Contains(text)).Select(p => p.name).Take(40).ToList() : new System.Collections.Generic.List<string>();
                output(new JObject { ["prefabs"] = new JArray(prefabs), ["statusEffects"] = new JArray(effects), ["animations"] = new JArray(anims) });
                yield break;
            }
            if (sub == "status")
            {
                output(new JObject { ["level"] = Tipsy.Level, ["stage"] = Tipsy.Stage(Tipsy.Level), ["effects"] = new JArray(player.GetSEMan().GetStatusEffects().Select(e => e.name)) });
                yield break;
            }

            ItemDrop.ItemData item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("dh_drink_") && (want == null || i.m_dropPrefab.name.Contains(want)));
            if (item == null) { error("no drink of ours in the bag" + (want != null ? " matching " + want : "")); yield break; }

            if (sub == "drink")
            {
                player.UseItem(player.GetInventory(), item, true);
                yield return new WaitForSeconds(2f);
                output(new JObject { ["drank"] = item.m_dropPrefab.name, ["level"] = Tipsy.Level, ["stage"] = Tipsy.Stage(Tipsy.Level) });
                yield break;
            }

            if (sub == "drop")
            {
                string prefab = item.m_dropPrefab.name;
                int before = player.GetInventory().CountItems(item.m_shared.m_name);
                Vector3 at = player.transform.position;
                bool ok = player.DropItem(player.GetInventory(), item, 1);   // (the game's own drop, as dragging it out of the bag does)
                yield return new WaitForSeconds(0.8f);
                ItemDrop dropped = FindNear(at, 4f, item.m_shared.m_name);
                ZNetView view = dropped != null ? dropped.GetComponent<ZNetView>() : null;
                int hash = prefab.GetStableHashCode();
                var result = new JObject
                {
                    ["drink"] = prefab,
                    ["dropReturned"] = ok,
                    ["bagBefore"] = before,
                    ["bagAfter"] = player.GetInventory().CountItems(item.m_shared.m_name),
                    ["foundOnGround"] = dropped != null,
                    ["groundName"] = dropped != null ? dropped.m_itemData.m_shared.m_name : null,
                    ["groundNetworked"] = view != null && view.IsValid(),
                    ["savedAsPrefab"] = view != null && view.IsValid() ? (view.GetZDO().GetPrefab() == hash) : (bool?)null,
                    ["sceneKnowsIt"] = ZNetScene.instance != null && ZNetScene.instance.GetPrefab(hash) != null,
                    ["scenePrefabIsOurs"] = ZNetScene.instance != null && ZNetScene.instance.GetPrefab(hash) == item.m_dropPrefab,
                };
                output(result);
                yield break;
            }
            error("unknown subcommand " + sub);
        }

        private static ItemDrop FindNear(Vector3 at, float range, string sharedName) =>
            UnityEngine.Object.FindObjectsOfType<ItemDrop>().Where(d => d != null && d.m_itemData.m_shared.m_name == sharedName && Vector3.Distance(d.transform.position, at) < range)
                .OrderBy(d => Vector3.Distance(d.transform.position, at)).FirstOrDefault();
    }
}
